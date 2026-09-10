using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// <b>AutomationId 生存</b>（v2.0 段 A・`v2-plan.md` §0・`v2-spec.md` §7-y）。
/// <para>
/// 画面の組み替えで id を落とす事故を止める錠である。規則は 3 つ＝
/// <b>改名しない・削除しない・移すだけ</b>。新設した物には新しい id を付ける。
/// id が 1 つでも消えると無人検分（<c>probe/d-launch-probe.ps1</c>・<c>wizard-probe.ps1</c>・
/// <c>e-install-probe.ps1</c>）が落ちるので、そこまで走らせずにここで気づけるようにする。
/// </para>
/// <para>
/// <b>XAML は試験の出力へ写して読む</b>（<c>.csproj</c> の <c>None Include</c>）＝
/// <see cref="AppContext.BaseDirectory"/> から読むので、リポの路を探し歩かない。
/// </para>
/// </summary>
public sealed class AutomationIdsTests
{
    private const string Marker = "AutomationProperties.AutomationId=\"";

    /// <summary>
    /// <b>工事の前に在った 139 個から、段 C で退役した 7 個を引いた 132 個</b>
    /// （`git show HEAD:Views/*.xaml` の実測＝`launcher/README.md` §7-3 の表）。
    /// <b>この配列から勝手に 1 行を消してはならない。</b>足すのは新設した id だけである。
    /// <para>
    /// <b>退役した 7 個</b>（v2.0 段 C／A-5・`v2-copy.md` §4 末尾「入らなかったので消した 7 件」＝
    /// `v2-spec.md` §2-5 の「入らなかったので消した物（移さない）」）＝
    /// <c>SettingsShowMemoryCheck</c>（GPU メモリの欄を出す設定）・
    /// <c>SettingsWarmupStagesBox</c>／<c>SettingsWarmupVoicesBox</c>（準備運転の段と使う声）・
    /// <c>SettingsEmptyCacheBox</c>／<c>SettingsEmptyCacheNoteText</c>（GPU キャッシュの解放間隔）・
    /// <c>SettingsReadyTimeoutBox</c>／<c>SettingsReadyTimeoutNoteText</c>（準備を待つ上限）。
    /// <b>設定の詳細は 12 行以内</b>という憲章 原則 7 の錠に入り切らなかったので、
    /// <b>移さずに消した</b>（原則 7 の検分文＝「超えた分は消す（移さない）」）。
    /// <c>settings.json</c> の鍵は 1 つも減らしていない＝手で書けば従来どおり効く。
    /// <b>台本（<c>probe/d-launch-probe.ps1</c>・<c>probe/wizard-probe.ps1</c>）は
    /// この 7 つを 1 度も押さず 1 度も読んでいない</b>ので、台本の書き替えは要らなかった。
    /// </para>
    /// </summary>
    private static readonly string[] Existing =
    [
        "AboutDisclaimerText",
        "AboutEthicsText",
        "AboutLicenseList",
        "AboutLicensesDirText",
        "AboutNoticesPathText",
        "AboutTitleText",
        "AboutUpstreamText",
        "AboutVersionText",
        "AboutView",
        "AboutWatermarkText",
        "FirstRunAcceptCheck",
        "FirstRunBackButton",
        "FirstRunCancelButton",
        "FirstRunDoneTrailList",
        "FirstRunDriverText",
        "FirstRunMessageText",
        "FirstRunNextButton",
        "FirstRunNoticesBox",
        "FirstRunProgressBar",
        "FirstRunProgressText",
        "FirstRunSizeText",
        "FirstRunStepNumber",
        "FirstRunStepTitle",
        "FirstRunTrailList",
        "FirstRunVariantBlockText",
        "FirstRunVariantCombo",
        "FirstRunVariantNameText",
        "FirstRunVariantNoteText",
        "FirstRunWizard",
        "MainEndpointText",
        "MainFirstRunButton",
        "MainHeaderText",
        "MainStartButton",
        "MainStateText",
        "MainStopButton",
        "MainTabs",
        "MainVersionText",
        "MainWindow",
        "SettingsAppDirText",
        "SettingsApplyButton",
        "SettingsAutoStartCheck",
        "SettingsCacheMessageText",
        "SettingsClearCacheButton",
        "SettingsDataDirText",
        "SettingsDriverText",
        "SettingsGpuCombo",
        "SettingsGpuMessageText",
        "SettingsMessageText",
        "SettingsModelDirText",
        "SettingsPortBox",
        "SettingsPortNoteText",
        "SettingsPrecisionCombo",
        "SettingsPrecisionNoteText",
        "SettingsPrecomputeCheck",
        "SettingsPrecomputeNoteText",
        "SettingsRefreshGpuButton",
        "SettingsRevertButton",
        "SettingsRuntimeRootText",
        "SettingsVariantBlockText",
        "SettingsVariantCombo",
        "SettingsVariantNameText",
        "SettingsView",
        "SettingsVoicesDirText",
        "SettingsWarmupCheck",
        "StatusAcquireButton",
        "StatusAcquisitionText",
        "StatusDeviceText",
        "StatusEndpointText",
        "StatusGpuMismatchText",
        "StatusGpuText",
        "StatusLatentCacheText",
        "StatusLogBox",
        "StatusMemoryPanel",
        "StatusMemoryText",
        "StatusNoticesText",
        "StatusPrecomputeText",
        "StatusReasonText",
        "StatusRebuildCancelButton",
        "StatusRebuildProgressBar",
        "StatusRebuildProgressText",
        "StatusRebuildRuntimeButton",
        "StatusRebuildRuntimeText",
        "StatusSettingsPendingText",
        "StatusStateText",
        "StatusUpstreamMismatchText",
        "StatusVariantText",
        "StatusView",
        "StatusVoiceMemoryText",
        "StatusWarmupText",
        "TabAbout",
        "TabSettings",
        "TabStatus",
        "TabTry",
        "TabVoices",
        "TryCaptionBox",
        "TryCfgCaptionBox",
        "TryCfgSpeakerBox",
        "TryCfgTextBox",
        "TryConcurrencyText",
        "TryInputBox",
        "TryInputLengthText",
        "TryMessageText",
        "TryReplayButton",
        "TryResultText",
        "TrySaveButton",
        "TrySeedBox",
        "TrySpeedBox",
        "TryStepsBox",
        "TryStepsPreset10",
        "TryStepsPreset40",
        "TryStopButton",
        "TrySynthesizeButton",
        "TryView",
        "TryVoiceCombo",
        "VoicesAddButton",
        "VoicesBrowseButton",
        "VoicesEthicsText",
        "VoicesGrid",
        "VoicesMessageText",
        "VoicesNewCaptionBox",
        "VoicesNewNameBox",
        "VoicesPrecomputeButton",
        "VoicesPreviewBlockedText",
        "VoicesPreviewButton",
        "VoicesRefreshButton",
        "VoicesRemoveBlockedText",
        "VoicesRemoveButton",
        "VoicesRestorePresetsButton",
        "VoicesSelectedMemoryText",
        "VoicesSourcePathBox",
        "VoicesStopPreviewButton",
        "VoicesView",
    ];

