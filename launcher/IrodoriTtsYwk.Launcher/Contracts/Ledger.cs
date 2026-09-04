using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>
/// 取得台帳の <c>items[].kind</c>（<c>ledger/README.md</c> §2 の表と 1 対 1）。
/// </summary>
public static class LedgerItemKinds
{
    /// <summary>埋め込み Python の zip → 変種ディレクトリへ展開し <c>python312._pth</c> を書く。</summary>
    public const string PythonEmbed = "python-embed";

    /// <summary><c>.whl</c>（＝zip）→ <c>site-packages/</c> へまるごと展開（<c>*.dist-info</c> 込み）。</summary>
    public const string Wheel = "wheel";

    /// <summary>wheel を出していない純 Python の <c>.tar.gz</c> → <c>package_dirs</c> を写す。</summary>
    public const string Sdist = "sdist";

    /// <summary>commit 固定の GitHub ソース zip → <c>package_dir</c> を写す。</summary>
    public const string Archive = "archive";

    /// <summary>HF リポ 1 本（<c>models.json</c> のみ）。</summary>
    public const string HfRepo = "hf-repo";

    /// <summary><c>vc_redist.x64.exe</c>（<c>vc_redist.json</c> のみ）。</summary>
    public const string Installer = "installer";
}

/// <summary>
/// 取得台帳 1 檔（<c>ledger/runtime-*.json</c>・<c>ledger/python-embed.json</c>）の封筒。
/// <para>
/// <b>手で書き換えない</b>物を読むだけの型＝欄は台帳 JSON と 1 対 1 で、
/// <c>build/make-ledger.ps1</c> の生成物がそのまま入る。ランチャと
/// <c>build/assemble-runtime.ps1</c> は<b>同じ檔</b>を読む（<c>ledger/README.md</c> 冒頭）。
/// </para>
/// <para>欄が増えても落ちないように、必須は <see cref="Items"/> だけにしてある。</para>
/// </summary>
public sealed record LedgerFile
{
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;

    /// <summary>台帳の名前（例 <c>runtime-rocm-gfx1151</c>・<c>python-embed</c>）。</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("generated")] public string? Generated { get; init; }

    [JsonPropertyName("generator")] public string? Generator { get; init; }

    /// <summary>埋め込み Python の版（例 <c>3.12.10</c>）。</summary>
    [JsonPropertyName("python")] public string? Python { get; init; }

    [JsonPropertyName("python_tag")] public string? PythonTag { get; init; }

    [JsonPropertyName("platform_tag")] public string? PlatformTag { get; init; }

    /// <summary>torch を引いた index（cu130／cu126／cpu／AMD）。</summary>
    [JsonPropertyName("torch_index")] public string? TorchIndex { get; init; }

    [JsonPropertyName("uv")] public string? Uv { get; init; }

    /// <summary>この台帳が前提にしている上流 pin（配布物の版と突合する）。</summary>
    [JsonPropertyName("upstream")] public LedgerUpstream? Upstream { get; init; }

    [JsonPropertyName("resolution")] public LedgerResolution? Resolution { get; init; }

    /// <summary>台帳の申告件数（<see cref="Items"/> の数と一致しなければ台帳が壊れている）。</summary>
    [JsonPropertyName("count")] public int? Count { get; init; }

    [JsonPropertyName("items")] public IReadOnlyList<LedgerItem> Items { get; init; } = [];

    /// <summary>埋め込み Python の item（<c>python-embed.json</c> に 1 件だけ在る）。</summary>
    public LedgerItem? PythonEmbed =>
        Items.FirstOrDefault(i => i.Kind == LedgerItemKinds.PythonEmbed);

    /// <summary>取得の総バイト（進捗の分母。<c>size</c> を持たない item は 0 として数える）。</summary>
    public long TotalBytes => Items.Sum(i => i.Size ?? 0);

    /// <summary>申告件数と実件数が食い違っていないか（読み込み直後の 1 検分）。</summary>
    public bool CountMatches => Count is null || Count.Value == Items.Count;
}

/// <summary>上流 2 本の pin（<c>ledger/*.json</c> の <c>upstream</c>）。</summary>
public sealed record LedgerUpstream
{
    [JsonPropertyName("irodori-tts")] public string? IrodoriTts { get; init; }

