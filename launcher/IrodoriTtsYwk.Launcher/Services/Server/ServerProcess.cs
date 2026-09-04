using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Server;

/// <summary>
/// 契約 ⑷ の実装＝wrapper の子プロセス 1 個体（設計書 §2）。
/// <para>
/// <b>やること 5 つ</b>＝⑴ 起こす前にポートを検める（裁定 52＝塞がっていたら<b>止まって告知</b>）
/// ⑵ <c>python.exe -m ywk_server --host 127.0.0.1 --port N</c> を env つきで起こす
/// ⑶ stdout／stderr を<b>非同期に</b>読んで状態機械へ流す ⑷ ready まで待つ（120 s・CPU 300 s）
/// ⑸ 停止＝<b>自分が起こした個体だけ</b>をツリー kill（上流に shutdown の路は無い）。
/// </para>
/// <para>
/// <b>即死を捕まえる</b>＝exit 2（wrapper の事前検査）／3（上流の startup 失敗）は待ちの中で結末になり、
/// 理由 1 行は stderr の最終行から作る（受け入れ条件 D-1＝≤ 15 s で UI に 1 行）。
/// </para>
/// <para>
/// <b>テストの継ぎ目は public コンストラクタ</b>＝ポート検査（<see cref="IPortProbe"/>）と
/// readiness（<see cref="IReadinessProbe"/>）を差せる。状態機械そのものは
/// <see cref="ServerStateMachine"/> に切り出してあり、子プロセスなしで釘付けできる。
/// </para>
/// </summary>
public sealed class ServerProcess : IServerProcess
{
    /// <summary>ready 待ちの間、readiness を採る間隔。</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>ready の後、暖機・死亡を見張る間隔。</summary>
    public static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(2);

    /// <summary>ツリー kill の後、抜けるまで待つ上限。</summary>
    public static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(10);

    private readonly ServerStateMachine _machine = new();
    private readonly IPortProbe _portProbe;
    private readonly IReadinessProbe _readinessProbe;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _watchInterval;
    private readonly object _gate = new();

    private LaunchedProcessRegistry? _registry;
    private Process? _process;
    private CancellationTokenSource? _watch;
    private Task? _watchTask;
    private int _port;
    private bool _disposed;

    /// <summary>実機用（TCP 検査＋<c>/ywk/status</c> の polling）。</summary>
    public ServerProcess()
        : this(new TcpPortProbe(), new HealthPoller(), null, null)
    {
    }

    /// <summary>テスト用（<b>public コンストラクタが継ぎ目</b>）。</summary>
    public ServerProcess(
        IPortProbe portProbe,
        IReadinessProbe readinessProbe,
        TimeSpan? pollInterval = null,
        TimeSpan? watchInterval = null)
    {
        ArgumentNullException.ThrowIfNull(portProbe);
        ArgumentNullException.ThrowIfNull(readinessProbe);
        _portProbe = portProbe;
        _readinessProbe = readinessProbe;
        _pollInterval = pollInterval ?? PollInterval;
        _watchInterval = watchInterval ?? WatchInterval;

        _machine.StateChanged += (_, e) => StateChanged?.Invoke(this, e);
        _machine.LogLine += (_, e) => LogLine?.Invoke(this, e);
    }

    public ServerState State => _machine.State;

    public int? ProcessId { get; private set; }

    public int? ExitCode { get; private set; }

    public string? FailureReason => _machine.FailureReason;

    public ServerLogEvent? Banner => _machine.Banner;

    public Uri? BaseAddress { get; private set; }

    /// <summary>実測した device（<c>ywk_server: device actual=</c>）。表示にだけ使う。</summary>
    public string? DeviceActual => _machine.DeviceActual;

    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;

    public event EventHandler<ServerLogLineEventArgs>? LogLine;

