# ben-f-handoff.md — 便 F（本体 yomiwakechan2 の「10 番目のエンジン」）への申し送り

> **正典は `decisions.md` と `docs/contract.md`（配布版側）と本体側の `decisions.md` である。
> 本檔はそこへの索引と要約であって、食い違ったら正典が勝ち、誤っているのは本檔である。**
> 最終の証拠は**契約テスト**（`tests/contract/**`）＝「本書と実装が食い違ったら実装が正で本書が誤り」
> ではなく、**契約テストが正**である（`docs/contract.md` ⑻）。テストが緑でない変更は入らない。
>
> 逐語はすべて出典（檔名と節）つきで書いた。**この檔は何も書き換えない**＝本体（別リポ・別席）が写して使う。
> 書いた席＝便 D（3）の申し送り筆記席（2026-09-05・Radeon 機・gfx1151）。
> 逐語の突合先＝`docs/contract.md`・`server/ywk_server.py`・`server/ywk_params.py`（突合の結果は §10）。
>
> **本体側で必ずセットになる改修 1 件**＝接続先。現行は `IrodoriConstants.cs:35` の固定値 8088・
> `App.xaml.cs:131` は baseUrl を渡していない（`docs/contract.md` ⑼ **D-9**）。
> 配布版は **18088** に確定（`decisions.md` 2）なので、**18088 にする以上 D-9 は必ず要る**。

---

## §0 一枚（最短の路）

| 段 | 口 | 判定 | 失敗したら |
|---|---|---|---|
| 1 | `GET http://127.0.0.1:18088/health` | **200**（モデル未読込でも 200） | 到達不能＝エンジン未起動として理由つき失敗。**本体の一括起動が引数なしでランチャの exe を起こしてよい**（`decisions.md` 103＝従来型 TTS と同じ扱い・裁定 6 の「見つけるだけ」を覆す） |
| 2 | `GET /ywk/status` | `engine == "irodori-ywk"` | **404 なら上流の素の Server（8088 経路の個体）＝配布版として扱わない**（`docs/contract.md` ⑵） |
| 3 | `GET /params` | 200・**≤ 100 ms**・`schema` を読む | 404 なら同上 |
| 4 | `GET /v1/audio/voices`（表示名が要るなら `GET /ywk/voices`） | 200・**「デフォルト」が必ず先頭に 1 件** | 配布版は台帳が壊れても **500 を返さない**（⑷・D-8 は消える） |
| 5 | ready 待ち | **`/ywk/status.runtime.loaded == true`**・**2 秒間隔**で待ちは **120 秒**（裁定 105 ⑸）。**`runtime.error` が非 null になったら、そこで理由つき除外**（時間切れも同じ）。元栓（一括起動）の完了検知は **`/health` 200 の疎通だけ**でよい | `loaded=false` の間に合成を撃つと**エラーにならず待たされて 200 が返る** |
| 6 | `POST /v1/audio/speech` | 200＋`RIFF`/`WAVE` の wav | 4xx／5xx は **`error.code`（`ywk_` 接頭辞）で分岐する**（文言依存を捨てる） |

- **`localhost` の名前指定は禁止・必ず `127.0.0.1`**（`localhost` は IPv6 を先に試すので**毎要求 +1.2〜2.0 秒**）。
- **`Authorization` を付けない**（api_key は無い・付けても無視される）。
- 既存 8088（本体の 9 番目のアダプタ `irodori`）は**そのまま**。配布版は**別個体**で同時に動いてよい。

---

## §1 発見（正典＝`docs/contract.md` ⑵）

### 1-1 4 段の逐語

`docs/contract.md` ⑵ の表を逐語で：

| 段 | 口 | 何を得るか | 失敗時 |
|---|---|---|---|
| 1 | `GET /health` | 生きているか。**モデル未読込でも 200**（遅延ロードが正常形） | 到達不能＝エンジン未起動として理由つき失敗 |
| 2 | `GET /ywk/status` | 配布版か否か・版・上流 pin・**実 device**・話者件数 | 404 なら**上流の素の Server**（＝8088 経路の個体）＝配布版として扱わない |
| 3 | `GET /params` | パラメータ一覧。**モデル未読込でも 200・≤ 100 ms** | 404 なら同上 |
| 4 | `GET /v1/audio/voices` | 話者一覧 | 500 は「台帳が壊れている」＝配布版では**返さない** |

**`/health` の body は上流のままで、解釈しない**（`docs/contract.md` ⑵）。ただし配布版は
`/health` の絶対パスも `<path>` に畳む。**`/health.model.model_device` は素の echo**＝`auto` は
`auto` のまま返り、**実際に載った device は `/health` からは分からない**。
**`/health.voices.files` は話者数ではなく音声拡張子の檔数**＝ここから話者数を数えると外れる。

### 1-2 `GET /ywk/status` の逐語（**実装で確認した全 14 欄**）

`server/ywk_server.py` の `ywk_status()` が返す鍵の並びそのまま（`docs/contract.md` ⑹ の逐語＋
その後に足った欄。**欄を足すのは `schema` を上げない**＝⑻）：

```json
{"engine":"irodori-ywk",
 "version":"1.1.0",
 "variant":"cuda|cpu|rocm-gfx1151",
 "upstream":{"irodori_tts":"8224daf","server":"841fb7c"},
 "host":"127.0.0.1","port":18088,
 "pid":12345,
 "runtime":{"loaded":bool,"loading":bool,"error":"…|null"},
 "device":{"configured":"cuda:0","codec_configured":"cuda:0","actual":"cuda:0|null",
           "precision":"bf16|fp32",
           "name":"…|null","uuid":"…|null","pci_bus_id":"…|null","hip":"…|null","gcn_arch":"…|null"},
 "torch":{"version":"2.13.0+rocm10.0.0","cuda":"…|null","hip":"…|null"},
 "voices":{"count":13,"dir":"<末尾 1 段のみ>","error":"…|null"},
 "memory":{…§4-4…},
 "warmup":{…§5-2…},
 "precompute":{…§5-3…}}
```

本体が読むのは太字の 6 つだけでよい：

- **`engine`**＝`"irodori-ywk"`（`ywk_params.ENGINE`）。**これが配布版の名乗り**。
- **`version`**＝配布版の版（実装の `YWK_VERSION` は現在 **`"1.1.0"`**）。
- **`upstream`**＝上流 pin（`{"irodori_tts":"8224daf","server":"841fb7c"}`）。
- **`device.actual`**＝**モデル読込後に実測した値**（`next(model.parameters()).device`）。
  **設定値の echo ではない・未読込なら `null`**。`device.configured` の方が echo。
- **`runtime.loaded`**＝**ready の判定はここ**（`docs/contract.md` ⑵）。
- **`runtime.loading` / `runtime.error`**＝**ポート先行（裁定 105）の 2 欄**。配布版は
  `preload=false` を焼いて**bind してから裏でモデルを載せる**ので、bind 直後（exe から約 9 s・実測 8.9 s）から
  `{loaded:false, loading:true, error:null}` が 20〜70 秒ずっと返るのが**正常形**である。
  読込に失敗した個体は**死なずに** `{loaded:false, loading:false, error:"<理由 1 行>"}` を返し、
  `/health` も `/ywk/status` も 200 のままである（＝本体はそこで理由つきに除外できる）。
  **`error` は「載らなかった」以外を意味しない**（是正・便 G）＝出る三つ組はこの 3 つだけで、
  **載っている個体の合成が 5xx で落ちても `error` には出ない**（CUDA OOM・上流の「待ち行列が
  一杯」503）。だから**除外の判定は `error` 単独ではなく `loaded=false` と併せて読む**のが安全で
  あり、そう読めば 1 回の合成の失敗で健全な個体を落とすことは無い。理由 1 行は
  **絶対パスを `<path>` に畳んである**（⑹ と同じ規則・裏読込の路も遅延読込の路も同じ整形）。
- **`pid`**＝**この応答を返した個体の pid**（`os.getpid()`）。

### 1-3 `pid` の突合＝**`/health` 200 はエンジンの保証にならない**（裁定 83）

**既にそのポートを握っている個体が居れば、同じ形で答えてくる**（`docs/contract.md` ⑹）。
〔**裁定 105（ポート先行）で窓は縮んだが閉じていない**＝1 巡目の理由は「`preload=true`
（`decisions.md` 7）はモデルを載せてから bind するので、起動の 20〜28 秒（gfx1151 の実測）の間は
別の個体が同じポートを握れる」だった。`preload=false` の今は自分の子が 1 秒以内に bind を
試みるが、**先客が居れば子は bind に失敗して落ちる**＝その間ずっと先客の応答が返る。〕
ランチャはこれを `pid` で突合している。

さらに**裁定 83 の実射**＝「起動は健全に見え（`/health` 200 を 50.8 s で・`/params`・`/ywk/status` も
200・device.actual=cpu）、**最初の合成で `prepare_reference: 0.1 ms` の直後にプロセスが
0xC0000005（STATUS_ACCESS_VIOLATION・-1073741819）で消える**。Python 例外は出ず、サーバのログにも
残らず、クライアントには `ConnectionResetError 10054` だけ」（`decisions.md` 83・cu130 を GPU を隠して
起こした場合）。

⇒ **本体の扱い（推奨）**：

