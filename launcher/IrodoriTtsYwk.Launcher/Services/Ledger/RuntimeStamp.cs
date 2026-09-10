using System;
using System.IO;
using System.Security.Cryptography;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Services.Ledger;

/// <summary>
/// 展開に使った台帳の焼き印（裁定 91）＝<c>settings.json</c> の
/// <c>runtimeLedgerSha256</c>／<c>installedAppVersion</c> と、配布樹の台帳の突合。
/// <para>
/// <b>なぜ要るか</b>＝<see cref="AppPaths.ResolvePythonExe"/> は変種ディレクトリに
/// <c>python.exe</c> が在るかしか見ない。インストーラで新しい版に入れ替えても、
/// 利用者データ側の <c>runtime\&lt;変種&gt;\</c> は<b>前の版のまま残る</b>（利用者データは
/// アンインストールでも消さない＝便 E）。台帳が変われば site-packages の中身も変わるので、
/// <b>「見つかったから使う」だけでは腐った実行系を起こし続ける</b>。
/// </para>
/// <para>
/// <b>判定は純関数</b>（<see cref="Compare"/>）で、檔に触るのは <see cref="Sha256OfFile"/> だけ。
/// </para>
/// </summary>
public static class RuntimeStamp
{
    /// <summary>
    /// <b>展開に使った台帳そのものの写し</b>の檔名（<c>v2-spec.md</c> §11-3）。
    /// <para>
    /// 置き場＝<c>&lt;データ樹&gt;\runtime\&lt;variant&gt;\.ledger.json</c>＝
    /// RTX（CUDA）版なら <c>%LOCALAPPDATA%\irodori-tts-ywk-cuda\runtime\…</c>、
    /// Radeon（ROCm）版なら <c>…-radeon\runtime\…</c>（<b>データ樹は版ごと</b>＝
    /// <c>decisions.md</c> 133＝§11-8）。<b>配布樹には 1 檔も書かない。</b>
    /// 2 つの版が同じ機体に入っていても互いに見えない＝差分の判定も版ごとに独立する。
    /// </para>
    /// <para>
    /// 焼き印（<c>settings.runtimeLedgers</c>）が持つのは sha256 だけで、
    /// <b>「どの item がどの sha256 で入ったか」は残らない</b>＝差分が組めない。この写しが材料である。
    /// </para>
    /// </summary>
    public const string AppliedLedgerFileName = ".ledger.json";

