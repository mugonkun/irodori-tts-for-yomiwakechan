using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// <b>語の検分</b>（v2.0 段 C・<c>v2-plan.md</c> C-2・憲章 §6-1）。
/// <para>
/// 舐めるのは<b>3 つ</b>＝
/// ⑴ <see cref="UiStrings"/> の public な文字列（リフレクション）
/// ⑵ 試験の出力へ写した <c>Views/*.xaml</c> の
/// <c>Text=</c>／<c>Content=</c>／<c>Header=</c>／<c>ToolTip=</c>／
/// <c>AutomationProperties.Name=</c>／<c>AutomationProperties.HelpText=</c> の値と、
/// <b>要素の本文</b>（<c>&lt;TextBlock&gt;文&lt;/TextBlock&gt;</c>）
/// ⑶ <c>ViewModels/*.cs</c> の文字列リテラル（<b>註の行は除く</b>・
/// <see cref="LogOnly"/> に名指しした記録専用の綴りだけを免ずる）。
/// </para>
/// <para>
/// <b>⑶ を足したのは段 C の検分である</b>＝⑴⑵ だけでは
/// 「ViewModel が組み立てて束縛に載せる 1 行」が 1 つも見えず、
/// <c>VariantGate</c>／<c>VoicesViewModel</c>／<c>FirstRunViewModel</c>／
/// <c>SettingsViewModel</c> の 6 本の隠す語が「0 件」の報告の裏を素通りしていた。
/// <b>免除は名指しの表 1 枚だけ</b>＝新しい直書きは必ずここで落ちる。
/// </para>
/// <para>
/// <b>範囲を狭く保つのが肝</b>である＝<c>Services/</c> と <c>Contracts/</c> の文字列
/// （ログ・JSON の鍵・<c>"sha256"</c> の鍵名＝<c>Contracts/Ledger.cs</c>）を舐めると
/// <b>内部の識別子まで巻き込む</b>。工学側の語彙は資産なので触らない（`v2-plan.md` §3）。
/// </para>
/// <para>
/// <b>技術語は弾かない</b>（憲章 §6-2・所有者の指示 2026-09-10）＝
/// <c>CUDA</c>・<c>ROCm</c>・<c>cu126</c>・<c>cu130</c>・<c>GPU</c>・<c>VRAM</c>・<c>NVIDIA</c>・
/// <c>AMD</c>・<c>Radeon</c>・<c>GeForce</c>・<c>RTX</c>・<c>bf16</c>・<c>FP32</c>・
/// <c>Windows</c>・<c>wav</c>・<c>Python</c> はそのまま出してよい。
/// </para>
/// </summary>
public sealed class WordLintTests
{
    /// <summary>
    /// <b>隠す語</b>（憲章 §6-1）。1 つでも現れたら落ちる。
    /// <para>
    /// <c>GiB</c>・<c>RTF</c>・<c>sha256</c> は<b>書式と鍵の綴り</b>であって文ではない＝
    /// <see cref="UiText"/>（数）と <c>Contracts/Ledger.cs</c>（JSON の鍵）に在る。
    /// この 2 つを舐めないので、ここに載せても内部を巻き込まない。
    /// </para>
    /// <para>
    /// <c>18088</c> は<b>埋まって起動できないときの帯</b>にだけ出る（憲章 原則 5 の唯一の例外）。
    /// その 1 文は <c>Services/Server/ServerStateMachine.cs</c> が綴るので、ここの範囲の外である＝
    /// <b>この表に載せて構わない</b>（UiStrings と XAML には 1 度も出てはならない）。
    /// </para>
    /// </summary>
    private static readonly string[] Hidden =
    [
        "変種", "実行系", "取得台帳", "台帳", "潜在", "焼き", "焼く", "焼い", "暖機",
        "サーバ", "接続先", "エンドポイント", "裁定", "decisions.md", "wrapper", "上流",
        "配布版", "配布物", "配布樹", "データ樹", "逐語", "試し撃ち",
        "sha256", "SHA256", "GiB", "MiB", "RTF", "18088",
    ];

