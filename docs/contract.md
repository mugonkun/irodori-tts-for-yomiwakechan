# contract.md — 配布版 irodori-TTS が本体（読み分けちゃん2）に約束する HTTP 契約

> **本檔は「本体が写す一枚」**＝本体リポの `docs/engine-adapters-overview-map.md` §9 と同じ書式で書き、
> 本体のアダプタ便（便 F）はここから写す。**正典は `decisions.md`**（配布版側）と本体の `decisions.md`
> （本体側）であり、両者が食い違ったらそれぞれの正典が正で本書が誤り＝直すべきは本書である。
> **書式の規律**（本体 `docs/operations.md` §3 に倣う）＝上流の檔:行は書く（腐りの検知点）が、配布版の
> 実装の行番号は書かない。記述には**契約テストの題目**を添える（契約が変わればテストごと書き換わる）。
>
> 依拠＝設計書 `docs/design/ben-a-skeleton-and-build.md`（便 A）／上流 pin＝`Irodori-TTS` `8224daf`・
> `Irodori-TTS-Server` `841fb7c`／事実の出典＝`research/`（調査便の写し）。
> **`schema: 1`**。この番号が上がるときは⑻の手順を踏む。

---

## ⑴ 接続

**`http://127.0.0.1:18088` の 1 本だけ。`localhost` の名前指定は禁止・api_key は無い。**

- **ポート 18088**（`decisions.md` 2）。既存の Irodori-TTS-Server 直結経路（8088）とは**別個体**で、
  同時に動かしてよい。本体の 9 番目のアダプタ（`irodori`・8088 固定＝`IrodoriConstants.cs:35`）は
  そのままで、配布版は**10 番目のエンジン**として別に建つ。
- **`localhost` を使わない**＝サーバは IPv4 でのみ待ち受け、`localhost` 名指定は IPv6 を先に試すため
  **毎要求 +1.2〜2.0 秒**の実測がある（本体 §9 の既知事実）。**必ず `127.0.0.1`**。
- **bind は `127.0.0.1` 固定**（ループバック限定）。外から触れないので **api_key を持たない**
  （`decisions.md` 2）。本体は `Authorization` ヘッダを付けない。**付けても無視される**。
- 起動主体は**配布版**（`decisions.md` 6）＝本体はサーバを起こさない。見つからなければ理由つき失敗で、
  他エンジンの読み上げは無傷（本体 §0-3 の掟 1・掟 2）。

題目＝`接続先は127_0_0_1の18088でありlocalhost名は使わない`／`Authorizationヘッダを付けない`。

---

## ⑵ 発見

**`GET /health` 200 → `GET /ywk/status` → `GET /params` → `GET /v1/audio/voices` の 4 段。
タイムアウト 5 秒・ready 待ち 120 秒・合成の可否は `runtime.loaded` で判る。**

| 段 | 口 | 何を得るか | 失敗時 |
|---|---|---|---|
| 1 | `GET /health` | 生きているか。**モデル未読込でも 200**（遅延ロードが正常形） | 到達不能＝エンジン未起動として理由つき失敗 |
| 2 | `GET /ywk/status` | 配布版か否か・版・上流 pin・**実 device**・話者件数（⑹） | 404 なら**上流の素の Server**（＝8088 経路の個体）＝配布版として扱わない |
| 3 | `GET /params` | パラメータ一覧（⑸）。**モデル未読込でも 200・≤ 100 ms** | 404 なら同上 |
| 4 | `GET /v1/audio/voices` | 話者一覧（⑷） | 500 は「台帳が壊れている」＝配布版では**返さない**（⑷ の約束） |

- **`/health` の body は上流のままで、解釈しない**（欄は上流のまま）。ただし配布版は
  **`/health` の絶対パスも `<path>` に畳む**＝上流はここに `settings.voices_dir` と
  HF スナップショットの実パス（＝OS の利用者名）をそのまま載せる（`app.py:167-193`）ので、
  200 でも本体・ランチャのログに実パスが乗らないようにしてある。`model.model_device` は
  `settings.model_device` の**素の echo**（`app.py:173`）＝`auto` は `auto` のまま返り、
  **実際に載った device は `/health` からは分からない**（`research/lab/notes/14` §7）。
  実 device を知りたいときは **`/ywk/status`**（⑹）を見る。
- **`/health.voices.files` は話者数ではなく音声拡張子の檔数**（`app.py:191-195`）＝
  ここから話者数を数えると外れる。話者数は `/v1/audio/voices` か `/ywk/status.voices.count`。
- **ready 判定＝`runtime.loaded`**（`research/lab/notes/14` T-9）。`preload=true` を焼いているので、
  起動後しばらくは `loaded=false`・`loading=true` が返る。**待ちは 120 秒**（CPU の埋め込み Python で
  読込 28 秒の実測・30 秒では足りない＝本体の `LaunchCompletionTimeout=120 s` は妥当）。
- **`loaded=false` の間に `POST /v1/audio/speech` を撃つと、エラーにならず待たされて 200 が返る**
  （本体 §9 の既知事実）。合成期限の設計はこれを飲み込む値にすること。
