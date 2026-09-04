using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>
/// wrapper に載せる env を組む（設計書 §2・<b>純関数</b>）。
/// <para>
/// <b>ランチャが載せるのは差分だけ</b>＝<c>server/ywk_server.py</c> の <c>apply_env_defaults</c> が
/// <c>setdefault</c> で焼いている物（<c>IRODORI_HF_CHECKPOINT</c>・<c>IRODORI_PRELOAD</c>・
/// <c>IRODORI_ALLOW_NO_REF_VOICE</c>・<c>IRODORI_DEFAULT_VOICE</c>・<c>IRODORI_DEFAULT_NUM_STEPS</c>
/// ・<c>IRODORI_DEFAULT_RESPONSE_FORMAT</c>）は<b>載せ直さない</b>。二重定義を作ると、
/// 上流の既定が動いた日にどちらが正か分からなくなる。
/// </para>
/// <para>
/// <b>載せる物</b>＝⑴ 場所（<c>YWK_DATA_DIR</c>・<c>HF_HOME</c>・<c>IRODORI_VOICES_DIR</c>・
/// <c>IRODORI_VOICE_ALIASES_FILE</c>）⑵ 変種と device（<c>YWK_VARIANT</c>・
/// <c>IRODORI_MODEL_DEVICE</c>／<c>IRODORI_CODEC_DEVICE</c>＝<b>2 本に同時に載る</b>＝受け入れ条件 D-2）
/// ⑶ 上級者設定（精度・<c>empty_cache_interval</c>）⑷ 暖機・事前計算 ⑸ Python の作法。
/// </para>
/// <para>
/// <b>device は要求 JSON には載らない</b>（契約 ⑹）＝1 プロセス 1 デバイス。ここで決めた物が
/// 本体の読み上げにも暗黙に効く（裁定 15）。
/// </para>
/// </summary>
public static class ServerEnvironment
{
    public const string ModelDevice = "IRODORI_MODEL_DEVICE";
    public const string CodecDevice = "IRODORI_CODEC_DEVICE";
    public const string ModelPrecision = "IRODORI_MODEL_PRECISION";
    public const string CodecPrecision = "IRODORI_CODEC_PRECISION";
    public const string VoicesDir = "IRODORI_VOICES_DIR";
    public const string VoiceAliasesFile = "IRODORI_VOICE_ALIASES_FILE";
    public const string EmptyCacheInterval = "IRODORI_EMPTY_CACHE_INTERVAL";
    public const string Host = "IRODORI_HOST";
    public const string Port = "IRODORI_PORT";
    public const string Variant = "YWK_VARIANT";
    public const string DataDir = "YWK_DATA_DIR";
    public const string WarmupOnStart = "YWK_WARMUP_ON_START";
    public const string WarmupStages = "YWK_WARMUP_STAGES";
    public const string WarmupVoices = "YWK_WARMUP_VOICES";
    public const string WarmupText = "YWK_WARMUP_TEXT";
    public const string PrecomputeOnStart = "YWK_PRECOMPUTE_ON_START";
    public const string HfHome = "HF_HOME";
    public const string HfHubOffline = "HF_HUB_OFFLINE";

    /// <summary>
    /// torch の device 番号の並べ方（<b>是正・2026-09-05</b>）。
    /// <para>
    /// <c>nvidia-smi</c> の <c>index</c> は <b>PCI バス順</b>だが、torch の既定は
    /// <c>CUDA_DEVICE_ORDER=FASTEST_FIRST</c> ＝<b>別の並び</b>である。ランチャは
    /// <c>nvidia-smi</c> の index をそのまま <c>cuda:N</c> に載せるので、2 台構成の NVIDIA 機では
    /// 同じ番号が別の個体を指しうる（この機体は Radeon 単騎なので実射では踏めない＝
    /// コードと CUDA の既定仕様からの是正）。
    /// </para>
    /// <para>
    /// <c>PCI_BUS_ID</c> を明示すれば 2 つの index 空間が揃う。<b>列挙の台本にも同じ物を載せる</b>
    /// （<c>Services/Gpu/GpuEnumerator</c>）＝torch 経路で数えた番号と起動時の番号も揃う。
    /// 揃ったことは <c>/ywk/status.device.uuid</c> と保存 UUID の突合で実測する
    /// （<c>ViewModels/StatusViewModel.DescribeGpuMismatch</c>）。
    /// </para>
    /// </summary>
    public const string CudaDeviceOrder = "CUDA_DEVICE_ORDER";

