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

**配布用に組んだ runtime で読み直した値**（便 C・2026-09-05・`build/out/runtime-rocm-gfx1151/python.exe` が
名乗った物＝`build/assemble-runtime.ps1 -Variant rocm-gfx1151 -ExpectGpu` のログ）

| 項目 | 値（実測） |
|---|---|
| `torch.__version__` | `2.13.0+rocm10.0.0` |
| `torch.version.hip` | `7.15.26333` |
| `torch.version.cuda` | `null` |
| `torch.cuda.is_available()` ／ `device_count()` | `True` ／ `1` |
| `get_device_properties(0).name` | `AMD Radeon(TM) 8060S Graphics` |
| `get_device_properties(0).gcnArchName` | `gfx1151` |
| `get_device_properties(0).total_memory` | `107,090,132,992` B |

**上の値は稼働機の `.venv-rocm` ではなく、台帳から組んだ配布用の樹が出した値である**
（`.venv-rocm` は読むだけ・実行していない）。

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

> **上の表は `research/lab/notes/40` の in-process 実射（研究段）である。**
> **配布用に組んだ樹を HTTP 経由で撃った実測は §7**（便 C・2026-09-05）。段の数と所要はそこの値が新しい。

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

> **上は torch の allocator から見た値**（`research/lab/notes/40` §1・in-process）。
> **OS の側から同じプロセスを見た値は §7-7**（便 C・Windows の `GPU Process Memory`）＝読込直後 3.37 GiB で
> ほぼ一定だが、`empty_cache_interval=0` のまま 1 セッション撃ち続けると **10〜13 GB まで単調に増える**。
> **測っている物が違う**ので、片方の数字でもう片方を否定しないこと。

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
| 6 | ~~**8088 経由（HTTP・チャンク分割・`empty_cache`）での同じ実験**~~ **→ 便 C で撃った（§7-4）** | 配布用の樹を私設ポート 18091 で起こし、同一 body を 4 連射した＝**3 発目の再発は出ない**（1,487／1,276／1,280／2,204 ms）。ただし**別の落ち込みが出る**＝長い連続運転で `sample_rf` が 2.3 倍に伸びる（§7-6）。8088 の既存個体では**撃っていない**（停止域） |
| 7 | **12 s を超える出力・`ref_embed` 経路・尺固定の聴感** | 未実測 |
| 8 | **連続運転で遅くなる原因**（§7-6 の `sample_rf` 618 ms → 1,400 ms） | **落ち込みは実測**したが、**クロック・電力・温度は測っていない**（この機体で読む道具を持っていない）＝「電力／熱で落ちた」は**推測**である |

---

## 6. Radeon 版のビルド変種（便 C で確定・数値は実測）

- 取得台帳＝`ledger/runtime-rocm-gfx1151.json`＝**107 item**。うち **99 件は cpu 変種（`ledger/runtime-cpu.json`）と
  name・version・sha256 が完全一致**（台帳生成が毎回突合し、違えば止まる）。**AMD 索引由来は 8 件**＝
  `torch 2.13.0+rocm10.0.0`・`torchaudio 2.11.0.2+rocm10.0.0`・`amd-torch-device-gfx1151`・
  **`amd-torch-device-gfx115x`**（`torch[device-gfx1151]` が引く 2 本目・187,800,925 B）・
  `rocm 10.0.0`（**wheel でなく sdist**・24,780 B・実行時必須）・`rocm-sdk-core`・`rocm-sdk-libraries`・
  `rocm-sdk-device-gfx1151`。**AMD 由来 8 件の合計 1,367,591,795 B（1.274 GB）**、台帳全体 1,536,696,364 B。
- **AMD 索引 `https://stable.repo.amd.com/rocm/whl-next/` は sha256 を publish していない**（href に
  `#sha256=` 断片が無く、PEP 691 の JSON も返らない＝2026-09-05 実照会）。よって台帳の sha256 は
  **落として計算した値**である（`sha256_source` にその旨が入る）。
- ビルド＝`build/assemble-runtime.ps1 -Variant rocm-gfx1151 -ExpectGpu`。**実測＝26,523 檔・4,347.7 MiB
  （4.25 GiB）・dist-info 107 件**（設計書の見積 3.7 GB は `.venv-rocm` の torch と rocm-sdk だけの値で、
  埋め込み Python と他の site-packages が入っていなかった）。落とした `.pth` は
  `distutils-precedence.pth` の 1 件だけ。
- 差分は **site-packages の中身（torch と torchaudio）と、暖機を有効にする既定**だけ。
  wrapper（`server/ywk_server.py`）・契約（`docs/contract.md`）・話者の扱いは CUDA 版と同一である。
  wrapper が Radeon 変種で足すのは **bf16 固定（fp32 は上流 import 前に exit 2）** と
  **MIOpen の db 2 本を `YWK_DATA_DIR` に置くこと**の 2 つだけ（§7-5）。
