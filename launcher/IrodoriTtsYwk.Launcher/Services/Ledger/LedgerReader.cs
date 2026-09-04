using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using IrodoriTtsYwk.Launcher.Contracts;

namespace IrodoriTtsYwk.Launcher.Services.Ledger;

/// <summary>取得台帳が読めなかった／壊れていたときの理由 1 行。</summary>
public sealed class LedgerException : Exception
{
    public LedgerException(string message) : base(message)
    {
    }

    public LedgerException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>台帳の檔名（拡張子なし）。<c>ledger/README.md</c> §1 の表と 1 対 1。</summary>
public static class LedgerFileNames
{
    public const string PythonEmbed = "python-embed";

    public const string Models = "models";

    /// <summary>檔名は <c>vc_redist.json</c>（下線・台帳の <c>name</c> は <c>vc-redist</c>）。</summary>
    public const string VcRedist = "vc_redist";
}

/// <summary>
/// 取得台帳（<c>ledger/*.json</c>）を読む。<b>手で書き換えない物を読むだけ</b>で、
/// <c>build/assemble-runtime.ps1</c> と<b>同じ檔</b>を同じ規則で読む（<c>ledger/README.md</c> 冒頭）。
/// <para>
/// 継ぎ目は public コンストラクタ（台帳ディレクトリを渡す）。解析そのものは
/// <see cref="ParseRuntime"/> ほかの静的メソッド＝<b>純関数</b>なので、檔を置かずにテストできる。
/// </para>
/// </summary>
public sealed class LedgerReader
{
    /// <summary>台帳の JSON を読む設定（設定檔と同じ物を使う＝大小無視・注釈可）。</summary>
    public static readonly JsonSerializerOptions JsonOptions = JsonSettingsStore.JsonOptions;

    public LedgerReader(string ledgerDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ledgerDir);
        LedgerDir = ledgerDir;
    }

    /// <summary>配布樹の <c>ledger/</c>（<see cref="AppPaths.LedgerDir"/>）。読むだけ。</summary>
    public string LedgerDir { get; }

