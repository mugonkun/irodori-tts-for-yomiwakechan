using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Voices;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 話者台帳と <c>voices.json</c>（契約 ⑷・⑺・受け入れ条件 D-3）。
/// 檔は一時ディレクトリだけを触る（導入先にも <c>%LOCALAPPDATA%</c> にも書かない）。
/// </summary>
public sealed class VoicesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-voices-tests-" + Guid.NewGuid().ToString("N")[..8]);

    private string VoicesDir => Path.Combine(_root, "voices");

    /// <summary>写した参照 wav の置き場（<c>voices_dir</c> 直下には置かない＝是正・2026-09-05）。</summary>
    private string RefsDir => Path.Combine(VoicesDir, VoiceStore.ReferencesSubdirectory);

    private string TablePath => Path.Combine(VoicesDir, "voices.ywk.json");

    private string AliasPath => Path.Combine(VoicesDir, "voices.json");

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
            // 消し損ねは無害
        }
    }

    private VoiceStore NewStore() => new(TablePath, VoicesDir);

    private string MakeWav(string name, byte fill)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
        var bytes = new byte[1024];
        Array.Fill(bytes, fill);
        // RIFF/WAVE の頭だけ本物らしくしておく（内容は sha256 の材料でしかない）
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void 台帳が無くてもデフォルトが1件返る()
    {
        // 裁定 16＝参照なしの話者が一覧に常在する
        var table = NewStore().Load();

        var entry = Assert.Contains(VoiceIds.Default, table.Voices);
        Assert.True(entry.NoRef);
        Assert.Null(entry.File);
    }

    [Fact]
    public void 台帳が壊れていてもデフォルトで立ち上がる()
    {
        Directory.CreateDirectory(VoicesDir);
        File.WriteAllText(TablePath, "{ this is not json");

        var store = NewStore();
        var table = store.Load();

        Assert.Single(table.Voices);
        Assert.NotNull(store.LastLoadError);
    }

    [Fact]
    public void 追加は2操作で檔名がASCIIになる()
    {
        // 受け入れ条件 D-3＝wav を選ぶ＋名前を付ける（日本語可）
        var store = NewStore();
        var wav = MakeWav("source.wav", 0x11);

        var table = store.AddVoice("琴葉茜", wav, "関西弁で明るく");

        var entry = Assert.Contains("琴葉茜", table.Voices);
        Assert.NotNull(entry.File);
        Assert.StartsWith("ywk-", entry.File!, StringComparison.Ordinal);
        Assert.EndsWith(".wav", entry.File!, StringComparison.Ordinal);
        Assert.Equal("user", entry.Origin);
        Assert.Equal("関西弁で明るく", entry.Caption);

        foreach (var c in entry.File!)
        {
            Assert.True(c < 128, "檔名は ASCII（話者名は日本語でよい）");
        }

        // voices_dir 直下ではなく refs/ に写る（上流の走査が幽霊行を作らない）
        Assert.True(File.Exists(Path.Combine(RefsDir, entry.File!)));
        Assert.False(File.Exists(Path.Combine(VoicesDir, entry.File!)));
    }

    [Fact]
    public void 檔名は内容のsha256の先頭12桁になる()
    {
        var store = NewStore();
        var wav = MakeWav("source.wav", 0x22);
        var sha = VoiceStore.Sha256OfFile(wav);

        var table = store.AddVoice("話者A", wav, null);

        Assert.Equal("ywk-" + sha[..12] + ".wav", table.Voices["話者A"].File);
        Assert.Equal("ywk-" + sha[..12], store.MakeAsciiId(sha));
    }

    [Fact]
    public void 同じ音を2名で登録しても檔は1つ()
    {
        var store = NewStore();
        var wav = MakeWav("source.wav", 0x33);

        store.AddVoice("話者A", wav, null);
        var table = store.AddVoice("話者B", wav, null);

        Assert.Equal(table.Voices["話者A"].File, table.Voices["話者B"].File);
        Assert.Single(Directory.GetFiles(RefsDir, "*.wav"));
        // voices_dir 直下には wav を 1 檔も置かない（走査で id が増えない）
        Assert.Empty(Directory.GetFiles(VoicesDir, "*.wav"));
    }

    [Fact]
    public void 削除しても他名が使っている檔は消さない()
    {
        var store = NewStore();
        var wav = MakeWav("source.wav", 0x44);
        store.AddVoice("話者A", wav, null);
        var table = store.AddVoice("話者B", wav, null);
        var fileName = table.Voices["話者A"].File!;

        store.RemoveVoice("話者A");

        Assert.True(File.Exists(Path.Combine(RefsDir, fileName)));

        store.RemoveVoice("話者B");
        Assert.False(File.Exists(Path.Combine(RefsDir, fileName)));
    }

    [Fact]
    public void デフォルトは消せない()
    {
        var store = NewStore();
        Assert.Throws<ArgumentException>(() => store.RemoveVoice(VoiceIds.Default));
    }

    [Fact]
    public void デフォルトの名前で上書き登録できない()
    {
        var store = NewStore();
        var wav = MakeWav("source.wav", 0x55);
        Assert.Throws<ArgumentException>(() => store.AddVoice(VoiceIds.Default, wav, null));
    }

    [Fact]
    public void 参照に使えない拡張子は弾く()
    {
        var store = NewStore();
        var text = MakeWav("source.txt", 0x66);
        Assert.Throws<ArgumentException>(() => store.AddVoice("話者A", text, null));
    }

    [Fact]
    public void voicesJsonはデフォルトが先頭で日本語名がそのまま載る()
    {
        var store = NewStore();
        var wav = MakeWav("source.wav", 0x77);
        var table = store.AddVoice("琴葉茜", wav, null);

        var json = new VoicesJsonWriter().Render(table);

        // 日本語を \uXXXX に潰さない（話者名が日本語＝契約 ⑷ 4-1）
        Assert.Contains("琴葉茜", json, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(json);
        var keys = new List<string>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            keys.Add(property.Name);
        }

        Assert.Equal(VoiceIds.Default, keys[0]);
        Assert.True(document.RootElement.GetProperty(VoiceIds.Default).GetProperty("no_ref").GetBoolean());
        // 相対パスは refs/ 前置（上流が voices_dir から解決する＝voices.py の _resolve_voice_path）
        Assert.Equal(
            VoicesJsonWriter.ReferencesPrefix + table.Voices["琴葉茜"].File,
            document.RootElement.GetProperty("琴葉茜").GetProperty("ref_wav").GetString());
    }

    [Fact]
    public void 焼いた話者はrefLatentで鳴りrefWavは残らない()
    {
        // 契約 ⑷ 4-4＝上流は波形と潜在の同時指定を 400 にする
        var voices = new Dictionary<string, VoiceEntry>(StringComparer.Ordinal)
        {
            ["琴葉茜"] = new VoiceEntry
            {
                DisplayName = "琴葉茜",
                File = "ywk-0123456789ab.wav",
                RefLatent = "latents/ywk-0123456789ab.pt",
            },
        };

        var json = new VoicesJsonWriter().Render(
            VoiceStore.EnsureDefault(new VoicesYwkFile { Voices = voices }));

        using var document = JsonDocument.Parse(json);
        var entry = document.RootElement.GetProperty("琴葉茜");
        Assert.Equal("latents/ywk-0123456789ab.pt", entry.GetProperty("ref_latent").GetString());
        Assert.False(entry.TryGetProperty("ref_wav", out _));
    }

    [Fact]
    public void wrapperが書いた潜在を上書きで消さない()
    {
        // 契約 ⑷ 4-3＝voices.json を書くのはランチャと wrapper の 2 つ＝書くときに読み直す
        var writer = new VoicesJsonWriter();
        var store = NewStore();
        var wav = MakeWav("source.wav", 0x88);
        var table = store.AddVoice("琴葉茜", wav, null);

        writer.Write(AliasPath, table);

        Assert.Contains("ref_wav", File.ReadAllText(AliasPath), StringComparison.Ordinal);

        // wrapper が事前計算の後に 1 欄だけ差し替えた、という状態を作る
        File.WriteAllText(
            AliasPath,
            "{\"" + VoiceIds.Default + "\":{\"no_ref\":true},"
            + "\"琴葉茜\":{\"ref_latent\":\"latents/ywk-abc.pt\"}}",
            new UTF8Encoding(false));

        // ランチャが別の話者を足して書き直しても、焼いた潜在は残る
        var wav2 = MakeWav("source2.wav", 0x99);
        writer.Write(AliasPath, store.AddVoice("月読アイ", wav2, null));

        using var document = JsonDocument.Parse(File.ReadAllText(AliasPath));
        Assert.Equal(
            "latents/ywk-abc.pt",
            document.RootElement.GetProperty("琴葉茜").GetProperty("ref_latent").GetString());
        Assert.True(document.RootElement.TryGetProperty("月読アイ", out _));
    }

    [Fact]
    public void 参照も潜在も無い話者はvoicesJsonに載せない()
    {
        var voices = new Dictionary<string, VoiceEntry>(StringComparer.Ordinal)
        {
            ["壊れた話者"] = new VoiceEntry { DisplayName = "壊れた話者" },
        };

        var json = new VoicesJsonWriter().Render(
            VoiceStore.EnsureDefault(new VoicesYwkFile { Voices = voices }));

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("壊れた話者", out _));
        Assert.True(document.RootElement.TryGetProperty(VoiceIds.Default, out _));
    }

    [Fact]
    public void voicesJsonの書き換えは原子的で残骸を残さない()
    {
        var writer = new VoicesJsonWriter();
        writer.Write(AliasPath, VoiceStore.Empty());

        Assert.True(File.Exists(AliasPath));
        Assert.False(File.Exists(AliasPath + ".tmp"));
    }

    [Fact]
    public void 一覧の並びはデフォルトが先頭()
    {
        var order = VoiceIds.Order(["月読アイ", VoiceIds.Default, "琴葉茜"], ["琴葉茜"]);

        Assert.Equal(VoiceIds.Default, order[0]);
        Assert.Equal("琴葉茜", order[1]);
        Assert.Equal(3, order.Count);
    }

    [Fact]
    public void 上流由来のnoneの別名は参照なしと読む()
    {
        // 一覧には出さない（同じ意味の話者が 2 つ見える状態を作らない＝契約 ⑷ 4-1）
        Assert.True(VoiceIds.IsNoRef("none"));
        Assert.True(VoiceIds.IsNoRef("NO-REF"));
        Assert.True(VoiceIds.IsNoRef(VoiceIds.Default));
        Assert.False(VoiceIds.IsNoRef("琴葉茜"));
    }

    [Fact]
    public void 参照の長さは10から30秒を勧める()
    {
        Assert.Null(VoiceStore.AdviseReferenceLength(TimeSpan.FromSeconds(20)));
        Assert.Contains("短め", VoiceStore.AdviseReferenceLength(TimeSpan.FromSeconds(3))!, StringComparison.Ordinal);
        Assert.Contains("長め", VoiceStore.AdviseReferenceLength(TimeSpan.FromSeconds(90))!, StringComparison.Ordinal);
    }

    [Fact]
    public void プリセットは初回だけ写される()
    {
        // 配布樹（読むだけ）を真似た形を作る
        var appDir = Path.Combine(_root, "app");
        var appVoices = Path.Combine(appDir, "voices");
        Directory.CreateDirectory(appVoices);
        File.WriteAllBytes(Path.Combine(appVoices, "vv_mochiko_sexy.wav"), new byte[512]);
        File.WriteAllText(
            Path.Combine(appVoices, "voices.ywk.json"),
            """
            {"schema":1,"voices":{
              "デフォルト":{"display_name":"デフォルト","preset":true,"no_ref":true},
              "もち子さん":{"display_name":"もち子さん","file":"vv_mochiko_sexy.wav","preset":true,"caption":"落ち着いた声"}
            }}
            """,
            new UTF8Encoding(false));

        var paths = AppPaths.Resolve(
            appDir,
            null,
            name => name == AppPaths.AppDirEnvName ? appDir
                : name == AppPaths.DataDirEnvName ? _root
                : null);

        var store = NewStore();
        var copied = PresetVoices.InstallIfFirstRun(paths, store);

        Assert.Equal(1, copied);
        Assert.True(File.Exists(Path.Combine(RefsDir, "vv_mochiko_sexy.wav")));

        var table = store.Load();
        Assert.True(table.Voices["もち子さん"].Preset);
        Assert.Equal("落ち着いた声", table.Voices["もち子さん"].Caption);
        Assert.True(table.Voices.ContainsKey(VoiceIds.Default));

        // 2 回目は走らない（利用者が消したプリセットを書き戻さない）
        store.RemoveVoice("もち子さん");
        Assert.Equal(0, PresetVoices.InstallIfFirstRun(paths, store));
        Assert.False(store.Load().Voices.ContainsKey("もち子さん"));

        // 設計書 §4「再インストールで戻る」＝入れ直す口が要る（是正・2026-09-05）。
        Assert.Equal(1, PresetVoices.Restore(paths, store));
        Assert.True(store.Load().Voices.ContainsKey("もち子さん"));
    }

    [Fact]
    public void プリセットが0件の古い台帳には1度だけ入れ直す()
    {
        // 所見 12 の釘＝presets.json を読めていなかった回に作られた台帳（プリセット 0 件）を
        // 持つ利用者へ直した版を配っても、「台帳が在る」だけの判定では 12 名が永久に入らない。
        var paths = MakePresetTree();
        var store = NewStore();

        // 印の無い古い台帳（「デフォルト」だけ）を置く
        store.Save(VoiceStore.Empty());
        Assert.False(store.Load().PresetsInstalled);

        Assert.Equal(1, PresetVoices.InstallIfFirstRun(paths, store));
        Assert.True(store.Load().Voices.ContainsKey("もち子さん"));
        Assert.True(store.Load().PresetsInstalled);

        // 印が付いた後は 2 度と書き戻さない
        store.RemoveVoice("もち子さん");
        Assert.Equal(0, PresetVoices.InstallIfFirstRun(paths, store));
    }

    [Fact]
    public void プリセットの正本はpresets配列で話者idは日本語の表示名になる()
    {
        // 所見 12 の釘＝実物の voices/presets.json はトップレベルが `presets`（**配列**）で、
        // 檔は secondary.file、話者 id は display_name（裁定 17・契約 ⑷ 4-1）。
        var appDir = Path.Combine(_root, "app");
        var appVoices = Path.Combine(appDir, "voices");
        var presetWavs = Path.Combine(appVoices, "presets");
        Directory.CreateDirectory(presetWavs);
        File.WriteAllBytes(Path.Combine(presetWavs, "vr2_akane_west.wav"), new byte[512]);
        File.WriteAllBytes(Path.Combine(presetWavs, "vv_mochiko_sexy.wav"), new byte[256]);
        File.WriteAllText(
            Path.Combine(appVoices, "presets.json"),
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

        var paths = PathsFor(appDir);
        var discovered = PresetVoices.Discover(paths);

        // status=skipped の行（裁定 27）は檔が無いので出さない
        Assert.Equal(2, discovered.Count);
        Assert.Contains(discovered, p => p.Id == "琴葉茜（関西弁）" && p.FileName == "vr2_akane_west.wav");
        Assert.Contains(discovered, p => p.Id == "もち子さん");
        Assert.DoesNotContain(discovered, p => p.Id == "vv_mochiko_sexy");

        var store = NewStore();
        Assert.Equal(2, PresetVoices.InstallIfFirstRun(paths, store));

        var table = store.Load();
        Assert.True(table.Voices["琴葉茜（関西弁）"].Preset);
        // 写る先は refs/（voices_dir 直下ではない）
        Assert.True(File.Exists(Path.Combine(RefsDir, "vr2_akane_west.wav")));
        Assert.Empty(Directory.GetFiles(VoicesDir, "*.wav"));
    }

    [Fact]
    public void 削除は潜在とsidecarも消して台帳に居たかを返す()
    {
        // 所見 13 の釘＝契約 ⑷ 4-3 の 4 檔のうち檔の 3 つ。stem は話者 id 由来
        // （server/ywk_server.py:2127-2139）なので、サーバが止まっていても場所は決まる。
        var store = NewStore();
        var wav = MakeWav("source.wav", 0xAB);
        store.AddVoice("テスト話者", wav, null);

        // wrapper が焼いた姿を作る（latents/<stem>.pt ＋ sidecar）
        var stem = VoiceStore.LatentStem("テスト話者");
        var latents = Path.Combine(VoicesDir, "latents");
        Directory.CreateDirectory(latents);
        File.WriteAllBytes(Path.Combine(latents, stem + ".pt"), new byte[16]);
        File.WriteAllText(Path.Combine(latents, stem + ".json"), "{}");

        var removal = store.RemoveVoiceDetailed("テスト話者");

        Assert.True(removal.WasKnown);
        Assert.False(removal.Table.Voices.ContainsKey("テスト話者"));
        Assert.False(File.Exists(Path.Combine(latents, stem + ".pt")));
        Assert.False(File.Exists(Path.Combine(latents, stem + ".json")));
        Assert.Empty(Directory.GetFiles(RefsDir, "*.wav"));

        // 台帳に居ない名は「何も起きなかった」と返る（画面が嘘をつかない）
        var ghost = store.RemoveVoiceDetailed("ywk-c962284a59da");
        Assert.False(ghost.WasKnown);
        Assert.Empty(ghost.RemovedFiles);
    }

    [Fact]
    public void 潜在の檔名の幹はwrapperと同じ規則で組む()
    {
        // server/ywk_server.py の latent_stem＝ASCII 以外を "-" に畳み、
        // 話者 id の sha256 の先頭 12 桁を後ろに付ける（ASCII が残らなければ "ywk-"）。
        var japanese = VoiceStore.LatentStem("テスト話者");
        Assert.StartsWith("ywk-", japanese, StringComparison.Ordinal);
        Assert.Equal(4 + 12, japanese.Length);

        var mixed = VoiceStore.LatentStem("akane_west");
        Assert.StartsWith("akane_west-", mixed, StringComparison.Ordinal);

        // 名が違えば幹も違う（同じ場所に前の話者の潜在が居座らない）
        Assert.NotEqual(VoiceStore.LatentStem("あ"), VoiceStore.LatentStem("い"));
    }

    [Fact]
    public void 参照に使える拡張子は実行系が読める形だけ()
    {
        // 所見 17 の釘＝裁定 30 で torchcodec を外したので復号は soundfile 一本。
        // 組んだ実行系の実測（libsndfile 1.2.2）＝m4a・aac・wma は 1 つも読めない。
        Assert.Equal([".wav", ".mp3", ".flac", ".ogg", ".opus"], VoiceIds.WavExtensions);
        Assert.DoesNotContain(".m4a", VoiceIds.WavExtensions);
        Assert.DoesNotContain(".aac", VoiceIds.WavExtensions);
        Assert.DoesNotContain(".wma", VoiceIds.WavExtensions);

        // 画面の文言と檔窓の絞りは同じ定数から作る
        Assert.Equal("wav・mp3・flac・ogg・opus", VoiceIds.ExtensionsText);
        Assert.Contains(VoiceIds.ExtensionsText, VoicesViewModel.ReferenceLengthNotice, StringComparison.Ordinal);

        // 落とす形式は 0 s で弾く（合成まで持ち越さない）
        Assert.NotNull(VoiceNameValidator.ValidateSourceFile(@"C:\x\voice.m4a"));
        Assert.Null(VoiceNameValidator.ValidateSourceFile(@"C:\x\voice.wav"));
    }

    [Fact]
    public void 旧い置き場の参照wavはrefsへ移る()
    {
        // 是正・2026-09-05 より前に登録した個体のための移行。
        var store = NewStore();
        var wav = MakeWav("source.wav", 0xCD);
        var table = store.AddVoice("話者A", wav, null);
        var fileName = table.Voices["話者A"].File!;

        // 旧い姿（voices_dir 直下）に戻す
        File.Move(Path.Combine(RefsDir, fileName), Path.Combine(VoicesDir, fileName));

        Assert.Equal(1, store.MigrateReferences());
        Assert.True(File.Exists(Path.Combine(RefsDir, fileName)));
        Assert.False(File.Exists(Path.Combine(VoicesDir, fileName)));

        // 2 度目は何もしない
        Assert.Equal(0, store.MigrateReferences());
    }

    [Fact]
    public void プリセットが1檔も無い配布樹では印を立てない()
    {
        // build/assemble-app.ps1 はまだ voices/presets/ を写していない（便 A へ票）。
        // その版で印を立てると、檔が入った版に更新しても 12 名が永久に入らない。
        var appDir = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(appDir, "voices"));
        var paths = PathsFor(appDir);
        var store = NewStore();

        Assert.Equal(0, PresetVoices.InstallIfFirstRun(paths, store));
        Assert.False(store.Load().PresetsInstalled);

        // 後から檔が入った版に更新すれば、そこで入る
        var appVoices = Path.Combine(appDir, "voices");
        File.WriteAllBytes(Path.Combine(appVoices, "vv_mochiko_sexy.wav"), new byte[512]);
        File.WriteAllText(
            Path.Combine(appVoices, "voices.ywk.json"),
            """
            {"schema":1,"voices":{
              "もち子さん":{"display_name":"もち子さん","file":"vv_mochiko_sexy.wav","preset":true}
            }}
            """,
            new UTF8Encoding(false));

        Assert.Equal(1, PresetVoices.InstallIfFirstRun(paths, store));
        Assert.True(store.Load().PresetsInstalled);
    }

    private AppPaths MakePresetTree()
    {
        var appDir = Path.Combine(_root, "app");
        var appVoices = Path.Combine(appDir, "voices");
        Directory.CreateDirectory(appVoices);
        File.WriteAllBytes(Path.Combine(appVoices, "vv_mochiko_sexy.wav"), new byte[512]);
        File.WriteAllText(
            Path.Combine(appVoices, "voices.ywk.json"),
            """
            {"schema":1,"voices":{
              "デフォルト":{"display_name":"デフォルト","preset":true,"no_ref":true},
              "もち子さん":{"display_name":"もち子さん","file":"vv_mochiko_sexy.wav","preset":true}
            }}
            """,
            new UTF8Encoding(false));

        return PathsFor(appDir);
    }

    private AppPaths PathsFor(string appDir) => AppPaths.Resolve(
        appDir,
        null,
        name => name == AppPaths.AppDirEnvName ? appDir
            : name == AppPaths.DataDirEnvName ? _root
            : null);
}
