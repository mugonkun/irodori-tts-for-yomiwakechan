using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using IrodoriTtsYwk.Launcher.Contracts;
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
    /// <para>
    /// <b>版ごとに割る</b>（<c>decisions.md</c> 133 ⑷＝RTX（CUDA）と Radeon（ROCm）は別アプリ＝
    /// 互いを起こし直さない）。綴りの正本は <see cref="AppPaths.SingleInstanceMutexName"/> で、
    /// <c>installer/irodori-tts-ywk.iss</c> の <c>AppMutex</c> も同じ 2 つに割ってある。
    /// 2 つを同時に開いた回は、後から起きた側が <b>18088 の塞がり</b>で止まる。
    /// </para>
    /// </summary>
    private static string MutexName => AppPaths.SingleInstanceMutexName(AppServices.Paths.Flavor);

    /// <summary>
    /// 2 個目が 1 個目に「窓を出せ」と伝える口（<b>錠と必ず同じ組で割る</b>）。
    /// 割り忘れると、合図は <see cref="EventResetMode.AutoReset"/> の 1 本なので
    /// <b>CUDA 版の 2 個目の起動が Radeon 版の窓を前に出す</b>。
    /// </summary>
    private static string ActivateEventName => AppPaths.ActivateEventName(AppServices.Paths.Flavor);

    private Mutex? _singleInstance;
    private EventWaitHandle? _activateSignal;
    private CancellationTokenSource? _activateWatch;
    private MainWindowView? _window;
    private bool _shuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ⑴ 単一起動。既に居るなら、そちらの窓を出させて自分は静かに退く。
        //    **合図が届かなかった回は錠を待つ**（是正・検分）＝1 個目が終了の後始末
        //    （ツリー kill・最大 15 秒）に入っていると、窓はもう無いのにプロセスは生きている。
        //    そこで黙って退くと「押しても何も出ない」になり、本体の一括起動（裁定 103）も
        //    /health が上がらないまま失敗を読む。錠が空いたら自分が 1 個目になる。
        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out var isFirst);
        if (!isFirst)
        {
            var owned = false;
            if (ShutdownSequence.SecondInstanceShouldWait(SignalExistingInstance()))
            {
                try
                {
                    owned = _singleInstance.WaitOne(ShutdownSequence.SecondInstanceWait);
                }
                catch (AbandonedMutexException)
                {
                    // 1 個目が後始末を終える前に殺された＝錠はこちらの物になった
                    owned = true;
                }
            }

            if (!owned)
            {
                _singleInstance.Dispose();
                _singleInstance = null;
                Shutdown();
                return;
            }
        }

        AppServices.Paths.EnsureDataDirectories();
        _ = AppServices.Settings; // 早い段階で読む（壊れていても既定で立ち上がる）

        // 実装を差すのはここ（窓が Server の StateChanged を購読する**前**）。
        Services.LauncherComposition.Compose();

        StartActivateWatch();

        _window = new MainWindowView();

        // **閉じた窓を後から Show() しない**（是正・検分）＝裁定 124 で × ＝終了になった今、
        // 窓が閉じてから OnExit が走るまでの隙に 2 個目の合図が届くと、閉じた窓に Show() を
        // 撃って InvalidOperationException になる（受け口は無いので終了が墜落に化ける）。
        _window.Closed += (_, _) =>
        {
            _shuttingDown = true;
            _window = null;
        };

        _window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;

        // ⑵ **合図の口を先に畳む**（是正・検分）＝この後の後始末は最大 15 秒 UI の糸を止める。
        //    口を開けたまま待つと、その間に起きた 2 個目は「1 個目が居る」と読んで合図を送り、
        //    止まった Dispatcher に積まれた ShowMainWindow は誰にも実行されずに捨てられる。
        //    先に閉じておけば、2 個目は OpenExisting の失敗で「終わろうとしている」と判り、
        //    錠が空くのを待って自分が 1 個目になる（ShutdownSequence.SecondInstanceShouldWait）。
        _activateWatch?.Cancel();
        _activateWatch?.Dispose();
        _activateSignal?.Dispose();

        // ⑶ 起こした個体をツリー kill してから消える（VRAM を返す＝裁定 124）。
        //    待ちには上限を置く（落ちない個体のためにアプリが終われない、を作らない）。
        //    どの状態から閉じられても同じ 1 本を通る＝起動中・読込中・暖機中も止める。
        ShutdownSequence.StopServerTree(AppServices.Server, ShutdownSequence.DefaultStopTimeout);

        AppServices.Wrapper?.Dispose();

        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();

        base.OnExit(e);
    }

    /// <summary>窓を出して前に持ってくる（2 個目の起動の合図）。</summary>
    public void ShowMainWindow()
    {
        // **終わりかけ・閉じた後は何も作らない**（是正・検分）＝合図の路から窓を新しく建てない
        // （建てると裁定 124 の「閉じたら終わる」を合図 1 つで覆せてしまう）。
        if (_shuttingDown || _window is null)
        {
            return;
        }

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

    /// <summary>
    /// 1 個目へ「窓を出せ」と伝える。<b>届いたかを返す</b>（是正・検分）＝届かなかった回は
    /// 1 個目が終わろうとしている（合図の口は終了の頭で閉じる）ので、呼び手は錠を待つ。
    /// </summary>
    private static bool SignalExistingInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ActivateEventName);
            signal.Set();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // 1 個目がまだ口を開けていない、または終了の後始末に入って閉じた
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // 別の利用者の個体＝触らない（待っても空かない）
            return true;
        }
    }
}
