using System.Windows.Controls;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Views;

/// <summary>
/// 設定の画面（<see cref="SettingsViewModel"/> に束縛）。
/// <para>
/// 檔の後ろに<b>何も持たない</b>＝判断も文言も ViewModel に在る。設定は「適用」まで
/// 本物に書かない（写しの上で編集する）ので、画面側で握る状態が要らない。
/// </para>
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }
}
