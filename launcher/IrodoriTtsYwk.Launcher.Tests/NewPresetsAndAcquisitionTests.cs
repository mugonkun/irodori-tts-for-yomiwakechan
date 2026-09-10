using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services;
using IrodoriTtsYwk.Launcher.Services.Models;
using IrodoriTtsYwk.Launcher.Services.Server;
using IrodoriTtsYwk.Launcher.Services.Voices;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 裁定 121 の雛形（配布樹・利用者データ樹を一時ディレクトリに作る）。
/// <para>
/// 司令官の報告（2026-09-10・逐語）＝「<b>話者一覧にシャンパンコールがないね。</b>」
/// 「<b>初回起動から、モデルダウンロードへの導線を追加してほしい。単に、サーバー起動失敗となるから。</b>」
/// </para>
/// </summary>
internal static class Ruling121Tree
{
    /// <summary>v1.0.0 から居る 1 名（改名の相手でもある＝裁定 108）。</summary>
    public const string Mochiko = "もち子さん（セクシー／あん子）";

    /// <summary>同上（対照＝触られてはいけない行）。</summary>
    public const string Akane = "琴葉茜（関西弁）";

    /// <summary>v1.0.1 で<b>増えた</b>1 名（裁定 118）＝この便で入らなかった話者。</summary>
    public const string Champagne = "シャンパンコール（ホスクラ）";

    public static AppPaths MakePaths(string root) => new(
        Path.Combine(root, "install"),
        Path.Combine(root, "app"),
        Path.Combine(root, "runtime"),
        Path.Combine(root, "data"),
        developerMode: true);

    /// <summary>配布樹の正本＝<paramref name="withChampagne"/> が偽なら v1.0.0 の 2 名。</summary>
    public static void MakeManifest(AppPaths paths, bool withChampagne)
    {
        Directory.CreateDirectory(paths.PresetVoicesDir);
        File.WriteAllBytes(Path.Combine(paths.PresetVoicesDir, "vv_mochiko_sexy.wav"), new byte[256]);
        File.WriteAllBytes(Path.Combine(paths.PresetVoicesDir, "vr2_akane_west.wav"), new byte[512]);
        File.WriteAllBytes(Path.Combine(paths.PresetVoicesDir, "ext_hostclub_champagne.wav"), new byte[128]);

        var champagne = withChampagne
            ? """
              ,{"id":"ext_hostclub_champagne","display_name":"シャンパンコール（ホスクラ）","status":"done",
                "secondary":{"file":"ext_hostclub_champagne.wav"}}
              """
            : string.Empty;

        File.WriteAllText(
            paths.PresetsJsonPath,
            """
            {"version":1,"presets":[
              {"id":"vv_mochiko_sexy","display_name":"もち子さん（セクシー／あん子）","status":"done",
               "caption":"落ち着いた声","secondary":{"file":"vv_mochiko_sexy.wav"}},
              {"id":"vr2_akane_west","display_name":"琴葉茜（関西弁）","status":"done",
               "secondary":{"file":"vr2_akane_west.wav"}}
            """
            + champagne
            + """
              ,{"id":"cevio_maki_en","display_name":"弦巻マキ（英語）","status":"skipped","secondary":null}]}
              """,
            new UTF8Encoding(false));
    }

    /// <summary>利用者データ側の台帳（<paramref name="offeredIds"/>＝null で欄そのものが無い姿）。</summary>
    public static void WriteTable(
        AppPaths paths, IReadOnlyList<string> voiceIds, string[]? offeredIds, bool installed = true)
    {
        Directory.CreateDirectory(paths.VoicesDir);
        Directory.CreateDirectory(paths.ReferenceWavDir);

        var rows = new List<string>
        {
            "\"" + VoiceIds.Default + "\":{\"display_name\":\"" + VoiceIds.Default
            + "\",\"no_ref\":true,\"preset\":true,\"origin\":\"preset\"}",
        };

        foreach (var id in voiceIds)
        {
            var file = FileOf(id);
            File.WriteAllBytes(Path.Combine(paths.ReferenceWavDir, file), new byte[16]);
            rows.Add("\"" + id + "\":{\"display_name\":\"" + id + "\",\"file\":\"" + file + "\","
                     + "\"caption\":\"元の caption\",\"preset\":true,\"origin\":\"preset\"}");
        }

        var ids = offeredIds is null
            ? string.Empty
            : ",\"presets_installed_ids\":[\"" + string.Join("\",\"", offeredIds) + "\"]";

        File.WriteAllText(
            paths.VoicesYwkJsonPath,
            "{\"schema\":1" + (installed ? ",\"presets_installed\":true" : string.Empty) + ids
            + ",\"voices\":{" + string.Join(",", rows) + "}}",
            new UTF8Encoding(false));
    }

