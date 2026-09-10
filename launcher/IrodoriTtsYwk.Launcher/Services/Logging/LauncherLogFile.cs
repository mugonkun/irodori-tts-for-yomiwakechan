using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace IrodoriTtsYwk.Launcher.Services.Logging;

/// <summary>
/// <b>画面に出た 1 行を檔にも残す</b>（裁定 126 の C（1））。
/// <para>
/// <b>なぜ要るか</b>＝v1.0.2 を清潔導入した機体で <c>&lt;データ樹&gt;\logs\</c> が<b>空のまま</b>だった
/// （司令官の実射・2026-09-10）。状態帯のログは末尾 20 行の環（<c>LogTail</c>）で、窓を閉じれば消える。
/// 失敗の理由を後から読む路が 1 本も無く、「起動しない」の報告に添える物が無い。
/// </para>
/// <para>
/// <b>作りは薄く</b>＝1 行ごとに開いて足して閉じる（<c>File.AppendAllText</c>）。日ごとに 1 檔
/// （<c>launcher-yyyyMMdd.log</c>）・UTF-8（BOM なし）。走っている個体は 1 つ（単一起動の錠）なので
/// 書き手の取り合いは起きないが、<b>どんな IO の失敗も UI へ投げ返さない</b>＝
/// ログが書けないことでアプリが止まる方が悪い。
/// </para>
/// </summary>
public sealed class LauncherLogFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly string _logDir;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();

    /// <param name="logDir"><c>AppPaths.LogDir</c>（無ければ最初の 1 行で作る）。</param>
    /// <param name="clock">時計（試験が差す）。既定＝端末の現地時刻。</param>
    public LauncherLogFile(string logDir, Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDir);
        _logDir = logDir;
        _clock = clock ?? (static () => DateTimeOffset.Now);
    }

    /// <summary>その日の檔の路（<b>純関数</b>）。</summary>
    public static string PathFor(string logDir, DateTimeOffset when)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDir);
        return Path.Combine(
            logDir,
            "launcher-" + when.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
    }

    /// <summary>いま書いている檔の路。</summary>
    public string CurrentPath => PathFor(_logDir, _clock());

    /// <summary>檔に落とす 1 行（<b>純関数</b>＝時刻を頭に付ける）。</summary>
    public static string FormatLine(DateTimeOffset when, string line) =>
        when.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + line;

    /// <summary>
    /// 1 行足す（<b>投げない</b>＝書けなければ黙って諦める）。空白だけの行は書かない。
    /// </summary>
    public void Append(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var now = _clock();
        var text = FormatLine(now, line.Trim()) + Environment.NewLine;

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_logDir);
                File.AppendAllText(PathFor(_logDir, now), text, Utf8NoBom);
            }
        }
        catch (IOException)
        {
            // 置き場が無い・掴まれている＝ログのために画面を止めない
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
        catch (NotSupportedException)
        {
            // 路が檔名として通らない（開発起動で妙な env を差した等）
        }
    }
}
