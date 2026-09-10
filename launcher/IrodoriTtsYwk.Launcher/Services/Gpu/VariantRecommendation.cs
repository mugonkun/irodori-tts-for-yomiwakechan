using System;
using System.Collections.Generic;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Gpu;

/// <summary>ドライバの検分の結果（<see cref="VariantRecommendation"/> の入力）。</summary>
/// <param name="DriverVersion"><c>nvidia-smi</c> の <c>driver_version</c>（読めなければ null）。</param>
/// <param name="GpuCount">列挙で見えた GPU の台数（0＝1 台も見えなかった）。</param>
/// <param name="Probed">列挙を<b>実際に撃ったか</b>（偽＝まだ撃っていない・落ちた＝「判らない」）。</param>
/// <param name="FailureReason">
/// 列挙が<b>1 台も返せなかった理由</b> 1 行（<see cref="Contracts.GpuEnumerationResult.FailureReason"/> の写し・
/// 読めたなら null）。<b>要る理由</b>（是正・検分）＝「0 台」には
/// ⑴ 本当に GPU が無い ⑵ <c>nvidia-smi</c> が無い・落ちた・期限切れ、の 2 つが混ざる。
/// 区別せずに ⑵ を「GPU 無し」と読むと、NVIDIA の機体に CPU 版（数百分の一の速さ）を
/// 勧めてしまう。<b>理由が在る 0 台は「判らない」側</b>に倒す。
/// </param>
public sealed record DriverProbe(
    string? DriverVersion, int GpuCount, bool Probed, string? FailureReason = null)
{
    /// <summary>まだ何も見ていない（ウィザードを開いた直後の値）。</summary>
    public static readonly DriverProbe Unknown = new(null, 0, false);
}

/// <summary>
/// <b>ドライバの帯から変種を勧め、下限に届かない変種を選ばせない</b>（裁定 126 の B・<b>純関数</b>）。
/// <para>
/// <b>なぜ要るか</b>（司令官の実射・2026-09-10＝RTX 3090・ドライバ 537.58・v1.0.2 の清潔導入）＝
/// ウィザードは <c>VariantChoices[0]</c>（＝<c>cu130</c>）を既定で選んだままにし、ドライバ検査は
/// <c>DriverText</c> の 1 行を書くだけだったので「この構成で取得を始める」がそのまま押せた。
/// 取得は最後まで通るのに、<see cref="VariantGate"/> は起動の時点で cu130 を断る＝
/// <b>起こせない実行系を数 GB 落とさせて終わる</b>。⇒ 選ばせる前に落とす。
/// </para>
/// <para>
/// <b>閾の定義は増やさない</b>＝下限は <see cref="DriverRequirement"/> の 1 箇所（cu130 ≥ 580.00・
/// cu126 ≥ 528.33）から読み、門（<see cref="VariantGate"/>）と同じ数字で判断する。
/// <c>cpu</c> は<b>いつでも選べる</b>（GPU を見ない）。
/// </para>
/// </summary>
public static class VariantRecommendation
{
    /// <summary>
    /// その変種が<b>このドライバでは動かない</b>か（<b>純関数</b>）。
    /// <para>
    /// 真になるのは⑴ 下限が定義された変種で ⑵ ドライバの版が<b>読めて</b> ⑶ 下限に届かないとき<b>だけ</b>。
    /// 版が読めない機体（AMD・<c>nvidia-smi</c> が無い）では<b>止めない</b>＝「見ていない」を
    /// 「駄目」の側に倒さない（読めなかったことは 1 行で名乗る＝<see cref="UnknownDriverNote"/>）。
    /// </para>
    /// </summary>
    public static bool IsBelowMinimum(string? variant, string? driverVersion)
    {
        var minimum = DriverRequirement.Minimum(variant);
        return minimum is not null
               && !string.IsNullOrWhiteSpace(driverVersion)
               && DriverRequirement.TryCompare(driverVersion, minimum, out var comparison)
               && comparison < 0;
    }

