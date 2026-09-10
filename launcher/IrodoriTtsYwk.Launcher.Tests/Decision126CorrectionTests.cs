using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Logging;
using IrodoriTtsYwk.Launcher.Services.Server;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 裁定 126 の<b>是正</b>（検分 2 席の所見・2026-09-10）。
/// <para>
/// 直した穴は 6 つ＝⑴ 終わりかけの個体に 2 個目の起動がぶつかると何も出ない
/// ⑵ 起こしている最中に止められた子が台帳に載らず生き残る ⑶ 設定頁がドライバを 1 度も読まない
/// ⑷ 列挙が落ちた「0 台」を GPU 無しと読んで CPU 版を勧める ⑸ ウィザードの行が檔に残らない
/// ⑹ 失敗の札が立った変種の段が門を飛ばす。
/// </para>
/// <para><b>実機・実 GPU・外への取得には触れない</b>（子は <c>cmd.exe</c> 1 個体だけ）。</para>
/// </summary>
public sealed class Decision126ExitRelaunchTests
{
    [Fact]
    public void 合図が届かなかった2個目は錠を待つ()
    {
        // 終了の路は合図の口を先に閉じる＝2 個目の OpenExisting は落ちる。
        // そこで黙って退くと「押しても何も出ない」＝裁定 124 の × ＝終了の直後がまさにこれ。
        Assert.True(ShutdownSequence.SecondInstanceShouldWait(signalReached: false));

        // 届いた＝1 個目は生きている＝窓が前に出るので、こちらは退いてよい。
        Assert.False(ShutdownSequence.SecondInstanceShouldWait(signalReached: true));
    }

    [Fact]
    public void 錠を待つ上限は後始末の上限より長い()
    {
        // 後始末（ツリー kill）を待ち切る前に待ちが切れると、2 個目がまた退くことになる。
        Assert.True(ShutdownSequence.SecondInstanceWait > ShutdownSequence.DefaultStopTimeout);
    }
}

/// <summary>裁定 126 の A＝<b>起こしている最中に止められた子を残さない</b>（VRAM とポート）。</summary>
public sealed class Decision126StartRaceTests
{
    private static readonly string[] CudaRelease =
        [RuntimeVariants.Cu130, RuntimeVariants.Cu126, RuntimeVariants.Cpu];

    private static TorchGpuProbe.TorchProbeResult CudaProbe() => new(
        [], "2.10.0+cu126", "12.6", null, null, true, 1);

    [Fact]
    public async Task 起こしている最中に止められた子は台帳に載らず自分で落ちる()
    {
        // 窓を閉じた利用者の路＝App.OnExit → StopAsync（ツリー kill）。門の検分（実路では
        // `python -c import torch`＝秒）に入っている間に来ると、StopAsync は _process が
        // まだ null なので**何も殺さずに帰る**。是正前はその後に生まれた子が「誰も畳まない台帳」に
        // 載って親より長生きした＝VRAM を握り 18088 を塞ぐ（＝裁定 124 が終わらせたい形）。
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var server = new ServerProcess(
            new FreePort(),
            new NeverReachable(),
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(50),
            new BlockingProbe(entered, release.Task, CudaProbe()),
            _ => Cmd("ping -n 30 127.0.0.1 >nul"));

        var request = Request(18099, RuntimeVariants.Cu126) with { DriverVersion = "530.00" };
        var start = server.StartAsync(request, CancellationToken.None);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await server.StopAsync(CancellationToken.None);   // ここで止められた（子はまだ居ない）
        release.SetResult();

        var result = await start.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(result.Ok);
        Assert.Equal(ServerState.Stopped, result.State);
        Assert.Equal("起動を中止しました。", result.FailureReason);
        Assert.Null(server.ProcessId);
        Assert.Equal(ServerState.Stopped, server.State);
    }

    [Fact]
    public async Task 止められていなければ子はそのまま台帳に載る()
    {
        // 上の見直しが**普通の起動を壊していない**ことの釘（ready には届かない子＝期限で失敗する）。
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        release.SetResult();

        await using var server = new ServerProcess(
            new FreePort(),
            new NeverReachable(),
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(50),
            new BlockingProbe(entered, release.Task, CudaProbe()),
            _ => Cmd("ping -n 30 127.0.0.1 >nul"));

        var request = Request(18099, RuntimeVariants.Cu126) with { DriverVersion = "530.00" };
        var result = await server.StartAsync(request, CancellationToken.None);

        Assert.False(result.Ok);                                  // ready には誰も答えない
        Assert.NotEqual("起動を中止しました。", result.FailureReason);
        Assert.Equal(ServerState.Failed, result.State);
    }

