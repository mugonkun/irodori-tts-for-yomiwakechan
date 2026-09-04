# 18 — 第 1 便のコード読解（03・04・05・06・07・08）の敵対検分

> 席＝第 2 便サブ席（opus）／担当＝敵対検分。各主張を**反証しようと試み**、反証できなければ CONFIRMED、
> できれば REFUTED、判定不能なら UNVERIFIABLE。迷ったら CONFIRMED にしない方針。
> 実施日 2026-09-04。GPU 未使用（torch 2.10.0+cpu）。8088/7861 は起動も停止もしていない・HTTP 送信なし。
> 相対パスの起点＝`C:/Users/mugonkun/source/repos/irodori-native-research/`。行番号は `awk`／AST で実測。
> 検分に使った venv＝`lab/.venv`（`PYTHONDONTWRITEBYTECODE=1`・`HF_HUB_OFFLINE=1`）。
> 検分後に `upstream/**` は両リポとも `git status --short --ignored` が空・`__pycache__` 0 件（§8）。

## 要点（10 行以内）

1. 検分した 17 主張のうち **13 が CONFIRMED、3 が部分 REFUTED、1 が CONFIRMED＋重大な追記**。芯（デバイス選択・透かし・CFG ループ・チャンク連結）は第 1 便の読みで**正しい**。
2. **最大の発見（新規・断定）**＝torchcodec 不在時の `ImportError` は `save_wav` だけでなく **`_load_audio`（`inference_runtime.py:1518`）にも漏れる**。ここは **Server の `ref_wav` 経路**（`:936`）＝**参照音声つき voice を使う要求が丸ごと 500 になる**。lab で再現（§3-e）。「torchcodec を配らない」（08 §4-F）と「参照音声 voice」は上流無改造では両立しない。
3. **REFUTED その 1**＝08 §2-D「`from transformers import AutoTokenizer` が pandas・pyarrow を引く」。実測では **sklearn 経由の任意 import** で、pandas 未導入の lab venv では引かれない（§3-m）。⒜の配布物からさらに **pandas 46 MB＋pyarrow 86 MB** を落とせる。
4. **REFUTED その 2**＝08 §4-F「`audio.py:47` の `except RuntimeError`」。実体は **`except Exception`**（`audio.py:47`）で、しかも順序は soundfile→torchaudio＝Server の wav/flac 出力は最初から torchcodec に依存していない。
5. **REFUTED その 3**＝05 要点1「`SamplingRequest` は 45 フィールド」。AST 実測 **43**（06 の「43 欄中 42 欄」が正しい）。
6. **要修正（重大度中）**＝07 D1 の `inference_runtime.py:1471` は **`unload()` の中**で、Server は unload を呼ばない（上流自身が `app.py:477-479` の docstring で明言）。多 GPU の実害は **`app.py:512` の 1 箇所だけ**。
7. 07 の「未確認」2 件を実測で埋めた＝`IRODORI_MODEL_DEVICE=CUDA:1`（大文字）は **`RuntimeError`**（`ValueError` ではない）、前後空白付きも `RuntimeError`（§3-b）。
8. (a)(b)(d)(f)(g)(h)(j)(k)(l)(n)(o)(p)(q) は**全て CONFIRMED**。(a) は AST の差集合で機械照合し `lab/out/18-field-diff.json` に残した。
9. 行番号のずれは 8 箇所（§6）。うち報告に響くのは 03 §6-1 の `forward_no_conv`＝**`dacvae.py:326-332`**（03 の `:333-338` は誤り）。
10. 「上流に onnx・G2P・einops・カスタム C++ 演算子が 0 件」「`irodori_tts`／gradio が `os.environ` を読まない」は**すべて再 grep で CONFIRMED**（§5）。

---

## 1. 判定表（1 行 1 主張）

