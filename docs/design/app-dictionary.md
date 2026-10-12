# このアプリ側の辞書（v2.0.11・`decisions.md` 166）― 設計

> 司令官（2026-10-12）＝「どのTTSも本体辞書とTTS辞書の二段構えになってるから、実装する方向で。」「設計は本体辞書やvoicevoxなどのTTSを参考に。」
> 正典＝`decisions.md` 166 と `docs/contract.md` ⑶ 3-1・⑹。食い違ったら契約テスト（`tests/contract/test_dictionary.py`）と
> xUnit（`DictionaryTests.cs`）が正。絵文字の扱い（v2.0.10）は `emoji-filter.md`。

## 1. 何を足すか

**読み上げる前に「文字 → 読み」を置き換える表**を、このアプリが持つ。読み分けちゃん2 の辞書（本体辞書）と
このアプリの辞書（TTS 辞書）の**二段構え**＝本体辞書で変換された文がこのアプリに届き、その文にこのアプリの辞書が
掛かり、その後に絵文字の扱い（v2.0.10）が掛かる。

```
読み分けちゃん2 ── 本体辞書（圧縮→対応表） ── POST /v1/audio/speech ──▶ このアプリ
                                                                      │ ⑴ このアプリの辞書（対応表・上から順）
                                                                      │ ⑵ 絵文字（45 種は素通し・他は取り除く・空なら無音）
                                                                      ▼
                                                                     Irodori-TTS
```

用途＝⑴ 非対応の絵文字に読みを付ける（🎉→ぱちぱち。付けなければ v2.0.10 どおり取り除かれる）⑵ 読み違える語に読みを
付ける（固有名詞・英字の略語・ネットの語）。読み分けちゃん2 を使わない人（このアプリの〔しゃべらせる〕だけの人）にも効く。

## 2. 参考にした物

| 製品 | 形 | 採った所 | 採らなかった所 |
|---|---|---|---|
| 読み分けちゃん2 の辞書設定（本体 `DictionaryTextTransform`・`decisions.md` §14） | 対応表（From→To・**上から順**・Ordinal の部分一致・正規表現なし）＋繰り返し圧縮。即反映（保存釦なし）。本文のみ | 対応表の形・順序の意味・即反映・本文のみ・正規表現なし | 繰り返し圧縮（本体が先にやる。二重に掛けない） |
| VOICEVOX／AivisSpeech「読み方＆アクセント辞書」 | 単語・読み（かな）・アクセント・単語優先度。エンジンの `/user_dict` API。設定画面の中・左に一覧 | 「単語／読み」の 2 欄・一覧＋追加の画面 | アクセント・優先度（Irodori-TTS は読みをそのまま本文として受ける＝アクセントの口が無い・優先度は「順序」で足りる）・HTTP の口（本体はこの辞書を叩かない） |
| VOICEPEAK の辞書 | 単語・読み・アクセント | 同上 | 同上 |

Irodori-TTS には G2P（文字→音素）も読みの指定もなく、**本文の文字列そのものが読み**なので、辞書は「文字列の置換」で
ある。かな・漢字・英字・絵文字・記号のどれでも From にも To にも書ける。

## 3. 規則

| # | 規則 | 理由 |
|---|---|---|
| 1 | 対応表（From → To）。**上から順**に、それぞれ本文中の**すべての一致**を置き換える（Ordinal・大小区別・部分一致） | 本体辞書と同じ作法＝利用者が 2 つの辞書で同じ考え方で済む |
| 2 | From が空の行は無視。To は空でよい（＝その文字を読まない） | 本体と同じ。「読まない」を表現できる |
| 3 | 掛かるのは `input`（本文）だけ。`caption`（演技指示）には掛けない | 絵文字（Q14）と同じ |
| 4 | **このアプリの辞書 → 絵文字の扱い** の順 | 辞書で読みを付けた絵文字が消えない。辞書が絵文字の一部だけを置き換えて残した部品（🏻 など）は絵文字の段が掃除する |
| 5 | 正規表現なし・繰り返し圧縮なし・アクセントなし | 本体が圧縮を先にする。Irodori-TTS にアクセントの口が無い |
| 6 | 即反映＝登録・削除・並べ替えのたびに檔へ書き、**次の読み上げから効く**（アプリの再起動も読み上げの止め直しも要らない） | 本体・VOICEVOX と同じ。wrapper は要求ごとに檔の更新時刻を見る |
| 7 | 檔が壊れていたら**空の辞書として続ける**（読み上げを止めない）。理由はランチャの画面と `/ywk/status` に 1 行 | 設定 1 檔で読み上げ不能にしない（`settings.json` と同じ） |
| 8 | ログ＝辞書で置き換えた要求にだけ 1 行 `dictionary applied: hits=N chars=A->B`（**From／To／本文は書かない**） | 本文も辞書の中身も記録に残さない |

