using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>
/// 口の呼び出し 1 回の結末（<b>欄が無い応答にも耐える</b>ための共通の器）。
/// <para>
/// <see cref="Available"/> が偽＝<b>その口が無い</b>（404／405）。
/// <c>POST /ywk/voices/precompute</c> は便 C（2）が足している最中なので、
/// <b>無ければ無いものとして動く</b>（UI は事前計算の欄を伏せる）。
/// </para>
/// </summary>
/// <typeparam name="T">応答の型。</typeparam>
/// <param name="Ok">2xx で読めたか。</param>
/// <param name="Value">読めた本文（失敗なら null）。</param>
/// <param name="StatusCode">HTTP の状態番号（到達不能なら 0）。</param>
/// <param name="Error">4xx／5xx の <c>{"error":{…}}</c>。</param>
/// <param name="Available">その口が在るか（404／405 なら偽）。</param>
/// <param name="Elapsed">往復。</param>
/// <param name="FailureReason">理由 1 行（到達不能・読めない body など）。</param>
public sealed record WrapperResult<T>(
    bool Ok,
    T? Value,
    int StatusCode,
    ErrorBody? Error,
    bool Available,
    TimeSpan Elapsed,
    string? FailureReason)
{
    /// <summary>機械可読な code（契約 ⑶ 3-3）。文言では判定しない。</summary>
    public string? Code => Error?.Code;
}

/// <summary>
/// <c>POST /v1/audio/speech</c> の body（契約 ⑶ 3-1）。
/// <para>
/// <b>上流の 44 欄は <see cref="Irodori"/> ネストに入れて送る</b>（Literal 3 欄はネスト必須）。
/// <c>speed</c> と <c>irodori.duration_scale</c> は<b>両方送ると除算で合成される</b>ので
/// どちらか一方にする。<c>irodori.seconds</c> は<b>暖機の射だけ</b>で使う（本番に送らない）。
/// </para>
/// <para>
/// 「既定に戻す」は<b>欄を出さない</b>ことで表す＝<see cref="Irodori"/> に載せない。
/// </para>
/// </summary>
/// <param name="Input">本文（1〜4096 字）。</param>
/// <param name="Voice">話者名（<c>/ywk/voices</c> の id）。null＝省略（＝参照なし合成・裁定 45）。</param>
/// <param name="Speed">0.25〜4.0。null＝送らない。</param>
/// <param name="Irodori">ネストに載せる欄（null の値は載せない）。</param>
public sealed record SpeechRequest(
    string Input,
    string? Voice = null,
    double? Speed = null,
    IReadOnlyDictionary<string, object?>? Irodori = null)
{
    /// <summary><c>model</c> は固定（他は 400）。</summary>
    public const string ModelName = "irodori-tts";

    /// <summary><c>response_format</c> は <c>wav</c> のみ（ffmpeg を同梱しない＝契約 ⑶ 3-2）。</summary>
    public const string ResponseFormat = "wav";
}

/// <param name="Ok">wav が返ったか。</param>
/// <param name="Audio">RIFF/WAVE の生バイト。</param>
/// <param name="ContentType">応答の型。</param>
/// <param name="Seed"><c>X-Irodori-Seed</c>（seed を明示しないと 1 本目のチャンクの値だけ）。</param>
/// <param name="StatusCode">HTTP の状態番号。</param>
/// <param name="Error">失敗時の 4 欄。</param>
/// <param name="Elapsed">往復（RTF の材料）。</param>
public sealed record SpeechResult(
    bool Ok,
    byte[]? Audio,
    string? ContentType,
    long? Seed,
    int StatusCode,
    ErrorBody? Error,
    TimeSpan Elapsed);

/// <summary>
/// <c>POST /ywk/warmup</c> の body（契約 ⑺ 7-2・3 欄とも任意）。
/// 射の順＝<c>stages</c> の秒（<c>no_ref</c>・<c>seconds</c> 指定）→ <c>voices</c> の各話者に短文 1 射。
/// </summary>
/// <param name="Stages">段（秒・0 &lt; s ≤ 60・最大 12 段）。既定 <c>[4,8,12]</c>。</param>
/// <param name="Voices">撃つ話者（最大 64 件）。</param>
/// <param name="Text">本文（最大 200 字）。既定「暖機です。」。</param>
public sealed record WarmupRequest(
    IReadOnlyList<double>? Stages = null,
    IReadOnlyList<string>? Voices = null,
    string? Text = null);