- `/openapi.json`・`/docs`・`/redoc` は上流のまま 200 で出るが、**本体は使わない**。
  `/docs` は jsdelivr の CDN を引くのでオフライン機では白紙になる（`research/lab/notes/37` §1-1）。

題目＝`発見はhealthからywkstatusとparamsとvoicesの4段`／`ywkstatusが404の個体は配布版として扱わない`／
`readyはruntimeloadedで判定し待ちは120秒`。

---

## ⑶ 合成

**`POST /v1/audio/speech` の 1 発で wav が返る（OpenAI 互換）。上流と違い、配布版は
未知欄・範囲外・排他違反を 400 で弾く。**

### 3-1 body の形

```json
{"model": "irodori-tts",
 "input": "本文（1〜4096 字）",
 "voice": "話者名（/v1/audio/voices の id）",
 "response_format": "wav",
 "speed": 1.0,
 "irodori": {"caption": "演技指示", "num_steps": 40, "seed": 1234}}
```

- `model` は `"irodori-tts"` 固定（他は 400）。
- **`voice` は明示送信を推奨**するが、**省略しても 400 にならない**（`decisions.md` 45）。
  配布版は `IRODORI_DEFAULT_VOICE` に **`"デフォルト"`** を焼く（wrapper の `setdefault`）ので、
  欄を出さなければ**参照なし合成**になる＝`/params` の `request.voice.default` が名乗る値と一致する。
  上流の素の挙動（`IRODORI_DEFAULT_VOICE` 未設定で 400＝`No voice was provided and
  IRODORI_DEFAULT_VOICE is not set.`）が出るのは、**ランチャがこの env を空にしたときだけ**
  （そのときの `code` は `ywk_missing_voice`＝3-3）。参照なしは **`"デフォルト"`**（⑷）。
  上流の別名（`none`・`no_ref`・`no-ref`・`null`・`text-only`・大小無視）を送っても
  **配布版は「デフォルト」に正規化して 200 を返す**（現行アダプタの `voice:"none"` 互換＝⑼ D-4）。
- **上流の 44 欄は `irodori` ネストに入れて送る**。優先順は **`irodori.X`（非 null）→ トップレベル
  `X` → env**（`research/lab/notes/14` T-2）＝env に焼いた既定は本体の指定に必ず負ける。
  「既定に戻す」は**欄を出さない**ことで表す（`null` を送ると負けるだけで、意味は同じ）。
- **Literal 3 欄（`t_schedule_mode`・`decode_mode`・`cfg_guidance_mode`）はネスト必須**。
  上流はトップレベル経路でこの 3 欄を**誰も検査せず素通しする**（`"zigzag"` が 200 で届く実射＝
  `research/lab/notes/37` S-2）。配布版はトップレベルにあれば 400 にする。
- `speed`（0.25〜4.0）と `irodori.duration_scale` は**別欄で、両方送ると除算で合成される**
  （`app.py:843-844`・`research/lab/notes/14` T-7）＝本体は**どちらか一方**にする。
- `irodori.seconds` を明示すると**チャンク分割が黙って無効**になり、**出力長が指定秒ちょうどになる**
  （14 字を 12 秒に間延びさせる・警告なし＝`research/lab/notes/40` §4）。本体は原則送らない。
- `caption` は**上流では空文字・空白のみが 400**。**配布版は空文字・空白のみを「未指定」として
  欄ごと畳む**（＝200・既定に戻る）。`/params` が `caption` の既定を `""` と名乗る以上、
  その値を送り返して 400 になってはならないからである（設計書 §4-2 ⑵）。`seed: ""` も同じ扱い
  （＝毎回の乱数）。現行アダプタが踏んで避けている型＝`research/lab/notes/37` §4-4。
- **`null` は「未指定」として扱ってよい**。上流は 44 欄のうち 3 欄
  （`ref_normalize_db`・`max_ref_seconds`・`first_sentence_chunk_min_chars`）だけを
  `_explicit_option`（`app.py:1089-1095`＝`model_fields_set` を見る）で読むので、
  **明示 `null` が env 既定に勝って `None` を運ぶ**（＝参照のラウドネス正規化が黙って切れて音が変わる）。
  配布版はこの 3 欄の明示 `null` を**欄ごと削って**から上流に渡す＝`/params.rules.null_means_unset`
  が 44 欄すべてで真になる（削る欄は `rules.null_normalized_by_wrapper` に載る）。

### 3-2 検査（配布版が足す＝上流には無い）

| # | 検査 | 結果 |
|---|---|---|
| 1 | 未知欄（トップレベル・`irodori` ネストの両方） | **400** `ywk_unknown_field` |
| 2 | 範囲外（`/params` の min/max と同じ表を使う） | **400** `ywk_out_of_range` |
| 3 | Literal 3 欄がトップレベルにある | **400**（ネストで送れ） |
| 4 | `voice` と `irodori.no_ref` の同時指定 | **400** `ywk_voice_and_no_ref`（⑷ の排他） |
| 4b | `voice` と参照 5 欄（`ref_wav`・`ref_wavs`・`ref_latent`・`ref_latents`・`ref_embed`）の同時指定 | **400** `ywk_voice_and_reference`（⑷ の排他・上流は voice を黙って捨てる） |
| 5 | `response_format` が `wav` 以外 | **400** `ywk_unsupported_response_format`（ffmpeg を同梱しない＝第三者バイナリ不同梱） |

