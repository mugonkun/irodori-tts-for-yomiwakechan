using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Gpu;

/// <summary>
/// 変種の <c>python.exe</c> で <see cref="TorchGpuProbe"/> の台本を 1 回撃つ口。
/// <para>
/// <b>2 人が同じ台本を要る</b>＝⑴ GPU の列挙（<see cref="GpuEnumerator"/>・受け入れ条件 D-2）
/// ⑵ <b>変種の門</b>（<see cref="VariantGate"/>・裁定 88 ⑴＝起こす前に <c>is_available()</c> と
/// <c>device_count()</c> を見る）。台本を書き出す手を 2 度書かないための 1 本。
/// </para>
/// <para>テストは偽物を差す（<b>実機・実 GPU に触れない</b>）。</para>
/// </summary>
public interface ITorchProbe
{
    /// <summary>撃つ。<b>例外を投げない</b>＝読めなかった理由は <c>Error</c> に入る。</summary>
    Task<TorchGpuProbe.TorchProbeResult> ProbeAsync(
        string pythonExe,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>
/// 実機用の <see cref="ITorchProbe"/>。台本を <c>%TEMP%</c> に<b>1 回ごとの名前</b>で書いて渡し、
/// 読んだら消す。
/// <para>
/// <b>名前を毎回変える</b>のは、列挙（窓の「数え直す」）と門（起動）が同時に走りうるからである。
/// 固定名だと片方が書いている最中の檔をもう片方が渡す。
/// </para>
/// </summary>
public sealed class TorchProbeRunner : ITorchProbe
{
    private readonly IProcessRunner _runner;
    private readonly string _scriptDirectory;

    /// <summary>実機用。</summary>
    public TorchProbeRunner()
        : this(new ProcessRunner(), null)
    {
    }

    /// <summary>テストの継ぎ目＝<b>public コンストラクタ</b>。</summary>
    /// <param name="runner">外の実行檔を起こす口。</param>
    /// <param name="scriptDirectory">台本を書き出す場所（既定＝<c>%TEMP%</c>）。</param>
    public TorchProbeRunner(IProcessRunner runner, string? scriptDirectory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _scriptDirectory = scriptDirectory ?? Path.GetTempPath();
    }

    /// <summary>
    /// 台本に載せる env（<b>列挙と起動で index の並べ方を揃える</b>＝
    /// <see cref="ServerEnvironment.CudaDeviceOrder"/>・取得のために外へ出ない）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> ProbeEnvironment { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PYTHONUTF8"] = "1",
            ["PYTHONDONTWRITEBYTECODE"] = "1",
            ["PYTHONUNBUFFERED"] = "1",
            ["PYTHONIOENCODING"] = "utf-8",

            // 取得は台帳の路 1 本（裁定 8）＝検分のために外へ出ない
            ["HF_HUB_OFFLINE"] = "1",

            // 数えるときと起こすときで index の並べ方を揃える（是正・2026-09-05）
            [ServerEnvironment.CudaDeviceOrder] = ServerEnvironment.PciBusIdOrder,
        };

    public async Task<TorchGpuProbe.TorchProbeResult> ProbeAsync(
        string pythonExe,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pythonExe);

        string script;
        try
        {
            script = WriteScript();
        }
        catch (IOException ex)
        {
            return Failed("列挙の台本を書けませんでした：" + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failed("列挙の台本を書けませんでした：" + ex.Message);
        }

        try
        {
            var run = await _runner
                .RunAsync(pythonExe, [script], ProbeEnvironment, timeout, cancellationToken)
                .ConfigureAwait(false);

            if (!run.Started)
            {
                return Failed("実行系を起こせませんでした：" + (run.FailureReason ?? string.Empty));
            }

            if (run.TimedOut)
            {
                return Failed("GPU の列挙が期限内に終わりませんでした。");
            }

            return TorchGpuProbe.Parse(run.StandardOutput);
        }
        finally
        {
            try
            {
                File.Delete(script);
            }
            catch (IOException)
            {
                // 消し損ねは無害（%TEMP% の 2 KB）
            }
            catch (UnauthorizedAccessException)
            {
                // 同上
            }
        }
    }

    private static TorchGpuProbe.TorchProbeResult Failed(string reason) =>
        new([], null, null, null, reason);

    private string WriteScript()
    {
        Directory.CreateDirectory(_scriptDirectory);

        // ywk_gpu_probe-<8 桁>.py＝同時に走る 2 人が同じ檔を掴まない
        var name = Path.GetFileNameWithoutExtension(TorchGpuProbe.ScriptFileName)
                   + "-" + Guid.NewGuid().ToString("N")[..8]
                   + Path.GetExtension(TorchGpuProbe.ScriptFileName);
        var path = Path.Combine(_scriptDirectory, name);
        File.WriteAllText(path, TorchGpuProbe.Script, new UTF8Encoding(false));
        return path;
    }
}
