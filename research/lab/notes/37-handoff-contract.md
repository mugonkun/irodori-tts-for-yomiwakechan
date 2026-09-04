# 37 — 配布版 irodori-TTS と読み分けちゃん2 本体の「契約面」（要求④⑥後半⑦⑧）

> 席＝第 10 次サブ席（opus）／担当＝本体との契約面。実施 2026-09-04。
> **設計は決めない**＝事実と選択肢と材料だけ。書いたのは `lab/` 配下のみ（本ノートと `lab/out/37_contract.json`・`lab/bin/37_contract.py`・`lab/tmp37/`）。
> 実射＝`lab/.venv`（CPU・torch 2.10.0+cpu）の **FastAPI TestClient ＋ FakeRuntime**。**8088／7861 は起動も停止も HTTP 送信もしていない**。8093 も使わなかった（TestClient で足りたため）。GPU 未使用。外部通信なし（`HF_HUB_OFFLINE=1`）。
> 対象コミット＝`upstream/Irodori-TTS-Server` = `841fb7c`／`upstream/Irodori-TTS` = `8224daf`。無改変の証明＝§9。
> 逐語の正本＝`C:/Users/mugonkun/source/repos/irodori-native-research/lab/out/37_contract.json`（86.5 KB）。台本＝`lab/bin/37_contract.py`。
> 行番号は `grep -n` の実測。参照ノート＝`06`（Server API）・`14`（契約 T-1〜T-12）・`05`（推論引数）・`29`〜`31`・`33`・`35`。

## 要点（10 行）

1. **④の取得手段は「ある」＝`/openapi.json` は認証なしで 200・10,850 B・`IrodoriOptions` の 44 欄が全部載る**。だが**型と 3 つの enum しか載らない＝既定値 0 欄・範囲 0 欄・説明 0 欄**（実射 §1-2）。本体の `ParamDescriptor`（Min/Max/Step/DefaultValue 必須）は**これだけでは組めない**。
2. **`/docs`・`/redoc` は jsdelivr の CDN を引く**（逐語 §1-1）＝**オフライン機では白紙**。配布版の UI に上流 `/docs` をそのまま出すのは不可。
3. **既定値の真実は 3 層に分かれる**＝`SamplingRequest` の dataclass 既定（`inference_runtime.py:202-246`）／`Settings` の `IRODORI_DEFAULT_*` 22 欄（`config.py:47-72`）／要求本文。**サーバ運用者が env を変えると既定が動く**＝「固定表を本体に持つ」は**この 22 欄で嘘になりうる**（§1-4）。
4. **範囲（Min/Max/Step）は上流の HTTP のどこにも無い**。機械可読の唯一の出所は **gradio のスライダ 8 本**（`gradio_app.py:481-531`・`gradio_app_voicedesign.py:538-544`）。`docs/parameters.md`（527 行・英文）は既定と散文だけ（§1-5）。
5. **T-3／T-4 は再現・かつ非対称を追加確認**＝綴り違いは `irodori` ネストでも**200 で沈黙**（`extra="allow"`）。`t_schedule_mode:"zigzag"` は**トップレベルなら 200 で素通り／ネストなら 422**。`num_steps:-5` は**両方 200**（範囲検査ゼロ）（§2-1）。
6. **⑦は「ファイル名 stem＝話者名」で成立するが、HTTP の登録口は日本語を拒む**＝走査は無検査（`ずんだもん.wav`・`四国 めたん.wav`・`emoji😊.wav` すべて一覧に出て 200 で合成）だが、**POST/PUT/GET/DELETE `/v1/audio/voices` は `^[A-Za-z0-9_-]+$`**（`voices.py:24,151-155`）＝**日本語話者は「ファイルを直接置く」か「`voices.json` の別名」でしか作れない**（§3-1・§3-2）。
7. **`VoiceSpec` はパス 5 欄＋`no_ref` だけ**（`voices.py:28-35`・実射で欄名を列挙）。`voices.json` に `caption`・`num_steps`・`display_name` を書いても**黙って捨てられる**（200・値は既定のまま＝§3-4）＝**話者メタ（名付け・caption・既定パラメータ）はアプリ側が持つしかない**（`31` §1-1 を実射で確定）。
8. **⑧「デフォルト」は Server に無い語＝`voice:"デフォルト"` は 404 でなく 400**（逐語＋**voices ディレクトリの絶対パスを漏らす**）。通るのは `none/no_ref/no-ref/null/text-only`（大小無視・strip 済み）と `irodori.no_ref=true` の 2 系（§4-1）。
9. **`no_ref=true` は voice を丸ごと無効化する**（`app.py:437-454`＝voice 解決に**入らない**）＝`voice:"zunda"＋no_ref:true` は**参照なしで 200**・`ref_wav=null`。本体が「デフォルト」を no_ref に写像するとき、voice 欄を併記すると**気づかず素の声になる**（§4-3）。
10. **上流の 2 つの穴（新規）**＝⒜ `voices.json` が壊れると **`GET /v1/audio/voices` が 500** なのに `/health` は 200（§3-5）。⒝ **同一 stem の `.wav` と `.pt`／`.speaker.safetensors` は `.wav` が黙って勝つ**＝事前計算潜在（`31` §3 の逃げ道）が**同名では効かない**（§3-6）。さらに `GET /v1/audio/voices`（一覧）と `GET /v1/audio/voices/{id}`（単票）が**同じ id に別の檔を返す**（§3-6）。

---

## 0. 実験の作り（再現手順）

```bash
source C:/Users/mugonkun/source/repos/irodori-native-research/lab/bin/env_server_contract.sh
"$PY" C:/Users/mugonkun/source/repos/irodori-native-research/lab/bin/37_contract.py \
      --out C:/Users/mugonkun/source/repos/irodori-native-research/lab/out/37_contract.json
# --only は a(スキーマ) b(話者) c(デフォルト) d(契約) の文字を並べる。全部で 3 秒弱。
```

- 環境は `14` §0-1 のまま（fastapi 0.141.1／pydantic 2.13.5／pydantic-settings 2.15.0）。**pytest は使わない**（`.pytest_cache` を upstream に書かないため）。
- 作業 CWD＝`lab/tmp37`（`voices/` はそこ）。`--out` の相対パスは CWD 基準になるので**絶対パスで渡すこと**（本便は 1 回踏んで `lab/out/` へ移した）。
- `FakeRuntime` は `14` と同じ作り（`tests/conftest.py` 相当）＝**モデルは 1 バイトも読まない**。ゆえに「参照 wav が実在するか」「壊れた wav か」は本便では分からない（`14` の Fake の限界と同じ・§8 の U に置く）。

---

## 1. 要求④「本体から設定可能パラメータ一覧を取得できる事」

### 1-1. 事実 — `/openapi.json`・`/docs` は出るか（実射）

`app.py:119-123` は `FastAPI(title=…, version="0.1.0", lifespan=…)` **だけ**＝`docs_url`／`redoc_url`／`openapi_url` を**明示していない**＝FastAPI の既定がそのまま生きる。`config.py` にも該当 env は無い（`config.py:10-72` の 50 欄に docs 系ゼロ）。実射：

| 口 | status | Content-Type | バイト |
|---|---|---|---|
| `GET /openapi.json` | **200** | `application/json` | **10,850** |
| `GET /docs` | **200** | `text/html; charset=utf-8` | 1,032 |
| `GET /redoc` | **200** | `text/html; charset=utf-8` | 914 |
| `GET /docs/oauth2-redirect` | **200** | `text/html; charset=utf-8` | 3,012 |

- **`IRODORI_API_KEY` を設定しても 4 口とも 200 のまま**（実射＝`openapi_with_api_key` 200／10,850 B・`docs_with_api_key` 200）。理由＝認証は `Depends(require_auth)` を**ルートごとに**付ける形（`app.py:203,218,237,259,272,299,314`）で、docs 系ルートは FastAPI が自前で足すため素通り。**⇒ 配布版で API キーを掛けても、スキーマだけは無認証で読める**（本体には都合がよく、外部公開時は情報漏れ）。
- **`/docs` の HTML は CDN を引く**（逐語）：

