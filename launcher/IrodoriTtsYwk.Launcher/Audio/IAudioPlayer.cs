using System;

namespace IrodoriTtsYwk.Launcher.Audio;

/// <summary>再生 1 回の結末（例外ではなく結末で返す＝合成の失敗と再生の失敗を混ぜない）。</summary>
/// <param name="Ok">鳴らせたか。</param>
/// <param name="Gain">掛けた利得（裁定 52＝−16 dBFS 相当）。</param>
/// <param name="DurationSeconds">音の長さ。</param>
/// <param name="FailureReason">理由 1 行（絶対パスを出さない）。</param>
public sealed record PlaybackResult(
    bool Ok,
    double Gain,
    double? DurationSeconds,
    string? FailureReason);

/// <summary>
/// 試聴と試し撃ちの再生（裁定 52＝NAudio・MIT・再生時に −16 dBFS 相当へ揃える）。
/// <para>
/// <b>形は wav だけ</b>＝同梱するのは <c>NAudio.WinMM</c> と <c>NAudio.Core</c> の 2 本で、
/// <c>Mp3FileReader</c>／<c>AudioFileReader</c> はどちらにも入っていない（2026-09-05 に
/// nupkg の型一覧を機械で確認）。mp3 等を鳴らすには NAudio のメタパッケージか ACM 経路が要る＝
/// <b>依存を増やさない</b>（依頼文の作法）ので、参照ボイスとして受ける 8 拡張子のうち
/// wav 以外の<b>試聴だけ</b>が「未対応」になる（登録・合成には影響しない）。
/// </para>
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>いま鳴っているか。</summary>
    bool IsPlaying { get; }

    /// <summary>wav の生バイトを鳴らす（前の再生は止める）。</summary>
    PlaybackResult Play(byte[] wav);

    /// <summary>檔を鳴らす（wav 以外は「未対応」を理由に偽を返す）。</summary>
    PlaybackResult PlayFile(string path);

    /// <summary>止める。</summary>
    void Stop();
}
