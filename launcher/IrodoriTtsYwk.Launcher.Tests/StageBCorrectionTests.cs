using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Ledger;
using IrodoriTtsYwk.Launcher.Services.Server;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// <b>v2.0 段 B の是正</b>（検分 3 席の所見＝`v2-plan.md` 段 B の記帳）。
/// <para>
/// 釘付けするのは<b>画面に何が出るか</b>の 5 つ＝
/// ⑴ 借りた「いま何をしているか」の 1 行を必ず段の 1 行へ戻すこと
/// ⑵ お知らせが読めない回は<b>押さずに</b>理由が画面へ着くこと
/// ⑶ 記録（内輪の綴り）が畳みの外へ漏れないこと
/// ⑷ 画面に出す失敗の 1 行が憲章 §6-1 の隠す語を持たないこと
/// ⑸ 起動が通らない回は<b>起こす側の理由</b>を出すこと（1 つの文で全部を名乗らない）。
/// </para>
/// <para><b>実機・実 GPU・実ポート・外への取得には 1 つも触れない</b>（1 バイトも外へ出さない）。</para>
/// </summary>
public sealed class StageBCorrectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-b-fix-" + Guid.NewGuid().ToString("N")[..8]);

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

    /// <summary>通知文だけを置いた樹（<b>取得台帳は置かない</b>＝計画が組めない機体）。</summary>
    private AppPaths TreeWithoutLedgers()
    {
        var paths = Paths;
        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文", new UTF8Encoding(false));
        return paths;
    }

    /// <summary>通知文＋取得台帳 2 本（＝最後まで進める最小の樹）。</summary>
    private AppPaths TreeWithLedgers()
    {
        var paths = TreeWithoutLedgers();
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

    private static FirstRunViewModel NewWizard(
        AppPaths paths,
        LauncherSettings settings,
        IDownloader? downloader = null,
        IRuntimeInstaller? installer = null,
        Func<CancellationToken, Task<bool>>? startServer = null)
    {
        var vm = new FirstRunViewModel(
            paths,
            settings,
            new JsonSettingsStore(paths.SettingsPath),
            new DriverRequirement(),
            () => downloader,
            () => installer,
            startServer ?? (static _ => Task.FromResult(true)));

        vm.ModelFetcher = (_, _) => Task.FromResult(true);
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
            [.. requests.Select(static r => new DownloadResult(
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

    // ---- ⑴ 借りた 1 行を返す -------------------------------------------------

    /// <summary>
    /// <b>vc_redist の段を抜けたら「いま何をしているか」は段の 1 行に戻る</b>（是正・検分）。
    /// <para>
    /// <b>何が起きていたか</b>＝<c>VcRedistRunner</c> が差さっていて <c>null</c> を返す機体
    /// （台帳が無い配布＝主窓は <c>ledger/vc_redist.json</c> が無ければ null を返す）では、
    /// 「Microsoft の部品を確かめています…」を書いたきり誰も戻さなかった。次に書くのは
    /// <c>OnDownloadProgress</c> だけで、<b>1 件も注文していない回はそれが 1 度も撃たれない</b>＝
    /// 続く W2／W3 の失敗が Microsoft の 1 行の脇に出て、台本の錨（必要な部品…）も外れる。
    /// </para>
    /// </summary>
    [Fact]
    public async Task vc_redistの段を抜けたら取得の1行に戻る()
    {
        var paths = TreeWithoutLedgers();          // 台帳が無い＝計画が組めない（W2）
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var vm = NewWizard(paths, settings, new HappyDownloader());

        // 台帳の無い配布＝手は差さっているが null を返す（MainViewModel.VcRedistRunner と同じ形）。
        vm.VcRedistRunner = (_, _, _) => Task.FromResult<VcRedistResult?>(null);

        vm.Accepted = true;
        await vm.NextAsync();                      // お知らせ → これからすること
        await vm.NextAsync();                      // これからすること → 取得（台帳が無いので失敗）

        Assert.Equal(FirstRunStep.Download, vm.Step);
        Assert.False(vm.LastStepOk);

        // 台本の 2 つの錨（`probe/d-launch-probe.ps1`）＝題＋段番号＋この 1 行。
        Assert.Equal(FirstRunViewModel.PhaseLine(FirstRunStep.Download), vm.PhaseText);
        Assert.Contains("必要な部品", vm.PhaseText, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft", vm.PhaseText, StringComparison.Ordinal);

        // 画面の 1 行は W2（⑴＋⑵）＝**檔名は出さない**。
        Assert.Contains("アプリのファイル", vm.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(".json", vm.Message, StringComparison.Ordinal);
    }

    // ---- ⑵ W1 は押さずに着く -------------------------------------------------

    /// <summary>
    /// <b>お知らせの全文が読めない回は、押す前に理由が画面へ出る</b>
    /// （`v2-copy.md` §2 段 1＝チェックも要約も出さない）。
    /// 1 巡目は差し替えの文を <c>FirstRunNoticesBox</c>（既定で閉じた畳みの中）にだけ置いたので、
    /// 画面には<b>押せないチェックと効かない〔次へ〕</b>しか無かった。
    /// </summary>
    [Fact]
    public void お知らせが読めない回は押さずに理由が画面へ出る()
    {
        var vm = NewWizard(Paths, new LauncherSettings());   // 通知文を置かない

        Assert.False(vm.CanAcceptNotices);
        Assert.True(vm.NoticesUnreadable);
        Assert.False(vm.NoticesReadable);                    // 要約もチェックも出さない
        Assert.Equal(FirstRunViewModel.NoticesMissingLine, vm.Message);
        Assert.Equal(FirstRunViewModel.NoticesMissingLine, vm.NoticesUnreadableText);

        // 錠（裁定 46）は 1 行も触っていない。
        vm.Accepted = true;
        Assert.False(vm.NextCommand.CanExecute(null));
    }

    // ---- ⑶ 記録は畳みの中と檔だけ --------------------------------------------

    /// <summary>
    /// <b>記録の内輪の 1 行は画面（<c>FirstRunMessageText</c>）に出ない</b>（憲章 §6-1）。
    /// <c>Record</c> が <c>Message</c> も書いていたころは、「変種＝…を選びました。」
    /// 「初回取得が終わりました。」が畳みの<b>外</b>に出ていた（＝隠すと決めた語がそのまま画面に）。
    /// <b>捨てはしない</b>＝<see cref="FirstRunViewModel.Trail"/> と檔には残る。
    /// </summary>
    [Fact]
    public async Task 記録の内輪の1行は画面に出ない()
    {
        var paths = TreeWithLedgers();
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var vm = NewWizard(paths, settings, new HappyDownloader(), new HappyInstaller());

        var log = new List<string>();
        vm.LogSink = log.Add;

        vm.Accepted = true;
        await vm.NextAsync();                                // お知らせ → これからすること
        await vm.NextAsync();                                // 取得→展開→モデル→起動→完了

        Assert.Equal(FirstRunStep.Done, vm.Step);

        // 記録は畳みの中（Trail）と檔に在る。
        // **綴りは「動かし方＝…（cpu）」**（決裁 135 ⑵・v2.0.1）＝記録の 1 行からも
        // 「変種」を落とした（Trail は畳みの中とはいえ画面に出る ListBox である）。
        // 内輪の id（cpu）は括弧に残す＝報告のときに要る。
        var chosen = FirstRunViewModel.ChosenVariantLogLine(RuntimeVariants.Cpu, chosenInSettings: true);
        Assert.Contains(vm.Trail, line => string.Equals(line, chosen, StringComparison.Ordinal));
        Assert.Contains(log, line => string.Equals(line, chosen, StringComparison.Ordinal));
        Assert.DoesNotContain(vm.Trail, line => line.Contains("変種＝", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("初回取得が終わりました。", StringComparison.Ordinal));

        // 画面には出ない（1 つでも漏れたら憲章 §6-1 の隠す語が利用者の目に入る）。
        foreach (var word in new[] { "変種", "初回取得", "実行系", "展開しました", "GiB", "便 D" })
        {
            Assert.DoesNotContain(word, vm.Message, StringComparison.Ordinal);
        }
    }

    // ---- ⑷ 画面に出す文の掃除 -------------------------------------------------

    /// <summary>
    /// <b>画面に出す失敗の 1 行は、隠す語も生の記録も持たない</b>
    /// （憲章 §6-1・`v2-copy.md` §3-2 の書き方の規則＝生の記録・番号つきの状態は画面に出さない）。
    /// </summary>
    [Fact]
    public void 画面に出す失敗の1行は隠す語を持たない()
    {
        string[] shown =
        [
            FirstRunViewModel.NoticesMissingLine,
            FirstRunViewModel.LedgerBrokenLine + "。" + FirstRunViewModel.LedgerBrokenWhyLine,
            FirstRunViewModel.FreeSpaceLine,
            FirstRunViewModel.DownloadFailedLine + FirstRunViewModel.DownloadFailedWhyLine
                + FirstRunViewModel.ResumeHintLine,
            FirstRunViewModel.InstallFailedLine + FirstRunViewModel.InstallFailedWhyLine,
            FirstRunViewModel.ModelsFailedLine,
            FirstRunViewModel.VcRedistFailedLine,
            FirstRunViewModel.CancelledLine,
            FirstRunViewModel.NothingToCancelLine,
            FirstRunViewModel.UnexpectedFailureLine,
            FirstRunViewModel.StartFailedLine,
            FirstRunViewModel.NotYetLine,
            FirstRunViewModel.VariantBlockedLine(RuntimeVariants.Cu130, "537.58"),
        ];

        foreach (var line in shown)
        {
            foreach (var word in new[] { "変種", "台帳", "展開", "配布物", "配布樹", "sha256", "GiB", "MiB", "便 " })
            {
                Assert.DoesNotContain(word, line, StringComparison.Ordinal);
            }
        }

        // W3 の数字は憲章 §4 の 1 枚表の綴り（約 12 GB）だけ＝実数の 3 つ組は詳細と檔の側に残る。
        Assert.Contains("約 12 GB", FirstRunViewModel.FreeSpaceLine, StringComparison.Ordinal);
        Assert.Contains(
            "GiB",
            FirstRunViewModel.FreeSpaceShortfall(12L << 30, 1L << 30)!,
            StringComparison.Ordinal);
    }

    // ---- ⑸ 起動が通らない回 ---------------------------------------------------

    /// <summary>
    /// <b>起こせなかった理由は、起こす側の 1 行を出す</b>（W7＝`v2-spec.md` §3・§2-1a）。
    /// 口が埋まっている・ドライバが下限未満・子が落ちた回まで「時間がかかりすぎました」と
    /// 名乗ると、⑶〔もう一度〕は同じ所で永久に落ちる。渡されなかった回だけ E-12 を出す。
    /// </summary>
    [Fact]
    public async Task 起動が通らない回は起こす側の理由を出す()
    {
        // 主窓が渡すのは状態帯が組んだ ⑴＋⑵ そのもの（`StatusViewModel.BandStateText`＋
        // `BandReasonText`）＝ここでは同じ純関数に同じ内輪の 1 行（口が埋まっている）を通す。
        var band = BandText.For(ServerState.Failed, ServerBindFailure.Message(18088));
        var shown = band.Headline + " " + band.Reason;

        var paths = TreeWithLedgers();
        var vm = NewWizard(
            paths,
            new LauncherSettings { Variant = RuntimeVariants.Cpu },
            new HappyDownloader(),
            new HappyInstaller(),
            static _ => Task.FromResult(false));
        vm.StartFailureLine = () => shown;

        vm.Accepted = true;
        await vm.NextAsync();
        await vm.NextAsync();

        Assert.Equal(FirstRunStep.Start, vm.Step);
        Assert.False(vm.LastStepOk);
        Assert.Equal("もう一度", vm.NextButtonText);
        Assert.Equal(shown, vm.Message);
        Assert.NotEqual(FirstRunViewModel.StartFailedLine, vm.Message);

        // 渡す物が無い回だけ E-12（＝時間内に終わらなかった）。
        var quiet = NewWizard(
            TreeWithLedgers(),
            new LauncherSettings { Variant = RuntimeVariants.Cpu },
            new HappyDownloader(),
            new HappyInstaller(),
            static _ => Task.FromResult(false));

        quiet.Accepted = true;
        await quiet.NextAsync();
        await quiet.NextAsync();

        Assert.Equal(FirstRunViewModel.StartFailedLine, quiet.Message);
    }

    // ---- 段 2 の 3 つ ---------------------------------------------------------

    /// <summary>
    /// <b>段 2 だけで ⑴ 量 ⑵ 時間 ⑶ なぜ が揃う</b>（憲章 原則 3 の机上の検分・`v2-spec.md`:1172）。
    /// お知らせに同意済みの回は段 1 を飛ばす（決裁 130 Q3）ので、「なぜ」が段 1 にしか無いと
    /// 〔はじめの準備をやり直す〕から入った利用者は 1 度も読まない。
    /// </summary>
    [Fact]
    public void 段2は量と時間となぜを1画面に持つ()
    {
        var paths = TreeWithLedgers();
        var first = NewWizard(paths, new LauncherSettings());

        // 同じ版のお知らせに同意済み＝段 2 から始まる（見せる段は 2 つ）。
        var settings = new LauncherSettings { AcceptedNoticesSha256 = first.NoticesSha256 };
        var vm = NewWizard(paths, settings);

        Assert.Equal(FirstRunStep.Variant, vm.Step);
        Assert.Equal("1 / 2", vm.StepNumberText);

        Assert.Contains("約 5.3 GB", vm.PlanText, StringComparison.Ordinal);   // ⑴ 量
        Assert.Contains("10 分", vm.PlanText, StringComparison.Ordinal);        // ⑵ 時間
        Assert.Equal(FirstRunViewModel.WhyLine, vm.WhyText);                    // ⑶ なぜ
        Assert.Contains("勝手に配って回らない", vm.WhyText, StringComparison.Ordinal);
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
