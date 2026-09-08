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

### 末尾判定（decisions 39・2026-09-05 に 3 条件へ強めた）

行末に `tail50=<末尾 50 ms の RMS> Δ=<全体 RMS との差> [clean|TAIL?] rise=<最後の 100 ms の最大立ち上がり> [why=...]`
が付く。**3 条件すべて**を満たしたときだけ `clean`。1 つでも外れたら `TAIL?`（＝`tail_suspect`）。

```
⒜ 相対   tail_delta_db = tail50_dbfs - rms_dbfs  <= -20.0
⒝ 絶対   tail50_dbfs                              <= -40.0 dBFS
⒞ 立ち上がり無し
   末尾 300 ms を 25 ms 刻みに割り（12 区間）、最後の 100 ms（4 区間）の各区間が
   直前の区間より +0.5 dB を超えて上がっていない＝単調減衰。
   ただし -60 dBFS 以下の区間は「無音」とみなし、立ち上がりに数えない。
```

自然に言い終わった音は末尾が減衰する。減衰していない＝**語の途中で終端している疑い**。

**なぜ 3 条件にしたか。** ⒜ だけの旧規則は、末尾がいったん無音に落ちてから**また鳴り出して切れる**檔を
取り逃がした。月読アイの seed 1234 版は Δ＝**−20.19 dB**（閾は −20.0）で 0.19 dB だけ通り抜けたが、
終端 0.5 s のうち 0.325 s が無音のあと −58→−36 dBFS へ立ち上がったまま檔が終わっていた。
⒝ が絶対水準（−36 dBFS は静かではない）で、⒞ が形（立ち上がり +2.68 dB）で押さえる。

窓と閾は全部口から動かせる。

| 引数 | 既定 | 効く条件 |
|------|------|----------|
| `--tail-ms` | 50 | ⒜⒝ が見る末尾の窓（ミリ秒） |
| `--tail-margin-db` | 20 | ⒜ 全体 RMS との差の閾 |
| `--tail-abs-dbfs` | -40 | ⒝ 末尾 RMS の絶対上限 |
| `--tail-profile-ms` | 300 | ⒞ 形を見る末尾の長さ |
| `--tail-step-ms` | 25 | ⒞ 刻み |
| `--tail-final-ms` | 100 | ⒞ 立ち上がりを許さない末端の長さ |
| `--tail-rise-db` | 0.5 | ⒞ 区間ごとに許す立ち上がり（測定の揺れ分） |
| `--tail-floor-dbfs` | -60 | ⒞ これ以下は無音として立ち上がりに数えない |

末尾が振幅ちょうど 0 なら `Δ=-inf`・`rise=none` で 3 条件とも満たす＝clean。
`rise=none` は「最後の 100 ms が全部 -60 dBFS 以下＝無音」の意味で、比べる相手が無かったということ。

