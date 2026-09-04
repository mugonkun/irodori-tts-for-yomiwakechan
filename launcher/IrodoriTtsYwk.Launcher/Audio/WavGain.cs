using System;
using System.Collections.Generic;

namespace IrodoriTtsYwk.Launcher.Audio;

/// <summary>
/// 再生時の音量合わせ（裁定 52＝<b>−16 dBFS 相当へ揃える</b>・裁定 38 の続き）。<b>すべて純関数</b>。
/// <para>
/// <b>なぜ再生時に揃えるのか</b>＝プリセットの檔の RMS は −16.6〜−23.8 dBFS とばらつくが、
/// 合成では上流が参照クリップごとに −16 dB へ正規化する（<c>ref_normalize_db=-16.0</c>）ので
/// 結果に効かない。差が出るのは<b>UI の試聴と試し撃ちの再生だけ</b>である（裁定 38）。
/// だから檔は 1 バイトも書き換えず、<b>鳴らすときの利得</b>だけを揃える。
/// </para>
/// <para>
/// <b>持ち上げでクリップさせない</b>＝利得は「山が 1.0 を超えない」上限で頭打ちにする。
/// 揃えるために割れさせるのは本末転倒だからである。
/// </para>
/// </summary>
public static class WavGain
{
    /// <summary>揃える先（裁定 52）。</summary>
    public const double TargetDbFs = -16.0;

    /// <summary>持ち上げの上限（+12 dB＝ほぼ無音の檔を増幅して雑音だけを鳴らさない）。</summary>
    public const double MaxBoostDb = 12.0;

    /// <summary>これより静かな檔は「無音」とみなして触らない。</summary>
    public const double SilenceDbFs = -70.0;

    /// <summary>二乗平均平方根（0〜1）。</summary>
    public static double Rms(IReadOnlyList<float> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            return 0;
        }

        double sum = 0;
        for (var i = 0; i < samples.Count; i++)
        {
            double value = samples[i];
            sum += value * value;
        }

        return Math.Sqrt(sum / samples.Count);
    }

    /// <summary>山（絶対値の最大）。</summary>
    public static double Peak(IReadOnlyList<float> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        double peak = 0;
        for (var i = 0; i < samples.Count; i++)
        {
            var value = Math.Abs((double)samples[i]);
            if (value > peak)
            {
                peak = value;
            }
        }

        return peak;
    }

    /// <summary>振幅比を dBFS に（0 は <see cref="double.NegativeInfinity"/>）。</summary>
    public static double ToDbFs(double amplitude) =>
        amplitude <= 0 ? double.NegativeInfinity : 20.0 * Math.Log10(amplitude);

    /// <summary>dB を振幅比に。</summary>
    public static double FromDb(double db) => Math.Pow(10.0, db / 20.0);

    /// <summary>
    /// 再生に掛ける利得（1.0＝そのまま）。無音・空なら 1.0。
    /// 持ち上げは <see cref="MaxBoostDb"/> まで、かつ山が 1.0 を超えないところで頭打ち。
    /// </summary>
    public static double ComputeGain(IReadOnlyList<float> samples, double targetDbFs = TargetDbFs)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var rms = Rms(samples);
        var rmsDb = ToDbFs(rms);
        if (double.IsNegativeInfinity(rmsDb) || rmsDb < SilenceDbFs)
        {
            return 1.0;
        }

        var gain = FromDb(Math.Min(targetDbFs - rmsDb, MaxBoostDb));

        var peak = Peak(samples);
        if (peak > 0 && peak * gain > 1.0)
        {
            gain = 1.0 / peak;
        }

        return gain <= 0 ? 1.0 : gain;
    }

    /// <summary>
    /// wav の生バイトから利得を出す（読めなければ 1.0）。
    /// <see cref="ComputeGain(IReadOnlyList{float}, double)"/> と名を分けるのは、
    /// <c>float[]</c> と空の collection expression で呼び分けが曖昧になるためである。
    /// </summary>
    public static double ComputeGainForWav(ReadOnlySpan<byte> wav, double targetDbFs = TargetDbFs)
    {
        var samples = WavInfo.ReadMonoSamples(wav);
        return samples.Count == 0 ? 1.0 : ComputeGain(samples, targetDbFs);
    }
}
