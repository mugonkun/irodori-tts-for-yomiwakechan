using System;
using System.Collections.Generic;
using System.IO;
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
/// 便 D（2）・画面席（LB）の釘。
/// <list type="number">
/// <item>裁定 87 ⑴＝<c>/ywk/status.memory</c> の欄（device／allocated／reserved／max／
/// gpu_total／gpu_free／gpu_used／latents／latents_total／sampled_at／error）。</item>
/// <item>裁定 67 ⑶＝<b>使用量＝allocated・占有量＝reserved・GPU 全体＝gpu_used／gpu_total</b>。</item>
/// <item>裁定 67 ⑵・low 13＝概算の<b>主役は 1 名あたり</b>（全員分は「同時に載せたときの上限」）。</item>
/// <item>裁定 67 ⑴＝潜在キャッシュの ON／OFF と「焼いた話者 n 名・合計 m MB」。</item>
/// <item>low 3＝状態の書き手は状態機械 1 つ（<c>/ywk/status</c> の標本は状態を動かさない）。</item>
/// <item>裁定 88 ⑵⑶＝門の告知をそのまま出す／合成中の消失は期限を待たずに告げる。</item>
/// <item>low 11・12・14＝試聴は wav だけ／一覧が読めなければ台帳だけ／削除は Command。</item>
/// </list>
/// <b>実機・実 GPU・実ポート・子プロセスには触れない</b>（すべて純関数と偽の口）。
/// </summary>
public sealed class RoundTwoMemoryStatusTests
{
    /// <summary>裁定 87 ⑴ の逐語（欄名はここから 1 字も動かさない）。</summary>
    private const string Sample = """
        {"engine":"irodori-tts-ywk","memory":{
          "device":"cuda:0",
          "allocated":1073741824,
          "reserved":2147483648,
          "max":3221225472,
          "gpu_total":115964116992,
          "gpu_free":107374182400,
          "gpu_used":8589934592,
          "latents":{"琴葉茜":102400,"月読アイ":98304},
          "latents_total":200704,
          "sampled_at":"2026-09-05T07:12:33.512+09:00",
          "error":null}}
        """;

    [Fact]
    public void 裁定87の11欄がそのまま読める()
    {
        var status = JsonSerializer.Deserialize<StatusResponse>(Sample);
        var memory = Assert.IsType<MemoryStatus>(status?.Memory);

        Assert.Equal("cuda:0", memory.Device);
        Assert.Equal(1073741824L, memory.AllocatedBytes);
        Assert.Equal(2147483648L, memory.ReservedBytes);
        Assert.Equal(3221225472L, memory.MaxAllocatedBytes);
        Assert.Equal(115964116992L, memory.GpuTotalBytes);
        Assert.Equal(107374182400L, memory.GpuFreeBytes);
        Assert.Equal(8589934592L, memory.GpuUsedBytes);
        Assert.Equal(200704L, memory.LatentsTotalBytes);
        Assert.Equal("2026-09-05T07:12:33.512+09:00", memory.SampledAt);
        Assert.Null(memory.Error);
        Assert.Equal(2, memory.LatentCount);
        Assert.Equal(102400L, memory.LatentBytesFor("琴葉茜"));
        Assert.Null(memory.LatentBytesFor("居ない話者"));
    }

    [Fact]
    public void gpu_usedが無ければtotalとfreeから起こす()
    {
        var memory = new MemoryStatus { GpuTotalBytes = 1000, GpuFreeBytes = 400 };
        Assert.Equal(600L, memory.EffectiveGpuUsed);
        Assert.Null(new MemoryStatus { GpuTotalBytes = 1000 }.EffectiveGpuUsed);
    }

    [Fact]
    public void latents_totalが無ければ表から足す()
    {
        var memory = new MemoryStatus
        {
            Latents = new Dictionary<string, long>(StringComparer.Ordinal) { ["あ"] = 10, ["い"] = 32 },
        };

        Assert.Equal(42L, memory.EffectiveLatentsTotal);
        Assert.Null(new MemoryStatus().EffectiveLatentsTotal);
    }

