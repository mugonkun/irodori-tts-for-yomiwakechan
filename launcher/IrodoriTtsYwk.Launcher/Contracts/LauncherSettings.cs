using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>
/// <c>%LOCALAPPDATA%\irodori-tts-ywk\settings.json</c> の中身（契約 ⑻）。
/// <para>
/// <b>GPU は UUID で持つ</b>（裁定 34・契約 ⑹）＝index は再起動で変わりうるので、起動のたびに
/// <see cref="GpuResolver"/> で index へ解決する。見つからなければ「前回の GPU が見つからない」と
/// 告げて選び直させる（設計書 §3）。
/// </para>
/// <para>
/// null が「未設定」を意味する欄が 3 つある＝<see cref="Precision"/>（device 連動に任せる）・
/// <see cref="PrecomputeOnStart"/>（変種の既定に任せる＝裁定 65）・<see cref="HfHome"/>
/// （<c>&lt;data&gt;\models</c> に任せる）。「既定に戻す」は値を書かないことで表す。
/// </para>
/// <para>
/// WPF の束縛から書き換えるので <b>可変の POCO</b>（record ではない）。保存は原子的
/// （<see cref="JsonSettingsStore"/>）。
/// </para>
/// </summary>
public sealed class LauncherSettings
{
    /// <summary>この檔の形。欄を足すときは上げない・意味を変えるときだけ上げる（契約 ⑻ の規律）。</summary>
    [JsonPropertyName("schema")]
    public int Schema { get; set; } = 1;

    /// <summary>選んだ GPU の UUID（<c>GPU-xxxxxxxx-...</c>。CPU 変種なら null）。</summary>
    [JsonPropertyName("gpuUuid")]
    public string? GpuUuid { get; set; }

    /// <summary>選んだ GPU の名前（見つからないときの告知に使うだけ＝同定は UUID）。</summary>
    [JsonPropertyName("gpuName")]
    public string? GpuName { get; set; }

    /// <summary>
    /// <b>配られたままの変種</b>（＝利用者がまだ 1 度も選んでいない印・決裁 135 ⑵）。
    /// <see cref="Variant"/> の初期値そのものである＝2 箇所に綴らない。
    /// </summary>
    public const string DefaultVariant = RuntimeVariants.Cu130;

    /// <summary>取得台帳の変種名（<see cref="RuntimeVariants"/>）。<c>YWK_VARIANT</c> ではない。</summary>
    [JsonPropertyName("variant")]
    public string Variant { get; set; } = DefaultVariant;

    /// <summary>上級者設定。null＝device 連動（裁定 7）に任せる。</summary>
    [JsonPropertyName("precision")]
    public string? Precision { get; set; }

    /// <summary>既定 18088（裁定 2）。塞がっていたら止まって告知する（裁定 52＝次を探さない）。</summary>
    [JsonPropertyName("port")]
    public int Port { get; set; } = DefaultPort;

    /// <summary>利用者データの根の上書き。null＝<c>%LOCALAPPDATA%\irodori-tts-ywk</c>。</summary>
    [JsonPropertyName("dataDir")]
    public string? DataDir { get; set; }

    /// <summary><c>HF_HOME</c> の上書き。null＝<c>&lt;data&gt;\models</c>。</summary>
    [JsonPropertyName("hfHome")]
    public string? HfHome { get; set; }

    /// <summary>ready 直後に暖機を撃つか（Radeon 版は既定 ON＝設計書 §5）。</summary>
    [JsonPropertyName("warmup")]
    public bool WarmupOnStart { get; set; }

    /// <summary>暖機の段（秒・契約 ⑺ 7-2 の既定 <c>[4,8,12]</c>）。</summary>
    [JsonPropertyName("warmupStages")]
    public IList<double> WarmupStages { get; set; } = [4, 8, 12];

