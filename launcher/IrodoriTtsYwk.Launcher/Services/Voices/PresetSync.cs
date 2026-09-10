using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Voices;

/// <summary>同梱の声 1 名について、この回にする事（<c>v2-spec.md</c> §11-2 の 5 枝）。</summary>
public enum PresetSyncAction
{
    /// <summary>枝 d／飛ばす行＝何もしない。</summary>
    None,

    /// <summary>枝 a＝利用者データに wav が無い＝<b>写す</b>。</summary>
    Copy,

    /// <summary>枝 b＝利用者は置いたまま触っていない＝<b>上書きする</b>。</summary>
    Overwrite,

    /// <summary>枝 c＝利用者が差し替えた＝<b>触らない</b>（1 行だけ告げる）。</summary>
    Keep,

    /// <summary>枝 e＝もう新しいのに <c>preset_md5</c> が無い＝<b>欄を書き足すだけ</b>。</summary>
    Record,
}

/// <summary>同梱の声 1 名ぶんの結論。</summary>
/// <param name="Id">話者 id（＝表示名）。</param>
/// <param name="FileName"><c>voices\refs\</c> に置く檔名。</param>
/// <param name="SourcePath">配布樹の実体。</param>
/// <param name="Action">する事。</param>
/// <param name="SourceMd5">配布側の <c>secondary.md5</c>（台帳に記録する値）。</param>
public sealed record PresetSyncItem(
    string Id, string FileName, string SourcePath, PresetSyncAction Action, string? SourceMd5);

/// <summary>差分の計画（<b>純関数の産物</b>＝檔は 1 バイトも動いていない）。</summary>
public sealed record PresetSyncPlan(IReadOnlyList<PresetSyncItem> Items)
{
    /// <summary>何もしない結末。</summary>
    public static readonly PresetSyncPlan Empty = new([]);

    /// <summary>枝 a＝写す。</summary>
    public IReadOnlyList<PresetSyncItem> Copies => Where(PresetSyncAction.Copy);

    /// <summary>枝 b＝上書きする（E3 の「n 名」はこの数）。</summary>
    public IReadOnlyList<PresetSyncItem> Overwrites => Where(PresetSyncAction.Overwrite);

    /// <summary>枝 c＝利用者が差し替えたので触らない。</summary>
    public IReadOnlyList<PresetSyncItem> Kept => Where(PresetSyncAction.Keep);

    /// <summary>枝 e＝欄を書き足すだけ。</summary>
    public IReadOnlyList<PresetSyncItem> Records => Where(PresetSyncAction.Record);

    /// <summary>檔か台帳のどちらかが動くか。</summary>
    public bool HasWork =>
        Copies.Count > 0 || Overwrites.Count > 0 || Records.Count > 0;

    private IReadOnlyList<PresetSyncItem> Where(PresetSyncAction action)
    {
        var found = new List<PresetSyncItem>();
        foreach (var item in Items)
        {
            if (item.Action == action)
            {
                found.Add(item);
            }
        }

        return found;
    }
}

/// <summary>
/// 差分を当てた結果（<see cref="PresetVoices.SyncContents"/> の戻り＝画面に出す材料）。
/// </summary>
/// <param name="Updated">
/// <b>写し直した</b>同梱の声（E3 の「n 名」）。<c>ref_latent</c> は落として <c>.pt</c> も消してある。
/// </param>
/// <param name="Kept">
/// <b>利用者が差し替えていたので触らなかった</b>声（枝 c＝取り込みのあと 1 行で告げる）。
/// </param>
public sealed record PresetSyncResult(IReadOnlyList<string> Updated, IReadOnlyList<string> Kept)
{
    /// <summary>何も起きなかった結末。</summary>
    public static readonly PresetSyncResult Empty = new([], []);

    /// <summary>1 名でも動いたか。</summary>
    public bool Any => Updated.Count > 0 || Kept.Count > 0;
}

/// <summary>
/// <b>同梱の声の中身の差分</b>（<c>v2-spec.md</c> §11-2・<c>decisions.md</c> 115 の穴）。
/// <para>
/// <b>穴の実物</b>＝<see cref="PresetVoices"/> の写しは「檔が在れば 1 バイトも書かない」ので、
/// 配布側が wav を録り直した版（<c>decisions.md</c> 114 の <c>calm_20s</c> 差し替えのような回）でも
/// 利用者のデータ樹には<b>古い音のまま</b>残る。<c>InstallIfFirstRun</c> は印が立った台帳に 0 を返し、
/// <c>InstallNew</c> は「id が増えた」回しか見ず、<c>Restore</c> も既存を飛ばす＝
/// <b>中身の更新だけが誰の持ち場でもなかった</b>。
/// </para>
/// <para>
/// <b>「利用者が変えたか」は md5 の一致だけで決める</b>＝更新時刻もサイズも使わない
/// （写しは時刻を持ち越さないし、同じ長さの差し替えを見逃す）。<c>size_bytes</c> は
/// md5 を計る前の足切りにだけ使う。
/// </para>
/// <para>
/// <b>利用者が足した声には絶対に触らない</b>＝<c>origin=preset</c>（か <c>preset</c>）の行だけを
/// 相手にし、<c>file</c> が <c>refs\</c> の外を指す行は飛ばす。
/// </para>
/// </summary>
public static class PresetSync
{
    /// <summary>
    /// 実測の代わりに置ける<b>「配布側とは違う」だけが判っている</b>印（16 進 32 桁にならない綴り＝
    /// どの md5 とも一致しない）。長さの足切りで md5 を計らずに済ませた回に使う。
    /// </summary>
    public const string Different = "different";

