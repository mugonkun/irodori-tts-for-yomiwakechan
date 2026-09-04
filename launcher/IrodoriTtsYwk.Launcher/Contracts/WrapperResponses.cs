using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>
/// エラー body の 4 欄（契約 ⑶ 3-3）。<b>機械可読な <c>code</c> が必ず入る</b>＝
/// 文言依存の判定をしない（本体の <c>IsMissingVoiceFailure</c> が踏んだ型を繰り返さない）。
/// </summary>
public sealed record ErrorBody
{
    [JsonPropertyName("message")] public string? Message { get; init; }

    [JsonPropertyName("type")] public string? Type { get; init; }

    [JsonPropertyName("param")] public string? Param { get; init; }

    /// <summary><c>ywk_unknown_voice</c> 等（契約 ⑶ 3-3 の表）。</summary>
    [JsonPropertyName("code")] public string? Code { get; init; }
}

/// <summary>エラー body の封筒（<c>{"error":{…}}</c>）。</summary>
public sealed record ErrorEnvelope
{
    [JsonPropertyName("error")] public ErrorBody? Error { get; init; }
}

/// <summary>
/// <c>GET /health</c>。<b>body は上流のままで解釈しない</b>（契約 ⑵）＝生のまま持つ。
/// 見るのは「200 が返ったか」だけ。ready の判定は <c>/ywk/status</c> か上流の
/// <c>runtime.loaded</c> で行う。
/// </summary>
/// <param name="StatusCode">HTTP の状態番号（到達不能なら 0）。</param>
/// <param name="RawBody">上流の body（畳まない・解釈しない）。</param>
/// <param name="Elapsed">往復。</param>
public sealed record HealthResponse(int StatusCode, string RawBody, TimeSpan Elapsed)
{
    public bool Ok => StatusCode == 200;
}

/// <summary>上流 pin（<c>/ywk/status.upstream</c>）。</summary>
public sealed record StatusUpstream
{
    [JsonPropertyName("irodori_tts")] public string? IrodoriTts { get; init; }

    [JsonPropertyName("server")] public string? Server { get; init; }
}

/// <summary><c>/ywk/status.runtime</c>。<b>ready の判定はここ</b>（契約 ⑵）。</summary>
public sealed record StatusRuntime
{
    [JsonPropertyName("loaded")] public bool? Loaded { get; init; }

    [JsonPropertyName("loading")] public bool? Loading { get; init; }

    [JsonPropertyName("error")] public string? Error { get; init; }
}

/// <summary>
/// <c>/ywk/status.device</c>。<b><c>actual</c> は実測値</b>（設定値の echo ではない）で、
/// モデル未読込なら null（契約 ⑹）。<c>hip</c>／<c>gcn_arch</c> が CUDA 機と Radeon 機を分ける
/// （ROCm の torch も device type を <c>cuda</c> と名乗る）。
/// </summary>
public sealed record StatusDevice
{
    [JsonPropertyName("configured")] public string? Configured { get; init; }

    [JsonPropertyName("actual")] public string? Actual { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("uuid")] public string? Uuid { get; init; }

    [JsonPropertyName("pci_bus_id")] public string? PciBusId { get; init; }

    [JsonPropertyName("precision")] public string? Precision { get; init; }

    [JsonPropertyName("hip")] public string? Hip { get; init; }

    [JsonPropertyName("gcn_arch")] public string? GcnArch { get; init; }
}

/// <summary><c>/ywk/status.torch</c>。</summary>
public sealed record StatusTorch
{
    [JsonPropertyName("version")] public string? Version { get; init; }

    [JsonPropertyName("cuda")] public string? Cuda { get; init; }

    [JsonPropertyName("hip")] public string? Hip { get; init; }
}

/// <summary><c>/ywk/status.voices</c>。<c>error</c> は「台帳が壊れている」理由 1 行（契約 ⑷）。</summary>
public sealed record StatusVoices
{
    [JsonPropertyName("count")] public int? Count { get; init; }

    /// <summary>末尾 1 段だけ（絶対パスは出ない）。</summary>
    [JsonPropertyName("dir")] public string? Dir { get; init; }

    [JsonPropertyName("error")] public string? Error { get; init; }
}

/// <summary>暖機の 1 射の記録（契約 ⑺ 7-2）。</summary>
public sealed record WarmupShot
{
    [JsonPropertyName("kind")] public string? Kind { get; init; }

    [JsonPropertyName("key")] public string? Key { get; init; }

    [JsonPropertyName("seconds")] public double? Seconds { get; init; }

    [JsonPropertyName("ms")] public double? Ms { get; init; }
}

/// <summary>
/// <c>/ywk/status.warmup</c>（契約 ⑺ 7-2）。<c>state</c>＝
/// <c>idle|running|done|failed|cancelled</c>。<c>shots</c> は <c>shots_done</c> の別名。
/// </summary>
public sealed record WarmupStatus
{
    [JsonPropertyName("state")] public string? State { get; init; }

    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("shots_done")] public int? ShotsDone { get; init; }

    [JsonPropertyName("shots_total")] public int? ShotsTotal { get; init; }

    [JsonPropertyName("elapsed_s")] public double? ElapsedSeconds { get; init; }

    [JsonPropertyName("last_shot")] public WarmupShot? LastShot { get; init; }

    [JsonPropertyName("error")] public string? Error { get; init; }

    [JsonPropertyName("shots")] public int? Shots { get; init; }

    public bool IsRunning => string.Equals(State, "running", StringComparison.Ordinal);
}

