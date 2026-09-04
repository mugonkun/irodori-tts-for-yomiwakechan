using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Views;

/// <summary>
/// 主窓。<b>持つのは 4 つだけ</b>＝⑴ ViewModel の組み立て ⑵ 子プロセスの事象を UI スレッドへ
/// 渡す marshal ⑶ <c>/ywk/status</c> を定期に読む時計 ⑷ 初回取得ウィザードの開閉。
/// <para>
/// 判断も文言も <see cref="MainViewModel"/> 以下に在る。窓の × は閉じずにトレイへ隠す
/// （常駐が主・裁定 6）。
/// </para>
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>状態を読む間隔（暖機の進みが見える程度に短く・要求は軽い）。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IAudioPlayer _player = new NAudioPlayer();
    private readonly MainViewModel _model;
    private readonly DispatcherTimer _timer;
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
        HeaderText.Text = "変種 " + RuntimeVariants.DisplayName(AppServices.Settings.Variant)
            + (paths.DeveloperMode ? "／開発モード（" + paths.AppDir + "）" : string.Empty);

        StatusPage.SetMemoryPanelVisible(AppServices.Settings.ShowMemoryPanel);

        if (AppServices.SettingsStore.LastLoadError is string loadError)
        {
            _model.AppendLog(loadError);
        }

        AppServices.Server.StateChanged += OnServerStateChanged;
        AppServices.Server.LogLine += OnServerLogLine;
        _model.ApplyServerState(AppServices.Server.State, AppServices.Server.FailureReason);

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
        _timer.Tick += OnTick;
        _timer.Start();

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

        _timer.Stop();
        AppServices.Server.StateChanged -= OnServerStateChanged;
        AppServices.Server.LogLine -= OnServerLogLine;
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

        // ウィザードで変種・場所が変わりうる＝状態帯と話者を引き直す。
        _model.ReapplySettings();
        StatusPage.SetMemoryPanelVisible(AppServices.Settings.ShowMemoryPanel);
        _model.Voices.Reload();
    }

    private void OnServerStateChanged(object? sender, ServerStateChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() => _model.ApplyServerState(e.Current, e.Reason));

    private void OnServerLogLine(object? sender, ServerLogLineEventArgs e) =>
        Dispatcher.BeginInvoke(() => _model.AppendLog(e.Event.Line));

    private void OnTick(object? sender, EventArgs e) => _ = _model.PollStatusAsync();
}
