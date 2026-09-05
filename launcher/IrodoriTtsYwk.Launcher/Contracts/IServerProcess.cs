using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>
/// wrapper の状態機械（設計書 §2）。
/// <c>Stopped → Starting（stderr 1 行目 <c>ywk_server &lt;版&gt;</c>）→ Listening（<c>Uvicorn running on</c>）
/// → Ready（<c>runtime loaded in</c> ＋ <c>/health.runtime.loaded=true</c>）→ Warming
/// （<c>/ywk/status.warmup.state=running</c>）→ Ready</c>。
/// </summary>
public enum ServerState
{
    /// <summary>起こしていない。</summary>
    Stopped,

    /// <summary>起こした。まだ listen していない。</summary>
    Starting,

    /// <summary>listen した。モデルはまだ読み込み中（<c>/health</c> は 200・<c>runtime.loaded=false</c>）。</summary>
    Listening,

    /// <summary>合成できる（<c>runtime.loaded=true</c>）。</summary>
    Ready,

    /// <summary>暖機／事前計算が走っている（合成はできる＝本物が必ず勝つ＝契約 ⑺ 7-2）。</summary>
    Warming,

    /// <summary>落ちた・起こせなかった。理由 1 行が <see cref="IServerProcess.FailureReason"/> に入る。</summary>
    Failed,
}

/// <summary>stderr の 1 行が何を告げているか（<b>純関数</b>で判る 5 種＝受け入れ条件 D-4）。</summary>
public enum ServerLogSignal
{
    /// <summary>状態に効かない行。</summary>
    Other,

    /// <summary><c>ywk_server &lt;版&gt; upstream=… variant=… device=… precision=…</c>（起動 1 行目）。</summary>
    Banner,

    /// <summary><c>Uvicorn running on …</c>（listen した）。</summary>
    Listening,

    /// <summary><c>runtime loaded in …</c>（上流がモデルを載せ終えた）。</summary>
    RuntimeLoaded,

    /// <summary><c>ywk_server: device actual=…</c>（実測した device）。</summary>
    DeviceActual,

    /// <summary><c>ywk_server: &lt;理由&gt;</c>（事前検査の告知＝この直後に exit 2 が来る）。</summary>
    WrapperNotice,
}

/// <summary>stderr 1 行の読み（<see cref="ServerLogParser"/> の返り）。</summary>
/// <param name="Signal">種。</param>
/// <param name="Line">元の行（そのまま）。</param>
/// <param name="Version">Banner のときの配布版の版。</param>
/// <param name="Upstream">Banner のときの <c>&lt;irodori&gt;/&lt;server&gt;</c>。</param>
/// <param name="Variant">Banner のときの <c>YWK_VARIANT</c>。</param>
/// <param name="Device">Banner のときの <c>model/codec</c> device、または DeviceActual の値。</param>
/// <param name="Precision">Banner のときの精度。</param>
/// <param name="Url">Listening のときの <c>http://127.0.0.1:18088</c>。</param>
public sealed record ServerLogEvent(
    ServerLogSignal Signal,
    string Line,
    string? Version = null,
    string? Upstream = null,
    string? Variant = null,
    string? Device = null,
    string? Precision = null,
    string? Url = null);

/// <summary>
/// stderr の 1 行から状態を読む（<b>純関数＝実機なしでテストできる継ぎ目</b>。
/// 本体 <c>IrodoriServerOutput.IsCpuDeviceLine</c> と同じ流儀＝判別の知識を 1 檔に閉じる）。
/// <para>
/// 見る行は 4 本だけ（受け入れ条件 D-4・便 A（2）／便 C の逐語）＝
/// <c>ywk_server &lt;版&gt; upstream=…</c>／<c>Uvicorn running on</c>／<c>runtime loaded in</c>／
/// <c>ywk_server: device actual=</c>。
/// </para>
/// <para>
/// <b>422 の本文 echo をログに流さない</b>（受け入れ条件 D-4）＝wrapper 側で既に潰してあるが、
/// ランチャも <see cref="LooksLikeRequestEcho"/> で長い行を弾いてから記録する。
/// </para>
/// </summary>
public static class ServerLogParser
{
    private const string BannerPrefix = "ywk_server ";
    private const string NoticePrefix = "ywk_server: ";
    private const string DeviceActualMarker = "ywk_server: device actual=";
    private const string ListeningMarker = "Uvicorn running on";
    private const string RuntimeLoadedMarker = "runtime loaded in";

