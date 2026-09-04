using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Ledger;
using IrodoriTtsYwk.Launcher.Services.Models;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

public sealed class VcRedistDecisionTests
{
    [Fact]
    public void 在れば飛ばす()
    {
        // 裁定 54＝検出して飛ばす（版の下限はまだ決まっていない）
        var verdict = VcRedistDecision.Evaluate(
            new MsvcpState(true, "14.42.34438.0", null), "14.44.35211.0");

        Assert.Equal(VcRedistAction.Skip, verdict.Action);
        Assert.Contains("14.42.34438.0", verdict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 無ければ入れる()
    {
        var verdict = VcRedistDecision.Evaluate(new MsvcpState(false, null, null), "14.44.35211.0");

        Assert.Equal(VcRedistAction.Install, verdict.Action);
        Assert.Contains("14.44.35211.0", verdict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 読めなければ判断しない()
    {
        var verdict = VcRedistDecision.Evaluate(new MsvcpState(false, null, "アクセスが拒否されました"));

        Assert.Equal(VcRedistAction.Unknown, verdict.Action);
    }

    [Fact]
    public void 下限を渡したときだけ版で入れ直す()
    {
        Assert.Equal(VcRedistAction.Install, VcRedistDecision.Evaluate(
            new MsvcpState(true, "14.20.27508.1", null), null, "14.40.0.0").Action);
        Assert.Equal(VcRedistAction.Skip, VcRedistDecision.Evaluate(
            new MsvcpState(true, "14.42.34438.0", null), null, "14.40.0.0").Action);
        // いまの既定は「下限なし」
        Assert.Null(VcRedistDecision.MinimumFileVersion);
    }

    [Theory]
    [InlineData("14.44.35211.0", "14.40.0.0", 1)]
    [InlineData("14.40.0.0", "14.40.0.0", 0)]
    [InlineData("14.20.27508.1 (built)", "14.40.0.0", -1)]
    public void 版の比較(string left, string right, int expected) =>
        Assert.Equal(expected, Math.Sign(VcRedistDecision.TryCompare(left, right)!.Value));

    [Fact]
    public void 比べられない版はnull()
    {
        Assert.Null(VcRedistDecision.TryCompare(null, "14.0"));
        Assert.Null(VcRedistDecision.TryCompare("unknown", "14.0"));
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(1638, true, false)]
    [InlineData(3010, true, true)]
    [InlineData(1602, false, false)]
    [InlineData(5100, false, false)]
    public void 終了コードの読み(int exitCode, bool ok, bool reboot)
    {
        var result = VcRedistInstaller.Describe(VcRedistAction.Install, exitCode);

        Assert.Equal(ok, result.Ok);
        Assert.Equal(reboot, result.RebootRequired);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}

public sealed class VcRedistInstallerTests : IDisposable
{
    private readonly string _dir = TestArchives.NewTempDir("vcredist");

    public void Dispose() => TestArchives.Remove(_dir);

    private static VcRedistLedger Ledger() => LedgerReader.ParseVcRedist("""
        { "schema": 1, "name": "vc-redist", "items": [
          { "kind": "installer", "name": "vc_redist.x64", "version": "14.44.35211.0",
            "url": "https://example.invalid/VC_redist.x64.exe",
            "sha256": "ee", "size": 20, "license": "Microsoft",
            "silent_args": ["/install", "/quiet", "/norestart"] } ] }
        """);

    /// <summary>何も取らず何も走らせない downloader（飛ばす経路の検分に使う）。</summary>
    private sealed class NeverDownloader : IDownloader
    {
        public int Calls { get; private set; }

        public Task<DownloadResult> DownloadAsync(
            DownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new DownloadResult(
                true, request.DestinationPath, 20, "ee", request.Url, false, false, 1, null));
        }

        public Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(
            IReadOnlyList<DownloadRequest> requests, IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task 在る機体では1バイトも落とさない()
    {
        var dll = Path.Combine(_dir, "msvcp140.dll");
        await File.WriteAllTextAsync(dll, "not a real dll");

        var downloader = new NeverDownloader();
        var installer = new VcRedistInstaller(downloader, (_, _, _) => Task.FromResult(0))
        {
            DllPathOverride = dll,
        };

        var result = await installer.EnsureAsync(Ledger(), _dir, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(VcRedistAction.Skip, result.Action);
        Assert.Equal(0, downloader.Calls);
    }

    [Fact]
    public async Task 無い機体では台帳のsilent_argsで走らせる()
    {
        IReadOnlyList<string>? seen = null;
        var installer = new VcRedistInstaller(
            new NeverDownloader(),
            (_, args, _) =>
            {
                seen = args;
                return Task.FromResult(0);
            })
        {
            DllPathOverride = Path.Combine(_dir, "absent.dll"),
        };

        var result = await installer.EnsureAsync(Ledger(), _dir, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(VcRedistAction.Install, result.Action);
        // 台帳の逐語をそのまま渡す（設計書 §6 の /passive とは食い違っている＝卓へ）
        Assert.Equal(["/install", "/quiet", "/norestart"], seen);
    }

    [Fact]
    public async Task 引数の上書きが効く()
    {
        IReadOnlyList<string>? seen = null;
        var installer = new VcRedistInstaller(
            new NeverDownloader(),
            (_, args, _) =>
            {
                seen = args;
                return Task.FromResult(0);
            })
        {
            DllPathOverride = Path.Combine(_dir, "absent.dll"),
            SilentArgsOverride = ["/install", "/passive", "/norestart"],
        };

        await installer.EnsureAsync(Ledger(), _dir, null, CancellationToken.None);

        Assert.Equal(["/install", "/passive", "/norestart"], seen);
    }
}

public sealed class ModelFetchEventTests
{
    [Fact]
    public void 計画の行を読む()
    {
        var e = ModelFetchEvent.Parse(
            """{"event": "plan", "total_files": 22, "total_bytes": 3570982039, "dest": "C:\\models", "check_only": false}""");

        Assert.NotNull(e);
        Assert.Equal(ModelFetchEvents.Plan, e!.Event);
        Assert.Equal(22, e.TotalFiles);
        Assert.Equal(3_570_982_039L, e.TotalBytes);
        Assert.Contains("22 檔", e.ForUi(), StringComparison.Ordinal);
    }

    [Fact]
    public void 進捗の行から全体の割合が出る()
    {
        var e = ModelFetchEvent.Parse(
            """{"event": "progress", "repo": "Aratako/Irodori-TTS-v4.1-Small", "path": "model.safetensors", "downloaded": 500, "bytes": 1000, "overall_downloaded": 500, "overall_bytes": 2000}""");

        Assert.NotNull(e);
        Assert.Equal(0.25, e!.Fraction);
        Assert.Equal(500, e.Downloaded);
    }

    [Fact]
    public void 済みと不良の行を読む()
    {
        var done = ModelFetchEvent.Parse(
            """{"event": "file_done", "repo": "sony/silentcipher", "path": "44_1khz/73999_iteration", "verified": "git-blob-sha1", "bytes": 65}""");
        Assert.Equal("git-blob-sha1", done!.Verified);

        var bad = ModelFetchEvent.Parse(
            """{"event": "file_bad", "repo": "r", "path": "p", "reason": "sha256 mismatch"}""");
        Assert.Contains("sha256 mismatch", bad!.ForUi(), StringComparison.Ordinal);
    }

    [Fact]
    public void 失敗つきのdoneを読む()
    {
        var e = ModelFetchEvent.Parse(
            """{"event": "done", "ok": false, "failures": ["r:p is not in the cache"], "bytes": 12}""");

        Assert.False(e!.Ok);
        Assert.Single(e.Failures);
        Assert.Equal(12, e.Bytes);
    }

    [Fact]
    public void 成功のdoneはfilesを名乗る()
    {
        var e = ModelFetchEvent.Parse("""{"event": "done", "ok": true, "files": 22, "bytes": 3570982039, "dest": "C:\\models"}""");

        Assert.True(e!.Ok);
        Assert.Equal(22, e.Files);
        Assert.Null(e.TotalFiles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ywk_fetch_models finished in 3.2s with exit code 0")]
    [InlineData("{ not json")]
    [InlineData("""{"no_event": 1}""")]
    public void JSONでない行はnull(string? line) => Assert.Null(ModelFetchEvent.Parse(line));

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(9, false)]
    public void 終了コードの理由1行(int exitCode, bool ok)
    {
        var text = ModelFetchExitCodes.Describe(exitCode, "詳細");

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Equal(ok, exitCode == ModelFetchExitCodes.Ok);
        if (!ok)
        {
            Assert.Contains("詳細", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 子プロセスの引数()
    {
        var arguments = ModelFetcher.BuildArguments(
            @"C:\app\server\ywk_fetch_models.py", @"C:\app\ledger\models.json", @"C:\data\models",
            checkOnly: true, force: false);

        Assert.Equal(
            [@"C:\app\server\ywk_fetch_models.py", "--ledger", @"C:\app\ledger\models.json",
                "--dest", @"C:\data\models", "--check-only"],
            arguments);
    }

    [Fact]
    public void 子プロセスのenvはオフラインを解く()
    {
        // 起動時（ServerEnvironment）は HF_HUB_OFFLINE=1。取りに行くここは逆。
        var env = ModelFetcher.BuildEnvironment(@"C:\data\models");

        Assert.Equal(@"C:\data\models", env["HF_HOME"]);
        Assert.Equal("0", env["HF_HUB_OFFLINE"]);
        Assert.Equal("1", env["PYTHONUNBUFFERED"]);
        Assert.Equal(string.Empty, env["PYTHONPATH"]);
    }

    [Fact]
    public async Task 台本が無ければ理由1行で返る()
    {
        var dir = TestArchives.NewTempDir("fetch");
        try
        {
            var fetcher = new ModelFetcher(
                Path.Combine(dir, "python.exe"), dir, Path.Combine(dir, "models.json"),
                Path.Combine(dir, "models"));

            var result = await fetcher.FetchAsync(null, CancellationToken.None);

            Assert.False(result.Ok);
            Assert.Equal(ModelFetchExitCodes.BadArguments, result.ExitCode);
            Assert.Contains(ModelFetcher.ScriptName, result.Message, StringComparison.Ordinal);
        }
        finally
        {
            TestArchives.Remove(dir);
        }
    }

    [Fact]
    public void 実物の台本の名前が配布樹に在る()
    {
        var repo = RepoLedger.RepoRoot();
        if (repo is null)
        {
            return;
        }

        Assert.True(File.Exists(Path.Combine(repo, "server", ModelFetcher.ScriptName)));
    }
}
