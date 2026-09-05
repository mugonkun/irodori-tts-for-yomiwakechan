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
using IrodoriTtsYwk.Launcher.Services.Models;
using IrodoriTtsYwk.Launcher.Services.Server;

namespace IrodoriTtsYwk.Launcher.ViewModels;

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
            settings, store, paths, AppServices.GpuEnumerator, AppServices.DriverCheck);

        About = new AboutViewModel(paths);

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
        Settings.Applied += (_, _) =>
        {
            Status.ApplySettings(settings);
            CheckRuntimeStamp();
        };

        // 捕れなかった例外を握り潰さない（§20-5 ⑴）＝「サーバ起動」が黙って何もしない、を作らない。
        Status.StartCommand.Faulted += (_, line) =>
            FailBeforeStart("サーバ起動の手が落ちました：" + line);
        Status.StopCommand.Faulted += (_, line) =>
            Status.AppendLog("サーバ停止の手が落ちました：" + line);
        Status.RebuildRuntimeCommand.Faulted += (_, line) =>
            Status.AppendLog("実行系の組み直しが落ちました：" + line);

        // 「試し撃ち」で 1 射 200＝取得キャッシュを捨ててよい合図（裁定 90 Q-E2 ⑶）。
        Try.Succeeded += (_, _) => ClearCacheAfterFirstShot();

        Voices.Reload();
        CheckRuntimeStamp();
    }

    public StatusViewModel Status { get; }

    public VoicesViewModel Voices { get; }

    public TryViewModel Try { get; }

    public SettingsViewModel Settings { get; }

    public AboutViewModel About { get; }

    /// <summary>初回取得を通していないか（窓が開いた直後にウィザードを出す判断）。</summary>
    public bool NeedsFirstRun => !_settings.FirstRunCompleted;

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

        // モデル＝変種の python.exe で server/ywk_fetch_models.py を子プロセス実行する
        // （取得席の ModelFetcher。展開が済んだ後の段なので python.exe はここで解決できる）。
        vm.ModelFetcher = async (progress, token) =>
        {
            var pythonExe = paths.ResolvePythonExe(vm.Variant);
            if (pythonExe is null)
            {
                progress.Report("変種の実行系がまだありません（展開の段をやり直してください）。");
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

        // 裁定 88 ⑶＝合成中に子が消えたら、HTTP の期限を待たずに「試す」画面へ理由 1 行。
        if (state is ServerState.Failed)
        {
            Try.NotifyServerFailed(AppServices.Server.ExitCode, reason ?? AppServices.Server.FailureReason);
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
            FailBeforeStart("変種「" + _settings.Variant + "」の実行系がまだありません（初回取得が未了です）。");
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
                gpuIndex = GpuResolver.ResolveIndex(gpus.Gpus, _settings.GpuUuid);
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
                // ここで漏らすと、トレイの「サーバ起動」（`_ = StartServerAsync()`）では
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

        // 裁定 88 ⑴⑵＝門で断られた理由 1 行と告知（「未実測の帯」など）を**そのまま**状態帯に出す。
        Status.ApplyStartOutcome(result.Ok, result.FailureReason, result.Notices);
        foreach (var notice in result.Notices)
        {
            Status.AppendLog(notice);
        }

        if (!result.Ok && result.FailureReason is string failure)
        {
            Status.AppendLog(failure);
        }

        if (result.Ok)
        {
            AttachWrapper();
            await Voices.RefreshAsync().ConfigureAwait(true);
        }

        return result.Ok;
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
    public void ApplyStatusSample(StatusResponse? status)
    {
        Status.ApplyStatus(status);
        Voices.ApplyMemory(status?.Memory);

        // 走行中で断られた焼きを、口が空いたところで出し直す（統合席 §19）。
        // 標本は見張り 1 本の物をそのまま配るだけ＝窓は HTTP を持たない（low 3）。
        Voices.ApplyPrecompute(status?.Precompute);
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
                RuntimeStamp.Burn(_settings, _paths, variant);
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
        var plan = FirstRunViewModel.TryPlan(_paths, _settings.Variant, skipVcRedist: true);
        if (plan is null)
        {
            return line;
        }

        var missing = MissingCacheRequests(plan);
        if (missing.Count == 0)
        {
            return line + "（原檔は取得キャッシュに揃っているので、取り直しはありません）";
        }

        var bytes = 0L;
        foreach (var request in missing)
        {
            bytes += request.ExpectedSize ?? 0;
        }

        return line + "（取得キャッシュに原檔が "
            + missing.Count.ToString(CultureInfo.InvariantCulture) + " 件足りません＝押すと "
            + FetchPlanner.FormatBytes(bytes) + " を取り直します）";
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
    /// 「試し撃ち」で 1 射 200 が返った後に、その変種の取得キャッシュを消す（裁定 90 Q-E2 ⑶）。
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
