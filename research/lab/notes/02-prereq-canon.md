# 02-prereq-canon.md — 前提資料（正典・要求側）と読み分けちゃん2 本体アダプタの現況

> 読解席（opus）／2026-09-04。**読むだけ**（yomiwakechan2 リポは 1 バイトも変えていない）。
> **引用規約**：`yomiwakechan2/…:N` ＝ `C:/Users/mugonkun/source/repos/yomiwakechan2/…` の N 行目。
> 行番号は `sed -n` / `grep -n` で実見したもののみ。推測には「〔未確認〕」の札。
> ライセンス・既定値・引数名は原文逐語。

## 要点（10 行以内）

1. エンジン契約は 2 本だけ＝`IEngineDiscovery`（能力を返す）と `IEngineSynthesizer`（喋って完了する）。先読みは `IPrefetchingSynthesizer` の**実装有無そのもの**が能力フラグ（`yomiwakechan2/Core/Abstractions/IPrefetchingSynthesizer.cs:10-11`）。
2. パラメータ型は **4 値**＝`Number`／`Choice`／`Flag`／`Text`（`Capabilities.cs:8-20`）。`Text` は **irodori 便で新設された唯一の Core+UI 型拡張**であり、選択肢照合も長さ上限も課さない自由記述（`VoiceResolver.cs:148-149`）。
3. 「声の値」欄＝`VoiceSetting`＝**1 プロファイル＝1 キャラ＋そのキャラの全パラメータ絶対値**（`VoiceSetting.cs:25-27`）。**パラメータ本数は発見時に固定・セッション中不変**（`Capabilities.cs:4-5`）＝プロファイル側から欄は増やせない。増やせるのは**プロファイル数**（コメント者ごと）だけ。
4. Irodori は**擬似キャラ 1 件**で、声の違いは全部パラメータ 5 本（プロンプト／参照音声／シード／話速／音量）が担う（`IrodoriCapabilityBuilder.cs:8-16`・`IrodoriConstants.cs:550-610`）。
5. 感情制御の実装済みの口は **caption（日本語自由記述）だけ**。`num_steps`・`cfg_scale_*` は**非露出・サーバ既定のまま**（`decisions.md:8279`）。絵文字による感情制御は survey が挙げるのみで露出も検証もされていない（`docs/irodori-tts-survey.md:177`）。
6. **GPU 指定の口は本体に存在しない**——「複数 GPU はどちらを使うかを利用者が Server 側で設定＝本体管轄外が正着」が 2026-08-29 の裁定（`docs/irodori-tts-survey.md:255-257`）。コード全体を grep しても GPU index を扱う箇所は 0 件。
7. 環境変数は**自由記述欄を作らない**裁定で、元栓の**精度 2 択（既定 bf16）**が `IRODORI_MODEL_PRECISION`／`IRODORI_CODEC_PRECISION` を子プロセスへ直接注入するだけ（`IrodoriConstants.cs:175-230`）。
8. 元栓席は 9 アダプタで唯一の**多欄構成**（パス〔フォルダ可・venv 自動解決〕／起動オプション／作業フォルダ ＋ 精度セレクタ）＋**終了時閉扉の初実装**（`decisions.md:8302-8311`）。
9. 見積り係数 **230ms/字** は「生成された音声の長さ」の実測（probe P-4 の音声長列＝50 字 11.48 秒・200 字 46.36 秒）であって合成速度ではない（`IrodoriConstants.cs:321-326`）。
10. 三案への直接の縛り＝**Core+UI への波及は司令官裁定が要る停止域**（irodori 便で 4 箇所に限定して初めて破られた＝`docs/irodori-tts-requests.md:136-144`）。逆に**同梱・初回 DL・ONNX 実行**は音声認識内包便で**すでに前例がある**（`decisions.md:8486-8489`・`8549-8554`）＝⒜⒝の追い風。

---

## 1. 読み分けちゃん2 側のエンジン契約

### 1-1 アダプタが実装するインターフェース（2 本＋任意 1 本）

| 契約 | 檔:行 | 約束すること | 約束しないこと |
|---|---|---|---|
| `IEngineDiscovery` | `yomiwakechan2/Core/Abstractions/IEngineDiscovery.cs:17-29` | 起動シーケンスの中で必ず「成功（能力確定）」か「失敗（除外）」に確定する（タイムアウト込み・例外で抜けない）。成功時の能力はイミュータブル・セッション中不変 | **エンジンの起動**（「アプリはエンジンを起動しない §10」:12）・稼働中の再スキャン・失敗エンジンの回復 |
| `IEngineSynthesizer` | 同 `IEngineSynthesizer.cs:16-34` | 正常完了＝読み上げ完了（完了駆動）。タイムアウト安全弁で必ず有限時間で返る。排他制御も各アダプタの責任 | **再生中の途中停止**（「エンジンにより不可能 §6-1」:12）・低レイテンシ・並列実行・内部性質の開示 |
| `IPrefetchingSynthesizer`（任意） | 同 `IPrefetchingSynthesizer.cs:22-29` | `PrepareAsync` 1 本。**対応可否＝実装有無そのもの**＝「`EngineCapability.CanPrefetch` のようなフラグは作らない（裁定5）」（:10-11） | 衝突時にどこまで待つかは**エンジンごとの裁定**（:14-19） |

戻り値は `SpeakResult{Completed, TimedOut}` の 2 値のみ（`IEngineSynthesizer.cs:36-41`）＝**どの失敗も TimedOut に畳んでログ 1 行**（掟2＝`docs/engine-adapters-overview-map.md:71-72`）。

補助口＝`EstimateSpeechLength`（既定実装 null・「将来エンジンは実装しなくてよい＝波及ゼロの意図」`IEngineSynthesizer.cs:31`）。

### 1-2 パラメータ宣言の型＝`ParamKind` の全値

`yomiwakechan2/Core/Models/Capabilities.cs:8-20`（逐語）：