    /// <summary>暖機で撃つ話者（空＝段だけ）。最近使った 1〜3 名を入れる想定（設計書 §5）。</summary>
    [JsonPropertyName("warmupVoices")]
    public IList<string> WarmupVoices { get; set; } = [];

    /// <summary>暖機の本文。null＝契約の既定「暖機です。」に任せる。</summary>
    [JsonPropertyName("warmupText")]
    public string? WarmupText { get; set; }

    /// <summary>参照潜在の事前計算（裁定 65・67）。null＝変種の既定（<c>rocm-*</c> は ON）。</summary>
    [JsonPropertyName("precompute")]
    public bool? PrecomputeOnStart { get; set; }

    /// <summary>裁定 69＝設定項目・初期値 0（＝上流の <c>empty_cache_interval</c> をそのまま渡す）。</summary>
    [JsonPropertyName("emptyCacheInterval")]
    public int EmptyCacheInterval { get; set; }

    /// <summary>UI の拡大率。</summary>
    [JsonPropertyName("uiScale")]
    public double UiScale { get; set; } = 1.0;

    /// <summary>話者一覧の並び（id の列。ここに無い話者は後ろに付く。「デフォルト」は常に先頭）。</summary>
    [JsonPropertyName("voiceOrder")]
    public IList<string> VoiceOrder { get; set; } = [];

    /// <summary>ready 待ち（秒）。0 以下＝変種の既定（契約 ⑵＝120 s・CPU は 300 s）。</summary>
    [JsonPropertyName("readyTimeoutSeconds")]
    public int ReadyTimeoutSeconds { get; set; }

    /// <summary>初回取得ウィザードを通したか。</summary>
    [JsonPropertyName("firstRunCompleted")]
    public bool FirstRunCompleted { get; set; }

    /// <summary>同意した通知文の sha256（<c>licenses/first-run-notices.md</c>・裁定 46）。</summary>
    [JsonPropertyName("acceptedNoticesSha256")]
    public string? AcceptedNoticesSha256 { get; set; }

    /// <summary>
    /// <b>展開に使った取得台帳</b>（<c>ledger/runtime-&lt;変種&gt;.json</c>）の sha256（裁定 91）を
    /// <b>変種ごとに</b>持つ表。鍵＝台帳の変種名（<see cref="RuntimeVariants"/> の綴り）・
    /// 値＝小文字 hex 64 字。
    /// <para>
    /// <see cref="AppPaths.ResolvePythonExe"/> は <c>python.exe</c> の在否しか見ないので、
    /// 配布物を新しい版に入れ替えても<b>古い実行系がそのまま使われる</b>。起動時にこの値と
    /// 配布樹の台帳を突き合わせ、食い違ったら状態帯に「実行系を組み直す」1 手を出す。
    /// その変種の欄が無い＝まだ 1 度も展開していない（または古い <c>settings.json</c>）＝<b>黙る</b>。
    /// </para>
    /// <para>
    /// <b>1 組ではなく表である理由</b>（是正・便 D（3）の 3 巡目）＝<b>実行系は変種ごとに在る</b>のに
    /// 焼き印が 1 組しか無いと、<b>両方組んである機体で変種を切り替えただけ</b>で
    /// 「実行系を組み直してください」の偽警告が出て 1 手が押せるようになり、押せば健全な実行系を
    /// 消して数 GiB を取り直す（裁定 88 ⑴ が勧める「cu126 → cpu へ切り替える」導線がそのまま
    /// この穴に落ちる）。取得キャッシュの関門（<c>CacheCleaner.Blocked</c>）も同じ値を見るので、
    /// 切り替えた瞬間に掃除まで止まった。
    /// </para>
    /// </summary>
    [JsonPropertyName("runtimeLedgers")]
    public IDictionary<string, string> RuntimeLedgers { get; set; } = NewMap();

