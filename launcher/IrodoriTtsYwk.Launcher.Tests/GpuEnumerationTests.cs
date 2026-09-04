using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// GPU 列挙とドライバ検査（設計書 §3・受け入れ条件 D-2）。
/// <b>固定入力は研究の逐語</b>＝<c>research/lab/out/3090-results-ssd-ref/gpu_props.json</c>
/// （3090 機・<c>nvidia-smi</c> 0.04 s）と <c>research/lab/notes/29-gpu-designation-lab.md</c> :317
/// （この機体・Radeon gfx1151）。実機・実 GPU には触れない。
/// </summary>
public sealed class GpuEnumerationTests
{
    /// <summary>3090 機の <c>nvidia-smi -L</c> の逐語。</summary>
    private const string SmiList =
        "GPU 0: NVIDIA GeForce RTX 3090 (UUID: GPU-19adfe89-c9e0-df55-4a8a-e31798717a36)";

    /// <summary>3090 機の <c>nvidia-smi --query-gpu=… --format=csv</c> の逐語。</summary>
    private const string SmiQuery =
        "index, name, uuid, pci.bus_id, driver_version\n"
        + "0, NVIDIA GeForce RTX 3090, GPU-19adfe89-c9e0-df55-4a8a-e31798717a36, 00000000:01:00.0, 591.86";

    /// <summary>VRAM つきの形（<c>memory.total</c> を足して問い合わせたとき）。</summary>
    private const string SmiQueryWithMemory =
        "index, name, uuid, pci.bus_id, memory.total [MiB], driver_version\n"
        + "0, NVIDIA GeForce RTX 3090, GPU-19adfe89-c9e0-df55-4a8a-e31798717a36, 00000000:01:00.0, 24576 MiB, 591.86\n"
        + "1, NVIDIA GeForce RTX 3090, GPU-2b2b2b2b-1111-2222-3333-444444444444, 00000000:02:00.0, 24576 MiB, 591.86";

    /// <summary>この機体（Radeon gfx1151）の torch 列挙（<c>29</c> §5 の実値から組んだ 1 行）。</summary>
    private const string TorchOutput =
        "UserWarning: torch is a ROCm build\n"
        + "YWK_GPU_JSON {\"ok\": true, \"torch\": \"2.13.0+rocm10.0.0\", \"cuda\": null, "
        + "\"hip\": \"7.15.26333\", \"count\": 1, \"devices\": [{\"index\": 0, "
        + "\"name\": \"AMD Radeon(TM) 8060S Graphics\", "
        + "\"uuid\": \"30303030-3031-3937-3030-303030303030\", \"total_memory\": 107090132992, "
        + "\"pci_bus_id\": \"197\", \"gcn_arch\": \"gfx1151\"}], \"error\": null}\n";

    [Fact]
    public void nvidiaSmiのLからUUIDと名前を読む()
    {
        var gpus = NvidiaSmiParser.ParseList(SmiList);

        var gpu = Assert.Single(gpus);
        Assert.Equal(0, gpu.Index);
        Assert.Equal("NVIDIA GeForce RTX 3090", gpu.Name);
        Assert.Equal("GPU-19adfe89-c9e0-df55-4a8a-e31798717a36", gpu.Uuid);
        Assert.Equal(GpuSource.NvidiaSmi, gpu.Source);
    }

    [Fact]
    public void queryの見出しで欄を対応づける()
    {
        // 逐語には memory.total が無い＝VRAM は 0（欄が欠けても読める）
        var gpus = NvidiaSmiParser.ParseQuery(SmiQuery);

        var gpu = Assert.Single(gpus);
        Assert.Equal("GPU-19adfe89-c9e0-df55-4a8a-e31798717a36", gpu.Uuid);
        Assert.Equal("00000000:01:00.0", gpu.PciBusId);
        Assert.Equal("591.86", gpu.DriverVersion);
        Assert.Equal(0, gpu.TotalMemoryBytes);
    }

    [Fact]
    public void 単位つきのVRAMも複数台も読む()
    {
        var gpus = NvidiaSmiParser.ParseQuery(SmiQueryWithMemory);

        Assert.Equal(2, gpus.Count);
        Assert.Equal(24576L * 1024 * 1024, gpus[0].TotalMemoryBytes);
        Assert.Equal(1, gpus[1].Index);
        Assert.Equal("591.86", NvidiaSmiParser.DriverVersion(gpus));
    }

    [Fact]
    public void ふたつの読みはUUIDで合わさる()
    {
        var merged = NvidiaSmiParser.Merge(
            NvidiaSmiParser.ParseList(SmiList),
            NvidiaSmiParser.ParseQuery(SmiQuery));

        var gpu = Assert.Single(merged);
        Assert.Equal("NVIDIA GeForce RTX 3090", gpu.Name);
        Assert.Equal("591.86", gpu.DriverVersion);
    }

