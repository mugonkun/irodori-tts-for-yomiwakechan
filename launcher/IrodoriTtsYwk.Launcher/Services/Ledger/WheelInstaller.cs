using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Ledger;

/// <summary>
/// 契約 ⑶ の実装＝取得した原檔から変種ディレクトリを組む。
/// <b>参照実装は <c>build/assemble-runtime.ps1</c></b>（同じ台帳・同じ規則で組む）。
/// <list type="number">
/// <item><c>python-embed</c>＝zip を変種ディレクトリへ展開し <c>python.exe</c> の在ることを見る。</item>
/// <item><c>python312._pth</c> を書く（<see cref="PthWriter"/>）。</item>
/// <item><c>wheel</c>＝zip を <c>site-packages/</c> へまるごと（<c>*.dist-info</c> 込み）。
/// <c>&lt;name&gt;-&lt;ver&gt;.data/purelib|platlib</c> は合流、<c>scripts|headers|data</c> は捨てる。</item>
/// <item><c>sdist</c>＝<c>.tar.gz</c> を .NET の tar で展開し <c>package_dirs</c> を写す。</item>
/// <item><c>archive</c>＝commit 固定 zip を展開し <c>package_dir</c> を写す。</item>
/// <item>どちらも最小 <c>*.dist-info</c> を作る（利用者機に pip は無い）。</item>
/// </list>
/// <para>
/// <b><c>site-packages/</c> 直下の <c>*.pth</c> は 1 件残らず捨てる</b>＝配布する <c>._pth</c> は
/// <c>import site</c> を持たないので <c>.pth</c> は読まれない（<c>ledger/README.md</c> §4-4）。
/// 捨てた件は <see cref="InstallResult.DroppedPth"/> に載せ、最後に 1 件でも残っていれば失敗にする。
/// </para>
/// <para>
/// <b>途中で落ちたら成果物を残さない</b>（家の規則＝<c>assemble-runtime.ps1</c> の trap）。
/// 半端な変種ディレクトリを「取得済み」と誤認させないため、失敗時は消す
/// （<see cref="RemovePartialOnFailure"/> で切れる）。
/// </para>
/// </summary>
public sealed class WheelInstaller : IRuntimeInstaller
{
    private readonly IPthWriter _pthWriter;

    /// <summary>既定の <see cref="PthWriter"/> で組む。</summary>
    public WheelInstaller() : this(new PthWriter())
    {
    }

    /// <summary>テストの継ぎ目＝<see cref="IPthWriter"/> を差せる public コンストラクタ。</summary>
    public WheelInstaller(IPthWriter pthWriter)
    {
        ArgumentNullException.ThrowIfNull(pthWriter);
        _pthWriter = pthWriter;
    }

    /// <summary>失敗したら組みかけの変種ディレクトリを消す（既定＝消す）。</summary>
    public bool RemovePartialOnFailure { get; init; } = true;

    /// <summary>組み直すときに既存の変種ディレクトリを消してから始める（既定＝消す）。</summary>
    public bool CleanBeforeInstall { get; init; } = true;

    /// <summary>
    /// 最後に <c>*.dist-info</c> の件数を<b>渡された台帳</b>と突き合わせる（既定＝突き合わせる）。
    /// <para>
    /// <b>差分の回だけ偽にする</b>（是正・2026-09-11・high 1）＝差分の回に渡る台帳は
    /// 「変わった item だけ」の切れ端で、<see cref="CleanBeforeInstall"/> が偽だから
    /// <c>site-packages</c> には<b>全 N 件</b>が居る。N と切れ端の件数は
    /// <see cref="RuntimeDiff.RebuildFraction"/> の歯止めにより<b>絶対に一致しない</b>＝
    /// この門を素通しにしないと差分の回は 1 度も成功しない（実射＝
    /// 「展開の件数が台帳と合わない（*.dist-info 4 件・台帳は 1 件）。」）。
    /// 締めが消えるわけではない＝差分の回は
    /// <see cref="RuntimeDiff.VerifyAfterApply"/> が<b>配布樹の台帳（全件）</b>で数え直す。
    /// </para>
    /// </summary>
    public bool VerifyDistInfoCount { get; init; } = true;

