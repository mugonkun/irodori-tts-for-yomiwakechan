using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services;
using IrodoriTtsYwk.Launcher.Services.Voices;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// プリセットの<b>改名の引き継ぎ</b>（裁定 108＝2026-09-07 の
/// 「もち子さん」→「もち子さん（セクシー／あん子）」）。
/// <para>
/// 表示名がそのまま話者 id である（裁定 17・契約 ⑷ 4-1）ので、配布樹の <c>presets.json</c> を
/// 直しただけでは既存の利用者の台帳が旧い id を持ち続け、<b>入れ直すと 2 名に増える</b>。
/// ここが釘付けするのはその引き継ぎ＝何を持ち越し・何を消し・どこで<b>やらない</b>か。
/// </para>
/// 檔は一時ディレクトリだけを触る（導入先にも <c>%LOCALAPPDATA%</c> にも書かない）。
/// </summary>
public sealed class PresetRenameTests : IDisposable
{
    /// <summary>改名の前の名（＝旧い話者 id）。</summary>
    private const string OldId = "もち子さん";

    /// <summary>改名の後の名（司令官の指示・全角の括弧と斜線）。</summary>
    private const string NewId = "もち子さん（セクシー／あん子）";

    /// <summary>もう 1 名（改名に巻き込まれないことの対照）。</summary>
    private const string OtherId = "琴葉茜（関西弁）";

    /// <summary><c>sha256(utf8("もち子さん"))</c> の先頭 12 桁＝旧い潜在の幹。</summary>
    private const string OldStem = "ywk-75b15fb1eea7";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-rename-" + Guid.NewGuid().ToString("N")[..8]);

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

    private VoiceStore NewStore(AppPaths paths) =>
        new(paths.VoicesYwkJsonPath, paths.VoicesDir, paths.ReferenceWavDir);

    /// <summary>配布樹＝<c>voices/presets/*.wav</c> と、<b>新しい表示名</b>を名乗る正本。</summary>
    private static void MakeManifest(AppPaths paths, bool withJson = true)
    {
        Directory.CreateDirectory(paths.PresetVoicesDir);
        File.WriteAllBytes(Path.Combine(paths.PresetVoicesDir, "vv_mochiko_sexy.wav"), new byte[256]);
        File.WriteAllBytes(Path.Combine(paths.PresetVoicesDir, "vr2_akane_west.wav"), new byte[512]);
        if (!withJson)
        {
            return; // 正本の無い配布樹＝Discover は檔名の幹（ASCII）へ落ちる
        }

        File.WriteAllText(
            paths.PresetsJsonPath,
            """
            {"version":1,"presets":[
              {"id":"vv_mochiko_sexy","display_name":"もち子さん（セクシー／あん子）","status":"done",
               "caption":"落ち着いた声","secondary":{"file":"vv_mochiko_sexy.wav"}},
              {"id":"vr2_akane_west","display_name":"琴葉茜（関西弁）","status":"done",
               "secondary":{"file":"vr2_akane_west.wav"}},
              {"id":"cevio_maki_en","display_name":"弦巻マキ（英語）","status":"skipped",
               "secondary":null}]}
            """,
            new UTF8Encoding(false));
    }

    /// <summary>利用者データ側＝写した参照 wav（改名しても<b>消してはいけない</b>檔）。</summary>
    private static void MakeReference(AppPaths paths, string fileName)
    {
        Directory.CreateDirectory(paths.ReferenceWavDir);
        File.WriteAllBytes(Path.Combine(paths.ReferenceWavDir, fileName), new byte[256]);
    }

    /// <summary>wrapper が焼いた潜在と sidecar（幹は話者 id 由来＝改名で当たらなくなる）。</summary>
    private static void MakeLatent(AppPaths paths, string stem)
    {
        Directory.CreateDirectory(paths.LatentsDir);
        File.WriteAllBytes(Path.Combine(paths.LatentsDir, stem + ".pt"), new byte[64]);
        File.WriteAllText(Path.Combine(paths.LatentsDir, stem + ".json"), "{\"voice_id\":\"" + OldId + "\"}");
    }

