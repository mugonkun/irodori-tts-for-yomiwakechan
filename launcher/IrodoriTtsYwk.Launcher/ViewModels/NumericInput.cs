using System.Globalization;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// 「空欄＝既定に戻す」を数の入力欄で表すための往復（<b>純関数</b>）。
/// <para>
/// 契約 ⑶ の「<b>既定に戻すは欄を出さないことで表す</b>」を UI の側で実現する 1 箇所である。
/// 0 と空欄を混同させないため、<b>空欄は null</b>（欄を送らない）・<b>0 は 0</b>（送る）と
/// 厳しく分ける。読みは <see cref="CultureInfo.InvariantCulture"/>（利用者の地域設定で
/// 小数点が変わっても、送る JSON は変わらない）。
/// </para>
/// </summary>
public static class NumericInput
{
    /// <summary>空欄＝null、読めれば値、読めなければ偽（理由 1 行）。</summary>
    public static bool TryParseOptional(string? text, out double? value, out string? error)
    {
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && !double.IsNaN(parsed) && !double.IsInfinity(parsed))
        {
            value = parsed;
            return true;
        }

        error = "「" + text.Trim() + "」は数として読めません。";
        return false;
    }

    /// <summary>整数版（歩数・empty_cache_interval）。</summary>
    public static bool TryParseOptionalInt(string? text, out int? value, out string? error)
    {
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        error = "「" + text.Trim() + "」は整数として読めません。";
        return false;
    }

    /// <summary>null は空欄に（<b>0 を空欄にしない</b>）。</summary>
    public static string Format(double? value) =>
        value is null ? string.Empty : value.Value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>同上（整数）。</summary>
    public static string Format(int? value) =>
        value is null ? string.Empty : value.Value.ToString(CultureInfo.InvariantCulture);
}