- **リリースは別**（`decisions.md` 5）＝UI の「CUDA 版」欄と同居させない。

---

## 7. 便 C の実射（2026-09-05・この機体・**配布用に組んだ樹**・HTTP 経由）

> 台本＝`probe/rocm-warmup-probe.ps1`＋`probe/rocm-warmup-probe.py`。
> なま値＝`build/out/probe-log/rocm-warmup-20260905-022123.json`（**空 MIOpen db から**）と
> `build/out/probe-log/rocm-warmup-20260905-022638.json`（**同じ db を温めたまま**・`-KeepDataDir`）。
> サーバの逐語＝同ディレクトリの `server-<日時>-{cold,warm}.err.log`。
> 条件＝`build/out/runtime-rocm-gfx1151` の python で `build/out/app/server/ywk_server.py` を起こし、
> `IRODORI_MODEL_DEVICE=IRODORI_CODEC_DEVICE=cuda:0`・精度 bf16・`num_steps=40`・`IRODORI_PRELOAD=true`・
> `IRODORI_EMPTY_CACHE_INTERVAL=0`・`HF_HUB_OFFLINE=1`・私設ポート **18091**（8088・7861 は起こしていない）。
> 短文＝19 字（出力 2.9〜5.4 s）。**数値は台本が出した物だけ**である。

### 7-1 起動（受け入れ条件 C-4）

`spawn` → `GET /health` 200 まで。**上流は preload が終わってから listen するので、この値は
`runtime.loaded=true` までの値**である（`/ywk/status` で確認済み）。

| # | MIOpen db | 実測 |
|---|---|---:|
| 1 | **空**（機体で初めて） | **29,043 ms** |
| 2 | 温（1 の直後・新プロセス） | 26,186 ms |
| 3 | 温（別走・新プロセス） | 26,077 ms |
| 4 | 温（別走・新プロセス） | 26,180 ms |

⇒ **空 db でも 29.0 s**＝受け入れ条件の「≤ 60 s」を満たす。**空 db の罰は起動には出ない**
（読込は MIOpen を通らない）。罰が出るのは**最初の合成**（7-2・7-3）である。

### 7-2 暖機（`POST /ywk/warmup` の 6 射＝段 4／8／12 s ＋ 話者 3 名）

| 射 | 種 | 鍵 | 空 db | 温 db |
|---|---|---|---:|---:|
| 1 | no_ref | 4 s | 8,068 ms | 2,526 ms |
| 2 | no_ref | 8 s | 8,279 ms | 1,653 ms |
| 3 | no_ref | 12 s | 11,691 ms | 1,949 ms |
| 4 | voice | もち子さん | 7,996 ms | 2,237 ms |
| 5 | voice | ちび式じい | 8,016 ms | 2,081 ms |
| 6 | voice | つくよみちゃん | 7,647 ms | 2,063 ms |
| | | **合計** | **51,750 ms** | **12,558 ms** |

⇒ **起動＋暖機の合計＝空 db 80.8 s／温 db 38.7 s**。
研究段（`40` §7-3・in-process・段 3 射だけ）の「26.2 s」に対し、**配布の形（HTTP＋話者 3 射＋40 steps）では
温 db でも 38.7 s** かかる。**空 db の初回は 80.8 s**＝これが「機体で初回」の実測値である
（`docs/acceptance.md` §3 の 7 番はこの値で埋まる）。

### 7-3 暖機の後の 1 発目（受け入れ条件 C-6）

同じプロセスの中で、暖機の直後に本物の要求を撃った値。**暖機した話者でも、暖機と S_lat が違えば
1 発目は罰を払う**（暖機は `seconds` で 4／8／12 s に固定した形状を焼くが、本物の要求は自然尺＝3.72 s）。

| 射 | 空 db（1 走目） | 温 db（2 走目） |
|---|---:|---:|
| 暖機済み話者・1 発目 | 5,365 ms | **1,487 ms** |
| 同・2 発目 | 1,217 ms | 1,276 ms |
| 同・3 発目 | — | 1,280 ms |
| 同・4 発目 | — | 2,204 ms |
| 参照なし・1 発目 | 4,873 ms | 1,053 ms |
| 同・2 発目 | 910 ms | 1,023 ms |
| 同・3 発目 | — | 844 ms |
| 同・4 発目 | — | 789 ms |
| **暖機していない話者**・1 発目 | **9,863 ms** | 2,522 ms |
| 同・2 発目 | 1,389 ms | 2,476 ms |

