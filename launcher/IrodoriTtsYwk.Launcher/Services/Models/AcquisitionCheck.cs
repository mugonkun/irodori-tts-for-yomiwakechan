using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Ledger;

namespace IrodoriTtsYwk.Launcher.Services.Models;

/// <summary>
/// 初回取得が<b>本当に済んでいるか</b>（裁定 121）＝実行系とモデルの在否 1 組。
/// </summary>
/// <param name="RuntimeReady">
/// 変種の <c>python.exe</c> が在る（<see cref="AppPaths.ResolvePythonExe"/> が非 null）。
/// </param>
/// <param name="ModelsReady">
/// pin した revision の檔が <c>HF_HOME/hub</c> に揃っている。
/// <b>台帳が読めない配布樹では真</b>＝判断の材料が無いのに急かさない。
/// </param>
/// <param name="MissingModelFiles">
/// 足りない檔（<c>&lt;repo&gt;:&lt;path&gt;</c> の形・<c>refs/main</c> も 1 件として並ぶ）。
/// </param>
/// <param name="Summary">
/// 不足の 1 行（揃っていれば空）。ウィザードの註と状態帯の理由に<b>そのまま</b>入る。
/// </param>
public sealed record AcquisitionState(
    bool RuntimeReady,
    bool ModelsReady,
    IReadOnlyList<string> MissingModelFiles,
    string Summary)
{
    /// <summary>実行系もモデルも揃っている。</summary>
    public bool Ready => RuntimeReady && ModelsReady;
}

/// <summary>
/// 初回取得の済み具合を<b>檔の在否だけ</b>で見る（裁定 121＝司令官の報告
/// 「初回起動から、モデルダウンロードへの導線を追加してほしい。単に、サーバー起動失敗となるから。」）。
/// <para>
/// <b>要る理由</b>＝<c>firstRunCompleted</c> は<b>設定の札</b>でしかない。古いデータ樹・
/// 消したモデル置き場・掃除したキャッシュでは、札が立ったまま実体が無いので
/// 主窓はウィザードを出さず、「サーバ起動」が事前検査で断られるか、起こした個体が
/// <c>HF_HUB_OFFLINE=1</c> でモデルを読めずに落ちる（<c>モデルの読込に失敗＝…</c>）。
/// どちらも<b>取得への導線が無い</b>行き止まりだった。
/// </para>
/// <para>
/// <b>安い検査だけ</b>＝檔の在否（と台帳の長さ）しか見ない。通信もしないし python も起こさない
/// （窓が開く前に走るので、ここで待たせてはいけない）。
/// </para>
/// <para>
/// <b>在否の規則は取得台本と同じ物を使う</b>＝<c>server/ywk_fetch_models.py</c> の
/// <c>_resolve_cached</c>（<c>&lt;hub&gt;/models--&lt;org&gt;--&lt;name&gt;/snapshots/&lt;revision&gt;/&lt;path&gt;</c>）と
/// <c>refs_main_path</c>（裁定 49＝これが無いと上流の読み込みが offline で落ちる）。
/// 台本の <c>--check-only</c> と同じ物差しなので、<b>上流サーバが必要分だけ落とした
/// 既存のキャッシュ</b>（<c>.gitattributes</c> 等が無い）は「不足」と読む＝
/// 異常ではない（<c>ledger/README.md</c> §6）が、その機体ではウィザードが出る。
/// 出ても主窓は使えるまま（閉じれば状態帯の 1 行が残るだけ）である。
/// </para>
/// </summary>
public static class AcquisitionCheck
{
    /// <summary><c>HF_HOME</c> の下でリポの部屋が並ぶ場所。</summary>
    public const string HubSubdirectory = "hub";

    /// <summary>不足を並べるときに名前を出す上限（残りは「他 N 件」）。</summary>
    public const int MaxNamedFiles = 3;

    /// <summary>取得へ導く 1 文（<b>逐語</b>＝理由 1 行の末尾に必ずこれが付く）。</summary>
    public const string Hint = "〔はじめの準備をする〕を押してください。";