1. **`/health` 200 だけで「使える」と決めない**＝`/ywk/status` の `engine` と `runtime.loaded` まで見る。
2. **接続断（`ConnectionResetError`／`SocketException`）は「サーバが消えた」として扱い、
   HTTP の期限を待たずに理由 1 行で失敗させる**。ランチャ側は同じことをしている
   （`decisions.md` 88 ⑶「合成中の消失は『試す』画面にも『サーバが落ちました（exit −1073741819）』と
   出し、HTTP の timeout を待たない」）。
3. **`pid` が変わったら「別個体になった」と読む**＝`pid` は毎回の `/ywk/status` に載る。
   **欄が無い個体（古い wrapper・上流の素の Server）とは突合しない**
   （`docs/design/ben-d-launcher.md` §20-5 ⑵「`pid` の突合は『欄が在るときだけ』」）。
   本体は**プロセスの所有者ではない**（起こすのも後始末もランチャ＝`decisions.md` 6→124・一括起動で起こすのは可＝103）ので、
   exit code は取れない＝**pid の変化と接続断が本体に見える全部**である。

### 1-4 ready 待ち

**ポート先行（裁定 105）で「元栓」と「スキャン」が別の物差しになった。**

| 段 | 何で判るか | 期限 | 外れたら |
|---|---|---|---|
| 元栓（一括起動の完了検知） | **`/health` 200 の疎通だけ** | exe から **約 9 s**（ランチャの門 5〜6 s＋wrapper の bind 3〜4 s・実測 8.9 s＝`decisions.md` 106・待ちは 120 s のまま） | 到達不能＝起こせなかった |
| スキャン（使えるか） | **`/ywk/status.runtime.loaded`** を **2 秒間隔**で見る | **最大 120 秒** | **`runtime.error` が立つか時間切れ**＝**理由つきで除外**（`error` の 1 行をそのまま出せる） |

- **`/health` 200 は「起きている」であって「使える」ではない**＝配布版は `preload=false` を焼き、
  bind してから裏の糸でモデルを載せる。**200 は exe から約 9 秒で返り**（実測 8.9 s＝`decisions.md` 106）、
  `runtime.loaded=true` はその 20〜70 秒後に来る（Radeon 実測 34.5 s・3090 20〜28 s）。
- **待ちは 120 秒**（`docs/contract.md` ⑵。本体の `LaunchCompletionTimeout=120 s` は妥当と正典が書いている）。
- **読込に失敗した個体はプロセスが生きたまま `runtime.error` に理由を載せる**（裁定 105 ⑴）＝
  **`/health` 200 のまま**なので、疎通だけを見ていると「起きているのに永久に載らない」個体を
  掴み続ける。スキャンで `error` を見て除外すること。
- **`loaded=false` の間に `POST /v1/audio/speech` を撃つと、エラーにならず待たされて 200 が返る**
  ＝合成期限の設計はこれを飲み込む値にすること（同 ⑵）。上流の遅延読込に**合流して待つ**ので
  二重に載ることはない（裁定 105 ⑶）が、**本体は `loaded` を見てから撃つ**のが正である。
- `/openapi.json`・`/docs`・`/redoc` は上流のまま 200 で出るが、**本体は使わない**
  （`/docs` は CDN を引くのでオフライン機では白紙）。

---

## §2 設定可能パラメータ（`GET /params`・正典＝`docs/contract.md` ⑸）

### 2-1 なぜ固定表を持ってはいけないか

上流の `/openapi.json` には `IrodoriOptions` 44 欄が載るが、**`default` 0 件・`minimum`/`maximum` 0 件・
`description` 0 件**（実射・機械集計）。範囲があるのは `SpeechRequest.speed`（0.25〜4.0）と
`input`（1〜4096）だけである。さらに**既定値は 3 層**（dataclass 43 欄／`IRODORI_DEFAULT_*` 22 欄／
要求本文）で **env で動く**＝「本体に固定表を持つ」は運用で嘘になる。
**`/params` が返す `default` は、起動中のプロセスの実効値**（env 反映後）である（`docs/contract.md` ⑸）。

### 2-2 応答の骨（**実装で確認した最上位 9 欄**）

```json
{"schema": 1, "engine": "irodori-ywk", "version": "1.1.0", "model_loaded": false,
 "checkpoint": {"hf": "Aratako/Irodori-TTS-v4.1-Small",
                "max_text_len": null, "max_caption_len": null, "ref_max_seconds": null},
 "request": { … 6 欄（下） … },
 "irodori": [ … 44 欄の配列 … ],
 "rules":  { … 下 … },
 "prefetch": {"available": false, "planned": "/ywk/prefetch"}}
```

- **`schema` は `/params` にだけ載る**（§10 ⑴）。`model_loaded: false` でも 200。
- **checkpoint 依存の 3 欄（`max_text_len`・`max_caption_len`・`ref_max_seconds`）は読込前は `null`**
  ＝**モデルを読まないと分からない**ので、固定表に書くと必ず嘘になる（`docs/contract.md` ⑸ 5-3）。
- **`prefetch.available` は `false`**＝**本体はこの口を叩かない**。`true` に変わるのは
  `docs/contract.md` ⑻ の手順を踏んだときだけ（先読みは⑺ 7-1 の予約）。

### 2-3 `request`（top-level）＝**6 欄**

実装の `ywk_params.REQUEST_PARAMS` の並び順そのまま＝
**`model`・`input`・`voice`・`speed`・`response_format`・`stream_format`**。
（`docs/contract.md` ⑸ 5-1 の逐語は**抜粋**で 4 欄しか載せていない＝`model` と `stream_format` は
逐語の外・実装には在る。§10 ⑷）

各欄は `{type, label, description, min/max/step または min_length/max_length, enum,
range_source, note, required, default, default_source, nullable, exposed_to_ywk}` の形で来る。
`voice` の逐語（`docs/contract.md` ⑸ 5-1）：

```json
"voice": {"type":"string","default":"デフォルト","default_source":"ywk","required":false,
          "nullable":false,"description":"話者名（… の id）。「デフォルト」= 参照なし",
          "note":"voice を省くと IRODORI_DEFAULT_VOICE が使われる…（decisions.md 45）"}
```

### 2-4 `irodori`（44 欄の配列）の 1 欄の読み方

逐語（`docs/contract.md` ⑸ 5-1）：

```json
{"key":"num_steps","type":"integer","default":40,"min":1,"max":120,"step":1,"nullable":false,
 "exposed_to_ywk":true,"presets":[10,40],"group":"quality","label":"サンプリング歩数",
 "range_source":"上流 gradio_app.py のスライダ（UI の範囲であって検査ではない）（:481・minimum=1 maximum=120 step=1）"}
```

- **`exposed_to_ywk`**＝この欄を**本体が `ParamDescriptor` に写してよいか**。
- **`range_source`**＝**範囲の出所**（「上流に範囲検査は無い／gradio スライダ由来／配布版の推奨」の別）。
  **上流が保証していない数字を、出所を伏せたまま本体の UI に名乗らせない**（`docs/contract.md` ⑸ 5-2）。
  実装の 5 種の逐語＝`上流 gradio_app.py のスライダ（UI の範囲であって検査ではない）`／
  `上流 gradio_app_voicedesign.py のスライダ（UI の範囲であって検査ではない）`／
  `配布版の推奨（上流の HTTP API に範囲検査はない）`／`上流 inference_runtime.py の検査`／
  `上流 app.py の検査`（`server/ywk_params.py` の `_SRC_*`）。
- **`nullable` は全 44 欄に載る**＝`true` なら「欄を出さない・`null` を送る＝上流の既定」。
- **`default_source`** の取りうる値＝`dataclass`／`ywk`／`settings`／`settings_optional`／`checkpoint`／
  `unset`。**`default: null` が許されるのは後ろの 3 つだけ**（`NULLABLE_SOURCES`）。

### 2-5 `/params.rules`＝**本体が読む一枚**（実装の `build_rules()` の全 14 鍵）

```json
{"priority": "irodori.X > top-level X > env",
 "literal_must_be_nested": ["t_schedule_mode","decode_mode","cfg_guidance_mode"],
 "voice_and_no_ref_exclusive": true,
 "voice_and_reference_exclusive": ["ref_wav","ref_wavs","ref_latent","ref_latents","ref_embed"],
 "unknown_field": "400",
 "out_of_range": "400",
 "response_format": ["wav"],
 "null_means_unset": true,
 "null_normalized_by_wrapper": ["ref_normalize_db","max_ref_seconds","first_sentence_chunk_min_chars"],
 "empty_string_means_unset": ["caption","seed"],
 "no_ref_voice_aliases": ["no-ref","no_ref","none","null","text-only"],
 "default_voice": "デフォルト",
 "exposed_to_ywk": ["caption","cfg_scale_caption","cfg_scale_speaker","cfg_scale_text",
                    "num_steps","seed","speed","sway_coeff","t_schedule_mode","voice"],
 "error_code_prefix": "ywk_"}
```

（`response_format` と `error_code_prefix` は `docs/contract.md` ⑸ 5-1 の**抜粋の外**・実装には在る＝§10 ⑷。
`no_ref_voice_aliases` は `sorted()` 済みなので上の並びが実物である。）

- **`rules.exposed_to_ywk` は 10 件**＝`irodori` 側 8 欄（`caption`・`cfg_scale_caption`・
  `cfg_scale_speaker`・`cfg_scale_text`・`num_steps`・`seed`・`sway_coeff`・`t_schedule_mode`）
  ＋ top-level 2 欄（`voice`・`speed`）。**この配列が正**＝
  `docs/contract.md` ⑸ の散文「9 欄＋`voice`・`speed`」（と `docs/acceptance.md` の同文）は
  **数え違いだった**（§10 ⑵＝`decisions.md` 99 で 8 欄に直した）。**本体は配列を読む**＝数を焼かない。

