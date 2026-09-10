using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>
/// 配布版の話者台帳 1 件（<c>voices/voices.ywk.json</c>＝契約 ⑷ 4-3）。
/// <b>上流には置き場が無い</b>（<c>VoiceSpec</c> はパス 5 欄＋<c>no_ref</c> の 7 欄だけで、
/// 余分な鍵は無警告で捨てられる）ので、表示名・caption 既定・既定パラメータはここが持つ。
/// </summary>
public sealed record VoiceEntry
{
    /// <summary>UI に出す名（日本語可）。上流の話者 id もこの名前である（契約 ⑷ 4-1）。</summary>
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = string.Empty;

    /// <summary><c>voices\</c> 直下の wav の檔名（ASCII）。参照なしの「デフォルト」は null。</summary>
    [JsonPropertyName("file")] public string? File { get; init; }

    /// <summary>この話者の caption 既定（null＝欄を出さない＝上流の既定）。</summary>
    [JsonPropertyName("caption")] public string? Caption { get; init; }

    /// <summary>この話者の既定パラメータ（<c>num_steps</c> 等）。空＝欄を出さない。</summary>
    [JsonPropertyName("defaults")]
    public IReadOnlyDictionary<string, JsonElement> Defaults { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    /// <summary>同梱のプリセット（裁定 17 の 11 名 ＋ 裁定 118 の 1 名＝<b>12 名</b>。
    /// 台帳 <c>voices/presets.json</c> は 13 行だが、弦巻マキ 英は <c>status: skipped</c> で同梱されない）。
    /// 削除は利用者の意思で可。</summary>
    [JsonPropertyName("preset")] public bool Preset { get; init; }

    /// <summary>参照なし（「デフォルト」だけが真＝裁定 16）。</summary>
    [JsonPropertyName("no_ref")] public bool NoRef { get; init; }

    /// <summary><c>preset</c>／<c>user</c>。</summary>
    [JsonPropertyName("origin")] public string? Origin { get; init; }

    [JsonPropertyName("added_at")] public DateTimeOffset? AddedAt { get; init; }

    /// <summary>
    /// 焼いた参照潜在（<c>latents/&lt;stem&gt;.pt</c>・裁定 65）。非 null なら
    /// <c>voices.json</c> の別名は <c>ref_latent</c> に<b>書き換わる</b>（<c>ref_wav</c> は残さない＝
    /// 上流は波形と潜在の同時指定を 400 にする）。
    /// </summary>
    [JsonPropertyName("ref_latent")] public string? RefLatent { get; init; }
}

/// <summary>
/// <c>voices/voices.ywk.json</c> の全体（便 A の assemble-app が置く雛形と同形）。
/// 鍵が話者 id（日本語可）。
/// </summary>
public sealed record VoicesYwkFile
{
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;

    [JsonPropertyName("note")] public string? Note { get; init; }

    /// <summary>
    /// 同梱プリセットの初回展開を<b>1 度でも通したか</b>（是正・2026-09-05）。
    /// <para>
    /// 「台帳が在る」だけを初回の判定にしていたころは、<c>voices/presets.json</c> を読めて
    /// いなかった回に作られた台帳（プリセット 0 件）を持つ利用者へ直した版を配っても
    /// 12 名が永久に入らなかった。この印が無い台帳は<b>1 度だけ</b>展開を通す
    /// （既に在る id は飛ばすので、利用者が消したプリセットは書き戻さない）。
    /// </para>
    /// </summary>
    [JsonPropertyName("presets_installed")] public bool PresetsInstalled { get; init; }