⇒ ⑴ **「同一形状・db 温で 1 発目 ≤ 5 s」（`docs/acceptance.md` の改訂スパイク行）は満たす**
（1.49 s／4.02 s＝7-5）。⑵ **空 db では暖機済み話者でも 5.4 s**・**未暖機の話者は 9.9 s**
（研究の「VOICEROID2 参照の初回 14.8 s」＝`decisions.md` 40 は、この機体・この樹では **9.9 s** で再現した
＝同型だが同値ではない）。⑶ **3 発目の再発（19.4 s）は HTTP 経路でも出ない**（1,487／1,276／1,280 ms）。

### 7-4 暖機中に来た本物の要求（受け入れ条件 C-5）

暖機を起こし、**0.406 s 後**に本物の `POST /v1/audio/speech` を 1 本入れた。

| 走 | 走っていた暖機の射 | 本物の所要 | 本物が返った時点の `shots_done` |
|---|---:|---:|---:|
| 1 走目（db 温・段 5／7／9／11 s＝未見） | 6,182 ms | **6,932 ms**（200） | 1 |
| 2 走目（db 温・同じ段） | 1,162 ms | **2,971 ms**（200） | 1 |

⇒ **機構は効いている**＝本物が待たされるのは「今走っている 1 射」だけ（1 走目 ≈5.8 s・
2 走目 ≈0.8 s）で、`shots_done` が 1 のまま返っている＝**暖機は次の射を撃たずに譲った**。

**しかし「≤ 10 s」は満たしていない**（敵対検分の指摘・是正席が生データを読み直した）。
上の 2 標本はどちらも**その走の 1 射目**に重ねたもので、**1 射目は一番短い段**である。
同じ走の後ろの段はもっと長い＝

| なま値の出所 | 段 | 1 射の所要 |
|---|---|---:|
| `priority.shots_after[3]`（db 温） | `11s` | **10,888.8 ms** |
| `warmup_cold.shots[2]`（**空 db**・既定段） | `12s` | **11,690.8 ms** |

