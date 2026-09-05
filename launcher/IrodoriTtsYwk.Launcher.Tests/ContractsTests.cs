using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IrodoriTtsYwk.Launcher.Contracts;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 共有の契約のうち<b>純関数の部分</b>を釘付けする（3 席がここを踏み台にする）。
/// 実機・GPU・HTTP には一切触れない。
/// </summary>
public sealed class AppPathsTests
{
    private static AppPaths Resolve(Dictionary<string, string?> env) =>
        AppPaths.Resolve(@"C:\Program Files\irodori-tts-ywk", @"C:\Users\u\AppData\Local",
            name => env.TryGetValue(name, out var value) ? value : null);

    [Fact]
    public void 既定は導入先と利用者データに分かれる()
    {
        var paths = Resolve([]);

        Assert.False(paths.DeveloperMode);
        Assert.Equal(@"C:\Program Files\irodori-tts-ywk", paths.AppDir);
        Assert.Equal(@"C:\Users\u\AppData\Local\irodori-tts-ywk", paths.DataDir);
        // 書き込み側は必ず利用者データ配下（導入先が読取専用でも壊れない）
        Assert.StartsWith(paths.DataDir, paths.VoicesDir, StringComparison.Ordinal);
        Assert.StartsWith(paths.DataDir, paths.HfHomeDir, StringComparison.Ordinal);
        // 読むだけの側は導入先
        Assert.StartsWith(paths.AppDir, paths.LedgerDir, StringComparison.Ordinal);
        Assert.StartsWith(paths.AppDir, paths.PresetVoicesDir, StringComparison.Ordinal);
    }

    [Fact]
    public void wrapperの既定と同じ場所を指す()
    {
        // server/ywk_server.py apply_env_defaults＝<data>/voices・<data>/voices/voices.json・<data>/models
        var paths = Resolve([]);

        Assert.Equal(Path.Combine(paths.DataDir, "voices"), paths.VoicesDir);
        Assert.Equal(Path.Combine(paths.DataDir, "voices", "voices.json"), paths.VoicesJsonPath);
        Assert.Equal(Path.Combine(paths.DataDir, "models"), paths.HfHomeDir);
    }

    [Fact]
    public void 開発モードのenvでbuild_outを指せる()
    {
        var paths = Resolve(new Dictionary<string, string?>
        {
            [AppPaths.AppDirEnvName] = @"C:\repo\build\out\app",
            [AppPaths.RuntimeRootEnvName] = @"C:\repo\build\out",
        });

        Assert.True(paths.DeveloperMode);
        Assert.Equal(@"C:\repo\build\out\app", paths.AppDir);
        Assert.Equal(@"C:\repo\build\out", paths.RuntimeRoot);
    }

    [Fact]
    public void 変種ディレクトリは配布の形とbuild_outの形の両方を拾う()
    {
        var candidates = AppPaths.RuntimeDirCandidates(@"C:\repo\build\out", "rocm-gfx1151");

        Assert.Equal(@"C:\repo\build\out\rocm-gfx1151", candidates[0]);
        Assert.Equal(@"C:\repo\build\out\runtime-rocm-gfx1151", candidates[1]);
        Assert.Equal(@"C:\repo\build\out", candidates[2]);

        var paths = Resolve(new Dictionary<string, string?>
        {
            [AppPaths.RuntimeRootEnvName] = @"C:\repo\build\out",
        });

        // build/out には runtime-<変種> の形で居る（便 A・便 C の実成果物）
        var resolved = paths.ResolveRuntimeDir(
            "rocm-gfx1151",
            dir => dir == @"C:\repo\build\out\runtime-rocm-gfx1151");
        Assert.Equal(@"C:\repo\build\out\runtime-rocm-gfx1151", resolved);
        Assert.Equal(
            Path.Combine(@"C:\repo\build\out\runtime-rocm-gfx1151", AppPaths.PythonExeName),
            paths.ResolvePythonExe("rocm-gfx1151", dir => dir == @"C:\repo\build\out\runtime-rocm-gfx1151"));
    }

