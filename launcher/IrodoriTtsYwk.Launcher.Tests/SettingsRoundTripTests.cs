using System;
using System.IO;
using IrodoriTtsYwk.Launcher.Contracts;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// settings.json の往復（契約 ⑻）。継ぎ目は <see cref="JsonSettingsStore"/> の
/// <b>public コンストラクタ</b>＝檔のパスを渡すだけで実機に依らず試せる。
/// </summary>
public sealed class SettingsRoundTripTests : IDisposable
{
    private readonly string _dir;

    public SettingsRoundTripTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ywk-launcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // 掃除に失敗しても試験の結果は変わらない
        }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    [Fact]
    public void 書いて読むと全欄が戻る()
    {
        var store = new JsonSettingsStore(PathFor("settings.json"));
        var written = new LauncherSettings
        {
            GpuUuid = "GPU-19adfe89-c9e0-df55-4a8a-e31798717a36",
            GpuName = "NVIDIA GeForce RTX 3090",
            Variant = RuntimeVariants.RocmGfx1151,
            Precision = "bf16",
            Port = 18094,
            HfHome = @"C:\Users\someone\.cache\huggingface",
            WarmupOnStart = true,
            WarmupStages = [4, 8, 12],
            WarmupVoices = ["デフォルト", "琴葉茜"],
            WarmupText = "暖機です。",
            PrecomputeOnStart = true,
            EmptyCacheInterval = 5,
            UiScale = 1.25,
            VoiceOrder = ["デフォルト", "つくよみちゃん"],
            ReadyTimeoutSeconds = 180,
            FirstRunCompleted = true,
            AcceptedNoticesSha256 = new string('a', 64),
            AutoStartServer = false,
            ShowMemoryPanel = false,
            LastTestVoice = "月読アイ",
            LastTestNumSteps = 10,
        };

        store.Save(written);
        var read = store.Load();

        Assert.Null(store.LastLoadError);
        Assert.Equal(written.GpuUuid, read.GpuUuid);
        Assert.Equal(written.GpuName, read.GpuName);
        Assert.Equal(written.Variant, read.Variant);
        Assert.Equal(written.Precision, read.Precision);
        Assert.Equal(written.Port, read.Port);
        Assert.Equal(written.HfHome, read.HfHome);
        Assert.True(read.WarmupOnStart);
        Assert.Equal(written.WarmupStages, read.WarmupStages);
        Assert.Equal(written.WarmupVoices, read.WarmupVoices);
        Assert.Equal(written.WarmupText, read.WarmupText);
        Assert.True(read.PrecomputeOnStart);
        Assert.Equal(written.EmptyCacheInterval, read.EmptyCacheInterval);
        Assert.Equal(written.UiScale, read.UiScale);
        Assert.Equal(written.VoiceOrder, read.VoiceOrder);
        Assert.Equal(written.ReadyTimeoutSeconds, read.ReadyTimeoutSeconds);
        Assert.True(read.FirstRunCompleted);
        Assert.Equal(written.AcceptedNoticesSha256, read.AcceptedNoticesSha256);
        Assert.False(read.AutoStartServer);
        Assert.False(read.ShowMemoryPanel);
        Assert.Equal(written.LastTestVoice, read.LastTestVoice);
        Assert.Equal(written.LastTestNumSteps, read.LastTestNumSteps);
    }

    [Fact]
    public void 日本語の話者名がエスケープされずに檔へ出る()
    {
        // 話者 id は日本語（契約 ⑷ 4-1）。\uXXXX に潰すと利用者が檔を読めない。
        var path = PathFor("settings.json");
        var store = new JsonSettingsStore(path);
        store.Save(new LauncherSettings { LastTestVoice = "琴葉茜" });

        var text = File.ReadAllText(path);
        Assert.Contains("琴葉茜", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 檔が無ければ既定値で理由も出ない()
    {
        var store = new JsonSettingsStore(PathFor("missing.json"));
        var settings = store.Load();

        Assert.Null(store.LastLoadError);
        Assert.Equal(LauncherSettings.DefaultPort, settings.Port);
        Assert.Equal(RuntimeVariants.Cu130, settings.Variant);
        Assert.Equal(0, settings.EmptyCacheInterval); // 裁定 69＝初期値 0
    }

    [Fact]
    public void 壊れていても既定値で立ち上がり理由が1行残る()
    {
        var path = PathFor("broken.json");
        File.WriteAllText(path, "{ これは JSON ではない");
        var store = new JsonSettingsStore(path);

        var settings = store.Load();

        Assert.NotNull(store.LastLoadError);
        Assert.Equal(LauncherSettings.DefaultPort, settings.Port);
    }

    [Fact]
    public void 保存は原子的で一時檔を残さない()
    {
        var path = PathFor("settings.json");
        var store = new JsonSettingsStore(path);
        store.Save(new LauncherSettings());
        store.Save(new LauncherSettings { Port = 18095 });

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(18095, store.Load().Port);
    }

    [Fact]
    public void 範囲外の値は既定へ寄る()
    {
        var settings = JsonSettingsStore.Sanitize(new LauncherSettings
        {
            Port = 0,
            Variant = "   ",
            UiScale = 99,
            LastTestNumSteps = -1,
            EmptyCacheInterval = -5,
            ReadyTimeoutSeconds = -1,
        });

        Assert.Equal(LauncherSettings.DefaultPort, settings.Port);
        Assert.Equal(RuntimeVariants.Cu130, settings.Variant);
        Assert.Equal(1.0, settings.UiScale);
        Assert.Equal(40, settings.LastTestNumSteps);
        Assert.Equal(0, settings.EmptyCacheInterval);
        Assert.Equal(0, settings.ReadyTimeoutSeconds);
    }

    [Fact]
    public void 知らない欄があっても読める()
    {
        // 契約 ⑻＝欄を足すのは schema を上げない。古いランチャが新しい檔を読んでも落ちない。
        var path = PathFor("future.json");
        File.WriteAllText(path, "{\"schema\":1,\"port\":18096,\"somethingNew\":{\"a\":1}}");
        var store = new JsonSettingsStore(path);

        var settings = store.Load();

        Assert.Null(store.LastLoadError);
        Assert.Equal(18096, settings.Port);
    }

    [Fact]
    public void 解決済みの既定は変種で決まる()
    {
        var rocm = new LauncherSettings { Variant = RuntimeVariants.RocmGfx1151, Precision = "fp32" };
        var cpu = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var cuda = new LauncherSettings { Variant = RuntimeVariants.Cu130 };

        // 裁定 5・36＝rocm 変種に fp32 は載せない（載せると wrapper が exit 2）
        Assert.Null(rocm.EffectivePrecision());
        // 裁定 65＝事前計算の既定 ON は rocm だけ
        Assert.True(rocm.EffectivePrecomputeOnStart());
        Assert.False(cuda.EffectivePrecomputeOnStart());
        // 契約 ⑵＝ready 待ち 120 s・CPU 変種は 300 s
        Assert.Equal(TimeSpan.FromSeconds(120), cuda.EffectiveReadyTimeout());
        Assert.Equal(TimeSpan.FromSeconds(300), cpu.EffectiveReadyTimeout());
    }
}
