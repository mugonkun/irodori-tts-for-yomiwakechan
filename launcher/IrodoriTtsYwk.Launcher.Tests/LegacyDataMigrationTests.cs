using System;
using System.Collections.Generic;
using System.IO;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Ledger;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 段 E-3＝<b>置き場は版ごと</b>（<c>decisions.md</c> 133・<c>v2-spec.md</c> §11-8）。
/// データ樹の既定が版で分かれること・明示指定が勝つこと・<b>旧名が解決に出ない</b>ことを釘付けする。
/// </summary>
public sealed class AppPathsFlavorTests
{
    private static AppPaths Resolve(
        ReleaseFlavor flavor,
        Dictionary<string, string?>? env = null,
        bool settingsOverride = false) =>
        AppPaths.Resolve(
            @"C:\Users\u\AppData\Local\Programs\irodori-tts-ywk-cuda",
            @"C:\Users\u\AppData\Local",
            name => env is not null && env.TryGetValue(name, out var value) ? value : null,
            flavor,
            settingsOverride);

    [Fact]
    public void データ樹の既定は版ごとに分かれる()
    {
        Assert.Equal(
            @"C:\Users\u\AppData\Local\irodori-tts-ywk-cuda", Resolve(ReleaseFlavor.Cuda).DataDir);
        Assert.Equal(
            @"C:\Users\u\AppData\Local\irodori-tts-ywk-radeon", Resolve(ReleaseFlavor.Radeon).DataDir);
    }