    [Fact]
    public void cpuと未読込では数値欄がnullでもlatentsだけ来る()
    {
        // 裁定 87 ⑴＝device が cpu か未読込のときは数値欄を null にし latents だけ出す
        var cpu = JsonSerializer.Deserialize<MemoryStatus>(
            """
            {"device":"cpu","allocated":null,"reserved":null,"max":null,
             "gpu_total":null,"gpu_free":null,"gpu_used":null,
             "latents":{"琴葉茜":102400},"latents_total":102400}
            """);

        Assert.NotNull(cpu);
        Assert.True(cpu.IsCpu);
        Assert.False(cpu.HasNumbers);
        Assert.Equal(1, cpu.LatentCount);
    }
}

public sealed class RoundTwoStatusBandTests
{
    private static StatusViewModel Create() =>
        new(static () => Task.CompletedTask, static () => Task.CompletedTask);

    [Fact]
    public void 使用量と占有量とGPU全体をこの順で出す()
    {
        // 裁定 67 ⑶＝使用量＝allocated・占有量＝reserved・GPU 全体＝gpu_used／gpu_total
        var text = StatusViewModel.DescribeMemory(new MemoryStatus
        {
            Device = "cuda:0",
            AllocatedBytes = 1073741824,
            ReservedBytes = 2147483648,
            GpuTotalBytes = 4294967296,
            GpuUsedBytes = 3221225472,
        });

        Assert.Contains("使用量 1.00 GB", text, StringComparison.Ordinal);
        Assert.Contains("占有量 2.00 GB", text, StringComparison.Ordinal);
        Assert.Contains("GPU 全体 3.00 GB / 4.00 GB", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("使用量", StringComparison.Ordinal)
            < text.IndexOf("占有量", StringComparison.Ordinal));
    }

    [Fact]
    public void 読めない欄は0ではなく棒で出す()
    {
        var text = StatusViewModel.DescribeMemory(new MemoryStatus { AllocatedBytes = 1024 });

        Assert.Contains("占有量 " + UiText.Missing, text, StringComparison.Ordinal);
        Assert.Contains("GPU 全体 " + UiText.Missing + " / " + UiText.Missing, text, StringComparison.Ordinal);
        Assert.DoesNotContain("0 B", text, StringComparison.Ordinal);
    }

    [Fact]
    public void cpuはGPUメモリなしと出す()
    {
        Assert.Equal("CPU（GPU メモリなし）",
            StatusViewModel.DescribeMemory(new MemoryStatus { Device = "cpu" }));
    }

    [Fact]
    public void 欄ごと無ければ未対応と出す()
    {
        Assert.Equal(UiText.NotSupported, StatusViewModel.DescribeMemory(null));
    }

