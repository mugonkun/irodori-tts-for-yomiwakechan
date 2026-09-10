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

/// <summary>
/// <c>/ywk/status.runtime</c>。<b>ready の判定はここ</b>（契約 ⑵）。
/// <para>
/// 裁定 105（ポート先行）で 3 欄が起動そのものを語る＝bind の直後は
/// <c>{loaded:false, loading:true, error:null}</c>・載ったら <c>loaded:true</c>・
/// <b>読込に失敗したら <c>{loaded:false, loading:false, error:"&lt;理由 1 行&gt;"}</c> のまま
/// プロセスは生き続ける</b>（本体とランチャが理由を読めるように）。
/// </para>
/// </summary>
public sealed record StatusRuntime
{
    [JsonPropertyName("loaded")] public bool? Loaded { get; init; }

    [JsonPropertyName("loading")] public bool? Loading { get; init; }

    /// <summary>
    /// 読込が失敗した理由 1 行（成功・読込中は null）。絶対パスは wrapper が
    /// <c>&lt;path&gt;</c> に畳んである（契約 ⑹）＝<b>裏読込の路も遅延読込の路も同じ
    /// 1 行整形を通る</b>（是正・便 G）。<b>この欄は「載らなかった」だけを意味する</b>＝
    /// 載っている個体の合成 1 回の 5xx は此処に出ない。
    /// <b><see cref="Loaded"/> が偽で此の欄が非 null の標本は <c>Failed</c>（理由つき）へ
    /// 落とす</b>＝<see cref="Services.Server.ServerStateMachine"/>。
    /// </summary>
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

/// <summary>
/// <c>/ywk/status.requests</c>（契約 ⑹・<c>decisions.md</c> 130 Q4 で足した欄）。
/// <para>
/// <see cref="InFlight"/>＝いま走っている<b>本物の</b> <c>POST /v1/audio/speech</c> の数
/// （暖機・事前計算は数えない＝⑺ 7-2 の優先度判定と同じ計数）。
/// <b>欄が無い個体（v1.1.0 以前の wrapper）では null</b>＝<b>0 と読む</b>ので、
/// 古い個体に当たったこの版のランチャは〔しゃべらせる〕を押せるまま（退行しない）。
/// </para>
/// <para>
/// <b>累計（<c>total</c>）は契約に足していない</b>＝ランチャ自身の射・第三のクライアント・
/// 落ちた射／SSE の中断が区別できず、引き算がずれて嘘の 1 行になる（<c>v2-spec.md</c> §8 Q4）。
/// </para>
/// <para>
/// <b>型違いは標本全体を落とす</b>＝<c>int?</c> で受けるので <c>1.0</c>・<c>"1"</c> が来ると
/// <c>JsonException</c> になり、<see cref="Services.Http.WrapperClient"/> の解釈が
/// <c>/ywk/status</c> の応答 1 本を丸ごと null にする（欄 1 つではなく標本ごと）。
/// <b>wrapper は必ず int を出す約束</b>（契約 ⑹）なので当面は据え置く＝直すなら
/// <c>TolerantStringConverter</c> の隣に数を飲む <c>JsonConverter&lt;int?&gt;</c> を足す。
/// </para>
/// </summary>
public sealed record StatusRequests
{
    [JsonPropertyName("in_flight")] public int? InFlight { get; init; }
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
/// <c>/ywk/status.memory</c>（<b>裁定 87 ⑴ で欄が確定した</b>・裁定 67 ⑶）。
/// <b>単位はすべてバイト</b>。
/// <list type="bullet">
/// <item><see cref="AllocatedBytes"/>／<see cref="ReservedBytes"/>／<see cref="MaxAllocatedBytes"/>
/// ＝torch の allocator（<c>memory_allocated</c>／<c>memory_reserved</c>／
/// <c>max_memory_allocated</c>）。<b>画面では 使用量＝allocated・占有量＝reserved</b>。</item>
/// <item><see cref="GpuTotalBytes"/>／<see cref="GpuFreeBytes"/>＝<c>torch.cuda.mem_get_info</c>
/// （ROCm も同じ口）。<see cref="GpuUsedBytes"/>＝<c>total − free</c>＝
/// <b>カード全体の占有（他プロセス込み）</b>
/// 〔<b>裁定 110（2026-09-08）で読み替え</b>＝Windows の ROCm では<b>自プロセス相当</b>で、
/// 他プロセスを含まない（実測＝同じ瞬間に <c>gpu_used</c> 3.66 GiB・OS の計数は自プロセス
/// 10.84 GiB／カード全体 29.79 GiB）。<b>カード全体は Windows の計数から採る</b>
/// （<c>Services/Gpu</c>＝PDH の <c>GPU Adapter Memory\Dedicated Usage</c> と DXGI の
/// <c>DedicatedVideoMemory</c>）。欄も算術も変えない＝<b>状態帯にこの 3 欄はもう出ない</b>〕。</item>
/// <item><see cref="Latents"/>＝<c>latents/&lt;stem&gt;.pt</c> の実サイズを話者 id で引く表
/// （<b>焼いていない話者は載らない</b>）。<see cref="LatentsTotalBytes"/> はその合計。</item>
/// </list>
/// <para>
/// <b><see cref="Device"/> が <c>cpu</c> か未読込のときは数値欄が全部 null</b>で
/// <see cref="Latents"/> だけが来る（裁定 87 ⑴）＝欄の欠けを「0」と読まないために、
/// 数値はすべて null 許容のままにしてある。
/// </para>
/// </summary>
public sealed record MemoryStatus
{
    /// <summary><c>cuda:0</c>／<c>cpu</c>／未読込なら null。</summary>
    [JsonPropertyName("device")] public string? Device { get; init; }

