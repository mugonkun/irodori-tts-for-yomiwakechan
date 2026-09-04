using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Voices;

/// <summary>
/// 暖機・事前計算の走行 1 回の結末（UI の状態欄に出す形）。
/// </summary>
/// <param name="Started">受理された（202）。</param>
/// <param name="Supported">その口が在る（<b>偽＝未対応</b>＝<c>/ywk/voices/precompute</c> がまだ無い個体）。</param>
/// <param name="Id">走行の id（取消に渡す）。</param>
/// <param name="Total">射／件の総数。</param>
/// <param name="Message">UI に出す 1 行。</param>
public sealed record RunStart(bool Started, bool Supported, string? Id, int? Total, string Message);

/// <summary>
/// 契約 ⑺ 7-2・7-3 のクライアント（<b>ランチャの口。本体は叩かない</b>）。
/// <para>
/// <b>暖機</b>＝ready 直後に <c>POST /ywk/warmup</c>。段（秒）→ 話者の順に撃つ。
/// 便 C の実測＝暖機 12.6〜51.8 s（機体初回 80.8 s）・走行中の本物は最大 1 射分待つだけ。
/// <b>事前計算</b>＝<c>POST /ywk/voices/precompute</c>（<b>口が無ければ「未対応」と名乗って何もしない</b>＝
/// 便 C（2）が足している最中）。
/// </para>
/// <para>
/// 走行の見張りは <c>/ywk/status</c> の <c>warmup</c>／<c>precompute</c> を読むだけ
/// （<b>欄が無くても落ちない</b>）。
/// </para>
/// </summary>
public sealed class WarmupCoordinator
{
    private readonly IWrapperClient _client;

    public WarmupCoordinator(IWrapperClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>
    /// 設定から暖機の要求を組む（<b>純関数</b>）。
    /// <para>
    /// 段は <see cref="LauncherSettings.WarmupStages"/>（既定 <c>[4,8,12]</c>）。話者は
    /// 設定の <see cref="LauncherSettings.WarmupVoices"/>＝空なら<b>最近使った話者</b>を
    /// 1〜3 名だけ載せる（設計書 §5）。<b>「デフォルト」は段の射が既に参照なしなので載せない</b>。
    /// </para>
    /// </summary>
    public static WarmupRequest BuildRequest(
        LauncherSettings settings,
        IReadOnlyList<string>? recentVoices = null,
        int maxVoices = 3)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var stages = new List<double>();
        foreach (var stage in settings.WarmupStages)
        {
            if (stage > 0 && stage <= 60 && stages.Count < 12)
            {
                stages.Add(stage);
            }
        }

        var voices = new List<string>();
        void AddVoice(string? id)
        {
            if (string.IsNullOrWhiteSpace(id) || voices.Count >= maxVoices)
            {
                return;
            }

            var name = id.Trim();
            if (VoiceIds.IsNoRef(name) || voices.Contains(name))
            {
                return;
            }

            voices.Add(name);
        }

        foreach (var id in settings.WarmupVoices)
        {
            AddVoice(id);
        }

        if (voices.Count == 0 && recentVoices is not null)
        {
            foreach (var id in recentVoices)
            {
                AddVoice(id);
            }
        }

        return new WarmupRequest(
            stages.Count > 0 ? stages : null,
            voices.Count > 0 ? voices : null,
            string.IsNullOrWhiteSpace(settings.WarmupText) ? null : settings.WarmupText.Trim());
    }

    /// <summary>暖機を起こす。走行中に重ねると 409（<c>ywk_warmup_running</c>）＝そのまま名乗る。</summary>
    public async Task<RunStart> StartWarmupAsync(WarmupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await _client.StartWarmupAsync(request, cancellationToken).ConfigureAwait(false);

        if (!result.Available)
        {
            return new RunStart(false, false, null, null, "この個体は暖機の口を持っていません（未対応）。");
        }

        if (result.Ok && result.Value is WarmupStartResult value)
        {
            return new RunStart(
                true, true, value.Id, value.ShotsTotal,
                "暖機を始めました（" + Count(value.ShotsTotal) + " 射）。");
        }

        if (string.Equals(result.Code, WrapperErrorCodes.WarmupRunning, StringComparison.Ordinal))
        {
            return new RunStart(false, true, null, null, "暖機は既に走っています。");
        }

        return new RunStart(false, true, null, null, "暖機を始められませんでした：" + Reason(result));
    }

    /// <summary>暖機を止める（<b>次の射の前で止まる</b>＝走っている射は最後まで走る）。</summary>
    public async Task<string> CancelWarmupAsync(string id, CancellationToken cancellationToken)
    {
        var result = await _client.CancelWarmupAsync(id, cancellationToken).ConfigureAwait(false);
        if (result.Ok)
        {
            return "暖機を止めます（走っている射は最後まで走ります）。";
        }

        return string.Equals(result.Code, WrapperErrorCodes.WarmupUnknownId, StringComparison.Ordinal)
            ? "その暖機はもう走っていません。"
            : "暖機を止められませんでした：" + Reason(result);
    }

