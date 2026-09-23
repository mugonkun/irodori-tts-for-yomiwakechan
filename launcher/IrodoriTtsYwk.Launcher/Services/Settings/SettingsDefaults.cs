using System;
using System.Collections.Generic;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;

namespace IrodoriTtsYwk.Launcher.Services.Settings;

/// <summary>
/// <b>変種で変わる既定</b>を 1 箇所に閉じる（<b>純関数</b>）。
/// <para>
/// 契約の <see cref="LauncherSettings"/> は「利用者が選んだ値」を持つ器で、
/// <c>bool</c> の欄には「未設定」が無い（<c>false</c> と区別できない）。だから
/// <b>変種を選んだ瞬間に既定を書き込む</b>形にしてある＝以後は利用者の意思がそのまま残り、
/// 「消したはずの暖機が毎回戻る」を作らない。
/// </para>
/// <para>
/// 決めごと（裁定 65・69・設計書 §5）＝
/// <list type="bullet">
/// <item><b>Radeon 版（<c>rocm-*</c>）は暖機が既定 ON</b>（話者切替の罰が 1 秒級＝<c>docs/radeon.md</c> §7-6）。</item>
/// <item><b>参照潜在の事前計算も <c>rocm-*</c> だけ既定 ON</b>（<see cref="RuntimeVariants.PrecomputeOnStartDefault"/>
/// ＝wrapper の未設定時の解決と同じ規則なので、<c>null</c> のまま置く＝二重定義を作らない）。</item>
/// <item><b><c>empty_cache_interval</c> の初期値は 0</b>（裁定 69）。</item>
/// <item><b><c>rocm-*</c> では精度を保存しない</b>（bf16 固定・fp32 を載せると wrapper が exit 2＝裁定 5・36）。</item>
/// <item><b>GPU メモリの上限は初期設定の回だけおすすめを敷く</b>（裁定 160・2026-09-24＝
/// <see cref="GpuMemoryLimitFor"/>＝専用メモリだけを見る・利用者の値は上書きしない）。</item>
/// </list>
/// </para>
/// </summary>
public static class SettingsDefaults
{
    /// <summary>暖機の既定（<c>rocm-*</c> だけ ON＝設計書 §5・便 C の実測）。</summary>
    public static bool WarmupOnStartDefault(string variant) => RuntimeVariants.IsRocm(variant);

    /// <summary>
    /// <b>変種を選んだときに走らせる</b>＝その変種の既定を書き込む（渡された物を直して返す）。
    /// 既に <paramref name="settings"/> の変種と同じなら何もしない。
    /// </summary>
    public static LauncherSettings ApplyVariant(LauncherSettings settings, string variant)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        var target = variant.Trim();
        if (string.Equals(settings.Variant, target, StringComparison.Ordinal))
        {
            return settings;
        }

        settings.Variant = target;
        settings.WarmupOnStart = WarmupOnStartDefault(target);

        // null＝変種の既定に任せる（裁定 65）。明示的に選び直したときだけ値が入る。
        settings.PrecomputeOnStart = null;

        if (!RuntimeVariants.AllowsPrecisionOverride(target))
        {
            settings.Precision = null;
        }

        if (RuntimeVariants.IsCpu(target))
        {
            // CPU 変種で GPU の UUID を握ったままにしない（device は cpu になる）
            settings.GpuUuid = null;
            settings.GpuName = null;
        }

        return settings;
    }

    /// <summary>
    /// <b>初回だけ</b>変種の既定を敷く（<see cref="LauncherSettings.FirstRunCompleted"/> が偽のとき）。
    /// 2 回目以降は利用者の意思をそのまま使う。
    /// </summary>
    public static LauncherSettings ApplyFirstRun(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.FirstRunCompleted)
        {
            return settings;
        }

        settings.WarmupOnStart = WarmupOnStartDefault(settings.Variant);
        settings.EmptyCacheInterval = 0;
        if (!RuntimeVariants.AllowsPrecisionOverride(settings.Variant))
        {
            settings.Precision = null;
        }

        return settings;
    }

    /// <summary>
    /// 選んだ GPU を書き込む（同定は UUID＝裁定 34。名前は告知にしか使わない）。
    /// <para>
    /// <b>初期設定の回だけ、GPU メモリの上限のおすすめも一緒に敷く</b>（裁定 160＝司令官の指示
    /// 2026-09-24）＝<see cref="GpuMemoryLimitFor"/> の条件に当たる回だけ値が入る
    /// （<paramref name="adapters"/> を渡さない呼び手では何も起きない）。
    /// </para>
    /// </summary>
    /// <param name="settings">直して返す設定。</param>
    /// <param name="gpu">選んだ GPU（null＝選んでいない）。</param>
    /// <param name="adapters">OS のアダプタ一覧（<c>DxgiGpuAdapters</c>・null＝数えていない）。</param>
    public static LauncherSettings ApplyGpu(
        LauncherSettings settings, GpuInfo? gpu, IReadOnlyList<GpuAdapterInfo>? adapters = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.GpuUuid = gpu?.Uuid;
        settings.GpuName = gpu?.Name;

        var limit = GpuMemoryLimitFor(settings, gpu, adapters);
        if (limit > 0)
        {
            settings.GpuMemoryLimitGiB = limit;
        }

        return settings;
    }

    /// <summary>
    /// <b>初期設定で敷く GPU メモリの上限</b>（GiB・<b>0＝敷かない</b>・<b>純関数</b>・裁定 160）。
    /// <para>
    /// 値が入るのは 3 つが揃った回だけである＝
    /// <list type="number">
    /// <item><see cref="LauncherSettings.FirstRunCompleted"/> が<b>偽</b>（＝はじめの準備の最中）</item>
    /// <item>いまの <see cref="LauncherSettings.GpuMemoryLimitGiB"/> が <b>0</b>
    /// （＝<b>利用者が入れた値には絶対に触らない</b>）</item>
    /// <item><see cref="GpuMemoryLimitAdvice.Recommend"/> が 0 より大きい
    /// （＝専用メモリが判っていて、上限が要る大きさだった）</item>
    /// </list>
    /// <b>押す手は 1 つも増えない</b>＝8 GB の板の機体は、ウィザードを通しただけで 5 GB に収まる。
    /// </para>
    /// </summary>
    public static int GpuMemoryLimitFor(
        LauncherSettings settings, GpuInfo? gpu, IReadOnlyList<GpuAdapterInfo>? adapters)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.FirstRunCompleted || settings.GpuMemoryLimitGiB != 0)
        {
            return 0;
        }

        return GpuMemoryLimitAdvice.Recommend(
            GpuMemoryLimitAdvice.DedicatedBytes(gpu, adapters), settings.Variant);
    }
}
