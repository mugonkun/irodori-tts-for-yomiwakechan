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
- 常駐と後始末は**配布版のランチャ**（`decisions.md` 6）。**本体の一括起動は引数なしで `IrodoriTtsYwk.Launcher.exe` を起こしてよい**（`decisions.md` 103＝従来型 TTS と同じ扱い・`LaunchKind.ExePath`）。本体はサーバのプロセスを持たない。見つからなければ理由つき失敗で、
  他エンジンの読み上げは無傷（本体 §0-3 の掟 1・掟 2）。

題目＝`接続先は127_0_0_1の18088でありlocalhost名は使わない`／`Authorizationヘッダを付けない`。

---

## ⑵ 発見

**`GET /health` 200 → `GET /ywk/status` → `GET /params` → `GET /v1/audio/voices`（表示名と理由が要るなら `GET /ywk/voices`）の 4 段。
タイムアウト 5 秒・ready 待ち 120 秒・合成の可否は `runtime.loaded` で判る。**

> **ポート先行（裁定 105）＝`/health` 200 は「起きている」であって「使える」ではない。**
> 配布版は `preload=false` を焼き、**bind してから裏でモデルを載せる**（`ywk_lifespan` が
> 読込の糸を 1 本起こす）。⇒ **`/health` 200 は wrapper の起動から 3〜4 秒で返り**（torch の import の後に bind＝実測 3.3〜3.6 s。ランチャの exe から数えると**約 9 s**＝起こす前の門〔torch の検査・GPU の列挙〕が 5〜6 s・実測 8.9 s＝`decisions.md` 106）、
> `runtime.loaded=true` はその 20〜70 秒後に来る（Radeon 実測 34.5 s・3090 20〜28 s）。
> 4 段の発見は `/health` 200 の直後から全部通るが、**合成を撃ってよいのは
> `runtime.loaded=true` を見てから**である（撃っても待たされるだけで 200 は返る＝下の註）。

| 段 | 口 | 何を得るか | 失敗時 |
|---|---|---|---|
| 1 | `GET /health` | 生きているか。**モデル未読込でも 200**（裁定 105 のポート先行では**これが正常形**＝wrapper の起動から 3〜4 秒で 200・exe からは約 9 s・`runtime.loaded=false`） | 到達不能＝エンジン未起動として理由つき失敗 |
| 2 | `GET /ywk/status` | 配布版か否か・版・上流 pin・**実 device**・話者件数（⑹） | 404 なら**上流の素の Server**（＝8088 経路の個体）＝配布版として扱わない |
| 3 | `GET /params` | パラメータ一覧（⑸）。**モデル未読込でも 200・≤ 100 ms** | 404 なら同上 |
| 4 | `GET /v1/audio/voices`（表示名と理由が要るなら **`GET /ywk/voices` でもよい**＝本体はこちらを叩く・裁定 104） | 話者一覧（⑷） | 500 は「台帳が壊れている」＝配布版では**返さない**（⑷ の約束） |

- **`/health` の body は上流のままで、解釈しない**（欄は上流のまま）。ただし配布版は
  **`/health` の絶対パスも `<path>` に畳む**＝上流はここに `settings.voices_dir` と
  HF スナップショットの実パス（＝OS の利用者名）をそのまま載せる（`app.py:167-193`）ので、
  200 でも本体・ランチャのログに実パスが乗らないようにしてある。`model.model_device` は
  `settings.model_device` の**素の echo**（`app.py:173`）＝`auto` は `auto` のまま返り、
  **実際に載った device は `/health` からは分からない**（`research/lab/notes/14` §7）。
  実 device を知りたいときは **`/ywk/status`**（⑹）を見る。
- **`/health.voices.files` は話者数ではなく音声拡張子の檔数**（`app.py:191-195`）＝
  ここから話者数を数えると外れる。話者数は `/v1/audio/voices` か `/ywk/status.voices.count`。
- **ready 判定＝`runtime.loaded`**（`research/lab/notes/14` T-9）。**`preload=false` を焼き、
  bind の後に裏の糸が載せる**（裁定 105＝裁定 7 の `preload=true` を覆した）ので、
  **bind 直後（wrapper の起動から 3〜4 秒）から `loaded=false`・`loading=true`・`error=null` がずっと返る**のが
  正常形である（1 巡目は「preload=true なのでこの窓はほぼ観測されない」と書いていた＝逆になった）。
  **待ちは 120 秒**（CPU の埋め込み Python で読込 28 秒の実測・30 秒では足りない＝本体の
  `LaunchCompletionTimeout=120 s` は妥当）。
- **読込に失敗した個体は死なずに理由を答える**（裁定 105 ⑴）＝`runtime` は
  `{loaded:false, loading:false, error:"<理由 1 行>"}` になり、**プロセスは生き続け・`/health` も
  `/ywk/status` も 200 のまま**である。`error` の 1 行に絶対パスは出ない（⑹ と同じ規則で `<path>` に畳む）
  ＝**裏読込の路も遅延読込の路も同じ整形を通る**。
- **`error` は「載らなかった」以外を意味しない**（裁定 105 ⑵・便 G の是正）。出る三つ組は次の 3 つだけ：
  読込中 `{loaded:false, loading:true, error:null}`・成功 `{loaded:true, loading:false, error:null}`・
  失敗 `{loaded:false, loading:false, error:"<理由 1 行>"}`。
  **載っている個体の合成が 5xx で落ちても `error` には出ない**（CUDA OOM・上流の「待ち行列が一杯」503）＝
  それを載せると、受け取る側は 1 回の合成の失敗で健全な個体を永久に除外してしまう。
  失敗の後に上流が読込を遣り直している最中は `loading=true`＝そのときも `error` は `null` に畳む。
  ⇒ **本体の扱い**＝スキャンは `runtime.loaded` を 2 秒間隔で最大 120 秒見て、
  **`error` が立つか時間切れになったら理由つきで除外**する（元栓＝一括起動の完了検知は
  `/health` 200 の疎通だけで済ませてよい＝裁定 105 ⑸）。
  ⇒ **ランチャの扱い**＝`loaded=false` かつ `error` が非 null の標本で状態帯を「失敗」に落とし、
  同じ理由 1 行を出す（`docs/design/ben-d-launcher.md` §25）。