    /// <summary>ログに流す 1 行の上限（これを超える行は畳む＝422 の本文 5 KB 対策）。</summary>
    public const int MaxLoggedLineLength = 1000;

    /// <summary>1 行を読む。</summary>
    public static ServerLogEvent Classify(string? line)
    {
        var text = line ?? string.Empty;

        if (text.Contains(DeviceActualMarker, StringComparison.Ordinal))
        {
            return new ServerLogEvent(
                ServerLogSignal.DeviceActual,
                text,
                Device: ValueAfter(text, DeviceActualMarker));
        }

        if (text.StartsWith(BannerPrefix, StringComparison.Ordinal)
            && text.Contains("upstream=", StringComparison.Ordinal))
        {
            var rest = text[BannerPrefix.Length..];
            var space = rest.IndexOf(' ', StringComparison.Ordinal);
            var version = space < 0 ? rest : rest[..space];
            return new ServerLogEvent(
                ServerLogSignal.Banner,
                text,
                Version: version,
                Upstream: Field(text, "upstream="),
                Variant: Field(text, "variant="),
                Device: Field(text, "device="),
                Precision: Field(text, "precision="));
        }

        if (text.Contains(ListeningMarker, StringComparison.Ordinal))
        {
            return new ServerLogEvent(
                ServerLogSignal.Listening,
                text,
                Url: ValueAfter(text, ListeningMarker));
        }

        if (text.Contains(RuntimeLoadedMarker, StringComparison.Ordinal))
        {
            return new ServerLogEvent(ServerLogSignal.RuntimeLoaded, text);
        }

        if (text.StartsWith(NoticePrefix, StringComparison.Ordinal))
        {
            return new ServerLogEvent(ServerLogSignal.WrapperNotice, text);
        }

        return new ServerLogEvent(ServerLogSignal.Other, text);
    }

    /// <summary>
    /// その信号を受けたときに移る状態（後戻りはしない＝<c>Ready</c> の後に <c>Uvicorn</c> の
    /// 行が流れても <c>Listening</c> に戻さない）。効かない行なら null。
    /// <para>
    /// <b><see cref="ServerLogSignal.RuntimeLoaded"/> は状態を動かさない</b>（是正・2026-09-05）。
    /// 上流は uvicorn の lifespan でモデルを載せるので <c>runtime loaded in</c> は
    /// <b>bind より先に出る</b>（実測＝ログ 30.264 s・socket 30.271 s）。この 1 行で Ready にすると
    /// 「誰も listen していない瞬間の待機」を publish することになり、1 度も bind しない個体まで
    /// 起動成功と見なされる。Ready は <c>/health</c>・<c>/ywk/status</c> の
    /// <c>runtime.loaded=true</c>（契約 ⑵）だけが立てる＝
    /// <see cref="Services.Server.ServerStateMachine.ApplyReadiness"/>。
    /// </para>
    /// </summary>
    public static ServerState? NextState(ServerState current, ServerLogSignal signal)
    {
        var next = signal switch
        {
            ServerLogSignal.Banner => ServerState.Starting,
            ServerLogSignal.Listening => ServerState.Listening,
            _ => (ServerState?)null,
        };

        if (next is null || current is ServerState.Failed)
        {
            return null;
        }

        return next.Value > current ? next : null;
    }

