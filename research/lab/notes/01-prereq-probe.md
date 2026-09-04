# 01 前提資料（実測側）の読解ノート — probe 系

- 席: 読解サブ席（opus）／作成 2026-09-04／依頼文 `BRIEF.md` §3・§4-5・§4-7 に対応する材料の抽出。
- **引用の書き方**: 本ノートの相対パスは 2 系ある。
  - `probe/...` = `C:\Users\mugonkun\source\repos\yomiwakechan2\probe\...`（読むだけ／1 バイトも変更していない）
  - `upstream/...` = `C:\Users\mugonkun\source\repos\irodori-native-research\upstream\...`（読むだけ）
  行番号は `cat -n` / `sed -n` / `grep -n` で実際に確認した値のみ。確認していない事は「未確認」札を付ける。
- 実行した書き込みは本ファイル 1 檔のみ。8088／7861 には一切触れていない（HTTP も投げていない＝既存の生データの読解だけで完結）。

## 要点（10 行以内）

1. wav は **PCM 16bit・48,000Hz・1ch**（`probe/irodori-tts-probe-report.md:118`）。`response_format=pcm` はヘッダ無しの生 16bit LE（`upstream/Irodori-TTS-Server/src/irodori_openai_tts/audio.py:40-42`）＝バイト列を素で流せる。
2. レイテンシは**環境で 17 倍動く**: CPU 10 字 13.5 秒 → ROCm fp32 3.7 秒 → **ROCm bf16 0.77 秒**（`probe/irodori-tts-probe-report.md:134,303,400`）。RTF は 3.4 → 0.95 → 約 0.2。
3. Windows の CPU 転落の原因は上流の依存宣言＝**pytorch-rocm index に `sys_platform == 'linux'` マーカー**（`probe/irodori-tts-probe-report.md:30-33`）。⒜⒝⒞のどの案でも「利用者に torch を選ばせない」ことが最大の効き所。
4. ROCm 化は成功しており、**AMD 公式 wheel `torch[device-gfx1151]==2.13.0+rocm10.0.0`（ROCm SDK 同梱・別途 SDK 不要）**で解決（`probe/irodori-tts-rocm-setup-guide.md:69,75`）。詰まりは 3 つ＝`uv run` が旧 venv を掴む・Python 3.12 必須・sentencepiece 0.2.1 差し替え。
5. GPU 固有の癖 2 つが運用を支配する＝**未見の音声長は初回 4〜5 倍遅い**（形状チューニング・プロセス内キャッシュのみ）と **`IRODORI_EMPTY_CACHE_INTERVAL=0` にしないと 10 発おきに +15 秒スパイク**（同 315-324）。
6. API 契約は FastAPI／全 5 パス。感情制御は `irodori` ネスト **44 欄**（`probe/irodori-tts-openapi.json` = 整形後 462-966 行）で、**openapi 上の既定値は 1 つも無い**（全欄 `null` 許容）＝既定はサーバ側 env（`upstream/Irodori-TTS-Server/.env.example:32-45`）。
7. **voice は「必須」ではない可能性が高い**＝`irodori.no_ref=true` を送ると voice レジストリを迂回する分岐がある（`upstream/.../app.py:439-455`）。probe の「voice 省略は 400」は no_ref 無しでの実射（`probe/irodori-tts-probe-report.md:103-105`）＝**要 1 射で確定**（未確認）。
8. chunking は既定 ON・`chunk_min_chars=80`（`upstream/.../.env.example:43-44`）。**200 字の実測はチャンク分割された直列合成**であり、連結は `torch.cat` でクロスフェード無し（`upstream/.../app.py:675`）＝境界の耳検分は未測。
9. **SSE ストリーミング経路が実装済み**（`stream_format="sse"` でチャンク単位に base64 音声を逐次配信・`upstream/.../app.py:693-775`）＝⒞の「先読み・初音までの短縮」の一次材料だが **probe では一度も撃っていない（未測）**。
10. 参照音声は**サーバ側ファイルパス指定**（バイト列を body で渡す口は無い）＝ voices CRUD か server FS 経由。定常コストは **+0.26 秒／参照秒**（`probe/irodori-tts-probe-report.md:365`）。

---

## 1. 実測値表（P-0〜P-10 ＋ 追補 1〜4）

### 1.0 計測環境と「値を読むときの前提」

| 項目 | 実測 | 出典 |
|---|---|---|
| 実施日／席 | 2026-08-29／Claude Code (Fable) | `probe/irodori-tts-probe-report.md:3` |
| 実機 | AMD Ryzen AI MAX+ 395（16C/32T）・RAM 64GB 統合・Radeon 8060S（iGPU・専有 VRAM 報告値 4GB） | 同 `:4,59-61` |
| 7861 | `gradio_app_voicedesign.py --server-name 0.0.0.0 --server-port 7861`（`C:\IrodoriTTS\Irodori-TTS\.venv`・uv run 経由） | 同 `:51-53` |
| 8088 | `uv.exe run --no-sync python -m irodori_openai_tts --host 0.0.0.0 --port 8088`（`C:\irodori-TTS-server\Irodori-TTS-Server`） | 同 `:75-76` |
| チェックポイントの食い違い | 7861 = `Aratako/Irodori-TTS-v4.1-Small`／8088 = `Aratako/Irodori-TTS-v4-Small`＝**2 実機で版が違う** | 同 `:54-57`, `probe/irodori-tts-health.json:5` |
| **計測の下駄** | `--host 0.0.0.0` は IPv4 のみ listen。クライアントが `localhost` で繋ぐと ::1 を先に試し **毎リクエスト +1.2〜2.0 秒**（health: 127.0.0.1=4ms vs localhost=2014ms） | `probe/irodori-tts-probe-report.md:255-257`, `probe/irodori-tts-restart-raw.txt:31-34` |
| ⇒ **アダプタは 127.0.0.1 明示が正** | P-4 の CPU 値は localhost 経由＝約 2 秒下駄込み。GPU 系列は 127.0.0.1 直 | 同 `:257-259`, `probe/irodori-tts-gpu-latency-raw.txt:18` |

### 1.1 wav 形式・サンプルレート（P-3）

| 項目 | 実測 | 出典 |
|---|---|---|
| Content-Type | `audio/wav` | `probe/irodori-tts-probe-report.md:116` |
| fmt タグ | **1（PCM）** | 同 `:118` |
| チャンネル | **1（モノラル）** | 同 `:118` |
| サンプルレート | **48,000 Hz** | 同 `:118` |
| ビット深度 | **16 bit** | 同 `:118` |
| バイト数の検算 | 3.92 秒 → 376,364 bytes。48000×2×3.92 = 376,320 ＋ RIFF ヘッダ 44 = **376,364** で完全一致＝48k/16bit/mono を独立に裏づけ | `probe/irodori-tts-latency-raw.txt:2`（bytes/audioSec） |
| ヘッドルーム | **peak=1.0 到達の生成物が複数**（speed025・emoji-only）＝乗算 >1.0 はクリップし得る | `probe/irodori-tts-probe-report.md:120-121`, `probe/irodori-tts-wav-metrics.txt:11,17` |
| クリップの機構 | エンコード時に `wav.clamp(-1.0, 1.0)` ＝**モデル出力側で既に飽和している**（後段の乗算以前の話） | `upstream/Irodori-TTS-Server/src/irodori_openai_tts/audio.py:38` |
| 対応形式（コード） | `mp3 / opus / aac / flac / wav / pcm` の 6 種。既定は env で wav | `upstream/.../audio.py:13-20`, `upstream/.../.env.example:33` |
| pcm の中身 | `(wav * 32767).astype("<i2").tobytes()`＝**ヘッダ無し・16bit LE・48kHz mono の生 PCM** | `upstream/.../audio.py:40-42` |
| pcm の実射 | **未測**（wav で確定したため撃っていない） | `probe/irodori-tts-probe-report.md:122-123` |

### 1.2 レイテンシ ― CPU（P-4・全値 localhost 経由＝約 +2 秒下駄）

| 系 | 所要（3 射） | 音声長 | RTF | 字あたり | 出典行 |
|---|---|---|---|---|---|
| 10 字 | 13.522 / 13.658 / 13.227 秒 | 3.92 秒 | 3.37〜3.48 | 約 1,350 ms/字 | `probe/irodori-tts-latency-raw.txt:2-4` |
| 50 字 | 27.843 / 28.144 / 28.625 秒 | 11.48 秒 | 2.43〜2.49 | 約 557 ms/字 | 同 `:5-7` |
| 200 字 | 103.905 / 103.401 / 103.837 秒 | 46.36 秒 | 2.23〜2.24 | 約 520 ms/字 | 同 `:8-10` |
| caption 10 字（本文 50 字） | 26.492 / 24.792 / 25.042 秒 | 10.04 秒 | 2.47〜2.64 | — | 同 `:11-13` |
| caption 106 字（本文 50 字） | 31.314 / 31.009 / 30.284 秒 | 12.24 秒 | 2.47〜2.56 | — | 同 `:14-16` |

