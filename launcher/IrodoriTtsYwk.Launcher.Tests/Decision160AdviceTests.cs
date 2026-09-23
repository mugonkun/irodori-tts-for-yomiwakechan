using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Settings;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// <b>GPU メモリの上限の「おすすめ」</b>（裁定 160＝司令官の指示 2026-09-24
/// 「vram は初期設定と再スキャンで最適な占有メモリを提示していいかもね。」
/// ＋「共有メモリは判定の材料に使わないでね。」）。
/// <para>
/// 釘付けするのは 3 つ＝⑴ 材料は<b>専用</b>メモリ 1 本だけ ⑵ 規則の表
/// ⑶ 出る場所は「はじめの準備」と「数え直す」の 2 面（押す手は増えない）。
/// </para>
/// </summary>
public sealed class GpuMemoryLimitAdviceTests
{
    private const long Gib = 1024L * 1024 * 1024;

    private static GpuInfo Amd(long totalBytes) =>
        new(
            "GPU-amd-0", "AMD Radeon(TM) 8060S Graphics", 0, totalBytes,
            null, "gfx1151", null, GpuSource.TorchProbe);

    private static GpuInfo Nvidia(long totalBytes, string name = "NVIDIA GeForce RTX 4060") =>
        new("GPU-nv-0", name, 0, totalBytes, null, null, "580.01", GpuSource.NvidiaSmi);

    private static GpuAdapterInfo Adapter(string name, long bytes) =>
        new("luid-" + name + "-" + bytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            name, bytes);

    [Fact]
    public void AMDはtorchの総量ではなくDXGIの専用メモリで見る()
    {
        // 本機の実測＝torch は 107 GB（専用＋共有）と名乗り、DXGI は 68,535,640,064 B（63.83 GiB）。
        // 共有ぶんを材料にすると「64 GB の板」を「107 GB の板」と読み違える（司令官の指示 2）。
        var gpu = Amd(107_000_000_000L);
        IReadOnlyList<GpuAdapterInfo> adapters =
        [
            Adapter("AMD Radeon(TM) 8060S Graphics", 68_535_640_064L),
            Adapter("AMD Radeon(TM) 8060S Graphics", 68_535_640_064L),
            Adapter("AMD Radeon(TM) 8060S Graphics", 68_535_640_064L),
            Adapter("AMD Radeon(TM) 8060S Graphics", 68_535_640_064L),
        ];

        var dedicated = GpuMemoryLimitAdvice.DedicatedBytes(gpu, adapters);

        Assert.Equal(68_535_640_064L, dedicated);
        Assert.Equal(64, GpuMemoryLimitAdvice.Gigabytes(dedicated!.Value));

        // 20 GiB 以上＝上限は要らない（0 のまま）。
        Assert.Equal(0, GpuMemoryLimitAdvice.Recommend(dedicated, RuntimeVariants.RocmGfx1151));
    }

    [Fact]
    public void NVIDIAは同じ名前のDXGIの板が在ればそれを使う()
    {
        var gpu = Nvidia(8 * Gib);
        IReadOnlyList<GpuAdapterInfo> adapters = [Adapter("NVIDIA GeForce RTX 4060", 8 * Gib)];

        Assert.Equal(8 * Gib, GpuMemoryLimitAdvice.DedicatedBytes(gpu, adapters));
        Assert.Equal(
            5,
            GpuMemoryLimitAdvice.Recommend(
                GpuMemoryLimitAdvice.DedicatedBytes(gpu, adapters), RuntimeVariants.Cu130));
    }

    [Fact]
    public void NVIDIAはDXGIに合う板が無ければnvidiasmiの総量に落ちる()
    {
        // nvidia-smi の総量は**専用**メモリである＝この 1 つだけが控えとして許される。
        var gpu = Nvidia(8 * Gib);
        IReadOnlyList<GpuAdapterInfo> adapters = [Adapter("Intel(R) UHD Graphics", 128 * 1024 * 1024)];

        Assert.Equal(8 * Gib, GpuMemoryLimitAdvice.DedicatedBytes(gpu, adapters));
        Assert.Equal(8 * Gib, GpuMemoryLimitAdvice.DedicatedBytes(gpu, []));
    }

    [Fact]
    public void AMDはDXGIに合う板が無ければおすすめを出さない()
    {
        // torch の総量に落ちると 107 GB を材料にしてしまう＝黙るのが正しい。
        var gpu = Amd(107_000_000_000L);

        Assert.Null(GpuMemoryLimitAdvice.DedicatedBytes(gpu, []));
        Assert.Null(GpuMemoryLimitAdvice.DedicatedBytes(gpu, [Adapter("NVIDIA GeForce RTX 3090", 24 * Gib)]));
        Assert.Equal(0, GpuMemoryLimitAdvice.Recommend(null, RuntimeVariants.RocmGfx1151));
    }