    /// <summary>torch の <c>memory_allocated</c>＝画面の<b>使用量</b>。</summary>
    [JsonPropertyName("allocated")] public long? AllocatedBytes { get; init; }

    /// <summary>torch の <c>memory_reserved</c>＝画面の<b>占有量</b>。</summary>
    [JsonPropertyName("reserved")] public long? ReservedBytes { get; init; }

    /// <summary>torch の <c>max_memory_allocated</c>。</summary>
    [JsonPropertyName("max")] public long? MaxAllocatedBytes { get; init; }

    /// <summary>
    /// <c>mem_get_info</c> の total（カード全体）〔<b>裁定 110 で読み替え</b>＝gfx1151 では
    /// <b>共有プールの総量</b>（実測 99.74 GiB）。帯の「GPU 全体」の分母は DXGI の
    /// <c>DedicatedVideoMemory</c>（同機体 63.83 GiB）に替わった〕。
    /// </summary>
    [JsonPropertyName("gpu_total")] public long? GpuTotalBytes { get; init; }

    /// <summary><c>mem_get_info</c> の free。</summary>
    [JsonPropertyName("gpu_free")] public long? GpuFreeBytes { get; init; }

    /// <summary>
    /// <c>total − free</c>＝カード全体の占有（<b>他プロセス込み</b>）
    /// 〔<b>裁定 110（2026-09-08）で読み替え</b>＝Windows の ROCm では<b>自プロセス相当で、
    /// 他プロセスを含まない</b>。<b>状態帯には出さない</b>（カード全体は Windows の計数から採る）〕。
    /// </summary>
    [JsonPropertyName("gpu_used")] public long? GpuUsedBytes { get; init; }

    /// <summary>
    /// 話者 id → 焼いた <c>.pt</c> の実サイズ（焼いていない話者は載らない）。
    /// <para>
    /// <b><c>"latents": null</c> でも空表になる</b>（是正・便 D（3）・low 6 の ⑶）＝
    /// <see cref="System.Text.Json"/> は<b>明示の null を初期化子より優先する</b>ので、
    /// 欄が来ない応答（既定値が残る）と <c>null</c> が来た応答（<c>null</c> が入る）で
    /// 挙動が割れ、後者は <see cref="LatentCount"/> の 1 語で <c>NullReferenceException</c> に
    /// なった（状態帯の描き直しは見張りの標本ごとに走るので、窓が 2 秒で落ちる）。
    /// 受けは <c>null</c> を許し、読みは必ず空表に落とす。
    /// </para>
    /// </summary>
    [JsonPropertyName("latents")] public IReadOnlyDictionary<string, long>? Latents { get; init; }

    /// <summary>焼いた潜在の表（<c>null</c> は空表として読む＝上の註）。</summary>
    public IReadOnlyDictionary<string, long> EffectiveLatents => Latents ?? EmptyLatents;

    private static readonly IReadOnlyDictionary<string, long> EmptyLatents =
        new Dictionary<string, long>(StringComparer.Ordinal);

    /// <summary><see cref="Latents"/> の合計。</summary>
    [JsonPropertyName("latents_total")] public long? LatentsTotalBytes { get; init; }

    /// <summary>採った時刻（ISO 8601 の<b>文字列のまま</b>持つ＝地域設定で揺らさない）。</summary>
    [JsonPropertyName("sampled_at")] public string? SampledAt { get; init; }

    /// <summary>読めなかった理由 1 行（読めていれば null）。</summary>
    [JsonPropertyName("error")] public string? Error { get; init; }

    /// <summary>CPU で走っている（GPU メモリの欄は出ない＝裁定 87 ⑴）。</summary>
    public bool IsCpu => Device is not null
        && Device.Trim().StartsWith("cpu", StringComparison.OrdinalIgnoreCase);

    /// <summary>torch と mem_get_info の数値がひとつでも来ているか。</summary>
    public bool HasNumbers => AllocatedBytes is not null || ReservedBytes is not null
        || MaxAllocatedBytes is not null || GpuTotalBytes is not null
        || GpuFreeBytes is not null || GpuUsedBytes is not null;

