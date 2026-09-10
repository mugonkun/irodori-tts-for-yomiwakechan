using System;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Server;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>状態の帯の丸の色（色そのものは XAML の Style が持つ）。</summary>
public enum BandSeverity
{
    /// <summary>灰＝準備中・止まっている（失敗ではない）。</summary>
    Neutral,

    /// <summary>緑＝使える。</summary>
    Ok,

    /// <summary>赤＝止まりました。</summary>
    Bad,
}

/// <summary>
/// 帯に出す「次の 1 手」の種類（<b>釦の中身は窓が決める</b>＝ここは何をするかの名前だけ持つ）。
/// </summary>
public enum BandActionKind
{
    /// <summary>1 手を出さない。</summary>
    None,

    /// <summary>もう一度動かす（<c>StartCommand</c>）。</summary>
    Start,

    /// <summary>いったん止めて、動かし直す（<c>StopCommand</c> → <c>StartCommand</c>）。</summary>
    Restart,

    /// <summary>はじめの準備（ウィザードを開く＝窓が要る仕事）。</summary>
    FirstRun,

    /// <summary>
    /// 動かすための一式を<b>入れ直す</b>（<c>RebuildRuntimeCommand</c>）。
    /// 札は <see cref="UiStrings.StatusRebuildButton"/> の 1 箇所だけが綴る（是正・段 C の検分）＝
    /// `v2-copy.md` §1-2 の 88 行目と §3-2 E-04 が正で、`v2-spec.md` §2-2 の
    /// 「新しくする」は copy に揃える（同じ操作に 2 つの名を出さない）。
    /// </summary>
    RebuildRuntime,

    /// <summary>設定の詳細を開く（設定のタブへ移り、畳みを開く）。</summary>
    OpenSettings,

    /// <summary>ログを開く（在り処ごと開く）。</summary>
    OpenLog,

    /// <summary>NVIDIA のドライバ更新ページを開く。</summary>
    OpenDriverPage,

    /// <summary>使い方（困ったときは）を開く。</summary>
    OpenGuide,
}

/// <summary>
/// 帯が文を組むのに要る事実（<b>内部の 1 行から掻き回して取らない</b>＝
/// 判っている側が渡す。判らない欄は null のままでよく、そのときは文から括弧ごと落ちる）。
/// </summary>
/// <param name="RuntimeLoaded"><c>/ywk/status.runtime.loaded</c>（声を読み終えたか）。</param>
/// <param name="UserStopped">利用者が止めた（＝開いた直後の「まだ起こしていない」と区別する）。</param>
/// <param name="AutoStartDisabled">
/// <c>autoStartServer:false</c> を明示している機体（<b>誰も起こさない</b>＝待っていても始まらない）。
/// <b>この 1 行が無いと、釦を消した機体で行き止まりになる</b>（`v2-spec.md` §2-5 末尾）。
/// </param>
/// <param name="DriverVersion">読めたグラフィックスドライバの版（例＝<c>537.58</c>）。</param>
/// <param name="Variant">いまの動かし方（台帳の綴り）。</param>
/// <param name="AlternativeVariant">切り替えれば動く動かし方（無ければ null）。</param>
/// <param name="GpuName">設定に覚えてあるグラフィックスの名前。</param>
/// <param name="MissingModelCount">足りない声のデータの数（判らなければ null）。</param>
/// <param name="ExitCode">
/// 落ちた子プロセスの終了コード（D6 の ⑵ に差す<b>唯一の手がかり</b>＝利用者が報告に書ける数。
/// 判らない回は null＝括弧ごと落とす）。
/// </param>
public sealed record BandContext(
    bool RuntimeLoaded = false,
    bool UserStopped = false,
    bool AutoStartDisabled = false,
    string? DriverVersion = null,
    string? Variant = null,
    string? AlternativeVariant = null,
    string? GpuName = null,
    int? MissingModelCount = null,
    int? ExitCode = null);