- 傾き **約 8.5 秒 ＋ 0.48 秒/字**（10〜200 字でほぼ線形・ばらつき ±2%）: `probe/irodori-tts-probe-report.md:140`
- **127.0.0.1 直の真値**: 10 字 = **12.273 秒**（PRELOAD 済み初回）: `probe/irodori-tts-restart-raw.txt:29`（同 localhost 経由 13.499 秒 = `:30`）
- caption を伸ばすと**所要だけでなく音声長も伸びる**（同一 50 字文で 10.04 → 12.24 秒）＝RTF は同じ 2.5。caption 長そのもののコストは小さい: `probe/irodori-tts-probe-report.md:145-147`

### 1.3 レイテンシ ― ROCm GPU・fp32（追補1・127.0.0.1 直）

| 系 | GPU 実測 | RTF | 字あたり | CPU 比 | 出典行 |
|---|---|---|---|---|---|
| 10 字ウォーム | 3.740 / 3.729 / 4.089 秒 | 0.95 / 0.95 / 1.04 | 約 373 ms/字 | 約 3.3 倍速 | `probe/irodori-tts-gpu-latency-raw.txt:19-21` |
| 50 字ウォーム | 10.075 / 10.095 秒（初回 60.352 秒 = 形状チューニング） | 0.88 | 約 202 ms/字 | 約 2.8 倍速 | 同 `:22-24` |
| 200 字ウォーム | 40.669 / 40.557 秒（初回 245.518 秒） | 0.88 / 0.87 | 約 203 ms/字 | 約 2.5 倍速 | 同 `:25-27` |
| num_steps=20（10 字） | 3.271 秒 | 0.83 | — | CPU 9.2 秒 | 同 `:28` |
| コールド（起動後初回・ロード込み） | 40.8〜64.7 秒（実測 64.724 秒） | — | — | **CPU（23.3〜23.7 秒）より遅い** | 同 `:5`, `probe/irodori-tts-probe-report.md:307` |
| メモリ | 専有 VRAM 6.6GB・RAM 2.1GB | — | — | CPU は RAM 8.6〜12.5GB | 同 `:309`, `probe/irodori-tts-gpu-latency-raw.txt:46` |

- **傾き（50→200 字）**: (40.61 − 10.08) ÷ 150 = **約 0.204 秒/字**（CPU の 0.48 秒/字 の約 43%）。切片はほぼ 0＝GPU では所要が音声長にほぼ比例。（本行は上記実測値からの当席の算術。原報告に同じ数字の記載は無い＝**導出値**）
- **RTF < 1 に到達したのはこの追補が初**: `probe/irodori-tts-probe-report.md:311`

### 1.4 GPU 固有の癖 ― 形状チューニングと empty_cache（追補1・最重要の運用材料）

| 癖 | 実測 | 出典行 |
|---|---|---|
| ① 未見の「音声長（潜在形状）」の初回は 4〜5 倍遅い | novel30(30字)=29.842 秒 rtf 5.45／novel50b(47字)=46.792 秒 rtf 5.37／novel80(78字)=87.525 秒 rtf 5.27 | `probe/irodori-tts-gpu-latency-raw.txt:33-35` |
| ①' 同一形状の 2 発目からは通常速度 | novel30-repeat 26.048 秒 →（再起動後）50.092 秒 → **2 発目 5.231 秒** | 同 `:36-39` |
| ② チューニング結果は**プロセス内キャッシュのみ・再起動で消える**（ディスク持続なし） | 同上（再起動後にまた 50 秒） | 同 `:37-39`, `probe/irodori-tts-probe-report.md:320-321` |
| ②' 47 字と 50 字は別形状扱い＝配信のコメント長はばらけるので**稼働初期はスパイクが混ざる** | — | `probe/irodori-tts-probe-report.md:317-319` |
| ③ 既定 `EMPTY_CACHE_INTERVAL=10` だと 10 発おきに +15 秒スパイク再発 | B-filler-6 18.876／-9 19.447／-10 19.438 秒（間の -7,-8 は 4.14 秒） | `probe/irodori-tts-gpu-latency-raw.txt:48-52` |
| ③' `EMPTY_CACHE_INTERVAL=0` にすると形状ウォーム後 **4.1 秒 ±0.05 に完全安定** | C-ec0-4〜12 が 4.065〜4.176 秒（12 連射・スパイク消滅） | 同 `:56-68` |
| ④ 内部機構（hipBLASLt/MIOpen のどちら由来か）の特定 | **未測**（運用ノブで抑止できるため採否判断に不要と判定済み） | `probe/irodori-tts-probe-report.md:326-327` |
| ⑤ ec=0 での長時間稼働の VRAM 単調増の有無 | **未測（観測残し）** | 同 `:324-325` |

### 1.5 bf16 の効果（追補3）

| 項目 | fp32 | **bf16** | 出典行 |
|---|---|---|---|
| ウォーム 10 字 | 3.7 秒 | **0.769〜0.782 秒（4.8 倍速・RTF ≈ 0.2）** | `probe/irodori-tts-ref-latency-raw.txt:37-40`, `probe/irodori-tts-probe-report.md:400` |
| VRAM（ロード後） | 6.58GB | **3.34GB（半減）** | `probe/irodori-tts-ref-latency-raw.txt:5,36` |
| 初回形状チューニング（10 字） | 数十秒級 | **5.539 秒（激減）** | 同 `:37`, `probe/irodori-tts-probe-report.md:402` |
| seed=42 決定性 | SHA256 一致 | **SHA256 一致（bf16 でも成立）**（bf16-3/-4 とも sha16=385AF4E1B2DD3C8A） | `probe/irodori-tts-ref-latency-raw.txt:39-40` |
| 起動→ロード完了（PRELOAD） | 11.1 秒 | 31.2 秒（bf16 変換分・一度きり） | 同 `:35`, `probe/irodori-tts-restart-raw.txt:27` |
| 合成後 VRAM | — | 4.37GB | `probe/irodori-tts-ref-latency-raw.txt:41` |

- **受理値は fp32／bf16 の二値のみ。fp16 は存在しない・bf16 は GPU 必須**（追補2 の「fp16/bf16 化」は誤記として追補3 で訂正済み。根拠 `inference_runtime.py:99,308` 実読）: `probe/irodori-tts-probe-report.md:394-396`
- ノブ名: `IRODORI_MODEL_PRECISION` / `IRODORI_CODEC_PRECISION`（既定 `fp32`）: `upstream/Irodori-TTS-Server/.env.example:16-17`
- 音質: 司令官が 7861 の bf16 で一次所感「問題ない」。**正式 A/B（同一 seed の fp32/bf16 聴き比べ）は未実施**: `probe/irodori-tts-probe-report.md:408-409`
- **bf16 × 参照音声の組合せ・bf16 での長文形状チューニング分布は未測**: 同 `:412-413`

### 1.6 参照音声（ボイスクローン）の挙動（追補2・P-2）

| 参照長 | 定常レイテンシ | 初回（形状チューニング込み） | 専有 VRAM（fp32） | 出典行 |
|---|---|---|---|---|
| なし（voice="none"） | 3.716 / 3.693 秒 | — | 6.58GB | `probe/irodori-tts-ref-latency-raw.txt:5-7` |
| 10 秒 | **6.228 秒（1 観測のみ）** | 42.735 秒 | 8.11〜8.29GB | 同 `:8-11` |
| 20 秒 | 8.983 / 9.039 秒 | 65.388 秒 | 10.65〜12.32GB | 同 `:12-15` |
| 30 秒（同一プロセス・第一系列） | 79.3 秒から下がらず | 86.429 秒 | 14.73〜15.43GB | 同 `:16-19` |
| 30 秒（**更地プロセス対照**） | **11.364 / 11.534 / 11.366 秒** | 102.436 秒（ロード込み） | 9.37 → 12.53GB | 同 `:26-32` |

- 定常はきれいな線形＝**約 3.7 秒 + 0.26 秒 × 参照秒**（参照 10 秒で +2.5 秒・30 秒で +7.7 秒）: `probe/irodori-tts-probe-report.md:365`
- **測定順序の交絡**: 第一系列の ref30s「79 秒から下がらない」は ec=0 で未解放 VRAM が 15.4GB まで積み上がった後の値。更地対照で 11.4 秒に決着。⇒ **ec=0（安定化ノブ）と長参照（VRAM 大食い）は相性が悪い**: 同 `:370-373`
- 長参照系は定常に入っても**時折 80 秒級のスパイクが混ざる**（更地対照でも 5 射中 1 射＝fresh30-4 が 79.464 秒）: `probe/irodori-tts-ref-latency-raw.txt:31`, 同 `:367-369`
- **参照長で生成音声の長さ・話し方も変わる**（同一本文で noref 3.92 秒／ref10s 3.2 秒／ref20s 3.44 秒／ref30s 3.28 秒）: `probe/irodori-tts-ref-latency-raw.txt:6,8,12,16`
- 参照 wav を voice 登録して合成した CPU 実測 = 17.994 秒（no_ref 比 約 +4.5 秒）: `probe/irodori-tts-voices-raw.txt:4`, `probe/irodori-tts-probe-report.md:109-110`
- 参照は**自己生成の合成音のみ**を使用（実在人物の声は不使用）。試験 voice 3 件は計測後削除済み: 同 `:352-354`, `probe/irodori-tts-ref-latency-raw.txt:20-23`
- **VRAM の含意**: fp32 × 参照 30 秒 = 専有 9.4〜15.4GB ＝ RTX 4080(16GB) では「載るが余裕なし」。10〜20 秒参照なら fp32 でも 8〜11GB: `probe/irodori-tts-probe-report.md:376-381`

