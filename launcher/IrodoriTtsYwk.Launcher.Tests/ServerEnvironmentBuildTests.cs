using System;
using System.Collections.Generic;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Server;
using IrodoriTtsYwk.Launcher.Services.Settings;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 起動の env の組み立て（設計書 §2・受け入れ条件 D-2）。
/// <b>載せるのは差分だけ</b>＝wrapper が <c>setdefault</c> する 6 欄は載せ直さない。
/// </summary>
public sealed class ServerEnvironmentBuildTests
{
    private static AppPaths Paths() => AppPaths.Resolve(
        @"C:\Program Files\irodori-tts-ywk",
        @"C:\Users\u\AppData\Local",
        static _ => null);

    private static LauncherSettings Settings(string variant)
    {
        var settings = new LauncherSettings();
        return SettingsDefaults.ApplyVariant(settings, variant);
    }

    [Fact]
    public void deviceはmodelとcodecの2本に同時に載る()
    {
        // 受け入れ条件 D-2＝1 プロセス 1 デバイス・要求 JSON には載らない
        var env = ServerEnvironment.Build(Settings(RuntimeVariants.Cu130), Paths(), gpuIndex: 1);

        Assert.Equal("cuda:1", env[ServerEnvironment.ModelDevice]);
        Assert.Equal("cuda:1", env[ServerEnvironment.CodecDevice]);
    }

    [Fact]
    public void UUIDから解決したindexがそのままcudaNになる()
    {
        // 保存は UUID（裁定 34）・起動のたびに index へ解決する
        var gpus = new List<GpuInfo>
        {
            new("GPU-aaaaaaaa-0000-0000-0000-000000000000", "GPU A", 0, 0, null, null, null, GpuSource.NvidiaSmi),
            new("GPU-bbbbbbbb-0000-0000-0000-000000000000", "GPU B", 1, 0, null, null, null, GpuSource.NvidiaSmi),
        };

        // 前置と大小を吸収する（torch は前置なしを返しうる）
        var index = GpuResolver.ResolveIndex(gpus, "BBBBBBBB-0000-0000-0000-000000000000");
        Assert.Equal(1, index);

        var env = ServerEnvironment.Build(Settings(RuntimeVariants.Cu126), Paths(), index);
        Assert.Equal("cuda:1", env[ServerEnvironment.ModelDevice]);
    }

    [Fact]
    public void 見つからないUUIDはnullで返り告知の1行が出る()
    {
        var gpus = new List<GpuInfo>
        {
            new("GPU-aaaaaaaa-0000-0000-0000-000000000000", "GPU A", 0, 0, null, null, null, GpuSource.NvidiaSmi),
        };

        Assert.Null(GpuResolver.ResolveIndex(gpus, "GPU-cccccccc-0000-0000-0000-000000000000"));
        Assert.Contains(
            "選び直して",
            GpuResolver.NotFoundMessage("NVIDIA GeForce RTX 3090", "GPU-cccccccc"),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RuntimeVariants.Cu130, "cuda")]
    [InlineData(RuntimeVariants.Cu126, "cuda")]
    [InlineData(RuntimeVariants.Cpu, "cpu")]
    [InlineData(RuntimeVariants.RocmGfx1151, "rocm-gfx1151")]
    public void 変種のラベルは台帳の綴りと別系統(string variant, string label)
    {
        // cu130 と cu126 はどちらも YWK_VARIANT=cuda（契約 ⑹＝何で組んだかの名前）
        var env = ServerEnvironment.Build(Settings(variant), Paths(), gpuIndex: 0);

        Assert.Equal(label, env[ServerEnvironment.Variant]);
        Assert.Equal("runtime-" + variant, RuntimeVariants.LedgerName(variant));
    }

