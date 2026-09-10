using System.Windows;
using System.Windows.Controls;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Views;

/// <summary>
/// まとめた記録の路を運ぶ（窓が埋め、画面が読む＝<b>檔を作るのは窓の仕事</b>）。
/// </summary>
public sealed class ReportLogEventArgs : System.EventArgs
{
    /// <summary>まとめた 1 檔の路（まとめられなければ null）。</summary>
    public string? Path { get; set; }
}

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

        // 見出しと「バージョン」は版で分ける（裁定 109 → v2.0 段 F-1）＝樹の ledger/ から読む。
        // 読めなければ RTX（CUDA）として振る舞う。**樹は 1 度だけ読む**（同じ画面で 2 度数えない）。
        var flavor = ReleaseFlavors.DetectFrom(AppServices.Paths.LedgerDir);
        TitleText.Text = ReleaseFlavors.AppTitle(flavor);

        DisclaimerText.Text = AboutViewModel.Disclaimer;
        VersionText.Text = AboutViewModel.VersionText(flavor);
        UpstreamText.Text = AboutViewModel.UpstreamText;
        WatermarkText.Text = AboutViewModel.WatermarkNotice;
        EthicsText.Text = AboutViewModel.EthicsNotice;

        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// 〔使い方を見る〕（新設 <c>AboutGuideButton</c>・<c>v2-spec.md</c> §2-6）が押された。
    /// <b>ここは伝えるだけ</b>＝外の物を開けるのは主窓である。
    /// </summary>
    public event System.EventHandler? GuideRequested;

    /// <summary>
    /// 〔報告用のログを保存〕（新設 <c>AboutSaveLogButton</c>）が押された。
    /// <b>檔の場所を出すだけ</b>＝送り先は画面に出さず、隣の 1 行が X の @yomiwakechan を指す（裁定 131）。
    /// <para>
    /// 窓は<b>記録を 1 檔にまとめて</b>その路を返す（`v2-spec.md` §2-6・`v2-copy.md` §8）。
    /// 返り null＝まとめられなかった。<b>〔ログを開く〕と同じ動きにしない</b>（是正・段 C の検分）＝
    /// 1 巡目は両方が当日の記録を選択して開くだけで、名の違う 2 つの釦が同じことをしていた。
    /// </para>
    /// </summary>
    public event System.EventHandler<ReportLogEventArgs>? SaveLogRequested;

    /// <summary>
    /// 〔フォルダを開く〕（まとめた檔の在り処）。押せるのは 1 度まとめた後だけ。
    /// </summary>
    public event System.EventHandler<ReportLogEventArgs>? OpenReportFolderRequested;

    private void OnGuideClick(object sender, RoutedEventArgs e) =>
        GuideRequested?.Invoke(this, System.EventArgs.Empty);

    private void OnSaveLogClick(object sender, RoutedEventArgs e)
    {
        var args = new ReportLogEventArgs();
        SaveLogRequested?.Invoke(this, args);

        _savedReportPath = args.Path;
        SavedReportText.Text = args.Path is null
            ? UiStrings.AboutReportSaveFailed
            : UiStrings.AboutReportSaved + args.Path;
        SavedReportPanel.Visibility = Visibility.Visible;
    }

    private void OnOpenReportFolderClick(object sender, RoutedEventArgs e) =>
        OpenReportFolderRequested?.Invoke(this, new ReportLogEventArgs { Path = _savedReportPath });

    private string? _savedReportPath;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is AboutViewModel model)
        {
            LicenseList.ItemsSource = model.LicenseFolders();
        }
    }
}
