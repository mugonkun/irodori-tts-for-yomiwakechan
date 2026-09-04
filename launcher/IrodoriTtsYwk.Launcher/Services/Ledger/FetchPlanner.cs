using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Ledger;

/// <summary>
/// 初回取得の段（設計書 §6・受け入れ条件 D-5 の順）。<b>列挙の順＝実行の順</b>。
/// </summary>
public enum FetchStage
{
    /// <summary><c>vc_redist.x64.exe</c>（UAC が要るので最初＝利用者の操作を先に済ませる）。</summary>
    VcRedist,

    /// <summary>埋め込み Python（これが無いとモデル取得の子プロセスも起こせない）。</summary>
    PythonEmbed,

    /// <summary>変種の site-packages 一式（torch を含む＝いちばん重い）。</summary>
    Runtime,

    /// <summary>HF の 3 リポ（<c>ywk_fetch_models.py</c> が取る＝cache を経由しない）。</summary>
    Models,
}

/// <summary>取得計画の 1 手。</summary>
/// <param name="Stage">段。</param>
/// <param name="Name">台帳の <c>name</c>（UI に出す）。</param>
/// <param name="Version">台帳の <c>version</c>。</param>
/// <param name="Bytes">落とすバイト（台帳の <c>size</c>。分からなければ 0）。</param>
/// <param name="Item">
/// 台帳の 1 件。<see cref="FetchStage.Models"/> の手だけ null＝
/// HF は <c>ywk_fetch_models.py</c> が取るので URL 単位の注文にならない。
/// </param>
public sealed record FetchStep(FetchStage Stage, string Name, string? Version, long Bytes, LedgerItem? Item);

/// <summary>
/// 初回取得の計画（<b>純関数の産物</b>＝これを見れば何をどの順に取るかが全部分かる）。
/// </summary>
/// <param name="Variant">台帳の綴り（<c>cu130</c>／<c>rocm-gfx1151</c> 等）。</param>
/// <param name="Steps">実行の順に並んだ手。</param>
/// <param name="ModelBytes">HF の 3 リポの合計（<see cref="Steps"/> の Models の手にも入っている）。</param>
public sealed record FetchPlan(string Variant, IReadOnlyList<FetchStep> Steps, long ModelBytes)
{
    /// <summary>その段の手だけ。</summary>
    public IReadOnlyList<FetchStep> Stage(FetchStage stage) =>
        Steps.Where(s => s.Stage == stage).ToArray();

    /// <summary>取得の総バイト（進捗の分母＝モデルも含む）。</summary>
    public long TotalBytes => Steps.Sum(s => s.Bytes);

    /// <summary><c>cache/</c> に落ちるバイト（vc_redist＋python-embed＋runtime）。モデルは経由しない。</summary>
    public long CacheBytes => Steps.Where(s => s.Stage != FetchStage.Models).Sum(s => s.Bytes);

    /// <summary>変種ディレクトリに展開されるバイトの見積り（<see cref="FetchPlanner.ExpansionFactor"/>）。</summary>
    public long EstimatedRuntimeBytes => (long)(
        Steps.Where(s => s.Stage is FetchStage.PythonEmbed or FetchStage.Runtime).Sum(s => s.Bytes)
        * FetchPlanner.ExpansionFactor);

    /// <summary>
    /// 取得中にいちばん要る空き容量の見積り＝cache の原檔＋展開した変種＋モデル。
    /// （展開が済めば cache は消してよいが、途中では両方在る。）
    /// </summary>
    public long EstimatedPeakDiskBytes => CacheBytes + EstimatedRuntimeBytes + ModelBytes;

    /// <summary>取得の所要見積り（<b>純関数</b>・受け入れ条件 D-5 は 100 Mbps 級で ≤ 10 分）。</summary>
    public TimeSpan? EstimateDuration(double bytesPerSecond) =>
        bytesPerSecond <= 0 ? null : TimeSpan.FromSeconds(TotalBytes / bytesPerSecond);

    /// <summary>UI に出す 1 行（「取得 5.4 GiB・展開後 9.1 GiB」）。</summary>
    public string Summary() => string.Create(CultureInfo.InvariantCulture,
        $"取得 {FetchPlanner.FormatBytes(TotalBytes)}・必要な空き {FetchPlanner.FormatBytes(EstimatedPeakDiskBytes)}");
}

/// <summary>取得計画を作るときの取捨。</summary>
/// <param name="SkipVcRedist">System32 に <c>msvcp140.dll</c> が在った（<see cref="VcRedistInstaller"/> の判定）。</param>
/// <param name="SkipModels">モデルは別立てで取る／既に在る。</param>
/// <param name="SkipRuntime">変種ディレクトリが既に組み上がっている。</param>
public sealed record FetchPlanOptions(
    bool SkipVcRedist = false,
    bool SkipModels = false,
    bool SkipRuntime = false);

