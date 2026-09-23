using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Services.Update;

/// <summary>更新の一手の結末（利用者への一行は <see cref="AboutViewModel"/> が綴る）。</summary>
public enum AppUpdateResultKind
{
    /// <summary>配布されている版と一致＝最新。</summary>
    UpToDate,

    /// <summary>版が違う＝新しい版がある（照合まで。まだ 1 バイトも落としていない）。</summary>
    UpdateAvailable,

    /// <summary>取得（配布情報・インストーラ）に失敗＝静かな不成立。</summary>
    CheckFailed,

    /// <summary>検分に通らなかった（名違い・sha256 不一致・https でない）＝適用しない。</summary>
    VerifyFailed,

    /// <summary>検分に通ったインストーラを起こした（以後このアプリは終了へ進む）。</summary>
    LaunchedInstaller,

    /// <summary>検分済みのインストーラを起こせなかった（<c>Detail</c>＝保存先＝手動実行の道順）。</summary>
    LaunchFailed,
}

/// <summary>更新の一手の結果（新しい版と起動成立は版の字を運ぶ）。</summary>
/// <param name="Kind">結末。</param>
/// <param name="NewVersion">配布されている表示版（判った回だけ）。</param>
/// <param name="Note">配布元からの一行（<b>表示のみ</b>）。</param>
/// <param name="Detail">一行に添える理由か保存先（無ければ null）。</param>
public sealed record AppUpdateResult(
    AppUpdateResultKind Kind,
    string? NewVersion = null,
    string? Note = null,
    string? Detail = null);

/// <summary>
/// 〔このアプリについて〕が更新経路へ触る口（VM のテストの継ぎ目。
/// <b>null＝未配線＝釦を押せない</b>＝本体 A-10 と同型）。
/// </summary>
public interface IAppUpdateGateway
{
    /// <summary>一押し目＝配布情報を取って版を照合するだけ（何も落とさない）。</summary>
    Task<AppUpdateResult> CheckAsync();

    /// <summary>
    /// 二押し目＝再照合 → インストーラ取得 → sha256 検分 → 保存 → 起動。
    /// <paramref name="confirmedVersion"/>＝一押し目で利用者に見せて承諾を得た版。
    /// 取り直した配布情報がこれと食い違えば適用せず「新しい版がある」へ倒す
    /// ＝<b>利用者が見ていない版を黙って入れない</b>。
    /// </summary>
    Task<AppUpdateResult> ApplyAsync(string confirmedVersion);
}

/// <summary>
/// アプリ内更新の実体（裁定 160・2026-09-24＝本体 yomiwakechan2 の
/// <c>UI/Services/Update/AppUpdateService.cs</c> を写した型）。
/// <para>
/// 流れ＝<b>着地頁の <c>app.json</c> を取る → 版を照合 → 新しければインストーラを取る →
/// sha256 で検分 → <c>&lt;データの家&gt;\updates\</c> へ置く → 起こす</b>。
/// このアプリを終わらせるのは<b>ここの仕事ではない</b>（VM に注入された Action が既存の
/// 終了の入口 1 本＝主窓の Closing → <c>App.OnExit</c> を通す）。
/// </para>
/// <para>
/// <b>契機は手動の釦だけ</b>（起動時も定期も確認しない＝この製品は「はじめの準備のときだけ
/// 外に出る」と約束している）。失敗は例外でも窓でもなく<b>一行の状態文</b>に畳む。
/// </para>
/// <para>
/// <b>上限は 2 つ</b>＝長さ <see cref="MaxInstallerBytes"/>（既定 200 MB）と
/// 見切り <see cref="InstallerTimeout"/>（既定 300 秒）。どちらも <c>init</c> で差し替えられる
/// ＝試験は小さな値で同じ道を通す。取得の継ぎ目は <see cref="HttpMessageHandler"/> で、
/// 起動の継ぎ目は <c>launchInstaller</c>（実プロセスを起こさずに撃てる）。
/// </para>
/// <para>
/// <b>取得系（<see cref="Ledger.HttpDownloader"/>）を使い回さない理由</b>＝あちらは台帳の 1 件を
/// 取る道具で、⑴ 長さの上限を持たない ⑵ 全体の見切りを持たない（無通信 60 秒だけ）
/// ⑶ sha256 が合わないと 5 回取り直す（200 MB を 5 回引く芽）＝更新には温度が合わない。
/// 小文字 hex の計算だけは <see cref="Ledger.HttpDownloader.Sha256OfAsync"/> を借りる
/// （同じ綴りの計算を 2 度書かない）。
/// </para>
/// </summary>
public sealed class AppUpdateService : IAppUpdateGateway, IDisposable
{
    /// <summary>配布情報の URL（<b>この 1 本だけ</b>＝着地頁の根・同梱の写しは作らない）。</summary>
    public const string ManifestUrl = "https://mugonkun.github.io/irodori-tts-for-yomiwakechan/app.json";

