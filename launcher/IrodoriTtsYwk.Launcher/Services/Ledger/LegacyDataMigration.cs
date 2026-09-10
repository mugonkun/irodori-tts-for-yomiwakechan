using System;
using System.IO;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Logging;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Services.Ledger;

/// <summary>移送の判定（<see cref="LegacyDataMigration.Plan"/> の戻り）。</summary>
public enum LegacyDataAction
{
    /// <summary>何もしない（版の樹が既に在る・旧樹が無い・明示指定が在る）。</summary>
    None,

    /// <summary>同じボリューム＝<b>改名</b>（<see cref="Directory.Move"/>・秒で終わる）。</summary>
    Rename,

    /// <summary>
    /// 別ボリューム、または<b>もう一方の版がまだ旧樹を要る</b>機体＝<b>写す</b>（旧樹は消さない）。
    /// </summary>
    Copy,
}

/// <summary>
/// 移送の計画（<b>純関数の産物</b>）。
/// </summary>
/// <param name="Action">する事。</param>
/// <param name="From">旧い共有樹（<see cref="LegacyDataAction.None"/> なら null）。</param>
/// <param name="To">版の樹（同上）。</param>
/// <param name="Reason">なぜその結論になったか（ログの 1 行に入る）。</param>
public sealed record LegacyDataPlan(
    LegacyDataAction Action, string? From, string? To, string Reason)
{
    /// <summary>何もしない結末。</summary>
    public static LegacyDataPlan Nothing(string reason) =>
        new(LegacyDataAction.None, null, null, reason);

    /// <summary>移すか。</summary>
    public bool Moves => Action != LegacyDataAction.None;
}

/// <summary>
/// <b>旧い共有樹から版の樹への移送</b>（<c>decisions.md</c> 133 ⑸・<c>v2-spec.md</c> §11-8）。
/// <para>
/// ≦ v1.1.0 は RTX（CUDA）版と Radeon（ROCm）版が <c>%LOCALAPPDATA%\irodori-tts-ywk\</c> の
/// <b>1 本を分け合っていた</b>。裁定 133 ⑶ で「別アプリ＝データ樹も設定も声も共有しない」と決まり、
/// 既定は <c>…-cuda\</c>／<c>…-radeon\</c> になった。この檔は<b>v2.0 の初回起動で 1 度だけ</b>
/// 旧樹の中身を版の樹へ移す。
/// </para>
/// <para>
/// <b>失敗したら移送しなかったことにする</b>（旧樹は消さない）＝<b>利用者の声を失わない</b>。
/// 旧樹が残ったことは<b>ログに 1 行だけ</b>残し、画面には出さない（§11-8 ⑶）。
/// </para>
/// <para>
/// <b>移送のために新しい枝を足さない</b>＝移った樹は <c>.ledger.json</c> も <c>preset_md5</c> も
/// 持たないので、<see cref="RuntimeDiff"/> の「不在なら丸ごと」と
/// <see cref="Voices.PresetSync"/> の枝 e がそのまま効く（§11-8 ⑵）。
/// </para>
/// </summary>
public static class LegacyDataMigration
{
    /// <summary>
    /// 移すかどうかを決める（<b>純関数</b>＝檔に触らない）。
    /// <list type="number">
    /// <item>明示指定（env・<c>settings.json</c> の <c>dataDir</c>）が在る＝<b>何もしない</b>。</item>
    /// <item>旧樹が無い＝何もしない（まっさらな機体・既に移した機体）。</item>
    /// <item>版の樹に<b>檔が 1 つでも在る</b>＝何もしない（もう使い始めている）。
    /// 空のディレクトリだけの樹は「まだ何も入っていない」と見る＝
    /// <c>EnsureDataDirectories</c> が作った直後でも移送が通る。</item>
    /// <item>旧樹と版の樹が<b>同じボリューム</b>＝改名。違えば写す。</item>
    /// <item><b>もう一方の版が旧樹をまだ要る</b>ときも写す（旧樹を消さない）。</item>
    /// <item><b>版が積極的に決まっていない</b>＝何もしない（行き先を間違えたら取り返しがつかない）。</item>
    /// </list>
    /// </summary>
    /// <param name="legacyDir">旧い共有樹（無い機体・明示指定の回は null）。</param>
    /// <param name="dataDir">版の樹。</param>
    /// <param name="dataDirOverridden">明示指定が在るか。</param>
    /// <param name="legacyExists">旧樹が在るか。</param>
    /// <param name="dataTreeHasFiles">版の樹に檔が 1 つでも在るか。</param>
    /// <param name="sameVolume">2 つが同じボリュームか。</param>
    /// <param name="otherFlavorNeedsLegacy">もう一方の版がまだ旧樹を要るか（＝写しにする）。</param>
    /// <param name="flavorDetermined">
    /// 版を<b>積極的に決められた</b>か（<see cref="AppPaths.FlavorDetermined"/>）。偽＝<b>移送しない</b>＝
    /// <c>ledger/</c> が 1 度読めなかっただけの Radeon 機が、共有樹を CUDA の樹へ移してしまうのを止める
    /// （是正・2026-09-11）。旧樹はそのまま残るので、次の起動でやり直せる。
    /// </param>
    public static LegacyDataPlan Plan(
        string? legacyDir,
        string dataDir,
        bool dataDirOverridden,
        bool legacyExists,
        bool dataTreeHasFiles,
        bool sameVolume,
        bool otherFlavorNeedsLegacy,
        bool flavorDetermined = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDir);

