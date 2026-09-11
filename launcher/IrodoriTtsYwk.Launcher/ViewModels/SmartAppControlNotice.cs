using System;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Security;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// <b>Smart App Control を利用者の言葉に直す</b>（<b>純関数だけ</b>・<c>decisions.md</c> 140・v2.0.2）。
/// <para>
/// 持ち場は 2 つある＝
/// ⑴ <b>押す前の告知</b>（はじめの準備の「これからすること」・設定 › 詳細の 1 行）＝
/// <see cref="SmartAppControl"/> が読んだ状態から文を選ぶ。
/// ⑵ <b>止められた後の畳み</b>＝上流や Windows が返した<b>生の 1 行</b>に
/// 「アプリケーション制御ポリシー」などの標識が在るかを見て、憲章 原則 6 の 3 部品へ言い直す。
/// </para>
/// <para>
/// <b>生の 1 行は帯にも発話テストにも出さない。</b>司令官の手動検分（`decisions.md` 140）で
/// 出てしまったのは「読み上げの用意の中で失敗しました。（ImportError: DLL load failed while
/// importing _spline: …）」＝<see cref="SpeechRequestBuilder.DescribeError"/> が
/// wrapper の <c>message</c> をそのまま括弧に入れていたためである。生の字が行く先は
/// <b>記録の檔（<c>LauncherLogFile</c>）と 詳しい状態 の「記録」の箱</b>の 2 つだけ
/// （この箱が、この製品で唯一の生の記録の置き場である＝`v2-copy.md` §3-1）＝
/// <b>帯・はじめの準備・発話テストには 1 文字も出さない</b>（是正・検分 low 7）。
/// </para>
/// <para>
/// <b>「切ってください」とは言わない。</b>防護の設定を決めるのは利用者である（所有者の規則）＝
/// 文が言うのは、事実・できないこと・設定の在り処、の 3 つだけ。
/// <b>オン／オフの可逆性には触れない</b>（<c>decisions.md</c> 142＝最近の Windows の更新で
/// 再びオンにできるようになった＝「戻せません」は誤り）。
/// </para>
/// </summary>
public static class SmartAppControlNotice
{
    /// <summary>
    /// <b>止められた 1 行を見分ける標識</b>（前方一致ではなく<b>部分一致</b>＝
    /// 生の字は ImportError の本文の中ほどに現れる）。
    /// <para>
    /// ⑴ 日本語の Windows が返す綴り（実測＝`decisions.md` 140 の逐語）
    /// ⑵ <b>英語の機体が返す綴り</b>（是正・検分 high 1＝実測で確かめた）
    /// ⑶ 古い綴りと機能そのものの名。
    /// </para>
    /// <para>
    /// <b>⑵ の出所</b>＝止めているのは Win32 の 4551
    /// （<c>ERROR_SYSTEM_INTEGRITY_POLICY_VIOLATION</c>）で、
    /// <c>FormatMessageW(4551, lang 0)</c> が日本語の機体で「アプリケーション制御ポリシーによって
    /// このファイルがブロックされました。」を、<c>lang 1033</c> が
    /// 「An Application Control policy has blocked this file.」を返す（実測・この機体）。
    /// CPython は拡張が読めなかった回に
    /// 「<c>ImportError: DLL load failed while importing _spline: 〈その OS の文〉</c>」を組むので、
    /// <b>機体の表示言語がそのまま本文に出る</b>。1 巡目が持っていた
    /// 「blocked by your organization」は SmartScreen ／ ストアの綴りで、
    /// <b>読み込みの失敗にはこの字は出ない</b>＝英語の機体では畳みが 1 度も効かず、
    /// v2.0.1 の生の ImportError が帯へ戻っていた。
    /// </para>
    /// </summary>
    public static readonly string[] Markers =
    [
        "アプリケーション制御ポリシー",
        "Application Control policy",
        "blocked by your organization",
        "Smart App Control",
    ];

    /// <summary>
    /// <b>拡張が読めなかった綴り</b>（CPython が組む前半＝表示言語に依らない）。
    /// これだけでは Smart App Control とは限らない（欠けた DLL でも出る）ので、
    /// <b>この機体の SAC が有効／評価中の回だけ</b>畳みに使う。
    /// </summary>
    public const string DllLoadMarker = "DLL load failed";