    /// <summary>配布情報の受け入れ上限（JSON 数 KB 想定の桁違い防衛）。</summary>
    public const int MaxManifestBytes = 1 * 1024 * 1024;

    /// <summary>インストーラの受け入れ上限の既定（200 MB。現行の setup は 3 MB 級）。</summary>
    public const long DefaultMaxInstallerBytes = 200L * 1024 * 1024;

    /// <summary>取ったインストーラの置き場の名（データの家の直下）。</summary>
    public const string UpdatesFolderName = "updates";

    /// <summary>読み書きの単位（<see cref="Ledger.HttpDownloader"/> と同じ 1 MiB）。</summary>
    private const int BufferSize = 1024 * 1024;

    private readonly string _currentVersion;
    private readonly ReleaseFlavor _flavor;
    private readonly string _updatesDir;
    private readonly string _manifestUrl;
    private readonly Func<string, bool> _launchInstaller;
    private readonly Action<string>? _log;
    private readonly HttpClient _client;
    private bool _disposed;

    /// <param name="currentVersion">
    /// 走っているアプリの表示版（<see cref="AppVersion.Display"/>）。
    /// <b>ここは版を作らない</b>＝受け取った字を配布情報の version と突き合わせるだけである。
    /// </param>
    /// <param name="flavor">この配布物の版（<c>installers</c> のどちらを読むか）。</param>
    /// <param name="updatesDir">取った物の置き場（<see cref="AppPaths.UpdatesDir"/>）。</param>
    /// <param name="handler">HTTP の継ぎ目（テストのフェイク。null＝実通信）。</param>
    /// <param name="manifestUrl">配布情報 URL の差し替え口（テスト用。null＝<see cref="ManifestUrl"/>）。</param>
    /// <param name="launchInstaller">起動の継ぎ目（テスト用。null＝実起動）。</param>
    /// <param name="log">記録の 1 行（null＝残さない）。</param>
    public AppUpdateService(
        string currentVersion,
        ReleaseFlavor flavor,
        string updatesDir,
        HttpMessageHandler? handler = null,
        string? manifestUrl = null,
        Func<string, bool>? launchInstaller = null,
        Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(updatesDir);

        _currentVersion = currentVersion;
        _flavor = flavor;
        _updatesDir = updatesDir;
        _manifestUrl = string.IsNullOrWhiteSpace(manifestUrl) ? ManifestUrl : manifestUrl;
        _launchInstaller = launchInstaller ?? LaunchInstallerProcess;
        _log = log;

        // 継ぎ目の handler は**こちらで捨てない**（試験が 1 本を使い回す）。
        _client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);