    [Theory]
    [InlineData(RuntimeVariants.RocmGfx1151, true)]
    [InlineData(RuntimeVariants.Cu130, false)]
    [InlineData(RuntimeVariants.Cu126, false)]
    [InlineData(RuntimeVariants.Cpu, false)]
    public void MIOpenの探索はRadeon変種だけFASTにする(string variant, bool expected)
    {
        // 裁定 157＝初見の出力長ごとの MIOpen 探索（decode_latent＋透かしで 6〜8 秒）を止める。
        // CUDA・CPU には効かない環境変数なので載せない（二重定義も残骸も作らない）。
        var env = ServerEnvironment.Build(Settings(variant), Paths(), gpuIndex: RuntimeVariants.IsCpu(variant) ? null : 0);

        Assert.Equal(expected, env.ContainsKey(ServerEnvironment.MiopenFindMode));
        if (expected)
        {
            Assert.Equal("2", env[ServerEnvironment.MiopenFindMode]);
        }
    }

    [Theory]
    [InlineData(RuntimeVariants.RocmGfx1151, ServerEnvironment.AllocConfExpandableSegments)]
    [InlineData(RuntimeVariants.Cu130, ServerEnvironment.AllocConfCudaDefault)]
    [InlineData(RuntimeVariants.Cu126, ServerEnvironment.AllocConfCudaDefault)]
    public void GPU変種はallocatorの設定を両方の名に同じ値で載せる(string variant, string expected)
    {
        // 裁定 160（検分の是正）＝torch は CUDA → HIP の順に最初に在る名を読むので両方に載せる。Radeon（torch 2.13）は
        // expandable_segments、RTX（torch 2.10）は Windows で受け付けないので garbage_collection_threshold:0.6。
        var env = ServerEnvironment.Build(Settings(variant), Paths(), gpuIndex: 0);

        Assert.Equal(expected, env[ServerEnvironment.CudaAllocConf]);
        Assert.Equal(expected, env[ServerEnvironment.HipAllocConf]);
    }

    [Theory]
    [InlineData(RuntimeVariants.RocmGfx1151, "expandable_segments:True", "expandable_segments:True,garbage_collection_threshold:0.8")]
    [InlineData(RuntimeVariants.Cu130, "garbage_collection_threshold:0.6", "garbage_collection_threshold:0.8")]
    [InlineData(RuntimeVariants.Cu126, "garbage_collection_threshold:0.6", "garbage_collection_threshold:0.8")]
    public void GPUメモリの上限はMiBで渡しallocatorの閾値を上限比に上げる(string variant, string offConf, string onConf)
    {
        // 裁定 160＝設定 gpuMemoryLimitGiB（0＝制限しない）。0 なら上限の env は無く、allocator は変種の既定のまま。
        var settings = Settings(variant);
        Assert.Equal(0, settings.GpuMemoryLimitGiB);
        var off = ServerEnvironment.Build(settings, Paths(), gpuIndex: 0);
        Assert.False(off.ContainsKey(ServerEnvironment.GpuMemoryLimitMib));
        Assert.Equal(offConf, off[ServerEnvironment.CudaAllocConf]);
        Assert.Equal(offConf, off[ServerEnvironment.HipAllocConf]);

        settings.GpuMemoryLimitGiB = 6;
        var on = ServerEnvironment.Build(settings, Paths(), gpuIndex: 0);
        Assert.Equal("6144", on[ServerEnvironment.GpuMemoryLimitMib]);
        Assert.Equal(onConf, on[ServerEnvironment.CudaAllocConf]);
        Assert.Equal(onConf, on[ServerEnvironment.HipAllocConf]);
    }

    [Fact]
    public void CPU変種にはGPUメモリの上限を載せない()
    {
        var settings = Settings(RuntimeVariants.Cpu);
        settings.GpuMemoryLimitGiB = 6;
        var env = ServerEnvironment.Build(settings, Paths(), gpuIndex: null);
        Assert.False(env.ContainsKey(ServerEnvironment.GpuMemoryLimitMib));
    }

    [Fact]
    public void CPU変種にはallocatorの設定を載せない()
    {
        var env = ServerEnvironment.Build(Settings(RuntimeVariants.Cpu), Paths(), gpuIndex: null);

        Assert.False(env.ContainsKey(ServerEnvironment.HipAllocConf));
        Assert.False(env.ContainsKey(ServerEnvironment.CudaAllocConf));
        Assert.False(env.ContainsKey(ServerEnvironment.GpuResourceCacheSize));
    }

