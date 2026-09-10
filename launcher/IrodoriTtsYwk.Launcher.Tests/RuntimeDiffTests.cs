using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Ledger;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 段 E-2＝<b>動かすための一式の差分</b>（<c>v2-spec.md</c> §11-3 の 4 枝＋歯止め）。
/// </summary>
public sealed class RuntimeDiffTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ywk-diff-" + Guid.NewGuid().ToString("N")[..8]);

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
    }

    private static LedgerItem Wheel(string name, string sha, long size = 1000) => new()
    {
        Kind = LedgerItemKinds.Wheel,
        Name = name,
        Version = "1.0.0",
        Url = "https://example.invalid/" + name + "-1.0.0-py3-none-any.whl",
        Sha256 = sha,
        Size = size,
    };

    private static LedgerFile Ledger(params LedgerItem[] items) =>
        new() { Name = "runtime-cpu", Items = items };

    private static string Sha(char c) => new(c, 64);

    [Fact]
    public void 枝1_同じ名で同じsha256なら触らない()
    {
        var plan = RuntimeDiff.Plan(
            Ledger(Wheel("torch", Sha('a')), Wheel("numpy", Sha('b'))),
            Ledger(Wheel("torch", Sha('a')), Wheel("numpy", Sha('b'))));

        Assert.False(plan.RebuildAll);
        Assert.True(plan.UpToDate);
        Assert.Empty(plan.Fetch);
        Assert.Equal(0, plan.Bytes);
    }

    [Fact]
    public void 枝2_shaが違う1檔だけを落とす()
    {
        var plan = RuntimeDiff.Plan(
            Ledger(Wheel("torch", Sha('a')), Wheel("numpy", Sha('b')), Wheel("scipy", Sha('c'))),
            Ledger(Wheel("torch", Sha('a')), Wheel("numpy", Sha('d'), 320), Wheel("scipy", Sha('c'))));

        Assert.False(plan.RebuildAll);
        Assert.Equal(["numpy"], plan.Changed.Select(static i => i.Name));
        Assert.Empty(plan.Added);
        Assert.Equal(320, plan.Bytes);
    }

    [Fact]
    public void 枝3_新にだけ在る物は落として入れる()
    {
        var plan = RuntimeDiff.Plan(
            Ledger(Wheel("torch", Sha('a')), Wheel("numpy", Sha('b')), Wheel("scipy", Sha('c'))),
            Ledger(
                Wheel("torch", Sha('a')), Wheel("numpy", Sha('b')),
                Wheel("scipy", Sha('c')), Wheel("filelock", Sha('e'), 42)));

        Assert.False(plan.RebuildAll);
        Assert.Equal(["filelock"], plan.Added.Select(static i => i.Name));
        Assert.Equal(42, plan.Bytes);
    }

    [Fact]
    public void 枝4_旧にだけ在る物は消さない_丸ごとへ落とす()
    {
        var plan = RuntimeDiff.Plan(
            Ledger(Wheel("torch", Sha('a')), Wheel("numpy", Sha('b')), Wheel("old-thing", Sha('f'))),
            Ledger(Wheel("torch", Sha('a')), Wheel("numpy", Sha('b'))));

        // **消す指図は 1 つも出さない**（site-packages から wheel 1 本を安全に抜く手が無い）。
        Assert.Equal(["old-thing"], plan.Removed);
        Assert.Empty(plan.Fetch);
        Assert.True(plan.RebuildAll);
        Assert.Contains("old-thing", plan.RebuildReason!, StringComparison.Ordinal);
        Assert.Contains("old-thing", RuntimeDiff.RemovedLine(plan.Removed)!, StringComparison.Ordinal);
    }

    [Fact]
    public void _6割を超えたら丸ごと組み直す()
    {
        var old = Ledger(
            Wheel("a", Sha('1')), Wheel("b", Sha('2')), Wheel("c", Sha('3')),
            Wheel("d", Sha('4')), Wheel("e", Sha('5')));
        var fresh = Ledger(
            Wheel("a", Sha('9')), Wheel("b", Sha('9')), Wheel("c", Sha('9')),
            Wheel("d", Sha('9')), Wheel("e", Sha('5')));

        var plan = RuntimeDiff.Plan(old, fresh);

        Assert.True(plan.RebuildAll); // 4/5 = 0.8 > 0.6
        Assert.Empty(plan.Fetch);

        // 3/5 = 0.6 ちょうどは差分のまま（「超える」＝厳密に大きいとき）。
        var three = Ledger(
            Wheel("a", Sha('9')), Wheel("b", Sha('9')), Wheel("c", Sha('9')),
            Wheel("d", Sha('4')), Wheel("e", Sha('5')));
        Assert.False(RuntimeDiff.Plan(old, three).RebuildAll);
    }

    [Fact]
    public void 台帳の写しが無ければその1回だけ丸ごと()
    {
        var plan = RuntimeDiff.Plan(null, Ledger(Wheel("torch", Sha('a'))));

        Assert.True(plan.RebuildAll);
        Assert.Contains("写しが無い", plan.RebuildReason!, StringComparison.Ordinal);
        Assert.Empty(plan.Fetch);

        // 空の台帳も同じ（items が 0 件）。
        Assert.True(RuntimeDiff.Plan(Ledger(), Ledger(Wheel("torch", Sha('a')))).RebuildAll);
    }

    [Fact]
    public void sha256の無いitemは取らない()
    {
        var plan = RuntimeDiff.Plan(
            Ledger(Wheel("torch", Sha('a'))),
            Ledger(Wheel("torch", Sha('a')), Wheel("mystery", null!)));

        Assert.True(plan.RebuildAll);
        Assert.Equal(["mystery"], plan.Unverifiable);
        Assert.Empty(plan.Fetch);
    }

    [Fact]
    public void 順序が違っても結果は同じ()
    {
        var old = Ledger(Wheel("a", Sha('1')), Wheel("b", Sha('2')), Wheel("c", Sha('3')));
        var forward = Ledger(Wheel("a", Sha('1')), Wheel("b", Sha('9')), Wheel("c", Sha('3')));
        var backward = Ledger(Wheel("c", Sha('3')), Wheel("b", Sha('9')), Wheel("a", Sha('1')));

        Assert.Equal(
            RuntimeDiff.Plan(old, forward).Changed.Select(static i => i.Name),
            RuntimeDiff.Plan(old, backward).Changed.Select(static i => i.Name));
    }

    [Fact]
    public void 差分の回はcleanせずに構える()
    {
        var plan = RuntimeDiff.Plan(
            Ledger(Wheel("torch", Sha('a')), Wheel("numpy", Sha('b'))),
            Ledger(Wheel("torch", Sha('a')), Wheel("numpy", Sha('d'), 320)));

        var (_, differential) = RuntimeDiff.ToDifferentialRun(RuntimeVariants.Cpu, plan);

        Assert.False(differential.CleanBeforeInstall);
        Assert.False(differential.RemovePartialOnFailure);

        // 丸ごと組み直しの回の既定（＝初回取得の道）はそのまま。
        var whole = new WheelInstaller();
        Assert.True(whole.CleanBeforeInstall);
        Assert.True(whole.RemovePartialOnFailure);
    }

    [Fact]
    public void 丸ごとの計画を差分の道へ流したら投げる()
    {
        // **取得計画と展開器を切り離せない**＝差分の計画を既定の WheelInstaller に流すと
        // 頭で樹が空になり「変わった数本だけの一式」が残る。入口は 1 本しか無い。
        var rebuild = RuntimeDiff.Plan(null, Ledger(Wheel("torch", Sha('a'))));

        Assert.True(rebuild.RebuildAll);
        Assert.Throws<InvalidOperationException>(
            () => RuntimeDiff.ToDifferentialRun(RuntimeVariants.Cpu, rebuild));
    }

    [Fact]
    public void 差分の取得計画はvc_redistもモデルも含まない()
    {
        var plan = RuntimeDiff.Plan(
            Ledger(Wheel("torch", Sha('a')), Wheel("numpy", Sha('b'))),
            Ledger(Wheel("torch", Sha('a')), Wheel("numpy", Sha('d'), 320)));

        var (fetch, _) = RuntimeDiff.ToDifferentialRun(RuntimeVariants.Cpu, plan);

        Assert.Empty(fetch.Stage(FetchStage.VcRedist));
        Assert.Empty(fetch.Stage(FetchStage.Models));
        Assert.Empty(fetch.Stage(FetchStage.PythonEmbed));
        Assert.Equal(["numpy"], fetch.Stage(FetchStage.Runtime).Select(static s => s.Name));
        Assert.Equal(320, fetch.TotalBytes);
    }

    [Fact]
    public void 押す前に量を告げる()
    {
        Assert.Null(RuntimeDiff.DownloadNotice(0));
        var line = RuntimeDiff.DownloadNotice(320L * 1024 * 1024);
        Assert.StartsWith("約 320.0 MiB をダウンロードします（", line, StringComparison.Ordinal);
        Assert.EndsWith("分ほど）。", line, StringComparison.Ordinal);
    }

    // ---- .ledger.json（適用済みの台帳の写し）--------------------------------

    private AppPaths Paths()
    {
        var appDir = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(appDir, "ledger"));
        File.WriteAllText(
            Path.Combine(appDir, "ledger", "runtime-cpu.json"),
            """
            {"schema":1,"name":"runtime-cpu","count":1,"items":[
              {"kind":"wheel","name":"torch","version":"2.9.0","url":"https://example.invalid/torch.whl",
               "license":"BSD-3-Clause",
               "sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":100}]}
            """,
            new UTF8Encoding(false));

        return AppPaths.Resolve(
            appDir, null,
            name => name == AppPaths.AppDirEnvName ? appDir
                : name == AppPaths.DataDirEnvName ? Path.Combine(_root, "data")
                : null);
    }

    [Fact]
    public void 焼き印と同じ回に台帳の写しを置く()
    {
        var paths = Paths();
        var settings = new LauncherSettings();

        RuntimeStamp.Burn(settings, paths, RuntimeVariants.Cpu);

        var copy = RuntimeStamp.AppliedLedgerPath(paths, RuntimeVariants.Cpu);
        Assert.True(File.Exists(copy));
        Assert.EndsWith(
            Path.Combine("runtime", RuntimeVariants.Cpu, ".ledger.json"), copy, StringComparison.Ordinal);
        // **データ樹の中**にしか書かない（配布樹には 1 檔も書かない）。
        Assert.StartsWith(paths.DataDir, copy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(paths.AppDir, copy, StringComparison.OrdinalIgnoreCase);

        var read = RuntimeStamp.ReadAppliedLedger(paths, RuntimeVariants.Cpu);
        Assert.Equal(["torch"], read!.Items.Select(static i => i.Name));
    }

    [Fact]
    public void 写しが読めない回は従来どおり丸ごとへ落ちる()
    {
        var paths = Paths();

        Assert.Null(RuntimeStamp.ReadAppliedLedger(paths, RuntimeVariants.Cpu));

        var copy = RuntimeStamp.AppliedLedgerPath(paths, RuntimeVariants.Cpu);
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.WriteAllText(copy, "{ this is not json", new UTF8Encoding(false));

        Assert.Null(RuntimeStamp.ReadAppliedLedger(paths, RuntimeVariants.Cpu));
        Assert.True(
            RuntimeDiff.Plan(
                RuntimeStamp.ReadAppliedLedger(paths, RuntimeVariants.Cpu),
                Ledger(Wheel("torch", Sha('a')))).RebuildAll);
    }

    [Fact]
    public void 素性の知れない樹には写しを置かない()
    {
        // 裁定 91 の受け入れ（MainViewModel.CheckRuntimeStamp）は「どの台帳で組んだか判らない」樹に
        // 焼き印を押す＝催促を止めるだけなら無害だが、.ledger.json を置くと
        // 「この樹の中身は全部この sha256 である」という**嘘**になり、次の版の差分がそれを信じる。
        var paths = Paths();
        var settings = new LauncherSettings();

        RuntimeStamp.Burn(settings, paths, RuntimeVariants.Cpu, writeAppliedLedger: false);

        Assert.False(File.Exists(RuntimeStamp.AppliedLedgerPath(paths, RuntimeVariants.Cpu)));
        // 焼き印そのものは押してある（催促は止まる）。
        Assert.NotNull(settings.RuntimeLedgerFor(RuntimeVariants.Cpu));
        // 写しが無い＝次の版は丸ごと組み直しへ落ちる＝素性の知れない樹には、それが正しい。
        Assert.True(
            RuntimeDiff.Plan(
                RuntimeStamp.ReadAppliedLedger(paths, RuntimeVariants.Cpu),
                Ledger(Wheel("torch", Sha('a')))).RebuildAll);
    }

    [Fact]
    public void 差分の締めは件数が合わなければ偽を返す()
    {
        // §11-3 の歯止め ⑵＝差分は RemovePartialOnFailure=false で構えるので、途中で落ちた樹は
        // 新旧が混ざったまま残る。締めで *.dist-info を数えて合わなければ丸ごとへ落とす。
        var paths = Paths();
        var ledger = Ledger(Wheel("torch", Sha('a')), Wheel("numpy", Sha('b')));
        var runtimeDir = Path.Combine(_root, "data", "runtime", RuntimeVariants.Cpu);
        var sitePackages = Path.Combine(runtimeDir, "site-packages");
        Directory.CreateDirectory(sitePackages);
        File.WriteAllBytes(Path.Combine(runtimeDir, AppPaths.PythonExeName), new byte[4]);

        // 2 本の台帳に対して 1 本しか入っていない＝短い樹。
        Directory.CreateDirectory(Path.Combine(sitePackages, "torch-2.9.0.dist-info"));
        Assert.False(RuntimeDiff.VerifyAfterApply(paths, ledger, RuntimeVariants.Cpu));

        Directory.CreateDirectory(Path.Combine(sitePackages, "numpy-2.3.0.dist-info"));
        Assert.True(RuntimeDiff.VerifyAfterApply(paths, ledger, RuntimeVariants.Cpu));
    }
}
