using System;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace IrodoriTtsYwk.Launcher.Audio;

/// <summary>
/// <see cref="IAudioPlayer"/> の実装（NAudio・MIT・裁定 52）。
/// <para>
/// <c>WaveOutEvent</c>（NAudio.WinMM）＋<c>WaveFileReader</c>＋<c>VolumeSampleProvider</c>
/// （NAudio.Core）の 3 つだけを使う。窓のハンドルを要らない <c>WaveOutEvent</c> を選ぶのは、
/// 本体 yomiwakechan2 と同じ選び方である（窓を閉じる＝アプリが終わるので、そこで鳴りも止まる）。
/// </para>
/// <para>
/// <b>利得は <see cref="WavGain"/> が決める</b>（純関数）＝ここは鳴らすだけ。檔は書き換えない。
/// </para>
/// </summary>
public sealed class NAudioPlayer : IAudioPlayer
{
    private readonly object _gate = new();
    private WaveOutEvent? _device;
    private WaveFileReader? _reader;
    private MemoryStream? _stream;
    private bool _disposed;

    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _device?.PlaybackState == PlaybackState.Playing;
            }
        }
    }

    public PlaybackResult Play(byte[] wav)
    {
        ArgumentNullException.ThrowIfNull(wav);
        if (wav.Length == 0)
        {
            return new PlaybackResult(false, 1.0, null, "鳴らす音がありません。");
        }

        var gain = WavGain.ComputeGainForWav(wav);
        var duration = WavInfo.DurationSeconds(wav);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopCore();
            try
            {
                _stream = new MemoryStream(wav, writable: false);
                _reader = new WaveFileReader(_stream);
                var provider = new VolumeSampleProvider(_reader.ToSampleProvider())
                {
                    Volume = (float)gain,
                };

                _device = new WaveOutEvent();
                _device.Init(provider);
                _device.Play();
            }
            catch (FormatException ex)
            {
                StopCore();
                return new PlaybackResult(false, gain, duration, "この音は読めませんでした：" + ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                StopCore();
                return new PlaybackResult(false, gain, duration, "再生できませんでした：" + ex.Message);
            }
            catch (ArgumentException ex)
            {
                StopCore();
                return new PlaybackResult(false, gain, duration, "再生できませんでした：" + ex.Message);
            }
        }

        return new PlaybackResult(true, gain, duration, null);
    }

    public PlaybackResult PlayFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // wav 以外は鳴らせない（NAudio.Core／WinMM に mp3 等の reader が無い＝IAudioPlayer の注記）。
        if (!string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return new PlaybackResult(
                false, 1.0, null, "試聴は wav のみ対応です（この檔は " + Path.GetExtension(path) + "）。");
        }

        try
        {
            return Play(File.ReadAllBytes(path));
        }
        catch (FileNotFoundException)
        {
            return new PlaybackResult(false, 1.0, null, "参照 wav が見つかりません。");
        }
        catch (DirectoryNotFoundException)
        {
            return new PlaybackResult(false, 1.0, null, "参照 wav が見つかりません。");
        }
        catch (IOException ex)
        {
            return new PlaybackResult(false, 1.0, null, "参照 wav が読めません：" + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new PlaybackResult(false, 1.0, null, "参照 wav が読めません：" + ex.Message);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopCore();
        }
    }

    private void StopCore()
    {
        try
        {
            _device?.Stop();
        }
        catch (InvalidOperationException)
        {
            // 既に閉じた装置＝黙って進む
        }

        _device?.Dispose();
        _device = null;
        _reader?.Dispose();
        _reader = null;
        _stream?.Dispose();
        _stream = null;
    }
}