    /// <summary>
    /// この 1 行は Smart App Control に止められた物か（<b>純関数</b>・<c>null</c> は偽）。
    /// 英字の標識は大小を問わない（Windows の版で綴りが揺れる）。
    /// </summary>
    /// <param name="raw">上流や Windows が返した生の 1 行。</param>
    /// <param name="state">
    /// <b>この機体の Smart App Control の状態</b>（既定＝無効＝標識だけで見分ける）。
    /// 有効・評価中を渡した回は、<see cref="DllLoadMarker"/> を含む 1 行も畳む＝
    /// 日本語でも英語でもない機体（OS の文がその言語で返る）を取りこぼさない。
    /// <b>無効な機体では効かない</b>ので、欠けた DLL の失敗を SAC のせいにしない。
    /// </param>
    public static bool Blocked(string? raw, SmartAppControlState state = SmartAppControlState.Off)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        foreach (var marker in Markers)
        {
            if (raw.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return state is SmartAppControlState.On or SmartAppControlState.Evaluation
            && raw.Contains(DllLoadMarker, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 帯の 1 行（⑴＋⑵＋⑶）。<b>生の字は 1 文字も載らない</b>＝
    /// 帯（<c>BandText.For</c>）と発話テストの両方がここを通る。
    /// </summary>
    /// <param name="state">
    /// <b>いまの状態</b>（是正・検分 medium 4）＝名乗る語は状態が決める。
    /// 参照ボイスの 1 射だけが止められた回、サーバは <see cref="ServerState.Ready"/> のまま
    /// <b>立っている</b>（本体からも呼べるし「デフォルト」の声は通る）ので、
    /// ここで「止まりました。」と名乗ると<b>嘘になる</b>（憲章 原則 6 の ⑴ は事実であること・
    /// `v2-copy.md` §3-1 は「止まりました。」を失敗の行にだけ充てている）。
    /// 丸は<b>どちらでも赤</b>＝直すまで参照ボイスは 1 本も鳴らない。
    /// </param>
    public static BandLine Band(ServerState state = ServerState.Failed) => new(
        BandSeverity.Bad,
        state switch
        {
            ServerState.Ready => BandText.Ready,
            ServerState.Warming => BandText.ReadyWarming,
            _ => BandText.Failed,
        },
        UiStrings.SmartAppControlFailedWhat + " " + UiStrings.SmartAppControlFailedWhy,
        UiStrings.SmartAppControlOpenSettingsButton,
        BandActionKind.OpenSmartAppControl);

    /// <summary>
    /// 発話テストの 1 行（釦を持てない面なので、⑶ は帯の釦に任せて<b>言わない</b>＝
    /// 同じ事件に 2 つの入口を作らない。帯には <see cref="Band"/> が同時に出る）。
    /// </summary>
    public static string Message() =>
        UiStrings.SmartAppControlFailedWhat + " " + UiStrings.SmartAppControlFailedWhy;

    /// <summary>
    /// 設定 → 詳細に出す 1 行（無効な機体は <c>null</c>＝<b>出す物が無い</b>）。
    /// </summary>
    public static string? StatusLine(SmartAppControlState state) => state switch
    {
        SmartAppControlState.On => UiStrings.SmartAppControlStatusOn,
        SmartAppControlState.Evaluation => UiStrings.SmartAppControlStatusEvaluation,
        _ => null,
    };

    /// <summary>
    /// その 1 行に添える註（<b>状態で言い切りを分ける</b>＝是正・検分 low 6）。
    /// <para>
    /// 有効な機体は<b>言い切る</b>（裁定 143＝「動かない前提で明示して告げる」）＝
    /// 公式ページ・はじめの準備の告知・リリース文と同じ強さにする。
    /// 評価中の機体だけが「ことがあります」＝Windows がまだ有効にするか決めていない。
    /// </para>
    /// </summary>
    public static string? StatusNote(SmartAppControlState state) => state switch
    {
        SmartAppControlState.On => UiStrings.SmartAppControlStatusNoteOn,
        SmartAppControlState.Evaluation => UiStrings.SmartAppControlStatusNoteEvaluation,
        _ => null,
    };

    /// <summary>
    /// はじめの準備の「これからすること」に出す告知（<b>有効な機体だけ</b>・
    /// 評価中と無効は <c>null</c>＝まだ止められると決まっていない機体を脅かさない）。
    /// </summary>
    public static string? WizardNotice(SmartAppControlState state) =>
        state is SmartAppControlState.On ? UiStrings.SmartAppControlWizardNotice : null;
}
