using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IrodoriTtsYwk.Launcher.Services.Gpu;

/// <summary>
/// 見張りの 1 標本ごとに OS の GPU 計数を採る係（裁定 110 D5・2026-09-08）。
/// <para>
/// <b>置き場は見張りの糸</b>（<c>ServerProcess.StartWatch</c>）＝窓は読むだけ（low 3・裁定 88 ⑶）。
/// PDH は collect も<b>開設も</b>UI の糸で回さない。
/// </para>
/// <para>
/// <b>query は最初の標本の回に開く</b>（是正・2026-09-08）＝<see cref="CreateForMachine"/> は
/// 口を用意するだけで <c>pdh.dll</c> を 1 度も叩かない。開設は実測で<b>プロセスの最初の 1 回だけ
/// 0.23〜0.28 s</b> 掛かる（perflib の初期化）ので、これをコンストラクタで撃つと
/// <see cref="Services.LauncherComposition"/>（＝<c>App.OnStartup</c>＝<b>UI の糸</b>・窓を作る前）が
/// その分だけ止まっていた。CPU 変種のように行が 1 度も出ない機体でも同じ代金を払っていた。
/// </para>
/// <para>
/// <b>落ちても画面は止めない</b>＝<c>pdh.dll</c> が無い・counter の組が無い・COM が落ちた、の
/// どれでも<b>行が出ないだけ</b>にする。理由 1 行は<b>プロセスにつき 1 度だけ</b>ログへ流す
/// （見張りは 2 秒ごとに来るので、毎回書くとログ帯の 20 行が理由で埋まる）。
/// </para>
/// <para>
/// <b>DXGI は数え直しを惜しむ</b>＝アダプタの名前と総量は動かないので 1 度読んで持ち、
/// 知らない LUID が出てきたときだけ（かつ <see cref="AdapterRefreshInterval"/> に 1 度まで）
/// 数え直す。PDH の標本（instance の数だけ動く）は毎回読む。
/// </para>
/// </summary>
public sealed class OsGpuMemorySampler : IDisposable
{
    /// <summary>DXGI を数え直す最短の間（知らない LUID が出たときだけ効く）。</summary>
    public static readonly TimeSpan AdapterRefreshInterval = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Func<OpenedSources>? _open;
    private readonly Action<string>? _log;

    private IGpuMemoryCounters? _counters;
    private IGpuAdapterInfoSource? _adapterSource;
    private IReadOnlyList<GpuAdapterInfo> _adapters = [];
    private long _adaptersReadAt;
    private bool _adaptersRead;
    private bool _opened;
    private bool _logged;
    private bool _disposed;

    /// <summary>テスト用（偽の計数と偽の DXGI を差す＝<b>もう開く物は無い</b>）。</summary>
    /// <param name="counters">計数の口（null＝OS の行は出ない）。</param>
    /// <param name="adapterSource">アダプタの口（null＝名前と総量が出ない）。</param>
    /// <param name="log">理由 1 行の置き場（<b>1 度だけ</b>呼ばれる）。</param>
    public OsGpuMemorySampler(
        IGpuMemoryCounters? counters, IGpuAdapterInfoSource? adapterSource, Action<string>? log = null)
    {
        _counters = counters;
        _adapterSource = adapterSource;
        _log = log;
        _opened = true;
    }

    /// <summary>
    /// <b>遅れ開き</b>（実機用＝<see cref="CreateForMachine"/>）。
    /// <paramref name="open"/> は<b>最初の <see cref="Sample"/> の回に 1 度だけ</b>呼ばれる
    /// （＝見張りの糸）。テストからは「いつ開くか」を釘付けするために使う。
    /// </summary>
    /// <param name="open">口 2 つと、開けなかった理由 1 行を返す手。</param>
    /// <param name="log">理由 1 行の置き場（<b>1 度だけ</b>呼ばれる）。</param>
    public OsGpuMemorySampler(Func<OpenedSources> open, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(open);
        _open = open;
        _log = log;
    }

    /// <summary>
    /// 実機用（PDH＋DXGI）。<b>ここでは何も開かない</b>（是正・2026-09-08）＝
    /// <c>pdh.dll</c> を叩くのは<b>最初の <see cref="Sample"/>＝見張りの糸</b>で、
    /// 開けなければ理由 1 行だけ流して以後は黙って空を返す（呼び手に分岐を作らない）。
    /// </summary>
    public static OsGpuMemorySampler CreateForMachine(Action<string>? log = null) =>
        new(
            static () =>
            {
                var counters = PdhGpuMemoryCounters.TryOpen(out var reason);
                return new OpenedSources(
                    counters,
                    counters is null ? null : new DxgiGpuAdapters(),
                    counters is null ? reason ?? "GPU の計数を開けませんでした。" : null);
            },
            log);

