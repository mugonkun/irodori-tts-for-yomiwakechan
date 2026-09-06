# 便 A 設計書 — 骨格＋ビルド（2026-09-04・設計席 Fable）

> 正典は `decisions.md`。本檔は便 A の設計と実装席への指示。事実の出典は `research/`（調査便の写し・読むだけ）。
> 用語＝本体＝yomiwakechan2／配布版＝このリポの成果物／上流＝`upstream/`（submodule・無改変）。

## 0. 便 A の範囲と受け入れ条件

**範囲**＝§3 の器・`patches/`・取得台帳（`ledger/`）・ビルド台本（`build/`）・wrapper（`server/ywk_server.py`＝`/params`・`/ywk/status`・白名簿・エラー整形・話者一覧の覆い）・契約文書（`docs/contract.md`）・受け入れ条件（`docs/acceptance.md`）・`licenses/`・契約テスト（`tests/`）・README。
**範囲外**＝ランチャ WPF（便 D）・インストーラ（便 E）・CUDA 実射（便 B）・Radeon 実射と暖機（便 C）・プリセット話者の生成（便 P）・本体アダプタ（便 F）。

**受け入れ条件（便 A・この Radeon 機で検分）**
| # | 条件 | 判定方法 |
|---|---|---|
| A-1 | `build/assemble-runtime.ps1 -Variant cpu` が **台帳だけから**（git・pip・uv を使わず）埋め込み Python＋site-packages を `build/out/runtime-cpu/` に組み立て、sha256 不一致は失敗で止まる | 台本の終了コードとログ |
| A-2 | `build/assemble-app.ps1` が上流 2 本を `build/out/app/server/upstream/` に写し `patches/` を当て（submodule は無改変のまま）、`git -C upstream/... status` が clean | `git submodule status`・`git status` |
| A-3 | 組み立てた runtime で `python.exe -m ywk_server` を起動し、モデル未読込で `GET /health` 200・`GET /params` 200（≤100 ms）・`GET /ywk/status` 200 | `build/verify-runtime.ps1` |
| A-4 | 既存 HF キャッシュ（`%USERPROFILE%\.cache\huggingface`・読むだけ）を `HF_HOME` に向け、CPU fp32・4 steps で `POST /v1/audio/speech` が wav を返す（依存の間引きが正しい証拠） | 同上・wav の RIFF 検査 |
| A-5 | 契約テスト（`tests/contract/`）が全緑＝未知欄 400・範囲外 400・`voice`＋`no_ref` 同時 400・エラー body に絶対パス 0 件・`/params` の **`exposed_to_ywk:true` の欄**に `default: null` 0 件（§4-2 の規則）・「デフォルト」常在・`none` 非露出・日本語話者名 200 | `build/run-tests.ps1` |
| A-6 | `licenses/` に Irodori 系 MIT 3 本の全文＋Ethical Restrictions＋自作分＋取得物の注意書き索引がある | 目視・`build/check-licenses.ps1` |
| A-7 | 第三者バイナリ・モデル・wheel が `git ls-files` に 0 件 | `build/check-tree.ps1` |
| A-8 | 敵対検分 3 席（契約・ビルド・ライセンス）の指摘を是正済み | 完了報告 |

## 1. 器（ディレクトリ）

