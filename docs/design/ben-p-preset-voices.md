# 便 P — プリセット話者 12 名の参照ボイス（設計書）

> 正典＝`decisions.md`（特に **17**・**18**・**26**・**27**）。本檔はその実装設計であり、正典を上書きしない。
> 席＝便 P 道具席（Opus 5）。起草 2026-09-04。

---

## 1. 目的と成り立ち

リリース版に**テンプレート参照ボイスを 12 名分同梱する**（decisions 17）。作り方は正典が定める二段構えに従う。

```
 各エンジン（VOICEVOX / COEIROINK / VOICEROID2 / CeVIO AI）
      │  素の地の文を読ませる
      ▼
  一次 wav  ── 素材。配布物には入れない。
      │  これを「参照ボイス」として Irodori-TTS-Server に渡し、
      │  「！」「？」絵文字入りの表情豊かな本文を合成させる
      ▼
  二次 wav  ── これがリリース版のテンプレート参照ボイス（voices/presets/<id>.wav）
```

**なぜ二段か。** 参照ボイスは「その声で・その演技の幅で」喋った見本であるほど効く。一次は各エンジンの素の地の文で*声色*を確保し、二次は Irodori 自身に*演技*（感嘆・疑問・絵文字由来の感情）を乗せさせる。配布するのは二次なので、利用者が最初に触る既定の話者がいきなり表情を持つ。

---

## 2. 話者台帳（12 名）

`decisions.md` 17 の 12 名。`id` は ASCII（参照ボイス檔名と Irodori の voice id を兼ねる）、`display_name` は本体 yomiwakechan2 に見せる日本語名。

**`display_name` はそのまま話者 id である**（裁定 17・契約 ⑷ 4-1＝`PresetVoices.FromPresetsJson` が `PresetVoice.Id` に入れる）。同じ話者で複数スタイルがあるとき、および司令官が名を指したときはスタイルを添える＝「おふとんP（きざ）」「もち子さん（セクシー／あん子）」（裁定 108・2026-09-07）。ここを変えると**既存の利用者の台帳の id も変わる**ので、ランチャが起動と「入れ直す」で改名を引き継ぐ（同じ参照 wav を指すプリセットの行を新しい名へ移し、旧い id の潜在を消す＝便 D 設計書 §4）。

接頭辞＝`vv_`（VOICEVOX）・`co_`（COEIROINK）・`vr2_`（VOICEROID2）・`cevio_`（CeVIO AI）。

| # | id | display_name | エンジン | 話者 / スタイル | 実機 ID | 状態 |
|---|----|--------------|----------|-----------------|---------|------|
| 1 | `vv_mochiko_sexy` | もち子さん（セクシー／あん子） | VOICEVOX | もち子さん / セクシー／あん子 | styleId **66** | **done** |
| 2 | `vv_chibishikijii` | ちび式じい | VOICEVOX | ちび式じい / ノーマル | styleId **42** | **done** |
| 3 | `co_tsukuyomi` | つくよみちゃん | COEIROINK | つくよみちゃん / れいせい | uuid `3c37646f-3881-5374-2a83-149267990abc` style **0** | **done** |
| 4 | `co_kana_naisho` | KANA（ないしょばなし） | COEIROINK | KANA / ないしょばなし | uuid `297a5b91-f88a-6951-5841-f1e648b2e594` style **33** | **done** |
| 5 | `co_mana_isshoukenmei` | MANA（いっしょうけんめい） | COEIROINK | MANA / いっしょうけんめい | uuid `292ea286-3d5f-f1cc-157c-66462a6a9d08` style **7** | **done** |
| 6 | `co_ofutonp_kiza` | おふとんP（きざ） | COEIROINK | おふとんP / きざ | uuid `a60ebf6c-626a-7ce6-5d69-c92bf2a1a1d0` style **23** | **done** |
| 7 | `co_ofutonp_normal_v2` | おふとんP（のーまるv2） | COEIROINK | おふとんP / のーまるv2 | 同 uuid style **2** | **done** |
| 8 | `vr2_akane_west` | 琴葉茜（関西弁） | VOICEROID2 | プリセット「琴葉 茜」/ ボイス `akane_west_emo_44` | プリセット名は実機 dump と逐語一致 | **done** |
| 9 | `vr2_yoshida` | 吉田くん | VOICEROID2 | 鷹の爪 吉田くん(v1) / `yoshidakun_44` | 同上 | **done** |
| 10 | `vr2_tsukuyomi_ai` | 月読アイ | VOICEROID2 | 月読アイ(v1) / `ai_44` | 同上 | **done** |
| 11 | `vr2_tsukuyomi_shota` | 月読ショウタ | VOICEROID2 | 月読ショウタ(v1) / `shouta_44` | 同上 | **done** |
| 12 | `cevio_maki_en` | 弦巻マキ（英語） | CeVIO AI | 弦巻マキ 英 | — | **skipped**（decisions 27） |

