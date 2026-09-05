using System;
using System.IO;
using System.Security.Cryptography;
using IrodoriTtsYwk.Launcher.Contracts;

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
            return new Verdict(
                "配布物の取得台帳（" + RuntimeVariants.LedgerName(variant)
                + ".json）が、いまの実行系を展開したときの台帳と違います。実行系を組み直してください。",
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
                Note: "この実行系はランチャ " + storedAppVersion.Trim() + " で展開した物です（いまは "
                    + currentAppVersion.Trim() + "）。取得台帳は同じなので、そのまま使えます。");
        }

        return Verdict.Silent;
    }

    /// <summary>
    /// いまの配布樹の値を settings へ焼く（展開が通った直後に呼ぶ）。
    /// <b>焼くのはその変種の欄だけ</b>＝他の変種の焼き印には触らない。
    /// </summary>
    public static void Burn(LauncherSettings settings, AppPaths paths, string variant)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        settings.SetRuntimeStamp(variant, LedgerSha256(paths, variant), AppVersion.Display);
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

    /// <summary>展開が途中で切れている樹に出す 1 行（<b>純関数</b>）。</summary>
    public static string IncompleteLine(string variant) =>
        "実行系（" + RuntimeVariants.DisplayName(variant)
        + "）が途中までしか組み上がっていません。実行系を組み直してください。";
}