（`build/out/probe-log/rocm-warmup-20260905-022123.json` の逐語。空 db の既定段は
4／8／12 s で 8,067.8／8,279.1/**11,690.8** ms。）本物がこれらの射の頭で入れば待ちは
**10.9〜11.7 s** になる。**10.9 s／11.7 s の射に本物を重ねた走は存在しない**＝
`priority.warmup_at_fire.elapsed_s = 0.406` のとおり、撃ったのは 1 射目の最中だけである。

さらに **「≤ 10 s」を担保する仕掛けはコードにも契約にも無い**＝`stages` の範囲検査は
`_WARMUP_MAX_SECONDS = 60.0`（`server/ywk_server.py`・`docs/contract.md` の「0 < s ≤ 60」）
なので、`{"stages":[60,60,...]}` を投げれば 1 射はさらに長くできる。口は無鍵
（`decisions.md` 31・`127.0.0.1` 束縛）。

⇒ **C-5 のうち「本物が待つのは高々 1 射分」は満たす。「その 1 射分 ≤ 10 s」は満たさない**
（実測幅 **1.2〜11.7 s**・既定段でも 11.7 s）。段の秒に上限を設けるか条件文を実測幅に改めるかは
設計席／司令官の裁定＝`docs/acceptance.md` §3 に欠落として記帳した。

### 7-5 MIOpen の db は**プロセスを跨いで**効く（受け入れ条件 C-7）

**暖機を一切せずに**新しいプロセスを起こし、同じ本文・同じ話者を撃った値。

| 射 | 1 走目 | 2 走目 |
|---|---:|---:|
| 暖機済み話者・1 発目（**暖機なし**） | **4,045 ms** | **4,022 ms** |
| 同・2 発目 | 1,102 ms | 1,067 ms |
| 同・3 発目 | — | 1,091 ms |
| 同・4 発目 | — | 1,135 ms |
| 未暖機話者・1 発目 | 2,281 ms | 2,506 ms |
| 参照なし・1 発目 | 989 ms | 1,007 ms |

⇒ **同じ形状の 1 発目は 5.4 s（空 db）→ 4.0 s（db 温・暖機なし）→ 1.5 s（db 温・暖機あり）**。
db は効くが**プロセス内の残り（≈3 s）は db では消えない**＝暖機はやはり要る。
内訳（`server-*.err.log` の逐語・4.02 s の射）＝`prepare_reference` 1,332 ms＋
`predict_duration` 639 ms＋`sample_rf` 760 ms＋`decode_latent` 327 ms＋`silentcipher_watermark` 928 ms。

**db の実体**（`YWK_DATA_DIR` の下・wrapper が `MIOPEN_USER_DB_PATH`／`MIOPEN_CUSTOM_CACHE_DIR` で置く）

| 檔 | 空 db から 1 走した後 | 2 走した後 |
|---|---:|---:|
| `miopen\db\gfx1151_20.HIP.3_6_0_8d1ae90e.ufdb.txt` | 55,768 B | 129,897 B |
| `miopen\db\...ufdb.txt.time` | 14 B | 14 B |
| `miopen\cache\3.6.0.8d1ae90e\gfx1151_20.ukdb` | 499,712 B | 696,320 B |
| **合計** | **555,494 B** | **826,231 B** |

＝**MIOpen が実際に書くのは 3 檔・1 MB 未満**である（`~/.miopen` ではなくこの場所に落ちた＝
wrapper の env が効いていることの証明）。

### 7-6 二次ボイス 11 本（受け入れ条件 C-8）＝**未達**

11 プリセット（`voices/presets/*.wav`・参照 24.8〜35.8 s）を `voices.json` の別名で登録し、
同じ短文を 1 本ずつ。**3 巡とも 11 本すべて 200**。RTF は**所要 ÷ 出力秒数**。

| 巡 | 条件 | 所要 | RTF |
|---|---|---|---|
| ① | db もプロセスも初見 | 1,160〜9,527 ms | **0.249〜2.469** |
| ② | db 温・プロセス初見 | 1,267〜3,440 ms | **0.341〜0.716** |
| ③ | db 温・プロセス 2 巡目 | 1,453〜3,517 ms | **0.271〜1.016** |

⇒ **C-8 の「RTF < 0.5」は満たしていない**（③で 0.5 未満は 11 本中 4 本）。原因は 2 つで、どちらも実測：

1. **参照ごとのプロセス内初回**＝`prepare_reference` が **0.95〜1.44 s**（2 度目は 0.28〜0.40 s）。
   話者を切り替えるたびに 1 秒級を払う。`ref_latent`（`.pt` 事前計算）ならここは消える見込みだが
   **この便では作っていない＝未検証**（`40` §4 の in-process 実測は 10〜11 ms）。
2. **連続運転で計算そのものが遅くなる**＝同じ 1 プロセスの 35 射を順に見ると、**純計算の段
   `sample_rf` が 618 ms（2 射目）→ 1,400 ms（34 射目）と 2.3 倍に伸びる**
   （`decode_latent` は 50〜90 ms のまま＝MIOpen ではない）。約 2 分の連続負荷での落ち込みで、
   **1 分ほど空けた次の走では 618 ms 級に戻る**。**クロック・電力・温度は測っていない**ので
   「電力／熱」は**推測**である（§5 の 8）。

**短文 1 本を単発で撃つ限り RTF は 0.25〜0.35**（7-3・7-5）＝**連射しなければ条件は満たす**。
読み分けちゃんの使い方（1 行ずつ合成）に近いのは前者だが、**「11 名を続けて試聴する」操作では
後半が 2 倍遅くなる**ことを UI の側で見込む必要がある。

### 7-7 GPU メモリ（`docs/acceptance.md` の VRAM 行への申し送り）

**測り方**＝Windows の性能カウンタ `GPU Process Memory`（サーバの pid で絞り・0.4 s 目標間隔・
実際は 1.4〜1.8 s 間隔）。**torch の allocator の値ではない**（外から見える OS 側の値）。

| 位置 | 実測（`Local Usage`） |
|---|---:|
| モデル読込直後（4 個体） | 3,615,744,000〜3,617,955,840 B（**3.37 GiB・ほぼ一定**） |
| 暖機 6 射の終わり（空 db） | 10,646,634,496 B |
| セッション終端（1 走目・warm 個体・11 プリセット後） | 13,708,050,432 B |
| セッション終端（2 走目・warm 個体） | 10,277,539,840 B |

- **単調に増える**（採った標本では `peak` と `last` が常に一致）＝`IRODORI_EMPTY_CACHE_INTERVAL=0` を
  焼いている以上、caching allocator は解放しない。**この機体は共有メモリ 107 GB なので破綻しないが、
  VRAM の少ない dGPU で同じ設定を使うと危ない**＝`empty_cache_interval` の既定は Radeon 版で
  別途考える価値がある（**当席は裁定しない**）。
- `Non Local Usage` は全標本 0・`Shared Usage` は 124〜142 MB。
- **`docs/acceptance.md` の「bf16 常駐 ≤ 3.2 GiB」は "読込直後" なら 3.37 GiB でほぼ境界、
  "セッション累積" では桁が違う**。研究段の 3.03 GB（`40` §1）は torch 側の射ごとのピークで、
  上の値とは**測っている物が違う**。両方を並べて読むこと。