    /// <summary>
    /// 差分を決める（<b>純関数</b>＝檔を触らない）。
    /// <list type="table">
    /// <item><term>a</term><description>利用者データに wav が無い＝写す。</description></item>
    /// <item><term>b</term><description>実測 ＝ <c>preset_md5</c> かつ配布側と違う＝上書きする。</description></item>
    /// <item><term>c</term><description>実測 ≠ <c>preset_md5</c>＝触らない（利用者が差し替えた）。</description></item>
    /// <item><term>d</term><description>実測 ＝ 配布側＝何もしない（もう新しい）。</description></item>
    /// <item><term>e</term><description><c>preset_md5</c> が無い（v1.x の台帳）＝触らない。
    /// ただし実測 ＝ 配布側なら欄を書き足すだけ（次の版から b が効く）。</description></item>
    /// </list>
    /// </summary>
    /// <param name="manifest">正本（<see cref="PresetVoices.DiscoverManifest"/> の戻り）。</param>
    /// <param name="table">いまの利用者の台帳。</param>
    /// <param name="measuredMd5">
    /// その話者の<b>いま置いてある wav</b> の md5。
    /// <b>null は「檔が無い」だけを意味する</b>（＝枝 a＝写す）。<b>在るのに読めなかった回は
    /// <see cref="Different"/> を返すこと</b>＝どの md5 とも一致しない綴りなので枝 c（触らない）に落ちる。
    /// 掴まれているだけの wav を「無い」と読ませると、利用者が差し替えた録音を上書きで失う。
    /// <b>台帳の <c>preset_md5</c> が配布側と同じ行では呼ばない</b>＝
    /// 2 度目からは文字列比較だけで済み、12 檔（35 MB 級）を読まずに終わる。
    /// </param>
    public static PresetSyncPlan Plan(
        IReadOnlyList<PresetVoice> manifest,
        VoicesYwkFile table,
        Func<string, string?> measuredMd5)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(measuredMd5);

        var items = new List<PresetSyncItem>();
        foreach (var preset in manifest)
        {
            items.Add(PlanOne(preset, table, measuredMd5));
        }

