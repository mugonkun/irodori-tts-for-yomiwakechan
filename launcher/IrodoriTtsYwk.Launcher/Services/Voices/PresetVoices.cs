using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Voices;

/// <summary>
/// プリセット 1 名の素性（配布樹から読むだけ）。
/// </summary>
/// <param name="Id">話者 id（＝表示名・日本語可）。</param>
/// <param name="FileName"><c>voices\</c> に置く檔名（ASCII）。</param>
/// <param name="SourcePath">配布樹の実体。</param>
/// <param name="Caption">caption の既定（無ければ null）。</param>
public sealed record PresetVoice(string Id, string FileName, string SourcePath, string? Caption);

/// <summary>
/// プリセット話者の初回展開（設計書 §4＝<c>voices/presets</c> を <c>&lt;data&gt;/voices</c> へ写す）。
/// <para>
/// <b>読む場所は 3 通りある</b>（配布樹の形が便 A の組み立てで変わりうるため・順に試す）＝
/// ⑴ <c>&lt;app&gt;/voices/presets.json</c>（表示名と caption を持つ台帳）
/// ⑵ <c>&lt;app&gt;/voices/voices.ywk.json</c>（同形の台帳＝いまの <c>build/out/app</c> はこれ）
/// ⑶ <c>&lt;app&gt;/voices/presets/*.wav</c> か <c>&lt;app&gt;/voices/*.wav</c>（台帳が無い＝檔名を id にする）。
/// </para>
/// <para>
/// <b>1 度だけ</b>＝利用者データ側の台帳がまだ無いときにしか走らない。削除は利用者の意思で可
/// （設計書 §4＝再インストールで戻る）ので、消したプリセットを毎回書き戻さない。
/// </para>
/// <para>
/// <b>導入先には 1 檔も書かない</b>（配布樹は読むだけ＝Program Files でも壊れない）。
/// </para>
/// </summary>
public static class PresetVoices
{
    /// <summary>
    /// 配布樹からプリセットの一覧を読む（<b>檔の実体は写さない＝計画するだけ</b>）。
    /// </summary>
    public static IReadOnlyList<PresetVoice> Discover(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var appVoices = Path.Combine(paths.AppDir, "voices");

        // ⑴ voices/presets.json＝**配列 `presets`**（表示名と二次 wav の檔名を持つ正本）。
        //    wav の実体は voices/presets/ に居る。
        var fromPresets = FromPresetsJson(paths.PresetsJsonPath, paths.PresetVoicesDir, appVoices);
        if (fromPresets is { Count: > 0 })
        {
            return fromPresets;
        }

        // ⑵ 同形の台帳（`voices` オブジェクト）＝いまの build/out/app の雛形。
        var fromTable = FromTable(paths.PresetsJsonPath, appVoices)
            ?? FromTable(Path.Combine(appVoices, "voices.ywk.json"), appVoices);
        if (fromTable is { Count: > 0 })
        {
            return fromTable;
        }

        // ⑶ 台帳が無い＝檔名の幹を id にする（表示名が ASCII になるので最後の手段）。
        var scanned = FromDirectory(paths.PresetVoicesDir);
        return scanned.Count > 0 ? scanned : FromDirectory(appVoices);
    }

    /// <summary>
    /// 初回展開を実行する（既に台帳が在れば何もしない）。戻り＝写した件数。
    /// <para>
    /// <b>「台帳は在るがプリセットが 1 件も無い」も初回と見なす</b>（是正・2026-09-05）。
    /// <c>presets.json</c> を読めていなかったころに作られた台帳（プリセット 0 件）を持つ
    /// 利用者へ直した版を配っても、<see cref="File.Exists(string)"/> の判定だけでは
    /// 12 名が永久に入らない。
    /// </para>
    /// </summary>
    public static int InstallIfFirstRun(AppPaths paths, VoiceStore store)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(store);

        if (!File.Exists(store.TablePath))
        {
            return Install(paths, store, VoiceStore.Empty());
        }

        var existing = store.Load();
        if (existing.PresetsInstalled)
        {
            return 0; // 2 回目以降＝利用者が消したプリセットを書き戻さない
        }

