using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Update;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// アプリ内更新（裁定 160・2026-09-24＝本体 yomiwakechan2 の更新シーケンスを写した型）。
/// <para>
/// 見るのは 4 つの層＝
/// ⑴ <b>配布情報の解釈</b>（<see cref="AppDistributionContract"/>）＝v≠1・版の欄の欠落・
/// sha256 の字数を不成立にする（前方互換＝未知の欄は無視）。
/// ⑵ <b>照合</b>＝一致なら最新（インストーラを取りにいかない）・不一致なら新しい版がある。
/// ⑶ <b>取得と検分</b>＝sha256 が合わなければ<b>置かずに消して起こさない</b>・
/// 上限超え／見切り／HTTP の失敗は一行に畳む。
/// ⑷ <b>画面の二段確認</b>＝初期の札・二押し目で札が変わる・走っている間は押せない・
/// 読み上げ中は断る・起こしたあとは起き直さない。
/// </para>
/// <para>
/// HTTP はフェイクの <see cref="HttpMessageHandler"/>・起動はテストが差す継ぎ目
/// （<b>実プロセスは 1 つも起こさない</b>・外へは 1 バイトも出ない）。
/// </para>
/// </summary>
public sealed class AppUpdateTests : IDisposable
{
    private const string CurrentVersion = "v2.0.7";
    private const string NewVersion = "v2.0.8";
    private const string ManifestUrl = "https://dist.test/app.json";
    private const string InstallerUrl = "https://dist.test/setup-cuda.exe";
    private const string RadeonInstallerUrl = "https://dist.test/setup-radeon.exe";

    private readonly string _root = TestArchives.NewTempDir("app-update");
    private readonly FakeHandler _handler = new();
    private readonly List<string> _launched = [];
    private readonly List<string> _log = [];
    private bool _launchSucceeds = true;

    private string UpdatesDir => Path.Combine(_root, "updates");

    public void Dispose()
    {
        _handler.Dispose();
        TestArchives.Remove(_root);
    }

    // ------------------------------------------------------------------ 配布情報の解釈

    [Fact]
    public void 正しい配布情報は版とインストーラの欄を返す()
    {
        var (manifest, error) = AppDistributionContract.ParseManifest(
            ManifestJson(NewVersion, Zeros(), note: "GPU メモリの伸びを直しました"), ReleaseFlavor.Cuda);

        Assert.Null(error);
        Assert.NotNull(manifest);
        Assert.Equal(AppDistributionContract.AppName, manifest!.Name);
        Assert.Equal(NewVersion, manifest.Version);
        Assert.Equal(InstallerUrl, manifest.InstallerUrl);
        Assert.Equal(Zeros(), manifest.InstallerSha256);
        Assert.Equal("GPU メモリの伸びを直しました", manifest.Note);

        // 版の鍵で読み分ける（同じ 1 枚から Radeon（ROCm）は別の url を読む）。
        var (radeon, radeonError) = AppDistributionContract.ParseManifest(
            ManifestJson(NewVersion, Zeros()), ReleaseFlavor.Radeon);
        Assert.Null(radeonError);
        Assert.Equal(RadeonInstallerUrl, radeon!.InstallerUrl);
    }

    [Fact]
    public void 知らないスキーマ版数の配布情報は不成立()
    {
        var json = ManifestJson(NewVersion, Zeros()).Replace("\"v\": 1", "\"v\": 2", StringComparison.Ordinal);
        var (manifest, error) = AppDistributionContract.ParseManifest(json, ReleaseFlavor.Cuda);

        Assert.Null(manifest);
        Assert.NotNull(error);
        Assert.Contains("v=2", error!);
    }

    [Fact]
    public void 自分の版の欄が無い配布情報は不成立()
    {
        const string json = """
            { "v": 1, "name": "irodori-tts-ywk", "version": "v2.0.8",
              "installers": { "radeon": { "url": "https://dist.test/setup-radeon.exe",
                                          "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } } }
            """;

        var (manifest, error) = AppDistributionContract.ParseManifest(json, ReleaseFlavor.Cuda);

        Assert.Null(manifest);
        Assert.NotNull(error);
        Assert.Contains("cuda", error!);
    }

