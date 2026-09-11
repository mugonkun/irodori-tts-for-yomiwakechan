using System;
using System.Collections.Generic;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Models;
using IrodoriTtsYwk.Launcher.Services.Server;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 状態の帯の純関数（<see cref="BandText"/>・`v2-spec.md` §2-1／§2-1a／§2-1b）。
/// <para>
/// <b>内部の 1 行は本物に組ませる</b>のが眼目である＝<see cref="ServerBindFailure.Message"/>・
/// <see cref="ServerExitCodes.Describe"/>・<see cref="VariantRecommendation.StartRefusalReason"/>・
/// <see cref="VariantGate.Decide"/>・<see cref="GpuResolver.NotFoundMessage"/>・
/// <see cref="ProcessRunner.StartStalledMessage"/> の**返り値そのもの**を通す。
/// 向こうの綴りが変われば、写しの標識が古びた瞬間にここが落ちる。
/// </para>
/// <para>
/// <b>⑶ の無い文が出る道は 1 本も無い</b>＝最後の 1 本がそれを釘付けする。
/// </para>
/// </summary>
public sealed class BandTextTests
{
    private static readonly string[] Choices = [RuntimeVariants.Cu130, RuntimeVariants.Cu126];

    // ---------------------------------------------------------------- 3 語（§2-1）

    [Theory]
    [InlineData(ServerState.Stopped)]
    [InlineData(ServerState.Starting)]
    public void 起こす前と起こしている間は準備しています(ServerState state)
    {
        var band = BandText.For(state, null);

        Assert.Equal(BandText.Preparing, band.Headline);
        Assert.Equal(BandSeverity.Neutral, band.Severity);
        Assert.Equal(BandActionKind.None, band.Action);
        Assert.Null(band.Reason);
    }

    [Fact]
    public void 声を読み込んでいる間は理由の1行を添える()
    {
        var loading = BandText.For(ServerState.Listening, null, new BandContext(RuntimeLoaded: false));
        var loaded = BandText.For(ServerState.Listening, null, new BandContext(RuntimeLoaded: true));

        // 名乗り（ひとこと）は 3 語のまま＝増やさない。**理由の 1 行**が待つ訳を言う。
        Assert.Equal(BandText.Preparing, loading.Headline);
        Assert.Equal(BandText.PreparingVoicesWhy, loading.Reason);
        Assert.Equal(BandActionKind.None, loading.Action);

        Assert.Equal(BandText.Preparing, loaded.Headline);
        Assert.Null(loaded.Reason);
    }

    /// <summary>
    /// <b>待っている秒は名乗りの中で毎秒動く</b>（決裁 137 ⒞・v2.0.1（2））＝
    /// 所有者の測り方 ⑶「目に見えて動く物が在れば固まってはいない」の帯側の持ち場。
    /// </summary>
    [Theory]
    [InlineData(ServerState.Starting, null)]
    [InlineData(ServerState.Listening, null)]
    // **降格した回も同じ**（是正・検分）＝この枝は Listening のまま落ちてきて、刻みは走っている。
    // 秒を落としていたころは帯を組み直しても同じ 1 行になり、束縛が動かず何分でも静止した。
    [InlineData(ServerState.Listening, ServerStateMachine.UnreachableReason)]
    public void 準備を待っている間は秒を添える(ServerState state, string? reason)
    {
        var band = BandText.For(state, reason, new BandContext(ElapsedSeconds: 12));

        Assert.Equal("準備しています…（12 秒）", band.Headline);
        Assert.StartsWith(BandText.Preparing[..^1], band.Headline, StringComparison.Ordinal);
        Assert.Equal(BandSeverity.Neutral, band.Severity);
        Assert.Equal(
            reason is null ? BandActionKind.None : BandActionKind.Restart,
            band.Action);

        // 判らない回・負の回は**推測の数を出さない**（括弧ごと落とす）。
        Assert.Equal(BandText.Preparing, BandText.PreparingWith(null));
        Assert.Equal(BandText.Preparing, BandText.PreparingWith(-1));
        Assert.Equal("準備しています…（0 秒）", BandText.PreparingWith(0));
    }