**確認の事実。** 1〜7 の ID は 2026-09-04 に実機の `GET /speakers`（VOICEVOX 0.25.2）と `GET /v1/speakers`（COEIROINK 2.13.0）で**席が実射して確認した**。台帳の話者名・スタイル名はエンジンの応答と逐語一致。

**8〜11（VOICEROID2）** の一次 wav は 2026-09-05 に生成済み（§6）。プリセット名は実機の `Vr2SaveTool.exe dump` が返した標準プリセット 7 件（琴葉 茜／琴葉 葵／結月ゆかり／月読アイ(v1)／月読ショウタ(v1)／水奈瀬コウ(v1)／鷹の爪 吉田くん(v1)）と逐語一致する。ボイス檔名は `C:/Program Files (x86)/AHS/VOICEROID2/Voice/` の実在檔（`akane_west_emo_44` ほか）。
**12（CeVIO AI 弦巻マキ 英）** は decisions 27 により今回は飛ばす。司令官が後で一次 wav を提供する。

---

## 3. 一次 wav の作法

### 3.1 本文

`tools/preset-voices/corpus.json` の `primary`。**素の地の文**＝感嘆符も絵文字も入れない平叙文。抑揚を煽らず、エンジン側のスタイル（セクシー／ないしょばなし等）に話者の色を担わせる。

出典は `research/lab/kit-cuda/ref-bench/voices/manifest.json` の 10 s／30 s 本文をそのまま採用した（調査便が実測済みの本文を再利用）。

- `text_10s`＝2 文・約 58 字
- `text_30s`＝6 文・約 195 字

### 3.2 生成

| エンジン | 口 | 道具 |
|----------|-----|------|
| VOICEVOX | HTTP `127.0.0.1:50021`・2 段（`POST /audio_query?text=..&speaker=<id>` → `POST /synthesis?speaker=<id>` に query JSON） | `tools/preset-voices/gen_voicevox.py` |
| COEIROINK | HTTP `127.0.0.1:50032`・`POST /v1/synthesis` に **10 欄すべて**（欠けると 422） | `tools/preset-voices/gen_coeiroink.py` |
| VOICEROID2 | API 無し・GUI を Codeer.Friendly で駆動し［音声保存］（§6） | `tools/preset-voices/gen_voiceroid2.py` ＋ `Vr2SaveTool/` |
| CeVIO AI | COM | 今回は飛ばす |

両道具とも Python 3.13 標準ライブラリのみ（`urllib`・`struct`）。エンジン生存確認と起動待ち、実機 ID の照合、wav ヘッダからの秒数／Hz ログを内蔵する。

### 3.3 実測（2026-09-04・席が生成）

