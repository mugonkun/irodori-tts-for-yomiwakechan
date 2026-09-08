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

`id`（ASCII）・`display_name`（日本語・本体に見せる名）・`engine`・`engine_speaker`・`speaker_uuid`・`style{name,id}`・`status`（done / pending / skipped）・`primary{檔名・秒・Hz・ch・bit・本文 id・peak・rms}`・`secondary{檔名・秒・Hz・ch・bit・peak・rms・本文 id・**本文の逐語**・ref_variant・irodori{num_steps,seed,trim_tail?,cfg_scale_speaker?,seed_sweep?}・server{checkpoint,precision,port,device}・elapsed}`（`?` は在るときだけ現れる欄。`trim_tail`・`seed_sweep` は decisions 39、`cfg_scale_speaker` は §10）・`rights_note`・`rights_confirmed_by`・`rights_confirmed_at`・`rights_source_fetched`・`generated_at`。

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

---

## 10. 追補（2026-09-09）— 参照ボイスへの寄せ具合 `cfg_scale_speaker`（裁定 111）

> **⚠ この節の実射（cfg 7.0＋146 字の本文）は司令官が聴いて外した。** 3 本とも同じ位置で参照ボイスから
> 離れたので、手当ては cfg ではなく**本文の長さ**に移った（§11＝裁定 112）。その §11 の 3 本も
> **おふとんP きざ の 1 本だけが合格**で、KANA と 琴葉茜 は「発話途中で入れ替わる」で外れた
> ＝**§12（裁定 113）が今の版**。
> `cfg_scale_speaker` **7.0 という値そのものは §11・§12 でも据え置き**なので、この節の口の説明はそのまま生きている。

司令官の検分（2026-09-09）＝逐語「**サンプルボイスをよく検分してなかった。２次生成ボイスが途中から参照ボイスに
なってないね。CFG Scale Speaker を2ほど上げて再生成を頼む。co_kana_naisho / co_ofutonp_kiza / vr2_akane_west
出来たら検分するので、検分合格で次に進もう**」。

**何の口か。** `cfg_scale_speaker`＝上流 `SamplingRequest` の欄で、**参照ボイス（話者条件）への誘導の強さ**。
`POST /v1/audio/speech` の `irodori` 欄にそのまま載る
（`upstream/Irodori-TTS-Server/src/irodori_openai_tts/app.py` が `opts.cfg_scale_speaker` →
`_extra(payload, "cfg_scale_speaker")` → 設定既定 の順に解決する）。

| 何 | 値 | 出どころ |
|---|---|---|
| 上流の既定 | **5.0** | `upstream/Irodori-TTS-Server/src/irodori_openai_tts/config.py:54` `default_cfg_scale_speaker` |
| 2026-09-05 に撃った 11 本 | **渡していない**＝既定 5.0 が効いた | 台帳に `cfg_scale_speaker` の欄が無い＝その意味 |
| 2026-09-09 に撃ち直した 3 本 | **7.0**（＝5.0＋2） | 台帳 `secondary.irodori.cfg_scale_speaker: 7.0` |

**道具の側。** `run_secondary.py` に `--cfg-scale-speaker <値>` を足した（既定 `None`＝**渡さない**）。
渡した run は、通常経路も掃引経路も（`trim_tail=false` の一巡も）全射の `irodori` 欄に同じ値を載せる。
**暖機の 1 発（`no_ref`）には載せない**（参照ボイスを使わない射なので意味が無い）。

**どこに残るか（掃引 run の記録の在処）。**

| 何 | 檔・欄 |
|---|---|
| 採用した射の値 | `logs/run_secondary.result.json` の `items[].request.irodori.cfg_scale_speaker` |
| 掃引の全射の値 | 同 `seed_sweep.results[].cfg_scale_speaker` と `…[].attempts[].cfg_scale_speaker`（`null`＝渡していない＝上流既定 5.0） |
| その run に渡した既定 | その run 限りの `logs/run_secondary.sweep.json` の `irodori_params`、および畳み込み先の `seed_sweep.irodori_params` |

**畳み込み先の top-level `irodori_params`（と `generated_at`・`server_log`）は差し替えない。**
あれは「最初に 22 本を撃った run」の素性で、掃引で撃ち直した数本のものではないから
（`merge_result()`＝`tools/preset-voices/run_secondary.py`）。top-level に新しい値を混ぜると、
`make_presets.py` の既定（`par`）経由で**撃っていない行にまで新しい値が被る**筋ができる。
代わりに、畳み込む行に自分の run の `generated_at`／`server_log` を持たせ、`make_presets.py` は
**行ごとの** `generated_at`（＝その 1 本を実際に撃った時刻）と、その run の server ログから拾った実 `device` を書く
（裁定 111 の付帯で直した。直す前は撃ち直した 3 行が 2026-09-05 を名乗っていた）。
`server.hf_checkpoint`・`precision`・`port` は畳み込み先の `/health` の写しのままだが、
2026-09-09 の run の `/health` も同値（v4.1-Small・bf16・8090）。

`make_presets.py` は各射の `request.irodori` から
`num_steps`・`seed`・`trim_tail` と同じ扱いで `cfg_scale_speaker` を台帳へ写す。
**無い行に既定値 5.0 を書き足すことはしない**＝「撃った時に明示していない」という事実をそのまま残し、
台帳の `notes` に「無い行は上流既定 5.0 で撃った」と 1 行で断ってある。形は `presets.json.schema` の
`secondary.irodori.properties.cfg_scale_speaker`（`type: number`）。

