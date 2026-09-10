# site/ — 配布ページ（landing page）

利用者に「落として入れる」だけをさせるための 1 枚の頁です。開発者向けの言葉は置きません。

## 中身

| 檔 | 何 |
|---|---|
| `index.html` | 頁の全部（CSS も JS も中に書いてある・**外部資産ゼロ**＝画像・font・CDN を 1 つも読まない） |
| `README.md` | この檔（公開の仕方の記帳・**頁には出ない**） |

- 組み立て（build）はありません。`index.html` をそのまま置けば動きます。
- 明／暗は `prefers-color-scheme` で切り替わります。折り返しと押下域は 400 px 幅（携帯）で確かめる寸法にしてあります。
- 配布物（setup）には入りません。`.iss` の `docs` は明示列挙で、`site\` を含めていません。
- **書く物・書かない物**（`decisions.md` 125）＝⑴ 初回起動でダウンロードが要ることを**釦より先に**書く
  ⑵ その理由（他の方のプログラムやモデルを同梱しない方針＝`decisions.md` 8）を添える
  ⑶ NVIDIA の人は CUDA 版・Radeon の人は ROCm 版、の 2 択を大きく
  ⑷ **sha256・署名・バイト数の説明は出さない**（`SHA256SUMS.txt` は Release に置くだけ）。

## 落とす釦の宛先の決まり方

1. 頁を開いた時に JS が
   `https://api.github.com/repos/mugonkun/irodori-tts-for-yomiwakechan/releases/latest` を 1 回だけ読みます。
2. `assets[]` の中から**檔名の末尾**で選びます＝`-cuda.exe` → CUDA の釦、`-radeon.exe` → ROCm の釦。
   選べたら `browser_download_url` を釦の宛先にし、釦の下に `tag_name`（例＝`最新版 v1.1.0`）を出します。
3. 取れなかったとき（API が落ちている・回数制限・repo が private・JS が無効）は
   **HTML に最初から書いてある宛先**＝`https://github.com/mugonkun/irodori-tts-for-yomiwakechan/releases/latest`
   のままにします。釦は必ず押せて、必ずリリース頁には着きます。

つまり**版を上げても、この檔を直す必要はありません**（檔名の型 `…-cuda.exe`／`…-radeon.exe` を変えない限り）。

**確かめたこと**（2026-09-10・この席）

- `api.github.com` は `Access-Control-Allow-Origin: *` を返す＝別ドメインの頁から読めます。
  無認証の回数制限は **60 回／時・IP** で、超えると 403＝上の 2 の道に落ちます。
- 現行の `releases/latest`（`v1.0.2`）の `assets` は
  `irodori-tts-ywk-setup-v1.0.2-cuda.exe`／`irodori-tts-ywk-setup-v1.0.2-radeon.exe`／`SHA256SUMS.txt` で、
  末尾一致の選び方が両方とも当たることを実射で確かめました。
- HTML は `html.parser` で通し、閉じ忘れ・食い違い 0、外部の `src`／`href` 0 を確かめました。
- **Chrome の headless で実際に描かせました**＝
  ①幅 400 CSS px（`--force-device-scale-factor=2 --window-size=800,…`。headless の窓は 500 px 未満に縮まないので、
  倍率で 400 を作る）で横あふれ無し・釦は全幅で押下域 ≥ 56 px、
  ②暗（`--blink-settings=preferredColorScheme=0`）で明暗どちらも読める、
  ③どちらの射でも釦の下に **`最新版 v1.0.2`** が出た＝API を読んで宛先を差し替える道が実際に通っています
  （`file://` から読めた＝別ドメインでも読めます）。

## 公開の仕方（主席の仕事）

GitHub Pages・`gh-pages` 枝の**根**から出します（`main` は触らない）。

```
git switch --orphan gh-pages          # 初回だけ
git checkout main -- site/index.html
mv site/index.html index.html
git rm -r --cached site >/dev/null && rm -r site   # 索引にも載るので外す（枝に site\ を作らない）
git add index.html && git commit -m "landing page"
git push -u origin gh-pages
```

そのあと **Settings → Pages → Source = Deploy from a branch → `gh-pages` / `/ (root)`** を選びます。
出先＝`https://mugonkun.github.io/irodori-tts-for-yomiwakechan/`。

- **repo が private の間は Pages も出ません**（GitHub Free の場合）。公開の可否は主席の判断です。
- 2 度目からは `main` の `site/index.html` を上と同じ手で `gh-pages` の `index.html` に写すだけです。
- 頁からは `docs/install.md` を `blob/main` の宛先で指しています＝repo が公開されている前提です。
