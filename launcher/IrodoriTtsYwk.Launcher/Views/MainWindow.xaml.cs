using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Logging;
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
/// 判断も文言も <see cref="MainViewModel"/> 以下に在る。<b>窓の × はアプリを終える</b>
/// （裁定 124＝裁定 6 の反転）＝閉じれば <c>App.OnExit</c> が wrapper をツリー kill して VRAM を返す。
/// 隠す枝は無い（<see cref="Services.Server.ShutdownSequence.WindowCloseHidesToTray"/>）。
/// </para>
/// </summary>
public partial class MainWindow : Window
{
    private readonly IAudioPlayer _player = new NAudioPlayer();
    private readonly MainViewModel _model;
    private ReleaseFlavor _flavor = ReleaseFlavor.Cuda;
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

        // v2.0 段 C＝**版の番号だけ**（`v2-copy.md` §1-1 の 33 行目）＝名札（RTX（CUDA））も
        // 元になった実装の pin の綴りも 〔このアプリについて〕 の側にだけ出す。
        VersionText.Text = AppVersion.Display;

        var paths = AppServices.Paths;

        // 窓題は版で分ける（裁定 109 → v2.0 段 F-1）。XAML 側の Title（MainWindow.xaml:5）は
        // 設計時（デザイナ）用の見本で、実行時は必ずこの行が版つきに差し替える＝
        // 素の幹が利用者の目に入る経路は無い。
        // 樹が読めないときも DetectFrom は投げず ReleaseFlavor.Cuda を返す＝「－ RTX（CUDA）」と名乗る。
        _flavor = ReleaseFlavors.DetectFrom(paths.LedgerDir);
        Title = ReleaseFlavors.AppTitle(_flavor);

        // **開発ビルドのときだけ本文を入れる**（v2.0 段 A-1）＝要素と id（MainHeaderText）は残す。
        // 出来上がりの配布物では 1 文字も出ない＝主画面から「変種」の語が消える。
        HeaderText.Text = AppVersion.IsReleaseBuild
            ? string.Empty
            : "変種 " + RuntimeVariants.DisplayName(AppServices.Settings.Variant)
              + (paths.DeveloperMode ? "／開発モード（" + paths.AppDir + "）" : string.Empty);

        if (AppServices.SettingsStore.LastLoadError is string loadError)
        {
            _model.AppendLog(loadError);
        }

        // 窓が構えるより前（LauncherComposition.Compose）に出た 1 行＝同梱の話者が増えた等
        // （裁定 121）。控えは 1 度だけ流れる。
        foreach (var note in Services.LauncherComposition.DrainNotes())
        {
            _model.AppendLog(note);
        }

        // 状態帯の「取得へ進む」（裁定 121）＝ウィザードを開けるのは窓だけなので、ここで繋ぐ。
        StatusPage.AcquireRequested += (_, _) => ShowFirstRun();

        // 「初回取得をやり直す」は設定 › 詳細へ要素ごと移った（v2.0 段 A-1）。
        // **ウィザードは窓が要る仕事**なので、押された事実だけを受け取って窓側で開く。
        // **この 1 押しだけは「アプリに決め直させる」回である**（是正・検分）＝利用者は
        // やり直したくて押しているので、設定に残っている変種を明示の選択として固めない
        // （固めると、ドライバを上げても古い変種のまま勧めが効かなくなる）。
        SettingsPage.FirstRunRequested += (_, _) => ShowFirstRun(decideAgain: true);

        // v2.0 段 C＝設定 › ふだんの 4 つと このアプリについて の 2 釦。
        // **開ける仕事は窓が 1 箇所で持つ**（在り処ごと開く 1 本を 3 度書かない）。
        SettingsPage.OpenLogRequested += (_, _) => OpenLog();
        SettingsPage.OpenFolderRequested += (_, path) => OpenFolder(path);
        AboutPage.GuideRequested += (_, _) => OpenGuide();

        // 〔報告用のログを保存〕は〔ログを開く〕とは**別の仕事**である（是正・段 C の検分）＝
        // 記録を 1 檔にまとめ、その檔の路を画面へ返す（v2-spec.md §2-6・v2-copy.md §8）。
        AboutPage.SaveLogRequested += (_, e) => e.Path = SaveReportLog();
        AboutPage.OpenReportFolderRequested += (_, e) => SelectInExplorer(e.Path);