```
irodori-tts-for-yomiwakechan/
  README.md                 非公式・Aratako 氏とは無関係／何をするアプリか／導入の流れ（案）／ライセンス
  decisions.md              正典
  docs/
    contract.md             本体が写す一枚＝配布版が本体に約束する HTTP 契約
    acceptance.md           受け入れ条件（引き継ぎ §4 を 18088・api_key なし・bf16 固定 Radeon に合わせて改訂）
    install.md              導入手順の案（便 E で確定）
    radeon.md               Radeon 版の注記（gfx1151 で確認済みの事実だけ）
    design/ben-a-skeleton-and-build.md   本檔
  upstream/Irodori-TTS      submodule @ 8224daf（無改変）
  upstream/Irodori-TTS-Server  submodule @ 841fb7c（無改変）
  patches/
    0001-audio-io-fallback-on-importerror.patch   torchcodec 不在時の ImportError 取りこぼし（3 ハンク）
    README.md               当て方・出典・上流へ出す PR の下書き
  server/
    ywk_server.py           wrapper（本檔 §4）
    ywk_params.py           /params の定義表（欄・型・既定・範囲・日本語説明）
    ywk_fetch_models.py     モデル初回取得（HF・revision pin・進捗 JSON 行）※便 A は台本のみ・実射は便 B/E
    python312._pth.template ._pth の雛形
  ledger/
    python-embed.json       埋め込み Python（URL・sha256・size）
    runtime-cpu.json        CPU 変種の wheel 台帳（開発・検分用）
    runtime-cu130.json      既定
    runtime-cu126.json      選択肢
    runtime-rocm-gfx1151.json  Radeon 変種（便 C で確定・便 A は雛形）
    models.json             HF 3 リポ（checkpoint・codec・透かし）の revision と檔ごとの sha256
    vc_redist.json          Microsoft 公式 URL（版固定 URL）と sha256
    README.md               台帳の書式（本檔 §3）
  build/
    Common.ps1              共通（ASCII 限定・PS 5.1/7 両対応・ログ・sha256・原子的着地）
    make-ledger.ps1         台帳の生成（PyPI JSON API・download.pytorch.org index・HF API から URL と sha256 を引く）
    assemble-runtime.ps1    台帳→embed 展開→wheel 展開→._pth 生成（-Variant cpu|cu130|cu126|rocm-gfx1151）
    assemble-app.ps1        上流の写し＋patches 適用＋server/ の配置
    verify-runtime.ps1      起動→/health・/params・/ywk/status→（-WithModel）CPU 合成 1 発
    run-tests.ps1           契約テスト
    check-tree.ps1          第三者バイナリ 0 件・submodule clean
    check-licenses.ps1      licenses/ の索引と実檔の突合
  tests/
    contract/               TestClient＋FakeRuntime（research/lab/bin/37_contract.py・38_naming.py の型を移植）
    conftest.py
  licenses/
    README.md               索引（何を・どこから・いつ・自作か原文か）
    irodori-tts/LICENSE     上流 MIT（原文）
    irodori-tts-server/LICENSE  上流 MIT（原文）
    irodori-tts-v4.1-small/LICENSE.md   重み MIT（宣言のみ＝MIT 全文を自作し「宣言の出典」を併記）＋Ethical Restrictions 4 条（原文）
    semantic-dacvae-japanese-32dim/LICENSE.md  コーデック MIT（同上）
    silentcipher/LICENSE    sony/silentcipher MIT（原文・GitHub）
    dacvae/LICENSE          facebookresearch/dacvae Apache-2.0（原文・GitHub）＝裁定 29「Apache-2.0 と読む（3 対 1）」。HF 本文 46 行目の SAM 記述は原文の檔と行つきで README に併記し「当方の読み」と明記
    irodori-tts-for-yomiwakechan/LICENSE  自作分 MIT
    first-run-notices.md    初回取得で利用者の機体に入る第三者物（torch・CUDA/cuDNN DLL・libsndfile・vc_redist・uv 不使用）の通知文の索引＝**配布物に入れる**（`decisions.md` 46）。初回取得 UI が**取得を始める前に**表示する檔なので、取得の前から手元に無ければならない。配布物に入れないのは第三者物そのもの（バイナリ・wheel・重み＝`decisions.md` 8）であって、その通知文ではない
  installer/                便 E（便 A は README のみ）
  launcher/                 便 D（便 A は README のみ）
  probe/                    実機検分の台本置き場（便 A は README のみ）
  research/                 調査便の写し（読むだけ）
```

