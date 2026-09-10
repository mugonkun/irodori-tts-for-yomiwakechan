using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Server;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 裁定 110（2026-09-08・司令官「Windowsの計数を読もうか。複数GPUの場合も想定して適切に対処よろしく。」
/// 「共有メモリは除外。あくまでTTSエンジンが使用しているGPUが集計対象。」）＝
/// <b>GPU メモリは Windows の計数（PDH）から読む</b>。
/// <para>
/// ここで押さえるのは<b>純関数だけ</b>＝instance 名の読み・集計・帯の綴り。実機の PDH と DXGI に
/// 触るのは <see cref="OsGpuLiveTests"/> の 1 本で、環境変数 <c>YWK_LIVE_GPU=1</c> のときだけ走る。
/// </para>
/// </summary>
public sealed class OsGpuInstanceNameTests
{
    [Fact]
    public void プロセス側のinstance名からpidとLUIDを読む()
    {
        // 実測（この機体・2026-09-08）＝'\GPU Process Memory(*)\Dedicated Usage' の instance 名
        var parsed = OsGpuMemory.ParseInstance("pid_31556_luid_0x00000000_0x000137d0_phys_0");

        Assert.NotNull(parsed);
        Assert.Equal(31556, parsed!.Pid);
        Assert.Equal("luid_0x00000000_0x000137d0", parsed.Luid);
        Assert.Equal(0, parsed.Phys);
        Assert.Null(parsed.Part);
    }

    [Fact]
    public void アダプタ側のinstance名は区画まで読む()
    {
        // 'GPU Adapter Memory' は区画なし・'GPU Local Adapter Memory' は _part_N つき
        var adapter = OsGpuMemory.ParseInstance("luid_0x00000000_0x000137d0_phys_0");
        Assert.NotNull(adapter);
        Assert.Null(adapter!.Pid);
        Assert.Equal("luid_0x00000000_0x000137d0", adapter.Luid);
        Assert.Equal(0, adapter.Phys);
        Assert.Null(adapter.Part);

        var local = OsGpuMemory.ParseInstance("luid_0x00000000_0x000137d0_phys_0_part_1");
        Assert.NotNull(local);
        Assert.Null(local!.Pid);
        Assert.Equal("luid_0x00000000_0x000137d0", local.Luid);
        Assert.Equal(0, local.Phys);
        Assert.Equal(1, local.Part);
    }