```html
<link type="text/css" rel="stylesheet" href="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui.css">
<link rel="shortcut icon" href="https://fastapi.tiangolo.com/img/favicon.png">
<script src="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
```

`/redoc` も `fonts.googleapis.com` と（本文中の）redoc CDN を引く。**⇒ ネット遮断機／社内 proxy では `/docs` は白紙**。`/openapi.json` は自己完結（純 JSON）なので影響なし。

### 1-2. 事実 — スキーマに何が載るか（実射・機械集計）

`components.schemas` は 6 件＝`SpeechRequest`／`IrodoriOptions`／`Body_upload_voice_v1_audio_voices_post`／`Body_replace_voice_v1_audio_voices__voice_id__put`／`HTTPValidationError`／`ValidationError`。`paths` は 5 件（`/health`・`/v1/models`・`/v1/audio/voices`・`/v1/audio/voices/{voice_id}`・`/v1/audio/speech`）。openapi 3.1.0。

| 集計 | `IrodoriOptions` | `SpeechRequest` |
|---|---|---|
| properties 欄数 | **44** | 7 |
| `additionalProperties` | **true**（＝`extra="allow"`・`app.py:38`） | **true**（`app.py:87`） |
| `default` を持つ欄 | **0** | 1（`speed: 1.0`） |
| `minimum`／`maximum` を持つ欄 | **0** | 1（`speed` ge 0.25 / le 4.0） |
| `minLength`／`maxLength` | 0 | 1（`input` 1〜4096） |
| `description` を持つ欄 | **0** | 0 |
| `enum` を持つ欄 | **3**（`t_schedule_mode`=[linear,sway]／`decode_mode`=[sequential,batch]／`cfg_guidance_mode`=[independent,joint,alternating]） | 0 |
| required | なし（全欄 optional） | `["model","input"]` |

載り方は全欄この形（逐語）：

```json
"num_steps":      {"anyOf":[{"type":"integer"},{"type":"null"}], "title":"Num Steps"}
"cfg_scale_text": {"anyOf":[{"type":"number"}, {"type":"null"}], "title":"Cfg Scale Text"}
"caption":        {"anyOf":[{"type":"string"}, {"type":"null"}], "title":"Caption"}
"ref_wavs":       {"anyOf":[{"items":{"type":"string"},"type":"array"},{"type":"null"}], "title":"Ref Wavs"}
"t_schedule_mode":{"anyOf":[{"type":"string","enum":["linear","sway"]},{"type":"null"}], "title":"T Schedule Mode"}
```

**`title` は pydantic が欄名から機械生成したもの**（`Cfg Scale Text`）＝人が読む表示名ではない。**`default` が 1 欄も無いのは、44 欄すべてが `X | None = None` だから**＝「既定は null」であって「既定は 40」ではない。

### 1-3. 事実 — 44 欄と `SamplingRequest` 43 欄の差集合（実射・dataclass 実物と照合）

- `SamplingRequest` の欄数＝**43**（`inference_runtime.py:202-246`・`dataclasses.fields()` 実測）。
- `IrodoriOptions` にしか無い＝**3**（`chunking_enabled`・`chunk_min_chars`・`first_sentence_chunk_min_chars`＝Server 専用のチャンク欄）。
- `SamplingRequest` にしか無い＝**1**（**`speaker_uncond_mode`**）。`text` は本文なので除外。
- ⇒ 44 = 43 − `text` − `speaker_uncond_mode` + チャンク 3。報告 §3-1 と `18-field-diff.json` の値を**独立に再現**（一致）。

### 1-4. 事実 — 「既定値」は 3 層に分かれ、env で動く（本節が本便の核心）

`_coalesce`（`app.py:832-1045`）の順は `irodori.X`（非 null）→ トップレベル `X` → **`settings.default_X`**（`14` T-2）。したがって欄の「実効既定」は**サーバの env に依る**。

| 層 | 出所 | 欄数 | 動くか |
|---|---|---|---|
| ⑴ dataclass 既定 | `inference_runtime.py:202-246` | 43 | **不動**（コード） |
| ⑵ Server の既定 env | `config.py:47-72` の `IRODORI_DEFAULT_*` | **22** | **運用者が動かせる** |
| ⑶ 要求本文 | `irodori.X`／トップレベル `X` | 44 | 発話ごと |

実射で ⑴ と ⑵ の**現在値は 22 欄すべて一致**（`env_overrides_dataclass` = 空）。ただし一致は偶然の同値であって保証ではない＝`IRODORI_DEFAULT_NUM_STEPS=10` を配布物の `.env` に書けば、その瞬間から「既定 40」は嘘になる。

**env を持つ 22 欄**（`config.py:47-72`）：`num_steps 40`／`t_schedule_mode linear`／`sway_coeff -1.0`／`duration_scale 1.0`／`min_seconds 0.5`／`max_seconds 30.0`／`cfg_scale_text 3.0`／`cfg_scale_speaker 5.0`／`cfg_guidance_mode independent`／`cfg_min_t 0.5`／`cfg_max_t 1.0`／`context_kv_cache true`／`max_ref_seconds None`／`ref_normalize_db -16.0`／`ref_ensure_max true`／`trim_tail true`／`tail_window_size 20`／`tail_std_threshold 0.05`／`tail_mean_threshold 0.1`／`num_candidates 1`／`decode_mode sequential`／`chunking_enabled true`／`chunk_min_chars 80`／`first_sentence_chunk_min_chars None`（＋`response_format wav`）。

**env を持たない＝dataclass 既定しか無い 19 欄**：`caption`／`ref_wav`／`ref_wavs`／`ref_latent`／`ref_latents`／`ref_embed`／`no_ref`／`seconds`／`max_text_len`／`max_caption_len`／**`cfg_scale_caption`**／`cfg_scale`／`truncation_factor`／`rescale_k`／`rescale_sigma`／`speaker_kv_scale`／`speaker_kv_min_t`／`speaker_kv_max_layers`／`seed`／`lora_adapter`。

**`cfg_scale_caption` の落とし穴（檔:行）**＝env が無いため、`app.py:932-938` は **`settings.default_cfg_scale_text`（3.0）を流用**する。一方 dataclass 既定も 3.0、**gradio の初期値は 4.0**（`gradio_app_voicedesign.py:538-544`）。⇒ **「感情の効き」の既定が UI と API で違う**（報告 §3-2 の指摘を檔:行で確定）。しかも `IRODORI_DEFAULT_CFG_SCALE_TEXT` を動かすと **caption 側の既定まで一緒に動く**（本便の新発見）。

### 1-5. 事実 — 範囲（Min/Max/Step）の唯一の機械可読な出所

HTTP には 1 欄も無い（§1-2）。上流にあるのは 2 つだけ：

| 出所 | 形 | 中身 |
|---|---|---|
| **gradio のスライダ**（`gradio_app.py:481-531`・`gradio_app_voicedesign.py:538-544`） | Python 定数 | `num_steps` 1〜120 step 1 既定 40／`num_candidates` 1〜MAX step 1／`duration_scale` **0.5〜1.5** step 0.01 既定 1.0／`sway_coeff` **−1.0〜1.5** step 0.1 既定 −1.0／`cfg_scale_text` 0〜10 step 0.1 既定 3.0／`cfg_scale_speaker` 0〜10 step 0.1 既定 5.0／**`cfg_scale_caption` 0〜10 step 0.1 既定 4.0**／`cfg_min_t` `cfg_max_t` は `gr.Number`（範囲なし・既定 0.5／1.0）／`context_kv_cache` Checkbox 既定 True |
| `upstream/Irodori-TTS/docs/parameters.md`（527 行・英文） | 表＋散文 | CLI フラグ名で **73 種**。既定値と「推奨」の散文はあるが**数値範囲はほぼ無い**（`grep` で range/between の記述は 0 件・「recommended」が 6 件） |

**注意**＝gradio の範囲は**UI の見た目の範囲**であって検査ではない。API は範囲を一切見ない（§2-1 の実射）。

### 1-6. 成立するか

