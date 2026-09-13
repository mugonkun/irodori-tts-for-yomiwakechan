using System;
using System.Linq;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 司令官の指示（2026-09-13）＝⑴ 帯の下の <b>GPU メモリのメーター</b>（全体／使用中／このアプリ・
/// RTX（CUDA）と Radeon（ROCm）で同じ道）⑵ <b>ログのタブ</b>（檔に加えて画面でもその場で流れる）
/// ⑶ 語句の差し替え（読み上げ／高品質・低品質（高速）／キャプション（演技指示）／
/// 話者と参照ファイル／発話待機中）。
/// </summary>
public sealed class GpuMeterAndLogTabTests
{
    private const long GiB = 1L << 30;

    private static OsGpuMemoryRow Row(string name, long app, long? adapter, long? total, string luid = "0x0_0x1") =>
        new(luid, name, app, adapter, total);

    // ---------------- ⑴ メーター（純関数）----------------

    [Fact]
    public void 行が無ければ棒は空で文は横棒()
    {
        var meter = GpuMeter.Describe(null);

        Assert.False(meter.HasData);
        Assert.Equal(0, meter.AppFraction);
        Assert.Equal(0, meter.OthersFraction);
        Assert.Equal(1, meter.FreeFraction);
        Assert.Equal(UiText.Missing, meter.Text);

        Assert.Same(GpuMeter.Empty, GpuMeter.Describe(Array.Empty<OsGpuMemoryRow>()));
    }

