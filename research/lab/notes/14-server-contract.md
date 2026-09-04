# 14 — サーバ契約の「無害な実射」（TestClient ＋ 自前 FakeRuntime）

> 席＝第 2 便サブ席（opus）／担当＝Irodori-TTS-Server の実挙動を稼働機に触らず確定する。実施 2026-09-04。
> 稼働中の 8088／7861 は**起動も停止も HTTP 送信もしていない**。GPU 未使用（torch 2.10.0+cpu）。
> upstream・yomiwakechan2・C:/IrodoriTTS・C:/irodori-TTS-server・HF キャッシュは 1 バイトも変更していない（§9）。
> 対象コミット＝`upstream/Irodori-TTS-Server` = `841fb7c`／`upstream/Irodori-TTS` = `8224daf`。
> 逐語データの正本＝`lab/out/server_contract.json`（102 件）。行番号は `grep -n` の実測。

## 要点（10 行以内）

1. **`irodori.no_ref=true` だけで voice は要らない＝200**（1a）。probe の「voice 省略は 400」は no_ref 無しの結果だった。⒞は voice CRUD もダミー voice も不要（BRIEF §4-4／01 ノート D-1 が**確定**）。
2. voice 未指定の 400 文言は逐語 **`'No voice was provided and IRODORI_DEFAULT_VOICE is not set.'`＝前後に単引用符が付く**（`str(KeyError)` の副作用）。未知 voice の文言は**二重引用符付きでサーバの絶対パスを漏らす**。
3. **`IRODORI_ALLOW_NO_REF_VOICE=false` でも `irodori.no_ref=true` は通る**（1h）。false が塞ぐのは `voice:"none"` だけ（1i）＝「no-ref を禁止する」ノブとして機能しない。
4. トップレベルの平坦な別名は**効く**。優先順は **`irodori.X`（非 null）→ トップレベル `X` → env 既定**。ただし**ネストの `false`／`0` はトップレベルより強い**（`_coalesce` は None しか飛ばさない・P1/P2）。
5. **綴り違いは完全な沈黙**（`num_step`・`also_unknown` とも 200・既定値のまま）。さらに**トップレベル経路は `Literal` 検査を素通りする**（`t_schedule_mode:"zigzag"` が 200 でそのままランタイムへ）＝⒞は白名簿検査が必須。
6. 依頼の本文＋`chunk_min_chars=80`＋`first_sentence=1` は **2 チャンク**（`こんにちは。` / 残り全部）。`audio_chunk` が index 0,1 と来て `done{"chunks":2}`。**seed 未指定ならチャンクごとに別 seed**。
7. **`X-Irodori-Seed` は複数チャンク時に 1 本目の seed しか返さない**（app.py:688）＝**その値で再合成しても同じ音は戻らない**。⒞の「seed で先読みキャッシュ」は**チャンク分割 OFF か seed 明示が前提**。
8. **SSE はチャンク粒度で他要求と交互配車になる**（実測 A1,B1,A2,B2,A3,B3）。非ストリームは要求まるごと直列（A1,A2,A3,B1,B2,B3）＝**SSE は初音が早い代わりに、同時 2 本で全体完了が遅れる**。
9. `speed=1.25` → `duration_scale=0.8`（`duration_scale` 併記なら 2.0/1.25=1.6）。`seconds` 明示で**チャンク分割は無効化**され、`seconds` 自身も speed で割られる（5.0→4.0）。
10. デバイスは `_resolve_device` が **auto/空だけ小文字化して判定し、それ以外は原文のまま素通し**＝`"CUDA:1"`・`"rocm"` は**サーバでは通り torch で落ちる**。CPU 機で `resolve_runtime_device("cuda:1")` は `ValueError: CUDA device requested but torch.cuda.is_available() is False.`。

---

## 0. 実験の作り（再現手順）

### 0-1. 環境

lab venv（`lab/.venv`・CPython 3.12.14・11 ノート §2 の箱）に本便が追加したもの：

```bash
uv pip install --python C:/Users/mugonkun/source/repos/irodori-native-research/lab/.venv/Scripts/python.exe \
  fastapi pydantic-settings python-dotenv python-multipart uvicorn
# -> fastapi 0.141.1 / starlette 1.6.0 / pydantic 2.13.5 / pydantic-settings 2.15.0
#    / python-dotenv 1.2.3 / python-multipart 0.0.32 / uvicorn 0.52.4（httpx 0.28.1 は既存）
```

環境変数（`lab/bin/env_server_contract.sh`・11 ノート §10 の一式に Server の src を足しただけ）：

```bash
export PY=C:/Users/mugonkun/source/repos/irodori-native-research/lab/.venv/Scripts/python.exe
export LAB=C:/Users/mugonkun/source/repos/irodori-native-research/lab
export PYTHONPATH="C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS;C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS-Server/src"
export PYTHONDONTWRITEBYTECODE=1   # upstream に __pycache__ を書かない（必須）
export HF_HUB_OFFLINE=1
export PYTHONIOENCODING=utf-8 ; export PYTHONUTF8=1 ; export PYTHONWARNINGS=ignore
export OMP_NUM_THREADS=8 ; export MKL_NUM_THREADS=8
```

### 0-2. 実行

```bash
source "$LAB/bin/env_server_contract.sh"
"$PY" "$LAB/bin/server_contract.py" --out "$LAB/out/server_contract.json"       # 全部（6.0 秒・102 件）
"$PY" "$LAB/bin/server_contract.py" --only 3 --out "$LAB/out/server_contract_q3.json"  # 問い 3 だけ
# --only は 0(追補) C(同時性) P(優先順) 1..9 の文字を並べる
```

### 0-3. ハーネスの作り（`lab/bin/server_contract.py`）

- **pytest は使わない**（`.pytest_cache` を upstream に書かないため）。素の Python スクリプトで `TestClient` を直に叩く。
- 作業ディレクトリを `lab/tmp` に `os.chdir` してから `irodori_openai_tts.app` を import する。
  `Settings` は `env_file=".env"`（config.py:13）＝**カレント相対**なので、`lab/tmp` に `.env` を置かない限り**全既定はコード既定**（config.py:18-72）。本便は `.env` を作らなかった＝表の既定値はすべてコード既定。