JSON には各本に **既存の欄**（`tail_window_ms`・`tail_rms_dbfs`・`tail_delta_db`・`tail_margin_db`・
`tail_clean`・`tail_verdict`）に加えて `tail_delta_clean`・`tail_abs_dbfs`・`tail_abs_clean`・
`tail_profile_ms`・`tail_step_ms`・`tail_final_ms`・`tail_floor_dbfs`・`tail_rise_limit_db`・
**`tail_profile_dbfs`**（25 ms 刻みの並び 12 個・末尾の形を目で追える）・`tail_rise_db`・
`tail_rise_checked`・`tail_rise_clean`・`tail_fail_reasons`（`delta`/`abs`/`rise`）が入る。
台帳の頭には `tail_clean`（本数）・`tail_suspect`・`tail_suspect_files`・`tail_rule`（逐語）・
`tail_fail_by_reason` が入る。**既存の欄は 1 つも消していない。**

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
| `--text expressive_20s` | **約 20 秒ぶんに縮めた本文**（90 字）を使う。裁定 112＝司令官「3ファイルともおなじところで参照ボイスが外れるね。アプローチを変えよう。文章を20秒程度に見積もって再生成。」＝3 本とも**同じ位置**で参照から離れたので、cfg ではなく**本文の長さ**を変えた手当て。`cfg_scale_speaker` は **7.0 のまま**添える |
| `--text calm_20s` | **感情の切り替えを弱めた約 20 秒の本文**（94 字・絵文字は 😊 が 1 つだけ・「！」無し）。裁定 113 の候補 ⒜ |
| `--text plain_20s` | **感情の切り替えを無くした約 20 秒の本文**（89 字・絵文字も「！」も無い平坦な地の文）。裁定 113 の候補 ⒝ |
| `--text greeting_10s` | 予備の短い挨拶本文を使う（既定は `expressive_30s`＝146 字） |
| `--only <id>` | 1 名だけ作り直す |
| `--port 8090` | 使う口（既定 8090。**8088 にはしない**） |
| `--ready-timeout 600` | ready 待ちの上限秒 |
| `--keep-server` | 終了時にサーバを落とさない（調査用・自分で止めること） |
| `--cfg-scale-speaker <値>` | 参照ボイス（話者）への寄せ具合＝上流 `cfg_scale_speaker`。**渡さなければ上流既定 5.0**（`Irodori-TTS-Server/src/irodori_openai_tts/config.py:54`）。渡すと全射の `irodori` 欄に載り、台帳の `secondary.irodori.cfg_scale_speaker` に残る（暖機の `no_ref` には載せない）。裁定 111 は **7.0** |

1 名あたり 25〜55 s かかる。14 本で 10 分ほど（本文の長さに比例する＝`expressive_20s` なら 3〜20 s）。

**撃った本文は射ごとに台帳へ刻む**（裁定 112）。`run_secondary.result.json` の `items[].text_id`／`items[].text` が
その 1 本の本文で、**頭の `text_id`／`text` は最初に 22 本を撃った run のもの**（掃引では差し替えない＝
`generated_at`・`irodori_params` と同じ方針）。`make_presets.py` は行から先に読むので、本文を変えて数本だけ
撃ち直しても、撃っていない行が新しい本文を名乗ることはない。

確かめる。

```powershell
python .\verify_wavs.py "$W\secondary" --out "$W\logs\verify_secondary.json"
```

### 4-2. 末尾が切れている本を seed 掃引で直す（decisions 39）

本文も参照も変えずに **seed だけ**を 1234 から順に振り、生成のたびに末尾判定を掛けて
**clean になった最初の seed を採る**。

**合格の条件は 2 つ（裁定 113 で 1 つ足した）。** ⑴ 末尾 3 条件が `clean`、⑵ **潰れていないこと**
（`verify_wavs.py` の `clipped`＝full scale が **3 サンプル以上連続**）。⑵ を見ていなかったせいで、
裁定 113 の初回は `plain_20s` の KANA に `max_clip_run=3` の射（seed 1238）を候補として採ってしまい、
**seed 1239 以降の 3 射が未使用のまま**残っていた（同梱 11 本はいずれも `max_clip_run` 0〜2 で `clipped=false`）。
今は末尾が `clean` でも潰れている射は `[CLIP?]` で失格にして次の seed へ進む（`--allow-clip` で外せる）。

```powershell
# 採用版（secondary\<id>_secondary.wav）のうち tail_suspect の本を自動で拾って掃引
python .\run_secondary.py --sweep-seed

# id を名指しする場合
python .\run_secondary.py --sweep-seed --sweep-ids "vv_mochiko_sexy,co_tsukuyomi"
```

