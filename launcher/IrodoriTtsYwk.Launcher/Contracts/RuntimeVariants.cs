using System;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>
/// <b>変種の名前が 2 系統ある</b>ことを 1 箇所に閉じる（3 席が別々に綴らないため）。
/// <list type="number">
/// <item><b>取得台帳の変種名</b>＝<c>cu130</c>／<c>cu126</c>／<c>cpu</c>／<c>rocm-gfx1151</c>。
/// <c>ledger/runtime-&lt;変種&gt;.json</c> と <c>runtime\&lt;変種&gt;\</c> の綴り。設定に保存するのはこちら。</item>
/// <item><b>wrapper が名乗るラベル</b>（env <c>YWK_VARIANT</c>・<c>/ywk/status.variant</c>）＝
/// <c>cuda</c>／<c>cpu</c>／<c>rocm-gfx1151</c>。cu130 と cu126 は<b>どちらも <c>cuda</c></b>
/// （契約 ⑹＝「何で組んだか」の名前であって能力ではない。実際に載った device は
/// <c>device.actual</c>）。<c>rocm-*</c> だけは wrapper の分岐に使われる
/// （bf16 固定・fp32 は exit 2・MIOpen db を <c>YWK_DATA_DIR</c> 配下へ）。</item>
/// </list>
/// </summary>
public static class RuntimeVariants
{
    /// <summary>既定（裁定 4＝NVIDIA・ドライバ 580 以上）。</summary>
    public const string Cu130 = "cu130";

    /// <summary>ドライバ 528.33 以上（裁定 88 ⑵）。UI で選べる（自動切替はしない＝裁定 4）。</summary>
    public const string Cu126 = "cu126";

    /// <summary>上級者・「遅い」の注記つき（裁定 13）。</summary>
    public const string Cpu = "cpu";

    /// <summary>Radeon 版だけが持つ（裁定 5＝別リリース・CUDA 版の選択肢には出さない）。</summary>
    public const string RocmGfx1151 = "rocm-gfx1151";

    /// <summary>env <c>YWK_VARIANT</c> の値（wrapper の <c>DEFAULT_VARIANT</c> と同じ）。</summary>
    public const string CudaLabel = "cuda";

    /// <summary>CUDA 版のリリースが UI に出す 3 択（Radeon 版は別リリース＝裁定 5）。</summary>
    public static readonly string[] CudaReleaseChoices = [Cu130, Cu126, Cpu];

    /// <summary>Radeon 版のリリースが UI に出す 2 択。</summary>
    public static readonly string[] RadeonReleaseChoices = [RocmGfx1151, Cpu];

    /// <summary>取得台帳の檔名（拡張子なし）＝<c>runtime-&lt;変種&gt;</c>。</summary>
    public static string LedgerName(string variant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        return "runtime-" + variant;
    }

    /// <summary>env <c>YWK_VARIANT</c> に載せる値。cu130／cu126 は <c>cuda</c> に畳む。</summary>
    public static string ServerLabel(string variant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        return IsCuda(variant) ? CudaLabel : variant.Trim();
    }

    public static bool IsCuda(string? variant) =>
        string.Equals(variant, Cu130, StringComparison.OrdinalIgnoreCase)
        || string.Equals(variant, Cu126, StringComparison.OrdinalIgnoreCase)
        || string.Equals(variant, CudaLabel, StringComparison.OrdinalIgnoreCase);

    public static bool IsRocm(string? variant) =>
        variant is not null && variant.TrimStart().StartsWith("rocm", StringComparison.OrdinalIgnoreCase);

    public static bool IsCpu(string? variant) =>
        string.Equals(variant, Cpu, StringComparison.OrdinalIgnoreCase);

    /// <summary>GPU で動く変種か（＝device に <c>cuda:N</c> を載せる変種か）。</summary>
    public static bool UsesGpu(string? variant) => !IsCpu(variant);

    /// <summary>
    /// 精度の既定（裁定 7＝device 連動。GPU→bf16・CPU→fp32）。
    /// <c>rocm-*</c> は bf16 固定（裁定 5・36＝fp32 を載せると wrapper が exit 2）。
    /// </summary>
    public static string DefaultPrecision(string variant) => IsCpu(variant) ? "fp32" : "bf16";

    /// <summary>利用者が精度を選べる変種か（Radeon 版は選べない＝bf16 固定）。</summary>
    public static bool AllowsPrecisionOverride(string variant) => !IsRocm(variant);

    /// <summary>
    /// 参照潜在の事前計算の既定（裁定 65＝<c>rocm-*</c> だけ ON・CUDA 版は設定で ON にできる）。
    /// wrapper の <c>YWK_PRECOMPUTE_ON_START</c> 未設定時の解決と同じ規則。
    /// </summary>
    public static bool PrecomputeOnStartDefault(string variant) => IsRocm(variant);

    /// <summary>ready 待ちの既定（契約 ⑵＝120 s。CPU 変種だけ 300 s＝設計書 §2）。</summary>
    public static TimeSpan ReadyTimeoutDefault(string variant) =>
        IsCpu(variant) ? TimeSpan.FromSeconds(300) : TimeSpan.FromSeconds(120);

    /// <summary>UI の表示名（日本語のみ＝裁定 52）。</summary>
    public static string DisplayName(string variant) => variant switch
    {
        Cu130 => "CUDA 13.0（既定・ドライバ 580 以上）",
        Cu126 => "CUDA 12.6（ドライバ 528.33 以上）",
        Cpu => "CPU（遅い・配信用途では非推奨）",
        RocmGfx1151 => "Radeon gfx1151（未保障・bf16 固定）",
        _ => variant,
    };

    /// <summary>
    /// <b>文中に差す短い名</b>（括弧の註を落とした形＝裁定 125 の B）。
    /// <see cref="DisplayName"/> は括弧に下限や注記を抱えているので、
    /// 「このドライバ（537.58）では <b>CUDA 13.0</b> は動きません」のような 1 行にはそのまま置けない。
    /// <b>名は 1 箇所で綴る</b>ので、短い形もここに置く。
    /// </summary>
    public static string ShortDisplayName(string variant) => variant switch
    {
        Cu130 => "CUDA 13.0",
        Cu126 => "CUDA 12.6",
        Cpu => "CPU",
        RocmGfx1151 => "Radeon gfx1151",
        CudaLabel => "CUDA 版",
        _ => variant,
    };
}