/// <summary>帯の 1 行（丸・ひとこと・理由 1 行・1 手）。</summary>
/// <param name="Severity">丸の色。</param>
/// <param name="Headline">名乗る語（<b>3 語だけ</b>）。</param>
/// <param name="Reason">⑴ 何が起きたか ＋ ⑵ なぜか を 1 行に畳んだ物（ふだんは null）。</param>
/// <param name="ActionLabel">⑶ 次にやること（釦の札・出せる手が無ければ null）。</param>
/// <param name="Action">その 1 手の種類。</param>
public sealed record BandLine(
    BandSeverity Severity,
    string Headline,
    string? Reason,
    string? ActionLabel,
    BandActionKind Action);

/// <summary>
/// 状態の帯の文を組む<b>純関数</b>（`v2-spec.md` §2-1・§2-1a・§2-1b）。
/// <para>
/// <b>名乗るのは 3 語だけ</b>＝準備しています…／使えます／止まりました。内部の
/// <see cref="ServerState"/> の 6 値は 1 つも触らない（<see cref="StatusViewModel.StateLabel"/> は
/// 6 語のまま <c>MainStateText</c> に残る＝無人検分の錨）。
/// </para>
/// <para>
/// <b>失敗の 1 行は 3 部品で言い直す</b>（憲章 原則 6）＝⑴ 何が起きたか（利用者の言葉）
/// ⑵ なぜか（1 行・技術語つきでよい）⑶ 次にやること（1 つだけ）。<b>⑶ の無い文が出る道は 1 本も無い</b>
/// ＝見分けがつかない 1 行は必ず〔ログを開く〕へ落ちる。
/// </para>
/// <para>
/// <b>見分けは前方一致と定数だけで行う</b>（`v2-spec.md` §2-1b）＝正規表現で本文を掻き回さない。
/// ここに置いた標識は、内部の 1 行を実際に綴っている所（<see cref="ServerBindFailure.Message"/>・
/// <see cref="ServerExitCodes.Describe"/>・<c>VariantRecommendation.StartRefusalReason</c>・
/// <c>VariantGate.Decide</c>・<c>GpuResolver.NotFoundMessage</c>・
/// <c>GpuEnumerator.StartStalledMessage</c>）の綴りの写しであり、<b>試験は写しではなく本物に
/// 組ませた 1 行を通す</b>ので、向こうの綴りが変われば試験が落ちる。
/// </para>
/// <para>
/// <b>ログへ落とす文は従来のまま</b>＝画面用と記録用を分ける。ここは読むだけで、元の理由は捨てない。
/// </para>
/// </summary>
public static class BandText
{
    /// <summary>準備しています…（灰）。</summary>
    public const string Preparing = "準備しています…";

    /// <summary>声を読み込んでいる間の 1 行（初回は長い）。</summary>
    public const string PreparingVoices = "準備しています… 声を読み込んでいます（初回は 1〜2 分かかります）";

    /// <summary>使えます（緑）。</summary>
    public const string Ready = "使えます";

    /// <summary>使えます（声を先に用意している間）。</summary>
    public const string ReadyWarming = "使えます（声を準備しています）";

    /// <summary>利用者が止めた（灰）。</summary>
    public const string StoppedByUser = "止まっています";

    /// <summary>止まりました（赤）。</summary>
    public const string Failed = "止まりました。";

    /// <summary>
    /// 自動で起こさない設定の機体への導線（<b>行き止まりを作らない</b>＝`v2-spec.md` §2-5 末尾）。
    /// </summary>
    public const string AutoStartOffHint =
        "アプリを開いても自動で準備しない設定になっています。（設定 › 詳細 › 読み上げの動作 で変えられます）";

    /// <summary>連携の 1 行＝呼べる状態である。</summary>
    public const string HostAvailable = "読み分けちゃん2 から使えます";

    /// <summary>連携の 1 行＝この起動ではまだ 1 度も呼ばれていない。</summary>
    public const string HostIdle = "まだ読み分けちゃん2 から呼ばれていません";

    /// <summary>連携の 1 行＝いま読み上げに使われている（現在形）。</summary>
    public const string HostBusy = "いま読み分けちゃん2 の読み上げに使われています";

    /// <summary>NVIDIA のドライバ更新ページ（⑶ の行き先）。</summary>
    public const string DriverPageUrl = "https://www.nvidia.com/Download/index.aspx";

    // ---- 内部の 1 行の標識（綴りの出所は上の註のとおり・試験が本物と突き合わせる） ----