    [Fact]
    public void 未取得ならnullを返す()
    {
        var paths = Resolve([]);
        Assert.Null(paths.ResolveRuntimeDir("cu130", _ => false));
        Assert.Null(paths.ResolvePythonExe("cu130", _ => false));
    }
}

/// <summary>変種の 2 系統の名前（台帳の綴りと <c>YWK_VARIANT</c> のラベル）。</summary>
public sealed class RuntimeVariantsTests
{
    [Theory]
    [InlineData(RuntimeVariants.Cu130, "cuda")]
    [InlineData(RuntimeVariants.Cu126, "cuda")]
    [InlineData(RuntimeVariants.Cpu, "cpu")]
    [InlineData(RuntimeVariants.RocmGfx1151, "rocm-gfx1151")]
    public void ラベルはcu130とcu126をcudaに畳む(string variant, string label) =>
        Assert.Equal(label, RuntimeVariants.ServerLabel(variant));

    [Fact]
    public void 台帳名はruntime前置()
    {
        Assert.Equal("runtime-cu130", RuntimeVariants.LedgerName(RuntimeVariants.Cu130));
        Assert.Equal("runtime-rocm-gfx1151", RuntimeVariants.LedgerName(RuntimeVariants.RocmGfx1151));
    }

    [Fact]
    public void 精度はdevice連動でrocmは上書きさせない()
    {
        Assert.Equal("bf16", RuntimeVariants.DefaultPrecision(RuntimeVariants.Cu130));
        Assert.Equal("fp32", RuntimeVariants.DefaultPrecision(RuntimeVariants.Cpu));
        Assert.False(RuntimeVariants.AllowsPrecisionOverride(RuntimeVariants.RocmGfx1151));
        Assert.True(RuntimeVariants.AllowsPrecisionOverride(RuntimeVariants.Cu130));
    }
}

/// <summary>起動 env の組み立て（設計書 §2）。</summary>
public sealed class ServerEnvironmentTests
{
    private static AppPaths Paths() => new(
        @"C:\app", @"C:\app", @"C:\data\runtime", @"C:\data", developerMode: false);

    [Fact]
    public void deviceはmodelとcodecの2本に同時に載る()
    {
        var settings = new LauncherSettings { Variant = RuntimeVariants.Cu130 };
        var env = ServerEnvironment.Build(settings, Paths(), gpuIndex: 1);

        Assert.Equal("cuda:1", env[ServerEnvironment.ModelDevice]);
        Assert.Equal("cuda:1", env[ServerEnvironment.CodecDevice]);
    }

    [Fact]
    public void rocmでもcuda_Nを名乗る()
    {
        // ROCm の torch も device type を cuda と名乗る（契約 ⑹）
        Assert.Equal("cuda:0", ServerEnvironment.DeviceString(RuntimeVariants.RocmGfx1151, 0));
        Assert.Equal("cpu", ServerEnvironment.DeviceString(RuntimeVariants.Cpu, null));
    }

    [Fact]
    public void 精度は既定では載せない()
    {
        var env = ServerEnvironment.Build(new LauncherSettings(), Paths(), 0);
        Assert.False(env.ContainsKey(ServerEnvironment.ModelPrecision));
        Assert.False(env.ContainsKey(ServerEnvironment.CodecPrecision));
    }

    [Fact]
    public void rocm変種にfp32を載せない()
    {
        var settings = new LauncherSettings { Variant = RuntimeVariants.RocmGfx1151, Precision = "fp32" };
        var env = ServerEnvironment.Build(settings, Paths(), 0);

        Assert.False(env.ContainsKey(ServerEnvironment.ModelPrecision));
        Assert.Equal("rocm-gfx1151", env[ServerEnvironment.Variant]);
    }