**④は成立する。ただし「上流の `/openapi.json` を本体がそのまま読む」形では成立しない。**

| 案 | 成立 | 根拠 |
|---|---|---|
| Ⓘ 本体が上流 `/openapi.json` を読む | **×** | 型と enum 3 本しか無い。`ParamDescriptor` の必須欄 `Min`/`Max`/`Step`/`DefaultValue`（`Core/Models/Capabilities.cs:50-59`）が埋まらない |
| Ⓙ 本体が固定表を持つ | △ | 上流の版が上がると腐る。**env で既定が動く 22 欄で嘘になる**（§1-4） |
| Ⓚ **配布版が自前の「パラメータ一覧」口を建て、本体はそれ 1 本を読む** | **○** | 範囲は gradio 由来・既定は**起動中の Server の `/health` と env から実測して返せる**（`/health` は既に `defaults` 4 欄を返す＝`app.py:196-201`）。表示名・グループ・感情フラグ・UI ヒントも同じ口に乗る |
| Ⓛ Ⓚ＋上流 `/openapi.json` を「欄の存在と型の検算」に使う | ○ | 上流の版上げで欄が増減したら差分が出る＝腐りの検知 |

### 1-7. 制約・落とし穴

1. `/docs` は**オフラインで白紙**（§1-1）＝配布版の UI に埋め込むなら swagger-ui を同梱するか自前 UI に。
2. `/openapi.json` は**API キーを掛けても無認証**＝外部公開時は塞ぐ判断が要る。
3. 44 欄のうち**範囲があるのは `speed` だけ**。`speed` は `SpeechRequest` 側（ge 0.25 / le 4.0）で、**`duration_scale` とは別欄・両方送ると除算で合成**（`14` T-7・`app.py:843-844`）。
4. `title` は機械生成（`Cfg Scale Text`）＝**日本語の表示名は上流のどこにも無い**。
5. `speaker_uncond_mode` は HTTP から触れない 1 欄（`ref_embed`＝Speaker Inversion のときだけ効く・`docs/parameters.md:174`）。
6. **`max_text_len`／`max_caption_len` の実既定は「チェックポイントのメタデータ」**（`docs/parameters.md:49-50`）＝コードにも env にも無い＝**モデルを読まないと分からない**。本体に固定表を持つと、この 2 欄は必ず嘘になる。

### 1-8. 本体（yomiwakechan2）が決めること

- **α** ParamDescriptor を**起動時に 1 往復で取る**（`EngineCapability` は毎起動再取得・非永続＝`Core/Models/Capabilities.cs:3-5` の設計に合う）か、**固定表＋差分検知**にするか。
- **β** 露出する欄数（裁定 6 の改訂＝報告 §0-2 の 4）。44 欄全部か、感情に効く 5〜8 欄（`caption`・`cfg_scale_caption`・`cfg_scale_text`・`cfg_scale_speaker`・`num_steps`・`t_schedule_mode`・`sway_coeff`・`duration_scale`）か。
- **γ** 範囲の出所を「gradio 由来の値」と認めるか（＝上流が保証していない数字を本体の UI が名乗る）。
- **δ** `speed`（本体の既存 5 本）と `duration_scale`（新規候補）の**排他**（`14` T-7）。
- **ε** 表示名・グループ（`IsEmotion`）・説明文を**誰が書くか**＝上流に無い＝配布版か本体のどちらかが日本語を書き足す。

### 1-9. 新リポ（配布版 irodori-TTS）が満たす受け入れ条件（数値）

1. **パラメータ一覧の口が 1 本**あり、**1 往復・応答 ≤ 200 ms（ウォーム）**で返る。**モデル未ロードでも返る**（`/health` と同じく＝`app.py:166` の性質を保つ）。
2. その応答に **1 欄あたり 8 属性**＝`id`／`kind`(Number|Choice|Flag|Text)／`min`／`max`／`step`／`default`／`choices`／`display_name` が**必ず入る**（null 可の欄は `nullable:true` を別に持つ）。**`default` が null の欄は 0 件**（＝「既定は決まっていない」を返さない）。
3. **露出欄数 ≥ β で決めた数**、うち **`min`/`max`/`step` が埋まった Number 欄 ≥ 8**（§1-5 の gradio 8 本を下回らない）。
4. `default` は**起動中のプロセスの実効値**（env 反映後）を返す＝`IRODORI_DEFAULT_NUM_STEPS=10` を設定した配布物で `num_steps.default == 10` になる（**実射 1 本で検証可能**）。
5. **オフライン機（DNS 遮断）で UI とスキーマ口の両方が 200**＝外部 CDN 参照 **0 件**（`grep -c "https://" 配布物の html` が 0）。
6. 上流の 44 欄との差分検知＝配布版の一覧に無い上流欄／上流に無い一覧欄を**起動時に 1 行ログ**（版上げの腐り検知）。

---

## 2. 要求⑥後半「パラメータなどは読み分けちゃんからの指定で動作」

### 2-1. 事実 — スキーマは真実と一致しない（T-3／T-4 の再現と拡張・全部実射）

| # | 送った物 | 経路 | 結果（実射） | スキーマの言い分 |
|---|---|---|---|---|
| S-1 | `irodori:{num_step:7, totally_unknown:1}` | ネスト | **200**・`num_steps` は **40** のまま | `additionalProperties: true` ＝**仕様どおり黙る** |
| S-2 | トップレベル `t_schedule_mode:"zigzag"`／`decode_mode:"nonsense"`／`cfg_guidance_mode:"bogus"` | 平坦 | **200**・ランタイムに **`"zigzag"`／`"nonsense"`／`"bogus"` がそのまま届く** | `enum` 3 本が載っている＝**スキーマは嘘** |
| S-3 | `irodori:{t_schedule_mode:"zigzag"}` | ネスト | **422**（`literal_error`・逐語に**サーバの絶対パスと行番号**が出る） | 一致 |
| S-4 | `speed: 9.0` | トップレベル | **422** | 一致（ge/le が載っている唯一の欄） |
| S-5 | `irodori:{num_steps: -5}` | ネスト | **200**・ランタイムに **`-5`** が届く | 範囲が載っていない＝**スキーマは何も言っていない** |
| S-6 | `irodori:{cfg_scale_text: 999.0}` | ネスト | **200**・**999.0** が届く | 同上 |
| S-7 | `irodori:{num_steps:"たくさん"}` | ネスト | **422**（`int_parsing`） | 一致 |
| S-8 | トップレベル `num_steps:"たくさん"` | 平坦 | **400** `"num_steps must be an integer."` | 一致（`_as_int` が拾う。**S-2 の Literal だけが素通る**） |

**S-2 と S-8 の非対称が本便の追加確認**＝トップレベル経路は**整数・小数の型は見る**（`_as_int`／`_as_float`）が、**`Literal` の 3 欄だけは誰も見ない**。∴「白名簿検査が必須」（T-3）は正しく、さらに**「Literal 3 欄は必ず `irodori` ネストに入れる」**が具体の処方になる。

**S-3 の 422 本文（逐語・そのまま利用者に出してはいけない例）**：

```
1 validation error:
  {'type': 'literal_error', 'loc': ('body', 'irodori', 't_schedule_mode'), 'msg': "Input should be 'linear' or 'sway'", 'input': 'zigzag', 'ctx': {'expected': "'linear' or 'sway'"}}

  File "C:\Users\mugonkun\source\repos\irodori-native-research\upstream\Irodori-TTS-Server\src\irodori_openai_tts\app.py", line 314, in create_speech
    POST /v1/audio/speech
```

**さらに悪い形**＝`input` が 4,096 字を超えたときの 422 は、**本文 5,000 字を丸ごと `'input': '…'` に echo する**（実射で確認）。ログにそのまま流すと 1 発で 5 KB。

### 2-2. 成立するか

**成立する。ただし「本体が送ったとおりに動く」を保証するのは Server ではなく間の層。**
上流は⑴未知欄を黙って捨て、⑵Literal を平坦経路で素通しし、⑶数値範囲を一切見ない。したがって**配布版の側に白名簿と範囲検査を置かない限り、本体の指定ミスは「無音の別の音」になる**。