**実射（2026-09-09・私設 8090・Radeon 8060S・bf16・v4.1-Small）。**
`--sweep-seed --sweep-ids "co_kana_naisho,co_ofutonp_kiza,vr2_akane_west" --cfg-scale-speaker 7.0`
＝seed 1234 起点・最大 8・末尾判定は 3 条件のまま。10 射・全体 116 s（読込 40.6 s・暖機 3.0 s・1 射 5.4〜9.9 s）。
採用は **KANA seed 1235（2 射目）・おふとんP きざ seed 1239（6 射目）・琴葉茜 seed 1235（2 射目）**で
いずれも `trim_tail=true`・末尾 `clean`。**秒数は 3 本とも旧版と 1/100 秒まで同じ**（33.28／30.60／34.92 s）。
旧版（cfg 5.0）は `build/out/preset-work/secondary/cfg5/` と N: の写しに残した＝A/B 用。
**採否は司令官の耳**（`docs/preset-voices-listening.md` 追記 ③）。合格するまで次へ進まない。

**申し送り（合格したあとの本体側）。**

1. **潜在は自動で作り直される＝鍵の確認は要らない。** 潜在の新旧は檔名ではなく**中身**で決まる。
   `server/ywk_server.py` の `_sources_match()`（:2597-2609）が sidecar `<stem>.json` に記録した
   **各 source wav の sha256** を今の檔の `file_digest()` と突き合わせ、違えば `latent_status()`（:2663-2666）が
   stale を返す。設計註（:2420-2424）逐語＝「*the sha256 of every source wav plus the three encode parameters.
   A changed wav flips ``latent_stale`` in the speaker list and the next run rebuilds.*」。
   ＝入れ直しのあと**古い（cfg 5.0 の）潜在を使い続ける恐れは無い**。目視するのは
   `GET /ywk/voices` の `latent_stale` がこの 3 名で **true** に立つこと（事前計算のあとは false に戻ること）だけ。
2. **この 3 名の 10 s 参照版（`ref_10s_file`）は cfg 5.0 のまま**＝掃引は採用版（30 s 参照）だけを撃つ。
   台帳 `notes` の「聴いて良い方に差し替えてよい」に従って 10 s 版へ差し替えるなら、**先に 7.0 で撃ち直すこと**
   （そうしないと司令官が是正を命じた当の値が黙って 5.0 に戻る）。
   **⚠ 追記 ④ で条件が 1 つ増えた**＝いまは `--cfg-scale-speaker 7.0 --text expressive_20s` の**両方**が要る
   （§11-4 の 3・台帳 `notes` も同じ逐語に直した）。
3. `build/installer-build.ps1` の `$ExpectedAppBytes` は主席の担当（この席は編集も `installer-build` の実行もしない）。
   ~~3 本とも旧版と**同じバイト数**（3,194,924／2,937,644／3,352,364）なので、アプリ木の総バイト数は動かない見込み~~
   ~~＝実際は主席の再計測で確かめること。~~
   **⚠ この見込みは追記 ④（§11-4 の 1）で覆った**＝本文を縮めた 3 本は 1,943,084／1,758,764／2,016,044 に減り、
   3 本で **−3,767,040 バイト**。`$ExpectedAppBytes` は**必ず動く**（主席が測り直す）。

---

## 11. 追補（2026-09-09）— 本文を約 20 秒に縮める（裁定 112）

> **⚠ この節の 3 本のうち、司令官が完成としたのは おふとんP きざ の 1 本だけ。**
> KANA ないしょばなし と 琴葉茜（関西弁）は「**発話途中で入れ替わる**」で外れた。
> 手当ては **§12（裁定 113）の候補 4 本＝今の版は §12**。きざ の 1 本（`voices/presets/co_ofutonp_kiza.wav`・
> 18.32 s・md5 `d606722e…`）は**確定＝以後撃ち直さない**。

司令官の検分（2026-09-09・§10 の cfg 7.0 版を聴いたうえで）＝逐語
「**3ファイルともおなじところで参照ボイスが外れるね。アプローチを変えよう。文章を20秒程度に見積もって再生成。**」。

**所見の読み方。** 3 本（KANA ないしょばなし・おふとんP きざ・琴葉茜 関西弁）は**エンジンも話者も別**なのに、
**離れる箇所が揃った**。cfg を 5.0→7.0 に上げても消えなかった（§10）。
＝これは**寄せの強さ**の問題ではなく、**本文がその位置まで来たこと（＝長さ）**に依る現象だと読める。
Irodori の話者条件は 1 本の参照 wav から取るので、生成が長くなるほど条件から離れていく余地が増える。
よって**アプローチを変えた**＝`cfg_scale_speaker` は **7.0 のまま**（裁定 111 の指示は撤回されていない）で、
**本文だけ**を約 20 秒ぶんに縮めてこの 3 本を撃ち直した。

### 11-1. 本文 `expressive_20s`（`corpus.json`）

`corpus.json` の `secondary` に 3 つ目の本文を足した（`expressive_30s`・`greeting_10s` は 1 字も触っていない）。

| 何 | 値 |
|---|---|
| id | `expressive_20s` |
| `target_s` | 20 |
| 字数 | **90 字**（うち絵文字 4・**半角空白 3**・全角空白 0） |
| `emotion_arc` | あいさつ・喜び → 告知・祝い → 落胆・悲しみ → 持ち直し → 高揚・呼びかけ（**5 段**） |
| 絵文字 | 😊🎉😢😆＝`emoji_palette.used` のまま（**増やしていない**） |

> こんにちは！いい知らせがあるんです😊 新しい企画が、ついに完成しました🎉 正直、何度もくじけそうになりました😢 でも、ここまで来られました。さあ、本番ですよ！一緒に楽しみましょう😆

**見積り。** 146 字の `expressive_30s` は 11 話者で 24.8〜35.8 s。この 3 名は 33.28／30.60／34.92 s
＝**4.4／4.8／4.2 字/秒**なので、20 s なら **85〜95 字**。落としたのは
『ええっ、もう出来たの？と驚きましたか？』『つらい日もありました。』『準備はいいですか？』『最高の一日になります🎉』
の **4 句**＝「驚き・問いかけ」段と「締めの祝い」段は丸ごと、「落胆・悲しみ」段と「高揚・呼びかけ」段は 1 句ずつ。
『でも、ここまで来られました。』（＝「持ち直し」段）は**残した**ので `emotion_arc` は 7 段 → **5 段**。