### 2-6 `ParamDescriptor` への写像（`docs/contract.md` ⑸ 5-2 の表を逐語）

| `/params` の `type` | 本体の `ParamKind` | 使う欄 |
|---|---|---|
| `number` | **Number** | `min`・`max`・`step`・`default`（`Core/Models/Capabilities.cs:47-62` の Number 用任意欄がそのまま埋まる） |
| `integer` | **Number** | 同上（`step: 1`） |
| `enum` | **Choice** | `enum`（選択肢）・`default` |
| `boolean` | **Flag** | `default` |
| `string` | **Text** | `caption`・`seed` など。`max_length` があれば入力長の上限に |

- **`cfg_scale_text` の `label` は「感情表現の強さ」**＝本体はこれをそのまま `DisplayName` にする（§4-2・§6・`decisions.md` 99）。
  **`IsEmotion` を立てる**（`decisions.md` 100＝本体の「感情」見出しの帯に出す。AivisSpeech の `intonationScale` は基本帯＝表示名だけ同じ）。**`group` は配布版自身の UI 用＝本体は帯の判断に使わない**
  （`caption`・`cfg_scale_text`・`cfg_scale_caption` は 3 欄とも `group:"emotion"`＝`max_caption_len` も同じ群だが、本体の帯は記述子の申告＝`IsEmotion`／`PrefersFullWidth` で決まる）。
  **`description` は `NoteToolTip` に写してよい・`note` は写さない**（実装席向けの注記＝裁定番号や上流の行番号を含む）。
  ただし**本体の Number の帯は現状 `Note`／`NoteToolTip` を出さない**（`SettingsTemplates.xaml` の `NumberParamViewModel` の雛形に束縛が無い・出るのは Text だけ＝`TextParamViewModel.cs:82-83`）＝出したいなら本体側の 1 手が要る。
- **`exposed_to_ywk: true` の欄に `default: null` は 0 件**（「既定が決まっていない」を返さない）。
  `caption`・`seed` のように「未指定」が意味を持つ欄は **`default: ""`＋`nullable: true`** で表す
  ＝**`""` を送り返しても 400 にならない**。
- **本体が写すのは `exposed_to_ywk: true` の欄だけ**。44 欄を全部 UI に出すのではない＝
  `ref_wav`・`ref_latent`・`ref_embed`・`lora_adapter` のようなパス欄や
  `duration_scale`（`speed` と除算合成される）は `exposed_to_ywk: false` で、**配布版の UI だけが扱う**。

### 2-7 既知の落とし穴（`docs/contract.md` ⑸ 5-3）

- **`cfg_scale_caption` は env を持たず、`default_cfg_scale_text`（3.0）を流用する**＝
  `IRODORI_DEFAULT_CFG_SCALE_TEXT` を動かすと caption 側の既定も一緒に動く。
  gradio の初期値は 4.0＝**UI と API で既定が違う**。`/params` は Server の実効値 **3.0** を採り、
  gradio の 4.0 は `note` に残す（`decisions.md` 33）。
- **`speaker_kv_min_t`** は `speaker_kv_scale` を指定したときだけ 0.9 に解決される（`note` に在る）。
- **`speaker_uncond_mode`** は HTTP から触れない（`ref_embed` のときだけ効く）。

---

## §3 話者（`GET /v1/audio/voices`・`GET /ywk/voices`・正典＝`docs/contract.md` ⑷）

### 3-1 `GET /v1/audio/voices`（上流互換の形・**配布版が覆っている**）

```json
{"object": "list",
 "data": [{"id": "デフォルト", "object": "voice", "display_name": "デフォルト",
           "preset": false, "no_ref": true, "latent": false, "latent_stale": false},
          {"id": "琴葉茜（関西弁）", "object": "voice", "display_name": "琴葉茜（関西弁）",
           "preset": true, "no_ref": false, "latent": true, "latent_stale": false}]}
```

- **パス欄（`ref_wav`・`ref_wavs`・`ref_latent`・`ref_latents`・`ref_embed`）は落としてある**＝
  上流は `ref_wav` に**利用者の絶対パスをそのまま返す**ので、配布版はこの route を差し替えている。
- **「デフォルト」が必ず 1 件・先頭**。**`voices.json` が無くても・壊れていても 1 件は必ず出る**。
- **`voices.json` が壊れていても 500 を返さない**＝「デフォルト」1 件だけの 200 を返し、
  理由を `GET /ywk/voices` の `error` と `GET /ywk/status.voices.error` に載せる。
  ⇒ **`docs/contract.md` ⑼ の D-8（`voices.json` 破損を発見段で検知できない）は配布版で消える。**
- **上流由来の `none` は一覧に出ない**（`IRODORI_ALLOW_NO_REF_VOICE=false` を焼いてある）。
- **話者 id に日本語・空白・記号が使える**（走査と合成は voice_id を一切検査しない）。
- **プリセットの表示名（＝話者 id）は版で変わりうる**（裁定 108＝初件は「もち子さん」→
  「もち子さん（セクシー／あん子）」）。ランチャが起動時に利用者の台帳を改名するので旧い id は
  一覧から消え、送れば `ywk_unknown_voice`（⑶ 3-3 の 1 行目）＝
  **id を版をまたいで固定せず一覧を読み直す**こと。

### 3-2 `GET /ywk/voices`（配布版の口・**表示名と理由が要るならこちら**）

実装（`server/ywk_server.py` の `ywk_voices()`）の逐語＝**6 鍵**：

```json
{"object":"list",
 "data":[ …/v1/audio/voices と同じ配列… ],
 "default_voice":"デフォルト",
 "no_ref_voice":"デフォルト",
 "count":13,
 "error":null}
```

- **`default_voice`**＝いま焼かれている `IRODORI_DEFAULT_VOICE` の実効値
  （ランチャが上書きしていればその話者名）。
- **`no_ref_voice`**＝**参照なしを意味する予約話者の名前**＝`"デフォルト"` 固定。
- **`error`**＝`voices.json` が読めなかった理由 1 行。読めていれば `null`。
  実装の逐語＝`"voices.json を読めなかった（一覧は「デフォルト」だけ・檔を直すか消すと戻る）"`。

### 3-3 参照なし＝話者名「デフォルト」（`decisions.md` 16・`docs/contract.md` ⑷ 4-2）

- **`voice: "デフォルト"` をそのまま送って 200**。上流の素の Server に送ると **404 ではなく 400**＝
  **配布版でだけ通る**（発見段の 2 番目の判別材料になる）。
- **`voice` を省いても 400 にならない**（`decisions.md` 45）＝配布版は `IRODORI_DEFAULT_VOICE` に
  `"デフォルト"` を焼く（`setdefault`）ので、欄を出さなければ**参照なし合成**になる。
- **上流の別名 5 つ（`none`・`no_ref`・`no-ref`・`null`・`text-only`・大小無視）を送っても
  配布版は「デフォルト」に正規化して 200 を返す**（`rules.no_ref_voice_aliases`）
  ＝**本体は現行アダプタの `voice:"none"` のままでも合成できる**（`docs/contract.md` ⑼ D-4）。
  **ただし新アダプタは `"デフォルト"`（または欄ごと省略）を送るのが正**。
- 参照なしの声は **Irodori 自身の素の声**（caption のみで作る）。

### 3-4 排他（**本体が踏みやすい 2 つ**）

- **`voice` と `irodori.no_ref` の同時指定＝400 `ywk_voice_and_no_ref`。**
- **`voice` と参照 5 欄（`ref_wav`・`ref_wavs`・`ref_latent`・`ref_latents`・`ref_embed`）の
  同時指定＝400 `ywk_voice_and_reference`。**
  理由＝上流はこの 6 欄のどれか 1 つでも明示されていれば話者解決に入らず、**参照が黙って落ちる**。
- **`null` を入れた欄は「明示」ではない**＝上流も読まないので 400 にしない。
- **`no_ref: false` は「参照あり」ではない**＝`voice` を省いた `{"irodori":{"no_ref":false}}` は
  **欄ごと出さないときと同じ 200**（参照なし合成）。**`voice` との同時指定は 400 のまま。**

### 3-5 latent（`latent`・`latent_stale`）＝**Radeon 版だけの話**（`decisions.md` 65）

一覧の 2 欄は**どちらも bool・パスは出さない**：

- **`latent`**＝この話者が**焼かれた `.pt` から鳴る**（`.pt` が実在する）。
- **`latent_stale`**＝焼いてはあるが、**元の wav が変わった／消えた**＝いま鳴る声は `voices/` の檔と違う。

**本体は読むだけでよい**（表示に使うなら「潜在キャッシュ済み」程度）。**焼く・外すのは配布版の口**で、
**本体は叩かない**（§5-3）。**本体に効く 1 点だけ**＝
**焼いた後は要求時の `ref_normalize_db`／`ref_ensure_max` が効かない**（正規化は焼くときに済んでいる）
＝`decisions.md` 78 が名指しで便 F への申し送りにしている行である。
既定のまま使う限り音は変わらない（焼くときに要求時と同じ既定値を使う）。

### 3-6 所有者（`docs/contract.md` ⑷ 4-3）