    /// <summary>
    /// この台帳へ<b>1 度でも差し出した</b>同梱プリセットの話者 id（裁定 121）。
    /// <para>
    /// <see cref="PresetsInstalled"/> の印だけでは<b>後の版で増えた</b>プリセットが永久に入らない
    /// （実射＝v1.0.1 で足した「シャンパンコール（ホスクラ）」が、v1.0.0 から使っている台帳の
    /// 話者一覧に出てこない）。この欄が「差し出した／差し出していない」を覚えるので、
    /// <b>利用者が消した 1 名は消えたまま</b>・<b>配布側が足した 1 名だけ</b>が起動で入る
    /// （<see cref="Services.Voices.PresetVoices.InstallNew"/>）。
    /// </para>
    /// <para>
    /// <c>null</c>＝この欄を知らない版（≦ v1.0.1）が書いた台帳。次の起動で
    /// <b>いま台帳に居るプリセットの id</b>で種を蒔く（＝「消した」と「まだ差し出していない」を
    /// 区別できないので、そこで消してあった 1 名は<b>1 度だけ</b>戻る）。
    /// </para>
    /// </summary>
    [JsonPropertyName("presets_installed_ids")]
    public IReadOnlyList<string>? PresetsInstalledIds { get; init; }

    [JsonPropertyName("voices")]
    public IReadOnlyDictionary<string, VoiceEntry> Voices { get; init; } =
        new Dictionary<string, VoiceEntry>(StringComparer.Ordinal);
}

/// <summary>
/// 契約 ⑺＝配布版の話者台帳の所有者（<c>voices/</c> と <c>voices.json</c> を書くのはランチャだけ）。
/// <para>
/// 追加＝wav を選ぶ→名前を付ける（日本語可）→ <c>voices\&lt;ascii-id&gt;.wav</c> に写す→
/// <c>voices.json</c> を原子的に書き換える（受け入れ条件 D-3＝2 操作・再起動なしで一覧に反映）。
/// <b>上流の登録 API は使わない</b>（ASCII 限定で日本語名が 400・そもそも口ごと外してある）。
/// </para>
/// </summary>
public interface IVoiceStore
{
    /// <summary>台帳を読む（無ければ「デフォルト」1 件だけの新品）。</summary>
    VoicesYwkFile Load();

    /// <summary>台帳を原子的に書く。</summary>
    void Save(VoicesYwkFile file);

    /// <summary>
    /// wav の檔名に使う ASCII の id（本体の <c>ywk-&lt;sha12&gt;</c> の型を倣う＝
    /// <b>内容の sha256 の先頭 12 桁</b>）。話者名が日本語でも檔名は ASCII になる。
    /// </summary>
    string MakeAsciiId(string wavSha256Hex);

    /// <summary>参照 wav を <c>voices\</c> へ写し、台帳に足す（返りは新しい台帳）。</summary>
    VoicesYwkFile AddVoice(string displayName, string sourceWavPath, string? caption);

    /// <summary>台帳から外し、写した wav も消す。</summary>
    VoicesYwkFile RemoveVoice(string voiceId);

    /// <summary>
    /// 同上だが<b>何が起きたかを返す</b>（契約 ⑷ 4-3 の 4 檔のうち檔の 3 つを消す）。
    /// 台帳に居ない名（上流の走査で見えているだけの檔）は
    /// <c>WasKnown=false</c> で返る＝画面が「削除しました」と嘘をつかない。
    /// </summary>
    Services.Voices.VoiceRemoval RemoveVoiceDetailed(string voiceId);
}

/// <summary>
/// 契約 ⑺＝上流が読む別名表 <c>voices.json</c> の書き手。
/// <para>
/// 形＝<c>{"&lt;表示名&gt;":{"ref_wav":"&lt;ascii-id&gt;.wav"}}</c> ＋
/// <c>{"デフォルト":{"no_ref":true}}</c>。焼いた話者は
/// <c>{"&lt;表示名&gt;":{"ref_latent":"latents/&lt;stem&gt;.pt"}}</c> に<b>書き換わる</b>
/// （<c>ref_wav</c> は残さない＝契約 ⑷ 4-4）。パスは相対（アプリを移せる）。
/// </para>
/// <para>
/// 上流は毎要求 <c>iterdir()</c> なので<b>再起動は要らない</b>。書き換えは原子的に行う。
/// </para>
/// </summary>
public interface IVoicesJsonWriter
{
    /// <summary>台帳から <c>voices.json</c> の本文を作る（**純関数**＝テストで釘付けする）。</summary>
    string Render(VoicesYwkFile store);

