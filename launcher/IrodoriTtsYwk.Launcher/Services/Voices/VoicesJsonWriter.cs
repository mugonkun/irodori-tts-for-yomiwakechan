using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Voices;

/// <summary>
/// 契約 ⑺ の実装＝上流が読む別名表 <c>voices.json</c> の書き手。
/// <para>
/// 形＝<c>{"&lt;表示名&gt;":{"ref_wav":"&lt;ascii-id&gt;.wav"}}</c> ＋
/// <c>{"デフォルト":{"no_ref":true}}</c>。焼いた話者は <c>{"ref_latent":"latents/&lt;stem&gt;.pt"}</c> に
/// <b>書き換わる</b>（<c>ref_wav</c> は残さない＝上流は波形と潜在の同時指定を 400 にする＝契約 ⑷ 4-4）。
/// </para>
/// <para>
/// <b>書くときに読み直す</b>（契約 ⑷ 4-3）＝<c>voices.json</c> を書くのはランチャと wrapper の 2 つで、
/// wrapper は事前計算が走ったときに<b>焼いた話者の欄 1 個だけ</b>を <c>ref_latent</c> に差し替える。
/// ランチャが台帳の写しを丸ごと上書きすると、その差し替えが消えて 1 秒級の話者切替に戻る。
/// ⇒ <see cref="Write"/> は既存の檔を読み、<b>台帳が潜在を知らない話者については既存の
/// <c>ref_latent</c> を残す</b>。
/// </para>
/// <para>
/// 上流は毎要求 <c>iterdir()</c>＋別名の読み直しなので<b>再起動は要らない</b>
/// （受け入れ条件 D-3＝再起動なしで一覧に反映）。書き換えは原子的（<c>.tmp</c>→1 手で差し替え）。
/// </para>
/// </summary>
public sealed class VoicesJsonWriter : IVoicesJsonWriter
{
    /// <summary>日本語の話者名を <c>\uXXXX</c> に潰さない（契約 ⑷ 4-1）。</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>潜在の相対パスの前置（<c>voices_dir</c> 直下には置かない＝契約 ⑷ 4-4）。</summary>
    public const string LatentsPrefix = "latents/";

    /// <summary>
    /// 参照 wav の相対パスの前置（是正・2026-09-05）。
    /// <para>
    /// <c>voices_dir</c> 直下に wav を置くと、上流の <c>_scan_voice_files</c> が檔名の幹を
    /// 話者 id にして<b>同じ話者が一覧に 2 件</b>出る（<c>voices.py:63-68</c> は走査に別名を
    /// 合流させるので、別名で上書きされない）。<c>latents/</c> と同じ理由で 1 段下げる。
    /// 上流は相対パスを <c>voices_dir</c> から解決する（<c>_resolve_voice_path</c>）。
    /// </para>
    /// </summary>
    public const string ReferencesPrefix = "refs/";

    public string Render(VoicesYwkFile store) => Render(store, null);

    /// <summary>
    /// 台帳から本文を作る（<b>純関数</b>）。<paramref name="existing"/> に既存の檔の中身を渡すと、
    /// wrapper が書いた <c>ref_latent</c> を残す（契約 ⑷ 4-3）。
    /// </summary>
    public string Render(VoicesYwkFile store, string? existing)
    {
        ArgumentNullException.ThrowIfNull(store);

        var keep = ReadExistingLatents(existing);
        var root = new JsonObject();

        // 「デフォルト」は常に先頭・常に在る（台帳に無くても注入する＝裁定 16・契約 ⑷ 4-2）
        root[VoiceIds.Default] = new JsonObject { ["no_ref"] = true };

        foreach (var id in VoiceIds.Order(store.Voices.Keys, null))
        {
            if (string.Equals(id, VoiceIds.Default, StringComparison.Ordinal))
            {
                continue;
            }

            if (!store.Voices.TryGetValue(id, out var entry))
            {
                continue;
            }

            if (entry.NoRef)
            {
                root[id] = new JsonObject { ["no_ref"] = true };
                continue;
            }

            // ⑴ 台帳が知っている潜在 → ⑵ wrapper が焼いた潜在 → ⑶ 参照 wav の順
            var latent = Normalize(entry.RefLatent) ?? (keep.TryGetValue(id, out var known) ? known : null);
            if (latent is not null)
            {
                root[id] = new JsonObject { ["ref_latent"] = latent };
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.File))
            {
                root[id] = new JsonObject { ["ref_wav"] = ReferencePath(entry.File) };
            }

            // 参照も潜在も無い話者は載せない（載せると上流が解決に失敗する）
        }

        return root.ToJsonString(JsonOptions) + Environment.NewLine;
    }

    public void Write(string path, VoicesYwkFile store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(store);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string? existing = null;
        try
        {
            if (File.Exists(path))
            {
                existing = File.ReadAllText(path, Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // 読めない＝残す物が無い（新しく書く）
        }

        var json = Render(store, existing);

        // 原子的な着地＝別檔に全部書いてから 1 手で差し替える
        var temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// 既存の <c>voices.json</c> から <c>ref_latent</c> だけを拾う（<b>純関数</b>）。
    /// 文字列の別名（<c>"名前":"x.pt"</c>）も上流が潜在として読む（<c>voices.py:213</c>）ので拾う。
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReadExistingLatents(string? existing)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(existing))
        {
            return found;
        }

        try
        {
            using var document = JsonDocument.Parse(existing);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return found;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (value is not null && value.EndsWith(".pt", StringComparison.OrdinalIgnoreCase))
                    {
                        found[property.Name] = value;
                    }

                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Object
                    && property.Value.TryGetProperty("ref_latent", out var latent)
                    && latent.ValueKind == JsonValueKind.String
                    && Normalize(latent.GetString()) is string text)
                {
                    found[property.Name] = text;
                }
            }
        }
        catch (JsonException)
        {
            // 壊れていた＝残す物が無い（上流も読めないので新しく書くのが正しい）
        }

        return found;
    }

    /// <summary>
    /// 台帳の檔名（ASCII・幹だけ）を <c>voices.json</c> に載せる相対パスにする（<b>純関数</b>）。
    /// 既に <c>/</c> を含む値（＝場所を明示した台帳）はそのまま通す。
    /// </summary>
    public static string ReferencePath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var text = fileName.Trim().Replace('\\', '/');
        return text.Contains('/', StringComparison.Ordinal) ? text : ReferencesPrefix + text;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace('\\', '/');
}