| # | 主張（要旨） | 判定 | 根拠 |
|---|---|---|---|
| a | サーバは `SamplingRequest` 全欄のうち `speaker_uncond_mode` だけを渡さない | **CONFIRMED** | AST 差集合。`inference_runtime.py:201-246` の 43 欄 − `app.py:850` の 42 kwargs ＝ `{speaker_uncond_mode}`（`:238`）。`lab/out/18-field-diff.json` |
| a' | 05 要点1「45 フィールド」 | **REFUTED** | 実測 **43**。06 の「43 欄中 42」が正 |
| b | `resolve_runtime_device` は `cuda:N` を index 付きで返し `mps:N`/`xpu:N` を拒否 | **CONFIRMED** | `inference_runtime.py:55-77`。`:62 return resolved`／`:64-65`／`:70-71`。lab で例外文言まで再現（§3-b） |
| b' | `runtime.py:97-102` の `_resolve_device` は auto/空以外を無加工で返す | **CONFIRMED** | `runtime.py:97-102`（`:99` は lower 済み `raw` で判定・`:102` は `str(value)` を返す） |
| c | `torch.cuda.empty_cache()` が device 引数なし・推論経路に `set_device` なし | **CONFIRMED（ただし重大度は要修正）** | `inference_runtime.py:1471`／`app.py:512`。`set_device` は `train.py:1523`・`prepare_manifest.py:484` のみ。**:1471 は `unload()` 内＝Server 未到達**（§3-c） |
| d | 透かしに on/off が無い・payload・device・失敗時 warning | **CONFIRMED（4 点すべて）** | `watermark.py:10`／`inference_runtime.py:619`／`:1433`・`:1443-1447`／`watermark.py:34-50`。Server 全体で `watermark|silentcipher` grep **0 件** |
| e | `.py` に torchcodec import 0 件・`save_wav` が ImportError を取りこぼす | **CONFIRMED＋追記（重大）** | grep 0 件（宣言は `pyproject.toml:23` 等のみ）。lab 再現＝`ImportError`。**`_load_audio`（`:1518`）も同じ穴で、そちらは Server の `ref_wav` 経路**（§3-e） |
| e' | 08 §4-F「`audio.py:47` の `except RuntimeError`」 | **REFUTED** | `audio.py:47` は **`except Exception as exc:`**。順序も soundfile(`:46`)→torchaudio(`:55`)→ffmpeg(`:58`) |
| f | chunk 連結は `torch.cat` のみ・`CHUNK_BOUNDARIES` に「、」 | **CONFIRMED** | `app.py:675`（crossfade・無音挿入なし）／`app.py:30` `frozenset("。、，,．.!！?？\n\r")` |
| g | SSE の `asyncio.shield`・`create_speech` が `Request` を取らない | **CONFIRMED** | `app.py:778-784`（**785 は空行**）／`app.py:315 async def create_speech(payload: SpeechRequest)`。`is_disconnected` grep 0 件 |
| h | 両 pyproject の rocm extra が `sys_platform == 'linux'` マーカー付き | **CONFIRMED** | gradio 側 `pyproject.toml:76`(torch)・`:82`(torchaudio)・`:86`・`:89`／Server 側 `:61`・`:66`・`:69`・`:72` |
| h' | Server `uv.lock` の win32×rocm で torch が PyPI 版に解決 | **CONFIRMED（引くべき行は別）** | 08 が引いた `uv.lock:99` は `accelerate` の従属欄。**根の証拠は `uv.lock:1871`（`[package.optional-dependencies] rocm`）と `:1874`（torchaudio）**（§3-h） |
| i | 複素数 RoPE・half-RoPE・`rf.py:459` ループ・`:464` の `t.item()`・早期停止なし | **CONFIRMED** | `model.py:30-42`（`:34`/`:39`/`:40`/`:41`）・`:247-250`／`rf.py:459`・`:464`・Euler `:582`。ループ本体 459-582 に `break`/`continue` 0 件。`.item()` は `:81,:207,:269,:464` の 4 箇所だけで全てループ外か schedule 由来 |
| j | `cfg_scale_caption` の既定が `default_cfg_scale_text` を流用・`config.py` に caption 用が無い | **CONFIRMED** | `app.py:932-939`（**key line は `:936`**）。`config.py` に `default_cfg_scale_caption` は不在（`:53` text／`:54` speaker のみ） |
| j' | `config.py:23` の既定 checkpoint が `Aratako/Irodori-TTS-v4-Small` | **CONFIRMED** | `config.py:23` 逐語 `hf_checkpoint: str = "Aratako/Irodori-TTS-v4-Small"` |
| k | `runtime.py` に unload 経路が無く `get_cached_runtime` を使わない | **CONFIRMED** | `runtime.py`（全 102 行）に `unload` 0 件・`:48` は `InferenceRuntime.from_key` 直呼び。`get_cached_runtime` の呼び元は `gradio_app*.py` のみ |
| l | `codec.py:82-96` の `alpha=0.0` と watermark 差し替え | **CONFIRMED（＋差し替えは必須と判明）** | `:84`・`:96`。`Decoder.watermark` は `alpha==0.0` で **`return x`**（`dacvae.py:455-456`）＝差し替えなしでは最終 96→1 ヘッドが落ちる（§3-l） |
| l' | `DecoderBlock.forward` が偶数チャンクだけ実行・`pad_mode="auto"` 層が経路外 | **CONFIRMED** | `dacvae.py:229-234`・`:159-227`・既定表 `:152-156`。実行 index = 0,1,4,5,8,9 |
| m | `AutoTokenizer` の import で pandas・pyarrow・sklearn が乗る | **部分 REFUTED** | lab 実測＝**sklearn は乗る／pandas・pyarrow は乗らない**。pandas は `sklearn.utils.fixes` の任意 import（§3-m） |
| m' | `import dacvae` で audiotools・tensorboard、`import silentcipher` で librosa | **CONFIRMED** | lab 実測。ただし 08 §2-B が STEP C に挙げた `soxr`・`joblib` は lab では**乗らない**（librosa の lazy_loader 止まり） |
| n | `tokenizer.py:78` BOS 手付け・EOS なし・`padding="max_length"`(:114)・256/512 | **CONFIRMED** | `tokenizer.py:78`・`:114`。safetensors ヘッダ実測で **v4-Small・v4.1-Small とも** `max_text_len=256`/`max_caption_len=512`（§3-n） |
| o | caption は `normalize_text` を通らない | **CONFIRMED** | `inference_runtime.py:1210` は `.strip()` のみ。`normalize_text` の推論経路の呼び出しは `:1091`（本文）**1 箇所だけ** |
| p | `gradio_app*.py` の CLI に device 引数なし・Dropdown に `allow_custom_value` なし | **CONFIRMED** | `gradio_app.py:642-645`／`gradio_app_voicedesign.py:660-663` は 4 引数のみ。`allow_custom_value` は **upstream 全 .py で 0 件** |
| q | `Settings` が `extra="ignore"`・`env_file=".env"`・`voices_dir=Path("voices")` | **CONFIRMED** | `config.py:13`・`:15`・`:41`（逐語） |