    /// <summary>
    /// 参照潜在の事前計算を起こす（裁定 65）。<b>口が無ければ「未対応」</b>＝
    /// <see cref="RunStart.Supported"/> が偽で返る（UI は欄を伏せる）。
    /// </summary>
    public async Task<RunStart> StartPrecomputeAsync(
        PrecomputeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await _client.StartPrecomputeAsync(request, cancellationToken).ConfigureAwait(false);

        if (!result.Available)
        {
            return new RunStart(false, false, null, null, "この個体は参照潜在の事前計算に未対応です。");
        }

        if (result.Ok && result.Value is PrecomputeStartResult value)
        {
            return new RunStart(
                true, true, value.Id, value.Total,
                "参照潜在を焼き始めました（" + Count(value.Total) + " 件）。");
        }

        if (string.Equals(result.Code, WrapperErrorCodes.PrecomputeRunning, StringComparison.Ordinal))
        {
            return new RunStart(false, true, null, null, "参照潜在の事前計算は既に走っています。");
        }

        if (string.Equals(result.Code, WrapperErrorCodes.UnknownVoice, StringComparison.Ordinal))
        {
            return new RunStart(false, true, null, null, "知らない話者名が混ざっています（1 件も焼いていません）。");
        }

        return new RunStart(false, true, null, null, "参照潜在を焼き始められませんでした：" + Reason(result));
    }

    /// <summary>事前計算を止める（<b>次の 1 件の前で止まる</b>）。</summary>
    public async Task<string> CancelPrecomputeAsync(string id, CancellationToken cancellationToken)
    {
        var result = await _client.CancelPrecomputeAsync(id, cancellationToken).ConfigureAwait(false);
        if (!result.Available)
        {
            return "この個体は参照潜在の事前計算に未対応です。";
        }

        if (result.Ok)
        {
            return "事前計算を止めます（焼いている 1 件は最後まで焼きます）。";
        }

        return string.Equals(result.Code, WrapperErrorCodes.PrecomputeUnknownId, StringComparison.Ordinal)
            ? "その事前計算はもう走っていません。"
            : "事前計算を止められませんでした：" + Reason(result);
    }

    /// <summary>いまの走行（<c>/ywk/status</c> を 1 回読む）。読めなければ null。</summary>
    public async Task<StatusResponse?> PollAsync(CancellationToken cancellationToken)
    {
        var status = await _client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.Ok ? status.Value : null;
    }

    /// <summary>暖機の進みを 1 行にする（<b>純関数</b>＝UI の状態欄）。</summary>
    public static string Describe(WarmupStatus? warmup)
    {
        if (warmup is null || warmup.State is null or "idle")
        {
            return "暖機：していません。";
        }

        var done = Count(warmup.ShotsDone ?? warmup.Shots);
        var total = Count(warmup.ShotsTotal);
        var elapsed = warmup.ElapsedSeconds is double s
            ? "・" + s.ToString("0.0", CultureInfo.InvariantCulture) + " 秒"
            : string.Empty;

        return warmup.State switch
        {
            "running" => "暖機中：" + done + "／" + total + elapsed,
            "done" => "暖機：終わりました（" + total + " 射" + elapsed + "）。",
            "cancelled" => "暖機：止めました（" + done + "／" + total + "）。",
            "failed" => "暖機：失敗しました" + (warmup.Error is null ? "。" : "：" + warmup.Error),
            _ => "暖機：" + warmup.State,
        };
    }

    /// <summary>事前計算の進みを 1 行にする（<b>純関数</b>）。</summary>
    public static string Describe(PrecomputeStatus? precompute)
    {
        if (precompute is null || precompute.State is null or "idle")
        {
            return "参照潜在：焼いていません。";
        }

        var done = Count(precompute.Done);
        var total = Count(precompute.Total);

        return precompute.State switch
        {
            "running" => "参照潜在を焼いています：" + done + "／" + total,
            "done" => "参照潜在：" + Count(precompute.Built) + " 件を焼き、"
                      + Count(precompute.Reused) + " 件は据え置きました。",
            "cancelled" => "参照潜在：止めました（" + done + "／" + total + "）。",
            "failed" => "参照潜在：" + Count(precompute.Failed) + " 件が失敗しました"
                        + (precompute.Error is null ? "。" : "：" + precompute.Error),
            _ => "参照潜在：" + precompute.State,
        };
    }

    private static string Count(int? value) =>
        (value ?? 0).ToString(CultureInfo.InvariantCulture);

    private static string Reason<T>(WrapperResult<T> result) =>
        result.Error?.Message ?? result.FailureReason ?? "理由は分かりません。";
}
