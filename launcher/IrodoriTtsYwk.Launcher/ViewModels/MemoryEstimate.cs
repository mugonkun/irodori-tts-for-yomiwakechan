using System;
using System.Collections.Generic;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// 裁定 67 ⑵＝<b>参照ボイスごとの消費メモリの概算</b>（すべて純関数）。
/// <list type="bullet">
/// <item><b>wav 参照＝+0.7 GB 級</b>（参照波形を毎回 encode する経路が確保する分）。</item>
/// <item><b>潜在参照（<c>ref_latent</c>）＝増えない</b>（裁定 65 の <c>.pt</c> は焼いた結果を読むだけ）。</item>
/// <item><b>参照なし（「デフォルト」）＝増えない</b>。</item>
/// <item>出力＝<b>1 フレーム ≈3.5 MB</b>。</item>
/// </list>
/// <para>
/// <b>これは概算であって実測ではない</b>（裁定 67 の係数は便 C（2）の実測で確定する）。
/// だから <see cref="IsProvisional"/> が真の間は画面に「概算」と明示する＝
/// 数字だけを出して実測のように見せない（裁定 18 の作法）。
/// </para>
/// <para>
/// <b>秒→フレームの換算率は席が知らない</b>ので <see cref="ForOutputFrames"/> は<b>フレーム数</b>を
/// 取る。秒しか手元に無いときに勝手な frame rate を掛けると、実測と桁で食い違う嘘の欄になる。
/// </para>
/// </summary>
public static class MemoryEstimate
{
    /// <summary>wav 参照 1 件（裁定 67 ⑵「+0.7 GB 級」＝0.7 GiB）。</summary>
    public const long WavReferenceBytes = 751619276L;

    /// <summary>潜在参照 1 件（焼いた <c>.pt</c> を読むだけ＝増えない）。</summary>
    public const long LatentReferenceBytes = 0L;

    /// <summary>出力 1 フレーム（裁定 67 ⑵「≈3.5 MB」＝3.5 MiB）。</summary>
    public const long OutputFrameBytes = 3670016L;

    /// <summary>係数が便 C（2）の実測で確定するまで真（画面に「概算」と出す）。</summary>
    public const bool IsProvisional = true;

    /// <summary>話者 1 件の概算（参照なし・潜在参照は 0）。</summary>
    public static long ForVoice(bool noRef, bool hasLatent) =>
        noRef || hasLatent ? LatentReferenceBytes : WavReferenceBytes;

    /// <summary><see cref="VoiceInfo"/> 1 件の概算（<c>/ywk/voices</c> の欄をそのまま読む）。</summary>
    public static long ForVoice(VoiceInfo voice)
    {
        ArgumentNullException.ThrowIfNull(voice);
        return ForVoice(voice.NoRef == true || VoiceIds.IsNoRef(voice.Id), voice.Latent == true);
    }

    /// <summary>載っている話者すべての概算（同時に何名を抱えるかの上限）。</summary>
    public static long ForVoices(IEnumerable<VoiceInfo> voices)
    {
        ArgumentNullException.ThrowIfNull(voices);
        var total = 0L;
        foreach (var voice in voices)
        {
            total += ForVoice(voice);
        }

        return total;
    }

    /// <summary>出力の概算（<b>フレーム数</b>から。秒からは換算しない）。</summary>
    public static long ForOutputFrames(int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        return frames * OutputFrameBytes;
    }

    /// <summary>話者 1 件の欄に出す 1 行。</summary>
    public static string Describe(bool noRef, bool hasLatent)
    {
        if (noRef)
        {
            return "参照なし（増えません）";
        }

        if (hasLatent)
        {
            return "潜在参照（増えません）";
        }

        return "wav 参照（概算 +" + UiText.Bytes(WavReferenceBytes) + "）";
    }

    /// <summary><see cref="VoiceInfo"/> 1 件の欄に出す 1 行。</summary>
    public static string Describe(VoiceInfo voice)
    {
        ArgumentNullException.ThrowIfNull(voice);
        return Describe(voice.NoRef == true || VoiceIds.IsNoRef(voice.Id), voice.Latent == true);
    }
}