## 2. 実行時の配置（利用者機）と起動の型

- インストール先（便 E・per-user）＝`%LOCALAPPDATA%\Programs\irodori-tts-ywk\`＝ランチャ exe・`server/`（wrapper＋**パッチ適用済みの上流の写し**＝MIT・数 MB のテキスト）・`ledger/`・`licenses/`・`voices/`（プリセット）・docs。
- 初回取得先＝`%LOCALAPPDATA%\irodori-tts-ywk\`＝`runtime\<variant>\`（埋め込み Python＋site-packages）・`models\`（`HF_HOME`）・`voices\`（利用者の話者・`voices.json`）・`logs\`・`settings.json`。
- 起動＝ランチャが env を組んで `runtime\<variant>\python.exe -m ywk_server --host 127.0.0.1 --port 18088` を子プロセスで起こす（bat を使わない＝CRLF/UTF-8 事故の反面教師 `research/local-additions/`）。
- `._pth`（`runtime\<variant>\python312._pth`）＝5 行・絶対パス・forward slash・`import site` なし：
  ```
  python312.zip
  .
  <runtime>/site-packages
  <app>/server
  <app>/server/upstream/Irodori-TTS
  <app>/server/upstream/Irodori-TTS-Server/src
  ```
  （調査の実射正本＝`research/lab/notes/30-embed-and-gpu-control-facts.md` §0-3。パスは `._pth` 専管・設定は env。`PYTHONPATH` は無視される。）
- 既定 env（wrapper が `os.environ.setdefault` で焼く＝ランチャが上書きできる）：
  〔**是正・便 G（裁定 105・2026-09-07）**＝下の `IRODORI_PRELOAD=true` は**`false` に変わった**
  （ポート先行＝bind してから裏の糸でモデルを載せる）。他の欄はそのまま。
  詳細＝`docs/design/ben-d-launcher.md` §25・`docs/contract.md` ⑵。〕
  `IRODORI_HOST=127.0.0.1`・`IRODORI_PORT=18088`・`IRODORI_HF_CHECKPOINT=Aratako/Irodori-TTS-v4.1-Small`・`IRODORI_PRELOAD=true`・`IRODORI_EMPTY_CACHE_INTERVAL=0`・`IRODORI_ALLOW_NO_REF_VOICE=false`・**`IRODORI_DEFAULT_VOICE=デフォルト`**（`decisions.md` 45＝`/params` の `request.voice.default` と揃える。省いた `voice` は参照なし合成になる）・`IRODORI_DEFAULT_NUM_STEPS=40`・`IRODORI_DEFAULT_RESPONSE_FORMAT=wav`・`IRODORI_VOICES_DIR=<絶対パス>`・`IRODORI_VOICE_ALIASES_FILE=<絶対パス>/voices.json`・`IRODORI_MODEL_DEVICE`／`IRODORI_CODEC_DEVICE`（ランチャが載せる・既定 `auto`）・精度＝device 連動（wrapper が起動時に決める＝§4-6）・`PYTHONUTF8=1`・`PYTHONDONTWRITEBYTECODE=1`・`PYTHONUNBUFFERED=1`・`HF_HOME=<models>`・`HF_HUB_OFFLINE=1`（初回取得後）。
- api_key は持たない（裁定 2）。bind は 127.0.0.1 固定。

## 3. 取得台帳（ledger）の書式

台帳は「配布物に入れない第三者物を、利用者機がどこから取り何で検証するか」の唯一の正本。ランチャ（便 D）と `build/assemble-runtime.ps1` が同じ檔を読む。

```json
{
  "schema": 1,
  "name": "runtime-cu130",
  "generated": "2026-09-04",
  "python": "3.12.10",
  "torch_index": "https://download.pytorch.org/whl/cu130",
  "items": [
    {"kind": "wheel", "name": "torch", "version": "2.10.0+cu130",
     "url": "https://download.pytorch.org/whl/cu130/torch-2.10.0%2Bcu130-cp312-cp312-win_amd64.whl",
     "sha256": "…", "size": 1867405006,
     "license": "BSD-3-Clause", "license_url": "https://github.com/pytorch/pytorch/blob/v2.10.0/LICENSE",
     "notices": ["NVIDIA CUDA EULA（同梱 DLL）", "NVIDIA cuDNN SLA（同梱 DLL）"]},
    {"kind": "archive", "name": "dacvae", "version": "1.0.0+414c207",
     "url": "https://github.com/facebookresearch/dacvae/archive/414c20785fc3a28373073ea8ef7a1316eeeaca6e.zip",
     "sha256": "…", "size": 0, "license": "Apache-2.0", "install": "pure-python-src", "package_dir": "dacvae"}
  ]
}
```
- `kind`＝`python-embed`（zip を展開）／`wheel`（zip 展開＝`*.dist-info` を含めて site-packages へ・`<name>.data/purelib|platlib` は site-packages へ合流・`scripts|headers|data` は捨てる）／`archive`（GitHub の commit 固定 zip・純 Python・`package_dir` を site-packages へ写し `<name>-<ver>.dist-info/METADATA` を最小生成）／`hf-file`（`models.json` のみ）／`installer`（vc_redist）。
- 生成＝`build/make-ledger.ps1`＝build 機で uv が解決した版を凍結し（`uv pip compile --python-version 3.12 --python-platform windows`）、各 wheel の URL と sha256 を PyPI JSON API（`https://pypi.org/pypi/<name>/<ver>/json` の `urls[].digests.sha256`・`cp312`/`py3`・`win_amd64`/`any` を選ぶ）と download.pytorch.org の index（`<index>/<name>/` の `#sha256=` 断片）から引く。**手で写した sha256 は禁止**（台帳生成のログを `build/out/ledger-log/` に残す）。
- 依存の母集合＝上流 2 本の `pyproject.toml` の dependencies から **gradio・wandb・datasets・torchdata・torchcodec・matplotlib・IPython・jedi・peft・pandas・pyarrow** を外し、`sentencepiece>=0.2.1`（cp312 wheel の都合＝`research/local-additions/*/overrides-rocm.txt`）を足す。間引きの正しさは A-4（実合成）で証明する。外した理由は `ledger/README.md` に 1 行ずつ。
- モデル台帳＝`models.json`＝`Aratako/Irodori-TTS-v4.1-Small`（revision `2b28324dc263ed5e6638b3cf3dd94c82ead07b4b`・`model.safetensors` 3,064,295,596 B・`tokenizer.json`・`tokenizer_config.json`）・`Aratako/Semantic-DACVAE-Japanese-32dim`（revision pin・`weights.pth` ≈410 MiB）・`sony/silentcipher`（revision pin・≈70 MB）。sha256 は HF API `https://huggingface.co/api/models/<repo>/tree/<rev>` の `lfs.sha256`（LFS 檔）と `oid`（非 LFS は blob sha1＝別欄）から機械取得。
- `vc_redist.json`＝`https://aka.ms/vs/17/release/vc_redist.x64.exe` は内容が動くので **版固定の直リンク**（Microsoft の `download.visualstudio.microsoft.com/...` 形）と sha256・版番号を載せ、aka.ms は `fallback_url` に置く。
- `python-embed.json`＝`https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip`・sha256 `4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3`・11,133,606 B（調査便の実物から算出・生成時に再取得して突合）。