/// <summary>事前計算の 1 件の記録（契約 ⑺ 7-3）。</summary>
public sealed record PrecomputeLast
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary><c>built|reused|skipped|failed</c>。</summary>
    [JsonPropertyName("state")] public string? State { get; init; }

    [JsonPropertyName("reason")] public string? Reason { get; init; }

    [JsonPropertyName("frames")] public int? Frames { get; init; }

    [JsonPropertyName("ms")] public double? Ms { get; init; }
}

/// <summary>
/// <c>/ywk/status.precompute</c>（契約 ⑺ 7-3）。<b>変種に依らず必ず在る欄</b>で、
/// CUDA 版では既定で <c>idle</c> のまま。
/// </summary>
public sealed record PrecomputeStatus
{
    [JsonPropertyName("state")] public string? State { get; init; }

    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("done")] public int? Done { get; init; }

    [JsonPropertyName("total")] public int? Total { get; init; }

    [JsonPropertyName("built")] public int? Built { get; init; }

    [JsonPropertyName("reused")] public int? Reused { get; init; }

    [JsonPropertyName("skipped")] public int? Skipped { get; init; }

    [JsonPropertyName("failed")] public int? Failed { get; init; }

    [JsonPropertyName("elapsed_s")] public double? ElapsedSeconds { get; init; }

    [JsonPropertyName("last")] public PrecomputeLast? Last { get; init; }

    [JsonPropertyName("error")] public string? Error { get; init; }

    public bool IsRunning => string.Equals(State, "running", StringComparison.Ordinal);
}

/// <summary>
/// <c>/ywk/status.memory</c>（裁定 67 ⑶）。<b>便 C（2）が wrapper に足している最中</b>＝
/// <b>欄が無い応答にも耐える</b>（すべて null 許容・生の JSON も残す）。
/// 値は torch の allocated／reserved／max。OS 側の GPU Process Memory は性能カウンタから読む
/// （ランチャ側の仕事＝この型には入らない）。
/// </summary>
public sealed record MemoryStatus
{
    [JsonPropertyName("allocated")] public long? AllocatedBytes { get; init; }

    [JsonPropertyName("reserved")] public long? ReservedBytes { get; init; }

    [JsonPropertyName("max")] public long? MaxAllocatedBytes { get; init; }

    /// <summary>話者ごとの潜在の大きさ（欄名は便 C（2）の実装で確定＝無ければ空）。</summary>
    [JsonPropertyName("latents")] public IReadOnlyDictionary<string, long> Latents { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
}

/// <summary>
/// <c>GET /ywk/status</c>（契約 ⑹）。<b>404 なら上流の素の Server</b>＝配布版として扱わない。
/// 欄はすべて null 許容＝wrapper が欄を足しても（<c>schema</c> は上がらない）落ちない。
/// </summary>
public sealed record StatusResponse
{
    [JsonPropertyName("engine")] public string? Engine { get; init; }

    [JsonPropertyName("version")] public string? Version { get; init; }

    [JsonPropertyName("upstream")] public StatusUpstream? Upstream { get; init; }

    [JsonPropertyName("host")] public string? Host { get; init; }

    [JsonPropertyName("port")] public int? Port { get; init; }

    /// <summary>env <c>YWK_VARIANT</c> の値そのまま（<c>cuda</c>／<c>cpu</c>／<c>rocm-gfx1151</c>）。</summary>
    [JsonPropertyName("variant")] public string? Variant { get; init; }

    [JsonPropertyName("runtime")] public StatusRuntime? Runtime { get; init; }

    [JsonPropertyName("device")] public StatusDevice? Device { get; init; }

    [JsonPropertyName("torch")] public StatusTorch? Torch { get; init; }

    [JsonPropertyName("voices")] public StatusVoices? Voices { get; init; }

    [JsonPropertyName("warmup")] public WarmupStatus? Warmup { get; init; }

    [JsonPropertyName("precompute")] public PrecomputeStatus? Precompute { get; init; }

    [JsonPropertyName("memory")] public MemoryStatus? Memory { get; init; }

    /// <summary>合成できる＝<c>runtime.loaded</c> が真。</summary>
    public bool IsReady => Runtime?.Loaded == true;
}

/// <summary><c>/params.checkpoint</c>。読込前は 3 欄とも null（契約 ⑸）。</summary>
public sealed record ParamsCheckpoint
{
    [JsonPropertyName("hf")] public string? Hf { get; init; }

    [JsonPropertyName("max_text_len")] public int? MaxTextLen { get; init; }

    [JsonPropertyName("max_caption_len")] public int? MaxCaptionLen { get; init; }

