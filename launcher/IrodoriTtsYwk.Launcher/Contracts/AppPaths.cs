using System;
using System.Collections.Generic;
using System.IO;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>
/// ランチャが触る場所の唯一の定義（契約 ⑼）。
/// <para>
/// <b>2 つの根がある</b>＝⑴ <b>導入先</b>（<see cref="AppDir"/>＝exe の隣・<b>読むだけ</b>）に
/// 配布樹（<c>server/</c>・<c>ledger/</c>・<c>licenses/</c>・<c>voices/presets/</c>）が乗り、
/// ⑵ <b>利用者データ</b>（<see cref="DataDir"/>＝<c>%LOCALAPPDATA%\irodori-tts-ywk</c>）に
/// 初回取得の実行系・モデル・話者・設定が落ちる。導入先が Program Files でも壊れないための分割で、
/// <c>IRODORI_VOICES_DIR</c> が絶対パス必須・起動時 mkdir という上流の型（launcher/README §4 落とし穴 2）を
/// そのまま満たす。
/// </para>
/// <para>
/// <b>開発モード</b>＝env <c>YWK_LAUNCHER_APP_DIR</c>／<c>YWK_LAUNCHER_RUNTIME_DIR</c>／
/// <c>YWK_LAUNCHER_DATA_DIR</c> で <c>build/out</c> の成果物を直接指せる（どれか 1 本でも
/// 立っていれば <see cref="DeveloperMode"/> が真）。この機体では
/// <c>YWK_LAUNCHER_APP_DIR=build/out/app</c>・<c>YWK_LAUNCHER_RUNTIME_DIR=build/out</c> で
/// <c>build/out/runtime-rocm-gfx1151</c> が変種 <c>rocm-gfx1151</c> として解決される
/// （<see cref="RuntimeDirCandidates"/>）。
/// </para>
/// <para>
/// 既定値は <c>server/ywk_server.py</c> の <c>apply_env_defaults</c> と<b>同じ場所</b>に揃えてある
/// （<c>&lt;data&gt;/voices</c>・<c>&lt;data&gt;/voices/voices.json</c>・<c>&lt;data&gt;/models</c>）。
/// 揃っていないと wrapper の setdefault とランチャの env が別の場所を指す。
/// </para>
/// </summary>
public sealed class AppPaths
{
    /// <summary>配布樹（server/・ledger/・licenses/・voices/presets/）の在り処を差し替える env。</summary>
    public const string AppDirEnvName = "YWK_LAUNCHER_APP_DIR";

    /// <summary>変種ディレクトリの親（runtime/&lt;variant&gt;）を差し替える env。</summary>
    public const string RuntimeRootEnvName = "YWK_LAUNCHER_RUNTIME_DIR";

    /// <summary>利用者データの根を差し替える env。</summary>
    public const string DataDirEnvName = "YWK_LAUNCHER_DATA_DIR";

    /// <summary><c>%LOCALAPPDATA%</c> 直下に作るディレクトリ名（wrapper の <c>_data_dir()</c> と同じ）。</summary>
    public const string DataDirName = "irodori-tts-ywk";

    /// <summary>埋め込み Python の実行形（変種ディレクトリの直下に居る）。</summary>
    public const string PythonExeName = "python.exe";

    /// <summary>子プロセスで起こすモジュール名（<c>python.exe -m ywk_server</c>）。</summary>
    public const string ServerModuleName = "ywk_server";

    public AppPaths(string installDir, string appDir, string runtimeRoot, string dataDir, bool developerMode)
    {
        InstallDir = Normalize(installDir);
        AppDir = Normalize(appDir);
        RuntimeRoot = Normalize(runtimeRoot);
        DataDir = Normalize(dataDir);
        DeveloperMode = developerMode;
    }

    /// <summary>exe が置かれている場所。</summary>
    public string InstallDir { get; }

    /// <summary>配布樹の根（<c>server/</c>・<c>ledger/</c>・<c>licenses/</c>・<c>voices/</c>）。読むだけ。</summary>
    public string AppDir { get; }

