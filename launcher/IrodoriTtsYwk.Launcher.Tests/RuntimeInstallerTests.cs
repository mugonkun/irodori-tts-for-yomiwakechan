using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Ledger;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 展開の規則を、テストの中で<b>合成した</b>小さな wheel／sdist／archive で釘付けする
/// （<c>build/assemble-runtime.ps1</c> の <c>Install-YwkWheel</c>／<c>Install-YwkSourceTree</c> と
/// 同じ結果になるか）。
/// </summary>
public sealed class RuntimeInstallerTests : IDisposable
{
    private readonly string _root = TestArchives.NewTempDir("install");

    private string Cache => Path.Combine(_root, "cache");

    private string RuntimeDir => Path.Combine(_root, "out", "runtime-test");

    private string SitePackages => Path.Combine(RuntimeDir, "site-packages");

    private string AppDir => Path.Combine(_root, "app");

    public void Dispose() => TestArchives.Remove(_root);

    private string WritePthTemplate()
    {
        var server = Path.Combine(AppDir, "server");
        Directory.CreateDirectory(server);
        var path = Path.Combine(server, "python312._pth.template");
        File.WriteAllText(
            path,
            "python312.zip\n.\n@RUNTIME_DIR@/site-packages\n@APP_DIR@/server\n",
            new UTF8Encoding(false));
        return path;
    }

    /// <summary>合成した檔から台帳を組む。</summary>
    private LedgerFile Ledger(params LedgerItem[] items) => new()
    {
        Schema = 1,
        Name = "runtime-test",
        Items = items,
    };

    /// <summary>
    /// 台帳の 1 件（<b>cache に在る檔の本物の sha256 を載せる</b>）。
    /// 展開は原檔を必ずハッシュで検めるので（<c>assemble-runtime.ps1</c> と同じ規律）、
    /// 偽の <c>"00"</c> では 1 件も通らない。檔がまだ無い件は "無い" 側の試験なので空のまま。
    /// </summary>
    private LedgerItem Item(string kind, string name, string version, string fileName,
        IReadOnlyList<string>? packageDirs = null) => new()
    {
        Kind = kind,
        Name = name,
        Version = version,
        Url = "https://example.invalid/" + fileName,
        Filename = fileName,
        Sha256 = Sha256OfCached(fileName),
        License = "MIT",
        PackageDirs = packageDirs ?? [],
    };

    private string Sha256OfCached(string fileName)
    {
        var path = Path.Combine(Cache, fileName);
        if (!File.Exists(path))
        {
            return "00";
        }

        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
    }

    private async Task<InstallResult> InstallAsync(LedgerFile ledger)
    {
        var installer = new WheelInstaller(new PthWriter());
        return await installer.InstallAsync(
            new InstallRequest(ledger, Cache, RuntimeDir, AppDir, WritePthTemplate()),
            null,
            CancellationToken.None);
    }

    private LedgerItem BuildPythonEmbed()
    {
        Directory.CreateDirectory(Cache);
        TestArchives.WritePythonEmbedZip(Path.Combine(Cache, "python-embed.zip"));
        return Item(LedgerItemKinds.PythonEmbed, "python", "3.12.10", "python-embed.zip");
    }

    [Fact]
    public async Task 埋め込みPythonを展開してpthを書く()
    {
        var result = await InstallAsync(Ledger(BuildPythonEmbed()));

        Assert.True(result.Ok, result.FailureReason);
        Assert.True(File.Exists(Path.Combine(RuntimeDir, "python.exe")));

        var pth = Path.Combine(RuntimeDir, PthWriter.PthFileName);
        Assert.Equal(pth, result.PthPath);

        // 檔は CRLF・末尾改行 1 本・BOM なし
        var bytes = await File.ReadAllBytesAsync(pth);
        Assert.NotEqual(0xEF, bytes[0]);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@", text, StringComparison.Ordinal);
        Assert.Contains(RuntimeDir.Replace('\\', '/') + "/site-packages", text, StringComparison.Ordinal);
        Assert.Contains(AppDir.Replace('\\', '/') + "/server", text, StringComparison.Ordinal);
        // zip が同梱していた素の python312._pth は残らない（二重にしない）
        Assert.Single(Directory.GetFiles(RuntimeDir, "*._pth"));
    }