    [Fact]
    public void wrapperが焼く既定を載せ直さない()
    {
        var env = ServerEnvironment.Build(new LauncherSettings(), Paths(), 0);

        // 二重定義を作らない（apply_env_defaults の setdefault に任せる欄）
        Assert.False(env.ContainsKey("IRODORI_HF_CHECKPOINT"));
        Assert.False(env.ContainsKey("IRODORI_PRELOAD"));
        Assert.False(env.ContainsKey("IRODORI_ALLOW_NO_REF_VOICE"));
        Assert.False(env.ContainsKey("IRODORI_DEFAULT_VOICE"));
        Assert.False(env.ContainsKey("IRODORI_DEFAULT_NUM_STEPS"));
    }

    [Fact]
    public void 暖機は設定が立っているときだけenvに出る()
    {
        var off = ServerEnvironment.Build(new LauncherSettings(), Paths(), 0);
        Assert.False(off.ContainsKey(ServerEnvironment.WarmupOnStart));

        var settings = new LauncherSettings
        {
            WarmupOnStart = true,
            WarmupStages = [4, 8, 12],
            WarmupVoices = ["デフォルト", "琴葉茜"],
        };
        var on = ServerEnvironment.Build(settings, Paths(), 0);

        Assert.Equal("1", on[ServerEnvironment.WarmupOnStart]);
        Assert.Equal("4,8,12", on[ServerEnvironment.WarmupStages]);
        Assert.Equal("デフォルト,琴葉茜", on[ServerEnvironment.WarmupVoices]);
    }

    [Fact]
    public void 事前計算は明示したときだけenvに出る()
    {
        var unset = ServerEnvironment.Build(
            new LauncherSettings { Variant = RuntimeVariants.RocmGfx1151 }, Paths(), 0);
        // 未設定なら wrapper が変種で決める（rocm-* は 1）
        Assert.False(unset.ContainsKey(ServerEnvironment.PrecomputeOnStart));

        var off = ServerEnvironment.Build(
            new LauncherSettings { Variant = RuntimeVariants.RocmGfx1151, PrecomputeOnStart = false },
            Paths(), 0);
        Assert.Equal("0", off[ServerEnvironment.PrecomputeOnStart]);
    }

    [Fact]
    public void 場所は4本とも絶対パスで載る()
    {
        var env = ServerEnvironment.Build(new LauncherSettings(), Paths(), 0);

        Assert.Equal(@"C:\data", env[ServerEnvironment.DataDir]);
        Assert.Equal(@"C:\data\voices", env[ServerEnvironment.VoicesDir]);
        Assert.Equal(@"C:\data\voices\voices.json", env[ServerEnvironment.VoiceAliasesFile]);
        Assert.Equal(@"C:\data\models", env[ServerEnvironment.HfHome]);
        Assert.Equal("1", env[ServerEnvironment.HfHubOffline]);
    }
}

/// <summary>stderr の 3 行＋device 行から状態を読む（受け入れ条件 D-4）。</summary>
public sealed class ServerLogParserTests
{
    private const string Banner =
        "ywk_server 0.1.0 upstream=8224daf/841fb7c variant=rocm-gfx1151 device=cuda:0/cuda:0 "
        + "precision=bf16 miopen=miopen/db";

    [Fact]
    public void 起動1行目から版と上流と変種を読む()
    {
        var read = ServerLogParser.Classify(Banner);

        Assert.Equal(ServerLogSignal.Banner, read.Signal);
        Assert.Equal("0.1.0", read.Version);
        Assert.Equal("8224daf/841fb7c", read.Upstream);
        Assert.Equal("rocm-gfx1151", read.Variant);
        Assert.Equal("cuda:0/cuda:0", read.Device);
        Assert.Equal("bf16", read.Precision);
    }

    [Fact]
    public void listenと読込完了と実deviceを見分ける()
    {
        Assert.Equal(
            ServerLogSignal.Listening,
            ServerLogParser.Classify("INFO:     Uvicorn running on http://127.0.0.1:18088 (Press CTRL+C to quit)").Signal);
        Assert.Equal(
            ServerLogSignal.RuntimeLoaded,
            ServerLogParser.Classify("INFO:irodori_openai_tts.app:runtime loaded in 18.98s").Signal);

        var device = ServerLogParser.Classify("ywk_server: device actual=cuda:0");
        Assert.Equal(ServerLogSignal.DeviceActual, device.Signal);
        Assert.Equal("cuda:0", device.Device);
    }

