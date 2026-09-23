using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Services.Voices;

/// <summary>
/// 受け箱に置かれた参照ボイスを話者にする（裁定 160・契約 ⑷ 4-5）。
/// <para>
/// 司令官の言葉（2026-09-24）＝「参照ボイスを本体から受け付ける口も新設できるかな。」
/// </para>
/// <para>
/// <b>口と持ち主を割ってある</b>＝wrapper は本体から受けた音声を
/// <c>voices\inbox\&lt;id&gt;.&lt;拡張子&gt;</c>＋同名 <c>.json</c>（素性と state）に置くだけで、
/// <c>voices.json</c>・<c>voices.ywk.json</c>・<c>refs\</c> には 1 行も書かない。
/// 話者を増やすのは<b>このアプリ</b>である（契約 ⑷ 4-3 の所有者はそのまま）。
/// だから ⑴ 名前をもう 1 度検分し ⑵ 画面の「声を追加する」と<b>同じ路</b>
/// （<see cref="IVoiceStore.AddVoice"/> → <see cref="IVoicesJsonWriter.Write"/>）を通し
/// ⑶ 終わったら state を進めて音声ファイルを消す。
/// </para>
/// <para>
/// <b>1 件ずつ</b>＝<see cref="ProcessAll"/> は待ち行列を頭から順に片付ける。
/// 画面の「声を追加する」と同時に走らせない錠は<b>呼ぶ側</b>（<see cref="VoicesViewModel"/>）が
/// 持つ＝追加の 1 手と同じ 1 つの錠を分け合う。
/// </para>
/// <para>
/// <b>テストの継ぎ目は public コンストラクタ</b>＝場所と台帳を渡して実際の檔で往復を試せる。
/// </para>
/// </summary>
public interface IVoiceInbox
{
    /// <summary>受け箱の場所（<c>&lt;voices_dir&gt;\inbox</c>）。</summary>
    string InboxDir { get; }

    /// <summary>まだ登録していない受け箱の中身（<c>queued</c> だけ・受けた順）。</summary>
    IReadOnlyList<VoiceInboxItem> ListQueued();

    /// <summary>待ち行列を頭から 1 件ずつ片付ける（戻り＝1 件につき 1 つの結果）。</summary>
    IReadOnlyList<VoiceInboxResult> ProcessAll();

    /// <summary>
    /// 済んだ記録（<c>done</c>／<c>failed</c>）のうち古い物を捨てる。戻り＝捨てた件数。
    /// </summary>
    int Sweep(TimeSpan keep, DateTimeOffset now);

    /// <summary>
    /// 前回の途中終い（<c>registering</c> のまま残った記録）を拾い直す＝起動時に 1 度だけ・<see cref="Sweep"/> より先。
    /// 音声が残っていれば <c>queued</c> に戻して次の標本で登録し直し、無ければ <c>failed</c> にする
    /// （検分の是正 3＝<c>registering</c> は誰も拾わない終点で、受け箱の件数が永久に正のままだった）。戻り＝拾った件数。
    /// </summary>
    int Reclaim();
}

/// <summary>受け箱の 1 件（sidecar の写し）。</summary>
/// <param name="Id">受け箱の id（小文字 16 進 24 字）。</param>
/// <param name="DisplayName">付けたい名（日本語可）。</param>
/// <param name="Caption">キャプション既定（無ければ null）。</param>
/// <param name="Format"><c>wav</c>／<c>mp3</c>／<c>flac</c>／<c>ogg</c>／<c>opus</c>。</param>
/// <param name="Client">渡してきたアプリの名（無ければ null）。</param>
/// <param name="State"><c>queued</c>／<c>registering</c>／<c>done</c>／<c>failed</c>。</param>
/// <param name="Error">失敗の理由 1 行（無ければ null）。</param>
/// <param name="ReceivedAt">受けた時刻（読めなければ null）。</param>
public sealed record VoiceInboxItem(
    string Id,
    string DisplayName,
    string? Caption,
    string Format,
    string? Client,
    string State,
    string? Error,
    DateTimeOffset? ReceivedAt);

/// <summary>受け箱 1 件の結末（画面の 1 行と、下ごしらえの引き金になる）。</summary>
/// <param name="Id">受け箱の id。</param>
/// <param name="DisplayName">付けた（付けようとした）名。</param>
/// <param name="Client">渡してきたアプリの名乗り（無ければ null）。</param>
/// <param name="Ok">話者になったか。</param>
/// <param name="Error">駄目だった理由 1 行（<paramref name="Ok"/> が真なら null）。</param>
public sealed record VoiceInboxResult(
    string Id, string DisplayName, string? Client, bool Ok, string? Error);

