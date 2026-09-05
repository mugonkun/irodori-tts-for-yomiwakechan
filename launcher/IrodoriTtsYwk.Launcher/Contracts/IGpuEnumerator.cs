using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>GPU をどこから読んだか。</summary>
public enum GpuSource
{
    /// <summary>読めなかった。</summary>
    None,

    /// <summary><c>nvidia-smi -L</c>／<c>--query-gpu</c>（0.04 s・NVIDIA 専用の高速路）。</summary>
    NvidiaSmi,

    /// <summary>変種の python で <c>torch.cuda.get_device_properties(i)</c>（1.2〜4 s・AMD も読める）。</summary>
    TorchProbe,
}

/// <summary>
/// GPU 1 台（裁定 34＝<b>同定は UUID</b>。index は再起動で変わりうる＝
/// <c>CUDA_DEVICE_ORDER</c> の既定は <c>FASTEST_FIRST</c>）。
/// </summary>
/// <param name="Uuid"><c>GPU-xxxxxxxx-…</c>。設定に保存するのはこれ 1 本。</param>
/// <param name="Name">表示名（例 <c>NVIDIA GeForce RTX 3090</c>・<c>AMD Radeon(TM) 8060S Graphics</c>）。</param>
/// <param name="Index">この列挙での index（<c>cuda:N</c> の N）。</param>
/// <param name="TotalMemoryBytes">VRAM（共有メモリの機体では巨大になる＝gfx1151 は 107 GB）。</param>
/// <param name="PciBusId">UUID の相方（契約 ⑹）。</param>
/// <param name="GcnArch"><c>gcnArchName</c>（例 <c>gfx1151</c>）。CUDA ビルドでは null。</param>
/// <param name="DriverVersion">NVIDIA のドライバ版（<c>nvidia-smi</c> 経路でだけ埋まる）。</param>
/// <param name="Source">読んだ路。</param>
public sealed record GpuInfo(
    string Uuid,
    string Name,
    int Index,
    long TotalMemoryBytes,
    string? PciBusId,
    string? GcnArch,
    string? DriverVersion,
    GpuSource Source)
{
    /// <summary>UI の 1 行（<c>0: NVIDIA GeForce RTX 3090（24.0 GB）</c>）。</summary>
    public string Label =>
        Index.ToString(CultureInfo.InvariantCulture) + ": " + Name
        + "（" + (TotalMemoryBytes / 1024.0 / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB）";

    /// <summary>Radeon（ROCm）か＝<c>gcnArchName</c> が読めた個体。</summary>
    public bool IsRocm => !string.IsNullOrWhiteSpace(GcnArch);

    /// <summary>
    /// <see cref="Label"/> をそのまま返す。
    /// <para>
    /// record の既定の <c>ToString()</c> は全欄を並べた <c>GpuInfo { Uuid = …, Name = … }</c> になる。
    /// <c>ComboBox</c> は <c>DisplayMemberPath="Label"</c> で正しく描くが、
    /// <b>UI Automation の項目名は既定の <c>ToString()</c> から作られる</b>ため、無人検分（受け入れ条件
    /// D-7・<c>probe/d-launch-probe.ps1</c>）には record の内部表現が見えてしまう（実測）。
    /// 画面に出る 1 行と、検分と、ログの 1 行を同じ文字列に揃える。
    /// </para>
    /// </summary>
    public override string ToString() => Label;
}

/// <param name="PythonExe">torch 経路で使う変種の python（無ければ nvidia-smi だけ試す）。</param>
/// <param name="Timeout">列挙の期限（受け入れ条件 D-2＝≤ 5 s）。</param>
public sealed record GpuEnumerationRequest(string? PythonExe, TimeSpan Timeout);

/// <param name="Gpus">見つかった GPU（index 昇順）。</param>
/// <param name="Source">実際に使った路。</param>
/// <param name="Elapsed">所要。</param>
/// <param name="FailureReason">1 台も読めなかった理由 1 行。</param>
public sealed record GpuEnumerationResult(
    IReadOnlyList<GpuInfo> Gpus,
    GpuSource Source,
    TimeSpan Elapsed,
    string? FailureReason);

/// <summary>
/// 契約 ⑹＝GPU 列挙（設計書 §3）。
/// <b>① <c>nvidia-smi</c>（速い・NVIDIA だけ）→ ② 変種の python で torch 列挙 1 回</b>の順に試す。
/// 保存は UUID・起動時に index へ解決する。
/// </summary>
public interface IGpuEnumerator
{
    Task<GpuEnumerationResult> EnumerateAsync(
        GpuEnumerationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// 保存した UUID を index へ解決する（<b>純関数</b>＝起動のたびに走る一手）。
/// </summary>
public static class GpuResolver
{
    /// <summary>
    /// UUID に合う GPU を探す。大小は無視し、<c>GPU-</c> の前置の有無も吸収する
    /// （<c>nvidia-smi</c> は <c>GPU-xxxx</c>・torch は前置なしを返しうる）。
    /// </summary>
    public static GpuInfo? Find(IReadOnlyList<GpuInfo> gpus, string? uuid)
    {
        ArgumentNullException.ThrowIfNull(gpus);
        if (string.IsNullOrWhiteSpace(uuid))
        {
            return null;
        }

        var want = NormalizeUuid(uuid);
        return gpus.FirstOrDefault(g => NormalizeUuid(g.Uuid) == want);
    }

    /// <summary><c>cuda:N</c> の N。見つからなければ null＝「前回の GPU が見つからない」。</summary>
    public static int? ResolveIndex(IReadOnlyList<GpuInfo> gpus, string? uuid) =>
        Find(gpus, uuid)?.Index;

    /// <summary>比較用の形（<c>GPU-</c> を剥ぎ小文字に）。</summary>
    public static string NormalizeUuid(string uuid)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        var text = uuid.Trim();
        if (text.StartsWith("GPU-", StringComparison.OrdinalIgnoreCase))
        {
            text = text[4..];
        }

        return text.ToLowerInvariant();
    }

    /// <summary>
    /// 2 つの UUID が同じ個体を指すか（<b>純関数</b>）。どちらかが空なら偽＝
    /// 「まだ選んでいない」と「選んである」を同じ物として扱わない。
    /// </summary>
    public static bool SameUuid(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return NormalizeUuid(left) == NormalizeUuid(right);
    }

    /// <summary>見つからないときに UI へ出す 1 行（設計書 §3）。</summary>
    public static string NotFoundMessage(string? savedName, string? savedUuid)
    {
        var name = string.IsNullOrWhiteSpace(savedName) ? "前回の GPU" : savedName.Trim();
        var uuid = string.IsNullOrWhiteSpace(savedUuid) ? string.Empty : "（" + savedUuid.Trim() + "）";
        return name + uuid + " が見つかりません。設定で GPU を選び直してください。";
    }
}

/// <summary>ドライバ検査の結末。</summary>
/// <param name="Ok">その変種を走らせてよいか。</param>
/// <param name="Unknown">ドライバ版が読めなかった（AMD 機など）＝止めはしないが名乗る。</param>
/// <param name="RequiredMinimum">その変種の下限（<c>580.00</c>／<c>528.33</c>＝裁定 88 ⑵）。</param>
/// <param name="Message">UI に出す 1 行。</param>
/// <param name="SuggestedVariant">下限に届かないときに勧める変種（自動切替はしない＝裁定 4）。</param>
public sealed record DriverVerdict(
    bool Ok,
    bool Unknown,
    string? RequiredMinimum,
    string Message,
    string? SuggestedVariant);

/// <summary>契約 ⑹＝ドライバ検査（不足なら合成を撃たずに告知する＝受け入れ条件の「ドライバ」行）。</summary>
public interface IDriverCheck
{
    DriverVerdict Check(string variant, string? driverVersion);
}

/// <summary>
/// ドライバの下限（裁定 88 ⑵・設計書 §3）。<b>純関数</b>。
/// <para>
/// <b>cu130 ≥ 580.00／cu126 ≥ 528.33</b>。便 B の U-14（RTX 3090 をドライバ 537.58 へ降格して実射）で
/// <c>torch 2.10.0+cu126</c> が <c>is_available=True</c>・<c>device_count=1</c> で通り、同じドライバで
/// cu130 は <c>cudaErrorNotSupported</c> だったことが判った（裁定 80）。よって cu126 の下限は
/// NVIDIA の CUDA 12.x minor version compatibility の Windows 下限 <b>528.33</b>（<b>未実測</b>）とし、
/// <b>528.33 ≤ v &lt; 537.58 は「未実測の帯」</b>として合成は許す（1 行の注意を出す＝
/// <see cref="Services.Gpu.VariantGate"/>）。<b>ここが唯一の定義箇所</b>。
/// </para>
/// </summary>
public sealed class DriverRequirement : IDriverCheck
{
    /// <summary>cu130 の下限（裁定 88 ⑵）。</summary>
    public const string Cu130Minimum = "580.00";

    /// <summary>cu126 の下限（裁定 88 ⑵＝NVIDIA の Windows 下限・<b>未実測</b>）。</summary>
    public const string Cu126Minimum = "528.33";

    /// <summary>
    /// cu126 を<b>実射で通した</b>いちばん低いドライバ（裁定 80＝537.58・RTX 3090・2026-09-05）。
    /// <see cref="Cu126Minimum"/> との間は「未実測の帯」＝止めずに 1 行の注意を出す。
    /// </summary>
    public const string Cu126MeasuredMinimum = "537.58";

    /// <summary>
    /// その変種の下限（要らない変種は null）。
    /// <para>
    /// <b>綴りの受けは大小と前後の空白を無視する</b>（是正・便 D（2）＝1 巡目は定数パターンの
    /// <c>switch</c> で、同じ門の中の <see cref="Services.Gpu.VariantGate.IsUnmeasuredBand"/> が
    /// <c>OrdinalIgnoreCase</c> なのに<b>ここだけ厳密一致</b>だった＝
    /// <c>settings.json</c> に <c>"CU130"</c> と書かれた機体で閾が静かに外れる）。
    /// </para>
    /// <para>
    /// <b>畳んだ名 <c>cuda</c> は cu130 と同じ閾で見る</b>（是正・便 D（2））。env の
    /// <c>YWK_VARIANT</c> は cu130 と cu126 を <c>cuda</c> に畳むので、この名で来た要求は
    /// <b>どちらの実行系なのか判らない</b>。1 巡目はここが <c>null</c> を返し、
    /// <b>ドライバの閾も「未実測の帯」の 1 行も丸ごと落ちていた</b>（実射＝
    /// <c>Decide("cuda", usable, "500.00")</c> が <c>Allow=True</c>）。判らない以上は
    /// <b>厳しい方（cu130）</b>で見る＝裁定 83 の「cu130 の CPU 転落は禁止」を、
    /// 綴りが畳まれた経路でも守る。正しい綴りを載せたい呼び手は
    /// <see cref="Services.Server.ServerLaunchPlan.Build"/> を通すこと。
    /// </para>
    /// </summary>
    public static string? Minimum(string? variant)
    {
        var name = variant?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (string.Equals(name, RuntimeVariants.Cu130, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, RuntimeVariants.CudaLabel, StringComparison.OrdinalIgnoreCase))
        {
            return Cu130Minimum;
        }

        return string.Equals(name, RuntimeVariants.Cu126, StringComparison.OrdinalIgnoreCase)
            ? Cu126Minimum
            : null;
    }

    public DriverVerdict Check(string variant, string? driverVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        var minimum = Minimum(variant);
        if (minimum is null)
        {
            return new DriverVerdict(true, false, null, "ドライバの下限はありません。", null);
        }

        if (string.IsNullOrWhiteSpace(driverVersion))
        {
            return new DriverVerdict(
                true,
                true,
                minimum,
                "ドライバの版が読めませんでした（" + RuntimeVariants.DisplayName(variant)
                + " は " + minimum + " 以上が要ります）。",
                null);
        }

        if (!TryCompare(driverVersion, minimum, out var comparison))
        {
            return new DriverVerdict(
                true,
                true,
                minimum,
                "ドライバの版「" + driverVersion.Trim() + "」が読めませんでした。",
                null);
        }

        if (comparison >= 0)
        {
            return new DriverVerdict(true, false, minimum,
                "ドライバ " + driverVersion.Trim() + "（下限 " + minimum + "）。", null);
        }

        var suggested = variant == RuntimeVariants.Cu130 ? RuntimeVariants.Cu126 : null;
        var tail = suggested is null
            ? "ドライバを更新してください。"
            : RuntimeVariants.DisplayName(suggested) + " を選ぶか、ドライバを更新してください。";

        return new DriverVerdict(
            false,
            false,
            minimum,
            "ドライバ " + driverVersion.Trim() + " は " + RuntimeVariants.DisplayName(variant)
            + " の下限 " + minimum + " に届きません。" + tail,
            suggested);
    }

    /// <summary>
    /// <c>591.86</c> のような版を比べる（<b>純関数</b>）。
    /// 読めなければ偽（＝止めずに「読めなかった」と名乗る）。
    /// <para>
    /// <b>2 節の版（NVIDIA の Windows ドライバの形）は小数として比べる</b>（是正・便 D（2））。
    /// 1 巡目は「.」で割った<b>整数の列</b>として比べていたので、桁数が違う相手で順序が逆になった
    /// （実射＝<c>TryCompare("537.58", "537.6")</c> が <c>1</c>＝537.58 のほうが大きい。
    /// 実際の版としては <c>537.6</c>＝<c>537.60</c> のほうが新しい）。閾 <c>528.33</c>／
    /// <c>537.58</c>／<c>580.00</c> と比べる相手が 1 桁で来た日に、届いているドライバを
    /// 「下限に届かない」と誤判定して合成を止める形だった。
    /// 3 節以上（ドライバ版には無い形）は<b>従来どおり節ごとの整数</b>で比べる＝
    /// <c>10.0.1</c> と <c>10.0.10</c> を同じ物にしない。
    /// </para>
    /// </summary>
    public static bool TryCompare(string left, string right, out int result)
    {
        result = 0;
        var a = Parse(left);
        var b = Parse(right);
        if (a is null || b is null)
        {
            return false;
        }

        if (a.Count == 2 && b.Count == 2)
        {
            var integral = a[0].CompareTo(b[0]);
            if (integral != 0)
            {
                result = integral < 0 ? -1 : 1;
                return true;
            }

            // 小数点以下は桁を揃えてから比べる（58 対 6 ではなく 58 対 60）。
            var fractional = CompareFraction(Fraction(left), Fraction(right));
            result = fractional;
            return true;
        }

        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var x = i < a.Count ? a[i] : 0;
            var y = i < b.Count ? b[i] : 0;
            if (x != y)
            {
                result = x < y ? -1 : 1;
                return true;
            }
        }

        return true;
    }

    /// <summary>「.」の後ろの桁（2 節の版だけで使う）。</summary>
    private static string Fraction(string version)
    {
        var parts = version.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? parts[1] : string.Empty;
    }

    /// <summary>小数点以下を桁数を揃えて比べる（<c>58</c> 対 <c>6</c>＝<c>58</c> 対 <c>60</c>）。</summary>
    private static int CompareFraction(string left, string right)
    {
        var width = Math.Max(left.Length, right.Length);
        var a = left.PadRight(width, '0');
        var b = right.PadRight(width, '0');
        var comparison = string.CompareOrdinal(a, b);
        return comparison == 0 ? 0 : (comparison < 0 ? -1 : 1);
    }

    private static List<int>? Parse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var parts = version.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var values = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            values.Add(value);
        }

        return values;
    }
}