**目盛りは実射で 2 度合わせた**（両方の記録を残してある＝`$calibration` に逐語）。

| 版 | 字数 | KANA | きざ | 琴葉茜 | 判定 |
|---|---|---|---|---|---|
| ⑴ 初稿（頭が『みなさん、こんにちは！』） | 95 字 | 22.40 s | 19.96 s | **23.24 s** | 琴葉茜が許容 18〜23 s を **0.24 s 超過**＝不採用 |
| ⑵ **今の本文**（呼びかけの 1 句『みなさん、』5 字だけを落とした） | **90 字** | **20.24 s** | **18.32 s** | **21.00 s** | **3 本とも許容の内側**＝採用 |

⑴ の射は `build/out/preset-work/secondary/sweep/20s-95chars/` と `logs/run_secondary.20s-95chars.log`・
`logs/run_secondary.sweep.20s-95chars.json` に残した。**縮めたのは 1 句だけ**（司令官の見積りの枠内で、
感情の 5 段も絵文字も動かさずに済ませるため）。

### 11-2. 本文を**行ごと**に刻む（道具の側の直し）

**踏みかけた穴。** `make_presets.py` は本文を台帳の**頭**から読んでいた
（`runsec.get("text_id")` / `runsec.get("text")`）。ところが `merge_result()` は畳み込み先の top-level を
**差し替えない**（撃っていない行の既定に新しい値が被らないようにする、§10 と裁定 111 の付帯の方針）。
＝本文を変えて数本だけ撃ち直すと、**撃ち直した行まで古い 146 字の本文を名乗る**。`generated_at` で踏んだのと同じ穴。

**直し方（`generated_at` と同じ扱いに揃えた）。**

1. `run_secondary.py` が**射ごとに** `items[].text_id`／`items[].text` を刻む（通常経路・掃引経路の両方）。
2. `merge_result()` はそれを持ったまま畳む（古い形の報告のために `setdefault` の控えだけ置く）。
   **top-level の `text_id`／`text` は差し替えない**＝`generated_at`・`irodori_params` と同じ。
3. `make_presets.py` は `s.get("text_id") or sec_text_id` の順で**行から**読む。
4. 台帳の頭に **`corpus_texts`** を足した＝`presets[].secondary.text_id` に実際に現れる id を畳んで並べる
   （今は `["expressive_20s", "expressive_30s"]`）。`corpus` は今までどおり出所の檔（`type: string`）のまま。
5. `presets.json.schema`＝`secondary.text_id` の説明に `expressive_20s` を足し、頭に `corpus_texts` を足した
   （**enum は無い**ので値の追加で弾かれることはない）。台帳 `notes` に 1 行
   「`secondary.text_id` が `expressive_20s` の行は本文を約 20 秒に縮めて撃った（裁定 112＝…）」を足した。
6. `--text` の候補に `expressive_20s` を足した（`run_secondary.py` の `choices`・README §4）。

### 11-3. 実射（2026-09-09・私設 8090・Radeon 8060S・bf16・v4.1-Small）

```
python .\run_secondary.py --sweep-seed --sweep-ids "co_kana_naisho,co_ofutonp_kiza,vr2_akane_west" \
                          --cfg-scale-speaker 7.0 --text expressive_20s
```

seed 1234 起点・最大 8・末尾判定は 3 条件のまま・`trim_tail` の一巡は要らなかった。
読込 32.1 s・暖機 3.3 s・4 射（1 射 3.3〜20.5 s）。

| 話者 | 採った seed（射数） | 秒数（§10＝30 s 本文 → §11＝20 s 本文） | 末尾 Δ／末尾 50 ms | md5 | バイト数 |
|---|---|---|---|---|---|
| KANA ないしょばなし | **1234**（1 射目） | 33.28 → **20.24 s** | **−56.40 dB**／**−73.96 dBFS** | **`494a3adc…`** | 3,194,924 → **1,943,084** |
| おふとんP きざ | **1234**（1 射目） | 30.60 → **18.32 s** | **−57.32 dB**／**−74.52 dBFS** | **`d606722e…`** | 2,937,644 → **1,758,764** |
| 琴葉茜（関西弁） | **1235**（2 射目・1234 は ⒝ 絶対値 −38.02 dBFS で失格） | 34.92 → **21.00 s** | **−57.92 dB**／**−74.86 dBFS** | **`27a93475…`** | 3,352,364 → **2,016,044** |

**3 本とも末尾 `clean`・11 本とも 3 条件で `clean`** は保たれている。**ほかの 8 本は 1 バイトも触っていない**
（md5 一致を確認済み）。§10 の版（30 s 本文・cfg 7.0）は `build/out/preset-work/secondary/cfg7-30s/`
（射は `secondary/sweep/cfg7-30s/` の 10 本）と N: の写しに退避＝A/B 用。cfg 5.0 の版も `secondary/cfg5/` のまま。

**採否は司令官の耳**（`docs/preset-voices-listening.md` 追記 ④）。合格するまで次へ進まない。
外れた場合の手は 2 つ＝⑴ さらに短い `greeting_10s`（44 字）で撃つ、⑵ 本文はこのままで cfg をさらに上げる。
**（→ 結果＝おふとんP きざ 合格・KANA と 琴葉茜 不合格。手当ては §12＝本文を替えた候補 4 本。）**

### 11-4. 申し送り（合格したあとの本体側）

1. **`$ExpectedAppBytes` は今度こそ動く。** §10 の 3 本はバイト数が同じだったが、今回は**尺が縮んだぶん減った**
   ＝3 本で **9,484,932 → 5,717,892 バイト（−3,767,040）**。`build/installer-build.ps1` の `$ExpectedAppBytes` は
   **主席が測り直す**（この席は編集も `installer-build` の実行もしない）。**wav の本数は 11 のまま**。