- `voices_dir` は `lab/tmp/voices`（空ディレクトリ）に固定。
- **FakeRuntime は upstream の `tests/conftest.py`・`tests/test_api.py:18-58` を読んで自前に書き直した**もの。差分は 2 点だけ＝
  (a) 呼び出しごとの `SamplingRequest` を丸ごと保存する、
  (b) `used_seed` を upstream の実装（`upstream/Irodori-TTS/irodori_tts/inference_runtime.py:1173-1179`）どおり
  「`req.seed is None` なら `secrets.randbits(63)`、さもなくば `int(req.seed)`」で決める
  ＝「チャンクごとに seed が変わるか」を実機なしで再現するため。
- 差し替え方（`tests/conftest.py:10-21` と同じ発想。monkeypatch の代わりに素の `setattr`）＝
  `main.settings.<欄> = ...` ／ `main.runtime_manager = FakeRuntimeManager(fake)` ／
  `main.voice_registry = VoiceRegistry(main.settings)` ／ `main._synthesis_semaphore = None`。
- `TestClient(main.app)` は**コンテキストマネージャで使っていない**＝`lifespan`（app.py:113-116）は走らない＝`startup()` も `preload` も発火しない。
- 音声は `torch.zeros(1, len(text)*10)`・`sample_rate=1000`（Fake）。**音の中身は検分していない**（契約だけの検分）。

**この Fake で確定できないこと**＝実推論の中身（音質・速度・実 seed の効き・参照音声の読み込み・ウォーターマーク）。§10 に列挙。

---

## 1. 問い 1 — voice 省略と `no_ref`

正本＝`lab/out/server_contract_q1.json`（12 件）・`server_contract_qP.json`。

| # | 要求 JSON（`model`/`input` は省略表記） | 状態 | 応答本文／ヘッダ（逐語） |
|---|---|---|---|
| 1a | `{"irodori":{"no_ref":true}}`（voice 無し） | **200** | `audio/wav`・164 B・`RIFF` 始まり。`Content-Disposition: attachment; filename="speech.wav"`／`X-Irodori-Seed: 1157884239768524863`／`X-Irodori-Total-To-Decode: 0.100000`。ランタイムに届いた `SamplingRequest` は `no_ref=True, ref_wav=None, ref_wavs=None, ref_latent=None, ref_embed=None` |
| 1b | `{}`（voice 無し・no_ref 無し） | **400** | `{"error":{"message":"'No voice was provided and IRODORI_DEFAULT_VOICE is not set.'","type":"invalid_request_error","param":null,"code":null}}` |
| 1c | `{"no_ref":true}`（**トップレベル平坦**） | **200** | 1a と同じ（`_extra` 経由・app.py:436-437） |
| 1d | `{"irodori":{"no_ref":false}}` | **400** | 1b と同一文言 |
| 1e | `{"voice":"none"}` | 200 | `no_ref=True` |
| 1f | `{"voice":""}` | **400** | 1b と同一文言 |
| 1g | `{"voice":null}` | **400** | 1b と同一文言 |
| 1h | `allow_no_ref_voice=false` ＋ `{"irodori":{"no_ref":true}}` | **200** | **塞がらない**（レジストリを通らないため） |
| 1i | `allow_no_ref_voice=false` ＋ `{"voice":"none"}` | **400** | `{"error":{"message":"\"Unknown voice='none'. Put a reference audio file in C:\\Users\\...\\lab\\tmp\\voices, add an alias file, or use voice='none'.\"",...}}` |
| 1j | `default_voice="none"` ＋ voice 省略 | **200** | probe の未測項目 U-2 を確定 |
| 1k | `{"voice":{"id":"none"}}` | 200 | |
| 1l | `{"voice":"dare"}` | **400** | 1i と同型（`Unknown voice='dare'. ...`） |
| P5 | `{"voice":{"path":"C:/x.wav"}}` | **400** | 1b と同一文言（`voice.get("id")` しか見ない＝`{}` 扱い） |
| P6 | `{"voice":{"id":"none","ref_wav":"C:/x.wav"}}` | 200 | `ref_wav=None`＝**voice オブジェクトの余分な鍵は完全に無視** |
| 0f | `{"ref_wav":"C:/nowhere/ref.wav"}`（トップレベル・voice 無し） | 200 | `SamplingRequest.ref_wav='C:/nowhere/ref.wav', no_ref=False`＝**voice 解決を丸ごと迂回** |

### 読み取り（断定）

- **`irodori.no_ref=true` は voice を不要にする**（app.py:439-455 の分岐に入る）。トップレベル平坦でも同じ。01 ノート D-1 の「未確認」は**解消**。
- **400 の文言は 2 系統あり、どちらも引用符でくるまれている**。原因＝`voice_registry.resolve` が `KeyError` を投げ（voices.py:77・91-94）、app.py:458-459 が `str(exc)` で detail にするため `repr` が挟まる。
  ⇒ **⒞がこの文字列で分岐するなら、単引用符・二重引用符ごと一致させるか、部分一致で見ること。**
- **未知 voice の 400 はサーバの `voices_dir` 絶対パスを漏らす**（1i・1l）。422 の message も `app.py` の絶対パスと行番号を漏らす（§2-2h）＝**⒞は生のまま利用者に見せてはいけない**（01 ノート D-11 の再確認）。
- `IRODORI_ALLOW_NO_REF_VOICE=false` は **no-ref 合成そのものを禁止できない**。禁止したいなら層側で `irodori.no_ref` を弾く必要がある。

---

## 2. 問い 2 — 平坦な別名・優先順・綴り違い

正本＝`lab/out/server_contract_q2.json`（16 件）・`server_contract_qP.json`（6 件）。
`SamplingRequest` の欄は Fake が受け取った実値。

