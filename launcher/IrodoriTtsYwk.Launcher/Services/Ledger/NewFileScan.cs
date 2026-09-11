using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IrodoriTtsYwk.Launcher.Services.Ledger;

/// <summary>
/// 新しく書いた檔を<b>1 度ずつ読むだけ</b>の段（決裁 136 ⒜・137・v2.0.1（2））。
/// </summary>
/// <param name="Done">読み終えた檔の数。</param>
/// <param name="Total">読む檔の総数（数え終わるまでは「いま見つかっている数」）。</param>
/// <param name="Counting">まだ数えている最中か（真＝<paramref name="Total"/> はまだ増える）。</param>
public sealed record NewFileScanProgress(int Done, int Total, bool Counting);

/// <summary>この段の結末（<b>成否を持たない</b>＝読めない檔が在っても段は失敗しない）。</summary>
/// <param name="Files">読み終えた檔の数。</param>
/// <param name="Bytes">読んだ量（記録用＝画面には出さない）。</param>
/// <param name="Elapsed">かかった時間。</param>
/// <param name="Skipped">開けなかった檔の数（使用中・権限）。</param>
/// <param name="Cancelled">途中でやめたか。</param>
public sealed record NewFileScanResult(
    int Files, long Bytes, TimeSpan Elapsed, int Skipped, bool Cancelled);

/// <summary>
/// 読む根 1 本（<b>枝ごとに数え方が違う</b>＝決裁 137 ⒜の是正・検分）。
/// </summary>
/// <param name="Path">根の道。</param>
/// <param name="FollowLinks">
/// 繋ぎ（<c>ReparsePoint</c>）を辿るか。
/// <para>
/// 既定は<b>辿らない</b>（展開した一式＝実体しか無いので、辿ると輪に落ちる危険だけが増える）。
/// モデルの窖（<c>huggingface_hub</c>）だけ真にする＝<c>snapshots</c> の側が繋ぎの回は、
/// <b>繋ぎを開けば指された実体が温まる</b>（＝本体が実際に開く檔はこちらである）。
/// </para>
/// </param>
/// <param name="SkipDirectory">
/// この名の枝の下は数えない（<c>blobs</c>）。
/// <b>同じ中身を 2 度読まないための錠である</b>＝<c>huggingface_hub</c> は繋ぎを作れない機体
/// （開発者モードでない Windows）では <c>blobs</c> の実体を <c>snapshots</c> へ<b>複製する</b>ので、
/// 両方を数えると 3.5 GB を 2 度読み、この段が縮めたかった待ちをそのまま倍にする。
/// </param>
public sealed record NewFileScanRoot(
    string Path, bool FollowLinks = false, string? SkipDirectory = null);

/// <summary>
/// <b>いま書いたばかりの檔を、進捗バーの下で 1 度ずつ読む</b>（決裁 136 ⒜・137）。
/// <para>
/// <b>なぜ要るか</b>＝決裁 136 が測った 115 秒の静止の本命は、Windows Defender の
/// オンアクセス初回スキャンである（展開で初めて書かれた 25,300 檔＝3.3 GB と、
/// 3 GB のモデルを、<c>open</c>／<c>LoadLibrary</c>／<c>mmap</c> の瞬間に同期でスキャンする）。
/// その代金は<b>誰かが最初に触ったときに必ず払う</b>ので、<b>どこで払うか</b>だけを選べる＝
/// 「起動の確認」で 1 行が止まったまま払うか、<b>数が動くバーの下で払うか</b>である。
/// ここは後者にするためだけに在る。
/// </para>
/// <para>
/// <b>持たない物</b>＝⑴ 設定（新しい鍵は 1 つも足さない＝決裁 137 の「設定の要らない規則」）
/// ⑵ 成否（読めない檔は数えて飛ばす＝段は失敗しない）⑶ 檔の書き換え（<b>読むだけ</b>）。
/// </para>
/// <para>
/// <b>2 度目は安い</b>＝同じ物をもう 1 度読むだけなので、途中でやめて開き直した回は
/// OS の頁キャッシュと Defender の済み印の上を走る（＝この段は冪等である）。
/// </para>
/// </summary>
public static class NewFileScan
{
    /// <summary>1 度に読む塊（檔の頭から順に流す＝<c>SequentialScan</c>）。</summary>
    public const int BufferBytes = 1 << 20;

    /// <summary>
    /// 進捗を配る間隔（<b>UI へ 25,300 回の通知を投げない</b>＝決裁 137 の求め）。
    /// 500 ms＝<b>毎秒 2 回まで</b>で、「1 秒に 1 度は必ず動く」も同時に満たす。
    /// </summary>
    public static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>同時に読む本数の上限（小さく保つ＝円盤とスキャンの列を詰まらせない）。</summary>
    public const int MaxParallelism = 4;

