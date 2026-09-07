using System;
using System.Collections.Generic;
using System.Globalization;

namespace IrodoriTtsYwk.Launcher.Services.Gpu;

/// <summary>
/// Windows の GPU 計数（PDH）の instance 1 件＝<c>(instance 名, バイト)</c>。
/// <para>
/// 名前の形は 2 つ（実測・この機体・2026-09-08）＝
/// プロセス側 <c>pid_31556_luid_0x00000000_0x000137d0_phys_0</c>・
/// アダプタ側 <c>luid_0x00000000_0x000137d0_phys_0</c>（<c>GPU Adapter Memory</c>）と
/// <c>luid_0x00000000_0x000137d0_phys_0_part_0</c>（<c>GPU Local Adapter Memory</c>）。
/// </para>
/// </summary>
/// <param name="InstanceName">PDH が名乗った instance 名（そのまま）。</param>
/// <param name="Bytes">その counter の値（<c>PDH_FMT_LARGE</c>）。</param>
public sealed record GpuCounterInstance(string InstanceName, long Bytes);

/// <summary>
/// instance 名を解いた結果（<b>純関数</b> <see cref="OsGpuMemory.ParseInstance"/> の返り）。
/// </summary>
/// <param name="Pid">プロセス側なら pid（アダプタ側は null）。</param>
/// <param name="Luid">
/// <c>luid_0x&lt;HighPart:X8&gt;_0x&lt;LowPart:X8&gt;</c>（<b>綴りはそのまま</b>＝突合は大小無視）。
/// </param>
/// <param name="Phys">
/// <c>_phys_&lt;k&gt;</c>（同じ LUID の中の物理面。読めなければ null）。
/// </param>
/// <param name="Part">
/// <c>_part_&lt;m&gt;</c>（<c>GPU Local Adapter Memory</c> だけが持つ区画。無ければ null）。
/// </param>
public sealed record GpuInstanceName(int? Pid, string Luid, int? Phys, int? Part);

/// <summary>
/// DXGI が名乗ったアダプタ 1 枚（<see cref="IGpuAdapterInfoSource"/> の返り）。
/// </summary>
/// <param name="Luid">
/// <c>DXGI_ADAPTER_DESC1.AdapterLuid</c> を PDH の綴りに直した物
/// （<see cref="OsGpuMemory.LuidToken"/>）。
/// </param>
/// <param name="Name"><c>DXGI_ADAPTER_DESC1.Description</c>。</param>
/// <param name="DedicatedVideoMemoryBytes">
/// <c>DedicatedVideoMemory</c>＝<b>専用 GPU メモリだけ</b>（司令官の指示 2＝共有は除外＝
/// <c>SharedSystemMemory</c> は読まない・出さない）。
/// </param>
public sealed record GpuAdapterInfo(string Luid, string Name, long DedicatedVideoMemoryBytes);

/// <summary>
/// 状態帯に出す OS 側の 1 行（GPU 1 枚ぶん・裁定 110）。
/// </summary>
/// <param name="Luid">その GPU の LUID の綴り。</param>
/// <param name="Name">DXGI の名前（読めなければ <paramref name="Luid"/> そのもの）。</param>
/// <param name="ProcessBytes">TTS のプロセスがその GPU で使っている専用メモリ。</param>
/// <param name="AdapterBytes">その GPU の専用メモリの占有（<b>他のプログラム込み</b>・読めなければ null）。</param>
/// <param name="TotalBytes">その GPU の専用メモリの総量（DXGI が読めなければ null）。</param>
public sealed record OsGpuMemoryRow(
    string Luid, string Name, long ProcessBytes, long? AdapterBytes, long? TotalBytes);