| 値 | 行 | 器（保存側 `ParamValueKind`） | 範囲指定 | UI の顔 |
|---|---|---|---|---|
| `Number` | :10 | `Number`（decimal） | **可**＝`Min`/`Max`/`Step`（`Capabilities.cs:53-58`「Number のみ有効」）。範囲外は**クランプ**（`VoiceResolver.cs:141-143`） | スライダー |
| `Choice` | :11 | `Choice`（string） | 列挙＝`Choices`（`Capabilities.cs:61-62`「Choice のみ有効（選択肢ID列）」）。**列挙に無い文字列は既定値へ差し替え**（`VoiceResolver.cs:144-146`） | プルダウン |
| `Flag` | :12 | `Flag`（bool） | なし | チェックボックス |
| `Text` | :19 | **`Choice` の器を流用**（逐語＝「値は既存の `ParamValueKind.Choice` の器が運ぶ（`ParamValue`・DTO・JSON は無改造）」:16-17） | **なし**＝逐語「選択肢の照合も長さ上限も課さない自由記述」（:15）。検証は「型違いだけ既定値へ畳む」（`VoiceResolver.cs:148-149`） | テキスト欄（`UI/ViewModels/Settings/TextParamViewModel.cs`） |

`ParamValue` 自体は 3 態のまま（`Core/Models/ParamValue.cs:3-12`）＝**`Text` は保存形を 1 バイトも増やしていない**。

`ParamDescriptor` の付随宣言（`Capabilities.cs:47-155`）：

| 口 | 行 | 意味 |
|---|---|---|
| `IsVolume` | :64 | マスターボリューム乗算対象（§13）。適用は `VoiceResolver.cs:107-108` の 1 箇所 |
| `MasterSpeed` | :71 | 話速写像 3 型＝`None`/`Multiply`/`Log2Offset`（:161-175）。適用は `VoiceResolver.cs:110-113` |
| `IsEmotion` | :83 | **感情系パラメータの印**。逐語＝「GUI のグループ見出し「感情」の判別に使う」「基本/感情の区別はエンジン側の事実であり、各アダプタが設定する」 |
| `TextPresets`（`ITextPresetSource`） | :89 | **Text のみ有効**。定型集の出所。null＝素の欄 |
| `TextValueGenerator` / `…Label` | :95 / :103 | Text のみ。「ランダム採番」ボタンの生成口とボタンの語 |
| `UnsetToggleLabel` | :115 | Text のみ。「値を空に保つ」スイッチ。逐語＝「**保存の形は増えない**——「空である」という値そのものの見える化」 |
| `FileDialogFilter` | :126 | Text のみ。「参照…」ボタン。逐語＝「選んだファイルのフルパスをそのまま欄へ書いて確定するだけ」 |
| `Note` / `NoteToolTip` | :134 / :141 | 欄の下 1 行とツールチップ（文言はエンジン側 Constants が持つ） |
| `PrefersFullWidth` | :154 | レイアウトヒント（長文・パスは縦列 200px では読めない） |

`ITextPresetSource` は 2 メソッドのみ（`Capabilities.cs:31-41`）＝`LoadAll()`（失敗は空リスト＝例外を漏らさない）／`TryAppend(title, body, out error)`（失敗＝false＋理由 1 行）。

### 1-3 「声の値」欄に写せる形の制約

| 問い | 答 | 根拠 |
|---|---|---|
| 1 キャラ＝1 組の値か | **そう**。`VoiceSetting` ＝ `CharacterRef`（エンジンID＋キャラID）＋`ParamValues`（そのキャラの**全パラメータ絶対値**） | `Core/Models/VoiceSetting.cs:25-27` |
| 欄（パラメータ本数）を動的に増やせるか | **不可**。能力は「発見層（`IEngineDiscovery`）が起動時に生成し、**セッション中不変**・イミュータブルで共有」「★設定ファイルには一切保存しない（毎起動時に再取得）」 | `Core/Models/Capabilities.cs:3-5` |
| では 1 キャラで複数の声色を持てるか | **プロファイルを増やすことで持てる**。プロファイルはコメント者ごと＋特殊面の集合で、各々が独立した `VoiceSetting` を持つ＝「読み替え・敬称・**声**・優先度…は保存値（配信者の意思）」 | `docs/voice-profile-mechanism-map.md:120`・`:85`（「8 種のプロファイル・4 階層とコメント者個別」） |
| 保存値は書き換わるか | **書き換わらない**。「保存値（`VoiceSetting`）は決して書き換わらない・実効値（`ResolvedVoice`）は毎回作り直される」（不変条件 V-14） | `docs/voice-profile-mechanism-map.md:110-111` |
| 過去のキャラの値の記憶 | `CharacterSwitchMemory`＝「**初期値から変更されたキャラのみ**記録」・コメント者のみ | `Core/Models/VoiceSetting.cs:33-40` |

**プロンプト定型集の仕組み**（`Text` の付随口の実体）：

- 器＝`ITextPresetSource`（Core）／実装＝`Engines/Irodori/IrodoriCaptionPresetStore.cs`。**パス・書式の知識はエンジン側に閉じる**（`IrodoriCaptionPresetStore.cs:20-21`）。
- 収蔵先＝`C:\yomiwakesozai\irodori-tts-captions.txt`（定数 1 箇所＝`IrodoriConstants.cs:621`）。
- 書式（逐語）＝「1 件＝「タイトル行」＋「本文行」の 2 行。本文行は `「…」` で囲まれた 1 行。`以下テンプレート` の行があればそれより後だけが定型」（`IrodoriCaptionPresetStore.cs:14-15`）。**寛容パーサ**（`:11-12`）。
- 操作＝一覧から選ぶと欄を**上書きして即確定**／「タイトルを付けて保存」で**追記のみ**（削除・書換は UI から行わない＝`IrodoriCaptionPresetStore.cs:18`）。**全プロファイル共有**（`docs/irodori-tts-requests.md:162-163`）。
- 不在は正常（0 件で動き、保存時に作る＝`IrodoriCaptionPresetStore.cs:57`）。失敗隔離＝設定画面・読み上げを壊さない（`:19-20`）。
- **テキストの制約 1 点**＝`TextParamViewModel.cs:14` 逐語「値は trim しない（前後の空白も配信者の入力）。**1 行運用のため確定時に改行だけ空白へ畳む**」＝**多行プロンプトは構造的に持てない**。