        RefreshLogButton();

        // **檔が生えた瞬間に釦を出す**（是正・検分）＝当日のログ檔を作るのは
        // `Status.LogSink`（＝`LauncherLogFile.Append`）が書く**最初の 1 行**で、それは
        // サーバの記録行とは限らない。清潔導入の初回起動で起こす前に断った回（A1〜A4・
        // つなぎ口の塞がり）は子が 1 行も吐かないので、`OnServerLogLine` だけを頼りにすると
        // **いちばん報告が要る回でだけ〔ログを開く〕が消えた**。`LogText` は画面に出た 1 行ごとに
        // 必ず動くので、そこに相乗りする。
        _model.Status.PropertyChanged += OnStatusPropertyChanged;

        // **下限に届かない変種で保存されている機体を取得へ連れて行く**（裁定 126 ⑽）＝
        // 判断も文言も ViewModel の側に在り、窓は求めに応じて 1 枚開くだけである。
        _model.WizardRequested += OnWizardRequested;

        AppServices.Server.StateChanged += OnServerStateChanged;
        AppServices.Server.LogLine += OnServerLogLine;
        AppServices.Server.StatusSampled += OnStatusSampled;
        _model.ApplyServerState(AppServices.Server.State, AppServices.Server.FailureReason);
        _model.ApplyStatusSample(
            AppServices.Server.LatestStatus, AppServices.Server.LatestOsGpuMemory);