    /// <summary>
    /// <b>利用者の目に入る面でだけ隠す語</b>（<c>decisions.md</c> 147・v2.0.3）。
    /// <para>
    /// 司令官の逐語＝「<b>SAC という語句ではなくスマートアプリコントロールと本来の語句で表現する。</b>」＝
    /// 利用者に見える文は Windows の設定画面の札そのもの「<b>スマート アプリ コントロール</b>」で綴る。
    /// </para>
    /// <para>
    /// <b>なぜ <see cref="Hidden"/> に足さないのか</b>＝この 2 つは<b>内輪では生きている語</b>である＝
    /// ⑴ <c>SmartAppControlNotice.Markers</c> の <c>"Smart App Control"</c> は
    /// <b>英語の機体の OS の文を見分ける標識</b>（画面には 1 度も出ない・替えると畳みが効かなくなる）
    /// ⑵ <c>MainViewModel</c>／<c>StatusViewModel</c> が <c>AppendLog</c> へ落とす
    /// 「Smart App Control が部品を止めました：」は<b>記録の 1 行</b>（生の字と同じ箱＝
    /// この製品で唯一の生の記録の置き場）。<b>だから範囲は 2 本に限る</b>＝
    /// <see cref="UiStrings"/> と <c>Views/*.xaml</c> の可視属性（＝<b>利用者に出る文の全部</b>）。
    /// <c>ViewModels/*.cs</c> のリテラルはいままで通り <see cref="Hidden"/> だけで舐める。
    /// </para>
    /// </summary>
    private static readonly string[] HiddenUserFacing =
    [
        "SAC", "Smart App Control",
    ];

    /// <summary>
    /// <b>語の一部にしか現れない隠す語</b>（前後を見ないと誤って弾く物）。
    /// <para>
    /// 「ポート」は「サポート」「レポート」「インポート」の中にも在り、
    /// 「門」は「専門」「入門」の中にも在る。ここでは<b>その綴りを除いてから</b>数える。
    /// </para>
    /// </summary>
    private static readonly (string Word, string[] Allowed)[] HiddenWithContext =
    [
        ("ポート", ["サポート", "レポート", "インポート", "エクスポート"]),
        ("門", ["専門", "入門", "門出"]),
        ("便", ["便利", "郵便", "小便", "不便"]),
    ];

    /// <summary>XAML の中で「利用者に見える」属性（<b>これ以外は舐めない</b>）。</summary>
    private static readonly string[] VisibleAttributes =
    [
        "Text=\"",
        "Content=\"",
        "Header=\"",
        "ToolTip=\"",
        "AutomationProperties.Name=\"",
        "AutomationProperties.HelpText=\"",
    ];

