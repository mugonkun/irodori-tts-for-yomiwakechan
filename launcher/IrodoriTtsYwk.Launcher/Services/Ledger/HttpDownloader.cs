using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Ledger;

/// <summary>
/// 契約 ⑵ の実装。<c>build/Common.ps1</c> の <c>Invoke-YwkDownload</c> と<b>同じ規則</b>で取る。
/// <list type="number">
/// <item><b>原子的着地</b>＝<c>&lt;檔&gt;.part</c> に書き、sha256 が合ってから 1 手で本名にする。</item>
/// <item><b>Range で再開</b>＝残っている <c>.part</c> の長さから続ける。ただし
/// ⒜ 既に期待長以上、⒝ 前回の試行で 1 バイトも増えなかった、⒞ サーバが 416 を返した、
/// のいずれかなら捨てて 0 から取り直す（<b>満杯の <c>.part</c> は永遠に 416 になる</b>＝
/// <c>Common.ps1</c> の 480〜497 行が同じ穴を塞いでいる）。</item>
/// <item><b>sha256 不一致は破棄して取り直す</b>（最大 <see cref="DownloadRequest.MaxAttempts"/> 回）。</item>
/// <item><b>url で落ちたら fallback_url</b>（<c>ledger/README.md</c> §3＝CDN の別名が畳まれた日に
/// 全利用者の初回取得が 404 で死ぬのを防ぐ 1 行）。同じ檔なので sha256 の検証は変わらない。</item>
/// </list>
/// <para>継ぎ目は public コンストラクタ（<see cref="HttpClient"/> を差せる＝テストは HttpListener を立てる）。</para>
/// </summary>
public sealed class HttpDownloader : IDownloader, IDisposable
{
    /// <summary>並列の上限（設計書 §6＝2 本まで）。</summary>
    public const int MaxParallel = 2;

    /// <summary>ストリームの読み書きの単位（<c>Common.ps1</c> と同じ 1 MiB）。</summary>
    private const int BufferSize = 1024 * 1024;

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private bool _disposed;

    /// <summary>既定の <see cref="HttpClient"/>（リダイレクト追従・無圧縮・長い読み待ち）で作る。</summary>
    public HttpDownloader() : this(CreateDefaultClient(), ownsClient: true)
    {
    }

    /// <summary>テストの継ぎ目＝叩き先を差し替えた <see cref="HttpClient"/> を渡す。</summary>
    /// <param name="client">使う <see cref="HttpClient"/>。</param>
    /// <param name="ownsClient"><see cref="Dispose"/> でこの client も捨てるか。</param>
    public HttpDownloader(HttpClient client, bool ownsClient = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _ownsClient = ownsClient;
    }

