# 07 デバイス選択（GPU 指定）はコードのどこで決まるか

対象＝`upstream/Irodori-TTS/`（gradio 本体）・`upstream/Irodori-TTS-Server/`（OpenAI 互換）・稼働機 venv の `dacvae`／`silentcipher`。読むだけ・Python 実行なし（行番号は `grep -n`／`sed -n` で実確認）。
相対パスの基点＝`C:/Users/mugonkun/source/repos/irodori-native-research/`。venv 側は絶対パスで引く。

## 要点（10 行以内）

1. デバイス決定の**唯一の関門**は `upstream/Irodori-TTS/irodori_tts/inference_runtime.py:55` `resolve_runtime_device()`。受理は `cpu / cuda / mps / xpu` のみで、`cuda` **だけ** index を通す（`cuda:1` は可・`mps:0`／`xpu:0` は明示拒否 :64,:70）。
2. `'auto'` の解決は Server 側だけ（`runtime.py:97` `_resolve_device`）→ `default_runtime_device()`（`inference_runtime.py:92`）＝ `cuda > mps > xpu > cpu` の**先頭を無条件採用**＝GPU があれば必ず 0 番。
3. **環境変数で device を触れるのは Server だけ**。`irodori_tts` パッケージと gradio 本体は `os.environ` を推論経路で 1 度も読まない（読むのは `train.py:1506` 系の `WORLD_SIZE/RANK/LOCAL_RANK` のみ）。
4. **gradio 本体の CLI には device 引数が無い**（`gradio_app.py:642-645`＝`--server-name/--server-port/--share/--debug` のみ）。UI の Dropdown も選択肢が `["cuda","cpu"]` 等の**列挙のみ・`allow_custom_value` 未指定**＝`cuda:1` は入力不能。
5. ROCm 分岐は**コードに一切無い**（`is_hip`／`torch.version.hip` の参照ゼロ）。ROCm 版 torch は `cuda` として振る舞い、上流自身も `compose.rocm.yaml:12` で `IRODORI_MODEL_DEVICE: cuda` と書いている。分岐は wheel の index（pyproject の extra）だけ。
6. **選んだ device を無視する箇所は 1 つだけ確認**＝`inference_runtime.py:1471` と `app.py:512` の `torch.cuda.empty_cache()`（引数なし＝カレント device＝0 番）。`set_device` はどこにも無い。dacvae・silentcipher・watermark.py・codec.py・quantization.py・lora.py には固定 `cuda` の実害は無い（`codec.py:52` の既定値 `"cuda"` は全呼び出し元が明示上書き）。
7. `bf16` は `cuda`／`xpu` **限定**（`inference_runtime.py:309-310` で CPU/MPS は `ValueError`）。推論経路に `autocast` は無く、重みを丸ごと bf16 化する方式（`_move_inference_module` :283）。`autocast(device_type="cuda")` は `train.py:1943,3799` のみ＝学習専用。
8. `IRODORI_COMPILE_MODEL=true` は**遅延**（`torch.compile` でメソッドを包むだけ :271-277）＝失敗は起動時でなく**最初の合成要求**。稼働機 venv に triton は無く、Windows 用 triton は upstream pyproject が CUDA/ROCm 向けに一切入れない＝GPU inductor は `TritonMissing` で落ちる見込み。
9. **CPU 強制には env が 2 本要る**＝`IRODORI_MODEL_DEVICE=cpu` だけでは codec が `auto`→GPU のまま（`config.py:28`）。かつ稼働機の起動 bat は `bf16` を立てているので、cpu にすると `resolve_runtime_dtype` で即 `ValueError`。
10. 結論＝**⒞（薄い層）の材料としては Server 側の env だけで「GPU index 指定・CPU 強制」は足りる**（`cuda:1` は素通り）。足りないのは gradio 本体側と、多 GPU 時の `empty_cache` 誤爆・`/health` が解決後 device を返さない点。

---

## 1. 決定点の表

### 1-1 芯（共通）

