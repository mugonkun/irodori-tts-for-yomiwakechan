# 19 — 敵対検分：01-prereq-probe / 02-prereq-canon の主張を潰す

> 席＝第 2 便サブ席（opus・敵対検分）／2026-09-04。**やったことは読みと grep と JSON 集計だけ**
> （yomiwakechan2・upstream・C:/IrodoriTTS・C:/irodori-TTS-server・HF キャッシュは 1 バイトも触っていない。
> HTTP も 1 発も撃っていない＝8088／7861 は停止のまま。GPU 不使用）。書いたのは本檔 1 枚のみ。
> 作法＝各主張を**反証しにいく**。反証できなければ CONFIRMED、できれば REFUTED、判定不能は UNVERIFIABLE。
> 迷ったら CONFIRMED にしない。

## 要点（10 行以内）

1. 15 主張のうち **CONFIRMED 10・部分 REFUTED 4・語の補正 1**。**Core/UI 側の構造主張（a〜k, n, o）は全部生き残った**＝報告の骨は揺らがない。
2. 崩れたのは**数値の解釈と「無い」の証明**の 4 点で、いずれも第 1 便が「原報告の逐語」をそのまま信じた箇所である。
3. **(l) ec=0「スパイク消滅」は生データに反する**＝`C-ec0-3 ms=19430`（`probe/irodori-tts-gpu-latency-raw.txt:59`）が系列内に残る。安定は 4 射目以降の 9 射だけ。
4. **(l) 「10 発おきに +15 秒」は :48-52 からは出ない**（スパイクは 6・9・10 発目＝連続 2 発を含む塊）。**+15 秒の大きさは CONFIRMED、周期は UNVERIFIABLE**。
5. **(m) 「bat の詰まりの記録は無い」は REFUTED**＝`docs/log.md:5503-5505` に**実機で起きて修理済み**の逐語がある（probe/ に無いだけ）。
6. **(m) 「stream_format が probe に一件も無い」は REFUTED**＝`probe/irodori-tts-probe-report.md:87` に欄名として載る。**実射ゼロという実質は CONFIRMED**。
7. **(d) 「device を扱う箇所 0 件」は言い過ぎ**＝GPU index は確かに 0 件だが、本体は**音声出力デバイスを名前→添字で選ぶ器を既に持つ**（`AudioOutputDevices.ResolveDeviceNumber`）。
8. **(c) の補正**＝UI のグループ分けは `IsFullWidth` が**先勝ち**（`VoiceEditorViewModel.cs:230-232`）＝幅を要求する感情軸は「感情」欄に入らない。
9. 便乗で 1 件潰した＝01 の「**5 パス・7 オペレーション**」は **8 オペレーション**（同ノートの表自体が 8 行ある＝自己矛盾）。
10. 01/02 の**行番号は全数一致**（20 箇所超を実見）。危ないのは行ではなく **02 §0 の引用規約の畳み方**（C# は `<repo>/yomiwakechan2/…` の 2 階層目にある）。

---

## 0. 引用規約（本ノート）

- `ywk2/…` ＝ `C:/Users/mugonkun/source/repos/yomiwakechan2/…`（**リポ根**）。
- C# の実体は **リポ根の下の `yomiwakechan2/` フォルダの中**＝`ywk2/yomiwakechan2/Core/Models/Capabilities.cs`。
- `probe/…`・`docs/…`・`decisions.md` はリポ根直下。
- 行番号は `awk NR` で 1 行ずつ実見した値のみ。

## 1. 判定表

