# 便 B 設計書 — CUDA 版の実射（RTX 3090 機・遠隔）（2026-09-05・設計席 Fable）

> 正典は `decisions.md`（4・8・19〜22・34・41〜49）。事実の出典は `research/lab/notes/21・24・25・27・33`（kit-cuda の実測）・`research/lab/kit-cuda/`・`ledger/runtime-cu130.json`・`ledger/runtime-cu126.json`。
> 機体＝RTX 3090（`12900K-NEW`・ドライバ 591.86・Windows 10 Pro 26200・pwsh 5.1＋7・git・Python のみ・個人情報なし・**Windows ごと壊してよい・D:/E:/F: は触らない**）。操作＝遠隔席 `12900k-new-enchanted-metcalfe`（Opus 5・Remote Control）に SendMessage で台本を渡す。受け渡し＝`N:/temp_for_claudecode_agents/irodori-ywk/`（両機から同じ N:）。**NAS 直走行は禁止**（research 21＝`import torch` が 311〜563 s）＝必ず C: に写してから走る。

## 0. 範囲と受け入れ条件

**範囲**＝⑴ 便 A の配置を RTX 機に写し、cu130 変種を台帳だけから組む ⑵ モデルを初回取得（`ywk_fetch_models.py`・refs/main）⑶ wrapper を GPU（bf16）で起動し、受け入れ条件「速度」「コールド」「VRAM」行を実測 ⑷ **U-8**＝vc_redist 欠落機の実挙動（この機体は素なので再現できる可能性が高い） ⑸ **U-14**＝2023 年秋のドライバ（R537〜R545）で cu126 が動くか（**ドライバ入れ替えは司令官の明示の一言を得てから**＝停止域） ⑹ cu126 変種の組み立てと速度の同値確認 ⑺ CUDA での「未見の参照形状の罰」の有無（便 C の暖機を CUDA でも既定 ON にするかの根拠）⑻ 契約テストが RTX 機の clone でも緑（`.gitattributes` の CRLF 対策の実証）。
**範囲外**＝ランチャ・インストーラ（便 D・E）。

| # | 受け入れ条件 | 判定 |
|---|---|---|
| B-1 | `assemble-runtime.ps1 -Variant cu130` が RTX 機で台帳だけから組み上がる（DL ≈2.1 GB・sha256 全一致・`fallback_url` の経路も 1 度は通す） | 台本ログ |
| B-2 | `ywk_fetch_models.py` が 3 リポ 22 檔を取得・検証し `refs/main` を書く。以後 `HF_HUB_OFFLINE=1` で起動できる | ログ・`--check-only` |
| B-3 | **U-8**＝vc_redist 未導入の状態で組んだ runtime の `python -c "import torch"` がどう落ちるか（逐語）→ `vc_redist.json` の直リンクから取得・sha256 検証・`/install /passive /norestart` で導入 → import が通る | 逐語 |
| B-4 | wrapper（bf16・cuda:0）で `/health`・`/params`・`/ywk/status`（`device.actual` に `NVIDIA GeForce RTX 3090`・uuid・pci_bus_id）→ 実合成（短文・長文・40 steps・10 steps・参照あり 10 s／30 s）＝受け入れ条件「速度」行（短文 RTF ≤ 0.25・長文 ≤ 0.10・参照あり ≤ 0.35・10 steps 短文 ≤ 0.10）・「コールド」行（1 発目 ≤ 2.0 s）・「VRAM」行（bf16 ≤ 3.2 GiB・kill 後 10 s で idle） | `probe/cuda-bench.ps1` |
| B-5 | 未見の参照形状の 1 発目（11 プリセットを順に）が CUDA でどう振る舞うか（研究 33＝3090 は cold 1.1〜2.1 倍のみ・参照長の罰なし＝再確認） | 同上 |
| B-6 | cu126 変種の組み立て（DL ≈2.6 GB）と同じベンチ＝cu130 と ±5 % | 同上 |
| B-7 | **U-14**＝司令官の一言の後で、2023 年秋のドライバ（R537 or R545・cu126 の minor version compatibility の下限は CUDA 12.x で ≥527.41「限定機能」・GA 最小 560.76）に入れ替え、cu126 の runtime で ⑴ `import torch` ⑵ `is_available()` ⑶ 実合成 1 射 の可否を逐語で。可なら「cu126＝560.76 以上で確認済み・527.41〜560 は動く可能性あり」の謳い方を「R5xx.xx で実合成を確認」に強められる。不可ならその逐語を docs に。終わったら 591.86 に戻す（必須ではない＝壊してよい機体） | 逐語 |
| B-8 | `git clone`（N: の bare か zip 経由）した木で `build/run-tests.ps1` が緑（LICENSE・patch の CRLF 対策の実証） | ログ |
| B-9 | 結果を `N:/temp_for_claudecode_agents/irodori-ywk/rtx/`（JSON・ログ・wav 数本）に置き、設計席が `docs/acceptance.md`・`docs/cuda.md`・`decisions.md` に写す | 受け渡し |

