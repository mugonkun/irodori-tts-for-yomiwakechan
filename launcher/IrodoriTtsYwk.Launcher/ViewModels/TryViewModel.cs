using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Mvvm;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// 試し撃ち（裁定 15＝利用者が自環境で GPU・変種・パラメータ・参照ボイスを試す画面）。
/// <para>
/// <b>撃つ前に 0 s で弾く</b>（<see cref="SpeechRequestBuilder"/>）＝上流は範囲検査を持たず
/// <c>num_steps:-5</c> も黙って通すので、UI が最後の砦である。
/// </para>
/// <para>
/// <b>合成は 1 プロセス 1 本の直列</b>（<c>max_concurrent_synthesis</c> 既定 1＝launcher/README §4 ⑧）。
/// 配信中にここで撃つと本体の読み上げが待たされる＝<b>本体が使っている間は譲る</b>
/// （決裁 130 Q4）＝<see cref="HostBusy"/> の間だけ〔しゃべらせる〕を押せなくし、
/// 脇に 1 行出す（<see cref="ConcurrencyNotice"/>）。<b>直列の制約そのものは変えない。</b>
/// </para>
/// <para>
/// 再生は裁定 52 の作法（NAudio・<b>再生時に −16 dBFS 相当へ揃える</b>）。保存は生の wav を
/// そのまま書く＝<b>揃えるのは鳴らすときだけ</b>（檔は上流が出した物のまま）。
/// </para>
/// </summary>
public sealed class TryViewModel : ObservableObject
{
    private readonly Func<IWrapperClient?> _wrapper;
    private readonly IAudioPlayer _player;
    private readonly LauncherSettings _settings;

    private string _input = UiStrings.TrySampleInput;
    private string? _selectedVoice;
    private int _numSteps = 40;
    private double? _cfgScaleText;
    private double? _cfgScaleCaption;
    private double? _cfgScaleSpeaker;
    private string _caption = string.Empty;
    private string _seed = string.Empty;
    private double? _speed;
    private string _message = string.Empty;
    private string _resultText = UiText.Missing;
    private string _detailText = UiText.Missing;
    private byte[]? _lastAudio;
    private long? _lastSeed;
    private CancellationTokenSource? _inFlight;
    private string? _serverDown;

    /// <summary>本体（読み分けちゃん2）の読み上げが走っている（決裁 130 Q4）。</summary>
    private bool _hostBusy;

    /// <summary>
    /// 直前の標本に<b>自分の射</b>が乗っていた（決裁 130 Q4・<c>v2-spec.md</c> §2-1c）。
    /// 標本は 2 秒ごとで射より遅れて届くので、射が終わった直後の 1 回ぶんだけ覚えておく。
    /// </summary>
    private bool _selfShotOnTheSample;

    public TryViewModel(Func<IWrapperClient?> wrapper, IAudioPlayer player, LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(wrapper);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(settings);

        _wrapper = wrapper;
        _player = player;
        _settings = settings;
        _numSteps = settings.LastTestNumSteps;
        _selectedVoice = settings.LastTestVoice ?? VoiceIds.Default;

        SynthesizeCommand = new AsyncRelayCommand(
            SynthesizeAsync, () => !string.IsNullOrWhiteSpace(Input) && !HostBusy);
        StopCommand = new RelayCommand(() => _player.Stop());
        ReplayCommand = new RelayCommand(Replay, () => _lastAudio is not null);

        // 自分の射が始まった／終わった回にも脇の 1 行を計り直す（決裁 130 Q4）＝
        // `AsyncRelayCommand.IsRunning` が変わると `CanExecuteChanged` が上がる。
        // ここでは**釦の再計算はしない**（属性の告知だけ）ので輪にならない。
        SynthesizeCommand.CanExecuteChanged += (_, _) => RaiseConcurrencyChanged();

        // 捕れなかった例外を握り潰さない（§20-5 ⑴）。
        SynthesizeCommand.Faulted += (_, line) => Message = Fold(UiStrings.TryFailed, line);
    }

    /// <summary>
    /// <b>1 射が 200 で返った</b>（裁定 90 Q-E2 ⑶）。
    /// <para>
    /// 束ねる側（<see cref="MainViewModel"/>）が拾って、初回取得の直後の 1 射なら
    /// 取得キャッシュを消す＝「起動の確認が通り、試し撃ちで本当に音が出た」ことを
    /// 原檔を捨ててよい合図として使う（展開に失敗している機体から原檔を奪わない）。
    /// </para>
    /// </summary>
    public event EventHandler? Succeeded;