    /// <summary>
    /// <paramref name="sinceLastReport"/> と進みから、いま配ってよいかを決める（<b>純関数</b>）。
    /// <list type="bullet">
    /// <item><b>最後の 1 回は必ず配る</b>（<paramref name="done"/> が <paramref name="total"/> に届いた）。</item>
    /// <item>それ以外は <see cref="ReportInterval"/> を越えた回だけ＝<b>毎秒 2 回まで</b>。</item>
    /// </list>
    /// </summary>
    public static bool ShouldReport(TimeSpan sinceLastReport, int done, int total) =>
        (total > 0 && done >= total) || sinceLastReport >= ReportInterval;

    /// <summary>
    /// 読む檔を数え上げる（<b>手前の枝が無い根は黙って飛ばす</b>）。
    /// <paramref name="found"/> は数えている最中の報せ（<b>1 檔ごとには呼ばない</b>＝呼び手が間引く）。
    /// </summary>
    public static List<string> Enumerate(
        IEnumerable<string> roots,
        Action<int>? found = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        return Enumerate(
            roots.Select(static path => new NewFileScanRoot(path)), found, cancellationToken);
    }

    /// <summary>
    /// 読む檔を数え上げる（<b>根ごとの決まり付き</b>＝<see cref="NewFileScanRoot"/>）。
    /// </summary>
    public static List<string> Enumerate(
        IEnumerable<NewFileScanRoot> roots,
        Action<int>? found = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (root is null || string.IsNullOrWhiteSpace(root.Path) || !Directory.Exists(root.Path))
            {
                continue;
            }

            IEnumerable<string> walk;
            try
            {
                walk = Directory.EnumerateFiles(root.Path, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,

                    // 繋ぎを辿る根は**深さに錠を掛ける**（辿る枝が輪を作っても止まる）。
                    AttributesToSkip = root.FollowLinks
                        ? FileAttributes.None
                        : FileAttributes.ReparsePoint,
                    MaxRecursionDepth = root.FollowLinks ? 32 : int.MaxValue,
                });
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var skip = string.IsNullOrEmpty(root.SkipDirectory)
                ? null
                : Path.DirectorySeparatorChar + root.SkipDirectory + Path.DirectorySeparatorChar;

            foreach (var file in walk)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (skip is not null
                    && file.Contains(skip, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 根が重なっても 1 度しか読まない（同じ道は 2 度数えない）。
                if (!seen.Add(file))
                {
                    continue;
                }

                files.Add(file);
                found?.Invoke(files.Count);
            }
        }

        return files;
    }

    /// <summary>
    /// 檔を 1 つ読み切る（既定の読み手＝<b>中身は捨てる</b>・戻りは読めたバイト数）。
    /// 開けない檔は <c>-1</c>（＝飛ばした）。
    /// </summary>
    public static long ReadOnce(string path) => ReadOnce(path, CancellationToken.None);

    /// <summary>
    /// 檔を 1 つ読み切る（<b>やめる合図は塊ごとに見る</b>＝決裁 137 ⒜の是正・検分）。
    /// <para>
    /// <b>なぜ「開くだけ」ではなく頭から流すか</b>＝Defender は <c>open</c> の時点で
    /// <b>檔の全体</b>を走査するので、代金そのものは最初の 1 バイトで払い終わる。
    /// それでも通して読むのは ⑴ 走査が<b>終わってから</b>この檔を「済み」と数えたいから
    /// （開いた直後に閉じると、走査の途中で次の檔へ進み、列が詰まって画面の数だけが走る）
    /// ⑵ NVMe で毎 GB 2 秒ほどの安い上乗せだから、の 2 つである。
    /// <b>これは選択であって必然ではない</b>＝遅い円盤で高く付くと判った回は、
    /// ここを「大きい檔は頭の 1 塊だけ」に替えてよい（そのときはこの註を書き替えること）。
    /// </para>
    /// <para>
    /// <b>塊ごとに合図を見る</b>＝3 GB の檔を読んでいる最中に〔やめる〕を押した回に、
    /// その檔を読み切るまで（＝初回の走査つきで数十秒）押し手を待たせない。
    /// 途中でやめた檔は次の回にもう 1 度読むだけで済む（この段は冪等）。
    /// </para>
    /// </summary>
    public static long ReadOnce(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BufferBytes,
                FileOptions.SequentialScan);

