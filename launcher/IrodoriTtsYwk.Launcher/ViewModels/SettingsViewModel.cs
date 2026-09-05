using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Mvvm;
using IrodoriTtsYwk.Launcher.Services.Ledger;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// 設定（設計書 §3・§5・裁定 65・67・69）。
/// <para>
/// <b>編集は写しの上で行い、「適用」で初めて本物へ書く</b>（<see cref="LauncherSettings.Clone"/>）＝
/// 途中の半端な値（空のポート・綴り違いの変種）が走っているサーバの env に混ざらない。
/// 「取り消し」は写しを捨てるだけである。
/// </para>
/// <para>
/// <b>精度は Radeon 版では触らせない</b>（裁定 5・36＝<c>rocm-*</c> に fp32 を載せると
/// wrapper が exit 2 で止まる）。<b>変種の自動切替はしない</b>（裁定 4）＝ドライバが下限に
/// 届かないときも「勧める」だけで、選ぶのは利用者である。
/// </para>
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly LauncherSettings _live;
    private readonly ISettingsStore _store;
    private readonly AppPaths _paths;
    private readonly IGpuEnumerator? _gpuEnumerator;
    private readonly IDriverCheck _driverCheck;

    private LauncherSettings _draft;
    private string _warmupStagesText;
    private string _warmupVoicesText;
    private string _message = string.Empty;
    private string _driverText = UiText.Missing;
    private string _gpuMessage = string.Empty;
    private GpuInfo? _selectedGpu;
    private bool _dirty;
    private int _cacheFiles;
    private long _cacheBytes;
    private string _cacheMessage = string.Empty;

    public SettingsViewModel(
        LauncherSettings settings,
        ISettingsStore store,
        AppPaths paths,
        IGpuEnumerator? gpuEnumerator,
        IDriverCheck driverCheck)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(driverCheck);

        _live = settings;
        _store = store;
        _paths = paths;
        _gpuEnumerator = gpuEnumerator;
        _driverCheck = driverCheck;

        _draft = settings.Clone();
        _warmupStagesText = WarmupStagesText.Format(_draft.WarmupStages);
        _warmupVoicesText = string.Join(", ", _draft.WarmupVoices);

        Flavor = ReleaseFlavors.Detect(ReleaseFlavors.LedgerNames(paths.LedgerDir));
        VariantChoices = ReleaseFlavors.AvailableChoices(
            Flavor, ReleaseFlavors.LedgerNames(paths.LedgerDir));

        RefreshGpusCommand = new AsyncRelayCommand(RefreshGpusAsync);
        ApplyCommand = new RelayCommand(Apply, () => IsDirty);
        RevertCommand = new RelayCommand(Revert, () => IsDirty);
        ClearCacheCommand = new RelayCommand(ClearCache, () => CacheBytes > 0);

        // 捕れなかった例外を握り潰さない（§20-5 ⑴）。
        RefreshGpusCommand.Faulted += (_, line) => GpuMessage = "GPU を数えられませんでした：" + line;

        UpdateDriverText();
        RefreshCache();
    }

    /// <summary>このリリースが持つ変種（裁定 5＝Radeon 版に CUDA の選択肢は出さない）。</summary>
    public ReleaseFlavor Flavor { get; }

    /// <summary>変種の選択肢（台帳が在る物だけ）。</summary>
    public IReadOnlyList<string> VariantChoices { get; }

    /// <summary>列挙した GPU。</summary>
    public ObservableCollection<GpuInfo> Gpus { get; } = [];

    /// <summary>精度の選択肢（上級者・null＝device 連動に任せる＝裁定 7）。</summary>
    public static IReadOnlyList<string> PrecisionChoices => ["（device 連動・既定）", "bf16", "fp16", "fp32"];

    public AsyncRelayCommand RefreshGpusCommand { get; }

    public RelayCommand ApplyCommand { get; }

    public RelayCommand RevertCommand { get; }

    /// <summary>編集中の写し（画面はこれに束縛する）。</summary>
    public LauncherSettings Draft => _draft;

    /// <summary>書いていない変更があるか。</summary>
    public bool IsDirty
    {
        get => _dirty;
        private set
        {
            if (SetProperty(ref _dirty, value))
            {
                ApplyCommand.RaiseCanExecuteChanged();
                RevertCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>ドライバ検査の 1 行（設計書 §3・受け入れ条件の「ドライバ」行）。</summary>
    public string DriverText
    {
        get => _driverText;
        private set => SetProperty(ref _driverText, value);
    }

    public string GpuMessage
    {
        get => _gpuMessage;
        private set => SetProperty(ref _gpuMessage, value);
    }

    public GpuInfo? SelectedGpu
    {
        get => _selectedGpu;
        set
        {
            if (!SetProperty(ref _selectedGpu, value))
            {
                return;
            }

            if (value is not null)
            {
                // 保存するのは UUID（裁定 34）。名前は見つからないときの告知にしか使わない。
                _draft.GpuUuid = value.Uuid;
                _draft.GpuName = value.Name;
                Touch();
                UpdateDriverText();
            }
        }
    }

    public string Variant
    {
        get => _draft.Variant;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(_draft.Variant, value, StringComparison.Ordinal))
            {
                return;
            }

            _draft.Variant = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(VariantDisplayName));
            RaisePropertyChanged(nameof(PrecisionEnabled));
            RaisePropertyChanged(nameof(PrecisionNote));
            RaisePropertyChanged(nameof(PrecomputeNote));
            RaisePropertyChanged(nameof(ReadyTimeoutNote));
            Touch();
            UpdateDriverText();
        }
    }

    public string VariantDisplayName => RuntimeVariants.DisplayName(_draft.Variant);

    /// <summary>精度を触らせるか（Radeon 版は bf16 固定＝裁定 5・36）。</summary>
    public bool PrecisionEnabled => RuntimeVariants.AllowsPrecisionOverride(_draft.Variant);

    public string PrecisionNote => PrecisionEnabled
        ? "上級者向け。既定は device 連動（GPU→bf16・CPU→fp32）です。"
        : "この変種は bf16 固定です（fp32 を載せるとサーバが起動前に止まります）。";

    /// <summary>精度の選択（「（device 連動・既定）」＝載せない）。</summary>
    public string PrecisionChoice
    {
        get => string.IsNullOrWhiteSpace(_draft.Precision) ? PrecisionChoices[0] : _draft.Precision;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) || value == PrecisionChoices[0] ? null : value;
            if (string.Equals(_draft.Precision, next, StringComparison.Ordinal))
            {
                return;
            }

            _draft.Precision = next;
            RaisePropertyChanged();
            Touch();
        }
    }

    public int Port
    {
        get => _draft.Port;
        set
        {
            if (_draft.Port == value)
            {
                return;
            }

            _draft.Port = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(PortNote));
            Touch();
        }
    }

    /// <summary>ポートの注記（裁定 2・52）。</summary>
    public string PortNote => _draft.Port == LauncherSettings.DefaultPort
        ? "既定 18088。塞がっていたら次を探さずに止まって告げます。"
        : "既定（18088）から変えています。本体（読み分けちゃん2）の接続先も同じ値にしてください。";

    public bool WarmupOnStart
    {
        get => _draft.WarmupOnStart;
        set
        {
            if (_draft.WarmupOnStart == value)
            {
                return;
            }

            _draft.WarmupOnStart = value;
            RaisePropertyChanged();
            Touch();
        }
    }

    /// <summary>暖機の段（秒・カンマ区切り）。</summary>
    public string WarmupStagesInput
    {
        get => _warmupStagesText;
        set
        {
            if (SetProperty(ref _warmupStagesText, value))
            {
                Touch();
            }
        }
    }

    /// <summary>暖機で撃つ話者（カンマ区切り）。</summary>
    public string WarmupVoicesInput
    {
        get => _warmupVoicesText;
        set
        {
            if (SetProperty(ref _warmupVoicesText, value))
            {
                Touch();
            }
        }
    }

    /// <summary>参照潜在キャッシュ（裁定 65・67 ⑴）。null＝変種の既定。</summary>
    public bool? PrecomputeOnStart
    {
        get => _draft.PrecomputeOnStart;
        set
        {
            if (_draft.PrecomputeOnStart == value)
            {
                return;
            }

            _draft.PrecomputeOnStart = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(PrecomputeNote));
            Touch();
        }
    }

    public string PrecomputeNote
    {
        get
        {
            var effective = _draft.EffectivePrecomputeOnStart();
            var head = _draft.PrecomputeOnStart is null
                ? "変種の既定（" + (RuntimeVariants.PrecomputeOnStartDefault(_draft.Variant) ? "ON" : "OFF") + "）に従います。"
                : "明示しています。";
            return head + " いまの実効値＝" + (effective ? "ON" : "OFF")
                + "。ON にすると話者の登録時に参照潜在を焼き、話者を切り替えるときの待ちが減ります。";
        }
    }

    /// <summary>裁定 69＝設定項目・初期値 0。</summary>
    public int EmptyCacheInterval
    {
        get => _draft.EmptyCacheInterval;
        set
        {
            if (_draft.EmptyCacheInterval == value)
            {
                return;
            }

            _draft.EmptyCacheInterval = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(EmptyCacheNote));
            Touch();
        }
    }

    public string EmptyCacheNote => _draft.EmptyCacheInterval == 0
        ? "0＝解放しません（既定）。GPU メモリの使用量は 1 セッションで単調に増えます。"
        : "この回数ごとに GPU のキャッシュを解放します（合成のたびに少し遅くなります）。";

    /// <summary>GPU メモリ欄の常時表示（裁定 67 ⑶）。</summary>
    public bool ShowMemoryPanel
    {
        get => _draft.ShowMemoryPanel;
        set
        {
            if (_draft.ShowMemoryPanel == value)
            {
                return;
            }

            _draft.ShowMemoryPanel = value;
            RaisePropertyChanged();
            Touch();
        }
    }

    public bool AutoStartServer
    {
        get => _draft.AutoStartServer;
        set
        {
            if (_draft.AutoStartServer == value)
            {
                return;
            }

            _draft.AutoStartServer = value;
            RaisePropertyChanged();
            Touch();
        }
    }

    /// <summary>ready 待ち（秒）。0＝変種の既定。</summary>
    public int ReadyTimeoutSeconds
    {
        get => _draft.ReadyTimeoutSeconds;
        set
        {
            if (_draft.ReadyTimeoutSeconds == value)
            {
                return;
            }

            _draft.ReadyTimeoutSeconds = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(ReadyTimeoutNote));
            Touch();
        }
    }

    public string ReadyTimeoutNote => "いまの実効値＝"
        + _draft.EffectiveReadyTimeout().TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " 秒"
        + (_draft.ReadyTimeoutSeconds <= 0 ? "（変種の既定）" : string.Empty) + "。";

    // ---- 取得キャッシュ（裁定 90 Q-E2 ⑶ の ⒝＝手で消す口） ---------------------

    /// <summary>取得キャッシュを手で消す。</summary>
    public RelayCommand ClearCacheCommand { get; }

    /// <summary>いま cache に居る原檔のバイト（<see cref="CacheCleaner.Measure"/>）。</summary>
    public long CacheBytes => _cacheBytes;

    /// <summary>同・檔数。</summary>
    public int CacheFiles => _cacheFiles;

    /// <summary>
    /// ボタンの文言（裁定 90 Q-E2 ⑶ の逐語＝「取得キャッシュを消す（n GB）」）。
    /// <b>空なら押せない</b>ことが文言からも判るようにする。
    /// </summary>
    public string ClearCacheText => _cacheBytes > 0
        ? "取得キャッシュを消す（" + FetchPlanner.FormatBytes(_cacheBytes) + "）"
        : "取得キャッシュを消す（空です）";

    /// <summary>消した結果・消せない理由の 1 行。</summary>
    public string CacheMessage
    {
        get => _cacheMessage;
        private set => SetProperty(ref _cacheMessage, value);
    }

    /// <summary>
    /// cache の量を数え直す（画面を開いたときと、消した後）。
    /// <para>
    /// <b>数えるのは置き場ごと</b>（入れ子も <c>.part</c> も＝裁定 95 ⑵ の追認）。ただし
    /// <b>まだ関門を通していない変種だけが名指す原檔</b>は数にも削除にも入れない
    /// （<see cref="CacheCleaner.ProtectedFileNames"/>）＝ボタンに出る量が、押したときに
    /// 実際に消える量と揃う。註が「台帳の item の檔だけ」と実装の逆を言っていたのを直した
    /// （是正・便 D（3）の 3 巡目・low）。
    /// </para>
    /// </summary>
    public void RefreshCache()
    {
        var measured = CacheCleaner.Measure(_paths.DownloadCacheDir, ProtectedCacheNames());

        _cacheFiles = measured.Files;
        _cacheBytes = measured.Bytes;
        RaisePropertyChanged(nameof(CacheBytes));
        RaisePropertyChanged(nameof(CacheFiles));
        RaisePropertyChanged(nameof(ClearCacheText));
        ClearCacheCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 消す（<b>関門は <see cref="CacheCleaner.Blocked"/></b>＝実行系が組み上がっていて、
    /// 展開に使った台帳と配布樹の台帳が一致するときだけ）。
    /// </summary>
    public void ClearCache()
    {
        var result = CacheCleaner.Clean(
            _paths.DownloadCacheDir,
            _paths.ResolvePythonExe(_live.Variant) is not null,
            _live.RuntimeLedgerFor(_live.Variant),
            RuntimeStamp.LedgerSha256(_paths, _live.Variant),
            ProtectedCacheNames());

        CacheMessage = result.Message;
        RefreshCache();
    }

    /// <summary>
    /// 掃除で<b>残す</b>原檔の名＝まだ関門を通していない変種<b>だけ</b>が名指す檔
    /// （<see cref="CacheCleaner.ProtectedFileNames"/>）。
    /// </summary>
    private IReadOnlyCollection<string> ProtectedCacheNames() =>
        CacheCleaner.ProtectedFileNames(
            _paths,
            _live,
            CacheCleaner.LedgerVariants(ReleaseFlavors.LedgerNames(_paths.LedgerDir)),
            variant => FirstRunViewModel.TryPlan(_paths, variant, skipVcRedist: true));

    /// <summary>データの置き場（読むだけ・移動は未対応）。</summary>
    public string DataDirText => _paths.DataDir;

    public string ModelDirText => _paths.HfHomeDir;

    public string VoicesDirText => _paths.VoicesDir;

    public string AppDirText => _paths.AppDir;

    public string RuntimeRootText => _paths.RuntimeRoot;

    /// <summary>GPU を数え直す（受け入れ条件 D-2＝≤ 5 s）。</summary>
    public async Task RefreshGpusAsync()
    {
        if (_gpuEnumerator is null)
        {
            GpuMessage = "GPU の列挙系がまだ組み込まれていません（便 D・起動席の実装待ち）。";
            return;
        }

        GpuMessage = "GPU を数えています…";
        var pythonExe = _paths.ResolvePythonExe(_draft.Variant);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var result = await _gpuEnumerator
                .EnumerateAsync(new GpuEnumerationRequest(pythonExe, TimeSpan.FromSeconds(5)), cts.Token)
                .ConfigureAwait(true);

            Gpus.Clear();
            foreach (var gpu in result.Gpus)
            {
                Gpus.Add(gpu);
            }

            // 告知は「**前回の** GPU が見つからない」なので、写しを書き換える前に控える。
            var previousUuid = _draft.GpuUuid;
            var previousName = _draft.GpuName;
            var wasMissing = !string.IsNullOrWhiteSpace(previousUuid)
                && GpuResolver.Find(result.Gpus, previousUuid) is null;

            var saved = GpuResolver.Find(result.Gpus, previousUuid);
            var pick = saved ?? result.Gpus.FirstOrDefault();

            // 画面に出ている GPU と settings.json の UUID を食い違わせない（裁定 34・受け入れ条件 D-2）。
            // 初回（UUID 未設定）と「前回の GPU が見つからない」場合は、ここで選び直したものとして
            // 写しに UUID を書き、**未保存**として印を付ける＝利用者が「適用」を押すまで確定しない。
            // 印を付けないと、選択が見えているのに保存された UUID が空のままになる（実測）。
            _selectedGpu = pick;
            if (pick is not null && !GpuResolver.SameUuid(previousUuid, pick.Uuid))
            {
                _draft.GpuUuid = pick.Uuid;
                _draft.GpuName = pick.Name;
                RaisePropertyChanged(nameof(SelectedGpu));
                IsDirty = true;
            }
            else
            {
                RaisePropertyChanged(nameof(SelectedGpu));
            }

            GpuMessage = result.Gpus.Count == 0
                ? result.FailureReason ?? "GPU が見つかりませんでした。"
                : (wasMissing
                    ? GpuResolver.NotFoundMessage(previousName, previousUuid)
                    : result.Gpus.Count.ToString(CultureInfo.InvariantCulture) + " 台見つかりました（"
                      + UiText.Milliseconds(result.Elapsed.TotalMilliseconds) + "）。");

            UpdateDriverText();
        }
        catch (OperationCanceledException)
        {
            GpuMessage = "GPU の列挙が期限内に終わりませんでした。";
        }
    }

    /// <summary>写しを本物へ書く（原子的保存）。</summary>
    public void Apply()
    {
        if (!WarmupStagesText.TryParse(_warmupStagesText, out var stages, out var stageError))
        {
            Message = stageError ?? "暖機の段が読めません。";
            return;
        }

        if (!WarmupStagesText.TryParseVoices(_warmupVoicesText, out var voices, out var voiceError))
        {
            Message = voiceError ?? "暖機の話者が読めません。";
            return;
        }

        if (_draft.Port is < 1 or > 65535)
        {
            Message = "ポートは 1〜65535 です。";
            return;
        }

        if (!VariantChoices.Contains(_draft.Variant, StringComparer.Ordinal))
        {
            Message = "この配布物には変種「" + _draft.Variant + "」の取得台帳がありません。";
            return;
        }

        _draft.WarmupStages = [.. stages];
        _draft.WarmupVoices = [.. voices];

        CopyInto(_draft, _live);
        _store.Save(_live);
        _draft = _live.Clone();
        IsDirty = false;
        Message = "設定を保存しました。変種・GPU・ポートを変えたときは、サーバを起動し直すと効きます。";

        // 変種が変われば cache の勘定も変わる（消す対象はその変種の台帳の item）。
        RefreshCache();

        // 状態帯は設定から出す欄（GPU・変種・接続先）を持っている。ここで告げないと、
        // 「適用」した直後の状態帯が古い値（GPU＝未選択）を出したままになる（実測）。
        Applied?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 設定が本物に書かれた（<see cref="Apply"/> が通った）。
    /// <para>
    /// <see cref="MainViewModel"/> が拾って状態帯を引き直す。<b>画面どうしは互いを知らない</b>ので、
    /// 横の繋がりは束ねる側が配る（話者一覧の <c>RowsChanged</c> と同じ形）。
    /// </para>
    /// </summary>
    public event EventHandler? Applied;

    /// <summary>写しを捨てる。</summary>
    public void Revert()
    {
        Resync();
        IsDirty = false;
        Message = "変更を取り消しました。";
    }

    /// <summary>
    /// <b>本物が外で書き換わったので写しを取り直す</b>（是正・便 D（3）の 3 巡目・high の ⒜）。
    /// <para>
    /// 写しは <see cref="MainViewModel"/> の構築時に採るが、<b>初回取得ウィザードはその後に</b>
    /// 同じ <see cref="LauncherSettings"/> 個体（変種・同意・焼き印・完了の札）を書き換える。
    /// 取り直す口が無かったころは、設定頁で「適用」を 1 度押すだけでウィザードの成果が
    /// 構築時の値へ巻き戻った。<c>CopyInto</c> から外した欄（ウィザードと他画面の持ち物）に
    /// 加えて、<b>設定頁も編集する変種</b>を巻き戻さないために、ここで丸ごと合わせ直す。
    /// </para>
    /// <b>編集中（<see cref="IsDirty"/>）なら何もしない</b>＝利用者の入力を捨てない。
    /// </summary>
    public void SyncFromLive()
    {
        if (IsDirty)
        {
            return;
        }

        Resync();
    }

    private void Resync()
    {
        _draft = _live.Clone();
        _warmupStagesText = WarmupStagesText.Format(_draft.WarmupStages);
        _warmupVoicesText = string.Join(", ", _draft.WarmupVoices);
        RaiseAll();
    }

    private void Touch()
    {
        IsDirty = true;
        Message = string.Empty;
    }

    private void UpdateDriverText()
    {
        var driver = _selectedGpu?.DriverVersion;
        var verdict = _driverCheck.Check(_draft.Variant, driver);
        DriverText = verdict.Message
            + (verdict.SuggestedVariant is null
                ? string.Empty
                : "（勧め＝" + RuntimeVariants.DisplayName(verdict.SuggestedVariant) + "・自動では切り替えません）");
    }

    private void RaiseAll()
    {
        RaisePropertyChanged(nameof(Draft));
        RaisePropertyChanged(nameof(Variant));
        RaisePropertyChanged(nameof(VariantDisplayName));
        RaisePropertyChanged(nameof(PrecisionChoice));
        RaisePropertyChanged(nameof(PrecisionEnabled));
        RaisePropertyChanged(nameof(PrecisionNote));
        RaisePropertyChanged(nameof(Port));
        RaisePropertyChanged(nameof(PortNote));
        RaisePropertyChanged(nameof(WarmupOnStart));
        RaisePropertyChanged(nameof(WarmupStagesInput));
        RaisePropertyChanged(nameof(WarmupVoicesInput));
        RaisePropertyChanged(nameof(PrecomputeOnStart));
        RaisePropertyChanged(nameof(PrecomputeNote));
        RaisePropertyChanged(nameof(EmptyCacheInterval));
        RaisePropertyChanged(nameof(EmptyCacheNote));
        RaisePropertyChanged(nameof(ShowMemoryPanel));
        RaisePropertyChanged(nameof(AutoStartServer));
        RaisePropertyChanged(nameof(ReadyTimeoutSeconds));
        RaisePropertyChanged(nameof(ReadyTimeoutNote));
    }

    /// <summary>
    /// 写しの中身を本物へ移す（<b>参照ごと差し替えない</b>＝他の画面が握っている個体を保つ）。
    /// <para>
    /// <b>移すのは設定頁が編集する欄だけ</b>（是正・便 D（3）の 3 巡目・high）。
    /// <c>_draft</c> は <see cref="MainViewModel"/> の構築時に採った写しで、そのあと
    /// <b>初回取得ウィザードが同じ <see cref="LauncherSettings"/> 個体を書き換える</b>のに、
    /// 写しを取り直す口がどこにも無い。だから「適用」を 1 度押すだけで
    /// <c>firstRunCompleted</c>（裁定 94）・<c>acceptedNoticesSha256</c>（裁定 46）・
    /// 焼き印の表（裁定 91）・変種が<b>構築時の値へ巻き戻り</b>、
    /// ⑴ 次の起動でウィザードがまた開く ⑵ 通知同意を撃ち直させる ⑶ 焼き印が消えて
    /// 裁定 90 の掃除と「実行系を組み直す」の検知が両方死ぬ、という壊れ方をした（実射で再現）。
    /// <b>設定頁はこれらの欄を 1 つも編集しないので、写しに載せる理由が無い</b>。
    /// 「試し撃ちの記憶」（<c>lastTestVoice</c>／<c>lastTestNumSteps</c>＝<c>TryViewModel</c> が書く）と
    /// 話者の並びも同じ理由で外す。
    /// </para>
    /// </summary>
    public static void CopyInto(LauncherSettings from, LauncherSettings to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        to.Schema = from.Schema;
        to.GpuUuid = from.GpuUuid;
        to.GpuName = from.GpuName;
        to.Variant = from.Variant;
        to.Precision = from.Precision;
        to.Port = from.Port;
        to.DataDir = from.DataDir;
        to.HfHome = from.HfHome;
        to.WarmupOnStart = from.WarmupOnStart;
        to.WarmupStages = [.. from.WarmupStages];
        to.WarmupVoices = [.. from.WarmupVoices];
        to.WarmupText = from.WarmupText;
        to.PrecomputeOnStart = from.PrecomputeOnStart;
        to.EmptyCacheInterval = from.EmptyCacheInterval;
        to.UiScale = from.UiScale;
        to.ReadyTimeoutSeconds = from.ReadyTimeoutSeconds;
        to.AutoStartServer = from.AutoStartServer;
        to.ShowMemoryPanel = from.ShowMemoryPanel;

        // **ここから下は写さない**（上の註）＝ウィザード（firstRunCompleted・
        // acceptedNoticesSha256・runtimeLedgers・installedAppVersions）・話者一覧（voiceOrder）・
        // 試し撃ち（lastTestVoice・lastTestNumSteps）が持ち主の欄である。
    }
}
