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
using IrodoriTtsYwk.Launcher.Services.Server;
using IrodoriTtsYwk.Launcher.ViewModels;
using IrodoriTtsYwk.Launcher.Views;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// <b>決裁 135＝RTX 機の段 H が拾った 3 つ</b>（v2.0.1）。
/// <list type="number">
/// <item><b>射 2</b>＝取得の直後の 1 回目は読み込みが数分に延びる（原因は決裁 136＝
/// Defender のオンアクセス初回スキャンが本命）。子は生きていて口も開いていたのに、
/// 契約 ⑵ の 120 秒で「止まりました。」になり
/// <c>firstRunCompleted</c> が偽のまま残った（〔もう一度〕では 36.62 秒・次は 12 秒）。
/// ⇒ <b>読み込んでいる最中だけ</b>硬い上限（600 s）まで伸ばす。</item>
/// <item><b>射 10</b>＝設定 › 詳細 で CUDA 12.6 に替えた機体で、次の起動に開いた
/// ウィザードが勧め（cu130）で<b>黙って上書き</b>した＝cu126 は 1 度も落ちなかった。
/// ⇒ 設定に残っている綴りは<b>明示の選択</b>として扱う。</item>
/// <item><b>射 10</b>＝動かし方の一覧に「cu130」「cu126」「cpu」の生の綴りが見えた。
/// ⇒ 一覧の行も選んでいる 1 行も<b>製品の語</b>（CUDA 13.0／CUDA 12.6／ROCm／CPU）で出す。</item>
/// </list>
/// <para>
/// <b>実 GPU・実ポート・外への取得には 1 つも触れない</b>（子は <c>cmd.exe</c> の偽物）。
/// </para>
/// </summary>
public sealed class Decision135ReadyWaitTests
{
    private const int Port = 18099;

    // ---- ⑴ 声を読み込んでいる間は、契約の期限で断らない ------------------------

    [Fact]
    public async Task 読み込んでいる最中は契約の期限を越えても待ち続ける()
    {
        // 段 H 射 2 そのもの＝口は開いている（/health 200）が runtime.loaded はまだ偽。
        // 契約の期限（ここでは 150 ms）を過ぎても、載り終えるまで待って**成功で返る**。
        await using var server = NewServer(new LoadingStatus(loadedAfterSamples: 20), LiveChild(20));

        var watch = Stopwatch.StartNew();
        var result = await server.StartAsync(
            Request(TimeSpan.FromMilliseconds(150)), CancellationToken.None);
        watch.Stop();

        Assert.True(result.Ok, result.FailureReason);
        Assert.Equal(ServerState.Ready, result.State);

        // 是正前はここで「起動が 0 秒で終わりませんでした。」に落ちていた。
        Assert.True(
            watch.Elapsed > TimeSpan.FromMilliseconds(300),
            "契約の期限（150 ms）を越えて待っていない＝伸ばしていない：" + watch.Elapsed);
    }

