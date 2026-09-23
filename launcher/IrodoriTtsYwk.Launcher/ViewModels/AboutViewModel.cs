using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Mvvm;
using IrodoriTtsYwk.Launcher.Services.Update;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// このアプリについて（裁定 1＝<b>非公式・Aratako 氏とは無関係</b>を冒頭に出す）。
/// <para>
/// 出すのは 4 つ＝⑴ <b>非公式である旨</b>（裁定 1・README 冒頭と同じ文言）⑵ <b>版</b>
/// （<c>AppDisplayVersion</c>＝1 箇所の定義）⑶ <b>上流 pin</b>（publish でだけ焼かれる＝
/// 開発ビルドでは「開発ビルド」と名乗る）⑷ <b>ライセンスの所在</b>（配布樹の <c>licenses/</c>）。
/// </para>
/// <para>
/// <b>版の行の下に更新経路が 1 本通っている</b>（裁定 160・2026-09-24＝本体 yomiwakechan2 の
/// バージョン情報タブと同型）＝<b>二段確認</b>で、一押し目は照合だけ・二押し目で取得と検分と
/// インストーラの起動まで行く。<b>ここは Process も Application も知らない</b>＝終わらせる手は
/// 主窓が注入する（<see cref="AttachUpdater"/>）。未配線なら釦は押せない。
/// </para>
/// </summary>
public sealed class AboutViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private IAppUpdateGateway? _updater;
    private Action? _exitForUpdate;
    private Func<bool>? _isBusy;
    private string _updateStatusText = string.Empty;
    private string? _pendingVersion;
    private bool _launched;

    public AboutViewModel(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;

        UpdateCommand = new AsyncRelayCommand(UpdateAsync, () => _updater is not null && !_launched);

        // 更新経路は結果で返す作りだが、握り潰さない口は残す（§20-5 ⑴）＝
        // 想定外が出ても窓ではなく一行に畳む。
        UpdateCommand.Faulted += (_, line) =>
            UpdateStatusText = UiStrings.AboutUpdateFailedPrefix + line + UiStrings.AboutUpdateFailedSuffix;
    }

    /// <summary>裁定 1（README 冒頭と同じ）。</summary>
    public static string Disclaimer => UiStrings.Disclaimer;

    /// <summary>裁定 9（透かしは既定 ON・切る経路を持たない）。</summary>
    public static string WatermarkNotice => UiStrings.WatermarkNotice;

    /// <summary>README §4（Ethical Restrictions 1・No Impersonation）。</summary>
    public static string EthicsNotice => VoicesViewModel.ImpersonationNotice;

    /// <summary>
    /// 〔このアプリについて〕の「バージョン」＝<b>番号 ＋ 版の名札</b>
    /// （<c>v2.0.1 － RTX（CUDA）</c>・`v2-copy.md` §1-8 の <c>:39</c>／§8）。
    /// <para>
    /// <b>主窓の隅は番号だけ</b>（`v2-copy.md` §1-1 の 33 行目）＝そちらは
    /// <see cref="AppVersion.Display"/> を直に読む。名札を綴るのは
    /// <see cref="ReleaseFlavors.FlavorLabel"/> ただ 1 箇所である（`v2-plan.md` F-1 の規則）。
    /// </para>
    /// </summary>
    public static string VersionText(ReleaseFlavor flavor) =>
        ReleaseFlavors.Decorate(AppVersion.Display, flavor);

    /// <summary>上流 pin（publish でだけ焼かれる）。</summary>
    public static string UpstreamText => AppVersion.IsReleaseBuild
        ? "Irodori-TTS " + AppVersion.UpstreamIrodoriTts
          + "／Irodori-TTS-Server " + AppVersion.UpstreamIrodoriTtsServer
        : "開発ビルド（上流 pin は焼かれていません）";

    /// <summary>ライセンス文の在り処（配布樹・読むだけ）。</summary>
    public string LicensesDirText => _paths.LicensesDir;

    /// <summary>初回取得の通知文（裁定 46＝配布物に入る）。</summary>
    public string NoticesPathText => _paths.FirstRunNoticesPath;

    /// <summary>実際に置かれている許諾の束（無ければ空＝開発起動）。</summary>
    public IReadOnlyList<string> LicenseFolders()
    {
        try
        {
            if (!Directory.Exists(_paths.LicensesDir))
            {
                return [];
            }

            var names = Directory.GetDirectories(_paths.LicensesDir);
            Array.Sort(names, StringComparer.Ordinal);
            var result = new List<string>(names.Length);
            foreach (var name in names)
            {
                result.Add(Path.GetFileName(name));
            }

            return result;
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

    // ---- 更新（裁定 160・2026-09-24）--------------------------------------------

    /// <summary>
    /// 版の行の下の釦（<b>二段確認</b>＝一押し目は照合だけ・二押し目で取得と起動）。
    /// <para>
    /// <see cref="AsyncRelayCommand"/> なので<b>走っている間は押せない</b>。
    /// 未配線（<see cref="AttachUpdater"/> を通していない）なら最初から押せない。
    /// インストーラを起こしたあとも<b>起こし直さない</b>＝終了までの窓で二重に起こす芽を残さない。
    /// </para>
    /// </summary>
    public AsyncRelayCommand UpdateCommand { get; }

    /// <summary>釦の札（控えが無ければ「新しい版を確認」・照合済みなら「vX に更新する」）。</summary>
    public string UpdateButtonText => _pendingVersion is null
        ? UiStrings.AboutUpdateCheckButton
        : _pendingVersion + UiStrings.AboutUpdateApplySuffix;

    /// <summary>直近の一手の結末の一行（空＝まだ何もしていない＝出さない）。</summary>
    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set
        {
            if (SetProperty(ref _updateStatusText, value))
            {
                RaisePropertyChanged(nameof(UpdateStatusVisible));
            }
        }
    }

    /// <summary>結末の一行を出すか（押す前は出さない＝否定形の空白を書かない）。</summary>
    public bool UpdateStatusVisible => UpdateStatusText.Length > 0;

    /// <summary>更新経路が配線されているか（テストと画面の検分用）。</summary>
    public bool UpdateAttached => _updater is not null;

    /// <summary>
    /// 更新経路を差す（<b>配線は主窓 1 箇所</b>＝ここは Process も Application も知らない）。
    /// </summary>
    /// <param name="updater">取得・検分・起動を持つ口。</param>
    /// <param name="exitForUpdate">
    /// インストーラが起きたあとにこのアプリを終わらせる手＝<b>既存の終了の入口 1 本</b>
    /// （主窓の Closing → <c>App.OnExit</c> → 子のツリー kill）を叩くだけの Action。
    /// </param>
    /// <param name="isBusy">
    /// いま読み上げに使われているか（<c>/ywk/status</c> の <c>requests.in_flight</c> の標本）。
    /// 真なら二押し目を断る（取りにも行かない）。null＝柵を張らない。
    /// </param>
    public void AttachUpdater(IAppUpdateGateway updater, Action exitForUpdate, Func<bool>? isBusy = null)
    {
        ArgumentNullException.ThrowIfNull(updater);
        ArgumentNullException.ThrowIfNull(exitForUpdate);
        _updater = updater;
        _exitForUpdate = exitForUpdate;
        _isBusy = isBusy;
        RaisePropertyChanged(nameof(UpdateAttached));
        UpdateCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 結末の一行への写像（<b>純関数</b>＝状態＋道順型・文はすべて <see cref="UiStrings"/> から引く）。
    /// </summary>
    public static string DescribeUpdateResult(AppUpdateResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Kind switch
        {
            AppUpdateResultKind.UpToDate =>
                UiStrings.AboutUpdateUpToDatePrefix + (result.NewVersion ?? string.Empty)
                + UiStrings.AboutUpdateUpToDateSuffix,
            AppUpdateResultKind.UpdateAvailable =>
                UiStrings.AboutUpdateAvailablePrefix + (result.NewVersion ?? string.Empty)
                + UiStrings.AboutUpdateAvailableSuffix + DescribeNote(result.Note),
            AppUpdateResultKind.CheckFailed =>
                UiStrings.AboutUpdateFailedPrefix + (result.Detail ?? string.Empty)
                + UiStrings.AboutUpdateFailedSuffix,
            AppUpdateResultKind.VerifyFailed =>
                UiStrings.AboutUpdateVerifyFailedPrefix + (result.Detail ?? string.Empty)
                + UiStrings.AboutUpdateVerifyFailedSuffix,
            AppUpdateResultKind.LaunchedInstaller => UiStrings.AboutUpdateLaunched,
            AppUpdateResultKind.LaunchFailed =>
                UiStrings.AboutUpdateLaunchFailedPrefix + (result.Detail ?? string.Empty)
                + UiStrings.AboutUpdateLaunchFailedSuffix,
            _ => string.Empty,
        };
    }

    /// <summary>配布元が添えた一行（<b>表示のみ</b>＝無ければ足さない）。</summary>
    private static string DescribeNote(string? note) => string.IsNullOrWhiteSpace(note)
        ? string.Empty
        : UiStrings.AboutUpdateNotePrefix + note.Trim() + UiStrings.AboutUpdateNoteSuffix;

    /// <summary>
    /// 二段確認の本体（裁定 160）。一押し目＝照合だけ／二押し目＝取得・検分・起動 → 終了。
    /// </summary>
    private async Task UpdateAsync()
    {
        if (_updater is null)
        {
            return;
        }

        if (_pendingVersion is null)
        {
            // 一押し目＝押しただけでは 1 バイトも落ちない。
            UpdateStatusText = UiStrings.AboutUpdateChecking;
            var checkResult = await _updater.CheckAsync().ConfigureAwait(true);
            UpdateStatusText = DescribeUpdateResult(checkResult);

            // 版の判らない「新しい版がある」は控えにしない（押せない札を出さない）。
            SetPendingVersion(
                checkResult.Kind == AppUpdateResultKind.UpdateAvailable
                && !string.IsNullOrWhiteSpace(checkResult.NewVersion)
                    ? checkResult.NewVersion
                    : null);
            return;
        }

        // 読み上げ中は取りにも行かない（終わってからもう一度押してもらう）。
        if (_isBusy?.Invoke() == true)
        {
            UpdateStatusText = UiStrings.AboutUpdateBusy;
            return;
        }

        // 二押し目＝承諾した版を渡す（配布が先へ進んでいたら口の側が適用せず倒す）。
        UpdateStatusText = _pendingVersion + UiStrings.AboutUpdateDownloadingSuffix;
        var result = await _updater.ApplyAsync(_pendingVersion).ConfigureAwait(true);
        UpdateStatusText = DescribeUpdateResult(result);

        switch (result.Kind)
        {
            case AppUpdateResultKind.LaunchedInstaller:
                // 起こした＝以後この釦は起き直さない（終了は非同期に進む）。
                _launched = true;
                SetPendingVersion(null);
                UpdateCommand.RaiseCanExecuteChanged();
                _exitForUpdate?.Invoke();
                break;
            case AppUpdateResultKind.UpToDate:
                SetPendingVersion(null);
                break;
            case AppUpdateResultKind.UpdateAvailable:
                // 承諾した版と配布が食い違った＝二段確認のやり直し（控えを新しい版へ差し替える）。
                SetPendingVersion(
                    string.IsNullOrWhiteSpace(result.NewVersion) ? null : result.NewVersion);
                break;
            default:
                // 取得・検分・起動の失敗は控えを保つ＝もう一押しで取り直せる。
                break;
        }
    }

    /// <summary>控えの差し替え（変わったときだけ札の変更通知）。</summary>
    private void SetPendingVersion(string? version)
    {
        if (string.Equals(_pendingVersion, version, StringComparison.Ordinal))
        {
            return;
        }

        _pendingVersion = version;
        RaisePropertyChanged(nameof(UpdateButtonText));
    }
}
