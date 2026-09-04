# 32 — 参照ボイス実測キット（3090 機で司令官が回す一式）

> 席＝第 8 次サブ席（opus）／担当＝司令官の指示「**実機の VOICEVOX などで参照ボイスを数種類用意して RTX 3090 機で実測**」の
> **道具立て**（実測そのものは 3090 機で司令官が回す）。実施 2026-09-04。
> 参照ボイス 10 本は主席が用意（`lab/out/refvoices/`）。**GPU は 1 度も使っていない**（別席が 8091 で GPU 実射中）。
> 検証は `lab/.venv`（CPU・torch 2.10.0+cpu・fp32）。8088／7861／8091 には触れていない。
> 書いたのは `lab/kit-cuda/ref-bench/**`・`lab/out/32_ref_smoke.json`・`lab/out/ref-latent/**`・`lab/out/ref-wav/**`・
> `lab/tmp/kit-ref-test/**`・`lab/tmp/32_kit_ref_test*.log`・本ノートのみ。上流は**読むだけ**（無改変・§5）。
> 相対パスの起点は `C:/Users/mugonkun/source/repos/irodori-native-research/`。

## 要点（10 行以内）

1. **成果＝`lab/kit-cuda/ref-bench/`（4 檔＋voices 11 檔・12 MB）**。3090 機の `C:\kit-ssd\ref-bench\` に丸ごと置いて
   `run-ref-bench.cmd` をダブルクリックするだけ。**キットの外に 1 バイトも書かない**（21 §2-1 の釘を踏襲）。
2. `ref_bench_voices.py`＝31 便の `lab/bin/ref_bench.py` を土台に、「参照秒数から合成した素材」ではなく
   **`voices/` の実ファイルを回す**版。条件＝`no-ref` ＋ 各 voice wav ＋（`--with-latent` のとき）各 voice の事前計算潜在。
   **rep を外側・条件を内側に固定**（31 §5-1 の interleave を既定に格上げ＝混んだ機でも条件が偏らない）。
3. **`--with-latent` が本便の要**＝31 §3 の「逃げ道」を**測れる形にした**。CPU 実測で `prepare_reference` が
   **1,175 ms → 9.5 ms（10 s）／3,808 ms → 8.3 ms（30 s）**＝**99.2 %／99.8 % 減**。31 §8-d の「未測」札が 1 枚剥がれた。
4. **CPU 検証で 31 の式が実測照合できた**＝speaker トークン数 `floor(round(sec×25)/4)+1` は 10.43 s→**66**・34.66 s→**217** で**一致**。
   ただし**潜在フレーム数は round ではなく ceil**（34.656 s → 式 866・実測 867）。トークン数は `//4` で丸まるので結論は変わらない。JSON に両方出す。
5. **`run-ref-bench.cmd` は ASCII・CRLF**（`file(1)` で `DOS batch file, ASCII text, with CRLF line terminators` 確認）。
   `.venv-cu130` が無ければ「`run-cu130.cmd` を先に」と出して `pause`。走らせる順＝(a) bf16 40 steps 短文 `--with-latent --save-wav`／
   (b) bf16 40 steps 長文 `--save-wav`／(c) bf16 10 steps 短文／(d) fp32 40 steps 短文。**見込み 20〜40 分**。
6. **通しで実行済み**＝`lab/tmp/kit-ref-test/`（src は upstream への junction・hf は既存 HF キャッシュへの junction・
   `.venv-cu130` は `lab/.venv` への junction）で **4 走とも完走・`script failures: 0`**。
7. **CPU 通しで踏んだ .cmd の罠を 1 つ潰した**＝`if errorlevel 1 ( ... echo pass (a) ... )` の**丸括弧がブロックを早閉じ**して
   `exited was unexpected at this time.` になる。08 §1-C の bat 事故の親戚＝**括弧つきブロックの中に丸括弧を書かない**。
8. **司令官の問いの答えがどの数字か**を README-ref.md §6 に 8 項目で明記した。本命は
   `summary.<条件>.vs_no_ref.warm_median_total_pct`（何 % 落ちるか）と `summary.<条件>.warm_median_rtf`（**0.5 未満なら合格**）。