| 檔:行 | 何を決めるか | 仕組み | index を受けるか |
|---|---|---|---|
| `upstream/Irodori-TTS/irodori_tts/inference_runtime.py:55-76` | `resolve_runtime_device()`＝**全経路の唯一の関門** | `torch.device(device)` に丸投げ → type で分岐 | **cuda のみ可**。`cpu`(:57) と `cuda`(:59) は `resolved` をそのまま返す＝index 保持。`mps`(:63) `xpu`(:69) は `index is not None` で `ValueError` |
| 同 :76 | 未対応 type の拒否 | 逐語＝`f"Unsupported inference device={resolved!s}. Expected one of: cpu, cuda, mps, xpu."` | — |
| 同 :80-89 | `list_available_runtime_devices()` | `cuda`→`mps`→`xpu`→`cpu` の順に append。**index 無しの型名のみ** | 無し |
| 同 :92-93 | `default_runtime_device()` | 上記リストの `[0]` を返す＝**GPU があれば必ず `"cuda"`（＝0 番）** | 無し |
| 同 :96-100 | `list_available_runtime_precisions()` | `cuda`/`xpu` → `["fp32","bf16"]`／他 → `["fp32"]` | — |
| 同 :186-198 | `RuntimeKey`（frozen dataclass） | `model_device`（既定なし＝必須）・`codec_device: str = "cpu"`・`model_precision/codec_precision: str = "fp32"`・`compile_model: bool = False`・`compile_dynamic: bool = False` | 文字列なので index を運べる |
| 同 :608-609 | 実体化 | `self.model_device = resolve_runtime_device(key.model_device)` / `self.codec_device = ...(key.codec_device)` | 保持 |
| 同 :626-627, 657-658 | 重みの移送 | `model.to(model_device)` → `_move_inference_module(model, device=..., dtype=...)`(:283) | 保持（`cuda:1` に載る） |
| 同 :1487-1496 | `get_cached_runtime()`＝**1 スロットの単一キャッシュ** | `RuntimeKey` が違えば旧 runtime を捨てて再構築＝**1 プロセス 1 device** | — |

### 1-2 入口ごと

| 入口 | 檔:行 | device の与え方 | `cuda:1` | CPU 強制 |
|---|---|---|---|---|
| **Server（8088）** | `upstream/Irodori-TTS-Server/src/irodori_openai_tts/config.py:11-16,27-30` | pydantic-settings。`env_prefix="IRODORI_"` ＋ `env_file=".env"`。`model_device: str = "auto"` / `codec_device: str = "auto"` | **可**（後述 1-3） | 可（**2 本必要**） |
| 同 | `runtime.py:48-61` | `RuntimeKey(model_device=self._resolve_device(settings.model_device), codec_device=self._resolve_device(settings.codec_device), ...)` | 素通り | — |
| 同 | `runtime.py:97-102` | `_resolve_device`＝`raw in {"", "auto"}` なら `default_runtime_device()`、**それ以外は `str(value)` を無加工で返す** | 素通り | — |
| 同 CLI | `__main__.py:17-21` | `--host` `--port` `--reload` **のみ**＝device 引数は無い | — | — |
| **gradio 本体（7860/7861）** | `gradio_app.py:641-646` / `gradio_app_voicedesign.py:656-663` | `--server-name` `--server-port` `--share` `--debug` **のみ** | **不可** | 不可（CLI では） |
| 同 UI | `gradio_app.py:394-398, 413-434`（voicedesign は :413-417, :432-）| `device_choices = list_available_runtime_devices()` を `gr.Dropdown(choices=...)` に。`allow_custom_value` は**全 py で 0 件** | **不可**（列挙外は入力不能） | 可（Dropdown で `cpu` を選ぶ） |
| 同 | `gradio_app.py:181-182` / `gradio_app_voicedesign.py:177-178` | `compile_model=False, compile_dynamic=False` を**ハードコード** | — | — |
| **infer.py（CLI）** | `infer.py:92-113` | `--model-device`（既定 `default_runtime_device()`）・`--codec-device`（同）・`--model-precision`/`--codec-precision`（`choices=["fp32","bf16"]`） | **可**（`str(args.model_device)` を素通し :400） | 可（`--model-device cpu --codec-device cpu`） |
| 同 | `infer.py:217-228` | `--compile-model` / `--compile-dynamic`（`BooleanOptionalAction`・既定 False） | — | — |

`--model-device` の help 逐語＝`"Model inference device (e.g. cuda, mps, cpu)."`（`infer.py:95`）。index の記載は無い。

### 1-3 `cuda:1` は本当に通るか（追跡）

