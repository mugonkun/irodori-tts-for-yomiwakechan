using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>どのリリースの配布樹の上で走っているか（裁定 5＝Radeon 版は別リリース）。</summary>
public enum ReleaseFlavor
{
    /// <summary>CUDA 版（cu130／cu126／cpu を選べる）。</summary>
    Cuda,

    /// <summary>Radeon 版（rocm-gfx1151／cpu だけ・CUDA の選択肢は出さない）。</summary>
    Radeon,
}

/// <summary>
/// <b>選択肢は配布樹が決める</b>（純関数）。
/// <para>
/// 初回取得ウィザードと設定画面が出す変種の一覧は、<b>その配布物に取得台帳が入っている変種</b>に
/// 限る＝<c>ledger/runtime-rocm-gfx1151.json</c> を持つ配布物は Radeon 版であり、
/// CUDA の選択肢を出してはならない（裁定 5＝出しても台帳が無いので取得が始まらない）。
/// 版の種類をアプリに焼き込まず樹から読むのは、同じ exe が両リリースで動くからである。
/// </para>
/// </summary>
public static class ReleaseFlavors
{
    /// <summary>
    /// 取得台帳の檔名（拡張子なし）の並びから判る変種（純関数）。
    /// <c>runtime-rocm-*</c> が 1 件でもあれば Radeon 版。
    /// </summary>
    public static ReleaseFlavor Detect(IEnumerable<string> ledgerNames)
    {
        ArgumentNullException.ThrowIfNull(ledgerNames);
        foreach (var name in ledgerNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var trimmed = name.Trim();
            if (trimmed.StartsWith("runtime-", StringComparison.OrdinalIgnoreCase)
                && RuntimeVariants.IsRocm(trimmed["runtime-".Length..]))
            {
                return ReleaseFlavor.Radeon;
            }
        }

        return ReleaseFlavor.Cuda;
    }

    /// <summary>UI に出す変種の並び（純関数）。</summary>
    public static IReadOnlyList<string> Choices(ReleaseFlavor flavor) => flavor switch
    {
        ReleaseFlavor.Radeon => RuntimeVariants.RadeonReleaseChoices,
        _ => RuntimeVariants.CudaReleaseChoices,
    };

