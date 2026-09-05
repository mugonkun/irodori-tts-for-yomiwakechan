using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Ledger;

/// <summary>vc_redist をどうするかの判断。</summary>
public enum VcRedistAction
{
    /// <summary>System32 に <c>msvcp140.dll</c> が在るので何もしない（裁定 54）。</summary>
    Skip,

    /// <summary>入れる（取得 → sha256 → UAC 昇格で silent 実行）。</summary>
    Install,

    /// <summary>System32 が読めなかった＝判断できないので利用者に問う。</summary>
    Unknown,
}

/// <summary>System32 の <c>msvcp140.dll</c> の観測（<b>判断の入力</b>）。</summary>
/// <param name="Present">在ったか。</param>
/// <param name="FileVersion">ファイル版（例 <c>14.42.34438.0</c>）。読めなければ null。</param>
/// <param name="ProbeError">読もうとして落ちた理由（null なら読めた）。</param>
public sealed record MsvcpState(bool Present, string? FileVersion, string? ProbeError);

/// <summary>判断の結果（理由 1 行つき）。</summary>
/// <param name="Action">どうするか。</param>
/// <param name="Message">UI に出す 1 行。</param>
public sealed record VcRedistVerdict(VcRedistAction Action, string Message);

/// <summary>
/// <c>vc_redist</c> を入れるかどうかの判断（<b>純関数</b>）。
/// <para>
/// 要る理由＝埋め込み Python の zip は <c>vcruntime140.dll</c>／<c>vcruntime140_1.dll</c> を
/// 持つが <b><c>msvcp140.dll</c> を持たない</b>のに、<c>torch/lib/c10.dll</c> が
/// <c>MSVCP140.dll</c> を import する（<c>ledger/vc_redist.json</c> の <c>notes</c> の逐語）。
/// </para>
/// <para>
/// <b>判定は「在れば飛ばす」だけ</b>（裁定 54＝便 B の U-8 は「検出して飛ばす」判定の実射のみ）。
/// 版の下限は<b>まだ決まっていない</b>ので <see cref="MinimumFileVersion"/> は既定 null＝見ない。
/// U-8 の逐語が出たら<b>ここ 1 箇所</b>に閾を入れる（席は推測で断定しない＝裁定 18）。
/// </para>
/// </summary>
public static class VcRedistDecision
{
    /// <summary>System32 の <c>msvcp140.dll</c>（64 bit プロセスから見える実体）。</summary>
    public static string DefaultDllPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "msvcp140.dll");

    /// <summary>
    /// 版の下限。<b>いまは null＝在るだけで飛ばす</b>（裁定 54＝便 B の U-8 は「検出して飛ばす」
    /// 判定の実射のみ）。U-8 が「この版では torch が落ちる」を実射で示したら
    /// <b>この 1 行</b>に値を入れる（<see cref="DriverRequirement"/> の閾と同じ作法）。
    /// </summary>
    public static readonly string? MinimumFileVersion = null;

    /// <summary>実際に System32 を見る。</summary>
    public static MsvcpState Probe(string? dllPath = null)
    {
        var path = dllPath ?? DefaultDllPath;
        try
        {
            if (!File.Exists(path))
            {
                return new MsvcpState(false, null, null);
            }

            var info = FileVersionInfo.GetVersionInfo(path);
            return new MsvcpState(true, info.FileVersion, null);
        }
        catch (IOException ex)
        {
            return new MsvcpState(false, null, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new MsvcpState(false, null, ex.Message);
        }
    }

    /// <summary>観測から判断する（<b>純関数</b>）。</summary>
    /// <param name="state">System32 の観測。</param>
    /// <param name="ledgerVersion">台帳の <c>version</c>（告知の文言にだけ使う）。</param>
    /// <param name="minimumFileVersion">
    /// 版の下限。既定 null＝<see cref="MinimumFileVersion"/> と同じ「在れば飛ばす」。
    /// </param>
    public static VcRedistVerdict Evaluate(
        MsvcpState state, string? ledgerVersion = null, string? minimumFileVersion = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.ProbeError is not null)
        {
            return new VcRedistVerdict(
                VcRedistAction.Unknown,
                "System32 の msvcp140.dll を確かめられなかった：" + state.ProbeError);
        }

        if (!state.Present)
        {
            var wanted = string.IsNullOrWhiteSpace(ledgerVersion) ? string.Empty : "（" + ledgerVersion + "）";
            return new VcRedistVerdict(
                VcRedistAction.Install,
                "msvcp140.dll が無いので Microsoft Visual C++ 再頒布可能パッケージ" + wanted + "を入れる。");
        }

        var have = state.FileVersion;
        if (minimumFileVersion is { } minimum && TryCompare(have, minimum) is { } comparison && comparison < 0)
        {
            return new VcRedistVerdict(
                VcRedistAction.Install,
                "msvcp140.dll が " + (have ?? "不明") + " で下限 " + minimum + " に届かないので入れ直す。");
        }

        return new VcRedistVerdict(
            VcRedistAction.Skip,
            "msvcp140.dll は既に在る" + (have is null ? string.Empty : "（" + have + "）") + "ので飛ばす。");
    }

    /// <summary>4 節までの版の比較（比べられなければ null）。</summary>
    public static int? TryCompare(string? left, string? right)
    {
        if (!Version.TryParse(Normalize(left), out var a) || !Version.TryParse(Normalize(right), out var b))
        {
            return null;
        }

        return a.CompareTo(b);
    }

    private static string? Normalize(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        // "14.44.35211.0 (foo)" のような名乗りを数の部分だけにする
        var text = version.Trim();
        var cut = text.IndexOf(' ');
        return cut > 0 ? text[..cut] : text;
    }
}

