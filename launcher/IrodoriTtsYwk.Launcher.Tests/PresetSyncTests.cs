using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services;
using IrodoriTtsYwk.Launcher.Services.Voices;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 段 E-1＝<b>同梱の声の中身の差分</b>（<c>v2-spec.md</c> §11-2 の 5 枝・<c>decisions.md</c> 115 の穴）。
/// <para>
/// <b>利用者の声を消す事故は取り返しがつかない</b>ので、⑴ <c>origin=preset</c> の行以外に触らない
/// ⑵ <c>file</c> が <c>refs\</c> の外を指す行は飛ばす、をここで釘付けする。
/// </para>
/// </summary>
public sealed class PresetSyncTests : IDisposable
{
    private const string OldMd5 = "0d2e0175c9db9f68dbd57a1ae2939440";
    private const string NewMd5 = "11112222333344445555666677778888";
    private const string MineMd5 = "99998888777766665555444433332222";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ywk-sync-" + Guid.NewGuid().ToString("N")[..8]);

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
    }

    private static PresetVoice Preset(string id = "もち子さん", string? md5 = NewMd5) =>
        new(id, "vv_mochiko_sexy.wav", @"C:\app\voices\presets\vv_mochiko_sexy.wav", null, md5, 2953004);

    private static VoicesYwkFile Table(VoiceEntry entry, string id = "もち子さん") => new()
    {
        Schema = 1,
        PresetsInstalled = true,
        Voices = new Dictionary<string, VoiceEntry>(StringComparer.Ordinal) { [id] = entry },
    };

    private static VoiceEntry PresetEntry(string? presetMd5) => new()
    {
        DisplayName = "もち子さん",
        File = "vv_mochiko_sexy.wav",
        Preset = true,
        Origin = "preset",
        PresetMd5 = presetMd5,
    };

    private static PresetSyncPlan PlanWith(VoiceEntry entry, string? measured, string? sourceMd5 = NewMd5) =>
        PresetSync.Plan([Preset(md5: sourceMd5)], Table(entry), _ => measured);

    // ---- 5 枝 -------------------------------------------------------------

    [Fact]
    public void 枝a_wavが無ければ写す()
    {
        Assert.Single(PlanWith(PresetEntry(OldMd5), measured: null).Copies);
    }

    [Fact]
    public void 枝b_置いたまま触っていない檔は上書きする()
    {
        var plan = PlanWith(PresetEntry(OldMd5), measured: OldMd5);

        var item = Assert.Single(plan.Overwrites);
        Assert.Equal(NewMd5, item.SourceMd5); // 台帳へ記録するのは**配布側**の値
        Assert.Empty(plan.Kept);
    }

    [Fact]
    public void 枝c_利用者が差し替えた檔は触らない()
    {
        var plan = PlanWith(PresetEntry(OldMd5), measured: MineMd5);

        Assert.Single(plan.Kept);
        Assert.Empty(plan.Overwrites);
        Assert.Empty(plan.Copies);
        Assert.False(plan.HasWork);
    }

    [Fact]
    public void 枝d_もう新しければ何もしない()
    {
        var plan = PlanWith(PresetEntry(NewMd5), measured: NewMd5);

        Assert.False(plan.HasWork);
        Assert.Empty(plan.Kept);
    }

    [Fact]
    public void 枝e_欄の無い台帳は触らず一致した回だけ書き足す()
    {
        // 中身が違う＝判らないので触らない
        Assert.False(PlanWith(PresetEntry(null), measured: MineMd5).HasWork);
        Assert.Empty(PlanWith(PresetEntry(null), measured: MineMd5).Kept);

        // 中身が配布側と同じ＝欄を書き足すだけ（次の版から b が効く）
        var plan = PlanWith(PresetEntry(null), measured: NewMd5);
        Assert.Single(plan.Records);
        Assert.Empty(plan.Overwrites);
    }

    // ---- 境目 -------------------------------------------------------------

    [Fact]
    public void 記録が配布側と同じなら実測を撃たない()
    {
        var measured = 0;
        var plan = PresetSync.Plan(
            [Preset()],
            Table(PresetEntry(NewMd5)),
            _ =>
            {
                measured++;
                return NewMd5;
            });

        Assert.Equal(0, measured); // 2 度目からは文字列比較だけ＝35 MB を読まない
        Assert.False(plan.HasWork);
    }

    [Fact]
    public void 配布側がmd5を名乗らなければ何もしない()
    {
        Assert.False(PlanWith(PresetEntry(OldMd5), measured: MineMd5, sourceMd5: null).HasWork);
        Assert.False(PlanWith(PresetEntry(OldMd5), measured: MineMd5, sourceMd5: "  ").HasWork);
    }

    [Fact]
    public void 利用者が足した声には触らない()
    {
        var user = new VoiceEntry
        {
            DisplayName = "もち子さん",
            File = "vv_mochiko_sexy.wav",
            Preset = false,
            Origin = "user",
            PresetMd5 = OldMd5,
        };

        var plan = PresetSync.Plan([Preset()], Table(user), _ => OldMd5);

        Assert.False(plan.HasWork);
        Assert.Empty(plan.Kept);
    }

    [Fact]
    public void 消したプリセットは戻さない()
    {
        var empty = new VoicesYwkFile { PresetsInstalled = true };

        Assert.False(PresetSync.Plan([Preset()], empty, _ => null).HasWork);
    }

    [Fact]
    public void refsの外を指す行は飛ばす()
    {
        foreach (var file in new[]
                 {
                     @"..\..\Documents\mine.wav", "sub/other.wav", @"sub\other.wav",
                     @"C:\Users\u\Documents\mine.wav", "", null,
                 })
        {
            var entry = PresetEntry(OldMd5) with { File = file };

            Assert.False(PresetSync.Plan([Preset()], Table(entry), _ => OldMd5).HasWork);
            Assert.False(PresetSync.IsInsideReferences(file));
        }

        Assert.True(PresetSync.IsInsideReferences("vv_mochiko_sexy.wav"));
    }

    [Fact]
    public void 上書きの行き先は台帳の檔名である()
    {
        // 配布側が secondary.file だけを改名した版（display_name は同じ）＝
        // 実測は台帳の refs\<entry.File> を測るので、**行き先も同じ檔でなければならない**。
        // 正本の檔名を行き先にすると、別の話者の参照 wav を潰したうえ、当の行は古い wav を
        // 指したまま preset_md5 だけ新しくなり、枝 d で二度と見直されない。
        var renamedSource = new PresetVoice(
            "もち子さん", "vv_mochiko_sexy_v2.wav",
            @"C:\app\voices\presets\vv_mochiko_sexy_v2.wav", null, NewMd5, 2953004);

        var item = Assert.Single(
            PresetSync.Plan([renamedSource], Table(PresetEntry(OldMd5)), _ => OldMd5).Overwrites);

        Assert.Equal("vv_mochiko_sexy.wav", item.FileName);            // ＝台帳の file
        Assert.EndsWith("vv_mochiko_sexy_v2.wav", item.SourcePath, StringComparison.Ordinal);
    }

    [Fact]
    public void 正本の檔名が路を名乗る行は飛ばす()
    {
        // 手で書いた／古い形の presets.json の secondary.file が refs\ の外を指す回。
        foreach (var file in new[] { @"..\..\x.wav", "sub/x.wav", @"C:\x.wav" })
        {
            var manifest = new[]
            {
                new PresetVoice("もち子さん", file, @"C:\app\voices\presets\x.wav", null, NewMd5, 4),
            };

            Assert.False(PresetSync.Plan(manifest, Table(PresetEntry(OldMd5)), _ => OldMd5).HasWork);
        }
    }

    [Fact]
    public void 在るのに読めなかった檔は枝cへ落ちる()
    {
        // Md5OfFile は「無い」も「掴まれている」も null を返す＝呼び手が見分けて
        // Different を名乗る約束（PresetVoices.SyncContents の測り手）。
        var plan = PlanWith(PresetEntry(OldMd5), measured: PresetSync.Different);

        Assert.Single(plan.Kept);
        Assert.Empty(plan.Copies);      // ← 上書きの写しには**絶対に**落とさない
        Assert.Empty(plan.Overwrites);
        Assert.False(plan.HasWork);
    }

    [Fact]
    public void 改名の直後でも新しい名の行に当たる()
    {
        // MigrateRenamed が先に走った後の姿＝台帳の鍵が新しい名になっている。
        var renamed = Table(PresetEntry(OldMd5), "もち子さん（セクシー／あん子）");
        var manifest = new[] { Preset("もち子さん（セクシー／あん子）") };

        Assert.Single(PresetSync.Plan(manifest, renamed, _ => OldMd5).Overwrites);
    }

    [Fact]
    public void 告げる1行は逐語である()
    {
        Assert.Equal("あなたが差し替えた声はそのままにしました（2 名）。", PresetSync.KeptLine(2));
        Assert.Equal("新しい版で録り直された声があります（3 名）。", PresetSync.UpdatedReason(3));
        Assert.Null(PresetSync.KeptLine(0));
        Assert.Null(PresetSync.UpdatedReason(0));
        Assert.Null(PresetSync.UpdatedLine(0));
    }

    [Fact]
    public void md5は実檔から計れて読めなければnull()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "a.bin");
        File.WriteAllBytes(path, [1, 2, 3]);

        Assert.Equal("5289df737df57326fcdd22597afb1fac", PresetSync.Md5OfFile(path));
        Assert.Null(PresetSync.Md5OfFile(Path.Combine(_root, "missing.bin")));
        Assert.Null(PresetSync.Md5OfFile(null));

        Assert.True(PresetSync.LengthCouldMatch(path, 3));
        Assert.False(PresetSync.LengthCouldMatch(path, 4));
        Assert.True(PresetSync.LengthCouldMatch(path, null)); // 長さを名乗らない台帳は足切りしない
    }

    // ---- 檔まで動かす道（SyncContents）-------------------------------------

    private (AppPaths Paths, VoiceStore Store) Build(string presetMd5InTable, byte[] userWav)
    {
        var appDir = Path.Combine(_root, "app");
        var dataDir = Path.Combine(_root, "data");
        var presets = Path.Combine(appDir, "voices", "presets");
        Directory.CreateDirectory(presets);

        // 配布樹の新しい wav（中身＝{9,9,9,9}）と、その md5 を名乗る presets.json。
        var fresh = new byte[] { 9, 9, 9, 9 };
        File.WriteAllBytes(Path.Combine(presets, "vv_mochiko_sexy.wav"), fresh);
        var freshMd5 = Md5(fresh);
        File.WriteAllText(
            Path.Combine(appDir, "voices", "presets.json"),
            """
            {"presets":[{"id":"vv_mochiko_sexy","display_name":"もち子さん","status":"done",
              "secondary":{"file":"vv_mochiko_sexy.wav","md5":"__MD5__","size_bytes":4}}]}
            """.Replace("__MD5__", freshMd5, StringComparison.Ordinal),
            new UTF8Encoding(false));

        var paths = AppPaths.Resolve(
            appDir, null,
            name => name == AppPaths.AppDirEnvName ? appDir
                : name == AppPaths.DataDirEnvName ? dataDir
                : null);

        var store = new VoiceStore(
            Path.Combine(dataDir, "voices", "voices.ywk.json"), Path.Combine(dataDir, "voices"));
        Directory.CreateDirectory(store.ReferencesDir);
        File.WriteAllBytes(Path.Combine(store.ReferencesDir, "vv_mochiko_sexy.wav"), userWav);
        store.Save(Table(PresetEntry(presetMd5InTable)));

        return (paths, store);
    }

    private static string Md5(byte[] bytes)
    {
#pragma warning disable CA5351 // 試験の材料（完全性の照合）＝暗号用途ではない
        return Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(bytes));
