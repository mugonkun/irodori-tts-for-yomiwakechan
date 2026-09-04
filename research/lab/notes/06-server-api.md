# 06 — OpenAI 互換サーバ（Irodori-TTS-Server）読解ノート

対象コミット＝`841fb7c6ec57729c56b9b75c0ef2562249b13a10`（`origin https://github.com/Aratako/Irodori-TTS-Server.git` / `.git/packed-refs`・`git log --oneline -3` で確認）。
読み方＝`upstream/Irodori-TTS-Server/**` は読むだけ。行番号は `cat -n` / `sed -n` の実測。

## 要点（10 行以内）

1. 推論パラメータは **SamplingRequest の 43 欄中 42 欄が HTTP に露出**。固定は `speaker_uncond_mode`（既定 `"mask"`）1 欄のみ＝⒞の「感情パラメータ露出」は**サーバ改造なしで満点**。
2. 露出口は入れ子 `irodori` オブジェクト＋**トップレベル別名**の二重。両方 `extra="allow"`＝未知欄は 400 にならず**黙って無視**される（typo が沈黙する）。
3. デバイスは**プロセス起動時の環境変数のみ**（`IRODORI_MODEL_DEVICE`／`IRODORI_CODEC_DEVICE`）。要求ごとの GPU 指定は無い。`auto`＝`cuda`→`mps`→`xpu`→`cpu` の先頭。
4. **`cuda:1` 形式は受かる**（`torch.device()` にそのまま渡り index を保持）。ただし index の範囲検査は無い＝存在しない index は torch 側で落ちる。**ROCm も `cuda` と名乗る**（compose.rocm.yaml が `IRODORI_MODEL_DEVICE: cuda`）。
5. モデルは**プロセスに 1 つだけ**（`RuntimeManager._runtime` に固着・`unload` 経路なし）＝**複数 GPU を使うにはプロセスを複数立てる**しかない。
6. **キャンセル経路が無い**。`create_speech` は `Request` を取らず `is_disconnected()` も呼ばない。ワーカースレッドは切断後も最後まで回る。
7. 同時実行は 2 段の直列化。サーバの `asyncio.Semaphore`（既定 1）＋**ランタイム内部の `threading.Lock`**＝`MAX_CONCURRENT_SYNTHESIS` を上げても実際には並列化しない（先読みは「待たせるだけ」）。
8. ストリーミングは **SSE（`stream_format:"sse"`）でチャンク単位・base64 の完全音声ファイル**。HTTP chunked の生 PCM 流しではない。進捗イベントは無い（`audio_chunk` と `done` のみ）。
9. 出力は**モノラル・48 kHz・16bit**（実測＝`yomiwakechan2/probe/irodori-tts-probe-report.md:19,118`）。`wav`/`flac`/`pcm` は soundfile だけで完結＝ffmpeg 不要。`aac` だけ torchaudio→ffmpeg。
10. `/health` はモデルを読まずに設定と状態を返す（**ready 判定に使える**）。`/ready` は無い。

---

## 1. エンドポイント一覧と要求／応答スキーマ

### 1-1. エンドポイント

| メソッド | パス | 認証 | 実装 | 備考 |
|---|---|---|---|---|
| GET | `/health` | **無し** | app.py:165-200 | 認証依存を持たない唯一のルート。モデルを読まない |
| GET | `/v1/models` | Bearer | app.py:203-215 | `id` は `IRODORI_MODEL_NAME`（既定 `irodori-tts`）1 件のみ |
| GET | `/v1/audio/voices` | Bearer | app.py:218-234 | 解決済み voice の一覧（ファイル＋alias＋`none`） |
| POST | `/v1/audio/voices` | Bearer | app.py:237-256 | multipart `file`＋任意 `voice_id`。201。重複は 409 |
| GET | `/v1/audio/voices/{voice_id}` | Bearer | app.py:259-269 | メタデータのみ（音声本体は返さない） |
| PUT | `/v1/audio/voices/{voice_id}` | Bearer | app.py:272-296 | 置換。未登録は 404 |
| DELETE | `/v1/audio/voices/{voice_id}` | Bearer | app.py:299-311 | |
| POST | `/v1/audio/speech` | Bearer | app.py:314-393 | 本体 |

認証＝`require_auth`（app.py:135-140）。`settings.api_key is None` なら**素通り**。設定時は `Authorization` が `f"Bearer {api_key}"` と**完全一致**でなければ 401（大小・空白の揺れを許さない）。

エラー本体は OpenAI 形（app.py:1156-1167）＝`{"error":{"message","type","param":null,"code":null}}`。`param`/`code` は非ストリーム経路では**常に null**（欄名を返さない）。

### 1-2. `SpeechRequest`（app.py:86-95・逐語）

```python
class SpeechRequest(BaseModel):
    model_config = ConfigDict(extra="allow")

    model: str
    input: str = Field(min_length=1, max_length=4096)
    voice: str | dict[str, Any] | None = None
    response_format: str | None = None
    speed: float = Field(default=1.0, ge=0.25, le=4.0)
    stream_format: str | None = None
    irodori: IrodoriOptions = Field(default_factory=IrodoriOptions)
```

| 欄 | 型 | 既定 | validation |
|---|---|---|---|
| `model` | str | 必須 | `settings.model_name` と不一致なら 400（app.py:409-413） |
| `input` | str | 必須 | pydantic で 1〜4096 字。**さらに空白のみを 400**（app.py:414-415） |
| `voice` | str \| dict \| None | None | dict の場合 `voice["id"]` を読む（voices.py:158-166）。未指定なら `IRODORI_DEFAULT_VOICE`、それも無ければ 400 |
| `response_format` | str \| None | None→`settings.default_response_format`（`wav`） | 前後空白 strip・小文字化。集合外は 400（audio.py:23-28） |
| `speed` | float | 1.0 | pydantic で `ge=0.25, le=4.0`。**`duration_scale /= speed` に変換**（app.py:843-844）。`seconds` を明示した場合も `seconds /= speed`（app.py:847-848） |
| `stream_format` | str \| None | None | `"sse"` 以外を渡すと 400（app.py:396-405）。None は非ストリーム |
| `irodori` | IrodoriOptions | 空オブジェクト | 下記 |

