# irodori-native-research — 調査便の作業場（2026-09-04 設営）

**着任＝まず `BRIEF.md`（依頼文の写し・正本は yomiwakechan2/docs/irodori-native-research-brief-2026-09-04.md）を読む。**

## 中身
- `upstream/Irodori-TTS/`＝gradio 本体の clone。**稼働機と同じ commit `8224daf`（v3-4-g8224daf・"Update default model to v4.1-Small"・上流 main の最新と同一）**に checkout 済み。
- `upstream/Irodori-TTS-Server/`＝OpenAI 互換サーバ（8088）の clone。**稼働機と同じ commit `841fb7c`（"Merge pull request #12 … mps-cache-release"・上流の最新と同一）**に checkout 済み。
- `local-additions/`＝稼働機（`C:\IrodoriTTS\Irodori-TTS`・`C:\irodori-TTS-server\Irodori-TTS-Server`）に**こちらが足した檔の写し**（git 追跡外の bat と `overrides-rocm.txt`）。追跡檔への改変は **0**（`tracked-changes.patch` は空）＝上流との差はこの追加檔だけ。
- `report/`＝成果物の置き場（`irodori-native-survey-2026-09-XX.md`）。
- `lab/`＝実験用（別 venv・ONNX 変換の試し等）。**稼働機の venv・設定・clone は触らない**（BRIEF §5 停止域）。

## 稼働機の事実（読むだけ）
- 本体＝`C:\IrodoriTTS\Irodori-TTS`（.venv・7861＝gradio_app_voicedesign.py）。
- サーバ＝`C:\irodori-TTS-server\Irodori-TTS-Server`（.venv-rocm・8088＝`python -m irodori_openai_tts --host 0.0.0.0 --port 8088`）。設営時点で 8088 は**停止中**。
- 機体＝AMD Ryzen AI MAX+ 395／Radeon 8060S（iGPU・ROCm）。ROCm 化の手順＝yomiwakechan2/probe/irodori-tts-rocm-setup-guide.md。
- モデル＝HF `Aratako/Irodori-TTS-v4-Small`（8088）／`v4.1-Small`（7861 既定）・各 2.86GB（HF キャッシュに並存）。

## yomiwakechan2 側の前提資料（絶対パス・読むだけ）
`C:\Users\mugonkun\source\repos\yomiwakechan2\`＝probe/irodori-tts-probe-report.md・probe/irodori-tts-licenses.md・probe/irodori-tts-openapi.json・probe/irodori-tts-params-raw.txt・
docs/irodori-tts-survey.md・docs/irodori-tts-requests.md・docs/engine-adapters-overview-map.md（irodori 節）・decisions.md（docs/canon-map.md で索引）。
