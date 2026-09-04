# 便 C 設計書 — Radeon（ROCm）版の実射と暖機（2026-09-05・設計席 Fable）

> 正典は `decisions.md`（5・19・24・36・38〜40）。事実の出典は `research/lab/notes/40-rocm-warmup.md`・`34-rocm-environment-detection.md`・`29-gpu-designation-lab.md`・`research/local-additions/`・便 P の二次席の実測（decisions 40）。
> **期限**＝Radeon 機（gfx1151）が使えるのは 2026-09-07（月）まで。便 C はそれまでにこの機体で全段を実射する。

## 0. 範囲と受け入れ条件

**範囲**＝⑴ ROCm 変種の取得台帳 `ledger/runtime-rocm-gfx1151.json`（AMD 索引・機械取得）⑵ `assemble-runtime -Variant rocm-gfx1151` でこの機体に組み立て ⑶ wrapper の Radeon 変種の振る舞い（bf16 固定・暖機・MIOpen db の置き場・実 device の可視化）⑷ 暖機 API `POST /ywk/warmup` と `/ywk/status.warmup` ⑸ 実射で受け入れ条件「スパイク」「起動」行を埋める ⑹ `docs/radeon.md` を事実で更新。
**範囲外**＝ランチャ UI（便 D）・インストーラ（便 E）・CUDA 側の暖機（便 B で同型の罰があるか確かめてから）。