**`extra="allow"` の帰結**＝`input` 4096 字上限を除いて、未知のトップレベル欄は 422 にならない。`_extra()`（app.py:1082-1086）が `payload.model_extra` から拾うため、`irodori` の主要欄は**トップレベルにも書ける**（例 `{"model":..., "input":..., "num_steps":24}`）。優先順は `irodori.X` → トップレベル `X` → 環境変数の既定（`_coalesce`・app.py:1149-1153）。

### 1-3. `IrodoriOptions`（app.py:37-83・逐語・全 44 欄）

```python
class IrodoriOptions(BaseModel):
    model_config = ConfigDict(extra="allow")

    caption: str | None = None
    ref_wav: str | None = None
    ref_wavs: list[str] | None = None
    ref_latent: str | None = None
    ref_latents: list[str] | None = None
    ref_embed: str | None = None
    no_ref: bool | None = None
    seconds: float | None = None
    duration_scale: float | None = None
    min_seconds: float | None = None
    max_seconds: float | None = None
    max_ref_seconds: float | None = None
    ref_normalize_db: float | None = None
    ref_ensure_max: bool | None = None
    num_steps: int | None = None
    t_schedule_mode: Literal["linear", "sway"] | None = None
    sway_coeff: float | None = None
    num_candidates: int | None = None
    decode_mode: Literal["sequential", "batch"] | None = None
    cfg_scale_text: float | None = None
    cfg_scale_caption: float | None = None
    cfg_scale_speaker: float | None = None
    cfg_guidance_mode: Literal["independent", "joint", "alternating"] | None = None
    cfg_scale: float | None = None
    cfg_min_t: float | None = None
    cfg_max_t: float | None = None
    truncation_factor: float | None = None
    rescale_k: float | None = None
    rescale_sigma: float | None = None
    context_kv_cache: bool | None = None
    speaker_kv_scale: float | None = None
    speaker_kv_min_t: float | None = None
    speaker_kv_max_layers: int | None = None
    seed: int | None = None
    trim_tail: bool | None = None
    tail_window_size: int | None = None
    tail_std_threshold: float | None = None
    tail_mean_threshold: float | None = None
    max_text_len: int | None = None
    max_caption_len: int | None = None
    lora_adapter: str | None = None
    chunking_enabled: bool | None = None
    chunk_min_chars: int | None = None
    first_sentence_chunk_min_chars: int | None = None
```

**全欄が `| None = None`**＝pydantic 段では型以外の制約が一切無い。範囲検査はすべて `_build_sampling_request`／`_validate_sampling_request` と推論側にある。
`Literal` 制約は 3 欄だけ（`t_schedule_mode`・`decode_mode`・`cfg_guidance_mode`）＝ここだけ 422 が返る。

### 1-4. 各欄の実効既定値と経路（app.py:832-1045）

`環境変数の既定` 列は `config.py` の欄名（`IRODORI_` 接頭辞を付けたものが環境変数）。

| irodori 欄 | 環境変数の既定 | 既定値 | 型変換関数 | 備考 |
|---|---|---|---|---|
| `caption` | 無し | None | `_as_optional_str` | 空文字は 400（app.py:1124-1130） |
| `ref_wav` / `ref_wavs` / `ref_latent` / `ref_latents` / `ref_embed` / `no_ref` | 無し | voice から解決 | — | §3 |
| `seconds` | 無し | None | `_as_optional_float` | 設定すると**チャンク分割が無効化**（app.py:562-564） |
| `duration_scale` | `default_duration_scale` | 1.0 | `_as_float` | `speed` で割られる。`<=0` は 400（app.py:1049-1050） |
| `min_seconds` | `default_min_seconds` | 0.5 | `_as_float` | |
| `max_seconds` | `default_max_seconds` | 30.0 | `_as_float` | `max<min` は 400（app.py:1051-1054） |
| `max_ref_seconds` | `default_max_ref_seconds` | None（=チェックポイント推奨値。v4 Small は 120 秒） | `_as_optional_float` | **`_explicit_option`**（明示 null で既定を打ち消せる） |
| `ref_normalize_db` | `default_ref_normalize_db` | **-16.0** | `_as_optional_float` | **`_explicit_option`** |
| `ref_ensure_max` | `default_ref_ensure_max` | True | `bool()` | |
| `num_steps` | `default_num_steps` | 40 | `_as_int` | |
| `t_schedule_mode` | `default_t_schedule_mode` | `"linear"` | `str()` | |
| `sway_coeff` | `default_sway_coeff` | -1.0 | `_as_float` | |
| `num_candidates` | `default_num_candidates` | 1 | `_as_int` | **>1 でも返るのは 1 本目のみ**（§4） |
| `decode_mode` | `default_decode_mode` | `"sequential"` | `str()` | |
| `cfg_scale_text` | `default_cfg_scale_text` | 3.0 | `_as_float` | |
| `cfg_scale_caption` | **`default_cfg_scale_text` を流用**（app.py:936） | 3.0 | `_as_float` | 専用の環境変数が無い＝**caption 専用の既定は環境変数から出せない**。要求ごとの指定は可能 |
| `cfg_scale_speaker` | `default_cfg_scale_speaker` | 5.0 | `_as_float` | |
| `cfg_guidance_mode` | `default_cfg_guidance_mode` | `"independent"` | `str()` | |
| `cfg_scale` | 無し | None | `_as_optional_float` | 一括上書き |
| `cfg_min_t` / `cfg_max_t` | `default_cfg_min_t` / `default_cfg_max_t` | 0.5 / 1.0 | `_as_float` | |
| `truncation_factor` / `rescale_k` / `rescale_sigma` | 無し | None | `_as_optional_float` | |
| `context_kv_cache` | `default_context_kv_cache` | True | `bool()` | |
| `speaker_kv_scale` / `speaker_kv_min_t` | 無し | None | `_as_optional_float` | |
| `speaker_kv_max_layers` | 無し | None | `_as_optional_int` | |
| `seed` | 無し | None（毎回ランダム） | `_as_optional_int` | 応答ヘッダ `X-Irodori-Seed` に実使用値が返る |
| `trim_tail` | `default_trim_tail` | True | `bool()` | |
| `tail_window_size` | `default_tail_window_size` | 20 | `_as_int` | |
| `tail_std_threshold` / `tail_mean_threshold` | `default_tail_std_threshold` / `..._mean_...` | 0.05 / 0.1 | `_as_float` | |
| `max_text_len` / `max_caption_len` | 無し | None | `_as_optional_int` | |
| `lora_adapter` | 無し | None | `_as_optional_str` | **サーバ側のディレクトリパス**（§2-7） |
| `chunking_enabled` | `default_chunking_enabled` | True | `bool()` | トップレベル別名 `chunking` も可（app.py:555） |
| `chunk_min_chars` | `default_chunk_min_chars` | 80 | `_as_int` | `<=0` は 400 |
| `first_sentence_chunk_min_chars` | `default_first_sentence_chunk_min_chars` | None | `_as_optional_int` | **`_explicit_option`** |

