using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>
/// このアプリの辞書の 1 行（<c>decisions.md</c> 166・<c>docs/design/app-dictionary.md</c>）＝
/// 本文の <see cref="From"/> をすべて <see cref="To"/> に置き換える（上から順・大小区別・部分一致＝
/// 本体 yomiwakechan2 の辞書と同じ作法）。<see cref="To"/> が空なら「読まない」。
/// </summary>
public sealed record DictionaryEntry(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string To);

/// <summary><c>dictionary.json</c> の形（欄を足すのは <c>schema</c> を上げない）。</summary>
public sealed class DictionaryFile
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;

    [JsonPropertyName("entries")] public List<DictionaryEntry> Entries { get; set; } = [];
}

/// <summary><c>&lt;データ樹&gt;\dictionary.json</c> の読み書き（wrapper が要求ごとに読む檔）。</summary>
public interface IDictionaryStore
{
    /// <summary>実際に読み書きする檔。</summary>
    string Path { get; }

    /// <summary>
    /// 読む。<b>檔が無ければ空・壊れていても空</b>（例外を投げない＝辞書 1 檔で画面を止めない）。
    /// 壊れていた理由は <see cref="LastLoadError"/> に 1 行で残り、画面が告げる。
    /// </summary>
    IReadOnlyList<DictionaryEntry> Load();

    /// <summary>原子的に書く（<c>.tmp</c> へ書いてから差し替え）。空の From は落とす。</summary>
    void Save(IReadOnlyList<DictionaryEntry> entries);

    /// <summary>直前の <see cref="Load"/> が空に落ちた理由（正常なら null）。</summary>
    string? LastLoadError { get; }
}

/// <summary>
/// <see cref="IDictionaryStore"/> の実装。JSON の作法は <see cref="JsonSettingsStore.JsonOptions"/>
/// （日本語を <c>\uXXXX</c> に潰さない・読みは緩め）。<b>テストの継ぎ目は public コンストラクタ</b>。
/// </summary>
public sealed class JsonDictionaryStore : IDictionaryStore
{
    /// <summary>檔名（wrapper の <c>ywk_dictionary.FILE_NAME</c> と同じ綴り）。</summary>
    public const string FileName = "dictionary.json";

    /// <summary>
    /// JSON の作法は <see cref="JsonSettingsStore.JsonOptions"/> そのもの（日本語は素のまま）。
    /// <b>BMP の外の字（🎉 など）は .NET の符号器がどれでも <c>🎉</c> の形に書く</b>＝
    /// JSON として正しく、wrapper（<c>ywk_dictionary.py</c>）はどちらの形も読む。手で開く人には
    /// 読みにくいが、檔は画面から書くのが本道なので許容（実測・2026-10-12）。
    /// </summary>
    public static JsonSerializerOptions JsonOptions => JsonSettingsStore.JsonOptions;

    private readonly object _gate = new();

    public JsonDictionaryStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    public string Path { get; }

    public string? LastLoadError { get; private set; }

    public IReadOnlyList<DictionaryEntry> Load()
    {
        lock (_gate)
        {
            LastLoadError = null;
            try
            {
                if (!File.Exists(Path))
                {
                    return [];
                }

                var text = File.ReadAllText(Path);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return [];
                }

                var parsed = JsonSerializer.Deserialize<DictionaryFile>(text, JsonOptions);
                return Sanitize(parsed?.Entries);
            }
            catch (JsonException ex)
            {
                LastLoadError = FileName + " が読めないので空の辞書から始めた：" + ex.Message;
                return [];
            }
            catch (IOException ex)
            {
                LastLoadError = FileName + " が開けないので空の辞書から始めた：" + ex.Message;
                return [];
            }
            catch (UnauthorizedAccessException ex)
            {
                LastLoadError = FileName + " が開けないので空の辞書から始めた：" + ex.Message;
                return [];
            }
        }
    }

    public void Save(IReadOnlyList<DictionaryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_gate)
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var file = new DictionaryFile { Entries = Sanitize(entries).ToList() };
            var temp = Path + ".tmp";
            var json = JsonSerializer.Serialize(file, JsonOptions);
            File.WriteAllText(temp, json);
            File.Move(temp, Path, overwrite: true);
        }
    }

    /// <summary>空・null の From は落とし、null の To は空にする（**純関数**）。順序は保つ。</summary>
    public static IReadOnlyList<DictionaryEntry> Sanitize(IEnumerable<DictionaryEntry?>? entries)
    {
        if (entries is null)
        {
            return [];
        }

        return entries
            .Where(static e => e is not null && !string.IsNullOrEmpty(e.From))
            .Select(static e => new DictionaryEntry(e!.From, e.To ?? string.Empty))
            .ToArray();
    }
}
