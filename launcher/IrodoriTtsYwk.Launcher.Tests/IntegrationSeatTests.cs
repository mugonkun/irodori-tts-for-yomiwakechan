using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 統合席が実機で見つけた 2 つの穴を釘付けする（便 D・2026-09-05）。
/// <para>
/// どちらも <c>probe/d-launch-probe.ps1</c> の 1 回目の実射で出た＝机上の 356 本は全部緑だった。
/// </para>
/// </summary>
public sealed class GpuLabelTests
{
    private static GpuInfo Gpu(string uuid = "30303030-3031-3937-3030-303030303030", int index = 0) =>
        new(uuid, "AMD Radeon(TM) 8060S Graphics", index, 107_090_132_992L, "197", "gfx1151", null, GpuSource.TorchProbe);

    [Fact]
    public void ToStringはLabelと同じ1行を返す()
    {
        var gpu = Gpu();
        Assert.Equal(gpu.Label, gpu.ToString());
        Assert.StartsWith("0: AMD Radeon(TM) 8060S Graphics", gpu.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToStringにrecordの内部表現を漏らさない()
    {
        // UI Automation の項目名は ToString() から作られる＝ここに Uuid = … が出ると
        // 無人検分（受け入れ条件 D-7）が record の内部表現を読むことになる（実測で出た）。
        var text = Gpu().ToString();
        Assert.DoesNotContain("Uuid", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GpuInfo {", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GPU-abcdef", "abcdef", true)]
    [InlineData("ABCDEF", "abcdef", true)]
    [InlineData("abcdef", "abcdee", false)]
    [InlineData(null, "abcdef", false)]
    [InlineData("abcdef", null, false)]
    [InlineData(null, null, false)]
    [InlineData("", "abcdef", false)]
    public void SameUuidは前置と大小を吸収し空を同一視しない(string? left, string? right, bool expected) =>
        Assert.Equal(expected, GpuResolver.SameUuid(left, right));
}

/// <summary>
/// 「数え直す」で選ばれた GPU が settings.json に届くか（裁定 34・受け入れ条件 D-2）。
/// </summary>
public sealed class SettingsGpuPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-integ-" + Guid.NewGuid().ToString("N"));

    private sealed class FakeEnumerator(params GpuInfo[] gpus) : IGpuEnumerator
    {
        private readonly IReadOnlyList<GpuInfo> _gpus = gpus;

        public Task<GpuEnumerationResult> EnumerateAsync(
            GpuEnumerationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new GpuEnumerationResult(
                _gpus, GpuSource.TorchProbe, TimeSpan.FromSeconds(2), null));
    }

    private static GpuInfo Gpu(string uuid, int index) =>
        new(uuid, "GPU " + index, index, 24L * 1024 * 1024 * 1024, null, null, "580.01", GpuSource.NvidiaSmi);

    private SettingsViewModel Create(LauncherSettings settings, IGpuEnumerator enumerator, out ISettingsStore store)
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
        return new SettingsViewModel(settings, store, paths, enumerator, new DriverRequirement());
    }

    [Fact]
    public async Task 初回は数え直しただけで未保存の印が立ち適用でUUIDが残る()
    {
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130 };
        var model = Create(settings, new FakeEnumerator(Gpu("uuid-a", 0), Gpu("uuid-b", 1)), out var store);

        Assert.False(model.IsDirty);
        await model.RefreshGpusAsync();

        // 画面には 1 台目が選ばれて見える。その UUID が写しに入っていなければ、
        // 「選んだのに保存されていない」状態になる（実機で出た穴）。
        Assert.Equal("uuid-a", model.SelectedGpu?.Uuid);
        Assert.Equal("uuid-a", model.Draft.GpuUuid);
        Assert.True(model.IsDirty);
        Assert.True(model.ApplyCommand.CanExecute(null));

        model.Apply();
        Assert.False(model.IsDirty);
        Assert.Equal("uuid-a", settings.GpuUuid);
        Assert.Equal("uuid-a", store.Load().GpuUuid);
    }

    [Fact]
    public async Task 保存済みのUUIDと同じなら未保存の印は立たない()
    {
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130, GpuUuid = "GPU-uuid-b", GpuName = "GPU 1" };
        var model = Create(settings, new FakeEnumerator(Gpu("uuid-a", 0), Gpu("uuid-b", 1)), out _);

        await model.RefreshGpusAsync();