`_explicit_option`（app.py:1089-1095）＝`model_fields_set` を見るので、**明示的な `null` と「未指定」を区別**する 3 欄。読み分けちゃん側で「既定に戻す」を表現するのに使える。

### 1-5. 交差 validation（app.py:1048-1079・すべて 400）

- `duration_scale <= 0`
- `max_seconds < min_seconds`
- `ref_wav` と `ref_wavs` の併用／`ref_latent` と `ref_latents` の併用
- 波形参照と潜在参照の併用
- `no_ref` と参照入力の併用
- `ref_embed` と波形／潜在参照の併用

### 1-6. 応答（非ストリーム）

本体＝音声バイト列そのもの。`media_type` は `CONTENT_TYPES[fmt]`（audio.py:13-20）。ヘッダは app.py:381-387：

| ヘッダ | 値 |
|---|---|
| `Content-Disposition` | `attachment; filename="speech.{fmt}"` |
| `X-Irodori-Seed` | 実使用シード（整数の文字列） |
| `X-Irodori-Total-To-Decode` | `%.6f` 秒（読み込み後〜デコード完了）。**チャンク分割時は合計**（app.py:687） |
| `X-Irodori-Messages` | `result.messages` を `" | "` で連結・4096 バイトで切り詰め。**メッセージが空なら欄ごと出ない** |

`X-Irodori-Messages` に載る典型＝ウォーターマーク不可時の `warning: SilentCipher watermark is unavailable; ...`（inference_runtime.py:1443-1447）、LoRA の読み込み／キャッシュ通知（同 1800 番台ではなく 812-818）。

### 1-7. サーバが固定している（要求で変えられない）もの

| 項目 | 固定値／決まり方 | 根拠 |
|---|---|---|
| `speaker_uncond_mode` | `"mask"`（SamplingRequest の既定のまま。**サーバが一切渡さない唯一の推論欄**） | inference_runtime.py:238／`_build_sampling_request` に出現せず（機械照合済み） |
| デバイス（model／codec） | プロセス起動時の環境変数のみ | config.py:27-28・runtime.py:51,54 |
| 精度（fp32/bf16） | 同上 | config.py:29-30 |
| `compile_model` / `compile_dynamic` | 同上 | config.py:33-34 |
| `codec_deterministic_encode/decode` | 同上（既定 True・**環境変数は .env.example に載っていない**が `IRODORI_CODEC_DETERMINISTIC_ENCODE` で効くはず＝未確認） | config.py:31-32 |
| チェックポイント／コーデック repo | 同上（要求ごとのモデル切替なし） | config.py:22-24 |
| ウォーターマーク（SilentCipher） | **常時 ON。切る API が無い**（`silentcipher` が入っていれば必ず埋め込む） | inference_runtime.py:1433-1441・watermark.py:28-54・Irodori-TTS/pyproject.toml:20 |
| 出力サンプルレート | コーデック由来（`self.codec.sample_rate`）。要求で変えられない | inference_runtime.py:1457 |
| 出力チャンネル数 | モノラル（1ch） | 実測＝probe-report.md:118 |
| `num_candidates>1` の候補選択 | **常に 0 本目**。他は捨てられる | inference_runtime.py:1455 |

---

## 2. ランタイム（runtime.py・app.py）

### 2-1. 読み込み

`RuntimeManager`（runtime.py:24-102）はプロセスに 1 インスタンス（app.py:99 でモジュール読み込み時に生成）。

- `get()`（runtime.py:31-66）＝二重チェック＋`threading.Lock`。ロック取得に `model_load_timeout` 秒（既定 300）掛かったら `RuntimeLoadTimeoutError`→**HTTP 503**（app.py:352-355）。
- **`unload()` を呼ぶ経路が無い**＝一度載ったモデルはプロセス終了まで居座る。`InferenceRuntime.unload()` は存在するが（inference_runtime.py:1464）サーバから呼ばれない。
- `InferenceRuntime.from_key()` を直接呼ぶ（runtime.py:48）。上流の**`get_cached_runtime()`（キー変更で旧ランタイムを unload する仕組み・inference_runtime.py:1487-1501）は使っていない**＝サーバはモデル切替の器を持たない。

### 2-2. チェックポイント解決（runtime.py:80-95）

1. `IRODORI_CHECKPOINT` が非空 → `expanduser()` して**ファイルであること**を検査（無ければ `FileNotFoundError`）。
2. さもなくば `IRODORI_HF_CHECKPOINT`（既定 `Aratako/Irodori-TTS-v4-Small`）を `download_hf_checkpoint(repo_id)` に渡す＝**初回はネットワーク取得**。`repo/subfolder` 形も受ける（README.md:65-69）。

### 2-3. デバイス解決（BRIEF §4-4 の芯）

```python
    @staticmethod
    def _resolve_device(value: str) -> str:
        raw = str(value).strip().lower()
        if raw in {"", "auto"}:
            return default_runtime_device()
        return str(value)
```
（runtime.py:97-102・逐語）

`default_runtime_device()`＝`list_available_runtime_devices()[0]`（inference_runtime.py:92-93）。一覧の作り方は inference_runtime.py:80-89：

```python
def list_available_runtime_devices() -> list[str]:
    devices: list[str] = []
    if torch.cuda.is_available():
        devices.append("cuda")
    if _is_mps_available():
        devices.append("mps")
    if _is_xpu_available():
        devices.append("xpu")
    devices.append("cpu")
    return devices
```

