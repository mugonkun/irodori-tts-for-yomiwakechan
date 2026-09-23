# app-update.md — アプリ内更新（裁定 160・2026-09-24）

> 司令官の指示（2026-09-24）＝「上限RAMの設定と、**本体を参考に更新シーケンスを実装**」の後半。
> **参考型＝本体 yomiwakechan2 の更新シーケンス**（`docs/app-update-requests.md` §3 A-1〜A-11・
> 同リポ `decisions.md` の「本体更新シーケンス」節）。本書はその型を**この配布物に合わせて写した**物で、
> 差分（2 つの版・着地頁に置く manifest）だけが新しい。**本体は 1 字も触っていない**（不触の掟）。

---

## 1. 何を作ったか（1 枚の絵）

〔このアプリについて〕の**版の行の直下**に釦が 1 つと、結末の 1 行。

```
  バージョン
  v2.0.7 － RTX（CUDA）
  [ 新しい版を確認 ]                     ← 一押し目＝照合だけ（1 バイトも落とさない）
  この釦を押したときだけ、…             ← 薄字の註（自動では見にいかない）

  ↓ 新しい版があった

  [ v2.0.8 に更新する ]                  ← 同じ釦の札が変わる＝**二段確認**
  新しい版 v2.0.8 があります。もう一度押すと、…
```

二押し目＝**配布情報の再取得 → 再照合 → インストーラ取得 → sha256 検分 → 置く → アプリを閉じる（子を畳み錠を返す）→ 起こす →
このアプリは既存の終了の入口 1 本を通って畳む**。
インストールが終われば、ウィザードの完了画面のチェック（`[Run]` の postinstall）でもう一度起きる。

**失敗はすべて 1 行の状態文**である＝窓（MessageBox）も例外も利用者に出さない。

---

## 2. 配布 manifest（`app.json`・schema v=1）

- **出先**＝`https://mugonkun.github.io/irodori-tts-for-yomiwakechan/app.json`（着地頁の根）。
  アプリが持つ定数は**この 1 本だけ**（同梱の写しは作らない＝取れなければ「確認できなかった」の 1 行で足りる）。
- **正本**＝リポの `site/app.json`。`site/` は版を切るたびに `gh-pages` の根へ写す（`site/README.md`）。
- **本体との差**＝この配布物は **RTX（CUDA）と Radeon（ROCm）の 2 本**なので、
  インストーラの欄を `installers.<cuda|radeon>` に割った（本体は 1 本なので `installerUrl` が直下に在る）。
  鍵の綴りは `AppPaths.FlavorId`（＝`.iss` の `/DFlavor=`・setup の檔名）と**同じ 1 箇所から**来る。

```json
{
  "v": 1,
  "name": "irodori-tts-ywk",
  "version": "v2.0.7",
  "builtAt": "2026-09-24T00:00:00Z",
  "pageUrl": "https://mugonkun.github.io/irodori-tts-for-yomiwakechan/",
  "note": null,
  "installers": {
    "cuda":   { "url": "https://github.com/…/download/v2.0.7/irodori-tts-ywk-setup-v2.0.7-cuda.exe",
                "sha256": "<64 桁の小文字 hex>", "sizeBytes": 0 },
    "radeon": { "url": "https://github.com/…/download/v2.0.7/irodori-tts-ywk-setup-v2.0.7-radeon.exe",
                "sha256": "<64 桁の小文字 hex>", "sizeBytes": 0 }
  }
}
```

| 欄 | 要否 | 読み方 |
|---|---|---|
| `v` | 必須（欠落＝1 扱い） | **1 以外は全体を不成立**（前方互換の作法＝知らない schema を推測で読まない） |
| `name` | 必須 | `irodori-tts-ywk` 固定。違えば適用しない（取り違えの検分） |
| `version` | 必須 | **v 前置の表示形**＝`AppVersion.Display` と**文字列でそのまま**突き合わせる |
| `installers.<版>.url` | 必須 | **https 限定**。それ以外は取りにいかない |
| `installers.<版>.sha256` | 必須 | 64 桁の hex であることだけを解釈時に見る（値の一致は取得後に見る） |
| `builtAt`／`pageUrl`／`note`／`sizeBytes` | 任意 | **表示のみ**＝値で枝を分けない |
| 未知の欄 | — | **無視**する |