## 4. wrapper `server/ywk_server.py`

上流 app を import して路を足す 1 檔（＋定義表 `ywk_params.py`）。上流の `irodori_openai_tts` と `irodori_tts` は無改変（パッチは `patches/` のみ）。

### 4-1 起動と既定値
- `python -m ywk_server [--host] [--port]`。§2 の既定 env を **上流 import より前に** `setdefault`（`settings` は import 時に `lru_cache` で固まる＝`research/` upstream-server 読解）。
- 精度の device 連動（§4-6）を env に反映してから `from irodori_openai_tts.app import app, settings, runtime_manager, voice_registry`。
- uvicorn は上流 `__main__` と同じ呼び方（`log_config` は既定・stderr 本命）。起動ログに `ywk_server <版> upstream=8224daf/841fb7c` を 1 行出す（ランチャの状態判定はこの行と `Uvicorn running on`・`runtime loaded in` の 3 行）。

### 4-2 `GET /params`（本体が読む・モデル未読込でも 200・≤100 ms）
```json
{"schema": 1, "engine": "irodori-ywk", "model_loaded": false,
 "checkpoint": {"hf": "Aratako/Irodori-TTS-v4.1-Small", "max_text_len": null, "max_caption_len": null, "ref_max_seconds": null},
 "request": {"input": {"type":"string","max_length":4096}, "speed": {"type":"number","default":1.0,"min":0.25,"max":4.0,"step":0.05},
             "voice": {"type":"string","default":"デフォルト","default_source":"ywk","required":false,"description":"話者名（/ywk/voices の id）。「デフォルト」= 参照なし"},
             "response_format": {"type":"enum","default":"wav","enum":["wav"]}},
 "irodori": [
   {"key":"caption","type":"string","default":"","nullable":true,"group":"emotion","label":"演技指示（キャプション）","description":"…","max_length":null,"exposed_to_ywk":true,"note":"空文字＝未指定（wrapper が欄ごと省略して上流へ渡す・上流は空文字を 400 にするので wrapper が畳む）"},
   {"key":"num_steps","type":"integer","default":40,"min":1,"max":120,"step":1,"group":"quality","label":"サンプリング歩数","range_source":"gradio","exposed_to_ywk":true,"presets":[10,40]},
   {"key":"cfg_scale_text","type":"number","default":3.0,"min":0.0,"max":10.0,"step":0.1,"group":"emotion","label":"感情表現の強さ", …},
   {"key":"t_schedule_mode","type":"enum","default":"…","enum":["linear","sway"], …},
   …（`IrodoriOptions` 44 欄を全部載せる。既定は **env 反映後の実効値**＝`settings.default_*` があればそれ、無ければ `SamplingRequest` の dataclass 既定。`cfg_scale_caption` は Server 実効値 3.0 を採り、gradio 4.0 は `note` に残す。`speaker_kv_min_t` は「`speaker_kv_scale` 指定時のみ 0.9 に解決」を `note` に書く）
 ],
 // 規則（批評席の指摘を受けた設計席の決定・2026-09-04）：
 //  ⑴ dataclass 既定が None の欄（19 欄）は `default:null`＋`nullable:true` で「未指定＝上流の挙動」を表す。
 //  ⑵ 本体に露出する欄は `exposed_to_ywk:true` の集合だけ＝caption・seed・speed・num_steps・cfg_scale_text・cfg_scale_caption・cfg_scale_speaker・
 //     t_schedule_mode・sway_coeff（＋voice は /ywk/voices の Choice）。この集合には null の default を置かない（文字列欄は "" で未指定・seed は "" で乱数）。
 //     受け入れ条件 A-5 の「null 0 件」はこの集合に対して撃つ。他の欄は `advanced` グループ（UI の上級者向け・本体には出さない）。
 //  ⑶ `range_source` を欄ごとに持つ＝"gradio"（上流 UI のスライダ 7 本＝num_steps 1〜120／num_candidates 1〜32／duration_scale 0.5〜1.5／
 //     sway_coeff −1.0〜1.5／cfg_scale_text・cfg_scale_speaker・cfg_scale_caption 0〜10）・"upstream-check"（コードの値域検査・例 seconds>0）・
 //     "ywk"（配布版が自分の責任で名乗る範囲）。上流が保証していない範囲を「gradio 準拠」と名乗らない。
 //  ⑷ `duration_scale` は本体に露出しない（上流が `duration_scale / speed` で除算合成する＝app.py の該当行を出典に note に書く）。本体は `speed` だけを送る。
 //  ⑸ `/params` の所要は ≤ 100 ms（引き継ぎ §4 API 行）。37 ノートの 200 ms は採らない。
 "rules": {"priority": "irodori.X > top-level X > env", "literal_must_be_nested": ["t_schedule_mode","decode_mode","cfg_guidance_mode"],
           "voice_and_no_ref_exclusive": true, "unknown_field": "400", "out_of_range": "400"},
 "prefetch": {"available": false, "planned": "/ywk/prefetch（§6）"}}
```
- 型・enum は上流 openapi（`research/` の `probe/irodori-tts-openapi.json` 写し）と起動時に突合し、差があれば stderr に 1 行（腐り検知＝上流追随の保守 1 箇所）。
- 日本語ラベル・説明は `ywk_params.py` に置く（上流 `docs/parameters.md` を出典に自作・MIT）。
- checkpoint 依存値はモデル読込後に埋まる（未読込なら `null`・`model_loaded:false`）。

