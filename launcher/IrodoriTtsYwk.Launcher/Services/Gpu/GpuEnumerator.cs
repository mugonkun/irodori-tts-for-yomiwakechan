using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Gpu;

/// <summary>外の実行檔を 1 回起こして標準出力を読む口（テストは偽物を差す）。</summary>
public interface IProcessRunner
{
    /// <summary>起こして待つ。<b>例外を投げない</b>＝結末で返す。</summary>
    Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <param name="Started">起こせたか（実行檔が無ければ偽）。</param>
/// <param name="ExitCode">終了コード（起こせなければ null）。</param>
/// <param name="StandardOutput">標準出力。</param>
/// <param name="StandardError">標準エラー。</param>
/// <param name="TimedOut">期限切れで殺したか。</param>
/// <param name="FailureReason">理由 1 行。</param>
public sealed record ProcessRunResult(
    bool Started,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    string? FailureReason);

/// <summary>
/// 実機用の <see cref="IProcessRunner"/>（窓を出さず、期限で殺す）。
/// <para>
/// <b>期限は 2 本ある</b>（是正・便 D（3）＝設計書 §20-5 ⑴）＝⑴ <see cref="StartTimeout"/>＝
/// <b>「起こす」段そのもの</b>の期限 ⑵ 引数の <c>timeout</c>＝起きた子が終わるまでの期限。
/// 1 巡目・2 巡目は ⑵ しか無く、<c>Process.Start</c> が返ってこない機体
/// （壊れた <c>python.exe</c> を指すと Windows が「このアプリは PC で実行できません」の窓を出す＝
/// WPF のメッセージポンプの上でだけ起きる）で <b>60 秒だれも何も出さなかった</b>。
/// コンソールから同じ材料を撃つと 18 ms で <c>Win32Exception</c> が返るので、
/// 待つ側に期限が要る。<see cref="CancellationToken"/> は同期呼び出しの <c>Start</c> を
/// 中断できない＝<b>別のスレッドに逃がしてから待つ</b>。
/// </para>
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>
    /// 「起こす」段の既定の期限（設計書 §20-5 ⑴ は「例 10 s」）。
    /// <para>
    /// <b>3 s に詰めてある</b>＝1 度の「サーバ起動」で<b>同じ python.exe を 3 回起こす</b>
    /// （⑴ 窓の GPU 列挙 ⑵ 変種の門の検分 ⑶ サーバの子）ので、無人検分の
    /// 「押してから理由が出るまで 10 s」の budget に 3 つとも収まる値が要る（3 × 3 s = 9 s）。
    /// <c>CreateProcess</c> そのものは健全な機体で ms の仕事なので、3 s でも 100 倍以上の余裕がある。
    /// </para>
    /// </summary>
    public static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromSeconds(3);

    /// <summary>「起こす」段そのものの期限（テストは短くする）。</summary>
    public TimeSpan StartTimeout { get; init; } = DefaultStartTimeout;

    /// <summary>
    /// <c>SEM_FAILCRITICALERRORS</c>（0x0001）｜<c>SEM_NOOPENFILEERRORBOX</c>（0x8000）。
    /// <see cref="StartAsync"/> の逐語の説明を読むこと。
    /// </summary>
    private const uint SuppressHardErrorBox = 0x8001;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadErrorMode(uint dwNewMode, out uint lpOldMode);

    /// <summary>
    /// <c>Process.Start</c> を別スレッドで撃つ＝<b>Windows のハードエラーの窓を出させない</b>
    /// （是正・便 D（3）の統合席＝無人検分の段 k が実窓で捕まえた）。
    /// <para>
    /// <b>実測（この機体・2026-09-05）</b>＝<c>python.exe</c> の名を付けたテキスト檔に
    /// <c>UseShellExecute=false</c> で <c>Process.Start</c> を撃つと、**プロセスのエラーモードが
    /// 0 の個体では Windows が「サポートされていない 16 ビット アプリケーション」の
    /// ハードエラー窓を出し、`Start` はその窓が押されるまで返らない**
    /// （＝待っても <c>CancellationToken</c> でも切れない・40 秒で打ち切っても返らなかった）。
    /// 同じ材料をエラーモード <c>0x8003</c> の個体（PowerShell など）から撃つと
    /// **12〜19 ms で <c>Win32Exception</c>** が返る。設計書 §20-5 ⑴ の
    /// 「窓が 60 秒黙る／コンソールは 18 ms で返る」の差はこれである
    /// （<see cref="StartTimeout"/> は返らない <c>Start</c> の保険として残す＝両方要る）。
    /// </para>
    /// <para>
    /// <b>スレッド単位で掛ける</b>（<c>SetErrorMode</c> ではなく <c>SetThreadErrorMode</c>）＝
    /// プロセス全体のエラーモードは<b>起こした子が引き継ぐ</b>ので、窓を出さない約束を
    /// wrapper の python にまで広げない。撃ち終えたら必ず元へ戻す
    /// （<c>Task.Run</c> のスレッドはプールの使い回しである）。
    /// </para>
    /// </summary>
    public static Task<bool> StartAsync(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return Task.Run(() => StartWithoutHardErrorBox(process), CancellationToken.None);
    }