    [Fact]
    public void 使えるときは緑で1手を出さない()
    {
        var ready = BandText.For(ServerState.Ready, null);
        var warming = BandText.For(ServerState.Warming, null);

        Assert.Equal(BandText.Ready, ready.Headline);
        Assert.Equal(BandText.ReadyWarming, warming.Headline);
        Assert.Equal(BandSeverity.Ok, ready.Severity);
        Assert.Equal(BandSeverity.Ok, warming.Severity);
        Assert.Equal(BandActionKind.None, ready.Action);
        Assert.Equal(BandActionKind.None, warming.Action);
    }

    [Fact]
    public void 利用者が止めた回だけ止まっていますと名乗る()
    {
        var band = BandText.For(ServerState.Stopped, null, new BandContext(UserStopped: true));

        Assert.Equal(BandText.StoppedByUser, band.Headline);
        Assert.Equal("もう一度動かす", band.ActionLabel);
        Assert.Equal(BandActionKind.Start, band.Action);
        Assert.Equal(BandSeverity.Neutral, band.Severity);
    }

    [Fact]
    public void 自動で起こさない機体には設定への導線を出す()
    {
        var band = BandText.For(
            ServerState.Stopped, null, new BandContext(AutoStartDisabled: true));

        Assert.Equal(BandText.StoppedByUser, band.Headline);
        Assert.Equal(BandText.AutoStartOffHint, band.Reason);
        Assert.Equal(BandActionKind.Start, band.Action);
    }

    // ---------------------------------------------------------------- A 群（起こす前に断った）

    [Fact]
    public void 実行系がまだ無い回は準備が終わっていませんと言う()
    {
        var internalLine = MainViewModel.RuntimeMissingPrefix + RuntimeVariants.Cu130
            + "」の実行系がまだありません（初回取得が未了です）。" + AcquisitionCheck.Hint;

        var band = BandText.For(ServerState.Failed, internalLine);

        Assert.Equal(BandSeverity.Bad, band.Severity);
        Assert.Equal(BandText.Failed, band.Headline);
        Assert.StartsWith("まだ準備が終わっていません。", band.Reason, StringComparison.Ordinal);
        Assert.Contains("動かすための一式", band.Reason, StringComparison.Ordinal);
        Assert.Equal(BandActionKind.FirstRun, band.Action);
        Assert.Equal("はじめの準備をする", band.ActionLabel);
    }

    [Fact]
    public void モデルが足りない回は数を出す()
    {
        var internalLine = MainViewModel.ModelsMissingPrefix + "a・b・c）。" + AcquisitionCheck.Hint;

        var band = BandText.For(
            ServerState.Failed, internalLine, new BandContext(MissingModelCount: 3));

        Assert.Contains("声のデータが 3 件足りません。", band.Reason, StringComparison.Ordinal);
        Assert.Equal(BandActionKind.FirstRun, band.Action);
    }

