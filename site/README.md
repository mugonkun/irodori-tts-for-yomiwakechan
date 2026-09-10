# site/ — 公式ページ（landing page）

利用者に「落として入れる」だけをさせるための 1 枚の頁です。開発者向けの言葉は置きません。
出先＝<https://mugonkun.github.io/irodori-tts-for-yomiwakechan/>（`gh-pages` 枝の根）。

## 中身

| 檔 | 何 |
|---|---|
| `index.html` | 頁の全部（CSS も JS も中に書いてある・**外部資産ゼロ**＝画像・font・CDN を 1 つも読まない） |
| `README.md` | この檔（公開の仕方の記帳・**頁には出ない**） |
| `img/`（未作成） | 使い方の 3 枚の画面写真の置き場。撮れるまで頁には**枠だけ**が出る（下の §画像） |

- 組み立て（build）はありません。`index.html` をそのまま置けば動きます。
- 明／暗は `prefers-color-scheme` で切り替わります。折り返しと押下域は 400 px 幅（携帯）で確かめる寸法にしてあります。
- 配布物（setup）には入りません。`.iss` の `docs` は明示列挙で、`site\` を含めていません。

## 文の正本と、書く物・書かない物

**文の正本＝`docs/design/v2-copy.md` §5**（憲章 `docs/design/charter.md` の出る物）。
この頁の文字を替えるときは、先に `v2-copy.md` §5 を直してからこちらへ写します。**この頁で文案を先に作らない。**

- **書く物**（`decisions.md` 125）＝⑴ はじめて開いたときにダウンロードが要ることを**釦より先に**書く
  ⑵ その理由（ほかの方が作ったプログラムやモデルを同梱しない方針＝`decisions.md` 8）を添える
  ⑶ 版の 2 択を大きく。
- **版の名は 2 つだけ**（`decisions.md` 128-4）＝**`RTX（CUDA）`／`Radeon（ROCm）`**。
  旧い「CUDA 版」「ROCm 版」「gfx1151」の綴りをこの頁に戻さない。
- **報告の送り先は X の `@yomiwakechan`**（`decisions.md` 131）＝脚の 1 行。
  **GitHub のリポジトリ（Issues）へは案内しない。**
- **消し方は `decisions.md` 132／133 のとおりに書く**＝アプリが自分の置き場に持っている物は全部消え、何も聞かれない。
  利用者が声を追加するときに選んだ**元の音声ファイルには触れない**。この 2 文を削らない。
- **2 つの版は別のアプリである**（`decisions.md` 133 ⑶）＝設定も、追加した声も、ダウンロードした物も別で、
  **同時には開けない**。消えるのは消した方だけ。この 2 文も「よくある質問」から削らない
  （両方入れた利用者が最初に当たる事実なので、着地頁で先に言う）。
- **書かない物**＝sha256・署名・バイト数・`SHA256SUMS.txt`（Release に置くだけ）・`gfx1151`・`cu130`／`cu126`・
  つなぎ口の番号・`install.md` への案内。憲章 §6-1 の隠す語（変種・実行系・台帳・サーバ ほか）も 1 語も出さない。
- **量の数は 3 つだけ**（憲章 §4 の「数字の 1 枚表」）＝ダウンロード **約 5.3 GB**／残る量 **約 8 GB**／
  作業に要る空き **12 GB**。ほかの量をこの頁に書かない。

## 落とす釦の宛先の決まり方

1. 頁を開いた時に JS が
   `https://api.github.com/repos/mugonkun/irodori-tts-for-yomiwakechan/releases/latest` を 1 回だけ読みます。
2. `assets[]` の中から**檔名の末尾**で選びます＝`-cuda.exe` → `RTX（CUDA）` の釦、`-radeon.exe` → `Radeon（ROCm）` の釦。
   選べたら `browser_download_url` を釦の宛先にし、釦の下の `version-line` に `tag_name`（例＝`最新版 v2.0.0`）を出します。
3. 取れなかったとき（API が落ちている・回数制限・JS が無効）は
   **HTML に最初から書いてある宛先**＝`https://github.com/mugonkun/irodori-tts-for-yomiwakechan/releases/latest`
   のままにします。釦は必ず押せて、必ずリリースの頁には着きます。

つまり**版を上げても、この檔を直す必要はありません**（檔名の型 `…-cuda.exe`／`…-radeon.exe` を変えない限り）。
**HTML に版の番号を直書きしない**＝`id="version-line"` の字は `最新版` の 3 文字だけで、
JS が `tag_name` を拾えた回にだけ「最新版 v…」へ書き換わります。拾えなかった回（API が落ちている・
回数制限・JS が無効）は番号を出さないまま、釦がリリースの頁へ送ります。
**まだ落とせない版の番号を見せない**ためで、版を上げても手で合わせる行はありません。

