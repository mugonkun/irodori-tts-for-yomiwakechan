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
