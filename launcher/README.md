# launcher/ — ランチャ UI（**便 D**・便 A では本 README のみ）

> **便 A の時点で、このディレクトリには実装が無い。**便 D が入る前の申し送りだけを置く。
> 正典＝`decisions.md`。契約＝`docs/contract.md`。受け入れ条件＝`docs/acceptance.md`。

## 1. 何を作るか

**WPF（.NET）のランチャ**。この機体に .NET SDK 10.0.400 がある（`decisions.md` 25）。

ランチャは 3 つの役目を持つ。

1. **初回取得**＝台帳（`ledger/*.json`）を読み、実行系（埋め込み Python＋wheel）とモデルと
   `vc_redist.x64.exe` を取得し、sha256 で検証して `%LOCALAPPDATA%\irodori-tts-ywk\` に展開する。
   進捗は `server/ywk_fetch_models.py` の**進捗 JSON 行**を読んで出す。
2. **常駐**＝env を組んで
   `runtime\<variant>\python.exe -m ywk_server --host 127.0.0.1 --port 18088` を子プロセスで起こし、
   stderr を取り込んで状態を判定する。**bat は使わない**（文字コード事故の反面教師＝
   `research/local-additions/`）。
3. **単体 TTS の UI**＝利用者が自環境で GPU・CUDA 版・パラメータ・参照ボイスを試す
   （配信外の読み上げテスト＝`decisions.md` 15）。**本体からの読み上げ司令は、ここで指定した
   GPU・CUDA 版を暗黙に使う**（device は Server プロセスの env でしか決まらない＝1 プロセス 1 デバイス）。

**上流の `gradio_app.py` は使わない**＝device の choices が型名だけ（`cuda:1` を選べない）・env を
1 欄も読まない・CLI に device 引数が無い（`research/lab/notes/36` P-4）。
gradio を依存から外すと配布サイズも 43〜82 MB 減る。

## 2. 決まっていること

| # | 事項 | 出典 |
|---|---|---|
| 1 | 起動主体は**配布版**＝本体は `/health` で見つけるだけ | `decisions.md` 6 |
| 2 | ポート **18088**・bind `127.0.0.1`・**api_key なし** | `decisions.md` 2 |
| 3 | GPU は **UUID（と PCI bus id）で保存**し、起動時に index へ解決する（列挙 ≤ 5 s）。index は再起動で変わりうる | `docs/acceptance.md`「GPU」行 |
| 4 | 精度は **device 連動**（GPU→bf16・CPU→fp32）。上級者設定で上書き可（Radeon 版を除く） | `decisions.md` 7・`docs/radeon.md` |
| 5 | 焼く既定＝`preload=true`・`empty_cache_interval=0`・`allow_no_ref_voice=false`＋alias「デフォルト」・v4.1-Small 決め打ち・`voices_dir` は絶対パス・ready 待ち 120 s | `decisions.md` 7 |
| 6 | **`voices/` と `voices.json` の所有者はランチャ**（上流の登録 API 4 口は使わない＝日本語名が 400 になる） | `docs/contract.md` ⑷ |
| 7 | 話者メタ（表示名・caption 既定・既定パラメータ）は**配布版の台帳** `voices/voices.ywk.json`（上流に置き場が無い） | 同 |
| 8 | 既定 steps は **40**（上流既定）。UI のプリセットで 10 を選べる。**本体の指定が来たら必ず勝つ** | `decisions.md` 10 |
| 9 | CPU 合成は**UI にだけ**露出し、配信用途では非推奨と明記する | `decisions.md` 13 |
| 10 | 透かしは**既定 ON**・切る経路を持たない | `decisions.md` 9 |
| 11 | 状態判定は stderr の **3 行**＝`ywk_server <版> upstream=…`／`Uvicorn running on`／`runtime loaded in` | 設計書 §4-1・`docs/acceptance.md`「ログ」行 |
| 12 | **422 の本文 echo をログに流さない**（1 発 5 KB＋絶対パス） | `docs/acceptance.md`「ログ」行 |
| 13 | 参照ボイス欄には **Ethical Restrictions 1（No Impersonation）の但し書き**を出す | `README.md` §4 |
| 14 | Radeon 版は **bf16 固定＋起動時暖機**（段の 3 射＋自然尺 1 射）・`/ywk/status.warmup` で進捗 | `docs/radeon.md` |

## 3. 決まっていないこと（便 D で決める）

1. 画面の構成・話者台帳（`voices.ywk.json`）の書式。
2. 「デフォルト」を削除・改名できるようにするか（`research/lab/notes/37` §4-6 の ξ）。
3. CUDA 版の切替の実装＝**V-1** site-packages 2 本を `._pth` の 1 行で切替（+3,937 MB・切替 0 s）／
   **V-2** 都度 DL（1,781／2,470 MiB）／**V-3** cu130 のみ＋手順書。
   **`._pth` 書換での切替は未実射**（`research/lab/notes/36` W-7）。
4. 終了時の扱い（本体の元栓は「自分が起こした個体だけ落とす」流儀＝本体 §9 ⑤。配布版が常駐主体に
   なるとこの前提が変わる）。
5. 話者 100 件級の一覧の所要（上流は毎要求 `iterdir()`＝キャッシュ無し・**未実測**）。

## 4. 落とし穴（実射で分かっているもの）

| # | 事実 | 効き |
|---|---|---|
| 1 | **`._pth` があると `PYTHONPATH` は無視される**（`PYTHON*`／`IRODORI_*`／`HF_*` の env は効く） | **パスは `._pth` の専管・設定は env**。混ぜない |
| 2 | `IRODORI_VOICES_DIR` は**絶対パス必須**（既定は CWD 相対で起動時に `mkdir`） | 読取専用の導入先だと起動で落ちる |
| 3 | **CPU×bf16 は起動時 `ValueError`**／**fp32×GPU 不在は黙って CPU に落ちて 200**（340 倍遅い） | 精度の device 連動は安全装置。黙った転落を塞ぐ |
| 4 | 上流は device 文字列を検査せず、`preload=true` なら **6〜11 s 後に落ちる** | ランチャ（と wrapper）が起動前に 0 s で弾く |
| 5 | `voices.json` が壊れると **一覧だけ 500・`/health` は 200** | 配布版は 500 を返さず「0 件＋理由」を返す |
| 6 | 同一 stem の `.wav` は `.pt`／`.speaker.safetensors` に**黙って勝つ** | 事前計算潜在は別 stem か `voices.json` 別名で置く |
| 7 | `voice` と `irodori.no_ref` を同時に送ると**参照が黙って落ちる** | 契約で同時指定を 400 にした（`docs/contract.md` ⑷） |
| 8 | 合成は 1 プロセス 1 本の直列（`max_concurrent_synthesis` 既定 1） | 配信中に UI で試し撃ちすると読み上げが待たされる。UI に警告を出す |

## 5. 停止域

- `upstream/` は読むだけ（`__pycache__` を作らない＝`PYTHONDONTWRITEBYTECODE=1`）。
- `C:/IrodoriTTS/`・`C:/irodori-TTS-server/`・`yomiwakechan2` に書かない。
- 第三者バイナリをリポ内（`build/out/` と `.venv-dev/` 以外）に置かない。
