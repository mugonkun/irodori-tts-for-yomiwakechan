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

    /// <summary>
    /// この計画の展開係数（<b>変種ごとの実測</b>＝裁定 94 ⑶・<see cref="FetchPlanner.ExpansionFactorFor"/>）。
    /// </summary>
    public ExpansionFactor Expansion => FetchPlanner.ExpansionFactorFor(Variant);

    /// <summary>変種ディレクトリに展開されるバイトの見積り（変種ごとの実測係数）。</summary>
    public long EstimatedRuntimeBytes => (long)(
        Steps.Where(s => s.Stage is FetchStage.PythonEmbed or FetchStage.Runtime).Sum(s => s.Bytes)
        * Expansion.Factor);

    /// <summary>
    /// 取得中にいちばん要る空き容量の見積り＝cache の原檔＋展開した変種＋モデル。
    /// （展開が済めば cache は消してよいが、途中では両方在る。）
    /// </summary>
    public long EstimatedPeakDiskBytes => CacheBytes + EstimatedRuntimeBytes + ModelBytes;

    /// <summary>取得の所要見積り（<b>純関数</b>・受け入れ条件 D-5 は 100 Mbps 級で ≤ 10 分）。</summary>
    public TimeSpan? EstimateDuration(double bytesPerSecond) =>
        bytesPerSecond <= 0 ? null : TimeSpan.FromSeconds(TotalBytes / bytesPerSecond);

    /// <summary>
    /// UI に出す 1 行（「取得 4.79 GiB・必要な空き 8.19 GiB（展開は実測 2.98 倍）」）。
    /// <para>
    /// <b>実測と推定を出し分ける</b>（裁定 94 ⑶）＝1 巡目・2 巡目は全変種に 3.3 を掛けており、
    /// cu126 では実測 1.69 の<b>倍近い空き</b>を要求していた（実射＝14.45 GiB と告げて
    /// 実際は 8.38 GB）。数字の根拠が実測か推定かを利用者に見せる。
    /// </para>
    /// </summary>
    public string Summary() => string.Create(CultureInfo.InvariantCulture,
        $"取得 {FetchPlanner.FormatBytes(TotalBytes)}・必要な空き {FetchPlanner.FormatBytes(EstimatedPeakDiskBytes)}（展開は{Expansion.Describe()}）");
}

/// <summary>
/// 落とした原檔が展開されて何倍になるか（<b>変種ごとの実測</b>＝裁定 94 ⑶）。
/// </summary>
/// <param name="Variant">台帳の綴り。</param>
/// <param name="Factor">倍率。</param>
/// <param name="Measured">実測か（偽＝まだ 1 度も測っていない＝推定）。</param>
public sealed record ExpansionFactor(string Variant, double Factor, bool Measured)
{
    /// <summary>「実測 2.98 倍」／「推定 3.3 倍」（<b>純関数</b>）。</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture, $"{(Measured ? "実測" : "推定")} {Factor:0.##} 倍");
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
    /// まだ 1 度も測っていない変種に掛ける係数（<b>推定</b>＝裁定 94 ⑶）。
    /// <para>
    /// 便 A の <c>cpu</c> の実走（270.1 MiB → 903 MB）から採った丸めで、cu130 だけがこの値に残る
    /// （RTX 機で展開後を 1 度測ったら実測へ移すこと＝裁定 94 ⑶ の宿題）。
    /// </para>
    /// </summary>
    public const double EstimatedExpansionFactor = 3.3;

    /// <summary>
    /// 便 E（2）の E2E で測った展開後／落としたバイトの比（裁定 94 ⑶ の逐語）。
    /// <list type="bullet">
    /// <item><c>cu126</c>＝<b>1.69</b>（実測）</item>
    /// <item><c>rocm-gfx1151</c>＝<b>2.98</b>（実測・26,523 檔 4.25 GB）</item>
    /// <item><c>cpu</c>＝<b>3.60</b>（実測）</item>
    /// <item><c>cu130</c>＝<b>未実測</b>＝<see cref="EstimatedExpansionFactor"/></item>
    /// </list>
    /// <b>これは表示のためだけの数</b>で、取得も展開もこの値には依らない。
    /// </summary>
    public static ExpansionFactor ExpansionFactorFor(string? variant)
    {
        var name = variant?.Trim() ?? string.Empty;

        if (string.Equals(name, RuntimeVariants.Cu126, StringComparison.OrdinalIgnoreCase))
        {
            return new ExpansionFactor(name, 1.69, Measured: true);
        }

        if (RuntimeVariants.IsRocm(name))
        {
            return new ExpansionFactor(name, 2.98, Measured: true);
        }

        if (RuntimeVariants.IsCpu(name))
        {
            return new ExpansionFactor(name, 3.60, Measured: true);
        }

        // cu130・畳んだ名 cuda・知らない綴り＝未実測（低めに出さない側に倒す）
        return new ExpansionFactor(name, EstimatedExpansionFactor, Measured: false);
    }

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

        // **素性の判らない檔を計画に入れない**（是正・2026-09-05・low 6）。
        // 取得側（HttpDownloader）は sha256 が無ければ長さしか見られないので、
        // 「入れない」判断はここと LedgerReader.Validate の 2 箇所で先に済ませる
        // （assemble-runtime.ps1 の Get-YwkCachedItem が throw するのと同じ規律）。
        RejectItemsWithoutSha256(steps);

        return new FetchPlan(variant, steps, modelBytes);
    }

    /// <summary>
    /// cache を経由する手（<see cref="FetchStage.Models"/> 以外）に sha256 の無い item が
    /// 1 件でもあれば投げる（<b>純関数</b>）。
    /// <c>models</c> の手は URL 単位の注文にならない（<c>ywk_fetch_models.py</c> が取る）ので見ない。
    /// </summary>
    public static void RejectItemsWithoutSha256(IReadOnlyList<FetchStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var bad = steps
            .Where(s => s.Stage != FetchStage.Models && s.Item is not null
                        && string.IsNullOrWhiteSpace(s.Item.Sha256))
            .Select(s => s.Name)
            .ToArray();

        if (bad.Length > 0)
        {
            throw new LedgerException(
                "取得台帳に sha256 の無い item がある（検証できない物は取らない）："
                + string.Join("・", bad.Take(3))
                + (bad.Length > 3
                    ? string.Create(CultureInfo.InvariantCulture, $"（他 {bad.Length - 3} 件）")
                    : string.Empty));
        }
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
