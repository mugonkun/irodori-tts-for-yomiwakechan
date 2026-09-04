# 調査便「irodori-tts の読み分けちゃん専用化（専用インストーラ／非 Python 化／専用制御方式）」依頼文 — 2026-09-04

> 起草＝副官兼実装席（Fable・yomiwakechan2 席）。宛先＝**別席（新セッション）**。本書は依頼文であり正典ではない。
> 実装は含まない＝成果は**裁定の材料**（案の比較表・見積もり・門の可否）。

## 0. 席の立て方（司令官が行う）

- **カレントディレクトリ**＝新規フォルダ `C:\Users\mugonkun\source\repos\irodori-native-research\`。
  中に上流を clone する（`Irodori-TTS`＝gradio 本体・`Irodori-TTS-Server`＝OpenAI 互換サーバ。Aratako 氏の GitHub。
  URL は席が HF モデルカード `Aratako/Irodori-TTS-v4-Small` から辿って確定し、報告に記す）。
  yomiwakechan2 のリポは**読むだけ**（絶対パスで参照・変更しない）。理由＝別コードベースの読解が主で、
  yomiwakechan2 の説明書の制度・釘は効かない。自動メモリはフォルダ単位なので、この席は yomiwakechan2 の記憶を持たない＝本書と §3 の資料から入る。
- **モデル**＝席は **Fable**（判断・比較表・門の評価）。**ultracode ON**＝読解・実験のサブ席は **opus を明示**（`model:'opus'`）。
  Fable 枠が惜しいときの退路＝席を Opus 5・ultracode OFF・読解は自分で順に（精度は落ちるが荷は成立する）。
- **本書の渡し方**＝新席の最初のメッセージに本書のパスを渡し「読んで着任せよ」。加えて §3 の資料パスを読ませる。

## 1. 目的

irodori-tts（Aratako/Irodori-TTS・MIT）を読み分けちゃん2 の 9 番目のエンジンとして**初心者が導入でき、感情を細かく制御でき、
複数 GPU の指定ができる**形にできるか、その道筋と工数を確定する。候補は三つ＝
⒜**専用インストーラ**（Python 同梱＝埋め込み配布・venv 固定・モデル自動取得）、⒝**非 Python 化**（推論を ONNX Runtime 等へ移し C# から直接呼ぶ）、
⒞**専用制御方式**（サーバは残しつつ、読み分けちゃんに都合のよい API＝感情パラメータ・GPU 指定・先読み・キャンセルを持つ薄い層を自前で建てる）。

## 2. 確定裁定（司令官・2026-09-04）

1. **調査専用**＝製品コードを書かない。yomiwakechan2 も上流 clone も変更しない（実験は別 venv・別フォルダ）。
2. **ライセンスの鎖が最初の門**＝コード（MIT）・モデル重み `Aratako/Irodori-TTS-v4-Small`（MIT 宣言）・
   コーデック `Aratako/Semantic-DACVAE-Japanese-32dim`（MIT 宣言）・その上流 `facebook/dacvae-watermarked`
   （**メタデータ apache-2.0 と本文 SAM License が矛盾＝前回調査で「卓の専管」と札**）。同梱・再配布の可否が×なら⒜⒝は成立しない＝ここで止まって報告してよい。
3. 現状の実機＝AMD Ryzen AI MAX+ 395／Radeon 8060S（iGPU・ROCm）。**NVIDIA 機での検証は無い**＝GPU 指定の調査は「デバイス選択がコードのどこで決まるか」を主とし、
   CUDA の実測は求めない。
4. 比較表の軸＝初心者の導入手順の長さ／感情パラメータの露出度／GPU 指定／**GPU 対応範囲（CUDA のみ vs CUDA＋ROCm）**／レイテンシ（RTF）／配布物の大きさ／ライセンス／保守（上流追随のしやすさ）／工数（便の数）。
5. 成果物＝`report/irodori-native-survey-2026-09-XX.md`（新フォルダ内）。閉幕時に yomiwakechan2 の `docs/` へ管制（yomiwakechan2 席）が写して記帳する。

## 3. 前提資料（yomiwakechan2 リポ・読むだけ）

- `probe/irodori-tts-probe-report.md`＝P-0〜P-10 実機プローブ（8088＝Server／7861＝gradio・wav 形式・レイテンシ・**Windows で ROCm が効かず CPU 合成だった経緯**）。
- `probe/irodori-tts-rocm-setup-guide.md`＝8088 を ROCm GPU 化した手順（RTF<1 に到達）。
- `probe/irodori-tts-licenses.md`＝ライセンス一次確認（上記の矛盾の原文）。
- `probe/irodori-tts-openapi.json`／`irodori-tts-params-raw.txt`／`irodori-tts-models.json`＝API 契約と露出パラメータの実測。
- `docs/irodori-tts-survey.md`・`docs/irodori-tts-requests.md`＝調査便と要求の記録。
- `docs/engine-adapters-overview-map.md` の irodori 節＝本体側アダプタの現況（HTTP・voice 必須・caption 経路・環境変数欄）。
- `decisions.md` の irodori 関連節（`docs/canon-map.md` で索引）。

## 4. 問い（この順で・各問いに「根拠の檔と行」を添える）

1. **ライセンスの鎖**＝§2-2 の 4 段それぞれの再配布可否と、矛盾の解き方（上流の実体ファイルの LICENSE・モデルカードの履歴）。同梱できるもの／利用者に取得させるものを分ける。
2. **推論の芯**＝モデルの構成（テキスト前処理・音素化・音響モデル・コーデック／ボコーダ）と各段のフレームワーク。**ONNX へ出せるか**を段ごとに（動的形状・カスタム演算子・
   コーデックの可逆性）。出せない段があれば⒝は「部分移植」になる＝その境界を示す。
3. **感情制御**＝HTTP API が露出していないパラメータを、推論関数の引数まで降りて列挙（caption／style／温度／速度／参照音声の重み等）。読み分けちゃんの「声の値」欄に写せる形かどうか。
4. **GPU 指定**＝デバイス選択がコード上どこで決まるか（環境変数・引数・固定）。複数 GPU の index 指定・CPU 強制・ROCm/CUDA の分岐。Server 起動引数で足りるなら⒞の材料。
5. **導入の重さ**＝現状の手順で初心者が詰まる箇所（uv・torch の index・ROCm）を列挙し、⒜の埋め込み配布（python-embed＋wheel 同梱＋モデルの初回取得）で消えるものと残るものを分ける。
   配布物の大きさの見積もり（モデル 2.86GB 込み）。
6. **三案の比較表と推奨**＝§2-4 の軸で。推奨は 1 案＋退路 1 案。工数は「便」の数で（1 便＝実装＋敵対検分＋書き戻し）。
7. **GPU 対応範囲のコスト比較（司令官追加 2026-09-04）**＝**CUDA のみに割り切る場合**と**ROCm も対応する場合**を、三案それぞれについて比べる。
   軸＝配布物（torch/ONNX Runtime の実行プロバイダごとの wheel／DLL の大きさと数）・導入手順の分岐（利用者にどちらか選ばせる負担）・
   検証機の要否（NVIDIA 機は手元に無い＝CUDA 側は誰がどう検証するか）・Windows での ROCm の成熟度（前回は CPU フォールバックを踏んだ）・
   上流追随の保守（torch の版が上がるたびの再検証）・利用者層の分布（配信者の GPU は NVIDIA が多数と見込む＝根拠があれば添える）。
   結論は「CUDA のみで出し、ROCm は司令官機だけの手動導入手順として残す」が成立するかどうかまで踏み込む。

## 5. 停止域

- 上流 clone・`C:\IrodoriTTS\`（稼働中の 7861／8088 の venv と設定）を**書き換えない**。実験は `irodori-native-research\lab\` の別 venv で。
- GPU 実験は配信中に行わない（司令官に配信の有無を確認）。8088 を止めない。
- ライセンスの結論を推測で断定しない＝原文の檔と行を引く。矛盾は矛盾のまま報告（解釈は卓）。
- yomiwakechan2 リポの檔を 1 行も変えない（読むだけ）。報告は新フォルダの `report/` に書く。
- 外部へ何も公開しない（HF・GitHub への push なし）。

（本依頼文は全 70 行）