| # | 主張（第 1 便） | 判定 | 根拠 |
|---|---|---|---|
| a | `ParamKind` は Number/Choice/Flag/Text の 4 値。Text は Choice の器を流用・長さ上限も照合もなし | **CONFIRMED** | `ywk2/yomiwakechan2/Core/Models/Capabilities.cs:8-20`／`Core/Playback/VoiceResolver.cs:148-149` |
| b | 裁定 5＝Text 新設の波及は 4 箇所限定 | **CONFIRMED** | `ywk2/docs/irodori-tts-requests.md:136-144` |
| c | `IsEmotion` は Capabilities.cs:83 に実在・CeVIO/VOICEPEAK/VOICEROID2 のみ立てる・Irodori 0 本・UI が見る | **CONFIRMED（要補正）** | `Capabilities.cs:83`／grep 全数／`UI/ViewModels/Settings/VoiceEditorViewModel.cs:230-232` |
| d | `*.cs` に GPU index／device を扱う箇所が 0 件 | **部分 REFUTED** | §2-1 |
| e | 「本体はバックエンド非依存」「GPU 複数枚は本体管轄外」 | **CONFIRMED** | `docs/irodori-tts-requests.md:105-106`／`docs/irodori-tts-survey.md:255-257` |
| f | sherpa-onnx 内包・ライセンス／モデル初回 DL・sha256・原子的着地・%LOCALAPPDATA% | **CONFIRMED** | `decisions.md:8486-8489`／`:8549-8554` |
| g | 配布ページ更新便の引き金と「引き金成立」 | **CONFIRMED** | `docs/distribution-page-handover.md:216`／`docs/log.md:5502` |
| h | 230ms/字 は「生成音声の長さ」の係数 | **CONFIRMED** | `Engines/Irodori/IrodoriConstants.cs:321-326` |
| i | 元栓 3 欄＋精度 2 択（既定 bf16・fp32 明示時のみ環境変数を立てない）・bat 不採用 | **CONFIRMED（1 点補正）** | `IrodoriConstants.cs:216-230`／`decisions.md:8302-8306` |
| j | 「道譲り Kill は作らない」逐語・ct は再生開始後は効かない | **CONFIRMED** | `IrodoriPrefetcher.cs:16-18`／`IrodoriSynthesizer.cs:24-25` |
| k | 露出 5 本（caption/voice/seed/speed/volume）と Kind・既定／参照音声の自動登録 `ywk-<12hex>` | **CONFIRMED** | `IrodoriConstants.cs:550-610`／`decisions.md:8292-8295` |
| l-1 | CPU 10 字 13.5 秒 | **CONFIRMED** | `probe/irodori-tts-latency-raw.txt:2-4`＝13522/13658/13227 ms |
| l-2 | ROCm fp32 10 字 3.7 秒 | **CONFIRMED** | `probe/irodori-tts-gpu-latency-raw.txt:19-21`＝3740/3729/4089 ms |
| l-3 | bf16 0.77 秒・VRAM 6.58→3.34GB | **CONFIRMED（引き所を 1 行ずらす）** | `probe/irodori-tts-ref-latency-raw.txt:38-40`＝782/769/769 ms・`:5`＝6.58GB・`:36`＝3.34GB |
| l-4 | seed=42 の SHA256 一致 | **CONFIRMED（語の補正）** | `probe/irodori-tts-determinism-raw.txt:2-3`＝`sha16` 欄の一致＝**SHA256 の先頭 16 桁**（§2-4） |
| l-5 | EMPTY_CACHE_INTERVAL=10 で「10 発おき」+15 秒 | **周期は UNVERIFIABLE／大きさは CONFIRMED** | §2-2 |
| l-6 | ec=0 で 4.1 秒安定（スパイク消滅） | **部分 REFUTED** | §2-2 |
| l-7 | localhost +1.2〜2.0 秒 | **CONFIRMED（出典 2 本必要）** | `probe/irodori-tts-restart-raw.txt:31-34`（+2.0 秒）＋`:29-30`（+1.226 秒） |
| l-8 | 参照 +0.26 秒/参照秒 | **CONFIRMED** | `probe/irodori-tts-probe-report.md:365` 逐語 |
| l-9 | wav 48kHz/16bit/mono | **CONFIRMED** | 同 `:118` 逐語「fmt タグ=1（PCM）・1ch・48,000Hz・16bit」 |
| m-1 | probe に `stream_format=sse` の実射が無い | **実質 CONFIRMED／「記載ゼロ」は REFUTED** | §2-3 |
| m-2 | bat の CRLF の詰まりが guide にも report にも無い | **REFUTED** | §2-3 |
| n | rocm-setup-guide の index URL・torch 版・Python 3.12・sentencepiece 0.2.1・「uv run で起動してはいけない」 | **CONFIRMED（全 5 点）** | §2-5 |
| o | `IrodoriOptions` が 44 欄・全欄 null 許容・enum 3 欄 | **CONFIRMED** | §2-6（JSON 実集計） |
| ＋ | 01 §3.1「5 パス・**7 オペレーション**」 | **REFUTED** | §2-7 |

