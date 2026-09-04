using System;
using System.Collections.Generic;
using System.Linq;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// ログの末尾だけを持つ環（受け入れ条件 D-4）。<b>純ロジック</b>＝窓なしで試せる。
/// <para>
/// 2 つの約束を守る＝⑴ <b>422 の本文 echo を流さない</b>（<see cref="ServerLogParser.ForLog"/> で
/// 畳む＝1 発 5 KB の body がログを埋めるのを塞ぐ）⑵ <b>上限行数を超えたら古い方から捨てる</b>
/// （長時間の常駐でメモリが伸び続けない）。
/// </para>
/// </summary>
public sealed class LogTail
{
    /// <summary>状態帯に出す既定の行数（依頼文＝ログ末尾 20 行）。</summary>
    public const int DefaultCapacity = 20;

    private readonly Queue<string> _lines = new();

    public LogTail(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count => _lines.Count;

    /// <summary>1 行足す（畳んでから入れる）。空行と<b>自分の足音</b>は捨てる。</summary>
    public void Append(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        // 見張り（2 秒ごとの /ywk/status）が生む access log を末尾 20 行に入れない。
        // 入れると 40 秒で D-4 の 3 行が押し出される（実測）。異常な応答は落とさない。
        if (ServerLogParser.IsLauncherPollNoise(line))
        {
            return;
        }

        _lines.Enqueue(ServerLogParser.ForLog(line.TrimEnd()));
        while (_lines.Count > Capacity)
        {
            _lines.Dequeue();
        }
    }

    public void Clear() => _lines.Clear();

    public IReadOnlyList<string> Lines => _lines.ToArray();

    /// <summary>画面に貼る 1 塊（改行区切り・古い行が上）。</summary>
    public string Text => string.Join(Environment.NewLine, _lines);
}