    /// <summary>
    /// 台帳をそのまま書く（改名前の実機の姿＝<c>presets_installed</c> は既定で立っている）。
    /// <paramref name="installed"/> を落とすと<b>印の無い台帳</b>＝
    /// <c>presets.json</c> を読めていなかった回の残骸になる。
    /// </summary>
    /// <param name="offeredIds">
    /// <c>presets_installed_ids</c>（裁定 121＝この台帳へ 1 度でも差し出したプリセット）。
    /// null＝欄そのものが無い台帳（≦ v1.0.1 が書いた姿）。
    /// </param>
    private static void WriteTable(
        AppPaths paths, string voicesJson, bool installed = true, string[]? offeredIds = null)
    {
        Directory.CreateDirectory(paths.VoicesDir);
        var ids = offeredIds is null
            ? string.Empty
            : ",\"presets_installed_ids\":[\""
              + string.Join("\",\"", offeredIds) + "\"]";
        File.WriteAllText(
            paths.VoicesYwkJsonPath,
            "{\"schema\":1" + (installed ? ",\"presets_installed\":true" : string.Empty) + ids
            + ",\"voices\":{" + voicesJson + "}}",
            new UTF8Encoding(false));
    }

    private static string PresetEntry(string id, string file, string? caption, string? refLatent) =>
        "\"" + id + "\":{\"display_name\":\"" + id + "\",\"file\":\"" + file + "\""
        + (caption is null ? string.Empty : ",\"caption\":\"" + caption + "\"")
        + ",\"preset\":true,\"origin\":\"preset\""
        + (refLatent is null ? string.Empty : ",\"ref_latent\":\"" + refLatent + "\"")
        + "}";

    private static string DefaultEntry() =>
        "\"" + VoiceIds.Default + "\":{\"display_name\":\"" + VoiceIds.Default
        + "\",\"no_ref\":true,\"preset\":true,\"origin\":\"preset\"}";

    // ---- ⒜ 旧い台帳は起動の 1 回で改まる -----------------------------------

    [Fact]
    public void 起動で旧い名の台帳が新しい名へ改まる()
    {
        var paths = Paths;
        MakeManifest(paths);
        MakeReference(paths, "vv_mochiko_sexy.wav");
        MakeLatent(paths, OldStem);
        WriteTable(
            paths,
            DefaultEntry() + ","
            + PresetEntry(OldId, "vv_mochiko_sexy.wav", "落ち着いた声", "latents/" + OldStem + ".pt"));

        // wrapper が書いた別名表（旧い鍵＋焼いた潜在）
        Directory.CreateDirectory(paths.VoicesDir);
        File.WriteAllText(
            paths.VoicesJsonPath,
            "{\"" + VoiceIds.Default + "\":{\"no_ref\":true},"
            + "\"" + OldId + "\":{\"ref_latent\":\"latents/" + OldStem + ".pt\"}}",
            new UTF8Encoding(false));

        var store = NewStore(paths);
        LauncherComposition.PrepareVoices(paths, store, new VoicesJsonWriter(), out var renamed);

        var rename = Assert.Single(renamed);
        Assert.Equal(OldId, rename.OldId);
        Assert.Equal(NewId, rename.NewId);
        Assert.Equal("vv_mochiko_sexy.wav", rename.FileName);

        var table = store.Load();
        Assert.DoesNotContain(OldId, table.Voices.Keys);
        var entry = Assert.Contains(NewId, table.Voices);
        Assert.Equal(NewId, entry.DisplayName);
        Assert.Equal("vv_mochiko_sexy.wav", entry.File);
        Assert.Equal("落ち着いた声", entry.Caption);
        Assert.True(entry.Preset);
        Assert.Equal("preset", entry.Origin);
        Assert.Null(entry.RefLatent); // 幹は id 由来＝旧い潜在は新しい名では当たらない

        // 旧い id の潜在と sidecar は消え、参照 wav は残る（新しい名が同じ檔を指す）
        Assert.False(File.Exists(Path.Combine(paths.LatentsDir, OldStem + ".pt")));
        Assert.False(File.Exists(Path.Combine(paths.LatentsDir, OldStem + ".json")));
        Assert.True(File.Exists(Path.Combine(paths.ReferenceWavDir, "vv_mochiko_sexy.wav")));

        // 別名表も鍵が入れ替わる（旧い鍵の ref_latent は残さない＝台帳に居ないので落ちる）
        var json = File.ReadAllText(paths.VoicesJsonPath, Encoding.UTF8);
        Assert.Contains("\"" + NewId + "\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"" + OldId + "\"", json, StringComparison.Ordinal);
        Assert.Contains("refs/vv_mochiko_sexy.wav", json, StringComparison.Ordinal);
        Assert.DoesNotContain(OldStem, json, StringComparison.Ordinal);
    }