`IRODORI_MODEL_DEVICE=cuda:1` → `config.py:27` → `runtime.py:99` `raw="cuda:1"` は `{"", "auto"}` に無い → `runtime.py:102` が **`str(value)` を無加工で返す** → `RuntimeKey.model_device="cuda:1"` → `inference_runtime.py:56` `torch.device("cuda:1")` → `:59` type=="cuda" → `:61` `torch.cuda.is_available()` だけ確認 → `:62` **index 付きのまま返す** → `:657` `model.to(cuda:1)`。**通る（断定）**。

注意（コードから導いた推論・実測未確認）＝
- `resolve_runtime_device` は **index の範囲検査をしない**。1 GPU 機で `cuda:1` を渡すと `is_available()` は True を返し、失敗は `.to()` 時の `invalid device ordinal` になる＝エラーが起動途中まで遅れる。「未確認（実測なし）」。
- `_resolve_device` は `raw`（lower 済み）で判定しつつ**戻り値は元文字列**（`runtime.py:99` vs `:102`）。`IRODORI_MODEL_DEVICE=CUDA:1` や前後空白付きは `torch.device()` 側で落ちる見込み。「未確認」。
- README は index を書いていない。逐語＝`| `IRODORI_MODEL_DEVICE` | `auto` | `auto`, `cuda`, `mps`, or `cpu`. |`（`upstream/Irodori-TTS-Server/README.md:556`）。**コードは README より広い**（index と `xpu` を受ける）。

### 1-4 ROCm / MPS / XPU の扱い

| 事項 | 根拠 | 内容 |
|---|---|---|
| ROCm 分岐 | `grep -rn -i "version\.hip|is_hip|rocm|hip"` の py 全走査 → ヒットは `prepare_manifest.py:473,481,949` の**エラー文言だけ**（逐語＝`"Multi-GPU mode requires CUDA/ROCm backend; use --device cuda."`） | **判定コードは 0 件**。ROCm は「`cuda` として見える」前提でそのまま動く |
| 上流自身の運用 | `upstream/Irodori-TTS-Server/compose.rocm.yaml:12-15` | `IRODORI_MODEL_DEVICE: cuda` / `IRODORI_CODEC_DEVICE: cuda` / `bf16` ×2。**ROCm でも文字列は `cuda`**（一次証拠） |
| 稼働機の実体 | `C:/irodori-TTS-server/Irodori-TTS-Server/.venv-rocm/Lib/site-packages/torch/version.py:4-9` | 逐語＝`__version__ = '2.13.0+rocm10.0.0'` / `cuda: Optional[str] = None` / `hip: Optional[str] = '7.15.26333'` / `rocm: Optional[str] = '10.0.0'` / `xpu: Optional[str] = None` |
| 同 | `.venv-rocm/Lib/site-packages/` の一覧 | `rocm_sdk_device_gfx1151-10.0.0.dist-info`（＝Radeon 8060S）、`torch_hip.dll`／`c10_hip.dll`（`torch/lib/`）。**Windows ネイティブ ROCm** |
| 同（差分） | `C:/irodori-TTS-server/Irodori-TTS-Server/overrides-rocm.txt`（全 3 行）| `torch==2.13.0+rocm10.0.0` / `torchaudio==2.11.0.2+rocm10.0.0` / `sentencepiece==0.2.1`。**上流 pyproject の `torchaudio>=2.10.0,<2.11.0` を外れている**（手組み venv） |
| 上流の ROCm extra | `upstream/Irodori-TTS-Server/pyproject.toml:32-38`, `upstream/Irodori-TTS/pyproject.toml:43-50` | index＝`https://download.pytorch.org/whl/rocm7.1`（:86-88 / :107-108）、`marker = "sys_platform == 'linux'"`＝**Windows ROCm は上流の想定外**。triton も linux 限定 |
| MPS | `inference_runtime.py:41-45, 63-68, 106-109, 1472-1475` | `torch.backends.mps.is_available()` で判定。**index 不可**。bf16 不可 |
| XPU | `inference_runtime.py:48-52, 69-74, 110-113, 1476-1479`；`upstream/Irodori-TTS/pyproject.toml:52-57` | `torch.xpu.is_available()` を try/except。**index 不可**。bf16 は可（:309）。`xpu` extra は **Server 側に無く gradio 側にだけある**（`triton-xpu==3.6.0; sys_platform == 'linux' or sys_platform == 'win32'`＝**Windows で triton が入る唯一の extra**） |

---

## 2. 「選んだ device を無視する箇所」の点検

### 2-1 見つかった 1 件（実害あり・多 GPU 時のみ）

