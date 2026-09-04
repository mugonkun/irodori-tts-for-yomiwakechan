using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>
/// <c>%LOCALAPPDATA%\irodori-tts-ywk\settings.json</c> の中身（契約 ⑻）。
/// <para>
/// <b>GPU は UUID で持つ</b>（裁定 34・契約 ⑹）＝index は再起動で変わりうるので、起動のたびに
/// <see cref="GpuResolver"/> で index へ解決する。見つからなければ「前回の GPU が見つからない」と
/// 告げて選び直させる（設計書 §3）。
/// </para>
/// <para>
/// null が「未設定」を意味する欄が 3 つある＝<see cref="Precision"/>（device 連動に任せる）・
/// <see cref="PrecomputeOnStart"/>（変種の既定に任せる＝裁定 65）・<see cref="HfHome"/>
/// （<c>&lt;data&gt;\models</c> に任せる）。「既定に戻す」は値を書かないことで表す。
/// </para>
/// <para>
/// WPF の束縛から書き換えるので <b>可変の POCO</b>（record ではない）。保存は原子的
/// （<see cref="JsonSettingsStore"/>）。
/// </para>
/// </summary>
public sealed class LauncherSettings
{
    /// <summary>この檔の形。欄を足すときは上げない・意味を変えるときだけ上げる（契約 ⑻ の規律）。</summary>
    [JsonPropertyName("schema")]
    public int Schema { get; set; } = 1;

    /// <summary>選んだ GPU の UUID（<c>GPU-xxxxxxxx-...</c>。CPU 変種なら null）。</summary>
    [JsonPropertyName("gpuUuid")]
    public string? GpuUuid { get; set; }

    /// <summary>選んだ GPU の名前（見つからないときの告知に使うだけ＝同定は UUID）。</summary>
    [JsonPropertyName("gpuName")]
    public string? GpuName { get; set; }

    /// <summary>取得台帳の変種名（<see cref="RuntimeVariants"/>）。<c>YWK_VARIANT</c> ではない。</summary>
    [JsonPropertyName("variant")]
    public string Variant { get; set; } = RuntimeVariants.Cu130;

    /// <summary>上級者設定。null＝device 連動（裁定 7）に任せる。</summary>
    [JsonPropertyName("precision")]
    public string? Precision { get; set; }

    /// <summary>既定 18088（裁定 2）。塞がっていたら止まって告知する（裁定 52＝次を探さない）。</summary>
    [JsonPropertyName("port")]
    public int Port { get; set; } = DefaultPort;

    /// <summary>利用者データの根の上書き。null＝<c>%LOCALAPPDATA%\irodori-tts-ywk</c>。</summary>
    [JsonPropertyName("dataDir")]
    public string? DataDir { get; set; }

    /// <summary><c>HF_HOME</c> の上書き。null＝<c>&lt;data&gt;\models</c>。</summary>
    [JsonPropertyName("hfHome")]
    public string? HfHome { get; set; }

    /// <summary>ready 直後に暖機を撃つか（Radeon 版は既定 ON＝設計書 §5）。</summary>
    [JsonPropertyName("warmup")]
    public bool WarmupOnStart { get; set; }

    /// <summary>暖機の段（秒・契約 ⑺ 7-2 の既定 <c>[4,8,12]</c>）。</summary>
    [JsonPropertyName("warmupStages")]
    public IList<double> WarmupStages { get; set; } = [4, 8, 12];

    /// <summary>暖機で撃つ話者（空＝段だけ）。最近使った 1〜3 名を入れる想定（設計書 §5）。</summary>
    [JsonPropertyName("warmupVoices")]
    public IList<string> WarmupVoices { get; set; } = [];

    /// <summary>暖機の本文。null＝契約の既定「暖機です。」に任せる。</summary>
    [JsonPropertyName("warmupText")]
    public string? WarmupText { get; set; }

    /// <summary>参照潜在の事前計算（裁定 65・67）。null＝変種の既定（<c>rocm-*</c> は ON）。</summary>
    [JsonPropertyName("precompute")]
    public bool? PrecomputeOnStart { get; set; }

    /// <summary>裁定 69＝設定項目・初期値 0（＝上流の <c>empty_cache_interval</c> をそのまま渡す）。</summary>
    [JsonPropertyName("emptyCacheInterval")]
    public int EmptyCacheInterval { get; set; }

