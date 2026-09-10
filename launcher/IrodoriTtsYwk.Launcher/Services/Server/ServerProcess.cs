using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;

namespace IrodoriTtsYwk.Launcher.Services.Server;

/// <summary>
/// 契約 ⑷ の実装＝wrapper の子プロセス 1 個体（設計書 §2）。
/// <para>
/// <b>やること 6 つ</b>＝⑴ 起こす前にポートを検める（裁定 52＝塞がっていたら<b>止まって告知</b>）
/// ⑵ <b>変種の門</b>＝GPU 変種はその <c>python.exe</c> で torch を検分し、GPU を見られなければ
/// <b>起こさない</b>（裁定 88 ⑴＝<see cref="VariantGate"/>）
/// ⑶ <c>python.exe -m ywk_server --host 127.0.0.1 --port N</c> を env つきで起こす
/// ⑷ stdout／stderr を<b>非同期に</b>読んで状態機械へ流す ⑸ ready まで待つ（120 s・CPU 300 s）
/// ⑹ 停止＝<b>自分が起こした個体だけ</b>をツリー kill（上流に shutdown の路は無い）。
/// </para>
/// <para>
/// <b>死活は <see cref="Process.Exited"/> で見る</b>（裁定 88 ⑶）＝見張りの標本（2 秒間隔）を待たない。
/// 子が消えた瞬間に終了コードを読み、stderr を読み切ってから
/// <see cref="ServerExitCodes.Describe"/> の 1 行で <c>Failed</c> に落とす。
/// </para>
/// <para>
/// <b>見張りは 1 本</b>＝採った <c>/ywk/status</c> を <see cref="LatestStatus"/> と
/// <see cref="StatusSampled"/> で公開する（low 3）。窓は別に叩かない。
/// </para>
/// <para>
/// <b>同じ回に Windows の GPU 計数も採る</b>（裁定 110・2026-09-08）＝
/// <see cref="OsGpuMemorySampler"/> が PDH（<c>GPU Process Memory</c>／<c>GPU Adapter Memory</c>）と
/// DXGI を読み、<see cref="LatestOsGpuMemory"/> に置いてから <see cref="StatusSampled"/> を上げる。
/// 読めない機体では<b>空</b>になるだけで、標本も状態も止まらない。
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
    /// <summary>
    /// ready 待ちの間、readiness を採る間隔。
    /// <b>実効の周期は「これ＋1 標本の代金」</b>＝誰も listen していない相手への 1 標本は
    /// 1.5 秒（実測・<c>WrapperClient.ConnectTimeout</c>）なので、起動待ちの実効は約 2.5 秒周期。
    /// 是正前は 1 標本 4.0 秒＝実効 5 秒周期だった（便 D（2）の是正＝
    /// <see cref="HealthPoller"/> が届かない相手に 2 本目を撃たなくなった）。
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// ready の後、暖機・死亡を見張る間隔。
    /// 実効は同じく「これ＋1 標本」＝生きている相手には 2 秒強、応答が消えた相手には 3.5 秒。
    /// 降ろすまでの合計は <see cref="ServerStateMachine.UnreachableSamplesToDowngrade"/> の註。
    /// </summary>
    public static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(2);

    /// <summary>ツリー kill の後、抜けるまで待つ上限。</summary>
    public static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 子が消えたあと stderr を読み切るのを待つ上限（low 2）。
    /// <para>
    /// <b>引数なしの <see cref="Process.WaitForExit()"/></b> は、<c>BeginErrorReadLine</c> の読みが
    /// 終わるまで待つ（<c>WaitForExit(int)</c> は待たない＝最終行を取りこぼす）。ただし子が
    /// 標準出力のハンドルを孫に渡していると<b>いつまでも返らない</b>ので、別スレッドで待って
    /// この上限で打ち切る。裁定 88 ⑶ の「1 秒以内に <c>StateChanged</c>」を守るため 0.5 秒。
    /// </para>
    /// </summary>
    public static readonly TimeSpan StderrDrainGrace = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// <b>子を「起こす」段そのものの期限</b>（是正・便 D（3）＝設計書 §20-5 ⑴）。
    /// <para>
    /// <see cref="Process.Start"/> は同期呼び出しで、返ってこない機体では
    /// <see cref="CancellationToken"/> でも切れない。1 度の「サーバ起動」で同じ
    /// <c>python.exe</c> を 3 回起こす（窓の列挙・門の検分・この子）ので、
    /// <see cref="ProcessRunner.DefaultStartTimeout"/> と同じ 3 s に揃えてある
    /// （3 つ止まっても 9 s＝無人検分の budget 10 s の内側）。
    /// </para>
    /// </summary>
    public static readonly TimeSpan SpawnTimeout = ProcessRunner.DefaultStartTimeout;

    private readonly ServerStateMachine _machine = new();
    private readonly IPortProbe _portProbe;
    private readonly IReadinessProbe _readinessProbe;
    private readonly ITorchProbe? _torchProbe;
    private readonly Func<ServerStartRequest, ProcessStartInfo> _startInfo;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _watchInterval;
    private readonly object _gate = new();

    /// <summary>OS の GPU 計数（裁定 110）。null＝この個体では数えない（テスト）。</summary>
    private readonly OsGpuMemorySampler? _osGpuMemory;

    private LaunchedProcessRegistry? _registry;
    private Process? _process;
    private CancellationTokenSource? _watch;
    private Task? _watchTask;
    private int _port;
    private bool _disposed;

    /// <summary>直前に「別の個体」と告げた pid（同じ 1 行をログに繰り返さない）。</summary>
    private int? _foreignPidLogged;

    /// <summary>いまの起動の世代（<see cref="Process.Exited"/> が前回の個体を告げないため）。</summary>
    private object _generation = new();

    /// <summary>
    /// 実機用（TCP 検査＋<c>/ywk/status</c> の polling＋変種の門＋OS の GPU 計数）。
    /// </summary>
    public ServerProcess()
        : this(new TcpPortProbe(), new HealthPoller(), null, null, new TorchProbeRunner())
    {
        // **計数を数えるのはここで作った個体だけ**（裁定 110 D5）＝テスト用の口から作った個体は
        // 差されない限り数えない。**ここでは `pdh.dll` を 1 度も叩かない**（是正・2026-09-08）＝
        // このコンストラクタは `LauncherComposition.Compose()`＝`App.OnStartup`＝**UI の糸**から
        // 呼ばれ、しかも窓を作る前である。query の開設は実測で 0.23 s 掛かる（perflib の初期化）ので、
        // 開くのは最初の標本＝**見張りの糸**に遅らせてある（`OsGpuMemorySampler.CreateForMachine`）。
        // 開けなかった機体は理由 1 行をログ帯へ 1 度だけ流し、以後は黙って空を返す。
        _osGpuMemory = OsGpuMemorySampler.CreateForMachine(line =>
            LogLine?.Invoke(this, new ServerLogLineEventArgs(
                new ServerLogEvent(ServerLogSignal.Other, line))));
    }

    /// <summary>テスト用（<b>public コンストラクタが継ぎ目</b>）。</summary>
    /// <param name="portProbe">起こす前の bind 検査。</param>
    /// <param name="readinessProbe">ready の標本。</param>
    /// <param name="pollInterval">ready 待ちの間隔。</param>
    /// <param name="watchInterval">ready の後の見張りの間隔。</param>
    /// <param name="torchProbe">
    /// <b>変種の門</b>の検分（裁定 88 ⑴）。null＝門を通さない
    /// （<c>cpu</c> 変種・変種が判らない要求と同じ扱い）。
    /// </param>
    /// <param name="startInfoFactory">
    /// 子の起こし方（既定＝<see cref="BuildStartInfo"/>）。<b>死活の釘</b>が偽の子プロセス
    /// （すぐ消える <c>cmd.exe</c>）を差すための継ぎ目。
    /// </param>
    /// <param name="osGpuMemory">
    /// OS の GPU 計数（裁定 110）。null＝この個体では数えない（<b>既定</b>＝実機の PDH も DXGI も
    /// テストでは 1 度も開かない）。偽の口を差せば<b>配線そのもの</b>
    /// （<see cref="LatestOsGpuMemory"/> が <see cref="StatusSampled"/> の前に置かれる・
    /// Stopped／Failed と <see cref="StartAsync"/> の頭で空へ戻る）を釘付けできる。
    /// </param>
    public ServerProcess(
        IPortProbe portProbe,
        IReadinessProbe readinessProbe,
        TimeSpan? pollInterval = null,
        TimeSpan? watchInterval = null,
        ITorchProbe? torchProbe = null,
        Func<ServerStartRequest, ProcessStartInfo>? startInfoFactory = null,
        OsGpuMemorySampler? osGpuMemory = null)
    {
        ArgumentNullException.ThrowIfNull(portProbe);
        ArgumentNullException.ThrowIfNull(readinessProbe);
        _portProbe = portProbe;
        _readinessProbe = readinessProbe;
        _torchProbe = torchProbe;
        _osGpuMemory = osGpuMemory;
        _startInfo = startInfoFactory ?? BuildStartInfo;
        _pollInterval = pollInterval ?? PollInterval;
        _watchInterval = watchInterval ?? WatchInterval;

        _machine.StateChanged += (_, e) =>
        {
            // **死んだ個体の数字を残さない**（是正・便 D（2）＝low）。窓は Failed／Stopped で
            // ApplyStatusSample(null) を撃つので画面は掃けていたが、`LatestStatus` を直に読む
            // 後続（検分の台本・便 E）には前の走行の標本が見えたままだった。
            if (e.Current is ServerState.Failed or ServerState.Stopped)
            {
                LatestStatus = null;
                LatestOsGpuMemory = [];
            }

            StateChanged?.Invoke(this, e);
        };
        _machine.LogLine += (_, e) => LogLine?.Invoke(this, e);
    }

    public ServerState State => _machine.State;

    public int? ProcessId { get; private set; }

    public int? ExitCode { get; private set; }

    public string? FailureReason => _machine.FailureReason;

    public ServerLogEvent? Banner => _machine.Banner;

    public Uri? BaseAddress { get; private set; }

    /// <summary>
    /// 見張り 1 本が採った最新の <c>/ywk/status</c>（low 3）。窓はこれを読むだけにする。
    /// </summary>
    public StatusResponse? LatestStatus { get; private set; }

    /// <summary>
    /// 見張り 1 本が同じ回に採った OS の GPU 計数（裁定 110）。数えられなければ空。
    /// </summary>
    public IReadOnlyList<OsGpuMemoryRow> LatestOsGpuMemory { get; private set; } = [];

    /// <summary>実測した device（<c>ywk_server: device actual=</c>）。表示にだけ使う。</summary>
    public string? DeviceActual => _machine.DeviceActual;

    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;

    public event EventHandler<ServerLogLineEventArgs>? LogLine;

    /// <inheritdoc />
    public event EventHandler<StatusResponse>? StatusSampled;

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
        _foreignPidLogged = null;
        ProcessId = null;
        ExitCode = null;
        LatestStatus = null;
        LatestOsGpuMemory = [];
        BaseAddress = request.BaseAddress;
        _port = request.Port;

        // ⑴ 裁定 52＝塞がっていたら止まって告知する（次のポートを探さない）
        if (!_portProbe.IsFree(request.Host, request.Port))
        {
            return Fail(started, ServerBindFailure.Message(request.Port));
        }

        // ⑵ 変種の門（裁定 88 ⑴）＝GPU を見られない変種は**起こさない**。
        var gate = await DecideGateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!gate.Allow)
        {
            return Fail(started, gate.Reason ?? "この変種はこの機体で起こせません。");
        }

        var generation = new object();
        _generation = generation;

        Process process;
        try
        {
            process = new Process { StartInfo = _startInfo(request), EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => OnLine(e.Data, isStandardError: false);
            process.ErrorDataReceived += (_, e) => OnLine(e.Data, isStandardError: true);

            // **死活は Exited で見る**（裁定 88 ⑶）＝見張りの 2 秒待ちに依らない。
            process.Exited += (_, _) => OnChildExited(process, generation);

            // **「起こす」段そのものに期限を掛ける**（是正・便 D（3）＝設計書 §20-5 ⑴）。
            // `Process.Start` は同期呼び出しで、返ってこない機体（壊れた python.exe）では
            // `CancellationToken` でも切れない＝窓（WPF のメッセージポンプ）の上でだけ
            // 60 秒黙る形になった。別スレッドへ逃がして待ち、見限った個体は手放す。
            // **`StartAsync` は Windows のハードエラー窓も止める**（是正・便 D（3）の統合席＝
            // 無人検分の段 k が実窓で「サポートされていない 16 ビット アプリケーション」を捕まえた。
            // その窓が出ている間 `Process.Start` は返らない＝逐語は `ProcessRunner.StartAsync`）。
            var spawn = ProcessRunner.StartAsync(process);
            if (await Task.WhenAny(spawn, Task.Delay(SpawnTimeout, CancellationToken.None))
                    .ConfigureAwait(false) != spawn)
            {
                ProcessRunner.Abandon(process, spawn);
                return Fail(started, ProcessRunner.StartStalledMessage(request.PythonExe, SpawnTimeout))
                    with { Notices = gate.Notices };
            }

            if (!await spawn.ConfigureAwait(false))
            {
                process.Dispose();
                // 門の告知は「起こしたが伝える」1 行だが、**起こし損ねた経路でも落とさない**
                // （是正・便 D（2）＝low。cu126 で「未実測の帯」を告げつつ python.exe の起動に
                // 失敗した機体で、その 1 行が画面に 1 度も出なかった）。
                return Fail(started, "サーバのプロセスを起こせませんでした。") with { Notices = gate.Notices };
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // 実行系が無い・壊れている（絶対パスは出さない＝檔名だけ）
            return Fail(started, "python.exe を起こせませんでした（" + ex.Message + "）。")
                with { Notices = gate.Notices };
        }
        catch (InvalidOperationException ex)
        {
            return Fail(started, "サーバのプロセスを起こせませんでした（" + ex.Message + "）。")
                with { Notices = gate.Notices };
        }

        lock (_gate)
        {
            _process = process;
            _registry ??= new LaunchedProcessRegistry();
            _registry.Register(new LaunchedProcess(process));
        }

        ProcessId = process.Id;
        _machine.ApplyStarted();

        // ⑸ ready まで待つ。結末は 4 つ＝ready／即死（exit 2・3）／期限切れ／取消。
        ServerStartResult result;
        try
        {
            result = await WaitForReadyAsync(request, process, started, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // **取消は例外で抜けない**（low 1）＝孤児を残さずツリー kill し、
            // 状態は Failed ではなく Stopped（利用者が止めたのだから理由 1 行は要らない）。
            // 契約の「例外は投げず結末で返す」（IServerProcess.StartAsync）をここで守る。
            var pid = ProcessId;
            await KillChildAsync(CancellationToken.None).ConfigureAwait(false);
            ProcessId = null;
            BaseAddress = null;
            _machine.ApplyStopped();
            return new ServerStartResult(
                false, ServerState.Stopped, pid, ExitCode, Stopwatch.GetElapsedTime(started),
                "起動を中止しました。")
            {
                Notices = gate.Notices,
            };
        }

        result = result with { Notices = gate.Notices };

        if (result.Ok)
        {
            StartWatch(request);
        }
        else if (IsAliveLoadFailure(result, process))
        {
            // **読込に失敗した個体は殺さない**（裁定 105 ⑴＝便 G・設計席の是正）。wrapper は
            // bind したまま /ywk/status.runtime.error に理由を載せて生きている＝本体の走査も
            // この窓もその理由を読める。ポートを閉じるのは「サーバ停止」（Failed でも押せる＝
            // 是正 2026-09-05）。下の「止めろと言われる前に止める」は期限切れ・ログからの
            // bind 失敗＝**答えない個体**にだけ効く。
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

    /// <summary>
    /// 起動の結末が「読込の失敗」（裁定 105）で、子がまだ答えているか＝理由の接頭辞
    /// （<see cref="ServerStateMachine.RuntimeLoadFailedPrefix"/>）・終了コード無し・生存の 3 つで判る。
    /// </summary>
    private static bool IsAliveLoadFailure(ServerStartResult result, Process process) =>
        result.ExitCode is null
        && result.FailureReason is not null
        && result.FailureReason.StartsWith(
            ServerStateMachine.RuntimeLoadFailedPrefix, StringComparison.Ordinal)
        && !HasExited(process);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await KillChildAsync(cancellationToken).ConfigureAwait(false);

        ProcessId = null;
        BaseAddress = null;
        _machine.ApplyStopped();
    }

    /// <summary>
    /// 起こす前に断った（呼び手＝<c>MainViewModel</c> の事前検査）。<b>状態機械へ通す</b>だけで、
    /// 子は 1 つも起こしていない（<see cref="ProcessId"/> は動かさない）。
    /// </summary>
    public void ReportPreflightFailure(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _machine.ApplyFailure(reason.Trim());
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
            // **世代を進めてから殺す**＝この後に上がる Process.Exited を「自分で落とした個体の
            // 断末魔」として捨てる（捨てないと StopAsync の Stopped が直後に Failed へ覆される）。
            _generation = new object();
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

        // PDH の query は 1 本を開きっぱなしにしてある（裁定 110 D1）＝ここで閉じる。
        _osGpuMemory?.Dispose();
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
                // **stderr を読み切ってから理由を組む**（low 2）。WaitForExit(int) は
                // 非同期の読みの完了を待たない＝理由 1 行になるはずの最終行を取りこぼす。
                // 引数なしの WaitForExit() は待つが返らないことがあるので、別スレッドで
                // StderrDrainGrace までに限って待つ。
                await DrainStandardErrorAsync(process).ConfigureAwait(false);

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
            if (_machine.ApplyReadiness(
                sample.Reachable, sample.Loaded, sample.WarmupRunning, sample.RuntimeError))
            {
                return new ServerStartResult(
                    true, _machine.State, ProcessId, null, Stopwatch.GetElapsedTime(started), null);
            }

            // **読込の失敗は次の巡回を待たずに返す**（裁定 105 ⑷）。ポート先行では子が
            // 死なない＝プロセスは生きたままなので、上の HasExited もループ頭の Failed も
            // 掛からず、理由が出るのが見張り 1 巡ぶん（2 秒）遅れていた。受け入れ条件は
            // 「理由 1 行が ≤ 15 s」（D-1）である。
            if (_machine.State == ServerState.Failed)
            {
                return new ServerStartResult(
                    false, ServerState.Failed, ProcessId, null, Stopwatch.GetElapsedTime(started),
                    _machine.FailureReason);
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
        ReadinessSample sample;
        try
        {
            sample = await _readinessProbe.ProbeAsync(baseAddress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ReadinessSample(false, false, false, null, null);
        }

        // **その応答が自分の子の物か確かめる**（是正・便 D（2））。
        // 裁定 105（ポート先行）で bind は 1 秒以内に来るようになったが、窓は閉じていない＝
        // 既にそのポートを握っている個体が居れば、こちらの子が bind に失敗して落ちるまでの間、
        // 同じ形の応答が返ってくる。
        // pid が食い違う標本は**自分の物として採らない**＝状態帯（裁定 67 ⑶）に他人の
        // GPU メモリを出さず、Ready にも上げない。子はこの後 bind に失敗して落ち、
        // その行を ServerBindFailure が拾う（裁定 52 の 1 行）。
        if (sample.Status is StatusResponse status && IsForeign(status) is string foreign)
        {
            // ログは pid 1 つにつき 1 行（見張りは 2 秒ごとに来る＝同じ 1 行で
            // 受け入れ条件 D-4 の 3 行を末尾 20 行から押し出さない）。
            if (_foreignPidLogged != status.Pid)
            {
                _foreignPidLogged = status.Pid;
                LogLine?.Invoke(this, new ServerLogLineEventArgs(
                    new ServerLogEvent(ServerLogSignal.Other, foreign)));
            }

            return new ReadinessSample(false, false, false, null, foreign);
        }

        // **見張り 1 本の標本をここで公開する**（low 3）＝窓は自分で /ywk/status を叩かない。
        if (sample.Status is StatusResponse mine)
        {
            LatestStatus = mine;

            // **OS の GPU 計数も同じ回に採る**（裁定 110 D5）＝数える相手は自分の子の pid
            // （欄の無い古い個体は ProcessId で代用する）。読めなければ空になるだけ。
            LatestOsGpuMemory = _osGpuMemory?.Sample(ProcessId ?? mine.Pid) ?? [];

            StatusSampled?.Invoke(this, mine);
        }

        return sample;
    }

    /// <summary>
    /// その <c>/ywk/status</c> が<b>別の個体</b>の物なら理由 1 行（自分の物なら null）。
    /// <para>
    /// 見るのは pid だけ＝<b>欄が無ければ何も言わない</b>（古い wrapper・上流の素の Server では
    /// <c>pid</c> が null になる＝欄の欠けを「別人」と読まない）。
    /// </para>
    /// </summary>
    private string? IsForeign(StatusResponse status)
    {
        if (status.Pid is not int theirs || ProcessId is not int mine || theirs == mine)
        {
            return null;
        }

        return "ポート " + _port.ToString(CultureInfo.InvariantCulture)
            + " には別の個体（pid " + theirs.ToString(CultureInfo.InvariantCulture)
            + "）が答えています。いま起こした個体（pid " + mine.ToString(CultureInfo.InvariantCulture)
            + "）の応答ではないので、この標本は使いません。";
    }

    /// <summary>
    /// 変種の門（裁定 88 ⑴）。<b>GPU 変種のときだけ</b> torch を撃ち、
    /// <see cref="VariantGate.Decide"/> の純関数に判断を任せる。
    /// <para>
    /// 変種の綴りは <see cref="ServerStartRequest.Variant"/>＝空なら env の <c>YWK_VARIANT</c>
    /// （<c>cuda</c>／<c>cpu</c>／<c>rocm-*</c>）で代用する。どちらも無ければ門を通さない。
    /// </para>
    /// <para>
    /// <b>畳んだ名 <c>cuda</c> で入ると判断は粗くなる</b>（是正・便 D（2）＝1 巡目の註は
    /// 「勧める変種が粗くなる」としか書いていなかったが、実際に落ちていたのは<b>閾そのもの</b>
    /// だった）。いまは <see cref="DriverRequirement.Minimum"/> が畳んだ名を <b>cu130 の閾</b>
    /// （580.00）で見て、検分が読めなければ<b>起こさない</b>（<see cref="VariantGate"/> の ⑵-b）。
    /// 代わりに落ちるのは cu126 の「未実測の帯」の 1 行で、その帯のドライバは<b>断られる</b>側に
    /// 倒れる＝正しい綴りを載せたい呼び手は <see cref="ServerLaunchPlan.Build"/> を通すこと。
    /// </para>
    /// <para>
    /// <b>ここは例外を外へ出さない</b>（契約 <see cref="IServerProcess.StartAsync"/>＝
    /// 「例外は投げず結末で返す」）。1 巡目は <see cref="OperationCanceledException"/> 2 種しか
    /// 捕らず、実路の <c>GpuEnumerator</c> の期限切れ枝が <c>Kill(entireProcessTree: true)</c> を
    /// <see cref="InvalidOperationException"/> だけで守っていたので、<c>Win32Exception</c> が
    /// <c>StartAsync</c> ごと抜けて「サーバ起動」が黙って何もしないボタンになった（実射）。
    /// </para>
    /// </summary>
    private async Task<GateDecision> DecideGateAsync(
        ServerStartRequest request, CancellationToken cancellationToken)
    {
        var variant = request.Variant;
        if (string.IsNullOrWhiteSpace(variant))
        {
            request.Environment.TryGetValue(ServerEnvironment.Variant, out var label);
            variant = label ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(variant) || RuntimeVariants.IsCpu(variant) || _torchProbe is null)
        {
            return new GateDecision(true, null, null, []);
        }

        TorchGpuProbe.TorchProbeResult? probe;
        try
        {
            probe = await _torchProbe
                .ProbeAsync(request.PythonExe, VariantGate.ProbeTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            probe = null;
        }
        catch (Exception ex)
        {
            // 検分が落ちた＝「GPU が無い」ではない。理由 1 行を持った「読めなかった」検分に
            // 落として、判断は同じ純関数に任せる（cu130 は起こさない・他は注意 1 行）。
            probe = new TorchGpuProbe.TorchProbeResult(
                [], null, null, null, "GPU の検分そのものが落ちました：" + ex.Message);
        }

        return VariantGate.Decide(variant, probe, request.DriverVersion, request.InstalledVariants);
    }

    /// <summary>
    /// 子が消えた（<see cref="Process.Exited"/>）。<b>見張りの標本を待たずに</b> Failed へ落とす
    /// （裁定 88 ⑶）。世代が違えば自分で落とした個体なので何もしない。
    /// </summary>
    private async void OnChildExited(Process process, object generation)
    {
        try
        {
            if (!ReferenceEquals(Volatile.Read(ref _generation), generation))
            {
                return;
            }

            await DrainStandardErrorAsync(process).ConfigureAwait(false);

            if (!ReferenceEquals(Volatile.Read(ref _generation), generation))
            {
                return;
            }

            var exit = SafeExitCode(process) ?? -1;
            ExitCode = exit;
            _machine.ApplyExit(exit);
        }
        catch (SystemException)
        {
            // 事象の handler は何があっても投げない（投げるとプロセスごと落ちる）。
        }
    }

    /// <summary>
    /// <b>引数なし</b>の <see cref="Process.WaitForExit()"/> で非同期の読みを回収する（low 2）。
    /// 返らないことがあるので別スレッドで待ち、<see cref="StderrDrainGrace"/> で打ち切る。
    /// </summary>
    private static async Task DrainStandardErrorAsync(Process process)
    {
        var drain = Task.Run(() =>
        {
            try
            {
                process.WaitForExit();
            }
            catch (SystemException)
            {
                // 既に片付いている・ハンドルが無い＝読む物も無い
            }
        });

        // 打ち切った側の task は拾わない（返らない可能性がある）＝例外は上の catch で潰してある。
        await Task.WhenAny(drain, Task.Delay(StderrDrainGrace)).ConfigureAwait(false);
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
                    _machine.ApplyReadiness(
                        sample.Reachable, sample.Loaded, sample.WarmupRunning, sample.RuntimeError);
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
/// 起動の材料を 1 箇所で組む（<b>純関数</b>＝窓も初回取得もここを通す）。
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
    /// <param name="settings">利用者の設定（変種・ポート・暖機ほか）。</param>
    /// <param name="paths">場所の 2 つの根。</param>
    /// <param name="pythonExe">変種の <c>python.exe</c>。</param>
    /// <param name="gpuIndex">UUID を解決した結果（CPU 変種なら null）。</param>
    /// <param name="offlineHuggingFace">取得が済んでいれば真（<c>HF_HUB_OFFLINE=1</c>）。</param>
    /// <param name="driverVersion">
    /// <c>nvidia-smi</c> の <c>driver_version</c>（<see cref="GpuEnumerator.DriverVersionOf"/>）。
    /// <b>変種の門</b>の閾に使う（裁定 88 ⑵）。読めない機体は null のままでよい。
    /// </param>
    /// <param name="installedVariants">
    /// この機体に組んである変種（<see cref="VariantGate.DetectInstalled"/>）。
    /// 門が「代わりに何を勧めるか」を選ぶのに使う。
    /// </param>
    public static ServerStartRequest Build(
        LauncherSettings settings,
        AppPaths paths,
        string pythonExe,
        int? gpuIndex,
        bool offlineHuggingFace = true,
        string? driverVersion = null,
        IReadOnlyList<string>? installedVariants = null)
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
            settings.EffectiveReadyTimeout())
        {
            Variant = settings.Variant,
            DriverVersion = driverVersion,
            InstalledVariants = installedVariants ?? [],
        };
    }
}
