using System;
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

        Status = new StatusViewModel(StartServerAsync, StopServerAsync);
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
        Settings.Applied += (_, _) => Status.ApplySettings(settings);

        Voices.Reload();
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
        var vm = new FirstRunViewModel(
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
    public async Task<bool> StartServerAsync()
    {
        var pythonExe = _paths.ResolvePythonExe(_settings.Variant);
        if (pythonExe is null)
        {
            var reason = "変種「" + _settings.Variant + "」の実行系がまだありません（初回取得が未了です）。";
            Status.AppendLog(reason);
            Status.ApplyState(ServerState.Failed, reason);
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
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                var gpus = await enumerator
                    .EnumerateAsync(new GpuEnumerationRequest(pythonExe, TimeSpan.FromSeconds(5)), cts.Token)
                    .ConfigureAwait(true);

                driverVersion = GpuEnumerator.DriverVersionOf(gpus.Gpus);
                gpuIndex = GpuResolver.ResolveIndex(gpus.Gpus, _settings.GpuUuid);
                if (gpuIndex is null && !string.IsNullOrWhiteSpace(_settings.GpuUuid))
                {
                    var notFound = GpuResolver.NotFoundMessage(_settings.GpuName, _settings.GpuUuid);
                    Status.AppendLog(notFound);

                    // **黙って cuda:0 に落とさない**（是正・2026-09-05）。列挙が 1 台でも
                    // 返っているのに保存した UUID が居ない＝別の個体を掴む危険がそこに在る。
                    // 裁定 52 と同じ流儀で「止まって告知」する。
                    if (gpus.Gpus.Count > 0)
                    {
                        Status.ApplyState(ServerState.Failed, notFound);
                        return false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Status.AppendLog("GPU の列挙が期限内に終わりませんでした（番号 0 で起こします）。");
            }
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

        var result = await AppServices.Server.StartAsync(request, CancellationToken.None)
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

    /// <summary>設定を保存した後に、状態帯へ新しい値を配る。</summary>
    public void ReapplySettings() => Status.ApplySettings(_settings);
}
