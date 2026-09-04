using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace IrodoriTtsYwk.Launcher.Audio;

/// <summary>
/// RIFF/WAVE の頭だけを読んだ結果（<c>/v1/audio/speech</c> の応答は <c>wav</c> 固定＝契約 ⑶ 3-2）。
/// </summary>
/// <param name="Channels">1（モノラル）。</param>
/// <param name="SampleRate">標本化周波数。</param>
/// <param name="BitsPerSample">16（PCM）または 32（IEEE float）。</param>
/// <param name="IsFloat"><c>WAVE_FORMAT_IEEE_FLOAT</c>（3）か。</param>
/// <param name="DataOffset"><c>data</c> チャンクの本体が始まる位置。</param>
/// <param name="DataLength"><c>data</c> チャンクの長さ（バイト）。</param>
public sealed record WavFormat(
    int Channels,
    int SampleRate,
    int BitsPerSample,
    bool IsFloat,
    int DataOffset,
    int DataLength)
{
    /// <summary>1 標本あたりのバイト数（全チャンネル分）。</summary>
    public int BlockAlign => Channels * (BitsPerSample / 8);

    /// <summary>音の長さ（秒）。試し撃ちの「出力秒」と RTF の分母。</summary>
    public double DurationSeconds =>
        BlockAlign > 0 && SampleRate > 0 ? (double)DataLength / BlockAlign / SampleRate : 0;
}

/// <summary>
/// wav の頭を読む<b>純関数</b>（試し撃ちの「出力秒」と、再生前の音量合わせに使う）。
/// <para>
/// NAudio に読ませる前にここで形を見るのは、⑴ <b>出力秒は再生しなくても要る</b>（保存だけした
/// ときも RTF を出す）⑵ <b>音量合わせ（裁定 52＝−16 dBFS 相当）の利得は再生器に依らない</b>
/// からである。読めない檔は例外を投げず偽を返す（合成の失敗と再生の失敗を混ぜない）。
/// </para>
/// </summary>
public static class WavInfo
{
    private const int RiffHeaderLength = 12;
    private const int ChunkHeaderLength = 8;

    /// <summary>頭を読む。RIFF/WAVE でない・欠けている・対応外の形なら偽。</summary>
    public static bool TryRead(ReadOnlySpan<byte> wav, out WavFormat? format)
    {
        format = null;
        if (wav.Length < RiffHeaderLength
            || !Matches(wav[..4], "RIFF")
            || !Matches(wav.Slice(8, 4), "WAVE"))
        {
            return false;
        }

        var channels = 0;
        var sampleRate = 0;
        var bits = 0;
        var isFloat = false;
        var sawFormat = false;

        var position = RiffHeaderLength;
        while (position + ChunkHeaderLength <= wav.Length)
        {
            var id = wav.Slice(position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(wav.Slice(position + 4, 4));
            var body = position + ChunkHeaderLength;
            if (size > int.MaxValue)
            {
                return false;
            }

            var length = (int)size;

            if (Matches(id, "fmt ") && length >= 16 && body + 16 <= wav.Length)
            {
                var tag = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body + 2, 2));
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(wav.Slice(body + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body + 14, 2));
                isFloat = tag == 3;

                // WAVE_FORMAT_EXTENSIBLE（0xFFFE）は SubFormat の先頭 2 バイトが実体の tag
                if (tag == 0xFFFE && length >= 40 && body + 26 <= wav.Length)
                {
                    isFloat = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body + 24, 2)) == 3;
                }
                else if (tag != 1 && tag != 3)
                {
                    return false;
                }

                sawFormat = true;
            }
            else if (Matches(id, "data"))
            {
                if (!sawFormat || channels <= 0 || sampleRate <= 0 || (bits != 16 && bits != 32))
                {
                    return false;
                }

                // 檔が途中で切れていても、在る分だけを長さとして扱う（壊れた wav で落ちない）
                var available = Math.Max(0, Math.Min(length, wav.Length - body));
                format = new WavFormat(channels, sampleRate, bits, isFloat, body, available);
                return true;
            }

            // チャンクは偶数境界（奇数長なら 1 バイトの詰め物が入る）
            position = body + length + (length % 2);
        }

        return false;
    }

    /// <summary>音の長さ（秒）。読めなければ null。</summary>
    public static double? DurationSeconds(ReadOnlySpan<byte> wav) =>
        TryRead(wav, out var format) && format is not null ? format.DurationSeconds : null;

    /// <summary>
    /// 標本を <c>-1..+1</c> の float に開く（音量合わせの材料）。読めなければ空。
    /// 多チャンネルは平均して 1 本にする（利得の計算に使うだけで、再生には使わない）。
    /// </summary>
    public static IReadOnlyList<float> ReadMonoSamples(ReadOnlySpan<byte> wav, int maxSamples = 1 << 20)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSamples, 1);
        if (!TryRead(wav, out var format) || format is null || format.BlockAlign <= 0)
        {
            return [];
        }

        var frames = format.DataLength / format.BlockAlign;
        if (frames <= 0)
        {
            return [];
        }

        var step = Math.Max(1, frames / maxSamples);
        var result = new List<float>(Math.Min(frames, maxSamples) + 1);
        var bytesPerSample = format.BitsPerSample / 8;

        for (var frame = 0; frame < frames; frame += step)
        {
            var at = format.DataOffset + (frame * format.BlockAlign);
            double sum = 0;
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var offset = at + (channel * bytesPerSample);
                sum += format.IsFloat
                    ? BinaryPrimitives.ReadSingleLittleEndian(wav.Slice(offset, 4))
                    : BinaryPrimitives.ReadInt16LittleEndian(wav.Slice(offset, 2)) / 32768.0;
            }

            result.Add((float)(sum / format.Channels));
        }

        return result;
    }

    private static bool Matches(ReadOnlySpan<byte> bytes, string ascii)
    {
        if (bytes.Length != ascii.Length)
        {
            return false;
        }

        for (var i = 0; i < ascii.Length; i++)
        {
            if (bytes[i] != (byte)ascii[i])
            {
                return false;
            }
        }

        return true;
    }
}