        return new PresetSyncPlan(items);
    }

    private static PresetSyncItem PlanOne(
        PresetVoice preset, VoicesYwkFile table, Func<string, string?> measuredMd5)
    {
        PresetSyncItem Nothing() =>
            new(preset.Id, preset.FileName, preset.SourcePath, PresetSyncAction.None, preset.SourceMd5);

        // **消したプリセットは戻さない**（裁定 121 の規律）＝台帳に居ない id は差分でも触らない。
        if (!table.Voices.TryGetValue(preset.Id, out var entry))
        {
            return Nothing();
        }

        // **利用者が足した声は別の id になる**＝origin=preset の行だけを相手にする。
        if (!entry.Preset && !string.Equals(entry.Origin, "preset", StringComparison.Ordinal))
        {
            return Nothing();
        }

        // **refs\ の外を指す行は飛ばす**（利用者の檔を上書きする事故は取り返しがつかない）。
        if (!IsInsideReferences(entry.File))
        {
            return Nothing();
        }

        // **正本の側の檔名も同じ関門を通す**（是正・2026-09-11）＝手で書いた／古い形の
        // `presets.json` の `secondary.file` が `..\..\x.wav` のような路を名乗った回に、
        // 下の `destination` が refs\ の外へ抜けるのを止める。
        if (!IsInsideReferences(preset.FileName))
        {
            return Nothing();
        }

        // **上書きの行き先は「md5 を測った檔」そのもの**（是正・2026-09-11）。
        // 実測は `refs\<entry.File>` を測るのに、写しの行き先を `refs\<preset.FileName>` にすると、
        // 配布側が `secondary.file` だけ改名した版で **別の檔（＝他の話者の参照 wav）を潰す**うえ、
        // 当の行は古い wav を指したまま `preset_md5` だけ新しくなる＝枝 d に落ちて二度と見直されない。
        var destination = entry.File!.Trim();

        // 配布側が md5 を名乗らない台帳（古い presets.json・檔名だけで拾った樹）＝判らないので黙る。
        var source = Normalize(preset.SourceMd5);
        if (source is null)
        {
            return Nothing();
        }

        // 2 度目からはここで終わる＝台帳の記録と配布側が同じなら実測を撃たない（枝 d）。
        var recorded = Normalize(entry.PresetMd5);
        if (recorded is not null && string.Equals(recorded, source, StringComparison.Ordinal))
        {
            return Nothing();
        }

        // **枝 a は「檔が無い」ときだけ**（是正・2026-09-11）＝呼び手は
        // 「在るが読めなかった」を <see cref="Different"/> で名乗る約束になっている（16 進 32 桁に
        // ならない綴りなので、どの md5 とも一致しない＝下で枝 c に落ちる）。掴まれているだけの wav を
        // 「無い」と読んで上書きすると、利用者が差し替えた録音を失う＝§11-2 の枝 c が守っている当の事故。
        var measured = Normalize(measuredMd5(preset.Id));
        if (measured is null)
        {
            // 枝 a＝檔が無い（利用者が消した wav・移送で落ちた檔）＝写す。
            return new PresetSyncItem(
                preset.Id, destination, preset.SourcePath, PresetSyncAction.Copy, source);
        }

        if (string.Equals(measured, source, StringComparison.Ordinal))
        {
            // 枝 d／e＝もう新しい。記録が食い違っていれば欄だけ直す。
            return recorded is null || !string.Equals(recorded, source, StringComparison.Ordinal)
                ? new PresetSyncItem(
                    preset.Id, destination, preset.SourcePath, PresetSyncAction.Record, source)
                : Nothing();
        }

        if (recorded is null)
        {
            return Nothing(); // 枝 e＝v1.x の台帳で中身も違う＝判らないので触らない
        }

        return string.Equals(recorded, measured, StringComparison.Ordinal)
            ? new PresetSyncItem(
                preset.Id, destination, preset.SourcePath, PresetSyncAction.Overwrite, source) // 枝 b
            : new PresetSyncItem(
                preset.Id, destination, preset.SourcePath, PresetSyncAction.Keep, source);     // 枝 c
    }

    /// <summary>
    /// <c>file</c> が <c>refs\</c> の中の 1 檔を指しているか（<b>純関数</b>）。
    /// 路の区切り・<c>..</c>・絶対路はすべて偽＝<b>置き場の外には手を出さない</b>。
    /// </summary>
    public static bool IsInsideReferences(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return false;
        }

        var name = file.Trim();
        return name.IndexOf('/') < 0
               && name.IndexOf('\\') < 0
               && !name.Contains("..", StringComparison.Ordinal)
               && !Path.IsPathRooted(name)
               && name.IndexOf(':') < 0;
    }

    /// <summary>
    /// 檔の md5（16 進小文字・読めなければ null）。
    /// <para>
    /// <b>用途は完全性の照合であって暗号ではない</b>＝<c>voices/presets.json</c> の
    /// <c>secondary.md5</c> は <c>tools/make_presets.py</c> が複写後に実檔から測った値で、
    /// <c>N:</c> の写しとの突合にも同じ md5 を使っている＝<b>既に台帳の一部として運用されている値</b>である。
    /// 新しく測り直す仕掛けは要らない（<c>v2-spec.md</c> §11-2）。
    /// </para>
    /// </summary>
    public static string? Md5OfFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
#pragma warning disable CA5351 // 完全性の照合（配布側の md5 との突合）であって暗号用途ではない
            return Convert.ToHexStringLower(MD5.HashData(stream));
#pragma warning restore CA5351
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 檔の長さが台帳と違えば md5 を<b>計らずに</b>「違う」と決める足切り（<b>純関数</b>）。
    /// <paramref name="expectedSize"/> が null／0 なら足切りしない。
    /// </summary>
    public static bool LengthCouldMatch(string path, long? expectedSize)
    {
        if (expectedSize is not > 0)
        {
            return true;
        }

        try
        {
            return File.Exists(path) && new FileInfo(path).Length == expectedSize.Value;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>E3 の理由の 1 行（<b>純関数</b>＝0 名なら null）。</summary>
    public static string? UpdatedReason(int count) => count <= 0
        ? null
        : "新しい版で録り直された声があります（" + count.ToString(CultureInfo.InvariantCulture) + " 名）。";

    /// <summary>枝 c を告げる 1 行（<b>純関数</b>＝逐語・0 名なら null）。</summary>
    public static string? KeptLine(int count) => count <= 0
        ? null
        : "あなたが差し替えた声はそのままにしました（" + count.ToString(CultureInfo.InvariantCulture) + " 名）。";

    /// <summary>取り込んだあとの 1 行（<b>純関数</b>＝0 名なら null）。</summary>
    public static string? UpdatedLine(int count) => count <= 0
        ? null
        : "最初から入っている声を " + count.ToString(CultureInfo.InvariantCulture) + " 名、新しくしました。";

    private static string? Normalize(string? md5) =>
        string.IsNullOrWhiteSpace(md5) ? null : md5.Trim().ToLowerInvariant();
}
