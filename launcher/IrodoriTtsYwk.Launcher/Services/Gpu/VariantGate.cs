using System;
using System.Collections.Generic;
using System.IO;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Gpu;

/// <summary>変種の門の判断（<see cref="VariantGate.Decide"/> の返り）。</summary>
/// <param name="Allow">起こしてよいか。</param>
/// <param name="Reason">起こさないときの理由 1 行（<see cref="Allow"/> が真なら null）。</param>
/// <param name="SuggestedVariant">代わりに勧める変種（勧めようが無ければ null）。</param>
/// <param name="Notices">起こすが伝える 1 行の列（既定は空・<b>null にはならない</b>）。</param>
public sealed record GateDecision(
    bool Allow,
    string? Reason,
    string? SuggestedVariant,
    IReadOnlyList<string> Notices);

/// <summary>
/// <b>変種の門</b>（裁定 88 ⑴⑵・<b>純関数</b>）。
/// <para>
/// GPU 変種（<c>cu130</c>／<c>cu126</c>／<c>rocm-*</c>）を起こす<b>前に</b>、その変種の
/// <c>python.exe</c> で <see cref="TorchGpuProbe"/> を撃ち、<c>is_available()=False</c> か
/// <c>device_count=0</c> なら<b>起こさない</b>。理由 1 行と勧める変種を返す。
/// </para>
/// <para>
/// <b>なぜ import の成否では足りないか</b>（裁定 80）＝cu130 は古いドライバでも <c>import torch</c> が
/// 通る。RTX 3090・ドライバ 537.58 の実射では <c>is_available=False</c>・<c>device_count=0</c> で、
/// それでも起動は健全に見え（<c>/health</c> 200・<c>/params</c> 200・<c>device.actual=cpu</c>）、
/// <b>最初の合成でプロセスが 0xC0000005 で消えた</b>（裁定 83）。Python の例外もサーバのログも残らず、
/// クライアントには <c>ConnectionResetError 10054</c> だけが返る。
/// ⇒ <b>cu130 の CPU 転落は禁止</b>＝門で止めて cpu か cu126 へ切り替えさせる。
/// </para>
/// <para>
/// <b>cpu 変種は門を通さない</b>（GPU を見ないので見る物が無い）。
/// </para>
/// <para>
/// <b>検分が読めなかったときの扱いは変種で違う</b>（是正・便 D（2））＝
/// <c>rocm-*</c>・<c>cu126</c> は<b>注意 1 行でそのまま起こす</b>（wrapper が自分で理由を出す・
/// cu126 は GPU を隠しても 200 が返る＝裁定 83）が、<c>cu130</c> と畳んだ名 <c>cuda</c> は
/// <b>起こさない</b>（<see cref="RequiresObservedProbe"/>）。cu130 の禁止形は沈黙して消えるので、
/// 「見ていない」を「大丈夫」の側に倒せない。
/// </para>
/// <para>
/// ドライバの閾（<see cref="DriverRequirement"/>）も同じ門に入る＝cu130 ≥ 580.00／cu126 ≥ 528.33。
/// <b>cu126 の 528.33 ≤ v &lt; 537.58 は「未実測の帯」</b>＝止めずに
/// <see cref="GateDecision.Notices"/> に 1 行を載せる（裁定 80・88 ⑵）。
/// </para>
/// </summary>
public static class VariantGate
{
    /// <summary>門で撃つ検分の期限（この機体の torch import は 2.2〜3.5 s＝§11-3）。</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    /// <summary>台帳の綴りとして知っている変種（<c>ledger/runtime-&lt;変種&gt;.json</c> の名）。</summary>
    public static readonly string[] KnownVariants =
    [
        RuntimeVariants.Cu130,
        RuntimeVariants.Cu126,
        RuntimeVariants.RocmGfx1151,
        RuntimeVariants.Cpu,
    ];

    /// <summary>
    /// 門の判断（<b>純関数</b>）。
    /// </summary>
    /// <param name="variant">
    /// 起こそうとしている変種（台帳の綴り。<c>cuda</c> の畳んだ名も受けるが、
    /// <b>畳んだ名は cu130 と同じ厳しさで見る</b>＝どちらの実行系か判らないため）。
    /// </param>
    /// <param name="probe">
    /// その変種の <c>python.exe</c> で撃った検分。<b>null＝撃てなかった</b>（実行系が無い等）。
    /// <c>rocm-*</c>・<c>cu126</c> は止めずに 1 行の注意を出す（起こせば wrapper 自身が理由を出す）が、
    /// <c>cu130</c>／<c>cuda</c> は<b>起こさない</b>（<see cref="RequiresObservedProbe"/>）。
    /// </param>
    /// <param name="driverVersion"><c>nvidia-smi</c> の <c>driver_version</c>（読めなければ null）。</param>
    /// <param name="installedVariants">この機体に組んである変種（勧める先を選ぶのに使う）。</param>
    public static GateDecision Decide(
        string variant,
        TorchGpuProbe.TorchProbeResult? probe,
        string? driverVersion,
        IReadOnlyList<string>? installedVariants)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        var name = variant.Trim();
        var installed = installedVariants ?? [];

