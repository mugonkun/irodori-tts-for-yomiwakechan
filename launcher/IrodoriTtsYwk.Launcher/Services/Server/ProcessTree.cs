using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IrodoriTtsYwk.Launcher.Services.Server;

/// <summary>
/// 落とす対象の 1 個体（実体は <see cref="System.Diagnostics.Process"/>＝<see cref="LaunchedProcess"/>。
/// テストはフェイクを差す＝<b>実機非依存の継ぎ目</b>。本体
/// <c>yomiwakechan2/UI/Services/Launch/IrodoriServerProcessRegistry.cs</c> の
/// <c>ILaunchedProcess</c> と同じ形＝檔は写さず型だけを倣う）。
/// </summary>
public interface ILaunchedProcess : IDisposable
{
    /// <summary>もう終わっているか（自然死した個体は殺しに行かない）。</summary>
    bool HasExited { get; }

    /// <summary>pid（ログと台帳の照合用）。</summary>
    int Id { get; }

    /// <summary>プロセスツリーごと落とす（python.exe が起こす子まで）。</summary>
    void KillTree();
}

/// <summary>
/// <see cref="System.Diagnostics.Process"/> の薄いラッパ（所有権つき＝Dispose でハンドルを解放）。
/// <b>ツリー kill</b> は本体と同じ <c>Kill(entireProcessTree: true)</c>。
/// wrapper は <c>python.exe -m ywk_server</c> の 1 段だが、torch／HIP が補助プロセスを
/// 起こすことがあるので直の子だけでは足りない。
/// </summary>
public sealed class LaunchedProcess : ILaunchedProcess
{
    private readonly Process _process;

    public LaunchedProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        _process = process;
        Id = process.Id;
    }

    public int Id { get; }

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                // ハンドルがもう無い＝居ないのと同じ
                return true;
            }
        }
    }

    public void KillTree() => _process.Kill(entireProcessTree: true);

    public void Dispose() => _process.Dispose();
}

/// <summary>
/// <b>自分が起こした個体だけ</b>の台帳（本体 <c>IrodoriServerProcessRegistry</c> の流儀）。
/// <para>
/// 手で起こした個体・別のランチャが起こした個体には<b>絶対に触れない</b>。
/// 登録は <c>Process.Start</c> が成功した瞬間に行う＝この分類が自動的に一致する。
/// </para>
/// <para>
/// 失敗は握ってログ 1 行（終了を止めない）。自然死した個体は殺しに行かない。
/// </para>
/// </summary>
public sealed class LaunchedProcessRegistry : IDisposable
{
    private readonly object _gate = new();
    private readonly List<ILaunchedProcess> _processes = [];
    private readonly Action<string>? _log;
    private bool _disposed;

    /// <param name="log">正直表示 1 行の出口（null＝ログなしで同じ動作）。</param>
    public LaunchedProcessRegistry(Action<string>? log = null) => _log = log;

    /// <summary>登録件数（検分・テスト用）。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _processes.Count;
            }
        }
    }

    /// <summary>
    /// 起こした個体を登録する（所有権の移譲＝以後 Dispose するのは台帳）。
    /// 破棄の後に来た登録はその場で片付ける（起動と終了が競走しても取り残さない）。
    /// </summary>
    public void Register(ILaunchedProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        lock (_gate)
        {
            if (!_disposed)
            {
                _processes.Add(process);
                return;
            }
        }

        Close(process);
    }

    /// <summary>台帳の全個体をツリー kill する。戻り＝実際に落とした件数。</summary>
    public int CloseAll()
    {
        List<ILaunchedProcess> targets;
        lock (_gate)
        {
            targets = [.. _processes];
            _processes.Clear();
        }

        var closed = 0;
        foreach (var process in targets)
        {
            if (Close(process))
            {
                closed++;
            }
        }

        return closed;
    }

    private bool Close(ILaunchedProcess process)
    {
        var killed = false;
        try
        {
            if (process.HasExited)
            {
                return false; // 自然死＝Kill しない
            }

            process.KillTree();
            killed = true;
            _log?.Invoke("サーバ（pid " + process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "）を終了しました。");
        }
        catch (InvalidOperationException ex)
        {
            _log?.Invoke("サーバを終了できませんでした（" + ex.Message + "）。");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _log?.Invoke("サーバを終了できませんでした（" + ex.Message + "）。");
        }
        catch (AggregateException ex)
        {
            // Kill(entireProcessTree) は子の失敗を AggregateException で束ねる
            _log?.Invoke("サーバを終了できませんでした（" + ex.Message + "）。");
        }
        finally
        {
            try
            {
                process.Dispose();
            }
            catch (InvalidOperationException)
            {
                // ハンドル解放の失敗は無害
            }
        }

        return killed;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        CloseAll();
    }
}
