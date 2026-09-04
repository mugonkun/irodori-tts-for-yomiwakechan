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
/// <param name="RequiredMinimum">その変種の下限（<c>580</c>／<c>560.76</c>）。</param>
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
/// ドライバの下限（決定 4・設計書 §3）。<b>純関数</b>。
/// <para>
/// cu130 ≥ 580／cu126 ≥ 560.76。cu126 の下限の謳い方は便 B の U-14（RTX 機でドライバを
/// 2023 年秋の版に落として実射）の結果で更新する＝<b>ここが唯一の定義箇所</b>。
/// </para>
/// </summary>
public sealed class DriverRequirement : IDriverCheck
{
    /// <summary>cu130 の下限。</summary>
    public const string Cu130Minimum = "580";

    /// <summary>cu126 の下限（便 B の実射で更新しうる）。</summary>
    public const string Cu126Minimum = "560.76";

    /// <summary>その変種の下限（要らない変種は null）。</summary>
    public static string? Minimum(string variant) => variant switch
    {
        RuntimeVariants.Cu130 => Cu130Minimum,
        RuntimeVariants.Cu126 => Cu126Minimum,
        _ => null,
    };

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
    /// <c>591.86</c> のような版を数の列として比べる（<b>純関数</b>）。
    /// 読めなければ偽（＝止めずに「読めなかった」と名乗る）。
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
