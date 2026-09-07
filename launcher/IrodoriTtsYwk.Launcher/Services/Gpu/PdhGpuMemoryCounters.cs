using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace IrodoriTtsYwk.Launcher.Services.Gpu;

/// <summary>
/// Windows の GPU 計数（PDH）を読む実機用の口（裁定 110 D1・2026-09-08）。
/// <para>
/// <b>第三者バイナリを増やさない</b>（licences の台帳は .NET と NAudio しか許していない）＝
/// <c>pdh.dll</c> への P/Invoke だけで組む。読む道は 3 本＝
/// <c>\GPU Process Memory(*)\Dedicated Usage</c>（pid ごと）・
/// <c>\GPU Adapter Memory(*)\Dedicated Usage</c>（カードごと＝タスク マネージャーの「専用」）・
/// <c>\GPU Local Adapter Memory(*)\Local Usage</c>（前者に instance が無い機体の控え）。
/// </para>
/// <para>
/// <b>綴りは英語で足す</b>＝<c>PdhAddEnglishCounterW</c>。この機体の Windows は日本語だが
/// counter 名は英語で並んでいる（実測）。それでも英語版の口を使うのは、地域設定で綴りが
/// 変わる機体に配っても同じ道が通るからである。
/// </para>
/// <para>
/// <b>query は 1 本を開きっぱなしにする</b>＝ワイルドカードは <c>PdhCollectQueryData</c> の
/// たびに展開し直されるので、新しく現れた pid も勝手に載る。閉じるのは <see cref="Dispose"/>。
/// メモリ系の counter は瞬時値なので、2 標本の助走は要らない（1 回目の collect で値が出る）。
/// </para>
/// <para>
/// <b>開くのは高い・採るのは安い</b>（実測・この機体・2026-09-08）＝<see cref="TryOpen"/> は
/// プロセスで最初の 1 回だけ <b>0.23〜0.28 s</b> 掛かる（`pdh.dll` の perflib の初期化＝
/// 内訳は `PdhOpenQueryW` 1.7 ms ＋ `\GPU Process Memory(*)` の追加 214 ms）。
/// 1 標本は <b>0.03〜0.30 ms</b>。だから<b>開くのも見張りの糸で</b>行う
/// （<see cref="OsGpuMemorySampler"/> が最初の標本の回に開く）＝UI の糸を 0.23 s 止めない。
/// </para>
/// </summary>
public sealed class PdhGpuMemoryCounters : IGpuMemoryCounters
{
    /// <summary>プロセスごとの専用 GPU メモリ。</summary>
    public const string ProcessDedicatedPath = @"\GPU Process Memory(*)\Dedicated Usage";

    /// <summary>カードごとの専用 GPU メモリ（他のプログラム込み）。</summary>
    public const string AdapterDedicatedPath = @"\GPU Adapter Memory(*)\Dedicated Usage";

    /// <summary>カードごとのローカル メモリ（<c>_part_m</c> の区画つき＝控え）。</summary>
    public const string LocalAdapterPath = @"\GPU Local Adapter Memory(*)\Local Usage";

    private const uint PdhCstatusValidData = 0x00000000;
    private const uint PdhCstatusNewData = 0x00000001;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFmtLarge = 0x00000400;

    /// <summary>ひとつの instance も返ってこない標本を諦めるまで（安全弁）。</summary>
    private const uint MaxItems = 4096;

    /// <summary>
    /// 2 度呼びを繰り返す上限（是正・2026-09-08）。<b>instance の顔ぶれは標本の合間にも動く</b>＝
    /// 大きさを聞いてから受け取るまでの間に新しい pid が現れると 2 度目も
    /// <c>PDH_MORE_DATA</c> が返り、1 度で諦めると<b>その counter の instance が丸ごと消える</b>
    /// （帯の OS の組が 2 秒だけ消えて、また出る）。
    /// </summary>
    private const int ReadAttempts = 3;

    private readonly object _gate = new();

    private IntPtr _query;
    private CounterSlot _processCounter;
    private CounterSlot _adapterCounter;
    private CounterSlot _localCounter;
    private bool _disposed;

    private PdhGpuMemoryCounters(
        IntPtr query, IntPtr processCounter, IntPtr adapterCounter, IntPtr localCounter)
    {
        _query = query;
        _processCounter = new CounterSlot(processCounter);
        _adapterCounter = new CounterSlot(adapterCounter);
        _localCounter = new CounterSlot(localCounter);
    }

