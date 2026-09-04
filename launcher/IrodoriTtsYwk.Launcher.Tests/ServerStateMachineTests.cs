using System;
using System.Collections.Generic;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Server;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 状態機械（設計書 §2・受け入れ条件 D-1／D-4）を<b>実機なしで</b>釘付けする。
/// 流す行は便 A（2）・便 C の逐語（<c>docs/contract.md</c> ⑹・<c>docs/radeon.md</c> §7）。
/// </summary>
public sealed class ServerStateMachineTests
{
    /// <summary>この機体（Radeon gfx1151）で実際に出た起動 3 行＋上流の 1 行。</summary>
    private const string BannerLine =
        "ywk_server 0.1.0 upstream=8224daf/841fb7c variant=rocm-gfx1151 device=cuda:0/cuda:0 "
        + "precision=None miopen=miopen/db";

    private const string UvicornLine = "INFO:     Uvicorn running on http://127.0.0.1:18088 (Press CTRL+C to quit)";

    private const string RuntimeLoadedLine = "INFO:irodori:runtime loaded in 26.4s";

    private const string DeviceActualLine =
        "ywk_server: device actual=cuda:0 dtype=torch.bfloat16 name=AMD Radeon(TM) 8060S Graphics gcn=gfx1151";

    private static ServerStateMachine Feed(params string[] lines)
    {
        var machine = new ServerStateMachine();
        machine.ApplyStarted();
        foreach (var line in lines)
        {
            machine.ApplyLogLine(line, isStandardError: true, port: 18088);
        }

        return machine;
    }

    [Fact]
    public void 起動3行で停止から待機まで進む()
    {
        var machine = new ServerStateMachine();
        Assert.Equal(ServerState.Stopped, machine.State);

        machine.ApplyStarted();
        Assert.Equal(ServerState.Starting, machine.State);

        machine.ApplyLogLine(BannerLine, true, 18088);
        Assert.Equal(ServerState.Starting, machine.State);

        machine.ApplyLogLine(UvicornLine, true, 18088);
        Assert.Equal(ServerState.Listening, machine.State);

        // ログの `runtime loaded in` は印を立てるだけ（是正・2026-09-05）
        machine.ApplyLogLine(RuntimeLoadedLine, true, 18088);
        Assert.True(machine.RuntimeLoadedSeen);
        Assert.Equal(ServerState.Listening, machine.State);

        // Ready は HTTP の標本だけが立てる（契約 ⑵＝ready は runtime.loaded で判る）
        Assert.True(machine.ApplyReadiness(reachable: true, loaded: true, warmupRunning: false));
        Assert.Equal(ServerState.Ready, machine.State);
    }

    [Fact]
    public void runtime_loadedを流してもreachableでない間は待機にしない()
    {
        // 所見 1 の釘＝上流は uvicorn の lifespan でモデルを載せるので、この 1 行は
        // **bind より先に出る**（実測＝ログ 30.264 s・socket 30.271 s）。
        // 1 度も listen しない個体を「起動しました」で抜けさせない。
        var machine = Feed(BannerLine, RuntimeLoadedLine);

        Assert.True(machine.RuntimeLoadedSeen);
        Assert.Equal(ServerState.Starting, machine.State);

        // 到達不能の標本は 3 回流しても Ready にならず、返りも偽（＝ready 待ちは抜けない）
        for (var i = 0; i < 3; i++)
        {
            Assert.False(machine.ApplyReadiness(reachable: false, loaded: false, warmupRunning: false));
        }

        Assert.NotEqual(ServerState.Ready, machine.State);

        // 届いて runtime.loaded=true になった標本で初めて Ready
        Assert.True(machine.ApplyReadiness(reachable: true, loaded: true, warmupRunning: false));
        Assert.Equal(ServerState.Ready, machine.State);
    }