    [Fact]
    public void 状態は前へしか進まない()
    {
        Assert.Equal(ServerState.Starting, ServerLogParser.NextState(ServerState.Stopped, ServerLogSignal.Banner));
        Assert.Equal(ServerState.Listening, ServerLogParser.NextState(ServerState.Starting, ServerLogSignal.Listening));

        // **`runtime loaded in` は状態を動かさない**（是正・2026-09-05）＝上流は uvicorn の
        // lifespan で載せるのでこの 1 行は bind より先に出る。Ready は /health・/ywk/status の
        // runtime.loaded=true だけが立てる（契約 ⑵）。
        Assert.Null(ServerLogParser.NextState(ServerState.Starting, ServerLogSignal.RuntimeLoaded));
        Assert.Null(ServerLogParser.NextState(ServerState.Listening, ServerLogSignal.RuntimeLoaded));
        Assert.Null(ServerLogParser.NextState(ServerState.Warming, ServerLogSignal.RuntimeLoaded));

        // Ready の後に Uvicorn の行が流れても戻さない
        Assert.Null(ServerLogParser.NextState(ServerState.Ready, ServerLogSignal.Listening));
        // Failed からは動かさない
        Assert.Null(ServerLogParser.NextState(ServerState.Failed, ServerLogSignal.Banner));
    }

    [Fact]
    public void 長い行は畳んでログに出す()
    {
        // 422 の本文 echo は 1 発 5 KB（受け入れ条件 D-4＝ログに流さない）
        var echo = new string('x', 5000);
        Assert.True(ServerLogParser.LooksLikeRequestEcho(echo));

        var folded = ServerLogParser.ForLog(echo);
        Assert.True(folded.Length < echo.Length);
        Assert.Contains("5000", folded, StringComparison.Ordinal);
    }

    [Fact]
    public void 終了コードは理由1行になる()
    {
        var two = ServerExitCodes.Describe(
            ServerExitCodes.WrapperPrecheck,
            "ywk_server: IRODORI_MODEL_PRECISION=fp32 is not supported on variant rocm-gfx1151");
        Assert.Contains("起動前の検査", two, StringComparison.Ordinal);
        Assert.Contains("fp32", two, StringComparison.Ordinal);

        Assert.Contains("モデルの読み込み", ServerExitCodes.Describe(ServerExitCodes.UpstreamStartup, null),
            StringComparison.Ordinal);
    }
}

/// <summary>UUID→index の解決とドライバ検査（裁定 34・決定 4）。</summary>
public sealed class GpuTests
{
    private static readonly GpuInfo[] Gpus =
    [
        new("GPU-19adfe89-c9e0-df55-4a8a-e31798717a36", "NVIDIA GeForce RTX 3090", 0,
            25_769_803_776, "0000:01:00.0", null, "591.86", GpuSource.NvidiaSmi),
        new("GPU-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "AMD Radeon(TM) 8060S Graphics", 1,
            107_090_132_992, "0000:65:00.0", "gfx1151", null, GpuSource.TorchProbe),
    ];

    [Fact]
    public void UUIDからindexを解決する()
    {
        Assert.Equal(0, GpuResolver.ResolveIndex(Gpus, "GPU-19adfe89-c9e0-df55-4a8a-e31798717a36"));
        // GPU- の前置の有無と大小は吸収する（nvidia-smi と torch で綴りが違う）
        Assert.Equal(1, GpuResolver.ResolveIndex(Gpus, "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"));
        Assert.Null(GpuResolver.ResolveIndex(Gpus, "GPU-00000000-0000-0000-0000-000000000000"));
        Assert.Null(GpuResolver.ResolveIndex(Gpus, null));
    }