    public async Task<ServerStartResult> StartAsync(
        ServerStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var started = Stopwatch.GetTimestamp();

        lock (_gate)
        {
            if (_process is not null && !HasExited(_process))
            {
                return Fail(started, "サーバは既に起きています（停止してから起こしてください）。", raise: false);
            }
        }

        _machine.Reset();
        ProcessId = null;
        ExitCode = null;
        BaseAddress = request.BaseAddress;
        _port = request.Port;

        // ⑴ 裁定 52＝塞がっていたら止まって告知する（次のポートを探さない）
        if (!_portProbe.IsFree(request.Host, request.Port))
        {
            return Fail(started, ServerBindFailure.Message(request.Port));
        }

        Process process;
        try
        {
            process = new Process { StartInfo = BuildStartInfo(request), EnableRaisingEvents = false };
            process.OutputDataReceived += (_, e) => OnLine(e.Data, isStandardError: false);
            process.ErrorDataReceived += (_, e) => OnLine(e.Data, isStandardError: true);

            if (!process.Start())
            {
                process.Dispose();
                return Fail(started, "サーバのプロセスを起こせませんでした。");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // 実行系が無い・壊れている（絶対パスは出さない＝檔名だけ）
            return Fail(started, "python.exe を起こせませんでした（" + ex.Message + "）。");
        }
        catch (InvalidOperationException ex)
        {
            return Fail(started, "サーバのプロセスを起こせませんでした（" + ex.Message + "）。");
        }

        lock (_gate)
        {
            _process = process;
            _registry ??= new LaunchedProcessRegistry();
            _registry.Register(new LaunchedProcess(process));
        }

        ProcessId = process.Id;
        _machine.ApplyStarted();

        // ⑷ ready まで待つ。結末は 4 つ＝ready／即死（exit 2・3）／期限切れ／取消。
        ServerStartResult result;
        try
        {
            result = await WaitForReadyAsync(request, process, started, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消でも孤児を残さない（起こした個体は自分の物）。
            await KillChildAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (result.Ok)
        {
            StartWatch(request);
        }
        else
        {
            // **止めろと言われる前に止める**（是正・2026-09-05）。
            // 期限切れ・ログからの bind 失敗で子を残すと、UI の「サーバ停止」は
            // Failed では押せず、「サーバ起動」は「既に起きています」で拒まれる＝
            // GPU とポートを掴んだ孤児から逃げる口が無くなる。ここで必ず落とす。
            await KillChildAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return result;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await KillChildAsync(cancellationToken).ConfigureAwait(false);

        ProcessId = null;
        BaseAddress = null;
        _machine.ApplyStopped();
    }

    /// <summary>
    /// 起こした個体を落として台帳を畳む（<b>状態機械には触れない</b>）。
    /// <para>
    /// <see cref="StopAsync"/> はこの後に <c>Stopped</c> へ移すが、起動が失敗した経路は
    /// <c>Failed</c> と理由 1 行を保ったまま子だけを落とす＝画面の理由が消えない。
    /// </para>
    /// </summary>
    private async Task KillChildAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? watch;
        Task? watchTask;
        LaunchedProcessRegistry? registry;
        Process? process;

        lock (_gate)
        {
            watch = _watch;
            watchTask = _watchTask;
            registry = _registry;
            process = _process;
            _watch = null;
            _watchTask = null;
            _registry = null;
            _process = null;
        }

        if (watch is not null)
        {
            await watch.CancelAsync().ConfigureAwait(false);
        }

        if (watchTask is not null)
        {
            try
            {
                await watchTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 見張りを畳んだだけ
            }
        }

        watch?.Dispose();

        // 起こした個体だけをツリー kill する（手で起こした個体には触れない）。
        // **殺してから抜けるまで待つ順**にする＝台帳（registry）はハンドルの所有者で、
        // Dispose するとこの Process からは終了コードも読めなくなる（実測＝
        // "No process is associated with this object"）。
        if (process is not null)
        {
            try
            {
                if (!HasExited(process))
                {
                    process.Kill(entireProcessTree: true);
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                ExitCode = SafeExitCode(process);
            }
            catch (OperationCanceledException)
            {
                // 抜け切らなかった＝次の起動でポート検査が拾う（裁定 52）
            }
            catch (InvalidOperationException)
            {
                // 既に片付いている
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 落とせなかった＝台帳がもう一度試す
            }
            catch (AggregateException)
            {
                // ツリーの子の 1 つが落ちなかっただけ
            }
        }

        // 自然死を見て何もしない（ハンドルだけ解放する）
        registry?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            using var cts = new CancellationTokenSource(StopGrace);
            await StopAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 終了は止めない
        }

        if (_readinessProbe is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <summary>
    /// 子に渡す起動情報を組む（<b>env は差分だけ</b>＝<see cref="ServerEnvironment.Build"/> が作った物を
    /// 親の env の上に載せる。親の PATH・SystemRoot は要る）。
    /// </summary>
    private static ProcessStartInfo BuildStartInfo(ServerStartRequest request)
    {
        var info = new ProcessStartInfo
        {
            FileName = request.PythonExe,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in request.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var pair in request.Environment)
        {
            info.Environment[pair.Key] = pair.Value;
        }

        return info;
    }

    private void OnLine(string? line, bool isStandardError)
    {
        if (line is null)
        {
            return; // ストリームの終わり
        }

        _machine.ApplyLogLine(line, isStandardError, _port);
    }

    private async Task<ServerStartResult> WaitForReadyAsync(
        ServerStartRequest request,
        Process process,
        long started,
        CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.GetTimestamp()
            + (long)(request.ReadyTimeout.TotalSeconds * Stopwatch.Frequency);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (HasExited(process))
            {
                // stderr の最後の行が届くまでの僅かな間を待つ（読みは非同期）
                try
                {
                    process.WaitForExit(500);
                }
                catch (SystemException)
                {
                    // 既に片付いている
                }

                var exit = SafeExitCode(process) ?? -1;
                ExitCode = exit;
                _machine.ApplyExit(exit);
                return new ServerStartResult(
                    false, ServerState.Failed, ProcessId, exit, Stopwatch.GetElapsedTime(started),
                    _machine.FailureReason);
            }

            if (_machine.State == ServerState.Failed)
            {
                // bind 失敗などをログから拾った＝止まって告知する（裁定 52）
                return new ServerStartResult(
                    false, ServerState.Failed, ProcessId, null, Stopwatch.GetElapsedTime(started),
                    _machine.FailureReason);
            }

            var sample = await SampleAsync(request.BaseAddress, cancellationToken).ConfigureAwait(false);
            if (_machine.ApplyReadiness(sample.Reachable, sample.Loaded, sample.WarmupRunning))
            {
                return new ServerStartResult(
                    true, _machine.State, ProcessId, null, Stopwatch.GetElapsedTime(started), null);
            }

            if (Stopwatch.GetTimestamp() >= deadline)
            {
                var reason = "起動が "
                    + request.ReadyTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)
                    + " 秒で終わりませんでした。"
                    + (sample.FailureReason ?? "モデルの読み込みが終わっていません。");
                _machine.ApplyFailure(reason);
                return new ServerStartResult(
                    false, ServerState.Failed, ProcessId, null, Stopwatch.GetElapsedTime(started), reason);
            }

            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ReadinessSample> SampleAsync(Uri baseAddress, CancellationToken cancellationToken)
    {
        try
        {
            return await _readinessProbe.ProbeAsync(baseAddress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ReadinessSample(false, false, false, null, null);
        }
    }

    /// <summary>
    /// ready の後の見張り＝⑴ 暖機の出入り（<c>Warming ⇄ Ready</c>）⑵ 落ちたことの検知。
    /// </summary>
    private void StartWatch(ServerStartRequest request)
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var process = _process;
        if (process is null)
        {
            cts.Dispose();
            return;
        }

        var task = Task.Run(
            async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_watchInterval, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (HasExited(process))
                    {
                        var exit = SafeExitCode(process) ?? -1;
                        ExitCode = exit;
                        _machine.ApplyExit(exit);
                        return;
                    }

                    var sample = await SampleAsync(request.BaseAddress, token).ConfigureAwait(false);
                    _machine.ApplyReadiness(sample.Reachable, sample.Loaded, sample.WarmupRunning);
                }
            },
            token);

        lock (_gate)
        {
            _watch = cts;
            _watchTask = task;
        }
    }

    private ServerStartResult Fail(long started, string reason, bool raise = true)
    {
        if (raise)
        {
            _machine.ApplyFailure(reason);
        }

        return new ServerStartResult(
            false, _machine.State, ProcessId, ExitCode, Stopwatch.GetElapsedTime(started), reason);
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static int? SafeExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>
/// 起動の材料を 1 箇所で組む（<b>純関数</b>＝窓もトレイも初回取得もここを通す）。
/// <para>
/// 窓（<c>Views/MainWindow</c>）は既に同じ手順を踏んでいるが、暖機・事前計算・検分の台本からも
/// 同じ組み立てが要る。<b>2 度書かない</b>ための 1 関数。
/// </para>
/// </summary>
public static class ServerLaunchPlan
{
    /// <summary>
    /// 設定・場所・解決済み GPU index から <see cref="ServerStartRequest"/> を作る。
    /// <paramref name="pythonExe"/> は <see cref="AppPaths.ResolvePythonExe"/> の結果。
    /// </summary>
    public static ServerStartRequest Build(
        LauncherSettings settings,
        AppPaths paths,
        string pythonExe,
        int? gpuIndex,
        bool offlineHuggingFace = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(pythonExe);

        IReadOnlyDictionary<string, string> env =
            ServerEnvironment.Build(settings, paths, gpuIndex, offlineHuggingFace);

        return new ServerStartRequest(
            pythonExe,
            paths.ServerDir,
            "127.0.0.1",
            settings.Port,
            env,
            settings.EffectiveReadyTimeout());
    }
}