        // 見切りは呼び出しごとの CTS で掛ける（用途で秒が違う）。
        _client.Timeout = Timeout.InfiniteTimeSpan;
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("irodori-tts-ywk-launcher/1");
    }

    /// <summary>配布情報の見切り（既定 15 秒＝数 KB の JSON）。</summary>
    public TimeSpan ManifestTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>インストーラの見切り（既定 300 秒）。</summary>
    public TimeSpan InstallerTimeout { get; init; } = TimeSpan.FromSeconds(300);

    /// <summary>インストーラの長さの上限（既定 <see cref="DefaultMaxInstallerBytes"/>）。</summary>
    public long MaxInstallerBytes { get; init; } = DefaultMaxInstallerBytes;

    /// <inheritdoc />
    public async Task<AppUpdateResult> CheckAsync()
    {
        try
        {
            var (manifest, failure) = await FetchManifestAsync().ConfigureAwait(false);
            return manifest is null ? failure! : CompareVersion(manifest);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unexpected(ex);
        }
    }

    /// <inheritdoc />
    public async Task<AppUpdateResult> ApplyAsync(string confirmedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmedVersion);
        try
        {
            var (manifest, failure) = await FetchManifestAsync().ConfigureAwait(false);
            if (manifest is null)
            {
                return failure!;
            }

            // ⑴ 再照合（この間に配布が戻った・別の道で入れ替わった＝最新へ倒す）。
            if (string.Equals(manifest.Version, _currentVersion, StringComparison.Ordinal))
            {
                return new AppUpdateResult(AppUpdateResultKind.UpToDate, manifest.Version);
            }

            // ⑵ 承諾した版の検査（二押しの間に配布が先へ進んだ＝見ていない版は入れない）。
            if (!string.Equals(manifest.Version, confirmedVersion, StringComparison.Ordinal))
            {
                return CompareVersion(manifest);
            }

            // ⑶ 取得先の検査（中身は sha256 が守るが、取りに行くのは https だけ）。
            if (!manifest.InstallerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return new AppUpdateResult(
                    AppUpdateResultKind.VerifyFailed, manifest.Version, Detail: "取得先が https ではありません");
            }

            // ⑷ 取得 → sha256 検分 → 保存。
            var (path, downloadFailure) = await DownloadInstallerAsync(manifest).ConfigureAwait(false);
            if (path is null)
            {
                return downloadFailure!;
            }

            // ⑸ 起動（ここだけ個別の網＝起こせなかったことを保存先つきで言う）。
            bool launched;
            try
            {
                launched = _launchInstaller(path);
            }
            catch (Exception ex)
            {
                Log("更新: インストーラを起こせませんでした（" + ex.GetType().Name + "）");
                launched = false;
            }

            if (!launched)
            {
                return new AppUpdateResult(AppUpdateResultKind.LaunchFailed, manifest.Version, Detail: path);
            }

            Log("更新: インストーラを起こしました（" + manifest.Version + "）");
            return new AppUpdateResult(AppUpdateResultKind.LaunchedInstaller, manifest.Version);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unexpected(ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }

    /// <summary>版の照合（<b>不一致＝新しい版がある</b>。順序比較をしない）。</summary>
    private AppUpdateResult CompareVersion(AppDistributionManifest manifest) =>
        string.Equals(manifest.Version, _currentVersion, StringComparison.Ordinal)
            ? new AppUpdateResult(AppUpdateResultKind.UpToDate, manifest.Version)
            : new AppUpdateResult(AppUpdateResultKind.UpdateAvailable, manifest.Version, manifest.Note);

    /// <summary>
    /// 配布情報の取得と検分（name の一致まで）。失敗は <c>Manifest=null</c>＋理由の一行に畳んで返す。
    /// </summary>
    private async Task<(AppDistributionManifest? Manifest, AppUpdateResult? Failure)> FetchManifestAsync()
    {
        var bytes = await FetchTextAsync(_manifestUrl).ConfigureAwait(false);
        if (bytes is null)
        {
            return (null, new AppUpdateResult(
                AppUpdateResultKind.CheckFailed, Detail: "更新の情報を取りにいけませんでした"));
        }

        var (manifest, error) = AppDistributionContract.ParseManifest(
            Encoding.UTF8.GetString(bytes), _flavor);
        if (manifest is null)
        {
            Log("更新: 配布の情報を読めません（" + error + "）");
            return (null, new AppUpdateResult(
                AppUpdateResultKind.CheckFailed, Detail: "更新の情報を読めませんでした"));
        }

        if (!string.Equals(manifest.Name, AppDistributionContract.AppName, StringComparison.Ordinal))
        {
            return (null, new AppUpdateResult(
                AppUpdateResultKind.VerifyFailed, Detail: "更新の情報が別のアプリのものです"));
        }

        return (manifest, null);
    }

    /// <summary>短い本文を取る（失敗＝null・例外は投げない）。</summary>
    private async Task<byte[]?> FetchTextAsync(string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(ManifestTimeout);
            using var response = await _client
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log("更新: 配布の情報を取れませんでした（HTTP "
                    + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + "）");
                return null;
            }

            if (response.Content.Headers.ContentLength > MaxManifestBytes)
            {
                Log("更新: 配布の情報が大きすぎます");
                return null;
            }

            await response.Content.LoadIntoBufferAsync(MaxManifestBytes, cts.Token).ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException
                                       or InvalidOperationException or ObjectDisposedException)
        {
            Log("更新: 配布の情報を取れませんでした（" + ex.GetType().Name + "）");
            return null;
        }
    }

    /// <summary>
    /// インストーラを取って検分して置く（<b>置き場は先に掃除する</b>＝版違いを溜めない）。
    /// <para>
    /// 書くのは <c>.part</c> で、sha256 が合ってから 1 手で本名にする
    /// （<see cref="Ledger.HttpDownloader"/> と同じ作法）＝<b>不一致の檔は本名で残らない</b>。
    /// </para>
    /// </summary>
    private async Task<(string? Path, AppUpdateResult? Failure)> DownloadInstallerAsync(
        AppDistributionManifest manifest)
    {
        string path;
        string part;
        try
        {
            Directory.CreateDirectory(_updatesDir);
            CleanUpdatesFolder();
            path = Path.Combine(_updatesDir, InstallerFileName(manifest.Version, _flavor));
            part = path + ".part";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log("更新: 置き場を用意できませんでした（" + ex.GetType().Name + "）");
            return (null, new AppUpdateResult(
                AppUpdateResultKind.CheckFailed, manifest.Version, Detail: "置き場を用意できませんでした"));
        }

        try
        {
            using var cts = new CancellationTokenSource(InstallerTimeout);
            using var response = await _client
                .GetAsync(manifest.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log("更新: 更新ファイルを取れませんでした（HTTP "
                    + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + "）");
                return (null, Fetching(manifest));
            }

            // 申告の時点で上限を超えていれば 1 バイトも読まない。
            if (response.Content.Headers.ContentLength > MaxInstallerBytes)
            {
                Log("更新: 更新ファイルが大きすぎます");
                return (null, TooLarge(manifest));
            }

            var overflowed = false;
            await using (var file = new FileStream(
                part, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            await using (var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
            {
                var buffer = new byte[BufferSize];
                var received = 0L;
                while (true)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token)
                        .ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    received += read;

                    // 長さを申告しない相手でも際限なく飲まない（上限を超えたらその場で止める）。
                    if (received > MaxInstallerBytes)
                    {
                        overflowed = true;
                        break;
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), cts.Token).ConfigureAwait(false);
                }

                await file.FlushAsync(cts.Token).ConfigureAwait(false);
            }

            if (overflowed)
            {
                Log("更新: 更新ファイルが大きすぎます");
                SafeDelete(part);
                return (null, TooLarge(manifest));
            }

            // sha256 の検分（合わなければ**置かずに消す**＝嘘の成功を返さない）。
            var actual = await Ledger.HttpDownloader
                .Sha256OfAsync(part, CancellationToken.None).ConfigureAwait(false);
            if (!actual.Equals(manifest.InstallerSha256, StringComparison.OrdinalIgnoreCase))
            {
                Log("更新: 取ったファイルが配布の情報と一致しません");
                SafeDelete(part);
                return (null, new AppUpdateResult(
                    AppUpdateResultKind.VerifyFailed, manifest.Version,
                    Detail: "ダウンロードしたファイルの中身が配布元のものと違います"));
            }

            File.Move(part, path, overwrite: true);
            return (path, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException
                                       or UnauthorizedAccessException or InvalidOperationException
                                       or ObjectDisposedException)
        {
            Log("更新: 更新ファイルを取れませんでした（" + ex.GetType().Name + "）");
            SafeDelete(part);
            return (null, Fetching(manifest));
        }
    }

    private static AppUpdateResult Fetching(AppDistributionManifest manifest) => new(
        AppUpdateResultKind.CheckFailed, manifest.Version, Detail: "更新ファイルを取りにいけませんでした");

    private static AppUpdateResult TooLarge(AppDistributionManifest manifest) => new(
        AppUpdateResultKind.CheckFailed, manifest.Version, Detail: "更新ファイルが大きすぎます");

    /// <summary>想定外も窓にしない＝静かな不成立の一行へ畳む。</summary>
    private AppUpdateResult Unexpected(Exception ex)
    {
        Log("更新: 更新の手が落ちました（" + ex.GetType().Name + ": " + ex.Message + "）");
        return new AppUpdateResult(AppUpdateResultKind.CheckFailed, Detail: "更新の手が落ちました");
    }

    /// <summary>
    /// 置く檔の名（<b>純関数</b>）＝配布物と同じ形。版の字に檔名へ使えない物が混じっていても
    /// 保存を人質に取らせない（'-' へ落とす）。
    /// </summary>
    public static string InstallerFileName(string version, ReleaseFlavor flavor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var safe = version;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, '-');
        }

        return "irodori-tts-ywk-setup-" + safe + "-" + AppDistributionContract.FlavorKey(flavor) + ".exe";
    }

    /// <summary>古い残骸の掃除（best-effort＝消せない物が在っても更新は続ける）。</summary>
    private void CleanUpdatesFolder()
    {
        foreach (var file in Directory.GetFiles(_updatesDir))
        {
            SafeDelete(file);
        }

        foreach (var directory in Directory.GetDirectories(_updatesDir))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log("更新: 古い置き場を消せませんでした（" + ex.GetType().Name + "）");
            }
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 消せなくても次の取得が上書きする。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// 既定の起動（シェル実行＝per-user のインストーラなので昇格は要らない）。false＝起こせなかった。
    /// </summary>
    private static bool LaunchInstallerProcess(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return process is not null;
    }

    private void Log(string line) => _log?.Invoke(line);
}