    [Fact]
    public async Task 伸ばしたことは記録に残る()
    {
        // 帯は「準備しています…」のままで 1 行も変わらない＝伸ばした事実は**記録**に落とす。
        await using var server = NewServer(new LoadingStatus(loadedAfterSamples: 20), LiveChild(20));
        var lines = new List<string>();
        server.LogLine += (_, e) => lines.Add(e.Event.Line);

        var result = await server.StartAsync(
            Request(TimeSpan.FromMilliseconds(150)), CancellationToken.None);

        Assert.True(result.Ok, result.FailureReason);
        Assert.Contains(
            lines,
            line => line.Contains("まだ読み込んでいるので", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 読み込んでいる最中に子が消えたら断る()
    {
        // 伸ばす条件の ⑴＝子が生きていること。死んだ子を 600 秒待たない。
        await using var server = NewServer(new LoadingStatus(), Cmd("exit /b 3"));

        var watch = Stopwatch.StartNew();
        var result = await server.StartAsync(Request(TimeSpan.FromSeconds(30)), CancellationToken.None);
        watch.Stop();

        Assert.False(result.Ok);
        Assert.Equal(ServerState.Failed, result.State);
        Assert.Equal(3, result.ExitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), watch.Elapsed.ToString());
    }

    [Fact]
    public async Task 読込の理由が載った標本では伸ばさない()
    {
        // 伸ばす条件の ⑶＝runtime.error が無いこと（裁定 105 ⑷ の路は今までどおり即断）。
        await using var server = NewServer(
            new LoadFailedStatus("FileNotFoundError: Checkpoint not found"),
            LiveChild(20),
            hardCap: TimeSpan.FromSeconds(30));

        var watch = Stopwatch.StartNew();
        var result = await server.StartAsync(
            Request(TimeSpan.FromMilliseconds(200)), CancellationToken.None);
        watch.Stop();

        Assert.False(result.Ok);
        Assert.Contains("モデルの読込に失敗＝", result.FailureReason!, StringComparison.Ordinal);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
    }

    [Fact]
    public async Task 硬い上限まで伸ばしても終わらなければ3部品で断る()
    {
        await using var server = NewServer(
            new LoadingStatus(), LiveChild(20), hardCap: TimeSpan.FromSeconds(1));

        var result = await server.StartAsync(
            Request(TimeSpan.FromMilliseconds(200)), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(ServerState.Failed, result.State);

        // 数字は**伸ばした後の上限**を名乗る（200 ms ではなく 1 秒）。
        Assert.Contains("起動が 1 秒で終わりませんでした。", result.FailureReason!, StringComparison.Ordinal);

        // 帯は 3 部品に言い直す（⑴ 何が起きたか ⑵ なぜか ⑶ 次にやること）。
        var band = BandText.For(ServerState.Failed, result.FailureReason);
        Assert.Equal(BandSeverity.Bad, band.Severity);
        Assert.Equal(BandText.Failed, band.Headline);
        Assert.Equal("準備に時間がかかりすぎました。 待っても声の読み込みが終わりませんでした。", band.Reason);
        Assert.Equal("もう一度動かす", band.ActionLabel);
    }

    [Fact]
    public async Task 口が開かない機体は契約の期限のまま断る()
    {
        // **ふだんの起動の期待（120 s でポートが開く）は 1 秒も伸ばさない**＝
        // 伸ばすのは「口が開いていて、まだ載っていない」回だけである。
        await using var server = NewServer(
            new NeverReachable(), LiveChild(20), hardCap: TimeSpan.FromSeconds(30));

        var watch = Stopwatch.StartNew();
        var result = await server.StartAsync(
            Request(TimeSpan.FromMilliseconds(400)), CancellationToken.None);
        watch.Stop();

        Assert.False(result.Ok);
        Assert.Contains(" 秒で終わりませんでした。", result.FailureReason!, StringComparison.Ordinal);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
    }

    [Fact]
    public void 伸ばす条件は3つ揃ったときだけ()
    {
        // 純関数の真理表（子の生死は呼ぶ側が見る＝ここは標本だけを見る）。
        Assert.True(ServerProcess.IsLoadingInProgress(
            new ReadinessSample(true, false, false, null, null)));

        // 口が開いていない
        Assert.False(ServerProcess.IsLoadingInProgress(
            new ReadinessSample(false, false, false, null, "誰も居ません。")));

        // もう載っている（＝待つ理由が無い）
        Assert.False(ServerProcess.IsLoadingInProgress(
            new ReadinessSample(true, true, false, null, null)));

        // 理由が載った＝伸ばしても変わらない
        Assert.False(ServerProcess.IsLoadingInProgress(
            new ReadinessSample(true, false, false, null, null) { RuntimeError = "boom" }));

        Assert.False(ServerProcess.IsLoadingInProgress(null));
    }

    [Fact]
    public async Task 期限に立った1標本の空振りで緩和ごと捨てない()
    {
        // 是正・検分＝伸ばすかどうかを**その回の 1 標本だけ**で決めていた。
        // /ywk/status の 1 標本は WrapperClient.DefaultTimeout（5 秒）で切れ、切れた標本は
        // 「届かない」で返る（HealthPoller）。決裁 136 が名指しする状態（Defender が
        // 3.3 GB を初回スキャンしている最中に torch が 3.06 GB を mmap する）は、
        // まさにその 1 標本が遅れる状態である＝期限の回にたまたま当たった空振り 1 つで
        // 緩和を捨てると、直したはずの段 H 射 2 がそのまま再現する。
        await using var server = NewServer(
            new OneMissedSample(
                missAt: 2, stall: TimeSpan.FromMilliseconds(400), loadedAfterSamples: 4),
            LiveChild(20));

        var result = await server.StartAsync(
            Request(TimeSpan.FromMilliseconds(200)), CancellationToken.None);

        Assert.True(result.Ok, result.FailureReason);
        Assert.Equal(ServerState.Ready, result.State);
    }

    [Fact]
    public void 伸ばしてよいかは1標本ではなく履歴で決める()
    {
        var loading = new ReadinessSample(true, false, false, null, null);
        var miss = new ReadinessSample(false, false, false, null, "誰も居ません。");

        // いま「読み込み中」と言っている標本は、それだけで足りる。
        Assert.True(ServerProcess.CanExtendForLoading(loading, loadingSeen: false, unreachableStreak: 0));

        // 一度でも読み込み中を見ていれば、届かない標本が 3 回続くまでは伸ばせる＝
        // 見張りが Ready を降ろす基準（UnreachableSamplesToDowngrade）と同じ数である。
        Assert.Equal(3, ServerStateMachine.UnreachableSamplesToDowngrade);
        Assert.True(ServerProcess.CanExtendForLoading(miss, loadingSeen: true, unreachableStreak: 1));
        Assert.True(ServerProcess.CanExtendForLoading(miss, loadingSeen: true, unreachableStreak: 2));
        Assert.False(ServerProcess.CanExtendForLoading(miss, loadingSeen: true, unreachableStreak: 3));

        // **1 度も口が開いていない機体は伸ばさない**（契約 ⑵ の「ポートが開く」期待は据え置き）。
        Assert.False(ServerProcess.CanExtendForLoading(miss, loadingSeen: false, unreachableStreak: 1));
        Assert.False(ServerProcess.CanExtendForLoading(null, loadingSeen: false, unreachableStreak: 0));
    }

    [Fact]
    public async Task 利用者の期限が硬い上限以上なら2段目に入らない()
    {
        // 是正・検分＝`readyTimeoutSeconds` に 600 以上を書いた機体では hardCap == limit＝
        // **伸びしろが無い**。そこで 2 段目に入ると「900 秒では終わりませんでした。まだ
        // 読み込んでいるので 900 秒まで待ちます。」という自家撞着の 1 行を記録に残したうえで、
        // 同じ数字で断ることになる。入らないのが正しい。
        await using var server = NewServer(
            new LoadingStatus(), LiveChild(20), hardCap: TimeSpan.FromSeconds(1));
        var lines = new List<string>();
        server.LogLine += (_, e) => lines.Add(e.Event.Line);

        // 契約の期限も硬い上限も 1 秒＝同じ値である。
        var result = await server.StartAsync(
            Request(TimeSpan.FromSeconds(1)), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("起動が 1 秒で終わりませんでした。", result.FailureReason!, StringComparison.Ordinal);
        Assert.DoesNotContain(
            lines, line => line.Contains("まだ読み込んでいるので", StringComparison.Ordinal));
    }

    [Fact]
    public void 硬い上限の既定は600秒で利用者の設定が長ければそちらが勝つ()
    {
        Assert.Equal(TimeSpan.FromSeconds(600), ServerProcess.LoadingHardCap);

        // settings.readyTimeoutSeconds は今までどおり効く（0 以下＝変種の既定）。
        var cuda = new LauncherSettings { Variant = RuntimeVariants.Cu130 };
        Assert.Equal(TimeSpan.FromSeconds(120), cuda.EffectiveReadyTimeout());
        cuda.ReadyTimeoutSeconds = 900;
        Assert.Equal(TimeSpan.FromSeconds(900), cuda.EffectiveReadyTimeout());
        Assert.True(cuda.EffectiveReadyTimeout() > ServerProcess.LoadingHardCap);
    }

    [Fact]
    public void 伸ばしたことを告げる記録の1行()
    {
        Assert.Equal(
            "モデルの読み込みが 120 秒では終わりませんでした。まだ読み込んでいるので 600 秒まで待ちます。",
            ServerProcess.LoadingStillRunningLine(
                TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(600)));
    }

    [Fact]
    public void 帯は読み込んでいる間も準備しているという名乗りのままである()
    {
        // 伸ばしている間の状態は Listening（口は開いた・まだ載っていない）＝
        // 帯の語は 3 つのまま（準備しています…）で、赤にも 1 手にもならない。
        var band = BandText.For(ServerState.Listening, null, new BandContext(RuntimeLoaded: false));
        Assert.Equal(BandSeverity.Neutral, band.Severity);
        Assert.Equal(BandText.PreparingVoices, band.Headline);
        Assert.Null(band.ActionLabel);
        Assert.StartsWith(BandText.Preparing, band.Headline, StringComparison.Ordinal);
    }

    // ---- 継ぎ目 ---------------------------------------------------------------

    private static ServerProcess NewServer(
        IReadinessProbe readiness, ProcessStartInfo child, TimeSpan? hardCap = null) =>
        new(
            new FreePort(),
            readiness,
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(100),
            null,
            _ => child,
            null,
            hardCap ?? TimeSpan.FromSeconds(10));

    private static ServerStartRequest Request(TimeSpan readyTimeout) =>
        new(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Path.GetTempPath(),
            "127.0.0.1",
            Port,
            new Dictionary<string, string>(StringComparer.Ordinal),
            readyTimeout)
        {
            Variant = RuntimeVariants.Cpu,
        };

    private static ProcessStartInfo LiveChild(int seconds) => Cmd(
        "1>&2 echo ywk_server 2.0.1 upstream=8224daf/841fb7c & ping -n "
        + (seconds + 1).ToString(CultureInfo.InvariantCulture) + " 127.0.0.1 >nul");

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

    /// <summary>口は開いているが、まだ載っていない（＝読み込み中）個体。</summary>
    private sealed class LoadingStatus(int loadedAfterSamples = int.MaxValue) : IReadinessProbe
    {
        private int _samples;

        public Task<ReadinessSample> ProbeAsync(Uri baseAddress, CancellationToken cancellationToken)
        {
            var loaded = Interlocked.Increment(ref _samples) > loadedAfterSamples;
            var status = new StatusResponse
            {
                Engine = "irodori-ywk",
                Runtime = new StatusRuntime { Loaded = loaded, Loading = !loaded },
            };

            return Task.FromResult(new ReadinessSample(true, loaded, false, status, null));
        }
    }

    private sealed class LoadFailedStatus(string reason) : IReadinessProbe
    {
        public Task<ReadinessSample> ProbeAsync(Uri baseAddress, CancellationToken cancellationToken)
        {
            var status = new StatusResponse
            {
                Engine = "irodori-ywk",
                Runtime = new StatusRuntime { Loaded = false, Loading = false, Error = reason },
            };

            return Task.FromResult(
                new ReadinessSample(true, false, false, status, null) { RuntimeError = reason });
        }
    }

    private sealed class NeverReachable : IReadinessProbe
    {
        public Task<ReadinessSample> ProbeAsync(Uri baseAddress, CancellationToken cancellationToken) =>
            Task.FromResult(new ReadinessSample(false, false, false, null, "誰も居ません。"));
    }

    /// <summary>
    /// 読み込み中を返すが、<paramref name="missAt"/> 番目の標本だけ
    /// <paramref name="stall"/> だけ黙ってから「届かない」で返る個体
    /// （＝5 秒で切れた <c>/ywk/status</c> の作り物・是正・検分）。
    /// </summary>
    private sealed class OneMissedSample(int missAt, TimeSpan stall, int loadedAfterSamples)
        : IReadinessProbe
    {
        private int _samples;

        public async Task<ReadinessSample> ProbeAsync(
            Uri baseAddress, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _samples);
            if (n == missAt)
            {
                await Task.Delay(stall, cancellationToken).ConfigureAwait(false);
                return new ReadinessSample(false, false, false, null, "誰も居ません。");
            }

            var loaded = n > loadedAfterSamples;
            var status = new StatusResponse
            {
                Engine = "irodori-ywk",
                Runtime = new StatusRuntime { Loaded = loaded, Loading = !loaded },
            };

            return new ReadinessSample(true, loaded, false, status, null);
        }
    }
}

/// <summary>
/// 決裁 135 ⑴ の画面の側＝<b>待っている間の 1 行</b>（ウィザードの「起動の確認」）。
/// </summary>
public sealed class Decision135WizardLineTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-d135-" + Guid.NewGuid().ToString("N"));

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
    public async Task 起動の確認の段では声を読み込んでいると名乗る()
    {
        // 働く段の 1 行は段が名乗る（「動くか確かめています。」）。
        Assert.Equal("動くか確かめています。", FirstRunViewModel.PhaseLine(FirstRunStep.Start));

        FirstRunViewModel? wizard = null;
        var during = new List<string>();

        // 主窓は Listening（口は開いた・まだ載っていない）を見て呼ぶ＝
        // ここではその瞬間を「起こす手」の中で作る。
        var vm = NewWizard(
            new LauncherSettings { Variant = RuntimeVariants.Cpu },
            _ =>
            {
                during.Add(wizard!.PhaseText);
                wizard!.ReportLoadingVoices();
                during.Add(wizard!.PhaseText);
                return Task.FromResult(true);
            });
        wizard = vm;

        vm.Accepted = true;
        await vm.NextAsync();                                // お知らせ → これからすること
        await vm.NextAsync();                                // 取得→展開→モデル→起動→完了

        Assert.Equal(FirstRunStep.Done, vm.Step);
        Assert.Equal(
            ["動くか確かめています。", UiStrings.WizardLoadingVoices],
            during);
        Assert.Equal(
            "声を読み込んでいます。初めてのときは数分かかることがあります。",
            UiStrings.WizardLoadingVoices);
    }

    [Fact]
    public void 主窓は口が開いた回にその1行を呼ぶ配線を持っている()
    {
        // 是正・検分＝上の試験は ViewModel の側しか見ておらず、**配線**（MainViewModel が
        // Listening を見て呼ぶ 1 行）は誰も釘付けしていなかった＝その 1 行を消しても
        // 全部の試験が緑のまま、ウィザードは「動くか確かめています。」のまま数分固まる。
        // MainViewModel は AppServices の静的に絡んで組み立てられないので、
        // **原文**で釘付けする（WordLintTests・Decision135VariantComboTests と同じ作法）。
        var source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "ViewModelsSource", "MainViewModel.cs"),
            Encoding.UTF8);

        var at = source.IndexOf("if (state is ServerState.Listening)", StringComparison.Ordinal);
        Assert.True(at > 0, "ApplyServerState に Listening だけの枝が無い");

        var arm = source[at..Math.Min(source.Length, at + 200)];
        Assert.Contains("_firstRun?.ReportLoadingVoices();", arm, StringComparison.Ordinal);
    }