---

## 2. 反証を試みて反証できなかったもの（＝CONFIRMED の裏取り）

### (a) 機械照合の手順（再現可能）

```python
# lab/.venv の python。PYTHONDONTWRITEBYTECODE=1
import ast
S = {AnnAssign の target.id for c in ast.parse(inference_runtime.py) if c.name=="SamplingRequest"}
kw = {k.arg for k in ast の SamplingRequest( 呼び出し in app.py}
```

出力（`lab/out/18-field-diff.json` に保存）:

- `SamplingRequest` **43 欄**（`inference_runtime.py:202`〜`:246`）
- `IrodoriOptions` **44 欄**（`app.py:40`〜`:83`）
- `app.py:850` の `SamplingRequest(...)` に渡る kwargs **42 個**（`:851`〜`:1041`）
- **`S − kw = ['speaker_uncond_mode']`**（1 個だけ）
- `kw − S = []`（余計な欄を渡していない）
- **`S − IrodoriOptions = ['speaker_uncond_mode', 'text']`**（`text` は `payload.input` 由来）
- `IrodoriOptions − S = ['chunk_min_chars', 'chunking_enabled', 'first_sentence_chunk_min_chars']`（＝Server 側 chunking 専用 3 欄）

44 − 3 = 41 ＝ 43 − `text` − `speaker_uncond_mode`。**過不足なく一致**＝06 の「42/43 露出」は数え上げとして正しい。

### (d) 透かしの on/off が本当に無いか（4 経路すべて潰した）

| 経路 | 結果 |
|---|---|
| `SamplingRequest` | 43 欄に watermark 系なし（上の一覧） |
| `IrodoriOptions` | 44 欄に watermark 系なし |
| `config.py`（＝`IRODORI_*` 環境変数の全集合） | `Settings` の全 34 欄に watermark 系なし。`extra="ignore"`（`:15`）なので `IRODORI_WATERMARK=0` 等は**黙って捨てられる** |
| Server ツリー全体 | `grep -rni "watermark\|silentcipher" Irodori-TTS-Server/` ＝ **0 hits**（.py/.toml/.yaml/.example すべて） |

分岐は `inference_runtime.py:1433 if self.watermarker.ready:` の 1 本のみ。`ready` は `watermark.py:52-54` で `self.model is not None`＝**import と重み読み込みの成否だけ**。逐語（`:1443-1446`）:

```
"warning: SilentCipher watermark is unavailable; generated audio was not "
"watermarked."
```

### (i) ループ内にデータ依存の早期停止が無いこと

`rf.py:459-582` を全走査。ループ内の Python 分岐は 4 つで、いずれも**データ非依存**:

| 行 | 分岐 | 依存先 |
|---|---|---|
| `:464` | `use_cfg = bool(enabled_cfg_names) and (cfg_min_t <= t.item() <= cfg_max_t)` | t は `t_schedule`（`:206`）由来＝実行前に確定 |
| `:466` | `if use_independent_cfg` | 引数 |
| `:547` | `if rescale_k is not None and rescale_sigma is not None` | 引数 |
| `:556-561` | `speaker_kv_active and … (t_next < speaker_kv_min_t) and (t >= speaker_kv_min_t)` | schedule と引数 |