    private const string RuntimeMissingMarker = "変種「";
    private const string ModelsMissingMarker = "モデルがまだありません（不足＝";
    private const string GpuNotFoundMarker = " が見つかりません。設定で GPU を選び直してください。";
    private const string DriverRefusalMarker = "このドライバ（";
    private const string DriverRefusalNoAlternative = UiStrings.SwitchInFirstRun + "。";
    private const string GateCannotSeeMarker = "この機体で GPU を見られません（";
    private const string GateProbeFailedMarker = " GPU 検分ができませんでした（";
    private const string GateBelowMinimumMarker = " に届きません。";
    private const string PortInUseMarker = "このアプリのつなぎ口（";
    private const string SpawnStalledMarker = "実行系を起こす段が ";
    private const string SpawnFailedMarker = "サーバのプロセスを起こせませんでした";
    private const string PythonFailedMarker = "python.exe を起こせませんでした（";
    private const string ReadyTimeoutMarker = " 秒で終わりませんでした。";
    private const string ExitPrecheckMarker = "起動前の検査で止まった";
    private const string ExitUpstreamMarker = "モデルの読み込みに失敗した。";
    private const string ExitAbnormalMarker = "サーバが異常終了した（終了コード ";
    private const string ExitCleanMarker = "サーバが終了した。";

    /// <summary>
    /// 帯の 1 行を組む（<b>純関数</b>）。
    /// </summary>
    /// <param name="state">状態機械のいまの値（6 値のまま渡す）。</param>
    /// <param name="internalReason">その状態に付いている内部の 1 行（<b>ログの綴りのまま</b>・null 可）。</param>
    /// <param name="ctx">文を組むのに要る事実（判らない欄は null のままでよい）。</param>
    public static BandLine For(ServerState state, string? internalReason, BandContext? ctx = null)
    {
        var facts = ctx ?? new BandContext();
        var reason = internalReason?.Trim();
        if (reason?.Length == 0)
        {
            reason = null;
        }

        // 応答が消えて降格した回は**赤にしない**（帯は「準備しています…」へ戻る＝§2-1a D3）。
        if (reason is not null
            && string.Equals(reason, ServerStateMachine.UnreachableReason, StringComparison.Ordinal))
        {
            return new BandLine(
                BandSeverity.Neutral,
                Preparing,
                "反応がなくなりました。 返事が返らなくなりました。",
                "いったん止めて、動かし直す",
                BandActionKind.Restart);
        }

        if (state is ServerState.Failed)
        {
            return Explain(reason, facts);
        }

        return state switch
        {
            ServerState.Ready => new BandLine(BandSeverity.Ok, Ready, null, null, BandActionKind.None),
            ServerState.Warming =>
                new BandLine(BandSeverity.Ok, ReadyWarming, null, null, BandActionKind.None),
            ServerState.Listening => new BandLine(
                BandSeverity.Neutral,
                facts.RuntimeLoaded ? Preparing : PreparingVoices,
                null,
                null,
                BandActionKind.None),
            ServerState.Stopped when facts.UserStopped || facts.AutoStartDisabled => new BandLine(
                BandSeverity.Neutral,
                StoppedByUser,
                facts.AutoStartDisabled && !facts.UserStopped ? AutoStartOffHint : null,
                "もう一度動かす",
                BandActionKind.Start),
            _ => new BandLine(BandSeverity.Neutral, Preparing, null, null, BandActionKind.None),
        };
    }

    /// <summary>
    /// 連携の 1 行（<b>純関数</b>・§2-1c）。<b>ポート番号も <c>/health</c> も出さない。</b>
    /// </summary>
    /// <param name="state">状態機械のいまの値。</param>
    /// <param name="hostFieldPresent">
    /// <c>/ywk/status</c> に本体の走行数の欄が在るか（古い個体＝偽＝「使えます」だけを出す＝嘘にならない）。
    /// </param>
    /// <param name="hostSeen">この起動で 1 度でも本体から呼ばれたか。</param>
    /// <param name="hostBusy">いま本体の読み上げが走っているか。</param>
    public static string HostLine(
        ServerState state, bool hostFieldPresent, bool hostSeen, bool hostBusy)
    {
        if (state is not (ServerState.Ready or ServerState.Warming))
        {
            return string.Empty;
        }

        if (!hostFieldPresent)
        {
            return HostAvailable;
        }

        if (hostBusy)
        {
            return HostBusy;
        }

        return hostSeen ? HostAvailable : HostIdle;
    }

