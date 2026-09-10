using System;
using System.Collections.Generic;
using System.Globalization;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>試し撃ちの入力 1 組（画面の欄をそのまま写した器・null＝未指定）。</summary>
/// <param name="Input">本文（1〜4096 字）。</param>
/// <param name="Voice">話者 id（「デフォルト」を含む）。</param>
/// <param name="NumSteps">サンプリング歩数（UI プリセット 10／40＝裁定 10）。</param>
/// <param name="CfgScaleText">本文の従い方。</param>
/// <param name="CfgScaleCaption">演技指示の従い方（既定 3.0＝裁定 33）。</param>
/// <param name="CfgScaleSpeaker">話者性の従い方。</param>
/// <param name="Caption">演技指示（空文字・空白のみ＝未指定＝裁定 48）。</param>
/// <param name="Seed">乱数の種（空文字＝未指定）。</param>
/// <param name="Speed">読み速さ（0.25〜4.0）。</param>
public sealed record TryShot(
    string Input,
    string? Voice = null,
    int? NumSteps = null,
    double? CfgScaleText = null,
    double? CfgScaleCaption = null,
    double? CfgScaleSpeaker = null,
    string? Caption = null,
    string? Seed = null,
    double? Speed = null);

/// <summary>試し撃ちの検分の結末。</summary>
/// <param name="Ok">撃ってよいか。</param>
/// <param name="Request">撃つ body（駄目なら null）。</param>
/// <param name="FailureReason">撃てない理由 1 行（画面に出す）。</param>
public sealed record TryShotBuildResult(bool Ok, SpeechRequest? Request, string? FailureReason);

/// <summary>
/// 試し撃ちの欄から <c>POST /v1/audio/speech</c> の body を組む（<b>純関数</b>）。
/// <para>
/// 契約 ⑶ の 4 つの約束をここ 1 箇所で守る。
/// <list type="number">
/// <item><b>「既定に戻す」は欄を出さないことで表す</b>＝null の欄は <c>irodori</c> に載せない。
/// 上流は <c>extra="allow"</c> で範囲検査ゼロなので、載せた値は必ず効いてしまう。</item>
/// <item><b>空文字・空白のみの <c>caption</c>／<c>seed</c> は未指定に畳む</b>（裁定 48）。
/// <c>/params</c> が <c>caption</c> の既定を <c>""</c> と名乗る以上、その値を送り返して
/// 400 になってはならない。</item>
/// <item><b><c>speed</c> と <c>irodori.duration_scale</c> は同時に送らない</b>（除算で合成される）＝
/// 試し撃ちは <c>speed</c> だけを使い、<c>duration_scale</c> は載せない。</item>
/// <item><b><c>irodori.seconds</c> は暖機の射だけ</b>＝ここでは決して載せない。</item>
/// </list>
/// </para>
/// <para>
/// 検査（本文の長さ・歩数の範囲・seed が数か）は<b>撃つ前に 0 s で弾く</b>。上流は
/// <c>num_steps:-5</c> も黙って通すので、UI が最後の砦である。
/// </para>
/// </summary>
public static class SpeechRequestBuilder
{
    /// <summary>本文の上限（契約 ⑶ 3-1）。</summary>
    public const int MaxInputLength = 4096;

    /// <summary>演技指示の上限（<c>/params</c> の <c>max_length</c>）。</summary>
    public const int MaxCaptionLength = 2048;

    /// <summary>歩数の範囲（上流 gradio のスライダ＝UI の範囲であって検査ではない）。</summary>
    public const int MinNumSteps = 1;

    /// <summary>同上。</summary>
    public const int MaxNumSteps = 120;

    /// <summary>UI のプリセット（裁定 10＝10 は速い・40 は上流既定）。</summary>
    public static readonly int[] NumStepsPresets = [10, 40];

    /// <summary>読み速さの範囲（契約 ⑶ 3-1）。</summary>
    public const double MinSpeed = 0.25;

    /// <summary>同上。</summary>
    public const double MaxSpeed = 4.0;

    /// <summary>組む。撃てない理由があれば <see cref="TryShotBuildResult.FailureReason"/> に 1 行。</summary>
    public static TryShotBuildResult Build(TryShot shot)
    {
        ArgumentNullException.ThrowIfNull(shot);

        var input = shot.Input ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return new TryShotBuildResult(false, null, "本文を入れてください。");
        }

        if (input.Length > MaxInputLength)
        {
            return new TryShotBuildResult(
                false,
                null,
                "本文が長すぎます（" + input.Length.ToString(CultureInfo.InvariantCulture)
                + " 字／上限 " + MaxInputLength.ToString(CultureInfo.InvariantCulture) + " 字）。");
        }