    /// <summary>
    /// カード全体の占有（<c>gpu_used</c>。欄が無ければ <c>total − free</c> から起こす）。
    /// <para>
    /// 〔<b>裁定 110（2026-09-08）で読み替え</b>＝「カード全体」ではない（Windows の ROCm では
    /// 自プロセス相当）。<b>状態帯からは外れた</b>ので、いまこれを読んでいるのは
    /// <c>RoundTwoUiTests</c> の<b>算術の釘</b>（欄が無いときに <c>total − free</c> から起こす）
    /// だけである。欄も算術も変えていない。〕
    /// </para>
    /// </summary>
    public long? EffectiveGpuUsed => GpuUsedBytes
        ?? (GpuTotalBytes is long total && GpuFreeBytes is long free ? total - free : null);

    /// <summary>焼いてある話者の数（<see cref="EffectiveLatents"/> の件数）。</summary>
    public int LatentCount => EffectiveLatents.Count;

    /// <summary>焼いた潜在の合計（<c>latents_total</c>。無ければ表から足す）。</summary>
    public long? EffectiveLatentsTotal
    {
        get
        {
            if (LatentsTotalBytes is long total)
            {
                return total;
            }

            var table = EffectiveLatents;
            if (table.Count == 0)
            {
                return null;
            }

            var sum = 0L;
            foreach (var value in table.Values)
            {
                sum += value;
            }

            return sum;
        }
    }

    /// <summary>この話者の焼いた潜在の実サイズ（焼いていなければ null）。</summary>
    public long? LatentBytesFor(string? voiceId) =>
        voiceId is not null && EffectiveLatents.TryGetValue(voiceId, out var bytes) ? bytes : null;
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

    /// <summary>
    /// 答えた個体の pid（契約 ⑹・便 D（2） で足した欄）。<b>古い wrapper では null</b>＝
    /// 欄が無いことを「別人だ」と読まない。
    /// <para>
    /// 何のためか＝<b>既にそのポートを握っている個体が居ると、同じ形で答えてくる</b>。
    /// （裁定 105 のポート先行で自分の子の bind は 1 秒以内に来るようになったが、
    /// その子が bind に失敗して落ちるまでの窓は残る。）
    /// ランチャは自分が起こした子の pid と突合し、違えば<b>その標本を自分の物として採らない</b>
    /// （状態帯に他人の GPU メモリを出さない＝裁定 67 ⑶）。
    /// </para>
    /// </summary>
    [JsonPropertyName("pid")] public int? Pid { get; init; }

    /// <summary>env <c>YWK_VARIANT</c> の値そのまま（<c>cuda</c>／<c>cpu</c>／<c>rocm-gfx1151</c>）。</summary>
    [JsonPropertyName("variant")] public string? Variant { get; init; }

    [JsonPropertyName("runtime")] public StatusRuntime? Runtime { get; init; }

    [JsonPropertyName("device")] public StatusDevice? Device { get; init; }

    [JsonPropertyName("torch")] public StatusTorch? Torch { get; init; }

    [JsonPropertyName("voices")] public StatusVoices? Voices { get; init; }

    [JsonPropertyName("warmup")] public WarmupStatus? Warmup { get; init; }

    [JsonPropertyName("precompute")] public PrecomputeStatus? Precompute { get; init; }

    [JsonPropertyName("memory")] public MemoryStatus? Memory { get; init; }

    /// <summary>
    /// いま走っている本物の合成の数（契約 ⑹・<c>decisions.md</c> 130 Q4）。
    /// <b>欄が無ければ null</b>＝古い wrapper。読み方は <see cref="RequestsInFlight"/>。
    /// </summary>
    [JsonPropertyName("requests")] public StatusRequests? Requests { get; init; }

    /// <summary>合成できる＝<c>runtime.loaded</c> が真。</summary>
    public bool IsReady => Runtime?.Loaded == true;

    /// <summary>
    /// いま走っている本物の合成の数（<b>欄が無い個体は 0</b>＝古い wrapper を「使用中」と
    /// 読まない）。負の数は来ない約束だが、来ても 0 に丸める（釦を理由なく殺さない）。
    /// </summary>
    public int RequestsInFlight => Requests?.InFlight is int count && count > 0 ? count : 0;

    /// <summary>
    /// <b>本体（読み分けちゃん2）が読み上げに使っている</b>＝〔しゃべらせる〕を譲る合図
    /// （決裁 130 Q4）。<b>錠ではなく案内</b>＝1 プロセス 1 合成の直列（契約 ⑶ 3-4）は
    /// 変えていないので、2 秒の見張りの隙をすり抜けて押せても壊れない（待たされるだけ）。
    /// <para>
    /// <b>自分の射との区別はここでは付けない</b>＝ランチャ自身の〔しゃべらせる〕も同じ
    /// <c>POST /v1/audio/speech</c> を撃って同じ数に乗るので、区別は
    /// <see cref="ViewModels.TryViewModel"/> が自分の走行中かどうかで行う（<c>v2-spec.md</c> §2-1c）。
    /// </para>
    /// </summary>
    public bool HostBusy => RequestsInFlight > 0;
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
