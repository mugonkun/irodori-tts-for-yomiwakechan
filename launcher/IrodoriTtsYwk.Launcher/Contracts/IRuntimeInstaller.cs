using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>展開の段。</summary>
public enum InstallPhase
{
    Pending,

    /// <summary>埋め込み Python の zip を変種ディレクトリへ。</summary>
    PythonEmbed,

    /// <summary>wheel／sdist／archive を <c>site-packages/</c> へ。</summary>
    Packages,

    /// <summary><c>python312._pth</c> を書く。</summary>
    PthFile,

    /// <summary>展開後の検分（<c>*.pth</c> の残り・dist-info の件数）。</summary>
    Verifying,

    Done,

    Failed,
}

/// <summary>展開 1 回の注文。</summary>
/// <param name="Ledger">読んだ取得台帳（<c>runtime-&lt;変種&gt;</c>）。</param>
/// <param name="CacheDir">検証済みの原檔が居る場所（<see cref="IDownloader"/> の着地先）。</param>
/// <param name="RuntimeDir">変種ディレクトリ（<c>python.exe</c> と <c>site-packages/</c> の親）。</param>
/// <param name="AppDir">配布樹の根（<c>._pth</c> の <c>@APP_DIR@</c>）。</param>
/// <param name="PthTemplatePath"><c>server/python312._pth.template</c>。</param>
public sealed record InstallRequest(
    LedgerFile Ledger,
    string CacheDir,
    string RuntimeDir,
    string AppDir,
    string PthTemplatePath);

/// <param name="Phase">段。</param>
/// <param name="ItemName">いま展開している物。</param>
/// <param name="Done">済んだ件数。</param>
/// <param name="Total">全件数。</param>
public sealed record InstallProgress(InstallPhase Phase, string? ItemName, int Done, int Total);

/// <param name="Ok">通ったか。</param>
/// <param name="Files">置いた檔の数（実測 26,523 檔＝<c>ledger/README.md</c> §9）。</param>
/// <param name="Bytes">置いた総バイト。</param>
/// <param name="DistInfoCount"><c>*.dist-info</c> の件数（台帳の件数と合うべき）。</param>
/// <param name="DroppedPth">捨てた <c>*.pth</c>（<c>import site</c> が無い環境では読まれない檔）。</param>
/// <param name="PthPath">書いた <c>python312._pth</c>。</param>
/// <param name="FailureReason">理由 1 行。</param>
public sealed record InstallResult(
    bool Ok,
    int Files,
    long Bytes,
    int DistInfoCount,
    IReadOnlyList<string> DroppedPth,
    string? PthPath,
    string? FailureReason);

/// <summary>
/// 契約 ⑶＝取得した原檔を変種ディレクトリへ組み上げる。<b>参照実装は
/// <c>build/assemble-runtime.ps1</c></b>（同じ台帳・同じ規則で組む＝ランチャの結果が
/// <c>build/out/runtime-&lt;変種&gt;</c> と一致しなければどちらかが誤り）。
/// <list type="bullet">
/// <item><c>python-embed</c>＝zip を変種ディレクトリへ展開。</item>
/// <item><c>wheel</c>＝zip を <c>site-packages/</c> へまるごと（<c>*.dist-info</c> 込み＝
/// transformers が <c>importlib.metadata.version()</c> を呼ぶ）。
/// <c>&lt;name&gt;-&lt;ver&gt;.data/purelib|platlib</c> は <c>site-packages/</c> へ合流し、
/// <c>scripts|headers|data</c> は捨てる。</item>
/// <item><c>sdist</c>＝<c>.tar.gz</c> を <see cref="System.Formats.Tar"/> で展開し
/// <c>package_dirs</c> を写して最小 <c>*.dist-info</c> を作る（利用者機に pip は無い）。</item>
/// <item><c>archive</c>＝commit 固定 zip を展開し <c>package_dir</c> を写す。</item>
/// </list>
/// <para>
/// <b><c>site-packages/</c> 直下の <c>*.pth</c> は 1 件残らず捨てる</b>＝配布する <c>._pth</c> は
/// <c>import site</c> を持たないので <c>.pth</c> は読まれない（「何かしているように見えて何もしない檔」＝
/// <c>ledger/README.md</c> §4-4）。捨てた件は <see cref="InstallResult.DroppedPth"/> に全部載せ、
/// 展開後に 1 件でも残っていたら失敗にする。
/// </para>
/// </summary>
public interface IRuntimeInstaller
{
    Task<InstallResult> InstallAsync(
        InstallRequest request,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// <c>python312._pth</c> の生成（<c>server/python312._pth.template</c> →絶対パス）。
/// <para>
/// <b>パスは <c>._pth</c> の専管・設定は env</b>（launcher/README §4 落とし穴 1＝
/// <c>._pth</c> が在ると <c>PYTHONPATH</c> は無視される）。混ぜない。
/// </para>
/// </summary>
public interface IPthWriter
{
    /// <summary>雛形を絶対パスで埋めた本文（CRLF 終端）。</summary>
    string Render(string template, string runtimeDir, string appDir);

    /// <summary>変種ディレクトリの <c>python312._pth</c> を書き換える（古い <c>*._pth</c> は消す）。</summary>
    string Write(string templatePath, string runtimeDir, string appDir);
}

/// <summary>
/// <see cref="IPthWriter.Render"/> の中身は<b>純関数</b>なのでここに置く
/// （<c>build/assemble-runtime.ps1</c> の §3 と 1 対 1・テストで釘付けする）。
/// </summary>
public static class PthTemplate
{
    /// <summary>雛形に残っていてはならない差し込み口。</summary>
    private static readonly Regex Placeholder = new("@[A-Z_]+@", RegexOptions.CultureInvariant);

    /// <summary><c>import site</c> の行（設計書 §2＝配布する <c>._pth</c> は持たない）。</summary>
    private static readonly Regex ImportSite = new(
        @"^\s*import\s+site\s*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// <c>@RUNTIME_DIR@</c>／<c>@APP_DIR@</c> を埋める（**純関数**）。
    /// 差し込み口が残ったら投げる・<c>import site</c> が居たら投げる＝
    /// <c>assemble-runtime.ps1</c> の 2 つの throw と同じ検分。
    /// </summary>
    public static string Render(string template, string runtimeDir, string appDir)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDir);

        var text = template
            .Replace("@RUNTIME_DIR@", ToPosixPath(runtimeDir), StringComparison.Ordinal)
            .Replace("@APP_DIR@", ToPosixPath(appDir), StringComparison.Ordinal);

        var left = Placeholder.Match(text);
        if (left.Success)
        {
            throw new InvalidOperationException(
                "python312._pth に差し込み口が残っている：" + left.Value);
        }

        if (ImportSite.IsMatch(text))
        {
            throw new InvalidOperationException(
                "python312._pth の雛形に \"import site\" があってはならない（設計書 §2）。");
        }

        // 檔は CRLF・末尾は改行 1 本（assemble-runtime.ps1 の Write-YwkTextFile -Newline CRLF と同じ）
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        return normalized.Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
    }

    /// <summary>
    /// <c>._pth</c> に書くパスの形（<c>\</c> を <c>/</c> に・末尾の区切りは落とす）。
    /// <c>assemble-runtime.ps1</c> の <c>ConvertTo-YwkPosixPath</c> と同じ。
    /// </summary>
    public static string ToPosixPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var text = path.Trim().Replace('\\', '/');
        while (text.Length > 3 && text.EndsWith('/'))
        {
            text = text[..^1];
        }

        return text;
    }
}
