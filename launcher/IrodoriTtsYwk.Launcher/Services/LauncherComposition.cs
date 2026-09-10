using System;
using System.Collections.Generic;
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
/// <c>App.OnStartup</c> の、窓を作る<b>前</b>に呼ぶ。後に呼ぶと、
/// 窓が <see cref="NullServerProcess"/> の <c>StateChanged</c> を購読したままになる。
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
    private static readonly List<string> Notes = [];

    /// <summary>
    /// <b>窓が構えるより前に出た 1 行</b>の控え（裁定 121）。
    /// <para>
    /// <see cref="Compose"/> は <c>App.OnStartup</c> の、窓を作る<b>前</b>に走るので
    /// 状態帯のログ（<c>StatusViewModel.AppendLog</c>）はまだ無い。設定の読み損ない
    /// （<c>ISettingsStore.LastLoadError</c>）と同じ流儀で、主窓の構築が
    /// <see cref="DrainNotes"/> で引き取って流す。
    /// </para>
    /// </summary>
    public static void Note(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            Notes.Add(line.Trim());
        }
    }

    /// <summary>控えを 1 度だけ取り出す（<b>取ったら空になる</b>＝窓を開き直しても 2 度出ない）。</summary>
    public static IReadOnlyList<string> DrainNotes()
    {
        if (Notes.Count == 0)
        {
            return [];
        }

        var drained = Notes.ToArray();
        Notes.Clear();
        return drained;
    }

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
            PrepareVoices(paths, store, new VoicesJsonWriter(), out var renamed, out var added);

            // 改名（裁定 108）＝設定に残った旧い id も同じ回で改める。並びと試し撃ちは知らない
            // id を捨てるだけだが、暖機は旧い id で走行ごと failed になる（RenameInSettings）。
            if (PresetVoices.RenameInSettings(AppServices.Settings, renamed))
            {
                AppServices.SaveSettings();
            }

            // 後の版で増えた同梱の話者（裁定 121）＝黙って増やさず 1 行残す。窓はまだ無いので
            // 控えに積み、主窓が構えたところで状態帯のログへ流す（Note の説明を見よ）。
            if (PresetVoices.AddedLine(added) is string line)
            {
                Note(line);
            }
        }
        catch (System.IO.IOException)
        {
            // 置き場が無い・掴まれている＝話者は後から足せる
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
        catch (ArgumentException)
        {
            // 台帳の中身（鍵）が壊れていても起動は止めない（裁定 45＝話者 0 名でも合成できる）。
        }
    }

    /// <summary>
    /// 起動の前に話者の置き場を整える（<b>テストの継ぎ目は public</b>＝実機に触れずに撃てる）。
    /// <para>
    /// ⑴ 旧い置き場（<c>voices_dir</c> 直下）に残っている参照 wav を <c>refs/</c> へ移す
    /// （是正・2026-09-05＝直下に置くと上流の走査が檔名の幹を話者 id にして同じ話者が一覧に 2 件出る）。
    /// ⑵ 初回だけプリセットを利用者データへ写す（配布樹は読むだけ）。
    /// ⑶ <b>いまの台帳を <c>voices.json</c> に書く</b>（統合席 §19・裁定 78 ⑴・<b>毎回</b>＝裁定 126 の C（2））。
    /// ⑷ <b>改名の引き継ぎ</b>（裁定 108）＝配布側が <c>display_name</c>（＝話者 id）を変えた分を
    /// 利用者の台帳へ写す（<see cref="Voices.PresetVoices.MigrateRenamed"/>）。
    /// </para>
    /// <para>
    /// ⑷ は<b>⑵ の前</b>に走る。後に回すと、<c>presets_installed</c> の印が無い台帳
    /// （<c>presets.json</c> を読めていなかった回の残骸）で ⑵ が新しい名を先に足してしまい、
    /// 旧い名と 2 名並ぶ＝同じ wav を指す重複ができる。改名が 1 件でも起きたら ⑶ も走らせる
    /// （別名表の鍵が変わる＝旧い鍵は台帳に居ないので落ちる）。
    /// </para>
    /// <para>
    /// ⑶ が要る理由＝<see cref="Voices.PresetVoices"/> が書くのはランチャの台帳
    /// （<c>voices.ywk.json</c>）だけで、<b>上流が読む別名表には 1 行も入らない</b>。
    /// 載せないままだと wrapper はプリセットを 1 名も知らず、<c>GET /v1/audio/voices</c> にも
    /// 試し撃ちにも 12 名が出てこない（一覧はランチャの台帳だけが持つので画面では見える＝
    /// <b>見えるのに合成できない</b>形になる）。wrapper の起動時の事前計算 <c>all</c> は
    /// <b>ready 時点の <c>voices.json</c></b> を見るので、ここで書いてあれば
    /// <c>rocm-*</c> 変種は起動のたびに自動で焼ける（裁定 78 ⑴）。
    /// 檔が既に在るときは書き手が読み直して <c>ref_latent</c> を残す（契約 ⑷ 4-3）。
    /// </para>
    /// </summary>
    /// <returns>写したプリセットの数（<c>voices.json</c> を書いたかは <c>File.Exists</c> で判る）。</returns>
    public static int PrepareVoices(AppPaths paths, VoiceStore store, IVoicesJsonWriter writer) =>
        PrepareVoices(paths, store, writer, out _, out _);

    /// <summary>
    /// 同上＋<b>改めた名を返す</b>（裁定 108）＝呼ぶ側が設定の旧い id も直せる
    /// （<see cref="Voices.PresetVoices.RenameInSettings"/>）。
    /// </summary>
    /// <param name="renamed">この回で改めた名（無ければ空）。</param>
    public static int PrepareVoices(
        AppPaths paths,
        VoiceStore store,
        IVoicesJsonWriter writer,
        out IReadOnlyList<PresetRename> renamed) =>
        PrepareVoices(paths, store, writer, out renamed, out _);

    /// <summary>
    /// 同上＋<b>後の版で増えて足した話者を返す</b>（裁定 121）＝呼ぶ側がログに 1 行残せる。
    /// <para>
    /// ⑸ <b>増えたプリセットを足す</b>（<see cref="Voices.PresetVoices.InstallNew"/>）は
    /// ⑷ 改名の<b>後</b>・⑵ 初回展開の<b>後</b>に走る。改名の後でなければ、旧い名で持っている
    /// 1 名を「まだ差し出していない」と読んで新しい名でもう 1 名足してしまう（同じ wav を
    /// 指す 2 名）。初回展開の後なら、その回に全員を入れた台帳には足す物が残らない。
    /// </para>
    /// </summary>
    /// <param name="renamed">この回で改めた名（無ければ空）。</param>
    /// <param name="added">この回で足した同梱の話者 id（無ければ空）。</param>
    public static int PrepareVoices(
        AppPaths paths,
        VoiceStore store,
        IVoicesJsonWriter writer,
        out IReadOnlyList<PresetRename> renamed,
        out IReadOnlyList<string> added)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(writer);

        store.MigrateReferences();
        renamed = PresetVoices.MigrateRenamed(paths, store);
        var copied = PresetVoices.InstallIfFirstRun(paths, store);
        added = PresetVoices.InstallNew(paths, store);

        // **毎回書く**（裁定 126 の C（2））。1 巡目は「この回で何かが動いたか、檔が無いか」を
        // 条件にしていたので、**サーバを止めている間に台帳を直した回**（話者一覧の編集・
        // 手で戻した voices.ywk.json・別名表を消してしまった機体）が別名表に届かず、
        // 起こし直しても `GET /v1/audio/voices` には古い顔ぶれが出た。ここは起動の前
        //（サーバはまだ走っていない）なので、書き直しても走っている個体の読みとは競合しない。
        // 書き手は既に在る檔を読み直して `ref_latent` を残す（契約 ⑷ 4-3）＝焼いた潜在は消えない。
        writer.Write(paths.VoicesJsonPath, store.Load());

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
