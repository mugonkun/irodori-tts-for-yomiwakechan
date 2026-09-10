using System;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Server;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// v2.0 <b>段 G</b>（版を切る前の最後の検分）で当て込んだ所見の釘（2026-09-11）。
/// <para>
/// 並びは所見の番号＝high 1（帯の E1）・high 7（はじめの準備が済んでいない機体）・
/// medium 2／9（連携の 1 行）・medium 11（自動で起こさない機体の導線）・
/// high 8／medium 12（版を間違えて入れた機体）。
/// </para>
/// </summary>
public sealed class StageGBandTests
{
    [Fact]
    public void 台帳が動いた回は帯に1行と1手が出る()
    {
        // 憲章 §4-24＝v2.0 の必須要件。E1 は**緑のまま**理由と 1 手を持つ（E 群は赤にしない）。
        var line = BandText.For(
            ServerState.Ready,
            null,
            new BandContext(RebuildRuntimeLine: "動かすための一式が、いまのアプリと合っていません。"));

        Assert.Equal(BandSeverity.Ok, line.Severity);
        Assert.Equal(BandText.Ready, line.Headline);
        Assert.Equal("動かすための一式が、いまのアプリと合っていません。", line.Reason);
        Assert.Equal(UiStrings.StatusRebuildButton, line.ActionLabel);
        Assert.Equal(BandActionKind.RebuildRuntime, line.Action);
    }

    [Fact]
    public void 台帳が動いていない回の帯は今までどおり無言()
    {
        var line = BandText.For(ServerState.Ready, null, new BandContext());

        Assert.Equal(BandText.Ready, line.Headline);
        Assert.Null(line.Reason);
        Assert.Equal(BandActionKind.None, line.Action);
    }

    [Fact]
    public void 声を先に用意している間もE1は出る()
    {
        var line = BandText.For(
            ServerState.Warming, null, new BandContext(RebuildRuntimeLine: "合っていません。"));

        Assert.Equal(BandSeverity.Ok, line.Severity);
        Assert.Equal(BandText.ReadyWarming, line.Headline);
        Assert.Equal(BandActionKind.RebuildRuntime, line.Action);
    }

    [Fact]
    public void はじめの準備が済んでいない機体は行き止まりにならない()
    {
        // ウィザードを閉じた回＝誰も起こさないので Stopped のまま止まる。
        // ここが無かったころは「準備しています…」＋釦なしだった（high 7）。
        var line = BandText.For(ServerState.Stopped, null, new BandContext(FirstRunPending: true));

        Assert.Equal(BandSeverity.Neutral, line.Severity);
        Assert.Equal(BandText.NotPreparedYet, line.Headline);
        Assert.Equal(BandText.NotPreparedYetWhy, line.Reason);
        Assert.Equal(UiStrings.StatusAcquireButton, line.ActionLabel);
        Assert.Equal(BandActionKind.FirstRun, line.Action);
    }

    [Fact]
    public void 準備が済んでいる機体の止まりは今までどおり()
    {
        var line = BandText.For(ServerState.Stopped, null, new BandContext(UserStopped: true));

        Assert.Equal(BandText.StoppedByUser, line.Headline);
        Assert.Equal(BandActionKind.Start, line.Action);
    }

    [Fact]
    public void 自動で起こさない機体の導線は札そのものを指す()
    {
        // medium 11＝印は ふだんの設定（畳みの外）に在る。詳細 › 読み上げの動作 には
        // 3 釦しか無いので、そこを指すとこの 1 行が在る理由が丸ごと消える。
        Assert.Contains(UiStrings.SettingsAutoStart, BandText.AutoStartOffHint, StringComparison.Ordinal);
        Assert.DoesNotContain("読み上げの動作", BandText.AutoStartOffHint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, false, BandText.HostAvailable)]
    [InlineData(true, false, false, BandText.HostIdle)]
    [InlineData(true, true, false, BandText.HostAvailable)]
    [InlineData(true, true, true, BandText.HostBusy)]
    public void 連携の1行は3通りとも出る(bool present, bool seen, bool busy, string expected) =>
        Assert.Equal(expected, BandText.HostLine(ServerState.Ready, present, seen, busy));
}

/// <summary>medium 2／9＝走行数の標本が実際に帯まで届く（段 D で契約に足った欄）。</summary>
public sealed class StageGHostLineTests
{
    private static StatusViewModel NewStatus() =>
        new(() => System.Threading.Tasks.Task.CompletedTask,
            () => System.Threading.Tasks.Task.CompletedTask,
            null,
            null);

    [Fact]
    public void 欄が無い個体は使えますだけを出す()
    {
        var status = NewStatus();
        status.ApplyState(ServerState.Ready, null);
        status.ApplyHost(fieldPresent: false, busy: false);

        Assert.Equal(BandText.HostAvailable, status.BandHostText);
    }

    [Fact]
    public void 呼ばれるまではまだ呼ばれていませんを出す()
    {
        var status = NewStatus();
        status.ApplyState(ServerState.Ready, null);
        status.ApplyHost(fieldPresent: true, busy: false);

        Assert.Equal(BandText.HostIdle, status.BandHostText);
    }