    [JsonPropertyName("irodori-tts-server")] public string? IrodoriTtsServer { get; init; }
}

/// <summary>依存解決の記録（表示と検分にだけ使う＝取得の手順には効かない）。</summary>
public sealed record LedgerResolution
{
    [JsonPropertyName("command")] public string? Command { get; init; }

    [JsonPropertyName("overrides")] public IReadOnlyList<string> Overrides { get; init; } = [];

    [JsonPropertyName("dropped_top_level")] public IReadOnlyList<string> DroppedTopLevel { get; init; } = [];

    [JsonPropertyName("pruned_after_solve")] public IReadOnlyList<string> PrunedAfterSolve { get; init; } = [];

    [JsonPropertyName("dropped_after_solve")] public IReadOnlyList<string> DroppedAfterSolve { get; init; } = [];

    [JsonPropertyName("dropped_after_solve_reason")] public string? DroppedAfterSolveReason { get; init; }

    [JsonPropertyName("shared_with")] public string? SharedWith { get; init; }

    [JsonPropertyName("shared_items_verified")] public int? SharedItemsVerified { get; init; }

    [JsonPropertyName("log")] public string? Log { get; init; }
}

/// <summary>
/// 取得台帳の 1 件（<c>ledger/README.md</c> §2「共通の欄」＋ kind 別の欄の<b>合併</b>）。
/// <para>
/// <b>url が 2 本ありうる</b>（<see cref="FallbackUrl"/>）＝torch は index の href が
/// <c>download-r2.pytorch.org</c>（CDN の別名）で、カノニカル側が fallback に入る。
/// AMD の wheel は索引側 URL とリダイレクト先の 2 本。<c>vc_redist</c> は版固定の直リンクと
/// <c>aka.ms</c>。<b>url で失敗したら fallback_url を試す</b>（<c>ledger/README.md</c> §3）。
/// sha256 は同じ檔なのでどちらから取っても検証は同じ。
/// </para>
/// </summary>
public sealed record LedgerItem
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("version")] public string? Version { get; init; }

    [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;

    /// <summary>同じ檔を出す別ホスト（無い item のほうが多い）。</summary>
    [JsonPropertyName("fallback_url")] public string? FallbackUrl { get; init; }

    /// <summary>小文字 hex。<c>models.json</c> の非 LFS 檔だけ null（git blob sha1 で検証する）。</summary>
    [JsonPropertyName("sha256")] public string? Sha256 { get; init; }

    /// <summary>バイト。進捗の分母と、取得後の長さ検分に使う。</summary>
    [JsonPropertyName("size")] public long? Size { get; init; }

    /// <summary>落とす檔名。無ければ <see cref="EffectiveFileName"/> が URL の末尾から作る。</summary>
    [JsonPropertyName("filename")] public string? Filename { get; init; }

    /// <summary>空にしない欄（<c>build/check-licenses.ps1</c> が検分する）。「未特定」もここに入る。</summary>
    [JsonPropertyName("license")] public string? License { get; init; }

    [JsonPropertyName("license_url")] public string? LicenseUrl { get; init; }

    [JsonPropertyName("license_source")] public string? LicenseSource { get; init; }

    /// <summary>どこから取った sha256 かの逐語（手打ち禁止の証跡）。</summary>
    [JsonPropertyName("sha256_source")] public string? Sha256Source { get; init; }

    /// <summary>
    /// 利用者機に入る第三者の許諾文と観測事実（<c>licenses/first-run-notices.md</c> と突合する）。
    /// </summary>
    [JsonPropertyName("notices")] public IReadOnlyList<string> Notices { get; init; } = [];

    [JsonPropertyName("note")] public string? Note { get; init; }

    // ---- sdist / archive の展開に要る欄 -------------------------------------

    /// <summary><c>pure-python-src</c>＝展開して <c>package_dir(s)</c> を写し最小 dist-info を作る。</summary>
    [JsonPropertyName("install")] public string? Install { get; init; }

    /// <summary>tar の中の根（例 <c>argbind-0.3.9/</c>）。</summary>
    [JsonPropertyName("archive_root")] public string? ArchiveRoot { get; init; }

    /// <summary>zip の 1 段目を剥ぐか（GitHub の <c>/archive/&lt;sha&gt;.zip</c> は剥ぐ）。</summary>
    [JsonPropertyName("archive_root_strip")] public bool? ArchiveRootStrip { get; init; }

    /// <summary>写す import 可能ディレクトリ（<c>archive</c> は 1 本）。</summary>
    [JsonPropertyName("package_dir")] public string? PackageDir { get; init; }

    /// <summary>写す import 可能ディレクトリ（<c>sdist</c> は複数ありうる）。</summary>
    [JsonPropertyName("package_dirs")] public IReadOnlyList<string> PackageDirs { get; init; } = [];

    [JsonPropertyName("commit")] public string? Commit { get; init; }

    // ---- installer（vc_redist）だけの欄 -------------------------------------

    /// <summary><c>/install /quiet /norestart</c>（台帳の逐語）。</summary>
    [JsonPropertyName("silent_args")] public IReadOnlyList<string> SilentArgs { get; init; } = [];

    [JsonPropertyName("docs_url")] public string? DocsUrl { get; init; }

    /// <summary>Microsoft が URL の path に埋めている sha256。</summary>
    [JsonPropertyName("sha256_in_url")] public string? Sha256InUrl { get; init; }

    [JsonPropertyName("sha256_matches_url_segment")] public bool? Sha256MatchesUrlSegment { get; init; }

    /// <summary>zip の中の許諾檔（<c>python-embed</c> の <c>LICENSE.txt</c>）。</summary>
    [JsonPropertyName("license_file_in_archive")] public string? LicenseFileInArchive { get; init; }

    [JsonPropertyName("notes")] public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>
    /// 落とす檔名（<see cref="Filename"/>→URL の末尾の順）。
    /// <para>
    /// <b><c>build/assemble-runtime.ps1</c> の <c>Get-YwkCachedItem</c> と同じ規則</b>で作る
    /// （同じ <c>cache/</c> を台本とランチャで共有するため。ここが 1 字でも違うと、
    /// 台本が落とした檔をランチャが「無い」と見て取り直す）。
    /// GitHub の <c>/archive/&lt;sha&gt;.zip</c> は末尾が commit そのものなので、
    /// <c>&lt;name&gt;-</c> を前置して 2 本の archive が見分けられるようにする。
    /// </para>
    /// </summary>
    public string EffectiveFileName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Filename))
            {
                return Sanitize(Filename);
            }

            var path = Url;
            var hash = path.IndexOf('#', StringComparison.Ordinal);
            if (hash >= 0)
            {
                path = path[..hash];
            }

            var query = path.IndexOf('?', StringComparison.Ordinal);
            if (query >= 0)
            {
                path = path[..query];
            }

            var slash = path.LastIndexOf('/');
            var last = slash >= 0 ? path[(slash + 1)..] : path;
            if (last.Length == 0)
            {
                last = Name + ".bin";
            }

            // GitHub の commit 固定 zip は檔名が sha そのもの＝どの物か分からなくなる
            if (!string.IsNullOrWhiteSpace(Commit)
                && string.Equals(last, Commit + ".zip", StringComparison.OrdinalIgnoreCase))
            {
                last = Name + "-" + last;
            }

            return Sanitize(last);
        }
    }

    /// <summary>Windows の檔名に置けない字を <c>_</c> にする（台本の <c>-replace</c> と同じ集合）。</summary>
    private static string Sanitize(string fileName)
    {
        Span<char> buffer = stackalloc char[fileName.Length];
        for (var i = 0; i < fileName.Length; i++)
        {
            buffer[i] = fileName[i] switch
            {
                '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' => '_',
                var c => c,
            };
        }

        return new string(buffer);
    }

    /// <summary>試す順の URL（<c>url</c> → <c>fallback_url</c>）。</summary>
    public IReadOnlyList<string> Urls => string.IsNullOrWhiteSpace(FallbackUrl)
        ? [Url]
        : [Url, FallbackUrl];

    /// <summary>写す import 可能ディレクトリ（<c>package_dir</c> と <c>package_dirs</c> を畳む）。</summary>
    public IReadOnlyList<string> EffectivePackageDirs =>
        PackageDirs.Count > 0 ? PackageDirs
        : string.IsNullOrWhiteSpace(PackageDir) ? []
        : [PackageDir];
}