---

## 2. Irodori アダプタの現況（`yomiwakechan2/Engines/Irodori/` 13 檔）

檔＝`IrodoriApiClient` / `CapabilityBuilder` / `CaptionPresetStore` / `Constants`(44KB) / `Discovery` / `Prefetcher` / `Seed` / `ServerOutput` / `SpeechRequest` / `Synthesizer`(33KB) / `VoiceRegistry` / `VolumeScaler` / `Warmup`（`ls` 実見）。

### 2-1 型と経路

| 項 | 現況 | 根拠 |
|---|---|---|
| 方式 | **HTTP 1 発型**（OpenAI 互換 `POST /v1/audio/speech`）。2 段合成でなく `QueryModifier` を持たない | `docs/engine-adapters-overview-map.md:373`・`decisions.md:8271` |
| 接続先 | `http://127.0.0.1:8088` **固定・localhost 禁止**（IPv6 先行試行で毎リクエスト約 +2 秒の実測） | `IrodoriConstants.cs:35`・`decisions.md:8274-8275` |
| voice | **常に明示送信**。参照なし＝`"none"`（省略は 400 実測） | `IrodoriConstants.cs:52`・`IrodoriSpeechRequest.cs:64-66` |
| 要求 JSON | `{model, input, voice, response_format, speed, irodori:{caption?, seed?}}` | `IrodoriSpeechRequest.cs:64`（組み立ては `:110-124`） |
| caption 経路 | `irodori` **ネスト内**。trim して非空なら**原文のまま**載せる。空・空白のみは**フィールドごと省略**（空文字送信は 400「caption must be non-empty when specified.」） | `IrodoriSpeechRequest.cs:69-72`・`:101`・`:119` |
| 発見 | `GET /health` 1 段（5 秒）。**モデル未ロード（loaded=false）でも使用可** | `IrodoriConstants.cs:60-65` |
| 音量 | API に口が無く**アダプタ内 wav 乗算＝減衰のみ**（生成 wav に peak=1.0 の個体がある） | `IrodoriSynthesizer.cs:36-38`・`decisions.md:8278` |
| 排他 | 内部セマフォで直列化（サーバ自身が内部直列・待ち超過 503 の実測） | `IrodoriSynthesizer.cs:19-20` |
| 期限 | 合成＝**60 秒 ＋ 1.5 秒/字・下限 90 秒**／再生＝wav 実長＋5 秒（解析失敗時 3 分） | `IrodoriConstants.cs:269-283`・算式 `:289-301` |

### 2-2 露出パラメータ 5 本（`IrodoriConstants.BuildSynthesisParams`＝`IrodoriConstants.cs:550-610`）

並び順＝**プロンプト → 参照音声 → シード → 話速 → 音量**（「パラメータ UI は記述子順に並ぶ」`:532`）。

| # | ParamId | 表示名 | Kind | 既定 | 付随口 | 行 |
|---|---|---|---|---|---|---|
| 1 | `caption`（**恒久固定**） | 「プロンプト」 | `Text` | 空 | `TextPresets`（定型集）・`NoteToolTip`・`PrefersFullWidth` | :553-562 |
| 2 | `voice` | 「参照音声」 | `Text` | 空 | `FileDialogFilter`（wav）・`Note`・`PrefersFullWidth` | :563-575 |
| 3 | `seed` | 「シード」 | `Text` | 空 | `TextValueGenerator`＋ラベル「シード値を生成」・`UnsetToggleLabel`「シード値を固定しない」 | :576-587 |
| 4 | `speed` | 「話速」 | `Number` | 1.0（0.25〜4.0・step 0.01） | **`MasterSpeed = Multiply`**（註「純粋な倍率系（0.25→4倍長・4.0→1/4 の実測）」:597） | :588-598 |
| 5 | `volume` | 「音量」 | `Number` | 1.0（0〜1） | `IsVolume = true` | :599-609 |

**非露出**＝`num_steps`・`cfg_scale_text`／`cfg_scale_caption`／`cfg_scale_speaker`・`response_format` 等は「サーバ既定のまま」（`docs/irodori-tts-requests.md:150-151`・`decisions.md:8279`）。理由の逐語＝「多パラメータ露出は題意「パラメータ群の代わりにプロンプト」と逆行＝卓 12」。

**`IsEmotion` は Irodori では 1 本も立っていない**（grep 実測＝`IsEmotion` を立てるのは CeVIO・VOICEPEAK・VOICEROID2 のみ）。

### 2-3 マスター話速の写像 `Multiply`

- 宣言は記述子 1 箇所（`IrodoriConstants.cs:597`）。実際の乗算は **上流 `VoiceResolver` が済ませる**＝`実効値 = 保存値 × マスター/100` → `Min`/`Max` クランプ（`Core/Playback/VoiceResolver.cs:128-137`）。
- 掟＝「写像は 3 型しかない」「乗算・加算そのものは上流が済ませる——アダプタは実効値を受け取るだけで自前の写像を持たない」（`docs/engine-adapters-overview-map.md:73-75`）。
- Irodori は `Multiply` 組（VOICEVOX・COEIROINK・AivisSpeech・VOICEPEAK・VOICEROID2・棒読みちゃん・Irodori＝同 :73）。

### 2-4 seed 3 態