    [Fact]
    public void 走っている間は使われていますを出し以後は使えますに戻る()
    {
        var status = NewStatus();
        status.ApplyState(ServerState.Ready, null);

        status.ApplyHost(fieldPresent: true, busy: true);
        Assert.Equal(BandText.HostBusy, status.BandHostText);
        Assert.True(status.HostBusy);

        // 1 度でも見たら「まだ呼ばれていません」へは戻らない（§2-1c の `_hostSeen`）。
        status.ApplyHost(fieldPresent: true, busy: false);
        Assert.Equal(BandText.HostAvailable, status.BandHostText);
        Assert.False(status.HostBusy);
    }
}

/// <summary>high 8／medium 12＝版を間違えて入れた機体に、見ていない事実を名乗らせない。</summary>
public sealed class StageGWrongEditionTests
{
    [Fact]
    public void Radeon版をNVIDIAの機体に入れたらそう言う()
    {
        var probe = new DriverProbe("580.00", 1, Probed: true, null, "NVIDIA GeForce RTX 3090");
        var line = FirstRunViewModel.DecisionLineFor(RuntimeVariants.RocmGfx1151, probe.GpuName, probe);

        Assert.Equal(FirstRunViewModel.WrongEditionNeedsCuda, line);
        Assert.DoesNotContain("AMD の Radeon（NVIDIA", line, StringComparison.Ordinal);
    }

    [Fact]
    public void RTX版をAMDの機体に入れたらそう言う()
    {
        var probe = new DriverProbe(
            null, 0, Probed: true, "調べられませんでした。", null,
            HasNvidiaAdapter: false, HasAmdAdapter: true);
        var line = FirstRunViewModel.DecisionLineFor(RuntimeVariants.Cu130, null, probe);

        Assert.Equal(FirstRunViewModel.WrongEditionNeedsRocm, line);
    }

    [Fact]
    public void 会社が合っている機体では何も言わない()
    {
        var probe = new DriverProbe(
            null, 0, Probed: true, "調べられませんでした。", null,
            HasNvidiaAdapter: false, HasAmdAdapter: true);

        Assert.Null(FirstRunViewModel.WrongEditionFor(RuntimeVariants.RocmGfx1151, probe));
        Assert.Contains(
            "ROCm で動かします",
            FirstRunViewModel.DecisionLineFor(RuntimeVariants.RocmGfx1151, null, probe),
            StringComparison.Ordinal);
    }

    [Fact]
    public void 両社の板が同居した機体では版を間違えたと言わない()
    {
        var probe = new DriverProbe(
            "580.00", 1, Probed: true, null, "NVIDIA GeForce RTX 4060",
            HasNvidiaAdapter: true, HasAmdAdapter: true);

        Assert.Null(FirstRunViewModel.WrongEditionFor(RuntimeVariants.RocmGfx1151, probe));

        // それでも「AMD の Radeon（NVIDIA …）」とは名乗らない（high 8 の最小の手）。
        var line = FirstRunViewModel.DecisionLineFor(RuntimeVariants.RocmGfx1151, probe.GpuName, probe);
        Assert.DoesNotContain("NVIDIA", line, StringComparison.Ordinal);
    }

    [Fact]
    public void 何も見ていない回は版の話をしない()
    {
        Assert.Null(FirstRunViewModel.WrongEditionFor(RuntimeVariants.Cu130, DriverProbe.Unknown));
        Assert.Null(FirstRunViewModel.WrongEditionFor(RuntimeVariants.RocmGfx1151, null));
    }
}

/// <summary>high 14＝一覧の行に台帳の生の綴りを出さない。</summary>
public sealed class StageGVariantRowTests
{
    [Theory]
    [InlineData(RuntimeVariants.Cu130)]
    [InlineData(RuntimeVariants.Cu126)]
    [InlineData(RuntimeVariants.Cpu)]
    [InlineData(RuntimeVariants.RocmGfx1151)]
    public void 一覧の札に変種の綴りが出ない(string variant)
    {
        var label = RuntimeVariants.DisplayName(variant);

        Assert.DoesNotContain(variant, label, StringComparison.Ordinal);
        Assert.DoesNotContain("gfx1151", label, StringComparison.Ordinal);
        Assert.DoesNotContain("cu13", label, StringComparison.Ordinal);
        Assert.DoesNotContain("cu12", label, StringComparison.Ordinal);
    }

    [Fact]
    public void 札はROCmとCUDAの版の数字で引ける()
    {
        // probe/d-launch-probe.ps1 の Get-VariantComboToken が引く語（同じ回に直した）。
        Assert.Contains("ROCm", RuntimeVariants.DisplayName(RuntimeVariants.RocmGfx1151), StringComparison.Ordinal);
        Assert.Contains("13.0", RuntimeVariants.DisplayName(RuntimeVariants.Cu130), StringComparison.Ordinal);
        Assert.Contains("12.6", RuntimeVariants.DisplayName(RuntimeVariants.Cu126), StringComparison.Ordinal);
        Assert.Contains("CPU", RuntimeVariants.DisplayName(RuntimeVariants.Cpu), StringComparison.Ordinal);
    }
}

/// <summary>medium 3＝配信中の × は 1 行だけ確かめる（文言は憲章 §4-21 の逐語）。</summary>
public sealed class StageGExitPromptTests
{
    [Fact]
    public void 確かめの1行は憲章の逐語である() =>
        Assert.Equal(
            "いま読み分けちゃん2 の読み上げに使われています。終了しますか。",
            UiStrings.ExitWhileHostBusy);
}
