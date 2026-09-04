# 26 — ⒝（ONNX）の「端から端まで」を 3090 機で採るための小キット

> 席＝第 5 便サブ席（opus）／担当＝25 §要点 8 の「⒝の NVIDIA 端から端までは**見立て**」という空欄を、
> 司令官が RTX 3090 機で 1 回まわすだけで**実測**に変えるための道具立て。実施 2026-09-04。
> 書いたのは `lab/kit-cuda/onnx-e2e/**`（新設・22 檔・1.66 MiB）・`lab/out/26_*`・`lab/tmp/kit-e2e-test/`・本ノートのみ。
> `upstream/**`・`C:/IrodoriTTS/**`・`C:/irodori-TTS-server/**`・HF キャッシュ・`lab/onnx/**` は **0 檔も変わっていない**（§7）。
> 相対パスの基点は `C:\Users\mugonkun\source\repos\irodori-native-research\`。GPU は司令官の許可のもとで使用（配信なし）。

## 要点（10 行以内）

1. **成果物＝`lab/kit-cuda/onnx-e2e/`（22 檔・1,742,284 B＝1.66 MiB）**。既存 CUDA キットの**追加 1 段**として、フォルダに落とすだけで足りる。ONNX 本体は同梱せず、キットの `onnx/`（3090 機に既にある 4.4 GB）を読む。
2. **`e2e_onnx.py`（785 行・torch 不使用）＝ 23 便の `40_loop_dml.py`＋`23_e2e.py` を 1 本にし、CUDA/DML/CPU の 3 EP を同じ物差しで測る**。段は 条件符号化→duration→DiT ループ（**形状ごとに session 2 本**）→DACVAE 復号→透かし、合計と RTF を cold/warm で JSON に落とす。
3. **数値の検算が自動で付く**＝最終潜在を同梱の参照（AMD DML fp32・torch と 5e-5 以内）と max abs diff で照合。本機の再走で **max abs 0.0（ビット一致）** を確認＝キットの入力・ループ・参照は 23 便と同一であることが証明できた。
4. **本機での整合**＝DML 端から端まで **2.824 s／RTF 0.751**（23 §6 の 2.815／0.749 と **0.3 % 差**・3 走の幅は ±5 %）、10 steps **1.022 s／0.272**（同 1.036／0.276）。CPU EP **12.261 s／3.261**（同 12.305／3.273）。**キットは 23 便の数字を再現する**。
5. **`run-onnx-e2e.cmd` の通し検証も済み**（`lab/tmp/kit-e2e-test/` にキットの写しを作り 2 回実行・script failures 0）。本機に NVIDIA が無いので CUDA EP は `CUDA failure 801 cudaSetDevice` で EP Error → CPU に落ちて完走した＝**落ち方まで含めて確認済み**。
6. **21 §7 の未確認「`onnxruntime-gpu[cuda,cudnn]` の extras 名が通るか」は通る**＝そのまま解決し 12 パッケージ・**DL 1,228 MiB**・venv 1.8 GB。中身は `onnxruntime-gpu 1.29.0`＋`nvidia-cudnn-cu13 9.25.1.1`／`cublas 13.6`＝**CUDA 13 系**（ドライバ 580+ が要る＝3090 機の 591.86 で満たす）。3 段退避は使われなかったが残してある。
7. **キットが作る CUDA venv は ORT 1.29.0＝透かしグラフが載る**（本機で CPU EP **248.6 ms**・warm）。DML venv（1.24.4）は 23 §2-1 と同じ逐語でロード不可＝**部品合計 37.4 ms に自動で切り替わる**。⇒ **端から端までの合計に透かしを含められるのは CUDA 側だけ**＝2 つの JSON を直接引き算してはいけない。
8. **副産物＝23 §10 (g) の未確認が解けた**。同じ機体・同じグラフ・同じ入力で `dit_step_fp16` の CPU EP 1 ステップ（B=3）が **1.24.4 で 392.9 ms／1.29.0 で 4,439.3 ms＝11.3 倍**。fp32 は両版 361〜362 ms で同じ＝**fp16 だけが 1.29 で桁で遅い ORT の版差**。
9. **fp16 の破綻も再確認**＝DML で最終潜在が参照と **5.381** 違う（23 §4-3 の 5.38 と同値）。CPU EP では 1.24.4 で 0.0078・1.29.0 で 0.0389＝**GPU EP でだけ壊れる**という 25 §要点 6 の見立てが、同じ道具で両方の側から裏づけられた。
10. **渡し方＝フォルダをコピー →`onnx-e2e\run-onnx-e2e.cmd` をダブルクリック → `results\onnx_e2e_{cuda,dml}.json` を返送**。所要 15〜40 分・DL 約 1.5 GB・空き 6 GB・管理者権限不要。**README-e2e.md の最上段に「N:\ 上で直接走らせない」を置いた**（25 §要点 3 の反省）。

---

## 1. 成果物の構成（`lab/kit-cuda/onnx-e2e/`）

| 檔 | バイト | 役目 |
|---|---:|---|
| `e2e_onnx.py` | 35,285 | 本体（785 行・**純 numpy + onnxruntime**・torch を 1 行も import しない） |
| `run-onnx-e2e.cmd` | 4,232 | ダブルクリック用。**ASCII・CRLF**（21 §2-2 の釘。`file(1)` で `DOS batch file, ASCII text, with CRLF` を確認） |
| `README-e2e.md` | 8,804 | 司令官向け手順書（日本語・1 行 1 操作・所要・読み方・失敗時） |
| `data/cond_real.npz` | 1,591,637 | 実条件（本文・caption のトークン列と符号化済み状態・duration 素性）＝13 §1 が作ったもの |
| `data/x0.npz` | 72,946 | 初期ノイズ（seed 1234・(1,94,32)） |
| `data/ref_latent_dml_fp32_s40.npz` | 12,298 | **参照潜在 40 steps**＝`lab/out/23_latent_dml_fp32_s40_2sess.npz`（AMD DML fp32・torch と 5.29e-05） |
| `data/ref_latent_dml_fp32_s10.npz` | 12,298 | 同 10 steps（torch と 4.12e-05） |
| `data/inputs/*.inputs.json` | 15 檔・4,784 | 入力形状。13 檔は `lab/onnx/` の写し、**`sc_enc_c_fp32` と `sc_dec_c_fp32` の 2 檔は本便で新規作成**（動的軸 T=83 を埋めないと透かし部品が測れないため）。手術版（`*_az0`／`*_dml`）は接尾辞を落として元グラフの形を引く |

- **ONNX 本体は同梱しない**（キットの `onnx/` を読む）。3090 機には `dit_step`・`dit_step_fp16`・`duration`・`encode_conditions_sdpa(_az0)`・`dacvae_decoder_fp32(_dml)`・`silentcipher_watermark_fp32` が既にある（25 §3）。
- スクリプトが作るのは `.venv-e2e-cuda\`・`.venv-e2e-dml\`・`uv-cache\`・`python\`・`results\` の**すべてキット内**（`UV_CACHE_DIR`／`UV_PYTHON_INSTALL_DIR`／`UV_NO_CONFIG=1`＝21 §2-1 と同じ釘）。

### 引数

```
e2e_onnx.py --ep cuda|dml|cpu --onnx-dir DIR --data-dir DIR --json-out PATH
            [--steps 40,10] [--reps 2] [--sessions 1|2] [--device-id 0]
            [--threads 8] [--no-cpu-ref] [--skip-fp16] [--label ...]
```

`--device-id` は複数 GPU 機での 2 枚目指定（BRIEF §4-4）。`--sessions 1` にすると 23 §4-2 の「形状切替の罰」を再現できる。

## 2. `e2e_onnx.py` が測る段（依頼の (a)〜(i) との対応）

| 依頼 | 段 | 中身 | 出典 |
|---|---|---|---|
| (a) | 前口上 | `ep=cuda` なら `ort.preload_dlls()`（無ければその旨を記録）＋`nvidia-smi`。`ep=dml` は `enable_mem_pattern=False`＋`ORT_SEQUENTIAL` | 25 §0・23 §7 棘1 |
| (b) | `encode_conditions` | **実トークン列**（`cond_real.npz` の `text_ids`/`cap_ids`/`ref_latent`）で 1 回。DML は `_az0`、CUDA/CPU は無手術版を自動で選ぶ | 23 §3-1 |
| (c) | `duration` | 実条件で 1 回。出力 `log1p_frames` を `cond_real` の値と突き合わせる | — |
| (d) | `dit_loop` | **40 steps と 10 steps**。t 線形・CFG independent（text 3.0／caption 3.0／speaker 0.0）・`cfg_min_t 0.5`・Euler。**B=3 用と B=1 用に session を分ける** | `lab/bin/40_loop_dml.py` の写し・23 §4-2 |
| (e) | `decode` | 最終潜在を転置して `dacvae_decoder_fp32`（DML は `_dml` 手術版）で 1 回 | 23 §3-2 |
| (f) | `watermark` | `silentcipher_watermark_fp32` を 1 回。**要求 EP でロードできなければ CPU EP に退避**して測り `ep_used` に明記。本体が載らないときは部品（`sc_stft`+`sc_enc_c`+`sc_dec_c`）の合計を代用 | 23 §5 |
| (g) | `totals` | `steps40_warm / steps40_cold / steps10_warm / steps10_cold` の 4 通り。段の内訳（`breakdown_ms`）と RTF（音声 3.76 s）と `missing_stages` | 23 §6 |
| (h) | `vs_reference_latent` | 最終潜在と同梱参照の `max_abs`・`rel_l2` | 23 §4-3 |
| (i) | `dit_fp16` | `dit_step_fp16` で同じループを 1 回。`latent_absmax`・参照との差・同一走の fp32 との差 | 23 §4-4・25 §要点 6 |

- **cold / warm の採り方**＝1 発目がコールド、以降の中央値がウォーム。単発の段は `--reps`+1 回、DiT ループは `--reps` 回まわす。
- **各段に CPU EP の照合が付く**（`--no-cpu-ref` で外せる）＝`cpu_ref.max_abs_diff_vs_cpu_ep`。CUDA EP は TF32 既定で 1e-2 級になるはず（25 §要点 5）。
- **失敗しても止まらない**＝逐語を `failures[]` と当該段の `error` に残して次へ進む。
- **実際にどの EP で走ったか**を `ep_effective.requested_ep_used_everywhere` と各段の `providers_used`／`ep_fallback` に残す（EP Error の CPU 落ちは ORT が黙って行うため）。

## 3. 本機（AMD 8060S）での検証＝23 §6 と整合するか

EP と venv の組み合わせを変えて 5 走した。生データ＝`lab/out/26_e2e_dml.json`・`26_e2e_cpu_ort1244.json`・`26_kit_e2e_cuda.json`・`26_kit_e2e_dml.json`。

| 走 | ORT | 40 steps warm | RTF | 10 steps warm | RTF | 23 §6 の値 |
|---|---|---:|---:|---:|---:|---|
| `lab/.venv-dml` 直走・`--ep dml` | 1.24.4 | **2.693 s** | 0.716 | 0.979 s | 0.260 | 2.815／0.749・1.036／0.276 |
| **キット経由 1 回目**（`.venv-e2e-dml`） | 1.24.4 | **2.824 s** | **0.751** | **1.022 s** | **0.272** | **同上＝0.3 % 差** |
| **キット経由 2 回目**（同・venv 再利用） | 1.24.4 | 2.696 s | 0.717 | 0.969 s | 0.258 | 同上＝4 % 速い側 |
| `lab/.venv-dml`・`--ep cpu` | 1.24.4 | 11.945 s | 3.177 | 4.488 s | 1.194 | 12.305／3.273 |
| キット経由（`.venv-e2e-cuda` が CPU に落ちた走）1／2 回目 | 1.29.0 | **12.261／12.377 s** | **3.261／3.292** | 4.635／4.590 s | 1.233／1.221 | 同上 |

段の内訳（キット経由 1 回目・DML・warm・ms）＝`encode 200.8 / duration 2.8 / DiT 2,402 / decode 180.9 / 透かし部品 37.4`。
23 §6 の内訳（`209.5 / 2.15 / 2,388 / 178.1 / 37.3`）と全段で数 % 以内。
**同じ檔を 3 回走らせた幅は ±5 %**（2.693／2.696／2.824 s）＝機体ノイズ。23 §6 との 0.3〜4 % 差はこの幅の中に収まる。

**数値の照合（キットが自動で採るもの）**

| 検算 | DML（1.24.4） | CPU EP（1.24.4） | CPU EP（1.29.0） |
|---|---|---|---|
| 最終潜在 vs 参照（40 steps） | **0.0（ビット一致）** | 1.45e-05 | 1.45e-05 |
| 最終潜在 vs 参照（10 steps） | **0.0** | 1.03e-04 | 1.03e-04 |
| `encode_conditions` vs CPU EP | 2.74e-06 | — | — |
| `duration` vs CPU EP | 4.77e-07 | — | — |
| `decode` vs CPU EP | 1.64e-06 | — | — |
| **fp16 の最終潜在 vs 参照** | **5.381**（＝23 §4-3 の 5.38） | 0.0078 | 0.0389 |

- **DML の最終潜在が参照とビット一致**＝23 便の `40_loop_dml.py` と本便の `e2e_onnx.py` は**同じ計算をしている**（写経が正しい）ことの証明。
- **fp16 は GPU EP（本機では DML）で壊れ**（5.381）、**CPU EP では両版とも丸め誤差の範囲**（0.008／0.039）＝25 §要点 6 の「fp16 の破綻は GPU EP 全般の問題（グラフ側の op が原因）」を、同じ道具の両側から支持。CUDA EP で本当に壊れるかは 3090 機の JSON で確定する。

### 3-1 副産物＝23 §10 (g) の未確認（ORT の版差）が解けた

同一機体・同一グラフ・同一入力・同一プロセス設計で:

| グラフ / 1 ステップ（B=3・CPU EP・中央値） | ORT 1.24.4 | ORT 1.29.0 | 比 |
|---|---:|---:|---:|
| `dit_step`（fp32） | 362.4 ms | 361.2 ms | 1.00 |
| **`dit_step_fp16`** | **392.9 ms** | **4,439.3 ms** | **11.3 倍** |
| 40 steps ループ全体（fp16） | 10.767 s | 127.563 s | 11.8 倍 |

**断定＝`dit_step_fp16` が ORT 1.29.0 の CPU EP で桁違いに遅いのは実在する版差**（13 §8-2 の 3,296 ms は 1.29 側の値、23 §2 の 402.6 ms は 1.24.4 側の値で、両方とも正しかった）。fp32 には差が無い。
⇒ ⒝を CPU で動かす退路を考えるとき、**fp16 グラフを ORT 1.29 の CPU EP に置くのは論外**（fp32 の 12 倍遅い）。

## 4. `run-onnx-e2e.cmd` の通し検証（`lab/tmp/kit-e2e-test/`）

キットの写しを作って（`uv/` と `onnx-e2e/` をコピー、`onnx` は `lab/onnx` への **junction**＝4.4 GB を複製しないため）、`.cmd` をそのまま 2 回実行した。

| 走 | 内容 | 所要（実測） | script failures |
|---|---|---:|---:|
| 1 回目 | venv 2 本を新規作成（CPython DL 込み）→ CUDA 走 → DML 走 | **4.6 分**（16:31→16:35:36） | **0** |
| 2 回目 | venv 再利用（`ort-ready.txt` を見て導入を飛ばす） | **3.4 分**（うち CUDA 走 175.8 s・DML 走 23.6 s） | **0** |

（本機の回線は 1.2 GiB を 1 分弱で引く。3090 機がもっと遅ければ導入だけで 10 分かかりうる＝README には 3〜10 分と書いた。
また本機の CUDA 走が 175.8 s と長いのは **CPU に落ちたから**で、3090 で CUDA EP が効けば **2 分前後**に縮む見込み。）

- **`onnxruntime-gpu[cuda,cudnn]` はそのまま解決した**（21 §7 の未確認への答え）。`uv` の実ログ＝`Resolved 12 packages in 192ms` → 9 wheel を DL（**合計 1,227.4 MiB**＝`nvidia-cudnn-cu13 388.9 / nvidia-cublas 376.3 / nvidia-cufft 175.4 / onnxruntime-gpu 141.3 / nvidia-curand 52.9 / nvidia-cuda-nvrtc 43.2 / nvidia-nvjitlink 36.0 / numpy 11.9 / nvidia-cuda-runtime 2.5`）→ `Prepared 12 packages in 7.04s` → `Installed 12 packages in 674ms`。**3 段退避（nvidia-*-cu13 明示 → 素の onnxruntime-gpu）は使われなかったが、そのまま残してある。**
- 入った版＝`onnxruntime-gpu 1.29.0`・`nvidia-cudnn-cu13 9.25.1.1`・`nvidia-cublas 13.6.0.2`・`nvidia-cufft 12.3.0.29`・`nvidia-curand 10.4.3.29`・`nvidia-cuda-nvrtc 13.3.33`・`nvidia-nvjitlink 13.3.33`・`nvidia-cuda-runtime 13.3.29`・`numpy 2.5.2`。**CUDA 13 系**＝ドライバ 580 以上が要る（3090 機は 591.86＝満たす／25 §要点 1）。
- venv の実寸＝`.venv-e2e-cuda` **1.8 GB**、CPython 3.12.14 は uv が 21.0 MiB を DL してキット内 `python\` に置いた。
- 本機に NVIDIA が無いので CUDA EP は毎 session で `EP Error ... CUDA failure 801: cudaSetDevice ... GPU=-1` を出し、ORT が `Falling back to ['CPUExecutionProvider']` して**完走**した。キットはこれを `ep_fallback` と `ep_effective.requested_ep_used_everywhere=false` に残す＝**3090 機で万一同じことが起きても JSON を見れば分かる**。
- **透かしは CUDA venv（1.29.0）でロードでき、CPU EP で 248.6 ms（warm）で走った**。DML venv（1.24.4）では 23 §2-1 と同じ逐語 `Node (node_DFT_342) Op (DFT) [ShapeInferenceError] is_onesided and inverse attributes cannot be enabled at the same time` でロード不可＝**部品合計 37.4 ms に自動で切り替わった**。

## 5. 司令官への渡し方（README-e2e.md の要約）

1. **キットのフォルダごと PC のローカル SSD にコピー**（`N:\` 上で直接走らせない＝25 §要点 3 の反省。README の最上段に置いた）。
2. `onnx-e2e\run-onnx-e2e.cmd` をダブルクリック。
3. 初回のみ約 1.5 GB のダウンロード（CUDA EP 1.3 GB＋DirectML 0.2 GB）。所要 15〜40 分・空き 6 GB・管理者権限不要。
4. 終わったら `results\onnx_e2e_cuda.json` と `results\onnx_e2e_dml.json` の**2 檔だけ返送**。
5. 途中の `FAIL` は飛ばして進む設計＝**止めずに最後まで**走らせてもらう。
6. `run-cuda-kit.ps1` とは独立（あちらを走らせ直す必要は無い。venv も別名 `.venv-e2e-*` なので衝突しない）。

## 6. 結果 JSON の読み方（主席用）

| 欄 | 何が分かるか | 判断の目安 |
|---|---|---|
| `totals.steps40_warm.rtf` | **⒝の NVIDIA 実力**（短文・常駐ウォーム） | 25 §要点 8 の見立ては CUDA EP 0.28／DML 0.6。**判断則 ≤0.5 に届くか**がここで決まる |
| `totals.steps40_warm.breakdown_ms` | 段の内訳 | DiT が 8 割なら 23・25 と同じ姿。透かし・復号の比が上がるなら⒝の効き所が変わる |
| `totals.steps40_cold` − `..._warm` | 初回合成の上乗せ | ⒜⒞に要った「起動直後のウォームアップ射」（20 §3.8）が⒝で要るか |
| `totals.*.missing_stages` | 測れなかった段 | 空でなければその段は合計から抜けている＝**RTF を過小評価している** |
| `ep_effective.requested_ep_used_everywhere` | **本当に GPU で走ったか** | `false` なら CPU 落ち＝数字は GPU の値ではない（`stages.*.ep_fallback` に逐語） |
| `stages.dit_loop.s40.vs_reference_latent.max_abs` | 数値の健全性 | 1e-4 以下＝健全。**1e-2 級なら TF32**（25 §要点 5・壊れではない）。1 以上なら壊れ |
| `stages.dit_loop.s40.session_b1/b3.session_init_ms` | 重み読込の実費 | ×2（形状ごとに session を分けるため）＝常駐サーバの起動時間 |
| `stages.dit_fp16.vs_reference_latent.max_abs` | **fp16 の可否** | 5 前後＝壊れ（23・25 と同じ）。0.01 級＝**直っている＝⒝の評価が動く** |
| `stages.watermark.ep_used` / `.warm_ms` | 透かしが GPU に載るか | `cuda`＝載る（25 §表 2 の 47.3 ms）。`cpu (…fell back…)`＝EP には載らない |
| `stages.watermark_parts.warm_ms_sum` | **本体が 1 檔もロードできなかったとき**の代用（`sc_stft`+`sc_enc_c`+`sc_dec_c`・iSTFT 抜き） | この欄がある＝合計は「透かし未完成」の値（23 §5 と同じ勘定） |
| `stages.*.cpu_ref.max_abs_diff_vs_cpu_ep` | 段ごとの GPU vs CPU 差 | 段別に数値の健全性を切り分けられる |
| `failures[]` | 落ちた段の逐語 | 空が理想。空でなくても他の段は測れている |
| `nvidia_smi` | 機体とドライバ | 25 §要点 1 の「cu130 は 580 以上」を満たすかの物証 |

**cu126/cu130 との関係**＝このキットは torch を使わないので variant の別が無い。`run-cuda-kit.ps1` の結果（⒜⒞の材料）と、このキットの結果（⒝の材料）は**独立に読める**。

## 7. 停止域の確認

| 対象 | 確認（実行して得た値） |
|---|---|
| `upstream/Irodori-TTS` / `-Server` | `git status --short --ignored` が**両方とも出力なし**。`find upstream -name __pycache__` → **0** |
| `C:/IrodoriTTS/`・`C:/irodori-TTS-server/` | 作業開始（16:00）以降の更新 **0 檔／0 檔** |
| HF キャッシュ | 同じく **0 檔**。全実行に `HF_HUB_OFFLINE=1`（キットは HF を一切触らない） |
| `lab/onnx/**` | 更新 **0 檔**＝**読むだけ**。テスト用の写しは junction で参照した（4.4 GB の複製もしていない。junction は検証後 `rmdir` で外し、`lab/onnx` の 63 檔が無傷であることを確認） |
| 稼働機のサーバ | 8088／7861 の LISTEN **0**（**一度も起動していない**） |
| 本便が書いた場所 | `lab/kit-cuda/onnx-e2e/**`（22 檔）・`lab/out/26_*`（6 檔）・`lab/tmp/kit-e2e-test/**`・`lab/notes/26-onnx-e2e-kit.md` |
| 外部公開 | 無し（PyPI からの取得のみ。HF/GitHub への push 0） |
| 副作用（記録） | `%LOCALAPPDATA%\AMD\DxcCache\9cfbebf.*.parc`＝**23 §8-2 が作った 2 檔のうち 1 檔が 16:28 に更新された**（新規作成は無し）。DirectML のシェーダキャッシュ・利用者プロファイル配下＝保護対象外 |

`lab/tmp/kit-e2e-test/` の venv 2 本・uv キャッシュ・CPython・uv の写しは検証後に削除（約 2 GB 回収）。
残したのは `onnx-e2e/` の写しと `results/` のみ。結果 JSON は `lab/out/26_kit_e2e_{cuda,dml}.json` に写した。
生データ＝`lab/out/26_e2e_dml.json`・`26_e2e_cpu_ort1244.json`・`26_kit_e2e_cuda.json`・`26_kit_e2e_dml.json`・`26_kit_e2e_test{,2}.log`。

## 8. 未確認・注意

1. **3090 機での実走はしていない**（本機に NVIDIA が無い）。CUDA EP の値は 1 つも取れていない＝**このキットを回してもらうまで⒝の NVIDIA 端から端まではゼロ**。
2. **`onnxruntime-gpu[cuda,cudnn]` が 3090 機でも同じ 12 パッケージに解決するかは未確認**（本機で通ったのは PyPI の解決だけで、GPU の有無に依らないはず）。通らなければ `.cmd` が 3 段に退く。
3. **DirectML 側の venv は `onnxruntime-directml`（1.24.4 系）**＝透かしは載らない。3090 の DML 値は「透かし抜きの部品合計」で出る＝**CUDA 側と直接引き算しないこと**。
4. **透かしの入力は 12/23 便と同じ静的形（167,936 サンプル＝3.50 s）**で、実音声 3.76 s より 7 % 短い＝わずかに楽な計測。JSON の `input_note` に明記した。
5. **長文（10.92 s）は測らない**（条件 npz が短文のみ）。長文の RTF が要るなら `cond_real.npz` を長文で作り直す便が 1 つ要る。
6. **同時 Run は測らない**（23 §7 棘2 で DML は落ちる。CUDA EP の同時 Run 可否は未確認のまま）。
7. `--sessions 1` の比較（形状切替の罰が NVIDIA でも出るか）は**キットの既定では走らない**。司令官に 1 回追加で回してもらう余地はある（`--sessions 1` を付けるだけ・+3 分）。

## 9. 主席への注意

1. **比較表の⒝の GPU 行は、このキットの `totals.steps40_warm.rtf` が返ってくるまで「見立て」のままにすること**（25 §要点 8 の 1.05 s／0.28 は段の足し算で、DiT の非 CFG 区間を「約 15 ms」と仮置きした推定値。本キットはそこを実測する）。
2. **返ってきた JSON で最初に見るのは `ep_effective.requested_ep_used_everywhere`**。ここが `false` なら RTF は CPU の数字であって⒝の GPU 値ではない。25 便で `preload_dlls()` を入れるまで CUDA EP が黙って CPU に落ちていた前科がある。
3. **⒝の DirectML 行と CUDA 行は「透かしの扱い」が違う**（DML は部品合計・CUDA は本体）。比較表に載せるときは**同じ勘定に揃えるか、揃えられない旨を注記**すること。⒝＋DirectML で透かしを本当に載せるには iSTFT の作り直しが要る（23 §9-5・未実施）。
4. **fp16 は今回も「壊れている」側に賭けてよい**が、キットの `stages.dit_fp16` が 0.01 級を返したら話が変わる（配布物半減＋RTF 0.52→ROCm 肉薄）。**その 1 欄だけは必ず読むこと。**
5. **キットは 23 便の数字をビット単位で再現する**（最終潜在の diff が 0.0）＝3090 機の数字は 23 §6 の表と**そのまま割り算して倍率を出してよい**。物差しの翻訳は要らない。
6. **導入コストの実測が 1 つ増えた**＝⒝を CUDA で配るなら実行時 DLL だけで **1,228 MiB**（onnxruntime-gpu 141 MiB＋nvidia の CUDA 13 ランタイム 1,087 MiB）。⒜の torch cu130（1,781 MiB）より**約 550 MiB 軽いだけ**＝「⒝は配布が軽い」という利点は **DirectML（wheel 23.9 MiB・20 §要点 10）でこそ効き、CUDA EP を選んだ瞬間にほぼ消える**。比較表の「配布物の大きさ」欄に効く。
7. **23 §10 (g) の札は外してよい**（§3-1）。`dit_step_fp16` の CPU EP は 1.29.0 で 11.3 倍遅い＝ORT の版差で確定。fp32 には差が無い。