    /// <summary>話者の候補（<see cref="VoicesViewModel"/> の一覧から流し込む）。</summary>
    public ObservableCollection<string> Voices { get; } = [VoiceIds.Default];

    /// <summary>歩数のプリセット（裁定 10＝10／40）。数値でも入れられる。</summary>
    public static IReadOnlyList<int> NumStepsPresets => SpeechRequestBuilder.NumStepsPresets;

    public AsyncRelayCommand SynthesizeCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand ReplayCommand { get; }

    /// <summary>
    /// 譲っている間の 1 行（決裁 130 Q4・<c>v2-copy.md</c> §3-3 の逐語）。
    /// <para>
    /// <b>常設をやめた</b>＝v1.1.0 までは「合成はサーバ 1 プロセスにつき 1 本ずつ…」の
    /// 注記が橙の枠で出っぱなしだった（合成の直列＝launcher/README §4 ⑧）。いまは
    /// <b>本体からの要求が走っている間だけ</b>出す＝原則 6 の ⑴何が起きたか ⑵なぜか
    /// ⑶次の 1 手（＝待つ）が 1 文に揃っている。
    /// </para>
    /// </summary>
    public static string ConcurrencyNotice =>
        "いま読み分けちゃん2 の読み上げに使われています。終わってからお試しください。";