    private static ServerStartRequest Request(int port, string variant) =>
        new(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Path.GetTempPath(),
            "127.0.0.1",
            port,
            new Dictionary<string, string>(StringComparer.Ordinal),
            TimeSpan.FromMilliseconds(300))
        {
            Variant = variant,
            InstalledVariants = CudaRelease,
        };

    private static ProcessStartInfo Cmd(string command)
    {
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            WorkingDirectory = Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add("/c");
        info.ArgumentList.Add(command);
        return info;
    }

    private sealed class FreePort : IPortProbe
    {
        public bool IsFree(string host, int port) => true;
    }

    private sealed class NeverReachable : IReadinessProbe
    {
        public Task<ReadinessSample> ProbeAsync(Uri baseAddress, CancellationToken cancellationToken) =>
            Task.FromResult(new ReadinessSample(false, false, false, null, "誰も居ません。"));
    }

    /// <summary>門の検分に入ったことを知らせ、合図が来るまで返らない検分。</summary>
    private sealed class BlockingProbe(
        TaskCompletionSource entered, Task release, TorchGpuProbe.TorchProbeResult result) : ITorchProbe
    {
        public async Task<TorchGpuProbe.TorchProbeResult> ProbeAsync(
            string pythonExe, TimeSpan timeout, CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.ConfigureAwait(false);
            return result;
        }
    }
}

/// <summary>
/// 裁定 126 の B＝<b>「0 台」を理由で読み分ける</b>（列挙が落ちた機体に CPU 版を勧めない）。
/// </summary>
public sealed class Decision126ProbeReasonTests
{
    private static readonly string[] CudaChoices =
        [RuntimeVariants.Cu130, RuntimeVariants.Cu126, RuntimeVariants.Cpu];

    /// <summary>初回取得の時点で実行系がまだ無いときに列挙が返す 1 行（逐語）。</summary>
    private const string NoSmi =
        "GPU を列挙できませんでした（nvidia-smi が無く、実行系もまだ取得できていません）。";

    [Fact]
    public void 理由つきの0台はGPU無しと読まない()
    {
        // 是正前＝nvidia-smi が無い・落ちた・期限切れの回も 0 台で返るので、NVIDIA の機体が
        // 黙って CPU 版（数百分の一の速さ・約 1 GB）を落とすことになっていた。
        var probe = new DriverProbe(null, 0, Probed: true, NoSmi);

        Assert.Equal(RuntimeVariants.Cu130, VariantRecommendation.Recommend(CudaChoices, probe));
    }

    [Fact]
    public void 見た上での0台はこれまでどおりcpu()
    {
        var probe = new DriverProbe(null, 0, Probed: true);

        Assert.Equal(RuntimeVariants.Cpu, VariantRecommendation.Recommend(CudaChoices, probe));
    }

    [Fact]
    public void 検分の1行は結果ごとに言い分ける()
    {
        // ⑴ 見た上で 0 台＝もう CPU を選んでいるのだから「CPU を選べ」とは言わない。
        Assert.Equal(
            VariantRecommendation.NoGpuNote,
            VariantRecommendation.ProbeNote(new DriverProbe(null, 0, Probed: true)));

        // ⑵ 落ちた＝読めなかったと名乗り、**列挙が返した理由も添える**（捨てない）。
        var note = VariantRecommendation.ProbeNote(new DriverProbe(null, 0, Probed: true, NoSmi));
        Assert.NotNull(note);
        Assert.Contains(VariantRecommendation.UnknownDriverNote, note!, StringComparison.Ordinal);
        Assert.Contains(NoSmi, note!, StringComparison.Ordinal);

        // ⑶ 版が読めた回・まだ撃っていない回は黙る。
        Assert.Null(VariantRecommendation.ProbeNote(new DriverProbe("537.58", 1, Probed: true)));
        Assert.Null(VariantRecommendation.ProbeNote(DriverProbe.Unknown));
        Assert.Null(VariantRecommendation.ProbeNote(null));
    }
}