    [Fact]
    public void 見つからないときは選び直しを促す()
    {
        var message = GpuResolver.NotFoundMessage("NVIDIA GeForce RTX 3090", "GPU-1234");
        Assert.Contains("見つかりません", message, StringComparison.Ordinal);
        Assert.Contains("選び直して", message, StringComparison.Ordinal);
    }

    [Fact]
    public void cu130はドライバ580未満で止めてcu126を勧める()
    {
        var check = new DriverRequirement();

        Assert.True(check.Check(RuntimeVariants.Cu130, "591.86").Ok);
        var low = check.Check(RuntimeVariants.Cu130, "537.13");
        Assert.False(low.Ok);
        Assert.Equal(RuntimeVariants.Cu126, low.SuggestedVariant);
        Assert.Equal(DriverRequirement.Cu130Minimum, low.RequiredMinimum);
    }

    [Fact]
    public void cu126の下限は528_33()
    {
        // 裁定 88 ⑵（便 B の U-14 で 560.76 から改めた）＝537.58 は実射で通した版（裁定 80）。
        var check = new DriverRequirement();
        Assert.True(check.Check(RuntimeVariants.Cu126, "528.33").Ok);
        Assert.True(check.Check(RuntimeVariants.Cu126, "537.58").Ok);
        Assert.False(check.Check(RuntimeVariants.Cu126, "528.32").Ok);
    }

    [Fact]
    public void 版が読めなければ止めずに名乗る()
    {
        var check = new DriverRequirement();
        var verdict = check.Check(RuntimeVariants.Cu130, null);

        Assert.True(verdict.Ok);
        Assert.True(verdict.Unknown);
    }

    [Fact]
    public void rocmとcpuには下限が無い()
    {
        Assert.Null(DriverRequirement.Minimum(RuntimeVariants.RocmGfx1151));
        Assert.Null(DriverRequirement.Minimum(RuntimeVariants.Cpu));
    }
}

/// <summary><c>python312._pth</c> の生成（<c>build/assemble-runtime.ps1</c> §3 と同じ規則）。</summary>
public sealed class PthTemplateTests
{
    private const string Template =
        "python312.zip\r\n.\r\n@RUNTIME_DIR@/site-packages\r\n@APP_DIR@/server\r\n"
        + "@APP_DIR@/server/upstream/Irodori-TTS\r\n@APP_DIR@/server/upstream/Irodori-TTS-Server/src\r\n";

    [Fact]
    public void 差し込み口が絶対パスに変わる()
    {
        var text = PthTemplate.Render(Template, @"C:\data\runtime\cu130", @"C:\app");

        Assert.Contains("C:/data/runtime/cu130/site-packages", text, StringComparison.Ordinal);
        Assert.Contains("C:/app/server/upstream/Irodori-TTS-Server/src", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@", text, StringComparison.Ordinal);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", text, StringComparison.Ordinal);
    }

    [Fact]
    public void import_siteを持つ雛形は拒む()
    {
        // import site が入ると .pth が読まれ、protobuf の nspkg 問題が戻る（ledger/README §4-4）
        var bad = Template + "import site\r\n";
        Assert.Throws<InvalidOperationException>(
            () => PthTemplate.Render(bad, @"C:\r", @"C:\a"));
    }

    [Fact]
    public void 埋め残しは拒む()
    {
        var bad = Template + "@SOMETHING@/x\r\n";
        Assert.Throws<InvalidOperationException>(
            () => PthTemplate.Render(bad, @"C:\r", @"C:\a"));
    }