    /// <summary>
    /// その pid が使っている GPU の行（<b>例外を投げない</b>＝読めなければ空）。
    /// </summary>
    /// <param name="pid">TTS の wrapper の pid（null＝起こしていない＝空）。</param>
    public IReadOnlyList<OsGpuMemoryRow> Sample(int? pid)
    {
        if (pid is null)
        {
            return [];
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return [];
            }

            // **開くのはここ＝見張りの糸で 1 度だけ**（是正・2026-09-08）。
            // 理由 1 行もここで出す＝コンストラクタの時点ではログ帯をまだ誰も購読していない。
            if (!_opened)
            {
                _opened = true;
                try
                {
                    var sources = _open!();
                    _counters = sources.Counters;
                    _adapterSource = sources.Adapters;
                    if (sources.FailureReason is string why)
                    {
                        Report(why);
                    }
                }
                catch (SystemException ex)
                {
                    // 開く手そのものが投げた（例外を出さない約束の外側）＝行が出ないだけにする。
                    Report("GPU の計数を開けませんでした（" + ex.Message + "）。");
                }
            }

            if (_counters is null)
            {
                return [];
            }

            GpuCounterSample? sample;
            string? failureReason;
            try
            {
                sample = _counters.Sample(out failureReason);
            }
            catch (SystemException ex)
            {
                Report("GPU の計数を読めませんでした（" + ex.Message + "）。");
                return [];
            }

            if (sample is null)
            {
                // 採れなかった理由も**プロセスにつき 1 度だけ**出す（是正・2026-09-08＝
                // counter の組は在るのに collect がずっと失敗する機体で、帯から組が消えたきり
                // ログに 1 行も出ないのを直す）。
                if (failureReason is string reason)
                {
                    Report(reason);
                }

                return [];
            }

            var rows = OsGpuMemory.Aggregate(
                pid, sample.Processes, sample.Adapters, sample.LocalAdapters, ReadAdapters(false));

            // 名前も総量も付かない行が出た＝知らない LUID（GPU を差し替えた・後から現れた）。
            // 数え直すのは間を空けて 1 度だけ（毎標本 DXGI を開き直さない）。
            if (NeedsAdapterRefresh(rows))
            {
                rows = OsGpuMemory.Aggregate(
                    pid, sample.Processes, sample.Adapters, sample.LocalAdapters, ReadAdapters(true));
            }

            return rows;
        }
    }

    /// <summary>計数の口を閉じる。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _opened = true;   // 閉じた後に開き直さない
            _counters?.Dispose();
            _counters = null;
        }
    }

    /// <summary>遅れ開きの結果（口 2 つと、開けなかった理由 1 行）。</summary>
    /// <param name="Counters">計数の口（null＝開けなかった）。</param>
    /// <param name="Adapters">アダプタの口（null＝名前と総量が出ない）。</param>
    /// <param name="FailureReason">開けなかった理由（開けたときは null）。</param>
    public sealed record OpenedSources(
        IGpuMemoryCounters? Counters, IGpuAdapterInfoSource? Adapters, string? FailureReason);

    private bool NeedsAdapterRefresh(IReadOnlyList<OsGpuMemoryRow> rows)
    {
        if (_adapterSource is null || rows.Count == 0)
        {
            return false;
        }

        var readJustNow = _adaptersRead
            && Stopwatch.GetElapsedTime(_adaptersReadAt) < AdapterRefreshInterval;
        if (readJustNow)
        {
            return false;
        }

        foreach (var row in rows)
        {
            if (row.TotalBytes is null)
            {
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<GpuAdapterInfo> ReadAdapters(bool force)
    {
        if (_adapterSource is null)
        {
            return [];
        }

        if (_adaptersRead && !force)
        {
            return _adapters;
        }

        try
        {
            _adapters = _adapterSource.Adapters() ?? [];
        }
        catch (SystemException ex)
        {
            _adapters = [];
            Report("GPU の名前と総量を数えられませんでした（" + ex.Message + "）。");
        }

        _adaptersRead = true;
        _adaptersReadAt = Stopwatch.GetTimestamp();
        return _adapters;
    }

    /// <summary>理由 1 行（<b>プロセスにつき 1 度</b>）。</summary>
    private void Report(string reason)
    {
        if (_logged)
        {
            return;
        }

        _logged = true;
        _log?.Invoke("ywk_launcher: " + reason + "（GPU メモリの「このプロセス」「GPU 全体」は出ません）");
    }
}
