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
using IrodoriTtsYwk.Launcher.Services.Gpu;
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

    /// <summary>完了（「発話テスト」へ誘導）。</summary>
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
    private readonly Func<CancellationToken, Task<bool>> _startServer;

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

    /// <summary>ドライバの検分（裁定 126 の B）。既定＝まだ何も見ていない。</summary>
    private DriverProbe _probe = DriverProbe.Unknown;

    /// <summary>利用者が変種を<b>自分で選んだ</b>か（真なら勧めで上書きしない）。</summary>
    private bool _variantChosen;

    public FirstRunViewModel(
        AppPaths paths,
        LauncherSettings settings,
        ISettingsStore store,
        IDriverCheck driverCheck,
        Func<IDownloader?> downloader,
        Func<IRuntimeInstaller?> installer,
        Func<CancellationToken, Task<bool>> startServer)
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

        // 捕れなかった例外を握り潰さない（§20-5 ⑴）＝黙って止まったウィザードを作らない。
        NextCommand.Faulted += (_, line) =>
        {
            SetStepOk(false);
            Message = "この段で予期しない失敗が起きました：" + line;
        };

        LoadNotices();
        UpdateVariantNotes();
    }

    /// <summary>
    /// <b>ドライバの検分の手</b>（裁定 126 の B）＝<c>nvidia-smi</c>／torch 列挙を 1 度撃って
    /// <see cref="DriverProbe"/> を返す。null＝撃たない（試験・列挙系が差さっていない配布）。
    /// <para>
    /// 窓が <see cref="RefreshDriverAsync"/> を呼ぶ。ここが <see cref="IGpuEnumerator"/> を
    /// 名前で持たないのは、ViewModel を起動席の檔に縛らないため（<see cref="ModelFetcher"/> と同じ形）。
    /// </para>
    /// </summary>
    public Func<CancellationToken, Task<DriverProbe>>? DriverProbeAsync { get; set; }

    /// <summary>
    /// ドライバを見て、<b>勧める変種を選び直す</b>（裁定 126 の B）。
    /// <para>
    /// <b>投げない</b>＝列挙が落ちても「判らない」（<see cref="DriverProbe.Unknown"/> のまま）で進む。
    /// <b>利用者が既に自分で選んでいれば上書きしない</b>（裁定 4＝自動切替はしない。勧めるのは
    /// <b>まだ選んでいない初期値</b>だけで、選び直す口は常に開いている）。
    /// </para>
    /// </summary>
    public async Task RefreshDriverAsync(CancellationToken cancellationToken = default)
    {
        if (DriverProbeAsync is not { } probe)
        {
            return;
        }

        // 主窓が渡した版（裁定 126 ⑽の Preselect）。こちらが読めなかった回に残す。
        var seeded = _probe.DriverVersion;

        try
        {
            _probe = await probe(cancellationToken).ConfigureAwait(true) ?? DriverProbe.Unknown;
        }
        catch (OperationCanceledException)
        {
            return;
        }
#pragma warning disable CA1031 // 列挙の失敗でウィザードを止めない（理由は下の 1 行に出る）
        catch (Exception)
#pragma warning restore CA1031
        {
            _probe = DriverProbe.Unknown;
        }

        // **渡された版を捨てない**（裁定 126 ⑽・是正・検分）＝ここの検分は主窓のより弱い
        // （実行系がまだ無い変種の python.exe しか渡せないので nvidia-smi の 1 本だけ）。
        // 期限切れ・nvidia-smi の一時の失敗で版が消えると <see cref="VariantRecommendation.Recommend"/>
        // は「判らない」に落ちて<b>並びの先頭＝断られた変種</b>を勧め直し、Trail の
        // 「CUDA 12.6 に切り替えて取得します。」と選びが食い違う（＝また数 GB 落として、
        // 次の起動でまた断られる）。**新しく読めた回はそちらが勝つ**＝上書きはしない。
        if (string.IsNullOrWhiteSpace(_probe.DriverVersion) && !string.IsNullOrWhiteSpace(seeded))
        {
            _probe = _probe with { DriverVersion = seeded };
        }

        if (!_variantChosen)
        {
            var recommended = VariantRecommendation.Recommend(VariantChoices, _probe);
            if (VariantChoices.Contains(recommended, StringComparer.Ordinal)
                && !string.Equals(recommended, _variant, StringComparison.Ordinal))
            {
                SetProperty(ref _variant, recommended, nameof(Variant));
            }
        }

        UpdateVariantNotes();
    }

    /// <summary>いま見えているドライバの版（読めていなければ null）。</summary>
    public string? DriverVersion => _probe.DriverVersion;

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

    /// <summary>
    /// 「次へ」の文言。
    /// <para>
    /// <b>働く段には「次へ」が無い</b>（裁定 94 ⑴）＝取得→展開→モデル→起動は自動で繋がるので、
    /// この文言が出るのは⑴ 通知 ⑵ 変種 ⑶ 完了 ⑷ <b>失敗した段（「もう一度」）</b>の 4 つだけである。
    /// </para>
    /// </summary>
    public string NextButtonText => !_lastStepOk
        ? "もう一度"
        : Step switch
        {
            FirstRunStep.Notices => "同意して次へ",
            FirstRunStep.Variant => "この構成で取得を始める",
            FirstRunStep.Done => "発話テストへ",
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

            // 利用者が自分で選んだ＝以後は勧めで上書きしない（裁定 4・126 の B）。
            _variantChosen = true;
            UpdateVariantNotes();
        }
    }

    /// <summary>
    /// <b>いま選んでいる変種はこのドライバでは動かない</b>（裁定 126 の B）＝
    /// 真の間は「この構成で取得を始める」を押せない（<see cref="CanGoNext"/>）。
    /// <c>cpu</c> はいつでも偽（GPU を見ない）。ドライバの版が読めない機体も偽＝止めない。
    /// </summary>
    public bool VariantBlocked => VariantBlockReason is not null;

    /// <summary>
    /// 押せない理由の 1 行（押せるなら null）。
    /// 例＝<c>このドライバ（537.58）では CUDA 13.0 は動きません（下限 580.00）。CUDA 12.6 を選んでください。</c>
    /// </summary>
    public string? VariantBlockReason =>
        VariantRecommendation.BlockReason(VariantChoices, _variant, _probe.DriverVersion);

    public string VariantDisplayName => RuntimeVariants.DisplayName(_variant);

    /// <summary>CPU 変種の注記（裁定 13＝「遅い」を明記する）。</summary>
    public string VariantNote => RuntimeVariants.IsCpu(_variant)
        ? "CPU は GPU の数百分の一の速さです。配信用途では勧めません（試すためだけの選択肢です）。"
        : RuntimeVariants.IsRocm(_variant)
            ? "ROCm 版です（Radeon の GPU 向け）。精度は bf16 に固定されます（未保障・gfx1151 で確認済み）。"
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

    /// <summary>
    /// いま画面に出ている 1 行。<b>変わった行はそのまま檔へも落ちる</b>
    /// （裁定 126 の C（1）＝<see cref="LogSink"/>）。<see cref="Record"/> もここを通る。
    /// 進捗（<see cref="ProgressText"/>）は 1 秒に何度も動くので落とさない。
    /// </summary>
    public string Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                Log(value);
            }
        }
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
    /// 「次へ」の 1 手。
    /// <para>
    /// <b>成功した段は自動で次へ進む</b>（裁定 94 ⑴）。押下は<b>5</b> だけになる＝
    /// 同意チェック・同意して次へ・変種・取得を始める・発話テストへ。取得→展開→モデル→起動は
    /// <see cref="AdvanceAsync"/> が繋ぎ、<b>失敗した段でだけ止まる</b>（そこで「もう一度」と理由 1 行）。
    /// 便 E（2）の E2E は 9 押下で、受け入れ条件の「利用者操作 ≤ 6」を落としていた
    /// （<c>docs/acceptance.md</c> 導入行・裁定 94 ⑴）。
    /// </para>
    /// <para>
    /// <b>失敗した段からは進まない</b>（是正・便 D（2））。以前は段の遷移を先に行い、
    /// <c>RunXxxAsync</c> の結果を 1 度も見なかったので、1 檔も落とせていない機体でも
    /// 「次へ」を押し続ければ最後まで通り、<c>FirstRunCompleted=true</c> が無条件に焼かれた。
    /// </para>
    /// </summary>
    public async Task NextAsync()
    {
        // **変種の門は何より先に見る**（是正・検分）＝下の「失敗した段をやり直す」枝は
        // `AdvanceAsync(Step)` へ直に入るので、変種の段で `NextCommand.Faulted` が立った状態
        // （例＝`_store.Save` が投げた）だと、下限未満の変種のまま取得へ抜けられた。
        if (Step is FirstRunStep.Variant && VariantBlockReason is string blockedFirst)
        {
            Message = blockedFirst;
            return;
        }

        // 失敗した段は、同じ段からもう 1 度走らせる（通れば続きも自動で進む）。
        if (!_lastStepOk)
        {
            await AdvanceAsync(Step).ConfigureAwait(true);
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
                // 門はこの手の頭でもう見た（束縛の外＝試験・台本から呼ばれても通さない＝裁定 126 の B）。
                _settings.Variant = _variant;
                _store.Save(_settings);
                Record("変種＝" + RuntimeVariants.DisplayName(_variant) + " を選びました。");
                await AdvanceAsync(FirstRunStep.Download).ConfigureAwait(true);
                break;

            case FirstRunStep.Done:
                Completed?.Invoke(this, EventArgs.Empty);
                break;

            default:
                // 働く段に「次へ」は無い（自動で進む）。押せてしまったら続きを繋ぐだけ。
                await AdvanceAsync(Step).ConfigureAwait(true);
                break;
        }
    }

    /// <summary>
    /// <paramref name="from"/> の段から<b>成功する限り自動で</b>進める（裁定 94 ⑴）。
    /// <para>
    /// 失敗（中断を含む）した段でそのまま止まる＝<see cref="LastStepOk"/> が偽になり、
    /// 「次へ」は「もう一度」に変わる。段の出入りは <see cref="Trail"/> に残すので、
    /// どこまで進んだかは自動でも画面から読める。
    /// </para>
    /// </summary>
    private async Task AdvanceAsync(FirstRunStep from)
    {
        var step = from;
        while (true)
        {
            Step = step;

            if (IsWorkStep)
            {
                // 同じ段を「もう一度」で撃ち直しても行は重ねない（是正・便 D（3）の 3 巡目）。
                var head = "― " + Title(step);
                if (Trail.Count == 0 || !string.Equals(Trail[^1], head, StringComparison.Ordinal))
                {
                    Trail.Add(head);
                }
            }

            if (!await RunStepAsync(step).ConfigureAwait(true))
            {
                return;
            }

            if (step is FirstRunStep.Start)
            {
                // 起動の確認まで通った＝初回取得は終わり（ここで初めて焼く）。
                _settings.FirstRunCompleted = true;
                _store.Save(_settings);
                Record("初回取得が終わりました。");
                Step = FirstRunStep.Done;
                return;
            }

            if (step >= FirstRunStep.Done)
            {
                return;
            }

            step = (FirstRunStep)((int)step + 1);
        }
    }

    /// <summary>その段の仕事を 1 回走らせ、成否を <see cref="LastStepOk"/> に立てて返す。</summary>
    private async Task<bool> RunStepAsync(FirstRunStep step)
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
        return ok;
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
        if (Step <= FirstRunStep.Notices || IsBusy)
        {
            return;
        }

        // 戻った＝失敗の札は下ろす（「もう一度」ではなく普通の「次へ」に戻す）。
        SetStepOk(true);

        // **働く段からの「戻る」は変種の段へ戻す**（是正・便 D（3）の 3 巡目）。
        // 1 つ前の働く段へ戻していたころは、次の「次へ」が<b>失敗した段ではなく前の段</b>を
        // やり直した（展開で失敗して「戻る」を押すと、取得の段からやり直しになる）。
        Step = IsWorkStep ? FirstRunStep.Variant : (FirstRunStep)((int)Step - 1);
    }

    /// <summary>
    /// 走っている仕事を止める（<c>.part</c> は残るので次回は続きから）。
    /// <b>止める物が無いときは「中断しました」と名乗らない</b>（是正・便 D（3）の 3 巡目）。
    /// </summary>
    public void CancelRunning()
    {
        if (_cancel is null)
        {
            Message = "いま中断できる仕事はありません。";
            return;
        }

        _cancel.Cancel();
        Message = "中断しました（続きから取り直せます）。";
    }

    /// <summary>
    /// 「次へ」を押せるか。通知の段は<b>通知文を読み込めていて</b>、かつ同意に印が要る
    /// （裁定 46＝檔が無ければ先へ進めない・<c>acceptedNoticesSha256</c> に null を残さない）。
    /// </summary>
    /// <summary>
    /// 「次へ」を押せるか。
    /// <para>
    /// 通知の段＝同意の印と、読めた通知文の sha256 が要る（裁定 46）。
    /// <b>変種の段＝下限に届かないドライバの GPU 変種は押せない</b>（裁定 126 の B）＝
    /// 数 GB 落としてから <see cref="Services.Gpu.VariantGate"/> に断られる形を作らない。
    /// </para>
    /// </summary>
    private bool CanGoNext() =>
        !IsBusy
        && (Step is not FirstRunStep.Notices || (Accepted && CanAcceptNotices))
        && (Step is not FirstRunStep.Variant || !VariantBlocked);

    /// <summary>
    /// <b>ウィザードの 1 行を檔にも残す口</b>（裁定 126 の C（1）・是正・検分）。
    /// <para>
    /// <b>なぜ要るか</b>＝切符の元になった実射は<b>清潔導入</b>で、そこで詰まる回（取得・展開・
    /// モデル）は主窓の状態帯を 1 度も通らない＝<c>StatusViewModel.LogSink</c> だけを差した形では
    /// <c>logs\</c> が<b>まさにその場合に空のまま</b>だった。ここも同じ檔へ落とす。
    /// null＝檔には残さない（試験の既定）。<b>投げても止めない</b>（下で包む）。
    /// </para>
    /// </summary>
    public Action<string>? LogSink { get; set; }

    private void Record(string line)
    {
        Trail.Add(line);
        Message = line;
    }

    /// <summary>檔に 1 行落とす（<b>ウィザードを止めない</b>＝IO の失敗は握り潰す）。</summary>
    private void Log(string? line)
    {
        if (LogSink is not { } sink || string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        try
        {
            sink(line);
        }
#pragma warning disable CA1031 // ログが書けないことでウィザードを止めない
        catch (Exception)
#pragma warning restore CA1031
        {
            // 画面には出ている（Trail／Message は上で足し終えている）
        }
    }

    /// <summary>
    /// ウィザードの外で起きた事実を <see cref="Trail"/> に足す口（裁定 90 Q-E2 ⑶）。
    /// <para>
    /// 使うのは<b>取得キャッシュの削除</b>＝「起動の確認」が通り「発話テスト」で 1 射 200 が
    /// 返ったところで消すので、消したのはウィザードの完了の段より<b>後</b>である。
    /// 消したバイトはログと、まだ開いていればこの Trail の両方に残す。
    /// </para>
    /// </summary>
    public void Note(string line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            Record(line.Trim());
        }
    }

    /// <summary>
    /// <b>主窓が自動で開いた回の初期値を入れる</b>（裁定 126 ⑽）＝勧める変種と、
    /// 主窓の列挙が読んだドライバの版。
    /// <para>
    /// <b>「利用者が選んだ」ことにはしない</b>（<c>_variantChosen</c> は立てない）＝
    /// 窓が続けて撃つ <see cref="RefreshDriverAsync"/> の結果で勧め直せる。どちらも
    /// <see cref="VariantRecommendation.Recommend"/> の同じ判断なので<b>食い違わない</b>。
    /// 利用者が自分で選び直せば（<see cref="Variant"/> の setter）そちらが正本になる。
    /// </para>
    /// <para>
    /// ドライバの版は<b>まだ読めていないときだけ</b>入れる＝この画面自身の検分が
    /// 済んでいれば、そちらの方が新しい。
    /// </para>
    /// </summary>
    /// <param name="variant">初期値にする変種（一覧に無い名は無視する）。</param>
    /// <param name="driverVersion">主窓の列挙が読んだドライバの版（null＝渡さない）。</param>
    public void Preselect(string? variant, string? driverVersion = null)
    {
        if (!string.IsNullOrWhiteSpace(driverVersion) && string.IsNullOrWhiteSpace(_probe.DriverVersion))
        {
            _probe = new DriverProbe(driverVersion.Trim(), GpuCount: 1, Probed: true);
        }

        if (!string.IsNullOrWhiteSpace(variant)
            && VariantChoices.Contains(variant, StringComparer.Ordinal))
        {
            SetProperty(ref _variant, variant, nameof(Variant));
        }

        UpdateVariantNotes();
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
        RaisePropertyChanged(nameof(DriverVersion));
        RaisePropertyChanged(nameof(VariantBlocked));
        RaisePropertyChanged(nameof(VariantBlockReason));
        NextCommand.RaiseCanExecuteChanged();

        // ドライバの版は**検分で読めた物**を渡す（裁定 126 の B）。1 巡目はここが常に null で、
        // 検査は「読めませんでした」しか言えず、下限に届かない変種でも「始める」が押せた
        // （司令官の実射＝RTX 3090・537.58・cu130 のまま取得が最後まで通り、起動で門に断られた）。
        // 検分の 1 行は**結果ごとに言い分ける**（是正・検分）＝見た上で 0 台なら「CPU を選んでいる」、
        // 列挙が落ちた回は「読めなかった」＋**列挙が返した理由**（捨てない）。
        var verdict = _driverCheck.Check(_variant, _probe.DriverVersion);
        DriverText = VariantRecommendation.ProbeNote(_probe) is string note
            ? verdict.Message + " " + note
            : verdict.Message;

        // 「必要な空き」は**実際の空きと突き合わせて**から出す（是正・便 D（3）の 3 巡目）。
        // 突き合わせていなかったころは、空きが足りない機体が 4.8 GiB を落とし切ってから
        // 展開の段で落ちた（インストーラ側の門＝裁定 89 は導入の時点の話で、初回取得は別の日である）。
        var text = EstimateSizeText(_paths, _variant, _skipVcRedist);
        var plan = TryPlan(_paths, _variant, _skipVcRedist, out _);
        var shortfall = plan is null
            ? null
            : FreeSpaceShortfall(plan.EstimatedPeakDiskBytes, FreeBytes(_paths.DataDir));
        SizeText = shortfall is null ? text : text + "　" + shortfall;
    }

    /// <summary>
    /// 空きが足りないときの 1 行（足りている・読めないなら null＝<b>純関数</b>）。
    /// </summary>
    public static string? FreeSpaceShortfall(long neededBytes, long? freeBytes)
    {
        if (freeBytes is not long free || neededBytes <= 0 || free >= neededBytes)
        {
            return null;
        }

        return "【空き容量が足りません】必要 " + FetchPlanner.FormatBytes(neededBytes)
            + "・いまの空き " + FetchPlanner.FormatBytes(free)
            + "（あと " + FetchPlanner.FormatBytes(neededBytes - free) + "）。";
    }

    /// <summary>その場所が乗っているドライブの空き（読めなければ null＝黙る）。</summary>
    public static long? FreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (ArgumentException)
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

    /// <summary>
    /// 選んだ変種の実行系が<b>もう出来上がっている</b>か（是正・検分＝裁定 121 の自動再開）。
    /// <para>
    /// <b>要る理由</b>＝取得キャッシュは 1 射目が通った時点で空にする（裁定 90 Q-E2 ⑶＝
    /// <c>MainViewModel.ClearCacheAfterFirstShot</c>）ので、初回取得を通した機体では
    /// <b>原檔が 1 檔も残っていないのが常態</b>である。そこへ「モデルだけ無い」機体で
    /// ウィザードを自動で開くと（裁定 121）、取得の段が 2.77 GiB（cu126）を丸ごと落とし直し、
    /// 展開の段は健全な実行系の置き場を消してから入れ直す（<c>WheelInstaller</c> の
    /// <c>CleanBeforeInstall</c>）＝<b>足りていない物は 1 つも無かった</b>のに、である。
    /// </para>
    /// <para>
    /// 見る物は主窓の焼き印の検査（<c>MainViewModel.CheckRuntimeStamp</c>）と同じ 3 つ＝
    /// <c>python.exe</c> が在る・展開の件数が台帳と合う（<see cref="RuntimeStamp.LooksComplete"/>）・
    /// 展開に使った台帳が配布樹の台帳と同じ（<see cref="RuntimeStamp.Compare"/> の
    /// <c>LedgerChanged</c> が偽）。1 つでも欠ければ<b>飛ばさない</b>＝いつも通り取得から通す。
    /// </para>
    /// </summary>
    public bool RuntimeLooksSound()
    {
        if (_paths.ResolvePythonExe(_variant) is null)
        {
            return false;
        }

        var ledger = ReadLedgerFile<LedgerFile>(_paths.LedgerPath(RuntimeVariants.LedgerName(_variant)));
        if (!RuntimeStamp.LooksComplete(_paths, ledger, _variant))
        {
            return false;
        }

        return !RuntimeStamp.Compare(
            _settings.RuntimeLedgerFor(_variant),
            _settings.InstalledAppVersionFor(_variant),
            RuntimeStamp.LedgerSha256(_paths, _variant),
            AppVersion.Display,
            runtimeInstalled: true,
            _variant).LedgerChanged;
    }

    /// <summary>飛ばしたことを告げる 1 行（<b>逐語</b>＝Trail に残る）。</summary>
    public const string RuntimeSoundSkipLine =
        "実行系は揃っているので取得と展開は飛ばします（モデルだけ取り直します）。";

    /// <summary>展開の段を飛ばしたときの 1 行（<b>逐語</b>）。</summary>
    public const string InstallSkipLine = "実行系は揃っているので展開は飛ばします。";

    private async Task<bool> RunDownloadAsync()
    {
        // **揃っている実行系を落とし直さない**（是正・検分）＝裁定 90 で cache は空なのが
        // 常態なので、ここを素通りさせると数 GiB の再取得と健全な樹の作り直しになる。
        if (RuntimeLooksSound())
        {
            Record(RuntimeSoundSkipLine);
            return true;
        }

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

            UpdateVariantNotes();

            // **落とし始める前に空きを見る**（是正・便 D（3）の 3 巡目）。
            if (FreeSpaceShortfall(plan.EstimatedPeakDiskBytes, FreeBytes(_paths.DataDir))
                is string shortfall)
            {
                Message = shortfall + "空けてから「もう一度」を押してください。";
                return false;
            }

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
        // 取得の段と同じ判断（是正・検分）＝展開は置き場を消してから入れ直すので、
        // 揃っている樹をここへ通してはいけない。
        if (RuntimeLooksSound())
        {
            Record(InstallSkipLine);
            return true;
        }

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

            // **展開に使った台帳を焼く**（裁定 91）＝次の起動で配布樹と突き合わせ、
            // 食い違えば状態帯に「実行系を組み直す」1 手を出す。
            RuntimeStamp.Burn(_settings, _paths, _variant);
            _store.Save(_settings);

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

    /// <summary>
    /// 起動の確認（最後の働く段）。
    /// <para>
    /// <b>ここも取消を持つ</b>（是正・便 D（3）の 3 巡目）＝1 巡目は <c>_cancel</c> を作らず
    /// <c>_startServer</c> にも token を渡していなかったのに、<see cref="CancelCommand"/> は
    /// 押せて「中断しました（続きから取り直せます）。」と名乗り、そのまま Done まで進んで
    /// <c>firstRunCompleted=true</c> が焼かれた（直前の段の <c>finally</c> が
    /// <c>_cancel</c> を null にした後だからである）。ready 待ちはこの機体の設定で 600 秒。
    /// </para>
    /// </summary>
    private async Task<bool> RunStartAsync()
    {
        IsBusy = true;
        _cancel = new CancellationTokenSource();
        try
        {
            ProgressText = "サーバを起こしています…";
            var ok = await _startServer(_cancel.Token).ConfigureAwait(true);
            if (_cancel.IsCancellationRequested)
            {
                // 起こす側は取消を例外で返さない（IServerProcess.StartAsync の約束＝
                // ツリー kill して Stopped で返る）ので、ここで「中断」に読み替える。
                Message = "中断しました（続きから取り直せます）。";
                return false;
            }

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
}
