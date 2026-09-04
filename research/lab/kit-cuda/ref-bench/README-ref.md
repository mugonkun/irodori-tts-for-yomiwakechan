# 参照ボイス実測（32 便）— RTX 3090 機での回し方

司令官の問い＝**「実機の VOICEVOX などで作ったサンプルボイスを当てたら、レスポンスは大きく落ちるか」**。
これまでの速度実測（13・20・23・24・25・27・28 ノート）は**全部「参照なし（`--no-ref`）」**だった。
このフォルダはそこを実機の参照ボイスで埋めるための一式。**キットの外に 1 バイトも書かない。**

---

## 1. 置き場所

1. このフォルダ（`ref-bench`）を**まるごと** 3090 機のキット直下に置く＝`C:\kit-ssd\ref-bench\` になるようにする。
2. `C:\kit-ssd\.venv-cu130\`・`C:\kit-ssd\src\Irodori-TTS\`・`C:\kit-ssd\hf\` があることを確認する（`run-cu130.cmd` を 1 度でも通していれば全部ある）。
3. まだなら先に `C:\kit-ssd\run-cu130.cmd` をダブルクリックして終わらせる。

## 2. 走らせる

1. `C:\kit-ssd\sync-results.cmd` をダブルクリックする（結果を N: へ写し続ける窓。開いたままにする）。
2. `C:\kit-ssd\ref-bench\run-ref-bench.cmd` をダブルクリックする。
3. 黒い窓が閉じるまで待つ（**20〜40 分**）。他の GPU 作業は同時にしない。
4. 窓の最後の行に `script failures: 0` と出ていれば成功。
5. `C:\kit-ssd\results\ref-wav\` の wav を聴く（声が参照ボイスに似ているかは耳で判定する。数字では出ない）。
6. `N:\irodori-native-research\results-ssd\` に写った `ref_bench_*.json` 4 本を持ち帰る。

## 3. 何を 4 回走らせているか

| 走 | 精度 | steps | 本文 | 追加 | 出力 |
|---|---|---:|---|---|---|
| (a) | bf16 | 40 | 短文 | `--with-latent`・`--save-wav` | `results\ref_bench_a_bf16_s40_short.json` |
| (b) | bf16 | 40 | 長文 | `--save-wav` | `results\ref_bench_b_bf16_s40_long.json` |
| (c) | bf16 | 10 | 短文 | — | `results\ref_bench_c_bf16_s10_short.json` |
| (d) | fp32 | 40 | 短文 | — | `results\ref_bench_d_fp32_s40_short.json` |

- 所要の見込み＝(a) 約 8〜12 分・(b) 約 6〜10 分・(c) 約 3〜5 分・(d) 約 5〜8 分＝**合計 20〜40 分**。
  内訳は「モデル読み込み 4 回（各 30〜60 秒）＋合成 162 回＋参照符号化の分解測定 40 回」。
- (a) だけ条件が倍ある（wav 経路 10 本 ＋ 事前計算した潜在 10 本 ＋ 参照なし＝21 条件）。
- (b) の長文は出力音声が 3 倍長い＝`decode_latent` と透かしが伸びる。参照のコストが**出力長に埋もれるか**を見るための走。
- (c) は steps を 1/4 にした走＝**参照の固定費が相対的にどれだけ効くか**を見る。
- (d) は fp32＝bf16 との比。これまでの CPU 基準（11・31 ノート）と同じ精度。

## 4. 条件（1 走のなか）

- `no-ref` … 参照なし。**これまでの全実測と同じ条件＝比較の原点**。CFG バッチ 3。
- `wav:<檔名>` … `voices\` の wav をそのまま当てる。**実運用の経路**。CFG バッチ 4。
- `lat:<檔名>` … 同じ wav から**事前に作った潜在 `.pt`** を当てる（(a) だけ）。31 §3 の「逃げ道」。

参照ボイスは 10 本＝VOICEVOX 4 話者（ずんだもん・四国めたん・青山龍星・玄野武宏）と COEIROINK 1 話者（アメノちゃん）の
**10 秒級と 30 秒級**。内訳は `voices\manifest.json`。

## 5. 結果 JSON の読み方

`ref_bench_*.json` の中身は 5 つ。

| 鍵 | 中身 |
|---|---|
| 先頭の平たい欄 | 機体・精度・steps・GPU 名・モデル読み込み秒・`ref_max_seconds_default`（＝120.0） |
| `voices` | 参照ボイス 1 本ごとの**実秒数**・サンプリング周波数・期待される潜在フレーム数と speaker トークン数 |
| `latents` | `--with-latent` のときだけ。潜在の**事前計算に何 ms かかったか**・`.pt` の形と大きさ |
| `runs` | 合成 1 回ぶんの生値。**条件 × rep** の全部が残る |
| `ref_encode_micro` | 参照の符号化を段に割った測定（wav 読み／DACVAE encoder／参照 Transformer） |
| `summary` | 条件ごとのまとめ。**ここだけ読めば答えは出る** |

### `runs` の 1 行

```
"cond": "wav:vv_zundamon_10s"   条件（no-ref / wav:檔名 / lat:檔名）
"rep": 1, "phase": "cold"       rep1 だけ cold（モデルの初回暖機を被る）。rep2 以降が warm
"total_to_decode_s": 1.05       ← 端から端まで。これが「レスポンス」
"rtf": 0.28                     ← total_to_decode ÷ 出力音声の秒数。0.5 未満なら配信で使える
"audio_s": 3.24                 出力音声の秒数（参照を当てると短くなる。§6-3）
"stage_ms": { ... }             段別のミリ秒
"vram_peak_mb": 4321.0          その 1 回の VRAM ピーク
"messages": [ ... ]             上流が返した逐語（切り詰め warning・予測尺など）
```

`stage_ms` の中身：

| 段 | 意味 | 参照で増えるか |
|---|---|---|
| `prepare_reference` | 参照 wav を読んで DACVAE encoder に通す | **増える（参照秒数に比例）**。参照なしは 0.1 ms 級、潜在経路は 10 ms 級 |
| `predict_duration` | 尺の予測（ModernBERT ×2 ＋ 参照 Transformer 1 回） | 少し増える（参照 Transformer 1 回ぶん） |
| `sample_rf` | **DiT の反復（ここが支配的）** | **増える（CFG の枝が 3→4 になる）** |
| `decode_latent` | 潜在 → 波形 | 増えない（出力音声長にのみ比例） |
| `silentcipher_watermark` | 電子透かし | 増えない（同上） |

### `summary` の 1 ブロック

```
"wav:vv_zundamon_10s": {
  "min_total_s": 1.05,            3 走の最小値（機体が混んでいてもこれは素直）
  "min_rtf": 0.28,
  "min_stage_ms": { ... },
  "warm_median_total_s": 1.07,    rep2 以降の中央値（＝常駐サーバでの実効値に近い）
  "warm_median_rtf": 0.29,
  "vs_no_ref": {
     "min_total_pct": 22.4,       ← ★ 参照なしに対して何 % 遅くなったか
     "warm_median_total_pct": 21.8,
     "prepare_reference_delta_ms": 95.0,   ← 参照の符号化に増えたぶん
     "sample_rf_pct": 14.0,                ← ★ DiT が何 % 重くなったか
     "audio_s_delta": -0.52       出力音声が何秒短くなったか
  }
}
```

## 6. どの数字が司令官の問いの答えになるか

1. **「レスポンスは落ちるか」＝`summary` の各条件の `vs_no_ref.warm_median_total_pct`**（無ければ `min_total_pct`）。
   `no-ref` を 0 % として、実機の参照ボイスを当てると何 % 遅くなるかがそのまま出る。
2. **「配信に耐えるか」＝`warm_median_rtf`**。**0.5 未満なら合格**（読み上げが実時間に追いつく）。
   (a) の bf16 40 steps 短文の `wav:*` 条件の RTF が判定の本命。参考＝参照なしの実測は `27` ノートで **RTF 0.215**。
3. **「落ちるとしてどこが重いか」＝`vs_no_ref` の 2 つの数字の大小**。
   - `prepare_reference_delta_ms` … 参照の符号化。**参照秒数に比例する固定費**。10 s と 30 s の条件を比べれば比例が見える。
   - `sample_rf_pct` … **CFG の枝が 3→4 に増えたぶん**。参照秒数にはほぼ依らない。CPU 実測では +20.6 %、GPU の理論値は +12〜18 %（31 §4-2）。
4. **「逃げ道は効くか」＝(a) の `lat:*` 条件と `wav:*` 条件の `prepare_reference` の比**。
   潜在を事前計算して置いておけば `prepare_reference` はほぼ 0 になるはず（CPU では 1,303 ms → 8 ms を確認済み）。
   ここが効いていれば、読み分けちゃん側は**「声の登録時に 1 回だけ潜在を作る」**設計で参照の固定費を消せる。
   1 回ぶんの費用は `latents[].precompute_ms` に出る。
5. **「10 秒と 30 秒でどれだけ違うか」**＝同じ voice\_id の `_10s` と `_30s` の条件を並べる。
   差のほとんどは `prepare_reference` に出るはず（`sample_rf` の差は小さい）。
6. **「声によって違うか」**＝5 話者の同じ長さの条件を並べる。参照の長さが同じなら**話者では変わらないはず**（変わったら要報告）。
7. **VRAM**＝`vram_peak_mb` の最大値。参照ありは speaker トークンが増える（10 s で 66・30 s で 217）ので少し増える。
8. **`audio_s_delta`** ＝ **参照を当てると同じ本文でも出力が短くなる**（尺の予測に参照が効くため）。字幕同期・尺合わせを作るなら仕様に要る数字。

## 7. うまくいかないとき

- `ERROR: ... .venv-cu130\Scripts\python.exe not found` → 先に `run-cu130.cmd` を通す。
- `precision='bf16' currently requires CUDA or XPU device.` → CPU で走らせている。3090 機で走らせる。
- 途中で 1 走だけ落ちても残り 3 走は続く。最後の `script failures:` の数が落ちた走の数。
- ログは窓に流れるだけなので、心配なら窓を右クリック→全選択→コピーしてテキストに貼って送る。

## 8. 試験用の逃げ口（3090 機では使わない）

CPU で配線だけ確かめたいときに、**環境変数を先に立ててから** `.cmd` を叩くと軽くできる。

| 変数 | 効き |
|---|---|
| `REF_STEPS` | 4 走すべての steps を上書き（例 `4`） |
| `REF_REPS` | 各条件の反復回数（例 `1`） |
| `REF_VOICES` | 使う voice\_id を絞る（例 `vv_zundamon`） |
| `REF_DEVICE` | デバイス（例 `cpu`） |
| `REF_PRECISION` | 4 走すべての精度を上書き（例 `fp32`） |

**3090 機での本番はどれも立てない**（立てると数字が本番のものでなくなる）。

## 9. 台本を直に叩く（1 行）

```
C:\kit-ssd\.venv-cu130\Scripts\python.exe C:\kit-ssd\ref-bench\ref_bench_voices.py --device cuda --precision bf16 --steps 40 --reps 3 --len short --with-latent --save-wav --tag manual --out C:\kit-ssd\results\ref_bench_manual.json
```

引数＝`--device` `--codec-device` `--precision` `--steps` `--reps` `--len` `--voices-dir` `--manifest` `--voices`
`--with-latent` `--save-wav` `--seconds` `--micro-reps` `--skip-micro` `--skip-synth` `--threads` `--results-dir` `--tag` `--out`。
`--seconds 3.76` を足すと尺を固定して duration 予測をバイパスできる（参照の有無で出力長が変わるのを止めて DiT の増分だけを見たいとき）。
