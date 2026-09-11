using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Ledger;
using IrodoriTtsYwk.Launcher.Services.Server;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// <b>決裁 137＝「固まったように見えない」ことの試験</b>（v2.0.1（2））。
/// <para>
/// 所有者の受け入れ条件は<b>逐語</b>で
/// 「使用者からは固まった(フリーズした)ように見えなければよい。」であり、測り方は
/// ⑴ <c>FirstRunProgressText</c>／<c>FirstRunPhaseText</c>／<c>FirstRunStepTitle</c> の
/// どれも変わらない最長の間が 5 秒以下 ⑵ <c>IsHungAppWindow</c> が 1 度も真にならない
/// ⑶ <b>目に見えて動く物</b>（経過秒・檔の数）が在れば ⑴ を越えても「固まっていない」。
/// </para>
/// <para>
/// ここで釘付けするのはその ⑶ を作る 3 つ＝⑴ 新しいファイルを確認する段（数が動く）
/// ⑵ 待っている秒（1 秒ごとに動く）⑶ <b>UI の綱を塞がない</b>（読みは綱の外・通知は間引く）。
/// <b>実 GPU・実ポート・外への取得には 1 つも触れない。</b>
/// </para>
/// </summary>
public sealed class Decision137FileScanTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-d137-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 掃除は best effort
        }
    }

    [Fact]
    public async Task 根の下の檔をすべて1度ずつ読む()
    {
        var tree = MakeTree(files: 40, bytesEach: 64);
        var reads = new List<string>();
        var gate = new object();

        var result = await NewFileScan.RunAsync(
            [tree],
            progress: null,
            parallelism: 2,
            reader: (path, token) =>
            {
                lock (gate)
                {
                    reads.Add(path);
                }

                return NewFileScan.ReadOnce(path, token);
            });

        Assert.Equal(40, result.Files);
        Assert.Equal(40, reads.Count);
        Assert.Equal(40, reads.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(40 * 64, result.Bytes);
        Assert.Equal(0, result.Skipped);
        Assert.False(result.Cancelled);
    }

    [Fact]
    public async Task 無い根は黙って飛ばす()
    {
        var result = await NewFileScan.RunAsync(
            [Path.Combine(_root, "there-is-no-such-tree"), string.Empty]);

        Assert.Equal(0, result.Files);
        Assert.False(result.Cancelled);
    }

    /// <summary>
    /// <b>2 度目も走る（冪等）</b>＝〔やめる〕で止めた回は、次に開いたときに同じ物をもう 1 度
    /// 読むだけで済む（檔は 1 つも書き換えないので、続きの管理も要らない）。
    /// </summary>
    [Fact]
    public async Task 途中でやめられて2度目も同じに走る()
    {
        var tree = MakeTree(files: 60, bytesEach: 16);
        using var cancel = new CancellationTokenSource();
        var seen = 0;

        var first = await NewFileScan.RunAsync(
            [tree],
            progress: null,
            parallelism: 1,
            reader: (path, _) =>
            {
                if (Interlocked.Increment(ref seen) == 5)
                {
                    cancel.Cancel();
                }

                return 1;
            },
            cancellationToken: cancel.Token);

        Assert.True(first.Cancelled);
        Assert.True(first.Files < 60, "やめたのに全部読んでいる：" + first.Files);

        var second = await NewFileScan.RunAsync([tree]);
        Assert.Equal(60, second.Files);
        Assert.False(second.Cancelled);
    }

    /// <summary>
    /// <b>読みは UI の綱の外</b>（決裁 137 ⒞）＝掴んだ綱（<see cref="SynchronizationContext"/>）の
    /// 上では 1 バイトも読まない。<b>これが破れると、確認の段そのものが窓を固める。</b>
    /// </summary>
    [Fact]
    public async Task 読みはUIの綱の外で走る()
    {
        var tree = MakeTree(files: 8, bytesEach: 8);
        var previous = SynchronizationContext.Current;
        var ui = new RecordingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(ui);
        try
        {
            var uiThread = Environment.CurrentManagedThreadId;
            var contexts = new List<SynchronizationContext?>();
            var threads = new List<int>();
            var gate = new object();

            var result = await NewFileScan.RunAsync(
                [tree],
                progress: null,
                parallelism: 1,
                reader: (path, _) =>
                {
                    lock (gate)
                    {
                        contexts.Add(SynchronizationContext.Current);
                        threads.Add(Environment.CurrentManagedThreadId);
                    }

                    return 1;
                });

            Assert.Equal(8, result.Files);
            Assert.All(contexts, Assert.Null);
            Assert.DoesNotContain(uiThread, threads);

            // 戻り先は掴んだ綱＝`ConfigureAwait(true)` で待っている（1 行も投げっぱなしにしない）。
            Assert.True(ui.Posts > 0, "掴んだ綱へ 1 度も戻っていない");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>
    /// <b>通知は毎秒 2 回まで</b>（決裁 137 ⒜の求め＝25,300 回の束縛更新を投げない）。
    /// </summary>
    [Fact]
    public void 間引きの規則は純関数である()
    {
        // 最後の 1 回は間隔にかかわらず必ず配る（＝100 % が出ないまま終わらない）。
        Assert.True(NewFileScan.ShouldReport(TimeSpan.Zero, 25300, 25300));

        // 途中は 500 ms を越えた回だけ＝毎秒 2 回まで。
        Assert.False(NewFileScan.ShouldReport(TimeSpan.FromMilliseconds(1), 1, 25300));
        Assert.False(NewFileScan.ShouldReport(TimeSpan.FromMilliseconds(499), 12000, 25300));
        Assert.True(NewFileScan.ShouldReport(TimeSpan.FromMilliseconds(500), 12000, 25300));
        Assert.Equal(TimeSpan.FromMilliseconds(500), NewFileScan.ReportInterval);
    }

    [Fact]
    public async Task 通知は毎秒2回までに間引かれる()
    {
        var tree = MakeTree(files: 400, bytesEach: 4);
        var reports = 0;
        var progress = new Progress<NewFileScanProgress>(_ => Interlocked.Increment(ref reports));
        var watch = Stopwatch.StartNew();

        var result = await NewFileScan.RunAsync([tree], progress, parallelism: 2, reader: (_, _) => 4);
        watch.Stop();

        // `Progress<T>` は綱へ投げるので、数え終わるまで少し待つ（投げた分は必ず届く）。
        await Task.Delay(200);

        Assert.Equal(400, result.Files);
        var allowed = (2 * (int)Math.Ceiling(watch.Elapsed.TotalSeconds)) + 2;
        Assert.True(
            Volatile.Read(ref reports) <= allowed,
            "通知が多すぎる：" + reports + " 通 / " + watch.Elapsed.TotalSeconds + " 秒（上限 " + allowed + "）");
        Assert.True(Volatile.Read(ref reports) >= 1, "最後の 1 通が届いていない");
    }

    /// <summary>
    /// <b>大きい 1 檔の途中でも〔やめる〕が効く</b>（是正・検分）。
    /// <para>
    /// 合図を檔と檔の間でしか見ていなかったころは、3 GB の <c>model.safetensors</c> を
    /// 読んでいる最中に押された取消が、その檔を読み切るまで（＝初回の走査つきで数十秒）
    /// 効かなかった。その間は <c>IsBusy</c> が真のままで、押せる釦が 1 つも無かった。
    /// </para>
    /// </summary>
    [Fact]
    public async Task 大きい檔の途中でもやめられる()
    {
        var tree = MakeTree(files: 4, bytesEach: 8);
        using var cancel = new CancellationTokenSource();
        var inside = new SemaphoreSlim(0);
        var watch = Stopwatch.StartNew();

        var run = NewFileScan.RunAsync(
            [tree],
            progress: null,
            parallelism: 1,
            reader: (_, token) =>
            {
                // 「まだ読み終わっていない 1 檔」の代わり＝合図が来るまで塊を繰り返す。
                inside.Release();
                for (var i = 0; i < 2000; i++)
                {
                    if (token.IsCancellationRequested)
                    {
                        return i;
                    }

                    Thread.Sleep(1);
                }

                return -1;
            },
            cancellationToken: cancel.Token);

        Assert.True(await inside.WaitAsync(TimeSpan.FromSeconds(5)));
        cancel.Cancel();
        var result = await run;
        watch.Stop();

        Assert.True(result.Cancelled);
        Assert.True(
            watch.Elapsed < TimeSpan.FromSeconds(2),
            "檔を読み切るまで待っている：" + watch.Elapsed.TotalSeconds + " 秒");
    }

    /// <summary>
    /// <b>既定の読み手も塊ごとに合図を見る</b>（是正・検分）＝上の試験は継ぎ目を差し替えるので、
    /// 本物の <see cref="NewFileScan.ReadOnce"/> を別に釘付けする。
    /// </summary>
    [Fact]
    public void 既定の読み手は塊ごとに合図を見る()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "big.bin");
        var size = (NewFileScan.BufferBytes * 3) + 7;
        File.WriteAllBytes(path, new byte[size]);

        Assert.Equal(size, NewFileScan.ReadOnce(path));

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var read = NewFileScan.ReadOnce(path, cancel.Token);

        Assert.True(read > 0, "1 塊も読まずに返った");
        Assert.True(read <= NewFileScan.BufferBytes, "合図を見ずに読み切った：" + read);
    }

    /// <summary>
    /// <b>モデルの窖は 2 度読まない</b>（是正・検分）。
    /// <para>
    /// <c>huggingface_hub</c> は繋ぎを作れない機体（開発者モードでない Windows）で
    /// <c>blobs</c> の実体を <c>snapshots</c> へ<b>複製する</b>。両方を数えると同じ 3.5 GB を
    /// 2 度読み、この段が縮めたかった待ちをそのまま倍にする＝<c>blobs</c> は数えない。
    /// </para>
    /// </summary>
    [Fact]
    public void モデルの窖はblobsを数えない()
    {
        var hub = Path.Combine(_root, "hf", "hub", "models--ywk--voice");
        var blobs = Path.Combine(hub, "blobs");
        var snapshot = Path.Combine(hub, "snapshots", "abc123");
        Directory.CreateDirectory(blobs);
        Directory.CreateDirectory(snapshot);
        File.WriteAllText(Path.Combine(blobs, "0f1e2d"), "weights");
        File.WriteAllText(Path.Combine(snapshot, "model.safetensors"), "weights");

        var files = NewFileScan.Enumerate(
            [new NewFileScanRoot(Path.Combine(_root, "hf"), FollowLinks: true, SkipDirectory: "blobs")]);

        var one = Assert.Single(files);
        Assert.EndsWith("model.safetensors", one, StringComparison.Ordinal);
    }

    /// <summary>根が重なっても同じ檔は 1 度しか読まない。</summary>
    [Fact]
    public void 重なった根でも1度しか数えない()
    {
        var tree = MakeTree(files: 6, bytesEach: 4);
        var files = NewFileScan.Enumerate([tree, tree]);
        Assert.Equal(6, files.Count);
    }

    private string MakeTree(int files, int bytesEach)
    {
        var tree = Path.Combine(_root, "runtime", "cpu");
        Directory.CreateDirectory(Path.Combine(tree, "Lib"));
        var blob = new byte[bytesEach];
        for (var i = 0; i < files; i++)
        {
            var dir = i % 2 == 0 ? tree : Path.Combine(tree, "Lib");
            File.WriteAllBytes(Path.Combine(dir, "f" + i + ".bin"), blob);
        }

        return tree;
    }

    /// <summary>掴んだ綱の代わり（<b>投げられた仕事は同じ綱では走らせない</b>＝数えるだけ）。</summary>
    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private int _posts;

        public int Posts => Volatile.Read(ref _posts);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _posts);
            base.Post(d, state);
        }
    }
}

