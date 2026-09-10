using System;
using System.Windows;
using System.Windows.Controls;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.ViewModels;
using Win32 = Microsoft.Win32;

namespace IrodoriTtsYwk.Launcher.Views;

/// <summary>
/// 話者の画面（<see cref="VoicesViewModel"/> に束縛）。
/// <para>
/// <b>檔を選ぶ窓（<c>OpenFileDialog</c>）と、消すときの確認だけ</b>がここに在る＝
/// どちらも「窓が要る」から View の仕事である。写す・台帳を書く・<c>voices.json</c> を
/// 書き換えるのは ViewModel（と <see cref="IVoiceStore"/>）が行う。
/// </para>
/// </summary>
public partial class VoicesView : UserControl
{
    public VoicesView()
    {
        InitializeComponent();
        EthicsText.Text = VoicesViewModel.ImpersonationNotice;
        LengthNoticeText.Text = VoicesViewModel.ReferenceLengthNotice;

        // 削除は Command に寄せた（low 14）。確認窓は「窓が要る仕事」なので、
        // ここで手を差す＝ViewModel は窓を知らないまま確認を挟める。
        DataContextChanged += (_, _) =>
        {
            if (Model is { } model)
            {
                model.ConfirmRemove = ConfirmRemove;
            }
        };
    }

    private VoicesViewModel? Model => DataContext as VoicesViewModel;

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var pattern = string.Join(";", Array.ConvertAll(VoiceIds.WavExtensions, static x => "*" + x));
        var dialog = new Win32.OpenFileDialog
        {
            Title = UiStrings.VoicesBrowseDialogTitle,
            Filter = UiStrings.VoicesBrowseFilterAudio + "（" + pattern + "）|" + pattern
                     + "|" + UiStrings.VoicesBrowseFilterAll + "|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            SourcePathBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(NewNameBox.Text))
            {
                // 名付けの初期値は檔名の幹（利用者は上書きしてよい）
                NewNameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                NewNameBox.Focus();
                NewNameBox.SelectAll();
            }
        }
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        var ok = await model
            .AddVoiceAsync(NewNameBox.Text, SourcePathBox.Text, NewCaptionBox.Text)
            .ConfigureAwait(true);

        if (ok)
        {
            SourcePathBox.Text = string.Empty;
            NewNameBox.Text = string.Empty;
            NewCaptionBox.Text = string.Empty;
        }
    }

    /// <summary>削除の確認窓（<see cref="VoicesViewModel.ConfirmRemove"/> に差す手）。</summary>
    private bool ConfirmRemove(VoiceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        // プリセットとそれ以外で文言を分ける（設計書 §4＝プリセットは入れ直せる）。
        var tail = row.IsPreset
            ? UiStrings.VoicesRemoveConfirmTail + UiStrings.VoicesRemoveConfirmPreset
            : UiStrings.VoicesRemoveConfirmTail;

        return MessageBox.Show(
            Window.GetWindow(this),
            "「" + row.DisplayName + "」を消します。" + tail + "よろしいですか。",
            UiStrings.VoicesRemoveDialogCaption,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question) == MessageBoxResult.OK;
    }

    private void OnRestorePresetsClick(object sender, RoutedEventArgs e) => Model?.RestorePresets();

    private void OnStopPreviewClick(object sender, RoutedEventArgs e) => Model?.StopPreview();
}