`break`／`continue`／`return` は本体に 0 件（`grep -n "break\|continue"` で `rf.py` 全体 0 件）。**必ず `num_steps` 回まわる**＝「1 ステップを export して外で回す」は成立（03 §3 の判定を支持）。

---

## 3. 反証できた／条件つきで覆したもの

### (c) `empty_cache` の実害は 1 箇所だけ（07 D1 の重大度を下げる）

`inference_runtime.py:1469-1471` は **`InferenceRuntime.unload()`（`:1464`）の中**にある。`unload()` の呼び元は
`get_cached_runtime`（`:1499`）と `clear_cached_runtime`（`:1512`）の 2 つだけで、**その 2 つを呼ぶのは `gradio_app.py` / `gradio_app_voicedesign.py` のみ**（`runtime.py` は `InferenceRuntime.from_key` を直呼び・`:48`）。

上流自身がこう書いている（`app.py:475-479` docstring・**逐語**）:

> `InferenceRuntime only releases its accelerator cache in unload(), which never`
> `runs while the server keeps a runtime resident. Long-lived serving therefore`
> `accumulates allocator blocks across requests.`

→ **Server 運用で毎回踏むのは `app.py:512` の 1 箇所だけ**（`_synthesize_once` の `finally`・`app.py:483-485`、周期は `IRODORI_EMPTY_CACHE_INTERVAL` 既定 10）。07 の D1 を「2 箇所」と書くと、gradio を使わない⒞案で過大な工数見積になる。**「Server 経路では 1 箇所・`IRODORI_EMPTY_CACHE_INTERVAL=0` で完全に回避可能（稼働機の bat は既に 0）」が正確**。

### (e) ImportError の穴は「CLI の wav 書き出し」に留まらない（重大・新規）

lab（torchcodec 未導入・torchaudio 2.10.0+cpu）での実測:

```
torchaudio.save(...)  -> ImportError: TorchCodec is required for save_with_torchcodec. …
   at torchaudio/__init__.py:178 save -> torchaudio/_torchcodec.py:248 save_with_torchcodec
irodori_tts.inference_runtime.save_wav(...)   -> ImportError（そのまま貫通）
irodori_tts.inference_runtime._load_audio(...) -> ImportError: TorchCodec is required for load_with_torchcodec. …
```

`except RuntimeError` の実在箇所は 3 つだけ（`grep -rn "except RuntimeError"`）:

| 檔:行 | 何を守る | 呼び元 | ⒜への影響 |
|---|---|---|---|
| `inference_runtime.py:1536` | `save_wav`（`:1530-1541`） | `infer.py` の CLI 出力 | CLI のみ |
| **`inference_runtime.py:1518`** | **`_load_audio`（`:1515-1527`）** | **`inference_runtime.py:936`＝参照音声の読み込み** | **Server の `ref_wav`/`ref_wavs` 経路が全滅** |
| `codec.py:272` | `DACVAECodec.encode_file`（`:269-282`） | **呼び元 0 件＝デッドコード**（grep 済み） | 影響なし |

`app.py:47`（Server の音声符号化）は **`except Exception`** で、しかも soundfile を**先に**試す（`audio.py:44-49`）＝ここは torchcodec と無関係。08 §4-F がこれを `except RuntimeError` と書いたのは誤り。

**含意（断定）**＝08 §4-F の「torchcodec は落としてよい」は、**参照音声つき voice を使う限り成立しない**。稼働機は torchcodec が「入っているが DLL で落ちる」＝`RuntimeError` なので `:1518` に救われているだけで、**同梱しない配布では `ImportError` になって救われない**。⒜の見積では上流パッチを
`inference_runtime.py:1518` と `:1536` の **2 箇所**（`except (RuntimeError, ImportError):`）と数えること。11 §4 の「1 行のパッチ 1 本」は過小。

### (m) transformers は pandas/pyarrow を引かない（08 §2-D の修正）

lab venv（transformers **5.16.1**・稼働機 ROCm venv と**同版**）で `sys.modules` 差分を実測:

| 実験 | 乗った（抜粋） | 乗らなかった |
|---|---|---|
| `from transformers import AutoTokenizer` | PIL, accelerate, **sklearn**, scipy, joblib, narwhals, sentencepiece, tokenizers, sympy, regex, safetensors, torch | **pandas・pyarrow**・numba・llvmlite・matplotlib・IPython |
| `import dacvae` | **audiotools・tensorboard**・absl・julius・einops・ffmpy・randomname・scipy・soundfile・torchaudio | — |
| `import silentcipher` | **librosa**・pydub・lazy_loader・scipy・soundfile・torchaudio | **soxr・joblib**・numba・llvmlite |

`__import__` を差し替えて「誰が最初に呼んだか」を採った結果（**一次証拠**）:

