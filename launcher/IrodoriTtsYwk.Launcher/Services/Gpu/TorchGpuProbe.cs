using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Gpu;

/// <summary>
/// 変種の python で torch に GPU を数えさせる路（<c>nvidia-smi</c> が無い機体＝AMD・不明）。
/// <para>
/// <b>本檔は台本と読みだけ</b>（起こすのは <see cref="GpuEnumerator"/>）。台本は
/// <b>配布樹（<c>app/server</c>）には置かない</b>＝ランチャの中の文字列を一時檔へ書いて渡す
/// （導入先が Program Files でも書けない・配布樹に檔を増やさない）。
/// </para>
/// <para>
/// 出力は <b>1 行の JSON</b>（前置 <see cref="Marker"/>）。torch は import のときに警告を吐くので、
/// 標準出力の全部を JSON として読んではいけない＝印のある行だけを読む。
/// </para>
/// <para>
/// 読む欄は <c>research/lab/kit-cuda/gpu_props.py</c> と同じ（<c>uuid</c>・<c>name</c>・
/// <c>total_memory</c>・<c>pci_bus_id</c>・<c>gcnArchName</c>）＝ROCm でも CUDA でも取れることが
/// 実測されている（<c>29</c> §5・<c>33</c> 追記）。
/// </para>
/// </summary>
public static class TorchGpuProbe
{
    /// <summary>JSON の 1 行に付ける印（torch の警告と混ざらないため）。</summary>
    public const string Marker = "YWK_GPU_JSON ";

    /// <summary>一時檔の名前（<c>%TEMP%</c> の下に置いて、読んだら消す）。</summary>
    public const string ScriptFileName = "ywk_gpu_probe.py";

    /// <summary>
    /// 列挙の台本（ASCII のみ・依存は torch と標準ライブラリだけ）。
    /// <b>何も載せない・何も書かない</b>＝<c>get_device_properties</c> を読むだけ。
    /// </summary>
    public const string Script = """
import json
import sys

out = {"ok": False, "torch": None, "cuda": None, "hip": None, "count": 0, "devices": [], "error": None}
try:
    import torch

    out["torch"] = torch.__version__
    out["cuda"] = torch.version.cuda
    out["hip"] = torch.version.hip
    if torch.cuda.is_available():
        count = torch.cuda.device_count()
        out["count"] = count
        for i in range(count):
            p = torch.cuda.get_device_properties(i)
            out["devices"].append({
                "index": i,
                "name": str(getattr(p, "name", "")),
                "uuid": str(getattr(p, "uuid", "")) if hasattr(p, "uuid") else "",
                "total_memory": int(getattr(p, "total_memory", 0) or 0),
                "pci_bus_id": str(getattr(p, "pci_bus_id", "")) if hasattr(p, "pci_bus_id") else None,
                "gcn_arch": str(getattr(p, "gcnArchName", "")) if hasattr(p, "gcnArchName") else None,
            })
    out["ok"] = True
except Exception as exc:  # noqa: BLE001 -- a diagnostic must never be the failure
    out["error"] = "%s: %s" % (type(exc).__name__, exc)

sys.stdout.write("YWK_GPU_JSON " + json.dumps(out) + "\n")
sys.stdout.flush()
""";

    /// <summary>台本の出力を読む（<b>純関数</b>）。</summary>
    /// <param name="stdout">標準出力の全部（torch の警告が混ざっていてよい）。</param>
    public static TorchProbeResult Parse(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return new TorchProbeResult([], null, null, null, "GPU の列挙が何も返しませんでした。");
        }

        string? json = null;
        foreach (var raw in stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            var at = line.IndexOf(Marker, StringComparison.Ordinal);
            if (at >= 0)
            {
                json = line[(at + Marker.Length)..];
            }
        }

        if (json is null)
        {
            return new TorchProbeResult([], null, null, null, "GPU の列挙の出力に印がありませんでした。");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var torch = Text(root, "torch");
            var cuda = Text(root, "cuda");
            var hip = Text(root, "hip");
            var error = Text(root, "error");

            var gpus = new List<GpuInfo>();
            if (root.TryGetProperty("devices", out var devices)
                && devices.ValueKind == JsonValueKind.Array)
            {
                foreach (var device in devices.EnumerateArray())
                {
                    var uuid = Text(device, "uuid") ?? string.Empty;
                    if (uuid.Length == 0)
                    {
                        continue; // UUID の無い個体は同定できない（裁定 34）
                    }

                    gpus.Add(new GpuInfo(
                        uuid,
                        Text(device, "name") ?? string.Empty,
                        Number(device, "index"),
                        Long(device, "total_memory"),
                        Text(device, "pci_bus_id"),
                        NullIfEmpty(Text(device, "gcn_arch")),
                        null,
                        GpuSource.TorchProbe));
                }
            }

            return new TorchProbeResult(gpus, torch, cuda, hip, error);
        }
        catch (JsonException ex)
        {
            return new TorchProbeResult([], null, null, null, "GPU の列挙の出力が読めませんでした：" + ex.Message);
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;

    private static long Long(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var parsed)
            ? parsed
            : 0;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>台本の読み。</summary>
    /// <param name="Gpus">見つかった GPU（index 昇順で来る）。</param>
    /// <param name="TorchVersion"><c>torch.__version__</c>。</param>
    /// <param name="CudaVersion"><c>torch.version.cuda</c>（ROCm ビルドは null）。</param>
    /// <param name="HipVersion"><c>torch.version.hip</c>（CUDA ビルドは null）。</param>
    /// <param name="Error">読めなかった・torch が落ちた理由 1 行。</param>
    public sealed record TorchProbeResult(
        IReadOnlyList<GpuInfo> Gpus,
        string? TorchVersion,
        string? CudaVersion,
        string? HipVersion,
        string? Error)
    {
        /// <summary>ROCm ビルドか（<c>hip</c> が読めた＝Radeon 機）。</summary>
        public bool IsRocmBuild => !string.IsNullOrWhiteSpace(HipVersion);

        /// <summary>UI に出す 1 行（版の告知）。</summary>
        public string Describe() =>
            "torch " + (TorchVersion ?? "?")
            + (CudaVersion is null ? string.Empty : "／cuda " + CudaVersion)
            + (HipVersion is null ? string.Empty : "／hip " + HipVersion)
            + "・GPU " + Gpus.Count.ToString(CultureInfo.InvariantCulture) + " 台";
    }
}