    [Fact]
    public void sha256が64桁のhexでない配布情報は不成立()
    {
        var (manifest, error) = AppDistributionContract.ParseManifest(
            ManifestJson(NewVersion, "not-a-hash"), ReleaseFlavor.Cuda);

        Assert.Null(manifest);
        Assert.NotNull(error);
        Assert.Contains("sha256", error!);

        // 字数だけ合っていて hex でない物も通さない。
        var (second, secondError) = AppDistributionContract.ParseManifest(
            ManifestJson(NewVersion, new string('z', 64)), ReleaseFlavor.Cuda);
        Assert.Null(second);
        Assert.NotNull(secondError);
        Assert.Contains("sha256", secondError!);
    }

    [Fact]
    public void 未知の欄は無視して読む()
    {
        var json = ManifestJson(NewVersion, Zeros())
            .Replace("\"v\": 1", "\"v\": 1, \"unknownField\": { \"x\": 1 }", StringComparison.Ordinal);

        var (manifest, error) = AppDistributionContract.ParseManifest(json, ReleaseFlavor.Cuda);

        Assert.Null(error);
        Assert.Equal(NewVersion, manifest!.Version);
    }

    // ------------------------------------------------------------------ 照合

    [Fact]
    public async Task 版が同じなら最新でインストーラを取りにいかない()
    {
        SetManifest(CurrentVersion, Zeros());
        using var service = NewService();

        var result = await service.CheckAsync();

        Assert.Equal(AppUpdateResultKind.UpToDate, result.Kind);
        Assert.Equal(CurrentVersion, result.NewVersion);
        Assert.Equal([ManifestUrl], _handler.Requested);
    }

    [Fact]
    public async Task 版が違えば新しい版があると言い配布元の一行も運ぶ()
    {
        SetManifest(NewVersion, Zeros(), note: "読み上げが速くなりました");
        using var service = NewService();

        var result = await service.CheckAsync();

        Assert.Equal(AppUpdateResultKind.UpdateAvailable, result.Kind);
        Assert.Equal(NewVersion, result.NewVersion);
        Assert.Equal("読み上げが速くなりました", result.Note);
        Assert.Equal([ManifestUrl], _handler.Requested);
    }

    [Fact]
    public async Task 別のアプリの配布情報は検分に落とす()
    {
        _handler.SetText(ManifestUrl, ManifestJson(NewVersion, Zeros(), name: "yomiwakechan2"));
        using var service = NewService();

        var result = await service.CheckAsync();

        Assert.Equal(AppUpdateResultKind.VerifyFailed, result.Kind);
    }

    // ------------------------------------------------------------------ 取得と検分

    [Fact]
    public async Task 検分に通れば掃除してから置いて起こす()
    {
        var body = TestHttpServer.Payload(8 * 1024, 3);
        SetManifest(NewVersion, TestArchives.Sha256Of(body));
        _handler.SetBytes(InstallerUrl, body);

        // 古い残骸を先に置いておく（版違いを溜めない＝掃除されること）。
        Directory.CreateDirectory(UpdatesDir);
        var stale = Path.Combine(UpdatesDir, "irodori-tts-ywk-setup-v2.0.1-cuda.exe");
        File.WriteAllText(stale, "old");

        using var service = NewService();
        var result = await service.ApplyAsync(NewVersion);

        Assert.Equal(AppUpdateResultKind.LaunchedInstaller, result.Kind);
        Assert.Equal(NewVersion, result.NewVersion);
        Assert.False(File.Exists(stale));

        var expected = Path.Combine(UpdatesDir, "irodori-tts-ywk-setup-v2.0.8-cuda.exe");
        Assert.Equal([expected], _launched);
        Assert.Equal(body, File.ReadAllBytes(expected));
        Assert.Equal([expected], Directory.GetFiles(UpdatesDir));
    }

    [Fact]
    public async Task sha256が合わなければ置かずに消して起こさない()
    {
        var body = TestHttpServer.Payload(4 * 1024, 5);
        SetManifest(NewVersion, TestArchives.Sha256Of(TestHttpServer.Payload(4 * 1024, 9)));
        _handler.SetBytes(InstallerUrl, body);

        using var service = NewService();
        var result = await service.ApplyAsync(NewVersion);

        Assert.Equal(AppUpdateResultKind.VerifyFailed, result.Kind);
        Assert.Empty(_launched);
        Assert.Empty(Directory.GetFiles(UpdatesDir));
    }