    /// <summary>UI の拡大率。</summary>
    [JsonPropertyName("uiScale")]
    public double UiScale { get; set; } = 1.0;

    /// <summary>話者一覧の並び（id の列。ここに無い話者は後ろに付く。「デフォルト」は常に先頭）。</summary>
    [JsonPropertyName("voiceOrder")]
    public IList<string> VoiceOrder { get; set; } = [];

    /// <summary>ready 待ち（秒）。0 以下＝変種の既定（契約 ⑵＝120 s・CPU は 300 s）。</summary>
    [JsonPropertyName("readyTimeoutSeconds")]
    public int ReadyTimeoutSeconds { get; set; }

    /// <summary>初回取得ウィザードを通したか。</summary>
    [JsonPropertyName("firstRunCompleted")]
    public bool FirstRunCompleted { get; set; }

    /// <summary>同意した通知文の sha256（<c>licenses/first-run-notices.md</c>・裁定 46）。</summary>
    [JsonPropertyName("acceptedNoticesSha256")]
    public string? AcceptedNoticesSha256 { get; set; }

    /// <summary>ランチャの起動と同時にサーバを起こすか。</summary>
    [JsonPropertyName("autoStartServer")]
    public bool AutoStartServer { get; set; } = true;

    /// <summary>GPU メモリの常時表示（裁定 67 ⑶）。</summary>
    [JsonPropertyName("showMemoryPanel")]
    public bool ShowMemoryPanel { get; set; } = true;

    /// <summary>試し撃ちで最後に使った話者。</summary>
    [JsonPropertyName("lastTestVoice")]
    public string? LastTestVoice { get; set; }

    /// <summary>試し撃ちの steps（UI プリセット 10／40＝裁定 10）。</summary>
    [JsonPropertyName("lastTestNumSteps")]
    public int LastTestNumSteps { get; set; } = 40;

    /// <summary>既定ポート（裁定 2）。本体の接続先と同じ値でなければならない。</summary>
    public const int DefaultPort = 18088;

    /// <summary>解決済みの ready 待ち（0 以下なら変種の既定）。</summary>
    public TimeSpan EffectiveReadyTimeout() => ReadyTimeoutSeconds > 0
        ? TimeSpan.FromSeconds(ReadyTimeoutSeconds)
        : RuntimeVariants.ReadyTimeoutDefault(Variant);

    /// <summary>解決済みの事前計算の可否（null なら変種の既定＝裁定 65）。</summary>
    public bool EffectivePrecomputeOnStart() =>
        PrecomputeOnStart ?? RuntimeVariants.PrecomputeOnStartDefault(Variant);

    /// <summary>
    /// 解決済みの精度。Radeon 変種は利用者の指定を無視して bf16（裁定 5・36＝載せると exit 2）。
    /// null を返したときは env に精度を<b>載せない</b>＝wrapper の device 連動に任せる。
    /// </summary>
    public string? EffectivePrecision()
    {
        if (RuntimeVariants.IsRocm(Variant))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(Precision) ? null : Precision.Trim();
    }

    /// <summary>丸ごとの写し（設定画面の「取り消し」用）。</summary>
    public LauncherSettings Clone() => new()
    {
        Schema = Schema,
        GpuUuid = GpuUuid,
        GpuName = GpuName,
        Variant = Variant,
        Precision = Precision,
        Port = Port,
        DataDir = DataDir,
        HfHome = HfHome,
        WarmupOnStart = WarmupOnStart,
        WarmupStages = [.. WarmupStages],
        WarmupVoices = [.. WarmupVoices],
        WarmupText = WarmupText,
        PrecomputeOnStart = PrecomputeOnStart,
        EmptyCacheInterval = EmptyCacheInterval,
        UiScale = UiScale,
        VoiceOrder = [.. VoiceOrder],
        ReadyTimeoutSeconds = ReadyTimeoutSeconds,
        FirstRunCompleted = FirstRunCompleted,
        AcceptedNoticesSha256 = AcceptedNoticesSha256,
        AutoStartServer = AutoStartServer,
        ShowMemoryPanel = ShowMemoryPanel,
        LastTestVoice = LastTestVoice,
        LastTestNumSteps = LastTestNumSteps,
    };
}
