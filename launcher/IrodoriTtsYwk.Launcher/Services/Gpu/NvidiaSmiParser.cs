using System;
using System.Collections.Generic;
using System.Globalization;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Gpu;

/// <summary>
/// <c>nvidia-smi</c> の出力を読む（<b>純関数</b>＝逐語を固定入力にしてテストで釘付けする）。
/// <para>
/// 固定入力の出所＝<c>research/lab/out/3090-results-ssd-ref/gpu_props.json</c> の
/// <c>nvidia_smi.list</c>／<c>nvidia_smi.query</c>（3090 機・torch 2.10.0+cu130・実測 0.04 s）。
/// </para>
/// <para>
/// <b>NVIDIA 専用の高速路</b>である。AMD 機ではそもそも <c>nvidia-smi</c> が無いので
/// <see cref="TorchGpuProbe"/> に落ちる。
/// </para>
/// </summary>
public static class NvidiaSmiParser
{
    /// <summary>問い合わせる欄（順は問わない＝見出しで対応づける）。</summary>
    public const string QueryFields = "index,name,uuid,pci.bus_id,memory.total,driver_version";

    /// <summary>
    /// <c>nvidia-smi -L</c>＝<c>GPU 0: NVIDIA GeForce RTX 3090 (UUID: GPU-19adfe89-…)</c>。
    /// VRAM とドライバ版は載らない（0／null になる）。
    /// </summary>
    public static IReadOnlyList<GpuInfo> ParseList(string? output)
    {
        var gpus = new List<GpuInfo>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return gpus;
        }

        foreach (var raw in SplitLines(output))
        {
            var line = raw.Trim();
            if (!line.StartsWith("GPU ", StringComparison.Ordinal))
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0
                || !int.TryParse(line[4..colon].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                continue;
            }

            var rest = line[(colon + 1)..].Trim();
            var uuid = string.Empty;
            var name = rest;

            var marker = rest.LastIndexOf("(UUID:", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                name = rest[..marker].Trim();
                var tail = rest[(marker + 6)..].Trim();
                var close = tail.IndexOf(')', StringComparison.Ordinal);
                uuid = (close < 0 ? tail : tail[..close]).Trim();
            }

            if (uuid.Length == 0)
            {
                continue; // UUID の無い行は同定に使えない（裁定 34）
            }

            gpus.Add(new GpuInfo(uuid, name, index, 0, null, null, null, GpuSource.NvidiaSmi));
        }

        return gpus;
    }

    /// <summary>
    /// <c>nvidia-smi --query-gpu=… --format=csv</c>（見出しつき）。
    /// <para>
    /// 見出しで欄を対応づけるので、<b>欄の順が変わっても・欄が欠けても読める</b>
    /// （固定入力の 3090 機の逐語は <c>index, name, uuid, pci.bus_id, driver_version</c> の 5 欄＝
    /// <c>memory.total</c> が無い＝VRAM は 0 になる）。単位つき（<c>24576 MiB</c>）でも
    /// <c>nounits</c> でも読む。
    /// </para>
    /// </summary>
    public static IReadOnlyList<GpuInfo> ParseQuery(string? output)
    {
        var gpus = new List<GpuInfo>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return gpus;
        }

        string[]? header = null;
        foreach (var raw in SplitLines(output))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var cells = SplitCsv(line);
            if (header is null)
            {
                header = NormalizeHeader(cells);
                continue;
            }

            var index = 0;
            var name = string.Empty;
            var uuid = string.Empty;
            string? pci = null;
            string? driver = null;
            long memoryBytes = 0;

            for (var i = 0; i < cells.Length && i < header.Length; i++)
            {
                var value = cells[i].Trim();
                switch (header[i])
                {
                    case "index":
                        _ = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
                        break;
                    case "name":
                        name = value;
                        break;
                    case "uuid":
                        uuid = value;
                        break;
                    case "pci.bus_id":
                        pci = value;
                        break;
                    case "driver_version":
                        driver = value;
                        break;
                    case "memory.total":
                        memoryBytes = ParseMegabytes(value);
                        break;
                    default:
                        break;
                }
            }

            if (uuid.Length == 0)
            {
                continue;
            }

            gpus.Add(new GpuInfo(uuid, name, index, memoryBytes, pci, null, driver, GpuSource.NvidiaSmi));
        }

        return gpus;
    }

    /// <summary>
    /// 2 つの読みを合わせる（<c>-L</c> は名前と UUID・<c>--query-gpu</c> は VRAM とドライバ版）。
    /// 同定は UUID（裁定 34）。片方にしか居ない個体も落とさない。
    /// </summary>
    public static IReadOnlyList<GpuInfo> Merge(
        IReadOnlyList<GpuInfo> fromList,
        IReadOnlyList<GpuInfo> fromQuery)
    {
        ArgumentNullException.ThrowIfNull(fromList);
        ArgumentNullException.ThrowIfNull(fromQuery);

        var merged = new List<GpuInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var query in fromQuery)
        {
            var key = GpuResolver.NormalizeUuid(query.Uuid);
            var listed = GpuResolver.Find(fromList, query.Uuid);
            merged.Add(query with
            {
                Name = string.IsNullOrWhiteSpace(query.Name) ? listed?.Name ?? string.Empty : query.Name,
            });
            seen.Add(key);
        }

        foreach (var listed in fromList)
        {
            if (seen.Add(GpuResolver.NormalizeUuid(listed.Uuid)))
            {
                merged.Add(listed);
            }
        }

        merged.Sort(static (a, b) => a.Index.CompareTo(b.Index));
        return merged;
    }

    /// <summary>
    /// <c>--query-gpu</c> の 1 行から読んだドライバ版（見つからなければ null）。
    /// <b>ドライバ検査（<see cref="DriverRequirement"/>）の唯一の入力</b>。
    /// </summary>
    public static string? DriverVersion(IReadOnlyList<GpuInfo> gpus)
    {
        ArgumentNullException.ThrowIfNull(gpus);
        foreach (var gpu in gpus)
        {
            if (!string.IsNullOrWhiteSpace(gpu.DriverVersion))
            {
                return gpu.DriverVersion;
            }
        }

        return null;
    }

    private static string[] NormalizeHeader(string[] cells)
    {
        var header = new string[cells.Length];
        for (var i = 0; i < cells.Length; i++)
        {
            var name = cells[i].Trim().ToLowerInvariant();

            // "memory.total [MiB]" → "memory.total"
            var bracket = name.IndexOf('[', StringComparison.Ordinal);
            if (bracket > 0)
            {
                name = name[..bracket].Trim();
            }

            header[i] = name;
        }

        return header;
    }

    /// <summary>MiB の値（単位つきでも読む）をバイトに直す。読めなければ 0。</summary>
    private static long ParseMegabytes(string value)
    {
        var digits = value;
        var space = digits.IndexOf(' ', StringComparison.Ordinal);
        if (space > 0)
        {
            digits = digits[..space];
        }

        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var mib)
            ? (long)(mib * 1024 * 1024)
            : 0;
    }

    private static string[] SplitCsv(string line) => line.Split(',');

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