| # | 受け入れ条件（この Radeon 機で検分） | 判定 |
|---|---|---|
| C-1 | `make-ledger.ps1 -Variant rocm-gfx1151` が AMD 索引 `https://stable.repo.amd.com/rocm/whl-next/` から URL・sha256・size を機械取得し、torch `2.13.0+rocm10.0.0`・torchaudio `2.11.0.2+rocm10.0.0`・`amd-torch-device-gfx1151`・`rocm-sdk-core`・`rocm-sdk-libraries`・`rocm-sdk-device-gfx1151`・`rocm` を含む台帳を書く。他の依存は cpu 変種と同じ pin（sha256 一致） | 台帳の差分 |
| C-2 | `assemble-runtime.ps1 -Variant rocm-gfx1151` が台帳だけから組み上がり（DL ≈1.2〜1.4 GB・展開 ≈3.7 GB）、組んだ python.exe で `torch.version.hip` が非 null・`torch.cuda.is_available()` が True・device 名に `gfx1151` か `Radeon` | 台本のログ |
| C-3 | wrapper が `YWK_VARIANT=rocm-gfx1151` で起動し、精度を **bf16 に固定**（fp32 指定は起動前に exit 2 で理由 1 行）、`/ywk/status.device.actual` に実 device（`cuda:0`・name・torch.hip）が出る | `verify-runtime -Variant rocm-gfx1151` |
| C-4 | 起動→`/health` 200＋`runtime.loaded=true` が **≤ 60 s**（HF キャッシュ温・db 温）、機体初回（空 db）は実測して docs に書く | 実測 |
| C-5 | `POST /ywk/warmup` が no_ref 3 段（`seconds` 4／8／12）＋指定話者の各 1 射を**直列・低優先**で撃ち、`/ywk/status.warmup` に `state/shots_done/shots_total/elapsed` が出る。暖機中に来た本物の要求が暖機の 1 射分（≤ 10 s）以上待たされない | 実測 |
| C-6 | 暖機後、暖機済みの話者・同一形状で **1 発目 ≤ 5 s**、以後 ≤ 2 s（bf16・短文）。未暖機の話者の 1 発目の実測値を docs に書く（研究の「14.8 s」の再現可否） | `probe/rocm-warmup-probe.ps1` |
| C-7 | MIOpen の db が `%LOCALAPPDATA%\irodori-tts-ywk\miopen\`（`MIOPEN_USER_DB_PATH`・`MIOPEN_CUSTOM_CACHE_DIR`）に永続し、**プロセスを跨いで**暖機の効果が残る（再起動後の 1 発目 ≤ 5 s） | 実測（2 プロセス） |
| C-8 | 二次ボイス（`voices/presets/`・11 本）を参照にした合成が 11 本とも 200・RTF < 0.5（bf16・40 steps） | `probe/` の台本 |
| C-9 | 敵対検分 2 席（台帳・暖機）の指摘を是正済み | 完了報告 |

## 1. 事実（設計の根拠）

- 稼働機の実構成（decisions 24・読むだけ）＝Python 3.12.14・`torch==2.13.0+rocm10.0.0`・`torchaudio==2.11.0.2+rocm10.0.0`・`amd-torch-device-gfx1151==2.13.0+rocm10.0.0`・`rocm-sdk-core==10.0.0`・`rocm-sdk-libraries==10.0.0`・`rocm-sdk-device-gfx1151==10.0.0`・`rocm==10.0.0`・`rocm-bootstrap==0.2.0`・HIP 7.15。index `https://stable.repo.amd.com/rocm/whl-next/`・`--index-strategy unsafe-best-match`。
- AMD 索引（2026-09-05 に設計席が実照会）＝`torch-2.13.0+rocm10.0.0-cp312-cp312-win_amd64.whl`・`torchaudio-2.11.0.2+rocm10.0.0-cp312-...`・`amd_torch_device_gfx1151-2.13.0+rocm10.0.0-cp312-...`・`rocm_sdk_core-10.0.0-py3-none-win_amd64.whl`・`rocm_sdk_libraries-10.0.0-py3-none-win_amd64.whl`・`rocm_sdk_device_gfx1151-10.0.0-py3-none-win_amd64.whl`・`rocm_bootstrap-0.1.0-py3-none-any.whl`（venv は 0.2.0＝PyPI）。href に `#sha256=` が付く。
- **`.pth` 依存なし**（設計席が venv-rocm の site-packages を実見）＝`.pth` は `_virtualenv`・`distutils-precedence`・`protobuf-3.19.6-nspkg`・editable の 4 本のみ＝venv 由来。torch は `torch/__init__.py:242` の `os.add_dll_directory` で DLL を解決する。→ 埋め込み Python（`import site` なし）で成立する見込み。**ただし実射で確かめる**（C-2）。
- 展開後の大きさ（venv-rocm 実測）＝`_rocm_sdk_core` 2.1 GB・`_rocm_sdk_libraries` 1.1 GB・`torch/lib` 543 MB ≈ 3.7 GB。
- 暖機の機構（research 40）＝罰の鍵は出力潜在フレーム数 S_lat（40 ms 刻み）。罰が出るのは畳み込み 3 段（decode_latent・silentcipher_watermark・prepare_reference）。二層＝MIOpen db（ディスク永続・`~/.miopen`・機体初見のみ・+3〜10 s）とプロセス内（+0.18〜0.61 s）。3 段暖機（4/8/12 s・no_ref）＝6.25 s・読込 20.0 s。`seconds` 段階化は出力尺を指定秒ちょうどにする副作用があるので**暖機射にだけ使う**（本番要求には使わない）。
- 便 P 二次席の実測（decisions 40）＝私設サーバ読込 18.98 s・spawn→ready 21.1 s・no_ref 暖機 2.3 s。**未見の参照ボイス形状に当たるたびに decode_latent が跳ねる**（0.6〜1.0 s → VOICEROID2 参照の初回 14.8 s・MIOpen `IsEnoughWorkspace` 警告と同時）。146 字は 2 分割される。
- bf16 固定の根拠＝同一形状 1 発目 fp32 32.1 s 対 bf16 2.84 s（research 40 §8）。fp32 の decode は CPU より遅い（MIOpen が workspace 0 の遅い solver に落ちる）。
- 実 device の判別＝`/health` の `model_device` は設定値の echo。wrapper の `/ywk/status.device.actual` が読込後の実値（便 A で実装済み・CPU で検分済み）。ROCm の torch は device type `cuda`・`torch.version.hip` が非 null・`get_device_properties(0).name` が `AMD Radeon ...`／`gcnArchName` が `gfx1151`。

## 2. 設計

