using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// 話者の名付けの検分（<b>純関数</b>＝受け入れ条件 D-3 の「2 操作で追加」の 2 手目）。
/// <para>
/// <b>日本語名を通す</b>のが眼目である（上流の登録 API は ASCII 限定で 400 になるので使わない＝
/// 裁定 16・契約 ⑷）。檔名は内容の sha256 から作る ASCII の id になるので、名前に檔名の制約は
/// 掛からない。ただし<b>上流の話者 id は表示名そのもの</b>なので、⑴ 空 ⑵ 参照なしの別名
/// （<c>none</c> 等）⑶ 既にある名 ⑷ 制御文字は弾く。
/// </para>
/// <para>
/// <b>返す 1 行はすべて画面に出る</b>（<c>VoicesViewModel.Message</c> →
/// <c>VoicesMessageText</c>＝声を足すいちばん普通の道）。だから綴りは
/// <see cref="UiStrings"/> に置く（是正・段 C の検分）＝1 巡目はここに「話者」「参照」「檔」が
/// 直に書いてあり、`v2-copy.md` §1-8 にこの檔の行が無かったので行ごとの当て込みで拾えなかった。
/// </para>
/// </summary>
public static class VoiceNameValidator
{
    /// <summary>表示名の上限（画面と <c>voices.json</c> の鍵として現実的な長さ）。</summary>
    public const int MaxLength = 64;

    /// <summary>通れば null、駄目なら理由 1 行。</summary>
    public static string? Validate(string? name, IEnumerable<string>? existingIds)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return UiStrings.VoiceNameRequired;
        }

        if (trimmed.Length > MaxLength)
        {
            return UiStrings.VoiceNameTooLongHead
                + MaxLength.ToString(CultureInfo.InvariantCulture)
                + UiStrings.VoiceNameTooLongTail;
        }

        foreach (var c in trimmed)
        {
            if (char.IsControl(c))
            {
                return UiStrings.VoiceNameControlChar;
            }
        }

        // 「デフォルト」と参照なしの別名は、意味が二重になるので取れない（契約 ⑷ 4-2）。
        if (VoiceIds.IsNoRef(trimmed))
        {
            return "「" + trimmed + UiStrings.VoiceNameReservedTail;
        }

        if (existingIds is not null
            && existingIds.Any(id => string.Equals(id, trimmed, StringComparison.Ordinal)))
        {
            return UiStrings.VoiceNameTaken;
        }

        return null;
    }

    /// <summary>
    /// 参照に選んだ檔の検分（<b>純関数</b>）。受ける拡張子は 8 種（設計書 §4）。
    /// 長さの推奨（10〜30 s）は<b>止めない</b>＝注意として返すだけ。
    /// </summary>
    public static string? ValidateSourceFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return UiStrings.VoiceSourceRequired;
        }

        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension)
            || !VoiceIds.WavExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return UiStrings.VoiceSourceUnsupportedHead
                + string.Join("・", VoiceIds.WavExtensions)
                + UiStrings.VoiceSourceUnsupportedTail;
        }

        return null;
    }

    /// <summary>
    /// 参照 wav の長さについての注意（推奨 10〜30 s＝上流の推奨・設計書 §4）。
    /// 推奨の内なら null。<b>止めない</b>（短くても長くても合成はできる）。
    /// </summary>
    public static string? AdviseLength(double? seconds)
    {
        if (seconds is null || seconds.Value <= 0)
        {
            return null;
        }

        var min = VoiceIds.RecommendedRefMin.TotalSeconds;
        var max = VoiceIds.RecommendedRefMax.TotalSeconds;
        if (seconds.Value < min)
        {
            return UiStrings.VoiceSourceShortHead + UiText.Seconds(seconds) + "）。"
                + min.ToString("0", CultureInfo.InvariantCulture) + "〜"
                + max.ToString("0", CultureInfo.InvariantCulture)
                + UiStrings.VoiceSourceLengthTail;
        }

        if (seconds.Value > max)
        {
            return UiStrings.VoiceSourceLongHead + UiText.Seconds(seconds) + "）。"
                + min.ToString("0", CultureInfo.InvariantCulture) + "〜"
                + max.ToString("0", CultureInfo.InvariantCulture)
                + UiStrings.VoiceSourceLengthTail;
        }

        return null;
    }
}
