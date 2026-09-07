using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Ledger;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 便 D（3）の 3 巡目＝<b>是正席の釘</b>（敵対検分の high 3・medium 7 と low の一部）。
/// <para>
/// <b>外へ 1 バイトも出さない</b>（取得は E2E 席だけ）。子プロセスは 1 つも起こさない。
/// 所見ごとに「壊れていた形をそのまま組んで、直った形を要求する」1 本を置く。
/// </para>
/// </summary>
public static class CorrectionThreeTree
{
    /// <summary>変種 cpu の台帳（wheel 1 件＝<c>torch-1.whl</c>）。</summary>
    public const string CpuLedger = """
        {"schema":1,"items":[
          {"kind":"wheel","name":"torch","url":"https://example.invalid/torch-1.whl",
           "sha256":"bb","size":1000},
          {"kind":"wheel","name":"shared","url":"https://example.invalid/shared.whl",
           "sha256":"cc","size":50}]}
        """;

    /// <summary>変種 cu126 の台帳（<c>torch-cu126.whl</c> は<b>この変種だけ</b>が名指す）。</summary>
    public const string Cu126Ledger = """
        {"schema":1,"items":[
          {"kind":"wheel","name":"torch","url":"https://example.invalid/torch-cu126.whl",
           "sha256":"dd","size":2000},
          {"kind":"wheel","name":"shared","url":"https://example.invalid/shared.whl",
           "sha256":"cc","size":50}]}
        """;

    /// <summary>配布樹（ledger 3 檔）と利用者データ樹（cache）を作る。</summary>
    public static AppPaths MakePaths(string root)
    {
        var appDir = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(appDir, "ledger"));
        Directory.CreateDirectory(Path.Combine(root, "data", "cache"));

        var paths = new AppPaths(
            Path.Combine(root, "install"),
            appDir,
            Path.Combine(root, "runtime"),
            Path.Combine(root, "data"),
            developerMode: true);