    [Fact]
    public async Task wheelはdist_info込みで展開しdataを畳む()
    {
        Directory.CreateDirectory(Cache);
        TestArchives.WriteZip(Path.Combine(Cache, "demo-1.0-py3-none-any.whl"), new Dictionary<string, string>
        {
            ["demo/__init__.py"] = "x = 1\n",
            ["demo-1.0.dist-info/METADATA"] = "Name: demo\nVersion: 1.0\n",
            ["demo-1.0.dist-info/RECORD"] = string.Empty,
            // <name>-<ver>.data/purelib|platlib は 1 段上へ合流する
            ["demo-1.0.data/purelib/demo_extra/__init__.py"] = "y = 2\n",
            ["demo-1.0.data/platlib/demo_native.pyd"] = "binary",
            // scripts / headers / data は捨てる（PATH に console script を置かない）
            ["demo-1.0.data/scripts/demo.exe"] = "launcher",
            ["demo-1.0.data/headers/demo.h"] = "#pragma once",
            ["demo-1.0.data/data/share/demo.txt"] = "note",
        });

        var result = await InstallAsync(Ledger(
            BuildPythonEmbed(),
            Item(LedgerItemKinds.Wheel, "demo", "1.0", "demo-1.0-py3-none-any.whl")));

        Assert.True(result.Ok, result.FailureReason);
        Assert.True(File.Exists(Path.Combine(SitePackages, "demo", "__init__.py")));
        Assert.True(File.Exists(Path.Combine(SitePackages, "demo-1.0.dist-info", "METADATA")));
        Assert.True(File.Exists(Path.Combine(SitePackages, "demo_extra", "__init__.py")));
        Assert.True(File.Exists(Path.Combine(SitePackages, "demo_native.pyd")));
        Assert.False(Directory.Exists(Path.Combine(SitePackages, "demo-1.0.data")));
        Assert.False(File.Exists(Path.Combine(SitePackages, "demo.exe")));
        Assert.False(File.Exists(Path.Combine(SitePackages, "demo.h")));
        Assert.Equal(1, result.DistInfoCount);
    }

    [Fact]
    public async Task site_packages直下のpthは捨てる()
    {
        // ledger/README.md §4-4＝import site が無い ._pth では .pth は読まれない
        Directory.CreateDirectory(Cache);
        TestArchives.WriteZip(Path.Combine(Cache, "setuptools-80.0-py3-none-any.whl"),
            new Dictionary<string, string>
            {
                ["setuptools/__init__.py"] = string.Empty,
                ["distutils-precedence.pth"] = "import os; ...\n",
                ["setuptools-80.0.dist-info/METADATA"] = "Name: setuptools\nVersion: 80.0\n",
            });

        var result = await InstallAsync(Ledger(
            BuildPythonEmbed(),
            Item(LedgerItemKinds.Wheel, "setuptools", "80.0", "setuptools-80.0-py3-none-any.whl")));

        Assert.True(result.Ok, result.FailureReason);
        Assert.Equal(["distutils-precedence.pth"], result.DroppedPth);
        Assert.Empty(Directory.GetFiles(SitePackages, "*.pth"));
    }