    /// <summary>
    /// 埋め込み Python を当てない（既定＝当てる）。
    /// <para>
    /// <b>差分の回だけ真にする</b>（是正・2026-09-11・high 2）＝差分の取得計画は
    /// <see cref="RuntimeDiff.ToDifferentialRun"/> が <c>python-embed</c> を<b>1 件も積まない</b>のに、
    /// ここが無条件に配布樹の <c>ledger\python-embed.json</c> を足して原檔を要求すると、
    /// <c>decisions.md</c> 90 で空になっている取得キャッシュに当たって
    /// <b>wheel を 1 本も触らないうちに落ちる</b>（実射＝「python の原檔が cache に無い」）。
    /// 差分の回は相手の樹に <c>python.exe</c> が既に在る（呼び手の
    /// <c>MainViewModel.TryDifferentialAsync</c> がそれを見てから入る）ので、
    /// 11 MB の zip を落とし直して生きた樹の上へ展開し直す意味が無い。
    /// </para>
    /// </summary>
    public bool SkipPythonEmbed { get; init; }

    /// <summary>
    /// 当てる前に<b>同じ名の古い <c>*.dist-info</c></b> を落とす（既定＝落とさない）。
    /// <para>
    /// <b>差分の回だけ真にする</b>（是正・2026-09-11・high 3）＝
    /// <see cref="InstallWheel"/> は新しい zip を site-packages へ被せるだけなので、
    /// 版が上がった wheel は <c>torch-1.dist-info</c> と <c>torch-2.dist-info</c> を<b>両方</b>残す。
    /// 差分の締め（<see cref="RuntimeStamp.LooksComplete"/>）は件数で数えるから、
    /// <b>版が動いたという一番ありふれた回</b>が必ず締めで落ちて丸ごとへ流れていた。
    /// </para>
    /// <para>
    /// <b>落とすのは <c>*.dist-info</c> だけ</b>＝<c>RECORD</c> の列を辿って本体の檔まで抜く手は採らない
    /// （<see cref="RuntimeDiffPlan.Removed"/> を消さないのと同じ理由＝他の item と共有する路を
    /// 巻き添えにしうる）。古い版だけが持っていた <c>.py</c> は残りうる＝
    /// <b>判っている欠落</b>として記帳する（<c>v2-plan.md</c> 段 F の記帳）。
    /// </para>
    /// </summary>
    public bool ReplaceSupersededDistInfo { get; init; }