/// <summary>
/// <b>待っている秒</b>（決裁 137 ⒞）＝1 秒ごとに動く数を持つだけの刻み。
/// </summary>
public sealed class Decision137ElapsedTickerTests
{
    [Fact]
    public async Task 刻むたびに1秒ずつ増える()
    {
        var ticker = new ElapsedTicker();
        var releases = new List<TaskCompletionSource>();
        var ticks = 0;
        var gate = new SemaphoreSlim(0);

        ticker.Delay = _ =>
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (releases)
            {
                releases.Add(release);
            }

            gate.Release();
            return release.Task;
        };
        ticker.Ticked += (_, _) => Interlocked.Increment(ref ticks);

        ticker.Start();
        Assert.True(ticker.IsRunning);

        for (var i = 1; i <= 3; i++)
        {
            await gate.WaitAsync(TimeSpan.FromSeconds(5));
            TaskCompletionSource release;
            lock (releases)
            {
                release = releases[^1];
            }

            release.SetResult();
            await WaitFor(() => ticker.Seconds >= i);
            Assert.Equal(i, ticker.Seconds);
        }

        Assert.Equal(3, Volatile.Read(ref ticks));

        ticker.Stop();
        Assert.False(ticker.IsRunning);
        Assert.Equal(0, ticker.Seconds);

