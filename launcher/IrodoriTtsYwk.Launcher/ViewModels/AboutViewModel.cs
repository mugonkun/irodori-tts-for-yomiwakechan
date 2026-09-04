using System;
using System.Collections.Generic;
using System.IO;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Mvvm;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// このアプリについて（裁定 1＝<b>非公式・Aratako 氏とは無関係</b>を冒頭に出す）。
/// <para>
/// 出すのは 4 つ＝⑴ <b>非公式である旨</b>（裁定 1・README 冒頭と同じ文言）⑵ <b>版</b>
/// （<c>AppDisplayVersion</c>＝1 箇所の定義）⑶ <b>上流 pin</b>（publish でだけ焼かれる＝
/// 開発ビルドでは「開発ビルド」と名乗る）⑷ <b>ライセンスの所在</b>（配布樹の <c>licenses/</c>）。
/// </para>
/// </summary>
public sealed class AboutViewModel : ObservableObject
{
    private readonly AppPaths _paths;

    public AboutViewModel(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
    }

    /// <summary>裁定 1（README 冒頭と同じ）。</summary>
    public static string Disclaimer =>
        "これは非公式のランチャです。Aratako 氏および Irodori-TTS／Irodori-TTS-Server とは"
        + "関係がありません。上流は無改変のまま commit で固定して使っています。";

    /// <summary>裁定 9（透かしは既定 ON・切る経路を持たない）。</summary>
    public static string WatermarkNotice =>
        "合成した音声には SilentCipher の電子透かしが入ります（切る経路はありません）。";

    /// <summary>README §4（Ethical Restrictions 1・No Impersonation）。</summary>
    public static string EthicsNotice => VoicesViewModel.ImpersonationNotice;

    public static string VersionText => "版 " + AppVersion.Display;

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
}