    [Fact]
    public void errorは1行で添える()
    {
        var text = StatusViewModel.DescribeMemory(new MemoryStatus
        {
            Device = "cuda:0",
            AllocatedBytes = 1024,
            Error = "mem_get_info が失敗しました。",
        });

        Assert.Contains("mem_get_info が失敗しました。", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 潜在キャッシュはONとOFFと件数と合計を出す()
    {
        // 裁定 67 ⑴＝状態帯に「潜在キャッシュ ON/OFF（焼いた話者 n 名・合計 m MB）」
        var memory = new MemoryStatus
        {
            Latents = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["琴葉茜"] = 1048576,
                ["月読アイ"] = 1048576,
            },
            LatentsTotalBytes = 2097152,
        };

        var on = StatusViewModel.DescribeLatentCache(true, memory, null);
        Assert.StartsWith("ON（焼いた話者 2 名・合計 2.0 MB", on, StringComparison.Ordinal);

        var off = StatusViewModel.DescribeLatentCache(false, memory, null);
        Assert.StartsWith("OFF（", off, StringComparison.Ordinal);
    }

    [Fact]
    public void memoryが無ければ焼いた数だけ一覧から名乗る()
    {
        var rows = new[]
        {
            new VoiceRow(VoiceIds.Default, "デフォルト", null, false, true, false, false, null),
            new VoiceRow("焼き済み", "焼き済み", "a.wav", false, false, true, false, null),
        };

        var text = StatusViewModel.DescribeLatentCache(true, null, rows);
        Assert.Contains("焼いた話者 1 名", text, StringComparison.Ordinal);
        Assert.Contains("合計 " + UiText.Missing, text, StringComparison.Ordinal);
    }

    [Fact]
    public void 設定のONOFFがそのまま状態帯に出る()
    {
        var vm = Create();
        vm.ApplySettings(new LauncherSettings
        {
            Variant = RuntimeVariants.RocmGfx1151,
            PrecomputeOnStart = false,
        });

        Assert.StartsWith("OFF", vm.LatentCacheText, StringComparison.Ordinal);

        vm.ApplySettings(new LauncherSettings
        {
            Variant = RuntimeVariants.RocmGfx1151,
            PrecomputeOnStart = true,
        });

        Assert.StartsWith("ON", vm.LatentCacheText, StringComparison.Ordinal);
    }

    [Fact]
    public void 概算は1名あたりが主役で全員分は括弧に落ちる()
    {
        // 裁定 67 ⑵・low 13
        var rows = new[]
        {
            new VoiceRow(VoiceIds.Default, "デフォルト", null, false, true, false, false, null),
            new VoiceRow("あかね", "あかね", "a.wav", false, false, false, false, null),
            new VoiceRow("あい", "あい", "b.wav", false, false, false, false, null),
        };

        var text = StatusViewModel.DescribeVoiceMemory(rows[1], rows);

        Assert.StartsWith("1 名あたり「あかね」＝", text, StringComparison.Ordinal);
        Assert.Contains("実測前の概算", text, StringComparison.Ordinal);
        Assert.Contains("全員分＝全部を同時に載せたときの上限 "
            + UiText.Bytes(MemoryEstimate.WavReferenceBytes * 2), text, StringComparison.Ordinal);
    }

    [Fact]
    public void 焼いてある話者は実サイズを出す()
    {
        // 焼いてあれば memory.latents[id] の実サイズ（概算ではない）
        var row = new VoiceRow("琴葉茜", "琴葉茜", "a.wav", true, false, true, false, null,
            IsInTable: true, LatentBytes: 102400);

        Assert.Equal(102400L, row.MemoryBytes);
        Assert.Contains("潜在参照（実測 100.0 KB）", row.MemoryText, StringComparison.Ordinal);
        Assert.DoesNotContain("概算", row.MemoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void 選ぶ前は全員分だけを出す()
    {
        var rows = new[] { new VoiceRow("あ", "あ", "a.wav", false, false, false, false, null) };
        var text = StatusViewModel.DescribeVoiceMemory(null, rows);

        Assert.Contains("話者を選ぶと出ます", text, StringComparison.Ordinal);
        Assert.Contains("全部を同時に載せたときの上限", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 選んだ話者が変われば主役も変わる()
    {
        var vm = Create();
        var rows = new[]
        {
            new VoiceRow(VoiceIds.Default, "デフォルト", null, false, true, false, false, null),
            new VoiceRow("あかね", "あかね", "a.wav", false, false, false, false, null),
        };

        vm.ApplyVoices(rows);
        vm.ApplySelectedVoice(rows[1]);
        Assert.Contains("「あかね」", vm.VoiceMemoryText, StringComparison.Ordinal);

        vm.ApplySelectedVoice(rows[0]);
        Assert.Contains("「デフォルト」", vm.VoiceMemoryText, StringComparison.Ordinal);
        Assert.Contains("参照なし（増えません）", vm.VoiceMemoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void status標本は状態を動かさない()
    {
        // low 3＝状態の書き手は状態機械 1 つ。暖機の出入りを窓が書かない。
        var vm = Create();
        vm.ApplyState(ServerState.Ready, null);

        vm.ApplyStatus(new StatusResponse
        {
            Runtime = new StatusRuntime { Loaded = true },
            Warmup = new WarmupStatus { State = "running", ShotsDone = 1, ShotsTotal = 6 },
        });

        Assert.Equal(ServerState.Ready, vm.State);
        Assert.Contains("実行中", vm.WarmupText, StringComparison.Ordinal);
    }

    [Fact]
    public void 門の告知はそのまま状態帯に出る()
    {
        // 裁定 88 ⑵＝「未実測の帯」の注意も畳まずに出す
        var vm = Create();
        vm.ApplyStartOutcome(true, null,
        [
            "cu126 のドライバ 530.00 は未実測の帯です（実測済みは 537.58 以上）。",
        ]);

        Assert.Contains("未実測の帯", vm.NoticesText!, StringComparison.Ordinal);
    }

    [Fact]
    public void 門で断られた理由は状態と重ならない形で出る()
    {
        // 裁定 88 ⑴＝理由 1 行（勧める変種を含む）。Reason に出ていれば二重に出さない。
        const string Reason = "cu130 はこの機体で GPU を見られません（ドライバ 537.58・CUDA 12.2）。"
            + "cu126 か cpu の変種に切り替えてください。";

        Assert.Equal(Reason, StatusViewModel.ComposeStartOutcome(false, Reason, null, null));
        Assert.Null(StatusViewModel.ComposeStartOutcome(false, Reason, null, Reason));

        var both = StatusViewModel.ComposeStartOutcome(false, Reason, ["未実測の帯です。"], Reason);
        Assert.Equal("未実測の帯です。", both);
    }

    [Fact]
    public void 停止に戻れば告知も潜在の数も落ちる()
    {
        var vm = Create();
        vm.ApplySettings(new LauncherSettings { Variant = RuntimeVariants.RocmGfx1151 });
        vm.ApplyNotices(["未実測の帯です。"]);
        vm.ApplyStatus(new StatusResponse
        {
            Memory = new MemoryStatus
            {
                Device = "cuda:0",
                AllocatedBytes = 1024,
                Latents = new Dictionary<string, long>(StringComparer.Ordinal) { ["あ"] = 1024 },
            },
        });

        Assert.True(vm.MemorySupported);
        Assert.Contains("焼いた話者 1 名", vm.LatentCacheText, StringComparison.Ordinal);

        vm.ApplyState(ServerState.Stopped, null);

        Assert.Null(vm.NoticesText);
        Assert.False(vm.MemorySupported);
        Assert.Equal(UiText.NotSupported, vm.MemoryText);
    }
}

public sealed class RoundTwoVoicesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-lb2-" + Guid.NewGuid().ToString("N")[..8]);

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

    private VoicesViewModel Create(StubWrapper? wrapper) =>
        new(null, null, new FakeAudioPlayer(), () => wrapper, Paths, new LauncherSettings());

    [Fact]
    public void 試聴はwavのときだけ押せる()
    {
        // low 11＝mp3 等は押せない（NAudio.WinMM に読み手が無い）＋理由 1 行
        var wav = new VoiceRow("あ", "あ", "ywk-aaaa.wav", false, false, false, false, null);
        var mp3 = new VoiceRow("い", "い", "ywk-bbbb.mp3", false, false, false, false, null);
        var noRef = new VoiceRow(VoiceIds.Default, "デフォルト", null, false, true, false, false, null);

        Assert.True(wav.CanPreview);
        Assert.Null(wav.PreviewBlockedReason);

        Assert.False(mp3.CanPreview);
        Assert.Contains("MP3", mp3.PreviewBlockedReason!, StringComparison.Ordinal);
        Assert.Contains("試聴は wav だけ", mp3.PreviewBlockedReason!, StringComparison.Ordinal);

        Assert.False(noRef.CanPreview);
        Assert.Contains("参照なし", noRef.PreviewBlockedReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 押せない試聴は理由1行を残す()
    {
        var vm = Create(null);
        vm.Selected = new VoiceRow("い", "い", "ywk-bbbb.mp3", false, false, false, false, null);

        Assert.False(vm.PreviewCommand.CanExecute(null));
        Assert.Contains("試聴は wav だけ", vm.PreviewBlockedText, StringComparison.Ordinal);

        vm.Preview();
        Assert.Contains("試聴は wav だけ", vm.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 一覧が読めなければ台帳だけで出す()
    {
        // low 12＝Ok でなければ写しを捨てる（古い一覧を黙って見せない）
        var wrapper = new StubWrapper
        {
            Voices = new VoicesResponse { Data = [new VoiceInfo { Id = "サーバの話者" }] },
        };

        var vm = Create(wrapper);
        await vm.RefreshAsync();
        Assert.Contains(vm.Rows, r => r.Id == "サーバの話者");

        wrapper.FailStatus = 500;
        await vm.RefreshAsync();

        Assert.DoesNotContain(vm.Rows, r => r.Id == "サーバの話者");
        Assert.Contains("HTTP 500", vm.Message, StringComparison.Ordinal);
        Assert.Contains("台帳だけで一覧を出しています", vm.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 読めなかった理由は状態番号つきの1行()
    {
        Assert.Equal("サーバの話者一覧が読めませんでした（HTTP 500）。台帳だけで一覧を出しています。",
            VoicesViewModel.DescribeVoicesFailure(500));
        Assert.Contains("応答なし", VoicesViewModel.DescribeVoicesFailure(0), StringComparison.Ordinal);
    }

    [Fact]
    public void 削除はCommandに寄り押せない理由が読める()
    {
        // low 14＝押せる・押せないを 1 箇所（RemoveCommand）で決め、理由は UIA から読める文字列
        var vm = Create(null);
        vm.Reload();

        vm.Selected = Assert.Single(vm.Rows, r => r.Id == VoiceIds.Default);
        Assert.False(vm.RemoveCommand.CanExecute(null));
        Assert.Contains("常在するので消せません", vm.RemoveBlockedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 確認を断れば1檔も消えない()
    {
        // low 14＝削除は Command に寄せたが、確認窓は落とさない（View が差す手）。
        var paths = Paths;
        var store = new VoiceStore(paths.VoicesYwkJsonPath, paths.VoicesDir, paths.ReferenceWavDir);
        var vm = new VoicesViewModel(
            store, new VoicesJsonWriter(), new FakeAudioPlayer(),
            static () => null, paths, new LauncherSettings());

        store.AddVoice("テスト話者", MakeWav(0x31), null);
        vm.Reload();
        vm.Selected = Assert.Single(vm.Rows, r => r.Id == "テスト話者");

        var asked = 0;
        vm.ConfirmRemove = _ =>
        {
            asked++;
            return false;
        };

        await vm.RemoveSelectedAsync();

        Assert.Equal(1, asked);
        Assert.True(store.Load().Voices.ContainsKey("テスト話者"));
        Assert.Single(Directory.GetFiles(paths.ReferenceWavDir, "*.wav"));
        Assert.DoesNotContain("削除しました", vm.Message, StringComparison.Ordinal);
    }

    private string MakeWav(byte fill)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "source-"
            + fill.ToString("x2", System.Globalization.CultureInfo.InvariantCulture) + ".wav");
        var bytes = new byte[1024];
        Array.Fill(bytes, fill);
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void 潜在の実サイズは一覧の行に載る()
    {
        // 裁定 87 ⑴ の latents を VoiceRowBuilder が話者ごとに引く
        var live = new VoicesResponse
        {
            Data =
            [
                new VoiceInfo { Id = "琴葉茜", Latent = true },
                new VoiceInfo { Id = "未焼き" },
            ],
        };

        var memory = new MemoryStatus
        {
            Latents = new Dictionary<string, long>(StringComparer.Ordinal) { ["琴葉茜"] = 102400 },
        };

        var rows = VoiceRowBuilder.Build(null, live, null, memory);

        var baked = Assert.Single(rows, r => r.Id == "琴葉茜");
        Assert.Equal(102400L, baked.LatentBytes);
        Assert.Contains("実測", baked.MemoryText, StringComparison.Ordinal);

        var raw = Assert.Single(rows, r => r.Id == "未焼き");
        Assert.Null(raw.LatentBytes);
        Assert.Contains("wav 参照", raw.MemoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void 標本のlatentsが変わったときだけ一覧を組み直す()
    {
        var vm = Create(null);
        vm.Reload();

        var seen = 0;
        vm.RowsChanged += (_, _) => seen++;

        var memory = new MemoryStatus
        {
            Latents = new Dictionary<string, long>(StringComparer.Ordinal) { ["あ"] = 10 },
        };

        vm.ApplyMemory(memory);
        Assert.Equal(1, seen);

        // 同じ表なら組み直さない（2 秒ごとの標本で一覧が点滅しない）
        vm.ApplyMemory(new MemoryStatus
        {
            Latents = new Dictionary<string, long>(StringComparer.Ordinal) { ["あ"] = 10 },
            AllocatedBytes = 999,
        });
        Assert.Equal(1, seen);

        vm.ApplyMemory(new MemoryStatus
        {
            Latents = new Dictionary<string, long>(StringComparer.Ordinal) { ["あ"] = 11 },
        });
        Assert.Equal(2, seen);
    }

    /// <summary>使う口だけ答える偽の wrapper（残りは呼ばれたら落ちる）。</summary>
    private sealed class StubWrapper : IWrapperClient
    {
        public Uri BaseAddress { get; } = new("http://127.0.0.1:18099/");

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

        public VoicesResponse? Voices { get; set; }

        /// <summary>0 以外＝この状態番号で失敗を返す。</summary>
        public int FailStatus { get; set; }

        public Task<WrapperResult<VoicesResponse>> GetVoicesAsync(CancellationToken cancellationToken) =>
            FailStatus != 0
                ? Task.FromResult(new WrapperResult<VoicesResponse>(
                    false, null, FailStatus, null, true, TimeSpan.Zero, "サーバが 500 を返しました。"))
                : Task.FromResult(new WrapperResult<VoicesResponse>(
                    Voices is not null, Voices, 200, null, true, TimeSpan.Zero, null));

        public Task<WrapperResult<DropLatentResult>> DropLatentAsync(
            string voiceId, CancellationToken cancellationToken) =>
            Task.FromResult(new WrapperResult<DropLatentResult>(
                false, null, 404, null, false, TimeSpan.Zero, "この個体にはこの口がありません。"));

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

public sealed class RoundTwoTryTests
{
    [Fact]
    public void 落ちた理由は終了コードつきの1行()
    {
        // 裁定 88 ⑶ の逐語（0xC0000005＝−1073741819 が見分けの手がかり）
        var text = TryViewModel.DescribeServerDown(-1073741819, "サーバが異常終了した（終了コード -1073741819）。");

        Assert.StartsWith("サーバが落ちました（exit -1073741819）。", text, StringComparison.Ordinal);
        Assert.Contains("サーバが異常終了した", text, StringComparison.Ordinal);
        Assert.Equal("サーバが落ちました。", TryViewModel.DescribeServerDown(null, null));
    }

    [Fact]
    public async Task 合成中に子が消えたら期限を待たずに告げる()
    {
        // 裁定 88 ⑶＝HTTP の timeout（既定 120 s）を待たない
        var wrapper = new BlockingWrapper();
        var vm = new TryViewModel(() => wrapper, new FakeAudioPlayer(), new LauncherSettings())
        {
            Input = "あ。",
        };

        var shot = vm.SynthesizeAsync();
        await wrapper.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        vm.NotifyServerFailed(-1073741819, "サーバが異常終了した（終了コード -1073741819）。");
        await shot.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("サーバが落ちました（exit -1073741819）", vm.Message, StringComparison.Ordinal);
        Assert.False(vm.HasAudio);
    }

    [Fact]
    public void 走っていなくても落ちた事実は画面に残る()
    {
        var vm = new TryViewModel(static () => null, new FakeAudioPlayer(), new LauncherSettings());
        vm.NotifyServerFailed(2, "起動前の検査で止まった。");

        Assert.Contains("サーバが落ちました（exit 2）", vm.Message, StringComparison.Ordinal);
    }

    /// <summary>取消されるまで返らない合成（実 HTTP には触れない）。</summary>
    private sealed class BlockingWrapper : IWrapperClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Uri BaseAddress { get; } = new("http://127.0.0.1:18099/");

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

        public async Task<SpeechResult> SynthesizeAsync(
            SpeechRequest request, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("ここには来ない");
        }

        public Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<StatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<ParamsResponse>> GetParamsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<VoicesResponse>> GetVoicesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<VoicesResponse>> GetOpenAiVoicesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<WarmupStartResult>> StartWarmupAsync(
            WarmupRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<CancelResult>> CancelWarmupAsync(
            string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<PrecomputeStartResult>> StartPrecomputeAsync(
            PrecomputeRequest request, CancellationToken cancellationToken) =>
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
}
