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
