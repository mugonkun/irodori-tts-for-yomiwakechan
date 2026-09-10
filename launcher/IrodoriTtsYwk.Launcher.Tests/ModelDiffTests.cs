using System.Collections.Generic;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Services.Models;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 段 E-2＝<b>声のデータ（モデル）の差分は見積り専用</b>（<c>v2-spec.md</c> §11-4）。
/// <b>檔を 1 つも動かさない</b>＝取得の道（<c>hf_hub_download</c> の blob 共有）は 1 行も替えない。
/// </summary>
public sealed class ModelDiffTests
{
    private static ModelsLedger Ledger(params ModelRepo[] repos) => new() { Repos = repos };

    private static ModelRepo Repo(string name, string revision, params ModelFile[] files) => new()
    {
        Repo = name,
        Revision = revision,
        Files = files,
    };

    private static ModelFile File(string path, string? sha, long size) => new()
    {
        Path = path,
        Sha256 = sha,
        Size = size,
        Verify = sha is null ? "git-blob-sha1" : "sha256",
    };

    [Fact]
    public void 落ちる量はshaが変わった檔のsizeの和()
    {
        var old = Ledger(Repo(
            "Aratako/Irodori-TTS-v4.1-Small", "aaa",
            File("model.safetensors", "1111", 3_000_000_000),
            File("dac.pth", "2222", 400_000_000)));
        var fresh = Ledger(Repo(
            "Aratako/Irodori-TTS-v4.1-Small", "bbb",
            File("model.safetensors", "1111", 3_000_000_000),   // 中身は同じ＝ネットに出ない
            File("dac.pth", "3333", 400_000_000)));             // 変わった＝これだけ落ちる

        var plan = ModelDiff.Plan(old, fresh);

        Assert.Equal(400_000_000, plan.Bytes);
        Assert.Equal(["Aratako/Irodori-TTS-v4.1-Small:dac.pth"], plan.Changed);
        Assert.Empty(plan.Added);
        Assert.True(plan.Any);
        Assert.Contains("381.5 MiB", ModelDiff.Summary(plan)!, System.StringComparison.Ordinal);
        Assert.Contains("1 檔", ModelDiff.Summary(plan)!, System.StringComparison.Ordinal);
    }

    [Fact]
    public void shaがnullの檔は見積りに数えない()
    {
        var old = Ledger(Repo("r", "aaa", File(".gitattributes", null, 1_000)));
        var fresh = Ledger(Repo(
            "r", "bbb",
            File(".gitattributes", null, 2_000),  // 非 LFS＝git blob sha1 でしか見られない
            File("README.md", null, 500)));

        var plan = ModelDiff.Plan(old, fresh);

        Assert.Equal(0, plan.Bytes);
        Assert.False(plan.Any);
        Assert.Equal(["r:.gitattributes", "r:README.md"], plan.Unknown);
        Assert.Null(ModelDiff.Summary(plan));
        Assert.Null(ModelDiff.DownloadNotice(plan));
    }

    [Fact]
    public void 写しが無い回は黙る_推測の数字を出さない()
    {
        var fresh = Ledger(Repo("r", "bbb", File("model.safetensors", "9999", 3_000_000_000)));

        var plan = ModelDiff.Plan(null, fresh);

        Assert.Equal(0, plan.Bytes);
        Assert.False(plan.Any);
        Assert.Empty(plan.Changed);
        Assert.Empty(plan.Added);
    }

    [Fact]
    public void 新しい台帳にだけ在る檔は落ちる分に数える()
    {
        var old = Ledger(Repo("r", "aaa", File("a.bin", "1111", 100)));
        var fresh = Ledger(
            Repo("r", "aaa", File("a.bin", "1111", 100)),
            Repo("s", "ccc", File("b.bin", "4444", 250)));

        var plan = ModelDiff.Plan(old, fresh);

        Assert.Equal(["s:b.bin"], plan.Added);
        Assert.Equal(250, plan.Bytes);
    }

    [Fact]
    public void 空の台帳どうしなら何も落ちない()
    {
        Assert.False(ModelDiff.Plan(Ledger(), Ledger()).Any);
        Assert.Equal(0, ModelDiff.Plan(Ledger(), Ledger()).Bytes);
    }
}
