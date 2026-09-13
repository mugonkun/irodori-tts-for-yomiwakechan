using System;
using System.Windows;
using System.Windows.Controls;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Views;

/// <summary>
/// ログのタブ（<see cref="StatusViewModel"/> に束縛・司令官の指示 2026-09-13）。
/// <para>
/// 檔の後ろに書くのは<b>流し込みだけ</b>＝⑴ タブが VM を掴んだときに
/// <see cref="StatusViewModel.FullLogLines"/> を 1 度だけ種まきし ⑵ 以後は
/// <see cref="StatusViewModel.LineLogged"/> を聞いて末尾に 1 行ずつ足す ⑶ 上限を超えたら
/// VM の環（古い方から捨て済み）で置き直す。判断も文言も VM の側に在る。
/// </para>
/// </summary>
public partial class LogView : UserControl
{
    /// <summary>置き直しの余裕＝上限を超えるたびに組み直さない（200 行ごと）。</summary>
    private const int TrimSlack = 200;

    private StatusViewModel? _model;
    private int _lines;

    public LogView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// 〔ログを開く〕が押された。<b>ここは伝えるだけ</b>＝檔を在り処ごと開くのは主窓だけ
    /// （<c>MainWindow.OpenLog</c>）なので、押された事実を上へ渡す。
    /// </summary>
    public event EventHandler? OpenLogRequested;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_model is not null)
        {
            _model.LineLogged -= OnLineLogged;
        }

        _model = e.NewValue as StatusViewModel;
        Reseed();

        if (_model is not null)
        {
            _model.LineLogged += OnLineLogged;
        }
    }

    /// <summary>VM の環から中身を置き直す（種まきと、上限を超えたときの畳み）。</summary>
    private void Reseed()
    {
        var lines = _model?.FullLogLines ?? [];
        _lines = lines.Count;
        LogBox.Text = string.Join(Environment.NewLine, lines);
        LogBox.ScrollToEnd();
    }

    private void OnLineLogged(object? sender, string line)
    {
        // 1 行は UI の糸から積むのが常だが、TextBox は UI の糸でしか触れないので念のため渡す。
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnLineLogged(sender, line));
            return;
        }

        if (_lines >= StatusViewModel.FullLogCapacity + TrimSlack)
        {
            Reseed();
            return;
        }

        if (_lines > 0)
        {
            LogBox.AppendText(Environment.NewLine);
        }

        LogBox.AppendText(line);
        _lines++;
        LogBox.ScrollToEnd();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _model?.ClearFullLog();
        Reseed();
    }

    private void OnOpenLogClick(object sender, RoutedEventArgs e) =>
        OpenLogRequested?.Invoke(this, EventArgs.Empty);
}