    // ---- ⒝ 利用者が消したプリセットは書き戻さない ---------------------------

    [Fact]
    public void 消されたプリセットは改名でも起動でも戻らない()
    {
        // 設計書 §4＝削除は利用者の意思。改名の引き継ぎは「戻す」仕掛けではない。
        // 裁定 121 で足した「増えた分だけ入れる」も同じ＝**差し出した記録**（presets_installed_ids）に
        // 居て台帳に居ない id は「利用者が消した」ので、起動では二度と戻らない。
        var paths = Paths;
        MakeManifest(paths);
        MakeReference(paths, "vr2_akane_west.wav");
        WriteTable(
            paths,
            DefaultEntry() + "," + PresetEntry(OtherId, "vr2_akane_west.wav", null, null),
            offeredIds: [OtherId, NewId]);

        var store = NewStore(paths);
        Assert.Equal(0, LauncherComposition.PrepareVoices(paths, store, new VoicesJsonWriter(), out var renamed));
        Assert.Empty(renamed);
        Assert.Equal(2, store.Load().Voices.Count);

        // 入れ直すと初めて戻る＝そのとき入るのは**新しい名だけ**
        Assert.Equal(1, PresetVoices.Restore(paths, store));
        var table = store.Load();
        Assert.Contains(NewId, table.Voices.Keys);
        Assert.DoesNotContain(OldId, table.Voices.Keys);
    }

    // ---- ⒞ 新しい名が既に居るなら改名しない -------------------------------

    [Fact]
    public void 新しい名が利用者の話者として既に居るなら改名しない()
    {
        var paths = Paths;
        MakeManifest(paths);
        MakeReference(paths, "vv_mochiko_sexy.wav");
        MakeReference(paths, "ywk-abcdef012345.wav");
        WriteTable(
            paths,
            DefaultEntry() + ","
            + PresetEntry(OldId, "vv_mochiko_sexy.wav", null, null) + ","
            + "\"" + NewId + "\":{\"display_name\":\"" + NewId + "\",\"file\":\"ywk-abcdef012345.wav\","
            + "\"preset\":false,\"origin\":\"user\"}");

        var store = NewStore(paths);
        Assert.Empty(PresetVoices.MigrateRenamed(paths, store));

        var table = store.Load();
        Assert.Contains(OldId, table.Voices.Keys); // 旧い名はそのまま（勝手に潰さない）
        Assert.Equal("ywk-abcdef012345.wav", table.Voices[NewId].File);
        Assert.Equal("user", table.Voices[NewId].Origin);
    }

    // ---- ⒟ 利用者の話者は同じ檔を指していても改名しない ---------------------

    [Fact]
    public void 利用者が足した話者は同じ檔でも改名しない()
    {
        var paths = Paths;
        MakeManifest(paths);
        MakeReference(paths, "vv_mochiko_sexy.wav");
        WriteTable(
            paths,
            DefaultEntry() + ","
            + "\"わたしの録音\":{\"display_name\":\"わたしの録音\",\"file\":\"vv_mochiko_sexy.wav\","
            + "\"preset\":false,\"origin\":\"user\"}");

        var store = NewStore(paths);
        Assert.Empty(PresetVoices.MigrateRenamed(paths, store));
        Assert.Contains("わたしの録音", store.Load().Voices.Keys);
    }

    // ---- ⒠ 正本が無い配布樹では 1 名も改名しない ---------------------------