| # | 要求 JSON（抜粋） | 状態 | ランタイムに届いた値／応答本文 |
|---|---|---|---|
| 2a | `{"num_steps":7}`（トップレベル） | 200 | **`num_steps=7`**＝平坦な別名は効く |
| 2b | `{"num_steps":7,"irodori":{"num_steps":11}}` | 200 | **`num_steps=11`**＝**ネストが勝つ** |
| 2c | `{"num_step":7}`（綴り違い・トップレベル） | **200** | `num_steps=40`（既定）＝**黙って無視** |
| 2d | `{"irodori":{"num_step":7}}`（綴り違い・ネスト） | **200** | `num_steps=40`＝**黙って無視** |
| 2e | `{"caption":"元気な声で","cfg_scale_caption":4.0}` | 200 | `caption='元気な声で'`・`cfg_scale_caption=4.0` |
| 2f | `{"chunking":false,"chunk_min_chars":5}` | 200 | チャンク 1 本＝**トップレベル別名 `chunking` は効く**（app.py:555） |
| 2g | `{"irodori":{"chunking":false,"chunk_min_chars":5}}` | 200 | **チャンク 4 本**＝**ネスト内に `chunking` と書いても効かない**（`_extra` はトップレベルしか見ない・app.py:1082-1086）。同時に書いた `chunk_min_chars:5` の方は正規欄なので効いている |
| 2h | `{"irodori":{"num_steps":"abc"}}` | **422** | `{"error":{"message":"1 validation error:\n  {'type': 'int_parsing', 'loc': ('body', 'irodori', 'num_steps'), 'msg': 'Input should be a valid integer, unable to parse string as an integer', 'input': 'abc'}\n\n  File \"C:\\...\\upstream\\Irodori-TTS-Server\\src\\irodori_openai_tts\\app.py\", line 314, in create_speech\n    POST /v1/audio/speech","type":"invalid_request_error","param":null,"code":null}}` |
| 2i | `{"num_steps":"abc"}`（トップレベル） | **400** | `{"error":{"message":"num_steps must be an integer.",...}}`＝**欄名が入る**（`_as_int`・app.py:1111-1116） |
| 2j | `{"irodori":{"t_schedule_mode":"zigzag"}}` | **422** | `'type': 'literal_error' ... 'msg': "Input should be 'linear' or 'sway'"` |
| 2k | `{"t_schedule_mode":"zigzag"}`（トップレベル） | **200** | **`t_schedule_mode='zigzag'` がそのままランタイムへ**＝`Literal` 検査を回避できてしまう |
| 2l | env `first_sentence=3`・要求は無指定 | 200 | チャンク 2 本（`こんにちは。` / 残り） |
| 2m | env `first_sentence=3` ＋ `{"irodori":{"first_sentence_chunk_min_chars":null}}` | 200 | **チャンク 1 本**＝明示 null が env 既定を打ち消す（`_explicit_option`・app.py:1089-1095） |
| 2m2 | env `first_sentence=3` ＋ トップレベル `first_sentence_chunk_min_chars: null` | 200 | **チャンク 1 本**＝トップレベルの明示 null も効く |
| 2n0 | `{"irodori":"num_steps=7"}`（非オブジェクト） | **422** | `'type': 'model_attributes_type' ... 'Input should be a valid dictionary or object to extract fields from'` |
| 2n | `{"totally_unknown":1,"irodori":{"also_unknown":"x"}}` | **200** | 既定のまま合成される |
| P1 | `{"irodori":{"no_ref":false},"no_ref":true}` | **400** | **ネストの `false` が勝つ**（`_coalesce` は `None` だけを飛ばす・app.py:1149-1153） |
| P2 | `{"chunking":true,"irodori":{"chunking_enabled":false,"chunk_min_chars":5}}` | 200 | **チャンク 1 本**＝ネストの `false` が勝つ |
| P3 | `{"num_steps":7,"irodori":{"num_steps":null}}` | 200 | **`num_steps=7`**＝明示 `null` は「未指定」と同じ扱い＝トップレベルが勝つ |
| P4 | `{"caption":"元気に","irodori":{"caption":null}}` | 200 | `caption='元気に'`＝同上 |
| 0j | `{"speaker_uncond_mode":"zero","irodori":{"speaker_uncond_mode":"zero"}}` | 200 | **`speaker_uncond_mode='mask'`**（既定のまま）＝サーバは渡さない唯一の欄（06 ノート §1-7 を実射で確認） |

### 結論表（優先順）

```
irodori.X が非 null      →  それを採る
irodori.X が null / 未指定 →  トップレベル X（extra）を見る
両方 null / 未指定        →  env 既定（settings.default_X）
```

例外は `_explicit_option` の 3 欄（`max_ref_seconds`・`ref_normalize_db`・`first_sentence_chunk_min_chars`）で、
ここだけ **「明示 null」と「未指定」を区別**し、明示 null は **env 既定を打ち消して None にする**（2m・2m2 で実測）。

### 罠（⒞の設計に直結・断定）

1. **綴り違いは 4 通りとも沈黙**（トップレベル／ネスト × 未知欄／似た名前）。⒞は**許可欄の白名簿**を持ち、送る前に弾くしかない。
2. **トップレベル経路は pydantic の型・`Literal` 検査を通らない**（2k）。`_as_int`/`_as_float` が掛かる欄だけ 400 になり、`str(...)` で受ける欄（`t_schedule_mode`・`decode_mode`・`cfg_guidance_mode`）は**素通しでランタイムへ行く**。⇒ **⒞は `Literal` 3 欄を必ず `irodori` ネストに入れて送る**べき（サーバに検査させるため）。
3. **`false`／`0` はネストが強く、`null` はネストが弱い**（P1〜P4）。⒞が「既定に戻す」を表現するなら **`irodori` 側に `null` を置く**（＝トップレベルにフォールバックする）か、**`_explicit_option` の 3 欄だけは `irodori` に `null` を書く**（＝env 既定を打ち消す）。**同じ `null` が欄によって逆の意味を持つ**点に注意。
4. **`irodori` を非オブジェクトで送ると 422**、未知欄は 200＝**エラーの粒度がばらばら**（400／422／沈黙の 3 態）。

---

## 3. 問い 3 — SSE の分割・イベント列・seed

正本＝`lab/out/server_contract_q3.json`（6 件）。
本文（依頼指定・41 字）＝`こんにちは。今日は良い天気ですね、散歩に行きましょう。それから買い物もしましょう。`

### 3a（本命ケース）要求 JSON（逐語）

```json
{"model":"irodori-tts","input":"こんにちは。今日は良い天気ですね、散歩に行きましょう。それから買い物もしましょう。",
 "voice":"none","stream_format":"sse",
 "irodori":{"chunking_enabled":true,"chunk_min_chars":80,"first_sentence_chunk_min_chars":1}}
```

**応答＝200**／ヘッダ `content-type: text/event-stream; charset=utf-8`・`cache-control: no-cache`・`x-accel-buffering: no`
（**`X-Irodori-Seed` も `X-Irodori-Total-To-Decode` も付かない**＝SSE 経路にはヘッダが無い）