- **`loaded=false` の間の合成は上流の遅延読込に合流して待つ**（裁定 105 ⑶）＝裏の糸と
  同じ `runtime_manager.get()` を、同じ鍵の下で待つので**二重に載ることはない**。
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
  | 話者 id に許されない文字（上流の登録 API の検査に被せる札＝配布版は登録の 3 口を外しているので本体には届かないはず。網羅表に載せるのは `default` 分岐に落とさないため・裁定 104） | `ywk_invalid_voice_id` |
  | 読込中・読込失敗（503） | `ywk_runtime_unavailable` |
  | 配布版の前段検査（3-2） | `ywk_unknown_field`・`ywk_out_of_range`・`ywk_type_error`・`ywk_invalid_enum`・`ywk_literal_top_level`・`ywk_voice_and_no_ref`・`ywk_voice_and_reference`・`ywk_unsupported_response_format`・`ywk_invalid_body`・`ywk_validation_error` |
  | 暖機の口（⑺ 7-2・**本体は叩かない**） | `ywk_warmup_running`（409）・`ywk_warmup_unknown_id`（404） |
  | 事前計算の口（⑺ 7-3・**本体は叩かない**） | `ywk_precompute_running`（409・`DELETE /ywk/voices/{id}/latent` も走行中は同じ）・`ywk_precompute_unknown_id`（404）・`ywk_unknown_voice`（400・`ids` に知らない名／**404**・`DELETE /ywk/voices/{id}/latent` の知らない話者） |
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
           "preset": false, "no_ref": true, "latent": false, "latent_stale": false},
          {"id": "琴葉茜（関西弁）", "object": "voice", "display_name": "琴葉茜（関西弁）",
           "preset": true, "no_ref": false, "latent": true, "latent_stale": false}]}