/// <summary>vc_redist を通した結果。</summary>
/// <param name="Ok">入った（または飛ばしてよかった）か。</param>
/// <param name="Action">実際にやったこと。</param>
/// <param name="ExitCode">走らせたときの終了コード（飛ばしたら null）。</param>
/// <param name="Message">理由 1 行。</param>
/// <param name="RebootRequired">3010＝再起動が要る。</param>
public sealed record VcRedistResult(
    bool Ok,
    VcRedistAction Action,
    int? ExitCode,
    string Message,
    bool RebootRequired)
{
    /// <summary>
    /// <b>利用者に問わないと進めない</b>（裁定 87 ⑷・low 7）＝System32 が読めず、入れるべきか
    /// 飛ばしてよいかが判らなかった。<b>「入れる」に落とさない</b>＝UAC の窓を勝手に出さない。
    /// </summary>
    public bool NeedsUserDecision => !Ok && Action == VcRedistAction.Unknown;
}

/// <summary>
/// <c>ledger/vc_redist.json</c> の installer を通す。
/// <list type="number">
/// <item>System32 の <c>msvcp140.dll</c> を見て、在れば<b>何も落とさず飛ばす</b>（裁定 54）。</item>
/// <item>無ければ台帳の URL（url→fallback_url）から取り、sha256 を検証する。</item>
/// <item>台帳の <c>silent_args</c> で <b>UAC 昇格して</b>走らせる（利用者操作 1 回）。</item>
/// </list>
/// <para>
/// <b>引数は台帳の逐語をそのまま使う</b>＝<c>ledger/vc_redist.json</c> の
/// <c>/install /quiet /norestart</c>。裁定 87 ⑷ で<b>台帳が正</b>と決着し、設計書 §6 の
/// <c>/passive</c> は工具席が台帳に合わせて直した（是正・便 D（2）＝この註の
/// 「食い違っている」は決着前の記述だった）。<see cref="SilentArgsOverride"/> は
/// 差し替えの継ぎ目として残す。
/// </para>
/// <para>継ぎ目は public コンストラクタ（<see cref="IDownloader"/> とプロセス起動子を差せる）。</para>
/// </summary>
public sealed class VcRedistInstaller
{
    /// <summary>既に新しい物が入っている（installer 自身の申告）。</summary>
    public const int ExitAlreadyNewer = 1638;

    /// <summary>入ったが再起動が要る。</summary>
    public const int ExitRebootRequired = 3010;

    /// <summary>利用者が UAC を断った／中止した。</summary>
    public const int ExitUserCancelled = 1602;

    private readonly IDownloader _downloader;
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<int>> _runElevated;

    /// <summary>実機用（<see cref="RunElevatedAsync"/> で UAC 昇格する）。</summary>
    public VcRedistInstaller(IDownloader downloader)
        : this(downloader, RunElevatedAsync)
    {
    }

