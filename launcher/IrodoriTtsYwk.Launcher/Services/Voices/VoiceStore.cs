using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Voices;

/// <summary>
/// 契約 ⑺ の実装＝配布版の話者台帳（<c>voices/voices.ywk.json</c>）の所有者。
/// <para>
/// <b>追加は 2 操作</b>（受け入れ条件 D-3）＝wav を選ぶ→名前を付ける（日本語可）。
/// 檔は <c>voices\ywk-&lt;sha12&gt;.wav</c> に写す（<b>内容の sha256 の先頭 12 桁</b>＝本体の
/// <c>ywk-&lt;sha12&gt;</c> の型）。話者名が日本語でも檔名は ASCII になる。
/// <b>上流の登録 API は使わない</b>（ASCII 限定で日本語名が 400・そもそも口ごと外してある）。
/// </para>
/// <para>
/// <b>「デフォルト」は常在</b>（裁定 16）＝台帳が無くても・壊れていても 1 件は返る。
/// </para>
/// <para>
/// <b>テストの継ぎ目は public コンストラクタ</b>＝場所を渡して実際の檔で往復を試せる。
/// </para>
/// </summary>
public sealed class VoiceStore : IVoiceStore
{
    /// <summary>ASCII の id の前置（本体の <c>ywk-&lt;sha12&gt;</c> と同型）。</summary>
    public const string IdPrefix = "ywk-";

    /// <summary>sha256 から採る桁数。</summary>
    public const int IdHashLength = 12;

    /// <summary>台帳の note（何のための檔かを檔自身に書いておく）。</summary>
    public const string Note = "distributor-side speaker table (display name, default caption, default parameters).";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();

    /// <summary>実機用（<see cref="AppPaths"/> から場所を採る）。</summary>
    public VoiceStore(AppPaths paths)
        : this(
            (paths ?? throw new ArgumentNullException(nameof(paths))).VoicesYwkJsonPath,
            paths.VoicesDir,
            paths.ReferenceWavDir)
    {
    }

    /// <summary>テスト用＝檔の場所を直に渡す。</summary>
    /// <param name="tablePath"><c>voices.ywk.json</c>。</param>
    /// <param name="voicesDir"><c>IRODORI_VOICES_DIR</c>（別名表の相対パスの基点）。</param>
    /// <param name="referencesDir">
    /// 写した参照 wav の置き場。既定＝<c>&lt;voicesDir&gt;/refs</c>。
    /// <b><c>voicesDir</c> 直下には置かない</b>（上流の走査が檔名の幹を話者 id にするため＝
    /// <see cref="AppPaths.ReferenceWavDir"/>）。
    /// </param>
    public VoiceStore(string tablePath, string voicesDir, string? referencesDir = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(voicesDir);
        TablePath = tablePath;
        VoicesDir = voicesDir;
        ReferencesDir = string.IsNullOrWhiteSpace(referencesDir)
            ? Path.Combine(voicesDir, ReferencesSubdirectory)
            : referencesDir;
    }

    /// <summary><c>voices_dir</c> の下に切る参照 wav の部屋の名（<c>latents/</c> の隣）。</summary>
    public const string ReferencesSubdirectory = "refs";

    /// <summary><c>voices/voices.ywk.json</c>。</summary>
    public string TablePath { get; }

    /// <summary>上流が別名の相対パスを解決する根（<c>IRODORI_VOICES_DIR</c>）。</summary>
    public string VoicesDir { get; }

    /// <summary>写した参照 wav の置き場（<c>&lt;voices_dir&gt;/refs</c>）。</summary>
    public string ReferencesDir { get; }

    /// <summary>直前の <see cref="Load"/> が既定へ落ちた理由（正常なら null）。</summary>
    public string? LastLoadError { get; private set; }

    /// <summary>「デフォルト」1 件だけの新品（台帳が無い・壊れているときの姿）。</summary>
    public static VoicesYwkFile Empty() => new()
    {
        Schema = 1,
        Note = Note,
        Voices = new Dictionary<string, VoiceEntry>(StringComparer.Ordinal)
        {
            [VoiceIds.Default] = new VoiceEntry
            {
                DisplayName = VoiceIds.Default,
                NoRef = true,
                Preset = true,
                Origin = "preset",
            },
        },
    };