    /// <summary>
    /// 実行系を展開したときのランチャの版（<see cref="AppVersion.Display"/>・裁定 91）を
    /// <b>変種ごとに</b>持つ表。版だけが動いた（台帳は同じ）ときは<b>急かさない</b>ための欄である。
    /// </summary>
    [JsonPropertyName("installedAppVersions")]
    public IDictionary<string, string> InstalledAppVersions { get; set; } = NewMap();

    /// <summary>その変種の焼き印（無ければ null）。<b>純関数</b>＝鍵の大小と前後の空白は問わない。</summary>
    public string? RuntimeLedgerFor(string? variant) => Lookup(RuntimeLedgers, variant);

    /// <summary>その変種を展開したときのランチャの版（無ければ null）。</summary>
    public string? InstalledAppVersionFor(string? variant) => Lookup(InstalledAppVersions, variant);

    /// <summary>
    /// その変種の焼き印を書く（null・空を渡すと<b>その変種の欄を消す</b>）。
    /// 他の変種の焼き印には触らない。
    /// </summary>
    public void SetRuntimeStamp(string variant, string? ledgerSha256, string? appVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        RuntimeLedgers = Put(RuntimeLedgers, variant, ledgerSha256);
        InstalledAppVersions = Put(InstalledAppVersions, variant, appVersion);
    }