    [Fact]
    public void 同じ名前の板が違う量を名乗る回はおすすめを出さない()
    {
        var gpu = Nvidia(8 * Gib);
        IReadOnlyList<GpuAdapterInfo> adapters =
        [
            Adapter("NVIDIA GeForce RTX 4060", 8 * Gib),
            Adapter("NVIDIA GeForce RTX 4060", 12 * Gib),
        ];

        Assert.Null(GpuMemoryLimitAdvice.DedicatedBytes(gpu, adapters));
    }

    [Fact]
    public void GPUを選んでいなければ材料が無い() =>
        Assert.Null(GpuMemoryLimitAdvice.DedicatedBytes(null, [Adapter("NVIDIA GeForce RTX 4060", 8 * Gib)]));

    [Theory]
    [InlineData(4, 4)]     // 2.6 → 下限の 4 まで上げる（3 以下は読み込みが落ちる）
    [InlineData(6, 4)]     // 3.9 → 4
    [InlineData(8, 5)]     // 5.2 → 5
    [InlineData(10, 6)]    // 6.5 → 6
    [InlineData(12, 7)]    // 7.8 → 7
    [InlineData(16, 10)]   // 10.4 → 10
    [InlineData(20, 0)]    // 20 以上は上限が要らない
    [InlineData(24, 0)]
    [InlineData(64, 0)]
    public void おすすめの表(int dedicatedGiB, int expected) =>
        Assert.Equal(expected, GpuMemoryLimitAdvice.Recommend(dedicatedGiB * Gib, RuntimeVariants.Cu130));

    [Fact]
    public void CPUで動かす回は上限を勧めない()
    {
        Assert.Equal(0, GpuMemoryLimitAdvice.Recommend(8 * Gib, RuntimeVariants.Cpu));
        Assert.Null(GpuMemoryLimitAdvice.Line(8 * Gib, RuntimeVariants.Cpu, 0));
    }

    [Fact]
    public void おすすめの1行はUiStringsから組む()
    {
        var advice = GpuMemoryLimitAdvice.Line(8 * Gib, RuntimeVariants.Cu130, 0);
        Assert.Equal("この GPU（8 GB）のおすすめは 5 GB です。", advice);

        // いまの値が既におすすめ＝そう言う（釦は押せない＝SettingsViewModel 側で釘付け）。
        var same = GpuMemoryLimitAdvice.Line(8 * Gib, RuntimeVariants.Cu130, 5);
        Assert.NotNull(same);
        Assert.Contains("いまはその値になっています", same!, StringComparison.Ordinal);

        // 上限が要らない板＝0 のままでよいと言う。
        Assert.Equal(
            "この GPU（24 GB）では上限は要りません（0 のまま）。",
            GpuMemoryLimitAdvice.Line(24 * Gib, RuntimeVariants.Cu130, 0));

        // 材料が無ければ 1 行も出さない。
        Assert.Null(GpuMemoryLimitAdvice.Line(null, RuntimeVariants.Cu130, 0));
    }

    [Fact]
    public void GBの数はいちばん近い整数にする()
    {
        Assert.Equal(64, GpuMemoryLimitAdvice.Gigabytes(68_535_640_064L)); // 63.83
        Assert.Equal(8, GpuMemoryLimitAdvice.Gigabytes(8_585_216_000L));   // 7.995
        Assert.Equal(24, GpuMemoryLimitAdvice.Gigabytes(25_753_026_560L)); // 23.98
    }
}

/// <summary>
/// <b>はじめの準備で敷く上限</b>（裁定 160）＝押す手は 1 つも増えないこと・
/// 利用者の値には触らないことの 2 つを釘付けする。
/// </summary>
public sealed class GpuMemoryLimitFirstRunTests
{
    private const long Gib = 1024L * 1024 * 1024;

    private static GpuInfo Nvidia(long totalBytes) =>
        new("GPU-nv-0", "NVIDIA GeForce RTX 4060", 0, totalBytes, null, null, "580.01", GpuSource.NvidiaSmi);

    private static IReadOnlyList<GpuAdapterInfo> Adapters(long bytes) =>
        [new GpuAdapterInfo("luid-0", "NVIDIA GeForce RTX 4060", bytes)];

    [Fact]
    public void 初期設定の8GBの板は押さずに5GBで始まる()
    {
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130 };
        Assert.False(settings.FirstRunCompleted);
        Assert.Equal(0, settings.GpuMemoryLimitGiB);

        SettingsDefaults.ApplyGpu(settings, Nvidia(8 * Gib), Adapters(8 * Gib));

