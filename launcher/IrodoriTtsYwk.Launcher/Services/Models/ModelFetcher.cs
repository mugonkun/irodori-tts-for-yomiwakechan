using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Ledger;

namespace IrodoriTtsYwk.Launcher.Services.Models;

/// <summary>
/// <c>ywk_fetch_models.py</c> が stdout に 1 行 1 個で吐く JSON の <c>event</c>
/// （檔の <c>emit(...)</c> の呼び口と 1 対 1）。
/// </summary>
public static class ModelFetchEvents
{
    /// <summary>取得計画（総檔数・総バイト・行き先）。最初に 1 回。</summary>
    public const string Plan = "plan";

    public const string FileStart = "file_start";

    /// <summary>1 檔の途中経過（<c>overall_downloaded</c>／<c>overall_bytes</c> が全体の分子分母）。</summary>
    public const string Progress = "progress";

    public const string FileDone = "file_done";

    /// <summary>検証に落ちた（sha256 か git blob sha1）。</summary>
    public const string FileBad = "file_bad";

    /// <summary><c>--check-only</c> でキャッシュに無かった。</summary>
    public const string FileMissing = "file_missing";

    /// <summary><c>refs/main</c> を pin した commit に向けた（<c>HF_HUB_OFFLINE=1</c> の要）。</summary>
    public const string RefsMain = "refs_main";

    public const string RefsBad = "refs_bad";

    public const string Error = "error";

    /// <summary>最後に 1 回（<c>ok</c> が真偽）。</summary>
    public const string Done = "done";
}

/// <summary>
/// stdout の JSON 行 1 本（<b>解析は純関数</b>）。
/// 欄は檔の <c>emit(...)</c> の合併で、無い欄は null（上流が欄を足しても落ちない）。
/// </summary>
public sealed record ModelFetchEvent
{
    public string Event { get; init; } = string.Empty;

    public string? Repo { get; init; }

    public string? Path { get; init; }

    public string? Revision { get; init; }

    public string? Stage { get; init; }

    public string? Message { get; init; }

    public string? Reason { get; init; }

    /// <summary><c>file_done</c> の <c>verified</c>（<c>sha256</c> か <c>git-blob-sha1</c>）。</summary>
    public string? Verified { get; init; }

    /// <summary>この檔の長さ（<c>bytes</c>）。</summary>
    public long? Bytes { get; init; }

    /// <summary>この檔の済みバイト（<c>downloaded</c>）。</summary>
    public long? Downloaded { get; init; }

    public long? OverallDownloaded { get; init; }

    public long? OverallBytes { get; init; }

    /// <summary><c>plan</c> の <c>total_files</c>。</summary>
    public int? TotalFiles { get; init; }

    /// <summary><c>done</c> の <c>files</c>（<c>plan</c> の <c>total_files</c> とは別の欄）。</summary>
    public int? Files { get; init; }

    public long? TotalBytes { get; init; }

    public bool? Ok { get; init; }

    public string? Dest { get; init; }

    public IReadOnlyList<string> Failures { get; init; } = [];

    /// <summary>全体の進み具合（0〜1）。分母が無ければ null。</summary>
    public double? Fraction
    {
        get
        {
            var total = OverallBytes ?? TotalBytes;
            var done = OverallDownloaded;
            return total is > 0 && done is not null
                ? Math.Clamp((double)done.Value / total.Value, 0, 1)
                : null;
        }
    }

    /// <summary>UI に出す 1 行（<b>純関数</b>）。</summary>
    public string ForUi() => Event switch
    {
        ModelFetchEvents.Plan => string.Create(CultureInfo.InvariantCulture,
            $"モデル {TotalFiles ?? 0} 檔（{FetchPlanner.FormatBytes(TotalBytes ?? 0)}）を取得する。"),
        ModelFetchEvents.FileStart => (Repo ?? "?") + " / " + (Path ?? "?"),
        ModelFetchEvents.FileDone => (Path ?? "?") + " 済み",
        ModelFetchEvents.FileBad => (Path ?? "?") + " が検証に落ちた：" + (Reason ?? "理由不明"),
        ModelFetchEvents.FileMissing => (Path ?? "?") + " がキャッシュに無い",
        ModelFetchEvents.RefsMain => (Repo ?? "?") + " の refs/main を pin した commit に向けた",
        ModelFetchEvents.RefsBad => (Repo ?? "?") + " の refs/main が合わない",
        ModelFetchEvents.Error => "モデル取得が失敗した（" + (Stage ?? "?") + "）：" + (Message ?? string.Empty),
        ModelFetchEvents.Done => Ok == true ? "モデルの取得が終わった。" : "モデルの取得が失敗した。",
        _ => Event,
    };