    [Fact]
    public void ほかの段では1行を差し替えない()
    {
        var vm = NewWizard(new LauncherSettings(), static _ => Task.FromResult(true));
        var before = vm.PhaseText;

        vm.ReportLoadingVoices();

        Assert.Equal(before, vm.PhaseText);
        Assert.NotEqual(UiStrings.WizardLoadingVoices, vm.PhaseText);
    }

    private FirstRunViewModel NewWizard(
        LauncherSettings settings, Func<CancellationToken, Task<bool>> startServer)
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
        File.WriteAllText(
            paths.LedgerPath(RuntimeVariants.LedgerName(RuntimeVariants.Cpu)),
            """
            {"schema":1,"name":"runtime","items":[
              {"kind":"wheel","name":"torch","url":"https://example.invalid/t.whl",
               "sha256":"bb","size":1000}]}
            """,
            new UTF8Encoding(false));

        var vm = new FirstRunViewModel(
            paths,
            settings,
            new JsonSettingsStore(paths.SettingsPath),
            new DriverRequirement(),
            static () => new HappyDownloader(),
            static () => new HappyInstaller(),
            startServer);

        vm.ModelFetcher = static (_, _) => Task.FromResult(true);
        return vm;
    }

    /// <summary>1 檔も落とさずに「落とせた」と答える偽の取得系（外へ 1 バイトも出さない）。</summary>
    private sealed class HappyDownloader : IDownloader
    {
        public Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(
            IReadOnlyList<DownloadRequest> requests,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<DownloadResult> results =
            [.. System.Linq.Enumerable.Select(requests, static r => new DownloadResult(
                    true, r.DestinationPath, r.ExpectedSize ?? 0, r.Sha256, r.Url,
                    false, true, 1, null))];
            return Task.FromResult(results);
        }

        public Task<DownloadResult> DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken) =>
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
        public Task<InstallResult> InstallAsync(
            InstallRequest request,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new InstallResult(true, 26523, 4_250_000_000, 101, [], null, null));
    }
}

