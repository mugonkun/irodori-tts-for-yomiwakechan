# ONNX「端から端まで」測定キット（RTX 3090 機で司令官が実行する）— 手順書

このフォルダは、既存の CUDA 検証キット（`run-cuda-kit.ps1`）の**追加の 1 段**です。
測るのは⒝案（非 Python 化＝ONNX Runtime）の**合成 1 回まるごと**＝
条件符号化 → 長さ予測 → DiT ループ（40／10 ステップ）→ DACVAE 復号 → 透かし の合計時間と RTF。
これまで 3090 機で採れているのは**グラフ 1 本ずつの時間**だけで、**合計は誰も測っていません**（25 §要点 8 は見立て）。
その 1 個の空欄を埋めるためのものです。所要 **15〜40 分**・**ダウンロード約 1.5 GB**・**空き容量 6 GB**。

---

## 0. 先に 1 つだけ（いちばん大事）

**キットのフォルダを、NAS（`N:\` など）ではなく PC のローカル SSD に置いてから走らせてください。**
前回（25 §要点 3）は `N:\` 上で直接走らせたため、モデル読み込みだけで 5〜9 分かかり、
全段の数字が遅い側に汚れました。ONNX のグラフは合計 4.4 GB あり、この測定はそれを**何度も読み直します**。

- 良い例＝`C:\irodori-kit\`（コピーしてから実行）
- 悪い例＝`N:\irodori-kit\`（そのまま実行）

終わったら `results\` の 2 つの JSON だけ返送してください。フォルダごと消せば跡は残りません。

## 1. 事前に要るもの

| 要る | 説明 |
|---|---|
| CUDA 検証キット一式 | このフォルダ（`onnx-e2e`）の**親フォルダ**。`uv\uv.exe` と `onnx\` を使います |
| `onnx\` の中身 | 前回 3090 機に置いた ONNX 一式（`dit_step.onnx` ほか・約 4.4 GB）。**新しく置き直す必要はありません** |
| 空き容量 | **約 6 GB**（venv 2 本＋uv キャッシュ。全部キットの中） |
| 回線 | 初回のみ約 1.5 GB（CUDA EP の実行時 DLL 1.3 GB＋DirectML 200 MB） |
| 管理者権限 | **不要** |

`onnx\` に前回の `*_az0.onnx` / `*_dml.onnx`（DirectML 用の手術版）が入っていればそれも自動で使います。
無ければ無手術版で測り、落ちた事実を JSON に残します（どちらでも走ります）。

## 2. 実行する（1 行 1 操作）

1. キットのフォルダごと、PC のローカル SSD にコピーする（例＝`C:\irodori-kit\`）。
2. `C:\irodori-kit\onnx-e2e\run-onnx-e2e.cmd` を**ダブルクリック**する。
3. 黒い窓が開く。`installing onnxruntime-gpu[cuda,cudnn]` と出たらしばらく待つ（3〜10 分・約 1.3 GB）。
4. `---- CUDA EP run ----` の下に段ごとの行が流れる（2〜5 分）。最後に `steps40_warm ... RTF ...` が 4 行出る。
5. 続けて `---- DirectML EP run ----` が同じように流れる（venv 作成 1 分＋実走 2〜5 分）。
6. `Finished.` と出たら何かキーを押して窓を閉じる。
7. `C:\irodori-kit\results\onnx_e2e_cuda.json` と `onnx_e2e_dml.json` の **2 檔を返送**する。

**2 回目以降**＝venv は作り直さないので、同じ `.cmd` をもう一度押すだけで再測定できます（各 5 分）。
結果 JSON は**上書き**されるので、比べたいときは 1 回目の `results` を先に zip で退避してください。

## 3. 所要時間の目安

| 段 | 目安 | 備考 |
|---|---|---|
| `.venv-e2e-cuda` 作成＋onnxruntime-gpu 導入 | 3〜10 分 | 初回のみ。約 1.3 GB。`[cuda,cudnn]` が拒まれたら nvidia の wheel を個別に、それも駄目なら素の onnxruntime-gpu と**自動で 3 段に退きます** |
| CUDA EP の実走 | 2〜5 分 | DiT ループを 40 steps × 2 回・10 steps × 2 回・fp16 1 回 |
| `.venv-e2e-dml` 作成＋onnxruntime-directml 導入 | 1〜2 分 | 初回のみ。約 200 MB |
| DirectML EP の実走 | 2〜5 分 | 3090 でも DirectML は動きます（25 §要点 7） |

## 4. 結果 JSON の読み方（返送前にここだけ見てもらえると助かります）

いちばん見たいのは 1 行だけです。

```
totals.steps40_warm.rtf     <- これが 1.00 未満なら「実時間より速い」。0.5 未満なら文句なし
```

窓の最後に出る 4 行がそれです（`steps40_warm / steps40_cold / steps10_warm / steps10_cold`）。

| JSON の欄 | 何が分かるか | 期待値 |
|---|---|---|
| `totals.steps40_warm.sec` / `.rtf` | **⒝の実力**（短文 3.76 秒を常駐ウォームで合成した時間） | CUDA EP の見立ては 1.05 s／RTF 0.28（25 §要点 8）。AMD の DirectML 実測は 2.815 s／0.749（23 §6） |
| `totals.steps40_warm.breakdown_ms` | 段ごとの内訳（条件符号化／長さ／DiT／復号／透かし） | DiT が全体の 8 割を占めるはず |
| `totals.steps40_cold` | 初回合成（形状チューニング込み） | warm との差が「起動直後に空射ちが要るか」の判断材料 |
| `totals.*.missing_stages` | 測れなかった段の名前 | 空なら全段そろっている |
| `stages.dit_loop.s40.vs_reference_latent.max_abs` | **数値が壊れていないかの検算**。AMD 機の fp32 結果（torch と 5e-5 以内）との差 | 1e-4 以下なら健全。CUDA EP は TF32 の既定で 1e-2 級になる見込み（25 §要点 5）＝**壊れではない** |
| `stages.dit_loop.s40.session_b1` / `session_b3` | バッチ形状ごとに session を分けた 2 本の読み込み時間 | 常駐サーバなら起動時の 1 回だけの費用 |
| `stages.dit_fp16.vs_reference_latent.max_abs` | **fp16 が壊れる件の再確認** | 5 前後なら「GPU EP では fp16 は使えない」が確定（23 §4-4・25 §要点 6）。0.01 級なら**直っている＝大ニュース** |
| `stages.watermark.ep_used` | 透かしが GPU に載ったか | `cuda` なら載った（ORT 1.29 なら載る＝25 §要点 4）。`cpu (fallback)` なら GPU では載らない |
| `stages.*.cpu_ref.max_abs_diff_vs_cpu_ep` | 各段の GPU 出力と CPU 出力の差 | 段ごとの数値の健全性 |
| `failures[]` | 落ちた段と**逐語のエラー** | 空が理想。空でなくても測定は続いています |
| `nvidia_smi` / `available_providers` / `ort_get_device` | 機体と EP の素性 | `CUDAExecutionProvider` が `available_providers` に無ければ CPU で走っています |

## 5. うまくいかないとき

- **窓が一瞬で閉じる**＝`.cmd` を右クリック →「編集」ではなく、そのままダブルクリックしてください。それでも閉じるなら、
  コマンドプロンプトを開いて `C:\irodori-kit\onnx-e2e\run-onnx-e2e.cmd` と打って実行すると、最後の行が読めます。
- **`ERROR: ...\onnx\dit_step.onnx not found`**＝キットの `onnx\` フォルダが空です。前回の ONNX 一式を戻してください。
- **`SKIPPED the CUDA EP run`**＝onnxruntime-gpu が 3 段とも入らなかった（回線かプロキシ）。DirectML 側だけでも返送してください。
- **途中の段で赤い `FAIL` が出た**＝その段は飛ばして次に進みます。**止めずに最後まで走らせてください**（逐語が JSON に残るのが目的です）。
- **やり直したい**＝`.venv-e2e-cuda` と `.venv-e2e-dml` のフォルダを消してから `.cmd` を押すと、導入からやり直します。

## 6. このキットが触るもの・触らないもの

| 触る | 触らない |
|---|---|
| キットの中だけ（`.venv-e2e-cuda\`・`.venv-e2e-dml\`・`uv-cache\`・`python\`・`results\`） | ふだんお使いの Python・環境変数・レジストリ・システム |
| `results\onnx_e2e_cuda.json`・`results\onnx_e2e_dml.json`（上書き） | `onnx\` の中身（**読むだけ**）。ネットへの送信は PyPI からの取得のみ |

アンインストール＝**フォルダごと削除**するだけです。

## 7. 中で何をしているか（読まなくてよい）

`e2e_onnx.py` は torch を使わず、numpy と onnxruntime だけで合成 1 回を再現します。
条件（本文・caption・初期ノイズ）は `data\cond_real.npz` / `data\x0.npz` に固めてあり、
**AMD 機・CPU・NVIDIA 機のどれで走らせても同じ入力**になります（比較が成立する理由）。
DiT ループは t スケジュール線形・CFG independent（text 3.0／caption 3.0／speaker 0.0）・`cfg_min_t 0.5`・Euler 更新で、
CFG 区間（バッチ 3）と非 CFG 区間（バッチ 1）で **session を 2 本に分けます**（1 本だと DirectML で後半が 3.6 倍遅くなるため・23 §4-2）。
透かしの入力だけは実合成より 7 % 短い静的な形（167,936 サンプル）で測っています（前回までの測定と揃えるため）。