| 檔:行 | 内容 | 影響 |
|---|---|---|
| `upstream/Irodori-TTS/irodori_tts/inference_runtime.py:1469-1471` | `for device in (self.model_device, self.codec_device): if device.type == "cuda": torch.cuda.empty_cache()` | **`empty_cache()` に device 引数が無い**＝torch のカレント device（既定 0 番）の allocator を空にする。`cuda:1` で動かしていると **1 番のキャッシュは解放されず、無関係な 0 番を叩く** |
| `upstream/Irodori-TTS-Server/src/irodori_openai_tts/app.py:504-512` | `_release_device_cache()`。同じ形（`device_type == "cuda"` → `torch.cuda.empty_cache()`） | 同上。`IRODORI_EMPTY_CACHE_INTERVAL`（既定 10・`config.py:39`）ごとに呼ばれる |
| （前提） | `grep -rn "set_device" upstream/Irodori-TTS/irodori_tts/ ... upstream/Irodori-TTS-Server/src/` → **0 件**。ヒットは `train.py:1523` と `prepare_manifest.py:484` のみ＝学習／前処理側 | 推論プロセスは一度も `torch.cuda.set_device()` を呼ばない＝カレント device は 0 のまま |

対照＝`_sync_device`（`:103-105`）は `torch.cuda.synchronize(device)` と**device を渡している**＝こちらは正しい。`empty_cache` だけが取りこぼし。

### 2-2 重点 6 檔の判定（いずれも実害なし）

| 檔 | 判定 | 根拠 |
|---|---|---|
| `upstream/Irodori-TTS/irodori_tts/codec.py` | **既定値だけ危険・実害なし** | `:52` `device: str = "cuda",` が `DACVAECodec.load` の既定。ただし呼び出し元は `inference_runtime.py:722` `device=str(codec_device)` の 1 箇所のみ（`grep -rn "DACVAECodec.load\|DACVAECodec("` で他に呼び出しなし）＝常に上書き。以降は `:78` `.to(device)` / `:103` `dummy = torch.zeros(..., device=device, ...)` / `:125` `message_device = torch.device(device)` / `:237,:266` `.to(self.device, dtype=self.dtype)` と全て引数追従。**`cuda:1` も文字列のまま届く** |
| `upstream/Irodori-TTS/irodori_tts/watermark.py` | **追従する** | `:29-30` `def __init__(self, *, device: str, ...)` → `:43` `silentcipher.get_model(model_type=model_type, device=device)`。`:71` `vector.to(self.model.device)`、`:76` 結果は `device="cpu"` に固定して返す（設計通り）。**呼び出し元は `inference_runtime.py:619` `SilentCipherWatermarker(device=str(self.codec_device))`＝codec 側に張り付く**（model_device ではない・後述 5-d） |
| `upstream/Irodori-TTS/irodori_tts/quantization.py` | **device 参照ゼロ** | `grep -n "cuda|device|\.to\(|map_location"` → 0 件 |
| `upstream/Irodori-TTS/irodori_tts/lora.py` | **追従する** | `:278` `torch_device: str | None = None` → `:287,:296` peft へ素通し。呼び出し元 `inference_runtime.py:825` `torch_device=str(self.model_device)`＝index 込み。直後 `:818-822` で `_move_inference_module(..., device=self.model_device, ...)` と再移送 |
| `.venv-rocm/.../dacvae/` | **追従する** | `grep -rn` の全ヒットが相対参照＝`model/base.py:212,271` `.to(self.device)`（`self.device` は `audiotools/ml/layers/base.py:131-136` の「最初のパラメータの device」）、`model/dacvae.py:467` `message.to(x.device)`、`nn/layers.py:203` `.to(hidden.device)`、`nn/quantize.py:172,184` `.to(z.device)`／`device=z.device`。**固定 `cuda` は 0 件**。重みは `audiotools/ml/layers/base.py:172` `torch.load(location, "cpu")` で CPU 経由＝GPU 0 を触らない |
| `.venv-rocm/.../silentcipher/` | **追従する（ただし `str` 保持）** | `server.py:22-25` `def __init__(self, config, device='cpu'): ... self.device = device`、`:55-61` `.to(self.device)` ×4、`:463-466` `torch.load(..., map_location=self.device)`、`:469` `def get_model(..., device='cpu')`。`stft.py:22,36` は `x.device` 追従。**`.cuda(` は 0 件**。`self.device` は torch.device でなく素の文字列なので `"cuda:1"` がそのまま `map_location` と `.to()` に渡る＝正しく 1 番に載る |

