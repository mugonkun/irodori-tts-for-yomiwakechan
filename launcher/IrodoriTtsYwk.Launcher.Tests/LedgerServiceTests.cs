using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Ledger;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>リポの <c>ledger/</c> を探す（テストの出力先から上へ辿る）。</summary>
internal static class RepoLedger
{
    public static string? Directory()
    {
        string? dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "ledger");
            if (File.Exists(Path.Combine(candidate, "python-embed.json")))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    /// <summary>リポの根（<c>ledger/</c> の親）。</summary>
    public static string? RepoRoot()
    {
        var ledger = Directory();
        return ledger is null ? null : Path.GetDirectoryName(ledger);
    }
}

public sealed class LedgerReaderTests
{
    private const string MinimalWheelLedger = """
        {
          "schema": 1,
          "name": "runtime-test",
          "count": 1,
          "items": [
            { "kind": "wheel", "name": "numpy", "version": "2.0.0",
              "url": "https://example.invalid/numpy-2.0.0-cp312-cp312-win_amd64.whl",
              "sha256": "aa", "size": 12, "license": "BSD-3-Clause" }
          ]
        }
        """;

    [Fact]
    public void 素の台帳が型に落ちる()
    {
        var ledger = LedgerReader.ParseRuntime(MinimalWheelLedger, "runtime-test");

        Assert.Single(ledger.Items);
        Assert.True(ledger.CountMatches);
        Assert.Equal(12, ledger.TotalBytes);
        Assert.Equal("numpy-2.0.0-cp312-cp312-win_amd64.whl", ledger.Items[0].EffectiveFileName);
    }