### 1.7 chunking の挙動

| 事実 | 値／内容 | 出典 |
|---|---|---|
| 既定 | `chunking_enabled=true` / `chunk_min_chars=80` / `first_sentence_chunk_min_chars=null` | `probe/irodori-tts-health.json:27-32`, `upstream/Irodori-TTS-Server/.env.example:43-45` |
| 分割の境界文字 | `frozenset("。、，,．.!！?？\n\r")`（読点も境界） | `upstream/.../app.py:30` |
| 分割規則 | 境界文字に当たった時点で**非空白の累積文字数 ≥ min_chars** なら切る。末尾は tail として 1 チャンク。結果が空なら原文 1 本 | `upstream/.../app.py:606-639` |
| 先頭文だけ別閾値 | `first_sentence_chunk_min_chars` を設定すると**最初のチャンクだけ短く切れる**＝初音までを早める公式ノブ（既定 unset） | `upstream/.../app.py:615,624-627`, `.env.example:45` |
| 連結の仕方 | `torch.cat(...)` で**単純連結。クロスフェード無し・無音挿入無し** | `upstream/.../app.py:675` |
| 複数チャンクは | **1 チャンクずつ直列**に `_synthesize_once`（並列化されていない） | `upstream/.../app.py:654-662` |
| **含意（当席の導出・未確認）** | P-4 の 200 字系（103.9 秒 CPU／40.6 秒 GPU）は `chunk_min_chars=80` 既定下＝**分割済みの直列合成**の値。1 発の大形状ではない可能性が高い。probe 側にチャンク数のログ照合は無い | ― |
| chunking の実射検証 | **未測**（probe に chunk 数・境界の耳検分・chunking=false との A/B は無い） | ― |

### 1.8 パラメータの効き（P-5）

| パラメータ | 実測 | 出典 |
|---|---|---|
| `speed` 0.25 | 音声長 15.68 秒（4 倍化）・所要 36.640 秒 | `probe/irodori-tts-params-raw.txt:2` |
| `speed` 1.0（基準） | 音声長 3.92 秒 | `probe/irodori-tts-latency-raw.txt:2` |
| `speed` 4.0 | 音声長 0.96 秒（1/4 化）・所要 8.216 秒 | `probe/irodori-tts-params-raw.txt:3` |
| `speed` 5.0 | **422**（`'msg': 'Input should be less than or equal to 4', 'ctx': {'le': 4.0}`）＝クランプではなくエラー | 同 `:4` |
| `speed` 0.1 | **422**（`'Input should be greater than or equal to 0.25', 'ctx': {'ge': 0.25}`） | 同 `:5` |
| speed の実装 | 「duration_scale / speed」の写像＝**生成時の長さ予測を縮める方式・後段リサンプルではない**（app.py 実読） | `probe/irodori-tts-probe-report.md:155-157` |
| `num_steps` 20 vs 40 | 9.215 秒 vs 13.860 秒（**34% 高速化**）・音声長は同一 3.92 秒・バイト列は別 | `probe/irodori-tts-params-raw.txt:6-7` |
| `cfg_scale_caption` 2.0 / 6.0 | 13.557 / 13.851 秒＝**所要ほぼ不変**・音声長同一・F0 は 228.6 / 224.3Hz（器械上は微差） | 同 `:8-9`, `probe/irodori-tts-wav-metrics.txt:14-15` |
| `cfg_scale_caption` の既定 | Server は **`default_cfg_scale_text`(=3.0) を流用**（`.env.example` に caption 用の欄が無い）。7861 Gradio の既定 4 とは**不一致** | `upstream/.../app.py:932-939`, `.env.example:38`, `probe/irodori-tts-probe-report.md:163-165` |
| **耳検分（音質差）** | **未測**（実装席は聴取不能。材料 wav は `C:\yomiwakesozai\irodori-probe-wavs\`） | `probe/irodori-tts-probe-report.md:160,162` |

### 1.9 caption・異常系・決定性・並列・起動（P-6〜P-9・再起動フェーズ）

| 項目 | 実測 | 出典 |
|---|---|---|
| caption 上限 | **max_caption_len = 512 トークン**（文字ではない・model.safetensors ヘッダの config_json 直読）。max_text_len = 256 トークン | `probe/irodori-tts-probe-report.md:187-188` |
| caption 超過 | 600 字 caption → **200・黙って切詰め**（13.841 秒・4.0 秒音声。tokenizer の max_length 打ち切り・エラーにならない） | `probe/irodori-tts-caption-raw.txt:2`, 同 `:189-190` |
| caption 空文字 | **400**「`caption must be non-empty when specified.`」 | `probe/irodori-tts-caption-raw.txt:3` |
| caption 省略 | **200**（3.64 秒・11.878 秒）＝無条件の声 | 同 `:4` |
| caption 特殊文字（引用符・改行・絵文字） | 200・正常 | 同 `:5`, 同 `:193` |
| caption 安定性（10 連射・seed なし） | 音声長 **10 本とも 3.8 秒で完全一致**・sha16 は毎回別・F0 中央値 194.3〜241.2Hz（中央 219Hz・±11%） | `probe/irodori-tts-caption-raw.txt:6-15`, `probe/irodori-tts-wav-metrics.txt:1-10` |
| 入力 空文字 | **422**（`string_too_short` / `min_length: 1`） | `probe/irodori-tts-anomaly-raw.txt:2` |
| 入力 空白のみ | **400**「`input must contain non-whitespace text.`」 | 同 `:3` |
| 入力 記号のみ「、、、。。。！？…」 | **200**・5.56 秒の音声（**無音ではない**） | 同 `:4` |
| 入力 絵文字のみ「😀🎉😭」 | **200**・5.36 秒の非言語音・peak=1.0 | 同 `:5`, `probe/irodori-tts-wav-metrics.txt:17` |
| 入力 改行入り | 200・6.52 秒・正常 | `probe/irodori-tts-anomaly-raw.txt:6` |
| ⇒ 含意 | **「読み上げ可能文字なし→スキップ」の判断は本体側の責務**（サーバは記号だけでも喋る） | `probe/irodori-tts-probe-report.md:179-180` |
| 決定性 seed=42 | 2 射とも sha16=`1FEC07267F3B128D`＝**SHA256 完全一致** | `probe/irodori-tts-determinism-raw.txt:2-3` |
| 決定性 seed 未指定 | 2 射で別（`681D…` / `DD81…`）・音声長のみ同一 | 同 `:4-5` |
| 並列 同時 2 | 両方 200・**完全直列**（1 発目 13.648 秒／2 発目 24.853 秒） | `probe/irodori-tts-parallel-raw.txt:4-5` |
| 並列 10 連投 | 全部 200・**503 なし**・完了が 13.607 → 115.097 秒の階段（+約 11.2 秒/射） | 同 `:8-17` |
| 直列の機構 | `IRODORI_MAX_CONCURRENT_SYNTHESIS=1`（既定） | `upstream/.../.env.example:23`, `probe/irodori-tts-health.json:19` |
| メモリ（連射前後） | WS 8699 → 8670MB・Private 12557 → 12564MB＝**増加なし（リークの気配なし）** | `probe/irodori-tts-parallel-raw.txt:2,18` |
| 未起動時の接続拒否 | 127.0.0.1 = 2.012/2.010 秒／localhost = 4.263/4.016 秒／[::1] = 2.016/2.008 秒 | `probe/irodori-tts-restart-raw.txt:13-15` |
| 起動 → API 応答（遅延ロード既定） | **2.7 秒**（loaded=false） | 同 `:37` |
| 起動 → API 応答＝ロード済み（PRELOAD=true） | **11.1 秒**（api-up 時点で loaded=True） | 同 `:27-28` |
| 真のコールド初回合成（DL 済み・遅延ロード・10 字） | 23.747 / 23.250 秒 | 同 `:16,21` |
| PRELOAD 済み初回合成（10 字・127.0.0.1） | **12.273 秒**＝ウォームと同等（コールドペナルティ消滅） | 同 `:29` |
| ロード中に届いた 2 発目 | **エラーにならず待たされて 200**（29.087 秒・503 なし） | 同 `:17` |
| 初回モデル DL 混みのコールド | 70.9 秒（HF からの DL 混入・別物として扱う） | `probe/irodori-tts-probe-report.md:133,143-144` |
| ディスク荷（CPU 構成） | venv 1.21GB ＋ モデル v4-Small 2.86GB ＋ コーデック 0.40GB | 同 `:96` |
| ディスク荷（ROCm 追加） | `.venv-rocm` **4.64GB**（rocm-sdk 同梱 torch 込み） | 同 `:345` |
| HF キャッシュ | v4-Small と v4.1-Small が**各 2.86GB** 並存 | 同 `:58` |

### 1.10 「失敗した事」「未測」の一覧（⒜⒝⒞の見積もりの穴になる箇所）

| # | 失敗／未達 | 内容 | 出典 |
|---|---|---|---|
| F-1 | **Windows で ROCm が効かず CPU 合成だった**（最重要の食い違い） | 8088 の venv の torch が `2.10.0+cpu`・`torch.cuda.is_available()=False`。7861 も同じ | `probe/irodori-tts-probe-report.md:28-35` |
| F-2 | 依頼文出典の欠落 | `docs/irodori-tts-survey.md` がリポジトリに存在しない（当時） | 同 `:40-45` |
| F-3 | 再起動スクリプトの「API NOT UP in 180s」×3 | **計測アーティファクト**（localhost × 短タイムアウト）。サーバは毎回正常起動していた | `probe/irodori-tts-restart-raw.txt:11,20,25`, 同 `:260-261` |
| F-4 | sentencepiece 0.1.99 がソースビルド落ち | cp312 wheel が無い → 0.2.1 に差し替えて回避 | 同 `:290-292`, `probe/irodori-tts-rocm-setup-guide.md:86-90` |
| F-5 | GPU のコールドが CPU より遅い | 40.8〜64.7 秒 vs CPU 23.3〜23.7 秒（形状チューニングのため） | 同 `:307` |
| F-6 | ref30s 第一系列が「79 秒から下がらない」 | 測定順序の交絡（VRAM 圧）。更地対照で 11.4 秒に決着 | `probe/irodori-tts-ref-latency-raw.txt:16-19,28-32`, 同 `:370-373` |
| U-1 | `response_format=pcm` の実射 | 未測 | 同 `:122-123` |
| U-2 | `IRODORI_DEFAULT_VOICE=none` での voice 省略 | 未測（既定稼働のまま） | 同 `:104-105` |
| U-3 | `stream_format="sse"` の実射 | **probe に記載なし＝未測**（当席の全文検索でも該当ゼロ） | ― |
| U-4 | 「待ち超過 503」の実射 | 条件未達で未観測（同サイズなら同時 26 発超で発生する計算） | 同 `:208-210` |
| U-5 | 配信同時負荷（OBS＋ゲーム）での再現 | 未測 | 同 `:212-214` |
| U-6 | bf16 の正式 A/B 音質検分・bf16×参照・bf16 長文形状分布 | 未測 | 同 `:408-409,412-413` |
| U-7 | NVIDIA(CUDA) 実機の実測 | **本機に NVIDIA なし＝未測** | 同 `:387-388`、`BRIEF.md` §2-3 と整合 |
| U-8 | ec=0 長時間稼働での VRAM 単調増 | 未測 | 同 `:324-325` |
| U-9 | chunking の実射（チャンク数・境界の耳検分・on/off の A/B） | 未測 | ― |
| U-10 | 7861 と 8088 の同時合成 | 読み取り限定制約により未測 | 同 `:93-95` |

---

## 2. 「Windows で ROCm が効かず CPU 合成だった」経緯と、rocm-setup-guide の解決

### 2.1 時系列

| 段 | 事実 | 出典 |
|---|---|---|
| ① 前提 | 依頼文の追記は「静かな CPU フォールバック」を警戒していた | `probe/irodori-tts-probe-report.md:34` |
| ② 実測 | 8088 の venv の torch = **`2.10.0+cpu`**（cuda/hip/rocm すべて None・`torch.cuda.is_available()=False`）。7861 側も同じ | 同 `:28-33` |
| ③ 原因 | **導入経路そのもの**＝Irodori-TTS-Server の `pyproject.toml` が pytorch-rocm index に `sys_platform == 'linux'` マーカーを張っている。README にも "the `rocm` extra uses the PyTorch ROCm index **on Linux**" と明記。⇒ **Windows では rocm エクストラ指定が torch に効かず CPU wheel が入る** | 同 `:30-33` |
| ④ 判定の強さ | GPU カウンタ目視ではなく **torch ビルド直読**で確定＝「GPU 合成は構造的に不可」 | 同 `:90-92` |
| ⑤ 帰結 | P-4 の全数値が「CPU 実測」であり GPU 実効時の姿ではない＝**レイテンシの前提が全部変わる** | 同 `:34-35` |
| ⑥ 分ける観測 | 「Windows で gfx1151 向け ROCm torch wheel を入れて `torch.cuda.is_available()` が True になるか」の 1 点 | 同 `:36-38` |
| ⑦ 解決（同日・司令官下命） | AMD 公式安定チャンネルの wheel で `.venv-rocm` を並設 → `True AMD Radeon(TM) 8060S Graphics`・合成中 GPU compute 60〜104%＝**ROCm 実効** | 同 `:286-297`, `probe/irodori-tts-gpu-latency-raw.txt:6-14` |
| ⑧ 到達点 | **RTF 0.87〜1.06（fp32）→ bf16 で RTF ≈ 0.2** | 同 `:311,400` |

### 2.2 rocm-setup-guide が使った具体（版・index・場所）

| 要素 | 逐語 | 出典 |
|---|---|---|
| index（AMD 公式） | `https://stable.repo.amd.com/rocm/whl-next/` | `probe/irodori-tts-rocm-setup-guide.md:69` |
| torch | `torch[device-gfx1151]==2.13.0+rocm10.0.0` | 同 `:69` |
| torchaudio | `torchaudio==2.11.0.2+rocm10.0.0` | 同 `:69` |
| index 戦略 | `--index-strategy unsafe-best-match` | 同 `:69` |
| ROCm SDK | **別途インストール不要（この torch に同梱）** | 同 `:75`, `probe/irodori-tts-probe-report.md:289` |
| Python | **3.12 を指定**（ROCm 版 torch は 3.11〜3.14 のみ対応・サーバ付属の古い箱は 3.10 で使い回せない） | `probe/irodori-tts-rocm-setup-guide.md:57,61-62` |
| overrides | `overrides-rocm.txt` 3 行＝`torch==2.13.0+rocm10.0.0` / `torchaudio==2.11.0.2+rocm10.0.0` / `sentencepiece==0.2.1` | 同 `:84-86` |
| overrides が要る理由 | 「サーバは古い torch を要求してくる」（Server の pin は `torch<2.11`） | 同 `:79`, `probe/irodori-tts-probe-report.md:290-291` |
| torchao | irodori_tts・Server とも未参照＝**実害なし** | `probe/irodori-tts-probe-report.md:292` |
| 対応表（他 GPU） | `https://rocm.docs.amd.com/projects/ai-ecosystem/en/latest/frameworks/pytorch/install.html` | `probe/irodori-tts-rocm-setup-guide.md:74,181` |
| ダウンロード量／空き | 合計 5〜6GB・ディスク空き 15GB 以上 | 同 `:25-26` |