### 2-3 補足（学習側・本便の対象外だが記録）

`upstream/Irodori-TTS/train.py:1943` と `:3799`＝`torch.autocast(device_type="cuda", dtype=torch.bfloat16)`＝**device_type が "cuda" 固定**。ROCm は cuda を名乗るので通るが、XPU/MPS では学習不能。推論には影響なし。

---

## 3. `torch.compile` / triton（Windows で何が起きるか）

| 事項 | 檔:行 | 内容 |
|---|---|---|
| 何を包むか | `upstream/Irodori-TTS/irodori_tts/inference_runtime.py:260-278` | `enabled` が False なら素通り（:267）。True なら `model.encode_conditions` / `model.build_context_kv_cache` / `model.forward_with_encoded_conditions` の 3 メソッドを `torch.compile(..., dynamic=bool(dynamic))` で包む |
| 起動時に落ちるか | 同 :268-269 | `hasattr(torch, "compile")` が無いときだけ即 `RuntimeError`。**それ以外は包むだけ＝コンパイルは遅延**＝失敗は「最初の合成要求」で 500 になる（`app.py:158-160` の `unhandled_exception_handler` が拾う） |
| Server の入口 | `config.py:33-34`, `runtime.py:58-59` | `IRODORI_COMPILE_MODEL` / `IRODORI_COMPILE_DYNAMIC`（既定 false）。README 逐語＝`| `IRODORI_COMPILE_MODEL` | `false` | Enable `torch.compile` for core inference methods. Keep disabled when using dynamic LoRA adapters. |`（`README.md:560`） |
| gradio では | `gradio_app.py:181-182`, `gradio_app_voicedesign.py:177-178` | **常に False にハードコード**＝gradio 経路で compile は使えない |
| triton の有無（稼働機） | `ls .venv-rocm/Lib/site-packages | grep -i triton` → **0 件** | ROCm venv に triton は入っていない |
| torch の判定 | `.venv-rocm/.../torch/utils/_triton.py:177-179` | `has_triton()` は `has_triton_package()`（:37-43 の `import triton`）が False なら即 False |
| 失敗時のメッセージ | `.venv-rocm/.../torch/_inductor/exc.py:132-139` | `class TritonMissing` 逐語＝`"Cannot find a working triton installation. Either the package is not installed or it is too old. More information on installing Triton can be found at: https://github.com/triton-lang/triton"` |
| Windows の MSVC 要件 | `.venv-rocm/.../torch/_inductor/cpp_builder.py:137-141` | `def check_compiler_exist_windows(compiler)` docstring 逐語＝`"Check if compiler is ready, in case end user not activate MSVC environment."`（`cl.exe /help` を実行して確認） |
| 同（言語パック） | 同 :228-233 | `def check_msvc_cl_language_id(compiler)` docstring 逐語＝`"Torch.compile() is only work on MSVC with English language pack well."`＝**MSVC が英語言語パックでないと不調**（日本語環境の司令官機では地雷） |
| 上流が triton を入れるか | `upstream/Irodori-TTS/pyproject.toml:48-49, 56` / `upstream/Irodori-TTS-Server/pyproject.toml:36-37` | rocm extra＝`pytorch-triton-rocm>=3.5.1; sys_platform == 'linux'` と `triton-rocm>=3.6.0; sys_platform == 'linux'`。**cpu / cu128 extra には triton が 1 行も無い**。`triton-xpu==3.6.0; ... or sys_platform == 'win32'` だけが win32 を含む（gradio 側 pyproject のみ） |
| 上流の但し書き | `upstream/Irodori-TTS/README.md:84-86` | 逐語＝`The `rocm` extra includes `pytorch-triton-rocm` because `triton-rocm` alone does not provide `triton.language` for the `transformers` to `torch._dynamo` import path. This was validated with AMD GPU inference.` |

**まとめ（推論・実測未確認）**＝Windows で `IRODORI_COMPILE_MODEL=true` にすると、
(a) CUDA（cu128 extra）＝pyproject が triton を入れないので `TritonMissing` で最初の合成が失敗する見込み、
(b) ROCm（稼働機）＝triton 不在なので同上、
(c) inductor が CPU 側コード生成に降りた場合は MSVC が要る（上記 cpp_builder の 2 つの check）。
「Windows 用 triton の公式 wheel が存在するか」は**未確認**（PyPI 未照会・外部照会禁止のため）。**⒜／⒞ では `IRODORI_COMPILE_MODEL` は既定 false のまま封じ、初心者向けの選択肢に出さないのが安全**。