```

- 形は上流互換（`{"object":"list","data":[…]}`）だが、**パス欄（`ref_wav`・`ref_wavs`・`ref_latent`・
  `ref_latents`・`ref_embed`）を落とし**、`display_name`・`preset`・`no_ref`・
  `latent`・`latent_stale`（4-4）を足す。
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
- **プリセットの表示名（＝話者 id）は配布版の版で変わりうる**（裁定 108）。ランチャは起動時に
  利用者の台帳の旧い id を新しい id へ改めるので、**旧い id は一覧から消える**。本体が保存して
  おいた旧い id を送ると `ywk_unknown_voice`（⑶ 3-3 の 1 行目）で返る＝**版をまたいで id を固定せず、
  一覧を読み直す**こと。最初の 1 件＝「もち子さん」→「もち子さん（セクシー／あん子）」（2026-09-07）。
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
- **`no_ref: false` は「参照あり」ではない**＝上流の 6 欄の短絡は**揃っていない**。パス 5 欄は
  「`None` でなければ」短絡するが、`no_ref` は同じ `or` 連鎖の最後の項で**真値として**読まれる
  （`app.py:439-446` の `or explicit_no_ref`）＝`no_ref: false` は話者解決に進む。配布版も同じに読む
  ＝`voice` を省いた `{"irodori":{"no_ref":false}}` は**欄ごと出さないときと同じ 200**（参照なし合成）で、
  `voices.json` がまだ無い初回起動でも 400 にならない。**`voice` との同時指定は 400 のまま**
  （3-2 の 4＝どちらの意味か body から決められない）。
- 参照なしの声は **Irodori 自身の素の声**（caption のみで作る）。

### 4-3 所有者

- **`voices/` と `voices.json` を書くのは配布版だけ**。本体は書かない・消さない・
  wav のパスを持たない。本体がやるのは「一覧から名前で選ぶ」ことだけ。
- **配布版の中では、`voices.json` を書くのはランチャ（便 D）と wrapper の 2 つ**である。
  wrapper が書くのは⑺ 7-3 の事前計算が走ったときだけで、**触るのは焼いた話者の欄 1 個**
  （`ref_latent` に差し替え・他の鍵は残す・檔全体は temp→`os.replace` で置換）。
  ⇒ **ランチャは `voices.json` を握りっぱなしにせず、書くときに読み直す**こと。
  `voices/voices.ywk.json`（表示名・preset）は wrapper が**読むだけ**で、こちらは触らない。
- **上流の書き込み 3 口は口ごと外してある**＝`POST /v1/audio/voices`・
  `PUT /v1/audio/voices/{id}`・`DELETE /v1/audio/voices/{id}` は 404／405 を返す。
  理由＝本体も配布版 UI も使わない口であり（D-3）、api_key を持たない設計
  （`decisions.md` 31）では**登録口が開いていること自体が利用者機に残る唯一の書き込み面**
  だから（multipart/form-data は CORS の simple request＝preflight なしで届く）。
  読みの `GET /v1/audio/voices/{id}`（檔名・大きさ・更新時刻だけ）は残す。
- **話者を消すときは 4 つを消す**（Radeon 版で潜在を焼いた後・裁定 65）。wrapper が書いた alias 欄は
  **wav より長生きする**＝`resolve()` は別名を走査より先に読む（`voices.py:80-88`）ので、
  `voices/` の wav を消しても `voices.json` の `{"ref_latent":"latents/…"}` が残る限り
  **話者は一覧に残り `.pt` から鳴り続ける**。上流の書き込み 3 口は外してある（上記）ので、
  便 D は次の 4 つを消す＝⒜ `voices.json` の当該話者の欄 ⒝ `voices/latents/<stem>.pt`
  ⒞ `voices/latents/<stem>.json` ⒟ 元の参照 wav。
  **⒜〜⒞ は口 1 本で済む**＝`DELETE /ywk/voices/{id}/latent`（⑺ 7-3）が alias を wav へ戻し
  2 檔を消すので、便 D が檔を直接いじる必要があるのは ⒟ だけである。
- 話者メタ（表示名・caption 既定・既定パラメータ）は**上流に置き場が無い**
  （`VoiceSpec` はパス 5 欄＋`no_ref` の 7 欄だけ・余分な鍵は無警告で捨てられる実射＝
  `research/lab/notes/37` §3-4）＝配布版の独自台帳（`voices/voices.ywk.json`）が持つ。
- **プリセット話者 12 名**をリリース版に同梱する予定（`decisions.md` 17・生成は便 P）。
  一覧では `preset: true` で区別できる。

### 4-4 参照潜在（`latent`・`latent_stale`）＝**Radeon 版では話者登録時に潜在を焼く**

**`decisions.md` 65**（裁定 11 を Radeon 版に限って覆した）。Radeon（gfx1151）では
**話者を切り替えるたびに `prepare_reference` が 0.95〜1.44 s** かかる（`docs/radeon.md` §7-6 の実測）。
参照 wav を**一度だけ**潜在に焼いて `.pt` で渡すと、ここが **1 発目 10.4〜11.5 ms・以後 1.2〜1.5 ms**
になり、**参照長にまったく依らなくなる**（VRAM も増えない＝`research/lab/notes/40-rocm-warmup.md` §6-2）。

- **一覧の 2 欄**（どちらも **bool**・パスは出さない）：
  - `latent`＝この話者が**焼かれた `.pt` から鳴る**（`.pt` が実在する）。
  - `latent_stale`＝焼いてはあるが、**元の wav が変わった／消えた**＝いま鳴る声は
    `voices/` の檔と違う。ランチャはここに印を出し、焼き直しを促す。
    **`<stem>.json` が無いときの読みは檔の在り処で分かれる**＝
    ⒜ **配布版の置き場ではない `.pt`**（alias が `latents/<stem>.pt` 以外を指す）は
    `latent: true`・`latent_stale: false`＝素性が判らない檔を「古い」と名乗って上書きさせない。
    ⒝ **配布版の置き場そのもの**（`latents/<stem>.pt`＝`<stem>` は話者 id の sha256 を含むので
    偶然その名になることはない）は `latent_stale: true`＝**配布版が焼いた物の地図を失った**状態で、
    健全と名乗らせない（名乗らせると、alias はもう wav を指さず sidecar も無いので
    **その話者は二度と焼き直せないまま「異常なし」に見える**）。
- **置き場は `voices/latents/<ascii-stem>.pt`**（`<stem>.json` が隣）。**`voices_dir` 直下には置かない**。
  理由 2 つ＝⑴ 上流の走査は `voices_dir` **直下 1 段だけ**なので、`.pt` が**話者として一覧に増えない**
  ⑵ 上流の走査では**同 stem の `.wav` が `.pt` に勝つ**（`research/report/irodori-native-handoff-2026-09-04.md`
  §1-7 ⒝＝無警告）。別ディレクトリなら stem が衝突しない。
  `<stem>` は話者 id の sha256 先頭 12 桁を必ず含む ASCII（話者名は日本語でよい＝4-1）。
- **経路は alias 1 本**＝`voices.json` の当該話者を
  `{"<話者名>": {"ref_latent": "latents/<stem>.pt"}}` に**書き換える**（相対パス＝アプリを移せる）。
  **`ref_wav` は残さない**＝上流は波形と潜在の同時指定を 400 にする（`app.py:1062-1067`）し、
  `VoiceSpec` にはどちらを優先するかを書く欄が無い。`resolve()` は**別名を走査より先に読む**
  （`voices.py:80-88`）ので、`voices/` に wav 檔が残っていても潜在が勝つ。
- **元の wav は消さない**。`<stem>.json` が wav の **sha256** と符号化の 3 条件
  （`normalize_db`・`ensure_max`・`max_ref_seconds`）**＋チェックポイント**
  （`checkpoint`＝`IRODORI_HF_CHECKPOINT` の実効値・判れば `latent_dim` も）を持ち、
  これが**焼き直しの地図**になる（alias が `.pt` を指した後、`VoiceSpec` はもう wav を知らない）。
  **`<stem>.json` は wav と対の檔である**＝消すと地図を失う。sidecar の `schema` は **2**
  （1 はチェックポイント欄が無い＝素性を保証できないので 1 度だけ焼き直す）。
  - **チェックポイントが鍵に要る理由**＝上流の潜在経路は**形と `latent_dim` しか見ない**
    （`inference_runtime.py:906-912`・`_coerce_latent_shape` 同 :136-147）。この機体の
    `latent_dim` は **32**＝**次元が同じ別モデルで焼いた `.pt` は無警告で通る**。
    `GET /params` は `checkpoint.hf` を外に出しており、ランチャが差し替えられる面である。
    チェックポイントが変われば `latent_stale: true`。
- **地図を失っても諦めない**＝`<stem>.json` が無く alias が配布版の置き場を指しているときは、
  wrapper が元 wav を 2 つの経路で引き直す＝⑴ `voices/voices.ywk.json` の
  `ref_wav`／`ref_wavs`（便 D の台帳・wrapper は読むだけ）⑵ 上流と同じ走査の命名
  `voices/<話者 id>.<拡張子>`（`voices.py:165-183`＝プリセット wav を `voices/` に置く形）。
  **どちらでも辿れないときだけ**焼けない＝`force:true` の要求は `skipped` ではなく
  **`failed`（理由つき）**で終える（⑺ 7-3）。
- **焼いた後は要求時の `ref_normalize_db` が効かない**＝正規化は**焼くときに済んでいる**。
  上流の潜在経路は `ref_normalize_db`／`ref_ensure_max` を読まない（`inference_runtime.py:903-924`）。
  配布版は焼くときに `IRODORI_DEFAULT_REF_NORMALIZE_DB`（既定 −16.0）と
  `IRODORI_DEFAULT_REF_ENSURE_MAX`（既定 true）＝**要求時の既定と同じ値**を使うので、
  既定のまま使う限り音は変わらない。この 3 条件（＋チェックポイント）が変わると `latent_stale` が立つ。
- **CUDA 版は既定で焼かない**（`decisions.md` 11＝GPU では節約が 40〜115 ms しかない）。
  口は変種に依らず在る（⑺ 7-3）。

題目（4-4 の追補）＝`sidecarを失うと配布版の潜在はstaleになる`／`他人のptはsidecarが無くてもstaleにしない`／
`sidecarを失っても走査のwavから焼き直せる`／`sidecarを失っても台帳のwavから焼き直せる`／
`チェックポイントも鍵のうち`／`schema1のsidecarは1度だけ焼き直す`／
`潜在を外すとaliasがwavへ戻る`／`潜在を外してもランチャの欄は残る`。

題目＝`norefのfalseは参照ではない`／`一覧の先頭にデフォルトが常在する`／`voicesjsonが無くてもデフォルトで合成200`／
`voicesjsonが壊れても500ではなく1件と理由`／`一覧にnoneが0件`／`noneの別名は正規化されて200`／
`一覧の応答に絶対パスが0件`／`日本語話者名で合成200`／`上流の書き込み3口が外れている`／
`一覧にlatentとlatentstaleが載る`／`焼いた話者はrefwavではなくreflatentで鳴る`／
`潜在は一覧に話者として増えない`。

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
   {"key":"cfg_scale_text","type":"number","default":3.0,"min":0.0,"max":10.0,"step":0.1,"nullable":false,"exposed_to_ywk":true,"group":"emotion","label":"感情表現の強さ","description":"本文条件（本文の文字列そのもの＝挿した絵文字を含む）への追従の強さ。上げるほど本文どおりに読み、絵文字で指定した表情も強く出るが、上げすぎると不自然になる。発音・滑舌が弱いときに少し上げる。0 は本文条件の誘導を切る（実用下限は 1.0 付近）。","note":"本体はこの欄を「感情表現の強さ」の表示名で出し、IsEmotion を立てて「感情」の帯に置く（decisions.md 100・表示名は AivisSpeech の intonationScale と同じだが帯は違う＝Aivis は基本帯・目盛りは互換でない＝Aivis 0〜2 中立 1.0／Irodori 0〜10 中立 3.0）。0 は本文 CFG の枝そのものを外す（上流 rf.py:264 has_text_cfg = cfg_scale_text > 0）＝表情が薄くなるのではなく本文への追従が弱る。caption／cfg_scale_caption は演技指示の欄（caption を使うときだけ意味がある）。decisions.md 99","range_source":"上流 gradio_app.py のスライダ（UI の範囲であって検査ではない）（:520-526）"},
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

- **本体が写すのは `exposed_to_ywk: true` の欄だけ**（8 欄＋`voice`・`speed`）。
  44 欄を全部 UI に出すのではない＝`ref_wav`・`ref_latent`・`ref_embed`・`lora_adapter` のような
  パス欄や `duration_scale`（`speed` と除算合成される）は `exposed_to_ywk: false` で、
  配布版の UI（上級者向け `advanced` 群）だけが扱う。
- **`cfg_scale_text` は本体の「感情表現の強さ」**（`decisions.md` 99）＝本体はこの欄を表示名「感情表現の強さ」で出す
  （表示名は `/params` の `label` をそのまま `DisplayName` に・Number・0.0〜10.0・step 0.1・**既定は `/params` の `default`**＝
  現在 3.0・`IRODORI_DEFAULT_CFG_SCALE_TEXT` で動くので焼かない）。**`IsEmotion` を立てる＝本体の「感情」見出しの帯に出す**（`decisions.md` 100・司令官の裁定）。
  表示名だけ AivisSpeech の `intonationScale` と同じで、帯は違う（Aivis は基本帯）。
  **表示名だけが同じで目盛りは互換でない**（Aivis 0〜2 中立 1.0／Irodori 0〜10 中立 3.0）・本体にマスター感情の写像は無く、値はエンジンごとに独立。
  意味＝本文条件（挿した絵文字を含む本文そのもの）への追従の強さ（上流 `docs/parameters.md:129`・README「Emoji-based Style Control」）＝
  **0 は本文 CFG を切る**（表情が薄くなるのではなく本文への追従が弱る＝上流 `rf.py:264`・実用下限 1.0 付近）。
  `caption`（演技指示）と `cfg_scale_caption` は演技指示の欄＝caption を使うときだけ意味がある。
  **この欄を「CFG 強度」の名では出さない**（`cfg_scale_caption`・`cfg_scale_speaker` の `label` は「CFG 強度（…）」のまま）。
  `group` は配布版自身の UI 用＝本体は帯の判断に使わない（`caption`・`cfg_scale_text`・`cfg_scale_caption` の 3 欄が `group:"emotion"`・
  `max_caption_len` も同じ群に居る＝群は本体の帯ではない）。
  **`description` は `NoteToolTip` に写してよい・`note` は写さない**（実装席向けの注記＝裁定番号や上流の行番号を含む）。
  ただし本体の Number の帯は現状 `Note`／`NoteToolTip` を出さない（出るのは Text だけ＝`TextParamViewModel.cs:82-83`）＝出したいなら本体側の 1 手が要る。
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
{"engine":"irodori-ywk","version":"<配布版の版>","upstream":{"irodori_tts":"8224daf","server":"841fb7c"},"host":"127.0.0.1","port":18088,"runtime":{"loaded":bool,"loading":bool,"error":str|null},"device":{"configured":"cuda:0","actual":"cuda:0"|null,"name":"NVIDIA GeForce RTX 3090"|null,"uuid":"…"|null,"pci_bus_id":"…"|null,"precision":"bf16"},"torch":{"version":"2.10.0+cu130","cuda":"13.0","hip":null},"voices":{"count":13,"dir":"<絶対パスは出さない・末尾 1 段のみ>"},"warmup":{"state":"idle|running|done","shots":0}}
```