    [Fact]
    public void 生きている132個のidは1つも消えていない()
    {
        var found = AllIds();
        var missing = Existing.Where(id => !found.Contains(id)).ToArray();

        Assert.Equal(132, Existing.Length);
        Assert.True(
            missing.Length == 0,
            "消えた AutomationId：" + string.Join("・", missing));
    }

    [Fact]
    public void idは1つも重複していない()
    {
        var all = ReadAll();
        var duplicated = all
            .GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key + "（" + string.Join("・", group.Select(e => e.File)) + "）")
            .ToArray();

        Assert.True(
            duplicated.Length == 0,
            "2 度綴られた AutomationId：" + string.Join("・", duplicated));
    }

    [Fact]
    public void 段Aで新設したidが揃っている()
    {
        var found = AllIds();
        string[] added =
        [
            "MainBandStateText", "MainBandReasonText", "MainBandActionButton", "MainBandHostText",
            "MainOpenLogButton", "MainGearButton", "MainDetailOverlay", "MainDetailTabs",
            "MainDetailCloseButton", "SettingsAdvancedExpander",
        ];

        var missing = added.Where(id => !found.Contains(id)).ToArray();
        Assert.True(missing.Length == 0, "新設できていない AutomationId：" + string.Join("・", missing));
    }

    /// <summary>
    /// <b>段 B（はじめの準備）で新設した id</b>（`v2-spec.md` §3・`v2-plan.md` 段 B）。
    /// <para>
    /// <b>退役させた id は 1 つも無い</b>＝選ぶ段は消えたが、<c>FirstRunVariantCombo</c> ほか 6 つは
    /// <c>FirstRunAdvancedExpander</c> の中へ<b>要素ごと</b>移した（規則＝改名しない・削除しない・移すだけ）。
    /// 台本は畳みを開く 1 手（<c>Open-YwkFold</c>）で従来どおり触れる。
    /// </para>
    /// </summary>
    [Fact]
    public void 段Bで新設したidが揃っている()
    {
        var found = AllIds();
        string[] added =
        [
            "FirstRunNoticesSummaryText", "FirstRunNoticesFullButton", "FirstRunNoticesExpander",
            "FirstRunDecisionText", "FirstRunPlanText", "FirstRunPhaseText",
            "FirstRunProgressDetailText", "FirstRunUacNoticeText", "FirstRunDoneText",
            "FirstRunAdvancedExpander",

            // 是正・検分で足した 2 つ＝⑴ W1（全文が読めない回に要約とチェックの代わりに出す 1 行）
            // ⑵ 段 2 の「なぜ落ちるのか」（憲章 原則 3 の 3 つ目・お知らせを飛ばした回に要る）。
            "FirstRunNoticesUnreadableText", "FirstRunWhyText",
        ];

        var missing = added.Where(id => !found.Contains(id)).ToArray();
        Assert.True(missing.Length == 0, "新設できていない AutomationId：" + string.Join("・", missing));
    }

    /// <summary>
    /// <b>段 C（文言と畳み込み）で新設した id</b>（`v2-spec.md` §2-2／§2-4／§2-5／§2-6）。
    /// <para>
    /// 4 枚の畳みと、そこから出た釦である。<b>改名・削除は 1 つも無い</b>＝
    /// 畳みへ入れた要素は id ごと移しただけで、台本は <c>Open-YwkFold</c> の 1 手で従来どおり触れる。
    /// </para>
    /// </summary>
    [Fact]
    public void 段Cで新設したidが揃っている()
    {
        var found = AllIds();
        string[] added =
        [
            "StatusAdvancedExpander", "TryAdvancedExpander", "VoicesAdvancedExpander",
            "AboutAdvancedExpander", "AboutGuideButton", "AboutSaveLogButton",
            "SettingsOpenLogButton",

            // 仕様の表に id が無いが**押す物・読む値**なので付けた 4 つ（報告に列挙した）＝
            // 発話テストの詳細の内訳・声の一覧の下の薄字・〔フォルダを開く〕2 つ。
            "TryDetailText", "VoicesCountText",
            "SettingsOpenVoicesDirButton", "SettingsOpenDataDirButton",

            // 是正・段 C の検分＝〔報告用のログを保存〕は〔ログを開く〕と同じ動きをしていた。
            // 記録を 1 檔にまとめ、その路を 1 行で出し、脇に〔フォルダを開く〕を置く
            // （`v2-copy.md` §8・`v2-spec.md` §2-6）。
            "AboutSavedLogText", "AboutOpenReportFolderButton",
        ];

        var missing = added.Where(id => !found.Contains(id)).ToArray();
        Assert.True(missing.Length == 0, "新設できていない AutomationId：" + string.Join("・", missing));
    }

    /// <summary>
    /// <b>段 F（名札・文書・インストーラ）で新設した id</b>（`v2-copy.md` §4 の 6 行目）。
    /// <para>
    /// 1 つだけである＝設定 › 詳細 の 6 行目「更新のとき、新しくなった分だけ取り直す」。
    /// 段 C はこの 1 行を持たずに 7 行で置き、段 E が差分の道具（<c>RuntimeDiff</c>・
    /// <c>ModelDiff</c>・<c>PresetSync</c>）を入れ、段 F がその 2 つを結んで <b>8 行</b>にした。
    /// <b>退役させた id は 1 つも無い。</b>
    /// </para>
    /// </summary>
    [Fact]
    public void 段Fで新設したidが揃っている()
    {
        var found = AllIds();
        string[] added = ["SettingsDifferentialUpdateCheck"];

        var missing = added.Where(id => !found.Contains(id)).ToArray();
        Assert.True(missing.Length == 0, "新設できていない AutomationId：" + string.Join("・", missing));
    }

    /// <summary>
    /// <b>退役させた id は本当に画面から消えているか</b>（段 C）＝
    /// 「消した」と帳面に書いておいて XAML に残っている、を止める錠である。
    /// </summary>
    [Fact]
    public void 段Cで退役させた7個は画面に残っていない()
    {
        var found = AllIds();
        string[] retired =
        [
            "SettingsShowMemoryCheck", "SettingsWarmupStagesBox", "SettingsWarmupVoicesBox",
            "SettingsEmptyCacheBox", "SettingsEmptyCacheNoteText",
            "SettingsReadyTimeoutBox", "SettingsReadyTimeoutNoteText",
        ];

        var alive = retired.Where(found.Contains).ToArray();
        Assert.True(alive.Length == 0, "退役したはずの AutomationId が残っている：" + string.Join("・", alive));
    }

    /// <summary>
    /// <b>台本が触る id は、台本の手で本当に触れるか</b>（是正・段 C の検分）。
    /// <para>
    /// 上の錠は<b>在る・重ならない</b>しか見ていない。段 C は 13 個の要素を
    /// <c>StatusAdvancedExpander</c>（<c>IsExpanded="False"</c>）の中へ移したが、
    /// <b>畳んだ WPF の <c>Expander</c> の中は UI Automation から見えない</b>ので、
    /// 台本が畳みを開かないまま <c>Get-TextById</c> を撃つと <c>$null</c> が返る＝
    /// id は 1 つも消えていないのに歩けない。この錠はその形を捕まえる。
    /// </para>
    /// <para>
    /// 見方＝⑴ 台本（<c>probe/*.ps1</c>）の <c>-Id '…'</c> を全部拾う
    /// ⑵ 画面の檔で、その id が <c>IsExpanded="False"</c> の <c>Expander</c> の中に居るかを
    /// 素直なタグの入れ子で数える ⑶ 居るなら、台本がその畳みの id を
    /// <c>Open-YwkFold</c>（か、それを包む助手）で開いているかを見る。
    /// <b>助手の名は台本の中で解決する</b>＝<c>Open-YwkRunControls</c> のように
    /// <c>Open-YwkFold -Id '…'</c> を内に持つ関数も「開いている」と数える。
    /// </para>
    /// </summary>
    [Fact]
    public void 台本が触る畳みの中のidは台本が開けるようになっている()
    {
        var scripts = ProbeScripts();
        var targeted = scripts.SelectMany(script => ProbeIds(script.Text))
            .ToHashSet(StringComparer.Ordinal);

        // 台本が開いている畳み＝どれかの台本に `Open-YwkFold -Id '<畳み>'` が在る物。
        var opened = scripts
            .SelectMany(script => OpenedFolds(script.Text))
            .ToHashSet(StringComparer.Ordinal);

        // **空振りで通らない**＝写しが届かない日や拾い方を壊した日に「0 件」と嘘をつかせない。
        var all = AllIds();
        Assert.True(
            targeted.Count(all.Contains) >= 30,
            "台本から拾えた画面の id が少なすぎる：" + targeted.Count(all.Contains));
        Assert.NotEmpty(FoldedIds());

        var unreachable = new List<string>();
        foreach (var (file, id, fold) in FoldedIds())
        {
            if (targeted.Contains(id) && !opened.Contains(fold))
            {
                unreachable.Add(id + "（" + file + " の " + fold + " の中・台本は開いていない）");
            }
        }

        Assert.True(
            unreachable.Count == 0,
            "台本が触るのに畳みの中で届かない AutomationId：" + string.Join("・", unreachable));
    }

    /// <summary>
    /// <b>写しが本当に届いているか</b>＝ここが空だと上の 3 本が「全部在る」と嘘をつく。
    /// <para>
    /// <b>枚数ではなく名前で釘付けする</b>＝写しは <c>PreserveNewest</c> で<b>消えない</b>ので、
    /// 画面の檔を 1 枚落として別の 1 枚を足すと「7 枚」は保たれてしまい、
    /// 錠が守るはずの事故（id ごと画面を落とす）がそのまま素通りする。
    /// <c>.csproj</c> は毎度の建てで写しの棚を掃除してから写し直す（<c>CleanCopiedViews</c>）。
    /// </para>
    /// </summary>
    [Fact]
    public void 画面の檔が試験の出力に写っている()
    {
        var names = Directory.GetFiles(ViewsDirectory(), "*.xaml")
            .Select(path => Path.GetFileName(path))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray()!;

        Assert.Equal(
            [
                "AboutView.xaml", "FirstRunWizard.xaml", "MainWindow.xaml", "SettingsView.xaml",
                "StatusView.xaml", "TryView.xaml", "VoicesView.xaml",
            ],
            names);
    }

    /// <summary>試験の出力に写した無人検分の台本（<c>.csproj</c> の <c>ProbeScripts</c>）。</summary>
    private static IReadOnlyList<(string File, string Text)> ProbeScripts()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "ProbeScripts");
        Assert.True(Directory.Exists(dir), "台本が試験の出力に無い：" + dir);

        var found = Directory.GetFiles(dir, "*.ps1")
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)))
            .ToArray();

        Assert.True(found.Length >= 2, "台本の写しが足りない：" + found.Length);
        return found!;
    }

    /// <summary>
    /// 台本が名指しする id（<b>単引用符で括られた綴りを全部拾う</b>）。
    /// <para>
    /// <c>-Id 'X'</c> の形（<c>d-launch-probe.ps1</c>）と、位置引数の形
    /// （<c>wizard-probe.ps1</c> の <c>Get-TextById $wizard 'X'</c>）の<b>両方</b>を拾うため、
    /// 綴りの形では絞らない。id かどうかは呼ぶ側が画面の id と突き合わせて決める＝
    /// 関係の無い綴りは 1 つも当たらない。
    /// </para>
    /// </summary>
    private static IEnumerable<string> ProbeIds(string script)
    {
        var at = script.IndexOf('\'');
        while (at >= 0)
        {
            var end = script.IndexOf('\'', at + 1);
            if (end < 0)
            {
                yield break;
            }

            if (end > at + 1)
            {
                yield return script[(at + 1)..end];
            }

            at = script.IndexOf('\'', end + 1);
        }
    }

    /// <summary><c>Open-YwkFold … -Id 'X'</c> の X を拾う（台本が開ける畳み）。</summary>
    private static IEnumerable<string> OpenedFolds(string script)
    {
        var at = script.IndexOf("Open-YwkFold", StringComparison.Ordinal);
        while (at >= 0)
        {
            // 同じ行（呼び出し 1 本）の中の -Id だけを見る。
            var lineEnd = script.IndexOf('\n', at);
            var line = lineEnd < 0 ? script[at..] : script[at..lineEnd];
            foreach (var id in Quoted(line, "-Id "))
            {
                yield return id;
            }

            at = script.IndexOf("Open-YwkFold", at + 1, StringComparison.Ordinal);
        }
    }

    /// <summary><paramref name="marker"/> の直後の <c>'…'</c> を拾う（台本は単引用符で綴る）。</summary>
    private static IEnumerable<string> Quoted(string text, string marker)
    {
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        while (at >= 0)
        {
            var open = at + marker.Length;
            if (open < text.Length && text[open] == '\'')
            {
                var end = text.IndexOf('\'', open + 1);
                if (end > open + 1)
                {
                    yield return text[(open + 1)..end];
                }
            }

            at = text.IndexOf(marker, at + marker.Length, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <b>畳んだ <c>Expander</c> の中に居る id</b>（檔・id・その畳みの id）。
    /// <para>
    /// <c>&lt;Expander … IsExpanded="False" … AutomationId="X"&gt;</c> から
    /// 対応する <c>&lt;/Expander&gt;</c> までを 1 つの区間として数える
    /// （入れ子の <c>Expander</c> はこの樹に無いが、数えられるように積む）。
    /// </para>
    /// </summary>
    private static IReadOnlyList<(string File, string Id, string Fold)> FoldedIds()
    {
        var found = new List<(string, string, string)>();
        foreach (var path in Directory.GetFiles(ViewsDirectory(), "*.xaml"))
        {
            var name = Path.GetFileName(path);
            var xaml = File.ReadAllText(path);
            var open = new Stack<string>();
            var at = 0;
            while (at < xaml.Length)
            {
                var next = xaml.IndexOf('<', at);
                if (next < 0)
                {
                    break;
                }

                if (xaml.AsSpan(next).StartsWith("</Expander"))
                {
                    if (open.Count > 0)
                    {
                        open.Pop();
                    }

                    at = next + 1;
                    continue;
                }

                if (xaml.AsSpan(next).StartsWith("<Expander"))
                {
                    var tagEnd = xaml.IndexOf('>', next);
                    var tag = tagEnd < 0 ? xaml[next..] : xaml[next..tagEnd];
                    var collapsed = tag.Contains("IsExpanded=\"False\"", StringComparison.Ordinal);
                    var id = Extract(tag).FirstOrDefault();
                    open.Push(collapsed && id is not null ? id : string.Empty);
                    at = tagEnd < 0 ? xaml.Length : tagEnd + 1;
                    continue;
                }

                var fold = open.FirstOrDefault(entry => entry.Length > 0);
                if (fold is { Length: > 0 })
                {
                    var elementEnd = xaml.IndexOf('>', next);
                    var element = elementEnd < 0 ? xaml[next..] : xaml[next..elementEnd];
                    foreach (var id in Extract(element))
                    {
                        found.Add((name, id, fold));
                    }
                }

                at = next + 1;
            }
        }

        return found;
    }

    private static string ViewsDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Views");
        Assert.True(Directory.Exists(dir), "Views が試験の出力に無い：" + dir);
        return dir;
    }

    private static HashSet<string> AllIds() =>
        new(ReadAll().Select(entry => entry.Id), StringComparer.Ordinal);

    private static IReadOnlyList<(string File, string Id)> ReadAll()
    {
        var found = new List<(string File, string Id)>();
        foreach (var path in Directory.GetFiles(ViewsDirectory(), "*.xaml"))
        {
            var name = Path.GetFileName(path);
            foreach (var id in Extract(File.ReadAllText(path)))
            {
                found.Add((name, id));
            }
        }

        return found;
    }

    /// <summary>属性の値を拾う（<b>正規表現を使わない</b>＝素直な前方一致で足りる）。</summary>
    private static IEnumerable<string> Extract(string xaml)
    {
        var at = xaml.IndexOf(Marker, StringComparison.Ordinal);
        while (at >= 0)
        {
            var start = at + Marker.Length;
            var end = xaml.IndexOf('"', start);
            if (end < 0)
            {
                yield break;
            }

            yield return xaml[start..end];
            at = xaml.IndexOf(Marker, end, StringComparison.Ordinal);
        }
    }
}
