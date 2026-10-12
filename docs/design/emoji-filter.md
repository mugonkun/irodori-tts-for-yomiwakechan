# 絵文字の扱い（v2.0.10・`decisions.md` 165）― 設計

> 正典＝`decisions.md` 165 と `docs/contract.md` ⑶ 3-1。調査＝`research/report/emoji-survey-2026-10-12.md`。
> 本檔は実装の形の記録。食い違ったら契約テスト（`tests/contract/test_emoji.py`）が正。

## 1. 何を直すか

読み分けちゃん2 から届く本文に、Irodori-TTS（v4.1-Small）が注釈として学習した **45 種以外の絵文字**が入ると、
継続時間の予測器（token ごとの予測 frame の総和）がその絵文字のぶんの時間を要求し、読む中身が無いまま
音響側が埋める＝「意味の通らない語句」。配布版の wrapper が本文を上流へ渡す前に絵文字を扱う。
本体（yomiwakechan2）は不触＝要求 JSON・`error.code`・期限・鍵は不変。**このアプリに辞書は無い**（本体の辞書が
本文に先に掛かってから届く）。

## 2. 規則（`server/ywk_emoji.py`・1 回の走査）

| 段 | 対象 | 扱い |
|---|---|---|
| a 正規化 | 45 種の骨格（飾り＝肌色・VS15・VS16・tag を剥いだ形。😮‍💨 は骨格に ZWJ を含む）に一致する塊 | **正本の綴りへ**（⏸ → ⏸️・⏩️ → ⏩・🙏🏻 → 🙏・😊️ → 😊）。😮💨（ZWJ 欠け）は補わない＝😮 ＋ 💨 削除 |
| b 畳み | 上流 `normalize_text` が字に直す 22 点（‼ ⁉ ™ ℹ Ⓜ ㊗ ㊙ 🈁 🈂 🈚 🈯 🈲〜🈺 🉐 🉑）・♥ | **字へ**（㊗ → 祝・‼ → !!・♥ → ♡）＝上流の処理の前倒し。飾りが付いていても同じ |
| b' 裁定の畳み | ❗❕ → `!`・❓❔ → `?`（Q8）・キーキャップ `1️⃣` → `1`（Q9） | 字へ |
| c 削除 | それ以外の塊（基底＋飾り・ZWJ 連結・旗・tag 旗・文字既定の絵文字 © ▶ ⚠ ❤ ☺ …）と**孤立部品**（肌色・単独 RI・VS15／VS16・ZWJ・U+20E3・LANGUAGE TAG・tag 文字） | **取り除く**（字数は詰まる） |
| d 切る | 畳みで伸びた分 | 書記素（`\X`）の境で 4096 符号点に |

式（`regex`・Unicode 17.0 の表）：

```
DECOR  = [\p{Emoji_Modifier}︎️\U000E0020-\U000E007F]
塊     = \p{Regional_Indicator}{2} | [0-9#*]️?⃣ | \p{Extended_Pictographic}DECOR*(?:‍\p{Extended_Pictographic}DECOR*)*
孤立   = [\p{Emoji_Modifier}\p{Regional_Indicator}︎️‍⃣\U000E0001\U000E0020-\U000E007F]
```

網羅試験（検分席・2026-10-12）＝emoji-test 17.0 の 5,225 列（18.0 の 5,244 列も）で「1 単位・残骸 0・文中・連続・
二度掛け不変」全数通過。45 種の亜種 500 通り全部が正本へ。割当済みの非絵文字 295,797 字で変わるのは
VS15 と LANGUAGE TAG だけ。日本語の記号 110 字・顔文字 33 例・文書 137,774 行で誤爆 0。

## 3. 置き場と順（`server/ywk_server.py`）

`_create_speech`＝`normalize_speech_body` → `validate_speech_body` → `resolve_default_voice` → `SpeechRequest`
→ **`prepare_speech_input`** → （無音なら `_silent_speech_response`）→ 上流 `create_speech`。

