using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Http;
using IrodoriTtsYwk.Launcher.Services.Voices;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// wrapper の HTTP（契約 ⑵〜⑺）。<b>実ポートも実サーバも起こさない</b>＝
/// <see cref="HttpMessageHandler"/> に偽物を差し、応答の逐語（<c>docs/contract.md</c>）を読ませる。
/// </summary>
public sealed class WrapperClientTests
{
    private static readonly Uri Base = new("http://127.0.0.1:18088/");

    /// <summary>契約 ⑹ の逐語（この機体・Radeon 版で返る形）。</summary>
    private const string StatusBody = """
        {"engine":"irodori-ywk","version":"0.1.0",
         "upstream":{"irodori_tts":"8224daf","server":"841fb7c"},
         "host":"127.0.0.1","port":18088,"variant":"rocm-gfx1151",
         "runtime":{"loaded":true,"loading":false,"error":null},
         "device":{"configured":"cuda:0","actual":"cuda:0","name":"AMD Radeon(TM) 8060S Graphics",
                   "uuid":"30303030-3031-3937-3030-303030303030","pci_bus_id":"197",
                   "precision":"bf16","hip":"7.15.26333","gcn_arch":"gfx1151"},
         "torch":{"version":"2.13.0+rocm10.0.0","cuda":null,"hip":"7.15.26333"},
         "voices":{"count":13,"dir":"voices","error":null},
         "warmup":{"state":"running","id":"w1","shots_done":2,"shots_total":5,"elapsed_s":6.25,
                   "last_shot":{"kind":"no_ref","key":"4s","seconds":4.0,"ms":2518.4},
                   "error":null,"shots":2},
         "precompute":{"state":"idle","id":null,"done":0,"total":0,
                       "built":0,"reused":0,"skipped":0,"failed":0,"elapsed_s":0.0,
                       "last":null,"error":null}}
        """;

    [Fact]
    public async Task statusから実deviceと変種と話者件数を読む()
    {
        using var client = Client(Respond(HttpStatusCode.OK, StatusBody));

        var result = await client.GetStatusAsync(CancellationToken.None);

        Assert.True(result.Ok);
        var status = result.Value!;
        Assert.True(status.IsReady);
        Assert.Equal(RuntimeVariants.RocmGfx1151, status.Variant);
        Assert.Equal("cuda:0", status.Device!.Actual);
        Assert.Equal("gfx1151", status.Device.GcnArch);
        Assert.Equal(13, status.Voices!.Count);
        Assert.True(status.Warmup!.IsRunning);
        Assert.False(status.Precompute!.IsRunning);
    }

    [Fact]
    public async Task memoryの欄が無くても落ちない()
    {
        // 裁定 67 ⑶＝便 C（2）が wrapper に足している最中（欄名は仮）
        using var client = Client(Respond(HttpStatusCode.OK, StatusBody));

        var status = (await client.GetStatusAsync(CancellationToken.None)).Value!;

        Assert.Null(status.Memory);
    }

    [Fact]
    public async Task memoryの欄が在れば読む()
    {
        const string body = """
            {"engine":"irodori-ywk","runtime":{"loaded":true},
             "memory":{"allocated":1234,"reserved":5678,"max":9012,"latents":{"琴葉茜":4096}}}
            """;
        using var client = Client(Respond(HttpStatusCode.OK, body));

        var status = (await client.GetStatusAsync(CancellationToken.None)).Value!;

        Assert.Equal(1234, status.Memory!.AllocatedBytes);
        Assert.Equal(9012, status.Memory.MaxAllocatedBytes);
        Assert.Equal(4096, status.Memory.EffectiveLatents["琴葉茜"]);
    }