        var utf8 = new UTF8Encoding(false);
        File.WriteAllText(
            paths.LedgerPath("python-embed"),
            """
            {"schema":1,"items":[
              {"kind":"python-embed","name":"python-embed",
               "url":"https://example.invalid/python-embed.zip","sha256":"aa","size":100}]}
            """,
            utf8);
        File.WriteAllText(paths.LedgerPath("runtime-cpu"), CpuLedger, utf8);
        File.WriteAllText(paths.LedgerPath("runtime-cu126"), Cu126Ledger, utf8);
        return paths;
    }

    /// <summary>展開が済んだ樹（<c>python.exe</c>＋台帳と件数の合う <c>*.dist-info</c>）。</summary>
    public static void MakeRuntime(AppPaths paths, string variant, int distInfos = 2)
    {
        var dir = Path.Combine(paths.RuntimeRoot, variant);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, AppPaths.PythonExeName), []);
        for (var i = 0; i < distInfos; i++)
        {
            Directory.CreateDirectory(Path.Combine(dir, "site-packages", "pkg" + i + "-1.0.dist-info"));
        }
    }

    /// <summary>展開が python-embed の直後で切れた樹（<c>python.exe</c> 1 檔だけ）。</summary>
    public static void MakeHalfRuntime(AppPaths paths, string variant)
    {
        var dir = Path.Combine(paths.RuntimeRoot, variant);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, AppPaths.PythonExeName), []);
    }

    public static void Write(string path, int bytes) =>
        File.WriteAllBytes(path, new byte[bytes]);

    public sealed class SilentPlayer : IAudioPlayer
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

    public sealed class QuietServer : IServerProcess
    {
        public ServerState State { get; private set; } = ServerState.Stopped;

        public int? ProcessId => null;

        public int? ExitCode => null;

        public string? FailureReason { get; private set; }

        public ServerLogEvent? Banner => null;

        public Uri? BaseAddress => null;

        public StatusResponse? LatestStatus => null;

        /// <summary>OS の GPU 計数も無い（裁定 110）。</summary>
        public IReadOnlyList<IrodoriTtsYwk.Launcher.Services.Gpu.OsGpuMemoryRow> LatestOsGpuMemory => [];

#pragma warning disable CS0067 // 偽物なので誰も上げない
        public event EventHandler<ServerStateChangedEventArgs>? StateChanged;

        public event EventHandler<ServerLogLineEventArgs>? LogLine;

        public event EventHandler<StatusResponse>? StatusSampled;
#pragma warning restore CS0067

        public Task<ServerStartResult> StartAsync(
            ServerStartRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ServerStartResult(
                false, ServerState.Failed, null, null, TimeSpan.Zero, "偽物なので起こしません。"));

        public void ReportPreflightFailure(string reason)
        {
            State = ServerState.Failed;
            FailureReason = reason;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

// ================================================================ 焼き印（変種ごと）

/// <summary>high ⑴＋medium＝焼き印は変種ごとに持つ・版だけの差で急かさない・組みかけには押さない。</summary>
[Collection(AppServicesCollection.Name)]
public sealed class CorrectionThreeStampTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-c3-stamp-" + Guid.NewGuid().ToString("N"));

    private MainViewModel NewMain(AppPaths paths, LauncherSettings settings)
    {
        AppServices.Server = new CorrectionThreeTree.QuietServer();
        AppServices.GpuEnumerator = null;
        return new MainViewModel(
            paths, settings, new JsonSettingsStore(paths.SettingsPath),
            new CorrectionThreeTree.SilentPlayer());
    }

    [Fact]
    public void 焼き印は変種ごと_隣の変種へ切り替えても偽警告を出さない()
    {
        // 壊れていた形＝焼き印は settings に 1 組しか無く、実行系は変種ごとに在った。
        // 両方組んである機体で cu126 へ切り替えただけで「実行系を組み直してください」が出て、
        // 押せば健全な実行系を消して数 GiB を取り直した（裁定 88 ⑴ の導線がそのまま落ちる）。
        var paths = CorrectionThreeTree.MakePaths(_root);
        CorrectionThreeTree.MakeRuntime(paths, RuntimeVariants.Cpu);
        CorrectionThreeTree.MakeRuntime(paths, RuntimeVariants.Cu126);

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var main = NewMain(paths, settings);

        // ⑴ cpu で起動＝cpu の欄だけが焼かれる
        Assert.Null(main.Status.RebuildRuntimeText);
        Assert.Equal(
            RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu),
            settings.RuntimeLedgerFor(RuntimeVariants.Cpu));
        Assert.Null(settings.RuntimeLedgerFor(RuntimeVariants.Cu126));

        // ⑵ 変種を cu126 に変える＝**黙って**cu126 の欄を焼く（1 手を出さない）
        settings.Variant = RuntimeVariants.Cu126;
        main.CheckRuntimeStamp();

        Assert.Null(main.Status.RebuildRuntimeText);
        Assert.Equal(
            RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cu126),
            settings.RuntimeLedgerFor(RuntimeVariants.Cu126));

        // ⑶ cpu へ戻しても、cpu の焼き印は消えていない
        settings.Variant = RuntimeVariants.Cpu;
        main.CheckRuntimeStamp();
        Assert.Null(main.Status.RebuildRuntimeText);
        Assert.NotEqual(
            settings.RuntimeLedgerFor(RuntimeVariants.Cpu),
            settings.RuntimeLedgerFor(RuntimeVariants.Cu126));
    }

    [Fact]
    public void 焼き印は変種ごと_掃除の関門も変種ごとに読む()
    {
        var paths = CorrectionThreeTree.MakePaths(_root);
        CorrectionThreeTree.MakeRuntime(paths, RuntimeVariants.Cpu);
        CorrectionThreeTree.MakeRuntime(paths, RuntimeVariants.Cu126);

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        RuntimeStamp.Burn(settings, paths, RuntimeVariants.Cpu);
        RuntimeStamp.Burn(settings, paths, RuntimeVariants.Cu126);

        foreach (var variant in new[] { RuntimeVariants.Cpu, RuntimeVariants.Cu126 })
        {
            Assert.Null(CacheCleaner.Blocked(
                true, settings.RuntimeLedgerFor(variant), RuntimeStamp.LedgerSha256(paths, variant)));
        }
    }

    [Fact]
    public void 版だけ違えば焼き直して黙る_2度目の起動でも黙る()
    {
        // 壊れていた形＝Line を返すので 1 手が押せる状態になり、CheckRuntimeStamp は
        // 空欄しか焼き直さないので installedAppVersion が永久に古いまま＝毎起動の急かし。
        var paths = CorrectionThreeTree.MakePaths(_root);
        CorrectionThreeTree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        settings.SetRuntimeStamp(
            RuntimeVariants.Cpu, RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu), "v0.0.9");

        var main = NewMain(paths, settings);

        Assert.Null(main.Status.RebuildRuntimeText);                       // 1 手は出ない
        Assert.False(main.Status.RebuildRuntimeCommand.CanExecute(null));
        Assert.Contains("そのまま使えます", main.Status.LogText, StringComparison.Ordinal);
        Assert.Equal(AppVersion.Display, settings.InstalledAppVersionFor(RuntimeVariants.Cpu));

        // 檔にも焼き直っている＝窓を開き直しても同じ 1 行を見せられない
        var written = new JsonSettingsStore(paths.SettingsPath).Load();
        Assert.Equal(AppVersion.Display, written.InstalledAppVersionFor(RuntimeVariants.Cpu));

        var second = NewMain(paths, written);
        Assert.Null(second.Status.RebuildRuntimeText);
        Assert.DoesNotContain("そのまま使えます", second.Status.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void 組みかけの樹には焼き印を押さずに1手を出す()
    {
        // 壊れていた形＝根拠が「python.exe が在る」だけだったので、python-embed の直後で
        // 切れた樹にも今の台帳を焼き、以後その樹は「この台帳から出来ている」と名乗った。
        var paths = CorrectionThreeTree.MakePaths(_root);
        CorrectionThreeTree.MakeHalfRuntime(paths, RuntimeVariants.Cpu);

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var main = NewMain(paths, settings);

        Assert.Null(settings.RuntimeLedgerFor(RuntimeVariants.Cpu));       // 焼かない
        Assert.NotNull(main.Status.RebuildRuntimeText);                    // 1 手は出す
        Assert.Contains("途中まで", main.Status.RebuildRuntimeText!, StringComparison.Ordinal);
    }

    [Fact]
    public void 組みかけの樹では取得キャッシュを消さない()
    {
        var paths = CorrectionThreeTree.MakePaths(_root);
        CorrectionThreeTree.MakeHalfRuntime(paths, RuntimeVariants.Cpu);
        var cache = paths.DownloadCacheDir;
        CorrectionThreeTree.Write(Path.Combine(cache, "torch-1.whl"), 1000);

        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cpu,
            FirstRunCompleted = true,
        };
        var main = NewMain(paths, settings);
        main.ClearCacheAfterFirstShot();

        Assert.True(File.Exists(Path.Combine(cache, "torch-1.whl")));
        Assert.Contains("消しません", main.Status.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void 組み直しの1行は取り直す量を名乗る()
    {
        // 裁定 90 の自動削除で cache は空なのが常態＝押した瞬間に数 GiB の再取得になる。
        var paths = CorrectionThreeTree.MakePaths(_root);
        CorrectionThreeTree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        settings.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", AppVersion.Display);

        var main = NewMain(paths, settings);

        Assert.NotNull(main.Status.RebuildRuntimeText);
        Assert.Contains("取り直します", main.Status.RebuildRuntimeText!, StringComparison.Ordinal);
        Assert.Contains(
            FetchPlanner.FormatBytes(1150), main.Status.RebuildRuntimeText!, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        AppServices.Reset();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 後始末の失敗は結果を変えない
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }

        GC.SuppressFinalize(this);
    }
}

// ================================================================ 取得キャッシュ

/// <summary>medium＝掃除は「関門を通っていない変種だけが名指す原檔」を残す・断られた回は数えない。</summary>
[Collection(AppServicesCollection.Name)]
public sealed class CorrectionThreeCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-c3-cache-" + Guid.NewGuid().ToString("N"));

    private MainViewModel NewMain(AppPaths paths, LauncherSettings settings)
    {
        AppServices.Server = new CorrectionThreeTree.QuietServer();
        AppServices.GpuEnumerator = null;
        return new MainViewModel(
            paths, settings, new JsonSettingsStore(paths.SettingsPath),
            new CorrectionThreeTree.SilentPlayer());
    }

    [Fact]
    public void 別の変種だけが名指す原檔は残る()
    {
        // 壊れていた形＝関門は「いまの変種 1 つ」としか突き合わせないのに、消す範囲は
        // <data>\cache 全部だった。実機ではそれで cu126 専用の 2.41 GiB が、cu126 の台帳を
        // 1 度も検めないまま消えた。
        var paths = CorrectionThreeTree.MakePaths(_root);
        CorrectionThreeTree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var cache = paths.DownloadCacheDir;
        CorrectionThreeTree.Write(Path.Combine(cache, "torch-1.whl"), 1000);        // cpu（通った）
        CorrectionThreeTree.Write(Path.Combine(cache, "torch-cu126.whl"), 2000);    // cu126 専用
        CorrectionThreeTree.Write(Path.Combine(cache, "shared.whl"), 50);           // 両方が名指す
        CorrectionThreeTree.Write(Path.Combine(cache, "python-embed.zip"), 100);    // 両方が名指す
        CorrectionThreeTree.Write(Path.Combine(cache, "old.whl.part"), 7);          // 打ち切り

        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cpu,
            FirstRunCompleted = true,
        };
        RuntimeStamp.Burn(settings, paths, RuntimeVariants.Cpu);   // cu126 は焼かない＝未検分

        var main = NewMain(paths, settings);
        main.ClearCacheAfterFirstShot();

        // cu126 専用の原檔だけが残る
        Assert.True(File.Exists(Path.Combine(cache, "torch-cu126.whl")));
        Assert.False(File.Exists(Path.Combine(cache, "torch-1.whl")));
        Assert.False(File.Exists(Path.Combine(cache, "shared.whl")));       // 通った変種と共有
        Assert.False(File.Exists(Path.Combine(cache, "python-embed.zip"))); // 同上
        Assert.False(File.Exists(Path.Combine(cache, "old.whl.part")));     // 裁定 95 ⑵ の ⒝
        Assert.Contains("別の変種の原檔", main.Status.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void 関門を通った変種の原檔は残さない()
    {
        // cu126 も検分が通っていれば、置き場は空になる（裁定 94 ⑶ の実測を守る）。
        var paths = CorrectionThreeTree.MakePaths(_root);
        CorrectionThreeTree.MakeRuntime(paths, RuntimeVariants.Cpu);
        CorrectionThreeTree.MakeRuntime(paths, RuntimeVariants.Cu126);

        var cache = paths.DownloadCacheDir;
        CorrectionThreeTree.Write(Path.Combine(cache, "torch-1.whl"), 1000);
        CorrectionThreeTree.Write(Path.Combine(cache, "torch-cu126.whl"), 2000);

        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cpu,
            FirstRunCompleted = true,
        };
        RuntimeStamp.Burn(settings, paths, RuntimeVariants.Cpu);
        RuntimeStamp.Burn(settings, paths, RuntimeVariants.Cu126);

        NewMain(paths, settings).ClearCacheAfterFirstShot();

        Assert.Empty(Directory.GetFiles(cache, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void 除外集合は純関数で決まる()
    {
        var paths = CorrectionThreeTree.MakePaths(_root);
        var names = CacheCleaner.ProtectedFileNames(
            CacheCleaner.LedgerVariants(ReleaseFlavors.LedgerNames(paths.LedgerDir)),
            variant => FirstRunViewModel.TryPlan(paths, variant, skipVcRedist: true),
            variant => string.Equals(variant, RuntimeVariants.Cpu, StringComparison.Ordinal));

        Assert.Contains("torch-cu126.whl", names);
        Assert.DoesNotContain("torch-1.whl", names);
        Assert.DoesNotContain("shared.whl", names);        // 通った変種と共有＝守らない
        Assert.DoesNotContain("python-embed.zip", names);
    }

    [Fact]
    public void 台帳の名から変種を拾う()
    {
        var variants = CacheCleaner.LedgerVariants(
            ["python-embed", "models", "vc_redist", "runtime-cpu", "runtime-rocm-gfx1151", "runtime-"]);

        Assert.Equal([RuntimeVariants.Cpu, RuntimeVariants.RocmGfx1151], variants);
    }

    [Fact]
    public void 関門に断られた回は1度と数えない()
    {
        // 壊れていた形＝_cacheCleared を Clean の前に立てていたので、断られた理由を直して
        // 撃ち直しても、その起動では二度と掃除が走らなかった。
        var paths = CorrectionThreeTree.MakePaths(_root);
        CorrectionThreeTree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var cache = paths.DownloadCacheDir;
        CorrectionThreeTree.Write(Path.Combine(cache, "torch-1.whl"), 1000);

        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cpu,
            FirstRunCompleted = true,
        };
        settings.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", AppVersion.Display);

        var main = NewMain(paths, settings);

        // ⑴ 1 射目＝焼き印が食い違う＝断られる（1 檔も消さない）
        main.ClearCacheAfterFirstShot();
        Assert.True(File.Exists(Path.Combine(cache, "torch-1.whl")));

        // ⑵ 言われたとおり焼き印を入れ直して 2 射目＝消える
        RuntimeStamp.Burn(settings, paths, RuntimeVariants.Cpu);
        main.ClearCacheAfterFirstShot();
        Assert.False(File.Exists(Path.Combine(cache, "torch-1.whl")));

        // ⑶ 3 射目は「もう空でした」も出さない（1 度成功したら終わり）
        var before = main.Status.LogText;
        main.ClearCacheAfterFirstShot();
        Assert.Equal(before, main.Status.LogText);
    }

    [Fact]
    public void 設定の量と消える量は同じ()
    {
        // ボタンの文言に出る量は、押したときに実際に消える量である（守る檔は数えない）。
        var paths = CorrectionThreeTree.MakePaths(_root);
        CorrectionThreeTree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var cache = paths.DownloadCacheDir;
        CorrectionThreeTree.Write(Path.Combine(cache, "torch-1.whl"), 1000);
        CorrectionThreeTree.Write(Path.Combine(cache, "torch-cu126.whl"), 2000);

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        RuntimeStamp.Burn(settings, paths, RuntimeVariants.Cpu);

        var vm = new SettingsViewModel(
            settings, new JsonSettingsStore(paths.SettingsPath), paths, null, new DriverRequirement());

        Assert.Equal(1000, vm.CacheBytes);          // cu126 専用の 2000 は数えない
        vm.ClearCache();
        Assert.Equal(0, vm.CacheBytes);
        Assert.True(File.Exists(Path.Combine(cache, "torch-cu126.whl")));
    }

    public void Dispose()
    {
        AppServices.Reset();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 同上
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }

        GC.SuppressFinalize(this);
    }
}

// ================================================================ 初回取得ウィザード

/// <summary>medium＝起動の段の「中断」が効く／low＝戻る・Trail・空き容量。</summary>
public sealed class CorrectionThreeWizardTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-c3-wiz-" + Guid.NewGuid().ToString("N"));

    private AppPaths MakeTree()
    {
        var paths = CorrectionThreeTree.MakePaths(_root);
        Directory.CreateDirectory(paths.LicensesDir);
        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文", new UTF8Encoding(false));
        return paths;
    }

    private FirstRunViewModel NewWizard(
        AppPaths paths,
        LauncherSettings settings,
        Func<CancellationToken, Task<bool>> startServer)
    {
        var vm = new FirstRunViewModel(
            paths,
            settings,
            new JsonSettingsStore(paths.SettingsPath),
            new DriverRequirement(),
            static () => new NoopDownloader(),
            static () => new NoopInstaller(),
            startServer)
        {
            ModelFetcher = static (_, _) => Task.FromResult(true),
        };

        return vm;
    }

    [Fact]
    public async Task 起動の段でも中断が効く()
    {
        // 壊れていた形＝_cancel は直前の段の finally で null になり、RunStartAsync は
        // CancellationTokenSource を作らず _startServer にも token を渡さなかった。
        // それでもボタンは押せて「中断しました」と名乗り、Done まで進んで
        // firstRunCompleted=true が焼かれた（ready 待ちは最長 600 秒）。
        var paths = MakeTree();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        FirstRunViewModel? wizard = null;
        var sawToken = false;
        var couldCancel = false;
        var calls = 0;

        wizard = NewWizard(paths, settings, token =>
        {
            calls++;
            sawToken = token.CanBeCanceled;
            couldCancel = wizard!.CancelCommand.CanExecute(null);
            wizard.CancelRunning();                  // 段の中で「中断」を押す
            token.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        });

        wizard.Accepted = true;
        await wizard.NextAsync();                    // 通知 → 変種
        await wizard.NextAsync();                    // 変種 → 取得…起動まで自動で進む

        Assert.Equal(1, calls);
        Assert.True(sawToken);                       // token が届いている
        Assert.True(couldCancel);                    // その段で押せる
        Assert.Equal(FirstRunStep.Start, wizard.Step);   // Done へ進まない
        Assert.False(wizard.LastStepOk);
        Assert.Equal("中断しました（続きから取り直せます）。", wizard.Message);
        Assert.False(settings.FirstRunCompleted);    // 焼かれない
    }

    [Fact]
    public async Task 起こす側が取消を例外で返さなくても中断と読む()
    {
        // IServerProcess.StartAsync は取消でも例外を投げず Stopped で返る（契約）。
        var paths = MakeTree();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        FirstRunViewModel? wizard = null;

        wizard = NewWizard(paths, settings, _ =>
        {
            wizard!.CancelRunning();
            return Task.FromResult(false);           // 「起こせませんでした」ではなく中断
        });

        wizard.Accepted = true;
        await wizard.NextAsync();
        await wizard.NextAsync();

        Assert.Equal("中断しました（続きから取り直せます）。", wizard.Message);
        Assert.False(settings.FirstRunCompleted);
    }

    [Fact]
    public void 止める物が無いときは中断しましたと名乗らない()
    {
        var paths = MakeTree();
        var wizard = NewWizard(
            paths, new LauncherSettings { Variant = RuntimeVariants.Cpu },
            static _ => Task.FromResult(true));

        wizard.CancelRunning();

        Assert.Equal("いま中断できる仕事はありません。", wizard.Message);
    }

    [Fact]
    public async Task 働く段からの戻るは変種の段へ戻る()
    {
        // 壊れていた形＝1 つ前の段へ戻すので、次の「次へ」は**失敗した段ではなく前の段**を
        // やり直した（取得の段に戻れば 1.4〜2.6 GiB の注文がまた出る）。
        var paths = MakeTree();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var wizard = NewWizard(paths, settings, static _ => Task.FromResult(false));

        wizard.Accepted = true;
        await wizard.NextAsync();
        await wizard.NextAsync();                     // 起動の段で止まる（起こせない）

        Assert.Equal(FirstRunStep.Start, wizard.Step);
        Assert.False(wizard.LastStepOk);

        wizard.Back();

        Assert.Equal(FirstRunStep.Variant, wizard.Step);
        Assert.True(wizard.LastStepOk);
    }

    [Fact]
    public async Task 同じ段を撃ち直してもTrailは重ねない()
    {
        var paths = MakeTree();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var wizard = NewWizard(paths, settings, static _ => Task.FromResult(false));

        wizard.Accepted = true;
        await wizard.NextAsync();
        await wizard.NextAsync();                     // 起動の段で失敗
        var first = wizard.Trail.Count(static t => t.Contains("起動の確認", StringComparison.Ordinal));

        await wizard.NextAsync();                     // 「もう一度」
        var second = wizard.Trail.Count(static t => t.Contains("起動の確認", StringComparison.Ordinal));

        Assert.Equal(1, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public void 空きが足りなければ1行で名乗る()
    {
        // 壊れていた形＝EstimatedPeakDiskBytes はウィザードの 1 行に出るだけで、
        // どこも実際の空きと突き合わせていなかった。
        Assert.Null(FirstRunViewModel.FreeSpaceShortfall(1000, 1000));
        Assert.Null(FirstRunViewModel.FreeSpaceShortfall(1000, null));     // 読めない＝黙る

        var line = FirstRunViewModel.FreeSpaceShortfall(1000, 400);
        Assert.NotNull(line);
        Assert.Contains("空き容量が足りません", line!, StringComparison.Ordinal);
        Assert.Contains(FetchPlanner.FormatBytes(600), line!, StringComparison.Ordinal);
    }

    [Fact]
    public void 空きは実際のドライブから読める()
    {
        Assert.NotNull(FirstRunViewModel.FreeBytes(_root));
    }

    private sealed class NoopDownloader : IDownloader
    {
        public Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(
            IReadOnlyList<DownloadRequest> requests,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            foreach (var request in requests)
            {
                File.WriteAllBytes(request.DestinationPath, new byte[request.ExpectedSize ?? 0]);
            }

            IReadOnlyList<DownloadResult> results =
            [.. requests.Select(static r => new DownloadResult(
                true, r.DestinationPath, r.ExpectedSize ?? 0, r.Sha256, r.Url, false, false, 1, null))];
            return Task.FromResult(results);
        }

        public Task<DownloadResult> DownloadAsync(
            DownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class NoopInstaller : IRuntimeInstaller
    {
        public Task<InstallResult> InstallAsync(
            InstallRequest request, IProgress<InstallProgress>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new InstallResult(true, 2, 4096, 2, [], null, null));
    }

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
            // 同上
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }

        GC.SuppressFinalize(this);
    }
}

// ================================================================ 設定頁と組み直し

/// <summary>high ⑵＝「適用」でウィザードの成果を巻き戻さない／medium＝組み直しの取消と帯。</summary>
[Collection(AppServicesCollection.Name)]
public sealed class CorrectionThreeSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-c3-set-" + Guid.NewGuid().ToString("N"));

    private MainViewModel NewMain(AppPaths paths, LauncherSettings settings, ISettingsStore store)
    {
        AppServices.Server = new CorrectionThreeTree.QuietServer();
        AppServices.GpuEnumerator = null;
        return new MainViewModel(paths, settings, store, new CorrectionThreeTree.SilentPlayer());
    }

    [Fact]
    public void ウィザードが焼いた後に適用しても巻き戻らない()
    {
        // 壊れていた形＝_draft は MainViewModel の構築時の写しで、ウィザードはその後に
        // 同じ個体を書き換えるのに、写しを取り直す口が無かった。「適用」1 押しで
        // firstRunCompleted・acceptedNoticesSha256・焼き印・変種が構築時の値へ巻き戻った。
        var paths = CorrectionThreeTree.MakePaths(_root);
        CorrectionThreeTree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var store = new JsonSettingsStore(paths.SettingsPath);
        var live = new LauncherSettings { Variant = RuntimeVariants.Cu126 };
        var main = NewMain(paths, live, store);              // ここで写しが採られる

        // ウィザードを真似る（同じ個体を書き換えて保存する）
        var notices = new string('a', 64);
        live.Variant = RuntimeVariants.Cpu;
        live.FirstRunCompleted = true;
        live.AcceptedNoticesSha256 = notices;
        RuntimeStamp.Burn(live, paths, RuntimeVariants.Cpu);
        store.Save(live);

        // 窓がウィザードを閉じた後にやること（MainWindow.ShowFirstRun）
        main.ReapplySettings();

        main.Settings.Port = 18099;
        main.Settings.Apply();

        Assert.Equal(
            "設定を保存しました。変種・GPU・ポートを変えたときは、サーバを起動し直すと効きます。",
            main.Settings.Message);

        Assert.True(live.FirstRunCompleted);
        Assert.Equal(notices, live.AcceptedNoticesSha256);
        Assert.Equal(RuntimeVariants.Cpu, live.Variant);
        Assert.Equal(
            RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu),
            live.RuntimeLedgerFor(RuntimeVariants.Cpu));
        Assert.Equal(18099, live.Port);

        // 檔にも残る（次の起動がウィザードを開き直さない）
        var written = store.Load();
        Assert.True(written.FirstRunCompleted);
        Assert.Equal(notices, written.AcceptedNoticesSha256);
        Assert.Equal(RuntimeVariants.Cpu, written.Variant);
        Assert.Matches(
            "^[0-9a-f]{64}$", written.RuntimeLedgerFor(RuntimeVariants.Cpu) ?? string.Empty);
    }

    [Fact]
    public void 写しの取り直しは編集中なら何もしない()
    {
        var paths = CorrectionThreeTree.MakePaths(_root);
        CorrectionThreeTree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var store = new JsonSettingsStore(paths.SettingsPath);
        var live = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var main = NewMain(paths, live, store);

        main.Settings.Port = 18098;                 // 未保存の編集
        main.ReapplySettings();

        Assert.Equal(18098, main.Settings.Port);    // 捨てない
        Assert.True(main.Settings.IsDirty);
    }

    [Fact]
    public async Task 組み直しは取消と進捗帯を持つ()
    {
        // 壊れていた形＝取得も展開も CancellationToken.None で撃ち、進捗は 20 行の
        // ログ帯に流れるだけだった（cache は自動削除で空なのが常態＝止める手が要る）。
        var paths = CorrectionThreeTree.MakePaths(_root);
        CorrectionThreeTree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var store = new JsonSettingsStore(paths.SettingsPath);
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        settings.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", AppVersion.Display);

        var main = NewMain(paths, settings, store);
        var downloader = new CancelWatchingDownloader();
        AppServices.Downloader = downloader;
        AppServices.RuntimeInstaller = new NeverInstaller();

        var busyDuringRun = false;
        var canCancelDuringRun = false;
        var bandDuringRun = string.Empty;
        downloader.OnCall = () =>
        {
            busyDuringRun = main.Status.IsRebuilding;
            canCancelDuringRun = main.Status.CancelRebuildCommand.CanExecute(null);
            main.CancelRebuildRuntime();
        };

        await main.RebuildRuntimeAsync();
        bandDuringRun = downloader.BandSeen;

        Assert.True(downloader.Cancellable);              // token が届いている
        Assert.True(busyDuringRun);
        Assert.True(canCancelDuringRun);
        Assert.False(string.IsNullOrWhiteSpace(bandDuringRun));   // 帯に 1 行出た
        Assert.False(main.Status.IsRebuilding);           // 後始末で下ろす
        Assert.False(main.Status.CancelRebuildCommand.CanExecute(null));
        Assert.Contains("やめました", main.Status.LogText, StringComparison.Ordinal);

        // 焼き印は動かさない（組み直していないのだから）
        Assert.Equal("0123456789abcdef", settings.RuntimeLedgerFor(RuntimeVariants.Cpu));
    }

    private sealed class CancelWatchingDownloader : IDownloader
    {
        public bool Cancellable { get; private set; }

        public string BandSeen { get; private set; } = string.Empty;

        public Action? OnCall { get; set; }

        public Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(
            IReadOnlyList<DownloadRequest> requests,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            Cancellable = cancellationToken.CanBeCanceled;
            progress?.Report(new DownloadProgress(
                "torch-1.whl", DownloadPhase.Downloading, 1, 2, 0.5, null, 1, null));
            BandSeen = "reported";
            OnCall?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<DownloadResult> results = [];
            return Task.FromResult(results);
        }

        public Task<DownloadResult> DownloadAsync(
            DownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class NeverInstaller : IRuntimeInstaller
    {
        public Task<InstallResult> InstallAsync(
            InstallRequest request, IProgress<InstallProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("取消の後は呼ばれてはならない。");
    }

    public void Dispose()
    {
        AppServices.Reset();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 同上
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }

        GC.SuppressFinalize(this);
    }
}
