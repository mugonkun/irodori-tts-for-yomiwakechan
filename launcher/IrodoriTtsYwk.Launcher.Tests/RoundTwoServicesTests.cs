using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Ledger;
using IrodoriTtsYwk.Launcher.Services.Server;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 便 D（2）＝起動席（LA）の釘。裁定 87 ⑶・88 ⑴〜⑶ と low 1〜9 を、
/// <b>実 GPU・実サーバ・停止域のポート（8088／7861／18088）に触れずに</b>押さえる。
/// <para>
/// 子プロセスが要る 3 本だけ <c>cmd.exe</c> の偽の子を起こす（<b>ネットへは 1 バイトも出ない</b>・
/// <c>ping</c> の宛先は <c>127.0.0.1</c>）。HTTP は <see cref="TestHttpServer"/>（localhost）だけ。
/// </para>
/// </summary>
public sealed class RoundTwoServicesTests(Xunit.Abstractions.ITestOutputHelper output)
{
    // ---- 裁定 88 ⑴⑵＝変種の門（VariantGate は純関数） ------------------------

    /// <summary>この機体（Radeon gfx1151・便 C の実測）の検分。</summary>
    private static TorchGpuProbe.TorchProbeResult RocmProbe(bool usable = true) => new(
        usable
            ? [new GpuInfo("30303030-3031-3937-3030-303030303030", "AMD Radeon(TM) 8060S Graphics",
                0, 107090132992L, "197", "gfx1151", null, GpuSource.TorchProbe)]
            : [],
        "2.13.0+rocm10.0.0",
        null,
        "7.15.26333",
        null,
        usable,
        usable ? 1 : 0);

    /// <summary>CUDA ビルドの検分（<paramref name="usable"/> 偽＝裁定 83 の 537.58／cu130）。</summary>
    private static TorchGpuProbe.TorchProbeResult CudaProbe(bool usable, string cuda = "13.0") => new(
        usable
            ? [new GpuInfo("GPU-19adfe89-c9e0-df55-4a8a-e31798717a36", "NVIDIA GeForce RTX 3090",
                0, 25769803776L, "00000000:01:00.0", null, "591.86", GpuSource.TorchProbe)]
            : [],
        "2.10.0+cu" + cuda.Replace(".", string.Empty, StringComparison.Ordinal),
        cuda,
        null,
        null,
        usable,
        usable ? 1 : 0);

    private static readonly string[] CudaRelease = [RuntimeVariants.Cu130, RuntimeVariants.Cu126, RuntimeVariants.Cpu];

    private static readonly string[] RadeonRelease = [RuntimeVariants.RocmGfx1151, RuntimeVariants.Cpu];

    [Fact]
    public void 門_Radeon機でcu130を選んだら起こさずrocmを勧める()
    {
        // AMD 機なので nvidia-smi は無い＝driver_version は読めない。
        // cu130 の実行系で撃った torch は GPU を 1 台も見られない。
        var decision = VariantGate.Decide(
            RuntimeVariants.Cu130, CudaProbe(usable: false), null, RadeonRelease);

        Assert.False(decision.Allow);
        Assert.Equal(RuntimeVariants.RocmGfx1151, decision.SuggestedVariant);
        Assert.NotNull(decision.Reason);
        Assert.Contains("GPU を見られません", decision.Reason!, StringComparison.Ordinal);
        Assert.Contains("is_available=False", decision.Reason!, StringComparison.Ordinal);
        Assert.Contains("device_count=0", decision.Reason!, StringComparison.Ordinal);
        Assert.Contains("rocm-gfx1151 か cpu の変種に切り替えてください", decision.Reason!, StringComparison.Ordinal);
        Assert.Empty(decision.Notices);
    }