    public static string FileOf(string id) => id switch
    {
        Mochiko => "vv_mochiko_sexy.wav",
        Akane => "vr2_akane_west.wav",
        Champagne => "ext_hostclub_champagne.wav",
        _ => "vv_mochiko_sexy.wav",
    };

    public static VoiceStore NewStore(AppPaths paths) =>
        new(paths.VoicesYwkJsonPath, paths.VoicesDir, paths.ReferenceWavDir);

    // ---- モデル台帳（AcquisitionCheck の雛形） -------------------------------

    public const string Repo = "Aratako/Irodori-TTS-v4.1-Small";

    public const string Revision = "2b28324dc263ed5e6638b3cf3dd94c82ead07b4b";

    /// <summary><c>ledger/models.json</c>（1 リポ 2 檔＝実物と同形の最小）。</summary>
    public static void MakeModelsLedger(AppPaths paths)
    {
        Directory.CreateDirectory(paths.LedgerDir);
        File.WriteAllText(
            paths.LedgerPath("models"),
            """
            {"schema":1,"name":"models","repos":[
              {"kind":"hf-repo","role":"checkpoint","repo":"
            """.TrimEnd() + Repo + "\",\"revision\":\"" + Revision + "\","
            + "\"hf_cache_dir\":\"models--Aratako--Irodori-TTS-v4.1-Small\",\"files\":["
            + "{\"path\":\"model.safetensors\",\"size\":16},"
            + "{\"path\":\"tokenizer/tokenizer.json\",\"size\":8}]}]}",
            new UTF8Encoding(false));
    }

    /// <summary>台帳が名乗る長さ（<c>MakeModelsLedger</c> と 1 字も違えない）。</summary>
    public static readonly (string Path, int Size)[] ModelFiles =
    [
        ("model.safetensors", 16),
        ("tokenizer/tokenizer.json", 8),
    ];

    /// <summary>
    /// 取得が済んだ姿（snapshot の 2 檔＋<c>refs/main</c>＝裁定 49）。
    /// <b>長さも台帳の通りに書く</b>（是正・検分＝在否だけでなく長さも見る）。
    /// </summary>
    public static void MakeModelCache(AppPaths paths, bool withRefsMain = true)
    {
        var hub = Path.Combine(paths.HfHomeDir, AcquisitionCheck.HubSubdirectory);
        foreach (var (relative, size) in ModelFiles)
        {
            WriteModelFile(paths, relative, size);
        }

        var refs = AcquisitionCheck.RefsMainPath(hub, Repo);
        Directory.CreateDirectory(Path.GetDirectoryName(refs)!);
        File.WriteAllText(refs, withRefsMain ? Revision : "0000000000000000000000000000000000000000");
    }

    /// <summary>snapshot の檔を 1 本、名指した長さで書く（欠けや切れを作る口）。</summary>
    public static void WriteModelFile(AppPaths paths, string relative, int size)
    {
        var hub = Path.Combine(paths.HfHomeDir, AcquisitionCheck.HubSubdirectory);
        var path = AcquisitionCheck.SnapshotPath(hub, Repo, Revision, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }

    /// <summary>展開が済んだ実行系（<c>python.exe</c> 1 檔で足りる＝在否しか見ない）。</summary>
    public static void MakeRuntime(AppPaths paths, string variant)
    {
        var dir = Path.Combine(paths.RuntimeRoot, variant);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, AppPaths.PythonExeName), []);
    }
}