    /// <summary>台帳 1 檔の在り処（<c>&lt;ledger&gt;\&lt;name&gt;.json</c>）。</summary>
    public string PathOf(string ledgerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ledgerName);
        return Path.Combine(LedgerDir, ledgerName + ".json");
    }

    /// <summary>その変種の台帳が配布樹に在るか（UI の変種の選択肢を絞るのに使う）。</summary>
    public bool HasRuntime(string variant) =>
        File.Exists(PathOf(RuntimeVariants.LedgerName(variant)));

    /// <summary><c>runtime-&lt;変種&gt;.json</c>。</summary>
    public LedgerFile ReadRuntime(string variant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        var name = RuntimeVariants.LedgerName(variant);
        return ParseRuntime(ReadText(PathOf(name)), name);
    }

    /// <summary><c>python-embed.json</c>（item は 1 件だけ）。</summary>
    public LedgerFile ReadPythonEmbed()
    {
        var ledger = ParseRuntime(ReadText(PathOf(LedgerFileNames.PythonEmbed)), LedgerFileNames.PythonEmbed);
        if (ledger.PythonEmbed is null)
        {
            throw new LedgerException("python-embed.json に kind=python-embed の item が無い。");
        }

        return ledger;
    }

    /// <summary><c>models.json</c>（HF 3 リポ）。</summary>
    public ModelsLedger ReadModels() =>
        ParseModels(ReadText(PathOf(LedgerFileNames.Models)));

    /// <summary><c>vc_redist.json</c>（installer 1 件）。</summary>
    public VcRedistLedger ReadVcRedist() =>
        ParseVcRedist(ReadText(PathOf(LedgerFileNames.VcRedist)));

    // ---- 純関数（檔を置かずに試せる） ---------------------------------------

    /// <summary>
    /// <c>runtime-*.json</c>／<c>python-embed.json</c> の本文を型に落とし、<see cref="Validate"/> を通す。
    /// </summary>
    public static LedgerFile ParseRuntime(string json, string ledgerName = "")
    {
        var ledger = Deserialize<LedgerFile>(json, ledgerName);
        var problems = Validate(ledger);
        if (problems.Count > 0)
        {
            throw new LedgerException(Describe(ledgerName, problems));
        }

        return ledger;
    }

    public static ModelsLedger ParseModels(string json)
    {
        var ledger = Deserialize<ModelsLedger>(json, LedgerFileNames.Models);
        if (ledger.Repos.Count == 0)
        {
            throw new LedgerException("models.json に repos が 1 件も無い。");
        }

        var problems = new List<string>();
        foreach (var repo in ledger.Repos)
        {
            if (string.IsNullOrWhiteSpace(repo.Repo))
            {
                problems.Add("repo 名が空の項目がある");
            }

            if (string.IsNullOrWhiteSpace(repo.Revision))
            {
                // 裁定 49＝revision は必ず pin する（main を引かない）。
                problems.Add(repo.Repo + " の revision が空（pin されていない）");
            }
        }

        if (problems.Count > 0)
        {
            throw new LedgerException(Describe(LedgerFileNames.Models, problems));
        }

        return ledger;
    }

    public static VcRedistLedger ParseVcRedist(string json)
    {
        var ledger = Deserialize<VcRedistLedger>(json, LedgerFileNames.VcRedist);
        if (ledger.Installer is null)
        {
            throw new LedgerException("vc_redist.json に kind=installer の item が無い。");
        }

        if (string.IsNullOrWhiteSpace(ledger.Installer.Sha256))
        {
            throw new LedgerException("vc_redist.json の installer に sha256 が無い。");
        }

        return ledger;
    }

    /// <summary>
    /// 台帳 1 檔の検分（<b>純関数</b>）。落ちた理由を全部並べて返す（0 件＝健全）。
    /// <c>assemble-runtime.ps1</c> が throw する条件と同じ物を並べてある。
    /// </summary>
    public static IReadOnlyList<string> Validate(LedgerFile ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var problems = new List<string>();

        if (ledger.Items.Count == 0)
        {
            problems.Add("items が 0 件（生成前の雛形か、壊れた檔）");
            return problems;
        }

        if (!ledger.CountMatches)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture,
                $"count={ledger.Count} と items={ledger.Items.Count} が食い違う"));
        }

        foreach (var item in ledger.Items)
        {
            var who = string.IsNullOrWhiteSpace(item.Name) ? "(名前なし)" : item.Name;

            if (string.IsNullOrWhiteSpace(item.Url))
            {
                problems.Add(who + " に url が無い");
            }

            // sha256 が無い物は入れない（assemble-runtime.ps1 の "refusing to install it"）。
            if (string.IsNullOrWhiteSpace(item.Sha256))
            {
                problems.Add(who + " に sha256 が無い（検証できない物は入れない）");
            }

            // build/check-licenses.ps1 と同じ検分＝license 欄は空にしない。
            if (string.IsNullOrWhiteSpace(item.License))
            {
                problems.Add(who + " の license 欄が空");
            }

            switch (item.Kind)
            {
                case LedgerItemKinds.PythonEmbed:
                case LedgerItemKinds.Wheel:
                case LedgerItemKinds.Installer:
                    break;

                case LedgerItemKinds.Sdist:
                case LedgerItemKinds.Archive:
                    if (item.EffectivePackageDirs.Count == 0)
                    {
                        problems.Add(who + " に package_dir も package_dirs も無い");
                    }

                    break;

                default:
                    problems.Add(who + " の kind \"" + item.Kind + "\" が未知");
                    break;
            }
        }

        return problems;
    }

    /// <summary>
    /// 変種の台帳に埋め込み Python の item を<b>先頭に</b>足した 1 本にする（<b>純関数</b>）。
    /// <see cref="IRuntimeInstaller"/> は 1 本の <see cref="LedgerFile"/> しか受けないので、
    /// 展開の順（python-embed → wheel／sdist／archive）をここで決めてしまう。
    /// </summary>
    public static LedgerFile WithPythonEmbed(LedgerFile runtime, LedgerFile pythonEmbed)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(pythonEmbed);

        var embed = pythonEmbed.PythonEmbed
            ?? throw new LedgerException("python-embed の台帳に kind=python-embed の item が無い。");

        if (runtime.PythonEmbed is not null)
        {
            return runtime;
        }

        var items = new List<LedgerItem>(runtime.Items.Count + 1) { embed };
        items.AddRange(runtime.Items);

        // count は「変種の台帳の申告件数」なので、足した後は名乗らない（CountMatches の嘘を作らない）。
        return runtime with { Items = items, Count = null };
    }

    private static string ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path, System.Text.Encoding.UTF8);
        }
        catch (FileNotFoundException ex)
        {
            throw new LedgerException("取得台帳が見つからない：" + Path.GetFileName(path), ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new LedgerException("取得台帳の置き場が見つからない：" + Path.GetFileName(path), ex);
        }
        catch (IOException ex)
        {
            throw new LedgerException("取得台帳が開けない：" + Path.GetFileName(path), ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new LedgerException("取得台帳が読めない：" + Path.GetFileName(path), ex);
        }
    }

    private static T Deserialize<T>(string json, string ledgerName)
    {
        ArgumentNullException.ThrowIfNull(json);
        T? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new LedgerException(Label(ledgerName) + " が JSON として読めない：" + ex.Message, ex);
        }

        return parsed ?? throw new LedgerException(Label(ledgerName) + " が null だった。");
    }

    private static string Label(string ledgerName) =>
        string.IsNullOrWhiteSpace(ledgerName) ? "取得台帳" : ledgerName + ".json";

    private static string Describe(string ledgerName, IReadOnlyList<string> problems)
    {
        // 理由 1 行（先頭 3 件まで＝UI に流す都合）。
        var head = problems.Take(3).ToArray();
        var more = problems.Count > head.Length
            ? string.Create(CultureInfo.InvariantCulture, $"（他 {problems.Count - head.Length} 件）")
            : string.Empty;
        return Label(ledgerName) + " が壊れている：" + string.Join(" / ", head) + more;
    }
}