`decisions.md:8285-8287` 逐語＝「空（既定）＝送らない＝TTS 側ランダム（揺らぎ既定）／数値＝固定（同一入力＝バイト列一致・GPU/bf16 でも成立の実測）／UI＝「シード値を生成」＋「シード値を固定しない」スイッチ。**非数値・値域外（0〜int.MaxValue 外）は送らない側に畳む**」。
実装＝`IrodoriSpeechRequest.cs:102`（`IrodoriSeed.TryParse` が通ったときだけ数値として載せる）・値域定数＝`IrodoriConstants.cs:458-461`。

### 2-5 先読みとキャンセル（COEIROINK T1〜T5 との差）

| 論点 | COEIROINK | Irodori | 根拠 |
|---|---|---|---|
| 型 | 錠型（割り切り型） | **錠型・COEIROINK の写経**（Kill 系は持たない） | `IrodoriPrefetcher.cs:6-9` |
| キー | `BuildRaw` 別建てが要る（音量が要求に載るため） | **要求 JSON そのもの**＝「音量はこの JSON に構造的に現れない」ため別建て不要 | `IrodoriSpeechRequest.cs:82-85` |
| 飛行中 | 常に 1 本 | 同（「先読み合成（飛行中）は常に 1 本」） | `IrodoriPrefetcher.cs:11-13` |
| 着火ゲート | 本線合成中は新規着火しない（T5 の改鋳要 10%） | 同（`BeginSpeakSynthesis`） | `IrodoriPrefetcher.cs:14-15`／`docs/coeiroink-prefetch-requests.md:51-53` |
| **キャンセル** | サーバ完全直列・キャンセル不能＝Kill せず join | **同じ。かつ明文で不採用**＝逐語「道譲り Kill は作らない（HTTP 型に道譲りの手段が無い。**キャンセルは孤児仕事をサーバに積むだけで後続をさらに待たせる**）」 | `IrodoriPrefetcher.cs:16-18` |
| 本線の ct | — | 「ct は合成開始前の中止・合成中の中止にのみ効かせる。**再生が始まったら読み切る**」 | `IrodoriSynthesizer.cs:24-25` |
| T1 被害上限 | 「飛行中 1 本の残り（≤約 1.6 秒）」＋人の操作起点に限る | Irodori は 1 発の実所要が桁違いに大きい〔未確認：Irodori 版の被害上限の再計算は docs に無い〕 | `docs/coeiroink-prefetch-requests.md:23-30` |
| seed の門 | — | 先読みは**seed 未固定でも同乗**（メモリ）。門が要るのは名前キャッシュ（ディスク）だけ | `IrodoriPrefetcher.cs:21-24` |
| 容量 | — | 駐機 4・プール LRU 8・TTL 5 分（COEIROINK 同値を初期値） | `IrodoriConstants.cs:303-319` |

### 2-6 元栓（一括起動）・環境変数欄・ExePath の欄

| 項 | 現況 | 根拠 |
|---|---|---|
| 席の型 | `LaunchKind.ExePath`。**既定パス候補なし**（「導入がコマンドライン手順で置き場が定まらない＝推定が成り立たない」） | `IrodoriConstants.cs:67-73` |
| 行の表示名 | 元栓面だけ **「Irodori-TTS-Server」**（読み上げエンジン名は `Irodori-TTS` のまま）。理由＝「行が「Irodori-TTS」と名乗ると**手元の本体を起動する行だと誤読される**のが実機で起きた事故」 | `IrodoriConstants.cs:76-88` |
| 欄① パス | **フォルダも受ける**＝直下/自体の venv を `Scripts\python.exe` の実在で解決。**2 個以上と 0 個は理由つき起動失敗** | `IrodoriConstants.cs:154-159`・`docs/launch-mechanism-map.md:364` |
| 欄② 起動オプション | 生の 1 行のまま `ProcessStartInfo.Arguments` へ（本体は分割も解釈もしない）。フォルダ指定かつ空欄なら標準形 `-m irodori_openai_tts --host 0.0.0.0 --port 8088` を自動適用 | `IrodoriConstants.cs:144-152`・`docs/launch-mechanism-map.md:365` |
| 欄③ 作業フォルダ | 不在なら**撃たずに**理由つき起動失敗 | `IrodoriConstants.cs:168-173` |
| 欄④ 精度セレクタ | **2 択・既定 `bf16`**。`fp32` **明示のときだけ環境変数を 1 本も設定しない**、それ以外（bf16・空・欠落・未知値）は `IRODORI_MODEL_PRECISION`＝`IRODORI_CODEC_PRECISION`＝`bf16` を注入 | `IrodoriConstants.cs:216-230`（純関数 `PrecisionEnvironment`） |
| **自由記述の環境変数欄** | **作らない**（逐語＝「自由記述の環境変数欄は作らない（打ち間違いが静かに効かない形で効く）。効きが実測で確定している 2 変数だけを、選ぶだけの 2 択にする」） | `IrodoriConstants.cs:176-177` |
| bat 経由 | **不採用**＝「bat 経由は文字コード事故の実機実証により不採用」＝環境変数は子プロセスへ直接注入 | `decisions.md:8305-8306`・実機の顛末＝`docs/log.md:5503-5505` |
| 即死検知 | 発射直後 **3 秒**。`ModuleNotFoundError` のような即死を「120 秒後に見切り」にしない | `IrodoriConstants.cs:232-237` |
| 生存プローブ／完了検知 | `GET /health` 3 秒／完了時限 **120 秒**（PRELOAD・bf16 で API 開通まで約 31 秒の実測に余裕） | `IrodoriConstants.cs:239-254` |
| 実行順 | **元栓の実行順だけ irodori が先頭**（モデルロードを他席起動と並行）。§11 探索順（登録順＝AivisSpeech 直後）とは**別の裁定** | `decisions.md:8308`・`docs/engine-adapters-overview-map.md:408-409` |
| 終了時閉扉 | **9 席で唯一**。元栓が起こした個体（`LaunchedByUs`）のみプロセスツリー Kill・✓に依らず常時・手起動個体は不触 | `decisions.md:8309-8311`・`docs/launch-mechanism-map.md:724` |
| 共有発射の例外 | 「irodori 席だけが自前の組み立てを持つ既知の例外」（他席は `StartExe` 1 本） | `decisions.md:8145`・`docs/launch-mechanism-map.md:602` |
| ウォームアップ | 貫通の戻り後に **fire-and-forget 3 発**（caption/seed なし）。CPU 検知でウォームアップ残発を打ち切る | `decisions.md:8312-8316` |
| CPU 判別 | **サーバ標準出力の 1 行**でのみ判別＝指標 `model_device=`（逐語の実行ログ＝`IrodoriServerOutput.cs:15`）。**API では判別不能**（openapi に device 系なし・/health の `model_device` は設定値の echo・PyTorch は ROCm も "cuda" と名乗る） | `IrodoriServerOutput.cs:6-21` |