**上流は⑴〜⑶を黙って通す**（`extra="allow"`・範囲検査ゼロ・`num_steps:-5` も `cfg_scale_text:999`
も 200＝`research/lab/notes/37` S-1／S-5／S-6）。**綴り違いが「無音の別の音」になる沈黙**は、
この 5 検査で塞がる。

### 3-3 エラー 5 態と body の形

態＝**400／422／500／503／SSE の `event: error`**（`research/lab/notes/14` T-8）。
配布版はどの態でも次の形で返し、**機械可読な `code` を必ず入れる**（上流は `param`・`code` とも
常に `null`＝`app.py:1156-1167`）。

```json
{"error": {"message": "unknown field: num_step",
           "type": "invalid_request_error",
           "param": "num_step",
           "code": "ywk_unknown_field"}}
```

- **`code` は上流由来のエラーにも被せる**。上流の `openai_error_response`（`app.py:1156-1167`）は
  `"code": None` を固定で書き、Starlette の 404/405 は `{"detail": …}` のまま出るので、配布版の
  応答 middleware が形と `code` を揃える。本体が見る主な値＝

  | 態 | `code` |
  |---|---|
  | 未知の話者（`Unknown voice=`） | `ywk_unknown_voice` ← **D-5 はこれで文言依存を捨てられる** |
  | `voice` 省略（`No voice was provided`）＝**ランチャが `IRODORI_DEFAULT_VOICE` を空にした場合のみ**（3-1） | `ywk_missing_voice` |
  | `model` 違い | `ywk_unknown_model` |
  | `input` が空白のみ | `ywk_empty_input` |
  | 読込中・読込失敗（503） | `ywk_runtime_unavailable` |
  | 配布版の前段検査（3-2） | `ywk_unknown_field`・`ywk_out_of_range`・`ywk_type_error`・`ywk_invalid_enum`・`ywk_literal_top_level`・`ywk_voice_and_no_ref`・`ywk_voice_and_reference`・`ywk_unsupported_response_format`・`ywk_invalid_body`・`ywk_validation_error` |
  | 上記以外の 4xx／5xx | `ywk_upstream_error`／`ywk_server_error`（404 は `ywk_not_found`・405 は `ywk_method_not_allowed`） |

- **エラー body に絶対パスを 1 件も出さない**。上流は⒜未知 voice の 400 で `voices` ディレクトリの
  絶対パスを漏らし（`app.py:457-459`）、⒝ 422 で**サーバの絶対パスと行番号**を漏らし、さらに
  **本文 5,000 字を丸ごと echo する**（実射＝`research/lab/notes/37` §2-1）。配布版は応答 middleware で
  絶対パスを `<path>` に置換し、`RequestValidationError` を欄名と理由だけに畳む。
- **本体は `message` を生のまま利用者に出さない**。理由 1 行への畳み込みは `code` で行う
  （文言依存の判定＝本体の `IsMissingVoiceFailure` を `code` に移せる＝⑼ の D-5）。
- **422 の本文をログに流さない**（1 発 5 KB）。

### 3-4 動きの性質（本体の期限・並列設計に効く）

- **同時要求は Lock で直列**（`max_concurrent_synthesis` 既定 1・`inference_runtime.py:620`）
  ＝1 プロセス 1 合成。配信中に UI で試し撃ちすると読み上げが待たされる。
- **キャンセルは効かない**（`asyncio.shield` は SSE だけ＝`app.py:781`）。中止の粒度は**チャンク**が上限
  ＝「止める」は次のチャンクを投げないこと。
- **参照ありは毎要求・毎チャンク再符号化**（実射で 4 チャンクとも同じ `ref_wav` が届く＝
  `research/lab/notes/37` §5-1）。
- **`seed` を明示しないと `X-Irodori-Seed` は 1 本目のチャンクの値しか返らない**
  （`research/lab/notes/14` T-4）＝先読みキャッシュを効かせるなら
  `irodori.chunking_enabled:false` か `irodori.seed` の明示が前提。
- **参照を当てると予測尺が変わる**（94.3→82.4 フレームの実測）＝同じ本文でも出力長が変わる。

### 3-5 SSE（`stream_format:"sse"`）＝**受けるが本体は使わない**

**上流の機能は殺さない**（`decisions.md` 47）＝配布版は枠をそのまま通す。**本体は使わない**
（1 発で wav を受け取る 3-1 の経路が正）。以下は上流 `app.py` の実装から写した**枠の形**であり、
欄名は推測ではなく実名である。

- **入り口**＝`stream_format` は `"sse"` だけ。他の値は **400**（`stream_format must be 'sse' when
  specified.`＝`app.py:396-405`）。欄を出さない／`null` は非ストリーム（1 本の wav）。
- **応答**＝**200**・`Content-Type: text/event-stream`・`Cache-Control: no-cache`・
  `X-Accel-Buffering: no`（`app.py:770-774`）。非ストリームで載る `X-Irodori-Seed`・
  `X-Irodori-Total-To-Decode`・`X-Irodori-Messages` は**載らない**（seed はチャンクの枠に入る）。
