using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Mvvm;
using IrodoriTtsYwk.Launcher.Services.Ledger;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>初回取得の段（設計書 §6・受け入れ条件 D-5）。列挙の順＝進行の順。</summary>
public enum FirstRunStep
{
    /// <summary>通知（<c>licenses/first-run-notices.md</c> をそのまま出す＝裁定 46）。</summary>
    Notices,

    /// <summary>変種の選択（ドライバ検出で勧める・<b>自動切替はしない</b>＝裁定 4）。</summary>
    Variant,

    /// <summary>取得（台帳順・並列 2 本まで・進捗・中断／再開）。</summary>
    Download,

    /// <summary>展開（wheel＝zip・sdist＝tar・<c>._pth</c>）。</summary>
    Install,

    /// <summary>モデル（<c>ywk_fetch_models.py</c> の進捗 JSON 行）。</summary>
    Models,

    /// <summary>起こして <c>/health</c> まで。</summary>
    Start,

    /// <summary>完了（試し撃ちへ誘導）。</summary>
    Done,
}

/// <summary>
/// 初回取得ウィザード（設計書 §6・受け入れ条件 D-5＝利用者操作 ≤ 6）。
/// <para>
/// <b>取得の順は台帳が決める</b>＝vc_redist → python-embed → runtime-&lt;変種&gt; → models
/// （設計書 §6）。sha256 は全檔・不一致は破棄して最大 5 回・<c>url</c> で落ちたら
/// <c>fallback_url</c>（<see cref="IDownloader"/> の約束）。ここはその段を<b>並べて見せる</b>
/// だけで、取り方そのものは持たない。
/// </para>
/// <para>
/// <b>実装がまだ差さっていなくても進める</b>＝<see cref="IDownloader"/>／
/// <see cref="IRuntimeInstaller"/> が null の段は「実装待ち」と告げて止まる（黙って成功した
/// ことにしない）。
/// </para>
/// </summary>
public sealed class FirstRunViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private readonly LauncherSettings _settings;
    private readonly ISettingsStore _store;
    private readonly IDriverCheck _driverCheck;
    private readonly Func<IDownloader?> _downloader;
    private readonly Func<IRuntimeInstaller?> _installer;
    private readonly Func<Task<bool>> _startServer;

    private CancellationTokenSource? _cancel;
    private FirstRunStep _step = FirstRunStep.Notices;
    private string _noticesText = string.Empty;
    private string? _noticesSha256;
    private bool _accepted;
    private string _variant;
    private string _message = string.Empty;
    private string _progressText = UiText.Missing;
    private double _progressFraction;
    private bool _isBusy;
    private string _sizeText = UiText.Missing;
    private string _driverText = UiText.Missing;
    private bool _lastStepOk = true;
    private bool _skipVcRedist;

    public FirstRunViewModel(
        AppPaths paths,
        LauncherSettings settings,
        ISettingsStore store,
        IDriverCheck driverCheck,
        Func<IDownloader?> downloader,
        Func<IRuntimeInstaller?> installer,
        Func<Task<bool>> startServer)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(driverCheck);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(startServer);

        _paths = paths;
        _settings = settings;
        _store = store;
        _driverCheck = driverCheck;
        _downloader = downloader;
        _installer = installer;
        _startServer = startServer;

        var ledgerNames = ReleaseFlavors.LedgerNames(paths.LedgerDir);
        Flavor = ReleaseFlavors.Detect(ledgerNames);
        VariantChoices = ReleaseFlavors.AvailableChoices(Flavor, ledgerNames);
        _variant = VariantChoices.Contains(settings.Variant, StringComparer.Ordinal)
            ? settings.Variant
            : VariantChoices[0];

        NextCommand = new AsyncRelayCommand(NextAsync, CanGoNext);
        BackCommand = new RelayCommand(Back, () => Step > FirstRunStep.Notices && !IsBusy);
        CancelCommand = new RelayCommand(CancelRunning, () => IsBusy);

        LoadNotices();
        UpdateVariantNotes();
    }

    public ReleaseFlavor Flavor { get; }

    public IReadOnlyList<string> VariantChoices { get; }

    /// <summary>いま済んだ段の記録（画面の左に並べる）。</summary>
    public ObservableCollection<string> Trail { get; } = [];

    public AsyncRelayCommand NextCommand { get; }

    public RelayCommand BackCommand { get; }

    public RelayCommand CancelCommand { get; }

    public FirstRunStep Step
    {
        get => _step;
        private set
        {
            if (!SetProperty(ref _step, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(StepTitle));
            RaisePropertyChanged(nameof(StepNumberText));
            RaisePropertyChanged(nameof(NextButtonText));
            RaisePropertyChanged(nameof(IsNoticesStep));
            RaisePropertyChanged(nameof(IsVariantStep));
            RaisePropertyChanged(nameof(IsWorkStep));
            RaisePropertyChanged(nameof(IsDoneStep));
            NextCommand.RaiseCanExecuteChanged();
            BackCommand.RaiseCanExecuteChanged();
        }
    }

    public static string Title(FirstRunStep step) => step switch
    {
        FirstRunStep.Notices => "初回取得の前に（第三者物の通知）",
        FirstRunStep.Variant => "実行系の種類を選ぶ",
        FirstRunStep.Download => "取得（実行系）",
        FirstRunStep.Install => "展開",
        FirstRunStep.Models => "取得（モデル）",
        FirstRunStep.Start => "起動の確認",
        FirstRunStep.Done => "完了",
        _ => step.ToString(),
    };

    public string StepTitle => Title(Step);

    public string StepNumberText =>
        (((int)Step) + 1).ToString(CultureInfo.InvariantCulture) + " / "
        + (((int)FirstRunStep.Done) + 1).ToString(CultureInfo.InvariantCulture);

    public string NextButtonText => !_lastStepOk
        ? "やり直す"
        : Step switch
        {
            FirstRunStep.Notices => "同意して次へ",
            FirstRunStep.Variant => "この構成で取得を始める",
            FirstRunStep.Done => "試し撃ちへ",
            _ => "次へ",
        };

    /// <summary>直前の段が成功したか（偽なら「次へ」は「やり直す」になる）。</summary>
    public bool LastStepOk => _lastStepOk;

    /// <summary>
    /// 通知文を実際に読み込めたか（＝同意の証跡 <c>acceptedNoticesSha256</c> を残せるか）。
    /// <b>偽の間は同意できない</b>（裁定 46＝ランチャは初回取得前にこの檔を表示する）。
    /// </summary>
    public bool CanAcceptNotices => _noticesSha256 is not null;

    public bool IsNoticesStep => Step is FirstRunStep.Notices;

    public bool IsVariantStep => Step is FirstRunStep.Variant;

    public bool IsWorkStep => Step is FirstRunStep.Download or FirstRunStep.Install
        or FirstRunStep.Models or FirstRunStep.Start;

    public bool IsDoneStep => Step is FirstRunStep.Done;

    /// <summary>通知文（<b>そのまま出す</b>＝席が要約しない＝裁定 46）。</summary>
    public string NoticesText
    {
        get => _noticesText;
        private set => SetProperty(ref _noticesText, value);
    }

    /// <summary>同意した通知文の sha256（設定に残す＝文面が変わったら取り直す）。</summary>
    public string? NoticesSha256 => _noticesSha256;

    public bool Accepted
    {
        get => _accepted;
        set
        {
            if (SetProperty(ref _accepted, value))
            {
                NextCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Variant
    {
        get => _variant;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !SetProperty(ref _variant, value))
            {
                return;
            }

            UpdateVariantNotes();
        }
    }

    public string VariantDisplayName => RuntimeVariants.DisplayName(_variant);

    /// <summary>CPU 変種の注記（裁定 13＝「遅い」を明記する）。</summary>
    public string VariantNote => RuntimeVariants.IsCpu(_variant)
        ? "CPU は GPU の数百分の一の速さです。配信用途では勧めません（試すためだけの選択肢です）。"
        : RuntimeVariants.IsRocm(_variant)
            ? "Radeon（ROCm）版です。精度は bf16 に固定されます（未保障・gfx1151 で確認済み）。"
            : "NVIDIA の GPU で動きます。ドライバの版が下限に届いているか下の行を確かめてください。";

    /// <summary>取得の見積り（台帳の <c>size</c> の総和＋モデル）。</summary>
    public string SizeText
    {
        get => _sizeText;
        private set => SetProperty(ref _sizeText, value);
    }

    /// <summary>ドライバ検査の 1 行（勧めるだけ・自動切替はしない＝裁定 4）。</summary>
    public string DriverText
    {
        get => _driverText;
        private set => SetProperty(ref _driverText, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>取得の進捗の 1 行（bytes／ETA＝受け入れ条件 D-5）。</summary>
    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    /// <summary>0〜1（分母が無ければ 0）。</summary>
    public double ProgressFraction
    {
        get => _progressFraction;
        private set => SetProperty(ref _progressFraction, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NextCommand.RaiseCanExecuteChanged();
                BackCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 段を 1 つ進める。
    /// <para>
    /// <b>失敗した段からは進まない</b>（是正・2026-09-05）。以前は段の遷移を先に行い、
    /// <c>RunXxxAsync</c> の結果を 1 度も見なかったので、1 檔も落とせていない機体でも
    /// 「次へ」を押し続ければ最後まで通り、<c>FirstRunCompleted=true</c> が無条件に焼かれた
    /// （＝次の起動からウィザードが出なくなる）。いまは直前の段が成功したときだけ進み、
    /// 失敗した段では同じ段を「やり直す」。
    /// </para>
    /// </summary>
    public async Task NextAsync()
    {
        // 失敗した段は、進まずに同じ段をもう 1 度走らせる。
        if (!_lastStepOk)
        {
            await RunStepAsync(Step).ConfigureAwait(true);
            return;
        }

        switch (Step)
        {
            case FirstRunStep.Notices:
                if (_noticesSha256 is null)
                {
                    Message = "通知文を読み込めていないので同意できません（配布物を確かめてください）。";
                    return;
                }

                _settings.AcceptedNoticesSha256 = _noticesSha256;
                Record("通知に同意しました。");
                Step = FirstRunStep.Variant;
                break;

            case FirstRunStep.Variant:
                _settings.Variant = _variant;
                _store.Save(_settings);
                Record("変種＝" + RuntimeVariants.DisplayName(_variant) + " を選びました。");
                Step = FirstRunStep.Download;
                await RunStepAsync(FirstRunStep.Download).ConfigureAwait(true);
                break;

            case FirstRunStep.Download:
                Step = FirstRunStep.Install;
                await RunStepAsync(FirstRunStep.Install).ConfigureAwait(true);
                break;

            case FirstRunStep.Install:
                Step = FirstRunStep.Models;
                await RunStepAsync(FirstRunStep.Models).ConfigureAwait(true);
                break;

            case FirstRunStep.Models:
                Step = FirstRunStep.Start;
                await RunStepAsync(FirstRunStep.Start).ConfigureAwait(true);
                break;

            case FirstRunStep.Start:
                _settings.FirstRunCompleted = true;
                _store.Save(_settings);
                Record("初回取得が終わりました。");
                Step = FirstRunStep.Done;
                break;

            case FirstRunStep.Done:
            default:
                Completed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    /// <summary>その段の仕事を 1 回走らせ、成否を <see cref="LastStepOk"/> に立てる。</summary>
    private async Task RunStepAsync(FirstRunStep step)
    {
        var ok = step switch
        {
            FirstRunStep.Download => await RunDownloadAsync().ConfigureAwait(true),
            FirstRunStep.Install => await RunInstallAsync().ConfigureAwait(true),
            FirstRunStep.Models => await RunModelsAsync().ConfigureAwait(true),
            FirstRunStep.Start => await RunStartAsync().ConfigureAwait(true),
            _ => true,
        };

        SetStepOk(ok);
    }

    private void SetStepOk(bool ok)
    {
        if (_lastStepOk == ok)
        {
            return;
        }

        _lastStepOk = ok;
        RaisePropertyChanged(nameof(LastStepOk));
        RaisePropertyChanged(nameof(NextButtonText));
    }

    /// <summary>ウィザードを閉じてよい合図（窓が受ける）。</summary>
    public event EventHandler? Completed;

    public void Back()
    {
        if (Step > FirstRunStep.Notices && !IsBusy)
        {
            Step = (FirstRunStep)((int)Step - 1);
        }
    }

    /// <summary>走っている取得を止める（<c>.part</c> は残るので次回は続きから）。</summary>
    public void CancelRunning()
    {
        _cancel?.Cancel();
        Message = "中断しました（続きから取り直せます）。";
    }

    /// <summary>
    /// 「次へ」を押せるか。通知の段は<b>通知文を読み込めていて</b>、かつ同意に印が要る
    /// （裁定 46＝檔が無ければ先へ進めない・<c>acceptedNoticesSha256</c> に null を残さない）。
    /// </summary>
    private bool CanGoNext() =>
        !IsBusy && (Step is not FirstRunStep.Notices || (Accepted && CanAcceptNotices));

    private void Record(string line)
    {
        Trail.Add(line);
        Message = line;
    }

    private void LoadNotices()
    {
        try
        {
            if (!File.Exists(_paths.FirstRunNoticesPath))
            {
                NoticesText = "通知文（licenses/first-run-notices.md）が配布物に見つかりません。"
                    + "この状態では初回取得を始められません。";
                return;
            }

            var bytes = File.ReadAllBytes(_paths.FirstRunNoticesPath);
            NoticesText = new UTF8Encoding(false).GetString(bytes);
            _noticesSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            RaisePropertyChanged(nameof(NoticesSha256));
        }
        catch (IOException ex)
        {
            NoticesText = "通知文が読めませんでした：" + ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            NoticesText = "通知文が読めませんでした：" + ex.Message;
        }
        finally
        {
            RaisePropertyChanged(nameof(CanAcceptNotices));
            NextCommand.RaiseCanExecuteChanged();
        }
    }

    private void UpdateVariantNotes()
    {
        RaisePropertyChanged(nameof(VariantDisplayName));
        RaisePropertyChanged(nameof(VariantNote));

        var verdict = _driverCheck.Check(_variant, null);
        DriverText = verdict.Message;
        SizeText = EstimateSizeText(_paths, _variant, _skipVcRedist);
    }

    /// <summary>
    /// 取得の見積り（<b><see cref="FetchPlanner"/> の計画そのもの</b>＝席が数字を作らない）。
    /// <para>
    /// <b>見せる数と落とす物を同じ 1 つの計画から作る</b>（是正・2026-09-05）。以前は
    /// 台帳 2 檔の <c>TotalBytes</c> を足すだけで、実際に注文へ足す vc_redist と
    /// python-embed（合わせて 35 MiB）が数に入っておらず、<b>必要な空き容量は 1 度も出なかった</b>
    /// （rocm 版で 9.55 GiB）。展開の途中で容量切れに当たる利用者を作らない。
    /// </para>
    /// 台帳が読めなければ「不明」と名乗る（推測の数字を出さない）。
    /// </summary>
    public static string EstimateSizeText(AppPaths paths, string variant, bool skipVcRedist = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        var plan = TryPlan(paths, variant, skipVcRedist, out var reason);
        if (plan is null)
        {
            // **理由を添える**（是正・便 D（2））＝low 6 で `FetchPlanner` が sha256 の無い item を
            // 投げて弾くようになったのに、ここは「読めない」としか言えなかった（実際は
            // 例外がそのまま UI スレッドへ抜けていた＝下の TryPlan の註）。
            return reason is null
                ? "不明（取得台帳が読めません）"
                : "不明（取得台帳が読めません：" + reason + "）";
        }

        var runtime = plan.Steps
            .Where(static s => s.Stage is FetchStage.PythonEmbed or FetchStage.Runtime)
            .Sum(static s => s.Bytes);
        var models = plan.ModelBytes;
        var vc = plan.Steps.Where(static s => s.Stage is FetchStage.VcRedist).Sum(static s => s.Bytes);

        var text = plan.Summary()
            + "（実行系 " + FetchPlanner.FormatBytes(runtime)
            + "・モデル " + FetchPlanner.FormatBytes(models);
        if (vc > 0)
        {
            text += "・vc_redist " + FetchPlanner.FormatBytes(vc);
        }

        return text + "）";
    }

    /// <summary>
    /// 台帳 4 種から計画を組む（読めない台帳が 1 つでもあれば null）。
    /// <b>ここが唯一の計画の作り口</b>＝見積りも注文もこれを通す。
    /// </summary>
    public static FetchPlan? TryPlan(AppPaths paths, string variant, bool skipVcRedist) =>
        TryPlan(paths, variant, skipVcRedist, out _);

    /// <summary>
    /// 同上＋読めなかった理由 1 行（読めたなら null）。
    /// <para>
    /// <b>投げない</b>（是正・便 D（2））＝檔頭の約束は「読めない台帳が 1 つでもあれば null」だが、
    /// low 6 の直し（<c>FetchPlanner.RejectItemsWithoutSha256</c> が <see cref="LedgerException"/> を
    /// 投げる）を素通しにしていた。ここは <see cref="UpdateVariantNotes"/> → 構築時と
    /// <see cref="Variant"/> の setter＝<b>UI スレッドの束縛経路</b>から呼ばれ、
    /// <c>App.xaml.cs</c> に <c>DispatcherUnhandledException</c> の受け口は無く、
    /// <c>AsyncRelayCommand</c> も <see cref="LedgerException"/> を捕らない
    /// ＝台帳が 1 件でも欠けた日に<b>窓ごと落ちる</b>（実射＝sha256 の無い item 1 件で再現）。
    /// </para>
    /// </summary>
    public static FetchPlan? TryPlan(
        AppPaths paths, string variant, bool skipVcRedist, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        failureReason = null;

        var embed = ReadLedgerFile<LedgerFile>(paths.LedgerPath(LedgerFileNames.PythonEmbed));
        var runtime = ReadLedgerFile<LedgerFile>(paths.LedgerPath(RuntimeVariants.LedgerName(variant)));
        if (embed is null || runtime is null)
        {
            failureReason = embed is null
                ? LedgerFileNames.PythonEmbed + ".json が読めません"
                : RuntimeVariants.LedgerName(variant) + ".json が読めません";
            return null;
        }

        try
        {
            return FetchPlanner.Plan(
                variant,
                embed,
                runtime,
                ReadLedgerFile<VcRedistLedger>(paths.LedgerPath(LedgerFileNames.VcRedist)),
                ReadLedgerFile<ModelsLedger>(paths.LedgerPath(LedgerFileNames.Models)),
                new FetchPlanOptions(SkipVcRedist: skipVcRedist));
        }
        catch (LedgerException ex)
        {
            failureReason = ex.Message;
            return null;
        }
    }

    /// <summary>台帳 1 檔を読む（無い・壊れているなら null＝黙って既定に落ちない）。</summary>
    public static T? ReadLedgerFile<T>(string path)
        where T : class
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonSettingsStore.JsonOptions)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// vc_redist を通す手（判定 → 要れば取得 → sha256 → UAC 昇格で silent 実行）。
    /// 差さっていなければ<b>飛ばさずに告げる</b>（<c>msvcp140.dll</c> が無い機体で
    /// <c>torch/lib/c10.dll</c> が読めなくなるのを黙って見過ごさない）。
    /// <para>
    /// 第 1 引数＝<b>「判らない」でも入れる</b>（利用者が下の <see cref="AskVcRedist"/> で
    /// 「入れる」と答えた）。既定の 1 巡目は必ず偽で撃つ＝勝手に UAC を出さない（裁定 87 ⑷）。
    /// </para>
    /// </summary>
    public Func<bool, IProgress<DownloadProgress>, CancellationToken, Task<VcRedistResult?>>? VcRedistRunner
    {
        get;
        set;
    }

    /// <summary>
    /// <b>「入れる／飛ばす」を利用者に問う手</b>（裁定 87 ⑷・是正・便 D（2））。
    /// <para>
    /// 真＝入れる・偽＝飛ばす・<c>null</c>＝答えなかった（＝先へ進めない）。
    /// 窓（<c>Views/FirstRunWizard</c>）が差す＝ViewModel は WPF の型に触れない（§12-2 ⑴）。
    /// <b>差さっていなければ問わずに止まる</b>（1 巡目の挙動）＝
    /// <see cref="VcRedistResult.NeedsUserDecision"/> は定義されていたのに
    /// <b>launcher/ の中でどこからも読まれておらず</b>、System32 が読めない機体では
    /// 初回取得が先へ進めなかった（飛ばす口も入れる口も無かった）。
    /// </para>
    /// </summary>
    public Func<string, bool?>? AskVcRedist { get; set; }

    private async Task<bool> RunDownloadAsync()
    {
        var downloader = _downloader();
        if (downloader is null)
        {
            Message = "取得系がまだ組み込まれていません（便 D・取得席の実装待ち）。";
            return false;
        }

        IsBusy = true;
        _cancel = new CancellationTokenSource();
        try
        {
            var progress = new Progress<DownloadProgress>(OnDownloadProgress);

            // ⑴ vc_redist＝**判定が先**（在る機体では 1 バイトも落とさない）。
            //    落とした exe を走らせるのもここ＝取得だけして放置しない。
            if (!await RunVcRedistAsync(progress, _cancel.Token).ConfigureAwait(true))
            {
                return false;
            }

            // ⑵ python-embed → runtime-<変種>（vc_redist は判定の結果で計画から外れる）。
            var plan = TryPlan(_paths, _variant, _skipVcRedist, out var planReason);
            if (plan is null)
            {
                Message = "取得台帳が読めないので取得を始められません"
                    + (planReason is null ? "。" : "（" + planReason + "）。");
                return false;
            }

            SizeText = EstimateSizeText(_paths, _variant, _skipVcRedist);

            var requests = FetchPlanner.ToDownloadRequests(plan, _paths.DownloadCacheDir);
            if (requests.Count == 0)
            {
                Message = "取得台帳が読めないので取得を始められません。";
                return false;
            }

            var results = await downloader
                .DownloadAllAsync(requests, progress, _cancel.Token)
                .ConfigureAwait(true);

            var failed = results.FirstOrDefault(static r => !r.Ok);
            if (failed is not null)
            {
                Message = "取得に失敗しました：" + (failed.FailureReason ?? "理由が分かりません。");
                return false;
            }

            Record("実行系を取得しました（" + results.Count.ToString(CultureInfo.InvariantCulture) + " 件）。");
            return true;
        }
        catch (OperationCanceledException)
        {
            Message = "中断しました（続きから取り直せます）。";
            return false;
        }
        finally
        {
            _cancel?.Dispose();
            _cancel = null;
            IsBusy = false;
        }
    }

    private async Task<bool> RunVcRedistAsync(
        IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        if (VcRedistRunner is null)
        {
            // 台帳が無い配布（Radeon 版の一部）なら黙って飛ばしてよいが、
            // 台帳が在るのに手が差さっていないのは実装の穴なので告げる。
            if (ReadLedgerFile<VcRedistLedger>(_paths.LedgerPath(LedgerFileNames.VcRedist))?.Installer is null)
            {
                _skipVcRedist = true;
                return true;
            }

            Message = "vc_redist の導入系がまだ組み込まれていません（便 D・取得席の実装待ち）。";
            return false;
        }

        ProgressText = "Visual C++ 再頒布可能パッケージを確かめています…";
        var result = await VcRedistRunner(false, progress, cancellationToken).ConfigureAwait(true);
        if (result is null)
        {
            _skipVcRedist = true;
            return true; // 台帳が無い＝この段は無い
        }

        // 入った・飛ばした、のどちらでも「もう落とさなくてよい」＝計画から外す。
        _skipVcRedist = true;

        // **判らないときは利用者に問う**（裁定 87 ⑷）＝勝手に入れない・黙って止まらない。
        if (result.NeedsUserDecision)
        {
            Record(result.Message);
            var answer = AskVcRedist?.Invoke(result.Message);
            if (answer == false)
            {
                Record("Visual C++ 再頒布可能パッケージの導入を飛ばしました（利用者の選択）。"
                    + "torch の読み込みで msvcp140.dll が見つからないと出たら、"
                    + "Microsoft の再頒布可能パッケージを手で入れてください。");
                return true;
            }

            if (answer is null)
            {
                // 問う口が無い（または利用者が答えなかった）＝1 巡目と同じ「止まる」。
                Message = result.Message;
                return false;
            }

            ProgressText = "Visual C++ 再頒布可能パッケージを入れています…";
            result = await VcRedistRunner(true, progress, cancellationToken).ConfigureAwait(true)
                     ?? result;
        }

        Record(result.Message);

        if (!result.Ok)
        {
            Message = result.Message;
        }

        return result.Ok;
    }

    private void OnDownloadProgress(DownloadProgress progress)
    {
        ProgressFraction = progress.Fraction ?? 0;
        ProgressText = Describe(progress);
    }

    /// <summary>進捗の 1 行（<b>純関数</b>＝受け入れ条件 D-5 の「bytes／ETA」）。</summary>
    public static string Describe(DownloadProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var phase = progress.Phase switch
        {
            DownloadPhase.Pending => "待機",
            DownloadPhase.Connecting => "接続",
            DownloadPhase.Downloading => "取得",
            DownloadPhase.Verifying => "検証",
            DownloadPhase.Committing => "着地",
            DownloadPhase.CacheHit => "取得済み",
            DownloadPhase.Done => "完了",
            DownloadPhase.Failed => "失敗",
            _ => progress.Phase.ToString(),
        };

        var text = phase + "：" + progress.DisplayName
            + "　" + UiText.Bytes(progress.BytesReceived)
            + " / " + UiText.Bytes(progress.TotalBytes);

        if (progress.Phase is DownloadPhase.Downloading)
        {
            text += "　" + UiText.Rate(progress.BytesPerSecond) + "　残り " + UiText.Eta(progress.Eta);
        }

        if (progress.Attempt > 1)
        {
            text += "　（" + progress.Attempt.ToString(CultureInfo.InvariantCulture) + " 回目）";
        }

        return text;
    }

    private async Task<bool> RunInstallAsync()
    {
        var installer = _installer();
        var ledger = ReadLedgerFile<LedgerFile>(_paths.LedgerPath(RuntimeVariants.LedgerName(_variant)));
        if (installer is null || ledger is null)
        {
            Message = installer is null
                ? "展開系がまだ組み込まれていません（便 D・取得席の実装待ち）。"
                : "取得台帳が読めないので展開できません。";
            return false;
        }

        var runtimeDir = Path.Combine(_paths.RuntimeRoot, _variant);
        IsBusy = true;
        _cancel = new CancellationTokenSource();
        try
        {
            var progress = new Progress<InstallProgress>(p =>
            {
                ProgressFraction = p.Total > 0 ? Math.Clamp((double)p.Done / p.Total, 0, 1) : 0;
                ProgressText = p.Phase + "：" + (p.ItemName ?? string.Empty)
                    + "　" + UiText.Progress(p.Done, p.Total);
            });

            var result = await installer
                .InstallAsync(
                    new InstallRequest(ledger, _paths.DownloadCacheDir, runtimeDir, _paths.AppDir, _paths.PthTemplatePath),
                    progress,
                    _cancel.Token)
                .ConfigureAwait(true);

            if (!result.Ok)
            {
                Message = "展開に失敗しました：" + (result.FailureReason ?? "理由が分かりません。");
                return false;
            }

            Record("展開しました（" + result.Files.ToString(CultureInfo.InvariantCulture) + " 檔・"
                + UiText.Bytes(result.Bytes) + "）。");
            return true;
        }
        catch (OperationCanceledException)
        {
            Message = "中断しました。";
            return false;
        }
        finally
        {
            _cancel?.Dispose();
            _cancel = null;
            IsBusy = false;
        }
    }

    /// <summary>
    /// モデルの取得（<c>server/ywk_fetch_models.py</c> を変種の python で走らせる＝設計書 §6）。
    /// 差し替えの口を開けてあるのは、子プロセスの扱いが取得席の管掌だからである。
    /// </summary>
    public Func<IProgress<string>, CancellationToken, Task<bool>>? ModelFetcher { get; set; }

    private async Task<bool> RunModelsAsync()
    {
        if (ModelFetcher is null)
        {
            Message = "モデルの取得系がまだ組み込まれていません（便 D・取得席の実装待ち）。";
            return false;
        }

        IsBusy = true;
        _cancel = new CancellationTokenSource();
        try
        {
            var progress = new Progress<string>(line => ProgressText = line);
            var ok = await ModelFetcher(progress, _cancel.Token).ConfigureAwait(true);
            if (ok)
            {
                Record("モデルを取得しました。");
            }
            else
            {
                Message = "モデルの取得に失敗しました。";
            }

            return ok;
        }
        catch (OperationCanceledException)
        {
            Message = "中断しました。";
            return false;
        }
        finally
        {
            _cancel?.Dispose();
            _cancel = null;
            IsBusy = false;
        }
    }

    private async Task<bool> RunStartAsync()
    {
        IsBusy = true;
        try
        {
            ProgressText = "サーバを起こしています…";
            var ok = await _startServer().ConfigureAwait(true);
            if (ok)
            {
                Record("サーバが起動しました。");
            }
            else
            {
                Message = "サーバを起こせませんでした（状態タブの理由を見てください）。";
            }

            return ok;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
