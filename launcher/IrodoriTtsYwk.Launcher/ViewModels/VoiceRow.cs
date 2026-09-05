using System;
using System.Collections.Generic;
using System.Linq;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>話者一覧の 1 行（<b>不変</b>＝一覧は作り直す）。</summary>
/// <param name="Id">上流の話者 id（＝表示名。日本語可）。</param>
/// <param name="DisplayName">画面に出す名。</param>
/// <param name="FileName">写した参照 wav の檔名（ASCII）。参照なしは null。</param>
/// <param name="IsPreset">同梱のプリセット（裁定 17）。</param>
/// <param name="IsNoRef">参照なし（「デフォルト」）。</param>
/// <param name="HasLatent">焼いた参照潜在から鳴る（裁定 65）。</param>
/// <param name="IsLatentStale">焼いてあるが元の wav が変わった／消えた。</param>
/// <param name="Caption">この話者の演技指示の既定。</param>
/// <param name="IsInTable">
/// 配布版の台帳（<c>voices.ywk.json</c>）がこの話者を知っているか。
/// <b>偽＝サーバ側の走査でだけ見えている檔</b>で、ランチャからは消せない（是正・2026-09-05）。
/// </param>
/// <param name="LatentBytes">
/// 焼いた <c>.pt</c> の<b>実サイズ</b>（<c>/ywk/status.memory.latents[id]</c>＝裁定 87 ⑴）。
/// サーバが居ない・まだ焼いていないなら null＝そのときだけ係数の概算を出す（裁定 67 ⑵・low 13）。
/// </param>
public sealed record VoiceRow(
    string Id,
    string DisplayName,
    string? FileName,
    bool IsPreset,
    bool IsNoRef,
    bool HasLatent,
    bool IsLatentStale,
    string? Caption,
    bool IsInTable = true,
    long? LatentBytes = null)
{
    /// <summary>種別の 1 語（プリセット／利用者／参照なし／サーバ側）。</summary>
    public string KindText => IsNoRef
        ? "参照なし"
        : !IsInTable ? "サーバ側" : IsPreset ? "プリセット" : "利用者";

    /// <summary>
    /// 潜在キャッシュの状態（裁定 65・67）。<b>焼き直しを促す印を落とさない</b>＝
    /// <c>latent_stale</c> は「効いているが古い」であって「効いていない」ではない。
    /// </summary>
    public string LatentText => IsNoRef
        ? UiText.Missing
        : IsLatentStale ? "要・焼き直し" : HasLatent ? "焼き済み" : "未";

    /// <summary>
    /// 消費メモリ（裁定 67 ⑵・low 13）＝<b>1 名あたり</b>。焼いてあれば実サイズ、無ければ概算。
    /// </summary>
    public string MemoryText => MemoryEstimate.Describe(IsNoRef, HasLatent, LatentBytes);

    /// <summary>この 1 名を載せたときのバイト数（焼いてあれば実サイズ）。</summary>
    public long MemoryBytes => MemoryEstimate.ForVoice(IsNoRef, HasLatent, LatentBytes);

    /// <summary>
    /// 消せる行か（「デフォルト」は常在＝契約 ⑷ 4-2）。
    /// <b>台帳が知らない行も消せない</b>（是正・2026-09-05）＝消しても何も起きないのに
    /// 「削除しました」と告げ、行も残ったままになるため。
    /// </summary>
    public bool CanRemove => !IsNoRef && IsInTable;

    /// <summary>
    /// 消せない理由 1 行（消せるなら null）。<b>UIA から読める形にする</b>ため、
    /// 画面はこの文言をボタンの <c>HelpText</c> と 1 行の表示の両方に出す（low 14）。
    /// </summary>
    public string? RemoveBlockedReason
    {
        get
        {
            if (CanRemove)
            {
                return null;
            }

            return IsNoRef
                ? "「" + DisplayName + "」は一覧に常在するので消せません。"
                : "「" + DisplayName + "」は配布版の台帳にありません（サーバ側の走査で見えている檔です）。";
        }
    }

    /// <summary>
    /// 試聴できる行か（<b><c>.wav</c> のときだけ</b>＝low 11）。
    /// <para>
    /// <c>NAudio.WinMM</c>／<c>NAudio.Core</c> には <c>Mp3FileReader</c>／<c>AudioFileReader</c> が
    /// 入っていないので、受ける 5 拡張子のうち<b>鳴らせるのは wav だけ</b>である（README §7-2 ⑷）。
    /// 「押せるのに必ず失敗する」形を作らず、押せない理由を
    /// <see cref="PreviewBlockedReason"/> で 1 行出す。
    /// </para>
    /// </summary>
    public bool CanPreview => !IsNoRef
        && !string.IsNullOrWhiteSpace(FileName)
        && FileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);

    /// <summary>試聴できない理由 1 行（できるなら null＝low 11）。</summary>
    public string? PreviewBlockedReason
    {
        get
        {
            if (CanPreview)
            {
                return null;
            }

            if (IsNoRef)
            {
                return "「" + DisplayName + "」は参照なしの話者なので試聴する音がありません。";
            }

            if (string.IsNullOrWhiteSpace(FileName))
            {
                return "「" + DisplayName + "」には配布版が写した参照音声がありません（サーバ側の檔です）。";
            }

            return "「" + DisplayName + "」の参照音声は "
                + System.IO.Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant()
                + " なので試聴できません（試聴は wav だけ。登録と合成には使えます）。";
        }
    }
}