/// <summary>
/// 決裁 135 ⑵＝<b>設定で選んだ動かし方をウィザードが黙って上書きしない</b>。
/// </summary>
public sealed class Decision135VariantChoiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-d135v-" + Guid.NewGuid().ToString("N"));

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
    public async Task 設定で選んだcu126は勧めで上書きされない()
    {
        // 段 H 射 10 の逐一＝設定 › 詳細 で CUDA 12.6 に替え、次の起動で（cu126 の一式が
        // まだ無いので）ウィザードが開いた。ドライバは 616.92＝cu130 も動く機体である。
        var paths = MakeTree();
        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cu126,
            FirstRunCompleted = true,
        };
        var vm = NewWizard(paths, settings, new DriverProbe("616.92", 1, Probed: true));

        Assert.True(vm.VariantFromSettings);
        await vm.RefreshDriverAsync();

        // 是正前はここが cu130 に戻り、1 行も「…CUDA 13.0 で動かします。」になっていた。
        Assert.Equal(RuntimeVariants.Cu126, vm.Variant);
        Assert.True(vm.VariantFromSettings);
        Assert.Equal("設定で選んだ CUDA 12.6 で動かします。", vm.DecisionLine);
        Assert.Equal(FirstRunViewModel.ChosenInSettingsLine(RuntimeVariants.Cu126), vm.DecisionLine);
        Assert.False(vm.VariantBlocked);

        // 落とすのも cu126 の一式である（＝保存も取得の計画も cu126）。
        vm.Accepted = true;
        await vm.NextAsync();                                  // お知らせ → これからすること
        Assert.Equal(FirstRunStep.Variant, vm.Step);
        await vm.NextAsync();                                  // これからすること → 取得

        Assert.Equal(RuntimeVariants.Cu126, settings.Variant);
        Assert.Equal("runtime-cu126", RuntimeVariants.LedgerName(vm.Variant));
        Assert.NotNull(FirstRunViewModel.TryPlan(paths, vm.Variant, skipVcRedist: true));
    }

    [Fact]
    public async Task 本当の初回はアプリが勧める()
    {
        // 配られたままの既定（cu130）でまだ 1 度も通していない＝誰も選んでいない。
        var paths = MakeTree();
        var settings = new LauncherSettings();
        Assert.Equal(LauncherSettings.DefaultVariant, settings.Variant);
        Assert.False(settings.FirstRunCompleted);

        var vm = NewWizard(paths, settings, new DriverProbe("537.58", 1, Probed: true));
        Assert.False(vm.VariantFromSettings);

        await vm.RefreshDriverAsync();

        Assert.Equal(RuntimeVariants.Cu126, vm.Variant);
        Assert.False(vm.VariantFromSettings);

        // 「このパソコンに合わせて、動かし方を選びました。」＝アプリが決めたと名乗る枝。
        Assert.StartsWith(FirstRunViewModel.DecisionLead, vm.DecisionLine, StringComparison.Ordinal);
        Assert.Contains("CUDA 12.6 で動かします", vm.DecisionLine, StringComparison.Ordinal);
        Assert.DoesNotContain(
            FirstRunViewModel.ChosenInSettingsLead, vm.DecisionLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 設定の選びが下限に届かなければ理由を添えて言い換える()
    {
        // v1.1.0（裁定 126 の B）の振る舞いは 1 行も変えない＝下限未満は勧めで言い換える。
        var paths = MakeTree();
        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cu130,
            FirstRunCompleted = true,
        };
        var vm = NewWizard(paths, settings, new DriverProbe("537.58", 1, Probed: true));
        Assert.True(vm.VariantFromSettings);

        await vm.RefreshDriverAsync();

        Assert.Equal(RuntimeVariants.Cu126, vm.Variant);
        Assert.False(vm.VariantFromSettings);
        Assert.Contains("CUDA 12.6 で動かします", vm.DecisionLine, StringComparison.Ordinal);
        Assert.Contains("580.00 未満のためです", vm.DecisionLine, StringComparison.Ordinal);
        Assert.False(vm.VariantBlocked);
    }

    [Fact]
    public async Task 畳みの中で選び直せば設定の名乗りは下ろす()
    {
        var paths = MakeTree();
        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cu126,
            FirstRunCompleted = true,
        };
        var vm = NewWizard(paths, settings, new DriverProbe("616.92", 1, Probed: true));
        await vm.RefreshDriverAsync();
        Assert.True(vm.VariantFromSettings);

        vm.Variant = RuntimeVariants.Cu130;                    // 利用者が自分で選び直した

        Assert.False(vm.VariantFromSettings);
        Assert.DoesNotContain(
            FirstRunViewModel.ChosenInSettingsLead, vm.DecisionLine, StringComparison.Ordinal);
    }

    [Fact]
    public void 明示の選びかどうかは純関数で決まる()
    {
        string[] choices = [RuntimeVariants.Cu130, RuntimeVariants.Cu126, RuntimeVariants.Cpu];

        // **利用者が選んだ印**が在れば明示である（是正・検分＝v2.0.1 からはこれが本筋）。
        Assert.True(FirstRunViewModel.IsExplicitChoice(
            choices,
            new LauncherSettings
            {
                Variant = RuntimeVariants.Cu126,
                FirstRunCompleted = true,
                VariantChosenByUser = true,
            }));

        // **アプリが焼いた値は明示ではない**＝一度も選んでいない利用者に「設定で選んだ」と言わない。
        Assert.False(FirstRunViewModel.IsExplicitChoice(
            choices,
            new LauncherSettings
            {
                Variant = RuntimeVariants.Cu130,
                FirstRunCompleted = true,
                VariantChosenByUser = false,
            }));

        // **〔はじめの準備をやり直す〕で開いた回は、印が在っても明示として扱わない**＝
        // 利用者はアプリに決め直させたくて押している。
        Assert.False(FirstRunViewModel.IsExplicitChoice(
            choices,
            new LauncherSettings
            {
                Variant = RuntimeVariants.Cu126,
                FirstRunCompleted = true,
                VariantChosenByUser = true,
            },
            openedForAcquisition: false));

        // ---- 印が無い古い檔（v2.0.0 まで）だけ、従来どおりの推測に落ちる ----

        // 本当の初回（配られたままの既定・まだ通していない）＝明示ではない
        Assert.Null(new LauncherSettings().VariantChosenByUser);
        Assert.False(FirstRunViewModel.IsExplicitChoice(choices, new LauncherSettings()));

        // 1 度通した機体は、既定のままでも「そのままでよい」と選んだことになる
        Assert.True(FirstRunViewModel.IsExplicitChoice(
            choices, new LauncherSettings { FirstRunCompleted = true }));

        // 既定と違う綴りは、まだ通していなくても明示である
        Assert.True(FirstRunViewModel.IsExplicitChoice(
            choices, new LauncherSettings { Variant = RuntimeVariants.Cu126 }));

        // この配布物が出せない綴り（別の版の設定を持ち込んだ機体）は採らない
        Assert.False(FirstRunViewModel.IsExplicitChoice(
            choices,
            new LauncherSettings { Variant = RuntimeVariants.RocmGfx1151, FirstRunCompleted = true }));
    }

    [Fact]
    public async Task やり直すで開いた回はアプリが決め直す()
    {
        // 是正・検分＝〔はじめの準備をやり直す〕（設定 › 詳細）は「もう一度この機体に合わせて
        // 決めてほしい」という 1 押しである。ドライバを 537.58 → 616.92 に上げた機体で
        // cu126 のまま動かし続けるのは、押した意図と逆である。
        var paths = MakeTree();
        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cu126,
            FirstRunCompleted = true,
            VariantChosenByUser = true,
        };
        var vm = NewWizard(
            paths, settings, new DriverProbe("616.92", 1, Probed: true), openedForAcquisition: false);

        Assert.False(vm.VariantFromSettings);
        await vm.RefreshDriverAsync();

        Assert.Equal(RuntimeVariants.Cu130, vm.Variant);
        Assert.False(vm.VariantFromSettings);
        Assert.StartsWith(FirstRunViewModel.DecisionLead, vm.DecisionLine, StringComparison.Ordinal);
        Assert.DoesNotContain(
            FirstRunViewModel.ChosenInSettingsLead, vm.DecisionLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task アプリが焼いた綴りを設定で選んだと名乗らない()
    {
        // 是正・検分＝前の回に**アプリ自身が**書いた値（VariantChosenByUser=false）である。
        // 取得のために開いた回でも「設定で選んだ …」とは言わない。
        var paths = MakeTree();
        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cu130,
            FirstRunCompleted = true,
            VariantChosenByUser = false,
        };
        var vm = NewWizard(paths, settings, new DriverProbe("616.92", 1, Probed: true));

        Assert.False(vm.VariantFromSettings);
        await vm.RefreshDriverAsync();

        Assert.Equal(RuntimeVariants.Cu130, vm.Variant);
        Assert.StartsWith(FirstRunViewModel.DecisionLead, vm.DecisionLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 検められなかった回は設定の名乗りより先に言う()
    {
        // 是正・検分＝nvidia-smi が答えない機体（素の機体・検分が落ちた回）で
        // 「設定で選んだ CUDA 13.0 で動かします。」とだけ言うと、⑴ グラフィックスを
        // 検められていない ⑵ うまく動かないときは 詳細 で替えられる、の 2 つが消える。
        var paths = MakeTree();
        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cu130,
            FirstRunCompleted = true,
            VariantChosenByUser = true,
        };
        var vm = NewWizard(
            paths,
            settings,
            new DriverProbe(null, 0, Probed: true, FailureReason: "nvidia-smi が見つかりません。"));

        await vm.RefreshDriverAsync();

        Assert.Equal(RuntimeVariants.Cu130, vm.Variant);
        Assert.True(vm.VariantFromSettings);
        Assert.DoesNotContain(
            FirstRunViewModel.ChosenInSettingsLead, vm.DecisionLine, StringComparison.Ordinal);
        Assert.Contains("確かめられませんでした", vm.DecisionLine, StringComparison.Ordinal);
        Assert.Equal(
            FirstRunViewModel.UnknownDecisionLine(RuntimeVariants.Cu130), vm.DecisionLine);
    }

    [Fact]
    public async Task 選んだのが誰かは檔に残る()
    {
        // 是正・検分＝この真偽（settings.json の variantChosenByUser）が本筋である。
        // ⑴ 畳みの中で利用者が選んだ回＝真が焼ける。
        var paths = MakeTree();
        var settings = new LauncherSettings();
        var vm = NewWizard(paths, settings, new DriverProbe("616.92", 1, Probed: true));
        await vm.RefreshDriverAsync();

        vm.Variant = RuntimeVariants.Cu126;
        vm.Accepted = true;
        await vm.NextAsync();                                  // お知らせ → これからすること
        await vm.NextAsync();                                  // これからすること → 取得

        Assert.Equal(RuntimeVariants.Cu126, settings.Variant);
        Assert.True(settings.VariantChosenByUser);

        // ⑵ アプリが勧めて決めた回＝偽が焼ける（次の回にこちらが勧め直せる）。
        var paths2 = MakeTree2();
        var settings2 = new LauncherSettings();
        var vm2 = NewWizard(paths2, settings2, new DriverProbe("537.58", 1, Probed: true));
        await vm2.RefreshDriverAsync();

        vm2.Accepted = true;
        await vm2.NextAsync();
        await vm2.NextAsync();

        Assert.Equal(RuntimeVariants.Cu126, settings2.Variant);
        Assert.False(settings2.VariantChosenByUser);
    }

    [Fact]
    public async Task 設定で選び直してからウィザードが開く道を通しで見る()
    {
        // 是正・検分＝段 H 射 10 の道を**設定頁から**通す（利用者の印が本物に届くか）。
        var paths = MakeTree();
        var live = new LauncherSettings
        {
            Variant = RuntimeVariants.Cu130,
            FirstRunCompleted = true,
            VariantChosenByUser = false,                       // 前の回はアプリが決めた
        };
        var settingsVm = new SettingsViewModel(
            live, new JsonSettingsStore(paths.SettingsPath), paths, null, new DriverRequirement())
        {
            Variant = RuntimeVariants.Cu126,                   // 利用者が 設定 › 詳細 で選び直す
        };
        settingsVm.Apply();

        Assert.Equal(RuntimeVariants.Cu126, live.Variant);
        Assert.True(live.VariantChosenByUser);

        // 次の起動＝cu126 の一式がまだ無いのでウィザードが開く（正しい）。
        var wizard = NewWizard(paths, live, new DriverProbe("616.92", 1, Probed: true));
        await wizard.RefreshDriverAsync();

        Assert.Equal(RuntimeVariants.Cu126, wizard.Variant);
        Assert.Equal("設定で選んだ CUDA 12.6 で動かします。", wizard.DecisionLine);
    }

    // ---- Radeon 版の配布樹（選択肢＝rocm-gfx1151／cpu）------------------------

    [Fact]
    public async Task Radeon版の初回はアプリが勧める()
    {
        // 是正・検分＝Radeon 版では配られたままの既定（cu130）が**そもそも一覧に無い**＝
        // これだけが本当の初回で IsExplicitChoice を偽に保っている。釘を打っておく。
        var paths = MakeRadeonTree();
        var settings = new LauncherSettings();
        var vm = NewWizard(paths, settings, DriverProbe.Unknown);

        Assert.Equal([RuntimeVariants.RocmGfx1151, RuntimeVariants.Cpu], vm.VariantChoices);
        Assert.DoesNotContain(LauncherSettings.DefaultVariant, vm.VariantChoices);
        Assert.False(vm.VariantFromSettings);

        await vm.RefreshDriverAsync();

        Assert.Equal(RuntimeVariants.RocmGfx1151, vm.Variant);
        Assert.False(vm.VariantFromSettings);
        Assert.StartsWith(FirstRunViewModel.DecisionLead, vm.DecisionLine, StringComparison.Ordinal);
        Assert.DoesNotContain(
            FirstRunViewModel.ChosenInSettingsLead, vm.DecisionLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Radeon版で選んだcpuは勧めで上書きされない()
    {
        // 是正前の穴は Radeon 版にも在った＝Recommend は rocm が一覧に在れば無条件に rocm を返す。
        var paths = MakeRadeonTree();
        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cpu,
            FirstRunCompleted = true,
            VariantChosenByUser = true,
        };
        var vm = NewWizard(paths, settings, DriverProbe.Unknown);

        Assert.True(vm.VariantFromSettings);
        await vm.RefreshDriverAsync();

        Assert.Equal(RuntimeVariants.Cpu, vm.Variant);
        Assert.Equal("設定で選んだ CPU で動かします。", vm.DecisionLine);

        vm.Accepted = true;
        await vm.NextAsync();
        await vm.NextAsync();

        Assert.Equal(RuntimeVariants.Cpu, settings.Variant);
        Assert.Equal("runtime-cpu", RuntimeVariants.LedgerName(vm.Variant));
    }

    [Fact]
    public async Task Radeon版は動かし方の既定が設定に敷かれる()
    {
        // 是正・検分＝`SettingsDefaults.ApplyVariant` はどこからも呼ばれておらず、
        // 裁定 65・設計書 §5 が約束した「Radeon は暖機が既定 ON」が 1 度も効いていなかった。
        var paths = MakeRadeonTree();
        var settings = new LauncherSettings();
        Assert.False(settings.WarmupOnStart);

        var vm = NewWizard(paths, settings, DriverProbe.Unknown);
        await vm.RefreshDriverAsync();

        vm.Accepted = true;
        await vm.NextAsync();
        await vm.NextAsync();

        Assert.Equal(RuntimeVariants.RocmGfx1151, settings.Variant);
        Assert.True(settings.WarmupOnStart);

        // 起動の env にもそのまま届く（＝話者切替の 1 秒級の罰を既定で避ける）。
        var env = ServerEnvironment.Build(settings, paths, gpuIndex: null);
        Assert.Equal("1", env[ServerEnvironment.WarmupOnStart]);
    }

    [Fact]
    public void 記録の1行に隠す語が無い()
    {
        // 旧＝「変種＝CUDA 12.6（ドライバ 528.33 以上） を選びました。」＝憲章 §6-1 の隠す語。
        var line = FirstRunViewModel.ChosenVariantLogLine(RuntimeVariants.Cu126);
        Assert.Equal("動かし方＝CUDA 12.6（cu126）に決まりました。", line);
        Assert.DoesNotContain("変種", line, StringComparison.Ordinal);

        var kept = FirstRunViewModel.ChosenVariantLogLine(
            RuntimeVariants.Cu126, chosenInSettings: true);
        Assert.Equal("動かし方＝CUDA 12.6（cu126）を設定のまま使います。", kept);
        Assert.DoesNotContain("変種", kept, StringComparison.Ordinal);

        // 内輪の id は記録に残してよい（報告のときに要る＝憲章 §6-2 の技術語）。
        Assert.Contains("cu126", line, StringComparison.Ordinal);
    }

    /// <summary>CUDA 版の配布樹（選択肢＝cu130／cu126／cpu）。</summary>
    private AppPaths MakeTree() => MakeTree("app", [RuntimeVariants.Cu130, RuntimeVariants.Cu126, RuntimeVariants.Cpu]);

    /// <summary>同・2 本目（1 つの試験で 2 つのウィザードを立てる回＝設定の檔を分ける）。</summary>
    private AppPaths MakeTree2() => MakeTree("app2", [RuntimeVariants.Cu130, RuntimeVariants.Cu126, RuntimeVariants.Cpu]);

    /// <summary>
    /// Radeon 版の配布樹（選択肢＝rocm-gfx1151／cpu・是正・検分）＝
    /// 配られたままの既定（cu130）が<b>一覧に無い</b>版である。
    /// </summary>
    private AppPaths MakeRadeonTree() =>
        MakeTree("app-radeon", [RuntimeVariants.RocmGfx1151, RuntimeVariants.Cpu]);

    private AppPaths MakeTree(string name, string[] variants)
    {
        var appDir = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(appDir, "ledger"));
        Directory.CreateDirectory(Path.Combine(appDir, "licenses"));
        var paths = new AppPaths(
            Path.Combine(_root, name, "install"),
            appDir,
            Path.Combine(_root, name, "runtime"),
            Path.Combine(_root, name, "data"),
            developerMode: true);

        Directory.CreateDirectory(Path.Combine(_root, name, "data"));
        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文", new UTF8Encoding(false));
        File.WriteAllText(
            paths.LedgerPath("python-embed"),
            """
            {"schema":1,"name":"python-embed","items":[
              {"kind":"python-embed","name":"python-embed","url":"https://example.invalid/p.zip",
               "sha256":"aa","size":100}]}
            """,
            new UTF8Encoding(false));

        foreach (var variant in variants)
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
        AppPaths paths,
        LauncherSettings settings,
        DriverProbe probe,
        bool openedForAcquisition = true)
    {
        var vm = new FirstRunViewModel(
            paths,
            settings,
            new JsonSettingsStore(paths.SettingsPath),
            new DriverRequirement(),
            static () => null,
            static () => null,
            static _ => Task.FromResult(true),
            openedForAcquisition);

        vm.DriverProbeAsync = _ => Task.FromResult(probe);
        return vm;
    }
}

