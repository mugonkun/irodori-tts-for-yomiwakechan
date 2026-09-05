using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Audio;
using IrodoriTtsYwk.Launcher.Contracts;
using IrodoriTtsYwk.Launcher.Mvvm;
using IrodoriTtsYwk.Launcher.Services.Voices;

namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// 話者の画面（受け入れ条件 D-3・裁定 65・67）。
/// <para>
/// <b>ここが <c>voices/</c> と <c>voices.json</c> の唯一の書き手</b>（契約 ⑷・裁定 16）＝
/// 上流の登録 API は使わない（ASCII 限定で日本語名が 400 になる）。追加は
/// <b>wav を選ぶ→名前を付ける</b>の 2 操作で、<b>再起動は要らない</b>（上流は毎要求
/// <c>iterdir()</c>）。
/// </para>
/// <para>
/// <b>取れる物と取れない物を混ぜない</b>＝<c>latent</c>／<c>latent_stale</c> は走っている
/// wrapper しか知らないので、サーバが止まっていれば台帳の <c>ref_latent</c> だけで一覧を組む
/// （一覧は必ず出る）。<c>POST /ywk/voices/precompute</c> は便 C（2）が追加中なので
/// <b>無ければ無いものとして動く</b>（<see cref="PrecomputeSupported"/> が偽になり、欄を伏せる）。
/// </para>
/// </summary>
public sealed class VoicesViewModel : ObservableObject
{
    /// <summary>
    /// 「事前計算が走行中」の機械可読な code（<c>server/ywk_server.py</c> が 409 で返す）。
    /// 文言では判定しない（契約 ⑶ 3-3）。
    /// </summary>
    public const string PrecomputeRunningCode = "ywk_precompute_running";


    private readonly IVoiceStore? _store;
    private readonly IVoicesJsonWriter? _writer;
    private readonly IAudioPlayer _player;
    private readonly Func<IWrapperClient?> _wrapper;
    private readonly AppPaths _paths;
    private readonly LauncherSettings _settings;

    private VoicesYwkFile? _file;
    private VoicesResponse? _live;
    private MemoryStatus? _memory;
    private VoiceRow? _selected;
    private string _message = string.Empty;
    private bool _precomputeSupported = true;
    private bool _busy;

    /// <summary>
    /// 走行中（409 <c>ywk_precompute_running</c>）で断られた焼き＝口が空いたら出し直す
    /// （<see cref="ApplyPrecompute"/>・統合席 §19）。
    /// </summary>
    private readonly List<string> _pendingPrecompute = [];

    public VoicesViewModel(
        IVoiceStore? store,
        IVoicesJsonWriter? writer,
        IAudioPlayer player,
        Func<IWrapperClient?> wrapper,
        AppPaths paths,
        LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(wrapper);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settings);

