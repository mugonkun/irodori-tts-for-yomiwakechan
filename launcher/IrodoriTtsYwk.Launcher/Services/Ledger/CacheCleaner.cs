using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Services.Ledger;

/// <summary>取得キャッシュ 1 回分の結末。</summary>
/// <param name="Ok">消したか（偽＝1 檔も消していない）。</param>
/// <param name="Files">消した檔の数。</param>
/// <param name="Bytes">消したバイト。</param>
/// <param name="Message">画面とログに出す 1 行。</param>
public sealed record CacheCleanResult(bool Ok, int Files, long Bytes, string Message);

/// <summary>
/// 取得キャッシュの削除（裁定 90 Q-E2 ⑶・94 ⑶）。
/// <para>
/// <b>なぜ消すか</b>＝展開が済めば <c>cache/</c> の原檔は要らない（実測＝cu126 2.77 GB・
/// rocm 1.37 GB）。受け入れ条件の「展開後（cache 削除後）≤ 9.0 GB」は<b>消すことを前提に</b>
/// 書き直された（裁定 94 ⑶）ので、消さないランチャは条件を満たさない。
/// </para>
/// <para>
/// <b>消してよいと判る前に 1 バイトも消さない</b>（裁定 90 ⑶ の ⒞）＝⑴ 変種ディレクトリに
/// <c>python.exe</c> が在る（展開が本当に済んでいる）⑵ <c>settings.json</c> の
/// <c>runtimeLedgers[&lt;変種&gt;]</c> が<b>配布樹の台帳と一致</b>する（いま在る実行系がこの台帳から
/// 出来ている）。どちらか 1 つでも欠ければ「消さない」で返す＝取り直しが要る機体から
/// 原檔を奪わない。
/// </para>
/// <para>
/// <b>消す範囲は <see cref="AppPaths.DownloadCacheDir"/> の中身すべて</b>（ディレクトリ自身は残す）。
/// 裁定 90 の逐語は「台帳の item の檔」だが、そこに落ちるのは<b>取得系だけ</b>（<c>HttpDownloader</c>
/// の着地先・<c>VcRedistInstaller</c> の原檔）で、しかも実際に残るのは<b>今の台帳が名指す檔だけ
/// ではない</b>＝⒜ 前の台帳の原檔 ⒝ 打ち切った <c>.part</c> ⒞ 名前が変わった檔。名前で選ぶと
/// この 3 つが残り、「展開後（cache 削除後）」の実測が合わなくなる。<b>関門</b>（上）で
/// 「取り直しは要らない」と判った後に、置き場ごと空にする（裁定 95 ⑵ の追認）。
/// </para>
/// <para>
/// <b>例外は「関門を通っていない変種だけが名指す原檔」</b>（是正・便 D（3）の 3 巡目）＝
/// 関門は<b>いまの変種 1 つ</b>としか突き合わせないのに、置き場は変種で分かれていない。
/// この機体の実物では <c>cache</c> 110 檔 4.14 GB のうち <b>2.41 GiB が cu126 専用</b>
/// （<c>torch-2.10.0+cu126…whl</c> 2,589,881,452 B ほか）で、変種 <c>rocm-gfx1151</c> の関門を
/// 通しただけの掃除がそれを<b>cu126 の台帳を 1 度も検めずに</b>消していた。だから
/// <see cref="Clean"/> は「消してよいと判った変種の台帳が名指す檔」と「どの台帳も名指さない檔」
/// （＝⒜⒝⒞）を消し、<b>まだ検めていない変種だけが名指す檔</b>は残す。除外集合は
/// <see cref="ProtectedFileNames"/> が組む。
/// </para>
/// </summary>
public static class CacheCleaner
{
    /// <summary>取得の途中経過の拡張子（<c>HttpDownloader</c> の <c>.part</c>）。</summary>
    public const string PartSuffix = ".part";