### 2-1 台帳（`build/make-ledger.ps1` の rocm 枝）
- 変種タグ `rocm-gfx1151`。母集合は cpu 変種と同じ（上流 2 本の pyproject から間引き）＋ `torch[device-gfx1151]==2.13.0+rocm10.0.0`・`torchaudio==2.11.0.2+rocm10.0.0`。解決は `uv pip compile --python-version 3.12 --python-platform windows --extra-index-url https://stable.repo.amd.com/rocm/whl-next/ --index-strategy unsafe-best-match`（uv の `--python-platform` で解決できない wheel タグがあれば `--no-build`・`--only-binary` を試し、それでも駄目なら venv-rocm の実構成（research/local-additions と decisions 24）を pin として台帳に書き、理由を残す）。
- torch 系の item に `notices`＝「AMD ROCm SDK の同梱バイナリ（rocm-sdk-core／libraries／device-gfx1151）。ライセンス表示は wheel の METADATA からは特定できない（research/lab/notes/22 §7）＝未特定」を事実だけ書く。`first-run-notices.md` §B1 と整合させ、check-licenses の copyleft／未特定の突合で名前が出ること。
- `sha256_source`＝AMD 索引の `#sha256=`。size は HEAD の Content-Length。

### 2-2 組み立て（`assemble-runtime.ps1`）
- 変種名の一般化（`-Variant rocm-gfx1151`）。`kind: wheel` の展開は cpu と同じ。`*.pth` は落とす（ROCm 由来の `.pth` は無い＝§1）。
- 組んだ後の import 検分に `torch.version.hip`・`torch.cuda.is_available()`・`torch.cuda.get_device_properties(0).gcnArchName` を足す（GPU 機でしか通らないので `-ExpectGpu` スイッチ）。

### 2-3 wrapper の Radeon 変種（`server/ywk_server.py`）
- env `YWK_VARIANT`（ランチャが載せる・既定 `cuda`）。`rocm-*` のとき：
  - 精度は **bf16 固定**＝`IRODORI_MODEL_PRECISION`／`CODEC` が `fp32` なら起動前に exit 2「Radeon 版は bf16 固定（decisions 5・36）」。`cpu` 指定は許す（fp32 連動）。
  - `MIOPEN_USER_DB_PATH`＝`<YWK_DATA_DIR>/miopen/db`・`MIOPEN_CUSTOM_CACHE_DIR`＝`<YWK_DATA_DIR>/miopen/cache` を setdefault（ディレクトリは作る）。上流・torch はこの env を MIOpen 経由で読む（research 40 §5-2 で実射済み）。
  - `HIP_VISIBLE_DEVICES` は ランチャが載せる（UUID→index 解決は便 D）。wrapper は `cuda:N` の範囲検査を CUDA と同じ経路で行う。
  - `/ywk/status.variant`＝`rocm-gfx1151`・`device.actual.hip`＝`torch.version.hip`・`gcn_arch`。
- 暖機 API（変種に依らず持つ・Radeon では既定 ON・CUDA では便 B の結果で決める）：
  - `POST /ywk/warmup {"stages":[4,8,12],"voices":["デフォルト","vv_mochiko_sexy",...],"text":"暖機です。"}` → 202 `{"id","shots_total"}`。既に走っていれば 409。
  - 実装＝バックグラウンドのスレッド 1 本が、上流の `/v1/audio/speech` と**同じ関数**（wrapper の `ywk_create_speech` を経ずに上流の合成関数を直接呼ぶ＝白名簿を通す必要が無い・ただし上流の `Semaphore(max_concurrent_synthesis=1)` と `threading.Lock` に並ぶ）で 1 射ずつ撃つ。射の順＝no_ref の `seconds` 段（4/8/12）→ 指定話者ごとに短文 1 射（`seconds` は指定しない＝自然尺・参照形状を焼くのが目的）。
  - **優先度**＝wrapper が `pending_real_requests` カウンタを持ち、本物の `/v1/audio/speech` が入口でカウンタを上げる。暖機スレッドは次の射の前に「カウンタが 0 になるまで待つ」（最大 30 s・以後は 1 射だけ撃って再確認）。これで暖機中の本物の要求は最大 1 射分しか待たない（C-5）。
  - `/ywk/status.warmup`＝`{"state":"idle|running|done|failed","id","shots_done","shots_total","elapsed_s","last_shot":{"kind":"no_ref|voice","key":"...","ms":...},"error":null}`。
  - 暖機の wav は捨てる。ログは 1 射 1 行（kind・key・S_lat 相当の秒・所要 ms）。
  - `DELETE /ywk/warmup/{id}`＝取消（次の射の前で止まる）。