2. **潜在は自動で作り直される**＝§10 の申し送り 1 と同じ（`_sources_match()` が source wav の sha256 で見るので、
   中身が変われば `latent_stale` が立ち、次の事前計算で焼き直る）。**檔名も本数も変わっていない。**
3. **この 3 名の 10 s 参照版（`ref_10s_file`）は cfg 5.0＋146 字のまま**＝掃引は採用版（30 s 参照）だけを撃つ。
   台帳 `notes` の「聴いて良い方に差し替えてよい」に従って 10 s 版へ差し替えるなら、
   **先に 7.0＋`expressive_20s` で撃ち直すこと**（そうしないと司令官が命じた是正が 2 つとも黙って戻る）。
4. **同梱の参照ボイスの長さが不揃いになった**＝3 本が 18〜21 s、ほかの 8 本が 24.8〜35.8 s。
   揃えるなら 8 本も `expressive_20s` で撃ち直すことになる（司令官の判断・聴取表 §3 の 0 に問いを置いた）。

---

## 12. 追補（2026-09-09）— 候補モードと、感情の切り替えを弱めた／無くした本文（裁定 113）

司令官の検分（2026-09-09・§11 の 20 秒版 3 本を聴いたうえで）＝逐語
「**co_ofutonp_kiza_secondaryのみ完成として残り2本かな、発話途中で入れ替わる・・・。参照ボイスの動きが読めないね。文章を変えて残り2ファイルを再トライ**」。

読み方＝⑴ **おふとんP きざ は完成**（採用・以後この席は触らない）。
⑵ **KANA ないしょばなし と 琴葉茜（関西弁）は発話の途中で声が入れ替わる**＝まだ落ちている。
⑶ 手当ては「**文章を変えて**再トライ」＝本文をもう一度替える。

### 12-1. 所見の読み方と仮説

**同じ本文（`expressive_20s`・90 字）で 1 名は通り 2 名は落ちた。** §11 の時点の読み（＝本文の長さ＝
その位置まで来たこと）だけでは、3 名が同じ長さの本文で撃たれているのに 1 名だけ通った理由が説明できない。
＝現象は**話者と本文の組**に出る。司令官の「**参照ボイスの動きが読めないね**」はこの非決定性そのものの所見。

**仮説（仮説であって確かめていない）。** Irodori は「！」「？」と絵文字（😊🎉😢😆）を演技に反映する
（§4・`corpus.json` の `secondary.$comment`）。`expressive_20s` は 20 秒のうちに感情を **5 段**振る
（喜び→祝い→悲しみ→持ち直し→呼びかけ）。**強い調子の切り替えそのものが「別人の声」に聞こえている**
のではないか＝参照から離れたのではなく、参照の中の別の面が出ているのではないか、という読み。
**確かめる術はこの席には無い**（席は音を聴けない）ので、**仮説を検証する形の本文を 2 本作って司令官に聴かせる**。

| | 本文 | 字数 | 絵文字 | 「！」 | `emotion_arc` | 狙い |
|---|---|---|---|---|---|---|
| ⒜ | `calm_20s` | **94 字** | 😊 が **1 つだけ**（後ろ寄り） | 無し | **1 段**（終始おだやかな一調子） | 切り替えを**弱める** |
| ⒝ | `plain_20s` | **89 字** | **無し** | 無し | **0 段＝空配列**（平坦） | 切り替えを**無くす** |

⒝ が良くて ⒜ が駄目なら「絵文字 1 つでも振れる」、両方良ければ「5 段の振れが原因だった」、
両方駄目なら「本文の調子ではない」＝**どちらに転んでも次の一手が決まる**ように 2 本並べた。
`emoji_palette.used` は**増やしていない**（😊 は既にある）。

> ⒜ 今日は、来てくれてありがとう。この配信では、みんなのコメントを読みながら、のんびりお話ししていくね😊 途中で気になることがあったら、いつでも声をかけてね。最後まで、ゆっくり楽しんでいこう。

> ⒝ はじめまして。これは、声の見本です。今日は、この声で読み上げをしていきます。ゆっくり、はっきり、最後まで同じ調子で話しますので、落ち着いて聞いてください。それでは、始めましょう。

**字数の見積り。** §11 の実測で KANA は 90 字 20.24 s＝**4.45 字/秒**、琴葉茜は 90 字 21.00 s＝**4.29 字/秒**。
許容 18〜23 s に**両名とも**収まる字数は **81〜98 字**（KANA 4.45 字/秒で 18 s＝80.1 字・
琴葉茜 4.29 字/秒で 23 s＝98.7 字＝両名の共通域。片方だけなら 77〜99 字まで伸びるが、
2 名を同じ本文で撃つので狭い方を採る）。⒜ は 94 字のまま撃った（見込み 21.1／21.9 s）。
⒝ は初稿が 78 字＝17.5／18.2 s と 20 s に届かないので、**撃つ前に 1 句『これは、声の見本です。』（11 字）を足して 89 字**にした。
**実射で足し引きした回数は 0**（4 本とも 1 度で許容の内側に入った）。

### 12-2. 候補モード（道具の側の直し）

**踏みそうだった穴。** 本文を替えて撃つたびに `secondary/<id>_secondary.wav` が上書きされ、
`make_presets.py --copy` を打てば `voices/presets/` と `voices/presets.json` まで動く。
今回は「**どちらの本文が良いか司令官が選ぶ**」ので、**選ばれる前に採用中の音を差し替えてはいけない**
（§11 で完成と言われた おふとんP きざ を巻き添えにする恐れもある）。

**直し方＝`--candidate <名>` を足した**（`run_secondary.py`）。行き先だけが移り、判定も掃引も同じ。

