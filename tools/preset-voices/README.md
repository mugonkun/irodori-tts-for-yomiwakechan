# tools/preset-voices — プリセット話者 12 名の参照ボイスを作る

設計＝`docs/design/ben-p-preset-voices.md`。正典＝`decisions.md`（17・18・26・27）。

各エンジンで**一次 wav**（素の地の文）を作り、それを参照ボイスにして Irodori-TTS-Server で
「！」「？」絵文字入りの本文を合成した**二次 wav**を作る。二次がリリース版の同梱参照ボイス。

すべて **Python 3.13 標準ライブラリのみ**（`urllib`・`struct`・`array`・`subprocess`）。
外部パッケージは要らない。仮想環境も要らない。

---

## 0. 約束（停止域）

- **8088 は起こさない。** 二次生成は自前の **8090** を立てて行う（`run_secondary.py` が自動でやる）。
- `C:/IrodoriTTS/`・`C:/irodori-TTS-server/` を**書き換えない**。`.venv-rocm` の `python.exe` を*実行する*だけ。
- `PYTHONDONTWRITEBYTECODE=1` を必ず付ける（上流ツリーに `.pyc` を落とさないため）。
- `voices_dir` と `cwd` は必ず `build/out/preset-work/` 配下に向ける（`run_secondary.py` が自動でやる）。
- yomiwakechan2 の檔は 1 行も変えない。

環境変数はまとめてこう置く。

```powershell
$env:PYTHONUTF8 = "1"
$env:PYTHONDONTWRITEBYTECODE = "1"
```

---

## 1. エンジンを起こす

未起動なら自分で起動してよい。終了時は**自分が起こしたものだけ**止める。

```powershell
# VOICEVOX（エンジン単体・50021）
Start-Process "C:\Program Files\VOICEVOX\vv-engine\run.exe" -ArgumentList "--host","127.0.0.1","--port","50021" -WindowStyle Hidden

# COEIROINK（50032）
Start-Process "C:\Program Files\COEIROINK_WIN_CPU_v.2.13.0\engine\engine.exe" `
  -WorkingDirectory "C:\Program Files\COEIROINK_WIN_CPU_v.2.13.0\engine" -WindowStyle Hidden

# VOICEROID2（GUI・API 無し。gen_voiceroid2.py --launch でも起こせる）
Start-Process "C:\Program Files (x86)\AHS\VOICEROID2\VoiceroidEditor.exe"
```

VOICEROID2 だけは GUI を掴んで操作するので、**画面が前面に出る・操作中は人が触らない**こと。

起動待ちは各道具が内蔵している（`--wait` 秒・既定 VOICEVOX 180 / COEIROINK 240）ので、
すぐ次へ進んでよい。

生存確認だけしたい場合。

```powershell
curl.exe -s http://127.0.0.1:50021/version
curl.exe -s http://127.0.0.1:50032/v1/speakers | Select-Object -First 1
```

---

## 2. 一次 wav を作る

出力先は作業ディレクトリ（git 管理外）。

```powershell
$W = "C:\Users\mugonkun\source\repos\irodori-tts-for-yomiwakechan\build\out\preset-work"

python .\gen_voicevox.py  "$W\primary"
python .\gen_coeiroink.py "$W\primary"