            var buffer = new byte[BufferBytes];
            long total = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (cancellationToken.IsCancellationRequested)
                {
                    return total;
                }
            }

            return total;
        }
        catch (IOException)
        {
            return -1;
        }
        catch (UnauthorizedAccessException)
        {
            return -1;
        }
    }

    /// <summary>
    /// 根の下の檔を<b>すべて 1 度ずつ読む</b>（<b>UI の綱では 1 バイトも読まない</b>＝
    /// 数え上げも読みも <see cref="Task.Run(Action)"/> の中）。
    /// </summary>
    /// <param name="roots">読む根（存在しない根は黙って飛ばす）。</param>
    /// <param name="progress">進捗（<see cref="ShouldReport"/> で間引いて配る）。</param>
    /// <param name="parallelism">同時に読む本数（1〜<see cref="MaxParallelism"/> に丸める）。</param>
    /// <param name="reader">
    /// 檔 1 つを読む手（試験の継ぎ目＝既定は <see cref="ReadOnce"/>）。
    /// <b>この手は必ず UI の綱の外で呼ばれる</b>。
    /// </param>
    /// <param name="cancellationToken">〔やめる〕（＝途中でやめても次に続きから安く走る）。</param>
    public static Task<NewFileScanResult> RunAsync(
        IReadOnlyList<string> roots,
        IProgress<NewFileScanProgress>? progress = null,
        int parallelism = 2,
        Func<string, CancellationToken, long>? reader = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        return RunAsync(
            [.. roots.Select(static path => new NewFileScanRoot(path))],
            progress,
            parallelism,
            reader,
            cancellationToken);
    }

    /// <summary>
    /// 根の下の檔を<b>すべて 1 度ずつ読む</b>（<b>根ごとの決まり付き</b>＝
    /// <see cref="NewFileScanRoot"/>）。
    /// </summary>
    public static async Task<NewFileScanResult> RunAsync(
        IReadOnlyList<NewFileScanRoot> roots,
        IProgress<NewFileScanProgress>? progress = null,
        int parallelism = 2,
        Func<string, CancellationToken, long>? reader = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var read = reader ?? ReadOnce;
        var workers = Math.Clamp(parallelism, 1, MaxParallelism);
        var watch = Stopwatch.StartNew();

        List<string> files;
        try
        {
            files = await Task.Run(
                () =>
                {
                    var last = TimeSpan.Zero;
                    return Enumerate(
                        roots,
                        count =>
                        {
                            var now = watch.Elapsed;
                            if (now - last < ReportInterval)
                            {
                                return;
                            }

                            last = now;
                            progress?.Report(new NewFileScanProgress(0, count, Counting: true));
                        },
                        cancellationToken);
                },
                cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            watch.Stop();
            return new NewFileScanResult(0, 0, watch.Elapsed, 0, Cancelled: true);
        }

        var total = files.Count;
        if (total == 0)
        {
            watch.Stop();
            progress?.Report(new NewFileScanProgress(0, 0, Counting: false));
            return new NewFileScanResult(0, 0, watch.Elapsed, 0, Cancelled: false);
        }

        var next = -1;
        var done = 0;
        var skipped = 0;
        long bytes = 0;
        var gate = new object();
        var lastReport = TimeSpan.Zero;

        void Pump()
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var index = Interlocked.Increment(ref next);
                if (index >= total)
                {
                    return;
                }

                // **読みの中でも合図を見る**（塊ごと＝大きい檔の途中でも〔やめる〕が効く）。
                var size = read(files[index], cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (size < 0)
                {
                    Interlocked.Increment(ref skipped);
                }
                else
                {
                    Interlocked.Add(ref bytes, size);
                }

                var doneNow = Interlocked.Increment(ref done);

                // **通知は間引く**＝25,300 回の束縛更新を投げない（決裁 137）。
                NewFileScanProgress? report = null;
                lock (gate)
                {
                    var now = watch.Elapsed;
                    if (ShouldReport(now - lastReport, doneNow, total))
                    {
                        lastReport = now;
                        report = new NewFileScanProgress(doneNow, total, Counting: false);
                    }
                }

                if (report is not null)
                {
                    progress?.Report(report);
                }
            }
        }

        // **読みは UI の綱の外**＝この 1 行がこの檔の要である。
        var running = new Task[workers];
        for (var i = 0; i < workers; i++)
        {
            running[i] = Task.Run(Pump, CancellationToken.None);
        }

        await Task.WhenAll(running).ConfigureAwait(true);
        watch.Stop();
        var cancelled = cancellationToken.IsCancellationRequested;

        var finished = Volatile.Read(ref done);
        if (!cancelled)
        {
            progress?.Report(new NewFileScanProgress(finished, total, Counting: false));
        }

        return new NewFileScanResult(
            finished, Interlocked.Read(ref bytes), watch.Elapsed, Volatile.Read(ref skipped), cancelled);
    }
}
