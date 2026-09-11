using System;
using System.IO;

namespace IrodoriTtsYwk.Launcher.Services.Security;

/// <summary>
/// Windows の <b>Smart App Control</b> の状態（<c>decisions.md</c> 140 が名指した 3 値）。
/// </summary>
public enum SmartAppControlState
{
    /// <summary>無効（値 0・鍵ごと無い機体・読めなかった機体）＝<b>何も告げない</b>。</summary>
    Off,

    /// <summary>有効（値 1）＝未署名で評判の無い <c>.pyd</c>／<c>.dll</c> が止まる。</summary>
    On,

    /// <summary>評価中（値 2）＝Windows が有効にするか見極めている途中。</summary>
    Evaluation,
}

/// <summary>
/// <b>いまこの機体の状態を読むだけの口</b>（試験は偽物を差す）。
/// <para>
/// 返すのは<b>生の数</b>である＝意味づけ（0／1／2）は
/// <see cref="SmartAppControl.FromPolicyValue"/> の 1 箇所だけが持つ。
/// 読めなかった回は <c>null</c> を返す（<b>投げない</b>）。
/// </para>
/// </summary>
public interface ISmartAppControlPolicyReader
{
    /// <summary>
    /// <c>HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy</c> の
    /// <c>VerifiedAndReputablePolicyState</c>（読めなければ <c>null</c>）。
    /// </summary>
    int? ReadPolicyState();
}

/// <summary>
/// <b>Smart App Control が有効かどうかを読む</b>（<c>decisions.md</c> 140・v2.0.2）。
/// <para>
/// <b>なぜ要るか</b>＝司令官の手動検分（2026-09-11）で、参照ボイスを選んで〔しゃべらせる〕と
/// Python の拡張（<c>_spline.cp312-win_amd64.pyd</c>＝scipy.signal の一部）が
/// Smart App Control に止められた。初回にダウンロードする一式に入っている第三者の
/// <c>.pyd</c>／<c>.dll</c> は<b>未署名で評判が無い</b>ので、どれが止まるかを配布側から
/// 予測も是正もできない（裁定 8＝第三者バイナリを同梱しない）。
/// <b>止められてから謝るのではなく、押す前に事実を告げる</b>のがこの檔の仕事である。
/// </para>
/// <para>
/// <b>純で安い</b>＝読むのは鍵 1 つの値 1 つだけ。<b>書かない・投げない・待たない。</b>
/// 読めない機体（鍵が無い・権限が無い・Win10）は <see cref="SmartAppControlState.Off"/> に落ちる＝
/// <b>判らない回に警告を出さない</b>（憲章 原則 6 の裏＝出す文は必ず事実であること）。
/// </para>
/// <para>
/// <b>利用者に見せる文はここに無い</b>＝文は <c>ViewModels/SmartAppControlNotice.cs</c> と
/// <see cref="ViewModels.UiStrings"/> が持つ。ここは状態と、設定を開く行き先だけを持つ。
/// </para>
/// </summary>
public static class SmartAppControl
{
    /// <summary>状態が載っている鍵（<c>HKEY_LOCAL_MACHINE</c> の下）。</summary>
    public const string PolicyKeyPath = @"SYSTEM\CurrentControlSet\Control\CI\Policy";

    /// <summary>その中の値の名。</summary>
    public const string PolicyValueName = "VerifiedAndReputablePolicyState";

    /// <summary>
    /// スマート アプリ コントロールの頁を直に開く（Windows セキュリティの中の 1 枚）。
    /// <b>この機体（Windows 11 26200）の <c>SecHealthUI</c> が持っている綴りを実測して決めた。</b>
    /// </summary>
    public const string SettingsUri = "windowsdefender://smartapp";

    /// <summary>
    /// 上が開けない機体のための逃げ道（Windows セキュリティそのもの）。
    /// 頁の綴りは版で変わりうるが、こちらは古い版にも在る。
    /// </summary>
    public const string FallbackSettingsUri = "ms-settings:windowsdefender";

    /// <summary>
    /// <b>生の数を状態に直す</b>（<b>純関数</b>）＝1 有効／2 評価中／それ以外と <c>null</c> は無効。
    /// </summary>
    public static SmartAppControlState FromPolicyValue(int? value) => value switch
    {
        1 => SmartAppControlState.On,
        2 => SmartAppControlState.Evaluation,
        _ => SmartAppControlState.Off,
    };

    /// <summary>
    /// いまの状態を読む（<b>絶対に投げない</b>）。
    /// <paramref name="reader"/> が <c>null</c> なら本物の登録簿を読む。
    /// 読み手が投げた回も <see cref="SmartAppControlState.Off"/> に落ちる。
    /// </summary>
    public static SmartAppControlState Read(ISmartAppControlPolicyReader? reader = null)
    {
        var source = reader ?? new RegistryPolicyReader();
        try
        {
            return FromPolicyValue(source.ReadPolicyState());
        }
#pragma warning disable CA1031 // 判らない回は「無効」に落とす＝この読み取りで窓を落とさない。
        catch (Exception)
#pragma warning restore CA1031
        {
            return SmartAppControlState.Off;
        }
    }
}

/// <summary>
/// 本物の登録簿を読む（<b>読み取り専用</b>・<c>HKLM</c> は誰でも読める）。
/// <para>
/// 値は <c>REG_DWORD</c> だが、型が違う機体でも落ちないように
/// <see cref="Convert.ToInt32(object, IFormatProvider)"/> を通してから返す。
/// </para>
/// </summary>
public sealed class RegistryPolicyReader : ISmartAppControlPolicyReader
{
    /// <inheritdoc />
    public int? ReadPolicyState()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(SmartAppControl.PolicyKeyPath, writable: false);
            var raw = key?.GetValue(SmartAppControl.PolicyValueName);
            return raw is null
                ? null
                : Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