- **`voices/` と `voices.json` を書くのは配布版だけ。本体は書かない・消さない・wav のパスを持たない。**
  本体がやるのは**一覧から名前で選ぶ**ことだけ。
- **上流の書き込み 3 口は口ごと外してある**＝`POST /v1/audio/voices`・`PUT /v1/audio/voices/{id}`・
  `DELETE /v1/audio/voices/{id}` は **404／405** を返す。
  ⇒ **本体の D-3（`POST /v1/audio/voices`＋`ywk-<sha12>` での話者登録）は路ごと無くなる。**
  読みの `GET /v1/audio/voices/{id}`（檔名・大きさ・更新時刻だけ）は残っている。
- **プリセット話者 12 名が配布物に同梱されている**（`decisions.md` 17・37・93 ⑴ の 11 名 ＋
  **裁定 118** の「シャンパンコール（ホスクラ）」）＝一覧では `preset: true` で区別できる。
  台帳の 13 行目（弦巻マキ英語）は司令官提供待ちで同梱されない（`decisions.md` 27・`status: skipped`）。
  裁定 118 の 1 名だけは**エンジン由来ではない**（司令官が渡した録音そのもの＝台帳の `engine` が
  `"external"`）が、本体から見た形は他の 11 名と 1 つも変わらない（`preset: true` の 1 行）。

---

## §4 合成（`POST /v1/audio/speech`・正典＝`docs/contract.md` ⑶）

### 4-1 body の逐語（`docs/contract.md` ⑶ 3-1）

```json
{"model": "irodori-tts",
 "input": "本文（1〜4096 字）",
 "voice": "話者名（/v1/audio/voices の id）",
 "response_format": "wav",
 "speed": 1.0,
 "irodori": {"caption": "演技指示", "num_steps": 40, "seed": 1234}}
```

- **`model` は `"irodori-tts"` 固定**（他は 400 `ywk_unknown_model`）。
- **`response_format` は `wav` のみ**（他は 400 `ywk_unsupported_response_format`＝
  ffmpeg を同梱しない＝第三者バイナリ不同梱）。
- **上流の 44 欄は `irodori` ネストに入れて送る**。優先順は
  **`irodori.X`（非 null）→ トップレベル `X` → env**＝**env に焼いた既定は本体の指定に必ず負ける**。
- **「既定に戻す」は欄を出さないことで表す**（`null` を送っても意味は同じ）。
- **Literal 3 欄（`t_schedule_mode`・`decode_mode`・`cfg_guidance_mode`）はネスト必須**＝
  トップレベルにあれば **400 `ywk_literal_top_level`**（上流はこの 3 欄を誰も検査せず素通しする）。

### 4-2 本体が触ってよい欄と、触ってはいけない欄

| 欄 | 置き場 | 本体の扱い |
|---|---|---|
| `input` | top-level | 必須・1〜4096 字。**空白のみは 400 `ywk_empty_input`** |
| `voice` | top-level | 一覧の `id`。省略＝参照なし（§3-3） |
| `speed` | top-level | 0.25〜4.0・step 0.05 |
| `irodori.caption` | ネスト | 演技指示。**空文字・空白のみは「未指定」に畳まれる（200）** |
| `irodori.num_steps` | ネスト | 1〜120・既定 **40**・UI のプリセットは **10／40**（`decisions.md` 10） |
| `irodori.seed` | ネスト | **`""` は「未指定」＝毎回の乱数**に畳まれる |
| **`irodori.cfg_scale_text`** | ネスト | **本体の「感情表現の強さ」**（§6・`decisions.md` 99）＝Number・0.0〜10.0・step 0.1・既定は `/params` の `default`（現在 3.0・env で動く＝焼かない）。**0 は本文 CFG を切る**（実用下限 1.0 付近）。表示名は `/params` の `label` |
| `irodori.cfg_scale_caption`／`cfg_scale_speaker`／`sway_coeff`／`t_schedule_mode` | ネスト | `exposed_to_ywk: true`（§2-5） |
| **`irodori.duration_scale`** | ネスト | **送らない**＝`speed` と**除算で合成される**（両方送ると尺が二重に効く）。**どちらか一方**にする |
| **`irodori.seconds`** | ネスト | **送らない**＝明示すると**チャンク分割が黙って無効**になり、**出力長が指定秒ちょうどになる**（14 字を 12 秒に間延びさせる・警告なし）。**暖機の射だけで使う欄**である |
| `stream_format` | top-level | **`"sse"` だけ受ける・本体は使わない**（§4-6） |

### 4-3 検査（配布版が足す＝上流には無い・`docs/contract.md` ⑶ 3-2）

| # | 検査 | 結果 |
|---|---|---|
| 1 | 未知欄（トップレベル・`irodori` ネストの両方） | **400** `ywk_unknown_field` |
| 2 | 範囲外（`/params` の min/max と同じ表を使う） | **400** `ywk_out_of_range` |
| 3 | Literal 3 欄がトップレベルにある | **400**（ネストで送れ） |
| 4 | `voice` と `irodori.no_ref` の同時指定 | **400** `ywk_voice_and_no_ref` |
| 4b | `voice` と参照 5 欄の同時指定 | **400** `ywk_voice_and_reference` |
| 5 | `response_format` が `wav` 以外 | **400** `ywk_unsupported_response_format` |

**上流はこれらを黙って通す**（`extra="allow"`・範囲検査ゼロ・`num_steps:-5` も `cfg_scale_text:999` も
200）。**綴り違いが「無音の別の音」になる沈黙は、この 5 検査で塞がる。**

### 4-4 エラー body（**本体の分岐はここだけを見る**・`docs/contract.md` ⑶ 3-3）

態＝**400／422／500／503／SSE の `event: error`**。配布版はどの態でも次の形で返し、
**機械可読な `code` を必ず入れる**（上流は `param`・`code` とも常に `null`）：

```json
{"error": {"message": "unknown field: num_step",
           "type": "invalid_request_error",
           "param": "num_step",
           "code": "ywk_unknown_field"}}
```

**`code` の一覧**（`docs/contract.md` ⑶ 3-3 の表＋実装 `_UPSTREAM_CODES`／`_STATUS_CODES` で確認）：

| 態 | `code` |
|---|---|
| 未知の話者（`Unknown voice=`） | **`ywk_unknown_voice`** ← **D-5 はこれで文言依存を捨てられる** |
| `voice` 省略（`No voice was provided`）＝**ランチャが `IRODORI_DEFAULT_VOICE` を空にした場合のみ** | `ywk_missing_voice` |
| `model` 違い | `ywk_unknown_model` |
| `input` が空白のみ | `ywk_empty_input` |
| 上流の話者 id 検査（`voice_id must contain only …`） | `ywk_invalid_voice_id`（**実装に在る・契約 ⑶ 3-3 の表には無い**＝§10 ⑸） |
| 読込中・読込失敗（503） | `ywk_runtime_unavailable` |
| 配布版の前段検査（4-3） | `ywk_unknown_field`・`ywk_out_of_range`・`ywk_type_error`・`ywk_invalid_enum`・`ywk_literal_top_level`・`ywk_voice_and_no_ref`・`ywk_voice_and_reference`・`ywk_unsupported_response_format`・`ywk_invalid_body`・`ywk_validation_error` |
| 暖機の口（**本体は叩かない**） | `ywk_warmup_running`（409）・`ywk_warmup_unknown_id`（404） |
| 事前計算の口（**本体は叩かない**） | `ywk_precompute_running`（409）・`ywk_precompute_unknown_id`（404）・`ywk_unknown_voice`（400／404） |
| 上記以外の 4xx／5xx | `ywk_upstream_error`／`ywk_server_error`（404 は `ywk_not_found`・405 は `ywk_method_not_allowed`・422 は `ywk_validation_error`） |

**本体の掟 3 つ**（`docs/contract.md` ⑶ 3-3）：

1. **`message` を生のまま利用者に出さない**。理由 1 行への畳み込みは **`code`** で行う
   ＝**本体の `IsMissingVoiceFailure` の文言依存を `code` に移せる**（⑼ D-5）。
   ※ **`ywk_missing_voice` が出るのはランチャが `IRODORI_DEFAULT_VOICE` を空にしたときだけ**
   （既定では省略しても 200）＝**この code を待つ分岐は常時は走らない**。
2. **422 の本文をログに流さない**（1 発 5 KB）。
3. **エラー body に絶対パスは 1 件も出ない**（配布版が `<path>` に畳んでいる）。
   ⇒ **本体は body をそのままログに載せてよい**（畳む処理を本体側に重ねる必要はない）。

### 4-5 動きの性質（本体の期限・並列設計に効く・`docs/contract.md` ⑶ 3-4）

- **同時要求は Lock で直列**（`max_concurrent_synthesis` 既定 1）＝**1 プロセス 1 合成**。
  配信中に配布版の UI で試し撃ちすると読み上げが待たされる（逆も同じ）。
- **キャンセルは効かない**＝中止の粒度は**チャンク**が上限＝「止める」は次のチャンクを投げないこと。
- **参照ありは毎要求・毎チャンク再符号化**。
- **`seed` を明示しないと `X-Irodori-Seed` は 1 本目のチャンクの値しか返らない**
  ＝先読みキャッシュを効かせるなら `irodori.chunking_enabled:false` か `irodori.seed` の明示が前提。
