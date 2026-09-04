# 21 — CUDA 検証キット（RTX 3090 機で司令官が回す自己完結キット）

> 席＝第 2 便 b サブ席（opus）／担当＝BRIEF §4-7「検証機の要否＝CUDA 側は誰がどう検証するか」への答えと、
> CUDA 実測（RTF・VRAM・ドライバ・cu126 vs cu130・配布サイズ）の取得手段。実施 2026-09-04。
> 書いたのは `lab/kit-cuda/**`・`lab/out/kit-selftest-*`・`lab/tmp/**`・本ノートのみ。
> upstream clone・`C:/IrodoriTTS/`・`C:/irodori-TTS-server/`・HF キャッシュは 1 バイトも変えていない（§6）。
> 相対パスの基点は `C:\Users\mugonkun\source\repos\irodori-native-research\`。

## 要点（10 行以内）

1. キット＝`lab/kit-cuda/`（**608,408 B ＝ 594 KiB・11 檔**）。**git も Python も要らず uv だけ**・管理者権限不要・**キットの外に 1 バイトも書かない**（venv／uv キャッシュ／CPython／HF キャッシュ／結果すべてキット内）。
2. 中身＝upstream の `git archive` zip（`8224daf`・`.git` 無し）＋ dacvae/silentcipher の**純 Python wheel を作り置き**＋ PowerShell 5.1/7 両対応の実行スクリプト ＋ 合成ベンチのシム ＋ 汎用 ONNX ベンチ ＋ 日本語手順書 ＋ ダブルクリック用 .cmd 2 本。
3. **この機体で end-to-end 検証済み**（`-Variant cpu -Offline`）＝**5.9 分・script failures 0**・`results/summary.md` が出た（`lab/out/kit-selftest-cpu/summary.md`）。
4. **決定的**＝fp32 の 4 条件すべてで wav の sha256 が 11 §5 の基準と**一致**（`678d2384…`／`50b3f271…`／`ccf53374…`／`9142235546…`）＝3090 機で採る数字は CPU 基準と**そのまま倍率で比較できる**。
5. **cu130 の venv 作成〜torch 導入もこの機体で実測**＝`torch 2.10.0+cu130`（`torch.version.cuda=13.0`・cuDNN 91200）が **38.8 秒**で入り、venv **3191.6 MB** ／ `torch/` **2649.5 MB** ／ `torch/lib` **2554.1 MB**。
6. wheel の実バイト（HEAD 実測）＝**cu130 1,867,405,006 B（1781 MiB）／cu126 2,589,881,452 B（2470 MiB）**。**cu126・cu130 とも `torch 2.10.0` の cp312/win_amd64 が実在**＝キットの pin `torch>=2.10,<2.11` は両方で成立（フォールバック不要）。
7. `--compile-model` は Windows CPU で `InductorError: RuntimeError: Compiler: cl is not found.`＝**Triton ではなく MSVC の `cl` 不在**が原因（11 §10 注 3 の見立ての訂正）。CUDA 側の可否は 3090 機で確定させる。
8. bf16 は CPU で `ValueError: precision='bf16' currently requires CUDA or XPU device.`。キットは**「測定結果としての失敗（result-FAIL）」と「スクリプトの失敗（FAIL）」を別札**にして記録する。
9. ONNX 段は `onnx/` に `*.onnx` を置いたときだけ動く任意段。CPU EP で素振り済み（3 檔中 2 檔成功・1 檔は動的軸の既定値が小さすぎて Pad で落ちた＝`<名>.inputs.json` で直す設計）。
10. **BRIEF §4-7 への答え**＝CUDA の検証は**司令官の 3090 機で 1 variant あたり 15〜25 分・費用 0**で足りる。GitHub Actions の `windows_4_core_gpu`（$0.102/分・10 §5）は「torch の版が上がるたびの回帰」用に残す。

---

## 1. キットの構成（`lab/kit-cuda/`）

| 檔 | バイト | 役目 |
|---|---:|---|
| `README-kit.md` | 9,945 | **司令官向け手順書**（日本語・1 行 1 操作）。所要時間・DL 量・空き容量・失敗時の見方 |
| `run-cu130.cmd` / `run-cu126.cmd` | 各 189 | ダブルクリック用ランチャ。**CRLF・ASCII**（08 §1-C の bat 事故対策） |
| `run-cuda-kit.ps1` | 35,669 | 本体。PS 5.1／7 両方でパース確認済み。**ASCII のみ**（理由は §2-2） |
| `bench_infer.py` | 11,909 | 合成ベンチのシム（`lab/bin/infer_cpu.py` と同型＋GPU 欄・コールド/ウォーム分離） |
| `bench_onnx.py` | 7,393 | 汎用 ONNX ベンチ（CUDA EP / DirectML EP / CPU EP） |
| `torch_check.py` | 1,113 | venv の torch 素性を JSON に落とす（§2-3 の引用符事故対策） |
| `cases.json` | 637 | 短文・長文・caption の**日本語文面**（11 §5 と同一） |
| `summary-header.md` | 669 | `results/summary.md` の先頭に貼る日本語の読み方 |
| `Irodori-TTS-8224daf.zip` | 505,778 | `git archive --prefix=Irodori-TTS/ 8224dafb46d0aba89209a8f905f1cb7e3299d9c1`。**52 エントリ・`.git` 無し**。sha256 `0c1a486dd51d9162ada65d53d21951f23bd878785c5a87a3219d52a1a7658829` |
| `wheels/dacvae-1.0.0-py3-none-any.whl` | 22,626 | git commit `414c20785fc3a28373073ea8ef7a1316eeeaca6e`（11 §3 と一致）。sha256 `411e7aa2…` |
| `wheels/silentcipher-1.0.5-py3-none-any.whl` | 12,291 | git commit `d46d7d0893a583d8968ab3a6626e2289faec9152`（upstream の釘と一致）。sha256 `92c02958…` |

- wheel はどちらも `Tag: py3-none-any` / `Root-Is-Purelib: true`（`WHEEL` 実読）＝**3090 機に git もコンパイラも要らない**。
  `descript-audiotools` など残りの依存は PyPI から素直に取れるので同梱していない。
- スクリプトが自動で作るのは `src/`（zip 展開先）・`.venv-<variant>/`・`uv-cache/`・`python/`（uv 管理の CPython 3.12）・`hf/`・`results/`・`onnx/`。**すべてキット内**。

### 実行の引数

```
run-cuda-kit.ps1 [-Variant cu126|cu130|cpu] [-KitRoot <dir>] [-Offline] [-HfHome <dir>]
                 [-Checkpoint <repo>] [-Device cuda|cuda:1|cpu] [-Repeats 3] [-Threads 8]
                 [-Quick] [-SkipBench] [-SkipOnnx]