/// <summary>裁定 126 の B・C（1）＝ウィザード側の是正 3 つ。</summary>
public sealed class Decision126WizardCorrectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-126c-wiz-" + Guid.NewGuid().ToString("N")[..8]);

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

    /// <summary>CUDA 版の配布樹（選択肢＝cu130／cu126／cpu）。</summary>
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
        File.WriteAllText(
            paths.LedgerPath("python-embed"),
            """
            {"schema":1,"name":"python-embed","items":[
              {"kind":"python-embed","name":"python-embed","url":"https://example.invalid/p.zip",
               "sha256":"aa","size":100}]}
            """,
            new UTF8Encoding(false));

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

    private static FirstRunViewModel NewWizard(
        AppPaths paths, LauncherSettings settings, DriverProbe probe, ISettingsStore? store = null)
    {
        var vm = new FirstRunViewModel(
            paths,
            settings,
            store ?? new JsonSettingsStore(paths.SettingsPath),
            new DriverRequirement(),
            static () => null,
            static () => null,
            static _ => Task.FromResult(true));

        vm.DriverProbeAsync = _ => Task.FromResult(probe);
        return vm;
    }

    [Fact]
    public async Task 列挙が落ちた機体をCPUへ倒さない()
    {
        // 司令官の機体で起きうる形＝初回取得の時点では python がまだ無く、nvidia-smi も
        // 落ちる／期限切れになると 0 台で返る。是正前はこれで cpu が選ばれ、画面には
        // 「起動できないときは CPU の変種を選んでください」＝もう選んでいる、と出ていた。
        var paths = MakeTree();
        var vm = NewWizard(
            paths,
            new LauncherSettings(),
            new DriverProbe(null, 0, Probed: true, "nvidia-smi を起こせませんでした。"));

        await vm.RefreshDriverAsync();

        Assert.Equal(RuntimeVariants.Cu130, vm.Variant);          // 並びの先頭のまま
        Assert.False(vm.VariantBlocked);
        Assert.Contains("nvidia-smi を起こせませんでした。", vm.DriverText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 失敗の札が立っていても下限未満の変種では進めない()
    {
        // `_lastStepOk` が偽の枝は AdvanceAsync(Step) へ直に入るので、是正前はここだけ
        // 門を飛ばして取得へ抜けられた（＝門が要る唯一の状態で門が無い）。
        var paths = MakeTree();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu126 };
        var vm = NewWizard(
            paths, settings, new DriverProbe("537.58", 1, Probed: true),
            new ThrowingStore(paths.SettingsPath));

        await vm.RefreshDriverAsync();
        vm.Accepted = true;
        await vm.NextCommand.ExecuteAsync();                      // 通知 → 変種
        Assert.Equal(FirstRunStep.Variant, vm.Step);

        // 変種の段で「予期しない失敗」が起きた＝もう一度の札が立つ（段は変種のまま）。
        await vm.NextCommand.ExecuteAsync();
        Assert.Equal(FirstRunStep.Variant, vm.Step);
        Assert.False(vm.LastStepOk);

        // ここで下限未満へ選び直す＝「もう一度」を押しても取得へは進まない。
        vm.Variant = RuntimeVariants.Cu130;
        Assert.True(vm.VariantBlocked);

        await vm.NextAsync();

        Assert.Equal(FirstRunStep.Variant, vm.Step);
        Assert.Equal(vm.VariantBlockReason, vm.Message);
    }

    [Fact]
    public async Task ウィザードの行も同じ檔に残る()
    {
        // 切符の元は**清潔導入**で、そこで詰まる回（取得・展開・モデル）は主窓の状態帯を
        // 1 度も通らない＝StatusViewModel.LogSink だけでは logs\ がまさにその場合に空だった。
        var paths = MakeTree();
        var log = new LauncherLogFile(paths.LogDir);
        var vm = NewWizard(paths, new LauncherSettings(), new DriverProbe("591.86", 1, Probed: true));
        vm.LogSink = log.Append;

        vm.Accepted = true;
        await vm.NextAsync();                                     // 通知 → 変種
        await vm.NextAsync();                                     // 変種 → 取得（取得系が無いので失敗）

        Assert.Equal(FirstRunStep.Download, vm.Step);
        Assert.False(vm.LastStepOk);

        var text = File.ReadAllText(log.CurrentPath, new UTF8Encoding(false));
        Assert.Contains("通知に同意しました。", text, StringComparison.Ordinal);
        Assert.Contains("取得系がまだ組み込まれていません", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 檔に書けなくてもウィザードは進む()
    {
        var paths = MakeTree();
        var vm = NewWizard(paths, new LauncherSettings(), DriverProbe.Unknown);
        vm.LogSink = _ => throw new IOException("書けません");

        vm.Accepted = true;
        await vm.NextAsync();

        Assert.Equal(FirstRunStep.Variant, vm.Step);
    }

    /// <summary>書こうとすると必ず落ちる設定の口（段の中で「予期しない失敗」を起こす）。</summary>
    private sealed class ThrowingStore(string path) : ISettingsStore
    {
        public string Path => path;

        public string? LastLoadError => null;

        public LauncherSettings Load() => new();

        public void Save(LauncherSettings settings) => throw new IOException("settings.json を書けません");
    }
}

/// <summary>裁定 126 の B＝<b>設定頁は「数え直す」を押さなくてもドライバを知っている</b>。</summary>
public sealed class Decision126SettingsDriverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-126c-set-" + Guid.NewGuid().ToString("N")[..8]);

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

    private AppPaths MakePaths()
    {
        var appDir = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(appDir, "ledger"));
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        var paths = new AppPaths(
            Path.Combine(_root, "install"),
            appDir,
            Path.Combine(_root, "runtime"),
            Path.Combine(_root, "data"),
            developerMode: true);

        foreach (var variant in new[] { RuntimeVariants.Cu130, RuntimeVariants.Cu126, RuntimeVariants.Cpu })
        {
            File.WriteAllText(
                paths.LedgerPath(RuntimeVariants.LedgerName(variant)),
                """{"schema":1,"items":[]}""",
                new UTF8Encoding(false));
        }

        return paths;
    }

    [Fact]
    public void 数え直していなくても知っている版で断る()
    {
        // Gpus／SelectedGpu は「数え直す」の釦を押した回にしか埋まらない。是正前は、設定頁を
        // 開いただけの利用者（＝司令官の機体の形）に下限未満の cu130 がそのまま適用でき、
        // 次の「サーバ起動」を門が断った＝裁定 126 の B が塞いだはずの行き止まりが残っていた。
        var paths = MakePaths();
        var live = new LauncherSettings { Variant = RuntimeVariants.Cu126 };
        var vm = new SettingsViewModel(
            live, new JsonSettingsStore(paths.SettingsPath), paths, null, new DriverRequirement())
        {
            KnownDriverVersion = "537.58",
        };

        vm.Variant = RuntimeVariants.Cu130;

        Assert.True(vm.VariantBlocked);
        Assert.Contains("CUDA 12.6 を選んでください", vm.VariantBlockReason!, StringComparison.Ordinal);

        vm.Apply();

        Assert.Equal(RuntimeVariants.Cu126, live.Variant);        // 本物は動いていない
        Assert.False(File.Exists(paths.SettingsPath));            // 1 度も焼いていない
    }

    [Fact]
    public void 知っている版でもcpuは通る()
    {
        var paths = MakePaths();
        var live = new LauncherSettings { Variant = RuntimeVariants.Cu126 };
        var vm = new SettingsViewModel(
            live, new JsonSettingsStore(paths.SettingsPath), paths, null, new DriverRequirement())
        {
            KnownDriverVersion = "400.00",
        };

        vm.Variant = RuntimeVariants.Cpu;
        vm.Apply();

        Assert.False(vm.VariantBlocked);
        Assert.Equal(RuntimeVariants.Cpu, live.Variant);
    }

    [Fact]
    public void 版を知らなければこれまでどおり止めない()
    {
        var paths = MakePaths();
        var live = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var vm = new SettingsViewModel(
            live, new JsonSettingsStore(paths.SettingsPath), paths, null, new DriverRequirement());

        vm.Variant = RuntimeVariants.Cu130;
        vm.Apply();

        Assert.False(vm.VariantBlocked);
        Assert.Equal(RuntimeVariants.Cu130, live.Variant);
    }
}