### 2-3. 制約・落とし穴（本体アダプタに効くもの・`14` T-1〜T-12 から抜粋＋本便の実射）

| 記号 | 契約 | 本体側の効き |
|---|---|---|
| T-2 | 優先順＝`irodori.X`（非 null）→ 平坦 `X` → env。**ネストの `false`／`0` は勝ち、`null` は負ける** | **`irodori` ネストで統一して送る**。「既定に戻す」は欄を**出さない**ことで表す |
| T-3 | 綴り違い・未知欄は**沈黙**（本便 S-1 で再現） | 白名簿。**Literal 3 欄はネスト固定**（S-2） |
| — | **範囲検査ゼロ**（S-5・S-6） | クランプは本体か配布版が持つ。`ParamDescriptor.Min/Max` は**本体の UI の話であってサーバの検査ではない** |
| T-4 | `seed` は**先頭 chunk の値しか `X-Irodori-Seed` に出ない**。**seed 未指定はチャンクごとに別 seed／指定すると全チャンク同一 seed**（本便 §5-1 で再実射・4 チャンクとも 1234） | 先読みキャッシュは `chunking_enabled:false` か `seed` 明示が前提 |
| T-7 | `seconds` 明示で chunking が**黙って無効**／`speed` と `duration_scale` は除算で合成 | 「声の値」欄で排他に |
| T-5 | **SSE はチャンク粒度で他要求と交互配車** | 順序を守るなら層側で 1 本ずつ直列 |
| T-8 | エラーは **400／422／500／503／SSE `event: error`** の 5 態。**422 と未知 voice の 400 は絶対パスを漏らす**（本便で 4 例の逐語＝§5-2） | 生 message を利用者に出さない。ログには残す（ただし 422 の本文 echo に注意） |
| — | **キャンセル不可**（`asyncio.shield` は **SSE だけ**＝`app.py:781`・`30` §2-4）。中止の粒度は**チャンク**が上限 | 「止める」は次チャンクを投げないこと |
| `30` §2-6 | **同時要求は Lock で直列**（`max_concurrent_synthesis` 既定 1・`inference_runtime.py:620` の `_infer_lock`）＝1 プロセス 1 合成 | 先読みは待ち行列を作るだけ |
| `31` §3 | **参照ありは毎要求・毎チャンク再符号化**（本便 §5-1 で 4 チャンクとも同じ `ref_wav` が届くことを実射） | チャンク数だけ参照符号化が走る |
| `31` §8 | **参照を当てると予測尺が変わる**（94.3→82.4 フレーム）＝同じ本文でも出力長が変わる | 尺合わせ・字幕同期の仕様に効く |

### 2-4. 本体が決めること

- **ζ** クランプの担当（本体の `VoiceResolver` が既にクランプする器を持つ＝`IrodoriSpeechRequest.cs` の冒頭 doc）か、配布版か、両方か。
- **η** 5 態のエラーを本体の理由 1 行にどう畳むか（既存の `IsMissingVoiceFailure`／`IsAlreadyRegisteredFailure` は**文言依存**＝§6-2 で差分を書く）。
- **θ** 先読み（`IrodoriPrefetcher`・344 行）のキーに `seed` を含めるか＝T-4 の帰結。

### 2-5. 新リポが満たす受け入れ条件（数値）

1. **白名簿の外の欄は 400 で拒む**＝未知欄を 1 つ混ぜた要求で **200 が返る件数 0**（S-1 の再射が 400 になる）。
2. **Literal 3 欄は平坦経路でも検査される**＝`t_schedule_mode:"zigzag"` は**どの経路でも 400／422**（S-2 の再射が 200 にならない）。
3. **範囲外は 400**＝`num_steps:-5`・`cfg_scale_text:999` が 200 にならない（S-5・S-6 の再射）。
4. **エラー本文にサーバの絶対パスと本文 echo を出さない**＝5 態すべての body で `grep -c "C:\\\\"` が 0、body の長さ **≤ 512 B**（現状 422 は 5,000 B 超）。
5. 5 態（400／422／500／503／`event: error`）に**機械可読な `code`** が入る（現状は `param`・`code` とも常に null＝`app.py:1156-1167`）。

---

## 3. 要求⑦「参照ボイス檔＋名付け＝話者」

### 3-1. 事実 — `GET /v1/audio/voices` は有る・応答形（実射逐語）

`app.py:218-234`。Bearer 必要。応答＝`{"object":"list","data":[…]}`、要素は **7 欄固定**：

```json
{"id":"ずんだもん","object":"voice",
 "ref_wav":"C:\\…\\lab\\tmp37\\voices\\ずんだもん.wav",
 "ref_wavs":null,"ref_latent":null,"ref_latents":null,"ref_embed":null,"no_ref":false}
```

- **絶対パスをそのまま返す**（＝本体が「この話者はどの檔か」を知れる。外部公開時は漏洩）。
- 並びは `sorted(voices)`（`voices.py:70`）＝**Python の文字列順**＝`1234 < UPPER < alice < dot.name < emoji😊 < inv < n-hyphen_under < none < precomputed < with space < ずんだもん < 四国 めたん < 春日部つむぎ`（実射のとおり）。**日本語は五十音順にならない**（コードポイント順）。
- `none`（`no_ref:true`）は `allow_no_ref_voice` が真なら**常に混ざる**（`voices.py:68-69`）。

**実射した檔名 13 種と結果**（全部 `voices/` に置いただけ）：`alice.wav`・`ずんだもん.wav`・`四国 めたん.wav`（**全角スペース入り**）・`with space.wav`・`dot.name.wav`・`UPPER.WAV`・`emoji😊.wav`・`n-hyphen_under.wav`・`1234.wav`・`春日部つむぎ.mp3`・`precomputed.pt`・`inv.speaker.safetensors` ⇒ **13 件すべて一覧に出た**。うち 8 種で `POST /v1/audio/speech` を撃ち、**8 件すべて 200・`ref_wav` に正しい絶対パスが届いた**（日本語・空白・絵文字・大文字・数字・ドット入り）。

⇒ **走査経路（`voices.py:168-186`）は voice_id を一切検査しない。**

### 3-2. 事実 — 登録 API は日本語・空白を拒む（本便の中心的発見）

`voices.py:24` `VOICE_ID_PATTERN = re.compile(r"^[A-Za-z0-9_-]+$")`、`voices.py:151-155` `validate_voice_id`。呼ばれるのは **`write_file`（POST/PUT）・`get_voice_file`（GET 単票）・`replace_voice`・`delete_voice`** の 4 箇所（`app.py:246,261,278,303`）。**`list_voices` と `resolve`（合成）では呼ばれない。**

| 口 | 与えた voice_id | status | body（逐語） |
|---|---|---|---|
| POST（multipart） | 檔名 `newvoice.wav`・`voice_id` 無し | **201** | `{"id":"newvoice","object":"voice_file","filename":"newvoice.wav","bytes":6444,"created_at":…}` |
| POST | 檔名 **`新しい声.wav`**・`voice_id` 無し | **400** | `voice_id must contain only ASCII letters, numbers, underscores, or hyphens.` |
| POST | `voice_id="ずんだもん2"` | **400** | 同上 |
| POST | `voice_id="four koku"`（空白） | **400** | 同上 |
| POST | `voice_id="dot.id"`（ドット） | **400** | 同上 |
| POST | `voice_id="zundamon_2-b"` | **201** | `{"id":"zundamon_2-b","filename":"zundamon_2-b.wav",…}` |
| POST | `latent.pt` | **400** | `Unsupported voice file extension '.pt'. Use one of: .aac, .flac, .m4a, .mp3, .ogg, .opus, .wav, .webm.` |
| POST | 既存 `alice.wav` | **409** | `Voice 'alice' already exists. Use PUT to replace it.` |
| GET 単票 | `alice` | 200 | メタ 5 欄 |
| GET 単票 | **`ずんだもん`／`with space`／`dot.name`** | **400** | `voice_id must contain only ASCII…`（**一覧には出ているのに単票は 400**） |
| DELETE | `ずんだもん` | **400** | 同上（**一覧に出ている話者を API で消せない**） |
| DELETE | `alice` | 200 | `{"id":"alice","object":"voice_file","deleted":true}` |