### 2.3 詰まり所（初心者が踏む地雷）

| 地雷 | 症状 | 回避 | 出典 |
|---|---|---|---|
| **`uv run` が旧 venv を掴む** | GPU 化したのに CPU で動く | `uv run` で起動してはいけない。`.venv-rocm\Scripts\python.exe` を直接指定 | `probe/irodori-tts-rocm-setup-guide.md:126`, `probe/irodori-tts-probe-report.md:295` |
| **uv 導入直後にパスが通らない** | 「uv が見つからない」 | **ターミナルを開き直す** | `probe/irodori-tts-rocm-setup-guide.md:39,161` |
| **Python 3.10 の箱を流用** | ROCm 版 torch が入らない | 箱を分けて `--python 3.12` | 同 `:57,61-62` |
| **sentencepiece 0.1.99** | 手順 7 でソースビルド落ち | overrides 3 行目 `sentencepiece==0.2.1` | 同 `:86,89-90,160` |
| **既定の pin（torch<2.11）** | 新 torch が蹴られる | `--overrides overrides-rocm.txt` | 同 `:99` |
| **`--no-sync` を外す** | CPU 側起動で backend エクストラが落ちる | CPU 起動は `uv run --no-sync` 固定 | `probe/irodori-tts-probe-report.md:262-264` |
| **`localhost` で叩く** | 毎リクエスト +1.2〜2.0 秒 | **127.0.0.1 明示** | 同 `:255-257` |
| **EMPTY_CACHE_INTERVAL 既定 10** | 10 発おきに +15 秒 | `IRODORI_EMPTY_CACHE_INTERVAL=0` | `probe/irodori-tts-rocm-setup-guide.md:128-129` |
| **PRELOAD 既定 false** | 初回合成が +11 秒 | `IRODORI_PRELOAD=true` | 同 `:130-131` |
| **bat の改行コード（CRLF）** | ― | **guide に記載なし。当席の全文検索でも CRLF への言及ゼロ＝「詰まった記録は無い」**（依頼文の想定に対する回答: **未確認**。guide は `.bat` 化を「上の 5 行をメモ帳に貼って保存」としか書いていない） | 同 `:132-133` |
| **AMD ドライバが古い** | 手順 8 で `False` | Adrenalin を公式最新に更新して PC 再起動 | 同 `:159` |
| **壊した** | ― | `.venv-rocm` を丸ごと削除して手順 4 からやり直し（他に影響なし） | 同 `:163` |

### 2.4 初心者が同じ手順を踏む場合の操作列（1 行 1 操作・道具札つき）

すべて `probe/irodori-tts-rocm-setup-guide.md` の写し。行番号は同ファイル。