    /// <inheritdoc />
    public async Task<InstallResult> InstallAsync(
        InstallRequest request,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ledger = request.Ledger;
        var runtimeDir = request.RuntimeDir;
        var sitePackages = Path.Combine(runtimeDir, "site-packages");
        // 作業場は「作ってから」名前が決まる（失敗時に他人の tmp を消さないため空で始める）。
        var scratchRoot = string.Empty;
        var dropped = new List<string>();
        var started = false;

        try
        {
            // **消す前に原檔を一巡して検める**（是正・2026-09-05・low 8）。
            // これまでは item ごとに「展開の直前」に sha256 を突き合わせていたので、
            // 101 件目で cache が欠けていると**既に消した変種ディレクトリ**が戻らない
            // （RemovePartialOnFailure が残骸を消して、動いていた実行系が丸ごと失われる）。
            // 1 巡の代金は 903 MB で 0.2 s（§10-2 の実走＝VERIFY cache-hit=102/102 in 0.2s）。
            var preflight = await PreflightAsync(request, cancellationToken).ConfigureAwait(false);
            if (preflight is not null)
            {
                progress?.Report(new InstallProgress(InstallPhase.Failed, null, 0, ledger.Items.Count));
                return new InstallResult(false, 0, 0, 0, dropped, null, preflight);
            }

            if (CleanBeforeInstall && Directory.Exists(runtimeDir))
            {
                Directory.Delete(runtimeDir, recursive: true);
            }

            Directory.CreateDirectory(runtimeDir);
            Directory.CreateDirectory(sitePackages);
            scratchRoot = ArchiveExtractor.CreateScratch(Path.GetDirectoryName(Path.GetFullPath(runtimeDir))!, "tmp");
            started = true;

            var items = ledger.Items;
            var total = items.Count;
            var done = 0;

            // ---- ⑴ 埋め込み Python -------------------------------------------
            //
            // 変種の台帳（runtime-<変種>.json）は埋め込み Python を持たない。呼び手が
            // LedgerReader.WithPythonEmbed で足していれば item として来るし、足していなければ
            // 配布樹の ledger/python-embed.json から自分で読む（呼び手に順を強いない）。
            // **差分の回は当てない**（SkipPythonEmbed）＝相手の樹に python.exe が既に在る。
            LedgerItem? embed = null;
            if (!SkipPythonEmbed)
            {
                embed = ledger.PythonEmbed ?? TryReadPythonEmbed(request.AppDir);
                if (embed is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new InstallProgress(InstallPhase.PythonEmbed, embed.Name, done, total));
                    var embedPath = await VerifiedCachedPathAsync(request.CacheDir, embed, cancellationToken)
                        .ConfigureAwait(false);
                    ArchiveExtractor.ExtractZip(embedPath, runtimeDir, cancellationToken);
                    if (ledger.PythonEmbed is not null)
                    {
                        done++;
                    }
                }
            }

            var pythonExe = Path.Combine(runtimeDir, AppPaths.PythonExeName);
            if (!File.Exists(pythonExe))
            {
                return Fail(runtimeDir, started, dropped,
                    SkipPythonEmbed
                        ? "新しくなった分を当てる相手に python.exe が無い（先に丸ごと組むこと）。"
                        : embed is null
                            ? "埋め込み Python の台帳（ledger/python-embed.json）が配布樹に無い。"
                            : "埋め込み Python の zip に python.exe が無い（台帳か cache が壊れている）。");
            }

            // ---- ⑵ python312._pth --------------------------------------------
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress(InstallPhase.PthFile, PthWriter.PthFileName, done, total));
            var pthPath = _pthWriter.Write(request.PthTemplatePath, runtimeDir, request.AppDir);