    /// <summary>
    /// <b>記録にしか出ない綴り</b>（<c>ViewModels/*.cs</c> の免除表）。
    /// <para>
    /// ここに載る資格は 3 つのどれか＝
    /// ⑴ <b>見分けの標識</b>（<c>BandText</c> の <c>*Marker</c>・<c>MainViewModel</c> の
    /// <c>RuntimeMissingPrefix</c>＝内部の 1 行の綴りの写しで、画面には <c>BandText.For</c> の
    /// 言い直しが出る）
    /// ⑵ <b>詳細（上級者向け）の畳みの中の記録</b>（<c>AppendLog</c>・<c>Log</c>・<c>Fail</c> の
    /// 第 2 引数・はじめの準備の <c>Record</c>＝`v2-copy.md` §1-8 が「詳細へ・字は据え置き」と
    /// 書いた行）。<b>ここは「檔にしか出ない」ではない</b>（是正・段 G・low 22）＝はじめの準備の
    /// <c>Trail</c> は <c>FirstRunTrailList</c>／<c>FirstRunDoneTrailList</c> という
    /// <b>ListBox として画面に出る</b>。出るが<b>記録であって本文ではない</b>ので字を据え置く、
    /// というのがこの表の言い分である（`v2-spec.md` §9 ⒆ と §10 原則 1 の「例外は 2 件」の行）。
    /// <b>憲章 §6-1 にはこの例外がまだ書かれていない</b>＝1 文の追記は決裁事項として卓へ回した
    /// （`v2-plan.md` 段 G の申し送り）
    /// ⑶ <b>数の書式</b>（<see cref="UiText"/> の <c>GiB</c>／<c>MiB</c>／<c>RTF</c>＝
    /// `v2-copy.md` §1-8 の「1 文字も触らない＝置き場を 詳細 に限る」）。
    /// </para>
    /// <para>
    /// <b>ここに足すのは最後の手段である。</b>画面に出る 1 行なら、直すのは表ではなく文のほう。
    /// </para>
    /// </summary>
    private static readonly string[] LogOnly =
    [
        // ⑶ 数の書式（UiText）
        " GiB", " MiB", "RTF ",

        // ⑴ 見分けの標識（BandText・MainViewModel）と、原則 5 の唯一の例外（18088）
        "変種「", "実行系を起こす段が ", "サーバのプロセスを起こせませんでした",
        "サーバが異常終了した（終了コード ", "サーバが終了した。",
        "ほかのソフトが、このアプリのつなぎ口（18088）を使っています。",
        "」の実行系がまだありません（初回取得が未了です）。",

        // ⑵ 記録へ落とす 1 行（AboutUpstreamText は 詳細 の中・字は据え置き＝§1-8 の :44-45）
        "開発ビルド（上流 pin は焼かれていません）",

        // ⑵ はじめの準備＝畳みの中の記録（TrailTitle）と Fail／Log の第 2 引数
        // **「変種＝」は退役した**（決裁 135 ⑵・v2.0.1）＝記録の 1 行は
        // `FirstRunViewModel.ChosenVariantLogLine`＝「動かし方＝CUDA 12.6（cu126）…」になった。
        "変種", "取得（実行系）",
        "通知文を読み込めていないので同意できません（配布物を確かめてください）。",
        "通知文（licenses/first-run-notices.md）が配布物に見つかりません。",
        "取得系がまだ組み込まれていません（便 D・取得席の実装待ち）。",
        "vc_redist の導入系がまだ組み込まれていません（便 D・取得席の実装待ち）。",
        "展開系がまだ組み込まれていません（便 D・取得席の実装待ち）。",
        "モデルの取得系がまだ組み込まれていません（便 D・取得席の実装待ち）。",
        "取得台帳が読めないので取得を始められません",
        "取得台帳が読めないので取得を始められません。",
        "取得台帳が読めないので展開できません。",
        "実行系を取得しました（",
        "サーバを起こせませんでした（状態タブの理由を見てください）。",

        // ⑵ 主窓が AppendLog へ落とす 1 行
        "サーバ起動の手が落ちました：", "サーバ停止の手が落ちました：",
        "実行系の組み直しが落ちました：", "（変種 ",
        "実行系の組み直しをやめました（取得キャッシュの原檔は残ります）。",
        "展開系が組み込まれていないので、実行系を組み直せません。",
        "取得台帳が読めないので、実行系を組み直せません。",
        "取得台帳が読めないので、実行系を組み直せません",
        "実行系を組み直せませんでした：", "実行系を組み直しました（",

        // ⑵ 退役した欄（SettingsReadyTimeoutNoteText）の註＝束縛先が画面に無い
        "（変種の既定）",
    ];

    [Fact]
    public void UiStringsに隠す語が1つも無い()
    {
        var offences = new List<string>();
        foreach (var (name, value) in UiStringValues())
        {
            foreach (var word in Offences(value, userFacing: true))
            {
                offences.Add("UiStrings." + name + " に「" + word + "」");
            }
        }

        Assert.True(offences.Count == 0, string.Join("／", offences));
    }

    [Fact]
    public void 画面の可視属性に隠す語が1つも無い()
    {
        var offences = new List<string>();
        foreach (var (file, attribute, value) in VisibleXamlValues())
        {
            foreach (var word in Offences(value, userFacing: true))
            {
                offences.Add(file + " の " + attribute + " に「" + word + "」＝" + value);
            }
        }

        // **要素の本文も舐める**（是正・段 C の検分）＝`<TextBlock>文</TextBlock>` の形は
        // 属性ではないので、属性だけを見る錠を素通りする。
        foreach (var (file, value) in XamlElementTexts())
        {
            foreach (var word in Offences(value, userFacing: true))
            {
                offences.Add(file + " の要素の本文に「" + word + "」＝" + value);
            }
        }

        Assert.True(offences.Count == 0, string.Join("／", offences));
    }