    [Fact]
    public void パスはposix形で末尾の区切りを落とす()
    {
        Assert.Equal("C:/data/runtime", PthTemplate.ToPosixPath(@"C:\data\runtime\"));
        Assert.Equal("C:/", PthTemplate.ToPosixPath(@"C:\"));
    }
}

/// <summary>話者の並びと参照なしの別名（契約 ⑷）。</summary>
public sealed class VoiceIdsTests
{
    [Fact]
    public void デフォルトが先頭に来る()
    {
        var ordered = VoiceIds.Order(["琴葉茜", "デフォルト", "月読アイ"], preferred: null);
        Assert.Equal(VoiceIds.Default, ordered[0]);
        Assert.Equal(3, ordered.Count);
    }

    [Fact]
    public void 設定の並びが次に来る()
    {
        var ordered = VoiceIds.Order(
            ["琴葉茜", "デフォルト", "月読アイ", "つくよみちゃん"],
            preferred: ["月読アイ", "つくよみちゃん"]);

        Assert.Equal(new[] { "デフォルト", "月読アイ", "つくよみちゃん", "琴葉茜" }, ordered);
    }

    [Fact]
    public void 参照なしの別名を見分ける()
    {
        Assert.True(VoiceIds.IsNoRef("デフォルト"));
        Assert.True(VoiceIds.IsNoRef("none"));
        Assert.True(VoiceIds.IsNoRef("NO_REF"));
        Assert.False(VoiceIds.IsNoRef("琴葉茜"));
        Assert.False(VoiceIds.IsNoRef(null));
    }
}

/// <summary>取得台帳を実檔で読む（<c>ledger/*.json</c> が在る機体でだけ走る）。</summary>
public sealed class LedgerReadTests
{
    /// <summary>リポの <c>ledger/</c> を探す（テストの出力先から上へ辿る）。</summary>
    private static string? FindLedgerDir()
    {
        string? dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "ledger");
            if (File.Exists(Path.Combine(candidate, "python-embed.json")))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    [Fact]
    public void 実物の台帳が型に落ちる()
    {
        var ledgerDir = FindLedgerDir();
        if (ledgerDir is null)
        {
            return; // リポ外で走らせた＝この検分は飛ばす
        }

        var options = JsonSettingsStore.JsonOptions;

        var embed = System.Text.Json.JsonSerializer.Deserialize<LedgerFile>(
            File.ReadAllText(Path.Combine(ledgerDir, "python-embed.json")), options);
        Assert.NotNull(embed);
        Assert.NotNull(embed.PythonEmbed);
        Assert.Equal("3.12.10", embed.PythonEmbed!.Version);
        Assert.Equal("python-3.12.10-embed-amd64.zip", embed.PythonEmbed.EffectiveFileName);

        var vc = System.Text.Json.JsonSerializer.Deserialize<VcRedistLedger>(
            File.ReadAllText(Path.Combine(ledgerDir, "vc_redist.json")), options);
        Assert.NotNull(vc);
        Assert.NotNull(vc.Installer);
        // url と fallback_url の 2 本を持つ（aka.ms は中身が動くので fallback 側）
        Assert.Equal(2, vc.Installer!.Urls.Count);
        Assert.NotEmpty(vc.Installer.SilentArgs);

        var models = System.Text.Json.JsonSerializer.Deserialize<ModelsLedger>(
            File.ReadAllText(Path.Combine(ledgerDir, "models.json")), options);
        Assert.NotNull(models);
        Assert.NotEmpty(models.Repos);
        // 非 LFS の檔は sha256 を持たず git blob sha1 で検証する
        Assert.Contains(models.Repos.SelectMany(r => r.Files), f => f.Verify == "git-blob-sha1");

        var rocmPath = Path.Combine(ledgerDir, "runtime-rocm-gfx1151.json");
        if (File.Exists(rocmPath))
        {
            var rocm = System.Text.Json.JsonSerializer.Deserialize<LedgerFile>(
                File.ReadAllText(rocmPath), options);
            Assert.NotNull(rocm);
            Assert.True(rocm.CountMatches);
            Assert.Contains(rocm.Items, i => i.Kind == LedgerItemKinds.Sdist && i.EffectivePackageDirs.Count > 0);
            Assert.Contains(rocm.Items, i => i.Kind == LedgerItemKinds.Archive && i.EffectivePackageDirs.Count > 0);
            // license 欄は空にしない（build/check-licenses.ps1 と同じ検分）
            Assert.All(rocm.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.License)));
        }
    }
}
