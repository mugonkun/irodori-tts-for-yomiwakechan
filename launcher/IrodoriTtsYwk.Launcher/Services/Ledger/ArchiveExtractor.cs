using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace IrodoriTtsYwk.Launcher.Services.Ledger;

/// <summary>展開 1 回の結果。</summary>
/// <param name="Files">置いた檔の数。</param>
/// <param name="Bytes">置いた総バイト。</param>
public readonly record struct ExtractResult(int Files, long Bytes);

/// <summary>
/// zip と tar.gz の展開（<c>build/assemble-runtime.ps1</c> の <c>Expand-YwkZip</c> と
/// <c>tar -xzf</c> に相当）。<b>tar は .NET の <see cref="System.Formats.Tar"/></b>＝
/// 利用者機の <c>tar.exe</c> に依らない（裁定 43 の逐語）。
/// <para>
/// <b>檔名は必ず行き先の中に収める</b>（zip slip）＝<c>..</c> や絶対パスを名乗る entry は
/// 展開せずに投げる。第三者の archive を 100 件超え展開する経路なので、ここは削らない。
/// </para>
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>zip を丸ごと展開する（既存檔は上書き）。</summary>
    public static ExtractResult ExtractZip(string zipPath, string destination, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination);
        var files = 0;
        var bytes = 0L;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 末尾が区切りの entry はディレクトリ（zip の作法）
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(ResolveInside(root, entry.FullName, zipPath));
                continue;
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var target = ResolveInside(root, entry.FullName, zipPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            files++;
            bytes += entry.Length;
        }

        return new ExtractResult(files, bytes);
    }

    /// <summary>
    /// <c>.tar.gz</c> を展開する（sdist）。<c>tar.exe</c> は使わない。
    /// ディレクトリと普通の檔だけを置き、それ以外（symlink・device 等）は無視する
    /// ＝PyPI の sdist に居てはいけない物であり、Windows では作れない。
    /// </summary>
    public static async Task<ExtractResult> ExtractTarGzAsync(
        string tarGzPath, string destination, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tarGzPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination);
        var files = 0;
        var bytes = 0L;

        await using var raw = new FileStream(
            tarGzPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        await using var gzip = new GZipStream(raw, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip, leaveOpen: true);

        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false)
               is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = entry.Name.Replace('\\', '/').TrimStart('.', '/');
            if (name.Length == 0)
            {
                continue;
            }

            if (entry.EntryType is TarEntryType.Directory or TarEntryType.DirectoryList)
            {
                Directory.CreateDirectory(ResolveInside(root, name, tarGzPath));
                continue;
            }

            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile
                or TarEntryType.ContiguousFile))
            {
                continue;
            }

            var target = ResolveInside(root, name, tarGzPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var file = new FileStream(
                target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            if (entry.DataStream is { } data)
            {
                await data.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            files++;
            bytes += file.Length;
        }

        return new ExtractResult(files, bytes);
    }

    /// <summary>
    /// 展開先に出来た「唯一のトップレベルディレクトリ」を返す
    /// （<c>assemble-runtime.ps1</c> の「expected exactly one top level directory」と同じ検分）。
    /// </summary>
    public static string SingleRootDirectory(string extractedTo, string archiveName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extractedTo);
        var roots = Directory.GetDirectories(extractedTo);
        if (roots.Length != 1)
        {
            throw new InvalidOperationException(
                archiveName + " のトップレベルのディレクトリが 1 つではない（" + roots.Length + " 個）。");
        }

        return roots[0];
    }

    /// <summary>ディレクトリを丸ごと写す（行き先が在れば消してから）。</summary>
    public static ExtractResult CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        Directory.CreateDirectory(destination);
        var files = 0;
        var bytes = 0L;

        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            files++;
            bytes += new FileInfo(target).Length;
        }

        return new ExtractResult(files, bytes);
    }

    /// <summary>
    /// ディレクトリの中身を 1 段上へ移す（wheel の <c>&lt;name&gt;.data/purelib</c> の合流）。
    /// 戻り値は動かした檔の数。
    /// </summary>
    public static int MoveDirectoryContents(string source, string destination, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        Directory.CreateDirectory(destination);
        var moved = 0;

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(file, target);
            moved++;
        }

        Directory.Delete(source, recursive: true);
        return moved;
    }

    /// <summary>行き先の中に収まる絶対パスに直す（外へ出る entry は投げる）。</summary>
    private static string ResolveInside(string root, string entryName, string archiveName)
    {
        var relative = entryName.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        var full = Path.GetFullPath(Path.Combine(root, relative));
        var guard = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!full.StartsWith(guard, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                Path.GetFileName(archiveName) + " が展開先の外を指す項目を持っている：" + entryName);
        }

        return full;
    }

    /// <summary>展開先の檔の数と総バイト（報告に使う）。</summary>
    public static ExtractResult Measure(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            return new ExtractResult(0, 0);
        }

        var files = 0;
        var bytes = 0L;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            files++;
            bytes += new FileInfo(file).Length;
        }

        return new ExtractResult(files, bytes);
    }

    /// <summary>使い捨てのディレクトリ（展開の作業場）。</summary>
    public static string CreateScratch(string parent, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        var path = Path.Combine(parent, name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>作業場を消す（消せなくても止めない）。</summary>
    public static void RemoveScratch(string path)
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

    /// <summary>展開先の直下の <c>*.pth</c>（<c>import site</c> が無い環境では読まれない檔）。</summary>
    public static IReadOnlyList<string> TopLevelPthFiles(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.pth", SearchOption.TopDirectoryOnly)
            : [];
}