```
sklearn        first imported by -> transformers.generation.candidate_generator
pandas         first imported by -> sklearn.utils.fixes        # ← try/except の任意 import
narwhals       first imported by -> sklearn.utils.validation
joblib         first imported by -> sklearn.utils.validation
scipy          first imported by -> sklearn.utils._param_validation
sentencepiece  first imported by -> transformers.tokenization_utils_sentencepiece
sympy          first imported by -> torch.fx.experimental.symbolic_shapes
```

- `transformers/` 内に `import pandas` / `import pyarrow` / `import sklearn` の**直書きは 1 件も無い**（grep 0 件）。`sklearn` は `transformers.generation.candidate_generator` が引く。
- lab venv には pandas・pyarrow・datasets が**未導入**、稼働機 ROCm venv には `pandas-3.0.5` / `pyarrow-25.0.1` / `datasets-5.0.1` が**導入済み**。08 の測定は後者で行われたため pandas/pyarrow が観測された。
- pandas 3.0 は pyarrow を必須にするので、**`datasets` を入れなければ pandas も pyarrow も入らず、`AutoTokenizer` は問題なく動く**（lab で合成まで通っている＝11 §5）。

**含意（⒜の配布物）**＝08 §4 の「削れる 370 MB」に **pandas 46 MB＋pyarrow 86 MB（＋`.libs`）が追加で乗る**＝実測ベースで **約 500 MB** 削れる。`sklearn`(31 MB) は transformers が本当に引くので**削れない**（08 が削除候補に入れていないのは正しい）。

### (b) 07 の「未確認」を 2 つ埋めた（lab 実測・逐語）

```
'cpu'      -> OK cpu (index=None)         'cpu:0'  -> OK cpu:0 (index=0)   ← cpu も index を保持する
'cuda'     -> ValueError: CUDA device requested but torch.cuda.is_available() is False.
'cuda:1'   -> ValueError: （同上）        ← index 検査より先に is_available() で落ちる
'mps:0'    -> ValueError: MPS device index is not supported. Use 'mps'.
'xpu:0'    -> ValueError: XPU device index is not supported. Use 'xpu'.
'meta'     -> ValueError: Unsupported inference device=meta. Expected one of: cpu, cuda, mps, xpu.
'CUDA:1'   -> RuntimeError: Expected one of cpu, cuda, ipu, xpu, … at start of device string: CUDA
' cuda:1 ' -> RuntimeError: Invalid device string: ' cuda:1 '
bf16 on cpu-> ValueError: precision='bf16' currently requires CUDA or XPU device.
```

- 07 §6-7(iv)「`IRODORI_MODEL_DEVICE=CUDA:1` の大文字扱い」＝**`RuntimeError`**（`ValueError` ではない）。`_resolve_device` は lower した `raw` で auto 判定するが**戻り値は原文**（`runtime.py:99` vs `:102`）なので、大文字・空白付きは `torch.device()` で `RuntimeError`。→ Server では `unhandled_exception_handler`（`app.py:161`）が拾って **500**（400 にならない）。07 D7 の指摘は妥当。
- `cuda:1` の index 保持そのものは CPU 機では実測不能（`torch.cuda.is_available()` が False）。**コード上は `:62 return resolved` で保持が確定**、`torch.device("cuda:1").index == 1` も確認。**index の範囲検査が無いこと**（`device_count()` の参照が `resolve_runtime_device` に無い）はコードで確定＝07 D8 は CONFIRMED。実エラー文言は**依然 未確認**。

### (h) uv.lock はもっと強い行を引ける

08 が引いた `uv.lock:99` は `accelerate` の従属欄。**根の証拠**は Server `uv.lock` の**ルートプロジェクト**側:

| 行 | 内容 |
|---|---|
| `Irodori-TTS-Server/uv.lock:1869` | `rocm = [` （`[package.optional-dependencies]`・`name = "irodori-tts-server"` は `:1827`） |
| **`:1871`** | `{ name = "torch", version = "2.10.0", source = { registry = "https://pypi.org/simple" }, marker = "(sys_platform != 'linux' and extra == 'extra-18-irodori-tts-server-rocm') or …` |
| **`:1874`** | `{ name = "torchaudio", version = "2.10.0", source = { registry = "https://pypi.org/simple" }, marker = "(sys_platform != 'linux' and extra == 'extra-18-irodori-tts-server-rocm') …` |
| `:1872` / `:1875` | rocm7.1 index 側は `sys_platform == 'linux'` に限定 |

