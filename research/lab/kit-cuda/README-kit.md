> **最上段の釘＝このフォルダを丸ごとコピーしない・N: 上で直接走らせない。** SSD で回すときは隣の `kit-ssd`（約 90 ファイル・7.5 GB）を使う＝手順は `kit-ssd/README-ssd.md`。N: 直走行は `import torch` だけで 5〜9 分かかる。

# CUDA 検証キット（RTX 3090 機で司令官が実行する）— 手順書

> このフォルダを **丸ごと** RTX 3090 の PC にコピーして、PowerShell から 1 行打つだけ。
> git も Python も uv も要りません（**uv.exe はキットに同梱**。Python 3.12 は uv がキット内に取ります）。
> **管理者権限は不要**。キットは**このフォルダの外に何も書きません**（venv・uv キャッシュ・Python 本体・モデル置き場・結果、すべてフォルダ内）。

---

## 0. 事前にそろえるもの

| 項目 | 必要な値 | 確かめ方 |
|---|---|---|
| OS | Windows 10 / 11 の 64bit | — |
| PowerShell | 5.1 でも 7 でも可（どちらでも動くように書いてある） | — |
| NVIDIA ドライバ | **cu130 を測るなら 580 以上**／**cu126 を測るなら 560.76 以上**（CUDA 12.6 GA の Windows 最小値） | PowerShell で `nvidia-smi` と打つ。右上に Driver Version が出る |
| ネット | **約 6 GB** ダウンロードする（内訳は §4） | — |
| ディスク空き | **25 GB 以上**（キットを置くドライブ） | — |
| GPU を使う他のソフト | **止めておく**（配信・ゲーム・別の TTS）。VRAM の実測が汚れる | — |

---

## 1. uv について（**同梱済み＝手順は不要**）

- このキットには `uv/uv.exe`（0.12.7・MIT/Apache-2.0 二重ライセンス・`uv/LICENSE-uv-*`）を**同梱**しています。
  スクリプトは **同梱の `uv/uv.exe` を優先**して使い、無いときだけ PATH の `uv` を探します。
- 3090 機に uv や Python を入れてあっても構いませんが、**使いません**（Python 3.12 も uv がキット内に自前で取ります）。
- 何も入れなくても動きます。手順 2 へ進んでください。

---

## 2. キットをコピーする

1. このフォルダ（`kit-cuda`）を丸ごと 3090 機の**空きの多いドライブ**にコピーする（例 `D:\kit-cuda`）。
2. コピーしたフォルダを開いて、`run-cuda-kit.ps1` があることを確認する。

---

## 3. 実行する（cu130 → cu126 の順に 2 回）

1. コピー先フォルダを**エクスプローラで開く**。
2. アドレスバーに `powershell` と打って Enter（そのフォルダで PowerShell が開く）。
3. **一番簡単な方法**＝エクスプローラで `run-cu130.cmd` を**ダブルクリック**する（終わったら `run-cu126.cmd` も）。
   以降の手順 3〜5 はコマンドで打ちたいときだけ読めばよい。
4. 1 回目（**cu130**・既定）:
   ```
   powershell -ExecutionPolicy Bypass -File .\run-cuda-kit.ps1 -Variant cu130
   ```
5. 終わったら 2 回目（**cu126**）:
   ```
   powershell -ExecutionPolicy Bypass -File .\run-cuda-kit.ps1 -Variant cu126
   ```
6. 画面の最後に `done in ... min` と `summary : ...\results\summary.md` が出れば終わり。
7. `results` フォルダを右クリック →「送る」→「圧縮 (zip 形式) フォルダー」で zip にして持ち帰る。

> 2 回目は **モデル 3.5 GB を再取得しません**（`hf` フォルダを共有するため）。torch の入れ替えだけです。
> 途中で止めたくなったら **Ctrl+C**。もう一度同じコマンドを打てば、できているところは飛ばして続きから走ります。

### 使える追加スイッチ（必要なときだけ）