### 4-3 `GET /ywk/status`
`{"engine":"irodori-ywk","version":"<配布版の版>","upstream":{"irodori_tts":"8224daf","server":"841fb7c"},"host":"127.0.0.1","port":18088,"runtime":{"loaded":bool,"loading":bool,"error":str|null},"device":{"configured":"cuda:0","actual":"cuda:0"|null,"name":"NVIDIA GeForce RTX 3090"|null,"uuid":"…"|null,"pci_bus_id":…,"precision":"bf16"},"torch":{"version":"2.10.0+cu130","cuda":"13.0","hip":null},"voices":{"count":13,"dir":"<絶対パスは出さない・末尾 1 段のみ>"},"warmup":{"state":"idle|running|done","shots":0}}`
- `actual` は**モデル読込後**に `runtime_manager` から実 device（`next(model.parameters()).device`・`torch.cuda.get_device_properties(idx).uuid`）を読む＝生 echo でない実値。未読込なら `null`。
- 絶対パスは出さない（受け入れ条件「API」行）。

### 4-4 `GET /v1/audio/voices` の覆いと `GET /ywk/voices`
- wrapper は `voice` が上流の NO_REF_IDS（none/no_ref/no-ref/null/text-only・大小無視）のときも「デフォルト」に正規化して受ける（本体の現行アダプタが `voice:"none"` を送る互換の保険。`allow_no_ref_voice=false` でもこの経路は 400 にしない）。
- 上流の一覧は `ref_wav` に絶対パスを返す→ wrapper が同パスの route を差し替え（`app.router.routes` から上流の GET を外して自前を登録）。返す形は上流互換（`{"object":"list","data":[{"id":"デフォルト","object":"voice", ...}]}`）で **パス欄を落とし** `display_name`・`preset:true|false`・`no_ref:true|false` を足す。「デフォルト」を先頭に並べ替える。`none` は `allow_no_ref_voice=false` で出ない（テストで釘）。
- `voices.json` は配布版（ランチャ）が所有＝書式は上流 README §voices.json（`research/` upstream-server 読解）＋`{"デフォルト":{"no_ref":true}}`。wrapper は読むだけ。話者メタ（表示名・caption 既定・既定パラメータ）は `voices/voices.ywk.json`（配布版の台帳・便 D で確定）。便 A は「デフォルト」1 行の `voices.json` と台帳の空雛形を `build/out/app/voices/` に置く。