/// <summary>
/// 裁定 121 ⒜＝<b>後の版で増えた同梱の話者が起動で入る</b>（利用者が消した 1 名は消えたまま）。
/// </summary>
public sealed class NewPresetInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-121-preset-" + Guid.NewGuid().ToString("N")[..8]);

    private AppPaths Paths => Ruling121Tree.MakePaths(_root);

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

    [Fact]
    public void 新しい台帳は全員入り差し出した_id_を記帳する()
    {
        var paths = Paths;
        Ruling121Tree.MakeManifest(paths, withChampagne: true);

        var store = Ruling121Tree.NewStore(paths);
        Assert.Equal(3, LauncherComposition.PrepareVoices(
            paths, store, new VoicesJsonWriter(), out _, out var added));

        Assert.Empty(added); // 初回展開が全員入れた回に、足す物は残らない

        var table = store.Load();
        Assert.True(table.PresetsInstalled);
        Assert.Equal(4, table.Voices.Count); // デフォルト＋3 名
        Assert.NotNull(table.PresetsInstalledIds);
        Assert.Equal(3, table.PresetsInstalledIds!.Count);
        Assert.Contains(Ruling121Tree.Champagne, table.PresetsInstalledIds);
    }

    [Fact]
    public void 印つきで_id_欄の無い台帳には増えた_1_名だけが入る()
    {
        // 実射＝v1.0.0 から使っている台帳（presets_installed だけ）に、v1.0.1 で足した
        // 「シャンパンコール（ホスクラ）」が入らなかった（司令官の報告）。
        var paths = Paths;
        Ruling121Tree.MakeManifest(paths, withChampagne: true);
        Ruling121Tree.WriteTable(paths, [Ruling121Tree.Mochiko, Ruling121Tree.Akane], offeredIds: null);

        var store = Ruling121Tree.NewStore(paths);
        Assert.Equal(0, LauncherComposition.PrepareVoices(
            paths, store, new VoicesJsonWriter(), out var renamed, out var added));

        Assert.Empty(renamed);
        Assert.Equal(new[] { Ruling121Tree.Champagne }, added);

        var table = store.Load();
        Assert.Equal(4, table.Voices.Count);
        var entry = Assert.Contains(Ruling121Tree.Champagne, table.Voices);
        Assert.Equal("ext_hostclub_champagne.wav", entry.File);
        Assert.True(entry.Preset);
        Assert.Equal("preset", entry.Origin);
        Assert.True(File.Exists(
            Path.Combine(paths.ReferenceWavDir, "ext_hostclub_champagne.wav")));

        // 元から居た行は 1 字も触らない（caption を書き換えない）
        Assert.Equal("元の caption", table.Voices[Ruling121Tree.Mochiko].Caption);
        Assert.Equal("元の caption", table.Voices[Ruling121Tree.Akane].Caption);

        // 種（台帳に居た 2 名）＋足した 1 名が記帳される
        Assert.Equal(3, table.PresetsInstalledIds!.Count);
    }

    [Fact]
    public void 差し出した記録に在って台帳に居ない_id_は戻らない()
    {
        // 利用者が消した 1 名＝起動では二度と戻さない（戻す口は「入れ直す」だけ）。
        var paths = Paths;
        Ruling121Tree.MakeManifest(paths, withChampagne: true);
        Ruling121Tree.WriteTable(
            paths,
            [Ruling121Tree.Mochiko, Ruling121Tree.Akane],
            offeredIds: [Ruling121Tree.Mochiko, Ruling121Tree.Akane, Ruling121Tree.Champagne]);

        var store = Ruling121Tree.NewStore(paths);
        LauncherComposition.PrepareVoices(paths, store, new VoicesJsonWriter(), out _, out var added);

        Assert.Empty(added);
        Assert.DoesNotContain(Ruling121Tree.Champagne, store.Load().Voices.Keys);

        // 「入れ直す」なら戻る（設計書 §4）
        Assert.Equal(1, PresetVoices.Restore(paths, store));
        Assert.Contains(Ruling121Tree.Champagne, store.Load().Voices.Keys);
    }

    [Fact]
    public void 二度目の起動は_0_名で台帳を書き直さない()
    {
        var paths = Paths;
        Ruling121Tree.MakeManifest(paths, withChampagne: true);
        Ruling121Tree.WriteTable(paths, [Ruling121Tree.Mochiko, Ruling121Tree.Akane], offeredIds: null);

        var store = Ruling121Tree.NewStore(paths);
        LauncherComposition.PrepareVoices(paths, store, new VoicesJsonWriter(), out _, out var first);
        Assert.Single(first);

        var before = File.ReadAllBytes(paths.VoicesYwkJsonPath);
        var stamp = File.GetLastWriteTimeUtc(paths.VoicesYwkJsonPath);

        Assert.Empty(PresetVoices.InstallNew(paths, store));

        Assert.Equal(before, File.ReadAllBytes(paths.VoicesYwkJsonPath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(paths.VoicesYwkJsonPath));
    }

    [Fact]
    public void 改名と追加が同じ起動で通る()
    {
        // 改名（裁定 108）が先＝旧い名を「まだ差し出していない」と読んで 2 名にしない。
        var paths = Paths;
        Ruling121Tree.MakeManifest(paths, withChampagne: true);
        Directory.CreateDirectory(paths.VoicesDir);
        Directory.CreateDirectory(paths.ReferenceWavDir);
        File.WriteAllBytes(Path.Combine(paths.ReferenceWavDir, "vv_mochiko_sexy.wav"), new byte[16]);
        File.WriteAllText(
            paths.VoicesYwkJsonPath,
            "{\"schema\":1,\"presets_installed\":true,\"voices\":{"
            + "\"" + VoiceIds.Default + "\":{\"display_name\":\"" + VoiceIds.Default
            + "\",\"no_ref\":true,\"preset\":true,\"origin\":\"preset\"},"
            + "\"もち子さん\":{\"display_name\":\"もち子さん\",\"file\":\"vv_mochiko_sexy.wav\","
            + "\"preset\":true,\"origin\":\"preset\"}}}",
            new UTF8Encoding(false));

        var store = Ruling121Tree.NewStore(paths);
        LauncherComposition.PrepareVoices(paths, store, new VoicesJsonWriter(), out var renamed, out var added);

        var rename = Assert.Single(renamed);
        Assert.Equal("もち子さん", rename.OldId);
        Assert.Equal(Ruling121Tree.Mochiko, rename.NewId);

        // 改名した 1 名は「差し出し済み」と読まれる＝増えたのは残り 2 名だけ
        Assert.Equal(new[] { Ruling121Tree.Akane, Ruling121Tree.Champagne }, added);

        var table = store.Load();
        Assert.Equal(4, table.Voices.Count); // デフォルト＋改まった 1 名＋足した 2 名
        Assert.DoesNotContain("もち子さん", table.Voices.Keys);
    }

    [Fact]
    public void 改名した名も記録に移すので後で消しても戻らない()
    {
        // 是正・検分＝改名（裁定 108）が台帳の行だけを改めて記録（presets_installed_ids）に
        // 旧い名を残していたころは、新しい名が「まだ差し出していない id」に見えた＝
        // 利用者がその 1 名を消した次の起動で戻ってしまった（不変が破れる）。
        var paths = Paths;
        Ruling121Tree.MakeManifest(paths, withChampagne: true);
        Ruling121Tree.WriteTable(
            paths,
            ["もち子さん", Ruling121Tree.Akane, Ruling121Tree.Champagne],
            offeredIds: ["もち子さん", Ruling121Tree.Akane, Ruling121Tree.Champagne]);

        var store = Ruling121Tree.NewStore(paths);
        LauncherComposition.PrepareVoices(paths, store, new VoicesJsonWriter(), out var renamed, out var added);

        Assert.Single(renamed);
        Assert.Empty(added);

        var afterRename = store.Load();
        Assert.Contains(Ruling121Tree.Mochiko, afterRename.PresetsInstalledIds!);
        Assert.DoesNotContain("もち子さん", afterRename.PresetsInstalledIds!);

        // 利用者が改まった名の 1 名を消す → 起動で戻らない
        store.RemoveVoiceDetailed(Ruling121Tree.Mochiko);
        LauncherComposition.PrepareVoices(paths, store, new VoicesJsonWriter(), out _, out var second);

        Assert.Empty(second);
        Assert.DoesNotContain(Ruling121Tree.Mochiko, store.Load().Voices.Keys);
    }

    [Fact]
    public void 台帳に居るのに記録に無い_id_はその起動で書き足す()
    {
        // 是正・検分＝「足した話者が 0 名」でも、記録が増えた回は台帳を書く。
        // 書かないと次の起動が同じ id を「まだ差し出していない」と読む。
        var paths = Paths;
        Ruling121Tree.MakeManifest(paths, withChampagne: true);
        Ruling121Tree.WriteTable(
            paths,
            [Ruling121Tree.Mochiko, Ruling121Tree.Akane, Ruling121Tree.Champagne],
            offeredIds: [Ruling121Tree.Mochiko, Ruling121Tree.Akane]);

        var store = Ruling121Tree.NewStore(paths);
        Assert.Empty(PresetVoices.InstallNew(paths, store));

        Assert.Contains(Ruling121Tree.Champagne, store.Load().PresetsInstalledIds!);
    }

    [Fact]
    public void 足した話者は_1_行に畳んで告げる()
    {
        Assert.Null(PresetVoices.AddedLine([]));
        Assert.Equal(
            "同梱の話者を 2 名足しました：" + Ruling121Tree.Akane + "・" + Ruling121Tree.Champagne,
            PresetVoices.AddedLine([Ruling121Tree.Akane, Ruling121Tree.Champagne]));
    }
}