    /// <summary>要求本文の echo らしい行（長すぎる＝ログに流さない）。</summary>
    public static bool LooksLikeRequestEcho(string? line) =>
        line is not null && line.Length > MaxLoggedLineLength;

    /// <summary>ランチャ自身の見張りが叩く路（<see cref="IsLauncherPollNoise"/>）。</summary>
    private static readonly string[] PolledPaths = ["/ywk/status", "/health", "/ywk/voices"];

    /// <summary>
    /// ランチャ自身の見張りが生んだ uvicorn の access log か（<b>純関数</b>）。
    /// <para>
    /// 窓は 2 秒ごとに <c>/ywk/status</c> を読む（暖機の出入りと死亡検知のため）。その 1 本ごとに
    /// uvicorn が <c>INFO:     127.0.0.1:… - "GET /ywk/status HTTP/1.1" 200 OK</c> を吐くので、
    /// 末尾 20 行は<b>約 40 秒で自分の足音に埋まり</b>、受け入れ条件 D-4 の 3 行
    /// （<c>ywk_server &lt;版&gt;</c>・<c>Uvicorn running on</c>・<c>runtime loaded in</c>）が
    /// 画面から押し出される（実測＝<c>probe/d-launch-probe.ps1</c> の 1 回目）。
    /// </para>
    /// <para>
    /// <b>落とすのは 200 で返った見張りの GET だけ</b>＝合成（<c>POST /v1/audio/speech</c>）・
    /// 422・500・暖機は 1 行も落とさない。異常は必ず残る。
    /// </para>
    /// </summary>
    public static bool IsLauncherPollNoise(string? line)
    {
        if (string.IsNullOrEmpty(line) || !line.Contains("\" 200 OK", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var path in PolledPaths)
        {
            if (line.Contains("\"GET " + path + " HTTP/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ログに残す形（長い行は畳む）。</summary>
    public static string ForLog(string? line)
    {
        var text = line ?? string.Empty;
        return text.Length <= MaxLoggedLineLength
            ? text
            : text[..MaxLoggedLineLength] + "…（" + text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 字を畳んだ）";
    }

    private static string? Field(string line, string marker)
    {
        var at = line.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var rest = line[(at + marker.Length)..];
        var space = rest.IndexOf(' ', StringComparison.Ordinal);
        var value = space < 0 ? rest : rest[..space];
        return value.Length == 0 ? null : value;
    }

    private static string? ValueAfter(string line, string marker)
    {
        var at = line.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var value = line[(at + marker.Length)..].Trim();
        return value.Length == 0 ? null : value;
    }
}

/// <summary>
/// 終了コードの読み（設計書 §2・便 A（2））＝<c>2</c> は wrapper の事前検査、<c>3</c> は上流の
/// startup 失敗。理由 1 行は stderr の最終行から採る（受け入れ条件 D-1＝≤ 15 s で UI に 1 行）。
/// </summary>
public static class ServerExitCodes
{
    /// <summary>wrapper の事前検査（範囲外 device・rocm×fp32・綴り違い）。</summary>
    public const int WrapperPrecheck = 2;

    /// <summary>上流の startup 失敗（checkpoint 不在・精度不整合）。</summary>
    public const int UpstreamStartup = 3;

    /// <summary>UI に出す理由 1 行（**純関数**）。</summary>
    public static string Describe(int exitCode, string? lastStderrLine)
    {
        var tail = string.IsNullOrWhiteSpace(lastStderrLine)
            ? null
            : ServerLogParser.ForLog(lastStderrLine.Trim());

        var head = exitCode switch
        {
            0 => "サーバが終了した。",
            WrapperPrecheck => "起動前の検査で止まった（設定を見直してください）。",
            UpstreamStartup => "モデルの読み込みに失敗した。",
            _ => "サーバが異常終了した（終了コード "
                 + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + "）。",
        };

        return tail is null ? head : head + " " + tail;
    }
}

/// <summary>子プロセスの起こし方（env は <see cref="ServerEnvironment"/> が組む）。</summary>
/// <param name="PythonExe">変種の <c>python.exe</c>。</param>
/// <param name="WorkingDirectory">作業ディレクトリ（<c>server/</c> を推奨）。</param>
/// <param name="Host">bind（<c>127.0.0.1</c> 固定＝裁定 2）。</param>
/// <param name="Port">既定 18088。塞がっていたら止まって告知（裁定 52）。</param>
/// <param name="Environment">子に載せる env の差分。</param>
/// <param name="ReadyTimeout">ready 待ち（契約 ⑵＝120 s・CPU 変種は 300 s）。</param>
public sealed record ServerStartRequest(
    string PythonExe,
    string WorkingDirectory,
    string Host,
    int Port,
    IReadOnlyDictionary<string, string> Environment,
    TimeSpan ReadyTimeout)
{
    /// <summary>
    /// 台帳の綴り（<c>cu130</c>／<c>cu126</c>／<c>cpu</c>／<c>rocm-gfx1151</c>）＝<b>変種の門</b>の入力
    /// （裁定 88 ⑴）。空なら <see cref="Environment"/> の <c>YWK_VARIANT</c> を代わりに見る。
    /// <para>
    /// <b>畳んだ名で入ると判断が変わる</b>（是正・便 D（2））＝cu130 と cu126 はどちらも
    /// <c>cuda</c> に畳まれているので、⒜ 勧める変種は <c>cpu</c> に落ち ⒝ ドライバの閾は
    /// <b>厳しい方（cu130 の 580.00）</b>が掛かり ⒞ 検分が読めなければ<b>起こさない</b>。
    /// 1 巡目はここが「勧める変種が粗くなる」だけだと書いていたが、実際には
    /// <see cref="DriverRequirement.Minimum"/> が畳んだ名に <c>null</c> を返していたので
    /// <b>閾も「未実測の帯」の 1 行も丸ごと落ちていた</b>（実射＝ドライバ 500.00 でも
    /// <c>Allow=True</c>）。cu126 を正しく走らせたい呼び手は<b>この欄に台帳の綴りを載せる</b>こと
    /// （<c>ServerLaunchPlan.Build</c> が載せる）。
    /// </para>
    /// </summary>
    public string Variant { get; init; } = string.Empty;

    /// <summary><c>nvidia-smi</c> の <c>driver_version</c>（AMD 機・ツール不在なら null）。</summary>
    public string? DriverVersion { get; init; }

    /// <summary>この機体に組んである変種（門が勧める先を選ぶのに使う）。</summary>
    public IReadOnlyList<string> InstalledVariants { get; init; } = [];

    /// <summary><c>python.exe -m ywk_server --host … --port …</c> の引数。</summary>
    public IReadOnlyList<string> Arguments =>
    [
        "-m", AppPaths.ServerModuleName,
        "--host", Host,
        "--port", Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
    ];

    /// <summary>本体・ランチャが叩く根（<c>localhost</c> は使わない＝契約 ⑴）。</summary>
    public Uri BaseAddress => new("http://" + Host + ":"
        + Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/");
}

/// <param name="Ok">ready まで届いたか。</param>
/// <param name="State">結末の状態。</param>
/// <param name="ProcessId">起こした個体（起こせなかったら null）。</param>
/// <param name="ExitCode">既に落ちていれば終了コード。</param>
/// <param name="Elapsed">spawn→ready の所要（実測 26〜29 s＝docs/radeon.md §7-1）。</param>
/// <param name="FailureReason">理由 1 行。</param>
public sealed record ServerStartResult(
    bool Ok,
    ServerState State,
    int? ProcessId,
    int? ExitCode,
    TimeSpan Elapsed,
    string? FailureReason)
{
    /// <summary>
    /// <b>止めはしないが伝える 1 行の列</b>（裁定 88 ⑵）。いまの住人は「未実測の帯」＝
    /// cu126 をドライバ 528.33 以上 537.58 未満で起こしたときの注意で、状態帯に出す。
    /// <para>
    /// <see cref="FailureReason"/> とは別物である＝理由は「起こさなかった／落ちた」ことの説明、
    /// 注意は「起こしたが知らせておく」ことの説明。既定は空（<b>null にはならない</b>）。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Notices { get; init; } = [];
}

public sealed class ServerStateChangedEventArgs(ServerState previous, ServerState current, string? reason)
    : EventArgs
{
    public ServerState Previous { get; } = previous;

    public ServerState Current { get; } = current;

    /// <summary>Failed のときの理由 1 行。</summary>
    public string? Reason { get; } = reason;
}

public sealed class ServerLogLineEventArgs(ServerLogEvent logEvent) : EventArgs
{
    public ServerLogEvent Event { get; } = logEvent;
}

/// <summary>
/// 契約 ⑷＝wrapper の子プロセス 1 個体。
/// <para>
/// <b>起こした個体だけをツリー kill する</b>（本体 <c>IrodoriServerProcessRegistry</c> の流儀＝
/// 手で起こした個体には触れない）。上流に shutdown の路は無いので、停止＝ツリー kill である
/// （設計書 §2）。終了時・変種／GPU／ポートの変更時に再起動する。
/// </para>
/// <para>
/// <b>即死を捕まえる</b>＝exit 2／3 は <c>Start</c> の待ちの中で結末になる（受け入れ条件 D-1＝
/// ≤ 15 s で理由 1 行）。
/// </para>
/// </summary>
public interface IServerProcess : IAsyncDisposable
{
    ServerState State { get; }

    /// <summary>起こした個体の pid（起こしていなければ null）。</summary>
    int? ProcessId { get; }

    /// <summary>落ちていれば終了コード。</summary>
    int? ExitCode { get; }

    /// <summary>Failed のときの理由 1 行（絶対パスを出さない）。</summary>
    string? FailureReason { get; }

    /// <summary>直前の起動の <c>ywk_server</c> 1 行目（版・上流 pin・variant・device・precision）。</summary>
    ServerLogEvent? Banner { get; }

    /// <summary>この個体を叩く根（起こしていなければ null）。</summary>
    Uri? BaseAddress { get; }

    /// <summary>
    /// <b>見張り 1 本が採った最新の <c>/ywk/status</c></b>（まだ 1 度も読めていなければ null）。
    /// <para>
    /// <b>窓はこれを読むだけにする</b>（low 3・裁定 88 ⑶）＝窓側の <c>DispatcherTimer</c> で
    /// <c>/ywk/status</c> を別に叩くと、⑴ uvicorn の access log が 2 倍になって受け入れ条件 D-4 の
    /// 3 行を末尾 20 行から押し出し ⑵ 暖機・事前計算の判定が 2 つの標本に割れる。
    /// 標本を採るのは <c>ServerProcess.StartWatch</c> の 1 本だけである。
    /// </para>
    /// </summary>
    StatusResponse? LatestStatus { get; }

    event EventHandler<ServerStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 見張りが 1 標本を読むたびに上がる（<see cref="LatestStatus"/> と同じ物）。
    /// <b>UI スレッドではない</b>＝窓は <c>Dispatcher</c> へ渡してから描くこと（§12-2 ⑴）。
    /// </summary>
    event EventHandler<StatusResponse>? StatusSampled;

    /// <summary>stderr の 1 行（畳んだ形＝<see cref="ServerLogParser.ForLog"/> を通したもの）。</summary>
    event EventHandler<ServerLogLineEventArgs>? LogLine;

    /// <summary>起こして ready まで待つ。例外は投げず結末で返す。</summary>
    Task<ServerStartResult> StartAsync(ServerStartRequest request, CancellationToken cancellationToken);

    /// <summary>ツリー kill。起こしていない・既に落ちているなら何もしない。</summary>
    Task StopAsync(CancellationToken cancellationToken);
}
