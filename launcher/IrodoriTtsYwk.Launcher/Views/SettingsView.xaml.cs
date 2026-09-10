using System;
using System.Windows;
using System.Windows.Controls;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Views;

/// <summary>
/// 設定の画面（<see cref="SettingsViewModel"/> に束縛）。
/// <para>
/// 檔の後ろに<b>判断も文言も持たない</b>＝設定は「適用」まで本物に書かない（写しの上で編集する）
/// ので、画面側で握る状態が要らない。持つのは 2 つだけ＝⑴ 詳細の畳みを開ける口
/// ⑵ 「初回取得をやり直す」が押された事実を上へ渡す道（<b>ウィザードを開けるのは窓だけ</b>）。
/// </para>
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 「初回取得をやり直す」（v2.0 段 A-1 で主窓の帯からここへ移った釦）が押された。
    /// <b>ここは伝えるだけ</b>＝ウィザードを開けるのは主窓（<c>MainWindow.ShowFirstRun</c>）である。
    /// </summary>
    public event EventHandler? FirstRunRequested;

    /// <summary>
    /// 詳細（上級者向け）の畳みを開く（帯の 1 手〔設定の詳細を開く〕がここへ連れて来る）。
    /// </summary>
    public void OpenAdvanced() => AdvancedExpander.IsExpanded = true;

    private void OnFirstRunClick(object sender, RoutedEventArgs e) =>
        FirstRunRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 〔ログを開く〕（v2.0 段 C・<c>v2-spec.md</c> §2-5 の ⑷）が押された。
    /// <b>帯と同じ動き</b>なので、開けるのは主窓に任せる（在り処ごと開く 1 本を 2 度書かない）。
    /// </summary>
    public event EventHandler? OpenLogRequested;

    /// <summary>〔フォルダを開く〕（声のファイルの場所・データの場所）が押された。</summary>
    public event EventHandler<string>? OpenFolderRequested;

    private void OnOpenLogClick(object sender, RoutedEventArgs e) =>
        OpenLogRequested?.Invoke(this, EventArgs.Empty);

    private void OnOpenVoicesDirClick(object sender, RoutedEventArgs e) =>
        RaiseOpenFolder((DataContext as SettingsViewModel)?.VoicesDirText);

    private void OnOpenDataDirClick(object sender, RoutedEventArgs e) =>
        RaiseOpenFolder((DataContext as SettingsViewModel)?.DataDirText);

    private void RaiseOpenFolder(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            OpenFolderRequested?.Invoke(this, path);
        }
    }
}
