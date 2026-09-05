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
/// 配信中にここで撃つと本体の読み上げが待たされる＝画面に注記を出す。
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

    private string _input = "こんにちは。読み分けちゃん用の irodori-TTS です。";
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
    private byte[]? _lastAudio;
    private long? _lastSeed;
    private CancellationTokenSource? _inFlight;
    private string? _serverDown;

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

        SynthesizeCommand = new AsyncRelayCommand(SynthesizeAsync, () => !string.IsNullOrWhiteSpace(Input));
        StopCommand = new RelayCommand(() => _player.Stop());
        ReplayCommand = new RelayCommand(Replay, () => _lastAudio is not null);

        // 捕れなかった例外を握り潰さない（§20-5 ⑴）。
        SynthesizeCommand.Faulted += (_, line) => Message = "合成の手が落ちました：" + line;
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

    /// <summary>合成の直列（1 本）についての注記（launcher/README §4 ⑧）。</summary>
    public static string ConcurrencyNotice =>
        "合成はサーバ 1 プロセスにつき 1 本ずつ走ります。配信中にここで撃つと、本体の読み上げがその分だけ待たされます。";

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

    /// <summary>撃って鳴らす。</summary>
    public async Task SynthesizeAsync()
    {
        var built = BuildRequest();
        if (!built.Ok || built.Request is null)
        {
            Message = built.FailureReason ?? "撃てません。";
            return;
        }

        var client = _wrapper();
        if (client is null)
        {
            Message = "サーバが動いていません（先に「サーバ起動」を押してください）。";
            return;
        }

        Message = "合成しています…";
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
            Message = _serverDown ?? "合成が期限内に終わりませんでした。";
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

        ResultText = "所要 " + UiText.Milliseconds(elapsedMs)
            + "／出力 " + UiText.Seconds(seconds)
            + "／" + UiText.RealTimeFactor(elapsedMs, seconds)
            + "／seed " + (result.Seed?.ToString(CultureInfo.InvariantCulture) ?? UiText.Missing);

        // 撃った条件を覚えておく（次に開いたときの初期値）
        _settings.LastTestVoice = SelectedVoice;
        _settings.LastTestNumSteps = NumSteps;

        var played = _player.Play(result.Audio);
        Message = played.Ok
            ? "再生中（音量を −16 dBFS 相当に揃えています）。"
            : played.FailureReason ?? "再生できませんでした。";

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
        var head = exitCode is int code
            ? "サーバが落ちました（exit " + code.ToString(CultureInfo.InvariantCulture) + "）。"
            : "サーバが落ちました。";

        return string.IsNullOrWhiteSpace(reason) ? head : head + " " + reason.Trim();
    }

    /// <summary>直前の音をもう一度鳴らす。</summary>
    public void Replay()
    {
        if (_lastAudio is null)
        {
            return;
        }

        var played = _player.Play(_lastAudio);
        Message = played.Ok ? "再生中。" : played.FailureReason ?? "再生できませんでした。";
    }

    /// <summary>直前の音を檔に書く（<b>揃えない生の wav</b>＝上流が出した物のまま）。</summary>
    public bool Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_lastAudio is null)
        {
            Message = "保存する音がありません。";
            return false;
        }

        try
        {
            File.WriteAllBytes(path, _lastAudio);
            Message = "保存しました（" + UiText.Bytes(_lastAudio.LongLength) + "）。";
            return true;
        }
        catch (IOException ex)
        {
            Message = "保存できませんでした：" + ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Message = "保存できませんでした：" + ex.Message;
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
