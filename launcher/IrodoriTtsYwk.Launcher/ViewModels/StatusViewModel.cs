using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Mvvm;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Server;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// 状態帯（設計書 §2・裁定 67 ⑶）。<b>窓なしで作れる</b>＝WPF の型に触れない。
/// <para>
/// 出すのは 6 つ＝⑴ <b>状態</b>（Stopped／Starting／Listening／Ready／Warming／Failed）と理由 1 行
/// ⑵ <b>GPU</b>（名前と UUID の下 6 桁＝裁定 34）⑶ <b>変種</b>（台帳の綴りと、wrapper が名乗った
/// ラベルの突合）⑷ <b>ポート</b>（127.0.0.1 の 1 本＝裁定 2）⑸ <b>ログ末尾 20 行</b>（422 の本文
/// echo は畳む＝受け入れ条件 D-4）⑹ <b>GPU メモリ</b>（<c>/ywk/status.memory</c>＝無ければ
/// 「未対応」・便 C（2）が wrapper に足している最中）。
/// </para>
/// <para>
/// <b>起動・停止の中身はここに無い</b>＝渡された 2 つの手（<see cref="StartCommand"/>／
/// <see cref="StopCommand"/>）を押すだけ。env の組み立てと子プロセスは
/// <see cref="ServerEnvironment"/> と <see cref="IServerProcess"/> の仕事である。
/// </para>
/// </summary>
public sealed class StatusViewModel : ObservableObject
{
    private readonly LogTail _log = new(LogTail.DefaultCapacity);

    private ServerState _state = ServerState.Stopped;
    private string? _reason;
    private string _gpuText = UiText.Missing;
    private string _variantText = string.Empty;
    private string _endpointText = string.Empty;
    private string _deviceText = UiText.Missing;
    private string _warmupText = "—";
    private string _precomputeText = "—";
    private string _memoryText = UiText.NotRunning;
    private bool _memorySupported;
    private bool _serverAnswered;
    private bool _memoryPanelVisible = true;
    private string? _rebuildRuntime;
    private bool _acquisitionNeeded;
    private bool _acquisitionPending;
    private bool _isRebuilding;
    private string _rebuildProgress = string.Empty;
    private double _rebuildFraction;
    private string _voiceMemoryText = UiText.Missing;
    private string _latentCacheText = UiText.Missing;
    private string? _noticesText;
    private string _logText = string.Empty;
    private string? _upstreamMismatch;
    private bool _hasProcess;
    private string? _gpuMismatch;
    private string? _settingsPending;
    private LauncherSettings? _desired;
    private RunningSettings? _running;
    private MemoryStatus? _memory;

    /// <summary>OS の GPU 計数の行（裁定 110＝見張りが同じ回に採った物）。</summary>
    private IReadOnlyList<OsGpuMemoryRow> _osGpuRows = [];

    private IReadOnlyList<VoiceRow>? _rows;
    private VoiceRow? _selectedVoice;

    public StatusViewModel(Func<Task> start, Func<Task> stop)
        : this(start, stop, null)
    {
    }

    /// <param name="start">「サーバ起動」。</param>
    /// <param name="stop">「サーバ停止」。</param>
    /// <param name="rebuildRuntime">
    /// 「実行系を組み直す」（裁定 91）＝cache から再展開し、cache が無ければ取得から。
    /// null＝この配布ではその手を出さない（<see cref="CanRebuildRuntime"/> が偽）。
    /// </param>
    public StatusViewModel(Func<Task> start, Func<Task> stop, Func<Task>? rebuildRuntime)
        : this(start, stop, rebuildRuntime, null)
    {
    }

    /// <param name="start">「サーバ起動」。</param>
    /// <param name="stop">「サーバ停止」。</param>
    /// <param name="rebuildRuntime">「実行系を組み直す」。</param>
    /// <param name="cancelRebuild">
    /// 走っている組み直しをやめる手（是正・便 D（3）の 3 巡目）。null＝やめる口を出さない。
    /// <b>取消が要る理由</b>＝裁定 90 の自動削除で cache は空なのが常態なので、この 1 手は
    /// 押した瞬間に数 GiB の再取得になる（実射＝rocm 1.44 GiB・cu126 2.58 GiB）。
    /// </param>
    public StatusViewModel(
        Func<Task> start, Func<Task> stop, Func<Task>? rebuildRuntime, Action? cancelRebuild)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(stop);

