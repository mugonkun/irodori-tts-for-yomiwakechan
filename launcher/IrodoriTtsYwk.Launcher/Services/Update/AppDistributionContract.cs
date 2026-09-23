using System;
using System.Text.Json;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.ViewModels;

namespace IrodoriTtsYwk.Launcher.Services.Update;

/// <summary>
/// 着地頁に置く <c>app.json</c> の、検分を通った中身（裁定 160・2026-09-24）。
/// <para>
/// 本体（yomiwakechan2）の <c>AppDistributionManifest</c> と同型だが、<b>こちらは 2 つの版
/// （RTX（CUDA）／Radeon（ROCm））を 1 枚で配る</b>ので、インストーラの欄は
/// <c>installers.&lt;cuda|radeon&gt;</c> の下に 2 本並ぶ。ここに載るのは<b>読み手の版 1 本ぶんだけ</b>＝
/// 解釈の時点でどちらを使うかが決まっている（<see cref="AppDistributionContract.ParseManifest"/>）。
/// </para>
/// <para>
/// <see cref="Version"/> は <b>v 前置の表示形</b>（<c>v2.0.7</c>）＝
/// <see cref="AppVersion.Display"/> と<b>そのまま文字列で突き合わせる</b>ため
/// （順序比較をしない＝本体 A-3 と同型）。
/// </para>
/// </summary>
public sealed record AppDistributionManifest
{
    /// <summary>配布物の名（この製品は <c>irodori-tts-ywk</c> 固定＝取り違えの検分の鍵）。</summary>
    public required string Name { get; init; }

    /// <summary>配布されている表示版（<c>v2.0.7</c> の形）。</summary>
    public required string Version { get; init; }

    /// <summary>この版のインストーラ（setup exe）の取得先（https 限定）。</summary>
    public required string InstallerUrl { get; init; }

    /// <summary>インストーラの SHA-256（小文字 hex が正典・読みは大小を無視する）。</summary>
    public required string InstallerSha256 { get; init; }

    /// <summary>インストーラの長さ（表示と見切りの補助・0 や欠落は「判らない」）。</summary>
    public long? InstallerSizeBytes { get; init; }

    /// <summary>作成日時（RFC 3339・UTC。<b>表示のみ</b>＝値で枝を分けない）。</summary>
    public string? BuiltAt { get; init; }

    /// <summary>着地頁の URL（<b>表示のみ</b>）。</summary>
    public string? PageUrl { get; init; }

    /// <summary>利用者向けの一行（<b>表示のみ</b>）。</summary>
    public string? Note { get; init; }
}

/// <summary>
/// 着地頁の配布契約（<c>app.json</c>）の解釈（裁定 160・2026-09-24）。
/// <para>
/// <b>正本は <c>site/app.json</c></b>（<c>site/</c> は版を切るたびに <c>gh-pages</c> の根へ写す＝
/// <c>site/README.md</c>）。出先は
/// <c>https://mugonkun.github.io/irodori-tts-for-yomiwakechan/app.json</c> の 1 本だけで、
/// 同梱の写しは持たない（取れなければ「確認できなかった」の一行で足りる＝本体 A-2 と同じ判断）。
/// </para>
/// <para>
/// 前方互換の作法は本体と同じ＝<b>未知のフィールドは無視・<c>v</c> の欠落は 1 扱い・
/// 1 以外は全体を不成立</b>。失敗は例外ではなく理由の文字列で返す（呼び手が一行に畳む）。
/// </para>
/// <para>
/// <b>sha256 の字は 64 桁の hex であることだけを見る</b>＝値そのものは見ない
/// （公開前の <c>site/app.json</c> に入っている 0 が 64 個の置き札も、ここでは普通の値として通る。
/// 実物と合わなければ取得のあとの検分で落ちる＝嘘の成功を返さない）。
/// </para>
/// </summary>
public static class AppDistributionContract
{
    /// <summary>この解釈が読める schema の版数（これ以外は不成立）。</summary>
    public const int SchemaVersion = 1;