**イベント列（逐語・`audio_base64` だけ復号後のサイズに置換）**

```
event: audio_chunk
data: {"index":0,"text":"こんにちは。","format":"wav","media_type":"audio/wav",
       "audio_base64":<164 bytes, RIFF>,"seed":216565751011936164,"total_to_decode":0.1}

event: audio_chunk
data: {"index":1,"text":"今日は良い天気ですね、散歩に行きましょう。それから買い物もしましょう。",
       "format":"wav","media_type":"audio/wav",
       "audio_base64":<744 bytes, RIFF>,"seed":6094888012671332292,"total_to_decode":0.1}

event: done
data: {"chunks":2}
```

**＝2 個・index 0→1 の順。`text` は分割後の本文そのもの。各チャンクは完結した wav（`RIFF` 始まり）。**

分割の理由＝`first_sentence_chunk_min_chars=1` が**先頭の境界文字 1 回だけ**適用され（app.py:624-627）、
`こんにちは。` の 6 字 ≥ 1 で切れる。以降は `min_chars=80` に戻り、残り 34 字は 80 に届かないので tail として 1 本にまとまる。

### seed（依頼の核心）

| ケース | `SamplingRequest.seed`（Fake が受け取った値） | SSE の `seed`（＝`used_seed`） |
|---|---|---|
| 3a（seed 未指定） | `[None, None]` | `[216565751011936164, 6094888012671332292]`＝**チャンクごとに別** |
| 3b（`irodori.seed=1234`） | `[1234, 1234]` | `[1234, 1234]`＝**全チャンク同一** |
| 3c（`chunk_min_chars=10`・seed 未指定） | `[None, None, None, None]` | 4 本すべて別 |

- サーバは `replace(sampling_request, text=chunk)`（app.py:710）で**text だけ差し替える**＝`seed` 欄は各チャンクで同じ値（未指定なら `None`）のまま渡る。
- **`None` を受けたランタイムが毎回新しい乱数 seed を引く**（`upstream/Irodori-TTS/irodori_tts/inference_runtime.py:1173-1179`・逐語）：
  ```python
  if req.seed is None:
      used_seed = int(secrets.randbits(63))
      msg = f"info: seed not specified; using random seed {used_seed}."
  ```
  ⇒ **seed 未指定＝チャンクごとに seed が変わる**（断定。Fake は上記規則をそのまま写して再現した）。

### 3c（`chunk_min_chars=10` ＋ `first_sentence=1`）＝4 チャンク

`["こんにちは。", "今日は良い天気ですね、", "散歩に行きましょう。", "それから買い物もしましょう。"]`
（index 0〜3・`done{"chunks":4}`）

### 3d（**非ストリームで同じ分割**）＝⒞に効く落とし穴

要求 `{"irodori":{"chunk_min_chars":80,"first_sentence_chunk_min_chars":1}}`（`stream_format` 無し）→ **200・864 B の連結 wav**。

```
Content-Disposition: attachment; filename="speech.wav"
X-Irodori-Seed: 918251886266776451
X-Irodori-Total-To-Decode: 0.200000
```

Fake の記録＝`used_seeds = [918251886266776451, 4372707742979687144]`。
⇒ **`X-Irodori-Seed` は 1 本目の seed だけ**（app.py:688 `used_seed=results[0].used_seed`）。
**この値を送り返しても同じ音は再現しない**（2 本目は別 seed で合成済み）。
`X-Irodori-Total-To-Decode` は**合計**（0.1+0.1=0.2）。

### 3e / 3f（`stream_format` の受理）

| 要求 | 状態 | 本文 |
|---|---|---|
| `"stream_format":"SSE"`（大文字） | **200・SSE** | `_stream_format_is_sse` が `strip().lower()` する（app.py:399） |
| `"stream_format":"audio"` | **400** | `{"error":{"message":"stream_format must be 'sse' when specified.","type":"invalid_request_error","param":null,"code":null}}` |

### 3 の追補（`lab/out/server_contract_q0.json`）

- **0b/0c＝SSE でも検証は先に走る**。`stream_format:"sse"` ＋ 未知 voice → **SSE ではなく JSON の 400**（`content-type: application/json`）。model 不一致も同じ。
  ⇒ **⒞は「SSE を頼んだのに JSON が返る」ケースを必ず扱うこと**（`Accept: text/event-stream` を見て分岐してはいけない）。
- **0h＝`chunking_enabled:false` なら長文でも `audio_chunk` は 1 個**（`done{"chunks":1}`）。
- **0g＝複数チャンクの `X-Irodori-Messages` はチャンク数ぶん連結される**（`" | "` 区切り・3 チャンクなら同じ警告が 3 回並ぶ）。

---

## 4. 問い 4 — `_split_text_for_speech` の分割規則（直接実測）

app.py:606-639 の純関数を HTTP を介さず直接呼んだ（`lab/out/server_contract.json` の `4 _split_text_for_speech 直接呼び出し`）。
境界文字＝`CHUNK_BOUNDARIES = frozenset("。、，,．.!！?？\n\r")`（app.py:30・逐語）。

| # | 入力 | 字数 | `min_chars` | `first` | 結果（n＝チャンク数） |
|---|---|---|---|---|---|
| 4-1 | `あいうえお、かきくけこ、さしすせそ、たちつてと`（読点だけ） | 23 | 5 | — | **n=4**：`["あいうえお、","かきくけこ、","さしすせそ、","たちつてと"]` |
| 4-2 | 同上 | 23 | 80 | — | **n=1**：原文まるごと |
| 4-3 | 同上 | 23 | 5 | 1 | n=4（4-1 と同じ。先頭がすでに 5 字超なので差が出ない） |
| 4-4 | 本文（§3 の 41 字） | 41 | 80 | — | **n=1** |
| 4-5 | 同上 | 41 | 80 | **1** | **n=2**：`["こんにちは。","今日は良い天気ですね、散歩に行きましょう。それから買い物もしましょう。"]` |
| 4-6 | 同上 | 41 | 10 | — | **n=3**：`["こんにちは。今日は良い天気ですね、","散歩に行きましょう。","それから買い物もしましょう。"]` |
| 4-7 | `第一行です\n第二行です\r\n第三行です` | 18 | 80 | — | **n=1**（改行を含んだまま 1 本） |
| 4-8 | 同上 | 18 | 3 | — | **n=3**：`["第一行です","第二行です","第三行です"]`（**改行は `strip()` で消える**） |
| 4-9 | 同上 | 18 | 3 | 1 | n=3（同上） |
| 4-10 | `句点のない文` | 6 | 1 | — | **n=1**（境界文字が 1 つも無い＝tail のみ） |
| 4-11 | `あ 、い 、う 、え 、お 。`（空白入り） | 15 | 5 | — | **n=2**：`["あ 、い 、う 、","え 、お 。"]` |
| 4-12 | `あいうえお、`×20 | 120 | 80 | — | **n=2**：84 字＋36 字 |

