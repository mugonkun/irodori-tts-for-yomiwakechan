using System;
using System.Globalization;
using System.Windows.Data;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Views;

/// <summary>
/// 動かし方の一覧の行に<b>世間の語</b>を出す（<b>値そのものは 1 字も変えない</b>）。
/// <para>
/// <b>なぜ要るか</b>（是正・段 G・high 14）＝<c>SettingsVariantCombo</c> と
/// <c>FirstRunVariantCombo</c> は <c>ItemsSource</c> に
/// <see cref="RuntimeVariants.CudaReleaseChoices"/>／<see cref="RuntimeVariants.RadeonReleaseChoices"/>
/// ＝<b>台帳の生の綴り</b>を直に載せている。<c>DisplayMemberPath</c> も変換も無かったので、
/// 一覧の行には <c>cu130</c>／<c>cu126</c>／<c>cpu</c>／<c>rocm-gfx1151</c> がそのまま出ていた
/// ＝憲章 §6-1（変種の綴りは出さない）・附録 5（<c>gfx1151</c> の綴りだけを落とす）・
/// `v2-copy.md` §9-2（cu126／cu130 はどこにも出していない）に反し、
/// `v2-spec.md` §2-5 の 2 行目が定めた表示（CUDA 13.0／CUDA 12.6／ROCm／CPU）とも食い違う。
/// </para>
/// <para>
/// <b>直すのは見た目だけ</b>（憲章 §6-3）＝<c>ItemTemplate</c> に噛ませるので
/// <c>SelectedItem</c> は今までどおり<b>台帳の綴りの文字列</b>のままであり、
/// <c>settings.json</c> の <c>variant</c> も <c>AutomationId</c> も 1 字も動かない。
/// <b>綴りの正本は 2 つに分かれている</b>（是正・検分・v2.0.1）＝
/// <b>一覧の行の札</b>は <see cref="RuntimeVariants.ShortDisplayName"/>（＝下の
/// <see cref="Display"/>・4 語）、<b>一覧の下の 1 行</b>（<c>SettingsVariantNameText</c>／
/// <c>FirstRunVariantNameText</c>＝<c>VariantDisplayName</c>）は
/// <see cref="RuntimeVariants.DisplayName"/>（＝下限や注記を抱えた長い方）である。
/// どちらも <c>RuntimeVariants</c> の 1 箇所ずつで、この器は綴りを持たない。
/// </para>
/// </summary>
public sealed class VariantDisplayConverter : IValueConverter
{
    /// <summary>
    /// <b>札の綴りの正本</b>（<b>純関数</b>＝XAML を組まずに釘付けできる・決裁 135 ⑶）。
    /// <para>
    /// <b>短い名を使う</b>（是正・v2.0.1）＝<see cref="RuntimeVariants.DisplayName"/> は
    /// 括弧に下限（<c>ドライバ 580.00 以上</c>）や注記を抱えているので、
    /// `v2-spec.md` §2-5 の 2 行目が定めた一覧の表示（<b>CUDA 13.0／CUDA 12.6／ROCm／CPU</b>）と
    /// 食い違う。下限と注記は一覧の<b>下の 1 行</b>（<c>SettingsVariantNameText</c>／
    /// <c>FirstRunVariantNameText</c>＝<c>VariantDisplayName</c>）が今までどおり名乗る。
    /// </para>
    /// </summary>
    public static string Display(object? value) =>
        value is string variant && variant.Trim().Length > 0
            ? RuntimeVariants.ShortDisplayName(variant.Trim())
            : string.Empty;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Display(value);

    /// <summary>戻す道は無い（<c>ItemTemplate</c> の片道である）。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
