using System.Windows;
using System.Windows.Controls;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Views;

/// <summary>
/// 状態帯（<see cref="StatusViewModel"/> に束縛）。
/// <para>
/// 檔の後ろに書くのは<b>見た目の都合だけ</b>＝ログの自動スクロールと、GPU メモリ欄の
/// 表示切替（裁定 67 ⑶＝設定で伏せられる）。状態の判断も文言も ViewModel に在る。
/// </para>
/// </summary>
public partial class StatusView : UserControl
{
    public StatusView()
    {
        InitializeComponent();
    }

    /// <summary>GPU メモリ欄を出すか（設定の <c>showMemoryPanel</c>）。</summary>
    public void SetMemoryPanelVisible(bool visible) =>
        MemoryPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void OnLogChanged(object sender, TextChangedEventArgs e) => LogBox.ScrollToEnd();
}
