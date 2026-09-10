using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Ledger;

namespace IrodoriTtsYwk.Launcher.Services.Models;

/// <summary>
/// 声のデータ（モデル）の差分の<b>見積り</b>（<c>v2-spec.md</c> §11-4）。
/// </summary>
/// <param name="Changed">
/// <c>sha256</c> が変わった檔（<c>&lt;repo&gt;:&lt;path&gt;</c>）＝落ちるバイトのほぼ全部。
/// </param>
/// <param name="Added">新しい台帳にだけ在る檔（同じ形）。</param>
/// <param name="Unknown">
/// <c>sha256</c> が <c>null</c> の檔（非 LFS＝<c>.gitattributes</c>・<c>README.md</c> 等）。
/// <b>見積りに数えない</b>（小さいので実害が無い＝毎回 <c>hf_hub_download</c> に任せる）。
/// </param>
/// <param name="Bytes">落ちるバイトの見積り（<see cref="Changed"/>＋<see cref="Added"/> の <c>size</c> の和）。</param>
public sealed record ModelDiffPlan(
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Unknown,
    long Bytes)
{
    /// <summary>何も落ちない結末。</summary>
    public static readonly ModelDiffPlan Empty = new([], [], [], 0);

    /// <summary>1 檔でも落ちるか。</summary>
    public bool Any => Changed.Count > 0 || Added.Count > 0;
}

/// <summary>
/// <b>見積り専用の純関数</b>（<c>v2-spec.md</c> §11-4）＝何 MB 落ちるかを数えるだけで、
/// <b>檔を 1 つも動かさない</b>。
/// <para>
/// <b>穴だと思っていた物は、穴ではない</b>＝<c>server/ywk_fetch_models.py</c> は
/// <c>hf_hub_download(cache_dir=hub_cache)</c> を使い、huggingface_hub の cache は
/// <c>blobs/&lt;etag&gt;</c> で中身を共有する。revision が動いても<b>中身が同じ檔は
/// 新しい <c>snapshots/&lt;revision&gt;/</c> に張り直されるだけでネットには出ない</b>＝
/// <b>差分は既存の取得の道が既に持っている。</b>新しい取得系は要らない。
/// </para>
/// <para>
/// 旧案の「古い snapshot から新しい snapshot へ hard link を張る」は<b>既に済んでいる仕事の
/// 作り直し</b>であり、hub cache の作法（Windows で symlink が張れないときはコピーへ落ちる）を
/// 手で壊す危険がある。<b>採らない。</b>
/// </para>
/// <para>
/// <b>古い revision の snapshot は自動で消さない</b>＝前の版へ戻れなくなる。
/// </para>
/// </summary>
public static class ModelDiff
{
    /// <summary>
    /// 適用済みのモデル台帳の写し（<c>&lt;データ樹&gt;\models\.ledger.json</c>＝<c>HF_HOME</c> の直下）。
    /// <c>hub\</c> の外なので huggingface_hub の作法には一切触れない。
    /// </summary>
    public const string AppliedLedgerFileName = ".ledger.json";

