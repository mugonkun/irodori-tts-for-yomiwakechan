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
        model.Completed += OnCompleted;

        // 「入れる／飛ばす」の 2 択（裁定 87 ⑷）＝System32 の msvcp140.dll を確かめられなかった
        // ときだけ出る。窓が差す手なので ViewModel は WPF の型に触れない（§12-2 ⑴）。
        model.AskVcRedist = AskVcRedist;
    }

    /// <summary>
    /// vc_redist を入れるか飛ばすかを問う（<see cref="FirstRunViewModel.AskVcRedist"/> に差す手）。
    /// 「はい」＝入れる・「いいえ」＝飛ばす・「キャンセル」＝答えない（先へ進まない）。
    /// </summary>
    private bool? AskVcRedist(string reason)
    {
        var answer = MessageBox.Show(
            this,
            reason + "\n\n"
            + "「はい」＝Visual C++ 再頒布可能パッケージを入れます"
            + "（Microsoft の公式 URL から取得し、管理者の確認が 1 回出ます）。\n"
            + "「いいえ」＝入れずに先へ進みます"
            + "（既に入っている機体なら問題ありません。無ければ torch の読み込みで"
            + " msvcp140.dll が見つからないと出ます）。",
            "Visual C++ 再頒布可能パッケージ",
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
                "取得が走っています。中断して閉じますか（続きから取り直せます）。",
                "初回取得",
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