## 2. 反証できた／揺らいだものの詳細

### 2-1 (d) 「device を扱う箇所が 0 件」は言い過ぎ

**GPU index を選ぶ箇所が 0 件**という芯は反証できなかった＝CONFIRMED。`--include=*.cs` の全数 grep で
`gpu` の当たりは**注記文言・実測コメント・bat 名の例示だけ**である（`App.xaml.cs:694,697`＝
`IrodoriConstants.LaunchGpuRecommendationNote` の行注記／`IrodoriConstants.cs:93,108,113,117,181,189,202,213,257-264,271,323`／
`IrodoriServerOutput.cs:19-20,44,48`／`IrodoriWarmup.cs:7,18`／`EngineLaunchSeats.cs:335,464`／
`IrodoriServerProcessRegistry.cs:112`／`SystemMetricsSampler.cs:30`／`BulkLaunchTabViewModel.cs:50,498`）。

しかし**「device を扱う箇所が 0 件」は誤り**で、少なくとも 3 系統ある：

| 系統 | 檔:行 | 中身 |
|---|---|---|
| **本体が device を添字で選ぶ器**（GPU ではなく音声出力） | `ywk2/yomiwakechan2/Core/Models/AppSettings.cs:533`（`AudioOutputDeviceName`）／`Engines/Common/AudioOutputDevices.cs:21,24,52`／`Engines/Common/NAudioWavPlayer.cs:42-47,75` | 名前で保存し、**再生直前に毎回 名前→番号を再解決**して `WaveOutEvent.DeviceNumber` に入れる。既定は `-1` |
| エンジンの推論デバイスを**読む**前例 | `Engines/Coeiroink/CoeiroinkSpeakerInfo.cs:36-37`（逐語「推論デバイス（例 "cpu" / "gpu"）」）／`CoeiroinkSpeakersParser.cs:80`／`CoeiroinkDiscovery.cs:66`（スキャン成功ログに Device を出す） | **COEIROINK は API が返す device を本体が受け取り、ログに出している** |
| Irodori の device 文字列の**解釈** | `Engines/Irodori/IrodoriServerOutput.cs:39,63-67` | `model_device=` の値を切り出し `cpu` / `cpu:0` を判定する（**添字つき表記まで扱う**） |

⇒ **報告で「前例ゼロ」と書いてよいのは「本体が推論デバイスを*選ぶ*前例」だけ**である。
「デバイスを名前で保存し添字へ再解決する器」も「エンジンの device を受け取って表示する前例」も**既にある**＝
⒞の薄い層が `device`／`index` を受ける設計は、**まったくの新機軸ではなく既存の流儀 2 本の合成**として提案できる
（`AudioOutputDeviceName` は「添字は抜き差しでずれるため名前で保存」という裁定つき＝`decisions.md:8494` に音声認識便の同型の裁定が逐語である）。これは⒞の説得力を上げる材料なので、書き落としは損。

### 2-2 (l) empty_cache 系の 2 主張

生データ（`probe/irodori-tts-gpu-latency-raw.txt`）逐語：

```
47  # empty_cache boundary probe 18:04:08
48  B-filler-6 ms=18876
49  B-filler-7 ms=4144
50  B-filler-8 ms=4153
51  B-filler-9 ms=19447
52  B-filler-10 ms=19438
53  B-filler-11 ms=4127
56  # empty_cache=0 control 18:05:54
57  C-ec0-1 ms=40806
58  C-ec0-2 ms=4065
59  C-ec0-3 ms=19430
60  C-ec0-4 ms=4153      （…以下 -12 まで 4092〜4176）
```

