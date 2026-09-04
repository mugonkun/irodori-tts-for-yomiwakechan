# ledger/ — 取得台帳の書式と、依存を間引いた理由

> 台帳は「**配布物に入れない第三者物を、利用者機がどこから取り、何で検証するか**」の唯一の正本。
> ランチャ（便 D）と `build/assemble-runtime.ps1` は**同じ檔**を読む。
> **手で書き換えない**。全部 `build/make-ledger.ps1` の生成物であり、`sha256` は 1 つ残らず機械取得（§3）。

---

## 1. 檔の一覧

| 檔 | 中身 | 生成 |
|---|---|---|
| `python-embed.json` | 埋め込み Python 3.12.10（`python-3.12.10-embed-amd64.zip`） | `make-ledger.ps1`（実ダウンロード＋sha256 再計算） |
| `runtime-cpu.json` | CPU 変種の site-packages 一式（開発・検分用） | 同上 |
| `runtime-cu130.json` | **既定**（NVIDIA・ドライバ 580 以上） | 同上 |
| `runtime-cu126.json` | 選択肢（ドライバ 560.76 以上） | 同上 |
| `runtime-rocm-gfx1151.json` | Radeon 変種。**便 A では雛形**（`status: "template"`・`items: []`）。便 C が埋める | `make-ledger.ps1` が雛形だけ書く（既存檔は上書きしない） |
| `models.json` | HF 3 リポの pin 版と檔ごとのハッシュ | 同上（HF API） |
| `vc_redist.json` | `msvcp140.dll` を入れる Microsoft の再頒布パッケージ | 同上（版固定直リンク＋実ダウンロード） |

生成の逐語ログは **`build/out/ledger-log/`**（git 管理外）＝`requirements.in`・`overrides.txt`・
`mother-set.txt`・`requirements-<変種>.txt`（`# via` 注釈つき）・`pruned-<変種>.txt`・
`requirements-<変種>.pruned.txt`・`sources-<変種>.txt`・`uv-compile-<変種>.log`・`make-ledger.log`。

---

## 2. 檔の書式

```json
{
  "schema": 1,
  "name": "runtime-cu130",
  "generated": "2026-09-04",
  "generator": "build/make-ledger.ps1",
  "python": "3.12.10",
  "python_tag": "cp312",
  "platform_tag": "win_amd64",
  "torch_index": "https://download.pytorch.org/whl/cu130",
  "uv": "uv 0.12.7 (...)",
  "upstream": { "irodori-tts": "8224daf...", "irodori-tts-server": "841fb7c..." },
  "resolution": { "command": "uv pip compile ...", "overrides": [...],
                  "dropped_top_level": [...], "pruned_after_solve": [...], "log": "build/out/ledger-log/" },
  "count": 101,
  "items": [ ... ]
}
```

### `items[].kind`

| kind | 意味 | `assemble-runtime.ps1` の扱い |
|---|---|---|
| `python-embed` | 埋め込み Python の zip | 変種ディレクトリへ展開 → `python312._pth` を生成（`server/python312._pth.template` から） |
| `wheel` | `.whl`（＝zip） | `site-packages/` へ**まるごと**展開。`*.dist-info` も込み（transformers が `importlib.metadata.version()` を呼ぶ＝`research/lab/notes/30` §6-1 N-8）。`<name>-<ver>.data/purelib\|platlib` は `site-packages/` へ合流、`scripts\|headers\|data` は捨てる |
| `sdist` | wheel を出していない純 Python（`argbind`・`randomname`）の `.tar.gz` | `tar.exe` で展開 → `package_dirs` を写す → 最小 `*.dist-info` を生成 |
| `archive` | commit 固定の GitHub ソース zip（`dacvae`・`silentcipher`） | 展開 → `package_dir` を写す → 最小 `*.dist-info` を生成 |
| `hf-repo` | HF リポ 1 本（`models.json` のみ） | `server/ywk_fetch_models.py` が取る |
| `installer` | `vc_redist.x64.exe`（`vc_redist.json` のみ） | 便 E のインストーラが `/install /quiet /norestart` で通す |

### 共通の欄

