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
        catch (InvalidOperationException ex)
        {
            Faulted?.Invoke(this, ex.Message);
        }
        catch (System.IO.IOException ex)
        {
            Faulted?.Invoke(this, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            Faulted?.Invoke(this, ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
