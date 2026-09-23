using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
/// ほかのアプリから受け取った参照ボイスを話者にする路（裁定 160・契約 ⑷ 4-5）。
/// <para>
/// 司令官の言葉（2026-09-24）＝「参照ボイスを本体から受け付ける口も新設できるかな。」
/// </para>
/// <para>
/// wrapper が受け箱に置くだけなのは<b>所有者を動かさないため</b>である（契約 ⑷ 4-3）。
/// ここで釘付けするのは、その受け箱をこのアプリが ⑴ 画面の「声を追加する」と同じ路で
/// 話者にすること ⑵ 通らない名・重複は <c>failed</c>＋理由 1 行で終えること
/// ⑶ 画面からの追加と<b>同じ 1 つの錠</b>を分け合うこと ⑷ 古い記録を起動時に捨てること、である。
/// 実ポート・子プロセス・実 GPU には触れない。
/// </para>
/// </summary>
public sealed class VoiceInboxTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-inbox-" + Guid.NewGuid().ToString("N")[..8]);

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

    private VoiceStore NewStore()
    {
        var paths = Paths;
        return new VoiceStore(paths.VoicesYwkJsonPath, paths.VoicesDir, paths.ReferenceWavDir);
    }

    private VoiceInbox NewInbox(IVoiceStore store, IVoicesJsonWriter? writer = null) =>
        new(Paths.VoicesDir, Paths.VoicesJsonPath, store, writer ?? new VoicesJsonWriter());

    /// <summary>wrapper が置く 2 檔を作る（音声＋sidecar）。戻り＝受け箱の id。</summary>
    private string Drop(
        string displayName,
        string? caption = null,
        string? client = VoiceInbox.HontaiClientId,
        string state = "queued",
        string format = "wav",
        DateTimeOffset? receivedAt = null,
        bool withAudio = true)
    {
        var id = Guid.NewGuid().ToString("N")[..24];
        var dir = Path.Combine(Paths.VoicesDir, VoiceInbox.InboxSubdirectory);
        Directory.CreateDirectory(dir);

        if (withAudio)
        {
            var bytes = new byte[1024];
            Array.Fill(bytes, (byte)(displayName.Length + format.Length));
            Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
            Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
            File.WriteAllBytes(Path.Combine(dir, id + "." + format), bytes);
        }

        var sidecar = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["display_name"] = displayName,
            ["caption"] = caption,
            ["format"] = format,
            ["client"] = client,
            ["received_at"] = (receivedAt ?? DateTimeOffset.UtcNow)
                .ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["state"] = state,
            ["error"] = null,
            ["bytes"] = 1024,
        };
        File.WriteAllText(
            Path.Combine(dir, id + ".json"),
            JsonSerializer.Serialize(sidecar),
            new UTF8Encoding(false));
        return id;
    }

    private Dictionary<string, JsonElement> Sidecar(string id)
    {
        var path = Path.Combine(
            Paths.VoicesDir, VoiceInbox.InboxSubdirectory, id + ".json");
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            File.ReadAllText(path, Encoding.UTF8))!;
    }

    private bool AudioExists(string id, string format = "wav") =>
        File.Exists(Path.Combine(
            Paths.VoicesDir, VoiceInbox.InboxSubdirectory, id + "." + format));

    // ---- 話者になる回 -------------------------------------------------------

    [Fact]
    public void 受け箱の1件が画面の追加と同じ路で話者になる()
    {
        var store = NewStore();
        var inbox = NewInbox(store);
        var id = Drop("受け取った声", caption: "明るく");

        var results = inbox.ProcessAll();

        var result = Assert.Single(results);
        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.Equal("受け取った声", result.DisplayName);
        Assert.Equal(VoiceInbox.HontaiClientId, result.Client);

        // ⑴ 台帳に 1 名増え、⑵ 参照は refs\ に写り、⑶ 別名表にも出る
        var entry = Assert.Contains("受け取った声", store.Load().Voices);
        Assert.False(entry.Preset);
        Assert.False(entry.NoRef);
        Assert.Single(Directory.GetFiles(Paths.ReferenceWavDir, "*.wav"));
        Assert.Contains(
            "受け取った声", File.ReadAllText(Paths.VoicesJsonPath), StringComparison.Ordinal);

        // ⑷ sidecar は done で残り、⑸ 受け箱の音声は消える
        Assert.Equal("done", Sidecar(id)["state"].GetString());
        Assert.False(AudioExists(id));
    }

    [Fact]
    public void 渡してきたアプリの名はキャプションの前置きに残る()
    {
        // VoiceEntry に「どのアプリから来たか」の欄は無い（origin は preset／user の 2 値）。
        // 台帳の形を変えずに素性を残す＝キャプションの前置き。
        var store = NewStore();
        NewInbox(store).ProcessAll();

        var id = Drop("素性つきの声", caption: "落ち着いて", client: "myapp");
        NewInbox(store).ProcessAll();
        _ = id;

        var entry = Assert.Contains("素性つきの声", store.Load().Voices);
        Assert.Equal("［myapp から］ 落ち着いて", entry.Caption);
    }

    [Fact]
    public void キャプションが無ければ前置きだけが残る()
    {
        var store = NewStore();
        Drop("前置きだけの声", caption: null, client: "myapp");

        NewInbox(store).ProcessAll();

        Assert.Equal("［myapp から］", store.Load().Voices["前置きだけの声"].Caption);
    }

    [Fact]
    public void 名乗りが無ければ但し書きも付かない()
    {
        var store = NewStore();
        Drop("名乗り無しの声", caption: "元気に", client: null);

        NewInbox(store).ProcessAll();

        Assert.Equal("元気に", store.Load().Voices["名乗り無しの声"].Caption);
    }

    [Fact]
    public void 受けた順に1件ずつ片付ける()
    {
        var store = NewStore();
        var now = DateTimeOffset.UtcNow;
        Drop("あと", receivedAt: now.AddMinutes(1));
        Drop("さき", receivedAt: now);

        var results = NewInbox(store).ProcessAll();

        Assert.Equal(["さき", "あと"], results.Select(r => r.DisplayName));
        Assert.True(results.All(r => r.Ok));
    }

    [Fact]
    public void queued以外は拾わない()
    {
        var store = NewStore();
        Drop("済んだ声", state: "done");
        Drop("失敗した声", state: "failed");
        Drop("途中の声", state: "registering");

        Assert.Empty(NewInbox(store).ProcessAll());
        Assert.Single(store.Load().Voices);  // 「デフォルト」だけ
    }

    // ---- 失敗の態 -----------------------------------------------------------

    [Fact]
    public void 通らない名はfailedで終わり音声も消える()
    {
        var store = NewStore();
        var id = Drop(VoiceIds.Default);  // 予約名

        var result = Assert.Single(NewInbox(store).ProcessAll());

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal("failed", Sidecar(id)["state"].GetString());
        Assert.False(string.IsNullOrWhiteSpace(Sidecar(id)["error"].GetString()));
        Assert.False(AudioExists(id));
        // 台帳は動いていない（「デフォルト」1 件のまま）
        Assert.Single(store.Load().Voices);
        Assert.False(Directory.Exists(Paths.ReferenceWavDir));
    }

    [Fact]
    public void 既にいる話者と同じ名はfailedで終わる()
    {
        var store = NewStore();
        var wav = Path.Combine(_root, "first.wav");
        Directory.CreateDirectory(_root);
        var bytes = new byte[512];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        File.WriteAllBytes(wav, bytes);
        store.AddVoice("先にいる声", wav, null);

        var id = Drop("先にいる声");
        var result = Assert.Single(NewInbox(store).ProcessAll());

        Assert.False(result.Ok);
        Assert.Equal("failed", Sidecar(id)["state"].GetString());
        Assert.False(AudioExists(id));
        // 先にいた 1 名の参照 wav だけが残る（上書きされていない）
        Assert.Single(Directory.GetFiles(Paths.ReferenceWavDir, "*.wav"));
    }

    [Fact]
    public void 同じ回に同じ名が2件来たら2件目はfailed()
    {
        var store = NewStore();
        var now = DateTimeOffset.UtcNow;
        Drop("同じ名", receivedAt: now);
        Drop("同じ名", receivedAt: now.AddMinutes(1));

        var results = NewInbox(store).ProcessAll();

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Ok);
        Assert.False(results[1].Ok);
    }

    [Fact]
    public void 音声が見当たらなければfailed()
    {
        var store = NewStore();
        var id = Drop("音の無い声", withAudio: false);

        var result = Assert.Single(NewInbox(store).ProcessAll());

        Assert.False(result.Ok);
        Assert.Equal(UiStrings.VoiceIntakeNoAudio, result.Error);
        Assert.Equal("failed", Sidecar(id)["state"].GetString());
    }

    [Fact]
    public void 台帳が投げてもfailedで終えて理由に路を出さない()
    {
        var store = new ThrowingStore(new InvalidOperationException("boom"));
        var id = Drop("落ちる声");

        var result = Assert.Single(NewInbox(store, new VoicesJsonWriter()).ProcessAll());

        Assert.False(result.Ok);
        Assert.DoesNotContain(":\\", result.Error ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("failed", Sidecar(id)["state"].GetString());
        Assert.False(AudioExists(id));
    }

    [Fact]
    public void 壊れたsidecarは拾わない()
    {
        var store = NewStore();
        var dir = Path.Combine(Paths.VoicesDir, VoiceInbox.InboxSubdirectory);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "aaaaaaaaaaaaaaaaaaaaaaaa.json"), "{ not json");
        // id の形が違う・知らない拡張子＝どちらも拾わない
        File.WriteAllText(
            Path.Combine(dir, "short.json"),
            """{"id":"short","display_name":"x","format":"wav","state":"queued"}""");
        File.WriteAllText(
            Path.Combine(dir, "bbbbbbbbbbbbbbbbbbbbbbbb.json"),
            """{"id":"bbbbbbbbbbbbbbbbbbbbbbbb","display_name":"x","format":"m4a","state":"queued"}""");

        Assert.Empty(NewInbox(store).ListQueued());
        Assert.Empty(NewInbox(store).ProcessAll());
    }

    // ---- 掃除 ---------------------------------------------------------------

    [Fact]
    public void 古い済みの記録だけを捨てる()
    {
        var store = NewStore();
        var now = DateTimeOffset.UtcNow;
        var old = Drop("古い済み", state: "done", receivedAt: now.AddDays(-8), withAudio: false);
        var oldFailed = Drop("古い失敗", state: "failed", receivedAt: now.AddDays(-30), withAudio: false);
        var fresh = Drop("新しい済み", state: "done", receivedAt: now.AddDays(-1), withAudio: false);
        var waiting = Drop("待っている声", state: "queued", receivedAt: now.AddDays(-99));

        var swept = NewInbox(store).Sweep(VoiceInbox.KeepFinished, now);

        Assert.Equal(2, swept);
        Assert.False(File.Exists(SidecarPath(old)));
        Assert.False(File.Exists(SidecarPath(oldFailed)));
        Assert.True(File.Exists(SidecarPath(fresh)));
        // **待っている物は日付に関わらず残す**（掃除で仕事を捨てない）
        Assert.True(File.Exists(SidecarPath(waiting)));
        Assert.True(AudioExists(waiting));
    }

    [Fact]
    public void 受け箱が無くても掃除は落ちない()
    {
        Assert.Equal(0, NewInbox(NewStore()).Sweep(VoiceInbox.KeepFinished, DateTimeOffset.UtcNow));
    }

    private string SidecarPath(string id) =>
        Path.Combine(Paths.VoicesDir, VoiceInbox.InboxSubdirectory, id + ".json");

    // ---- 名付けの規則＝wrapper と同じ 1 枚の逐語 -----------------------------

    [Fact]
    public void 名付けの規則はwrapperと同じ1枚から読む()
    {
        // tests/contract/voice-name-cases.json を Python 側（validate_voice_name）と
        // この席の VoiceNameValidator が**同じ判定**で通る／落とすことを確かめる。
        // リポの根が見つからない機体（成果物だけの実行）では黙って戻る
        // ＝台帳の実物を読む隣の試験と同じ作法（RuntimeInstallerTests・VcRedistAndModelsTests）。
        // 第三者パッケージを増やさないので SkippableFact は使わない。
        var repo = RepoLedger.RepoRoot();
        if (repo is null)
        {
            return;
        }

        var path = Path.Combine(repo, "tests", "contract", "voice-name-cases.json");
        Assert.True(File.Exists(path), path);

        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var root = document.RootElement;
        Assert.Equal(VoiceNameValidator.MaxLength, root.GetProperty("max_length_utf16").GetInt32());

        var seen = 0;
        foreach (var element in root.GetProperty("cases").EnumerateArray())
        {
            var name = element.GetProperty("name").GetString()!;
            var valid = element.GetProperty("valid").GetBoolean();
            var why = element.GetProperty("why").GetString();

            var error = VoiceNameValidator.Validate(name, null);
            Assert.True(valid == (error is null), why + "／" + name);
            seen++;
        }

        Assert.True(seen >= 20, "逐語が痩せていないか（" + seen + " 件）");
    }

    // ---- 1 行の綴り（純関数） -----------------------------------------------

    [Fact]
    public void 本体の名乗りは日本語の呼び名になる()
    {
        Assert.Equal(
            UiStrings.VoiceIntakeHontaiName,
            VoicesViewModel.IntakeAppName(VoiceInbox.HontaiClientId));
        Assert.Equal("myapp", VoicesViewModel.IntakeAppName("myapp"));
        Assert.Equal(UiStrings.VoiceIntakeAppUnknown, VoicesViewModel.IntakeAppName(null));
        Assert.Equal(UiStrings.VoiceIntakeAppUnknown, VoicesViewModel.IntakeAppName("  "));
        // 相手が入れてくる字はそのまま出さない（制御文字・長すぎ）
        Assert.Equal(UiStrings.VoiceIntakeAppUnknown, VoicesViewModel.IntakeAppName("a\u0007b"));
        Assert.Equal(
            UiStrings.VoiceIntakeAppUnknown,
            VoicesViewModel.IntakeAppName(new string('a', VoicesViewModel.IntakeAppNameMax + 1)));
    }

    [Fact]
    public void 結末の1行は受け取れたかで2通り()
    {
        var ok = VoicesViewModel.IntakeLine(
            new VoiceInboxResult("a", "ずんだもん", VoiceInbox.HontaiClientId, true, null));
        Assert.Equal("読み分けちゃん2 から声「ずんだもん」を受け取りました。", ok);

        var ng = VoicesViewModel.IntakeLine(
            new VoiceInboxResult("a", "ずんだもん", null, false, "同じ名前の声があります。"));
        Assert.Equal(
            "ほかのアプリ から声「ずんだもん」を受け取れませんでした（同じ名前の声があります。）。", ng);
    }

    // ---- 画面（VoicesViewModel）------------------------------------------

    [Fact]
    public async Task 標本のinboxが正なら受け箱を片付けて1行残す()
    {
        var paths = Paths;
        var store = NewStore();
        var inbox = NewInbox(store);
        var id = Drop("標本から増えた声");

        var log = new List<string>();
        var vm = new VoicesViewModel(
            store, new VoicesJsonWriter(), new SilentPlayer(), static () => null,
            paths, new LauncherSettings(), inbox)
        {
            Log = log.Add,
        };

        Assert.Equal(1, await vm.ProcessInboxAsync());

        Assert.Contains("標本から増えた声", store.Load().Voices.Keys);
        Assert.Equal("done", Sidecar(id)["state"].GetString());
        Assert.Equal(["読み分けちゃん2 から声「標本から増えた声」を受け取りました。"], log);
        Assert.Contains("受け取りました", vm.Message, StringComparison.Ordinal);
        Assert.Contains(vm.Rows, r => r.Id == "標本から増えた声");
    }

    [Fact]
    public async Task 受け箱が空なら何も起きない()
    {
        var store = NewStore();
        var log = new List<string>();
        var vm = new VoicesViewModel(
            store, new VoicesJsonWriter(), new SilentPlayer(), static () => null,
            Paths, new LauncherSettings(), NewInbox(store))
        {
            Log = log.Add,
        };

        Assert.Equal(0, await vm.ProcessInboxAsync());
        Assert.Empty(log);
    }

    [Fact]
    public void 受け箱が差さっていなければ標本を配っても何も起きない()
    {
        var vm = new VoicesViewModel(
            NewStore(), new VoicesJsonWriter(), new SilentPlayer(), static () => null,
            Paths, new LauncherSettings());

        // 口が無い個体（欄ごと無い）も、件数が 0 の個体も、素通り
        vm.ApplyInbox(null);
        vm.ApplyInbox(new StatusVoices { Count = 1, Inbox = 3 });
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task 画面からの追加と同じ1つの錠を分け合う()
    {
        // 「声を追加する」と受け箱が同時に台帳へ書くと、後から書いたほうが
        // 先の 1 名を消してしまう（台帳は読んで書き直す形）。錠は 1 つである。
        using var gate = new ManualResetEventSlim(false);
        var blocking = new BlockingInbox(gate);
        var vm = new VoicesViewModel(
            NewStore(), new VoicesJsonWriter(), new SilentPlayer(), static () => null,
            Paths, new LauncherSettings(), blocking);

        var first = vm.ProcessInboxAsync();

        // 1 本目が受け箱を掴んでいる間は、2 本目も画面からの追加も入れない
        Assert.True(SpinWait.SpinUntil(() => vm.IsBusy, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, await vm.ProcessInboxAsync());
        Assert.False(await vm.AddVoiceAsync("割り込む声", Path.Combine(_root, "x.wav"), null));

        gate.Set();
        await first;

        Assert.Equal(1, blocking.Calls);
        Assert.False(vm.IsBusy);
    }

    // ---- 状態の欄（契約 ⑹）--------------------------------------------------

    [Fact]
    public void 標本のinboxは欄が無ければ0と読む()
    {
        var without = JsonSerializer.Deserialize<StatusResponse>(
            """{"engine":"irodori-ywk","voices":{"count":1,"dir":"voices","error":null}}""")!;
        Assert.Null(without.Voices!.Inbox);
        Assert.Equal(0, without.Voices.Pending);

        var with = JsonSerializer.Deserialize<StatusResponse>(
            """{"engine":"irodori-ywk","voices":{"count":1,"dir":"voices","error":null,"inbox":2}}""")!;
        Assert.Equal(2, with.Voices!.Inbox);
        Assert.Equal(2, with.Voices.Pending);

        // 負の数は来ない約束だが、来ても 0 に丸める
        var negative = JsonSerializer.Deserialize<StatusResponse>(
            """{"voices":{"inbox":-3}}""")!;
        Assert.Equal(0, negative.Voices!.Pending);
    }

    // ---- 継ぎ目 -------------------------------------------------------------

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

    /// <summary>足そうとすると投げる台帳（失敗の態を作る）。</summary>
    private sealed class ThrowingStore(Exception error) : IVoiceStore
    {
        public VoicesYwkFile Load() => VoiceStore.Empty();

        public void Save(VoicesYwkFile file)
        {
        }

        public string MakeAsciiId(string wavSha256Hex) => "ywk-000000000000";

        public VoicesYwkFile AddVoice(string displayName, string sourceWavPath, string? caption) =>
            throw (error is ArgumentException or IOException or UnauthorizedAccessException
                ? error
                : new IOException(error.Message));

        public VoicesYwkFile RemoveVoice(string voiceId) => VoiceStore.Empty();

        public VoiceRemoval RemoveVoiceDetailed(string voiceId) =>
            new(VoiceStore.Empty(), false, []);
    }

    /// <summary>掛け金が開くまで返らない受け箱（錠を試す）。</summary>
    private sealed class BlockingInbox(ManualResetEventSlim gate) : IVoiceInbox
    {
        public int Calls { get; private set; }

        public string InboxDir => "inbox";

        public IReadOnlyList<VoiceInboxItem> ListQueued() => [];

        public IReadOnlyList<VoiceInboxResult> ProcessAll()
        {
            Calls++;
            gate.Wait(TimeSpan.FromSeconds(30));
            return [];
        }

        public int Sweep(TimeSpan keep, DateTimeOffset now) => 0;
    }
}