/// <summary>PDH の 1 標本（3 つの counter を同じ <c>PdhCollectQueryData</c> で採る）。</summary>
/// <param name="Processes"><c>\GPU Process Memory(*)\Dedicated Usage</c>。</param>
/// <param name="Adapters"><c>\GPU Adapter Memory(*)\Dedicated Usage</c>。</param>
/// <param name="LocalAdapters">
/// <c>\GPU Local Adapter Memory(*)\Local Usage</c>＝<b>控え</b>（アダプタ側に instance が
/// 無い LUID にだけ使う・区画は LUID ごとに足す）。
/// </param>
public sealed record GpuCounterSample(
    IReadOnlyList<GpuCounterInstance> Processes,
    IReadOnlyList<GpuCounterInstance> Adapters,
    IReadOnlyList<GpuCounterInstance> LocalAdapters);

/// <summary>
/// Windows の GPU 計数を 1 標本読む口（裁定 110 D1）。実機は <c>pdh.dll</c>、テストは偽物。
/// </summary>
public interface IGpuMemoryCounters : IDisposable
{
    /// <summary>
    /// 1 標本。<b>例外を投げない</b>＝読めなければ null。
    /// <para>
    /// <b>読めなかった理由は返り値ではなく <paramref name="failureReason"/> に載せる</b>
    /// （是正・2026-09-08）＝collect が status だけで失敗する機体（perflib が壊れている・
    /// counter を切ってある＝<c>PDH_NO_DATA</c>）でも、畳み方の決め（D5＝理由 1 行）が効くようにする。
    /// 読めたときは null。
    /// </para>
    /// </summary>
    GpuCounterSample? Sample(out string? failureReason);
}

/// <summary>
/// アダプタの名前と専用メモリの総量を数える口（裁定 110 D2）。実機は <c>dxgi.dll</c>。
/// </summary>
public interface IGpuAdapterInfoSource
{
    /// <summary>いま居るアダプタ（<b>例外を投げない</b>＝読めなければ空）。</summary>
    IReadOnlyList<GpuAdapterInfo> Adapters();
}

/// <summary>
/// Windows の GPU 計数から状態帯の行を組む<b>純関数</b>だけの檔（裁定 110・2026-09-08）。
/// <para>
/// <b>なぜ要るか</b>＝wrapper が出す <c>/ywk/status.memory.gpu_used</c>（<c>torch.cuda.mem_get_info</c>）は
/// Windows の ROCm では<b>カード全体でも自プロセスの OS 上の占有でもない</b>。実測（この機体・
/// 2026-09-08 01:3x・Radeon 8060S gfx1151・wrapper pid 31556）＝<c>gpu_used</c> 3.66 GiB に対し、
/// OS の計数は同じ瞬間に <c>GPU Process Memory\Dedicated Usage</c> 10.84 GiB（同じ pid）・
/// <c>GPU Adapter Memory\Dedicated Usage</c> 29.79 GiB（タスク マネージャーの「専用」29.8 GB と一致）。
/// ランチャは OS の計数を別に読んで出す。
/// </para>
/// <para>
/// <b>集計の相手は TTS エンジンが使っている GPU だけ</b>（司令官の指示 2）＝設定の名前や UUID とは
/// 突き合わせない。<b>その pid に instance がある LUID</b> がそのまま相手である
/// （模型と codec を別の GPU に載せれば 2 行出る・python が触っていない iGPU は出ない）。
/// </para>
/// </summary>
public static class OsGpuMemory
{
    private const string LuidPrefix = "luid_";
    private const string PidPrefix = "pid_";
    private const string PhysMarker = "_phys_";
    private const string PartMarker = "_part_";