| 何 | 既定 | `--candidate <名>` |
|---|---|---|
| 採用相当の射 | `secondary/<id>_secondary.wav` | `candidates/<名>/<id>_secondary.wav` |
| 掃引の全射 | `secondary/sweep/` | `candidates/<名>/sweep/` |
| その run の台帳 | `logs/run_secondary.sweep.json` | `logs/run_secondary.<名>.sweep.json` |
| 画面の写し | 標準出力だけ | `logs/run_secondary.<名>.log`（`_Tee` で画面と檔の両方へ・**標準エラーも同じ檔**） |
| `run_secondary.result.json` への畳み込み | する | **しない** |

**口の締め方（4 つ）。**

1. **`--candidate` は `--sweep-seed` と併せてのみ**（argparse の段で弾く）。掃引でない候補 run は
   台帳が `run_secondary.<名>.result.json` になり、`--adopt-candidate`（`.sweep.json` を読む）では
   **上げられない候補**ができてしまう。表の「その run の台帳」は掃引の話だけで済むよう、
   非掃引の候補という筋そのものを閉じた。
2. 候補の掃引では **`--sweep-ids` を必須**（候補置き場には採用版が無いので、
   tail_suspect の自動拾い＝`tail_suspect_ids()` が「対象 0 本」で黙って終わってしまう）。
3. **`<名>` を検める**＝`[A-Za-z0-9_.-]` の 1〜64 字だけ（`.`／`..` は不可）。名はそのまま
   `candidates/<名>/` と `logs/run_secondary.<名>.*` に埋まるので、`..\..\secondary` のような値を
   通すと**採用側に直接書けてしまい**、この口の唯一の売り（採用中の成果物に触れない）が名前 1 つで崩れる。
   `--adopt-candidate` の名にも同じ検査を掛ける。
4. **`--adopt-candidate` と `--candidate` は同時に渡せない**（採用の分岐が先に `return` するので、
   黙って片方が捨てられる＝「撃ち直したつもりが別の候補を採用に上げていた」を防ぐ）。

**画面の写しの取り方。** `_Tee` の設置から後を `try/finally` で包み、`sys.stdout` と `sys.stderr` の
**両方**を同じ檔へ流す（1 行ごとに flush）。`SystemExit` は文言を檔に落としてから投げ直し、
それ以外の例外は追跡も落とす。venv が無い・`--sweep-ids` が無い、といった**サーバを起こす前の停止**でも
理由が候補ログに残るようにするため（初版は `_Tee` が `try` の外で、この経路が檔に 1 行も残らなかった）。

**採用の口も同じ回で入れた（今回は打っていない）。** `--adopt-candidate <名> --sweep-ids <ids>`＝
サーバを起こさずに ⑴ 候補の wav を `secondary/` へ複写し、⑵ **その id の掃引の射
（`candidates/<名>/sweep/<id>_seed*.wav`）を `secondary/sweep/<名>/` へ複写**し、⑶ 候補 run の台帳から
**その id の項目と掃引の行だけ**を `merge_result()` で `run_secondary.result.json` に畳む。項目の
`file`／`path` は複写先に書き換え、`adopted_from_candidate`／`candidate_path` に出どころを刻む。

⑵ が要る理由＝台帳の `sweep_file` と `attempts[].file` は**裸の檔名**（`<id>_seed<N>.wav`）で、
README §4-2 も聴取表も掃引の射の置き場を `secondary/sweep/` として案内している。射を連れて行かないと、
採用後の `run_secondary.result.json` が**そこに存在しない檔名**を指し、「どの射を採ったのか」を後から辿れない。
箱を `secondary/sweep/<名>/` に分けるのは `cfg5/`・`cfg7-30s/`・`20s-95chars/` と同じ要領で、
既存の射（同じ id・同じ seed 番号の別の run）を上書きしないため。項目と行には `sweep_dir`
（複写先の絶対路）を刻んで、裸の檔名が指す箱を固定する。
行が自分の `text_id`／`text`／`seed`／`cfg_scale_speaker`／`generated_at` を持っているので
（裁定 111・112 でそう直した）、**畳んだあとも素性は候補 run のもの**が残る。
そのあとは通常どおり `verify_wavs.py` → `make_presets.py --copy`。

### 12-3. 実射（2026-09-09・私設 8090・Radeon 8060S・bf16・v4.1-Small）

```
python .\run_secondary.py --sweep-seed --sweep-ids "co_kana_naisho,vr2_akane_west" \
                          --cfg-scale-speaker 7.0 --text calm_20s  --candidate calm_20s
python .\run_secondary.py --sweep-seed --sweep-ids "co_kana_naisho,vr2_akane_west" \
                          --cfg-scale-speaker 7.0 --text plain_20s --candidate plain_20s
```

`cfg_scale_speaker` は **7.0 のまま**（裁定 111 の指示は撤回されていない）。seed 1234 起点・最大 8・
末尾判定は 3 条件のまま・`trim_tail=false` の一巡は要らなかった。読込 28.6／28.1 s・暖機 2.9／2.8 s。
（`plain_20s` は下の「クリップの門」の是正で **2026-09-09 04:01 に同じ命令で撃ち直した**＝読込 28.1 s・暖機 2.8 s。
琴葉茜は同じ seed 1234 で **md5 まで一致**した＝同じ seed は同じ音を返す、が実射で確かめられた。）

| 候補 | 話者 | 採った seed（射数） | 秒数 | 読み速 | 末尾 Δ／末尾 50 ms | 末尾 | クリップ | md5 | バイト数 |
|---|---|---|---|---|---|---|---|---|---|
| `calm_20s`（94 字） | KANA | **1234**（1 射目） | **20.08 s** | 4.68 字/秒 | −52.40 dB／−73.77 dBFS | **clean** | `norm`（run 2） | `021feb6a…` | 1,927,724 |
| `calm_20s` | 琴葉茜 | **1234**（1 射目） | **20.48 s** | 4.59 字/秒 | −56.82 dB／−72.91 dBFS | **clean** | `norm`（run 2） | `610ec04b…` | 1,966,124 |
| `plain_20s`（89 字） | KANA | **1239**（**6 射目**・1234〜1237 は delta+abs+rise で失格・**1238 は末尾 clean だがクリップで失格**） | **21.48 s** | 4.14 字/秒 | −52.22 dB／−74.15 dBFS | **clean** | `norm`（run 2） | `e7d54eed…` | 2,062,124 |
| `plain_20s` | 琴葉茜 | **1234**（1 射目） | **21.24 s** | 4.19 字/秒 | −57.69 dB／−74.24 dBFS | **clean** | `norm`（run 2） | `fa12fee4…` | 2,039,084 |

