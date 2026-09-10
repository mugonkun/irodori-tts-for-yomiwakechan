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
    /// <b>工事の前に在った 139 個</b>（`git show HEAD:Views/*.xaml` の実測＝`launcher/README.md` §7-3 の表）。
    /// <b>この配列から 1 行でも消してはならない。</b>足すのは新設した id だけである。
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
        "SettingsEmptyCacheBox",
        "SettingsEmptyCacheNoteText",
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
        "SettingsReadyTimeoutBox",
        "SettingsReadyTimeoutNoteText",
        "SettingsRefreshGpuButton",
        "SettingsRevertButton",
        "SettingsRuntimeRootText",
        "SettingsShowMemoryCheck",
        "SettingsVariantBlockText",
        "SettingsVariantCombo",
        "SettingsVariantNameText",
        "SettingsView",
        "SettingsVoicesDirText",
        "SettingsWarmupCheck",
        "SettingsWarmupStagesBox",
        "SettingsWarmupVoicesBox",
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
    public void 既存の139個のidは1つも消えていない()
    {
        var found = AllIds();
        var missing = Existing.Where(id => !found.Contains(id)).ToArray();

        Assert.Equal(139, Existing.Length);
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