### 2-7 230ms/字の見積り係数の出所【重要な誤読注意】

`IrodoriConstants.cs:321-326`（逐語）：

> 読み上げ長見積りの係数（ms/字。メーター便＝**表示だけに使う概算**で、読み上げの実挙動には関与しない）。実測（probe P-4 の「音声長」列・**CPU/GPU で不変の生成物側の性質**）＝50 字で 11.48 秒（230ms/字）・200 字で 46.36 秒（232ms/字）＝既定の 160ms/字より明確に遅い。

⇒ **これは合成にかかる時間ではなく「生成される音声そのものの長さ」**。`docs/engine-adapters-overview-map.md:382` の「見積り係数＝230ms/字（9 アダプタで最も遅い）」は**話速が遅い**（＝しゃべりがゆっくり）の意。三案いずれでも**モデルを替えない限り変わらない**性質であり、⒝⒞の性能改善では消えない。

---

## 3. 確定裁定 22 項（`docs/irodori-tts-requests.md:122-230`）＋追撃

「◎＝三案を縛る／○＝追い風／—＝中立」は本席の評（推測）。

| # | 1 行要約 | 行 | 三案への効き |
|---|---|---|---|
| 1 | 独立アダプタ `Engines/Irodori/`・既存エンジンは 1 バイトも触らない・写経の重複は受け入れる・1 発型 | :124-127 | ○（⒝⒞ もアダプタ内で完結させれば同じ枠に収まる） |
| 2 | ID `irodori`・表示名 `Irodori-TTS`・接続先 `127.0.0.1:8088`・定数は `IrodoriConstants` 1 箇所 | :128-130 | ◎ ⒝で HTTP を捨てると `BaseUrl` 系の契約が丸ごと死ぬ |
| 3 | 経路は Server（OpenAI 互換）のみ・Gradio 7861 は叩かない | :131-132 | — |
| 4 | 擬似キャラ 1 件・voice は常に `"none"` 明示・voices 登録簿は初版不使用 | :133-135 | ○ 話者列挙の要らない設計＝⒝でも同型 |
| 5 | `ParamKind.Text` 新設・**波及は 4 箇所に限定**（Capabilities／VoiceResolver／TextParamViewModel／SettingsTemplates.xaml）・長さ上限は課さない | :136-144 | ◎ **Core+UI 波及は明示の許可が要る停止域**という前例 |
| 6 | 露出は 4 本（caption/seed/speed/volume）。`num_steps`・`cfg_scale_*` は非露出 | :145-151 | ◎ **感情制御を増やすには裁定 6 の改訂が要る** |
| 7 | seed 3 態（空＝送らない／数値＝固定／ランダム採番ボタン） | :152-155 | — |
| 8 | caption 非空なら送る・空はフィールドごと省略 | :156-157 | — |
| 9 | 定型集 `C:\yomiwakesozai\irodori-tts-captions.txt`・選択で上書き・追記のみ・全プロファイル共有・不在は正常・失敗隔離 | :158-168 | ○ **プロンプト資産はファイル 1 本**＝案の切替でも持ち越せる |
| 10 | 発見＝`/health` 1 段・未起動は理由つき失敗（接続拒否に約 2.0 秒） | :169-172 | ◎ ⒝（インプロセス化）なら発見の意味論が変わる |
| 11 | 実行＝セマフォ → POST → wav に音量乗算 → `IWavPlayer` 再生 → 完了で返る | :173-176 | — |
| 12 | 空・空白のみ本文は送らない（400 実測）。記号・絵文字のみは送ってよい | :177-178 | — |
| 13 | タイムアウトは実測根拠で決める・**CPU 値を GPU の前提にしない、その逆もしない** | :179-183 | ○ 案ごとに再実測が要ることの根拠 |
| 14 | 先読み＝錠型を初版から実装・seed 未固定でも同乗・キーは実効引数＋最終テキスト、音量は除外 | :184-188 | ○ ⒞で「キャンセル口を持つ API」を作れば錠型を緩められる |
| 15 | 名前キャッシュ＝共通器・**実効 seed 固定時のみ**読み書き | :189-196 | — |
| 16 | 一括起動＝ExePath 席・既定パス候補なし・完了検知は PRELOAD が収まる値・初回既定に足さない | :197-202 | ◎ ⒜（専用インストーラ）なら**既定パス候補を持てる**＝この裁定の前提が変わる |
| 17 | 終了時閉扉を irodori 席で初実装・元栓起動個体のみ・プロセスツリー Kill | :203-209 | ◎ ⒝なら閉扉そのものが不要になる |
| 18 | CPU 判別警告＝サーバ標準出力から。**API では判別不能が実測済み**。判別できなければ実装せず申告 | :210-216 | ○ ⒞なら薄い層が device を素直に返せる＝この苦労が消える |
| 19 | ウォームアップ空撃ち（fire-and-forget・見切り後は撃たない） | :217-222 | ○ 起動の重さの緩和策として既に存在 |
| 20 | 登録順＝AivisSpeech の直後・両コンポジションルート同一 | :223-225 | — |
| 21 | 歌・空耳は対象外 | :226 | — |
| 22 | 単体テストは実機非依存の純粋ロジック・継ぎ目は public コンストラクタ（`InternalsVisibleTo` は使わない） | :227-230 | ○ 案の比較で「テスト可能な継ぎ目」を保つ縛り |