    [Fact]
    public async Task 上限を超える更新ファイルは飲まない()
    {
        var body = TestHttpServer.Payload(64 * 1024, 11);
        SetManifest(NewVersion, TestArchives.Sha256Of(body));
        _handler.SetBytes(InstallerUrl, body);

        // 申告（Content-Length）の時点で超えている回。
        using (var service = NewService(maxInstallerBytes: 1024))
        {
            var declared = await service.ApplyAsync(NewVersion);
            Assert.Equal(AppUpdateResultKind.CheckFailed, declared.Kind);
            Assert.Empty(_launched);
            Assert.Empty(Directory.GetFiles(UpdatesDir));
        }

        // 長さを申告しない相手（分割送り）でも際限なく飲まない回。
        _handler.SetChunked(InstallerUrl, body);
        using (var service = NewService(maxInstallerBytes: 1024))
        {
            var undeclared = await service.ApplyAsync(NewVersion);
            Assert.Equal(AppUpdateResultKind.CheckFailed, undeclared.Kind);
            Assert.Empty(_launched);
            Assert.Empty(Directory.GetFiles(UpdatesDir));
        }
    }

    [Fact]
    public async Task 見切りを過ぎたら諦めて一行にする()
    {
        SetManifest(NewVersion, Zeros());
        _handler.SetStall(InstallerUrl);

        using var service = NewService(installerTimeout: TimeSpan.FromMilliseconds(80));
        var result = await service.ApplyAsync(NewVersion);

        Assert.Equal(AppUpdateResultKind.CheckFailed, result.Kind);
        Assert.Empty(_launched);
        Assert.Empty(Directory.GetFiles(UpdatesDir));
    }

    [Fact]
    public async Task 配布情報が取れない回は確認できなかったの一行()
    {
        _handler.SetStatus(ManifestUrl, HttpStatusCode.NotFound);
        using var service = NewService();

        var result = await service.CheckAsync();

        Assert.Equal(AppUpdateResultKind.CheckFailed, result.Kind);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }

    [Fact]
    public async Task 更新ファイルが取れない回も一行に畳む()
    {
        SetManifest(NewVersion, Zeros());
        _handler.SetStatus(InstallerUrl, HttpStatusCode.ServiceUnavailable);
        using var service = NewService();

        var result = await service.ApplyAsync(NewVersion);

        Assert.Equal(AppUpdateResultKind.CheckFailed, result.Kind);
        Assert.Empty(_launched);
    }

    [Fact]
    public async Task 承諾した版と配布が食い違えば適用しない()
    {
        SetManifest("v2.0.9", Zeros());
        using var service = NewService();

        var result = await service.ApplyAsync(NewVersion);

        Assert.Equal(AppUpdateResultKind.UpdateAvailable, result.Kind);
        Assert.Equal("v2.0.9", result.NewVersion);
        Assert.Empty(_launched);
        Assert.Equal([ManifestUrl], _handler.Requested);
    }

    [Fact]
    public async Task 取得先がhttpsでなければ取りにいかない()
    {
        _handler.SetText(
            ManifestUrl, ManifestJson(NewVersion, Zeros(), cudaUrl: "http://dist.test/setup-cuda.exe"));
        using var service = NewService();

        var result = await service.ApplyAsync(NewVersion);

        Assert.Equal(AppUpdateResultKind.VerifyFailed, result.Kind);
        Assert.Equal([ManifestUrl], _handler.Requested);
    }

    [Fact]
    public async Task 起こせなかった回は保存先を添えて一行にする()
    {
        var body = TestHttpServer.Payload(2 * 1024, 13);
        SetManifest(NewVersion, TestArchives.Sha256Of(body));
        _handler.SetBytes(InstallerUrl, body);
        _launchSucceeds = false;

        using var service = NewService();
        var result = await service.ApplyAsync(NewVersion);

        Assert.Equal(AppUpdateResultKind.LaunchFailed, result.Kind);
        Assert.Equal(Path.Combine(UpdatesDir, "irodori-tts-ywk-setup-v2.0.8-cuda.exe"), result.Detail);
        Assert.True(File.Exists(result.Detail));
    }

