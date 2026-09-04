# radeon.md — Radeon（ROCm）版の注記

> **ROCm は未保障。Radeon 版は別リリースである**（`decisions.md` 5・21）。
> 本檔に書くのは **gfx1151（Ryzen AI MAX+ 395／Radeon 8060S）で実際に測った事実だけ**である。
> 他の Radeon で動くか・速いかは**測っていない**＝謳わない。
> 出典＝`research/lab/notes/34-rocm-environment-detection.md`（環境判定）・
> `research/lab/notes/40-rocm-warmup.md`（暖機の実測・in-process 実射）・`decisions.md` 24。

---

## 1. 保証する範囲（この 1 文だけ）

> **gfx1151（Ryzen AI MAX+ 395／Radeon 8060S）で確認済み。**

それ以外は「動くかもしれない」であって、保証ではない。UI にもこの文言だけを出す。

**確認した機体の構成（実測・`decisions.md` 24・`research/lab/notes/34`）**

| 項目 | 値 |
|---|---|
| GPU | AMD Radeon(TM) 8060S Graphics（gfx1151） |
| ドライバ | 32.0.31041.1004（2026-08-17） |
| Python | 3.12.14 |
| torch | `2.13.0+rocm10.0.0`（`torch[device-gfx1151]`） |
| torchaudio | `2.11.0.2+rocm10.0.0` |
| wheel 索引 | `https://stable.repo.amd.com/rocm/whl-next/` |
| HIP | 7.15 |

**`whl-next` は AMD 公式手順に載る経路だが、名のとおり先行チャネルで版が動く**＝取得台帳
（`ledger/runtime-rocm-gfx1151.json`）で版と sha256 を固定し、動いたら勝手に追随しない。

---

## 2. CUDA 版との違い（3 つ）

### 2-1 精度は **bf16 固定**（選ばせない）

- **fp32 の復号は病的に遅い**＝同一形状 6 連射で fp32 は 1 発目 **32.077 s**（うち
  `decode_latent` 28.5 s）・定常 5.61 s（RTF 0.94）。同じ位置が **bf16 なら 2.841 s／定常 0.85〜1.09 s**
  （`research/lab/notes/40` §8）。
- ⇒ **bf16 固定は「起動直後の詰まり」を 11 倍縮める**。これが `decisions.md` 5 の
  「Radeon 版は bf16 固定」の根拠である。
- 上級者向けの精度セレクタも Radeon 版では**開けない**（CPU に落とす経路だけ残す）。

### 2-2 **初見形状の罰**がある（CUDA では観測されていない）

- **罰の鍵は出力潜在フレーム数 S_lat ただ 1 つ**（40 ms 刻み）。**同じ S_lat なら本文が 14 字でも
  78 字でも罰は出ず、S_lat が 1 フレーム違うだけで罰が戻る**（`seconds` 4.00／4.04／4.08 で実証）。
  **字数から S_lat は当てられない**（29 字→150・38 字→146）。
- **罰が出る段は畳み込み 3 段だけ**＝`decode_latent`・`silentcipher_watermark`・`prepare_reference`。
  DiT（`sample_rf`・ウォーム時間の 60〜85 %）には**一切出ない**。
- **罰は二層**＝
  ⑴ **MIOpen のカーネル生成**＝ディスクの user db（`~/.miopen`・`gfx1151_20.ukdb`）に**永続**し、
  その形状を**機体で初めて見たときだけ**。`decode_latent` で **+3.0〜10.2 s**。
  ⑵ **プロセス内のアルゴリズム選択／ワークスペース確保**＝**永続しない**。同じ形状でも新プロセスなら
  毎回 **+0.18〜0.61 s**。
- **db はプロセスを跨いで効く**＝S_lat 363 は初見 14.97 s → 新プロセス 2.87 s → プロセス内ウォーム
  2.17 s。空 db では 1 発目 11.2 s・モデル読込も 20.0 → 29.3 s。
- **CUDA 機（3090）では同型の罰は観測されていない**（cold 1.1〜2.1 倍のみ・参照長の罰も無し）。
  ⇒ `docs/acceptance.md` の「スパイク」行は **Radeon 版にだけ**掛ける。

### 2-3 起動時に**暖機する**

**実測のレシピ**（`research/lab/notes/40` §7-3・in-process・bf16）

| レシピ | モデル読込 | 暖機 | 合計 | VRAM ピーク | 焼ける形状 |
|---|---:|---:|---:|---:|---|
| 暖機なし | 20.0 s | 0 | **20.0 s** | 1.77 GB | 0（第一声が罰を丸被り） |
| **3 段（4／8／12 s）** | 20.0 s | 6.25 s | **26.2 s** | 3.03 GB | 3 |
| 12 段（1〜12 s・1 s 刻み） | 20.0 s | 19.6 s | **39.7 s** | 3.03 GB | 12 |
| 12 段・**機体で初回** | 29.3 s（空 db） | 32.0 s | **50〜60 s 級**（要・初回実測） | 3.03 GB | 12 |

- **話者ごとの暖機射は要らない**＝参照長の罰は `ref_latent`（`.pt` 事前計算）で**完全に消える**
  （初回 10〜11 ms・以後 1.2〜1.5 ms・参照長に依らず一定・VRAM も増えない）。
  wav 経路は初見で +1.08 s（参照 5 s）〜+2.12 s（20 s）・VRAM +0.7 GB。
  事前計算は 0.37〜0.90 s／件・`.pt` は 9.5〜49.5 KB。