    /// <summary><see cref="CudaDeviceOrder"/> に載せる値。</summary>
    public const string PciBusIdOrder = "PCI_BUS_ID";

    /// <summary>参照なしの話者名（契約 ⑷ 4-2・裁定 16）。</summary>
    public const string DefaultVoiceId = "デフォルト";

    /// <summary>
    /// device 文字列を組む（<c>cuda:N</c>／<c>cpu</c>）。
    /// <b>UUID→index の解決は呼ぶ側</b>（<see cref="GpuResolver"/>）＝ここは形だけを作る。
    /// </summary>
    public static string DeviceString(string variant, int? gpuIndex)
    {
        if (RuntimeVariants.IsCpu(variant))
        {
            return "cpu";
        }

        var index = gpuIndex ?? 0;
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gpuIndex), gpuIndex, "GPU の index は 0 以上。");
        }

        // ROCm の torch も device type を cuda と名乗る（契約 ⑹）＝rocm 変種でも cuda:N。
        return "cuda:" + index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 差分の env を組む（**純関数**）。<paramref name="gpuIndex"/> は
    /// UUID を解決した結果（CPU 変種なら null でよい）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> Build(
        LauncherSettings settings,
        AppPaths paths,
        int? gpuIndex,
        bool offlineHuggingFace = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(paths);

        var variant = settings.Variant;
        var device = DeviceString(variant, gpuIndex);

        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Host] = "127.0.0.1",
            [Port] = settings.Port.ToString(CultureInfo.InvariantCulture),

            // 1 プロセス 1 デバイス。model と codec の 2 本に同時に載せる（受け入れ条件 D-2）。
            [ModelDevice] = device,
            [CodecDevice] = device,

            [Variant] = RuntimeVariants.ServerLabel(variant),
            [DataDir] = paths.DataDir,
            [VoicesDir] = paths.VoicesDir,
            [VoiceAliasesFile] = paths.VoicesJsonPath,
            [EmptyCacheInterval] = settings.EmptyCacheInterval.ToString(CultureInfo.InvariantCulture),
            [HfHome] = string.IsNullOrWhiteSpace(settings.HfHome) ? paths.HfHomeDir : settings.HfHome.Trim(),

            // 取得が済んだ後は必ずオフライン（裁定 8＝取得は台帳の路 1 本）。
            [HfHubOffline] = offlineHuggingFace ? "1" : "0",

            // これらは ._pth 環境の子にも効かせる（verify-runtime.ps1 と同じ 4 本）。
            ["PYTHONUTF8"] = "1",
            ["PYTHONDONTWRITEBYTECODE"] = "1",
            ["PYTHONUNBUFFERED"] = "1",
            ["PYTHONIOENCODING"] = "utf-8",
        };

        // GPU を使う変種は index の並べ方を明示する（nvidia-smi の PCI バス順に揃える）。
        // CPU 変種には要らない（device を数えない）。
        if (RuntimeVariants.UsesGpu(variant))
        {
            env[CudaDeviceOrder] = PciBusIdOrder;
        }

        // 精度＝既定は device 連動（裁定 7）に任せて<b>載せない</b>。
        // rocm 変種は載せてはいけない（fp32 なら wrapper が exit 2＝裁定 5・36）。
        var precision = settings.EffectivePrecision();
        if (precision is not null)
        {
            env[ModelPrecision] = precision;
            env[CodecPrecision] = precision;
        }

        // 起動時暖機。ランチャは原則 POST /ywk/warmup を明示で叩くが（契約 ⑺ 7-2）、
        // 設定で ON のときは env でも撃てるようにしておく（ready 直後に 1 回）。
        if (settings.WarmupOnStart)
        {
            env[WarmupOnStart] = "1";
            if (settings.WarmupStages.Count > 0)
            {
                env[WarmupStages] = string.Join(
                    ",",
                    settings.WarmupStages.Select(s => s.ToString("0.###", CultureInfo.InvariantCulture)));
            }

            if (settings.WarmupVoices.Count > 0)
            {
                env[WarmupVoices] = string.Join(",", settings.WarmupVoices);
            }

            if (!string.IsNullOrWhiteSpace(settings.WarmupText))
            {
                env[WarmupText] = settings.WarmupText.Trim();
            }
        }

        // 事前計算＝未設定なら wrapper が変種で決める（rocm-* は 1）。明示すれば両方向に上書き。
        if (settings.PrecomputeOnStart is bool precompute)
        {
            env[PrecomputeOnStart] = precompute ? "1" : "0";
        }

        return env;
    }
}