/// <summary>
/// 話者一覧の 1 行を組む（<b>純関数</b>＝3 つの出所を 1 つの並びに畳む）。
/// <para>
/// 出所は⑴ <b>配布版の台帳</b> <c>voices.ywk.json</c>（表示名・檔名・preset・caption 既定）
/// ⑵ <b>走っている wrapper の <c>/ywk/voices</c></b>（<c>latent</c>／<c>latent_stale</c>＝
/// 焼いたかどうかは wrapper しか知らない）⑶ <b>設定の並び</b>（<see cref="LauncherSettings.VoiceOrder"/>）。
/// サーバが止まっていれば⑵は無く、台帳の <c>ref_latent</c> だけで組む（一覧は必ず出る）。
/// </para>
/// <para>
/// <b>「デフォルト」は必ず先頭に 1 件</b>（受け入れ条件 D-3）。台帳に無くても足す＝
/// wrapper が <c>allow_no_ref_voice=false</c>＋alias で常に持っているからである。
/// <b><c>none</c> 等の別名は出さない</b>（同じ意味の話者が 2 つ見える状態を作らない）。
/// </para>
/// </summary>
public static class VoiceRowBuilder
{
    /// <summary>台帳＋（あれば）<c>/ywk/voices</c>＋設定の並びから一覧を組む。</summary>
    /// <param name="store">配布版の台帳。</param>
    /// <param name="live">走っている wrapper の <c>/ywk/voices</c>（無ければ null）。</param>
    /// <param name="preferredOrder">設定の並び。</param>
    /// <param name="memory">
    /// <c>/ywk/status.memory</c>（裁定 87 ⑴）。<c>latents</c> から<b>焼いた <c>.pt</c> の実サイズ</b>を
    /// 話者ごとに引く（無ければ null＝係数の概算に落ちる）。
    /// </param>
    public static IReadOnlyList<VoiceRow> Build(
        VoicesYwkFile? store,
        VoicesResponse? live,
        IReadOnlyList<string>? preferredOrder,
        MemoryStatus? memory = null)
    {
        var entries = store?.Voices ?? new Dictionary<string, VoiceEntry>(StringComparer.Ordinal);

        var liveById = new Dictionary<string, VoiceInfo>(StringComparer.Ordinal);
        if (live is not null)
        {
            foreach (var info in live.Data)
            {
                if (!string.IsNullOrWhiteSpace(info.Id))
                {
                    liveById[info.Id] = info;
                }
            }
        }

        // 出す id ＝台帳と wrapper の合併から、参照なしの別名（none 等）を落とし、
        // 「デフォルト」を必ず 1 件だけ入れる。
        var ids = new HashSet<string>(StringComparer.Ordinal) { VoiceIds.Default };
        foreach (var id in entries.Keys.Concat(liveById.Keys))
        {
            if (!string.IsNullOrWhiteSpace(id) && !VoiceIds.IsNoRef(id))
            {
                ids.Add(id);
            }
        }

        var rows = new List<VoiceRow>(ids.Count);
        foreach (var id in VoiceIds.Order(ids, preferredOrder))
        {
            entries.TryGetValue(id, out var entry);
            liveById.TryGetValue(id, out var info);

            var noRef = VoiceIds.IsNoRef(id) || entry?.NoRef == true || info?.NoRef == true;

            // 焼いたかどうかは wrapper が正本。無ければ台帳の ref_latent で代用する。
            var hasLatent = info?.Latent ?? !string.IsNullOrWhiteSpace(entry?.RefLatent);
            var stale = info?.LatentStale == true;

            rows.Add(new VoiceRow(
                id,
                string.IsNullOrWhiteSpace(entry?.DisplayName)
                    ? (string.IsNullOrWhiteSpace(info?.DisplayName) ? id : info!.DisplayName!)
                    : entry!.DisplayName,
                noRef ? null : entry?.File,
                entry?.Preset ?? info?.Preset ?? false,
                noRef,
                !noRef && hasLatent,
                !noRef && stale,
                entry?.Caption,
                // 「デフォルト」は台帳に無くても常在する（裁定 16）＝台帳の物として扱う。
                entry is not null || VoiceIds.IsNoRef(id),
                noRef ? null : memory?.LatentBytesFor(id)));
        }

        return rows;
    }

    /// <summary>
    /// 焼き直しが要る話者（<c>latent_stale</c>）と、まだ焼いていない話者の id
    /// （<c>POST /ywk/voices/precompute</c> の <c>ids</c> に渡す＝純関数）。
    /// 参照なしは含めない（焼く物が無い）。
    /// </summary>
    public static IReadOnlyList<string> NeedsPrecompute(IEnumerable<VoiceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        // 台帳が知らない行（サーバ側の走査でだけ見える檔）は焼きに行かない。
        return rows
            .Where(static r => !r.IsNoRef && r.IsInTable && (!r.HasLatent || r.IsLatentStale))
            .Select(static r => r.Id)
            .ToArray();
    }
}