# VOICEROID2（4 名・GUI 駆動。作業ディレクトリを渡す点だけ他と違う）
dotnet build .\Vr2SaveTool\Vr2SaveTool.csproj -c Release      # 初回だけ
python .\gen_voiceroid2.py "$W" --launch
```

HTTP の 2 つは実行時に **実機の `GET /speakers` ／ `GET /v1/speakers` で ID を照合**し、
台帳と食い違えばそこで止まる。話者 1 名につき 10 s 版と 30 s 版の 2 本を書く。

- 出力＝`<id>_10s.wav`・`<id>_30s.wav`
- 台帳＝`gen_voicevox.result.json`・`gen_coeiroink.result.json`・`gen_voiceroid2.result.json`

`gen_voiceroid2.py` は VOICEROID2 の GUI を Codeer.Friendly で駆動して［音声保存］から wav を取り出す
（詳細と、実機で踏んだ罠は設計書 §6）。1 本ごとに **ファイル名欄の二重確認・上書き確認の檔名照合・
同時出力 .txt との本文突き合わせ**を通し、どれか外れたらその 1 本を失敗にして wav を消す。所要は 8 本で約 40 s。
UI 木を見たいときは `Vr2SaveTool\bin\Release\net48\Vr2SaveTool.exe dump`（開いている窓は `dumpdlg`）。

一部だけ作り直したい場合は `--only <id>`。

```powershell
python .\gen_coeiroink.py "$W\primary" --only co_tsukuyomi
```

### 一次 wav を N: へ保全する

```powershell
$N = "N:\temp_for_claudecode_agents\irodori-ywk\preset-voices\primary"
New-Item -ItemType Directory -Force $N | Out-Null
Copy-Item "$W\primary\*" $N -Force
```

---

## 3. 諸元を確かめる

```powershell
python .\verify_wavs.py "$W\primary" --out "$W\logs\verify_primary.json"
```

Hz・ch・bit・秒・RMS・ピーク・先頭末尾の無音長・クリップの有無を出す。

行頭の印の読み方。

| 印 | 意味 |
|----|------|
| `ok` | 健全 |
| `norm` | full scale に触るサンプルが**孤立して**ある＝ピーク正規化。**異常ではない** |
| `CLIP` | full scale が**連続 3 サンプル以上**＝本当に潰れている |
| `SILENT` | 全区間が無音 |

二次 wav はサーバがピーク正規化するため全本が `norm` になる。これは正常。

---

## 4. 二次 wav を作る

`run_secondary.py` が私設 8090 サーバの起動から後片付けまで一手にやる。

```powershell
python .\run_secondary.py --also-ref10
```

やること。

1. 一次 30 s を `build/out/preset-work/voices/<id>.wav`、10 s を `<id>_ref10.wav` として複写
2. `.venv-rocm` の python で `-m irodori_openai_tts --host 127.0.0.1 --port 8090` を子起動
   （cwd＝`preset-work/server/`・`IRODORI_VOICES_DIR` は絶対パス・bf16・preload・HF offline）
3. `GET /health` の `runtime.loaded` を待つ（ROCm で実測 36 s・既定上限 600 s）
4. `no_ref` の短文で暖機 1 発（実測 3 s）
5. 各話者に `POST /v1/audio/speech`（`num_steps: 40`・`seed: 1234`）
6. 子プロセスを**ツリー kill**

出力＝`build/out/preset-work/secondary/<id>_secondary.wav`（と `_ref10_secondary.wav`）。
台帳＝`build/out/preset-work/logs/run_secondary.result.json`。
サーバのログ＝`build/out/preset-work/logs/server-8090-<時刻>.log`。

主な口。

| 引数 | 意味 |
|------|------|
| `--also-ref10` | 10 s 参照でも生成して比較用に残す |
| `--text greeting_10s` | 予備の短い挨拶本文を使う（既定は `expressive_30s`） |
| `--only <id>` | 1 名だけ作り直す |
| `--port 8090` | 使う口（既定 8090。**8088 にはしない**） |
| `--ready-timeout 600` | ready 待ちの上限秒 |
| `--keep-server` | 終了時にサーバを落とさない（調査用・自分で止めること） |

1 名あたり 25〜55 s かかる。14 本で 10 分ほど。

確かめる。

```powershell
python .\verify_wavs.py "$W\secondary" --out "$W\logs\verify_secondary.json"
```

---

## 5. 採用して成果に置く

`make_presets.py` が生成結果（`gen_*.result.json`・`verify_*.json`・`run_secondary.result.json`）を
畳んで台帳 `voices/presets.json` を書き、`--copy` を付ければ採用分を `voices/presets/<id>.wav` へ複写する。

```powershell
# 30 s 参照を採る（既定）
python .\make_presets.py --copy

# 10 s 参照を採る
python .\make_presets.py --copy --ref-variant 10s
```

台帳には 12 名全員が載る（`status` が `pending` の 4 名と `skipped` の 1 名は
`primary` / `secondary` が `null`）。形は `presets.json.schema`（JSON Schema 2020-12）。

30 s 参照と 10 s 参照の**両方を聴いて**良い方を選ぶ（席は音を聴けない＝**司令官の試聴が要る**）。
話者ごとに別の版を採りたい場合は、`--copy` で一括して置いたあと、
その 1 名だけ手で差し替えて台帳の `secondary.ref_variant` を直す。

```powershell
$R = "C:\Users\mugonkun\source\repos\irodori-tts-for-yomiwakechan"
Copy-Item "$W\secondary\co_tsukuyomi_ref10_secondary.wav" "$R\voices\presets\co_tsukuyomi.wav"
```

---

## 6. 本文を変えるとき

`corpus.json` だけを直す。道具はすべてここから読む。

- `primary.text_10s` / `text_30s`＝一次の素の地の文（感嘆符・絵文字を**入れない**）
- `secondary.expressive_30s.text`＝二次の本番本文（「！」「？」と 😊😢😆🎉 を含む・1 文を短く）
- `secondary.greeting_10s.text`＝予備の短い挨拶
- `irodori_params`＝`num_steps`（既定 40）・`seed`（既定 1234）

**秒数の目安。** Irodori の読み速は実測**約 4.6 字/秒**。VOICEVOX／COEIROINK より遅いので、
二次本文は一次より短く書く。30 s を狙うなら**約 145 字**。
（初稿の約 200 字は 37〜48 s になり目標超過だった。）

---

## 7. 未了

- **VOICEROID2 の 4 名**（琴葉茜・吉田くん・月読アイ・月読ショウタ）＝**一次 wav は 2026-09-05 に生成済み**
  （`gen_voiceroid2.py` ＋ `Vr2SaveTool/`・設計書 §6）。`run_secondary.py` の `PRIMARY_IDS` にも
  4 つの id を追加済み＝**二次 wav がまだ未生成**なので、§4 の `run_secondary.py --also-ref10` を
  もう一度回して §5 の `make_presets.py --copy` を打ち直せば 11 名が揃う。
- **CeVIO AI 弦巻マキ（英）**＝decisions 27 により今回は飛ばす。司令官が後で提供。
- **規約原文の `licenses/` 台帳**＝設計書 §7。別便へ申し送り。