    [Fact]
    public void 旧い共有樹の綴りは解決に出ない()
    {
        foreach (var flavor in new[] { ReleaseFlavor.Cuda, ReleaseFlavor.Radeon })
        {
            var paths = Resolve(flavor);
            foreach (var path in new[]
                     {
                         paths.DataDir, paths.VoicesDir, paths.HfHomeDir, paths.LogDir,
                         paths.DownloadCacheDir, paths.RuntimeRoot, paths.SettingsPath,
                     })
            {
                Assert.DoesNotContain(
                    @"Local\" + AppPaths.LegacyDataDirName + @"\", path, StringComparison.Ordinal);
                Assert.False(
                    path.EndsWith(@"Local\" + AppPaths.LegacyDataDirName, StringComparison.Ordinal));
            }

            // 移送の元としてだけ綴りが残る（v2-spec.md §11-8）。
            Assert.Equal(@"C:\Users\u\AppData\Local\irodori-tts-ywk", paths.LegacyDataDir);
        }
    }

    [Fact]
    public void 版ごとの樹は互いに重ならない()
    {
        Assert.NotEqual(Resolve(ReleaseFlavor.Cuda).DataDir, Resolve(ReleaseFlavor.Radeon).DataDir);
        Assert.NotEqual(
            AppPaths.SingleInstanceMutexName(ReleaseFlavor.Cuda),
            AppPaths.SingleInstanceMutexName(ReleaseFlavor.Radeon));
        Assert.NotEqual(
            AppPaths.ActivateEventName(ReleaseFlavor.Cuda),
            AppPaths.ActivateEventName(ReleaseFlavor.Radeon));

        // 錠と合図は**必ず同じ組**で割る（片方だけ割ると 2 個目が向こうの窓を前に出す）。
        Assert.Equal(@"Local\irodori-tts-ywk-launcher-cuda",
            AppPaths.SingleInstanceMutexName(ReleaseFlavor.Cuda));
        Assert.Equal(@"Local\irodori-tts-ywk-launcher-radeon",
            AppPaths.SingleInstanceMutexName(ReleaseFlavor.Radeon));
        Assert.Equal(@"Local\irodori-tts-ywk-launcher-activate-cuda",
            AppPaths.ActivateEventName(ReleaseFlavor.Cuda));
        Assert.Equal(@"Local\irodori-tts-ywk-launcher-activate-radeon",
            AppPaths.ActivateEventName(ReleaseFlavor.Radeon));
    }

    [Fact]
    public void envの明示指定は版より優先する()
    {
        var paths = Resolve(
            ReleaseFlavor.Radeon,
            new Dictionary<string, string?> { [AppPaths.DataDirEnvName] = @"D:\ywk-data" });

        Assert.Equal(@"D:\ywk-data", paths.DataDir);
        Assert.True(paths.DataDirOverridden);
        Assert.Null(paths.LegacyDataDir); // 明示指定の回は移送そのものが無い
    }

    [Fact]
    public void settingsのdataDirも明示指定として扱う()
    {
        var paths = Resolve(ReleaseFlavor.Cuda, settingsOverride: true);

        Assert.True(paths.DataDirOverridden);
        Assert.Null(paths.LegacyDataDir);
    }

    [Fact]
    public void settingsのdataDirは置き場そのものを差し替える()
    {
        // **CANON＝「settings.json の dataDir が勝つ」**（是正・2026-09-11）＝
        // 前は欄を読むだけで誰も使わず、DataDirOverridden が実機で真になる道が無かった。
        var paths = AppPaths.Resolve(
            @"C:\Users\u\AppData\Local\Programs\irodori-tts-ywk-radeon",
            @"C:\Users\u\AppData\Local",
            _ => null,
            ReleaseFlavor.Radeon,
            dataDirOverriddenBySettings: true,
            dataDirFromSettings: @"D:\ywk");

        Assert.Equal(@"D:\ywk", paths.DataDir);
        Assert.Equal(@"D:\ywk\runtime", paths.RuntimeRoot);
        Assert.True(paths.DataDirOverridden);
        Assert.Null(paths.LegacyDataDir);   // 明示指定の回は移送そのものが無い
        Assert.False(paths.DeveloperMode);  // env と違い「開発起動」ではない
    }

    [Fact]
    public void 台帳が読めなければ版を決めたことにしない()
    {
        // 前は DetectFrom が「空を読んだ」と「CUDA だと判った」を同じ顔にしていた＝
        // 名札しか決めていなかった頃は無害だったが、いまはデータ樹・錠・移送先が動く。
        Assert.False(ReleaseFlavors.TryDetectFrom(null, out _));
        Assert.False(ReleaseFlavors.TryDetectFrom("   ", out _));
        Assert.False(
            ReleaseFlavors.TryDetectFrom(
                Path.Combine(Path.GetTempPath(), "ywk-no-such-" + Guid.NewGuid().ToString("N")[..8]),
                out var missing));
        Assert.Equal(ReleaseFlavor.Cuda, missing); // 仮に名乗る値は CUDA のまま
    }

    [Fact]
    public void 台帳が読めない回は導入先の名で版を当てる()
    {
        Assert.True(
            ReleaseFlavors.TryDetectFromDirectoryName(
                @"C:\Users\u\AppData\Local\Programs\irodori-tts-ywk-radeon", out var radeon));
        Assert.Equal(ReleaseFlavor.Radeon, radeon);

        Assert.True(
            ReleaseFlavors.TryDetectFromDirectoryName(
                @"C:\Users\u\AppData\Local\Programs\irodori-tts-ywk-cuda\", out var cuda));
        Assert.Equal(ReleaseFlavor.Cuda, cuda);

        // 版を名乗らない路（開発起動の build\out\app など）は答えない。
        Assert.False(ReleaseFlavors.TryDetectFromDirectoryName(@"C:\src\build\out\app", out _));
        Assert.False(ReleaseFlavors.TryDetectFromDirectoryName(null, out _));
    }

    [Fact]
    public void 版が確かめられたかは既定で真()
    {
        Assert.True(Resolve(ReleaseFlavor.Cuda).FlavorDetermined);
        Assert.False(
            AppPaths.Resolve(
                @"C:\app", @"C:\Users\u\AppData\Local", _ => null,
                ReleaseFlavor.Cuda, flavorDetermined: false).FlavorDetermined);
    }
}

/// <summary>
/// 段 E-3＝<b>旧い共有樹からの移送</b>（<c>decisions.md</c> 133 ⑸・<c>v2-spec.md</c> §11-8）。
/// </summary>
public sealed class LegacyDataMigrationTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ywk-migrate-" + Guid.NewGuid().ToString("N")[..8]);

    private string Legacy => Path.Combine(_root, "irodori-tts-ywk");

    private string Data => Path.Combine(_root, "irodori-tts-ywk-cuda");

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

    private LegacyDataPlan Plan(
        bool overridden = false,
        bool otherFlavorNeedsLegacy = false,
        bool sameVolume = true) =>
        LegacyDataMigration.Plan(
            Legacy,
            Data,
            overridden,
            legacyExists: Directory.Exists(Legacy),
            dataTreeHasFiles: LegacyDataMigration.HasAnyFile(Data),
            sameVolume: sameVolume,
            otherFlavorNeedsLegacy: otherFlavorNeedsLegacy);

    private void SeedLegacy()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "voices", "refs"));
        File.WriteAllText(Path.Combine(Legacy, "settings.json"), "{\"variant\":\"cpu\"}");
        File.WriteAllBytes(Path.Combine(Legacy, "voices", "refs", "ywk-abc.wav"), new byte[16]);
    }

    [Fact]
    public void 同じボリュームなら改名で移す()
    {
        SeedLegacy();

        var plan = Plan();

        Assert.Equal(LegacyDataAction.Rename, plan.Action);
        Assert.Equal(Legacy, plan.From);
        Assert.Equal(Data, plan.To);
    }

    [Fact]
    public void 別ボリュームなら写す()
    {
        SeedLegacy();

        Assert.Equal(LegacyDataAction.Copy, Plan(sameVolume: false).Action);
    }

    [Fact]
    public void もう一方の版が入っていれば写す_旧樹を消さない()
    {
        SeedLegacy();

        var plan = Plan(otherFlavorNeedsLegacy: true);

        Assert.Equal(LegacyDataAction.Copy, plan.Action);
        Assert.Contains("もう一方の版", plan.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void 版の樹が既に使われていれば何もしない()
    {
        SeedLegacy();
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "settings.json"), "{}");

        Assert.Equal(LegacyDataAction.None, Plan().Action);
    }

    [Fact]
    public void 版の樹が空のディレクトリだけなら移送は通る()
    {
        // EnsureDataDirectories が作った直後の姿（檔は 1 つも無い）。
        SeedLegacy();
        foreach (var name in new[] { "voices", "models", "cache", "logs" })
        {
            Directory.CreateDirectory(Path.Combine(Data, name));
        }

        Assert.False(LegacyDataMigration.HasAnyFile(Data));
        Assert.Equal(LegacyDataAction.Rename, Plan().Action);
    }

    [Fact]
    public void 明示指定の回は何もしない()
    {
        SeedLegacy();

        var plan = Plan(overridden: true);

        Assert.Equal(LegacyDataAction.None, plan.Action);
        Assert.Contains("明示", plan.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void 旧樹が無ければ何もしない()
    {
        Assert.Equal(LegacyDataAction.None, Plan().Action);
        Assert.Equal(
            LegacyDataAction.None,
            LegacyDataMigration.Plan(null, Data, false, false, false, true, false).Action);
    }

    [Fact]
    public void 写しは旧樹を1檔も消さない()
    {
        SeedLegacy();

        LegacyDataMigration.CopyTree(Legacy, Data);

        Assert.True(File.Exists(Path.Combine(Data, "settings.json")));
        Assert.True(File.Exists(Path.Combine(Data, "voices", "refs", "ywk-abc.wav")));
        // **利用者の声を失わない**＝旧樹はそのまま残る。
        Assert.True(File.Exists(Path.Combine(Legacy, "settings.json")));
        Assert.True(File.Exists(Path.Combine(Legacy, "voices", "refs", "ywk-abc.wav")));
    }

    [Fact]
    public void 実際に移すと声と設定が版の樹へ移る()
    {
        SeedLegacy();
        var paths = new AppPaths(
            _root, _root, Path.Combine(Data, "runtime"), Data, false,
            ReleaseFlavor.Cuda, dataDirOverridden: false, legacyDataDir: Legacy);

        var plan = LegacyDataMigration.Run(paths);

        Assert.Equal(LegacyDataAction.Rename, plan.Action);
        Assert.True(File.Exists(Path.Combine(Data, "settings.json")));
        Assert.True(File.Exists(Path.Combine(Data, "voices", "refs", "ywk-abc.wav")));
        Assert.False(Directory.Exists(Legacy));

        // 2 度目は何も起きない（1 度だけ）。
        Assert.Equal(LegacyDataAction.None, LegacyDataMigration.Run(paths).Action);
    }

    [Fact]
    public void ログの1行は移した路を名乗る()
    {
        var moved = new LegacyDataPlan(LegacyDataAction.Rename, Legacy, Data, "");

        Assert.Contains(Legacy, LegacyDataMigration.LogLine(moved), StringComparison.Ordinal);
        Assert.Contains(Data, LegacyDataMigration.LogLine(moved), StringComparison.Ordinal);
        Assert.Equal(
            "旧い共有樹が無い。",
            LegacyDataMigration.LogLine(LegacyDataPlan.Nothing("旧い共有樹が無い。")));
    }

    [Fact]
    public void 同じ路を指していれば何もしない()
    {
        Directory.CreateDirectory(Data);

        Assert.Equal(
            LegacyDataAction.None,
            LegacyDataMigration.Plan(Data, Data, false, true, false, true, false).Action);
    }

    // ---- 是正（2026-09-11）--------------------------------------------------

    /// <summary>junction を 1 本張る（<c>mklink /J</c>＝非昇格でも通る）。</summary>
    private static void Junction(string link, string target)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit(15000);
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void 版が確かめられなかった回は移送しない()
    {
        // ledger\ が 1 度読めなかっただけの Radeon 機が、共有樹を CUDA の樹へ移してしまうのを止める。
        // 旧樹はそのまま残るので、次の起動でやり直せる。
        SeedLegacy();

        var plan = LegacyDataMigration.Plan(
            Legacy, Data, false, true, false, true, false, flavorDetermined: false);

        Assert.Equal(LegacyDataAction.None, plan.Action);
        Assert.Contains("確かめられなかった", plan.Reason, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Legacy, "settings.json")));
    }

    [Fact]
    public void 版が確かめられなかった回はRunも旧樹に触らない()
    {
        SeedLegacy();
        var paths = new AppPaths(
            _root, _root, Path.Combine(Data, "runtime"), Data, false,
            ReleaseFlavor.Cuda, dataDirOverridden: false, legacyDataDir: Legacy,
            flavorDetermined: false);

        Assert.Equal(LegacyDataAction.None, LegacyDataMigration.Run(paths).Action);
        Assert.True(File.Exists(Path.Combine(Legacy, "voices", "refs", "ywk-abc.wav")));
        Assert.False(File.Exists(Path.Combine(Data, "voices", "refs", "ywk-abc.wav")));
    }

    [Fact]
    public void 写しは途中の置き場を経てから1手で版の樹になる()
    {
        // **半端な版の樹を残さない**＝落ちた回に残るのは .migrating だけで、版の樹は空のまま＝
        // Plan は次も Copy を返す（版の樹に檔が在ると None になり、移送は二度と再開しない）。
        SeedLegacy();

        LegacyDataMigration.CopyToStaging(Legacy, Data);

        Assert.True(File.Exists(Path.Combine(Data, "settings.json")));
        Assert.True(File.Exists(Path.Combine(Data, "voices", "refs", "ywk-abc.wav")));
        Assert.False(Directory.Exists(LegacyDataMigration.StagingDirFor(Data))); // 残骸は残らない
        Assert.True(File.Exists(Path.Combine(Legacy, "settings.json")));         // 旧樹は 1 檔も消さない
        Assert.Equal(Data + ".migrating", LegacyDataMigration.StagingDirFor(Data));
    }

    [Fact]
    public void 前の回の残骸が在っても写し直せる()
    {
        SeedLegacy();
        var staging = LegacyDataMigration.StagingDirFor(Data);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "half.tmp"), "x");