    /// <summary>写しの路。</summary>
    public static string AppliedLedgerPath(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths.HfHomeDir, AppliedLedgerFileName);
    }

    /// <summary>
    /// 新旧 2 つの台帳を <c>&lt;repo&gt;:&lt;path&gt;</c> の <c>sha256</c> で突き合わせる（<b>純関数</b>）。
    /// <paramref name="oldLedger"/> が null＝写しが無い＝<b>見積れない</b>ので
    /// <see cref="ModelDiffPlan.Empty"/> を返す（推測の数字を出さない）。
    /// </summary>
    public static ModelDiffPlan Plan(ModelsLedger? oldLedger, ModelsLedger newLedger)
    {
        ArgumentNullException.ThrowIfNull(newLedger);
        if (oldLedger is null)
        {
            return ModelDiffPlan.Empty;
        }

        var previous = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var repo in oldLedger.Repos)
        {
            foreach (var file in repo.Files)
            {
                if (!string.IsNullOrWhiteSpace(repo.Repo) && !string.IsNullOrWhiteSpace(file.Path))
                {
                    previous[Key(repo.Repo, file.Path)] = Trim(file.Sha256);
                }
            }
        }

        var changed = new List<string>();
        var added = new List<string>();
        var unknown = new List<string>();
        var bytes = 0L;

        foreach (var repo in newLedger.Repos)
        {
            foreach (var file in repo.Files)
            {
                if (string.IsNullOrWhiteSpace(repo.Repo) || string.IsNullOrWhiteSpace(file.Path))
                {
                    continue;
                }

                var key = Key(repo.Repo, file.Path);
                var sha = Trim(file.Sha256);
                if (sha is null)
                {
                    // 非 LFS＝git blob sha1 でしか見られない＝**見積りに数えない**（小さい）。
                    unknown.Add(key);
                    continue;
                }

                if (!previous.TryGetValue(key, out var before))
                {
                    added.Add(key);
                    bytes += file.Size ?? 0;
                    continue;
                }

                if (!string.Equals(before, sha, StringComparison.OrdinalIgnoreCase))
                {
                    changed.Add(key);
                    bytes += file.Size ?? 0;
                }
            }
        }

        return new ModelDiffPlan(changed, added, unknown, bytes);
    }

    /// <summary>
    /// いまの配布樹の台帳と、この機体に適用済みの写しを突き合わせる（薄い殻）。
    /// 写しが無い・台帳が読めない回は <see cref="ModelDiffPlan.Empty"/>＝<b>黙る</b>。
    /// </summary>
    public static ModelDiffPlan PlanAgainstApplied(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var fresh = ReadLedger(paths.LedgerPath(LedgerFileNames.Models));
        return fresh is null ? ModelDiffPlan.Empty : Plan(ReadLedger(AppliedLedgerPath(paths)), fresh);
    }

    /// <summary>
    /// <b>モデルが揃っている回にだけ</b>、いまの台帳を写しへ置き直す。
    /// <para>
    /// 揃っていない回（＝新しい版の台帳で足りない檔が在る回）に置き直すと、
    /// <b>差分の見積りの元が消える</b>。だから「揃った」を条件にする＝取得が済んだ次の起動で
    /// 写しが新しくなり、その次の版でまた差分が数えられる。
    /// </para>
    /// </summary>
    /// <returns>置き直したか。</returns>
    public static bool RecordAppliedIfReady(AppPaths paths, string? variant)
    {
        ArgumentNullException.ThrowIfNull(paths);

        try
        {
            var source = paths.LedgerPath(LedgerFileNames.Models);
            if (!File.Exists(source) || !Directory.Exists(paths.HfHomeDir))
            {
                return false;
            }

            if (AcquisitionCheck.MissingModelFiles(paths).Count > 0)
            {
                return false; // まだ揃っていない＝写しは前の版のまま（差分の元を消さない）
            }

            var destination = AppliedLedgerPath(paths);
            if (File.Exists(destination)
                && string.Equals(
                    RuntimeStamp.Sha256OfFile(source),
                    RuntimeStamp.Sha256OfFile(destination),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false; // もう同じ＝1 バイトも書かない
            }

            File.Copy(source, destination, overwrite: true);
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

        // variant はいまの見積りには効かない（モデルは変種に依らない）。
        // 呼ぶ側の綴りを 1 本に保つために受け取るだけにしてある。
    }

    /// <summary>押す前に量を告げる 1 行（<b>純関数</b>＝0 バイトなら null）。</summary>
    public static string? DownloadNotice(ModelDiffPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return RuntimeDiff.DownloadNotice(plan.Bytes);
    }

    /// <summary>ログに残す 1 行（<b>純関数</b>＝0 檔なら null）。</summary>
    public static string? Summary(ModelDiffPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.Any)
        {
            return null;
        }

        return "声のデータの差分＝"
               + (plan.Changed.Count + plan.Added.Count).ToString(CultureInfo.InvariantCulture)
               + " 檔・" + FetchPlanner.FormatBytes(plan.Bytes);
    }

    private static ModelsLedger? ReadLedger(string path)
    {
        try
        {
            return File.Exists(path) ? LedgerReader.ParseModels(File.ReadAllText(path)) : null;
        }
        catch (LedgerException)
        {
            return null;
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

    private static string Key(string repo, string path) => repo.Trim() + ":" + path.Trim();

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
