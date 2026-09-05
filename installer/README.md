# installer/ — インストーラ（**便 E**）

> **設計は済んだ**（2026-09-05・`docs/design/ben-e-installer.md`）。このディレクトリにはまだ実装が無い。
> 正典＝`decisions.md`。導入の確定手順＝`docs/install.md`。受け入れ条件＝`docs/acceptance.md` の
> 「導入」「サイズ」「ドライバ」行と §3。

## 1. 何を作るか

**per-user のインストーラを、版ごとに 2 本**（管理者権限を求めない）。

- **`irodori-tts-ywk-setup-v<版>-cuda.exe`**（cu130／cu126／CPU）と
  **`irodori-tts-ywk-setup-v<版>-radeon.exe`**（rocm-gfx1151／CPU）＝`decisions.md` 5「Radeon 版は別リリース」。
  `AppId`・導入先・同梱台帳を分ける。同じ機体に同居できる（取得物の置き場は共有）。
  ※ 便 A の本 README は「per-user のインストーラ **1 本**」と書いていた。これは「per-user か per-machine か」の
  意味と読み、**「版ごとに 1 本」に読み替えた**（便 E 設計 §1-4・**卓の裁定待ち**＝同 §9 Q-E1）。
- 置き場＝`%LOCALAPPDATA%\Programs\irodori-tts-ywk\`（Radeon 版は `…-radeon\`）。
- 中身＝ランチャ exe（便 D）・`server\`（wrapper ＋ **パッチ適用済みの上流の写し**＝MIT・数 MB の
  テキスト）・`ledger\`（**版ごとに間引く**）・`licenses\`・`voices\`（プリセット話者・便 P）・`docs\`（**明示列挙**）。
- **第三者バイナリ（wheel・exe・dll・モデル）は 1 つも入れない**（`decisions.md` 8）。
  実行系もモデルも `vc_redist.x64.exe` も**初回起動時にランチャが取得する**。
  ＝Release 資産は **約 81 MiB** に収まり、GitHub Releases の 1 資産 2 GiB 上限に触れない。
  ※ 唯一の例外は .NET ランタイム（ランチャ exe に焼き込み＝`decisions.md` 51）。
- 道具＝**Inno Setup 6.7.3**（この機体にある＝`%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`・
  `decisions.md` 25。版の逐語＝実射ログの `Setup version: Inno Setup version 6.7.3`）。
  `.iss` は CRLF（`.gitattributes` で強制済み）・UTF-8 **BOM 付き**。

## 2. 決まっていること

| # | 事項 | 出典 |
|---|---|---|
| 1 | 導入先は per-user（`%LOCALAPPDATA%\Programs\`）・取得物は `%LOCALAPPDATA%\irodori-tts-ywk\` | 設計書 §2 |
| 2 | オフライン導入は用意しない | `decisions.md` 8 |
| 3 | 利用者操作 ≤ 6（vc_redist を黙って通せれば ≤ 5）・オンライン導入 ≤ 10 分（100 Mbps 級） | `docs/acceptance.md` |
| 4 | 初回取得の前に**ライセンス通知**（`licenses/first-run-notices.md`）を出して同意を取る | 同・`licenses/README.md` |
| 5 | アンインストールで **`voices\`（利用者の話者）は既定で残す** | `docs/install.md` §5 |
| 6 | **bat を使わない**（CRLF／UTF-8 の文字コード事故＝`research/local-additions/` の反面教師） | 設計書 §2 |

## 3. 決めたこと（**便 E 設計**・2026-09-05）

全文＝`docs/design/ben-e-installer.md`。便 A が「決まっていない」と挙げた 5 件の帰結：

| 便 A の宿題 | 決めたこと | どこ |
|---|---|---|
| ⑴ 画面・文言・同意の取り方 | **インストーラは同意を取らない**（`LicenseFile` を置かない）。通知はランチャの初回ウィザード 1 段目が `first-run-notices.md` を**そのまま**出す（`decisions.md` 46）。インストーラで置くと同意の二重取りになり操作数を 1 食う。頁は **3 枚**（場所・準備完了・完了）＝`[Tasks]` を持たない。**歓迎頁は Inno の既定で出ない**（`DisableWelcomePage` の既定は yes）ので、数字の告知は `ReadyLabel2a／2b` と `FinishedLabel` に置く | 設計書 §1-6・§2・§4 |
| ⑵ 取得の中断・再開 | **インストーラの範囲外**＝ランチャの管掌（`cache\` に検証済みの原檔が残り、再開は 1 バイトも落とし直さない）。インストーラは取得を 1 バイトもしない | 設計書 §0・§5-2 |
| ⑶ ドライバ検査の告知と止め方 | **インストーラは検査しない。**`torch.cuda.is_available()` は実行系を取った後にしか撃てない（導入時点で python は 1 檔も無い）＝ランチャの門（`decisions.md` 80・88 ⑴⑵）。インストーラが語る数字は準備完了頁と完了頁の 2 箇所だけ | 設計書 §6 |
| ⑷ 更新配布の形（実行系とモデルを分離） | **実装するものが無い＝この宿題は消える。**アプリ樹（`{app}`）はインストーラの専管、データ樹（`%LOCALAPPDATA%\irodori-tts-ywk\`）はランチャの専管で、**インストーラはデータ樹に 1 檔も作らず更新でも触らない**（`AppPaths.cs:216` の逐語「導入先には 1 檔も作らない」）。差分パッケージも作らない（81 MiB の全量置換で足りる） | 設計書 §1-1・§5-1 |
| ⑸ vc_redist を黙って通す経路 | **インストーラでは通さない。**通すには管理者昇格が要り、`PrivilegesRequired=lowest` が崩れる。ランチャが取得の段の先頭で通す（引数は台帳 `ledger/vc_redist.json` の `silent_args`＝`/install /quiet /norestart`＝`decisions.md` 87 ⑷） | 設計書 §6-1 |

**あわせて決めた 5 つ**

1. **`[InstallDelete]` を 4 行置く**（`{app}\server`・`{app}\ledger`・`{app}\licenses`・`{app}\voices\presets`）。
   Inno は「新版で消えた檔」を消さないので、⒜ 上流の写しから減った `.py` が `._pth` 経由で import され続ける、
   ⒝ 種を跨いだ上書きで `ledger\` が混ざり **cu130／cu126 が UI から消える**（`ReleaseFlavor.Detect` は
   `runtime-rocm-*` を 1 件見た瞬間 Radeon 版と判定する）。
2. **`docs\` は明示列挙**（`README.md`・`docs/install.md`＋Radeon 版のみ `docs/radeon.md`）。
   一括写しにすると `acceptance.md`・`contract.md` という内部檔が利用者機へ出る。
3. **アンインストールの問いは 2 段**（1 段目＝取得物・2 段目＝話者と設定）・**どちらも既定は「残す」**・
   無人では黙って残る（実射で確認）。**もう一方の種がまだ入っていれば 1 段目を出さない**（取得物は共有）。
4. **環境変数を 1 本も立てない**（`YWK_LAUNCHER_*` を `setx` しない）＝1 本でも立つとランチャが「開発起動」を名乗る。
5. **生産ラインは `build/installer-build.ps1`**（新設・ASCII・CRLF・PS 5.1／7）。
   `assemble-app.ps1` → `release-build.ps1 -SkipZip` → 門 A（**7 本**）→ ISCC → 門 B（3 本）→ sha256。
   **A-7 は是正席が足した 1 本**＝「名指しの檔（`server\ywk_server.py`・`ywk_params.py`・`ywk_fetch_models.py`・
   `python312._pth.template`・`voices\presets.json`・`licenses\first-run-notices.md`）が配布樹に在って空でない」。
   A-1 の檔数不一致も **WARN から失敗に**した（総バイトの drift は WARN のまま）。
   理由＝これが無いと **9 本すべて通って exit 0・sha256 まで記帳された、動かない中身の setup** が出せた
   （2026-09-05 の実射＝wrapper 2 檔を退けても `DONE: 1 installer(s), 9 gate(s), 0 failed.`）。
   **終了コードは「落ちた門の本数」**で、道具や入力が無くて走が死んだときだけ **64**（門の数と衝突しない番号）。
   **版は `launcher/Directory.Build.props` の `<AppDisplayVersion>` 1 箇所から読み `/D` で渡す**（`.iss` に書き写さない）。
   混入検分は**拡張子の白名簿＋1 檔 4 MiB の上限**で撃つ（`release-build.ps1` の `$forbidden` は `*.wav` と
   `voices.json` を禁じるので**流用できない**＝配布樹には自作 wav 11 檔が正当に入る＝`decisions.md` 37）。

**卓へ回した 3 件**（`docs/design/ben-e-installer.md` §9）＝Q-E1 版を 2 本にする件と `AppId` の GUID 記帳／
Q-E2 「導入後 ≤ 7.0 GB」の読み方（落とすバイトか展開後か）と `cache\` を誰が消すか／
Q-E3 この機体（09-07 まで）で撃つ不可逆 2 件と、要る管理者昇格 2 回。

## 4. 検分項目（便 E が実機で潰す）

台本＝`probe/e-install-probe.ps1`（新設・9 段・`probe/common.ps1` を dot-source。
**Inno 用の 4 関数だけ台本の中に足す**＝起動した setup の pid には窓が無く子が持つ・
`probe/common.ps1` の `Find-ById`／`Invoke-ButtonById` は Inno の `TNewButton` に効かない・押す道は `BM_CLICK`）。

| # | 何 | 状態 | 計画（機体・日） |
|---|---|---|---|
| E-1 | **vc_redist 欠落機で理由が読める形で止まる**（DLL 名を出す・黙って `ImportError` にしない） | **未確認**（`research/lab/notes/30` V-5） | 段 8。**Windows Sandbox**（この機体に実体が無く、機能の有効化に**管理者 1 回＋再起動**）＝**Q-E3 の裁定待ち**。下りなければ月曜にまっさらな RTX 機で `msvcp140.dll` の在否を見て代用 |
| E-2 | 日本語・空白を含むユーザ名（`%LOCALAPPDATA%` にそれが入る）で全経路が通る | 未確認 | 段 9。Radeon 機・**09-06**（新規ローカルユーザ「テスト 太郎」の作成に**管理者 1 回**＝Q-E3） |
| E-3 | 導入先が読取専用のとき（`IRODORI_VOICES_DIR` を絶対パスで渡す＝`docs/install.md` §3-1 の条件 2） | 設計は済・実射は未 | 段 4。Radeon 機・**09-06**（`icacls /deny (OI)(CI)W` を掛けて 1 周） |
| E-4 | アンインストール後に取得物が意図どおり残る／消える | 未 | 段 7・8。Radeon 機・**09-05（今日）**。rocm の実行系 4.32 GiB が既に在る＝実弾で消して戻せる |
| E-5（新） | **利用者操作 ≤ 6**（UI で 3 頁を通し、無人導入と同じ結果になる） | 未 | 段 2。Radeon 機・**09-05** |
| E-6（新） | 走行中のランチャを `AppMutex` が弾くか（`Local\` は**セッション名前空間**＝別セッションを捕まえるかは**推測**） | 未 | 段 5。Radeon 機・**09-05**。結果を設計書に記帳する |
| E-7（新） | 種を跨いだ上書きで `ledger\` の混入が消えるか（`[InstallDelete]` の実射） | 未 | 段 6。Radeon 機・**09-05** |

**この機体（Radeon・`decisions.md` 19）は 2026-09-07（月）に失う。**
月曜以降に撃ち直せない不可逆は 2 件だけ＝⒜ **Radeon 版 setup の実射** ⒝ **本物の NVIDIA 無し機での W-1**
（`docs/acceptance.md` §3 の 6）。RTX 機には **Inno Setup 6.7.3 を公式インストーラで導入する**
（`Inno Setup 6\` 一式を N: で写す案は採らない＝`decisions.md` 22「ライセンスの結論を推測で断定しない」）。

## 5. 停止域

- `C:/IrodoriTTS/`・`C:/irodori-TTS-server/`・`yomiwakechan2` に書かない。**System32 に触らない。**
- `upstream/` は読むだけ（`__pycache__` も作らない＝Python 実行時は必ず
  `PYTHONDONTWRITEBYTECODE=1`）。
- 第三者バイナリを `build/out/` と `.venv-dev/` 以外のリポ内に置かない。
- 外部（GitHub 公開・HF・PyPI）へ push しない。Release は司令官の裁定まで作らない。
- **便 D（2）が在飛行中は `launcher/`・`server/`・`build/` と `probe/` の既存檔・`docs/acceptance.md`・
  `docs/design/ben-d-launcher.md` を読むだけにする。**`installer/*.iss` は停止域の外＝先に書いてよい。