    [Fact]
    public void 正本が無ければ檔名の幹へ改名してしまわない()
    {
        // Discover の ⑶（檔名の幹＝ASCII）で改名すると、日本語の id が
        // 「vv_mochiko_sexy」に化ける。改名は正本（⑴）から読めたときだけ。
        var paths = Paths;
        MakeManifest(paths, withJson: false);
        MakeReference(paths, "vv_mochiko_sexy.wav");
        WriteTable(paths, DefaultEntry() + "," + PresetEntry(OldId, "vv_mochiko_sexy.wav", null, null));

        var store = NewStore(paths);
        Assert.Empty(PresetVoices.MigrateRenamed(paths, store));

        var table = store.Load();
        Assert.Contains(OldId, table.Voices.Keys);
        Assert.DoesNotContain("vv_mochiko_sexy", table.Voices.Keys);
    }

    // ---- ⒡ 入れ直しは重複を作らない ---------------------------------------

    [Fact]
    public void 入れ直しは改名を通してから走るので重複しない()
    {
        var paths = Paths;
        MakeManifest(paths);
        MakeReference(paths, "vv_mochiko_sexy.wav");
        MakeReference(paths, "vr2_akane_west.wav");
        WriteTable(
            paths,
            DefaultEntry() + ","
            + PresetEntry(OldId, "vv_mochiko_sexy.wav", "落ち着いた声", null) + ","
            + PresetEntry(OtherId, "vr2_akane_west.wav", null, null));

        var store = NewStore(paths);
        var vm = new VoicesViewModel(
            store, new VoicesJsonWriter(), new SilentPlayer(), static () => null, paths, new LauncherSettings());

        vm.RestorePresets();

        var table = store.Load();
        Assert.Equal(3, table.Voices.Count); // デフォルト＋2 名（増えも減りもしない）
        Assert.Contains(NewId, table.Voices.Keys);
        Assert.DoesNotContain(OldId, table.Voices.Keys);
        Assert.Equal("落ち着いた声", table.Voices[NewId].Caption);
        Assert.Contains("名を改めた 1 名（" + OldId + "→" + NewId + "）", vm.Message, StringComparison.Ordinal);

        // 入れ直す物が無かった回に「既に全員居ます」を添えない（改名と噛み合わない＝是正）。
        Assert.DoesNotContain("既に全員居ます", vm.Message, StringComparison.Ordinal);
    }

    // ---- ⒡1 「入れ直す」で改名が起きたら設定も保存し直す ---------------------

    [Fact]
    public void 入れ直しで改名が起きたら設定の保存の手を呼ぶ()
    {
        // 呼ばないと直した warmupVoices が檔に落ちず、設定頁の「適用」（構築時の写しを
        // 書き戻す）で旧い id が甦る＝暖機が走行ごと failed のまま直らない。
        var paths = Paths;
        MakeManifest(paths);
        MakeReference(paths, "vv_mochiko_sexy.wav");
        WriteTable(paths, DefaultEntry() + "," + PresetEntry(OldId, "vv_mochiko_sexy.wav", null, null));

        var settings = new LauncherSettings
        {
            VoiceOrder = [OldId],
            WarmupVoices = [OldId],
            LastTestVoice = OldId,
        };
        var saves = 0;

        var store = NewStore(paths);
        var vm = new VoicesViewModel(
            store, new VoicesJsonWriter(), new SilentPlayer(), static () => null, paths, settings)
        {
            SettingsChanged = () => saves++,
        };

        vm.RestorePresets();

        Assert.Equal(1, saves);
        Assert.Equal(new[] { NewId }, settings.VoiceOrder);
        Assert.Equal(new[] { NewId }, settings.WarmupVoices);
        Assert.Equal(NewId, settings.LastTestVoice);

        // 改名が無い回は保存の手を呼ばない（無駄な書き込みを増やさない）。
        vm.RestorePresets();
        Assert.Equal(1, saves);
    }

    // ---- ⒡2 印の無い台帳でも重複しない（改名は初回展開の前） ----------------

