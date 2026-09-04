# SSD で回す手順（3090 機・軽量キット `kit-ssd`）

`kit-cuda` は uv のキャッシュと venv（数十万ファイル）を含むため丸ごとコピーに数時間かかる。`kit-ssd` は **ファイル約 90 個・約 7.5 GB**（モデル 3.4 GB・ONNX 3.8 GB・スクリプト）だけにした。torch・CUDA ライブラリ・Python 本体は 3090 機で uv が PyPI から取る（cu130 で約 2 GB のダウンロード・**要ネット**）。

1. `N:\irodori-native-research\kit-ssd\copy-kit-to-ssd.cmd` をダブルクリック＝`C:\irodori-kit\` に写す（数分）。
2. `C:\irodori-kit\sync-results.cmd` をダブルクリック＝`results\` を 1 分ごとに `N:\irodori-native-research\results-ssd\` へ写し続ける窓。閉じるまで動く（N:\ 側は消さない）。
3. `C:\irodori-kit\run-cu130.cmd`＝cu130 の venv 作成（ダウンロード込み）→合成 8 条件→compile→ONNX（CUDA EP・DirectML）。ONNX は `speaker_encoder` と `dacvae_decoder_fp16` を抜いてある（前回で取得済み）。
4. 3 が終わったら `C:\irodori-kit\onnx-e2e\run-onnx-e2e.cmd`（届いていれば）＝⒝の端から端まで（CUDA EP・DirectML）。
5. `run-cu126.cmd` は、3 の `results\summary.md` で bf16 短文 40 steps の warm が 1.4 s 級のままだった時だけ（同じ SSD 条件で cu126 と並べるため・cu126 は約 2.5 GB のダウンロード）。

進捗の見方＝`results-ssd\logs\` の最新ログの末尾、または `results-ssd\summary.md`（走行の最後に生成。途中経過は `logs` の最新ログと `steps.json`）。