    [JsonPropertyName("ref_max_seconds")] public double? RefMaxSeconds { get; init; }
}

/// <summary>
/// <c>/params.irodori[]</c> の 1 欄（契約 ⑸ 5-1）。<b>本体が写すのは
/// <c>exposed_to_ywk: true</c> の欄だけ</b>だが、<b>ランチャは 44 欄すべてを扱う</b>
/// （上級者向け <c>advanced</c> 群）。
/// </summary>
public sealed record ParamDescriptor
{
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;

    /// <summary><c>string|number|integer|boolean|enum</c>。</summary>
    [JsonPropertyName("type")] public string? Type { get; init; }

    /// <summary>env 反映後の<b>実効値</b>（契約 ⑸）。型は欄ごとに違うので生のまま持つ。</summary>
    [JsonPropertyName("default")] public JsonElement? Default { get; init; }

    [JsonPropertyName("min")] public double? Min { get; init; }

    [JsonPropertyName("max")] public double? Max { get; init; }

    [JsonPropertyName("step")] public double? Step { get; init; }

    [JsonPropertyName("nullable")] public bool? Nullable { get; init; }

    [JsonPropertyName("exposed_to_ywk")] public bool? ExposedToYwk { get; init; }

    [JsonPropertyName("group")] public string? Group { get; init; }

    [JsonPropertyName("label")] public string? Label { get; init; }

    [JsonPropertyName("description")] public string? Description { get; init; }

    [JsonPropertyName("max_length")] public int? MaxLength { get; init; }

    [JsonPropertyName("enum")] public IReadOnlyList<string> Enum { get; init; } = [];

    /// <summary>UI のプリセット（<c>num_steps</c> の 10／40＝裁定 10）。</summary>
    [JsonPropertyName("presets")] public IReadOnlyList<double> Presets { get; init; } = [];

    [JsonPropertyName("note")] public string? Note { get; init; }

    /// <summary>範囲の出所（上流に検査は無い／gradio スライダ由来／配布版の推奨）。</summary>
    [JsonPropertyName("range_source")] public string? RangeSource { get; init; }
}

/// <summary>
/// <c>GET /params</c>（契約 ⑸）。<b>モデル未読込でも 200・≤ 100 ms</b>。
/// <c>request</c> と <c>rules</c> は形が欄ごとに違うので生の JSON で持つ
/// （UI が読むのは <c>irodori[]</c> と <c>rules.exposed_to_ywk</c> が主）。
/// </summary>
public sealed record ParamsResponse
{
    [JsonPropertyName("schema")] public int? Schema { get; init; }

    [JsonPropertyName("engine")] public string? Engine { get; init; }

    [JsonPropertyName("model_loaded")] public bool? ModelLoaded { get; init; }

    [JsonPropertyName("checkpoint")] public ParamsCheckpoint? Checkpoint { get; init; }

    [JsonPropertyName("request")] public JsonElement? Request { get; init; }

    [JsonPropertyName("irodori")] public IReadOnlyList<ParamDescriptor> Irodori { get; init; } = [];

    [JsonPropertyName("rules")] public JsonElement? Rules { get; init; }

    [JsonPropertyName("prefetch")] public JsonElement? Prefetch { get; init; }
}

/// <summary>
/// 話者 1 件（契約 ⑷ 4-1）。<b>パス欄は落ちている</b>（絶対パスを出さない）。
/// <c>latent</c>／<c>latent_stale</c> は裁定 65 の参照潜在（Radeon 版で使う）。
/// </summary>
public sealed record VoiceInfo
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("object")] public string? Object { get; init; }

    [JsonPropertyName("display_name")] public string? DisplayName { get; init; }

    [JsonPropertyName("preset")] public bool? Preset { get; init; }

    [JsonPropertyName("no_ref")] public bool? NoRef { get; init; }

    /// <summary>焼いた <c>.pt</c> から鳴る。</summary>
    [JsonPropertyName("latent")] public bool? Latent { get; init; }

    /// <summary>焼いてはあるが元の wav が変わった／消えた＝焼き直しを促す印。</summary>
    [JsonPropertyName("latent_stale")] public bool? LatentStale { get; init; }

    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
}

/// <summary>
/// <c>GET /ywk/voices</c>（と <c>GET /v1/audio/voices</c>）。<b>台帳が壊れても 500 を返さない</b>＝
/// 「デフォルト」1 件と <c>error</c> の理由が返る（契約 ⑷ 4-1）。
/// </summary>
public sealed record VoicesResponse
{
    [JsonPropertyName("object")] public string? Object { get; init; }

    [JsonPropertyName("data")] public IReadOnlyList<VoiceInfo> Data { get; init; } = [];

    [JsonPropertyName("default_voice")] public string? DefaultVoice { get; init; }

    [JsonPropertyName("no_ref_voice")] public string? NoRefVoice { get; init; }

    [JsonPropertyName("count")] public int? Count { get; init; }

    /// <summary><c>voices.json</c> が読めないときの理由 1 行。</summary>
    [JsonPropertyName("error")] public string? Error { get; init; }
}