            // ---- ⑶ wheel / sdist / archive -----------------------------------
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Kind == LedgerItemKinds.PythonEmbed)
                {
                    continue;
                }

                progress?.Report(new InstallProgress(InstallPhase.Packages, item.Name, done, total));
                var source = await VerifiedCachedPathAsync(request.CacheDir, item, cancellationToken)
                    .ConfigureAwait(false);

                // **版が上がった item は古い *.dist-info を先に落とす**（差分の回だけ）。
                // 被せるだけでは torch-1 と torch-2 が並び、締めの件数が必ず 1 件多くなる。
                if (ReplaceSupersededDistInfo)
                {
                    RemoveSupersededDistInfo(sitePackages, item.Name);
                }

                switch (item.Kind)
                {
                    case LedgerItemKinds.Wheel:
                        InstallWheel(source, sitePackages, dropped, cancellationToken);
                        break;

                    case LedgerItemKinds.Sdist:
                    case LedgerItemKinds.Archive:
                        await InstallSourceTreeAsync(
                            source, item, sitePackages, scratchRoot, cancellationToken).ConfigureAwait(false);
                        break;

                    default:
                        return Fail(runtimeDir, started, dropped,
                            "台帳の kind \"" + item.Kind + "\"（" + item.Name + "）は展開できない。");
                }

                done++;
            }

            // ---- ⑷ 検分 ------------------------------------------------------
            progress?.Report(new InstallProgress(InstallPhase.Verifying, null, done, total));

            var stray = ArchiveExtractor.TopLevelPthFiles(sitePackages);
            if (stray.Count > 0)
            {
                return Fail(runtimeDir, started, dropped,
                    "site-packages に *.pth が残っている（import site が無い環境では読まれない檔）："
                    + string.Join("・", stray.Select(Path.GetFileName)));
            }

            // **<name>-<ver>.data の残骸は失敗**（是正・2026-09-05・low 9）。
            // MergeDataDirectories は purelib／platlib を上げ scripts／headers／data を捨てるので、
            // 空になった dataDir だけを消す。残っている＝知らない小分類が入っていた、である。
            var residue = ResidualDataDirectories(sitePackages);
            if (residue.Count > 0)
            {
                return Fail(runtimeDir, started, dropped,
                    "wheel の .data が畳めずに残っている（知らない小分類が入っている）："
                    + string.Join("・", residue),
                    residue);
            }

            var distInfos = Directory.GetDirectories(sitePackages, "*.dist-info", SearchOption.TopDirectoryOnly).Length;

            // **数えた件数を台帳と突き合わせる**（是正・2026-09-05）。数えて報告するだけでは、
            // 檔が 1 件も入らなくても Ok=true になる（実射＝dist-info 1・台帳 2 で成功を名乗った）。
            // 差分の回（VerifyDistInfoCount=false）は**ここでは数えない**＝渡る台帳が切れ端で、
            // 樹には全件が居るので絶対に合わない。締めは RuntimeDiff.VerifyAfterApply が全件で撃つ。
            var expected = ExpectedDistInfoCount(items);
            if (VerifyDistInfoCount && distInfos != expected)
            {
                return Fail(runtimeDir, started, dropped, string.Create(CultureInfo.InvariantCulture,
                    $"展開の件数が台帳と合わない（*.dist-info {distInfos} 件・台帳は {expected} 件）。"));
            }

            var measured = ArchiveExtractor.Measure(runtimeDir);
            if (measured.Files <= 0)
            {
                return Fail(runtimeDir, started, dropped, "変種ディレクトリに檔が 1 つも入っていない。");
            }

            if (!File.Exists(pythonExe))
            {
                return Fail(runtimeDir, started, dropped, "組み上げた後に python.exe が見当たらない。");
            }

            if (pthPath is null || !File.Exists(pthPath))
            {
                return Fail(runtimeDir, started, dropped, PthWriter.PthFileName + " が書けていない。");
            }

            progress?.Report(new InstallProgress(InstallPhase.Done, null, done, total));
            return new InstallResult(true, measured.Files, measured.Bytes, distInfos, dropped, pthPath, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RemovePartial(runtimeDir, started);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or InvalidOperationException or InvalidDataException
                                       or LedgerException or FileNotFoundException)
        {
            progress?.Report(new InstallProgress(InstallPhase.Failed, null, 0, ledger.Items.Count));
            return Fail(runtimeDir, started, dropped, ex.Message);
        }
        finally
        {
            if (scratchRoot.Length > 0)
            {
                ArchiveExtractor.RemoveScratch(scratchRoot);
            }
        }
    }

    // ---- wheel --------------------------------------------------------------

    /// <summary>
    /// <c>.whl</c>（＝zip）を <c>site-packages/</c> へまるごと展開し、
    /// <c>&lt;name&gt;-&lt;ver&gt;.data</c> を畳んで <c>*.pth</c> を捨てる
    /// （<c>Install-YwkWheel</c> と 1 対 1）。
    /// </summary>
    public static void InstallWheel(
        string wheelPath,
        string sitePackages,
        IList<string> droppedPth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wheelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sitePackages);
        ArgumentNullException.ThrowIfNull(droppedPth);

        ArchiveExtractor.ExtractZip(wheelPath, sitePackages, cancellationToken);
        MergeDataDirectories(sitePackages, cancellationToken);
        DropInertPthFiles(sitePackages, droppedPth);
    }

    /// <summary>
    /// <c>site-packages/*.data</c> を畳む＝<c>purelib</c>／<c>platlib</c> は 1 段上へ、
    /// <c>scripts</c>／<c>headers</c>／<c>data</c> は捨てる（合成の経路が 1 つも読まないうえ、
    /// console script を PATH に置きたくない）。
    /// </summary>
    public static void MergeDataDirectories(string sitePackages, CancellationToken cancellationToken)
    {
        foreach (var dataDir in Directory.GetDirectories(sitePackages, "*.data", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var sub in new[] { "purelib", "platlib" })
            {
                var path = Path.Combine(dataDir, sub);
                if (Directory.Exists(path))
                {
                    ArchiveExtractor.MoveDirectoryContents(path, sitePackages, cancellationToken);
                }
            }

            foreach (var sub in new[] { "scripts", "headers", "data" })
            {
                var path = Path.Combine(dataDir, sub);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }

            if (Directory.GetFileSystemEntries(dataDir).Length == 0)
            {
                Directory.Delete(dataDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// <c>site-packages/</c> 直下の <c>*.pth</c> を捨てて名前を控える。
    /// <c>._pth</c> に <c>import site</c> が無い＝<c>site.py</c> が走らない＝<c>.pth</c> は
    /// 「何かしているように見えて何もしない檔」（<c>ledger/README.md</c> §4-4）。
    /// </summary>
    public static void DropInertPthFiles(string sitePackages, IList<string> droppedPth)
    {
        foreach (var pth in ArchiveExtractor.TopLevelPthFiles(sitePackages))
        {
            File.Delete(pth);
            droppedPth.Add(Path.GetFileName(pth));
        }
    }

    // ---- sdist / archive ----------------------------------------------------

    /// <summary>
    /// <c>sdist</c>／<c>archive</c> を作業場に展開し、<c>package_dir(s)</c> を
    /// <c>site-packages/</c> へ写して最小 <c>*.dist-info</c> を作る
    /// （<c>Install-YwkSourceTree</c> と 1 対 1）。
    /// </summary>
    public static async Task InstallSourceTreeAsync(
        string archivePath,
        LedgerItem item,
        string sitePackages,
        string scratchRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentNullException.ThrowIfNull(item);

        var packageDirs = item.EffectivePackageDirs;
        if (packageDirs.Count == 0)
        {
            throw new LedgerException(item.Name + " に package_dir も package_dirs も無い。");
        }

        var scratch = ArchiveExtractor.CreateScratch(scratchRoot, item.Name);
        try
        {
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ArchiveExtractor.ExtractZip(archivePath, scratch, cancellationToken);
            }
            else
            {
                await ArchiveExtractor.ExtractTarGzAsync(archivePath, scratch, cancellationToken)
                    .ConfigureAwait(false);
            }

            var root = ArchiveExtractor.SingleRootDirectory(scratch, Path.GetFileName(archivePath));

            var topLevel = new List<string>(packageDirs.Count);
            foreach (var packageDir in packageDirs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = Path.Combine(root, packageDir.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(source))
                {
                    throw new LedgerException(
                        "package_dir \"" + packageDir + "\" が " + Path.GetFileName(archivePath) + " に無い。");
                }

                var leaf = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar));
                ArchiveExtractor.CopyDirectory(source, Path.Combine(sitePackages, leaf), cancellationToken);
                topLevel.Add(leaf);
            }

            MinimalDistInfo.Write(
                sitePackages,
                item.Name,
                item.Version ?? "0",
                item.License,
                topLevel);
        }
        finally
        {
            ArchiveExtractor.RemoveScratch(scratch);
        }
    }

    // ---- 補助 ---------------------------------------------------------------

    /// <summary>
    /// 展開後に在るべき <c>*.dist-info</c> の件数（<b>純関数</b>）＝
    /// <c>wheel</c>＋<c>sdist</c>＋<c>archive</c> の item 数。埋め込み Python は数えない
    /// （<c>site-packages/</c> に何も置かない）。
    /// </summary>
    public static int ExpectedDistInfoCount(IReadOnlyList<LedgerItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var count = 0;
        foreach (var item in items)
        {
            if (item.Kind is LedgerItemKinds.Wheel or LedgerItemKinds.Sdist or LedgerItemKinds.Archive)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 配布樹の <c>ledger/python-embed.json</c> を読む（無ければ null）。
    /// 変種の台帳に埋め込み Python が混ざっていないときの受け皿。
    /// </summary>
    private static LedgerItem? TryReadPythonEmbed(string appDir)
    {
        try
        {
            return new LedgerReader(Path.Combine(appDir, "ledger")).ReadPythonEmbed().PythonEmbed;
        }
        catch (LedgerException)
        {
            return null;
        }
    }

    /// <summary>
    /// cache に落ちている原檔の在り処（<see cref="LedgerItem.EffectiveFileName"/> と同じ規則）を返し、
    /// <b>展開の直前に sha256 を台帳と突き合わせる</b>（是正・2026-09-05）。
    /// <para>
    /// 参照実装 <c>build/assemble-runtime.ps1</c> の <c>Get-YwkCachedItem</c>（146〜148 行）は
    /// <c>sha256</c> が空なら <b>throw</b> し、そうでなければ必ずハッシュを突き合わせる。
    /// 置き場は <c>%LOCALAPPDATA%\irodori-tts-ywk\cache</c>＝利用者が書けて回を跨いで残るので、
    /// 取得時の検証は<b>今回の展開を保証しない</b>。「同じ台帳・同じ規則で組む」という檔頭の
    /// 約束を守るには、ここで 1 回読むしかない（便 A の実走で 903 MB の展開は 10.8 s＝
    /// ハッシュ 1 巡は無視できる）。
    /// </para>
    /// </summary>
    private static async Task<string> VerifiedCachedPathAsync(
        string cacheDir, LedgerItem item, CancellationToken cancellationToken)
    {
        var path = Path.Combine(cacheDir, item.EffectiveFileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                item.Name + " の原檔が cache に無い（先に取得を済ませること）：" + item.EffectiveFileName, path);
        }

        var want = item.Sha256?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(want))
        {
            throw new LedgerException(
                "台帳の " + item.Name + " に sha256 が無い（素性の判らない檔は展開しない）。");
        }

        var have = await HttpDownloader.Sha256OfAsync(path, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(have, want, StringComparison.Ordinal))
        {
            throw new LedgerException(
                item.Name + " の原檔が台帳の sha256 と合わない（" + item.EffectiveFileName
                + "）。取得からやり直すこと。");
        }

        return path;
    }

    private InstallResult Fail(
        string runtimeDir,
        bool started,
        IReadOnlyList<string> dropped,
        string reason,
        IReadOnlyList<string>? dataResidue = null)
    {
        RemovePartial(runtimeDir, started);
        return new InstallResult(false, 0, 0, 0, dropped, null, reason)
        {
            DataResidue = dataResidue ?? [],
        };
    }

    /// <summary>
    /// <b>組み始める前に</b>台帳の全 item が cache に在り sha256 が合うことを 1 巡して確かめる
    /// （low 8）。落ちた理由 1 行を返す（健全なら null）。
    /// </summary>
    private async Task<string?> PreflightAsync(
        InstallRequest request, CancellationToken cancellationToken)
    {
        var items = new List<LedgerItem>(request.Ledger.Items.Count + 1);
        if (!SkipPythonEmbed
            && request.Ledger.PythonEmbed is null
            && TryReadPythonEmbed(request.AppDir) is { } embed)
        {
            items.Add(embed);
        }

        // 差分の回は埋め込み Python を 1 件も要求しない（当てないので原檔も要らない）。
        items.AddRange(SkipPythonEmbed
            ? request.Ledger.Items.Where(static i => i.Kind != LedgerItemKinds.PythonEmbed)
            : request.Ledger.Items);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await VerifiedCachedPathAsync(request.CacheDir, item, cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException ex)
            {
                return ex.Message;
            }
            catch (LedgerException ex)
            {
                return ex.Message;
            }
            catch (IOException ex)
            {
                return item.Name + " の原檔が読めない：" + ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                return item.Name + " の原檔が読めない：" + ex.Message;
            }
        }

        return null;
    }

    /// <summary>
    /// item の名を <c>*.dist-info</c> の綴りに揃える（<b>純関数</b>＝小文字・<c>-</c>／<c>_</c>／<c>.</c>
    /// の連なりは <c>_</c> 1 つ）。<see cref="MinimalDistInfo.DirectoryName"/> と同じ規則で、
    /// wheel 側の escape（PEP 503）にも合う。
    /// </summary>
    public static string NormalizeDistributionName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var text = new System.Text.StringBuilder(name.Length);
        var lastWasSeparator = false;
        foreach (var ch in name.Trim())
        {
            if (ch is '-' or '_' or '.')
            {
                if (!lastWasSeparator)
                {
                    text.Append('_');
                }

                lastWasSeparator = true;
                continue;
            }

            text.Append(char.ToLowerInvariant(ch));
            lastWasSeparator = false;
        }

        return text.ToString();
    }

    /// <summary>
    /// <c>site-packages</c> に居る<b>同じ名の <c>*.dist-info</c></b>（版は問わない）。
    /// <b>純関数に近い＝檔を読むだけ</b>で、消しはしない。
    /// </summary>
    public static IReadOnlyList<string> SupersededDistInfoDirectories(string sitePackages, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sitePackages);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!Directory.Exists(sitePackages))
        {
            return [];
        }

        var want = NormalizeDistributionName(name);
        return Directory
            .GetDirectories(sitePackages, "*.dist-info", SearchOption.TopDirectoryOnly)
            .Where(dir => string.Equals(DistributionNameOf(dir), want, StringComparison.Ordinal))
            .OrderBy(dir => dir, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary><c>&lt;name&gt;-&lt;ver&gt;.dist-info</c> の名の側（揃えた綴り・読めなければ null）。</summary>
    private static string? DistributionNameOf(string distInfoDir)
    {
        var leaf = Path.GetFileName(distInfoDir);
        if (leaf.Length <= DistInfoSuffix.Length)
        {
            return null;
        }

        var stem = leaf[..^DistInfoSuffix.Length];
        var dash = stem.LastIndexOf('-');
        return dash <= 0 ? null : NormalizeDistributionName(stem[..dash]);
    }

    private const string DistInfoSuffix = ".dist-info";

    /// <summary>同じ名の古い <c>*.dist-info</c> を落とす（差分の回だけ＝消せなければ黙って諦める）。</summary>
    private static void RemoveSupersededDistInfo(string sitePackages, string name)
    {
        foreach (var dir in SupersededDistInfoDirectories(sitePackages, name))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // 掴まれている＝締めの件数で落ちて丸ごとへ流れる（黙って混ぜない）
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// 畳み残った <c>*.data</c>（<b>純関数に近い＝檔を読むだけ</b>）。名前だけを返す。
    /// </summary>
    public static IReadOnlyList<string> ResidualDataDirectories(string sitePackages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sitePackages);
        if (!Directory.Exists(sitePackages))
        {
            return [];
        }

        return Directory
            .GetDirectories(sitePackages, "*.data", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private void RemovePartial(string runtimeDir, bool started)
    {
        if (!RemovePartialOnFailure || !started)
        {
            return;
        }

        try
        {
            if (Directory.Exists(runtimeDir))
            {
                Directory.Delete(runtimeDir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>組み上がりの 1 行（ログと報告）。</summary>
    public static string Describe(InstallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Ok)
        {
            return "組み上げに失敗した：" + (result.FailureReason ?? "理由不明");
        }

        var head = string.Create(CultureInfo.InvariantCulture,
            $"{result.Files} 檔・{FetchPlanner.FormatBytes(result.Bytes)}・dist-info {result.DistInfoCount} 件");
        return result.DroppedPth.Count > 0
            ? head + string.Create(CultureInfo.InvariantCulture, $"・捨てた .pth {result.DroppedPth.Count} 件")
            : head;
    }
}