| 問い | 答え | 根拠 |
|---|---|---|
| `auto` は何に解決されるか | **`cuda` → `mps` → `xpu` → `cpu` の先頭**。index は付かない（素の `"cuda"`＝既定デバイス＝index 0） | inference_runtime.py:80-93 |
| `cuda:1` 形式を受けるか | **受ける**。`_resolve_device` は auto/空以外を素通しし、`resolve_runtime_device` は `torch.device(device)` で index を保持したまま返す | runtime.py:102・inference_runtime.py:55-62 |
| index の妥当性検査 | **無い**。`torch.cuda.is_available()` しか見ない＝存在しない index は torch 側の例外になる（起動時／初回合成時） | inference_runtime.py:59-62 |
| `mps:0` / `xpu:0` | **明示的に拒否**（`"MPS device index is not supported. Use 'mps'."`） | inference_runtime.py:64-65,70-71 |
| 対応 type | `cpu` / `cuda` / `mps` / `xpu` のみ。それ以外は `ValueError` | inference_runtime.py:75-77 |
| ROCm はどう名乗るか | **`cuda`**。ROCm 版 torch は `torch.cuda.*` API を使うので分岐が無い。compose.rocm.yaml:12-13 が `IRODORI_MODEL_DEVICE: cuda` と書いている | compose.rocm.yaml:11-15 |
| model と codec を別デバイスに置けるか | **置ける**（`IRODORI_MODEL_DEVICE=cuda:0` / `IRODORI_CODEC_DEVICE=cpu` 等）。RuntimeKey が別欄 | runtime.py:51,54 |
| **複数 GPU に別々のモデルを載せられるか** | **1 プロセスでは不可**。ランタイムは 1 つ・デバイスは環境変数固定。**プロセスを複数立てて別ポートで動かす**しかない | runtime.py:28,48・config.py:27-28 |

### 2-4. 精度

`model_precision` / `codec_precision`（既定 `"fp32"`）。`list_available_runtime_precisions()`（inference_runtime.py:96-100）＝`cuda`/`xpu` は `["fp32","bf16"]`、それ以外（cpu・mps）は `["fp32"]` のみ。README.md:558-559 も `fp32` / `bf16` の 2 択。**fp16 は無い**。

### 2-5. compile

`compile_model=True` で `encode_conditions` / `build_context_kv_cache` / `forward_with_encoded_conditions` を `torch.compile`（inference_runtime.py:260-277）。
**per-request LoRA と排他**＝`compile_model` 有効時に `lora_adapter` を指定すると `RuntimeError("Dynamic LoRA loading is not compatible with compile_model=True.")`（inference_runtime.py:806-807）→ サーバが 400 に変換（app.py:361-364）。

### 2-6. preload / 同時実行 / empty_cache

| 機構 | 既定 | 挙動 | 根拠 |
|---|---|---|---|
| `preload` | `false` | true なら lifespan 起動時に `runtime_manager.get()`＝**起動が数十秒〜数分ブロックし、失敗すると起動そのものが落ちる**（例外を握らない） | app.py:103-116 |
| `max_concurrent_synthesis` | 1 | `asyncio.Semaphore(max(1, n))`。limit が変わると作り直す | app.py:523-530 |
| `synthesis_wait_timeout` | 300.0 | 取得待ちが超えたら **503 `"Synthesis queue is full. Retry after a moment. timeout=%.1fs"`** | app.py:533-543 |
| `empty_cache_interval` | 10 | N 回の合成ごとに `torch.cuda.empty_cache()` 等。**0 で無効・1 で毎回**。`finally` で数えるので**失敗した合成も 1 回に数える**（テストで保証・test_api.py:1031-1046） | app.py:474-521 |
| `model_load_timeout` | 300.0 | §2-1 | runtime.py:35-40 |

**重要（先読み設計の前提）**＝`InferenceRuntime.synthesize` は内部で `self._infer_lock`（`threading.Lock`）を取る（inference_runtime.py:620・1183-1184）。よって `IRODORI_MAX_CONCURRENT_SYNTHESIS=2` にしても**GPU 上で 2 本が同時に走ることはない**。増やしても「セマフォを抜けてランタイムのロックで待つ」だけ＝キューの深さが変わるにすぎない（＝先読みの「投げておける本数」としては意味がある）。

### 2-7. LoRA の per-request 読み込み

- 要求ごとに `irodori.lora_adapter`＝**サーバから見えるディレクトリパス**（`adapter_config.json` とアダプタ重みが必要・inference_runtime.py:756-759）。
- 解決は `_resolve_lora_adapter_path`（inference_runtime.py:746-760）。`""` / `"none"` / `"null"` / `"off"` / `"disable"` / `"disabled"` / `"base"` は**アダプタ無効化**（大小無視）。
- 一度読んだアダプタはパスの sha1 先頭 16 桁で名付けて `self._lora_adapter_names` に**キャッシュ**（inference_runtime.py:762-765・809-827）。**アンロードの経路は無い**＝アダプタを増やすほどメモリが積み上がる。
- 存在しないディレクトリ＝`FileNotFoundError` → 400（app.py:359-360）。

---

## 3. voices の規約

### 3-1. ディレクトリ走査（voices.py:168-186）

- 走査対象＝`IRODORI_VOICES_DIR`（既定 `Path("voices")`＝**プロセスのカレント相対**。config.py:41）。起動時に `mkdir(parents=True, exist_ok=True)`（voices.py:58-61・app.py:104）。
- **檔名の stem がそのまま voice 名**（`alice.wav` → `voice:"alice"`）。付随テキスト（参照音声の書き起こし）を置く仕組みは**無い**＝v4 は参照テキスト不要。
- 拡張子で役割が決まる：

| パターン | VoiceSpec の欄 | 根拠 |
|---|---|---|
| `.wav .flac .mp3 .m4a .ogg .opus .aac .webm` | `ref_wav` | voices.py:11-20,181-183 |
| `.pt .pth` | `ref_latent` | voices.py:21,184-185 |
| `*.speaker.safetensors` | `ref_embed`（Speaker Inversion）。**voice_id はサフィックスを除いた檔名全体**（`alice.speaker.safetensors` → `alice`） | voices.py:22,177-179 |