`name`・`version`・`url`・`sha256`（小文字 hex）・`size`（バイト・整数）・`filename`・
`license`・`license_url`・`sha256_source`（**どこから取った sha256 か**の逐語）。
`notices` は「その物を入れると利用者の機体に入る第三者の許諾文と、その wheel が何を同梱しているかの観測事実」。
**torch／torchaudio の item は変種に依らず必ず持つ**（cpu・cu130／cu126・rocm*）＝cu 版は NVIDIA CUDA EULA と
cuDNN SLA、cpu 版は同梱の `dist-info/LICENSE`（third_party 33 件・oneDNN を含む）と
`torch/lib/libiomp5md.dll` の許諾が**未特定**である事実、rocm 版は AMD wheel が許諾を名乗らない事実
（便 C が埋める）。cu* 限定だった頃は cpu 台帳の torch が何も名乗らず、突合の対象が 0 件だった。
`licenses/first-run-notices.md` と `build/check-licenses.ps1` がこの欄を突合する。
`fallback_url` は「同じ檔を出す別ホスト」（torch・torchaudio・`vc_redist`）＝§3。

**`license` 欄は空にしない**。`build/check-licenses.ps1` は全 item の `license` が null・空でないこと、
および `license` に GPL／LGPL／MPL／AGPL／未特定 を含む item の名前が
`licenses/first-run-notices.md` に出ていることを検分する（`notices` 欄の有無に依らない）。

---

## 3. sha256 の取得元（**手打ち禁止**）

| 物 | 取得元 | 檔中の `sha256_source` |
|---|---|---|
| PyPI の wheel / sdist | `https://pypi.org/pypi/<name>/<version>/json` の `urls[].digests.sha256` | `pypi json api urls[].digests.sha256` |
| torch・torchaudio | `https://download.pytorch.org/whl/<cu>/<name>/` の index ページの `#sha256=` 断片 | `index page <index>/<name>/ (href #sha256 fragment); the href points at download-r2.pytorch.org` |
| GitHub の commit 固定 zip | 実際にダウンロードして計算 | `downloaded and hashed by build/make-ledger.ps1` |
| 埋め込み Python | 実ダウンロード＋計算。**`research/lab/notes/22` §4 の実測値と一致しなければ生成を止める** | 同上（cross-checked） |
| `vc_redist.x64.exe` | 実ダウンロード＋計算。**URL 中の 64 桁 hex（Microsoft が埋めている）とも突合**（`sha256_matches_url_segment`） | 同上 |
| HF の LFS 檔 | `https://huggingface.co/api/models/<repo>/tree/<rev>` の `lfs.oid`（＝sha256） | `models.json` の `sha256_source` |
| HF の非 LFS 檔 | 同 API の `oid`（＝**git blob sha1**）。`sha256` は `null`・`verify: "git-blob-sha1"` | 同上 |

`torch` の `size` だけは index ページに無いので、pin した URL への **HEAD の `Content-Length`**。

### torch の `url` と `fallback_url`（**ホストが 2 つある**）

`https://download.pytorch.org/whl/<cu>/<name>/` の index ページが返す `href` は
**`https://download-r2.pytorch.org/...`**（CDN の別名）である。生成器は href をそのまま `url` に採り、
**同じ path をカノニカル側（`download.pytorch.org`）に写した `fallback_url` を必ず足す**。
実測（2026-09-05・HEAD）＝両ホストとも 200・`Content-Length` 一致
（例＝cpu の torch 113,671,330 B）。sha256 は同じ檔なので**どちらから取っても検証は同じ**。
`build/assemble-runtime.ps1` とランチャ（便 D）は `url` で失敗したら `fallback_url` を試す。
この 1 行が無いと、CDN の別名が畳まれた日に**全利用者の初回取得が 404 で死ぬ**（手も足も出ない）。

### `vc_redist` の版固定

`https://aka.ms/vs/17/release/vc_redist.x64.exe` は**中身が動く**ので `fallback_url` に置き、
`url` は aka.ms が生成時点で指した `https://download.visualstudio.microsoft.com/...` の直リンクを凍結した。
`version` は落とした exe の `ProductVersion`（2026-09-04 の生成では **14.44.35211.0**）。
Microsoft はこの直リンクの path に payload の sha256 を埋めているので、**自分で計算した値と URL の値が一致すること**も檔に記録している。

---

## 4. 依存の母集合と、外した物の理由（1 行ずつ）

母集合＝**上流 2 本の `pyproject.toml` の `[project].dependencies`**（`make-ledger.ps1` が檔から読む＝上流が動いたら差分が見える）。
それを次のように加工してから `uv pip compile --python-version 3.12 --python-platform windows` で凍結する。

### 4-1. トップレベルから外した物