/// <summary>
/// <c>POST /ywk/warmup</c> の応答（契約 ⑺ 7-2＝<b>202</b>）。
/// <para>
/// <b>欄名は属性で書く</b>（裁定 87 ⑶）＝<c>WrapperClient.JsonOptions</c> の
/// <c>SnakeCaseLower</c> に頼ると、<c>shots_total</c> のように綴りが規則どおりの間は読めても、
/// 規則に乗らない欄（<c>ref_wav</c>／<c>gcn_arch</c> のような綴り）を足した日に<b>黙って null</b> になる。
/// 契約側に書いてあれば、命名方針を外した読み手でも同じ物が読める。
/// </para>
/// </summary>
/// <param name="Id">走行の id（<c>DELETE /ywk/warmup/{id}</c> に渡す）。</param>
/// <param name="ShotsTotal"><c>len(stages)+len(voices)</c>。</param>
/// <param name="State">受理直後は <c>running</c>。</param>
public sealed record WarmupStartResult(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("shots_total")] int? ShotsTotal,
    [property: JsonPropertyName("state")] string? State);

/// <summary>
/// <c>POST /ywk/voices/precompute</c> の body（契約 ⑺ 7-3）。
/// <b><see cref="Ids"/> と <see cref="All"/> はどちらか一方</b>（両方＝400・どちらも無し＝400）。
/// </summary>
/// <param name="Ids">焼く話者（最大 256 件）。知らない名が 1 つでもあれば走る前に 400。</param>
/// <param name="All">「デフォルト」と参照なしを除く全話者。</param>
/// <param name="Force">変わっていなくても焼き直す。</param>
public sealed record PrecomputeRequest(
    IReadOnlyList<string>? Ids = null,
    bool? All = null,
    bool? Force = null);

/// <summary><c>POST /ywk/voices/precompute</c> の応答（契約 ⑺ 7-3＝<b>202</b>・欄名は裁定 87 ⑶）。</summary>
/// <param name="Id">走行の id。</param>
/// <param name="Total">焼く件数。</param>
/// <param name="State">受理直後は <c>running</c>。</param>
public sealed record PrecomputeStartResult(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("total")] int? Total,
    [property: JsonPropertyName("state")] string? State);

/// <summary>暖機／事前計算の取消の応答（契約 ⑺ 7-2・7-3・欄名は裁定 87 ⑶）。</summary>
/// <param name="Id">止めた走行。</param>
/// <param name="State">止めた時点の state。</param>
/// <param name="CancelRequested">取消が受理されたか。</param>
public sealed record CancelResult(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("cancel_requested")] bool? CancelRequested);

/// <summary>
/// <c>DELETE /ywk/voices/{話者 id}/latent</c> の応答（契約 ⑺ 7-3・⑷ 4-3）。
/// <para>
/// 焼いた潜在から<b>元の wav へ戻る唯一の口</b>である。上流は別名を走査より先に読むので
/// （<c>voices.py:80-88</c>）、<c>ref_latent</c> が残っている限り
/// <c>voices/&lt;話者&gt;.wav</c> を消しても話者は一覧に残り、誰も指していない <c>.pt</c> から
/// 鳴り続ける。
/// </para>
/// </summary>
/// <param name="Id">話者 id。</param>
/// <param name="State"><c>reverted</c>（外した）／<c>absent</c>（もともと無い）。</param>
/// <param name="Alias">別名がどうなったか（<c>ref_wav</c>／<c>ref_wavs</c>／<c>removed</c>／<c>unchanged</c>）。</param>
/// <param name="Removed">実際に消えた檔。</param>
public sealed record DropLatentResult(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("alias")] string? Alias,
    [property: JsonPropertyName("removed")] IReadOnlyList<string> Removed);

/// <summary>
/// 契約 ⑸＝wrapper の HTTP 口（<c>docs/contract.md</c> の写し）。
/// <para>
/// <b>接続先は <c>127.0.0.1:&lt;port&gt;</c> の 1 本だけ</b>＝<c>localhost</c> の名前指定は禁止
/// （IPv6 を先に試して毎要求 +1.2〜2.0 秒）。<c>Authorization</c> は付けない（付けても無視される）。
/// </para>
/// <para>
/// <b>本体が叩かない口をランチャが叩く</b>＝<c>/ywk/warmup</c>（⑺ 7-2）と
/// <c>/ywk/voices/precompute</c>（⑺ 7-3）。前者は状態欄に、後者は話者一覧の
/// <c>latent</c>／<c>latent_stale</c> に出る。
/// </para>
/// <para>
/// <b>どのメソッドも例外を投げない</b>（取消を除く）＝<see cref="WrapperResult{T}"/> で返し、
/// 口が無い（404／405）ことと落ちたことを区別する。
/// </para>
/// </summary>
public interface IWrapperClient : IDisposable
{
    /// <summary>叩く根（<c>http://127.0.0.1:18088/</c>）。</summary>
    Uri BaseAddress { get; }

