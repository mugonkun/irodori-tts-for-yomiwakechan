using System;
using System.Collections.Generic;
using System.Globalization;
using IrodoriTtsYwk.Launcher.Services.Gpu;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// 状態の帯の下に出す <b>GPU メモリのメーター</b>（司令官の指示 2026-09-13）。
/// <para>
/// リソース モニターの 1 本の棒と同じ読み方＝左から<b>このアプリの占有</b>・
/// <b>ほかのプログラムの使用</b>・<b>空き</b>の 3 区画で、3 つの割合を足すと必ず 1 になる。
/// 元の数は OS の計数（裁定 110＝<see cref="OsGpuMemoryRow"/>）なので、
/// <b>RTX（CUDA）でも Radeon（ROCm）でも同じ道で読める</b>（torch の口に依らない）。
/// </para>
/// <para>
/// <b>純関数で組む</b>（<see cref="Describe"/>）＝窓なしで試せる。数が読めない回は
/// 棒を空（全部が空き色）にし、文は「—」にする＝<b>推測の数を出さない</b>。
/// </para>
/// </summary>
/// <param name="AppFraction">このアプリ（TTS のプロセス）の占有の割合（0〜1）。</param>
/// <param name="OthersFraction">ほかのプログラムの使用の割合（0〜1）。</param>
/// <param name="FreeFraction">空きの割合（0〜1）。3 つを足すと 1。</param>
/// <param name="Text">棒の脇の 1 行＝<c>このアプリ x／使用中 y（p%）／全体 z</c>。</param>
/// <param name="HasData">総量まで読めているか（偽＝棒は空で文は「—」）。</param>
public sealed record GpuMeter(
    double AppFraction,
    double OthersFraction,
    double FreeFraction,
    string Text,
    bool HasData)
{
    /// <summary>何も読めていない回（止まっている・計数が採れない機体）。</summary>
    public static readonly GpuMeter Empty = new(0, 0, 1, UiText.Missing, false);

    /// <summary>
    /// OS の計数の行からメーターを組む（<b>純関数</b>）。
    /// <para>
    /// GPU が 2 枚以上の個体は<b>このアプリがいちばん多く載っている 1 枚</b>を選ぶ
    /// （模型を載せた GPU＝利用者が見たい 1 枚）。同点なら先頭。2 枚以上のときだけ名前を頭に立てる。
    /// </para>
    /// <para>
    /// 「使用中」は<b>その GPU の専用メモリの占有（ほかのプログラム込み）</b>で、共有メモリは
    /// 入らない（裁定 110）。占有が読めない行は、このアプリの分だけを使用中と読む
    /// （<b>読めない数を 0 と混ぜない</b>＝ほかの区画を出さない）。
    /// </para>
    /// </summary>
    public static GpuMeter Describe(IReadOnlyList<OsGpuMemoryRow>? rows)
    {
        if (rows is null || rows.Count == 0)
        {
            return Empty;
        }

        OsGpuMemoryRow? pick = null;
        foreach (var row in rows)
        {
            if (row is not null && (pick is null || row.ProcessBytes > pick.ProcessBytes))
            {
                pick = row;
            }
        }

        if (pick is null)
        {
            return Empty;
        }

        var head = rows.Count > 1 ? pick.Name + "＝" : string.Empty;
        var app = Math.Max(0, pick.ProcessBytes);

        if (pick.TotalBytes is not long total || total <= 0)
        {
            // 総量が読めない（DXGI が名を返さなかった等）＝割合は出せない。数だけ 1 行に。
            return new GpuMeter(
                0, 0, 1,
                head + UiStrings.GpuMeterApp + " " + UiText.Bytes(app)
                    + "／" + UiStrings.GpuMeterTotal + " " + UiText.Missing,
                false);
        }

        app = Math.Min(app, total);
        var used = pick.AdapterBytes is long adapter ? Math.Clamp(adapter, app, total) : app;
        var others = used - app;
        var free = total - used;

        var percent = (int)Math.Round(100.0 * used / total, MidpointRounding.AwayFromZero);
        var text = head
            + UiStrings.GpuMeterApp + " " + UiText.Bytes(app)
            + "／" + UiStrings.GpuMeterUsed + " " + UiText.Bytes(used)
            + "（" + percent.ToString(CultureInfo.InvariantCulture) + "%）"
            + "／" + UiStrings.GpuMeterTotal + " " + UiText.Bytes(total);

        return new GpuMeter(
            (double)app / total,
            (double)others / total,
            (double)free / total,
            text,
            true);
    }
}