    /// <summary>失敗の 1 行を 3 部品へ言い直す（<b>純関数</b>・§2-1a の A〜D 群）。</summary>
    private static BandLine Explain(string? reason, BandContext ctx)
    {
        if (reason is null)
        {
            return Fail("うまく動きませんでした。", "理由が分かりませんでした。", "ログを開く", BandActionKind.OpenLog);
        }

        // A 群＝起こす前に断った（取得が未了）。
        if (reason.Contains(Services.Models.AcquisitionCheck.Hint, StringComparison.Ordinal))
        {
            var why = reason.StartsWith(RuntimeMissingMarker, StringComparison.Ordinal)
                ? "動かすための一式が、まだこのパソコンにありません。"
                : reason.StartsWith(ModelsMissingMarker, StringComparison.Ordinal)
                    ? MissingModelsLine(ctx.MissingModelCount)
                    : "はじめの準備が最後まで済んでいません。";
            return Fail("まだ準備が終わっていません。", why, "はじめの準備をする", BandActionKind.FirstRun);
        }

        // A3＝設定に覚えてある GPU が居ない。
        if (reason.EndsWith(GpuNotFoundMarker, StringComparison.Ordinal))
        {
            var name = string.IsNullOrWhiteSpace(ctx.GpuName) ? null : ctx.GpuName!.Trim();
            return Fail(
                "前に使っていたグラフィックスが見つかりません。",
                "設定に覚えてある GPU" + (name is null ? string.Empty : "（" + name + "）")
                    + "が、いまこのパソコンに見当たりません（差し替えたときに起きます）。",
                "使うグラフィックスを選び直す",
                BandActionKind.OpenSettings);
        }

        // A4／A4'＝ドライバが下限に届かないので起こす前に断った。
        if (reason.StartsWith(DriverRefusalMarker, StringComparison.Ordinal))
        {
            return reason.EndsWith(DriverRefusalNoAlternative, StringComparison.Ordinal)
                ? Fail(
                    "この GPU では、いまの動かし方が使えません。",
                    "グラフィックスドライバが古いためです" + DriverNumbers(ctx)
                        + "。このパソコンで使える別の動かし方がありません。",
                    "NVIDIA のドライバ更新ページを開く",
                    BandActionKind.OpenDriverPage)
                : Fail(
                    "この GPU では、いまの動かし方が使えません。",
                    "グラフィックスドライバが古いためです" + DriverNumbers(ctx) + "。"
                        + AlternativeWorks(ctx),
                    SwitchLabel(ctx),
                    BandActionKind.FirstRun);
        }

        // B 群＝動かし方の門が断った。
        if (reason.Contains(GateCannotSeeMarker, StringComparison.Ordinal))
        {
            return Fail(
                "グラフィックスが使えませんでした。",
                Particle(VariantName(ctx.Variant), "で") + "このパソコンの GPU を見つけられませんでした"
                    + DriverOnly(ctx) + "。",
                SwitchOrSettingsLabel(ctx),
                SwitchOrSettingsKind(ctx));
        }

        if (reason.Contains(GateProbeFailedMarker, StringComparison.Ordinal))
        {
            return Fail(
                "グラフィックスを確かめられませんでした。",
                "確認の途中で止まりました。確かめないまま動かすと、"
                    + "最初にしゃべらせたときにアプリごと終わってしまうことがあります。",
                "もう一度動かす",
                BandActionKind.Start);
        }

        if (reason.StartsWith("ドライバ ", StringComparison.Ordinal)
            && reason.Contains(GateBelowMinimumMarker, StringComparison.Ordinal))
        {
            return Fail(
                "この GPU では、いまの動かし方が使えません。",
                "グラフィックスドライバが古いためです" + DriverNumbers(ctx) + "。",
                SwitchOrSettingsLabel(ctx),
                SwitchOrSettingsKind(ctx));
        }

        // C 群・D 群＝起こす途中で失敗した／走り出してから落ちた。
        if (reason.Contains(PortInUseMarker, StringComparison.Ordinal))
        {
            return Fail(
                "ほかのソフトが、このアプリのつなぎ口（18088）を使っています。",
                "同じ番号を先に使っているソフトがあると、読み上げの用意ができません。",
                "使っているソフトを調べる方法を見る",
                BandActionKind.OpenGuide);
        }

        if (reason.StartsWith(SpawnStalledMarker, StringComparison.Ordinal)
            || reason.StartsWith(SpawnFailedMarker, StringComparison.Ordinal)
            || reason.StartsWith(PythonFailedMarker, StringComparison.Ordinal))
        {
            return Fail(
                "動かすための一式を起こせませんでした。",
                "一式のファイルが壊れているようです。",
                UiStrings.StatusRebuildButton,
                BandActionKind.RebuildRuntime);
        }

        if (reason.StartsWith("起動が ", StringComparison.Ordinal)
            && reason.Contains(ReadyTimeoutMarker, StringComparison.Ordinal))
        {
            return Fail(
                "準備に時間がかかりすぎました。",
                "待っても声の読み込みが終わりませんでした。",
                "もう一度動かす",
                BandActionKind.Restart);
        }

        // D2＝<c>runtime.error</c> の 1 行。**その本文は帯に出さない**（憲章 原則 6／原則 7・
        // `v2-copy.md` §3-2 の書き方の規則）＝生の記録・スタックトレース・絶対パスが載りうる
        // （`ServerLogParser.ForLog` は 1000 字で切るだけで畳まない）。⑵ は E-07 の固定文にし、
        // 元の 1 行はログ（`LauncherLogFile`）と 詳しい状態 の `StatusReasonText` に残す。
        if (reason.StartsWith(ServerStateMachine.RuntimeLoadFailedPrefix, StringComparison.Ordinal))
        {
            return Fail(
                "声の読み込みに失敗しました。",
                "声のデータが途中までしか入っていない可能性があります。",
                "はじめの準備をやり直す",
                BandActionKind.FirstRun);
        }

        if (reason.StartsWith(ExitPrecheckMarker, StringComparison.Ordinal))
        {
            return Fail(
                "設定と合いませんでした。",
                "動かすための一式が、いまの設定では動けません。",
                "設定の詳細を開く",
                BandActionKind.OpenSettings);
        }

        if (reason.StartsWith(ExitUpstreamMarker, StringComparison.Ordinal))
        {
            return Fail(
                "声のデータを読み込めませんでした。",
                "読み込みの途中で終わりました。",
                "はじめの準備をやり直す",
                BandActionKind.FirstRun);
        }

        if (reason.StartsWith(ExitAbnormalMarker, StringComparison.Ordinal))
        {
            return Fail(
                "とちゅうで終わってしまいました。",
                "予期しない終わり方をしました" + ExitCodeNumber(ctx) + "。",
                "ログを開く",
                BandActionKind.OpenLog);
        }

        // D7（exit 0）＝**理由の行は出さない**（`v2-spec.md` §2-1a の D7）＝
        // 見出しと同じ「止まりました。」を 2 度並べない。`Fail` を通さないのはそのためである。
        if (reason.StartsWith(ExitCleanMarker, StringComparison.Ordinal))
        {
            return new BandLine(BandSeverity.Bad, Failed, null, "もう一度動かす", BandActionKind.Start);
        }

        // 見分けがつかない 1 行（新しい失敗・上流の変な行）＝必ず〔ログを開く〕を付ける。
        return Fail("うまく動きませんでした。", "理由が分かりませんでした。", "ログを開く", BandActionKind.OpenLog);
    }

