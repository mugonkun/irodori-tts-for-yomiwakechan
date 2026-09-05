using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace IrodoriTtsYwk.Launcher.Mvvm;

/// <summary>
/// 押したら 1 手が走る <see cref="ICommand"/>（自前・依存追加なし）。
/// <para>
/// <c>CommandManager.RequerySuggested</c> は<b>使わない</b>＝WPF の静的な口に繋ぐと
/// ViewModel が窓なしで作れなくなる。可否が変わったら
/// <see cref="RaiseCanExecuteChanged"/> を明示で叩く（誰が可否を動かしたかが読める）。
/// </para>
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute is null || _canExecute();

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            _execute();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// 走っている間は押せなくなる非同期の <see cref="ICommand"/>。
/// <para>
/// 合成・取得・起動はどれも秒〜分の仕事なので、<b>二重に走らせない</b>ことが第一の約束である
/// （<see cref="IsRunning"/> が真の間 <see cref="CanExecute"/> は偽）。例外は握り潰さず
/// <see cref="Faulted"/> に 1 行で残す（UI が告げる）。
/// </para>
/// <para>
/// <b>受け口は全例外</b>（便 D（3）・設計書 §20-5 ⑴）。1 巡目・2 巡目は
/// <see cref="InvalidOperationException"/>／<see cref="System.IO.IOException"/>／
/// <see cref="UnauthorizedAccessException"/> の 3 型だけを数え上げていたので、
/// <c>Process.Start</c> が投げる <c>Win32Exception</c>（「この OS では動かない実行檔」）は
/// どの catch にも当たらず、<b>押した手が黙って消えた</b>（実射＝「サーバ起動」を押しても
/// 窓が 60 秒何も出さない・ログ帯にも 1 行も出ない）。型で数え上げるのをやめ、
/// <b>捕った物は必ず 1 行にして出す</b>。
/// </para>
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>握り潰さないための口（理由 1 行）。</summary>
    public event EventHandler<string>? Faulted;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value)
            {
                return;
            }

            _isRunning = value;
            RaiseCanExecuteChanged();
        }
    }

    public bool CanExecute(object? parameter) =>
        !IsRunning && (_canExecute is null || _canExecute());

    public void Execute(object? parameter) => _ = ExecuteAsync();

    /// <summary>待てる形（テストと、View の「終わってから次へ」に使う）。</summary>
    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        IsRunning = true;
        try
        {
            await _execute().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 取消は失敗ではない（利用者が止めた）
        }
        catch (Exception ex)
        {
            // **型で数え上げない**（是正・便 D（3）＝§20-5 ⑴）。捕らなかった例外は
            // `Execute` の投げ捨て（`_ = ExecuteAsync()`）の中で観測されないまま消える＝
            // 押しても何も起きないボタンになる。ここで 1 行にして必ず外へ出す。
            Faulted?.Invoke(this, Describe(ex));
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>例外の 1 行（<b>純関数</b>・文言が空の型は型名で名乗る）。</summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = exception.Message;
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message.Trim();
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