⇒ **⑦「日本語の名付け」を HTTP の登録口では作れない。** 作れる道は 2 つだけ＝**⒜ ファイルを `voices/` に直接置く**（配布版が自分のディスクに書く）／**⒝ `voices.json` の別名**（キーは無検査）。

### 3-3. 事実 — `voices.json` 別名（実射）

`voices.py:188-254`。置き場＝`IRODORI_VOICE_ALIASES_FILE`、未指定なら `voices_dir/voices.json`。**別名は走査結果を上書き**（`voices.py:66-67`）。実射した 5 種すべて 200：

| 別名キー | 書いた値 | 合成の結果（届いた `SamplingRequest`） |
|---|---|---|
| `"ずんだもん"` | `{"ref_wavs":["zunda_10s.wav","zunda_30s.wav"]}` | 200・`ref_wavs`＝絶対パス 2 本（**複数クリップは別名でしか作れない**＝走査は 1 檔＝1 話者） |
| `"四国 めたん"` | `"zunda_10s.wav"`（文字列） | 200・`ref_wav`＝絶対パス |
| `"ずんだもん（潜在）"` | `{"ref_latent":"zunda.pt"}` | 200・`ref_latent`＝絶対パス（**事前計算潜在の置き方**＝`31` §3 の逃げ道） |
| `"デフォルト"` | `{"no_ref":true}` | **200・`no_ref=true`**（⑧の第 3 の道＝§4-2） |
| `"絶対パス"` | `{"ref_wav":"C:/…/zunda_10s.wav"}` | 200・そのまま |

- 相対パスは `voices_dir` 基準、絶対パスはそのまま（`voices.py:250-254`）。
- `ref_wavs`／`ref_latents` は**非空配列でないと `ValueError`**（`voices.py:238-248`）。
- **別名が指す先が存在しなくても一覧にも合成にも出る**（実射＝`{"欠番":"missing.wav"}` で一覧に `欠番` が出て、Fake 経路では 200）。**実ランタイムでは合成時に初めて落ちる**（`voices.py:119-127` は中身を検証しない＝`06` §3-5）。

### 3-4. 事実 — `VoiceSpec` はメタを持てない（`31` §1-1 の実射確定）

`dataclasses.fields(VoiceSpec)` の実測＝**7 欄**：`voice_id`／`ref_wav`／`ref_wavs`／`ref_latent`／`ref_latents`／`ref_embed`／`no_ref`（`voices.py:27-35`）。

`voices.json` に**余分な鍵を書いた実射**：

```json
{"メタ入り": {"ref_wav":"zunda_10s.wav","caption":"優しい声","cfg_scale_caption":4.0,
              "display_name":"ずんだもん","num_steps":10}}
```

⇒ **一覧は 200 で `ref_wav` だけを返す**（`caption` も `display_name` も応答に無い）。**合成も 200 だが `caption=null`・`num_steps=40`・`cfg_scale_caption=3.0`＝全部既定**（`_parse_alias`（`voices.py:213-249`）が知らない鍵を読まないだけ・**エラーも警告も出ない**）。

⇒ **話者メタ（名付けの表示名・caption・既定パラメータ・参照秒数・作成日時）は上流に置き場が無い＝配布版アプリが自前の台帳を持つしかない**（断定・実射根拠つき）。

### 3-5. 事実 — 変更検知（要求ごとに `iterdir`・キャッシュ無し）

`voices.py:168-186` は毎回 `root.iterdir()`。実射＝同じ `TestClient` のまま：

```
before      : none, zunda, zunda_10s, zunda_30s, ずんだもん, ずんだもん（潜在）, デフォルト, 四国 めたん, 絶対パス
+hot_added  : hot_added が即座に増える（再起動なし）
-hot_added  : 即座に消える
```

⇒ **再起動不要・即反映**（`README.md:148` の記述を実射で確認）。代償＝**話者数に比例して毎要求 `iterdir` が走る**（`06` §3-1 の「巨大だと遅くなる」は本便でも未実測＝U-37-3）。

**新発見の穴**＝`voices.json` が壊れていると **`GET /v1/audio/voices` が 500**：

```json
{"error":{"message":"Expecting property name enclosed in double quotes: line 1 column 3 (char 2)",
          "type":"server_error","param":null,"code":null}}
```

同時に **`/health` は 200**・**`irodori.no_ref=true` の合成も 200**。⇒ **「サーバは健康なのに話者一覧だけ死ぬ」状態が作れる**。本体の発見段が `/health` だけを見る現行設計（`IrodoriDiscovery.cs`）ではこれを検知できない。

### 3-6. 事実 — 同一 stem の衝突（新発見・2 つ）

`voices/` に `zunda.wav`＋`zunda.pt`＋`zunda.speaker.safetensors` を同居させた実射：

- 一覧は **`zunda` 1 件・`ref_wav` だけ**（`.pt` も `.speaker.safetensors` も**消える**）。理由＝`_scan_voice_files` が `out[stem]` を上書きし、`sorted(root.iterdir())` の順で `zunda.pt` → `zunda.speaker.safetensors` → **`zunda.wav` が最後に勝つ**（`voices.py:171-186`）。
- ⇒ **事前計算潜在（`31` §3・増分が消える逃げ道）は「同じ名前で `.pt` を隣に置く」形では効かない**。効かせるには**別 stem** か **`voices.json` 別名**。**警告は一切出ない。**

`voices/` に `meta.wav`＋`meta.mp3` を同居させた実射：

- `GET /v1/audio/voices`（一覧）は **`meta.wav`** を返す。
- `GET /v1/audio/voices/meta`（単票）は **`meta.mp3`** を返す（`get_file` は `sorted(root.iterdir())` の**最初の一致**＝`voices.py:104-109`）。
- ⇒ **同じ id について 2 つの口が別の檔を名乗る。** `DELETE` も単票側の規則で動く＝**一覧に出ている檔とは違う檔が消える**。
- `/health` の `voices.files` は**音声拡張子の檔数**（この例で 3）＝**話者数（2＋`none`）ではない**（`app.py:191-195`）。本体が「話者が何人いるか」を `/health` から数えると外れる。

### 3-7. 成立するか

**⑦は成立する。ただし「配布版アプリが `voices/` の中身を自分で書く」ことが前提。** HTTP の登録口だけでは日本語の名付けができない（§3-2）。

| 案 | 名付け | 複数クリップ | 潜在 | メタ | 成立 |
|---|---|---|---|---|---|
| ⒜ 檔名 stem をそのまま話者名にする（配布版がファイルを直接置く） | 日本語可 | ×（1 檔＝1 話者） | 別 stem なら可 | × | ○（最も簡単・§3-6 の衝突に注意） |
| ⒝ `voices.json` 別名を配布版が生成 | 日本語可 | **○** | **○** | ×（余分な鍵は捨てられる＝§3-4） | ○（最も表現力がある） |
| ⒞ 上流の POST を使う | **ASCII のみ** | × | ×（`.pt` は 400） | × | **×**（⑦を満たさない） |
| ⒟ 配布版が独自の話者 CRUD を建て、内部で ⒜／⒝ を書く | 自由 | ○ | ○ | **○**（台帳は配布版が持つ） | ○（要求④⑦⑧を一手に満たす） |

### 3-8. 制約・落とし穴（まとめ）

1. **`^[A-Za-z0-9_-]+$` は POST/PUT/GET単票/DELETE の 4 口だけ**。一覧と合成は無検査（§3-2）。
2. **一覧の並びはコードポイント順**＝日本語は五十音にならない。
3. **絶対パスが応答に出る**（一覧・エラー本文とも）。
4. **`voices.json` の壊れは 500・`/health` は 200**（§3-5）。
5. **同 stem 衝突は無警告**・**一覧と単票が食い違う**（§3-6）。
6. **アップロードは中身を検証しない**（`voices.py:119-127`）＝壊れた wav は合成時に初めて落ちる（`06` §3-5）。
7. **`voices_dir` は既定が CWD 相対 `Path("voices")`・起動時に `mkdir`**（`config.py:41`・`voices.py:58-61`・`app.py:104`）＝読取専用の導入先だと**起動で落ちる**（`30` §1-A・N-8）。**絶対パスの `IRODORI_VOICES_DIR` が必須。**
8. **本体は登録した voice を消さない流儀**（現行 `IrodoriVoiceRegistry.cs` の doc）＝配布版が話者台帳を持つなら**掃除の責任がどちらにあるか**が新しい論点。