    /// <summary>
    /// 計画が cache に落とす檔の名（<b>純関数</b>・<see cref="FetchStage.Models"/> は経由しない）。
    /// <b>削除には使わない</b>＝いま在る檔のうち何件が今の台帳の物かを言うためだけの表である。
    /// </summary>
    public static IReadOnlyList<string> FileNames(FetchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Steps
            .Where(static s => s.Stage != FetchStage.Models && s.Item is not null)
            .Select(static s => s.Item!.EffectiveFileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// <b>消してはいけない原檔の名</b>（<b>純関数</b>）＝<paramref name="variants"/> のうち
    /// <paramref name="cleanable"/> が偽の変種<b>だけ</b>が名指す檔。
    /// <para>
    /// 通った変種と<b>共有している</b>檔（同じ檔名の wheel）は除外集合に入れない＝その 1 檔は
    /// 通った変種の展開で既に使われており、置き場ごと空にする値打ち（裁定 94 ⑶ の実測）を
    /// 守るためである。落ちるのは「まだ検めていない変種<b>専用</b>の原檔」だけになる。
    /// </para>
    /// </summary>
    /// <param name="variants">配布樹に取得台帳が在る変種。</param>
    /// <param name="planOf">その変種の取得計画（読めなければ null）。</param>
    /// <param name="cleanable">その変種は関門を通ったか（＝その台帳の原檔を消してよいか）。</param>
    public static IReadOnlyCollection<string> ProtectedFileNames(
        IEnumerable<string> variants,
        Func<string, FetchPlan?> planOf,
        Func<string, bool> cleanable)
    {
        ArgumentNullException.ThrowIfNull(variants);
        ArgumentNullException.ThrowIfNull(planOf);
        ArgumentNullException.ThrowIfNull(cleanable);

        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var drop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var variant in variants)
        {
            if (string.IsNullOrWhiteSpace(variant))
            {
                continue;
            }

            var plan = planOf(variant);
            if (plan is null)
            {
                continue;
            }

            var target = cleanable(variant) ? drop : keep;
            foreach (var name in FileNames(plan))
            {
                target.Add(name);
            }
        }

        keep.ExceptWith(drop);
        return keep;
    }

    /// <summary>
    /// 同上を配布樹と設定から組む（<b>関門は <see cref="Blocked"/> と同じ 1 本</b>）。
    /// </summary>
    /// <param name="paths">場所。</param>
    /// <param name="settings">焼き印の表を持つ設定。</param>
    /// <param name="variants">配布樹に取得台帳が在る変種（<see cref="LedgerVariants"/>）。</param>
    /// <param name="planOf">その変種の取得計画（<c>FirstRunViewModel.TryPlan</c>）。</param>
    public static IReadOnlyCollection<string> ProtectedFileNames(
        AppPaths paths,
        LauncherSettings settings,
        IEnumerable<string> variants,
        Func<string, FetchPlan?> planOf)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settings);

