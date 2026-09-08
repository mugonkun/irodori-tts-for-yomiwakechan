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
> 離れたので、手当ては cfg ではなく**本文の長さ**に移った＝**§11（裁定 112）が今の版**。
> `cfg_scale_speaker` **7.0 という値そのものは §11 でも据え置き**なので、この節の口の説明はそのまま生きている。

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