        Assert.Equal("uuid-b", model.SelectedGpu?.Uuid);
        Assert.False(model.IsDirty);
    }

    [Fact]
    public async Task 前回のGPUが消えていたら選び直したものとして印が立つ()
    {
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130, GpuUuid = "uuid-gone", GpuName = "old" };
        var model = Create(settings, new FakeEnumerator(Gpu("uuid-a", 0)), out _);

        await model.RefreshGpusAsync();

        Assert.Equal("uuid-a", model.Draft.GpuUuid);
        Assert.True(model.IsDirty);
        Assert.Contains("old", model.GpuMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 適用は状態帯へ告げる()
    {
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130 };
        var model = Create(settings, new FakeEnumerator(Gpu("uuid-a", 0)), out _);
        var status = new StatusViewModel(() => Task.FromResult(true), () => Task.CompletedTask);
        status.ApplySettings(settings);
        Assert.Equal("未選択", status.GpuText);

        // MainViewModel が張る配線と同じ形（画面どうしは互いを知らない）。
        model.Applied += (_, _) => status.ApplySettings(settings);

        await model.RefreshGpusAsync();
        Assert.Equal("未選択", status.GpuText);   // 適用するまでは動かない

        model.Apply();
        Assert.Contains("UUID", status.GpuText, StringComparison.Ordinal);
        Assert.Contains("GPU 0", status.GpuText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GPUが1台も無ければ写しは触らない()
    {
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130 };
        var model = Create(settings, new FakeEnumerator(), out _);

        await model.RefreshGpusAsync();

        Assert.Null(model.SelectedGpu);
        Assert.Null(model.Draft.GpuUuid);
        Assert.False(model.IsDirty);
    }

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
}

/// <summary>
/// 是正席が直した穴の釘（便 D・2026-09-05）。実機の逐語から起こした試験で、
/// どれも<b>純ロジックだけ</b>（子プロセスも実 GPU も実ポートも触らない）。
/// </summary>
public sealed class CorrectionSeatTests
{
    private static StatusViewModel NewStatus() =>
        new(static () => Task.CompletedTask, static () => Task.CompletedTask);

    [Fact]
    public void 失敗しても個体が残っていれば停止を押せる()
    {
        // 所見 2 の釘＝ready 待ちが期限切れになった個体は Failed に落ちるが、
        // 子プロセスは生きたままモデルを載せ続ける。IsRunning だけで判定していたころは
        // 停止も起動も押せず、逃げ道がトレイの「終了」だけになった。
        var status = NewStatus();
        status.ApplyState(ServerState.Failed, "起動が 3 秒で終わりませんでした。");
        status.HasProcess = true;

        Assert.False(status.IsRunning);
        Assert.True(status.CanStop);
        Assert.True(status.StopCommand.CanExecute(null));

        // 個体が居なくなれば押せない（何もしないボタンを押させない）
        status.HasProcess = false;
        Assert.False(status.CanStop);
        Assert.False(status.StopCommand.CanExecute(null));
    }

    [Fact]
    public void 走行中の状態帯は走っている個体を名乗る()
    {
        // 所見 6 の釘＝走行中に変種・GPU・ポートを変えて「適用」しても、
        // 状態帯が走っていないポートと選んでいない GPU を名乗らない。
        var status = NewStatus();
        var running = new LauncherSettings
        {
            Variant = RuntimeVariants.RocmGfx1151,
            Port = 18088,
            GpuName = "GPU A",
            GpuUuid = "30303030-0000-0000-0000-0000000aa1111",
        };

        status.ApplySettings(running);
        status.BeginRun(running);
        status.ApplyState(ServerState.Ready, null);
        Assert.Contains("18088", status.EndpointText, StringComparison.Ordinal);
        Assert.Contains("GPU A", status.GpuText, StringComparison.Ordinal);
        Assert.Null(status.SettingsPendingText);

        // 走行中に別の設定を適用した
        status.ApplySettings(new LauncherSettings
        {
            Variant = RuntimeVariants.Cpu,
            Port = 19000,
            GpuName = "GPU B",
            GpuUuid = "30303030-0000-0000-0000-0000000bb2222",
        });

        Assert.Contains("18088", status.EndpointText, StringComparison.Ordinal);
        Assert.Contains("GPU A", status.GpuText, StringComparison.Ordinal);
        Assert.NotNull(status.SettingsPendingText);
        Assert.Contains("次回の起動", status.SettingsPendingText!, StringComparison.Ordinal);

        // 止まれば新しい設定の姿に戻る
        status.EndRun();
        Assert.Contains("19000", status.EndpointText, StringComparison.Ordinal);
        Assert.Null(status.SettingsPendingText);
    }