    /// <summary>進捗を出す間隔（既定 200 ms）。</summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>既定の client（<c>Common.ps1</c> の <c>UserAgent</c> と読み待ちを倣う）。</summary>
    public static HttpClient CreateDefaultClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            // 落とすのは wheel と exe＝既に圧縮済み。自動展開は Content-Length と長さの突合を壊す。
            AutomaticDecompression = DecompressionMethods.None,
        };

        var client = new HttpClient(handler)
        {
            // 本文の読みは自前の CancellationToken で見る（1 檔 1.8 GB を落とす）
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("irodori-tts-ywk-launcher/1");
        return client;
    }

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var want = string.IsNullOrWhiteSpace(request.Sha256)
            ? null
            : request.Sha256.Trim().ToLowerInvariant();

        var directory = Path.GetDirectoryName(request.DestinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // ⑴ 既に在る＝検証済みなら 1 バイトも落とさない（cache/ の再利用）。
        if (File.Exists(request.DestinationPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, request, DownloadPhase.Verifying, 0, null, 0, 1, null);
            var have = await Sha256OfAsync(request.DestinationPath, cancellationToken).ConfigureAwait(false);
            var length = new FileInfo(request.DestinationPath).Length;
            if (want is null || string.Equals(have, want, StringComparison.Ordinal))
            {
                Report(progress, request, DownloadPhase.CacheHit, length, length, 0, 1, null);
                return new DownloadResult(true, request.DestinationPath, length, have, null, false, true, 0, null);
            }

            SafeDelete(request.DestinationPath);
        }

        var part = request.DestinationPath + ".part";
        var urls = new List<string> { request.Url };
        if (!string.IsNullOrWhiteSpace(request.FallbackUrl))
        {
            urls.Add(request.FallbackUrl);
        }

        var attempts = Math.Max(1, request.MaxAttempts);

        // **1 本の url ごとに「前回どこまで書けたか」を持つ**（是正・2026-09-05）。
        // offset を試行ごとに 1 回しか読まないと、1 本目の url が途中まで書いて落ちた後、
        // 2 本目（fallback_url）が**古い offset** で Range を頼み、FileMode.Append で伸びた
        // 檔末に書き足す＝.part が壊れる（実射＝103,985 B 目から実物と食い違った）。
        // 参照実装 build/Common.ps1 の Invoke-YwkDownload は url を 1 本しか取らず、
        // Get-YwkCachedItem が url ごとに入り直すので offset を必ず読み直している。
        var lastOffsets = new Dictionary<string, long>(StringComparer.Ordinal);
        string? lastFailure = null;
        var resumed = false;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var url in urls)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // **檔の実長をここで読み直す**（1 つ前の url が書いた分を必ず見る）。
                var offset = File.Exists(part) ? new FileInfo(part).Length : 0L;
                if (offset > 0)
                {
                    // 満杯の .part / 進まない .part は捨てる（Common.ps1 と同じ 2 つの罠）。
                    var restart =
                        request.ExpectedSize is > 0 && offset >= request.ExpectedSize.Value
                            ? "残っていた途中檔が期待より長い"
                            : lastOffsets.TryGetValue(url, out var seen) && offset == seen
                                ? "前回の試行で 1 バイトも増えなかった"
                                : null;
                    if (restart is not null)
                    {
                        SafeDelete(part);
                        offset = 0;
                    }
                }

                lastOffsets[url] = offset;

                try
                {
                    var body = await FetchBodyAsync(
                        url, part, offset, request, progress, attempt, cancellationToken).ConfigureAwait(false);
                    resumed |= body.Resumed;

                    var size = new FileInfo(part).Length;
                    if (request.ExpectedSize is > 0 && size != request.ExpectedSize.Value)
                    {
                        // 長さ違いの .part を残すと、次の試行が「サーバが拒む範囲」を頼んで 416 で詰まる。
                        SafeDelete(part);
                        throw new IOException(string.Create(CultureInfo.InvariantCulture,
                            $"長さが違う（{size} B・台帳は {request.ExpectedSize.Value} B）"));
                    }

                    Report(progress, request, DownloadPhase.Verifying, size, request.ExpectedSize, 0, attempt, url);
                    var got = await Sha256OfAsync(part, cancellationToken).ConfigureAwait(false);
                    if (want is not null && !string.Equals(got, want, StringComparison.Ordinal))
                    {
                        SafeDelete(part);
                        throw new IOException("sha256 が台帳と合わない");
                    }

                    Report(progress, request, DownloadPhase.Committing, size, request.ExpectedSize, 0, attempt, url);
                    File.Move(part, request.DestinationPath, overwrite: true);
                    Report(progress, request, DownloadPhase.Done, size, size, 0, attempt, url);

                    return new DownloadResult(
                        true, request.DestinationPath, size, got, url, resumed, false, attempt, null);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or WebException
                                               or TaskCanceledException or UnauthorizedAccessException)
                {
                    lastFailure = Describe(request.DisplayName, url, ex);
                }
            }
        }

        Report(progress, request, DownloadPhase.Failed, 0, request.ExpectedSize, 0, attempts, null);
        return new DownloadResult(
            false,
            request.DestinationPath,
            File.Exists(part) ? new FileInfo(part).Length : 0,
            null,
            null,
            resumed,
            false,
            attempts,
            lastFailure ?? (request.DisplayName + " を取得できなかった。"));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(
        IReadOnlyList<DownloadRequest> requests,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var results = new DownloadResult?[requests.Count];
        using var gate = new SemaphoreSlim(MaxParallel, MaxParallel);
        var running = new List<Task>(requests.Count);
        var failed = 0;

        for (var i = 0; i < requests.Count; i++)
        {
            if (Volatile.Read(ref failed) != 0)
            {
                break;
            }

            // 台帳の並び順に「始める」＝2 本までしか同時に走らない。
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (Volatile.Read(ref failed) != 0)
            {
                gate.Release();
                break;
            }

            var index = i;
            running.Add(Task.Run(async () =>
            {
                try
                {
                    var result = await DownloadAsync(requests[index], progress, cancellationToken)
                        .ConfigureAwait(false);
                    results[index] = result;
                    if (!result.Ok)
                    {
                        Interlocked.Exchange(ref failed, 1);
                    }
                }
                finally
                {
                    gate.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(running).ConfigureAwait(false);

        var final = new DownloadResult[requests.Count];
        for (var i = 0; i < requests.Count; i++)
        {
            final[i] = results[i] ?? new DownloadResult(
                false, requests[i].DestinationPath, 0, null, null, false, false, 0,
                "前の取得が失敗したので取りやめた。");
        }

        return final;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    // ---- 本体 ---------------------------------------------------------------

    private async Task<(bool Resumed, long Bytes)> FetchBodyAsync(
        string url,
        string part,
        long offset,
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        int attempt,
        CancellationToken cancellationToken)
    {
        Report(progress, request, DownloadPhase.Connecting, offset, request.ExpectedSize, 0, attempt, url);

        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        if (offset > 0)
        {
            message.Headers.Range = new RangeHeaderValue(offset, null);
        }

        using var response = await _client
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (offset > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // 416＝サーバがこの範囲を拒んだ。残骸を捨てて 0 から取り直させる。
            SafeDelete(part);
            throw new IOException("途中檔の続きをサーバが拒んだ（416）ので取り直す");
        }

        response.EnsureSuccessStatusCode();

        var resumed = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        var start = resumed ? offset : 0;
        if (offset > 0 && !resumed)
        {
            // Range を無視して 200 で返してきた＝先頭から書き直す。
            SafeDelete(part);
        }

        var total = request.ExpectedSize
            ?? (response.Content.Headers.ContentLength is { } length ? start + length : null);

        Report(progress, request, DownloadPhase.Downloading, start, total, 0, attempt, url);

        var mode = resumed ? FileMode.Append : FileMode.Create;
        await using var file = new FileStream(
            part, mode, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[BufferSize];
        var received = start;
        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        var lastReportedBytes = start;

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;

            var now = clock.Elapsed;
            if (progress is not null && now - lastReport >= ProgressInterval)
            {
                var seconds = (now - lastReport).TotalSeconds;
                var speed = seconds > 0 ? (received - lastReportedBytes) / seconds : 0;
                Report(progress, request, DownloadPhase.Downloading, received, total, speed, attempt, url);
                lastReport = now;
                lastReportedBytes = received;
            }
        }

        await file.FlushAsync(cancellationToken).ConfigureAwait(false);
        return (resumed, received);
    }

    /// <summary>檔全体の sha256（小文字 hex）をストリームで計算する。</summary>
    public static async Task<string> Sha256OfAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static void Report(
        IProgress<DownloadProgress>? progress,
        DownloadRequest request,
        DownloadPhase phase,
        long received,
        long? total,
        double bytesPerSecond,
        int attempt,
        string? url)
    {
        progress?.Report(new DownloadProgress(
            request.DisplayName,
            phase,
            received,
            total,
            bytesPerSecond,
            DownloadProgress.EstimateEta(received, total, bytesPerSecond),
            attempt,
            url));
    }

    private static void SafeDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 消せなくても次の試行が上書きする。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>理由 1 行（<b>絶対パスを出さない</b>＝ログ規律。ホスト名までは出す）。</summary>
    private static string Describe(string name, string url, Exception ex)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "(不明なホスト)";
        var detail = ex switch
        {
            HttpRequestException http when http.StatusCode is { } code =>
                string.Create(CultureInfo.InvariantCulture, $"HTTP {(int)code}"),
            _ => ex.Message,
        };
        return name + " を " + host + " から取れなかった：" + detail;
    }
}