- **参照を当てると予測尺が変わる**（94.3→82.4 フレームの実測）＝同じ本文でも出力長が変わる。
- **応答ヘッダは上流のまま届く**（wrapper は上流の Response をそのまま返す）＝
  `X-Irodori-Seed`・`X-Irodori-Total-To-Decode`・`X-Irodori-Messages`。

### 4-6 SSE＝**受けるが本体は使わない**（`decisions.md` 47・`docs/contract.md` ⑶ 3-5）

`stream_format:"sse"` だけ受ける（他の値は 400）。応答は 200・`text/event-stream`。
枠は 3 種（`audio_chunk`／`done`／`error`）。**`error` 枠は 200 の中に乗る**ので
**HTTP の状態番号では失敗を判別できない**（判別は `data.error.code`。**この枠の code だけは
`ywk_` 接頭辞にならない**）。**取消は効かない**。
⇒ **本体は 1 発で wav を受け取る 4-1 の経路だけを使う。**

---

## §5 暖機と事前計算（**ランチャの口・本体は叩かない**）

**この 2 つは配布版のランチャ（便 D）が叩く口である。本体は⑶の 1 本道だけを使う。**
ただし**状態は `/ywk/status` から読める**ので、本体が「いま暖機中だから遅い」を説明したいときに使える。

### 5-1 なぜ在るか（1 行）

Radeon（gfx1151）では**出力の潜在フレーム数と参照ボイスの長さが「形」**で、**形ごとに MIOpen の探索が走る**。
罰は**未見の参照ボイスに当たるたび**に跳ねる（VOICEVOX/COEIROINK 参照で 0.6〜1.0 s → 最初の
VOICEROID2 参照で **14.8 s**＝`decisions.md` 40）。**CUDA では同じ罰は無い**
（`decisions.md` 75 ⑸「**CUDA では未見の参照形状の罰は無い**」＝11 プリセットの spread 1.12／1.30）。

### 5-2 暖機（`POST /ywk/warmup`・`DELETE /ywk/warmup/{id}`・`/ywk/status.warmup`）

| 口 | body | 応答 |
|---|---|---|
| `POST /ywk/warmup` | `{"stages":[4,8,12],"voices":["デフォルト","琴葉茜"],"text":"暖機です。"}`（3 欄とも任意） | **202** `{"id","shots_total","state":"running"}` ／ 走行中は **409** `ywk_warmup_running` |
| `DELETE /ywk/warmup/{id}` | — | **200** `{"id","state","cancel_requested"}`／別の id は **404** `ywk_warmup_unknown_id` |

`/ywk/status.warmup` の逐語（実装の `warmup_snapshot()` で確認・9 鍵）：

```json
{"state":"idle|running|done|failed|cancelled","id":"…|null","shots_done":0,"shots_total":0,
 "elapsed_s":0.0,"last_shot":{"kind":"no_ref|voice","key":"4s|琴葉茜","seconds":4.0,"ms":2518.4},
 "error":null,"shots":0}
```

- **`shots` は `shots_done` の別名**（便 A が名乗った欄をそのまま残してある）。
- **本物の要求が必ず勝つ**＝暖機は**次の射の前に**「走行中の本物」が 0 になるまで待つ（最大 30 s）。
  **本体が待たされるのは走行中の暖機 1 射分**（既定段では最大 ≈12 s＝`decisions.md` 60）。
- **`cancelled` は取消で終わったとき。走行中の `DELETE` の応答だけは `state:"cancelling"`**
  （§10 ⑹）。

### 5-3 事前計算（`POST /ywk/voices/precompute` ほか・**Radeon 版だけ既定 ON**）

| 口 | body | 応答 |
|---|---|---|
| `POST /ywk/voices/precompute` | `{"ids":["琴葉茜","月読アイ"]}` **または** `{"all":true}`（＋任意 `force`） | **202** `{"id","total","state":"running"}` ／ 走行中は **409** `ywk_precompute_running` |
| `DELETE /ywk/voices/precompute/{id}` | — | **200** `{"id","state","cancel_requested"}`／別の id は **404** `ywk_precompute_unknown_id` |
| `DELETE /ywk/voices/{話者 id}/latent` | — | **200** `{"id","state":"reverted\|absent","alias":"…","removed":[…]}`／知らない話者は **404**／走行中は **409** |

`/ywk/status.precompute` の逐語（実装の `precompute_snapshot()` で確認・11 鍵）：

```json
{"state":"idle|running|done|failed|cancelled","id":"…|null","done":0,"total":0,
 "built":0,"reused":0,"skipped":0,"failed":0,"elapsed_s":0.0,
 "last":{"id":"琴葉茜","state":"built|reused|skipped|failed","reason":null,"frames":750,"ms":612.4},
 "error":null}
```

- **既定 ON は `rocm-*` だけ**（env `YWK_PRECOMPUTE_ON_START`・未設定なら変種で決まる）。
  **口そのものは変種に依らず在る**＝CUDA 版でも `precompute` 欄は必ず在り、既定で `idle` のまま。
- **本物の要求が来たら次の 1 件の前で待つ**（最大 30 s）＝本体が待たされるのは**最大 1 件ぶん**
  （1 件 0.37〜0.90 s）。

---

## §6 本体の設定 UI に出す最小の欄

**原則**＝**`/params` を読んで組む**（固定表を持たない＝§2-1）。そのうえで**画面に出すのはこれだけ**：

> ※ `build/out` の組み上げ済み樹とインストーラ（2026-09-06 22:10 の組み＝cuda 85,005,985 B sha256 `ffd35375…`・radeon 85,020,453 B sha256 `6a1d95f4…`）と、
> 設計席の機体に入れ直した Radeon 版（18088）は、この label「感情表現の強さ」と裁定 100 の `note` を返す（`decisions.md` 101）。それより古い組みは旧 label「CFG 強度（本文）」を返す。

| 欄 | 出所 | 備考 |
|---|---|---|
| **エンジンの URL** | 本体の設定 | 既定 **`http://127.0.0.1:18088`**。**`localhost` は選ばせない**（+1.2〜2.0 秒）。**8088 の既存アダプタとは別項目**（D-9） |
| **話者** | `GET /ywk/voices`（表示名が要るとき）／`GET /v1/audio/voices` | **Choice**。`display_name` を出し `id` を送る。**先頭は必ず「デフォルト」＝参照なし** |
| **speed** | `/params.request.speed` | Number・0.25〜4.0・step 0.05・既定 1.0 |
| **num_steps** | `/params.irodori[key=num_steps]` | Number・1〜120・既定 40・**プリセット 10／40**（`presets` 欄がそのまま来る） |
| **seed** | `/params.irodori[key=seed]` | Text・**空欄＝毎回の乱数**（`""` を送っても 400 にならない） |
| **caption（演技指示）** | `/params.irodori[key=caption]` | Text・**空欄＝未指定** |
| **感情表現の強さ（`cfg_scale_text`）** | `/params.irodori[key=cfg_scale_text]` | Number・0.0〜10.0・step 0.1・**既定は `/params` の `default` を読む**（現在 3.0・`IRODORI_DEFAULT_CFG_SCALE_TEXT` で動く＝焼かない）。**本体の「感情表現の強さ」＝この欄**（`decisions.md` 99）＝表示名は `/params` の `label` をそのまま `DisplayName` に・この欄を「CFG 強度」の名では出さない。**`IsEmotion` を立てる＝本体の「感情」見出しの帯に出す**（`decisions.md` 100）。**10 番目は 9 番目（8088）とあくまで別物として扱う**（`decisions.md` 101＝別 builder・別テスト）＝9 番目の `IrodoriCapabilityBuilderTests.cs:222`（全欄 `IsEmotion=false`）には触らない。**表示名だけが同じで目盛りは互換でない**（Aivis 0〜2 中立 1.0／Irodori 0〜10 中立 3.0・本体にマスター感情の写像は無い）。**0 は本文 CFG を切る**（表情が薄くなるのではなく本文への追従が弱る＝上流 `rf.py:264`・実用下限 1.0 付近）。**本体の裁定 6（露出 5 本・`cfg_scale_*` 非露出＝`Engines/Irodori/IrodoriConstants.cs:343`）はこの欄・`num_steps`、および次の「余力があれば」行の `cfg_scale_caption`・`cfg_scale_speaker`・`t_schedule_mode` について改訂が要る**（`sway_coeff` は 343 行の列挙に無い・10 番目のエンジンの話・9 番目の 8088 アダプタは変えない）。この名の規則は本体の面だけ＝ランチャの「試す」は生の欄名 `cfg_scale_text` のまま |
| （余力があれば）`cfg_scale_caption`・`cfg_scale_speaker`・`sway_coeff`・`t_schedule_mode` | `/params.irodori` | `exposed_to_ywk: true` の残り 4 欄 |

**出さない欄**（理由つき）：

- **`empty_cache_interval`＝そもそも要求の欄ではない**。これは env
  `IRODORI_EMPTY_CACHE_INTERVAL`（配布版は **`0`** を焼く＝`decisions.md` 7）で、
  **`/params` の 44 欄にも `request` にも 1 件も出て来ない**。
  **ランチャの設定項目（初期値 0）**として配布版側が持つ（`decisions.md` 69 ⑵）。
- **`device`／GPU／精度**＝**本体は要求 JSON に載せない**。device は Server プロセスの env
  （`IRODORI_MODEL_DEVICE`／`IRODORI_CODEC_DEVICE`）だけで決まる＝**1 プロセス 1 デバイス**で、
  **ランチャで選んだ GPU が暗黙に使われる**（`decisions.md` 15・⑼ D-7）。
  `/ywk/status` は**表示と記録のため**の口であって、**切り替えの口ではない**。
  ⇒ **本体の D-7 は差分ゼロ**（元栓の env 注入も不要）。
