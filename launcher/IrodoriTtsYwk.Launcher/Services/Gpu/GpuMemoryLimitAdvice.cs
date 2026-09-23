using System;
using System.Collections.Generic;
using System.Globalization;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Gpu;

/// <summary>
/// 「GPU メモリの上限」の<b>おすすめ</b>を決める（<b>純関数</b>・裁定 160＝司令官の指示 2026-09-24
/// 「vram は初期設定と再スキャンで最適な占有メモリを提示していいかもね。」）。
/// <para>
/// <b>材料は専用の GPU メモリ 1 本だけ</b>（司令官の指示 2026-09-24＝「共有メモリは判定の材料に
/// 使わないでね。」＝裁定 110 D2 と同じ線）。数えるのは <see cref="DxgiGpuAdapters"/> が
/// <c>DXGI_ADAPTER_DESC1.DedicatedVideoMemory</c> から採った値で、
/// <c>SharedSystemMemory</c> は読まない・足さない。
/// </para>
/// <para>
/// <b><see cref="GpuInfo.TotalMemoryBytes"/> は AMD では使えない</b>＝NVIDIA 機では
/// <c>nvidia-smi</c> の専用メモリだが、AMD 機では torch の <c>total_memory</c>＝
/// <b>専用＋共有</b>である（本機の gfx1151 は 107 GB と名乗る。DXGI は同じ板を
/// 68,535,640,064 B＝63.83 GiB と数える）。だから
/// <list type="number">
/// <item>DXGI に<b>同じ名前</b>の板が在ればその専用メモリ（同名が複数でも<b>値が揃っていれば</b>使う）</item>
/// <item>同名の板の値が<b>食い違う</b>なら<b>おすすめを出さない</b>（どれの話か決められない）</item>
/// <item>DXGI に合う板が無い回は<b>NVIDIA だけ</b> <see cref="GpuInfo.TotalMemoryBytes"/> に落ちる</item>
/// <item>それ以外（AMD・素性の判らない名）は<b>おすすめを出さない</b></item>
/// </list>
/// の 4 手に限る。<b>ほかの控えは作らない。</b>
/// </para>
/// <para>
/// <b>おすすめの規則</b>（<see cref="Recommend"/>）＝
/// <list type="bullet">
/// <item>CPU で動かす回・専用メモリが判らない回＝<b>0（制限しない）</b>。</item>
/// <item>専用メモリが <see cref="NoLimitFromGiB"/> GiB 以上＝<b>0</b>。24 GB 級の板と、
/// メモリを本体と分け合う APU には上限が要らない（このアプリ自身が v2.0.7 で 4.7 GiB 前後に
/// 収まるため＝リリース文 v2.0.7）。</item>
/// <item>それ以外＝<b>専用メモリの <see cref="Share"/>（65 %）を切り捨て</b>、
/// <see cref="LauncherSettings.MinGpuMemoryLimitGiB"/>（4）〜<see cref="MaxGiB"/> に丸める。
/// 例＝6 GB→4・8 GB→5・10 GB→6・12 GB→7・16 GB→10。</item>
/// </list>
/// </para>
/// <para>
/// <b>数の根拠</b>（裁定 160 の実測）＝模型そのものは 1.9 GiB 前後を握り、24 秒の読み上げで
/// 3.85 GiB まで伸びる。3 GiB では読み込みの山（3.09 GiB）で落ちるので<b>下限は 4</b>。
/// 8 GB の板で 65 % に置くと 35 % 前後が配信ソフトとゲームに残る＝8 GB の利用者が
/// 「8 時間の配信で 99 % まで埋まる」と報せてきた形（v2.0.7 以前）を作らない。
/// </para>
/// </summary>
public static class GpuMemoryLimitAdvice
{
    /// <summary>1 GiB のバイト数。</summary>
    public const long GibiByte = 1024L * 1024 * 1024;

    /// <summary>この量（GiB）以上の専用メモリなら上限は要らない＝0 を勧める。</summary>
    public const int NoLimitFromGiB = 20;

    /// <summary>勧める取り分（残りは配信ソフトとゲームに残す）。</summary>
    public const double Share = 0.65;

    /// <summary>上限に入れられる最大（<c>JsonSettingsStore.Sanitize</c> と同じ窓）。</summary>
    public const int MaxGiB = 1024;

