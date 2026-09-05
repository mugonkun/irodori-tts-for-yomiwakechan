using System;
using System.Threading;
using System.Threading.Tasks;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>
/// 何も起こさない <see cref="IServerProcess"/>。
/// <para>
/// <b>骨組みが実装なしで通るための繋ぎ</b>＝トレイと窓は最初から動き、起動席（3 席のうちの 1 つ）が
/// <see cref="AppServices.Server"/> を本物に差し替える。差し替え前に「サーバ起動」を押すと、
/// Failed と理由 1 行が出る（黙って何も起きない、にはしない）。
/// </para>
/// </summary>
public sealed class NullServerProcess : IServerProcess
{
    private const string Reason = "サーバの起動系がまだ組み込まれていません（便 D・起動席の実装待ち）。";

    public ServerState State { get; private set; } = ServerState.Stopped;

    public int? ProcessId => null;

    public int? ExitCode => null;

    public string? FailureReason { get; private set; }

    public ServerLogEvent? Banner => null;

    public Uri? BaseAddress => null;

    /// <summary>何も起こさないので標本も無い。</summary>
    public StatusResponse? LatestStatus => null;

    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;

    public event EventHandler<ServerLogLineEventArgs>? LogLine;

    /// <summary>上がらない（見張りが居ない）＝購読は受けるが呼ばない。</summary>
    public event EventHandler<StatusResponse>? StatusSampled
    {
        add { }
        remove { }
    }

    public Task<ServerStartResult> StartAsync(ServerStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var previous = State;
        State = ServerState.Failed;
        FailureReason = Reason;
        LogLine?.Invoke(this, new ServerLogLineEventArgs(
            new ServerLogEvent(ServerLogSignal.WrapperNotice, "ywk_server: " + Reason)));
        StateChanged?.Invoke(this, new ServerStateChangedEventArgs(previous, State, Reason));

        return Task.FromResult(new ServerStartResult(
            false, ServerState.Failed, null, null, TimeSpan.Zero, Reason));
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = State;
        State = ServerState.Stopped;
        FailureReason = null;
        if (previous != ServerState.Stopped)
        {
            StateChanged?.Invoke(this, new ServerStateChangedEventArgs(previous, State, null));
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