    /// <summary>テストの継ぎ目＝installer を「走らせる」手を差せる。</summary>
    public VcRedistInstaller(
        IDownloader downloader,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<int>> runElevated)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(runElevated);
        _downloader = downloader;
        _runElevated = runElevated;
    }

    /// <summary>台帳の <c>silent_args</c> を上書きする（既定 null＝台帳のまま）。</summary>
    public IReadOnlyList<string>? SilentArgsOverride { get; init; }

    /// <summary>System32 を見る場所（テストは偽の檔を指す）。</summary>
    public string? DllPathOverride { get; init; }

    /// <summary>
    /// System32 の観測そのものを差し替える（既定＝<see cref="VcRedistDecision.Probe"/>）。
    /// <b>「読めなかった」（<see cref="MsvcpState.ProbeError"/> つき）を作れるのはここだけ</b>＝
    /// <c>File.Exists</c> は権限が無くても投げずに偽を返すので、檔の場所だけでは再現できない。
    /// </summary>
    public Func<MsvcpState>? StateProbe { get; init; }

    /// <summary>
    /// <b>「判らない」でも入れる</b>（既定 偽）。<see cref="VcRedistAction.Unknown"/> のとき、
    /// 利用者が画面で「入れる」と答えた場合だけ真で作り直す
    /// （<c>ViewModels/FirstRunViewModel.AskVcRedist</c>）。
    /// <b>既定で真にはしない</b>＝裁定 87 ⑷「Unknown は『入れる』に落とさず利用者に問う」。
    /// </summary>
    public bool AssumeInstallWhenUnknown { get; init; }

    /// <summary>
    /// 判定 →（要れば）取得 → sha256 → 昇格実行。<b>取得は判定の後</b>＝
    /// 在る機体では 1 バイトも落とさない。
    /// </summary>
    public async Task<VcRedistResult> EnsureAsync(
        VcRedistLedger ledger,
        string cacheDir,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDir);

        var item = ledger.Installer
            ?? throw new LedgerException("vc_redist.json に installer が無い。");

        var state = StateProbe is null ? VcRedistDecision.Probe(DllPathOverride) : StateProbe();
        var verdict = VcRedistDecision.Evaluate(
            state, item.Version, VcRedistDecision.MinimumFileVersion);

        if (verdict.Action == VcRedistAction.Skip)
        {
            return new VcRedistResult(true, VcRedistAction.Skip, null, verdict.Message, false);
        }

        // **Unknown は「入れる」に落とさない**（裁定 87 ⑷・是正・2026-09-05・low 7）。
        // System32 が読めなかったのは「無い」ではない。落として UAC を出すと、
        // 既に入っている機体で管理者の窓を出し（利用者操作を 1 つ増やし）、断られれば
        // 1602 で初回取得ごと止まる。判らないときは<b>確認が要る</b>と言って止まる。
        if (verdict.Action == VcRedistAction.Unknown && !AssumeInstallWhenUnknown)
        {
            return new VcRedistResult(
                false,
                VcRedistAction.Unknown,
                null,
                verdict.Message
                + " Visual C++ 再頒布可能パッケージを入れるかどうかは利用者に確かめてください"
                + "（勝手には入れません）。",
                false);
        }

        // 利用者が「入れる」と答えた Unknown は、以後 Install と同じ道を通る
        // （結末の Action も Install＝「判らないまま入れた」を「判らない」と記帳しない）。
        var action = verdict.Action == VcRedistAction.Unknown ? VcRedistAction.Install : verdict.Action;

        var download = await _downloader
            .DownloadAsync(DownloadRequest.FromLedgerItem(item, cacheDir), progress, cancellationToken)
            .ConfigureAwait(false);

        if (!download.Ok)
        {
            return new VcRedistResult(
                false, action, null,
                download.FailureReason ?? "vc_redist を取得できなかった。", false);
        }

        var args = SilentArgsOverride ?? item.SilentArgs;
        if (args.Count == 0)
        {
            throw new LedgerException("vc_redist.json の installer に silent_args が無い。");
        }

        var exitCode = await _runElevated(download.Path, args, cancellationToken).ConfigureAwait(false);
        return Describe(action, exitCode);
    }

    /// <summary>終了コードを結末に落とす（<b>純関数</b>）。</summary>
    public static VcRedistResult Describe(VcRedistAction action, int exitCode) => exitCode switch
    {
        0 => new VcRedistResult(true, action, 0, "Visual C++ 再頒布可能パッケージを入れた。", false),
        ExitAlreadyNewer => new VcRedistResult(
            true, action, exitCode, "より新しい Visual C++ 再頒布可能パッケージが既に入っていた。", false),
        ExitRebootRequired => new VcRedistResult(
            true, action, exitCode, "Visual C++ 再頒布可能パッケージを入れた（再起動が要る）。", true),
        ExitUserCancelled => new VcRedistResult(
            false, action, exitCode, "Visual C++ 再頒布可能パッケージの導入が中止された（管理者の許可が要る）。", false),
        _ => new VcRedistResult(
            false, action, exitCode,
            string.Create(CultureInfo.InvariantCulture,
                $"Visual C++ 再頒布可能パッケージの導入が終了コード {exitCode} で失敗した。"),
            false),
    };

    /// <summary>UAC 昇格して走らせる（<c>runas</c>＝利用者操作 1 回）。</summary>
    public static async Task<int> RunElevatedAsync(
        string exePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        ArgumentNullException.ThrowIfNull(arguments);

        var info = new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = false,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED＝UAC の窓で「いいえ」。installer 自身の中止と同じ扱いにする。
            return ExitUserCancelled;
        }

        if (process is null)
        {
            throw new InvalidOperationException("vc_redist を起動できなかった。");
        }

        using (process)
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
    }
}