        return ProtectedFileNames(
            variants,
            planOf,
            variant => Blocked(
                paths.ResolvePythonExe(variant) is not null,
                settings.RuntimeLedgerFor(variant),
                RuntimeStamp.LedgerSha256(paths, variant)) is null);
    }

    /// <summary>
    /// 台帳の檔名（拡張子なし）の並びから、取得台帳が在る変種を拾う（<b>純関数</b>）。
    /// </summary>
    public static IReadOnlyList<string> LedgerVariants(IEnumerable<string> ledgerNames)
    {
        ArgumentNullException.ThrowIfNull(ledgerNames);

        const string prefix = "runtime-";
        var variants = new List<string>();
        foreach (var name in ledgerNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var trimmed = name.Trim();
            if (trimmed.Length > prefix.Length
                && trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                variants.Add(trimmed[prefix.Length..]);
            }
        }

        return variants;
    }

    /// <summary>
    /// cache に居る檔の数と合計バイト（<b>入れ子も数える</b>）。読めない檔は数えない。
    /// <paramref name="protectedFileNames"/> に載っている檔名は<b>数えない</b>
    /// （数えた量がそのままボタンの文言＝消える量になる）。
    /// </summary>
    public static (int Files, long Bytes) Measure(
        string cacheDir, IReadOnlyCollection<string>? protectedFileNames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDir);

        var keep = ToSet(protectedFileNames);
        var files = 0;
        var bytes = 0L;
        foreach (var path in EnumerateFiles(cacheDir))
        {
            if (IsProtected(path, keep))
            {
                continue;
            }

            try
            {
                var info = new FileInfo(path);
                if (info.Exists)
                {
                    files++;
                    bytes += info.Length;
                }
            }
            catch (IOException)
            {
                // 読めない＝数えない
            }
            catch (UnauthorizedAccessException)
            {
                // 同上
            }
        }

        return (files, bytes);
    }

    /// <summary>
    /// 消してよい状態か（<b>純関数</b>）。理由 1 行つき（null＝消してよい）。
    /// </summary>
    /// <param name="runtimeInstalled">変種ディレクトリに <c>python.exe</c> が在るか。</param>
    /// <param name="storedLedgerSha256">
    /// <c>settings.json</c> の <c>runtimeLedgers[&lt;変種&gt;]</c>（その変種の焼き印）。
    /// </param>
    /// <param name="currentLedgerSha256">配布樹の台帳の sha256。</param>
    public static string? Blocked(
        bool runtimeInstalled, string? storedLedgerSha256, string? currentLedgerSha256)
    {
        if (!runtimeInstalled)
        {
            return UiStrings.CacheBlockedNotInstalled;
        }

        if (string.IsNullOrWhiteSpace(storedLedgerSha256) || string.IsNullOrWhiteSpace(currentLedgerSha256))
        {
            return UiStrings.CacheBlockedUnknown;
        }

        return string.Equals(storedLedgerSha256.Trim(), currentLedgerSha256.Trim(), StringComparison.OrdinalIgnoreCase)
            ? null
            : UiStrings.CacheBlockedMismatch;
    }

    /// <summary>
    /// 消す。<b>関門を通らなければ 1 檔も触らない</b>（<see cref="Blocked"/>）。
    /// 置き場そのもの（<paramref name="cacheDir"/>）は残す＝次の取得がそのまま使える。
    /// </summary>
    /// <param name="cacheDir"><c>&lt;data&gt;\cache</c>。</param>
    /// <param name="runtimeInstalled">変種ディレクトリに <c>python.exe</c> が在るか。</param>
    /// <param name="storedLedgerSha256">
    /// <c>settings.json</c> の <c>runtimeLedgers[&lt;変種&gt;]</c>（その変種の焼き印）。
    /// </param>
    /// <param name="currentLedgerSha256">配布樹の台帳の sha256。</param>
    /// <param name="protectedFileNames">
    /// 残す檔名（<see cref="ProtectedFileNames"/>）＝<b>まだ検めていない変種だけが名指す原檔</b>。
    /// </param>
    public static CacheCleanResult Clean(
        string cacheDir,
        bool runtimeInstalled,
        string? storedLedgerSha256,
        string? currentLedgerSha256,
        IReadOnlyCollection<string>? protectedFileNames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDir);

        if (Blocked(runtimeInstalled, storedLedgerSha256, currentLedgerSha256) is string blocked)
        {
            return new CacheCleanResult(false, 0, 0, blocked);
        }

        var keep = ToSet(protectedFileNames);
        var files = 0;
        var bytes = 0L;
        var failed = 0;
        var kept = 0;
        var keptBytes = 0L;
        foreach (var path in EnumerateFiles(cacheDir))
        {
            if (IsProtected(path, keep))
            {
                kept++;
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists)
                    {
                        keptBytes += info.Length;
                    }
                }
                catch (IOException)
                {
                    // 読めない＝数えない
                }
                catch (UnauthorizedAccessException)
                {
                    // 同上
                }

                continue;
            }

            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    continue;
                }

                var length = info.Length;
                File.Delete(path);
                files++;
                bytes += length;
            }
            catch (IOException)
            {
                // 掴まれている檔＝消せなかっただけ（次に押せば消える）
                failed++;
            }
            catch (UnauthorizedAccessException)
            {
                failed++;
            }
        }

        RemoveEmptyDirectories(cacheDir);
        return new CacheCleanResult(true, files, bytes, Describe(files, bytes, failed, kept, keptBytes));
    }

    /// <summary>結末の 1 行（<b>純関数</b>＝Trail とログに同じ文言を出す）。</summary>
    /// <param name="kept">別の変種のために残した檔の数（0 なら文言に出さない）。</param>
    /// <param name="keptBytes">同・バイト。</param>
    public static string Describe(int files, long bytes, int failed, int kept = 0, long keptBytes = 0)
    {
        var tail = kept > 0
            ? "（別の変種の原檔 " + kept.ToString(CultureInfo.InvariantCulture) + " 檔・"
              + FetchPlanner.FormatBytes(keptBytes) + " は残しました）"
            : string.Empty;

        if (files == 0)
        {
            return (failed == 0
                ? "取得キャッシュはもう空でした（消した物はありません）。"
                : "取得キャッシュを消せませんでした（"
                  + failed.ToString(CultureInfo.InvariantCulture) + " 檔が使用中です）。") + tail;
        }

        var text = "取得キャッシュを消しました（"
            + files.ToString(CultureInfo.InvariantCulture) + " 檔・"
            + FetchPlanner.FormatBytes(bytes) + "）。";

        return (failed == 0
            ? text
            : text + "（" + failed.ToString(CultureInfo.InvariantCulture) + " 檔は使用中で残りました）") + tail;
    }

    /// <summary>除外集合（null・空なら「1 檔も守らない」）。</summary>
    private static HashSet<string>? ToSet(IReadOnlyCollection<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            return null;
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                set.Add(name.Trim());
            }
        }

        return set.Count == 0 ? null : set;
    }

    /// <summary>この檔は残すか（檔名だけで判る＝入れ子の位置は問わない）。</summary>
    private static bool IsProtected(string path, HashSet<string>? keep) =>
        keep is not null && keep.Contains(Path.GetFileName(path));

    /// <summary>置き場の中の檔を全部（入れ子も）。無ければ空。</summary>
    private static IReadOnlyList<string> EnumerateFiles(string cacheDir)
    {
        try
        {
            return Directory.Exists(cacheDir)
                ? Directory.GetFiles(cacheDir, "*", SearchOption.AllDirectories)
                : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>空になった入れ子のディレクトリを畳む（置き場そのものは残す）。</summary>
    private static void RemoveEmptyDirectories(string cacheDir)
    {
        try
        {
            if (!Directory.Exists(cacheDir))
            {
                return;
            }

            foreach (var dir in Directory.GetDirectories(cacheDir, "*", SearchOption.AllDirectories)
                         .OrderByDescending(static d => d.Length))
            {
                try
                {
                    if (Directory.GetFileSystemEntries(dir).Length == 0)
                    {
                        Directory.Delete(dir);
                    }
                }
                catch (IOException)
                {
                    // 残っても害は無い（0 バイト）
                }
                catch (UnauthorizedAccessException)
                {
                    // 同上
                }
            }
        }
        catch (IOException)
        {
            // 同上
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
    }
}