/// <summary>
/// 決裁 135 ⑶＝<b>動かし方の一覧に内輪の綴りを出さない</b>（一覧の行も、選んでいる 1 行も）。
/// </summary>
public sealed class Decision135VariantComboTests
{
    /// <summary>画面を組まずに釘付けできる札の対応表（`v2-spec.md` §2-5 の 2 行目）。</summary>
    [Fact]
    public void 札は4語である()
    {
        Assert.Equal("CUDA 13.0", VariantDisplayConverter.Display(RuntimeVariants.Cu130));
        Assert.Equal("CUDA 12.6", VariantDisplayConverter.Display(RuntimeVariants.Cu126));
        Assert.Equal("ROCm", VariantDisplayConverter.Display(RuntimeVariants.RocmGfx1151));
        Assert.Equal("CPU", VariantDisplayConverter.Display(RuntimeVariants.Cpu));

        // 空・非文字列は空文字（一覧に生の綴りが漏れる路を作らない）。
        Assert.Equal(string.Empty, VariantDisplayConverter.Display(null));
        Assert.Equal(string.Empty, VariantDisplayConverter.Display("   "));
        Assert.Equal(string.Empty, VariantDisplayConverter.Display(42));

        // 束縛が通る道（IValueConverter）も同じ物を返す。
        var converter = new VariantDisplayConverter();
        Assert.Equal(
            "CUDA 12.6",
            converter.Convert(
                RuntimeVariants.Cu126, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void 値そのものは1字も変えない()
    {
        // 憲章 §6-3＝settings.json の variant も AutomationId も台帳の綴りのままである。
        Assert.Equal("cu126", RuntimeVariants.Cu126);
        Assert.Equal("runtime-cu126", RuntimeVariants.LedgerName(RuntimeVariants.Cu126));
        Assert.Equal("cuda", RuntimeVariants.ServerLabel(RuntimeVariants.Cu126));
    }

    [Theory]
    [InlineData("SettingsView.xaml", "SettingsVariantCombo")]
    [InlineData("FirstRunWizard.xaml", "FirstRunVariantCombo")]
    public void 両方のコンボが札の変換を通している(string file, string automationId)
    {
        var xaml = File.ReadAllText(Path.Combine(ViewsDirectory(), file));

        // 資源＝変換器と、それを噛ませた 1 行の型紙。
        Assert.Contains(
            "<v:VariantDisplayConverter x:Key=\"VariantDisplay\" />", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "<DataTemplate x:Key=\"VariantRow\">", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "Text=\"{Binding Converter={StaticResource VariantDisplay}}\"",
            xaml,
            StringComparison.Ordinal);

        // その型紙をコンボが引いている＝選んでいる 1 行（ContentPresenter）に効く。
        var combo = ComboElement(xaml, automationId);
        Assert.Contains(
            "ItemTemplate=\"{StaticResource VariantRow}\"", combo, StringComparison.Ordinal);

        // **畳んだ一覧の行と読み上げ機の名**にも効かせる（是正・v2.0.1＝段 H 射 10）。
        Assert.Contains(
            "ItemContainerStyle=\"{StaticResource VariantItem}\"", combo, StringComparison.Ordinal);
        Assert.Contains(
            "<Style x:Key=\"VariantItem\" TargetType=\"ComboBoxItem\">", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"ContentTemplate\" Value=\"{StaticResource VariantRow}\" />",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name", xaml, StringComparison.Ordinal);

        // 値の束縛は台帳の綴りのまま（DisplayMemberPath は付けない＝ItemTemplate と併用できない）。
        Assert.Contains(
            "SelectedItem=\"{Binding Variant, Mode=TwoWay}\"", combo, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayMemberPath", combo, StringComparison.Ordinal);
    }

    /// <summary>その AutomationId を持つ <c>&lt;ComboBox …/&gt;</c> の 1 要素を切り出す。</summary>
    private static string ComboElement(string xaml, string automationId)
    {
        var at = xaml.IndexOf(
            "AutomationProperties.AutomationId=\"" + automationId + "\"", StringComparison.Ordinal);
        Assert.True(at > 0, automationId + " が " + nameof(xaml) + " に無い");

        var open = xaml.LastIndexOf("<ComboBox", at, StringComparison.Ordinal);
        Assert.True(open >= 0, automationId + " は ComboBox ではない");

        var close = xaml.IndexOf("/>", at, StringComparison.Ordinal);
        Assert.True(close > open, automationId + " の要素が閉じていない");

        return xaml[open..(close + 2)];
    }

    private static string ViewsDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Views");
        Assert.True(Directory.Exists(dir), "Views が試験の出力に無い：" + dir);
        return dir;
    }
}
