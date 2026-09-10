using System;
using System.Collections.Generic;
using System.IO;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>
/// ランチャが触る場所の唯一の定義（契約 ⑼）。
/// <para>
/// <b>2 つの根がある</b>＝⑴ <b>導入先</b>（<see cref="AppDir"/>＝exe の隣・<b>読むだけ</b>）に
/// 配布樹（<c>server/</c>・<c>ledger/</c>・<c>licenses/</c>・<c>voices/presets/</c>）が乗り、
/// ⑵ <b>利用者データ</b>（<see cref="DataDir"/>＝<b>版ごと</b>＝
/// <c>%LOCALAPPDATA%\irodori-tts-ywk-cuda</c>／<c>…-radeon</c>＝<c>decisions.md</c> 133 ⑶）に
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

    /// <summary>
    /// <b>旧い共有樹</b>の名（≦ v1.1.0 は CUDA 版と ROCm 版が <c>%LOCALAPPDATA%</c> の下の
    /// この 1 本を分け合っていた）。<b>綴りは残す</b>＝v2.0 の初回起動で版の樹へ移すのに要る
    /// （<c>decisions.md</c> 133 ⑸・<c>v2-spec.md</c> §11-8）。<b>解決には二度と出ない。</b>
    /// </summary>
    public const string LegacyDataDirName = "irodori-tts-ywk";

    /// <summary>RTX（CUDA）版の利用者データの根の名（<c>decisions.md</c> 133 ⑶）。</summary>
    public const string DataDirNameCuda = "irodori-tts-ywk-cuda";

    /// <summary>Radeon（ROCm）版の利用者データの根の名（同上）。</summary>
    public const string DataDirNameRadeon = "irodori-tts-ywk-radeon";

    /// <summary>
    /// 単一起動の錠と合図の綴りの<b>幹</b>（<c>App.xaml.cs</c> と <c>.iss</c> の <c>AppMutex</c> が
    /// 同じ 1 箇所から採る）。版ごとに割るのは <see cref="SingleInstanceMutexName"/>／
    /// <see cref="ActivateEventName"/>。
    /// </summary>
    public const string SingleInstanceNameBase = @"Local\irodori-tts-ywk-launcher";

    /// <summary>埋め込み Python の実行形（変種ディレクトリの直下に居る）。</summary>
    public const string PythonExeName = "python.exe";

    /// <summary>子プロセスで起こすモジュール名（<c>python.exe -m ywk_server</c>）。</summary>
    public const string ServerModuleName = "ywk_server";

    /// <summary>
    /// 版の内部 id（<b>1 字も変えない</b>＝<c>.iss</c> の <c>/DFlavor=</c>・setup の檔名と同じ綴り）。
    /// </summary>
    public static string FlavorId(ReleaseFlavor flavor) =>
        flavor == ReleaseFlavor.Radeon ? "radeon" : "cuda";

    /// <summary>
    /// その版の利用者データの根の名（<b>純関数</b>＝<c>decisions.md</c> 133 ⑶）。
    /// </summary>
    public static string DataDirNameFor(ReleaseFlavor flavor) =>
        flavor == ReleaseFlavor.Radeon ? DataDirNameRadeon : DataDirNameCuda;

    /// <summary>
    /// 単一起動の錠の名（<b>版ごと</b>＝<c>decisions.md</c> 133 ⑷。別アプリなので互いを起こし直さない）。
    /// <c>.iss</c> の <c>AppMutex</c> と<b>1 字も違えない</b>。
    /// <para>
    /// <b>鏡は <c>installer/irodori-tts-ywk.iss:177</c></b>（<c>AppMutex=Local\irodori-tts-ywk-launcher-{#Flavor}</c>）。
    /// Inno から C# の定数は引けないので、綴りは<b>手で揃えるしかない</b>＝ずれると
    /// 「走っているランチャを止めてください」の関門が黙って効かなくなる（Inno の照合は<b>大小を区別する</b>）。
    /// 4 本の逐語は <c>LegacyDataMigrationTests.版ごとの樹は互いに重ならない</c> が釘付けしている。
    /// </para>
    /// </summary>
    public static string SingleInstanceMutexName(ReleaseFlavor flavor) =>
        SingleInstanceNameBase + "-" + FlavorId(flavor);

    /// <summary>
    /// 2 個目が 1 個目に「窓を出せ」と伝える口（<b>錠と必ず同じ組で割る</b>）。
    /// 割り忘れると、合図は <c>AutoReset</c> の 1 本なので
    /// <b>CUDA 版の 2 個目の起動が Radeon 版の窓を前に出す</b>（<c>v2-plan.md</c> 段 E-3 の註）。
    /// </summary>
    public static string ActivateEventName(ReleaseFlavor flavor) =>
        SingleInstanceNameBase + "-activate-" + FlavorId(flavor);

    public AppPaths(
        string installDir,
        string appDir,
        string runtimeRoot,
        string dataDir,
        bool developerMode,
        ReleaseFlavor flavor = ReleaseFlavor.Cuda,
        bool dataDirOverridden = false,
        string? legacyDataDir = null,
        bool flavorDetermined = true)
    {
        InstallDir = Normalize(installDir);
        AppDir = Normalize(appDir);
        RuntimeRoot = Normalize(runtimeRoot);
        DataDir = Normalize(dataDir);
        DeveloperMode = developerMode;
        Flavor = flavor;
        DataDirOverridden = dataDirOverridden;
        LegacyDataDir = string.IsNullOrWhiteSpace(legacyDataDir) ? null : Normalize(legacyDataDir);
        FlavorDetermined = flavorDetermined;
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

    /// <summary>
    /// どの版の樹の上に居るか（<c>decisions.md</c> 133 ⑶＝RTX（CUDA）と Radeon（ROCm）は別アプリ）。
    /// </summary>
    public ReleaseFlavor Flavor { get; }

    /// <summary>
    /// 利用者データの根を<b>明示で</b>指されている（env <c>YWK_LAUNCHER_DATA_DIR</c>、
    /// ないし <c>settings.json</c> の <c>dataDir</c>）。真なら旧共有樹からの移送は<b>走らせない</b>。
    /// <para>
    /// <c>settings.json</c> の側は <see cref="FromEnvironment"/> が<b>版の樹（と、まだ移していない
    /// 旧い共有樹）の <c>settings.json</c> を先に覗いて</c></b>拾う＝<see cref="EnsureDataDirectories"/>
    /// より前に決まる（移送はこの値を見て走るか止まるかを決めるので、順を崩せない）。
    /// </para>
    /// </summary>
    public bool DataDirOverridden { get; }

    /// <summary>
    /// <b>版を積極的に決められたか</b>（配布樹の <c>ledger/</c>・導入先の名・既に在る版の樹のどれかが
    /// 答えた）。偽＝<b>CUDA を仮に名乗っているだけ</b>で、
    /// <see cref="Services.Ledger.LegacyDataMigration.Plan"/> は<b>移送を断る</b>
    /// （行き先を間違えると Radeon 機の声が CUDA の樹へ消える＝是正・2026-09-11）。
    /// </summary>
    public bool FlavorDetermined { get; }

    /// <summary>
    /// 旧い共有樹（<c>%LOCALAPPDATA%\irodori-tts-ywk</c>）＝<b>移送の元</b>。
    /// <c>%LOCALAPPDATA%</c> が取れない機体と、明示指定の回は null。
    /// </summary>
    public string? LegacyDataDir { get; }

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
    /// <param name="flavor">
    /// どの版か（<b>利用者データの根の名がこれで決まる</b>＝<c>decisions.md</c> 133 ⑶）。
    /// 既定は CUDA 版＝<b>樹を読まない</b>（この関数は純関数のまま）。実行時に樹から決めるのは
    /// <see cref="FromEnvironment"/>。
    /// </param>
    /// <param name="dataDirOverriddenBySettings">
    /// <c>settings.json</c> の <c>dataDir</c> で明示に指されている（env と同じ扱い＝移送を走らせない）。
    /// </param>
    /// <param name="dataDirFromSettings">
    /// <c>settings.json</c> の <c>dataDir</c> の値そのもの（空なら見ない）。env と違い
    /// <see cref="DeveloperMode"/> は立てない＝これは<b>利用者の置き場の指定</b>であって開発起動ではない。
    /// </param>
    /// <param name="flavorDetermined">
    /// <paramref name="flavor"/> を<b>積極的に決められた</b>か（<see cref="FlavorDetermined"/>）。
    /// </param>
    public static AppPaths Resolve(
        string baseDirectory,
        string? localAppData,
        Func<string, string?> getEnv,
        ReleaseFlavor flavor = ReleaseFlavor.Cuda,
        bool dataDirOverriddenBySettings = false,
        string? dataDirFromSettings = null,
        bool flavorDetermined = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(getEnv);

        var appOverride = Trim(getEnv(AppDirEnvName));
        var runtimeOverride = Trim(getEnv(RuntimeRootEnvName));
        var dataOverride = Trim(getEnv(DataDirEnvName));
        var settingsDataDir = Trim(dataDirFromSettings);

        var installDir = baseDirectory;
        var appDir = appOverride ?? installDir;
        var dataDirName = DataDirNameFor(flavor);
        var dataDir = dataOverride
            ?? settingsDataDir
            ?? (string.IsNullOrWhiteSpace(localAppData)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "." + dataDirName)
                : Path.Combine(localAppData, dataDirName));
        var runtimeRoot = runtimeOverride ?? Path.Combine(dataDir, "runtime");
        var developer = appOverride is not null || runtimeOverride is not null || dataOverride is not null;
        var overridden = dataOverride is not null || dataDirOverriddenBySettings || settingsDataDir is not null;

        // 旧共有樹は **%LOCALAPPDATA% 直下にしか無い**（≦ v1.1.0 の既定＝AppPaths の旧 DataDirName）。
        // 明示指定の回は移送そのものを走らせないので、元の路も持たせない。
        var legacy = overridden || string.IsNullOrWhiteSpace(localAppData)
            ? null
            : Path.Combine(localAppData, LegacyDataDirName);

        return new AppPaths(
            installDir, appDir, runtimeRoot, dataDir, developer, flavor, overridden, legacy,
            flavorDetermined);
    }

    /// <summary>
    /// 実行時の既定（<see cref="AppContext.BaseDirectory"/>＋実 env）。
    /// <para>
    /// <b>版の決め方は 3 段</b>（<c>decisions.md</c> 133・是正・2026-09-11）＝
    /// ⑴ 配布樹の <c>ledger/</c>（<see cref="ReleaseFlavors.TryDetectFrom"/>＝exe は 1 本で両リリースを兼ねる）
    /// ⑵ 導入先の名（<c>.iss</c> の <c>MyDirName</c> は版ごと）
    /// ⑶ <b>もう出来ている版の樹</b>（片方だけ在るとき）。どれも答えなければ CUDA を<b>仮に</b>名乗り、
    /// <see cref="FlavorDetermined"/> を偽にして<b>移送を断らせる</b>。
    /// 昔（裁定 109）は誤判定が窓題を間違えるだけだったが、いまは<b>データ樹・錠・移送先</b>が動く。
    /// </para>
    /// <para>
    /// <b><c>settings.json</c> の <c>dataDir</c> はここで効く</b>＝版の既定の樹（無ければ、まだ移していない
    /// 旧い共有樹）の <c>settings.json</c> を 1 度だけ覗いて、値が在れば置き場ごと差し替える。
    /// <see cref="EnsureDataDirectories"/>（＝移送）より前に決まっていないと意味が無い。
    /// </para>
    /// </summary>
    public static AppPaths FromEnvironment()
    {
        var baseDir = AppContext.BaseDirectory;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static string? Env(string name) => Environment.GetEnvironmentVariable(name);

        // 1 度目は版を決めるためだけ（AppDir は版に依らないので、この 1 往復で足りる）。
        var probe = Resolve(baseDir, localAppData, Env);
        var determined = ReleaseFlavors.TryDetectFrom(probe.LedgerDir, out var flavor)
                         || ReleaseFlavors.TryDetectFromDirectoryName(probe.InstallDir, out flavor)
                         || TryFlavorFromExistingTree(localAppData, out flavor);

        // 2 度目＝版の樹で解き直す（この時点ではまだ settings.json を読んでいない）。
        var resolved = Resolve(baseDir, localAppData, Env, flavor, flavorDetermined: determined);
        if (resolved.DataDirOverridden)
        {
            return resolved; // env が勝っている＝settings は見ない（従来どおり）
        }

        var fromSettings = ReadDataDirSetting(resolved);
        return fromSettings is null
            ? resolved
            : Resolve(
                baseDir, localAppData, Env, flavor,
                dataDirOverriddenBySettings: true,
                dataDirFromSettings: fromSettings,
                flavorDetermined: determined);
    }

    /// <summary>
    /// <b>もう出来ている版の樹</b>から版を当てる（<b>片方だけ在るときに限る</b>＝両方在る機体は答えない）。
    /// </summary>
    private static bool TryFlavorFromExistingTree(string? localAppData, out ReleaseFlavor flavor)
    {
        flavor = ReleaseFlavor.Cuda;
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return false;
        }

        try
        {
            var cuda = Directory.Exists(Path.Combine(localAppData, DataDirNameCuda));
            var radeon = Directory.Exists(Path.Combine(localAppData, DataDirNameRadeon));
            if (cuda == radeon)
            {
                return false; // 両方在る／どちらも無い＝判らない
            }

            flavor = radeon ? ReleaseFlavor.Radeon : ReleaseFlavor.Cuda;
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
    /// <c>settings.json</c> の <c>dataDir</c> を 1 度だけ覗く（<b>無い・壊れている・空なら null</b>）。
    /// 版の樹に <c>settings.json</c> がまだ無い機体では、<b>まだ移していない旧い共有樹</b>の側も見る＝
    /// v1.x で置き場を指していた利用者が、v2.0 の初回起動で既定の樹へ落とされないようにする。
    /// </summary>
    private static string? ReadDataDirSetting(AppPaths resolved)
    {
        var found = ReadDataDirFrom(resolved.SettingsPath);
        if (found is not null)
        {
            return found;
        }

        var legacy = resolved.LegacyDataDir;
        return legacy is null ? null : ReadDataDirFrom(Path.Combine(legacy, "settings.json"));
    }

    private static string? ReadDataDirFrom(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return null;
            }

            var settings = System.Text.Json.JsonSerializer.Deserialize<LauncherSettings>(
                File.ReadAllText(settingsPath), JsonSettingsStore.JsonOptions);
            return Trim(settings?.DataDir);
        }
        catch (System.Text.Json.JsonException)
        {
            return null; // 壊れた設定 1 檔で起動不能にしない（ISettingsStore.Load と同じ流儀）
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
    /// 書き込み側のディレクトリを作る（導入先には 1 檔も作らない）。
    /// <para>
    /// <b>作る前に 1 度だけ移送を走らせる</b>（<c>decisions.md</c> 133 ⑸・<c>v2-spec.md</c> §11-8）＝
    /// 旧い共有樹が在って版の樹がまだ空なら、版の樹へ移す。ここに置く理由は<b>順</b>である＝
    /// この呼びは <c>App.OnStartup</c> の<b>いちばん最初にデータ樹へ触る 1 手</b>で、
    /// <c>settings.json</c> の読み（<c>AppServices.Settings</c>）よりも前に走る。後に回すと、
    /// 既定の設定を読んだ個体が移送で届いた <c>settings.json</c> を上書きしてしまう。
    /// </para>
    /// </summary>
    public void EnsureDataDirectories()
    {
        Services.Ledger.LegacyDataMigration.Run(this);

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
