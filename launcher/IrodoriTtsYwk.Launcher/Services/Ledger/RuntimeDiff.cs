using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Ledger;

/// <summary>
/// 動かすための一式の差分（<b>純関数の産物</b>＝<c>v2-spec.md</c> §11-3）。
/// </summary>
/// <param name="Added">新にだけ在る item（落として入れる）。</param>
/// <param name="Changed">同じ名で <c>sha256</c> が違う item（<b>その 1 檔だけ</b>入れ直す）。</param>
/// <param name="Removed">
/// 旧にだけ在る item の名（<b>消さない</b>＝<c>LedgerItem</c> は「その item が展開先へ書いた檔の
/// 一覧」を持たないので、site-packages から wheel 1 本ぶんを安全に抜く手が無い）。ログに 1 行残す。
/// </param>
/// <param name="Unverifiable">
/// 落とす対象なのに <c>sha256</c> が無い item の名（<b>取らない</b>＝
/// <see cref="FetchPlanner.RejectItemsWithoutSha256"/> の規則を先に守る）。
/// </param>
/// <param name="RebuildAll">差分をやめて<b>丸ごと組み直す</b>か。</param>
/// <param name="RebuildReason">丸ごとへ落とした理由（差分で足りるなら null）。</param>
public sealed record RuntimeDiffPlan(
    IReadOnlyList<LedgerItem> Added,
    IReadOnlyList<LedgerItem> Changed,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Unverifiable,
    bool RebuildAll,
    string? RebuildReason)
{
    /// <summary>落とす item（新の並び順＝台帳の順を保つ）。丸ごとの回は空。</summary>
    public IReadOnlyList<LedgerItem> Fetch => RebuildAll ? [] : [.. Added, .. Changed];

    /// <summary>落とすバイト（押す前に量を告げるため＝§11-3）。</summary>
    public long Bytes => Fetch.Sum(i => i.Size ?? 0);

    /// <summary>差分で済むか（1 檔でも落とす物が在る）。</summary>
    public bool Any => Fetch.Count > 0;

    /// <summary>差分も丸ごとも要らない（もう合っている）。</summary>
    public bool UpToDate => !RebuildAll && Fetch.Count == 0;
}

/// <summary>
/// <b>展開に使った台帳どうしを突き合わせる</b>（<c>v2-spec.md</c> §11-3）。
/// <para>
/// <b>いま在る道</b>＝<see cref="RuntimeStamp.Compare"/> は台帳の sha256 が違えば
/// 「実行系を組み直す」1 手を出す。<b>判定は既に在る。穴は「その 1 手が丸ごとしか無い」こと</b>＝
/// 4 GB 級の再展開で、しかも <c>decisions.md</c> 90 で取得キャッシュは空なので実際は
/// 数 GiB の再ダウンロードになる。ここが「変わった item だけ」を選び出す。
/// </para>
/// <para>
/// <b>歯止め</b>＝⑴ 旧台帳（<c>.ledger.json</c>）が無い機体は差分を組めない → その 1 回だけ丸ごと
/// ⑵ 落ちる量が台帳全体の <see cref="RebuildFraction"/> を超えたら丸ごと（半端に混ざった樹を作らない）
/// ⑶ 旧にだけ在る item が 1 つでも在れば丸ごと（抜く手が無いので新旧が混ざる）
/// ⑷ <c>sha256</c> の無い item は取らない → 丸ごとへ落として <see cref="FetchPlanner"/> に断らせる。
/// </para>
/// <para>
/// <b>差分の回の構え</b>＝<see cref="ToDifferentialRun"/> が取得計画と展開器を対で返す
/// （<c>CleanBeforeInstall=false</c>・<c>RemovePartialOnFailure=false</c>）。既定のまま流すと
/// ⑴ 頭で既存の一式が消えて差分の意味が無くなり、⑵ 差分の 1 檔が落ちただけで動いていた一式が
/// 丸ごと失われる。<b>締めは <see cref="VerifyAfterApply"/> を必ず撃つ</b>（歯止め ⑵）。
/// </para>
/// </summary>
public static class RuntimeDiff
{
    /// <summary>
    /// 落ちる量が台帳全体のこの割合を超えたら<b>丸ごと組み直す</b>（<c>v2-spec.md</c> §11-3）。
    /// <para>
    /// <b>§11-3 の「量」は item の<u>件数</u>で読む</b>（是正・2026-09-11 の記帳＝
    /// <c>ben-e</c> §22-6）。§11-3 の他の「量」（押す前に告げる量）はバイトだが、この歯止めの目的は
    /// <b>「半端に混ざった樹を作らない」</b>で、混ざりの度合いは<b>入れ替わる item の数</b>に比例する＝
    /// 4 GB の <c>torch</c> 1 本だけが動いた版はバイトでは 9 割を超えるのに、樹はほとんど混ざらない。
    /// 逆の穴（小さい wheel が 61 本動いて 4 GB を落とし直す）は判っていて残してある。
    /// </para>
    /// </summary>
    public const double RebuildFraction = 0.6;