        // 台帳は在るが「展開を通した」印が無い＝presets.json を読めていなかった回の残骸。
        // 1 度だけ通す（既に在る id は飛ばす）。
        return Install(paths, store, existing, skipExisting: true);
    }

    /// <summary>
    /// 同梱のプリセットを入れ直す（設計書 §4「再インストールで戻る」の実装＝是正・2026-09-05）。
    /// <para>
    /// 利用者データはアプリを入れ直しても消えないので、<see cref="InstallIfFirstRun"/> だけでは
    /// 消したプリセットが二度と戻らない。<b>既に在る id は飛ばす</b>ので、名付け直した話者や
    /// 利用者が足した話者には触れない。戻り＝入れ直した件数。
    /// </para>
    /// </summary>
    public static int Restore(AppPaths paths, VoiceStore store)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(store);
        return Install(paths, store, store.Load(), skipExisting: true);
    }

    private static int Install(
        AppPaths paths, VoiceStore store, VoicesYwkFile table, bool skipExisting = false)
    {
        var presets = Discover(paths);
        var voices = new Dictionary<string, VoiceEntry>(
            VoiceStore.EnsureDefault(table).Voices, StringComparer.Ordinal);
        var copied = 0;

        Directory.CreateDirectory(store.ReferencesDir);
        foreach (var preset in presets)
        {
            if (skipExisting && voices.ContainsKey(preset.Id))
            {
                continue;
            }

            var destination = Path.Combine(store.ReferencesDir, preset.FileName);
            try
            {
                if (!File.Exists(destination))
                {
                    File.Copy(preset.SourcePath, destination, overwrite: false);
                }
            }
            catch (IOException)
            {
                continue; // 1 名の失敗で残り全部を落とさない
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            voices[preset.Id] = new VoiceEntry
            {
                DisplayName = preset.Id,
                File = preset.FileName,
                Caption = preset.Caption,
                Preset = true,
                NoRef = false,
                Origin = "preset",
                AddedAt = DateTimeOffset.Now,
            };
            copied++;
        }

        // **印は「本当に見つけた」ときだけ立てる**。配布樹にプリセットが 1 檔も無い版
        // （build/assemble-app.ps1 がまだ voices/presets/ を写していない＝便 A へ票）で
        // 印を立ててしまうと、檔が入った版に更新しても 12 名が永久に入らない。
        var installed = table.PresetsInstalled || presets.Count > 0;
        store.Save(table with { Voices = voices, PresetsInstalled = installed });
        return copied;
    }

    /// <summary>
    /// <c>voices/presets.json</c> の実物（<b>トップレベルの鍵は <c>presets</c>・値は配列</b>）を読む。
    /// <para>
    /// <b>話者 id は <c>display_name</c></b>（裁定 17・契約 ⑷ 4-1 の例
    /// <c>{"id":"琴葉茜","display_name":"琴葉茜（関西弁）"}</c>）＝日本語の名で一覧に出る。
    /// 檔は <c>secondary.file</c>（ASCII）で、実体は <c>voices/presets/</c> に居る。
    /// <c>status</c> が <c>done</c> 以外の行（裁定 27 の CeVIO 弦巻マキ）と、二次 wav の無い行は飛ばす。
    /// </para>
    /// <para>
    /// 是正・2026-09-05＝以前はトップレベルの <c>voices</c>（オブジェクト）しか読まなかったので
    /// この檔は毎回 null を返し、12 名の日本語表示名と caption が丸ごと捨てられて、
    /// 檔名の幹（<c>vv_mochiko_sexy</c>）が話者 id になっていた。
    /// </para>
    /// </summary>
    private static IReadOnlyList<PresetVoice>? FromPresetsJson(
        string presetsJsonPath, string presetWavDir, string appVoicesDir)
    {
        if (!File.Exists(presetsJsonPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(presetsJsonPath));
            if (!document.RootElement.TryGetProperty("presets", out var presets)
                || presets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var found = new List<PresetVoice>();
            foreach (var element in presets.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                // 二次 wav がまだ無い行（status != done）は配布物に檔が無い。
                if (element.TryGetProperty("status", out var status)
                    && status.ValueKind == JsonValueKind.String
                    && !string.Equals(status.GetString(), "done", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!element.TryGetProperty("secondary", out var secondary)
                    || secondary.ValueKind != JsonValueKind.Object
                    || !secondary.TryGetProperty("file", out var file)
                    || file.ValueKind != JsonValueKind.String
                    || file.GetString() is not string fileName
                    || fileName.Length == 0)
                {
                    continue;
                }

                // 話者 id＝display_name（無ければ id へ落ちる＝黙って ASCII にはしない）
                var displayName = Text(element, "display_name") ?? Text(element, "id");
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                var source = Path.Combine(presetWavDir, fileName);
                if (!File.Exists(source))
                {
                    source = Path.Combine(appVoicesDir, fileName);
                    if (!File.Exists(source))
                    {
                        continue;
                    }
                }

                found.Add(new PresetVoice(displayName.Trim(), fileName, source, Text(element, "caption")));
            }

            return found;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<PresetVoice>? FromTable(string tablePath, string appVoicesDir)
    {
        if (!File.Exists(tablePath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(tablePath));
            if (!document.RootElement.TryGetProperty("voices", out var voices)
                || voices.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var found = new List<PresetVoice>();
            foreach (var property in voices.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!property.Value.TryGetProperty("file", out var file)
                    || file.ValueKind != JsonValueKind.String
                    || file.GetString() is not string fileName
                    || fileName.Length == 0)
                {
                    continue; // 参照なし（「デフォルト」）は写す檔が無い
                }

                var source = Path.Combine(appVoicesDir, fileName);
                if (!File.Exists(source))
                {
                    continue;
                }

                string? caption = null;
                if (property.Value.TryGetProperty("caption", out var captionValue)
                    && captionValue.ValueKind == JsonValueKind.String)
                {
                    caption = captionValue.GetString();
                }

                found.Add(new PresetVoice(property.Name, fileName, source, caption));
            }

            return found;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static IReadOnlyList<PresetVoice> FromDirectory(string directory)
    {
        var found = new List<PresetVoice>();
        if (!Directory.Exists(directory))
        {
            return found;
        }

        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (Array.IndexOf(VoiceIds.WavExtensions, extension) < 0)
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            found.Add(new PresetVoice(name, Path.GetFileName(path), path, null));
        }

        found.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        return found;
    }
}