- **1 枠の形**＝`event: <名>\n` `data: <JSON 1 行>\n` の 2 行のあとに空行（\n\n で終わる）（`_sse_event`＝`app.py:787-789`・
  `json.dumps(ensure_ascii=False, separators=(",", ":"))`）。枠は 3 種：

  | `event:` | `data:` の欄（**上流の実名**） |
  |---|---|
  | `audio_chunk` | `index`（0 起点）・`text`（そのチャンクの本文）・`format`（`wav`）・`media_type`（`audio/wav`）・`audio_base64`・`seed`・`total_to_decode`（`app.py:734-744`） |
  | `done` | `chunks`（送り終えたチャンク数・`app.py:769`） |
  | `error` | `error.message`・`error.type`・`error.param`・`error.code`（`_sse_error_event`＝`app.py:792-810`＝非ストリームの 3-3 と同じ 4 欄） |

- **`audio_chunk` はチャンクごとに完結した wav**（`encode_audio` をチャンク単位で呼ぶ＝
  `app.py:719-724`）＝`audio_base64` を復号して**そのまま鳴らせる**が、**連結しても 1 本の wav には
  ならない**（RIFF ヘッダが各チャンクに付く）。
- **`error` 枠は 200 の中に乗る**＝HTTP の状態番号では失敗を判別できない。判別は `data.error.code`
  で行う。上流が出す値＝`runtime_unavailable`（読込待ちの時間切れ）・`invalid_request`
  （`FileNotFoundError`／`ValueError`）・`stream_error`（`RuntimeError`）・`synthesis_queue_timeout`
  （503 相当）＝`app.py:745-762`・`812-815`。**この枠の `code` だけは `ywk_` 接頭辞にならない**
  （3-3 の middleware は 4xx/5xx の body を書き換えるもので、200 の中身には触らない）。
- **`error` 枠が出たらそこで終わり**＝上流は `return` する（`done` は来ない）。
- **`error` 枠にも絶対パスを出さない**＝配布版は SSE の本体を 1 枠ずつ見て、`event: error` の枠だけ
  絶対パスを `<path>` に置換する（`audio_base64` を含む枠には触らない＝base64 を壊さない）。
  上流の枠は非ストリームと同じ `str(exc)` を載せるので、これが無いと 3-3 の「絶対パス 0 件」が
  ストリーム経路だけ破れる。
- **取消は効かない**＝`_run_stream_blocking` が `asyncio.shield` で包む（`app.py:776-784`）。
  接続を切っても**走っているチャンクの合成は最後まで走る**。止まる粒度は「次のチャンクを投げない」
  ところまで（3-4 と同じ）。

題目＝`未知欄は400で拒む`／`Literal3欄はトップレベルにあれば400`／`範囲外は400`／
`voiceとno_refの同時指定は400`／`voiceと参照5欄の同時指定は400`／`エラーbodyに絶対パスが0件`／
`上流由来の400にもcodeが載る`／`response_formatはwavのみ`／`3欄の明示nullは未指定として畳まれる`／`SSEのerror枠に絶対パスが0件`／`stream_formatはsse以外400`。

---

## ⑷ 話者

**一覧の形は上流互換。「デフォルト」が先頭に常在し、上流由来の `none` は出ない。
話者 id は日本語でよい。`voices/` の所有者は配布版で、本体は名前で選ぶだけ。**

### 4-1 `GET /v1/audio/voices`（配布版が覆う）

```json
{"object": "list",
 "data": [{"id": "デフォルト", "object": "voice", "display_name": "デフォルト",
           "preset": false, "no_ref": true},
          {"id": "琴葉茜", "object": "voice", "display_name": "琴葉茜（関西弁）",
           "preset": true, "no_ref": false}]}
```

- 形は上流互換（`{"object":"list","data":[…]}`）だが、**パス欄（`ref_wav`・`ref_wavs`・`ref_latent`・
  `ref_latents`・`ref_embed`）を落とし**、`display_name`・`preset`・`no_ref` を足す。
  上流は `ref_wav` に**利用者の絶対パスをそのまま返す**（実射＝`research/lab/notes/37` §3-1）ので、
  配布版はこの route を差し替える。
- **「デフォルト」が必ず 1 件・先頭**。上流の並びは `sorted()`＝コードポイント順で日本語は
  五十音にならない（`voices.py:70`）ので、配布版が並べ替える。
  **`voices.json` が無くても・壊れていても 1 件は必ず出る**＝配布版が注入する
  （`voices.json` を書くのはランチャ＝便 D で、初回起動の時点ではまだ無い）。
- **`voices.json` が壊れていても 500 を返さない**＝「デフォルト」1 件だけの 200 を返し、
  理由を `GET /ywk/voices` の `error` と `GET /ywk/status` の `voices.error` に載せる
  （上流は `ValueError` を絶対パスつきで投げる＝`voices.py:196-201`）。**D-8 はこれで消える**（⑼）。
- **上流由来の `none` は出ない**＝`IRODORI_ALLOW_NO_REF_VOICE=false` を焼く
  （`research/lab/notes/38` 実射 13b）。同じ意味の話者が 2 つ見える状態を作らない。
  ただし**送るぶんには通る**＝上流の別名 5 つは「デフォルト」に正規化される（⑶ 3-1・⑼ D-4）。