    /// <summary>
    /// PDH の instance 名を解く（<b>純関数</b>）。読めない綴りは null（例外は投げない）。
    /// <para>
    /// 受ける形は 2 つ＝<c>pid_&lt;n&gt;_luid_0x…_0x…_phys_&lt;k&gt;</c> と
    /// <c>luid_0x…_0x…_phys_&lt;k&gt;[_part_&lt;m&gt;]</c>。16 進の大小は問わない
    /// （<see cref="GpuInstanceName.Luid"/> には<b>来た綴りのまま</b>入れ、突合で大小を無視する）。
    /// </para>
    /// </summary>
    public static GpuInstanceName? ParseInstance(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var text = name.Trim();
        int? pid = null;

        if (text.StartsWith(PidPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var luidAt = text.IndexOf('_' + LuidPrefix, StringComparison.OrdinalIgnoreCase);
            if (luidAt <= PidPrefix.Length - 1)
            {
                return null;
            }

            var digits = text[PidPrefix.Length..luidAt];
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                return null;
            }

            pid = parsed;
            text = text[(luidAt + 1)..];
        }

        if (!text.StartsWith(LuidPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int? part = null;
        var partAt = text.IndexOf(PartMarker, StringComparison.OrdinalIgnoreCase);
        if (partAt >= 0)
        {
            if (!int.TryParse(text[(partAt + PartMarker.Length)..], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var parsedPart))
            {
                return null;
            }

            part = parsedPart;
            text = text[..partAt];
        }

        int? phys = null;
        var physAt = text.IndexOf(PhysMarker, StringComparison.OrdinalIgnoreCase);
        if (physAt >= 0)
        {
            if (!int.TryParse(text[(physAt + PhysMarker.Length)..], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var parsedPhys))
            {
                return null;
            }

            phys = parsedPhys;
            text = text[..physAt];
        }

        // 残りが LUID の綴り 1 つであることを確かめる（luid_0x<8 桁>_0x<8 桁>）。
        var body = text[LuidPrefix.Length..];
        var separator = body.IndexOf('_', StringComparison.Ordinal);
        if (separator <= 0 || separator == body.Length - 1)
        {
            return null;
        }

        if (!IsHexToken(body[..separator]) || !IsHexToken(body[(separator + 1)..]))
        {
            return null;
        }

        return new GpuInstanceName(pid, text, phys, part);
    }

    /// <summary>
    /// DXGI の <c>LUID</c> を PDH の綴りにする（<b>純関数</b>）＝
    /// <c>luid_0x&lt;HighPart:X8&gt;_0x&lt;LowPart:X8&gt;</c>。
    /// <para>
    /// <c>HighPart</c> は符号つき（<c>LONG</c>）なので、負でも 32 bit の 16 進 8 桁で綴る
    /// （PDH は 2 の補数の綴りを出す）。
    /// </para>
    /// </summary>
    public static string LuidToken(int highPart, uint lowPart) =>
        LuidPrefix + "0x" + ((uint)highPart).ToString("x8", CultureInfo.InvariantCulture)
        + "_0x" + lowPart.ToString("x8", CultureInfo.InvariantCulture);

    /// <summary>
    /// 1 標本から状態帯の行を組む（<b>純関数</b>・裁定 110 D3）。
    /// <para>
    /// ⑴ <paramref name="pid"/> の instance で<b>値が 0 より大きい</b> LUID だけを相手にする
    /// （pid が null＝止まっている・計数が読めない＝<b>空</b>）
    /// ⑵ 同じ LUID に複数の instance（<c>_phys_k</c> 違い）があれば足す
    /// ⑶ 「GPU 全体」は <paramref name="adapterInstances"/>＝<c>GPU Adapter Memory</c> が正で、
    /// その LUID の instance が<b>無いか 0 のとき</b>は <paramref name="localAdapterInstances"/>＝
    /// <c>GPU Local Adapter Memory</c> の区画（<c>_part_m</c>）を足した値を使う
    /// （是正・2026-09-08＝この機体の <c>GPU Adapter Memory</c> は<b>値が 0 の instance</b>を
    /// 名乗る LUID があり、instance の有無だけで控えに落ちると「GPU 全体 0 B」と綴ってしまう。
    /// <b>自分より小さい「全体」も出さない</b>＝読めなかった物として <c>—</c> にする）
    /// ⑷ 名前と総量は DXGI（<paramref name="adapters"/>）から引き、読めなければ名前は LUID の綴り・
    /// 総量は null（画面は「—」）。
    /// </para>
    /// <para>
    /// 並びは<b>このプロセスの使用量の多い順</b>（同点は LUID の綴り順）＝標本ごとに行が入れ替わらない。
    /// </para>
    /// </summary>
    public static IReadOnlyList<OsGpuMemoryRow> Aggregate(
        int? pid,
        IReadOnlyList<GpuCounterInstance>? processInstances,
        IReadOnlyList<GpuCounterInstance>? adapterInstances,
        IReadOnlyList<GpuCounterInstance>? localAdapterInstances,
        IReadOnlyList<GpuAdapterInfo>? adapters)
    {
        if (pid is not int wanted || processInstances is null || processInstances.Count == 0)
        {
            return [];
        }

        var mine = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var instance in processInstances)
        {
            if (instance is null || instance.Bytes <= 0)
            {
                continue;
            }

            if (ParseInstance(instance.InstanceName) is not { Pid: int owner } parsed || owner != wanted)
            {
                continue;
            }

            mine.TryGetValue(parsed.Luid, out var sum);
            mine[parsed.Luid] = sum + instance.Bytes;
        }

        if (mine.Count == 0)
        {
            return [];
        }

        var adapterTotals = SumByLuid(adapterInstances);
        var localTotals = SumByLuid(localAdapterInstances);

        var rows = new List<OsGpuMemoryRow>(mine.Count);
        foreach (var pair in mine)
        {
            // 「GPU 全体」は**使える数が出たときだけ**名乗る（是正・2026-09-08）。
            // ⑴ アダプタ側が 0（instance はあるが数えていない）なら控えへ落ちる
            // ⑵ 控えも 0 なら「読めなかった」＝null（画面は「—」）
            // ⑶ **自分のぶんより小さい「全体」は嘘**なので出さない（帯が自分自身と矛盾しない）
            long? whole = adapterTotals.TryGetValue(pair.Key, out var dedicated) && dedicated > 0
                ? dedicated
                : (localTotals.TryGetValue(pair.Key, out var local) && local > 0 ? local : null);
            if (whole is long sum && sum < pair.Value)
            {
                whole = null;
            }

            var info = Find(adapters, pair.Key);
            rows.Add(new OsGpuMemoryRow(
                pair.Key,
                string.IsNullOrWhiteSpace(info?.Name) ? pair.Key : info!.Name.Trim(),
                pair.Value,
                whole,
                info is null || info.DedicatedVideoMemoryBytes <= 0
                    ? null
                    : info.DedicatedVideoMemoryBytes));
        }

        rows.Sort(static (left, right) =>
        {
            var byBytes = right.ProcessBytes.CompareTo(left.ProcessBytes);
            return byBytes != 0
                ? byBytes
                : string.Compare(left.Luid, right.Luid, StringComparison.OrdinalIgnoreCase);
        });

        return rows;
    }

    /// <summary>その LUID のアダプタ（<b>大小を無視して</b>突き合わせる）。</summary>
    private static GpuAdapterInfo? Find(IReadOnlyList<GpuAdapterInfo>? adapters, string luid)
    {
        if (adapters is null)
        {
            return null;
        }

        foreach (var adapter in adapters)
        {
            if (adapter is not null
                && string.Equals(adapter.Luid, luid, StringComparison.OrdinalIgnoreCase))
            {
                return adapter;
            }
        }

        return null;
    }

    /// <summary>LUID ごとに足す（<c>_phys_k</c>・<c>_part_m</c> は同じ 1 枚の内訳）。</summary>
    private static Dictionary<string, long> SumByLuid(IReadOnlyList<GpuCounterInstance>? instances)
    {
        var sums = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (instances is null)
        {
            return sums;
        }

        foreach (var instance in instances)
        {
            if (instance is null || instance.Bytes < 0)
            {
                continue;
            }

            if (ParseInstance(instance.InstanceName) is not { Pid: null } parsed)
            {
                continue;
            }

            sums.TryGetValue(parsed.Luid, out var sum);
            sums[parsed.Luid] = sum + instance.Bytes;
        }

        return sums;
    }

    private static bool IsHexToken(string token)
    {
        if (!token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || token.Length <= 2)
        {
            return false;
        }

        for (var i = 2; i < token.Length; i++)
        {
            if (!Uri.IsHexDigit(token[i]))
            {
                return false;
            }
        }

        return true;
    }
}