    [Theory]
    [InlineData(RuntimeVariants.RocmGfx1151, true)]
    [InlineData(RuntimeVariants.Cu130, false)]
    [InlineData(RuntimeVariants.Cu126, false)]
    [InlineData(RuntimeVariants.Cpu, false)]
    public void ROCmランタイムの溜め置きはRadeon変種だけ止める(string variant, bool expected)
    {
        // 裁定 160＝Windows の ROCm ランタイムは hipFree された塊を同じ大きさの要求にしか使い回さない（上限 ≈10 GiB）。
        // CUDA には無い仕組みなので載せない（MIOPEN_FIND_MODE と同じ作法）。
        var env = ServerEnvironment.Build(Settings(variant), Paths(), gpuIndex: RuntimeVariants.IsCpu(variant) ? null : 0);

        Assert.Equal(expected, env.ContainsKey(ServerEnvironment.GpuResourceCacheSize));
        if (expected)
        {
            Assert.Equal("0", env[ServerEnvironment.GpuResourceCacheSize]);
        }
    }

    [Fact]
    public void CPU変種のdeviceはcpuになる()
    {
        var env = ServerEnvironment.Build(Settings(RuntimeVariants.Cpu), Paths(), gpuIndex: null);

        Assert.Equal("cpu", env[ServerEnvironment.ModelDevice]);
        Assert.Equal("cpu", env[ServerEnvironment.CodecDevice]);
    }

    [Fact]
    public void 精度の既定は載せない()
    {
        // 裁定 7＝device 連動を wrapper に任せる（二重定義を作らない）
        var env = ServerEnvironment.Build(Settings(RuntimeVariants.Cu130), Paths(), 0);

        Assert.False(env.ContainsKey(ServerEnvironment.ModelPrecision));
        Assert.False(env.ContainsKey(ServerEnvironment.CodecPrecision));
    }

    [Fact]
    public void rocm変種では精度を絶対に載せない()
    {
        // 裁定 5・36＝fp32 を載せると wrapper が exit 2
        var settings = Settings(RuntimeVariants.RocmGfx1151);
        settings.Precision = "fp32"; // 利用者が無理に書いても

        var env = ServerEnvironment.Build(settings, Paths(), 0);

        Assert.False(env.ContainsKey(ServerEnvironment.ModelPrecision));
        Assert.Null(settings.EffectivePrecision());
    }

    [Fact]
    public void 上級者が精度を選んだときだけ2本に載る()
    {
        var settings = Settings(RuntimeVariants.Cu130);
        settings.Precision = "fp32";

        var env = ServerEnvironment.Build(settings, Paths(), 0);

        Assert.Equal("fp32", env[ServerEnvironment.ModelPrecision]);
        Assert.Equal("fp32", env[ServerEnvironment.CodecPrecision]);
    }

    [Fact]
    public void wrapperがsetdefaultする6欄は載せ直さない()
    {
        var env = ServerEnvironment.Build(Settings(RuntimeVariants.Cu130), Paths(), 0);

        foreach (var name in new[]
                 {
                     "IRODORI_HF_CHECKPOINT", "IRODORI_PRELOAD", "IRODORI_ALLOW_NO_REF_VOICE",
                     "IRODORI_DEFAULT_VOICE", "IRODORI_DEFAULT_NUM_STEPS", "IRODORI_DEFAULT_RESPONSE_FORMAT",
                 })
        {
            Assert.False(env.ContainsKey(name), name + " は wrapper の setdefault に任せる");
        }
    }