- **「10 発おき」は :48-52 からは出ない**。この窓ではスパイクが **6・9・10 発目**＝**連続 2 発を含む塊**で、
  周期 10 の像にならない。ラベルが `boundary probe` である以上、発数カウンタは前段から継続している可能性が高く、
  **周期の主張はこの生データだけでは検証不能＝UNVERIFIABLE**。原報告 `probe/irodori-tts-probe-report.md:322-323` が
  「10 発おきの empty_cache 後に」と書いており、01 はそれを忠実に写しただけ＝**01 の写経は正しく、原報告の断定が生データより強い**。
- **大きさは CONFIRMED**＝18.876／19.447／19.438 秒 対 定常 4.14 秒＝**+14.7〜15.3 秒**。
- **「ec=0 でスパイク消滅」は REFUTED**。同一系列の **3 発目に 19.430 秒が残っている**（:59）。
  原報告 `:323-324` の逐語は「**形状ウォーム後は 4.1 秒 ±0.05 に完全安定**（同文 12 連射で実証・**スパイク消滅**）」だが、
  12 射のうち **:57＝40.806 秒・:59＝19.430 秒の 2 射がスパイク**であり、「消滅」は言えない。
  **正しい言い方**＝「ec=0 の 12 連射では 4 発目以降の 9 射が 4.092〜4.176 秒に収まった。1・3 発目に 40.8 秒・19.4 秒が残る」。
- 01 の書いた範囲「4.065〜4.176 秒」の下限 **4.065 は `C-ec0-2`（:58）** であり、引用範囲 `-4〜12` の外＝**下限は 4.092 が正**。

### 2-3 (m) 「無い」の 2 主張

| 主張 | 判定 | 反証 |
|---|---|---|
| 「`stream_format="sse"` は **probe に記載なし**・全文検索で該当ゼロ」（01 §1.10 U-3） | **REFUTED（部分）** | `probe/irodori-tts-probe-report.md:87` に逐語で載る＝`{model, input(1..4096字), voice(string\|object\|null), response_format, speed(0.25..4.0), stream_format, irodori:{...}}`。さらに `probe/irodori-tts-openapi.json` にも欄として存在。**「実射が無い」という実質は CONFIRMED**（撃った記録は全 probe 生データに 1 件も無い）。なお `probe/irodori-tts-gradio-route-raw.txt:10` に `sse-wait(合成本体): 3978 ms` があり、**7861 の Gradio 経路では SSE を実射している**（Gradio 自身の SSE であって Server の `stream_format` ではない）＝「SSE を一度も踏んでいない」と書くと事故る |
| 「bat の CRLF の詰まりが guide にも report にも無い＝**詰まった記録は無い**（未確認）」（01 §2.3・§4.6 ?-1） | **REFUTED** | probe/ に無いのは正しいが、**記録は docs 側にある**。`ywk2/docs/log.md:5503-5505` 逐語＝「**実機 `irodori-server-gpu.bat` の `IRODORI_PRELOAD`／`IRODORI_MODEL_PRECISION=bf16` が /health 実応答で実効していなかった（bat の文字コード事故＝修理済み・以後は本体の精度セレクタが環境変数を直接注入するため bat 非依存）**」。裁定側にも `decisions.md:8305-8306` 逐語「**bat 経由は文字コード事故の実機実証により不採用**」。⇒ **「詰まらなかった」ではなく「詰まって、その結果 bat 経由そのものが不採用になった」**が正 |

補足＝この事故は **CRLF（改行）ではなく文字コード**と記録されている。`crlf`／`改行コード` の語は
yomiwakechan2 全体（binary 除く）で irodori 関連に 1 件も無い＝**「CRLF」と特定して書くのは根拠がない**。

### 2-4 (l-4) `sha16` は SHA256 そのものではない