**追撃 1〜10**（便中の司令官裁定＝実機検分 2 ラウンドの上書き。**正は「PR #470 コメントと decisions 節」**＝`docs/engine-adapters-overview-map.md:61`）。
**番号→内容の一覧表は docs に存在しない**＝以下は docs に散る言及から拾えたものだけ。拾えなかった番号は〔未確認〕。

| 追撃 | 内容 | 根拠 |
|---|---|---|
| （無番） | irodori 元栓席の起動体験是正（行名を Server 名に・参照フィルタに bat） | `docs/launch-mechanism-map.md:160` |
| 2 | 元栓席を **3 欄構成**へ（起動オプション・作業フォルダの追加） | `docs/launch-mechanism-map.md:131` |
| 3 | 元栓席＝**Server フォルダ指定**（venv 自動解決）＋即死検知＋精度 2 択 | `docs/launch-mechanism-map.md:91-92`・`:758` |
| 6 | irodori パラメータ面の**レイアウト調整** | `docs/settings-tab-ui-mechanism-map.md:50` |
| 8 | ⒜登録面の行に可視の注記を足す口（GPU 推奨注記） ⒝参照音声欄を 1 行高に **※同番で 2 件あり＝docs の番号は一貫していない** | `docs/launch-mechanism-map.md:72` / `docs/settings-tab-ui-mechanism-map.md:33` |
| 10 | irodori 行の下段整理と**精度の既定反転**（fp32→bf16） | `docs/launch-mechanism-map.md:46` |
| 1,4,5,7,9 | 〔未確認〕docs に番号つきの言及なし | — |
| （総括） | 「参照音声の口〔卓 4/6 上書き〕・元栓 3 欄・Server フォルダ指定＋venv 自動解決・精度セレクタ既定 bf16・**プロンプト改称**・**シードスイッチ**・**再起動後 409 是正**・レイアウト 3 巡」 | `docs/log.md:5483-5485`（逐語） |

**追撃で増えた 5 本目のパラメータ＝参照音声**（卓 4／裁定 6 の上書き）＝`decisions.md:8292-8295`。初回合成時に内容 SHA-256 由来の安定名 `ywk-<12hex>` で `/v1/audio/voices` へ**自動登録**。

---

## 4. 要求記録にある未充足の要望（逐語）

### 4-1 感情制御

- **モデル側の 3 系統のうち、実装されたのは caption だけ**。survey 逐語（`docs/irodori-tts-survey.md:174-177`）：
  > 声の条件付けは **3 系統・組み合わせ可**：①**caption＝日本語自由記述プロンプト**（VoiceDesign。例「落ち着いた女性の声で、近い距離感でやわらかく自然に読み上げてください。」）②参照音声ゼロショットクローン（`ref_wav(s)`・同一話者合計 30 秒程度推奨）③本文中の**絵文字**による感情・スタイル制御（**対応表は非公開**）。
  ⇒ ③絵文字は probe P-6 で「絵文字のみは 200 で音が出る」と観測されただけ（`docs/irodori-tts-requests.md:178`）で、**感情制御としての露出も検証もされていない＝未充足**。
- **細かい制御ノブが露出していない**（`docs/irodori-tts-requests.md:150-151` 逐語）：
  > `num_steps`・`cfg_scale_*`・`response_format` 等は**非露出・サーバ既定のまま**（多パラメータ露出は題意「パラメータ群の代わりにプロンプト」と逆行＝卓 12）。
- survey 卓 12 の原文（`docs/irodori-tts-survey.md:230` 逐語）：
  > **最小＝`prompt`（Text）＋音量**から始める。`num_steps`／`cfg_scale_caption`／`seed` 等の露出は実測（P-5・P-7）を見て裁く——多パラメータ露出は題意（パラメータ群の代わりにプロンプト）と逆行するため慎重

### 4-2 GPU 指定

- **本体管轄外という裁定で閉じている**（`docs/irodori-tts-survey.md:255-257` 逐語）：
  > **GPU 複数枚**：どちらを使うかは**利用者が Server 側で設定＝本体管轄外が正着**（API に デバイス指定の口が無い・アダプタはエンジン内部設定を管掌しない既定の流儀。配布案内に「本体からは変更できない・手順は公式 README 参照」の 1 行を置く）。
- 配布ページ側の申し送り（`docs/distribution-page-handover.md:186-187` 逐語）：
  > 「**複数 GPU のどちらを使うかは Irodori-TTS-Server 側の設定（公式 README 参照）＝読み分けちゃんからは変更できない**」旨
- **実効バックエンドの検出も API では不能**（`docs/irodori-tts-survey.md:251-254` 逐語）：
  > Server API は CUDA/ROCm/CPU を**返さない**［実測］（openapi 全面に device 系フィールドなし・/health の `model_device` は設定値の echo・PyTorch は ROCm も "cuda" と名乗る＝文字列でも区別不能）。
- ⇒ **司令官の要望「複数 GPU の指定ができる」（BRIEF §1）は現状 100% 未充足**。コード全体を grep しても GPU index を扱う箇所は 0 件（Irodori 檔の GPU 出現は全部**注記文言と実測コメント**）。

### 4-3 導入の簡便化

