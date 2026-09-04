using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IrodoriTtsYwk.Launcher.Mvvm;

/// <summary>
/// <see cref="INotifyPropertyChanged"/> の土台（<b>自前・依存追加なし</b>＝依頼文の作法）。
/// <para>
/// WPF の型に一切触れないので、ViewModel は xUnit から素で作れる（テストの継ぎ目は
/// public コンストラクタ＝本体 yomiwakechan2 の流儀）。窓へ配るための Dispatcher も持たない＝
/// <b>スレッドを跨ぐ marshal は View の仕事</b>（View が <c>Dispatcher.BeginInvoke</c> で
/// ViewModel のメソッドを呼ぶ）。ここに Dispatcher を入れると ViewModel が STA を要求し、
/// 純ロジックのテストが書けなくなる。
/// </para>
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>値が変わったときだけ通知する（変わらなければ偽を返す）。</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    /// <summary>派生の計算プロパティを手で叩く口。</summary>
    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