**「Windows で `--extra rocm` を打つと torch/torchaudio が PyPI 版になる」は lock の根の欄に書いてある**＝08 の結論は CONFIRMED、引く行だけ差し替えるとよい。
なお「その PyPI 版が CPU 版である」ことは lock に文字としては無い（**推論**）。支える事実 2 つ＝`torch-2.10.0-cp310-cp310-win_amd64.whl` の `size = 113720931`（`uv.lock:5219`＝108 MB）と、cu128 の wheel 行（`:5358-5378`）には `size` フィールドが**無い**こと（08 §6 の記述は CONFIRMED）。

### (l) `decoder.watermark` の差し替えは「保険」ではなく必須

`dacvae.py:454-456`（逐語）:

```python
    def watermark(self, x, message: Optional[torch.Tensor] = None):
        if self.alpha == 0.0:
            return x
```

＝`alpha = 0.0` だけなら `Decoder.forward`（`:448-451`）は最終段の 96 ch 特徴をそのまま返してしまう。
irodori の `_watermark_passthrough`（`codec.py:88-96`）が `wm_model.encoder_block.forward_no_conv(x)` を呼ぶことで、
`Snake1d(96) → NormConv1d(96→1, k=7) → Tanh → Identity`（`dacvae.py:277-298` ＋ `:326-332` が `pre[-1]` を Identity に差し替え）
＝**「潜在→波形」の最終ヘッド**が供給される。04 §2-4 の読みは正しく、しかも「差し替えは飾りではない」＝
⒝で ONNX 化するときに**この差し替え後のグラフを出さないと 96 ch のまま出力される**。実装時の落とし穴として報告に残す価値がある。

なお `codec.py:103-105` の latent_dim 推定用ダミー encode は `model.encode()`（`dacvae.py:715-722`）を呼ぶので、
**`_vae_sample`（`randn_like`）を 1 回だけ通る**。03 §6-1 の P「randn を通らない」は**推論経路については正しいが、codec ロード時には通る**（seed 再現性には影響しない＝サンプリングは専用 Generator・`rf.py:158`）。

---

## 4. 数値・メタデータの再検算（(n) の裏取り）

HF キャッシュの `model.safetensors` を**ヘッダだけ**読んだ（先頭 8 バイト＋JSON・テンソル本体は読まず・書き込みなし）。

| 項目 | v4-Small（`snapshots/4c92c7ee…`） | v4.1-Small（`snapshots/2b28324d…`） |
|---|---|---|
| ファイルサイズ | 3,064,295,596 B | 3,064,295,596 B |
| ヘッダ長 | 86,048 | 86,048 |
| テンソル数 | 714 | 714 |
| パラメータ数 | **766,052,385**（全 `F32`） | **766,052,385**（全 `F32`） |
| 検算 | `4×766,052,385 + 86,048 + 8 = 3,064,295,596` **＝一致** | 同左 |
| `max_text_len` / `max_caption_len` | **256 / 512** | **256 / 512** |
| `ref_max_seconds` | 120.0 | 120.0 |
| `latent_dim` / `latent_patch_size` / `speaker_patch_size` | 32 / 1 / 4 | 同左 |
| `model_dim` / `num_layers` / `num_heads` | 1280 / 12 / 20 | 同左 |
| `text_vocab_size` | 102400 | 102400 |
| `text_add_bos` | `True` | `True` |
| `text_tokenizer_repo` / `caption_tokenizer_repo` | `sbintuitions/modernbert-ja-310m` | 同左 |
| `__metadata__` のキー | `config_json`, `text_encoder_config_json` | 同左 |

→ 03 §5・05 §1-3 の諸元は**すべて CONFIRMED**（03 は v4-Small、05 は v4.1-Small を引いていたが**両者は同一値**）。
`text_add_bos: true` も確認＝`tokenizer.py:78` の BOS 手付けが実際に効く経路であることが重みメタデータ側から裏付いた。

---

## 5. 「0 件」系の主張の再 grep（すべて CONFIRMED）