- **要求ごとの走査**（キャッシュ無し）＝起動後にファイルを足しても即反映（README.md:148）。逆に毎回 `iterdir()` するので voices が巨大だと遅くなる（未確認・実測せず）。

### 3-2. `voices.json`（alias）

置き場所＝`IRODORI_VOICE_ALIASES_FILE`、未指定なら `voices_dir/voices.json`（voices.py:188-196）。**alias はスキャン結果を上書きする**（voices.py:66-67）。
値は文字列（＝1 檔）または オブジェクト（`no_ref` / `ref_wav` / `ref_wavs` / `ref_latent` / `ref_latents` / `ref_embed`）。相対パスは `voices_dir` 基準、絶対パスはそのまま（voices.py:250-254）。`ref_wavs`/`ref_latents` は**非空配列**でないと `ValueError`（voices.py:238-248）。

### 3-3. 解決順（`VoiceRegistry.resolve`・voices.py:72-94）

1. 要求の `voice`（str か `{"id":...}`）。空なら `IRODORI_DEFAULT_VOICE`。それも空なら `KeyError` → 400。
2. `voice_id.lower()` が `{"none","no_ref","no-ref","null","text-only"}` に入り、かつ `allow_no_ref_voice` が真 → **no_ref**（voices.py:23,80-81）。
3. alias → 4. スキャン結果 → 見つからねば `KeyError`（"Unknown voice=..."）→ 400。

なお **`irodori.ref_wav` 等を直接渡すと voice 解決を丸ごと飛ばす**（app.py:418-459）。この場合の `voice_id` は文字列 `"request"` 固定。

### 3-4. `IRODORI_ALLOW_NO_REF_VOICE`

既定 `true`（config.py:44）。効果は 2 つ＝(a) `list()` に合成 voice `none` が現れる（voices.py:68-69）、(b) `resolve()` が上記 5 語を no_ref として通す（voices.py:80-81）。**false にすると `voice:"none"` は "Unknown voice" で 400 になる**（＝Voice Design 単体利用が塞がる）。

### 3-5. 参照音声の前処理

サーバ側では**何もしない**（パスを渡すだけ）。前処理はすべて `irodori_tts` 側：

- `ref_normalize_db`（既定 **-16.0**・`_explicit_option` で明示 null にすると無効化）＝ラウドネス正規化。
- `ref_ensure_max`（既定 True）。
- `max_ref_seconds`（既定＝チェックポイント値。v4 Small は 120 秒、旧檔は 30 秒フォールバック・README.md:353,578）＝複数クリップを順に連結してから頭から切る（README.md:481-486）。
- 参照のサンプルレートはコーデック側で自動リサンプル（codec.py:200-201）。
- アップロード API は**中身を検証しない**（拡張子と空かどうかのみ・voices.py:119-127）＝壊れた wav は合成時に初めて落ちる。

### 3-6. caption（感情・声質の記述）

**`irodori.caption`（トップレベル別名 `caption` も可）**。`_as_optional_str` で strip され、空文字は 400（app.py:852-855,1124-1130）。文字数上限は `irodori.max_caption_len`（既定 None＝チェックポイント既定）。強さは `irodori.cfg_scale_caption`（既定はテキスト側と同値 3.0）。
使い方は 2 通り（README.md:190-233）＝(a) `voice:"none"` ＋ caption ＝純 Voice Design、(b) 参照音声 ＋ caption ＝話者は参照から・語り口は caption から。
**チャンク分割時は同じ caption が各チャンクに使われる**（`replace(sampling_request, text=chunk)` は text だけ差し替え・app.py:657,710）。

---

## 4. 音声（audio.py）

### 4-1. 対応フォーマットと符号器の経路

`CONTENT_TYPES`（audio.py:13-20・逐語）＝`mp3: audio/mpeg` / `opus: audio/opus` / `aac: audio/aac` / `flac: audio/flac` / `wav: audio/wav` / `pcm: audio/pcm`。

| fmt | 経路（先に成功したものを採る） | 外部依存 | 根拠 |
|---|---|---|---|
| `pcm` | numpy で `(wav*32767).astype("<i2")`＝**ヘッダ無し 16bit LE、モノラル前提**（`squeeze(0)`） | 無し | audio.py:40-42 |
| `wav` | **soundfile のみ**。失敗したら `raise`（フォールバックしない） | libsndfile | audio.py:44-49,80-91 |
| `flac` | 同上 | libsndfile | 同上 |
| `mp3` | soundfile（`format="MP3", subtype="MPEG_LAYER_III"`）→ torchaudio → ffmpeg（`libmp3lame`） | libsndfile(≥1.1)／ffmpeg | audio.py:44-69,72-77,130-137 |
| `opus` | soundfile（`format="OGG", subtype="OPUS"`）→ torchaudio(`ogg`) → ffmpeg(`libopus`, `-f ogg`) | 同上 | 同上 |
| `aac` | **soundfile を試さない**。torchaudio(`adts`) → ffmpeg(`aac`, `-f adts`) | ffmpeg 系 | audio.py:44,51-52,94-99,122-137 |

ffmpeg は**外部プロセス**（`shutil.which("ffmpeg")` → `subprocess.run(..., check=True, capture_output=True)`・audio.py:140-168）。中間 wav を一時ディレクトリに書いてから変換する。
全経路が失敗すると `RuntimeError` に 3 つの例外文を連結して投げ、`unhandled_exception_handler` が **500** にする（app.py:160-162）。

**⒜（専用インストーラ）への含意**＝`wav`／`flac`／`pcm` に限れば **ffmpeg も torchaudio の符号器も不要**（soundfile＝libsndfile DLL だけ）。読み分けちゃんは wav で受け取っている（probe-report.md:19）ので、配布物から ffmpeg を落とせる。

### 4-2. サンプルレート・チャンネル

- サンプルレートは**コーデックが決める**（`int(self.codec.sample_rate)`・inference_runtime.py:1457）。サーバは受け取って符号化に渡すだけ（app.py:370）。要求で変えられない。
- 実測値＝**PCM 16bit・48,000 Hz・1ch**（`yomiwakechan2/probe/irodori-tts-probe-report.md:19` および `:118`「fmt タグ=1（PCM）・1ch・48,000Hz・16bit」）。
- 形は `(channels, samples)`。1 次元なら `unsqueeze(0)`、3 次元以上は `ValueError`→400（audio.py:33-38・app.py:822-829）。
- `clamp(-1.0, 1.0)` を必ず掛ける（audio.py:38）。

