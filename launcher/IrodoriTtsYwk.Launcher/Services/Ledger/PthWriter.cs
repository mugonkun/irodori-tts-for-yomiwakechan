using System;
using System.IO;
using System.Text;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Ledger;

/// <summary>
/// <c>python312._pth</c> を書く（<c>build/assemble-runtime.ps1</c> §3 と 1 対 1）。
/// <para>
/// <b>パスは <c>._pth</c> の専管・設定は env</b>＝<c>._pth</c> が在ると <c>PYTHONPATH</c> は無視される。
/// 混ぜない（launcher/README §4 落とし穴 1）。
/// </para>
/// <para>
/// 檔は <b>UTF-8（BOM なし）・CRLF・末尾に改行 1 本</b>（<c>Common.ps1</c> の
/// <c>Write-YwkTextFile -Newline CRLF</c> と同じバイト）。書く前に変種ディレクトリの
/// <c>*._pth</c> を全部消す＝埋め込み Python の zip が同梱している素の <c>python312._pth</c> が
/// 残ると、こちらの書いた行と二重になる。
/// </para>
/// </summary>
public sealed class PthWriter : IPthWriter
{
    /// <summary>書く檔の名前（埋め込み Python 3.12 の zip が名乗る物と同じ）。</summary>
    public const string PthFileName = "python312._pth";

    /// <summary>BOM を付けない UTF-8（<c>Common.ps1</c> の <c>UTF8Encoding($false)</c>）。</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public string Render(string template, string runtimeDir, string appDir) =>
        PthTemplate.Render(template, runtimeDir, appDir);

    /// <inheritdoc />
    public string Write(string templatePath, string runtimeDir, string appDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDir);

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException(
                "python312._pth の雛形が配布樹に無い：" + Path.GetFileName(templatePath), templatePath);
        }

        var template = File.ReadAllText(templatePath, Encoding.UTF8);
        var text = Render(template, runtimeDir, appDir);

        Directory.CreateDirectory(runtimeDir);
        foreach (var stale in Directory.GetFiles(runtimeDir, "*._pth", SearchOption.TopDirectoryOnly))
        {
            File.Delete(stale);
        }

        var path = Path.Combine(runtimeDir, PthFileName);
        File.WriteAllText(path, text, Utf8NoBom);
        return path;
    }
}