    [Fact]
    public void 場所はwrapperの既定と同じ所を指す()
    {
        var paths = Paths();
        var env = ServerEnvironment.Build(Settings(RuntimeVariants.Cu130), paths, 0);

        Assert.Equal(paths.DataDir, env[ServerEnvironment.DataDir]);
        Assert.Equal(paths.VoicesDir, env[ServerEnvironment.VoicesDir]);
        Assert.Equal(paths.VoicesJsonPath, env[ServerEnvironment.VoiceAliasesFile]);
        Assert.Equal(paths.HfHomeDir, env[ServerEnvironment.HfHome]);
        Assert.Equal("1", env[ServerEnvironment.HfHubOffline]);
    }

    [Fact]
    public void 暖機はrocmで既定ONになりenvに段が載る()
    {
        // 設計書 §5・便 C の実測（話者切替の罰が 1 秒級）
        var settings = Settings(RuntimeVariants.RocmGfx1151);
        Assert.True(settings.WarmupOnStart);

        var env = ServerEnvironment.Build(settings, Paths(), 0);
        Assert.Equal("1", env[ServerEnvironment.WarmupOnStart]);
        Assert.Equal("4,8,12", env[ServerEnvironment.WarmupStages]);
    }

    [Fact]
    public void 暖機はCUDAで既定OFFになりenvに載らない()
    {
        var settings = Settings(RuntimeVariants.Cu130);
        Assert.False(settings.WarmupOnStart);

        var env = ServerEnvironment.Build(settings, Paths(), 0);
        Assert.False(env.ContainsKey(ServerEnvironment.WarmupOnStart));
    }

    [Fact]
    public void 事前計算は未設定なら載せず変種の既定に任せる()
    {
        // 裁定 65＝wrapper の未設定時の解決（rocm-* は 1）と同じ規則なので二重定義を作らない
        var rocm = Settings(RuntimeVariants.RocmGfx1151);
        Assert.Null(rocm.PrecomputeOnStart);
        Assert.True(rocm.EffectivePrecomputeOnStart());
        Assert.False(ServerEnvironment.Build(rocm, Paths(), 0).ContainsKey(ServerEnvironment.PrecomputeOnStart));

        var cuda = Settings(RuntimeVariants.Cu130);
        Assert.False(cuda.EffectivePrecomputeOnStart());

        cuda.PrecomputeOnStart = true; // 明示すれば両方向に上書き
        Assert.Equal("1", ServerEnvironment.Build(cuda, Paths(), 0)[ServerEnvironment.PrecomputeOnStart]);
    }

    [Fact]
    public void empty_cache_intervalの初期値は0で必ず載る()
    {
        // 裁定 69＝設定項目・初期値 0
        var settings = Settings(RuntimeVariants.RocmGfx1151);
        Assert.Equal(0, settings.EmptyCacheInterval);
        Assert.Equal("0", ServerEnvironment.Build(settings, Paths(), 0)[ServerEnvironment.EmptyCacheInterval]);

        settings.EmptyCacheInterval = 8;
        Assert.Equal("8", ServerEnvironment.Build(settings, Paths(), 0)[ServerEnvironment.EmptyCacheInterval]);
    }

    [Fact]
    public void 起動の引数はモジュール実行の4語になる()
    {
        var request = ServerLaunchPlan.Build(
            Settings(RuntimeVariants.RocmGfx1151), Paths(), @"C:\rt\python.exe", 0);

        Assert.Equal(
            new[] { "-m", "ywk_server", "--host", "127.0.0.1", "--port", "18088" },
            request.Arguments);
        Assert.Equal(new Uri("http://127.0.0.1:18088/"), request.BaseAddress);
        Assert.Equal("127.0.0.1", request.Host); // localhost の名前指定は禁止（契約 ⑴）
    }

    [Fact]
    public void ready待ちの既定はCPUだけ300秒()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(120),
            ServerLaunchPlan.Build(Settings(RuntimeVariants.Cu130), Paths(), @"C:\rt\python.exe", 0).ReadyTimeout);
        Assert.Equal(
            TimeSpan.FromSeconds(300),
            ServerLaunchPlan.Build(Settings(RuntimeVariants.Cpu), Paths(), @"C:\rt\python.exe", null).ReadyTimeout);
    }
}