- **`voice` を省いた要求も 200**（`decisions.md` 45）＝wrapper が `IRODORI_DEFAULT_VOICE=デフォルト` を
  `setdefault` で焼き、`resolve_default_voice` が「デフォルト」を参照なし合成に畳む。`/params` の
  `request.voice` は `default:"デフォルト"`・`required:false` を名乗る＝名乗った既定を送り返しても
  省いても同じ 200 になる。ランチャがこの env を上書きすれば、その話者が既定になる。
  参照 6 欄のどれかが本文にあるときは畳まない（上流は参照を優先するので触ってはいけない）。

### 4-5 `POST /v1/audio/speech` の前段検査（白名簿・範囲・排他）
- 上流 route を差し替え、body を先に検査してから上流の関数を呼ぶ（上流のコードは呼ぶだけ・改変しない）。
- 検査＝⑴ 未知欄（top-level と `irodori` ネストの両方）→ 400 `{"error":{"message":"unknown field: <k>","type":"invalid_request_error","code":"ywk_unknown_field"}}`。⑵ 範囲外→ 400（`/params` の min/max と同じ表を使う）。⑶ Literal 3 欄が top-level にあれば 400（ネストで送れ）。⑷ `voice` と `irodori.no_ref` の同時指定→ 400。⑸ `response_format` は `wav` のみ（上流は wav/flac を soundfile で完結し、mp3/opus/aac は torchaudio→ffmpeg に落ちうる＝ffmpeg 不要にするには wav 固定・第三者バイナリ不同梱）。⑹ 422 の本文 echo（5,000 字＋app.py の絶対パス）を wrapper の `RequestValidationError` ハンドラで置き換え（欄名と理由だけ）。
- 綴り違いの沈黙（上流 T-2）はこれで塞がる。優先順（`irodori.X`→top→env）は上流のまま。