/// <summary>
/// <c>ledger/models.json</c>（HF 3 リポ）。<b>取り方が他と違う</b>＝
/// <c>server/ywk_fetch_models.py</c> を変種の <c>python.exe</c> で子プロセス実行し、
/// JSON 行の進捗を読む（設計書 §1）。ランチャがこの型を使うのは件数・容量の見積りと検分。
/// </summary>
public sealed record ModelsLedger
{
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;

    [JsonPropertyName("name")] public string Name { get; init; } = "models";

    [JsonPropertyName("generated")] public string? Generated { get; init; }

    [JsonPropertyName("generator")] public string? Generator { get; init; }

    [JsonPropertyName("sha256_source")] public string? Sha256Source { get; init; }

    [JsonPropertyName("notes")] public IReadOnlyList<string> Notes { get; init; } = [];

    [JsonPropertyName("repos")] public IReadOnlyList<ModelRepo> Repos { get; init; } = [];

    public long TotalBytes => Repos.Sum(r => r.TotalBytes ?? r.Files.Sum(f => f.Size ?? 0));

    public int FileCount => Repos.Sum(r => r.Files.Count);
}

/// <summary>HF リポ 1 本（revision 固定）。</summary>
public sealed record ModelRepo
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = LedgerItemKinds.HfRepo;

    /// <summary><c>checkpoint</c>／<c>dacvae</c>／<c>silentcipher</c> 等。</summary>
    [JsonPropertyName("role")] public string? Role { get; init; }

    [JsonPropertyName("repo")] public string Repo { get; init; } = string.Empty;

    /// <summary>pin した revision（<c>refs/main</c> は台本が書く＝裁定 49）。</summary>
    [JsonPropertyName("revision")] public string Revision { get; init; } = string.Empty;

    [JsonPropertyName("head_at_generation")] public string? HeadAtGeneration { get; init; }

    /// <summary><c>HF_HOME/hub/</c> 直下に出来るディレクトリ名（<c>models--&lt;repo&gt;</c>）。</summary>
    [JsonPropertyName("hf_cache_dir")] public string? HfCacheDir { get; init; }

    [JsonPropertyName("file_count")] public int? FileCount { get; init; }

    [JsonPropertyName("total_bytes")] public long? TotalBytes { get; init; }

    [JsonPropertyName("files")] public IReadOnlyList<ModelFile> Files { get; init; } = [];
}