        LegacyDataMigration.CopyToStaging(Legacy, Data);

        Assert.False(File.Exists(Path.Combine(Data, "half.tmp"))); // 残骸は畳んでから写す
        Assert.True(File.Exists(Path.Combine(Data, "settings.json")));
    }

    [Fact]
    public void 樹のバイトを数えて空きを見る()
    {
        SeedLegacy();

        Assert.Equal(16 + 17, LegacyDataMigration.TreeBytes(Legacy)); // wav 16 B ＋ settings 17 B
        Assert.Equal(0, LegacyDataMigration.TreeBytes(Path.Combine(_root, "nope")));

        // 実機の %TEMP% は 34 B くらい入る（空きが引けない機体は真＝止めない）。
        Assert.True(LegacyDataMigration.FitsOnDestination(Legacy, Data));
    }

    [Fact]
    public void 版の樹の1段下にjunctionが在れば移送しない()
    {
        // `<data>\models` だけを別ドライブへ逃がした樹（docs/install.md §5-3 の手）は
        // 檔を 1 つも持たないので HasAnyFile は偽＝Plan は改名を出す。そのまま消すと
        // .NET は的ではなく環を消す＝利用者の逃がし先が黙って消える。
        SeedLegacy();
        var real = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(real);
        Directory.CreateDirectory(Data);
        var link = Path.Combine(Data, "models");
        Junction(link, real); // mklink /J は昇格が要らない（NTFS の reparse point）

        Assert.True(LegacyDataMigration.HasReparsePointChild(Data));

        var paths = new AppPaths(
            _root, _root, Path.Combine(Data, "runtime"), Data, false,
            ReleaseFlavor.Cuda, dataDirOverridden: false, legacyDataDir: Legacy);

        Assert.Equal(LegacyDataAction.None, LegacyDataMigration.Run(paths).Action);
        Assert.True(Directory.Exists(link));                                     // 環は残る
        Assert.True(File.Exists(Path.Combine(Legacy, "settings.json")));         // 旧樹も残る

        Directory.Delete(link); // 片付け（環だけを外す＝的は Dispose が畳む）
    }
}
