# 33 — RTX 3090 機の第 5 回収（参照ボイス＝実機 VOICEVOX 4 話者＋COEIROINK 1 話者×10 s／30 s・キット `32`）

> 席＝Fable（主席）が回収して整理。実行＝司令官が `C:\kit-ssd\ref-bench\run-ref-bench.cmd` を SSD 上で走らせた（cu130 venv・bf16／fp32・4 走・`sync-results.cmd` 経由で N:\ へ）。
> 回収物＝`lab/out/3090-results-ssd-ref/`（`ref_bench_a_bf16_s40_short.json`・`b_bf16_s40_long`・`c_bf16_s10_short`・`d_fp32_s40_short`・`ref-wav/` 32 本・`ref-latent/` 10 本）。数値＝各 JSON の `summary`（warm 中央値・3 走・rep1＝cold）。5 話者の幅で書く。

## 要点（10 行以内）

1. **落ちるが小さい**＝bf16 40 steps 短文で参照なし 0.866 s／RTF 0.230 → 実ファイル 10 s **0.87〜0.95 s（+0〜10 %）**・30 s **0.96〜0.98 s（+11〜13 %）**。RTF は最悪 0.34（短文 30 s）。**全 4 走・全条件で RTF 0.5 未満**（最悪＝fp32 30 s の 0.39）。
2. **増分はほぼ参照符号化だけ**（bf16）＝`prepare_reference` 10 s **41〜52 ms**・30 s **105〜117 ms**（ROCm 8060S の 136／328 ms の 1/3）。`sample_rf` は 740〜800 ms で参照の有無に依らず＝短文・bf16 の DiT はカーネル起動が支配し、CFG 枝 4 も鍵長の増分も埋もれる（`31` 追記と同じ構図）。
3. **fp32 では DiT が伸びる**＝短文 40 steps で `sample_rf` 820 → 821〜1,024 ms（CFG 枝 4 が計算律速で表に出る）。端から端まで +6〜28 %（10 s）／+18〜36 %（30 s）・RTF 0.31〜0.39。**bf16 既定の理由が 1 つ増えた**。
4. **事前計算潜在（`ref_latent`）で増分は消える**＝`prepare_reference` **1 ms**・端から端まで −2〜+6 %（10 s）／−2〜+4 %（30 s）＝参照なしと同値。`31` §3 の逃げ道は GPU でも成立（CPU の 99 % 減に対し、GPU では絶対値 40〜115 ms の節約）。
5. **CUDA には ROCm のような参照長ごとの初見罰が無い**＝cold（rep1）は 0.88〜1.46 s の幅で、参照なし cold 1.095 s と同級（ROCm は長文 10 s で 19.4 s）。
6. **出力尺が変わる副作用**＝短文 3.76 s → 2.84〜3.40 s、長文 10.96 → 9.28〜11.24 s。RTF の上昇分の多くは分母が縮んだ分。長文の増分の幅（−2〜+33 %）も主に予測尺の差（`sample_rf` 960〜1,321 ms が尺に比例）。
7. **10 steps では参照符号化の比率が上がる**＝短文 0.340 s → 0.36〜0.42（+6〜23 %）／0.41〜0.46（+21〜34 %）。それでも RTF ≤ 0.15。
8. **話者差・サンプルレート差はノイズ幅の中**＝COEIROINK（44.1 kHz・リサンプル段あり）が特に重いことは無い。トークン数の式は実測一致（10.43 s → 66・`speaker_state [1,66,768]`）。
9. 聴感は未検分＝出力 wav 32 本（`ref-wav/`）を司令官が耳で見る（似ているか）。
10. `gpu_props.json`（`gpu-props.cmd`）＝追記のとおり回収済み＝CUDA ビルドの torch にも `uuid`・`pci_bus_id` があり、`nvidia-smi` は System32（`-L` 0.04 s）。