    [Fact]
    public void 檔名に使えない字は版から落とす()
    {
        var name = AppUpdateService.InstallerFileName("v2.0.8/beta", ReleaseFlavor.Radeon);

        Assert.Equal("irodori-tts-ywk-setup-v2.0.8-beta-radeon.exe", name);
    }

    // ------------------------------------------------------------------ 画面（二段確認）

    [Fact]
    public void 未配線なら更新の釦は押せず一行も出ない()
    {
        var about = NewAbout();

        Assert.False(about.UpdateAttached);
        Assert.False(about.UpdateCommand.CanExecute(null));
        Assert.Equal(UiStrings.AboutUpdateCheckButton, about.UpdateButtonText);
        Assert.Equal(string.Empty, about.UpdateStatusText);
        Assert.False(about.UpdateStatusVisible);
    }

    [Fact]
    public async Task 一押し目は照合だけで札が次の一押しを言う()
    {
        var gateway = new FakeGateway
        {
            CheckResult = new AppUpdateResult(AppUpdateResultKind.UpdateAvailable, NewVersion),
        };
        var about = NewAbout();
        var exits = 0;
        about.AttachUpdater(gateway, () => exits++);

        Assert.True(about.UpdateCommand.CanExecute(null));
        await about.UpdateCommand.ExecuteAsync();

        Assert.Equal(1, gateway.Checks);
        Assert.Equal(0, gateway.Applies);
        Assert.Equal(0, exits);
        Assert.Equal(NewVersion + UiStrings.AboutUpdateApplySuffix, about.UpdateButtonText);
        Assert.Contains(NewVersion, about.UpdateStatusText);
        Assert.True(about.UpdateStatusVisible);

        // 二押し目＝取得・検分・起動まで行って、既存の終了の入口を 1 度だけ叩く。
        gateway.ApplyResult = new AppUpdateResult(AppUpdateResultKind.LaunchedInstaller, NewVersion);
        await about.UpdateCommand.ExecuteAsync();

        Assert.Equal(NewVersion, gateway.ConfirmedVersion);
        Assert.Equal(1, exits);
        Assert.Equal(UiStrings.AboutUpdateLaunched, about.UpdateStatusText);

        // 起こしたあとは起き直さない（終了までの窓で二重に起こさない）。
        Assert.False(about.UpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task 最新なら控えを持たず札は確認のまま()
    {
        var gateway = new FakeGateway
        {
            CheckResult = new AppUpdateResult(AppUpdateResultKind.UpToDate, CurrentVersion),
        };
        var about = NewAbout();
        about.AttachUpdater(gateway, () => { });

        await about.UpdateCommand.ExecuteAsync();

        Assert.Equal(UiStrings.AboutUpdateCheckButton, about.UpdateButtonText);
        Assert.Contains(CurrentVersion, about.UpdateStatusText);
        Assert.True(about.UpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task 走っているあいだは押せない()
    {
        var gate = new TaskCompletionSource<AppUpdateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new FakeGateway { CheckGate = gate.Task };
        var about = NewAbout();
        about.AttachUpdater(gateway, () => { });

        var running = about.UpdateCommand.ExecuteAsync();
        Assert.False(about.UpdateCommand.CanExecute(null));

        gate.SetResult(new AppUpdateResult(AppUpdateResultKind.UpToDate, CurrentVersion));
        await running;

        Assert.True(about.UpdateCommand.CanExecute(null));
        Assert.Equal(1, gateway.Checks);
    }

    [Fact]
    public async Task 読み上げ中の二押し目は断って控えを保つ()
    {
        var gateway = new FakeGateway
        {
            CheckResult = new AppUpdateResult(AppUpdateResultKind.UpdateAvailable, NewVersion),
        };
        var about = NewAbout();
        var busy = false;
        about.AttachUpdater(gateway, () => { }, () => busy);

        await about.UpdateCommand.ExecuteAsync();
        busy = true;
        await about.UpdateCommand.ExecuteAsync();

        Assert.Equal(0, gateway.Applies);
        Assert.Equal(UiStrings.AboutUpdateBusy, about.UpdateStatusText);
        Assert.Equal(NewVersion + UiStrings.AboutUpdateApplySuffix, about.UpdateButtonText);

        // 読み上げが終われば同じ釦でそのまま進める。
        busy = false;
        gateway.ApplyResult = new AppUpdateResult(AppUpdateResultKind.LaunchedInstaller, NewVersion);
        await about.UpdateCommand.ExecuteAsync();
        Assert.Equal(1, gateway.Applies);
    }

    [Fact]
    public async Task 失敗しても控えは残りもう一押しで取り直せる()
    {
        var gateway = new FakeGateway
        {
            CheckResult = new AppUpdateResult(AppUpdateResultKind.UpdateAvailable, NewVersion),
            ApplyResult = new AppUpdateResult(
                AppUpdateResultKind.CheckFailed, NewVersion, Detail: "更新ファイルを取りにいけませんでした"),
        };
        var about = NewAbout();
        about.AttachUpdater(gateway, () => { });

        await about.UpdateCommand.ExecuteAsync();
        await about.UpdateCommand.ExecuteAsync();

        Assert.Equal(NewVersion + UiStrings.AboutUpdateApplySuffix, about.UpdateButtonText);
        Assert.True(about.UpdateCommand.CanExecute(null));
        Assert.StartsWith(UiStrings.AboutUpdateFailedPrefix, about.UpdateStatusText);
    }

    [Fact]
    public void 結末の一行はすべて出所がUiStringsである()
    {
        Assert.Equal(
            UiStrings.AboutUpdateUpToDatePrefix + CurrentVersion + UiStrings.AboutUpdateUpToDateSuffix,
            AboutViewModel.DescribeUpdateResult(
                new AppUpdateResult(AppUpdateResultKind.UpToDate, CurrentVersion)));

        Assert.Equal(
            UiStrings.AboutUpdateVerifyFailedPrefix + "理由" + UiStrings.AboutUpdateVerifyFailedSuffix,
            AboutViewModel.DescribeUpdateResult(
                new AppUpdateResult(AppUpdateResultKind.VerifyFailed, NewVersion, Detail: "理由")));

        Assert.Equal(
            UiStrings.AboutUpdateLaunchFailedPrefix + "C:\\x\\setup.exe"
            + UiStrings.AboutUpdateLaunchFailedSuffix,
            AboutViewModel.DescribeUpdateResult(
                new AppUpdateResult(AppUpdateResultKind.LaunchFailed, NewVersion, Detail: "C:\\x\\setup.exe")));

        // 配布元の一行は在るときだけ足す（表示のみ＝値で枝を分けない）。
        Assert.DoesNotContain(
            UiStrings.AboutUpdateNotePrefix,
            AboutViewModel.DescribeUpdateResult(
                new AppUpdateResult(AppUpdateResultKind.UpdateAvailable, NewVersion)));
        Assert.Contains(
            UiStrings.AboutUpdateNotePrefix + "お知らせ",
            AboutViewModel.DescribeUpdateResult(
                new AppUpdateResult(AppUpdateResultKind.UpdateAvailable, NewVersion, "お知らせ")));
    }

    // ------------------------------------------------------------------ 同梱の app.json

    [Fact]
    public void 同梱のappJsonは両方の版の欄を持ち解釈に通る()
    {
        var path = Path.Combine(RepoRoot(), "site", "app.json");
        Assert.True(File.Exists(path), "site/app.json が無い：" + path);

        var json = File.ReadAllText(path, Encoding.UTF8);
        foreach (var flavor in new[] { ReleaseFlavor.Cuda, ReleaseFlavor.Radeon })
        {
            var (manifest, error) = AppDistributionContract.ParseManifest(json, flavor);
            Assert.Null(error);
            Assert.Equal(AppDistributionContract.AppName, manifest!.Name);
            Assert.StartsWith("v", manifest.Version, StringComparison.Ordinal);
            Assert.StartsWith("https://", manifest.InstallerUrl, StringComparison.Ordinal);
            Assert.Contains(AppDistributionContract.FlavorKey(flavor), manifest.InstallerUrl);
        }
    }

    // ------------------------------------------------------------------ 道具立て

    private static string Zeros() => new('0', AppDistributionContract.Sha256HexLength);

    private static string ManifestJson(
        string version,
        string sha256,
        string? note = null,
        string name = AppDistributionContract.AppName,
        string cudaUrl = InstallerUrl)
    {
        var noteJson = note is null ? "null" : "\"" + note + "\"";
        return """
            { "v": 1, "name": "@NAME@", "version": "@VERSION@", "builtAt": "2026-09-24T00:00:00Z",
              "pageUrl": "https://dist.test/", "note": @NOTE@,
              "installers": {
                "cuda":   { "url": "@CUDA@",   "sha256": "@SHA@", "sizeBytes": 0 },
                "radeon": { "url": "@RADEON@", "sha256": "@SHA@", "sizeBytes": 0 } } }
            """
            .Replace("@NAME@", name, StringComparison.Ordinal)
            .Replace("@VERSION@", version, StringComparison.Ordinal)
            .Replace("@NOTE@", noteJson, StringComparison.Ordinal)
            .Replace("@CUDA@", cudaUrl, StringComparison.Ordinal)
            .Replace("@RADEON@", RadeonInstallerUrl, StringComparison.Ordinal)
            .Replace("@SHA@", sha256, StringComparison.Ordinal);
    }

    private void SetManifest(string version, string sha256, string? note = null) =>
        _handler.SetText(ManifestUrl, ManifestJson(version, sha256, note));

    private AppUpdateService NewService(long? maxInstallerBytes = null, TimeSpan? installerTimeout = null) =>
        new(
            CurrentVersion,
            ReleaseFlavor.Cuda,
            UpdatesDir,
            _handler,
            ManifestUrl,
            path =>
            {
                _launched.Add(path);
                return _launchSucceeds;
            },
            _log.Add)
        {
            MaxInstallerBytes = maxInstallerBytes ?? AppUpdateService.DefaultMaxInstallerBytes,
            InstallerTimeout = installerTimeout ?? TimeSpan.FromSeconds(300),
        };

    private AboutViewModel NewAbout() => new(new AppPaths(
        Path.Combine(_root, "app"),
        Path.Combine(_root, "app"),
        Path.Combine(_root, "runtime"),
        _root,
        developerMode: false));

    /// <summary>リポの根（試験の出力から数えて上へ辿る＝路を書き写さない）。</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "site")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>HTTP の継ぎ目（外へは 1 バイトも出ない）。</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _responses =
            new(StringComparer.Ordinal);

        public List<string> Requested { get; } = [];

        public void SetText(string url, string body) =>
            _responses[url] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
            };

        public void SetBytes(string url, byte[] body) =>
            _responses[url] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            };

        /// <summary>長さを申告しない相手（分割送り）。</summary>
        public void SetChunked(string url, byte[] body) =>
            _responses[url] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(body)),
            };