| 主張（出所） | コマンド | 結果 |
|---|---|---|
| 上流に ONNX 実装なし（03 §7） | `grep -rni "onnx" --include=*.py` 両リポ | **0 hits**（`uv.lock` 除く全ファイルでも 0） |
| G2P・音素化なし（04 §1-5） | `grep -rniE "pyopenjtalk\|g2p\|phoneme\|jaconv\|mecab\|fugashi\|num2words"` 両リポ（`.git`/`uv.lock` 除く） | **0 hits** |
| einops/rearrange 不使用（03 §2） | `grep -rn "einops\|rearrange" --include=*.py` 両リポ | **0 hits**（ただし `dacvae` が実行時に `einops` を import する＝§3-m の表） |
| カスタム autograd／C++ 演算子なし（03 §6-1 S） | `grep -rn "autograd.Function\|load_inline\|cpp_extension\|torch.ops\."` | **0 hits** |
| ROCm 判定コード 0 件（07 §1-4） | `grep -rniE "is_hip\|version\.hip\|rocm" --include=*.py` | **3 hits のみ**＝`prepare_manifest.py:473,481,949`（エラー文言）。判定コードは 0 |
| `irodori_tts`／gradio が env を読まない（07 要点3） | `grep -rn "os\.environ\|getenv"` を `irodori_tts/`・`gradio_app*.py`・`infer.py` に | **0 hits** |
| `allow_custom_value` 不在（07 §1-2） | `grep -rn "allow_custom_value" --include=*.py` upstream 全体 | **0 hits** |
| Server に `speaker_uncond` なし（06 §1-7） | `grep -rn "speaker_uncond"` Server ツリー | **0 hits** |
| `is_disconnected` なし（06 §5-3） | `grep -rn "is_disconnected"` Server `src/` | **0 hits**。`Request` の出現は import(`:16`)と例外ハンドラ(`:144,:154,:161`)のみ |
| `set_device` なし（07 §2-1） | `grep -rn "set_device" --include=*.py` 両リポ | `train.py:1523` / `prepare_manifest.py:484` の 2 件のみ（学習・前処理） |

---

## 6. 行番号のずれ一覧（正しい行）

| 出所 | 書かれていた行 | **正しい行** | 影響 |
|---|---|---|---|
| 03 §6-1 K | `dacvae/model/dacvae.py:333-338`（`forward_no_conv`） | **`:326-332`**（`:333-338` は `post_process`） | 中（⒝の export 手順で参照される） |
| 03 §8 | `model.py:66`（RMSNorm の `x = x.float()`） | **`:67`** | 小 |
| 03 §8・要点10 | `inference_runtime.py:310-311`（bf16 の `raise`）／`:304-313` | **`:310`（raise）／`:304-312`** | 小（07 §4 の `:309-310`・`:304-312` が正） |
| 05 §1-2 行 2 | caption 空文字の `caption_mask.zero_()` を `:1211-1213` | **`:1215-1216`**（04 §1-4 が正） | 小 |
| 05 §1-2 行 19 | caption tokenizer を `:1207-1210` | **`:1211-1213`** | 小 |
| 05 §4-2 | `_infer_lock` を `:1180`、同期ブロックを `:1178-1184` | **`_infer_lock` は `:1184`（`with` は `:1183-1192`・`torch.inference_mode()` は `:1191`）**（06 の `:1183-1184` が正） | 小 |
| 05 §1-2 行 22 | `cfg_scale_caption` の既定流用を `app.py:930-937` | **`:932-939`（key line `:936`）**（06 の `:936` が正） | 小 |
| 05 §4-2・要点7 | `asyncio.shield` を `app.py:778-785` | **`:778-784`**（`:785` は空行） | 極小 |
| 07 §1-1 | `resolve_runtime_device` を `:55-76` | **`:55-77`**（`raise` は `:75-77`） | 極小 |

上記以外（06・08 の主要行番号、03 の `rf.py`・`model.py` の行、04 の `codec.py`・`dacvae.py`・`watermark.py` の行、07 の `pyproject.toml`・`config.py` の行）は**全て実測と一致**した。

---

## 7. 検分で新たに確定した事実（第 1 便のノートに無いもの）

1. **`codec.py:269-282` `DACVAECodec.encode_file` はデッドコード**（呼び元 0 件）。参照音声の読み込みは全て `inference_runtime.py:936` の `_load_audio` を通る。→ 上流パッチの対象は 2 箇所に絞れる。
2. **`cpu:0` は受理され index を保持する**（`resolve_runtime_device("cpu:0") -> cpu:0`）。`cpu` 側にも index 検査が無い。
3. **v4-Small と v4.1-Small はバイト数・テンソル数・パラメータ数・`config_json` の主要値が完全に同一**（重みの中身が同じとは限らない＝**未確認**）。「8088 の既定が v4 で取り残されている」（05 §6-6）という懸念は、**契約面（形状・上限値）では差が無い**＝⒞の API 設計には影響しない。
4. **lab venv と稼働機 ROCm venv の `dacvae/model/dacvae.py` はバイト一致**（両方とも git commit `414c20785fc3a28373073ea8ef7a1316eeeaca6e`）。04 が稼働機 venv を読んだ結論は lab でもそのまま成立する。
5. `transformers`／`torch` の版は両 venv で `5.16.1` / （lab 2.10.0+cpu・稼働機 2.13.0+rocm10.0.0）。**依存面積の差は `datasets` の有無**に集約される。

---

## 8. 停止域の確認（無改変の証明）

```
git -C upstream/Irodori-TTS        status --short --ignored   -> 出力なし
git -C upstream/Irodori-TTS-Server status --short --ignored   -> 出力なし
find upstream -name "__pycache__" | wc -l                     -> 0
```