`probe/irodori-tts-determinism-raw.txt:2-3` の欄は `"sha16":"1FEC07267F3B128D"`＝**16 桁の 16 進**。
つまり比較されているのは **SHA256 の先頭 64 bit** であって、01 §1.9 の「**SHA256 完全一致**」は生データが示す以上の断定。
実害はほぼ無い（衝突確率無視可）が、**報告で「バイト列一致」と書くなら根拠は `bytes` 欄の一致（376364）＋`sha16` 一致の 2 本立て**にするのが正直。
（bf16 側 `probe/irodori-tts-ref-latency-raw.txt:39-40` も同じく `sha16=385AF4E1B2DD3C8A` の一致。）

### 2-5 (n) rocm-setup-guide ＝逐語 5 点すべて一致

| 項 | 行 | 逐語 |
|---|---|---|
| index URL | `probe/irodori-tts-rocm-setup-guide.md:69` | `--extra-index-url https://stable.repo.amd.com/rocm/whl-next/ --index-strategy unsafe-best-match` |
| torch 版 | 同 `:69` | `"torch[device-gfx1151]==2.13.0+rocm10.0.0" "torchaudio==2.11.0.2+rocm10.0.0"` |
| Python 3.12 | 同 `:57`／`:61-62` | `uv venv .venv-rocm --python 3.12`／「**Python は 3.12 を指定すること**（ROCm 版 torch は 3.11〜3.14 のみ対応。サーバ付属の古い箱は 3.10 なので使い回せない。だから箱を分ける）」 |
| sentencepiece | 同 `:86`（`:84-86` が overrides 3 行）／`:89-90` | `sentencepiece==0.2.1`／「（3 行目は、古い sentencepiece が Python 3.12 に対応しておらずエラーになるのを避けるための差し替え。動作への影響はない。）」 |
| uv run 禁止 | 同 `:126` | 「**注意: `uv run` で起動してはいけない**（古い CPU の箱の方を掴んでしまう）」 |

付随で一致を確認した行＝`:25-26`（5〜6GB／空き 15GB）・`:75`（ROCm 本体の別途導入は**ない**）・
`:74`／`:181`（対応表 URL）・`:121-123`（ec=0／PRELOAD／venv 直起動の 3 行）・`:132-133`（5 行を bat 化）・
`:152-153`（10 字≒4 秒・50≒10・200≒41・GPU メモリ 6.6GB）・`:171`（CPU への戻し方）。**行ずれ 0**。

### 2-6 (o) openapi 実集計（JSON を機械で数えた）

`probe/irodori-tts-openapi.json` を `json.load` して数えた結果（lab の venv・`PYTHONDONTWRITEBYTECODE=1`・`HF_HUB_OFFLINE=1`）：

- `IrodoriOptions.properties` ＝ **44 欄**。
- **`anyOf` に `{"type":"null"}` を持たない欄＝0**（＝**全欄 null 許容**）。
- **`default` を持つ欄＝0**（openapi 上の既定はゼロ）。
- **`enum` を持つ欄＝3**＝`t_schedule_mode ['linear','sway']`／`decode_mode ['sequential','batch']`／`cfg_guidance_mode ['independent','joint','alternating']`。
- `additionalProperties: true`。`required` なし。
- 参考＝`SpeechRequest` は 7 欄（`model,input,voice,response_format,speed,stream_format,irodori`）・`required=['model','input']`・
  `additionalProperties: true`・**`default` を持つのは `speed`＝1.0 の 1 欄のみ**・`input` は `minLength 1 / maxLength 4096`。

⇒ 01 §3.3 の 44 欄・全欄 null・enum 3 欄・「既定値の記載が一つも無い」は**すべて機械照合で一致**。

### 2-7 便乗で 1 件

01 §3.1 の見出し「**全エンドポイント（5 パス・7 オペレーション）**」は誤り。
`paths` は 5 だが**オペレーションは 8**（`/health` 1・`/v1/models` 1・`/v1/audio/voices` 2・`/v1/audio/voices/{voice_id}` 3・`/v1/audio/speech` 1）。
**同ノートの表自体が 8 行ある**＝見出しの数え違い。報告に「7 本の口」と書くと openapi と合わない。

## 3. 反証を試みて**できなかった**もの（＝主張が強い箇所）