- **話者 id に日本語・空白・記号が使える**（走査と合成は voice_id を一切検査しない＝`voices.py:168-186`）。
  上流の登録 API は `^[A-Za-z0-9_-]+$`（`voices.py:24,151-155`）で日本語を 400 にするが、
  **本体はこれを使わない**（⑼ の D-3）＝配布版は**書き込みの 3 口
  （`POST /v1/audio/voices`・`PUT`／`DELETE /v1/audio/voices/{id}`）を外している**（4-3）。

### 4-2 「デフォルト」＝参照なし

- `voice: "デフォルト"` を**そのまま送って 200**。配布版が `voices.json` に
  `{"デフォルト":{"no_ref":true}}` を持つ（`decisions.md` 16）。
  上流の素の Server に `"デフォルト"` を送ると **404 ではなく 400**（`app.py:457-459`）＝
  配布版でだけ通る。
- **`voice` と参照 6 欄を同時に送ってはいけない**。上流は
  `ref_wav`／`ref_wavs`／`ref_latent`／`ref_latents`／`ref_embed`／`no_ref` のどれか 1 つでも明示されて
  いれば話者解決に入らず（`app.py:437-454`）、**参照が黙って落ちる**（実射＝`voice:"zunda"` +
  `no_ref:true` が 200 で `ref_wav=null`）。配布版はこの同時指定を **400** で弾く
  （`no_ref` は `ywk_voice_and_no_ref`・残り 5 欄は `ywk_voice_and_reference`）。
  **`null` を入れた欄は「明示」ではない**＝上流も読まないので 400 にしない。
- 参照なしの声は **Irodori 自身の素の声**（caption のみで作る）。

### 4-3 所有者

- **`voices/` と `voices.json` を書くのは配布版だけ**。本体は書かない・消さない・
  wav のパスを持たない。本体がやるのは「一覧から名前で選ぶ」ことだけ。
- **上流の書き込み 3 口は口ごと外してある**＝`POST /v1/audio/voices`・
  `PUT /v1/audio/voices/{id}`・`DELETE /v1/audio/voices/{id}` は 404／405 を返す。
  理由＝本体も配布版 UI も使わない口であり（D-3）、api_key を持たない設計
  （`decisions.md` 31）では**登録口が開いていること自体が利用者機に残る唯一の書き込み面**
  だから（multipart/form-data は CORS の simple request＝preflight なしで届く）。
  読みの `GET /v1/audio/voices/{id}`（檔名・大きさ・更新時刻だけ）は残す。
- 話者メタ（表示名・caption 既定・既定パラメータ）は**上流に置き場が無い**
  （`VoiceSpec` はパス 5 欄＋`no_ref` の 7 欄だけ・余分な鍵は無警告で捨てられる実射＝
  `research/lab/notes/37` §3-4）＝配布版の独自台帳（`voices/voices.ywk.json`）が持つ。
- **プリセット話者 12 名**をリリース版に同梱する予定（`decisions.md` 17・生成は便 P）。
  一覧では `preset: true` で区別できる。

題目＝`一覧の先頭にデフォルトが常在する`／`voicesjsonが無くてもデフォルトで合成200`／
`voicesjsonが壊れても500ではなく1件と理由`／`一覧にnoneが0件`／`noneの別名は正規化されて200`／
`一覧の応答に絶対パスが0件`／`日本語話者名で合成200`／`上流の書き込み3口が外れている`。

---

## ⑸ パラメータ（`GET /params`）

**上流の `/openapi.json` は既定 0 欄・範囲 0 欄・説明 0 欄で使えない。配布版が `/params` を建て、
本体はこれ 1 本を読んで `ParamDescriptor` を組む。**

- 上流の openapi には `IrodoriOptions` 44 欄が載るが、**`default` 0・`minimum`/`maximum` 0・
  `description` 0**（実射・機械集計＝`research/lab/notes/37` §1-2）。範囲があるのは
  `SpeechRequest.speed`（0.25〜4.0）と `input`（1〜4096）だけ。
- **既定値は 3 層**（dataclass 43 欄／`IRODORI_DEFAULT_*` 22 欄／要求本文）で、**env で動く**
  （`config.py:47-72`）。だから「本体に固定表を持つ」は運用で嘘になる。
  **`/params` が返す `default` は、起動中のプロセスの実効値**（env 反映後）である。
- **モデル未読込でも 200・≤ 100 ms**。checkpoint 依存の値（`max_text_len`・`max_caption_len`・
  `ref_max_seconds`）は**読込前は `null`** で、`model_loaded: false` が立つ。

### 5-1 応答の形

> **抜粋である**（設計書 §4-2 の逐語）。`…` は省略の印であって、実際の応答には現れない。
> 末尾の `planned` の `（§6）` は設計書の節番号を指す＝**本書では⑺**である。