| 外した物 | 理由（出典） |
|---|---|
| `gradio` | サーバ経路で 1 度も import されない。7861 の gradio UI 専用（`research/lab/notes/08` §2-B・STEP A〜E に出ない）。約 77 MB |
| `wandb` | 訓練専用（`upstream/Irodori-TTS/train.py:3013` の関数内 import）。約 81 MB |
| `datasets` | 前処理専用（`prepare_manifest.py:20`） |
| `torchdata` | 訓練専用（`train.py:21-22`） |
| `torchcodec` | `torchaudio.load/save` の裏でしか使われず、**不在でも soundfile に落ちる**。ただし上流は `except RuntimeError` しか書いていないので `patches/0001-...` で `ImportError` も捕まえる。第三者バイナリ（FFmpeg 系）を持ち込まないための最重要の 1 件 |
| `peft` | LoRA 適用時のみの遅延 import（`irodori_tts/lora.py:137`）。配布版は LoRA を露出しない |
| `irodori-tts`（Server 側の git 依存） | **wheel では入れない**。`server/upstream/Irodori-TTS/` にソースとして置き `._pth` で通す（設計書 §2） |

### 4-2. 解決後に間引いた物（＋それだけを親に持つ孤児）

`descript-audiotools`（＝`dacvae` が引く）が**宣言だけ**で `matplotlib` と `ipython` を要求するため、
そのまま解くと 21 個の道連れが乗る。実 import は 0（`research/lab/notes/08` §2-B）。

- 間引きの根＝**`matplotlib`・`ipython`・`jedi`・`pandas`・`pyarrow`**
- 孤児の判定＝`uv pip compile --annotation-style line` の `# via` 注釈で親を辿り、
  **親が全部消えていて、かつ自分が直接要求でない**物を不動点まで削る（`build/out/ledger-log/pruned-<変種>.txt` に全件と親を残す）。
- 2026-09-04 の結果＝122 → **101**。消えたのは
  `asttokens・contourpy・cycler・executing・fonttools・ipython・ipython-pygments-lexers・jedi・kiwisolver・
  matplotlib・matplotlib-inline・parso・prompt-toolkit・psutil・pure-eval・pyparsing・python-dateutil・six・
  stack-data・traitlets・wcwidth` の 21 件。
- **間引きが正しいことの証明は実合成**（受け入れ条件 A-4）。台帳を眺めて決めていない。
  なお便 A の時点で、組み上げた `runtime-cpu` の `python.exe` で
  `torch / torchaudio / transformers / soundfile / argbind / randomname / dacvae / silentcipher` の
  import が全部通ることは確認済み（`import site` なし）。

### 4-3. 差し替えた物

| 元 | 後 | 理由 |
|---|---|---|
| `sentencepiece>=0.1.99,<0.2` | `sentencepiece>=0.2.1` | 0.1.x に cp312 の wheel が無く、ソースビルドに落ちて失敗する。稼働機の ROCm 導入でも同じ差し替えを踏んでいる（`research/local-additions/*/overrides-rocm.txt`・`research/lab/notes/08` §1-C） |
| `torch` / `torchaudio`（範囲指定） | `torch==2.10.0` / `torchaudio==2.10.0` | 変種ごとに index を切り替えて pin。3 index とも cp312・win_amd64 の 2.10.0 が実在することを生成時に確認し、無ければ index の実在版を並べて**止まる** |
| `dacvae`（commit 無し） | `@414c20785fc3a28373073ea8ef7a1316eeeaca6e` | 上流 `uv.lock` の `source = { git = "...#414c2078..." }`。`research/lab/kit-cuda` が作り置きした wheel と同じ commit |

### 4-4. override（1 件だけ）

```
protobuf>=5.29.0
```

`descript-audiotools` 0.7.2 が `protobuf (<3.20,>=3.9.2)` を宣言しており、素直に解くと **protobuf 3.19.6** になる。
3.19.6 は `protobuf-3.19.6-nspkg.pth` を site-packages に置く型で、**`.pth` は `import site` が無いと読まれない**。
配布する `python312._pth` は設計書 §2 のとおり `import site` を**持たない**ので、その組み合わせは黙って壊れる
（`research/lab/notes/08` §4-C がこれを「embed で `import site` が要る理由」として挙げ、
`research/lab/notes/30` §1-A a14 が「protobuf 7.36 では `nspkg.pth` が存在しないので不要になる」と更新している）。
override 後の実解は **protobuf 7.36.1**＝調査便の lab 箱と同じ版。