    /// <summary>1 要求の期限（契約 ⑵＝5 秒。合成だけは別）。</summary>
    TimeSpan Timeout { get; set; }

    /// <summary>発見の 1 段目。<b>モデル未読込でも 200</b>。body は解釈しない。</summary>
    Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken);

    /// <summary>発見の 2 段目。<b>404 なら上流の素の Server</b>＝配布版として扱わない。</summary>
    Task<WrapperResult<StatusResponse>> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>発見の 3 段目。<b>モデル未読込でも 200・≤ 100 ms</b>。</summary>
    Task<WrapperResult<ParamsResponse>> GetParamsAsync(CancellationToken cancellationToken);

    /// <summary>配布版の話者一覧（<c>error</c> つき）。</summary>
    Task<WrapperResult<VoicesResponse>> GetVoicesAsync(CancellationToken cancellationToken);

    /// <summary>OpenAI 互換の話者一覧（本体が見るのはこちら＝形の突合に使う）。</summary>
    Task<WrapperResult<VoicesResponse>> GetOpenAiVoicesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 合成 1 発（<c>POST /v1/audio/speech</c>）。<b>読込中に撃つと待たされて 200 が返る</b>ので、
    /// 期限は ready 待ちを飲み込む値にする。
    /// </summary>
    Task<SpeechResult> SynthesizeAsync(
        SpeechRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>暖機を起こす（202）。走行中に重ねると 409 <c>ywk_warmup_running</c>。</summary>
    Task<WrapperResult<WarmupStartResult>> StartWarmupAsync(
        WarmupRequest request,
        CancellationToken cancellationToken);

    /// <summary>暖機を止める（次の射の前で止まる＝走っている射は最後まで走る）。</summary>
    Task<WrapperResult<CancelResult>> CancelWarmupAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// 参照潜在の事前計算を起こす（202）。<b>口がまだ無い個体もある</b>＝
    /// <see cref="WrapperResult{T}.Available"/> が偽で返る。
    /// </summary>
    Task<WrapperResult<PrecomputeStartResult>> StartPrecomputeAsync(
        PrecomputeRequest request,
        CancellationToken cancellationToken);

    /// <summary>事前計算を止める（次の 1 件の前で止まる）。</summary>
    Task<WrapperResult<CancelResult>> CancelPrecomputeAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// 焼いた参照潜在を外して別名を wav へ戻す（<c>DELETE /ywk/voices/{id}/latent</c>＝契約 ⑺ 7-3）。
    /// <para>
    /// <b>話者を消すときはこれが 1 手目</b>（裁定 78 ⑶＝<c>DELETE …/latent</c> → wav と台帳の順）。
    /// 知らない話者は <b>404</b>（<see cref="WrapperResult{T}.Available"/> は真・
    /// <see cref="WrapperResult{T}.Code"/> が <c>ywk_unknown_voice</c>）、事前計算の走行中は
    /// <b>409</b> <c>ywk_precompute_running</c>。口ごと無い個体は <c>Available=false</c>。
    /// </para>
    /// </summary>
    Task<WrapperResult<DropLatentResult>> DropLatentAsync(string voiceId, CancellationToken cancellationToken);
}

/// <summary>契約 ⑶ 3-3 の <c>code</c>（文言依存の判定を捨てるための定数）。</summary>
public static class WrapperErrorCodes
{
    public const string UnknownVoice = "ywk_unknown_voice";
    public const string MissingVoice = "ywk_missing_voice";
    public const string UnknownModel = "ywk_unknown_model";
    public const string EmptyInput = "ywk_empty_input";
    public const string RuntimeUnavailable = "ywk_runtime_unavailable";
    public const string UnknownField = "ywk_unknown_field";
    public const string OutOfRange = "ywk_out_of_range";
    public const string TypeError = "ywk_type_error";
    public const string InvalidEnum = "ywk_invalid_enum";
    public const string LiteralTopLevel = "ywk_literal_top_level";
    public const string VoiceAndNoRef = "ywk_voice_and_no_ref";
    public const string VoiceAndReference = "ywk_voice_and_reference";
    public const string UnsupportedResponseFormat = "ywk_unsupported_response_format";
    public const string InvalidBody = "ywk_invalid_body";
    public const string ValidationError = "ywk_validation_error";
    public const string WarmupRunning = "ywk_warmup_running";
    public const string WarmupUnknownId = "ywk_warmup_unknown_id";
    public const string PrecomputeRunning = "ywk_precompute_running";
    public const string PrecomputeUnknownId = "ywk_precompute_unknown_id";
    public const string UpstreamError = "ywk_upstream_error";
    public const string ServerError = "ywk_server_error";
    public const string NotFound = "ywk_not_found";
    public const string MethodNotAllowed = "ywk_method_not_allowed";
}