`docs/distribution-page-handover.md:199-213`（逐語・**未起票の将来荷**）：
> **課題の芯**：Irodori-TTS-Server は **GUI なし・コマンドライン導入のみ**。既存エンジン群（VOICEVOX 等＝公式インストーラをダブルクリック）と違い、**配布ページが導入の面倒を見る最初のエンジン**になる。
> 2. **GPU 分岐の設計が本丸**：…①NVIDIA＝`uv sync --extra cu128`（公式サポートの本道・手順は簡単）②GPU なし＝`--extra cpu`（動くが 10 字 13 秒＝実測。重さを正直に書く）③AMD Windows＝overrides 経路（**gfx 型番がユーザーごとに違う**＝「自分の型番の調べ方」まで ケアするかが論点・卓の裁定事項）。
> 3. **重さの予告を書く**：ダウンロード合計 5〜6GB＋モデル初回 DL 2.9GB・所要 15〜30 分（実測）。予告なしだと離脱する。
> 4. **運用の推奨形（実測根拠つき）**＝127.0.0.1・`IRODORI_PRELOAD=true`・`IRODORI_EMPTY_CACHE_INTERVAL=0`・bat 化 1 行。

同 `:216` 逐語＝「**引き金**＝irodori-tts 採用裁定 → 実装便着地 → 次のページ更新便。」⇒ **引き金は既に成立している**が便は未起票（`docs/log.md:5502`「**配布ページ更新便の引き金成立**」）。

裁定 16 の逐語（`docs/irodori-tts-requests.md:198-199`）＝「**既定パス候補なし**（導入形が定まらない＝COEIROINK・棒読みちゃんと同じ正直な非対応）」⇒ **導入形が定まれば裁定 16 は変えられる**。

### 4-4 非 Python 化

- **irodori 便の資料に「非 Python 化」への言及は 1 件も無い**（grep 実測＝0 件）。本調査便が初出。
- 最も近い既決の姿勢＝**本体はバックエンド非依存**（`docs/irodori-tts-requests.md:105-106` 逐語）：
  > **本体はバックエンド非依存**（司令官裁定 2026-08-29）＝接続契約は 127.0.0.1:8088 のみ。モデル版・venv・環境変数（`IRODORI_*`）は利用者の管掌であり、本体は語らない。
  ⇒ **⒝はこの裁定を真っ向から書き換える**（本体が推論を持つ＝バックエンド依存になる）。

---

## 5. 他エンジンの前例

### 5-1 感情・スタイル制御の前例

| エンジン | 感情/スタイルの表現 | 根拠 |
|---|---|---|
| **AivisSpeech** | **スタイルはキャラである**＝逐語「話者の単位＝**キャラ×スタイル = 1話者**。CharacterId＝"キャラ名(スタイル名)"」。パラメータ側の「感情表現の強さ」は `intonationScale` の**改称**（逐語＝「「抑揚」と表示しない（裁定10）＝意味が VOICEVOX と別物（スタイルの感情表現の強さ。ノーマルスタイルでは無視される）」）。`IsEmotion` は**立てていない** | `Engines/Aivisspeech/AivisspeechCapabilityBuilder.cs:20`・`AivisspeechConstants.cs:136-138`・`:129-145` |
| **VOICEVOX** | 同型（styles をキャラへ畳む）。感情軸なし | `Engines/Voicevox/VoicevoxSpeakersParser.cs:38-52` |
| **CeVIO AI** | **`IsEmotion = true` の可変軸**＝「キャスト依存の感情 Components（ParamId＝安定 Id・表示名は GUI 用・IsEmotion=true。裁定⑥）」。キャラごとに本数が違う | `Engines/Cevio/CevioCapabilityBuilder.cs:12`・`:70` |
| **VOICEPEAK** | 同＝「**そのナレーター固有の感情**（ParamId＝CLI が受け付ける感情キー・IsEmotion=true。裁定11）」 | `Engines/Voicepeak/VoicepeakCapabilityBuilder.cs:12`・`:74` |
| **VOICEROID2** | Bridge の実データから `IsEmotion` を受け取る | `Engines/Voiceroid2/Voiceroid2CapabilityBuilder.cs:53` |
| COEIROINK | 感情軸なし＝「API に感情フィールドなし＝IsEmotion 該当なし」 | `Engines/Coeiroink/CoeiroinkConstants.cs:144` |
| SAPI | 「感情軸はない（IsEmotion 該当なし）」 | `Engines/Sapi/SapiConstants.cs:78` |
| **Irodori** | **`IsEmotion` 0 本**。感情は caption の文章に溶けている | 本ノート §2-2 |

⇒ **前例は 2 型ある**：①**キャラとして増やす**（VOICEVOX/AivisSpeech＝キャラ×スタイル）②**キャラごとに違う本数の `IsEmotion` パラメータを立てる**（CeVIO/VOICEPEAK/VOICEROID2）。**②の器は Core に既にあり、Irodori が使っていないだけ**＝⒞で薄い層が感情軸を露出するなら、**Core を 1 バイトも触らずに載る**（`ParamDescriptor.IsEmotion` は既存・UI のグループ分けも既存＝`UI/ViewModels/Settings/VoiceEditorViewModel.cs:231`）。

### 5-2 GPU 指定の前例

**無い。** `--include=*.cs` の grep で GPU を語るのは Irodori の注記文言と `App.xaml.cs:694` のコメントだけ。
`decisions.md` の GPU 言及も ⒜配信中の GPU 争奪（:371）⒝診断情報に GPU 情報を**載せない**棄却（:4475）⒞AI 推論を「ゲーム配信と同一 GPU での推論は非推奨の一行案内のみ」（:8409）——**いずれもデバイス選択の口ではない**。
⇒ **「本体が GPU を指定する」は 9 エンジン通じて前例ゼロ**＝三案いずれでも**新機軸**になる。

### 5-3 エンジン同梱／インストーラの前例

