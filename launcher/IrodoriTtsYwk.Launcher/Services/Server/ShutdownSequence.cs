using System;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Server;

/// <summary>終了の後始末の結末（<see cref="ShutdownSequence.StopServerTree"/> の返り）。</summary>
/// <param name="Stopped"><see cref="IServerProcess.StopAsync"/> を最後まで待てたか。</param>
/// <param name="TimedOut">期限切れで諦めたか（<see cref="Stopped"/> は偽になる）。</param>
/// <param name="Note">ログに残す 1 行（残す物が無ければ null）。</param>
public sealed record ShutdownOutcome(bool Stopped, bool TimedOut, string? Note);

/// <summary>
/// <b>アプリの終わり方</b>（裁定 124・126 の A・<b>WPF の型に触れない</b>＝xUnit から素で撃てる）。
/// <para>
/// <b>裁定 6（配布版が常駐・× は隠すだけ）は裁定 124 で覆った。</b>司令官の評価（逐語・2026-09-10）＝
/// 「このアプリは開発者向けではない。ただのTTSエンジンとしてvoiceroid2のような動作を期待している。
/// <b>アプリ終了でサーバーを落とし、VRAMを開放せよ。</b>」
/// ⇒ 窓の × は<b>閉じてアプリを終える</b>＝トレイに隠れない・トレイに常駐しない。
/// </para>
/// <para>
/// <b>後始末は終わるまで待つ</b>＝子（wrapper）をツリー kill してからプロセスが消える。
/// 待たずに消えると、親が居なくなった python が VRAM を握ったまま残る（上流に shutdown の路が無い＝
/// 設計書 §2）。ただし<b>待ちには上限を置く</b>（<see cref="DefaultStopTimeout"/>）＝
/// 落ちない個体のために「終われないアプリ」を作らない。
/// </para>
/// <para>
/// <b>どの状態からでも同じ 1 本を通す</b>＝<c>Starting</c>／<c>Listening</c>／<c>Warming</c> の
/// 途中で窓を閉じても <see cref="IServerProcess.StopAsync"/> を呼ぶ
/// （<c>ServerProcess.StopAsync</c> は状態を見ずにツリー kill する）。
/// </para>
/// </summary>
public static class ShutdownSequence
{
    /// <summary>終了時にサーバの後始末を待つ上限。</summary>
    public static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 2 個目の起動が単一起動の錠を待つ上限（後始末の上限＋余裕）。
    /// </summary>
    public static readonly TimeSpan SecondInstanceWait = DefaultStopTimeout + TimeSpan.FromSeconds(5);

    /// <summary>
    /// <b>2 個目の起動は「1 個目が終わろうとしている」ときだけ錠を待つ</b>（<b>純関数</b>・是正・検分）。
    /// <para>
    /// <b>なぜ要るか</b>＝裁定 124 で × ＝終了になった後、終了の路は
    /// <see cref="StopServerTree"/> でツリー kill を待ち切る（最大
    /// <see cref="DefaultStopTimeout"/>）。その間<b>窓はもう画面に無いのにプロセスは生きている</b>。
    /// 合図の口（activate）は終了の頭で閉じるので、この窓で起きた 2 個目は
    /// <c>OpenExisting</c> に失敗する＝<b>合図が届かなかった</b>。そこで黙って退くと、
    /// 利用者には「アイコンを押しても何も出ない」しか見えず、本体の一括起動（裁定 103）も
    /// <c>/health</c> が上がらないまま失敗を読む。届かなかった回は<b>錠が空くのを待って
    /// 自分が 1 個目になる</b>。
    /// </para>
    /// </summary>
    /// <param name="signalReached">合図の口を開けて <c>Set</c> できたか。</param>
    public static bool SecondInstanceShouldWait(bool signalReached) => !signalReached;

    /// <summary>
    /// <b>窓の × はアプリを終える</b>（裁定 124＝裁定 6 の反転・<b>純関数</b>）。
    /// 隠す枝はもう無いので、この述語は常に偽である＝<c>MainWindow.OnClosing</c> は
    /// <c>CancelEventArgs.Cancel</c> を 1 度も真にしない。
    /// </summary>
    public static bool WindowCloseHidesToTray => false;

    /// <summary>
    /// <b>この状態で窓を閉じたらサーバを止めるか</b>（<b>純関数</b>）＝どの状態でも真。
    /// 起動中・読込中・暖機中に閉じた回こそ VRAM を握った子が残るので、
    /// 「走っていないように見える」状態を理由に飛ばさない。
    /// </summary>
    public static bool StopsServerOnExit(ServerState state) => true;

    /// <summary>
    /// 終了時の後始末（<b>同期</b>＝<c>App.OnExit</c> はここで待ち切ってから返る）。
    /// <para>
    /// 例外は投げず結末で返す＝終了の路で例外を上げると、後始末の残り
    /// （<c>DisposeAsync</c>・Mutex の解放）が走らない。
    /// </para>
    /// </summary>
    public static ShutdownOutcome StopServerTree(IServerProcess? server, TimeSpan timeout) =>
        StopServerTreeAsync(server, timeout).GetAwaiter().GetResult();

    /// <summary>同上の非同期版（試験はこちらを撃つ）。</summary>
    public static async Task<ShutdownOutcome> StopServerTreeAsync(IServerProcess? server, TimeSpan timeout)
    {
        if (server is null)
        {
            return new ShutdownOutcome(false, false, null);
        }

        var stopped = false;
        var timedOut = false;
        string? note = null;

        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await server.StopAsync(cts.Token).ConfigureAwait(false);
            stopped = true;
        }
        catch (OperationCanceledException)
        {
            // 期限切れ＝諦めて進む（次の起動でポートが塞がっていれば裁定 52 の告知が出る）
            timedOut = true;
            note = "サーバの停止が期限内に終わりませんでした（"
                + timeout.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                + " 秒）。";
        }
        catch (InvalidOperationException)
        {
            // 既に落ちている個体を止めようとしただけ
        }

        try
        {
            await server.DisposeAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // 同上
        }

        return new ShutdownOutcome(stopped, timedOut, note);
    }
}
