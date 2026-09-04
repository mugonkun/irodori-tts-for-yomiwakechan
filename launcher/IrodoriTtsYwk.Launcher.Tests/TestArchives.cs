using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// テストの中で小さな wheel／sdist／archive／埋め込み Python を<b>合成する</b>道具。
/// 実物の台帳（1.4 GB）には触らず、展開の規則だけを釘付けするため。
/// </summary>
internal static class TestArchives
{
    /// <summary>使い捨てのディレクトリ（テストごとに新しい名前）。</summary>
    public static string NewTempDir(string label)
    {
        var path = Path.Combine(
            Path.GetTempPath(), "ywk-launcher-test", label + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }

    public static void Remove(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>檔名 → 中身（UTF-8）の並びから zip を作る。</summary>
    public static string WriteZip(string path, IReadOnlyDictionary<string, string> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, body) in entries)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(body);
        }

        return path;
    }

    /// <summary>檔名 → 中身の並びから <c>.tar.gz</c> を作る（sdist の型）。</summary>
    public static string WriteTarGz(string path, IReadOnlyDictionary<string, string> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var gzip = new GZipStream(stream, CompressionLevel.Fastest);
        using var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true);
        foreach (var (name, body) in entries)
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(body)),
            };
            tar.WriteEntry(entry);
        }

        return path;
    }

    /// <summary>檔の sha256（小文字 hex）。</summary>
    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>バイト列の sha256（小文字 hex）。</summary>
    public static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>
    /// 埋め込み Python の代わり＝<c>python.exe</c> と素の <c>python312._pth</c> を持つ zip。
    /// （中身は走らせないので、在ることだけが意味を持つ。）
    /// </summary>
    public static string WritePythonEmbedZip(string path) => WriteZip(path, new Dictionary<string, string>
    {
        ["python.exe"] = "not a real exe",
        ["python312.zip"] = "stdlib",
        ["python312._pth"] = "python312.zip\n.\n\n#import site\n",
        ["LICENSE.txt"] = "PSF-2.0",
    });
}