    /// <summary>
    /// この GPU の<b>専用</b>メモリ（バイト・判らなければ null＝おすすめを出さない）。
    /// <para>
    /// 照合は<b>名前</b>（前後の空白を落とし、大小を無視する）＝
    /// <see cref="GpuInfo"/> は UUID・DXGI は LUID で、同定の鍵を共有していないためである。
    /// </para>
    /// </summary>
    /// <param name="gpu">選んでいる GPU（null＝選んでいない）。</param>
    /// <param name="adapters">OS のアダプタ一覧（<see cref="DxgiGpuAdapters"/>・null／空＝数えていない）。</param>
    public static long? DedicatedBytes(GpuInfo? gpu, IReadOnlyList<GpuAdapterInfo>? adapters)
    {
        if (gpu is null)
        {
            return null;
        }

        var name = gpu.Name?.Trim();
        long? matched = null;
        if (!string.IsNullOrEmpty(name) && adapters is not null)
        {
            foreach (var adapter in adapters)
            {
                if (!string.Equals(adapter.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (matched is null)
                {
                    matched = adapter.DedicatedVideoMemoryBytes;
                }
                else if (matched.Value != adapter.DedicatedVideoMemoryBytes)
                {
                    // 同じ名前の板が違う量を名乗る＝どれの話か決められない（勝手に決めない）。
                    return null;
                }
            }
        }

        if (matched is > 0)
        {
            return matched;
        }

        // **控えは NVIDIA だけ**＝`nvidia-smi` の総量は専用メモリである。
        // AMD の torch の総量は共有ぶんが混ざるので、ここには落とさない。
        return GpuVendors.IsNvidia(name) && gpu.TotalMemoryBytes > 0 ? gpu.TotalMemoryBytes : null;
    }

    /// <summary>
    /// おすすめの上限（GiB・<b>0＝上限は要らない</b>）。この class の要約の規則をそのまま実装する。
    /// </summary>
    /// <param name="dedicatedBytes">専用メモリ（<see cref="DedicatedBytes"/>・null＝判らない）。</param>
    /// <param name="variant">いまの動かし方（CPU なら常に 0）。</param>
    public static int Recommend(long? dedicatedBytes, string? variant)
    {
        if (RuntimeVariants.IsCpu(variant) || dedicatedBytes is not > 0)
        {
            return 0;
        }

        var gib = dedicatedBytes.Value / (double)GibiByte;
        if (gib >= NoLimitFromGiB)
        {
            return 0;
        }

        return Math.Clamp(
            (int)Math.Floor(gib * Share), LauncherSettings.MinGpuMemoryLimitGiB, MaxGiB);
    }

    /// <summary>
    /// 画面に出す GB の数（<b>いちばん近い整数</b>＝63.83 GiB は 64・7.99 GiB は 8）。
    /// 単位の語は「GB」である（憲章 §6-1＝GiB は隠す語・<c>WordLintTests</c>）。
    /// </summary>
    public static int Gigabytes(long bytes) =>
        (int)Math.Round(bytes / (double)GibiByte, MidpointRounding.AwayFromZero);

    /// <summary>
    /// おすすめの 1 行（<b>文は <c>UiStrings</c> だけから組む</b>・null＝出さない）。
    /// <para>
    /// 出さないのは ⑴ 専用メモリが判らない ⑵ CPU で動かす、の 2 つである。
    /// いまの値が既におすすめと同じ回は<b>そう言う</b>（釦は押せなくなる）。
    /// </para>
    /// </summary>
    /// <param name="dedicatedBytes">専用メモリ（<see cref="DedicatedBytes"/>）。</param>
    /// <param name="variant">いまの動かし方。</param>
    /// <param name="currentGiB">いま入っている上限（写しの値）。</param>
    public static string? Line(long? dedicatedBytes, string? variant, int currentGiB)
    {
        if (dedicatedBytes is not > 0 || RuntimeVariants.IsCpu(variant))
        {
            return null;
        }

        var gigabytes = Gigabytes(dedicatedBytes.Value);
        var recommended = Recommend(dedicatedBytes, variant);
        if (recommended <= 0)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                ViewModels.UiStrings.SettingsGpuMemoryLimitAdviceNoneFormat,
                gigabytes);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            currentGiB == recommended
                ? ViewModels.UiStrings.SettingsGpuMemoryLimitAdviceSameFormat
                : ViewModels.UiStrings.SettingsGpuMemoryLimitAdviceFormat,
            gigabytes,
            recommended);
    }
}