### 3-9. 本体が決めること

- **ι** 話者を **`CharacterCapability` の複数件**として列挙するか（現行は擬似キャラ 1 件固定＝`IrodoriCapabilityBuilder.cs`）。⇒ これは**裁定 4／10 の改訂**（「話者を問い合わせる意味がない」「発見は `/health` 1 段で閉じる」の上書き）。
- **κ** 話者一覧の取得タイミング＝発見時 1 回（`EngineCapability` はセッション中不変・`Capabilities.cs:3-5`）か、都度か。**上流は即反映なので「都度」が取れるが、本体の器は「起動時 1 回・非永続」に寄っている。**
- **λ** 参照 wav の実体を**どちらが持つか**＝本体のプロファイル（現行＝欄にフルパス）か、配布版の `voices/`（新要求の読み＝配布版が話者台帳を持つ）か。**両方に持たせると同期問題が生まれる。**
- **μ** 話者ごとの既定パラメータ（caption・cfg・steps）を**話者メタに載せるか**、本体の `VoiceSetting` 側に置くか。上流に置き場は無い（§3-4）。

### 3-10. 新リポが満たす受け入れ条件（数値）

1. **話者名に日本語・空白・記号が使える**＝`ずんだもん`／`四国 めたん`／`emoji😊` の 3 種を登録して**一覧・単票・削除・合成の 4 口すべてで 200**（現状は単票と削除が 400＝§3-2）。
2. **話者 1 件＝(参照檔 1 本以上, 名付け, メタ)** を扱える＝複数クリップ **≥ 2 本**、事前計算潜在 `.pt` **1 本**、`no_ref` の 3 形すべてが**同じ話者台帳の 1 行**で表せる。
3. **一覧の並びが日本語で自然**（かな順 or 登録順 or 利用者指定）＝コードポイント順にしない。
4. **話者一覧の応答が 100 件で ≤ 100 ms**（毎要求 `iterdir` の代わりに mtime 監視 or キャッシュ＋変更検知）。**追加・削除の反映は ≤ 2 秒**（上流の「即反映」を落とさない）。
5. **同 stem 衝突を 0 件にする**＝台帳の id が檔名と独立（衝突しえない）か、衝突時に**起動時 1 行の警告**。
6. **台帳が壊れても一覧の口は 200 で「0 件＋理由」を返す**（現状は 500＝§3-5）。
7. **応答に利用者の絶対パスを出さない**（`grep -c "C:\\\\"` が 0）。ファイルの同定は台帳 id で。

---

## 4. 要求⑧「参照なし＝話者名『デフォルト』」

### 4-1. 事実 — Server に「デフォルト」は無い（実射・全 12 語）

`voices.py:23` `NO_REF_IDS = {"none","no_ref","no-ref","null","text-only"}`、`voices.py:80-81` で `voice_id.lower()` が一致し、かつ `allow_no_ref_voice` が真なら no_ref。`voice_id` は先に `.strip()`（`voices.py:79`）。

| 送った `voice` | status | `no_ref` |
|---|---|---|
| `"none"` / `"None"` / `"NONE"` / `"no_ref"` / `"no-ref"` / `"null"` / `"text-only"` / `"TEXT-ONLY"` / `" none "` | **200** | **true** |
| **`"デフォルト"`** | **400** | — |
| `"デフォルト "`（末尾空白） | **400**（strip されて同じ文言） | — |
| `"default"` | **400** | — |

**400 の逐語（`"デフォルト"`）**：

```json
{"error":{"message":"\"Unknown voice='デフォルト'. Put a reference audio file in C:\\Users\\mugonkun\\source\\repos\\irodori-native-research\\lab\\tmp37\\voices, add an alias file, or use voice='none'.\"",
          "type":"invalid_request_error","param":null,"code":null}}
```

**404 ではなく 400**（`app.py:457-459` が `KeyError` → `HTTPException(400)`）。二重引用符は `str(KeyError)` の副作用。**voices ディレクトリの絶対パスを漏らす**（T-8）。
**`"default"` も駄目**（`NO_REF_IDS` に無い）＝英語でも通らない。

### 4-2. 事実 — 「デフォルト」→ no_ref に写像する道は 3 本（全部実射で 200）

| 道 | 形 | 実射 | 制約 |
|---|---|---|---|
| **①** 本体（またはアダプタ）が `"デフォルト"` を検知して `irodori.no_ref=true` に**置き換える** | `{"irodori":{"no_ref":true}}` | **200・`no_ref=true`**（voice 欄なしでよい＝T-1） | **`allow_no_ref_voice=false` でも通る**（実射・T-11 再現）＝最も頑丈 |
| **②** `"デフォルト"` を `"none"` に**言い換える** | `{"voice":"none"}` | 200・`no_ref=true` | `IRODORI_ALLOW_NO_REF_VOICE=false` にすると **400**（実射）。一覧にも `none` が出なくなる（実射＝一覧が `["zunda"]` だけになる） |
| **③** `voices.json` に **`{"デフォルト":{"no_ref":true}}`** を置く | `{"voice":"デフォルト"}` | **200・`no_ref=true`**・**一覧にも `デフォルト` が出る**（実射＝`["none","zunda","デフォルト"]`） | 配布版が `voices.json` を書ける前提。`none` も並ぶので**同じ意味の話者が 2 つ見える** |
| — | `IRODORI_DEFAULT_VOICE="デフォルト"` | **400**（実射） | env は「voice が省略されたときの既定値」を入れるだけで、**語の解決規則は同じ**＝救いにならない |
| — | `IRODORI_DEFAULT_VOICE="none"` | 200・`no_ref=true` | voice を送らない要求だけに効く |

### 4-3. 事実 — `no_ref=true` は voice を丸ごと無効化する（落とし穴・実射）

`app.py:437-454`＝`ref_wav`／`ref_wavs`／`ref_latent`／`ref_latents`／`ref_embed`／`no_ref` の**どれか 1 つでも明示されていれば `voice_registry.resolve()` に入らず**、`VoiceSpec(voice_id="request", …)` を作って返す。

実射：`{"voice":"zunda", "irodori":{"no_ref":true}}` → **200・`no_ref=true`・`ref_wav=null`**（`zunda.wav` は実在するのに使われない）。
実射：`{"voice":"デフォルト", "irodori":{"no_ref":true}}` → **200**（未知 voice なのに 400 にならない）。

⇒ **本体が「話者名 → voice 文字列」と「デフォルト → no_ref」の両方を送る形にすると、no_ref が付いた瞬間に参照が黙って落ちる。** ①を採るなら**`no_ref` を立てるときは voice を送らない**（または voice を送るときは `no_ref` を絶対に送らない）を契約にする。

### 4-4. 事実 — no_ref のときの声＝キャプションのみ

- `no_ref=true` かつ **caption 無しでも 200**（実射・`caption=null`）＝Irodori 自身の素の声（`35` 追記＝司令官の耳検分「参照なしは Irodori 自身の声」）。
- **caption に空文字・空白のみを送ると 400**（実射・逐語 `caption must be non-empty when specified.`）＝`app.py:852-855,1124-1130` の `_as_optional_str`。**「caption を空にする」は欄ごと省略でしか表せない**（現行アダプタ `IrodoriSpeechRequest.cs` が既に踏んで避けている＝§6-2）。
- 参照なしで声を変える手は **caption（512 トークン上限）＋本文中の絵文字 45 種＋`cfg_scale_caption`** の 3 層だけ（報告 §3-2）。**数値の感情軸は無い。**

### 4-5. 成立するか

