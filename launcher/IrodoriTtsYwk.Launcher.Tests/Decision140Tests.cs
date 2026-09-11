using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using IrodoriTtsYwk.Launcher.Services.Security;
using IrodoriTtsYwk.Launcher.Services.Server;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// <b>裁定 140＝スマート アプリ コントロール</b>（v2.0.2・文言は 145〜147 で v2.0.3 に直した）。
/// <para>
/// 司令官の手動検分（RTX 機・v2.0.1）＝参照ボイスを選んで〔しゃべらせる〕と、Windows が
/// <c>_spline.cp312-win_amd64.pyd</c>（scipy.signal の拡張）を止め、帯に<b>生の ImportError</b> が出た。
/// ここで釘を打つのは 4 つ＝
/// ⑴ 状態の読み（0／1／2／欄が無い／投げる → 無効・有効・評価中・無効・無効）
/// ⑵ 畳み（生の 1 行 → 3 部品・<b>生の字は帯に 1 文字も出ない</b>）
/// ⑶ はじめの準備の告知は<b>有効な機体だけ</b>
/// ⑷ 発話テストの失敗の道が同じ畳みを通る。
/// </para>
/// <para>
/// <b>登録簿には 1 度も触らない</b>＝読み手は偽物を差す（この試験は機体の設定を見ない）。
/// </para>
/// </summary>
public sealed class Decision140StateTests
{
    /// <summary>値を 1 つ返すだけの読み手（<c>null</c>＝欄が無い）。</summary>
    private sealed class FakeReader(int? value) : ISmartAppControlPolicyReader
    {
        public int? ReadPolicyState() => value;
    }

    /// <summary>読むたびに投げる読み手（権限・鍵の壊れ・別の版）。</summary>
    private sealed class ThrowingReader : ISmartAppControlPolicyReader
    {
        public int? ReadPolicyState() => throw new IOException("登録簿を読めません。");
    }

    [Theory]
    [InlineData(0, SmartAppControlState.Off)]
    [InlineData(1, SmartAppControlState.On)]
    [InlineData(2, SmartAppControlState.Evaluation)]
    [InlineData(4, SmartAppControlState.Off)]
    [InlineData(null, SmartAppControlState.Off)]
    public void 登録簿の数を状態に直す(int? raw, SmartAppControlState expected)
    {
        // 1 有効／2 評価中／それ以外と欄の無い機体（Win10・Home の古い版）は無効。
        Assert.Equal(expected, SmartAppControl.FromPolicyValue(raw));
        Assert.Equal(expected, SmartAppControl.Read(new FakeReader(raw)));
    }

    [Fact]
    public void 読めない機体は無効に落ちる()
    {
        // **投げない**のが錠である＝この読み取りで窓が落ちたら、SAC とは無関係の機体まで道連れになる。
        Assert.Equal(SmartAppControlState.Off, SmartAppControl.Read(new ThrowingReader()));
    }

    [Fact]
    public void 読むのは1つの鍵の1つの値だけ()
    {
        // 綴りが動いたら告知ごと死ぬので、値の名を試験に固定する（`decisions.md` 140 の逐語）。
        Assert.Equal(@"SYSTEM\CurrentControlSet\Control\CI\Policy", SmartAppControl.PolicyKeyPath);
        Assert.Equal("VerifiedAndReputablePolicyState", SmartAppControl.PolicyValueName);

        // 次の 1 手の行き先（実測＝この機体の SecHealthUI が持つ綴り）と、その逃げ道。
        Assert.Equal("windowsdefender://smartapp", SmartAppControl.SettingsUri);
        Assert.Equal("ms-settings:windowsdefender", SmartAppControl.FallbackSettingsUri);
    }

    [Fact]
    public void 本物の読み手も投げない()
    {
        // 実機の登録簿を**読むだけ**（書かない）。値が何であれ、3 値のどれかが返る。
        var state = SmartAppControl.Read();
        Assert.Contains(
            state,
            new[]
            {
                SmartAppControlState.Off, SmartAppControlState.On, SmartAppControlState.Evaluation,
            });
    }
}

/// <summary>裁定 140＝畳み（生の 1 行 → 憲章 原則 6 の 3 部品）。</summary>
public sealed class Decision140FoldTests
{
    /// <summary>司令官の画面に出た<b>逐語</b>（`decisions.md` 140）。</summary>
    private const string RawImportError =
        "ImportError: DLL load failed while importing _spline: "
        + "アプリケーション制御ポリシーによってこのファイルがブロックされました。";