    [Fact]
    public async Task pciBusIdが数で来ても応答1本を落とさない()
    {
        // 実測（この機体・2026-09-05）＝ROCm の torch は pci_bus_id を整数 197 で返し、
        // wrapper がそのまま載せる。string 決め打ちで読むと /ywk/status が丸ごと読めなくなる。
        const string body = """
            {"engine":"irodori-ywk","runtime":{"loaded":true},
             "device":{"configured":"cuda:0","actual":"cuda:0","pci_bus_id":197,
                       "gcn_arch":"gfx1151","hip":"7.15.26333"}}
            """;
        using var client = Client(Respond(HttpStatusCode.OK, body));

        var result = await client.GetStatusAsync(CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("197", result.Value!.Device!.PciBusId);
        Assert.Equal("gfx1151", result.Value.Device.GcnArch);
    }

    [Fact]
    public async Task statusが404なら配布版として扱わない()
    {
        // 契約 ⑵＝404 は上流の素の Server（到達はしている）
        using var client = Client(Respond(HttpStatusCode.NotFound, "{\"detail\":\"Not Found\"}"));

        var result = await client.GetStatusAsync(CancellationToken.None);

        Assert.False(result.Ok);
        Assert.False(result.Available);
    }

    [Fact]
    public async Task 事前計算の口が無ければ未対応として動く()
    {
        // 便 C（2）が追加中＝無ければ無いものとして動く
        using var client = Client(Respond(HttpStatusCode.MethodNotAllowed, string.Empty));
        var coordinator = new WarmupCoordinator(client);

        var start = await coordinator.StartPrecomputeAsync(new PrecomputeRequest(All: true), CancellationToken.None);

        Assert.False(start.Started);
        Assert.False(start.Supported);
        Assert.Contains("未対応", start.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 走行中の暖機に重ねると409を名乗る()
    {
        using var client = Client(Respond(
            HttpStatusCode.Conflict,
            "{\"error\":{\"message\":\"warmup is running\",\"type\":\"invalid_request_error\","
            + "\"param\":null,\"code\":\"ywk_warmup_running\"}}"));
        var coordinator = new WarmupCoordinator(client);

        var start = await coordinator.StartWarmupAsync(new WarmupRequest(), CancellationToken.None);

        Assert.False(start.Started);
        Assert.True(start.Supported);
        Assert.Contains("既に走って", start.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 暖機は202でidと射数を返す()
    {
        using var client = Client(Respond(
            HttpStatusCode.Accepted, "{\"id\":\"w7\",\"shots_total\":5,\"state\":\"running\"}"));
        var coordinator = new WarmupCoordinator(client);

        var start = await coordinator.StartWarmupAsync(
            new WarmupRequest([4, 8, 12], ["琴葉茜"], null), CancellationToken.None);

        Assert.True(start.Started);
        Assert.Equal("w7", start.Id);
        Assert.Equal(5, start.Total);
    }

    [Fact]
    public async Task 合成はwavのバイトとseedを返す()
    {
        var wav = Encoding.ASCII.GetBytes("RIFF____WAVEfmt ");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(wav),
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        response.Headers.Add("X-Irodori-Seed", "123456");

        using var client = Client(new StubHttpHandler(_ => response));

        var result = await client.SynthesizeAsync(
            new SpeechRequest("こんにちは。", "琴葉茜"), TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(wav, result.Audio);
        Assert.Equal("audio/wav", result.ContentType);
        Assert.Equal(123456, result.Seed);
    }

    [Fact]
    public async Task 未知の話者はcodeで判る()
    {
        // 契約 ⑶ 3-3＝文言依存の判定をしない（本体の D-5 がこれで消える）
        using var client = Client(Respond(
            HttpStatusCode.BadRequest,
            "{\"error\":{\"message\":\"unknown voice\",\"type\":\"invalid_request_error\","
            + "\"param\":\"voice\",\"code\":\"ywk_unknown_voice\"}}"));

        var result = await client.SynthesizeAsync(
            new SpeechRequest("あ", "居ない人"), TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(WrapperErrorCodes.UnknownVoice, result.Error!.Code);
    }

    [Fact]
    public async Task 繋がらなければ状態0と理由1行で返る()
    {
        using var client = Client(new StubHttpHandler(_ =>
            throw new HttpRequestException("connection refused", null, HttpStatusCode.ServiceUnavailable)));

        var result = await client.GetStatusAsync(CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(0, result.StatusCode);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void 合成のbodyはmodelとwav固定で空欄を出さない()
    {
        var json = WrapperClient.BuildSpeechBody(new SpeechRequest("こんにちは。"));

        using var document = JsonDocument.Parse(json);
        Assert.Equal(SpeechRequest.ModelName, document.RootElement.GetProperty("model").GetString());
        Assert.Equal("wav", document.RootElement.GetProperty("response_format").GetString());

        // 「既定に戻す」は欄を出さないことで表す（契約 ⑶ 3-1）
        Assert.False(document.RootElement.TryGetProperty("voice", out _));
        Assert.False(document.RootElement.TryGetProperty("speed", out _));
        Assert.False(document.RootElement.TryGetProperty("irodori", out _));

        // 日本語を \uXXXX に潰さない
        Assert.Contains("こんにちは。", json, StringComparison.Ordinal);
    }

    [Fact]
    public void 上流の44欄はirodoriネストに入れて送る()
    {
        var irodori = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["num_steps"] = 40,
            ["cfg_scale_text"] = 2.5,
            ["caption"] = "落ち着いた声",
            ["chunking_enabled"] = false,
            ["seed"] = null, // null は載せない（欄ごと出さないのと同じ）
        };

        var json = WrapperClient.BuildSpeechBody(new SpeechRequest("あ", "琴葉茜", 1.0, irodori));

        using var document = JsonDocument.Parse(json);
        var nested = document.RootElement.GetProperty("irodori");
        Assert.Equal(40, nested.GetProperty("num_steps").GetInt32());
        Assert.Equal(2.5, nested.GetProperty("cfg_scale_text").GetDouble());
        Assert.False(nested.GetProperty("chunking_enabled").GetBoolean());
        Assert.False(nested.TryGetProperty("seed", out _));
        Assert.Equal(1.0, document.RootElement.GetProperty("speed").GetDouble());
    }

    [Fact]
    public void 事前計算のbodyはidsかallのどちらか一方になる()
    {
        // 契約 ⑺ 7-3＝両方＝400・どちらも無し＝400
        using var withIds = JsonDocument.Parse(
            WrapperClient.BuildPrecomputeBody(new PrecomputeRequest(["琴葉茜"], All: true)));
        Assert.True(withIds.RootElement.TryGetProperty("ids", out _));
        Assert.False(withIds.RootElement.TryGetProperty("all", out _));

        using var withAll = JsonDocument.Parse(
            WrapperClient.BuildPrecomputeBody(new PrecomputeRequest(All: true, Force: true)));
        Assert.True(withAll.RootElement.GetProperty("all").GetBoolean());
        Assert.True(withAll.RootElement.GetProperty("force").GetBoolean());
    }

    [Fact]
    public void 暖機の要求は設定から組む()
    {
        var settings = new LauncherSettings { WarmupText = " 暖機です。 " };
        settings.WarmupVoices.Add(VoiceIds.Default); // 段の射が既に参照なし＝載せない
        settings.WarmupVoices.Add("琴葉茜");

        var request = WarmupCoordinator.BuildRequest(settings, ["月読アイ"], maxVoices: 3);

        Assert.Equal(new[] { 4d, 8d, 12d }, request.Stages!);
        Assert.Equal(new[] { "琴葉茜" }, request.Voices!);
        Assert.Equal("暖機です。", request.Text);
    }

    [Fact]
    public void 暖機の話者が空なら最近使った話者を載せる()
    {
        var settings = new LauncherSettings();

        var request = WarmupCoordinator.BuildRequest(settings, ["琴葉茜", "月読アイ", "A", "B"], maxVoices: 3);

        Assert.Equal(3, request.Voices!.Count);
    }

    [Fact]
    public void 走行の1行はstateで決まる()
    {
        Assert.Contains("暖機中", WarmupCoordinator.Describe(new WarmupStatus
        {
            State = "running",
            ShotsDone = 2,
            ShotsTotal = 5,
        }), StringComparison.Ordinal);

        Assert.Contains("していません", WarmupCoordinator.Describe((WarmupStatus?)null), StringComparison.Ordinal);
        Assert.Contains("焼いていません", WarmupCoordinator.Describe((PrecomputeStatus?)null), StringComparison.Ordinal);
    }

    private static WrapperClient Client(HttpMessageHandler handler) => new(Base, handler);

    private static StubHttpHandler Respond(HttpStatusCode status, string body) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

    /// <summary>実 HTTP を張らずに応答を決め打ちする（テストの継ぎ目＝public コンストラクタ）。</summary>
    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