    [Fact]
    public void 待機の後に3回届かなければ読込中へ降りる()
    {
        // 所見 5 の釘＝プロセスは生きているが応答しない wrapper（HIP のハング・
        // event loop の詰まり）を永遠に「待機」のまま見せない。**殺しはしない**。
        var machine = Feed(BannerLine, UvicornLine, RuntimeLoadedLine);
        machine.ApplyReadiness(true, true, false);
        Assert.Equal(ServerState.Ready, machine.State);

        var reasons = new List<string?>();
        machine.StateChanged += (_, e) => reasons.Add(e.Reason);

        machine.ApplyReadiness(false, false, false);
        Assert.Equal(ServerState.Ready, machine.State);
        machine.ApplyReadiness(false, false, false);
        Assert.Equal(ServerState.Ready, machine.State);

        Assert.False(machine.ApplyReadiness(false, false, false));
        Assert.Equal(ServerState.Listening, machine.State);
        Assert.Contains(reasons, r => r is not null && r.Contains("応答しなく", StringComparison.Ordinal));

        // 戻ってくれば待機に上がり直す（自力復帰の路は残す）
        Assert.True(machine.ApplyReadiness(true, true, false));
        Assert.Equal(ServerState.Ready, machine.State);
    }

    [Fact]
    public void 起動1行目から版と上流と変種とdeviceを読む()
    {
        var machine = Feed(BannerLine);

        Assert.NotNull(machine.Banner);
        Assert.Equal("0.1.0", machine.Banner!.Version);
        Assert.Equal("8224daf/841fb7c", machine.Banner.Upstream);
        Assert.Equal(RuntimeVariants.RocmGfx1151, machine.Banner.Variant);
        Assert.Equal("cuda:0/cuda:0", machine.Banner.Device);
    }

    [Fact]
    public void device実測の行から実deviceを読む()
    {
        var machine = Feed(BannerLine, UvicornLine, DeviceActualLine);

        // 設定値の echo ではなく実測値（契約 ⑹）
        Assert.StartsWith("cuda:0", machine.DeviceActual, StringComparison.Ordinal);

        // 診断の行は状態を進めない
        Assert.Equal(ServerState.Listening, machine.State);
    }

    [Fact]
    public void 待機の後にUvicornの行が流れても読込中へ戻らない()
    {
        var machine = Feed(BannerLine, UvicornLine, RuntimeLoadedLine);
        machine.ApplyReadiness(true, true, false);
        Assert.Equal(ServerState.Ready, machine.State);

        machine.ApplyLogLine(UvicornLine, true, 18088);
        Assert.Equal(ServerState.Ready, machine.State);
    }

    [Fact]
    public void 暖機が走ると暖機中になり終われば待機へ戻る()
    {
        var machine = Feed(BannerLine, UvicornLine, RuntimeLoadedLine);

        machine.ApplyReadiness(reachable: true, loaded: true, warmupRunning: true);
        Assert.Equal(ServerState.Warming, machine.State);

        machine.ApplyReadiness(reachable: true, loaded: true, warmupRunning: false);
        Assert.Equal(ServerState.Ready, machine.State);
    }

    [Fact]
    public void healthが200なら1行も読めなくても読込中になる()
    {
        var machine = new ServerStateMachine();
        machine.ApplyStarted();

        machine.ApplyReadiness(reachable: true, loaded: false, warmupRunning: false);

        Assert.Equal(ServerState.Listening, machine.State);
    }