**確かめたこと**（2026-09-11・v2 の書き直しの席）

- `html.parser` で通し、閉じ忘れ・食い違い **0**、`id` の重複 **0**（`dl-cuda`／`dl-rocm`／`version-line` の 3 つだけ）。
- 外から読む資産 **0**（`src`／`href` の外部は、釦の 2 本のリリース宛先と脚の `https://x.com/yomiwakechan` だけ）。
- 隠す語の当て込み＝上の「書かない物」と憲章 §6-1 の表を全語で当てて、**見える文にも註にも 0 件**。
- **Chrome の headless で実際に描かせました**＝
  ①幅 400 CSS px（`--force-device-scale-factor=2 --window-size=800,3800`。headless の窓は 500 px 未満に縮まないので、
  倍率で 400 を作る）で横あふれ無し・釦は全幅で押下域 ≥ 56 px、
  ②暗（`--blink-settings=preferredColorScheme=0`）で明暗どちらも読める、
  ③どちらの射でも釦の下に**そのとき公開されている版**が出た＝API を読んで宛先を差し替える道が実際に通っています
  （`file://` から読めた＝別ドメインでも読めます）。
- `api.github.com` は `Access-Control-Allow-Origin: *` を返します。無認証の回数制限は **60 回／時・IP** で、
  超えると 403＝上の 3 の道に落ちます。

## 画像（3 枚・未撮影）

使い方の 3 手順には、いま**枠だけ**が出ています（`<div class="shot">`）。撮るのは RTX 機の席で、**v2.0 の画面**です。
古い版の画面を載せない（`v2-copy.md` §9-3 の 5）。

| 枠 | 置く檔 | `alt` |
|---|---|---|
| 画像 1 | `site/img/step1-setup.png` | インストーラの完了画面 |
| 画像 2 | `site/img/step2-setup.png` | はじめの準備の進み具合 |
| 画像 3 | `site/img/step3-talk.png` | しゃべらせているところ |

- 横 **1100 px 以上**（頁での表示は 960 px 目安）。
- 差し替え方＝各 `<figure>` の直前に置いた註（`<!-- 画像 N の差し替え口 … -->`）に、そのまま貼る 1 行が書いてあります。
  `<figure>` ごと入れ替え、`width`／`height`／`loading="lazy"` を付けてください。
- 宛先は `img/…` の**相対**にしてあります＝`main` の `site/img/` と `gh-pages` の根の `img/` の両方で当たります。
  画像を入れた回は、下の公開の手で `img/` も一緒に写してください。

## 公開の仕方（主席の仕事）

GitHub Pages・`gh-pages` 枝の**根**から出します。**`main` は触りません。**
`main` の作業樹を汚さないよう、`gh-pages` は **worktree を scratchpad に出して**触ります（`ben-e-installer.md` §21）。

```sh
# 初回だけ（枝がまだ無いとき）
git worktree add --orphan -b gh-pages "$SCRATCH/gh-pages"

# 2 度目から
git worktree add "$SCRATCH/gh-pages" gh-pages
cd "$SCRATCH/gh-pages" && git pull --ff-only origin gh-pages
```

```sh
# 写して commit（site\ という階を gh-pages に作らない＝index.html は根に置く）
cp <repo>/site/index.html "$SCRATCH/gh-pages/index.html"
cp -r <repo>/site/img     "$SCRATCH/gh-pages/img"      # 画像を入れた回だけ
touch "$SCRATCH/gh-pages/.nojekyll"                    # 初回だけ（Jekyll に触らせない）
cd "$SCRATCH/gh-pages"
git add index.html .nojekyll img 2>/dev/null
git commit -m "landing page v2.0.0"
git push origin gh-pages
```

```sh
# 片付け（worktree を残さない）
cd <repo> && git worktree remove "$SCRATCH/gh-pages"
```

- Pages の設定は **Settings → Pages → Source = Deploy from a branch → `gh-pages` / `/ (root)`**。
  API なら `source.branch=gh-pages, source.path=/`。**一度入れたら以後は触りません**（v1.1.0 の回に有効化済み）。
- リポジトリは 2026-09-10 から public なので Pages も出ます。「private だから出ない」という旧い註は**古い**。
- 出るまでに 1 分ほどかかります。押してから **釦 2 つの宛先が実物の資産に変わるか**を、公開した頁で 1 度確かめてください。
- 版を切る手順の中の位置＝`ben-e-installer.md` §18 の段 6（Release 公開）の**あと**。
  `site/index.html` が変わった回だけ走らせます。
