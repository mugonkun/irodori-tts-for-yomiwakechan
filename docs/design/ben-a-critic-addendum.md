# 便 A 追補 — 着任読解の批評席の指摘を是正段に渡す（2026-09-04・設計席）

> 批評席（opus・xhigh）が着任メモと便 A 入力を原文と突合した結果のうち、**便 A の成果物に効くもの**を設計席が仕分けした。
> 是正席は本檔の各項を「直した／該当なし（理由）」で報告する。出典は批評席の逐語（`research/` の檔:行）。

## A. 設計書に反映済み（実装がこれに従っているか検分する）

| # | 指摘 | 設計書の対応 | 検分 |
|---|---|---|---|
| A1 | `/params` の「`default` が null の欄 0 件」は 44 欄では構造的に満たせない（dataclass 既定が None の欄が 19） | `exposed_to_ywk:true` の集合にだけ課す。None 既定の欄は `default:null`＋`nullable:true`。文字列欄は `""`＝未指定 | `ywk_params.py` と `tests/` が新規則に沿うか |
| A2 | `num_steps` の範囲「gradio 準拠 4〜64」は誤り（gradio は 1〜120・step 1） | 1〜120・`range_source:"gradio"`・presets [10,40] | `/params` の実値 |
| A3 | gradio のスライダは 8 本でなく 7 本（`cfg_scale_caption` は voicedesign 側のみ）。`num_candidates` の max は `MAX_GRADIO_CANDIDATES=32` | `range_source` を欄ごとに持ち、gradio 由来は 7 欄のみ。8 本目以降は `"ywk"` と名乗る | `range_source` の値 |
| A4 | `speed` と `duration_scale` は上流が `duration_scale / speed` で除算合成（app.py の該当行） | `duration_scale` は `exposed_to_ywk:false`・note に出典 | `/params` の note |
| A5 | 本体の現行アダプタは `voice:"none"` を送る＝`allow_no_ref_voice=false` では 400＋絶対パス漏れ | wrapper が NO_REF_IDS を「デフォルト」に正規化 | テスト 1 本追加 |
| A6 | `/params` の閾値が 100 ms と 200 ms の 2 本立て | 100 ms を採る | `verify-runtime.ps1` の計測 |
| A7 | 出力形式＝ffmpeg に落ちうるのは aac だけでなく mp3/opus も | wav 固定の理由に書き換え | `docs/contract.md` の文言 |
| A8 | dacvae のライセンス＝裁定 29（Apache-2.0 と読む） | `licenses/dacvae/` の README に「当方の読み」を明記 | `licenses/README.md` |

## B. 台帳・ビルドの検分項目（S1 の成果を疑う）

| # | 指摘 | 是正席がすること |
|---|---|---|
| B1 | `model.safetensors` 3,064,295,596 B は **v4-Small** の実測値の可能性（v4.1-Small は du 2,929 MB の粗い値のみ） | `ledger/models.json` の size と sha256 が HF API（`/api/models/Aratako/Irodori-TTS-v4.1-Small/tree/<rev>`）の実値であることを再取得して突合。設計書 §3 の数字を写していたら直す |
| B2 | codec（`Aratako/Semantic-DACVAE-Japanese-32dim`）と `sony/silentcipher` の revision が未取得だった | `models.json` に 3 リポとも revision（commit sha）と檔ごとの sha256 があるか |
| B3 | torchaudio 2.10.0（cu130 2,008,471 B／cu126 1,800,247 B）は torchcodec 丸投げの薄い wheel＝パッチの必要性の物証 | 台帳の torchaudio の size がこの桁であること・`patches/README.md` にこの事実を 1 行 |
| B4 | dacvae/silentcipher は手元ビルド wheel では sha256 が再現しない | 台帳は GitHub の commit 固定 zip（`archive` kind）であること。手元 wheel の sha256 を写していないこと |
| B5 | torchao は非量子化 checkpoint では不要（`quantization.py` の import は関数内） | `ledger/README.md` の「外した理由」に torchao の行があるか（無ければ足す） |
| B6 | `._pth` に `server/` の行が要る・起動形は `python -m ywk_server` の 1 本 | テンプレと `assemble-runtime.ps1` の生成結果 |
| B7 | patches の生成手順が停止域と噛む（submodule を編集→復元は禁止） | S2 が写しから diff を取っていること・`git -C upstream/* status --short --ignored` が空・`__pycache__` 0 件 |

## C. ライセンスの検分項目（裁定 8・28・29 の下で）

| # | 指摘 | 是正席がすること |
|---|---|---|
| C1 | torch 2.10.0+cu130 の `License-File:` 一覧は未取得（手元は 2.14.0 のもの） | 配布物に torch は入らない（裁定 8）ので `licenses/` には置かない。`first-run-notices.md` は「利用者機に入る torch wheel の同梱通知（NVIDIA CUDA EULA・cuDNN SLA・third_party/）は wheel 内 `*.dist-info/licenses/` にある」と所在で書き、版固有の檔名を断定しない |
| C2 | `nvperf_host.dll`・`zlibwapi.dll`・`nvrtc64_130_0.alt.dll` の EULA 上の扱いが未確認 | 同上＝配布しないので義務は立たない。`licenses/README.md` に「配布物に含めない第三者物」の節を置き、未確認の事実を推測なしで記す |
| C3 | 「外部取得 4・自作 2」「5＋2」「6＋3」の員数が食い違う | `licenses/README.md` の索引を「原文コピー／自作／配布物に含めない（通知のみ）」の 3 区分で員数を確定し、`check-licenses.ps1` はその索引と実檔を突合する |
| C4 | libsndfile＝裁定 28（通知のみ） | `first-run-notices.md` に LGPL-2.1 の通知とソースの所在 URL（libsndfile 公式）があるか。SOURCE-OFFER は作らない |

## D. 契約文書の検分項目

| # | 指摘 | 是正席がすること |
|---|---|---|
| D1 | 露出欄の集合（β）と日本語表示名の書き手（ε）が契約に無かった | `docs/contract.md` §5 に `exposed_to_ywk` の集合と「表示名・説明は配布版が書く（`/params` の label/description）」を明記 |
| D2 | `IrodoriConstants.cs` の行番号（BaseUrl は :35 のみ・:36 は空行／CPU×bf16 の注記は :178-181）・パスは `yomiwakechan2/yomiwakechan2/Engines/Irodori/...` | D-9・P-3 の出典行を訂正 |
| D3 | app.py の route 行番号は def 行でなくデコレータ行（165/203/218/237/259/272/299/314） | 契約文書が上流の行番号を引くなら統一する（引かないのが最善） |

## E. 是正しない（理由）

- 着任メモ内の行番号ずれ（`30-…:602,611` 等）＝実装は原文を開いて書いているので成果物には影響しない。
- 「不要 500 MiB を外して起動する確認は未実施」＝kit-cuda は最初から最小集合で走っており実質証明済み。便 A では A-4（実合成）で改めて通す。
