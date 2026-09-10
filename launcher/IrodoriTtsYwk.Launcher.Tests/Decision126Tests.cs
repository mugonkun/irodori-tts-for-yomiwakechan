using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Logging;
using IrodoriTtsYwk.Launcher.Services.Server;
using IrodoriTtsYwk.Launcher.Services.Voices;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 裁定 126＝v1.1.0 のランチャ席の釘（A＝×で終了・B＝下限未満の変種を選ばせない・
/// C＝ログ檔／voices.json を毎回書く／GPU を設定へ焼く）。
/// <para>
/// <b>実機・実 GPU・実ポート・外への取得には 1 つも触れない</b>＝子プロセスは 1 つも起こさない。
/// </para>
/// </summary>
public sealed class Decision126ShutdownTests
{
    /// <summary>止めた回数と、そのときの取消の様子を覚えるだけの偽の子プロセス。</summary>
    private sealed class StoppableServer(bool hangOnStop = false) : IServerProcess
    {
        public int StopCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public ServerState State { get; set; } = ServerState.Ready;

        public int? ProcessId => 4321;

        public int? ExitCode => null;

        public string? FailureReason => null;

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
            ServerStartRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void ReportPreflightFailure(string reason)
        {
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            if (hangOnStop)
            {
                // 落ちない個体＝期限が来るまで返らない（実機のツリー kill の待ちと同じ形）。
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void 窓を閉じてももう隠さない()
    {
        // 裁定 6（配布版が常駐・× は隠すだけ）は裁定 124 で覆った。
        Assert.False(ShutdownSequence.WindowCloseHidesToTray);
    }

    [Fact]
    public void どの状態から閉じてもサーバを止める()
    {
        // 起動中・読込中・暖機中に閉じた回こそ VRAM を握った子が残る。
        foreach (var state in Enum.GetValues<ServerState>())
        {
            Assert.True(ShutdownSequence.StopsServerOnExit(state));
        }
    }

    [Fact]
    public async Task 終了はツリーkillを待ち切ってからDisposeする()
    {
        var server = new StoppableServer();

        var outcome = await ShutdownSequence.StopServerTreeAsync(
            server, ShutdownSequence.DefaultStopTimeout);

        Assert.True(outcome.Stopped);
        Assert.False(outcome.TimedOut);
        Assert.Null(outcome.Note);
        Assert.Equal(1, server.StopCalls);
        Assert.Equal(1, server.DisposeCalls);
    }

    [Fact]
    public async Task 起動中に閉じてもツリーkillを撃つ()
    {
        var server = new StoppableServer { State = ServerState.Starting };

        var outcome = await ShutdownSequence.StopServerTreeAsync(server, TimeSpan.FromSeconds(5));

        Assert.True(outcome.Stopped);
        Assert.Equal(1, server.StopCalls);
    }

    [Fact]
    public async Task 落ちない個体は期限で諦めて終わる()
    {
        // 「終われないアプリ」を作らない＝諦めた理由は 1 行で残す。
        var server = new StoppableServer(hangOnStop: true);

        var outcome = await ShutdownSequence.StopServerTreeAsync(server, TimeSpan.FromMilliseconds(50));

        Assert.False(outcome.Stopped);
        Assert.True(outcome.TimedOut);
        Assert.NotNull(outcome.Note);
        Assert.Equal(1, server.DisposeCalls);   // 諦めても後始末は続ける
    }

    [Fact]
    public async Task 個体が無くても投げない()
    {
        var outcome = await ShutdownSequence.StopServerTreeAsync(null, TimeSpan.FromSeconds(1));

        Assert.False(outcome.Stopped);
        Assert.False(outcome.TimedOut);
    }
}

/// <summary>裁定 126 の B＝ドライバの帯から勧め、下限未満は選ばせない（<b>純関数</b>）。</summary>
public sealed class Decision126VariantRecommendationTests
{
    private static readonly string[] CudaChoices =
        [RuntimeVariants.Cu130, RuntimeVariants.Cu126, RuntimeVariants.Cpu];

    private static readonly string[] RadeonChoices =
        [RuntimeVariants.RocmGfx1151, RuntimeVariants.Cpu];

    [Theory]
    [InlineData("591.86", RuntimeVariants.Cu130)]
    [InlineData("580.00", RuntimeVariants.Cu130)]
    [InlineData("579.99", RuntimeVariants.Cu126)]
    [InlineData("537.58", RuntimeVariants.Cu126)]   // 司令官の実射（RTX 3090）
    [InlineData("528.33", RuntimeVariants.Cu126)]
    [InlineData("500.00", RuntimeVariants.Cpu)]
    public void ドライバの帯で勧める変種が決まる(string driver, string expected) =>
        Assert.Equal(
            expected,
            VariantRecommendation.Recommend(CudaChoices, new DriverProbe(driver, 1, Probed: true)));

    [Fact]
    public void ROCm版の樹ではrocmを勧める() =>
        Assert.Equal(
            RuntimeVariants.RocmGfx1151,
            VariantRecommendation.Recommend(RadeonChoices, DriverProbe.Unknown));

    [Fact]
    public void GPUが1台も見えなければcpuを勧める() =>
        Assert.Equal(
            RuntimeVariants.Cpu,
            VariantRecommendation.Recommend(CudaChoices, new DriverProbe(null, 0, Probed: true)));

    [Fact]
    public void まだ撃っていなければ並びの先頭のまま()
    {
        // 判らないことを勝手に決めない（＝既定の cu130 を保つ）。
        Assert.Equal(RuntimeVariants.Cu130, VariantRecommendation.Recommend(CudaChoices, DriverProbe.Unknown));
    }

    [Theory]
    [InlineData(RuntimeVariants.Cu130, "537.58", true)]
    [InlineData(RuntimeVariants.Cu126, "537.58", false)]
    [InlineData(RuntimeVariants.Cu130, "580.00", false)]
    [InlineData(RuntimeVariants.Cpu, "100.00", false)]              // cpu はいつでも通る
    [InlineData(RuntimeVariants.RocmGfx1151, "537.58", false)]      // 下限が無い
    [InlineData(RuntimeVariants.Cu130, null, false)]                // 読めなければ止めない
    [InlineData(RuntimeVariants.Cu130, "よめない", false)]
    public void 下限未満の判定(string variant, string? driver, bool expected) =>
        Assert.Equal(expected, VariantRecommendation.IsBelowMinimum(variant, driver));

    [Fact]
    public void 断る1行はドライバと勧める先を名指す()
    {
        var reason = VariantRecommendation.BlockReason(CudaChoices, RuntimeVariants.Cu130, "537.58");

        Assert.NotNull(reason);
        Assert.Contains("このドライバ（537.58）では CUDA 13.0 は動きません", reason!, StringComparison.Ordinal);
        Assert.Contains("CUDA 12.6 を選んでください", reason!, StringComparison.Ordinal);

        // 通る組み合わせでは 1 行も出ない。
        Assert.Null(VariantRecommendation.BlockReason(CudaChoices, RuntimeVariants.Cu126, "537.58"));
        Assert.Null(VariantRecommendation.BlockReason(CudaChoices, RuntimeVariants.Cpu, "100.00"));
        Assert.Null(VariantRecommendation.BlockReason(CudaChoices, RuntimeVariants.Cu130, null));
    }
}

/// <summary>裁定 126 の B＝初回取得ウィザードの変種の段。</summary>
public sealed class Decision126WizardVariantTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-126-wiz-" + Guid.NewGuid().ToString("N")[..8]);

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
    public async Task ドライバ537_58ではcu126が選ばれる()
    {
        // 司令官の実射（RTX 3090・537.58・v1.0.2 の清潔導入）＝1 巡目は cu130 のままだった。
        var paths = MakeTree();
        var vm = NewWizard(paths, new LauncherSettings(), new DriverProbe("537.58", 1, Probed: true));

        Assert.Equal(RuntimeVariants.Cu130, vm.Variant);        // 検分の前は並びの先頭
        await vm.RefreshDriverAsync();

        Assert.Equal(RuntimeVariants.Cu126, vm.Variant);
        Assert.False(vm.VariantBlocked);
        Assert.Equal("537.58", vm.DriverVersion);
    }

    [Fact]
    public async Task ドライバ580以上ならcu130のまま()
    {
        var paths = MakeTree();
        var vm = NewWizard(paths, new LauncherSettings(), new DriverProbe("591.86", 1, Probed: true));

        await vm.RefreshDriverAsync();

        Assert.Equal(RuntimeVariants.Cu130, vm.Variant);
        Assert.False(vm.VariantBlocked);
    }

    [Fact]
    public async Task 下限未満の変種を選ぶと取得を始められない()
    {
        var paths = MakeTree();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var vm = NewWizard(paths, settings, new DriverProbe("537.58", 1, Probed: true));

        await vm.RefreshDriverAsync();
        vm.Variant = RuntimeVariants.Cu130;                     // 利用者が自分で選び直した

        Assert.True(vm.VariantBlocked);
        Assert.Contains("CUDA 12.6 を選んでください", vm.VariantBlockReason!, StringComparison.Ordinal);

        // 変種の段では「この構成で取得を始める」が押せない
        vm.Accepted = true;
        await vm.NextAsync();                                    // 通知 → 変種
        Assert.Equal(FirstRunStep.Variant, vm.Step);
        Assert.False(vm.NextCommand.CanExecute(null));

        // 束縛の外から呼んでも通さない（段も設定も動かない）
        await vm.NextAsync();
        Assert.Equal(FirstRunStep.Variant, vm.Step);
        Assert.Equal(RuntimeVariants.Cpu, settings.Variant);
        Assert.False(File.Exists(paths.SettingsPath));           // 1 度も焼いていない
    }

    [Fact]
    public async Task cpuの変種はどのドライバでも選べる()
    {
        var paths = MakeTree();
        var vm = NewWizard(paths, new LauncherSettings(), new DriverProbe("400.00", 1, Probed: true));

        await vm.RefreshDriverAsync();
        vm.Accepted = true;
        await vm.NextAsync();                                    // 通知 → 変種
        vm.Variant = RuntimeVariants.Cpu;

        Assert.Equal(FirstRunStep.Variant, vm.Step);
        Assert.False(vm.VariantBlocked);
        Assert.Null(vm.VariantBlockReason);
        Assert.True(vm.NextCommand.CanExecute(null));
    }

    [Fact]
    public async Task ドライバが読めなくても進める()
    {
        // AMD 機・nvidia-smi の無い機体＝止めずに「読めなかった」と名乗る。
        var paths = MakeTree();
        var vm = NewWizard(paths, new LauncherSettings(), new DriverProbe(null, 1, Probed: true));

        await vm.RefreshDriverAsync();
        vm.Accepted = true;
        await vm.NextAsync();                                    // 通知 → 変種

        Assert.Equal(FirstRunStep.Variant, vm.Step);
        Assert.False(vm.VariantBlocked);
        Assert.True(vm.NextCommand.CanExecute(null));
        Assert.Contains(
            VariantRecommendation.UnknownDriverNote, vm.DriverText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 検分が落ちてもウィザードは止まらない()
    {
        var paths = MakeTree();
        var vm = NewWizard(paths, new LauncherSettings(), DriverProbe.Unknown);
        vm.DriverProbeAsync = _ => throw new InvalidOperationException("列挙が落ちた");

        await vm.RefreshDriverAsync();

        Assert.Equal(RuntimeVariants.Cu130, vm.Variant);
        Assert.False(vm.VariantBlocked);
    }

    [Fact]
    public async Task 利用者が選んだ変種は勧めで上書きしない()
    {
        // 裁定 4＝自動切替はしない。勧めるのは「まだ選んでいない初期値」だけ。
        var paths = MakeTree();
        var vm = NewWizard(paths, new LauncherSettings(), new DriverProbe("591.86", 1, Probed: true));

        vm.Variant = RuntimeVariants.Cpu;
        await vm.RefreshDriverAsync();

        Assert.Equal(RuntimeVariants.Cpu, vm.Variant);
    }
}

/// <summary>裁定 126 の B＝設定頁は下限未満の変種を書かない。</summary>
public sealed class Decision126SettingsVariantTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-126-set-" + Guid.NewGuid().ToString("N")[..8]);

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

    private static GpuInfo Gpu(string driver) => new(
        "GPU-1111", "NVIDIA GeForce RTX 3090", 0, 24L * 1024 * 1024 * 1024,
        "1", null, driver, GpuSource.NvidiaSmi);

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

    [Fact]
    public void 下限未満の変種は適用できない()
    {
        var paths = MakePaths();
        var live = new LauncherSettings { Variant = RuntimeVariants.Cu126 };
        var vm = new SettingsViewModel(
            live, new JsonSettingsStore(paths.SettingsPath), paths, null, new DriverRequirement())
        {
            SelectedGpu = Gpu("537.58"),
        };

        vm.Variant = RuntimeVariants.Cu130;

        Assert.True(vm.VariantBlocked);
        vm.Apply();

        Assert.Equal(RuntimeVariants.Cu126, live.Variant);       // 本物は動いていない
        Assert.Contains("CUDA 12.6 を選んでください", vm.Message, StringComparison.Ordinal);
        Assert.True(vm.IsDirty);                                  // 未保存のまま
    }

    [Fact]
    public void cpuの変種は同じドライバでも適用できる()
    {
        var paths = MakePaths();
        var live = new LauncherSettings { Variant = RuntimeVariants.Cu126 };
        var vm = new SettingsViewModel(
            live, new JsonSettingsStore(paths.SettingsPath), paths, null, new DriverRequirement())
        {
            SelectedGpu = Gpu("537.58"),
        };

        vm.Variant = RuntimeVariants.Cpu;

        Assert.False(vm.VariantBlocked);
        vm.Apply();

        Assert.Equal(RuntimeVariants.Cpu, live.Variant);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void ドライバが読めない機体では止めない()
    {
        var paths = MakePaths();
        var live = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var vm = new SettingsViewModel(
            live, new JsonSettingsStore(paths.SettingsPath), paths, null, new DriverRequirement());

        vm.Variant = RuntimeVariants.Cu130;                       // GPU を数えていない＝版が無い

        Assert.False(vm.VariantBlocked);
        vm.Apply();

        Assert.Equal(RuntimeVariants.Cu130, live.Variant);
    }
}

/// <summary>裁定 126 の C（1）＝画面に出た 1 行が檔にも残る。</summary>
public sealed class Decision126LogFileTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-126-log-" + Guid.NewGuid().ToString("N")[..8]);

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

    [Fact]
    public void 檔名は日ごとに分かれる()
    {
        var dir = Path.Combine(_root, "logs");
        Assert.Equal(
            Path.Combine(dir, "launcher-20260910.log"),
            LauncherLogFile.PathFor(dir, new DateTimeOffset(2026, 9, 10, 20, 30, 0, TimeSpan.FromHours(9))));
        Assert.Equal(
            Path.Combine(dir, "launcher-20260911.log"),
            LauncherLogFile.PathFor(dir, new DateTimeOffset(2026, 9, 11, 0, 1, 0, TimeSpan.FromHours(9))));
    }

    [Fact]
    public void 足した行はUTF8で檔に残る()
    {
        var dir = Path.Combine(_root, "logs");
        var when = new DateTimeOffset(2026, 9, 10, 20, 30, 5, TimeSpan.FromHours(9));
        var log = new LauncherLogFile(dir, () => when);

        log.Append("起動：http://127.0.0.1:18088/（変種 cu126）");
        log.Append("  停止しました。  ");
        log.Append("   ");        // 空白だけの行は書かない
        log.Append(null);

        var path = LauncherLogFile.PathFor(dir, when);
        var lines = File.ReadAllLines(path, new UTF8Encoding(false));

        Assert.Equal(2, lines.Length);
        Assert.Equal("20:30:05 起動：http://127.0.0.1:18088/（変種 cu126）", lines[0]);
        Assert.Equal("20:30:05 停止しました。", lines[1]);
    }

    [Fact]
    public void 日をまたぐと次の檔に移る()
    {
        var dir = Path.Combine(_root, "logs");
        var now = new DateTimeOffset(2026, 9, 10, 23, 59, 59, TimeSpan.FromHours(9));
        var log = new LauncherLogFile(dir, () => now);

        log.Append("昨日の行");
        now = new DateTimeOffset(2026, 9, 11, 0, 0, 1, TimeSpan.FromHours(9));
        log.Append("今日の行");

        Assert.Equal(2, Directory.GetFiles(dir, "launcher-*.log").Length);
    }

    [Fact]
    public void 書けない置き場でも投げない()
    {
        // 置き場の名で檔が既に在る＝Directory.CreateDirectory が IOException を投げる路。
        Directory.CreateDirectory(_root);
        var occupied = Path.Combine(_root, "logs");
        File.WriteAllText(occupied, "これは檔です", new UTF8Encoding(false));

        var log = new LauncherLogFile(occupied);
        log.Append("落ちてはいけない");   // 投げないことが釘（Assert は不要だが形を残す）

        Assert.True(File.Exists(occupied));
    }

    [Fact]
    public void 状態帯の1行は檔の書き手にも渡る()
    {
        var seen = new List<string>();
        var status = new StatusViewModel(
            static () => Task.CompletedTask, static () => Task.CompletedTask)
        {
            LogSink = seen.Add,
        };

        status.AppendLog("1 行目");
        status.AppendLog("   ");          // 空白は渡さない
        status.AppendLog(null);

        Assert.Equal(["1 行目"], seen);
        Assert.Contains("1 行目", status.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void 檔に書けなくても画面のログは進む()
    {
        var status = new StatusViewModel(
            static () => Task.CompletedTask, static () => Task.CompletedTask)
        {
            LogSink = static _ => throw new IOException("書けない"),
        };

        status.AppendLog("画面には出る");

        Assert.Contains("画面には出る", status.LogText, StringComparison.Ordinal);
    }
}

/// <summary>裁定 126 の C（2）＝<c>voices.json</c> は起動のたびに書き直す。</summary>
public sealed class Decision126VoicesJsonTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-126-vj-" + Guid.NewGuid().ToString("N")[..8]);

    private AppPaths Paths => Ruling121Tree.MakePaths(_root);

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

    /// <summary>書いた回数と、そのときの中身を覚えるだけの書き手。</summary>
    private sealed class CountingWriter : IVoicesJsonWriter
    {
        public int Calls { get; private set; }

        public IReadOnlyList<string> LastIds { get; private set; } = [];

        public string Render(VoicesYwkFile store) => "{}";

        public void Write(string path, VoicesYwkFile store)
        {
            Calls++;
            LastIds = [.. store.Voices.Keys];
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{}", new UTF8Encoding(false));
        }
    }

    [Fact]
    public void 何も動かない回でも書き直す()
    {
        // 1 巡目は「この回で何かが動いたか、檔が無いか」を条件にしていたので、サーバを止めている
        // 間に台帳を直した回が別名表に届かなかった。
        var paths = Paths;
        Ruling121Tree.MakeManifest(paths, withChampagne: true);
        var store = new VoiceStore(paths);
        var writer = new CountingWriter();

        LauncherComposition.PrepareVoices(paths, store, writer);   // 1 回目＝プリセットを写す
        Assert.Equal(1, writer.Calls);
        Assert.True(File.Exists(paths.VoicesJsonPath));

        LauncherComposition.PrepareVoices(paths, store, writer);   // 2 回目＝動く物は無い
        Assert.Equal(2, writer.Calls);

        LauncherComposition.PrepareVoices(paths, store, writer);   // 3 回目も同じ
        Assert.Equal(3, writer.Calls);
    }

    [Fact]
    public void 止めている間に直した台帳が別名表へ届く()
    {
        var paths = Paths;
        Ruling121Tree.MakeManifest(paths, withChampagne: true);
        var store = new VoiceStore(paths);
        var writer = new CountingWriter();

        LauncherComposition.PrepareVoices(paths, store, writer);
        var before = writer.LastIds.Count;

        // 走っていない間に利用者が台帳から 1 名消した（手で直した・一覧で消した）。
        var table = store.Load();
        var victim = table.Voices.Keys.First(id => id != VoiceIds.Default);
        store.Save(table with
        {
            Voices = table.Voices
                .Where(pair => pair.Key != victim)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        });

        LauncherComposition.PrepareVoices(paths, store, writer);

        Assert.DoesNotContain(victim, writer.LastIds);
        Assert.Equal(before - 1, writer.LastIds.Count);
    }
}

/// <summary>裁定 126 の C（3）＝起動が通った回に GPU を設定へ焼く。</summary>
[Collection(AppServicesCollection.Name)]
public sealed class Decision126GpuPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-126-gpu-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>起動が通ったことにする偽の子プロセス（子は 1 つも起こさない）。</summary>
    private sealed class HappyServer : IServerProcess
    {
        public ServerState State => ServerState.Ready;

        public int? ProcessId => 1234;

        public int? ExitCode => null;

        public string? FailureReason => null;

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
            ServerStartRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ServerStartResult(
                true, ServerState.Ready, 1234, null, TimeSpan.FromSeconds(1), null));

        public void ReportPreflightFailure(string reason)
        {
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

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

    private static readonly GpuInfo Rtx3090 = new(
        "GPU-abcdef12-3456-7890-abcd-ef1234567890", "NVIDIA GeForce RTX 3090",
        0, 24L * 1024 * 1024 * 1024, "1", null, "591.86", GpuSource.NvidiaSmi);

    private AppPaths MakeTree(string variant)
    {
        var paths = Ruling121Tree.MakePaths(_root);
        Directory.CreateDirectory(paths.LedgerDir);
        Ruling121Tree.MakeModelsLedger(paths);
        Ruling121Tree.MakeModelCache(paths);
        Ruling121Tree.MakeRuntime(paths, variant);
        return paths;
    }

    private MainViewModel NewMain(AppPaths paths, LauncherSettings settings) =>
        new(paths, settings, new JsonSettingsStore(paths.SettingsPath), new SilentPlayer());

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
    public async Task 起動が通れば選んでいないGPUを設定に焼く()
    {
        // 司令官の実射＝設定頁を 1 度も開かずウィザードだけで通した機体で
        // gpuUuid／gpuName が null のまま残った。
        var paths = MakeTree(RuntimeVariants.Cu130);
        AppServices.Server = new HappyServer();
        AppServices.GpuEnumerator = new FixedEnumerator(Rtx3090);

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130, Port = 18099 };
        var main = NewMain(paths, settings);

        Assert.True(await main.StartServerAsync());

        Assert.Equal(Rtx3090.Uuid, settings.GpuUuid);
        Assert.Equal(Rtx3090.Name, settings.GpuName);
        Assert.Contains(
            Rtx3090.Uuid,
            File.ReadAllText(paths.SettingsPath, new UTF8Encoding(false)),
            StringComparison.Ordinal);
        Assert.Contains("設定に覚えました", main.Status.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 既に選んである設定は上書きしない()
    {
        var paths = MakeTree(RuntimeVariants.Cu130);
        AppServices.Server = new HappyServer();
        AppServices.GpuEnumerator = new FixedEnumerator(Rtx3090);

        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cu130,
            Port = 18099,
            GpuUuid = Rtx3090.Uuid,
            GpuName = "利用者が付けた名",
        };

        Assert.True(await NewMain(paths, settings).StartServerAsync());

        Assert.Equal("利用者が付けた名", settings.GpuName);
    }

    [Fact]
    public async Task cpuの変種では焼かない()
    {
        var paths = MakeTree(RuntimeVariants.Cpu);
        AppServices.Server = new HappyServer();
        AppServices.GpuEnumerator = new FixedEnumerator(Rtx3090);

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu, Port = 18099 };

        Assert.True(await NewMain(paths, settings).StartServerAsync());

        Assert.Null(settings.GpuUuid);
        Assert.Null(settings.GpuName);
    }

    [Fact]
    public async Task 起動の1行は檔のログにも落ちる()
    {
        // C（1）の配線＝MainViewModel が状態帯に LauncherLogFile を差している。
        var paths = MakeTree(RuntimeVariants.Cpu);
        AppServices.Server = new HappyServer();
        AppServices.GpuEnumerator = null;

        var main = NewMain(paths, new LauncherSettings { Variant = RuntimeVariants.Cpu, Port = 18099 });
        Assert.True(await main.StartServerAsync());

        var text = File.ReadAllText(main.LogFile.CurrentPath, new UTF8Encoding(false));
        Assert.Contains("起動：", text, StringComparison.Ordinal);
    }
}
