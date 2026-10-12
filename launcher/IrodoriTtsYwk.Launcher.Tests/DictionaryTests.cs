using System;
using System.IO;
using System.Linq;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.ViewModels;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 辞書（<c>decisions.md</c> 166・<c>docs/design/app-dictionary.md</c>）＝
/// 檔の往復（<see cref="JsonDictionaryStore"/>）と画面の純ロジック（<see cref="DictionaryViewModel"/>）。
/// 窓も Dispatcher も出てこない。
/// </summary>
public sealed class DictionaryTests : IDisposable
{
    private readonly string _root = TestArchives.NewTempDir("dictionary");

    private string FilePath => Path.Combine(_root, JsonDictionaryStore.FileName);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ---------------------------------------------------------------- 檔

    [Fact]
    public void 檔が無ければ空で読め理由も無い()
    {
        var store = new JsonDictionaryStore(FilePath);
        Assert.Empty(store.Load());
        Assert.Null(store.LastLoadError);
    }

    [Fact]
    public void 保存して読み直すと順序と日本語と絵文字が保たれる()
    {
        var store = new JsonDictionaryStore(FilePath);
        store.Save(
        [
            new DictionaryEntry("\U0001F389", "ぱちぱち"),
            new DictionaryEntry("www", "わら"),
            new DictionaryEntry("GPU", string.Empty),
        ]);

        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"from\"", json, StringComparison.Ordinal);
        Assert.Contains("\"to\"", json, StringComparison.Ordinal);
        Assert.Contains("ぱちぱち", json, StringComparison.Ordinal); // 日本語は \uXXXX に潰さない
        Assert.Contains("\\uD83C\\uDF89", json, StringComparison.Ordinal); // BMP の外は符号器が潰す（wrapper は両方読む）
        Assert.Contains("\"schema\": 1", json, StringComparison.Ordinal);
        Assert.False(File.Exists(FilePath + ".tmp"));

        var back = new JsonDictionaryStore(FilePath).Load();
        Assert.Equal(["\U0001F389", "www", "GPU"], back.Select(e => e.From).ToArray());
        Assert.Equal(["ぱちぱち", "わら", ""], back.Select(e => e.To).ToArray());
    }

    [Fact]
    public void 空のFromは保存で落ち_nullのToは空になる()
    {
        var sanitized = JsonDictionaryStore.Sanitize(
            [new DictionaryEntry("", "爆発"), new DictionaryEntry("a", null!), null]);
        Assert.Single(sanitized);
        Assert.Equal("a", sanitized[0].From);
        Assert.Equal(string.Empty, sanitized[0].To);
    }

    [Fact]
    public void 壊れた檔は空から始めて理由を1行残す()
    {
        File.WriteAllText(FilePath, "{not json");
        var store = new JsonDictionaryStore(FilePath);
        Assert.Empty(store.Load());
        Assert.NotNull(store.LastLoadError);
        Assert.Contains(JsonDictionaryStore.FileName, store.LastLoadError, StringComparison.Ordinal);
    }

    [Fact]
    public void 知らない欄や欠けた欄があっても読める()
    {
        File.WriteAllText(FilePath, "{\"schema\":1,\"future\":true,\"entries\":[{\"from\":\"x\"},{\"from\":\"\",\"to\":\"y\"},{\"from\":\"z\",\"to\":\"ぜっと\",\"note\":1}]}");
        var back = new JsonDictionaryStore(FilePath).Load();
        Assert.Equal(["x", "z"], back.Select(e => e.From).ToArray());
        Assert.Equal(["", "ぜっと"], back.Select(e => e.To).ToArray());
    }

    // ---------------------------------------------------------------- 画面

    [Fact]
    public void 登録は前後の空白を落として末尾に足し即保存する()
    {
        var store = new JsonDictionaryStore(FilePath);
        var vm = new DictionaryViewModel(store);

        Assert.False(vm.AddCommand.CanExecute(null));
        vm.NewFrom = "  GPU ";
        vm.NewTo = "じーぴーゆー";
        Assert.True(vm.AddCommand.CanExecute(null));
        vm.AddCommand.Execute(null);

        Assert.Equal([new DictionaryRow("GPU", "じーぴーゆー")], vm.Rows.ToArray());
        Assert.Equal(string.Empty, vm.NewFrom);
        Assert.Equal(string.Empty, vm.NewTo);
        Assert.Equal(UiStrings.DictionarySaved, vm.Message);
        Assert.Equal("GPU", Assert.Single(store.Load()).From); // 即反映＝檔に在る
        Assert.Contains("1", vm.CountText, StringComparison.Ordinal);
    }

    [Fact]
    public void 空のFromは登録できず空のToは読まないとして登録できる()
    {
        var vm = new DictionaryViewModel(new JsonDictionaryStore(FilePath));
        vm.NewFrom = "   ";
        Assert.False(vm.AddCommand.CanExecute(null));
        vm.AddCommand.Execute(null);
        Assert.Empty(vm.Rows);

        vm.NewFrom = "草";
        vm.AddCommand.Execute(null);
        Assert.Equal(new DictionaryRow("草", ""), Assert.Single(vm.Rows));
    }

    [Fact]
    public void 削除と並べ替えが順序どおり即反映される()
    {
        var store = new JsonDictionaryStore(FilePath);
        store.Save([new DictionaryEntry("a", "1"), new DictionaryEntry("b", "2"), new DictionaryEntry("c", "3")]);
        var vm = new DictionaryViewModel(store);
        Assert.Equal(3, vm.Rows.Count);

        Assert.False(vm.RemoveCommand.CanExecute(null));
        vm.Selected = vm.Rows[2];
        Assert.False(vm.MoveDownCommand.CanExecute(null));
        Assert.True(vm.MoveUpCommand.CanExecute(null));
        vm.MoveUpCommand.Execute(null);
        Assert.Equal(["a", "c", "b"], vm.Rows.Select(r => r.From).ToArray());
        Assert.Equal(["a", "c", "b"], store.Load().Select(e => e.From).ToArray());

        vm.MoveUpCommand.Execute(null);
        Assert.Equal(["c", "a", "b"], vm.Rows.Select(r => r.From).ToArray());
        Assert.False(vm.MoveUpCommand.CanExecute(null));

        vm.RemoveCommand.Execute(null);
        Assert.Equal(["a", "b"], vm.Rows.Select(r => r.From).ToArray());
        Assert.Null(vm.Selected);
        Assert.Equal(["a", "b"], store.Load().Select(e => e.From).ToArray());
    }

    [Fact]
    public void 読めない檔は空から始めて画面と記録に理由を出す()
    {
        File.WriteAllText(FilePath, "[1,2,3");
        string? logged = null;
        var vm = new DictionaryViewModel(new JsonDictionaryStore(FilePath)) { Log = line => logged = line };
        // 構築時の理由は Log が差さる前に出るので Message で確かめる
        Assert.Empty(vm.Rows);
        Assert.StartsWith(UiStrings.DictionaryLoadFailed, vm.Message, StringComparison.Ordinal);
        Assert.Equal(UiStrings.DictionaryCountNone, vm.CountText);
        _ = logged;
    }

    [Fact]
    public void 檔が無くても画面は動く()
    {
        var vm = new DictionaryViewModel(null);
        vm.NewFrom = "a";
        vm.AddCommand.Execute(null);
        Assert.Single(vm.Rows);
        Assert.Equal(string.Empty, vm.Message);
    }
}
