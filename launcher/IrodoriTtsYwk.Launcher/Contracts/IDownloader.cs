using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>取得の段（進捗表示の分類）。</summary>
public enum DownloadPhase
{
    /// <summary>まだ始めていない。</summary>
    Pending,

    /// <summary>接続中（Range の交渉を含む）。</summary>
    Connecting,

    /// <summary>本体を落としている。</summary>
    Downloading,

    /// <summary>sha256 を計算している。</summary>
    Verifying,

    /// <summary><c>.part</c> を本名へ差し替えている。</summary>
    Committing,

    /// <summary>既に検証済みの檔が在ったので落とさなかった。</summary>
    CacheHit,

    Done,

    Failed,
}

/// <summary>取得 1 件の注文。</summary>
/// <param name="Url">第 1 候補。</param>
/// <param name="FallbackUrl">同じ檔を出す別ホスト（無ければ null）。</param>
/// <param name="DestinationPath">着地先（<c>.part</c> はこの隣に作る）。</param>
/// <param name="Sha256">小文字 hex。null なら sha256 検証をしない（HF の非 LFS 檔）。</param>
/// <param name="ExpectedSize">台帳の <c>size</c>（進捗の分母・長さの検分）。</param>
/// <param name="DisplayName">UI に出す名（台帳の <c>name</c>）。</param>
/// <param name="MaxAttempts">不一致で破棄して取り直す上限（設計書 §6＝5 回）。</param>
public sealed record DownloadRequest(
    string Url,
    string? FallbackUrl,
    string DestinationPath,
    string? Sha256,
    long? ExpectedSize,
    string DisplayName,
    int MaxAttempts = 5)
{
    /// <summary>台帳の 1 件から注文を作る（url→fallback の順は <see cref="LedgerItem.Urls"/> と同じ）。</summary>
    public static DownloadRequest FromLedgerItem(LedgerItem item, string cacheDir)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDir);
        return new DownloadRequest(
            item.Url,
            item.FallbackUrl,
            System.IO.Path.Combine(cacheDir, item.EffectiveFileName),
            item.Sha256,
            item.Size,
            item.Name);
    }
}

/// <summary>取得 1 件の進み具合（UI は bytes と ETA を出す＝受け入れ条件 D-5）。</summary>
/// <param name="DisplayName">いま落としている物。</param>
/// <param name="Phase">段。</param>
/// <param name="BytesReceived">着地済みバイト（再開分を含む）。</param>
/// <param name="TotalBytes">分母（分からなければ null）。</param>
/// <param name="BytesPerSecond">直近の速さ。</param>
/// <param name="Eta">残り時間の見積り（分母が無ければ null）。</param>
/// <param name="Attempt">1 起点。再取得のたびに増える。</param>
/// <param name="Url">実際に叩いている URL（fallback に落ちたことが見える）。</param>
public sealed record DownloadProgress(
    string DisplayName,
    DownloadPhase Phase,
    long BytesReceived,
    long? TotalBytes,
    double BytesPerSecond,
    TimeSpan? Eta,
    int Attempt,
    string? Url)
{
    /// <summary>0〜1。分母が無ければ null。</summary>
    public double? Fraction => TotalBytes is > 0
        ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1)
        : null;

    /// <summary>速さと残りから ETA を出す（**純関数**＝表示の見た目を揃える 1 箇所）。</summary>
    public static TimeSpan? EstimateEta(long received, long? total, double bytesPerSecond)
    {
        if (total is not > 0 || bytesPerSecond <= 0)
        {
            return null;
        }

        var remaining = total.Value - received;
        if (remaining <= 0)
        {
            return TimeSpan.Zero;
        }

        var seconds = remaining / bytesPerSecond;
        return seconds > TimeSpan.MaxValue.TotalSeconds ? null : TimeSpan.FromSeconds(seconds);
    }
}

/// <summary>取得 1 件の結末。</summary>
/// <param name="Ok">着地して検証も通ったか。</param>
/// <param name="Path">着地先（失敗時も、途中檔の在り処として意味がある）。</param>
/// <param name="Bytes">着地した長さ。</param>
/// <param name="ActualSha256">計算した sha256（検証をしなかったら null）。</param>
/// <param name="UsedUrl">実際に取れた URL。</param>
/// <param name="Resumed">Range で途中から続けたか。</param>
/// <param name="FromCache">既に検証済みの檔が在って落とさなかったか。</param>
/// <param name="Attempts">試した回数。</param>
/// <param name="FailureReason">理由 1 行（絶対パスを出さない＝ログ規律）。</param>
public sealed record DownloadResult(
    bool Ok,
    string Path,
    long Bytes,
    string? ActualSha256,
    string? UsedUrl,
    bool Resumed,
    bool FromCache,
    int Attempts,
    string? FailureReason);

/// <summary>
/// 契約 ⑵＝HTTP 取得（Range 再開・sha256・<c>.part</c>→Rename・進捗）。
/// <para>
/// <b>着地は原子的</b>＝<c>&lt;檔&gt;.part</c> に書き、sha256 が合ってから 1 手で本名にする。
/// 途中で落ちた <c>.part</c> は次回 Range で続きから取る（受け入れ条件 D-5）。
/// </para>
/// <para>
/// <b>url で失敗したら fallback_url を試す</b>（<c>ledger/README.md</c> §3＝CDN の別名が
/// 畳まれた日に全利用者の初回取得が 404 で死ぬのを防ぐ 1 行）。
/// </para>
/// <para>
/// <b>sha256 が合わなければ破棄して取り直す</b>（最大 <see cref="DownloadRequest.MaxAttempts"/> 回）。
/// 半端な檔を site-packages に展開させない。
/// </para>
/// </summary>
public interface IDownloader
{
    /// <summary>1 件取る。例外は投げず <see cref="DownloadResult.Ok"/> で返す（取消は除く）。</summary>
    Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// 台帳の並び順に取る（<b>並列 2 本まで</b>＝設計書 §6）。
    /// 1 件でも落ちたらそこで止め、落ちた件を <see cref="DownloadResult.FailureReason"/> に載せて返す。
    /// </summary>
    Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(
        IReadOnlyList<DownloadRequest> requests,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken);
}