| スイッチ | 意味 |
|---|---|
| `-Quick` | 動作確認用の短縮版（各 1 回・短文 steps10 のみ）。2〜3 分で終わる |
| `-Repeats 5` | 各条件の繰り返し回数（既定 3） |
| `-Device cuda:1` | **2 枚目の GPU** で測る（複数 GPU 機のとき。BRIEF §4-4 の材料になる） |
| `-Variant cpu` | GPU を使わず CPU で測る（比較用） |
| `-SkipOnnx` | ONNX のベンチを飛ばす |
| `-Offline` | HF を一切叩かない（モデルが既にある場合のみ） |
| `-Checkpoint Aratako/Irodori-TTS-v4-Small` | 別のチェックポイントで測る（既定は v4.1-Small） |

---

## 4. 所要時間・ダウンロード量の目安

| 段 | ダウンロード | 所要（目安） |
|---|---|---|
| uv が入れる CPython 3.12 | 約 30 MB | 10 秒 |
| torch + torchaudio（**cu130**） | **1781 MiB + 2 MiB**（`torch-2.10.0+cu130-cp312-cp312-win_amd64.whl` の実バイト＝1,867,405,006） | 回線 100 Mbps で 3〜5 分 |
| torch + torchaudio（**cu126**） | **2470 MiB + 2 MiB**（同 2,589,881,452 B） | 4〜7 分 |
| 残りの依存（transformers ほか約 80 個） | 約 250 MB | 1 分 |
| モデル（初回だけ・2 回目は再利用） | **約 3.5 GB**（本体 2.86 GiB ＋ コーデック 410 MiB ＋ 透かし 68 MB） | 3〜8 分 |
| 合成ベンチ 8 条件 × 3 回 | 0 | GPU なら 3〜6 分（CPU だと 8 分） |
| **合計（1 回目・cu130）** | **約 5.6 GB** | **15〜25 分** |
| **合計（2 回目・cu126）** | **約 2.7 GB**（モデルは再利用） | **10〜20 分** |

ディスクは 2 variant 走らせ終わった時点で **約 16 GB** 使います（venv 2 個・uv キャッシュ・モデル）。25 GB 空けておけば安全です。

---

## 5. 終わったら何を見るか

`results\summary.md` を開くと表が並んでいます。見どころは 3 つ。

1. **§3 の `warm RTF`** — これが **1.00 を切っていれば「実時間より速い」**＝読み上げに使える。
   司令官機の CPU 実測は 短文 steps40 で 4.06、steps10 で 1.79（＝どちらも実時間より遅い）。
2. **§3 の `VRAM peak MiB`** — 3090 の 24 GB に対してどれだけ食うか。fp32 と bf16 で 2 倍近く変わるはず。
3. **§4 の torch.compile** — 成功したか、失敗したならその逐語のエラー。

`results` の中身:

| 檔 | 中身 |
|---|---|
| `summary.md` | 上記の表（**まずこれ**） |
| `env.json` | `nvidia-smi` の全文・OS 版・空き容量・PowerShell 版 |
| `install.json` | 入った torch の版・venv の MB・torch/ の MB・ダウンロード量 |
| `bench_<variant>_<精度>_steps<数>_<短長>.json` | 1 条件ぶんの生データ（3 回ぶんの段別時刻・VRAM・wav の sha256） |
| `compile.json` | `--compile-model` の成否と逐語エラー |
| `onnx_cuda.json` / `onnx_dml.json` | ONNX のベンチ（`onnx\` に .onnx を置いたときだけ） |
| `steps.json` | どの段が何秒で成否どうだったか |
| `logs\*.log` | すべてのコマンドの出力（逐語） |
| `wav\*.wav` | 生成された音声（耳で確かめる用） |

---

## 6. うまくいかないときの見方

| 症状 | 見るところ | たいていの原因 |
|---|---|---|
| `FATAL: uv was not found` | — | 手順 1 をやり直す。**ターミナルを開き直す**のを忘れがち |
| `summary.md` の §2 で `torch.cuda.is_available()` が **False** | `results\env.json` の `nvidia_smi` | **ドライバが古い**。cu130 は 580 以上、cu126 は 560.76 以上。ドライバを更新して再実行 |
| §6 の status が **`FAIL`** | `results\logs\FAIL_*.txt` と `logs\*.log` | スクリプト側の問題。逐語をそのまま持ち帰ってください |
| §6 の status が **`result-FAIL`** | §3 の表の status 欄 | **これは結果です**（例＝`bf16` は CPU では動かない、という測定結果）。異常ではありません |
| §4 の compile が FAILED | `results\logs\compile.log` | Windows では `torch.compile` が通らない見込み。**通らなかったこと自体が知りたい情報**なので、そのまま持ち帰ってください |
| 途中で止まった | もう一度同じコマンド | 済んだ段は飛ばして続きから走ります |
| ダウンロードが遅い／切れる | もう一度同じコマンド | uv は途中まで落としたものをキャッシュ（`uv-cache\`）から再利用します |

---

## 7. このキットが触るもの・触らないもの

- **書く**＝このフォルダの中だけ（`.venv-*`・`uv-cache`・`python`・`hf`・`src`・`results`）。
- **書かない**＝レジストリ・Program Files・`%APPDATA%`・他のフォルダ。アンインストールは**フォルダごと消すだけ**。
- 上流のソース（`Irodori-TTS-8224daf.zip`）は `src\` に展開して**読むだけ**。改変していません。
  展開後の中身は Aratako/Irodori-TTS の commit `8224dafb46d0aba89209a8f905f1cb7e3299d9c1`（MIT）そのものです。
- `wheels\` の 2 つ（`dacvae` / `silentcipher`）は、3090 機に git を要求しないよう**こちらで作り置きした純 Python の wheel** です
  （dacvae = commit `414c2078…` / silentcipher = commit `d46d7d08…`。上流の pin と一致）。

---

## 8. ONNX のベンチ（**同梱済み・自動で走る**）

このキットの `onnx/` には、司令官機で ONNX に変換した 8 グラフ（DiT 1 ステップ fp32/fp16・duration・条件符号化・DACVAE 復号 fp32/fp16・透かし・参照 encoder＝合計 約 4.4 GB）と、
それぞれの入力形状ファイル（`*.inputs.json`・実際の合成と同じ形＝短文 3.76 秒・CFG バッチ 3）を入れてあります。
`run-cuda-kit.ps1` は **CUDA EP 用**と **DirectML EP 用**の別 venv を自動で作り、両方で各グラフを 40 回まわして
**中央値 ms** と **CPU EP との最大差**を `results/onnx_cuda.json` / `results/onnx_dml.json` に落とします（所要 10〜20 分・DL 約 1.3 GB＝CUDA EP のランタイム）。
これが「非 Python 化（ONNX）案が NVIDIA でどれだけ速いか」の唯一の実測になるので、**飛ばさず走らせてください**。

以下は仕組みの説明（読まなくてよい）。

`onnx` フォルダに `*.onnx`（と同名の `*.onnx.data`）があると、
`run-cuda-kit.ps1` が **CUDA EP 用**と **DirectML EP 用**の別 venv を自動で作り、両方で 40 回まわして
**中央値 ms** と **CPU EP との最大差**を `results\onnx_cuda.json` / `results\onnx_dml.json` に落とします。
入力の形は、`<モデル名>.inputs.json` を隣に置けばそれを使い、無ければ**動的軸を batch=4・その他 128** で埋めます。

`.onnx` を 1 つも置かなければこの段は自動で飛びます（`-SkipOnnx` でも明示的に飛ばせます）。
ONNX の一式は 1 モデルで数百 MB〜1.5 GB あるので、**測りたいものだけ**入れてください。

`<モデル名>.inputs.json` の書き方（例＝`sc_stft.onnx.inputs.json`。動的軸の既定値では小さすぎて落ちた例の直し方）:

```json
{ "x": { "shape": [1, 48000], "dtype": "float32", "fill": "randn" } }
```

（`fill` は `randn` / `zeros` / `ones`。整数入力は既定で 0、bool 入力は既定で true が入ります。）