**版の照合は Ordinal の不一致＝「新しい版がある」**（順序比較をしない・降格の判断も持たない＝本体 A-3 と同型）。
どちらが新しいかは配布側が決める＝`app.json` が正である。

**置き札について**＝リポの `site/app.json` は、まだ公開していない版の sha256 に **0 を 64 個**書いてある。
解釈はこれを普通の値として通す（字数と hex しか見ない）＝実物と合わなければ取得後の検分で落ちる。
本物の値は公開の工程（§5）が実物から計って書く。

---

## 3. 順序（シーケンス）と上限

| 段 | すること | 上限・断り |
|---|---|---|
| ① | `app.json` を取る | 1 MB・15 秒 |
| ② | 版を照合 | 一致＝最新（**インストーラを取りにいかない**） |
| ③ | 〈二押し目〉再取得して再照合 | 承諾した版と食い違えば**適用せず**「新しい版がある」へ倒す |
| ④ | インストーラを取る | **200 MB・900 秒**（申告の Content-Length でも、申告しない相手の実バイトでも切る） |
| ⑤ | sha256 の検分 | 不一致＝**置かずに消して起こさない**（嘘の成功を返さない） |
| ⑥ | 置く | `<データの家>\updates\`（**取りにいく前に同じ枝を掃除**＝版違いを溜めない） |
| ⑦ | 起こす | `Process.Start(UseShellExecute=true)`＝per-user なので昇格しない |
| ⑧ | 畳む | **既存の終了の入口 1 本**＝主窓の Closing → `App.OnExit` → 子をツリー kill |

- **契機は手動の釦だけ**＝起動時も定期も確認しない（この製品は「はじめの準備のときだけ外に出る」と
  約束している＝着地頁の文と揃える）。
- **走っている間は釦が押せない**（`AsyncRelayCommand`）。**起こしたあとは起き直さない**＝
  終了は非同期に進むので、その窓で二重に起こす芽を残さない。
- **読み上げ中は二押し目を断る**（1 行＝「読み上げ中は更新できません。終わってからもう一度押してください。」）。
  見るのは `/ywk/status` の `requests.in_flight` の標本（`StatusViewModel.HostBusy`）＝
  2 秒ごとの見張りが既に持っている値なので、更新のために 1 度も叩き足さない。
  **一押し目（照合だけ）は断らない**＝読み上げ中でも「新しい版が在るか」は見てよい。
- 取得・検分・起動の失敗では**控え（承諾した版）を保つ**＝もう一押しで取り直せる。

---

## 4. 置き場（どの檔が何を持つか）

| 檔 | 持ち物 |
|---|---|
| `launcher/…/Services/Update/AppDistributionContract.cs` | `app.json` の**解釈**（純関数）。schema・必須欄・版の鍵・sha256 の字の検分 |
| `launcher/…/Services/Update/AppUpdateService.cs` | 取得・照合・検分・保存・起動。継ぎ目＝`HttpMessageHandler` と起動の `Func<string,bool>` |
| `launcher/…/ViewModels/AboutViewModel.cs` | 二段確認の状態機械・結末の 1 行への写像（`DescribeUpdateResult` は純関数） |
| `launcher/…/ViewModels/UiStrings.cs` | **文言の唯一の出所**（`AboutUpdate*`）。VM に直書きしない |
| `launcher/…/Views/AboutView.xaml` | 釦・1 行・薄字の註（id＝`AboutUpdateButton`／`AboutUpdateStatusText`／`AboutUpdateNoteText`） |
| `launcher/…/Views/MainWindow.xaml.cs` | **配線はここ 1 箇所**＝版・置き場・終わらせる手・読み上げ中の判定を差す |
| `launcher/…/Contracts/AppPaths.cs` | `UpdatesDir`（`<データの家>\updates`・**`EnsureDataDirectories` では作らない**） |
| `installer/irodori-tts-ywk.iss` | `RestartApplications=no`（既に在った）＋`[Run]` の postinstall＝**再起動の導線** |
| `build/make-app-manifest.ps1` | `site/app.json` を実物から書く（§5） |
| `site/app.json` | 配布 manifest の正本 |

**画面は `Process` も `Application` も知らない**＝終わらせる手は主窓が `Action` で差す
（`AboutViewModel.AttachUpdater`）。差していなければ釦は最初から押せない。

---

## 5. 公開の工程（**資産が先・manifest が後**）

`site/README.md` §公開の順序が正本。要約＝

1. `build/installer-build.ps1 -All` → `build/out/installer/` に 2 本の setup と `SHA256SUMS.txt`。
2. **GitHub Release の資産を先に上げる**（タグ `v<版>`）。
3. `pwsh -NoProfile -ExecutionPolicy Bypass -File build/make-app-manifest.ps1`
   ＝版は `launcher/Directory.Build.props` の `AppDisplayVersion` から、sha256 と長さは**実物から**。
   `SHA256SUMS.txt` と食い違えば**止まる**（嘘の manifest を作らない）。`site/app.json` を UTF-8・LF で書く。
4. `site/` を `gh-pages` の根へ写して push＝**この push がアプリ内更新に降ろすスイッチ**。

逆順にしない＝manifest が先に降りると、アプリが「新しい版がある」と言ってから 404 を引く。
途中で止まっても「資産は在るが `app.json` は旧版のまま」にしかならず、再実行で治る。

---

## 6. 検分（`launcher/IrodoriTtsYwk.Launcher.Tests/AppUpdateTests.cs`・26 本）

- **解釈**＝正しい 1 枚が両方の版を返す／`v=2` は不成立／自分の版の欄が無ければ不成立／
  sha256 が 64 桁の hex でなければ不成立／未知の欄は無視。
- **照合**＝一致なら最新で**インストーラを取りにいかない**（叩いた URL が manifest の 1 本だけであること）／
  不一致なら「新しい版がある」と配布元の 1 行を運ぶ／別のアプリの名は検分に落とす。
- **取得と検分**＝通れば掃除してから置いて起こす／sha256 不一致は**置かずに消して起こさない**／
  上限超え（申告あり・申告なしの 2 通り）／見切り／HTTP の失敗／承諾した版との食い違い／
  https でない取得先／起こせなかった回は保存先を添える。
- **画面**＝未配線なら押せない・1 行も出ない／一押し目で札が「vX に更新する」へ変わる／
  二押し目で終了の手が 1 度だけ走り、以後は押せない／最新なら控えを持たない／
  走っている間は押せない／読み上げ中は断って控えを保つ／失敗しても控えは残る／
  結末の 1 行は全部 `UiStrings` から出る。
- **同梱の `site/app.json`** が両方の版で解釈に通ること（置き札のままでも通る）。
- `AutomationIdsTests.裁定160で新設したidが揃っている`＝3 つの id が画面に在る。

HTTP はフェイクの `HttpMessageHandler`・起動はテストが差す継ぎ目＝**実プロセスは 1 つも起こさず、
外へは 1 バイトも出ない**。

---

## 7. 決めなかったこと（次の回の材料）

- **署名**は無いまま（裁定 146）＝インストーラを起こすと SmartScreen が出る回がある。
  アプリ内更新はそこを変えない（利用者の面前で起きるので、出た窓は利用者が読める）。
- **prerelease 運用**（本体の項 10）はまだ持ち込んでいない＝この製品の `app.json` を書けば
  すぐ全利用者に降りる。段階的に降ろしたくなった回に、`app.json` を後から写す手を挟めばよい。
- 取得の**再開**（Range）は持たない＝200 MB 未満の 1 檔を 900 秒で取り切る前提。
  途中で切れたら `.part` を捨ててもう一押し。
