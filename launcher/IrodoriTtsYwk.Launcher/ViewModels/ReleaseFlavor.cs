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

    // ---- 版の名札（裁定 109＝CUDA 版と ROCm 版を利用者に見せ分ける）--------------------
    // 司令官の逐語（2026-09-07）＝「『CUDA版とROCm版の区別』インストーラーが別のはずだが、アプリ名称も
    // (CUDA版)(ROCm版)としてデフォルトインストールフォルダも分けたい。……Windowタイトルも別に分ける。」
    // ここに置く理由＝exe は 1 本で両リリースを兼ねる（樹から読む）ので、名札も樹から導く純関数にする。
    // 内部の識別子（enum の Radeon・台帳名 runtime-rocm-*・Flavor id の radeon）は 1 字も変えない
    // ＝利用者に見せる名だけが「ROCm 版」である。

    /// <summary>版に依らないアプリの名（表示名の幹）。</summary>
    public const string AppBaseName = "irodori-TTS for 読み分けちゃん";

    /// <summary>版の名札（<c>CUDA 版</c>／<c>ROCm 版</c>・純関数）。</summary>
    public static string FlavorLabel(ReleaseFlavor flavor) => flavor switch
    {
        ReleaseFlavor.Radeon => "ROCm 版",
        _ => "CUDA 版",
    };

    /// <summary>幹に版の名札を括弧で足す（全角括弧＝インストーラの表示名と 1 字も違えない）。</summary>
    public static string Decorate(string baseName, ReleaseFlavor flavor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        return baseName + "（" + FlavorLabel(flavor) + "）";
    }

    /// <summary>窓題・このアプリについての見出し（＝インストーラの AppName と同じ文字列）。</summary>
    public static string AppTitle(ReleaseFlavor flavor) => Decorate(AppBaseName, flavor);

    /// <summary>初回取得ウィザードの窓題。</summary>
    public static string WizardTitle(ReleaseFlavor flavor) => Decorate("初回取得", flavor);

    // トレイの吹き出し（TrayBaseName／TrayText）は裁定 124 で消えた＝常駐しないので
    // NotifyIcon も 63 字の枠も無い。状態の日本語は StatusViewModel.StateLabel が 1 箇所で綴る。

    /// <summary>配布樹の <c>ledger/</c> を読んで版を決める（薄い殻＝読めなければ CUDA 版）。</summary>
    public static ReleaseFlavor DetectFrom(string? ledgerDir) =>
        string.IsNullOrWhiteSpace(ledgerDir)
            ? ReleaseFlavor.Cuda
            : Detect(LedgerNames(ledgerDir));

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
