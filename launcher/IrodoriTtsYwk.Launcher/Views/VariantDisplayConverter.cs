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
/// 札の綴りの正本は <see cref="RuntimeVariants.DisplayName"/> ただ 1 箇所である
/// （すぐ下の <c>SettingsVariantNameText</c>／<c>FirstRunVariantNameText</c> と同じ物）。
/// </para>
/// </summary>
public sealed class VariantDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string variant && variant.Length > 0
            ? RuntimeVariants.DisplayName(variant)
            : string.Empty;

    /// <summary>戻す道は無い（<c>ItemTemplate</c> の片道である）。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