### 規則（断定）

1. 1 文字ずつ走査し、**空白でない字だけを数える**（`if not char.isspace(): current_chars += 1`・app.py:619-620）。
   ⇒ 4-11 のとおり**空白は閾値に効かないがチャンクには残る**（末尾は `strip()` される）。
2. 境界文字に当たった時点で `current_chars >= min_chars` なら切る。**境界文字はチャンクの末尾に含まれる**（`。` や `、` が付いたまま）。
3. `first_sentence_min_chars` は **最初に境界文字へ当たった 1 回だけ** `min_chars` の代わりに使われる（`use_first_sentence_min` は無条件に `False` になる・app.py:625-626）。
   ⇒ **「切れたかどうか」に関わらず 1 回で使い切る**。4-3 で差が出ないのはそのため。
4. 残りは tail として 1 チャンク。すべて空なら `[text]`（原文 1 本）を返す（app.py:639）。
5. `\r\n` は境界が 2 つ連続する形になるが、間の `\n` は空白なので `current_chars=0` のまま切られず、次のチャンクの先頭に付いて `strip()` で消える（4-8）。

**⒞への含意**＝「初音を早くする」ノブは `first_sentence_chunk_min_chars=1`（＋短い本文の先頭に必ず `。`／`、` があること）。
**境界文字が 1 つも無い長文は分割されない**（4-10 の系）＝コメント読み上げのように句読点が無い入力では**チャンク分割が効かず、初音が遅い**。⒞側で句読点を補うか、自前で分割して複数リクエストにする必要がある。

---

## 5. 問い 5 — `speed` と `seconds`

正本＝`lab/out/server_contract_q45.json`。

| # | 要求 JSON（抜粋） | 状態 | ランタイムに届いた値 |
|---|---|---|---|
| 5a | `{"speed":1.25}` | 200 | **`duration_scale=0.8`**・`seconds=None` |
| 5b | `{"speed":1.25,"irodori":{"duration_scale":2.0}}` | 200 | **`duration_scale=1.6`**（2.0 ÷ 1.25） |
| 5c | `{"speed":1.0,"irodori":{"duration_scale":2.0}}` | 200 | `duration_scale=2.0`（`payload.speed != 1.0` の分岐に入らない・app.py:843-844） |
| 5d | `{"speed":5.0}` | **422** | `'type': 'less_than_equal', 'loc': ('body','speed'), 'msg': 'Input should be less than or equal to 4', 'ctx': {'le': 4.0}` |
| 5e | `{"irodori":{"seconds":5.0,"chunk_min_chars":10}}` ＋ 41 字本文 | 200 | **チャンク 1 本**（`chunk_min_chars=10` なら本来 3 本）＝**`seconds` 明示で分割が無効化**（app.py:562-564） |
| 5f | `{"speed":1.25,"irodori":{"seconds":5.0}}` | 200 | **`seconds=4.0`**（5.0 ÷ 1.25）・`duration_scale=0.8`＝**両方が speed で割られる** |
| 5g | `{"irodori":{"duration_scale":0}}` | **400** | `{"error":{"message":"duration_scale must be greater than 0.",...}}` |

**⒞への含意**＝読み分けちゃんの「速度」欄を `speed` に写すのは正しいが、
**同じ要求で `duration_scale` も送ると割り算で合成される**（5b）。両方を露出するなら層で排他にすること。
**`seconds` を使うと chunking が黙って死ぬ**（5e）＝長文＋`seconds` は 1 発の大形状合成になり、初音が最も遅くなる。

---

## 6. 問い 6 — デバイス解決（CPU 機での実測）

機体＝`torch 2.10.0+cpu`／`torch.cuda.is_available() = False`／`torch.version.cuda = None`／`torch.version.hip = None`。

### 6-1. `RuntimeManager._resolve_device`（`runtime.py:97-102`）

| 入力 | 戻り |
|---|---|
| `"cuda:1"` | **`"cuda:1"`**（そのまま） |
| `"auto"` | **`"cpu"`** |
| `""` | **`"cpu"`** |
| `"AUTO"` | `"cpu"` |
| `"  auto  "` | `"cpu"` |
| **`"CUDA:1"`** | **`"CUDA:1"`**（**大文字のまま素通し**） |
| `"cpu"` / `"cuda"` / `"mps"` | 同名をそのまま |
| **`"rocm"`** | **`"rocm"`**（そのまま素通し） |

**＝小文字化されるのは「auto／空かどうか」の判定だけで、戻り値は `str(value)` の原文**（runtime.py:99-102）。
CPU しか無い機で `auto` が `cpu` に落ちることを実射で確認（`list_available_runtime_devices()` の先頭・inference_runtime.py:80-93）。

### 6-2. `inference_runtime.resolve_runtime_device`（`inference_runtime.py:55-77`）— CPU 機での戻り／例外（逐語）

| 入力 | 結果 |
|---|---|
| `"cuda:1"` | **`ValueError: CUDA device requested but torch.cuda.is_available() is False.`** |
| `"cuda"` | 同上（**index の有無で文言は変わらない**） |
| `"cpu"` | `cpu` |
| `"cpu:0"` | `cpu:0`（**受かる**） |
| `"CUDA:1"` | `RuntimeError: Expected one of cpu, cuda, ipu, xpu, mkldnn, opengl, opencl, ideep, hip, ve, fpga, maia, xla, lazy, vulkan, mps, meta, hpu, mtia, privateuseone device type at start of device string: CUDA` |
| `"mps"` | `ValueError: MPS device requested but torch.backends.mps.is_available() is False.` |
| `"mps:0"` | `ValueError: MPS device index is not supported. Use 'mps'.`（**index の検査が先**） |
| `"xpu"` | `ValueError: XPU device requested but torch.xpu.is_available() is False.` |
| `"xpu:0"` | `ValueError: XPU device index is not supported. Use 'xpu'.` |
| `"rocm"` | `RuntimeError: Expected one of cpu, cuda, ... : rocm` |
| `"gpu"` | `RuntimeError: Expected one of cpu, cuda, ... : gpu` |
| `""` | **`RuntimeError: Device string must not be empty`** |
| `"auto"` | `RuntimeError: Expected one of cpu, cuda, ... : auto` |

