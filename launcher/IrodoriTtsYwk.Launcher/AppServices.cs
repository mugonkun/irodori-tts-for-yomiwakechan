using System;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher;

/// <summary>
/// 組み立ての 1 箇所（<b>3 席の継ぎ目</b>）。
/// <para>
/// 骨組み席は<b>形</b>と<b>既定の繋ぎ</b>だけを置く。実装席は <see cref="App.OnStartup"/> の後の
/// <c>Compose</c> で自分の実装を差す＝
/// <c>AppServices.Server = new ServerProcess(...)</c>／<c>AppServices.Downloader = ...</c> のように。
/// </para>
/// <para>
/// <b>差し替えは起動の 1 度だけ</b>（走行中に入れ替えない）。窓も試し撃ちもここから読むので、
/// 席ごとに別の設定・別のパスを持たない。
/// </para>
/// </summary>
public static class AppServices
{
    private static AppPaths? _paths;
    private static ISettingsStore? _settingsStore;
    private static LauncherSettings? _settings;
    private static IServerProcess _server = new NullServerProcess();
    private static IDownloader? _downloader;
    private static IRuntimeInstaller? _runtimeInstaller;
    private static Services.Security.ISmartAppControlPolicyReader? _smartAppControlReader;
    private static Services.Security.SmartAppControlState? _smartAppControl;

    /// <summary>場所（exe の隣・利用者データ・変種ディレクトリ）。</summary>
    public static AppPaths Paths
    {
        get => _paths ??= AppPaths.FromEnvironment();
        set => _paths = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>settings.json の読み書き。</summary>
    public static ISettingsStore SettingsStore
    {
        get => _settingsStore ??= new JsonSettingsStore(Paths.SettingsPath);
        set => _settingsStore = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>いまの設定（<see cref="SaveSettings"/> で書き戻す）。</summary>
    public static LauncherSettings Settings
    {
        get => _settings ??= SettingsStore.Load();
        set => _settings = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>wrapper の子プロセス（既定は何も起こさない繋ぎ）。</summary>
    public static IServerProcess Server
    {
        get => _server;
        set => _server = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>走っている個体を叩く口（起動席が Ready のときに差す）。</summary>
    public static IWrapperClient? Wrapper { get; set; }

    /// <summary>
    /// 初回取得（取得席が差す。既定は <see cref="Services.Ledger.HttpDownloader"/>＝
    /// Range 再開・sha256・<c>.part</c>→Rename・fallback_url・並列 2 本）。
    /// <b>型は null 許容のまま</b>＝既に <c>is null</c> で見ている呼び手を壊さないため。
    /// </summary>
    public static IDownloader? Downloader
    {
        get => _downloader ??= new Services.Ledger.HttpDownloader();
        set => _downloader = value;
    }

    /// <summary>
    /// 展開（取得席が差す。既定は <see cref="Services.Ledger.WheelInstaller"/>＝
    /// <c>build/assemble-runtime.ps1</c> と同じ規則で変種ディレクトリを組む）。
    /// </summary>
    public static IRuntimeInstaller? RuntimeInstaller
    {
        get => _runtimeInstaller ??= new Services.Ledger.WheelInstaller();
        set => _runtimeInstaller = value;
    }

    /// <summary>GPU 列挙（起動席が差す）。</summary>
    public static IGpuEnumerator? GpuEnumerator { get; set; }

    /// <summary>ドライバ検査（純関数なので既定を持たせておく）。</summary>
    public static IDriverCheck DriverCheck { get; set; } = new DriverRequirement();

    /// <summary>
    /// Smart App Control の状態を読む口（<c>decisions.md</c> 140・v2.0.2）。
    /// 既定は本物の登録簿＝<b>読むだけ・投げない</b>。試験は偽物を差す。
    /// </summary>
    public static Services.Security.ISmartAppControlPolicyReader SmartAppControlReader
    {
        get => _smartAppControlReader ??= new Services.Security.RegistryPolicyReader();
        set
        {
            _smartAppControlReader = value;
            _smartAppControl = null;
        }
    }

    /// <summary>
    /// いまの Smart App Control の状態（<b>1 起動に 1 度だけ読む</b>）。
    /// <para>
    /// 走行中に切り替わる物ではない（切り替えには再起動が要る）ので覚えておく＝
    /// ウィザードの告知・設定 › 詳細の 1 行・帯の畳みが同じ 1 つの事実を見る。
    /// </para>
    /// </summary>
    public static Services.Security.SmartAppControlState SmartAppControl =>
        _smartAppControl ??= Services.Security.SmartAppControl.Read(SmartAppControlReader);

    /// <summary>話者台帳（UI 席が差す）。</summary>
    public static IVoiceStore? VoiceStore { get; set; }

    /// <summary>上流が読む別名表の書き手（UI 席が差す）。</summary>
    public static IVoicesJsonWriter? VoicesJsonWriter { get; set; }

    /// <summary>設定を書き戻す（原子的）。</summary>
    public static void SaveSettings() => SettingsStore.Save(Settings);

    /// <summary>テスト・再起動用に全部を初期化（実装は保持しない）。</summary>
    public static void Reset()
    {
        _paths = null;
        _settingsStore = null;
        _settings = null;
        _server = new NullServerProcess();
        Wrapper = null;
        (_downloader as IDisposable)?.Dispose();
        _downloader = null;
        _runtimeInstaller = null;
        _smartAppControlReader = null;
        _smartAppControl = null;
        GpuEnumerator = null;
        DriverCheck = new DriverRequirement();
        VoiceStore = null;
        VoicesJsonWriter = null;
    }
}
