using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Mvvm;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// L3（Views／ViewModels）の<b>純ロジックだけ</b>を釘付けする。
/// 窓も Dispatcher も GPU も HTTP も出てこない（ViewModel は WPF の型に触れない設計）。
/// </summary>
public sealed class UiTextTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KiB")]
    [InlineData(1048576, "1.0 MiB")]
    [InlineData(1073741824, "1.00 GiB")]
    public void バイト数は1024進で出る(long bytes, string expected) =>
        Assert.Equal(expected, UiText.Bytes(bytes));

    [Fact]
    public void 値が無い欄はダッシュになる()
    {
        Assert.Equal(UiText.Missing, UiText.Bytes(null));
        Assert.Equal(UiText.Missing, UiText.Eta(null));
        Assert.Equal(UiText.Missing, UiText.Milliseconds(null));
        Assert.Equal(UiText.Missing, UiText.Seconds(null));
    }

    [Fact]
    public void UUIDは下6桁だけを出す()
    {
        // 裁定 34＝同定は UUID。画面には人が見分けられる長さだけ出す。
        Assert.Equal("717a36", UiText.UuidTail("GPU-19adfe89-c9e0-df55-4a8a-e31798717a36"));
        // GPU- の前置が無くても同じ（torch は前置なしを返しうる）
        Assert.Equal("717a36", UiText.UuidTail("19adfe89-c9e0-df55-4a8a-e31798717a36"));
        Assert.Equal(UiText.Missing, UiText.UuidTail(null));
        Assert.Equal(UiText.Missing, UiText.UuidTail("   "));
    }

    [Fact]
    public void 短いUUIDはそのまま出る() => Assert.Equal("abc", UiText.UuidTail("abc"));

    [Fact]
    public void ETAは時分秒に畳まれる()
    {
        Assert.Equal("30 秒", UiText.Eta(TimeSpan.FromSeconds(30)));
        Assert.Equal("2 分 5 秒", UiText.Eta(TimeSpan.FromSeconds(125)));
        Assert.Equal("1 時間 1 分", UiText.Eta(TimeSpan.FromMinutes(61)));
    }

    [Fact]
    public void 所要は1秒を境に単位が変わる()
    {
        Assert.Equal("500 ms", UiText.Milliseconds(500));
        Assert.Equal("1.49 秒", UiText.Milliseconds(1490));
    }

    [Fact]
    public void RTFは所要を出力尺で割る()
    {
        // docs/radeon.md §7 の物差し（1 未満なら実時間より速い）
        Assert.Equal("RTF 0.50", UiText.RealTimeFactor(1000, 2.0));
        Assert.Equal(UiText.Missing, UiText.RealTimeFactor(1000, 0));
        Assert.Equal(UiText.Missing, UiText.RealTimeFactor(null, 2.0));
    }

    [Fact]
    public void 件数は分母が無くてもダッシュで出る()
    {
        Assert.Equal("3 / 6", UiText.Progress(3, 6));
        Assert.Equal("3 / —", UiText.Progress(3, null));
        Assert.Equal(UiText.Missing, UiText.Progress(null, null));
    }
}

public sealed class MemoryEstimateTests
{
    [Fact]
    public void wav参照だけが積み上がる()
    {
        // 裁定 67 ⑵＝wav 参照は +0.7 GB 級・潜在参照と参照なしは増えない
        Assert.Equal(MemoryEstimate.WavReferenceBytes, MemoryEstimate.ForVoice(noRef: false, hasLatent: false));
        Assert.Equal(0, MemoryEstimate.ForVoice(noRef: false, hasLatent: true));
        Assert.Equal(0, MemoryEstimate.ForVoice(noRef: true, hasLatent: false));
    }

    [Fact]
    public void 一覧の合計はwav参照の数に比例する()
    {
        var voices = new[]
        {
            new VoiceInfo { Id = VoiceIds.Default, NoRef = true },
            new VoiceInfo { Id = "つくよみちゃん" },
            new VoiceInfo { Id = "もち子さん", Latent = true },
            new VoiceInfo { Id = "琴葉茜" },
        };

        Assert.Equal(MemoryEstimate.WavReferenceBytes * 2, MemoryEstimate.ForVoices(voices));
    }

    [Fact]
    public void 出力はフレーム数から出す()
    {
        // 秒→フレームの換算率は席が知らない＝フレーム数を受ける（推測の数字を出さない）
        Assert.Equal(MemoryEstimate.OutputFrameBytes * 10, MemoryEstimate.ForOutputFrames(10));
        Assert.Equal(0, MemoryEstimate.ForOutputFrames(0));
    }

    [Fact]
    public void 説明はどの種類かを言う()
    {
        Assert.Contains("参照なし", MemoryEstimate.Describe(true, false), StringComparison.Ordinal);
        Assert.Contains("潜在参照", MemoryEstimate.Describe(false, true), StringComparison.Ordinal);
        Assert.Contains("wav 参照", MemoryEstimate.Describe(false, false), StringComparison.Ordinal);
    }
}

public sealed class ReleaseFlavorTests
{
    [Fact]
    public void rocmの台帳があればRadeon版と読む()
    {
        // 裁定 5＝Radeon 版は別リリース。CUDA の選択肢を出してはならない。
        var flavor = ReleaseFlavors.Detect(["python-embed", "runtime-rocm-gfx1151", "models"]);
        Assert.Equal(ReleaseFlavor.Radeon, flavor);
        Assert.DoesNotContain(RuntimeVariants.Cu130, ReleaseFlavors.Choices(flavor));
        Assert.Contains(RuntimeVariants.RocmGfx1151, ReleaseFlavors.Choices(flavor));
    }

    [Fact]
    public void 既定はCUDA版()
    {
        var flavor = ReleaseFlavors.Detect(["runtime-cu130", "runtime-cu126", "runtime-cpu"]);
        Assert.Equal(ReleaseFlavor.Cuda, flavor);
        Assert.Equal(3, ReleaseFlavors.Choices(flavor).Count);
    }

    [Fact]
    public void 台帳の無い変種は選べない()
    {
        var available = ReleaseFlavors.AvailableChoices(
            ReleaseFlavor.Cuda, ["runtime-cu130", "runtime-cpu"]);

        Assert.Equal([RuntimeVariants.Cu130, RuntimeVariants.Cpu], available);
    }

    [Fact]
    public void 台帳が1件も読めなければ全部を出す()
    {
        // 樹が読めなかっただけで選べなくしない（初回起動で詰ませない）
        var available = ReleaseFlavors.AvailableChoices(ReleaseFlavor.Radeon, []);
        Assert.Equal(RuntimeVariants.RadeonReleaseChoices, available);
    }

    // ---- 裁定 109＝版の名札（CUDA 版／ROCm 版）--------------------------------------

    [Fact]
    public void 版の名札は利用者にはROCm版と名乗る()
    {
        // 内部の識別子（enum の Radeon・台帳名 runtime-rocm-*・Flavor id の radeon）は据え置きで、
        // **利用者に見せる名だけ**が「ROCm 版」＝司令官の逐語「(CUDA版)(ROCm版)」。
        Assert.Equal("CUDA 版", ReleaseFlavors.FlavorLabel(ReleaseFlavor.Cuda));
        Assert.Equal("ROCm 版", ReleaseFlavors.FlavorLabel(ReleaseFlavor.Radeon));
    }

    [Fact]
    public void 窓題はインストーラの表示名と1字も違わない()
    {
        // installer/irodori-tts-ywk.iss の MyAppName の逐語（全角括弧・版の前に半角空白 1 つ）。
        Assert.Equal("irodori-TTS for 読み分けちゃん（CUDA 版）", ReleaseFlavors.AppTitle(ReleaseFlavor.Cuda));
        Assert.Equal("irodori-TTS for 読み分けちゃん（ROCm 版）", ReleaseFlavors.AppTitle(ReleaseFlavor.Radeon));
        Assert.StartsWith(ReleaseFlavors.AppBaseName, ReleaseFlavors.AppTitle(ReleaseFlavor.Radeon), StringComparison.Ordinal);
    }

    [Fact]
    public void 初回取得の窓題も版で分かれる()
    {
        Assert.Equal("初回取得（CUDA 版）", ReleaseFlavors.WizardTitle(ReleaseFlavor.Cuda));
        Assert.Equal("初回取得（ROCm 版）", ReleaseFlavors.WizardTitle(ReleaseFlavor.Radeon));
    }

    // トレイの吹き出しの 2 本（`TrayText` の版の名札と 63 字の枠）は裁定 124 で消えた＝
    // 常駐しないので NotifyIcon が無い（App.xaml.cs から丸ごと落ちた）。
    // 窓題の名札は上の 2 本（AppTitle／WizardTitle）が引き続き釘付けする。