**⑧は成立する。写像は本体か配布版のどちらかが必ず持つ**（Server は「デフォルト」を知らない）。**①（no_ref 直送）が最も頑丈**＝`allow_no_ref_voice` の設定に依らず、上流の 5 語の綴りにも依らない。

### 4-6. 本体が決めること

- **ν** 「デフォルト」の語を**本体が持つ**（アダプタが no_ref に写す）か、**配布版が持つ**（話者台帳の 1 行として `no_ref` を持ち、本体には普通の話者に見せる）か。**後者なら本体は⑦と⑧を区別しなくてよい**（要求⑦⑧が 1 本になる）。
- **ξ** 「デフォルト」を削除・改名できるようにするか（配布版の話者台帳での扱い）。
- **ο** 「デフォルト」に caption の既定を持たせるか（＝参照なしでも声色を固定したい場合）。

### 4-7. 新リポが満たす受け入れ条件（数値）

1. **話者一覧に「デフォルト」が必ず 1 件あり、削除できない**（削除要求は 400 で理由 1 行）。
2. **「デフォルト」を指定した合成が 200**＝`voice` の値として `デフォルト` をそのまま送って通る（現状は 400＝§4-1）。**404／400 が返る件数 0。**
3. **`no_ref` と参照の同時指定は 400**（現状は「参照が黙って落ちる」＝§4-3）。
4. **caption 空文字は 400 ではなく「省略と同じ」**（現状 400＝§4-4）＝空欄を送っても 200。
5. `allow_no_ref_voice` に相当する設定を**外に出さない**（no-ref を禁止できないノブ＝T-11・実射再現）。

---

## 5. 補足の実射（本体アダプタに効くもの）

### 5-1. チャンク分割時に voice・caption・seed がどう複製されるか

本文＝`こんにちは。今日はいい天気ですね。散歩に行きましょう。とても楽しみです。`、`chunk_min_chars=10`・`first_sentence=1`、`voice:"zunda"`・`irodori:{caption:"元気な声", seed:1234}`：

```
chunks: 4  texts: ["こんにちは。","今日はいい天気ですね。","散歩に行きましょう。","とても楽しみです。"]
ref_wav_each : 同じ絶対パス × 4   ← 参照は毎チャンク再符号化（31 §3 を実射で裏づけ）
caption_each : "元気な声" × 4      ← app.py:657,710 が text だけ差し替える
seed_each    : 1234 × 4            ← seed 明示なら全チャンク同一
X-Irodori-Seed: "1234"
```

seed 未指定の同じ本文：`seed_each = [null,null,null,null]`（＝ランタイムが各チャンクで `secrets.randbits(63)`）、**`X-Irodori-Seed: "8725727506779062217"` は 1 本目だけ**＝T-4 を再現。

### 5-2. エラー 4 態の逐語（本体が写像するもの）

| 状況 | status | message（逐語） |
|---|---|---|
| 未知 voice | **400** | `"Unknown voice='居ない人'. Put a reference audio file in C:\…\voices, add an alias file, or use voice='none'."`（**二重引用符つき・絶対パス漏れ**） |
| voice 未指定 | **400** | `'No voice was provided and IRODORI_DEFAULT_VOICE is not set.'`（**単引用符つき**） |
| model 不一致 | **400** | `Unsupported model 'gpt-4o-mini-tts'. Use 'irodori-tts'.` |
| `input` 空 | **422** | `1 validation error:\n  {'type': 'string_too_short', 'loc': ('body','input'), …}` ＋**絶対パスと行番号** |
| `input` 5,000 字 | **422** | `{'type':'string_too_long', …, 'input':'あああ…（本文 5,000 字を丸ごと echo）…'}` |
| `response_format:"xyz"` | **400** | （`normalize_response_format` の文言） |

`GET /v1/models` は 1 件固定（`{"id":"irodori-tts","object":"model","created":0,"owned_by":"irodori-tts"}`）。

---

## 6. 現行アダプタ（`yomiwakechan2/Engines/Irodori/`・読むだけ）と要求仕様の差分

### 6-1. 今どう呼んでいるか（檔:行）

| 面 | 現況 | 檔 |
|---|---|---|
| **話者** | **擬似キャラ 1 件固定**（`Characters` は 1 要素）。「列挙すべき話者が存在しない」「サーバへ話者を問い合わせる意味がない」＝裁定 4／10 | `IrodoriCapabilityBuilder.cs:24-41` |
| **発見** | **`GET /health` 1 段だけ**。`loaded=false` でも可。body は解釈しない | `IrodoriDiscovery.cs:6-24,55-` |
| **パラメータ** | **5 本固定**＝`caption`（Text）・`seed`（Text）・`voice`（Text＝**wav のフルパス**）・`speed`（Number）・`volume`（Number）。定義は 1 箇所 | `IrodoriConstants.cs:550-610`（`BuildSynthesisParams`） |
| **要求 JSON** | `{model,input,voice,response_format,speed,irodori:{caption?,seed?}}`。**`voice` は常に明示送信**（省略は 400）・参照なしは **`"none"`**。caption は空なら**欄ごと省略**（400 地雷の回避）・seed は数値化できたときだけ載せる | `IrodoriSpeechRequest.cs:88-129`・`IrodoriConstants.cs:52`（`VoiceNone="none"`） |
| **参照ボイス** | プロファイルの欄に **wav のフルパス**。初回合成時に **POST /v1/audio/voices** で自動登録。**登録名＝`ywk-` ＋ SHA-256 先頭 12 桁（16 進小文字）**。409 は「もう居る」＝成功と同義。1 回だけの自己回復（`Forget` → 再登録 → 再試行） | `IrodoriVoiceRegistry.cs:1-45,78-118` |
| **一覧の口** | **`GET /v1/audio/voices` は 1 度も呼んでいない**（grep＝POST だけ） | `IrodoriApiClient.cs:68-92` |
| **音量** | API に無いので **wav に乗算**（VOICEPEAK と同型） | `IrodoriVolumeScaler.cs` |
| **GPU／精度** | アダプタは device を一切触らない。**元栓が `IRODORI_MODEL_PRECISION`／`IRODORI_CODEC_PRECISION` の 2 本だけ env 注入**（fp32 を選んだときは 1 つも設定しない） | `IrodoriConstants.cs:200-232`（`PrecisionEnvironment`） |
| 先読み | 要求 JSON がそのままキャッシュ鍵（音量は構造的に鍵に入らない）。登録名が**内容ハッシュ由来**＝参照 wav が変われば鍵も変わる | `IrodoriSpeechRequest.cs` の doc・`IrodoriPrefetcher.cs` |

### 6-2. 要求仕様との差分（本便の実射で新たに分かるもの）