- **`response_format`**＝`wav` の 1 択なので選ばせない。
- **`ref_wav`・`ref_latent`・`ref_embed`・`lora_adapter` のパス欄**＝`exposed_to_ywk: false`。
  **話者の所有者は配布版**（§3-6）＝本体は wav のパスを持たない。
- **`duration_scale`・`seconds`**＝§4-2 の理由で送らない（UI にも出さない）。
- **暖機・事前計算の口**＝ランチャの口。本体の UI には出さない（状態の表示に留める）。

**表示に使える 3 つ**（`/ywk/status` から・読むだけ）：`device.actual`＋`device.name`（いま何で
動いているか）・`variant`（`cuda`／`cpu`／`rocm-gfx1151`）・`warmup.state`／`precompute.state`
（「いま暖機中なので遅い」の説明）。

---

## §7 変えてはいけない前提

1. **`schema` の上げ方**（`docs/contract.md` ⑻）＝
   **欄を消す・意味を変えるときだけ上げる。欄を足すのは上げない**（本体は知らない欄を無視する）。
   ⇒ **本体の record／DTO は「知らない欄を捨てる」で書く**（`schema` が同じまま欄が増える）。
   **`schema` は `/params` から読む**（§10 ⑴）。現在 **`schema: 1`**。
2. **`api_key` は無い**（`decisions.md` 2・31・`docs/acceptance.md` 「安全」行）。
   自動生成もしない。**本体は `Authorization` を付けない**（付けても無視される）。
3. **loopback 限定**＝bind は `127.0.0.1` 固定。**別機から叩く路は無い。**
   `localhost` の名前指定は使わない。
4. **8088 と 18088 の同居**＝配布版は**既存の Irodori-TTS-Server 直結経路（8088）とは別個体**で、
   **同時に動かしてよい**（`decisions.md` 2・`docs/acceptance.md` の追加行＝
   「配布版を起こした状態で 8088 の既存個体を起こし、両方の `/health` が 200 になること」）。
   **本体の 9 番目のアダプタ（`irodori`・8088 固定）はそのまま**で、配布版は**10 番目のエンジン**として別に建つ。
5. **起こすのも後始末もランチャ**（`decisions.md` 6→**124＝常駐はやめた**・ランチャの窓を閉じるとサーバも落ちて VRAM が返る）＝**本体の一括起動は引数なしで `IrodoriTtsYwk.Launcher.exe` を起こしてよい**（`decisions.md` 103・従来型 TTS と同じ扱い・`LaunchKind.ExePath`・終了時は放置＝起こした窓はそのまま残る）。本体はサーバのプロセスを持たない。
   見つからなければ理由つき失敗で、**他エンジンの読み上げは無傷**（本体 §0-3 の掟 1・掟 2）。
6. **上流の書き込み 3 口は外してある**＝本体が叩けば 404／405（§3-6）。
7. **上流の機能は殺さない**（`decisions.md` 47）＝SSE の枠はそのまま通す。**本体は使わない**。
8. **`response_format` は `wav` だけ**（ffmpeg を同梱しない＝第三者バイナリ不同梱＝`decisions.md` 8）。
9. **変更時の告知**＝正典を直し、**契約テストの題目を書き換え**、本体側（便 F）に知らせる
   （`docs/contract.md` ⑻）。

---

## §8 実測値（逐語・出典つき）

### 8-1 RTX 3090（`decisions.md` 75 ⑷・ドライバ 591.86・bf16・warm）

| 条件 | cu130 | cu126 |
|---|---|---|
| cold 1 発目 | **1.363 s** | **1.258 s** |
| 短文 40 steps・参照なし（`rtf_http`） | **0.277** | **0.262** |
| 長文 40 steps（`rtf_http`） | **0.101** | **0.096** |
| 短文 10 steps（`rtf_http`） | **0.107** | **0.104** |
| 参照 30 s あり（`rtf_http`） | **0.309** | **0.313** |

- `device.actual=cuda:0`・RTX 3090・uuid `19adfe89-…`・`pci_bus_id` 1・bf16。
- **`rtf_http` は kit の `rtf_synth` より HTTP 分だけ大きい**＝
  「予算 0.25／0.10 を 10 % 台で超えるのは測定の定義差＝未達とは書かない」（`decisions.md` 75 ⑷）。
- **VRAM はプロセス全体の占有で peak 7.8 GB**（torch allocator の値ではない・idle 752 MiB に完全復帰）。
- **CUDA では未見の参照形状の罰は無い**（11 プリセットの spread 1.12／1.30＝同 ⑸）。
- `docs/acceptance.md` の原文の根拠値＝「3090・cu130・bf16・40 steps・warm・参照なし＝
  短文 RTF ≤ 0.25／長文 ≤ 0.10」に対し**実測 0.215／0.084**、
  「起動後 1 発目 ≤ 2.0 s」に対し **cold 1.032 s**。

### 8-2 RTX 3090・古いドライバ 537.58（`decisions.md` 80・U-14）

- **cu126 は通る**＝`is_available=True`・`device_count=1`・4 検査すべて PASS・
  **`/health` 200 まで 19.2 s**・cold 1 発目 **1.776 s**・warm **0.996 s**（**RTF_http 0.265**）・
  VRAM ピーク 5,196 MiB。
- **同じドライバで cu130 は `cudaGetDeviceCount() returned cudaErrorNotSupported`
  ＝`is_available=False`・`device_count=0`（import は通り、落ちない）。**
- **起動前検査の閾値**＝**cu130 ≥ 580.xx／cu126 ≥ 528.33**（528.33〜537.57 は「未実測の帯」）。
  **これは配布版のランチャが持つ門**であって、本体は関与しない。

### 8-3 Radeon gfx1151（bf16・`cuda:0`・40 steps・`decisions.md` 59・76）

| 何 | 空 MIOpen db | 温 db |
|---|---|---|
| 起動（spawn→loaded） | **29.0 s** | **26.1 s** |
| 暖機 6 射 | **51.8 s** | **12.6 s** |
| 合計（機体初回／2 回目以降） | **80.8 s** | **38.7 s** |

- 暖機後の同一プロセス＝**暖機済み話者 1 発目 1.49 s・参照なし 1.05 s・未暖機話者 1 発目 2.5 s**
  （空 db では 9.9 s）。
- **参照潜在を焼いた後**（`decisions.md` 76）＝`prepare_reference` は **0.95〜1.44 s → 1.0〜4.3 ms**
  （プロセス初見 7.7〜16.7 ms）・**11 プリセットの 2 巡目 RTF は 0.222〜0.398＝11/11 が 0.5 未満**。
  **プロセスで最初の 1 発だけは潜在でも RTF 0.726〜0.848**（暖機で償う枠）。
- **未解決として残っている 1 つ**＝連続運転で `sample_rf` が 618→1,400 ms（2.3 倍）に伸びる件は
  **22 射・約 35 s では再現せず＝解消の証明ではない**（`docs/acceptance.md` §3 の 9）。

### 8-4 導入〜最初の音（本物の E2E・`decisions.md` 93 ⑶・`docs/design/ben-e-installer.md` §15-3・§15-5-2）

**Radeon 版**（この機体）＝setup **2.71 s** → ウィザード → 取得 **16.45 s**（新規 8 檔 1.37 GB・線速 83 MB/s）
→ 展開 **27.25 s**（26,523 檔 4.25 GB）→ モデル **3.81 s**（既存・再取得 0）→ 起動 **39.26 s**（ready・bf16・gfx1151）
＝**導入〜完了 97.84 s**。

入れ直して製品の路で 2 射（`docs/design/ben-e-installer.md` §15-5-2 の逐語）：

```
[shot] state = 待機  after 31.64 s
[shot 1] result  = 所要 6.71 秒／出力 3.20 秒／RTF 2.10／seed 6283283498982061075
[shot 2] result  = 所要 267 ms／出力 3.20 秒／RTF 0.08／seed 2756786406996769807
```

⇒ **本体が憶えること**＝**プロセスの 1 射目は帯の外**（導入直後は `__pycache__` も冷えている）。
**2 射目以降が実力**。本体の期限をプロセス 1 射目に合わせて短く切らないこと。

**CPU（cu126 の wheel・NVIDIA 無し機・`docs/design/ben-e-installer.md` §15-4）**＝
`runtime loaded in` **10.36 s**・`/health` 200＋`runtime.loaded=true` まで **14.16 s**・
`POST /v1/audio/speech` **200・303,404 B の RIFF/WAVE**（fp32・10 steps・3.16 s 出力）・
HTTP 所要 **4.56 s** ⇒ **RTF_http ≈ 1.44**。
`docs/acceptance.md` の CPU 行の原文＝「CPU（fp32・OMP=8）＝RTF ≤ 4.0（速さは要求外）」・実測 3.27。
⇒ **CPU 変種は「動くが遅い」＝配信用途では非推奨**（`decisions.md` 13）。

### 8-5 起動の条件（`docs/acceptance.md`）