| # | 操作（1 行） | 道具の札 | 行 |
|---|---|---|---|
| 1 | AMD Adrenalin ドライバが入っていることを確認（不安なら公式最新を入れ直す） | **ドライバ** | 27-30 |
| 2 | ディスク空き 15GB 以上・DL 5〜6GB 分の回線を確保 | **前提** | 25-26 |
| 3 | `winget install astral-sh.uv` | **uv** | 36 |
| 4 | ターミナルを閉じて開き直す | **uv（PATH）** | 39 |
| 5 | `git clone https://github.com/Aratako/Irodori-TTS-Server.git C:\irodori-TTS-server\Irodori-TTS-Server` | **git** | 46 |
| 6 | （git が無ければ）`winget install Git.Git` → ターミナル開き直し | **git** | 49 |
| 7 | `cd C:\irodori-TTS-server\Irodori-TTS-Server` | **シェル** | 56 |
| 8 | `uv venv .venv-rocm --python 3.12` | **uv／venv** | 57 |
| 9 | `uv pip install --python .venv-rocm\Scripts\python.exe --extra-index-url https://stable.repo.amd.com/rocm/whl-next/ --index-strategy unsafe-best-match "torch[device-gfx1151]==2.13.0+rocm10.0.0" "torchaudio==2.11.0.2+rocm10.0.0"` | **uv／torch index（AMD 公式）／ROCm SDK 同梱** | 69 |
| 10 | メモ帳で `overrides-rocm.txt` を作り 3 行（torch／torchaudio／sentencepiece==0.2.1）を保存 | **overrides ファイル** | 80-87 |
| 11 | `uv pip install --python .venv-rocm\Scripts\python.exe --overrides overrides-rocm.txt --extra-index-url https://stable.repo.amd.com/rocm/whl-next/ --index-strategy unsafe-best-match -e .` | **uv／overrides／torch index** | 99 |
| 12 | `.venv-rocm\Scripts\python.exe -c "import torch; print(torch.cuda.is_available(), torch.cuda.get_device_name(0))"` | **検証（torch）** | 109 |
| 13 | 出力が `True AMD Radeon(TM) 8060S Graphics` であることを確認 | **検証** | 112 |
| 14 | `cd C:\irodori-TTS-server\Irodori-TTS-Server` | **シェル** | 120 |
| 15 | `set IRODORI_EMPTY_CACHE_INTERVAL=0` | **環境変数** | 121 |
| 16 | `set IRODORI_PRELOAD=true` | **環境変数** | 122 |
| 17 | `.venv-rocm\Scripts\python.exe -m irodori_openai_tts --host 0.0.0.0 --port 8088`（**`uv run` は使わない**） | **起動（venv 直指定）** | 123,126 |
| 18 | （任意）手順 14-17 の 5 行をメモ帳に貼って `irodori-gpu-server.bat` として保存 | **bat** | 132-133 |
| 19 | 別ターミナルで `curl -X POST http://127.0.0.1:8088/v1/audio/speech ... -o test.wav` を実行し再生確認 | **検証（curl）** | 142 |
| 20 | 停止は Ctrl+C（またはターミナルを閉じる） | **シェル** | 135 |
| 21 | CPU に戻すときは `uv run --no-sync python -m irodori_openai_tts --host 0.0.0.0 --port 8088` | **uv（退路）** | 171 |

- **初心者向けの目安（guide が明記）**: 10 文字 ≒ 4 秒・50 文字 ≒ 10 秒・200 文字 ≒ 41 秒・GPU メモリ 6.6GB: 同 `:152-153`
- **「故障ではない」と guide が予告する挙動**: 初めての長さの文だけ 4〜5 倍遅い／再起動でリセット: 同 `:149-151`
- **⒜（専用インストーラ）の観点での棚卸し**: 21 操作のうち **1〜18 の 18 操作**は同梱／自動化で消せる性質のもの（uv 導入・clone・venv・pip・overrides・env・bat）。**消えないのは 1（ドライバ）・2（空き容量）・19-20（動作確認と停止）**。ただし手順 9 の `[device-gfx1151]` は**機種依存の指定**であり、インストーラは GPU を判別して差し替えるか、対応 GPU を限定する必要がある（同 `:23-24,73-74`）。

---

## 3. API 契約（openapi.json 実測）

- 出典は `probe/irodori-tts-openapi.json`（1 行 JSON）。行番号は `python -m json.tool` で整形した版の行（当席スクラッチ `openapi-pretty.json`・全 1082 行）。整形は決定的なので再現可能。
- タイトル: `"Irodori-TTS OpenAI-compatible API"` / version `"0.1.0"` / openapi `"3.1.0"`（整形 2-5 行）。

### 3.1 全エンドポイント（5 パス・7 オペレーション）

| パス | メソッド | operationId | body | 成功 | 備考 | 整形行 |
|---|---|---|---|---|---|---|
| `/health` | GET | `health_health_get` | ― | 200 (object) | **authorization ヘッダ欄すら無い＝無認証で叩ける** | 8-27 |
| `/v1/models` | GET | `list_models_v1_models_get` | ― | 200 (object) | authorization 任意 | 28-75 |
| `/v1/audio/voices` | GET | `list_voices_v1_audio_voices_get` | ― | 200 (object) | 一覧 | 76-122 |
| `/v1/audio/voices` | POST | `upload_voice_v1_audio_voices_post` | **multipart/form-data**（`file` 必須・`voice_id` 任意 string\|null） | **201** | 登録 | 123-174, 424-448 |
| `/v1/audio/voices/{voice_id}` | GET | `get_voice_file_v1_audio_voices__voice_id__get` | ― | 200 (object) | メタ取得（実応答は JSON メタ） | 177-231 |
| `/v1/audio/voices/{voice_id}` | PUT | `replace_voice_v1_audio_voices__voice_id__put` | **multipart/form-data**（`file` 必須のみ） | 200 | 差し替え | 232-296, 410-423 |
| `/v1/audio/voices/{voice_id}` | DELETE | `delete_voice_v1_audio_voices__voice_id__delete` | ― | 200 (object) | 削除 | 297-351 |
| `/v1/audio/speech` | POST | `create_speech_v1_audio_speech_post` | **application/json** → `SpeechRequest` | 200（**schema 空 `{}` ＝バイナリ**） | 本命 | 353-406 |

- 全オペレーション共通で `authorization` ヘッダが **`in: header` / `required: false` / `string \| null`**（例 357-373 行）＝ `IRODORI_API_KEY` 未設定なら無認証（`upstream/.../.env.example:6-7`）。
- **FastAPI の `/docs` も 200 で存在**する（openapi には現れない）: `probe/irodori-tts-probe-report.md:84-85`
- `probe` 実測で `/openapi.json` 自体も 200: 同 `:13`

### 3.2 `SpeechRequest`（トップレベル・整形 972-1039 行）

| 欄 | 型 | 必須 | 既定（openapi 上） | 制約 | 整形行 |
|---|---|---|---|---|---|
| `model` | string | **必須** | ― | ― | 974-977 |
| `input` | string | **必須** | ― | **`minLength: 1` / `maxLength: 4096`** | 978-983 |
| `voice` | `string \| object \| null` | 任意 | 無し（null 可） | object の場合は **`.id` しか見ない** | 984-998 |
| `response_format` | `string \| null` | 任意 | 無し（サーバ側 env で `wav`） | コードの許容 6 種 | 999-1009 |
| `speed` | number | 任意 | **`default: 1.0`（openapi に明記された唯一の既定）** | **`minimum: 0.25` / `maximum: 4.0`** | 1010-1016 |
| `stream_format` | `string \| null` | 任意 | 無し | **`"sse"` のみ許容**（他は 400） | 1017-1027 |
| `irodori` | `$ref IrodoriOptions` | 任意 | ― | ― | 1028-1030 |

- **`additionalProperties: true`**（整形 1032 行）＝ **未知の欄を投げても弾かれない**。さらにサーバは `_extra(payload, "…")` でトップレベルの余剰欄も irodori 欄の代替として拾う（`upstream/.../app.py:427-437, 925-939`）＝ **`irodori` ネストに入れずフラットに書いても効く**。
- `required: ["model", "input"]`（整形 1034-1037 行）。
- `voice` object の解釈（コード）: `voice.get("id")` のみ。**参照音声を inline object で渡す口ではない**（`upstream/Irodori-TTS-Server/src/irodori_openai_tts/voices.py:158-165`）。
- `voice` 文字列の特別扱い: `NO_REF_IDS = {"none", "no_ref", "no-ref", "null", "text-only"}` のいずれかで no-ref 合成（`allow_no_ref_voice=true` 時）: `upstream/.../voices.py:23,80-81`
- 未知 voice のエラー文言（逐語）: `Unknown voice={voice_id!r}. Put a reference audio file in {voices_dir}, add an alias file, or use voice='none'.`（`upstream/.../voices.py:91-94`）
- voice 未指定のエラー文言（逐語）: `No voice was provided and IRODORI_DEFAULT_VOICE is not set.`（`upstream/.../voices.py:77`。probe 実射も同文言: `probe/irodori-tts-probe-report.md:103-104`）

### 3.3 `IrodoriOptions`（整形 462-971 行・**全 44 欄**）

openapi 上は**全欄 `anyOf[<型>, null]` で既定値の記載が一つも無い**。既定は `.env.example` かコード側にある（3.4）。列挙・制約が openapi に出るのは 3 欄のみ（enum）。

