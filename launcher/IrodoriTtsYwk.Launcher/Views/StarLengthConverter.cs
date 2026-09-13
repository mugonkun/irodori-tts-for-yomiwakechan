using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IrodoriTtsYwk.Launcher.Views;

/// <summary>
/// 割合（0〜1 の <see cref="double"/>）を <c>Grid</c> の星幅（<c>n*</c>）に読み替える
/// （GPU メモリのメーター＝<c>MainWindow.xaml</c>）。
/// <para>
/// 3 列の星幅を「このアプリ／ほか／空き」の割合にすると、列の幅がそのまま棒の区画になる＝
/// 描画の寸法を自分で計らない（窓の幅が変わっても WPF が割り直す）。
/// 負・NaN は 0 と読む。0 の星幅は WPF が幅 0 に畳む（3 つとも 0 にはしない＝
/// <see cref="ViewModels.GpuMeter.Empty"/> は空きを 1 にしている）。
/// </para>
/// </summary>
public sealed class StarLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var fraction = value is double d && !double.IsNaN(d) && d > 0 ? d : 0.0;
        return new GridLength(fraction, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