    private static BandLine Fail(string what, string? why, string label, BandActionKind kind) =>
        new(BandSeverity.Bad, Failed, why is null ? what : what + " " + why, label, kind);

    private static string MissingModelsLine(int? count) =>
        count is int n and > 0
            ? "声のデータが " + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 件足りません。"
            : "声のデータが足りません。";

    /// <summary>「（いまの版 537.58・CUDA 13.0 には 580.00 以上が必要）」＝判らない欄は括弧ごと落とす。</summary>
    private static string DriverNumbers(BandContext ctx)
    {
        var driver = string.IsNullOrWhiteSpace(ctx.DriverVersion) ? null : ctx.DriverVersion!.Trim();
        var minimum = DriverRequirement.Minimum(ctx.Variant);
        if (driver is null && minimum is null)
        {
            return string.Empty;
        }

        if (driver is null)
        {
            return "（" + Particle(VariantName(ctx.Variant), "には") + " " + minimum + " 以上が必要です）";
        }

        return minimum is null
            ? "（いまの版 " + driver + "）"
            : "（いまの版 " + driver + "・" + Particle(VariantName(ctx.Variant), "には")
                + " " + minimum + " 以上が必要です）";
    }

    /// <summary>
    /// 動かし方の名に助詞を継ぐ（<c>CUDA 13.0 には</c>／<c>いまの動かし方には</c>）。
    /// 名が ASCII で終わるときだけ空白を入れる＝二重の切れ目を作らない
    /// （<c>VariantGate.With</c> と同じ作法）。
    /// </summary>
    private static string Particle(string name, string particle) =>
        name.Length > 0 && name[^1] <= 0x7F ? name + " " + particle : name + particle;