        // ⑴ cpu 変種は門を通さない（裁定 88 ⑴）。
        if (RuntimeVariants.IsCpu(name))
        {
            return new GateDecision(true, null, null, []);
        }

        // ⑵ 検分そのもの＝is_available()／device_count()（裁定 88 ⑴）。
        //    **閾より先に見る**＝実際に撃った観測のほうが表の値より強い。
        if (probe is not null && probe.Observed && !probe.GpuUsable)
        {
            var refused = Suggest(name, probe, driverVersion, installed);
            return new GateDecision(
                false,
                With(name, "は") + "この機体で GPU を見られません（" + Observed(probe, driverVersion)
                + "）。" + SwitchTo(refused) + "。",
                refused,
                []);
        }

        // ⑵-b 検分が**読めなかった** cu130（是正・便 D（2））。
        //     裁定 83 の禁止形は「起動は健全に見え、最初の合成でプロセスが 0xC0000005 で消える」で、
        //     Python の例外もサーバのログも残らない。1 巡目はこの枝が rocm と同じ「注意 1 行で
        //     そのまま起こす」に落ちていたので、**is_available を 1 度も観測しないまま**その形へ
        //     到達できた（実射＝`Decide("cu130", 期限切れ, null)` が `Allow=True`）。
        //     rocm では無害（wrapper が自分で理由を出す）だが cu130 は沈黙して消える。
        //     ⇒ **cu130（と、どちらか判らない畳んだ名 `cuda`）だけは、確かめられないまま起こさない。**
        if (RequiresObservedProbe(name) && (probe is null || !probe.Observed))
        {
            var unverified = Suggest(name, probe, driverVersion, installed);
            return new GateDecision(
                false,
                With(name, "の") + " GPU 検分ができませんでした（" + WhyUnobserved(probe) + "）。"
                + With(name, "は") + " GPU を見られない機体だと最初の合成でプロセスごと消えるので"
                + "（裁定 83）、確かめられないまま起こしません。" + SwitchTo(unverified) + "。",
                unverified,
                []);
        }

        // ⑶ ドライバの閾（NVIDIA の変種だけ・版が読めなければ見ない＝AMD 機で止めない）。
        var minimum = DriverRequirement.Minimum(name);
        if (minimum is not null
            && !string.IsNullOrWhiteSpace(driverVersion)
            && DriverRequirement.TryCompare(driverVersion, minimum, out var against)
            && against < 0)
        {
            var suggestion = Suggest(name, probe, driverVersion, installed);
            return new GateDecision(
                false,
                "ドライバ " + driverVersion.Trim() + " は " + With(name, "の") + "下限 " + minimum
                + " に届きません。" + SwitchTo(suggestion) + "。",
                suggestion,
                []);
        }

        var notices = new List<string>();

        if (probe is null)
        {
            notices.Add(
                With(name, "の") + " GPU 検分ができませんでした（実行系が見つかりません）。"
                + "そのまま起こしますが、合成が落ちるようなら cpu の変種を選んでください。");
        }
        else if (!probe.Observed)
        {
            notices.Add(
                With(name, "の") + " GPU 検分が読めませんでした（" + (probe.Error ?? "理由不明") + "）。"
                + "そのまま起こします。");
        }

        // ⑷ 未実測の帯（裁定 80・88 ⑵）＝止めずに 1 行だけ出す。
        if (IsUnmeasuredBand(name, driverVersion))
        {
            notices.Add(
                "ドライバ " + driverVersion!.Trim() + " は未実測の帯です（cu126 は "
                + DriverRequirement.Cu126MeasuredMinimum + " まで実射で確かめてあり、下限の "
                + DriverRequirement.Cu126Minimum + " は NVIDIA の表からの値で未実測）。"
                + "合成はできますが、落ちるようなら cpu の変種を選んでください。");
        }