```json
{"schema": 1, "engine": "irodori-ywk", "model_loaded": false,
 "checkpoint": {"hf": "Aratako/Irodori-TTS-v4.1-Small", "max_text_len": null, "max_caption_len": null, "ref_max_seconds": null},
 "request": {"input": {"type":"string","max_length":4096}, "speed": {"type":"number","default":1.0,"min":0.25,"max":4.0,"step":0.05},
             "voice": {"type":"string","default":"デフォルト","default_source":"ywk","required":false,"nullable":false,"description":"話者名（/ywk/voices の id）。「デフォルト」= 参照なし","note":"voice を省くと IRODORI_DEFAULT_VOICE が使われる…（decisions.md 45）"},
             "response_format": {"type":"enum","default":"wav","enum":["wav"]}},
 "irodori": [
   {"key":"caption","type":"string","default":"","nullable":true,"exposed_to_ywk":true,"group":"emotion","label":"演技指示（キャプション）","description":"…","max_length":2048,"note":"空文字・空白のみ＝未指定（wrapper が欄ごと畳む）"},
   {"key":"num_steps","type":"integer","default":40,"min":1,"max":120,"step":1,"nullable":false,"exposed_to_ywk":true,"presets":[10,40],"group":"quality","label":"サンプリング歩数","range_source":"上流 gradio_app.py のスライダ（UI の範囲であって検査ではない）（:481・minimum=1 maximum=120 step=1）"},
   {"key":"cfg_scale_text","type":"number","default":3.0,"min":1.0,"max":10.0,"step":0.1,"group":"emotion", …},
   {"key":"t_schedule_mode","type":"enum","default":"…","enum":["linear","sway"], …},
   …（`IrodoriOptions` 44 欄を全部載せる。既定は **env 反映後の実効値**＝`settings.default_*` があればそれ、無ければ `SamplingRequest` の dataclass 既定。`cfg_scale_caption` は Server 実効値 3.0 を採り、gradio 4.0 は `note` に残す。`speaker_kv_min_t` は「`speaker_kv_scale` 指定時のみ 0.9 に解決」を `note` に書く）
 ],
 "rules": {"priority": "irodori.X > top-level X > env", "literal_must_be_nested": ["t_schedule_mode","decode_mode","cfg_guidance_mode"],
           "voice_and_no_ref_exclusive": true, "voice_and_reference_exclusive": ["ref_wav","ref_wavs","ref_latent","ref_latents","ref_embed"],
           "unknown_field": "400", "out_of_range": "400", "null_means_unset": true,
           "null_normalized_by_wrapper": ["ref_normalize_db","max_ref_seconds","first_sentence_chunk_min_chars"],
           "empty_string_means_unset": ["caption","seed"],
           "no_ref_voice_aliases": ["no-ref","no_ref","none","null","text-only"], "default_voice": "デフォルト",
           "exposed_to_ywk": ["caption","cfg_scale_caption","cfg_scale_speaker","cfg_scale_text","num_steps","seed","speed","sway_coeff","t_schedule_mode","voice"]},
 "prefetch": {"available": false, "planned": "/ywk/prefetch（§6）"}}
```

### 5-2 本体の `ParamDescriptor` への写像

| `/params` の `type` | 本体の `ParamKind` | 使う欄 |
|---|---|---|
| `number` | **Number** | `min`・`max`・`step`・`default`（`Core/Models/Capabilities.cs:47-62` の Number 用任意欄がそのまま埋まる） |
| `integer` | **Number** | 同上（`step: 1`） |
| `enum` | **Choice** | `enum`（選択肢）・`default` |
| `boolean` | **Flag** | `default` |
| `string` | **Text** | `caption`・`seed` など。`max_length` があれば入力長の上限に |

- **本体が写すのは `exposed_to_ywk: true` の欄だけ**（9 欄＋`voice`・`speed`）。
  44 欄を全部 UI に出すのではない＝`ref_wav`・`ref_latent`・`ref_embed`・`lora_adapter` のような
  パス欄や `duration_scale`（`speed` と除算合成される）は `exposed_to_ywk: false` で、
  配布版の UI（上級者向け `advanced` 群）だけが扱う。
- **`exposed_to_ywk: true` の欄に `default: null` は 0 件**（「既定が決まっていない」を返さない）。
  `caption`・`seed` のように「未指定」が意味を持つ欄は `default: ""`＋`nullable: true` で表す
  （`""` を送り返しても 400 にならない＝⑶ 3-1）。
- **`nullable` は全 44 欄に載る**＝`true` なら「欄を出さない・`null` を送る＝上流の既定」。
- **範囲の出所を明示する**＝`range_source` に「上流に範囲検査は無い／gradio スライダ由来／配布版の推奨」の
  別を書く。上流が保証していない数字を、出所を伏せたまま本体の UI に名乗らせない。
- 型・enum は上流 openapi と起動時に突合し、差があれば stderr に 1 行出す（上流追随の腐り検知）。

### 5-3 既知の落とし穴（本体の表示に効く）

- **`cfg_scale_caption` は env を持たず、`default_cfg_scale_text`（3.0）を流用する**
  （`app.py:932-938`）＝`IRODORI_DEFAULT_CFG_SCALE_TEXT` を動かすと caption 側の既定も一緒に動く。
  gradio の初期値は 4.0（`gradio_app_voicedesign.py:538-544`）＝**UI と API で既定が違う**。
  `/params` は Server の実効値 3.0 を採り、gradio の 4.0 は `note` に残す。