    /// <summary>
    /// stdout の 1 行を解析する（<b>純関数</b>）。JSON でない行・<c>event</c> の無い行は null。
    /// </summary>
    public static ModelFetchEvent? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var text = line.Trim();
        if (text.Length < 2 || text[0] != '{')
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("event", out var name)
                || name.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var failures = new List<string>();
            if (root.TryGetProperty("failures", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in list.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        failures.Add(entry.GetString() ?? string.Empty);
                    }
                }
            }

            return new ModelFetchEvent
            {
                Event = name.GetString() ?? string.Empty,
                Repo = Text(root, "repo"),
                Path = Text(root, "path"),
                Revision = Text(root, "revision"),
                Stage = Text(root, "stage"),
                Message = Text(root, "message"),
                Reason = Text(root, "reason"),
                Verified = Text(root, "verified"),
                Bytes = Number(root, "bytes"),
                Downloaded = Number(root, "downloaded"),
                OverallDownloaded = Number(root, "overall_downloaded"),
                OverallBytes = Number(root, "overall_bytes"),
                TotalFiles = Number(root, "total_files") is { } planned ? (int)planned : null,
                Files = Number(root, "files") is { } finished ? (int)finished : null,
                TotalBytes = Number(root, "total_bytes"),
                Ok = root.TryGetProperty("ok", out var ok)
                     && ok.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? ok.GetBoolean()
                    : null,
                Dest = Text(root, "dest"),
                Failures = failures,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : null;
}

/// <summary><c>ywk_fetch_models.py</c> の終了コード（檔の docstring の逐語）。</summary>
public static class ModelFetchExitCodes
{
    /// <summary>全部在って検証も通った。</summary>
    public const int Ok = 0;

    /// <summary>検証に落ちた（sha256／git blob sha1／refs/main）。</summary>
    public const int VerificationFailed = 2;

    /// <summary>取得か通信の失敗（<c>huggingface_hub</c> が import できない場合も含む）。</summary>
    public const int DownloadFailed = 3;

    /// <summary>引数か台帳が悪い。</summary>
    public const int BadArguments = 4;

    /// <summary>UI に出す理由 1 行（<b>純関数</b>）。</summary>
    public static string Describe(int exitCode, string? lastError) => exitCode switch
    {
        Ok => "モデルの取得が終わった。",
        VerificationFailed => "モデルの検証に落ちた（取り直しが要る）。" + Tail(lastError),
        DownloadFailed => "モデルを取得できなかった（通信か保存先を確かめてください）。" + Tail(lastError),
        BadArguments => "モデル取得の呼び出しが誤っている（台帳か保存先の指定）。" + Tail(lastError),
        _ => string.Create(CultureInfo.InvariantCulture,
            $"モデル取得が終了コード {exitCode} で止まった。") + Tail(lastError),
    };

    private static string Tail(string? message) =>
        string.IsNullOrWhiteSpace(message) ? string.Empty : " " + message.Trim();
}

/// <summary>モデル取得 1 回の結末。</summary>
/// <param name="Ok">終了コード 0 か。</param>
/// <param name="ExitCode">子プロセスの終了コード。</param>
/// <param name="Files">取れた／確かめた檔の数（<c>done</c> の <c>files</c>）。</param>
/// <param name="Bytes">同じくバイト。</param>
/// <param name="Failures"><c>done</c> が並べた失敗（<c>--check-only</c> の欠落も含む）。</param>
/// <param name="Message">理由 1 行。</param>
public sealed record ModelFetchResult(
    bool Ok,
    int ExitCode,
    int Files,
    long Bytes,
    IReadOnlyList<string> Failures,
    string Message);

/// <summary>
/// 変種の <c>python.exe</c> で <c>&lt;app&gt;/server/ywk_fetch_models.py</c> を子プロセス実行し、
/// stdout の JSON 行を進捗に流す（設計書 §1 の <c>Models/ModelFetcher.cs</c>）。
/// <para>
/// <b>ランチャは HF を直接叩かない</b>＝<c>huggingface_hub</c> のキャッシュの形を再実装しないため。
/// pin した revision の全檔を取り、<b><c>refs/main</c> を書く</b>（これが無いと
/// <c>HF_HUB_OFFLINE=1</c> で上流の読み込みが落ちる＝裁定 49）。
/// </para>
/// <para>継ぎ目は public コンストラクタ（<c>python.exe</c>・配布樹・台帳・行き先を渡す）。</para>
/// </summary>
public sealed class ModelFetcher
{
    /// <summary>配布樹の <c>server/</c> に在る取得台本。</summary>
    public const string ScriptName = "ywk_fetch_models.py";

    private readonly string _pythonExe;
    private readonly string _serverDir;
    private readonly string _ledgerPath;
    private readonly string _destination;

    /// <param name="pythonExe">変種の <c>python.exe</c>。</param>
    /// <param name="serverDir">配布樹の <c>server/</c>（台本の在り処・作業ディレクトリ）。</param>
    /// <param name="ledgerPath"><c>ledger/models.json</c>。</param>
    /// <param name="destination"><c>HF_HOME</c>（<see cref="AppPaths.HfHomeDir"/>）。</param>
    public ModelFetcher(string pythonExe, string serverDir, string ledgerPath, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pythonExe);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(ledgerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        _pythonExe = pythonExe;
        _serverDir = serverDir;
        _ledgerPath = ledgerPath;
        _destination = destination;
    }

