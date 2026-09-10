using System;
using System.Linq;
using System.Reflection;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// <b>1 本の進捗バー</b>（v2.0 段 B-3・`v2-spec.md` §3 段 3）。
/// <para>
/// 見るのは 5 つ＝⑴ 段の境目で連続すること ⑵ 0 と 1 ⑶ 逆行しないこと
/// ⑷ 列挙に無い段でも直前の値のまま ⑸ その段の進みが範囲外でも 0〜1 に収まること。
/// </para>
/// <para>
/// <b>重みは見せ方であって実測ではない</b>＝ここは「段が変わってもバーが戻らない」ことだけを釘付けする。
/// </para>
/// </summary>
public sealed class FirstRunProgressTests
{
    /// <summary>働く 4 段の<b>進行の順</b>（＝列挙の順でもある）。</summary>
    private static readonly FirstRunStep[] Order =
    [
        FirstRunStep.Notices, FirstRunStep.Variant, FirstRunStep.Download,
        FirstRunStep.Install, FirstRunStep.Models, FirstRunStep.Start, FirstRunStep.Done,
    ];

    [Fact]
    public void 段の境目で連続する()
    {
        // 前の段の 1 と、次の段の 0 が同じ値＝バーが飛ばない・戻らない。
        Assert.Equal(
            FirstRunProgress.Overall(FirstRunStep.Download, 1),
            FirstRunProgress.Overall(FirstRunStep.Install, 0),
            10);

        Assert.Equal(
            FirstRunProgress.Overall(FirstRunStep.Install, 1),
            FirstRunProgress.Overall(FirstRunStep.Models, 0),
            10);

        Assert.Equal(
            FirstRunProgress.Overall(FirstRunStep.Models, 1),
            FirstRunProgress.Overall(FirstRunStep.Start, 0),
            10);

        Assert.Equal(
            FirstRunProgress.Overall(FirstRunStep.Start, 1),
            FirstRunProgress.Overall(FirstRunStep.Done, 0),
            10);
    }

    [Fact]
    public void 両端は0と1()
    {
        Assert.Equal(0, FirstRunProgress.Overall(FirstRunStep.Notices, 0));
        Assert.Equal(0, FirstRunProgress.Overall(FirstRunStep.Variant, 1));   // まだ 1 バイトも落ちていない
        Assert.Equal(0, FirstRunProgress.Overall(FirstRunStep.Download, 0));
        Assert.Equal(1, FirstRunProgress.Overall(FirstRunStep.Done, 0));

        // 取り分の総和は 1（55 + 10 + 30 + 5）。
        Assert.Equal(
            1,
            FirstRunProgress.DownloadWeight + FirstRunProgress.InstallWeight
                + FirstRunProgress.ModelsWeight + FirstRunProgress.StartWeight,
            10);
    }

    [Fact]
    public void 逆行しない()
    {
        var previous = -1.0;
        foreach (var step in Order)
        {
            foreach (var fraction in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
            {
                var value = FirstRunProgress.Overall(step, fraction);
                Assert.True(
                    value >= previous,
                    step + " の " + fraction + " で戻った：" + previous + " → " + value);
                previous = value;
            }
        }

        Assert.Equal(1, previous);
    }

    [Fact]
    public void 列挙に無い段は直前の値のまま()
    {
        // 先＝完了の値・手前＝始まりの値（＝バーが勝手に飛ばない）。
        Assert.Equal(1, FirstRunProgress.Overall((FirstRunStep)99, 0));
        Assert.Equal(0, FirstRunProgress.Overall((FirstRunStep)(-1), 1));
    }

    [Fact]
    public void 段の進みが範囲外でも0から1に収まる()
    {
        foreach (var step in Order)
        {
            foreach (var fraction in new[] { -5.0, 12.0, double.NaN })
            {
                var value = FirstRunProgress.Overall(step, fraction);
                Assert.InRange(value, 0, 1);
            }
        }

        Assert.Equal(0, FirstRunProgress.Overall(FirstRunStep.Download, -1));
        Assert.Equal(FirstRunProgress.DownloadWeight, FirstRunProgress.Overall(FirstRunStep.Download, 9), 10);
    }

    [Fact]
    public void 進捗の1行は割合と丸めた残りだけを出す()
    {
        Assert.Equal("42 %　あと 6 分ほど", FirstRunProgress.Line(0.42, TimeSpan.FromMinutes(5.5)));
        Assert.Equal("0 %", FirstRunProgress.Line(0, null));                       // 読めない残りは黙る
        Assert.Equal("100 %　まもなく終わります", FirstRunProgress.Line(1, TimeSpan.FromSeconds(20)));
        Assert.Equal("50 %　あと 2 時間ほど", FirstRunProgress.Line(0.5, TimeSpan.FromMinutes(150)));

        // 桁の躍る秒・現実に無い長さは出さない。
        Assert.Null(FirstRunProgress.Remaining(TimeSpan.FromDays(3)));
        Assert.Null(FirstRunProgress.Remaining(null));
    }

    /// <summary>
    /// <b>利用者向けの面に出す数は 3 つだけ</b>（`v2-spec.md` §1-3・是正・検分）＝
    /// ⑴ 割合 ⑵ 丸めた残り ⑶ 散文の総量（<c>約 5.3 GB</c>＝憲章 §4 の 1 枚表の定数）。
    /// <b>実測を綴る入口をここに足さない</b>（1 巡目の <c>Gb(long)</c> は削った）＝実数は
    /// <see cref="UiText.Bytes"/>（<c>GiB</c>）のまま、置き場は詳細の中に限る。
    /// </summary>
    [Fact]
    public void 実測のバイト数を綴る入口をここに持たない()
    {
        var entries = typeof(FirstRunProgress)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Line", "Overall", "Remaining"], entries);

        // 散文の総量を綴るのは文言の定数 1 箇所だけ（席が数を作らない）。
        Assert.Contains("約 5.3 GB", FirstRunViewModel.PlanLine, StringComparison.Ordinal);
    }
}