| id | 10 s 版 | 30 s 版 | Hz |
|----|---------|---------|-----|
| `vv_mochiko_sexy` | 10.60 s | 36.47 s | 24000 |
| `vv_chibishikijii` | 12.82 s | **42.91 s** | 24000 |
| `co_tsukuyomi` | 8.29 s | 27.57 s | 44100 |
| `co_kana_naisho` | 11.71 s | 39.22 s | 44100 |
| `co_mana_isshoukenmei` | 9.75 s | 32.13 s | 44100 |
| `co_ofutonp_kiza` | 10.13 s | 33.42 s | 44100 |
| `co_ofutonp_normal_v2` | 9.13 s | 29.35 s | 44100 |

全 14 本がクリップ無し・ピーク −0.7〜−6.7 dBFS・RMS −17.7〜−26.1 dBFS・先頭末尾の無音 0.1 s 前後で健全（`verify_wavs.py`）。

**話速の差が出る。** 同じ本文でも `co_tsukuyomi` 27.6 s に対し `vv_chibishikijii` 42.9 s と 1.5 倍開く。目標秒数はエンジン側では揃わない。**揃える必要はない**（参照ボイスは長さより声色の代表性が効く）が、台帳には実測値を書く。

### 3.4 置き場

- 作業用＝`build/out/preset-work/primary/`（git 管理外）
- 保全＝`N:/temp_for_claudecode_agents/irodori-ywk/preset-voices/primary/`（両機から見える N:）
- **配布物には入れない**。一次は素材。

---

## 4. 二次 wav の作法

### 4.1 本文

`corpus.json` の `secondary`。Irodori が演技に反映する「**！**」「**？**」と絵文字（😊 😢 😆 🎉）を含め、**1 文を短く**し、**複数の感情が順に出る**構成にする。

- `expressive_30s`（本番）＝感情 7 段＝あいさつ・喜び → 告知・祝い → 驚き・問いかけ → 落胆・悲しみ → 持ち直し → 高揚・呼びかけ → 締めの祝い。目標 25〜35 s。
- `greeting_10s`（予備）＝短い挨拶 8〜10 s。長文で崩れる話者の代替。

**校正の事実。** 初稿は約 200 字で、実測 37〜48 s と目標を超えた。Irodori の読み速は実測**約 4.6 字/秒**（v4.1-Small・bf16・steps 40・seed 1234・7 話者 14 本）で、VOICEVOX/COEIROINK より遅い。約 145 字に短縮して目標域に収めた。感情 7 段と絵文字 4 種は残している。

### 4.2 生成の経路

`tools/preset-voices/run_secondary.py` が**私設サーバを自分で立てて**行う。

> **8088 は起こさない**（停止域）。**8090 を自分で立てる**。`C:/irodori-TTS-server/` と `C:/IrodoriTTS/` は**書き換えない**＝`.venv-rocm` の `python.exe` を*実行するだけ*。`voices_dir` と `cwd` は必ず自分の作業ディレクトリに向ける。

```
"C:/irodori-TTS-server/Irodori-TTS-Server/.venv-rocm/Scripts/python.exe" -m irodori_openai_tts --host 127.0.0.1 --port 8090
  cwd = build/out/preset-work/server/
  env = IRODORI_VOICES_DIR=<絶対パス build/out/preset-work/voices>
        IRODORI_PRELOAD=true
        IRODORI_EMPTY_CACHE_INTERVAL=0
        IRODORI_MODEL_PRECISION=bf16
        IRODORI_CODEC_PRECISION=bf16
        IRODORI_HF_CHECKPOINT=Aratako/Irodori-TTS-v4.1-Small
        HF_HUB_OFFLINE=1
        PYTHONDONTWRITEBYTECODE=1
        PYTHONUTF8=1
```

**cwd を変えても import できるか＝実射で確認済み。** `.venv-rocm` は上流 Irodori-TTS-Server を `-e`（編集可能）で入れており、`__editable__.irodori_tts_server-0.1.0.pth` が**絶対パスを指す**ため cwd に依存しない。`irodori_tts` 本体は site-packages の通常インストール。**`PYTHONPATH` は不要**だった。道具側には保険として、import に失敗した時だけ `C:/irodori-TTS-server/Irodori-TTS-Server/src` と `C:/IrodoriTTS/Irodori-TTS` を `PYTHONPATH` に足して再試行する経路を持たせてある（どちらも読むだけ）。