    /// <summary>量を告げるときに前提にする速さ（100 Mbps 級＝受け入れ条件 D-5 と同じ物差し）。</summary>
    public const double AssumedBytesPerSecond = 12.5 * 1024 * 1024;

    /// <summary>
    /// 新旧 2 つの台帳を <c>(name, sha256)</c> で突き合わせる（<b>純関数</b>＝檔に触らない）。
    /// <paramref name="oldLedger"/> が null／空＝<c>.ledger.json</c> が無い機体＝丸ごと。
    /// </summary>
    public static RuntimeDiffPlan Plan(LedgerFile? oldLedger, LedgerFile newLedger)
    {
        ArgumentNullException.ThrowIfNull(newLedger);

        var fresh = Installable(newLedger);
        if (oldLedger?.Items is null || Installable(oldLedger).Count == 0)
        {
            return Rebuild("展開に使った台帳の写しが無い（この 1 回だけ丸ごと組み直す）。");
        }

        var previous = new Dictionary<string, LedgerItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Installable(oldLedger))
        {
            previous[item.Name] = item; // 同じ名が 2 度出る台帳は後ろ勝ち（順序不同でも結果は 1 つ）
        }

        var added = new List<LedgerItem>();
        var changed = new List<LedgerItem>();
        var unverifiable = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in fresh)
        {
            seen.Add(item.Name);
            if (!previous.TryGetValue(item.Name, out var before))
            {
                Take(item, added, unverifiable);
                continue;
            }

            if (!string.Equals(
                    Trim(before.Sha256), Trim(item.Sha256), StringComparison.OrdinalIgnoreCase)
                || Trim(item.Sha256) is null)
            {
                Take(item, changed, unverifiable);
            }
        }

        var removed = previous.Keys.Where(name => !seen.Contains(name)).OrderBy(
            static n => n, StringComparer.Ordinal).ToArray();

        if (unverifiable.Count > 0)
        {
            return Rebuild(
                "取得台帳に sha256 の無い item がある（差分では取らない）："
                + string.Join("・", unverifiable.Take(3)),
                removed,
                unverifiable);
        }

        if (removed.Length > 0)
        {
            return Rebuild(
                "前の台帳にだけ在る item がある（site-packages から安全に抜く手が無い）："
                + string.Join("・", removed.Take(3)),
                removed,
                unverifiable);
        }

        var take = added.Count + changed.Count;
        if (fresh.Count > 0 && take > fresh.Count * RebuildFraction)
        {
            return Rebuild(
                "落ちる item が台帳の "
                + ((int)(RebuildFraction * 100)).ToString(CultureInfo.InvariantCulture)
                + " % を超える（半端に混ざった樹を作らない）。",
                removed,
                unverifiable);
        }

        return new RuntimeDiffPlan(added, changed, removed, unverifiable, false, null);
    }

    /// <summary>
    /// <b>差分の回の一式（取得計画と展開器を 1 手で渡す）</b>（是正・2026-09-11）。
    /// <para>
    /// <b>なぜ 1 本にするか</b>＝取得計画（差分だけ）と展開器（<c>CleanBeforeInstall=false</c>）は
    /// <b>必ず対で使う</b>。片方を忘れて既定の <see cref="WheelInstaller"/> に差分の計画を流すと、
    /// 頭で変種ディレクトリを丸ごと消してから<b>変わった数本だけ</b>を入れる＝
    /// 動いていた一式が「数本の wheel だけの樹」になる。2 つの入口を並べておくと、
    /// その取り違えは<b>註でしか止められない</b>。だから入口を 1 つにする。
    /// </para>
    /// <para>
    /// 丸ごと組み直しの回（<see cref="RuntimeDiffPlan.RebuildAll"/>）はここを通さない＝
    /// 既存の初回取得の道（既定の <see cref="WheelInstaller"/>）をそのまま使う。
    /// 誤って渡されたら<b>投げる</b>（黙って半端な樹を作らせない）。
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="diff"/> が丸ごと組み直し（差分では済まない）のとき。
    /// </exception>
    public static (FetchPlan Plan, WheelInstaller Installer) ToDifferentialRun(
        string variant, RuntimeDiffPlan diff)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        ArgumentNullException.ThrowIfNull(diff);

        if (diff.RebuildAll)
        {
            throw new InvalidOperationException(
                "丸ごと組み直しの計画を差分の道へ流そうとした：" + (diff.RebuildReason ?? "（理由なし）"));
        }

        return (ToFetchPlan(variant, diff), NewInstaller(differential: true));
    }

    /// <summary>
    /// <b>差分を当てた後の締め</b>（<c>v2-spec.md</c> §11-3 の歯止め ⑵・是正・2026-09-11）。
    /// <para>
    /// 差分の回は <c>RemovePartialOnFailure=false</c> で構えるので、途中で落ちた機体は
    /// <b>新旧が混ざった樹</b>で残る。展開器が「成功」を名乗った回でも、
    /// <c>site-packages\*.dist-info</c> の件数が配布樹の台帳と合うかを<b>必ず</b>撃ち、
    /// 合わなければ<b>その場で丸ごと組み直しへ落とす</b>（裁定 91 の規律をそのまま使う）。
    /// </para>
    /// <para>
    /// <b><c>.ledger.json</c>（<see cref="RuntimeStamp.Burn"/>）はこれが真を返してから置く。</b>
    /// 偽の樹に写しを置くと、次の版の <see cref="Plan"/> がその嘘を信じて混ざりを温存する。
    /// </para>
    /// </summary>
    /// <returns>締めが通ったか（偽＝丸ごと組み直しへ落とす）。</returns>
    public static bool VerifyAfterApply(AppPaths paths, LedgerFile? ledger, string variant)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        return RuntimeStamp.LooksComplete(paths, ledger, variant);
    }

    /// <summary>
    /// 差分だけを落とす取得計画（<c>vc_redist</c> と models は<b>含めない</b>）。
    /// <see cref="FetchPlanner.RejectItemsWithoutSha256"/> はそのまま効く。
    /// <b>入口は <see cref="ToDifferentialRun"/> 1 本</b>＝展開器と切り離して使わせない。
    /// </summary>
    private static FetchPlan ToFetchPlan(string variant, RuntimeDiffPlan diff) =>
        FetchPlanner.Plan(
            variant,
            new LedgerFile(),
            new LedgerFile { Items = diff.Fetch },
            null,
            null,
            new FetchPlanOptions(SkipVcRedist: true, SkipModels: true));

    /// <summary>
    /// 差分の回の <see cref="WheelInstaller"/>（<b>既存の樹を消さない構え</b>）。
    /// <b>入口は <see cref="ToDifferentialRun"/> 1 本</b>＝取得計画と切り離して使わせない。
    /// <para>
    /// <b>差分の回に外す門は 3 つ</b>（是正・2026-09-11・high 1〜3）＝
    /// ⑴ <c>VerifyDistInfoCount</c>＝渡す台帳が<b>切れ端</b>なので、樹の全件と突き合わせても合わない
    /// （締めは <see cref="VerifyAfterApply"/> が配布樹の台帳＝全件で撃つ）
    /// ⑵ <c>SkipPythonEmbed</c>＝取得計画に <c>python-embed</c> が 1 件も無いのに原檔を要求され、
    /// 空の取得キャッシュ（<c>decisions.md</c> 90）で wheel を触る前に落ちていた
    /// ⑶ <c>ReplaceSupersededDistInfo</c>＝版が上がった wheel の古い <c>*.dist-info</c> が残ると
    /// 締めの件数が必ず 1 件多くなる。<b>3 つとも、外さなければ差分の回は 1 度も成功しない。</b>
    /// </para>
    /// </summary>
    private static WheelInstaller NewInstaller(bool differential) => new()
    {
        CleanBeforeInstall = !differential,
        RemovePartialOnFailure = !differential,
        VerifyDistInfoCount = !differential,
        SkipPythonEmbed = differential,
        ReplaceSupersededDistInfo = differential,
    };

    /// <summary>
    /// 押す前に量を告げる 1 行（<b>純関数</b>）＝「約 320 MiB をダウンロードします（2 分ほど）。」。
    /// 0 バイトなら null。
    /// </summary>
    public static string? DownloadNotice(long bytes)
    {
        if (bytes <= 0)
        {
            return null;
        }

        var minutes = Math.Max(1, (int)Math.Round(bytes / AssumedBytesPerSecond / 60.0));
        return "約 " + FetchPlanner.FormatBytes(bytes) + " をダウンロードします（"
               + minutes.ToString(CultureInfo.InvariantCulture) + " 分ほど）。";
    }

    /// <summary>旧にだけ在る item をログに残す 1 行（<b>純関数</b>＝0 件なら null）。</summary>
    public static string? RemovedLine(IReadOnlyList<string> removed)
    {
        ArgumentNullException.ThrowIfNull(removed);
        return removed.Count == 0
            ? null
            : "前の台帳にだけ在る item は消しません（" + removed.Count.ToString(CultureInfo.InvariantCulture)
              + " 件）：" + string.Join("・", removed.Take(3));
    }

    /// <summary>
    /// 展開の対象になる item だけ（<c>python-embed</c> は変種の台帳に混ざっていても別段が持つ）。
    /// </summary>
    private static IReadOnlyList<LedgerItem> Installable(LedgerFile ledger) =>
        ledger.Items
            .Where(static i => i.Kind != LedgerItemKinds.PythonEmbed && !string.IsNullOrWhiteSpace(i.Name))
            .ToArray();

    private static void Take(LedgerItem item, List<LedgerItem> into, List<string> unverifiable)
    {
        if (string.IsNullOrWhiteSpace(item.Sha256))
        {
            unverifiable.Add(item.Name);
            return;
        }

        into.Add(item);
    }

    private static RuntimeDiffPlan Rebuild(
        string reason, IReadOnlyList<string>? removed = null, IReadOnlyList<string>? unverifiable = null) =>
        new([], [], removed ?? [], unverifiable ?? [], true, reason);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