        Assert.Equal(5, settings.GpuMemoryLimitGiB);
        Assert.Equal("GPU-nv-0", settings.GpuUuid);
    }

    [Fact]
    public void はじめの準備が済んだ機体には敷かない()
    {
        var settings = new LauncherSettings
        {
            Variant = RuntimeVariants.Cu130,
            FirstRunCompleted = true,
        };

        SettingsDefaults.ApplyGpu(settings, Nvidia(8 * Gib), Adapters(8 * Gib));

        Assert.Equal(0, settings.GpuMemoryLimitGiB);
    }

    [Fact]
    public void 利用者が入れた値は上書きしない()
    {
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130, GpuMemoryLimitGiB = 7 };

        SettingsDefaults.ApplyGpu(settings, Nvidia(8 * Gib), Adapters(8 * Gib));

        Assert.Equal(7, settings.GpuMemoryLimitGiB);
    }

    [Fact]
    public void 上限の要らない板には何も入れない()
    {
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130 };

        SettingsDefaults.ApplyGpu(settings, Nvidia(24 * Gib), Adapters(24 * Gib));

        Assert.Equal(0, settings.GpuMemoryLimitGiB);
    }

    [Fact]
    public void アダプタを数えていない呼び手では何も起きない()
    {
        // 既存の呼び手（2 引数）は 1 字も振る舞いが変わらない。
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130 };

        SettingsDefaults.ApplyGpu(settings, Nvidia(8 * Gib));

        // NVIDIA は nvidia-smi の総量に落ちるので 5 が入る（控えは NVIDIA だけ）。
        Assert.Equal(5, settings.GpuMemoryLimitGiB);
        Assert.Equal(0, SettingsDefaults.GpuMemoryLimitFor(
            new LauncherSettings { Variant = RuntimeVariants.Cu130 }, null, null));
    }

    [Fact]
    public void 完了の本文は敷いた回だけ1文増える()
    {
        Assert.Equal(FirstRunViewModel.DoneLine, FirstRunViewModel.DoneLineWith(0));

        var withLimit = FirstRunViewModel.DoneLineWith(5);
        Assert.StartsWith(FirstRunViewModel.DoneLine, withLimit, StringComparison.Ordinal);
        Assert.Contains("GPU メモリの上限はおすすめの 5 GB にしました", withLimit, StringComparison.Ordinal);
        Assert.Contains(UiStrings.AdvancedHeader, withLimit, StringComparison.Ordinal);
    }

    [Fact]
    public void ウィザードは報せを受けてから1文を出す()
    {
        var root = Path.Combine(Path.GetTempPath(), "ywk-d160-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(
                Path.Combine(root, "install"),
                Path.Combine(root, "app"),
                Path.Combine(root, "runtime"),
                Path.Combine(root, "data"),
                developerMode: true);

            var vm = new FirstRunViewModel(
                paths,
                new LauncherSettings(),
                new JsonSettingsStore(paths.SettingsPath),
                new DriverRequirement(),
                static () => null,
                static () => null,
                static _ => Task.FromResult(false));

            Assert.Equal(FirstRunViewModel.DoneLine, vm.DoneText);

            vm.NoteGpuMemoryLimit(0);                                  // 敷かなかった回は黙る
            Assert.Equal(FirstRunViewModel.DoneLine, vm.DoneText);

            vm.NoteGpuMemoryLimit(5);
            Assert.Contains("おすすめの 5 GB", vm.DoneText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

/// <summary>
/// <b>「数え直す」のおすすめ</b>（裁定 160）＝1 行と釦は詳細 9 行目の中に出る。
/// 釦は<b>写しに入れるだけ</b>で、本物に届くのは〔適用〕である。
/// </summary>
public sealed class GpuMemoryLimitSettingsAdviceTests : IDisposable
{
    private const long Gib = 1024L * 1024 * 1024;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-d160s-" + Guid.NewGuid().ToString("N"));

    private sealed class FixedEnumerator(params GpuInfo[] gpus) : IGpuEnumerator
    {
        public Task<GpuEnumerationResult> EnumerateAsync(
            GpuEnumerationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new GpuEnumerationResult(
                gpus, GpuSource.NvidiaSmi, TimeSpan.FromSeconds(1), null));
    }

    private sealed class FixedAdapters(params GpuAdapterInfo[] adapters) : IGpuAdapterInfoSource
    {
        public IReadOnlyList<GpuAdapterInfo> Adapters() => adapters;
    }

    private static GpuInfo Nvidia(long totalBytes) =>
        new("GPU-nv-0", "NVIDIA GeForce RTX 4060", 0, totalBytes, null, null, "580.01", GpuSource.NvidiaSmi);

    private SettingsViewModel Create(
        LauncherSettings settings,
        IGpuEnumerator enumerator,
        IGpuAdapterInfoSource? adapters,
        out ISettingsStore store)
    {
        Directory.CreateDirectory(Path.Combine(_root, "app", "ledger"));
        foreach (var name in new[] { "runtime-cu130", "runtime-cpu", "models", "python-embed" })
        {
            File.WriteAllText(Path.Combine(_root, "app", "ledger", name + ".json"), "{}");
        }

        var paths = new AppPaths(
            Path.Combine(_root, "install"),
            Path.Combine(_root, "app"),
            Path.Combine(_root, "runtime"),
            Path.Combine(_root, "data"),
            developerMode: true);

        Directory.CreateDirectory(paths.DataDir);
        store = new JsonSettingsStore(paths.SettingsPath);
        return new SettingsViewModel(settings, store, paths, enumerator, new DriverRequirement())
        {
            AdapterSource = adapters,
        };
    }

    [Fact]
    public async Task 数え直すとおすすめが出て釦で写しに入り適用で本物へ届く()
    {
        var live = new LauncherSettings { Variant = RuntimeVariants.Cu130, FirstRunCompleted = true };
        var vm = Create(
            live,
            new FixedEnumerator(Nvidia(8 * Gib)),
            new FixedAdapters(new GpuAdapterInfo("luid-0", "NVIDIA GeForce RTX 4060", 8 * Gib)),
            out var store);

        // 数え直す前は材料が無い＝1 行も釦も出さない。
        Assert.False(vm.GpuMemoryLimitAdviceVisible);

        await vm.RefreshGpusAsync();

        Assert.True(vm.GpuMemoryLimitAdviceVisible);
        Assert.Equal(5, vm.GpuMemoryLimitRecommendation);
        Assert.Contains("8 GB", vm.GpuMemoryLimitAdviceText, StringComparison.Ordinal);
        Assert.Contains("5 GB", vm.GpuMemoryLimitAdviceText, StringComparison.Ordinal);
        Assert.True(vm.GpuMemoryLimitAdviceCommand.CanExecute(null));

        vm.GpuMemoryLimitAdviceCommand.Execute(null);

        // 写しには入ったが本物はまだ 0＝〔適用〕を押すのは利用者である。
        Assert.Equal(5, vm.GpuMemoryLimitGiB);
        Assert.True(vm.IsDirty);
        Assert.Equal(0, live.GpuMemoryLimitGiB);

        // もうおすすめの値＝釦は押せない・1 行はそう言う。
        Assert.False(vm.GpuMemoryLimitAdviceCommand.CanExecute(null));
        Assert.Contains("いまはその値になっています", vm.GpuMemoryLimitAdviceText, StringComparison.Ordinal);

        vm.Apply();

        Assert.Equal(5, live.GpuMemoryLimitGiB);
        Assert.Equal(5, store.Load().GpuMemoryLimitGiB);
    }

    [Fact]
    public async Task 上限の要らない板では0のままでよいと言う()
    {
        var live = new LauncherSettings { Variant = RuntimeVariants.Cu130, FirstRunCompleted = true };
        var vm = Create(
            live,
            new FixedEnumerator(Nvidia(24 * Gib)),
            new FixedAdapters(new GpuAdapterInfo("luid-0", "NVIDIA GeForce RTX 4060", 24 * Gib)),
            out _);

        await vm.RefreshGpusAsync();

        Assert.True(vm.GpuMemoryLimitAdviceVisible);
        Assert.Equal(0, vm.GpuMemoryLimitRecommendation);
        Assert.Contains("上限は要りません", vm.GpuMemoryLimitAdviceText, StringComparison.Ordinal);

        // 既に 0＝押す先が無い。
        Assert.False(vm.GpuMemoryLimitAdviceCommand.CanExecute(null));
    }

    [Fact]
    public async Task 専用メモリが判らない回は1行も釦も出さない()
    {
        // AMD の板で DXGI の口が差さっていない（＝数えられない）回＝torch の総量には落ちない。
        var live = new LauncherSettings { Variant = RuntimeVariants.Cu130, FirstRunCompleted = true };
        var amd = new GpuInfo(
            "GPU-amd-0", "AMD Radeon(TM) 8060S Graphics", 0, 107_000_000_000L,
            null, "gfx1151", null, GpuSource.TorchProbe);

        var vm = Create(live, new FixedEnumerator(amd), adapters: null, out _);

        await vm.RefreshGpusAsync();

        Assert.NotNull(vm.SelectedGpu);
        Assert.False(vm.GpuMemoryLimitAdviceVisible);
        Assert.Equal(string.Empty, vm.GpuMemoryLimitAdviceText);
        Assert.False(vm.GpuMemoryLimitAdviceCommand.CanExecute(null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