    /// <summary>変種ディレクトリの親。<see cref="ResolveRuntimeDir"/> で 1 変種に落とす。</summary>
    public string RuntimeRoot { get; }

    /// <summary>利用者データの根。<c>YWK_DATA_DIR</c> としてそのまま wrapper に渡る。</summary>
    public string DataDir { get; }

    /// <summary>env で場所を差し替えている＝<c>build/out</c> を指した開発起動。</summary>
    public bool DeveloperMode { get; }

    // ---- 配布樹（読むだけ） -------------------------------------------------

    public string ServerDir => Path.Combine(AppDir, "server");

    public string LedgerDir => Path.Combine(AppDir, "ledger");

    public string LicensesDir => Path.Combine(AppDir, "licenses");

    /// <summary>初回に <see cref="VoicesDir"/> へ写すプリセット wav（配布樹・読むだけ）。</summary>
    public string PresetVoicesDir => Path.Combine(AppDir, "voices", "presets");

    /// <summary>プリセットの台帳（<c>voices/presets.json</c>・配布樹・読むだけ）。</summary>
    public string PresetsJsonPath => Path.Combine(AppDir, "voices", "presets.json");

    /// <summary>初回取得の前に表示する通知文（裁定 46＝配布物に入れる）。</summary>
    public string FirstRunNoticesPath => Path.Combine(LicensesDir, "first-run-notices.md");

    public string PthTemplatePath => Path.Combine(ServerDir, "python312._pth.template");

    public string LedgerPath(string ledgerName) => Path.Combine(LedgerDir, ledgerName + ".json");

    // ---- 利用者データ（書く） -----------------------------------------------

    public string SettingsPath => Path.Combine(DataDir, "settings.json");

    /// <summary><c>IRODORI_VOICES_DIR</c>。wrapper の既定と同じ場所。</summary>
    public string VoicesDir => Path.Combine(DataDir, "voices");

    /// <summary><c>IRODORI_VOICE_ALIASES_FILE</c>（上流が読む別名表）。</summary>
    public string VoicesJsonPath => Path.Combine(VoicesDir, "voices.json");

    /// <summary>配布版の話者台帳（表示名・caption 既定・既定パラメータ＝契約 ⑷ 4-3）。</summary>
    public string VoicesYwkJsonPath => Path.Combine(VoicesDir, "voices.ywk.json");

    /// <summary>焼いた参照潜在の置き場（契約 ⑷ 4-4＝<c>voices_dir</c> 直下には置かない）。</summary>
    public string LatentsDir => Path.Combine(VoicesDir, "latents");

    /// <summary>
    /// 写した参照 wav の置き場（<b><c>voices_dir</c> 直下には置かない</b>＝是正・2026-09-05）。
    /// <para>
    /// 上流の <c>VoiceRegistry.list()</c> は <c>voices_dir</c> <b>直下</b>を檔名の幹で id 化して走査し、
    /// その結果に別名表を<b>上書きではなく合流</b>させる（<c>voices.py:63-68</c>）。参照 wav を直下に
    /// 置くと、1 人の話者が「日本語の表示名」と「ASCII の檔名の幹」の <b>2 件</b>になって一覧に出る
    /// （実射＝話者 2 名で <c>count = 5</c>）。<c>.pt</c> を <c>latents/</c> に逃がしたのと同じ理由で、
    /// wav も 1 段下げる。別名表のパスは相対なので上流が <c>voices_dir</c> から解決する
    /// （<c>voices.py:_resolve_voice_path</c>）。
    /// </para>
    /// </summary>
    public string ReferenceWavDir => Path.Combine(VoicesDir, "refs");

    /// <summary><c>HF_HOME</c>。wrapper の既定と同じ場所。</summary>
    public string HfHomeDir => Path.Combine(DataDir, "models");

    /// <summary>取得中の <c>.part</c> と検証済みの原檔の置き場。</summary>
    public string DownloadCacheDir => Path.Combine(DataDir, "cache");

    public string LogDir => Path.Combine(DataDir, "logs");

    /// <summary>MIOpen の db（rocm 変種でだけ wrapper が作る。ランチャは表示にしか使わない）。</summary>
    public string MiopenDir => Path.Combine(DataDir, "miopen");

