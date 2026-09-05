using System;
using System.Globalization;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Server;

/// <summary>
/// 起動ログから bind の失敗を読む（<b>純関数</b>・裁定 52）。
/// <para>
/// ポートが塞がっていたら<b>次を探さずに止まって告知する</b>。検知の路は 2 本＝
/// ⑴ 起こす前の TCP 検査（<see cref="TcpPortProbe"/>）⑵ uvicorn が吐く bind 失敗の行。
/// ⑴ だけでは、検査と起動の間に他人が掴む狭い窓が残るので ⑵ も見る。
/// </para>
/// <para>
/// 契約の <see cref="ServerLogSignal"/> に信号を足していないのは、骨組み席の契約を
/// 実装席が書き換えないため（開けた口は <see cref="ServerProcess"/> の中だけ）。
/// </para>
/// <para>
/// <b>裸の数字 <c>10048</c> は標識にしない</b>（是正・2026-09-05）。健全に走っている個体は
/// その 4 桁を平気で吐く＝<c>… (GPU 0; 10048.00 MiB total capacity)</c>・
/// <c>INFO:     127.0.0.1:10048 - "GET /ywk/status HTTP/1.1" 200 OK</c>・
/// <c>synthesis done in 10048 ms</c>。数字だけで Failed に落とすと、理由が嘘のまま
/// 終端状態（<see cref="ServerStateMachine.ApplyReadiness"/> が即 false）に落ちる。
/// ⇒ <c>10048</c> は <b>bind／socket／WSAEADDRINUSE と同居する行に限る</b>。
/// </para>
/// </summary>
public static class ServerBindFailure
{
    /// <summary>WSAEADDRINUSE（Windows）。</summary>
    public const int WsaAddressInUse = 10048;

    /// <summary>その 1 行だけで bind 失敗と断じてよい標識。</summary>
    private static readonly string[] Markers =
    [
        "error while attempting to bind on address",
        "address already in use",
        "only one usage of each socket address",
        "wsaeaddrinuse",
    ];

    /// <summary><c>10048</c> が bind の話であることを裏づける同居語。</summary>
    private static readonly string[] ErrnoContext =
    [
        "bind",
        "socket",
        "wsaeaddrinuse",
        "ソケット",
        "アドレス",
    ];

