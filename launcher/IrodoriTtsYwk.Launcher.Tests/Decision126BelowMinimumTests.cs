using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Server;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 裁定 126 ⑽＝<b>下限に届かない変種で保存されている機体を、断りの 1 行で放り出さない</b>。
/// <para>
/// <b>元になった姿</b>（実射・2026-09-10＝RTX 3090・ドライバ 537.58）＝v1.0.2 で <c>cu130</c> の
/// まま取得を通した機体には <c>settings.json</c> の <c>variant=cu130</c> と <c>runtime\cu130</c> が
/// 残る。v1.1.0（裁定 126 の B）はウィザードと設定頁で下限未満の変種を<b>選ばせなく</b>なったが、
/// もう保存されている値は誰も直さないので、利用者が見るのは失敗の 1 行だけだった。
/// </para>
/// <para>
/// <b>実機・実 GPU・実ポート・外への取得には 1 つも触れない</b>＝子プロセスは 1 つも起こさない。
/// </para>
/// </summary>
[Collection(AppServicesCollection.Name)]
public sealed class Decision126BelowMinimumTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-126-below-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>起こそうとされた回数を数えるだけの偽の子プロセス（子は 1 つも起こさない）。</summary>
    private sealed class CountingServer : IServerProcess
    {
        public int Starts { get; private set; }

        public string? PreflightReason { get; private set; }

        public ServerState State { get; private set; } = ServerState.Stopped;

        public int? ProcessId => Starts > 0 ? 1234 : null;

        public int? ExitCode => null;

        public string? FailureReason { get; private set; }

        public ServerLogEvent? Banner => null;

        public Uri? BaseAddress => null;

        public StatusResponse? LatestStatus => null;

        public IReadOnlyList<OsGpuMemoryRow> LatestOsGpuMemory => [];

#pragma warning disable CS0067 // 偽物なので誰も上げない
        public event EventHandler<ServerStateChangedEventArgs>? StateChanged;

        public event EventHandler<ServerLogLineEventArgs>? LogLine;

        public event EventHandler<StatusResponse>? StatusSampled;
#pragma warning restore CS0067

        public Task<ServerStartResult> StartAsync(
            ServerStartRequest request, CancellationToken cancellationToken)
        {
            Starts++;
            State = ServerState.Ready;
            FailureReason = null;
            return Task.FromResult(new ServerStartResult(
                true, ServerState.Ready, 1234, null, TimeSpan.FromSeconds(1), null));
        }

        public void ReportPreflightFailure(string reason)
        {
            PreflightReason = reason;
            State = ServerState.Failed;
            FailureReason = reason;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>列挙の返しを固定する（<c>driver_version</c> はここが決める）。</summary>
    private sealed class FixedEnumerator(params GpuInfo[] gpus) : IGpuEnumerator
    {
        public Task<GpuEnumerationResult> EnumerateAsync(
            GpuEnumerationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new GpuEnumerationResult(
                gpus, GpuSource.NvidiaSmi, TimeSpan.FromMilliseconds(40), null));
    }

    private sealed class SilentPlayer : IAudioPlayer
    {
        public bool IsPlaying => false;

        public PlaybackResult Play(byte[] wav) => new(true, 1.0, null, null);

        public PlaybackResult PlayFile(string path) => new(true, 1.0, null, null);

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }

    /// <summary>実射の機体（RTX 3090・ドライバ 537.58＝cu130 の下限 580.00 に届かない）。</summary>
    private static GpuInfo Rtx3090(string driverVersion) => new(
        "GPU-abcdef12-3456-7890-abcd-ef1234567890", "NVIDIA GeForce RTX 3090",
        0, 24L * 1024 * 1024 * 1024, "1", null, driverVersion, GpuSource.NvidiaSmi);

    /// <summary>取得の済んだ CUDA 版の樹（モデルは在り・名指した変種の実行系だけ在る）。</summary>
    private AppPaths MakeTree(string variant)
    {
        var paths = Ruling121Tree.MakePaths(_root);
        Directory.CreateDirectory(paths.LedgerDir);
        Ruling121Tree.MakeModelsLedger(paths);
        Ruling121Tree.MakeModelCache(paths);
        Ruling121Tree.MakeRuntime(paths, variant);

        // 配布物の取得台帳＝選択肢は cu130／cu126／cpu（ウィザードが出す一覧と同じ物）。
        foreach (var name in new[] { RuntimeVariants.Cu130, RuntimeVariants.Cu126, RuntimeVariants.Cpu })
        {
            File.WriteAllText(
                paths.LedgerPath(RuntimeVariants.LedgerName(name)),
                """
                {"schema":1,"name":"runtime","items":[
                  {"kind":"wheel","name":"torch","url":"https://example.invalid/t.whl",
                   "sha256":"bb","size":1000}]}
                """,
                new UTF8Encoding(false));
        }

        return paths;
    }

    private MainViewModel NewMain(AppPaths paths, LauncherSettings settings) =>
        new(paths, settings, new JsonSettingsStore(paths.SettingsPath), new SilentPlayer());

    /// <summary>ウィザードの求めを数える（窓の代わり）。</summary>
    private static List<FirstRunRequest> Watch(MainViewModel main)
    {
        var seen = new List<FirstRunRequest>();
        main.WizardRequested += (_, request) => seen.Add(request);
        return seen;
    }

    public void Dispose()
    {
        AppServices.Server = new NullServerProcess();
        AppServices.GpuEnumerator = null;

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 掃除は best effort
        }
    }

    [Fact]
    public async Task ドライバ537_58のcu130は起こさずに取得へ導く()
    {
        var paths = MakeTree(RuntimeVariants.Cu130);
        var server = new CountingServer();
        AppServices.Server = server;
        AppServices.GpuEnumerator = new FixedEnumerator(Rtx3090("537.58"));

        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cu130,
            Port = 18099,
            FirstRunCompleted = true,
        };

        var main = NewMain(paths, settings);
        var requests = Watch(main);

        Assert.False(await main.StartServerAsync());

        // ⑴ 起こしていない。
        Assert.Equal(0, server.Starts);

        // ⑵ ドライバ・断った変種・勧める先の 3 つが 1 行に出る（画面にも檔にも）。
        Assert.Contains("537.58", main.Status.LogText, StringComparison.Ordinal);
        Assert.Contains("CUDA 13.0 は動きません", main.Status.LogText, StringComparison.Ordinal);
        Assert.Contains("CUDA 12.6 に切り替えて取得します", main.Status.LogText, StringComparison.Ordinal);
        Assert.Contains(
            "CUDA 12.6 に切り替えて取得します",
            File.ReadAllText(main.LogFile.CurrentPath, new UTF8Encoding(false)),
            StringComparison.Ordinal);

        // ⑶ 状態帯＝失敗の理由＋「取得が未了です。」＋「取得へ進む」。
        Assert.Equal(ServerState.Failed, main.Status.State);
        Assert.Contains("CUDA 13.0 は動きません", main.Status.Reason!, StringComparison.Ordinal);
        Assert.True(main.Status.AcquisitionNeeded);
        Assert.Equal(StatusViewModel.AcquisitionLine, main.Status.AcquisitionText);

        // ⑷ ウィザードの求めは 1 度・勧める変種と理由が乗っている。
        var request = Assert.Single(requests);
        Assert.Equal(RuntimeVariants.Cu126, request.Variant);
        Assert.Equal("537.58", request.DriverVersion);
        Assert.Contains("CUDA 13.0 は動きません", request.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 押し直してもウィザードは2枚目を開かない()
    {
        var paths = MakeTree(RuntimeVariants.Cu130);
        AppServices.Server = new CountingServer();
        AppServices.GpuEnumerator = new FixedEnumerator(Rtx3090("537.58"));

        var main = NewMain(
            paths,
            new LauncherSettings
            {
                Variant = RuntimeVariants.Cu130,
                Port = 18099,
                FirstRunCompleted = true,
            });

        var requests = Watch(main);

        Assert.False(await main.StartServerAsync());
        Assert.False(await main.StartServerAsync());   // 利用者が「サーバ起動」を押し直した

        Assert.Single(requests);                       // 2 枚目は開かない
        Assert.True(main.Status.AcquisitionNeeded);    // 帯の 1 手は出たまま
    }

    [Fact]
    public async Task 取得せずにウィザードを閉じても取得への1手は残る()
    {
        // 是正・検分＝閉じた後の見直し（MainWindow.ShowFirstRun → ReapplySettings）で
        // 檔の検分は「揃っている」と答える（runtime\cu130 もモデルも在る＝檔はドライバを
        // 知らない）。そこで帯の 1 手まで消すと、失敗の 1 行だけが残り、ウィザードも
        // 2 枚目は開かない＝⑽ が無くそうとした袋小路がそのまま戻る。
        var paths = MakeTree(RuntimeVariants.Cu130);
        AppServices.Server = new CountingServer();
        AppServices.GpuEnumerator = new FixedEnumerator(Rtx3090("537.58"));

        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cu130,
            Port = 18099,
            FirstRunCompleted = true,
        };

        var main = NewMain(paths, settings);

        Assert.False(await main.StartServerAsync());
        Assert.True(main.Status.AcquisitionNeeded);

        main.ReapplySettings();

        Assert.False(main.NeedsAcquisition);            // 檔は揃っている
        Assert.True(main.Status.AcquisitionNeeded);     // それでも帯の 1 手は残る
        Assert.Equal(StatusViewModel.AcquisitionLine, main.Status.AcquisitionText);

        // ウィザードが cu126 を保存して取得を通せば、覚えは自分で効かなくなる。
        Ruling121Tree.MakeRuntime(paths, RuntimeVariants.Cu126);
        settings.Variant = RuntimeVariants.Cu126;
        main.ReapplySettings();

        Assert.False(main.Status.AcquisitionNeeded);
    }

    [Fact]
    public async Task ドライバ581のcu130はそのまま起こす()
    {
        var paths = MakeTree(RuntimeVariants.Cu130);
        var server = new CountingServer();
        AppServices.Server = server;
        AppServices.GpuEnumerator = new FixedEnumerator(Rtx3090("581.29"));

        var main = NewMain(
            paths,
            new LauncherSettings
            {
                Variant = RuntimeVariants.Cu130,
                Port = 18099,
                FirstRunCompleted = true,
            });

        var requests = Watch(main);

        Assert.True(await main.StartServerAsync());
        Assert.Equal(1, server.Starts);
        Assert.Empty(requests);
        Assert.False(main.Status.AcquisitionNeeded);
    }

    [Fact]
    public async Task ドライバ537_58のcu126はそのまま起こす()
    {
        // cu126 の下限は 528.33＝537.58 は届いている（未実測の帯でもない）。
        var paths = MakeTree(RuntimeVariants.Cu126);
        var server = new CountingServer();
        AppServices.Server = server;
        AppServices.GpuEnumerator = new FixedEnumerator(Rtx3090("537.58"));

        var main = NewMain(
            paths,
            new LauncherSettings
            {
                Variant = RuntimeVariants.Cu126,
                Port = 18099,
                FirstRunCompleted = true,
            });

        var requests = Watch(main);

        Assert.True(await main.StartServerAsync());
        Assert.Equal(1, server.Starts);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task ドライバが読めない機体では今まで通り()
    {
        // 「見ていない」を「駄目」の側に倒さない＝列挙が 1 台も返さない機体（AMD・
        // nvidia-smi が無い）では断らないし、ウィザードも開かない。
        var paths = MakeTree(RuntimeVariants.Cu130);
        var server = new CountingServer();
        AppServices.Server = server;
        AppServices.GpuEnumerator = new FixedEnumerator();

        var main = NewMain(
            paths,
            new LauncherSettings
            {
                Variant = RuntimeVariants.Cu130,
                Port = 18099,
                FirstRunCompleted = true,
            });

        var requests = Watch(main);

        Assert.True(await main.StartServerAsync());
        Assert.Equal(1, server.Starts);
        Assert.Empty(requests);
    }

    [Fact]
    public void 断りの1行は下限と勧める先を名乗る()
    {
        // 純関数（VariantRecommendation）＝ウィザードの断り（「選んでください」）と
        // 主窓の断り（「切り替えて取得します」）は同じ頭を分け合う。
        string[] choices = [RuntimeVariants.Cu130, RuntimeVariants.Cu126, RuntimeVariants.Cpu];

        var line = VariantRecommendation.StartRefusalReason(choices, RuntimeVariants.Cu130, "537.58");

        Assert.NotNull(line);
        Assert.Contains("このドライバ（537.58）では CUDA 13.0 は動きません", line!, StringComparison.Ordinal);
        Assert.Contains("下限 " + DriverRequirement.Cu130Minimum, line!, StringComparison.Ordinal);
        Assert.Contains("CUDA 12.6 に切り替えて取得します", line!, StringComparison.Ordinal);

        // 届いている変種・版が読めない機体では断る理由が無い。
        Assert.Null(VariantRecommendation.StartRefusalReason(choices, RuntimeVariants.Cu126, "537.58"));
        Assert.Null(VariantRecommendation.StartRefusalReason(choices, RuntimeVariants.Cu130, null));
    }
}

/// <summary>
/// 裁定 126 ⑽＝主窓が渡した初期値とウィザード自身の検分が<b>食い違わない</b>こと。
/// </summary>
public sealed class Decision126PreselectTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-126-pre-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 掃除は best effort
        }
    }

    private AppPaths MakeTree()
    {
        var appDir = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(appDir, "ledger"));
        Directory.CreateDirectory(Path.Combine(appDir, "licenses"));
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        var paths = new AppPaths(
            Path.Combine(_root, "install"),
            appDir,
            Path.Combine(_root, "runtime"),
            Path.Combine(_root, "data"),
            developerMode: true);

        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文", new UTF8Encoding(false));
        foreach (var variant in new[] { RuntimeVariants.Cu130, RuntimeVariants.Cu126, RuntimeVariants.Cpu })
        {
            File.WriteAllText(
                paths.LedgerPath(RuntimeVariants.LedgerName(variant)),
                """
                {"schema":1,"name":"runtime","items":[
                  {"kind":"wheel","name":"torch","url":"https://example.invalid/t.whl",
                   "sha256":"bb","size":1000}]}
                """,
                new UTF8Encoding(false));
        }

        return paths;
    }

    private FirstRunViewModel NewWizard(AppPaths paths, LauncherSettings settings, DriverProbe probe)
    {
        var vm = new FirstRunViewModel(
            paths,
            settings,
            new JsonSettingsStore(paths.SettingsPath),
            new DriverRequirement(),
            static () => null,
            static () => null,
            static _ => Task.FromResult(true));

        vm.DriverProbeAsync = _ => Task.FromResult(probe);
        return vm;
    }

    [Fact]
    public async Task 渡された初期値と自分の検分は同じ答えになる()
    {
        var paths = MakeTree();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130 };
        var vm = NewWizard(paths, settings, new DriverProbe("537.58", 1, Probed: true));

        vm.Note("このドライバ（537.58）では CUDA 13.0 は動きません（下限 580.00）。"
                + "CUDA 12.6 に切り替えて取得します。");
        vm.Preselect(RuntimeVariants.Cu126, "537.58");

        // 窓が検分を撃つ前から、註と選びが揃っている（＝開いた瞬間に矛盾しない）。
        Assert.Equal(RuntimeVariants.Cu126, vm.Variant);
        Assert.False(vm.VariantBlocked);
        Assert.Equal("537.58", vm.DriverVersion);
        Assert.Contains(vm.Trail, line => line.Contains("CUDA 12.6", StringComparison.Ordinal));

        // ウィザード自身の検分が来ても答えは変わらない。
        await vm.RefreshDriverAsync();
        Assert.Equal(RuntimeVariants.Cu126, vm.Variant);
        Assert.False(vm.VariantBlocked);
    }

    [Fact]
    public async Task 自分の検分が版を読めなくても渡された版は残る()
    {
        // 是正・検分＝ウィザードの検分は主窓のより弱い（実行系がまだ無い変種の python.exe しか
        // 渡せない＝nvidia-smi の 1 本だけ）。期限切れ・一時の失敗で版が消えると
        // Recommend は「判らない」に落ちて並びの先頭＝断られた cu130 を勧め直し、
        // 註（「CUDA 12.6 に切り替えて取得します。」）と選びが食い違う。
        var paths = MakeTree();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130 };
        var vm = NewWizard(
            paths, settings, new DriverProbe(null, 0, Probed: true, "nvidia-smi が見つかりません"));

        vm.Note("このドライバ（537.58）では CUDA 13.0 は動きません（下限 580.00）。"
                + "CUDA 12.6 に切り替えて取得します。");
        vm.Preselect(RuntimeVariants.Cu126, "537.58");

        await vm.RefreshDriverAsync();

        Assert.Equal("537.58", vm.DriverVersion);
        Assert.Equal(RuntimeVariants.Cu126, vm.Variant);   // 断られた変種へ戻らない
        Assert.False(vm.VariantBlocked);

        // cu130 を選び直せば、渡された版で断れる（捨てていれば素通りしてしまう）。
        vm.Variant = RuntimeVariants.Cu130;
        Assert.True(vm.VariantBlocked);
    }

    [Fact]
    public async Task 自分の検分が新しい版を読めばそちらが勝つ()
    {
        // 上書きはしない＝読めた回は必ずウィザード自身の版で判断する。
        var paths = MakeTree();
        var vm = NewWizard(paths, new LauncherSettings(), new DriverProbe("591.86", 1, Probed: true));

        vm.Preselect(RuntimeVariants.Cu126, "537.58");
        await vm.RefreshDriverAsync();

        Assert.Equal("591.86", vm.DriverVersion);
        Assert.Equal(RuntimeVariants.Cu130, vm.Variant);
    }

    [Fact]
    public async Task 利用者が選び直せばそちらが残る()
    {
        var paths = MakeTree();
        var vm = NewWizard(paths, new LauncherSettings(), new DriverProbe("591.86", 1, Probed: true));

        vm.Preselect(RuntimeVariants.Cu130, "591.86");
        vm.Variant = RuntimeVariants.Cpu;          // 利用者が自分で選んだ

        await vm.RefreshDriverAsync();             // 勧めで上書きしない（裁定 4）

        Assert.Equal(RuntimeVariants.Cpu, vm.Variant);
    }

    [Fact]
    public void 一覧に無い名は無視する()
    {
        var paths = MakeTree();
        var vm = NewWizard(paths, new LauncherSettings(), DriverProbe.Unknown);

        vm.Preselect(RuntimeVariants.RocmGfx1151, "537.58");   // CUDA 版の樹には無い

        Assert.Equal(RuntimeVariants.Cu130, vm.Variant);       // 並びの先頭のまま
    }
}