        StartCommand = new AsyncRelayCommand(start, () => !IsRunning);
        StopCommand = new AsyncRelayCommand(stop, () => CanStop);
        RebuildRuntimeCommand = new AsyncRelayCommand(
            rebuildRuntime ?? (static () => Task.CompletedTask),
            () => rebuildRuntime is not null && RebuildRuntimeText is not null && !IsRunning);
        CancelRebuildCommand = new RelayCommand(
            cancelRebuild ?? (static () => { }),
            () => cancelRebuild is not null && IsRebuilding);
    }

    /// <summary>
    /// 走っている個体が名乗る設定の写し（<b>不変</b>）。走行中の状態帯はここから描く。
    /// </summary>
    /// <param name="Variant">起こしたときの変種（台帳の綴り）。</param>
    /// <param name="Port">起こしたときのポート。</param>
    /// <param name="GpuName">起こしたときの GPU の名前。</param>
    /// <param name="GpuUuid">起こしたときの GPU の UUID。</param>
    /// <param name="PrecomputeOnStart">
    /// 起こしたときの参照潜在キャッシュの<b>実効値</b>（裁定 65・67 ⑴）。走行中の状態帯は
    /// いま走っている個体の ON／OFF を名乗る（設定を変えても次の起動まで変わらない）。
    /// </param>
    private sealed record RunningSettings(
        string Variant, int Port, string? GpuName, string? GpuUuid, bool PrecomputeOnStart);

    /// <summary>「サーバ起動」。走っている間は押せない。</summary>
    public AsyncRelayCommand StartCommand { get; }

    /// <summary>「サーバ停止」（ツリー kill）。</summary>
    public AsyncRelayCommand StopCommand { get; }

    /// <summary>
    /// 「実行系を組み直す」（裁定 91）＝配布樹の取得台帳と、展開に使った台帳が食い違ったときの 1 手。
    /// </summary>
    public AsyncRelayCommand RebuildRuntimeCommand { get; }

    /// <summary>
    /// 実行系が配布樹と食い違っている 1 行（合っていれば null＝手も出さない）。
    /// <para>
    /// 裁定 91 の票＝<see cref="Contracts.AppPaths.ResolvePythonExe"/> は
    /// <c>python.exe</c> の在否しか見ないので、配布物を新しい版に入れ替えても
    /// <b>古い実行系がそのまま使われる</b>（台帳が変わったのに site-packages は前の版のまま）。
    /// <c>settings.json</c> に焼いた <c>runtimeLedgerSha256</c>／<c>installedAppVersion</c> と
    /// 突き合わせ、食い違ったらここに 1 行出して 1 手を押せるようにする。
    /// </para>
    /// </summary>
    public string? RebuildRuntimeText
    {
        get => _rebuildRuntime;
        private set
        {
            if (SetProperty(ref _rebuildRuntime, value))
            {
                RaisePropertyChanged(nameof(CanRebuildRuntime));
                RebuildRuntimeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>「実行系を組み直す」を出すか。</summary>
    public bool CanRebuildRuntime => RebuildRuntimeText is not null;

    // ---- 裁定 121＝取得への導線 ---------------------------------------------

    /// <summary>取得が未了のときに出す 1 行（<b>逐語</b>）。</summary>
    public const string AcquisitionLine = UiStrings.StatusAcquisitionLine;

    /// <summary>
    /// 取得への 1 手（「取得へ進む」）を出すか。
    /// <para>
    /// <b>出す条件は 3 つ</b>＝⑴ 起こす前の事前検査が「実行系／モデルがまだありません」で
    /// 断った（理由に <see cref="Services.Models.AcquisitionCheck.Hint"/> が入っている）
    /// ⑵ 起こした個体が <c>HF_HUB_OFFLINE=1</c> でモデルを読めずに落ちた
    /// （<see cref="ServerStateMachine.RuntimeLoadFailedPrefix"/> の理由に、欠けた snapshot の
    /// ときだけ上流が吐く字が入っている＝<see cref="MissingModelMarkers"/>）
    /// ⑶ <b>檔を見た結果が「揃っていない」</b>（<see cref="ApplyAcquisition"/>＝
    /// <c>MainViewModel.NeedsAcquisition</c>）＝ウィザードを出さずに閉じた利用者が、
    /// 何もせずに帯だけを見ている回である。
    /// それ以外の失敗（ポート・ドライバ・GPU の取り違え）では<b>出さない</b>。
    /// </para>
    /// </summary>
    public bool AcquisitionNeeded
    {
        get => _acquisitionNeeded;
        private set
        {
            if (SetProperty(ref _acquisitionNeeded, value))
            {
                RaisePropertyChanged(nameof(AcquisitionText));
            }
        }
    }

    /// <summary>その 1 行（出さないときは空）。</summary>
    public string AcquisitionText => AcquisitionNeeded ? AcquisitionLine : string.Empty;

    /// <summary>
    /// 檔を見た結果を入れる（真＝実行系かモデルが揃っていない）。窓がウィザードを閉じた後にも
    /// 呼ばれる＝<b>失敗を待たずに</b>帯へ 1 手を出す。
    /// <para>
    /// <b>新しい検分は古い断り書きに勝つ</b>（是正・検分）＝⑴ の事前検査で断った理由は
    /// <see cref="ServerState.Failed"/> のまま残るので、取得を済ませてウィザードを閉じても
    /// 帯が「取得が未了です。」と言い続けていた。檔を見て「揃っている」と判った回は、
    /// 事前検査の名残（<see cref="Services.Models.AcquisitionCheck.Hint"/>）を無視する。
    /// 個体が落ちた回（⑵ の <see cref="ServerStateMachine.RuntimeLoadFailedPrefix"/>）は
    /// 檔の在否で否定できない事実なので、そのまま残す。
    /// </para>
    /// </summary>
    public void ApplyAcquisition(bool needed)
    {
        _acquisitionPending = needed;
        AcquisitionNeeded = needed
            || (IsAcquisitionFailure(State, Reason) && !ReasonIsPreflight(Reason));
    }

    /// <summary>
    /// <b>はじめの準備が最後まで済んでいない</b>（<c>MainViewModel.NeedsFirstRun</c> ないし
    /// <c>NeedsAcquisition</c>）＝帯に E-01 を出す（是正・段 G・high 7）。
    /// <b>失敗を待たない</b>のが眼目である＝ウィザードを閉じた機体は誰もサーバを起こさないので、
    /// 断りの 1 行すら出ないまま <see cref="ServerState.Stopped"/> のまま止まる。
    /// </summary>
    public void ApplyFirstRunPending(bool pending)
    {
        if (_firstRunPending == pending)
        {
            return;
        }

        _firstRunPending = pending;
        RefreshBand();
    }

    /// <summary>事前検査がランチャ自身の字で断った理由か（<b>純関数</b>）。</summary>
    private static bool ReasonIsPreflight(string? reason) =>
        reason is not null
        && reason.Contains(Services.Models.AcquisitionCheck.Hint, StringComparison.Ordinal);

    /// <summary>
    /// モデルの読込の失敗が<b>「檔が無い／offline」だと読める</b>ときの字（控えめに拾う）。
    /// <c>huggingface_hub</c> が pin した snapshot を見つけられなかったときに上流の
    /// <c>from_pretrained</c> が吐く物である。
    /// </summary>
    public static readonly string[] MissingModelMarkers =
    [
        "OSError",
        "does not appear to have a file",
        "offline",
        "not found",
        "No such file",
        "LocalEntryNotFound",
    ];

    /// <summary>取得が未了だと読める失敗か（<b>純関数</b>）。</summary>
    public static bool IsAcquisitionFailure(ServerState state, string? reason)
    {
        if (state is not ServerState.Failed || string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        var text = reason.Trim();

        // ⑴ 事前検査で断った回＝ランチャ自身が書いた 1 文が入っている。
        if (text.Contains(Services.Models.AcquisitionCheck.Hint, StringComparison.Ordinal))
        {
            return true;
        }

        // ⑵ 起こした個体がモデルを載せられなかった回。前置きが違えば相手にしない。
        if (!text.StartsWith(ServerStateMachine.RuntimeLoadFailedPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var marker in MissingModelMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>食い違いの 1 行を入れ替える（null＝消す）。</summary>
    public void ApplyRuntimeStamp(string? mismatchLine)
    {
        RebuildRuntimeText = mismatchLine;

        // E1 は**帯にも載る**（憲章 §4-24＝v2.0 の必須要件・是正・段 G・high 1）。
        // 詳しい状態 の畳みの外の 1 行と 1 手は据え置きで、同じ事実を帯にも出すだけである。
        RefreshBand();
    }

    // ---- 連携の 1 行の材料（§2-1c）--------------------------------------------

    /// <summary>
    /// 本体の走行の標本を入れる（<b>配るのは <see cref="MainViewModel.ApplyStatusSample"/> 1 本</b>＝
    /// 見張りが採った同じ 2 秒の標本をそのまま渡す。新しい問い合わせは 1 本も足さない）。
    /// <para>
    /// <b>段 D が契約に足した欄</b>（`docs/contract.md` ⑹ の <c>requests.in_flight</c>）を、
    /// 段 G でようやく帯へ結んだ（是正・medium 2／9）＝それまで帯は
    /// <c>hostFieldPresent:false</c> で凍っており、<see cref="BandText.HostIdle"/> と
    /// <see cref="BandText.HostBusy"/> の 2 文は到達できなかった。
    /// </para>
    /// </summary>
    /// <param name="fieldPresent">
    /// <c>/ywk/status</c> に走行数の欄が在るか（<c>StatusResponse.Requests is not null</c>）。
    /// 偽＝古い個体＝「読み分けちゃん2 から使えます」だけを出す（§2-1c＝嘘にならない）。
    /// </param>
    /// <param name="busy">いま本体の読み上げが走っているか（<b>自分の射は濾してある値</b>）。</param>
    public void ApplyHost(bool fieldPresent, bool busy)
    {
        _hostFieldPresent = fieldPresent;
        _hostBusy = busy;

        // 「まだ呼ばれていません」は**この起動で 1 度でも見たら二度と出さない**（§2-1c）。
        _hostSeen |= busy;
        RefreshBand();
    }

    /// <summary>いま本体の読み上げが走っているか（<see cref="TryViewModel.HostBusy"/> の出所）。</summary>
    public bool HostBusy => _hostBusy;

    // ---- 組み直しの取消と進捗（是正・便 D（3）の 3 巡目） ----------------------

    /// <summary>走っている組み直しをやめる。</summary>
    public RelayCommand CancelRebuildCommand { get; }

    /// <summary>組み直しが走っているか（帯と「やめる」を出す）。</summary>
    public bool IsRebuilding
    {
        get => _isRebuilding;
        private set
        {
            if (SetProperty(ref _isRebuilding, value))
            {
                CancelRebuildCommand.RaiseCanExecuteChanged();
                RebuildRuntimeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 組み直しの進捗 1 行。<b>ログ帯（20 行）に流すだけでは読めない</b>ので、
    /// ウィザードと同じ帯を状態タブにも出す。
    /// </summary>
    public string RebuildProgressText
    {
        get => _rebuildProgress;
        private set => SetProperty(ref _rebuildProgress, value);
    }

    /// <summary>0〜1（分母が無ければ 0）。</summary>
    public double RebuildProgressFraction
    {
        get => _rebuildFraction;
        private set => SetProperty(ref _rebuildFraction, value);
    }

    /// <summary>組み直しに入った。</summary>
    public void BeginRebuild()
    {
        RebuildProgressText = UiStrings.StatusRebuilding;
        RebuildProgressFraction = 0;
        IsRebuilding = true;
    }

    /// <summary>組み直しが終わった（通っても落ちても取消でも）。</summary>
    public void EndRebuild()
    {
        IsRebuilding = false;
        RebuildProgressFraction = 0;
        RebuildProgressText = string.Empty;
    }

    /// <summary>組み直しの 1 行と進み具合。</summary>
    public void ApplyRebuildProgress(string line, double fraction)
    {
        RebuildProgressText = line;
        RebuildProgressFraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
    }

    /// <summary>
    /// GPU メモリ欄を出すか（設定 <c>showMemoryPanel</c>・裁定 67 ⑶）。
    /// <para>
    /// <b>ここが正本</b>（是正・便 D（3）・low 6 の ⑸）＝1 巡目・2 巡目は窓が
    /// <c>StatusView.SetMemoryPanelVisible</c> を<b>構築時とウィザードを閉じたときだけ</b>叩いて
    /// いたので、設定画面で外して「適用」を押しても<b>次に起動し直すまで欄が消えなかった</b>
    /// （設定は保存されているのに画面が変わらない＝押し忘れたと読める）。
    /// </para>
    /// </summary>
    public bool MemoryPanelVisible
    {
        get => _memoryPanelVisible;
        private set => SetProperty(ref _memoryPanelVisible, value);
    }

    public ServerState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(StateText));
            RaisePropertyChanged(nameof(IsRunning));
            RaisePropertyChanged(nameof(IsFailed));
            RaisePropertyChanged(nameof(CanStop));
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// 起こした子プロセスがまだ台帳に居るか（<see cref="IServerProcess.ProcessId"/> が非 null）。
    /// <b>Failed でも真になりうる</b>のが眼目で、これが「止める口」の保険である（是正・2026-09-05）。
    /// </summary>
    public bool HasProcess
    {
        get => _hasProcess;
        set
        {
            if (!SetProperty(ref _hasProcess, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(CanStop));
            StopCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// 「サーバ停止」を押せるか＝走っている、または<b>個体が残っている</b>。
    /// <para>
    /// <see cref="IsRunning"/> だけで判定していたころは、ready 待ちが期限切れになって
    /// <c>Failed</c> に落ちた個体（子プロセスは生きたままモデルを載せ続ける）に対して
    /// 停止も起動も押せなくなった＝逃げ道が窓を閉じる（＝アプリを終える・裁定 124）だけになる。
    /// </para>
    /// </summary>
    public bool CanStop => IsRunning || HasProcess;

    /// <summary>状態の日本語（裁定 52＝UI は日本語のみ）。</summary>
    public string StateText => StateLabel(State);

    /// <summary>起こしてある（起動中・読込中・待機・暖機中）。</summary>
    public bool IsRunning => State is ServerState.Starting or ServerState.Listening
        or ServerState.Ready or ServerState.Warming;

    public bool IsFailed => State is ServerState.Failed;

    /// <summary>Failed のときの理由 1 行（受け入れ条件 D-1＝≤ 15 s で必ず出る）。</summary>
    public string? Reason
    {
        get => _reason;
        private set => SetProperty(ref _reason, value);
    }

    /// <summary>選んだ GPU（名前＋UUID の下 6 桁）。</summary>
    public string GpuText
    {
        get => _gpuText;
        private set => SetProperty(ref _gpuText, value);
    }

    /// <summary>変種（台帳の綴りの表示名）。</summary>
    public string VariantText
    {
        get => _variantText;
        private set => SetProperty(ref _variantText, value);
    }

    /// <summary><c>http://127.0.0.1:18088</c>。</summary>
    public string EndpointText
    {
        get => _endpointText;
        private set => SetProperty(ref _endpointText, value);
    }

    /// <summary>実際に載った device（<c>/ywk/status.device.actual</c>＝設定値の echo ではない）。</summary>
    public string DeviceText
    {
        get => _deviceText;
        private set => SetProperty(ref _deviceText, value);
    }

    /// <summary>暖機（契約 ⑺ 7-2＝<c>state</c> と <c>shots_done/shots_total</c>）。</summary>
    public string WarmupText
    {
        get => _warmupText;
        private set => SetProperty(ref _warmupText, value);
    }

    /// <summary>参照潜在の事前計算（裁定 65）。</summary>
    public string PrecomputeText
    {
        get => _precomputeText;
        private set => SetProperty(ref _precomputeText, value);
    }

    /// <summary>GPU メモリ（裁定 67 ⑶）。口が無ければ「未対応」。</summary>
    public string MemoryText
    {
        get => _memoryText;
        private set => SetProperty(ref _memoryText, value);
    }

    /// <summary><c>/ywk/status.memory</c> が返ってきたか。</summary>
    public bool MemorySupported
    {
        get => _memorySupported;
        private set => SetProperty(ref _memorySupported, value);
    }

    /// <summary>
    /// 参照ボイスの消費メモリ（裁定 67 ⑵・low 13）＝<b>1 名あたりが主役</b>で、
    /// 全員分は「全部を同時に載せたときの上限」として括弧に落とす。
    /// </summary>
    public string VoiceMemoryText
    {
        get => _voiceMemoryText;
        private set => SetProperty(ref _voiceMemoryText, value);
    }

    /// <summary>
    /// 参照潜在キャッシュ（裁定 67 ⑴）＝<c>ON／OFF（焼いた話者 n 名・合計 m MB）</c>。
    /// </summary>
    public string LatentCacheText
    {
        get => _latentCacheText;
        private set => SetProperty(ref _latentCacheText, value);
    }

    /// <summary>
    /// 起動の告知（<c>ServerStartResult.Notices</c>＝裁定 88 ⑵の「未実測の帯」など）。
    /// <b>そのまま出す</b>＝畳んだり言い換えたりしない。無ければ null。
    /// </summary>
    public string? NoticesText
    {
        get => _noticesText;
        private set => SetProperty(ref _noticesText, value);
    }

    /// <summary>ログ末尾 20 行。</summary>
    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    /// <summary>
    /// 配布物に焼いた上流 pin と、走っている wrapper が名乗った pin の食い違い
    /// （＝樹が腐っている印。null なら合っている・分からない）。
    /// </summary>
    public string? UpstreamMismatch
    {
        get => _upstreamMismatch;
        private set => SetProperty(ref _upstreamMismatch, value);
    }

    /// <summary>状態の日本語（<b>1 箇所で綴る</b>＝状態帯と窓題の元）。</summary>
    public static string StateLabel(ServerState state) => state switch
    {
        ServerState.Stopped => BandText.StoppedByUser,
        ServerState.Starting => BandText.Preparing,
        ServerState.Listening => BandText.Preparing,
        ServerState.Ready => BandText.Ready,
        ServerState.Warming => BandText.Ready,
        ServerState.Failed => BandText.Failed,
        _ => state.ToString(),
    };

    // ===================== 状態の帯（v2.0・`v2-spec.md` §2-1）=====================
    //
    // **名乗るのは 3 語だけ**（準備しています…／使えます／止まりました）。
    //
    // **上の StateLabel も同じ語を返す**（段 C・是正）＝1 巡目のこの註は「StateLabel の 6 語は
    // 1 字も触らない」と書いていたが、段 C が StateLabel を書き替えて ServerState の 6 値を
    // 帯の語へ畳んだ（Stopped＝止まっています／Starting・Listening＝準備しています…／
    // Ready・Warming＝使えます／Failed＝止まりました。）。**enum ServerState の 6 値と状態機械と
    // 契約は 1 つも触っていない**＝替えたのは綴りだけである。結果、MainStateText（下の StatusBar）と
    // MainBandStateText は同じ 4 綴りを運ぶ＝台本の錨も同じ語で書く
    // （probe/d-launch-probe.ps1 の $T.Ready／Starting／Stopped／Failed の註と揃えてある）。
    //
    // 文を組むのは純関数 BandText.For で、ここはその材料を集めて結果を配るだけ。

    private BandLine _band = BandText.For(ServerState.Stopped, null);
    private string _bandHostText = string.Empty;
    private bool _runtimeLoaded;
    private bool _userStopped;
    private bool _hostFieldPresent;
    private bool _hostSeen;
    private bool _hostBusy;
    private bool _firstRunPending;
    private string? _driverVersion;
    private string? _alternativeVariant;
    private int? _missingModelCount;
    private int? _exitCode;

    /// <summary>帯の丸の色（灰／緑／赤）。</summary>
    public BandSeverity BandSeverity => _band.Severity;

    /// <summary>帯が名乗る 1 語（3 語だけ）。</summary>
    public string BandStateText => _band.Headline;

    /// <summary>帯の理由 1 行（⑴＋⑵・ふだんは空）。</summary>
    public string BandReasonText => _band.Reason ?? string.Empty;

    /// <summary>理由の行を出すか。</summary>
    public bool HasBandReason => _band.Reason is not null;

    /// <summary>帯の 1 手の札（⑶）。</summary>
    public string BandActionText => _band.ActionLabel ?? string.Empty;

    /// <summary>出せる 1 手が在るか（<b>無いときは釦ごと出さない</b>）。</summary>
    public bool HasBandAction => _band.Action is not BandActionKind.None;

    /// <summary>帯の 1 手の種類（押したときに何をするかは窓が決める）。</summary>
    public BandActionKind BandAction => _band.Action;

    /// <summary>連携の 1 行（§2-1c）。</summary>
    public string BandHostText
    {
        get => _bandHostText;
        private set => SetProperty(ref _bandHostText, value);
    }

    /// <summary>
    /// 帯が文を組むのに使う事実を渡す（<b>内部の 1 行から掻き回して取らない</b>＝
    /// 判っている側＝<see cref="MainViewModel"/> が起動のたびに渡す）。
    /// </summary>
    /// <param name="driverVersion">読めたドライバの版（読めなければ null）。</param>
    /// <param name="alternativeVariant">切り替えれば動く動かし方（無ければ null）。</param>
    public void ApplyDriverFacts(string? driverVersion, string? alternativeVariant)
    {
        _driverVersion = driverVersion;
        _alternativeVariant = alternativeVariant;
        RefreshBand();
    }

    /// <summary>足りない声のデータの数（判らない回は null）。</summary>
    public void ApplyMissingModels(int? count)
    {
        _missingModelCount = count;
        RefreshBand();
    }

    /// <summary>
    /// 落ちた子プロセスの終了コード（`v2-spec.md` §2-1a の D6＝⑵ に差す数）。
    /// <b>内部の 1 行から掻き回して取らない</b>（§2-1b）＝<see cref="IServerProcess.ExitCode"/> を
    /// 読んでいる側（<see cref="MainViewModel.ApplyServerState"/>）が渡す。落ちていない回は null。
    /// </summary>
    public void ApplyExitCode(int? exitCode)
    {
        _exitCode = exitCode;
        RefreshBand();
    }

    /// <summary>いま集まっている材料で帯を組み直す。</summary>
    private void RefreshBand()
    {
        var context = new BandContext(
            RuntimeLoaded: _runtimeLoaded,
            UserStopped: _userStopped,
            AutoStartDisabled: _desired is not null && !_desired.AutoStartServer,
            DriverVersion: _driverVersion,
            Variant: _running?.Variant ?? _desired?.Variant,
            AlternativeVariant: _alternativeVariant,
            GpuName: _running?.GpuName ?? _desired?.GpuName,
            MissingModelCount: _missingModelCount,
            ExitCode: _exitCode,
            RebuildRuntimeLine: _rebuildRuntime,
            FirstRunPending: _firstRunPending);

        var next = BandText.For(State, Reason, context);
        if (!Equals(_band, next))
        {
            _band = next;
            RaisePropertyChanged(nameof(BandSeverity));
            RaisePropertyChanged(nameof(BandStateText));
            RaisePropertyChanged(nameof(BandReasonText));
            RaisePropertyChanged(nameof(HasBandReason));
            RaisePropertyChanged(nameof(BandActionText));
            RaisePropertyChanged(nameof(HasBandAction));
            RaisePropertyChanged(nameof(BandAction));
        }

        // 走行数の欄は段 D が契約に足した（`server/ywk_server.py` の `requests.in_flight`・
        // `docs/contract.md` ⑹）。標本は `ApplyHost` が配る（§2-1c）。
        BandHostText = BandText.HostLine(State, _hostFieldPresent, _hostSeen, _hostBusy);
    }

    /// <summary>
    /// 設定から出せる欄（サーバが止まっていても出る）を入れ直す。
    /// <para>
    /// <b>走行中は書き換えない</b>（是正・2026-09-05）＝状態帯は<b>いま走っている個体</b>の
    /// 変種・GPU・接続先を名乗る。走行中に「適用」した設定は
    /// <see cref="SettingsPendingText"/> に「次回の起動から有効」として出す
    /// （再起動は実装していないので、画面が嘘をつくよりこちらを採る）。
    /// </para>
    /// </summary>
    public void ApplySettings(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _desired = settings;
        Repaint();
    }

    /// <summary>
    /// 起こした＝この設定で走る、を写し取る（状態帯はここから描く）。
    /// </summary>
    public void BeginRun(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _desired ??= settings;
        _running = new RunningSettings(
            settings.Variant,
            settings.Port,
            settings.GpuName,
            settings.GpuUuid,
            settings.EffectivePrecomputeOnStart());
        Repaint();
    }

    /// <summary>止まった＝写しを捨てて、いまの設定を描き直す。</summary>
    public void EndRun()
    {
        _running = null;
        Repaint();
    }

    /// <summary>
    /// 走行中の個体と設定が食い違っているときの 1 行（合っていれば null）。
    /// </summary>
    public string? SettingsPendingText
    {
        get => _settingsPending;
        private set => SetProperty(ref _settingsPending, value);
    }

    private void Repaint()
    {
        var settings = _desired;
        if (settings is null)
        {
            return;
        }

        var variant = _running?.Variant ?? settings.Variant;
        var port = _running?.Port ?? settings.Port;
        var gpuName = _running is null ? settings.GpuName : _running.GpuName;
        var gpuUuid = _running is null ? settings.GpuUuid : _running.GpuUuid;

        // 「GPU メモリの欄」は設定を「適用」した瞬間から効く（low 6 の ⑸）。
        MemoryPanelVisible = settings.ShowMemoryPanel;

        VariantText = RuntimeVariants.DisplayName(variant);
        EndpointText = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
        GpuText = string.IsNullOrWhiteSpace(gpuName) && string.IsNullOrWhiteSpace(gpuUuid)
            ? (RuntimeVariants.IsCpu(variant) ? UiStrings.StatusDeviceCpu : UiStrings.StatusGpuUnselected)
            : gpuName ?? UiStrings.StatusDeviceGpu;

        SettingsPendingText = DescribePending(_running, settings);
        RepaintMemory();
        RefreshBand();
    }

    /// <summary>
    /// GPU メモリ・参照潜在キャッシュ・参照ボイスの 3 欄を引き直す（材料が変わるたびに呼ぶ）。
    /// </summary>
    private void RepaintMemory()
    {
        MemorySupported = _memory is not null;
        MemoryText = DescribeMemory(_memory, _serverAnswered, _osGpuRows);

        var on = _running?.PrecomputeOnStart
            ?? _desired?.EffectivePrecomputeOnStart()
            ?? false;
        LatentCacheText = DescribeLatentCache(on, _memory, _rows);
        VoiceMemoryText = DescribeVoiceMemory(_selectedVoice, _rows);
    }

    /// <summary>走行中の個体と設定の食い違いの 1 行（<b>純関数</b>）。</summary>
    private static string? DescribePending(RunningSettings? running, LauncherSettings settings)
    {
        if (running is null)
        {
            return null;
        }

        var differs = !string.Equals(running.Variant, settings.Variant, StringComparison.Ordinal)
            || running.Port != settings.Port
            || !string.Equals(running.GpuUuid ?? string.Empty, settings.GpuUuid ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

        // 走っている個体の綴り（動かし方・つなぎ口・グラフィックス）は**画面に出さない**＝
        // 詳しい状態の詳細に同じ 3 つが並んでいる（憲章 原則 1・§6-1）。
        return differs ? UiStrings.SettingsPending : null;
    }

    /// <summary>
    /// 状態機械が動いた（<b>状態の書き手はここ 1 本</b>＝low 3）。
    /// <para>
    /// 窓も見張りもこの口を直に叩かない。<c>Warming</c> への出入りを含めて、状態を決めるのは
    /// <see cref="IServerProcess"/> の状態機械だけである（<c>StateChanged</c> を窓が
    /// marshal してここへ運ぶ）。<c>/ywk/status</c> の標本は
    /// <see cref="ApplyStatus"/> で<b>読むだけ</b>で、状態は動かさない。
    /// </para>
    /// </summary>
    public void ApplyState(ServerState state, string? reason)
    {
        // 帯は「まだ起こしていない」と「利用者が止めた」を別の語で名乗る（`v2-spec.md` §2-1）。
        // 一度でも起こした後に Stopped へ戻った回だけ「止まっています」＋〔もう一度動かす〕を出す。
        // **Failed も「一度は起こした後」に数える**（是正・検分）＝子が生きたまま Failed に落ちる態
        // （D2 ほか＝CanStop は真）で〔サーバ停止〕を押すと、IsRunning だけで見ていたころは
        // Stopped＋「準備しています…」＋釦なしの行き止まりになった。
        var wasRunning = IsRunning || IsFailed;

        State = state;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Reason = reason.Trim();
        }
        else if (state is not ServerState.Failed)
        {
            Reason = null;
        }

        // 取得が未了で落ちた回だけ、取得への 1 手を出す（裁定 121）。
        AcquisitionNeeded = _acquisitionPending || IsAcquisitionFailure(state, Reason);

        if (state is ServerState.Stopped)
        {
            DeviceText = UiText.Missing;
            WarmupText = "—";
            PrecomputeText = "—";
            NoticesText = null;
            _memory = null;
            _osGpuRows = [];
            _serverAnswered = false;
            _runtimeLoaded = false;
            _userStopped = wasRunning;
            RepaintMemory();
        }
        else
        {
            _userStopped = false;
        }

        RebuildRuntimeCommand.RaiseCanExecuteChanged();
        RefreshBand();
    }

    /// <summary>
    /// 起動の告知（「未実測の帯」＝裁定 88 ⑵）をそのまま状態帯に載せる。
    /// </summary>
    public void ApplyNotices(IReadOnlyList<string>? notices) =>
        NoticesText = ComposeNotices(notices);

    /// <summary>
    /// 起動の結末を載せる（裁定 88 ⑴⑵）＝<b>門の告知をそのまま出す</b>。
    /// <para>
    /// <b>内部の理由 1 行はここから画面へ出さない</b>（是正・段 C の検分＝憲章 原則 6
    /// 「生のログ・スタックトレース…は画面に出さず」）。1 巡目は「断られた事実が状態に載らない
    /// 実装でも画面から消えない」ための保険として <see cref="Reason"/> と食い違う
    /// <paramref name="failureReason"/> を告知の先頭へ足していたが、その道で
    /// <c>VariantGate</c> の工学の 1 行（検分・実行系・裁定の番号）が
    /// <c>StatusNoticesText</c> へそのまま流れていた。断られた事実は帯
    /// （<see cref="BandReasonText"/>＝<c>BandText.For</c> の 3 部品）が必ず出すので、
    /// ここは<b>利用者の言葉で書かれた告知だけ</b>を運ぶ。元の 1 行は記録の檔に残る
    /// （<c>MainViewModel</c> が <c>ServerStartResult.NoticeTrail</c> と理由を <c>AppendLog</c> する）。
    /// </para>
    /// </summary>
    public void ApplyStartOutcome(bool ok, string? failureReason, IReadOnlyList<string>? notices) =>
        NoticesText = ComposeStartOutcome(ok, failureReason, notices, Reason);

    /// <summary>
    /// 起動の結末の畳み方（<b>純関数</b>）。
    /// <paramref name="failureReason"/> と <paramref name="shownReason"/> は
    /// <b>画面には出さない</b>（上の註）＝欄は呼ぶ側の形を変えないために残してある。
    /// </summary>
    public static string? ComposeStartOutcome(
        bool ok, string? failureReason, IReadOnlyList<string>? notices, string? shownReason)
    {
        _ = ok;
        _ = failureReason;
        _ = shownReason;

        return ComposeNotices(notices);
    }

    /// <summary>告知の畳み方（<b>純関数</b>＝行はそのまま・空は null）。</summary>
    public static string? ComposeNotices(IReadOnlyList<string>? notices)
    {
        if (notices is null || notices.Count == 0)
        {
            return null;
        }

        var lines = new List<string>(notices.Count);
        foreach (var notice in notices)
        {
            if (!string.IsNullOrWhiteSpace(notice))
            {
                lines.Add(notice.Trim());
            }
        }

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// <b>檔にも残す口</b>（裁定 126 の C（1）＝<see cref="Services.Logging.LauncherLogFile.Append"/>）。
    /// null＝檔には残さない（試験の既定）。<b>ここが投げても画面は止めない</b>
    /// （<see cref="AppendLog"/> が包む）。
    /// </summary>
    public Action<string>? LogSink { get; set; }

    /// <summary>
    /// stderr の 1 行（畳んでから入れる）。
    /// <para>
    /// 末尾 20 行の環（<see cref="LogTail"/>）に足すのと同じ 1 行を
    /// <see cref="LogSink"/> にも渡す＝<b>画面に出た物が檔にも残る</b>（裁定 126 の C（1））。
    /// 窓が構えるより前の行（<c>LauncherComposition.DrainNotes</c>）も、窓が引き取った時点で
    /// ここを通るので同じ檔に載る。
    /// </para>
    /// </summary>
    public void AppendLog(string? line)
    {
        _log.Append(line);
        LogText = _log.Text;

        if (LogSink is not { } sink || string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        try
        {
            sink(line);
        }
#pragma warning disable CA1031 // ログが書けないことで画面を止めない（檔の書き手も自分で握り潰す）
        catch (Exception)
#pragma warning restore CA1031
        {
            // 檔に残せなかった＝画面には出ている（LogTail は上で足し終えている）
        }
    }

    public void ClearLog()
    {
        _log.Clear();
        LogText = string.Empty;
    }

    /// <summary>
    /// <c>/ywk/status</c> を反映する。<b>欄が無い応答にも耐える</b>＝
    /// <c>memory</c> が無ければ「未対応」（便 C（2）が入れば勝手に効き始める）。
    /// </summary>
    /// <param name="status">見張りが採った標本（null＝口が閉じた）。</param>
    /// <param name="osGpuMemory">
    /// <b>同じ回に採った OS の GPU 計数</b>（裁定 110＝<c>IServerProcess.LatestOsGpuMemory</c>）。
    /// null／空＝計数が読めない機体・止まっている＝OS の組は帯に出ない。
    /// </param>
    public void ApplyStatus(
        StatusResponse? status, IReadOnlyList<OsGpuMemoryRow>? osGpuMemory = null)
    {
        if (status is null)
        {
            _memory = null;
            _osGpuRows = [];
            _serverAnswered = false;
            _runtimeLoaded = false;
            RepaintMemory();
            GpuMismatch = null;
            RefreshBand();
            return;
        }

        _serverAnswered = true;
        _runtimeLoaded = status.IsReady;
        DeviceText = Compose(status.Device);
        WarmupText = Compose(status.Warmup);
        PrecomputeText = Compose(status.Precompute);
        _memory = status.Memory;
        _osGpuRows = osGpuMemory ?? [];
        RepaintMemory();
        UpstreamMismatch = DescribeUpstreamMismatch(status.Upstream);
        GpuMismatch = DescribeGpuMismatch(_running?.GpuUuid ?? _desired?.GpuUuid, status.Device);
        RefreshBand();
    }

    /// <summary>
    /// 選んだ GPU（UUID）と、走っている個体が実際に載った GPU の食い違い（合っていれば null）。
    /// <para>
    /// 裁定 34 は「同定は UUID」だが、子に渡すのは <c>cuda:N</c> の N である。NVIDIA 機では
    /// <c>nvidia-smi</c> の index（PCI バス順）と torch の index（<c>CUDA_DEVICE_ORDER</c> の既定＝
    /// <c>FASTEST_FIRST</c>）が別の空間なので、番号が同じでも別の個体を指しうる。
    /// <see cref="ServerEnvironment"/> は <c>CUDA_DEVICE_ORDER=PCI_BUS_ID</c> を載せて 2 つの空間を
    /// 揃えるが、<b>揃ったことは実測で確かめる</b>＝この 1 行がその突合である。
    /// </para>
    /// </summary>
    public string? GpuMismatch
    {
        get => _gpuMismatch;
        private set => SetProperty(ref _gpuMismatch, value);
    }

    /// <summary>UUID の突合（<b>純関数</b>）。どちらかが読めなければ黙る。</summary>
    public static string? DescribeGpuMismatch(string? wantedUuid, StatusDevice? device)
    {
        var actual = device?.Uuid;
        if (string.IsNullOrWhiteSpace(wantedUuid) || string.IsNullOrWhiteSpace(actual))
        {
            return null;
        }

        return GpuResolver.SameUuid(wantedUuid, actual)
            ? null
            : UiStrings.StatusGpuMismatch;
    }

    /// <summary>話者一覧が変わった（裁定 67 ⑵）。</summary>
    public void ApplyVoices(IReadOnlyList<VoiceRow>? rows)
    {
        _rows = rows;

        // 選んでいた話者は作り直された同じ id の行に付け替える（実サイズが載るのは新しい行）。
        if (_selectedVoice is { } previous && rows is not null)
        {
            foreach (var row in rows)
            {
                if (string.Equals(row.Id, previous.Id, StringComparison.Ordinal))
                {
                    _selectedVoice = row;
                    break;
                }
            }
        }

        RepaintMemory();
    }

    /// <summary>話者画面で選んでいる 1 名（<b>概算の主役</b>＝裁定 67 ⑵・low 13）。</summary>
    public void ApplySelectedVoice(VoiceRow? row)
    {
        _selectedVoice = row;
        RepaintMemory();
    }

    /// <summary>
    /// GPU メモリの 1 行（<b>純関数</b>・裁定 67 ⑶・87 ⑴・<b>110</b>）＝
    /// <b>torch 使用量＝allocated（最大 …）・占有量＝reserved</b> と、
    /// <b>このプロセス／GPU 全体＝Windows の GPU 計数</b>（<paramref name="osRows"/>）。
    /// null は「—」・<c>cpu</c> は「CPU（GPU メモリなし）」。
    /// <para>
    /// <b>wrapper の <c>gpu_used</c>／<c>gpu_total</c> はもう帯に出さない</b>（裁定 110・2026-09-08）＝
    /// あれは <c>torch.cuda.mem_get_info</c> の値で、Windows の ROCm では<b>カード全体でも
    /// 自プロセスの OS 上の占有でもない</b>（実測＝同じ瞬間に <c>gpu_used</c> 3.66 GiB・
    /// OS の計数は自プロセス 10.84 GiB／カード全体 29.79 GiB）。「GPU 全体」を名乗れるのは
    /// OS の計数のほうだけなので、名前は OS の行へ渡し、torch の 2 つは
    /// <b>torch の見た値</b>と明記して残す（契約 ⑹ の JSON は据え置き）。
    /// </para>
    /// <para>
    /// <b>「（最大 …）」は使用量の隣に置く</b>（是正・便 D（3）・low 6 の ⑵）＝
    /// <c>max</c> は <c>max_memory_allocated</c>＝<b>使用量の山</b>である。
    /// </para>
    /// <para>
    /// <b>数字が無い理由を 2 つに分ける</b>（同上）＝<paramref name="serverAnswered"/> が偽＝
    /// そもそも誰も起きていない（<see cref="UiText.NotRunning"/>）・真で <c>memory</c> が
    /// 無い＝起きている個体にこの口が無い（<see cref="UiText.NotSupported"/>）。
    /// </para>
    /// </summary>
    /// <param name="memory"><c>/ywk/status.memory</c>（欄ごと無ければ null）。</param>
    /// <param name="serverAnswered"><c>/ywk/status</c> そのものが返ってきているか。</param>
    /// <param name="osRows">
    /// OS の計数の行（裁定 110＝<see cref="OsGpuMemory.Aggregate"/> の返り）。
    /// 空＝計数が読めない・その pid がどの GPU も触っていない＝<b>OS の組は出さない</b>。
    /// </param>
    public static string DescribeMemory(
        MemoryStatus? memory,
        bool serverAnswered = true,
        IReadOnlyList<OsGpuMemoryRow>? osRows = null)
    {
        if (memory is null)
        {
            return serverAnswered ? UiText.NotSupported : UiText.NotRunning;
        }

        if (memory.IsCpu)
        {
            return "CPU（GPU メモリなし）" + ErrorSuffix(memory.Error);
        }

        var os = DescribeOsGpuRows(osRows);

        if (!memory.HasNumbers)
        {
            // device が null＝モデル未読込（裁定 87 ⑴＝数値欄は null で latents だけ来る）。
            // OS の計数は torch の外から見た値なので、読めていればここでも出す。
            return UiStrings.StatusModelNotLoaded + os + ErrorSuffix(memory.Error);
        }

        var used = UiStrings.StatusMemoryUsed + " " + UiText.Bytes(memory.AllocatedBytes);
        if (memory.MaxAllocatedBytes is not null)
        {
            used += "（最大 " + UiText.Bytes(memory.MaxAllocatedBytes) + "）";
        }

        var text = used + "／" + UiStrings.StatusMemoryReserved + " " + UiText.Bytes(memory.ReservedBytes) + os;

        return text + ErrorSuffix(memory.Error);
    }

    /// <summary>
    /// OS の計数の組（<b>純関数</b>・裁定 110 D4）＝
    /// <c>／このプロセス x／GPU 全体 y / z（名前）</c>。
    /// <para>
    /// <b>GPU 1 枚なら名前は行末の括弧</b>・<b>2 枚以上なら組ごとに名前を頭に立てる</b>
    /// （<c>／&lt;名前&gt;＝このプロセス x／GPU 全体 y / z</c>）＝模型と codec を別の GPU に
    /// 載せた個体は 2 組出る（司令官の指示 1＝複数 GPU も想定する）。
    /// </para>
    /// <para>
    /// 「GPU 全体」は<b>その GPU の専用メモリの占有（他のプログラム込み）／専用メモリの総量</b>で、
    /// <b>共有メモリは入らない</b>（司令官の指示 2）。読めない数は「—」（0 と混ぜない）。
    /// </para>
    /// <para>
    /// <b>同じ名前の GPU が並んだら LUID を添える</b>（是正・2026-09-08）＝この機体の DXGI は
    /// <c>AMD Radeon(TM) 8060S Graphics</c> を<b>4 つ</b>名乗る（総量も同じ）。python が 2 つ目の
    /// LUID にも載った瞬間、名前だけでは<b>どちらがどれか読めない</b>ので、重なった名前にだけ
    /// <c>（0x000137d0）</c> のように LUID の下位を付ける（重ならない名前はそのまま）。
    /// </para>
    /// </summary>
    public static string DescribeOsGpuRows(IReadOnlyList<OsGpuMemoryRow>? rows)
    {
        if (rows is null || rows.Count == 0)
        {
            return string.Empty;
        }

        var single = rows.Count == 1;
        var text = new System.Text.StringBuilder();
        foreach (var row in rows)
        {
            if (row is null)
            {
                continue;
            }

            var label = Label(row, rows);
            text.Append('／');
            if (!single)
            {
                text.Append(label).Append('＝');
            }

            text.Append("このプロセス ").Append(UiText.Bytes(row.ProcessBytes))
                .Append("／GPU 全体 ").Append(UiText.Bytes(row.AdapterBytes))
                .Append(" / ").Append(UiText.Bytes(row.TotalBytes));

            if (single)
            {
                text.Append('（').Append(label).Append('）');
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// その行の見出し（<b>純関数</b>）＝名前が重なっていなければ名前そのもの、
    /// 重なっていれば <c>名前（0x&lt;LowPart&gt;）</c>。
    /// </summary>
    private static string Label(OsGpuMemoryRow row, IReadOnlyList<OsGpuMemoryRow> rows)
    {
        var same = 0;
        foreach (var other in rows)
        {
            if (other is not null
                && string.Equals(other.Name, row.Name, StringComparison.OrdinalIgnoreCase))
            {
                same++;
            }
        }

        if (same <= 1)
        {
            return row.Name;
        }

        var at = row.Luid.LastIndexOf("_0x", StringComparison.OrdinalIgnoreCase);
        var tail = at >= 0 ? row.Luid[(at + 1)..] : row.Luid;
        return row.Name + "（" + tail + "）";
    }

    /// <summary>
    /// 参照潜在キャッシュの 1 行（<b>純関数</b>・裁定 67 ⑴）＝
    /// <c>ON／OFF（焼いた話者 n 名・合計 m MB）</c>。
    /// <c>memory.latents</c> が正本で、無ければ一覧の <c>latent</c> の数だけを名乗る。
    /// </summary>
    public static string DescribeLatentCache(
        bool on, MemoryStatus? memory, IReadOnlyList<VoiceRow>? rows)
    {
        var head = on ? "ON" : "OFF";

        if (memory is not null && memory.LatentCount > 0)
        {
            return head + "（下ごしらえ済みの声 " + memory.LatentCount.ToString(CultureInfo.InvariantCulture)
                + " 人・合計 " + UiText.Bytes(memory.EffectiveLatentsTotal) + "）";
        }

        if (memory is not null)
        {
            return head + "（下ごしらえ済みの声 0 人・合計 " + UiText.Bytes(0L) + "）";
        }

        var baked = 0;
        if (rows is not null)
        {
            foreach (var row in rows)
            {
                if (row.HasLatent)
                {
                    baked++;
                }
            }
        }

        return head + "（下ごしらえ済みの声 " + baked.ToString(CultureInfo.InvariantCulture)
            + " 人・合計 " + UiText.Missing + "）";
    }

    /// <summary>
    /// 参照ボイスの概算の 1 行（<b>純関数</b>・裁定 67 ⑵・low 13）。
    /// <b>1 名あたりが主役</b>＝選んでいる話者の値を先に出し、全員分は
    /// 「全部を同時に載せたときの上限」と明記して括弧に落とす。
    /// </summary>
    public static string DescribeVoiceMemory(VoiceRow? selected, IReadOnlyList<VoiceRow>? rows)
    {
        if (selected is null && (rows is null || rows.Count == 0))
        {
            return UiText.Missing;
        }

        var upper = 0L;
        if (rows is not null)
        {
            foreach (var row in rows)
            {
                upper += row.MemoryBytes;
            }
        }

        var tail = rows is null || rows.Count == 0
            ? string.Empty
            : "（全員ぶん＝" + UiText.Bytes(upper) + "）";

        if (selected is null)
        {
            return "1 人あたり＝声を選ぶと出ます" + tail;
        }

        return "1 人あたり「" + selected.DisplayName + "」＝" + selected.MemoryText + tail;
    }

    /// <summary>
    /// <c>memory.error</c> の添え方（<b>純関数</b>）。
    /// <para>
    /// <b>何が読めなかったのかを名乗る</b>（是正・便 D（3）・low 6 の ⑵）＝1 巡目は
    /// <c>「：」＋逐語</c> だけだったので、<c>使用量 1.73 GB…：mem_get_info が失敗しました。</c> と
    /// 並んで<b>出ている数字そのものの説明</b>に見えた。出た数字は正しく、読めなかったのは
    /// 別の欄である、と読める形にする（逐語は畳まない＝wrapper の 1 行をそのまま出す）。
    /// </para>
    /// </summary>
    public static string ErrorSuffix(string? error) =>
        string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : "（一部の欄が読めませんでした：" + error.Trim() + "）";

    /// <summary>
    /// 詳細の 2 行目「いま動いている場所」（<b>純関数</b>・`v2-spec.md` §2-2 の 2）。
    /// <para>
    /// <b>製品名だけを出す</b>（是正・段 C の検分）＝1 巡目は
    /// <c>cuda:0／NVIDIA GeForce RTX 3090（gfx1151）／bf16</c> の形で、
    /// <c>device.actual</c> の生の綴りと <c>gcnArchName</c> をそのまま並べていた。
    /// <c>cuda:0</c> は `v2-copy.md` §1-2 の 40 行目が削除と決めた機械の綴りで、
    /// <c>gfx1151</c> は憲章 附録 5 が「綴りだけを落とす」と決めた語である。
    /// 名が読めない回は <c>GPU</c>／<c>CPU（GPU を使いません）</c> の普通の語へ落とす。
    /// <b>bf16・FP32 は残す</b>（憲章 §6-2＝詳細の中に出してよい技術語）。
    /// 生の <c>device</c>／<c>gcn_arch</c> は<b>記録の側にだけ</b>在る
    /// （<c>ServerLogParser</c> が読む wrapper の banner 行と <c>/ywk/status</c> の JSON）。
    /// </para>
    /// </summary>
    private static string Compose(StatusDevice? device)
    {
        if (device is null)
        {
            return UiText.Missing;
        }

        var name = string.IsNullOrWhiteSpace(device.Name) ? null : device.Name.Trim();
        var precision = string.IsNullOrWhiteSpace(device.Precision) ? null : device.Precision.Trim();

        // actual は実測値（モデル未読込なら null）＝**綴りは出さず、CPU か GPU かだけを読む**。
        // CPU で載った回は名が付いていても「CPU（GPU を使いません）」が事実である。
        var actual = device.Actual?.Trim();
        var onCpu = !string.IsNullOrEmpty(actual)
            && actual.StartsWith("cpu", StringComparison.OrdinalIgnoreCase);

        var text = onCpu
            ? UiStrings.StatusDeviceCpu
            : name ?? (string.IsNullOrEmpty(actual) ? UiText.Missing : UiStrings.StatusDeviceGpu);

        if (precision is not null)
        {
            text += "／" + precision;
        }

        return text;
    }

    private static string Compose(WarmupStatus? warmup)
    {
        if (warmup is null || string.IsNullOrWhiteSpace(warmup.State))
        {
            return "—";
        }

        var state = warmup.State.Trim();
        var label = state switch
        {
            "idle" => "未実施",
            "running" => "実行中",
            "done" => "完了",
            "failed" => "失敗",
            "cancelled" => "中止",
            _ => state,
        };

        if (state == "running" || state == "done")
        {
            label += "（" + UiText.Progress(warmup.ShotsDone ?? warmup.Shots, warmup.ShotsTotal) + " 射）";
        }

        if (!string.IsNullOrWhiteSpace(warmup.Error))
        {
            label += "：" + warmup.Error.Trim();
        }

        return label;
    }

    private static string Compose(PrecomputeStatus? precompute)
    {
        if (precompute is null || string.IsNullOrWhiteSpace(precompute.State))
        {
            return "—";
        }

        var state = precompute.State.Trim();
        var label = state switch
        {
            "idle" => "未実施",
            "running" => "実行中",
            "done" => "完了",
            "failed" => "失敗",
            "cancelled" => "中止",
            _ => state,
        };

        if (state == "running" || state == "done")
        {
            label += "（" + UiText.Progress(precompute.Done, precompute.Total) + " 件）";
        }

        if (!string.IsNullOrWhiteSpace(precompute.Error))
        {
            label += "：" + precompute.Error.Trim();
        }

        return label;
    }

    /// <summary>
    /// 焼いた上流 pin と wrapper が名乗った pin の突合（<b>純関数</b>）。
    /// 開発ビルドは焼かれていないので黙る（null）。
    /// </summary>
    public static string? DescribeUpstreamMismatch(StatusUpstream? upstream)
    {
        if (upstream is null || !AppVersion.IsReleaseBuild)
        {
            return null;
        }

        var mine = AppVersion.UpstreamIrodoriTts;
        var theirs = upstream.IrodoriTts;
        if (string.IsNullOrWhiteSpace(mine) || string.IsNullOrWhiteSpace(theirs))
        {
            return null;
        }

        return StartsWithEither(mine, theirs)
            ? null
            : UiStrings.StatusUpstreamMismatch;
    }

    private static bool StartsWithEither(string a, string b)
    {
        var left = a.Trim();
        var right = b.Trim();
        return left.StartsWith(right, StringComparison.OrdinalIgnoreCase)
            || right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
    }
}
