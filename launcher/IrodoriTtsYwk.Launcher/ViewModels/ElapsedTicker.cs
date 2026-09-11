using System;
using System.Threading;
using System.Threading.Tasks;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// <b>待っている秒を 1 秒ごとに動かす</b>（決裁 137 ⒝⒞・憲章 §2 原則 3＝<b>数つきで正直に待つ</b>）。
/// <para>
/// <b>なぜ要るか</b>＝所有者の受け入れ条件は「<b>使用者からは固まった（フリーズした）ように
/// 見えなければよい</b>」であり、その測り方の ⑶ が「<b>目に見えて動く物</b>（経過秒・檔の数）が
/// 在れば、1 行が 5 秒を越えて変わらなくても固まってはいない」である。読み込みの待ちは
/// 数分に延びうるので、動くのはこの秒だけになる回がある。
/// </para>
/// <para>
/// <b>UI の綱を 1 度も塞がない</b>＝<see cref="Task.Delay(TimeSpan, CancellationToken)"/> を
/// <c>ConfigureAwait(true)</c> で待つ非同期の輪であり、<c>Thread.Sleep</c> も
/// <c>DispatcherTimer</c> も使わない（<b>窓の無い試験でもそのまま回る</b>＝
/// <see cref="Delay"/> を差し替えれば 1 秒も待たずに刻める）。
/// </para>
/// <para>
/// <b>文は組まない</b>＝ここは数だけを持つ。文言は <see cref="BandText"/>／
/// <see cref="FirstRunViewModel"/> の<b>純関数</b>が組む。
/// </para>
/// </summary>
public sealed class ElapsedTicker
{
    private CancellationTokenSource? _cancel;

    /// <summary>いま何秒目か（止まっている間は 0）。</summary>
    public int Seconds { get; private set; }

    /// <summary>刻んでいる最中か。</summary>
    public bool IsRunning => _cancel is not null;

    /// <summary>
    /// 1 刻みの待ち（試験の継ぎ目＝既定は 1 秒）。
    /// <b>取消で例外を投げてよい</b>（輪はそれを終わりとして受ける）。
    /// </summary>
    public Func<CancellationToken, Task> Delay { get; set; } =
        static token => Task.Delay(TimeSpan.FromSeconds(1), token);

    /// <summary>1 秒進んだ（<b>呼ばれるのは始めた側の綱</b>＝UI の綱）。</summary>
    public event EventHandler? Ticked;

    /// <summary>
    /// 刻み始める（<b>もう走っていれば何もしない</b>＝二重に走らせない）。
    /// </summary>
    public void Start()
    {
        if (_cancel is not null)
        {
            return;
        }

        Seconds = 0;
        var source = new CancellationTokenSource();
        _cancel = source;
        _ = LoopAsync(source);
    }

    /// <summary>止めて 0 に戻す（止まっている物をもう 1 度止めても何も起きない）。</summary>
    public void Stop()
    {
        var source = _cancel;
        _cancel = null;
        Seconds = 0;

        if (source is null)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 既に片づいている＝止まっている。
        }

        source.Dispose();
    }

    private async Task LoopAsync(CancellationTokenSource source)
    {
        var token = source.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                // **待つのは綱を渡さずに**（ConfigureAwait(true)）＝戻ってくる先は始めた綱である。
                await Delay(token).ConfigureAwait(true);

                if (token.IsCancellationRequested || !ReferenceEquals(_cancel, source))
                {
                    return;
                }

                Seconds++;
                Ticked?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // 〔やめる〕・段が終わった＝止まるのが正しい。
        }
    }
}