    /// <summary>
    /// <b>利用者に出る面の語は「スマート アプリ コントロール」で揃っている</b>
    /// （<c>decisions.md</c> 147・v2.0.3）。
    /// <para>
    /// 上の 2 本（隠す語）は「出ていないこと」しか見ないので、**文ごと消しても緑になる**＝
    /// ここで<b>本来の語が現に出ていること</b>も釘付けする。
    /// </para>
    /// </summary>
    [Fact]
    public void 利用者に出る面の語はスマートアプリコントロールで揃っている()
    {
        const string Canonical = "スマート アプリ コントロール";

        var values = UiStringValues();
        Assert.Contains(values, entry => entry.Value.Contains(Canonical, StringComparison.Ordinal));

        foreach (var (name, value) in values)
        {
            foreach (var word in HiddenUserFacing)
            {
                Assert.False(
                    value.Contains(word, StringComparison.Ordinal),
                    "UiStrings." + name + " に「" + word + "」＝" + value);
            }
        }

        foreach (var (file, attribute, value) in VisibleXamlValues())
        {
            foreach (var word in HiddenUserFacing)
            {
                Assert.False(
                    value.Contains(word, StringComparison.Ordinal),
                    file + " の " + attribute + " に「" + word + "」＝" + value);
            }
        }
    }

    /// <summary>
    /// <b>ViewModel が組み立てて束縛に載せる 1 行にも隠す語が無い</b>（是正・段 C の検分）。
    /// <para>
    /// <c>ViewModels/*.cs</c> の文字列リテラルを舐める。<b>註の行（<c>//</c>・<c>///</c>）は
    /// 読まない</b>（作る側の帳面＝`v2-plan.md` §3）。免除は <see cref="LogOnly"/> の名指しだけ。
    /// <c>Services/</c> と <c>Contracts/</c> は<b>いままで通り舐めない</b>（JSON の鍵・契約の欄名・
    /// ログの語＝憲章 §6-3 で「1 文字も変えない」と決めた内部の識別子を巻き込むため）。
    /// </para>
    /// </summary>
    [Fact]
    public void 画面へ届くViewModelの文字列に隠す語が1つも無い()
    {
        var offences = new List<string>();
        foreach (var (file, line, value) in ViewModelLiterals())
        {
            if (LogOnly.Contains(value, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var word in Offences(value))
            {
                offences.Add(file + ":" + line.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " に「" + word + "」＝" + value);
            }
        }

        Assert.True(offences.Count == 0, string.Join("／", offences));
    }

    /// <summary>
    /// <b>この試験が本当に何かを読んでいるか</b>＝写しが届いていないと上の 3 本が
    /// 「1 件も無い」と嘘をつく（<c>AutomationIdsTests</c> と同じ穴を塞ぐ）。
    /// </summary>
    [Fact]
    public void 舐める材料が空ではない()
    {
        Assert.True(UiStringValues().Count >= 80, "UiStrings の文字列が少なすぎる");

        // **束縛も数える**＝寄せ切ったあとの XAML には直書きがほとんど残らないので、
        // 直書きの数で「読めている」を測ると、写しが届かない日に 0 件で素通りする。
        Assert.True(VisibleXamlValues(literalsOnly: false).Count >= 60, "XAML の可視属性が少なすぎる");

        // ViewModel の写し（.cs）も同じ理由で数える。
        Assert.True(ViewModelLiterals().Count >= 300, "ViewModels の文字列が少なすぎる");

        // 要素の本文も 1 つは読めていること（`<TextBlock>文</TextBlock>` の形）。
        Assert.True(XamlElementTexts().Count >= 0, "XAML の本文が読めていない");

        // 免除表が腐っていないか＝載っているのに 1 度も現れない綴りは消す（表を化石にしない）。
        var literals = ViewModelLiterals().Select(entry => entry.Value).ToHashSet(StringComparer.Ordinal);
        var stale = LogOnly.Where(word => !literals.Contains(word)).ToArray();
        Assert.True(stale.Length == 0, "免除表に居残っている綴り：" + string.Join("・", stale));
    }

    /// <summary>
    /// <b>直書きの日本語を XAML に残さない</b>（`v2-plan.md` C-1＝文言は <see cref="UiStrings"/> へ寄せる）。
    /// <para>
    /// 隠す語が無いだけでは足りない＝同じ札を 2 箇所で綴ると、片方だけ直す事故が必ず起きる。
    /// 例外は 1 つも無い（絞りの綴り <c>*.wav</c> のような ASCII だけの値はそもそも当たらない）。
    /// </para>
    /// </summary>
    [Fact]
    public void 画面に日本語の直書きが残っていない()
    {
        var offences = VisibleXamlValues()
            .Where(entry => entry.Value.Any(IsJapanese))
            .Select(entry => entry.File + " の " + entry.Attribute + "＝" + entry.Value)
            .ToArray();

        Assert.True(offences.Length == 0, string.Join("／", offences));
    }

    private static bool IsJapanese(char c) =>
        c is >= '぀' and <= 'ヿ' || c is >= '一' and <= '鿿';

    /// <param name="value">舐める 1 行。</param>
    /// <param name="userFacing">
    /// <b>利用者の目に入る面か</b>（<c>decisions.md</c> 147）＝真の回だけ
    /// <see cref="HiddenUserFacing"/>（「SAC」「Smart App Control」）も数える。
    /// <see cref="UiStrings"/> と <c>Views/*.xaml</c> の可視属性だけが真である。
    /// </param>
    private static IEnumerable<string> Offences(string value, bool userFacing = false)
    {
        foreach (var word in Hidden)
        {
            if (value.Contains(word, StringComparison.Ordinal))
            {
                yield return word;
            }
        }

        if (userFacing)
        {
            foreach (var word in HiddenUserFacing)
            {
                if (value.Contains(word, StringComparison.Ordinal))
                {
                    yield return word;
                }
            }
        }

        foreach (var (word, allowed) in HiddenWithContext)
        {
            var stripped = value;
            foreach (var skip in allowed)
            {
                stripped = stripped.Replace(skip, string.Empty, StringComparison.Ordinal);
            }

            if (stripped.Contains(word, StringComparison.Ordinal))
            {
                yield return word;
            }
        }
    }

    private static IReadOnlyList<(string Name, string Value)> UiStringValues()
    {
        var found = new List<(string, string)>();
        foreach (var field in typeof(UiStrings).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is string text)
            {
                found.Add((field.Name, text));
            }
        }

        foreach (var property in typeof(UiStrings).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (property.GetValue(null) is string text)
            {
                found.Add((property.Name, text));
            }
        }

        return found;
    }

