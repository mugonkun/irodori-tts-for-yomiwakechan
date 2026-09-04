using System.Windows;
using System.Windows.Controls;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Views;

/// <summary>
/// このアプリについて（<see cref="AboutViewModel"/> に束縛）。
/// <para>
/// 版と上流 pin と免責は<b>静的な文言</b>なので束縛ではなく直に入れる（束縛の失敗で
/// 免責が空欄になる、という事故を作らない）。ライセンスの束は樹を読んだ結果なので束縛で出す。
/// </para>
/// </summary>
public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();

        DisclaimerText.Text = AboutViewModel.Disclaimer;
        VersionText.Text = AboutViewModel.VersionText;
        UpstreamText.Text = AboutViewModel.UpstreamText;
        WatermarkText.Text = AboutViewModel.WatermarkNotice;
        EthicsText.Text = AboutViewModel.EthicsNotice;

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is AboutViewModel model)
        {
            LicenseList.ItemsSource = model.LicenseFolders();
        }
    }
}