/// <inheritdoc cref="IVoiceInbox"/>
public sealed class VoiceInbox : IVoiceInbox
{
    /// <summary><c>voices_dir</c> の下に切る受け箱の名（<c>server/ywk_server.py</c> の <c>INBOX_SUBDIR</c>）。</summary>
    public const string InboxSubdirectory = "inbox";

    /// <summary>済んだ記録を残す日数（これより古い <c>done</c>／<c>failed</c> は起動時に捨てる）。</summary>
    public static readonly TimeSpan KeepFinished = TimeSpan.FromDays(7);

    /// <summary>
    /// 本体（読み分けちゃん2）の名乗り＝本体側のアダプタが <c>client</c> に入れてくる値。
    /// <b>綴りを揃えるのはここ 1 箇所</b>（画面に出す呼び名は
    /// <see cref="UiStrings.VoiceIntakeHontaiName"/>）。
    /// </summary>
    public const string HontaiClientId = "yomiwakechan2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IVoiceStore _store;
    private readonly IVoicesJsonWriter _writer;
    private readonly string _voicesJsonPath;

    /// <summary>実機用（<see cref="AppPaths"/> から場所を採る）。</summary>
    public VoiceInbox(AppPaths paths, IVoiceStore store, IVoicesJsonWriter writer)
        : this(
            (paths ?? throw new ArgumentNullException(nameof(paths))).VoicesDir,
            paths.VoicesJsonPath,
            store,
            writer)
    {
    }