**手順**は次の通り。

1. 一次 30 s wav を `voices_dir` へ **ASCII 檔名**で複写（`vv_mochiko_sexy.wav`）。10 s 版は `<id>_ref10.wav` として比較用に併置。
2. サーバを子プロセスで起動。
3. **ready 待ち**＝`GET /health` の `runtime.loaded` が true になるまで。ROCm は読込 20〜30 s。**実測 36.1 s**。
4. **暖機 1 発**＝`no_ref`・短文（機体初回形状のコンパイルを済ませる）。**実測 3.1 s**。
5. 各話者に `POST /v1/audio/speech`：

```json
{ "model": "irodori-tts", "input": "<二次本文>", "voice": "<id>",
  "response_format": "wav", "irodori": { "num_steps": 40, "seed": 1234 } }
```

6. 終了時に子プロセスを**ツリー kill**（`taskkill /T /F`）＝uvicorn の孫を残さない。

`num_steps: 40` は decisions 10（既定は上流のまま 40）。`seed: 1234` は再現のため固定し、台帳に書く。

### 4.3 参照は 30 s 版を既定・10 s 版も作る

既定の参照は 30 s 版（`voice: "<id>"`）。10 s 版（`voice: "<id>_ref10"`）も比較用に生成し、**聴いて良かった方を採用**する。採用しなかった側は配布しない。

### 4.4 出力の素性

Irodori-TTS-Server の wav 出力は **48000 Hz・1ch・16bit**（実測）。一次の 24000／44100 Hz とは無関係に揃う。

**ピーク正規化に注意。** 二次 wav は**全本が peak = 0.0 dBFS ちょうど**になる。これは*潰れている*のではなく、サーバ側が full scale へ正規化しているため。full scale に達するサンプルは約 200 万中 7〜68 個（0.0003〜0.003 %）で、いずれも**孤立**している（連続長 1〜2）。
そこで `verify_wavs.py` は full scale の**最長連続長**（`max_clip_run`）を出し、**連続 3 サンプル以上を「潰れている」**と判定する。孤立した full scale は `norm` と表示し異常扱いしない。この区別が無いと全本が偽の CLIP 警告になる。

### 4.5 実測（2026-09-04・短縮後の本文・30 s 参照）

| id | 二次の長さ | 合成所要 |
|----|-----------|---------|
| `vv_mochiko_sexy` | 30.76 s | 20.3 s |
| `vv_chibishikijii` | 31.68 s | 32.2 s |
| `co_tsukuyomi` | 28.24 s | 18.5 s |
| `co_kana_naisho` | 33.28 s | 36.4 s |
| `co_mana_isshoukenmei` | 30.16 s | 33.0 s |
| `co_ofutonp_kiza` | 30.60 s | 30.5 s |
| `co_ofutonp_normal_v2` | 26.32 s | 30.0 s |

全 7 名が目標の 25〜35 s に収まった（10 s 参照版も 27.7〜32.2 s で同様）。
ready 待ち 36.1 s・暖機 2.4 s・14 本の生成で約 7 分。

`co_ofutonp_normal_v2_ref10` のみ `max_clip_run = 3` で CLIP 判定に触れた（境界値）。
30 s 参照版は `run = 2` で `norm`。既定は 30 s 参照なので**採用分に CLIP は無い**。
10 s 参照を採る場合はこの 1 本だけ耳で確かめること。

### 4.6 置き場と命名

- 作業用＝`build/out/preset-work/secondary/<id>_secondary.wav`（`_ref10_secondary.wav` は比較用）
- **成果＝`voices/presets/<id>.wav`**（採用した二次のみを、`<id>.wav` の名で置く）
- 台帳＝`voices/presets.json`（形は `tools/preset-voices/presets.json.schema`）