**この規則は機械で守る**＝`build/assemble-runtime.ps1` は wheel を展開するたびに
`site-packages/` 直下の `*.pth` を**捨てて**ログに 1 行残し（`dropped inert <name>.pth`）、
§5 で 1 件でも残っていれば **throw** する（報告 JSON の `dropped_pth` に全件）。
読まれない `.pth` は「何かしているように見えて何もしない檔」であり、次に台帳を作り直したときに
同じ黙った壊れが戻るのを防ぐ。2026-09-04 の cpu 台帳では `setuptools` の
`distutils-precedence.pth` が 1 本入っていた（Python 3.12 は stdlib から distutils を外しており、
その shim は `.pth` 経由でしか入らない＝`import site` の無い配布版では最初から効かない）。

### 4-5. 残してある物（外していない）

`numba` / `llvmlite`（合わせて約 130 MB）は上流の `dependencies` に載っており、設計書 §3 の除外一覧にも無いので**残した**。
`research/lab/notes/08` §2-B は「両コードベースに import 0 件・librosa が遅延で持つだけ」と記録しているので、
**外せる見込みはあるが、便 A では決めない**（外すなら A-4 の実合成で証明してからにすること）。

---

## 5. wheel を出していない依存（`kind: "sdist"`）

`argbind` と `randomname` は **PyPI に wheel が 1 つも無い**（全リリースが sdist のみ）。
利用者機に pip は無い（`._pth` 環境では `pip` が `find_spec` で None＝`research/lab/notes/30` §1-B）ので、
`assemble-runtime.ps1` が `.tar.gz` を展開し、`package_dirs` に書かれた import 可能なディレクトリを写して
最小の `*.dist-info` を作る。`package_dirs` は生成時に sdist の中身を実際に見て決めている（推測していない）。

---

## 6. `models.json`

- 3 リポ・22 檔・**3,570,982,039 B**（2026-09-04 の生成）。
- `Aratako/Irodori-TTS-v4.1-Small` は設計書指定の revision `2b28324dc263ed5e6638b3cf3dd94c82ead07b4b` を pin
  （`model.safetensors` 3,064,295,596 B・sha256 は HF API の `lfs.oid`）。
- 残り 2 本は生成時点の head を pin し、`head_at_generation` にも同じ値を残す。
- `sony/silentcipher` は **リポジトリ全体**が要る＝上流 `silentcipher/server.py:475` が
  `snapshot_download(repo_id="sony/silentcipher")` を `allow_patterns` 無しで呼ぶため。
  この repo の `.ckpt` は LFS ではないので **git blob sha1 でしか検証できない**（`verify: "git-blob-sha1"`）。
- `hf_cache_dir` は `models--<repo を -- で連結>`＝`HF_HOME/hub/` 直下に出来るディレクトリ名。
- **注意**＝`ywk_fetch_models.py --check-only` は pin した revision の**全檔**を見るので、
  上流サーバが必要分だけ落とした既存キャッシュに対しては `.gitattributes` や `README.md` を「無い」と報告する。
  これは異常ではなく、台帳が revision 全体を持っていることの結果。

---

## 7. 大きさ（2026-09-04 の生成・items の `size` 合計）

| 変種 | items | wheel/sdist/archive の合計 |
|---|---|---|
| `runtime-cpu` | 101 | **270.1 MiB** |
| `runtime-cu130` | 101 | **1,944.1 MiB**（うち torch 1,867,405,006 B） |
| `runtime-cu126` | 101 | **2,632.9 MiB**（うち torch 2,589,881,452 B） |

＋ `python-embed` 11,133,606 B ＋ `vc_redist` 25,635,768 B ＋ モデル 3,570,982,039 B。
cu130 の初回取得は合計 **約 5.4 GiB**（`research/report/irodori-native-handoff-2026-09-04.md` §5-3 の見積と一致）。

---

## 8. 作り直し方

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build\make-ledger.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File build\make-ledger.ps1 -Variant cu130 -SkipModels -SkipVcRedist -SkipPythonEmbed
```

- ネットに出る。uv **0.12.7** 以外では止まる（`decisions.md` 25 の機体前提）。
- `build/cache/` は再利用する（sha256 が合えば再取得しない）。
- **cu130 / cu126 は URL と sha256 を引くだけで wheel を落とさない**（HEAD で size を見るだけ）。
  実際に落ちるのは `assemble-runtime.ps1 -Variant cu130` を走らせたとき。