    [Fact]
    public void 印の無い台帳では改名が先に走るので初回展開が重複を作らない()
    {
        // `presets_installed` が無い台帳＝presets.json を読めていなかった回の残骸。
        // 改名を初回展開の**後**に回すと、展開が新しい名を先に足し、PlanRenames は
        // 「新しい名が既に居る」で降りる＝同じ wav を指す 2 名が残る。
        var paths = Paths;
        MakeManifest(paths);
        MakeReference(paths, "vv_mochiko_sexy.wav");
        WriteTable(
            paths,
            DefaultEntry() + "," + PresetEntry(OldId, "vv_mochiko_sexy.wav", null, null),
            installed: false);

        var store = NewStore(paths);
        LauncherComposition.PrepareVoices(paths, store, new VoicesJsonWriter(), out var renamed);

        Assert.Single(renamed);
        var table = store.Load();
        Assert.Equal(3, table.Voices.Count); // デフォルト＋改まった 1 名＋展開で入った 1 名
        Assert.Contains(NewId, table.Voices.Keys);
        Assert.DoesNotContain(OldId, table.Voices.Keys);
        Assert.Contains(OtherId, table.Voices.Keys);
    }

    // ---- ⒡3 壊れた台帳（空白だけの鍵）でも起動は止まらない ------------------

    [Fact]
    public void 空白だけの鍵を持つ台帳でも改名は落ちない()
    {
        // 空白の id を DeleteLatentFiles に渡すと ArgumentException＝起動席は
        // IOException と UnauthorizedAccessException しか捕らないので、毎起動落ちる。
        var paths = Paths;
        MakeManifest(paths);
        MakeReference(paths, "vv_mochiko_sexy.wav");
        WriteTable(
            paths,
            DefaultEntry() + ","
            + "\"  \":{\"display_name\":\"  \",\"file\":\"vv_mochiko_sexy.wav\","
            + "\"preset\":true,\"origin\":\"preset\"}");

        var store = NewStore(paths);
        Assert.Empty(PresetVoices.MigrateRenamed(paths, store));
        Assert.Contains("  ", store.Load().Voices.Keys); // 触らない（消しも改めもしない）
    }

    // ---- ⒢ 別名表は台帳に居ない鍵を残さない -------------------------------

    [Fact]
    public void 別名表の書き直しは旧い鍵の潜在を残さない()
    {
        // 契約 ⑷ 4-3 の「wrapper が書いた ref_latent を残す」は**台帳に居る id だけ**。
        var file = new VoicesYwkFile
        {
            Voices = new Dictionary<string, VoiceEntry>(StringComparer.Ordinal)
            {
                [NewId] = new VoiceEntry
                {
                    DisplayName = NewId,
                    File = "vv_mochiko_sexy.wav",
                    Preset = true,
                    Origin = "preset",
                },
            },
        };

        var json = new VoicesJsonWriter().Render(
            file,
            "{\"" + VoiceIds.Default + "\":{\"no_ref\":true},"
            + "\"" + OldId + "\":{\"ref_latent\":\"latents/" + OldStem + ".pt\"}}");

        Assert.DoesNotContain("\"" + OldId + "\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain(OldStem, json, StringComparison.Ordinal);
        Assert.Contains("\"" + NewId + "\"", json, StringComparison.Ordinal);
        Assert.Contains("refs/vv_mochiko_sexy.wav", json, StringComparison.Ordinal);
    }

    // ---- ⒣ 設定に残った旧い id も同じ回で改める ----------------------------

    [Fact]
    public void 設定の暖機と並びと試し撃ちの旧い名も改まる()
    {
        // 暖機は知らない話者で走行ごと failed になる（server/ywk_server.py:2127-2151＋
        // _warmup_run_shots）。並びと試し撃ちは捨てられるだけだが、揃えて直す。
        var settings = new LauncherSettings
        {
            VoiceOrder = [OldId, OtherId],
            WarmupVoices = [OldId],
            LastTestVoice = OldId,
        };

        var changed = PresetVoices.RenameInSettings(
            settings, [new PresetRename(OldId, NewId, "vv_mochiko_sexy.wav")]);

        Assert.True(changed);
        Assert.Equal(new[] { NewId, OtherId }, settings.VoiceOrder);
        Assert.Equal(new[] { NewId }, settings.WarmupVoices);
        Assert.Equal(NewId, settings.LastTestVoice);

        // 改名が無ければ 1 字も触らない
        Assert.False(PresetVoices.RenameInSettings(settings, []));
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
}