    [Fact]
    public void 止められた1行を見分ける()
    {
        Assert.True(SmartAppControlNotice.Blocked(RawImportError));
        Assert.True(SmartAppControlNotice.Blocked("The app was blocked by your organization."));
        Assert.True(SmartAppControlNotice.Blocked("Smart App Control blocked this file."));

        // 大小は問わない（Windows の版で綴りが揺れる）。
        Assert.True(SmartAppControlNotice.Blocked("BLOCKED BY YOUR ORGANIZATION"));

        // 関係の無い失敗は 1 つも掬わない。
        Assert.False(SmartAppControlNotice.Blocked(null));
        Assert.False(SmartAppControlNotice.Blocked("   "));
        Assert.False(SmartAppControlNotice.Blocked("CUDA out of memory."));
        Assert.False(SmartAppControlNotice.Blocked("サーバが異常終了した（終了コード -1073741819）。"));
    }

    [Fact]
    public void 帯は3部品になり生の字を1文字も載せない()
    {
        var band = BandText.For(ServerState.Failed, RawImportError);

        // ⑴ 何が起きたか ＋ ⑵ なぜか
        Assert.Equal(BandSeverity.Bad, band.Severity);
        Assert.Equal(BandText.Failed, band.Headline);
        Assert.Contains(UiStrings.SmartAppControlFailedWhat, band.Reason!, StringComparison.Ordinal);
        Assert.Contains(UiStrings.SmartAppControlFailedWhy, band.Reason!, StringComparison.Ordinal);

        // ⑶ 次にやること（1 つだけ）
        Assert.Equal(UiStrings.SmartAppControlOpenSettingsButton, band.ActionLabel);
        Assert.Equal(BandActionKind.OpenSmartAppControl, band.Action);

        // **生の字は 1 つも出ない**（記録へ落とす＝画面には出さない）。
        Assert.DoesNotContain("ImportError", band.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("DLL load failed", band.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("アプリケーション制御ポリシー", band.Reason!, StringComparison.Ordinal);

        // 止められた檔の名は**例として**出る（Windows の通知と同じ事件だと判るため）。
        Assert.Contains("_spline.cp312-win_amd64.pyd", band.Reason!, StringComparison.Ordinal);

        // **切れとは言わない**（利用者の防護は利用者が決める）＝言うのは設定の在り処までである。
        Assert.DoesNotContain("無効にしてください", band.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("切ってください", band.Reason!, StringComparison.Ordinal);
        Assert.Contains("スマート アプリ コントロール", band.Reason!, StringComparison.Ordinal);

        // **語は本来の綴りだけ**（`decisions.md` 147）＝利用者の目に入る面に「SAC」も
        // 「Smart App Control」も出さない（標識と記録の側は据え置き＝`WordLintTests`）。
        Assert.DoesNotContain("SAC", band.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("Smart App Control", band.Reason!, StringComparison.Ordinal);

        // **可逆性には触れない**（`decisions.md` 142＝最近の Windows の更新で再びオンにできる＝
        // 「一度無効にすると戻せません」は誤り）。6 面から外した綴りを、ここが二度と戻さない。
        Assert.DoesNotContain("戻せません", band.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("入れ直すまで", band.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 声の読み込みの失敗に標識が乗っていても畳む()
    {
        // D2＝runtime.error の 1 行（v2.0.1 までは「声のデータが途中までしか…」へ落ちていた）。
        // 標識が在る回は**そちらが先**＝直し方が丸ごと違うので、間違った 1 手を出さない。
        var reason = ServerStateMachine.RuntimeLoadFailedPrefix + RawImportError;
        var band = BandText.For(ServerState.Failed, reason);

        Assert.Equal(BandActionKind.OpenSmartAppControl, band.Action);
        Assert.DoesNotContain("ImportError", band.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 使えますのまま射だけ止められた回も帯に出る()
    {
        // 参照ボイスの 1 射で初めて scipy が読まれる＝サーバは立ったままである。
        // 状態機械からは見えないので、判っている側（発話テスト）が渡す。
        var band = BandText.For(ServerState.Ready, null, new BandContext(SmartAppControlBlocked: true));

        Assert.Equal(BandSeverity.Bad, band.Severity);
        Assert.Equal(BandActionKind.OpenSmartAppControl, band.Action);

        // **「止まりました。」とは名乗らない**（是正・検分 medium 4）＝サーバは立っていて、
        // 本体からも呼べるし「デフォルト」の声は通る。`v2-copy.md` §3-1 はこの語を失敗の行だけに充てる。
        Assert.Equal(BandText.Ready, band.Headline);
        Assert.NotEqual(BandText.Failed, band.Headline);

        // 声を先に準備している最中も同じ（名乗る語は状態が決める）。
        var warming = BandText.For(
            ServerState.Warming, null, new BandContext(SmartAppControlBlocked: true));
        Assert.Equal(BandText.ReadyWarming, warming.Headline);
        Assert.Equal(BandActionKind.OpenSmartAppControl, warming.Action);

        // **本当に落ちた回は「止まりました。」のまま**（理由の 1 行に標識が載る道）。
        var failed = BandText.For(ServerState.Failed, RawImportError);
        Assert.Equal(BandText.Failed, failed.Headline);
        Assert.Equal(BandActionKind.OpenSmartAppControl, failed.Action);

        // 渡さない回は 1 行も変わらない（ふだんの緑）。
        var plain = BandText.For(ServerState.Ready, null, new BandContext());
        Assert.Equal(BandText.Ready, plain.Headline);
        Assert.Equal(BandActionKind.None, plain.Action);
    }

    /// <summary>英語の機体（是正・検分 high 1＝実測した Win32 4551 の英語の綴り）。</summary>
    private const string RawImportErrorEnglish =
        "ImportError: DLL load failed while importing _spline: "
        + "An Application Control policy has blocked this file.";

    [Fact]
    public void 英語の機体の綴りも畳む()
    {
        // **1 巡目はここが抜けていた**＝標識が「blocked by your organization」（SmartScreen ／
        // ストアの綴り）だけだったので、英語の Windows では畳みが 1 度も効かず、
        // v2.0.1 の生の ImportError がそのまま帯と発話テストへ戻っていた。
        Assert.True(SmartAppControlNotice.Blocked(RawImportErrorEnglish));

        var band = BandText.For(ServerState.Failed, RawImportErrorEnglish);
        Assert.Equal(BandActionKind.OpenSmartAppControl, band.Action);
        Assert.Contains(UiStrings.SmartAppControlFailedWhat, band.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportError", band.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("Application Control policy", band.Reason!, StringComparison.Ordinal);

        // 4551 の親類（4556／4558／4582 ほか）も同じ句で始まる＝1 つの標識で全部当たる。
        Assert.True(SmartAppControlNotice.Blocked(
            "An Application Control policy has blocked this file from running."));
    }

    [Theory]
    [InlineData(SmartAppControlState.On)]
    [InlineData(SmartAppControlState.Evaluation)]
    public void 日本語でも英語でもない機体は状態で救う(SmartAppControlState state)
    {
        // OS の文は機体の表示言語で返るので、綴りの標識では当たらない回が残る。
        // 有効・評価中の機体に限り、「DLL load failed」だけでも畳む（読めない部品＝同じ事件）。
        const string raw =
            "ImportError: DLL load failed while importing _spline: "
            + "Une stratégie de contrôle d'application a bloqué ce fichier.";

        Assert.False(SmartAppControlNotice.Blocked(raw));
        Assert.True(SmartAppControlNotice.Blocked(raw, state));

        var band = BandText.For(
            ServerState.Failed,
            ServerStateMachine.RuntimeLoadFailedPrefix + raw,
            new BandContext(SmartAppControl: state));
        Assert.Equal(BandActionKind.OpenSmartAppControl, band.Action);
        Assert.DoesNotContain("DLL load failed", band.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 無効な機体では読めない部品をSACのせいにしない()
    {
        // 欠けた DLL・壊れた一式でも同じ字は出る＝**判らない回に嘘の 1 手を出さない**。
        const string raw = "ImportError: DLL load failed while importing _spline: 指定されたモジュールが見つかりません。";

        Assert.False(SmartAppControlNotice.Blocked(raw, SmartAppControlState.Off));

        var band = BandText.For(
            ServerState.Failed,
            ServerStateMachine.RuntimeLoadFailedPrefix + raw,
            new BandContext(SmartAppControl: SmartAppControlState.Off));
        Assert.NotEqual(BandActionKind.OpenSmartAppControl, band.Action);
    }

    [Fact]
    public void 設定の詳細は状態1行だけを出す()
    {
        Assert.Equal(
            UiStrings.SmartAppControlStatusOn,
            SmartAppControlNotice.StatusLine(SmartAppControlState.On));
        Assert.Equal(
            UiStrings.SmartAppControlStatusEvaluation,
            SmartAppControlNotice.StatusLine(SmartAppControlState.Evaluation));

        // 無効な機体には**出す物が無い**（帯にも詳細にも 1 行も足さない）。
        Assert.Null(SmartAppControlNotice.StatusLine(SmartAppControlState.Off));

        // **語は Windows の設定画面の札そのもの**（`decisions.md` 147・司令官の逐語
        // 「SAC という語句ではなくスマートアプリコントロールと本来の語句で表現する。」）＝
        // 伏せ字にはしない（利用者が設定で探す語である）が、綴りは「スマート アプリ コントロール」。
        Assert.Equal("スマート アプリ コントロール: 有効", UiStrings.SmartAppControlStatusOn);
        Assert.Equal("スマート アプリ コントロール: 評価中", UiStrings.SmartAppControlStatusEvaluation);

        // **註はどちらも「ことがあります」**（`decisions.md` 145／146＝同一ハッシュが
        // block → allow に反転する＝評判が許可へ振れている間は読み込めて鳴る）。
        Assert.Contains(
            "読み込めないことがあります",
            SmartAppControlNotice.StatusNote(SmartAppControlState.On)!,
            StringComparison.Ordinal);
        Assert.Contains(
            "読み込めないことがあります",
            SmartAppControlNotice.StatusNote(SmartAppControlState.Evaluation)!,
            StringComparison.Ordinal);
        Assert.Null(SmartAppControlNotice.StatusNote(SmartAppControlState.Off));
    }

    [Fact]
    public void 設定の画面は有効な機体だけ1行を持つ()
    {
        var paths = MakePaths();
        var settings = new LauncherSettings();
        var store = new JsonSettingsStore(paths.SettingsPath);

        var on = new SettingsViewModel(
            settings, store, paths, null, new DriverRequirement(), SmartAppControlState.On);
        Assert.True(on.ShowSmartAppControl);
        Assert.Equal(UiStrings.SmartAppControlStatusOn, on.SmartAppControlText);

        var off = new SettingsViewModel(
            settings, store, paths, null, new DriverRequirement(), SmartAppControlState.Off);
        Assert.False(off.ShowSmartAppControl);
        Assert.Equal(string.Empty, off.SmartAppControlText);

        // 既定（渡さない回）＝無効＝黙る。
        var bare = new SettingsViewModel(settings, store, paths, null, new DriverRequirement());
        Assert.False(bare.ShowSmartAppControl);
    }

    private static AppPaths MakePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "ywk-140-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "app", "ledger"));
        Directory.CreateDirectory(Path.Combine(root, "data"));
        return new AppPaths(
            Path.Combine(root, "install"),
            Path.Combine(root, "app"),
            Path.Combine(root, "runtime"),
            Path.Combine(root, "data"),
            developerMode: true);
    }
}

/// <summary>裁定 140＝はじめの準備の告知（<b>ダウンロードが始まる前</b>・憲章 原則 3）。</summary>
public sealed class Decision140WizardTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-140-wiz-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 掃除は best effort
        }
    }

    [Fact]
    public async Task 有効な機体だけこれからすることに告知が出る()
    {
        var paths = MakeTree();
        var vm = NewWizard(paths, new LauncherSettings());
        vm.SmartAppControl = SmartAppControlState.On;

        // お知らせの段には出さない（同意の段に別の話を差し込まない）。
        Assert.Equal(FirstRunStep.Notices, vm.Step);
        Assert.False(vm.SmartAppControlNoticeVisible);

        vm.Accepted = true;
        await vm.NextAsync();                                   // お知らせ → これからすること

        Assert.Equal(FirstRunStep.Variant, vm.Step);
        Assert.True(vm.SmartAppControlNoticeVisible);
        Assert.Equal(UiStrings.SmartAppControlWizardNotice, vm.SmartAppControlNoticeText);

        // ⑴ 事実 ⑵ 落としても動かないことがあること ⑶ 設定の在り処、の 3 つだけ。
        Assert.Contains("スマート アプリ コントロール", vm.SmartAppControlNoticeText, StringComparison.Ordinal);
        Assert.Contains("しゃべらせられません", vm.SmartAppControlNoticeText, StringComparison.Ordinal);

        // **「読み込めません」とは言い切らない**（`decisions.md` 145／146＝評判（ISG）の揺れで
        // 同一ハッシュが block → allow に反転する＝許可の間は読み込めて鳴る）。
        Assert.Contains("読み込めないことがあります", vm.SmartAppControlNoticeText, StringComparison.Ordinal);
        Assert.DoesNotContain("読み込めないため", vm.SmartAppControlNoticeText, StringComparison.Ordinal);

        // **語は本来の綴りだけ**（`decisions.md` 147）。
        Assert.DoesNotContain("SAC", vm.SmartAppControlNoticeText, StringComparison.Ordinal);
        Assert.DoesNotContain("Smart App Control", vm.SmartAppControlNoticeText, StringComparison.Ordinal);

        // **切れとは言わない・可逆性にも触れない**（`decisions.md` 142）。
        Assert.DoesNotContain("無効にしてください", vm.SmartAppControlNoticeText, StringComparison.Ordinal);
        Assert.DoesNotContain("戻せません", vm.SmartAppControlNoticeText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SmartAppControlState.Off)]
    [InlineData(SmartAppControlState.Evaluation)]
    public async Task 無効と評価中の機体には1行も出さない(SmartAppControlState state)
    {
        var paths = MakeTree();
        var vm = NewWizard(paths, new LauncherSettings());
        vm.SmartAppControl = state;

        vm.Accepted = true;
        await vm.NextAsync();

        Assert.Equal(FirstRunStep.Variant, vm.Step);
        Assert.False(vm.SmartAppControlNoticeVisible);
        Assert.Equal(string.Empty, vm.SmartAppControlNoticeText);
    }

    [Fact]
    public void 既定は無効なので渡されない回に警告を出さない()
    {
        var paths = MakeTree();
        var vm = NewWizard(paths, new LauncherSettings());

        Assert.Equal(SmartAppControlState.Off, vm.SmartAppControl);
        Assert.False(vm.SmartAppControlNoticeVisible);
    }

    private AppPaths MakeTree()
    {
        var appDir = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(appDir, "ledger"));
        Directory.CreateDirectory(Path.Combine(appDir, "licenses"));
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        var paths = new AppPaths(
            Path.Combine(_root, "install"),
            appDir,
            Path.Combine(_root, "runtime"),
            Path.Combine(_root, "data"),
            developerMode: true);

        File.WriteAllText(paths.FirstRunNoticesPath, "通知の本文", new UTF8Encoding(false));
        File.WriteAllText(
            paths.LedgerPath("python-embed"),
            """
            {"schema":1,"name":"python-embed","items":[
              {"kind":"python-embed","name":"python-embed","url":"https://example.invalid/p.zip",
               "sha256":"aa","size":100}]}
            """,
            new UTF8Encoding(false));

        foreach (var variant in new[] { RuntimeVariants.Cu130, RuntimeVariants.Cu126, RuntimeVariants.Cpu })
        {
            File.WriteAllText(
                paths.LedgerPath(RuntimeVariants.LedgerName(variant)),
                """
                {"schema":1,"name":"runtime","items":[
                  {"kind":"wheel","name":"torch","url":"https://example.invalid/t.whl",
                   "sha256":"bb","size":1000}]}
                """,
                new UTF8Encoding(false));
        }

        return paths;
    }

    private static FirstRunViewModel NewWizard(AppPaths paths, LauncherSettings settings) =>
        new(
            paths,
            settings,
            new JsonSettingsStore(paths.SettingsPath),
            new DriverRequirement(),
            static () => null,
            static () => null,
            static _ => Task.FromResult(true));
}

/// <summary>裁定 140＝発話テストの失敗の道（<b>生の ImportError を画面に出さない</b>）。</summary>
public sealed class Decision140TryTests
{
    private const string RawImportError =
        "ImportError: DLL load failed while importing _spline: "
        + "アプリケーション制御ポリシーによってこのファイルがブロックされました。";

    [Fact]
    public async Task 止められた射は3部品になり生の字を出さない()
    {
        var wrapper = new FailingWrapper(new ErrorBody
        {
            Code = WrapperErrorCodes.ServerError,
            Message = RawImportError,
        });

        var vm = new TryViewModel(() => wrapper, new FakeAudioPlayer(), new LauncherSettings())
        {
            Input = "あ。",
        };

        string? raw = null;
        vm.SmartAppControlBlocked += (_, line) => raw = line;

        await vm.SynthesizeAsync();

        // v2.0.1 の画面（`decisions.md` 140 の逐語）＝
        // 「読み上げの用意の中で失敗しました。（ImportError: …）」は**もう出ない**。
        Assert.Equal(SmartAppControlNotice.Message(), vm.Message);
        Assert.DoesNotContain("ImportError", vm.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("アプリケーション制御ポリシー", vm.Message, StringComparison.Ordinal);
        Assert.Contains(UiStrings.SmartAppControlFailedWhat, vm.Message, StringComparison.Ordinal);

        // **生の 1 行は記録の側へ渡る**（帯の材料ではない）。
        Assert.Equal(RawImportError, raw);
    }

    [Fact]
    public async Task ほかの失敗は従来どおりの1行のまま()
    {
        var wrapper = new FailingWrapper(new ErrorBody
        {
            Code = WrapperErrorCodes.UnknownVoice,
            Message = "no such voice",
        });

        var vm = new TryViewModel(() => wrapper, new FakeAudioPlayer(), new LauncherSettings())
        {
            Input = "あ。",
        };

        var blocked = false;
        vm.SmartAppControlBlocked += (_, _) => blocked = true;

        await vm.SynthesizeAsync();

        Assert.False(blocked);
        Assert.Contains("その話者が見つかりません", vm.Message, StringComparison.Ordinal);
    }

    /// <summary>1 つの失敗を返すだけの偽の口（実 HTTP には触れない）。</summary>
    private sealed class FailingWrapper(ErrorBody error) : IWrapperClient
    {
        public Uri BaseAddress { get; } = new("http://127.0.0.1:18099/");

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

        public Task<SpeechResult> SynthesizeAsync(
            SpeechRequest request, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(new SpeechResult(false, null, null, null, 500, error, TimeSpan.Zero));

        public Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<StatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<ParamsResponse>> GetParamsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<VoicesResponse>> GetVoicesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<VoicesResponse>> GetOpenAiVoicesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<WarmupStartResult>> StartWarmupAsync(
            WarmupRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<CancelResult>> CancelWarmupAsync(
            string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<PrecomputeStartResult>> StartPrecomputeAsync(
            PrecomputeRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<CancelResult>> CancelPrecomputeAsync(
            string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WrapperResult<DropLatentResult>> DropLatentAsync(
            string voiceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// 裁定 140＝<b>止められた札の一生</b>と<b>準備運転・声の下ごしらえの失敗</b>
/// （是正・検分 medium 2／low 3／medium 8）。
/// <para>
/// 1 巡目の札は「音が出た射」と「起こし直した回」でしか下りなかったので、止めた後も落ちた後も
/// 帯が SAC の 1 行に塗り潰され、〔もう一度動かす〕が主窓から消えていた。
/// また、Radeon 機が起動時に撃つ焼きの失敗（<c>/ywk/status.warmup.error</c>）は畳みを 1 度も
/// 通らず、詳しい状態 に生の 1 行がそのまま出て、帯は緑の「使えます」のままだった。
/// </para>
/// </summary>
public sealed class Decision140BandLifeTests
{
    private const string RawImportError =
        "ImportError: DLL load failed while importing _spline: "
        + "アプリケーション制御ポリシーによってこのファイルがブロックされました。";

    private static StatusViewModel NewStatus(List<string>? log = null) =>
        new(static () => Task.CompletedTask, static () => Task.CompletedTask)
        {
            LogSink = log is null ? null : log.Add,
        };

    [Fact]
    public void 止めた回は止まっていますともう一度動かすへ戻る()
    {
        var status = NewStatus();
        status.ApplyState(ServerState.Ready, null);
        status.ApplySmartAppControlBlock(true);
        Assert.Equal(BandActionKind.OpenSmartAppControl, status.BandAction);

        // 〔いったん止める〕＝走行が終わる＝札を下ろす（直っていなければ次の射がまた立てる）。
        status.ApplyState(ServerState.Stopped, null);

        Assert.Equal(BandText.StoppedByUser, status.BandStateText);
        Assert.Equal(BandActionKind.Start, status.BandAction);
    }

    [Fact]
    public void 落ちた回は終了コードの1行が出て塗り潰されない()
    {
        var status = NewStatus();
        status.ApplyState(ServerState.Ready, null);
        status.ApplySmartAppControlBlock(true);

        // 裁定 83 の形＝子が 0xC0000005 で消える。**別の失敗は別の 1 行で告げる**（憲章 原則 6）。
        status.ApplyExitCode(-1073741819);
        status.ApplyState(ServerState.Failed, "サーバが異常終了した（終了コード -1073741819）。");

        Assert.Equal(BandText.Failed, status.BandStateText);
        Assert.NotEqual(BandActionKind.OpenSmartAppControl, status.BandAction);
        Assert.Contains("-1073741819", status.BandReasonText, StringComparison.Ordinal);
    }

    [Fact]
    public void 降格しただけの回は札を下ろさない()
    {
        // 同じ走行の続き（応答が消えた＝Listening へ戻る）＝止められた事実は変わらない。
        var status = NewStatus();
        status.ApplyState(ServerState.Ready, null);
        status.ApplySmartAppControlBlock(true);

        status.ApplyState(ServerState.Listening, null);
        status.ApplyState(ServerState.Ready, null);

        Assert.Equal(BandActionKind.OpenSmartAppControl, status.BandAction);
        Assert.Equal(BandText.Ready, status.BandStateText);
    }

    [Fact]
    public void 準備運転の失敗も畳んで帯と設定を開くへ回す()
    {
        var log = new List<string>();
        var status = NewStatus(log);
        status.ApplyState(ServerState.Ready, null);

        status.ApplyStatus(new StatusResponse
        {
            Runtime = new StatusRuntime { Loaded = true },
            Warmup = new WarmupStatus { State = "failed", Error = RawImportError },
        });

        // 詳しい状態 の 1 行は**利用者の言葉**に差し替わる（生の字は出さない）。
        Assert.Contains(UiStrings.SmartAppControlFailedWhat, status.WarmupText, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportError", status.WarmupText, StringComparison.Ordinal);
        Assert.DoesNotContain("アプリケーション制御ポリシー", status.WarmupText, StringComparison.Ordinal);

        // 帯は 3 部品と〔設定を開く〕へ（サーバは立ったままなので名乗る語は「使えます」）。
        Assert.Equal(BandText.Ready, status.BandStateText);
        Assert.Equal(BandActionKind.OpenSmartAppControl, status.BandAction);

        // 生の 1 行は**記録へ**（標本は何度も来るので、立てる回の 1 度だけ）。
        Assert.Contains(log, line => line.Contains(RawImportError, StringComparison.Ordinal));
        Assert.Single(log);
    }

    [Fact]
    public void 声の下ごしらえの失敗も同じ道を通る()
    {
        var status = NewStatus();
        status.ApplyState(ServerState.Ready, null);

        status.ApplyStatus(new StatusResponse
        {
            Runtime = new StatusRuntime { Loaded = true },
            Precompute = new PrecomputeStatus { State = "failed", Error = RawImportError },
        });

        Assert.Contains(
            UiStrings.SmartAppControlFailedWhat, status.PrecomputeText, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportError", status.PrecomputeText, StringComparison.Ordinal);
        Assert.Equal(BandActionKind.OpenSmartAppControl, status.BandAction);
    }

    [Fact]
    public void 関係の無い焼きの失敗は1行もいじらない()
    {
        var status = NewStatus();
        status.ApplyState(ServerState.Ready, null);

        status.ApplyStatus(new StatusResponse
        {
            Runtime = new StatusRuntime { Loaded = true },
            Warmup = new WarmupStatus { State = "failed", Error = "CUDA out of memory." },
        });

        Assert.Contains("CUDA out of memory.", status.WarmupText, StringComparison.Ordinal);
        Assert.Equal(BandText.Ready, status.BandStateText);
        Assert.Equal(BandActionKind.None, status.BandAction);
    }
}
