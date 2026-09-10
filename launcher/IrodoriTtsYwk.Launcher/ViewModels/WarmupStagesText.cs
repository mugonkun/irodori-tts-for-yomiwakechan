using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// 暖機の段（秒）を 1 行の文字列と往復させる（<b>純関数</b>・契約 ⑺ 7-2）。
/// <para>
/// 段は「その秒数の <c>no_ref</c> の射を撃って形状の db を育てる」ものなので、
/// <b>秒の並び</b>である。上限 12 段・各段は 0 より大きく 60 秒以下（契約 ⑺ 7-2 の検査と同じ）。
/// 設定画面で綴り違いのまま保存させないため、<b>保存の前にここで弾く</b>。
/// </para>
/// <para>
/// 段の秒に上限を設けない、という便 C の申し送り（裁定 60）は<b>射 1 本の所要</b>のことで、
/// 段そのものの上限（60 秒）は契約の検査である。混ぜない。
/// </para>
/// <para>
/// <b><paramref name="error"/> に返す 1 行は記録にしか出ない</b>（段 C・是正）＝
/// この設定の欄は退役し（<c>SettingsWarmupStagesBox</c>／<c>SettingsWarmupVoicesBox</c>）、
/// 値は <c>settings.json</c> を手で書いた機体でだけ変わる。だから理由は
/// <b>その鍵の名で</b>綴る（画面の語彙ではない＝憲章 §6-1 の「暖機」「話者」は出さない）。
/// <c>SettingsViewModel.Apply</c> はこの 1 行で〔適用〕を止めず、記録へ落として先へ進む。
/// </para>
/// </summary>
public static class WarmupStagesText
{
    /// <summary>段の最大数（契約 ⑺ 7-2）。</summary>
    public const int MaxStages = 12;

    /// <summary>1 段の上限（秒）。</summary>
    public const double MaxSeconds = 60.0;

    /// <summary>既定（契約 ⑺ 7-2）。</summary>
    public static readonly double[] Default = [4, 8, 12];

    /// <summary>並びを 1 行に（<c>4, 8, 12</c>）。</summary>
    public static string Format(IEnumerable<double>? stages)
    {
        if (stages is null)
        {
            return string.Empty;
        }

        return string.Join(", ", stages.Select(static s => s.ToString("0.###", CultureInfo.InvariantCulture)));
    }

    /// <summary>1 行を並びに。駄目なら <paramref name="error"/> に理由 1 行。</summary>
    public static bool TryParse(string? text, out IReadOnlyList<double> stages, out string? error)
    {
        stages = [];
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            // 空＝段を撃たない（話者だけの暖機）。これは正しい設定である。
            return true;
        }

        var parts = text.Split([',', '、', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var values = new List<double>(parts.Length);
        foreach (var part in parts)
        {
            if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                error = "settings.json の warmupStages は数（秒）をカンマで区切って書きます：「"
                    + part.Trim() + "」が読めません。";
                return false;
            }

            if (value <= 0 || value > MaxSeconds || double.IsNaN(value))
            {
                error = "settings.json の warmupStages は 0 より大きく "
                    + MaxSeconds.ToString("0", CultureInfo.InvariantCulture) + " 秒以下です。";
                return false;
            }

            values.Add(value);
        }

        if (values.Count > MaxStages)
        {
            error = "settings.json の warmupStages は "
                + MaxStages.ToString(CultureInfo.InvariantCulture) + " 個までです。";
            return false;
        }

        stages = values;
        return true;
    }

    /// <summary>先に準備しておく声の 1 行（カンマ区切り・最大 64 件＝契約 ⑺ 7-2）。</summary>
    public static bool TryParseVoices(string? text, out IReadOnlyList<string> voices, out string? error)
    {
        voices = [];
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        // 話者名は日本語で空白を含みうるので、区切りはカンマだけにする。
        var parts = text.Split([',', '、'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static p => p.Trim())
            .Where(static p => p.Length > 0)
            .ToArray();

        if (parts.Length > 64)
        {
            error = "settings.json の warmupVoices は 64 件までです。";
            return false;
        }

        voices = parts;
        return true;
    }
}