    [Fact]
    public void torchの列挙はROCmでもUUIDとgcnArchを返す()
    {
        var probe = TorchGpuProbe.Parse(TorchOutput);

        Assert.Null(probe.Error);
        Assert.True(probe.IsRocmBuild);
        Assert.Equal("2.13.0+rocm10.0.0", probe.TorchVersion);

        var gpu = Assert.Single(probe.Gpus);
        Assert.Equal("30303030-3031-3937-3030-303030303030", gpu.Uuid);
        Assert.Equal("gfx1151", gpu.GcnArch);
        Assert.True(gpu.IsRocm);
        Assert.Equal(107090132992L, gpu.TotalMemoryBytes);
        Assert.Equal(GpuSource.TorchProbe, gpu.Source);

        // 統合メモリの機体は VRAM が巨大になる（表示が壊れないこと）
        Assert.Contains("99.7 GB", gpu.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void torchの出力に印が無ければ理由1行で返る()
    {
        var probe = TorchGpuProbe.Parse("Traceback (most recent call last):\nImportError: no torch");

        Assert.Empty(probe.Gpus);
        Assert.NotNull(probe.Error);
    }

    [Fact]
    public void 台本はASCIIだけで書かれている()
    {
        // 埋め込み Python の既定の符号化に依らせない（一時檔に書いて渡す）
        foreach (var c in TorchGpuProbe.Script)
        {
            Assert.True(c < 128, "台本は ASCII 限定");
        }
    }

    [Fact]
    public async Task nvidiaSmiが在ればtorchを起こさない()
    {
        var runner = new RecordingRunner
        {
            Responses =
            {
                ["nvidia-smi --query-gpu=" + NvidiaSmiParser.QueryFields + " --format=csv"] = SmiQuery,
                ["nvidia-smi -L"] = SmiList,
            },
        };

        var enumerator = new GpuEnumerator(runner, _ => true, null);
        var result = await enumerator.EnumerateAsync(
            new GpuEnumerationRequest(@"C:\rt\python.exe", TimeSpan.FromSeconds(5)), CancellationToken.None);

        Assert.Equal(GpuSource.NvidiaSmi, result.Source);
        Assert.Single(result.Gpus);
        Assert.DoesNotContain(runner.Calls, call => call.Contains("python.exe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task nvidiaSmiが無ければtorchに落ちる()
    {
        var runner = new RecordingRunner { Fallback = TorchOutput };
        var enumerator = new GpuEnumerator(runner, _ => false, null);

        var result = await enumerator.EnumerateAsync(
            new GpuEnumerationRequest(@"C:\rt\python.exe", TimeSpan.FromSeconds(5)), CancellationToken.None);

        Assert.Equal(GpuSource.TorchProbe, result.Source);
        Assert.Single(result.Gpus);
        Assert.Equal("gfx1151", result.Gpus[0].GcnArch);
    }

    [Fact]
    public async Task 実行系も無ければ理由1行で0台になる()
    {
        var enumerator = new GpuEnumerator(new RecordingRunner(), _ => false, null);

        var result = await enumerator.EnumerateAsync(
            new GpuEnumerationRequest(null, TimeSpan.FromSeconds(5)), CancellationToken.None);

        Assert.Equal(GpuSource.None, result.Source);
        Assert.Empty(result.Gpus);
        Assert.NotNull(result.FailureReason);
    }

    [Theory]
    [InlineData(RuntimeVariants.Cu130, "591.86", true)]
    [InlineData(RuntimeVariants.Cu130, "580.00", true)]
    [InlineData(RuntimeVariants.Cu130, "579.99", false)]
    [InlineData(RuntimeVariants.Cu126, "560.76", true)]
    [InlineData(RuntimeVariants.Cu126, "560.75", false)]
    [InlineData(RuntimeVariants.Cu126, "561", true)]
    public void ドライバの閾は変種ごとに決まる(string variant, string driver, bool ok)
    {
        // 決定 4＝cu130 ≥ 580／cu126 ≥ 560.76（便 B の U-14 で更新するのはここ 1 箇所）
        var verdict = new DriverRequirement().Check(variant, driver);

        Assert.Equal(ok, verdict.Ok);
        Assert.False(verdict.Unknown);
    }

    [Fact]
    public void cu130が足りなければcu126を勧める()
    {
        // 自動切替はしない（裁定 4）＝勧めるだけ
        var verdict = new DriverRequirement().Check(RuntimeVariants.Cu130, "570.10");

        Assert.False(verdict.Ok);
        Assert.Equal(RuntimeVariants.Cu126, verdict.SuggestedVariant);
    }

    [Fact]
    public void ドライバ版が読めない機体は止めずに名乗る()
    {
        // AMD 機＝nvidia-smi が無い＝DriverVersion は null
        var verdict = new DriverRequirement().Check(RuntimeVariants.Cu130, null);

        Assert.True(verdict.Ok);
        Assert.True(verdict.Unknown);
    }

    [Fact]
    public void rocmとcpuにはドライバの下限が無い()
    {
        Assert.Null(DriverRequirement.Minimum(RuntimeVariants.RocmGfx1151));
        Assert.Null(DriverRequirement.Minimum(RuntimeVariants.Cpu));
        Assert.True(new DriverRequirement().Check(RuntimeVariants.RocmGfx1151, null).Ok);
    }

    /// <summary>外の実行檔を起こす代わりに、決め打ちの出力を返す（実機非依存の継ぎ目）。</summary>
    private sealed class RecordingRunner : IProcessRunner
    {
        public Dictionary<string, string> Responses { get; } = new(StringComparer.Ordinal);

        public string? Fallback { get; set; }

        public List<string> Calls { get; } = [];

        public Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var key = fileName + " " + string.Join(' ', arguments);
            Calls.Add(key);

            if (Responses.TryGetValue(key, out var output))
            {
                return Task.FromResult(new ProcessRunResult(true, 0, output, string.Empty, false, null));
            }

            return Task.FromResult(Fallback is null
                ? new ProcessRunResult(false, null, string.Empty, string.Empty, false, "在りません。")
                : new ProcessRunResult(true, 0, Fallback, string.Empty, false, null));
        }
    }
}