/// <summary>裁定 121 ⒝-1＝取得の済み具合を檔の在否だけで見る。</summary>
public sealed class AcquisitionCheckTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-121-acq-" + Guid.NewGuid().ToString("N")[..8]);

    private AppPaths Paths => Ruling121Tree.MakePaths(_root);

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

    [Fact]
    public void 実行系もモデルも揃っていれば何も言わない()
    {
        var paths = Paths;
        Ruling121Tree.MakeModelsLedger(paths);
        Ruling121Tree.MakeModelCache(paths);
        Ruling121Tree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var state = AcquisitionCheck.Check(paths, RuntimeVariants.Cpu);

        Assert.True(state.RuntimeReady);
        Assert.True(state.ModelsReady);
        Assert.True(state.Ready);
        Assert.Empty(state.MissingModelFiles);
        Assert.Equal(string.Empty, state.Summary);
    }

    [Fact]
    public void 実行系が無ければ不足に名が出る()
    {
        var paths = Paths;
        Ruling121Tree.MakeModelsLedger(paths);
        Ruling121Tree.MakeModelCache(paths);

        var state = AcquisitionCheck.Check(paths, RuntimeVariants.Cpu);

        Assert.False(state.RuntimeReady);
        Assert.True(state.ModelsReady);
        Assert.False(state.Ready);
        Assert.Contains("実行系", state.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void モデルが無ければ檔の名まで並べる()
    {
        var paths = Paths;
        Ruling121Tree.MakeModelsLedger(paths);
        Ruling121Tree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var state = AcquisitionCheck.Check(paths, RuntimeVariants.Cpu);

        Assert.True(state.RuntimeReady);
        Assert.False(state.ModelsReady);
        Assert.Equal(3, state.MissingModelFiles.Count); // 2 檔＋refs/main
        Assert.Contains(Ruling121Tree.Repo + ":model.safetensors", state.MissingModelFiles);
        Assert.Contains(Ruling121Tree.Repo + ":refs/main", state.MissingModelFiles);
        Assert.Contains("model.safetensors", state.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void refs_main_が_pin_を指していなければ不足に数える()
    {
        // 裁定 49＝これが無いと上流の読み込みが HF_HUB_OFFLINE=1 で落ちる。
        var paths = Paths;
        Ruling121Tree.MakeModelsLedger(paths);
        Ruling121Tree.MakeModelCache(paths, withRefsMain: false);
        Ruling121Tree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var state = AcquisitionCheck.Check(paths, RuntimeVariants.Cpu);

        Assert.False(state.ModelsReady);
        Assert.Equal([Ruling121Tree.Repo + ":refs/main"], state.MissingModelFiles);
    }

    [Fact]
    public void 長さが台帳と違う檔は在っても不足に数える()
    {
        // 是正・検分＝在否だけを見ていたころは、途中で切れた檔・写し損ねたキャッシュが
        // 「揃っている」と読まれ、事前検査を通った個体が上流の読み込みで落ちた＝
        // この裁定が消そうとしている行き止まりへ戻る。
        var paths = Paths;
        Ruling121Tree.MakeModelsLedger(paths);
        Ruling121Tree.MakeModelCache(paths);
        Ruling121Tree.MakeRuntime(paths, RuntimeVariants.Cpu);
        Ruling121Tree.WriteModelFile(paths, "model.safetensors", 4); // 台帳は 16 B

        var state = AcquisitionCheck.Check(paths, RuntimeVariants.Cpu);

        Assert.False(state.ModelsReady);
        Assert.Equal([Ruling121Tree.Repo + ":model.safetensors"], state.MissingModelFiles);
    }

    [Fact]
    public void 台帳が読めない配布樹では急かさない()
    {
        // 判断の材料が無いのに毎起動ウィザードを出すほうが害が大きい。
        var paths = Paths;
        Ruling121Tree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var state = AcquisitionCheck.Check(paths, RuntimeVariants.Cpu);

        Assert.True(state.ModelsReady);
        Assert.True(state.Ready);
    }

    [Fact]
    public void 檔の場所は取得台本と同じ規則で組む()
    {
        // server/ywk_fetch_models.py の _resolve_cached／refs_main_path と 1 字も違わない。
        Assert.Equal("models--Aratako--Irodori-TTS-v4.1-Small", AcquisitionCheck.CacheDirName(Ruling121Tree.Repo));
        Assert.Equal(
            Path.Combine(
                "hub", "models--Aratako--Irodori-TTS-v4.1-Small", "snapshots", Ruling121Tree.Revision,
                "tokenizer", "tokenizer.json"),
            AcquisitionCheck.SnapshotPath("hub", Ruling121Tree.Repo, Ruling121Tree.Revision, "tokenizer/tokenizer.json"));
        Assert.Equal(
            Path.Combine("hub", "models--Aratako--Irodori-TTS-v4.1-Small", "refs", "main"),
            AcquisitionCheck.RefsMainPath("hub", Ruling121Tree.Repo));
    }
}

/// <summary>裁定 121 ⒝-2＝主窓の判断・事前検査の 1 行・状態帯の 1 手。</summary>
[Collection(AppServicesCollection.Name)]
public sealed class AcquisitionGuidanceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-121-guide-" + Guid.NewGuid().ToString("N")[..8]);

    private AppPaths Paths => Ruling121Tree.MakePaths(_root);

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

        GC.SuppressFinalize(this);
    }

    private MainViewModel NewMain(AppPaths paths, LauncherSettings settings, IServerProcess server)
    {
        AppServices.Server = server;
        AppServices.GpuEnumerator = null;
        return new MainViewModel(
            paths, settings, new JsonSettingsStore(paths.SettingsPath),
            new CorrectionThreeTree.SilentPlayer());
    }

    [Fact]
    public void 札は立っているのにモデルが無ければ取得へ導く()
    {
        var paths = Paths;
        Ruling121Tree.MakeModelsLedger(paths);
        Ruling121Tree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var main = NewMain(
            paths,
            new LauncherSettings { Variant = RuntimeVariants.Cpu, FirstRunCompleted = true },
            new RecordingServer());

        Assert.False(main.NeedsFirstRun);
        Assert.True(main.NeedsAcquisition);
        Assert.Contains("モデル", main.AcquisitionSummary, StringComparison.Ordinal);
        Assert.Contains(
            "実行系／モデルが揃っていないため、取得からやり直します（不足＝",
            MainViewModel.AcquisitionNotice(main.AcquisitionSummary),
            StringComparison.Ordinal);
    }

    [Fact]
    public void 揃っていれば勝手にウィザードを出さない()
    {
        var paths = Paths;
        Ruling121Tree.MakeModelsLedger(paths);
        Ruling121Tree.MakeModelCache(paths);
        Ruling121Tree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var main = NewMain(
            paths,
            new LauncherSettings { Variant = RuntimeVariants.Cpu, FirstRunCompleted = true },
            new RecordingServer());

        Assert.False(main.NeedsAcquisition);
        Assert.Equal(string.Empty, main.AcquisitionSummary);
        Assert.False(main.Status.AcquisitionNeeded);
    }

    [Fact]
    public void ウィザードを閉じても帯に_1_手が残る()
    {
        // 失敗を待たずに出す＝閉じた利用者が「サーバ起動」を押すまで導線が消える、を作らない。
        var paths = Paths;
        Ruling121Tree.MakeModelsLedger(paths);
        Ruling121Tree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var main = NewMain(
            paths,
            new LauncherSettings { Variant = RuntimeVariants.Cpu, FirstRunCompleted = true },
            new RecordingServer());

        Assert.Equal(ServerState.Stopped, main.Status.State);
        Assert.True(main.Status.AcquisitionNeeded);

        // 取れた後に見直せば消える
        Ruling121Tree.MakeModelCache(paths);
        main.ReapplySettings();

        Assert.False(main.NeedsAcquisition);
        Assert.False(main.Status.AcquisitionNeeded);
    }

    [Fact]
    public void 初回取得がまだの回はこの判断を使わない()
    {
        // 出す口は 1 つでよい（窓は NeedsFirstRun を先に見る）。
        var paths = Paths;
        Ruling121Tree.MakeModelsLedger(paths);

        var main = NewMain(
            paths,
            new LauncherSettings { Variant = RuntimeVariants.Cpu, FirstRunCompleted = false },
            new RecordingServer());

        Assert.True(main.NeedsFirstRun);
        Assert.False(main.NeedsAcquisition);
    }

    [Fact]
    public async Task モデルが無い回は起こす前に断って取り方を告げる()
    {
        var paths = Paths;
        Ruling121Tree.MakeModelsLedger(paths);
        Ruling121Tree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var server = new RecordingServer();
        var main = NewMain(
            paths,
            new LauncherSettings { Variant = RuntimeVariants.Cpu, Port = 18099, FirstRunCompleted = true },
            server);

        Assert.False(await main.StartServerAsync());

        Assert.NotNull(server.Preflight);
        Assert.Contains("モデルがまだありません", server.Preflight!, StringComparison.Ordinal);
        Assert.Contains(AcquisitionCheck.Hint, server.Preflight!, StringComparison.Ordinal);
        Assert.Equal(ServerState.Failed, main.Status.State);
        Assert.True(main.Status.AcquisitionNeeded);
        Assert.Equal(StatusViewModel.AcquisitionLine, main.Status.AcquisitionText);
    }

    [Fact]
    public async Task 実行系が無い断りにも取り方が付く()
    {
        var paths = Paths;
        Ruling121Tree.MakeModelsLedger(paths);

        var server = new RecordingServer();
        var main = NewMain(
            paths,
            new LauncherSettings { Variant = RuntimeVariants.Cpu, Port = 18099, FirstRunCompleted = true },
            server);

        Assert.False(await main.StartServerAsync());

        Assert.Contains("実行系がまだありません", server.Preflight!, StringComparison.Ordinal);
        Assert.Contains(AcquisitionCheck.Hint, server.Preflight!, StringComparison.Ordinal);
        Assert.True(main.Status.AcquisitionNeeded);
    }

    [Fact]
    public void 変種を替えて適用すれば帯を引き直す()
    {
        // 是正・検分＝「適用」で変種が動けば取得の済み具合も動く。引き直さないと、
        // 実行系の無い変種で出た帯が、入っている変種へ替えても消えない（逆も出ない）。
        var paths = Paths;
        Ruling121Tree.MakeModelsLedger(paths);
        Ruling121Tree.MakeModelCache(paths);
        Ruling121Tree.MakeRuntime(paths, RuntimeVariants.Cpu);

        var main = NewMain(
            paths,
            new LauncherSettings { Variant = RuntimeVariants.Cu126, FirstRunCompleted = true },
            new RecordingServer());

        Assert.True(main.NeedsAcquisition); // cu126 の実行系は無い
        Assert.True(main.Status.AcquisitionNeeded);

        main.Settings.Variant = RuntimeVariants.Cpu;
        main.Settings.Apply();

        Assert.False(main.NeedsAcquisition);
        Assert.False(main.Status.AcquisitionNeeded);
    }

    [Fact]
    public void 取れた後は事前検査の断りが残っていても帯を下げる()
    {
        // 是正・検分＝事前検査で断った理由は Failed のまま残るので、取得を済ませて
        // ウィザードを閉じても帯が「取得が未了です。」と言い続けていた。
        var status = new StatusViewModel(
            static () => Task.CompletedTask, static () => Task.CompletedTask);

        status.ApplyState(ServerState.Failed, "モデルがまだありません（不足＝1 檔）。" + AcquisitionCheck.Hint);
        Assert.True(status.AcquisitionNeeded);

        status.ApplyAcquisition(false); // 檔を見たら揃っていた
        Assert.False(status.AcquisitionNeeded);

        // 個体が落ちた回（檔の在否では否定できない事実）はそのまま残す
        status.ApplyState(
            ServerState.Failed,
            ServerStateMachine.RuntimeLoadFailedReason(
                "OSError: does not appear to have a file named model.safetensors"));
        status.ApplyAcquisition(false);
        Assert.True(status.AcquisitionNeeded);
    }

    [Fact]
    public void 実行系が揃っていればウィザードは取得と展開を飛ばす()
    {
        // 是正・検分＝取得キャッシュは 1 射目で空にする（裁定 90 Q-E2 ⑶）ので、
        // モデルだけ無い機体でウィザードを自動で出すと、飛ばさない限り実行系を
        // 丸ごと落とし直したうえで健全な樹を消して入れ直すことになる。
        var paths = Paths;
        Ruling121Tree.MakeModelsLedger(paths);
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cpu, FirstRunCompleted = true };
        var main = NewMain(paths, settings, new RecordingServer());

        var wizard = main.CreateFirstRun();
        Assert.False(wizard.RuntimeLooksSound()); // python.exe がまだ無い

        Ruling121Tree.MakeRuntime(paths, RuntimeVariants.Cpu);
        Assert.True(main.CreateFirstRun().RuntimeLooksSound());
    }

    [Fact]
    public void 状態帯は取得が未了だと読める失敗のときだけ_1_手を出す()
    {
        var status = new StatusViewModel(
            static () => Task.CompletedTask, static () => Task.CompletedTask);

        // ⑴ 事前検査で断った回
        status.ApplyState(ServerState.Failed, "モデルがまだありません（不足＝1 檔）。" + AcquisitionCheck.Hint);
        Assert.True(status.AcquisitionNeeded);
        Assert.Equal(StatusViewModel.AcquisitionLine, status.AcquisitionText);

        // ⑵ 起こした個体が offline でモデルを読めなかった回
        status.ApplyState(
            ServerState.Failed,
            ServerStateMachine.RuntimeLoadFailedReason(
                "OSError: Aratako/Irodori-TTS-v4.1-Small does not appear to have a file named model.safetensors"));
        Assert.True(status.AcquisitionNeeded);

        // ⑶ それ以外の失敗では出さない（ポート・ドライバ・GPU の取り違え）
        status.ApplyState(ServerState.Failed, "ポート 18088 は他のプログラムが握っています。");
        Assert.False(status.AcquisitionNeeded);
        Assert.Equal(string.Empty, status.AcquisitionText);

        // ⑷ モデルの読込の失敗でも、檔が無いと読めない理由なら出さない
        status.ApplyState(
            ServerState.Failed,
            ServerStateMachine.RuntimeLoadFailedReason("RuntimeError: CUDA error: out of memory"));
        Assert.False(status.AcquisitionNeeded);

        // ⑸ 落ちていない回は出さない
        status.ApplyState(ServerState.Ready, null);
        Assert.False(status.AcquisitionNeeded);
    }

    /// <summary>事前検査の理由だけを覚える繋ぎ（起こしはしない）。</summary>
    private sealed class RecordingServer : IServerProcess
    {
        public string? Preflight { get; private set; }

        public ServerState State { get; private set; } = ServerState.Stopped;

        public int? ProcessId => null;

        public int? ExitCode => null;

        public string? FailureReason { get; private set; }

        public ServerLogEvent? Banner => null;

        public Uri? BaseAddress => null;

        public StatusResponse? LatestStatus => null;

        public IReadOnlyList<IrodoriTtsYwk.Launcher.Services.Gpu.OsGpuMemoryRow> LatestOsGpuMemory => [];

#pragma warning disable CS0067 // 偽物なので誰も上げない
        public event EventHandler<ServerStateChangedEventArgs>? StateChanged;

        public event EventHandler<ServerLogLineEventArgs>? LogLine;

        public event EventHandler<StatusResponse>? StatusSampled;
#pragma warning restore CS0067

        public Task<ServerStartResult> StartAsync(
            ServerStartRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ServerStartResult(
                false, ServerState.Failed, null, null, TimeSpan.Zero, "偽物なので起こしません。"));

        public void ReportPreflightFailure(string reason)
        {
            Preflight = reason;
            State = ServerState.Failed;
            FailureReason = reason;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
