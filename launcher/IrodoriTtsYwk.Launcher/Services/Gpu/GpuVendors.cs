using System;
using System.Collections.Generic;

namespace IrodoriTtsYwk.Launcher.Services.Gpu;

/// <summary>
/// アダプタの名前から<b>会社</b>だけを読む（<b>純関数</b>・是正・段 G・medium 10／12）。
/// <para>
/// <b>なぜ要るか</b>＝素の機体には実行系がまだ無く（<c>ResolvePythonExe</c> は null）、
/// <see cref="GpuEnumerator"/> は <c>nvidia-smi</c> しか撃てない。それが無い回は
/// <b>「本当に GPU が無い」と「NVIDIA の道具が無いだけ」を区別できなかった</b>ので、
/// <see cref="VariantRecommendation.Recommend"/> の CPU の枝も
/// <c>FirstRunViewModel.NoGpuDecisionLine</c> も<b>実機では 1 度も通らなかった</b>。
/// DXGI は実行系が無くても答えるので、そこを埋める（同じ口を
/// <see cref="OsGpuMemorySampler"/> が既に使っている）。
/// </para>
/// <para>
/// <b>判定は名前の中の 1 語だけ</b>＝Windows のアダプタ名は会社名を先頭に持つ
/// （<c>NVIDIA GeForce RTX 3090</c>／<c>AMD Radeon(TM) 8060S Graphics</c>／
/// <c>Intel(R) Arc(TM) Graphics</c>）。<b>ソフトウェアのアダプタは
/// <see cref="DxgiGpuAdapters"/> が先に落としてある。</b>
/// </para>
/// </summary>
public static class GpuVendors
{
    /// <summary>NVIDIA の名か（<b>純関数</b>）。</summary>
    public static bool IsNvidia(string? name) =>
        Has(name, "NVIDIA") || Has(name, "GeForce") || Has(name, "Quadro");

    /// <summary>AMD／Radeon の名か（<b>純関数</b>）。</summary>
    public static bool IsAmd(string? name) =>
        Has(name, "AMD") || Has(name, "Radeon") || Has(name, "ATI ");

    /// <summary>この並びに NVIDIA の板が 1 枚でも居るか。</summary>
    public static bool AnyNvidia(IReadOnlyList<GpuAdapterInfo>? adapters) => Any(adapters, IsNvidia);

    /// <summary>この並びに AMD の板が 1 枚でも居るか。</summary>
    public static bool AnyAmd(IReadOnlyList<GpuAdapterInfo>? adapters) => Any(adapters, IsAmd);

    private static bool Any(IReadOnlyList<GpuAdapterInfo>? adapters, Func<string?, bool> match)
    {
        if (adapters is null)
        {
            return false;
        }

        foreach (var adapter in adapters)
        {
            if (match(adapter.Name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Has(string? name, string needle) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