### 読み取り（断定）

1. **`IRODORI_MODEL_DEVICE=cuda:1` はサーバ層を素通りし、実際の可否は torch が決める**。CPU 機では `ValueError`（上表）。
   `RuntimeManager.get()` の中で起きるので、**ラップされずに `unhandled_exception_handler` へ行き 500**（`preload=true` なら**起動そのものが落ちる**・app.py:106-108 は例外を握らない）。
2. **`"CUDA:1"`（大文字）・`"rocm"`・`"gpu"` は「もっともらしい綴り間違い」だが、サーバでは検査されず torch の `RuntimeError` になる**。
   ⇒ **⒞のインストーラ／ランチャは `IRODORI_MODEL_DEVICE` の値を自前で検査すべき**（`cpu` / `cuda` / `cuda:N` / `mps` / `xpu` / `auto` / 空 の白名簿）。ROCm でも値は **`cuda`**（06 ノート §2-3・compose.rocm.yaml:12-13）。
3. `""`（空文字）は `_resolve_device` が `auto` と同じに畳むので**サーバ経由なら安全**。危ないのは `resolve_runtime_device` を直に呼ぶ経路（⒝の自前ローダ等）。
4. `cpu:0` は受かる＝**`cpu` に index を付けても落ちない**（無害）。

---

## 7. 問い 7 — `/health` の応答 JSON 全欄

`GET /health`・**認証不要**（`api_key="secret"` を設定しても 200・7c）。**モデルを読まない**（本便は本物の `RuntimeManager` を未ロードのまま置いて実射）。

### 7a（全既定・未ロード）応答本文（逐語）

```json
{"status":"ok",
 "model":{"id":"irodori-tts","hf_checkpoint":"Aratako/Irodori-TTS-v4-Small",
          "model_device":"auto","codec_device":"auto",
          "model_precision":"fp32","codec_precision":"fp32",
          "compile_model":false,"compile_dynamic":false},
 "runtime":{"preload":false,"loaded":false,"loading":false,"checkpoint":null,
            "load_timeout":300.0,"max_concurrent_synthesis":1,"synthesis_wait_timeout":300.0},
 "voices":{"dir":"C:\\Users\\mugonkun\\source\\repos\\irodori-native-research\\lab\\tmp\\voices",
           "dir_exists":true,"files":0},
 "defaults":{"response_format":"wav","chunking_enabled":true,
             "chunk_min_chars":80,"first_sentence_chunk_min_chars":null}}
```

### 7b（`model_device="cuda:1"`・`codec_device="cpu"`・`model_precision="bf16"` を設定した CPU 機）

```json
"model":{"id":"irodori-tts","hf_checkpoint":"Aratako/Irodori-TTS-v4.1-Small",
         "model_device":"cuda:1","codec_device":"cpu",
         "model_precision":"bf16","codec_precision":"fp32",
         "compile_model":false,"compile_dynamic":false}
```

**＝`model_device` は `settings.model_device` の素の echo**（app.py:173）。`auto` は `auto` のまま返り、
**解決後の実デバイス（`cpu` / `cuda:0`）は `/health` からは分からない**。CPU 機で `cuda:1` と設定しても `/health` は 200 を返す。

### 7d（ロード済みを模した状態）

`runtime.loaded=true`・`runtime.checkpoint="C:/fake/model.safetensors"`。
**`checkpoint` は `RuntimeManager._checkpoint_path`＝ロードした瞬間に埋まる**（runtime.py:46・68-70）＝未ロードなら `null`。

### 7e / 7f（対照）

- `GET /v1/models` → `{"object":"list","data":[{"id":"irodori-tts","object":"model","created":0,"owned_by":"irodori-tts"}]}`
- `GET /v1/audio/voices`（voices ディレクトリが空）→ `{"object":"list","data":[{"id":"none","object":"voice","ref_wav":null,"ref_wavs":null,"ref_latent":null,"ref_latents":null,"ref_embed":null,"no_ref":true}]}`

### ⒞への含意（断定）

- **ready 判定＝`/health` の `runtime.loaded`**。認証不要なので鍵の設定前でもポーリングできる。
- **`model_device` の echo は「設定値」であって「実際に載ったデバイス」ではない**。
  ⇒ **⒞が「本当に GPU で動いているか」を確かめる手段は `/health` には無い**。
  版（v4 / v4.1）は `model.hf_checkpoint`、実デバイスは**ログ（`logger.info("runtime loaded in %.2fs")` の前後）か合成速度からの推定**しかない＝**未確認の穴**として §10 に残す。
- `runtime.checkpoint` が `null` から実パスに変わることが**ロード完了の第二の目印**になる。

---

## 8. 問い 8 — 例外の応答形

正本＝`lab/out/server_contract_q8.json`（17 件）。エラー本体はすべて `{"error":{"message","type","param","code"}}`（app.py:1156-1167）で **`param`・`code` は非ストリーム経路で常に `null`**。

### 8-1. 合成中に例外が飛んだとき（Fake が投げる）

| 例外 | 状態 | 応答本文（逐語） |
|---|---|---|
| `ValueError("bad reference")` | **400** | `{"error":{"message":"bad reference","type":"invalid_request_error","param":null,"code":null}}` |
| `FileNotFoundError("no such file: C:/x/y.wav")` | **400** | `{"error":{"message":"no such file: C:/x/y.wav","type":"invalid_request_error",...}}` |
| `RuntimeError("Dynamic LoRA loading is not compatible with compile_model=True.")` | **400** | 同文言・`invalid_request_error` |
| `RuntimeError("kaboom")` | **500** | `{"error":{"message":"kaboom","type":"server_error","param":null,"code":null}}` |
| `torch.cuda.OutOfMemoryError("CUDA out of memory.")` | **500** | `{"error":{"message":"CUDA out of memory.","type":"server_error",...}}` |
| `RuntimeLoadTimeoutError(...)` | **503** | `{"error":{"message":"Model is still loading. Retry after a moment. timeout=300.0s","type":"server_error","param":null,"code":null}}` |

