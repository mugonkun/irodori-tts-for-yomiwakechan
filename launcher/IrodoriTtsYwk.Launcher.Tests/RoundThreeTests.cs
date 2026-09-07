using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Mvvm;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Ledger;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// <see cref="AppServices"/>（静的な差し替え口）を触る檔を並べて走らせないための collection。
/// </summary>
[CollectionDefinition(Name)]
public sealed class AppServicesCollection
{
    public const string Name = "AppServices";
}

/// <summary>
/// 便 D（3）＝ランチャ席の釘（裁定 90・91・92 の low 6・94 ⑴⑶・設計書 §20-5 ⑴）。
/// <para>
/// <b>実機・実 GPU・実ポート・外への取得には 1 つも触れない</b>（外部取得は E2E 席だけ）。
/// 子プロセスを起こすのは「起こせない実行檔」の 1 本だけで、それも起きない
/// （<c>Process.Start</c> が <c>Win32Exception</c> を返す路の釘）。
/// </para>
/// </summary>
public sealed class RoundThreeWizardTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-d3-wiz-" + Guid.NewGuid().ToString("N")[..8]);

    // ---- 雛形 ---------------------------------------------------------------

    private AppPaths Paths
    {
        get
        {
            var appDir = Path.Combine(_root, "app");
            Directory.CreateDirectory(Path.Combine(appDir, "ledger"));
            Directory.CreateDirectory(Path.Combine(appDir, "licenses"));
            Directory.CreateDirectory(Path.Combine(_root, "data"));
            return new AppPaths(
                Path.Combine(_root, "install"),
                appDir,
                Path.Combine(_root, "runtime"),
                Path.Combine(_root, "data"),
                developerMode: true);
        }
    }

    /// <summary>取得台帳 2 本と通知文（＝ウィザードが最後まで進める最小の配布樹）。</summary>
    private AppPaths MakeTree()
    {
        var paths = Paths;
        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文", new UTF8Encoding(false));
        File.WriteAllText(
            paths.LedgerPath("python-embed"),
            """
            {"schema":1,"name":"python-embed","items":[
              {"kind":"python-embed","name":"python-embed","url":"https://example.invalid/p.zip",
               "sha256":"aa","size":100}]}
            """,
            new UTF8Encoding(false));
        File.WriteAllText(
            paths.LedgerPath("runtime-cpu"),
            """
            {"schema":1,"name":"runtime-cpu","items":[
              {"kind":"wheel","name":"torch","url":"https://example.invalid/t.whl",
               "sha256":"bb","size":1000}]}
            """,
            new UTF8Encoding(false));
        return paths;
    }

    /// <summary>1 檔も落とさずに「落とせた」と答える偽の取得系（外へ 1 バイトも出さない）。</summary>
    private sealed class HappyDownloader : IDownloader
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(
            IReadOnlyList<DownloadRequest> requests,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            IReadOnlyList<DownloadResult> results =
            [.. requests.Select(static r => new DownloadResult(
                    true, r.DestinationPath, r.ExpectedSize ?? 0, r.Sha256, r.Url,
                    false, true, 1, null))];
            return Task.FromResult(results);
        }

        public Task<DownloadResult> DownloadAsync(
            DownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new DownloadResult(
                true, request.DestinationPath, request.ExpectedSize ?? 0, request.Sha256,
                request.Url, false, true, 1, null));

        public void Dispose()
        {
        }
    }

    /// <summary>展開したことにする偽の展開系（1 檔も置かない）。</summary>
    private sealed class HappyInstaller : IRuntimeInstaller
    {
        public int Calls { get; private set; }

        public Task<InstallResult> InstallAsync(
            InstallRequest request, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new InstallResult(true, 26523, 4_250_000_000, 101, [], null, null));
        }
    }

    private FirstRunViewModel NewWizard(
        AppPaths paths,
        LauncherSettings settings,
        IDownloader? downloader,
        IRuntimeInstaller? installer,
        Func<CancellationToken, Task<bool>>? startServer = null,
        Func<IProgress<string>, CancellationToken, Task<bool>>? models = null)
    {
        var vm = new FirstRunViewModel(
            paths,
            settings,
            new JsonSettingsStore(paths.SettingsPath),
            new DriverRequirement(),
            () => downloader,
            () => installer,
            startServer ?? (static _ => Task.FromResult(true)));

        vm.ModelFetcher = models ?? ((_, _) => Task.FromResult(true));
        return vm;
    }

    // ---- 裁定 94 ⑴＝自動進行（押下 9 → 5） ----------------------------------

    [Fact]
    public async Task 初回_成功した段は押さずに次へ進む()
    {
        // 便 E（2）の E2E は 9 押下（同意チェック・同意して次へ・変種・取得を始める・
        // 「次へ」4・試し撃ちへ）で、受け入れ条件「利用者操作 ≤ 6」を落としていた。
        var paths = MakeTree();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var downloader = new HappyDownloader();
        var installer = new HappyInstaller();
        var vm = NewWizard(paths, settings, downloader, installer);

        var presses = 0;

        vm.Accepted = true;                                  // ⑴ 同意チェック
        presses++;

        await vm.NextAsync();                                // ⑵ 同意して次へ
        presses++;
        Assert.Equal(FirstRunStep.Variant, vm.Step);

        vm.Variant = RuntimeVariants.Cpu;                    // ⑶ 変種
        presses++;

        await vm.NextAsync();                                // ⑷ 取得を始める → 完了まで自動
        presses++;

        Assert.Equal(FirstRunStep.Done, vm.Step);
        Assert.True(settings.FirstRunCompleted);
        Assert.Equal(1, downloader.Calls);
        Assert.Equal(1, installer.Calls);

        var closed = false;
        vm.Completed += (_, _) => closed = true;
        await vm.NextAsync();                                // ⑸ 試し撃ちへ
        presses++;

        Assert.True(closed);
        Assert.Equal(5, presses);                            // 裁定 94 ⑴＝押下 5
    }

    [Fact]
    public async Task 初回_失敗した段で止まり理由ともう一度を出す()
    {
        var paths = MakeTree();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };

        // 展開系が差さっていない＝展開の段で必ず失敗する（取得は通る）。
        var vm = NewWizard(paths, settings, new HappyDownloader(), installer: null);

        vm.Accepted = true;
        await vm.NextAsync();
        await vm.NextAsync();

        Assert.Equal(FirstRunStep.Install, vm.Step);         // 取得は自動で通り、展開で止まった
        Assert.False(vm.LastStepOk);
        Assert.Equal("もう一度", vm.NextButtonText);
        Assert.Contains("展開系", vm.Message, StringComparison.Ordinal);
        Assert.False(settings.FirstRunCompleted);            // 完了は焼かれない
    }

    [Fact]
    public async Task 初回_もう一度で直ればそこから先も自動で進む()
    {
        var paths = MakeTree();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };

        IRuntimeInstaller? installer = null;
        var vm = NewWizard(paths, settings, new HappyDownloader(), installer: null);

        // 展開系の差さり方を後から変えられるように、遅延で読む口を作り直す。
        var live = new FirstRunViewModel(
            paths,
            settings,
            new JsonSettingsStore(paths.SettingsPath),
            new DriverRequirement(),
            static () => new HappyDownloader(),
            () => installer,
            static _ => Task.FromResult(true))
        {
            ModelFetcher = (_, _) => Task.FromResult(true),
        };

        live.Accepted = true;
        await live.NextAsync();
        await live.NextAsync();
        Assert.Equal(FirstRunStep.Install, live.Step);
        Assert.False(live.LastStepOk);

        installer = new HappyInstaller();
        await live.NextAsync();                              // 「もう一度」＝ここから先も自動

        Assert.Equal(FirstRunStep.Done, live.Step);
        Assert.True(live.LastStepOk);
        Assert.True(settings.FirstRunCompleted);
        GC.KeepAlive(vm);
    }

    [Fact]
    public async Task 初回_段の進みはTrailに残る()
    {
        var paths = MakeTree();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var vm = NewWizard(paths, settings, new HappyDownloader(), new HappyInstaller());

        vm.Accepted = true;
        await vm.NextAsync();
        await vm.NextAsync();

        var trail = string.Join("\n", vm.Trail);
        Assert.Contains("通知に同意しました。", trail, StringComparison.Ordinal);
        Assert.Contains("― 取得（実行系）", trail, StringComparison.Ordinal);
        Assert.Contains("― 展開", trail, StringComparison.Ordinal);
        Assert.Contains("― 取得（モデル）", trail, StringComparison.Ordinal);
        Assert.Contains("― 起動の確認", trail, StringComparison.Ordinal);
        Assert.Contains("初回取得が終わりました。", trail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 初回_取消は走っている段を止めて先へ進まない()
    {
        var paths = MakeTree();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };

        // 取得の最中に「中断」を押す（取消は各段で効く＝裁定 94 ⑴）。
        FirstRunViewModel? wizard = null;
        var downloader = new CancelOnCallDownloader(() => wizard!.CancelRunning());
        wizard = NewWizard(paths, settings, downloader, new HappyInstaller());

        wizard.Accepted = true;
        await wizard.NextAsync();
        await wizard.NextAsync();

        Assert.Equal(FirstRunStep.Download, wizard.Step);    // 展開へ進んでいない
        Assert.False(wizard.LastStepOk);
        Assert.Contains("中断", wizard.Message, StringComparison.Ordinal);
        Assert.False(settings.FirstRunCompleted);
    }

    /// <summary>呼ばれた瞬間に「中断」を押させる偽の取得系。</summary>
    private sealed class CancelOnCallDownloader(Action cancel) : IDownloader
    {
        public Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(
            IReadOnlyList<DownloadRequest> requests,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancel();
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

    // ---- 裁定 91＝展開に使った台帳の焼き印 ------------------------------------

    [Fact]
    public async Task 焼き印_展開が通ると台帳のsha256と版が焼かれる()
    {
        var paths = MakeTree();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var vm = NewWizard(paths, settings, new HappyDownloader(), new HappyInstaller());

        vm.Accepted = true;
        await vm.NextAsync();
        await vm.NextAsync();

        Assert.Equal(
            RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu),
            settings.RuntimeLedgerFor(RuntimeVariants.Cpu));
        Assert.Equal(AppVersion.Display, settings.InstalledAppVersionFor(RuntimeVariants.Cpu));

        // **焼くのはその変種の欄だけ**（是正・便 D（3）の 3 巡目）
        Assert.Null(settings.RuntimeLedgerFor(RuntimeVariants.Cu126));

        // 檔にも落ちている（次の起動が読む）
        var written = new JsonSettingsStore(paths.SettingsPath).Load();
        Assert.Equal(
            settings.RuntimeLedgerFor(RuntimeVariants.Cpu),
            written.RuntimeLedgerFor(RuntimeVariants.Cpu));
        Assert.Equal(
            settings.InstalledAppVersionFor(RuntimeVariants.Cpu),
            written.InstalledAppVersionFor(RuntimeVariants.Cpu));
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// 裁定 91 の突合と裁定 90 の cache 削除（<b>どちらも純関数の釘</b>）。
/// </summary>
public sealed class RoundThreeStampAndCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-d3-cache-" + Guid.NewGuid().ToString("N")[..8]);

    private AppPaths MakePaths()
    {
        var appDir = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(appDir, "ledger"));
        Directory.CreateDirectory(Path.Combine(_root, "data", "cache"));
        return new AppPaths(
            Path.Combine(_root, "install"),
            appDir,
            Path.Combine(_root, "runtime"),
            Path.Combine(_root, "data"),
            developerMode: true);
    }

    private void WriteLedgers(AppPaths paths, string runtimeBody)
    {
        File.WriteAllText(
            paths.LedgerPath("python-embed"),
            """
            {"schema":1,"items":[
              {"kind":"python-embed","name":"python-embed",
               "url":"https://example.invalid/python-embed.zip","sha256":"aa","size":100}]}
            """,
            new UTF8Encoding(false));
        File.WriteAllText(paths.LedgerPath("runtime-cpu"), runtimeBody, new UTF8Encoding(false));
    }

    private const string RuntimeLedgerV1 = """
        {"schema":1,"items":[
          {"kind":"wheel","name":"torch","url":"https://example.invalid/torch-1.whl",
           "sha256":"bb","size":1000}]}
        """;

    private const string RuntimeLedgerV2 = """
        {"schema":1,"items":[
          {"kind":"wheel","name":"torch","url":"https://example.invalid/torch-2.whl",
           "sha256":"cc","size":2000}]}
        """;

    private void MakeRuntime(AppPaths paths, string variant)
    {
        var dir = Path.Combine(paths.RuntimeRoot, variant);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, AppPaths.PythonExeName), []);

        // **展開が済んだ樹の形**（是正・便 D（3）の 3 巡目）＝site-packages の *.dist-info が
        // 台帳の wheel/sdist/archive の件数と合う。雛形の台帳は wheel 1 件なので 1 つ作る。
        Directory.CreateDirectory(Path.Combine(dir, "site-packages", "torch-1.dist-info"));
    }

    private FetchPlan Plan(AppPaths paths) =>
        FirstRunViewModel.TryPlan(paths, RuntimeVariants.Cpu, skipVcRedist: true)!;

    // ---- 裁定 91＝突合（純関数） ---------------------------------------------

    [Fact]
    public void 焼き印_台帳が変われば組み直す1行を出す()
    {
        var verdict = RuntimeStamp.Compare(
            "aaaa", "v0.1.0", "bbbb", "v0.1.0", runtimeInstalled: true, RuntimeVariants.Cpu);

        Assert.True(verdict.Mismatch);
        Assert.True(verdict.LedgerChanged);
        Assert.Contains("runtime-cpu.json", verdict.Line!, StringComparison.Ordinal);
        Assert.Contains("組み直して", verdict.Line!, StringComparison.Ordinal);
    }

    [Fact]
    public void 焼き印_合っていれば黙る()
    {
        var verdict = RuntimeStamp.Compare(
            "AAAA", "v0.1.0", "aaaa", "v0.1.0", runtimeInstalled: true, RuntimeVariants.Cpu);

        Assert.False(verdict.Mismatch);   // sha256 の大小は問わない
        Assert.Null(verdict.Line);
    }

    [Fact]
    public void 焼き印_版だけ違えば1手を出さない()
    {
        // 是正・便 D（3）の 3 巡目＝1 巡目は Line を返していたので Mismatch が真になり、
        // 「実行系を組み直す」が押せる状態のまま**毎起動**その 1 行を見せた（焼き直しも
        // しないので永久に消えない）。覚え書き（Note）に落とし、1 手は出さない。
        var verdict = RuntimeStamp.Compare(
            "aaaa", "v0.1.0", "aaaa", "v0.2.0", runtimeInstalled: true, RuntimeVariants.Cpu);

        Assert.False(verdict.Mismatch);
        Assert.Null(verdict.Line);
        Assert.False(verdict.LedgerChanged);
        Assert.True(verdict.AppVersionChanged);
        Assert.Contains("そのまま使えます", verdict.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public void 焼き印_実行系が無い間と焼き印が無い間は黙る()
    {
        // 実行系がまだ無い＝初回取得の仕事（ウィザードが出る）
        Assert.False(RuntimeStamp
            .Compare("aaaa", "v0.1.0", "bbbb", "v0.1.0", runtimeInstalled: false, RuntimeVariants.Cpu)
            .Mismatch);

        // 焼き印が無い＝古い settings.json・台本で組んだ樹（腐っている証拠ではない）
        Assert.False(RuntimeStamp
            .Compare(null, null, "bbbb", "v0.1.0", runtimeInstalled: true, RuntimeVariants.Cpu)
            .Mismatch);

        // 配布樹の台帳が読めない＝推測で急かさない
        Assert.False(RuntimeStamp
            .Compare("aaaa", "v0.1.0", null, "v0.1.0", runtimeInstalled: true, RuntimeVariants.Cpu)
            .Mismatch);
    }

    [Fact]
    public void 焼き印_台帳を1字変えるとsha256が動く()
    {
        var paths = MakePaths();
        WriteLedgers(paths, RuntimeLedgerV1);
        var first = RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu);

        WriteLedgers(paths, RuntimeLedgerV2);
        var second = RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.Equal(64, first!.Length);
    }

    // ---- 裁定 90 Q-E2 ⑶＝cache の削除 ---------------------------------------

    [Fact]
    public void cache_置き場の中身を残さず消す()
    {
        // 取得系だけが書く置き場なので、**今の台帳が名指す檔だけ**を選ぶと⒜ 前の台帳の原檔
        // ⒝ 打ち切った .part ⒞ 名前が変わった檔 が残り、「展開後（cache 削除後）」の実測が
        // 合わなくなる（裁定 94 ⑶）。関門を通った後は置き場ごと空にする（置き場自身は残す）。
        var paths = MakePaths();
        WriteLedgers(paths, RuntimeLedgerV1);
        MakeRuntime(paths, RuntimeVariants.Cpu);

        var cache = paths.DownloadCacheDir;
        File.WriteAllBytes(Path.Combine(cache, "torch-1.whl"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(cache, "python-embed.zip"), new byte[100]);
        File.WriteAllBytes(Path.Combine(cache, "torch-0.whl.part"), new byte[7]);   // 前の台帳の残骸
        Directory.CreateDirectory(Path.Combine(cache, "nested"));
        File.WriteAllBytes(Path.Combine(cache, "nested", "old.bin"), new byte[64]);

        var sha = RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu);
        var result = CacheCleaner.Clean(cache, runtimeInstalled: true, sha, sha);

        Assert.True(result.Ok);
        Assert.Equal(4, result.Files);
        Assert.Equal(1171, result.Bytes);
        Assert.Empty(Directory.GetFiles(cache, "*", SearchOption.AllDirectories));
        Assert.True(Directory.Exists(cache));                    // 置き場そのものは残す

        // 消したバイトを 1 行で名乗る（Trail とログに出す文言）
        Assert.Contains("4 檔", result.Message, StringComparison.Ordinal);
        Assert.Contains(FetchPlanner.FormatBytes(1171), result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void cache_pythonexeが無ければ1檔も消さない()
    {
        var paths = MakePaths();
        WriteLedgers(paths, RuntimeLedgerV1);
        var cache = paths.DownloadCacheDir;
        File.WriteAllBytes(Path.Combine(cache, "torch-1.whl"), new byte[1000]);

        var sha = RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu);
        var result = CacheCleaner.Clean(cache, runtimeInstalled: false, sha, sha);

        Assert.False(result.Ok);
        Assert.Equal(0, result.Files);
        Assert.True(File.Exists(Path.Combine(cache, "torch-1.whl")));
        Assert.Contains("実行系がまだ組み上がっていない", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void cache_台帳のsha256が食い違えば1檔も消さない()
    {
        // 裁定 90 ⑶ の ⒞＝python.exe と台帳の sha256 の一致を確かめてから消す。
        var paths = MakePaths();
        WriteLedgers(paths, RuntimeLedgerV1);
        MakeRuntime(paths, RuntimeVariants.Cpu);
        var cache = paths.DownloadCacheDir;
        File.WriteAllBytes(Path.Combine(cache, "torch-1.whl"), new byte[1000]);

        var result = CacheCleaner.Clean(
            cache,
            runtimeInstalled: true,
            storedLedgerSha256: "0123456789abcdef",
            currentLedgerSha256: RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu));

        Assert.False(result.Ok);
        Assert.True(File.Exists(Path.Combine(cache, "torch-1.whl")));
        Assert.Contains("組み直して", result.Message, StringComparison.Ordinal);

        // 焼き印そのものが無い機体も同じ（判らない物は消さない）
        Assert.NotNull(CacheCleaner.Blocked(true, null, "aaaa"));
    }

    [Fact]
    public void cache_量は入れ子まで数える()
    {
        var paths = MakePaths();
        WriteLedgers(paths, RuntimeLedgerV1);
        var cache = paths.DownloadCacheDir;
        File.WriteAllBytes(Path.Combine(cache, "torch-1.whl"), new byte[1000]);
        Directory.CreateDirectory(Path.Combine(cache, "nested"));
        File.WriteAllBytes(Path.Combine(cache, "nested", "old.bin"), new byte[24]);

        var measured = CacheCleaner.Measure(cache);
        Assert.Equal(2, measured.Files);
        Assert.Equal(1024, measured.Bytes);

        // 台帳の名は「今の台帳の物か」を言うためだけに残してある（削除には使わない）
        Assert.Contains("torch-1.whl", CacheCleaner.FileNames(Plan(paths)));
    }

    [Fact]
    public void cache_設定の手動ボタンは量を出して消す()
    {
        // 裁定 90 Q-E2 ⑶ の ⒝＝「取得キャッシュを消す（n GB）」
        var paths = MakePaths();
        WriteLedgers(paths, RuntimeLedgerV1);
        MakeRuntime(paths, RuntimeVariants.Cpu);
        var cache = paths.DownloadCacheDir;
        File.WriteAllBytes(Path.Combine(cache, "torch-1.whl"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(cache, "python-embed.zip"), new byte[100]);

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        RuntimeStamp.Burn(settings, paths, RuntimeVariants.Cpu);

        var vm = new SettingsViewModel(
            settings, new JsonSettingsStore(paths.SettingsPath), paths, null, new DriverRequirement());

        Assert.Equal(1100, vm.CacheBytes);
        Assert.Contains("取得キャッシュを消す（", vm.ClearCacheText, StringComparison.Ordinal);
        Assert.Contains(FetchPlanner.FormatBytes(1100), vm.ClearCacheText, StringComparison.Ordinal);
        Assert.True(vm.ClearCacheCommand.CanExecute(null));

        vm.ClearCache();

        Assert.Equal(0, vm.CacheBytes);
        Assert.False(vm.ClearCacheCommand.CanExecute(null));  // 空なら押せない
        Assert.Contains("取得キャッシュを消しました", vm.CacheMessage, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(cache, "torch-1.whl")));
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// 裁定 94 ⑶＝展開係数を変種ごとの実測に（見積りの文言で実測と推定を出し分ける）。
/// </summary>
public sealed class RoundThreeExpansionTests
{
    [Theory]
    [InlineData(RuntimeVariants.Cu126, 1.69, true)]
    [InlineData(RuntimeVariants.RocmGfx1151, 2.98, true)]
    [InlineData(RuntimeVariants.Cpu, 3.60, true)]
    [InlineData(RuntimeVariants.Cu130, 3.3, false)]
    [InlineData(RuntimeVariants.CudaLabel, 3.3, false)]
    public void 係数は変種ごとの実測(string variant, double factor, bool measured)
    {
        var expansion = FetchPlanner.ExpansionFactorFor(variant);
        Assert.Equal(factor, expansion.Factor, 3);
        Assert.Equal(measured, expansion.Measured);
    }

    [Fact]
    public void 係数_大小と綴りの揺れを受ける()
    {
        Assert.True(FetchPlanner.ExpansionFactorFor(" CU126 ").Measured);
        Assert.True(FetchPlanner.ExpansionFactorFor("rocm-gfx1200").Measured);
        Assert.False(FetchPlanner.ExpansionFactorFor(null).Measured);
    }

    [Fact]
    public void 見積りの文言は実測と推定を出し分ける()
    {
        Assert.Equal("実測 2.98 倍", FetchPlanner.ExpansionFactorFor(RuntimeVariants.RocmGfx1151).Describe());
        Assert.Equal("推定 3.3 倍", FetchPlanner.ExpansionFactorFor(RuntimeVariants.Cu130).Describe());

        var plan = new FetchPlan(RuntimeVariants.Cu126, [], 0);
        Assert.Contains("（展開は実測 1.69 倍）", plan.Summary(), StringComparison.Ordinal);
    }

    [Fact]
    public void 必要な空きは変種ごとの係数で出る()
    {
        // cu126 の実測 1.69＝2 巡目の一律 3.3 は倍近い空きを要求していた（裁定 94 ⑶）。
        var embed = new LedgerFile
        {
            Items = [new LedgerItem
            {
                Kind = LedgerItemKinds.PythonEmbed, Name = "python-embed",
                Url = "https://example.invalid/p.zip", Sha256 = "aa", Size = 1000,
            }],
        };
        var runtime = new LedgerFile
        {
            Items = [new LedgerItem
            {
                Kind = LedgerItemKinds.Wheel, Name = "torch",
                Url = "https://example.invalid/t.whl", Sha256 = "bb", Size = 9000,
            }],
        };

        var cu126 = FetchPlanner.Plan(RuntimeVariants.Cu126, embed, runtime, null, null);
        var cu130 = FetchPlanner.Plan(RuntimeVariants.Cu130, embed, runtime, null, null);

        Assert.Equal((long)(10000 * 1.69), cu126.EstimatedRuntimeBytes);
        Assert.Equal((long)(10000 * 3.3), cu130.EstimatedRuntimeBytes);
        Assert.True(cu126.EstimatedPeakDiskBytes < cu130.EstimatedPeakDiskBytes);
    }
}

/// <summary>
/// 裁定 92 が便 D（3）へ残した low 6 件（画面側）と、設計書 §20-5 ⑴ の受け口。
/// </summary>
public sealed class RoundThreeLowSixTests
{
    // ---- ⑴ GB／GiB の綴り ---------------------------------------------------

    [Theory]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KiB")]
    [InlineData(1048576, "1.0 MiB")]
    [InlineData(1073741824, "1.00 GiB")]
    public void 綴りは1024進の物に揃える(long bytes, string expected)
    {
        // FetchPlanner.FormatBytes（GiB）と UiText.Bytes（GB）が同じ 1024 進を別の名で
        // 出していた＝台帳 4.79 GiB と帯 2.21 GB が同じ進法だと読めなかった。
        Assert.Equal(expected, UiText.Bytes(bytes));
        Assert.EndsWith("iB", UiText.Bytes(1024), StringComparison.Ordinal);
    }

    // ---- ⑵「（最大 …）」の置き場と memory.error の文言 ------------------------

    [Fact]
    public void 最大は使用量の隣に出る()
    {
        // max＝max_memory_allocated＝**使用量の山**。行末に置くと直前の
        // 「GPU 全体 x / y」に掛かる数に見えた（実射の帯＝§20-2）。
        var text = StatusViewModel.DescribeMemory(new MemoryStatus
        {
            Device = "cuda:0",
            AllocatedBytes = 1_073_741_824,
            ReservedBytes = 2_147_483_648,
            MaxAllocatedBytes = 3_221_225_472,
            GpuTotalBytes = 4_294_967_296,
            GpuUsedBytes = 3_221_225_472,
        });

        Assert.Contains("使用量 1.00 GiB（最大 3.00 GiB）", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("（最大", StringComparison.Ordinal)
            < text.IndexOf("占有量", StringComparison.Ordinal));

        // 裁定 110（2026-09-08）＝torch の gpu_used／gpu_total はもう帯に出さない＝
        // 行の末尾は占有量になり、「GPU 全体」は Windows の計数の組にだけ出る。
        Assert.EndsWith("占有量 2.00 GiB", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GPU 全体", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 動いていないのと未対応を別の1語で出す()
    {
        // 「未対応」＝起きている個体にこの口が無い（古い wrapper・上流の素の Server）。
        // 「サーバが動いていません」＝そもそも誰も起きていない。
        Assert.Equal(UiText.NotSupported, StatusViewModel.DescribeMemory(null, serverAnswered: true));
        Assert.Equal(UiText.NotRunning, StatusViewModel.DescribeMemory(null, serverAnswered: false));
        Assert.NotEqual(UiText.NotSupported, UiText.NotRunning);
    }

    [Fact]
    public void memoryのerrorは読めなかった欄の話だと判る文言で添える()
    {
        var text = StatusViewModel.DescribeMemory(new MemoryStatus
        {
            Device = "cuda:0",
            AllocatedBytes = 1024,
            Error = "mem_get_info が失敗しました。",
        });

        Assert.Contains("一部の欄が読めませんでした：mem_get_info が失敗しました。",
            text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, StatusViewModel.ErrorSuffix("   "));
    }

    [Fact]
    public void 帯は答えが来ているかで文言を変える()
    {
        var vm = new StatusViewModel(static () => Task.CompletedTask, static () => Task.CompletedTask);
        vm.ApplySettings(new LauncherSettings());

        Assert.Equal(UiText.NotRunning, vm.MemoryText);          // まだ 1 標本も来ていない

        vm.ApplyStatus(new StatusResponse());                    // 答えたが memory 欄が無い
        Assert.Equal(UiText.NotSupported, vm.MemoryText);

        vm.ApplyStatus(null);                                    // 口が閉じた
        Assert.Equal(UiText.NotRunning, vm.MemoryText);
    }

    // ---- ⑶ MemoryStatus.Latents の JSON null ---------------------------------

    [Fact]
    public void latentsがJSONのnullでも落ちない()
    {
        // System.Text.Json は**明示の null を初期化子より優先する**＝欄が来ない応答と
        // null が来た応答で挙動が割れ、後者は LatentCount の 1 語で NRE になった。
        var memory = JsonSerializer.Deserialize<MemoryStatus>(
            """{"device":"cuda:0","allocated":1024,"latents":null,"latents_total":null}""",
            JsonSettingsStore.JsonOptions)!;

        Assert.Equal(0, memory.LatentCount);
        Assert.Null(memory.EffectiveLatentsTotal);
        Assert.Null(memory.LatentBytesFor("琴葉茜"));
        Assert.Empty(memory.EffectiveLatents);

        // 状態帯の 3 欄がどれも落ちない（見張りの標本ごとに描き直す路）
        Assert.Contains("使用量", StatusViewModel.DescribeMemory(memory), StringComparison.Ordinal);
        Assert.StartsWith("ON（焼いた話者 0 名",
            StatusViewModel.DescribeLatentCache(true, memory, null), StringComparison.Ordinal);
    }

    // ---- ⑸「GPU メモリの欄」の適用 --------------------------------------------

    [Fact]
    public void GPUメモリの欄は適用した瞬間に効く()
    {
        // 窓が構築時とウィザードを閉じたときにだけ叩いていたので、設定で外して「適用」を
        // 押しても次の起動まで欄が消えなかった。
        var vm = new StatusViewModel(static () => Task.CompletedTask, static () => Task.CompletedTask);

        vm.ApplySettings(new LauncherSettings { ShowMemoryPanel = true });
        Assert.True(vm.MemoryPanelVisible);

        var changed = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StatusViewModel.MemoryPanelVisible))
            {
                changed++;
            }
        };

        vm.ApplySettings(new LauncherSettings { ShowMemoryPanel = false });
        Assert.False(vm.MemoryPanelVisible);
        Assert.Equal(1, changed);
    }

    // ---- §20-5 ⑴＝AsyncRelayCommand の受け口 ---------------------------------

    [Fact]
    public async Task 手は全例外を受けて理由1行を出す()
    {
        // 1 巡目・2 巡目は 3 型だけを数え上げていたので、Process.Start が投げる
        // Win32Exception はどの catch にも当たらず、押した手が黙って消えた。
        var reasons = new List<string>();
        var command = new AsyncRelayCommand(
            static () => throw new Win32Exception(
                193, "is not a valid application for this OS platform."));
        command.Faulted += (_, line) => reasons.Add(line);

        await command.ExecuteAsync();

        Assert.Single(reasons);
        Assert.Contains("not a valid application", reasons[0], StringComparison.Ordinal);
        Assert.False(command.IsRunning);      // 走りっぱなしにしない
    }

    [Fact]
    public async Task 取消は失敗として出さない()
    {
        var faulted = 0;
        var command = new AsyncRelayCommand(
            static () => throw new OperationCanceledException());
        command.Faulted += (_, _) => faulted++;

        await command.ExecuteAsync();
        Assert.Equal(0, faulted);
    }

    [Fact]
    public void 文言の無い例外は型名で名乗る()
    {
        Assert.Equal("だめでした。", AsyncRelayCommand.Describe(new InvalidOperationException("だめでした。")));
        Assert.Equal(
            nameof(OperationCanceledException),
            AsyncRelayCommand.Describe(new OperationCanceledException(string.Empty)));
    }

    // ---- §20-5 ⑴＝ProcessRunner の「起こす」段の期限 --------------------------

    [Fact]
    public async Task 起こせない実行檔は期限を待たずに理由1行で返る()
    {
        // 模型の側は健全（8 ms で Failed）＝窓の側でだけ 60 秒黙っていた。ここは
        // 「起こせない」路が**期限より早く**理由つきで返ることの釘である。
        var runner = new ProcessRunner { StartTimeout = TimeSpan.FromSeconds(3) };
        var missing = Path.Combine(
            Path.GetTempPath(), "ywk-not-here-" + Guid.NewGuid().ToString("N")[..8] + ".exe");

        var watch = Stopwatch.StartNew();
        var result = await runner.RunAsync(
            missing, [], null, TimeSpan.FromSeconds(30), CancellationToken.None);
        watch.Stop();

        Assert.False(result.Started);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"起こす段が {watch.Elapsed} 掛かった");
    }

    [Fact]
    public void 起こす段の期限は3秒で理由1行を持つ()
    {
        // 1 度の「サーバ起動」で同じ python.exe を **3 回**起こす（窓の列挙・門の検分・子）ので、
        // 3 つとも止まっても 9 s＝無人検分の budget 10 s の内側に収まる値にしてある。
        Assert.Equal(TimeSpan.FromSeconds(3), ProcessRunner.DefaultStartTimeout);
        Assert.Equal(TimeSpan.FromSeconds(3), new ProcessRunner().StartTimeout);
        Assert.Equal(ProcessRunner.DefaultStartTimeout, Services.Server.ServerProcess.SpawnTimeout);
        Assert.True(ProcessRunner.DefaultStartTimeout * 3 < TimeSpan.FromSeconds(10));

        var line = ProcessRunner.StartStalledMessage(
            @"C:\data\runtime\cpu\python.exe", TimeSpan.FromSeconds(3));
        Assert.Contains("3 秒で返りませんでした", line, StringComparison.Ordinal);
        Assert.Contains("python.exe", line, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\data", line, StringComparison.Ordinal);  // 絶対パスは出さない
    }
}

/// <summary>
/// low 6 の ⑹＝走行中（409）で断られた焼きの待ち行列。
/// </summary>
public sealed class RoundThreePrecomputeQueueTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-d3-q-" + Guid.NewGuid().ToString("N")[..8]);

    private AppPaths MakePaths()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        return new AppPaths(
            Path.Combine(_root, "install"),
            Path.Combine(_root, "app"),
            Path.Combine(_root, "runtime"),
            Path.Combine(_root, "data"),
            developerMode: true);
    }

    /// <summary>断り方を台本で決められる偽の wrapper（他の口は使わない）。</summary>
    private sealed class ScriptedWrapper(params Func<int, WrapperResult<PrecomputeStartResult>>[] script)
        : IWrapperClient
    {
        public List<string[]> Calls { get; } = [];

        public Uri BaseAddress { get; } = new("http://127.0.0.1:18099/");

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

        public Task<WrapperResult<PrecomputeStartResult>> StartPrecomputeAsync(
            PrecomputeRequest request, CancellationToken cancellationToken)
        {
            Calls.Add([.. request.Ids ?? []]);
            var step = script[Math.Min(Calls.Count - 1, script.Length - 1)];
            return Task.FromResult(step(Calls.Count));
        }

        public static WrapperResult<PrecomputeStartResult> Busy() => new(
            false, null, 409,
            new ErrorBody { Code = VoicesViewModel.PrecomputeRunningCode, Message = "busy" },
            true, TimeSpan.Zero, null);

        /// <summary>繋がらない（<c>Available=true</c>＝口が無いのではなく届かない）。</summary>
        public static WrapperResult<PrecomputeStartResult> Down() => new(
            false, null, 0, null, true, TimeSpan.Zero, "繋がりません。");

        public static WrapperResult<PrecomputeStartResult> Ok() => new(
            true, new PrecomputeStartResult("run", 1, "running"), 202, null, true, TimeSpan.Zero, null);

        public Task<WrapperResult<VoicesResponse>> GetVoicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new WrapperResult<VoicesResponse>(
                false, null, 0, null, false, TimeSpan.Zero, "使わない"));

        public Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<StatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<ParamsResponse>> GetParamsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<VoicesResponse>> GetOpenAiVoicesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SpeechResult> SynthesizeAsync(
            SpeechRequest request, TimeSpan timeout, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<WarmupStartResult>> StartWarmupAsync(
            WarmupRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<CancelResult>> CancelWarmupAsync(
            string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<CancelResult>> CancelPrecomputeAsync(
            string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<DropLatentResult>> DropLatentAsync(
            string voiceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
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

    private VoicesViewModel NewVoices(IWrapperClient wrapper) =>
        new(null, null, new SilentPlayer(), () => wrapper, MakePaths(), new LauncherSettings());

    [Fact]
    public async Task 出し直しが落ちても覚えたままにする()
    {
        // 1 巡目・2 巡目は出し直す**前に**待ち行列を空にしていたので、出し直しが 409 以外
        // （サーバが落ちた・期限切れ）で落ちると覚えていた話者がそのまま消えた。
        var wrapper = new ScriptedWrapper(
            static _ => ScriptedWrapper.Busy(),   // 1 回目＝走行中
            static _ => ScriptedWrapper.Down(),   // 2 回目＝落ちた（覚えたまま）
            static _ => ScriptedWrapper.Ok());    // 3 回目＝受け取られた
        var voices = NewVoices(wrapper);

        await voices.PrecomputeAsync(["テスト話者"]);
        Assert.Single(wrapper.Calls);

        voices.ApplyPrecompute(new PrecomputeStatus { State = "done" });
        Assert.Equal(2, wrapper.Calls.Count);          // 出し直した（が落ちた）

        voices.ApplyPrecompute(new PrecomputeStatus { State = "done" });
        Assert.Equal(3, wrapper.Calls.Count);          // **忘れていない**＝もう 1 度出す
        Assert.Equal(["テスト話者"], wrapper.Calls[2]);

        voices.ApplyPrecompute(new PrecomputeStatus { State = "done" });
        Assert.Equal(3, wrapper.Calls.Count);          // 受け取られたので落とす
    }

    [Fact]
    public async Task 何度も落ちたら諦めて理由を残す()
    {
        var wrapper = new ScriptedWrapper(
            static call => call == 1 ? ScriptedWrapper.Busy() : ScriptedWrapper.Down());
        var voices = NewVoices(wrapper);

        await voices.PrecomputeAsync(["テスト話者"]);

        for (var i = 0; i < VoicesViewModel.MaxAutoRetries + 2; i++)
        {
            voices.ApplyPrecompute(new PrecomputeStatus { State = "done" });
        }

        // 1（最初の注文）＋ 自動の出し直し 3 回で打ち止め（標本ごとに叩き続けない）
        Assert.Equal(1 + VoicesViewModel.MaxAutoRetries, wrapper.Calls.Count);
        Assert.Contains("押してください", voices.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 覚えている間も走行中の標本では出し直さない()
    {
        var wrapper = new ScriptedWrapper(static _ => ScriptedWrapper.Busy());
        var voices = NewVoices(wrapper);

        await voices.PrecomputeAsync(["テスト話者"]);
        voices.ApplyPrecompute(new PrecomputeStatus { State = "running" });
        voices.ApplyPrecompute(null);

        Assert.Single(wrapper.Calls);
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// 束ねる側（<see cref="MainViewModel"/>）の釘＝low 6 の ⑷（状態機械を通す）・
/// 裁定 91（起動時の突合と「実行系を組み直す」1 手）・裁定 90（試し撃ちの 200 で cache を消す）・
/// 設計書 §20-5 ⑴（列挙が落ちても理由 1 行が出る）。
/// </summary>
[Collection(AppServicesCollection.Name)]
public sealed class RoundThreeMainViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-d3-main-" + Guid.NewGuid().ToString("N")[..8]);

    // ---- 偽物（実機に触れない継ぎ目） ---------------------------------------

    /// <summary>事前検査の断りを覚えるだけの偽の子プロセス（起こさない）。</summary>
    private sealed class RecordingServer : IServerProcess
    {
        public string? Preflight { get; private set; }

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
            Preflight = reason;
            State = ServerState.Failed;
            FailureReason = reason;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>撃つと落ちる列挙（<c>Process.Start</c> の <c>Win32Exception</c> と同じ形）。</summary>
    private sealed class ThrowingEnumerator : IGpuEnumerator
    {
        public Task<GpuEnumerationResult> EnumerateAsync(
            GpuEnumerationRequest request, CancellationToken cancellationToken) =>
            throw new Win32Exception(193, "is not a valid application for this OS platform.");
    }

    private sealed class FixedEnumerator(params GpuInfo[] gpus) : IGpuEnumerator
    {
        public Task<GpuEnumerationResult> EnumerateAsync(
            GpuEnumerationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new GpuEnumerationResult(
                gpus, GpuSource.NvidiaSmi, TimeSpan.FromSeconds(1), null));
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

    /// <summary>200 と音を返すだけの偽 wrapper（合成の口しか使わない）。</summary>
    private sealed class ShootingWrapper : IWrapperClient
    {
        public Uri BaseAddress { get; } = new("http://127.0.0.1:18099/");

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

        public Task<SpeechResult> SynthesizeAsync(
            SpeechRequest request, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(new SpeechResult(
                true, new byte[64], "audio/wav", 1, 200, null, TimeSpan.FromMilliseconds(10)));

        public Task<WrapperResult<VoicesResponse>> GetVoicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new WrapperResult<VoicesResponse>(
                false, null, 0, null, true, TimeSpan.Zero, "使わない"));

        public Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<StatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<ParamsResponse>> GetParamsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<VoicesResponse>> GetOpenAiVoicesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<WarmupStartResult>> StartWarmupAsync(
            WarmupRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<PrecomputeStartResult>> StartPrecomputeAsync(
            PrecomputeRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new WrapperResult<PrecomputeStartResult>(
                false, null, 404, null, false, TimeSpan.Zero, "使わない"));

        public Task<WrapperResult<CancelResult>> CancelWarmupAsync(
            string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<CancelResult>> CancelPrecomputeAsync(
            string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<DropLatentResult>> DropLatentAsync(
            string voiceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    // ---- 雛形 ---------------------------------------------------------------

    private const string RuntimeLedger = """
        {"schema":1,"items":[
          {"kind":"wheel","name":"torch","url":"https://example.invalid/torch-1.whl",
           "sha256":"bb","size":1000}]}
        """;

    private AppPaths MakePaths()
    {
        var appDir = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(appDir, "ledger"));
        Directory.CreateDirectory(Path.Combine(_root, "data", "cache"));
        var paths = new AppPaths(
            Path.Combine(_root, "install"),
            appDir,
            Path.Combine(_root, "runtime"),
            Path.Combine(_root, "data"),
            developerMode: true);

        File.WriteAllText(
            paths.LedgerPath("python-embed"),
            """
            {"schema":1,"items":[
              {"kind":"python-embed","name":"python-embed",
               "url":"https://example.invalid/python-embed.zip","sha256":"aa","size":100}]}
            """,
            new UTF8Encoding(false));
        File.WriteAllText(paths.LedgerPath("runtime-cpu"), RuntimeLedger, new UTF8Encoding(false));
        return paths;
    }

    private static void MakeRuntime(AppPaths paths, string variant)
    {
        var dir = Path.Combine(paths.RuntimeRoot, variant);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, AppPaths.PythonExeName), []);

        // **展開が済んだ樹の形**（是正・便 D（3）の 3 巡目）＝site-packages の *.dist-info が
        // 台帳の wheel/sdist/archive の件数と合う。雛形の台帳は wheel 1 件なので 1 つ作る。
        Directory.CreateDirectory(Path.Combine(dir, "site-packages", "torch-1.dist-info"));
    }

    private MainViewModel NewMain(AppPaths paths, LauncherSettings settings) =>
        new(paths, settings, new JsonSettingsStore(paths.SettingsPath), new SilentPlayer());

    // ---- low 6 の ⑷＝状態機械を通さず Failed を書かない -----------------------

    [Fact]
    public async Task 実行系が無い断りは状態機械を通す()
    {
        // 直に Status.ApplyState(Failed, …) と書いていたころは IServerProcess.State が
        // Stopped のままで、窓を開き直すと理由が消え、「サーバ起動」もまた押せた。
        var paths = MakePaths();                       // python.exe は作らない
        var server = new RecordingServer();
        AppServices.Server = server;
        AppServices.GpuEnumerator = null;

        var main = NewMain(paths, new LauncherSettings { Variant = RuntimeVariants.Cpu, Port = 18099 });

        Assert.False(await main.StartServerAsync());

        Assert.NotNull(server.Preflight);
        Assert.Contains("実行系がまだありません", server.Preflight!, StringComparison.Ordinal);
        Assert.Equal(ServerState.Failed, server.State);          // 機械が知っている
        Assert.Equal(ServerState.Failed, main.Status.State);     // 画面も同じ
        Assert.Equal(server.FailureReason, main.Status.Reason);
    }

    [Fact]
    public async Task 保存したGPUが居ない断りも状態機械を通す()
    {
        var paths = MakePaths();
        MakeRuntime(paths, RuntimeVariants.Cu130);
        var server = new RecordingServer();
        AppServices.Server = server;
        AppServices.GpuEnumerator = new FixedEnumerator(new GpuInfo(
            "uuid-here", "NVIDIA GeForce RTX 3090", 0, 25L * 1024 * 1024 * 1024,
            "1", null, "580.00", GpuSource.NvidiaSmi));

        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cu130,
            Port = 18099,
            GpuUuid = "uuid-gone",
            GpuName = "前回の GPU",
        };

        Assert.False(await NewMain(paths, settings).StartServerAsync());

        Assert.NotNull(server.Preflight);
        Assert.Equal(ServerState.Failed, server.State);
    }

    // ---- 設計書 §20-5 ⑴＝列挙が落ちても黙らない ------------------------------

    [Fact]
    public async Task GPU列挙が落ちても起動の手は理由をログに残す()
    {
        // 実射＝起こせない python.exe を指すと窓が 60 秒何も出さなかった（模型は 8 ms で Failed）。
        var paths = MakePaths();
        MakeRuntime(paths, RuntimeVariants.Cu130);
        AppServices.Server = new RecordingServer();
        AppServices.GpuEnumerator = new ThrowingEnumerator();

        var main = NewMain(paths, new LauncherSettings { Variant = RuntimeVariants.Cu130, Port = 18099 });

        // 投げずに結末で返る（トレイの投げ捨て起動でも消えない）
        Assert.False(await main.StartServerAsync());
        Assert.Contains("GPU の列挙が落ちました", main.Status.LogText, StringComparison.Ordinal);
        Assert.Contains("not a valid application", main.Status.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 起動の手が落ちても理由1行が出る()
    {
        // AsyncRelayCommand の受け口（全例外）→ MainViewModel が状態機械へ通す路。
        var paths = MakePaths();
        var server = new RecordingServer();
        AppServices.Server = server;
        AppServices.GpuEnumerator = null;

        var main = NewMain(paths, new LauncherSettings { Variant = RuntimeVariants.Cpu, Port = 18099 });
        await main.Status.StartCommand.ExecuteAsync();

        Assert.Equal(ServerState.Failed, main.Status.State);
        Assert.NotNull(main.Status.Reason);
    }

    // ---- 裁定 91＝起動時の突合と「実行系を組み直す」1 手 -----------------------

    [Fact]
    public void 台帳が食い違えば組み直す1手を出す()
    {
        var paths = MakePaths();
        MakeRuntime(paths, RuntimeVariants.Cpu);
        AppServices.Server = new RecordingServer();
        AppServices.GpuEnumerator = null;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        settings.SetRuntimeStamp(
            RuntimeVariants.Cpu, "0123456789abcdef", AppVersion.Display);   // 別の台帳で展開した樹

        var main = NewMain(paths, settings);

        Assert.True(main.Status.CanRebuildRuntime);
        Assert.Contains("組み直して", main.Status.RebuildRuntimeText!, StringComparison.Ordinal);
        Assert.True(main.Status.RebuildRuntimeCommand.CanExecute(null));

        // 焼き印を今の台帳に直せば 1 手は消える
        RuntimeStamp.Burn(settings, paths, RuntimeVariants.Cpu);
        main.CheckRuntimeStamp();
        Assert.False(main.Status.CanRebuildRuntime);
        Assert.False(main.Status.RebuildRuntimeCommand.CanExecute(null));
    }

    [Fact]
    public void 焼き印の無い樹は起動時にいまの台帳で焼き直して黙る()
    {
        // 便 D（2）以前に組んだ樹・台本（assemble-runtime.ps1）で組んだ樹には焼き印が無い。
        // そこで「判らないから組み直せ」と急かすと、正しく組んである機体まで 4 GB の展開を
        // やり直させる。いま在る物をその台帳の産物として受け入れ、**次に台帳が動いた日から**
        // 検知できる状態にするのがこの欄の値打ちである。
        var paths = MakePaths();
        MakeRuntime(paths, RuntimeVariants.Cpu);
        AppServices.Server = new RecordingServer();
        AppServices.GpuEnumerator = null;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        Assert.Null(settings.RuntimeLedgerFor(RuntimeVariants.Cpu));

        var main = NewMain(paths, settings);

        Assert.Equal(
            RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu),
            settings.RuntimeLedgerFor(RuntimeVariants.Cpu));
        Assert.Equal(AppVersion.Display, settings.InstalledAppVersionFor(RuntimeVariants.Cpu));
        Assert.Null(main.Status.RebuildRuntimeText);          // 受け入れた＝黙る

        // 檔にも落ちている（無人検分はここを読む）
        var written = new JsonSettingsStore(paths.SettingsPath).Load();
        Assert.Matches(
            "^[0-9a-f]{64}$", written.RuntimeLedgerFor(RuntimeVariants.Cpu) ?? string.Empty);
        Assert.False(
            string.IsNullOrWhiteSpace(written.InstalledAppVersionFor(RuntimeVariants.Cpu)));
    }

    [Fact]
    public void 実行系がまだ無い樹には焼き印を押さない()
    {
        // 初回取得の前に焼くと、展開していない台帳の sha256 が「展開に使った台帳」になる。
        var paths = MakePaths();                       // python.exe は作らない
        AppServices.Server = new RecordingServer();
        AppServices.GpuEnumerator = null;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var main = NewMain(paths, settings);

        Assert.Null(settings.RuntimeLedgerFor(RuntimeVariants.Cpu));
        Assert.Null(main.Status.RebuildRuntimeText);
    }

    [Fact]
    public void 焼き印が合っていれば1手を出さない()
    {
        var paths = MakePaths();
        MakeRuntime(paths, RuntimeVariants.Cpu);
        AppServices.Server = new RecordingServer();
        AppServices.GpuEnumerator = null;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        RuntimeStamp.Burn(settings, paths, RuntimeVariants.Cpu);

        Assert.Null(NewMain(paths, settings).Status.RebuildRuntimeText);
    }

    // ---- 裁定 90 Q-E2 ⑶＝試し撃ちの 200 で cache を消す ------------------------

    [Fact]
    public async Task 試し撃ちで200が返ると取得キャッシュを消す()
    {
        var paths = MakePaths();
        MakeRuntime(paths, RuntimeVariants.Cpu);
        AppServices.Server = new RecordingServer();
        AppServices.GpuEnumerator = null;
        AppServices.Wrapper = new ShootingWrapper();

        var cache = paths.DownloadCacheDir;
        File.WriteAllBytes(Path.Combine(cache, "torch-1.whl"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(cache, "python-embed.zip"), new byte[100]);

        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cpu,
            FirstRunCompleted = true,
        };
        RuntimeStamp.Burn(settings, paths, RuntimeVariants.Cpu);

        var main = NewMain(paths, settings);
        main.Try.Input = "テストです。";
        await main.Try.SynthesizeAsync();

        Assert.False(File.Exists(Path.Combine(cache, "torch-1.whl")));
        Assert.False(File.Exists(Path.Combine(cache, "python-embed.zip")));

        // 消したバイトはログに残る（ウィザードが開いていれば Trail にも）
        Assert.Contains("取得キャッシュを消しました", main.Status.LogText, StringComparison.Ordinal);
        Assert.Contains(FetchPlanner.FormatBytes(1100), main.Status.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 初回取得を通していなければ試し撃ちでは消さない()
    {
        var paths = MakePaths();
        MakeRuntime(paths, RuntimeVariants.Cpu);
        AppServices.Server = new RecordingServer();
        AppServices.GpuEnumerator = null;
        AppServices.Wrapper = new ShootingWrapper();

        var cache = paths.DownloadCacheDir;
        File.WriteAllBytes(Path.Combine(cache, "torch-1.whl"), new byte[1000]);

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu, FirstRunCompleted = false };
        RuntimeStamp.Burn(settings, paths, RuntimeVariants.Cpu);

        var main = NewMain(paths, settings);
        main.Try.Input = "テストです。";
        await main.Try.SynthesizeAsync();

        Assert.True(File.Exists(Path.Combine(cache, "torch-1.whl")));
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// 裁定 91 の 1 手＝「実行系を組み直す」（cache から再展開・cache が無ければ取得から）。
/// </summary>
[Collection(AppServicesCollection.Name)]
public sealed class RoundThreeRebuildTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-d3-rb-" + Guid.NewGuid().ToString("N")[..8]);

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

    private sealed class CountingDownloader : IDownloader
    {
        public List<string> Wanted { get; } = [];

        public Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(
            IReadOnlyList<DownloadRequest> requests,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            foreach (var request in requests)
            {
                Wanted.Add(Path.GetFileName(request.DestinationPath));
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

    private sealed class CountingInstaller : IRuntimeInstaller
    {
        public int Calls { get; private set; }

        public Task<InstallResult> InstallAsync(
            InstallRequest request, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new InstallResult(true, 101, 4096, 1, [], null, null));
        }
    }

    private AppPaths MakePaths()
    {
        var appDir = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(appDir, "ledger"));
        Directory.CreateDirectory(Path.Combine(_root, "data", "cache"));
        var paths = new AppPaths(
            Path.Combine(_root, "install"),
            appDir,
            Path.Combine(_root, "runtime"),
            Path.Combine(_root, "data"),
            developerMode: true);

        File.WriteAllText(
            paths.LedgerPath("python-embed"),
            """
            {"schema":1,"items":[
              {"kind":"python-embed","name":"python-embed",
               "url":"https://example.invalid/python-embed.zip","sha256":"aa","size":100}]}
            """,
            new UTF8Encoding(false));
        File.WriteAllText(
            paths.LedgerPath("runtime-cpu"),
            """
            {"schema":1,"items":[
              {"kind":"wheel","name":"torch","url":"https://example.invalid/torch-1.whl",
               "sha256":"bb","size":1000}]}
            """,
            new UTF8Encoding(false));

        var runtimeDir = Path.Combine(paths.RuntimeRoot, RuntimeVariants.Cpu);
        Directory.CreateDirectory(runtimeDir);
        File.WriteAllBytes(Path.Combine(runtimeDir, AppPaths.PythonExeName), []);

        // 展開が済んだ樹の形（*.dist-info が台帳の wheel 1 件と合う）＝焼き印を押してよい樹
        Directory.CreateDirectory(Path.Combine(runtimeDir, "site-packages", "torch-1.dist-info"));
        return paths;
    }

    private MainViewModel NewMain(AppPaths paths, LauncherSettings settings)
    {
        AppServices.GpuEnumerator = null;
        return new MainViewModel(
            paths, settings, new JsonSettingsStore(paths.SettingsPath), new SilentPlayer());
    }

    [Fact]
    public async Task 組み直しはcacheに原檔が揃っていれば取得へ出ない()
    {
        var paths = MakePaths();
        var cache = paths.DownloadCacheDir;
        File.WriteAllBytes(Path.Combine(cache, "torch-1.whl"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(cache, "python-embed.zip"), new byte[100]);

        var downloader = new CountingDownloader();
        var installer = new CountingInstaller();
        AppServices.Downloader = downloader;
        AppServices.RuntimeInstaller = installer;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        settings.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", null);   // 食い違い＝1 手

        var main = NewMain(paths, settings);
        Assert.True(main.Status.CanRebuildRuntime);

        await main.RebuildRuntimeAsync();

        Assert.Empty(downloader.Wanted);                // 外へ 1 バイトも出ない
        Assert.Equal(1, installer.Calls);
        Assert.Equal(
            RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu),
            settings.RuntimeLedgerFor(RuntimeVariants.Cpu));
        Assert.Equal(AppVersion.Display, settings.InstalledAppVersionFor(RuntimeVariants.Cpu));
        Assert.False(main.Status.CanRebuildRuntime);    // 1 行も 1 手も消える
        Assert.Contains("実行系を組み直しました", main.Status.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 組み直しはcacheに原檔が無ければ取得から()
    {
        var paths = MakePaths();
        var cache = paths.DownloadCacheDir;
        File.WriteAllBytes(Path.Combine(cache, "torch-1.whl"), new byte[7]);   // 長さが合わない

        var downloader = new CountingDownloader();
        var installer = new CountingInstaller();
        AppServices.Downloader = downloader;
        AppServices.RuntimeInstaller = installer;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        settings.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", null);

        await NewMain(paths, settings).RebuildRuntimeAsync();

        // 足りない 2 檔（長さ違いの torch と、居ない python-embed）だけを取り直す
        Assert.Equal(2, downloader.Wanted.Count);
        Assert.Contains("torch-1.whl", downloader.Wanted);
        Assert.Contains("python-embed.zip", downloader.Wanted);
        Assert.Equal(1, installer.Calls);
    }

    [Fact]
    public async Task 展開系が無ければ組み直さずに理由を残す()
    {
        var paths = MakePaths();
        AppServices.Downloader = new CountingDownloader();
        AppServices.RuntimeInstaller = null;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        settings.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", null);

        var main = NewMain(paths, settings);
        await main.RebuildRuntimeAsync();

        Assert.Equal(   // 焼き印は動かさない
            "0123456789abcdef", settings.RuntimeLedgerFor(RuntimeVariants.Cpu));
        Assert.True(main.Status.CanRebuildRuntime);
        Assert.Contains("組み直せません", main.Status.LogText, StringComparison.Ordinal);
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
