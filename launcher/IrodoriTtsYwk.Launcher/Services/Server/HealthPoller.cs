using System;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Http;

namespace IrodoriTtsYwk.Launcher.Services.Server;

/// <summary>
/// ready 判定の 1 標本（契約 ⑵＝<b>ready は <c>runtime.loaded</c> で判る</b>）。
/// </summary>
/// <param name="Reachable"><c>/health</c> か <c>/ywk/status</c> が返った（listen している）。</param>
/// <param name="Loaded"><c>runtime.loaded=true</c>（合成できる）。</param>
/// <param name="WarmupRunning">暖機か事前計算が走っている（<c>state=running</c>）。</param>
/// <param name="Status">読めた <c>/ywk/status</c>（無ければ null＝上流の素の Server か未実装）。</param>
/// <param name="FailureReason">読めなかった理由 1 行。</param>
public sealed record ReadinessSample(
    bool Reachable,
    bool Loaded,
    bool WarmupRunning,
    StatusResponse? Status,
    string? FailureReason);

/// <summary>ready 待ちの間、繰り返し叩かれる口（テストは偽物を差す）。</summary>
public interface IReadinessProbe
{
    Task<ReadinessSample> ProbeAsync(Uri baseAddress, CancellationToken cancellationToken);
}

/// <summary>
/// <c>/ywk/status</c>（→ 読めなければ <c>/health</c>）で 1 標本を採る。
/// <para>
/// <b>2 段にしてある</b>理由＝<c>/ywk/status</c> は配布版だけの口で、上流の素の Server では 404 になる
/// （契約 ⑵ の 2 段目）。404 でも listen はしているので「到達はした」を落とさない。
/// <c>runtime.loaded</c> が読めない個体は <c>/health</c> の 200 だけで ready と見なす
/// （上流の素の Server を相手にしたときの保険で、配布版では通らない路）。
/// </para>
/// <para>
/// <b>標本 1 回の期限は 5 秒</b>（契約 ⑵）。ready 待ちそのものの期限は呼ぶ側（120 s・CPU は 300 s）。
/// </para>
/// </summary>
public sealed class HealthPoller : IReadinessProbe, IDisposable
{
    private readonly Func<Uri, IWrapperClient> _factory;
    private readonly object _gate = new();
    private IWrapperClient? _client;
    private Uri? _clientAddress;
    private bool _disposed;

    /// <summary>実機用。</summary>
    public HealthPoller()
        : this(null)
    {
    }

    /// <summary>テスト用＝口の作り方を差す（<b>public コンストラクタが継ぎ目</b>）。</summary>
    public HealthPoller(Func<Uri, IWrapperClient>? factory) =>
        _factory = factory ?? (address => new WrapperClient(address));

    public async Task<ReadinessSample> ProbeAsync(Uri baseAddress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        var client = GetClient(baseAddress);

        var status = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.Ok && status.Value is StatusResponse value)
        {
            var warming = value.Warmup?.IsRunning == true || value.Precompute?.IsRunning == true;
            return new ReadinessSample(true, value.IsReady, warming, value, null);
        }

        // 404／405＝上流の素の Server（配布版ではない）。到達はしている。
        if (!status.Available)
        {
            return new ReadinessSample(true, false, false, null, "この個体は配布版の wrapper ではありません。");
        }

        var health = await client.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        if (health.Ok)
        {
            return new ReadinessSample(true, false, false, null, null);
        }

        return new ReadinessSample(false, false, false, null, status.FailureReason ?? "サーバに繋がりません。");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _client?.Dispose();
            _client = null;
        }
    }

    private IWrapperClient GetClient(Uri baseAddress)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_client is not null && _clientAddress == baseAddress)
            {
                return _client;
            }

            _client?.Dispose();
            _client = _factory(baseAddress);
            _clientAddress = baseAddress;
            return _client;
        }
    }
}