    /// <summary>
    /// <b>実際に取得台帳が在る変種だけ</b>に絞る（純関数）。台帳の無い変種を選ばせない。
    /// 1 件も残らなければ <see cref="Choices"/> をそのまま返す（樹が読めなかった＝止めない）。
    /// </summary>
    public static IReadOnlyList<string> AvailableChoices(
        ReleaseFlavor flavor,
        IEnumerable<string> ledgerNames)
    {
        ArgumentNullException.ThrowIfNull(ledgerNames);
        var present = new HashSet<string>(
            ledgerNames.Where(static n => !string.IsNullOrWhiteSpace(n)).Select(static n => n.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var filtered = Choices(flavor)
            .Where(v => present.Contains(RuntimeVariants.LedgerName(v)))
            .ToArray();

        return filtered.Length > 0 ? filtered : Choices(flavor);
    }

    // ---- 版の名札（裁定 109 → v2.0 段 F-1 で綴りを改めた）------------------------------
    // 司令官の逐語（2026-09-07）＝「『CUDA版とROCm版の区別』インストーラーが別のはずだが、アプリ名称も
    // (CUDA版)(ROCm版)としてデフォルトインストールフォルダも分けたい。……Windowタイトルも別に分ける。」
    // 所有者の指示（2026-09-10・憲章 §6-2 附録 4／`v2-spec.md` §9）＝**併記が正**である＝
    //   「（CUDA 版）」「（ROCm 版）」→ **「RTX（CUDA）」「Radeon（ROCm）」**。
    //   利用者は自分の機体の箱に書いてある語（RTX／Radeon）で選ぶのであって、CUDA／ROCm は
    //   どの箱にも書いていない。だから**利用者の語を先に置き、技術の語を括弧で添える**。
    // ここに置く理由＝exe は 1 本で両リリースを兼ねる（樹から読む）ので、名札も樹から導く純関数にする。
    // 内部の識別子（enum の Radeon・台帳名 runtime-rocm-*・Flavor id の radeon）は 1 字も変えない
    // ＝利用者に見せる名だけが「Radeon（ROCm）」である。
    //
    // **版の名札を綴るのはこの 1 箇所だけ**（`v2-plan.md` F-1 の規則）＝
    // `RuntimeVariants.ShortDisplayName` と `VariantGate.Label` の「CUDA」は**動かし方の短い名**で
    // あって版の名札ではない（同じ文で「動かし方」と「版」の名を混ぜない）。
    // インストーラ側の逐語は `installer/irodori-tts-ywk.iss` の MyAppName＝**同じ 2 語**である。

    /// <summary>版に依らないアプリの名（表示名の幹）。</summary>
    public const string AppBaseName = "irodori-TTS for 読み分けちゃん";

    /// <summary>幹と版の名札のあいだの飾り（全角ダッシュ＝インストーラの表示名と 1 字も違えない）。</summary>
    public const string Separator = " － ";

    /// <summary>版の名札（<c>RTX（CUDA）</c>／<c>Radeon（ROCm）</c>・純関数）。</summary>
    public static string FlavorLabel(ReleaseFlavor flavor) => flavor switch
    {
        ReleaseFlavor.Radeon => "Radeon（ROCm）",
        _ => "RTX（CUDA）",
    };

    /// <summary>
    /// 幹に版の名札を足す（<c>幹 － RTX（CUDA）</c>）。
    /// <b>括弧では飾らない</b>＝名札そのものが括弧を持つので「…（RTX（CUDA））」と二重になる。
    /// </summary>
    public static string Decorate(string baseName, ReleaseFlavor flavor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        return baseName + Separator + FlavorLabel(flavor);
    }

    /// <summary>窓題・このアプリについての見出し（＝インストーラの AppName と同じ文字列）。</summary>
    public static string AppTitle(ReleaseFlavor flavor) => Decorate(AppBaseName, flavor);

    /// <summary>
    /// はじめの準備の窓題（<c>はじめの準備 － RTX（CUDA）</c>）。
    /// <b>幹に本体名を入れない</b>＝` － ` が 2 つ並ぶ（`v2-copy.md` §1-8 の :110）。
    /// </summary>
    public static string WizardTitle(ReleaseFlavor flavor) => Decorate("はじめの準備", flavor);

    // トレイの吹き出し（TrayBaseName／TrayText）は裁定 124 で消えた＝常駐しないので
    // NotifyIcon も 63 字の枠も無い。状態の日本語は StatusViewModel.StateLabel が 1 箇所で綴る。

    /// <summary>配布樹の <c>ledger/</c> を読んで版を決める（薄い殻＝読めなければ RTX（CUDA））。</summary>
    public static ReleaseFlavor DetectFrom(string? ledgerDir) =>
        string.IsNullOrWhiteSpace(ledgerDir)
            ? ReleaseFlavor.Cuda
            : Detect(LedgerNames(ledgerDir));

    /// <summary>
    /// <b>読めたときだけ真</b>で版を返す（<c>decisions.md</c> 133・是正・2026-09-11）。
    /// <para>
    /// <see cref="DetectFrom"/> は <c>ledger/</c> が無い・読めない回も <see cref="ReleaseFlavor.Cuda"/> を
    /// 返す＝<b>「空を読んだ」と「CUDA だと判った」が同じ顔になる</b>。名札しか決めていなかった頃
    /// （裁定 109）は無害だったが、いまは<b>データ樹・単一起動の錠・移送の行き先</b>がこの 1 値で決まる＝
    /// Radeon 機で <c>ledger/</c> の読みが 1 度こければ CUDA の樹で起動し、未移送の共有樹を
    /// <b>CUDA の樹へ移してしまう</b>（Radeon 版は二度と見ないし、Radeon の撤去でも消えない）。
    /// だから「判らなかった」を呼び手へ返す口を分ける（<see cref="Contracts.AppPaths.FromEnvironment"/>）。
    /// </para>
    /// </summary>
    /// <param name="ledgerDir">配布樹の <c>ledger/</c>。</param>
    /// <param name="flavor">判った版（偽のときは <see cref="ReleaseFlavor.Cuda"/>）。</param>
    /// <returns>台帳が 1 檔でも読めたか（＝この判定を信じてよいか）。</returns>
    public static bool TryDetectFrom(string? ledgerDir, out ReleaseFlavor flavor)
    {
        flavor = ReleaseFlavor.Cuda;
        if (string.IsNullOrWhiteSpace(ledgerDir))
        {
            return false;
        }

        var names = LedgerNames(ledgerDir);
        if (names.Count == 0)
        {
            return false; // 無い／読めない＝**CUDA だと判ったことにはしない**
        }

        flavor = Detect(names);
        return true;
    }

    /// <summary>
    /// 導入先の名から版を当てる（<b>純関数</b>＝<c>.iss</c> の <c>MyDirName</c> は版ごと＝
    /// <c>irodori-tts-ywk-cuda</c>／<c>…-radeon</c>）。<see cref="TryDetectFrom"/> が黙った回の 1 段目の控え。
    /// </summary>
    public static bool TryDetectFromDirectoryName(string? installDir, out ReleaseFlavor flavor)
    {
        flavor = ReleaseFlavor.Cuda;
        if (string.IsNullOrWhiteSpace(installDir))
        {
            return false;
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(installDir.Trim()));
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.EndsWith("-" + Contracts.AppPaths.FlavorId(ReleaseFlavor.Radeon), StringComparison.OrdinalIgnoreCase))
        {
            flavor = ReleaseFlavor.Radeon;
            return true;
        }

        return name.EndsWith("-" + Contracts.AppPaths.FlavorId(ReleaseFlavor.Cuda), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>配布樹の <c>ledger/</c> を読む（薄い殻＝檔が読めなければ CUDA 版として振る舞う）。</summary>
    public static IReadOnlyList<string> LedgerNames(string ledgerDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ledgerDir);
        try
        {
            return Directory.Exists(ledgerDir)
                ? Directory.GetFiles(ledgerDir, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(static n => !string.IsNullOrWhiteSpace(n))
                    .Select(static n => n!)
                    .ToArray()
                : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