---

## 4. 精度と device の組合せ

| 事項 | 檔:行 | 内容 |
|---|---|---|
| 許可表 | `inference_runtime.py:96-100` | `cuda`／`xpu` → `["fp32","bf16"]`、`cpu`／`mps` → `["fp32"]` |
| 実際の門 | 同 :304-312 | `resolve_runtime_dtype(precision, device)`。`fp32`→`torch.float32`。`bf16` は `if device.type not in ("cuda","xpu"): raise ValueError("precision='bf16' currently requires CUDA or XPU device.")`（逐語・:309-310）。それ以外は `ValueError(f"Unsupported precision={precision!r}. Expected one of: fp32, bf16.")` |
| **bf16 は CPU で落ちるか** | 同 :310 | **落ちる。ランタイム開始前（`from_key` :628-637）に `ValueError`**＝合成要求ではなくモデル読込で死ぬ。fp16 という選択肢は存在しない |
| autocast | `grep -rn "autocast" --include=*.py upstream/` | 推論経路には**無い**。あるのは `model.py:860` の `torch.is_autocast_enabled(state.device.type)`（＝autocast 中なら dtype キャストを避ける防御）と、`train.py:1943,3799` の `torch.autocast(device_type="cuda", dtype=torch.bfloat16)` だけ |
| 実装方式 | `inference_runtime.py:283-301`（`_move_inference_module`） | 全パラメータ・全 buffer を `device` と `dtype` に**丸ごと変換**＝mixed precision ではなく **pure bf16**。`self._model_dtype = next(self.model.parameters()).dtype`（:625） |
| model と codec を別精度にできるか | `RuntimeKey:191,193` | `model_precision` と `codec_precision` は独立。`from_key:628-637` で別々に解決＝`model=cuda/bf16`・`codec=cpu/fp32` の組合せは合法 |
| 稼働機の実設定 | `C:/irodori-TTS-server/Irodori-TTS-Server/irodori-server-gpu.bat`（全 12 行） | `set IRODORI_MODEL_PRECISION=bf16` / `set IRODORI_CODEC_PRECISION=bf16`。コメント逐語＝`rem bf16: measured 4.8x faster warm synth, half VRAM (delete the next 2 lines to go back to fp32)`。device 系の `set` は**無い**＝両方 `auto`＝ROCm iGPU |

---

## 5. 結論の材料

### (a) 既存サーバの環境変数だけで「GPU index 指定／CPU 強制」は足りるか → **足りる。ただし 2 本ずつ要る**

| やりたいこと | 必要な env | 注意 |
|---|---|---|
| 0 番 GPU | （既定 `auto` のまま） | `default_runtime_device()` が `cuda` を返す＝index 無し＝0 番 |
| 1 番 GPU | `IRODORI_MODEL_DEVICE=cuda:1` ＋ `IRODORI_CODEC_DEVICE=cuda:1` | `runtime.py:102` が無加工で通す。**codec を忘れると codec だけ `auto`→0 番に載る**（`config.py:28`） |
| **CPU 強制** | `IRODORI_MODEL_DEVICE=cpu` ＋ `IRODORI_CODEC_DEVICE=cpu` ＋ **`IRODORI_MODEL_PRECISION=fp32` ＋ `IRODORI_CODEC_PRECISION=fp32`** | 精度を戻さないと `:310` で `ValueError`。稼働機の bat は bf16 を立てているので**この 4 本セットが必須** |
| 設定の置き場 | `config.py:13` `env_file=".env"` | **CWD 相対**。`irodori-server-gpu.bat` は `cd /d` してから起動しているので `.env` も効く（ただし現状 `.env` は不在＝`.env.example` のみ） |

診断の穴＝`/health`（`app.py:163-178`）が返すのは `settings.model_device`＝**生の `"auto"`**。解決後の実 device（`runtime.model_device`）を返さない＝「今どの GPU に載っているか」を HTTP から確認できない。

### (b) gradio 本体の CLI で足りるか → **足りない**