/// <summary>
/// 台帳から初回取得の計画を作る（<b>すべて純関数</b>）。
/// <para>
/// 順は受け入れ条件 D-5 の逐語＝<b>vc_redist → python-embed → runtime-&lt;変種&gt; → models</b>。
/// vc_redist が先頭なのは UAC の窓を最初に出して利用者の操作を済ませるため（設計書 §6・利用者操作 ≤ 6）。
/// </para>
/// </summary>
public static class FetchPlanner
{
    /// <summary>
    /// 落とした原檔が展開されて何倍になるかの実測係数。
    /// <list type="bullet">
    /// <item>cpu＝270.1 MiB → 903 MB（便 A の実走・<c>decisions.md</c> 41）＝約 3.3 倍</item>
    /// <item>rocm-gfx1151＝1,465.5 MiB → 4,347.7 MiB（<c>ledger/README.md</c> §9）＝約 2.97 倍</item>
    /// </list>
    /// 見積りなので低めに出さない側（3.3）を採る。<b>これは表示のためだけの数</b>で、
    /// 取得も展開もこの値には依らない。
    /// </summary>
    public const double ExpansionFactor = 3.3;

    /// <summary>
    /// 計画を組む。<paramref name="models"/>／<paramref name="vcRedist"/> は null 可
    /// （台帳が無い配布＝その段を作らない）。
    /// </summary>
    public static FetchPlan Plan(
        string variant,
        LedgerFile pythonEmbed,
        LedgerFile runtime,
        VcRedistLedger? vcRedist,
        ModelsLedger? models,
        FetchPlanOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        ArgumentNullException.ThrowIfNull(pythonEmbed);
        ArgumentNullException.ThrowIfNull(runtime);
        options ??= new FetchPlanOptions();

        var steps = new List<FetchStep>();

        if (!options.SkipVcRedist && vcRedist?.Installer is { } installer)
        {
            steps.Add(new FetchStep(
                FetchStage.VcRedist, installer.Name, installer.Version, installer.Size ?? 0, installer));
        }

        if (!options.SkipRuntime)
        {
            if (pythonEmbed.PythonEmbed is { } embed)
            {
                steps.Add(new FetchStep(
                    FetchStage.PythonEmbed, embed.Name, embed.Version, embed.Size ?? 0, embed));
            }

            foreach (var item in runtime.Items)
            {
                // python-embed が変種の台帳に混ざっていても二重に数えない
                if (item.Kind == LedgerItemKinds.PythonEmbed)
                {
                    continue;
                }

                steps.Add(new FetchStep(
                    FetchStage.Runtime, item.Name, item.Version, item.Size ?? 0, item));
            }
        }

        var modelBytes = 0L;
        if (!options.SkipModels && models is not null && models.Repos.Count > 0)
        {
            modelBytes = models.TotalBytes;
            foreach (var repo in models.Repos)
            {
                steps.Add(new FetchStep(
                    FetchStage.Models,
                    repo.Repo,
                    repo.Revision.Length > 7 ? repo.Revision[..7] : repo.Revision,
                    repo.TotalBytes ?? repo.Files.Sum(f => f.Size ?? 0),
                    null));
            }
        }

        return new FetchPlan(variant, steps, modelBytes);
    }

    /// <summary>
    /// 計画の <see cref="FetchStage.VcRedist"/>／<see cref="FetchStage.PythonEmbed"/>／
    /// <see cref="FetchStage.Runtime"/> の手を <see cref="IDownloader"/> の注文に落とす（<b>順は保つ</b>）。
    /// <see cref="FetchStage.Models"/> は cache を経由しないので含まれない。
    /// </summary>
    public static IReadOnlyList<DownloadRequest> ToDownloadRequests(FetchPlan plan, string cacheDir)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDir);

        return plan.Steps
            .Where(s => s.Stage != FetchStage.Models && s.Item is not null)
            .Select(s => DownloadRequest.FromLedgerItem(s.Item!, cacheDir))
            .ToArray();
    }

    /// <summary>UI に出すバイト表記（<b>純関数</b>・KiB／MiB／GiB）。</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return "-";
        }

        const double Ki = 1024;
        const double Mi = Ki * 1024;
        const double Gi = Mi * 1024;

        return bytes switch
        {
            < (long)Ki => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
            < (long)Mi => string.Create(CultureInfo.InvariantCulture, $"{bytes / Ki:0.0} KiB"),
            < (long)Gi => string.Create(CultureInfo.InvariantCulture, $"{bytes / Mi:0.0} MiB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / Gi:0.00} GiB"),
        };
    }
}
