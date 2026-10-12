using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Mvvm;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>対応表の 1 行（表示用。編集は行の入れ替え＝登録／削除／並べ替えで行う）。</summary>
public sealed record DictionaryRow(string From, string To)
{
    public override string ToString() => From + " → " + To;
}

/// <summary>
/// 辞書の画面（<c>decisions.md</c> 166・<c>docs/design/app-dictionary.md</c> §5）。
/// <para>
/// 対応表（文字 → 読み・<b>上から順に効く</b>ので並べ替えに意味がある）。本体 yomiwakechan2 の
/// 辞書設定と同じく<b>即反映</b>＝登録・削除・並べ替えのたびに <see cref="IDictionaryStore"/> へ書き、
/// 次の読み上げから効く（wrapper は要求ごとに檔の更新時刻を見る）。保存の釦は無い。
/// </para>
/// <para>
/// <b>窓も Dispatcher も触らない</b>＝xUnit から素で叩ける。檔が壊れていれば空から始め、
/// 理由は <see cref="Message"/> と <see cref="Log"/> に 1 行。
/// </para>
/// </summary>
public sealed class DictionaryViewModel : ObservableObject
{
    private readonly IDictionaryStore? _store;

    private DictionaryRow? _selected;
    private string _newFrom = string.Empty;
    private string _newTo = string.Empty;
    private string _message = string.Empty;

    /// <param name="store">辞書の檔（無ければ画面は出るが保存されない＝試験用）。</param>
    public DictionaryViewModel(IDictionaryStore? store)
    {
        _store = store;

        AddCommand = new RelayCommand(Add, () => !string.IsNullOrWhiteSpace(_newFrom));
        RemoveCommand = new RelayCommand(Remove, () => _selected is not null);
        MoveUpCommand = new RelayCommand(() => Move(-1), () => _selected is not null && Rows.IndexOf(_selected) > 0);
        MoveDownCommand = new RelayCommand(
            () => Move(+1),
            () => _selected is not null && Rows.IndexOf(_selected) is var i && i >= 0 && i < Rows.Count - 1);

        if (store is not null)
        {
            foreach (var entry in store.Load())
            {
                Rows.Add(new DictionaryRow(entry.From, entry.To));
            }

            if (store.LastLoadError is { } error)
            {
                Message = UiStrings.DictionaryLoadFailed + error;
                Log?.Invoke(Message);
            }
        }

        RaisePropertyChanged(nameof(CountText));
    }

    /// <summary>記録へ落とす手（状態タブのログ＝差さっていなければ落とさない）。</summary>
    public Action<string>? Log { get; set; }

    public ObservableCollection<DictionaryRow> Rows { get; } = [];

    public DictionaryRow? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
            {
                return;
            }

            RemoveCommand.RaiseCanExecuteChanged();
            MoveUpCommand.RaiseCanExecuteChanged();
            MoveDownCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>① 置き換える文字（前後の空白は登録時に落とす）。</summary>
    public string NewFrom
    {
        get => _newFrom;
        set
        {
            if (SetProperty(ref _newFrom, value ?? string.Empty))
            {
                AddCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>② 読み（空＝その文字は読まれない）。</summary>
    public string NewTo
    {
        get => _newTo;
        set => SetProperty(ref _newTo, value ?? string.Empty);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>一覧の下の薄字（件数）。</summary>
    public string CountText =>
        Rows.Count == 0
            ? UiStrings.DictionaryCountNone
            : string.Format(CultureInfo.InvariantCulture, UiStrings.DictionaryCountFormat, Rows.Count);

    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }

    /// <summary>いまの表（檔に書く形・順序つき）。</summary>
    public DictionaryEntry[] Entries => Rows.Select(static r => new DictionaryEntry(r.From, r.To)).ToArray();

    private void Add()
    {
        var from = _newFrom.Trim();
        if (from.Length == 0)
        {
            return;
        }

        var row = new DictionaryRow(from, _newTo);
        Rows.Add(row);
        NewFrom = string.Empty;
        NewTo = string.Empty;
        Selected = row;
        Commit();
    }

    private void Remove()
    {
        if (_selected is null)
        {
            return;
        }

        var index = Rows.IndexOf(_selected);
        if (index < 0)
        {
            return;
        }

        Rows.RemoveAt(index);
        Selected = null;
        Commit();
    }

    private void Move(int delta)
    {
        if (_selected is null)
        {
            return;
        }

        var index = Rows.IndexOf(_selected);
        var next = index + delta;
        if (index < 0 || next < 0 || next >= Rows.Count)
        {
            return;
        }

        Rows.Move(index, next);
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        Commit();
    }

    /// <summary>檔へ書く（即反映）。失敗は 1 行にして画面と記録へ。</summary>
    private void Commit()
    {
        RaisePropertyChanged(nameof(CountText));
        if (_store is null)
        {
            return;
        }

        try
        {
            _store.Save(Entries);
            Message = UiStrings.DictionarySaved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Message = UiStrings.DictionarySaveFailed + ex.Message;
            Log?.Invoke(Message);
        }
    }
}