### 4-6 精度の device 連動と GPU 検査
- 起動時、`IRODORI_MODEL_DEVICE`（と `CODEC`）を読み、`cpu` なら精度 `fp32` を、`cuda*` なら `bf16` を **env が未指定のときだけ** 焼く（ランチャの上級者設定で上書き可）。`auto` は torch の可用性で判定（`torch.cuda.is_available()`・import 1.2 s）。
- device 文字列は `^(cpu|auto|cuda(:\d+)?)$` で検査し、`cuda:N` は `torch.cuda.device_count()` 範囲内でなければ **起動前に** 理由 1 行で exit 2（上流は 6〜11 s 後に落ちる）。
- fp32×GPU 不可視の黙った CPU 転落を塞ぐ＝`cuda` 指定で `is_available()=False` なら exit 2。

### 4-7 エラー整形（絶対パス 0 件）
- 応答 middleware＝status ≥ 400 かつ JSON の body から、`settings.voices_dir`・app のディレクトリ・`sys.prefix` の絶対パス文字列を `<path>` に置換。加えて 4-5 ⑹。テストは「エラー body 全件に `:\\` と `:/` を含む絶対パスが 0 件」。

### 4-8 ログ
- stderr 本命（`PYTHONUNBUFFERED=1`）。422 の本文 echo をログに流さない。合成ログは上流のまま（env の echo）＋wrapper が読込完了時に `ywk_server: device actual=<...> uuid=<...>` を 1 行。

## 5. `patches/0001-audio-io-fallback-on-importerror.patch`

対象＝`upstream/Irodori-TTS`（8224daf）。3 ハンク＝`irodori_tts/inference_runtime.py` `_load_audio`（:1515-1527）と `save_wav`（:1530-1541）の `except RuntimeError:` → `except (RuntimeError, ImportError):`、`irodori_tts/codec.py` `encode_file`（:269-280）も同型。torchaudio 2.10 の load/save は torchcodec 不在で `ImportError` を投げる（`research/` upstream-core 読解・`_torchcodec.py:80-86/244-250` 原文確認）。torchcodec がある環境では挙動不変。実装席は **必ず上流の該当行を開いて原文から diff を起こす**（読解の行番号は目安）。適用は `build/assemble-app.ps1` が写しに対して `git apply --check` → `git apply`（submodule には当てない）。`patches/README.md` に上流へ出す PR 文の下書き（英語）を添える。

## 6. 契約文書 `docs/contract.md`（本体が写す一枚）