| # | 差分 | 効き |
|---|---|---|
| **D-1** | **⑦は「擬似キャラ 1 件」の設計を壊す**＝話者が N 件になる。裁定 4／10 の改訂が要る（`IrodoriCapabilityBuilder.cs:24-31` の doc がそのまま反証される） | Core 不触では済まない可能性＝`CharacterCapability` の複数件は既存の器（他 8 エンジンが使用）なので**器はある**。変わるのは Irodori の発見段 |
| **D-2** | **④は発見段を 1 往復増やす**＝`/health` だけの発見（裁定 10）にパラメータ一覧の口が加わる | `EngineCapability` は起動時生成・非永続（`Capabilities.cs:3-5`）＝**増やしても保存の形は増えない** |
| **D-3** | **`ywk-<hash12>` は偶然 `^[A-Za-z0-9_-]+$` を満たしていた**（§3-2）＝**現行が動いているのはこの命名のおかげ**。⑦で「日本語の名付け」を入れると**同じ POST 経路では 400 になる**（実射） | 配布版が独自の話者 CRUD を持つ根拠が 1 つ増える |
| **D-4** | 参照なしが **`voice:"none"`**（②の道）＝`IRODORI_ALLOW_NO_REF_VOICE=false` で**全滅する**（実射・400）。①（`no_ref:true`）なら落ちない | ⑧の写像を①に寄せると頑丈になる。ただし**①は voice を無効化する**（§4-3）＝`voice` と `no_ref` の同時送信をやめる必要 |
| **D-5** | `IsMissingVoiceFailure` は「400／404 で、本文に voice の語か登録名を含む」**文言依存**（`IrodoriVoiceRegistry.cs:96-118`）。実射の 400 文言は `Unknown voice='…'` で `voice` を含む＝**今は当たる**。ただし**日本語話者名だと単票／削除が 400 `voice_id must contain only ASCII…`**（§3-2）＝これも `voice` を含むので**誤検知して 1 回無駄に再登録する** | 配布版が `code` を返せば文言依存が消える（§2-5 の 5） |
| **D-6** | 現行は**話者メタを本体のプロファイル側に持っている**（参照 wav のパス＝`VoiceParamId` の Text 欄）。⑦は「配布版が (檔, 名付け) の対を持つ」＝**持ち主が移る** | λ（§3-9）の裁定次第で `VoiceParamId` の Text 欄が不要になる（＝Choice の話者選択に変わる） |
| **D-7** | GPU／CUDA 版の選択は**現行アダプタに欄が無い**（精度 2 択のみ）。⑥前半の「UI で選んだ GPU を暗黙に使う」＝**配布版アプリ側の設定として持ち、本体は触らない**なら差分ゼロ（元栓の env 注入も不要になる） | 報告 §4-2・§10-2 と整合。`29`・`30` の env 3 本（`IRODORI_MODEL_DEVICE`／`IRODORI_CODEC_DEVICE`／`IRODORI_PRELOAD`）の管掌が本体→配布版へ移る |
| **D-8** | 現行の発見は `/health` 200 だけ＝**`voices.json` 破損（§3-5）を検知できない**。⑦で話者一覧を読む設計に変えると、この 500 を必ず踏む | 発見段のエラー処理に 1 態増える |

---

## 7. 主席への注意（要点）

1. **④の答えは「`/openapi.json` は出るが使えない」**＝型と enum 3 本だけ。**既定 0 欄・範囲 0 欄・説明 0 欄**（実射）。本体の `ParamDescriptor` を組むには**配布版が自前の口を建てる**しかない（§1-6 のⓀ／Ⓛ）。この 1 点が「新リポを建てる」判断の材料として最も重い。
2. **既定値は env で動く 22 欄がある**（§1-4）＝「本体に固定表」は**運用で嘘になる**。`cfg_scale_caption` は env が無く `default_cfg_scale_text` を流用する（`app.py:936`）＝**text の既定を動かすと caption の効きも動く**。
3. **⑦の核心は「上流の登録 API が ASCII しか受けない」**（`voices.py:24`）。**現行アダプタが動いているのは `ywk-<hash>` が偶然 ASCII だったから**。日本語の名付けは**ファイルを直接置く**か**`voices.json` 別名**でしか作れない＝**配布版が `voices/` の所有者になる**という設計上の含意。
4. **`VoiceSpec` はパスと `no_ref` だけ**（実射で 7 欄を列挙）。**別名に書いた `caption`・`num_steps`・`display_name` は無警告で捨てられる**（実射）＝**話者メタは配布版の台帳**。`31` §1-1 の推定が実射で確定した。
5. **⑧「デフォルト」は 400（404 ではない）**。写像は 3 本あり、**`irodori.no_ref=true`（①）だけが `allow_no_ref_voice` に依存しない**。ただし**`no_ref` は voice を丸ごと無効化する**（`app.py:437-454`・実射）＝**voice と no_ref の同時送信を禁じる**契約が要る。現行アダプタは②（`voice:"none"`）＝設定 1 つで全滅する形。
6. **上流の穴を 3 つ新たに見つけた**（どれも無警告）＝⒜ `voices.json` 破損で一覧だけ 500・`/health` は 200／⒝ 同 stem は `.wav` が勝ち **事前計算潜在の逃げ道（`31` §3）が同名では効かない**／⒞ 一覧と単票が**同じ id に別の檔**を返す。⒝は §10-4 の 13（参照潜在のキャッシュ）に直接効く。
7. **422 は本文を丸ごと echo する**（5,000 字）＋**絶対パスと行番号**を漏らす。本体のログにそのまま流さないこと。
8. **設計は決めていない**。§1-6・§3-7 は選択肢の表であって推奨ではない。受け入れ条件の数値（§1-9・§2-5・§3-10・§4-7）は**検証可能な形に落としただけ**で、値そのものは卓が動かしてよい。

---

## 8. 本便で埋まらなかったもの（U-37）

| # | 未確認 | なぜ |
|---|---|---|
| U-37-1 | 実ランタイムで**日本語パスの参照 wav が読めるか**（`_load_audio` → soundfile／torchcodec の Windows 非 ASCII パス） | Fake はモデルを読まない。**⑦の成立に直結する 1 射**＝実機で `ずんだもん.wav` を当てて 200 と音を確認すべき（GPU 不要・CPU で可） |
| U-37-2 | 別名が指す先が**存在しない／壊れた wav** のときの実ランタイムの落ち方（500 の文言） | 同上 |
| U-37-3 | `voices/` が **100〜1,000 件**のときの `GET /v1/audio/voices` と合成の遅れ（毎要求 `iterdir`） | 実測せず。§3-10 の 4 の根拠になる |
| U-37-4 | `IRODORI_VOICE_ALIASES_FILE` を `voices_dir` の外に置いたときの相対パス基準（コードは `voices_dir` 基準＝`voices.py:255-259` だが実射せず） | 配布版が別名檔をどこに置くかに効く |
| U-37-5 | `/openapi.json` を**上流の版が上がったとき**にどれだけ変わるか（欄の増減の検知精度） | 本便は 1 コミットのみ |
| U-37-6 | 本体側 UI の器（`VoiceEditorViewModel.cs:230-232` の `IsFullWidth` 先勝ち）で、**44 欄を出したときの見た目** | 本体は読むだけ・UI は動かさない |

---

## 9. 停止域の確認（無改変の証明）

```bash
cd upstream/Irodori-TTS-Server && git status --porcelain   # -> 出力なし（clean）・HEAD 841fb7c
cd upstream/Irodori-TTS        && git status --porcelain   # -> 出力なし（clean）・HEAD 8224daf
cd C:/Users/mugonkun/source/repos/yomiwakechan2 && git status --porcelain  # -> 出力なし（clean・読むだけ）
find upstream -name "__pycache__"                          # -> 出力なし（PYTHONDONTWRITEBYTECODE=1）
```

- **8088／7861／8093 は起動していない**（TestClient＝プロセス内 ASGI 呼び出しのみ）。ポートは 1 つも開けていない。
- **GPU 未使用**（torch 2.10.0+cpu）。**HF キャッシュもモデルも 1 バイトも読んでいない**（FakeRuntime のため）。`HF_HUB_OFFLINE=1`。
- **外部通信ゼロ**。`/docs` の HTML は取得したが**ブラウザで開いていない**＝CDN へのアクセスは発生していない（HTML の文字列を見ただけ）。
- 本便が書いたのは **`lab/notes/37-handoff-contract.md`・`lab/bin/37_contract.py`・`lab/out/37_contract.json`・`lab/tmp37/`（作業ディレクトリ・`voices/` の試験檔）** だけ。`C:/IrodoriTTS/`・`C:/irodori-TTS-server/` には触れていない。

---

## 主席追記＝U-37-1（日本語パスの参照 wav で実合成が通るか）＝解消

lab の CPU 実ランタイム（torch 2.10・fp32・4 steps・torchaudio.load を soundfile に差し替え）で、`tmp/39/ずんだもん 10秒.wav`（日本語＋空白・24 kHz）を `_load_audio` に直接渡して `(1, 250368) @ 24000`・絶対パスでも同じ・日本語ディレクトリ `参照 置き場/` 越しの合成も完走（`prepare_reference` 1,607.7 ms・出力 3.24 s・警告なし）。生データ＝`lab/out/39_jp_path_cpu.json`。素の `torchaudio.load`（torchcodec 経路）は lab では ImportError＝torchcodec を残す設計なら別途 1 射。