        public void SetStatus(string url, HttpStatusCode status) =>
            _responses[url] = () => new HttpResponseMessage(status);

        /// <summary>返事をしない相手（見切りの検分）。</summary>
        public void SetStall(string url) => _responses[url] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream()),
        };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            await Task.Yield();
            return _responses.TryGetValue(url, out var make)
                ? make()
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    /// <summary>1 バイトも進まない本文（無通信の見切りに掛かる相手）。</summary>
    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>画面の検分用の口（取得も檔も触らない）。</summary>
    private sealed class FakeGateway : IAppUpdateGateway
    {
        public AppUpdateResult CheckResult { get; set; } =
            new(AppUpdateResultKind.UpToDate, CurrentVersion);

        public AppUpdateResult ApplyResult { get; set; } =
            new(AppUpdateResultKind.LaunchedInstaller, NewVersion);

        public Task<AppUpdateResult>? CheckGate { get; set; }

        public int Checks { get; private set; }

        public int Applies { get; private set; }

        public string? ConfirmedVersion { get; private set; }

        public async Task<AppUpdateResult> CheckAsync()
        {
            Checks++;
            if (CheckGate is not null)
            {
                return await CheckGate.ConfigureAwait(false);
            }

            return CheckResult;
        }

        public Task<AppUpdateResult> ApplyAsync(string confirmedVersion)
        {
            Applies++;
            ConfirmedVersion = confirmedVersion;
            return Task.FromResult(ApplyResult);
        }
    }
}