節＝⑴ 接続（`http://127.0.0.1:18088`・`localhost` 禁止・api_key なし）⑵ 発見（`GET /health` 200→`GET /ywk/status`→`GET /params`→`GET /v1/audio/voices`・タイムアウト 5 s・ready 待ち 120 s・`runtime.loaded` で合成可否）⑶ 合成（`POST /v1/audio/speech`・body の形・`irodori` ネスト・Literal 3 欄はネスト必須・白名簿・優先順・`voice`／`no_ref` 排他・`response_format: wav`・エラー 5 態と body の形・絶対パス 0 件）⑷ 話者（一覧の形・「デフォルト」常在・`none` 不在・日本語 id・配布版が `voices/` の所有者・本体は名前で選ぶだけ）⑸ パラメータ（`/params` の形・本体の `ParamDescriptor` への写像＝Number は min/max/step・String は caption・Choice は enum・既定は実効値）⑹ 実 device の可視化（`/ywk/status.device.actual`・UUID）⑺ 先読み（予約＝`POST /ywk/prefetch {同じ body}`→`{"id","state":"queued|running|done|failed"}`・`GET /ywk/prefetch/{id}`→state と `audio_url`／`DELETE`＝取消・キー＝本体が渡す `client_key`（本体の先読みキー＝要求 JSON そのもの）・優先度は生要求より下・TTL 10 分・**便 A では `available:false`**・本体スケジューラの対応は後続）⑻ 版と互換（`schema` 番号・上流 pin・変更時の告知）⑼ 本体側差分 D-1〜D-9 の写し（`research/report/irodori-native-handoff-2026-09-04.md` §5・18088 で D-9 が必ず要る）。
書式＝本体の `docs/engine-adapters-overview-map.md` §9（`research/` ywk2-adapter 読解）に倣う。

## 7. `docs/acceptance.md`

引き継ぎ §4 の 22 行を原文で写し、次を改訂＝ポート 18088／api_key なし（「安全」行）／「スパイク」行は Radeon 版のみ・bf16 固定で「1 発目 ≤ 5 s（同一形状・db 温）」に書き換え（`research/lab/notes/40-rocm-warmup.md` §8＝3 発目再発なし・fp32 の値を根拠にしない）／VRAM 行は「S_lat 比例（3.5 MB/フレーム）・14.5 s 出力で 3.17 GiB」を注記／導入行は「vc_redist を初回取得で通す」前提で ≤ 6 操作。値は卓が動かしてよい旨を残す。

## 8. 実装席の割り当て（Workflow・opus・並列・同一ツリー・担当パス以外に触らない・commit しない）

| 席 | 担当パス | 成果の判定 |
|---|---|---|
| S1 build+ledger | `build/`・`ledger/`・`server/python312._pth.template`・`server/ywk_fetch_models.py` | A-1・A-2・A-7・`make-ledger.ps1` を実走して cpu 台帳を生成（cu130/cu126 も生成＝ダウンロードはせず URL と sha256 だけ） |
| S2 wrapper+tests | `server/ywk_server.py`・`server/ywk_params.py`・`tests/`・`patches/` | A-5（FakeRuntime で緑）・パッチの `git apply --check` |
| S3 docs+licenses | `docs/contract.md`・`docs/acceptance.md`・`docs/install.md`・`docs/radeon.md`・`licenses/`・`README.md`・`installer|launcher|probe/README.md` | A-6 |
| 統合（設計席） | S1〜S3 の成果で A-3・A-4 を実走 | 実機 |
| 敵対検分 3 席 | 契約（S2 を壊しに行く）・ビルド（台帳と台本を壊しに行く）・ライセンス（原文と突合） | 是正票 |

停止域＝§5 の通り。特に `upstream/` は読むだけ（`PYTHONDONTWRITEBYTECODE=1` を全実行に付け、`__pycache__` を submodule に書かない）。
