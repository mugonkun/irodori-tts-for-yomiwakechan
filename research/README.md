# research/ — 調査便「irodori-native-research」の証拠の写し（2026-09-04）

設営便の依頼文 §0「第一コミット＝調査の証拠の持ち込み」に従い、
`C:/Users/mugonkun/source/repos/irodori-native-research/`（git 追跡外の作業場）から**テキスト分だけ**を写した。
原本は上記パスと `N:/irodori-native-research/`（キット・結果）に残る。この写しは**読むだけの証拠**であり、
新リポの実装はここを import しない（流用するときは `build/`・`server/` 等へ写して出典を残す）。

## 写したもの
| 置き場 | 原本 | 内容 |
|---|---|---|
| `README-irodori-native-research.md` | `README.md` | 調査便の作業場の説明（稼働機の事実・前提資料の所在） |
| `BRIEF.md` | `BRIEF.md` | 調査便の依頼文の写し |
| `report/` | `report/` | 調査報告 `irodori-native-survey-2026-09-04.md`・引き継ぎ `irodori-native-handoff-2026-09-04.md`（2 檔） |
| `lab/notes/` | `lab/notes/` | 調査ノート 01〜40（39 本＋`venv-sizes.txt`＝40 檔） |
| `lab/bin/` | `lab/bin/` | 実射台本（`.py`／`.sh`／`.ps1`・67 檔） |
| `lab/kit-cuda/` | `lab/kit-cuda/` | CUDA 実射キットの**テキスト分**＝台本（`.py`／`.ps1`／`.cmd`）・README・`cases.json`・`*.inputs.json`・`ref-bench/voices/manifest.json`・`uv/LICENSE-*`（39 檔） |
| `local-additions/` | `local-additions/` | 稼働機に足した檔の写し（ROCm 用 bat・`overrides-rocm.txt`・空の `tracked-changes.patch`＝上流無改変の証拠。Radeon 版の種） |

## 写さなかったもの（原本に残す）
- `lab/onnx/`（5.9 GB・ONNX 変換物）・`lab/out/`（111 MB・実射の生データ）・`lab/tmp/`・`lab/.venv*`・`lab/dotnet/`
- `lab/kit-cuda/` の非テキスト＝`uv/uv.exe`（41.6 MB）・`wheels/*.whl`（dacvae・silentcipher）・`Irodori-TTS-8224daf.zip`・
  `ref-bench/voices/*.wav`（10 檔・12.5 MB）・`onnx-e2e/data/*.npz`（4 檔）
- `upstream/`（上流 clone）＝新リポでは submodule で pin する（Irodori-TTS `8224daf`・Irodori-TTS-Server `841fb7c`）

第三者バイナリ（wheel・exe・モデル）をリポにコミットしない裁定（依頼文 §5）に従う。
