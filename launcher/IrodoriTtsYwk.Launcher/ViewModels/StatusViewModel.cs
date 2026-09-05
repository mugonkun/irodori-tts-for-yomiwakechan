using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Mvvm;

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
    private string _memoryText = UiText.NotSupported;
    private bool _memorySupported;
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
    private IReadOnlyList<VoiceRow>? _rows;
    private VoiceRow? _selectedVoice;

    public StatusViewModel(Func<Task> start, Func<Task> stop)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(stop);

        StartCommand = new AsyncRelayCommand(start, () => !IsRunning);
        StopCommand = new AsyncRelayCommand(stop, () => CanStop);
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
    /// 停止も起動も押せなくなった＝逃げ道がトレイの「終了」だけになる。
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

    /// <summary>状態の日本語（トレイと共用＝1 箇所で綴る）。</summary>
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

        VariantText = RuntimeVariants.DisplayName(variant);
        EndpointText = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
        GpuText = string.IsNullOrWhiteSpace(gpuName) && string.IsNullOrWhiteSpace(gpuUuid)
            ? (RuntimeVariants.IsCpu(variant) ? "CPU（GPU を使いません）" : "未選択")
            : (gpuName ?? "GPU") + "（UUID …" + UiText.UuidTail(gpuUuid) + "）";

        SettingsPendingText = DescribePending(_running, settings);
        RepaintMemory();
    }

    /// <summary>
    /// GPU メモリ・参照潜在キャッシュ・参照ボイスの 3 欄を引き直す（材料が変わるたびに呼ぶ）。
    /// </summary>
    private void RepaintMemory()
    {
        MemorySupported = _memory is not null;
        MemoryText = DescribeMemory(_memory);

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

        return differs
            ? "設定は次回の起動から有効です（いま走っているのは "
              + RuntimeVariants.DisplayName(running.Variant)
              + "・http://127.0.0.1:" + running.Port.ToString(CultureInfo.InvariantCulture)
              + "・" + (string.IsNullOrWhiteSpace(running.GpuName) ? "GPU 未選択" : running.GpuName)
              + "）。"
            : null;
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
        State = state;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Reason = reason.Trim();
        }
        else if (state is not ServerState.Failed)
        {
            Reason = null;
        }

        if (state is ServerState.Stopped)
        {
            DeviceText = UiText.Missing;
            WarmupText = "—";
            PrecomputeText = "—";
            NoticesText = null;
            _memory = null;
            RepaintMemory();
        }
    }

    /// <summary>
    /// 起動の告知（「未実測の帯」＝裁定 88 ⑵）をそのまま状態帯に載せる。
    /// </summary>
    public void ApplyNotices(IReadOnlyList<string>? notices) =>
        NoticesText = ComposeNotices(notices);

    /// <summary>
    /// 起動の結末を載せる（裁定 88 ⑴⑵）＝<b>門で断られた理由 1 行と告知をそのまま出す</b>。
    /// <para>
    /// 理由は状態機械が <see cref="Reason"/> に書くのが筋だが、<b>断られた事実が
    /// 状態に載らない実装でも画面から消えない</b>ように、<see cref="Reason"/> と食い違うときだけ
    /// 告知の先頭に足す（同じ文言を 2 度出さない）。
    /// </para>
    /// </summary>
    public void ApplyStartOutcome(bool ok, string? failureReason, IReadOnlyList<string>? notices) =>
        NoticesText = ComposeStartOutcome(ok, failureReason, notices, Reason);

    /// <summary>起動の結末の畳み方（<b>純関数</b>）。</summary>
    public static string? ComposeStartOutcome(
        bool ok, string? failureReason, IReadOnlyList<string>? notices, string? shownReason)
    {
        var lines = new List<string>();
        if (!ok && !string.IsNullOrWhiteSpace(failureReason)
            && !string.Equals(shownReason?.Trim(), failureReason.Trim(), StringComparison.Ordinal))
        {
            lines.Add(failureReason.Trim());
        }

        if (notices is not null)
        {
            lines.AddRange(notices);
        }

        return ComposeNotices(lines);
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

    /// <summary>stderr の 1 行（畳んでから入れる）。</summary>
    public void AppendLog(string? line)
    {
        _log.Append(line);
        LogText = _log.Text;
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
    public void ApplyStatus(StatusResponse? status)
    {
        if (status is null)
        {
            _memory = null;
            RepaintMemory();
            GpuMismatch = null;
            return;
        }

        DeviceText = Compose(status.Device);
        WarmupText = Compose(status.Warmup);
        PrecomputeText = Compose(status.Precompute);
        _memory = status.Memory;
        RepaintMemory();
        UpstreamMismatch = DescribeUpstreamMismatch(status.Upstream);
        GpuMismatch = DescribeGpuMismatch(_running?.GpuUuid ?? _desired?.GpuUuid, status.Device);
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
            : "選んだ GPU（UUID …" + UiText.UuidTail(wantedUuid)
              + "）と、実際に載った GPU（UUID …" + UiText.UuidTail(actual)
              + "／" + (device?.Name ?? "名前不明") + "）が違います。";
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
    /// GPU メモリの 1 行（<b>純関数</b>・裁定 67 ⑶・87 ⑴）＝
    /// <b>使用量＝allocated・占有量＝reserved・GPU 全体＝gpu_used／gpu_total</b>。
    /// null は「—」・<c>cpu</c> は「CPU（GPU メモリなし）」・欄ごと無ければ「未対応」。
    /// </summary>
    public static string DescribeMemory(MemoryStatus? memory)
    {
        if (memory is null)
        {
            return UiText.NotSupported;
        }

        if (memory.IsCpu)
        {
            return "CPU（GPU メモリなし）" + ErrorSuffix(memory.Error);
        }

        if (!memory.HasNumbers)
        {
            // device が null＝モデル未読込（裁定 87 ⑴＝数値欄は null で latents だけ来る）
            return UiText.Missing + "（モデル未読込）" + ErrorSuffix(memory.Error);
        }

        var text = "使用量 " + UiText.Bytes(memory.AllocatedBytes)
            + "／占有量 " + UiText.Bytes(memory.ReservedBytes)
            + "／GPU 全体 " + UiText.Bytes(memory.EffectiveGpuUsed)
            + " / " + UiText.Bytes(memory.GpuTotalBytes);

        if (memory.MaxAllocatedBytes is not null)
        {
            text += "（最大 " + UiText.Bytes(memory.MaxAllocatedBytes) + "）";
        }

        return text + ErrorSuffix(memory.Error);
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
            return head + "（焼いた話者 " + memory.LatentCount.ToString(CultureInfo.InvariantCulture)
                + " 名・合計 " + UiText.Bytes(memory.EffectiveLatentsTotal) + "）";
        }

        if (memory is not null)
        {
            return head + "（焼いた話者 0 名・合計 " + UiText.Bytes(0L) + "）";
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

        return head + "（焼いた話者 " + baked.ToString(CultureInfo.InvariantCulture)
            + " 名・合計 " + UiText.Missing + "）";
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
            : "（全員分＝全部を同時に載せたときの上限 " + UiText.Bytes(upper) + "）";

        if (selected is null)
        {
            return "1 名あたり＝話者を選ぶと出ます" + tail;
        }

        return "1 名あたり「" + selected.DisplayName + "」＝" + selected.MemoryText + tail;
    }

    private static string ErrorSuffix(string? error) =>
        string.IsNullOrWhiteSpace(error) ? string.Empty : "：" + error.Trim();

    private static string Compose(StatusDevice? device)
    {
        if (device is null)
        {
            return UiText.Missing;
        }

        // actual は実測値（モデル未読込なら null）＝設定値と食い違ったらそれが事実である。
        var actual = string.IsNullOrWhiteSpace(device.Actual) ? UiText.Missing : device.Actual.Trim();
        var name = string.IsNullOrWhiteSpace(device.Name) ? null : device.Name.Trim();
        var precision = string.IsNullOrWhiteSpace(device.Precision) ? null : device.Precision.Trim();
        var arch = string.IsNullOrWhiteSpace(device.GcnArch) ? null : device.GcnArch.Trim();

        var text = actual;
        if (name is not null)
        {
            text += "／" + name;
        }

        if (arch is not null)
        {
            text += "（" + arch + "）";
        }

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
            : "配布物の上流 pin（" + mine + "）と、走っているサーバの pin（" + theirs.Trim() + "）が違います。";
    }

    private static bool StartsWithEither(string a, string b)
    {
        var left = a.Trim();
        var right = b.Trim();
        return left.StartsWith(right, StringComparison.OrdinalIgnoreCase)
            || right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
    }
}