9. **3090 機では環境変数を立てない**。`REF_STEPS`／`REF_REPS`／`REF_VOICES`／`REF_DEVICE`／`REF_PRECISION` は
   **CPU 素振り専用の逃げ口**（本便の検証で使った）。立てると本番の数字でなくなる。
10. **参照ボイスは声質の差も測れる形にした**（5 話者 × 10 s／30 s）。ただし**似ているかの判定は耳**＝`--save-wav` で
    `results\ref-wav\` に warm の出力を残すようにした。司令官が後で聴く。

---

## 1. 成果物（`lab/kit-cuda/ref-bench/`）

| 檔 | バイト | 役目 |
|---|---:|---|
| `ref_bench_voices.py` | 24,422 | 本体。実ファイルを回す参照ボイス・ベンチ（§2） |
| `run-ref-bench.cmd` | 5,009 | ダブルクリック用ランチャ。**ASCII・CRLF**・`cd /d "%~dp0.."` でキット直下を KIT とする（§3） |
| `README-ref.md` | 10,559 | 司令官向け手順書（日本語・1 行 1 操作・結果の読み方・§4） |
| `voices/` | 11 檔・12 MB | 主席が用意した参照ボイス 10 本 ＋ `manifest.json`（`lab/out/refvoices/` の写し・**ASCII 名のまま**） |

参照ボイスの内訳（`manifest.json` の実測秒数）:

| voice_id | 話者 | エンジン | sr | 10 s 級 | 30 s 級 |
|---|---|---|---:|---:|---:|
| `vv_zundamon` | ずんだもん | VOICEVOX | 24 kHz | 10.43 s | 34.66 s |
| `vv_metan` | 四国めたん | VOICEVOX | 24 kHz | 14.25 s | 30.19 s |
| `vv_ryusei` | 青山龍星 | VOICEVOX | 24 kHz | 14.12 s | 34.14 s |
| `vv_takehiro` | 玄野武宏 | VOICEVOX | 24 kHz | 13.13 s | 32.25 s |
| `co_ameno` | アメノちゃん | COEIROINK | 44.1 kHz | 11.26 s | 30.65 s |

- **全 10 本が上限 120 s の 1/4 以下**＝切り詰めも warning も起きない（31 §1-3）。`voices[].over_max_ref_seconds` で JSON にも出す。
- **入力の sr はバラバラ（24 k／44.1 k）だが問題にならない**＝`encode_waveform` が 48 kHz へリサンプルする（31 §1-2 R2）。
  31 便の素材は 48 kHz だったので R2 は不発だったが、**本便の素材では R2 が実際に走る**＝31 より現実に近い。

## 2. `ref_bench_voices.py`（31 便の台本との違い）

| 論点 | 31 の `lab/bin/ref_bench.py` | 本便の `ref_bench_voices.py` |
|---|---|---|
| 参照素材 | `--ref 0 10 30` の秒数から合成物を繰り返して自作 | **`--voices-dir` の実ファイル**（`--manifest` で列挙・`--voices` で voice_id 絞り込み） |
| 条件 | 参照秒数 | `no-ref` ＋ `wav:<檔名>` ＋ `lat:<檔名>` |
| 順序 | `--interleave` は任意 | **interleave 固定**（rep 外側・条件内側） |
| `ref_latent` | 無し | **`--with-latent`**＝runtime と同じ経路で潜在を事前計算して `results/ref-latent/<檔名>.pt` に保存し、その経路も回す |
| wav 保存 | 無し | **`--save-wav`**＝最終 rep の出力を `results/ref-wav/<tag>_<条件>.wav` に保存 |
| 既定 | `--device cpu --precision fp32 --reps 2` | **`--device cuda --precision bf16 --reps 3`**（rep1=cold） |
| 集計 | 最小値のみ | **最小値 ＋ warm 中央値 ＋ `no-ref` 比の増分（%）** |
| 形の照合 | 形を出すだけ | **31 の式との一致を `formula_matches` で真偽判定**（潜在フレームは ceil 版も併記） |

引数＝`--device --codec-device --precision --steps --reps --len --voices-dir --manifest --voices --with-latent --save-wav
--seconds --micro-reps --skip-micro --skip-synth --threads --results-dir --seed --caption --checkpoint --tag --out`。

記録するもの（JSON・UTF-8）:

- `runs[]`＝`cond` / `rep` / `phase`（cold/warm）/ `wall_s` / `total_to_decode_s` / `rtf` / `audio_s` /
  `stage_ms`（`prepare_reference` `tokenize_text` `predict_duration` `sample_rf` `unpatchify_latent` `decode_latent` `silentcipher_watermark`）/
  `vram_peak_mb`（CUDA のとき）/ `messages`（切り詰め warning・予測尺の逐語）/ `wav_out`。
- `voices[]`＝**参照の実秒数**（`sf.info` の frames÷sr）・実 sr・チャンネル・バイト・`over_max_ref_seconds`・期待トークン数。
- `latents[]`＝`--with-latent` のとき。**潜在の事前計算に何 ms かかったか**・`.pt` の形／dtype／バイト・切り詰めの有無。
- `ref_encode_micro[]`＝voice ごとの分解（`_load_audio` ／ `encode_waveform`（正規化あり・なし）／ `ReferenceLatentEncoder`）
  ＋ **潜在フレーム数と speaker トークン数の実測 vs 式**・`ms_per_ref_second`。
- `summary`＝条件ごとの `min_*` と `warm_median_*`、`vs_no_ref`（`min_total_pct` / `warm_median_total_pct` /
  段別の `*_delta_ms`・`*_pct` / `audio_s_delta`）。

**上流は無改変**＝monkeypatch は `torchaudio.load` / `torchaudio.save` を soundfile 実装に差し替えるシム 1 つだけ（31 §7 と同じ理由）。
潜在の事前計算は `inference_runtime._load_reference_latent` の wav 枝（`:935-951`）と**同じ順**で再現している
（`_load_audio` → 単一 wav なら秒で先に切る → `encode_waveform(normalize_db=-16.0, ensure_max=True)` → `.cpu()`）。
保存の形は `(T,32)`＝`_coerce_latent_shape`（`inference_runtime.py:136-147`）がそのまま受ける形。
読みは上流の `torch.load(..., weights_only=True)`（`:906`）で通ることを実測で確認した。

## 3. `run-ref-bench.cmd`

- **ASCII・CRLF**（21 §2-2 の釘）。`cd /d "%~dp0.."` → `set "KIT=%CD%"`＝**キット直下が KIT**（`onnx-e2e/run-onnx-e2e.cmd` と同型）。
- 環境＝`HF_HOME=%KIT%\hf`・`HF_HUB_OFFLINE=1`・`PYTHONPATH=%KIT%\src\Irodori-TTS`・`PYTHONDONTWRITEBYTECODE=1`・
  `PYTHONUTF8=1`（＋`PYTHONIOENCODING=utf-8`・`PYTHONWARNINGS=ignore`）。
- 門＝`.venv-cu130\Scripts\python.exe` が無ければ「`run-cu130.cmd` を先に」と出して `pause`。台本と `voices\manifest.json` の在否も見る。
- 出力＝`%KIT%\results\ref_bench_<tag>.json`。tag＝`a_bf16_s40_short` / `b_bf16_s40_long` / `c_bf16_s10_short` / `d_fp32_s40_short`
  （**精度と steps を tag に埋める**ので、環境変数で上書きしても檔名が嘘にならない）。最後に `script failures:` を出して `pause`。
- **所要の見込み**＝(a) 8〜12 分・(b) 6〜10 分・(c) 3〜5 分・(d) 5〜8 分＝**合計 20〜40 分**（README §3 に記載）。
  内訳＝モデル読み込み 4 回（各 30〜60 秒）＋合成 162 回＋参照符号化の分解測定 40 回。
- **CPU 素振り用の逃げ口**＝`REF_STEPS` / `REF_REPS` / `REF_VOICES` / `REF_DEVICE` / `REF_PRECISION`。
  README §8 に「**3090 機ではどれも立てない**」と明記した。

## 4. 検証（すべて `lab/.venv`・CPU・fp32）

### 4-1. 台本単体（`--voices vv_zundamon --steps 4 --reps 1 --with-latent --save-wav`）

正本＝`lab/out/32_ref_smoke.json`。**5 条件すべて完走**（`no-ref` / `wav:*_10s` / `wav:*_30s` / `lat:*_10s` / `lat:*_30s`）。

| 条件 | prepare_reference | predict_duration | sample_rf | 合計 | audio_s |
|---|---:|---:|---:|---:|---:|
| `no-ref` | **0.1 ms** | 1,108.5 | 2,965.0 | **5.381 s** | 3.76 |
| `wav:vv_zundamon_10s` | **1,303.2 ms** | 1,053.2 | 2,613.9 | 6.037 s | 3.24 |
| `wav:vv_zundamon_30s` | **3,854.6 ms** | 1,047.2 | 2,809.1 | 8.730 s | 3.24 |
| `lat:vv_zundamon_10s` | **8.1 ms** | 977.0 | 2,763.0 | 4.838 s | 3.24 |
| `lat:vv_zundamon_30s` | **9.8 ms** | 1,157.5 | 3,088.5 | 5.396 s | 3.24 |

- **`.pt` は出た**＝`lab/out/ref-latent/vv_zundamon_10s.pt`（`(261,32)` fp32・35,041 B）・`_30s.pt`（`(867,32)`・112,609 B）。
  **事前計算は 1,711 ms（10 s）／3,858 ms（30 s）**＝「声の登録時に 1 回だけ払う」費用。
- **wav も出た**＝`lab/out/ref-wav/32_smoke_{no-ref,wav-…,lat-…}.wav`（5 本・48 kHz）。
- **`ref_latent` 経路は `no-ref` と同じ形で通る**＝例外なし・`messages` から参照の逐語が消えるだけ・出力尺は wav 経路と同じ 3.24 s。
- **形の照合**＝`speaker_state` は `(1,66,768)`（10.43 s）／`(1,217,768)`（34.66 s）＝31 の式と**一致**（`formula_matches: true`）。
- `steps 4` なので `sample_rf` の CFG 枝の増分は固定費に埋もれる（40 steps で見るべき数字）。

### 4-2. `.cmd` の通し（`lab/tmp/kit-ref-test/`）

キットの写しを `lab/tmp/kit-ref-test/` に作った（`ref-bench/` は実体コピー・`src/Irodori-TTS` は upstream への junction・
`hf` は `C:/Users/mugonkun/.cache/huggingface` への junction・`.venv-cu130` は `lab/.venv` への junction）。
`REF_DEVICE=cpu REF_PRECISION=fp32 REF_STEPS=4 REF_REPS=1 REF_VOICES=vv_zundamon` を立てて 4 走とも実行。

- **結果＝4 走とも完走・逐語 `Finished.  script failures: 0`・終了コード 0**（ログ＝`lab/tmp/32_kit_ref_test2.log`）。
  出た檔＝`lab/tmp/kit-ref-test/results/ref_bench_{a_fp32_s4_short,b_fp32_s4_long,c_fp32_s4_short,d_fp32_s4_short}.json`
  ＋ `ref-latent/*.pt` 2 本 ＋ `ref-wav/*.wav` 8 本（(a) 5 本・(b) 3 本＝`--save-wav` を付けた走のぶんだけ）。
  長文走（b）の CPU 実測＝`no-ref` 8.909 s／RTF 0.816・`wav:*_10s` 10.447 s（**+17.3 %**）・`wav:*_30s` 13.397 s（**+50.4 %**）
  ＝**出力が長いほど参照の固定費は相対的に薄まる**（短文走の +23.0 %／+80.6 % と比べて）。
- **1 回目は失敗している**（`lab/tmp/32_kit_ref_test.log`）＝(a) は完走したが .cmd が
  `exited was unexpected at this time.` で止まった。原因は `if errorlevel 1 ( ... echo pass (a) ... )` の**丸括弧**（§要点 7）。修正済み。
- `file(1)`＝`run-ref-bench.cmd: DOS batch file, ASCII text, with CRLF line terminators`・**非 ASCII バイト 0**。

## 5. 停止域の確認（無改変の証明）

```bash
git -C upstream/Irodori-TTS status --short --ignored          # -> 出力なし
find upstream -name "__pycache__" -not -path "*/.git/*"       # -> 出力なし
```

- **GPU は 1 度も使っていない**（torch は CPU ビルド・`--device cpu` のみ）。**8088／7861／8091 の起動・停止・HTTP 送信は一切なし**。
- 外部へ何も公開していない（`HF_HUB_OFFLINE=1`・push なし）。HF キャッシュは checkpoint／コーデックの**読み込みのみ**。
- `C:/kit-ssd/`・`C:/IrodoriTTS/`・`C:/irodori-TTS-server/`・yomiwakechan2 には**触れていない**（読んでもいない）。
- `lab/out/refvoices/` は**読むだけ**（`voices/` へコピーしただけ・原本は無改変）。
- 書いたもの＝§冒頭のとおり。`lab/tmp/kit-ref-test/` の junction は**リンクだけ**＝リンク先には書いていない
  （`results/` は写しの中にできる）。

## 6. 主席への注意

1. **渡し方**＝`lab/kit-cuda/ref-bench/` を丸ごと N: 経由で 3090 機の `C:\kit-ssd\ref-bench\` へ置く。
   `kit-ssd` は `run-cu130.cmd` 済みなので `.venv-cu130`・`src\Irodori-TTS`・`hf` はそのまま使える。
   **司令官への 1 行＝「`C:\kit-ssd\ref-bench\run-ref-bench.cmd` をダブルクリック。20〜40 分。窓の最後が `script failures: 0` なら成功」**。
   `sync-results.cmd` の窓を先に開けておけば `results\` は 1 分ごとに N: へ写る。
2. **報告の速度表の注記は 31 §8-2 のまま生きている**＝13・20・23・24・25・27・28 の全数値は `--no-ref`（CFG バッチ 3）。
   本便の JSON が返ってきたら、**参照ありの実測**でその注記を数字に置き換えられる。
3. **⒞（専用制御方式）の値打ちの数字がここで確定する**＝`lat:*` と `wav:*` の `prepare_reference` の比。
   CPU では 99 % 減を確認済み。**GPU でも同じ比になるとは限らない**（GPU では DACVAE encoder が速くなるので、
   減る絶対値は小さくなる）＝JSON が来るまで「97 %」「99 %」を GPU の数字として書かないこと。
4. **31 の式の訂正 1 件**＝潜在フレーム数は `round(sec×25)` ではなく **`ceil(sec×25)`**。speaker トークン数
   （`floor(frames/4)+1`）は丸めで吸収されるので **31 §1-2 の結論は変わらない**。ノート 31 に脚注を入れるかは主席の裁量。
5. **聴感の判定は本便の範囲外**。`--save-wav` で `results\ref-wav\` に 21 本（(a)）＋ 11 本（(b)）が残るので、
   **司令官に「参照ボイスに似ているか」を耳で見てもらう**のが次の一手。似ていなければ速度の話の前に⒞の設計が変わる。
6. **`co_ameno` は 44.1 kHz**＝他の 4 話者（24 kHz）と入力 sr が違う。リサンプル段（31 §1-2 R2）が走る唯一の系列なので、
   **`co_ameno` だけ `prepare_reference` が相対的に重かったら sr の差**（話者の差ではない）。JSON の `voices[].actual_sample_rate` で切り分ける。
7. **`--seconds` は本番では使わない**。使うと duration 予測がバイパスされて `predict_duration` が消え、
   Server のチャンク分割も黙って無効化される（14 T-7）＝**実運用の数字にならない**。S_lat を揃えた対照が要るときだけ足す。
8. **未実施の札**＝(a) **GPU 実測そのもの**（本便は道具立てのみ）、(b) ONNX 経路（CUDA EP／DirectML）での参照あり
   ＝`26` のキットは `data/` の条件が S_spk=2 で焼かれている（31 §8-b と同じ札）、(c) `ref_embed`（Speaker Inversion）
   ＝`.speaker.safetensors` を持っていない、(d) 複数クリップ（`ref_wavs`）、(e) 参照 120 s（上限）、(f) **音の検分**。