    /// <summary>
    /// <see cref="StartAsync"/> の中身（同じスレッドで撃つ形＝子を待たない呼び手のため）。
    /// <b>例外はそのまま通す</b>（<c>Win32Exception</c> を握り潰さない）。
    /// </summary>
    public static bool StartWithoutHardErrorBox(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        var restore = false;
        uint previous = 0;
        try
        {
            restore = SetThreadErrorMode(SuppressHardErrorBox, out previous);
        }
        catch (EntryPointNotFoundException)
        {
            // 呼べない OS＝窓が出る形に戻るだけで、結末は変わらない（期限が受ける）。
        }
        catch (DllNotFoundException)
        {
            // 同上
        }

        try
        {
            return process.Start();
        }
        finally
        {
            if (restore)
            {
                try
                {
                    _ = SetThreadErrorMode(previous, out _);
                }
                catch (EntryPointNotFoundException)
                {
                }
                catch (DllNotFoundException)
                {
                }
            }
        }
    }

    /// <summary>起こす段が期限で返らなかったときの 1 行（<b>純関数</b>）。</summary>
    public static string StartStalledMessage(string fileName, TimeSpan startTimeout) =>
        "実行系を起こす段が " + startTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)
        + " 秒で返りませんでした（" + System.IO.Path.GetFileName(fileName)
        + "＝この機体では起こせない実行檔かもしれません）。";

    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var info = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                info.Environment[pair.Key] = pair.Value;
            }
        }

        var process = new Process { StartInfo = info };

        // ---- ⑴「起こす」段（別スレッドへ逃がして期限つきで待つ） ----------------
        var start = StartAsync(process);
        var startDeadline = Task.Delay(StartTimeout, CancellationToken.None);
        if (await Task.WhenAny(start, startDeadline).ConfigureAwait(false) != start)
        {
            // **返ってこない Start を待ち続けない**。個体は手放し、後から起きたら殺す係を付ける。
            Abandon(process, start);
            return new ProcessRunResult(
                false, null, string.Empty, string.Empty, true, StartStalledMessage(fileName, StartTimeout));
        }

        using var owned = process;
        try
        {
            if (!await start.ConfigureAwait(false))
            {
                return new ProcessRunResult(false, null, string.Empty, string.Empty, false, "起こせませんでした。");
            }
        }
        catch (Exception ex)
        {
            // Win32Exception（実行檔が無い・この OS で動かない）・InvalidOperationException など。
            // **契約は「例外を投げない」**なので、型で数え上げずに結末へ落とす。
            return new ProcessRunResult(false, null, string.Empty, string.Empty, false, ex.Message);
        }

        // ---- ⑵ 起きた子が終わるまでの期限 ---------------------------------------
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // もう死んでいる
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // **落とせなかった**（是正・便 D（2））＝`Kill` は権限・ハンドルの都合で
                // Win32Exception も投げる。ここで漏らすと `TorchProbeRunner` は catch を持たず、
                // `ServerProcess.StartAsync` が例外で抜ける＝契約（例外は投げず結末で返す）に反した。
                // 期限切れの結末は変わらない＝台本の子は %TEMP% の 2 KB を読むだけで自然に終わる。
            }
            catch (AggregateException)
            {
                // ツリーの子の 1 つが落ちなかっただけ（同上）
            }

            try
            {
                // 読みの task を回収する（拾わないと未観測の例外が残る）
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // 途中で閉じた口＝期限切れの結末は変わらない
            }
            catch (OperationCanceledException)
            {
                // 同上
            }

            return new ProcessRunResult(true, null, string.Empty, string.Empty, true, "期限切れで打ち切りました。");
        }

        var outText = await stdout.ConfigureAwait(false);
        var errText = await stderr.ConfigureAwait(false);
        return new ProcessRunResult(true, process.ExitCode, outText, errText, false, null);
    }

    /// <summary>
    /// 期限で見限った <see cref="Process"/> を手放す＝<b>後から起きたら殺してから捨てる</b>
    /// （見限ったまま放っておくと、返ってきた <c>Start</c> が起こした子が居残る）。
    /// <c>ServerProcess</c> の子の spawn も同じ始末をするので <c>public</c>。
    /// </summary>
    public static void Abandon(Process process, Task<bool> start) =>
        _ = start.ContinueWith(
            static (task, state) =>
            {
                var abandoned = (Process)state!;
                try
                {
                    _ = task.Exception; // 観測しないと未観測の例外として残る
                    if (task.Status == TaskStatus.RanToCompletion && task.Result)
                    {
                        abandoned.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception)
                {
                    // 既に死んでいる・落とせない＝どちらでも見限った結末は変わらない
                }
                finally
                {
                    abandoned.Dispose();
                }
            },
            process,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}

/// <summary>
/// 契約 ⑹ の実装＝GPU 列挙（設計書 §3）。
/// <para>
/// <b>① <c>nvidia-smi</c>（0.04 s・NVIDIA 専用）→ ② 変種の python で torch 列挙 1 回</b>の順。
/// ① で 1 台でも読めたらそこで止める（torch を起こすと 1.2〜4.4 s かかる＝受け入れ条件 D-2 の 5 s に
/// 効く）。② は AMD 機と、NVIDIA だがドライバのツールが無い機体のための路。
/// </para>
/// <para>
/// <b>保存は UUID</b>（裁定 34）＝ここは列挙するだけで、index への解決は
/// <see cref="GpuResolver.ResolveIndex"/>（純関数）が起動のたびに行う。
/// </para>
/// <para>
/// <b>テストの継ぎ目は public コンストラクタ</b>＝<see cref="IProcessRunner"/> を差せば
/// 実機・実 GPU なしで路の選び方まで釘付けできる。
/// </para>
/// </summary>
public sealed class GpuEnumerator : IGpuEnumerator
{
    /// <summary>Windows の既定の在り処（PATH に無い機体のための保険）。</summary>
    public static readonly string[] NvidiaSmiCandidates =
    [
        "nvidia-smi",
        @"C:\Windows\System32\nvidia-smi.exe",
        @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
    ];

    private readonly IProcessRunner _runner;
    private readonly Func<string, bool> _fileExists;
    private readonly ITorchProbe _torchProbe;

    /// <summary>
    /// OS のアダプタ一覧（<b>実行系が無くても答える</b>＝是正・段 G・medium 10／12）。
    /// 差し替えられるのは試験のためで、実機は <see cref="DxgiGpuAdapters"/> である。
    /// </summary>
    private readonly IGpuAdapterInfoSource _adapters;

    /// <summary>実機用。</summary>
    public GpuEnumerator()
        : this(new ProcessRunner(), null, null)
    {
    }

    /// <summary>テスト用（<b>public コンストラクタが継ぎ目</b>）。</summary>
    /// <param name="runner">外の実行檔を起こす口。</param>
    /// <param name="fileExists">実行檔が在るかの判定（既定＝実際の檔検査）。</param>
    /// <param name="scriptDirectory">torch の台本を書き出す場所（既定＝<c>%TEMP%</c>）。</param>
    public GpuEnumerator(IProcessRunner runner, Func<string, bool>? fileExists, string? scriptDirectory)
        : this(runner, fileExists, scriptDirectory, null)
    {
    }

    /// <summary>試験用（アダプタ一覧まで差せる継ぎ目）。</summary>
    /// <param name="runner">外の実行檔を起こす口。</param>
    /// <param name="fileExists">実行檔が在るかの判定（既定＝実際の檔検査）。</param>
    /// <param name="scriptDirectory">torch の台本を書き出す場所（既定＝<c>%TEMP%</c>）。</param>
    /// <param name="adapters">
    /// OS のアダプタ一覧（既定＝<see cref="DxgiGpuAdapters"/>）。<b>0 台の理由を分けるためだけ</b>に
    /// 読む＝ここから <see cref="GpuInfo"/> は作らない（DXGI は VRAM の総量しか持たず、
    /// UUID も compute の可否も判らない＝保存に使える値ではない）。
    /// </param>
    public GpuEnumerator(
        IProcessRunner runner,
        Func<string, bool>? fileExists,
        string? scriptDirectory,
        IGpuAdapterInfoSource? adapters)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _fileExists = fileExists ?? (path => path == "nvidia-smi" || File.Exists(path));
        _adapters = adapters ?? new DxgiGpuAdapters();

        // 台本を書いて撃つ手は 1 本（変種の門＝VariantGate も同じ物を使う）。
        _torchProbe = new TorchProbeRunner(runner, scriptDirectory);
    }

    public async Task<GpuEnumerationResult> EnumerateAsync(
        GpuEnumerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = Stopwatch.GetTimestamp();

        // ① nvidia-smi（速い・NVIDIA だけ）
        var smi = await TryNvidiaSmiAsync(request.Timeout, cancellationToken).ConfigureAwait(false);
        if (smi.Count > 0)
        {
            return new GpuEnumerationResult(smi, GpuSource.NvidiaSmi, Stopwatch.GetElapsedTime(started), null);
        }

        // ② 変種の python で torch 列挙 1 回（AMD も読める）
        if (string.IsNullOrWhiteSpace(request.PythonExe))
        {
            return Empty(
                started,
                "グラフィックスを調べられませんでした（動かすための一式がまだ入っていません）。");
        }

        var probe = await TryTorchAsync(request.PythonExe, request.Timeout, cancellationToken)
            .ConfigureAwait(false);
        if (probe.Gpus.Count > 0)
        {
            return new GpuEnumerationResult(
                probe.Gpus, GpuSource.TorchProbe, Stopwatch.GetElapsedTime(started), null);
        }

        return Empty(started, probe.Error ?? "GPU が 1 台も見つかりませんでした。");
    }

    /// <summary>
    /// <b>0 台で返るときに、その 0 台の意味を OS のアダプタ一覧で言い分ける</b>
    /// （是正・段 G・medium 10／12）。
    /// <list type="bullet">
    /// <item>NVIDIA も AMD も<b>1 枚も居ない</b>＝<b>見た上で 0 台</b>なので理由を落とす（null）。
    /// これで <see cref="VariantRecommendation.Recommend"/> の CPU の枝が実機でも通り、
    /// はじめの準備が憲章 §7 の 1 行（<c>NoGpuDecisionLine</c>）を出してから始まる。</item>
    /// <item>どちらかが居る＝<b>道具が無いだけ</b>なので理由は残す（＝勝手に CPU へ倒さない）。
    /// 会社の 2 欄は、はじめの準備が「版を間違えて入れた」を言うために持ち帰る。</item>
    /// <item>一覧そのものが空（<c>dxgi.dll</c> が無い・COM が落ちた）＝<b>何も判らない</b>ので
    /// 今までどおり理由を残し、2 欄は null（＝見ていない）にする。</item>
    /// </list>
    /// </summary>
    private GpuEnumerationResult Empty(long started, string reason)
    {
        var adapters = _adapters.Adapters();
        if (adapters.Count == 0)
        {
            return new GpuEnumerationResult(
                [], GpuSource.None, Stopwatch.GetElapsedTime(started), reason);
        }

        var nvidia = GpuVendors.AnyNvidia(adapters);
        var amd = GpuVendors.AnyAmd(adapters);
        return new GpuEnumerationResult(
            [],
            GpuSource.None,
            Stopwatch.GetElapsedTime(started),
            nvidia || amd ? reason : null,
            nvidia,
            amd);
    }

    /// <summary>
    /// ドライバ版（<c>nvidia-smi</c> 経路でだけ読める）。AMD 機では null＝
    /// <see cref="DriverRequirement.Check"/> が「読めなかった」と名乗る（止めはしない）。
    /// </summary>
    public static string? DriverVersionOf(IReadOnlyList<GpuInfo> gpus) => NvidiaSmiParser.DriverVersion(gpus);

    private async Task<IReadOnlyList<GpuInfo>> TryNvidiaSmiAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in NvidiaSmiCandidates)
        {
            if (!_fileExists(candidate))
            {
                continue;
            }

            var query = await _runner.RunAsync(
                candidate,
                ["--query-gpu=" + NvidiaSmiParser.QueryFields, "--format=csv"],
                null,
                timeout,
                cancellationToken).ConfigureAwait(false);

            if (!query.Started)
            {
                continue;
            }

            var list = await _runner.RunAsync(candidate, ["-L"], null, timeout, cancellationToken)
                .ConfigureAwait(false);

            var merged = NvidiaSmiParser.Merge(
                NvidiaSmiParser.ParseList(list.StandardOutput),
                NvidiaSmiParser.ParseQuery(query.StandardOutput));

            if (merged.Count > 0)
            {
                return merged;
            }
        }

        return [];
    }

    private Task<TorchGpuProbe.TorchProbeResult> TryTorchAsync(
        string pythonExe,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        _torchProbe.ProbeAsync(pythonExe, timeout, cancellationToken);
}
