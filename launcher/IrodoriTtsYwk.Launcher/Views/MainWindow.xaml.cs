using System;
using System.ComponentModel;
using System.Windows;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Views;

/// <summary>
/// 主窓。<b>持つのは 3 つだけ</b>＝⑴ ViewModel の組み立て ⑵ 子プロセスの事象を UI スレッドへ
/// 渡す marshal ⑶ 初回取得ウィザードの開閉。
/// <para>
/// <b>窓は <c>/ywk/status</c> を叩かない</b>（low 3・裁定 88 ⑶）＝見張りは
/// <see cref="IServerProcess"/> の 1 本に寄せ、窓はその標本
/// （<see cref="IServerProcess.LatestStatus"/>／<see cref="IServerProcess.StatusSampled"/>）を
/// 読むだけである。時計（<c>DispatcherTimer</c>）は無くなった＝2 秒ごとの GET が生む
/// uvicorn の access log も、窓と状態機械の二重の書き手も消える。
/// </para>
/// <para>
/// 判断も文言も <see cref="MainViewModel"/> 以下に在る。窓の × は閉じずにトレイへ隠す
/// （常駐が主・裁定 6）。
/// </para>
/// </summary>
public partial class MainWindow : Window
{
    private readonly IAudioPlayer _player = new NAudioPlayer();
    private readonly MainViewModel _model;
    private bool _firstRunShown;

    public MainWindow()
    {
        InitializeComponent();

        // 起きた個体を叩く口の開け閉めは起動席（Services/LauncherComposition）の手を借りる。
        // ViewModel は名前で参照しない（手を渡すだけ＝檔の依存を作らない）。
        _model = new MainViewModel(
            AppServices.Paths,
            AppServices.Settings,
            AppServices.SettingsStore,
            _player,
            Services.LauncherComposition.AttachWrapper,
            Services.LauncherComposition.DetachWrapper);
        DataContext = _model;

        VersionText.Text = AboutViewModel.VersionText
            + "／" + (AppVersion.IsReleaseBuild ? AboutViewModel.UpstreamText : "開発ビルド");

        var paths = AppServices.Paths;

        // 窓題は版で分ける（裁定 109）。XAML 側の Title（MainWindow.xaml:5）は設計時（デザイナ）用の
        // 見本で、実行時は必ずこの行が版つきに差し替える＝素の幹が利用者の目に入る経路は無い。
        // 樹が読めないときも DetectFrom は投げず ReleaseFlavor.Cuda を返す＝「（CUDA 版）」と名乗る。
        Title = ReleaseFlavors.AppTitle(ReleaseFlavors.DetectFrom(paths.LedgerDir));

        HeaderText.Text = "変種 " + RuntimeVariants.DisplayName(AppServices.Settings.Variant)
            + (paths.DeveloperMode ? "／開発モード（" + paths.AppDir + "）" : string.Empty);

        if (AppServices.SettingsStore.LastLoadError is string loadError)
        {
            _model.AppendLog(loadError);
        }

        AppServices.Server.StateChanged += OnServerStateChanged;
        AppServices.Server.LogLine += OnServerLogLine;
        AppServices.Server.StatusSampled += OnStatusSampled;
        _model.ApplyServerState(AppServices.Server.State, AppServices.Server.FailureReason);
        _model.ApplyStatusSample(
            AppServices.Server.LatestStatus, AppServices.Server.LatestOsGpuMemory);

        Loaded += OnLoaded;
    }

    /// <summary>トレイの「サーバ起動」から呼ばれる。</summary>
    public void RequestServerStart() => _ = _model.StartServerAsync();

    protected override void OnClosing(CancelEventArgs e)
    {
        // 常駐が主（裁定 6）＝窓の × は隠すだけ。終わるのはトレイの「終了」。
        if (!e.Cancel && Application.Current is App)
        {
            e.Cancel = true;
            _player.Stop();
            Hide();
            return;
        }

        AppServices.Server.StateChanged -= OnServerStateChanged;
        AppServices.Server.LogLine -= OnServerLogLine;
        AppServices.Server.StatusSampled -= OnStatusSampled;
        _player.Dispose();
        base.OnClosing(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        // 初回取得を通していなければ、まずウィザードを出す（設計書 §6）。
        if (_model.NeedsFirstRun && !_firstRunShown)
        {
            ShowFirstRun();
            return;
        }

        if (AppServices.Settings.AutoStartServer)
        {
            _ = _model.StartServerAsync();
        }
    }

    private void OnFirstRunClick(object sender, RoutedEventArgs e) => ShowFirstRun();

    private void ShowFirstRun()
    {
        _firstRunShown = true;
        var wizard = new FirstRunWizard(_model.CreateFirstRun()) { Owner = this };
        wizard.ShowDialog();

        // ウィザードで変種・場所が変わりうる＝状態帯と話者を引き直す
        // （GPU メモリ欄の可否は状態帯の束縛が拾う＝low 6 の ⑸）。
        _model.ReapplySettings();
        _model.Voices.Reload();
    }

    private void OnServerStateChanged(object? sender, ServerStateChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() => _model.ApplyServerState(e.Current, e.Reason));

    private void OnServerLogLine(object? sender, ServerLogLineEventArgs e) =>
        Dispatcher.BeginInvoke(() => _model.AppendLog(e.Event.Line));

    /// <summary>
    /// 見張りが採った <c>/ywk/status</c> の標本（窓は読むだけ＝low 3）。
    /// <b>OS の GPU 計数も同じ回の物を読む</b>（裁定 110）＝見張りが標本の直後に置いた
    /// <see cref="IServerProcess.LatestOsGpuMemory"/> をそのまま配る。PDH を叩くのは
    /// <b>開設も collect も</b>見張りの糸で、UI の糸（ここ）では 1 度も走らない。
    /// </summary>
    private void OnStatusSampled(object? sender, StatusResponse e)
    {
        var osGpuMemory = AppServices.Server.LatestOsGpuMemory;
        Dispatcher.BeginInvoke(() => _model.ApplyStatusSample(e, osGpuMemory));
    }
}