- 起動時暖機（ランチャが無い間の検分用）＝env `YWK_WARMUP_ON_START=1`・`YWK_WARMUP_VOICES=デフォルト,vv_mochiko_sexy` で ready 直後に自動で `POST /ywk/warmup` 相当を走らせる（便 D はランチャから明示的に叩く形にしてもよい）。

### 2-4 実射の台本（`probe/rocm-warmup-probe.ps1`・`probe/rocm-warmup-probe.py`）
- 手順＝⑴ 空 db（`YWK_DATA_DIR` を一時ディレクトリに向ける）で起動→ready 時間→暖機（no_ref 3 段＋話者 3 名）の各射 ms→本物 1 射（暖機済み話者・短文）→未暖機話者 1 射→同一話者の 2 発目 ⑵ プロセスを落として再起動（同じ db）→ ready 時間→暖機なしで暖機済み話者 1 射（db の永続の効き） ⑶ 11 プリセットを参照に短文 1 射ずつ（RTF）。
- 出力＝`build/out/probe-log/rocm-warmup-<日時>.json`（射ごとの ms・messages・VRAM は `/ywk/status` から取れれば）と `docs/radeon.md` に写す表。
- 8088 は起こさない。私設の wrapper（18089 等）だけ。HF キャッシュは既存を `HF_HOME` に向けて読むだけ（`HF_HUB_OFFLINE=1`）。

### 2-5 docs/radeon.md
- 「gfx1151（Ryzen AI MAX+ 395／Radeon 8060S・ドライバ 32.0.31041.1004・ROCm 10.0.0・HIP 7.15）で確認済み」の事実だけ。他 gfx への言及は「AMD 索引に device wheel が並ぶが未検証」。自動判定・対応表は持たない（裁定 5）。
- 実測表（C-4〜C-8）と「未見の参照形状の 1 発目」の値、MIOpen db の置き場と初回の重さ、bf16 固定の理由。

## 3. 実装席の割り当て（Workflow・opus・同一ツリー・担当パス以外に触らない・commit しない）

| 席 | 担当 | 判定 |
|---|---|---|
| S1 台帳＋組み立て | `build/make-ledger.ps1`（rocm 枝）・`build/assemble-runtime.ps1`（-ExpectGpu）・`ledger/runtime-rocm-gfx1151.json`・`licenses/first-run-notices.md` §B の ROCm 行 | C-1・C-2 を実走 |
| S2 wrapper 暖機 | `server/ywk_server.py`・`server/ywk_params.py`（status の欄）・`tests/contract/test_warmup.py`（FakeRuntime で優先度と状態遷移）・`docs/contract.md` ⑹⑺ に warmup の欄 | テスト緑 |
| 統合＋実射 | `probe/rocm-warmup-probe.*`・`build/verify-runtime.ps1 -Variant rocm-gfx1151`・`docs/radeon.md`・`decisions.md` への実測の申し送り（設計席が記帳） | C-3〜C-8 |
| 敵対検分 2 席 | 台帳（AMD 索引と実檔の突合・sha256・未特定ライセンスの扱い）・暖機（優先度の穴・取消・例外時の状態・db 永続） | 是正票 |

停止域＝8088・7861 を起こさない／`C:/irodori-TTS-server/`・`C:/IrodoriTTS/` を書き換えない（venv-rocm は読むだけ・実行もしない＝組んだ runtime で撃つ）／upstream・research は読むだけ／GPU 実験は司令官が PC を使わない今のうちに。