        Loaded += OnLoaded;
    }

    /// <summary>
    /// 窓を閉じる＝<b>アプリを終える</b>（裁定 124）。
    /// <para>
    /// <b>取り消すのは 1 つの回だけ</b>（憲章 §4-21 の後半・是正・段 G・medium 3）＝
    /// <b>配信中に読み分けちゃん2 が読み上げに使っている間</b>に × を押したら 1 行だけ確かめる。
    /// 「いいえ」なら <c>e.Cancel</c> を真にして<b>何も片づけずに</b>返る（購読も
    /// <c>_player</c> も生かしたままにする＝ここで外すと、閉じるのをやめた窓が抜け殻になる）。
    /// 走っていない回・無人の回（<c>WizardSilent</c> 相当）は今までどおり素通りする。
    /// </para>
    /// <para>
    /// そのほかは<b>取り消さない</b>（隠さない）＝鳴っている音を止め、見張りの購読を外して閉じる。
    /// 閉じた窓が最後の 1 枚なら（<c>App.xaml</c> の <c>ShutdownMode=OnLastWindowClose</c>）
    /// <c>App.OnExit</c> が続き、そこで wrapper をツリー kill し切ってからプロセスが消える
    /// ＝VRAM が返る（<see cref="Services.Server.ShutdownSequence"/>）。
    /// 起動中・読込中・暖機中に閉じても同じ。
    /// </para>
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // 確かめは**片づけより先**（片づけたあとで取り消すと、窓は生きているのに標本が届かない）。
        if (_model.Status.HostBusy
            && MessageBox.Show(
                this,
                UiStrings.ExitWhileHostBusy,
                Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        _player.Stop();

        AppServices.Server.StateChanged -= OnServerStateChanged;
        AppServices.Server.LogLine -= OnServerLogLine;
        AppServices.Server.StatusSampled -= OnStatusSampled;
        _model.Status.PropertyChanged -= OnStatusPropertyChanged;
        _model.WizardRequested -= OnWizardRequested;
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

        // **札は立っているのに実体が無い**（裁定 121・司令官の報告 2026-09-10）＝
        // 古いデータ樹・消したモデル置き場では「サーバ起動」が断られるか、起こした個体が
        // offline でモデルを読めずに落ちる。そこに取得への導線が無かったので、
        // <b>自動起動はやめて</b>ウィザードを出す（閉じれば主窓はそのまま使える）。
        if (_model.NeedsAcquisition && !_firstRunShown)
        {
            var notice = MainViewModel.AcquisitionNotice(_model.AcquisitionSummary);
            _model.AppendLog(notice);
            ShowFirstRun(notice);
            return;
        }

        if (AppServices.Settings.AutoStartServer)
        {
            _ = _model.StartServerAsync();
        }
    }

    /// <summary>
    /// <b>ViewModel がウィザードを求めた</b>（裁定 126 ⑽＝保存された変種がドライバの下限に
    /// 届かない）。<b>その場では開かない</b>（<c>BeginInvoke</c>）＝求めは
    /// <c>StartServerAsync</c> の途中で上がるので、ここで <c>ShowDialog</c> を回すと
    /// 断りの後始末がウィザードを閉じるまで止まる。
    /// </summary>
    private void OnWizardRequested(object? sender, FirstRunRequest e) =>
        Dispatcher.BeginInvoke(() => ShowFirstRun(e.Notice, e.Variant, e.DriverVersion));

    /// <param name="notice">
    /// ウィザードの Trail に先に置いておく 1 行（裁定 121＝なぜ勝手に開いたかを書く）。null＝置かない。
    /// </param>
    /// <param name="preselect">
    /// 変種の段の初期値（裁定 126 ⑽＝勧める変種。null＝ウィザードの既定に任せる）。
    /// </param>
    /// <param name="driverVersion">その判断に使ったドライバの版（null＝ウィザード自身の検分に任せる）。</param>
    /// <param name="decideAgain">
    /// 〔はじめの準備をやり直す〕から開いた回＝真（是正・検分）＝設定に残っている変種を
    /// 「利用者の明示の選択」として固めない＝アプリがこの機体に合わせて決め直す。
    /// </param>
    private void ShowFirstRun(
        string? notice = null,
        string? preselect = null,
        string? driverVersion = null,
        bool decideAgain = false)
    {
        _firstRunShown = true;
        var firstRun = _model.CreateFirstRun(openedForAcquisition: !decideAgain);
        if (notice is not null)
        {
            firstRun.Note(notice);
        }

        if (preselect is not null)
        {
            firstRun.Preselect(preselect, driverVersion);
        }

        var wizard = new FirstRunWizard(firstRun) { Owner = this };
        wizard.ShowDialog();

        // ウィザードで変種・場所が変わりうる＝状態帯と話者を引き直す
        // （GPU メモリ欄の可否は状態帯の束縛が拾う＝low 6 の ⑸）。
        _model.ReapplySettings();
        _model.Voices.Reload();
    }

    private void OnServerStateChanged(object? sender, ServerStateChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() => _model.ApplyServerState(e.Current, e.Reason));

    /// <summary>
    /// 画面に出た 1 行が増えた＝当日のログ檔が生まれた（かもしれない）＝〔ログを開く〕を見直す。
    /// <b>ここは在否を見るだけ</b>で、檔を開きはしない。
    /// </summary>
    private void OnStatusPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StatusViewModel.LogText))
        {
            return;
        }

        // 1 行は UI の糸から積むのが常だが、**釦は UI の糸でしか触れない**ので念のため渡す。
        if (Dispatcher.CheckAccess())
        {
            RefreshLogButton();
            return;
        }

        Dispatcher.BeginInvoke(() => RefreshLogButton());
    }

    private void OnServerLogLine(object? sender, ServerLogLineEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            _model.AppendLog(e.Event.Line);
            RefreshLogButton();
        });

    // ===================== 状態の帯と歯車の層（v2.0 段 A・v2-spec.md §2-1）=====================

    /// <summary>
    /// 歯車＝「詳しい状態」と「このアプリについて」の層を開け閉てする。
    /// <b>別窓にしない</b>＝同じ窓の中の層なので、無人検分の root は主窓のままである。
    /// </summary>
    private void OnGearClick(object sender, RoutedEventArgs e) =>
        DetailOverlay.Visibility = DetailOverlay.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    private void OnDetailCloseClick(object sender, RoutedEventArgs e) =>
        DetailOverlay.Visibility = Visibility.Collapsed;

    /// <summary>
    /// 帯の「次の 1 手」（憲章 原則 6 の ⑶）。<b>何をするかは純関数が決め</b>
    /// （<see cref="BandText.For"/> が返す <see cref="BandActionKind"/>）、
    /// <b>窓はそれを実行するだけ</b>である。
    /// </summary>
    private async void OnBandActionClick(object sender, RoutedEventArgs e)
    {
        // **投げ捨てにしない**＝async void の中で漏れた例外はアプリごと落とす。
        // ここは 1 手を撃つだけの席なので、落ちた理由は帯とログに残して窓は生かす。
        try
        {
            await RunBandActionAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _model.AppendLog("次の 1 手が失敗しました：" + Mvvm.AsyncRelayCommand.Describe(ex));
        }
    }

    private async System.Threading.Tasks.Task RunBandActionAsync()
    {
        switch (_model.Status.BandAction)
        {
            case BandActionKind.Start:
                await _model.StartServerAsync().ConfigureAwait(true);
                break;

            case BandActionKind.Restart:
                await _model.StopServerAsync().ConfigureAwait(true);
                await _model.StartServerAsync().ConfigureAwait(true);
                break;

            case BandActionKind.FirstRun:
                ShowFirstRun();
                break;

            case BandActionKind.RebuildRuntime:
                await _model.RebuildRuntimeAsync().ConfigureAwait(true);
                break;

            case BandActionKind.OpenSettings:
                OpenSettingsAdvanced();
                break;

            case BandActionKind.OpenLog:
                OpenLog();
                break;

            case BandActionKind.OpenDriverPage:
                OpenExternal(BandText.DriverPageUrl);
                break;

            case BandActionKind.OpenGuide:
                OpenGuide();
                break;

            // Windows の設定のスマート アプリ コントロールの頁（decisions.md 140・v2.0.2）。
            // **連れて行くだけ**＝こちらは 1 つも切らない。頁の綴りが無い版のために
            // Windows セキュリティそのものへ落ちる道を持つ。
            case BandActionKind.OpenSmartAppControl:
                OpenSmartAppControlSettings();
                break;

            case BandActionKind.None:
            default:
                break;
        }
    }

    private void OnOpenLogClick(object sender, RoutedEventArgs e) => OpenLog();

    /// <summary>
    /// 設定 › 詳細を開く＝タブを選び、畳みを開く（釦を移した先へ利用者を連れて行く）。
    /// </summary>
    private void OpenSettingsAdvanced()
    {
        DetailOverlay.Visibility = Visibility.Collapsed;
        MainTabs.SelectedItem = SettingsTab;
        SettingsPage.OpenAdvanced();
    }

    /// <summary>
    /// 当日のログを<b>在り処ごと</b>開く（関連付けの無い機体でも必ず開く）。
    /// 檔が無い回は釦を出さないので、ここへは来ない。
    /// </summary>
    private void OpenLog()
    {
        var path = CurrentLogPath();
        if (path is null || !File.Exists(path))
        {
            _model.AppendLog(UiStrings.NoLogYet);
            RefreshLogButton();
            return;
        }

        SelectInExplorer(path);
    }

    /// <summary>1 つの檔を在り処ごと選んで開く（路が無い・檔が無い回は何もしない）。</summary>
    private void SelectInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        StartShell("explorer.exe", "/select,\"" + path + "\"");
    }

    /// <summary>
    /// <b>報告用に記録を 1 檔へまとめる</b>（`v2-spec.md` §2-6＝「1 檔にまとめて<b>その檔の場所を
    /// 出すだけ</b>」・`v2-copy.md` §8 の 1 行）。
    /// <para>
    /// 集めるのは <c>AppPaths.LogDir</c> に在る記録の檔（新しい順に最大 5 日ぶん）。
    /// <b>送らない・開かない</b>＝出来た檔の路を返すだけで、送り先は隣の 1 行が指す
    /// （X の @yomiwakechan＝裁定 131）。まとめられなければ null。
    /// </para>
    /// </summary>
    private string? SaveReportLog()
    {
        try
        {
            var dir = AppServices.Paths.LogDir;
            if (!Directory.Exists(dir))
            {
                return null;
            }

            var sources = Directory.GetFiles(dir, "*.log")
                .Where(path => !Path.GetFileName(path)
                    .StartsWith(ReportPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .Reverse()
                .ToArray();

            if (sources.Length == 0)
            {
                return null;
            }

            var target = Path.Combine(
                dir,
                ReportPrefix + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                + ".log");

            using (var writer = new StreamWriter(target, append: false, new UTF8Encoding(false)))
            {
                writer.WriteLine("# " + AboutViewModel.VersionText(_flavor));
                foreach (var source in sources)
                {
                    writer.WriteLine();
                    writer.WriteLine("### " + Path.GetFileName(source));
                    writer.WriteLine(File.ReadAllText(source));
                }
            }

            _model.AppendLog(UiStrings.AboutReportSaved + target);
            RefreshLogButton();
            return target;
        }
        catch (IOException ex)
        {
            _model.AppendLog(UiStrings.AboutReportSaveFailed + "：" + ex.Message);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _model.AppendLog(UiStrings.AboutReportSaveFailed + "：" + ex.Message);
            return null;
        }
    }

    /// <summary>まとめた檔の名の頭（自分自身を集め直さないための印）。</summary>
    private const string ReportPrefix = "report-";

    /// <summary>
    /// 困ったときの手引き（<c>docs\guide.md</c>）を開く。
    /// <para>
    /// <b>指すのは 1 檔である</b>（是正・2026-09-11・low 19）＝`v2-spec.md` §2-6 も
    /// <c>installer/irodori-tts-ywk.iss</c> の註も「〔使い方を見る〕が開くのは
    /// <c>docs\guide.md</c>」と書いているのに、ここは<b>置き場</b>を開いていた。
    /// 置き場には作る側の帳面（<c>README.md</c>）も並んでいるので、押した人がどれを読むのか
    /// 判らない。<b>檔を選んで開く</b>（<c>/select,</c>＝置き場を開いてその 1 檔を選んだ形＝
    /// <c>.md</c> の開き手が入っていない機体でも失敗しない）。
    /// 檔が無い機体（古い配布）は、これまでどおり置き場→記録の順に落ちる。
    /// </para>
    /// </summary>
    private void OpenGuide()
    {
        var docs = Path.Combine(AppServices.Paths.AppDir, "docs");
        var guide = Path.Combine(docs, GuideFileName);
        if (File.Exists(guide))
        {
            SelectInExplorer(guide);
            return;
        }

        if (Directory.Exists(docs))
        {
            StartShell("explorer.exe", "\"" + docs + "\"");
            return;
        }

        OpenLog();
    }

    /// <summary>利用者向けの 1 檔（<c>installer/irodori-tts-ywk.iss</c> の <c>[Files]</c> と同じ綴り）。</summary>
    private const string GuideFileName = "guide.md";

    private void OpenExternal(string url) => StartShell(url, null);

    /// <summary>
    /// スマート アプリ コントロールの頁を開く（<c>decisions.md</c> 140・v2.0.2）。
    /// <para>
    /// 開けなかったら Windows セキュリティそのものへ落ちる＝<b>押した釦が黙って何もしない形を作らない</b>。
    /// どちらも開けない機体は、<see cref="StartShell"/> が理由を記録へ落とす。
    /// </para>
    /// </summary>
    private void OpenSmartAppControlSettings()
    {
        if (TryStartShell(Services.Security.SmartAppControl.SettingsUri))
        {
            return;
        }

        StartShell(Services.Security.SmartAppControl.FallbackSettingsUri, null);
    }

    /// <summary>1 つ開いてみる（開けたら真・開けなければ<b>記録を汚さずに</b>偽）。</summary>
    private static bool TryStartShell(string target)
    {
        try
        {
            var info = new ProcessStartInfo(target) { UseShellExecute = true };
            using var started = Process.Start(info);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>置き場を 1 つ開く（無ければ何もしない＝押せる釦が必ず失敗する形を作らない）。</summary>
    private void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            StartShell("explorer.exe", "\"" + path + "\"");
        }
    }

    private void StartShell(string target, string? arguments)
    {
        try
        {
            var info = new ProcessStartInfo(target) { UseShellExecute = true };
            if (arguments is not null)
            {
                info.Arguments = arguments;
            }

            using var started = Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _model.AppendLog("開けませんでした：" + ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _model.AppendLog("開けませんでした：" + ex.Message);
        }
    }

    private static string? CurrentLogPath()
    {
        try
        {
            return LauncherLogFile.PathFor(AppServices.Paths.LogDir, DateTimeOffset.Now);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// <b>開けない釦は出さない</b>（v2-spec.md §2-1a）＝当日の記録が無い回は
    /// <c>IsEnabled=false</c> ではなく <c>Visibility</c> で消す。
    /// </summary>
    private void RefreshLogButton()
    {
        var path = CurrentLogPath();
        var exists = path is not null && File.Exists(path);
        OpenLogButton.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
    }

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