- `gradio_app.py:641-646` / `gradio_app_voicedesign.py:656-663`＝device 引数ゼロ。
- 環境変数も読まない（`grep -rn "os.environ|getenv" upstream/Irodori-TTS/gradio_app*.py irodori_tts/` → 0 件）。
- UI Dropdown（`gradio_app.py:413-418`）の choices は `list_available_runtime_devices()`＝型名のみ・`allow_custom_value` 未指定＝**`cuda:1` を打ち込めない**。
- したがって gradio 本体を多 GPU の 1 番に固定する手段は **`CUDA_VISIBLE_DEVICES` しかない**（＝(c)）。
- 参考＝`infer.py`（CLI）なら `--model-device cuda:1 --codec-device cuda:1` で直接指定できる。gradio だけが穴。

### (c) 複数 GPU 機で 2 プロセスを別 GPU に固定する手順

**方式 A（プロセス外から可視性を絞る・推奨・gradio でも効く）**

```
# プロセス 1（NVIDIA）
set CUDA_VISIBLE_DEVICES=0
（IRODORI_MODEL_DEVICE は auto または cuda のまま）

# プロセス 2（NVIDIA）
set CUDA_VISIBLE_DEVICES=1
（同上。プロセス内では「0 番」に見える）
```
- torch 側の解釈＝`.venv-rocm/.../torch/cuda/__init__.py:870-895` `_parse_visible_devices()`。`var = os.getenv("CUDA_VISIBLE_DEVICES")`（:872）。
- **ROCm では**同関数 :874 `if torch.version.hip:` に入り、:875-876 で `HIP_VISIBLE_DEVICES` と `ROCR_VISIBLE_DEVICES` を読む。コメント逐語＝`# HIP_VISIBLE_DEVICES is preferred over ROCR_VISIBLE_DEVICES`（:890）。両方立てると `HIP` の個数が `ROCR` を超えたとき `RuntimeError("HIP_VISIBLE_DEVICES contains more devices than ROCR_VISIBLE_DEVICES")`（:888）。
- したがって **AMD 機では `HIP_VISIBLE_DEVICES=0` / `=1`**（`CUDA_VISIBLE_DEVICES` も同関数に渡るが hip 分岐で上書きされる）。片方だけ立てるのが安全。
- 利点＝上流無改造・gradio でも Server でも効く・`empty_cache()` の 0 番誤爆も自動的に正しくなる（見えている 0 番＝目的の GPU）。

**方式 B（Server のみ・env で index を指定）**
```
set IRODORI_MODEL_DEVICE=cuda:1
set IRODORI_CODEC_DEVICE=cuda:1
```
- 利点＝1 プロセスから複数 GPU を見たまま選べる（将来 model=cuda:0 / codec=cuda:1 の分割も可）。
- 欠点＝`empty_cache()` が 0 番を叩く（2-1）。`IRODORI_EMPTY_CACHE_INTERVAL=0` で無効化すれば回避（`config.py:39`・稼働機 bat は既に `=0`）。

**併用時の落とし穴（推論・未実測）**＝方式 A で `CUDA_VISIBLE_DEVICES=1` を立てた上で方式 B の `cuda:1` を書くと、プロセス内には 0 番しか見えないので `invalid device ordinal` になる。**A と B は排他で使う**。

### (d) 直さないと駄目な箇所

| # | 檔:行 | 症状 | 直し方（提案） | 深刻度 |
|---|---|---|---|---|
| D1 | `inference_runtime.py:1471`／`app.py:512` | `torch.cuda.empty_cache()` が device 引数なし＝`cuda:1` 運用でカレント 0 番を叩き、狙った GPU のキャッシュが解放されない | `with torch.cuda.device(device): torch.cuda.empty_cache()` で包む。または方式 A（`*_VISIBLE_DEVICES`）で回避 | 中（多 GPU のみ・回避策あり） |
| D2 | `gradio_app.py:641-646` / `gradio_app_voicedesign.py:656-663` | gradio に device の CLI／env が無い＝多 GPU 機で選べない | ⒞なら**そもそも gradio を使わない**（Server 一本）ので不要。⒜で gradio を同梱するなら `--model-device`/`--codec-device` の追加が要る | 大（⒜で gradio を使う場合） |
| D3 | `gradio_app.py:413-418` の Dropdown | 選択肢が型名だけ＝`cuda:1` を打てない | `allow_custom_value=True` か、`torch.cuda.device_count()` で `cuda:0..N-1` を列挙 | 中 |
| D4 | `config.py:28` `codec_device: str = "auto"` | model を `cpu` にしても codec が GPU に残る／model を `cuda:1` にしても codec が 0 番に残る | 「codec_device 未指定なら model_device に追従」に変える。ラッパ側で 2 本必ずセットすれば回避可 | 中（⒞のラッパで吸収可） |
| D5 | `app.py:163-178` `/health` | 解決前の `"auto"` を返す＝実 device が外から見えない | `runtime_manager` が load 済みなら `str(runtime.model_device)` を併記 | 小（運用の見通し） |
| D6 | `inference_runtime.py:619` `SilentCipherWatermarker(device=str(self.codec_device))` | 透かし器が **codec_device** に張り付く。`codec_device=cpu`＋`model_device=cuda` にすると透かしだけ CPU で毎回走る／逆に codec を GPU にすると silentcipher の重みも GPU を食う | 独立の `watermark_device`（既定 cpu）を切る。⒞では VRAM 見積りに影響 | 小〜中 |
| D7 | `runtime.py:99` vs `:102` | 判定は lower・戻り値は原文＝`CUDA:1` や空白付きが `torch.device()` で落ちる | `return raw` にするだけ | 小 |
| D8 | `resolve_runtime_device:59-62` | index の範囲検査なし＝存在しない GPU 番号が重み移送まで検出されない | `resolved.index < torch.cuda.device_count()` を確認して分かりやすい `ValueError` に | 小 |

