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
            PrepareVoices(paths, store, new VoicesJsonWriter());
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
    /// 起動の前に話者の置き場を整える（<b>テストの継ぎ目は public</b>＝実機に触れずに撃てる）。
    /// <para>
    /// ⑴ 旧い置き場（<c>voices_dir</c> 直下）に残っている参照 wav を <c>refs/</c> へ移す
    /// （是正・2026-09-05＝直下に置くと上流の走査が檔名の幹を話者 id にして同じ話者が一覧に 2 件出る）。
    /// ⑵ 初回だけプリセットを利用者データへ写す（配布樹は読むだけ）。
    /// ⑶ <b>写した話者を <c>voices.json</c> にも載せる</b>（統合席 §19・裁定 78 ⑴）。
    /// </para>
    /// <para>
    /// ⑶ が要る理由＝<see cref="Voices.PresetVoices"/> が書くのはランチャの台帳
    /// （<c>voices.ywk.json</c>）だけで、<b>上流が読む別名表には 1 行も入らない</b>。
    /// 載せないままだと wrapper はプリセットを 1 名も知らず、<c>GET /v1/audio/voices</c> にも
    /// 試し撃ちにも 11 名が出てこない（一覧はランチャの台帳だけが持つので画面では見える＝
    /// <b>見えるのに合成できない</b>形になる）。wrapper の起動時の事前計算 <c>all</c> は
    /// <b>ready 時点の <c>voices.json</c></b> を見るので、ここで書いてあれば
    /// <c>rocm-*</c> 変種は起動のたびに自動で焼ける（裁定 78 ⑴）。
    /// 檔が既に在るときは書き手が読み直して <c>ref_latent</c> を残す（契約 ⑷ 4-3）。
    /// </para>
    /// </summary>
    /// <returns>写したプリセットの数（<c>voices.json</c> を書いたかは <c>File.Exists</c> で判る）。</returns>
    public static int PrepareVoices(AppPaths paths, VoiceStore store, IVoicesJsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(writer);

        var moved = store.MigrateReferences();
        var copied = PresetVoices.InstallIfFirstRun(paths, store);

        if (moved > 0 || copied > 0 || !System.IO.File.Exists(paths.VoicesJsonPath))
        {
            writer.Write(paths.VoicesJsonPath, store.Load());
        }

        return copied;
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