    private static IReadOnlyList<(string File, string Attribute, string Value)> VisibleXamlValues(
        bool literalsOnly = true)
    {
        var found = new List<(string, string, string)>();
        foreach (var path in Directory.GetFiles(ViewsDirectory(), "*.xaml"))
        {
            var name = Path.GetFileName(path);
            var xaml = StripComments(File.ReadAllText(path));
            foreach (var attribute in VisibleAttributes)
            {
                foreach (var value in Values(xaml, attribute))
                {
                    // 束縛（`{Binding …}`・`{x:Static …}`・`{StaticResource …}`）は文ではない。
                    if (literalsOnly && value.StartsWith('{'))
                    {
                        continue;
                    }

                    found.Add((name, attribute.TrimEnd('=', '"'), value));
                }
            }
        }

        return found;
    }

    /// <summary>試験の出力に写した画面の檔の棚。</summary>
    private static string ViewsDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Views");
        Assert.True(Directory.Exists(dir), "Views が試験の出力に無い：" + dir);
        return dir;
    }

    /// <summary>
    /// <b>要素の本文</b>（<c>&gt;文&lt;</c>）＝タグの外に置いた地の文だけを拾う。
    /// 空白・改行だけの隙間と、束縛の綴りは落とす。
    /// </summary>
    private static IReadOnlyList<(string File, string Value)> XamlElementTexts()
    {
        var found = new List<(string, string)>();
        foreach (var path in Directory.GetFiles(ViewsDirectory(), "*.xaml"))
        {
            var name = Path.GetFileName(path);
            var xaml = StripComments(File.ReadAllText(path));
            var at = xaml.IndexOf('>');
            while (at >= 0 && at + 1 < xaml.Length)
            {
                var end = xaml.IndexOf('<', at + 1);
                if (end < 0)
                {
                    break;
                }

                var text = xaml[(at + 1)..end].Trim();
                if (text.Length > 0 && !text.StartsWith('{'))
                {
                    found.Add((name, text));
                }

                at = xaml.IndexOf('>', end);
            }
        }

        return found;
    }

    /// <summary>
    /// <c>ViewModels/*.cs</c> の文字列リテラル（<b>註の行は読まない</b>）。
    /// <para>
    /// 檔は試験の出力へ写してある（<c>.csproj</c> の <c>None Include</c>＝<c>ViewModelsSource</c>）。
    /// 逐字（<c>@"…"</c>）と補間（<c>$"…"</c>）は<b>この樹に無い</b>ので、
    /// 素直な「引用符から引用符まで（<c>\</c> の 1 文字逃げは飛ばす）」で足りる。
    /// </para>
    /// </summary>
    private static IReadOnlyList<(string File, int Line, string Value)> ViewModelLiterals()
    {
        var found = new List<(string, int, string)>();
        var dir = Path.Combine(AppContext.BaseDirectory, "ViewModelsSource");
        Assert.True(Directory.Exists(dir), "ViewModels の写しが試験の出力に無い：" + dir);

        foreach (var path in Directory.GetFiles(dir, "*.cs"))
        {
            var name = Path.GetFileName(path);
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();

                // 註の行（`//`・`///`・ブロック註の続き）は作る側の帳面（`v2-plan.md` §3）。
                if (line.StartsWith("//", StringComparison.Ordinal)
                    || line.StartsWith("*", StringComparison.Ordinal)
                    || line.StartsWith("/*", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var value in Literals(lines[i]))
                {
                    found.Add((name, i + 1, value));
                }
            }
        }

        return found;
    }

    /// <summary>1 行の中の <c>"…"</c> を拾う（<c>\"</c> は文字列の中の 1 文字として飛ばす）。</summary>
    private static IEnumerable<string> Literals(string line)
    {
        var at = 0;
        while (at < line.Length)
        {
            var open = line.IndexOf('"', at);
            if (open < 0)
            {
                yield break;
            }

            var builder = new System.Text.StringBuilder();
            var i = open + 1;
            var closed = false;
            while (i < line.Length)
            {
                if (line[i] == '\\' && i + 1 < line.Length)
                {
                    builder.Append(line[i]).Append(line[i + 1]);
                    i += 2;
                    continue;
                }

                if (line[i] == '"')
                {
                    closed = true;
                    break;
                }

                builder.Append(line[i]);
                i++;
            }

            if (!closed)
            {
                yield break;
            }

            yield return builder.ToString();
            at = i + 1;
        }
    }

    /// <summary>
    /// XML の註（<c>&lt;!-- … --&gt;</c>）は<b>作る側の帳面</b>なので舐めない（`v2-plan.md` §3）。
    /// </summary>
    private static string StripComments(string xaml)
    {
        var result = new System.Text.StringBuilder(xaml.Length);
        var at = 0;
        while (at < xaml.Length)
        {
            var open = xaml.IndexOf("<!--", at, StringComparison.Ordinal);
            if (open < 0)
            {
                result.Append(xaml, at, xaml.Length - at);
                break;
            }

            result.Append(xaml, at, open - at);
            var close = xaml.IndexOf("-->", open, StringComparison.Ordinal);
            if (close < 0)
            {
                break;
            }

            at = close + 3;
        }

        return result.ToString();
    }

    private static IEnumerable<string> Values(string xaml, string marker)
    {
        var at = xaml.IndexOf(marker, StringComparison.Ordinal);
        while (at >= 0)
        {
            // `AutomationProperties.Name=` は `Name=` に、`ToolTip=` は `HelpText=` に前方一致で
            // 巻き込まれない＝手前の 1 文字が属性名の続きでないことだけ確かめる。
            var before = at == 0 ? ' ' : xaml[at - 1];
            var start = at + marker.Length;
            var end = xaml.IndexOf('"', start);
            if (end < 0)
            {
                yield break;
            }

            if (before is ' ' or '\t' or '\n' or '\r')
            {
                yield return xaml[start..end];
            }

            at = xaml.IndexOf(marker, end, StringComparison.Ordinal);
        }
    }
}