- **暖機で消せない罰は 3 つ**＝
  ⑴ 暖機した段の外の S_lat（db にあれば +0.18〜0.61 s・**機体で初見なら 1 発 8.5 s**
  ＝decode 5.1＋透かし 2.4）／
  ⑵ `predict_duration` のプロセス初回（592〜623 ms。**自然尺の 1 射を暖機に混ぜれば 156 ms** に落ちる）／
  ⑶ 参照ありの CFG 枝 3→4（`sample_rf` +20〜50 %・構造的な増分）。
- ⇒ **暖機には「段の 3 射」＋「自然尺の 1 射」を入れる**（`decisions.md` 5 の「起動時暖機」の中身）。
  **`/ywk/status.warmup`**（`state`・`shots`）で進み具合を出す（`docs/contract.md` ⑹）。

### 2-4 `seconds` の段階化は**採らない**

- **`seconds` の 3 段化（4／8／12 s）は罰に完全に効く**（段内は本文が違っても 0.72〜1.89 s で一定）。
- **しかし副作用が重い**＝**出力長が指定秒ちょうどになる**（14 字を 12 s に間延び・78 字を 4 s に
  詰める・**警告は出ない**）。**形状の有限化と自然な尺は両立しない**。
- ⇒ 配布版は**自然尺のまま**で、暖機と MIOpen db の育ちで償う。新しい S_lat ごとの
  **+0.2〜0.6 s は残る**（正直に書く）。
- 別案（復号直前に潜在を段の長さへ詰め物して音声側で切る）は**上流パッチ 1 箇所・未実射**＝採らない。

---

## 3. VRAM

- 読込直後 **1,768 MB**（全走同一）。
- **1 射のピークは S_lat に比例する（≒3.51 MB／フレーム）**＝S_lat 25→2,062 MB／100→2,331／
  200→2,683／300→3,031／**363→3,248 MB（＝14.5 s 出力で 3.17 GiB）**。
- ＝「bf16 なら形状に依らず 3,079 MB 一定」は**プロセス全体の累積ピーク**の見方であり、
  射ごとにリセットして採ると上のとおり増える（`docs/acceptance.md` の VRAM 行の改訂）。
- 暖機（3 段でも 12 段でも）のピークは **3.03 GB**。

---

## 4. 環境の判定

- **判定の道具は AMD 自身が配っている**＝`rocm-bootstrap` 0.2.0（PyPI・純 Python・172 KB・依存なし）。
  Windows では **`clinfo`**（AMD ドライバ同梱＝`C:\WINDOWS\system32\clinfo.exe`）を子プロセスで呼び、
  gfx 名を返す。実測＝`rocm-bootstrap-detect.exe` → `gfx1151`・**1.5 s**。
  上書き用の env＝`ROCM_BOOTSTRAP_FORCE_GFX_ARCH`／`ROCM_BOOTSTRAP_DISABLE_DETECTION=1`。
- **手順は 4 段で、重いのは最後だけ**＝① DXGI／WMI で vendor=AMD → ② `rocm-bootstrap` で gfx 名
  （1.5 s）→ ③ 索引に `amd-torch-device-<gfx>` の win wheel が有るか → ④ 実際に入れて bf16 で 1 発焼く
  （DL 1.1〜1.3 GB・展開 ≈3.5 GB）。①〜③は数秒。
- **索引には広い arch が並んでいる**（gfx1010〜1036／1100〜1103／1150〜1153／1200・1201 など）が、
  **索引に wheel があることと動くことは別**＝**gfx1151 以外は測っていない**。
- **PyTorch は ROCm でも device 名として `"cuda"` を名乗る**＝`IRODORI_MODEL_DEVICE` の値は
  Radeon 版でも `cuda`／`cuda:N`。`/ywk/status.torch.hip` に HIP の版が入るかどうかで見分ける
  （`torch.version.hip`）。

---

## 5. 未確認（謳わない）

| # | 何 | なぜ |
|---|---|---|
| 1 | **gfx1151 以外の Radeon での可否・速度** | 実測が gfx1151 だけ。索引に wheel があることは根拠にならない |
| 2 | **Windows の ROCm ドライバ要件の原文** | 未取得（司令官機は 32.0.31041.1004 で動いた、という事実だけ） |
| 3 | **MIOpen の db（`gfx1151_20.ukdb`）を他機に同梱してよいか・効くか** | 未検証。効けば「機体で初回」の数十秒が消える |
| 4 | **AMD 2 枚機（iGPU＋dGPU）で `clinfo` がどちらを 0 番にするか** | 未確認 |
| 5 | **AMD の ROCm wheel の license の一次資料** | `rocm_sdk_core-10.0.0.dist-info/METADATA` は **59 B・3 行**で License 欄も分類子も無い。wheel 内の license 檔は amd_comgr（Apache-2.0 WITH LLVM Exceptions）と hipcc の 2 件だけ。**参考**＝GitHub `ROCm/ROCm` は MIT。**同梱・再配布の可否は断定しない**（`research/lab/notes/22` 表 7・未確認 6） |
| 6 | **8088 経由（HTTP・チャンク分割・`empty_cache`）での同じ実験** | `40` は in-process 実射。3 発目の再発（19.4 s）が HTTP 経路で起きるかは未確認 |
| 7 | **12 s を超える出力・`ref_embed` 経路・尺固定の聴感** | 未実測 |

---

## 6. Radeon 版のビルド変種

- 取得台帳＝`ledger/runtime-rocm-gfx1151.json`（**便 A では雛形・便 C で確定**）。
- ビルド＝`build/assemble-runtime.ps1 -Variant rocm-gfx1151`。
- 差分は **site-packages の中身（torch と torchaudio）と、暖機を有効にする既定**だけ。
  wrapper（`server/ywk_server.py`）・契約（`docs/contract.md`）・話者の扱いは CUDA 版と同一である。
- **リリースは別**（`decisions.md` 5）＝UI の「CUDA 版」欄と同居させない。