- 「UI 起動→`/health` 200＋`runtime.loaded=true` まで **≤ 120 s**（3090・SSD なら ≤ 60 s）・
  **ready 待ち ≥ 120 s**」＝**本体の待ちも 120 s**。
  （**改訂・裁定 105**＝この行は 2 つに割れた。**`/health` 200 まで ≤ 10 s**（ポート先行・exe から実測 8.9 s＝`decisions.md` 106）と
  **`runtime.loaded=true` まで ≤ 120 s**。`docs/acceptance.md` の 起動 行。）
- 「範囲外 GPU・精度不整合・checkpoint 不在は **≤ 15 s** で理由 1 行に落ちる」
  （改訂＝範囲外 GPU と device 綴り違いは **0 s** で弾く＝上流に渡す前に wrapper が exit 2）。
- 「`GET /params` **≤ 100 ms**・モデル未読込でも 200・本体が露出する全欄を 100 % 含む」。

---

## §9 契約テストの写し方（`tests/contract/**` を本体側でどう真似るか）

### 9-1 配布版側の型（`tests/conftest.py`）

**モデルは 1 度も載せない。** `FakeRuntimeManager` / `FakeRuntime` が上流の runtime を差し替え、
**HTTP の形だけ**を撃つ（`tests/conftest.py` の冒頭 3 行が名乗っているとおり）。逐語＝

```python
class FakeRuntime:
    """Records the ``SamplingRequest`` it is handed and returns silence."""
    def __init__(self) -> None:
        self.requests: list[Any] = []
        self.model_device = torch.device("cpu")
        self.codec_device = torch.device("cpu")
        self.default_text_max_len = 256
        self.default_caption_max_len = 256
        self.default_max_ref_seconds = 120.0

    def synthesize(self, req, *, log_fn=None) -> SamplingResult:
        self.requests.append(req)
        audio = torch.zeros(1, 100)
        return SamplingResult(audio=audio, audios=[audio], sample_rate=16000,
                              stage_timings=[], total_to_decode=0.1,
                              used_seed=1 if req.seed is None else int(req.seed), messages=[])
```

```python
class FakeRuntimeManager:
    def __init__(self, runtime=None):
        self.runtime = runtime
        self.checkpoint_path = None
        self.is_loaded = runtime is not None
        self.is_loading = False
```

**この型が担保している 5 つ**（本体側でも同じ 5 つを担保する）：

1. **送った body が記録される**（`self.requests.append(req)`）＝**要求の欄の突合が試験の主眼**。
2. **音の中身は試験しない**（`torch.zeros`）＝**音質は契約ではない**。
3. **`used_seed = 1 if req.seed is None else int(req.seed)`**＝**seed の往復**を釘で留めている。
4. **`is_loaded` / `is_loading` の 2 旗を偽物が持つ**＝**ready 待ちの分岐**が本物のモデル無しで撃てる。
   **裁定 105 でここが 3 旗になった**＝読込の失敗は `get()` を投げさせて `runtime.error` で読む
   （`tests/contract/test_runtime_loader.py` の `RecordingManager` は `get()` を**堰き止められる**
   ので、「載っている最中」＝`{loaded:false, loading:true}` の窓ごと試験できる）。
5. **`BASELINE` を試験ごとに貼り直す**＝1 つの試験の設定が次に漏れない
   （`IRODORI_PRELOAD=false`・`YWK_DATA_DIR` は tempdir＝**3 GB の checkpoint を落とさない**）。
   ※ この `false` は**もう試験専用の細工ではない**＝裁定 105 で**配布版が焼く値そのもの**に
   なった（`test_env_and_devices.py`）。

### 9-2 本体側（C#）は「runtime」ではなく「HTTP」を偽る

本体は Python の runtime を持てない。**真似るのは型であって檔ではない**＝
本体側の対応物は **wrapper の応答を返す偽の HTTP** である。**すでに 2 つの実物が配布版側に在る**：

| 写す先 | 何 |
|---|---|
| `launcher/IrodoriTtsYwk.Launcher.Tests/WrapperClientTests.cs` | **`HttpMessageHandler` に偽物を差し、応答の逐語（`docs/contract.md`）を読ませる**。`StatusBody` などの const 文字列が**そのまま fixture**になる |
| `launcher/IrodoriTtsYwk.Launcher.Tests/TestHttpServer.cs` | `HttpListener` の本物のローカル HTTP（**Windows では管理者権限なしで開ける**）。Range の再開・接続断・取り直しを実 HTTP で釘付ける |

**record（DTO）はゼロから書かない**＝配布版のランチャに `[JsonPropertyName]` つきの完成品が在る：

- `launcher/IrodoriTtsYwk.Launcher/Contracts/WrapperResponses.cs`＝
  `ErrorBody`・`ErrorEnvelope`・`HealthResponse`・`StatusUpstream`・`StatusRuntime`・`StatusDevice`・
  `StatusTorch`・`StatusVoices`・`WarmupShot`・`WarmupStatus`・`PrecomputeLast`・`PrecomputeStatus`・
  `MemoryStatus`・`StatusResponse`・`ParamsCheckpoint`・`ParamDescriptor`・`ParamsResponse`。
- `launcher/IrodoriTtsYwk.Launcher/Contracts/IWrapperClient.cs`＝
  `SpeechRequest`・`SpeechResult`・`WarmupRequest`・`WarmupStartResult`・`PrecomputeRequest` ほか。

**本体が写すのは `Error*`・`Health*`・`Status*`・`Params*`・`Speech*` の範囲でよい**
（`Warmup*`・`Precompute*`・`Memory*` はランチャの持ち物＝§5・§6）。

**流儀 2 つ**（`decisions.md` 87 ⑶）＝⒜ **欄名は `[JsonPropertyName]` で書く**（`SnakeCaseLower`
頼みをやめる＝`ref_wav`／`gcn_arch` のような綴りを足した日に黙って null になる）
⒝ **欄はすべて null 許容**（wrapper が欄を足しても `schema` は上がらない＝落ちてはいけない）。

### 9-3 本体側で最低限置く釘（配布版の契約テストの題目に対応させる）

| 本体の試験 | 対応する配布版の題目（`docs/contract.md`） |
|---|---|
| 接続先は `127.0.0.1:18088`・`localhost` 名を使わない | `接続先は127_0_0_1の18088でありlocalhost名は使わない` |
| `Authorization` を付けない | `Authorizationヘッダを付けない` |
| `/ywk/status` が 404 の個体は配布版として扱わない | `ywkstatusが404の個体は配布版として扱わない` |
| ready は `runtime.loaded` で判定・待ちは 120 秒 | `readyはruntimeloadedで判定し待ちは120秒` |
| 未知話者の 400 を **`code == "ywk_unknown_voice"`** で判定する（**文言では判定しない**＝D-5） | `上流由来の400にもcodeが載る` |
| `voice` 省略でも 200（`ywk_missing_voice` の分岐は常時は走らない） | `voicesjsonが無くてもデフォルトで合成200` |
| `voice:"none"` を送っても 200（正規化される） | `noneの別名は正規化されて200` |
| `voice` と `no_ref`／参照 5 欄を同時に送らない | `voiceとno_refの同時指定は400`／`voiceと参照5欄の同時指定は400` |
| Literal 3 欄をネストに入れる | `Literal3欄はトップレベルにあれば400` |
| `caption:""`・`seed:""` の往復（400 にならない） | `caption と seed の空文字既定が往復する` |
| `/params` の `exposed_to_ywk` を**配列から**読む（数を焼かない） | `exposedtoywkの欄にdefaultnullが0件`／`paramsは44欄を全部載せる` |
| 接続断（プロセス消失）を timeout を待たずに理由 1 行にする | （配布版側はランチャの死活＝`decisions.md` 88 ⑶） |

---

## §10 突合の記録（正典 対 実装・**本檔の JSON はすべてこれで直した**）

`docs/contract.md` の逐語と `server/ywk_server.py`／`server/ywk_params.py` の実装を突き合わせた。
**本檔の JSON は実装側の実物に合わせてある。**下の 6 件（うち ⑴⑵⑸ は `decisions.md` 99・104 で解消＝卓への票は 3 件）は**正典の内部で食い違っている／
正典の抜粋の外に在る**もので、**本檔では正典側の逐語（⑸ 5-1・⑹ の JSON）を正とし、
散文の側を採らなかった**。卓（設計席）への票として置く。

1. **`/ywk/status` に `schema` 欄は無い。**
   `docs/contract.md` ⑻ は「**`schema` 番号＝`/params` と `/ywk/status` の応答に載る**」と書くが、
   同 ⑹ の逐語（`{"engine":…,"version":…,"upstream":…}`）に `schema` は無く、
   実装の `ywk_status()` も返していない（返す 14 鍵は §1-2 のとおり）。
   ⇒ **本檔は「`schema` は `/params` から読む」と書いた。**
   卓への票＝⑻ の 1 行を「`/params` の応答に載る」に直すか、`/ywk/status` に足すか。**直った**（`decisions.md` 104＝⑻ を「`/params` の応答に載る」に）。
2. **`exposed_to_ywk` は「9 欄＋voice・speed」ではなく「8 欄＋voice・speed＝10」。**
   `docs/contract.md` ⑸ 5-2 の散文と `docs/acceptance.md` API 行の散文が「9 欄」と書くが、
   同 ⑸ 5-1 の逐語 `rules.exposed_to_ywk` は **10 件**で、実装の
   `EXPOSED_TO_YWK`（8 件）＋`EXPOSED_REQUEST_KEYS`（2 件）と**完全に一致**する。
   ⇒ **本檔は逐語（10 件の配列）を採った。** 卓への票＝散文の「9 欄」を「8 欄」に直す。**直った**（`decisions.md` 99＝`contract.md` ⑸ 5-2・`acceptance.md` API 行・`ywk_params.py` の docstring）。