    /// <summary>テスト用＝場所を直に渡す。</summary>
    /// <param name="voicesDir"><c>IRODORI_VOICES_DIR</c>（受け箱はこの直下）。</param>
    /// <param name="voicesJsonPath">上流が読む別名表の路。</param>
    /// <param name="store">話者台帳（画面の追加と<b>同じ</b>実装を渡す）。</param>
    /// <param name="writer">別名表の書き手（同上）。</param>
    public VoiceInbox(
        string voicesDir, string voicesJsonPath, IVoiceStore store, IVoicesJsonWriter writer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(voicesDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(voicesJsonPath);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(writer);

        InboxDir = Path.Combine(voicesDir, InboxSubdirectory);
        _voicesJsonPath = voicesJsonPath;
        _store = store;
        _writer = writer;
    }

    /// <inheritdoc/>
    public string InboxDir { get; }

    /// <inheritdoc/>
    public IReadOnlyList<VoiceInboxItem> ListQueued() =>
        [.. ReadAll().Where(static item => item.State == StateQueued)
                    .OrderBy(static item => item.ReceivedAt ?? DateTimeOffset.MaxValue)
                    .ThenBy(static item => item.Id, StringComparer.Ordinal)];

    /// <summary><c>queued</c>／<c>registering</c> の件数（<c>/ywk/status.voices.inbox</c> の相方）。</summary>
    public int PendingCount() =>
        ReadAll().Count(static item => item.State is StateQueued or StateRegistering);

    /// <inheritdoc/>
    public IReadOnlyList<VoiceInboxResult> ProcessAll()
    {
        var results = new List<VoiceInboxResult>();
        foreach (var item in ListQueued())
        {
            results.Add(Process(item));
        }

        return results;
    }

    /// <summary>
    /// 1 件を話者にする（<b>公開しているのは試験の継ぎ目のため</b>＝1 件だけ撃てる）。
    /// <para>
    /// 順は 5 手＝⑴ state を <c>registering</c> にして他の手が同じ 1 件を拾わないようにし
    /// ⑵ 名前をもう 1 度検分し（受けてから登録までの間に同じ名が増えていることがある）
    /// ⑶ 画面の追加と同じ路で写して台帳に足し ⑷ 別名表を書き直し
    /// ⑸ state を <c>done</c> にして受け箱の音声を消す。
    /// どこで転んでも <c>failed</c>＋理由 1 行で終え、音声は同じく消す
    /// （残すと次の起動で同じ物にもう 1 度挑んで同じ所で転ぶ）。
    /// </para>
    /// </summary>
    public VoiceInboxResult Process(VoiceInboxItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var audio = AudioPath(item);
        try
        {
            Advance(item, StateRegistering, null);

            if (!File.Exists(audio))
            {
                return Fail(item, audio, UiStrings.VoiceIntakeNoAudio);
            }

            var known = _store.Load().Voices.Keys;
            var nameError = VoiceNameValidator.Validate(item.DisplayName, known);
            if (nameError is not null)
            {
                return Fail(item, audio, nameError);
            }

            var table = _store.AddVoice(item.DisplayName.Trim(), audio, Note(item));
            _writer.Write(_voicesJsonPath, table);

            Advance(item, StateDone, null);
            TryDelete(audio);
            return new VoiceInboxResult(
                item.Id, item.DisplayName.Trim(), item.Client, true, null);
        }
        catch (ArgumentException ex)
        {
            return Fail(item, audio, OneLine(ex.Message));
        }
        catch (IOException ex)
        {
            return Fail(item, audio, OneLine(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Fail(item, audio, OneLine(ex.Message));
        }
    }

    /// <inheritdoc/>
    public int Sweep(TimeSpan keep, DateTimeOffset now)
    {
        var swept = 0;
        foreach (var item in ReadAll())
        {
            if (item.State is not (StateDone or StateFailed))
            {
                continue;
            }

            // 時刻が読めない記録は**残さない**＝受けた日が判らない物を永久に持ち歩かない。
            if (item.ReceivedAt is DateTimeOffset received && now - received < keep)
            {
                continue;
            }

            TryDelete(AudioPath(item));
            if (TryDelete(SidecarPath(item.Id)))
            {
                swept++;
            }
        }

        // 検分の是正 3＝sidecar の無い音声（sidecar の書き込みに失敗した回・半端な .tmp）も古ければ捨てる。
        if (Directory.Exists(InboxDir))
        {
            var known = new HashSet<string>(ReadAll().Select(static item => item.Id), StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(InboxDir))
            {
                var name = Path.GetFileName(path);
                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || known.Contains(name.Split('.')[0]))
                {
                    continue;
                }

                DateTimeOffset written;
                try
                {
                    written = File.GetLastWriteTimeUtc(path);
                }
                catch (IOException)
                {
                    continue;
                }

                if (now - written < keep)
                {
                    continue;
                }

                if (TryDelete(path))
                {
                    swept++;
                }
            }
        }

        return swept;
    }

    /// <inheritdoc/>
    public int Reclaim()
    {
        var reclaimed = 0;
        foreach (var item in ReadAll())
        {
            if (item.State != StateRegistering)
            {
                continue;
            }

            if (File.Exists(AudioPath(item)))
            {
                Advance(item, StateQueued, null);
            }
            else
            {
                Advance(item, StateFailed, UiStrings.VoiceIntakeNoAudio);
            }

            reclaimed++;
        }

        return reclaimed;
    }

    /// <summary>受け箱の音声の路（<c>&lt;inbox&gt;\&lt;id&gt;.&lt;拡張子&gt;</c>）。</summary>
    public string AudioPath(VoiceInboxItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Path.Combine(InboxDir, item.Id + "." + item.Format);
    }

    /// <summary>
    /// 台帳に残す但し書き（<b>純関数</b>）。
    /// <para>
    /// <c>VoiceEntry</c> には「どのアプリから来たか」を書く欄が無く、<c>origin</c> は
    /// <c>preset</c>／<c>user</c> の 2 値である（<c>Contracts/IVoiceStore.cs</c>）。
    /// 新しい欄を足すと台帳の形が変わり、wrapper と旧い版の読みに掛かるので、
    /// <b>キャプションの前置き</b>として残す＝渡してきたアプリの名が判り、
    /// キャプションが空なら但し書きだけになる。
    /// </para>
    /// </summary>
    public static string? Note(VoiceInboxItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var caption = item.Caption?.Trim();
        var client = item.Client?.Trim();
        if (string.IsNullOrEmpty(client))
        {
            return string.IsNullOrEmpty(caption) ? null : caption;
        }

        var head = "［" + client + UiStrings.VoiceIntakeNoteTail;
        return string.IsNullOrEmpty(caption) ? head : head + " " + caption;
    }

    // ---- 受け箱の檔 --------------------------------------------------------

    private const string StateQueued = "queued";
    private const string StateRegistering = "registering";
    private const string StateDone = "done";
    private const string StateFailed = "failed";

    private string SidecarPath(string id) => Path.Combine(InboxDir, id + ".json");

    private IReadOnlyList<VoiceInboxItem> ReadAll()
    {
        if (!Directory.Exists(InboxDir))
        {
            return [];
        }

        var items = new List<VoiceInboxItem>();
        string[] files;
        try
        {
            files = Directory.GetFiles(InboxDir, "*.json");
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        Array.Sort(files, StringComparer.Ordinal);
        foreach (var path in files)
        {
            if (Read(path) is VoiceInboxItem item)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static VoiceInboxItem? Read(string path)
    {
        VoiceInboxSidecar? sidecar;
        try
        {
            sidecar = JsonSerializer.Deserialize<VoiceInboxSidecar>(
                File.ReadAllText(path, Encoding.UTF8), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (sidecar is null)
        {
            return null;
        }

        var id = (sidecar.Id ?? Path.GetFileNameWithoutExtension(path)).Trim();
        if (!LooksLikeId(id) || string.IsNullOrWhiteSpace(sidecar.DisplayName))
        {
            return null;
        }

        var format = (sidecar.Format ?? string.Empty).Trim().ToLowerInvariant();
        if (Array.IndexOf(VoiceIds.WavExtensions, "." + format) < 0)
        {
            return null;
        }

        DateTimeOffset? received = null;
        if (DateTimeOffset.TryParse(
                sidecar.ReceivedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            received = parsed;
        }

        return new VoiceInboxItem(
            id,
            sidecar.DisplayName!,
            string.IsNullOrWhiteSpace(sidecar.Caption) ? null : sidecar.Caption!.Trim(),
            format,
            string.IsNullOrWhiteSpace(sidecar.Client) ? null : sidecar.Client!.Trim(),
            (sidecar.State ?? string.Empty).Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(sidecar.Error) ? null : sidecar.Error!.Trim(),
            received);
    }

    /// <summary>受け箱の id の形（<c>os.urandom(12).hex()</c>＝小文字 16 進 24 字）。</summary>
    public static bool LooksLikeId(string? id)
    {
        if (id is null || id.Length != 24)
        {
            return false;
        }

        foreach (var c in id)
        {
            if (!char.IsAsciiHexDigitLower(c))
            {
                return false;
            }
        }

        return true;
    }

    private VoiceInboxResult Fail(VoiceInboxItem item, string audio, string reason)
    {
        Advance(item, StateFailed, reason);
        TryDelete(audio);
        return new VoiceInboxResult(
            item.Id, item.DisplayName.Trim(), item.Client, false, reason);
    }

    /// <summary>sidecar を原子的に書き直す（temp → <c>File.Move(overwrite)</c>）。</summary>
    private void Advance(VoiceInboxItem item, string state, string? error)
    {
        var path = SidecarPath(item.Id);
        VoiceInboxSidecar sidecar;
        try
        {
            sidecar = JsonSerializer.Deserialize<VoiceInboxSidecar>(
                          File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                      ?? new VoiceInboxSidecar();
        }
        catch (JsonException)
        {
            sidecar = new VoiceInboxSidecar();
        }
        catch (IOException)
        {
            return;  // 掴まれている＝次の標本でもう 1 度
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        sidecar = sidecar with
        {
            Id = item.Id,
            DisplayName = sidecar.DisplayName ?? item.DisplayName,
            Format = sidecar.Format ?? item.Format,
            State = state,
            Error = error,
        };

        try
        {
            Directory.CreateDirectory(InboxDir);
            var temp = path + ".tmp";
            File.WriteAllText(
                temp, JsonSerializer.Serialize(sidecar, JsonOptions), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>理由は<b>1 行・路を出さない</b>（契約 ⑶ 3-3 と同じ作法）。</summary>
    private static string OneLine(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return UiStrings.VoiceIntakeUnknownReason;
        }

        var folded = string.Join(" ", message.Split(
            ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var cut = folded.IndexOf(":\\", StringComparison.Ordinal);
        if (cut > 0)
        {
            folded = folded[..Math.Max(0, cut - 1)].TrimEnd();
        }

        return string.IsNullOrWhiteSpace(folded) ? UiStrings.VoiceIntakeUnknownReason : folded;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
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

/// <summary>受け箱の sidecar（<c>server/ywk_server.py</c> が書く形）。</summary>
internal sealed record VoiceInboxSidecar
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("display_name")] public string? DisplayName { get; init; }

    [JsonPropertyName("caption")] public string? Caption { get; init; }

    [JsonPropertyName("format")] public string? Format { get; init; }

    [JsonPropertyName("client")] public string? Client { get; init; }

    [JsonPropertyName("received_at")] public string? ReceivedAt { get; init; }

    [JsonPropertyName("state")] public string? State { get; init; }

    [JsonPropertyName("error")] public string? Error { get; init; }

    [JsonPropertyName("bytes")] public long? Bytes { get; init; }
}