    [Fact]
    public void exit2は事前検査の理由1行になる()
    {
        var machine = Feed(
            "ywk_server: IRODORI_MODEL_PRECISION=fp32 is not allowed on YWK_VARIANT=rocm-gfx1151.");
        machine.ApplyExit(ServerExitCodes.WrapperPrecheck);

        Assert.Equal(ServerState.Failed, machine.State);
        Assert.NotNull(machine.FailureReason);
        Assert.Contains("起動前の検査で止まった", machine.FailureReason!, StringComparison.Ordinal);
        // stderr の最終行が理由に載る（受け入れ条件 D-1＝理由 1 行）
        Assert.Contains("fp32", machine.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void exit3は上流のstartup失敗になる()
    {
        var machine = Feed(BannerLine, "RuntimeError: CUDA error: invalid device ordinal");
        machine.ApplyExit(ServerExitCodes.UpstreamStartup);

        Assert.Equal(ServerState.Failed, machine.State);
        Assert.Contains("モデルの読み込みに失敗", machine.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 失敗した後はどの行でも状態が進まない()
    {
        var machine = Feed(BannerLine);
        machine.ApplyExit(ServerExitCodes.WrapperPrecheck);

        machine.ApplyLogLine(UvicornLine, true, 18088);
        machine.ApplyReadiness(true, true, false);

        Assert.Equal(ServerState.Failed, machine.State);
    }

    [Theory]
    [InlineData("ERROR:    [Errno 10048] error while attempting to bind on address ('127.0.0.1', 18088)")]
    [InlineData("OSError: [WinError 10048] 通常、各ソケット アドレスに対してプロトコル、ネットワーク アドレス")]
    [InlineData("[Errno 98] Address already in use")]
    public void ポートが塞がっていたら止まって告知する(string line)
    {
        // 裁定 52＝次のポートを探さない
        Assert.True(ServerBindFailure.Detect(line));

        var machine = Feed(BannerLine, line);

        Assert.Equal(ServerState.Failed, machine.State);
        Assert.Contains("18088", machine.FailureReason!, StringComparison.Ordinal);
        Assert.Contains("自動では別のポートを探しません", machine.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 普通の行をbind失敗と読み違えない()
    {
        Assert.False(ServerBindFailure.Detect(UvicornLine));
        Assert.False(ServerBindFailure.Detect(BannerLine));
        Assert.False(ServerBindFailure.Detect(null));
    }

    [Fact]
    public void 本文echoの422はログに流さず畳む()
    {
        var machine = new ServerStateMachine();
        var logged = new List<string>();
        machine.LogLine += (_, e) => logged.Add(e.Event.Line);
        machine.ApplyStarted();

        var echo = new string('あ', 5000);
        machine.ApplyLogLine(echo, true, 18088);

        Assert.Single(logged);
        Assert.True(logged[0].Length <= ServerLogParser.MaxLoggedLineLength + 40);
        Assert.True(ServerLogParser.LooksLikeRequestEcho(echo));
    }

    [Fact]
    public void 停止は失敗からも戻れる()
    {
        var machine = Feed(BannerLine);
        machine.ApplyFailure("試しに落とした。");
        Assert.Equal(ServerState.Failed, machine.State);

        machine.ApplyStopped();
        Assert.Equal(ServerState.Stopped, machine.State);
        Assert.Null(machine.FailureReason);
    }

    [Fact]
    public void 状態が変わるたびに1回だけ告げる()
    {
        var machine = new ServerStateMachine();
        var seen = new List<ServerState>();
        machine.StateChanged += (_, e) => seen.Add(e.Current);

        machine.ApplyStarted();
        machine.ApplyLogLine(BannerLine, true, 18088);   // Starting のまま＝告げない
        machine.ApplyLogLine(UvicornLine, true, 18088);
        machine.ApplyLogLine(UvicornLine, true, 18088);  // 同じ状態＝告げない
        machine.ApplyLogLine(RuntimeLoadedLine, true, 18088); // 印だけ＝告げない
        machine.ApplyReadiness(true, true, false);
        machine.ApplyReadiness(true, true, false);        // 同じ状態＝告げない

        Assert.Equal(new[] { ServerState.Starting, ServerState.Listening, ServerState.Ready }, seen);
    }

    [Theory]
    [InlineData("torch.OutOfMemoryError: CUDA out of memory. Tried to allocate 512.00 MiB "
                + "(GPU 0; 10048.00 MiB total capacity)")]
    [InlineData("INFO:     127.0.0.1:10048 - \"GET /ywk/status HTTP/1.1\" 200 OK")]
    [InlineData("ywk_server: synthesis done in 10048 ms")]
    public void 数字10048を含むだけの行をbind失敗と読み違えない(string line)
    {
        // 所見 3 の釘＝裸の 10048 は標識にしない（理由が嘘のまま終端状態に落ちる）
        Assert.False(ServerBindFailure.Detect(line));
    }

    [Fact]
    public void listenした後の行では状態を落とさない()
    {
        // 所見 3 の釘＝bind 失敗は listen する前にしか来ない。健全に走っている個体の
        // 1 行（OOM・access log・合成の所要）が Ready を Failed に落とさない。
        var machine = Feed(BannerLine, UvicornLine, RuntimeLoadedLine);
        machine.ApplyReadiness(true, true, false);
        Assert.Equal(ServerState.Ready, machine.State);

        machine.ApplyLogLine(
            "ERROR:    [Errno 10048] error while attempting to bind on address ('127.0.0.1', 18088)",
            true, 18088);

        Assert.Equal(ServerState.Ready, machine.State);
        Assert.Null(machine.FailureReason);

        // 後から来た良い標本で待機のままである（自力復帰の必要すらない）
        Assert.True(machine.ApplyReadiness(true, true, false));
    }
}