    /// <summary>その 1 行が「ポートが塞がっている」を告げているか。</summary>
    public static bool Detect(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        foreach (var marker in Markers)
        {
            if (line.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 日本語ロケールの WSAEADDRINUSE（"通常、各ソケット アドレスに対してプロトコル…"）
        if (line.Contains("各ソケット", StringComparison.Ordinal))
        {
            return true;
        }

        // 裸の 10048 は「bind の話だ」と裏づける語と同居するときだけ採る。
        if (!line.Contains("10048", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var context in ErrnoContext)
        {
            if (line.Contains(context, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>UI に出す理由 1 行（<b>次のポートを探さない</b>ことを明言する）。</summary>
    public static string Message(int port) =>
        "ポート " + port.ToString(CultureInfo.InvariantCulture)
        + " は既に使われています。別のアプリを閉じるか、設定でポートを変えてください"
        + "（自動では別のポートを探しません）。";
}

/// <summary>
/// 状態機械の本体（<b>子プロセスを持たない＝実機なしでテストできる</b>）。
/// <para>
/// 設計書 §2 の <c>Stopped → Starting → Listening → Ready → Warming</c> を、
/// 契約の純関数（<see cref="ServerLogParser.Classify"/>／<see cref="ServerLogParser.NextState"/>）で
/// 進める。<see cref="ServerProcess"/> はこの箱に「stderr の行」「readiness の標本」「終了コード」を
/// 流し込むだけ。
/// </para>
/// <para>
/// <b>後戻りしない</b>のは契約の <see cref="ServerLogParser.NextState"/> の規律。ただし
/// <c>Warming → Ready</c> だけは戻る（暖機は終わる）＝<see cref="ApplyReadiness"/> が扱う。
/// </para>
/// <para>
/// <b>Ready は HTTP の標本だけが立てる</b>（是正・2026-09-05）。上流は uvicorn の lifespan で
/// モデルを載せるので <c>runtime loaded in</c> は <b>bind より先に出る</b>（実測＝ログの
/// 30.264 s に対し socket が 30.271 s）。ログ 1 行で Ready にすると「誰も listen していない
/// 瞬間の待機」を publish することになり、1 度も bind しない個体・直後に exit 3 する個体まで
/// 「起動しました」と記帳される。⇒ <c>runtime loaded in</c> は
/// <see cref="RuntimeLoadedSeen"/> の印を立てるだけにし、Ready への昇格は
/// <c>/health</c>・<c>/ywk/status</c> の <c>runtime.loaded=true</c> が来た標本にだけ許す（契約 ⑵）。
/// </para>
/// </summary>
public sealed class ServerStateMachine
{
    /// <summary>
    /// Ready／Warming から降ろすまでに要る「届かない」標本の数（是正・2026-09-05）。
    /// プロセスが生きていても応答が消えた個体（HIP のハング・event loop の詰まり）を
    /// 「待機」のまま放置しない。
    /// <para>
    /// <b>実効は「見張りの間隔＋1 標本の代金」×この数</b>（是正・便 D（2）＝1 巡目の註は
    /// 「2 秒間隔なので 3 標本＝約 6 秒」と書いていたが、<b>1 標本そのものが只ではない</b>）。
    /// 誰も listen していない相手への HTTP は 1 本 2.0 秒（実測）かかり、1 巡目の 1 標本は
    /// <c>/ywk/status</c> と <c>/health</c> の 2 本＝4.0 秒だったので、
    /// <b>実測は 18.0 秒</b>だった。⒜ 応答が 1 つも返らない相手に 2 本目を撃たない
    /// ⒝ 接続の期限 1.5 秒（<c>WrapperClient.ConnectTimeout</c>）の 2 つで
    /// <b>1 標本 1.5 秒・降ろすまで 10.5 秒</b>（実測）になった。
    /// </para>
    /// </summary>
    public const int UnreachableSamplesToDowngrade = 3;

    /// <summary>降ろすときに出す理由 1 行。</summary>
    public const string UnreachableReason =
        "サーバが応答しなくなりました（プロセスは生きています）。停止してから起こし直してください。";

    private readonly object _gate = new();

    private int _unreachable;

    public ServerState State { get; private set; } = ServerState.Stopped;

    /// <summary>
    /// <b>自分の子が <c>Uvicorn running on …</c> を吐いたか</b>（是正・便 D（2））。
    /// <para>
    /// bind 失敗の検知を切る条件は「listen した」ではなく<b>「自分の子が listen した」</b>である。
    /// wrapper は <c>preload=true</c> でモデルを載せてから bind する（この機体で 20〜28 s）ので、
    /// その窓の間に<b>他人が同じポートを握る</b>と、ランチャは他人の <c>/ywk/status</c> を読んで
    /// <c>Listening</c>／<c>Ready</c> へ上がってしまう。1 巡目はそこで検知を切っていたため、
    /// 後から流れてくる<b>自分の子の bind 失敗の行を無視した</b>（実射＝他席の HttpListener が
    /// 18098 を握った走行で「10.1 s で待機」＝清浄な走行の 27.5〜27.8 s の 3 分の 1）。
    /// この印は<b>子の口（pipe）から来た行でしか立たない</b>ので、他人の応答では立たない。
    /// </para>
    /// </summary>
    public bool OwnListenLineSeen { get; private set; }

    /// <summary>
    /// <c>runtime loaded in</c> を見たか（<b>Ready の根拠にはしない</b>＝印だけ）。
    /// bind の前に出うるので、これが真でも listen しているとは限らない。
    /// </summary>
    public bool RuntimeLoadedSeen { get; private set; }

    /// <summary>直前の起動の <c>ywk_server &lt;版&gt;</c> 1 行目。</summary>
    public ServerLogEvent? Banner { get; private set; }

    /// <summary>Failed のときの理由 1 行。</summary>
    public string? FailureReason { get; private set; }

    /// <summary>直近の stderr 1 行（<see cref="ServerExitCodes.Describe"/> の材料）。</summary>
    public string? LastStderrLine { get; private set; }

    /// <summary>実測した device（<c>ywk_server: device actual=</c>）。</summary>
    public string? DeviceActual { get; private set; }

    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;

    /// <summary>ログの 1 行（畳んだ形で載る）。</summary>
    public event EventHandler<ServerLogLineEventArgs>? LogLine;

    /// <summary>起動のたびに真っさらへ戻す。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            State = ServerState.Stopped;
            Banner = null;
            FailureReason = null;
            LastStderrLine = null;
            DeviceActual = null;
            RuntimeLoadedSeen = false;
            OwnListenLineSeen = false;
            _unreachable = 0;
        }
    }

    /// <summary>
    /// stderr／stdout の 1 行を流す。返りは読んだ内容（呼ぶ側がログに書く）。
    /// <paramref name="port"/> は bind 失敗の告知に使う。
    /// </summary>
    public ServerLogEvent ApplyLogLine(string? line, bool isStandardError, int port)
    {
        var read = ServerLogParser.Classify(line);
        ServerStateChangedEventArgs? changed = null;

        lock (_gate)
        {
            if (isStandardError && !string.IsNullOrWhiteSpace(line))
            {
                LastStderrLine = line;
            }

            if (read.Signal == ServerLogSignal.Banner)
            {
                Banner = read;
            }
            else if (read.Signal == ServerLogSignal.DeviceActual && read.Device is not null)
            {
                DeviceActual = read.Device;
            }
            else if (read.Signal == ServerLogSignal.RuntimeLoaded)
            {
                // 印を立てるだけ＝Ready への昇格は readiness の標本に任せる。
                RuntimeLoadedSeen = true;
            }

            if (read.Signal == ServerLogSignal.Listening)
            {
                // **自分の子**が bind した（この行は子の口からしか来ない）。
                OwnListenLineSeen = true;
            }

            // bind 失敗は自分の子が listen する前にしか来ない。listen した後も掛け続けると、
            // 走行中の 1 行（OOM・access log・所要）が状態を Failed へ落としうる。
            // **切る条件は状態ではなく「自分の子の Uvicorn の行を見たか」**（是正・便 D（2））＝
            // 他人が同じポートを握っていると状態だけが先に上がり、後から来る子の bind 失敗を
            // 取りこぼした（<see cref="OwnListenLineSeen"/> の註）。
            if (!OwnListenLineSeen && ServerBindFailure.Detect(line))
            {
                changed = MoveToUnlocked(ServerState.Failed, ServerBindFailure.Message(port));
            }
            else if (ServerLogParser.NextState(State, read.Signal) is ServerState next)
            {
                changed = MoveToUnlocked(next, null);
            }
        }

        // ログは 1 行ずつ・長い行は畳む（422 の本文 echo をログに流さない＝受け入れ条件 D-4）
        LogLine?.Invoke(this, new ServerLogLineEventArgs(
            read with { Line = ServerLogParser.ForLog(read.Line) }));
        Raise(changed);
        return read;
    }

    /// <summary>
    /// <c>/health</c>／<c>/ywk/status</c> の 1 標本を流す。
    /// <paramref name="loaded"/>＝<c>runtime.loaded</c>・<paramref name="warmupRunning"/>＝
    /// <c>warmup.state=running</c>（事前計算の走行も同じ扱い＝どちらも「暖機中」と見せる）。
    /// <para>
    /// <b>返りは「いま合成を撃てるか」</b>＝<paramref name="reachable"/> が偽の標本で真を返さない
    /// （是正・2026-09-05）。返り値は <see cref="ServerProcess"/> の ready 待ちの結末そのもので、
    /// これが状態だけで決まっていたため、1 度も listen しない個体でも「起動しました」で抜けていた。
    /// </para>
    /// <para>
    /// <b>届かなくなったら降ろす</b>＝<see cref="UnreachableSamplesToDowngrade"/> 回続けて
    /// 到達不能なら Ready／Warming から <c>Listening</c> へ落とし、理由 1 行を添える。
    /// <b>殺しはしない</b>（プロセスが生きている間は告知だけ＝本体の流儀）。
    /// </para>
    /// </summary>
    public bool ApplyReadiness(bool reachable, bool loaded, bool warmupRunning)
    {
        ServerStateChangedEventArgs? changed = null;
        bool serving;
        lock (_gate)
        {
            if (State is ServerState.Failed or ServerState.Stopped)
            {
                return false;
            }

            if (reachable)
            {
                _unreachable = 0;

                if (State < ServerState.Listening)
                {
                    changed = MoveToUnlocked(ServerState.Listening, null);
                }

                if (loaded)
                {
                    var want = warmupRunning ? ServerState.Warming : ServerState.Ready;
                    if (State != want && State is ServerState.Listening or ServerState.Ready
                        or ServerState.Warming or ServerState.Starting)
                    {
                        changed = MoveToUnlocked(want, null);
                    }
                }
            }
            else
            {
                _unreachable++;
                if (_unreachable >= UnreachableSamplesToDowngrade
                    && State is ServerState.Ready or ServerState.Warming)
                {
                    _unreachable = 0;
                    changed = MoveToUnlocked(ServerState.Listening, UnreachableReason);
                }
            }

            // 「合成を撃てる」＝いま届いていて、かつ載っている個体だけ。
            serving = reachable && State is ServerState.Ready or ServerState.Warming;
        }

        Raise(changed);
        return serving;
    }

    /// <summary>子プロセスが終わった（理由 1 行を組む＝受け入れ条件 D-1）。</summary>
    public void ApplyExit(int exitCode)
    {
        ServerStateChangedEventArgs? changed;
        lock (_gate)
        {
            var reason = FailureReason ?? ServerExitCodes.Describe(exitCode, LastStderrLine);
            changed = MoveToUnlocked(ServerState.Failed, reason);
        }

        Raise(changed);
    }

    /// <summary>起こせなかった／期限切れ（理由 1 行はそのまま）。</summary>
    public void ApplyFailure(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ServerStateChangedEventArgs? changed;
        lock (_gate)
        {
            changed = MoveToUnlocked(ServerState.Failed, reason);
        }

        Raise(changed);
    }

    /// <summary>止めた（Stopped は Failed からも戻れる唯一の遷移）。</summary>
    public void ApplyStopped()
    {
        ServerStateChangedEventArgs? changed;
        lock (_gate)
        {
            FailureReason = null;
            changed = MoveToUnlocked(ServerState.Stopped, null);
        }

        Raise(changed);
    }

    /// <summary>起こした（Stopped → Starting）。</summary>
    public void ApplyStarted()
    {
        ServerStateChangedEventArgs? changed;
        lock (_gate)
        {
            _unreachable = 0;
            changed = MoveToUnlocked(ServerState.Starting, null);
        }

        Raise(changed);
    }

    private ServerStateChangedEventArgs? MoveToUnlocked(ServerState next, string? reason)
    {
        if (State == next && (reason is null || reason == FailureReason))
        {
            return null;
        }

        var previous = State;
        State = next;
        FailureReason = next == ServerState.Failed ? reason ?? FailureReason : null;

        // Failed 以外でも理由 1 行は運ぶ（「届かなくなった」の降格は Failed ではないが告知が要る）。
        return new ServerStateChangedEventArgs(previous, next, FailureReason ?? reason);
    }

    private void Raise(ServerStateChangedEventArgs? args)
    {
        if (args is not null)
        {
            StateChanged?.Invoke(this, args);
        }
    }
}