| # | 欄 | 型 | enum／備考 | 既定（env 由来） | 整形行 |
|---|---|---|---|---|---|
| 1 | `caption` | string\|null | **感情・声質の自然文プロンプト** | 無し（省略＝無条件） | 464-474 |
| 2 | `ref_wav` | string\|null | **サーバ側パス文字列**（バイト列不可） | ― | 475-485 |
| 3 | `ref_wavs` | string[]\|null | 複数参照（表示順連結） | ― | 486-499 |
| 4 | `ref_latent` | string\|null | 事前抽出の潜在 | ― | 500-510 |
| 5 | `ref_latents` | string[]\|null | 同上・複数 | ― | 511-524 |
| 6 | `ref_embed` | string\|null | 話者埋め込み | ― | 525-535 |
| 7 | `no_ref` | boolean\|null | **true なら voice レジストリを迂回** | ― | 536-546 |
| 8 | `seconds` | number\|null | 生成秒数の直接指定（空＝auto） | ― | 547-557 |
| 9 | `duration_scale` | number\|null | 長さ倍率（**speed の実体**） | `1.0` | 558-568 |
| 10 | `min_seconds` | number\|null | ― | ― | 569-579 |
| 11 | `max_seconds` | number\|null | ― | ― | 580-590 |
| 12 | `max_ref_seconds` | number\|null | 参照の切り詰め上限 | **unset 時 v4 Small は 120 秒** | 591-601 |
| 13 | `ref_normalize_db` | number\|null | 参照の音量正規化 | ― | 602-612 |
| 14 | `ref_ensure_max` | boolean\|null | ― | ― | 613-623 |
| 15 | `num_steps` | integer\|null | 拡散ステップ数（**速度と直結**） | `40` | 624-634 |
| 16 | `t_schedule_mode` | string\|null | **enum: `linear` / `sway`** | `linear` | 635-649 |
| 17 | `sway_coeff` | number\|null | ― | `-1.0` | 650-660 |
| 18 | `num_candidates` | integer\|null | 候補数（7861 UI は 1〜32） | ― | 661-671 |
| 19 | `decode_mode` | string\|null | **enum: `sequential` / `batch`** | ― | 672-686 |
| 20 | `cfg_scale_text` | number\|null | 本文への追従強度 | `3.0` | 687-697 |
| 21 | `cfg_scale_caption` | number\|null | **caption（感情）への追従強度** | **`3.0`（text の既定を流用）** | 698-708 |
| 22 | `cfg_scale_speaker` | number\|null | 話者への追従強度 | `5.0` | 709-719 |
| 23 | `cfg_guidance_mode` | string\|null | **enum: `independent` / `joint` / `alternating`** | `independent` | 720-735 |
| 24 | `cfg_scale` | number\|null | 一括指定 | ― | 736-746 |
| 25 | `cfg_min_t` | number\|null | CFG 適用の時刻下限 | ― | 747-757 |
| 26 | `cfg_max_t` | number\|null | 同上限 | ― | 758-768 |
| 27 | `truncation_factor` | number\|null | ― | ― | 769-779 |
| 28 | `rescale_k` | number\|null | ― | ― | 780-790 |
| 29 | `rescale_sigma` | number\|null | ― | ― | 791-801 |
| 30 | `context_kv_cache` | boolean\|null | ― | ― | 802-812 |
| 31 | `speaker_kv_scale` | number\|null | ― | ― | 813-823 |
| 32 | `speaker_kv_min_t` | number\|null | ― | ― | 824-834 |
| 33 | `speaker_kv_max_layers` | integer\|null | ― | ― | 835-845 |
| 34 | `seed` | integer\|null | **決定性の鍵**（固定で SHA256 一致） | 無し（毎回ランダム） | 846-856 |
| 35 | `trim_tail` | boolean\|null | 末尾の無音刈り | ― | 857-867 |
| 36 | `tail_window_size` | integer\|null | ― | ― | 868-878 |
| 37 | `tail_std_threshold` | number\|null | ― | ― | 879-889 |
| 38 | `tail_mean_threshold` | number\|null | ― | ― | 890-900 |
| 39 | `max_text_len` | integer\|null | 本文トークン上限（CP 実値 **256**） | CP 由来 | 901-911 |
| 40 | `max_caption_len` | integer\|null | caption トークン上限（CP 実値 **512**） | CP 由来 | 912-922 |
| 41 | `lora_adapter` | string\|null | **リクエスト単位の LoRA 差し替え** | ― | 923-933 |
| 42 | `chunking_enabled` | boolean\|null | 分割 ON/OFF | `true` | 934-944 |
| 43 | `chunk_min_chars` | integer\|null | 分割の最小文字数 | `80` | 945-955 |
| 44 | `first_sentence_chunk_min_chars` | integer\|null | **先頭文だけ別閾値＝初音短縮ノブ** | unset（null） | 956-965 |

- `IrodoriOptions` も **`additionalProperties: true`**（整形 968 行）。
- 7861 Gradio UI が露出する範囲（対照）: Num Steps 40 [1–120]・Num Candidates 1 [1–32]・Seed 空=random・Seconds 空=auto・Duration Scale 1 [0.5–1.5]・Time Schedule linear・Sway Coeff -1・CFG Guidance Mode independent・CFG Scale Text 3・**CFG Scale Caption 4**・CFG Scale Speaker 5: `probe/irodori-tts-probe-report.md:66-70`
  ⇒ **Gradio の CFG Scale Caption 既定 4 と Server の 3 は不一致**（同 `:163-165`）。

### 3.4 既定値の出所（openapi に無いもの＝サーバ側 env）

`.env.example` の逐語（`upstream/Irodori-TTS-Server/.env.example`）:

| 行 | 逐語 | 意味 |
|---|---|---|
| 2-3 | `IRODORI_HOST=0.0.0.0` / `IRODORI_PORT=8088` | 待受 |
| 5 | `IRODORI_TTS_BACKEND=cu128` | **Docker build backend: cu128, rocm, or cpu.**（4 行目のコメント逐語） |
| 7 | `# IRODORI_API_KEY=change-me` | 逐語コメント: `Optional. When set, clients must send Authorization: Bearer <value>.`（6 行目） |
| 10 | `IRODORI_HF_CHECKPOINT=Aratako/Irodori-TTS-v4-Small` | 既定 CP（**v4 のまま・カードは v4.1 推奨**） |
| 12 | `IRODORI_CODEC_REPO=Aratako/Semantic-DACVAE-Japanese-32dim` | コーデック |
| 14-15 | `IRODORI_MODEL_DEVICE=auto` / `IRODORI_CODEC_DEVICE=auto` | **デバイス選択（⒞の GPU 指定の入口。詳細は別席）** |
| 16-17 | `IRODORI_MODEL_PRECISION=fp32` / `IRODORI_CODEC_PRECISION=fp32` | **bf16 の入口** |
| 19-20 | `IRODORI_COMPILE_MODEL=false` / `IRODORI_COMPILE_DYNAMIC=false` | 逐語コメント: `Keep compile disabled when using per-request irodori.lora_adapter.`（18 行目） |
| 21 | `IRODORI_PRELOAD=false` | 遅延ロード既定 |
| 22-24 | `IRODORI_MODEL_LOAD_TIMEOUT=300` / `IRODORI_MAX_CONCURRENT_SYNTHESIS=1` / `IRODORI_SYNTHESIS_WAIT_TIMEOUT=300` | 直列 1・待ち 300 秒 |
| 25 | `IRODORI_EMPTY_CACHE_INTERVAL=10` | **スパイクの元凶。実測では 0 が正** |
| 28-30 | `IRODORI_VOICES_DIR=voices` / `# IRODORI_DEFAULT_VOICE=sample` / `IRODORI_ALLOW_NO_REF_VOICE=true` | voice 既定 |
| 33-40 | `IRODORI_DEFAULT_RESPONSE_FORMAT=wav` / `NUM_STEPS=40` / `T_SCHEDULE_MODE=linear` / `SWAY_COEFF=-1.0` / `DURATION_SCALE=1.0` / `CFG_SCALE_TEXT=3.0` / `CFG_SCALE_SPEAKER=5.0` / `CFG_GUIDANCE_MODE=independent` | 合成既定 |
| 41-42 | `# Unset uses the checkpoint recommendation (120 seconds for v4 Small; 30 seconds for legacy checkpoints).` / `# IRODORI_DEFAULT_MAX_REF_SECONDS=120` | 参照上限の逐語 |
| 43-45 | `IRODORI_DEFAULT_CHUNKING_ENABLED=true` / `IRODORI_DEFAULT_CHUNK_MIN_CHARS=80` / `# IRODORI_DEFAULT_FIRST_SENTENCE_CHUNK_MIN_CHARS=1` | chunking 既定 |

- **`IRODORI_DEFAULT_CFG_SCALE_CAPTION` は `.env.example` に存在しない**＝caption 用 CFG は env で動かせず、コードが `default_cfg_scale_text` を流用（`upstream/.../app.py:932-939`）。**caption の効き具合をサーバ既定で変えたいなら、リクエストごとに `cfg_scale_caption` を送るしかない**（⒞の材料）。
- 稼働実機の /health 実応答（既定どおりで稼働していた証拠）: `model_device:"auto"` `codec_device:"auto"` `model_precision:"fp32"` `codec_precision:"fp32"` `compile_model:false` `preload:false` `load_timeout:300.0` `max_concurrent_synthesis:1` `synthesis_wait_timeout:300.0` `response_format:"wav"` `chunking_enabled:true` `chunk_min_chars:80` `first_sentence_chunk_min_chars:null`: `probe/irodori-tts-health.json:5-32`