## 表 1＝端から端まで（3090・warm 中央値・5 話者の幅・出力尺は条件で変わる）

| 条件 | 参照なし | 実ファイル 10 s | 実ファイル 30 s | 事前計算潜在 10 s／30 s |
|---|---|---|---|---|
| bf16 40 steps 短文（3.76 s） | **0.866 s／RTF 0.230** | 0.87〜0.95 s（+0〜10 %）／0.27〜0.32 | 0.96〜0.98 s（+11〜13 %）／0.29〜0.34 | 0.85〜0.92（−2〜+6 %）／0.85〜0.90（−2〜+4 %） |
| bf16 40 steps 長文（10.96 s） | **1.232／0.112** | 1.22〜1.42（−1〜+15 %）／0.12〜0.15 | 1.21〜1.64（−2〜+33 %）／0.13〜0.16 | — |
| bf16 10 steps 短文 | **0.340／0.090** | 0.36〜0.42（+6〜23 %）／0.11〜0.15 | 0.41〜0.46（+21〜34 %）／0.13〜0.14 | — |
| fp32 40 steps 短文 | **0.927／0.247** | 0.98〜1.19（+6〜28 %）／0.31〜0.35 | 1.09〜1.26（+18〜36 %）／0.34〜0.39 | — |

段別（warm 中央値・ms）＝`prepare_reference` bf16 10 s 38〜55／30 s 99〜121、fp32 10 s 55〜73／30 s 137〜161、潜在経路 0.6〜1.1。`sample_rf` bf16 短文 738〜798（参照なし 753）・長文 960〜1,321（参照なし 1,057）・fp32 短文 821〜1,024（参照なし 820）。モデル読込 9.0〜9.4 s・読込後 VRAM bf16 1,767 MB／fp32 3,460 MB。

## 主席への注意

- 報告 §10-3 の答え＝「落ちるが小さい（bf16 で +0〜13 %）・GPU なら RTF 0.5 を割らない（最悪 0.39）・参照潜在の事前計算で増分は消える・fp32 では DiT の増分が表に出る」。3090 の見積り（0.95〜1.05 s）は実測 0.87〜0.98 s で、やや上振れの見積りだった。
- ⒞の層の候補「参照潜在のキャッシュ」は GPU では 40〜115 ms の節約＝CPU 経路（1〜4 s）ほどの値打ちは無い。採るかは卓（10-4 の 13）。

## 追記＝`gpu_props.json`（`gpu-props.cmd`・cu130 venv・`lab/out/3090-results-ssd-ref/gpu_props.json`）

- `import torch` 1.218 s（SSD）・`device_count()` 0.2 ms・1 枚（index 0）。
- **CUDA ビルド（torch 2.10.0+cu130）の `get_device_properties(0)` にも `uuid` と `pci_bus_id`／`pci_device_id`／`pci_domain_id` がある**（属性 23 個・`30` V-3 は解消＝stub の欠落は stub 側の不備）。値＝`uuid 19adfe89-c9e0-df55-4a8a-e31798717a36`・PCI domain 0／bus 1／device 0・24,575.5 MB・compute 8.6。
- **`nvidia-smi` は `C:\WINDOWS\system32\nvidia-smi.EXE`（PATH 上・`shutil.which` で見つかる）**。`-L` 0.04 s＝`GPU 0: NVIDIA GeForce RTX 3090 (UUID: GPU-19adfe89-c9e0-df55-4a8a-e31798717a36)`。`--query-gpu=index,name,uuid,pci.bus_id,driver_version --format=csv`＝`0, NVIDIA GeForce RTX 3090, GPU-19adfe89-…, 00000000:01:00.0, 591.86`（`30` V-2 は解消）。
- 照合＝torch の `uuid` は nvidia-smi の UUID から接頭辞 `GPU-` を除いたものと一致。PCI は `00000000:01:00.0` ↔ `pci_domain_id 0／pci_bus_id 1／pci_device_id 0`。