### 4-3. 引っかかりどころ

- `opus` の `Content-Type` が `audio/opus` になっている（実体は Ogg 容器）。OpenAI 本家／一般的なプレイヤは `audio/ogg` を期待する＝**クライアント側で容器判定を content-type に頼らないこと**。
- `pcm` はヘッダ無しなので**サンプルレートを別途知っている必要がある**（応答ヘッダにも入らない）。

---

## 5. ストリーミングとキャンセル

### 5-1. 非ストリーム（既定）

**一括**。合成 → 符号化 → 全バイトを `Response` で返す（app.py:389-393）。`Content-Length` が付く（chunked ではない）。
OpenAI SDK の `with_streaming_response` を使っても中身は完成品（README.md:188 が明記）。

### 5-2. SSE（`stream_format:"sse"`）

`StreamingResponse(..., media_type="text/event-stream", headers={"Cache-Control":"no-cache","X-Accel-Buffering":"no"})`（app.py:771-775）。

イベントは 2 種のみ（app.py:734-745,769）：

| event | data |
|---|---|
| `audio_chunk` | `{"index", "text", "format", "media_type", "audio_base64", "seed", "total_to_decode"}` |
| `done` | `{"chunks": <完了数>}` |
| `error` | `{"error":{"message","type","param","code"}}`。`code` は `runtime_unavailable` / `invalid_request` / `stream_error` / `synthesis_queue_timeout` |

**各チャンクは完結した音声ファイル**（wav ならそれぞれ RIFF ヘッダ付き）を base64 で載せる＝復号して順に鳴らせるが、**base64 で約 1.33 倍に膨らむ**。
進捗（%・残りチャンク数・段階タイミング）を流すイベントは**無い**。`stage_timings` は SamplingResult に入っているが API のどこにも出ない。
**HTTP ステータスは常に 200**（エラーもイベントとして流れる）＝クライアントは `event: error` を見なければならない（test_api.py:610-643,646-674 が保証）。

### 5-3. キャンセル／クライアント切断

| 論点 | 事実 |
|---|---|
| 切断の検知 | **無い**。`create_speech(payload: SpeechRequest)`（app.py:315）は `Request` を受け取らず、`request.is_disconnected()` の呼び出しはコード中に一切無い（grep 済み・`Request` の出現は例外ハンドラの `_request` 引数のみ） |
| 推論の中断 | **不可能**。`InferenceRuntime.synthesize` はキャンセルトークンも進捗コールバックも取らない（引数は `req` と `log_fn` だけ・inference_runtime.py:1036-1041） |
| 実行スレッド | `loop.run_in_executor(None, ...)`＝asyncio 既定の ThreadPoolExecutor（app.py:469-471）。**future を cancel しても実行中スレッドは止まらない**（Python の仕様） |
| SSE 経路 | `_run_stream_blocking`（app.py:778-784）が `asyncio.shield`＋`await task` で**合成が終わるまでスロットを保持**してから CancelledError を再送出。test_api.py:702-754 が保証＝切断しても GPU は最後まで回るが、**次の要求が割り込むことはない** |
| 非ストリーム経路 | shield **無し**（app.py:358）。`finally: _release_synthesis_slot`（app.py:365-366）が即座に走るので、**もし外側がキャンセルされると、まだ動いているスレッドがあるのにセマフォが空く**（＝二重実行の窓。ただし `_infer_lock` が最終的に直列化する）。実測はしていない＝**この窓が実際に開くかは未確認**（Starlette が通常応答のハンドラを切断で cancel するかどうかに依る） |

**⒞への含意**＝「読み上げ中止」を押しても、そのチャンクぶんの GPU 時間は必ず消費される。中止の粒度は**チャンク単位**が上限。

### 5-4. キュー・health・ready・モデル切替

| 論点 | 事実 |
|---|---|
| キュー | `asyncio.Semaphore` のみ。**優先度・順序保証・キュー長の観測手段が無い**（FIFO かどうかも asyncio の実装依存）。溢れは時間切れ 503 |
| キュー長の露出 | `/health` に出るのは**設定値**（`max_concurrent_synthesis` / `synthesis_wait_timeout`）だけ。**現在の待ち数は出ない**（app.py:180-188） |
| health | `GET /health`・**認証不要**・モデルを読まない（test_api.py:90 が保証）。`runtime.loaded` / `runtime.loading` / `runtime.checkpoint` を返す |
| ready | 専用エンドポイントは**無い**。`/health` の `runtime.loaded` を ready 代わりに使える |
| モデル切替 | **無い**。`/v1/models` は 1 件を返すだけ、`model` 欄は一致検査に使うだけ（app.py:409-413）。切替は**プロセス再起動**のみ |
| メトリクス | 無し（ログのみ・`logging.INFO`） |

---

## 6. 起動

### 6-1. `__main__.py`（全 32 行）

引数は **`--host` / `--port` / `--reload` の 3 つだけ**（`__main__.py`:18-20）。`--workers` は**無い**＝常に 1 プロセス。既定値は `get_settings()` から取る。
`uvicorn.run("irodori_openai_tts.app:app", host=..., port=..., reload=...)`（`__main__.py`:23-28）。ログは `logging.basicConfig(level=logging.INFO, format="%(levelname)s:%(name)s:%(message)s")`（同 12-15）。
コンソールスクリプトも生える＝`irodori-openai-tts = "irodori_openai_tts.__main__:main"`（pyproject.toml:46）。

### 6-2. 設定の読み方（config.py:10-16・逐語）

```python
    model_config = SettingsConfigDict(
        env_prefix="IRODORI_",
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )
```

