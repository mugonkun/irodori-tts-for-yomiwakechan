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
    private string _progressDetailText = string.Empty;
    private string _phaseText = string.Empty;
    private bool _uacNoticeVisible;
    private double _progressFraction;
    private double _stepFraction;
    private TimeSpan? _eta;
    private bool _noticesSkipped;
    private bool _isBusy;
    private string _sizeText = UiText.Missing;
    private string _driverText = UiText.Missing;
    private bool _lastStepOk = true;
    private bool _skipVcRedist;

    /// <summary>ドライバの検分（裁定 126 の B）。既定＝まだ何も見ていない。</summary>
    private DriverProbe _probe = DriverProbe.Unknown;

    /// <summary>利用者が変種を<b>自分で選んだ</b>か（真なら勧めで上書きしない）。</summary>
    private bool _variantChosen;

    /// <summary>
    /// いまの動かし方が<b>設定で選ばれた物</b>か（決裁 135 ⑵・v2.0.1）。
    /// <para>
    /// 真の間は段 2 の 1 行が「設定で選んだ … で動かします。」になる＝
    /// <b>アプリが決めたと名乗らない</b>。畳みの中で選び直した回・勧めで上書きした回・
    /// 主窓が <see cref="Preselect"/> で初期値を入れた回は偽に戻す。
    /// </para>
    /// </summary>
    private bool _variantFromSettings;

    /// <summary>
    /// <b>檔に書く真偽</b>＝<see cref="LauncherSettings.VariantChosenByUser"/>（是正・検分）。
    /// <para>
    /// <see cref="_variantFromSettings"/> と分けてある＝あちらは
    /// <b>この回の名乗り方</b>（古い檔では推測が混じる）で、こちらは
    /// <b>利用者が実際に一覧を動かしたか</b>だけを持つ。推測を焼き付けないための分けである。
    /// </para>
    /// </summary>
    private bool _variantChosenByUser;

    /// <param name="openedForAcquisition">
    /// 取得のために開いた回（状態帯の〔取得へ進む〕・起動時の自動・裁定 126 ⑽ の求め）＝真。
    /// 設定 › 詳細 の〔はじめの準備をやり直す〕＝<b>偽</b>（アプリに決め直させる回）。
    /// </param>
    public FirstRunViewModel(
        AppPaths paths,
        LauncherSettings settings,
        ISettingsStore store,
        IDriverCheck driverCheck,
        Func<IDownloader?> downloader,
        Func<IRuntimeInstaller?> installer,
        Func<CancellationToken, Task<bool>> startServer,
        bool openedForAcquisition = true)
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

        // **設定に残っている動かし方は「利用者が選んだ物」である**（決裁 135 ⑵・v2.0.1）。
        // RTX 機の段 H 射 10＝設定 › 詳細 で CUDA 12.6 に替えた機体で、次の起動が
        // （cu126 の一式がまだ無いので正しく）ウィザードを開いた。そこで
        // `RefreshDriverAsync` の勧め（ドライバ 616.92 ⇒ cu130）が**黙って上書き**し、
        // 1 行は「…CUDA 13.0 で動かします。」・保存も cu130 に戻り、cu126 は 1 度も落ちなかった。
        // ⇒ 設定に**利用者が選んだ**綴りが入っていれば、それが**明示の選択**である。
        // 勧めで決めてよいのは⑴ 本当の初回（まだ 1 度も通しておらず、配られたままの既定）
        // ⑵ その選択がドライバの下限に届かないとき（理由を添えて言い換える＝下の
        // <see cref="RefreshDriverAsync"/>）⑶ **〔はじめの準備をやり直す〕で開いた回**
        // （＝利用者はアプリに決め直させたくて押している・是正・検分）の 3 つ。
        //
        // **「アプリが焼いた値」と「利用者が選んだ値」を取り違えない**（是正・検分）＝
        // <see cref="LauncherSettings.Variant"/> は前の回に**アプリ自身が**書いた値でもあるので、
        // それだけを見て「設定で選んだ」と名乗ると、一度も選んでいない利用者に嘘をつく
        // （Radeon 版では GPU の選択肢が ROCm 1 つしか無いのに「設定で選んだ ROCm」と言う）。
        // 真偽は <see cref="LauncherSettings.VariantChosenByUser"/> が持つ＝この欄が無い
        // 古い檔（v2.0.0 まで）だけ、従来どおりの推測に落ちる。
        _variantFromSettings = IsExplicitChoice(VariantChoices, settings, openedForAcquisition);
        _variantChosen = _variantFromSettings;

        // **推測は焼き付けない**＝檔へ書き戻す真偽は「利用者が実際に選んだ」回だけ真にする。
        _variantChosenByUser = settings.VariantChosenByUser == true
            && VariantChoices.Contains(settings.Variant, StringComparer.Ordinal);

        NextCommand = new AsyncRelayCommand(NextAsync, CanGoNext);
        BackCommand = new RelayCommand(Back, () => Step > FirstStep && !IsBusy);
        CancelCommand = new RelayCommand(CancelRunning, () => IsBusy);

        // 捕れなかった例外を握り潰さない（§20-5 ⑴）＝黙って止まったウィザードを作らない。
        NextCommand.Faulted += (_, line) =>
        {
            SetStepOk(false);

            // W11（`v2-spec.md` §3）＝画面は 2 部品の平語・**生の 1 行はログへ**。
            Fail(UnexpectedFailureLine, "この段で予期しない失敗が起きました：" + line);
        };

        LoadNotices();

        // **同じ版のお知らせは二度と訊かない**（決裁 130 Q3・`v2-spec.md` §3 段 1 の ⑶）＝
        // 読み込んだ全文の sha256 が既に同意済みの物と一致する回は、**通知の段を出さずに**
        // 確認の段から始める。**acceptedNoticesSha256 は読むだけで上書きしない**
        // （`v2-plan.md` 段 B の危険 ⑵）。錠（CanAcceptNotices・:606 の CanGoNext）は 1 行も触らない。
        if (NoticesAlreadyAccepted)
        {
            _noticesSkipped = true;
            _accepted = true;
            _step = FirstRunStep.Variant;
        }

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

        // **明示の選択は勧めで上書きしない**（決裁 135 ⑵）＝上書きしてよいのは
        // ⑴ まだ誰も選んでいない（本当の初回）⑵ その選択がこのドライバの下限に届かない、
        // の 2 つだけである。⑵ で替えた回は「アプリが決めた」に戻す（＝理由つきの 1 行が出る
        // ＝<see cref="DecisionLineFor"/> の cu126 の枝が「ドライバの版が 580.00 未満のため」と名乗る）。
        var belowMinimum = VariantRecommendation.IsBelowMinimum(_variant, _probe.DriverVersion);
        if (!_variantChosen || belowMinimum)
        {
            var recommended = VariantRecommendation.Recommend(VariantChoices, _probe);
            if (VariantChoices.Contains(recommended, StringComparer.Ordinal)
                && !string.Equals(recommended, _variant, StringComparison.Ordinal))
            {
                SetProperty(ref _variant, recommended, nameof(Variant));
                _variantChosen = false;
                _variantFromSettings = false;

                // **決めたのはアプリである**＝檔にもそう書く（是正・検分）。
                _variantChosenByUser = false;
            }
        }

        UpdateVariantNotes();
    }

    /// <summary>
    /// <b>設定に残っている動かし方が「利用者の明示の選択」か</b>（<b>純関数</b>・決裁 135 ⑵）。
    /// <para>
    /// 見るのは 3 つ＝⑴ この配布物が出せる一覧に在る綴りか
    /// ⑵ <b>取得のために開いた回か</b>（偽＝〔はじめの準備をやり直す〕＝アプリに決め直させる回・
    /// 是正・検分）⑶ <see cref="LauncherSettings.VariantChosenByUser"/>。
    /// </para>
    /// <para>
    /// ⑶ が <c>null</c>（この欄より古い <c>settings.json</c>＝v2.0.0 まで）の回だけ、
    /// <b>従来どおりの推測</b>に落ちる＝「まだ 1 度も通しておらず
    /// （<see cref="LauncherSettings.FirstRunCompleted"/> が偽）、綴りが配られたままの既定
    /// （<see cref="LauncherSettings.DefaultVariant"/>）」でなければ選ばれた物とみなす。
    /// <b>この推測は檔へ書き戻さない</b>（<c>_variantChosenByUser</c> の註）＝
    /// 次の保存から本当の真偽が入る。
    /// </para>
    /// <para>
    /// <b>下限の判定はここでしない</b>＝ドライバの版はこの時点ではまだ読めていない
    /// （<see cref="RefreshDriverAsync"/> が読む）。下限未満だった回はそちらが勧めで言い換える。
    /// </para>
    /// </summary>
    public static bool IsExplicitChoice(
        IReadOnlyList<string> choices, LauncherSettings settings, bool openedForAcquisition = true)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(settings);

        if (!choices.Contains(settings.Variant, StringComparer.Ordinal))
        {
            return false;
        }

        // 〔はじめの準備をやり直す〕＝**アプリが決め直す**回である（勧めを殺さない）。
        if (!openedForAcquisition)
        {
            return false;
        }

        if (settings.VariantChosenByUser is bool chosen)
        {
            return chosen;
        }

        var untouched = !settings.FirstRunCompleted
            && string.Equals(
                settings.Variant, LauncherSettings.DefaultVariant, StringComparison.Ordinal);
        return !untouched;
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
            RaisePropertyChanged(nameof(HasAdvanced));
            RaisePropertyChanged(nameof(BackVisible));
            NextCommand.RaiseCanExecuteChanged();
            BackCommand.RaiseCanExecuteChanged();

            // 「いま何をしているか」は段に従う（働く段以外は空＝出す物が無い）。
            PhaseText = PhaseLine(_step);
            UacNoticeVisible = false;

            // 1 本のバーは**段が変わった瞬間にも**組み直す（段ごとに 0 へ戻さない＝段 B-3）。
            _stepFraction = 0;
            _eta = null;
            ApplyProgress();
        }
    }

    /// <summary>段 1 の題（`v2-copy.md` §2）。</summary>
    public const string TitleNotices = "お知らせ";

    /// <summary>段 2 の題（`v2-copy.md` §2）。</summary>
    public const string TitleVariant = "これからすること";

    /// <summary>段 3 の題＝<b>働く 4 段で同じ 1 つ</b>（`v2-copy.md` §2）。</summary>
    public const string TitlePreparing = "準備しています";

    /// <summary>完了の題（`v2-copy.md` §2）。</summary>
    public const string TitleDone = "使えます。";

    /// <summary>
    /// 見せる段の題（`v2-copy.md` §2＝<b>4 つ</b>）。
    /// <para>
    /// <b>内部の 7 段は 1 つも触らない</b>（<see cref="FirstRunStep"/>）＝ここは<b>表示用の対応表</b>である。
    /// 働く 4 段（取得・展開・モデル・起動）は利用者からは 1 つの「準備しています」に見え、
    /// いま何をしているかは <see cref="PhaseText"/> の 1 行が名乗る。
    /// </para>
    /// </summary>
    public static string Title(FirstRunStep step) => step switch
    {
        FirstRunStep.Notices => TitleNotices,
        FirstRunStep.Variant => TitleVariant,
        FirstRunStep.Download or FirstRunStep.Install
            or FirstRunStep.Models or FirstRunStep.Start => TitlePreparing,
        FirstRunStep.Done => TitleDone,
        _ => step.ToString(),
    };

    /// <summary>
    /// 記録（<see cref="Trail"/>・ログ）に残す段の名（<b>内輪の 7 つのまま</b>）。
    /// 画面の題（<see cref="Title"/>）が 4 つに畳まれても、詳細の中の記録は
    /// どの段で何が起きたかを段ごとに残す（`v2-copy.md` §1-8＝記録の字は据え置き）。
    /// </summary>
    public static string TrailTitle(FirstRunStep step) => step switch
    {
        FirstRunStep.Notices => "通知",
        FirstRunStep.Variant => "変種",
        FirstRunStep.Download => "取得（実行系）",
        FirstRunStep.Install => "展開",
        FirstRunStep.Models => "取得（モデル）",
        FirstRunStep.Start => "起動の確認",
        FirstRunStep.Done => "完了",
        _ => step.ToString(),
    };

    public string StepTitle => Title(Step);

    /// <summary>
    /// 見せる段の番号（1 起点・<b>完了は番号を持たない</b>＝0）。
    /// <paramref name="noticesSkipped"/>＝同じ版のお知らせに同意済みで段 1 を飛ばした回。
    /// </summary>
    public static int VisibleStepNumber(FirstRunStep step, bool noticesSkipped) => step switch
    {
        FirstRunStep.Notices => 1,
        FirstRunStep.Variant => noticesSkipped ? 1 : 2,
        FirstRunStep.Done => 0,
        _ => noticesSkipped ? 2 : 3,
    };

    /// <summary>見せる段の数（ふだん 3・お知らせを飛ばした回は 2）。</summary>
    public int VisibleStepCount => _noticesSkipped ? 2 : 3;

    /// <summary>お知らせの段を飛ばしたか（同じ版に同意済み＝決裁 130 Q3）。</summary>
    public bool NoticesSkipped => _noticesSkipped;

    /// <summary>段番号の綴り（<c>3 / 3</c>・完了は空＝番号を出さない）。</summary>
    public string StepNumberText =>
        VisibleStepNumber(Step, _noticesSkipped) is int number and > 0
            ? number.ToString(CultureInfo.InvariantCulture) + " / "
              + VisibleStepCount.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>
    /// 「次へ」の文言（`v2-copy.md` §2・§1-8）。
    /// <para>
    /// <b>働く段には「次へ」が無い</b>（裁定 94 ⑴）＝取得→展開→モデル→起動は自動で繋がるので、
    /// この文言が出るのは⑴ お知らせ ⑵ 確認 ⑶ 完了 ⑷ <b>失敗した段（「もう一度」）</b>の 4 つだけである。
    /// </para>
    /// </summary>
    public string NextButtonText => !_lastStepOk
        ? "もう一度"
        : Step switch
        {
            FirstRunStep.Notices => "次へ",
            FirstRunStep.Variant => "準備を始める",
            FirstRunStep.Done => "しゃべらせてみる",
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

    /// <summary>
    /// 詳細の畳み（<c>FirstRunAdvancedExpander</c>）を出す段か。
    /// お知らせの段には別の畳み（全文＝<c>FirstRunNoticesExpander</c>）が在るので出さない。
    /// </summary>
    public bool HasAdvanced => !IsNoticesStep;

    /// <summary>
    /// この画面が始まる段（＝<see cref="Back"/> がこれより手前へは戻らない）。
    /// お知らせを飛ばした回は確認の段が最初になる。
    /// </summary>
    private FirstRunStep FirstStep => _noticesSkipped ? FirstRunStep.Variant : FirstRunStep.Notices;

    /// <summary>
    /// <b>同じ版のお知らせに既に同意している</b>か（決裁 130 Q3・<c>v2-spec.md</c> §3 段 1 の ⑶）。
    /// <para>
    /// 読み込んだ全文の sha256 と <c>settings.acceptedNoticesSha256</c> の突き合わせだけで決まる
    /// ＝<b>新しい判定も新しい鍵も足していない</b>。文面が別の版になった回は偽に戻り、
    /// もう 1 度お知らせの段から始まる。
    /// </para>
    /// </summary>
    public bool NoticesAlreadyAccepted =>
        _noticesSha256 is not null
        && string.Equals(_settings.AcceptedNoticesSha256, _noticesSha256, StringComparison.OrdinalIgnoreCase);

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

            // **檔にもそう書く**（是正・検分）＝次に開いたときアプリが決め直さない。
            _variantChosenByUser = true;

            // **この画面で選び直した**＝もう「設定で選んだ」ではない（決裁 135 ⑵）。
            _variantFromSettings = false;
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

    /// <summary>
    /// CPU 変種の注記（裁定 13＝「遅い」を明記する）。
    /// <para>
    /// <b>版の名は 2 つだけ</b>（`v2-copy.md` §0＝<c>RTX（CUDA）</c>／<c>Radeon（ROCm）</c>）＝
    /// ここで「ROCm 版」という<b>3 つ目の名</b>を綴らない。「未保障」は帳面の語なので、
    /// 憲章 根 6 の平語（どこまで試したか・どこは試していないか）に言い直す。
    /// 技術語（bf16・製品名）は詳細の中なので出してよい（`v2-spec.md` §6-2）。
    /// </para>
    /// </summary>
    public string VariantNote => RuntimeVariants.IsCpu(_variant)
        ? "CPU は GPU の数百分の一の速さです。配信用途では勧めません（試すためだけの選択肢です）。"
        : RuntimeVariants.IsRocm(_variant)
            ? "Radeon（ROCm）で動かします。精度は bf16 に固定されます"
              + "（Ryzen AI MAX+ 395 ／ Radeon 8060S で確認済み。ほかの Radeon では試していません）。"
            : "NVIDIA の GPU で動きます。ドライバの版が下限に届いているか下の行を確かめてください。";

    /// <summary>
    /// <b>アプリが決めた動かし方を告げる 1 行</b>（決裁 130 Q1・`v2-copy.md` §2 段 2・<b>純関数</b>）。
    /// <para>
    /// <b>選ばせない</b>＝判定は <see cref="VariantRecommendation.Recommend"/> そのままで、
    /// ここは<b>その結果を名乗るだけ</b>である（新しい判定は 1 行も書かない）。
    /// 動かし方の名は <see cref="BandText.VariantName"/> の 4 語（CUDA 13.0／CUDA 12.6／ROCm／CPU）＝
    /// 帯と同じ綴りを使う（同じ画面に 2 通りの名を並べない）。
    /// </para>
    /// <para>
    /// <b>CPU は GPU が在る機体では一切見せない</b>（憲章 §7 既定）＝ここが CPU を名乗るのは
    /// <see cref="VariantRecommendation.Recommend"/> が「見た上で 0 台」と決めた回だけである。
    /// </para>
    /// </summary>
    /// <param name="variant">決まった動かし方（台帳の綴り）。</param>
    /// <param name="gpuName">1 台目の製品名（読めなければ null＝括弧ごと落とす）。</param>
    /// <param name="probe">
    /// その判定の<b>もと</b>（null＝渡さない＝従来どおり結果だけを名乗る）。
    /// <b>要る理由</b>（是正・検分）＝<see cref="VariantRecommendation.Recommend"/> の戻りは
    /// 3 通りの事情を 1 つの綴りに畳む。⑴ <c>cpu</c> は「見た上で 0 台」だけでなく
    /// <b>版が読めていて下限未満</b>（＝GPU は在る）でも返る ⑵ <c>cu130</c> は
    /// <b>まだ何も見ていない・列挙が落ちた</b>回の<b>並びの先頭</b>としても返る。
    /// 事情を見ずに名乗ると、GeForce の機体に「GPU が見つかりませんでした」と告げたり、
    /// 何も見ていないのに「このパソコンは CUDA 13.0 です」と言い切ったりする。
    /// </param>
    /// <param name="chosenInSettings">
    /// <b>設定で選ばれた動かし方をそのまま使う回</b>（決裁 135 ⑵・v2.0.1）＝
    /// この 1 行は「アプリが決めました」ではなく<b>「設定で選んだ … で動かします。」</b>になる。
    /// <b>先に言う枝が 2 つある</b>＝⑶ 版を間違えて入れた回（E-06b）と
    /// ⑵ グラフィックスを検められなかった回（<see cref="UnknownDecisionLine"/>・是正・検分）＝
    /// どちらも設定より重い事実で、消すと画面が知らない事を言い切ることになる。
    /// </param>
    public static string DecisionLineFor(
        string? variant, string? gpuName, DriverProbe? probe = null, bool chosenInSettings = false)
    {
        var name = string.IsNullOrWhiteSpace(gpuName) ? null : "（" + gpuName.Trim() + "）";
        var chosen = variant?.Trim();
        var driver = probe?.DriverVersion;

        // ⑶ **版を間違えて入れた回**＝いちばん先に言う（E-06b・是正・段 G・high 8／medium 12）。
        // 公式ページ（site/index.html）が「まちがえて入れても、開いたときにアプリが教えます」と
        // 約束している 1 行はここである。**片方の会社の板しか居ないとき**にだけ言う＝
        // NVIDIA と AMD が同居した機体（ノートの内蔵 Radeon ＋ GeForce）で誤爆しない。
        if (WrongEditionFor(chosen, probe) is { } wrongEdition)
        {
            return wrongEdition;
        }

        // ⑵ **判らないまま並びの先頭に落ちた回**＝決めたふりをしない（`v2-spec.md` §1-2）。
        // **⑷ より先に言う**（是正・検分）＝この枝が持っている 2 つの事実
        // （グラフィックスを検められなかった・うまく動かないときは 詳細 で替えられる）は、
        // 設定に何が入っていようと消えない。順を逆にすると、検分が落ちた機体に
        // 「設定で選んだ CUDA 13.0 で動かします。」とだけ告げて、知らない事を 2 つ言い切ることになる。
        if (probe is not null
            && string.IsNullOrWhiteSpace(driver)
            && (!probe.Probed || probe.FailureReason is not null)
            && chosen is RuntimeVariants.Cu130 or RuntimeVariants.Cu126)
        {
            return UnknownDecisionLine(chosen);
        }

        // ⑷ **設定で選んだ物をそのまま使う回**（決裁 135 ⑵）＝決めたのはアプリではない。
        if (chosenInSettings)
        {
            return ChosenInSettingsLine(chosen);
        }

        return chosen switch
        {
            // ⑴ **版が読めていて下限未満**の cpu＝GPU は在る（A4）＝数字ごと名乗る。
            RuntimeVariants.Cpu =>
                VariantRecommendation.IsBelowMinimum(RuntimeVariants.Cu126, driver)
                    ? DriverTooOldDecisionLine(name, driver)
                    : NoGpuDecisionLine,
            // **「AMD の Radeon」の後ろに NVIDIA の製品名を置かない**（是正・段 G・high 8）＝
            // 上の E-06b が拾いきれない回（両社の板が同居していて、名前だけが NVIDIA だった回）
            // でも、名乗る名前と会社が食い違わないようにする。
            RuntimeVariants.RocmGfx1151 =>
                DecisionLead + "AMD の Radeon"
                + (Services.Gpu.GpuVendors.IsNvidia(gpuName) ? null : name) + "・ROCm で動かします。",
            RuntimeVariants.Cu126 =>
                DecisionLead + "NVIDIA の GPU" + name + "・CUDA 12.6 で動かします"
                + "（グラフィックスドライバの版が " + DriverRequirement.Minimum(RuntimeVariants.Cu130)
                + " 未満のためです）。",
            RuntimeVariants.Cu130 =>
                DecisionLead + "NVIDIA の GPU" + name + "・CUDA 13.0 で動かします。",
            _ => DecisionLead + BandText.VariantName(chosen) + " で動かします。",
        };
    }

    /// <summary>
    /// 段 2 の本文の<b>頭の 1 文</b>（`v2-copy.md` §2 段 2 の逐語）＝
    /// <b>アプリが決めた</b>ことを告げる 1 文（決裁 130 Q1 が届けたい 1 文はこれである）。
    /// </summary>
    public const string DecisionLead = "このパソコンに合わせて、動かし方を選びました。";

    /// <summary>
    /// 設定で選ばれた動かし方をそのまま使う回の頭（決裁 135 ⑵・v2.0.1）。
    /// <b>「選びました」と名乗らない</b>＝選んだのは利用者である。
    /// </summary>
    public const string ChosenInSettingsLead = "設定で選んだ ";

    /// <summary>
    /// 同・1 行まるごと（<b>純関数</b>）＝<c>設定で選んだ CUDA 12.6 で動かします。</c>。
    /// 名は <see cref="BandText.VariantName"/> の 4 語（CUDA 13.0／CUDA 12.6／ROCm／CPU）＝
    /// 帯・設定・ウィザードで同じ綴りを使う。
    /// </summary>
    public static string ChosenInSettingsLine(string? variant) =>
        ChosenInSettingsLead + BandText.VariantName(variant) + " で動かします。";

    /// <summary>
    /// 決まった動かし方を<b>記録</b>（<see cref="Trail"/>・檔）に残す 1 行（<b>純関数</b>・決裁 135 ⑵）。
    /// <para>
    /// 旧＝<c>変種＝CUDA 12.6（ドライバ 528.33 以上） を選びました。</c>＝
    /// 憲章 §6-1 の隠す語「変種」が畳みの中とはいえ画面（<c>FirstRunTrailList</c>）に出ていた。
    /// 新＝<c>動かし方＝CUDA 12.6（cu126）に決まりました。</c>＝<b>内輪の id は括弧に残す</b>
    /// （報告のときに要る・技術語は出してよい＝憲章 §6-2）。
    /// </para>
    /// </summary>
    public static string ChosenVariantLogLine(string? variant, bool chosenInSettings = false) =>
        "動かし方＝" + BandText.VariantName(variant)
        + "（" + (variant?.Trim() ?? UiText.Missing) + "）"
        + (chosenInSettings ? "を設定のまま使います。" : "に決まりました。");

    /// <summary>
    /// <b>版を間違えて入れた機体の 1 行</b>（`v2-copy.md` §3-2 の <b>E-06b</b>・
    /// 是正・段 G・high 8／medium 12）＝null＝間違えていない／判らない。
    /// <para>
    /// <b>言うのは片方の会社の板しか居ないときだけ</b>である。
    /// ⑴ Radeon（ROCm）版なのに NVIDIA しか居ない＝<c>nvidia-smi</c> が答えた
    /// （＝ドライバの版か製品名が読めた）か、OS のアダプタ一覧が NVIDIA だけを返した。
    /// ⑵ RTX（CUDA）版なのに AMD しか居ない＝OS のアダプタ一覧が AMD だけを返した
    /// （素の機体では実行系がまだ無いので、torch も <c>nvidia-smi</c> も答えられない）。
    /// </para>
    /// <para>
    /// <b>ROCm の枝にこれが要る理由</b>＝<see cref="VariantRecommendation.Recommend"/> は
    /// 選択肢に <c>rocm-*</c> が在れば<b>ドライバも GPU も見ずに</b> <c>rocm-gfx1151</c> を返す。
    /// それを受けた ⑴ の枝は「AMD の Radeon（NVIDIA GeForce RTX 3090）・ROCm で動かします。」と
    /// <b>見ていない事実を、しかも相手の製品名つきで</b>名乗っていた。
    /// </para>
    /// </summary>
    public static string? WrongEditionFor(string? variant, DriverProbe? probe)
    {
        if (probe is null || !probe.Probed)
        {
            return null;
        }

        var nvidia = Services.Gpu.GpuVendors.IsNvidia(probe.GpuName)
                     || !string.IsNullOrWhiteSpace(probe.DriverVersion)
                     || probe.HasNvidiaAdapter == true;
        var amd = Services.Gpu.GpuVendors.IsAmd(probe.GpuName) || probe.HasAmdAdapter == true;

        if (string.Equals(variant?.Trim(), RuntimeVariants.RocmGfx1151, StringComparison.Ordinal))
        {
            return nvidia && !amd ? WrongEditionNeedsCuda : null;
        }

        if (variant?.Trim() is RuntimeVariants.Cu130 or RuntimeVariants.Cu126)
        {
            return amd && !nvidia ? WrongEditionNeedsRocm : null;
        }

        return null;
    }

    /// <summary>
    /// E-06b＝Radeon（ROCm）版を NVIDIA の機体に入れた回（⑴⑵⑶ の 3 部品・憲章 原則 6）。
    /// <b>⑶ は釦ではなく公式ページの案内</b>である＝版を入れ替える手はこのアプリの外に在る
    /// （`v2-plan.md` 段 G の記帳・`v2-spec.md` §9 ⒅）。
    /// </summary>
    public const string WrongEditionNeedsCuda =
        "このパソコンのグラフィックスは、この版では使えません。"
        + "このパソコンに入っているのは NVIDIA のグラフィックスですが、"
        + "いま入れてあるのは Radeon（ROCm）版です。"
        + "公式ページから RTX（CUDA）版をダウンロードして入れ直してください。";

    /// <summary>E-06b＝RTX（CUDA）版を AMD の機体に入れた回。</summary>
    public const string WrongEditionNeedsRocm =
        "このパソコンのグラフィックスは、この版では使えません。"
        + "このパソコンに入っているのは AMD の Radeon ですが、"
        + "いま入れてあるのは RTX（CUDA）版です。"
        + "公式ページから Radeon（ROCm）版をダウンロードして入れ直してください。";

    /// <summary>GPU が見つからなかった機体の 1 行（憲章 §7・`v2-copy.md` §2 段 2）。</summary>
    public const string NoGpuDecisionLine =
        "このパソコンには対応する GPU が見つかりませんでした。"
        + "とても遅い方法（CPU）で試すこともできますが、配信には使えません。";

    /// <summary>
    /// <b>GPU は在るが、ドライバが古くて使えない</b>機体の 1 行（A4・`v2-spec.md` §3 段 2 の
    /// 「下限未満で断られた回は、本文の 1 行にドライバのいまの版と必要な版の数字を入れ」）。
    /// ⑵ の綴りは帯の E-02 と同じ形（`v2-copy.md` §3-2）にする。
    /// </summary>
    private static string DriverTooOldDecisionLine(string? name, string? driverVersion) =>
        "このパソコンの GPU" + name + "は、いまのグラフィックスドライバでは使えません"
        + "（いまの版 " + (driverVersion?.Trim() ?? UiText.Missing)
        + "・必要なのは " + DriverRequirement.Minimum(RuntimeVariants.Cu126) + " 以上です）。"
        + "とても遅い方法（CPU）で試すこともできますが、配信には使えません。"
        + "グラフィックスドライバを新しくしてから、はじめの準備をやり直すこともできます。";

    /// <summary>
    /// <b>グラフィックスを確かめられなかった</b>回の 1 行（<see cref="VariantRecommendation.Recommend"/>
    /// が「判らないことを勝手に決めない」で並びの先頭に落ちた回）＝
    /// <b>決めたとは言わない</b>・既定で進むことと、あとから変えられることを告げる。
    /// </summary>
    public static string UnknownDecisionLine(string? variant) =>
        "このパソコンのグラフィックスを確かめられませんでした。"
        + "ひとまず " + BandText.VariantName(variant) + " で準備します。"
        + "うまく動かないときは、設定の「詳細」で動かし方を変えてください。";

    /// <summary>いま決まっている動かし方を告げる 1 行（<see cref="DecisionLineFor"/>）。</summary>
    public string DecisionLine =>
        DecisionLineFor(_variant, _probe.GpuName ?? _settings.GpuName, _probe, _variantFromSettings);

    /// <summary>
    /// いまの動かし方が<b>設定で選ばれた物</b>か（決裁 135 ⑵・試験と窓が読む）。
    /// </summary>
    public bool VariantFromSettings => _variantFromSettings;

    /// <summary>
    /// これから落とす量と時間の 1 行（`v2-copy.md` §2 段 2）。
    /// <b>丸めた散文の定数</b>である（`v2-spec.md` §1-3＝実数は詳細の中に <c>GiB</c> のまま残す）。
    /// 数は憲章 §4 の「数字の 1 枚表」だけを引く。
    /// </summary>
    public const string PlanLine =
        "これから約 5.3 GB をダウンロードします。10 分ほどかかります（回線の速さによります）。";

    /// <summary>
    /// <b>なぜ落とすのか</b>の 1 行（`v2-copy.md` §2 段 1 の 3 行目と<b>同じ 1 文</b>）。
    /// <para>
    /// <b>段 2 にも要る</b>（憲章 原則 3 の机上の検分＝<b>釦を押す前の 1 画面</b>に
    /// ⑴ 量 ⑵ 時間 ⑶ なぜ が全部載っていること・`v2-spec.md`:1172 の「§3 段 1 と段 2 も同じ 3 つを持つ」）。
    /// お知らせに同意済みの回は段 1 を飛ばすので（決裁 130 Q3）、この 1 文が段 2 に無いと
    /// 〔はじめの準備をやり直す〕から入った利用者は<b>なぜを 1 度も読まない</b>。
    /// </para>
    /// </summary>
    public const string WhyLine = "ほかの方が作った物を勝手に配って回らない方針だからです。";

    /// <summary>途中でやめられることを告げる 1 行（`v2-copy.md` §2 段 2）。</summary>
    public const string ResumeLine = "途中でやめられます。次に開いたときは、続きから始めます。";

    /// <summary>お知らせの段の要約（`v2-copy.md` §2 段 1＝全文は〔全文を見る〕で開く）。</summary>
    public const string NoticesSummary =
        "このアプリは、ほかの方が作ったプログラムと音声モデルを使って動きます。\n"
        + "それらはこのアプリには入っていないので、これからこのパソコンへダウンロードします"
        + "（約 5.3 GB・10 分ほど）。\n"
        + "ほかの方が作った物を勝手に配って回らない方針だからです。\n"
        + "それぞれに作者と利用条件があり、その全文はダウンロードのあと、このパソコンにも残ります。";

    /// <summary>準備中に常設で出す 1 行（`v2-copy.md` §2 段 3）。</summary>
    public const string WaitLine =
        "このままお待ちください。ネットにつないだままにしてください。ほかの作業をしていてかまいません。";

    /// <summary>Windows の許可の窓が出る直前の 1 行（憲章 §4-9・`v2-copy.md` §2 段 3）。</summary>
    public const string UacNoticeLine =
        "Microsoft の部品を入れます。Windows の許可の窓が 1 度出るので「はい」を押してください。";

    /// <summary>完了の本文（`v2-copy.md` §2 完了）。</summary>
    public const string DoneLine =
        "さっそく、ひとことしゃべらせてみましょう。\n"
        + "読み分けちゃん2 で使うときは、このアプリを開いたままにしてください"
        + "（閉じると読み上げも止まります）。";

    // ---- 画面が束縛する口（XAML から定数を直に引かない＝綴りが 2 つに割れない）----

    /// <summary>お知らせの要約（<see cref="NoticesSummary"/>）。</summary>
    public string NoticesSummaryText => NoticesSummary;

    /// <summary>これから落とす量と時間の 1 行（<see cref="PlanLine"/>）。</summary>
    public string PlanText => PlanLine;

    /// <summary>なぜ落とすのかの 1 行（<see cref="WhyLine"/>）。</summary>
    public string WhyText => WhyLine;

    /// <summary>お知らせの全文が読めない（＝要約もチェックも出さない・`v2-copy.md` §2 段 1）。</summary>
    public bool NoticesUnreadable => !CanAcceptNotices;

    /// <summary>お知らせの全文が読めた（＝ふだんの段 1 を出す）。</summary>
    public bool NoticesReadable => CanAcceptNotices;

    /// <summary>全文が読めないときに<b>本文の代わりに出す</b> 1 行（`v2-copy.md` §2 段 1）。</summary>
    public string NoticesUnreadableText => NoticesMissingLine;

    /// <summary>
    /// 〔戻る〕を<b>画面に出すか</b>（`v2-spec.md` §3 段 3＝働いている間の釦は〔やめる〕だけ・
    /// `v2-copy.md` §2 完了＝完了の釦は〔しゃべらせてみる〕だけ）。
    /// <b>押せる条件（<see cref="BackCommand"/>）と同じ式</b>に、完了の段を足しただけである
    /// ＝要素も id も残る（灰色の釦を並べない）。
    /// </summary>
    public bool BackVisible => Step > FirstStep && !IsBusy && !IsDoneStep;

    /// <summary>途中でやめられることを告げる 1 行（<see cref="ResumeLine"/>）。</summary>
    public string ResumeText => ResumeLine;

    /// <summary>準備中の常設 1 行（<see cref="WaitLine"/>）。</summary>
    public string WaitText => WaitLine;

    /// <summary>Windows の許可の予告（<see cref="UacNoticeLine"/>）。</summary>
    public string UacNoticeText => UacNoticeLine;

    /// <summary>完了の本文（<see cref="DoneLine"/>）。</summary>
    public string DoneText => DoneLine;

    // ---- 失敗の 1 行（`v2-spec.md` §3 段 3 の W 群＝⑴ 何が起きたか ＋ ⑵ なぜか）----
    // ⑶ 次にやること は釦（`NextButtonText` が「もう一度」に変わる）が受け持つ。
    // **元の内輪の 1 行は捨てない**＝`Fail` が檔へ落とす。

    /// <summary>W1＝お知らせの全文が読めない（`v2-copy.md` §1-8 の :452）。</summary>
    public const string NoticesUnreadableLine =
        "お知らせの全文が読めないので、先へ進めません。アプリを入れ直してください。";

    /// <summary>W1＝お知らせの全文が見つからない（`v2-copy.md` §1-8 の :700-716）。</summary>
    public const string NoticesMissingLine =
        "お知らせの全文が見つかりません。この状態でははじめの準備を始められません。"
        + "アプリを入れ直してください。";

    /// <summary>W2＝取得台帳が読めない（`v2-spec.md` §3 W2）。</summary>
    public const string LedgerBrokenLine = "準備を始められません。アプリのファイルが壊れているようです";

    /// <summary>
    /// W2 の ⑵（`v2-copy.md` §3-2 E-03）＝<b>理由は平語 1 文に畳む</b>。
    /// <b>台帳の檔名は画面に出さない</b>（`v2-copy.md` §3-2 の書き方の規則＝生の記録は画面に出さない・
    /// 檔には残る）＝<c>runtime-cu130.json が読めません</c> は <see cref="Fail"/> の第 2 引数でログへ落ちる。
    /// </summary>
    public const string LedgerBrokenWhyLine = "必要なファイルの一覧が読めませんでした。";

    /// <summary>
    /// W3＝ディスクの空きが足りない（`v2-copy.md` §3-2 E-09 の 3 部品・<b>逐語</b>）。
    /// <b>実数（GiB）は画面に出さない</b>（憲章 §6-1＝画面は「GB」で通す・詳細と帳面は GiB のまま）＝
    /// 要る量は憲章 §4 の「数字の 1 枚表」の<b>約 12 GB</b> 1 つだけを綴る。
    /// 実数の 3 つ組は <see cref="FreeSpaceShortfall"/> がログと <see cref="SizeText"/>（詳細の中）に残す。
    /// </summary>
    public const string FreeSpaceLine =
        "ディスクの空きが足りません。準備には空き 約 12 GB が要ります。"
        + "要らないファイルを消してから〔もう一度〕を押してください。";

    /// <summary>W4＝ダウンロードできなかった（`v2-spec.md` §3 W4・E-10 の ⑶ を 1 文で添える）。</summary>
    public const string DownloadFailedLine = "ダウンロードできませんでした。";

    /// <summary>
    /// W4 の ⑵（`v2-copy.md` §3-2 E-10 の<b>逐語</b>）。
    /// <b>取り手が返した生の理由（<c>HTTP 404</c>・<c>sha256 が台帳と合わない</c>・例外の文）は
    /// 画面に出さない</b>＝<see cref="Fail"/> の第 2 引数でログへ落ちる。
    /// </summary>
    public const string DownloadFailedWhyLine =
        "ネットにつながらなくなったか、配布元が応答しませんでした。";

    /// <summary>W4 の ⑶＝続きから始まることを告げる 1 文。</summary>
    public const string ResumeHintLine = "〔もう一度〕を押すと、続きから始めます。最初からにはなりません。";

    /// <summary>W5＝組み立てに失敗した（`v2-spec.md` §3 W5）。</summary>
    public const string InstallFailedLine = "組み立てに失敗しました。";

    /// <summary>
    /// W5 の ⑵。<b>展開系の生の理由</b>（<c>変種ディレクトリに檔が 1 つも入っていない。</c>・
    /// <c>展開の件数が台帳と合わない（*.dist-info N 件・台帳は M 件）。</c>）は画面に出さない
    /// ＝<b>変種・台帳・配布樹</b>は憲章 §6-1 の隠す語である。生の 1 行はログに残る。
    /// </summary>
    public const string InstallFailedWhyLine =
        "ダウンロードしたファイルが壊れているかもしれません。";

    /// <summary>
    /// <b>まだ組み込まれていない段</b>の 1 行（`v2-copy.md` §1-8 の :1000／:1083／:1189／:1251）。
    /// 便・台帳・展開・実行系は憲章 §6-1 の隠す語なので、<b>内輪の 1 行はログだけ</b>に残す。
    /// </summary>
    public const string NotYetLine = "まだできません。";

    /// <summary>W6＝声のデータを落とせなかった（`v2-spec.md` §3 W6）。</summary>
    public const string ModelsFailedLine =
        "声のデータをダウンロードできませんでした。途中で止まりました。";

    /// <summary>W8＝Microsoft の部品を入れられなかった（`v2-spec.md` §3 W8）。</summary>
    public const string VcRedistFailedLine =
        "Microsoft の部品を入れられませんでした。Windows の確認が出なかったか、閉じられました。";

    /// <summary>W9＝やめた（<b>失敗ではない</b>＝`v2-copy.md` §1-8 の :589）。</summary>
    public const string CancelledLine = "やめました。次に開いたときは、続きから始めます。";

    /// <summary>W10＝止める物が無い（`v2-copy.md` §1-8 の :584）。</summary>
    public const string NothingToCancelLine = "いまはやめられません。";

    /// <summary>W11＝予期しない失敗（`v2-spec.md` §3 W11）。</summary>
    public const string UnexpectedFailureLine = "うまくいきませんでした。予期しない失敗です。";

    /// <summary>
    /// E-12＝<b>時間内に終わらなかった</b>回の 1 行（`v2-copy.md` §3-2 E-12）。
    /// <b>これは「理由が判らない回」の文ではない</b>（是正・検分）＝口が埋まっている・
    /// ドライバが下限未満・子が exit 2 で落ちた回は<b>別の理由</b>なので、
    /// 起こす側が持っている 1 行を <see cref="StartFailureLine"/> から受けて
    /// <see cref="BandText.For"/> の ⑴＋⑵ を出す。ここはその 1 行が無い回だけの文である。
    /// </summary>
    public const string StartFailedLine =
        "準備に時間がかかりすぎました。時間内に、読み上げの準備が終わりませんでした"
        + "（ほかのソフトが重いときに起きやすくなります）。";

    /// <summary>
    /// <b>下限に届かない動かし方を選んでいる</b>間の画面の 1 行（`v2-copy.md` §3-2 E-02・<b>純関数</b>）。
    /// <para>
    /// <b>内輪の 1 行をそのまま出さない</b>（是正・検分）＝<see cref="VariantRecommendation.BlockReason"/>
    /// は「変種」を綴る（憲章 §6-1 の隠す語）。数字（いまの版・下限）は ⑵ に出してよい
    /// （`v2-copy.md` §3-2 の書き方の規則）。⑶ は<b>この画面の畳み</b>を開く 1 手である。
    /// </para>
    /// </summary>
    public static string VariantBlockedLine(string? variant, string? driverVersion) =>
        "この GPU では、いまの動かし方が使えません。"
        + "グラフィックスドライバが古いためです（いまの版 "
        + (string.IsNullOrWhiteSpace(driverVersion) ? UiText.Missing : driverVersion.Trim())
        + "・" + BandText.VariantName(variant) + " には "
        + (DriverRequirement.Minimum(variant) ?? UiText.Missing) + " 以上が必要です）。"
        + "〔詳細（上級者向け）〕を開いて、動かし方を変えてください。";

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

    /// <summary>
    /// <b>画面に出す文と、檔に残す文を分ける</b>（`v2-spec.md` §3 段 3 の失敗表＝
    /// 「内部の 1 行（ログはこのまま）」・憲章 §5「ログの綴りは変えない」）。
    /// <para>
    /// 画面には 3 部品（⑴ 何が起きたか ⑵ なぜか）に言い直した 1 行を出し、
    /// <b>元の 1 行は捨てずに</b>ログへ落とす。⑶ 次にやること＝〔もう一度〕は
    /// <see cref="NextButtonText"/> が出す（失敗した段では札がそう変わる）。
    /// </para>
    /// </summary>
    private void Fail(string display, string internalLine)
    {
        // 記録が先＝画面の文だけが残って原因が消える、という順にしない。
        if (!string.Equals(display, internalLine, StringComparison.Ordinal))
        {
            Log(internalLine);
        }

        Message = display;
    }

    /// <summary>
    /// 進捗の 1 行（`v2-copy.md` §2 段 3＝<c>42 %　あと 6 分ほど</c>）。
    /// <b>速さ・件数・バイト数の内訳は <see cref="ProgressDetailText"/>（詳細の中）へ</b>。
    /// </summary>
    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    /// <summary>
    /// 進捗の内訳 1 行（<b>詳細の畳みの中だけ</b>＝速さ・件数・バイト数・回数）。
    /// 受け入れ条件 D-5 の「bytes／ETA」はこの行が引き続き満たす。
    /// </summary>
    public string ProgressDetailText
    {
        get => _progressDetailText;
        private set => SetProperty(ref _progressDetailText, value);
    }

    /// <summary>
    /// <b>いま何をしているか</b>の 1 行（憲章 §4-8・`v2-copy.md` §2 段 3）＝
    /// 働く 4 段のうち 1 つだけを平語で名乗り、量が読める段は「（3.2 GB / 5.3 GB・残り 4 分）」を添える。
    /// </summary>
    public string PhaseText
    {
        get => _phaseText;
        private set => SetProperty(ref _phaseText, value);
    }

    /// <summary>
    /// <b>声を読み込んでいる最中だと告げる</b>（決裁 135 ⑴・v2.0.1）。
    /// <para>
    /// 主窓（<c>MainViewModel.ApplyServerState</c>）が、起こした個体の口が開いて
    /// まだ載っていない状態（<see cref="ServerState.Listening"/>）を見たときに呼ぶ。
    /// <b>「起動の確認」の段の間だけ</b> 1 行を差し替える＝ほかの段の文言は 1 字も動かない。
    /// 取得の直後の 1 回目は読み込みが数分に延びうるので、ここで黙っていると
    /// 「動くか確かめています。」のまま固まったように見える（段 H 射 2＝実測 約 2 分の静止）。
    /// 毎秒動かす経過秒は決裁 137 ⒝ の持ち場（別の工事）である。
    /// </para>
    /// </summary>
    public void ReportLoadingVoices()
    {
        if (Step is FirstRunStep.Start)
        {
            PhaseText = UiStrings.WizardLoadingVoices;
        }
    }

    /// <summary>Windows の許可の窓の予告を出しているか（憲章 §4-9）。</summary>
    public bool UacNoticeVisible
    {
        get => _uacNoticeVisible;
        private set => SetProperty(ref _uacNoticeVisible, value);
    }

    /// <summary>
    /// <b>1 本のバー</b>（0〜1）＝働く 4 段を <see cref="FirstRunProgress.Overall"/> で合成した値。
    /// 段が変わっても 0 へ戻らない（`v2-plan.md` 段 B-3）。
    /// </summary>
    public double ProgressFraction
    {
        get => _progressFraction;
        private set => SetProperty(ref _progressFraction, value);
    }

    /// <summary>
    /// その段の進み（0〜1）を受けて、1 本のバーと進捗の 1 行を組み直す。
    /// <b>合成は純関数に任せる</b>（<see cref="FirstRunProgress"/>）＝ここは値を配るだけ。
    /// </summary>
    private void ApplyProgress()
    {
        ProgressFraction = FirstRunProgress.Overall(Step, _stepFraction);
        ProgressText = FirstRunProgress.Line(ProgressFraction, _eta);
    }

    /// <summary>
    /// 「いま何をしているか」の 1 行を組む（<b>純関数</b>・`v2-copy.md` §2 段 3＝<b>5 文のどれか 1 つ</b>）。
    /// <para>
    /// <b>数を添えない</b>（是正・検分＝`v2-spec.md` §1-3）＝利用者向けの面に出す数は
    /// ⑴ 割合（<c>42 %</c>）⑵ 丸めた残り（<c>あと 6 分ほど</c>）⑶ 散文の総量（<c>約 5.3 GB</c>）の
    /// 3 つだけで、<b>この 3 つ以外の数を利用者向けの面に書かない</b>。実測の内訳は
    /// <see cref="ProgressDetailText"/>／<see cref="SizeText"/>（＝詳細の中）に <c>GiB</c> のまま残る。
    /// </para>
    /// </summary>
    /// <param name="step">いまの段。</param>
    public static string PhaseLine(FirstRunStep step)
    {
        var head = step switch
        {
            FirstRunStep.Download => "必要な部品をダウンロードしています",
            FirstRunStep.Install => "落とした物を組み立てています",
            FirstRunStep.Models => "声のデータをダウンロードしています",
            FirstRunStep.Start => "動くか確かめています",
            _ => string.Empty,
        };

        return head.Length == 0 ? string.Empty : head + "。";
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaisePropertyChanged(nameof(BackVisible));
                NextCommand.RaiseCanExecuteChanged();
                BackCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 「次へ」の 1 手。
    /// <para>
    /// <b>成功した段は自動で次へ進む</b>（裁定 94 ⑴）。押下は<b>4</b> だけになる＝
    /// 同意チェック・次へ・準備を始める・しゃべらせてみる（v2.0 段 B-1／B-4＝<b>動かし方を選ぶ 1 押しは
    /// 無くなった</b>＝アプリが決める）。取得→展開→モデル→起動は
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
            // 画面は E-02 の 3 部品・**内輪の 1 行（「変種」を綴る）はログへ**（是正・検分）。
            Fail(VariantBlockedLine(_variant, _probe.DriverVersion), blockedFirst);
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
                    Fail(
                        NoticesUnreadableLine,
                        "通知文を読み込めていないので同意できません（配布物を確かめてください）。");
                    return;
                }

                _settings.AcceptedNoticesSha256 = _noticesSha256;
                Record("通知に同意しました。");         // 記録は畳みの中＋檔（画面には出さない）
                Step = FirstRunStep.Variant;
                break;

            case FirstRunStep.Variant:
                // 門はこの手の頭でもう見た（束縛の外＝試験・台本から呼ばれても通さない＝裁定 126 の B）。
                // **変種の既定はここで敷く**（裁定 65・設計書 §5・是正・検分）＝
                // 生の代入だと <see cref="Services.Settings.SettingsDefaults.ApplyVariant"/> が
                // 1 度も走らず、Radeon 版に暖機の既定 ON が届かない・cpu に替えても GPU の
                // UUID／名が残る、という穴が開く（綴りが同じ回は何もしない＝利用者の意思を消さない）。
                Services.Settings.SettingsDefaults.ApplyVariant(_settings, _variant);

                // **選んだのが誰かも檔に残す**（是正・検分）＝古い檔の推測は書き戻さない。
                _settings.VariantChosenByUser = _variantChosenByUser;
                _store.Save(_settings);

                // **記録の 1 行にも「変種」を綴らない**（決裁 135 ⑵・憲章 §6-1）＝
                // Trail（FirstRunTrailList）は畳みの中とはいえ画面に出る。内輪の id（cu126）は
                // 記録に残してよい（＝報告のときに要る）ので括弧で添える。
                Record(ChosenVariantLogLine(_variant, _variantFromSettings));
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
                // 「いま何をしているか」の 1 行は段ごとに言い直す（`v2-copy.md` §2 段 3）。
                PhaseText = PhaseLine(step);

                // 同じ段を「もう一度」で撃ち直しても行は重ねない（是正・便 D（3）の 3 巡目）。
                // **記録は内輪の 7 段の名のまま**（画面の題は 4 つに畳んだ＝`Title`）。
                var head = "― " + TrailTitle(step);
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
        if (Step <= FirstStep || IsBusy)
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
            Fail(NothingToCancelLine, "いま中断できる仕事はありません。");
            return;
        }

        _cancel.Cancel();
        Fail(CancelledLine, "中断しました（続きから取り直せます）。");
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

    /// <summary>
    /// 済んだ事実を<b>詳細の中の記録</b>（<see cref="Trail"/>）と檔に残す。
    /// <para>
    /// <b>画面の 1 行（<see cref="Message"/>）には出さない</b>（是正・検分＝憲章 §6-1）。
    /// ここを通る字は内輪の綴りのまま（変種・初回取得・実行系・展開・GiB）で、
    /// <see cref="Message"/> は畳みの<b>外</b>（<c>FirstRunMessageText</c>）に出るので、
    /// 通していたころは隠す語がそのまま利用者の目に入っていた。
    /// <paramref name="show"/>＝<b>文言表が画面に出すと決めた行</b>だけ真で呼ぶ。
    /// </para>
    /// </summary>
    private void Record(string line, bool show = false)
    {
        Trail.Add(line);

        if (show)
        {
            Message = line;      // setter が檔にも落とす
            return;
        }

        Log(line);
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
            && VariantChoices.Contains(variant, StringComparer.Ordinal)
            && !string.Equals(variant, _variant, StringComparison.Ordinal))
        {
            SetProperty(ref _variant, variant, nameof(Variant));

            // **主窓の勧めで替えた＝もう「設定で選んだ」ではない**（決裁 135 ⑵）。
            // ここへ来るのは下限未満で断った回だけ（裁定 126 ⑽）＝理由は帯と Trail に在る。
            _variantFromSettings = false;

            // 決めたのはアプリである＝檔にもそう書く（是正・検分）。
            _variantChosenByUser = false;
        }

        UpdateVariantNotes();
    }

    private void LoadNotices()
    {
        try
        {
            if (!File.Exists(_paths.FirstRunNoticesPath))
            {
                // W1（`v2-spec.md` §3）＝画面は平語・**内輪の 1 行は檔へ**（路を捨てない）。
                Log("通知文（licenses/first-run-notices.md）が配布物に見つかりません。"
                    + "この状態では初回取得を始められません。");
                NoticesText = NoticesMissingLine;
                return;
            }

            var bytes = File.ReadAllBytes(_paths.FirstRunNoticesPath);
            NoticesText = new UTF8Encoding(false).GetString(bytes);
            _noticesSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            RaisePropertyChanged(nameof(NoticesSha256));
        }
        catch (IOException ex)
        {
            Log("通知文が読めませんでした：" + ex.Message);
            NoticesText = NoticesMissingLine;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log("通知文が読めませんでした：" + ex.Message);
            NoticesText = NoticesMissingLine;
        }
        finally
        {
            RaisePropertyChanged(nameof(CanAcceptNotices));
            RaisePropertyChanged(nameof(NoticesReadable));
            RaisePropertyChanged(nameof(NoticesUnreadable));
            NextCommand.RaiseCanExecuteChanged();

            // **W1 は 1 度も押させずに着地させる**（是正・検分）＝要約もチェックも出さない
            // （`v2-copy.md` §2 段 1）代わりに、この 1 行を画面の 1 行にも置く。
            // 置かなかったころは「押せないチェックと効かない〔次へ〕」だけが残り、
            // 差し替えの文は畳み（`FirstRunNoticesExpander`・既定は閉）の中で見えなかった。
            if (!CanAcceptNotices)
            {
                Message = NoticesMissingLine;
            }
        }
    }

    private void UpdateVariantNotes()
    {
        RaisePropertyChanged(nameof(VariantDisplayName));
        RaisePropertyChanged(nameof(VariantNote));
        RaisePropertyChanged(nameof(VariantFromSettings));
        RaisePropertyChanged(nameof(DecisionLine));
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
        var plan = TryPlan(_paths, _variant, _skipVcRedist, out var planReason);

        // 見積りが立たなかった理由は**記録にだけ**残す（是正・段 C の検分）＝
        // 画面の 1 行は「必要な大きさが分かりませんでした。」で、工学の綴りは檔へ落ちる。
        if (plan is null && !string.IsNullOrWhiteSpace(planReason))
        {
            Log(planReason);
        }

        var shortfall = plan is null
            ? null
            : FreeSpaceShortfall(plan.EstimatedPeakDiskBytes, FreeBytes(_paths.DataDir));
        SizeText = shortfall is null ? text : text + "　" + shortfall;
    }

    /// <summary>
    /// 空きが足りないときの 1 行（足りている・読めないなら null＝<b>純関数</b>）。
    /// <para>
    /// W3（`v2-spec.md` §3）の 3 部品＝⑴ 空き容量が足りません。 ⑵ あと &lt;c&gt; 足りません
    /// （必要 &lt;a&gt;・いまの空き &lt;b&gt;）。 ⑶〔もう一度〕（空けてから押す）。
    /// <b>実数はここだけ残す</b>＝どれだけ空ければよいかは、数が無いと利用者が動けない。
    /// </para>
    /// </summary>
    public static string? FreeSpaceShortfall(long neededBytes, long? freeBytes)
    {
        if (freeBytes is not long free || neededBytes <= 0 || free >= neededBytes)
        {
            return null;
        }

        return "空き容量が足りません。あと " + FetchPlanner.FormatBytes(neededBytes - free)
            + " 足りません（必要 " + FetchPlanner.FormatBytes(neededBytes)
            + "・いまの空き " + FetchPlanner.FormatBytes(free) + "）。";
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
            // **読めなかった理由は画面に出さない**（是正・段 C の検分）＝「取得台帳」は
            // 憲章 §6-1 の隠す語で、`sha256 の無い item …` のような工学の 1 行が
            // FirstRunSizeText（はじめの準備 段 2）にそのまま載っていた。
            // 理由は呼ぶ側が `TryPlan(… out reason)` で取り、記録へ落とす。
            _ = reason;
            return UiStrings.WizardSizeUnknown;
        }

        var runtime = plan.Steps
            .Where(static s => s.Stage is FetchStage.PythonEmbed or FetchStage.Runtime)
            .Sum(static s => s.Bytes);
        var models = plan.ModelBytes;
        var vc = plan.Steps.Where(static s => s.Stage is FetchStage.VcRedist).Sum(static s => s.Bytes);

        // 内訳の札は利用者の語で綴る（`v2-copy.md` §1-8 の :814-832＝実数は詳細の中に `GiB` のまま）。
        var text = plan.Summary()
            + "（" + UiStrings.WizardSizeRuntime + FetchPlanner.FormatBytes(runtime)
            + UiStrings.WizardSizeModels + FetchPlanner.FormatBytes(models);
        if (vc > 0)
        {
            text += UiStrings.WizardSizeVcRedist + FetchPlanner.FormatBytes(vc);
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

    /// <summary>飛ばしたことを告げる 1 行（<b>逐語</b>＝Trail に残る・`v2-copy.md` §1-8 の :982）。</summary>
    public const string RuntimeSoundSkipLine =
        "必要な部品は揃っています。声のデータだけをダウンロードします。";

    /// <summary>展開の段を飛ばしたときの 1 行（<b>逐語</b>・`v2-copy.md` §1-8 の :985）。</summary>
    public const string InstallSkipLine = "必要な部品は揃っています。";

    private async Task<bool> RunDownloadAsync()
    {
        // **揃っている実行系を落とし直さない**（是正・検分）＝裁定 90 で cache は空なのが
        // 常態なので、ここを素通りさせると数 GiB の再取得と健全な樹の作り直しになる。
        if (RuntimeLooksSound())
        {
            Record(RuntimeSoundSkipLine, show: true);   // 文言表が画面に出すと決めた行
            return true;
        }

        var downloader = _downloader();
        if (downloader is null)
        {
            Fail(NotYetLine, "取得系がまだ組み込まれていません（便 D・取得席の実装待ち）。");
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
                // W2＝⑴ 準備を始められません。 ⑵ アプリのファイルが壊れているようです。
                // **檔名（runtime-cu130.json …）は画面に出さない**＝ログにだけ残す。
                Fail(
                    LedgerBrokenLine + "。" + LedgerBrokenWhyLine,
                    "取得台帳が読めないので取得を始められません"
                        + (planReason is null ? "。" : "（" + planReason + "）。"));
                return false;
            }

            UpdateVariantNotes();

            // **落とし始める前に空きを見る**（是正・便 D（3）の 3 巡目）。
            if (FreeSpaceShortfall(plan.EstimatedPeakDiskBytes, FreeBytes(_paths.DataDir))
                is string shortfall)
            {
                // W3＝E-09 の 3 部品（`v2-copy.md` §3-2）。**実数（GiB）はログと詳細の中だけ**。
                Fail(FreeSpaceLine, shortfall + "空けてから「もう一度」を押してください。");
                return false;
            }

            var requests = FetchPlanner.ToDownloadRequests(plan, _paths.DownloadCacheDir);
            if (requests.Count == 0)
            {
                Fail(
                    LedgerBrokenLine + "。" + LedgerBrokenWhyLine,
                    "取得台帳が読めないので取得を始められません。");
                return false;
            }

            var results = await downloader
                .DownloadAllAsync(requests, progress, _cancel.Token)
                .ConfigureAwait(true);

            var failed = results.FirstOrDefault(static r => !r.Ok);
            if (failed is not null)
            {
                // W4＝⑴ ダウンロードできませんでした。 ⑵ E-10 の 1 文 ⑶〔もう一度〕（続きから）。
                // **取り手の生の 1 行（HTTP 404・sha256・例外）はログへ**（`v2-copy.md` §3-2 の規則）。
                Fail(
                    DownloadFailedLine + DownloadFailedWhyLine + ResumeHintLine,
                    "取得に失敗しました：" + (failed.FailureReason ?? "理由が分かりません。"));
                return false;
            }

            Record("実行系を取得しました（" + results.Count.ToString(CultureInfo.InvariantCulture) + " 件）。");
            return true;
        }
        catch (OperationCanceledException)
        {
            Fail(CancelledLine, "中断しました（続きから取り直せます）。");
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

            Fail(
                VcRedistFailedLine,
                "vc_redist の導入系がまだ組み込まれていません（便 D・取得席の実装待ち）。");
            return false;
        }

        // 憲章 §4-9＝Windows の許可の窓が出る**直前に**予告する。
        // **この 1 行は借り物である**（是正・検分）＝出入りで必ず段の 1 行へ戻す。戻していなかった
        // ころは、vc_redist の台帳が無い機体（Radeon 版・清潔導入の台本）でこの 1 行が居座り、
        // 続く W2／W3 の失敗が「Microsoft の部品を確かめています…」の脇に出た（次に書くのは
        // OnDownloadProgress だけで、1 件も注文していない回はそれが 1 度も撃たれない）。
        PhaseText = "Microsoft の部品を確かめています…";
        UacNoticeVisible = true;
        var result = await VcRedistRunner(false, progress, cancellationToken).ConfigureAwait(true);
        if (result is null)
        {
            _skipVcRedist = true;
            UacNoticeVisible = false;
            PhaseText = PhaseLine(FirstRunStep.Download);
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
                UacNoticeVisible = false;
                PhaseText = PhaseLine(FirstRunStep.Download);
                Record(
                    "Microsoft の部品を入れずに進みました。"
                    + "うまく動かないときは、はじめの準備をやり直してください。",
                    show: true);   // 文言表が画面に出すと決めた行（`v2-copy.md` §1-8 の :1105-1107）
                return true;
            }

            if (answer is null)
            {
                // 問う口が無い（または利用者が答えなかった）＝1 巡目と同じ「止まる」。
                UacNoticeVisible = false;
                Fail(VcRedistFailedLine, result.Message);
                return false;
            }

            PhaseText = "Microsoft の部品を入れています…";
            result = await VcRedistRunner(true, progress, cancellationToken).ConfigureAwait(true)
                     ?? result;
        }

        UacNoticeVisible = false;
        PhaseText = PhaseLine(FirstRunStep.Download);
        Record(result.Message);

        if (!result.Ok)
        {
            Fail(VcRedistFailedLine, result.Message);
        }

        return result.Ok;
    }

    private void OnDownloadProgress(DownloadProgress progress)
    {
        // 段の中の進みは 1 本のバーへ合成する（段ごとに 0 へ戻さない＝段 B-3）。
        _stepFraction = progress.Fraction ?? 0;
        _eta = progress.Eta;
        ApplyProgress();

        // 画面の 1 行＝いま何をしているか（数は添えない＝`v2-spec.md` §1-3）。
        PhaseText = PhaseLine(Step);

        // 速さ・件数・バイト数・回数は詳細の中だけ（`v2-spec.md` §3 段 3）。
        ProgressDetailText = Describe(progress);
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
            Record(InstallSkipLine, show: true);        // 文言表が画面に出すと決めた行
            return true;
        }

        var installer = _installer();
        var ledger = ReadLedgerFile<LedgerFile>(_paths.LedgerPath(RuntimeVariants.LedgerName(_variant)));
        if (installer is null || ledger is null)
        {
            // 台帳が読めない方は W2（`v2-spec.md` §3 の 2 本目の内部の 1 行そのもの）＝
            // 実装がまだの方だけ「まだできません。」に落とす。**どちらも内輪の 1 行はログへ**。
            if (installer is null)
            {
                Fail(NotYetLine, "展開系がまだ組み込まれていません（便 D・取得席の実装待ち）。");
            }
            else
            {
                Fail(
                    LedgerBrokenLine + "。" + LedgerBrokenWhyLine,
                    "取得台帳が読めないので展開できません。");
            }

            return false;
        }

        var runtimeDir = Path.Combine(_paths.RuntimeRoot, _variant);
        IsBusy = true;
        _cancel = new CancellationTokenSource();
        try
        {
            var progress = new Progress<InstallProgress>(p =>
            {
                _stepFraction = p.Total > 0 ? Math.Clamp((double)p.Done / p.Total, 0, 1) : 0;
                _eta = null;
                ApplyProgress();
                PhaseText = PhaseLine(FirstRunStep.Install);
                ProgressDetailText = p.Phase + "：" + (p.ItemName ?? string.Empty)
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
                // W5＝⑴ 組み立てに失敗しました。 ⑵ 平語 1 文（**展開系の生の理由はログへ**）。
                Fail(
                    InstallFailedLine + InstallFailedWhyLine,
                    "展開に失敗しました：" + (result.FailureReason ?? "理由が分かりません。"));
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
            Fail(CancelledLine, "中断しました。");
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
            Fail(NotYetLine, "モデルの取得系がまだ組み込まれていません（便 D・取得席の実装待ち）。");
            return false;
        }

        IsBusy = true;
        _cancel = new CancellationTokenSource();
        try
        {
            // モデルの段は件数の分数を持たない（進捗は 1 行の文字列）＝
            // 画面の 1 行は段の名乗りのまま、内訳だけを詳細の中で入れ替える。
            PhaseText = PhaseLine(FirstRunStep.Models);
            var progress = new Progress<string>(line => ProgressDetailText = line);
            var ok = await ModelFetcher(progress, _cancel.Token).ConfigureAwait(true);
            if (ok)
            {
                Record("モデルを取得しました。");
            }
            else
            {
                // W6＝⑴ 声のデータをダウンロードできませんでした。 ⑵ 途中で止まりました。
                Fail(ModelsFailedLine, "モデルの取得に失敗しました。");
            }

            return ok;
        }
        catch (OperationCanceledException)
        {
            Fail(CancelledLine, "中断しました。");
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
    /// <summary>
    /// <b>起こす側が持っている「なぜ止まったか」の 1 行</b>（W7＝`v2-spec.md` §3・§2-1a）。
    /// <para>
    /// 主窓が <see cref="StatusViewModel"/> の帯（<see cref="BandText.For"/> が組んだ ⑴＋⑵）を渡す。
    /// null／空＝判らない回で、そのときだけ <see cref="StartFailedLine"/>（E-12）を出す。
    /// <b>ここが帯の文を作り直さない</b>＝同じ事故に 2 通りの文を持たせない（§2-1b）。
    /// </para>
    /// <para>
    /// ⑶（〔ドライバの入れ方を見る〕等の名指しの 1 手）は<b>まだ釦にしていない</b>＝
    /// 案内を開く配線は段 F の管掌で、いまは〔もう一度〕がその席に居る（`v2-plan.md` 段 B の申し送り）。
    /// </para>
    /// </summary>
    public Func<string?>? StartFailureLine { get; set; }

    private async Task<bool> RunStartAsync()
    {
        IsBusy = true;
        _cancel = new CancellationTokenSource();
        try
        {
            PhaseText = PhaseLine(FirstRunStep.Start);
            ProgressDetailText = UiStrings.WizardVerifyingRun;
            var ok = await _startServer(_cancel.Token).ConfigureAwait(true);
            if (_cancel.IsCancellationRequested)
            {
                // 起こす側は取消を例外で返さない（IServerProcess.StartAsync の約束＝
                // ツリー kill して Stopped で返る）ので、ここで「中断」に読み替える。
                Fail(CancelledLine, "中断しました（続きから取り直せます）。");
                return false;
            }

            if (ok)
            {
                Record("使えるようになりました。");
            }
            else
            {
                // W7（`v2-spec.md` §3）＝**§2-1a の A〜D 群を共用**（同じ純関数 `BandText.For` を通す）。
                // 起こす側が持っている 1 行を受けて言い直す＝口が埋まっている（C1／D1）・
                // ドライバが下限未満（A4）・python.exe が起こせない（C3）・声の読込が落ちた（D2／D5）を
                // 「時間がかかりすぎました」と名乗らない（是正・検分＝⑶〔もう一度〕が永久に同じ所で失敗する）。
                var reason = StartFailureLine?.Invoke();
                Fail(
                    string.IsNullOrWhiteSpace(reason) ? StartFailedLine : reason.Trim(),
                    "サーバを起こせませんでした（状態タブの理由を見てください）。");
            }

            return ok;
        }
        catch (OperationCanceledException)
        {
            Fail(CancelledLine, "中断しました（続きから取り直せます）。");
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
