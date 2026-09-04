using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Ledger;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 取得の規則を<b>実際の HTTP</b>（ローカルの <see cref="HttpListener"/>）で釘付けする。
/// 外へは 1 バイトも出ない。
/// </summary>
public sealed class DownloaderTests : IDisposable
{
    private readonly string _dir = TestArchives.NewTempDir("download");

    public void Dispose() => TestArchives.Remove(_dir);

    private static HttpDownloader NewDownloader() =>
        new(HttpDownloader.CreateDefaultClient(), ownsClient: true)
        {
            ProgressInterval = TimeSpan.Zero,
        };

    [Fact]
    public async Task 落として検証して原子的に着地する()
    {
        var body = TestHttpServer.Payload(64 * 1024, 7);
        using var server = new TestHttpServer();
        server.MapBytes("wheel.whl", body);

        using var downloader = NewDownloader();
        var destination = Path.Combine(_dir, "wheel.whl");
        var progress = new List<DownloadProgress>();

        var result = await downloader.DownloadAsync(
            new DownloadRequest(server.Url("wheel.whl"), null, destination,
                TestArchives.Sha256Of(body), body.Length, "wheel"),
            new Progress<DownloadProgress>(progress.Add),
            CancellationToken.None);

        Assert.True(result.Ok, result.FailureReason);
        Assert.Equal(body.Length, result.Bytes);
        Assert.Equal(TestArchives.Sha256Of(body), result.ActualSha256);
        Assert.False(result.FromCache);
        Assert.Equal(body, await File.ReadAllBytesAsync(destination));
        // .part は残らない
        Assert.False(File.Exists(destination + ".part"));
    }