    [Fact]
    public void countと件数の食い違いで止まる()
    {
        var broken = MinimalWheelLedger.Replace("\"count\": 1", "\"count\": 2", StringComparison.Ordinal);

        var ex = Assert.Throws<LedgerException>(() => LedgerReader.ParseRuntime(broken, "runtime-test"));
        Assert.Contains("count", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void sha256の無いitemは受けない()
    {
        // assemble-runtime.ps1 の "refusing to install it" と同じ判断
        var broken = MinimalWheelLedger.Replace("\"sha256\": \"aa\",", string.Empty, StringComparison.Ordinal);

        var ex = Assert.Throws<LedgerException>(() => LedgerReader.ParseRuntime(broken, "runtime-test"));
        Assert.Contains("sha256", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void license欄が空のitemは受けない()
    {
        // build/check-licenses.ps1 と同じ検分
        var broken = MinimalWheelLedger.Replace("\"license\": \"BSD-3-Clause\"", "\"license\": \"\"",
            StringComparison.Ordinal);

        var ex = Assert.Throws<LedgerException>(() => LedgerReader.ParseRuntime(broken, "runtime-test"));
        Assert.Contains("license", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void package_dirの無いsdistは受けない()
    {
        var broken = MinimalWheelLedger.Replace("\"kind\": \"wheel\"", "\"kind\": \"sdist\"", StringComparison.Ordinal);

        var ex = Assert.Throws<LedgerException>(() => LedgerReader.ParseRuntime(broken, "runtime-test"));
        Assert.Contains("package_dir", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 未知のkindは受けない()
    {
        var broken = MinimalWheelLedger.Replace("\"kind\": \"wheel\"", "\"kind\": \"conda\"", StringComparison.Ordinal);

        var ex = Assert.Throws<LedgerException>(() => LedgerReader.ParseRuntime(broken, "runtime-test"));
        Assert.Contains("conda", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 埋め込みPythonを先頭に足す()
    {
        var runtime = LedgerReader.ParseRuntime(MinimalWheelLedger, "runtime-test");
        var embed = LedgerReader.ParseRuntime("""
            { "schema": 1, "name": "python-embed", "count": 1, "items": [
              { "kind": "python-embed", "name": "python", "version": "3.12.10",
                "url": "https://example.invalid/python-3.12.10-embed-amd64.zip",
                "sha256": "bb", "size": 3, "license": "PSF-2.0" } ] }
            """, "python-embed");

        var merged = LedgerReader.WithPythonEmbed(runtime, embed);

        Assert.Equal(2, merged.Items.Count);
        Assert.Equal(LedgerItemKinds.PythonEmbed, merged.Items[0].Kind);
        Assert.Null(merged.Count); // 足した後は件数を名乗らない
        Assert.True(merged.CountMatches);
    }

    [Fact]
    public void 実物のcpu台帳が読める()
    {
        var ledgerDir = RepoLedger.Directory();
        if (ledgerDir is null)
        {
            return; // リポ外＝この検分は飛ばす
        }

        var reader = new LedgerReader(ledgerDir);
        var cpu = reader.ReadRuntime(RuntimeVariants.Cpu);

        Assert.Equal("runtime-cpu", cpu.Name);
        Assert.Equal(101, cpu.Items.Count);
        Assert.True(cpu.CountMatches);
        // §2 の kind の表と 1 対 1（wheel 97・sdist 2・archive 2）
        Assert.Equal(97, cpu.Items.Count(i => i.Kind == LedgerItemKinds.Wheel));
        Assert.Equal(2, cpu.Items.Count(i => i.Kind == LedgerItemKinds.Sdist));
        Assert.Equal(2, cpu.Items.Count(i => i.Kind == LedgerItemKinds.Archive));
        // torch は 2 ホスト（download-r2 と download.pytorch.org）
        var torch = cpu.Items.Single(i => i.Name == "torch");
        Assert.Equal(2, torch.Urls.Count);
        Assert.NotEqual(torch.Urls[0], torch.Urls[1]);
        // silentcipher の package_dir は 1 段深い
        Assert.Equal("src/silentcipher", cpu.Items.Single(i => i.Name == "silentcipher").PackageDir);
    }

    [Fact]
    public void 実物のpython_embedとvc_redistとmodelsが読める()
    {
        var ledgerDir = RepoLedger.Directory();
        if (ledgerDir is null)
        {
            return;
        }

        var reader = new LedgerReader(ledgerDir);

        var embed = reader.ReadPythonEmbed();
        Assert.Equal("3.12.10", embed.PythonEmbed!.Version);

        var vc = reader.ReadVcRedist();
        Assert.NotNull(vc.Installer);
        Assert.Equal(3, vc.Installer!.SilentArgs.Count);

        var models = reader.ReadModels();
        Assert.Equal(3, models.Repos.Count);
        Assert.All(models.Repos, r => Assert.False(string.IsNullOrWhiteSpace(r.Revision)));
        Assert.True(models.TotalBytes > 3_000_000_000L);
    }
}

public sealed class FetchPlannerTests
{
    private static LedgerFile Embed() => LedgerReader.ParseRuntime("""
        { "schema": 1, "name": "python-embed", "count": 1, "items": [
          { "kind": "python-embed", "name": "python", "version": "3.12.10",
            "url": "https://example.invalid/python-3.12.10-embed-amd64.zip",
            "sha256": "bb", "size": 100, "license": "PSF-2.0" } ] }
        """, "python-embed");

    private static LedgerFile Runtime() => LedgerReader.ParseRuntime("""
        { "schema": 1, "name": "runtime-cpu", "count": 2, "items": [
          { "kind": "wheel", "name": "torch", "version": "2.10.0",
            "url": "https://example.invalid/torch.whl", "sha256": "cc", "size": 1000, "license": "BSD-3-Clause" },
          { "kind": "sdist", "name": "argbind", "version": "0.3.9",
            "url": "https://example.invalid/argbind.tar.gz", "sha256": "dd", "size": 10,
            "license": "MIT", "package_dirs": ["argbind"] } ] }
        """, "runtime-cpu");

    private static VcRedistLedger VcRedist() => LedgerReader.ParseVcRedist("""
        { "schema": 1, "name": "vc-redist", "items": [
          { "kind": "installer", "name": "vc_redist.x64", "version": "14.44.35211.0",
            "url": "https://example.invalid/VC_redist.x64.exe",
            "fallback_url": "https://aka.ms/vs/17/release/vc_redist.x64.exe",
            "sha256": "ee", "size": 20, "license": "Microsoft",
            "silent_args": ["/install", "/quiet", "/norestart"] } ] }
        """);

    private static ModelsLedger Models() => LedgerReader.ParseModels("""
        { "schema": 1, "name": "models", "repos": [
          { "kind": "hf-repo", "repo": "Aratako/Irodori-TTS-v4.1-Small", "revision": "2b28324dc263ed5",
            "total_bytes": 5000, "files": [ { "path": "model.safetensors", "size": 5000 } ] } ] }
        """);

    [Fact]
    public void 段の順は受け入れ条件Dの5のとおり()
    {
        var plan = FetchPlanner.Plan(RuntimeVariants.Cpu, Embed(), Runtime(), VcRedist(), Models());

        // vc_redist → python-embed → runtime → models
        Assert.Equal(
            [FetchStage.VcRedist, FetchStage.PythonEmbed, FetchStage.Runtime, FetchStage.Runtime, FetchStage.Models],
            plan.Steps.Select(s => s.Stage).ToArray());
        Assert.Equal("vc_redist.x64", plan.Steps[0].Name);
        // 台帳の並び順が保たれる（torch → argbind）
        Assert.Equal(["torch", "argbind"], plan.Stage(FetchStage.Runtime).Select(s => s.Name).ToArray());
    }

    [Fact]
    public void 容量の見積り()
    {
        var plan = FetchPlanner.Plan(RuntimeVariants.Cpu, Embed(), Runtime(), VcRedist(), Models());

        Assert.Equal(20 + 100 + 1000 + 10 + 5000, plan.TotalBytes);
        Assert.Equal(20 + 100 + 1000 + 10, plan.CacheBytes); // モデルは cache を経由しない
        Assert.Equal(5000, plan.ModelBytes);
        // 展開後＝(python-embed + runtime) × 変種ごとの実測係数（cpu＝3.60・裁定 94 ⑶）
        Assert.Equal(
            (long)((100 + 1000 + 10) * FetchPlanner.ExpansionFactorFor(RuntimeVariants.Cpu).Factor),
            plan.EstimatedRuntimeBytes);
        Assert.Equal(plan.CacheBytes + plan.EstimatedRuntimeBytes + plan.ModelBytes, plan.EstimatedPeakDiskBytes);
    }

    [Fact]
    public void 取捨で段を落とせる()
    {
        var plan = FetchPlanner.Plan(RuntimeVariants.Cpu, Embed(), Runtime(), VcRedist(), Models(),
            new FetchPlanOptions(SkipVcRedist: true, SkipModels: true));

        Assert.DoesNotContain(plan.Steps, s => s.Stage == FetchStage.VcRedist);
        Assert.DoesNotContain(plan.Steps, s => s.Stage == FetchStage.Models);
        Assert.Equal(0, plan.ModelBytes);
    }

    [Fact]
    public void 注文への変換はモデルを含まない()
    {
        var plan = FetchPlanner.Plan(RuntimeVariants.Cpu, Embed(), Runtime(), VcRedist(), Models());

        var requests = FetchPlanner.ToDownloadRequests(plan, @"C:\cache");

        Assert.Equal(4, requests.Count);
        Assert.Equal(@"C:\cache\VC_redist.x64.exe", requests[0].DestinationPath);
        Assert.Equal("https://aka.ms/vs/17/release/vc_redist.x64.exe", requests[0].FallbackUrl);
        Assert.DoesNotContain(requests, r => r.DisplayName.Contains("Irodori-TTS", StringComparison.Ordinal));
    }

    [Fact]
    public void 所要の見積り()
    {
        var plan = FetchPlanner.Plan(RuntimeVariants.Cpu, Embed(), Runtime(), VcRedist(), Models());

        Assert.Null(plan.EstimateDuration(0));
        Assert.Equal(TimeSpan.FromSeconds(613), plan.EstimateDuration(10));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KiB")]
    [InlineData(1024L * 1024, "1.0 MiB")]
    [InlineData(5_798_205_849L, "5.40 GiB")]
    public void バイト表記(long bytes, string expected) =>
        Assert.Equal(expected, FetchPlanner.FormatBytes(bytes));

    [Fact]
    public void 実物の台帳でcu130の初回取得は約5点3GiB()
    {
        var ledgerDir = RepoLedger.Directory();
        if (ledgerDir is null || !File.Exists(Path.Combine(ledgerDir, "runtime-cu130.json")))
        {
            return;
        }

        var reader = new LedgerReader(ledgerDir);
        var plan = FetchPlanner.Plan(
            RuntimeVariants.Cu130,
            reader.ReadPythonEmbed(),
            reader.ReadRuntime(RuntimeVariants.Cu130),
            reader.ReadVcRedist(),
            reader.ReadModels());

        // ledger/README.md §7 は「合計 約 5.4 GiB」と書くが、台帳の size を素直に足すと
        // 5.2585 GiB（runtime 1,944.1 MiB ＋ python-embed 10.6 MiB ＋ vc_redist 24.4 MiB
        // ＋ モデル 3,570,982,039 B）。台帳の実値のほうを釘付けする。
        var gib = plan.TotalBytes / 1024d / 1024 / 1024;
        Assert.InRange(gib, 5.24, 5.28);
    }
}