- **`speaker_kv_min_t`** は `speaker_kv_scale` を指定したときだけ 0.9 に解決される＝`note` に書く。
- **`speaker_uncond_mode`** は HTTP から触れない（`ref_embed` のときだけ効く）。
- `max_text_len`・`max_caption_len` は checkpoint のメタデータ由来（`docs/parameters.md:49-50`）＝
  **モデルを読まないと分からない**。固定表に書くと必ず嘘になる。

題目＝`paramsはモデル未読込でも200`／`exposedtoywkの欄にdefaultnullが0件`／
`nullableが全44欄に載る`／`caption と seed の空文字既定が往復する`／`paramsは44欄を全部載せる`。

---

## ⑹ 実 device の可視化（`GET /ywk/status`）

**「いま何で動いているか」は上流からは取れない。配布版が実 device を実測して返す。**

> **設計書 §4-3 の逐語**。`"cuda:0"|null` の縦棒は**その欄が null を取りうる**ことを示す型の記法で
> あって、応答本文にこの文字が入るわけではない。`…` は省略の印である。

```json
{"engine":"irodori-ywk","version":"<配布版の版>","upstream":{"irodori_tts":"8224daf","server":"841fb7c"},"host":"127.0.0.1","port":18088,"runtime":{"loaded":bool,"loading":bool,"error":str|null},"device":{"configured":"cuda:0","actual":"cuda:0"|null,"name":"NVIDIA GeForce RTX 3090"|null,"uuid":"…"|null,"pci_bus_id":…,"precision":"bf16"},"torch":{"version":"2.10.0+cu130","cuda":"13.0","hip":null},"voices":{"count":13,"dir":"<絶対パスは出さない・末尾 1 段のみ>"},"warmup":{"state":"idle|running|done","shots":0}}
```

- **`device.actual` はモデル読込後に実測した値**（`next(model.parameters()).device`・
  `torch.cuda.get_device_properties(idx).uuid`）＝**設定値の echo ではない**。**未読込なら `null`**。
  上流の `/health.model.model_device` は素の echo で、`auto` は `auto` のまま返る
  （`app.py:173`・`research/lab/notes/14` §7）。
- **GPU の同定は UUID（と PCI bus id）で行う**。index は再起動で変わりうる
  （`CUDA_DEVICE_ORDER` の既定は `FASTEST_FIRST`・nvidia-smi 自身が再起動間の一貫性を保証しないと明記）。
  torch は CUDA でも AMD でも `uuid` と `pci_bus_id` を持つ。
- **精度は device と連動**＝GPU→`bf16`・CPU→`fp32`（`decisions.md` 7）。
  上流は **CPU×bf16 を起動時に `ValueError` で弾き**（`inference_runtime.py:306-310`）、
  **fp32×GPU 不在は黙って CPU に落ちて 200 を返す**（2 文字・10 steps で 267 秒＝340 倍の実測）。
  配布版は `cuda` 指定で `torch.cuda.is_available()` が偽なら**起動前に理由 1 行で exit 2** にする。
- **絶対パスは出さない**（`voices.dir` は末尾 1 段だけ）。
- 上の逐語に**足した欄が 1 つある**＝`voices.error`（`string|null`）。`voices.json` が読めないときに
  理由を 1 行で載せる（⑷ の「0 件＋理由」の相方）。**欄を足すのは `schema` を上げない**（⑻）。
- **本体は device を要求 JSON に載せない**。device は Server プロセスの env
  （`IRODORI_MODEL_DEVICE`／`IRODORI_CODEC_DEVICE`）だけで決まる＝1 プロセス 1 デバイス。
  ランチャで選んだ GPU が**暗黙に**使われる（`decisions.md` 15・⑼ の D-7）。
  `/ywk/status` は**表示と記録のため**の口であって、切り替えの口ではない。

題目＝`ywkstatusのdeviceactualは実測値でモデル未読込ならnull`／`ywkstatusに絶対パスが0件`。

---

## ⑺ 先読み（**予約**＝便 A では `available: false`）

**契約の形だけ先に決めておく。実装は後続便で、本体スケジューラの対応も後続。**
（`decisions.md` 14＝「深い先読み機構は余力があれば・使いやすい形で契約に準備する」）

| 口 | 形 | 応答 |
|---|---|---|
| `POST /ywk/prefetch` | **合成と同じ body**＋`client_key`（本体の先読みキー＝要求 JSON そのもの） | `{"id":"…","state":"queued"}` |
| `GET /ywk/prefetch/{id}` | — | `{"id":"…","state":"queued\|running\|done\|failed","audio_url":"…"}`（`done` のときだけ `audio_url`） |
| `DELETE /ywk/prefetch/{id}` | — | 取消 |

- **キー＝本体が渡す `client_key`**（本体の先読みキーは要求 JSON そのもの＝本体 §9 ④）。
  音量は要求に構造的に不在なので鍵の変数にならない。
- **優先度は生の合成要求より下**。合成は 1 プロセス 1 本の直列なので、先読みは待ち行列を作るだけ。
- **TTL 10 分**。
- **`seed` 未固定の先読みは「先に引いた 1 抽選をそのまま再生する」意味**になる
  （`X-Irodori-Seed` は 1 本目のチャンクしか返さない＝`research/lab/notes/14` T-4）。
