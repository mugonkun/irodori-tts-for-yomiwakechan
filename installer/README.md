# installer/ — インストーラ（**便 E**・便 A では本 README のみ）

> **便 A の時点で、このディレクトリには実装が無い。**便 E が入る前の申し送りだけを置く。
> 正典＝`decisions.md`。導入の案＝`docs/install.md`。受け入れ条件＝`docs/acceptance.md` の
> 「導入」「サイズ」「ドライバ」行。

## 1. 何を作るか

**per-user のインストーラ 1 本**（管理者権限を求めない）。

- 置き場＝`%LOCALAPPDATA%\Programs\irodori-tts-ywk\`。
- 中身＝ランチャ exe（便 D）・`server\`（wrapper ＋ **パッチ適用済みの上流の写し**＝MIT・数 MB の
  テキスト）・`ledger\`・`licenses\`・`voices\`（プリセット話者・便 P）・docs。
- **第三者バイナリ（wheel・exe・dll・モデル）は 1 つも入れない**（`decisions.md` 8）。
  実行系もモデルも `vc_redist.x64.exe` も**初回起動時にランチャが取得する**。
  ＝Release 資産は数十 MB に収まり、GitHub Releases の 1 資産 2 GiB 上限に触れない。
- 道具＝**Inno Setup 6**（この機体にある＝`%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`・
  `decisions.md` 25）。`.iss` は CRLF（`.gitattributes` で強制済み）。

## 2. 決まっていること

| # | 事項 | 出典 |
|---|---|---|
| 1 | 導入先は per-user（`%LOCALAPPDATA%\Programs\`）・取得物は `%LOCALAPPDATA%\irodori-tts-ywk\` | 設計書 §2 |
| 2 | オフライン導入は用意しない | `decisions.md` 8 |
| 3 | 利用者操作 ≤ 6（vc_redist を黙って通せれば ≤ 5）・オンライン導入 ≤ 10 分（100 Mbps 級） | `docs/acceptance.md` |
| 4 | 初回取得の前に**ライセンス通知**（`licenses/first-run-notices.md`）を出して同意を取る | 同・`licenses/README.md` |
| 5 | アンインストールで **`voices\`（利用者の話者）は既定で残す** | `docs/install.md` §5 |
| 6 | **bat を使わない**（CRLF／UTF-8 の文字コード事故＝`research/local-additions/` の反面教師） | 設計書 §2 |

## 3. 決まっていないこと（便 E で決める）

1. 画面・文言・同意の取り方（`first-run-notices.md` をそのまま出すか要約するか）。
2. 取得の中断・再開（5.4 GiB の途中で切れたとき）。
3. ドライバ検査（cu130 ≥ 580／cu126 ≥ 560.76）の告知文言と、不足時に合成を撃たせない止め方。
4. 更新配布の形（**実行系とモデルを分離**が推奨＝モデルは版が変わらない限り再取得不要）。
5. vc_redist を「黙って通す」経路の実装（`/quiet /norestart` の扱い・既に入っている機体の判定）。

## 4. 検分項目（便 E が実機で潰す）

| # | 何 | 状態 |
|---|---|---|
| E-1 | **vc_redist 欠落機で理由が読める形で止まる**（DLL 名を出す・黙って `ImportError` にしない） | **未確認**（`research/lab/notes/30` V-5） |
| E-2 | 日本語・空白を含むユーザ名（`%LOCALAPPDATA%` にそれが入る）で全経路が通る | 未確認 |
| E-3 | 導入先が読取専用のとき（`IRODORI_VOICES_DIR` を絶対パスで渡す＝`docs/install.md` §3-1 の条件 2） | 設計は済・実射は未 |
| E-4 | アンインストール後に取得物（≈6.9 GB）が意図どおり残る／消える | 未 |

## 5. 停止域

- `C:/IrodoriTTS/`・`C:/irodori-TTS-server/`・`yomiwakechan2` に書かない。
- `upstream/` は読むだけ（`__pycache__` も作らない＝Python 実行時は必ず
  `PYTHONDONTWRITEBYTECODE=1`）。
- 第三者バイナリを `build/out/` と `.venv-dev/` 以外のリポ内に置かない。
- 外部（GitHub 公開・HF・PyPI）へ push しない。Release は司令官の裁定まで作らない。
