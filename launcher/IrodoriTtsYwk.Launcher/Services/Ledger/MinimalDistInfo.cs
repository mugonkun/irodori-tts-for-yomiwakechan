using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IrodoriTtsYwk.Launcher.Services.Ledger;

/// <summary>
/// wheel を出していない依存（<c>sdist</c>・<c>archive</c>）のために最小の <c>*.dist-info</c> を作る。
/// <para>
/// 要る理由＝<b>利用者機に pip は無い</b>（<c>._pth</c> 環境では <c>pip</c> が <c>find_spec</c> で
/// None）ので、誰も metadata を書かない。transformers は <c>importlib.metadata.version()</c> を
/// 呼ぶ（<c>ledger/README.md</c> §2）。<c>build/assemble-runtime.ps1</c> の
/// <c>Write-YwkMinimalDistInfo</c> と同じ檔・同じ並び（LF・末尾改行）。
/// </para>
/// <para>
/// <b>台本との唯一の差</b>＝<c>WHEEL</c> の <c>Generator:</c> と <c>METADATA</c> 末尾の 1 行が
/// 「誰が置いたか」を名乗るので、ランチャが置いた樹と <c>build/out/runtime-&lt;変種&gt;</c> は
/// この 2 行だけ違う（<c>sdist</c>・<c>archive</c> の 4 件のみ）。中身の意味は同じで、
/// <c>importlib.metadata</c> が読むのは <c>METADATA</c> の <c>Name</c>／<c>Version</c>。
/// </para>
/// </summary>
public static class MinimalDistInfo
{
    /// <summary><c>INSTALLER</c> に書く名（台本と同じ）。</summary>
    public const string InstallerName = "irodori-tts-ywk";

    /// <summary><c>WHEEL</c> の <c>Generator:</c>（台本は <c>assemble-runtime.ps1</c> を名乗る）。</summary>
    public const string Generator = "irodori-tts-ywk IrodoriTtsYwk.Launcher";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary><c>*.dist-info</c> のディレクトリ名（<b>純関数</b>＝<c>-</c> を <c>_</c> に）。</summary>
    public static string DirectoryName(string name, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        return name.Replace('-', '_') + "-" + version + ".dist-info";
    }

    /// <summary><c>METADATA</c> の本文（<b>純関数</b>・LF・末尾改行 1 本）。</summary>
    public static string RenderMetadata(string name, string version, string? license)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var lines = new List<string>
        {
            "Metadata-Version: 2.1",
            "Name: " + name,
            "Version: " + version,
        };

        if (!string.IsNullOrWhiteSpace(license))
        {
            lines.Add("License: " + license);
        }

        lines.Add(string.Empty);
        lines.Add("Installed by the irodori-tts-ywk launcher from a source archive listed in the ledger.");
        return string.Join("\n", lines) + "\n";
    }

    /// <summary><c>WHEEL</c> の本文（<b>純関数</b>）。</summary>
    public static string RenderWheel() =>
        "Wheel-Version: 1.0\nGenerator: " + Generator + "\nRoot-Is-Purelib: true\nTag: py3-none-any\n";

    /// <summary>
    /// <c>site-packages/&lt;name&gt;-&lt;version&gt;.dist-info/</c> に 5 檔置いて、その場所を返す。
    /// </summary>
    public static string Write(
        string sitePackages,
        string name,
        string version,
        string? license,
        IReadOnlyList<string> topLevel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sitePackages);
        ArgumentNullException.ThrowIfNull(topLevel);

        var directory = Path.Combine(sitePackages, DirectoryName(name, version));
        Directory.CreateDirectory(directory);

        WriteLf(Path.Combine(directory, "METADATA"), RenderMetadata(name, version, license));
        WriteLf(Path.Combine(directory, "WHEEL"), RenderWheel());
        WriteLf(Path.Combine(directory, "INSTALLER"), InstallerName + "\n");
        WriteLf(Path.Combine(directory, "RECORD"), string.Empty);
        if (topLevel.Count > 0)
        {
            WriteLf(Path.Combine(directory, "top_level.txt"), string.Join("\n", topLevel) + "\n");
        }

        return directory;
    }

    private static void WriteLf(string path, string text) =>
        File.WriteAllText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal), Utf8NoBom);
}