    [Fact]
    public void 三区画の割合は足すと1になり文は三つの数を並べる()
    {
        var meter = GpuMeter.Describe([Row("AMD Radeon(TM) 8060S Graphics", 13 * GiB, 51 * GiB, 64 * GiB)]);

        Assert.True(meter.HasData);
        Assert.Equal(13.0 / 64, meter.AppFraction, 10);
        Assert.Equal(38.0 / 64, meter.OthersFraction, 10);
        Assert.Equal(13.0 / 64, meter.FreeFraction, 10);
        Assert.Equal(1.0, meter.AppFraction + meter.OthersFraction + meter.FreeFraction, 10);

        Assert.Equal("このアプリ 13.00 GiB／使用中 51.00 GiB（80%）／全体 64.00 GiB", meter.Text);

        // 1 枚だけなら名前は頭に立てない（詳しい状態の欄が名乗る）。
        Assert.DoesNotContain("Radeon", meter.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void 占有が読めない行はこのアプリの分だけを使用中と読む()
    {
        var meter = GpuMeter.Describe([Row("NVIDIA GeForce RTX 3090", 10 * GiB, null, 24 * GiB)]);

        Assert.True(meter.HasData);
        Assert.Equal(10.0 / 24, meter.AppFraction, 10);
        Assert.Equal(0, meter.OthersFraction);
        Assert.Equal(14.0 / 24, meter.FreeFraction, 10);
        Assert.Contains("使用中 10.00 GiB", meter.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void 総量が読めなければ割合は出さず数だけ出す()
    {
        var meter = GpuMeter.Describe([Row("Unknown", 2 * GiB, 3 * GiB, null)]);

        Assert.False(meter.HasData);
        Assert.Equal(1, meter.FreeFraction);
        Assert.Equal("このアプリ 2.00 GiB／全体 —", meter.Text);
    }

    [Fact]
    public void 占有がこのアプリより小さくても総量を超えても壊れない()
    {
        // PDH の 2 counter は同じ瞬間ではないので、逆転・超過が起きうる＝挟んで丸める。
        var meter = GpuMeter.Describe([Row("X", 5 * GiB, 3 * GiB, 4 * GiB)]);

        Assert.Equal(1.0, meter.AppFraction, 10);
        Assert.Equal(0, meter.OthersFraction);
        Assert.Equal(0, meter.FreeFraction);
    }

    [Fact]
    public void 二枚以上ならこのアプリがいちばん載っている一枚を選び名前を頭に立てる()
    {
        var meter = GpuMeter.Describe(
        [
            Row("Small", 1 * GiB, 2 * GiB, 8 * GiB, "0x0_0x1"),
            Row("Big", 6 * GiB, 7 * GiB, 16 * GiB, "0x0_0x2"),
        ]);

        Assert.StartsWith("Big＝このアプリ 6.00 GiB", meter.Text, StringComparison.Ordinal);
        Assert.Equal(6.0 / 16, meter.AppFraction, 10);
    }

    [Fact]
    public void 状態帯は標本の計数からメーターを組み直し標本が消えれば空に戻る()
    {
        var status = new StatusViewModel(() => Task.CompletedTask, () => Task.CompletedTask);
        Assert.Same(GpuMeter.Empty, status.GpuMeter);

        var raised = 0;
        status.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StatusViewModel.GpuMeter))
            {
                raised++;
            }
        };

        status.ApplyStatus(new StatusResponse(), [Row("G", 1 * GiB, 2 * GiB, 4 * GiB)]);
        Assert.True(status.GpuMeter.HasData);
        Assert.Equal(0.25, status.GpuMeter.AppFraction, 10);
        Assert.Equal(1, raised);

        status.ApplyStatus(null);
        Assert.Same(GpuMeter.Empty, status.GpuMeter);
        Assert.Equal(2, raised);
    }

    // ---------------- ⑵ ログのタブ ----------------

    [Fact]
    public void ログのタブは檔と同じ時刻つきの行を持ち事象でも配る()
    {
        var status = new StatusViewModel(() => Task.CompletedTask, () => Task.CompletedTask)
        {
            Clock = static () => new DateTimeOffset(2026, 9, 13, 12, 34, 56, TimeSpan.FromHours(9)),
        };
        string? delivered = null;
        status.LineLogged += (_, line) => delivered = line;

        status.AppendLog("起動：http://127.0.0.1:18088");

        Assert.Equal("12:34:56 起動：http://127.0.0.1:18088", delivered);
        Assert.Equal(["12:34:56 起動：http://127.0.0.1:18088"], status.FullLogLines);
        Assert.Equal(delivered, status.FullLogText);
    }

    [Fact]
    public void ログのタブは見張りの足音と空行を入れない()
    {
        var status = new StatusViewModel(() => Task.CompletedTask, () => Task.CompletedTask);

        status.AppendLog("   ");
        status.AppendLog("INFO: 127.0.0.1:50000 - \"GET /ywk/status HTTP/1.1\" 200 OK");
        status.AppendLog("INFO: 127.0.0.1:50000 - \"POST /v1/audio/speech HTTP/1.1\" 200 OK");

        Assert.Single(status.FullLogLines);
        Assert.Contains("/v1/audio/speech", status.FullLogLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ログのタブは上限を超えたら古い方から捨てる()
    {
        var status = new StatusViewModel(() => Task.CompletedTask, () => Task.CompletedTask);

        for (var i = 0; i < StatusViewModel.FullLogCapacity + 5; i++)
        {
            status.AppendLog("line " + i);
        }

        Assert.Equal(StatusViewModel.FullLogCapacity, status.FullLogLines.Count);
        Assert.EndsWith("line 5", status.FullLogLines[0], StringComparison.Ordinal);
        Assert.EndsWith("line " + (StatusViewModel.FullLogCapacity + 4), status.FullLogLines[^1], StringComparison.Ordinal);

        // 帯の末尾 20 行は別の箱のまま。
        Assert.Equal(LogTail.DefaultCapacity, status.LogText.Split(Environment.NewLine).Length);
    }

    [Fact]
    public void 画面を空にしても檔の書き手は呼ばれず次の行からまた溜まる()
    {
        var sunk = 0;
        var status = new StatusViewModel(() => Task.CompletedTask, () => Task.CompletedTask)
        {
            LogSink = _ => sunk++,
        };

        status.AppendLog("a");
        status.ClearFullLog();
        Assert.Empty(status.FullLogLines);
        Assert.Equal(string.Empty, status.FullLogText);

        status.AppendLog("b");
        Assert.Single(status.FullLogLines);
        Assert.Equal(2, sunk);
    }

    // ---------------- ⑶ 語句 ----------------

    [Fact]
    public void 語句は司令官の指示どおりに差し替わっている()
    {
        Assert.Equal("読み上げ", UiStrings.TrySynthesizeButton);
        Assert.Equal("高品質", UiStrings.TryQualityFine);
        Assert.Equal("低品質（高速）", UiStrings.TryQualityFast);
        Assert.StartsWith("キャプション（演技指示", UiStrings.TryCaptionLabel, StringComparison.Ordinal);
        Assert.StartsWith("キャプション（演技指示", UiStrings.VoicesCaptionLabel, StringComparison.Ordinal);
        Assert.Equal("話者と参照ファイル", UiStrings.TabVoices);
        Assert.Equal("発話待機中", BandText.Ready);
        Assert.StartsWith("発話待機中", BandText.ReadyWarming, StringComparison.Ordinal);
        Assert.Equal("ログ", UiStrings.TabLog);

        // 旧い綴りは利用者に出る文から消えている（UiStrings の public な文字列を舐める）。
        var strings = typeof(UiStrings)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToArray();
        Assert.DoesNotContain(strings, s => s.Contains("しゃべらせ", StringComparison.Ordinal));
        Assert.DoesNotContain(strings, s => s.Contains("話し方の指示", StringComparison.Ordinal));
        Assert.DoesNotContain(strings, s => s == "はやい" || s == "きれい");
    }
}