    [Fact]
    public async Task sdistはpackage_dirsを写して最小dist_infoを作る()
    {
        Directory.CreateDirectory(Cache);
        TestArchives.WriteTarGz(Path.Combine(Cache, "argbind-0.3.9.tar.gz"), new Dictionary<string, string>
        {
            ["argbind-0.3.9/PKG-INFO"] = "Name: argbind\n",
            ["argbind-0.3.9/setup.py"] = "from setuptools import setup\n",
            ["argbind-0.3.9/argbind/__init__.py"] = "from .argbind import bind\n",
            ["argbind-0.3.9/argbind/argbind.py"] = "def bind(): pass\n",
        });

        var result = await InstallAsync(Ledger(
            BuildPythonEmbed(),
            Item(LedgerItemKinds.Sdist, "argbind", "0.3.9", "argbind-0.3.9.tar.gz", ["argbind"])));

        Assert.True(result.Ok, result.FailureReason);
        Assert.True(File.Exists(Path.Combine(SitePackages, "argbind", "argbind.py")));
        // setup.py・PKG-INFO は写さない
        Assert.False(File.Exists(Path.Combine(SitePackages, "setup.py")));

        var distInfo = Path.Combine(SitePackages, "argbind-0.3.9.dist-info");
        Assert.True(Directory.Exists(distInfo));
        var metadata = await File.ReadAllTextAsync(Path.Combine(distInfo, "METADATA"));
        Assert.Contains("Name: argbind", metadata, StringComparison.Ordinal);
        Assert.Contains("Version: 0.3.9", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", metadata, StringComparison.Ordinal); // LF
        Assert.Equal("argbind\n", await File.ReadAllTextAsync(Path.Combine(distInfo, "top_level.txt")));
        Assert.Equal(MinimalDistInfo.InstallerName + "\n",
            await File.ReadAllTextAsync(Path.Combine(distInfo, "INSTALLER")));
    }

    [Fact]
    public async Task archiveは1段深いpackage_dirを写せる()
    {
        // silentcipher の package_dir は "src/silentcipher"
        Directory.CreateDirectory(Cache);
        TestArchives.WriteZip(Path.Combine(Cache, "silentcipher-abc123.zip"), new Dictionary<string, string>
        {
            ["silentcipher-abc123/README.md"] = "# silentcipher\n",
            ["silentcipher-abc123/src/silentcipher/__init__.py"] = "from .server import get_model\n",
            ["silentcipher-abc123/src/silentcipher/server.py"] = "def get_model(): pass\n",
        });

        var item = Item(LedgerItemKinds.Archive, "silentcipher", "1.0.5+d46d7d0", "silentcipher-abc123.zip")
            with { PackageDir = "src/silentcipher" };

        var result = await InstallAsync(Ledger(BuildPythonEmbed(), item));

        Assert.True(result.Ok, result.FailureReason);
        Assert.True(File.Exists(Path.Combine(SitePackages, "silentcipher", "server.py")));
        Assert.False(Directory.Exists(Path.Combine(SitePackages, "src")));
        Assert.True(Directory.Exists(Path.Combine(SitePackages, "silentcipher-1.0.5+d46d7d0.dist-info")));
    }

    [Fact]
    public async Task package_dirが無ければ失敗して成果物を残さない()
    {
        Directory.CreateDirectory(Cache);
        TestArchives.WriteTarGz(Path.Combine(Cache, "broken-1.0.tar.gz"), new Dictionary<string, string>
        {
            ["broken-1.0/PKG-INFO"] = "Name: broken\n",
        });

        var result = await InstallAsync(Ledger(
            BuildPythonEmbed(),
            Item(LedgerItemKinds.Sdist, "broken", "1.0", "broken-1.0.tar.gz", ["broken"])));

        Assert.False(result.Ok);
        Assert.Contains("package_dir", result.FailureReason!, StringComparison.Ordinal);
        // 家の規則＝非ゼロ終了は成果物を残さない
        Assert.False(Directory.Exists(RuntimeDir));
    }

    [Fact]
    public async Task cacheに原檔が無ければ失敗する()
    {
        var result = await InstallAsync(Ledger(
            BuildPythonEmbed(),
            Item(LedgerItemKinds.Wheel, "missing", "1.0", "missing-1.0-py3-none-any.whl")));

        Assert.False(result.Ok);
        Assert.Contains("missing", result.FailureReason!, StringComparison.Ordinal);
        Assert.False(Directory.Exists(RuntimeDir));
    }

    [Fact]
    public async Task cacheの原檔が台帳のsha256と合わなければ展開しない()
    {
        // 所見 9 の釘＝置き場は %LOCALAPPDATA%\irodori-tts-ywk\cache（利用者が書けて回を跨いで
        // 残る）で、展開は取得とは別の段。取得時の検証は今回の展開を保証しない。
        Directory.CreateDirectory(Cache);
        var wheel = Path.Combine(Cache, "demo-1.0-py3-none-any.whl");
        TestArchives.WriteZip(wheel, new Dictionary<string, string>
        {
            ["demo/__init__.py"] = "x = 1\n",
            ["demo-1.0.dist-info/METADATA"] = "Name: demo\nVersion: 1.0\n",
        });

        var item = Item(LedgerItemKinds.Wheel, "demo", "1.0", "demo-1.0-py3-none-any.whl");

        // 台帳を書いた後に cache の檔だけ差し替えられた（＝改竄・取り違え）
        TestArchives.WriteZip(wheel, new Dictionary<string, string>
        {
            ["demo/__init__.py"] = "TAMPERED\n",
            ["demo-1.0.dist-info/METADATA"] = "Name: demo\nVersion: 1.0\n",
        });

        var result = await InstallAsync(Ledger(BuildPythonEmbed(), item));

        Assert.False(result.Ok);
        Assert.Contains("sha256", result.FailureReason!, StringComparison.Ordinal);
        // 汚れた檔は import の路に 1 秒も置かない
        Assert.False(Directory.Exists(RuntimeDir));
    }

    [Fact]
    public async Task sha256を持たない台帳の件は展開しない()
    {
        // 参照実装 assemble-runtime.ps1 の Get-YwkCachedItem（146〜148 行）は throw する。
        Directory.CreateDirectory(Cache);
        TestArchives.WriteZip(Path.Combine(Cache, "demo-1.0-py3-none-any.whl"),
            new Dictionary<string, string>
            {
                ["demo/__init__.py"] = "x = 1\n",
                ["demo-1.0.dist-info/METADATA"] = "Name: demo\nVersion: 1.0\n",
            });

        var item = Item(LedgerItemKinds.Wheel, "demo", "1.0", "demo-1.0-py3-none-any.whl")
            with { Sha256 = null };

        var result = await InstallAsync(Ledger(BuildPythonEmbed(), item));

        Assert.False(result.Ok);
        Assert.Contains("sha256", result.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void 展開後に在るべきdist_infoの件数は台帳から決まる()
    {
        // 所見 10 の釘＝数えて報告するだけでは、檔が 1 件も入らなくても Ok=true になる。
        // 埋め込み Python は site-packages に何も置かないので数えない。
        var items = new[]
        {
            Item(LedgerItemKinds.PythonEmbed, "python", "3.12.10", "python-embed.zip"),
            Item(LedgerItemKinds.Wheel, "a", "1.0", "a.whl"),
            Item(LedgerItemKinds.Sdist, "b", "1.0", "b.tar.gz"),
            Item(LedgerItemKinds.Archive, "c", "1.0", "c.zip"),
        };

        Assert.Equal(3, WheelInstaller.ExpectedDistInfoCount(items));
        Assert.Equal(0, WheelInstaller.ExpectedDistInfoCount([]));
    }

    [Fact]
    public async Task python_exeが無い埋め込みzipは失敗する()
    {
        Directory.CreateDirectory(Cache);
        TestArchives.WriteZip(Path.Combine(Cache, "python-embed.zip"),
            new Dictionary<string, string> { ["README.txt"] = "no python here" });

        var result = await InstallAsync(Ledger(
            Item(LedgerItemKinds.PythonEmbed, "python", "3.12.10", "python-embed.zip")));

        Assert.False(result.Ok);
        Assert.Contains("python.exe", result.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 変種の台帳に埋め込みPythonが無ければ配布樹の台帳から読む()
    {
        // 呼び手が LedgerReader.WithPythonEmbed を通さなくても組める（順を強いない）。
        Directory.CreateDirectory(Cache);
        TestArchives.WritePythonEmbedZip(Path.Combine(Cache, "python-3.12.10-embed-amd64.zip"));
        var ledgerDir = Path.Combine(AppDir, "ledger");
        Directory.CreateDirectory(ledgerDir);
        await File.WriteAllTextAsync(Path.Combine(ledgerDir, "python-embed.json"),
            """
            { "schema": 1, "name": "python-embed", "count": 1, "items": [
              { "kind": "python-embed", "name": "python", "version": "3.12.10",
                "url": "https://example.invalid/python-3.12.10-embed-amd64.zip",
                "sha256": "@SHA@", "size": 3, "license": "PSF-2.0" } ] }
            """.Replace("@SHA@", Sha256OfCached("python-3.12.10-embed-amd64.zip"), StringComparison.Ordinal));

        TestArchives.WriteZip(Path.Combine(Cache, "demo-1.0-py3-none-any.whl"), new Dictionary<string, string>
        {
            ["demo/__init__.py"] = string.Empty,
            ["demo-1.0.dist-info/METADATA"] = "Name: demo\nVersion: 1.0\n",
        });

        // 台帳は変種の物だけ（埋め込み Python の item を持たない）
        var result = await InstallAsync(Ledger(
            Item(LedgerItemKinds.Wheel, "demo", "1.0", "demo-1.0-py3-none-any.whl")));

        Assert.True(result.Ok, result.FailureReason);
        Assert.True(File.Exists(Path.Combine(RuntimeDir, "python.exe")));
        Assert.True(File.Exists(Path.Combine(SitePackages, "demo", "__init__.py")));
    }

    [Fact]
    public async Task 展開先の外を指すzipは弾く()
    {
        // zip slip＝第三者の archive を 100 件超え展開する経路の防具
        Directory.CreateDirectory(Cache);
        TestArchives.WriteZip(Path.Combine(Cache, "evil-1.0-py3-none-any.whl"),
            new Dictionary<string, string> { ["../../evil.txt"] = "pwned" });

        var result = await InstallAsync(Ledger(
            BuildPythonEmbed(),
            Item(LedgerItemKinds.Wheel, "evil", "1.0", "evil-1.0-py3-none-any.whl")));

        Assert.False(result.Ok);
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
        Assert.False(Directory.Exists(RuntimeDir));
    }

    [Fact]
    public async Task 台帳の順に展開する()
    {
        Directory.CreateDirectory(Cache);
        foreach (var name in new[] { "a", "b" })
        {
            TestArchives.WriteZip(Path.Combine(Cache, name + "-1.0-py3-none-any.whl"),
                new Dictionary<string, string>
                {
                    [name + "/__init__.py"] = string.Empty,
                    [name + "-1.0.dist-info/METADATA"] = "Name: " + name + "\nVersion: 1.0\n",
                });
        }

        var seen = new List<string>();
        var installer = new WheelInstaller(new PthWriter());
        var result = await installer.InstallAsync(
            new InstallRequest(
                Ledger(
                    BuildPythonEmbed(),
                    Item(LedgerItemKinds.Wheel, "a", "1.0", "a-1.0-py3-none-any.whl"),
                    Item(LedgerItemKinds.Wheel, "b", "1.0", "b-1.0-py3-none-any.whl")),
                Cache, RuntimeDir, AppDir, WritePthTemplate()),
            new Progress<InstallProgress>(p =>
            {
                if (p.Phase == InstallPhase.Packages && p.ItemName is not null)
                {
                    seen.Add(p.ItemName);
                }
            }),
            CancellationToken.None);

        Assert.True(result.Ok, result.FailureReason);
        Assert.Equal(2, result.DistInfoCount);
        for (var i = 0; i < 50 && seen.Count < 2; i++)
        {
            await Task.Delay(20);
        }

        Assert.Equal(["a", "b"], seen);
    }
}

public sealed class PthWriterTests : IDisposable
{
    private readonly string _root = TestArchives.NewTempDir("pth");

    public void Dispose() => TestArchives.Remove(_root);

    [Fact]
    public void 雛形を絶対パスで埋める()
    {
        var writer = new PthWriter();

        var text = writer.Render(
            "python312.zip\n.\n@RUNTIME_DIR@/site-packages\n@APP_DIR@/server\n",
            @"C:\data\runtime\cu130",
            @"C:\Program Files\irodori-tts-ywk");

        Assert.Equal(
            "python312.zip\r\n.\r\nC:/data/runtime/cu130/site-packages\r\nC:/Program Files/irodori-tts-ywk/server\r\n",
            text);
    }

    [Fact]
    public void 差し込み口が残ったら投げる()
    {
        var writer = new PthWriter();

        Assert.Throws<InvalidOperationException>(() =>
            writer.Render("@RUNTIME_DIR@\n@MODEL_DIR@\n", @"C:\r", @"C:\a"));
    }

    [Fact]
    public void import_siteが居たら投げる()
    {
        // 設計書 §2＝配布する ._pth は import site を持たない
        var writer = new PthWriter();

        Assert.Throws<InvalidOperationException>(() =>
            writer.Render("@RUNTIME_DIR@\nimport site\n", @"C:\r", @"C:\a"));
    }

    [Fact]
    public void 実物の雛形が埋まる()
    {
        var repo = RepoLedger.RepoRoot();
        if (repo is null)
        {
            return;
        }

        var template = Path.Combine(repo, "server", "python312._pth.template");
        if (!File.Exists(template))
        {
            return;
        }

        var runtimeDir = Path.Combine(_root, "runtime-cpu");
        Directory.CreateDirectory(runtimeDir);
        var path = new PthWriter().Write(template, runtimeDir, Path.Combine(_root, "app"));

        var text = File.ReadAllText(path);
        var lines = text.TrimEnd('\r', '\n').Split("\r\n");
        Assert.Equal("python312.zip", lines[0]);
        Assert.Equal(".", lines[1]);
        Assert.Equal(6, lines.Length);
        Assert.DoesNotContain(lines, l => l.Contains('\\', StringComparison.Ordinal));
        Assert.DoesNotContain("import site", text, StringComparison.Ordinal);
    }
}