    [Fact]
    public void 樹が読めなくてもDetectFromは投げずCUDA版と読む()
    {
        // 窓題（MainWindow.xaml.cs）・このアプリについての見出し（AboutView.xaml.cs）・
        // トレイ（App.OnStartup）の 3 箇所がこの 1 本を通る＝ここが投げると起動そのものが死ぬ。
        // LedgerNames は ArgumentException.ThrowIfNullOrWhiteSpace で始まるので、
        // DetectFrom の空白判定だけがそれを塞いでいる（ReleaseFlavor.cs の逐語）。
        Assert.Equal(ReleaseFlavor.Cuda, ReleaseFlavors.DetectFrom(null));
        Assert.Equal(ReleaseFlavor.Cuda, ReleaseFlavors.DetectFrom(string.Empty));
        Assert.Equal(ReleaseFlavor.Cuda, ReleaseFlavors.DetectFrom("   "));

        // 在りもしない路＝Directory.Exists が false ＝空の並び ＝ CUDA 版
        var missing = Path.Combine(Path.GetTempPath(), "ywk-detect-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(ReleaseFlavor.Cuda, ReleaseFlavors.DetectFrom(missing));
    }

    [Fact]
    public void rocmの台帳が在る樹はDetectFromでもRadeon版と読む()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ywk-detect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "runtime-cpu.json"), "{}");
            Assert.Equal(ReleaseFlavor.Cuda, ReleaseFlavors.DetectFrom(dir));

            File.WriteAllText(Path.Combine(dir, "runtime-rocm-gfx1151.json"), "{}");
            Assert.Equal(ReleaseFlavor.Radeon, ReleaseFlavors.DetectFrom(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public sealed class LogTailTests
{
    [Fact]
    public void 上限を超えたら古い行から捨てる()
    {
        var tail = new LogTail(3);
        for (var i = 1; i <= 5; i++)
        {
            tail.Append("line " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Equal(3, tail.Count);
        Assert.Equal(["line 3", "line 4", "line 5"], tail.Lines);
    }

    [Fact]
    public void 既定は20行()
    {
        Assert.Equal(20, LogTail.DefaultCapacity);
        Assert.Equal(20, new LogTail().Capacity);
    }

    [Fact]
    public void 長い行は畳む()
    {
        // 受け入れ条件 D-4＝422 の本文 echo（1 発 5 KB）をログに流さない
        var tail = new LogTail(2);
        tail.Append(new string('x', 5000));

        var line = Assert.Single(tail.Lines);
        Assert.True(line.Length < 5000);
        Assert.Contains("畳んだ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void 空行は入れない()
    {
        var tail = new LogTail();
        tail.Append(null);
        tail.Append("   ");
        Assert.Equal(0, tail.Count);
    }
}

public sealed class SpeechRequestBuilderTests
{
    [Fact]
    public void 既定に戻す欄はirodoriに載らない()
    {
        // 契約 ⑶＝「既定に戻す」は欄を出さないことで表す
        var built = SpeechRequestBuilder.Build(new TryShot("こんにちは。", "デフォルト"));

        Assert.True(built.Ok);
        Assert.NotNull(built.Request);
        Assert.Null(built.Request!.Irodori);
        Assert.Null(built.Request.Speed);
    }

    [Fact]
    public void 空のcaptionとseedは未指定に畳む()
    {
        // 裁定 48＝上流は空 caption を 400 にする
        var built = SpeechRequestBuilder.Build(
            new TryShot("あ", "話者", NumSteps: 40, Caption: "   ", Seed: ""));

        Assert.True(built.Ok);
        var irodori = built.Request!.Irodori!;
        Assert.False(irodori.ContainsKey("caption"));
        Assert.False(irodori.ContainsKey("seed"));
        Assert.Equal(40, irodori["num_steps"]);
    }

    [Fact]
    public void 指定した欄だけがirodoriに載る()
    {
        var built = SpeechRequestBuilder.Build(new TryShot(
            "あ", "話者", NumSteps: 10, CfgScaleText: 3.5, Caption: "楽しそうに", Seed: "1234"));

        var irodori = built.Request!.Irodori!;
        Assert.Equal(10, irodori["num_steps"]);
        Assert.Equal(3.5, irodori["cfg_scale_text"]);
        Assert.Equal("楽しそうに", irodori["caption"]);
        Assert.Equal(1234L, irodori["seed"]);
        Assert.False(irodori.ContainsKey("cfg_scale_caption"));
        Assert.False(irodori.ContainsKey("cfg_scale_speaker"));
        // irodori.seconds は暖機の射だけ＝本番には決して載せない
        Assert.False(irodori.ContainsKey("seconds"));
        // speed と duration_scale は同時に送らない
        Assert.False(irodori.ContainsKey("duration_scale"));
    }

    [Fact]
    public void 本文が空なら撃たない()
    {
        var built = SpeechRequestBuilder.Build(new TryShot("   "));
        Assert.False(built.Ok);
        Assert.Null(built.Request);
        Assert.NotNull(built.FailureReason);
    }

    [Fact]
    public void 本文が上限を超えたら撃たない()
    {
        var built = SpeechRequestBuilder.Build(
            new TryShot(new string('あ', SpeechRequestBuilder.MaxInputLength + 1)));
        Assert.False(built.Ok);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(121)]
    public void 範囲外の歩数は撃つ前に弾く(int steps)
    {
        // 上流は num_steps:-5 も黙って通す＝UI が最後の砦
        var built = SpeechRequestBuilder.Build(new TryShot("あ", NumSteps: steps));
        Assert.False(built.Ok);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(4.5)]
    public void 範囲外の読み速さは撃つ前に弾く(double speed)
    {
        var built = SpeechRequestBuilder.Build(new TryShot("あ", Speed: speed));
        Assert.False(built.Ok);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    public void 数でない種は撃つ前に弾く(string seed)
    {
        var built = SpeechRequestBuilder.Build(new TryShot("あ", Seed: seed));
        Assert.False(built.Ok);
    }

    [Fact]
    public void 誤りはcodeで判じて文言では判じない()
    {
        // 契約 ⑶ 3-3＝機械可読な code が必ず入る
        var unknownVoice = SpeechRequestBuilder.DescribeError(
            new ErrorBody { Code = WrapperErrorCodes.UnknownVoice, Message = "voice not found" }, 400);
        Assert.Contains("話者", unknownVoice, StringComparison.Ordinal);

        // 知らない code は wrapper の message をそのまま出す（席が言い換えない）
        var unknown = SpeechRequestBuilder.DescribeError(
            new ErrorBody { Code = "ywk_something_new", Message = "なにか" }, 400);
        Assert.Contains("なにか", unknown, StringComparison.Ordinal);

        // 届かなかったとき（StatusCode 0）
        Assert.Contains("届きません", SpeechRequestBuilder.DescribeError(null, 0), StringComparison.Ordinal);
    }

    [Fact]
    public void 空白の畳みは前後だけを落とす()
    {
        Assert.Null(SpeechRequestBuilder.Fold(null));
        Assert.Null(SpeechRequestBuilder.Fold("  "));
        Assert.Equal("あ", SpeechRequestBuilder.Fold("  あ  "));
    }
}

public sealed class VoiceRowTests
{
    private static VoicesYwkFile Store(params (string Id, VoiceEntry Entry)[] entries) => new()
    {
        Voices = entries.ToDictionary(e => e.Id, e => e.Entry, StringComparer.Ordinal),
    };

    [Fact]
    public void デフォルトは台帳に無くても先頭に常在する()
    {
        // 受け入れ条件 D-3＝「デフォルト」が常在（先頭）・none 0 件
        var rows = VoiceRowBuilder.Build(Store(("あ", new VoiceEntry { DisplayName = "あ" })), null, null);

        Assert.Equal(VoiceIds.Default, rows[0].Id);
        Assert.True(rows[0].IsNoRef);
        Assert.False(rows[0].CanRemove);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void 参照なしの別名は一覧に出さない()
    {
        var live = new VoicesResponse
        {
            Data =
            [
                new VoiceInfo { Id = VoiceIds.Default, NoRef = true },
                new VoiceInfo { Id = "none", NoRef = true },
                new VoiceInfo { Id = "no-ref", NoRef = true },
                new VoiceInfo { Id = "つくよみちゃん" },
            ],
        };

        var rows = VoiceRowBuilder.Build(null, live, null);

        Assert.Equal([VoiceIds.Default, "つくよみちゃん"], rows.Select(r => r.Id));
    }

    [Fact]
    public void 焼いたかどうかはwrapperが正本()
    {
        // 台帳は ref_latent を持たないが、走っている個体が latent=true と名乗る
        var store = Store(("もち子さん", new VoiceEntry { DisplayName = "もち子さん", File = "ywk-abc.wav" }));
        var live = new VoicesResponse
        {
            Data = [new VoiceInfo { Id = "もち子さん", Latent = true, LatentStale = true }],
        };

        var row = VoiceRowBuilder.Build(store, live, null).Single(r => r.Id == "もち子さん");

        Assert.True(row.HasLatent);
        Assert.True(row.IsLatentStale);
        Assert.Equal("要・焼き直し", row.LatentText);
        // 焼いてあるので概算メモリは増えない（裁定 67 ⑵）
        Assert.Equal(0, MemoryEstimate.ForVoice(row.IsNoRef, row.HasLatent));
    }

    [Fact]
    public void サーバが居なければ台帳のref_latentで代用する()
    {
        var store = Store(("あ", new VoiceEntry { DisplayName = "あ", File = "x.wav", RefLatent = "latents/x.pt" }));
        var row = VoiceRowBuilder.Build(store, null, null).Single(r => r.Id == "あ");

        Assert.True(row.HasLatent);
        Assert.False(row.IsLatentStale);
        Assert.Equal("焼き済み", row.LatentText);
    }

    [Fact]
    public void 設定の並びが効く()
    {
        var store = Store(
            ("あ", new VoiceEntry { DisplayName = "あ" }),
            ("い", new VoiceEntry { DisplayName = "い" }),
            ("う", new VoiceEntry { DisplayName = "う" }));

        var rows = VoiceRowBuilder.Build(store, null, ["う", "い"]);

        Assert.Equal([VoiceIds.Default, "う", "い", "あ"], rows.Select(r => r.Id));
    }

    [Fact]
    public void 焼き直しが要る話者だけを拾う()
    {
        var rows = new[]
        {
            new VoiceRow(VoiceIds.Default, "デフォルト", null, false, true, false, false, null),
            new VoiceRow("焼き済み", "焼き済み", "a.wav", false, false, true, false, null),
            new VoiceRow("古い", "古い", "b.wav", false, false, true, true, null),
            new VoiceRow("未", "未", "c.wav", false, false, false, false, null),
        };

        Assert.Equal(["古い", "未"], VoiceRowBuilder.NeedsPrecompute(rows));
    }

    [Fact]
    public void 参照wavが無い行は試聴できない()
    {
        var row = new VoiceRow("あ", "あ", null, false, false, false, false, null);
        Assert.False(row.CanPreview);
        Assert.True(row.CanRemove);
    }
}

public sealed class VoiceNameValidatorTests
{
    [Fact]
    public void 日本語の名前は通る() => Assert.Null(VoiceNameValidator.Validate("つくよみちゃん", []));

    [Fact]
    public void 空の名前は弾く() => Assert.NotNull(VoiceNameValidator.Validate("  ", []));

    [Theory]
    [InlineData("デフォルト")]
    [InlineData("none")]
    [InlineData("NO-REF")]
    public void 参照なしの別名は取れない(string name) =>
        Assert.NotNull(VoiceNameValidator.Validate(name, []));

    [Fact]
    public void 同じ名前は取れない() =>
        Assert.NotNull(VoiceNameValidator.Validate("あ", ["あ", "い"]));

    [Fact]
    public void 制御文字は弾く() =>
        Assert.NotNull(VoiceNameValidator.Validate("あ\tい", []));

    [Fact]
    public void 長すぎる名前は弾く() =>
        Assert.NotNull(VoiceNameValidator.Validate(new string('あ', VoiceNameValidator.MaxLength + 1), []));

    [Theory]
    [InlineData("a.wav")]
    [InlineData("a.MP3")]
    [InlineData("a.opus")]
    public void 受ける拡張子は8種(string path) => Assert.Null(VoiceNameValidator.ValidateSourceFile(path));

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a")]
    [InlineData("")]
    public void 受けない拡張子は弾く(string path) => Assert.NotNull(VoiceNameValidator.ValidateSourceFile(path));

    [Fact]
    public void 長さの推奨は止めずに告げるだけ()
    {
        Assert.Null(VoiceNameValidator.AdviseLength(20));
        Assert.NotNull(VoiceNameValidator.AdviseLength(3));
        Assert.NotNull(VoiceNameValidator.AdviseLength(120));
        Assert.Null(VoiceNameValidator.AdviseLength(null));
    }
}

public sealed class WarmupStagesTextTests
{
    [Fact]
    public void 既定は4と8と12() => Assert.Equal("4, 8, 12", WarmupStagesText.Format(WarmupStagesText.Default));

    [Fact]
    public void カンマでも読点でも読める()
    {
        Assert.True(WarmupStagesText.TryParse("4、8, 12", out var stages, out var error));
        Assert.Null(error);
        Assert.Equal([4.0, 8.0, 12.0], stages);
    }

    [Fact]
    public void 空は段を撃たない設定として通す()
    {
        Assert.True(WarmupStagesText.TryParse("  ", out var stages, out _));
        Assert.Empty(stages);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("61")]
    [InlineData("あ")]
    public void 範囲外と読めない段は弾く(string text)
    {
        Assert.False(WarmupStagesText.TryParse(text, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void 上限を超える段は弾く()
    {
        var text = string.Join(",", Enumerable.Repeat("4", WarmupStagesText.MaxStages + 1));
        Assert.False(WarmupStagesText.TryParse(text, out _, out _));
    }

    [Fact]
    public void 話者は空白を含めるのでカンマだけで割る()
    {
        Assert.True(WarmupStagesText.TryParseVoices("つくよみ ちゃん, もち子さん", out var voices, out _));
        Assert.Equal(["つくよみ ちゃん", "もち子さん"], voices);
    }

    [Fact]
    public void 話者は64件まで()
    {
        var text = string.Join(",", Enumerable.Range(0, 65).Select(i => "v" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.False(WarmupStagesText.TryParseVoices(text, out _, out _));
    }
}

public sealed class NumericInputTests
{
    [Fact]
    public void 空欄はnullで0は0()
    {
        Assert.True(NumericInput.TryParseOptional("", out var blank, out _));
        Assert.Null(blank);

        Assert.True(NumericInput.TryParseOptional("0", out var zero, out _));
        Assert.Equal(0.0, zero);
    }

    [Fact]
    public void 読めない値は理由をつけて偽()
    {
        Assert.False(NumericInput.TryParseOptional("あ", out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void NaNと無限は受けない()
    {
        Assert.False(NumericInput.TryParseOptional("NaN", out _, out _));
        Assert.False(NumericInput.TryParseOptional("Infinity", out _, out _));
    }

    [Fact]
    public void 書き戻しはnullだけが空欄()
    {
        Assert.Equal(string.Empty, NumericInput.Format((double?)null));
        Assert.Equal("0", NumericInput.Format((double?)0));
        Assert.Equal("3.5", NumericInput.Format((double?)3.5));
    }
}

public sealed class WavTests
{
    /// <summary>16 bit・モノラルの wav を組む（試験の材料）。</summary>
    internal static byte[] MakeWav(int sampleRate, IReadOnlyList<short> samples)
    {
        var dataBytes = samples.Count * 2;
        var wav = new byte[44 + dataBytes];
        var span = wav.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4, 4), (uint)(36 + dataBytes));
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(22, 2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(24, 4), (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(28, 4), (uint)(sampleRate * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(32, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(34, 2), 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(40, 4), (uint)dataBytes);

        for (var i = 0; i < samples.Count; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(44 + (i * 2), 2), samples[i]);
        }

        return wav;
    }

    [Fact]
    public void 頭を読んで長さが出る()
    {
        var wav = MakeWav(24000, new short[24000]);

        Assert.True(WavInfo.TryRead(wav, out var format));
        Assert.NotNull(format);
        Assert.Equal(1, format!.Channels);
        Assert.Equal(24000, format.SampleRate);
        Assert.Equal(16, format.BitsPerSample);
        Assert.False(format.IsFloat);
        Assert.Equal(1.0, format.DurationSeconds, 3);
    }

    [Fact]
    public void RIFFでなければ読めない()
    {
        Assert.False(WavInfo.TryRead("not a wav at all"u8, out _));
        Assert.Null(WavInfo.DurationSeconds([]));
    }

    [Fact]
    public void 途中で切れた檔でも落ちない()
    {
        var wav = MakeWav(24000, new short[1000]);
        var truncated = wav.AsSpan(0, 100).ToArray();

        Assert.True(WavInfo.TryRead(truncated, out var format));
        Assert.NotNull(format);
        // data の宣言長ではなく、実際に在る分だけを長さにする
        Assert.Equal(100 - 44, format!.DataLength);
    }

    [Fact]
    public void 利得は目標のdBFSへ寄せる()
    {
        // 裁定 52＝再生時に −16 dBFS 相当へ揃える
        var quiet = Enumerable.Repeat(0.05f, 1000).ToArray();
        var gain = WavGain.ComputeGain(quiet);
        var after = WavGain.ToDbFs(WavGain.Rms(quiet) * gain);

        Assert.InRange(after, WavGain.TargetDbFs - 0.01, WavGain.TargetDbFs + 0.01);
    }

    [Fact]
    public void 持ち上げで割らせない()
    {
        // 山が 1.0 を超えるところで頭打ちにする（揃えるために割るのは本末転倒）
        var loud = new[] { 0.99f, -0.99f, 0.01f, 0.01f, 0.01f, 0.01f };
        var gain = WavGain.ComputeGain(loud);

        Assert.True(WavGain.Peak(loud) * gain <= 1.0 + 1e-9);
    }

    [Fact]
    public void 無音は触らない()
    {
        Assert.Equal(1.0, WavGain.ComputeGain(new float[100]));
        Assert.Equal(1.0, WavGain.ComputeGain(Array.Empty<float>()));
    }

    [Fact]
    public void 生バイトからも利得が出る()
    {
        var wav = MakeWav(24000, Enumerable.Repeat((short)1000, 2400).ToArray());
        Assert.True(WavGain.ComputeGainForWav(wav) > 1.0);
    }
}

/// <summary>鳴らさない再生器（ViewModel の試験の継ぎ目＝public コンストラクタ）。</summary>
internal sealed class FakeAudioPlayer : IAudioPlayer
{
    public bool IsPlaying { get; private set; }

    public int PlayCount { get; private set; }

    public byte[]? LastAudio { get; private set; }

    public string? LastFile { get; private set; }

    public PlaybackResult Result { get; set; } = new(true, 1.0, 1.0, null);

    public PlaybackResult Play(byte[] wav)
    {
        PlayCount++;
        LastAudio = wav;
        IsPlaying = Result.Ok;
        return Result;
    }

    public PlaybackResult PlayFile(string path)
    {
        LastFile = path;
        return Result;
    }

    public void Stop() => IsPlaying = false;

    public void Dispose() => IsPlaying = false;
}

public sealed class StatusViewModelTests
{
    private static StatusViewModel Create() =>
        new(static () => Task.CompletedTask, static () => Task.CompletedTask);

    [Fact]
    public void 状態は日本語で出る()
    {
        Assert.Equal("停止", StatusViewModel.StateLabel(ServerState.Stopped));
        Assert.Equal("起動中", StatusViewModel.StateLabel(ServerState.Starting));
        Assert.Equal("読込中", StatusViewModel.StateLabel(ServerState.Listening));
        Assert.Equal("待機", StatusViewModel.StateLabel(ServerState.Ready));
        Assert.Equal("暖機中", StatusViewModel.StateLabel(ServerState.Warming));
        Assert.Equal("失敗", StatusViewModel.StateLabel(ServerState.Failed));
    }

    [Fact]
    public void 走っている間は起動を押せない()
    {
        var vm = Create();
        Assert.True(vm.StartCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null));

        vm.ApplyState(ServerState.Ready, null);

        Assert.True(vm.IsRunning);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.True(vm.StopCommand.CanExecute(null));
    }

    [Fact]
    public void 失敗の理由は1行で残る()
    {
        // 受け入れ条件 D-1＝理由 1 行が UI に出る
        var vm = Create();
        vm.ApplyState(ServerState.Failed, "起動前の検査で止まった。");

        Assert.True(vm.IsFailed);
        Assert.Equal("起動前の検査で止まった。", vm.Reason);
    }

    [Fact]
    public void memoryの欄が無ければ未対応と出る()
    {
        // 便 C（2）が wrapper に足している最中＝無ければ無いものとして動く（裁定 67 ⑶）
        var vm = Create();
        vm.ApplyStatus(new StatusResponse { Runtime = new StatusRuntime { Loaded = true } });

        Assert.False(vm.MemorySupported);
        Assert.Equal(UiText.NotSupported, vm.MemoryText);
    }

    [Fact]
    public void memoryが返れば3つの値を出す()
    {
        var vm = Create();
        vm.ApplyStatus(new StatusResponse
        {
            Memory = new MemoryStatus
            {
                AllocatedBytes = 1073741824,
                ReservedBytes = 2147483648,
                MaxAllocatedBytes = 3221225472,
            },
        });

        Assert.True(vm.MemorySupported);
        Assert.Contains("1.00 GiB", vm.MemoryText, StringComparison.Ordinal);
        Assert.Contains("2.00 GiB", vm.MemoryText, StringComparison.Ordinal);
        Assert.Contains("3.00 GiB", vm.MemoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void 実測のdeviceを出す()
    {
        var vm = Create();
        vm.ApplyStatus(new StatusResponse
        {
            Device = new StatusDevice
            {
                Configured = "cuda:0",
                Actual = "cuda:0",
                Name = "AMD Radeon(TM) 8060S Graphics",
                GcnArch = "gfx1151",
                Precision = "bf16",
            },
        });

        Assert.Contains("cuda:0", vm.DeviceText, StringComparison.Ordinal);
        Assert.Contains("gfx1151", vm.DeviceText, StringComparison.Ordinal);
        Assert.Contains("bf16", vm.DeviceText, StringComparison.Ordinal);
    }

    [Fact]
    public void 暖機は進みつきで出る()
    {
        var vm = Create();
        vm.ApplyStatus(new StatusResponse
        {
            Warmup = new WarmupStatus { State = "running", ShotsDone = 2, ShotsTotal = 6 },
        });

        Assert.Contains("実行中", vm.WarmupText, StringComparison.Ordinal);
        Assert.Contains("2 / 6", vm.WarmupText, StringComparison.Ordinal);
    }

    [Fact]
    public void GPUはUUIDの下6桁で出る()
    {
        var vm = Create();
        vm.ApplySettings(new LauncherSettings
        {
            Variant = RuntimeVariants.RocmGfx1151,
            GpuName = "AMD Radeon(TM) 8060S Graphics",
            GpuUuid = "GPU-19adfe89-c9e0-df55-4a8a-e31798717a36",
            Port = 18094,
        });

        Assert.Contains("717a36", vm.GpuText, StringComparison.Ordinal);
        Assert.DoesNotContain("19adfe89", vm.GpuText, StringComparison.Ordinal);
        Assert.Equal("http://127.0.0.1:18094", vm.EndpointText);
    }

    [Fact]
    public void CPU変種はGPU未選択と言わない()
    {
        var vm = Create();
        vm.ApplySettings(new LauncherSettings { Variant = RuntimeVariants.Cpu });
        Assert.Contains("CPU", vm.GpuText, StringComparison.Ordinal);
    }

    [Fact]
    public void 概算メモリはwav参照の数で決まる()
    {
        // 便 D（2）＝主役は「1 名あたり」で、全員分は「同時に載せたときの上限」に落ちた
        // （裁定 67 ⑵・low 13）。上限の値は wav 参照 1 名分＝潜在と参照なしは 0 のまま。
        var vm = Create();
        vm.ApplyVoices(
        [
            new VoiceRow(VoiceIds.Default, "デフォルト", null, false, true, false, false, null),
            new VoiceRow("a", "a", "a.wav", false, false, false, false, null),
            new VoiceRow("b", "b", "b.wav", false, false, true, false, null),
        ]);

        Assert.Contains("全部を同時に載せたときの上限 "
            + UiText.Bytes(MemoryEstimate.WavReferenceBytes), vm.VoiceMemoryText, StringComparison.Ordinal);
    }

    [Fact]
    public void ログは末尾20行だけ持つ()
    {
        var vm = Create();
        for (var i = 0; i < 30; i++)
        {
            vm.AppendLog("行 " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Equal(20, vm.LogText.Split(Environment.NewLine).Length);
        Assert.DoesNotContain("行 9\r", vm.LogText, StringComparison.Ordinal);
        Assert.Contains("行 29", vm.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void 開発ビルドでは上流pinの突合を黙る()
    {
        // publish でだけ pin が焼かれる＝開発ビルドは食い違いを名乗らない
        Assert.Null(StatusViewModel.DescribeUpstreamMismatch(
            new StatusUpstream { IrodoriTts = "8224daf", Server = "841fb7c" }));
    }

    [Fact]
    public void 停止に戻ればサーバ由来の欄は消える()
    {
        var vm = Create();
        vm.ApplyStatus(new StatusResponse { Memory = new MemoryStatus { AllocatedBytes = 1 } });
        Assert.True(vm.MemorySupported);

        vm.ApplyState(ServerState.Stopped, null);

        Assert.False(vm.MemorySupported);
        Assert.Equal(UiText.Missing, vm.DeviceText);
    }
}

public sealed class TryViewModelTests
{
    private static TryViewModel Create(LauncherSettings? settings = null) =>
        new(static () => null, new FakeAudioPlayer(), settings ?? new LauncherSettings());

    [Fact]
    public void 初期値は設定から来る()
    {
        var vm = Create(new LauncherSettings { LastTestNumSteps = 10, LastTestVoice = "もち子さん" });
        Assert.Equal(10, vm.NumSteps);
        Assert.Equal("もち子さん", vm.SelectedVoice);
    }

    [Fact]
    public void 話者の候補を入れ替えても選択が残る()
    {
        var vm = Create();
        vm.SetVoices(
        [
            new VoiceRow(VoiceIds.Default, "デフォルト", null, false, true, false, false, null),
            new VoiceRow("もち子さん", "もち子さん", "a.wav", true, false, false, false, null),
        ]);
        vm.SelectedVoice = "もち子さん";

        vm.SetVoices(
        [
            new VoiceRow(VoiceIds.Default, "デフォルト", null, false, true, false, false, null),
            new VoiceRow("もち子さん", "もち子さん", "a.wav", true, false, false, false, null),
            new VoiceRow("琴葉茜", "琴葉茜", "b.wav", true, false, false, false, null),
        ]);

        Assert.Equal("もち子さん", vm.SelectedVoice);
        Assert.Equal(3, vm.Voices.Count);
    }

    [Fact]
    public void 消えた話者を選んでいたら先頭に戻る()
    {
        var vm = Create();
        vm.SetVoices([new VoiceRow("あ", "あ", "a.wav", false, false, false, false, null)]);
        vm.SelectedVoice = "あ";

        vm.SetVoices([new VoiceRow(VoiceIds.Default, "デフォルト", null, false, true, false, false, null)]);

        Assert.Equal(VoiceIds.Default, vm.SelectedVoice);
    }

    [Fact]
    public void 一覧が空でもデフォルトは残る()
    {
        var vm = Create();
        vm.SetVoices([]);
        Assert.Equal([VoiceIds.Default], vm.Voices);
    }

    [Fact]
    public void 本体が使っている間はしゃべらせるを押せず脇に1行出る()
    {
        // 決裁 130 Q4＝/ywk/status の requests.in_flight > 0 を MainViewModel が流し込む。
        var vm = Create();
        Assert.True(vm.SynthesizeCommand.CanExecute(null));
        Assert.False(vm.ShowConcurrency);
        Assert.Equal(string.Empty, vm.ConcurrencyText);

        vm.HostBusy = true;

        Assert.False(vm.SynthesizeCommand.CanExecute(null));
        Assert.True(vm.ShowConcurrency);
        Assert.Equal(
            "いま読み分けちゃん2 の読み上げに使われています。終わってからお試しください。",
            vm.ConcurrencyText);

        vm.HostBusy = false;

        Assert.True(vm.SynthesizeCommand.CanExecute(null));
        Assert.False(vm.ShowConcurrency);
        Assert.Equal(string.Empty, vm.ConcurrencyText);
    }

    [Fact]
    public async Task 自分の射が乗った標本を本体の使用と読まない()
    {
        // 決裁 130 Q4・v2-spec §2-1c＝ランチャ自身の〔しゃべらせる〕も同じ
        // POST /v1/audio/speech を撃って wrapper の同じ数に乗る。濾さないと
        // **自分で撃つたび**に、射の終わりから次の標本までの最大 2 秒だけ釦が死んで
        // 橙の枠に「読み分けちゃん2 が使っています」という嘘が出た（本体は 1 度も
        // 呼んでいないのに）。標本は射より遅れて届くので 1 回ぶん覚えて引く。
        var wrapper = new GatedWrapper();
        var vm = new TryViewModel(() => wrapper, new FakeAudioPlayer(), new LauncherSettings())
        {
            Input = "あ。",
        };

        var run = vm.SynthesizeCommand.ExecuteAsync();
        await wrapper.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        vm.HostBusy = true;  // ⑴ 射の最中に届いた標本＝自分の分
        wrapper.Gate.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(vm.HostBusy);
        Assert.False(vm.ShowConcurrency);
        Assert.Equal(string.Empty, vm.ConcurrencyText);
        Assert.True(vm.SynthesizeCommand.CanExecute(null));

        vm.HostBusy = true;  // ⑵ 射の直後の標本＝まだ自分の分（見張りは 2 秒遅れる）
        Assert.False(vm.HostBusy);
        Assert.False(vm.ShowConcurrency);
        Assert.True(vm.SynthesizeCommand.CanExecute(null));

        vm.HostBusy = true;  // ⑶ 次の標本＝本物（ここで初めて譲る）
        Assert.True(vm.HostBusy);
        Assert.True(vm.ShowConcurrency);
        Assert.False(vm.SynthesizeCommand.CanExecute(null));
    }

    [Fact]
    public void 橙の枠は常設をやめた()
    {
        // 常設だった注記（launcher/README §4 ⑧）は 1 文に短くなり、**出るのは
        // 本体の要求が走っている間だけ**＝ふだんは何も出さない（v2-spec §3-3）。
        var vm = Create();

        Assert.False(vm.ShowConcurrency);
        Assert.Equal(string.Empty, vm.ConcurrencyText);
        Assert.DoesNotContain("1 プロセス", TryViewModel.ConcurrencyNotice);
        Assert.StartsWith("いま読み分けちゃん2", TryViewModel.ConcurrencyNotice);
    }

    [Fact]
    public void 入力欄の空はnullとして扱う()
    {
        var vm = Create();
        vm.CfgScaleTextInput = "3.5";
        Assert.Equal(3.5, vm.CfgScaleText);

        vm.CfgScaleTextInput = "  ";
        Assert.Null(vm.CfgScaleText);

        // 読めない値は前の値を守る（黙って 0 にしない）
        vm.CfgScaleTextInput = "2";
        vm.CfgScaleTextInput = "あ";
        Assert.Equal(2.0, vm.CfgScaleText);
    }

    [Fact]
    public void 撃つ前の検分は純関数を通る()
    {
        var vm = Create();
        vm.Input = "  ";
        Assert.False(vm.BuildRequest().Ok);

        vm.Input = "こんにちは。";
        vm.SelectedVoice = VoiceIds.Default;
        var built = vm.BuildRequest();

        Assert.True(built.Ok);
        Assert.Equal("こんにちは。", built.Request!.Input);
        Assert.Equal(VoiceIds.Default, built.Request.Voice);
    }

    [Fact]
    public void 字数の表示は上限つき()
    {
        var vm = Create();
        vm.Input = "あいう";
        Assert.Equal("3 / 4096 字", vm.InputLengthText);
    }

    [Fact]
    public void 合成の前は保存できない()
    {
        var vm = Create();
        Assert.False(vm.HasAudio);
        Assert.False(vm.ReplayCommand.CanExecute(null));
    }

    [Fact]
    public void 保存の既定の檔名はwav()
    {
        Assert.EndsWith(".wav", Create().SuggestedFileName(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 門を開けるまで返らない合成（実 HTTP には触れない＝<c>RoundTwoUiTests.BlockingWrapper</c>
    /// と同じ形だが、こちらは<b>開けたら 200 で返る</b>＝射の「終わり」まで見る。
    /// </summary>
    private sealed class GatedWrapper : IWrapperClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Uri BaseAddress { get; } = new("http://127.0.0.1:18099/");

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

        public async Task<SpeechResult> SynthesizeAsync(
            SpeechRequest request, TimeSpan timeout, System.Threading.CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Gate.Task.ConfigureAwait(false);
            return new SpeechResult(
                true,
                WavTests.MakeWav(24000, new short[2400]),
                "audio/wav",
                1234,
                200,
                null,
                TimeSpan.FromMilliseconds(1));
        }

        public Task<HealthResponse> GetHealthAsync(System.Threading.CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<StatusResponse>> GetStatusAsync(System.Threading.CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<ParamsResponse>> GetParamsAsync(System.Threading.CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<VoicesResponse>> GetVoicesAsync(System.Threading.CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<VoicesResponse>> GetOpenAiVoicesAsync(System.Threading.CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<WarmupStartResult>> StartWarmupAsync(
            WarmupRequest request, System.Threading.CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<CancelResult>> CancelWarmupAsync(
            string id, System.Threading.CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<PrecomputeStartResult>> StartPrecomputeAsync(
            PrecomputeRequest request, System.Threading.CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<CancelResult>> CancelPrecomputeAsync(
            string id, System.Threading.CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<DropLatentResult>> DropLatentAsync(
            string voiceId, System.Threading.CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-l3-" + Guid.NewGuid().ToString("N"));

    private SettingsViewModel Create(LauncherSettings settings, out ISettingsStore store)
    {
        Directory.CreateDirectory(Path.Combine(_root, "app", "ledger"));
        foreach (var name in new[] { "runtime-cu130", "runtime-cpu", "models", "python-embed" })
        {
            File.WriteAllText(Path.Combine(_root, "app", "ledger", name + ".json"), "{}");
        }

        var paths = new AppPaths(
            Path.Combine(_root, "install"),
            Path.Combine(_root, "app"),
            Path.Combine(_root, "runtime"),
            Path.Combine(_root, "data"),
            developerMode: true);

        store = new JsonSettingsStore(paths.SettingsPath);
        return new SettingsViewModel(settings, store, paths, null, new DriverRequirement());
    }

    [Fact]
    public void 台帳のある変種だけを出す()
    {
        var vm = Create(new LauncherSettings(), out _);
        Assert.Equal([RuntimeVariants.Cu130, RuntimeVariants.Cpu], vm.VariantChoices);
        Assert.Equal(ReleaseFlavor.Cuda, vm.Flavor);
    }

    [Fact]
    public void 適用するまで本物は動かない()
    {
        var live = new LauncherSettings { Port = LauncherSettings.DefaultPort };
        var vm = Create(live, out var store);

        vm.Port = 18094;

        Assert.True(vm.IsDirty);
        Assert.Equal(LauncherSettings.DefaultPort, live.Port);

        vm.Apply();

        Assert.False(vm.IsDirty);
        Assert.Equal(18094, live.Port);
        Assert.Equal(18094, store.Load().Port);
    }

    [Fact]
    public void 取り消しは写しを捨てる()
    {
        var live = new LauncherSettings { EmptyCacheInterval = 0 };
        var vm = Create(live, out _);

        vm.EmptyCacheInterval = 5;
        vm.Revert();

        Assert.Equal(0, vm.EmptyCacheInterval);
        Assert.Equal(0, live.EmptyCacheInterval);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void 綴り違いの暖機の段は保存させない()
    {
        var live = new LauncherSettings();
        var vm = Create(live, out _);

        vm.WarmupStagesInput = "4, あ";
        vm.Apply();

        Assert.True(vm.IsDirty);
        Assert.NotEmpty(vm.Message);
        Assert.Equal([4.0, 8.0, 12.0], live.WarmupStages);
    }

    [Fact]
    public void 範囲外のポートは保存させない()
    {
        var live = new LauncherSettings();
        var vm = Create(live, out _);

        vm.Port = 0;
        vm.Apply();

        Assert.True(vm.IsDirty);
        Assert.Equal(LauncherSettings.DefaultPort, live.Port);
    }

    [Fact]
    public void 台帳の無い変種は保存させない()
    {
        var live = new LauncherSettings();
        var vm = Create(live, out _);

        vm.Variant = RuntimeVariants.Cu126; // 台帳を置いていない
        vm.Apply();

        Assert.True(vm.IsDirty);
        Assert.Equal(RuntimeVariants.Cu130, live.Variant);
    }

    [Fact]
    public void Radeon変種では精度を触らせない()
    {
        // 裁定 5・36＝rocm に fp32 を載せると wrapper が exit 2 で止まる
        var vm = Create(new LauncherSettings { Variant = RuntimeVariants.RocmGfx1151 }, out _);
        Assert.False(vm.PrecisionEnabled);
        Assert.Contains("bf16", vm.PrecisionNote, StringComparison.Ordinal);
    }

    [Fact]
    public void 精度の既定はdevice連動で欄を出さない()
    {
        var live = new LauncherSettings();
        var vm = Create(live, out _);

        vm.PrecisionChoice = "fp16";
        Assert.Equal("fp16", live.Precision is null ? vm.PrecisionChoice : vm.PrecisionChoice);

        vm.PrecisionChoice = SettingsViewModel.PrecisionChoices[0];
        vm.Apply();
        Assert.Null(live.Precision);
    }

    [Fact]
    public void 事前計算は3状態で持つ()
    {
        // null＝変種の既定・true＝必ず焼く・false＝焼かない（裁定 65）
        var vm = Create(new LauncherSettings { Variant = RuntimeVariants.RocmGfx1151 }, out _);

        Assert.Null(vm.PrecomputeOnStart);
        Assert.Contains("ON", vm.PrecomputeNote, StringComparison.Ordinal);

        vm.PrecomputeOnStart = false;
        Assert.Contains("OFF", vm.PrecomputeNote, StringComparison.Ordinal);
    }

    [Fact]
    public void 写しの複写は設定頁の欄だけを移す()
    {
        // 是正・便 D（3）の 3 巡目（high）＝写しはウィザードより**前**に採られるので、
        // ウィザードと他画面が持ち主の欄まで移すと「適用」1 押しでそれが巻き戻る。
        var from = new LauncherSettings
        {
            GpuUuid = "GPU-1",
            GpuName = "g",
            Variant = RuntimeVariants.Cpu,
            Port = 18099,
            WarmupVoices = ["a"],
            VoiceOrder = ["b"],
            LastTestVoice = "c",
        };
        var to = new LauncherSettings
        {
            VoiceOrder = ["z"],
            LastTestVoice = "z",
            FirstRunCompleted = true,
            AcceptedNoticesSha256 = "sha",
        };
        to.SetRuntimeStamp(RuntimeVariants.Cpu, "0123456789abcdef", "v0.1.0");

        SettingsViewModel.CopyInto(from, to);

        // 設定頁の欄は移る
        Assert.Equal("GPU-1", to.GpuUuid);
        Assert.Equal(RuntimeVariants.Cpu, to.Variant);
        Assert.Equal(18099, to.Port);
        Assert.Equal(["a"], to.WarmupVoices);
        // 参照ごと差し替えない（他の画面が握っている個体を保つ）
        Assert.NotSame(from.WarmupVoices, to.WarmupVoices);

        // 他の持ち主の欄は**動かない**
        Assert.Equal(["z"], to.VoiceOrder);
        Assert.Equal("z", to.LastTestVoice);
        Assert.True(to.FirstRunCompleted);
        Assert.Equal("sha", to.AcceptedNoticesSha256);
        Assert.Equal("0123456789abcdef", to.RuntimeLedgerFor(RuntimeVariants.Cpu));
        Assert.Equal("v0.1.0", to.InstalledAppVersionFor(RuntimeVariants.Cpu));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 試験の後始末が失敗しても試験の結果は変えない
        }
    }
}

public sealed class FirstRunViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-l3-fr-" + Guid.NewGuid().ToString("N"));

    private AppPaths MakePaths()
    {
        Directory.CreateDirectory(Path.Combine(_root, "app", "ledger"));
        Directory.CreateDirectory(Path.Combine(_root, "app", "licenses"));
        return new AppPaths(
            Path.Combine(_root, "install"),
            Path.Combine(_root, "app"),
            Path.Combine(_root, "runtime"),
            Path.Combine(_root, "data"),
            developerMode: true);
    }

    private FirstRunViewModel Create(LauncherSettings? settings = null) =>
        NewWizard(MakePaths(), settings ?? new LauncherSettings());

    private static FirstRunViewModel NewWizard(AppPaths paths, LauncherSettings settings) =>
        new(
            paths,
            settings,
            new JsonSettingsStore(paths.SettingsPath),
            new DriverRequirement(),
            static () => null,
            static () => null,
            static _ => Task.FromResult(false));

    [Fact]
    public void 最初の段は通知で同意するまで進めない()
    {
        // 裁定 46＝取得を始める前に通知を出して同意を取る
        var paths = MakePaths();
        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文");
        var vm = NewWizard(paths, new LauncherSettings());

        Assert.Equal(FirstRunStep.Notices, vm.Step);
        Assert.False(vm.NextCommand.CanExecute(null));

        vm.Accepted = true;
        Assert.True(vm.NextCommand.CanExecute(null));
    }

    [Fact]
    public void 通知文が読めなければ同意そのものができない()
    {
        // 所見 15 の釘＝檔が無いまま印を付けられると acceptedNoticesSha256 に null が残り、
        // LGPL-2.1 の libsndfile の通知（裁定 28）を出さずに取得へ進む路ができる。
        var settings = new LauncherSettings();
        var vm = Create(settings);

        Assert.Null(vm.NoticesSha256);
        Assert.False(vm.CanAcceptNotices);

        vm.Accepted = true;                                  // 画面では印を付けられない束縛だが、
        Assert.False(vm.NextCommand.CanExecute(null));       // 仮に付いても次へは進めない
    }

    [Fact]
    public void 通知文が無ければその旨を出す()
    {
        var vm = Create();
        Assert.Contains("見つかりません", vm.NoticesText, StringComparison.Ordinal);
        Assert.Null(vm.NoticesSha256);
    }

    [Fact]
    public void 通知文があればsha256を採る()
    {
        var paths = MakePaths();
        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文");

        var vm = new FirstRunViewModel(
            paths,
            new LauncherSettings(),
            new JsonSettingsStore(paths.SettingsPath),
            new DriverRequirement(),
            static () => null,
            static () => null,
            static _ => Task.FromResult(false));

        Assert.Equal("通知の本文", vm.NoticesText);
        Assert.NotNull(vm.NoticesSha256);
        Assert.Equal(64, vm.NoticesSha256!.Length);
    }

    [Fact]
    public async Task 同意したら変種の段へ進む()
    {
        var settings = new LauncherSettings();
        var paths = MakePaths();
        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文");
        var vm = NewWizard(paths, settings);
        vm.Accepted = true;

        await vm.NextAsync();

        Assert.Equal(FirstRunStep.Variant, vm.Step);
        Assert.Single(vm.Trail);
        Assert.NotNull(settings.AcceptedNoticesSha256);
    }

    [Fact]
    public void 見せる段の題は4つに畳まれる()
    {
        // v2.0 段 B（`v2-copy.md` §2）＝内部の 7 段は据え置きで、**題だけ**が 4 つになる。
        Assert.Equal("お知らせ", FirstRunViewModel.Title(FirstRunStep.Notices));
        Assert.Equal("これからすること", FirstRunViewModel.Title(FirstRunStep.Variant));
        Assert.Equal("使えます。", FirstRunViewModel.Title(FirstRunStep.Done));

        // 働く 4 段は 1 つの題に見える（どこで止まったかは PhaseText と Trail が残す）。
        foreach (var step in new[]
                 {
                     FirstRunStep.Download, FirstRunStep.Install,
                     FirstRunStep.Models, FirstRunStep.Start,
                 })
        {
            Assert.Equal("準備しています", FirstRunViewModel.Title(step));
        }

        // 記録の側は内輪の 7 つのまま（詳細の中・ログ）。
        Assert.Equal("取得（実行系）", FirstRunViewModel.TrailTitle(FirstRunStep.Download));
        Assert.Equal("取得（モデル）", FirstRunViewModel.TrailTitle(FirstRunStep.Models));
    }

    [Fact]
    public void 段番号は3つ組で完了は番号を持たない()
    {
        var paths = MakePaths();
        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文");
        var vm = NewWizard(paths, new LauncherSettings());

        Assert.Equal(3, vm.VisibleStepCount);
        Assert.Equal("1 / 3", vm.StepNumberText);
        Assert.Equal(2, FirstRunViewModel.VisibleStepNumber(FirstRunStep.Variant, noticesSkipped: false));

        foreach (var step in new[]
                 {
                     FirstRunStep.Download, FirstRunStep.Install,
                     FirstRunStep.Models, FirstRunStep.Start,
                 })
        {
            Assert.Equal(3, FirstRunViewModel.VisibleStepNumber(step, noticesSkipped: false));
        }

        Assert.Equal(0, FirstRunViewModel.VisibleStepNumber(FirstRunStep.Done, noticesSkipped: false));
    }

    [Fact]
    public void 同じ版のお知らせは二度と訊かない()
    {
        // 決裁 130 Q3＝sha256 が一致する回は段 1 を飛ばし、見せる段は 2 つになる。
        // **acceptedNoticesSha256 は読むだけ**（上書きしない）。
        var paths = MakePaths();
        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文");
        var first = NewWizard(paths, new LauncherSettings());
        Assert.False(first.NoticesAlreadyAccepted);
        Assert.False(first.NoticesSkipped);

        var settings = new LauncherSettings { AcceptedNoticesSha256 = first.NoticesSha256 };
        var again = NewWizard(paths, settings);

        Assert.True(again.NoticesAlreadyAccepted);
        Assert.True(again.NoticesSkipped);
        Assert.Equal(FirstRunStep.Variant, again.Step);
        Assert.Equal(2, again.VisibleStepCount);
        Assert.Equal("1 / 2", again.StepNumberText);

        // 飛ばした段へは「戻る」で降りられない（同意はもう済んでいる）。
        Assert.False(again.BackCommand.CanExecute(null));
        again.Back();
        Assert.Equal(FirstRunStep.Variant, again.Step);
        Assert.Equal(first.NoticesSha256, settings.AcceptedNoticesSha256);
    }

    [Fact]
    public void 釦の札は決裁130の4押下に揃っている()
    {
        // 押下 4＝チェック → 次へ → 準備を始める → しゃべらせてみる（`v2-plan.md` 段 B-4）。
        var paths = MakePaths();
        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文");
        var vm = NewWizard(paths, new LauncherSettings());

        Assert.Equal("次へ", vm.NextButtonText);
        vm.Accepted = true;

        var settings = new LauncherSettings { AcceptedNoticesSha256 = vm.NoticesSha256 };
        var atVariant = NewWizard(paths, settings);
        Assert.Equal("準備を始める", atVariant.NextButtonText);
    }

    [Fact]
    public void 動かし方を決めた1行は選ばせずに名乗る()
    {
        // 決裁 130 Q1＝判定は VariantRecommendation.Recommend のまま・ここは名乗るだけ。
        // **頭の 1 文（アプリが決めた）を落とさない**（`v2-copy.md` §2 段 2 の逐語）＝
        // 決裁 130 Q1 が画面に届けたい 1 文はこれである。
        Assert.Equal(
            "このパソコンに合わせて、動かし方を選びました。"
            + "NVIDIA の GPU（GeForce RTX 3090）・CUDA 13.0 で動かします。",
            FirstRunViewModel.DecisionLineFor(RuntimeVariants.Cu130, "GeForce RTX 3090"));

        Assert.Equal(
            FirstRunViewModel.DecisionLead + "AMD の Radeon（Radeon 8060S）・ROCm で動かします。",
            FirstRunViewModel.DecisionLineFor(RuntimeVariants.RocmGfx1151, "Radeon 8060S"));

        // 版が古い機体は、勧め直した理由（下限の数字）まで 1 行で名乗る（`v2-copy.md` §2 段 2）。
        var cu126 = FirstRunViewModel.DecisionLineFor(RuntimeVariants.Cu126, "GeForce RTX 3090");
        Assert.StartsWith(FirstRunViewModel.DecisionLead, cu126, StringComparison.Ordinal);
        Assert.Contains("CUDA 12.6 で動かします", cu126, StringComparison.Ordinal);
        Assert.Contains(
            DriverRequirement.Minimum(RuntimeVariants.Cu130)!, cu126, StringComparison.Ordinal);

        // 製品名が読めない回は括弧ごと落とす（推測の名前を出さない）。
        Assert.Equal(
            FirstRunViewModel.DecisionLead + "NVIDIA の GPU・CUDA 13.0 で動かします。",
            FirstRunViewModel.DecisionLineFor(RuntimeVariants.Cu130, null));

        // CPU を名乗るのは GPU が見つからなかった機体だけ（憲章 §7 既定）。
        Assert.Equal(
            FirstRunViewModel.NoGpuDecisionLine,
            FirstRunViewModel.DecisionLineFor(RuntimeVariants.Cpu, "Intel UHD"));
    }

    /// <summary>
    /// <b>cpu は「GPU が無い」だけの綴りではない</b>（是正・検分）＝
    /// <see cref="VariantRecommendation.Recommend"/> は<b>版が読めていて下限未満</b>の機体でも
    /// cpu を返す（GeForce が在るのに 470.00 のまま、など）。そこで「対応する GPU が
    /// 見つかりませんでした」と告げると、画面が事実の逆を名乗る。
    /// </summary>
    [Fact]
    public void ドライバが古くてcpuに落ちた回は数字ごと名乗る()
    {
        var probe = new DriverProbe("470.00", GpuCount: 1, Probed: true, null, "NVIDIA GeForce RTX 3090");

        // 台帳の並びは配布物のまま（cu130／cu126／cpu）＝判定は 1 つも足していない。
        Assert.Equal(
            RuntimeVariants.Cpu,
            VariantRecommendation.Recommend(
                [RuntimeVariants.Cu130, RuntimeVariants.Cu126, RuntimeVariants.Cpu], probe));

        var line = FirstRunViewModel.DecisionLineFor(
            RuntimeVariants.Cpu, "NVIDIA GeForce RTX 3090", probe);

        Assert.NotEqual(FirstRunViewModel.NoGpuDecisionLine, line);
        Assert.Contains("470.00", line, StringComparison.Ordinal);
        Assert.Contains(
            DriverRequirement.Minimum(RuntimeVariants.Cu126)!, line, StringComparison.Ordinal);
        Assert.Contains("NVIDIA GeForce RTX 3090", line, StringComparison.Ordinal);

        // 見た上で 0 台だった機体は、これまでどおり「GPU が見つかりませんでした」。
        Assert.Equal(
            FirstRunViewModel.NoGpuDecisionLine,
            FirstRunViewModel.DecisionLineFor(
                RuntimeVariants.Cpu, null, new DriverProbe(null, 0, Probed: true)));
    }

    /// <summary>
    /// <b>判らない回は決めたと言わない</b>（是正・検分）＝<c>nvidia-smi</c> が無い・落ちた・
    /// まだ撃っていない回、<see cref="VariantRecommendation.Recommend"/> は
    /// 「判らないことを勝手に決めない」で<b>並びの先頭</b>（cu130）を返す。
    /// それを「このパソコンは CUDA 13.0 です」と名乗ると、5.3 GB 落としたあと門に断られる。
    /// </summary>
    [Fact]
    public void 検分できなかった回は決めたと言わない()
    {
        foreach (var probe in new[]
                 {
                     DriverProbe.Unknown,                                          // まだ撃っていない
                     new DriverProbe(null, 0, Probed: true, "nvidia-smi が無い"),   // 理由つきの 0 台
                 })
        {
            var line = FirstRunViewModel.DecisionLineFor(RuntimeVariants.Cu130, null, probe);

            Assert.DoesNotContain(FirstRunViewModel.DecisionLead, line, StringComparison.Ordinal);
            Assert.Contains("確かめられませんでした", line, StringComparison.Ordinal);
            Assert.Contains(BandText.VariantName(RuntimeVariants.Cu130), line, StringComparison.Ordinal);
        }

        // 版が読めた回は、これまでどおり結果を名乗る。
        Assert.StartsWith(
            FirstRunViewModel.DecisionLead,
            FirstRunViewModel.DecisionLineFor(
                RuntimeVariants.Cu130, null, new DriverProbe("580.00", 1, Probed: true)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void いま何をしているかの1行は段ごとに1つ()
    {
        Assert.Equal("落とした物を組み立てています。", FirstRunViewModel.PhaseLine(FirstRunStep.Install));
        Assert.Equal("声のデータをダウンロードしています。", FirstRunViewModel.PhaseLine(FirstRunStep.Models));
        Assert.Equal("動くか確かめています。", FirstRunViewModel.PhaseLine(FirstRunStep.Start));

        // 通知・確認・完了は「いま何をしているか」を持たない。
        Assert.Equal(string.Empty, FirstRunViewModel.PhaseLine(FirstRunStep.Notices));
        Assert.Equal(string.Empty, FirstRunViewModel.PhaseLine(FirstRunStep.Done));

        // **数は添えない**（是正・検分＝`v2-spec.md` §1-3）＝利用者向けの面に出す数は
        // 割合・丸めた残り・散文の総量の 3 つだけで、実測は詳細の中に GiB のまま残る。
        var line = FirstRunViewModel.PhaseLine(FirstRunStep.Download);

        Assert.Equal("必要な部品をダウンロードしています。", line);
        Assert.DoesNotContain("GB", line, StringComparison.Ordinal);
    }

    [Fact]
    public void 台帳が読めなければ見積りは不明と名乗る()
    {
        // 推測の数字を出さない（裁定 18 の作法）
        var text = FirstRunViewModel.EstimateSizeText(MakePaths(), RuntimeVariants.Cu130);
        Assert.Contains("不明", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 台帳のsizeの総和で見積る()
    {
        var paths = MakePaths();
        File.WriteAllText(
            paths.LedgerPath("runtime-cpu"),
            """
            {"schema":1,"items":[
              {"kind":"wheel","name":"a","url":"u","size":1048576,
               "sha256":"0000000000000000000000000000000000000000000000000000000000000000"},
              {"kind":"wheel","name":"b","url":"u","size":1048576,
               "sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]}
            """);

        // 見積りは FetchPlanner の計画そのもの＝取得の総量と**必要な空き**を出す
        // （是正・2026-09-05＝以前は台帳 2 檔の総和だけで、空き容量は 1 度も出なかった）。
        File.WriteAllText(
            paths.LedgerPath("python-embed"),
            """
            {"schema":1,"items":[
              {"kind":"python-embed","name":"python","url":"u","size":1048576,
               "sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]}
            """);

        var text = FirstRunViewModel.EstimateSizeText(paths, RuntimeVariants.Cpu);

        Assert.Contains("取得 3.0 MiB", text, StringComparison.Ordinal);
        Assert.Contains("必要な空き", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 見せた見積りと注文する檔が同じ計画から出る()
    {
        // 所見 12 の釘＝画面の数と DownloadRequest が定義上ずれない
        var paths = MakePaths();
        File.WriteAllText(
            paths.LedgerPath("python-embed"),
            """
            {"schema":1,"items":[
              {"kind":"python-embed","name":"python","url":"u","size":1048576,
               "sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]}
            """);
        File.WriteAllText(
            paths.LedgerPath("runtime-cpu"),
            """
            {"schema":1,"items":[
              {"kind":"wheel","name":"a","url":"u","size":1048576,
               "sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]}
            """);
        File.WriteAllText(
            paths.LedgerPath("vc_redist"),
            """
            {"schema":1,"name":"vc-redist","items":[
              {"kind":"installer","name":"vc_redist.x64","url":"u","size":1048576,
               "sha256":"0000000000000000000000000000000000000000000000000000000000000000",
               "silent_args":["/install","/quiet","/norestart"]}]}
            """);

        var plan = FirstRunViewModel.TryPlan(paths, RuntimeVariants.Cpu, skipVcRedist: false);
        Assert.NotNull(plan);
        Assert.Equal(3, IrodoriTtsYwk.Launcher.Services.Ledger.FetchPlanner
            .ToDownloadRequests(plan!, paths.DownloadCacheDir).Count);

        // vc_redist が要らない機体では計画からも注文からも消える
        var skipped = FirstRunViewModel.TryPlan(paths, RuntimeVariants.Cpu, skipVcRedist: true);
        Assert.NotNull(skipped);
        Assert.Equal(2, IrodoriTtsYwk.Launcher.Services.Ledger.FetchPlanner
            .ToDownloadRequests(skipped!, paths.DownloadCacheDir).Count);
    }

    [Fact]
    public void 進捗の1行はbytesとETAを持つ()
    {
        // 受け入れ条件 D-5＝進捗（bytes／ETA）
        var line = FirstRunViewModel.Describe(new DownloadProgress(
            "torch",
            DownloadPhase.Downloading,
            BytesReceived: 1048576,
            TotalBytes: 2097152,
            BytesPerSecond: 1048576,
            Eta: TimeSpan.FromSeconds(1),
            Attempt: 2,
            Url: "https://example.invalid/a"));

        Assert.Contains("取得", line, StringComparison.Ordinal);
        Assert.Contains("torch", line, StringComparison.Ordinal);
        Assert.Contains("1.0 MiB / 2.0 MiB", line, StringComparison.Ordinal);
        Assert.Contains("残り 1 秒", line, StringComparison.Ordinal);
        Assert.Contains("2 回目", line, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 同上
        }
    }
}

public sealed class MvvmTests
{
    private sealed class Probe : ObservableObject
    {
        private int _value;

        public int Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }
    }

    [Fact]
    public void 同じ値なら通知しない()
    {
        var probe = new Probe();
        var count = 0;
        probe.PropertyChanged += (_, _) => count++;

        probe.Value = 1;
        probe.Value = 1;
        probe.Value = 2;

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task 走っている間は押せない()
    {
        var gate = new TaskCompletionSource();
        var command = new AsyncRelayCommand(() => gate.Task);

        var running = command.ExecuteAsync();

        Assert.True(command.IsRunning);
        Assert.False(command.CanExecute(null));

        gate.SetResult();
        await running;

        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task 失敗は握り潰さずに理由で出る()
    {
        string? reason = null;
        var command = new AsyncRelayCommand(
            static () => throw new InvalidOperationException("駄目でした"));
        command.Faulted += (_, message) => reason = message;

        await command.ExecuteAsync();

        Assert.Equal("駄目でした", reason);
    }

    [Fact]
    public async Task 取消は失敗にしない()
    {
        string? reason = null;
        var command = new AsyncRelayCommand(
            static () => throw new OperationCanceledException());
        command.Faulted += (_, message) => reason = message;

        await command.ExecuteAsync();

        Assert.Null(reason);
    }

    [Fact]
    public void 可否は明示で引き直す()
    {
        var enabled = false;
        var command = new RelayCommand(static () => { }, () => enabled);
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        Assert.False(command.CanExecute(null));
        enabled = true;
        command.RaiseCanExecuteChanged();

        Assert.True(command.CanExecute(null));
        Assert.Equal(1, raised);
    }
}

public sealed class AboutViewModelTests
{
    [Fact]
    public void 冒頭に非公式と出す()
    {
        // 裁定 1＝README 冒頭と同じ文言
        Assert.Contains("非公式", AboutViewModel.Disclaimer, StringComparison.Ordinal);
        Assert.Contains("Aratako", AboutViewModel.Disclaimer, StringComparison.Ordinal);
    }

    [Fact]
    public void 透かしは切れないと明記する()
    {
        // 裁定 9＝既定 ON・切る経路を持たない
        Assert.Contains("切る経路はありません", AboutViewModel.WatermarkNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void 開発ビルドはそう名乗る()
    {
        Assert.Contains("開発ビルド", AboutViewModel.UpstreamText, StringComparison.Ordinal);
        Assert.StartsWith("版 ", AboutViewModel.VersionText, StringComparison.Ordinal);
    }

    [Fact]
    public void ライセンスの束が無くても落ちない()
    {
        var paths = new AppPaths(@"C:\x", @"C:\x\nowhere", @"C:\x\rt", @"C:\x\data", false);
        Assert.Empty(new AboutViewModel(paths).LicenseFolders());
    }
}

/// <summary>
/// 決裁 130 Q4 の<b>配り道</b>＝見張りの標本（<c>/ywk/status</c>）が〔しゃべらせる〕の
/// 可否まで届くか。<b>新しい問い合わせは足していない</b>ので、届かなければ釦は永久に
/// 押せるまま（＝配信の読み上げを待たせる）か、永久に押せない（＝退行）かのどちらかになる。
/// </summary>
[Collection(AppServicesCollection.Name)]
public sealed class Decision130HostBusyWiringTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-d-hostbusy-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void 標本のin_flightがしゃべらせるの可否まで届く()
    {
        var paths = CorrectionThreeTree.MakePaths(_root);
        AppServices.Server = new CorrectionThreeTree.QuietServer();
        AppServices.GpuEnumerator = null;
        var main = new MainViewModel(
            paths,
            new LauncherSettings(),
            new JsonSettingsStore(paths.SettingsPath),
            new CorrectionThreeTree.SilentPlayer());

        main.ApplyStatusSample(Sample(1));
        Assert.True(main.Try.HostBusy);
        Assert.False(main.Try.SynthesizeCommand.CanExecute(null));
        Assert.True(main.Try.ShowConcurrency);

        main.ApplyStatusSample(Sample(0));
        Assert.False(main.Try.HostBusy);
        Assert.True(main.Try.SynthesizeCommand.CanExecute(null));

        // 欄の無い個体（v1.1.0 以前の wrapper）＝0 と読む＝押せるまま。
        main.ApplyStatusSample(new StatusResponse());
        Assert.False(main.Try.HostBusy);

        // 標本が無い回（止まっている・口が無い）も押せない理由を残さない。
        main.ApplyStatusSample(Sample(1));
        main.ApplyStatusSample(null);
        Assert.False(main.Try.HostBusy);
    }

    private static StatusResponse Sample(int inFlight) => new()
    {
        Engine = "irodori-ywk",
        Runtime = new StatusRuntime { Loaded = true, Loading = false },
        Requests = new StatusRequests { InFlight = inFlight },
    };

    public void Dispose()
    {
        AppServices.Reset();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