        if (shot.NumSteps is int steps && (steps < MinNumSteps || steps > MaxNumSteps))
        {
            return new TryShotBuildResult(
                false,
                null,
                "サンプリング歩数は " + MinNumSteps.ToString(CultureInfo.InvariantCulture)
                + "〜" + MaxNumSteps.ToString(CultureInfo.InvariantCulture) + " です。");
        }

        if (shot.Speed is double speed && (speed < MinSpeed || speed > MaxSpeed))
        {
            return new TryShotBuildResult(
                false,
                null,
                "読み速さは " + MinSpeed.ToString("0.##", CultureInfo.InvariantCulture)
                + "〜" + MaxSpeed.ToString("0.##", CultureInfo.InvariantCulture) + " です。");
        }

        var caption = Fold(shot.Caption);
        if (caption is not null && caption.Length > MaxCaptionLength)
        {
            return new TryShotBuildResult(
                false,
                null,
                "演技指示が長すぎます（上限 "
                + MaxCaptionLength.ToString(CultureInfo.InvariantCulture) + " 字）。");
        }

        long? seed = null;
        var seedText = Fold(shot.Seed);
        if (seedText is not null)
        {
            if (!long.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                || parsed < 0)
            {
                return new TryShotBuildResult(false, null, "乱数の種は 0 以上の整数で入れてください。");
            }

            seed = parsed;
        }

        var irodori = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (shot.NumSteps is int numSteps)
        {
            irodori["num_steps"] = numSteps;
        }

        if (shot.CfgScaleText is double cfgText)
        {
            irodori["cfg_scale_text"] = cfgText;
        }

        if (shot.CfgScaleCaption is double cfgCaption)
        {
            irodori["cfg_scale_caption"] = cfgCaption;
        }

        if (shot.CfgScaleSpeaker is double cfgSpeaker)
        {
            irodori["cfg_scale_speaker"] = cfgSpeaker;
        }

        if (caption is not null)
        {
            irodori["caption"] = caption;
        }

        if (seed is long seedValue)
        {
            irodori["seed"] = seedValue;
        }

        var voice = Fold(shot.Voice);
        return new TryShotBuildResult(
            true,
            new SpeechRequest(input, voice, shot.Speed, irodori.Count == 0 ? null : irodori),
            null);
    }

    /// <summary>
    /// 空文字・空白のみを「未指定」に畳む（裁定 48）。それ以外は前後の空白だけ落とす。
    /// </summary>
    public static string? Fold(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 4xx の <c>code</c> から UI の 1 行を作る（<b>文言では判定しない</b>＝契約 ⑶ 3-3）。
    /// 知らない code は wrapper の <c>message</c> をそのまま出す（席が言い換えない）。
    /// </summary>
    public static string DescribeError(ErrorBody? error, int statusCode)
    {
        var code = error?.Code;
        var known = code switch
        {
            WrapperErrorCodes.UnknownVoice => "その話者が見つかりません（一覧を読み直してください）。",
            WrapperErrorCodes.MissingVoice => "話者を選んでください。",
            WrapperErrorCodes.EmptyInput => "本文を入れてください。",
            WrapperErrorCodes.RuntimeUnavailable => "モデルがまだ読み込まれていません。",
            WrapperErrorCodes.OutOfRange => "値が範囲の外です。",
            WrapperErrorCodes.TypeError => "値の型が違います。",
            WrapperErrorCodes.UnknownField => "その項目は受け付けません。",
            WrapperErrorCodes.InvalidEnum => "選べない値です。",
            WrapperErrorCodes.LiteralTopLevel => "この項目は irodori の中に入れて送る必要があります。",
            WrapperErrorCodes.VoiceAndNoRef => "話者の指定と「参照なし」は同時に使えません。",
            WrapperErrorCodes.VoiceAndReference => "話者の指定と参照波形は同時に使えません。",
            WrapperErrorCodes.UnsupportedResponseFormat => "wav 以外の形式は扱えません。",
            WrapperErrorCodes.UpstreamError => "合成の途中で失敗しました。",
            // 「サーバ」は憲章 §6-1 の隠す語（是正・段 C の検分）＝この 1 行は
            // TryMessageText（発話テスト）に出る。番号つきの状態は出さず、記録へ落とす。
            WrapperErrorCodes.ServerError => "読み上げの用意の中で失敗しました。",
            _ => null,
        };

        var detail = SpeechDetail(error);
        if (known is not null)
        {
            return detail is null ? known : known + "（" + detail + "）";
        }

        var head = statusCode > 0
            ? "合成に失敗しました（HTTP "
              + statusCode.ToString(CultureInfo.InvariantCulture) + "）。"
            : "読み上げの用意に届きませんでした。";
        return detail is null ? head : head + " " + detail;
    }

    private static string? SpeechDetail(ErrorBody? error)
    {
        var message = Fold(error?.Message);
        if (message is null)
        {
            return null;
        }

        return message.Length <= 200 ? message : message[..200] + "…";
    }
}