**注意**＝`RuntimeError` は**メッセージの部分一致（`"Dynamic LoRA loading is not compatible" in str(exc)`・app.py:362）だけ**が 400 に落ち、
**それ以外はすべて 500 で例外文がそのままクライアントに出る**。OOM も 500＝**⒞は 500 の message を利用者にそのまま見せない**こと。
（500 の観測には `TestClient(..., raise_server_exceptions=False)` が要る＝実サーバでは JSON が返る。）

### 8-2. SSE 経路（**HTTP は常に 200**）

| 例外 | 状態 | イベント（逐語） |
|---|---|---|
| `ValueError("bad reference")` | **200** | `event: error` ／ `data: {"error":{"message":"bad reference","type":"invalid_request_error","param":null,"code":"invalid_request"}}` |
| `RuntimeError("kaboom")` | **200** | `data: {"error":{"message":"kaboom","type":"server_error","param":null,"code":"stream_error"}}` |
| `RuntimeLoadTimeoutError(...)` | **200** | `data: {"error":{"message":"Model is still loading. Retry after a moment. timeout=300.0s","type":"server_error","param":null,"code":"runtime_unavailable"}}` |

**＝SSE では `code` に分類名が入る唯一の経路**（非ストリームは常に `null`）。`done` は流れない。

### 8-3. 推論に届く前に弾かれるもの（Fake の呼び出し回数 0 で確認）

| 要求 | 状態 | 本文（逐語） |
|---|---|---|
| `model:"gpt-4o-mini-tts"` | 400 | `Unsupported model 'gpt-4o-mini-tts'. Use 'irodori-tts'.` |
| `input:"   "` | 400 | `input must contain non-whitespace text.` |
| `input:""` | **422** | `'type': 'string_too_short' ... 'ctx': {'min_length': 1}` ＋**`app.py` の絶対パスと行番号** |
| `input` 4096 字 | **200** | 上限ちょうどは通る |
| `input` 4097 字 | **422** | `'type': 'string_too_long' ... 'String should have at most 4096 characters'`（**`input` に本文全体がそのまま載る**＝メッセージが 4KB 超になる） |
| `response_format:"ogg"` | 400 | `Unsupported response_format='ogg'. Expected one of: aac, flac, mp3, opus, pcm, wav.` |
| `irodori.caption:""` | 400 | `caption must be non-empty when specified.` |
| `no_ref` ＋ `ref_wav` 併用 | 400 | `no_ref cannot be combined with a reference input.` |
| `irodori.chunk_min_chars:0` | 400 | `chunk_min_chars must be greater than 0.` |

### 8-4. `X-Irodori-Messages`

`messages` が非空のときだけ現れる。1 チャンク＝`warning: SilentCipher watermark is unavailable; skipping.`、
3 チャンク＝同じ文が `" | "` で 3 つ連結（0g）。**空なら欄ごと出ない**。

---

## 9. 問い 9 — 認証（`IRODORI_API_KEY` 設定時）

正本＝`lab/out/server_contract_q9.json`（13 件）。`api_key="secret"` を設定して `/v1/audio/speech` を叩いた。

| `Authorization` ヘッダ | 状態 |
|---|---|
| （無し） | **401** |
| `Bearer secret` | **200** |
| `bearer secret`（小文字 b） | **401** |
| `BEARER secret` | **401** |
| `Bearer  secret`（空白 2 個） | **401** |
| `Bearer secret `（末尾空白） | **401** |
| ` Bearer secret`（先頭空白） | **401** |
| `Bearer wrong` | **401** |
| `secret`（素の値） | **401** |
| `Token secret` | **401** |

**401 の応答本文（逐語・全ケース共通）**

```json
{"error":{"message":"Invalid API key.","type":"invalid_request_error","param":null,"code":null}}
```

**401 の全ヘッダ（実測）**＝`{"content-length":"96","content-type":"application/json"}`
⇒ **`WWW-Authenticate` は付かない**（HTTP の作法としては不完全だが、⒞には影響しない）。

- `require_auth`（app.py:135-140）は `authorization != f"Bearer {api_key}"` の**完全一致**＝**大小・空白の揺れを一切許さない**（断定）。
- **`/health` だけは `api_key` 設定時でもヘッダ無しで 200**（7c）＝**依存に `require_auth` を持たない唯一のルート**（app.py:165）。
- `api_key` 未設定なら**間違ったヘッダを送っても素通り**（200）。

---

## 10. 主席への注意

### 10-1. この実射で**確定**した契約（⒞の薄い制御層を建てるときの前提にしてよい）

