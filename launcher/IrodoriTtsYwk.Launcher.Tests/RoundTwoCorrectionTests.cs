using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Ledger;
using IrodoriTtsYwk.Launcher.Services.Server;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 便 D（2）＝<b>是正席</b>の釘（敵対検分の high 1 件・medium 8 件と、ついでに直した low）。
/// <para>
/// <b>実 GPU・実サーバ・停止域のポート（8088／7861／18088）に触れない</b>。
/// 子プロセスが要る釘は <c>cmd.exe</c> の偽の子（<c>ping</c> の宛先は <c>127.0.0.1</c>）、
/// HTTP は <c>127.0.0.1</c> の <see cref="HttpListener"/> だけ（外へ 1 バイトも出ない）。
/// </para>
/// </summary>
public sealed class RoundTwoCorrectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-fix-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>検分が読めた CUDA の観測（<paramref name="usable"/> 偽＝裁定 83 の形）。</summary>
    private static TorchGpuProbe.TorchProbeResult CudaProbe(bool usable) => new(
        [], "2.10.0+cu130", "13.0", null, null, usable, usable ? 1 : 0);

    /// <summary>検分が<b>読めなかった</b>（torch が落ちた・期限切れ・実行系を起こせない）。</summary>
    private static TorchGpuProbe.TorchProbeResult Unobserved(string why) =>
        new([], null, null, null, why);

    private static readonly string[] CudaRelease =
        [RuntimeVariants.Cu130, RuntimeVariants.Cu126, RuntimeVariants.Cpu];

    // ---- high 1＝検分が読めない cu130 を通していた（裁定 83） -----------------

    [Fact]
    public void 門_cu130は検分が撃てなければ起こさない()
    {
        // 是正前＝注意 1 行で**そのまま起こしていた**（実射 Allow=True）。
        // 裁定 83 の禁止形（起動は健全・最初の合成で 0xC0000005）へ、is_available を
        // 1 度も観測しないまま到達できた。
        var decision = VariantGate.Decide(RuntimeVariants.Cu130, null, null, CudaRelease);

        Assert.False(decision.Allow);
        Assert.Empty(decision.Notices);
        Assert.Equal(RuntimeVariants.Cpu, decision.SuggestedVariant);
        Assert.Contains("確かめられないまま起こしません", decision.Reason!, StringComparison.Ordinal);
        Assert.Contains("実行系が見つかりません", decision.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 門_cu130は検分が読めなければ起こさない()
    {
        var decision = VariantGate.Decide(
            RuntimeVariants.Cu130, Unobserved("GPU の列挙が期限内に終わりませんでした。"),
            "591.86", CudaRelease);

        Assert.False(decision.Allow);
        // ドライバが読めれば勧め先は cu126（閾を満たす方）＝cpu に落とさない。
        Assert.Equal(RuntimeVariants.Cu126, decision.SuggestedVariant);
        Assert.Contains("期限内に終わりませんでした", decision.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 門_cu126とrocmは検分が読めなくても注意1行で起こす()
    {
        // **cu130 だけを強く扱う**ことの対照。cu126 は GPU を隠しても 200 が返る（裁定 83）。
        var cu126 = VariantGate.Decide(
            RuntimeVariants.Cu126, Unobserved("ImportError: DLL load failed"), "591.86", CudaRelease);
        Assert.True(cu126.Allow);
        // 是正・段 C の検分＝torch の生の例外文は**記録の側**に落ちる（Trail）。
        // 画面の 1 行は「確かめられませんでした」＋次の 1 手だけ（憲章 原則 6）。
        Assert.Contains("ImportError", Assert.Single(cu126.LogLines), StringComparison.Ordinal);
        Assert.DoesNotContain("ImportError", Assert.Single(cu126.Notices), StringComparison.Ordinal);

        var rocm = VariantGate.Decide(
            RuntimeVariants.RocmGfx1151, null, null, [RuntimeVariants.RocmGfx1151, RuntimeVariants.Cpu]);
        Assert.True(rocm.Allow);
        Assert.Single(rocm.Notices);
    }

    // ---- medium＝畳んだ名 cuda で閾も注意も落ちていた -------------------------

    [Fact]
    public void 門_畳んだ名cudaにもドライバの閾が掛かる()
    {
        // 是正前＝`Minimum("cuda")` が null で、**ドライバ 500.00 でも Allow=True**（実射）。
        // どちらの実行系か判らない以上、厳しい方（cu130 の 580.00）で見る。
        Assert.Equal(DriverRequirement.Cu130Minimum, DriverRequirement.Minimum(RuntimeVariants.CudaLabel));

        var decision = VariantGate.Decide(
            RuntimeVariants.CudaLabel, CudaProbe(usable: true), "500.00", CudaRelease);

        Assert.False(decision.Allow);
        Assert.Contains("下限 580.00 に届きません", decision.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 門_畳んだ名cudaも検分が読めなければ起こさない()
    {
        Assert.True(VariantGate.RequiresObservedProbe(RuntimeVariants.CudaLabel));

        var decision = VariantGate.Decide(
            RuntimeVariants.CudaLabel, Unobserved("期限切れ"), "591.86", CudaRelease);

        Assert.False(decision.Allow);
        Assert.Contains("確かめられないまま起こしません", decision.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 門_理由1行に絶対パスを出さない()
    {
        // この 1 行は状態帯にもログにも出る。.NET の例外文はフルパスを持つ（実射＝
        // "trying to start process 'C:\...\python.exe'"）ので檔名だけに畳む。
        var probe = Unobserved(
            @"実行系を起こせませんでした：An error occurred trying to start process "
            + @"'C:\Users\dareka\AppData\Local\irodori-tts-ywk\runtime\cu130\python.exe' with working "
            + @"directory 'C:\Program Files\irodori-tts-ywk\server'.");

        var decision = VariantGate.Decide(RuntimeVariants.Cu130, probe, null, CudaRelease);

        Assert.False(decision.Allow);
        Assert.DoesNotContain(@":\", decision.Reason!, StringComparison.Ordinal);
        Assert.Contains("python.exe", decision.Reason!, StringComparison.Ordinal);
        Assert.Contains("server", decision.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 閾_綴りの大小と前後の空白を受ける()
    {
        // 同じ門の中で IsUnmeasuredBand は OrdinalIgnoreCase なのに、Minimum だけ厳密一致だった。
        Assert.Equal(DriverRequirement.Cu130Minimum, DriverRequirement.Minimum(" CU130 "));
        Assert.Equal(DriverRequirement.Cu126Minimum, DriverRequirement.Minimum("Cu126"));
        Assert.Null(DriverRequirement.Minimum("  "));
        Assert.Null(DriverRequirement.Minimum(null));
    }

    [Fact]
    public void 閾_小数点以下の桁数が違っても順序が逆にならない()
    {
        // 是正前＝整数の列として比べていたので 537.58 > 537.6（＝537.60）と読んでいた。
        Assert.True(DriverRequirement.TryCompare("537.58", "537.6", out var a));
        Assert.Equal(-1, a);

        Assert.True(DriverRequirement.TryCompare("528.33", "528.4", out var b));
        Assert.Equal(-1, b);

        // 桁の揃った比較はそのまま（既存の釘と食い違わない）。
        Assert.True(DriverRequirement.TryCompare("580.00", "580", out var c));
        Assert.Equal(0, c);
        Assert.True(DriverRequirement.TryCompare("591.86", "580.00", out var d));
        Assert.Equal(1, d);

        // 3 節以上（ドライバ版には無い形）は節ごとの整数のまま＝10.0.1 と 10.0.10 を混ぜない。
        Assert.True(DriverRequirement.TryCompare("10.0.1", "10.0.10", out var e));
        Assert.Equal(-1, e);
    }

    [Fact]
    public void 勧め_組んでいない変種は勧めない()
    {
        // 台帳が rocm だけの配布で「cpu に切り替えてください」と出さない（low）。
        Assert.Null(VariantGate.Suggest(
            RuntimeVariants.RocmGfx1151, CudaProbe(usable: false), null, [RuntimeVariants.RocmGfx1151]));

        // **判らない（空）ときは従来どおり cpu**＝「判らない」と「無い」を混ぜない。
        Assert.Equal(RuntimeVariants.Cpu, VariantGate.Suggest(
            RuntimeVariants.RocmGfx1151, CudaProbe(usable: false), null, []));
    }

    // ---- medium＝門の検分が落ちると StartAsync が例外で抜けていた -------------

    [Fact]
    public async Task 門の検分が落ちてもStartAsyncは投げない()
    {
        // 契約（IServerProcess.StartAsync＝例外は投げず結末で返す）。実路は
        // GpuEnumerator の期限切れ枝の Kill が Win32Exception を投げる形。
        foreach (var thrown in new Exception[]
                 {
                     new System.ComponentModel.Win32Exception(5, "アクセスが拒否されました。"),
                     new InvalidOperationException("No process is associated with this object."),
                 })
        {
            await using var server = new ServerProcess(
                new FreePort(), new NeverReachable(), TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(50), new ThrowingProbe(thrown), _ => Cmd("exit /b 0"));

            var result = await server.StartAsync(Request(18097, RuntimeVariants.Cu130), CancellationToken.None);

            Assert.False(result.Ok);
            Assert.Equal(ServerState.Failed, result.State);
            Assert.Contains("GPU の検分そのものが落ちました", result.FailureReason!, StringComparison.Ordinal);
            Assert.Contains(thrown.Message, result.FailureReason!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task 子を起こし損ねても門の告知は結末に載る()
    {
        // 「未実測の帯」の 1 行が、python.exe を起こせなかった機体で画面から落ちていた（low）。
        await using var server = new ServerProcess(
            new FreePort(), new NeverReachable(), TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(50),
            new FixedProbe(CudaProbe(usable: true)),
            _ => new ProcessStartInfo(Path.Combine(Path.GetTempPath(), "no-such-python.exe"))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

        var request = Request(18097, RuntimeVariants.Cu126) with { DriverVersion = "530.00" };
        var result = await server.StartAsync(request, CancellationToken.None);

        Assert.False(result.Ok);
        // 是正・段 C の検分＝告知は**画面の半分と記録の半分**に割れた。
        // 画面はドライバの版と次の 1 手だけ、工学の綴り（未実測の帯）は NoticeTrail に残る。
        Assert.Contains("530.00", Assert.Single(result.Notices), StringComparison.Ordinal);
        Assert.Contains("未実測の帯", Assert.Single(result.NoticeTrail), StringComparison.Ordinal);
    }

    // ---- medium＝見張りが「自分の子か」を確かめていなかった -------------------

    [Fact]
    public void 死活_自分の子のuvicornの行を見るまでbind失敗を拾い続ける()
    {
        // 他人が同じポートで答えると HTTP だけで Ready まで上がる。是正前は
        // `State < Listening` で検知を切っていたので、その後に来る**自分の子の**
        // bind 失敗の行を無視した。
        var machine = new ServerStateMachine();
        machine.ApplyStarted();
        machine.ApplyReadiness(reachable: true, loaded: true, warmupRunning: false);
        Assert.Equal(ServerState.Ready, machine.State);
        Assert.False(machine.OwnListenLineSeen);

        machine.ApplyLogLine(
            "ERROR:    [Errno 10048] error while attempting to bind on address ('127.0.0.1', 18098)",
            isStandardError: true, port: 18098);

        Assert.Equal(ServerState.Failed, machine.State);
        Assert.Contains("つなぎ口（18098）を使っています", machine.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 死活_自分の子がlistenした後は走行中の行で落とさない()
    {
        // 対照＝切る条件が消えたわけではない（access log や所要の行で Failed にしない）。
        var machine = new ServerStateMachine();
        machine.ApplyStarted();
        machine.ApplyLogLine(
            "INFO:     Uvicorn running on http://127.0.0.1:18098 (Press CTRL+C to quit)",
            isStandardError: true, port: 18098);
        Assert.True(machine.OwnListenLineSeen);
        machine.ApplyReadiness(reachable: true, loaded: true, warmupRunning: false);

        machine.ApplyLogLine(
            "INFO:     127.0.0.1:10048 - \"GET /ywk/status HTTP/1.1\" 200 OK",
            isStandardError: false, port: 18098);
        machine.ApplyLogLine(
            "synthesis done in 10048 ms (bind free)", isStandardError: true, port: 18098);

        Assert.Equal(ServerState.Ready, machine.State);
    }

    [Fact]
    public async Task 死活_他人のpidの応答は自分の物として採らない()
    {
        // 既にそのポートを握っている個体が居ると、同じ形の応答が返る（裁定 105 のポート先行で
        // 自分の子の bind は 1 秒以内に来るようになったが、その子が bind に失敗して落ちるまでの
        // 窓は残る）。pid が違えば採らない（Ready にも上げない）。
        await using var server = new ServerProcess(
            new FreePort(), new ForeignStatus(pid: 999999), TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(50), null, _ => LiveChild(3));

        var lines = new List<string>();
        server.LogLine += (_, e) => lines.Add(e.Event.Line);

        var result = await server.StartAsync(
            Request(18097, RuntimeVariants.Cpu, TimeSpan.FromMilliseconds(400)), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Null(server.LatestStatus);
        Assert.Contains("別の個体（pid 999999）", result.FailureReason!, StringComparison.Ordinal);
        Assert.Contains(lines, line => line.Contains("別の個体（pid 999999）", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 死活_pidの無い応答は別人と読まない()
    {
        // 古い wrapper・上流の素の Server では `pid` が来ない＝欄の欠けを「別人」にしない。
        await using var server = new ServerProcess(
            new FreePort(), new ForeignStatus(pid: null), TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(50), null, _ => LiveChild(3));

        var result = await server.StartAsync(
            Request(18097, RuntimeVariants.Cpu, TimeSpan.FromSeconds(2)), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.NotNull(server.LatestStatus);
    }

    [Fact]
    public async Task 読込に失敗した個体は生きたまま理由つきで失敗になる()
    {
        // 裁定 105 ⑷（便 G＝ポート先行）＝wrapper は bind してから裏でモデルを載せるので、
        // 読込の失敗はもうプロセスの死（exit 3）として届かない。/ywk/status は 200 のまま
        // runtime.error に理由 1 行を載せる。ready 待ちは**期限を待たず**そこで終わる。
        await using var server = new ServerProcess(
            new FreePort(), new LoadFailedStatus(RuntimeLoadReason), TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(50), null, _ => LiveChild(3));

        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await server.StartAsync(
            Request(18097, RuntimeVariants.Cpu, TimeSpan.FromSeconds(30)), CancellationToken.None);
        started.Stop();

        Assert.False(result.Ok);
        Assert.Equal(ServerState.Failed, result.State);
        Assert.Contains("モデルの読込に失敗＝", result.FailureReason!, StringComparison.Ordinal);
        Assert.Contains("Checkpoint not found", result.FailureReason!, StringComparison.Ordinal);
        // 期限（30 s）を待っていない＝受け入れ条件 D-1 の「≤ 15 s で理由 1 行」。
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(15), started.Elapsed.ToString());
        // 理由は**標本**から来た＝終了コードの綴りに化けていない（子は生きたまま答えた）。
        Assert.DoesNotContain("異常終了", result.FailureReason!, StringComparison.Ordinal);
        Assert.NotNull(result.ProcessId);
        // **子は落とさない**（裁定 105 ⑴＝便 G・設計席の是正）。読込に失敗した個体は bind
        // したまま理由を答えている＝本体の走査がその理由を読める。ポートを閉じるのは
        // 「サーバ停止」（Failed でも押せる＝是正 2026-09-05）。
        Assert.NotNull(server.ProcessId);
        using (var child = System.Diagnostics.Process.GetProcessById(result.ProcessId!.Value))
        {
            Assert.False(child.HasExited);
        }
        Assert.Contains("モデルの読込に失敗＝", server.FailureReason!, StringComparison.Ordinal);

        await server.StopAsync(CancellationToken.None);
        Assert.Null(server.ProcessId);
    }

    [Fact]
    public async Task 走り出した後に読込が失敗したら子を残したまま失敗にする()
    {
        // 裁定 105 ⑷＋是正・2026-09-05＝ready の後の見張りで error を読んだときは子を殺さない。
        // ここが「Failed でも『サーバ停止』が押せる」（StatusViewModel.CanStop）の効き所である。
        var machine = new ServerStateMachine();
        machine.ApplyStarted();
        machine.ApplyLogLine(
            "INFO:     Uvicorn running on http://127.0.0.1:18097 (Press CTRL+C to quit)",
            isStandardError: true, port: 18097);
        Assert.True(machine.ApplyReadiness(true, true, false));
        Assert.Equal(ServerState.Ready, machine.State);

        Assert.False(machine.ApplyReadiness(true, false, false, RuntimeLoadReason));

        Assert.Equal(ServerState.Failed, machine.State);
        Assert.Contains("モデルの読込に失敗＝", machine.FailureReason!, StringComparison.Ordinal);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task 死活_落ちた個体の標本は残さない()
    {
        // low＝`LatestStatus` が前の走行の値のまま残っていた。
        await using var server = new ServerProcess(
            new FreePort(), new ForeignStatus(pid: null), TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(50), null, _ => LiveChild(1));

        var result = await server.StartAsync(
            Request(18097, RuntimeVariants.Cpu, TimeSpan.FromSeconds(2)), CancellationToken.None);
        Assert.True(result.Ok);
        Assert.NotNull(server.LatestStatus);

        await server.StopAsync(CancellationToken.None);
        Assert.Null(server.LatestStatus);
    }

    // ---- medium＝取得の本文の読みに期限が無かった -----------------------------

    [Fact]
    public async Task 取得_本文が進まなければ無通信の期限で打ち切る()
    {
        // 是正前＝Content-Length だけ返して黙る相手に**45 秒待っても返らなかった**（実射）。
        var port = 18096;
        using var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/");
        listener.Start();
        var stop = new CancellationTokenSource();
        var serving = Task.Run(async () =>
        {
            try
            {
                var context = await listener.GetContextAsync();
                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = 400000;
                await context.Response.OutputStream.WriteAsync(new byte[16], stop.Token);
                await context.Response.OutputStream.FlushAsync(stop.Token);
                await Task.Delay(TimeSpan.FromMinutes(1), stop.Token);
            }
            catch (Exception)
            {
                // 相手を落としただけ
            }
        });

        Directory.CreateDirectory(_root);
        var destination = Path.Combine(_root, "stalled.bin");
        using var downloader = new HttpDownloader { IdleTimeout = TimeSpan.FromSeconds(1) };

        var clock = Stopwatch.StartNew();
        var result = await downloader.DownloadAsync(
            new DownloadRequest(
                "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/x.bin",
                null, destination, null, 400000, "黙る相手", 1),
            null,
            CancellationToken.None);
        clock.Stop();

        Assert.False(result.Ok);
        Assert.Contains("1 バイトも進まなかった", result.FailureReason!, StringComparison.Ordinal);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "無通信の期限で打ち切っていない");

        // .part は残す＝次の試行が Range で続きから取る。
        Assert.True(File.Exists(destination + ".part"));

        await stop.CancelAsync();
        listener.Stop();
        await serving;
    }

    // ---- medium＝台帳の例外が UI スレッドへ抜けていた --------------------------

    [Fact]
    public void 初回_sha256の無い台帳でも計画は投げずに理由を返す()
    {
        var paths = LedgerWithoutSha256();

        var plan = FirstRunViewModel.TryPlan(paths, RuntimeVariants.Cpu, false, out var reason);

        Assert.Null(plan);
        Assert.Contains("sha256 の無い item", reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 初回_sha256の無い台帳でも見積りは投げずに不明と名乗る()
    {
        var paths = LedgerWithoutSha256();

        var text = FirstRunViewModel.EstimateSizeText(paths, RuntimeVariants.Cpu);

        // 是正・段 C の検分＝画面は平語 1 行、理由（sha256 の無い item …）は記録へ。
        Assert.Equal(UiStrings.WizardSizeUnknown, text);
        Assert.DoesNotContain("sha256", text, StringComparison.Ordinal);
    }

    // ---- medium＝vc_redist の「判らない」に答える口が無かった（裁定 87 ⑷）-----

    [Fact]
    public async Task vc_redist_判らないときは飛ばす選択で先へ進める()
    {
        var (vm, calls) = WizardWithUnknownVcRedist();
        vm.AskVcRedist = _ => false;                    // 「飛ばす」

        await RunToDownloadAsync(vm);

        Assert.False(Assert.Single(calls));              // 入れ直しには行かない（1 度だけ・force なし）

        // v2.0 段 B＝記録の 1 行も平語にした（`v2-copy.md` §1-8 の :1105-1107）。
        Assert.Contains(
            vm.Trail,
            line => line.Contains("Microsoft の部品を入れずに進みました", StringComparison.Ordinal));
    }

    [Fact]
    public async Task vc_redist_入れると答えたときだけ入れ直す()
    {
        var (vm, calls) = WizardWithUnknownVcRedist();
        vm.AskVcRedist = _ => true;                     // 「入れる」

        await RunToDownloadAsync(vm);

        Assert.Equal(2, calls.Count);
        Assert.True(calls[1]);                          // 2 度目は「判らなくても入れる」で撃つ
        Assert.Contains(vm.Trail, line => line.Contains("入れた", StringComparison.Ordinal));
    }

    [Fact]
    public async Task vc_redist_問う口が無ければ勝手に入れない()
    {
        var (vm, calls) = WizardWithUnknownVcRedist();
        vm.AskVcRedist = null;                          // 窓が差されていない

        await RunToDownloadAsync(vm);

        Assert.False(Assert.Single(calls));

        // v2.0 段 B＝画面には W8 の 3 部品（`v2-spec.md` §3）を出す。**内輪の 1 行は檔へ**＝
        // 「利用者に確かめてください」は VcRedistInstaller の綴りのままログに残る。
        Assert.Equal(FirstRunViewModel.VcRedistFailedLine, vm.Message);
    }

    [Fact]
    public async Task vc_redist_入れると答えたら判らなくても導入まで行く()
    {
        // VcRedistInstaller 側の口（AssumeInstallWhenUnknown）＝既定では止まる。
        var ledger = LedgerReader.ParseVcRedist(
            """
            {"schema":1,"name":"vc-redist","items":[
              {"kind":"installer","name":"vc_redist.x64","version":"14.44.35211.0",
               "url":"https://example.invalid/VC_redist.x64.exe",
               "sha256":"cc0ff0eb1dc3f5188ae6300faef32bf5beeba4bdd6e8e445a9184072096b713b",
               "size":25635768,
               "silent_args":["/install","/quiet","/norestart"],
               "license":"Microsoft Software License Terms"}]}
            """);

        MsvcpState Unreadable() => new(false, null, "アクセスが拒否されました。");
        var downloader = new FakeDownloader(Path.Combine(_root, "vc_redist.x64.exe"));

        var stopped = await new VcRedistInstaller(downloader, (_, _, _) => Task.FromResult(0))
            { StateProbe = Unreadable }
            .EnsureAsync(ledger, _root, null, CancellationToken.None);
        Assert.True(stopped.NeedsUserDecision);
        Assert.Equal(0, downloader.Calls);              // 1 バイトも落とさない

        var installed = await new VcRedistInstaller(downloader, (_, _, _) => Task.FromResult(0))
            { StateProbe = Unreadable, AssumeInstallWhenUnknown = true }
            .EnsureAsync(ledger, _root, null, CancellationToken.None);

        Assert.True(installed.Ok);
        Assert.Equal(VcRedistAction.Install, installed.Action);
        Assert.Equal(1, downloader.Calls);
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ---- 道具 ---------------------------------------------------------------

    private AppPaths LedgerWithoutSha256()
    {
        var ledgerDir = Path.Combine(_root, "app", "ledger");
        Directory.CreateDirectory(ledgerDir);
        File.WriteAllText(
            Path.Combine(ledgerDir, "python-embed.json"),
            """{"schema":1,"items":[{"kind":"archive","name":"python-embed","url":"https://example.invalid/a.zip","sha256":"aa","size":1}]}""");
        File.WriteAllText(
            Path.Combine(ledgerDir, "runtime-cpu.json"),
            """{"schema":1,"items":[{"kind":"wheel","name":"sha256 の無い item","url":"https://example.invalid/b.whl","size":2}]}""");

        return new AppPaths(
            Path.Combine(_root, "install"), Path.Combine(_root, "app"),
            Path.Combine(_root, "runtime"), Path.Combine(_root, "data"), developerMode: true);
    }

    /// <summary>System32 が読めない機体（<see cref="VcRedistAction.Unknown"/>）のウィザード。</summary>
    private (FirstRunViewModel Wizard, List<bool> Calls) WizardWithUnknownVcRedist()
    {
        var appDir = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(appDir, "ledger"));
        Directory.CreateDirectory(Path.Combine(appDir, "licenses"));
        var paths = new AppPaths(
            Path.Combine(_root, "install"), appDir, Path.Combine(_root, "runtime"),
            Path.Combine(_root, "data"), developerMode: true);
        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文");

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var vm = new FirstRunViewModel(
            paths, settings, new JsonSettingsStore(paths.SettingsPath), new DriverRequirement(),
            () => new FakeDownloader(Path.Combine(_root, "unused.bin")),
            static () => null,
            static _ => Task.FromResult(false));

        var calls = new List<bool>();
        vm.VcRedistRunner = (assumeInstall, _, _) =>
        {
            calls.Add(assumeInstall);
            return Task.FromResult<VcRedistResult?>(assumeInstall
                ? new VcRedistResult(true, VcRedistAction.Install, 0, "Visual C++ 再頒布可能パッケージを入れた。", false)
                : new VcRedistResult(
                    false, VcRedistAction.Unknown, null,
                    "System32 の msvcp140.dll を確かめられなかった：アクセスが拒否されました。"
                    + " Visual C++ 再頒布可能パッケージを入れるかどうかは利用者に確かめてください（勝手には入れません）。",
                    false));
        };

        return (vm, calls);
    }

    /// <summary>通知 → 変種 → 取得（＝vc_redist の段）まで進める。</summary>
    private static async Task RunToDownloadAsync(FirstRunViewModel vm)
    {
        vm.Accepted = true;
        await vm.NextAsync();                 // 通知 → 変種
        await vm.NextAsync();                 // 変種 → 取得（vc_redist を通す）
        Assert.Equal(FirstRunStep.Download, vm.Step);
    }

    private static ServerStartRequest Request(int port, string variant, TimeSpan? readyTimeout = null) =>
        new(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Path.GetTempPath(),
            "127.0.0.1",
            port,
            new Dictionary<string, string>(StringComparer.Ordinal),
            readyTimeout ?? TimeSpan.FromMilliseconds(300))
        {
            Variant = variant,
            InstalledVariants = CudaRelease,
        };

    private static ProcessStartInfo LiveChild(int seconds) => Cmd(
        "1>&2 echo ywk_server 0.1.0 upstream=8224daf/841fb7c & ping -n "
        + (seconds + 1).ToString(CultureInfo.InvariantCulture) + " 127.0.0.1 >nul");

    private static ProcessStartInfo Cmd(string command)
    {
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            WorkingDirectory = Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add("/c");
        info.ArgumentList.Add(command);
        return info;
    }

    private sealed class FreePort : IPortProbe
    {
        public bool IsFree(string host, int port) => true;
    }

    private sealed class NeverReachable : IReadinessProbe
    {
        public Task<ReadinessSample> ProbeAsync(Uri baseAddress, CancellationToken cancellationToken) =>
            Task.FromResult(new ReadinessSample(false, false, false, null, "誰も居ません。"));
    }

    /// <summary>載っている個体の応答（<paramref name="pid"/> が null＝pid の無い古い wrapper）。</summary>
    private sealed class ForeignStatus(int? pid) : IReadinessProbe
    {
        public Task<ReadinessSample> ProbeAsync(Uri baseAddress, CancellationToken cancellationToken)
        {
            var status = new StatusResponse
            {
                Engine = "irodori-ywk",
                Pid = pid,
                Runtime = new StatusRuntime { Loaded = true, Loading = false },
                Memory = new MemoryStatus { Device = "cuda:0", AllocatedBytes = 1 },
            };

            return Task.FromResult(new ReadinessSample(true, true, false, status, null));
        }
    }

    /// <summary>読込に失敗した個体（裁定 105 ⑴＝200 のまま <c>runtime.error</c> に理由 1 行）。</summary>
    private const string RuntimeLoadReason = "FileNotFoundError: Checkpoint not found: <path>";

    private sealed class LoadFailedStatus(string reason) : IReadinessProbe
    {
        public Task<ReadinessSample> ProbeAsync(Uri baseAddress, CancellationToken cancellationToken)
        {
            var status = new StatusResponse
            {
                Engine = "irodori-ywk",
                Runtime = new StatusRuntime { Loaded = false, Loading = false, Error = reason },
            };

            return Task.FromResult(
                new ReadinessSample(true, false, false, status, null) { RuntimeError = reason });
        }
    }

    private sealed class ThrowingProbe(Exception exception) : ITorchProbe
    {
        public Task<TorchGpuProbe.TorchProbeResult> ProbeAsync(
            string pythonExe, TimeSpan timeout, CancellationToken cancellationToken) => throw exception;
    }

    private sealed class FixedProbe(TorchGpuProbe.TorchProbeResult result) : ITorchProbe
    {
        public Task<TorchGpuProbe.TorchProbeResult> ProbeAsync(
            string pythonExe, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    /// <summary>落としたことにする取得器（<b>ネットへ出ない</b>）。</summary>
    private sealed class FakeDownloader(string path) : IDownloader
    {
        public int Calls { get; private set; }

        public Task<DownloadResult> DownloadAsync(
            DownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [0]);
            return Task.FromResult(new DownloadResult(true, path, 1, "00", request?.Url, false, false, 1, null));
        }

        public async Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(
            IReadOnlyList<DownloadRequest> requests,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            var results = new List<DownloadResult>();
            foreach (var request in requests ?? [])
            {
                results.Add(await DownloadAsync(request, progress, cancellationToken));
            }

            return results;
        }
    }
}