- **`device.actual` はモデル読込後に実測した値**（`next(model.parameters()).device`・
  `torch.cuda.get_device_properties(idx).uuid`）＝**設定値の echo ではない**。**未読込なら `null`**。
  上流の `/health.model.model_device` は素の echo で、`auto` は `auto` のまま返る
  （`app.py:173`・`research/lab/notes/14` §7）。
- **GPU の同定は UUID（と PCI bus id）で行う**。index は再起動で変わりうる
  （`CUDA_DEVICE_ORDER` の既定は `FASTEST_FIRST`・nvidia-smi 自身が再起動間の一貫性を保証しないと明記）。
  torch は CUDA でも AMD でも `uuid` と `pci_bus_id` を持つ。
- **`device.pci_bus_id` は `string|null`**（`decisions.md` 87 ⑵）。**torch は CUDA でも ROCm でも
  `int` を返す**（gfx1151 の実射で `197`・RTX 3090 の実射で `1`＝`decisions.md` 75 ⑷）＝
  両者は同じ C++ binding 1 つで、torch 自身も整数として扱い（`torch/numa/binding.py` の
  `f"{bus:02x}"`）、型 stub（`torch/_C/__init__.pyi` の `_CudaDeviceProperties`）はこの属性を
  宣言してすらいない。そのまま出すと本体・ランチャの同じ 1 欄が**数**のまま流れるので、
  **wrapper が `str()` を掛けて必ず文字列にする**（属性が無い・未読込なら `null` のまま）。
  〔便 D（2）の是正＝1 巡目の「CUDA 側は文字列」は席が足した推測で、裁定 87 ⑵ 自身は
  「ROCm は int を返す」としか言っていない。実測は上のとおり両方 int。〕
- **精度は device と連動**＝GPU→`bf16`・CPU→`fp32`（`decisions.md` 7）。
  上流は **CPU×bf16 を起動時に `ValueError` で弾き**（`inference_runtime.py:306-310`）、
  **fp32×GPU 不在は黙って CPU に落ちて 200 を返す**（2 文字・10 steps で 267 秒＝340 倍の実測）。
  配布版は `cuda` 指定で `torch.cuda.is_available()` が偽なら**起動前に理由 1 行で exit 2** にする。