### 3.5 ストリーミング（`stream_format="sse"`）― probe 未測・コード確認済み

| 事実 | 内容 | 出典 |
|---|---|---|
| 受理値 | **`"sse"` のみ**。他は 400「`stream_format must be 'sse' when specified.`」（逐語） | `upstream/.../app.py:396-404` |
| 配信単位 | **チャンク 1 個ずつ**（`_split_text_for_speech` の結果） | `upstream/.../app.py:703-717` |
| イベント | `event: audio_chunk` に `{index, text, format, media_type, audio_base64, seed, total_to_decode}`／終端 `event: done` に `{chunks}` | `upstream/.../app.py:734-745, 769` |
| 転送 | `media_type: text/event-stream`・`Cache-Control: no-cache`・`X-Accel-Buffering: no` | `upstream/.../app.py:771-775` |
| 直列性 | チャンクごとに合成セマフォを取り直す＝**他リクエストとチャンク単位で交互配車になり得る** | `upstream/.../app.py:711-719` |
| エラー | SSE イベントとして OpenAI 型エラーを流す（`runtime_unavailable` / `invalid_request` / `stream_error`） | `upstream/.../app.py:746-762` |
| **実射** | **未測**（probe に一度も登場しない） | ― |

### 3.6 エラー応答の形（アダプタの分岐設計に直結）

| 状況 | ステータス | 形 | 出典 |
|---|---|---|---|
| pydantic 検証失敗 | **422** | `{"error":{"message":"1 validation error:\n {'type': ..., 'loc': ('body','speed'), 'msg': ..., 'ctx': {...}}\n\n File \"C:\\irodori-TTS-server\\...\\app.py\", line …","type":"invalid_request_error","param":null,"code":null}}` ＝**message にサーバの絶対パスとソース行が漏れる** | `probe/irodori-tts-params-raw.txt:4-5`, `probe/irodori-tts-anomaly-raw.txt:2` |
| 業務検証失敗 | **400** | `{"error":{"message":"input must contain non-whitespace text.","type":"invalid_request_error","param":null,"code":null}}` | `probe/irodori-tts-anomaly-raw.txt:3` |
| voice 不備 | **400** | `No voice was provided and IRODORI_DEFAULT_VOICE is not set` | `probe/irodori-tts-probe-report.md:103-104` |
| caption 空 | **400** | `caption must be non-empty when specified.` | `probe/irodori-tts-caption-raw.txt:3` |
| openapi 上の 422 | ― | `HTTPValidationError` → `ValidationError[]`（`loc`/`msg`/`type` 必須・`input`/`ctx` 任意） | 整形 449-461, 1040-1079 行 |
| **注意** | 実応答の JSON 形（`{"error":{...}}`）は **openapi のスキーマ（`HTTPValidationError`）と一致しない**＝カスタム例外ハンドラで包み直されている。**契約は openapi ではなく実測が正** | 上の 2 系を突き合わせた当席の判定 |

### 3.7 `params-raw.txt` / `models.json` が単独で示す事実

**`probe/irodori-tts-models.json`**（整形 1-11 行）:
```
{"object":"list","data":[{"id":"irodori-tts","object":"model","created":0,"owned_by":"irodori-tts"}]}
```
- モデルは **1 件のみ**・`id` は `IRODORI_MODEL_NAME`（既定 `irodori-tts`）と一致（`.env.example:13`）。
- ⇒ **`/v1/models` はチェックポイントの版（v4 か v4.1 か）を教えてくれない**。版を知るには `/health` の `hf_checkpoint` を見るしかない（`probe/irodori-tts-health.json:5`）。**読み分けちゃんのエンジン検出でモデル一覧に頼ると版差が見えない**＝重要。
- `created: 0` ＝ OpenAI 互換の飾りで、実体のタイムスタンプではない。

**`probe/irodori-tts-params-raw.txt`**（9 行・P-5 の生データ）:
- 200 応答は 1 行 JSON で `{sha16, status, contentType, ok, audioSec, bytes, label, ms}`。エラー行は `{body, ok, error, label, ms, status}` と**形が違う**（`:2-3` vs `:4-5`）。
- **`speed` は音声長を作り替える**: 0.25 → 15.68 秒／1.0 → 3.92 秒／4.0 → 0.96 秒（`:2-3` と `probe/irodori-tts-latency-raw.txt:2`）＝きっちり 4 倍／4 分の 1。
- **`num_steps` は音声長を変えない**: steps20 と steps40 が両方 `audioSec 3.92 / bytes 376364` で完全一致、sha16 だけ違う（`:6-7`）＝**バイト数まで一致＝長さ予測は steps に依存しない**。
- **`cfg_scale_caption` も音声長を変えない**: cfgcap-low2 / high6 とも `audioSec 3.92 / bytes 376364`（`:8-9`）。
- **`steps40` の sha16 `1FEC07267F3B128D` は決定性テストの `seed42-a/b` と同一**（`probe/irodori-tts-determinism-raw.txt:2-3`）＝params 系列も seed=42 固定で、**別系列・別時刻でもバイト列が完全再現している**（決定性の独立な裏づけ・当席の突き合わせ）。

---

## 4. 主席への注意（三案の評価に効く実測事実）

### 4.1 三案共通に効く事実

| # | 事実 | 効き方 | 出典 |
|---|---|---|---|
| C-1 | **Windows での CPU 転落は「利用者の操作ミス」ではなく上流の依存宣言（`sys_platform=='linux'`）由来** | 「手順書を丁寧に書く」では直らない。**⒜⒝⒞のどれを採っても、torch/実行プロバイダの選択を配布側が握るのが必須条件**。逆に言えば、この 1 点を握れば三案とも初心者導入の主要リスクが消える | `probe/irodori-tts-probe-report.md:30-33` |
| C-2 | **bf16 で 10 字 0.77 秒・VRAM 3.3GB** | 「配信リアルタイムは無理」という P-4 の印象は**環境の問題であって設計の問題ではない**。三案の速度評価はすべて bf16/GPU を基準にすべき。ただし**正式な音質 A/B は未実施**＝速度の話をするときは「音質未確定」の札が要る | 同 `:400-409` |
| C-3 | **決定性が seed 固定で fp32/GPU/bf16 のすべてで SHA256 一致** | 先読み・名前キャッシュ・事前生成という設計が**全案で成立する**。⒞の価値の相当部分がここに乗る | `probe/irodori-tts-determinism-raw.txt:2-3`, `probe/irodori-tts-ref-latency-raw.txt:39-40`, 同 `:308` |
| C-4 | **48kHz/16bit/mono PCM で、`response_format=pcm` はヘッダ無しの生バイト** | 音量スケーラ・再生系の前提は成立。ただし **peak=1.0 到達＝出力側で既に飽和**しており、乗算 >1 はクリップ。ゲインは <1 側で設計するのが安全 | 同 `:118-121`, `upstream/.../audio.py:38-42` |
| C-5 | **記号だけ・絵文字だけでも 200 で「音」が返る** | 「読み上げ可能文字なし → スキップ」は**本体側の責務**。どの案でも入力前処理レイヤは要る | 同 `:179-180` |
| C-6 | **`max_concurrent_synthesis=1` で完全直列。10 連投は 115 秒の階段** | 配信の連投は必ず詰まる。**キャンセル可能なキュー（⒞の中心機能）が案の別を問わず要る**。並列化は VRAM を食うので単純な増設では解けない | `probe/irodori-tts-parallel-raw.txt:8-17` |
| C-7 | **モデル 2.86GB ＋ コーデック 0.40GB ＋ CPU venv 1.21GB／ROCm venv 4.64GB** | 配布物見積もりの一次資料。**ROCm venv だけで CPU venv の 3.8 倍**。⒜の配布サイズはバックエンドの選び方で 5〜8GB 級に振れる | 同 `:96,345` |

### 4.2 ⒜専用インストーラに効く事実

| # | 事実 | 効き方 |
|---|---|---|
| A-1 | rocm-setup-guide の 21 操作のうち **18 操作が同梱で消せる**（§2.4）。消えないのは ドライバ確認・空き容量・動作確認・停止 | ⒜の「初心者の導入手順の長さ」は **21 → 3〜4 操作**まで縮む見込み。BRIEF §4-5 の直接材料 |
| A-2 | ただし **`[device-gfx1151]` は機種依存の指定**（他 AMD GPU では書き換えが要る・guide が対応表 URL を案内している） | ROCm を配布に入れるなら **GPU 判別 or 対応 GPU 限定**が必要。CUDA のみに割り切るなら消える問題（BRIEF §4-7 の材料） |
| A-3 | **Python 3.12 必須**（ROCm torch は 3.11〜3.14・サーバ付属の箱は 3.10）。python-embed を同梱するなら 3.12 系を選ぶ | 埋め込み配布のバージョン選定を縛る具体条件 |
| A-4 | **`overrides-rocm.txt` が要る＝上流の pin（`torch<2.11`）と新 torch が衝突している** | 上流追随の保守コスト。上流が pin を上げるまで override を持ち回る必要がある |
| A-5 | **HF からの初回モデル DL は 70.9 秒級**（本機・実測に混入） | 「モデル自動取得」の初回体験の一次見積もり。**PRELOAD=true なら起動 11.1 秒**（bf16 は 31.2 秒） |
| A-6 | **v4-Small と v4.1-Small が各 2.86GB でキャッシュ並存**・Server 既定は v4・**カードは v4.1 推奨**・7861 は v4.1 | 同梱／取得の対象版を**インストーラが決め打つ必要がある**（放置すると 2 版 5.7GB を抱える） |
| A-7 | **`uv run --no-sync` を外すと backend エクストラが落ちる**／**`uv run` は旧 venv を掴む** | インストーラは uv を隠して **venv の python.exe を直起動**すべき（guide の結論と同じ） |