    private static string DriverOnly(BandContext ctx) =>
        string.IsNullOrWhiteSpace(ctx.DriverVersion)
            ? string.Empty
            : "（ドライバ " + ctx.DriverVersion!.Trim() + "）";

    /// <summary>「（終了コード 9）」＝判らない回は括弧ごと落とす（`DriverNumbers` と同じ作法）。</summary>
    private static string ExitCodeNumber(BandContext ctx) =>
        ctx.ExitCode is int code
            ? "（終了コード " + code.ToString(System.Globalization.CultureInfo.InvariantCulture) + "）"
            : string.Empty;

    private static string AlternativeWorks(BandContext ctx) =>
        string.IsNullOrWhiteSpace(ctx.AlternativeVariant)
            ? "別の動かし方に切り替えれば動きます。"
            : VariantName(ctx.AlternativeVariant) + " なら動きます。";

    private static string SwitchLabel(BandContext ctx) =>
        string.IsNullOrWhiteSpace(ctx.AlternativeVariant)
            ? "はじめの準備をやり直す"
            : VariantName(ctx.AlternativeVariant) + " で準備しなおす";

    private static string SwitchOrSettingsLabel(BandContext ctx) =>
        string.IsNullOrWhiteSpace(ctx.AlternativeVariant)
            ? "設定の詳細を開く"
            : VariantName(ctx.AlternativeVariant) + " で準備しなおす";

    private static BandActionKind SwitchOrSettingsKind(BandContext ctx) =>
        string.IsNullOrWhiteSpace(ctx.AlternativeVariant)
            ? BandActionKind.OpenSettings
            : BandActionKind.FirstRun;

    /// <summary>
    /// 文中に差す<b>動かし方の短い名</b>（判らない綴りは「いまの動かし方」＝台帳の生の綴りを出さない）。
    /// <para>
    /// <b><see cref="RuntimeVariants.ShortDisplayName"/> を帯から呼ばない</b>のが眼目である＝
    /// あちらは <c>rocm-gfx1151</c> に「Radeon gfx1151」を、<c>cuda</c> に「CUDA 版」を返す。
    /// <c>gfx1151</c> は憲章 附録 5 が「綴りだけを落とす」と決めた語であり、
    /// 「CUDA 版」だけ・「ROCm 版」だけの名乗りは `v2-spec.md` §6-1（版の名は
    /// <b>RTX（CUDA）／Radeon（ROCm）の併記が正</b>）に反する。ここに置く 4 語は
    /// 憲章 §6-1 の「変種」の欄（CUDA 13.0／CUDA 12.6／ROCm／CPU）と同じ物である。
    /// </para>
    /// <para>
    /// <b>公開してあるのは、はじめの準備も同じ 4 語で名乗るためである</b>（v2.0 段 B・
    /// <see cref="FirstRunViewModel.DecisionLineFor"/>）＝同じ画面に 2 通りの名が並ばない。
    /// </para>
    /// </summary>
    public static string VariantName(string? variant) => variant?.Trim() switch
    {
        RuntimeVariants.Cu130 => "CUDA 13.0",
        RuntimeVariants.Cu126 => "CUDA 12.6",
        RuntimeVariants.RocmGfx1151 => "ROCm",
        RuntimeVariants.Cpu => "CPU",
        _ => "いまの動かし方",
    };
}