    [Fact]
    public async Task 検証済みの檔は1バイトも落とさない()
    {
        var body = TestHttpServer.Payload(1024, 3);
        using var server = new TestHttpServer();
        server.MapBytes("wheel.whl", body);

        var destination = Path.Combine(_dir, "wheel.whl");
        await File.WriteAllBytesAsync(destination, body);

        using var downloader = NewDownloader();
        var result = await downloader.DownloadAsync(
            new DownloadRequest(server.Url("wheel.whl"), null, destination,
                TestArchives.Sha256Of(body), body.Length, "wheel"),
            null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.True(result.FromCache);
        Assert.Equal(0, server.Hits("wheel.whl"));
    }

    [Fact]
    public async Task 途中で切れたらRangeで続きから取る()
    {
        var body = TestHttpServer.Payload(256 * 1024, 11);
        using var server = new TestHttpServer();
        // 1 回目は先頭 64 KiB で切る → 2 回目は Range で続きを返す
        server.MapTruncatedThenWhole("torch.whl", body, cut: 64 * 1024, truncatedAttempts: 1);

        using var downloader = NewDownloader();
        var destination = Path.Combine(_dir, "torch.whl");

        var result = await downloader.DownloadAsync(
            new DownloadRequest(server.Url("torch.whl"), null, destination,
                TestArchives.Sha256Of(body), body.Length, "torch"),
            null, CancellationToken.None);

        Assert.True(result.Ok, result.FailureReason);
        Assert.True(result.Resumed);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, server.Hits("torch.whl"));
        Assert.Equal(body, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task sha256が合わなければ破棄して取り直す()
    {
        var good = TestHttpServer.Payload(4096, 1);
        var bad = TestHttpServer.Payload(4096, 2); // 同じ長さ・違う中身
        using var server = new TestHttpServer();
        server.MapBadThenGood("numpy.whl", bad, good, badAttempts: 2);

        using var downloader = NewDownloader();
        var destination = Path.Combine(_dir, "numpy.whl");

        var result = await downloader.DownloadAsync(
            new DownloadRequest(server.Url("numpy.whl"), null, destination,
                TestArchives.Sha256Of(good), good.Length, "numpy"),
            null, CancellationToken.None);

        Assert.True(result.Ok, result.FailureReason);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(good, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task sha256が最後まで合わなければ失敗して本名を作らない()
    {
        var good = TestHttpServer.Payload(4096, 1);
        var bad = TestHttpServer.Payload(4096, 2);
        using var server = new TestHttpServer();
        server.MapBytes("numpy.whl", bad);

        using var downloader = NewDownloader();
        var destination = Path.Combine(_dir, "numpy.whl");

        var result = await downloader.DownloadAsync(
            new DownloadRequest(server.Url("numpy.whl"), null, destination,
                TestArchives.Sha256Of(good), good.Length, "numpy", MaxAttempts: 3),
            null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(3, result.Attempts);
        Assert.False(File.Exists(destination));
        Assert.Contains("sha256", result.FailureReason!, StringComparison.Ordinal);
        // 理由 1 行に絶対パスを出さない（ログ規律）
        Assert.DoesNotContain(_dir, result.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task urlで落ちたらfallback_urlを試す()
    {
        var body = TestHttpServer.Payload(2048, 5);
        using var server = new TestHttpServer();
        server.MapStatus("r2/torch.whl", HttpStatusCode.NotFound);
        server.MapBytes("canonical/torch.whl", body);

        using var downloader = NewDownloader();
        var destination = Path.Combine(_dir, "torch.whl");

        var result = await downloader.DownloadAsync(
            new DownloadRequest(server.Url("r2/torch.whl"), server.Url("canonical/torch.whl"),
                destination, TestArchives.Sha256Of(body), body.Length, "torch"),
            null, CancellationToken.None);

        Assert.True(result.Ok, result.FailureReason);
        Assert.Equal(server.Url("canonical/torch.whl"), result.UsedUrl);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task 途中で落ちたurlの続きをfallbackが正しい位置から取る()
    {
        // 所見 7 の釘＝offset を「試行ごとに 1 回」しか読まないと、1 本目が途中まで書いて
        // 落ちた後、2 本目（fallback_url）が**古い offset** で Range を頼み、
        // FileMode.Append で伸びた檔末に書き足す＝.part が壊れる（実射＝103,985 B 目から
        // 実物と食い違った）。offset は **url ごとに読み直す**。
        var body = TestHttpServer.Payload(400_000, 11);
        using var server = new TestHttpServer();

        // 1 本目＝毎回 4,000 B で切る（＝1 回も最後まで返さない）
        server.MapTruncatedThenWhole("mirror/torch.whl", body, cut: 4_000, truncatedAttempts: int.MaxValue);
        // 2 本目＝Range も 200 も正しく返す
        server.MapBytes("canonical/torch.whl", body);

        using var downloader = NewDownloader();
        var destination = Path.Combine(_dir, "torch.whl");

        var result = await downloader.DownloadAsync(
            new DownloadRequest(
                server.Url("mirror/torch.whl"),
                server.Url("canonical/torch.whl"),
                destination,
                TestArchives.Sha256Of(body),
                body.Length,
                "torch",
                MaxAttempts: 5),
            null,
            CancellationToken.None);

        Assert.True(result.Ok, result.FailureReason);
        Assert.Equal(server.Url("canonical/torch.whl"), result.UsedUrl);

        // **1 バイトも食い違わない**（壊れた .part を着地させない）
        Assert.Equal(body, await File.ReadAllBytesAsync(destination));
        Assert.False(File.Exists(destination + ".part"));
    }

    [Fact]
    public async Task 試行1回でもfallbackが続きを取れる()
    {
        // 所見 7 の逐語 D1＝fallback が全部きちんと返し Range も通ったのに、
        // 古い offset のせいで「長さが違う」で失敗し、落ちていた分まで消えていた。
        var body = TestHttpServer.Payload(200_000, 23);
        using var server = new TestHttpServer();
        server.MapTruncatedThenWhole("mirror/torch.whl", body, cut: 50_000, truncatedAttempts: int.MaxValue);
        server.MapBytes("canonical/torch.whl", body);

        using var downloader = NewDownloader();
        var destination = Path.Combine(_dir, "torch.whl");

        var result = await downloader.DownloadAsync(
            new DownloadRequest(
                server.Url("mirror/torch.whl"),
                server.Url("canonical/torch.whl"),
                destination,
                TestArchives.Sha256Of(body),
                body.Length,
                "torch",
                MaxAttempts: 1),
            null,
            CancellationToken.None);

        Assert.True(result.Ok, result.FailureReason);
        Assert.True(result.Resumed);
        Assert.Equal(body, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task 満杯の途中檔は捨てて0から取り直す()
    {
        // 期待長と同じ長さの .part が残っていると "Range: bytes=<len>-" は永遠に 416 になる。
        var body = TestHttpServer.Payload(8192, 9);
        using var server = new TestHttpServer();
        server.MapBytes("torch.whl", body);

        var destination = Path.Combine(_dir, "torch.whl");
        await File.WriteAllBytesAsync(destination + ".part", TestHttpServer.Payload(8192, 42));

        using var downloader = NewDownloader();
        var result = await downloader.DownloadAsync(
            new DownloadRequest(server.Url("torch.whl"), null, destination,
                TestArchives.Sha256Of(body), body.Length, "torch"),
            null, CancellationToken.None);

        Assert.True(result.Ok, result.FailureReason);
        Assert.False(result.Resumed);
        Assert.Equal(body, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task Rangeをサーバが無視して200で返したら先頭から書き直す()
    {
        var body = TestHttpServer.Payload(8192, 13);
        using var server = new TestHttpServer();
        server.MapIgnoringRange("torch.whl", body);

        var destination = Path.Combine(_dir, "torch.whl");
        // 中途半端な .part を置く（Range を頼む条件を作る）
        await File.WriteAllBytesAsync(destination + ".part", new byte[1024]);

        using var downloader = NewDownloader();
        var result = await downloader.DownloadAsync(
            new DownloadRequest(server.Url("torch.whl"), null, destination,
                TestArchives.Sha256Of(body), body.Length, "torch"),
            null, CancellationToken.None);

        Assert.True(result.Ok, result.FailureReason);
        Assert.False(result.Resumed);
        Assert.Equal(body, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task 台帳の順に取り並列は2本まで()
    {
        using var server = new TestHttpServer();
        var requests = new List<DownloadRequest>();
        for (var i = 0; i < 6; i++)
        {
            var body = TestHttpServer.Payload(1024 + i, (byte)i);
            var name = "pkg" + i + ".whl";
            server.MapBytes(name, body);
            requests.Add(new DownloadRequest(
                server.Url(name), null, Path.Combine(_dir, name),
                TestArchives.Sha256Of(body), body.Length, "pkg" + i));
        }

        using var downloader = NewDownloader();
        var results = await downloader.DownloadAllAsync(requests, null, CancellationToken.None);

        Assert.Equal(6, results.Count);
        Assert.All(results, r => Assert.True(r.Ok, r.FailureReason));
        Assert.Equal(2, HttpDownloader.MaxParallel);
    }

    [Fact]
    public async Task どれか1件でも落ちたら後の取得を始めない()
    {
        using var server = new TestHttpServer();
        var body = TestHttpServer.Payload(512, 4);
        server.MapBytes("ok.whl", body);
        server.MapStatus("bad.whl", HttpStatusCode.InternalServerError);
        server.MapBytes("later.whl", body);

        var requests = new List<DownloadRequest>
        {
            new(server.Url("bad.whl"), null, Path.Combine(_dir, "bad.whl"),
                TestArchives.Sha256Of(body), body.Length, "bad", MaxAttempts: 1),
            new(server.Url("ok.whl"), null, Path.Combine(_dir, "ok.whl"),
                TestArchives.Sha256Of(body), body.Length, "ok"),
            new(server.Url("later.whl"), null, Path.Combine(_dir, "later.whl"),
                TestArchives.Sha256Of(body), body.Length, "later"),
        };

        using var downloader = NewDownloader();
        var results = await downloader.DownloadAllAsync(requests, null, CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.False(results[0].Ok);
        // 3 件目は始まってすらいない（先に落ちたので取りやめ）
        Assert.False(results[2].Ok);
        Assert.Equal(0, server.Hits("later.whl"));
    }

    [Fact]
    public async Task 取消は例外で抜ける()
    {
        var body = TestHttpServer.Payload(4096, 6);
        using var server = new TestHttpServer();
        server.MapBytes("slow.whl", body);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        using var downloader = NewDownloader();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloader.DownloadAsync(
            new DownloadRequest(server.Url("slow.whl"), null, Path.Combine(_dir, "slow.whl"),
                TestArchives.Sha256Of(body), body.Length, "slow"),
            null, cts.Token));
    }

    [Fact]
    public async Task 進捗はbytesとETAを持つ()
    {
        var body = TestHttpServer.Payload(512 * 1024, 21);
        using var server = new TestHttpServer();
        server.MapBytes("big.whl", body);

        var seen = new List<DownloadProgress>();
        using var downloader = NewDownloader();
        var result = await downloader.DownloadAsync(
            new DownloadRequest(server.Url("big.whl"), null, Path.Combine(_dir, "big.whl"),
                TestArchives.Sha256Of(body), body.Length, "big"),
            new Progress<DownloadProgress>(seen.Add),
            CancellationToken.None);

        Assert.True(result.Ok, result.FailureReason);
        // Progress<T> は SynchronizationContext 越しに来るので、少し待って数える
        for (var i = 0; i < 50 && !seen.Any(p => p.Phase == DownloadPhase.Done); i++)
        {
            await Task.Delay(20);
        }

        Assert.Contains(seen, p => p.Phase == DownloadPhase.Downloading);
        Assert.Contains(seen, p => p.Phase == DownloadPhase.Verifying);
        Assert.Contains(seen, p => p.Phase == DownloadPhase.Done);
        Assert.All(seen, p => Assert.Equal("big", p.DisplayName));
    }
}
