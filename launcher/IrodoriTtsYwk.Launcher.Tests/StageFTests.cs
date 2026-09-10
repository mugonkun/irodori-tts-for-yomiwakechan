using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Ledger;
using IrodoriTtsYwk.Launcher.Services.Settings;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// <b>v2.0 段 F</b>＝⑴ 設定 › 詳細 の 6 行目（更新のとき、新しくなった分だけ取り直す）と
/// ⑵ その 1 行が更新の道に本当に効いていること（段 E の <c>RuntimeDiff</c> の配線）。
/// <para>
/// 段 E は<b>道具</b>（差分の計画・展開器・締め）を入れたが、それを呼ぶ者が居なかった＝
/// 「新しい版に上げると、新しくなった分だけ取り直す」（憲章 §4-24・`v2-copy.md` §6 の 7）は
/// <b>まだ嘘だった</b>。段 F がこの 2 つを結ぶ。だから釘も 2 種類ある＝
/// <b>設定が本当に残るか</b>と、<b>ON の回に丸ごと展開へ行かないか</b>。
/// </para>
/// </summary>
[Collection(AppServicesCollection.Name)]
public sealed class StageFDifferentialUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-stage-f-" + Guid.NewGuid().ToString("N"));

    public StageFDifferentialUpdateTests() => Directory.CreateDirectory(_root);

    // ---- 設定の 1 行（v2-copy.md §4 の 6 行目）--------------------------------------

    [Fact]
    public void 差分の取り直しは既定でON()
    {
        // 憲章 §4-24＝v2.0 の必須要件。切る口は 詳細 の中に在るが、既定は ON である。
        Assert.True(new LauncherSettings().DifferentialUpdate);
    }

    [Fact]
    public void 差分の設定は檔に残り読み戻せる()
    {
        var path = Path.Combine(_root, "settings.json");
        var store = new JsonSettingsStore(path);
        var settings = new LauncherSettings { DifferentialUpdate = false };
        store.Save(settings);

        Assert.Contains(
            "\"differentialUpdate\"", File.ReadAllText(path, Encoding.UTF8), StringComparison.Ordinal);
        Assert.False(new JsonSettingsStore(path).Load().DifferentialUpdate);
    }

    [Fact]
    public void 欄を知らない檔は既定のONで読む()
    {
        // v1.1.0 までの settings.json にはこの欄が無い＝**足すだけで schema は上げない**
        // （契約 ⑻ の規則と同じ流儀）。読めた古い檔は「新しくなった分だけ取り直す」で動く。
        var path = Path.Combine(_root, "old-settings.json");
        File.WriteAllText(path, """{"schema":1,"variant":"cpu"}""", new UTF8Encoding(false));

        Assert.True(new JsonSettingsStore(path).Load().DifferentialUpdate);
    }

    [Fact]
    public void 設定画面の写しは差分の1行を運ぶ()
    {
        // CopyInto に載せ忘れると「適用」で ON へ巻き戻る（便 D（3）の 3 巡目と同じ壊れ方）。
        var from = new LauncherSettings { DifferentialUpdate = false };
        var to = new LauncherSettings();

        SettingsViewModel.CopyInto(from, to);

        Assert.False(to.DifferentialUpdate);
    }

    // ---- 更新の道（MainViewModel.RebuildRuntimeCoreAsync の差分の枝）-----------------
    //
    // **見る継ぎ目は 2 つ**＝⑴ 最初に注文した原檔（差分なら「変わった 1 本」だけ）
    // ⑵ 記録の 1 行（UiStrings.DifferentialStarting／DifferentialFallsBack）。
    // 展開器では見られない＝差分の回の WheelInstaller は
    // RuntimeDiff.ToDifferentialRun が**自分で構える**（入口を 1 つにする、という段 E の設計＝
    // 取得計画と展開器を切り離させない）ので、AppServices.RuntimeInstaller の差し替えは届かない。
    // その代わりこの 2 つは、配線を外せば必ず変わる。

    [Fact]
    public async Task 版が上がった回はONなら変わった分だけを先に取りに行く()
    {
        // 旧台帳の写し（.ledger.json）が在り、3 本のうち 1 本が入れ替わった＝差分で済む回。
        var paths = MakeInstalledTree(appliedSha: "old-sha");
        var downloader = new CountingDownloader();
        AppServices.Downloader = downloader;
        AppServices.RuntimeInstaller = new CountingInstaller();

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        settings.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", "v1.1.0");

        var main = NewMain(paths, settings);
        Assert.True(main.Status.CanRebuildRuntime);

        await main.RebuildRuntimeAsync();

        Assert.Contains(
            UiStrings.DifferentialStarting, main.Status.LogText, StringComparison.Ordinal);
        Assert.NotEmpty(downloader.Wanted);
        // **1 本目が「変わった 1 本」である**＝丸ごとの計画なら python-embed から始まる。
        Assert.Equal("torch-1.whl", downloader.Wanted[0]);
    }

    [Fact]
    public async Task 切ってあれば差分の道に入らない()
    {
        var paths = MakeInstalledTree(appliedSha: "old-sha");
        var downloader = new CountingDownloader();
        var whole = new CountingInstaller();
        AppServices.Downloader = downloader;
        AppServices.RuntimeInstaller = whole;

        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cpu,
            DifferentialUpdate = false,
        };
        settings.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", "v1.1.0");

        var main = NewMain(paths, settings);
        await main.RebuildRuntimeAsync();

        Assert.DoesNotContain(
            UiStrings.DifferentialStarting, main.Status.LogText, StringComparison.Ordinal);
        Assert.Equal(1, whole.Calls);   // いままでどおり丸ごとの道
        Assert.Equal(
            RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu),
            settings.RuntimeLedgerFor(RuntimeVariants.Cpu));
    }

    [Fact]
    public async Task 写しが無い機体はONでも丸ごと入れ直す()
    {
        // .ledger.json を置いていない樹（v1.1.0 以前で組んだ機体）＝差分の相手が居ない。
        // **その 1 回だけ**丸ごと（RuntimeDiff の歯止め ⑴）。
        var paths = MakeInstalledTree(appliedSha: null);
        AppServices.Downloader = new CountingDownloader();
        var whole = new CountingInstaller();
        AppServices.RuntimeInstaller = whole;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        settings.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", "v1.1.0");

        var main = NewMain(paths, settings);
        await main.RebuildRuntimeAsync();

        Assert.Equal(1, whole.Calls);
        Assert.Contains(
            UiStrings.DifferentialFallsBack, main.Status.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            UiStrings.DifferentialStarting, main.Status.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 差分の締めが通らなければ丸ごとへ落ちて樹を半端に残さない()
    {
        // 差分の道に入りはするが、当てた結果が台帳と合わない（この試験の wheel は本物でない）＝
        // RuntimeDiff.VerifyAfterApply が偽を返す回である。**そこで黙って終わらない**＝
        // 丸ごと入れ直しが拾い、焼き印は最後まで通った道の値になる。
        var paths = MakeInstalledTree(appliedSha: "old-sha");
        AppServices.Downloader = new CountingDownloader();
        var whole = new CountingInstaller();
        AppServices.RuntimeInstaller = whole;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        settings.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", "v1.1.0");

        var main = NewMain(paths, settings);
        await main.RebuildRuntimeAsync();

        Assert.Contains(
            UiStrings.DifferentialStarting, main.Status.LogText, StringComparison.Ordinal);
        Assert.Equal(1, whole.Calls);
        Assert.False(main.Status.CanRebuildRuntime);   // 1 行も 1 手も消える
        Assert.Equal(AppVersion.Display, settings.InstalledAppVersionFor(RuntimeVariants.Cpu));
    }

    [Fact]
    public async Task 差分が通った回は丸ごとの展開器を1度も呼ばず古い名残も残さない()
    {
        // **「ON で、しかも通った」回の釘**（是正・2026-09-11・medium 7）。
        // これが無かったので、差分の道を 1 度も成功させない 3 つの塞ぎ（埋め込み Python の要求・
        // 切れ端の台帳との件数突合・古い *.dist-info の残り）が 897 本を緑のまま通り抜けた。
        // 上の 4 本は ON／OFF／写し無し／締めが落ちる回＝**どれも丸ごとへ落ちる**回しか見ていない。
        var paths = MakeRealTree();
        var downloader = new CountingDownloader();
        var whole = new CountingInstaller();
        AppServices.Downloader = downloader;
        AppServices.RuntimeInstaller = whole;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        settings.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", "v1.1.0");

        var main = NewMain(paths, settings);
        Assert.True(main.Status.CanRebuildRuntime);

        await main.RebuildRuntimeAsync();

        Assert.Contains(UiStrings.DifferentialDone, main.Status.LogText, StringComparison.Ordinal);
        Assert.Equal(0, whole.Calls);       // 丸ごとの展開器は 1 度も呼ばれない
        Assert.Empty(downloader.Wanted);    // 変わった 1 本は cache に在る＝落としに行かない

        // **古い torch-1 は残らない**＝残ると締めの件数が 1 件多くなり、版が上がった回
        // （一番ありふれた回）が必ず丸ごとへ落ちていた。
        var sitePackages = Path.Combine(
            paths.RuntimeRoot, RuntimeVariants.Cpu, "site-packages");
        Assert.Equal(
            ["numpy-1.dist-info", "scipy-1.dist-info", "torch-2.dist-info"],
            Directory.GetDirectories(sitePackages, "*.dist-info")
                .Select(static d => Path.GetFileName(d)!)
                .OrderBy(static n => n, StringComparer.Ordinal)
                .ToArray());
        Assert.True(File.Exists(Path.Combine(sitePackages, "torch", "__init__.py")));

        // 焼き印と写しは**通ってから**入る（次の版の差分の材料）。1 行と 1 手は消える。
        Assert.Equal(
            RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu),
            settings.RuntimeLedgerFor(RuntimeVariants.Cpu));
        Assert.Equal(AppVersion.Display, settings.InstalledAppVersionFor(RuntimeVariants.Cpu));
        Assert.True(File.Exists(RuntimeStamp.AppliedLedgerPath(paths, RuntimeVariants.Cpu)));
        Assert.False(main.Status.CanRebuildRuntime);
    }

    [Fact]
    public async Task 中身が1件も動いていない回は何も落とさず何も入れ替えない()
    {
        // 持ち物の一覧の檔は別物になった（sha256 が動いた＝状態帯は 1 手を出す）のに、
        // item はどれも同じ＝落とす物も入れ替える物も無い回。ここを素通りさせると
        // 0 件の当て込みが失敗し、見出しが動いただけの更新に数 GiB を払わせることになる。
        var paths = MakeInstalledTree(appliedSha: "bb");
        var downloader = new CountingDownloader();
        var whole = new CountingInstaller();
        AppServices.Downloader = downloader;
        AppServices.RuntimeInstaller = whole;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        settings.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", "v1.1.0");

        var main = NewMain(paths, settings);
        await main.RebuildRuntimeAsync();

        Assert.Contains(
            UiStrings.DifferentialNothingToDo, main.Status.LogText, StringComparison.Ordinal);
        Assert.Equal(0, whole.Calls);
        Assert.Empty(downloader.Wanted);
        Assert.Equal(
            RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu),
            settings.RuntimeLedgerFor(RuntimeVariants.Cpu));
        Assert.False(main.Status.CanRebuildRuntime);
    }

    [Fact]
    public async Task 途中で切れている樹には差分を当てない()
    {
        // 写しは在るので計画は組めるが、当てた先で締めが必ず落ちる＝丸ごとへ行く前に
        // 樹をもう一度いじるだけになる。**入る前に**止める（値を告げる側も同じ条件を見る）。
        var paths = MakeInstalledTree(appliedSha: "old-sha", distInfos: 1);
        var downloader = new CountingDownloader();
        var whole = new CountingInstaller();
        AppServices.Downloader = downloader;
        AppServices.RuntimeInstaller = whole;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        settings.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", "v1.1.0");

        var main = NewMain(paths, settings);
        await main.RebuildRuntimeAsync();

        Assert.DoesNotContain(
            UiStrings.DifferentialStarting, main.Status.LogText, StringComparison.Ordinal);
        Assert.Equal(1, whole.Calls);
    }

    [Fact]
    public void 素性の知れない樹を受け入れても写しは置かない()
    {
        // 裁定 91 の受け入れ（*.dist-info の件数が合う樹は焼き直して黙る）は、
        // **何で組んだかを知らない**。写しまで置くと「中身は全部この内容である」と名乗ることになり、
        // 次の版の差分がその嘘を信じて古い wheel を見送る＝混ざった樹が残る。
        var paths = MakeInstalledTree(appliedSha: null);
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };

        var main = NewMain(paths, settings);
        main.CheckRuntimeStamp();

        Assert.Equal(
            RuntimeStamp.LedgerSha256(paths, RuntimeVariants.Cpu),
            settings.RuntimeLedgerFor(RuntimeVariants.Cpu));
        Assert.False(File.Exists(RuntimeStamp.AppliedLedgerPath(paths, RuntimeVariants.Cpu)));
    }

    [Fact]
    public void 一覧も版も動いた回は版だけ先に焼き直す()
    {
        // 同梱の声の突き合わせ（12 檔・35 MB 級）は「版が変わった回だけ」の門で守られているが、
        // その門が見る欄は**組み直しが通るまで**古いままだった＝入れ直しを後回しにしている間、
        // 毎起動その代金を払っていた。版の欄だけ先に焼き直す（内容の側は古いまま＝催促は続く）。
        var paths = MakeInstalledTree(appliedSha: null);
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        settings.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", "v1.1.0");

        var main = NewMain(paths, settings);
        main.CheckRuntimeStamp();

        Assert.True(main.Status.CanRebuildRuntime);                         // 1 手は残る
        Assert.Equal("0123456789abcdef", settings.RuntimeLedgerFor(RuntimeVariants.Cpu));
        Assert.Equal(AppVersion.Display, settings.InstalledAppVersionFor(RuntimeVariants.Cpu));
    }

    [Fact]
    public void 同じ名の古い畳みは版が違っても見つかる()
    {
        // 「同じ名」の判定は wheel の綴り（PEP 503＝小文字・記号は _ 1 つ）で行う。
        // typing-extensions と typing_extensions を別物と見ると、古い畳みが残って締めが落ちる。
        var sitePackages = Path.Combine(_root, "sp");
        Directory.CreateDirectory(sitePackages);
        foreach (var name in new[]
                 { "torch-1.dist-info", "torch_vision-1.dist-info", "numpy-1.dist-info" })
        {
            Directory.CreateDirectory(Path.Combine(sitePackages, name));
        }

        Assert.Equal(
            ["torch-1.dist-info"],
            WheelInstaller.SupersededDistInfoDirectories(sitePackages, "torch")
                .Select(static d => Path.GetFileName(d)!).ToArray());
        Assert.Equal(
            ["torch_vision-1.dist-info"],
            WheelInstaller.SupersededDistInfoDirectories(sitePackages, "torch-vision")
                .Select(static d => Path.GetFileName(d)!).ToArray());
        Assert.Empty(WheelInstaller.SupersededDistInfoDirectories(sitePackages, "scipy"));
    }

    // ---- 下ごしらえ ------------------------------------------------------------------

    /// <summary>
    /// 組み上がった樹（<c>python.exe</c> ＋ <c>*.dist-info</c> 3 枚）と台帳 2 枚。
    /// <paramref name="appliedSha"/> が null でなければ、その sha256 を持つ
    /// <c>.ledger.json</c>（展開に使った台帳の写し）も置く＝差分が組める機体になる。
    /// <para>
    /// <b>wheel を 3 本にしてあるのは意味が在る</b>＝<see cref="RuntimeDiff.RebuildFraction"/> は
    /// 「入れ替わる item が全体の 6 割を超えたら丸ごと」である。1 本だけの台帳では
    /// <b>どの変更も 100 %</b>になり、差分の枝を 1 度も通れない。3 本のうち 1 本＝33 % で通る。
    /// </para>
    /// </summary>
    private AppPaths MakeInstalledTree(string? appliedSha, int distInfos = 3)
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
               "sha256":"bb","size":1000,"license":"BSD-3-Clause"},
              {"kind":"wheel","name":"numpy","url":"https://example.invalid/numpy-1.whl",
               "sha256":"cc","size":200,"license":"BSD-3-Clause"},
              {"kind":"wheel","name":"scipy","url":"https://example.invalid/scipy-1.whl",
               "sha256":"dd","size":300,"license":"BSD-3-Clause"}]}
            """,
            new UTF8Encoding(false));

        var runtimeDir = Path.Combine(paths.RuntimeRoot, RuntimeVariants.Cpu);
        Directory.CreateDirectory(runtimeDir);
        File.WriteAllBytes(Path.Combine(runtimeDir, AppPaths.PythonExeName), []);
        foreach (var distInfo in new[] { "torch-1.dist-info", "numpy-1.dist-info", "scipy-1.dist-info" }
                     .Take(distInfos))
        {
            Directory.CreateDirectory(Path.Combine(runtimeDir, "site-packages", distInfo));
        }

        // 変わっていない 2 本の原檔は cache に揃っている＝差分の回は落としに行かない。
        File.WriteAllBytes(Path.Combine(paths.DownloadCacheDir, "numpy-1.whl"), new byte[200]);
        File.WriteAllBytes(Path.Combine(paths.DownloadCacheDir, "scipy-1.whl"), new byte[300]);

        if (appliedSha is not null)
        {
            // 同じ名の 1 本が別の sha256 で入っていた＝「変わった item が 1 件」。
            // **写しは LedgerReader.Validate を通る**（RuntimeStamp.ReadAppliedLedger）ので、
            // license 欄まで揃った本物の形で置く＝欠けると黙って null＝丸ごとへ落ちる。
            File.WriteAllText(
                RuntimeStamp.AppliedLedgerPath(paths, RuntimeVariants.Cpu),
                """
                {"schema":1,"items":[
                  {"kind":"wheel","name":"torch","url":"https://example.invalid/torch-0.whl",
                   "sha256":"OLD","size":1000,"license":"BSD-3-Clause"},
                  {"kind":"wheel","name":"numpy","url":"https://example.invalid/numpy-1.whl",
                   "sha256":"cc","size":200,"license":"BSD-3-Clause"},
                  {"kind":"wheel","name":"scipy","url":"https://example.invalid/scipy-1.whl",
                   "sha256":"dd","size":300,"license":"BSD-3-Clause"}]}
                """.Replace("OLD", appliedSha, StringComparison.Ordinal),
                new UTF8Encoding(false));
        }

        return paths;
    }

    /// <summary>
    /// <b>本物の zip で組んだ樹</b>＝差分の道を<b>最後まで通す</b>ための下ごしらえ。
    /// <para>
    /// <see cref="MakeInstalledTree"/> との違いは 2 つ＝⑴ 変わった 1 本（<c>torch-2.whl</c>）が
    /// <b>本当に展開できる zip</b> で、台帳の <c>sha256</c>／<c>size</c> もその実測値である
    /// ⑵ <c>python312._pth</c> の雛形を置く（展開器が書く先）。
    /// <b>埋め込み Python の zip は置かない</b>＝差分の回はそれを当てない、というのが釘の一部である
    /// （置いてしまうと「当てないこと」を確かめられない）。
    /// </para>
    /// </summary>
    private AppPaths MakeRealTree()
    {
        var appDir = Path.Combine(_root, "real-app");
        Directory.CreateDirectory(Path.Combine(appDir, "ledger"));
        Directory.CreateDirectory(Path.Combine(appDir, "server"));
        Directory.CreateDirectory(Path.Combine(_root, "real-data", "cache"));
        var paths = new AppPaths(
            Path.Combine(_root, "real-install"),
            appDir,
            Path.Combine(_root, "real-runtime"),
            Path.Combine(_root, "real-data"),
            developerMode: true);

        File.WriteAllText(
            paths.PthTemplatePath,
            "python312.zip\n.\n@RUNTIME_DIR@/site-packages\n@APP_DIR@/server\n",
            new UTF8Encoding(false));

        // 変わった 1 本＝版が 1 → 2 に上がった wheel（畳みの名も torch-1 → torch-2 に動く）。
        var wheel = Path.Combine(paths.DownloadCacheDir, "torch-2.whl");
        TestArchives.WriteZip(wheel, new Dictionary<string, string>
        {
            ["torch/__init__.py"] = "version = 2\n",
            ["torch-2.dist-info/METADATA"] = "Name: torch\nVersion: 2\n",
            ["torch-2.dist-info/RECORD"] = "torch/__init__.py,,\n",
        });
        var sha = TestArchives.Sha256Of(wheel);
        var size = new FileInfo(wheel).Length;

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
              {"kind":"wheel","name":"torch","url":"https://example.invalid/torch-2.whl",
               "sha256":"NEWSHA","size":NEWSIZE,"license":"BSD-3-Clause"},
              {"kind":"wheel","name":"numpy","url":"https://example.invalid/numpy-1.whl",
               "sha256":"cc","size":200,"license":"BSD-3-Clause"},
              {"kind":"wheel","name":"scipy","url":"https://example.invalid/scipy-1.whl",
               "sha256":"dd","size":300,"license":"BSD-3-Clause"}]}
            """
                .Replace("NEWSHA", sha, StringComparison.Ordinal)
                .Replace("NEWSIZE", size.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
            new UTF8Encoding(false));

        var runtimeDir = Path.Combine(paths.RuntimeRoot, RuntimeVariants.Cpu);
        Directory.CreateDirectory(runtimeDir);
        File.WriteAllBytes(Path.Combine(runtimeDir, AppPaths.PythonExeName), []);
        foreach (var distInfo in new[] { "torch-1.dist-info", "numpy-1.dist-info", "scipy-1.dist-info" })
        {
            Directory.CreateDirectory(Path.Combine(runtimeDir, "site-packages", distInfo));
        }

        File.WriteAllText(
            RuntimeStamp.AppliedLedgerPath(paths, RuntimeVariants.Cpu),
            """
            {"schema":1,"items":[
              {"kind":"wheel","name":"torch","url":"https://example.invalid/torch-1.whl",
               "sha256":"bb","size":1000,"license":"BSD-3-Clause"},
              {"kind":"wheel","name":"numpy","url":"https://example.invalid/numpy-1.whl",
               "sha256":"cc","size":200,"license":"BSD-3-Clause"},
              {"kind":"wheel","name":"scipy","url":"https://example.invalid/scipy-1.whl",
               "sha256":"dd","size":300,"license":"BSD-3-Clause"}]}
            """,
            new UTF8Encoding(false));

        return paths;
    }

    private static MainViewModel NewMain(AppPaths paths, LauncherSettings settings)
    {
        AppServices.GpuEnumerator = null;
        return new MainViewModel(
            paths, settings, new JsonSettingsStore(paths.SettingsPath), new SilentPlayer());
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
            DownloadRequest request,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class CountingInstaller : IRuntimeInstaller
    {
        public int Calls { get; private set; }

        public Task<InstallResult> InstallAsync(
            InstallRequest request,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new InstallResult(true, 101, 4096, 1, [], null, null));
        }
    }

    public void Dispose()
    {
        AppServices.Downloader = null;
        AppServices.RuntimeInstaller = null;
        AppServices.GpuEnumerator = null;
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
