using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using IrodoriTtsYwk.Launcher.Services.Server;

// Application.MainWindow（既存のプロパティ）と型名がぶつかるので別名で呼ぶ。
using MainWindowView = IrodoriTtsYwk.Launcher.Views.MainWindow;

namespace IrodoriTtsYwk.Launcher;

/// <summary>
/// ランチャの入り口。<b>この檔が持つのは 3 つだけ</b>＝
/// ⑴ 単一起動（Mutex）⑵ 2 個目の起動で 1 個目の窓を前に出す合図 ⑶ 終了時に wrapper をツリー kill。
/// 画面の中身は <see cref="MainWindow"/> と 3 席の View が持つ。
/// <para>
/// <b>常駐しない</b>（裁定 124＝裁定 6 の反転・司令官の評価の逐語は
/// <see cref="ShutdownSequence"/> に引いてある）＝窓の × でアプリが終わり、
/// 終われば起こした wrapper をツリー kill して VRAM を返す。トレイのアイコンも常駐のメニューも無い。
/// 最小化は<b>タスクバー</b>へ落ちる。本体（読み分けちゃん2）は従来どおり <c>/health</c> で見つける
/// （裁定 103＝本体の一括起動が引数なしでこの exe を起こしてよい・プロセスは持たない）。
/// </para>
/// <para>
/// <b>なぜ終了時にツリー kill か</b>＝上流に shutdown の路が無く（設計書 §2）、wrapper は窓を持たない
/// ので利用者に閉じる口が無い。放置個体がポートを塞ぎ、VRAM を握り続ける。本体
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

    private Mutex? _singleInstance;
    private EventWaitHandle? _activateSignal;
    private CancellationTokenSource? _activateWatch;
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

        // 実装を差すのはここ（窓が Server の StateChanged を購読する**前**）。
        Services.LauncherComposition.Compose();

        StartActivateWatch();

        _window = new MainWindowView();
        _window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;

        // ⑶ 起こした個体をツリー kill してから消える（VRAM を返す＝裁定 124）。
        //    待ちには上限を置く（落ちない個体のためにアプリが終われない、を作らない）。
        //    どの状態から閉じられても同じ 1 本を通る＝起動中・読込中・暖機中も止める。
        ShutdownSequence.StopServerTree(AppServices.Server, ShutdownSequence.DefaultStopTimeout);

        _activateWatch?.Cancel();
        _activateWatch?.Dispose();
        _activateSignal?.Dispose();

        AppServices.Wrapper?.Dispose();

        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();

        base.OnExit(e);
    }

    /// <summary>窓を出して前に持ってくる（2 個目の起動の合図）。</summary>
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