        _store = store;
        _writer = writer;
        _player = player;
        _wrapper = wrapper;
        _paths = paths;
        _settings = settings;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        PreviewCommand = new RelayCommand(Preview, () => Selected?.CanPreview == true);
        RemoveCommand = new AsyncRelayCommand(RemoveSelectedAsync, () => Selected?.CanRemove == true);
        PrecomputeCommand = new AsyncRelayCommand(PrecomputeAsync, () => PrecomputeSupported);
    }

    /// <summary>
    /// 削除の確認（<b>窓が要る仕事なので View が差す</b>＝低 14 で削除を
    /// <see cref="RemoveCommand"/> に寄せたときに、確認窓を落とさないための継ぎ目）。
    /// 差されていなければ確認なしで進む（xUnit から素で叩ける）。
    /// </summary>
    public Func<VoiceRow, bool>? ConfirmRemove { get; set; }

    /// <summary>一覧（「デフォルト」が先頭に常在＝受け入れ条件 D-3）。</summary>
    public ObservableCollection<VoiceRow> Rows { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public RelayCommand PreviewCommand { get; }

    public AsyncRelayCommand RemoveCommand { get; }

    /// <summary>焼いていない／古い話者の参照潜在を焼く（裁定 65）。</summary>
    public AsyncRelayCommand PrecomputeCommand { get; }

    /// <summary>一覧が更新されたことを窓へ告げる（状態帯の概算メモリを引き直す）。</summary>
    public event EventHandler<IReadOnlyList<VoiceRow>>? RowsChanged;

    /// <summary>
    /// 選んでいる話者が変わったことを告げる（<b>状態帯の「1 名あたり」が主役</b>＝low 13）。
    /// </summary>
    public event EventHandler<VoiceRow?>? SelectionChanged;

    public VoiceRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                PreviewCommand.RaiseCanExecuteChanged();
                RemoveCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(SelectedMemoryText));
                RaisePropertyChanged(nameof(PreviewBlockedText));
                RaisePropertyChanged(nameof(RemoveBlockedText));
                SelectionChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// 選んだ話者の消費メモリ（裁定 67 ⑵・low 13）＝<b>1 名あたり</b>。
    /// 焼いてあれば <c>/ywk/status.memory.latents[id]</c> の実サイズを出す。
    /// </summary>
    public string SelectedMemoryText => Selected is null
        ? UiText.Missing
        : "1 名あたり＝" + Selected.MemoryText;

    /// <summary>
    /// 試聴が押せない理由 1 行（押せるなら空＝low 11）。<b>UIA から読める</b>ように、
    /// 画面はこれを 1 行の表示とボタンの <c>HelpText</c> の両方に出す。
    /// </summary>
    public string PreviewBlockedText => Selected?.PreviewBlockedReason ?? string.Empty;

    /// <summary>削除が押せない理由 1 行（押せるなら空＝low 14）。</summary>
    public string RemoveBlockedText => Selected?.RemoveBlockedReason ?? string.Empty;

    /// <summary>画面下の 1 行（結果・理由）。</summary>
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary><c>POST /ywk/voices/precompute</c> が在るか（無ければ欄を伏せる）。</summary>
    public bool PrecomputeSupported
    {
        get => _precomputeSupported;
        private set
        {
            if (SetProperty(ref _precomputeSupported, value))
            {
                PrecomputeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>参照ボイスの但し書き（README §4＝Ethical Restrictions 1・No Impersonation）。</summary>
    public static string ImpersonationNotice =>
        "参照ボイスは、権利者の許諾がある音声だけを使ってください。"
        + "実在の人物の声を本人の許諾なく模倣することは、上流 Irodori-TTS の利用条件"
        + "（Ethical Restrictions 1・No Impersonation）で禁じられています。";

    /// <summary>
    /// 参照 wav の推奨の長さと、受ける形式の但し書き（設計書 §4）。
    /// <b>形式は <see cref="VoiceIds.WavExtensions"/> から作る</b>＝画面の文言・檔窓の絞り・
    /// 検分が同じ 1 つの定数を見る（是正・2026-09-05）。
    /// </summary>
    public static string ReferenceLengthNotice =>
        "参照 wav は 10〜30 秒を勧めます（" + VoiceIds.ExtensionsText + "）。";

    /// <summary>台帳を読み直して一覧を組む（サーバが止まっていても出る）。</summary>
    public void Reload()
    {
        _file = LoadStore();
        Rebuild();
    }

    /// <summary>
    /// <c>/ywk/status.memory</c> を受ける（裁定 87 ⑴）＝<c>latents</c> の実サイズを一覧に流す。
    /// <b>窓は状態機械が採った標本を配るだけ</b>で、ここから HTTP は 1 本も出ない（low 3）。
    /// </summary>
    public void ApplyMemory(MemoryStatus? memory)
    {
        var before = _memory;
        _memory = memory;

        // 焼いた表が変わったときだけ組み直す（2 秒ごとの標本で一覧を作り直さない）。
        if (!SameLatents(before, memory))
        {
            Rebuild();
        }
    }

    /// <summary>
    /// <c>/ywk/status.precompute</c> を受ける（統合席 §19）＝<b>走行中で断られた焼きを、
    /// 走行が終わったところで 1 度だけ出し直す</b>。
    /// <para>
    /// 実射で出た形（2026-09-05）＝<c>rocm-*</c> は ready の直後にプリセット 11 名の焼きを
    /// 自分で始める（裁定 78 ⑴）。その最中に話者を足すと
    /// <c>POST /ywk/voices/precompute</c> が <b>409 <c>ywk_precompute_running</c></b> で断られ、
    /// ランチャは理由を出したきり二度と出し直さないので、足した話者は
    /// <b>「未」のまま置き去りになる</b>（一覧には出るが 1 秒級の話者切替に戻る）。
    /// 走行が終われば口は空くので、覚えておいて出し直す。
    /// </para>
    /// <b>窓は標本を配るだけ</b>＝ここから出る HTTP は「口が空いた 1 回」だけである。
    /// </summary>
    public void ApplyPrecompute(PrecomputeStatus? precompute)
    {
        if (_pendingPrecompute.Count == 0)
        {
            return;
        }

        // 走行中は待つ（state が読めない＝口がまだ無い個体なら出し直さない）。
        if (precompute is null
            || string.Equals(precompute.State, "running", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var ids = _pendingPrecompute.ToArray();
        _pendingPrecompute.Clear();
        _ = PrecomputeAsync(ids);
    }

    /// <summary>潜在の表が同じか（<b>純関数</b>＝標本ごとの作り直しを止める判定）。</summary>
    private static bool SameLatents(MemoryStatus? left, MemoryStatus? right)
    {
        var a = left?.Latents;
        var b = right?.Latents;
        if (a is null || a.Count == 0)
        {
            return b is null || b.Count == 0;
        }

        if (b is null || a.Count != b.Count)
        {
            return false;
        }

        foreach (var pair in a)
        {
            if (!b.TryGetValue(pair.Key, out var bytes) || bytes != pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>台帳＋<c>/ywk/voices</c> を読み直す。</summary>
    public async Task RefreshAsync()
    {
        _file = LoadStore();

        var client = _wrapper();
        if (client is not null)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var result = await client.GetVoicesAsync(cts.Token).ConfigureAwait(true);
                if (result.Ok && result.Value is not null)
                {
                    _live = result.Value;
                    if (!string.IsNullOrWhiteSpace(result.Value.Error))
                    {
                        Message = "サーバの話者一覧に問題があります：" + result.Value.Error.Trim();
                    }
                }
                else
                {
                    // low 12＝**読めなかったら古い写しを捨てる**。500 や到達不能のときに
                    // 前回の一覧を残すと、消えた話者・焼き直した印がそのまま画面に居座る。
                    _live = null;
                    Message = DescribeVoicesFailure(result.StatusCode);
                }
            }
            catch (OperationCanceledException)
            {
                _live = null;
                Message = "サーバの話者一覧が期限内に返りませんでした。台帳だけで一覧を出しています。";
            }
        }
        else
        {
            _live = null;
        }

        Rebuild();
    }

    /// <summary>
    /// <c>/ywk/voices</c> が読めなかったときの 1 行（<b>純関数</b>＝low 12）。
    /// <b>台帳だけで一覧を出している</b>ことを必ず言う（黙って古い写しを見せない）。
    /// </summary>
    public static string DescribeVoicesFailure(int statusCode) =>
        statusCode > 0
            ? "サーバの話者一覧が読めませんでした（HTTP "
              + statusCode.ToString(CultureInfo.InvariantCulture)
              + "）。台帳だけで一覧を出しています。"
            : "サーバの話者一覧が読めませんでした（応答なし）。台帳だけで一覧を出しています。";

    /// <summary>
    /// 追加＝wav を写して台帳に足し、<c>voices.json</c> を書き換える（受け入れ条件 D-3）。
    /// 檔を選ぶのは View（<c>OpenFileDialog</c>）の仕事で、ここは受けるだけ。
    /// </summary>
    public async Task<bool> AddVoiceAsync(string displayName, string sourcePath, string? caption)
    {
        if (_busy)
        {
            return false;
        }

        var fileError = VoiceNameValidator.ValidateSourceFile(sourcePath);
        if (fileError is not null)
        {
            Message = fileError;
            return false;
        }

        var nameError = VoiceNameValidator.Validate(displayName, Rows.Select(static r => r.Id));
        if (nameError is not null)
        {
            Message = nameError;
            return false;
        }

        if (_store is null || _writer is null)
        {
            Message = "話者の登録系がまだ組み込まれていません（便 D・話者席の実装待ち）。";
            return false;
        }

        _busy = true;
        try
        {
            _file = _store.AddVoice(displayName.Trim(), sourcePath, SpeechRequestBuilder.Fold(caption));
            _writer.Write(_paths.VoicesJsonPath, _file);
            Rebuild();

            var advice = AdviseLength(sourcePath);
            Message = "「" + displayName.Trim() + "」を追加しました（再起動は要りません）。"
                + (advice is null ? string.Empty : " " + advice);

            // 裁定 65＝Radeon 版は登録のたびに焼く。口が無ければ黙って飛ばす。
            if (_settings.EffectivePrecomputeOnStart())
            {
                await PrecomputeAsync([displayName.Trim()]).ConfigureAwait(true);
            }

            return true;
        }
        catch (IOException ex)
        {
            Message = "参照 wav を写せませんでした：" + ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Message = "参照 wav を写せませんでした：" + ex.Message;
            return false;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// 選んだ話者を消す（「デフォルト」は消せない）。
    /// <para>
    /// <b>順は 3 手</b>（裁定 78 ⑶・契約 ⑷ 4-3）＝⑴ サーバが居れば
    /// <c>DELETE /ywk/voices/{id}/latent</c> で別名を wav へ戻す ⑵ 台帳から外して
    /// 参照 wav・潜在・sidecar を消す ⑶ <c>voices.json</c> を書き直す。
    /// ⑴ を飛ばすと、<c>voices.json</c> に <c>ref_latent</c> が残ったまま <c>.pt</c> だけが
    /// 消える瞬間ができ、同じ名前で登録し直したときに前の話者の潜在がその場所に居座る
    /// （<c>&lt;stem&gt;</c> は話者 id 由来＝<c>server/ywk_server.py:2127-2139</c>）。
    /// </para>
    /// </summary>
    public async Task RemoveSelectedAsync()
    {
        var row = Selected;
        if (row is null)
        {
            return;
        }

        if (!row.CanRemove)
        {
            // 押せない理由は行が持つ（画面の 1 行・ボタンの HelpText と同じ文言＝low 14）
            Message = row.RemoveBlockedReason ?? "この話者は消せません。";
            return;
        }

        if (_store is null || _writer is null)
        {
            Message = "話者の登録系がまだ組み込まれていません（便 D・話者席の実装待ち）。";
            return;
        }

        // 確認窓は View が差す（差されていなければ確認なしで進む＝xUnit の継ぎ目）。
        if (ConfirmRemove is { } confirm && !confirm(row))
        {
            return;
        }

        // ⑴ 焼いた潜在を外す（サーバが居ないときは ⑵ の檔消しが引き受ける）。
        var latentNote = await DropLatentAsync(row.Id).ConfigureAwait(true);
        if (latentNote is not null)
        {
            Message = latentNote;
            return;
        }

        try
        {
            // ⑵ 台帳・参照 wav・潜在・sidecar
            var removal = _store.RemoveVoiceDetailed(row.Id);
            _file = removal.Table;

            // ⑶ voices.json
            _writer.Write(_paths.VoicesJsonPath, _file);
            Rebuild();

            Message = removal.WasKnown
                ? "「" + row.DisplayName + "」を削除しました。"
                : "「" + row.DisplayName + "」は台帳にありませんでした（何も消していません）。";
        }
        catch (IOException ex)
        {
            Message = "削除できませんでした：" + ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            Message = "削除できませんでした：" + ex.Message;
        }
    }

    /// <summary>
    /// <c>DELETE /ywk/voices/{id}/latent</c>（口が無い・サーバが居ないなら何もしない）。
    /// 戻り＝<b>削除を続けてはいけない</b>ときの理由 1 行（続けてよければ null）。
    /// </summary>
    private async Task<string?> DropLatentAsync(string voiceId)
    {
        var client = _wrapper();
        if (client is null)
        {
            return null; // サーバが止まっている＝檔は台帳側の路で消す
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await client.DropLatentAsync(voiceId, cts.Token).ConfigureAwait(true);

            if (result.Ok || !result.Available)
            {
                return null; // 外せた／口が無い（古い個体）＝続けてよい
            }

            // 404＝サーバがこの話者を知らない（台帳側で消せばよい）。
            if (result.StatusCode == 404 || result.Code == WrapperErrorCodes.UnknownVoice)
            {
                return null;
            }

            // 409＝事前計算が走っている。いま消すと走行が別名を書き戻す＝待ってもらう。
            if (result.Code == WrapperErrorCodes.PrecomputeRunning)
            {
                return "参照潜在の事前計算が走っています。終わってから削除してください。";
            }

            return "参照潜在を外せませんでした"
                + Suffix(result.Error?.Message ?? result.FailureReason);
        }
        catch (OperationCanceledException)
        {
            return "参照潜在を外す要求が期限内に返りませんでした。";
        }
    }

    /// <summary>
    /// 同梱のプリセットを入れ直す（設計書 §4「再インストールで戻る」＝是正・2026-09-05）。
    /// 既に在る id は飛ばすので、利用者が足した話者には触れない。
    /// </summary>
    public void RestorePresets()
    {
        if (_store is not VoiceStore store || _writer is null)
        {
            Message = "話者の登録系がまだ組み込まれていません（便 D・話者席の実装待ち）。";
            return;
        }

        try
        {
            var restored = PresetVoices.Restore(_paths, store);
            _file = LoadStore();
            _writer.Write(_paths.VoicesJsonPath, _file ?? VoiceStore.Empty());
            Rebuild();

            Message = restored > 0
                ? "同梱のプリセットを " + restored.ToString(CultureInfo.InvariantCulture) + " 名入れ直しました。"
                : "入れ直すプリセットはありませんでした（配布物にプリセットが無いか、既に全員居ます）。";
        }
        catch (IOException ex)
        {
            Message = "プリセットを入れ直せませんでした：" + ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            Message = "プリセットを入れ直せませんでした：" + ex.Message;
        }
    }

    /// <summary>
    /// 試聴（裁定 52＝再生時に −16 dBFS 相当へ揃える）。
    /// <b>鳴らせるのは wav だけ</b>で、それ以外は押せない＋理由 1 行（low 11）。
    /// </summary>
    public void Preview()
    {
        var row = Selected;
        if (row is null)
        {
            return;
        }

        if (!row.CanPreview || row.FileName is null)
        {
            Message = row.PreviewBlockedReason ?? "この話者は試聴できません。";
            return;
        }

        var result = _player.PlayFile(Path.Combine(_paths.ReferenceWavDir, row.FileName));
        Message = result.Ok
            ? "「" + row.DisplayName + "」を試聴中（" + UiText.Seconds(result.DurationSeconds) + "）。"
            : result.FailureReason ?? "試聴できませんでした。";
    }

    public void StopPreview() => _player.Stop();

    /// <summary>焼いていない／古い話者をまとめて焼く。</summary>
    public Task PrecomputeAsync() => PrecomputeAsync(VoiceRowBuilder.NeedsPrecompute(Rows));

    /// <summary>指定の話者を焼く。口が無ければ黙って伏せる（便 C（2）が追加中）。</summary>
    public async Task PrecomputeAsync(IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            Message = "焼き直しが要る話者はありません。";
            return;
        }

        var client = _wrapper();
        if (client is null)
        {
            Message = "サーバが動いていないので、参照潜在の事前計算はできません。";
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await client
                .StartPrecomputeAsync(new PrecomputeRequest(Ids: ids), cts.Token)
                .ConfigureAwait(true);

            if (!result.Available)
            {
                PrecomputeSupported = false;
                Message = "このサーバは参照潜在の事前計算に対応していません。";
                return;
            }

            PrecomputeSupported = true;
            if (result.Ok)
            {
                Message = "参照潜在の事前計算を始めました（"
                    + UiText.Progress(0, result.Value?.Total ?? ids.Count) + " 件）。";
                return;
            }

            // 走行中で断られた（409）＝**忘れない**。口が空いたら ApplyPrecompute が出し直す。
            // 文言では判定しない（契約 ⑶ 3-3＝機械可読な code で見る）。
            if (string.Equals(result.Code, PrecomputeRunningCode, StringComparison.Ordinal))
            {
                foreach (var id in ids)
                {
                    if (!_pendingPrecompute.Contains(id, StringComparer.Ordinal))
                    {
                        _pendingPrecompute.Add(id);
                    }
                }

                Message = "いま別の事前計算が走っているので、終わり次第この "
                    + ids.Count.ToString(CultureInfo.InvariantCulture) + " 名を焼きます。";
                return;
            }

            Message = "事前計算を始められませんでした" + Suffix(result.Error?.Message);
        }
        catch (OperationCanceledException)
        {
            Message = "事前計算の要求が期限内に返りませんでした。";
        }
    }

    private void Rebuild()
    {
        var rows = VoiceRowBuilder.Build(
            _file, _live, _settings.VoiceOrder as IReadOnlyList<string>, _memory);
        var selectedId = Selected?.Id;

        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        Selected = rows.FirstOrDefault(r => string.Equals(r.Id, selectedId, StringComparison.Ordinal))
            ?? rows.FirstOrDefault();

        RaisePropertyChanged(nameof(Rows));
        RowsChanged?.Invoke(this, rows);
    }

    private VoicesYwkFile? LoadStore()
    {
        if (_store is null)
        {
            return _file;
        }

        try
        {
            return _store.Load();
        }
        catch (IOException ex)
        {
            Message = "話者台帳が読めませんでした：" + ex.Message;
            return _file;
        }
        catch (UnauthorizedAccessException ex)
        {
            Message = "話者台帳が読めませんでした：" + ex.Message;
            return _file;
        }
    }

    private static string? AdviseLength(string sourcePath)
    {
        if (!string.Equals(Path.GetExtension(sourcePath), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            // 頭だけ読めば長さは判る（大きい檔を丸ごと読まない）
            using var stream = File.OpenRead(sourcePath);
            var head = new byte[Math.Min(4096, stream.Length)];
            var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            if (!WavInfo.TryRead(head.AsSpan(0, read), out var format) || format is null)
            {
                return null;
            }

            var blockAlign = format.BlockAlign;
            if (blockAlign <= 0 || format.SampleRate <= 0)
            {
                return null;
            }

            var dataBytes = stream.Length - format.DataOffset;
            var seconds = (double)dataBytes / blockAlign / format.SampleRate;
            return VoiceNameValidator.AdviseLength(seconds);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Suffix(string? message) =>
        string.IsNullOrWhiteSpace(message) ? "。" : "：" + message.Trim();
}
