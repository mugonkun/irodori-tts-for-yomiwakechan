using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Http;

/// <summary>
/// 契約 ⑸ の実装（<c>docs/contract.md</c> の口）。
/// <para>
/// <b>テストの継ぎ目は public コンストラクタ</b>＝<see cref="HttpMessageHandler"/> を差せる
/// （<c>InternalsVisibleTo</c> は使わない＝本体の流儀）。実機・実ポートなしで
/// 応答の読みを釘付けできる。
/// </para>
/// <para>
/// <b>どのメソッドも例外を投げない</b>（取消を除く）＝<see cref="WrapperResult{T}"/> で返し、
/// <b>口が無い（404／405）</b>ことと落ちたことを区別する。欄が無い応答でも落ちない
/// （すべて null 許容の record＝<c>Contracts/WrapperResponses.cs</c>）。
/// </para>
/// <para>
/// 接続先は <c>127.0.0.1</c> の 1 本（<c>localhost</c> は IPv6 を先に試して毎要求 +1.2〜2.0 s＝契約 ⑴）。
/// <c>Authorization</c> は付けない。
/// </para>
/// </summary>
public sealed class WrapperClient : IWrapperClient
{
    /// <summary>1 要求の既定の期限（契約 ⑵＝5 秒。合成だけは呼ぶ側が別に渡す）。</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 応答の読み（日本語話者名を潰さない・未知の欄は捨てる・欄名の大小は無視）。
    /// <para>
    /// <b><see cref="JsonNamingPolicy.SnakeCaseLower"/> を敷いてある</b>＝契約の
    /// <see cref="WarmupStartResult"/>・<see cref="PrecomputeStartResult"/>・<see cref="CancelResult"/> は
    /// 位置レコードで <c>[JsonPropertyName]</c> を持たないので、これが無いと
    /// <c>shots_total</c>／<c>cancel_requested</c> が読めずに null になる（実測）。
    /// 明示の <c>[JsonPropertyName]</c> を持つ型（<c>StatusResponse</c> ほか）はそちらが勝つので影響しない。
    /// </para>
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new TolerantStringConverter() },
    };

    /// <summary>
    /// 文字列の欄に数・真偽が来ても落ちない読み。
    /// <para>
    /// <b>実測で要る</b>＝ROCm の torch は <c>get_device_properties(i).pci_bus_id</c> を
    /// <b>整数</b>（<c>197</c>）で返し、wrapper はそれをそのまま
    /// <c>/ywk/status.device.pci_bus_id</c> に載せる（<c>server/ywk_server.py</c> の
    /// <c>_cuda_device_info</c>・<c>research/lab/notes/29</c> §5）。契約 ⑹ の逐語は
    /// この欄の型を書いていないので、<b>読む側が両方を飲む</b>。
    /// これが無いと <c>/ywk/status</c> の応答 1 本が丸ごと読めなくなる（この機体で実測）。
    /// </para>
    /// </summary>
    private sealed class TolerantStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Null => null,
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Number => ReadNumber(ref reader),
                _ => throw new JsonException("文字列として読めない値です。"),
            };

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value);
        }

        private static string ReadNumber(ref Utf8JsonReader reader) =>
            reader.TryGetInt64(out var integer)
                ? integer.ToString(CultureInfo.InvariantCulture)
                : reader.GetDouble().ToString(CultureInfo.InvariantCulture);
    }

    private readonly HttpClient _http;
    private bool _disposed;

    /// <summary>実機用（<paramref name="baseAddress"/> は <c>http://127.0.0.1:18088/</c>）。</summary>
    public WrapperClient(Uri baseAddress)
        : this(baseAddress, handler: null)
    {
    }

    /// <summary>
    /// 接続そのものの期限（<b>誰も listen していない相手を待つ上限</b>）。
    /// <para>
    /// 既定の <see cref="HttpClient"/> は接続に期限を持たない。実測＝誰も居ない
    /// <c>127.0.0.1</c> への 1 本が <b>2.0 秒</b>（proxy のせいではない＝<c>UseProxy=false</c> でも
    /// 2.0 秒）。この代金は死活の見張り（2 秒間隔・3 標本で降ろす）にそのまま乗るので、
    /// loopback だけを相手にする本 client では <b>1.5 秒</b>で打ち切る
    /// （生きている相手への loopback 接続は 1 ms 未満＝正常路には掛からない）。
    /// </para>
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(1.5);

    /// <summary>テスト用＝<paramref name="handler"/> に偽物を差す。</summary>
    public WrapperClient(Uri baseAddress, HttpMessageHandler? handler)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        BaseAddress = baseAddress;
        _http = handler is null
            ? new HttpClient(new SocketsHttpHandler { ConnectTimeout = ConnectTimeout }, disposeHandler: true)
            : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = baseAddress;
        _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan; // 期限は要求ごとの CTS で掛ける
        _http.DefaultRequestHeaders.ExpectContinue = false;
        Timeout = DefaultTimeout;
    }

    public Uri BaseAddress { get; }

    public TimeSpan Timeout { get; set; }

    /// <summary>その状態番号が「口が無い」を意味するか（404／405＝契約 ⑵・⑺）。</summary>
    public static bool IsMissingEndpoint(int statusCode) => statusCode is 404 or 405;

    /// <summary>
    /// <c>POST /v1/audio/speech</c> の body を組む（<b>純関数</b>＝テストで釘付けする）。
    /// <para>
    /// <c>model</c>／<c>response_format</c> は固定。<c>voice</c>・<c>speed</c> は null なら
    /// <b>欄ごと出さない</b>（「既定に戻す」は欄を出さないことで表す＝契約 ⑶ 3-1）。
    /// <c>irodori</c> の中も null の値は載せない。
    /// </para>
    /// </summary>
    public static string BuildSpeechBody(SpeechRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var root = new JsonObject
        {
            ["model"] = SpeechRequest.ModelName,
            ["input"] = request.Input,
            ["response_format"] = SpeechRequest.ResponseFormat,
        };

        if (!string.IsNullOrWhiteSpace(request.Voice))
        {
            root["voice"] = request.Voice;
        }

        if (request.Speed is double speed)
        {
            root["speed"] = speed;
        }

        if (request.Irodori is { Count: > 0 })
        {
            var nested = new JsonObject();
            foreach (var pair in request.Irodori)
            {
                if (pair.Value is null)
                {
                    continue; // 明示 null は「欄を出さない」と同義（契約 ⑶ 3-1）
                }

                nested[pair.Key] = ToNode(pair.Value);
            }

            if (nested.Count > 0)
            {
                root["irodori"] = nested;
            }
        }

        return root.ToJsonString(JsonOptions);
    }

    /// <summary><c>POST /ywk/warmup</c> の body（3 欄とも任意＝空なら <c>{}</c>）。</summary>
    public static string BuildWarmupBody(WarmupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = new JsonObject();
        if (request.Stages is { Count: > 0 })
        {
            var stages = new JsonArray();
            foreach (var value in request.Stages)
            {
                stages.Add((JsonNode)JsonValue.Create(value));
            }

            root["stages"] = stages;
        }

        if (request.Voices is { Count: > 0 })
        {
            var voices = new JsonArray();
            foreach (var value in request.Voices)
            {
                voices.Add(ToNode(value));
            }

            root["voices"] = voices;
        }

        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            root["text"] = request.Text;
        }

        return root.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// <c>POST /ywk/voices/precompute</c> の body。<c>ids</c> と <c>all</c> は
    /// <b>どちらか一方</b>（両方＝400・どちらも無し＝400＝契約 ⑺ 7-3）。
    /// </summary>
    public static string BuildPrecomputeBody(PrecomputeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = new JsonObject();
        if (request.Ids is { Count: > 0 })
        {
            var ids = new JsonArray();
            foreach (var value in request.Ids)
            {
                ids.Add(ToNode(value));
            }

            root["ids"] = ids;
        }
        else if (request.All is bool all)
        {
            root["all"] = all;
        }

        if (request.Force is bool force)
        {
            root["force"] = force;
        }

        return root.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// 値 1 つを JSON のノードにする（<b>載せる型を明示する</b>＝反射経路に頼らない）。
    /// 知らない型は文字列に落とす（勝手な型で 400 を貰うより、判る形で送る）。
    /// </summary>
    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create(f),
        decimal m => JsonValue.Create(m),
        JsonNode node => node.DeepClone(),
        _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    /// <summary>本文を型へ落とす（<b>純関数</b>・読めなければ null）。</summary>
    public static T? Parse<T>(string? body)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>エラー body の 4 欄（封筒が無い応答でも落ちない）。</summary>
    public static ErrorBody? ParseError(string? body) => Parse<ErrorEnvelope>(body)?.Error;

    public async Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken)
    {
        var call = await SendAsync(HttpMethod.Get, "health", null, Timeout, cancellationToken)
            .ConfigureAwait(false);
        return new HealthResponse(call.StatusCode, call.Body ?? string.Empty, call.Elapsed);
    }

    public Task<WrapperResult<StatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
        GetAsync<StatusResponse>("ywk/status", cancellationToken);

    public Task<WrapperResult<ParamsResponse>> GetParamsAsync(CancellationToken cancellationToken) =>
        GetAsync<ParamsResponse>("params", cancellationToken);

    public Task<WrapperResult<VoicesResponse>> GetVoicesAsync(CancellationToken cancellationToken) =>
        GetAsync<VoicesResponse>("ywk/voices", cancellationToken);

    public Task<WrapperResult<VoicesResponse>> GetOpenAiVoicesAsync(CancellationToken cancellationToken) =>
        GetAsync<VoicesResponse>("v1/audio/voices", cancellationToken);

    public async Task<SpeechResult> SynthesizeAsync(
        SpeechRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var call = await SendAsync(
            HttpMethod.Post,
            "v1/audio/speech",
            BuildSpeechBody(request),
            timeout,
            cancellationToken,
            wantBytes: true).ConfigureAwait(false);

        if (call.StatusCode is >= 200 and < 300 && call.Bytes is not null)
        {
            return new SpeechResult(true, call.Bytes, call.ContentType, call.Seed, call.StatusCode, null, call.Elapsed);
        }

        var error = call.Body is null
            ? new ErrorBody { Message = call.FailureReason, Code = null }
            : ParseError(call.Body) ?? new ErrorBody { Message = call.Body };

        return new SpeechResult(false, null, call.ContentType, call.Seed, call.StatusCode, error, call.Elapsed);
    }

    public Task<WrapperResult<WarmupStartResult>> StartWarmupAsync(
        WarmupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<WarmupStartResult>("ywk/warmup", BuildWarmupBody(request), cancellationToken);
    }

    public Task<WrapperResult<CancelResult>> CancelWarmupAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return SendJsonAsync<CancelResult>(HttpMethod.Delete, "ywk/warmup/" + Uri.EscapeDataString(id), null, cancellationToken);
    }

    public Task<WrapperResult<PrecomputeStartResult>> StartPrecomputeAsync(
        PrecomputeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<PrecomputeStartResult>(
            "ywk/voices/precompute",
            BuildPrecomputeBody(request),
            cancellationToken);
    }

    public Task<WrapperResult<CancelResult>> CancelPrecomputeAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return SendJsonAsync<CancelResult>(
            HttpMethod.Delete,
            "ywk/voices/precompute/" + Uri.EscapeDataString(id),
            null,
            cancellationToken);
    }

    public Task<WrapperResult<DropLatentResult>> DropLatentAsync(
        string voiceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceId);
        return SendJsonAsync<DropLatentResult>(
            HttpMethod.Delete,
            "ywk/voices/" + Uri.EscapeDataString(voiceId.Trim()) + "/latent",
            null,
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
    }

    private Task<WrapperResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class =>
        SendJsonAsync<T>(HttpMethod.Get, path, null, cancellationToken);

    private Task<WrapperResult<T>> PostAsync<T>(string path, string body, CancellationToken cancellationToken)
        where T : class =>
        SendJsonAsync<T>(HttpMethod.Post, path, body, cancellationToken);

    private async Task<WrapperResult<T>> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        string? body,
        CancellationToken cancellationToken)
        where T : class
    {
        var call = await SendAsync(method, path, body, Timeout, cancellationToken).ConfigureAwait(false);

        if (call.StatusCode == 0)
        {
            return new WrapperResult<T>(false, null, 0, null, true, call.Elapsed, call.FailureReason);
        }

        if (IsMissingEndpoint(call.StatusCode))
        {
            // その口が無い（便 C（2）が /ywk/voices/precompute を足している最中＝無ければ無いものとして動く）
            return new WrapperResult<T>(
                false, null, call.StatusCode, ParseError(call.Body), false, call.Elapsed,
                "この個体にはこの口がありません。");
        }

        if (call.StatusCode is >= 200 and < 300)
        {
            var value = Parse<T>(call.Body);
            return value is null
                ? new WrapperResult<T>(false, null, call.StatusCode, null, true, call.Elapsed, "応答の本文が読めませんでした。")
                : new WrapperResult<T>(true, value, call.StatusCode, null, true, call.Elapsed, null);
        }

        var error = ParseError(call.Body);
        return new WrapperResult<T>(
            false, null, call.StatusCode, error, true, call.Elapsed,
            error?.Message ?? ("サーバが " + call.StatusCode.ToString(CultureInfo.InvariantCulture) + " を返しました。"));
    }

    private async Task<HttpCall> SendAsync(
        HttpMethod method,
        string path,
        string? body,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool wantBytes = false)
    {
        var started = Stopwatch.GetTimestamp();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout > TimeSpan.Zero)
        {
            linked.CancelAfter(timeout);
        }

        try
        {
            using var message = new HttpRequestMessage(method, new Uri(BaseAddress, path));
            if (body is not null)
            {
                message.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            using var response = await _http
                .SendAsync(message, HttpCompletionOption.ResponseContentRead, linked.Token)
                .ConfigureAwait(false);

            var status = (int)response.StatusCode;
            var contentType = response.Content.Headers.ContentType?.MediaType;
            long? seed = null;
            if (response.Headers.TryGetValues("X-Irodori-Seed", out var seeds))
            {
                foreach (var value in seeds)
                {
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        seed = parsed;
                    }

                    break;
                }
            }

            if (wantBytes && status is >= 200 and < 300)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(linked.Token).ConfigureAwait(false);
                return new HttpCall(status, null, bytes, contentType, seed, Elapsed(started), null);
            }

            var text = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            return new HttpCall(status, text, null, contentType, seed, Elapsed(started), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // **接続の期限は「繋がらない」であって「遅い」ではない**（是正・便 D（2））。
            // SocketsHttpHandler は ConnectTimeout の満了を TaskCanceledException（内側が
            // TimeoutException）で返すので、要求そのものの期限（5 秒）と混ぜない＝
            // 死活の 1 行が「期限切れ（5 秒）」と嘘をつかない。
            if (ex.InnerException is TimeoutException)
            {
                return new HttpCall(
                    0, null, null, null, null, Elapsed(started),
                    "サーバに繋がりません（まだ起きていないか、落ちています）。");
            }

            return new HttpCall(0, null, null, null, null, Elapsed(started), "期限切れ（"
                + timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " 秒）で応答がありませんでした。");
        }
        catch (HttpRequestException ex)
        {
            return new HttpCall(0, null, null, null, null, Elapsed(started), Describe(ex));
        }
    }

    private static string Describe(HttpRequestException ex) =>
        ex.HttpRequestError == HttpRequestError.ConnectionError
            ? "サーバに繋がりません（まだ起きていないか、落ちています）。"
            : "通信に失敗しました：" + ex.Message;

    private static TimeSpan Elapsed(long started) => Stopwatch.GetElapsedTime(started);

    private sealed record HttpCall(
        int StatusCode,
        string? Body,
        byte[]? Bytes,
        string? ContentType,
        long? Seed,
        TimeSpan Elapsed,
        string? FailureReason);
}