    /// <summary>
    /// <b>この配布物で勧める変種</b>（<b>純関数</b>）。
    /// <list type="number">
    /// <item>ROCm 版の配布樹（選択肢に <c>rocm-*</c> が在る）＝<c>rocm-*</c>。</item>
    /// <item>ドライバが読めた＝<c>580.00</c> 以上なら <c>cu130</c>・<c>528.33</c> 以上なら <c>cu126</c>・
    /// それ未満なら <c>cpu</c>（GPU では動かない）。</item>
    /// <item>ドライバが読めず、<b>列挙を撃って GPU が 1 台も見えず、しかも
    /// <see cref="DriverProbe.FailureReason"/> が無い</b>（＝「見た上で 0 台」）＝<c>cpu</c>。</item>
    /// <item>それ以外（まだ撃っていない・落ちた・<b>理由つきの 0 台</b>）＝<b>並びの先頭のまま</b>＝
    /// 判らないことを勝手に決めない。</item>
    /// </list>
    /// </summary>
    public static string Recommend(IReadOnlyList<string> choices, DriverProbe probe)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(probe);
        if (choices.Count == 0)
        {
            return RuntimeVariants.Cpu;
        }

        if (Has(choices, RuntimeVariants.RocmGfx1151))
        {
            return RuntimeVariants.RocmGfx1151;
        }

        if (!string.IsNullOrWhiteSpace(probe.DriverVersion))
        {
            if (Has(choices, RuntimeVariants.Cu130)
                && !IsBelowMinimum(RuntimeVariants.Cu130, probe.DriverVersion))
            {
                return RuntimeVariants.Cu130;
            }

            if (Has(choices, RuntimeVariants.Cu126)
                && !IsBelowMinimum(RuntimeVariants.Cu126, probe.DriverVersion))
            {
                return RuntimeVariants.Cu126;
            }

            return Has(choices, RuntimeVariants.Cpu) ? RuntimeVariants.Cpu : choices[0];
        }

        // **「見た上で 0 台」だけを GPU 無しと読む**（是正・検分）＝初回取得の時点では実行系がまだ
        // 無く（`ResolvePythonExe` は null）、列挙は nvidia-smi しか試せない。それが無い・落ちた・
        // 期限切れの回も 0 台で返る（理由 1 行つき）ので、理由を見ずに cpu へ倒すと
        // **NVIDIA の機体が黙って CPU 版（数百分の一の速さ）を落とす**。
        if (probe.Probed && probe.GpuCount == 0 && probe.FailureReason is null
            && Has(choices, RuntimeVariants.Cpu))
        {
            return RuntimeVariants.Cpu;
        }