        // 止まっている物をもう 1 度止めても何も起きない。
        ticker.Stop();
        Assert.Equal(0, ticker.Seconds);
    }

    [Fact]
    public void 二重に始めない()
    {
        var started = 0;
        var ticker = new ElapsedTicker
        {
            Delay = token =>
            {
                Interlocked.Increment(ref started);
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
        };

        ticker.Start();
        ticker.Start();
        ticker.Start();

        Assert.True(ticker.IsRunning);
        Assert.True(Volatile.Read(ref started) <= 1, "輪が二重に走っている：" + started);
        ticker.Stop();
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }
}

/// <summary>
/// <b>ウィザードの新しい段</b>（決裁 137 ⒜）＝展開とモデルの後・起動の確認の前に入り、
/// 1 本のバーは<b>逆行しない</b>。
/// </summary>
public sealed class Decision137WizardTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-d137w-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 掃除は best effort
        }
    }

    [Fact]
    public void 段の並びは展開とモデルの後で起動の確認の前である()
    {
        Assert.True((int)FirstRunStep.Models < (int)FirstRunStep.Warmup);
        Assert.True((int)FirstRunStep.Warmup < (int)FirstRunStep.Start);
        Assert.Equal(FirstRunStep.Warmup, (FirstRunStep)((int)FirstRunStep.Models + 1));
        Assert.Equal(FirstRunStep.Start, (FirstRunStep)((int)FirstRunStep.Warmup + 1));

        // 見える題は 4 つのまま（働く段は 1 つの「準備しています」に畳む）。
        Assert.Equal(FirstRunViewModel.TitlePreparing, FirstRunViewModel.Title(FirstRunStep.Warmup));
        Assert.Equal("3 / 3", StepNumber(FirstRunStep.Warmup));
    }

    [Fact]
    public void 確認の1行は動く数を出す()
    {
        Assert.Equal(
            "新しいファイルを確認しています（1,234 / 25,300）",
            FirstRunViewModel.WarmupPhaseLine(1234, 25300));

        // 数え上げの最中は**見つかった数だけ**（総数はまだ増えるので分数にしない）。
        Assert.Equal(
            "新しいファイルを数えています（12,345）",
            FirstRunViewModel.WarmupPhaseLine(0, 12345, counting: true));

        // 総数がまだ無い回は段の名乗りのまま（推測の数を出さない）。
        Assert.Equal(
            "新しいファイルを確認しています。",
            FirstRunViewModel.WarmupPhaseLine(0, 0));
        Assert.Equal(
            "新しいファイルを確認しています。",
            FirstRunViewModel.PhaseLine(FirstRunStep.Warmup));

        // 記録の 1 行＝件数と秒（決裁 137 ⒜の求め）。
        Assert.Equal(
            "新しいファイルを確認しました（25,300 件・61.5 秒）。",
            FirstRunViewModel.WarmupDoneLine(25300, TimeSpan.FromSeconds(61.53)));
        Assert.Equal(
            "確認する新しいファイルはありませんでした。",
            FirstRunViewModel.WarmupDoneLine(0, TimeSpan.Zero));
    }

    /// <summary>
    /// <b>1 檔が長い間は秒が動く</b>（決裁 137 ⒜の是正・検分）。
    /// <para>
    /// モデルの <c>model.safetensors</c> は 3 GB 余りで、初回は Defender の走査つきで
    /// 数十秒かかる。その間、檔の数は <c>n / N</c> のまま 1 つも動かない＝
    /// <b>この段が消したかった静止を、この段の尻尾が作る</b>。秒はそこを埋める。
    /// </para>
    /// </summary>
    [Fact]
    public void 確認の1行は秒も添える()
    {
        Assert.Equal(
            "新しいファイルを確認しています（25,299 / 25,300・63 秒）",
            FirstRunViewModel.WarmupPhaseLine(25299, 25300, counting: false, seconds: 63));

        // 1 秒ごとに**必ず違う 1 行**になる（＝檔の数が止まっていても静止しない）。
        Assert.NotEqual(
            FirstRunViewModel.WarmupPhaseLine(25299, 25300, false, 63),
            FirstRunViewModel.WarmupPhaseLine(25299, 25300, false, 64));

        // 数え上げの最中も同じ。
        Assert.Equal(
            "新しいファイルを数えています（12,345・7 秒）",
            FirstRunViewModel.WarmupPhaseLine(0, 12345, counting: true, seconds: 7));

        // 秒を渡さない回は v2.0.1（2）の綴りのまま（＝既存の 1 行は 1 字も動かない）。
        Assert.Equal(
            "新しいファイルを確認しています（1,234 / 25,300）",
            FirstRunViewModel.WarmupPhaseLine(1234, 25300));
    }

    /// <summary>
    /// <b>読み手が 1 檔で止まっていても、画面の 1 行は毎秒変わる</b>（是正・検分）。
    /// <para>
    /// 檔の数だけを進捗にしていたころは、最後の 1 檔（3 GB）の間だけ
    /// <c>PhaseText</c>・割合・バーの 3 つとも凍り、決裁 137 の ⑴ も ⑶ も落ちた。
    /// </para>
    /// </summary>
    [Fact]
    public async Task 檔が1つも進まなくても秒で1行が変わる()
    {
        var tree = Path.Combine(_root, "runtime", RuntimeVariants.Cpu);
        Directory.CreateDirectory(tree);
        File.WriteAllText(Path.Combine(tree, "big.bin"), "x");

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inside = new SemaphoreSlim(0);
        var lines = new List<string>();

        var vm = NewWizard(_ => Task.FromResult(true));
        vm.WarmupReader = (_, _) =>
        {
            // 「まだ読み終わらない 1 檔」＝ここで止まっている間、檔の数は 1 つも動かない。
            inside.Release();
            release.Task.GetAwaiter().GetResult();
            return 1;
        };
        vm.LoadingTicker.Delay = async _ =>
        {
            await tick.Task.ConfigureAwait(false);
            tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FirstRunViewModel.PhaseText)
                && vm.Step is FirstRunStep.Warmup)
            {
                lines.Add(vm.PhaseText);
            }
        };

        vm.Accepted = true;
        await vm.NextAsync();
        var run = vm.NextAsync();

        Assert.True(await inside.WaitAsync(TimeSpan.FromSeconds(10)));
        for (var i = 1; i <= 3; i++)
        {
            await WaitFor(() => vm.LoadingTicker.Seconds >= i - 1 && vm.LoadingTicker.IsRunning);
            tick.TrySetResult();
            await WaitFor(() => lines.Count >= i && vm.LoadingTicker.Seconds >= i);
        }

        release.TrySetResult();
        await run;

        // 1 檔も読み終わっていない間に、3 つの違う 1 行が出ている（＝5 秒の静止が作れない）。
        var distinct = lines.Distinct(StringComparer.Ordinal).ToList();
        Assert.True(distinct.Count >= 3, "止まっている間に 1 行が変わっていない：" + string.Join(" / ", lines));
        Assert.All(
            lines,
            line => Assert.StartsWith("新しいファイルを", line, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("1 秒", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("3 秒", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>モデルの段でもバーと秒が動く</b>（是正・検分）。
    /// <para>
    /// 取得系は 0.5 秒ごとに <c>overall_downloaded</c> を配るのに、窓へ渡る途中で
    /// 1 行の文字列へ畳まれて数が落ちていた＝数 GB を落とす数分の間、
    /// 利用者に見える面（バー・割合・名乗り）は 1 つも動かなかった。
    /// </para>
    /// </summary>
    [Fact]
    public async Task モデルの段はバーと秒が動く()
    {
        var bar = new List<double>();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = NewWizard(_ => Task.FromResult(true));
        vm.ModelFetcher = async (progress, _) =>
        {
            progress.Report(new ModelFetchProgress("0.5 GB / 3.5 GB", 0.25));
            progress.Report(new ModelFetchProgress("1.8 GB / 3.5 GB", 0.5));
            await Task.Delay(50).ConfigureAwait(true);
            reported.TrySetResult();
            return true;
        };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FirstRunViewModel.ProgressFraction)
                && vm.Step is FirstRunStep.Models)
            {
                bar.Add(vm.ProgressFraction);
            }
        };

        vm.Accepted = true;
        await vm.NextAsync();
        await vm.NextAsync();

        await reported.Task;
        Assert.Equal(FirstRunStep.Done, vm.Step);

        // 段の中でバーが 2 度以上動いている（0 のまま突き抜けない）。
        Assert.True(bar.Count >= 2, "モデルの段でバーが動いていない");
        Assert.True(bar[^1] > bar[0], "バーが進んでいない：" + string.Join(", ", bar));

        // 名乗りは秒つき（遅い回線で 1 % に 5 秒以上かかる回の保険）。
        Assert.Equal("声のデータをダウンロードしています（12 秒）", FirstRunViewModel.ModelsPhaseLine(12));
    }

    [Fact]
    public async Task 確認の段を通ってから起動の確認へ進む()
    {
        var tree = Path.Combine(_root, "runtime", RuntimeVariants.Cpu);
        Directory.CreateDirectory(tree);
        for (var i = 0; i < 12; i++)
        {
            File.WriteAllText(Path.Combine(tree, "f" + i + ".bin"), "x");
        }

        var seen = new List<FirstRunStep>();
        var bar = new List<double>();
        var vm = NewWizard(_ => Task.FromResult(true));
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FirstRunViewModel.Step))
            {
                seen.Add(vm.Step);
            }
            else if (e.PropertyName == nameof(FirstRunViewModel.ProgressFraction))
            {
                bar.Add(vm.ProgressFraction);
            }
        };

        vm.Accepted = true;
        await vm.NextAsync();                                  // お知らせ → これからすること
        await vm.NextAsync();                                  // 取得→展開→モデル→確認→起動→完了

        Assert.Equal(FirstRunStep.Done, vm.Step);
        Assert.Equal(
            [
                FirstRunStep.Variant, FirstRunStep.Download, FirstRunStep.Install,
                FirstRunStep.Models, FirstRunStep.Warmup, FirstRunStep.Start, FirstRunStep.Done,
            ],
            seen);

        // **バーは 1 度も戻らない**（段が増えても＝`v2-plan.md` 段 B-3 の錠）。
        for (var i = 1; i < bar.Count; i++)
        {
            Assert.True(bar[i] >= bar[i - 1], "バーが戻った：" + bar[i - 1] + " → " + bar[i]);
        }

        // 記録は件数と秒を 1 行で残す（画面には出さない＝詳細の中）。
        var line = Assert.Single(
            vm.Trail,
            static t => t.StartsWith("新しいファイルを確認しました（", StringComparison.Ordinal));
        Assert.Contains(" 件・", line, StringComparison.Ordinal);
        Assert.Contains(" 秒）。", line, StringComparison.Ordinal);
        Assert.Contains("― 新しいファイルの確認", vm.Trail);
    }

    /// <summary>
    /// <b>待っている秒はウィザードの 1 行でも動く</b>（決裁 137 ⒝⒞）。
    /// </summary>
    [Fact]
    public async Task 起動の確認では秒が1秒ごとに動く()
    {
        FirstRunViewModel? wizard = null;
        var lines = new List<string>();
        var seconds = new List<int>();
        var beforeListening = string.Empty;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ticked = new SemaphoreSlim(0);

        var vm = NewWizard(async _ =>
        {
            // **口が開く前**＝起こしている最中の 1 行（秒は段の頭から刻んである）。
            beforeListening = wizard!.PhaseText;

            // 主窓が Listening を見た瞬間（＝口は開いた・まだ載っていない）。
            wizard!.ReportLoadingVoices();
            lines.Add(wizard!.PhaseText);
            seconds.Add(wizard!.LoadingTicker.Seconds);

            for (var i = 0; i < 3; i++)
            {
                await ticked.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                lines.Add(wizard!.PhaseText);
                seconds.Add(wizard!.LoadingTicker.Seconds);
            }

            return true;
        });
        wizard = vm;
        vm.LoadingTicker.Delay = async _ =>
        {
            await release.Task.ConfigureAwait(false);
            release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        };
        vm.LoadingTicker.Ticked += (_, _) => ticked.Release();

        vm.Accepted = true;
        await vm.NextAsync();
        var run = vm.NextAsync();
        for (var i = 0; i < 3; i++)
        {
            await WaitFor(() => vm.LoadingTicker.IsRunning);
            release.TrySetResult();
            await Task.Delay(20);
        }

        await run;

        Assert.Equal(FirstRunStep.Done, vm.Step);

        // **口が開く前から秒は動いている**（是正・検分）＝ここが constant だったころは、
        // 起こしてから口が開くまでの実測 12 秒以上が丸ごと静止していた。
        Assert.StartsWith("起動しています（", beforeListening, StringComparison.Ordinal);

        // 口が開いた後は文言だけが替わる＝**秒は 0 へ戻らない**（刻みを止めて始め直さない）。
        Assert.Equal(
            "声を読み込んでいます（0 秒）… 初めてのときは、パソコンの安全機能が新しいファイルを確認するので数分かかります。",
            FirstRunViewModel.LoadingVoicesLine(0));
        for (var i = 0; i < lines.Count; i++)
        {
            Assert.Equal(FirstRunViewModel.LoadingVoicesLine(seconds[i]), lines[i]);
        }

        // 刻むたびに<b>数が増えて 1 行が変わる</b>（＝5 秒を越える静止が作れない）。
        // **「ちょうど 1 つ」とは言わない**＝標本を採る側（この試験）が遅れると刻みが先へ行く。
        // 釘付けしたいのは「必ず進む」ことであって、試験の綱の速さではない。
        for (var i = 1; i < lines.Count; i++)
        {
            Assert.True(seconds[i] > seconds[i - 1], "秒が進んでいない：" + string.Join(", ", seconds));
            Assert.NotEqual(lines[i - 1], lines[i]);
        }

        Assert.True(lines.Count >= 4, "標本が足りない：" + lines.Count);

        // 待ちが終わったら刻みは止まる（止まった画面に古い秒が居座らない）。
        Assert.False(vm.LoadingTicker.IsRunning);
    }

    /// <summary>
    /// <b>働く段が UI の綱で檔を読まない</b>（決裁 137 ⒞）＝原文で釘付けする
    /// （<c>MainViewModel</c> の配線を釘付けした決裁 135 の試験と同じ作法）。
    /// </summary>
    [Fact]
    public void 働く段の檔読みは綱の外へ出してある()
    {
        var source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "ViewModelsSource", "FirstRunViewModel.cs"),
            Encoding.UTF8);

        // 取得の段＝揃っているかの判定・台帳・空き容量の 3 つとも `Task.Run` の中。
        Assert.Equal(2, Occurrences(source, "await Task.Run(RuntimeLooksSound).ConfigureAwait(true)"));
        Assert.Contains("var value = TryPlan(_paths, _variant, _skipVcRedist, out var why);", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(() => FreeBytes(_paths.DataDir)).ConfigureAwait(true)", source, StringComparison.Ordinal);

        // 展開の段＝台帳読み。
        Assert.Contains("await Task\n            .Run(() => ReadLedgerFile<LedgerFile>(", source.Replace("\r\n", "\n"), StringComparison.Ordinal);

        // 綱を止める手（同期待ち）はどの段にも無い。
        Assert.DoesNotContain(".Result", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Thread.Sleep", source, StringComparison.Ordinal);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    private static string StepNumber(FirstRunStep step) =>
        FirstRunViewModel.VisibleStepNumber(step, noticesSkipped: false) + " / 3";

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    private FirstRunViewModel NewWizard(Func<CancellationToken, Task<bool>> startServer)
    {
        var appDir = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(appDir, "ledger"));
        Directory.CreateDirectory(Path.Combine(appDir, "licenses"));
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        var paths = new AppPaths(
            Path.Combine(_root, "install"),
            appDir,
            Path.Combine(_root, "runtime"),
            Path.Combine(_root, "data"),
            developerMode: true);

        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文", new UTF8Encoding(false));
        File.WriteAllText(
            paths.LedgerPath("python-embed"),
            """
            {"schema":1,"name":"python-embed","items":[
              {"kind":"python-embed","name":"python-embed","url":"https://example.invalid/p.zip",
               "sha256":"aa","size":100}]}
            """,
            new UTF8Encoding(false));
        File.WriteAllText(
            paths.LedgerPath(RuntimeVariants.LedgerName(RuntimeVariants.Cpu)),
            """
            {"schema":1,"name":"runtime","items":[
              {"kind":"wheel","name":"torch","url":"https://example.invalid/t.whl",
               "sha256":"bb","size":1000}]}
            """,
            new UTF8Encoding(false));

        var vm = new FirstRunViewModel(
            paths,
            new LauncherSettings { Variant = RuntimeVariants.Cpu },
            new JsonSettingsStore(paths.SettingsPath),
            new DriverRequirement(),
            static () => new NoopDownloader(),
            static () => new NoopInstaller(),
            startServer)
        {
            ModelFetcher = static (_, _) => Task.FromResult(true),
        };

        return vm;
    }

    /// <summary>1 檔も落とさずに「落とせた」と答える偽の取得系（外へ 1 バイトも出さない）。</summary>
    private sealed class NoopDownloader : IDownloader
    {
        public Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(
            IReadOnlyList<DownloadRequest> requests,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<DownloadResult> results =
            [.. requests.Select(static r => new DownloadResult(
                true, r.DestinationPath, r.ExpectedSize ?? 0, r.Sha256, r.Url, false, true, 1, null))];
            return Task.FromResult(results);
        }

        public Task<DownloadResult> DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DownloadResult(
                true, request.DestinationPath, request.ExpectedSize ?? 0, request.Sha256,
                request.Url, false, true, 1, null));

        public void Dispose()
        {
        }
    }

    /// <summary>展開したことにする偽の展開系（1 檔も置かない）。</summary>
    private sealed class NoopInstaller : IRuntimeInstaller
    {
        public Task<InstallResult> InstallAsync(
            InstallRequest request,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new InstallResult(true, 12, 1_000, 3, [], null, null));
    }
}

/// <summary>
/// <b>状態帯の秒</b>（決裁 137 ⒞）＝主窓の帯でも 1 秒ごとに数が動く。
/// </summary>
public sealed class Decision137BandTests
{
    [Fact]
    public async Task 準備を待っている間だけ秒が動く()
    {
        var status = new StatusViewModel(static () => Task.CompletedTask, static () => Task.CompletedTask);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        status.PreparingTicker.Delay = async _ =>
        {
            await release.Task.ConfigureAwait(false);
            release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        };

        status.ApplyState(ServerState.Listening, null);
        Assert.True(status.PreparingTicker.IsRunning);
        Assert.Equal("準備しています…（0 秒）", status.BandStateText);
        Assert.Equal(BandText.PreparingVoicesWhy, status.BandReasonText);

        release.TrySetResult();
        await WaitFor(() => status.BandStateText == "準備しています…（1 秒）");
        Assert.Equal("準備しています…（1 秒）", status.BandStateText);

        // 使えるようになったら刻みは止まり、秒は消える。
        status.ApplyState(ServerState.Ready, null);
        Assert.False(status.PreparingTicker.IsRunning);
        Assert.Equal(BandText.Ready, status.BandStateText);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }
}