    /// <summary>
    /// 写しの路（<b>純関数に近い薄い殻</b>＝変種ディレクトリが未解決なら
    /// <c>&lt;RuntimeRoot&gt;\&lt;variant&gt;</c> を使う）。
    /// </summary>
    public static string AppliedLedgerPath(AppPaths paths, string variant)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        var runtimeDir = paths.ResolveRuntimeDir(variant)
                         ?? Path.Combine(paths.RuntimeRoot, variant);
        return Path.Combine(runtimeDir, AppliedLedgerFileName);
    }

    /// <summary>
    /// 写しを読む（無い・壊れている＝null＝<b>差分を組めない</b>＝その 1 回だけ丸ごと）。
    /// </summary>
    public static LedgerFile? ReadAppliedLedger(AppPaths paths, string variant)
    {
        var path = AppliedLedgerPath(paths, variant);
        try
        {
            return File.Exists(path)
                ? LedgerReader.ParseRuntime(File.ReadAllText(path), RuntimeVariants.LedgerName(variant))
                : null;
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

    /// <summary>
    /// 配布樹の台帳を写しへ置く（<b><see cref="Burn"/> と同じ回</b>＝片方だけ残さない）。
    /// <b>展開が全部終わってから</b>呼ぶこと（途中で落ちた回は次回また全差分になる）。
    /// 戻り＝置けたか（置けなくても起動は止めない＝次の版で丸ごとになるだけ）。
    /// </summary>
    public static bool BurnAppliedLedger(AppPaths paths, string variant)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        var source = paths.LedgerPath(RuntimeVariants.LedgerName(variant));
        var destination = AppliedLedgerPath(paths, variant);
        try
        {
            if (!File.Exists(source))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
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
    }

    /// <summary>
    /// <b>写しを落とす</b>（是正・2026-09-11・medium 4）＝差分の当て込みが<b>檔を書いたあとで</b>
    /// 落ちた回に撃つ。
    /// <para>
    /// そのとき樹は新旧が混ざっているのに、写しは<b>混ざる前の姿</b>を名乗ったまま残る＝
    /// 次の <see cref="RuntimeDiff.Plan"/> がその嘘を信じ、もう入れ替わっている item を
    /// 「変わっていない」と見送って<b>混ざりを温存する</b>。写しを落としておけば、
    /// 次の回は歯止め ⑴ で丸ごと組み直しへ落ちる＝素性の知れない樹には、それが正しい。
    /// </para>
    /// 戻り＝落としたか（無かった・落とせなかったなら偽＝起動は止めない）。
    /// </summary>
    public static bool RemoveAppliedLedger(AppPaths paths, string variant)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        var path = AppliedLedgerPath(paths, variant);
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
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

    /// <summary>
    /// 台帳 1 檔の sha256（小文字 hex）。読めなければ null＝<b>黙る</b>
    /// （配布樹が読めない機体で「組み直せ」と急かさない）。
    /// </summary>
    public static string? Sha256OfFile(string path)
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
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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

    /// <summary>配布樹の <c>ledger/runtime-&lt;変種&gt;.json</c> の sha256（読めなければ null）。</summary>
    public static string? LedgerSha256(AppPaths paths, string variant)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        return Sha256OfFile(paths.LedgerPath(RuntimeVariants.LedgerName(variant)));
    }

    /// <summary>突合の結末（<see cref="Line"/> が null なら 1 手を出さない）。</summary>
    /// <param name="Line">
    /// 状態帯に出して<b>「実行系を組み直す」1 手を添える</b>行（合っている・判らないなら null）。
    /// </param>
    /// <param name="LedgerChanged">台帳が変わった（＝中身が別物）。</param>
    /// <param name="AppVersionChanged">ランチャの版だけが変わった。</param>
    /// <param name="Note">
    /// <b>1 手を添えない</b>覚え書き（ログに 1 度だけ流す）。版だけが動いたときの
    /// 「そのまま使えます」はこちら＝<b>4 GB の組み直しを毎起動勧めない</b>
    /// （是正・便 D（3）の 3 巡目）。
    /// </param>
    public sealed record Verdict(
        string? Line, bool LedgerChanged, bool AppVersionChanged, string? Note = null)
    {
        /// <summary>何も出さない結末。</summary>
        public static readonly Verdict Silent = new(null, false, false);

        /// <summary>「実行系を組み直す」を出すか。</summary>
        public bool Mismatch => Line is not null;
    }

    /// <summary>
    /// 焼き印と配布樹を突き合わせる（<b>純関数</b>）。
    /// <list type="number">
    /// <item>実行系がまだ無い＝初回取得の仕事＝<b>黙る</b>（ウィザードが出る）。</item>
    /// <item>配布樹の台帳が読めない＝<b>黙る</b>（推測で急かさない）。</item>
    /// <item>焼き印が無い（古い <c>settings.json</c>・台本で組んだ樹）＝<b>黙る</b>＝
    /// 「いつ組んだか判らない」だけで腐っている証拠ではない（便 D（2）以前の配布と混ぜても
    /// 壊れない＝§20-5 ⑵ と同じ流儀）。</item>
    /// <item>台帳の sha256 が違う＝<b>組み直す</b>。</item>
    /// <item>台帳は同じで版だけ違う＝<b><see cref="Verdict.Note"/> だけ</b>（1 手は出さない）。
    /// 1 手を出していたころ（便 D（3）の 1 巡目）は、<c>CheckRuntimeStamp</c> が焼き印の空欄しか
    /// 焼き直さないので <c>installedAppVersion</c> が永久に古いまま残り、ランチャを更新した機体は
    /// <b>毎起動</b>「実行系を組み直す」を見せられた（消す手立ては 4 GB の再展開だけ＝しかも
    /// 裁定 90 で cache は空なので実際は数 GiB の再取得）。</item>
    /// </list>
    /// </summary>
    /// <param name="storedLedgerSha256">settings の <c>runtimeLedgerSha256</c>。</param>
    /// <param name="storedAppVersion">settings の <c>installedAppVersion</c>。</param>
    /// <param name="currentLedgerSha256">配布樹の台帳の sha256（読めなければ null）。</param>
    /// <param name="currentAppVersion">いまのランチャの版。</param>
    /// <param name="runtimeInstalled">変種ディレクトリに <c>python.exe</c> が在るか。</param>
    /// <param name="variant">台帳の綴り（文言に出す）。</param>
    public static Verdict Compare(
        string? storedLedgerSha256,
        string? storedAppVersion,
        string? currentLedgerSha256,
        string? currentAppVersion,
        bool runtimeInstalled,
        string variant)
    {
        if (!runtimeInstalled || currentLedgerSha256 is null || string.IsNullOrWhiteSpace(storedLedgerSha256))
        {
            return Verdict.Silent;
        }

        if (!string.Equals(storedLedgerSha256.Trim(), currentLedgerSha256, StringComparison.OrdinalIgnoreCase))
        {
            // 画面に出る 1 行は UiStrings が綴る（段 F＝段 C の申し送り ⑵）。
            // 台帳の檔名（runtime-cu130.json）は**記録の側の綴り**なので画面へは出さない。
            return new Verdict(
                UiStrings.StatusRebuildLedgerChanged,
                LedgerChanged: true,
                AppVersionChanged: false);
        }

        if (!string.IsNullOrWhiteSpace(storedAppVersion)
            && !string.IsNullOrWhiteSpace(currentAppVersion)
            && !string.Equals(storedAppVersion.Trim(), currentAppVersion.Trim(), StringComparison.Ordinal))
        {
            return new Verdict(
                Line: null,
                LedgerChanged: false,
                AppVersionChanged: true,
                // Note は**記録にしか落ちない**（MainViewModel.CheckRuntimeStamp が AppendLog へ渡す）＝
                // 版の綴りを残してよい面である。画面に出す言い直しは UiStrings が持つ。
                Note: UiStrings.StatusAppVersionChangedLog + "（"
                    + storedAppVersion.Trim() + " → " + currentAppVersion.Trim() + "）");
        }

        return Verdict.Silent;
    }

    /// <summary>
    /// いまの配布樹の値を settings へ焼く（展開が通った直後に呼ぶ）。
    /// <b>焼くのはその変種の欄だけ</b>＝他の変種の焼き印には触らない。
    /// <para>
    /// <b>同じ回で <c>.ledger.json</c> も置く</b>（<c>v2-spec.md</c> §11-3＝片方だけ残さない）。
    /// 次の版の差分はこの写しと配布樹の台帳を突き合わせて組む（<see cref="RuntimeDiff.Plan"/>）。
    /// </para>
    /// <para>
    /// <b><paramref name="writeAppliedLedger"/> を偽にする場面が 1 つある</b>（是正・2026-09-11）＝
    /// <b>展開をこの回にしていない</b>呼び（裁定 91 の受け入れ＝
    /// <c>MainViewModel.CheckRuntimeStamp</c>）。あそこが見ているのは <see cref="LooksComplete"/>
    /// （<c>*.dist-info</c> の件数）だけで、その樹を<b>どの台帳で</b>組んだかは判っていない。
    /// sha256 の焼き印だけなら「催促を止める」で済むが、<c>.ledger.json</c> を置くのは
    /// <b>「この樹の中身は全部この sha256 である」と名乗る</b>行為で、それは嘘になりうる＝
    /// 次の版の <see cref="RuntimeDiff.Plan"/> がその嘘を信じ、本当は古い wheel を「変わっていない」と
    /// 見送って<b>混ざった樹</b>を残す。写しが<b>無い</b>回は
    /// <see cref="RuntimeDiff.Plan"/> が丸ごと組み直しへ落ちる＝素性の知れない樹には、それが正しい。
    /// </para>
    /// </summary>
    /// <param name="settings">焼き印を持つ設定。</param>
    /// <param name="paths">置き場。</param>
    /// <param name="variant">変種。</param>
    /// <param name="writeAppliedLedger">
    /// <c>.ledger.json</c> も置くか。<b>展開が本当に通った回だけ真</b>（既定）。
    /// </param>
    public static void Burn(
        LauncherSettings settings, AppPaths paths, string variant, bool writeAppliedLedger = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        settings.SetRuntimeStamp(variant, LedgerSha256(paths, variant), AppVersion.Display);
        if (writeAppliedLedger)
        {
            BurnAppliedLedger(paths, variant);
        }
    }

    /// <summary>
    /// 台帳は同じで<b>版だけ</b>が動いたときの焼き直し（展開はしない）。
    /// これを撃たないと <c>installedAppVersion</c> が永久に古いまま残る。
    /// </summary>
    public static void BurnAppVersion(LauncherSettings settings, string variant)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        settings.SetRuntimeStamp(
            variant, settings.RuntimeLedgerFor(variant), AppVersion.Display);
    }

    /// <summary>
    /// <b>展開が本当に済んでいる樹か</b>（<c>site-packages\*.dist-info</c> の件数が台帳と合うか）。
    /// <para>
    /// <c>python.exe</c> の在否だけを根拠に焼き印を押すと、<b>python-embed の直後で切れた樹</b>
    /// （<c>python.exe</c> 1 檔だけ）でも「この台帳から出来ている」と名乗り、取得キャッシュの
    /// 関門は自分で作ったその値と突き合わせるだけなので通ってしまう＝<b>壊れた樹を直すのに要る
    /// 原檔が消える</b>（是正・便 D（3）の 3 巡目）。<c>WheelInstaller</c> が展開の最後に撃つ
    /// のと同じ数え方（<see cref="WheelInstaller.ExpectedDistInfoCount"/>）で締める。
    /// </para>
    /// <para>
    /// 台帳が読めない・置き場が読めないときは<b>真</b>を返す（＝黙る）。推測で
    /// 「組み直せ」と急かさないのは <see cref="Compare"/> と同じ流儀である。
    /// </para>
    /// </summary>
    public static bool LooksComplete(AppPaths paths, LedgerFile? ledger, string variant)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        if (ledger?.Items is null || ledger.Items.Count == 0)
        {
            return true;
        }

        var runtimeDir = paths.ResolveRuntimeDir(variant);
        if (runtimeDir is null)
        {
            return true;
        }

        var sitePackages = Path.Combine(runtimeDir, "site-packages");
        try
        {
            if (!Directory.Exists(sitePackages))
            {
                return false;
            }

            var found = Directory
                .GetDirectories(sitePackages, "*.dist-info", SearchOption.TopDirectoryOnly).Length;
            return found == WheelInstaller.ExpectedDistInfoCount(ledger.Items);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// 展開が途中で切れている樹に出す 1 行（<b>純関数</b>）。
    /// <paramref name="variant"/> は<b>読まない</b>＝画面に動かし方の綴りを出さない（憲章 §6-1）。
    /// 引数は残す（呼び手 2 箇所の形と、記録側の呼びを変えないため）。
    /// </summary>
    public static string IncompleteLine(string variant) => UiStrings.StatusRebuildIncomplete;
}