        return choices[0];
    }

    /// <summary>
    /// 選べない理由の 1 行（<b>純関数</b>・選べるなら null）。
    /// 例＝<c>このドライバ（537.58）では CUDA 13.0 は動きません。CUDA 12.6 を選んでください。</c>
    /// </summary>
    public static string? BlockReason(
        IReadOnlyList<string> choices,
        string? variant,
        string? driverVersion)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (!IsBelowMinimum(variant, driverVersion))
        {
            return null;
        }

        var alternative = Recommend(choices, new DriverProbe(driverVersion, 1, true));
        return string.Equals(alternative, variant?.Trim(), StringComparison.OrdinalIgnoreCase)
            ? Head(variant!, driverVersion!) + "別の変種を選んでください。"
            : Head(variant!, driverVersion!) + RuntimeVariants.ShortDisplayName(alternative)
              + " を選んでください。";
    }

    /// <summary>
    /// <b>起動を断って取得へ導く 1 行</b>（裁定 126 ⑽・<b>純関数</b>・断る理由が無ければ null）。
    /// 例＝<c>このドライバ（537.58）では CUDA 13.0 は動きません（下限 580.00）。CUDA 12.6 に切り替えて取得します。</c>
    /// <para>
    /// <see cref="BlockReason"/> と<b>同じ頭</b>で、末尾だけが違う＝ウィザードの中では
    /// 「選んでください」（選ぶのは利用者）、主窓では「切り替えて取得します」
    /// （<b>ランチャがこれから連れて行く</b>＝失敗の理由だけを見せて放り出さない）。
    /// 閾も勧める先も <see cref="IsBelowMinimum"/>／<see cref="Recommend"/> の 1 箇所から読む。
    /// </para>
    /// </summary>
    /// <param name="choices">その配布物で取得できる変種の並び（ウィザードの一覧と同じ物を渡す）。</param>
    /// <param name="variant">設定に保存されている変種。</param>
    /// <param name="driverVersion">列挙で読めたドライバの版（読めなければ null＝断らない）。</param>
    public static string? StartRefusalReason(
        IReadOnlyList<string> choices,
        string? variant,
        string? driverVersion)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (!IsBelowMinimum(variant, driverVersion))
        {
            return null;
        }

        var alternative = Recommend(choices, new DriverProbe(driverVersion, 1, true));
        return string.Equals(alternative, variant?.Trim(), StringComparison.OrdinalIgnoreCase)
            ? Head(variant!, driverVersion!) + "取得からやり直して別の変種を選んでください。"
            : Head(variant!, driverVersion!) + RuntimeVariants.ShortDisplayName(alternative)
              + " に切り替えて取得します。";
    }

    /// <summary>断りの 1 行の頭（<b>純関数</b>）＝ドライバの版・断る変種・その下限。</summary>
    private static string Head(string variant, string driverVersion) =>
        "このドライバ（" + driverVersion.Trim() + "）では "
        + RuntimeVariants.ShortDisplayName(variant) + " は動きません（下限 "
        + DriverRequirement.Minimum(variant) + "）。";

    /// <summary>ドライバの版が読めなかったときの 1 行（<b>止めはしない</b>＝そう名乗るだけ）。</summary>
    public const string UnknownDriverNote =
        "ドライバの版が読めませんでした。この構成で進められますが、"
        + "起動できないときは CPU の変種を選んでください。";

    /// <summary>
    /// <b>見た上で GPU が 0 台だった</b>ときの 1 行（＝<see cref="Recommend"/> が <c>cpu</c> を選ぶ回）。
    /// 「CPU を選べ」と勧めない＝もう選んでいる（是正・検分）。
    /// </summary>
    public const string NoGpuNote =
        "GPU を 1 台も見つけられませんでした。CPU の変種を選んでいます"
        + "（GPU で動かすときは選び直してください）。";

    /// <summary>
    /// 検分の結果を 1 行で名乗る（<b>純関数</b>・名乗る物が無ければ null）。
    /// <para>
    /// ドライバの版が読めた回は黙る（<see cref="Contracts.DriverRequirement"/> の検分が語る）。
    /// 読めなかった回は⑴ 見た上で 0 台＝<see cref="NoGpuNote"/> ⑵ それ以外＝
    /// <see cref="UnknownDriverNote"/>（<b>列挙が返した理由も添える</b>＝捨てない）。
    /// </para>
    /// </summary>
    public static string? ProbeNote(DriverProbe? probe)
    {
        if (probe is null || !probe.Probed || !string.IsNullOrWhiteSpace(probe.DriverVersion))
        {
            return null;
        }

        if (probe.FailureReason is null)
        {
            return probe.GpuCount == 0 ? NoGpuNote : UnknownDriverNote;
        }

        return UnknownDriverNote + "（検分＝" + probe.FailureReason.Trim() + "）";
    }

    private static bool Has(IReadOnlyList<string> choices, string candidate)
    {
        foreach (var item in choices)
        {
            if (string.Equals(item, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