**4 本とも許容 18〜23 s の内側・4 本とも末尾 3 条件で clean・4 本とも `norm`。**
副産物の目盛り＝**読み速は本文でも動く**（`expressive_20s` 4.45／4.29 → `calm_20s` **4.68／4.59** →
`plain_20s` **4.14／4.19**）＝**平坦な地の文ほどゆっくり読まれる**。§4 の「約 4.6 字/秒」は
本文の調子込みで **4.1〜4.7 字/秒**の幅を見ておくのが正しい（README §6 に足した）。

**掃引の合格条件にクリップの門を足した（この回の是正）。** 初回は `plain_20s` の KANA に
seed 1238 の射（**`max_clip_run=3`＝`verify_wavs.py` の `clipped`**・full scale がちょうど 3 サンプル連続）を
候補として採ってしまった。原因は掃引の採用分岐が**末尾 3 条件だけ**を見ていたこと
（`if t["tail_clean"]:`）で、`verify_wavs.analyze` の `clipped` を一切見ていなかった。
同梱の 11 本はいずれも `max_clip_run` 0〜2 で `clipped=false`＝**初の潰れた個体**になるところだった。
しかも掃引の seed 列（1234〜1241）のうち **1239〜1241 の 3 射が未使用のまま**で、
安い代替が残っていた（実際、撃ち直したら **seed 1239 が 1 射で clean かつ `norm`**）。

直し＝`tail_of()` が `clipped`／`max_clip_run`／`clipped_samples` も持ち帰り、採用の分岐を
**「末尾 3 条件 clean **かつ** 潰れていない」**にした。落ちた射は画面に `[CLIP?] why=clip(run=3)` と出て
次の seed へ進む。門を外す `--allow-clip` も置いたが既定は on。台帳にも残す＝
`seed_sweep.require_no_clip`・行の `require_no_clip`／`clip_rejected`・射ごとの `clipped`／`max_clip_run`／`clip_reject`。
初回の記録（潰れた射も含む）は `candidates/plain_20s/sweep/co_kana_naisho_seed1238.wav` と
`logs/run_secondary.plain_20s.pre-clipfix.log`／`.pre-clipfix.sweep.json`／
`logs/verify_candidates.plain_20s.pre-clipfix.json` に残してある。

**頭の無音は候補 2 本が採用 11 本より長い。** `lead_silence_s`＝calm/KANA 0.464・**calm/琴葉茜 0.930**・
plain/KANA 0.383・**plain/琴葉茜 0.883**。採用中の 11 本は **0.0〜0.726 s**（最長は つくよみちゃん 0.726）なので、
琴葉茜の候補 2 本は採ればそのまま**同梱ボイスで最長の頭の無音**になる。`make_presets.py --copy` は
wav をそのまま複写する（頭を詰めない）ので、採用するなら ⑴ 頭を詰める、⑵ 別 seed で撃ち直す、
⑶ そのまま容れる、のどれかを選ぶことになる（聴取表 追記 ⑤ の表に lead／trail の欄を足した）。

### 12-4. この回で動かしていないもの（大事）

- `voices/presets/*.wav`（11 本）は **md5 が HEAD の blob と一致**（`494a3adc…` ほか）。
  `voices/presets.json` は **`git status` で無変更**＝これが正しい物差し（`.gitattributes` の `* text=auto` で
  作業ツリーは CRLF・HEAD の blob は LF なので、**台帳だけは md5 が一致しないのが正常**。
  作業ツリーの md5 は `28eab8d7…`）。`make_presets.py` は打っていない。
- `logs/run_secondary.result.json` も動いていない（候補は畳み込まない）。この檔は **git 管理外**
  （`.gitignore` の `out/`）なので HEAD と突き合わせようがなく、**裁定 112 の run
  （畳み込み `2026-09-09T02:37:35+0900`）のまま**であることで確かめる。
- **おふとんP きざ は完成扱い**＝候補も撃っていない（司令官の「co_ofutonp_kiza_secondaryのみ完成」）。
- `build/installer-build.ps1` の `$ExpectedAppBytes` は**触っていない**（アプリ木が動いていないので動かす理由が無い）。
  候補が採用されたら §11-4 の 1 と同じ扱いで**主席が測り直す**。

### 12-5. 申し送り

1. **採否は司令官の耳**（`docs/preset-voices-listening.md` 追記 ⑤ の順路＝話者ごとに ⒜ calm → ⒝ plain →
   一次 30 s と突き合わせ）。選ばれたら `--adopt-candidate <名> --sweep-ids "co_kana_naisho,vr2_akane_west"` →
   `verify_wavs.py` → `make_presets.py --copy`。
2. **話者ごとに別の本文を採ってもよい**（`--adopt-candidate` は id を名指しするので、
   KANA だけ `plain_20s`・琴葉茜だけ `calm_20s` という採り方ができる）。そのとき台帳の
   `secondary.text_id` は行ごとに分かれる（裁定 112 でそう直してある）。
3. **両方とも駄目だった場合の手**＝⑴ さらに短い `greeting_10s`（44 字）で撃つ、⑵ `cfg_scale_speaker` を
   さらに上げる（例 9.0）、⑶ 参照ボイス（一次 wav）そのものを録り直す＝この 2 名は一次が
   39.22 s／41.09 s と長く、しかも小さめ（RMS −26.1／−25.8 dBFS）。仮説が外れたときはここが次の的。