    [Fact]
    public void 載ったGPUが選んだ個体と違えば1行出す()
    {
        // 所見 4 の釘＝nvidia-smi の index（PCI バス順）と torch の index
        // （既定 FASTEST_FIRST）は別の空間で、番号が同じでも別の個体を指しうる。
        var device = new StatusDevice { Actual = "cuda:0", Name = "NVIDIA GeForce RTX 3090", Uuid = "GPU-bbbb" };

        Assert.NotNull(StatusViewModel.DescribeGpuMismatch("GPU-aaaa", device));
        Assert.Null(StatusViewModel.DescribeGpuMismatch("GPU-bbbb", device));
        // 前置と大小は吸収する（nvidia-smi は GPU- 付き・torch は付かないことがある）
        Assert.Null(StatusViewModel.DescribeGpuMismatch("BBBB", device));
        // どちらかが読めなければ黙る（推測で騒がない）
        Assert.Null(StatusViewModel.DescribeGpuMismatch(null, device));
        Assert.Null(StatusViewModel.DescribeGpuMismatch("GPU-aaaa", new StatusDevice { Actual = "cuda:0" }));
    }

    [Fact]
    public void 台帳が知らない行は消せずKindも分ける()
    {
        // 所見 16 の釘＝台帳に無い話者を消しても何も起きないのに「削除しました」と告げ、
        // 行も残る（幽霊行で必ず踏む）。
        var ghost = new VoiceRow("ywk-c962284a59da", "ywk-c962284a59da", null, false, false, false, false,
            null, IsInTable: false);
        Assert.False(ghost.CanRemove);
        Assert.Equal("サーバ側", ghost.KindText);

        var known = new VoiceRow("琴葉茜", "琴葉茜", "ywk-abc.wav", false, false, false, false, null);
        Assert.True(known.CanRemove);
        Assert.Equal("利用者", known.KindText);

        // 幽霊行は焼きにも行かない
        Assert.Empty(VoiceRowBuilder.NeedsPrecompute([ghost]));
        Assert.Single(VoiceRowBuilder.NeedsPrecompute([known]));
    }

    [Fact]
    public void 一覧はサーバ側の走査で見えた行を台帳の物と区別する()
    {
        // 所見 11 の裏＝参照 wav を refs/ に逃がしても、他人が voices_dir 直下に
        // 檔を置けば走査由来の id は出る。出すのはよいが「台帳の物」と混ぜない。
        var store = new VoicesYwkFile
        {
            Voices = new Dictionary<string, VoiceEntry>(StringComparer.Ordinal)
            {
                ["琴葉茜"] = new VoiceEntry { DisplayName = "琴葉茜", File = "ywk-abc.wav" },
            },
        };

        var live = new VoicesResponse
        {
            Data = [new VoiceInfo { Id = "琴葉茜" }, new VoiceInfo { Id = "ywk-abc" }],
        };

        var rows = VoiceRowBuilder.Build(VoiceStoreDefault(store), live, null);

        var known = Assert.Single(rows, r => r.Id == "琴葉茜");
        Assert.True(known.IsInTable);

        var ghost = Assert.Single(rows, r => r.Id == "ywk-abc");
        Assert.False(ghost.IsInTable);
        Assert.False(ghost.CanRemove);
    }

    private static VoicesYwkFile VoiceStoreDefault(VoicesYwkFile file) =>
        IrodoriTtsYwk.Launcher.Services.Voices.VoiceStore.EnsureDefault(file);

    [Fact]
    public void 子に載せるenvはindexの並べ方を明示する()
    {
        // 所見 4 の釘＝2 つの index 空間（nvidia-smi の PCI バス順と torch の
        // FASTEST_FIRST）を揃える 1 本。CPU 変種には載せない。
        var paths = new AppPaths(@"C:\i", @"C:\a", @"C:\r", @"C:\d", developerMode: true);

        var gpu = ServerEnvironment.Build(
            new LauncherSettings { Variant = RuntimeVariants.Cu130 }, paths, 1);
        Assert.Equal(ServerEnvironment.PciBusIdOrder, gpu[ServerEnvironment.CudaDeviceOrder]);
        Assert.Equal("cuda:1", gpu[ServerEnvironment.ModelDevice]);
        Assert.Equal("cuda:1", gpu[ServerEnvironment.CodecDevice]);

        var cpu = ServerEnvironment.Build(
            new LauncherSettings { Variant = RuntimeVariants.Cpu }, paths, null);
        Assert.False(cpu.ContainsKey(ServerEnvironment.CudaDeviceOrder));
    }
}