| 前例 | 内容 | 三案への効き | 根拠 |
|---|---|---|---|
| **音声認識内包（sherpa-onnx）** | 「採用スタック＝**sherpa-onnx 1.13.5（NuGet）**＋ Silero VAD ＋ ReazonSpeech k2 v2 int8。完全ローカル（外部送信なし・API キー不要）・**CPU int8（RTF 実測 0.01〜0.02）**・非ストリーミング transducer」。ライセンス＝「Apache-2.0×2＋MIT（Silero は現行 MIT を実確認）＝**クローズド配布・将来有償化とも障害なし**」 | ◎◎ **⒝の直接の前例**——ONNX 推論を C# から NuGet 経由で回し、本体に内包した実績が既にある | `decisions.md:8486-8489` |
| **モデルの初回 DL 方式** | 「**モデル配布＝初回 DL 方式**（司令官裁定「アセット追加可」・調査メモの「同梱が現実的」を上書き）。配布リポ Release `asr-model-reazonspeech-v1` の再梱包 zip（**121MB**＝int8 一式＋silero＋**ライセンス 3 檔＋来歴 README 同梱**）をタブの手動ボタンで取得→**sha256 検分**（URL・ハッシュは本体定数＝`VoiceModelDownloader`）→tmp 展開→リネームの**原子的着地**（`%LOCALAPPDATA%\yomiwakechan2\models\`＝世代移行の退避対象外）→ModelDir 自動設定。手動フォルダ指定は差し替え口として存置」 | ◎◎ **⒜のモデル 2.86GB の取得手順は、この型の 24 倍でそのまま写せる**。ライセンス檔と来歴 README の同梱まで既に流儀がある | `decisions.md:8549-8554` |
| **本体インストーラ（v2.1）** | 世代移行（`.pre21.bak` 退避）・初期設定ウィザード 6 ステップ・**書き込みは [完了] の 1 回のみ**・VOICEVOX 寄せ（名指しの声の初期割り当て） | ○ **⒜のウィザードの器**が既にある（Python 埋め込みの展開先・PATH・初回 DL をここに載せられる〔未確認：ウィザードは現状 OBS/マイクを扱わない＝:7917〕） | `decisions.md:7901-7935` |
| **エンジン本体の同梱** | **前例なし**——9 エンジンすべて「アプリはエンジンを起動しない（§10）」「未起動＝理由つき失敗」の掟の下にあり、導入は利用者の管掌 | ◎ ⒜は**この掟に触れずに済む**（元栓が起こす形は既に irodori 席で成立）が、**「配布ページが導入の面倒を見る最初のエンジン」**という自認は既にある | `docs/engine-adapters-overview-map.md:69-70`／`docs/distribution-page-handover.md:199-202` |
| **AI 連携の「非同梱・接続可」** | ローカル AI は同梱せず接続だけ可（写経元＝Irodori）。「別の箱（推論専用機）でも URL 接続」 | ○ ⒞（薄い層＝接続契約の拡張）の追い風。別機推論という退路も既に語られている | `docs/status.md:162`・`decisions.md:8409` |

---

## 6. 主席への注意

1. **230ms/字 を「遅い合成」と読まないこと**。生成音声そのものの長さ（話速）で、`IrodoriConstants.cs:321-326` に「表示だけに使う概算」と明記。**三案いずれでも改善しない**＝比較表の「レイテンシ（RTF）」軸とは別行に置くべき。
2. **裁定 5（`ParamKind.Text` の 4 箇所限定）が三案共通の最重い柵**。感情パラメータを増やす案は **Core+UI を再び触る**＝司令官裁定が要る（`docs/irodori-tts-requests.md:136-144`）。ただし `IsEmotion` の器は既存で、**「数値の感情軸を N 本立てる」だけなら Core 不触で載る**（CeVIO/VOICEPEAK 前例）——ここは比較表で分けて書く価値がある。
3. **GPU 指定は 9 エンジン通じて前例ゼロ**（grep 実測）。⒞なら薄い層が `device`／`index` を受ければ済むが、**「アダプタはエンジン内部設定を管掌しない」既定の流儀**（`docs/irodori-tts-survey.md:255-257`）を明示的に上書きする裁定が要る。
4. **⒝の技術的前例は音声認識内包便が既に作っている**（sherpa-onnx NuGet・CPU int8・Apache-2.0/MIT でクローズド配布可）。⒝を評価するときは**この便を写経元として名指しできる**——ライセンス整理・モデル初回 DL・sha256 検分・原子的着地・`%LOCALAPPDATA%\models\` の置き場まで型がある（`decisions.md:8486-8489`・`8549-8554`）。
5. **配布ページ更新便は引き金成立済み・未起票**（`docs/log.md:5502`・`docs/distribution-page-handover.md:192-218`）。三案の推奨がどれでも**この便と衝突または合流する**＝報告に「この将来荷をどう畳むか」を 1 行で示すと卓が早い。
6. **追撃 1〜10 の番号→内容の一覧は docs に存在しない**（`docs/engine-adapters-overview-map.md:61` が「PR #470 コメントと decisions 節が正」と明記）。しかも **追撃8 は 2 つの別内容で 2 檔に出る**（launch＝行の可視注記／settings-tab-ui＝参照音声欄を 1 行高に）＝**番号で引くと事故る**。報告では番号でなく内容で引くことを勧める。
7. **「非 Python 化」は irodori 便の資料に 1 件も無い**（grep 0 件）＝本調査便が初出の論点。既決の「本体はバックエンド非依存」（`docs/irodori-tts-requests.md:105-106`）を**真っ向から書き換える**案であることを、⒝の評価に必ず添えること。
8. **本ノートで確認していないこと**＝probe 報告書（`probe/irodori-tts-probe-report.md`）本文・ライセンス一次資料・上流 clone の実体は本席の担当外。数値（0.48 秒/字・RTF 0.87〜1.06・2.9GB 等）は**すべて yomiwakechan2 側 doc からの引き写し**であり、一次資料と突合していない。
