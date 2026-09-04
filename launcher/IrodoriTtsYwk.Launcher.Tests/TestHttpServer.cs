using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// テスト用のローカル HTTP サーバ（<see cref="HttpListener"/>）。
/// <c>http://localhost:&lt;港&gt;/</c> の前置は Windows では管理者権限なしで開ける。
/// <para>
/// 檔ごとに「何度目の要求で何を返すか」を決められるようにしてあり、
/// Range の再開・sha256 不一致の取り直し・fallback_url への落ちを実際の HTTP で釘付けする。
/// </para>
/// </summary>
internal sealed class TestHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, Func<int, HttpListenerContext, Task>> _routes = new();
    private readonly ConcurrentDictionary<string, int> _hits = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public TestHttpServer()
    {
        Port = FreePort();
        Prefix = "http://localhost:" + Port.ToString(CultureInfo.InvariantCulture) + "/";
        _listener.Prefixes.Add(Prefix);
        _listener.Start();
        _loop = Task.Run(LoopAsync);
    }

    public int Port { get; }

    public string Prefix { get; }

    public string Url(string path) => Prefix + path.TrimStart('/');

    /// <summary>その path が何回叩かれたか。</summary>
    public int Hits(string path) => _hits.TryGetValue("/" + path.TrimStart('/'), out var n) ? n : 0;

    /// <summary>path に応答を割り当てる（第 1 引数＝その path の何回目の要求か・1 起点）。</summary>
    public void Map(string path, Func<int, HttpListenerContext, Task> handler) =>
        _routes["/" + path.TrimStart('/')] = handler;

    /// <summary>本文をそのまま返す（Range 対応）。</summary>
    public void MapBytes(string path, byte[] body) =>
        Map(path, (_, context) => WriteAsync(context, body, body.Length, abortAfter: null));

    /// <summary>
    /// 最初の <paramref name="truncatedAttempts"/> 回は先頭 <paramref name="cut"/> バイトだけ返して
    /// 接続を切る（＝途中で落ちた取得）。以降は最後まで返す。
    /// </summary>
    public void MapTruncatedThenWhole(string path, byte[] body, int cut, int truncatedAttempts) =>
        Map(path, (attempt, context) => attempt <= truncatedAttempts
            ? WriteAsync(context, body, body.Length, abortAfter: cut)
            : WriteAsync(context, body, body.Length, abortAfter: null));

    /// <summary>
    /// 最初の <paramref name="badAttempts"/> 回は<b>別の中身</b>（＝sha256 が合わない）を返す。
    /// </summary>
    public void MapBadThenGood(string path, byte[] bad, byte[] good, int badAttempts) =>
        Map(path, (attempt, context) => attempt <= badAttempts
            ? WriteAsync(context, bad, bad.Length, abortAfter: null)
            : WriteAsync(context, good, good.Length, abortAfter: null));

    /// <summary>常にその状態コードを返す（404 で fallback に落ちるのを見る）。</summary>
    public void MapStatus(string path, HttpStatusCode code) => Map(path, (_, context) =>
    {
        context.Response.StatusCode = (int)code;
        context.Response.Close();
        return Task.CompletedTask;
    });

    /// <summary>Range を無視して常に 200 で全部返す（サーバが再開を拒む機体）。</summary>
    public void MapIgnoringRange(string path, byte[] body) => Map(path, (_, context) =>
    {
        context.Response.StatusCode = 200;
        context.Response.ContentLength64 = body.Length;
        context.Response.OutputStream.Write(body, 0, body.Length);
        context.Response.Close();
        return Task.CompletedTask;
    });

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _loop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }

        _stop.Dispose();
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            var attempt = _hits.AddOrUpdate(path, 1, (_, n) => n + 1);

            if (!_routes.TryGetValue(path, out var handler))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                continue;
            }

            try
            {
                await handler(attempt, context).ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.IO.IOException)
            {
            }
        }
    }

    /// <summary>Range を見て 200／206／416 を返し、途中で切ることもできる。</summary>
    private static Task WriteAsync(HttpListenerContext context, byte[] body, int total, int? abortAfter)
    {
        var offset = 0;
        var range = context.Request.Headers["Range"];
        if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes=", StringComparison.Ordinal))
        {
            var spec = range["bytes=".Length..];
            var dash = spec.IndexOf('-');
            var head = dash >= 0 ? spec[..dash] : spec;
            if (int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                offset = parsed;
            }

            if (offset >= total)
            {
                context.Response.StatusCode = 416;
                context.Response.Close();
                return Task.CompletedTask;
            }

            context.Response.StatusCode = 206;
            context.Response.Headers["Content-Range"] = string.Create(CultureInfo.InvariantCulture,
                $"bytes {offset}-{total - 1}/{total}");
        }
        else
        {
            context.Response.StatusCode = 200;
        }

        var length = total - offset;
        context.Response.ContentLength64 = length;

        var write = abortAfter is { } cut ? Math.Min(cut, length) : length;
        context.Response.OutputStream.Write(body, offset, write);
        context.Response.OutputStream.Flush();

        if (abortAfter is not null)
        {
            // ContentLength64 より短く書いて切る＝client 側は途中で切れた本文として見る
            context.Response.Abort();
        }
        else
        {
            context.Response.Close();
        }

        return Task.CompletedTask;
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>再現可能な中身（sha256 を先に計算しておくため）。</summary>
    public static byte[] Payload(int length, byte seed)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)((i * 31 + seed) & 0xFF);
        }

        return bytes;
    }
}