- pydantic-settings。優先度は **実環境変数 > `.env` > コード既定**。
- `.env` の**パスはカレント相対**（絶対パスではない）＝作業ディレクトリを間違えると黙って既定にフォールバックする。`voices_dir` の既定 `Path("voices")` も同様にカレント相対。**⒜のランチャは CWD を固定するか、全部を絶対パスの環境変数で渡すべき**。
- `extra="ignore"`＝`.env.example` にある **`IRODORI_TTS_BACKEND` は Settings の欄ではない**（Docker のビルド引数専用・Dockerfile:4,28）。同様に未知の `IRODORI_*` は静かに捨てられる＝綴り間違いに気づけない。
- `get_settings()` は `@lru_cache(maxsize=1)`（config.py:75-77）。app.py:98 でモジュール読み込み時に 1 回だけ評価＝**実行中の設定変更は効かない**。
- `cors_origins: list[str] = Field(default_factory=list)`（config.py:72）。空なら CORS ミドルウェアを付けない（app.py:125-132）。複合型なので環境変数には JSON 配列を書く必要がある（pydantic-settings の既定挙動・**この repo 内での実例は無い＝未確認**）。

### 6-3. Docker / compose

- Dockerfile:2 `ARG BASE_IMAGE=python:3.10-slim`、:4 `ARG IRODORI_TTS_BACKEND=cu128`。`uv sync --locked --no-dev --extra "${IRODORI_TTS_BACKEND}"`（:28,34）。apt で `build-essential ca-certificates ffmpeg git libsndfile1`（:14-21）。`PYTHONDONTWRITEBYTECODE=1` 済み（:6）。
- `compose.yaml`＝`.env` を env_file に、`hf_cache:/root/.cache/huggingface` と `./voices:/app/voices` をマウント（:9-15）。
- **`compose.gpu.yaml`（NVIDIA）**：`gpus: all` ＋ `IRODORI_MODEL_DEVICE: cuda` / `IRODORI_CODEC_DEVICE: cuda` / 精度 `bf16`（全 8 行）。
- **`compose.rocm.yaml`（AMD）**：`/dev/kfd` `/dev/dri` を渡し `group_add: video` `ipc: host` `security_opt: seccomp=unconfined`、環境変数は **NVIDIA と同一の `cuda`**（:11-15）＝**ROCm も device 名は `cuda`**。
- **GPU の index 指定は compose には無い**（`gpus: all`）。特定の GPU に固定するなら `CUDA_VISIBLE_DEVICES` か `IRODORI_MODEL_DEVICE=cuda:N` を自分で足す必要がある（この repo にその例は無い）。
- Python は両 repo とも `.python-version` = `3.10`、`requires-python = ">=3.10"`（pyproject.toml:7）。torch は `>=2.10.0,<2.11.0`（同 :16-17）。extra の index＝cpu `whl/cpu`・cu128 `whl/cu128`・**rocm `whl/rocm7.1` かつ `sys_platform == 'linux'` 限定**（pyproject.toml:57-88）＝**Windows の ROCm は上流の想定外**（司令官機の手順が独自になる理由がここに書いてある）。
- `.dockerignore` が `voices` と `*.wav`/`*.safetensors` 等を除外（:9-21）＝イメージにモデルも声も入らない設計。

---

## 7. tests から読める仕様（＝上流が保証している挙動）

| 保証 | テスト |
|---|---|
| `/health` はモデルを読まない | test_api.py:90 |
| API キー設定時は 401 | test_api.py:116 |
| voice の upload→list→get→replace→delete が一巡する。拡張子が変われば旧檔を消す | test_api.py:129・test_voices.py:148-166 |
| 重複 upload は 409、不正 voice_id（`../bad`）・非対応拡張子・空データは 400/ValueError | test_api.py:159・test_voices.py:179-187 |
| モデル読み込み中は 503、キュー溢れも 503（**推論は呼ばれない**） | test_api.py:193,587 |
| **不正な要求はランタイムを読み込む前に弾く**（`manager.thread_ids == []` を毎回検査）＝未知 model・空白 input・未知 response_format・未知 stream_format・`duration_scale<=0`・`max<min`・参照の併用 | test_api.py:245,266,286,306,325,346,545 |
| トップレベルの別名欄に非数値を渡すと 400 で**欄名がメッセージに入る**（`duration_scale` `chunk_min_chars` `first_sentence_chunk_min_chars` `ref_normalize_db`） | test_api.py:367-392 |
| LoRA の欄がそのまま `SamplingRequest.lora_adapter` に渡る／ディレクトリ不在は 400／compile 併用は 400 | test_api.py:413,431,472 |
| `ref_wavs`/`ref_latents` が配列として渡る。空配列・文字列・空要素は 400 | test_api.py:497,516 |
| **モデル読み込みも合成も別スレッド**（イベントループを塞がない） | test_api.py:564-584 |
| 応答ヘッダ `X-Irodori-Seed` / `X-Irodori-Total-To-Decode`（`%.6f`）が付き、本体は `RIFF` で始まる | test_api.py:449-469 |
| SSE は `audio_chunk`…→`done`。`audio_base64` は復号すると完全な wav | test_api.py:214-242,910 |
| **SSE はチャンクを yield する前にスロットを解放する**が、**キャンセルされたら合成完了までスロットを保持する** | test_api.py:677,702 |
| チャンク分割＝`chunk_min_chars` 未満では切らない／`first_sentence_chunk_min_chars` は最初の 1 回だけ／明示 `null` で既定を打ち消す／`seconds` 明示で分割しない | test_api.py:782,810,837,863,887,943 |
| `empty_cache_interval` の周期どおりに解放し、**失敗した合成も数える** | test_api.py:1024,1031 |
| **`speed=1.25` → `duration_scale == 0.8`** | test_api.py:1049-1060 |
| 音声符号化＝format は小文字化・未知は ValueError／mp3・opus は soundfile 優先で torchaudio を呼ばない／aac は torchaudio→ffmpeg／pcm は clamp して int16／3 次元は ValueError | test_audio.py 全体 |
| ランタイム読み込みの排他とタイムアウト／ローカル檔優先・不在は FileNotFoundError／未指定なら HF から取得 | test_runtime.py 全体 |

`tests/` は **推論を一切走らせない**（`FakeRuntime` に差し替える）＝上流 CI は CPU のみ・モデル不要。⒞の回帰試験も同じ型を使える。

---

## 8. ⒞（専用制御層）に足りないもの／外側から補えるか