- **絶対パスは出さない**（`voices.dir` は末尾 1 段だけ）。
- 上の逐語に**足した欄が 6 つある**（**欄を足すのは `schema` を上げない**＝⑻）：
  - **`pid`**（`int`）＝**この応答を返した個体の pid**（便 D（2）の是正で追加）。
    要る理由＝**既にそのポートを握っている個体が居れば、同じ形で答えてくる**。
    〔**裁定 105（ポート先行）で窓は縮んだが閉じていない**＝1 巡目の理由は「`preload=true`
    （裁定 7）はモデルを載せてから bind するので、起動の 20〜28 秒（gfx1151 の実測）の間は
    別の個体が同じポートを握って同じ形で答えられる」だった。`preload=false` の今は自分の子が
    1 秒以内に bind を試みるが、**先客が居れば子は bind に失敗して落ちる**＝その間ずっと
    先客の応答が返る。〕ランチャは
    自分が起こした子の pid と突合し、違えばその標本を採らない（状態帯＝裁定 67 ⑶ に他人の
    GPU メモリを出さず、`Ready` にも上げない）。**欄が無ければ突合しない**＝古い個体・上流の
    素の Server を「別人」と読まない。パスでも秘密でもない（OS のタスク一覧と同じ数）。
  - `voices.error`（`string|null`）＝`voices.json` が読めないときの理由 1 行（⑷ の「0 件＋理由」の相方）。
  - **`variant`**（`string`）＝ランチャが起こしたビルドの名前。既定 **`"cuda"`**（`decisions.md` 4）・
    CPU 版は `"cpu"`・Radeon 版は `"rocm-gfx1151"`（`decisions.md` 5＝**arch を名乗る**。
    確認済みは gfx1151 だけで、他 gfx は未検証）。env `YWK_VARIANT` の値をそのまま返す。
    **これは「何で組んだか」の名前であって能力ではない**＝実際に載った device は `device.actual`。
  - **`device.hip`**（`string|null`）＝`torch.version.hip`。**`device.gcn_arch`**（`string|null`）＝
    `torch.cuda.get_device_properties(i).gcnArchName`（例 `"gfx1151"`）。**ROCm の torch は device type を
    `cuda` と名乗る**ので、CUDA 機と Radeon 機を分けるのはこの 2 欄である（CUDA ビルドでは
    `gcnArchName` 属性が無い＝両方 `null`・CPU に載っているときも `null`）。
    実測の形＝`research/lab/notes/29-gpu-designation-lab.md` :317。
  - **`precompute`**（`object`）＝⑺ 7-3 の参照潜在の事前計算の記録。`warmup` と同じ形の
    走行記録で、**欄は `state`／`id`／`done`／`total`／`last`／`error`**
    （＋`elapsed_s`・内訳 `built`／`reused`／`skipped`／`failed`）。
    **`state` は `idle|running|done|failed|cancelled`**。走ったことが無ければ
    `{"state":"idle","id":null,"done":0,"total":0,…}`。**変種に依らず必ず在る欄**で、
    CUDA 版では既定で `idle` のまま（⑷ 4-4）。
  - **`memory`**（`object`）＝GPU メモリと潜在の大きさ（`decisions.md` 87 ⑴・67 ⑵⑶）＝**6-1**。
- **`warmup` は⑺ 7-2 の暖機の記録**に育った（便 C）。便 A が約束した `state`・`shots` の 2 欄は
  そのままの意味で残っている。
- **Radeon 版は精度が bf16 に固定**（`decisions.md` 5・36）＝`IRODORI_MODEL_PRECISION=fp32` を
  載せて `rocm-*` を起動すると、**上流を import する前に理由 1 行で exit 2** する
  （`IRODORI_MODEL_DEVICE=cpu` のときだけ fp32 を許す）。根拠＝同一形状 1 発目が
  fp32 32.1 s 対 bf16 2.84 s（`research/lab/notes/40-rocm-warmup.md` §8）＝MIOpen が
  workspace 0 の遅い solver に落ちるため、fp32 の decode は CPU より遅い。
- **Radeon 版は MIOpen の db を利用者データ配下に置く**＝`MIOPEN_USER_DB_PATH`＝
  `<YWK_DATA_DIR>/miopen/db`・`MIOPEN_CUSTOM_CACHE_DIR`＝`<YWK_DATA_DIR>/miopen/cache`（`setdefault`）。
  この db は**プロセスを跨いで効く**（空 db の 1 発目 11.2 s → 既存 db 2.5 s＝`40` §5）。
  本体には見えない（`/ywk/status` はパスを載せない）が、暖機の効きが再起動後も残る根拠はここ。
- **本体は device を要求 JSON に載せない**。device は Server プロセスの env
  （`IRODORI_MODEL_DEVICE`／`IRODORI_CODEC_DEVICE`）だけで決まる＝1 プロセス 1 デバイス。
  ランチャで選んだ GPU が**暗黙に**使われる（`decisions.md` 15・⑼ の D-7）。
  `/ywk/status` は**表示と記録のため**の口であって、切り替えの口ではない。

### 6-1 `memory`＝GPU メモリと潜在の大きさ（`decisions.md` 87 ⑴）

**ランチャが常時（2 秒ごと）読む欄**（`decisions.md` 67 ⑶）。**単位はすべてバイト。**

```json
{"device":"cuda:0"|"cpu"|null,"allocated":int|null,"reserved":int|null,"max":int|null,
 "gpu_total":int|null,"gpu_free":int|null,"gpu_used":int|null,
 "latents":{"<話者 id>":int,…},"latents_total":int,"sampled_at":"<ISO 8601・UTC・末尾 Z>"}
```

| 欄 | 何を測っているか | 出所 |
|---|---|---|
| `device` | いま測っている device＝`device.actual` と同じ値。未読込は `null` | 実測（⑹） |
| `allocated` | **このプロセスが今つかんでいる量**（ランチャの表示＝**使用量**） | `torch.cuda.memory_allocated` |
| `reserved` | **このプロセスが OS から取り上げてある量**（同＝**占有量**）。`allocated` **以上**（等しくもなる＝読込前はどちらも 0） | `torch.cuda.memory_reserved` |
| `max` | このプロセスの `allocated` の**累積ピーク**（プロセス起動以来） | `torch.cuda.max_memory_allocated` |
| `gpu_total` | **wrapper の torch／HIP から見た値**（裁定 110＝下の註） | `torch.cuda.mem_get_info`（**ROCm も同じ口**） |
| `gpu_free` | 同上（空き） | 同上 |
| `gpu_used` | `gpu_total − gpu_free`。〔**裁定 110（2026-09-08）で読み替え**＝**「カード全体（他プロセス込み）」ではない**。Windows の ROCm では**自プロセス相当**で、他プロセスを含まない〕 | 同上 |
| `latents` | 焼いてある話者の `latents/<stem>.pt` の**実サイズ**を**話者 id で引いた表** | `stat` |
| `latents_total` | `latents` の合計 | 同上 |
| `sampled_at` | 測った時刻（ISO 8601・UTC・`…Z`） | — |

- **`gpu_used` と `allocated`／`reserved` は測っている物が違う**。前 3 つは torch の allocator が見た
  **このプロセス**、`gpu_*` は HIP／CUDA のドライバが見た値である。
  Radeon 機では両者が大きく食い違うことがある（`docs/radeon.md` §3 と §7-7＝in-process 対 OS 側）。
  **片方の数字でもう片方を否定しない。**