    [Fact]
    public void 十六進の大小はどちらでも読めて突合は大小を無視する()
    {
        var upper = OsGpuMemory.ParseInstance("pid_7_luid_0x00000000_0x000137D0_phys_0");
        var lower = OsGpuMemory.ParseInstance("luid_0x00000000_0x000137d0_phys_0");

        Assert.NotNull(upper);
        Assert.NotNull(lower);
        Assert.Equal("luid_0x00000000_0x000137D0", upper!.Luid);   // 綴りはそのまま残す
        Assert.NotEqual(upper.Luid, lower!.Luid);                  // 綴りとしては違う
        Assert.Equal(upper.Luid, lower.Luid, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Total")]
    [InlineData("_Total")]
    [InlineData("pid_luid_0x00000000_0x000137d0_phys_0")]     // pid の桁が無い
    [InlineData("pid_abc_luid_0x00000000_0x000137d0")]        // pid が 10 進でない
    [InlineData("luid_0x00000000")]                           // LUID が片方しか無い
    [InlineData("luid_0xzzzzzzzz_0x000137d0_phys_0")]         // 16 進でない
    [InlineData("luid_0x00000000_0x000137d0_phys_x")]         // phys が数でない
    [InlineData("luid_0x00000000_0x000137d0_part_x")]         // part が数でない
    public void 読めない綴りはnullを返す(string? name) =>
        Assert.Null(OsGpuMemory.ParseInstance(name));

    [Fact]
    public void DXGIのLUIDをPDHの綴りに直す()
    {
        // 実測＝この機体の 8060S は LowPart 0x000137d0・HighPart 0
        Assert.Equal("luid_0x00000000_0x000137d0", OsGpuMemory.LuidToken(0, 0x000137d0));

        // HighPart は符号つき（LONG）＝負でも 32 bit の 16 進 8 桁で綴る
        Assert.Equal("luid_0xffffffff_0x00000abc", OsGpuMemory.LuidToken(-1, 0x00000abc));
        Assert.Equal("luid_0x80000000_0x00000001", OsGpuMemory.LuidToken(int.MinValue, 1));
    }
}

/// <summary>裁定 110 D3＝集計の相手は<b>その pid が触っている GPU だけ</b>。</summary>
public sealed class OsGpuAggregationTests
{
    private const string LuidA = "luid_0x00000000_0x000137d0";
    private const string LuidB = "luid_0x00000000_0x00015a49";

    private static GpuCounterInstance Process(int pid, string luid, long bytes, int phys = 0) =>
        new(luid.Replace("luid_", "pid_" + pid.ToString(CultureInfo.InvariantCulture) + "_luid_",
                StringComparison.Ordinal)
            + "_phys_" + phys.ToString(CultureInfo.InvariantCulture), bytes);

    private static GpuCounterInstance Adapter(string luid, long bytes, int phys = 0) =>
        new(luid + "_phys_" + phys.ToString(CultureInfo.InvariantCulture), bytes);

    private static GpuCounterInstance Local(string luid, long bytes, int part) =>
        new(luid + "_phys_0_part_" + part.ToString(CultureInfo.InvariantCulture), bytes);

    [Fact]
    public void 二枚のGPUのうちpidが触っている1枚だけを出す()
    {
        // 3090 と iGPU が居て、python は 3090 しか触っていない機体
        var rows = OsGpuMemory.Aggregate(
            31556,
            [Process(31556, LuidA, 11_634_429_952), Process(2968, LuidB, 868_675_584)],
            [Adapter(LuidA, 31_983_185_920), Adapter(LuidB, 1_000_000)],
            [],
            [new GpuAdapterInfo(LuidA, "AMD Radeon(TM) 8060S Graphics", 63_805_628_416),
             new GpuAdapterInfo(LuidB, "iGPU", 1_073_741_824)]);

        var row = Assert.Single(rows);
        Assert.Equal(LuidA, row.Luid);
        Assert.Equal("AMD Radeon(TM) 8060S Graphics", row.Name);
        Assert.Equal(11_634_429_952L, row.ProcessBytes);
        Assert.Equal(31_983_185_920L, row.AdapterBytes);
        Assert.Equal(63_805_628_416L, row.TotalBytes);
    }

    [Fact]
    public void 二枚に載っていれば2行出て多い順に並ぶ()
    {
        // 模型を cuda:0・codec を cuda:1 に載せた個体（司令官の指示 1＝複数 GPU）
        var rows = OsGpuMemory.Aggregate(
            100,
            [Process(100, LuidA, 1_000), Process(100, LuidB, 2_000)],
            [Adapter(LuidA, 3_000), Adapter(LuidB, 4_000)],
            [],
            [new GpuAdapterInfo(LuidA, "GPU A", 8_000), new GpuAdapterInfo(LuidB, "GPU B", 9_000)]);

        Assert.Equal(2, rows.Count);
        Assert.Equal("GPU B", rows[0].Name);       // 2,000 が先
        Assert.Equal("GPU A", rows[1].Name);
    }

    [Fact]
    public void 同じLUIDの複数instanceは足す()
    {
        var rows = OsGpuMemory.Aggregate(
            100,
            [Process(100, LuidA, 1_000), Process(100, LuidA, 500, phys: 1)],
            [Adapter(LuidA, 3_000), Adapter(LuidA, 1_000, phys: 1)],
            [],
            []);

        var row = Assert.Single(rows);
        Assert.Equal(1_500L, row.ProcessBytes);
        Assert.Equal(4_000L, row.AdapterBytes);
    }

    [Fact]
    public void アダプタ側にinstanceが無いLUIDだけローカルの区画を足して使う()
    {
        // 'GPU Adapter Memory' に居ない LUID の控え＝'GPU Local Adapter Memory' の _part_N
        var rows = OsGpuMemory.Aggregate(
            100,
            [Process(100, LuidA, 1_000), Process(100, LuidB, 900)],
            [Adapter(LuidA, 3_000)],
            [Local(LuidA, 999_999, 0), Local(LuidB, 2_000, 0), Local(LuidB, 500, 1)],
            []);

        Assert.Equal(2, rows.Count);
        Assert.Equal(3_000L, rows[0].AdapterBytes);   // A は Dedicated が正（控えは使わない）
        Assert.Equal(2_500L, rows[1].AdapterBytes);   // B は区画を足した控え
    }

    [Fact]
    public void アダプタ側のinstanceが0なら控えへ落ちる()
    {
        // 是正（2026-09-08）＝**この機体で実際に居る形**＝'GPU Adapter Memory' に instance は
        // 在るのに値が 0 の LUID（実測＝luid_…_0x00015A49_phys_0 = 0 のとき
        // 'GPU Local Adapter Memory' は 2,583,031,808）。instance の**有無**だけで控えに
        // 落としていたので、帯が「GPU 全体 0 B」と綴っていた。
        var rows = OsGpuMemory.Aggregate(
            100,
            [Process(100, LuidB, 2_000_000_000)],
            [Adapter(LuidB, 0)],
            [Local(LuidB, 2_583_031_808, 0)],
            []);

        Assert.Equal(2_583_031_808L, Assert.Single(rows).AdapterBytes);
    }

    [Fact]
    public void 自分より小さいGPU全体は出さない()
    {
        // 控えも 0（＝どちらの計数も答えていない）＝**0 B ではなく null**（画面は「—」）。
        var none = OsGpuMemory.Aggregate(
            100, [Process(100, LuidB, 2_000_000_000)], [Adapter(LuidB, 0)], [Local(LuidB, 0, 0)], []);
        Assert.Null(Assert.Single(none).AdapterBytes);

        // 「全体」が自分のぶんより小さい＝帯が自分自身と矛盾する＝読めなかった物として畳む。
        var smaller = OsGpuMemory.Aggregate(
            100, [Process(100, LuidA, 2_000)], [Adapter(LuidA, 1_000)], [], []);
        Assert.Null(Assert.Single(smaller).AdapterBytes);
    }

    [Fact]
    public void DXGIが読めなければ名前はLUIDの綴り総量はnull()
    {
        var rows = OsGpuMemory.Aggregate(
            100, [Process(100, LuidA, 1_000)], [Adapter(LuidA, 3_000)], [], []);

        var row = Assert.Single(rows);
        Assert.Equal(LuidA, row.Name);
        Assert.Null(row.TotalBytes);
        Assert.Equal(3_000L, row.AdapterBytes);
    }

    [Fact]
    public void アダプタの計数が無ければGPU全体はnull()
    {
        var rows = OsGpuMemory.Aggregate(
            100, [Process(100, LuidA, 1_000)], [], [],
            [new GpuAdapterInfo(LuidA, "GPU A", 8_000)]);

        Assert.Null(Assert.Single(rows).AdapterBytes);
    }

    [Fact]
    public void pidが無いか触っていなければ空()
    {
        // 止まっている（pid null）・別人の pid しか居ない・値が 0
        Assert.Empty(OsGpuMemory.Aggregate(null, [Process(100, LuidA, 1_000)], [], [], []));
        Assert.Empty(OsGpuMemory.Aggregate(100, [Process(999, LuidA, 1_000)], [], [], []));
        Assert.Empty(OsGpuMemory.Aggregate(100, [Process(100, LuidA, 0)], [], [], []));
        Assert.Empty(OsGpuMemory.Aggregate(100, [], [], [], []));
        Assert.Empty(OsGpuMemory.Aggregate(100, null, null, null, null));
    }

    [Fact]
    public void 見張りの標本が採れなければ行は出ない()
    {
        // 計数が読めない機体＝OsGpuMemorySampler は空を返し、例外も投げない
        // （口 2 つの側を名指しする＝遅れ開きのコンストラクタと取り違えない）
        using var sampler = new OsGpuMemorySampler((IGpuMemoryCounters?)null, null);
        Assert.Empty(sampler.Sample(31556));

        var lines = new List<string>();
        using var broken = new OsGpuMemorySampler(new BrokenCounters(), null, lines.Add);
        Assert.Empty(broken.Sample(31556));
        Assert.Empty(broken.Sample(31556));
        Assert.Single(lines);            // 理由 1 行は**プロセスにつき 1 度**（2 秒ごとに書かない）
    }

    [Fact]
    public void collectが失敗し続けても理由は1行だけ出る()
    {
        // 是正（2026-09-08）＝counter の組は在るのに collect が status で失敗する機体
        // （PDH_NO_DATA＝perflib が壊れている・切ってある）。前は行が消えるだけで**ログが無音**、
        // 「pid が GPU を触っていない」と見分けが付かなかった（畳み方の決め D5 は「理由 1 行」）。
        var lines = new List<string>();
        using var sampler = new OsGpuMemorySampler(
            new SilentCounters("GPU の計数を採れませんでした（PdhCollectQueryData 0x800007d5）。"),
            null, lines.Add);

        Assert.Empty(sampler.Sample(31556));
        Assert.Empty(sampler.Sample(31556));
        Assert.Empty(sampler.Sample(31556));
        Assert.Contains("0x800007d5", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void 実機の口を開くのは最初の標本の回で1度だけ()
    {
        // 是正（2026-09-08・high）＝開設は実測 0.23 s 掛かる。作った回（＝App.OnStartup＝UI の糸）
        // ではなく**最初に数えた回**（＝見張りの糸）で開く。開けた口は使い回す。
        var opened = 0;
        var lines = new List<string>();
        using var sampler = new OsGpuMemorySampler(
            () =>
            {
                opened++;
                return new OsGpuMemorySampler.OpenedSources(
                    new FakeCounters(new GpuCounterSample(
                        [Process(31556, LuidA, 11_634_429_952)], [Adapter(LuidA, 31_983_185_920)], [])),
                    null,
                    null);
            },
            lines.Add);

        Assert.Equal(0, opened);                      // **作っただけでは開かない**
        Assert.Empty(sampler.Sample(null));           // 起こしていない回も開かない
        Assert.Equal(0, opened);

        Assert.Single(sampler.Sample(31556));
        Assert.Single(sampler.Sample(31556));
        Assert.Equal(1, opened);                      // 開くのは 1 度だけ（query は持ちっぱなし）
        Assert.Empty(lines);
    }

    [Fact]
    public void 開けなかった理由は最初の標本の回に1行だけ出る()
    {
        // 開設の理由もログ帯へ 1 度だけ（見張りは 2 秒ごとに来る＝毎回書かない）。
        var lines = new List<string>();
        using var sampler = new OsGpuMemorySampler(
            static () => new OsGpuMemorySampler.OpenedSources(
                null, null, "pdh.dll が読めませんでした（試験）。"),
            lines.Add);

        Assert.Empty(sampler.Sample(31556));
        Assert.Empty(sampler.Sample(31556));
        Assert.Contains("pdh.dll", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void 標本が採れれば行になる()
    {
        var counters = new FakeCounters(new GpuCounterSample(
            [Process(31556, LuidA, 11_634_429_952)],
            [Adapter(LuidA, 31_983_185_920)],
            []));
        using var sampler = new OsGpuMemorySampler(
            counters, new FakeAdapters([new GpuAdapterInfo(LuidA, "AMD Radeon(TM) 8060S Graphics", 63_805_628_416)]));

        var row = Assert.Single(sampler.Sample(31556));
        Assert.Equal("AMD Radeon(TM) 8060S Graphics", row.Name);
        Assert.Equal(11_634_429_952L, row.ProcessBytes);
        Assert.Empty(sampler.Sample(null));
    }

    private sealed class FakeCounters(GpuCounterSample sample) : IGpuMemoryCounters
    {
        public GpuCounterSample? Sample(out string? failureReason)
        {
            failureReason = null;
            return sample;
        }

        public void Dispose()
        {
        }
    }

    private sealed class BrokenCounters : IGpuMemoryCounters
    {
        public GpuCounterSample? Sample(out string? failureReason) =>
            throw new InvalidOperationException("計数が壊れました。");

        public void Dispose()
        {
        }
    }

    /// <summary>投げずに「採れなかった」と言う口（<c>PdhCollectQueryData</c> が status で失敗する形）。</summary>
    private sealed class SilentCounters(string reason) : IGpuMemoryCounters
    {
        public GpuCounterSample? Sample(out string? failureReason)
        {
            failureReason = reason;
            return null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeAdapters(IReadOnlyList<GpuAdapterInfo> adapters) : IGpuAdapterInfoSource
    {
        public IReadOnlyList<GpuAdapterInfo> Adapters() => adapters;
    }
}

/// <summary>裁定 110 D4＝状態帯の綴り（torch の 2 つと、OS の組）。</summary>
public sealed class OsGpuBandTests
{
    private const string LuidA = "luid_0x00000000_0x000137d0";
    private const string LuidB = "luid_0x00000000_0x00015a49";

    private static MemoryStatus Radeon() => new()
    {
        Device = "cuda:0",
        AllocatedBytes = 2_223_570_944,      // 2.07 GiB
        ReservedBytes = 2_835_349_504,       // 2.64 GiB
        MaxAllocatedBytes = 4_907_167_744,   // 4.57 GiB
        GpuTotalBytes = 107_090_132_992,     // torch/HIP の値（もう帯には出ない）
        GpuUsedBytes = 3_929_288_704,
    };

    [Fact]
    public void GPUが1枚なら名前は行末の括弧に出る()
    {
        // 司令官に見せた形（実射の数字・2026-09-08）
        var text = StatusViewModel.DescribeMemory(Radeon(), true,
            [new OsGpuMemoryRow(LuidA, "AMD Radeon(TM) 8060S Graphics",
                11_634_429_952, 32_283_066_368, 63_805_628_416)]);

        Assert.Equal(
            "いま使っている量 2.07 GiB（最大 4.57 GiB）／確保している量 2.64 GiB"
            + "／このプロセス 10.84 GiB／GPU 全体 30.07 GiB / 59.42 GiB（AMD Radeon(TM) 8060S Graphics）",
            text);
    }

    [Fact]
    public void wrapperのgpu_usedとgpu_totalはもう帯に出さない()
    {
        // 裁定 110＝torch/HIP の値は「カード全体」ではない（契約 ⑹ の JSON は据え置き）
        var text = StatusViewModel.DescribeMemory(Radeon(), true,
            [new OsGpuMemoryRow(LuidA, "GPU", 1_073_741_824, 2_147_483_648, 4_294_967_296)]);

        Assert.DoesNotContain("99.74 GiB", text, StringComparison.Ordinal);
        Assert.DoesNotContain("3.66 GiB", text, StringComparison.Ordinal);
        Assert.StartsWith("いま使っている量 ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GPUが2枚なら組ごとに名前を頭に立てる()
    {
        var text = StatusViewModel.DescribeMemory(Radeon(), true,
        [
            new OsGpuMemoryRow(LuidA, "GPU A", 2_147_483_648, 4_294_967_296, 8_589_934_592),
            new OsGpuMemoryRow(LuidB, "GPU B", 1_073_741_824, 2_147_483_648, 4_294_967_296),
        ]);

        Assert.Equal(
            "いま使っている量 2.07 GiB（最大 4.57 GiB）／確保している量 2.64 GiB"
            + "／GPU A＝このプロセス 2.00 GiB／GPU 全体 4.00 GiB / 8.00 GiB"
            + "／GPU B＝このプロセス 1.00 GiB／GPU 全体 2.00 GiB / 4.00 GiB",
            text);
    }

    [Fact]
    public void 同じ名前のGPUが2枚並んだらLUIDを添える()
    {
        // 是正（2026-09-08）＝この機体の DXGI は同じ名前・同じ総量のアダプタを 4 つ名乗る。
        // python が 2 つ目の LUID にも載ると、名前だけの見出しでは**どちらがどれか読めない**。
        var text = StatusViewModel.DescribeMemory(Radeon(), true,
        [
            new OsGpuMemoryRow(LuidA, "AMD Radeon(TM) 8060S Graphics",
                2_147_483_648, 4_294_967_296, 8_589_934_592),
            new OsGpuMemoryRow(LuidB, "AMD Radeon(TM) 8060S Graphics",
                1_073_741_824, 2_147_483_648, 4_294_967_296),
        ]);

        Assert.Contains(
            "／AMD Radeon(TM) 8060S Graphics（0x000137d0）＝このプロセス 2.00 GiB",
            text, StringComparison.Ordinal);
        Assert.Contains(
            "／AMD Radeon(TM) 8060S Graphics（0x00015a49）＝このプロセス 1.00 GiB",
            text, StringComparison.Ordinal);
    }

    [Fact]
    public void 読めない総量は0ではなく棒で出す()
    {
        var text = StatusViewModel.DescribeMemory(Radeon(), true,
            [new OsGpuMemoryRow(LuidA, LuidA, 1_073_741_824, null, null)]);

        Assert.Contains("GPU 全体 " + UiText.Missing + " / " + UiText.Missing, text, StringComparison.Ordinal);
        Assert.Contains("（" + LuidA + "）", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0 B", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 計数が読めなければtorchの2つだけを出す()
    {
        var text = StatusViewModel.DescribeMemory(Radeon(), true, []);

        Assert.Equal("いま使っている量 2.07 GiB（最大 4.57 GiB）／確保している量 2.64 GiB", text);
        Assert.DoesNotContain("このプロセス", text, StringComparison.Ordinal);

        // 組そのものの純関数も、空と null では何も綴らない
        Assert.Equal(string.Empty, StatusViewModel.DescribeOsGpuRows(null));
        Assert.Equal(string.Empty, StatusViewModel.DescribeOsGpuRows([]));
    }

    [Fact]
    public void モデル未読込でも計数が読めていればOSの組は出る()
    {
        var text = StatusViewModel.DescribeMemory(new MemoryStatus(), true,
            [new OsGpuMemoryRow(LuidA, "GPU A", 1_073_741_824, 2_147_483_648, 4_294_967_296)]);

        Assert.StartsWith(UiStrings.StatusModelNotLoaded + "／このプロセス 1.00 GiB",
            text, StringComparison.Ordinal);
    }

    [Fact]
    public void 止まっているときと未対応の文言は据え置き()
    {
        Assert.Equal(UiText.NotRunning, StatusViewModel.DescribeMemory(null, false, []));
        Assert.Equal(UiText.NotSupported, StatusViewModel.DescribeMemory(null, true, []));
        Assert.Equal("CPU（GPU メモリなし）",
            StatusViewModel.DescribeMemory(new MemoryStatus { Device = "cpu" }, true, []));
    }

    [Fact]
    public void 見張りの標本と一緒に帯へ届く()
    {
        var vm = new StatusViewModel(static () => Task.CompletedTask, static () => Task.CompletedTask);
        vm.ApplySettings(new LauncherSettings());

        vm.ApplyStatus(
            new StatusResponse { Pid = 31556, Memory = Radeon() },
            [new OsGpuMemoryRow(LuidA, "GPU A", 1_073_741_824, 2_147_483_648, 4_294_967_296)]);
        Assert.Contains("このプロセス 1.00 GiB", vm.MemoryText, StringComparison.Ordinal);

        // 止まったら OS の組も消える（死んだ個体の数字を残さない）
        vm.ApplyState(ServerState.Stopped, null);
        Assert.Equal(UiText.NotRunning, vm.MemoryText);
    }
}

/// <summary>
/// 裁定 110 D5＝<b>見張りの配線</b>（<c>ServerProcess</c>）を釘付けする。
/// <para>
/// 是正（2026-09-08）＝新設の 33 本は<b>純関数と係だけ</b>を通っていて、
/// 「<c>StatusSampled</c> の回にはもう新しい行が置かれている」「止まったら空へ戻る」という
/// <b>継ぎ目そのもの</b>が無検証だった。テスト用のコンストラクタに口を足して押さえる
/// （実機の PDH も DXGI も 1 度も開かない＝偽の計数を差す）。
/// </para>
/// </summary>
public sealed class OsGpuWatcherWiringTests
{
    private const string LuidA = "luid_0x00000000_0x000137d0";

    [Fact]
    public async Task 見張りは標本を配る前にOSの計数を置き止まったら空へ戻す()
    {
        ServerProcess? watcher = null;
        var sampler = new OsGpuMemorySampler(
            new PidEchoCounters(() => watcher?.ProcessId),
            new FixedAdapters([new GpuAdapterInfo(LuidA, "GPU A", 8_589_934_592)]));

        await using var server = new ServerProcess(
            new FreePort(), new ReadyStatus(), TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(50), null, _ => LiveChild(3), sampler);
        watcher = server;

        // 見張りが標本を配る**その回に**読んだ物を控える（窓と同じ読み方＝low 3）。
        var seen = new List<IReadOnlyList<OsGpuMemoryRow>>();
        server.StatusSampled += (_, _) => seen.Add(server.LatestOsGpuMemory);

        Assert.Empty(server.LatestOsGpuMemory);   // 起こす前は空

        var result = await server.StartAsync(Request(18097), CancellationToken.None);
        Assert.True(result.Ok);
        Assert.NotEmpty(seen);

        // **StatusSampled の回にはもう新しい行が置かれている**（1 巡遅れない）。
        var row = Assert.Single(seen[^1]);
        Assert.Equal("GPU A", row.Name);
        Assert.Equal(11_634_429_952L, row.ProcessBytes);
        // 行が出たこと自体が「数えた相手は**自分の子の pid**」の証（偽の計数は子の pid で
        // instance 名を組む＝別の pid で数えていれば Aggregate が落として空になる）。
        Assert.NotNull(server.ProcessId);
        Assert.Equal(LuidA, row.Luid);

        await server.StopAsync(CancellationToken.None);
        Assert.Empty(server.LatestOsGpuMemory);   // 死んだ個体の数字を残さない
    }

    /// <summary>子の pid でそのまま instance 名を組む偽の計数（相手が合わなければ行は出ない）。</summary>
    private sealed class PidEchoCounters(Func<int?> pid) : IGpuMemoryCounters
    {
        public GpuCounterSample? Sample(out string? failureReason)
        {
            failureReason = null;
            var mine = pid() ?? 0;
            return new GpuCounterSample(
                [new GpuCounterInstance(
                    "pid_" + mine.ToString(CultureInfo.InvariantCulture)
                    + "_" + LuidA + "_phys_0", 11_634_429_952)],
                [new GpuCounterInstance(LuidA + "_phys_0", 31_983_185_920)],
                []);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FixedAdapters(IReadOnlyList<GpuAdapterInfo> adapters) : IGpuAdapterInfoSource
    {
        public IReadOnlyList<GpuAdapterInfo> Adapters() => adapters;
    }

    private sealed class FreePort : IPortProbe
    {
        public bool IsFree(string host, int port) => true;
    }

    /// <summary>載っている個体の応答（<c>pid</c> は載せない＝別人と読まれない古い形）。</summary>
    private sealed class ReadyStatus : IReadinessProbe
    {
        public Task<ReadinessSample> ProbeAsync(Uri baseAddress, CancellationToken cancellationToken)
        {
            var status = new StatusResponse
            {
                Engine = "irodori-ywk",
                Runtime = new StatusRuntime { Loaded = true, Loading = false },
                Memory = new MemoryStatus { Device = "cuda:0", AllocatedBytes = 1 },
            };

            return Task.FromResult(new ReadinessSample(true, true, false, status, null));
        }
    }

    private static ServerStartRequest Request(int port) =>
        new(
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            System.IO.Path.GetTempPath(),
            "127.0.0.1",
            port,
            new Dictionary<string, string>(StringComparer.Ordinal),
            TimeSpan.FromSeconds(2))
        {
            Variant = RuntimeVariants.Cpu,
        };

    /// <summary>数秒だけ生きている偽の子（<c>ping</c> の宛先は <c>127.0.0.1</c>＝外へ出ない）。</summary>
    private static ProcessStartInfo LiveChild(int seconds)
    {
        var info = new ProcessStartInfo
        {
            FileName = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            WorkingDirectory = System.IO.Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        info.ArgumentList.Add("/c");
        info.ArgumentList.Add(
            "1>&2 echo ywk_server 0.1.0 upstream=8224daf/841fb7c & ping -n "
            + (seconds + 1).ToString(CultureInfo.InvariantCulture) + " 127.0.0.1 >nul");
        return info;
    }
}

/// <summary>
/// 環境変数 <c>YWK_LIVE_GPU=1</c> のときだけ走る <see cref="FactAttribute"/>（裁定 110 D7）。
/// <para>
/// <b>第三者パッケージを増やさない</b>（台帳は .NET と NAudio だけ）＝<c>SkippableFact</c> は使わず、
/// 属性のコンストラクタで <see cref="FactAttribute.Skip"/> を立てる。こうすると環境変数の無い回は
/// <b>理由つきの「スキップ」として数えられる</b>（前は「何も検めない合格」で、この 1 本が壊れても
/// 合格数が変わらなかった）。
/// </para>
/// </summary>
public sealed class LiveGpuFactAttribute : FactAttribute
{
    /// <summary>門＝<c>YWK_LIVE_GPU</c>。</summary>
    public const string Gate = "YWK_LIVE_GPU";

    /// <summary>環境変数が <c>1</c> でなければ理由つきで飛ばす。</summary>
    public LiveGpuFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(Gate), "1", StringComparison.Ordinal))
        {
            Skip = Gate + "=1 のときだけ実機の PDH と DXGI を叩きます（既定では走らせない）。";
        }
    }
}

/// <summary>
/// <b>実機の PDH と DXGI を叩く 1 本</b>（裁定 110 D7）＝環境変数 <c>YWK_LIVE_GPU=1</c> の
/// ときだけ走る（<see cref="LiveGpuFactAttribute"/>＝無い回は<b>スキップ 1</b>と数えられる）。
/// 相手の pid は <c>YWK_LIVE_GPU_PID</c>、無ければ計数に居る python の中で
/// 専用メモリが一番多い個体。
/// </summary>
public sealed class OsGpuLiveTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const string PidGate = "YWK_LIVE_GPU_PID";

    [LiveGpuFact]
    public void 実機の計数から状態帯の行が出る()
    {
        // **開設の代金も測る**（是正・2026-09-08＝これが 0.23 s で、UI の糸に置いてはいけない理由）。
        var opening = Stopwatch.GetTimestamp();
        using var counters = PdhGpuMemoryCounters.TryOpen(out var reason);
        var openCost = Stopwatch.GetElapsedTime(opening).TotalMilliseconds;
        output.WriteLine("PdhOpenQuery: " + (counters is null ? "失敗＝" + reason : "開けました")
            + "（開設の代金 " + openCost.ToString("F1", CultureInfo.InvariantCulture) + " ms）");
        Assert.NotNull(counters);

        var adapters = new DxgiGpuAdapters().Adapters();
        foreach (var adapter in adapters)
        {
            output.WriteLine("[dxgi] " + adapter.Luid + " " + adapter.Name
                + " DedicatedVideoMemory=" + adapter.DedicatedVideoMemoryBytes
                .ToString(CultureInfo.InvariantCulture)
                + " B（" + UiText.Bytes(adapter.DedicatedVideoMemoryBytes) + "）");
        }

        var sample = counters!.Sample(out var sampleReason);
        Assert.True(sample is not null, sampleReason);
        var taken = sample!;

        // PDH の 1 標本の代金＝**2 通り測る**（是正・2026-09-08）。
        // ⑴ 連続撃ち＝PDH は直前の標本を返すので 2 回目以降はほぼ 0（実費ではない）
        // ⑵ **見張りと同じ 2 秒の間を空けた**回＝これが実費（それでも 5 ms の物差しの内側）
        var backToBack = new List<double>(5);
        for (var i = 0; i < 5; i++)
        {
            var started = Stopwatch.GetTimestamp();
            counters.Sample(out _);
            backToBack.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        output.WriteLine("[cost] PDH 1 標本（連続撃ち） = " + Milliseconds(backToBack) + " ms");

        var spaced = new List<double>(3);
        for (var i = 0; i < 3; i++)
        {
            System.Threading.Thread.Sleep(2000);   // 見張りの周期（WatchInterval）
            var started = Stopwatch.GetTimestamp();
            counters.Sample(out _);
            spaced.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        output.WriteLine("[cost] PDH 1 標本（2 s 間隔＝見張りと同じ） = " + Milliseconds(spaced) + " ms");

        var pid = ResolvePid(taken);
        output.WriteLine("[pid] " + (pid?.ToString(CultureInfo.InvariantCulture) ?? "見つかりません"));
        Assert.NotNull(pid);

        var rows = OsGpuMemory.Aggregate(
            pid, taken.Processes, taken.Adapters, taken.LocalAdapters, adapters);
        foreach (var row in rows)
        {
            output.WriteLine("[row] " + row.Name + " luid=" + row.Luid
                + " このプロセス=" + UiText.Bytes(row.ProcessBytes)
                + "（" + row.ProcessBytes.ToString(CultureInfo.InvariantCulture) + " B）"
                + " GPU 全体=" + UiText.Bytes(row.AdapterBytes)
                + " / " + UiText.Bytes(row.TotalBytes));
        }

        // アダプタ側の生の instance（**値が 0 の instance が居る**ことの実証＝控えへ落ちる筋）
        foreach (var instance in taken.Adapters)
        {
            output.WriteLine("[adapter] " + instance.InstanceName + " = "
                + instance.Bytes.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var instance in taken.LocalAdapters)
        {
            output.WriteLine("[local] " + instance.InstanceName + " = "
                + instance.Bytes.ToString(CultureInfo.InvariantCulture));
        }

        // **torch の 3 数は固定値**＝ここは綴りの見本であって、実機の /ywk/status の標本ではない。
        output.WriteLine("[band 綴りの見本＝torch の 3 数は固定値] " + StatusViewModel.DescribeMemory(
            new MemoryStatus
            {
                Device = "cuda:0",
                AllocatedBytes = 2_223_570_944,
                ReservedBytes = 2_835_349_504,
                MaxAllocatedBytes = 4_907_167_744,
            },
            true,
            rows));

        Assert.NotEmpty(rows);
        Assert.Contains(adapters, static adapter => adapter.DedicatedVideoMemoryBytes > 0);
    }

    /// <summary>測った ms を逐語で並べる（丸めない）。</summary>
    private static string Milliseconds(List<double> samples) =>
        string.Join(" / ", samples.ConvertAll(
            static ms => ms.ToString("F2", CultureInfo.InvariantCulture)));

    /// <summary>相手の pid＝環境変数、無ければ計数に居る python で一番多い個体。</summary>
    private int? ResolvePid(GpuCounterSample sample)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable(PidGate), NumberStyles.None,
                CultureInfo.InvariantCulture, out var wanted))
        {
            return wanted;
        }

        int? best = null;
        var bestBytes = 0L;
        foreach (var instance in sample.Processes)
        {
            if (instance.Bytes <= bestBytes
                || OsGpuMemory.ParseInstance(instance.InstanceName) is not { Pid: int pid })
            {
                continue;
            }

            string? name;
            try
            {
                using var process = Process.GetProcessById(pid);
                name = process.ProcessName;
            }
            catch (ArgumentException)
            {
                continue;   // もう居ない
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (name is null || !name.Contains("python", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            best = pid;
            bestBytes = instance.Bytes;
        }

        return best;
    }
}