        return new GateDecision(true, null, null, notices);
    }

    /// <summary>
    /// <b>検分が読めないまま起こしてはいけない変種</b>か（<b>純関数</b>・裁定 83）。
    /// <para>
    /// <c>cu130</c> と、cu130 かもしれない畳んだ名 <c>cuda</c> の 2 つだけ。
    /// cu126 は GPU を隠しても CPU で 200 が返ることが実射されている（裁定 83＝
    /// <c>CUDA_VISIBLE_DEVICES=-1</c> で 2 射とも 200）ので、この強い扱いはしない。
    /// <c>rocm-*</c> は wrapper 自身が理由を出す（<c>YWK_VARIANT=rocm-*</c> の分岐）。
    /// </para>
    /// </summary>
    public static bool RequiresObservedProbe(string? variant)
    {
        var name = variant?.Trim();
        return string.Equals(name, RuntimeVariants.Cu130, StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, RuntimeVariants.CudaLabel, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 検分が読めなかった理由（推測を混ぜない＝読めた物だけ）。
    /// <b>絶対パスは畳む</b>＝この 1 行は状態帯にもログにも出るので、
    /// .NET の例外文（<c>… trying to start process 'C:\…\python.exe' …</c>）を
    /// そのまま流さない（契約 ⑹・受け入れ条件 D-4 と同じ規律）。
    /// </summary>
    private static string WhyUnobserved(TorchGpuProbe.TorchProbeResult? probe) =>
        probe is null ? "実行系が見つかりません" : FoldPaths(probe.Error ?? "理由不明");

    /// <summary>
    /// 文中の絶対パスを檔名だけに畳む（<b>純関数</b>）。<c>X:\…\name</c> の形だけを見る＝
    /// 判らない物は 1 文字も変えない。
    /// </summary>
    public static string FoldPaths(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.IndexOf(":\\", StringComparison.Ordinal) < 0)
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            // 「英字 1 文字 + :\」で始まる並びを 1 つのパスとして読み、区切りまで進む。
            if (i + 2 < text.Length && char.IsLetter(text[i]) && text[i + 1] == ':' && text[i + 2] == '\\')
            {
                var end = i;
                while (end < text.Length && text[end] is not ('\'' or '"' or ' ' or '\t' or ',' or ')' or '）'))
                {
                    end++;
                }

                var path = text[i..end];
                var slash = path.LastIndexOf('\\');
                builder.Append(slash >= 0 && slash + 1 < path.Length ? path[(slash + 1)..] : path);
                i = end;
                continue;
            }

            builder.Append(text[i]);
            i++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// cu126 の「未実測の帯」か（<c>528.33 ≤ v &lt; 537.58</c>＝<b>純関数</b>）。
    /// <para>
    /// <b>畳んだ名 <c>cuda</c> はここに入らない</b>（是正・便 D（2））＝
    /// <see cref="DriverRequirement.Minimum"/> が畳んだ名を cu130 の閾（580.00）で見るので、
    /// この帯のドライバは⑶ で<b>断られる</b>（1 巡目はここも閾も落ちて<b>黙って起こしていた</b>）。
    /// </para>
    /// </summary>
    public static bool IsUnmeasuredBand(string variant, string? driverVersion)
    {
        if (!string.Equals(variant?.Trim(), RuntimeVariants.Cu126, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(driverVersion))
        {
            return false;
        }

        return DriverRequirement.TryCompare(driverVersion, DriverRequirement.Cu126Minimum, out var low)
               && low >= 0
               && DriverRequirement.TryCompare(driverVersion, DriverRequirement.Cu126MeasuredMinimum, out var high)
               && high < 0;
    }

    /// <summary>
    /// 代わりに勧める変種（<b>純関数</b>）。
    /// <b>導入済みの変種のうち、その機体で通る見込みのある GPU 変種を先に</b>、無ければ <c>cpu</c>。
    /// <para>
    /// 「通る見込み」は⑴ NVIDIA 機（<c>driver_version</c> が読めた）ならドライバの閾
    /// ⑵ 読めなければ NVIDIA が居ない機体＝<c>rocm-*</c> が組んであればそれ、で決める。
    /// <b>いま落ちた変種は勧めない</b>。
    /// </para>
    /// </summary>
    public static string? Suggest(
        string variant,
        TorchGpuProbe.TorchProbeResult? probe,
        string? driverVersion,
        IReadOnlyList<string>? installedVariants)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        var failing = variant.Trim();
        var installed = installedVariants ?? [];

        bool Has(string candidate) => Contains(installed, candidate);

        // 「いま落ちた変種」と同じ物は勧めない。畳んだ名 "cuda" が落ちたときは
        // cu130／cu126 のどちらが落ちたのか分からないので、どちらも勧めない
        // （呼ぶ側が ServerStartRequest.Variant に台帳の綴りを載せれば分かる）。
        bool Same(string candidate) =>
            string.Equals(failing, candidate, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(failing, RuntimeVariants.CudaLabel, StringComparison.OrdinalIgnoreCase)
                && RuntimeVariants.IsCuda(candidate));

        if (!string.IsNullOrWhiteSpace(driverVersion))
        {
            if (!Same(RuntimeVariants.Cu130) && Has(RuntimeVariants.Cu130)
                && MeetsMinimum(RuntimeVariants.Cu130, driverVersion))
            {
                return RuntimeVariants.Cu130;
            }

            if (!Same(RuntimeVariants.Cu126) && Has(RuntimeVariants.Cu126)
                && MeetsMinimum(RuntimeVariants.Cu126, driverVersion))
            {
                return RuntimeVariants.Cu126;
            }
        }
        else if (!Same(RuntimeVariants.RocmGfx1151) && Has(RuntimeVariants.RocmGfx1151)
                 && probe?.IsRocmBuild != true)
        {
            // NVIDIA が居ない機体で、Radeon 版の実行系が組んである＝それを勧める。
            // いま落ちたのが ROCm ビルドそのものなら勧めない（同じ物を勧めても直らない）。
            return RuntimeVariants.RocmGfx1151;
        }

        if (RuntimeVariants.IsCpu(failing))
        {
            return null;
        }

        // **在りもしない変種を勧めない**（是正・便 D（2）＝low）。台帳が rocm だけの配布
        // （§12-2 ⑵＝選択肢は台帳が実在する変種だけ）で「cpu に切り替えてください」と出すと、
        // 設定の一覧に無い名前を指すことになる。**組んである変種が判らない（空）ときだけ**
        // 従来どおり cpu を勧める＝判らないことと「無い」ことを混ぜない。
        return installed.Count == 0 || Has(RuntimeVariants.Cpu) ? RuntimeVariants.Cpu : null;
    }

    /// <summary>
    /// この機体に組んである変種を配布樹の台帳から数える（<c>ledger/runtime-&lt;変種&gt;.json</c> の在否）。
    /// <b>読むだけ</b>（配布樹は書き換えない）。
    /// </summary>
    public static IReadOnlyList<string> DetectInstalled(string ledgerDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ledgerDir);
        var found = new List<string>(KnownVariants.Length);
        foreach (var variant in KnownVariants)
        {
            if (File.Exists(Path.Combine(ledgerDir, RuntimeVariants.LedgerName(variant) + ".json")))
            {
                found.Add(variant);
            }
        }

        return found;
    }

    private static bool MeetsMinimum(string variant, string driverVersion)
    {
        var minimum = DriverRequirement.Minimum(variant);
        return minimum is null
               || (DriverRequirement.TryCompare(driverVersion, minimum, out var comparison) && comparison >= 0);
    }

    private static bool Contains(IReadOnlyList<string> list, string candidate)
    {
        foreach (var item in list)
        {
            if (string.Equals(item, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>理由 1 行に出す変種の名（台帳の綴りをそのまま＝利用者が設定で選ぶ名と揃える）。</summary>
    private static string Label(string variant) =>
        string.Equals(variant, RuntimeVariants.CudaLabel, StringComparison.OrdinalIgnoreCase)
            ? "CUDA 版"
            : variant;

    /// <summary>
    /// 変種の名に助詞を継ぐ（<c>cu130 は</c>／<c>CUDA 版は</c>）。
    /// 名が ASCII で終わるときだけ空白を入れる＝<c>CUDA 版 は</c> のような二重の切れ目を作らない。
    /// </summary>
    private static string With(string variant, string particle)
    {
        var label = Label(variant);
        return label.Length > 0 && label[^1] <= 0x7F
            ? label + " " + particle
            : label + particle;
    }

    /// <summary>「… に切り替えてください」の部分（裁定 88 ⑴ の例文の形）。</summary>
    private static string SwitchTo(string? suggested)
    {
        if (suggested is null)
        {
            return "設定で別の変種を選んでください";
        }

        return RuntimeVariants.IsCpu(suggested)
            ? "cpu の変種に切り替えてください"
            : suggested + " か cpu の変種に切り替えてください";
    }

    /// <summary>括弧の中に出す観測（<b>推測を混ぜない</b>＝読めた物だけ並べる）。</summary>
    private static string Observed(TorchGpuProbe.TorchProbeResult probe, string? driverVersion)
    {
        var parts = new List<string>(4)
        {
            "is_available=" + (probe.Available ? "True" : "False"),
            "device_count=" + probe.DeviceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrWhiteSpace(driverVersion))
        {
            parts.Add("ドライバ " + driverVersion.Trim());
        }

        if (!string.IsNullOrWhiteSpace(probe.CudaVersion))
        {
            parts.Add("torch cuda " + probe.CudaVersion);
        }
        else if (!string.IsNullOrWhiteSpace(probe.HipVersion))
        {
            parts.Add("torch hip " + probe.HipVersion);
        }

        return string.Join("・", parts);
    }
}