    public VoicesYwkFile Load()
    {
        lock (_gate)
        {
            LastLoadError = null;
            try
            {
                if (!File.Exists(TablePath))
                {
                    return Empty();
                }

                var text = File.ReadAllText(TablePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text))
                {
                    LastLoadError = "話者台帳が空だったので「デフォルト」だけで始めます。";
                    return Empty();
                }

                var parsed = JsonSerializer.Deserialize<VoicesYwkFile>(text, JsonOptions);
                if (parsed is null)
                {
                    LastLoadError = "話者台帳が読めないので「デフォルト」だけで始めます。";
                    return Empty();
                }

                return EnsureDefault(parsed);
            }
            catch (JsonException ex)
            {
                LastLoadError = "話者台帳が読めないので「デフォルト」だけで始めます：" + ex.Message;
                return Empty();
            }
            catch (IOException ex)
            {
                LastLoadError = "話者台帳が開けないので「デフォルト」だけで始めます：" + ex.Message;
                return Empty();
            }
            catch (UnauthorizedAccessException ex)
            {
                LastLoadError = "話者台帳が開けないので「デフォルト」だけで始めます：" + ex.Message;
                return Empty();
            }
        }
    }

    public void Save(VoicesYwkFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(TablePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(EnsureDefault(file), JsonOptions);
            var temp = TablePath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, TablePath, overwrite: true);
        }
    }

    /// <summary>
    /// 檔名に使う ASCII の id（<b>内容の sha256 の先頭 12 桁</b>）。
    /// 16 進以外が来たら投げる（呼ぶ側の誤りを黙って通さない）。
    /// </summary>
    public string MakeAsciiId(string wavSha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wavSha256Hex);
        var hex = wavSha256Hex.Trim().ToLowerInvariant();
        if (hex.Length < IdHashLength)
        {
            throw new ArgumentException("sha256 が短すぎます。", nameof(wavSha256Hex));
        }

        foreach (var c in hex.AsSpan(0, IdHashLength))
        {
            if (!char.IsAsciiHexDigitLower(c))
            {
                throw new ArgumentException("sha256 は 16 進でなければなりません。", nameof(wavSha256Hex));
            }
        }

        return IdPrefix + hex[..IdHashLength];
    }

    public VoicesYwkFile AddVoice(string displayName, string sourceWavPath, string? caption)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceWavPath);

        var name = displayName.Trim();
        if (VoiceIds.IsNoRef(name))
        {
            throw new ArgumentException("「" + VoiceIds.Default + "」は参照なしの話者なので上書きできません。", nameof(displayName));
        }

        var extension = Path.GetExtension(sourceWavPath).ToLowerInvariant();
        if (Array.IndexOf(VoiceIds.WavExtensions, extension) < 0)
        {
            throw new ArgumentException("この拡張子は参照に使えません：" + extension, nameof(sourceWavPath));
        }

        var id = MakeAsciiId(Sha256OfFile(sourceWavPath));
        var fileName = id + extension;

        Directory.CreateDirectory(ReferencesDir);
        var destination = Path.Combine(ReferencesDir, fileName);
        if (!File.Exists(destination))
        {
            File.Copy(sourceWavPath, destination, overwrite: false);
        }

        var table = Load();
        var voices = new Dictionary<string, VoiceEntry>(table.Voices, StringComparer.Ordinal);
        voices[name] = new VoiceEntry
        {
            DisplayName = name,
            File = fileName,
            Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
            Preset = false,
            NoRef = false,
            Origin = "user",
            AddedAt = DateTimeOffset.Now,
        };

        var updated = table with { Voices = voices };
        Save(updated);
        return updated;
    }

    public VoicesYwkFile RemoveVoice(string voiceId) => RemoveVoiceDetailed(voiceId).Table;

    /// <summary>
    /// 削除して<b>何が起きたかを返す</b>（是正・2026-09-05）。
    /// <para>
    /// 戻りを台帳だけにしていたころは、台帳に居ない話者（上流の走査で見えているだけの
    /// 幽霊行）を「削除」しても素の台帳が返るので、画面は無条件に「削除しました。」と
    /// 告げ、行はそのまま残った。<see cref="VoiceRemoval.WasKnown"/> がその区別である。
    /// </para>
    /// <para>
    /// 消す物は契約 ⑷ 4-3 の 4 つのうち<b>檔の 3 つ</b>＝⒝ <c>latents/&lt;stem&gt;.pt</c>
    /// ⒞ 同名 <c>.json</c> ⒟ 参照 wav。⒜ <c>voices.json</c> の欄は
    /// <see cref="VoicesJsonWriter"/> が書き直す。<c>&lt;stem&gt;</c> は<b>話者 id 由来</b>
    /// （<c>server/ywk_server.py:2506-2518</c> の <c>latent_stem</c>＝<c>sha256(voice_id)[:12]</c>）なので、
    /// 台帳の <c>ref_latent</c> が空でも場所は判る＝サーバが止まっていても取りこぼさない。
    /// </para>
    /// </summary>
    public VoiceRemoval RemoveVoiceDetailed(string voiceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceId);
        var id = voiceId.Trim();
        if (string.Equals(id, VoiceIds.Default, StringComparison.Ordinal))
        {
            throw new ArgumentException("「" + VoiceIds.Default + "」は消せません（一覧に常在します）。", nameof(voiceId));
        }

        var table = Load();
        if (!table.Voices.TryGetValue(id, out var entry))
        {
            return new VoiceRemoval(table, false, []);
        }

        var voices = new Dictionary<string, VoiceEntry>(table.Voices, StringComparer.Ordinal);
        voices.Remove(id);

        var removed = new List<string>(3);

        // ⒟ 写した wav も消す（他の話者が同じ檔を指していない場合だけ＝同内容 2 名の登録に耐える）
        if (!string.IsNullOrWhiteSpace(entry.File) && !IsFileUsed(voices, entry.File))
        {
            if (TryDelete(Path.Combine(ReferencesDir, entry.File)))
            {
                removed.Add(entry.File);
            }

            // 旧い置き場（voices_dir 直下）に残っている個体も掃除する。
            TryDelete(Path.Combine(VoicesDir, entry.File));
        }

        // ⒝⒞ 焼いた潜在と sidecar（台帳が知らなくても話者 id から場所は決まる）
        removed.AddRange(DeleteLatentFiles(id, entry.RefLatent));

        var updated = table with { Voices = voices };
        Save(updated);
        return new VoiceRemoval(updated, true, removed);
    }

    /// <summary>
    /// その話者の焼いた潜在と sidecar を消す（<b>檔だけ</b>＝台帳には触れない）。戻り＝実際に消えた檔の名。
    /// <para>
    /// 消す場所の規則は削除（<see cref="RemoveVoiceDetailed"/>）と改名（<see
    /// cref="PresetVoices.MigrateRenamed"/>＝裁定 108）で<b>1 つ</b>である。幹は話者 id 由来
    /// （<see cref="LatentStem"/>）なので、改名した話者の旧い <c>.pt</c> は新しい id からは
    /// 二度と当たらない＝ここで消さないと置き場に居座る。
    /// </para>
    /// </summary>
    /// <param name="voiceId">話者 id（改名なら<b>旧</b> id）。</param>
    /// <param name="refLatent">台帳が知っている潜在の相対パス（無ければ null）。</param>
    public IReadOnlyList<string> DeleteLatentFiles(string voiceId, string? refLatent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceId);
        var removed = new List<string>(2);
        foreach (var path in LatentPaths(voiceId.Trim(), refLatent))
        {
            if (TryDelete(path))
            {
                removed.Add(Path.GetFileName(path));
            }
        }

        return removed;
    }

    /// <summary>
    /// その話者の潜在まわりの檔（<c>latents/&lt;stem&gt;.pt</c> と同名 <c>.json</c>）。
    /// <paramref name="refLatent"/> が台帳に在ればそれも足す（wrapper が別の場所に焼いた個体）。
    /// </summary>
    private IEnumerable<string> LatentPaths(string voiceId, string? refLatent)
    {
        var stem = LatentStem(voiceId);
        yield return Path.Combine(VoicesDir, VoicesJsonWriter.LatentsPrefix.TrimEnd('/'), stem + ".pt");
        yield return Path.Combine(VoicesDir, VoicesJsonWriter.LatentsPrefix.TrimEnd('/'), stem + ".json");

        if (string.IsNullOrWhiteSpace(refLatent))
        {
            yield break;
        }

        var relative = refLatent.Trim().Replace('/', Path.DirectorySeparatorChar);
        var full = Path.Combine(VoicesDir, relative);
        yield return full;
        yield return Path.ChangeExtension(full, ".json");
    }

    /// <summary>
    /// 焼いた潜在の檔名の幹（<b>純関数</b>）。<c>server/ywk_server.py</c> の <c>latent_stem</c>
    /// （2127〜2139 行）と<b>1 字も違わない規則</b>で組む＝
    /// <c>[^A-Za-z0-9_-]+</c> を <c>-</c> に畳み、両端の <c>-</c> を落とし、48 字で切り、
    /// もう 1 度両端を落として、話者 id の UTF-8 の sha256 の先頭 12 桁を後ろに付ける。
    /// ASCII が 1 字も残らなければ <c>ywk-&lt;12 桁&gt;</c>。
    /// <para>
    /// これが判れば、<b>サーバが止まっていても</b>焼いた <c>.pt</c> と sidecar の場所が決まる
    /// （＝削除の取りこぼしが無い）。
    /// </para>
    /// </summary>
    public static string LatentStem(string voiceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceId);
        var id = voiceId.Trim();
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..IdHashLength];

        var folded = AsciiUnsafe.Replace(id, "-").Trim('-');
        if (folded.Length > 48)
        {
            folded = folded[..48];
        }

        folded = folded.Trim('-');
        return folded.Length == 0 ? "ywk-" + digest : folded + "-" + digest;
    }

    /// <summary>wrapper の <c>_ASCII_UNSAFE</c> と同じ（檔名に残す字の集合）。</summary>
    private static readonly System.Text.RegularExpressions.Regex AsciiUnsafe =
        new("[^A-Za-z0-9_-]+", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>檔の内容の sha256（16 進小文字）。</summary>
    public static string Sha256OfFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// 参照 wav の長さについての勧め（設計書 §4＝10〜30 s）。
    /// <b>純関数</b>＝判定だけ（長さの実測は呼ぶ側）。
    /// </summary>
    public static string? AdviseReferenceLength(TimeSpan length)
    {
        if (length <= TimeSpan.Zero)
        {
            return null;
        }

        if (length < VoiceIds.RecommendedRefMin)
        {
            return "参照が短めです（"
                + length.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)
                + " 秒）。10〜30 秒を勧めます。";
        }

        if (length > VoiceIds.RecommendedRefMax)
        {
            return "参照が長めです（"
                + length.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)
                + " 秒）。10〜30 秒を勧めます（上流は 120 秒で切り詰めます）。";
        }

        return null;
    }

    /// <summary>「デフォルト」を常在させる（<b>純関数</b>）。</summary>
    public static VoicesYwkFile EnsureDefault(VoicesYwkFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Voices.ContainsKey(VoiceIds.Default))
        {
            return file;
        }

        var voices = new Dictionary<string, VoiceEntry>(file.Voices, StringComparer.Ordinal)
        {
            [VoiceIds.Default] = new VoiceEntry
            {
                DisplayName = VoiceIds.Default,
                NoRef = true,
                Preset = true,
                Origin = "preset",
            },
        };

        return file with { Voices = voices };
    }

    private static bool IsFileUsed(IReadOnlyDictionary<string, VoiceEntry> voices, string fileName)
    {
        foreach (var entry in voices.Values)
        {
            if (string.Equals(entry.File, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            // 掴まれている＝次の起動で消える（台帳からは既に外れている）
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
            return false;
        }
    }

    /// <summary>
    /// 旧い置き場（<c>voices_dir</c> 直下）に残っている参照 wav を <c>refs/</c> へ移す
    /// （是正・2026-09-05 より前に登録した個体のため）。戻り＝移した件数。
    /// <para>
    /// 移すのは<b>台帳が名前を知っている檔だけ</b>＝利用者が自分で置いた檔には触れない。
    /// </para>
    /// </summary>
    public int MigrateReferences()
    {
        lock (_gate)
        {
            var table = Load();
            var moved = 0;

            foreach (var entry in table.Voices.Values)
            {
                if (string.IsNullOrWhiteSpace(entry.File))
                {
                    continue;
                }

                var stale = Path.Combine(VoicesDir, entry.File);
                var wanted = Path.Combine(ReferencesDir, entry.File);
                if (!File.Exists(stale) || string.Equals(
                        Path.GetFullPath(stale), Path.GetFullPath(wanted), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(ReferencesDir);
                    if (File.Exists(wanted))
                    {
                        File.Delete(stale);
                    }
                    else
                    {
                        File.Move(stale, wanted);
                    }

                    moved++;
                }
                catch (IOException)
                {
                    // 掴まれている＝次の起動でもう 1 度試す
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return moved;
        }
    }
}

/// <summary>
/// 話者を 1 名消した結果。
/// </summary>
/// <param name="Table">消した後の台帳。</param>
/// <param name="WasKnown">
/// その名が<b>台帳に居たか</b>。偽＝上流の走査で見えているだけの檔（配布版の台帳の外）で、
/// 消しても何も起きない＝画面は「削除しました」と言ってはいけない。
/// </param>
/// <param name="RemovedFiles">実際に消えた檔の名（参照 wav・潜在・sidecar）。</param>
public sealed record VoiceRemoval(VoicesYwkFile Table, bool WasKnown, IReadOnlyList<string> RemovedFiles);