        if (dataDirOverridden)
        {
            return LegacyDataPlan.Nothing("置き場が明示で指されている（移送しない）。");
        }

        if (!flavorDetermined)
        {
            return LegacyDataPlan.Nothing("どちらの版か確かめられなかった（移送しない）。");
        }

        if (string.IsNullOrWhiteSpace(legacyDir) || !legacyExists)
        {
            return LegacyDataPlan.Nothing("旧い共有樹が無い。");
        }

        if (string.Equals(
                Path.TrimEndingDirectorySeparator(legacyDir.Trim()),
                Path.TrimEndingDirectorySeparator(dataDir.Trim()),
                StringComparison.OrdinalIgnoreCase))
        {
            return LegacyDataPlan.Nothing("旧い共有樹と版の樹が同じ路である。");
        }

        if (dataTreeHasFiles)
        {
            return LegacyDataPlan.Nothing("版の樹が既に使われている。");
        }

        return otherFlavorNeedsLegacy || !sameVolume
            ? new LegacyDataPlan(
                LegacyDataAction.Copy, legacyDir, dataDir,
                otherFlavorNeedsLegacy
                    ? "もう一方の版がまだ旧い共有樹を要るので写す（旧樹は残す）。"
                    : "別のボリュームなので写す（旧樹は残す）。")
            : new LegacyDataPlan(
                LegacyDataAction.Rename, legacyDir, dataDir, "同じボリュームなので改名で移す。");
    }

    /// <summary>
    /// 実際に移す（<b>投げない</b>＝失敗したら移送しなかったことにして新しい樹で始める）。
    /// 戻り＝この回に起きたこと。
    /// </summary>
    public static LegacyDataPlan Run(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var legacy = paths.LegacyDataDir;
        LegacyDataPlan plan;
        try
        {
            // **ふつうの起動はここで終わる**（旧樹が無い＝移送済み・まっさらな機体）。
            // 樹の走査も HKCU の読みも、旧樹が在る回にしかしない。
            if (paths.DataDirOverridden || legacy is null || !Directory.Exists(legacy))
            {
                return Plan(
                    legacy, paths.DataDir, paths.DataDirOverridden, false, false, false, false,
                    paths.FlavorDetermined);
            }

            plan = Plan(
                legacy,
                paths.DataDir,
                paths.DataDirOverridden,
                legacyExists: true,
                dataTreeHasFiles: HasAnyFile(paths.DataDir),
                sameVolume: SameVolume(legacy, paths.DataDir),
                otherFlavorNeedsLegacy: OtherFlavorInstalled(paths.Flavor),
                flavorDetermined: paths.FlavorDetermined);
        }
        catch (IOException)
        {
            return LegacyDataPlan.Nothing("旧い共有樹を調べられなかった（移送しない）。");
        }
        catch (UnauthorizedAccessException)
        {
            return LegacyDataPlan.Nothing("旧い共有樹を調べられなかった（移送しない）。");
        }

        if (!plan.Moves)
        {
            // 落ちた写しの残骸を畳む（旧樹が在る回にしかここへ来ない＝ふつうの起動は上で帰っている）。
            DeleteQuietly(StagingDirFor(paths.DataDir));
            return plan;
        }

        try
        {
            if (IsReparsePoint(plan.From!) || IsReparsePoint(plan.To!))
            {
                // junction（mklink /J）で逃がしてある樹は路と実体がずれる＝手を出さない。
                return Note(paths, LegacyDataPlan.Nothing(
                    "旧い共有樹か版の樹が junction なので移送しない：" + plan.From));
            }

            // **1 段下の junction も同じ扱い**（是正・2026-09-11）＝`<data>\models` だけを別ドライブへ
            // 逃がした樹（docs/install.md §5-3 が教える手）は、檔を 1 つも持たないので HasAnyFile が
            // 偽を返し Plan が改名を出す。そのまま下の Directory.Delete を撃つと **.NET は的ではなく
            // 環（link）を消す**＝利用者の逃がし先が黙って消え、樹は C: に積み直る。
            if (HasReparsePointChild(plan.To!))
            {
                return Note(paths, LegacyDataPlan.Nothing(
                    "版の樹の中に junction がある（移送しない）：" + plan.To));
            }

            if (plan.Action == LegacyDataAction.Copy && !FitsOnDestination(plan.From!, plan.To!))
            {
                // 空きが足りないまま写すと、旧樹を丸ごと二重に置こうとして途中で ENOSPC になる。
                // 旧樹は 1 檔も触らずに帰る＝次の起動でやり直せる。
                return Note(paths, LegacyDataPlan.Nothing(
                    "移す先の空きが足りない（移送しない）：" + plan.From));
            }

            // 版の樹が「空のディレクトリだけ」なら、改名の邪魔になるので先に畳む
            //（檔が 1 つでも在れば Plan が None を返しているので、ここには来ない）。
            if (Directory.Exists(plan.To!))
            {
                Directory.Delete(plan.To!, recursive: true);
            }

            if (plan.Action == LegacyDataAction.Rename)
            {
                Directory.Move(plan.From!, plan.To!);
            }
            else
            {
                CopyToStaging(plan.From!, plan.To!);
            }

            var done = Note(paths, plan);
            if (plan.Action == LegacyDataAction.Copy)
            {
                NoteReclaimable(paths, plan.From!);
            }

            return done;
        }
        catch (IOException ex)
        {
            return Note(paths, LegacyDataPlan.Nothing(
                "移送に失敗したので旧い共有樹をそのまま残した（" + ex.Message + "）：" + plan.From));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Note(paths, LegacyDataPlan.Nothing(
                "移送に失敗したので旧い共有樹をそのまま残した（" + ex.Message + "）：" + plan.From));
        }
    }

    /// <summary>ログの 1 行（<b>純関数</b>）。</summary>
    public static string LogLine(LegacyDataPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Action switch
        {
            LegacyDataAction.Rename => "旧い置き場を新しい置き場へ移しました（改名）：" + plan.From + " → " + plan.To,
            LegacyDataAction.Copy => "旧い置き場を新しい置き場へ写しました：" + plan.From + " → " + plan.To,
            _ => plan.Reason,
        };
    }

    /// <summary>
    /// その樹に<b>檔が 1 つでも在るか</b>（空のディレクトリだけなら偽）。
    /// </summary>
    public static bool HasAnyFile(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }

            using var found = Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .GetEnumerator();
            return found.MoveNext();
        }
        catch (IOException)
        {
            return true; // 読めない＝手を出さない側に倒す
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>2 つの路が同じボリュームか（<b>純関数</b>＝根の綴りだけを見る）。</summary>
    public static bool SameVolume(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            Path.GetPathRoot(Path.GetFullPath(left.Trim())),
            Path.GetPathRoot(Path.GetFullPath(right.Trim())),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// もう一方の版がこの機体に入っているか（<c>.iss</c> の <c>OtherFlavorKey</c> と<b>同じ鍵</b>を見る）。
    /// </summary>
    public static bool OtherFlavorInstalled(ReleaseFlavor flavor)
    {
        var key = UninstallKeyPrefix + (flavor == ReleaseFlavor.Radeon ? CudaAppId : RadeonAppId) + "_is1";
        try
        {
            using var found = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key);
            return found is not null;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>アンインストール鍵の親（per-user 導入なので <c>HKCU</c>）。</summary>
    public const string UninstallKeyPrefix =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";

    /// <summary>RTX（CUDA）版の <c>AppId</c>（<c>installer/irodori-tts-ywk.iss</c> の逐語）。</summary>
    public const string CudaAppId = "{F228543A-DCF9-45A3-8826-7485C81E1757}";

    /// <summary>Radeon（ROCm）版の <c>AppId</c>（同上）。</summary>
    public const string RadeonAppId = "{ECA98712-1574-4D2A-A1FE-0FF5347BF185}";

    private static LegacyDataPlan Note(AppPaths paths, LegacyDataPlan plan)
    {
        new LauncherLogFile(paths.LogDir).Append(LogLine(plan));
        return plan;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return Directory.Exists(path)
                   && (new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return true; // 判らない＝手を出さない側に倒す
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// 写しの途中の置き場（<c>&lt;版の樹&gt;.migrating</c>）＝<b>版の樹の兄弟</b>なので同じボリューム＝
    /// 最後の 1 檔まで写せたら <see cref="Directory.Move"/> 1 手で版の樹になる。
    /// </summary>
    public static string StagingDirFor(string dataDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDir);
        return Path.TrimEndingDirectorySeparator(dataDir.Trim()) + ".migrating";
    }

    /// <summary>
    /// <b>途中で落ちても半端な樹を残さない写し</b>（是正・2026-09-11）。
    /// <para>
    /// 直に版の樹へ写すと、ディスクが尽きた回（両版の機体は旧樹を<b>版ごとに 1 本ずつ</b>写すので
    /// 樹の 2〜3 倍の空きが要る）に<b>途中まで入った版の樹</b>が残る。次の起動では
    /// <see cref="Plan"/> が「版の樹が既に使われている」で <see cref="LegacyDataAction.None"/> を返す＝
    /// <b>移送は二度と再開せず</b>、利用者は欠けた声の一覧と、旧樹に取り残された残りを抱える。
    /// </para>
    /// <para>
    /// だから<b>兄弟の <c>.migrating</c> へ写してから 1 手で改名する</b>＝落ちた回に残るのは
    /// <c>.migrating</c> だけで、版の樹は空のまま＝<see cref="Plan"/> は次も
    /// <see cref="LegacyDataAction.Copy"/> を返す。残骸は次の回の頭で畳む。
    /// </para>
    /// </summary>
    public static void CopyToStaging(string from, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        var staging = StagingDirFor(to);
        DeleteQuietly(staging); // 前の回の残骸（CopyTree は overwrite: true なので写し直せる）
        try
        {
            CopyTree(from, staging);
            if (Directory.Exists(to))
            {
                Directory.Delete(to, recursive: true);
            }

            Directory.Move(staging, to);
        }
        catch
        {
            DeleteQuietly(staging);
            throw;
        }
    }

    /// <summary>樹の中の檔の長さの和（読めない所は飛ばす）。</summary>
    public static long TreeBytes(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return total;
    }

    /// <summary>
    /// 写しきるだけの空きが在るか（<b>1 割の余裕を足して見る</b>＝写しの最中に他が使う分）。
    /// 空きが引けない機体は<b>真</b>（＝止めない＝従来どおり試して、落ちたら旧樹を残す）。
    /// </summary>
    public static bool FitsOnDestination(string from, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(to));
            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            var need = TreeBytes(from);
            return need <= 0 || new DriveInfo(root).AvailableFreeSpace >= need + (need / 10);
        }
        catch (ArgumentException)
        {
            return true;
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

    /// <summary>その樹の<b>すぐ下の枝</b>に junction が在るか（読めなければ真＝手を出さない側）。</summary>
    public static bool HasReparsePointChild(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (IsReparsePoint(child))
                {
                    return true;
                }
            }

            return false;
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
    /// 写しのあとに<b>旧樹が食っている量を 1 行だけ</b>ログに残す（<b>消さない</b>）。
    /// <para>
    /// 両版を使う機体は、旧樹 ＋ 版の樹 2 本＝<b>同じ物を 3 本</b>抱える。旧樹を launcher から
    /// 勝手に消すのは採らない＝「向こうの樹に檔が在る」は「向こうも移送を済ませた」の証明ではない
    /// （まっさらに作った樹でも真になる）＝<b>取り返しのつかない削除を推量で撃たない</b>。
    /// 旧樹は最後の撤去（<c>.iss</c> の <c>CurUninstallStepChanged</c>）が畳む。
    /// </para>
    /// </summary>
    private static void NoteReclaimable(AppPaths paths, string legacy)
    {
        var bytes = TreeBytes(legacy);
        if (bytes <= 0)
        {
            return;
        }

        new LauncherLogFile(paths.LogDir).Append(
            "旧い共有樹はそのまま残しています（もう一方の版がまだ要るかもしれないため・約 "
            + FetchPlanner.FormatBytes(bytes) + "）：" + legacy);
    }

    private static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>樹を丸ごと写す（<b>旧樹は 1 檔も消さない</b>）。</summary>
    public static void CopyTree(string from, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}