---

## 5. 台帳（`voices/presets.json`）

形は `tools/preset-voices/presets.json.schema`（JSON Schema 2020-12）。1 名あたり：

`id`（ASCII）・`display_name`（日本語・本体に見せる名）・`engine`・`engine_speaker`・`speaker_uuid`・`style{name,id}`・`status`（done / pending / skipped）・`primary{檔名・秒・Hz・ch・bit・本文 id・peak・rms}`・`secondary{檔名・秒・Hz・ch・bit・peak・rms・本文 id・**本文の逐語**・ref_variant・irodori{num_steps,seed}・server{checkpoint,precision,port}・elapsed}`・`rights_note`・`rights_confirmed_by`・`rights_confirmed_at`・`rights_source_fetched`・`generated_at`。

`status` が `pending` / `skipped` の 5 名も**台帳には最初から載せる**（`primary`・`secondary` は `null`）。12 名の全体像が台帳だけで分かるようにするため。

---

## 6. VOICEROID2 の wav 取り出し（**実装済み**・2026-09-05）

VOICEROID2 は 32bit WPF の GUI で、**API が無い**。本体 yomiwakechan2 は Codeer.Friendly で駆動している（`C:/Users/mugonkun/source/repos/yomiwakechan2/tools/Voiceroid2Bridge/Vr2Controller.cs` と `_v1_reference/VR2_Talker.cs`・**読むだけ**）が、**wav 保存の口は本体に無かった＝新規実装した**。

> **停止域。** yomiwakechan2 の檔は 1 行も変えていない。読んだのは UI 木の当たり（タブ・本文欄・ボタンの索引）と 32bit 前提の作法だけで、**檔はコピーしていない**（別ライセンスの本体コードを MIT のリポへ写さないため）。

### 6.1 採った手（案 1）

`tools/preset-voices/Vr2SaveTool/`（新規・C# コンソール・**net48 / x86**・Codeer.Friendly + RM.Friendly.WPFStandardControls）。x86 なのは Friendly が「操作側も同じアーキテクチャの .NET Framework」を要求するため（本体の Voiceroid2Bridge と同じ理由）。

    Vr2SaveTool.exe dump                    UI 木・プリセット一覧・窓の構成を書き出す（索引を決め打ちにしない）
    Vr2SaveTool.exe dumpdlg                 開いているダイアログの WPF 木を書き出す
    Vr2SaveTool.exe batch <jobs.tsv>        プリセット選択 → 本文投入 → ［音声保存］→ 保存 → 検証

Python 側の口は `tools/preset-voices/gen_voiceroid2.py`。他エンジンの `gen_voicevox.py` / `gen_coeiroink.py` と同じ形の `gen_voiceroid2.result.json` を書くので、`make_presets.py` がそのまま畳める。

### 6.2 実機で分かった事実（決め打ちを避けるために dump で確かめた）

| 事実 | 値 |
|------|-----|
| ［音声保存］ボタン | `AI.Talk.Editor.TextEditView` の LogicalTree **索引 24**。索引は決め打ちせず、ラベル TextBlock「音声保存」（索引 27）の直前の Button として**自動判定**する（v1 の `SaveButton = editUis[24]` と一致した） |
| 保存の第 1 段 | **Win32 の #32770 ではなく WPF 窓** `AI.Talk.Editor.SaveWaveWindow`（タイトル「音声保存」）。ファイル分割／ポーズ／その他の設定窓 |
| 保存の第 2 段 | Win32 コモンダイアログ `#32770`「名前を付けて保存」。ファイル名欄は `ComboBox` 配下の `Edit`・**ctrl id 1001** |
| ポーズ設定 | 開始 0 ms / 終了 **800 ms**（司令官の設定なので**触っていない**。出力 wav の末尾に 0.80 s の無音が付く） |
| 同時出力 | 「テキストファイルを音声ファイルと一緒に保存する」が ON＝`<名前>.txt`（Shift-JIS）も出る |

