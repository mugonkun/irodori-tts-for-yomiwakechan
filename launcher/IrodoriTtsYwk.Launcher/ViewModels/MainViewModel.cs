using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Mvvm;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Ledger;
using IrodoriTtsYwk.Launcher.Services.Logging;
using IrodoriTtsYwk.Launcher.Services.Models;
using IrodoriTtsYwk.Launcher.Services.Server;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// <b>初回取得ウィザードを自動で開いてほしい</b>という求め（裁定 126 ⑽・
/// <see cref="MainViewModel.WizardRequested"/> の引数）。
/// <para>
/// 窓はこの 3 つを受けて開くだけで、<b>判断も文言も ViewModel の側に在る</b>。
/// </para>
/// </summary>
/// <param name="Notice">なぜ勝手に開いたかの 1 行（ウィザードの Trail の先頭に置く）。</param>
/// <param name="Variant">初期値にする変種（ウィザードの一覧から選んだ勧め）。</param>
/// <param name="DriverVersion">その判断に使ったドライバの版（読めていなければ null）。</param>
public sealed record FirstRunRequest(string Notice, string Variant, string? DriverVersion);

/// <summary>
/// 5 つの画面を 1 つに束ねる（<b>組み立てはここ 1 箇所</b>）。
/// <para>
/// 各画面は互いを知らない。話者一覧が変われば試し撃ちの候補と状態帯の概算メモリが変わる、
/// という<b>横の繋がりだけ</b>をここが配る（<see cref="VoicesViewModel.RowsChanged"/>）。
/// </para>
/// <para>
/// <b>窓の型に触れない</b>ので xUnit から素で作れる。スレッド跨ぎの marshal だけが窓の側に在り、
/// 窓は <see cref="ApplyStatusSample"/> と <see cref="ApplyServerState"/> を呼ぶだけである
/// （<b>窓の時計は無い</b>＝low 3・見張りは <see cref="IServerProcess"/> 1 本）。
/// </para>
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private readonly LauncherSettings _settings;
    private readonly ISettingsStore _store;
    private readonly Func<Uri, IWrapperClient>? _attachWrapper;
    private readonly Action? _detachWrapper;

    /// <summary>いちばん最後に作ったウィザード（消したバイトを Trail に残すため＝裁定 90 Q-E2 ⑶）。</summary>
    private FirstRunViewModel? _firstRun;

    /// <summary>
    /// 取得キャッシュを<b>実際に消したか</b>（1 回の起動で 1 度だけ「消す」）。
    /// <b>関門に断られた回は数えない</b>（是正・便 D（3）の 3 巡目）＝断られた理由を直して
    /// （実行系を組み直して焼き印を入れ直して）から撃ち直せば、その起動でも掃除は走る。
    /// </summary>
    private bool _cacheCleared;

    /// <summary>「実行系を組み直す」の取消（走っていなければ null）。</summary>
    private CancellationTokenSource? _rebuildCancel;

    /// <summary>
    /// ウィザードの求め（<see cref="WizardRequested"/>）を<b>もう上げたか</b>（裁定 126 ⑽）。
    /// 1 走行に 1 度だけ＝「サーバ起動」を押し直しても 2 枚目は開かない。
    /// </summary>
    private bool _wizardRequested;

    /// <summary>
    /// <b>下限に届かないと断ったときのドライバの版</b>（裁定 126 ⑽・是正・検分）。
    /// <para>
    /// <b>要る理由</b>＝断った回のウィザードを<b>取得を始めずに閉じた</b>利用者では、
    /// 檔の検分（<see cref="AcquisitionCheck"/>）は「揃っている」と答える
    /// （<c>runtime\cu130</c> もモデルも在る＝檔はドライバを知らない）。そのまま
    /// <see cref="StatusViewModel.ApplyAcquisition"/> に偽を渡すと、帯には
    /// 「CUDA 12.6 に切り替えて取得します。」の 1 行だけが残り、<b>「取得へ進む」が消える</b>＝
    /// ウィザードも 2 枚目は開かないので、⑽ が無くそうとした袋小路がそのまま戻る。
    /// </para>
    /// <para>
    /// <b>自分で消える</b>＝ウィザードが cu126（ないし cpu）を保存すれば
    /// <see cref="VariantRecommendation.IsBelowMinimum"/> が偽になり、帯は黙る。
    /// </para>
    /// </summary>
    private string? _refusedDriverVersion;

    /// <param name="paths">場所（導入先と利用者データ）。</param>
    /// <param name="settings">いまの設定（<b>この個体を全画面で共有する</b>）。</param>
    /// <param name="store">設定の書き手。</param>
    /// <param name="player">試聴と試し撃ちの再生器。</param>
    /// <param name="attachWrapper">
    /// 起きた個体を叩く口を差す手（起動席の <c>LauncherComposition.AttachWrapper</c>）。
    /// <b>ここが名前で参照しないのは</b>、ViewModel が起動席の檔に縛られないためである
    /// （渡されなければ <c>/ywk/status</c> を読まないだけで、画面は動く）。
    /// </param>
    /// <param name="detachWrapper">個体が止まったら口を閉じる手。</param>
    public MainViewModel(
        AppPaths paths,
        LauncherSettings settings,
        ISettingsStore store,
        IAudioPlayer player,
        Func<Uri, IWrapperClient>? attachWrapper = null,
        Action? detachWrapper = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(player);

        _paths = paths;
        _settings = settings;
        _store = store;
        _attachWrapper = attachWrapper;
        _detachWrapper = detachWrapper;

        Status = new StatusViewModel(
            () => StartServerAsync(CancellationToken.None),
            StopServerAsync,
            RebuildRuntimeAsync,
            CancelRebuildRuntime);

        // **画面に出た 1 行を檔にも残す**（裁定 126 の C（1））＝<データ樹>\logs\launcher-<日>.log。
        // 司令官の実射（v1.0.2 の清潔導入）では logs\ が空のままで、失敗の理由を後から読む路が
        // 1 本も無かった。書き手は投げない（LauncherLogFile が握り潰す）。
        LogFile = new LauncherLogFile(paths.LogDir);
        Status.LogSink = LogFile.Append;

        Status.ApplySettings(settings);

        Voices = new VoicesViewModel(
            AppServices.VoiceStore,
            AppServices.VoicesJsonWriter,
            player,
            static () => AppServices.Wrapper,
            paths,
            settings);

        Try = new TryViewModel(static () => AppServices.Wrapper, player, settings);

        Settings = new SettingsViewModel(
            settings, store, paths, AppServices.GpuEnumerator, AppServices.DriverCheck)
        {
            // 段 C で欄ごと消した設定（準備運転の段・使う声）の検分は、画面に出す先が無い＝
            // 手で書いた settings.json が読めない回の理由は記録へ落とす（是正・段 C の検分）。
            Log = line => Status.AppendLog(line),
        };

        About = new AboutViewModel(paths);

        // 「入れ直す」で改名（裁定 108）が起きたら、直した設定を檔へ落とし、設定画面の写しも
        // 取り直す。取り直さないと、構築時の写しを書き戻す「適用」で旧い id が甦る
        // （暖機は知らない話者で走行ごと failed＝契約 ⑺ 7-2）。
        Voices.SettingsChanged = () =>
        {
            _store.Save(_settings);
            Settings.SyncFromLive();
        };

        // 話者が変わったら、試し撃ちの候補と状態帯の概算メモリを引き直す。
        Voices.RowsChanged += (_, rows) =>
        {
            Try.SetVoices(rows);
            Status.ApplyVoices(rows);
        };

        // 選んでいる話者は状態帯の「1 名あたり」の主役（裁定 67 ⑵・low 13）。
        Voices.SelectionChanged += (_, row) => Status.ApplySelectedVoice(row);

        // 設定を「適用」したら、状態帯の GPU・変種・接続先を引き直す
        // （引き直さないと、GPU を選んで保存した直後の状態帯が「未選択」のまま残る）。
        // 変種を変えれば「実行系を組み直す」の要否も変わる＝焼き印も突き合わせ直す（裁定 91）。
        // 変種を変えれば「取得が未了か」も変わる（是正・検分）＝引き直さないと、実行系の無い
        // 変種で出た帯が、入っている変種へ替えて「適用」しても消えない（逆も出ない）。
        Settings.Applied += (_, _) =>
        {
            Status.ApplySettings(settings);
            CheckRuntimeStamp();
            RefreshAcquisition();
        };

        // 捕れなかった例外を握り潰さない（§20-5 ⑴）＝「サーバ起動」が黙って何もしない、を作らない。
        Status.StartCommand.Faulted += (_, line) =>
            FailBeforeStart("サーバ起動の手が落ちました：" + line);
        Status.StopCommand.Faulted += (_, line) =>
            Status.AppendLog("サーバ停止の手が落ちました：" + line);
        Status.RebuildRuntimeCommand.Faulted += (_, line) =>
            Status.AppendLog("実行系の組み直しが落ちました：" + line);

        // 「発話テスト」で 1 射 200＝取得キャッシュを捨ててよい合図（裁定 90 Q-E2 ⑶）。
        Try.Succeeded += (_, _) => ClearCacheAfterFirstShot();

        Voices.Reload();
        CheckRuntimeStamp();
        RefreshAcquisition();
    }

    public StatusViewModel Status { get; }

    /// <summary>状態帯の 1 行を落とす檔（裁定 126 の C（1））。路は <c>AppPaths.LogDir</c> の下。</summary>
    public LauncherLogFile LogFile { get; }

    public VoicesViewModel Voices { get; }

    public TryViewModel Try { get; }

    public SettingsViewModel Settings { get; }

    public AboutViewModel About { get; }

    /// <summary>初回取得を通していないか（窓が開いた直後にウィザードを出す判断）。</summary>
    public bool NeedsFirstRun => !_settings.FirstRunCompleted;

    /// <summary>
    /// <b>札は立っているのに実体が無い</b>（裁定 121）＝初回取得を通したことになっているのに、
    /// 実行系かモデルが揃っていない。真なら主窓は自動起動をやめてウィザードを出す。
    /// <para>
    /// <see cref="NeedsFirstRun"/> が真の回はここを偽にする＝出す口は 1 つでよい
    /// （窓は <c>NeedsFirstRun</c> を先に見る）。判断はここに置く＝WPF に触れずに試せる。
    /// </para>
    /// </summary>
    public bool NeedsAcquisition { get; private set; }

    /// <summary>足りない物の 1 行（揃っていれば空）。ウィザードの註と状態帯に<b>そのまま</b>出る。</summary>
    public string AcquisitionSummary { get; private set; } = string.Empty;

    /// <summary>
    /// <b>ウィザードを自動で開いてほしい</b>（裁定 126 ⑽）。窓だけが開けるので、
    /// ViewModel は求めを上げるだけである（<b>WPF の型に触れない</b>＝§12-2 ⑴）。
    /// この 1 走行で<b>1 度しか上がらない</b>（「サーバ起動」を押し直しても増えない）。
    /// </summary>
    public event EventHandler<FirstRunRequest>? WizardRequested;

    /// <summary>
    /// <b>実行系がまだ無い</b>ときの断りの 1 行の頭（<c>BandText</c> が前方一致で読む標識＝
    /// `v2-spec.md` §2-1a の A1。<b>綴りはここ 1 箇所</b>）。
    /// </summary>
    public const string RuntimeMissingPrefix = "変種「";

    /// <summary>
    /// <b>モデルがまだ無い</b>ときの断りの 1 行の頭（同上＝§2-1a の A2）。
    /// </summary>
    public const string ModelsMissingPrefix = "モデルがまだありません（不足＝";

    /// <summary>
    /// ウィザードを自動で出すときに Trail とログへ残す 1 行（<b>純関数</b>）。
    /// </summary>
    public static string AcquisitionNotice(string summary) =>
        UiStrings.AcquisitionRestart
        + (string.IsNullOrWhiteSpace(summary) ? string.Empty : "（" + summary.Trim() + "）");

    /// <summary>取得の済み具合を見直す（構築時と、ウィザードを閉じた後）。</summary>
    private void RefreshAcquisition()
    {
        if (NeedsFirstRun)
        {
            NeedsAcquisition = false;
            AcquisitionSummary = string.Empty;

            // **ここで断ってはいけない**（是正・段 G・high 7）＝はじめの準備を 1 度も通していない
            // 機体こそ 1 手が要る。偽を配っていたころは、ウィザードを閉じた利用者から
            // 帯の 1 手も 詳しい状態 の〔はじめの準備をする〕も同時に消えていた。
            Status.ApplyAcquisition(true);
            Status.ApplyFirstRunPending(true);
            return;
        }

        var state = AcquisitionCheck.Check(_paths, _settings);
        NeedsAcquisition = !state.Ready;
        AcquisitionSummary = state.Summary;

        // ウィザードを出さずに閉じた回でも帯に 1 手が残る（失敗を待たない）。
        // **檔が揃っていても、下限に届かない変種で断った機体では 1 手を残す**（裁定 126 ⑽・是正・
        // 検分）＝取得を始めずにウィザードを閉じた回に「取得へ進む」が消えないようにする。
        // 広げるのは<b>帯だけ</b>で <see cref="NeedsAcquisition"/> は据え置く＝窓はそちらを見て
        // 裁定 121 の別の註でウィザードを開くので、そこに混ぜると文言が食い違う。
        Status.ApplyAcquisition(
            NeedsAcquisition
            || VariantRecommendation.IsBelowMinimum(_settings.Variant, _refusedDriverVersion));
        Status.ApplyFirstRunPending(NeedsAcquisition);
    }

    /// <summary>
    /// 初回取得ウィザードの ViewModel を作る。
    /// <para>
    /// <b>モデルの取得系と vc_redist をここで差す</b>（是正・2026-09-05）。差さっていなかった
    /// ころは、ウィザードのモデルの段が毎回「実装待ち」を出して素通りし、vc_redist は
    /// 落とすだけで 1 度も走らなかった（＝素の機体で <c>torch/lib/c10.dll</c> が
    /// <c>MSVCP140.dll</c> を解決できずに起動が落ちる）。
    /// </para>
    /// </summary>
    public FirstRunViewModel CreateFirstRun()
    {
        var paths = _paths;
        var vm = _firstRun = new FirstRunViewModel(
            paths,
            _settings,
            _store,
            AppServices.DriverCheck,
            static () => AppServices.Downloader,
            static () => AppServices.RuntimeInstaller,
            StartServerAsync);

        // **ウィザードの行も同じ檔へ落とす**（裁定 126 の C（1）・是正・検分）＝切符の元は
        // 清潔導入で、そこで詰まる回（取得・展開・モデル）は状態帯を 1 度も通らない＝
        // StatusViewModel.LogSink だけでは logs\ がまさにその場合に空のままだった。
        vm.LogSink = LogFile.Append;

        // **起動の確認が通らなかった理由は帯の文をそのまま使う**（W7＝`v2-spec.md` §3・§2-1a）。
        // 起こせない事情は 1 つではない（口が埋まっている・ドライバが下限未満・子が exit 2）ので、
        // ウィザードが自前の 1 文（E-12＝時間がかかりすぎました）で全部を名乗ると嘘になり、
        // 〔もう一度〕が同じ所で永久に落ちる。文を組むのは `BandText.For` の 1 箇所だけである。
        vm.StartFailureLine = () => Status.IsFailed && Status.HasBandReason
            ? Status.BandStateText + " " + Status.BandReasonText
            : null;

        // **ドライバを見てから変種を勧める**（裁定 126 の B）＝窓が RefreshDriverAsync を呼ぶ。
        // 初回取得の時点では実行系がまだ無いので、読めるのは nvidia-smi 経路（＝NVIDIA 機の
        // driver_version）だけである。AMD 機・nvidia-smi の無い機体では「読めなかった」に落ち、
        // 止めずに 1 行だけ名乗る。
        vm.DriverProbeAsync = async token =>
        {
            if (AppServices.GpuEnumerator is not { } enumerator)
            {
                return DriverProbe.Unknown;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(6));
            var gpus = await enumerator
                .EnumerateAsync(
                    new GpuEnumerationRequest(paths.ResolvePythonExe(vm.Variant), TimeSpan.FromSeconds(5)),
                    cts.Token)
                .ConfigureAwait(true);

            // **列挙が返した理由も渡す**（是正・検分）＝「0 台」には「本当に無い」と
            // 「nvidia-smi が無い・落ちた・期限切れ」が混ざる。理由つきの 0 台を「GPU 無し」と
            // 読むと NVIDIA の機体に CPU 版を勧めてしまう（VariantRecommendation.Recommend）。
            var driver = GpuEnumerator.DriverVersionOf(gpus.Gpus);
            Settings.KnownDriverVersion = driver ?? Settings.KnownDriverVersion;

            // **製品名も渡す**（v2.0 段 B）＝「動かし方を選びました」の 1 行が名乗る名前。
            // 判定には使わない（`VariantRecommendation.Recommend` は見ない）。
            var gpuName = gpus.Gpus.Count > 0 ? gpus.Gpus[0].Name : null;

            // **会社の 2 欄も渡す**（是正・段 G・medium 12）＝勧める先は 1 字も変えないが、
            // 「版を間違えて入れた」を告げられるのはこの 2 欄が在るときだけである。
            return new DriverProbe(
                driver,
                gpus.Gpus.Count,
                Probed: true,
                gpus.FailureReason,
                gpuName,
                gpus.HasNvidiaAdapter,
                gpus.HasAmdAdapter);
        };

        // モデル＝変種の python.exe で server/ywk_fetch_models.py を子プロセス実行する
        // （取得席の ModelFetcher。展開が済んだ後の段なので python.exe はここで解決できる）。
        vm.ModelFetcher = async (progress, token) =>
        {
            var pythonExe = paths.ResolvePythonExe(vm.Variant);
            if (pythonExe is null)
            {
                progress.Report(UiStrings.NotPreparedYet);
                return false;
            }

            var fetcher = new ModelFetcher(
                pythonExe, paths.ServerDir, paths.LedgerPath("models"), paths.HfHomeDir);

            var relay = new Progress<ModelFetchEvent>(e => progress.Report(e.ForUi()));
            var result = await fetcher.FetchAsync(relay, token).ConfigureAwait(false);
            progress.Report(result.Message);
            return result.Ok;
        };

        // vc_redist＝判定（System32 の msvcp140.dll）→ 要れば取得 → sha256 → UAC で silent 実行。
        // 第 1 引数＝「判らない」でも入れる（利用者が画面で「入れる」と答えた＝裁定 87 ⑷）。
        vm.VcRedistRunner = async (assumeInstall, progress, token) =>
        {
            var downloader = AppServices.Downloader;
            if (downloader is null)
            {
                return null;
            }

            var installer = new VcRedistInstaller(downloader) { AssumeInstallWhenUnknown = assumeInstall };
            var ledger = FirstRunViewModel.ReadLedgerFile<VcRedistLedger>(paths.LedgerPath("vc_redist"));
            if (ledger?.Installer is null)
            {
                return null;
            }

            return await installer
                .EnsureAsync(ledger, paths.DownloadCacheDir, progress, token)
                .ConfigureAwait(false);
        };

        return vm;
    }

    /// <summary>
    /// 状態機械が動いた（窓が Dispatcher 越しに呼ぶ）。
    /// <b>状態を書くのはこの 1 本だけ</b>（low 3＝見張りは <see cref="IServerProcess"/> に寄せ、
    /// 窓は最新の標本を読むだけ）。
    /// </summary>
    public void ApplyServerState(ServerState state, string? reason)
    {
        Status.ApplyState(state, reason);

        // 起こした個体が台帳に居る間は「停止」を押せる（Failed でも＝是正・2026-09-05）。
        Status.HasProcess = AppServices.Server.ProcessId is not null;

        // 帯の D6 は終了コードの数を ⑵ に差す（`v2-spec.md` §2-1a）＝利用者が報告に書ける
        // 唯一の手がかりである。**内部の 1 行から掻き回して取らない**（§2-1b）ので、
        // 数を持っている側（見張り）の値をここで渡す（起こす前に断った回は null＝括弧ごと落ちる）。
        Status.ApplyExitCode(state is ServerState.Failed ? AppServices.Server.ExitCode : null);

        // 裁定 88 ⑶＝合成中に子が消えたら、HTTP の期限を待たずに「試す」画面へ理由 1 行。
        if (state is ServerState.Failed)
        {
            Try.NotifyServerFailed(AppServices.Server.ExitCode, reason ?? AppServices.Server.FailureReason);
        }

        // **口が開いてまだ載っていない＝声を読み込んでいる最中**（決裁 135 ⑴・v2.0.1）。
        // ウィザードが「起動の確認」で待っている間は、その 1 行を名乗らせる＝
        // 冷えた円盤からの 1 回目（実測 36.62 秒・段 H 射 2 では 120 秒でも足りなかった）に
        // 「動くか確かめています。」のまま固まったように見せない。
        if (state is ServerState.Listening)
        {
            _firstRun?.ReportLoadingVoices();
        }

        // 口は「起きている個体」にしか意味が無い＝listen したら開き、止まったら閉じる。
        if (state is ServerState.Listening or ServerState.Ready or ServerState.Warming)
        {
            AttachWrapper();
        }
        else if (state is ServerState.Stopped or ServerState.Failed)
        {
            ApplyStatusSample(null);
            _detachWrapper?.Invoke();
        }

        if (state is ServerState.Stopped)
        {
            Status.EndRun();
        }
    }

    private void AttachWrapper()
    {
        if (_attachWrapper is null || AppServices.Wrapper is not null)
        {
            return;
        }

        if (AppServices.Server.BaseAddress is Uri baseAddress)
        {
            _attachWrapper(baseAddress);
        }
    }

    /// <summary>stderr の 1 行（同上）。</summary>
    public void AppendLog(string? line) => Status.AppendLog(line);

    /// <summary>
    /// 起動＝材料を組んで <see cref="IServerProcess"/> に渡す。
    /// <b>ここは組み立てだけ</b>で、実際に起こすのは差された実装である。
    /// </summary>
    /// <param name="cancellationToken">
    /// 初回取得ウィザードの「中断」（<c>FirstRunViewModel</c> の最後の働く段）。
    /// <b>ここまで通さないと中断ボタンは効かない</b>（是正・便 D（3）の 3 巡目）＝
    /// ready 待ちはこの機体の設定で最長 600 秒あり、その間ずっと効かないボタンを見せて
    /// 「中断しました（続きから取り直せます）。」と嘘を名乗っていた。
    /// </param>
    public async Task<bool> StartServerAsync(CancellationToken cancellationToken = default)
    {
        var pythonExe = _paths.ResolvePythonExe(_settings.Variant);
        if (pythonExe is null)
        {
            // **断るだけで終わらせない**（裁定 121）＝どこから取れるかを同じ 1 行に書く。
            Status.ApplyMissingModels(null);
            FailBeforeStart(RuntimeMissingPrefix + _settings.Variant + "」の実行系がまだありません（初回取得が未了です）。"
                + AcquisitionCheck.Hint);
            return false;
        }

        // モデルが無い個体は起こしても HF_HUB_OFFLINE=1 で「モデルの読込に失敗＝…」になる
        // （司令官の報告・2026-09-10）。起こす前に、何が足りないかと取り方を告げて断る。
        var acquisition = AcquisitionCheck.Check(_paths, _settings);
        if (!acquisition.ModelsReady)
        {
            Status.ApplyMissingModels(acquisition.MissingModelFiles.Count);
            FailBeforeStart(
                ModelsMissingPrefix
                + AcquisitionCheck.Describe(acquisition.MissingModelFiles) + "）。"
                + AcquisitionCheck.Hint);
            return false;
        }

        // GPU は UUID で保存し、起動のたびに index へ解決する（裁定 34・受け入れ条件 D-2）。
        int? gpuIndex = null;

        // **変種の門の入力**（裁定 88 ⑴⑵・起動席 §16-5 ⑴ の申し送り）。
        // 載せないと門は env の `YWK_VARIANT`（cu130 と cu126 が `cuda` に畳まれた名）で
        // 代用する＝⒜ 勧める先が cpu に落ち ⒝ 閾は厳しい方（cu130 の 580.00）が掛かり
        // ⒞ 検分が読めなければ起こさない、という粗い判断になる（是正・便 D（2）＝
        // 1 巡目の註は「勧める変種が cpu に落ちる」だけだったが、実際は畳んだ名に閾が
        // 1 つも無く、ドライバ 500.00 でも通っていた）。だから 3 つとも載せる。
        string? driverVersion = null;

        // **この起動で実際に使う GPU**（裁定 126 の C（3）＝settings に UUID が無ければ焼く）。
        GpuInfo? resolvedGpu = null;

        if (RuntimeVariants.UsesGpu(_settings.Variant) && AppServices.GpuEnumerator is { } enumerator)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(6));
                var gpus = await enumerator
                    .EnumerateAsync(new GpuEnumerationRequest(pythonExe, TimeSpan.FromSeconds(5)), cts.Token)
                    .ConfigureAwait(true);

                driverVersion = GpuEnumerator.DriverVersionOf(gpus.Gpus);

                // **設定頁にもドライバの版を渡す**（裁定 126 の B・是正・検分）＝設定頁は
                // 「数え直す」を押すまで GPU を 1 台も持たないので、押さない利用者には
                // 下限未満の変種が素通りで「適用」できた（＝次の起動を門が断る形が残った）。
                Settings.KnownDriverVersion = driverVersion ?? Settings.KnownDriverVersion;

                gpuIndex = GpuResolver.ResolveIndex(gpus.Gpus, _settings.GpuUuid);

                // 焼くのは「いま解決した個体」＝保存した UUID が居ればそれ、
                // まだ 1 度も選んでいなければ列挙の先頭（＝番号 0 で起こす個体）。
                resolvedGpu = GpuResolver.Find(gpus.Gpus, _settings.GpuUuid)
                    ?? (string.IsNullOrWhiteSpace(_settings.GpuUuid) ? gpus.Gpus.FirstOrDefault() : null);

                if (gpuIndex is null && !string.IsNullOrWhiteSpace(_settings.GpuUuid))
                {
                    var notFound = GpuResolver.NotFoundMessage(_settings.GpuName, _settings.GpuUuid);

                    // **黙って cuda:0 に落とさない**（是正・2026-09-05）。列挙が 1 台でも
                    // 返っているのに保存した UUID が居ない＝別の個体を掴む危険がそこに在る。
                    // 裁定 52 と同じ流儀で「止まって告知」する。
                    if (gpus.Gpus.Count > 0)
                    {
                        FailBeforeStart(notFound);
                        return false;
                    }

                    Status.AppendLog(notFound);
                }
            }
            catch (OperationCanceledException)
            {
                Status.AppendLog("GPU の列挙が期限内に終わりませんでした（番号 0 で起こします）。");
            }
            catch (Exception ex)
            {
                // **列挙が落ちても起動の判断は続ける**（是正・便 D（3）＝§20-5 ⑴）。
                // ここで漏らすと、投げ捨てで撃つ「サーバ起動」（`_ = StartServerAsync()`）では
                // 誰も捕らずに消える＝押しても何も起きない。門は「読めなかった検分」として
                // 扱えるので（cu130 は起こさない・cu126／rocm は注意 1 行）、理由を残して先へ進む。
                Status.AppendLog("GPU の列挙が落ちました：" + AsyncRelayCommand.Describe(ex));
            }
        }

        // 列挙の途中で中断されたら、そのまま起こさない（列挙の期限切れと同じ路に落とさない）。
        if (cancellationToken.IsCancellationRequested)
        {
            Status.AppendLog("起動を中止しました。");
            return false;
        }

        // 帯の理由 1 行はドライバの版を名指す（`v2-spec.md` §2-1a の B 群）＝読めた値をここで渡す。
        // **内部の 1 行から掻き回して取らない**（§2-1b）ので、判っている側が渡すのが筋である。
        //
        // **勧める先もここで渡す**（是正・検分）＝門の断り（B1／B3）はこの下の StartAsync の中で
        // 起きるので、null を渡していたころは帯の ⑶ が本番では必ず「設定の詳細を開く」に落ち、
        // §2-1a が求める〔CUDA 12.6 で準備しなおす〕には**絶対にならなかった**。
        // 勧める先が今の動かし方と同じときだけ null＝いま断られた当人を勧めない。
        Status.ApplyDriverFacts(driverVersion, RecommendedAlternative(driverVersion));

        // **下限に届かない変種で保存されている機体を、失敗の理由だけで放り出さない**（裁定 126 ⑽）。
        if (RefuseBelowMinimumVariant(driverVersion))
        {
            return false;
        }

        Status.BeginRun(_settings);

        // 組み立ては 1 箇所（ServerLaunchPlan）に寄せる＝門の入力 3 つを窓が落とさない。
        var request = ServerLaunchPlan.Build(
            _settings,
            _paths,
            pythonExe,
            gpuIndex,
            offlineHuggingFace: true,
            driverVersion: driverVersion,
            installedVariants: VariantGate.DetectInstalled(_paths.LedgerDir));

        Status.AppendLog("起動：" + request.BaseAddress + "（変種 " + _settings.Variant + "）");

        var result = await AppServices.Server.StartAsync(request, cancellationToken)
            .ConfigureAwait(true);

        Status.HasProcess = AppServices.Server.ProcessId is not null;

        // 裁定 88 ⑴⑵＝門の告知（「未実測の帯」など）を**そのまま**状態帯に出す。
        // **画面は利用者の言葉・記録は工学の 1 行**（是正・段 C の検分＝`v2-copy.md` §1-8 の
        // `VariantGate.cs:102-165`「画面に出す文とログに落とす文を分ける」）＝
        // 出すのは `Notices`、落とすのは `NoticeTrail`（検分の観測と実行系の在否）。
        Status.ApplyStartOutcome(result.Ok, result.FailureReason, result.Notices);
        foreach (var line in result.NoticeTrail)
        {
            Status.AppendLog(line);
        }

        if (!result.Ok && result.FailureReason is string failure)
        {
            Status.AppendLog(failure);
        }

        if (result.Ok)
        {
            PersistResolvedGpu(resolvedGpu);
            AttachWrapper();
            await Voices.RefreshAsync().ConfigureAwait(true);
        }

        return result.Ok;
    }

    /// <summary>
    /// <b>ドライバの下限に届かない変種は起こさず、取得へ連れて行く</b>（裁定 126 ⑽）。
    /// <para>
    /// <b>なぜ要るか</b>（実射・2026-09-10＝RTX 3090・ドライバ 537.58）＝v1.0.2 の頃に
    /// <c>cu130</c> で取得を通した機体には <c>settings.json</c> に <c>variant=cu130</c> と
    /// <c>runtime\cu130</c> だけが残る。v1.1.0（裁定 126 の B）はウィザードと設定頁で
    /// 下限未満の変種を選ばせなくなったが、<b>もう保存されている</b>その値は誰も直さない＝
    /// 利用者が見るのは「起こせません」の 1 行だけで、そこから設定→ cu126 →取得へ、を
    /// 自分で見つけるほかなかった。<b>この製品は利用者を導く TTS の器</b>なので、
    /// 断ると同時に⑴ 理由 1 行 ⑵ 帯の「取得が未了です。」＋「取得へ進む」
    /// ⑶ ウィザードを 1 度だけ自動で開く求め（<see cref="WizardRequested"/>）を出す。
    /// </para>
    /// <para>
    /// <b>この枝に落ちるのは下限未満の 1 つだけ</b>＝GPU が見つからない・検分が読めない等の
    /// 門の断り（<see cref="Services.Gpu.VariantGate"/>）は今まで通り理由 1 行で終える。
    /// ドライバの版が読めなかった機体も同じ＝<see cref="VariantRecommendation.IsBelowMinimum"/> は
    /// 「読めない」を「駄目」の側に倒さない。
    /// </para>
    /// </summary>
    /// <returns>真＝断った（呼ぶ側はここで起動をやめる）。</returns>
    private bool RefuseBelowMinimumVariant(string? driverVersion)
    {
        if (!VariantRecommendation.IsBelowMinimum(_settings.Variant, driverVersion))
        {
            return false;
        }

        // 勧める先は**ウィザードが出す一覧と同じ物**から選ぶ（＝開いた先の初期値と食い違わない）。
        var ledgerNames = ReleaseFlavors.LedgerNames(_paths.LedgerDir);
        var choices = ReleaseFlavors.AvailableChoices(ReleaseFlavors.Detect(ledgerNames), ledgerNames);
        var recommended = VariantRecommendation.Recommend(
            choices, new DriverProbe(driverVersion, 1, Probed: true));

        var reason = VariantRecommendation.StartRefusalReason(choices, _settings.Variant, driverVersion)
            ?? string.Empty;

        // 断った版を覚える＝ウィザードを取得せずに閉じても帯の 1 手を消さない（是正・検分）。
        _refusedDriverVersion = driverVersion;

        // 帯の ⑵ と ⑶ が名指す「切り替えれば動く動かし方」＝勧める先を渡す（§2-1a A4）。
        // **勧める先が自分自身なら null**＝A4' の「このパソコンで使える別の動かし方がありません」
        // へ落ちる（ウィザードへ渡す先は従来どおり `recommended` のまま＝振る舞いは変えない）。
        Status.ApplyDriverFacts(driverVersion, RecommendedAlternative(driverVersion));

        // ⑴⑵＝理由 1 行を状態帯とログ（LauncherLogFile）へ流し、状態機械を Failed にする。
        FailBeforeStart(reason);

        // ⑶＝帯の取得への 1 手（裁定 121 と同じ仕掛け）。理由は上の 1 行がそのまま出ている。
        Status.ApplyAcquisition(true);

        // ⑷＝ウィザードは**この起動で 1 度だけ**（押し直しで何枚も開かない）。
        if (!_wizardRequested)
        {
            _wizardRequested = true;
            WizardRequested?.Invoke(this, new FirstRunRequest(reason, recommended, driverVersion));
        }

        return true;
    }

    /// <summary>
    /// <b>切り替えれば動く動かし方</b>（`v2-spec.md` §2-1a の A4／B1／B3 の ⑵⑶ が名指す先）。
    /// <para>
    /// 勧める先は<b>ウィザードが出す一覧と同じ物</b>から選ぶ（＝開いた先の初期値と食い違わない）。
    /// <b>いまの動かし方と同じときは null</b>＝帯が「いま断られた当人で準備しなおす」と言わない。
    /// </para>
    /// </summary>
    private string? RecommendedAlternative(string? driverVersion)
    {
        var ledgerNames = ReleaseFlavors.LedgerNames(_paths.LedgerDir);
        var choices = ReleaseFlavors.AvailableChoices(ReleaseFlavors.Detect(ledgerNames), ledgerNames);
        var recommended = VariantRecommendation.Recommend(
            choices, new DriverProbe(driverVersion, 1, Probed: true));

        return string.Equals(recommended, _settings.Variant, StringComparison.Ordinal)
            ? null
            : recommended;
    }

    /// <summary>
    /// <b>起きた個体が実際に掴んだ GPU を設定へ焼く</b>（裁定 126 の C（3））。
    /// <para>
    /// <b>なぜ要るか</b>（司令官の実射・2026-09-10＝v1.0.2 の清潔導入）＝設定頁を 1 度も開かずに
    /// ウィザードだけで通した機体では <c>gpuUuid</c>／<c>gpuName</c> が <c>null</c> のまま残った。
    /// 状態帯には GPU が出る（走っている個体が名乗る）のに設定には無い＝⑴ 2 台目の GPU を挿した日に
    /// 「前回の GPU」が判らない ⑵ 引き渡しの settings.json から機体が読み取れない。
    /// </para>
    /// <para>
    /// <b>上書きはしない</b>＝既に UUID が入っている設定には触らない（利用者の選択が正本）。
    /// <b>書くのは起動が通った回だけ</b>＝断られた変種の GPU を焼かない。
    /// </para>
    /// </summary>
    private void PersistResolvedGpu(GpuInfo? gpu)
    {
        if (gpu is null
            || !RuntimeVariants.UsesGpu(_settings.Variant)
            || !string.IsNullOrWhiteSpace(_settings.GpuUuid))
        {
            return;
        }

        Services.Settings.SettingsDefaults.ApplyGpu(_settings, gpu);
        _store.Save(_settings);
        Status.ApplySettings(_settings);
        Settings.SyncFromLive();
        Status.AppendLog("この起動で使う GPU を設定に覚えました（" + gpu.Label + "）。");
    }

    /// <summary>
    /// <b>起こす前に断った</b>（事前検査で止めた）＝理由 1 行を状態機械へ通してから画面へ配る。
    /// <para>
    /// 直に <c>Status.ApplyState(Failed, …)</c> と書いていたころは（是正・便 D（3）・low 6 の ⑷）
    /// <see cref="IServerProcess.State"/> が <c>Stopped</c> のままだったので、状態機械と画面が
    /// 食い違った（窓を開き直すと理由が消える・「サーバ起動」がまた押せる）。
    /// 窓が居る実行時は <c>StateChanged</c> が Dispatcher 経由でもう 1 度この配りを起こすが、
    /// <see cref="ApplyServerState"/> は同じ値で 2 度呼んでも同じ結果になる。
    /// </para>
    /// </summary>
    private void FailBeforeStart(string reason)
    {
        Status.AppendLog(reason);
        AppServices.Server.ReportPreflightFailure(reason);

        // 機械が正本＝その現在値を配る（窓の無い席では誰も運ばないのでここで配る）。
        ApplyServerState(AppServices.Server.State, AppServices.Server.FailureReason ?? reason);
    }

    /// <summary>停止＝ツリー kill（上限つき）。</summary>
    public async Task StopServerAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await AppServices.Server.StopAsync(cts.Token).ConfigureAwait(true);
            Status.AppendLog("停止しました。");
        }
        catch (OperationCanceledException)
        {
            Status.AppendLog("停止の待ちが期限切れになりました。");
        }
        finally
        {
            Status.HasProcess = AppServices.Server.ProcessId is not null;
            Status.EndRun();
        }
    }

    /// <summary>
    /// <c>/ywk/status</c> の標本を受ける（low 3＝<b>窓は読むだけ</b>）。
    /// <para>
    /// 叩くのは <see cref="IServerProcess"/> の見張り 1 本で、ここは配るだけである
    /// （<b>状態は動かさない</b>＝<c>Warming</c> の出入りも状態機械の仕事）。
    /// null＝口が無い・止まっている＝画面は「未対応」に落ちる。
    /// </para>
    /// </summary>
    /// <param name="status">見張りが採った <c>/ywk/status</c>（null＝止まっている・口が無い）。</param>
    /// <param name="osGpuMemory">
    /// 同じ回に見張りが採った OS の GPU 計数（裁定 110）。窓は
    /// <c>IServerProcess.LatestOsGpuMemory</c> を読んで渡すだけ＝<b>ここも叩かない</b>。
    /// </param>
    public void ApplyStatusSample(
        StatusResponse? status, IReadOnlyList<OsGpuMemoryRow>? osGpuMemory = null)
    {
        Status.ApplyStatus(status, osGpuMemory);
        Voices.ApplyMemory(status?.Memory);

        // 走行中で断られた焼きを、口が空いたところで出し直す（統合席 §19）。
        // 標本は見張り 1 本の物をそのまま配るだけ＝窓は HTTP を持たない（low 3）。
        Voices.ApplyPrecompute(status?.Precompute);

        // 本体が読み上げに使っている間は〔しゃべらせる〕を譲る（決裁 130 Q4）。
        // **同じ 2 秒の標本を配るだけ**＝新しい問い合わせは 1 本も足さない。
        // 標本が無い回（止まっている・口が無い）は偽＝押せない理由を残さない。
        Try.HostBusy = status?.HostBusy == true;

        // 帯の連携の 1 行も**同じ標本**から組む（§2-1c・是正・段 G・medium 2／9）。
        // **濾したあとの値を渡す**＝`Try.HostBusy` の setter が自分の射を引いてから返すので、
        // ランチャ自身の〔しゃべらせる〕を「読み分けちゃん2 が使っています」と読まない。
        // 欄の在否は `Requests` が null かどうかで判る（古い個体は欄ごと無い＝§2-1c）。
        Status.ApplyHost(status?.Requests is not null, Try.HostBusy);
    }

    /// <summary>
    /// 初回取得ウィザードを閉じた後に、各画面へ新しい値を配る。
    /// <b>設定画面の写しも取り直す</b>（是正・便 D（3）の 3 巡目・high）＝ウィザードは
    /// この個体（変種・同意・焼き印・完了の札）を書き換えるのに、設定画面が握っている写しは
    /// 構築時のままだった。取り直さないと、設定頁で「適用」を 1 度押すだけで巻き戻る。
    /// </summary>
    public void ReapplySettings()
    {
        Settings.SyncFromLive();
        Status.ApplySettings(_settings);
        CheckRuntimeStamp();
        RefreshAcquisition();
    }

    // ---- 裁定 91＝展開に使った台帳の焼き印 ------------------------------------

    /// <summary>
    /// 焼き印（<c>runtimeLedgerSha256</c>／<c>installedAppVersion</c>）と配布樹の台帳を突き合わせ、
    /// 食い違ったら状態帯に 1 行と 1 手を出す（合っていれば消す）。
    /// <para>
    /// <b>焼き印が 1 つも無い樹は、いま在る台帳で焼き直して黙る</b>（裁定 91）＝
    /// 便 D（2）以前に組んだ樹・台本（<c>build/assemble-runtime.ps1</c>）で組んだ樹には
    /// 焼き印が無い。そこで「判らないから組み直せ」と急かすと、正しく組んである機体まで
    /// 4 GB の展開をやり直させることになる。**いま在る物をその台帳の産物として受け入れ**、
    /// <b>次に台帳が動いた日から</b>検知できる状態にするのが、この欄の値打ちである。
    /// </para>
    /// </summary>
    public void CheckRuntimeStamp()
    {
        var variant = _settings.Variant;
        var installed = _paths.ResolvePythonExe(variant) is not null;
        var current = RuntimeStamp.LedgerSha256(_paths, variant);

        if (installed && current is not null && string.IsNullOrWhiteSpace(_settings.RuntimeLedgerFor(variant)))
        {
            // **焼き印を無条件に押さない**（是正・便 D（3）の 3 巡目）＝根拠が「python.exe が在る」
            // だけだったころは、python-embed の直後で切れた樹（python.exe 1 檔）にも今の台帳の
            // sha256 を焼いていた。以後その樹は「この台帳から出来ている」と名乗り、取得
            // キャッシュの関門は<b>自分で作った値</b>と突き合わせるだけなので通り、壊れた樹を
            // 直すのに要る原檔（実射＝107 檔）が消えた。展開の件数で締めてから焼く。
            var ledger = FirstRunViewModel.ReadLedgerFile<LedgerFile>(
                _paths.LedgerPath(RuntimeVariants.LedgerName(variant)));

            if (RuntimeStamp.LooksComplete(_paths, ledger, variant))
            {
                // **写しは置かない**（是正・2026-09-11・medium 5）＝ここで受け入れているのは
                // 「*.dist-info の件数が合う樹」であって、**何で組んだかは判っていない**
                // （v1.1.0 で組んだ樹・build/assemble-runtime.ps1 で組んだ樹）。
                // 焼き印だけなら「催促を止める」で済むが、写しを置くのは「この樹の中身は全部この
                // 内容である」と名乗る行為で、それは嘘になりうる＝次の版の差分がその嘘を信じ、
                // 本当は古い wheel を見送って混ざった樹を残す。写しが**無い**回は差分が組めず、
                // その 1 回だけ丸ごとになる＝素性の知れない樹には、それが正しい。
                // 規則の正本は RuntimeStamp.Burn の註（writeAppliedLedger）。
                RuntimeStamp.Burn(_settings, _paths, variant, writeAppliedLedger: false);
                _store.Save(_settings);
            }
            else
            {
                // 焼かずに 1 手を出す＝裁定 91 の「正しく組んである機体に 4 GB をやり直させない」は
                // 満たしたまま、組みかけの樹だけを拾う。
                Status.ApplyRuntimeStamp(WithRefetchSize(RuntimeStamp.IncompleteLine(variant)));
                return;
            }
        }

        var verdict = RuntimeStamp.Compare(
            _settings.RuntimeLedgerFor(variant),
            _settings.InstalledAppVersionFor(variant),
            current,
            AppVersion.Display,
            installed,
            variant);

        if (verdict.AppVersionChanged && !verdict.LedgerChanged)
        {
            // 台帳は同じで版だけ動いた＝**焼き直して黙る**（展開はしない）。焼き直さないと
            // installedAppVersion が永久に古いまま残り、毎起動この 1 行を見せることになる。
            RuntimeStamp.BurnAppVersion(_settings, variant);
            _store.Save(_settings);
            if (verdict.Note is not null)
            {
                Status.AppendLog(verdict.Note);
            }
        }
        else if (verdict.LedgerChanged
                 && !string.Equals(
                     _settings.InstalledAppVersionFor(variant)?.Trim(),
                     AppVersion.Display,
                     StringComparison.Ordinal))
        {
            // **版だけは先に焼き直す**（是正・2026-09-11・low 9）＝台帳も一緒に動いた回
            // （v1.1.0 → v2.0.0 がまさにそれ）は上の枝に入らないので、組み直しを後回しにしている
            // 間ずっと installedAppVersion が古いまま残る。それを見て「版が変わった回だけ」と
            // 構えている同梱の声の突き合わせ（LauncherComposition の PresetSync＝12 檔・35 MB 級）が
            // **毎起動**走ってしまう。焼き直すのは版の欄だけで、内容の側（sha256）は古いまま＝
            // 状態帯の 1 行と 1 手は消えない（催促は続く）。
            RuntimeStamp.BurnAppVersion(_settings, variant);
            _store.Save(_settings);
        }

        Status.ApplyRuntimeStamp(verdict.Line is null ? null : WithRefetchSize(verdict.Line));
    }

    /// <summary>
    /// 「実行系を組み直す」の 1 行に<b>押したら何 GiB 取り直すか</b>を添える（裁定 90 との相性）。
    /// <para>
    /// 裁定 90 の自動削除で cache は空なのが常態なので、この 1 手は<b>押した瞬間に数 GiB の
    /// 再取得</b>になる。押す前に量が読めなければ、利用者は代金を知らずに押す
    /// （是正・便 D（3）の 3 巡目）。台帳が読めなければ何も足さない。
    /// </para>
    /// </summary>
    private string WithRefetchSize(string line)
    {
        // **差分で済む回は差分の代金を告げる**（是正・2026-09-11・low 8）＝丸ごとの計画で値を付けると、
        // 実際には数百 MB しか落とさない更新に「押すと 4.2 GiB を取り直します」と書くことになり、
        // この 1 行が在る理由（押す前に代金を告げる）と正反対になる。
        // 切ってある回・丸ごとへ落ちる回は、これまでどおり全体の計画で値を付ける。
        var plan = DifferentialFetchPlan()
            ?? FirstRunViewModel.TryPlan(_paths, _settings.Variant, skipVcRedist: true);
        if (plan is null)
        {
            return line;
        }

        var missing = MissingCacheRequests(plan);
        if (missing.Count == 0)
        {
            return line + "（" + UiStrings.RefetchNothingToDo + "）";
        }

        var bytes = 0L;
        foreach (var request in missing)
        {
            bytes += request.ExpectedSize ?? 0;
        }

        return line + "（" + UiStrings.RefetchMissingHead
            + missing.Count.ToString(CultureInfo.InvariantCulture)
            + UiStrings.RefetchMissingMiddle + UiText.RoundedGigabytes(bytes)
            + UiStrings.RefetchMissingTail + "）";
    }

    /// <summary>
    /// <b>いま押したら差分で済むか</b>＝済むならその取得計画（済まないなら null）。
    /// <see cref="WithRefetchSize"/> と <see cref="TryDifferentialAsync"/> が<b>同じ 4 つの条件</b>を
    /// 見る（値と行いを食い違わせない）。<b>檔は 1 つも書かない。</b>
    /// </summary>
    private FetchPlan? DifferentialFetchPlan()
    {
        if (!_settings.DifferentialUpdate)
        {
            return null;
        }

        var variant = _settings.Variant;
        if (_paths.ResolvePythonExe(variant) is null)
        {
            return null;
        }

        var ledger = FirstRunViewModel.ReadLedgerFile<LedgerFile>(
            _paths.LedgerPath(RuntimeVariants.LedgerName(variant)));
        if (ledger is null)
        {
            return null;
        }

        if (!RuntimeStamp.LooksComplete(_paths, ledger, variant))
        {
            return null; // 途中で切れている樹＝差分の相手にならない（丸ごとが拾う）
        }

        var diff = RuntimeDiff.Plan(RuntimeStamp.ReadAppliedLedger(_paths, variant), ledger);
        return diff.RebuildAll ? null : RuntimeDiff.ToDifferentialRun(variant, diff).Plan;
    }

    /// <summary>
    /// 「実行系を組み直す」（裁定 91）＝<b>cache から再展開し、cache に原檔が無ければ取得から</b>。
    /// <para>
    /// 走っている個体は先に降ろす（展開先の <c>site-packages</c> を掴んだまま書き換えない）。
    /// 通ったら焼き印を入れ直し、状態帯の 1 行を消す。
    /// </para>
    /// </summary>
    public async Task RebuildRuntimeAsync()
    {
        using var cancel = new CancellationTokenSource();
        _rebuildCancel = cancel;
        Status.BeginRebuild();
        try
        {
            await RebuildRuntimeCoreAsync(cancel.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status.AppendLog("実行系の組み直しをやめました（取得キャッシュの原檔は残ります）。");
        }
        finally
        {
            _rebuildCancel = null;
            Status.EndRebuild();
        }
    }

    /// <summary>走っている組み直しをやめる（<see cref="StatusViewModel.CancelRebuildCommand"/>）。</summary>
    public void CancelRebuildRuntime()
    {
        try
        {
            _rebuildCancel?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 既に終わっていた＝やめる物が無い
        }
    }

    private async Task RebuildRuntimeCoreAsync(CancellationToken cancellationToken)
    {
        var installer = AppServices.RuntimeInstaller;
        var ledger = FirstRunViewModel.ReadLedgerFile<LedgerFile>(
            _paths.LedgerPath(RuntimeVariants.LedgerName(_settings.Variant)));
        if (installer is null || ledger is null)
        {
            Status.AppendLog(installer is null
                ? "展開系が組み込まれていないので、実行系を組み直せません。"
                : "取得台帳が読めないので、実行系を組み直せません。");
            return;
        }

        if (AppServices.Server.ProcessId is not null)
        {
            await StopServerAsync().ConfigureAwait(true);
        }

        // ⑴ **差分で済むならそちら**（段 E の RuntimeDiff・段 F が配線した＝憲章 §4-24）。
        // 済まない回（設定が OFF・写しが無い・6 割超が動いた・締めが通らない）は下の丸ごとへ落ちる。
        if (await TryDifferentialAsync(ledger, cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        var plan = FirstRunViewModel.TryPlan(_paths, _settings.Variant, skipVcRedist: true, out var reason);
        if (plan is null)
        {
            Status.AppendLog("取得台帳が読めないので、実行系を組み直せません"
                + (reason is null ? "。" : "（" + reason + "）。"));
            return;
        }

        // ⑴ cache に原檔が揃っていなければ取得から（取得系が無ければそこで止まる）。
        var missing = MissingCacheRequests(plan);
        if (missing.Count > 0)
        {
            var downloader = AppServices.Downloader;
            if (downloader is null)
            {
                Status.AppendLog("取得キャッシュに原檔が "
                    + missing.Count.ToString(CultureInfo.InvariantCulture)
                    + " 件足りませんが、取得系が組み込まれていません。");
                return;
            }

            var missingBytes = 0L;
            foreach (var request in missing)
            {
                missingBytes += request.ExpectedSize ?? 0;
            }

            Status.AppendLog("取得キャッシュに原檔が "
                + missing.Count.ToString(CultureInfo.InvariantCulture) + " 件（"
                + FetchPlanner.FormatBytes(missingBytes) + "）足りないので取り直します。");

            var progress = new Progress<DownloadProgress>(p =>
            {
                Status.ApplyRebuildProgress(FirstRunViewModel.Describe(p), p.Fraction ?? 0);
                Status.AppendLog(FirstRunViewModel.Describe(p));
            });
            var results = await downloader
                .DownloadAllAsync(missing, progress, cancellationToken)
                .ConfigureAwait(true);

            if (results.FirstOrDefault(static r => !r.Ok) is { } failed)
            {
                Status.AppendLog("取得に失敗しました：" + (failed.FailureReason ?? "理由が分かりません。"));
                return;
            }
        }

        // ⑵ 展開（変種ディレクトリは展開系が組み直す）。
        var runtimeDir = _paths.ResolveRuntimeDir(_settings.Variant)
            ?? System.IO.Path.Combine(_paths.RuntimeRoot, _settings.Variant);
        var install = await installer
            .InstallAsync(
                new InstallRequest(
                    ledger, _paths.DownloadCacheDir, runtimeDir, _paths.AppDir, _paths.PthTemplatePath),
                new Progress<InstallProgress>(p =>
                {
                    var line = p.Phase + "：" + (p.ItemName ?? string.Empty)
                        + "　" + UiText.Progress(p.Done, p.Total);
                    Status.ApplyRebuildProgress(
                        line, p.Total > 0 ? Math.Clamp((double)p.Done / p.Total, 0, 1) : 0);
                    Status.AppendLog(line);
                }),
                cancellationToken)
            .ConfigureAwait(true);

        if (!install.Ok)
        {
            Status.AppendLog("実行系を組み直せませんでした："
                + (install.FailureReason ?? "理由が分かりません。"));
            return;
        }

        RuntimeStamp.Burn(_settings, _paths, _settings.Variant);
        _store.Save(_settings);
        Status.AppendLog("実行系を組み直しました（"
            + install.Files.ToString(CultureInfo.InvariantCulture) + " 檔・"
            + UiText.Bytes(install.Bytes) + "）。");
        CheckRuntimeStamp();
    }

    /// <summary>
    /// <b>新しくなった分だけ取り直す</b>（設定 › 詳細 の 6 行目・<c>v2-spec.md</c> §11-3）。
    /// <para>
    /// 段 E が道具（<see cref="RuntimeDiff"/>）を作り、段 F がここで更新の道に結んだ。
    /// <b>差分を選ぶ条件は 4 つとも揃ったときだけ</b>＝⑴ 設定が ON ⑵ いま動く一式が在る
    /// ⑶ 展開に使った台帳の写し（<c>.ledger.json</c>）が在る ⑷ 計画が丸ごとへ落ちていない。
    /// 1 つでも欠けたら<b>偽を返す</b>＝呼び手はそのまま丸ごと入れ直しへ進む。
    /// </para>
    /// <para>
    /// <b>締めを必ず撃つ</b>（<see cref="RuntimeDiff.VerifyAfterApply"/>）＝差分の回は
    /// <c>RemovePartialOnFailure=false</c> で構えるので、途中で落ちた機体は新旧が混ざった樹で残る。
    /// 締めが通らなければ<b>その場で偽を返し</b>、丸ごと入れ直しに拾わせる。
    /// <c>.ledger.json</c> は<b>締めが通ってから</b>置く（嘘の写しを次の版に信じさせない）。
    /// </para>
    /// </summary>
    /// <returns>差分で決着したか（偽＝丸ごと入れ直しへ落ちる）。</returns>
    private async Task<bool> TryDifferentialAsync(
        LedgerFile ledger, CancellationToken cancellationToken)
    {
        if (!_settings.DifferentialUpdate)
        {
            return false;
        }

        var variant = _settings.Variant;
        if (_paths.ResolvePythonExe(variant) is null)
        {
            return false; // まだ一式が無い＝初回の道（差分の相手が居ない）
        }

        if (!RuntimeStamp.LooksComplete(_paths, ledger, variant))
        {
            // **途中で切れている樹には当てない**（是正・2026-09-11）＝写しが残っていると計画は組めるが、
            // 当てた先で締めが必ず落ちる＝丸ごとへ行く前に樹をもう一度いじるだけである。
            // 値を告げる側（WithRefetchSize）も同じ条件を見るので、代金と行いが食い違わない。
            return false;
        }

        var diff = RuntimeDiff.Plan(RuntimeStamp.ReadAppliedLedger(_paths, variant), ledger);
        if (diff.RebuildAll)
        {
            Status.AppendLog(UiStrings.DifferentialFallsBack
                + (diff.RebuildReason is null ? string.Empty : "（" + diff.RebuildReason + "）"));
            return false;
        }

        if (diff.UpToDate)
        {
            // **落とす物も入れ替える物も無い**（是正・2026-09-11・medium 6）＝持ち物の一覧は
            // 別の檔になったが、中身（item と内容の突合）は 1 件も動いていない回である
            // （作り直した日付が動いただけ、など）。ここを素通りさせると 0 件の当て込みが失敗し、
            // 見出しが動いただけの更新に数 GiB を払わせることになる。
            // この回は樹が本当に新しい内容と合っているので、写しも置いてよい。
            Status.AppendLog(UiStrings.DifferentialNothingToDo);
            RuntimeStamp.Burn(_settings, _paths, variant);
            _store.Save(_settings);
            CheckRuntimeStamp();
            return true;
        }

        var downloader = AppServices.Downloader;
        var installer = AppServices.RuntimeInstaller;
        if (installer is null || (diff.Any && downloader is null))
        {
            return false;
        }

        var (fetch, differential) = RuntimeDiff.ToDifferentialRun(variant, diff);
        Status.ApplyRebuildProgress(UiStrings.DifferentialStarting, 0);
        Status.AppendLog(UiStrings.DifferentialStarting
            + (RuntimeDiff.DownloadNotice(diff.Bytes) ?? string.Empty));
        if (RuntimeDiff.RemovedLine(diff.Removed) is string removed)
        {
            Status.AppendLog(removed);
        }

        // ⑴ 落とす（cache に在る原檔はそのまま使う＝MissingCacheRequests が間引く）。
        var missing = MissingCacheRequests(fetch);
        if (missing.Count > 0)
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                Status.ApplyRebuildProgress(FirstRunViewModel.Describe(p), p.Fraction ?? 0);
                Status.AppendLog(FirstRunViewModel.Describe(p));
            });
            var results = await downloader!
                .DownloadAllAsync(missing, progress, cancellationToken)
                .ConfigureAwait(true);
            if (results.FirstOrDefault(static r => !r.Ok) is { } failed)
            {
                Status.AppendLog("取得に失敗しました：" + (failed.FailureReason ?? "理由が分かりません。"));
                return false;
            }
        }

        // ⑵ 変わった分だけを当てる（既存の樹は消さない＝ToDifferentialRun が構えた展開器）。
        var runtimeDir = _paths.ResolveRuntimeDir(variant)
            ?? System.IO.Path.Combine(_paths.RuntimeRoot, variant);
        var install = await differential
            .InstallAsync(
                new InstallRequest(
                    new LedgerFile { Items = diff.Fetch },
                    _paths.DownloadCacheDir,
                    runtimeDir,
                    _paths.AppDir,
                    _paths.PthTemplatePath),
                new Progress<InstallProgress>(p =>
                {
                    var line = p.Phase + "：" + (p.ItemName ?? string.Empty)
                        + "　" + UiText.Progress(p.Done, p.Total);
                    Status.ApplyRebuildProgress(
                        line, p.Total > 0 ? Math.Clamp((double)p.Done / p.Total, 0, 1) : 0);
                    Status.AppendLog(line);
                }),
                cancellationToken)
            .ConfigureAwait(true);

        // ⑶ 締め＝*.dist-info の件数が配布樹の台帳と合うか。合わなければ丸ごとへ落とす。
        if (!install.Ok || !RuntimeDiff.VerifyAfterApply(_paths, ledger, variant))
        {
            // **ここまで来た回は樹に手を入れている**（是正・2026-09-11・medium 4）＝
            // 当て込みは檔を書きながら進むので、落ちた地点によっては新旧が混ざっている。
            // ⑴ 画面には「途中で止まった＝丸ごと入れ直す」と出す（「変わった量が多い」ではない）
            // ⑵ **写しを落とす**＝混ざる前の姿を名乗ったまま残ると、次の回の差分がその嘘を信じて
            //    混ざりを温存する。落としておけば次は必ず丸ごとから始まる。
            Status.AppendLog(UiStrings.DifferentialApplyFailed
                + (install.FailureReason is null ? string.Empty : "（" + install.FailureReason + "）"));
            RuntimeStamp.RemoveAppliedLedger(_paths, variant);
            return false;
        }

        RuntimeStamp.Burn(_settings, _paths, variant);
        _store.Save(_settings);
        Status.AppendLog(UiStrings.DifferentialDone + "（"
            + install.Files.ToString(CultureInfo.InvariantCulture) + " 檔・"
            + UiText.Bytes(install.Bytes) + "）");

        // 声のデータ（モデル）の差分は**見積りだけ**を記録に残す（取得の道は既存のまま＝§11-4）。
        var models = ModelDiff.PlanAgainstApplied(_paths);
        if (ModelDiff.Summary(models) is string summary)
        {
            Status.AppendLog(summary);
        }

        CheckRuntimeStamp();
        return true;
    }

    /// <summary>cache に居ない（または長さが合わない）原檔の注文だけ（<b>純関数に近い</b>）。</summary>
    private IReadOnlyList<DownloadRequest> MissingCacheRequests(FetchPlan plan) =>
        [.. FetchPlanner.ToDownloadRequests(plan, _paths.DownloadCacheDir)
            .Where(static r =>
            {
                try
                {
                    var info = new System.IO.FileInfo(r.DestinationPath);
                    return !info.Exists || (r.ExpectedSize is long size && info.Length != size);
                }
                catch (System.IO.IOException)
                {
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    return true;
                }
            })];

    // ---- 裁定 90 Q-E2 ⑶＝取得キャッシュの削除 ---------------------------------

    /// <summary>
    /// 「発話テスト」で 1 射 200 が返った後に、その変種の取得キャッシュを消す（裁定 90 Q-E2 ⑶）。
    /// <para>
    /// <b>消すのは初回取得を通した後の 1 度だけ</b>（<c>firstRunCompleted</c> が真＝
    /// 「起動の確認」が通っている）。消したバイトは<b>ログとウィザードの Trail</b>の両方に残す。
    /// 手で消したいときは設定画面の「取得キャッシュを消す」（<see cref="SettingsViewModel"/>）。
    /// </para>
    /// <b>差し口は <see cref="TryViewModel.Succeeded"/> の 1 本</b>（public なのは釘のため）。
    /// </summary>
    public void ClearCacheAfterFirstShot()
    {
        if (_cacheCleared || !_settings.FirstRunCompleted)
        {
            return;
        }

        var result = CacheCleaner.Clean(
            _paths.DownloadCacheDir,
            _paths.ResolvePythonExe(_settings.Variant) is not null,
            _settings.RuntimeLedgerFor(_settings.Variant),
            RuntimeStamp.LedgerSha256(_paths, _settings.Variant),
            ProtectedCacheNames());

        // **札は結末の後に立てる**（是正・便 D（3）の 3 巡目）＝関門に断られた回を
        // 「1 度やった」と数えると、断られた理由を直して撃ち直しても、その起動では
        // 二度と掃除が走らない（受け入れ条件のサイズ行は自動の掃除を前提に書き直された）。
        if (result.Ok)
        {
            _cacheCleared = true;
        }

        Status.AppendLog(result.Message);
        _firstRun?.Note(result.Message);
    }

    /// <summary>
    /// 掃除で<b>残す</b>原檔の名（<see cref="CacheCleaner.ProtectedFileNames"/>）＝
    /// まだ関門を通していない変種<b>だけ</b>が名指す檔。
    /// </summary>
    public IReadOnlyCollection<string> ProtectedCacheNames() =>
        CacheCleaner.ProtectedFileNames(
            _paths,
            _settings,
            CacheCleaner.LedgerVariants(ReleaseFlavors.LedgerNames(_paths.LedgerDir)),
            variant => FirstRunViewModel.TryPlan(_paths, variant, skipVcRedist: true));
}