    // ---- 変種の解決 ---------------------------------------------------------

    /// <summary>
    /// 変種ディレクトリの候補（**純関数**）。順に⑴ <c>&lt;root&gt;\&lt;variant&gt;</c>（配布の形）
    /// ⑵ <c>&lt;root&gt;\runtime-&lt;variant&gt;</c>（<c>build/out</c> の形）⑶ <c>&lt;root&gt;</c> そのもの
    /// （1 変種を直接指した開発起動）。
    /// </summary>
    public static IReadOnlyList<string> RuntimeDirCandidates(string runtimeRoot, string variant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        return
        [
            Path.Combine(runtimeRoot, variant),
            Path.Combine(runtimeRoot, "runtime-" + variant),
            runtimeRoot,
        ];
    }

    /// <summary>
    /// 変種ディレクトリを 1 つに決める。<paramref name="hasPython"/> は
    /// 「そのディレクトリに <c>python.exe</c> が在るか」（既定＝実際の檔検査。テストは述語を差す）。
    /// 見つからなければ null＝「取得がまだ済んでいない」。
    /// </summary>
    public string? ResolveRuntimeDir(string variant, Func<string, bool>? hasPython = null)
    {
        hasPython ??= static dir => File.Exists(Path.Combine(dir, PythonExeName));
        foreach (var candidate in RuntimeDirCandidates(RuntimeRoot, variant))
        {
            if (hasPython(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>変種の <c>python.exe</c>。未取得なら null。</summary>
    public string? ResolvePythonExe(string variant, Func<string, bool>? hasPython = null)
    {
        var dir = ResolveRuntimeDir(variant, hasPython);
        return dir is null ? null : Path.Combine(dir, PythonExeName);
    }

    // ---- 組み立て -----------------------------------------------------------

    /// <summary>
    /// env と基準ディレクトリから組み立てる（**純関数**＝テストは辞書を渡す）。
    /// </summary>
    /// <param name="baseDirectory">exe の在り処（実行時は <see cref="AppContext.BaseDirectory"/>）。</param>
    /// <param name="localAppData"><c>%LOCALAPPDATA%</c>。空なら利用者の家の下に落とす。</param>
    /// <param name="getEnv">env の読み口（未設定は null／空を返すこと）。</param>
    public static AppPaths Resolve(string baseDirectory, string? localAppData, Func<string, string?> getEnv)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(getEnv);

        var appOverride = Trim(getEnv(AppDirEnvName));
        var runtimeOverride = Trim(getEnv(RuntimeRootEnvName));
        var dataOverride = Trim(getEnv(DataDirEnvName));

        var installDir = baseDirectory;
        var appDir = appOverride ?? installDir;
        var dataDir = dataOverride
            ?? (string.IsNullOrWhiteSpace(localAppData)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "." + DataDirName)
                : Path.Combine(localAppData, DataDirName));
        var runtimeRoot = runtimeOverride ?? Path.Combine(dataDir, "runtime");
        var developer = appOverride is not null || runtimeOverride is not null || dataOverride is not null;

        return new AppPaths(installDir, appDir, runtimeRoot, dataDir, developer);
    }

    /// <summary>実行時の既定（<see cref="AppContext.BaseDirectory"/>＋実 env）。</summary>
    public static AppPaths FromEnvironment() => Resolve(
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        static name => Environment.GetEnvironmentVariable(name));

    /// <summary>書き込み側のディレクトリを作る（導入先には 1 檔も作らない）。</summary>
    public void EnsureDataDirectories()
    {
        foreach (var dir in new[]
                 { DataDir, VoicesDir, LatentsDir, ReferenceWavDir, HfHomeDir, DownloadCacheDir, LogDir })
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var trimmed = path.Trim();
        // 末尾の区切りだけ落とす（"C:\" のような根は落とさない）
        if (trimmed.Length > 3 && (trimmed[^1] == Path.DirectorySeparatorChar || trimmed[^1] == Path.AltDirectorySeparatorChar))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed;
    }
}