| 引数 | 意味 |
|------|------|
| `--sweep-seed` | 掃引モードに入る（参照は 30 s 版のまま・10 s 版は撃たない） |
| `--sweep-ids <id,id>` | 掃引する id を名指し（既定＝tail_suspect を自動で拾う） |
| `--sweep-max-seeds 8` | 試す seed の個数（既定 8＝1234〜1241） |
| `--sweep-seed-start 1234` | 起点の seed。**一度採った seed から撃ち直すときは次の番号を渡す**（例 `--sweep-seed-start 1235`） |
| `--no-trim-fallback` | seed を使い切っても clean が出ない時の `trim_tail=false` の一巡を切る |
| `--allow-clip` | 潰れている射（`clipped`＝full scale が 3 サンプル以上連続）も採る。**既定は失格にして次の seed へ**（裁定 113 の是正） |
| `--tail-ms` ほか 8 つ | 末尾判定の窓と閾。名も既定も `verify_wavs.py` と同じ（上の表） |
| `--cfg-scale-speaker <値>` | 掃引の全射に同じ `cfg_scale_speaker` を載せる（`trim_tail=false` の一巡も同じ値） |
| `--candidate <名>` | **候補モード**（裁定 113）。射の行き先を `candidates\<名>\` に移し、`run_secondary.result.json` に**畳み込まない**＝採用中の成果物に触れない。§4-3 |

**本文の長さを変えて撃ち直すとき**は掃引に `--text` を添える
（裁定 112＝`python .\run_secondary.py --sweep-seed --sweep-ids "co_kana_naisho,co_ofutonp_kiza,vr2_akane_west" --cfg-scale-speaker 7.0 --text expressive_20s`
＝**cfg は 7.0 のまま**で本文だけ 146 字 → 90 字に縮めた。声が**3 本とも同じ位置で**参照ボイスから離れる、
という司令官の検分に対する手当て。実測＝20.24／18.32／21.00 s）。

**参照ボイスへの寄せ具合を変えて撃ち直すとき**は掃引に `--cfg-scale-speaker` を添える
（裁定 111＝`python .\run_secondary.py --sweep-seed --sweep-ids "co_kana_naisho,co_ofutonp_kiza,vr2_akane_west" --cfg-scale-speaker 7.0`
＝上流既定 5.0 から 2 上げた値。声が途中から参照ボイスから離れる、という司令官の検分に対する手当て）。

seed を使い切っても clean が出なければ、**本文を変えずに** `irodori.trim_tail=false` で同じ seed 列を
もう一巡する。`trim_tail` は上流 `SamplingRequest`（`upstream/Irodori-TTS/irodori_tts/inference_runtime.py:242`）の欄で、
既定 `true` のとき潜在の平坦点（`find_flattening_point`）で音を切る。`false` にすると推定長 `target_samples`
まで残すので、平坦点の誤検出で語尾が落ちた本が救えることがある。

出力。

- 射ごとの wav＝`secondary\sweep\<id>_seed<N>[_notrim].wav`（全射を残す）
- 採用した本は `secondary\<id>_secondary.wav` を**上書き**する
- 掃引の台帳＝`logs\run_secondary.sweep.json`（**その run の分だけ**。前の run の記録は入らない）
- `logs\run_secondary.result.json` には採用分の項目だけを畳み込み（他の話者の記録は消さない）、
  `seed_sweep` として採用 seed・試行回数・全射の測定を足す。
  `seed_sweep.results` も **voice ごとに畳む**ので、掃引を 2 度回しても前の run で採った話者の行は消えない。
  行ごとに自分の `seeds`（その run の seed 列）と `tail_rule` を持つ＝run をまたいでも何の規則でいつ採ったか読める。

掃引のあとは `verify_wavs.py` を掛け直してから §5 の `make_presets.py --copy` を打つ。

**実績（2026-09-05）。** 1 度目＝旧規則（⒜ のみ）で 6 本を 1234 起点で掃引して採用。
2 度目＝規則を 3 条件に強めたら 2 本（月読アイ・ちび式じい）が落ちたので `--sweep-seed-start 1235` で撃ち直し、
ちび式じい は 1 射目（seed 1235）、月読アイ は 4 射目（seed 1238）で clean。`trim_tail=false` の一巡は要らなかった。

### 4-3. 候補として撃つ（`--candidate`・採用は `--adopt-candidate`・裁定 113）

**何のための口か。** 本文をいくつか試して**司令官に聴き比べてもらう**とき、撃つたびに
`voices/presets/` と `voices/presets.json` が動いてしまうと、いま採用中の音が消える。
`--candidate <名>` を付けると行き先が候補置き場に移り、**採用中の成果物には 1 バイトも触れない**。

```powershell
# 本文 2 種 × 話者 2 名＝4 本を候補として撃つ（裁定 113 の実際）
python .\run_secondary.py --sweep-seed --sweep-ids "co_kana_naisho,vr2_akane_west" `
                          --cfg-scale-speaker 7.0 --text calm_20s  --candidate calm_20s
python .\run_secondary.py --sweep-seed --sweep-ids "co_kana_naisho,vr2_akane_west" `
                          --cfg-scale-speaker 7.0 --text plain_20s --candidate plain_20s