    [Fact]
    public void 門_537_58でcu130は起こさずcu126を勧める()
    {
        // 裁定 83・88 ⑴ の例文の形＝「cu130 はこの機体で GPU を見られません（…）。
        // cu126 か cpu の変種に切り替えてください」。
        var decision = VariantGate.Decide(
            RuntimeVariants.Cu130, CudaProbe(usable: false), "537.58", CudaRelease);

        Assert.False(decision.Allow);
        Assert.Equal(RuntimeVariants.Cu126, decision.SuggestedVariant);
        Assert.StartsWith("cu130 はこの機体で GPU を見られません（", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("ドライバ 537.58", decision.Reason!, StringComparison.Ordinal);
        Assert.EndsWith("cu126 か cpu の変種に切り替えてください。", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void 門_537_58でcu126は通り注意も出ない()
    {
        // 裁定 80＝537.58 は cu126 を実射で通した版そのもの（未実測の帯の上端）。
        var decision = VariantGate.Decide(
            RuntimeVariants.Cu126, CudaProbe(usable: true, cuda: "12.6"), "537.58", CudaRelease);

        Assert.True(decision.Allow);
        Assert.Null(decision.Reason);
        Assert.Empty(decision.Notices);
    }

    [Fact]
    public void 門_530でcu126は通るが未実測の帯を1行告げる()
    {
        // 裁定 88 ⑵＝528.33 ≤ v < 537.58 は合成を許して注意だけ出す。
        var decision = VariantGate.Decide(
            RuntimeVariants.Cu126, CudaProbe(usable: true, cuda: "12.6"), "530", CudaRelease);

        Assert.True(decision.Allow);
        Assert.Null(decision.Reason);
        var notice = Assert.Single(decision.Notices);
        Assert.Contains("未実測の帯", notice, StringComparison.Ordinal);
        Assert.Contains("537.58", notice, StringComparison.Ordinal);
        Assert.Contains("528.33", notice, StringComparison.Ordinal);
    }

    [Fact]
    public void 門_591_86でcu130は素通しする()
    {
        // 便 B の本測定の機体（裁定 75）。
        var decision = VariantGate.Decide(
            RuntimeVariants.Cu130, CudaProbe(usable: true), "591.86", CudaRelease);

        Assert.True(decision.Allow);
        Assert.Null(decision.Reason);
        Assert.Null(decision.SuggestedVariant);
        Assert.Empty(decision.Notices);
    }

    [Fact]
    public void 門_NVIDIAが居ない機体のcu126はcpuを勧める()
    {
        // ドライバも読めず GPU 変種も他に組んでいない＝勧め先は cpu しかない（裁定 88 ⑴）。
        var decision = VariantGate.Decide(
            RuntimeVariants.Cu126, CudaProbe(usable: false, cuda: "12.6"), null, CudaRelease);

        Assert.False(decision.Allow);
        Assert.Equal(RuntimeVariants.Cpu, decision.SuggestedVariant);
        Assert.Contains("cpu の変種に切り替えてください", decision.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 門_cpu変種は門を通さない()
    {
        // 見る GPU が無いので検分の結果に関わらず通す（裁定 88 ⑴）。
        var decision = VariantGate.Decide(
            RuntimeVariants.Cpu, CudaProbe(usable: false), "400.00", CudaRelease);

        Assert.True(decision.Allow);
        Assert.Null(decision.Reason);
        Assert.Empty(decision.Notices);
    }

    [Fact]
    public void 門_下限に届かないドライバは検分が通っても止める()
    {
        // 検分が「使える」と言っても、閾に届かなければ止める（裁定 88 ⑵）。
        var decision = VariantGate.Decide(
            RuntimeVariants.Cu126, CudaProbe(usable: true, cuda: "12.6"), "520.00", CudaRelease);

        Assert.False(decision.Allow);
        Assert.Contains("下限 528.33 に届きません", decision.Reason!, StringComparison.Ordinal);
        Assert.Equal(RuntimeVariants.Cpu, decision.SuggestedVariant);
    }

    [Fact]
    public void 門_検分が撃てなければ止めずに1行だけ告げる()
    {
        // 実行系が無い＝「GPU が無い」ではない。黙って通しも止めもせず、注意を残して起こす。
        var decision = VariantGate.Decide(RuntimeVariants.RocmGfx1151, null, null, RadeonRelease);

        Assert.True(decision.Allow);
        Assert.Single(decision.Notices);
        Assert.Contains("検分ができませんでした", decision.Notices[0], StringComparison.Ordinal);
    }

    [Fact]
    public void 門_torchが落ちた検分は止めずに理由を添えて通す()
    {
        var broken = new TorchGpuProbe.TorchProbeResult(
            [], null, null, null, "ImportError: DLL load failed");

        var decision = VariantGate.Decide(RuntimeVariants.RocmGfx1151, broken, null, RadeonRelease);

        Assert.True(decision.Allow);
        Assert.Contains("ImportError", Assert.Single(decision.Notices), StringComparison.Ordinal);
    }

    [Fact]
    public void 門_rocmが自分で落ちたらrocmは勧めない()
    {
        var decision = VariantGate.Decide(
            RuntimeVariants.RocmGfx1151, RocmProbe(usable: false), null, RadeonRelease);

        Assert.False(decision.Allow);
        Assert.Equal(RuntimeVariants.Cpu, decision.SuggestedVariant);
        Assert.Contains("torch hip 7.15.26333", decision.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 門_未実測の帯は528_33以上537_58未満だけ()
    {
        Assert.True(VariantGate.IsUnmeasuredBand(RuntimeVariants.Cu126, "528.33"));
        Assert.True(VariantGate.IsUnmeasuredBand(RuntimeVariants.Cu126, "537.57"));
        Assert.False(VariantGate.IsUnmeasuredBand(RuntimeVariants.Cu126, "537.58"));
        Assert.False(VariantGate.IsUnmeasuredBand(RuntimeVariants.Cu126, "528.32"));
        Assert.False(VariantGate.IsUnmeasuredBand(RuntimeVariants.Cu130, "530"));
        Assert.False(VariantGate.IsUnmeasuredBand(RuntimeVariants.Cu126, null));
    }

    [Fact]
    public void 門_組んである変種は台帳の在否で数える()
    {
        var dir = TestArchives.NewTempDir("ledger");
        try
        {
            File.WriteAllText(Path.Combine(dir, "runtime-cpu.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "runtime-rocm-gfx1151.json"), "{}");

            var installed = VariantGate.DetectInstalled(dir);

            Assert.Equal([RuntimeVariants.RocmGfx1151, RuntimeVariants.Cpu], installed);
        }
        finally
        {
            TestArchives.Remove(dir);
        }
    }

    // ---- 裁定 88 ⑴＝検分の読み（is_available と device_count は別物） ----------

    [Fact]
    public void 検分_is_availableとdevice_countを別々に読む()
    {
        // 裁定 83 の実射（537.58／cu130）＝import は通り、数えられない。
        var probe = TorchGpuProbe.Parse(
            "YWK_GPU_JSON {\"ok\": true, \"torch\": \"2.10.0+cu130\", \"cuda\": \"13.0\", "
            + "\"hip\": null, \"available\": false, \"count\": 0, \"devices\": [], \"error\": null}");

        Assert.Null(probe.Error);
        Assert.True(probe.Observed);
        Assert.False(probe.Available);
        Assert.Equal(0, probe.DeviceCount);
        Assert.False(probe.GpuUsable);
    }

    [Fact]
    public void 検分_availableを載せない古い出力も読める()
    {
        // 便 D 1 巡目の台本は available を載せない＝台数から読む（欄の無い出力で黙って
        // 「GPU が無い」と断じない）。
        var probe = TorchGpuProbe.Parse(
            "YWK_GPU_JSON {\"ok\": true, \"torch\": \"2.13.0+rocm10.0.0\", \"cuda\": null, "
            + "\"hip\": \"7.15.26333\", \"count\": 1, \"devices\": [{\"index\": 0, \"name\": \"AMD\", "
            + "\"uuid\": \"3030\", \"total_memory\": 1, \"pci_bus_id\": \"197\", "
            + "\"gcn_arch\": \"gfx1151\"}], \"error\": null}");

        Assert.True(probe.Available);
        Assert.Equal(1, probe.DeviceCount);
        Assert.True(probe.GpuUsable);
    }

    [Fact]
    public void 検分_台本は今もASCIIだけ()
    {
        foreach (var c in TorchGpuProbe.Script)
        {
            Assert.True(c < 128, "台本は ASCII 限定");
        }
    }

    // ---- 裁定 88 ⑵＝公開した口（Notices・LatestStatus・StatusSampled） ---------

    [Fact]
    public void 口_ServerStartResultのNoticesは既定で空でありnullにならない()
    {
        var bare = new ServerStartResult(true, ServerState.Ready, 1, null, TimeSpan.Zero, null);
        Assert.Empty(bare.Notices);

        var carried = bare with { Notices = new[] { "未実測の帯です。" } };
        Assert.Single(carried.Notices);
        Assert.Empty(bare.Notices); // 元は変わらない（record の写し）
    }

    [Fact]
    public void 口_起動要求は変種とドライバと導入済み変種を運ぶ()
    {
        var request = new ServerStartRequest(
            @"C:\rt\python.exe", @"C:\app\server", "127.0.0.1", 18097,
            new Dictionary<string, string>(StringComparer.Ordinal),
            TimeSpan.FromSeconds(120))
        {
            Variant = RuntimeVariants.Cu126,
            DriverVersion = "537.58",
            InstalledVariants = CudaRelease,
        };

        Assert.Equal(RuntimeVariants.Cu126, request.Variant);
        Assert.Equal("537.58", request.DriverVersion);
        Assert.Equal(3, request.InstalledVariants.Count);

        // 既定は空＝門を通さない要求（1 巡目の呼び手を壊さない）
        var bare = new ServerStartRequest(
            @"C:\rt\python.exe", @"C:\app\server", "127.0.0.1", 18097,
            new Dictionary<string, string>(StringComparer.Ordinal),
            TimeSpan.FromSeconds(120));
        Assert.Equal(string.Empty, bare.Variant);
        Assert.Empty(bare.InstalledVariants);
    }

    [Fact]
    public async Task 口_見張りの標本がLatestStatusとStatusSampledに出る()
    {
        var status = new StatusResponse { Engine = "irodori-tts-ywk", Runtime = new StatusRuntime { Loaded = true } };
        var readiness = new FixedReadiness(new ReadinessSample(true, true, false, status, null));

        var seen = new List<StatusResponse>();
        await using var server = NewServer(readiness, LiveChild(seconds: 6));
        server.StatusSampled += (_, s) => seen.Add(s);

        var result = await server.StartAsync(Request(18097), CancellationToken.None);

        Assert.True(result.Ok, result.FailureReason);
        Assert.Same(status, server.LatestStatus);
        Assert.NotEmpty(seen);
        Assert.Same(status, seen[0]);

        await server.StopAsync(CancellationToken.None);
    }

    // ---- 裁定 88 ⑶＝死活（Process.Exited で 1 秒以内に Failed へ） --------------

    [Fact]
    public async Task 死活_待機の後に子が消えたら1秒以内にFailedへ落ちる()
    {
        // 見張りの間隔をわざと 30 秒にする＝**標本では気づけない**状況で、
        // Process.Exited だけが Failed を立てることを見る（裁定 88 ⑶）。
        var readiness = new FixedReadiness(new ReadinessSample(
            true, true, false, new StatusResponse { Runtime = new StatusRuntime { Loaded = true } }, null));

        var dying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<ServerStateChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dyingAt = 0L;

        await using var server = new ServerProcess(
            new FreePort(),
            readiness,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(30),
            torchProbe: null,
            startInfoFactory: _ => Cmd(
                "1>&2 echo ywk_server 0.1.0 upstream=8224daf/841fb7c & "
                + "ping -n 3 127.0.0.1 >nul & "
                + "1>&2 echo ywk_server: the runtime went away & exit /b 3"));

        server.LogLine += (_, e) =>
        {
            if (e.Event.Line.Contains("went away", StringComparison.Ordinal))
            {
                dyingAt = Stopwatch.GetTimestamp();
                dying.TrySetResult();
            }
        };
        server.StateChanged += (_, e) =>
        {
            if (e.Current == ServerState.Failed)
            {
                failed.TrySetResult(e);
            }
        };

        var result = await server.StartAsync(Request(18098), CancellationToken.None);
        Assert.True(result.Ok, result.FailureReason);
        Assert.Equal(ServerState.Ready, server.State);

        await dying.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var change = await failed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var latency = Stopwatch.GetElapsedTime(dyingAt);

        output.WriteLine(
            "死活の遅れ＝" + latency.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)
            + " ms（見張りの間隔は 30,000 ms）");
        Assert.True(
            latency < TimeSpan.FromSeconds(1),
            "子が消えてから " + latency.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms");
        Assert.Equal(3, server.ExitCode);

        // 理由 1 行＝ServerExitCodes.Describe ＋ stderr の末尾 1 行（low 2）
        Assert.Contains("モデルの読み込みに失敗した。", change.Reason!, StringComparison.Ordinal);
        Assert.Contains("the runtime went away", change.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 死活_即死はstderrを読み切ってから理由を組む()
    {
        // low 2＝WaitForExit(int) は非同期の読みを待たないので、待たずに理由を組むと
        // 「起動前の検査で止まった。」だけになり、肝心の 1 行が落ちる。
        await using var server = NewServer(
            new FixedReadiness(new ReadinessSample(false, false, false, null, "まだ")),
            Cmd("1>&2 echo ywk_server: IRODORI_MODEL_DEVICE=cuda:9 is out of range & exit /b 2"));

        var result = await server.StartAsync(Request(18099), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(ServerState.Failed, result.State);
        Assert.Equal(ServerExitCodes.WrapperPrecheck, result.ExitCode);
        Assert.Contains("起動前の検査で止まった", result.FailureReason!, StringComparison.Ordinal);
        Assert.Contains("cuda:9 is out of range", result.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 死活_自分で止めた個体の断末魔はFailedにしない()
    {
        var readiness = new FixedReadiness(new ReadinessSample(
            true, true, false, new StatusResponse { Runtime = new StatusRuntime { Loaded = true } }, null));

        await using var server = NewServer(readiness, LiveChild(seconds: 20));

        var result = await server.StartAsync(Request(18097), CancellationToken.None);
        Assert.True(result.Ok, result.FailureReason);

        await server.StopAsync(CancellationToken.None);
        Assert.Equal(ServerState.Stopped, server.State);

        // Exited は非同期に上がる＝少し待っても Stopped のままであること
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        Assert.Equal(ServerState.Stopped, server.State);
        Assert.Null(server.FailureReason);
    }

    // ---- low 1＝ready 待ちの取消 ----------------------------------------------

    [Fact]
    public async Task low1_取消は子を落として起動を中止しましたで返る()
    {
        await using var server = NewServer(
            new FixedReadiness(new ReadinessSample(false, false, false, null, "まだ")),
            LiveChild(seconds: 30));

        using var cts = new CancellationTokenSource();
        var start = server.StartAsync(Request(18098), cts.Token);

        var pid = await WaitForPidAsync(server);
        await cts.CancelAsync();

        // **例外では抜けない**（契約＝StartAsync は結末で返す）
        var result = await start.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.False(result.Ok);
        Assert.Equal(ServerState.Stopped, result.State);
        output.WriteLine("取消からの結末＝" + result.Elapsed.TotalMilliseconds
            .ToString("0", CultureInfo.InvariantCulture) + " ms（pid "
            + pid.ToString(CultureInfo.InvariantCulture) + " は片付いた）");
        Assert.Equal("起動を中止しました。", result.FailureReason);
        Assert.Equal(ServerState.Stopped, server.State);
        Assert.Null(server.ProcessId);

        // 孤児を残さない
        Assert.True(HasGone(pid), "pid " + pid.ToString(CultureInfo.InvariantCulture) + " が残っている");
    }

    // ---- low 4＝tar の檔名（先頭の点を削らない・外を指す名は投げる） -------------

    [Fact]
    public async Task low4_点で始まる檔名は名を保つ()
    {
        var root = TestArchives.NewTempDir("tar-dot");
        try
        {
            var archive = TestArchives.WriteTarGz(
                Path.Combine(root, "pkg.tar.gz"),
                new Dictionary<string, string>
                {
                    ["pkg-1.0/.gitignore"] = "*.pyc\n",
                    ["./pkg-1.0/pkg/__init__.py"] = "x = 1\n",
                });

            var into = Path.Combine(root, "out");
            var result = await ArchiveExtractor.ExtractTarGzAsync(archive, into, CancellationToken.None);

            Assert.Equal(2, result.Files);
            Assert.True(File.Exists(Path.Combine(into, "pkg-1.0", ".gitignore")),
                "先頭の点まで削ると gitignore という別の檔になる");
            Assert.False(File.Exists(Path.Combine(into, "pkg-1.0", "gitignore")));
            Assert.True(File.Exists(Path.Combine(into, "pkg-1.0", "pkg", "__init__.py")));
        }
        finally
        {
            TestArchives.Remove(root);
        }
    }

    [Fact]
    public async Task low4_展開先の外を指すtarは投げる()
    {
        var root = TestArchives.NewTempDir("tar-escape");
        try
        {
            var archive = TestArchives.WriteTarGz(
                Path.Combine(root, "evil.tar.gz"),
                new Dictionary<string, string> { ["../evil.py"] = "import os\n" });

            var into = Path.Combine(root, "out");

            // 名を削って「直す」のではなく、外を指す名として投げる（zip slip）
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => ArchiveExtractor.ExtractTarGzAsync(archive, into, CancellationToken.None));
            Assert.Contains("展開先の外", ex.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root, "evil.py")));
        }
        finally
        {
            TestArchives.Remove(root);
        }
    }

    // ---- low 5・6＝取得（Content-Range と sha256 の無い item） -------------------

    [Fact]
    public async Task low5_206の位置が頼んだ所と違えば捨てて0から取り直す()
    {
        var body = TestHttpServer.Payload(128 * 1024, 5);
        var dir = TestArchives.NewTempDir("range");
        try
        {
            using var server = new TestHttpServer();
            server.Map("odd.bin", (attempt, context) =>
            {
                if (attempt == 1)
                {
                    // 途中で切れる＝.part が残る
                    context.Response.StatusCode = 200;
                    context.Response.ContentLength64 = body.Length;
                    context.Response.OutputStream.Write(body, 0, 4096);
                    context.Response.OutputStream.Flush();
                    context.Response.Abort();
                    return Task.CompletedTask;
                }

                // 2 回目＝Range を丸めて「0 から」の 206 を返す（CDN が範囲を丸めた形）
                context.Response.StatusCode = 206;
                context.Response.Headers["Content-Range"] = string.Create(
                    CultureInfo.InvariantCulture, $"bytes 0-{body.Length - 1}/{body.Length}");
                context.Response.ContentLength64 = body.Length;
                context.Response.OutputStream.Write(body, 0, body.Length);
                context.Response.Close();
                return Task.CompletedTask;
            });

            using var downloader = new HttpDownloader(HttpDownloader.CreateDefaultClient(), ownsClient: true)
            {
                ProgressInterval = TimeSpan.Zero,
            };

            var destination = Path.Combine(dir, "odd.bin");
            var result = await downloader.DownloadAsync(
                new DownloadRequest(server.Url("odd.bin"), null, destination,
                    TestArchives.Sha256Of(body), body.Length, "odd", MaxAttempts: 2),
                null,
                CancellationToken.None);

            // 2 回で打ち切ってあるので結末は失敗＝**そのときの理由が Content-Range の食い違い**
            Assert.False(result.Ok);
            Assert.Contains("Content-Range", result.FailureReason!, StringComparison.Ordinal);

            // 壊れた途中檔を残さない（次回が 416 で詰まる形を作らない）
            Assert.False(File.Exists(destination + ".part"));
            Assert.False(File.Exists(destination));
        }
        finally
        {
            TestArchives.Remove(dir);
        }
    }

    [Fact]
    public async Task low6_sha256の無いitemは長さで見る()
    {
        var body = TestHttpServer.Payload(8 * 1024, 9);
        var dir = TestArchives.NewTempDir("size");
        try
        {
            using var server = new TestHttpServer();
            server.MapBytes("blob.bin", body);

            var destination = Path.Combine(dir, "blob.bin");

            // 途中で切れた檔が居座っている（sha256 が無いので中身では判らない）
            await File.WriteAllBytesAsync(destination, body[..1024]);

            using var downloader = new HttpDownloader(HttpDownloader.CreateDefaultClient(), ownsClient: true)
            {
                ProgressInterval = TimeSpan.Zero,
            };

            var result = await downloader.DownloadAsync(
                new DownloadRequest(server.Url("blob.bin"), null, destination, null, body.Length, "blob"),
                null,
                CancellationToken.None);

            Assert.True(result.Ok, result.FailureReason);
            Assert.False(result.FromCache);
            Assert.Equal(1, server.Hits("blob.bin"));
            Assert.Equal(body, await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            TestArchives.Remove(dir);
        }
    }

    [Fact]
    public void low6_sha256の無いitemは計画の段で弾く()
    {
        var embed = new LedgerFile
        {
            Items = [new LedgerItem { Kind = LedgerItemKinds.PythonEmbed, Name = "python", Url = "u", Sha256 = "aa" }],
        };
        var runtime = new LedgerFile
        {
            Items =
            [
                new LedgerItem { Kind = LedgerItemKinds.Wheel, Name = "good", Url = "u", Sha256 = "bb" },
                new LedgerItem { Kind = LedgerItemKinds.Wheel, Name = "nameless", Url = "u" },
            ],
        };

        var ex = Assert.Throws<LedgerException>(
            () => FetchPlanner.Plan(RuntimeVariants.Cpu, embed, runtime, null, null));

        Assert.Contains("sha256 の無い item", ex.Message, StringComparison.Ordinal);
        Assert.Contains("nameless", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void low6_台帳の読みもsha256の無いitemを弾く()
    {
        // LedgerReader.Validate は assemble-runtime.ps1 の "refusing to install it" と同じ規律
        var problems = LedgerReader.Validate(new LedgerFile
        {
            Items = [new LedgerItem { Kind = LedgerItemKinds.Wheel, Name = "nameless", Url = "u", License = "MIT" }],
        });

        Assert.Contains(problems, p => p.Contains("sha256 が無い", StringComparison.Ordinal));
    }

    // ---- low 7＝vc_redist の Unknown --------------------------------------------

    [Fact]
    public async Task low7_判らないときは入れずに確認が要ると返す()
    {
        var downloader = new CountingDownloader();
        var ran = 0;
        var installer = new VcRedistInstaller(
            downloader,
            (_, _, _) =>
            {
                ran++;
                return Task.FromResult(0);
            })
        {
            // System32 を読もうとして落ちた＝「無い」ではない（裁定 87 ⑷）
            StateProbe = () => new MsvcpState(false, null, "アクセスが拒否されました。"),
        };

        var result = await installer.EnsureAsync(
            Ledger.VcRedist(), TestArchives.NewTempDir("vc"), null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(VcRedistAction.Unknown, result.Action);
        Assert.True(result.NeedsUserDecision);
        Assert.Contains("利用者に確かめて", result.Message, StringComparison.Ordinal);

        // **1 バイトも落とさず UAC の窓も出さない**
        Assert.Equal(0, downloader.Calls);
        Assert.Equal(0, ran);
    }

    [Fact]
    public async Task low7_引数は台帳のsilent_argsを使う()
    {
        IReadOnlyList<string>? used = null;
        var installer = new VcRedistInstaller(
            new CountingDownloader(),
            (_, arguments, _) =>
            {
                used = arguments;
                return Task.FromResult(0);
            })
        {
            StateProbe = () => new MsvcpState(false, null, null),
        };

        var result = await installer.EnsureAsync(
            Ledger.VcRedist(), TestArchives.NewTempDir("vc"), null, CancellationToken.None);

        Assert.True(result.Ok, result.Message);

        // 裁定 87 ⑷＝台帳（/install /quiet /norestart）が正・設計書 §6 の /passive ではない
        Assert.Equal(["/install", "/quiet", "/norestart"], used);
    }

    // ---- low 8・9＝組み上げ（先に一巡・.data の残骸） ---------------------------

    [Fact]
    public async Task low8_原檔が欠けていたら既存の実行系を消さない()
    {
        var root = TestArchives.NewTempDir("preflight");
        try
        {
            var cache = Path.Combine(root, "cache");
            var runtimeDir = Path.Combine(root, "runtime-cpu");
            var appDir = Path.Combine(root, "app");
            Directory.CreateDirectory(cache);
            Directory.CreateDirectory(runtimeDir);

            // 「いま動いている実行系」の印
            var marker = Path.Combine(runtimeDir, "python.exe");
            await File.WriteAllTextAsync(marker, "MZ");

            TestArchives.WritePythonEmbedZip(Path.Combine(cache, "python-embed.zip"));

            var ledger = new LedgerFile
            {
                Items =
                [
                    Ledger.Cached(cache, LedgerItemKinds.PythonEmbed, "python", "python-embed.zip"),

                    // cache に無い item＝ここで気づかなければ、消した後に気づくことになる
                    new LedgerItem
                    {
                        Kind = LedgerItemKinds.Wheel, Name = "missing", Url = "u",
                        Filename = "missing-1.0-py3-none-any.whl", Sha256 = "cc", License = "MIT",
                    },
                ],
            };

            var result = await new WheelInstaller(new PthWriter()).InstallAsync(
                new InstallRequest(ledger, cache, runtimeDir, appDir, Ledger.PthTemplate(appDir)),
                null,
                CancellationToken.None);

            Assert.False(result.Ok);
            Assert.Contains("missing", result.FailureReason!, StringComparison.Ordinal);
            Assert.True(File.Exists(marker), "組み始める前に気づけば、動いていた実行系は無傷のままである");
        }
        finally
        {
            TestArchives.Remove(root);
        }
    }

    [Fact]
    public async Task low9_畳めなかったdataの残骸は理由に載せて落とす()
    {
        var root = TestArchives.NewTempDir("residue");
        try
        {
            var cache = Path.Combine(root, "cache");
            var runtimeDir = Path.Combine(root, "runtime-cpu");
            var appDir = Path.Combine(root, "app");
            Directory.CreateDirectory(cache);

            TestArchives.WritePythonEmbedZip(Path.Combine(cache, "python-embed.zip"));
            TestArchives.WriteZip(
                Path.Combine(cache, "demo-1.0-py3-none-any.whl"),
                new Dictionary<string, string>
                {
                    ["demo/__init__.py"] = "x = 1\n",
                    ["demo-1.0.dist-info/METADATA"] = "Name: demo\nVersion: 1.0\n",

                    // purelib／platlib／scripts／headers／data のどれでもない小分類
                    ["demo-1.0.data/oddlib/thing.py"] = "y = 2\n",
                });

            var ledger = new LedgerFile
            {
                Items =
                [
                    Ledger.Cached(cache, LedgerItemKinds.PythonEmbed, "python", "python-embed.zip"),
                    Ledger.Cached(cache, LedgerItemKinds.Wheel, "demo", "demo-1.0-py3-none-any.whl"),
                ],
            };

            var result = await new WheelInstaller(new PthWriter()).InstallAsync(
                new InstallRequest(ledger, cache, runtimeDir, appDir, Ledger.PthTemplate(appDir)),
                null,
                CancellationToken.None);

            Assert.False(result.Ok);
            Assert.Equal(["demo-1.0.data"], result.DataResidue);
            Assert.Contains("demo-1.0.data", result.FailureReason!, StringComparison.Ordinal);
        }
        finally
        {
            TestArchives.Remove(root);
        }
    }

    // ---- 裁定 87 ⑶＝位置レコードの [JsonPropertyName] --------------------------

    /// <summary>
    /// <b>命名方針に頼らない読み</b>（<c>SnakeCaseLower</c> を外した素の options）で読める。
    /// 属性が無ければ <c>shots_total</c>／<c>cancel_requested</c> は黙って null になる。
    /// </summary>
    [Fact]
    public void 契約_位置レコード4つは綴りを自分で名乗る()
    {
        var plain = new JsonSerializerOptions();

        var warmup = JsonSerializer.Deserialize<WarmupStartResult>(
            """{"id":"w-1","shots_total":6,"state":"running"}""", plain);
        Assert.Equal("w-1", warmup!.Id);
        Assert.Equal(6, warmup.ShotsTotal);
        Assert.Equal("running", warmup.State);

        var precompute = JsonSerializer.Deserialize<PrecomputeStartResult>(
            """{"id":"p-1","total":11,"state":"running"}""", plain);
        Assert.Equal("p-1", precompute!.Id);
        Assert.Equal(11, precompute.Total);

        var cancel = JsonSerializer.Deserialize<CancelResult>(
            """{"id":"w-1","state":"cancelled","cancel_requested":true}""", plain);
        Assert.Equal("cancelled", cancel!.State);
        Assert.True(cancel.CancelRequested);

        var drop = JsonSerializer.Deserialize<DropLatentResult>(
            """{"id":"琴葉茜","state":"reverted","alias":"ref_wav","removed":["latents/ab.pt"]}""", plain);
        Assert.Equal("琴葉茜", drop!.Id);
        Assert.Equal("reverted", drop.State);
        Assert.Equal("ref_wav", drop.Alias);
        Assert.Equal(["latents/ab.pt"], drop.Removed);
    }

    // ---- 道具 ------------------------------------------------------------------

    private static ServerStartRequest Request(int port) => new(
        @"C:\nonexistent\python.exe",
        Path.GetTempPath(),
        "127.0.0.1",
        port,
        new Dictionary<string, string>(StringComparer.Ordinal),
        TimeSpan.FromSeconds(20));

    private static ServerProcess NewServer(IReadinessProbe readiness, ProcessStartInfo child) =>
        new(new FreePort(), readiness, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200),
            torchProbe: null, startInfoFactory: _ => child);

    /// <summary>その秒数だけ生きて 0 で抜ける偽の子（<c>ping</c> の宛先は <c>127.0.0.1</c>）。</summary>
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

    private static async Task<int> WaitForPidAsync(ServerProcess server)
    {
        for (var i = 0; i < 200; i++)
        {
            if (server.ProcessId is int pid)
            {
                return pid;
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException("子プロセスが起きなかった。");
    }

    private static bool HasGone(int pid)
    {
        for (var i = 0; i < 40; i++)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true; // もう居ない
            }

            Thread.Sleep(50);
        }

        return false;
    }

    private sealed class FreePort : IPortProbe
    {
        public bool IsFree(string host, int port) => true;
    }

    private sealed class FixedReadiness(ReadinessSample sample) : IReadinessProbe
    {
        public Task<ReadinessSample> ProbeAsync(Uri baseAddress, CancellationToken cancellationToken) =>
            Task.FromResult(sample);
    }

    private sealed class CountingDownloader : IDownloader
    {
        public int Calls { get; private set; }

        public Task<DownloadResult> DownloadAsync(
            DownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            var path = request.DestinationPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "installer");
            return Task.FromResult(new DownloadResult(
                true, path, 9, request.Sha256, request.Url, false, false, 1, null));
        }

        public Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(
            IReadOnlyList<DownloadRequest> requests,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>台帳の合成（cache に在る檔の<b>本物の sha256</b> を載せる）。</summary>
    private static class Ledger
    {
        public static VcRedistLedger VcRedist() => LedgerReader.ParseVcRedist(
            """
            {"schema":1,"name":"vc-redist","items":[
              {"kind":"installer","name":"vc_redist.x64","version":"14.44.35211.0",
               "url":"https://example.invalid/VC_redist.x64.exe",
               "sha256":"cc0ff0eb1dc3f5188ae6300faef32bf5beeba4bdd6e8e445a9184072096b713b",
               "size":25635768,
               "silent_args":["/install","/quiet","/norestart"],
               "license":"Microsoft Software License Terms"}]}
            """);

        public static LedgerItem Cached(string cacheDir, string kind, string name, string fileName)
        {
            var path = Path.Combine(cacheDir, fileName);
            using var stream = File.OpenRead(path);
            return new LedgerItem
            {
                Kind = kind,
                Name = name,
                Version = "1.0",
                Url = "https://example.invalid/" + fileName,
                Filename = fileName,
                Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream)),
                License = "MIT",
            };
        }

        public static string PthTemplate(string appDir)
        {
            var server = Path.Combine(appDir, "server");
            Directory.CreateDirectory(server);
            var path = Path.Combine(server, "python312._pth.template");
            File.WriteAllText(
                path,
                "python312.zip\n.\n@RUNTIME_DIR@/site-packages\n@APP_DIR@/server\n",
                new UTF8Encoding(false));
            return path;
        }
    }
}
