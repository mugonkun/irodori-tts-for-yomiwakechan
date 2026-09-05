using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Voices;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 話者の削除の<b>順</b>（裁定 78 ⑶・契約 ⑷ 4-3）と、初回取得ウィザードの<b>段の成否</b>を
/// 釘付けする（是正席・便 D・2026-09-05）。実 GPU・実ポート・子プロセスには触れない。
/// </summary>
public sealed class CorrectionSeatVoicesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-fix-" + Guid.NewGuid().ToString("N")[..8]);

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

    private AppPaths Paths => new(
        Path.Combine(_root, "install"),
        Path.Combine(_root, "app"),
        Path.Combine(_root, "runtime"),
        Path.Combine(_root, "data"),
        developerMode: true);

    private string MakeWav(byte fill)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "source-" + fill.ToString("x2", System.Globalization.CultureInfo.InvariantCulture) + ".wav");
        var bytes = new byte[1024];
        Array.Fill(bytes, fill);
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public async Task 削除はまずDELETE_latentを撃ってから檔と台帳を消す()
    {
        // 所見 13 の釘＝口が IWrapperClient に無かったので、便 C（2）の申し送り
        // （裁定 78 ⑶＝DELETE /ywk/voices/{id}/latent → wav と台帳の順）が実装されていなかった。
        var paths = Paths;
        var store = new VoiceStore(paths.VoicesYwkJsonPath, paths.VoicesDir, paths.ReferenceWavDir);
        var writer = new VoicesJsonWriter();
        var wrapper = new FakeWrapper();

        var vm = new VoicesViewModel(
            store, writer, new SilentPlayer(), () => wrapper, paths, new LauncherSettings());

        store.AddVoice("テスト話者", MakeWav(0x21), null);
        vm.Reload();
        vm.Selected = Assert.Single(vm.Rows, r => r.Id == "テスト話者");

        await vm.RemoveSelectedAsync();

        // ⑴ サーバの口が先に叩かれた
        Assert.Equal(["テスト話者"], wrapper.DroppedLatents);
        // ⑵⑶ 台帳・参照 wav・別名表
        Assert.False(store.Load().Voices.ContainsKey("テスト話者"));
        Assert.Empty(Directory.GetFiles(paths.ReferenceWavDir, "*.wav"));
        Assert.DoesNotContain("テスト話者", File.ReadAllText(paths.VoicesJsonPath), StringComparison.Ordinal);
        Assert.Contains("削除しました", vm.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 事前計算の走行中は削除せずに待つよう告げる()
    {
        // 409 ywk_precompute_running＝いま消すと走行が別名を書き戻す。
        var paths = Paths;
        var store = new VoiceStore(paths.VoicesYwkJsonPath, paths.VoicesDir, paths.ReferenceWavDir);
        var wrapper = new FakeWrapper { DropCode = WrapperErrorCodes.PrecomputeRunning, DropStatus = 409 };

        var vm = new VoicesViewModel(
            store, new VoicesJsonWriter(), new SilentPlayer(), () => wrapper, paths, new LauncherSettings());

        store.AddVoice("テスト話者", MakeWav(0x22), null);
        vm.Reload();
        vm.Selected = Assert.Single(vm.Rows, r => r.Id == "テスト話者");

        await vm.RemoveSelectedAsync();

        Assert.Contains("事前計算", vm.Message, StringComparison.Ordinal);
        // 1 檔も消えていない
        Assert.True(store.Load().Voices.ContainsKey("テスト話者"));
        Assert.Single(Directory.GetFiles(paths.ReferenceWavDir, "*.wav"));
    }

    [Fact]
    public async Task 口の無い個体でも削除は通る()
    {
        // 便 C（2）が足す前の wrapper（404／405）＝無ければ無いものとして動く。
        var paths = Paths;
        var store = new VoiceStore(paths.VoicesYwkJsonPath, paths.VoicesDir, paths.ReferenceWavDir);
        var wrapper = new FakeWrapper { DropAvailable = false };

        var vm = new VoicesViewModel(
            store, new VoicesJsonWriter(), new SilentPlayer(), () => wrapper, paths, new LauncherSettings());

        store.AddVoice("テスト話者", MakeWav(0x23), null);
        vm.Reload();
        vm.Selected = Assert.Single(vm.Rows, r => r.Id == "テスト話者");

        await vm.RemoveSelectedAsync();

        Assert.False(store.Load().Voices.ContainsKey("テスト話者"));
    }

    [Fact]
    public async Task 台帳が知らない行の削除は成功を名乗らない()
    {
        // 所見 16 の釘＝幽霊行（サーバ側の走査で見えているだけ）で必ず踏む。
        var paths = Paths;
        var store = new VoiceStore(paths.VoicesYwkJsonPath, paths.VoicesDir, paths.ReferenceWavDir);
        var wrapper = new FakeWrapper
        {
            Voices = new VoicesResponse { Data = [new VoiceInfo { Id = "ywk-c962284a59da" }] },
        };

        var vm = new VoicesViewModel(
            store, new VoicesJsonWriter(), new SilentPlayer(), () => wrapper, paths, new LauncherSettings());

        await vm.RefreshAsync();
        vm.Selected = Assert.Single(vm.Rows, r => r.Id == "ywk-c962284a59da");

        await vm.RemoveSelectedAsync();

        Assert.Contains("台帳にありません", vm.Message, StringComparison.Ordinal);
        Assert.Empty(wrapper.DroppedLatents);
    }

    [Fact]
    public async Task 失敗した段からは進まず初回取得を完了にしない()
    {
        // 所見 14 の釘＝1 檔も落とせていない機体でも「次へ」を押し続ければ最後まで通り、
        // FirstRunCompleted=true が無条件に焼かれていた。
        var paths = Paths;
        Directory.CreateDirectory(paths.LedgerDir);
        Directory.CreateDirectory(paths.LicensesDir);
        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文");
        File.WriteAllText(
            paths.LedgerPath("runtime-cpu"),
            """{"schema":1,"items":[{"kind":"wheel","name":"a","url":"u","size":1}]}""");

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu };
        var vm = new FirstRunViewModel(
            paths,
            settings,
            new JsonSettingsStore(paths.SettingsPath),
            new DriverRequirement(),
            static () => null,   // 取得系が差さっていない＝取得の段は必ず失敗する
            static () => null,
            static _ => Task.FromResult(false));

        vm.Accepted = true;
        await vm.NextAsync();                       // 通知 → 変種
        Assert.Equal(FirstRunStep.Variant, vm.Step);

        await vm.NextAsync();                       // 変種 → 取得（ここで失敗する）
        Assert.Equal(FirstRunStep.Download, vm.Step);
        Assert.False(vm.LastStepOk);

        // 便 D（3）＝自動進行になったので、止まる段の文言は「もう一度」（裁定 94 ⑴）
        Assert.Equal("もう一度", vm.NextButtonText);

        // もう 1 度押しても**先へ進まない**（同じ段をやり直す）
        await vm.NextAsync();
        Assert.Equal(FirstRunStep.Download, vm.Step);
        Assert.False(vm.LastStepOk);

        // 完了は焼かれていない（次の起動でもウィザードが出る）
        Assert.False(settings.FirstRunCompleted);
    }

    /// <summary>音を出さない再生器（試聴の路は触らない）。</summary>
    private sealed class SilentPlayer : IAudioPlayer
    {
        public bool IsPlaying => false;

        public PlaybackResult PlayFile(string path) => new(false, 0, null, "テストでは鳴らさない。");

        public PlaybackResult Play(byte[] wav) => new(false, 0, null, "テストでは鳴らさない。");

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 使う口だけ答える偽の wrapper（残りは呼ばれたら落ちる＝呼び手の誤りを黙って通さない）。
    /// </summary>
    private sealed class FakeWrapper : IWrapperClient
    {
        public Uri BaseAddress { get; } = new("http://127.0.0.1:18099/");

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

        public List<string> DroppedLatents { get; } = [];

        public bool DropAvailable { get; init; } = true;

        public string? DropCode { get; init; }

        public int DropStatus { get; init; } = 200;

        public VoicesResponse? Voices { get; init; }

        public Task<WrapperResult<DropLatentResult>> DropLatentAsync(
            string voiceId, CancellationToken cancellationToken)
        {
            if (!DropAvailable)
            {
                return Task.FromResult(new WrapperResult<DropLatentResult>(
                    false, null, 404, null, false, TimeSpan.Zero, "この個体にはこの口がありません。"));
            }

            if (DropCode is not null)
            {
                return Task.FromResult(new WrapperResult<DropLatentResult>(
                    false, null, DropStatus,
                    new ErrorBody { Code = DropCode, Message = "事前計算が走行中。" },
                    true, TimeSpan.Zero, null));
            }

            DroppedLatents.Add(voiceId);
            return Task.FromResult(new WrapperResult<DropLatentResult>(
                true, new DropLatentResult(voiceId, "reverted", "ref_wav", []),
                200, null, true, TimeSpan.Zero, null));
        }

        public Task<WrapperResult<VoicesResponse>> GetVoicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new WrapperResult<VoicesResponse>(
                Voices is not null, Voices, 200, null, true, TimeSpan.Zero, null));

        public Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<StatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<ParamsResponse>> GetParamsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<VoicesResponse>> GetOpenAiVoicesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SpeechResult> SynthesizeAsync(
            SpeechRequest request, TimeSpan timeout, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<WarmupStartResult>> StartWarmupAsync(
            WarmupRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<CancelResult>> CancelWarmupAsync(
            string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<PrecomputeStartResult>> StartPrecomputeAsync(
            PrecomputeRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new WrapperResult<PrecomputeStartResult>(
                false, null, 404, null, false, TimeSpan.Zero, "この個体にはこの口がありません。"));

        public Task<WrapperResult<CancelResult>> CancelPrecomputeAsync(
            string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