## 4. 檔（`<データ樹>\dictionary.json`）

```json
{"schema": 1,
 "entries": [{"from": "🎉", "to": "ぱちぱち"},
             {"from": "www", "to": "わら"}]}
```

- 置き場＝利用者データの根（Radeon（ROCm）＝`%LOCALAPPDATA%\irodori-tts-ywk-radeon\`・RTX（CUDA）＝`…-cuda\`）＝
  `settings.json`・`voices\` の隣。版ごとに別（`decisions.md` 133）。
- 書き手＝ランチャ（`JsonDictionaryStore`＝temp→差し替え・UTF-8・日本語を `\uXXXX` に潰さない＝`JsonSettingsStore.JsonOptions`。
  **BMP の外の絵文字（🎉）は .NET の符号器が `🎉` の形に書く**＝JSON として正しく、wrapper はどちらの形も読む）。
  読み手＝wrapper（`server/ywk_dictionary.py`＝大きさと更新時刻で読み直す）。手で編集してもよい（JSON・絵文字は素のまま書いてよい）。
- 欄を足すのは `schema` を上げない。`from` が空・文字列でない行は読み飛ばす。

## 5. 画面（ランチャ・タブ「辞書」＝`TabDictionary`）

- タブ帯＝発話テスト・話者と参照ファイル・**辞書**・設定・ログ（本体の「辞書設定」が独立のタブであるのに倣う。設定タブは
  「適用」で書く作法なので混ぜない）。
- 左＝説明 1 行＋対応表（`DictionaryGrid`・列「文字」「読み」）＋件数の薄字。右＝「登録する」（①置き換える文字 ②読み・
  〔登録する〕）と「選んでいる行」（〔削除〕〔上へ〕〔下へ〕）。下＝結果の 1 行（保存しました／保存できませんでした）。
- 例の 1 行（🎉→ぱちぱち・www→わら）を画面に置く＝何を書けばよいかを文で説明しない。
- 試す場＝〔発話テスト〕（同じ口を叩くので辞書が効く）。
- AutomationId（新設・退役なし）＝`TabDictionary`・`DictionaryView`・`DictionaryNoteText`・`DictionaryExampleText`・`DictionaryGrid`・
  `DictionaryCountText`・`DictionaryFromBox`・`DictionaryToBox`・`DictionaryAddButton`・`DictionaryRemoveButton`・
  `DictionaryUpButton`・`DictionaryDownButton`・`DictionaryMessageText`。
- 語＝「辞書」「文字」「読み」（憲章 §6 の隠す語は使わない）。

## 6. wrapper（`server/ywk_dictionary.py`・`ywk_server.py`）

- `load_table(path)`＝`dictionary.json` を読む（無ければ空・壊れていれば空＋`error`）。`(size, mtime_ns)` が同じなら再読しない。
- `apply(text, table) -> (text, hits)`＝規則 1〜2。
- `prepare_speech_input`＝⑴ 辞書 → ⑵ 絵文字（既存）→ ⑶ 空の判定（既存）。`chars` の記録は辞書の前後も持つ。
- `/ywk/status.dictionary`＝`{"file": "dictionary.json", "entries": N, "error": str|null}`（欄を足すだけ・`schema` 不変）。
- HTTP の口は増やさない（本体はこの辞書を叩かない・ランチャは檔を直接書く）。

## 7. 契約・文書

- `docs/contract.md` ⑶ 3-1（辞書の 1 項）・⑹（`dictionary` の欄）・⑻（上げない）。
- `docs/guide.md` §3（辞書の使い方）・§6（読み違えるとき）。`site/developers.html` 本文の項に 1 行。リリース文 v2.0.11。
- 本体席へ＝通知のみ（`docs/ben-f-handoff.md` §14＝順序は 本体辞書 → このアプリの辞書 → 絵文字）。

## 8. 試験

- 契約テスト `tests/contract/test_dictionary.py`＝檔が無い・在る・壊れている／順序／全一致の置換／空 To／`caption` に掛からない／
  辞書→絵文字の順（🎉→ぱちぱち が残る・🙏→おねがい の 🏻 が掃除される）／更新時刻で読み直す／`/ywk/status.dictionary`／ログ 1 行。
- xUnit `DictionaryTests.cs`＝檔の往復・壊れた檔・VM の登録／削除／並べ替え／即保存・空 From は登録しない・AutomationId の新設。

## 9. 未決・別便

- 正規表現・単語境界・大小無視＝入れない（本体と同じ）。要望が出たら別便。
- 取り込み／書き出し釦＝入れない（檔は JSON＝〔設定 › ファイルの場所〕から開ける）。
- 本体の辞書との「どちらに書くか」の案内＝guide に 1 行（コメントだけならどちらでも。〔しゃべらせる〕にも効かせるならこのアプリ）。
