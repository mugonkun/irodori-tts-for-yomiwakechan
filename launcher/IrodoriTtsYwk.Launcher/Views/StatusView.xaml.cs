using System.Windows.Controls;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Views;

/// <summary>
/// 状態帯（<see cref="StatusViewModel"/> に束縛）。
/// <para>
/// 檔の後ろに書くのは<b>見た目の都合だけ</b>＝ログの自動スクロール 1 本である。
/// GPU メモリ欄の表示切替（裁定 67 ⑶）は<b>窓から出た</b>（是正・便 D（3）・low 6 の ⑸）＝
/// 窓が <c>SetMemoryPanelVisible</c> を持っていたころは、呼ぶのが構築時とウィザードを
/// 閉じたときの 2 箇所だけだったので、設定で外して「適用」を押しても<b>次の起動まで
/// 欄が消えなかった</b>。いまは <see cref="StatusViewModel.MemoryPanelVisible"/> に束縛する。
/// </para>
/// </summary>
public partial class StatusView : UserControl
{
    public StatusView()
    {
        InitializeComponent();
    }

    private void OnLogChanged(object sender, TextChangedEventArgs e) => LogBox.ScrollToEnd();
}
