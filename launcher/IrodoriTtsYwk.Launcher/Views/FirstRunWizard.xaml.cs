using System;
using System.ComponentModel;
using System.Windows;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Views;

/// <summary>
/// 初回取得ウィザード（<see cref="FirstRunViewModel"/> に束縛）。
/// <para>
/// <b>取得の最中は閉じさせない</b>＝<c>.part</c> を残したまま窓が消えると、利用者からは
/// 「途中で止まったのか終わったのか」が分からなくなる。閉じるなら「中断」を押させる。
/// </para>
/// </summary>
public partial class FirstRunWizard : Window
{
    private readonly FirstRunViewModel _model;

    public FirstRunWizard(FirstRunViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        InitializeComponent();

        _model = model;
        DataContext = model;

        // 窓題は版で分ける（裁定 109）。版は ViewModel が樹から読んだもの（FirstRunViewModel.Flavor）。
        Title = ReleaseFlavors.WizardTitle(model.Flavor);

        model.Completed += OnCompleted;

        // 「入れる／飛ばす」の 2 択（裁定 87 ⑷）＝System32 の msvcp140.dll を確かめられなかった
        // ときだけ出る。窓が差す手なので ViewModel は WPF の型に触れない（§12-2 ⑴）。
        model.AskVcRedist = AskVcRedist;

        // **ドライバを見てから変種を勧める**（裁定 126 の B）＝窓が開いたところで 1 度だけ撃つ。
        // 待たない（nvidia-smi は 0.04 s だが、無い機体では期限まで掛かる）＝読めた時点で
        // 変種の選びと「この構成で取得を始める」の可否が束縛経由で入れ替わる。
        Loaded += OnLoadedProbeDriver;
    }

    private void OnLoadedProbeDriver(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedProbeDriver;
        _ = _model.RefreshDriverAsync();
    }

    /// <summary>
    /// 〔全文を見る〕＝お知らせの全文の畳みを開く／もう一度押すと畳む
    /// （決裁 130 Q3・`v2-spec.md` §3 段 1）。
    /// <para>
    /// <b>要約で済ませない</b>＝全文は<b>1 押しで必ず見られる</b>。同意の錠
    /// （<see cref="FirstRunViewModel.CanAcceptNotices"/>）はこの釦とは無関係に働く。
    /// </para>
    /// </summary>
    private void OnNoticesFullClick(object sender, RoutedEventArgs e) =>
        FirstRunNoticesExpander.IsExpanded = !FirstRunNoticesExpander.IsExpanded;

    /// <summary>
    /// Microsoft の部品を入れるか飛ばすかを問う（<see cref="FirstRunViewModel.AskVcRedist"/> に差す手）。
    /// 「はい」＝入れる・「いいえ」＝飛ばす・「キャンセル」＝答えない（先へ進まない）。
    /// <para>
    /// <b>内輪の 1 行（<paramref name="reason"/>）は窓に出さない</b>（`v2-copy.md` §1-8＝
    /// <c>Views/FirstRunWizard.xaml.cs:57-62</c>）＝理由は檔（<c>Record</c>）に残っている。
    /// </para>
    /// </summary>
    private bool? AskVcRedist(string reason)
    {
        var answer = MessageBox.Show(
            this,
            UiStrings.WizardUacBody,
            UiStrings.WizardUacCaption,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return answer switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_model.IsBusy)
        {
            var answer = MessageBox.Show(
                this,
                UiStrings.WizardCloseBody,
                UiStrings.WizardCloseCaption,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.OK)
            {
                e.Cancel = true;
                return;
            }

            _model.CancelRunning();
        }

        _model.Completed -= OnCompleted;
        base.OnClosing(e);
    }

    private void OnCompleted(object? sender, EventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