- `PYTHONDONTWRITEBYTECODE=1` を全コマンドに付与。`HF_HUB_OFFLINE=1` で HF キャッシュは既存 snapshot の**ヘッダ読みのみ**（追加 DL なし・書き込みなし）。
- `C:/IrodoriTTS/**`・`C:/irodori-TTS-server/**`・`yomiwakechan2/**` は**読み取りのみ**（稼働機 venv の python.exe は 1 度も起動していない）。
- 8088／7861 への HTTP 送信・起動・停止は**していない**。GPU 未使用（torch CPU ビルド）。
- 本便が書いたのは `lab/notes/18-verify-code-claims.md`・`lab/out/18-field-diff.json`・`lab/tmp/verify_*.wav`（検証用ダミー）のみ。

---

## 9. 主席への注意（報告で書き直すべき箇所）

1. **⒜の「torchcodec を配らない」判断に条件を付けること**（最重要）。`inference_runtime.py:1518` の `except RuntimeError` は torchcodec 不在時の `ImportError` を捕まえず、その経路は **Server の参照音声（`ref_wav`）読み込み**（`:936`）。「torchcodec なし配布＋参照音声 voice」は上流無改造では**動かない**（lab 実測）。上流パッチは `:1518` と `:1536` の **2 箇所**（11 §4 の「1 行 1 本」は過小）。
2. **配布物の削減見積を上方修正できる**。08 §4 の「削れる ≒370 MB」に **pandas 46 MB＋pyarrow 86 MB** を足せる（`datasets` を入れなければ transformers は両者を要求しない・§3-m の import 追跡が一次証拠）。実測ベースで **≒500 MB**。逆に `sklearn`(31 MB) は transformers が実際に引くので削れない。
3. **08 §2-D の「transformers 5.x は最初の一発で pandas・pyarrow・sklearn まで持ってくる」は書き直すこと**。正しくは「transformers → sklearn（必須）→ pandas/pyarrow（**sklearn の任意 import・導入済みのときだけ**）」。⒝で transformers を外す価値の議論も、この分だけ目減りする。
4. **08 §4-F の `audio.py:47` の記述は誤り**（`except Exception`・順序は soundfile 先）。Server の wav/flac/pcm 出力は**最初から torchcodec に依存していない**＝「wav なら ffmpeg も torchcodec も不要」という⒜の結論自体は正しいまま。
5. **07 D1 の重大度を下げること**。`inference_runtime.py:1471` は `unload()` 内で Server は到達しない（上流の docstring `app.py:477-479` が明言）。多 GPU の実害は `app.py:512` の 1 箇所のみ・`IRODORI_EMPTY_CACHE_INTERVAL=0` で消える。⒞の工数から落としてよい。
6. **05 要点1 の「45 フィールド」を 43 に直すこと**（06 の「43 欄中 42 欄」が正）。§4-3 の答えの数字は報告の目玉なので、AST 実測値で統一を（証拠＝`lab/out/18-field-diff.json`）。
7. **`uv.lock` の引用行を差し替えると強くなる**＝`Irodori-TTS-Server/uv.lock:1871`／`:1874`（ルートプロジェクトの `rocm` extra が win32 で PyPI の torch/torchaudio に落ちる）。08 の `:99` は `accelerate` の従属欄で、同じ事実の傍証ではあるが根ではない。
8. **07 の未確認札を 2 つ外せる**＝`CUDA:1`（大文字）と前後空白付きは `torch.device()` の **`RuntimeError`**（`ValueError` ではない）で落ち、Server では 400 ではなく **500** になる。⒞のラッパで device 文字列を正規化（lower・strip）してから渡す設計を推奨に書けるだけの根拠がある。
9. **⒝の実装注意を 1 行足すこと**＝DACVAE decoder を export するときは `codec.py:88-96` の `_watermark_passthrough` **差し替え後**のグラフを出す。`alpha=0.0` だけでは `Decoder.watermark`（`dacvae.py:455-456`）が 96 ch のまま返し、**無音ではなく形の違う出力**になる（静かに壊れる型のバグ）。行番号は 03 §6-1 K の `:333-338` ではなく **`dacvae.py:326-332`**。
10. **残した未確認札**＝(i) `cuda:1` の実 index 動作と範囲外 index のエラー文言（CPU 機では検証不能）、(ii) PyPI の win_amd64 torch が CPU 版であること（lock の `size` と `nvidia-*` の linux マーカーからの推論）、(iii) 稼働機 ROCm venv での import 面積（venv の python を起動していないため lab 環境からの類推）、(iv) `soxr`/`joblib` が稼働機の STEP C で乗るか（lab では乗らなかった）。