    /// <summary>
    /// query を 1 本開く。<b>例外を投げない</b>＝開けなければ null と理由 1 行
    /// （<c>pdh.dll</c> が無い・counter の組が無い機体はここで静かに落ちる）。
    /// </summary>
    /// <param name="failureReason">開けなかった理由（開けたときは null）。</param>
    public static PdhGpuMemoryCounters? TryOpen(out string? failureReason)
    {
        failureReason = null;
        var query = IntPtr.Zero;
        try
        {
            var status = PdhOpenQueryW(null, IntPtr.Zero, out query);
            if (status != 0)
            {
                failureReason = "PdhOpenQuery が失敗しました（0x"
                    + status.ToString("x8", CultureInfo.InvariantCulture) + "）。";
                return null;
            }

            // **プロセス側の 1 本だけが必須**＝アダプタ側の 2 本は片方あればよい
            // （この機体は両方ある。GPU Adapter Memory を持たない機体は Local を使う）。
            if (!TryAddCounter(query, ProcessDedicatedPath, out var processCounter, out var reason))
            {
                failureReason = reason;
                return null;
            }

            TryAddCounter(query, AdapterDedicatedPath, out var adapterCounter, out _);
            TryAddCounter(query, LocalAdapterPath, out var localCounter, out _);

            var opened = new PdhGpuMemoryCounters(query, processCounter, adapterCounter, localCounter);
            query = IntPtr.Zero;   // 以後の閉じ役は Dispose（下の finally では閉じない）
            return opened;
        }
        catch (DllNotFoundException ex)
        {
            failureReason = "pdh.dll が読めませんでした（" + ex.Message + "）。";
            return null;
        }
        catch (EntryPointNotFoundException ex)
        {
            failureReason = "pdh.dll に必要な口がありませんでした（" + ex.Message + "）。";
            return null;
        }
        catch (SystemException ex)
        {
            failureReason = "GPU の計数を開けませんでした（" + ex.Message + "）。";
            return null;
        }
        finally
        {
            // **閉じ役を 1 箇所に寄せる**（是正・2026-09-08）＝`PdhOpenQueryW` が通った後に
            // `PdhAddEnglishCounterW` が投げる筋（`EntryPointNotFoundException`＝この catch が
            // 在る理由そのもの）で、query の柄が閉じられずに残っていた。
            if (query != IntPtr.Zero)
            {
                try
                {
                    PdhCloseQuery(query);
                }
                catch (SystemException)
                {
                    // 開けなかった道の後始末なので、閉じ損ねても何も言わない
                }
            }
        }
    }

    /// <inheritdoc />
    public GpuCounterSample? Sample(out string? failureReason)
    {
        failureReason = null;
        lock (_gate)
        {
            if (_disposed || _query == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                // 1 回の collect で 3 本ぶんの値が揃う（同じ瞬間の標本になる）。
                var status = PdhCollectQueryData(_query);
                if (status != 0)
                {
                    // **理由を持ち帰る**（是正・2026-09-08）＝counter の組は在るのに collect が
                    // ずっと失敗する機体（`PDH_NO_DATA`＝perflib が壊れている・切ってある）で、
                    // 帯から組が消えたきり<b>ログに 1 行も出ない</b>のを直す。
                    failureReason = "GPU の計数を採れませんでした（PdhCollectQueryData 0x"
                        + status.ToString("x8", CultureInfo.InvariantCulture) + "）。";
                    return null;
                }

                return new GpuCounterSample(
                    Read(_processCounter),
                    Read(_adapterCounter),
                    Read(_localCounter));
            }
            catch (SystemException ex)
            {
                // 計数が読めなくなった＝画面からは行が消えるだけ（例外は外へ出さない）。
                failureReason = "GPU の計数を読めませんでした（" + ex.Message + "）。";
                return null;
            }
        }
    }

    /// <summary>query を閉じる（<b>2 度呼ばれても落ちない</b>）。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var query = _query;
            _query = IntPtr.Zero;
            _processCounter.Free();
            _adapterCounter.Free();
            _localCounter.Free();
            _processCounter = new CounterSlot(IntPtr.Zero);
            _adapterCounter = new CounterSlot(IntPtr.Zero);
            _localCounter = new CounterSlot(IntPtr.Zero);

            if (query == IntPtr.Zero)
            {
                return;
            }