| 足りないもの | 現状 | 外側（サーバ無改造の薄い層）から補えるか | 手段と限界 |
|---|---|---|---|
| **感情パラメータの露出** | **足りている**（42/43 欄） | — | ⒞の最大の利点。読み分けちゃんの「声の値」欄に `irodori` の欄をそのまま写せる |
| **キャンセル** | 検知も中断も無い | **△ 部分的** | 層側で HTTP 要求を捨てる（受け取った音声を鳴らさない）ことはできる。**GPU 時間は返らない**。SSE ＋小さい `chunk_min_chars` にすれば中止の粒度をチャンクまで細かくできる（＝実質これが唯一の手） |
| **先読み（prefetch）** | 直列（内部ロック） | **○ 条件付き** | 層が次の文を先に投げておくことは可能。ただし `_infer_lock` で直列＝**実効的な先読み＝「並べておく」だけ**。真の並列は**プロセスを増やす**（＝GPU を分ける）以外に無い |
| **GPU index 指定** | 環境変数のみ・要求では不可 | **○ ただしプロセス単位** | `IRODORI_MODEL_DEVICE=cuda:0` / `cuda:1` で**ポート違いのサーバを 2 本立て**、層が振り分ける。⒞はこの「複数バックエンドの束ね役」を担うのが自然 |
| **進捗** | 段階タイミングは内部に留まる | **✕** | `stage_timings` を出すには app.py の改造が要る。層から出せるのは「チャンク何本目」だけ（SSE の `index`） |
| **キュー長の観測** | 出ない | **○ 層が持てば良い** | 層が自前で発行数を数えれば足りる（サーバは 1 本ずつしか受けない前提で回す） |
| **複数モデル** | 1 プロセス 1 モデル固定 | **○ プロセス複数** | 上と同じ。`/v1/models` は常に 1 件しか返さない点に注意 |
| **モデルの動的切替** | 無し（`get_cached_runtime` を使っていない） | **✕（無改造では）** | 再起動が要る。層は「起動しなおして待つ」しかできない |
| **ウォーターマークの ON/OFF** | 常時 ON | **✕** | API に口が無い。`silentcipher` を入れない＝常時 OFF にはできるが、その場合は全要求で警告メッセージが返る |
| **未知パラメータの検出** | 黙って無視 | **○ 層で検査** | 層が許可欄の白名簿を持ち、綴り違いを弾く（サーバは教えてくれない） |
| **タイムアウトの粒度** | 待ち時間だけ（合成そのものに上限が無い） | **△** | 層側で HTTP のタイムアウトを切るのは可能だが、サーバ側の計算は止まらない（上のキャンセル欄と同じ） |
| **認証** | 単一の Bearer 固定文字列 | **○** | 層が付ければ足りる |
| **ready 待ち** | `/health` の `runtime.loaded` | **○** | `IRODORI_PRELOAD=true` ＋ `/health` ポーリングで起動完了を判定できる（`preload` 失敗は起動ごと落ちるので、プロセスの生死で判別可能） |
| **参照音声の受け渡し** | 要求のパスは**サーバ側パス**。リモートからは upload API 経由のみ（1 檔ずつ） | **○ 同一機なら容易** | 読み分けちゃんとサーバが同じ機なら `ref_wav` に絶対パスを渡すだけで済む（upload 不要）。**複数クリップの永続 voice だけは `voices.json` の書き込みが要る** |
| **出力の形式・SR** | wav/48k/mono 固定で十分 | — | 層で変換不要 |

**総括（⒞の見立て）**＝サーバは「パラメータの露出」と「デバイス選択の入口」はすでに揃っており、**⒞で自前に建てる必要があるのは主に (1) 複数プロセスの起動・監視・振り分け、(2) チャンク粒度の中止、(3) 白名簿検査**の 3 つ。**キャンセルと進捗だけは、上流を改造しない限り根本的に埋まらない**（層は「捨てる」ことしかできない）。

---

## 9. 主席への注意

1. **BRIEF §4-4 の答えは「環境変数のみ・`cuda:N` は通る・ただし 1 プロセス 1 デバイス」**。⒞の GPU 指定は「サーバ起動引数で足りる」に**半分だけ**該当＝引数ではなく環境変数、しかも複数 GPU は複数プロセス。比較表の「GPU 指定」欄は⒞について「プロセス多重で可能（層が管理）」と書くのが正確。
2. **ROCm と CUDA はコード上まったく分岐しない**（両方 `device="cuda"`・compose.rocm.yaml:12）。BRIEF §4-7 の「GPU 対応範囲」の分岐コストは**コードではなく torch の wheel（index の違い）にしか無い**＝pyproject.toml:57-88 が唯一の分岐点。しかも **rocm extra は `sys_platform == 'linux'` 限定**＝Windows 用 ROCm は上流に無い。
3. **`speed` は感情制御ではない**（`duration_scale` の逆数に潰される・app.py:843-844）。読み分けちゃんの既存「速度」欄は `speed` に写せるが、`duration_scale` と二重に触ると掛け算になる点に注意。
4. **v4 は参照テキスト（書き起こし）を要求しない**＝voices ディレクトリは音声檔だけ。他エンジンの「参照音声＋テキスト」の型を持ち込まないこと。
5. `X-Irodori-Messages` に「ウォーターマーク未適用」の警告が載る場合がある＝ライセンス／配布の議論（§4-2）で**ウォーターマークは切れないが「入らないこともある」**という事実は押さえておくとよい。
6. サンプルレート 48 kHz は**コード上の定数ではなくコーデック由来**。ノートでは実測（`yomiwakechan2/probe/irodori-tts-probe-report.md:19,118`）を根拠にした。⒝（ONNX 化）で別コーデックに差し替えれば変わりうる。
7. 未確認として残したもの＝(a) 非ストリーム経路でクライアント切断時にハンドラが実際に cancel されるか（Starlette の挙動依存・実測せず）、(b) `IRODORI_CODEC_DETERMINISTIC_ENCODE` 等が実際に環境変数として効くか（`.env.example` に無い）、(c) `IRODORI_CORS_ORIGINS` の環境変数記法、(d) voices ディレクトリが巨大なときの走査コスト。
8. 稼働中の 8088 は本読解中に応答しなかった（`curl http://localhost:8088/health` が無応答）。**私は何も起動・停止していない**。実機確認が要るなら別席へ。