    /// <summary>台本に渡す引数（<b>純関数</b>・テストで釘付けする）。</summary>
    public static IReadOnlyList<string> BuildArguments(
        string scriptPath, string ledgerPath, string destination, bool checkOnly, bool force)
    {
        var arguments = new List<string>
        {
            scriptPath,
            "--ledger", ledgerPath,
            "--dest", destination,
        };

        if (checkOnly)
        {
            arguments.Add("--check-only");
        }

        if (force)
        {
            arguments.Add("--force");
        }

        return arguments;
    }

    /// <summary>
    /// 子に載せる env（<b>純関数</b>）。
    /// <b><c>HF_HUB_OFFLINE</c> は 0</b>＝ここは取りに行く経路であり、
    /// 起動時（<see cref="ServerEnvironment"/>）の 1 とは逆（親から継いだ 1 を消す）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildEnvironment(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HF_HOME"] = destination,
            ["HF_HUB_OFFLINE"] = "0",
            ["PYTHONUNBUFFERED"] = "1",
            ["PYTHONIOENCODING"] = "utf-8",
            // 埋め込み Python は ._pth で path を決める。PYTHONPATH は無視されるが、
            // 親から継いだ物が紛れないように空にしておく（launcher/README §4 落とし穴 1）。
            ["PYTHONPATH"] = string.Empty,
        };
    }

    /// <summary>
    /// 取得（<paramref name="checkOnly"/> で「在るかどうかだけ見る」）。
    /// <para>
    /// <b>注意</b>＝<c>--check-only</c> は pin した revision の<b>全檔</b>を見るので、
    /// 上流サーバが必要分だけ落とした既存キャッシュに対しては <c>.gitattributes</c> 等を
    /// 「無い」と報告する。異常ではない（裁定 49・<c>ledger/README.md</c> §6）。
    /// </para>
    /// </summary>
    public async Task<ModelFetchResult> FetchAsync(
        IProgress<ModelFetchEvent>? progress,
        CancellationToken cancellationToken,
        bool checkOnly = false,
        bool force = false)
    {
        var script = System.IO.Path.Combine(_serverDir, ScriptName);
        if (!File.Exists(script))
        {
            return new ModelFetchResult(
                false, ModelFetchExitCodes.BadArguments, 0, 0, [],
                "配布樹に " + ScriptName + " が無い。");
        }

        if (!File.Exists(_pythonExe))
        {
            return new ModelFetchResult(
                false, ModelFetchExitCodes.BadArguments, 0, 0, [],
                "変種の python.exe が無い（先に実行系の取得を済ませること）。");
        }

        Directory.CreateDirectory(_destination);

        var info = new ProcessStartInfo(_pythonExe)
        {
            WorkingDirectory = _serverDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in BuildArguments(script, _ledgerPath, _destination, checkOnly, force))
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in BuildEnvironment(_destination))
        {
            info.Environment[name] = value;
        }

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };

        var failures = new List<string>();
        var files = 0;
        var bytes = 0L;
        string? lastError = null;
        var stderrTail = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            var parsed = ModelFetchEvent.Parse(e.Data);
            if (parsed is null)
            {
                return;
            }

            switch (parsed.Event)
            {
                case ModelFetchEvents.Error:
                    lastError = parsed.Message;
                    break;
                case ModelFetchEvents.Done:
                    files = parsed.Files ?? parsed.TotalFiles ?? 0;
                    bytes = parsed.Bytes ?? 0;
                    failures.AddRange(parsed.Failures);
                    break;
                case ModelFetchEvents.FileBad:
                    failures.Add((parsed.Path ?? "?") + " " + (parsed.Reason ?? string.Empty));
                    break;
            }

            progress?.Report(parsed);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            // 人間向けの注記だけが来る（進捗は stdout）。末尾だけ残す。
            if (stderrTail.Length > 4000)
            {
                stderrTail.Clear();
            }

            stderrTail.AppendLine(e.Data);
        };

        if (!process.Start())
        {
            return new ModelFetchResult(
                false, ModelFetchExitCodes.DownloadFailed, 0, 0, [], "モデル取得の子プロセスを起こせなかった。");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }

        var exitCode = process.ExitCode;
        if (lastError is null && exitCode != ModelFetchExitCodes.Ok && stderrTail.Length > 0)
        {
            lastError = LastLine(stderrTail.ToString());
        }

        return new ModelFetchResult(
            exitCode == ModelFetchExitCodes.Ok,
            exitCode,
            files,
            bytes,
            failures,
            ModelFetchExitCodes.Describe(exitCode, lastError));
    }

    /// <summary>stderr の末尾 1 行（理由に添える）。</summary>
    private static string? LastLine(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 0 ? lines[^1] : null;
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
