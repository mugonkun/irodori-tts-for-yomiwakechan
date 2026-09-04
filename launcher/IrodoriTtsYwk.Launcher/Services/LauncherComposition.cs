using System;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Http;
using IrodoriTtsYwk.Launcher.Services.Server;
using IrodoriTtsYwk.Launcher.Services.Voices;

namespace IrodoriTtsYwk.Launcher.Services;

/// <summary>
/// 起動席（便 D・L2）の実装を <see cref="AppServices"/> に差す 1 箇所。
/// <para>
/// <b>差し替えは起動の 1 度だけ</b>（走行中に入れ替えない）＝
/// <c>App.OnStartup</c> の、トレイと窓を作る<b>前</b>に呼ぶ。後に呼ぶと、
/// トレイと窓が <see cref="NullServerProcess"/> の <c>StateChanged</c> を購読したままになる。
/// </para>
/// <para>
/// ここが差すのは⑴ サーバの子プロセス ⑵ GPU 列挙 ⑶ 話者台帳と <c>voices.json</c> の書き手だけ。
/// 取得（<see cref="IDownloader"/>・<see cref="IRuntimeInstaller"/>）は取得席が差す。
/// <see cref="AppServices.Wrapper"/> は<b>起きている個体にしか意味が無い</b>ので、
/// ready になってから <see cref="AttachWrapper"/> で差す。
/// </para>
/// </summary>
public static class LauncherComposition
{
    /// <summary>起動席の実装を差す（<b>冪等</b>＝2 度呼んでも 1 度目の個体を保つ）。</summary>
    public static void Compose()
    {
        if (AppServices.Server is not NullServerProcess)
        {
            return;
        }

        var paths = AppServices.Paths;

        AppServices.Server = new ServerProcess();
        AppServices.GpuEnumerator = new GpuEnumerator();
        AppServices.DriverCheck = new DriverRequirement();

        var store = new VoiceStore(paths);
        AppServices.VoiceStore = store;
        AppServices.VoicesJsonWriter = new VoicesJsonWriter();

        // 初回だけ＝プリセットを利用者データへ写して台帳を作る（配布樹は読むだけ）。
        // 失敗しても起動は止めない（話者 0 名でも「デフォルト」で合成できる＝裁定 45）。
        try
        {
            // 旧い置き場（voices_dir 直下）に残っている参照 wav を refs/ へ移す
            // （是正・2026-09-05＝直下に置くと上流の走査が檔名の幹を話者 id にして
            //   同じ話者が一覧に 2 件出る）。移してから voices.json を書き直す。
            if (store.MigrateReferences() > 0)
            {
                new VoicesJsonWriter().Write(paths.VoicesJsonPath, store.Load());
            }

            PresetVoices.InstallIfFirstRun(paths, store);
        }
        catch (System.IO.IOException)
        {
            // 置き場が無い・掴まれている＝話者は後から足せる
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
    }

    /// <summary>
    /// 起きている個体を叩く口を差す（ready になってから）。前の口は閉じる。
    /// </summary>
    public static IWrapperClient AttachWrapper(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        var previous = AppServices.Wrapper;
        var client = new WrapperClient(baseAddress);
        AppServices.Wrapper = client;
        previous?.Dispose();
        return client;
    }

    /// <summary>個体が止まったら口も閉じる。</summary>
    public static void DetachWrapper()
    {
        var previous = AppServices.Wrapper;
        AppServices.Wrapper = null;
        previous?.Dispose();
    }
}
