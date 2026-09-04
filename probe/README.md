# probe/ — 実機検分の台本置き場（便 A では本 README のみ）

> ここは**実機で撃つ台本**（PowerShell／Python）と、その**結果の書き方**を置く場所である。
> 便 A の時点では台本が無い。便 B（CUDA 実射）・便 C（Radeon 実射と暖機）・便 E（導入）が
> ここへ入れる。
>
> **`build/` との違い**＝`build/` は**成果物を作る**台本（不合格なら非ゼロ終了で成果物を残さない）。
> `probe/` は**事実を測る**台本で、成果物は**生データ（JSON）と数値**である。
> **`research/` との違い**＝`research/` は調査便の写しで**読むだけ・書き換えない**。
> 新しく測ったものは `probe/` に置く。

## 1. 置くもの

| 何 | 形 |
|---|---|
| 台本 | `probe/bin/<番号>_<名前>.ps1`（ASCII 限定・CRLF・PS 5.1／7 両対応・`Set-StrictMode -Version Latest`・`$ErrorActionPreference='Stop'`）／`probe/bin/<番号>_<名前>.py`（UTF-8・3.12 前提） |
| 生データ | `probe/out/<番号>_<名前>.json`（**機械が書いた値だけ**。手で写した数字を混ぜない） |
| 報告 | `probe/notes/<番号>-<名前>.md`（**檔:行と逐語**を添える。断定と推測を分ける） |

**規律**（`research/` の調査便の作法をそのまま引き継ぐ）

1. **手で写した数値を混ぜない**。数値は台本が出したものを引く。
2. **逐語は逐語のまま**（訳・要約で置き換えない）。行番号は `grep -n`／`sed -n` の実測。
3. **断定と推測を分ける**。測っていないことは「未確認」と書く。
4. **停止域を報告に書く**（何を触ったか・何を触っていないか）。
5. **嘘の成功を返さない**＝不合格は非ゼロ終了で、成果物を残さない。

## 2. 便ごとに測るもの（申し送り）

### 便 B（RTX 3090 機・CUDA 実射）

| # | 何 | 根拠となる受け入れ条件 |
|---|---|---|
| B-1 | cu130・bf16・40 steps・warm・参照なしで短文 RTF ≤ 0.25／長文 ≤ 0.10 | `docs/acceptance.md` 速度 1 |
| B-2 | 参照 10〜30 s ありで RTF ≤ 0.35 | 同 速度 2 |
| B-3 | 起動後 1 発目 ≤ 2.0 s（コールド） | 同 コールド |
| B-4 | VRAM bf16 ≤ 3.2 GiB・kill 後 10 s で idle へ | 同 VRAM |
| B-5 | **cu126 が古いドライバで実際に動くか（U-14）**＝これが取れるまで「cu126＝古いドライバ向け」と謳わない | 同 ドライバ・§3 の 2 |
| B-6 | **CUDA wheel を入れた NVIDIA 無し機で CPU 合成が 200 で返るか（W-1）** | 同 §3 の 6 |
| B-7 | **`torch/lib/zlibwapi.dll` の出所（A14）**・`nvperf_host.dll`・`nvrtc*.alt.dll` を削れるか | `licenses/first-run-notices.md` A14・A15 |

**キットの再利用**＝`N:\irodori-native-research\kit-ssd\`（全段 6.2 分）・`kit-ssd\ref-bench\`
（参照ボイス 5 話者）・`gpu-props.cmd`・聴取セット `listening\`（9 対）。
結果の写し＝`N:\…\results-ssd\`（`decisions.md` 21＝「好きにしてよい」）。

**RTX 3090 機の停止域**＝**D:・E:・F: ドライブは触らない**。Windows ごと壊してよいが、
**ドライバの入れ替え（U-14）は司令官の明示の一言を得てから**（`decisions.md` 20・22）。

### 便 C（Radeon 機・実射と暖機）

| # | 何 | 根拠 |
|---|---|---|
| C-1 | bf16 固定で**同一形状・db 温の 1 発目 ≤ 5 s** | `docs/acceptance.md` スパイク（改訂） |
| C-2 | 暖機（段 3 射＋自然尺 1 射）の実所要と VRAM ピーク | `docs/radeon.md` §2-3 |
| C-3 | **8088 経由（HTTP・チャンク分割・`empty_cache`）で 3 発目の再発が起きるか** | 同 §5 の 6 |
| C-4 | **MIOpen の db（`gfx1151_20.ukdb`）を他機に同梱して効くか** | 同 §5 の 3 |
| C-5 | Radeon 変種の取得台帳（`ledger/runtime-rocm-gfx1151.json`）の確定 | 同 §6 |

**Radeon 機が使えるのは 2026-09-07（月）まで**（`decisions.md` 19）。C-1〜C-5 と便 P
（プリセット話者の一次 wav 生成）はこの枠で済ませる。

### 便 E（導入）

`installer/README.md` §4 の E-1〜E-4。

## 3. 停止域（全便共通）

- `upstream/` は読むだけ。**`__pycache__` を書かない**＝Python 実行時は必ず
  `PYTHONDONTWRITEBYTECODE=1`。検分の最後に
  `git -C upstream/Irodori-TTS status --porcelain` と
  `git -C upstream/Irodori-TTS-Server status --porcelain` が**空**であることを報告に書く。
- `C:/IrodoriTTS/`・`C:/irodori-TTS-server/`・`yomiwakechan2` に書かない（読むだけ）。
- 第三者バイナリ（wheel・exe・dll・モデル）をリポ内（`build/out/` と `.venv-dev/` 以外）に置かない。
  `probe/out/` に入れてよいのは**テキストの生データ**だけ。
- 外部へ push しない（GitHub private・HF／PyPI へアップロードしない）。
- **ライセンスの結論を推測で断定しない**。
