using System.Windows;
using System.Windows.Controls;
using IrodoriTtsYwk.Launcher.ViewModels;
using Win32 = Microsoft.Win32;

namespace IrodoriTtsYwk.Launcher.Views;

/// <summary>
/// 「発話テスト」（旧「試し撃ち」＝裁定 116）の画面（<see cref="TryViewModel"/> に束縛）。
/// <para>
/// ここに在るのは<b>窓が要る 2 つ</b>だけ＝歩数のプリセット（裁定 10＝10／40）の 2 つのボタンと、
/// 保存先を選ぶ窓（<c>SaveFileDialog</c>）。撃つ前の検分も body の組み立ても
/// <see cref="SpeechRequestBuilder"/>（純関数）に在る。
/// </para>
/// </summary>
public partial class TryView : UserControl
{
    public TryView()
    {
        InitializeComponent();
        ConcurrencyText.Text = TryViewModel.ConcurrencyNotice;
    }

    private TryViewModel? Model => DataContext as TryViewModel;

    private void OnSteps10Click(object sender, RoutedEventArgs e) => SetSteps(0);

    private void OnSteps40Click(object sender, RoutedEventArgs e) => SetSteps(1);

    private void SetSteps(int presetIndex)
    {
        if (Model is { } model && presetIndex < TryViewModel.NumStepsPresets.Count)
        {
            model.NumSteps = TryViewModel.NumStepsPresets[presetIndex];
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        if (!model.HasAudio)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "保存する音がありません。先に合成してください。",
                "保存",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new Win32.SaveFileDialog
        {
            Title = "合成した音声を保存する",
            Filter = "WAV（*.wav）|*.wav",
            FileName = model.SuggestedFileName(),
            DefaultExt = ".wav",
            AddExtension = true,
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            model.Save(dialog.FileName);
        }
    }
}
