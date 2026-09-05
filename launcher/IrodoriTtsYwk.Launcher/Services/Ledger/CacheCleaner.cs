using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IrodoriTtsYwk.Launcher.Contracts;

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
/// <c>runtimeLedgerSha256</c> が<b>配布樹の台帳と一致</b>する（いま在る実行系がこの台帳から
/// 出来ている）。どちらか 1 つでも欠ければ「消さない」で返す＝取り直しが要る機体から
/// 原檔を奪わない。
/// </para>
/// <para>
/// <b>消す範囲は <see cref="AppPaths.DownloadCacheDir"/> の中身すべて</b>（ディレクトリ自身は残す）。
/// 裁定 90 の逐語は「台帳の item の檔」だが、そこに落ちるのは<b>取得系だけ</b>（<c>HttpDownloader</c>
/// の着地先・<c>VcRedistInstaller</c> の原檔）で、しかも実際に残るのは<b>今の台帳が名指す檔だけ
/// ではない</b>＝⒜ 前の台帳の原檔 ⒝ 打ち切った <c>.part</c> ⒞ 名前が変わった檔。名前で選ぶと
/// この 3 つが残り、「展開後（cache 削除後）」の実測が合わなくなる。<b>関門</b>（上）で
/// 「取り直しは要らない」と判った後に、置き場ごと空にする。
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
    /// cache に居る檔の数と合計バイト（<b>入れ子も数える</b>）。読めない檔は数えない。
    /// </summary>
    public static (int Files, long Bytes) Measure(string cacheDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDir);

        var files = 0;
        var bytes = 0L;
        foreach (var path in EnumerateFiles(cacheDir))
        {
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
    /// <param name="storedLedgerSha256"><c>settings.json</c> の <c>runtimeLedgerSha256</c>。</param>
    /// <param name="currentLedgerSha256">配布樹の台帳の sha256。</param>
    public static string? Blocked(
        bool runtimeInstalled, string? storedLedgerSha256, string? currentLedgerSha256)
    {
        if (!runtimeInstalled)
        {
            return "実行系がまだ組み上がっていないので、取得キャッシュは消しません。";
        }

        if (string.IsNullOrWhiteSpace(storedLedgerSha256) || string.IsNullOrWhiteSpace(currentLedgerSha256))
        {
            return "展開に使った取得台帳が判らないので、取得キャッシュは消しません"
                + "（実行系を組み直すと判るようになります）。";
        }

        return string.Equals(storedLedgerSha256.Trim(), currentLedgerSha256.Trim(), StringComparison.OrdinalIgnoreCase)
            ? null
            : "いま在る実行系と配布物の取得台帳が違うので、取得キャッシュは消しません"
              + "（実行系を組み直してから消してください）。";
    }

    /// <summary>
    /// 消す。<b>関門を通らなければ 1 檔も触らない</b>（<see cref="Blocked"/>）。
    /// 置き場そのもの（<paramref name="cacheDir"/>）は残す＝次の取得がそのまま使える。
    /// </summary>
    /// <param name="cacheDir"><c>&lt;data&gt;\cache</c>。</param>
    /// <param name="runtimeInstalled">変種ディレクトリに <c>python.exe</c> が在るか。</param>
    /// <param name="storedLedgerSha256"><c>settings.json</c> の <c>runtimeLedgerSha256</c>。</param>
    /// <param name="currentLedgerSha256">配布樹の台帳の sha256。</param>
    public static CacheCleanResult Clean(
        string cacheDir,
        bool runtimeInstalled,
        string? storedLedgerSha256,
        string? currentLedgerSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDir);

        if (Blocked(runtimeInstalled, storedLedgerSha256, currentLedgerSha256) is string blocked)
        {
            return new CacheCleanResult(false, 0, 0, blocked);
        }

        var files = 0;
        var bytes = 0L;
        var failed = 0;
        foreach (var path in EnumerateFiles(cacheDir))
        {
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
        return new CacheCleanResult(true, files, bytes, Describe(files, bytes, failed));
    }

    /// <summary>結末の 1 行（<b>純関数</b>＝Trail とログに同じ文言を出す）。</summary>
    public static string Describe(int files, long bytes, int failed)
    {
        if (files == 0)
        {
            return failed == 0
                ? "取得キャッシュはもう空でした（消した物はありません）。"
                : "取得キャッシュを消せませんでした（"
                  + failed.ToString(CultureInfo.InvariantCulture) + " 檔が使用中です）。";
        }

        var text = "取得キャッシュを消しました（"
            + files.ToString(CultureInfo.InvariantCulture) + " 檔・"
            + FetchPlanner.FormatBytes(bytes) + "）。";

        return failed == 0
            ? text
            : text + "（" + failed.ToString(CultureInfo.InvariantCulture) + " 檔は使用中で残りました）";
    }

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