            try
            {
                PdhCloseQuery(query);
            }
            catch (SystemException)
            {
                // 終わりに掛ける手なので、閉じ損ねても何も言わない
            }
        }
    }

    private static bool TryAddCounter(
        IntPtr query, string path, out IntPtr counter, out string? failureReason)
    {
        var status = PdhAddEnglishCounterW(query, path, IntPtr.Zero, out counter);
        if (status == 0)
        {
            failureReason = null;
            return true;
        }

        counter = IntPtr.Zero;
        failureReason = "GPU の計数「" + path + "」がこの機体にありませんでした（0x"
            + status.ToString("x8", CultureInfo.InvariantCulture) + "）。";
        return false;
    }

    /// <summary>
    /// counter 1 本の instance をすべて読む（<b>2 度呼びの型</b>＝1 度目は要る大きさを聞き、
    /// 2 度目で受け取る）。<c>CStatus</c> が有効でない item は捨てる。
    /// <para>
    /// <b>2 度目がまた <c>PDH_MORE_DATA</c> なら数え直す</b>（是正・2026-09-08＝
    /// <see cref="ReadAttempts"/> 回まで）。<b>受け皿は counter ごとに使い回す</b>＝
    /// 落ち着いた機体では 2 秒ごとの割り当てが 0 になる（要る大きさが増えた回だけ取り直す）。
    /// </para>
    /// </summary>
    private static IReadOnlyList<GpuCounterInstance> Read(CounterSlot slot)
    {
        if (slot.Counter == IntPtr.Zero)
        {
            return [];
        }

        for (var attempt = 0; attempt < ReadAttempts; attempt++)
        {
            uint size = 0;
            uint count = 0;
            var status = PdhGetFormattedCounterArrayW(
                slot.Counter, PdhFmtLarge, ref size, ref count, IntPtr.Zero);
            if (status != PdhMoreData || size == 0 || count == 0 || count > MaxItems)
            {
                return [];
            }

            slot.EnsureCapacity(size);
            var capacity = slot.Capacity;
            uint items = 0;
            status = PdhGetFormattedCounterArrayW(
                slot.Counter, PdhFmtLarge, ref capacity, ref items, slot.Buffer);
            if (status == 0)
            {
                return Materialise(slot.Buffer, items > MaxItems ? 0 : items);
            }

            if (status != PdhMoreData)
            {
                return [];
            }

            // 聞いてから受け取るまでの間に instance が増えた＝もう 1 度、大きさから聞き直す
        }

        return [];
    }

    /// <summary>受け皿の中身を <see cref="GpuCounterInstance"/> に起こす。</summary>
    private static IReadOnlyList<GpuCounterInstance> Materialise(IntPtr buffer, uint count)
    {
        if (buffer == IntPtr.Zero || count == 0)
        {
            return [];
        }

        var stride = Marshal.SizeOf<CounterValueItem>();
        var items = new List<GpuCounterInstance>((int)count);
        for (var i = 0; i < count; i++)
        {
            var item = Marshal.PtrToStructure<CounterValueItem>(buffer + (i * stride));
            if (item.Value.CStatus is not (PdhCstatusValidData or PdhCstatusNewData))
            {
                continue;
            }

            var name = item.Name == IntPtr.Zero ? null : Marshal.PtrToStringUni(item.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            items.Add(new GpuCounterInstance(name, item.Value.LargeValue));
        }

        return items;
    }

    /// <summary>
    /// counter 1 本と、その<b>使い回しの受け皿</b>（是正・2026-09-08＝
    /// 2 秒ごとに 3 本ぶんを取り直さない）。
    /// </summary>
    private sealed class CounterSlot(IntPtr counter)
    {
        /// <summary><c>PdhAddEnglishCounterW</c> が返した柄（<c>IntPtr.Zero</c>＝この機体に無い）。</summary>
        public IntPtr Counter { get; } = counter;

        /// <summary>受け皿（まだ取っていなければ <c>IntPtr.Zero</c>）。</summary>
        public IntPtr Buffer { get; private set; }

        /// <summary>いま持っている受け皿の大きさ（バイト）。</summary>
        public uint Capacity { get; private set; }

        /// <summary><paramref name="size"/> バイト入るようにする（足りているなら何もしない）。</summary>
        public void EnsureCapacity(uint size)
        {
            if (Buffer != IntPtr.Zero && Capacity >= size)
            {
                return;
            }

            Free();
            Buffer = Marshal.AllocHGlobal((int)size);
            Capacity = size;
        }

        /// <summary>受け皿を返す（<b>2 度呼ばれても落ちない</b>）。</summary>
        public void Free()
        {
            if (Buffer == IntPtr.Zero)
            {
                return;
            }

            var buffer = Buffer;
            Buffer = IntPtr.Zero;
            Capacity = 0;
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary><c>PDH_FMT_COUNTERVALUE</c>（x64 で 16 バイト＝値は 8 バイト目から）。</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct CounterValue
    {
        [FieldOffset(0)]
        public uint CStatus;

        [FieldOffset(8)]
        public long LargeValue;
    }

    /// <summary><c>PDH_FMT_COUNTERVALUE_ITEM_W</c>（x64 で 24 バイト）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValueItem
    {
        public IntPtr Name;
        public CounterValue Value;
    }

    // **必ず System32 から読む**（是正・2026-09-08）＝`pdh.dll` は KnownDLLs に無く、
    // 既定の探索順は**アプリの置き場が先**である。配布先の置き場（`%LOCALAPPDATA%\Programs\…`）は
    // 利用者が書ける＝exe の隣に偽の `pdh.dll` を置かれると、それが載ってしまう。
    // `DllImportSearchPath.System32` に限れば、その筋は閉じる（両方の DLL は必ず System32 に在る）。
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhAddEnglishCounterW(
        IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhGetFormattedCounterArrayW(
        IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhCloseQuery(IntPtr query);
}
