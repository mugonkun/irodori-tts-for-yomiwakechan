using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>settings.json の読み書き（契約 ⑻）。</summary>
public interface ISettingsStore
{
    /// <summary>実際に読み書きする檔。</summary>
    string Path { get; }

    /// <summary>
    /// 読む。<b>檔が無ければ既定値・壊れていても既定値</b>（例外を投げない＝設定 1 檔で起動不能に
    /// しない）。壊れていた理由は <see cref="LastLoadError"/> に 1 行で残り、UI が告げる。
    /// </summary>
    LauncherSettings Load();

    /// <summary>原子的に書く（<c>.tmp</c> へ書いてから差し替え）。</summary>
    void Save(LauncherSettings settings);

    /// <summary>直前の <see cref="Load"/> が既定値に落ちた理由（正常なら null）。</summary>
    string? LastLoadError { get; }
}

/// <summary>
/// <see cref="ISettingsStore"/> の実装。<b>テストの継ぎ目は public コンストラクタ</b>
/// （本体 yomiwakechan2 の流儀＝InternalsVisibleTo は使わない）＝檔のパスを渡して往復を試せる。
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    /// <summary>
    /// 台帳・設定・voices.json で共有する JSON の作法＝
    /// 日本語を <c>\uXXXX</c> に潰さない（話者名が日本語＝契約 ⑷ 4-1）・読みは緩め
    /// （欄が増えても落ちない＝契約 ⑻「欄を足すのは schema を上げない」）。
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();

    public JsonSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    public string Path { get; }

    public string? LastLoadError { get; private set; }

    public LauncherSettings Load()
    {
        lock (_gate)
        {
            LastLoadError = null;
            try
            {
                if (!File.Exists(Path))
                {
                    return new LauncherSettings();
                }

                var text = File.ReadAllText(Path);
                if (string.IsNullOrWhiteSpace(text))
                {
                    LastLoadError = "settings.json が空だったので既定値で起動した。";
                    return new LauncherSettings();
                }

                var parsed = JsonSerializer.Deserialize<LauncherSettings>(text, JsonOptions);
                if (parsed is null)
                {
                    LastLoadError = "settings.json が null だったので既定値で起動した。";
                    return new LauncherSettings();
                }

                return Sanitize(parsed);
            }
            catch (JsonException ex)
            {
                LastLoadError = "settings.json が読めないので既定値で起動した：" + ex.Message;
                return new LauncherSettings();
            }
            catch (IOException ex)
            {
                LastLoadError = "settings.json が開けないので既定値で起動した：" + ex.Message;
                return new LauncherSettings();
            }
            catch (UnauthorizedAccessException ex)
            {
                LastLoadError = "settings.json が開けないので既定値で起動した：" + ex.Message;
                return new LauncherSettings();
            }
        }
    }

    public void Save(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 原子的な着地＝別檔に全部書き切ってから 1 手で差し替える（途中で落ちても
            // 元の settings.json は無傷）。取得台帳の .part→Rename と同じ作法。
            var temp = Path + ".tmp";
            var json = JsonSerializer.Serialize(Sanitize(settings), JsonOptions);
            File.WriteAllText(temp, json);
            File.Move(temp, Path, overwrite: true);
        }
    }

    /// <summary>
    /// 壊れた値を既定へ寄せる（**純関数的**＝渡された物を直して返す）。
    /// 0 のポート・負の待ち・空の変種で起動不能にならないための最後の砦。
    /// </summary>
    public static LauncherSettings Sanitize(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Schema <= 0)
        {
            settings.Schema = 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Variant))
        {
            settings.Variant = RuntimeVariants.Cu130;
        }

        if (settings.Port is < 1 or > 65535)
        {
            settings.Port = LauncherSettings.DefaultPort;
        }

        if (settings.ReadyTimeoutSeconds < 0)
        {
            settings.ReadyTimeoutSeconds = 0;
        }

        if (settings.EmptyCacheInterval < 0)
        {
            settings.EmptyCacheInterval = 0;
        }

        if (settings.UiScale is < 0.5 or > 3.0 || double.IsNaN(settings.UiScale))
        {
            settings.UiScale = 1.0;
        }

        if (settings.LastTestNumSteps is < 1 or > 120)
        {
            settings.LastTestNumSteps = 40;
        }

        settings.WarmupStages ??= [];
        settings.WarmupVoices ??= [];
        settings.VoiceOrder ??= [];

        return settings;
    }
}
