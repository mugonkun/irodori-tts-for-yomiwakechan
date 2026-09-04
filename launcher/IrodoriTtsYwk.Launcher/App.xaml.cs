using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using IrodoriTtsYwk.Launcher.Contracts;
using Forms = System.Windows.Forms;

// Application.MainWindow（既存のプロパティ）と型名がぶつかるので別名で呼ぶ。
using MainWindowView = IrodoriTtsYwk.Launcher.Views.MainWindow;

namespace IrodoriTtsYwk.Launcher;

/// <summary>
/// ランチャの入り口。<b>この檔が持つのは 3 つだけ</b>＝
/// ⑴ 単一起動（Mutex）⑵ トレイ常駐（NotifyIcon）⑶ 終了時に wrapper をツリー kill。
/// 画面の中身は <see cref="MainWindow"/> と 3 席の View が持つ。
/// <para>
/// <b>なぜ常駐か</b>＝起動主体は配布版で（裁定 6・G-2）、本体（読み分けちゃん2）は <c>/health</c> で
/// 見つけるだけだから。窓を閉じても wrapper は走り続けねばならない。
/// </para>
/// <para>
/// <b>なぜ終了時にツリー kill か</b>＝上流に shutdown の路が無く（設計書 §2）、wrapper は窓を持たない
/// ので利用者に閉じる口が無い。放置個体がポートを塞ぐ。本体
/// <c>IrodoriServerProcessRegistry</c> と同じ流儀で<b>自分が起こした個体だけ</b>を片付ける。
/// </para>
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 単一起動の錠（<c>Local\</c>＝ログオンセッション単位。利用者ごとに 1 個体）。
    /// </summary>
    private const string MutexName = @"Local\irodori-tts-ywk-launcher";

    /// <summary>2 個目が 1 個目に「窓を出せ」と伝える口。</summary>
    private const string ActivateEventName = @"Local\irodori-tts-ywk-launcher-activate";

    /// <summary>終了時にサーバの後始末を待つ上限。</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);

    private Mutex? _singleInstance;
    private EventWaitHandle? _activateSignal;
    private CancellationTokenSource? _activateWatch;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _startItem;
    private Forms.ToolStripMenuItem? _stopItem;
    private MainWindowView? _window;
    private bool _shuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ⑴ 単一起動。既に居るなら、そちらの窓を出させて自分は静かに退く。
        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out var isFirst);
        if (!isFirst)
        {
            SignalExistingInstance();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        AppServices.Paths.EnsureDataDirectories();
        _ = AppServices.Settings; // 早い段階で読む（壊れていても既定で立ち上がる）

        // 実装を差すのはここ（トレイと窓が Server の StateChanged を購読する**前**）。
        Services.LauncherComposition.Compose();

        StartActivateWatch();
        CreateTrayIcon();

        _window = new MainWindowView();
        _window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;

        // ⑶ 起こした個体をツリー kill してから消える。待ちには上限を置く
        //    （落ちない個体のためにアプリが終われない、を作らない）。
        try
        {
            using var cts = new CancellationTokenSource(StopTimeout);
            AppServices.Server.StopAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // 期限切れ＝諦めて進む（次の起動でポートが塞がっていれば裁定 52 の告知が出る）
        }
        catch (InvalidOperationException)
        {
            // 既に落ちている個体を止めようとしただけ
        }

        try
        {
            AppServices.Server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
            // 同上
        }

        _activateWatch?.Cancel();
        _activateWatch?.Dispose();
        _activateSignal?.Dispose();

        if (_trayIcon is not null)
        {
            // Dispose しないとトレイにアイコンの残骸が残る
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        AppServices.Wrapper?.Dispose();

        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();

        base.OnExit(e);
    }

    /// <summary>窓を出して前に持ってくる（トレイの「開く」・2 個目の起動の合図）。</summary>
    public void ShowMainWindow()
    {
        if (_shuttingDown)
        {
            return;
        }

        _window ??= new MainWindowView();
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    // ---- トレイ -------------------------------------------------------------

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();

        var openItem = new Forms.ToolStripMenuItem("開く(&O)");
        openItem.Click += (_, _) => ShowMainWindow();

        _startItem = new Forms.ToolStripMenuItem("サーバ起動(&S)");
        _startItem.Click += (_, _) => RequestServerStart();

        _stopItem = new Forms.ToolStripMenuItem("サーバ停止(&T)");
        _stopItem.Click += (_, _) => RequestServerStop();

        var exitItem = new Forms.ToolStripMenuItem("終了(&X)");
        exitItem.Click += (_, _) => Shutdown();

        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_startItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new Forms.NotifyIcon
        {
            // 専用アイコンはまだ無い（便 E の資産）。無い物を差すより、在る物を差す。
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        AppServices.Server.StateChanged += OnServerStateChanged;
        UpdateTray(AppServices.Server.State);
    }

    private void OnServerStateChanged(object? sender, ServerStateChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() => UpdateTray(e.Current));

    /// <summary>メニューの可否と吹き出しの文言を状態に合わせる。</summary>
    private void UpdateTray(ServerState state)
    {
        if (_trayIcon is null)
        {
            return;
        }

        var running = state is ServerState.Starting or ServerState.Listening
            or ServerState.Ready or ServerState.Warming;

        // 起こした個体が台帳に居る間は止められる（Failed でも＝是正・2026-09-05）。
        // 窓の StatusViewModel.CanStop と同じ判定にする（片方だけ押せる状態を作らない）。
        var stoppable = running || AppServices.Server.ProcessId is not null;

        if (_startItem is not null)
        {
            _startItem.Enabled = !running;
        }

        if (_stopItem is not null)
        {
            _stopItem.Enabled = stoppable;
        }

        // Text は 63 字まで（Win32 の制約）＝短く保つ
        _trayIcon.Text = "irodori-TTS " + AppVersion.Display + "／" + StateLabel(state);
    }

    /// <summary>状態の日本語（UI は日本語のみ＝裁定 52）。</summary>
    public static string StateLabel(ServerState state) => state switch
    {
        ServerState.Stopped => "停止",
        ServerState.Starting => "起動中",
        ServerState.Listening => "読込中",
        ServerState.Ready => "待機",
        ServerState.Warming => "暖機中",
        ServerState.Failed => "失敗",
        _ => state.ToString(),
    };

    private void RequestServerStart() =>
        _window?.RequestServerStart();

    private void RequestServerStop() =>
        _ = StopServerAsync();

    private async Task StopServerAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(StopTimeout);
            await AppServices.Server.StopAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 期限切れは UI に出す仕事（窓側）
        }
    }

    // ---- 2 個目の起動 -------------------------------------------------------

    private void StartActivateWatch()
    {
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateWatch = new CancellationTokenSource();
        var token = _activateWatch.Token;
        var signal = _activateSignal;

        // 待つだけの背景（Task.Run＝スレッドを 1 本借りる）
        _ = Task.Run(
            () =>
            {
                var handles = new WaitHandle[] { signal, token.WaitHandle };
                while (!token.IsCancellationRequested)
                {
                    var which = WaitHandle.WaitAny(handles);
                    if (which != 0 || token.IsCancellationRequested)
                    {
                        return;
                    }

                    Dispatcher.BeginInvoke(ShowMainWindow);
                }
            },
            token);
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ActivateEventName);
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // 1 個目がまだ口を開けていない＝黙って退く（多重起動はしていない）
        }
        catch (UnauthorizedAccessException)
        {
            // 別の利用者の個体＝触らない
        }
    }
}