**逆に「直さなくてよい」と確認できたもの**＝`codec.py`（既定値 `"cuda"` は常に上書き）・`lora.py`・`quantization.py`・`speaker_inversion.py`・`rf.py`・`model.py`・`dacvae`・`silentcipher`。全て相対 device 追従で、index も文字列のまま届く。

---

## 6. 主席（Fable）への注意

1. **§4-4 の答えの芯は 1 行**＝`inference_runtime.py:55` の `resolve_runtime_device()` が唯一の関門で、`cuda` だけ index を通す。ここだけ押さえれば ⒞ の GPU 指定は成立する（Server の env だけで足りる／⒞ の薄い層は「env を 4 本正しく組む」だけの仕事になる）。
2. **⒞ の売り文句にしてよい**＝「上流を 1 行も直さずに GPU index 指定と CPU 強制ができる」は**断定できる**。ただし条件は「Server を使う」「codec 側の env も必ず立てる」「bf16 と cpu を同時に指定しない」の 3 つ。ラッパ層でこの 3 つを機械的に強制すれば初心者は詰まらない。
3. **⒜（インストーラ）で gradio を同梱する案は GPU 指定で不利**＝gradio 本体は CLI にも env にも device の口が無く（D2/D3）、`CUDA_VISIBLE_DEVICES` を bat で立てる以外に手が無い。「複数 GPU の指定ができる」を要件に置くなら **gradio ではなく Server を土台にすべき**。
4. **ROCm 対応範囲（§4-7）の材料**＝コードには ROCm 分岐が 1 行も無く、分岐は wheel の index だけ（`pyproject.toml` の extra、`sys_platform == 'linux'` マーカー）。つまり **「CUDA のみで出す」＝コード分岐ゼロで、ROCm は手動 venv 差し替えだけで足りる**（稼働機の `overrides-rocm.txt` 全 3 行がその実例）。この点は「CUDA のみで出し、ROCm は司令官機だけの手動手順」の結論を強く支える。
5. **`IRODORI_COMPILE_MODEL` は触らない**ことを推奨として明記してよい。Windows では triton 不在で最初の合成が落ちる見込み（§3）。ただし「Windows 用 triton wheel の存在」は未確認札のまま報告すること。
6. **bf16 の効きは実測がある**＝稼働機 bat のコメント逐語 `measured 4.8x faster warm synth, half VRAM`。これは §4-5（導入の重さ）と RTF の軸にそのまま使える一次証拠だが、**出典は司令官が書いた bat のコメント**であって上流の主張ではない点に注意。
7. **未確認札の一覧**＝(i) `cuda:7` 等の範囲外 index の実エラー文言、(ii) Windows 用 triton wheel の入手可否、(iii) 方式 A＋B 併用時の挙動、(iv) `IRODORI_MODEL_DEVICE=CUDA:1` の大文字扱い。いずれも**推論であり実測していない**（GPU 実験禁止・8088 を止めない制約のため）。
8. **本便で 1 バイトも書き換えていない**（`upstream/**`・`C:/IrodoriTTS/**`・`C:/irodori-TTS-server/**` は読み取りのみ・Python 実行なし・`__pycache__` 生成なし）。