### 6.3 詰まった 2 点と、その直し（重要）

**(a) コモン保存ダイアログは 2 回目以降 `WM_SETTEXT` を無視する。**
1 本目は `WM_SETTEXT` でファイル名欄に流し込めば通る。ところが 2 本目以降は、**欄の表示は新しい檔名に変わるのに、ダイアログ内部の檔名は前回のまま**で、［保存］を押すと**前の檔名で保存される**。実際に「30 s の音が `vr2_akane_west_10s.wav` に書かれる」事故を観測した（同時出力の .txt の中身が 30 s 本文だったことで確定）。
直し＝**人が打つのと同じ `WM_CHAR` を 1 文字ずつ送る**（`EM_SETSEL` で全選択 → Backspace → 1 文字 4 ms 間隔で投函）。`WM_CHAR` は引数にポインタを持たないのでプロセスを跨いで届き、ダイアログの内部状態も追随する。オートコンプリートが末尾に補完を足した場合は Delete で落とし、**2 回続けて欄が目的のパスと一致すること**を確かめてから［保存］を押す。

**(b) 「付随ダイアログを既定ボタンで閉じる」処理が、保存ダイアログの［保存］を誤爆し得た。**
`#32770` の ctrl id 1 は「情報」ダイアログでは OK だが、保存ダイアログでは**［保存］**である。直し＝ファイル名欄を持つ窓は触らない／既定ボタンの文字列に「保存」を含む窓も触らない。

### 6.4 事故を検出する 3 つの関門

無言で間違った wav ができるのが一番まずいので、道具は次を必ず通す。

1. **ファイル名欄の二重確認**＝打ち込んだ後、200 ms 空けて 2 回読み、どちらも目的のパスと完全一致することを確かめる。
2. **上書き確認の照合**＝「既に存在します／置き換えますか？」が出たら、**そこに書かれた檔名が目的のものである時だけ［はい］**。違う檔名なら［いいえ］を押して**その 1 本を失敗にする**（他の便の成果を巻き添えで潰さない）。
3. **本文の突き合わせ**＝VOICEROID2 が wav と一緒に書く `.txt` を読み、流し込んだ本文の先頭 12 字と一致することを確かめてから .txt を捨てる。一致しなければ失敗（＝別の本文の音がこの檔名で保存された）。

失敗した 1 本は wav も .txt も削除する（次の便が「出来ている」と誤認しないため）。1 本ごとに、余計な窓が消えるまで待ってから次へ進む。

### 6.5 実測（2026-09-05・8 本・38.8 s）

| id | プリセット | 10 s 版 | 30 s 版 |
|----|-----------|---------|---------|
| `vr2_akane_west` | 琴葉 茜 | 13.04 s | 41.09 s |
| `vr2_yoshida` | 鷹の爪 吉田くん(v1) | 10.46 s | 32.85 s |
| `vr2_tsukuyomi_ai` | 月読アイ(v1) | 17.45 s | **55.30 s** |
| `vr2_tsukuyomi_shota` | 月読ショウタ(v1) | 13.82 s | 42.98 s |

全 8 本 **44100 Hz・1ch・16bit**。先頭無音 0.002〜0.011 s・末尾 0.80 s（§6.2 のポーズ設定）。`verify_wavs.py` で 6 本が `ok`、`vr2_tsukuyomi_ai` の 2 本が `norm`（full scale に触る孤立サンプルが 1〜2 個・連続 3 未満なので潰れてはいない）。

月読アイは話速が遅く 30 s 本文で 55.3 s になる。**揃える必要はない**（§3.3 と同じ理由・参照ボイスは長さより声色の代表性が効く）。

### 6.6 採らなかった案

- **案 2（WASAPI ループバック録音）**＝他の音が混じる・SRC が挟まる・切り出しが要る。**一次の品位が落ちる**ので案 1 が通った時点で不要。
- **案 3（手作業 8 本）**＝案 1 が 90 分以内に通らなかった時の退路として用意していたが、使わずに済んだ。