| # | 確定事項 | 効き方 |
|---|---|---|
| **T-1** | **`irodori.no_ref=true` だけで voice は不要**（voice CRUD も `voices/` へのダミー配置も要らない） | 01 ノート D-1 の「1 射で確定させる価値が最も高い未確認事項」を**解消**。⒞の初期設定手順が 1 段減る |
| **T-2** | **優先順は `irodori.X`（非 null）→ トップレベル `X` → env**。ただし**ネストの `false`/`0` は勝ち、ネストの `null` は負ける** | ⒞は `irodori` ネストで統一して送るのが安全。「既定に戻す」は `_explicit_option` の 3 欄だけが表現できる |
| **T-3** | **綴り違い・未知欄は 4 通りとも 200 で沈黙。トップレベル経路は `Literal` 検査も回避する** | **⒞に白名簿検査が必須**。とくに `t_schedule_mode`・`decode_mode`・`cfg_guidance_mode` は**必ず `irodori` ネストに入れる**（サーバに検査させる） |
| **T-4** | **`X-Irodori-Seed` は複数チャンク時に 1 本目しか返さない＝その seed で再現できない** | ⒞の「seed 固定で先読みキャッシュ」は **(a) `chunking_enabled:false` か (b) `irodori.seed` を必ず明示** のどちらかが前提。**放置すると「同じ文なのに毎回違う音」になる** |
| **T-5** | **SSE はチャンク粒度で他要求と交互配車される**（実測 A1,B1,A2,B2,A3,B3／非ストリームは A1,A2,A3,B1,B2,B3） | ⒞の「先読み」で 2 本目を投げると、**1 本目の後続チャンクが割り込まれて遅くなる**。順序を守りたいなら**⒞側で 1 本ずつ直列に投げる**（サーバ任せにしない） |
| **T-6** | **境界文字（`。、，,．.!！?？\n\r`）が無い本文は分割されない** | 句読点なしのコメントは初音が遅い。⒞側で分割するか句読点を補う |
| **T-7** | **`seconds` 明示で chunking が黙って無効化**／`speed` と `duration_scale` は掛け算（除算）になる | 「声の値」欄に両方出すなら排他にする |
| **T-8** | **エラーは 400／422／500／503／SSE の `event: error` の 5 態。422 と未知 voice の 400 はサーバの絶対パスを漏らす** | ⒞は 5 態すべてを扱い、**message を生のまま利用者に出さない**（ログには残す） |
| **T-9** | **`/health` は認証不要・モデルを読まない。`runtime.loaded` が ready 判定、`runtime.checkpoint` が第二の目印** | ⒞の起動待ちはこれで足りる |
| **T-10** | **`_resolve_device` は auto/空以外を原文素通し**。`"CUDA:1"`・`"rocm"`・`"gpu"` はサーバで検査されず torch の例外になる（`preload=true` なら起動ごと落ちる） | ⒞のランチャが `IRODORI_MODEL_DEVICE` を自前で検査すべき（白名簿＝`auto`/空/`cpu`/`cuda`/`cuda:N`/`mps`/`xpu`。**ROCm も値は `cuda`**） |
| **T-11** | **`IRODORI_ALLOW_NO_REF_VOICE=false` は no-ref 合成を禁止できない**（`voice:"none"` を塞ぐだけ） | ノブの説明を誤らないこと |
| **T-12** | **SSE を頼んでも検証段では JSON の 400 が返る**（未知 voice・model 不一致など） | ⒞の SSE クライアントは「最初の応答が `text/event-stream` でない」場合を必ず扱う |

### 10-2. **なお未確認**（実機 GPU／実推論でしか分からないもの）

| # | 未確認事項 | なぜ本便で決められないか |
|---|---|---|
| U-1 | **`irodori.seed` を固定したとき、チャンク分割しても各チャンクが決定的に再現するか** | Fake は seed を素通しするだけ。実推論の決定性は probe で seed 固定の SHA256 一致が出ている（`yomiwakechan2/probe/irodori-tts-determinism-raw.txt:2-3`）が、**チャンク分割込みの再現は未測** |
| U-2 | **`/health` から「実際に載ったデバイス」を知る手段が無い**（`model_device` は設定値の echo） | ⒞が「本当に GPU か」を確認したいなら**ログか合成速度からの推定**しかない。実機で確認する余地 |
| U-3 | **`cuda:1` を実 NVIDIA 機に指定したときの挙動**（index の範囲外・複数 GPU の実測） | 本機に NVIDIA なし（BRIEF §2-3）。CPU 機では `torch.cuda.is_available()=False` で先に落ちる |
| U-4 | **クライアント切断時に非ストリーム経路のハンドラが実際に cancel されるか**（06 ノート §5-3 の窓） | TestClient は切断を再現できない。実サーバ＋実クライアントが要る |
| U-5 | **`aac` 出力**＝実音声（1 秒 48 kHz）では **ffmpeg 経由で 9,332 B・78 ms** かかることを別途確認したが、Fake の極端に短い音声（20 サンプル・1000 Hz）では**200 で 0 バイトが返った**。実音声の短文（0.5 秒未満）で同じことが起きるかは**未確認** | `wav`/`flac`/`pcm` は soundfile だけで完結し安全（06 ノート §4-1）。**⒞は wav 固定でよい** |
| U-6 | **`IRODORI_CORS_ORIGINS` の環境変数記法**・`IRODORI_CODEC_DETERMINISTIC_*` が実際に効くか | 06 ノート §9-7 のまま持ち越し（本便は `.env` を作らずコード既定で走らせた） |
| U-7 | **同時 2 本の交互配車が実推論でも同じ順になるか** | Fake は 50 ms の `sleep` で見立てた。実推論は `_infer_lock`（inference_runtime.py:620）でさらに直列化されるため、**セマフォの取り合いの順序は実機で変わりうる**（ただし「SSE はチャンクごとにスロットを取り直す」構造は app.py:711-719 の実装事実＝交互配車は起こりうる） |
| U-8 | **`num_candidates>1`** が 4 として渡ることは確認したが（0i）、**返るのが 0 本目だけ**という 06 ノート §1-7 の記述は実推論でしか確かめられない | Fake は candidates を無視する |

### 10-3. 稼働機・停止域について

- **8088／7861 には一切触れていない**（起動・停止・HTTP 送信のいずれも無し）。すべて `TestClient`（プロセス内 ASGI 呼び出し）と `httpx.ASGITransport` で完結。
- 外部へ何も送っていない（`HF_HUB_OFFLINE=1`・モデルは 1 バイトも読んでいない＝Fake のため）。
- **例外**＝`response_format="aac"` の検分で**ローカルの `ffmpeg.exe` を子プロセスとして 2 回起動した**（`C:\Users\mugonkun\AppData\Local\...\ffmpeg-9.0.1-full_build\bin\ffmpeg.EXE`）。
  一時ディレクトリに中間 wav を書いて消すだけ（audio.py:140-168）＝副作用なし・ネットワークなし。

---

## 11. 無改変の証明

```bash
git -C C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS-Server status --short --ignored
# -> 出力なし（clean）
git -C C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS status --short --ignored
# -> 出力なし（clean）
find upstream -name "__pycache__" -o -name ".pytest_cache"
# -> 出力なし（PYTHONDONTWRITEBYTECODE=1 のため）
```

- 本便が書いたのは **`lab/bin/server_contract.py`・`lab/bin/env_server_contract.sh`・`lab/out/server_contract*.json`（12 檔）・`lab/tmp/voices/`（空）・本ノート**のみ。
- `lab/tmp` の他の檔（`*.cs`・`*.npz`・`inspect_tok.py`）は**別席の作業物**＝本便は触っていない。
- lab venv に追加したのは §0-1 の 10 パッケージ（fastapi 系）。既存の torch/onnx 系は変更していない。
- `lab/tmp` に `.env` は**作らなかった**＝本ノートの既定値はすべて `config.py:18-72` のコード既定（`.env.example` と一致する欄／しない欄は 01 ノート §3.4 を参照）。