`prepare_speech_input` の中：
1. `.NET` の `IsNullOrWhiteSpace` が真の本文（本体は送らない形）は触らない＝上流の 400 `ywk_empty_input` のまま。
2. a〜d の書き換え。
3. **上流が本文より前に掛ける検査を同じ順で**＝`_validate_speech_payload`（model）・`_stream_format_is_sse`・
   `_resolve_voice`（話者）・`_build_sampling_request`＋`_validate_sampling_request`（欄の値）・`_speech_chunks`（分割の欄）。
   書き換えで空になった本文は検査用の probe に `"x"` を置く（他の検査は本物の body で走る）。
4. 上流が切る塊ごとに `normalize_text` を掛け、**読む物（文字・数字・対応絵文字）が無い塊を落とす**。
   全部落ちれば `silent`。一部なら残りを繋いで上流へ（上流は再び同じ所で切る）。
5. ログ 1 行（変化があったときだけ）＝`emoji filtered: removed=… normalized=… folded=… chunks_dropped=n chars=A->B silent=1`。
   本文は書かない。見えない部品は `U+XXXX`。種類は 10 までで畳む。

`_silent_speech_response`＝PCM16・mono・0.1 s の無音（標本 4,800）・標本化周波数は読込済みなら
`runtime.codec.sample_rate`、未読込なら 48000（実測値）。ヘッダ＝`Content-Disposition`・`X-Irodori-Seed`（指定値か 0）・
`X-Irodori-Total-To-Decode: 0.000000`・`X-Ywk-Silent: 1`・`X-Ywk-Emoji-Removed`。SSE＝無音の `audio_chunk`
1 枠（`index` 0・`text` `""`）＋`done`（`chunks` 1）。上流は呼ばない（読込を起こさない・`_runtime_error` に触らない）。

## 4. 契約・`/params`・文書

- `docs/contract.md`＝⑶ 3-1（規則）・3-2 の新段「書き換え（400 にしない）」・3-3 の表（空白のみの注と無音の行）・
  3-5（`text` は書き換え後・無音の枠）・⑻（`schema` を上げない）・題目。
- `/params`＝`request.input.description`・`rules.emoji`（`passthrough` 45・`others: removed`・`nothing_to_pronounce: silent_wav_200`）。
- 利用者向け＝`docs/guide.md` §3・§6「読み上げに、意味の通らない言葉が混じる」・`site/developers.html` 本文の項・
  `docs/release-notes/v2.0.10.md`。
- 本体席へ＝通知のみ（`research/report/emoji-survey-2026-10-12.md` §7-5＝14 項）。

## 5. 試験

- `tests/contract/test_emoji.py`（761 件）＝元表 `emoji-cases.json`（652 行＝検分席が emoji-data.txt と手書きの文例から
  実装と独立に組んだ期待値から抜粋・キーキャップと ❗❓ は裁定で読み替え）＋45 種の素通し・未開示 11 の削除・
  亜種・孤立部品・列と畳み・4096 の切り・無音（6 形）・空白のみは 400・無音の前の 400・未読込・in-flight・
  記号だけ・塊の落とし・SSE・ログ・`/params`・regex。
- 既存 465 件は不変（合計 1,226）。

## 6. 受け入れ（実機）

`research/report/emoji-survey-2026-10-12.md` §13（ログで見る物 L-1〜L-6・本体側で増えてはならない物 B-1〜B-5・
現場の基準値＝3 符号点以下で ≥2.0 s＝28.3%・絵文字ありの 3 符号点以下＝76.9%・司令官の聴感・両機）。

## 7. 未了・別便の候補

- 本機の実機確認（ランチャの開発起動）・RTX 席・公開（Release・`site/app.json`）。
- **このアプリ側の「絵文字→読み」の対応表**（司令官の問い 2026-10-12）＝持たせるなら設定画面と保存先が増える＝別便。
- 絵文字と無関係に短文が長くなる型（話者ごとの基礎の長さ・issue #45）＝別件。
- `regex` の表が 18.0 になったら元表の差で気づく（`test_emoji.py`）。
