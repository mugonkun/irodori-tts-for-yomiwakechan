using System;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Settings;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 変種で変わる既定（裁定 65・69・設計書 §5）。
/// <b>「未設定」が無い bool の欄は、変種を選んだ瞬間に既定を書き込む</b>。
/// </summary>
public sealed class SettingsDefaultsTests
{
    [Fact]
    public void rocm変種を選ぶと暖機が既定ONになる()
    {
        var settings = SettingsDefaults.ApplyVariant(new LauncherSettings(), RuntimeVariants.RocmGfx1151);

        Assert.True(settings.WarmupOnStart);
        Assert.True(settings.EffectivePrecomputeOnStart());
        Assert.Null(settings.PrecomputeOnStart); // 変種の既定に任せる＝env に載せない
    }

    [Fact]
    public void CUDA変種を選ぶと暖機も潜在も既定OFFになる()
    {
        var settings = SettingsDefaults.ApplyVariant(new LauncherSettings(), RuntimeVariants.Cu126);

        Assert.False(settings.WarmupOnStart);
        Assert.False(settings.EffectivePrecomputeOnStart());
    }

    [Fact]
    public void 変種を変えても利用者の意思は次から残る()
    {
        var settings = SettingsDefaults.ApplyVariant(new LauncherSettings(), RuntimeVariants.RocmGfx1151);
        settings.WarmupOnStart = false; // 利用者が切った

        // 同じ変種を選び直しても書き戻さない
        SettingsDefaults.ApplyVariant(settings, RuntimeVariants.RocmGfx1151);

        Assert.False(settings.WarmupOnStart);
    }

    [Fact]
    public void rocm変種では精度の指定を捨てる()
    {
        // 裁定 5・36＝fp32 を載せると wrapper が exit 2
        var settings = new LauncherSettings { Precision = "fp32" };
        SettingsDefaults.ApplyVariant(settings, RuntimeVariants.RocmGfx1151);

        Assert.Null(settings.Precision);
        Assert.False(RuntimeVariants.AllowsPrecisionOverride(RuntimeVariants.RocmGfx1151));
    }

    [Fact]
    public void CPU変種ではGPUのUUIDを握らない()
    {
        var settings = new LauncherSettings { GpuUuid = "GPU-aaaa", GpuName = "GPU A" };
        SettingsDefaults.ApplyVariant(settings, RuntimeVariants.Cpu);

        Assert.Null(settings.GpuUuid);
        Assert.Null(settings.GpuName);
    }

    [Fact]
    public void 初回だけ変種の既定を敷く()
    {
        var settings = new LauncherSettings { Variant = RuntimeVariants.RocmGfx1151 };
        SettingsDefaults.ApplyFirstRun(settings);
        Assert.True(settings.WarmupOnStart);
        Assert.Equal(0, settings.EmptyCacheInterval); // 裁定 69＝初期値 0

        settings.WarmupOnStart = false;
        settings.FirstRunCompleted = true;
        SettingsDefaults.ApplyFirstRun(settings);
        Assert.False(settings.WarmupOnStart);
    }

    [Fact]
    public void 選んだGPUはUUIDで残る()
    {
        // 裁定 34＝同定は UUID・名前は告知にしか使わない
        var gpu = new GpuInfo(
            "GPU-19adfe89-c9e0-df55-4a8a-e31798717a36", "NVIDIA GeForce RTX 3090",
            0, 25769803776, "00000000:01:00.0", null, "591.86", GpuSource.NvidiaSmi);

        var settings = SettingsDefaults.ApplyGpu(new LauncherSettings(), gpu);

        Assert.Equal(gpu.Uuid, settings.GpuUuid);
        Assert.Equal(gpu.Name, settings.GpuName);

        SettingsDefaults.ApplyGpu(settings, null);
        Assert.Null(settings.GpuUuid);
    }

    [Fact]
    public void 既定のポートは18088で本体の接続先と同じ()
    {
        // 裁定 2＝本体の接続先と食い違わせない
        Assert.Equal(18088, LauncherSettings.DefaultPort);
        Assert.Equal(18088, new LauncherSettings().Port);
    }
}
