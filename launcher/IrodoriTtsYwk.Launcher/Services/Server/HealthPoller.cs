using System;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Http;

namespace IrodoriTtsYwk.Launcher.Services.Server;

/// <summary>
/// ready 判定の 1 標本（契約 ⑵＝<b>ready は <c>runtime.loaded</c> で判る</b>）。
/// <para>
/// <b>裁定 105（ポート先行）で「届いた」と「載った」は完全に別の事実になった</b>＝
/// wrapper は bind してから裏でモデルを載せるので、<c>/health</c> 200 は起動の 1 秒後から
/// 返り、<c>runtime.loaded=true</c> はその 20〜70 秒後に来る。<see cref="Reachable"/> は
/// 「読込中」を、<see cref="Loaded"/> は「待機」を立てる。
/// </para>
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
    string? FailureReason)
{
    /// <summary>
    /// <c>/ywk/status.runtime.error</c>＝<b>モデルの読込が失敗した理由 1 行</b>（裁定 105 ⑴）。
    /// <para>
    /// <b>ポート先行</b>になったので、読込の失敗はもう「子が落ちる」ことで伝わらない＝
    /// wrapper は bind してから裏でモデルを載せ、失敗しても<b>プロセスを生かしたまま</b>
    /// この 1 欄に理由を載せる（本体とランチャが読めるように）。終了コードの路
    /// （<see cref="Contracts.ServerExitCodes"/>）はもう通らないので、
    /// <see cref="ServerStateMachine.ApplyReadiness"/> がこの欄で Failed に落とす。
    /// </para>
    /// <para>
    /// <b>後付けの欄にしてある</b>のは、位置引数に足すと既存の呼び手（<c>new ReadinessSample(
    /// false, false, false, null, …)</c>）が全部黙って意味を変えるからである。
    /// </para>
    /// </summary>
    public string? RuntimeError { get; init; }
}

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
/// <para>
/// <b>誰も居ない相手には 2 本目を撃たない</b>（是正・便 D（2））＝<c>StatusCode == 0</c> は
/// 「HTTP の応答が 1 つも返っていない」（接続できない・期限切れ）の印で、その相手に
/// <c>/health</c> をもう 1 本撃っても同じ代金を 2 度払うだけである。実測＝
/// 誰も listen していない <c>127.0.0.1</c> への 1 本が <b>2.0 秒</b>（接続が拒まれるまでの待ち）で、
/// 是正前の 1 標本は <b>4.0 秒</b>かかっていた。これが
/// <see cref="ServerStateMachine.UnreachableSamplesToDowngrade"/> の 3 標本に掛かり、
/// 「応答が消えた個体を降ろす」までの実測が 18.0 秒になっていた。
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
            return new ReadinessSample(true, value.IsReady, warming, value, null)
            {
                // 裁定 105 ⑴＝読込に失敗した個体は生きたまま理由を答える。
                RuntimeError = value.Runtime?.Error,
            };
        }

        // 404／405＝上流の素の Server（配布版ではない）。到達はしている。
        if (!status.Available)
        {
            return new ReadinessSample(true, false, false, null, "この個体は配布版の wrapper ではありません。");
        }

        // **応答が 1 つも返っていない相手には /health を撃たない**（是正・便 D（2））。
        // ここで 2 本目を撃つと 1 標本の代金が倍になり、Ready から降ろすまでの実測が
        // 3 標本 × 4.0 s ＋ 見張りの 2 s × 3 ＝ 18.0 s になっていた。
        if (status.StatusCode == 0)
        {
            return new ReadinessSample(
                false, false, false, null,
                status.FailureReason ?? "サーバに繋がりません。");
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