- 〔**裁定 110（2026-09-08）＝`gpu_*` を「カード全体」と読むのをやめた**。JSON の形は**据え置き**
  （欄も出所も変えない）。読み替えるのは**意味**だけである＝`gpu_used` は **wrapper の torch／HIP から
  見た値**で、Windows の ROCm では**自プロセス相当・他プロセスを含まない**。実測（Radeon 8060S
  gfx1151・2026-09-08・wrapper pid 31556）＝同じ瞬間に `gpu_used` **3.66 GiB** に対し、Windows の
  計数は `GPU Process Memory\Dedicated Usage`（同じ pid）**10.84 GiB**・
  `GPU Adapter Memory\Dedicated Usage`（カード全体）**29.79 GiB**（タスク マネージャーの「専用」
  29.8 GB と一致）。**ランチャは Windows の計数（専用メモリ）を別に読んで出す**（設計は
  `docs/design/ben-d-launcher.md` §27）ので、状態帯に `gpu_used`／`gpu_total` は出さなくなった。
  **wrapper 側は何も変えない**＝この 3 欄は互換のためそのまま出し続ける。〕
- **`max` はピーク・`reserved` は現在**＝`allocated ≤ reserved ≤ max` は**不変条件ではない**
  （gfx1151 の実射で `max` 4,002,721,792 ＞ `reserved` 2,212,495,360）。
- **`gpu_total` が何の全体かは機体で違う**＝離散 GPU なら VRAM だが、**gfx1151 は共有メモリ**なので
  実射で **107,090,132,992 B（99.7 GiB）**＝「カードの残り」ではなく**共有プールの残り**である。
  Radeon 機かどうかは `device.gcn_arch` が非 `null` かで分かる。
- **device が `cpu` か未読込のときは数値 6 欄が `null`**（`0` ではない＝「測っていない」の意）。
  そのときも **`latents` は出る**＝焼いた `.pt` は何が載っているかに依らず檔として在る。
  〔**裁定 105（ポート先行）で成り立つようになった**＝1 巡目は「**ランチャは ready の前から
  大きさを出せる**」と書き、便 D（2）の是正が「`preload=true`（裁定 7）だと uvicorn は lifespan
  （＝モデル読込）を終えてから bind するので、その間はこの口ごと答えが無い」と打ち消していた。
  **配布する構成が `preload=false`＋裏読込に変わった**ので、`/ywk/status` は**bind 直後（wrapper の起動から 3〜4 秒・exe からは約 9 s）から
  答える**＝この欄（と ⑵ の「`loaded=false`・`loading=true` が返る窓」）は、いまや**配布版の
  正常形として 20〜70 秒ずっと観測できる**。読込前は数値 6 欄が `null` で `latents` だけが出る、
  という上の規則はそのままである。〕
- **`latents` に載るのは焼いてある話者だけ**＝焼いていない話者は**行ごと出ない**
  （`0` と「まだ焼いていない」を混ぜない）。`latents/<stem>.pt` の `<stem>` は⑷ 4-4 と同じ。
  **話者 id で引ける表**なので、台帳から消えた話者の残骸 `.pt` はここに現れない。
- **絶対パスは出さない**（⑹ の全体規則と同じ＝檔名も `<stem>` も外に出ない）。
- **取得に失敗しても `/ywk/status` は 200 のまま**＝`memory` の中に **`error` が 1 行**増えるだけで、
  読めた欄は読めたまま残る（allocator は取れたが `mem_get_info` が落ちた、など）。
  `error` は**失敗したときだけ現れる**欄で、絶対パスは `<path>` に畳む（⑶ 3-3 と同じ規則）。
- **所要は数 ms**（`stat` が話者の数だけ＋ドライバ問い合わせ 1 回）＝2 秒間隔の polling に耐える。
- **`schema` は上がらない**（欄を足しただけ＝⑻）。

題目＝`ywkstatusのdeviceactualは実測値でモデル未読込ならnull`／`ywkstatusに絶対パスが0件`／
`ywkstatusが変種を名乗る`／`ywkstatusのdeviceにhipとgcnarchが載る`／`rocm変種はfp32指定でexit2`／
`rocm変種はMIOpenのdbを利用者データ配下に置く`／`ywkstatusにprecomputeの欄が在る`／
`memoryは裁定87の10欄を名乗る`／`cpuではmemoryの数値6欄がnull`／`未読込でもmemoryの数値6欄がnull`／
`GPUではmemoryに数値が載る`／`gpuusedはカード全体`〔**題目は台帳の履歴**＝この読みは
**裁定 110（2026-09-08）で撤回**した（上の註）。釘そのものは
`gpu_used == gpu_total − gpu_free` の**算術だけ**で、いまも同じことを検めている＝
`tests/contract/test_memory.py::test_gpu_used_is_the_whole_card`〕／`latentsは話者idで引ける`／
`焼いていない話者はlatentsに載らない`／`latentsはcpuでも読める`／`pcibusidは文字列`／
`memoryの取得に失敗しても200でerrorが1行`／`memoryのerrorに絶対パスが0件`。

---

## ⑺ 先読み（7-1・**予約**）・暖機（7-2）・参照潜在の事前計算（7-3）

### 7-1 先読み（`POST /ywk/prefetch`）＝**予約**＝便 A では `available: false`

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

### 7-2 暖機（`POST /ywk/warmup`）＝**ランチャの口。本体は叩かない**

**「初めての形」は 1 発目だけ数秒〜十数秒かかる。それを利用者の読み上げの前に済ませておく口。**
叩くのは**ランチャ（便 D）**であって本体ではない（本体は⑶の 1 本道だけを使う）。

- **なぜ要るか**（事実＝`research/lab/notes/40-rocm-warmup.md`・`decisions.md` 40）＝Radeon（gfx1151）では
  **出力の潜在フレーム数（40 ms 刻み）と参照ボイスの長さが「形」**で、**形ごとに MIOpen の探索が走る**。
  罰は二層＝**ディスクの user db**（空 db の 1 発目 11.2 s 対 既存 db 2.5 s・**再起動を跨いで残る**＝§5）と
  **プロセス内の残差**（`decode_latent` に +0.18〜0.61 s＝§5-1）。
  **未見の参照ボイスに当たるたびに跳ねる**（VOICEVOX/COEIROINK 参照で 0.6〜1.0 s → 最初の
  VOICEROID2 参照で 14.8 s＝`decisions.md` 40）。**CUDA 側で同じ罰があるかは便 B の実射待ち**
  ＝口は変種に依らず在るが、既定で撃つかはランチャが決める。

| 口 | body | 応答 |
|---|---|---|
| `POST /ywk/warmup` | `{"stages":[4,8,12],"voices":["デフォルト","琴葉茜"],"text":"暖機です。"}`（3 欄とも任意） | **202** `{"id","shots_total","state":"running"}` ／ 走行中は **409** `ywk_warmup_running` |
| `DELETE /ywk/warmup/{id}` | — | **200** `{"id","state","cancel_requested"}`／別の id は **404** `ywk_warmup_unknown_id` |
| `GET /ywk/status` | — | `warmup` 欄（下） |