3. **`/params.prefetch.planned` の値は `"/ywk/prefetch"`**（実装）。
   `docs/contract.md` ⑸ 5-1 の逐語は `"/ywk/prefetch（§6）"` と書き、直前の但し書きで
   「末尾の `planned` の `（§6）` は設計書の節番号を指す＝本書では⑺である」と断っている
   ＝**節番号は注記であって値の一部ではない**と読んだ。
   ⇒ **本檔は実装の値をそのまま書いた。**
4. **抜粋の外に在る欄が 4 つ**（矛盾ではない＝⑸ 5-1 は「**抜粋である**」と明記している）。
   本檔は**実装に在る側**を書いた＝
   ⒜ `/params` の最上位に **`version`**（`docs/contract.md` ⑸ 5-1 の抜粋に無い）
   ⒝ `/params.request` は **6 欄**（`model`・`stream_format` が抜粋に無い）
   ⒞ `/params.rules` に **`response_format`**（`["wav"]`）と **`error_code_prefix`**（`"ywk_"`）
   ⒟ `/ywk/status.device` に **`codec_configured`**（⑹ の逐語にもランチャの `StatusDevice` にも無い）。
   **どれも「欄を足しただけ」＝`schema` は上がらない**（⑻）ので、本体は**知らない欄を無視する**読み方で
   そのまま通る。
5. **`ywk_invalid_voice_id` が実装に在る**（`_UPSTREAM_CODES` の 5 番目＝上流の
   `voice_id must contain only …` に被せる）が、`docs/contract.md` ⑶ 3-3 の表には無い。
   実際に出る路は上流の登録 API だけで、**配布版はその 3 口を外している**（⑷ 4-3）ので
   **本体には届かないはず**だが、**`code` の網羅表に載せておかないと `default` 分岐に落ちる**。
   ⇒ **本檔の表には載せた**（§4-4）。卓への票＝⑶ 3-3 の表に 1 行足す。**直った**（`decisions.md` 104）。
6. **走行中の `DELETE /ywk/warmup/{id}`／`DELETE /ywk/voices/precompute/{id}` の応答の
   `state` は `"cancelling"`**（実装）。`/ywk/status` の state の enum
   （`idle|running|done|failed|cancelled`）には `cancelling` は**無い**（`docs/contract.md` ⑺ の表は
   欄名 `{"id","state","cancel_requested"}` だけで値を挙げていない）。
   **走行中でないときは現在の state をそのまま返し `cancel_requested: false`。**
   ⇒ **本体は叩かない口なので影響なし。**本檔には注記として残した（§5-2）。

### 10-1 逐語が一致していることを確認した箇所（食い違い 0）

- `GET /ywk/voices` の 6 鍵（`object`・`data`・`default_voice`・`no_ref_voice`・`count`・`error`）
  ＝実装 `ywk_voices()` と一致。
- `GET /v1/audio/voices` の 1 件の 7 鍵（`id`・`object`・`display_name`・`preset`・`no_ref`・
  `latent`・`latent_stale`）＝実装 `_voice_list_with_reason()`／`_default_voice_entry()` と
  `docs/contract.md` ⑷ 4-1 の逐語が一致。**「デフォルト」の注入と先頭への並べ替えも実装に在る。**
- `/ywk/status.warmup` の 9 鍵・`/ywk/status.precompute` の 11 鍵＝実装の `_WARMUP_IDLE`／
  `_PRECOMPUTE_IDLE`＋`elapsed_s`（＋warmup は `shots`）と `docs/contract.md` ⑺ の逐語が一致。
- `POST /ywk/warmup` の 202 body `{"id","shots_total","state":"running"}`・
  `POST /ywk/voices/precompute` の 202 body `{"id","total","state":"running"}`＝実装と一致。
- `/ywk/status.memory` の 10 欄（＋失敗時のみ `error`）＝実装 `_MEMORY_EMPTY`＋`sampled_at` と
  `docs/contract.md` ⑹ 6-1・`decisions.md` 87 ⑴ が一致。
- エラー body の 4 欄（`message`・`type`・`param`・`code`）と `_STATUS_CODES`
  （404→`ywk_not_found`・405→`ywk_method_not_allowed`・422→`ywk_validation_error`・
  503→`ywk_runtime_unavailable`）＝`docs/contract.md` ⑶ 3-3 と一致。
- `rules` の 11 鍵（`priority`・`literal_must_be_nested`・`voice_and_no_ref_exclusive`・
  `voice_and_reference_exclusive`・`unknown_field`・`out_of_range`・`null_means_unset`・
  `null_normalized_by_wrapper`・`empty_string_means_unset`・`no_ref_voice_aliases`・
  `default_voice`）と `exposed_to_ywk` の 10 件＝`docs/contract.md` ⑸ 5-1 の逐語と実装が一致。
- `apply_env_defaults()` の焼き込み＝`IRODORI_HOST=127.0.0.1`・`IRODORI_PORT=18088`・
  **`IRODORI_PRELOAD=false`**（裁定 105 で `true` から変わった＝bind してから裏で載せる）・
  `IRODORI_EMPTY_CACHE_INTERVAL=0`・`IRODORI_ALLOW_NO_REF_VOICE=false`・
  `IRODORI_DEFAULT_VOICE=デフォルト`・`IRODORI_DEFAULT_NUM_STEPS=40`・
  `IRODORI_DEFAULT_RESPONSE_FORMAT=wav`・`IRODORI_HF_CHECKPOINT=Aratako/Irodori-TTS-v4.1-Small`
  ＝`decisions.md` 2・7・45・105 と一致。

---

## §11 本体側の差分 D-1〜D-9 の現在地（`docs/contract.md` ⑼ を配布版の実装で更新）

| # | 現行 | 配布版が建った後 |
|---|---|---|
| D-1 | 擬似キャラ 1 件固定 | **話者が N 件になる**（`GET /v1/audio/voices`）。`CharacterCapability` の複数件は他 8 エンジンの器がある |
| D-2 | 発見は `/health` 1 段 | **`/ywk/status` と `/params` が加わって 4 段**（§1）。`EngineCapability` は非永続＝保存の形は増えない |
| D-3 | 話者登録は `POST /v1/audio/voices`＋`ywk-<sha12>` | **路ごと無くなる**＝配布版は書き込み 3 口を外した（404／405）。**話者の所有者は配布版** |
| D-4 | 参照なし＝`voice:"none"` | **配布版では `voice:"none"` も 200**（別名 5 つを「デフォルト」に正規化）＝**本体は改修前でも合成できる**。新アダプタは `"デフォルト"` か欄ごと省略が正 |
| D-5 | `IsMissingVoiceFailure` は文言依存 | **`error.code` で判定できる**（未知話者＝`ywk_unknown_voice`）。**`ywk_missing_voice` はランチャが `IRODORI_DEFAULT_VOICE` を空にしたときだけ**＝この分岐は常時は走らない |
| D-6 | 話者メタ（wav パス）は本体プロファイル側 | **持ち主が配布版へ移る**＝`VoiceParamId` の Text 欄が Choice の話者選択に変わる |
| D-7 | GPU 欄なし・精度 2 env のみ | **差分ゼロ**（配布版が GPU を管掌・元栓の env 注入も不要） |
| D-8 | `/health` 200 だけで発見・`voices.json` 破損を検知できない | **消える**＝配布版の一覧は台帳が壊れても 500 を返さず「1 件（デフォルト）＋理由」を返す |
| D-9 | 接続先は `IrodoriConstants.cs:35` の固定値（8088）・`App.xaml.cs:131` は baseUrl を渡していない | **必ず要る改修**＝18088 に確定（`decisions.md` 2・32） |

---

## §12 出典の索引（この檔が写した元）

- **正典**＝`decisions.md`（配布版側の確定事項・特に 2・6・7・11・13・15・16・17・31・32・45・47・
  59・60・62・65・75・76・78・80・83・87・88・90・93・94）。
- **契約**＝`docs/contract.md` ⑴〜⑼（本檔の §1〜§7・§11 はここの要約）。
- **受け入れ条件**＝`docs/acceptance.md`（§8 の条件値・§3 の「満たしていない・未確認」）。
- **実装**＝`server/ywk_server.py`・`server/ywk_params.py`（本檔の JSON の突合先）。
- **契約テスト**＝`tests/conftest.py`・`tests/contract/**`（§9）。
- **本体が写せる C# の型**＝`launcher/IrodoriTtsYwk.Launcher/Contracts/WrapperResponses.cs`・
  `IWrapperClient.cs`／**試験の型**＝`launcher/IrodoriTtsYwk.Launcher.Tests/WrapperClientTests.cs`・
  `TestHttpServer.cs`。
- **設計書**＝`docs/design/ben-d-launcher.md`（§15＝wrapper 席の記帳・§20-5＝便 D（3）への票）・
  `docs/design/ben-e-installer.md`（§15＝E2E の実測・§16＝是正）。
- **調査（上流の実射）**＝`research/lab/notes/14-server-contract.md`・`37-handoff-contract.md`・
  `38-handoff-decisions.md`・`40-rocm-warmup.md`・
  `research/report/irodori-native-handoff-2026-09-04.md`（**読むだけ**）。
