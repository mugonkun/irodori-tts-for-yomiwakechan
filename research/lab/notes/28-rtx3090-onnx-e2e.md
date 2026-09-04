# 28 — RTX 3090 機の第 4 回収（⒝の端から端まで＝ONNX の CUDA EP／DirectML EP 実測・キット `26`）

> 席＝Fable（主席）が回収して整理。実行＝司令官が `C:\kit-ssd\onnx-e2e\run-onnx-e2e.cmd` を 3090 機のローカル SSD で走らせた（`27` の直後・cu130 の走行と同じ機体状態）。
> 回収物＝`lab/out/3090-results-ssd-cu130/onnx_e2e_cuda.json`・`onnx_e2e_dml.json`（`26` §「結果 JSON の読み方」）。短文 3.76 s・seed 1234・x0／条件は `26` 同梱の `data/`（司令官機 `13` と同じ）。

## 要点（10 行以内）

1. **両 EP とも「要求 EP が全段で使われた」**（`ep_effective.requested_ep_used_everywhere = true`・`25` の CPU 落ちの前科は無し）。CUDA EP＝ORT 1.29.0（`preload_dlls` ok）・DirectML＝ORT 1.24.4。壁時計は CUDA 21 s・DML 25 s（venv 作成除く）。
2. **CUDA EP・40 steps・warm＝1.094 s／RTF 0.291**（`25` §要点 8 の見立て 1.05 s／0.28 と一致）。内訳＝条件符号化 37.7 ms・duration 2.2・**DiT ループ 964 ms**（CFG ステップ中央値 30.5 ms×20・非 CFG 16.4 ms×20）・復号 38.0・透かし 51.8（GPU で動く）。cold 1.376 s／0.366。10 steps＝0.378 s／**0.100**。
3. **DirectML・40 steps・warm＝1.982 s／RTF 0.527（透かし抜き）**。内訳＝条件符号化（`_az0` 手術版）445.7 ms・duration 2.9・**DiT ループ 1,444 ms**（CFG 46.5 ms・非 CFG 25.0 ms）・復号（`_dml` 手術版）89.7・**透かしは ORT 1.24.4 では DML でも CPU EP でもロード不可**（逐語＝`Node (node_DFT_342) Op (DFT) [ShapeInferenceError] is_onesided and inverse attributes cannot be enabled at the same time`）＝`missing_stages: ["watermark_parts"]`。透かしを CPU に出す今日の形（司令官機 331 ms 級・3090 機の i9 でも 200〜300 ms の見込み）を足すと **≈2.2〜2.3 s／RTF ≈0.6**＝`25` の見立て 0.6 と一致。cold 2.579 s／0.686。10 steps＝0.906 s／0.241（透かし抜き）。
4. **判断則（短文・40 steps・fp32・DirectML・両機で RTF ≤ 0.5）は NVIDIA 側でも不合格**＝AMD 8060S 0.749（`23`）・RTX 3090 0.527（透かし抜きですら）。⒝の保留は両機の実測で確定。
5. **CUDA EP なら届く（0.291）**が、⒜の torch bf16（0.215・`27`）より遅く、「CUDA か ROCm か」の分岐を消す⒝の存在理由も消える。
6. **数値の一致**＝DirectML の最終潜在は参照（AMD DML fp32＝torch と 5e-5 以内）と **max abs 1.9e-5**（ベンダ非依存で同じ答え）。CUDA EP は **0.021**（rel_l2 1.2e-3・TF32 既定・`25` §要点 5 と同じ性質）。fp16 の DiT は両 EP とも参照との差 **5.381**（`23` §4-3・`26` と同値＝破綻の再現 4 回目）。
7. **3090 vs 8060S（DirectML）**＝DiT ループ 1.44 s vs 約 2.0 s・条件符号化 446 vs 210 ms（3090 の方が遅い＝`25` §要点 7 と同じ）・復号 90 vs 178 ms。端から端までは 1.98（透かし抜き）vs 2.82（透かし CPU 込み 2.815＝抜きなら ≈2.5）＝GPU の格差ほどには縮まらない（DiT の小バッチはカーネル起動が支配）。
8. session 分割（B=3／B=1 の 2 本）は両 EP で問題なし（`23` §4 の DML の要件と同じ構成で CUDA EP も動く）。
9. 未実施＝長文（10.92 s）の e2e・同時 Run・透かしの DFT 抜き再 export（DML で透かしを載せる唯一の道）・fp16 の原因 op。
10. 停止域＝3090 機の走行のみ・本機は読み取りだけ。

## 表 1＝端から端まで（短文 3.76 s・fp32・warm・`28`）

| EP（ORT） | 40 steps s／RTF | 10 steps s／RTF | DiT ループ（CFG／非 CFG ステップ） | 条件符号化 | 復号 | 透かし | 最終潜在の参照差 |
|---|---|---|---|---|---|---|---|
| CUDA EP（1.29.0） | **1.094／0.291**（cold 1.376／0.366） | 0.378／0.100 | 964 ms（30.5／16.4 ms） | 37.7 ms | 38.0 ms | 51.8 ms（GPU） | 0.021（TF32） |
| DirectML（1.24.4） | **1.982／0.527（透かし抜き）**（cold 2.579／0.686） | 0.906／0.241 | 1,444 ms（46.5／25.0 ms） | 445.7 ms（`_az0`） | 89.7 ms（`_dml`） | ロード不可（DFT・CPU EP も） | 1.9e-5 |
| 参考: 8060S DirectML（`23`） | 2.815／0.749（透かし CPU 331 ms 込み） | 1.036／0.276 | — | 209.5 ms | 178 ms | CPU | 5e-5 以内 |

## 主席への注意

- 報告 §6-3 の⒝段落＝「見立て」を実測に置き換え、**判断則不合格は両機で確定**と書く。§6-2 のレイテンシ欄・§0 の残る不確実性・U-5 も同様。
- DirectML の透かしは ORT 1.24.4 では載らない（CPU EP でも DFT の属性検査で落ちる）＝⒝を採るなら DFT 抜きの再 export が必須（U-5）。