- **射の順**＝`stages` の秒（**`no_ref`・`seconds` 指定**＝出力尺を秒ちょうどにして「形」を焼く）を並び順に、
  そのあと `voices` の各話者に**短文 1 射**（`seconds` は指定しない＝自然尺・**参照の形**を焼くのが目的）。
  **`shots_total` ＝ `len(stages)+len(voices)`**。`stages` の既定は **`[4,8,12]`**（3 段で 6.25 s＝`40` §3）、
  `text` の既定は **`"暖機です。"`**。**`seconds` を使うのは暖機の射だけ**（本番の要求に送ってはいけない＝⑶ 3-1）。
- **優先度＝本物の要求が必ず勝つ**。wrapper は `POST /v1/audio/speech` の入口と出口で「走行中の本物」を数え、
  暖機は**次の射の前に 0 になるまで待つ**（最大 **30 s**・以後は 1 射だけ撃って再確認）。
  射そのものは**上流の合成セマフォ**（`max_concurrent_synthesis` 既定 1＝⑶ 3-4）に並ぶので、
  **本物が待たされるのは最大で暖機 1 射分**（C-5）。
  **`stream_format:"sse"` の要求も同じ扱い**＝上流はチャンクごとに合成枠を取り直す
  （`app.py:711`）ので、数えるのは応答を返すまでではなく**最後のチャンクを流し終えるまで**。
  ここを handler の出口で下ろすと、1 本の SSE 要求がチャンクの合間に**射の数だけ**
  追い越される（敵対検分で実測）。切断で本文が閉じられたときも下ろす。
- **暖機の射も本番と同じ話者解決を通る**＝「デフォルト」は `voices.json` が無くても・壊れていても
  参照無しの射に書き換わる（⑷ 4-2・`decisions.md` 45）。通らないと、同じ話者 id が
  本番 200・暖機 400 になり、**「失敗は 1 射目で止める」規定と重なって run 全体が死ぬ**。
- **取消は「次の射を撃たない」ところまで**＝走っている射は最後まで走る（⑶ 3-4 と同じ粒度）。
- **暖機の wav は捨てる**（`/v1/audio/speech` は通らない＝本体の音は 1 度も鳴らない）。
  ログは **1 射 1 行**（`kind`・`key`・`seconds`・`ms`）。
- **失敗は 1 射目で止める**＝どれか 1 射が例外を出したらその時点で `state:"failed"`・`error` に理由
  （絶対パスは畳む＝⑶ 3-3 と同じ規則）。**知らない話者名は 1 射目で判る**という意味でもある。
- **`/ywk/status.warmup` の形**：

```json
{"state":"idle|running|done|failed|cancelled","id":"…|null","shots_done":0,"shots_total":0,
 "elapsed_s":0.0,"last_shot":{"kind":"no_ref|voice","key":"4s|琴葉茜","seconds":4.0,"ms":2518.4},
 "error":null,"shots":0}
```

  - `elapsed_s`＝走行中は現在まで・終わっていれば総計。`last_shot`＝直前に**成功した**射（未着手は `null`）。
  - **`shots` は `shots_done` の別名**＝⑹の逐語（便 A）が名乗った欄をそのまま残してある。
  - **`cancelled` は便 C が足した state**（設計書 §2-3 は 4 つを挙げるが、取消と完走は区別できねばならない）。
- **起動時暖機**＝env `YWK_WARMUP_ON_START=1`・`YWK_WARMUP_STAGES`（カンマ区切りの秒）・
  `YWK_WARMUP_VOICES`（カンマ区切りの話者 id）・`YWK_WARMUP_TEXT` で、**ready 直後に自動で 1 回**走る。
  ランチャが無い間の検分用であり、便 D は明示的に `POST /ywk/warmup` を叩く形にしてよい。
- **body の検査は⑶ 3-2 と同じ規律**＝未知欄は `ywk_unknown_field`・型違いは `ywk_type_error`・
  範囲外（`stages` の秒は 0 < s ≤ 60・最大 12 段・`voices` 最大 64 件・`text` 最大 200 字・
  **射が 0 本になる指定**）は `ywk_out_of_range`。

題目＝`暖機は段のあとに話者を撃つ`／`走行中の暖機に重ねると409`／`暖機の取消は次の射の前で止まる`／
`暖機の失敗はstatefailedと理由`／`本物の要求が来たら暖機は次の射の前で待つ`／
`暖機は上流のセマフォに並ぶ`／`暖機のbodyの検査`／`起動時暖機はenvで走る`／
`SSEの間もカウンタを握る`／`SSEが失敗してもカウンタは下りる`／
`暖機の射も保証された話者を解決する`／`暖機のrunは保証された話者で止まらない`。

### 7-3 参照潜在の事前計算（`POST /ywk/voices/precompute`）＝**ランチャの口。本体は叩かない**

**話者の参照 wav を 1 回だけ潜在（`.pt`）に焼いて、以後の話者切替を 1 秒級から ms 級にする口。**
形の詳細（置き場・alias・`latent`／`latent_stale`）は⑷ 4-4。ここは**口と走行記録**だけ。
`decisions.md` 65（裁定 11 を Radeon 版に限って覆した）。

| 口 | body | 応答 |
|---|---|---|
| `POST /ywk/voices/precompute` | `{"ids":["琴葉茜","月読アイ"]}` **または** `{"all":true}`（＋任意 `force`） | **202** `{"id","total","state":"running"}` ／ 走行中は **409** `ywk_precompute_running` |
| `DELETE /ywk/voices/precompute/{id}` | — | **200** `{"id","state","cancel_requested"}`／別の id は **404** `ywk_precompute_unknown_id` |
| `DELETE /ywk/voices/{話者 id}/latent` | — | **200** `{"id","state":"reverted\|absent","alias":"ref_wav\|ref_wavs\|removed\|unchanged","removed":[…]}`／知らない話者は **404** `ywk_unknown_voice`／走行中は **409** `ywk_precompute_running` |
| `GET /ywk/status` | — | `precompute` 欄（下） |

- **`ids` と `all` はどちらか一方**（両方＝400 `ywk_invalid_body`・どちらも無し＝400 `ywk_out_of_range`）。
  `all:true` の対象は**「デフォルト」と `no_ref`／`ref_embed` を除く全話者**＝`total` はその数。
  `ids` に**知らない話者名があれば 1 件も走らずに 400** `ywk_unknown_voice`（暖機と違い、走る前に判る）。
  `force:true` は**変わっていなくても焼き直す**＝だから **`force` で焼けなかったものは `skipped` にしない**
  ＝元 wav を辿れない話者（sidecar を失い ⑷ 4-4 の 2 経路でも引き直せない）は **`failed`＋理由**で終える。
  「参照なし」「`ref_embed`」は `force` でも `skipped`（焼く物が元から無い）。