    /// <summary>
    /// 手が落ちた・書けなかったときの 1 行（<b>純関数</b>）＝<b>詳しい字は檔へ</b>。
    /// 画面には利用者の言葉 1 文と〔ログを開く〕の案内だけを出す（憲章 原則 6 の ⑵⑶）。
    /// 元の 1 行は捨てず、記録に残す側（<c>MainViewModel.AppendLog</c>）が持つ。
    /// </summary>
    public static string Fold(string headline, string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? headline : headline + UiStrings.SeeLog;

    /// <summary>
    /// 本体（読み分けちゃん2）が読み上げに使っている
    /// （<c>/ywk/status.requests.in_flight &gt; 0</c>＝契約 ⑹・決裁 130 Q4）。
    /// <para>
    /// 流し込むのは<b>2 秒ごとの既存の見張りの標本</b>（<see cref="MainViewModel.ApplyStatusSample"/>）
    /// だけ＝この画面は新しい問い合わせを 1 本も足さない。<b>欄が無い個体では常に偽</b>
    /// （古い wrapper を「使用中」と読まない）。
    /// </para>
    /// <para>
    /// <b>錠ではなく案内</b>＝標本の隙（最大 2 秒）に押せてしまう射は残るが、1 プロセス
    /// 1 合成の直列（契約 ⑶ 3-4）は変えていないので、すり抜けても読み上げが少し待たされるだけ。
    /// </para>
    /// <para>
    /// <b>持つのは濾した値</b>＝<b>自分の射が乗った標本は偽に落とす</b>ので、<c>set</c> した値と
    /// <c>get</c> で返る値は一致しないことがある（下の setter の註）。
    /// </para>
    /// </summary>
    public bool HostBusy
    {
        get => _hostBusy;
        set
        {
            // 自分の射が乗った標本を「本体が使っている」と読まない（決裁 130 Q4・v2-spec §2-1c）。
            // ランチャ自身の〔しゃべらせる〕も同じ POST /v1/audio/speech を撃って
            // wrapper の同じ数に乗る＝濾さないと、**自分で撃つたび**に射の終わりから
            // 次の標本までの最大 2 秒、釦が死んで橙の枠に嘘の 1 行が出る。
            // 標本は射より遅れて届くので、射の直後の 1 回ぶんも自分の分として引く。
            var mine = _selfShotOnTheSample || SynthesizeCommand.IsRunning;
            _selfShotOnTheSample = SynthesizeCommand.IsRunning;
            if (SetProperty(ref _hostBusy, value && !mine))
            {
                SynthesizeCommand.RaiseCanExecuteChanged();
                RaiseConcurrencyChanged();
            }
        }
    }

    /// <summary>
    /// 脇の 1 行を出すか（<b>自分の射の間は出さない</b>）。
    /// <para>
    /// 自分の〔しゃべらせる〕も同じ <c>POST /v1/audio/speech</c> を撃って同じ数に乗る
    /// （wrapper は発信元を区別しない）ので、<b>自分が走っていないこと</b>が「本体が使っている」
    /// と言い切れる唯一確実な手である（<c>v2-spec.md</c> §2-1c）。
    /// </para>
    /// </summary>
    public bool ShowConcurrency => HostBusy && !SynthesizeCommand.IsRunning;

    /// <summary>橙の枠の本文（出さない回は空＝<c>TryConcurrencyText</c> は残るが無言）。</summary>
    public string ConcurrencyText => ShowConcurrency ? ConcurrencyNotice : string.Empty;

    public string Input
    {
        get => _input;
        set
        {
            if (SetProperty(ref _input, value))
            {
                RaisePropertyChanged(nameof(InputLengthText));
                SynthesizeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>本文の字数（上限 4096＝契約 ⑶ 3-1）。</summary>
    public string InputLengthText =>
        (Input?.Length ?? 0).ToString(CultureInfo.InvariantCulture)
        + " / " + SpeechRequestBuilder.MaxInputLength.ToString(CultureInfo.InvariantCulture) + " 字";

    public string? SelectedVoice
    {
        get => _selectedVoice;
        set => SetProperty(ref _selectedVoice, value);
    }

    public int NumSteps
    {
        get => _numSteps;
        set
        {
            if (SetProperty(ref _numSteps, value))
            {
                RaisePropertyChanged(nameof(NumStepsInput));
            }
        }
    }

    /// <summary>
    /// 歩数の入力欄（<b>文字列で受ける</b>）。空欄・綴り違いは<b>撃つときに 1 行で告げる</b>＝
    /// WPF の束縛の検証赤枠に頼らない（UIA 検分が読めるのは文字列だから）。
    /// </summary>
    public string NumStepsInput
    {
        get => _numSteps.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (NumericInput.TryParseOptionalInt(value, out var parsed, out _) && parsed is int steps)
            {
                NumSteps = steps;
            }

            RaisePropertyChanged();
        }
    }

    /// <summary>null＝既定に戻す（欄を出さない）。</summary>
    public double? CfgScaleText
    {
        get => _cfgScaleText;
        set
        {
            if (SetProperty(ref _cfgScaleText, value))
            {
                RaisePropertyChanged(nameof(CfgScaleTextInput));
            }
        }
    }

    /// <summary>本文の従い方の入力欄（空＝既定に戻す）。</summary>
    public string CfgScaleTextInput
    {
        get => NumericInput.Format(_cfgScaleText);
        set
        {
            if (NumericInput.TryParseOptional(value, out var parsed, out _))
            {
                CfgScaleText = parsed;
            }

            RaisePropertyChanged();
        }
    }

    /// <summary>null＝既定に戻す（既定 3.0＝裁定 33）。</summary>
    public double? CfgScaleCaption
    {
        get => _cfgScaleCaption;
        set
        {
            if (SetProperty(ref _cfgScaleCaption, value))
            {
                RaisePropertyChanged(nameof(CfgScaleCaptionInput));
            }
        }
    }

    /// <summary>演技指示の従い方の入力欄（空＝既定に戻す）。</summary>
    public string CfgScaleCaptionInput
    {
        get => NumericInput.Format(_cfgScaleCaption);
        set
        {
            if (NumericInput.TryParseOptional(value, out var parsed, out _))
            {
                CfgScaleCaption = parsed;
            }

            RaisePropertyChanged();
        }
    }

    /// <summary>null＝既定に戻す。</summary>
    public double? CfgScaleSpeaker
    {
        get => _cfgScaleSpeaker;
        set
        {
            if (SetProperty(ref _cfgScaleSpeaker, value))
            {
                RaisePropertyChanged(nameof(CfgScaleSpeakerInput));
            }
        }
    }

    /// <summary>話者性の従い方の入力欄（空＝既定に戻す）。</summary>
    public string CfgScaleSpeakerInput
    {
        get => NumericInput.Format(_cfgScaleSpeaker);
        set
        {
            if (NumericInput.TryParseOptional(value, out var parsed, out _))
            {
                CfgScaleSpeaker = parsed;
            }

            RaisePropertyChanged();
        }
    }

    /// <summary>演技指示。空文字・空白のみ＝未指定（裁定 48）。</summary>
    public string Caption
    {
        get => _caption;
        set => SetProperty(ref _caption, value);
    }

    /// <summary>乱数の種。空＝未指定（毎回変わる）。</summary>
    public string Seed
    {
        get => _seed;
        set => SetProperty(ref _seed, value);
    }

    /// <summary>読み速さ。null＝既定（<c>irodori.duration_scale</c> とは併用しない）。</summary>
    public double? Speed
    {
        get => _speed;
        set
        {
            if (SetProperty(ref _speed, value))
            {
                RaisePropertyChanged(nameof(SpeedInput));
            }
        }
    }

    /// <summary>読み速さの入力欄（空＝既定に戻す）。</summary>
    public string SpeedInput
    {
        get => NumericInput.Format(_speed);
        set
        {
            if (NumericInput.TryParseOptional(value, out var parsed, out _))
            {
                Speed = parsed;
            }

            RaisePropertyChanged();
        }
    }

    /// <summary>画面下の 1 行（理由・結果）。</summary>
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>所要 ms・出力秒・RTF・seed の 1 行。</summary>
    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    /// <summary>
    /// 所要 ms・RTF・seed の内訳（<b>詳細（上級者向け）の中にだけ出す</b>＝
    /// 憲章 §6-1 は RTF と ms の<b>置き場</b>を詳細に限る）。
    /// </summary>
    public string DetailText
    {
        get => _detailText;
        private set => SetProperty(ref _detailText, value);
    }

    /// <summary>
    /// 撃ち終わりの 1 文（<b>純関数</b>・`v2-copy.md` §1-8 の :393-396 の逐語）＝
    /// 「<b>3.3 秒の音を 1.4 秒で作りました。</b>」。<b>ms も RTF も seed も出さない。</b>
    /// </summary>
    public static string Outcome(double elapsedMs, double seconds) =>
        seconds.ToString("0.0", CultureInfo.InvariantCulture) + " 秒の音を "
        + (elapsedMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " 秒で作りました。";

    /// <summary>保存できる音を持っているか。</summary>
    public bool HasAudio => _lastAudio is not null;

    /// <summary>直前の射の <c>X-Irodori-Seed</c>（明示しなければ 1 本目のチャンクの値）。</summary>
    public long? LastSeed => _lastSeed;

    /// <summary>話者の候補を入れ替える（一覧が変わったら窓が呼ぶ）。</summary>
    public void SetVoices(IReadOnlyList<VoiceRow>? rows)
    {
        var keep = SelectedVoice;
        Voices.Clear();
        if (rows is null || rows.Count == 0)
        {
            Voices.Add(VoiceIds.Default);
        }
        else
        {
            foreach (var row in rows)
            {
                Voices.Add(row.Id);
            }
        }

        SelectedVoice = keep is not null && Voices.Contains(keep) ? keep : Voices[0];
    }

    /// <summary>いまの欄から body を組む（画面の「撃つ前の検分」＝純関数を呼ぶだけ）。</summary>
    public TryShotBuildResult BuildRequest() => SpeechRequestBuilder.Build(new TryShot(
        Input,
        SelectedVoice,
        NumSteps,
        CfgScaleText,
        CfgScaleCaption,
        CfgScaleSpeaker,
        Caption,
        Seed,
        Speed));

    /// <summary>脇の 1 行（出す・出さない・本文）を計り直す（決裁 130 Q4）。</summary>
    private void RaiseConcurrencyChanged()
    {
        RaisePropertyChanged(nameof(ShowConcurrency));
        RaisePropertyChanged(nameof(ConcurrencyText));
    }

    /// <summary>撃って鳴らす。</summary>
    public async Task SynthesizeAsync()
    {
        var built = BuildRequest();
        if (!built.Ok || built.Request is null)
        {
            Message = built.FailureReason ?? UiStrings.TryCannotSpeak;
            return;
        }

        var client = _wrapper();
        if (client is null)
        {
            Message = UiStrings.TryNotReady;
            return;
        }

        Message = UiStrings.TryWorking;
        _serverDown = null;
        var watch = Stopwatch.StartNew();
        SpeechResult result;

        // 裁定 88 ⑶＝**サーバの子が消えたら HTTP の期限を待たない**。
        // 見張り（状態機械）が Failed を告げたら NotifyServerFailed がこの札を切る。
        using var cts = new CancellationTokenSource();
        _inFlight = cts;
        try
        {
            // 読込中に撃つと待たされて 200 が返る（契約 ⑸）＝期限は ready 待ちを飲み込む値にする。
            result = await client
                .SynthesizeAsync(built.Request, _settings.EffectiveReadyTimeout(), cts.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 落ちたのなら理由 1 行、そうでなければ期限切れ（黙って同じ文言にしない）。
            Message = _serverDown ?? UiStrings.TryTimedOut;
            return;
        }
        finally
        {
            _inFlight = null;
        }

        watch.Stop();

        if (!result.Ok || result.Audio is null || result.Audio.Length == 0)
        {
            Message = SpeechRequestBuilder.DescribeError(result.Error, result.StatusCode);
            return;
        }

        _lastAudio = result.Audio;
        _lastSeed = result.Seed;
        RaisePropertyChanged(nameof(HasAudio));
        RaisePropertyChanged(nameof(LastSeed));
        ReplayCommand.RaiseCanExecuteChanged();

        var seconds = WavInfo.DurationSeconds(result.Audio);
        var elapsedMs = result.Elapsed > TimeSpan.Zero
            ? result.Elapsed.TotalMilliseconds
            : watch.Elapsed.TotalMilliseconds;

        // 帯の外に出すのは「何秒の音を何秒で作ったか」の 1 文だけ（`v2-copy.md` §1-8 の :393-396）。
        // ms・RTF・seed は**詳細（上級者向け）の中**にだけ置く（憲章 §6-1）。
        ResultText = Outcome(elapsedMs, seconds ?? 0);
        DetailText = "所要 " + UiText.Milliseconds(elapsedMs)
            + "／出力 " + UiText.Seconds(seconds)
            + "／" + UiText.RealTimeFactor(elapsedMs, seconds)
            + "／seed " + (result.Seed?.ToString(CultureInfo.InvariantCulture) ?? UiText.Missing);

        // 撃った条件を覚えておく（次に開いたときの初期値）
        _settings.LastTestVoice = SelectedVoice;
        _settings.LastTestNumSteps = NumSteps;

        var played = _player.Play(result.Audio);
        Message = played.Ok ? UiStrings.TryPlaying : played.FailureReason ?? UiStrings.TryPlayFailed;

        // 200 が返って音になった＝原檔を捨ててよい合図（再生の可否には掛けない＝
        // 音が出ないのは機体の音源の話で、実行系が組み上がった事実は変わらない）。
        Succeeded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// サーバの子が消えた（状態機械が <c>Failed</c> を告げた＝裁定 88 ⑶）。
    /// <para>
    /// <b>走っている HTTP をその場で切る</b>＝合成中にプロセスが 0xC0000005 で消える形
    /// （裁定 83）では、上流の応答も切断も返らないまま ready 待ちの期限（既定 120 s）まで
    /// 画面が「合成しています…」を出し続ける。<b>期限を待たずに理由 1 行を出す</b>。
    /// </para>
    /// </summary>
    public void NotifyServerFailed(int? exitCode, string? reason)
    {
        _serverDown = DescribeServerDown(exitCode, reason);
        Message = _serverDown;

        // 走っている射があれば切る（無ければ札だけ置く）。
        try
        {
            _inFlight?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 直前に終わっていた＝切る物が無い
        }
    }

    /// <summary>
    /// サーバが落ちたときの 1 行（<b>純関数</b>＝裁定 88 ⑶ の逐語）。
    /// 終了コードが取れていれば必ず出す（0xC0000005＝−1073741819 が見分けの手がかり）。
    /// </summary>
    public static string DescribeServerDown(int? exitCode, string? reason)
    {
        // 終了コードと内部の 1 行は**記録の側**に残る（帯の ⑵ が終了コードを出す＝`BandContext.ExitCode`）。
        // ここは画面に出す 1 文なので、利用者の言葉だけを綴る（憲章 原則 6）。
        _ = exitCode;
        _ = reason;
        return UiStrings.TryServerDown;
    }

    /// <summary>直前の音をもう一度鳴らす。</summary>
    public void Replay()
    {
        if (_lastAudio is null)
        {
            return;
        }

        var played = _player.Play(_lastAudio);
        Message = played.Ok ? UiStrings.TryPlaying : played.FailureReason ?? UiStrings.TryPlayFailed;
    }

    /// <summary>直前の音を檔に書く（<b>揃えない生の wav</b>＝上流が出した物のまま）。</summary>
    public bool Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_lastAudio is null)
        {
            Message = UiStrings.TryNothingToSave;
            return false;
        }

        try
        {
            File.WriteAllBytes(path, _lastAudio);
            Message = UiStrings.TrySaved;
            return true;
        }
        catch (IOException ex)
        {
            Message = Fold(UiStrings.TrySaveFailed, ex.Message);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Message = Fold(UiStrings.TrySaveFailed, ex.Message);
            return false;
        }
    }

    /// <summary>保存の既定の檔名（話者と時刻）。</summary>
    public string SuggestedFileName()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return "irodori-" + stamp + ".wav";
    }
}