4. **同梱の参照ボイスの長さと調子が不揃いのまま**＝§11-4 の 4 と同じ問いが残っている
   （8 本は 146 字の `expressive_30s`）。全体を揃えるかは司令官の判断。
5. **採用の回に一緒に直すもの**＝⑴ `presets.json.schema` の `secondary.text_id` の説明に採った id
   （`calm_20s`／`plain_20s`）を足す＋台帳 `notes` に 1 行（候補のうちは打たない＝README §6 の手順 4）、
   ⑵ 琴葉茜の**頭の無音 0.88〜0.93 s**（§12-3）を詰めるか容れるかを決める、
   ⑶ `$ExpectedAppBytes` の測り直し（§12-4）。
   → **⑴ と ⑵ は §12-6 で片付いた。⑶ は主席へ残る。**

### 12-6. 採用（2026-09-09・裁定 114）— `calm_20s` を 2 名とも採り、同梱 11 本が確定した

司令官の検分（§12-3 の候補 4 本を聴いたうえで）＝逐語
「**calm採用。 インストーラーくみなおしとマージを頼む。**」

読み方＝⑴ **① `calm_20s` を KANA・琴葉茜 の 2 名とも採用**（話者ごとに分けなかった＝§12-5 の 2 の選択肢は使わなかった）。
⑵ **② `plain_20s` は採らない**（＝候補のまま残す。「駄目だった」とまでは言われていない＝A/B の控えとして生きる）。
⑶ 続きは**インストーラーの組み直しとマージ**＝便 P の音づくりはここで終わり、以後は配布側の仕事。

**打った命令（この席・サーバは起こしていない＝新しい音は 1 本も作っていない）。**

```powershell
cd "C:\Users\mugonkun\source\repos\irodori-tts-for-yomiwakechan\tools\preset-voices"
$W = "C:\Users\mugonkun\source\repos\irodori-tts-for-yomiwakechan\build\out\preset-work"
python .\run_secondary.py --adopt-candidate calm_20s --sweep-ids "co_kana_naisho,vr2_akane_west"
python .\verify_wavs.py "$W\secondary" --out "$W\logs\verify_secondary.json"
python .\make_presets.py --copy
```

＝§12-2 で入れておいた採用の口をそのまま使った。`candidates/calm_20s/<id>_secondary.wav` が
`secondary/<id>_secondary.wav` へ、掃引の射（各 1 射）が `secondary/sweep/calm_20s/<id>_seed1234.wav` へ複写され、
候補 run の台帳から**その 2 id の項目と掃引の行だけ**が `run_secondary.result.json` に畳まれた
（`items[].adopted_from_candidate: "calm_20s"`・`candidate_path`・`sweep_dir` が刻まれている）。
**直下の `secondary/sweep/<id>_seed<N>.wav`（§11 の `expressive_20s` の射）は箱が別なので上書きされていない**
＝退いた射は `co_kana_naisho_seed1234.wav`（md5 `494a3adc…`）・`vr2_akane_west_seed1235.wav`（md5 `27a93475…`）として残る。

`verify_wavs.py` の測り直し＝**22 本中 21 本 clean**。残る 1 本は `co_ofutonp_kiza_ref10_secondary.wav`
（10 s 参照版・末尾 Δ +4.01 dB）で、**一度も採用していない**（採用は 30 s 参照版）＝§11 以前から同じ状態。

**確定した同梱 11 本（`voices/presets/<id>.wav`・台帳 `voices/presets.json`）。**

| # | id | `text_id` | 字数 | `cfg_scale_speaker` | seed | 秒数 | md5（頭 8 桁） | バイト数 |
|---|---|---|---|---|---|---|---|---|
| 1 | `vv_mochiko_sexy` | `expressive_30s` | 146 | 5.0（既定・欄無し） | 1235 | 30.76 s | `0d2e0175…` | 2,953,004 |
| 2 | `vv_chibishikijii` | `expressive_30s` | 146 | 5.0（既定・欄無し） | 1235 | 31.68 s | `247aef80…` | 3,041,324 |
| 3 | `co_tsukuyomi` | `expressive_30s` | 146 | 5.0（既定・欄無し） | 1240 | 28.24 s | `cdc00aba…` | 2,711,084 |
| 4 | **`co_kana_naisho`** | **`calm_20s`** | **94** | **7.0** | **1234** | **20.08 s** | **`021feb6a…`** | **1,927,724** |
| 5 | `co_mana_isshoukenmei` | `expressive_30s` | 146 | 5.0（既定・欄無し） | 1234 | 30.16 s | `4755b737…` | 2,895,404 |
| 6 | `co_ofutonp_kiza` | `expressive_20s` | 90 | 7.0 | 1234 | 18.32 s | `d606722e…` | 1,758,764 |
| 7 | `co_ofutonp_normal_v2` | `expressive_30s` | 146 | 5.0（既定・欄無し） | 1240 | 26.32 s | `42c0d528…` | 2,526,764 |
| 8 | **`vr2_akane_west`** | **`calm_20s`** | **94** | **7.0** | **1234** | **20.48 s** | **`610ec04b…`** | **1,966,124** |
| 9 | `vr2_yoshida` | `expressive_30s` | 146 | 5.0（既定・欄無し） | 1234 | 24.84 s | `a52ec2c9…` | 2,384,684 |
| 10 | `vr2_tsukuyomi_ai` | `expressive_30s` | 146 | 5.0（既定・欄無し） | 1238 | 35.76 s | `572f6f54…` | 3,433,004 |
| 11 | `vr2_tsukuyomi_shota` | `expressive_30s` | 146 | 5.0（既定・欄無し） | 1234 | 32.72 s | `de8e6b63…` | 3,141,164 |