```

既定＝`-Variant cu130`・`-KitRoot` はスクリプトの場所・`-HfHome` は `KitRoot\hf`・`-Checkpoint Aratako/Irodori-TTS-v4.1-Small`（11 §5 と同じ checkpoint）。

## 2. 設計の釘（なぜこの形か）

### 2-1 キットの外に書かない

`UV_CACHE_DIR`＝`KitRoot\uv-cache`、`UV_PYTHON_INSTALL_DIR`＝`KitRoot\python`、`HF_HOME`＝`KitRoot\hf`、venv も `KitRoot` 直下。
`UV_NO_CONFIG=1` で利用者の `uv.toml` も無視する。**アンインストールはフォルダごと消すだけ**。

### 2-2 `.ps1` は ASCII のみ・日本語は別檔

Windows PowerShell 5.1 は **BOM 無しの檔を ANSI（cp932）として読む**ため、`.ps1` に日本語を書くと化ける。
BOM を付ける手もあるが、編集・転送で落ちると無言で壊れる。よって
**`.ps1` は ASCII のみ**にし、合成に渡す日本語（短文・長文・caption）は `cases.json`（UTF-8）に置き、
`bench_infer.py` が `--cases-json/--case` で**自分で読み込んで `--text`/`--caption` を組み立てる**。
＝**日本語が PowerShell を一度も通らない**。`summary.md` の日本語見出しも `summary-header.md` を `-Encoding UTF8` で読んで貼るだけ。

### 2-3 PowerShell → ネイティブ exe の引用符剥がれ

`python -c "…json.dumps({\"v\":1})…"` は PS 5.1 で二重引用符が剥がれ `NameError: name 'v' is not defined` になる（当便が実際に踏んだ）。
→ **`torch_check.py` という檔に逃がし、結果は `results/torch.json` に書かせて PS 側はファイルを読む**。

### 2-4 コールド／ウォームの分離

`upstream/Irodori-TTS/infer.py` は `InferenceRuntime.from_key` を**キャッシュ無しで**呼ぶ（`upstream/Irodori-TTS/infer.py:397`）ので、
プロセスを分けて 3 回まわすと毎回モデル読み込みからになり、**CUDA の形状チューニング（01 §1.3＝未見の音声長は初回 4〜5 倍遅い）が毎回コールド**になって「常駐サーバでの実効値」が取れない。
→ シムは `irodori_tts.inference_runtime.InferenceRuntime.from_key` を **memo 化してから同一プロセスで N 回 `runpy`** する。
＝**1 発目＝コールド（読み込み＋形状チューニング込み）／2 発目以降＝ウォーム**。`runtime_memoized: true` が JSON に残る。

### 2-5 その他

- `torchaudio.save` → soundfile 差し替え（11 §4 と同じ。Windows の torchcodec 問題）。
- `infer.py --show-timings` の `[timing]` 行を捕まえて段別時刻を JSON 化。
- **torch のすり替え防止**＝手順 (d) の依存解決で PyPI の素の torch に落とされていないか `torch_check.py` で検査し、
  variant タグが消えていたら `--reinstall-package torch/torchaudio` で pytorch index から入れ直す（`d_torch_guard`）。
- 全段 `try/catch`。落ちても次へ進み、`results/steps.json` と `results/logs/*.log` に**逐語**が残る。
- ステータスは 3 種＝`ok` ／ **`result-FAIL`（測定結果としての失敗。例＝bf16 on CPU）** ／ `FAIL`（スクリプトの問題）。

## 3. この機体での end-to-end 検証（`-Variant cpu -Offline`）

コマンド（`lab/tmp/kit-test/` に KitRoot をコピーして実行）:

```
powershell -NoProfile -ExecutionPolicy Bypass -File .\run-cuda-kit.ps1 -Variant cpu -Offline -HfHome "C:\Users\mugonkun\.cache\huggingface" -SkipOnnx
```

**結果＝5.9 分・script failures 0・measured failures 4（bf16×4）**。生データ＝`lab/out/kit-selftest-cpu/`。

| 精度 | steps | 文 | 音声長 s | cold total_to_decode s | warm 中央値 s | warm RTF | wav sha256 先頭 | 11 §5 の基準 | 判定 |
|---|---|---|---:|---:|---:|---:|---|---|---|
| fp32 | 40 | 短 | 3.76 | 16.439 | 16.094 | 4.28 | `678d2384…` | 15.262 s / 4.06 | **sha 一致**・時間 +5% |
| fp32 | 10 | 短 | 3.76 | 6.693 | 6.473 | 1.722 | `50b3f271…` | 6.725 s / 1.79 | **sha 一致**・時間 −4% |
| fp32 | 40 | 長 | 10.92 | 40.445 | 38.463 | 3.522 | `ccf53374…` | 40.292 s / 3.69 | **sha 一致**・時間 −5% |
| fp32 | 10 | 長 | 10.92 | 14.554 | 14.956 | 1.37 | `9142235546…` | 14.047 s / 1.29 | **sha 一致**・時間 +6% |
| bf16 | 40/10 | 短/長 | — | — | — | — | — | — | **result-FAIL**（下記） |

- **sha256 は 4 条件すべて 11 §5 の表と一致**（`678d2384f9017ada…`／`50b3f271f200ee31…`／`ccf53374dd1d2e2e…`／`9142235546bfb809…`）。
  ＝キットは 11 の CPU 基準を**ビット単位で再現**する。3090 機の数字は同じ物差しで読める。
- 時間の ±6% は当便が同時に別作業をしていたための機体ノイズ。**判断に効かない**。
- bf16 の逐語（4 条件とも同一）＝`ValueError: precision='bf16' currently requires CUDA or XPU device.`（11 §6 の `list_available_runtime_precisions` の通り）
- 段別時刻（warm・ms）も採れている＝例 `fp32_steps40_短`: `predict_duration 1084.6 / sample_rf 13764.8 / decode_latent 920.3 / watermark 322.8`。
  11 §5 の「`sample_rf` が支配的・固定費 2.4 s」がそのまま再現。

### `--compile-model`（BRIEF §4-5・11 §10 注 3 の答え合わせ）

CPU 経路での逐語（`lab/out/kit-selftest-cpu/logs/compile.log`）:

```
InductorError: RuntimeError: Compiler: cl is not found.
Set TORCHDYNAMO_VERBOSE=1 for the internal stack trace ...
```

→ **CPU で `torch.compile` が通らない理由は Triton ではなく MSVC の `cl.exe` 不在**。11 §10 注 3 の「Triton が無いため期待薄」は CPU については**訂正**（Inductor の CPU バックエンドは C++ を吐いて `cl` でコンパイルする）。
CUDA 経路では Triton が要るので別の失敗になり得る＝**3090 機の `results/compile.json` で確定する**。
※⒜専用インストーラへの含意＝compile を使うなら**利用者機に Build Tools を要求する**ことになる＝初心者向けには外す方向。

### ONNX 段の素振り（CPU EP）

`lab/onnx/` の 3 檔をコピーして `bench_onnx.py --ep cpu --runs 10` を実行（`lab/out/kit-selftest-onnx-cpuep.json`）:

| モデル | 中央値 ms | CPU EP 比の最大差 | 備考 |
|---|---:|---:|---|
| `sc_enc_c_fp32.onnx` | 65.25 | 0.0 | 入力形状は静的軸をそのまま採用 |
| `sc_istft_slim.onnx` | 33.858 | 0.0 | 同上 |
| `sc_stft.onnx` | — | — | `Pad reflect: pre-pad (2048) exceeds maximum allowed (127) for axis 2. Input shape:{1,1,128}` |

→ **動的軸の既定値（batch=4・その他 128）では小さすぎる檔がある**＝`<名>.inputs.json` を隣に置いて形を指定する設計が要る、という設計判断が実測で裏づけられた。README-kit.md §8 に書式を書いた。

## 4. cu130 の導入だけこの機体で実測（GPU 無しでも取れる分）

```
powershell -NoProfile -ExecutionPolicy Bypass -File .\run-cuda-kit.ps1 -Variant cu130 -Offline -HfHome "C:\Users\mugonkun\.cache\huggingface" -SkipBench -SkipOnnx
```

生データ＝`lab/out/kit-selftest-cu130/`（`install.json`・`env.json`・`summary.md`）。**1.6 分・script failures 0**。

| 項目 | 実測 |
|---|---|
| 入った torch | **`2.10.0+cu130`**（`torch.version.cuda = 13.0`・`torch.backends.cudnn.version() = 91200`＝cuDNN 9.12.0） |
| pin の成否 | `torch>=2.10,<2.11` / `torchaudio>=2.10,<2.11` が**そのまま解決**（`fallback_used: false`） |
| `c_torch` 所要 | **38.8 秒**（当機の回線で 1.78 GiB のダウンロード＋展開込み） |
| `d_deps` 所要 | 34.2 秒 |
| venv 合計 | **3191.6 MB** |
| `torch/` | **2649.5 MB**（うち `torch/lib` **2554.1 MB**） |
| `torchaudio/` | 9.5 MB |
| uv キャッシュ（**展開後**・DL バイト数ではない） | 3196.4 MB（torch 直後 2690.8 MB） |
| `torch.cuda.is_available()` | **False**（当機に NVIDIA が無いため。スクリプトはここで警告を出す） |
| `--compile-model` | `ValueError: CUDA device requested but torch.cuda.is_available() is False.`（想定どおり） |
| `torch/lib` の大物（`du` 実測） | `cublasLt64_13.dll` 456 MB／`torch_cuda.dll` 391 MB／`cufft64_12.dll` 272 MB／`torch_cpu.dll` 253 MB／`cudnn_engines_precompiled64_9.dll` 180 MB／`cusparse64_12.dll` 144 MB |

### wheel の実バイト（`download.pytorch.org` に HEAD・2026-09-04）

| wheel（cp312 / win_amd64） | バイト | MiB |
|---|---:|---:|
| `torch-2.10.0+cu130` | 1,867,405,006 | **1780.9** |
| `torch-2.10.0+cu126` | 2,589,881,452 | **2469.9** |
| `torchaudio-2.10.0+cu130` | 2,008,471 | 1.9 |
| `torchaudio-2.10.0+cu126` | 1,800,247 | 1.7 |
| `torch-2.10.0+cpu`（比較） | 113,671,330 | 108.4 |

- 索引の実在確認（cp312/win_amd64）＝**cu130・cu126 とも `torch` は 2.10.0 / 2.11.0 / 2.12.0 / 2.12.1 / 2.13.0 / 2.14.0**、`torchaudio` は **2.10.0 / 2.11.0 のみ**。
- 10 §4-1 は 2.14.0 を測っていた（cu130 1898.4 MiB／cu126 2482.2 MiB）。**2.10.0 は cu130 で −117 MiB・cu126 で −12 MiB 小さい**＝上流の釘（`torch>=2.10.0`）どおり 2.10 に留めるほうが配布は軽い。
- `torch/lib` の展開後 2554.1 MB は 10 §4-2 の HTTP Range 読み（DLL 合計 2737.5 MiB・2.14.0）と整合。**実際に展開して裏が取れた**。

## 5. 3090 機から持ち帰る `results/` を主席がどう読むか

| 檔 | 見る欄 | 何が分かるか |
|---|---|---|
| `summary.md` §3 | **`warm RTF`** | **1.00 を切れば実時間より速い**。CPU 基準（4.06 / 1.79 / 3.69 / 1.29）との比が「GPU 化の効き目」。⒜⒞ の成立判定に直結 |
| `summary.md` §3 | `cold` と `warm` の差 | **CUDA の形状チューニング代**（01 §1.3 の「未見の音声長は初回 4〜5 倍」が NVIDIA でも出るか）。⒞ の「先読み」設計の要否 |
| `summary.md` §3 | **`VRAM peak MiB`** | `torch.cuda.max_memory_allocated()` の実測。fp32 と bf16 の差＝**最低 VRAM 要件**（配布の下限スペック） |
| `summary.md` §3 | bf16 行が `ok` か | **bf16 が NVIDIA で本当に効くか**。稼働機の ROCm では 0.77 秒／RTF 約 0.2 まで落ちた（01 §要点 2）＝同じ倍率が出るかが焦点 |
| `summary.md` §3-1 | `sample_rf` / `decode_latent` / `predict_duration` / `watermark` | **⒝ONNX 化の優先順**。11 §5 の CPU では `sample_rf` が 84%。GPU で固定費（watermark・decode）の比率が上がるなら ⒝ の効き所が変わる |
| `summary.md` §2 | `torch/ MB`・`venv total MB` | **⒜ の配布物サイズの確定値**（10 §4-6 の見積の裏取り） |
| `summary.md` §1 の `nvidia-smi` | Driver Version | **cu130 は 580 以上・cu126 は 527.41 以上**（10 §6）。3090 機の実ドライバがどちらを満たすか＝利用者に何を要求するかの根拠 |
| `bench_*.json` の `runs[].wav_sha256` | — | **CPU 基準と一致するか**。一致しなければ「GPU で数値が変わる」＝⒝の照合基準を GPU 側で別に採る必要がある（重要な分岐） |
| `bench_*.json` の `torch.devices[]` | `name` / `total_memory_mib` / `capability` | 機体の同定。複数 GPU 機なら `-Device cuda:1` で 2 枚目も測れる（BRIEF §4-4） |
| `compile.json` | `runs[0].error` | **Windows+CUDA で `torch.compile` が通るか**。通れば ⒞ の高速化の札が 1 枚増える |
| `onnx_cuda.json` / `onnx_dml.json` | `median_ms` / `max_abs_diff_vs_cpu_ep` / `session_error` | **⒝ の本命 DirectML EP が NVIDIA で使い物になるか**（10 §8-4 の三つの棘の実測） |
| `install.json` | `sizes_mb` / `uv_cache_mb_final` | 配布物の実寸。**`uv_cache_mb_final` は展開後で DL バイト数ではない**（DL 量は §4 の wheel サイズを使う） |
| `steps.json` | `status` | `FAIL` ならスクリプトの問題（`logs/FAIL_*.txt` に逐語）。`result-FAIL` は**測定結果**であって不具合ではない |

**cu130 と cu126 の 2 回を比べるとき**＝variant ごとに `bench_cu130_*.json` / `bench_cu126_*.json` と別名になるので同じ `results/` に共存する。
`summary.md` は**後から回したほうで上書きされる**ので、1 回目が終わった時点で `results` を zip して退避しておくのが安全（README-kit.md §3 手順 7 に書いた）。

## 6. 停止域の確認

| 対象 | 確認 |
|---|---|
| `upstream/Irodori-TTS` / `upstream/Irodori-TTS-Server` | `git status --short --ignored` が**両方とも空**（clean）。`__pycache__` は 0 個。読んだのは `git archive` と `infer.py` の閲覧のみ |
| `C:\IrodoriTTS\` ・ `C:\irodori-TTS-server\` | `find ... -newermt "2026-09-04 02:35"` が**0 件**＝当便の作業開始以降に 1 檔も変わっていない。`voices/` にも触れていない・voices API も叩いていない |
| HF キャッシュ | `HF_HUB_OFFLINE=1` を全実行に付与。`hub` の総量 **6,335 MB**（11 §9 の 2929+2929+410+68=6336 と一致）＝**追加ダウンロード無し**。repo ディレクトリも 4 つのまま |
| 稼働機のサーバ | **起動も停止もしていない**（当便は GPU も 8088/7861 も使わない設計だったため不要だった） |
| 当便が書いた場所 | `lab/kit-cuda/**`・`lab/out/kit-selftest-cpu/`・`lab/out/kit-selftest-cu130/`・`lab/out/kit-selftest-onnx-cpuep.json`・`lab/tmp/{kit-test,kit-cu130,kitsrc,onnx-test}`・`lab/notes/21-cuda-kit.md` |
| 外部公開 | 無し（HF/GitHub への push 0。GitHub からの clone と PyPI/pytorch index からの取得のみ） |

`lab/tmp/kit-test` と `lab/tmp/kit-cu130` は検証後に venv・uv キャッシュ・展開済み src を削除済み（合計 4.6 GB を回収）。結果は `lab/out/` に写した。

## 7. 未確認（3090 機でしか分からないこと）

- **CUDA での RTF・VRAM・コールド/ウォーム差**＝本キットの目的そのもの。当機では取れない。
- **Windows + CUDA での `torch.compile`**（Triton の有無）。CPU 側は `cl` 不在で落ちることまでは確定した。
- **`onnxruntime-gpu` の extras 名**＝`onnxruntime-gpu[cuda,cudnn]` が通るかは**未確認**。通らなければ `nvidia-*-cu13` を明示、それも駄目なら素の `onnxruntime-gpu` と 3 段で退く実装にしてある（`h_onnx_cuda`）。
- **DirectML EP が NVIDIA 上でどう振る舞うか**（10 §8-4 の 3 つの棘）。`onnx/` に檔を置いたときだけ測る任意段。
- **`-Device cuda:1` の複数 GPU 指定**＝3090 機が 1 枚なら試せない。コードの受けは作ってある。
- キットを **PowerShell 5.1 で実際に走らせた**のは当機（5.1.26100.9168）だけ。7 系は**パース確認のみ**で実走は未実施。
- 3090 機に **uv が無い場合の `winget install` の実挙動**は未確認（README の手順は 08 §1-C の稼働機実績に倣った）。

## 8. 主席への注意

1. **BRIEF §4-7 の「検証機の要否」は、CUDA 側の減点をほぼ消せる**。司令官が 3090 機を持っている以上、
   `kit-cuda` をコピーして 2 回走らせるだけ（**1 variant 15〜25 分・費用 0・管理者権限不要・アンインストールはフォルダ削除**）で
   RTF・VRAM・ドライバ・配布サイズが揃う。クラウド GPU（10 §5）は**「torch の版が上がるたびの回帰」用の退路**として残せばよい。
2. **cu126 と cu130 の選択は「サイズ vs ドライバ」だけの話ではない**。当便の実測で **cu130 は torch 2.10.0 で 1781 MiB・cu126 は 2470 MiB**、
   差は **689 MiB**。ドライバ要件は 580+ vs 527.41+（10 §6）。**どちらも `torch 2.10.0` が実在するので上流の釘は満たせる**＝
   「cu128 は袋小路」（10 §8-2）に加えて「**cu126/cu130 のどちらでも上流の pin は通る**」が確定した。決めるのは配布方針だけ。
3. **キットは CPU 基準をビット単位で再現する**（sha256 が 4 条件とも一致）。よって 3090 機の数字は
   11 §5 の表と**そのまま割り算して倍率を出してよい**。別の物差しに翻訳する必要はない。
4. **`torch.compile` は ⒜ の武器にしないほうがよい**。CPU では MSVC の `cl` を要求して落ちる＝
   初心者配布で Build Tools を要求することになる。CUDA 側で通ったとしても「通る機体と通らない機体が出る」性質の機能。
5. **`uv cache` の MB を配布サイズと読み違えないこと**。`install.json` の `uv_cache_mb_final`（cu130 で 3196 MB）は**展開後**の数字で、
   実ダウンロードは wheel の 1781 MiB＋依存 ≒250 MB。`summary.md` にもその注記を入れてある。
6. キットは **594 KiB** しかない（重いものは全部その場で取りに行く）＝**チャットに添付しても USB に入れても渡せる**。
   渡し方＝フォルダごとコピー →`run-cu130.cmd` をダブルクリック → 終わったら `run-cu126.cmd` → `results` を zip で返送。
