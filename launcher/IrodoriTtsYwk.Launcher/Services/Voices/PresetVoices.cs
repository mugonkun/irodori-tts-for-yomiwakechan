using System;
using System.Collections.Generic;
using System.Globalization;
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
/// プリセット 1 名の<b>改名</b>（裁定 108＝配布側が <c>display_name</c> を変えた分を利用者の台帳へ引き継ぐ）。
/// </summary>
/// <param name="OldId">台帳に居た旧い話者 id（＝旧い表示名）。</param>
/// <param name="NewId">配布樹の <c>presets.json</c> が名乗る新しい話者 id。</param>
/// <param name="FileName">2 つを同じ 1 名と見なした根拠＝参照 wav の檔名。</param>
public sealed record PresetRename(string OldId, string NewId, string FileName);

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
        var fromPresets = DiscoverManifest(paths);
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
    /// <b>正本（⑴ <c>voices/presets.json</c>）から読めたときだけ</b>一覧を返す（読めなければ null）。
    /// <para>
    /// 改名の引き継ぎ（<see cref="MigrateRenamed"/>・裁定 108）が使う口である。<see cref="Discover"/>
    /// の ⑵⑶ は<b>混ぜてはいけない</b>＝⑶ は檔名の幹（<c>vv_mochiko_sexy</c>）を id にするので、
    /// 台帳の日本語の id が ASCII の幹へ「改名」されてしまう（配布樹に台帳が無い版で起きる）。
    /// </para>
    /// </summary>
    public static IReadOnlyList<PresetVoice>? DiscoverManifest(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return FromPresetsJson(
            paths.PresetsJsonPath, paths.PresetVoicesDir, Path.Combine(paths.AppDir, "voices"));
    }

    /// <summary>
    /// 初回展開を実行する（既に台帳が在れば何もしない）。戻り＝写した件数。
    /// <para>
    /// <b>「台帳は在るがプリセットが 1 件も無い」も初回と見なす</b>（是正・2026-09-05）。
    /// <c>presets.json</c> を読めていなかったころに作られた台帳（プリセット 0 件）を持つ
    /// 利用者へ直した版を配っても、<see cref="File.Exists(string)"/> の判定だけでは
    /// 12 名が永久に入らない。
    /// </para>
    /// <para>
    /// <b>後の版で増えた 1 名はここでは入らない</b>（印が立った台帳には 0 を返す）＝
    /// それは <see cref="InstallNew"/> の持ち場である（裁定 121）。起動席は 2 本とも呼ぶ。
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
    /// <b>後の版で増えたプリセットだけ</b>を足す（裁定 121）。戻り＝足した話者 id（順は正本の順）。
    /// <para>
    /// <b>要る理由</b>＝<see cref="InstallIfFirstRun"/> は <c>presets_installed</c> の印が立った
    /// 台帳に 0 を返す（利用者が消した 1 名を毎起動書き戻さないため）ので、<b>配布側が
    /// 後から足した 1 名</b>も永久に入らない。実射＝v1.0.1 で足した
    /// 「シャンパンコール（ホスクラ）」が、v1.0.0 から使っている台帳の一覧に出てこなかった
    /// （司令官の報告・2026-09-10＝「話者一覧にシャンパンコールがないね。」）。
    /// </para>
    /// <para>
    /// <b>「消した」と「まだ差し出していない」を分ける</b>のが
    /// <see cref="VoicesYwkFile.PresetsInstalledIds"/> である＝
    /// ⑴ 欄が <c>null</c>（≦ v1.0.1 が書いた台帳）なら<b>いま台帳に居るプリセット</b>で種を蒔く
    /// ⑵ 足すのは「正本（<see cref="DiscoverManifest"/>・<c>status: done</c>）に居て、
    /// 種にも台帳にも居ない id」だけ ⑶ 足した id は欄に書き足す。
    /// 欄に在って台帳に居ない id＝<b>利用者が消した</b>ので二度と戻さない
    /// （戻す口は「同梱のプリセットを入れ直す」＝<see cref="Restore"/> のまま）。
    /// </para>
    /// <para>
    /// <b>1 度だけの副作用</b>＝⑴ の種蒔きは「消した」を知らないので、
    /// ≦ v1.0.1 の台帳で<b>プリセットを消してあった</b>利用者には、その 1 名が
    /// v1.0.2 の初回起動で 1 度だけ戻る（次の起動からは戻らない）。
    /// </para>
    /// <para>
    /// <b>何も変わらなければ台帳は書かない</b>（2 回目以降の起動は 1 バイトも触らない）＝
    /// ただし<b>足した話者が 0 名でも、記録した id が増えた回は書く</b>（是正・検分）。
    /// 書かずに捨てると、この回に覚えた id を次の起動が忘れ、利用者がその 1 名を消したときに戻ってしまう。
    /// 正本が読めない配布樹では何もしない（<see cref="Discover"/> の ⑵⑶ は混ぜない＝
    /// 檔名の幹が話者 id になって別人が増える）。
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> InstallNew(AppPaths paths, VoiceStore store)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(store);

        if (!File.Exists(store.TablePath))
        {
            return []; // 台帳がまだ無い＝初回展開（InstallIfFirstRun）の持ち場
        }

        var table = store.Load();
        if (!table.PresetsInstalled)
        {
            return []; // 印の無い台帳も初回展開の持ち場（そちらが全員を入れて id を記帳する）
        }

        var manifest = DiscoverManifest(paths);
        if (manifest is not { Count: > 0 })
        {
            return [];
        }

        var seeded = table.PresetsInstalledIds is null;
        var offered = new HashSet<string>(
            seeded ? PresetIdsIn(table.Voices) : table.PresetsInstalledIds!, StringComparer.Ordinal);

        // 記帳した時点の件数を控える（是正・検分）＝下の「何も変わっていない」の判定は
        // <b>足した話者の数だけでは足りない</b>。既に台帳に居るのに欄がまだ知らない id
        // （改名で新しい名が入った回など）をこの回に覚えても、書かずに捨てていた＝
        // その 1 名を利用者が消した次の起動で「まだ差し出していない」と読んで戻してしまう。
        var recorded = offered.Count;

        var voices = new Dictionary<string, VoiceEntry>(table.Voices, StringComparer.Ordinal);
        var added = new List<string>();

        foreach (var preset in manifest)
        {
            if (offered.Contains(preset.Id) || voices.ContainsKey(preset.Id))
            {
                offered.Add(preset.Id); // 既に居る＝差し出した物として覚える
                continue;
            }

            if (!CopyReference(store, preset))
            {
                continue; // 1 名の失敗で残り全部を落とさない（次の起動でもう 1 度試す）
            }

            voices[preset.Id] = NewEntry(preset);
            offered.Add(preset.Id);
            added.Add(preset.Id);
        }

        if (!seeded && added.Count == 0 && offered.Count == recorded)
        {
            return []; // 変わっていない＝台帳は書かない（記帳した id も増えていない）
        }

        store.Save(table with { Voices = voices, PresetsInstalledIds = Ordered(offered) });
        return added;
    }

    /// <summary>足した話者を告げる 1 行（<b>純関数</b>＝0 名なら null）。</summary>
    public static string? AddedLine(IReadOnlyList<string> added)
    {
        ArgumentNullException.ThrowIfNull(added);
        return added.Count == 0
            ? null
            : "同梱の話者を " + added.Count.ToString(CultureInfo.InvariantCulture)
              + " 名足しました：" + string.Join("・", added);
    }

    /// <summary>
    /// 台帳に居るプリセット（<c>preset</c> か <c>origin=preset</c>）の id（<b>純関数</b>）。
    /// 「デフォルト」は<b>数えない</b>＝正本に居ない常在の 1 名（裁定 16）で、写す檔も無い。
    /// </summary>
    private static IEnumerable<string> PresetIdsIn(IReadOnlyDictionary<string, VoiceEntry> voices)
    {
        foreach (var (id, entry) in voices)
        {
            if (!string.IsNullOrWhiteSpace(id)
                && !string.Equals(id, VoiceIds.Default, StringComparison.Ordinal)
                && (entry.Preset || string.Equals(entry.Origin, "preset", StringComparison.Ordinal)))
            {
                yield return id;
            }
        }
    }

    /// <summary>
    /// 差し出した記録を改名で引き継ぐ（<b>純関数</b>＝欄が null なら null のまま）。
    /// </summary>
    private static IReadOnlyList<string>? RenamedIds(
        IReadOnlyList<string>? ids, IReadOnlyList<PresetRename> renames)
    {
        if (ids is null || ids.Count == 0 || renames.Count == 0)
        {
            return ids;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rename in renames)
        {
            map[rename.OldId] = rename.NewId;
        }

        var moved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            moved.Add(map.TryGetValue(id, out var renamed) ? renamed : id);
        }

        return Ordered(moved);
    }

    /// <summary>記帳の並びを 1 つに決める（序数＝台帳の差分が読める）。</summary>
    private static IReadOnlyList<string> Ordered(IEnumerable<string> ids)
    {
        var list = new List<string>(ids);
        list.Sort(StringComparer.Ordinal);
        return list;
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

    /// <summary>
    /// 配布側の改名を利用者の台帳へ引き継ぐ（裁定 108＝2026-09-07 の
    /// 「もち子さん」→「もち子さん（セクシー／あん子）」が最初の 1 件）。戻り＝改めた名の一覧。
    /// <para>
    /// <b>要る理由</b>＝表示名がそのまま話者 id である（裁定 17・契約 ⑷ 4-1）ので、配布樹の
    /// <c>presets.json</c> だけを直しても既存の利用者の台帳は旧い id を持ち続ける。
    /// <see cref="InstallIfFirstRun"/> は <c>presets_installed</c> の印で 0 件を返し、
    /// <see cref="Restore"/> は<b>旧い id の隣に新しい id を足す</b>＝同じ wav を指す 2 名になる。
    /// </para>
    /// <para>
    /// <b>組にする規則</b>＝⑴ 正本（<see cref="DiscoverManifest"/>）から読めた行だけを相手にする
    /// ⑵ 台帳の側はプリセット（<c>preset</c> か <c>origin=preset</c>）で檔名を持ち、その id が
    /// 正本に無い行 ⑶ 檔名が一致する正本の行が 1 つ在り、その id が台帳にまだ無い。
    /// 同じ檔を指す台帳の行が 2 つ在れば<b>先頭の 1 つだけ</b>を改める（残りはそのまま）。
    /// </para>
    /// <para>
    /// <b>持ち越す物</b>＝檔・caption・既定パラメータ・<c>preset</c>・<c>origin</c>・<c>added_at</c>。
    /// <b>捨てる物</b>＝<c>ref_latent</c>（幹は話者 id 由来なので旧い潜在は新しい id では当たらない）と
    /// 旧い id の <c>.pt</c>／sidecar（<see cref="VoiceStore.DeleteLatentFiles"/>）。
    /// <b>参照 wav は消さない</b>（新しい名の行が同じ檔を指している）。焼き直しは wrapper の
    /// 起動時の事前計算がやる（裁定 78 ⑴）。
    /// </para>
    /// </summary>
    public static IReadOnlyList<PresetRename> MigrateRenamed(AppPaths paths, VoiceStore store)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(store);

        var manifest = DiscoverManifest(paths);
        if (manifest is not { Count: > 0 })
        {
            return []; // 正本が読めない＝改名の判断材料が無い（⑵⑶ の id で改名してはいけない）
        }

        if (!File.Exists(store.TablePath))
        {
            return []; // 台帳がまだ無い＝初回展開が新しい名で入れる
        }

        var table = store.Load();
        var renames = PlanRenames(table.Voices, manifest);
        if (renames.Count == 0)
        {
            return [];
        }

        var voices = new Dictionary<string, VoiceEntry>(table.Voices, StringComparer.Ordinal);
        var stale = new List<(string OldId, string? RefLatent)>(renames.Count);
        foreach (var rename in renames)
        {
            if (!voices.TryGetValue(rename.OldId, out var entry))
            {
                continue;
            }

            voices.Remove(rename.OldId);
            voices[rename.NewId] = entry with { DisplayName = rename.NewId, RefLatent = null };
            stale.Add((rename.OldId, entry.RefLatent));
        }

        // 台帳を先に確定させてから潜在を消す（是正・検分）。逆順にすると Save が投げた回に
        // 「旧い id の行と ref_latent は残っているのに .pt だけ無い」個体ができ、wrapper の
        // 事前計算は ref_embed を持つ行を飛ばす（server/ywk_server.py:3230-3246 の
        // precompute_targets）ので焼き直されず、その話者は二度と鳴らない。この順なら
        // 落ちても消し損ねの .pt が残るだけ＝害の小さいほうへ倒す（裁定 78 ⑴）。
        // **差し出した記録も一緒に改める**（是正・検分）＝旧い id を残したままにすると、
        // 欄は新しい名を知らないので「まだ差し出していない」と読み、利用者がその 1 名を
        // 消した次の起動で戻ってしまう（裁定 121 の不変＝欄に在って台帳に居ない id は戻さない）。
        store.Save(table with
        {
            Voices = voices,
            PresetsInstalledIds = RenamedIds(table.PresetsInstalledIds, renames),
        });
        foreach (var (oldId, refLatent) in stale)
        {
            store.DeleteLatentFiles(oldId, refLatent);
        }

        return renames;
    }

    /// <summary>
    /// 改名の組を決める（<b>純関数</b>＝檔に触らないので単体で試せる・裁定 108）。
    /// 規則は <see cref="MigrateRenamed"/> の通り。戻りは台帳の鍵の順（序数）。
    /// </summary>
    /// <param name="voices">いまの台帳（鍵＝話者 id）。</param>
    /// <param name="manifest">正本から読めたプリセット（<see cref="DiscoverManifest"/> の戻り）。</param>
    public static IReadOnlyList<PresetRename> PlanRenames(
        IReadOnlyDictionary<string, VoiceEntry> voices, IReadOnlyList<PresetVoice> manifest)
    {
        ArgumentNullException.ThrowIfNull(voices);
        ArgumentNullException.ThrowIfNull(manifest);

        var manifestIds = new HashSet<string>(StringComparer.Ordinal);
        var byFile = new Dictionary<string, PresetVoice>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in manifest)
        {
            manifestIds.Add(preset.Id);
            if (!byFile.ContainsKey(preset.FileName))
            {
                byFile[preset.FileName] = preset;
            }
        }

        var keys = new List<string>(voices.Keys);
        keys.Sort(StringComparer.Ordinal); // 同じ檔を 2 名が指しているときに結果を 1 つに決める
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var renames = new List<PresetRename>();

        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key)
                || string.Equals(key, VoiceIds.Default, StringComparison.Ordinal)
                || manifestIds.Contains(key))
            {
                // 「デフォルト」と、既に新しい名の行は触らない。空白だけの鍵（壊れた台帳）も
                // 相手にしない＝<see cref="VoiceStore.DeleteLatentFiles"/> が投げる＝起動が止まる。
                continue;
            }

            var entry = voices[key];
            if (!entry.Preset && !string.Equals(entry.Origin, "preset", StringComparison.Ordinal))
            {
                continue; // 利用者が足した話者は、同じ檔を指していても改名しない
            }

            var file = entry.File;
            if (string.IsNullOrWhiteSpace(file)
                || !byFile.TryGetValue(file, out var preset)
                || voices.ContainsKey(preset.Id)
                || !claimed.Add(preset.Id))
            {
                continue; // 檔が無い・正本に居ない・新しい名が既に居る／この回で埋まった
            }

            renames.Add(new PresetRename(key, preset.Id, file));
        }

        return renames;
    }

    /// <summary>
    /// 設定に残った旧い話者 id を改める（<b>純関数</b>＝裁定 108）。戻り＝1 つでも書き換えたか。
    /// <para>
    /// 直すのは 3 箇所＝<see cref="LauncherSettings.VoiceOrder"/>（並び）・
    /// <see cref="LauncherSettings.WarmupVoices"/>（暖機の名指し）・
    /// <see cref="LauncherSettings.LastTestVoice"/>（試し撃ちの選び）。
    /// 並びと試し撃ちは知らない id を黙って捨てるだけだが、<b>暖機は落ちる</b>＝
    /// <c>server/ywk_server.py:2127-2151</c> の <c>_fire_warmup_shot</c> が
    /// <c>upstream._resolve_voice</c> で投げ、<c>_warmup_run_shots</c> がその走行を
    /// <c>failed</c> にして<b>残りの射を全部落とす</b>（契約 ⑺ 7-2）。
    /// </para>
    /// </summary>
    public static bool RenameInSettings(LauncherSettings settings, IReadOnlyList<PresetRename> renames)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(renames);
        if (renames.Count == 0)
        {
            return false;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rename in renames)
        {
            map[rename.OldId] = rename.NewId;
        }

        var changed = false;
        changed |= RenameInList(settings.VoiceOrder, map);
        changed |= RenameInList(settings.WarmupVoices, map);

        if (settings.LastTestVoice is string last && map.TryGetValue(last, out var renamed))
        {
            settings.LastTestVoice = renamed;
            changed = true;
        }

        return changed;
    }

    private static bool RenameInList(IList<string> ids, IReadOnlyDictionary<string, string> map)
    {
        var changed = false;
        for (var i = 0; i < ids.Count; i++)
        {
            if (map.TryGetValue(ids[i], out var renamed))
            {
                ids[i] = renamed;
                changed = true;
            }
        }

        return changed;
    }

    private static int Install(
        AppPaths paths, VoiceStore store, VoicesYwkFile table, bool skipExisting = false)
    {
        var presets = Discover(paths);
        var voices = new Dictionary<string, VoiceEntry>(
            VoiceStore.EnsureDefault(table).Voices, StringComparer.Ordinal);
        var copied = 0;

        // **差し出した id を記帳する**（裁定 121）＝この後の版で増えたプリセットだけを
        // 足せるようにする（<see cref="InstallNew"/>）。写せなかった 1 名は記帳しない
        // （次の起動でもう 1 度差し出す）。
        var offered = new HashSet<string>(
            table.PresetsInstalledIds ?? [], StringComparer.Ordinal);

        Directory.CreateDirectory(store.ReferencesDir);
        foreach (var preset in presets)
        {
            if (skipExisting && voices.ContainsKey(preset.Id))
            {
                offered.Add(preset.Id);
                continue;
            }

            if (!CopyReference(store, preset))
            {
                continue; // 1 名の失敗で残り全部を落とさない
            }

            voices[preset.Id] = NewEntry(preset);
            offered.Add(preset.Id);
            copied++;
        }

        // **印は「本当に見つけた」ときだけ立てる**。配布樹にプリセットが 1 檔も無い版
        // （build/assemble-app.ps1 がまだ voices/presets/ を写していない＝便 A へ票）で
        // 印を立ててしまうと、檔が入った版に更新しても 12 名が永久に入らない。
        var installed = table.PresetsInstalled || presets.Count > 0;
        store.Save(table with
        {
            Voices = voices,
            PresetsInstalled = installed,
            PresetsInstalledIds = offered.Count == 0 && table.PresetsInstalledIds is null
                ? null
                : Ordered(offered),
        });
        return copied;
    }

    /// <summary>参照 wav を利用者データへ写す（既に在れば触らない）。戻り＝置けたか。</summary>
    private static bool CopyReference(VoiceStore store, PresetVoice preset)
    {
        Directory.CreateDirectory(store.ReferencesDir);
        var destination = Path.Combine(store.ReferencesDir, preset.FileName);
        try
        {
            if (!File.Exists(destination))
            {
                File.Copy(preset.SourcePath, destination, overwrite: false);
            }

            return true;
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

    /// <summary>台帳に足す 1 行（プリセットの姿＝<b>純関数</b>）。</summary>
    private static VoiceEntry NewEntry(PresetVoice preset) => new()
    {
        DisplayName = preset.Id,
        File = preset.FileName,
        Caption = preset.Caption,
        Preset = true,
        NoRef = false,
        Origin = "preset",
        AddedAt = DateTimeOffset.Now,
    };

    /// <summary>
    /// <c>voices/presets.json</c> の実物（<b>トップレベルの鍵は <c>presets</c>・値は配列</b>）を読む。
    /// <para>
    /// <b>話者 id は <c>display_name</c></b>（裁定 17・契約 ⑷ 4-1＝一覧は
    /// <c>id</c> と <c>display_name</c> に同じ字を出す）＝日本語の名で一覧に出る。
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