12 行目 `cevio_maki_en` は `status: "skipped"`（decisions 27）＝`primary`／`secondary` とも `null`。
**「5.0（既定・欄無し）」は台帳に `cfg_scale_speaker` の欄が無いこと**を指す＝撃った時に明示していない
＝上流既定 5.0（§10）。値を後から書き足していない。
**合計 28,804,324 → 28,739,044 バイト（−65,280）。** 動いたのは 4 と 8 の 2 本だけで、
**ほかの 9 本は `make_presets.py --copy` の前後で md5 が一致**（複写しても内容は同じ）。
本文は **3 世代が混ざったまま確定**した＝`expressive_30s` 8 本・`expressive_20s` 1 本・`calm_20s` 2 本。

**琴葉茜の頭の無音 0.930 s はそのまま容れた（§12-3 の 3 択のうち ⑶）。**
司令官は**この音を聴いたうえで採った**（逐語に詰めろの指示は無い）ので、席の判断で頭を切ることはしない。
＝`vr2_akane_west.wav` は**同梱 11 本で最長の頭の無音**（次点は `co_tsukuyomi` の 0.726 s）。
詰めるなら司令官の指示で別便を立てる（`make_presets.py --copy` は wav をそのまま複写する＝頭を詰める口は無い）。

**仮説（§12-1）の帰結。** 「強い調子の切り替えそのものが別人の声に聞こえていた」という読みは、
**① だけで足りた＝絵文字 1 つ・`emotion_arc` 1 段なら振れても入れ替わらない**という形で司令官の耳が支持した。
ただし採用の逐語は「calm採用」だけなので、**② `plain_20s` が駄目だったとまでは言えない**
（＝「5 段の振れが原因だった」も「絵文字 1 つでも振れる」も、この 1 語からは判じ切れない）。
副産物の目盛り（読み速は本文でも動く・平坦なほど遅い）は §12-3 のまま生きている。

**この回で直した台帳と形（§12-5 の 5 の ⑴）。**

- `tools/preset-voices/make_presets.py` の `notes`＝**裁定 114 の 1 行を足した**
  （`calm_20s` の行は絵文字 1 つ・「！」なしの 94 字で撃った／`plain_20s` は不採用）。
  併せて `expressive_20s` の註を「**この行は `co_ofutonp_kiza` だけ**」に改めた（3 本と書いたままでは嘘になる）。
- `tools/preset-voices/presets.json.schema` の `secondary.text_id` の説明＝`calm_20s`（採用）と
  `plain_20s`（**corpus に在るが採用した行は無い**）を足し、今の内訳（8／2／1）を書いた。
  `corpus_texts` の説明も裁定 114 まで進めた。**enum は置かない**（README §6 の手順 4 のまま）。
- `voices/presets.json`＝`make_presets.py --copy` の再実行で `notes` と `corpus_texts` だけが動いた
  （**wav は 11 本とも md5 不変**）。`corpus_texts` は `["calm_20s","expressive_20s","expressive_30s"]`。
- `docs/preset-voices-listening.md`＝**追記 ⑥**（この採用の記録・退いた射の在処・11 本の内訳）、
  §2 の凡例に **⒞ `calm_20s` 2 本**を足し ⒝ を 1 本に直した、§2 の 4（KANA）と 8（琴葉茜）の行、
  §1 の A 群・B 群・C 群の数値（打ち消し線で履歴を残した）、§4 の「数値の出どころ」。

**この席が触っていないもの。**

- `build/installer-build.ps1`（`$ExpectedAppBytes` を含む）＝**1 行も触っていない**。
  同梱 wav が 65,280 バイト減ったので**測り直しが要る**が、それは主席の仕事（§11-4 の 1・§12-4 と同じ扱い）。
  司令官の「インストーラーくみなおし」はこの席への指示ではなく、便の続きの話として受けている。
- `corpus.json`（`calm_20s`／`plain_20s` の本文も `$calibration` もそのまま）・`run_secondary.py`・`verify_wavs.py`。
- 8 名分の wav と台帳の行。**commit もしていない。**
- **`N:` の引き渡し木**（`N:\temp_for_claudecode_agents\irodori-ywk\preset-voices\`）＝檔の席は複写していない（**主席が 04:5x に揃えた＝下記**）＝
  `secondary\co_kana_naisho_secondary.wav` は `494a3adc…`・`vr2_akane_west_secondary.wav` は `27a93475…`＝
  **どちらも退いた §11（追記 ④）の射**、`presets.json` も `28eab8d7…`（`corpus_texts` が `[expressive_20s, expressive_30s]` の採用前の版）、
  聴取表の写しも追記 ⑥ を含まない旧版（2026-09-09 実測）。**採った音そのもの**は `candidates\calm_20s\` に在る
  （`021feb6a…`／`610ec04b…`＝S: と一致）。裁定 111・112・113 は毎回「N: 差し替え済み・hash 一致」まで記帳しているので、
  この便でも主席が揃えた＝`secondary/<id>_secondary.wav`（2 本・`021feb6a…`／`610ec04b…`）・`secondary/sweep/calm_20s/`・`secondary/run_secondary.result.json`・`presets.json`・聴取表を N: に複写し md5 一致を確認（2026-09-09 04:5x）。
- **`decisions.md`**＝114 の条は主席がこの便のコミットで起こした（檔の席の実測時点では 113 止まりだった）。
  この §12-6 と聴取表の追記 ⑥ が **114 の一次記録**で、条を起こすのは主席
  （`schema`・台帳 `notes`・README が参照している「裁定 114」の行き先は、条が立つまでこの 2 つ）。

**残る問い（司令官へ・席は決めない）。** ほかの 8 本は `expressive_30s` 146 字・cfg 5.0 のままで、
同梱ボイスは **18.32〜35.76 秒**と長さも調子も不揃い（§11-4 の 4・§12-5 の 4 と同じ問い）。
**8 本も `calm_20s`＋cfg 7.0 で撃ち直して揃えるか、このまま出すか**は司令官の判断＝
揃えるなら 8 本の実射と `$ExpectedAppBytes` の再測が要り、**今合格している 8 本が別の射に変わる危険**も負う。
聴取表 §3 の 0-1 に同じ問いを開いたまま置いた。
