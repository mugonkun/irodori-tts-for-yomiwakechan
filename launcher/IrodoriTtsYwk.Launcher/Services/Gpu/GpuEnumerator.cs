using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

/// <summary>実機用の <see cref="IProcessRunner"/>（窓を出さず、期限で殺す）。</summary>
public sealed class ProcessRunner : IProcessRunner
{
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

        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start())
            {
                return new ProcessRunResult(false, null, string.Empty, string.Empty, false, "起こせませんでした。");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new ProcessRunResult(false, null, string.Empty, string.Empty, false, ex.Message);
        }

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
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _fileExists = fileExists ?? (path => path == "nvidia-smi" || File.Exists(path));

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
            return new GpuEnumerationResult(
                [], GpuSource.None, Stopwatch.GetElapsedTime(started),
                "GPU を列挙できませんでした（nvidia-smi が無く、実行系もまだ取得できていません）。");
        }

        var probe = await TryTorchAsync(request.PythonExe, request.Timeout, cancellationToken)
            .ConfigureAwait(false);
        if (probe.Gpus.Count > 0)
        {
            return new GpuEnumerationResult(
                probe.Gpus, GpuSource.TorchProbe, Stopwatch.GetElapsedTime(started), null);
        }

        return new GpuEnumerationResult(
            [], GpuSource.None, Stopwatch.GetElapsedTime(started),
            probe.Error ?? "GPU が 1 台も見つかりませんでした。");
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
