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

**番号は設計書 `docs/design/ben-b-rtx-remote.md` §0 の記号に揃える**（台本 `probe/rtx-remote-runbook.md`
の見出しも同じ B-1〜B-9）。以前ここに置いていた独自番号（B-1〜B-7）は設計書の記号と衝突していたので捨てた。

| # | 何 | 根拠となる受け入れ条件 |
|---|---|---|
| B-1 | `assemble-runtime -Variant cu130` が RTX 機で台帳だけから組み上がる（`fallback_url` の sha256 も 1 度突き合わせる） | 設計書 §0 |
| B-2 | `ywk_fetch_models` が 3 リポ 22 檔を取得・検証し `refs/main` を書く | `decisions.md` 49 |
| B-3 | **U-8**＝vc_redist 未導入で `import torch` がどう落ちるか（逐語）→ 台帳の直リンクで導入 → 通る | 設計書 §0 |
| B-4 | cu130・bf16 の実射＝短文 40 steps 参照なし RTF ≤ 0.25／長文 40 steps 参照なし ≤ 0.10／**40 steps の参照あり（ref10・ref30）≤ 0.35**／短文 10 steps 参照なし ≤ 0.10／**短文 10 steps の参照あり（ref10・ref30）≤ 0.15**・コールド 1 発目 ≤ 2.0 s・VRAM bf16 ≤ 3.2 GiB・kill 後 10 s で idle・`device` の `uuid` と `pci_bus_id` が非 null | `docs/acceptance.md` 速度 1〜3・コールド・VRAM（予算の全 12 行は `probe/bench_cases.json` の `budgets.rtf`） |
| B-5 | 未見の参照形状の 1 発目（11 プリセット順撃ち）が CUDA でどう振る舞うか＝暖機を CUDA でも既定 ON にするかの根拠 | `research/lab/notes/33` §5・`decisions.md` 40 |
| B-6 | cu126 変種を組んで同じベンチ＝cu130 と ±5 % | 設計書 §0 |
| B-7 | **U-14**＝2023 年秋のドライバ（**537.58** 第一候補・545.84 代案）に降格して cu126 が動くか（`import torch`・`is_available()`／`device_count`・`nvidia-smi`・実合成 1 射）＋cu130 の落ち方を 1 回記録。**台本では独立した最終ブロック＝§3**（`decisions.md` 56） | `docs/acceptance.md` ドライバ行 |
| B-8 | clone した木で `build/run-tests.ps1` が緑（`.gitattributes` の CRLF 対策の実証）＝**判定は `EXIT=0` と pytest が印字した行の逐語。本数は台本に書かない**（bundle の HEAD の木で決まる） | 設計書 §0 |
| B-9 | 結果を `N:\temp_for_claudecode_agents\irodori-ywk\rtx\` へ・`SUMMARY.md`・停止域の証明 | `decisions.md` 21 |

**合否の読み方**＝`probe/cuda-bench.ps1` の走行は **`EXIT=0` かつ `status.txt` の `CHECKS=OK`** で合格。
終了コードが表すのは「撃った全射が 200 で wav を返した」1 点だけで、`GET /health`・`/params`・`/ywk/status` の
可否は `checks[]` にしか乗らない（`CHECKS=` の行はそれを `status.txt` に出したもの）。
`budget_checks` の不合格は**測定であって不合格ではない**（`rtf_http` は kit の `rtf_synth` より必ず大きい）。

**任意（時間が余れば・台本 §2 の末尾）**

| # | 何 | 根拠 |
|---|---|---|
| B-10 | **CUDA wheel を入れた NVIDIA 無し機で CPU 合成が 200 で返るか（W-1）**＝RTX 機では GPU を隠して代用する | `docs/acceptance.md` §3 の 6 |
| B-11 | **`torch/lib/zlibwapi.dll` の出所（A14）**・`nvperf_host.dll`・`nvrtc*.alt.dll` を削れるか | `licenses/first-run-notices.md` A14・A15 |

**キットの再利用**＝`N:\irodori-native-research\kit-ssd\`（全段 6.2 分）・`kit-ssd\ref-bench\`
（参照ボイス 5 話者）・`gpu-props.cmd`・聴取セット `listening\`（9 対）。
結果の写し＝`N:\…\results-ssd\`（`decisions.md` 21＝「好きにしてよい」）。

**RTX 3090 機の停止域**＝**D:・E:・F: ドライブは触らない**。Windows ごと壊してよい。

**ドライバの入れ替え（U-14・台本 §3）の関門は 2 つ**（`decisions.md` 50 → **56** → **70・71・72**）＝

- **関門⑵＝司令官の承諾**が**その遠隔席のセッションの中に在る**こと（56＝セッション間で許可を持ち回らない）。
  **`decisions.md` 72 で司令官が遠隔席の会話に先に書いたので、これは充足済み**（恒久降格・戻し不要・
  `RunOnce`＋`claude -c` の自動再起動可）。ただし**本席の申し送りを承諾の代わりにしない**（71）。
- **関門⑴＝チェックポイントと設計席の確認返信**＝**B-1〜B-6・B-8・B-9 を終えて結果を N: に写し**、
  `N:\…\irodori-ywk\rtx\SUMMARY.md` に **`入れ替え前の測定完了`** と記し、
  **設計席へ返信して確認の返信を受け取る**。

**両方揃うまで §3 に着手しない。**降格後に測るのは **cu126 のみ**、**cu130 は落ち方を 1 回記録するだけ**（56）。
**591.86 への復旧は不要＝恒久降格**（70）。**再起動**は、司令官がその会話で明示していれば
`decisions.md` 66 の仕込み（`RunOnce`＋`claude -c`）で席が自分で撃ってよいが、**本席の指示だけでは撃たない**（71）。
`docs/acceptance.md` の数値は遠隔席では書き換えない＝設計席に申し送る。

### 便 C（Radeon 機・実射と暖機）

| # | 何 | 根拠 |
|---|---|---|
| C-1 | bf16 固定で**同一形状・db 温の 1 発目 ≤ 5 s** | `docs/acceptance.md` スパイク（改訂） |
| C-2 | 暖機（段 3 射＋自然尺 1 射）の実所要と VRAM ピーク | `docs/radeon.md` §2-3 |
| C-3 | **8088 経由（HTTP・チャンク分割・`empty_cache`）で 3 発目の再発が起きるか** | 同 §5 の 6 |
| C-4 | **MIOpen の db（`gfx1151_20.ukdb`）を他機に同梱して効くか** | 同 §5 の 3 |
| C-5 | Radeon 変種の取得台帳（`ledger/runtime-rocm-gfx1151.json`）の確定 | 同 §6 |

**便 C の台本と結果（2026-09-05・実走済み）**＝台本 `probe/rocm-warmup-probe.ps1`＋`probe/rocm-warmup-probe.py`、
なま値 `build/out/probe-log/rocm-warmup-<日時>.json`（＋同ディレクトリの `server-<日時>-{cold,warm}.err.log` と
GPU メモリの標本 `gpu-<日時>-*.csv`）、読み方は `docs/radeon.md` §7。
**C-1（同一形状・db 温で 1 発目 ≤ 5 s）＝満たす**（4.02 s／暖機ありなら 1.49 s）・**C-2 は §7-2／§7-7**・
**C-3（HTTP 経路で 3 発目の再発）＝出ない**・**C-4（db の他機同梱）＝未検証のまま**・**C-5（台帳）＝確定**。
**新しく判った未達**＝11 プリセットの RTF（`docs/acceptance.md` §3 の 9）。
生データは `build/out/` に置いた（`probe/out/` は作っていない）＝檔が大きく、ビルド出力と同じ寿命で捨てられる場所に置く方が筋だと判断した。

**Radeon 機が使えるのは 2026-09-07（月）まで**（`decisions.md` 19）。C-1〜C-5 と便 P
（プリセット話者の一次 wav 生成）はこの枠で済ませる。

### 便 D（ランチャ・この機体で実射済み）

台本は `probe/d-launch-probe.ps1`（＋共通 `probe/common.ps1`）＝31 段・`-Variant`／`-Port` を引数で取る。
**便 E（インストーラの UIA 検分）の前提として、便 D の実機で判った 2 つをここに残す**（設計書 §13-3・§14-3）＝

- **UI Automation は自プロセスの持ち窓と共通檔窓を列挙しないことがある**＝`RootElement.FindAll(Children, ProcessId)` は自プロセスの modal 窓（`FirstRunWizard`）も `OpenFileDialog`（`#32770`）も 1 度も返さないのに、Win32 の `EnumWindows` は同じ瞬間に返す（実測）。**`EnumWindows`＋`AutomationElement.FromHandle` を併用する**（`probe/common.ps1` の `Get-ProcessWindowHandles`）。閉じられない窓で走行が止まるので、窓を開かせない路（貼り付け）も併せて用意すること。
- **PS 5.1 の `Get-Content` は `-Encoding UTF8` が必須**＝BOM 無しの `voices.json`／`settings.json` を機体の ANSI 頁で読むため、日本語の突合が **5.1 でだけ**落ちる（7 では通る）。檔頭の「PS 5.1／7 両対応」を守るなら読みは全部 `-Encoding UTF8`。

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