#pragma warning restore CA5351
    }

    [Fact]
    public void 触っていない檔は写し直して潜在を捨てる()
    {
        var old = new byte[] { 1, 1, 1, 1 };
        var (paths, store) = Build(Md5(old), old);

        // 焼いた潜在を置いておく＝上書きの回に捨てられなければならない。
        var latents = Path.Combine(store.VoicesDir, "latents");
        Directory.CreateDirectory(latents);
        var stem = VoiceStore.LatentStem("もち子さん");
        File.WriteAllBytes(Path.Combine(latents, stem + ".pt"), new byte[8]);

        var result = PresetVoices.SyncContents(paths, store);

        Assert.Equal(["もち子さん"], result.Updated);
        Assert.Empty(result.Kept);
        Assert.Equal(
            new byte[] { 9, 9, 9, 9 },
            File.ReadAllBytes(Path.Combine(store.ReferencesDir, "vv_mochiko_sexy.wav")));
        Assert.False(File.Exists(Path.Combine(latents, stem + ".pt")));

        var entry = store.Load().Voices["もち子さん"];
        Assert.Equal(Md5([9, 9, 9, 9]), entry.PresetMd5);
        Assert.Null(entry.RefLatent);

        // 2 度目は 1 バイトも動かない。
        Assert.Empty(PresetVoices.SyncContents(paths, store).Updated);
    }

    [Fact]
    public void 差し替えた檔はそのまま残る()
    {
        var mine = new byte[] { 7, 7, 7, 7 };
        var (paths, store) = Build(Md5([1, 1, 1, 1]), mine);

        var result = PresetVoices.SyncContents(paths, store);

        Assert.Empty(result.Updated);
        Assert.Equal(["もち子さん"], result.Kept);
        Assert.Equal(mine, File.ReadAllBytes(Path.Combine(store.ReferencesDir, "vv_mochiko_sexy.wav")));
    }

    [Fact]
    public void 掴まれている檔は上書きしない()
    {
        // 再生器・編集器が開いたままの wav（＝md5 が計れない）。「無い」と読んで写すと、
        // 利用者が差し替えた録音が配布側の音に戻る＝枝 c が守っている当の事故。
        var mine = new byte[] { 7, 7, 7, 7 };
        var (paths, store) = Build(Md5([1, 1, 1, 1]), mine);
        var wav = Path.Combine(store.ReferencesDir, "vv_mochiko_sexy.wav");

        PresetSyncResult result;
        using (var _ = new FileStream(wav, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = PresetVoices.SyncContents(paths, store);
        }

        Assert.Empty(result.Updated);
        Assert.Equal(["もち子さん"], result.Kept);
        Assert.Equal(mine, File.ReadAllBytes(wav));
        Assert.Equal(Md5([1, 1, 1, 1]), store.Load().Voices["もち子さん"].PresetMd5); // 記録も動かない
    }

    [Fact]
    public void 版が同じ回は走らない()
    {
        var old = new byte[] { 1, 1, 1, 1 };
        var (paths, store) = Build(Md5(old), old);

        LauncherComposition.PrepareVoices(
            paths, store, new VoicesJsonWriter(),
            out _, out _, out var synced, syncPresetContents: false);

        Assert.Empty(synced.Updated);
        Assert.Equal(old, File.ReadAllBytes(Path.Combine(store.ReferencesDir, "vv_mochiko_sexy.wav")));
    }

    [Fact]
    public void 呼び順に1段増えても足した数と改名の数は変わらない()
    {
        var old = new byte[] { 1, 1, 1, 1 };
        var (paths, store) = Build(Md5(old), old);

        var copied = LauncherComposition.PrepareVoices(
            paths, store, new VoicesJsonWriter(), out var renamed, out var added, out var synced);

        Assert.Equal(0, copied);      // 台帳は在る＝初回展開は走らない
        Assert.Empty(renamed);        // 正本の id と台帳の id が同じ＝改名なし
        Assert.Empty(added);          // 増えたプリセットは無い
        Assert.Equal(["もち子さん"], synced.Updated); // 動くのは中身の更新だけ
    }
}