python .\verify_wavs.py "$W\candidates\calm_20s"  --out "$W\logs\verify_candidates.calm_20s.json"
python .\verify_wavs.py "$W\candidates\plain_20s" --out "$W\logs\verify_candidates.plain_20s.json"
```

行き先（`--candidate` を付けたときと付けないときの対応）。

| 何 | 既定（採用側） | `--candidate <名>` |
|---|---|---|
| 採用相当の射 | `secondary\<id>_secondary.wav` | `candidates\<名>\<id>_secondary.wav` |
| 掃引の全射 | `secondary\sweep\` | `candidates\<名>\sweep\` |
| その run の台帳 | `logs\run_secondary.sweep.json` | `logs\run_secondary.<名>.sweep.json` |
| 画面の写し | （標準出力だけ） | `logs\run_secondary.<名>.log`（**画面にも出る**・**標準エラーも同じ檔**＝途中で止まった理由も残る） |
| `logs\run_secondary.result.json` への畳み込み | する | **しない** |
| `voices/presets.json`・`voices/presets/` | `make_presets.py --copy` で動く | **動かない**（`make_presets.py` は候補を見ない） |

**`--candidate` は `--sweep-seed` と併せてしか使えない**（付けずに打つと argparse の段で止まる）。
掃引でない候補 run は台帳が `run_secondary.<名>.result.json` になり、`--adopt-candidate` が読めない
＝**上げられない候補**ができてしまうため。あわせて **`--sweep-ids` も必須**
（候補置き場には採用版が無いので tail_suspect の自動拾いが効かない）。
`<名>` は英数字と `_ . -` の 1〜64 字だけ（`..` や `\` は弾く＝候補置き場の外に書かせない）。
`--adopt-candidate` と `--candidate` は**同時に渡せない**（取り違え防止）。

**司令官が選んだあと**は、サーバを起こさずに採用へ上げる（**下の 3 行は裁定 114 で実際に打った命令**）。

```powershell
python .\run_secondary.py --adopt-candidate calm_20s --sweep-ids "co_kana_naisho,vr2_akane_west"
python .\verify_wavs.py "$W\secondary" --out "$W\logs\verify_secondary.json"
python .\make_presets.py --copy
```

`--adopt-candidate` がやること＝⑴ `candidates\<名>\<id>_secondary.wav` を `secondary\<id>_secondary.wav` へ複写、
⑵ **その id の掃引の射（`candidates\<名>\sweep\<id>_seed*.wav`）を `secondary\sweep\<名>\` へ複写**し、
項目と行に `sweep_dir`（複写先の絶対路）を刻む＝台帳の `sweep_file`／`attempts[].file` は**裸の檔名**なので、
複写しないと採用後の台帳が `secondary\sweep\` に無い檔名を指す（どの射を採ったか辿れなくなる）。
箱を分けるのは `cfg5\`・`cfg7-30s\`・`20s-95chars\` と同じ要領、
⑶ その候補 run の台帳（`logs\run_secondary.<名>.sweep.json`）の**その id の項目と掃引の行だけ**を
`merge_result()` で `logs\run_secondary.result.json` に畳む（項目の `file`／`path` は複写先に書き換え、
`adopted_from_candidate` に候補の名を刻む）。行が自分の `text_id`／`text`／`seed`／`cfg_scale_speaker`／
`generated_at` を持っているので、**畳んだあとも素性は候補 run のもの**が残る。
`--sweep-ids` は**必須**（うっかり候補を全部上げないため）。

**実績（2026-09-09・裁定 113）。** `calm_20s`＝KANA 20.08 s（seed 1234・1 射目）／琴葉茜 20.48 s（同）。
`plain_20s`＝KANA 21.48 s（seed **1239**・**6 射目**＝1234〜1237 は末尾で失格・**1238 は末尾 clean だが
クリップ〔`max_clip_run=3`〕で失格**）／琴葉茜 21.24 s（seed 1234・1 射目）。
**4 本とも末尾 3 条件で clean・4 本とも `norm`（`max_clip_run` 2）**・4 本とも許容 18〜23 s の内側。
（初回はクリップの門が無く、seed 1238 の潰れた射を候補にしていた＝門を足して撃ち直した。
初回の記録は `logs\run_secondary.plain_20s.pre-clipfix.log` ほかに残してある。）
採否は司令官の耳（`docs/preset-voices-listening.md` 追記 ⑤）
＝~~**この時点では `voices/presets` は 1 バイトも動いていない**~~
→ **裁定 114（2026-09-09）で `calm_20s` が 2 名とも採用された**（司令官の逐語
「calm採用。 インストーラーくみなおしとマージを頼む。」）。上の 3 行を打って
`voices/presets/{co_kana_naisho,vr2_akane_west}.wav` と `voices/presets.json` が動いた
（**ほかの 9 本は md5 不変**・22 本の測り直しは 21/22 clean＝残る 1 本は未採用の `co_ofutonp_kiza_ref10`）。
`plain_20s` は不採用のまま `candidates\plain_20s\` に残る。記録＝聴取表 追記 ⑥・設計書 §12-6。

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

`secondary` の `md5`（小文字 32 桁）と `size_bytes` は **実檔**（`voices/presets/<id>.wav`・`--copy` の複写後）
から測る。`tail` 節（3 条件の判定・25 ms 刻みの並び・規則の逐語）と併せて **schema で required**。
N: の写しとの突合はこの `md5` で行う。

形を確かめる（`jsonschema` は使い捨てで足りる）。

```powershell
uv run --no-project --with jsonschema python -c @'
import json, sys
from jsonschema import Draft202012Validator
base = r"C:\Users\mugonkun\source\repos\irodori-tts-for-yomiwakechan"
schema = json.load(open(base + r"\tools\preset-voices\presets.json.schema", encoding="utf-8"))
doc = json.load(open(base + r"\voices\presets.json", encoding="utf-8"))
Draft202012Validator.check_schema(schema)
errs = sorted(Draft202012Validator(schema).iter_errors(doc), key=lambda e: list(e.absolute_path))
for e in errs: print("NG", list(e.absolute_path), e.message)
print("schema OK" if not errs else f"{len(errs)} errors")
sys.exit(1 if errs else 0)
'@
```

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
- `secondary.expressive_30s.text`＝二次の本番本文（146 字・「！」「？」と 😊😢😆🎉 を含む・1 文を短く）
- `secondary.expressive_20s.text`＝**約 20 秒ぶんの本文（90 字・裁定 112）**。感情 5 段。
  今の同梱ボイスは **おふとんP きざ の 1 本だけ**がこれ（~~KANA ないしょばなし・琴葉茜（関西弁）~~ の 2 本は
  裁定 114 で `calm_20s` に移った）
- `secondary.calm_20s.text`＝**感情の切り替えを弱めた約 20 秒の本文（94 字・裁定 113）**。絵文字は 😊 が 1 つだけ（後ろ寄り）・「！」無し・`emotion_arc` は 1 段。
  **裁定 114 で採用**（司令官 2026-09-09 逐語「calm採用。 インストーラーくみなおしとマージを頼む。」）＝
  今の同梱ボイスは **KANA ないしょばなし・琴葉茜（関西弁）の 2 本**がこれ（seed 1234・cfg 7.0・20.08／20.48 s）
- `secondary.plain_20s.text`＝**感情の切り替えを無くした約 20 秒の本文（89 字・裁定 113）**。絵文字も「！」も無い平坦な地の文・`emotion_arc` は **0 段（空配列）**。
  **裁定 114 で採用しなかった**＝同梱ボイスに 1 本も無い。**檔は消していない**＝候補 2 本は
  `build/out/preset-work/candidates/plain_20s/<id>_secondary.wav`（掃引の全射 7 本は `…/plain_20s/sweep/`）と
  N: の写しに残してあり、A/B で聴き比べられる。本文も `corpus.json` に残す
- `secondary.greeting_10s.text`＝予備の短い挨拶（44 字）。**未使用**（採用した行は無い）
- `irodori_params`＝`num_steps`（既定 40）・`seed`（既定 1234）

**秒数の目安。** Irodori の読み速は実測**約 4.6 字/秒**（話者ごとに 4.1〜6.1 字/秒と幅がある）。
VOICEVOX／COEIROINK より遅いので、二次本文は一次より短く書く。30 s を狙うなら**約 145 字**、
20 s を狙うなら**約 90 字**。（初稿の約 200 字は 37〜48 s になり目標超過だった。）

**読み速は本文でも動く（裁定 113 の実測）。** 同じ 2 名（KANA・琴葉茜）で本文だけを替えると
`expressive_20s`（90 字）4.45／4.29 字/秒 → `calm_20s`（94 字）**4.68／4.59** → `plain_20s`（89 字）**4.14／4.19**。
＝**平坦な地の文ほどゆっくり読まれる**（感嘆符と絵文字が入るほど速い）。見積りは話者だけでなく
**本文の調子込みで 4.1〜4.7 字/秒の幅**を見ておくこと。

**新しい本文を足すときの手順**（裁定 112 で `expressive_20s` を足したときの実際）。

1. `corpus.json` の `secondary` に `{id, target_s, emotion_arc, text, $calibration}` を足す。
   **`emoji_palette.used` に無い絵文字は本文に入れない**（増やすなら先にそちらへ足す）。
2. `run_secondary.py` の `--text` の `choices` に id を足す。
3. 撃って実測し、**目標から外れていたら 1 句だけ足し引きして撃ち直す**。
   `$calibration` に**両方の実測**を残す（見積りと実測がずれた幅が次の便の目盛りになる）。
4. `presets.json.schema` の `secondary.text_id` の説明に id を足す（**enum は無い**ので値では弾かれない）。
   併せて `make_presets.py` の `notes` に 1 行（その本文で撃った行がどれか・なぜそうしたか）。
   **候補（§4-3）のうちは 4 を打たない**＝台帳にその id の行がまだ 1 つも無いので、説明だけ先に増やすと
   「使っている本文」の一覧が実物とずれる。**採用（`--adopt-candidate`）のときに一緒に足す。**
   ~~裁定 113 の `calm_20s`／`plain_20s` は 1〜3 まで済み・**4 は未了**（候補のままなので正しい状態）。~~
   → **裁定 114（2026-09-09）で `calm_20s` を採用したので 4 まで打った。**説明には
   **`calm_20s`（採用・2 行）と `plain_20s`（corpus に在るが採用した行は無い）の別を書く**
   ＝「corpus に在る」と「台帳が使っている」は違う、が読み手に伝わるようにする。

**本文の世代は行ごとに混ざりうる（今そうなっている）。** 同梱 11 本の内訳は
`expressive_30s` 8 本（cfg 5.0）・`expressive_20s` 1 本（きざ・cfg 7.0）・`calm_20s` 2 本（KANA・琴葉茜・cfg 7.0）。
台帳の頭の `corpus_texts` に実際に使われている id が並ぶので、**そこを見れば何種類混ざっているかが分かる**。
揃えるかどうかは司令官の判断（`docs/preset-voices-listening.md` §3 の 0-1 に開いたまま置いてある）。

---

## 7. 未了

- **VOICEROID2 の 4 名**（琴葉茜・吉田くん・月読アイ・月読ショウタ）＝**一次 wav は 2026-09-05 に生成済み**
  （`gen_voiceroid2.py` ＋ `Vr2SaveTool/`・設計書 §6）。`run_secondary.py` の `PRIMARY_IDS` にも
  4 つの id を追加済み＝**二次 wav がまだ未生成**なので、§4 の `run_secondary.py --also-ref10` を
  もう一度回して §5 の `make_presets.py --copy` を打ち直せば 11 名が揃う。
- **CeVIO AI 弦巻マキ（英）**＝decisions 27 により今回は飛ばす。司令官が後で提供。
- **規約原文の `licenses/` 台帳**＝設計書 §7。別便へ申し送り。