## 1. RTX 機への持ち込み（設計席が用意する）

- `N:/temp_for_claudecode_agents/irodori-ywk/repo.bundle`＝`git bundle create`（main・submodule は別に `upstream/` の 2 リポも bundle か、GitHub から clone）。RTX 機に gh の認証が無い前提。
- `N:/temp_for_claudecode_agents/irodori-ywk/build-cache/`＝この機体の `build/cache/`（cpu 変種の wheel・embed zip・vc_redist.exe）を写す＝共通 wheel の再取得を省く（sha256 検証は写し先でも走る）。torch cu130/cu126 の wheel は RTX 機が直接取る（各 1.9／2.6 GB）。
- uv は不要（台帳だけから組む）。契約テスト（B-8）の `.venv-dev` だけ uv が要る＝`N:/irodori-native-research/kit-ssd/uv/uv.exe`（0.12.7）を C: に写して `UV_PYTHON_INSTALL_DIR` を C: に向ける（`build/dev-venv.ps1` は uv 0.12.7 以外で止まる）。
- 台本＝`probe/rtx-remote-runbook.md`（遠隔席に渡す手順・逐語で。1 手順 1 コマンド・結果の写し先・停止域）。

## 2. 遠隔席への台本の骨（`probe/rtx-remote-runbook.md` の内容）

0. 前提＝`C:/ywk/` を作業根にする（D/E/F は触らない）。`N:` から `repo.bundle`・`build-cache/`・`uv.exe` を `C:/ywk/` へ robocopy。`git clone repo.bundle C:/ywk/repo` → `git submodule update --init`（GitHub から取れなければ N: の upstream bundle）。
1. `powershell -File build/check-tree.ps1` → 緑。
2. `powershell -File build/assemble-runtime.ps1 -Variant cu130 -CacheRoot C:/ywk/build-cache` → B-1。
3. **U-8**＝vc_redist を入れる前に `C:/ywk/repo/build/out/runtime-cu130/python.exe -c "import torch"` → 逐語を記録（`msvcp140.dll` の欠落で落ちるはず）。落ちなければ「既に msvcp140 がある」事実を記録（`where.exe msvcp140.dll`・`C:\Windows\System32\msvcp140.dll` の有無と版）。
4. `ledger/vc_redist.json` の `url` から取得→sha256 検証→`VC_redist.x64.exe /install /passive /norestart`→再度 `import torch` → B-3。
5. `powershell -File build/assemble-app.ps1` → `python -m ywk_fetch_models --dest C:/ywk/models`（進捗 JSON 行・所要）→ `--check-only` → B-2。
6. `powershell -File build/verify-runtime.ps1 -Variant cu130 -WithModel -Device cuda:0 -Precision bf16 -HfHome C:/ywk/models`（verify-runtime に -Device／-Precision／-HfHome の引数が無ければ設計席が足す）→ B-4 の前半。
7. `probe/cuda-bench.ps1`（設計席が用意・kit-cuda の `cases.json` の凍結文面と `bench_infer.py` の RTF 定義を写す＝`total_to_decode ÷ audio_seconds`）→ 短文／長文 × 40／10 steps × 参照なし／10 s／30 s（`voices/presets/` の二次 wav を参照に使う）→ cold 1 発目・warm 3 発の中央値・VRAM（`nvidia-smi --query-gpu=memory.used`）→ B-4・B-5。
8. `assemble-runtime -Variant cu126` → 同ベンチ → B-6。
9. `build/dev-venv.ps1`（uv.exe を PATH に）→ `build/run-tests.ps1` → B-8。
10. 結果を `N:/temp_for_claudecode_agents/irodori-ywk/rtx/` へ（JSON・ログ・wav 6 本まで）。
11. **司令官の一言があれば** U-14（B-7）。無ければ台本はここで止まる。

## 3. 設計席が便 B の前に足すもの（便 A（2）の後）

- `build/verify-runtime.ps1` に `-Device`／`-Precision`／`-HfHome`／`-Variant cu130|cu126` の引数（cpu 決め打ちを外す）。
- `probe/cuda-bench.ps1`＋`probe/bench_cases.json`（kit-cuda の凍結文面を写す）。
- `probe/rtx-remote-runbook.md`。
- `N:` への持ち込み（bundle・build-cache・uv）。

## 4. 停止域（便 B 固有）

- RTX 機の D:/E:/F: に触らない。C: は壊してよい。
- ドライバの入れ替え（U-14）は司令官の明示の一言を得てから。
- 8088 は RTX 機に無い（関係なし）。外部公開なし。HF から取得はするが push しない。