    /// <summary>原子的に書く。</summary>
    void Write(string path, VoicesYwkFile store);
}

/// <summary>話者まわりの定数と、どこからでも要る 1 行の判定。</summary>
public static class VoiceIds
{
    /// <summary>参照なしの話者名（契約 ⑷ 4-2・裁定 16）。<b>一覧の先頭に常在する</b>。</summary>
    public const string Default = ServerEnvironment.DefaultVoiceId;

    /// <summary>
    /// 上流由来の別名（配布版は「デフォルト」に正規化して 200 を返す＝契約 ⑶ 3-1）。
    /// <b>一覧には出さない</b>（同じ意味の話者が 2 つ見える状態を作らない）。
    /// </summary>
    public static readonly string[] NoRefAliases = ["no-ref", "no_ref", "none", "null", "text-only"];

    /// <summary>
    /// 参照に使える拡張子（<b>配布する実行系が実際に読める形だけ</b>＝是正・2026-09-05）。
    /// <para>
    /// 裁定 30 で torchcodec を外したので復号は soundfile 一本である。組んだ実行系の実測
    /// （<c>build/out/runtime-rocm-gfx1151/python.exe</c>・libsndfile 1.2.2）＝読める形は
    /// <c>WAV</c>／<c>MP3</c>／<c>FLAC</c>／<c>OGG</c>（<c>VORBIS</c>・<c>OPUS</c> の両 subtype）で、
    /// <b><c>.m4a</c>・<c>.aac</c>・<c>.wma</c> は 1 つも読めない</b>。謳っていた 8 種のうち
    /// この 3 つは「追加は成功し、合成のときになって初めて落ちる」路だったので落とす。
    /// <c>.wma</c> は上流の <c>VOICE_EXTENSIONS</c> にも無い。
    /// </para>
    /// <para>
    /// 上流が走査で拾う <c>.webm</c> は soundfile が読めないので<b>足さない</b>。
    /// </para>
    /// </summary>
    public static readonly string[] WavExtensions =
        [".wav", ".mp3", ".flac", ".ogg", ".opus"];

    /// <summary>画面と檔窓に出す 1 行（<b>同じ定数から作る</b>＝文言と検分がずれない）。</summary>
    public static string ExtensionsText => string.Join("・", Array.ConvertAll(WavExtensions, e => e[1..]));

    /// <summary>参照 wav の推奨の長さ（上流の推奨＝設計書 §4）。</summary>
    public static readonly TimeSpan RecommendedRefMin = TimeSpan.FromSeconds(10);

    /// <summary>同上。</summary>
    public static readonly TimeSpan RecommendedRefMax = TimeSpan.FromSeconds(30);

    /// <summary>参照なしを指す名か（大小無視）。</summary>
    public static bool IsNoRef(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var text = id.Trim();
        if (string.Equals(text, Default, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var alias in NoRefAliases)
        {
            if (string.Equals(text, alias, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 一覧の並び（**純関数**）＝「デフォルト」が先頭・以下は設定の
    /// <see cref="LauncherSettings.VoiceOrder"/> 順・残りは表示名順。
    /// 上流の <c>sorted()</c> はコードポイント順で日本語が五十音にならないので配布版が並べ替える。
    /// </summary>
    public static IReadOnlyList<string> Order(
        IEnumerable<string> ids,
        IReadOnlyList<string>? preferred)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();

        void Add(string id)
        {
            if (seen.Add(id))
            {
                ordered.Add(id);
            }
        }

        var all = new List<string>();
        foreach (var id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                all.Add(id);
            }
        }

        if (all.Contains(Default, StringComparer.Ordinal))
        {
            Add(Default);
        }

        if (preferred is not null)
        {
            foreach (var id in preferred)
            {
                if (all.Contains(id, StringComparer.Ordinal))
                {
                    Add(id);
                }
            }
        }

        all.Sort(StringComparer.CurrentCulture);
        foreach (var id in all)
        {
            Add(id);
        }

        return ordered;
    }
}