- (a)(b)(k) の**逐語**は原文と 1 字ずれずに一致。特に `Capabilities.cs:14-19` の
  「任意文字列（irodori 便・裁定5）。**選択肢の照合も長さ上限も課さない自由記述**…値は既存の `ParamValueKind.Choice` の器が運ぶ（`ParamValue`・DTO・JSON は無改造）」と
  `VoiceResolver.cs:148-149` の「任意文字列は素通し＝選択肢照合も長さ上限もしない…**型違いだけ既定値へ畳む**」は、
  02 の引用が完全に正しい。
- (i) `IrodoriConstants.cs:223-230` の `PrecisionEnvironment` は逐語どおりの純関数＝
  `fp32` に**一致したときだけ空辞書**、それ以外は 2 変数（`IRODORI_MODEL_PRECISION`／`IRODORI_CODEC_PRECISION`＝`bf16`）。
  02 §2-6 の記述（bf16・空・欠落・未知値はすべて注入）が正しい。
- (k) 記述子 5 本の順・Kind・既定＝`caption`(Text/空)→`voice`(Text/空)→`seed`(Text/空)→`speed`(Number/1.0・0.25〜4・step 0.01・`MasterSpeed=Multiply`)→`volume`(Number/1.0・0〜1・`IsVolume`)。
  定数実値＝`SpeedMin 0.25m`(:428)・`SpeedMax 4m`(:431)・`SpeedDefault 1m`(:434)・`VolumeMin 0m`(:440)・`VolumeMax 1m`(:443)・`VolumeDefault 1m`(:446)・`NumberStep 0.01m`(:449)。
  表示名＝`CaptionDisplayName "プロンプト"`(:356)・`SeedDisplayName "シード"`(:359)・`VoiceDisplayName "参照音声"`(:386)。
- 02 が引いた行番号は**実見した 20 箇所超がすべて一致**（`IrodoriConstants.cs:35,52,60-65,67-73,269-272,303-319,321-326,550-610,621`／
  `IrodoriServerOutput.cs:6-21`／`IrodoriCaptionPresetStore.cs:14-15,18-21`／`IrodoriPrefetcher.cs:11-24`／`IrodoriSynthesizer.cs:19-25`）。

## 4. (c) の補正＝感情軸を立てるときの実際の落とし穴

02 §5-1／§6-2 は「`IsEmotion` の器は既存＝Core を 1 バイトも触らずに感情軸 N 本が載る」と書く。器の存在は CONFIRMED
（`Capabilities.cs:78-83` 逐語「**感情系パラメータの識別（GUI のグループ見出し「感情」の判別に使う。依頼C-2 確定）**。
基本/感情の区別はエンジン側の事実であり、各アダプタが設定する」）。ただし**振り分けの実装は先勝ちである**——
`ywk2/yomiwakechan2/UI/ViewModels/Settings/VoiceEditorViewModel.cs:228-233` 逐語：

```csharp
// 振り分けは記述子の 2 つの申告だけで決まる（幅が要るか → 感情か）
var target = item.IsFullWidth ? WideParams
           : descriptor.IsEmotion ? EmotionParams
           : BasicParams;
```

⇒ **`PrefersFullWidth` が真なら `IsEmotion` は無視されて横長帯に行く**。Irodori の caption と voice は
`PrefersFullWidth = true`（`IrodoriConstants.cs:561,574`）なので、**「プロンプトを感情欄に入れる」形は現行 UI では成立しない**。
数値の感情軸（スライダー）を N 本立てる形なら `IsFullWidth=false` なので素直に「感情」グループへ入る＝**02 の結論自体は生き残る**。
併せて＝パラメータ本数は**キャラごと**に持てる（`Capabilities.cs:177-184` 逐語「キャラの能力。**パラメータの本数・種類はキャラごとに違う**（v1実証）」）が、
Irodori は擬似キャラ 1 件なので実質**エンジン全体で 1 組**である。

## 5. 主席への注意（報告で書き直すべき箇所）

1. **「ec=0 でスパイク消滅」を報告に書かない**。生データに 19.43 秒（`probe/irodori-tts-gpu-latency-raw.txt:59`）が残る。
   書くなら「**12 連射のうち 4 発目以降の 9 射が 4.09〜4.18 秒**。1・3 発目に 40.8 秒・19.4 秒が残る」＝⒞の「安定ノブ」の売り方が変わる（万能ではない）。
