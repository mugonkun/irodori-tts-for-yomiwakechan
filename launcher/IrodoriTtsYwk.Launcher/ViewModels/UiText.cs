using System;
using System.Globalization;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// 画面に出す文字列の作り方を 1 箇所に閉じる（<b>すべて純関数</b>＝5 つの View が同じ数字を
/// 別々の書式で出さないため）。UI は日本語のみ（裁定 52）。
/// <para>
/// 数の書式は <see cref="CultureInfo.InvariantCulture"/> で作り、単位と助詞だけを日本語にする。
/// 桁区切りに利用者の地域設定を混ぜると、同じ値がログと画面で違って見える。
/// </para>
/// </summary>
public static class UiText
{
    /// <summary>値が無いときに出す 1 文字（空欄と「0」を混同させない）。</summary>
    public const string Missing = "—";

    /// <summary>その口が wrapper に無いとき（<c>WrapperResult&lt;T&gt;.Available=false</c>）。</summary>
    public const string NotSupported = "未対応";

    /// <summary>バイト数（1024 進・GB／MB／KB）。</summary>
    public static string Bytes(long? bytes)
    {
        if (bytes is null || bytes < 0)
        {
            return Missing;
        }

        var value = (double)bytes.Value;
        if (value >= 1024.0 * 1024.0 * 1024.0)
        {
            return Fixed(value / (1024.0 * 1024.0 * 1024.0), 2) + " GB";
        }

        if (value >= 1024.0 * 1024.0)
        {
            return Fixed(value / (1024.0 * 1024.0), 1) + " MB";
        }

        if (value >= 1024.0)
        {
            return Fixed(value / 1024.0, 1) + " KB";
        }

        return bytes.Value.ToString(CultureInfo.InvariantCulture) + " B";
    }

    /// <summary>取得の速さ。</summary>
    public static string Rate(double bytesPerSecond)
    {
        if (double.IsNaN(bytesPerSecond) || bytesPerSecond <= 0)
        {
            return Missing;
        }

        return Bytes((long)bytesPerSecond) + "/s";
    }

    /// <summary>残り時間（<see cref="Contracts.DownloadProgress.EstimateEta"/> の表示形）。</summary>
    public static string Eta(TimeSpan? eta)
    {
        if (eta is null || eta.Value < TimeSpan.Zero)
        {
            return Missing;
        }

        var span = eta.Value;
        if (span.TotalHours >= 1)
        {
            return ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture) + " 時間 "
                + span.Minutes.ToString(CultureInfo.InvariantCulture) + " 分";
        }

        if (span.TotalMinutes >= 1)
        {
            return ((int)span.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " 分 "
                + span.Seconds.ToString(CultureInfo.InvariantCulture) + " 秒";
        }

        return Math.Max(0, (int)Math.Ceiling(span.TotalSeconds)).ToString(CultureInfo.InvariantCulture) + " 秒";
    }

    /// <summary>所要（試し撃ちの ms・起動の秒）。</summary>
    public static string Milliseconds(double? ms)
    {
        if (ms is null || double.IsNaN(ms.Value) || ms.Value < 0)
        {
            return Missing;
        }

        return ms.Value >= 1000
            ? Fixed(ms.Value / 1000.0, 2) + " 秒"
            : Fixed(ms.Value, 0) + " ms";
    }

    /// <summary>音の長さ（出力秒）。</summary>
    public static string Seconds(double? seconds)
    {
        if (seconds is null || double.IsNaN(seconds.Value) || seconds.Value < 0)
        {
            return Missing;
        }

        return Fixed(seconds.Value, 2) + " 秒";
    }

    /// <summary>
    /// 実時間比（所要 ÷ 出力尺）。1 未満なら実時間より速い（受け入れ条件 C-8 の物差し）。
    /// </summary>
    public static string RealTimeFactor(double? elapsedMs, double? outputSeconds)
    {
        if (elapsedMs is null || outputSeconds is null || outputSeconds.Value <= 0)
        {
            return Missing;
        }

        return "RTF " + Fixed(elapsedMs.Value / 1000.0 / outputSeconds.Value, 2);
    }

    /// <summary>
    /// GPU の UUID の<b>下 6 桁</b>（画面の帯に出す形＝裁定 34 の「同定は UUID」を、
    /// 全 36 字を出さずに人が見分けられる長さで示す）。<c>GPU-</c> の前置は剥ぐ。
    /// </summary>
    public static string UuidTail(string? uuid, int length = 6)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        if (string.IsNullOrWhiteSpace(uuid))
        {
            return Missing;
        }

        var text = uuid.Trim();
        if (text.StartsWith("GPU-", StringComparison.OrdinalIgnoreCase))
        {
            text = text[4..];
        }

        text = text.Replace("-", string.Empty, StringComparison.Ordinal);
        if (text.Length == 0)
        {
            return Missing;
        }

        return text.Length <= length ? text : text[^length..];
    }

    /// <summary>0〜1 の割合を百分率に。</summary>
    public static string Percent(double? fraction)
    {
        if (fraction is null || double.IsNaN(fraction.Value))
        {
            return Missing;
        }

        return Fixed(Math.Clamp(fraction.Value, 0, 1) * 100.0, 1) + " %";
    }

    /// <summary>件数の 1 行（<c>3 / 6</c>）。</summary>
    public static string Progress(int? done, int? total)
    {
        if (done is null && total is null)
        {
            return Missing;
        }

        return (done ?? 0).ToString(CultureInfo.InvariantCulture)
            + " / " + (total?.ToString(CultureInfo.InvariantCulture) ?? Missing);
    }

    private static string Fixed(double value, int decimals) =>
        value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