    [Fact]
    public void 数が判らない回は数を出さない()
    {
        var internalLine = MainViewModel.ModelsMissingPrefix + "a）。" + AcquisitionCheck.Hint;

        var band = BandText.For(ServerState.Failed, internalLine);

        Assert.Contains("声のデータが足りません。", band.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("件", band.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void 覚えているGPUが居ない回は選び直させる()
    {
        var internalLine = GpuResolver.NotFoundMessage("NVIDIA GeForce RTX 3090", "GPU-abc");

        var band = BandText.For(
            ServerState.Failed, internalLine, new BandContext(GpuName: "NVIDIA GeForce RTX 3090"));

        Assert.StartsWith("前に使っていたグラフィックスが見つかりません。", band.Reason, StringComparison.Ordinal);
        Assert.Contains("NVIDIA GeForce RTX 3090", band.Reason, StringComparison.Ordinal);
        Assert.Equal(BandActionKind.OpenSettings, band.Action);
    }

    [Fact]
    public void ドライバが古い回は切り替え先を1手にする()
    {
        var internalLine = VariantRecommendation.StartRefusalReason(
            Choices, RuntimeVariants.Cu130, "537.58");
        Assert.NotNull(internalLine);

        var band = BandText.For(
            ServerState.Failed,
            internalLine,
            new BandContext(
                DriverVersion: "537.58",
                Variant: RuntimeVariants.Cu130,
                AlternativeVariant: RuntimeVariants.Cu126));

        Assert.StartsWith("この GPU では、いまの動かし方が使えません。", band.Reason, StringComparison.Ordinal);
        Assert.Contains("537.58", band.Reason, StringComparison.Ordinal);
        Assert.Contains("CUDA 12.6 なら動きます。", band.Reason, StringComparison.Ordinal);
        Assert.Equal("CUDA 12.6 で準備しなおす", band.ActionLabel);
        Assert.Equal(BandActionKind.FirstRun, band.Action);
    }

    [Fact]
    public void 逃げ道が無い回はドライバ更新ページを出す()
    {
        // 選べる道が cu130 しか無い機体＝勧める先が自分自身になり、末尾の 1 文が変わる。
        string[] only = [RuntimeVariants.Cu130];
        var internalLine = VariantRecommendation.StartRefusalReason(
            only, RuntimeVariants.Cu130, "537.58");
        Assert.NotNull(internalLine);

        var band = BandText.For(
            ServerState.Failed,
            internalLine,
            new BandContext(DriverVersion: "537.58", Variant: RuntimeVariants.Cu130));

        Assert.Contains("このパソコンで使える別の動かし方がありません。", band.Reason, StringComparison.Ordinal);
        Assert.Equal(BandActionKind.OpenDriverPage, band.Action);
    }

    // ---------------------------------------------------------------- B 群（門が断った）

    [Fact]
    public void GPUを見られない門の断りはグラフィックスの話にする()
    {
        var probe = new TorchGpuProbe.TorchProbeResult(
            [], "2.9.0", "13.0", null, null, Available: false, DeviceCount: 0);
        var decision = VariantGate.Decide(RuntimeVariants.Cu130, probe, "580.00", []);
        Assert.False(decision.Allow);

        var band = BandText.For(
            ServerState.Failed,
            decision.Reason,
            new BandContext(DriverVersion: "580.00", Variant: RuntimeVariants.Cu130));

        Assert.StartsWith("グラフィックスが使えませんでした。", band.Reason, StringComparison.Ordinal);
        Assert.Contains("CUDA 13.0", band.Reason, StringComparison.Ordinal);
        Assert.NotEqual(BandActionKind.None, band.Action);
    }

    /// <summary>
    /// <b>Radeon の機体でも台帳の綴りを出さない</b>（憲章 附録 5＝<c>gfx1151</c> の綴りは落とす・
    /// `v2-spec.md` §6-1＝版の名は <b>RTX（CUDA）／Radeon（ROCm）</b> の併記が正）。
    /// <see cref="RuntimeVariants.ShortDisplayName"/> は <c>rocm-gfx1151</c> に
    /// 「Radeon gfx1151」を、<c>cuda</c> に「CUDA 版」を返すので、<b>帯はそれを呼ばない</b>。
    /// </summary>
    [Fact]
    public void 帯は台帳の綴りも版の片名も出さない()
    {
        var probe = new TorchGpuProbe.TorchProbeResult(
            [], "2.9.0", null, null, null, Available: false, DeviceCount: 0);
        var decision = VariantGate.Decide(RuntimeVariants.RocmGfx1151, probe, null, []);
        Assert.False(decision.Allow);

        var band = BandText.For(
            ServerState.Failed,
            decision.Reason,
            new BandContext(
                Variant: RuntimeVariants.RocmGfx1151,
                AlternativeVariant: RuntimeVariants.RocmGfx1151));

        Assert.Contains("ROCm", band.Reason, StringComparison.Ordinal);

        foreach (var text in new[] { band.Headline, band.Reason, band.ActionLabel })
        {
            foreach (var forbidden in new[] { "gfx1151", "CUDA 版", "ROCm 版", "Radeon 版", "変種" })
            {
                Assert.DoesNotContain(forbidden, text ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// 知らない綴り（新しい変種・壊れた設定）は<b>台帳の生の綴りを出さず</b>「いまの動かし方」へ落ちる。
    /// </summary>
    [Fact]
    public void 知らない動かし方の綴りは画面に出さない()
    {
        var band = BandText.For(
            ServerState.Failed,
            "cu999 はこの機体で GPU を見られません（観測）。cu126 に切り替えてください。",
            new BandContext(Variant: "cu999"));

        Assert.DoesNotContain("cu999", band.Reason, StringComparison.Ordinal);
        Assert.Contains("いまの動かし方", band.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>本番と同じ文脈</b>（勧める先が渡らない回）でも 1 手は消えない＝設定の詳細へ落ちる。
    /// 勧める先を渡すのは <c>MainViewModel.RecommendedAlternative</c> の仕事である。
    /// </summary>
    [Fact]
    public void 勧める先が無い門の断りは設定の詳細へ落ちる()
    {
        var probe = new TorchGpuProbe.TorchProbeResult(
            [], "2.9.0", "13.0", null, null, Available: false, DeviceCount: 0);
        var decision = VariantGate.Decide(RuntimeVariants.Cu130, probe, "580.00", []);

        var band = BandText.For(
            ServerState.Failed,
            decision.Reason,
            new BandContext(DriverVersion: "580.00", Variant: RuntimeVariants.Cu130));

        Assert.Equal("設定の詳細を開く", band.ActionLabel);
        Assert.Equal(BandActionKind.OpenSettings, band.Action);
    }

    [Fact]
    public void 検分できなかった回は確かめられませんでしたと言う()
    {
        var decision = VariantGate.Decide(RuntimeVariants.Cu130, null, "580.00", []);
        Assert.False(decision.Allow);

        var band = BandText.For(ServerState.Failed, decision.Reason);

        Assert.StartsWith("グラフィックスを確かめられませんでした。", band.Reason, StringComparison.Ordinal);
        Assert.Equal(BandActionKind.Start, band.Action);
        Assert.Equal("もう一度動かす", band.ActionLabel);
    }

    [Fact]
    public void 下限に届かない門の断りもドライバの話にする()
    {
        var probe = new TorchGpuProbe.TorchProbeResult(
            [], "2.9.0", "13.0", null, null, Available: true, DeviceCount: 1);
        var decision = VariantGate.Decide(RuntimeVariants.Cu130, probe, "537.58", []);
        Assert.False(decision.Allow);

        var band = BandText.For(
            ServerState.Failed,
            decision.Reason,
            new BandContext(
                DriverVersion: "537.58",
                Variant: RuntimeVariants.Cu130,
                AlternativeVariant: RuntimeVariants.Cu126));

        Assert.StartsWith("この GPU では、いまの動かし方が使えません。", band.Reason, StringComparison.Ordinal);
        Assert.Contains("537.58", band.Reason, StringComparison.Ordinal);
        Assert.Equal(BandActionKind.FirstRun, band.Action);
    }

    // ---------------------------------------------------------------- C 群・D 群

    [Fact]
    public void つなぎ口が埋まった回だけ番号を出す()
    {
        var band = BandText.For(ServerState.Failed, ServerBindFailure.Message(18088));

        Assert.Contains("18088", band.Reason, StringComparison.Ordinal);
        Assert.StartsWith(
            "ほかのソフトが、このアプリのつなぎ口（18088）を使っています。",
            band.Reason,
            StringComparison.Ordinal);
        Assert.Equal(BandActionKind.OpenGuide, band.Action);
    }

    [Fact]
    public void 実行系を起こせない回は入れ直しへ導く()
    {
        var stalled = ProcessRunner.StartStalledMessage("python.exe", TimeSpan.FromSeconds(20));

        var band = BandText.For(ServerState.Failed, stalled);

        Assert.StartsWith("動かすための一式を起こせませんでした。", band.Reason, StringComparison.Ordinal);
        Assert.Equal(BandActionKind.RebuildRuntime, band.Action);
    }

    [Fact]
    public void 準備が間に合わなかった回はもう一度動かす()
    {
        var band = BandText.For(
            ServerState.Failed, "起動が 120 秒で終わりませんでした。モデルの読み込みが終わっていません。");

        Assert.StartsWith("準備に時間がかかりすぎました。", band.Reason, StringComparison.Ordinal);
        Assert.Equal(BandActionKind.Restart, band.Action);
    }

    /// <summary>
    /// D2＝<c>runtime.error</c> の 1 行。<b>その本文は帯に出さない</b>（憲章 原則 6／原則 7・
    /// `v2-copy.md` §3-2 の書き方の規則）＝⑵ は E-07 の固定文で、生の記録は檔に残る。
    /// </summary>
    [Fact]
    public void 声のデータを読めなかった回は準備のやり直しへ導く()
    {
        var internalLine = ServerStateMachine.RuntimeLoadFailedReason(
            "FileNotFoundError: [Errno 2] No such file or directory: "
            + "'C:\\Users\\someone\\AppData\\Local\\models\\model.safetensors'");

        var band = BandText.For(ServerState.Failed, internalLine);

        Assert.Equal(
            "声の読み込みに失敗しました。 声のデータが途中までしか入っていない可能性があります。",
            band.Reason);
        Assert.DoesNotContain("C:\\", band.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("FileNotFoundError", band.Reason, StringComparison.Ordinal);
        Assert.Equal(BandActionKind.FirstRun, band.Action);
        Assert.Equal("はじめの準備をやり直す", band.ActionLabel);
    }

    [Fact]
    public void 応答が消えた回は赤にしない()
    {
        var band = BandText.For(ServerState.Listening, ServerStateMachine.UnreachableReason);

        Assert.Equal(BandSeverity.Neutral, band.Severity);
        Assert.Equal(BandText.Preparing, band.Headline);
        Assert.Equal(BandActionKind.Restart, band.Action);
        Assert.Equal("いったん止めて、動かし直す", band.ActionLabel);

        // 秒を持っている回はその数を添える（＝毎秒 1 行が変わる）。
        Assert.Equal(
            "準備しています…（7 秒）",
            BandText.For(
                ServerState.Listening,
                ServerStateMachine.UnreachableReason,
                new BandContext(ElapsedSeconds: 7)).Headline);
    }

    [Theory]
    [InlineData(ServerExitCodes.WrapperPrecheck, "設定と合いませんでした。", BandActionKind.OpenSettings)]
    [InlineData(ServerExitCodes.UpstreamStartup, "声のデータを読み込めませんでした。", BandActionKind.FirstRun)]
    [InlineData(9, "とちゅうで終わってしまいました。", BandActionKind.OpenLog)]
    public void 終了コードごとに1手が変わる(int exitCode, string what, BandActionKind kind)
    {
        var band = BandText.For(ServerState.Failed, ServerExitCodes.Describe(exitCode, null));

        Assert.StartsWith(what, band.Reason, StringComparison.Ordinal);
        Assert.Equal(kind, band.Action);
    }

    /// <summary>
    /// D6 の ⑵ は<b>終了コードの数を差す</b>（`v2-spec.md` §2-1a）＝利用者が報告に書ける
    /// 唯一の手がかり。<b>判らない回は括弧ごと落とす</b>（プレースホルダを出さない）。
    /// </summary>
    [Fact]
    public void 異常終了の理由には終了コードの数が入る()
    {
        var internalLine = ServerExitCodes.Describe(9, null);

        var known = BandText.For(ServerState.Failed, internalLine, new BandContext(ExitCode: 9));
        var unknown = BandText.For(ServerState.Failed, internalLine);

        Assert.Equal(
            "とちゅうで終わってしまいました。 予期しない終わり方をしました（終了コード 9）。",
            known.Reason);
        Assert.Equal(
            "とちゅうで終わってしまいました。 予期しない終わり方をしました。",
            unknown.Reason);
        Assert.Equal(BandActionKind.OpenLog, known.Action);
    }

    /// <summary>
    /// D7（exit 0）＝<b>理由の行は出さない</b>（`v2-spec.md` §2-1a の D7）＝
    /// 見出しと同じ「止まりました。」を 2 度並べない。
    /// </summary>
    [Fact]
    public void 綺麗に終わった回は理由の行を出さない()
    {
        var band = BandText.For(ServerState.Failed, ServerExitCodes.Describe(0, null));

        Assert.Equal(BandText.Failed, band.Headline);
        Assert.Null(band.Reason);
        Assert.Equal(BandActionKind.Start, band.Action);
        Assert.Equal("もう一度動かす", band.ActionLabel);
    }

    [Fact]
    public void 見分けのつかない1行はログを開くへ落ちる()
    {
        var band = BandText.For(ServerState.Failed, "誰も知らない新しい失敗の 1 行");

        Assert.Equal("うまく動きませんでした。 理由が分かりませんでした。", band.Reason);
        Assert.Equal(BandActionKind.OpenLog, band.Action);
        Assert.Equal("ログを開く", band.ActionLabel);
    }

    [Fact]
    public void 理由が空の失敗でも1手は必ず出る()
    {
        foreach (var reason in new string?[] { null, string.Empty, "   " })
        {
            var band = BandText.For(ServerState.Failed, reason);

            Assert.Equal(BandActionKind.OpenLog, band.Action);
            Assert.NotNull(band.ActionLabel);
        }
    }

    /// <summary>
    /// <b>⑶ の無い文がコードから出る道は 1 本も無い</b>（憲章 原則 6 の検分文・§2-1b）。
    /// </summary>
    [Fact]
    public void 失敗の文には必ず次の1手が付く()
    {
        var lines = new List<string?>
        {
            null,
            "誰も知らない新しい失敗の 1 行",
            ServerBindFailure.Message(18088),
            ServerStateMachine.RuntimeLoadFailedReason("boom"),
            ServerStateMachine.UnreachableReason,
            GpuResolver.NotFoundMessage("RTX 3090", "GPU-abc"),
            ProcessRunner.StartStalledMessage("python.exe", TimeSpan.FromSeconds(20)),
            VariantRecommendation.StartRefusalReason(Choices, RuntimeVariants.Cu130, "537.58"),
            "起動が 120 秒で終わりませんでした。モデルの読み込みが終わっていません。",
            MainViewModel.RuntimeMissingPrefix + "cu130」の実行系がまだありません（初回取得が未了です）。"
                + AcquisitionCheck.Hint,
        };

        foreach (var exitCode in new[] { 0, 1, 2, 3, 9 })
        {
            lines.Add(ServerExitCodes.Describe(exitCode, "tail"));
        }

        foreach (var line in lines)
        {
            var band = BandText.For(ServerState.Failed, line);

            // **錠は ⑶ に掛かる**（原則 6 の検分文）＝⑵ の有無は D7 が例外だと spec が書いている
            // （`v2-spec.md` §2-1a の D7＝「（理由の行は出さない）」）。
            Assert.NotEqual(BandActionKind.None, band.Action);
            Assert.False(string.IsNullOrWhiteSpace(band.ActionLabel));
        }
    }

    // ---------------------------------------------------------------- 連携の 1 行（§2-1c）

    [Fact]
    public void 連携の1行は使えるときだけ出る()
    {
        Assert.Equal(
            string.Empty,
            BandText.HostLine(ServerState.Starting, hostFieldPresent: true, hostSeen: true, hostBusy: false));
        Assert.Equal(
            BandText.HostAvailable,
            BandText.HostLine(ServerState.Ready, hostFieldPresent: true, hostSeen: true, hostBusy: false));
    }

    [Fact]
    public void 欄が無い個体には使えますだけを出す()
    {
        var line = BandText.HostLine(
            ServerState.Ready, hostFieldPresent: false, hostSeen: false, hostBusy: false);

        Assert.Equal(BandText.HostAvailable, line);
    }

    [Fact]
    public void 走っている間といちども呼ばれていない間で文が変わる()
    {
        Assert.Equal(
            BandText.HostBusy,
            BandText.HostLine(ServerState.Ready, hostFieldPresent: true, hostSeen: true, hostBusy: true));
        Assert.Equal(
            BandText.HostIdle,
            BandText.HostLine(ServerState.Ready, hostFieldPresent: true, hostSeen: false, hostBusy: false));
    }
}