### 4.3 ⒝非 Python 化（ONNX 等）に効く事実

| # | 事実 | 効き方 |
|---|---|---|
| B-1 | **未見の音声長ごとに 4〜5 倍の初回コスト・プロセス内キャッシュのみ・再起動で消える** | これは torch/ROCm のカーネル自動チューニングの挙動。**ONNX Runtime に移すとこのスパイクの姿は変わる（消えるとは限らない）**＝⒝の速度は現状の実測から外挿できない。**⒝を評価するなら「形状の動的性」が最大の論点**（BRIEF §4-2 の動的形状に直結） |
| B-2 | **num_steps=40 の反復拡散**（20 で 34% 速く・音声長は不変） | 拡散ループを ONNX に出す場合、**1 ステップをグラフ化してホスト側でループを回す**形になりやすい＝「1 グラフ 1 呼び出し」では済まない見込み（当席の所見・未確認） |
| B-3 | **参照音声は +0.26 秒/参照秒の線形コスト**・VRAM が参照長で階段的に増える | 話者エンコード段が独立して重い＝**段ごとの部分移植の境界を引く材料** |
| B-4 | **`lora_adapter` がリクエスト単位で差し替え可能**（`.env.example:18-19` は「per-request lora を使うなら compile を無効に保て」と逐語で警告） | **torch.compile と LoRA が排他**＝上流自身が静的グラフ化と動的差し替えのトレードオフを踏んでいる。ONNX 化でも同じ壁に当たる強い示唆 |
| B-5 | **SilentCipher の非可聴透かしが常時埋め込まれる** | 透かし段も移植対象。落とすと**モデルカードの Ethical Restrictions との整合が問題**になり得る（判断は卓・ライセンス席） |
| B-6 | **CPU fp32 でも RTF 2.2〜3.5 で「動きはする」**（10 字 12.3 秒） | ⒝が仮に CPU 実行を狙うなら、現状 torch CPU が基準線。**ONNX Runtime CPU がこれを大きく上回る保証はない**＝⒝の売りは「配布の軽さ」であって「速さ」ではない可能性 |

### 4.4 ⒞専用制御層に効く事実

| # | 事実 | 効き方 |
|---|---|---|
| **D-1** | **`irodori.no_ref=true` を送ると voice レジストリを迂回する分岐がコードにある**（`upstream/.../app.py:439-455`）。probe の「voice 省略は 400」は **no_ref を付けずに撃った結果**（`probe/irodori-tts-probe-report.md:103-105`） | **⒞なら voice CRUD もダミー voice も要らない可能性**が高い。⒞の設計を左右するので **1 射で確定させる価値が最も高い未確認事項**。ただし**未確認**（実射していない） |
| D-2 | **トップレベルのフラットな余剰欄も `_extra()` が拾う**（`upstream/.../app.py:427-437,925-939`）・`additionalProperties: true` | アダプタは `irodori` ネストを組み立てなくてもよい＝**薄い層の実装が想像より軽い** |
| **D-3** | **SSE ストリーミング（`stream_format="sse"`）がチャンク単位で base64 音声を逐次配信する実装済み経路**（`upstream/.../app.py:693-775`）。**probe では未測** | ⒞の「初音までの短縮」の本命。**`first_sentence_chunk_min_chars` を小さくすれば先頭文だけ短く切れる**（`.env.example:45`・`app.py:615,624-627`）＝**「最初の一文だけ早く鳴らす」が上流機能だけで組める**可能性。**⒞の価値評価が変わるので実射推奨** |
| D-4 | **`cfg_scale_caption` の既定を env で変える口が無い**（コードが `default_cfg_scale_text` を流用） | ⒞の薄い層が**リクエストごとに補う**べき筆頭パラメータ。7861 の既定 4 と Server の 3 の不一致もここで吸収できる |
| D-5 | **参照音声は「サーバ側のパス文字列」でしか渡せない**（`ref_wav` は string・inline バイト列の口なし。`voice` object も `.id` しか見ない） | ⒞がプロファイルごとの参照音声を持つなら **voices CRUD で登録する**か、**サーバと同一マシンの FS を共有する**しかない。**リモートサーバ構成では前者が必須** |
| D-6 | **voices CRUD は素直に実動**（POST 201・GET/DELETE 200・一覧に即反映・削除で初期状態に復元） | ⒞のプロファイル同期は成立する。ただし**ファイルは `voices/` に実体で残る**（`voice_id` はファイル名の stem） |
| D-7 | **`localhost` は毎回 +1.2〜2.0 秒**（IPv4 のみ listen） | ⒞のクライアントは **127.0.0.1 直打ち固定**。これだけで 10 字合成の 3 割が消える |
| D-8 | **`EMPTY_CACHE_INTERVAL=0` と長参照（VRAM 大食い）は相性が悪い**（ec=0 の推奨形は no-ref 前提の実測） | ⒞が「安定ノブを常時 0」と決め打つと、**参照音声を使うプロファイルで首を絞める**。ノブは参照長に応じて出し分ける設計が要る |
| D-9 | **起動時ウォームアップ（想定長の空撃ち）がスパイク対策の候補**（効果は未実測・実装便へ） | ⒞の「先読み」は単に前倒しするだけでなく、**形状チューニングを先に済ませる**という第二の効能を持つ。⒞独自の価値として立てられる |
| D-10 | **Gradio 経路（7861）は構造的に不利**: 1 合成 2 書き込み（`gradio_outputs_voicedesign\` へ無条件保存・**削除処理なし**＋ `%TEMP%\gradio\` へ複製）・バイト列を直接受け取る口が無い・参照音声の住まいが %TEMP%（`gradio_app_voicedesign.py:373-383`） | **⒞は Server 経路で決着させてよい**（プロトコル往復は 29ms で無罪＝速度差ではなく構造差が理由）。`probe/irodori-tts-gradio-route-raw.txt:9-27`, `probe/irodori-tts-probe-report.md:425-443` |
| D-11 | **422 の message にサーバの絶対パスとソース行が漏れる** | ⒞がエラーをユーザに見せるなら**そのまま出さない**（整形が要る）。逆に**ログには有用** |
| D-12 | **`/v1/models` は版（v4/v4.1）を教えない**・版は `/health.model.hf_checkpoint` にしかない | ⒞のエンジン検出・版差の警告は **/health を見る**設計にする。実機で 7861=v4.1・8088=v4 の不一致が現に起きている |

### 4.5 主席が引くとき「この数字は環境つき」と札を貼るべき箇所

1. **P-4 の CPU 表は localhost 経由＝約 +2 秒の下駄込み**（真値の目安は 127.0.0.1 の 12.3 秒）: `probe/irodori-tts-probe-report.md:141-142,256-259`
2. **GPU 表の 50 字/200 字の「初回」は形状チューニング**（60.4 秒・245.5 秒）で、定常値（10.1 秒・40.6 秒）と混ぜてはいけない: `probe/irodori-tts-gpu-latency-raw.txt:22-27`
3. **ref30s の「79 秒」は VRAM 圧の犠牲**で、更地対照の 11.4 秒が定常: `probe/irodori-tts-ref-latency-raw.txt:16-19,28-32`
4. **bf16 の 0.77 秒は 10 字・no-ref・ウォーム・127.0.0.1 の 4 条件込み**の値: `probe/irodori-tts-ref-latency-raw.txt:38-40`
5. **200 字の値は chunking 既定 ON（min 80 字）下の分割直列合成**である可能性が高い（当席の導出・チャンク数の実ログ照合は未実施＝**未確認**）
6. **CUDA の実測は本便に一切ない**（本機に NVIDIA なし）。BRIEF §4-7 の CUDA 側コストは**実測ではなく推論**として書く必要がある: `probe/irodori-tts-probe-report.md:387-388`

### 4.6 当席が「未確認」と札を貼ったもの（主席が引くときの注意）

| # | 事項 | 状態 |
|---|---|---|
| ? -1 | **bat の CRLF で詰まった記録** | 依頼文が想定した詰まり所だが、**guide にも probe report にも記述が無い**（当席の全文検索で言及ゼロ）。「詰まらなかった」のか「記録していない」のかは判別不能＝**未確認** |
| ? -2 | `irodori.no_ref=true` で voice 省略が通るか | **コード根拠あり・実射なし＝未確認**（D-1） |
| ? -3 | 200 字系がチャンク分割されていたか | **コード根拠あり・ログ照合なし＝未確認**（§1.7） |
| ? -4 | ONNX 化の拡散ループの形（B-2） | 当席の所見。上流読解席の判断に委ねる＝**未確認** |
| ? -5 | GPU 傾き 0.204 秒/字 | 実測値からの**当席の算術**（原報告に同数値の記載なし） |