/// <summary>
/// HF の檔 1 本。<b>検証の手が 2 通りある</b>＝LFS なら sha256（<c>lfs.oid</c>）、
/// 非 LFS は git blob sha1 しか無い（<c>verify</c> 欄が名乗る＝<c>ledger/README.md</c> §3）。
/// </summary>
public sealed record ModelFile
{
    [JsonPropertyName("path")] public string Path { get; init; } = string.Empty;

    [JsonPropertyName("size")] public long? Size { get; init; }

    [JsonPropertyName("sha256")] public string? Sha256 { get; init; }

    [JsonPropertyName("git_blob_sha1")] public string? GitBlobSha1 { get; init; }

    /// <summary><c>sha256</c> か <c>git-blob-sha1</c>。</summary>
    [JsonPropertyName("verify")] public string? Verify { get; init; }

    [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;
}

/// <summary>
/// <c>ledger/vc_redist.json</c>。<b>埋め込み Python は <c>msvcp140.dll</c> を持たず torch が要る</b>
/// ので要る（台帳の <c>notes</c> の逐語）。既に System32 に在れば飛ばす（設計書 §6・裁定 54）。
/// </summary>
public sealed record VcRedistLedger
{
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;

    [JsonPropertyName("name")] public string Name { get; init; } = "vc-redist";

    [JsonPropertyName("generated")] public string? Generated { get; init; }

    [JsonPropertyName("generator")] public string? Generator { get; init; }

    [JsonPropertyName("items")] public IReadOnlyList<LedgerItem> Items { get; init; } = [];

    /// <summary>唯一の installer item（無ければ台帳が壊れている）。</summary>
    public LedgerItem? Installer =>
        Items.FirstOrDefault(i => i.Kind == LedgerItemKinds.Installer);
}