- **潜在を外す口**＝`DELETE /ywk/voices/{話者 id}/latent`。alias を**元の wav へ戻し**
  （`<stem>.json` の `sources`・無ければ ⑷ 4-4 の引き直し 2 経路）、`latents/<stem>.pt` と
  `<stem>.json` を消す。alias 欄の**他の鍵は残す**（残る鍵が無ければ欄ごと落として上流の走査に返す）。
  **書き換えの順は「alias が先・檔の削除が後」**＝途中に届いた要求が消える寸前の `.pt` を掴まない。
  焼いてなければ `state:"absent"`（200・冪等）。`removed` は **`voices_dir` からの相対**（絶対パスを出さない）。
  **配布版の置き場ではない `.pt`**（利用者が自分で置いた物）は **alias から外すだけで檔は消さない**
  ＝`removed` は空配列（人の檔を消さない・⑷ 4-4 の「素性が判らない檔」と同じ扱い）。
  **事前計算の走行中は 409**（走者が alias を書き戻すため）。これが ⑷ 4-3 の「話者を消す 4 つ」の ⒜〜⒞ を担う。
- **既定 ON は `rocm-*` だけ**＝env `YWK_PRECOMPUTE_ON_START`（**未設定なら変種で決まる＝`rocm-*` は 1・
  `cuda`／`cpu` は 0**・明示すれば両方向に上書き）。ON なら **ready 直後に `all` を 1 回**走らせる。
  口そのものは**変種に依らず在る**（CUDA 機で試したい利用者は叩ける）。
- **優先度は暖機と同じ作法**＝本物の `POST /v1/audio/speech` が走っている間は**次の 1 件の前で待つ**
  （最大 **30 s**・以後は 1 件だけ焼いて再確認）。焼く処理そのものも**上流の合成セマフォ**に並ぶので、
  本物が待たされるのは**最大で 1 件ぶん**（1 件 0.37〜0.90 s＝`40` §6-2）。
  **取消旗は run ごとに別**（暖機は暖機の・事前計算は事前計算の旗を見る）で、**run が終われば下ろす**
  ＝旗は「走っている run への合図」であって処理系の状態ではない。1 本にすると
  ⑴ 暖機を 1 度取り消した後は事前計算が本物にまったく譲らなくなり
  ⑵ 事前計算の `DELETE` が待ちを破れず取消が最大 30 s 遅れる。
- **取消は「次の 1 件を焼かない」ところまで**＝走っている 1 件は最後まで走る（7-2・⑶ 3-4 と同じ粒度）。
  **本物が走っている最中でも `DELETE` は即座に効く**（待ちを破る）。
- **失敗は run を止めない**（**暖機とはここが違う**）。暖機が 1 射目で止まるのは、話者名を間違えれば
  後続の射も全部間違いだから。事前計算の 12 件は**互いに独立**で、1 本の壊れた wav のせいで
  残り 11 名が 1 秒級を払い続ける方が悪い。**最初の理由を `error` に残し、`state` は `failed`** で終える。
- **1 件 1 行のログ**（`id`・`state`・`frames`・`ms`）。焼いた wav は残す（⑷ 4-4）。
- **`/ywk/status.precompute` の形**：

```json
{"state":"idle|running|done|failed|cancelled","id":"…|null","done":0,"total":0,
 "built":0,"reused":0,"skipped":0,"failed":0,"elapsed_s":0.0,
 "last":{"id":"琴葉茜","state":"built|reused|skipped|failed","reason":null,"frames":750,"ms":612.4},
 "error":null}
```

  - `built`＝焼いた／`reused`＝**wav も条件も変わっていないので焼かなかった**（alias だけ確かめる）／
    `skipped`＝**焼く物が無い**（「デフォルト」等の参照なし・`ref_embed`・元の wav を辿れない。
    ただし `force:true` で辿れないものは `failed`＝上記）／
    `failed`＝例外。`done` は**着手した件数**＝4 つの合計。
  - `error` は**絶対パスを畳んだ 1 行**（⑶ 3-3 と同じ規則）。
- **body の検査は⑶ 3-2 と同じ規律**＝未知欄は `ywk_unknown_field`・型違いは `ywk_type_error`・
  `ids` は最大 256 件・空配列や「どちらも無し」は `ywk_out_of_range`。

題目＝`事前計算は202でidとtotalを返す`／`走行中に重ねると409`／`取消は次の1件の前で止まる`／
`本物の要求が来たら次の1件の前で待つ`／`本物が飛んでいても取消は即座に効く`／
`取消旗はrunを跨がない`／`暖機を取り消した後でも事前計算は本物に譲る`／`焼き直しは元wavのsha256で決まる`／
`aliasがreflatentに書き換わりrefwavは残らない`／`一覧のlatent欄`／`cuda変種では既定OFF`／
`1件の失敗が他を巻き込まない`／`知らない話者名は走る前に400`／`bodyの検査`／
`forceで辿れない話者はfailed`／`参照なしはforceでもskipped`／`潜在を外す口`。

---

## ⑻ 版と互換

- **`schema` 番号**＝`/params` の応答に載る（`/ywk/status` には載らない＝本体は `/params` から読む・裁定 104）。**欄を消す・意味を変えるときだけ**上げる。
  欄を足すのは上げない（本体は知らない欄を無視する）。
- **配布版のランチャが `settings.json` に焼く 2 つの表（HTTP の口ではない・本体は読まない・裁定 91・95・97）**＝`runtimeLedgers`（鍵＝変種名・値＝展開に使った台帳 `ledger/runtime-<変種>.json` の sha256・小文字 hex 64 字）と `installedAppVersions`（鍵＝変種名・値＝`v0.1.0` の形）。組んだ変種の欄だけが焼かれる（組んでいない変種の欄は無い）。起動時にいまの変種の欄が配布樹の台帳と食い違えば状態帯に「実行系を組み直す」1 手が出る。`schema` は動かない。
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
| D-5 | `IsMissingVoiceFailure` は文言依存 | 日本語話者名の 400（`voice_id must contain only ASCII…`）を誤検知して 1 回無駄に再登録 | 経路を変えれば消える。**配布版は `error.code` を必ず載せる**（⑶ 3-3）＝未知話者は `ywk_unknown_voice` で判定できる。**`ywk_missing_voice`（`voice` 省略）が出るのは、ランチャが `IRODORI_DEFAULT_VOICE` を空にしたときだけ**（3-1・`decisions.md` 45＝既定では省略しても 200 になる）＝**この code を待つ分岐は常時は走らない** |
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