    /// <summary>配布物の名（<c>site/app.json</c> の <c>name</c> と 1 字も違えない）。</summary>
    public const string AppName = "irodori-tts-ywk";

    /// <summary>sha256 の字数（hex）。</summary>
    public const int Sha256HexLength = 64;

    /// <summary>
    /// 版の鍵（<c>installers</c> の下の名）＝<c>cuda</c>／<c>radeon</c>。
    /// 綴りの正本は <see cref="AppPaths.FlavorId"/> ただ 1 箇所である
    /// （インストーラの檔名・<c>.iss</c> の <c>/DFlavor=</c> と同じ綴り）。
    /// </summary>
    public static string FlavorKey(ReleaseFlavor flavor) => AppPaths.FlavorId(flavor);

    /// <summary>
    /// <c>app.json</c> の解釈（<b>純関数</b>＝取得も檔読みもしない）。
    /// </summary>
    /// <param name="json">取ってきた本文。</param>
    /// <param name="flavor">読み手の版（この版のインストーラが無ければ不成立）。</param>
    /// <returns>検分を通った中身か、通らなかった理由の一行。</returns>
    public static (AppDistributionManifest? Manifest, string? Error) ParseManifest(
        string json, ReleaseFlavor flavor)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, "ルートがオブジェクトではない");
            }

            if (CheckSchemaVersion(root) is string versionError)
            {
                return (null, versionError);
            }

            var name = ReadString(root, "name");
            var version = ReadString(root, "version");
            if (name is null)
            {
                return (null, "name の欠落");
            }

            if (version is null)
            {
                return (null, "version の欠落");
            }

            var key = FlavorKey(flavor);
            if (!root.TryGetProperty("installers", out var installers)
                || installers.ValueKind != JsonValueKind.Object
                || !installers.TryGetProperty(key, out var installer)
                || installer.ValueKind != JsonValueKind.Object)
            {
                return (null, "この版（" + key + "）の欄が無い");
            }

            var url = ReadString(installer, "url");
            var sha256 = ReadString(installer, "sha256");
            if (url is null)
            {
                return (null, "installers." + key + ".url の欠落");
            }

            if (sha256 is null)
            {
                return (null, "installers." + key + ".sha256 の欠落");
            }

            if (!IsSha256Hex(sha256))
            {
                return (null, "installers." + key + ".sha256 が 64 桁の hex ではない");
            }

            return (
                new AppDistributionManifest
                {
                    Name = name,
                    Version = version,
                    InstallerUrl = url,
                    InstallerSha256 = sha256,
                    InstallerSizeBytes = ReadPositiveLong(installer, "sizeBytes"),
                    BuiltAt = ReadString(root, "builtAt"),
                    PageUrl = ReadString(root, "pageUrl"),
                    Note = ReadString(root, "note"),
                },
                null);
        }
        catch (JsonException ex)
        {
            return (null, "JSON 不正: " + ex.Message);
        }
    }

    /// <summary>64 桁の hex か（<b>純関数</b>・大小は問わない）。</summary>
    public static bool IsSha256Hex(string? value)
    {
        if (value is null || value.Length != Sha256HexLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            var hex = c is >= '0' and <= '9' || c is >= 'a' and <= 'f' || c is >= 'A' and <= 'F';
            if (!hex)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary><c>v</c> の検分（欠落＝1 扱い・1 以外は不成立）。</summary>
    private static string? CheckSchemaVersion(JsonElement root)
    {
        if (!root.TryGetProperty("v", out var v) || v.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version))
        {
            return "v が整数ではない";
        }

        return version == SchemaVersion
            ? null
            : "知らない schema の版数 v=" + version.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static long? ReadPositiveLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var number))
        {
            return null;
        }

        return number > 0 ? number : null;
    }
}