    /// <summary>焼き印を持っている変種の並び（掃除の除外集合を組むときに使う）。</summary>
    public IReadOnlyList<string> StampedVariants()
    {
        if (RuntimeLedgers is null || RuntimeLedgers.Count == 0)
        {
            return [];
        }

        var names = new List<string>(RuntimeLedgers.Count);
        foreach (var pair in RuntimeLedgers)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                names.Add(pair.Key.Trim());
            }
        }

        return names;
    }

    /// <summary>変種の表の作り口（鍵の大小を問わない）。</summary>
    public static IDictionary<string, string> NewMap() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>変種の表の写し（<see cref="Clone"/> と <c>Sanitize</c> が使う）。</summary>
    public static IDictionary<string, string> CopyMap(IDictionary<string, string>? map)
    {
        var copy = NewMap();
        if (map is null)
        {
            return copy;
        }

        foreach (var pair in map)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                copy[pair.Key.Trim()] = pair.Value.Trim();
            }
        }

        return copy;
    }

    /// <summary>
    /// 鍵で引く（<b>大小を問わない</b>）。System.Text.Json は表を素の
    /// <see cref="Dictionary{TKey,TValue}"/>（既定の比較子）で作り直すので、
    /// 読み口の側で大小を吸収する。
    /// </summary>
    private static string? Lookup(IDictionary<string, string>? map, string? variant)
    {
        if (map is null || map.Count == 0 || string.IsNullOrWhiteSpace(variant))
        {
            return null;
        }

        var key = variant.Trim();
        if (map.TryGetValue(key, out var direct))
        {
            return string.IsNullOrWhiteSpace(direct) ? null : direct.Trim();
        }

        foreach (var pair in map)
        {
            if (string.Equals(pair.Key?.Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim();
            }
        }

        return null;
    }

    /// <summary>鍵を 1 つ書く（値が空なら消す）。大小違いの重複鍵も一緒に落とす。</summary>
    private static IDictionary<string, string> Put(
        IDictionary<string, string>? map, string variant, string? value)
    {
        var next = CopyMap(map);
        var key = variant.Trim();

        foreach (var existing in new List<string>(next.Keys))
        {
            if (string.Equals(existing?.Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                next.Remove(existing!);
            }
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            next[key] = value.Trim();
        }

        return next;
    }

    /// <summary>ランチャの起動と同時にサーバを起こすか。</summary>
    [JsonPropertyName("autoStartServer")]
    public bool AutoStartServer { get; set; } = true;

    /// <summary>GPU メモリの常時表示（裁定 67 ⑶）。</summary>
    [JsonPropertyName("showMemoryPanel")]
    public bool ShowMemoryPanel { get; set; } = true;

    /// <summary>
    /// 更新のとき、<b>新しくなった分だけ</b>取り直すか（既定 ON・憲章 §4-24・<c>v2-spec.md</c> §11-3）。
    /// <para>
    /// 真＝版が上がった回に <see cref="Services.Ledger.RuntimeDiff"/> の計画で
    /// <b>変わった item だけ</b>を落として当てる。偽＝いままでどおり丸ごと組み直す
    /// （<c>decisions.md</c> 90 で取得キャッシュは空なのが常態なので、実際は数 GiB の再取得になる）。
    /// 差分が組めない回（旧台帳の写しが無い・6 割超が動いた・旧にだけ在る item が在る）は
    /// この設定が真でも<b>丸ごと</b>へ落ちる＝<see cref="Services.Ledger.RuntimeDiffPlan.RebuildAll"/>。
    /// </para>
    /// </summary>
    [JsonPropertyName("differentialUpdate")]
    public bool DifferentialUpdate { get; set; } = true;

    /// <summary>試し撃ちで最後に使った話者。</summary>
    [JsonPropertyName("lastTestVoice")]
    public string? LastTestVoice { get; set; }

    /// <summary>試し撃ちの steps（UI プリセット 10／40＝裁定 10）。</summary>
    [JsonPropertyName("lastTestNumSteps")]
    public int LastTestNumSteps { get; set; } = 40;

    /// <summary>既定ポート（裁定 2）。本体の接続先と同じ値でなければならない。</summary>
    public const int DefaultPort = 18088;

    /// <summary>解決済みの ready 待ち（0 以下なら変種の既定）。</summary>
    public TimeSpan EffectiveReadyTimeout() => ReadyTimeoutSeconds > 0
        ? TimeSpan.FromSeconds(ReadyTimeoutSeconds)
        : RuntimeVariants.ReadyTimeoutDefault(Variant);

    /// <summary>解決済みの事前計算の可否（null なら変種の既定＝裁定 65）。</summary>
    public bool EffectivePrecomputeOnStart() =>
        PrecomputeOnStart ?? RuntimeVariants.PrecomputeOnStartDefault(Variant);

    /// <summary>
    /// 解決済みの精度。Radeon 変種は利用者の指定を無視して bf16（裁定 5・36＝載せると exit 2）。
    /// null を返したときは env に精度を<b>載せない</b>＝wrapper の device 連動に任せる。
    /// </summary>
    public string? EffectivePrecision()
    {
        if (RuntimeVariants.IsRocm(Variant))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(Precision) ? null : Precision.Trim();
    }

    /// <summary>丸ごとの写し（設定画面の「取り消し」用）。</summary>
    public LauncherSettings Clone() => new()
    {
        Schema = Schema,
        GpuUuid = GpuUuid,
        GpuName = GpuName,
        Variant = Variant,
        Precision = Precision,
        Port = Port,
        DataDir = DataDir,
        HfHome = HfHome,
        WarmupOnStart = WarmupOnStart,
        WarmupStages = [.. WarmupStages],
        WarmupVoices = [.. WarmupVoices],
        WarmupText = WarmupText,
        PrecomputeOnStart = PrecomputeOnStart,
        EmptyCacheInterval = EmptyCacheInterval,
        UiScale = UiScale,
        VoiceOrder = [.. VoiceOrder],
        ReadyTimeoutSeconds = ReadyTimeoutSeconds,
        FirstRunCompleted = FirstRunCompleted,
        AcceptedNoticesSha256 = AcceptedNoticesSha256,
        RuntimeLedgers = CopyMap(RuntimeLedgers),
        InstalledAppVersions = CopyMap(InstalledAppVersions),
        AutoStartServer = AutoStartServer,
        ShowMemoryPanel = ShowMemoryPanel,
        LastTestVoice = LastTestVoice,
        LastTestNumSteps = LastTestNumSteps,
    };
}