- **便 A の時点では `/params.prefetch.available` が `false`**＝本体はこの口を叩かない。
  `true` に変わるのは⑻の手順を踏んだときだけ。

題目＝`paramsのprefetchavailableは便Aではfalse`。

---

## ⑻ 版と互換

- **`schema` 番号**＝`/params` と `/ywk/status` の応答に載る。**欄を消す・意味を変えるときだけ**上げる。
  欄を足すのは上げない（本体は知らない欄を無視する）。
- **上流の pin**＝`/ywk/status.upstream` に `irodori_tts`・`server` の短い commit を載せる。
  上流を上げるときは、`/params` と上流 openapi の突合ログ（⑸）に差分が出るので、それを見て本書を直す。
- **配布版の版**＝`/ywk/status.version`。
- **変更時の告知**＝本書を直し、契約テストの題目を書き換え、本体側（便 F）に知らせる。
  **本書と実装が食い違ったら実装が正で本書が誤り**ではなく、**契約テストが正**である
  （テストが緑でない変更は入れない）。

---

## ⑼ 本体アダプタ側の差分 D-1〜D-9

> `research/report/irodori-native-handoff-2026-09-04.md` §5 の表から**原文で写した**（原文＝調査便の
> 断定であり、本書の推奨ではない）。**18088 にする以上、D-9 は必ず要る。**

| # | 現行 | 事実 | 効き |
|---|---|---|---|
| D-1 | 擬似キャラ 1 件固定（裁定 4／10） | ⑦で話者が N 件に | `CharacterCapability` の複数件は他 8 エンジンの器がある。Core 不触で済むかは未確定＝変わるのは Irodori の発見段（doc `IrodoriCapabilityBuilder.cs:5-18` は反証される） |
| D-2 | 発見は `/health` 1 段 | ④で `GET /params`（配布版）が加わる | `EngineCapability` は非永続＝保存の形は増えない |
| D-3 | 話者登録は `POST /v1/audio/voices`＋`ywk-<sha12>` | 日本語の名付けは同じ経路で 400 | 配布版が登録口の所有者になる根拠。**配布版は書き込み 3 口を外した**＝本体が叩けば 404／405（⑷ 4-3） |
| D-4 | 参照なし＝`voice:"none"` | `allow_no_ref_voice=false` で全滅（実射） | `no_ref:true` か alias「デフォルト」なら落ちない（推奨）。事実＝`voice` と `no_ref` の同時送信は参照が黙って落ちる。**配布版では `voice:"none"` も 200**＝別名 5 つを「デフォルト」に正規化する（⑶ 3-1・⑷ 4-1）＝本体は改修前でも合成できる |
| D-5 | `IsMissingVoiceFailure` は文言依存 | 日本語話者名の 400（`voice_id must contain only ASCII…`）を誤検知して 1 回無駄に再登録 | 経路を変えれば消える。**配布版は `error.code` を必ず載せる**（⑶ 3-3）＝未知話者は `ywk_unknown_voice`・`voice` 省略は `ywk_missing_voice` で判定できる |
| D-6 | 話者メタ（wav パス）は本体プロファイル側 | 持ち主が配布版へ移る | 話者の所有者の裁定次第で `VoiceParamId` の Text 欄が Choice の話者選択に変わる |
| D-7 | GPU 欄なし・精度 2 env のみ | 配布版が GPU を管掌すれば差分ゼロ（元栓の env 注入も不要） | 報告 §4-2・§10-2 と整合 |
| D-8 | `/health` 200 だけで発見 | `voices.json` 破損（一覧だけ 500）を検知できない | 発見段のエラー処理に 1 態増える |
| D-9 | 接続先は `IrodoriConstants.cs:35` の固定値（8088）で `App.xaml.cs:131` は baseUrl を渡していない | 決定 16 で 8088 以外にするなら本体 1 件の改修（`36` C-4 は 8088 既定を互換の下限に置く） | G-2「本体無改造」が成り立つのは 8088 のときだけ |

**本書の読み替え**（原文の記号 ④⑦・決定 16 は調査便のもの）：④＝`/params`（⑸）、⑦＝話者 N 件（⑷）、
決定 16＝`decisions.md` 2（**18088 に確定**）。**D-8 は配布版で消える**＝配布版の
`GET /v1/audio/voices` は台帳が壊れても 500 を返さず「0 件＋理由」を返す（⑷）。

---

## 付録 出典

- 設計＝`docs/design/ben-a-skeleton-and-build.md` §4・§6。
- 引き継ぎ＝`research/report/irodori-native-handoff-2026-09-04.md`（§1-4・§1-6〜§1-8・§4・§5）。
- 実射＝`research/lab/notes/14-server-contract.md`（T-1〜T-12）・`37-handoff-contract.md`（§1〜§5）・
  `38-handoff-decisions.md`（話者名 17 ケース）・`30-embed-and-gpu-control-facts.md`（env と `._pth`）・
  `40-rocm-warmup.md`（Radeon の暖機）。台本＝`research/lab/bin/37_contract.py`・`38_naming.py`・
  `server_contract.py`。
- 上流の檔:行は pin（`8224daf`／`841fb7c`）における実測。上流を上げたら**行番号ごと確認し直す**。