2. **「10 発おきに +15 秒」は原報告の断定であり生データからは検証できない**。報告では「**約 +15 秒のスパイクが再発する（周期は未検証）**」に落とす。
3. **「bat で詰まった記録は無い」を書かない＝逆である**。`docs/log.md:5503-5505` に実機事故（環境変数が黙って効かない）が記録され、
   その結果 `decisions.md:8305-8306` で **bat 経由が不採用**になっている。⒜専用インストーラの論拠として**強い**材料なので、拾い直す価値がある
   （「bat をユーザに書かせる形は本体が一度捨てた道」）。ただし**「CRLF」と特定しない**（記録の語は「文字コード事故」）。
4. **「SSE は probe に一件も無い」を「実射が無い」に書き換える**。欄名は `probe/irodori-tts-probe-report.md:87` に載っており、
   7861 側では Gradio の SSE を実射している（`probe/irodori-tts-gradio-route-raw.txt:10`）。⒞の目玉（初音短縮）の未測性は変わらないが、
   「見落としていた」ではなく「**契約は把握済み・実射だけ未了**」と書く方が正確で、1 射で埋まる穴だと伝わる。
5. **「GPU 指定は 9 エンジン通じて前例ゼロ」を 2 段に割る**。⑴**推論デバイスを選ぶ**前例＝ゼロ（正しい）。
   ⑵しかし**デバイスを名前で保存し添字へ再解決する器**は本体に既にある（`Engines/Common/AudioOutputDevices.cs:21,24,52`・`AppSettings.cs:533`）し、
   **エンジンの device を受け取って表示する**前例もある（COEIROINK＝`CoeiroinkSpeakerInfo.cs:36-37`・`CoeiroinkDiscovery.cs:66`）。
   ⒞の GPU 指定は「無から作る新機軸」ではなく「**既存 2 型の合成**」として書ける＝工数見積りと説得力の両方に効く。
6. **`sha16` は SHA256 の先頭 16 桁**。「SHA256 完全一致」ではなく「**同一 seed で `sha16` と `bytes` がともに一致**」と書く。
7. **openapi の口の数は「5 パス・8 オペレーション」**（01 §3.1 の見出し「7」は誤り。表の行数は 8 で正しい）。
8. **引用の根を 1 本に決めてから書く**。C# は `<repo>/yomiwakechan2/…`、`docs/`・`decisions.md`・`probe/` は `<repo>/…` にある。
   02 §0 の規約文（「`yomiwakechan2/…` ＝ `C:/…/repos/yomiwakechan2/…`」）を字義どおり畳むと**存在しないパス**になる。
   報告は「リポ根＝`C:/Users/mugonkun/source/repos/yomiwakechan2`」と宣言し、**C# だけ `yomiwakechan2/` が 1 段挟まる**と明記すること。
9. **02 の構造主張は全部使ってよい**（a〜k・n・o は反証できなかった）。特に **230ms/字＝生成音声の長さ**（`IrodoriConstants.cs:321-326`）・
   **裁定 5 の 4 箇所限定**（`docs/irodori-tts-requests.md:136-144`）・**⒝の前例＝音声認識内包便**（`decisions.md:8486-8489`・`:8549-8554`）は
   逐語まで一致したので、報告で強く引いてよい。
10. **感情軸を UI へ出す案を書くなら `PrefersFullWidth` との先勝ちに触れる**（`VoiceEditorViewModel.cs:230-232`）。
    「感情」グループに入るのは**幅を要求しない記述子だけ**＝プロンプト（横長）を感情欄に置く絵は現行 UI では描けない。
11. **本席が確認していないこと**＝upstream の実体・ライセンス原文・ONNX 変換の可否・CUDA 実測は担当外。
    01 §4 の⒜⒝⒞への「効き方」欄（B-1〜B-6・D-1〜D-12 等）は**解釈であって実測ではない**ため、本席は検分していない＝**未確認**。