    /// <summary>いまの設定の変種で見る。</summary>
    public static AcquisitionState Check(AppPaths paths, LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settings);
        return Check(paths, settings.Variant);
    }

    /// <summary>変種を名指して見る（<b>テストの継ぎ目</b>＝設定を組まずに撃てる）。</summary>
    public static AcquisitionState Check(AppPaths paths, string variant)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        var runtimeReady = paths.ResolvePythonExe(variant) is not null;
        var missing = MissingModelFiles(paths);
        var modelsReady = missing.Count == 0;

        var parts = new List<string>(2);
        if (!runtimeReady)
        {
            parts.Add("実行系（変種 " + RuntimeVariants.DisplayName(variant) + "）");
        }

        if (!modelsReady)
        {
            parts.Add("モデル " + Describe(missing));
        }

        return new AcquisitionState(
            runtimeReady, modelsReady, missing, string.Join("・", parts));
    }

    /// <summary>
    /// 足りないモデルの檔（<c>ledger/models.json</c> が読めなければ<b>空</b>＝判らないので急かさない）。
    /// </summary>
    public static IReadOnlyList<string> MissingModelFiles(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        ModelsLedger ledger;
        try
        {
            ledger = new LedgerReader(paths.LedgerDir).ReadModels();
        }
        catch (LedgerException)
        {
            return [];
        }

        var hub = Path.Combine(paths.HfHomeDir, HubSubdirectory);
        var missing = new List<string>();

        foreach (var repo in ledger.Repos)
        {
            if (string.IsNullOrWhiteSpace(repo.Repo) || string.IsNullOrWhiteSpace(repo.Revision))
            {
                continue;
            }

            foreach (var file in repo.Files)
            {
                if (string.IsNullOrWhiteSpace(file.Path))
                {
                    continue;
                }

                if (!Present(SnapshotPath(hub, repo.Repo, repo.Revision, file.Path), file.Size))
                {
                    missing.Add(repo.Repo + ":" + file.Path);
                }
            }

            // 裁定 49＝pin した sha で取っただけの樹には refs/ が無く、上流の読み込み（revision を
            // 渡さない）は HF_HUB_OFFLINE=1 で LocalEntryNotFoundError になる。台本が書く印。
            if (!RefsMainMatches(hub, repo.Repo, repo.Revision))
            {
                missing.Add(repo.Repo + ":refs/main");
            }
        }

        return missing;
    }

    /// <summary>
    /// <c>huggingface_hub</c> が置く檔の場所（<b>純関数</b>＝台本の <c>_resolve_cached</c> と同じ）。
    /// </summary>
    public static string SnapshotPath(string hubDir, string repo, string revision, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return Path.Combine(
            hubDir,
            CacheDirName(repo),
            "snapshots",
            revision,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary><c>models--&lt;org&gt;--&lt;name&gt;</c>（<b>純関数</b>）。</summary>
    public static string CacheDirName(string repo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        return "models--" + repo.Replace("/", "--", StringComparison.Ordinal);
    }

    /// <summary><c>&lt;hub&gt;/models--…/refs/main</c>（<b>純関数</b>）。</summary>
    public static string RefsMainPath(string hubDir, string repo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        return Path.Combine(hubDir, CacheDirName(repo), "refs", "main");
    }

    /// <summary>不足の並べ方（<b>純関数</b>＝先頭 3 件だけ名前を出す）。</summary>
    public static string Describe(IReadOnlyList<string> missing)
    {
        ArgumentNullException.ThrowIfNull(missing);
        if (missing.Count == 0)
        {
            return "0 檔";
        }

        var named = Math.Min(MaxNamedFiles, missing.Count);
        var head = new List<string>(named);
        for (var i = 0; i < named; i++)
        {
            head.Add(missing[i]);
        }

        var text = missing.Count.ToString(CultureInfo.InvariantCulture) + " 檔（"
                   + string.Join("・", head);
        return missing.Count > named
            ? text + " 他 " + (missing.Count - named).ToString(CultureInfo.InvariantCulture) + " 件）"
            : text + "）";
    }

    private static bool RefsMainMatches(string hubDir, string repo, string revision)
    {
        var path = RefsMainPath(hubDir, repo);
        try
        {
            return File.Exists(path)
                   && string.Equals(
                       File.ReadAllText(path).Trim(), revision.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 檔が<b>在って長さも台帳の通り</b>か（是正・検分）。
    /// <para>
    /// 在否だけを見ていたころは、途中で切れた檔・手で書き換えた檔・機体をまたいで写し損ねた
    /// キャッシュが「揃っている」と読まれ、事前検査を通った個体が上流の読み込みで落ちた＝
    /// この裁定が消そうとしている行き止まりへ戻る。台本の <c>--check-only</c> は sha256／
    /// git-blob-sha1 まで見るが、ここは<b>窓が開く前</b>に走るので長さ（stat 1 回）で止める。
    /// 台帳が長さを名乗らない檔（<c>size</c> が null か 0）は在否だけで見る。
    /// </para>
    /// </summary>
    private static bool Present(string path, long? size)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            return size is not > 0 || new FileInfo(path).Length == size.Value;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

}
