using System;
using System.Globalization;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// <b>はじめの準備の 1 本のバー</b>（`v2-spec.md` §3 段 3・`v2-plan.md` 段 B-3・<b>純関数だけ</b>）。
/// <para>
/// <b>なぜ要るか</b>＝働く 4 段（取得→展開→モデル→起動）はそれぞれ 0〜1 の進みを持つので、
/// 段が変わるたびにバーが 0 へ戻る＝利用者からは「進んでいない」ように見える。ここは
/// <b>4 段を 1 本に合成する重み</b>だけを持ち、状態は 1 つも持たない。
/// </para>
/// <para>
/// <b>重みは見せ方であって実測ではない</b>（`v2-plan.md` 段 B-3 の危険 ⑶）＝
/// 取得 .55／モデル .30／展開 .10／起動 .05。憲章にこの数は書かない。
/// </para>
/// </summary>
public static class FirstRunProgress
{
    /// <summary>取得（動かすための一式）の取り分。</summary>
    public const double DownloadWeight = 0.55;

    /// <summary>展開（落とした物を組み立てる）の取り分。</summary>
    public const double InstallWeight = 0.10;

    /// <summary>モデル（声のデータ）の取り分。</summary>
    public const double ModelsWeight = 0.30;

    /// <summary>起動の確認の取り分。</summary>
    public const double StartWeight = 0.05;

    /// <summary>
    /// 段とその段の進み（0〜1）から、<b>全体の進み</b>（0〜1）を作る（<b>純関数</b>）。
    /// <list type="bullet">
    /// <item>通知・確認＝まだ 1 バイトも落としていない＝<c>0</c>。</item>
    /// <item>働く 4 段＝<b>並びの順に</b>取り分を積む（取得 0〜.55・展開 .55〜.65・モデル .65〜.95・起動 .95〜1）。</item>
    /// <item>完了＝<c>1</c>。</item>
    /// <item><b>列挙に無い値</b>（前後の外）＝手前は 0・先は 1 に丸める＝<b>逆行しない</b>。</item>
    /// </list>
    /// <paramref name="fraction"/> が範囲外・NaN でも戻りは必ず 0〜1 に収まる。
    /// </summary>
    public static double Overall(FirstRunStep step, double fraction)
    {
        var f = double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0, 1);

        return step switch
        {
            FirstRunStep.Notices or FirstRunStep.Variant => 0,
            FirstRunStep.Download => Clamp(f * DownloadWeight),
            FirstRunStep.Install => Clamp(DownloadWeight + (f * InstallWeight)),
            FirstRunStep.Models => Clamp(DownloadWeight + InstallWeight + (f * ModelsWeight)),
            FirstRunStep.Start => Clamp(DownloadWeight + InstallWeight + ModelsWeight + (f * StartWeight)),
            FirstRunStep.Done => 1,

            // 列挙に無い値＝知らない段。**手前なら 0・先なら 1**（＝直前に出した値のまま）。
            _ => (int)step < (int)FirstRunStep.Notices ? 0 : 1,
        };
    }

    /// <summary>
    /// 進捗の 1 行（`v2-copy.md` §2 段 3＝<c>42 %　あと 6 分ほど</c>・<b>純関数</b>）。
    /// <para>
    /// <b>実数の内訳は出さない</b>（`v2-spec.md` §1-3）＝速さ・件数・バイト数は詳細の中に置く。
    /// 残りが読めない回は割合だけを出す（推測の分数を書かない）。
    /// </para>
    /// </summary>
    public static string Line(double overall, TimeSpan? eta)
    {
        var percent = (int)Math.Round(Math.Clamp(overall, 0, 1) * 100, MidpointRounding.AwayFromZero);
        var text = percent.ToString(CultureInfo.InvariantCulture) + " %";
        return Remaining(eta) is string remaining ? text + "　" + remaining : text;
    }

    /// <summary>
    /// 残りの<b>丸めた散文</b>（読めなければ null＝黙る・<b>純関数</b>）。
    /// 1 分未満は数を出さない＝秒の数字が 1 秒ごとに躍るのを利用者に見せない。
    /// </summary>
    public static string? Remaining(TimeSpan? eta)
    {
        if (eta is not TimeSpan left || left < TimeSpan.Zero || left > TimeSpan.FromHours(12))
        {
            return null;
        }

        if (left < TimeSpan.FromMinutes(1))
        {
            return "まもなく終わります";
        }

        if (left < TimeSpan.FromHours(1))
        {
            var minutes = (int)Math.Ceiling(left.TotalMinutes);
            return "あと " + minutes.ToString(CultureInfo.InvariantCulture) + " 分ほど";
        }

        var hours = (int)Math.Floor(left.TotalHours);
        return "あと " + hours.ToString(CultureInfo.InvariantCulture) + " 時間ほど";
    }

    // **表示用のバイト数の入口は足さない**（是正・検分＝`v2-spec.md` §1-3）。
    // 1 巡目はここに `Gb(long)`（1024 進を `GB` と綴る）を置き、「いま何をしているか」の 1 行へ
    // 「（3.2 GB / 5.3 GB・残り 4 分）」を添えていた。それは §1-3 が名指しで禁じた形＝
    // 「表示用の別入口を足すと**その欠陥に戻る**」であり、利用者向けの面に出してよい数は
    // ⑴ 割合 ⑵ 丸めた残り ⑶ 散文の総量（約 5.3 GB＝`FirstRunViewModel.PlanLine` の定数）の 3 つだけ。
    // 実測の内訳は `UiText.Bytes`（`GiB`）のまま、置き場は詳細の中に限る。

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);
}