## 7. 権利の注記

**decisions 18（司令官確認済み・2026-09-04）**＝上記エンジンの生成ボイスは**個人利用・商用・有償でも再配布可**（元々そういう目的の商品）。本ソフトは無償配布なので問題なし。

> **席は推測で断定しない。** 上は**司令官の確認**であって、**席が各エンジン／各キャラの規約原文を取得して読んだ結果ではない**。
> **2026-09-04 時点で、規約原文は席が未取得**（`rights_source_fetched: false`）。本便は外部へ何も送らない停止域の下にあり、規約ページの取得も行っていない。

したがって台帳の各行には次を必ず持たせる。

- `rights_note`＝「誰が・いつ確認したか」と「規約原文は未取得」の別を明記した文
- `rights_confirmed_by`＝「司令官（mugonkun）」
- `rights_confirmed_at`＝`2026-09-04`
- `rights_source_fetched`＝`false`

**残件。** 各エンジン／キャラの規約原文の所在（URL・版・取得日）を `licenses/` の台帳に、司令官の確認日付と共に記す（decisions 18）。これは本便の担当パス外なので、**別便への申し送り**とする。対象＝VOICEVOX（もち子さん・ちび式じい）／COEIROINK（つくよみちゃん・KANA・MANA・おふとんP）／VOICEROID2（琴葉茜・吉田くん・月読アイ・月読ショウタ）／CeVIO AI（弦巻マキ）。キャラごとに権利者が異なるため、**エンジン単位ではなくキャラ単位**で当たる必要がある。

---

## 8. 道具一覧

| 檔 | 役 |
|----|-----|
| `tools/preset-voices/corpus.json` | 本文（一次の地の文・二次の表情本文・予備の挨拶・irodori パラメータ） |
| `tools/preset-voices/gen_voicevox.py` | VOICEVOX で一次 wav（10 s／30 s） |
| `tools/preset-voices/gen_coeiroink.py` | COEIROINK で一次 wav（10 s／30 s） |
| `tools/preset-voices/gen_voiceroid2.py` | VOICEROID2 で一次 wav（10 s／30 s）。実体の GUI 操作は下の C# 道具 |
| `tools/preset-voices/Vr2SaveTool/` | VOICEROID2 を Codeer.Friendly で駆動して［音声保存］（net48/x86・§6） |
| `tools/preset-voices/run_secondary.py` | 私設 8090 サーバの起動 → ready 待ち → 暖機 → 二次生成 → ツリー kill |
| `tools/preset-voices/verify_wavs.py` | wav の諸元・RMS・ピーク・無音・クリップ（正規化と区別）を JSON に |
| `tools/preset-voices/make_presets.py` | 生成結果を畳んで台帳 `voices/presets.json` を書き、採用分を `voices/presets/` へ複写 |
| `tools/preset-voices/presets.json.schema` | 台帳 `voices/presets.json` の形 |
| `tools/preset-voices/README.md` | 実行手順 |

すべて Python 3.13 標準ライブラリのみ（`urllib`・`struct`・`array`・`subprocess`）。numpy も requests も使わない。

---

## 9. 残件

1. **VOICEROID2 4 名の一次 wav**（別席 G3・§6 案 1 を第一候補）。揃い次第 `run_secondary.py` の `PRIMARY_IDS` に足して二次を生成する。
2. **CeVIO AI 弦巻マキ（英）の一次 wav**＝司令官が後で提供（decisions 27）。
3. **30 s／10 s どちらの参照を採用するか**＝両方生成済み。**聴いて決める**。席は音を聴けないので、司令官の試聴が要る。
4. **規約原文の取得と `licenses/` 台帳**（§7）＝別便へ申し送り。
5. **Radeon 機の期限**＝2026-09-07（decisions 19・26）。1〜3 はそれまでに。
