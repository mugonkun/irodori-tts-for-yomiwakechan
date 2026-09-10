using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Voices;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 統合席（便 D（2））が 4 席の解を合わせたときに見つけた 2 つの割れの釘。
/// <para>
/// どちらも机上の 467 本では出なかった＝<b>席と席の間</b>に落ちていた
/// （⑴ 門の入力を窓が載せていない＝起動席 §16-5 ⑴ の申し送り
///  ⑵ 写したプリセットが上流の別名表に 1 行も入らない＝裁定 78 ⑴）。
/// </para>
/// <para>実機・実 GPU・実ポート・子プロセスには 1 つも触れない。</para>
/// <para>
/// <b><see cref="AppServices"/> を触るので同じ collection に入れる</b>（便 D（3））＝
/// 静的な差し替え口を 2 つの檔が同時に書くと、どちらの釘も理由なく落ちる。
/// </para>
/// </summary>
[Collection(AppServicesCollection.Name)]
public sealed class RoundTwoIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-integ2-" + Guid.NewGuid().ToString("N"));

    // ---- 偽物（実機に触れない継ぎ目） ---------------------------------------

    private sealed class FakeEnumerator(params GpuInfo[] gpus) : IGpuEnumerator
    {
        public Task<GpuEnumerationResult> EnumerateAsync(
            GpuEnumerationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new GpuEnumerationResult(
                gpus, GpuSource.NvidiaSmi, TimeSpan.FromSeconds(1), null));
    }

    /// <summary>起動要求を掴むだけの偽の子プロセス（起こさない・落とさない）。</summary>
    private sealed class CapturingServer : IServerProcess
    {
        public ServerStartRequest? Captured { get; private set; }

        /// <summary>事前検査で断られた理由（便 D（3）＝low 6 の ⑷ の口）。</summary>
        public string? Preflight { get; private set; }

        public ServerState State { get; private set; } = ServerState.Stopped;

        public int? ProcessId => null;

        public int? ExitCode => null;

        public string? FailureReason { get; private set; }

        public ServerLogEvent? Banner => null;

        public Uri? BaseAddress => null;

        public StatusResponse? LatestStatus => null;

        /// <summary>OS の GPU 計数も無い（裁定 110）。</summary>
        public IReadOnlyList<IrodoriTtsYwk.Launcher.Services.Gpu.OsGpuMemoryRow> LatestOsGpuMemory => [];

#pragma warning disable CS0067 // 偽物なので誰も上げない
        public event EventHandler<ServerStateChangedEventArgs>? StateChanged;

        public event EventHandler<ServerLogLineEventArgs>? LogLine;

        public event EventHandler<StatusResponse>? StatusSampled;
#pragma warning restore CS0067

        public Task<ServerStartResult> StartAsync(
            ServerStartRequest request, CancellationToken cancellationToken)
        {
            Captured = request;
            return Task.FromResult(new ServerStartResult(
                false, ServerState.Failed, null, null, TimeSpan.Zero, "偽物なので起こしません。"));
        }

        public void ReportPreflightFailure(string reason)
        {
            Preflight = reason;
            State = ServerState.Failed;
            FailureReason = reason;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SilentPlayer : IAudioPlayer
    {
        public bool IsPlaying => false;

        public PlaybackResult Play(byte[] wav) => new(true, 1.0, null, null);

        public PlaybackResult PlayFile(string path) => new(true, 1.0, null, null);

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }

    // ---- 配布樹と利用者データの雛形 -----------------------------------------

    private AppPaths MakePaths(params string[] ledgerVariants)
    {
        var appDir = Path.Combine(_root, "app");
        var ledgerDir = Path.Combine(appDir, "ledger");
        Directory.CreateDirectory(ledgerDir);
        foreach (var variant in ledgerVariants)
        {
            File.WriteAllText(Path.Combine(ledgerDir, "runtime-" + variant + ".json"), "{}");
        }

        var runtimeRoot = Path.Combine(_root, "runtime");
        var dataDir = Path.Combine(_root, "data");
        Directory.CreateDirectory(dataDir);
        return new AppPaths(Path.Combine(_root, "install"), appDir, runtimeRoot, dataDir, developerMode: true);
    }

    /// <summary>変種ディレクトリの中に「python.exe が在る」形だけ作る（起こさない）。</summary>
    private void MakeRuntime(string variant)
    {
        var dir = Path.Combine(_root, "runtime", variant);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, AppPaths.PythonExeName), string.Empty);
    }

    /// <summary>配布樹のプリセット（<c>voices/presets.json</c> の実物と同じ形）。</summary>
    private void MakePresets(AppPaths paths)
    {
        var presetDir = paths.PresetVoicesDir;
        Directory.CreateDirectory(presetDir);
        File.WriteAllBytes(Path.Combine(presetDir, "vr2_akane_west.wav"), new byte[512]);
        File.WriteAllBytes(Path.Combine(presetDir, "vv_mochiko_sexy.wav"), new byte[256]);
        File.WriteAllText(
            paths.PresetsJsonPath,
            """
            {"version":1,"presets":[
              {"id":"vr2_akane_west","display_name":"琴葉茜（関西弁）","status":"done",
               "secondary":{"file":"vr2_akane_west.wav"}},
              {"id":"vv_mochiko_sexy","display_name":"もち子さん","status":"done",
               "secondary":{"file":"vv_mochiko_sexy.wav"}},
              {"id":"cevio_maki_en","display_name":"弦巻マキ（英語）","status":"skipped",
               "secondary":null}]}
            """,
            new UTF8Encoding(false));
    }

    private MainViewModel NewMain(LauncherSettings settings, AppPaths paths, IGpuEnumerator enumerator)
    {
        AppServices.GpuEnumerator = enumerator;
        var store = new JsonSettingsStore(paths.SettingsPath);
        return new MainViewModel(paths, settings, store, new SilentPlayer());
    }

    // ---- ⑴ 門の入力を窓が載せる（裁定 88 ⑴⑵・起動席 §16-5 ⑴） ---------------

    [Fact]
    public async Task 窓の起動要求は門の入力3つを載せる()
    {
        // 載せないと門は env の YWK_VARIANT（cu130 と cu126 が "cuda" に畳まれた名）で
        // 代用する＝断ることはできても「どちらを勧めるか」が cpu に落ちる。
        //
        // **ドライバは下限を越えた版で撃つ**（裁定 126 ⑽）＝下限に届かない組み合わせ
        // （cu130・537.58）は、いまは主窓が起こす前に断って取得へ導くので、要求そのものが
        // 組まれない（その路は Decision126BelowMinimumTests が見る）。ここで見たいのは
        // 「起こす回に 3 つとも載っているか」なので、門まで届く版で撃つ。
        var paths = MakePaths("cu130", "cu126", "rocm-gfx1151", "cpu");
        MakeRuntime("cu130");
        var server = new CapturingServer();
        AppServices.Server = server;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130, Port = 18099 };
        var main = NewMain(
            settings,
            paths,
            new FakeEnumerator(new GpuInfo(
                "uuid-a", "NVIDIA GeForce RTX 3090", 0, 25L * 1024 * 1024 * 1024,
                "1", null, "591.86", GpuSource.NvidiaSmi)));

        Assert.False(await main.StartServerAsync());

        var request = Assert.IsType<ServerStartRequest>(server.Captured);
        Assert.Equal(RuntimeVariants.Cu130, request.Variant);          // 畳んだ "cuda" ではない
        Assert.Equal("591.86", request.DriverVersion);                 // nvidia-smi の逐語
        Assert.Contains(RuntimeVariants.Cu126, request.InstalledVariants);
        Assert.Contains(RuntimeVariants.RocmGfx1151, request.InstalledVariants);
    }

    [Fact]
    public async Task 載せた変種のおかげで門はcu130を断ってcu126を勧められる()
    {
        // 裁定 80 の実射（RTX 3090）＝cu130 は GPU を数えられず cu126 は通る。
        // **ドライバは下限を越えた版で撃つ**（裁定 126 ⑽）＝下限未満の組み合わせは主窓が
        // 起こす前に断るので門まで届かない。ここで見たいのは「門が要求の 3 つで判断できるか」
        // であり、断る理由は閾ではなく検分（is_available=False）の側である。
        var paths = MakePaths("cu130", "cu126", "cpu");
        MakeRuntime("cu130");
        var server = new CapturingServer();
        AppServices.Server = server;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130, Port = 18099 };
        var main = NewMain(
            settings,
            paths,
            new FakeEnumerator(new GpuInfo(
                "uuid-a", "NVIDIA GeForce RTX 3090", 0, 25L * 1024 * 1024 * 1024,
                "1", null, "591.86", GpuSource.NvidiaSmi)));

        await main.StartServerAsync();
        var request = Assert.IsType<ServerStartRequest>(server.Captured);

        // 掴んだ要求をそのまま門の純関数へ渡す＝窓が載せた 3 つで判断が変わることの釘。
        var refused = VariantGate.Decide(
            request.Variant,
            new TorchGpuProbe.TorchProbeResult([], "2.10.0+cu130", "13.0", null, null),
            request.DriverVersion,
            request.InstalledVariants);

        Assert.False(refused.Allow);
        Assert.Equal(RuntimeVariants.Cu126, refused.SuggestedVariant);
        Assert.Contains("GPU を見られません", refused.Reason!, StringComparison.Ordinal);

        // 畳んだ名（env の YWK_VARIANT）しか無い場合は cpu にしか落とせない＝載せる意味。
        var folded = VariantGate.Decide(
            RuntimeVariants.CudaLabel,
            new TorchGpuProbe.TorchProbeResult([], "2.10.0+cu130", "13.0", null, null),
            request.DriverVersion,
            request.InstalledVariants);
        Assert.Equal(RuntimeVariants.Cpu, folded.SuggestedVariant);
    }

    [Fact]
    public async Task この機体の形ならcu130を断ってrocmを勧める()
    {
        // Radeon 単騎（nvidia-smi が無い＝driver_version は読めない）＝検分席の実射の形。
        var paths = MakePaths("cu130", "cu126", "rocm-gfx1151", "cpu");
        MakeRuntime("cu130");
        var server = new CapturingServer();
        AppServices.Server = server;

        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130, Port = 18099 };
        var main = NewMain(settings, paths, new FakeEnumerator());

        await main.StartServerAsync();
        var request = Assert.IsType<ServerStartRequest>(server.Captured);
        Assert.Equal(RuntimeVariants.Cu130, request.Variant);
        Assert.Null(request.DriverVersion);

        var decision = VariantGate.Decide(
            request.Variant,
            new TorchGpuProbe.TorchProbeResult([], "2.10.0+cu130", "13.0", null, null),
            request.DriverVersion,
            request.InstalledVariants);

        Assert.False(decision.Allow);
        Assert.Equal(RuntimeVariants.RocmGfx1151, decision.SuggestedVariant);
        Assert.Contains("ROCm に切り替えるか、" + UiStrings.SwitchInSettings, decision.Reason!, StringComparison.Ordinal);
    }

    // ---- ⑵ 写したプリセットは voices.json にも載る（裁定 78 ⑴） --------------

    [Fact]
    public void 初回に写したプリセットは上流の別名表にも載る()
    {
        // 是正前＝PresetVoices が書くのはランチャの台帳だけで voices.json は 1 行も増えない
        //         ＝wrapper はプリセットを知らず、画面には見えるのに合成できない形になる。
        var paths = MakePaths("rocm-gfx1151");
        MakePresets(paths);
        var store = new VoiceStore(paths);

        Assert.False(File.Exists(paths.VoicesJsonPath));
        Assert.Equal(2, LauncherComposition.PrepareVoices(paths, store, new VoicesJsonWriter()));

        var json = File.ReadAllText(paths.VoicesJsonPath, Encoding.UTF8);
        Assert.Contains("\"琴葉茜（関西弁）\"", json, StringComparison.Ordinal);
        Assert.Contains("\"もち子さん\"", json, StringComparison.Ordinal);
        Assert.Contains("refs/vr2_akane_west.wav", json, StringComparison.Ordinal);
        // 「デフォルト」は常在（裁定 16）・skipped の行（裁定 27）は載らない
        Assert.Contains("no_ref", json, StringComparison.Ordinal);
        Assert.DoesNotContain("弦巻マキ", json, StringComparison.Ordinal);
    }

    [Fact]
    public void 二度目は写さないが別名表は消えたままにしない()
    {
        var paths = MakePaths("rocm-gfx1151");
        MakePresets(paths);
        var store = new VoiceStore(paths);

        Assert.Equal(2, LauncherComposition.PrepareVoices(paths, store, new VoicesJsonWriter()));
        File.Delete(paths.VoicesJsonPath);

        // 2 度目は 1 名も写さない（利用者が消したプリセットを書き戻さない）が、
        // 別名表が消えていれば書き直す＝wrapper が話者 0 名で起きる形を作らない。
        Assert.Equal(0, LauncherComposition.PrepareVoices(paths, store, new VoicesJsonWriter()));
        Assert.True(File.Exists(paths.VoicesJsonPath));
        Assert.Contains(
            "\"琴葉茜（関西弁）\"",
            File.ReadAllText(paths.VoicesJsonPath, Encoding.UTF8),
            StringComparison.Ordinal);
    }

    [Fact]
    public void 別名表が既に在れば焼いた潜在を消さない()
    {
        // 契約 ⑷ 4-3＝wrapper が書いた ref_latent を、ランチャの書き直しで消さない。
        var paths = MakePaths("rocm-gfx1151");
        MakePresets(paths);
        var store = new VoiceStore(paths);
        LauncherComposition.PrepareVoices(paths, store, new VoicesJsonWriter());

        File.WriteAllText(
            paths.VoicesJsonPath,
            """
            {"デフォルト":{"no_ref":true},
             "琴葉茜（関西弁）":{"ref_latent":"latents/ywk-0513cb458e53.pt"},
             "もち子さん":{"ref_wav":"refs/vv_mochiko_sexy.wav"}}
            """,
            new UTF8Encoding(false));

        // 3 度目＝写す物も移す物も無いので書かない（檔はそのまま）
        Assert.Equal(0, LauncherComposition.PrepareVoices(paths, store, new VoicesJsonWriter()));
        var json = File.ReadAllText(paths.VoicesJsonPath, Encoding.UTF8);
        Assert.Contains("latents/ywk-0513cb458e53.pt", json, StringComparison.Ordinal);

        // 書き直しが起きても残る（書き手が読み直す）
        new VoicesJsonWriter().Write(paths.VoicesJsonPath, store.Load());
        Assert.Contains(
            "latents/ywk-0513cb458e53.pt",
            File.ReadAllText(paths.VoicesJsonPath, Encoding.UTF8),
            StringComparison.Ordinal);
    }

    // ---- (3) 走行中で断られた焼きを出し直す（統合席 §19・実射で出た形） ------

    /// <summary>1 度目は「走行中」で断り、2 度目は受ける偽の wrapper（他の口は使わない）。</summary>
    private sealed class BusyThenFreeWrapper : IWrapperClient
    {
        public List<string[]> Calls { get; } = [];

        public Uri BaseAddress { get; } = new("http://127.0.0.1:18099/");

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>事前計算の口が無い個体（<c>Available=false</c>）を演じる。</summary>
        public bool Unsupported { get; init; }

        public Task<WrapperResult<PrecomputeStartResult>> StartPrecomputeAsync(
            PrecomputeRequest request, CancellationToken cancellationToken)
        {
            Calls.Add([.. request.Ids ?? []]);
            if (Unsupported)
            {
                return Task.FromResult(new WrapperResult<PrecomputeStartResult>(
                    false, null, 404, null, false, TimeSpan.Zero, "この個体にはこの口がありません。"));
            }

            if (Calls.Count == 1)
            {
                // server/ywk_server.py: 409 ywk_precompute_running（起動時の all が走っている）
                return Task.FromResult(new WrapperResult<PrecomputeStartResult>(
                    false, null, 409,
                    new ErrorBody { Code = VoicesViewModel.PrecomputeRunningCode, Message = "busy" },
                    true, TimeSpan.Zero, null));
            }

            return Task.FromResult(new WrapperResult<PrecomputeStartResult>(
                true, new PrecomputeStartResult("run-2", 1, "running"),
                202, null, true, TimeSpan.Zero, null));
        }

        public Task<WrapperResult<VoicesResponse>> GetVoicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new WrapperResult<VoicesResponse>(
                false, null, 0, null, false, TimeSpan.Zero, "使わない"));

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

        public Task<WrapperResult<CancelResult>> CancelPrecomputeAsync(
            string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<DropLatentResult>> DropLatentAsync(
            string voiceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private VoicesViewModel NewVoices(IWrapperClient wrapper, AppPaths paths) =>
        new(null, null, new SilentPlayer(), () => wrapper, paths, new LauncherSettings());

    [Fact]
    public async Task 走行中で断られた焼きは口が空いたら出し直す()
    {
        // 実射（2026-09-05）＝rocm は ready の直後にプリセット 11 名を自分で焼く。その最中に
        // 話者を足すと 409 で断られ、直す前のランチャは二度と出し直さなかった＝足した話者は
        // 「未」のまま置き去りになり、概算メモリも wav 参照のままだった（検分の逐語）。
        var paths = MakePaths("rocm-gfx1151");
        var wrapper = new BusyThenFreeWrapper();
        var voices = NewVoices(wrapper, paths);

        await voices.PrecomputeAsync(["テスト話者"]);
        Assert.Single(wrapper.Calls);
        Assert.Contains("下ごしらえ", voices.Message, StringComparison.Ordinal);

        // 走行中の標本では出し直さない
        voices.ApplyPrecompute(new PrecomputeStatus { State = "running", Id = "run-1" });
        Assert.Single(wrapper.Calls);

        // 走行が終わった標本で 1 度だけ出し直す
        voices.ApplyPrecompute(new PrecomputeStatus { State = "done", Id = "run-1" });
        Assert.Equal(2, wrapper.Calls.Count);
        Assert.Equal(["テスト話者"], wrapper.Calls[1]);

        // 2 度目は覚えていない（同じ標本が何度来ても撃ち直さない）
        voices.ApplyPrecompute(new PrecomputeStatus { State = "done", Id = "run-1" });
        Assert.Equal(2, wrapper.Calls.Count);
    }

    [Fact]
    public void 覚えていない焼きは標本が来ても出さない()
    {
        var paths = MakePaths("rocm-gfx1151");
        var wrapper = new BusyThenFreeWrapper();
        var voices = NewVoices(wrapper, paths);

        voices.ApplyPrecompute(null);
        voices.ApplyPrecompute(new PrecomputeStatus { State = "done" });
        Assert.Empty(wrapper.Calls);
    }

    [Fact]
    public async Task 口が無い個体では覚えない()
    {
        // 事前計算に対応しない wrapper（404・Available=false）は「未対応」＝待ち行列に入れない。
        var paths = MakePaths("cpu");
        var wrapper = new BusyThenFreeWrapper { Unsupported = true };
        var voices = NewVoices(wrapper, paths);

        await voices.PrecomputeAsync(["テスト話者"]);
        Assert.False(voices.PrecomputeSupported);

        voices.ApplyPrecompute(new PrecomputeStatus { State = "done" });
        Assert.Single(wrapper.Calls);
    }

    public void Dispose()
    {
        AppServices.Reset();
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
