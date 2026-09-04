# RTX 3090 機の台本（便 B・遠隔席が逐語で実行する）

> 正典＝`decisions.md`（19〜22・34・41〜57）。設計＝`docs/design/ben-b-rtx-remote.md`。受け入れ条件＝`docs/acceptance.md`。
> 実行するのは遠隔席 `12900k-new-enchanted-metcalfe`（Opus 5・Remote Control）。**この檔は「1 手順 1 コマンド」で書いてある。書いてあるコマンドを、書いてある順に、書いてあるとおりに撃つ。**
> 書いていないことを足さない。判断が要る所には「判断規則」を明示してある。規則で決まらない所に当たったら、**止まって司令官に上げる**（推測で進めない）。

> ### ⚠ 正典が動いた（`decisions.md` 53〜57・2026-09-05 02:58 のコミット `02b4587`）
>
> **これらは §3（B-7）に取り込み済み**（是正 2026-09-05）。B-3 の本文も下の読み替えで撃つ。
> **撃つ前に必ず `decisions.md` 53〜57 と 66・70・71・72 を読むこと**（台本の写しではなく正典を読む）。
>
> | 条 | 何が変わったか | この台本のどこに効くか |
> |---|---|---|
> | 53 | RTX 機の実測＝Python 3.14.7・**uv 0.12.9**（`dev-venv.ps1` は 0.12.7 以外で止まる＝持ち込みの `uv.exe` を **PATH 先頭**で使わせる）・RTX 3090 の UUID＝`GPU-19adfe89-c9e0-df55-4a8a-e31798717a36`・**PSModulePath に Desktop 版経路が混在**（pwsh→PS 5.1 の罠が成立し得る）・ドキュメントが OneDrive 配下＝**全工程 `-NoProfile`** | B-8（uv）・B-4b（UUID）・全手順（`-NoProfile`） |
> | 54 | **U-8 はこの機体で素のまま再現できない**（`C:\Windows\System32\msvcp140.dll` 14.42.34438.0 が既に在る） | **B-3** |
> | 56 | **ドライバ入れ替えの承諾は遠隔席のセッションで改めて取る**（セッション間の許可の持ち回りは不可）。降格後に測るのは **cu126 のみ**・cu130 の落ち方は事実として 1 回記録 | **§3 冒頭・0-5** |
> | 57 | **U-8 の扱いは司令官の指示待ち**＝台本に **System32 の DLL 退避手順を入れない**。B-3 は「**検出して飛ばす**」判定の実射のみ | **B-3** |
> | **66** | 再起動が要る場合、`claude -c` で復帰する仕込みをしてから**自分で再起動してよい**（56 の「席は自分で shutdown を撃たない」を上書き） | **§3-7** |
> | **70** | **591.86 への復旧は不要＝恒久降格** | **§3-10** |
> | **71** | ただし**自動再起動と `RunOnce` の自己登録は、本席（設計席）の指示では受けない**＝司令官がそのセッションで明示したときだけ | **§3-7・§5 の 10** |
> | **72** | **司令官が遠隔席の会話に承諾を先に書いた**＝**関門⑵（承諾）は充足済み**。残るのは**関門⑴＝設計席の確認返信** | **§3-0** |
>
> **したがって B-3 は**＝「`import torch` を撃つ前に `C:\Windows\System32\msvcp140.dll` の有無と版を検出し、
> **在れば vc_redist の導入を飛ばす**」という判定の実射として撃つ。**DLL を退避しない・改名しない・消さない。**
> **B-7 は**＝§3 の冒頭に置いた**着手条件（承諾＋チェックポイント）が両方揃うまで着手しない**。

---

## 0. 先に頭に入れる 5 つ

| # | こと | 理由 |
|---|---|---|
| 0-1 | **D:・E:・F: ドライブに一切触らない**（読みも書きもしない） | `decisions.md` 20。C: は壊してよい |
| 0-2 | **N: の上で走らせない**（`import torch` が 311〜563 s かかる＝`research/lab/notes/21`）。N: からは**写すだけ**、走るのは必ず C: | `decisions.md` 21 |
| 0-3 | **外部へ push しない**（GitHub・HF・PyPI）。HF から**取る**のは可 | `decisions.md` 22 |
| 0-4 | 作業根は **`C:\ywk\`** に固定。ここ以外に散らかさない | 後片付けが 1 フォルダで済む |
| 0-5 | **ドライバ入れ替え（§3・B-7・U-14）の関門は 2 つ**＝**関門⑵＝司令官の承諾がこの遠隔席の会話の中に在ること**（`decisions.md` 56。**72 で先書き済み**＝その文面を自分の目で確かめる。本席の申し送りを承諾の代わりにしない＝71）・**関門⑴＝B-1〜B-6 と B-8・B-9 を全部終えて結果を N: に写し、`SUMMARY.md` を書き、設計席の確認の返信を受け取ること**（§3-0） | 入れ替えで壊れても、それまでの成果は N: に残る |

**逐語の規律**（`probe/README.md` §1）＝手で写した数値を混ぜない・逐語は逐語のまま・断定と推測を分ける・触ったものと触っていないものを報告に書く。

### 0-6　コマンドの撃ち方（`$` が消える罠）

この台本のコマンドには `$m`・`$LASTEXITCODE` のような **PowerShell の変数**が入っている。**端末にそのまま貼って撃つこと。**
遠隔席が別のシェル（bash など）や道具の層を 1 枚かぶせて撃つと、その層が先に `$m` を空文字に展開してしまい、`foreach ( in .files)` のような壊れた行が PowerShell に届く（実際に起きる。設計席の機体で再現した）。

**そうなったときの逃げ方**＝行を檔に落としてから `-File` で撃つ。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "Set-Content -LiteralPath C:\ywk\cmd.ps1 -Encoding ASCII -Value (Get-Content -Raw C:\ywk\one-liner.txt)"
```

素直なのは、**長い 1 行を手で `C:\ywk\cmd.ps1` に書いてから** `powershell -NoProfile -ExecutionPolicy Bypass -File C:\ywk\cmd.ps1` で撃つこと。撃ち方を変えたら、変えたことを報告に書く。

### 0-7　いちばん最初に撃つ 1 本＝持ち込み物の HEAD が最新かを確かめる

**何よりも先にこれを撃つ。**`repo.bundle` は**コミット済みの履歴しか運ばない**。設計席が台本や道具を直してから
`build\make-handoff.ps1` を撃ち直すまでの間は、N: の持ち込み物は**一世代古い木**である。古い木で B-1〜B-9 を
全部走らせてから気付くと、測り直しになる。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$m = [System.IO.File]::ReadAllText('N:\temp_for_claudecode_agents\irodori-ywk\rtx-handoff\MANIFEST.json', [System.Text.Encoding]::UTF8) | ConvertFrom-Json; Write-Host ('generated_utc = ' + $m.generated_utc); Write-Host ('repo.head     = ' + $m.repo.head); Write-Host ('branch        = ' + $m.repo.branch); Write-Host ('head_line     = ' + $m.repo.head_line); Write-Host ('clean         = ' + $m.repo.working_tree_clean); foreach ($u in $m.repo.working_tree_status) { Write-Host ('  pending: ' + $u) }; foreach ($p in $m.probe_loose.files) { Write-Host ('  probe loose: ' + $p.name + '  in_bundle=' + $p.in_bundle) }"
```

**判断規則**＝

| 見たもの | 意味 | やること |
|---|---|---|
| `repo.head` が**設計席の返信に書いてある sha と一致** | 最新の木が届いている | **手順 1-1 へ進む** |
| `repo.head` が**違う sha**で、**設計席の返信が差し替えを告げている**（「新しい持ち込み物を置いた・新 HEAD は X」） | 設計席が走行中に持ち込み物を作り直した | **0-8 の差し替え手順を撃つ**（走行の途中でも可。ただし 0-8 に書いてある順のとおりに） |
| `repo.head` が**違う sha**で、**返信は何も言っていない** | 持ち込み物が古い（設計席がまだ作り直していない・作り直しが N: に届いていない）か、**告げられていない差し替えが起きた** | **止まって設計席に上げる**（「MANIFEST の repo.head は X、返信の sha は Y」と両方を逐語で書く）。**推測で走らせない・自分の判断で 0-8 を撃たない** |
| 設計席の返信に sha が**書いていない** | 照合先が無い | **止まって sha を訊く**。「たぶん最新」で走らせない |
| `working_tree_status` に行が在る | その檔は bundle の中身と食い違う | 行の頭が `??` なら**bundle に入っていない**（`probe\` の裸置きか、手順 1-7 で補う）。`??` 以外（` M` など）なら**bundle には古い内容で入っている**＝clone に出てくる版は設計席の手元より古い。**どちらもそのまま報告に書く** |

> `head_line` の日本語は `\uXXXX` で書いてある（MANIFEST.json は純 ASCII）。`ConvertFrom-Json` が読めば日本語に戻る。
> 読めなかったら**檔が壊れている**＝手順 1-3 の `verify-handoff.ps1` の結果と併せて報告する。

### 0-8　持ち込み物の差し替え（**走行中**）

設計席は、遠隔席が走っている**最中に**台本や道具を直し、`build\make-handoff.ps1` を撃ち直して
`N:\...\rtx-handoff\` の一式を**作り直す**ことがある。そのとき N: の `MANIFEST.json` の `repo.head` は
遠隔席の手元の clone の HEAD と食い違う（実際に 2026-09-05 に起きた）。

**規則＝差し替えは、設計席が返信で「差し替えた・新 HEAD は X」と告げたときだけ有効。**

* 返信が告げていないのに N: が変わっていたら（0-7 の表の 3 行目）＝**止まって上げる。**自分の判断で撃たない。
* 返信が告げていたら＝**下の ⑴〜⑺ を、この順に撃つ。**途中で 1 つでも合否を外したら**止まって上げる**。
* 撃ち終えたら、**どこから撃ち直すか**を返信で確かめる（差し替えの前に終えた測定がそのまま有効かは
  **設計席が決める**。台本には書いていない＝規則で決まらないので訊く）。

**⑴ N: の一式を C: に写し直す**（`/MIR`＝前の一式の残骸も消える。消えるのは `C:\ywk\handoff` の中だけ）

```
powershell -NoProfile -Command "robocopy N:\temp_for_claudecode_agents\irodori-ywk\rtx-handoff C:\ywk\handoff /MIR /R:2 /W:2 /NFL /NDL /NP; Write-Host ('EXIT=' + $LASTEXITCODE + '  OK=' + ($LASTEXITCODE -lt 8))"
```

**合否**＝`OK=True`。

**⑵ 写しを検証する**

```
powershell -NoProfile -ExecutionPolicy Bypass -File C:\ywk\handoff\verify-handoff.ps1
```

**合否**＝最後の行が `RESULT=OK`。`extra` は 0 のはず（`/MIR` で消えるので）。`FAIL` なら ⑴ からやり直す。

**⑶ 新しい bundle から取り込む**

```
git -C C:\ywk\repo fetch C:\ywk\handoff\repo.bundle main
```

**⑷ 木を新しい HEAD に合わせる**（**追跡檔だけ**が入れ替わる）

```
git -C C:\ywk\repo reset --hard FETCH_HEAD
```

> `reset --hard` が触るのは **git が追跡している檔だけ**である。`C:\ywk\repo\build\out\`（組み上げた runtime）・
> `C:\ywk\repo\.venv-dev`・`__pycache__`・`probe\` に裸で置いた未追跡の檔は**そのまま残る**＝組み直しは要らない。
> **`git clean` は撃たない**（撃つと build\out\ が消える）。

**⑸ submodule を bundle 経路で入れ直す**

```
git -C C:\ywk\repo -c protocol.file.allow=always submodule update --init
```

> 手順 1-5 で `.git\config` に書いた bundle の URL は `reset --hard` では消えない（`.git\config` は作業木ではない）ので、
> この 1 本はそのまま bundle から取る。**手順 1-5 を https 経路で通した席**なら `-c protocol.file.allow=always` は
> 効かないだけで害は無い。**どちらで入ったかを報告に書く。**

**⑹ HEAD が返信の sha と一致するか確かめる**

```
powershell -NoProfile -Command "git -C C:\ywk\repo log -1 --format='%H %ad %s' --date=iso"
```

**合否**＝ここで出る sha が**設計席の返信に書いてある新 HEAD の sha**と一致し、かつ ⑵ の `repo HEAD` とも一致する。
**違ったら止まる**（3 つの sha を逐語で並べて上げる）。

**⑺ `probe\` の 4 檔を整える**（**bundle に入った版を優先する＝裸置きで上書きしない**）

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$m = [System.IO.File]::ReadAllText('C:\ywk\handoff\MANIFEST.json', [System.Text.Encoding]::UTF8) | ConvertFrom-Json; foreach ($p in $m.probe_loose.files) { $dst = 'C:\ywk\repo\probe\' + $p.name; if ($p.in_bundle) { Write-Host ('in the bundle, left as the clone has it: ' + $p.name) } else { Copy-Item -LiteralPath ('C:\ywk\handoff\probe\' + $p.name) -Destination $dst -Force; Write-Host ('untracked, re-copied from the new handoff: ' + $p.name) } }"
```

**なぜこの形か**＝⑷ の `reset --hard` は**追跡檔を新しい版にする**（＝`in_bundle=true` の檔は既に新しい）。
一方 `in_bundle=false` の檔は**未追跡なので `reset --hard` が触らず、手順 1-7 で写した古い裸置きのまま残る**＝
**そこだけ上書きが要る。**手順 1-7 の 1 本は「clone に無ければ写す」形なので、この場面では**古いまま通してしまう**。
**差し替えのときは手順 1-7 ではなく、この ⑺ を撃つ。**

**記録すること**＝⑴〜⑺ の全出力・新旧の sha・`in_bundle` が偽だった檔の名（＝正典の木にまだ入っていない檔）。

---

## 1. 持ち込み物の受け取り（B-0）

設計席が `N:\temp_for_claudecode_agents\irodori-ywk\rtx-handoff\` に置いた一式を C: に写す。

### 手順 1-1　作業根を作る

```
powershell -NoProfile -Command "New-Item -ItemType Directory -Force -Path C:\ywk | Out-Null; Get-Item C:\ywk"
```

### 手順 1-2　持ち込み物を C: に写す

```
powershell -NoProfile -Command "robocopy N:\temp_for_claudecode_agents\irodori-ywk\rtx-handoff C:\ywk\handoff /E /R:2 /W:2 /NFL /NDL /NP; Write-Host ('EXIT=' + $LASTEXITCODE + '  OK=' + ($LASTEXITCODE -lt 8))"
```

**合否**＝`OK=True`（robocopy は 0〜7 が正常＝0 は差分なし・1 は写した。8 以上が失敗）。失敗ならやり直す。

> **終了コードの取り方について**＝この台本は終了コードを見る所を必ず `powershell -NoProfile -Command "& <台本>; Write-Host ('EXIT=' + $LASTEXITCODE)"` の**1 行**に畳んである。`powershell -File X` を撃った**次に**別の `powershell` を起こして `$LASTEXITCODE` を見ても、それは**別プロセスの変数**で常に空になる。分けて撃たない。

### 手順 1-3　持ち込み物を検証する（MANIFEST.json）

```
powershell -NoProfile -ExecutionPolicy Bypass -File C:\ywk\handoff\verify-handoff.ps1
```

**合否**＝最後の行が `RESULT=OK`。`RESULT=FAIL` なら**止まる**（写しが壊れている。手順 1-2 からやり直し、それでも直らなければ司令官に上げる）。
**記録すること**＝`repo HEAD` の行の sha（以後の全報告にこの sha を添える。**手順 0-7 で見た `repo.head` と同じはず**）・
`working tree : clean=` の値・その下の 2 種の行（`NOT in the bundle at all (untracked):` と
`in the bundle at its COMMITTED content, which is OLDER than this machine's copy (...):`）・`probe loose :` の 4 行・
`extra :` の行（0 のはず）。

> **`extra :` が 0 でないとき**＝MANIFEST.json が名指ししていない檔がこのフォルダに在る（前の持ち込み物の残骸など）。
> `WARN EXTRA:` の行を**そのまま報告する**。`RESULT=OK` は変わらない（台本が使う檔は全部揃って無傷なので）が、
> **その余分な檔は使わない**。

> `verify-handoff.ps1` は `MANIFEST.json` に載った全檔の sha256 と大きさを測り直す（写しの中身がそのまま届いたかを見る）。この 1 本には `$` が 1 つも要らない形（`-File`）にしてある＝下の注意の罠を踏まない。

### 手順 1-4　リポを C: に展開する

```
git clone C:\ywk\handoff\repo.bundle C:\ywk\repo
```

**合否**＝`C:\ywk\repo\decisions.md` が在る。

```
powershell -NoProfile -Command "git -C C:\ywk\repo log -1 --format='%H %ad %s' --date=iso"
```

**合否**＝ここで出る sha が手順 1-3 の `repo HEAD in manifest` と**一致**する。違ったら止まる。

### 手順 1-5　上流 2 本を bundle から入れる（GitHub の認証が無い前提）

```
git -C C:\ywk\repo submodule init
```

```
git -C C:\ywk\repo config submodule."upstream/Irodori-TTS".url C:/ywk/handoff/upstream/Irodori-TTS.bundle
```

```
git -C C:\ywk\repo config submodule."upstream/Irodori-TTS-Server".url C:/ywk/handoff/upstream/Irodori-TTS-Server.bundle
```

```
git -C C:\ywk\repo -c protocol.file.allow=always submodule update
```

> **`-c protocol.file.allow=always` は必須**。git 2.38 以降、submodule を **file 経路**（＝ローカルの bundle や path）から取ることは既定で禁止されている（CVE-2022-39253 の対策）。付けないと `fatal: transport 'file' not allowed` で止まる。**設計席の機体で実際に踏んで確かめた**（2026-09-05）。ここで許すのは、**手順 1-3 で sha256 を検証した自分の檔**だけであり、設定は `-c`（この 1 コマンドだけ）で渡すので機体の設定は変わらない。

**合否**＝

```
git -C C:\ywk\repo submodule status
```

が 2 行を返し、先頭が `8224dafb46d0aba89209a8f905f1cb7e3299d9c1 upstream/Irodori-TTS` と `841fb7c6ec57729c56b9b75c0ef2562249b13a10 upstream/Irodori-TTS-Server` であること（行頭に `-` や `+` が付いていないこと＝`-`＝未取得・`+`＝pin とずれている）。

> ネットが生きていて GitHub から直接取れるなら、手順 1-5 の 4 本を飛ばして `git -C C:\ywk\repo submodule update --init` 1 本でもよい（https 経路なので `protocol.file.allow` は要らない）。**上流 2 本は public なので gh の認証は要らない。**どちらで入れたかを報告に書く。
>
> **道具席が確かめたこと**（2026-09-05・Radeon 機で `N:` の同じ持ち込み物に対して実射）＝⑴ `git clone repo.bundle` は working tree ごと `main` を出す ⑵ 上の 4 本で submodule 2 本が pin どおり checkout され、`git status --porcelain` が両方とも空になる ⑶ ただし**とても長いパス**（200 文字級）に clone すると submodule の pack 書き出しが `unable to rename temporary '*.pack' file` で落ちた。`C:\ywk\repo` のような短いパスなら起きない。**作業根を `C:\ywk\` に固定しているのはこれも理由。**

### 手順 1-6　build/cache と uv を置く

```
powershell -NoProfile -Command "robocopy C:\ywk\handoff\build-cache C:\ywk\build-cache /E /R:2 /W:2 /NFL /NDL /NP; Write-Host ('EXIT=' + $LASTEXITCODE + '  OK=' + ($LASTEXITCODE -lt 8))"
```

```
powershell -NoProfile -Command "New-Item -ItemType Directory -Force -Path C:\ywk\uv | Out-Null; Copy-Item C:\ywk\handoff\uv\* C:\ywk\uv\ -Force; Get-ChildItem C:\ywk\uv | Select-Object Name,Length"
```

**合否**＝`C:\ywk\uv\uv.exe` が在る（41,628,672 B）。

### 手順 1-7　ベンチの道具が clone に入っているか確かめる（入っていなければ handoff から補う）

`repo.bundle` は**コミット済みの履歴しか運ばない**。持ち込み一式を作った時点で `probe\` の道具が未コミットだった場合、clone にはそれが無い。その場合に備えて同じ檔が `C:\ywk\handoff\probe\` にも裸で置いてある。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$m = Get-Content -Raw -Encoding UTF8 C:\ywk\handoff\MANIFEST.json | ConvertFrom-Json; $m.probe_loose.files | ForEach-Object { Write-Host ($_.name + '  in_bundle=' + $_.in_bundle) }; foreach ($f in @('bench_cases.json','cuda_bench.py','cuda-bench.ps1','rtx-remote-runbook.md')) { $p = 'C:\ywk\repo\probe\' + $f; if (-not (Test-Path -LiteralPath $p)) { Copy-Item ('C:\ywk\handoff\probe\' + $f) $p -Force; Write-Host ('copied from handoff: ' + $f) } else { Write-Host ('already in the clone: ' + $f) } }"
```

**合否**＝`C:\ywk\repo\probe\` に 4 檔すべてが在る。
**記録すること**＝どれを handoff から補ったか（＝その檔は正典の木にまだ入っていないという事実）。

---

## 2. B-1〜B-6・B-8〜B-9（本番）＋ B-10・B-11（任意）

> **B-7 はこの節に無い。**ドライバ降格（B-7・U-14）は**独立した最終ブロック＝§3**（`decisions.md` 56）。
> この節を全部終えて結果を N: に写し、`SUMMARY.md` を書き、設計席の確認の返信を受け取ってから §3 に入る。

以下はすべて `C:\ywk\repo` を作業ディレクトリにして撃つ。
**pwsh 7 と Windows PowerShell 5.1 の両方がこの機体にある。台本は両方で動く。以下は `powershell`（5.1）で書いてあるが、`pwsh` に置き換えてもよい。どちらで撃ったかを報告に書く。**

### B-0（前検査）　木が健全か

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\build\check-tree.ps1; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**合否**＝`EXIT=0`。
**読み方**＝第三者バイナリ 0 件・submodule が pin と一致・`__pycache__` 0 件。ここが赤なら以降は全部無意味なので**止まる**。

---

### B-1　cu130 変種を台帳だけから組む

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\build\assemble-runtime.ps1 -Variant cu130 -CacheRoot C:\ywk\build-cache; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**時間**＝torch cu130 の wheel が 1.87 GB・torchaudio 2.0 MB を**この機体が直接**取る（`C:\ywk\build-cache` には
torch と torchaudio を**除いた**共通の檔しか入っていない＝設計どおり）。回線しだいで 5〜30 分。

> **檔数を台本から写さない**（`probe/README.md` §1 の規律 1）。入っている檔の数は**機械が書いた値**を読む＝
> `MANIFEST.json` の `cache_selection.copied`。数えたければ下の 1 本を撃ち、**出た 2 つの数をそのまま報告に書く**。
>
> ```
> powershell -NoProfile -ExecutionPolicy Bypass -Command "$m = [System.IO.File]::ReadAllText('C:\ywk\handoff\MANIFEST.json', [System.Text.Encoding]::UTF8) | ConvertFrom-Json; Write-Host ('cache_selection.copied = ' + $m.cache_selection.copied); Write-Host ('on disk                = ' + (Get-ChildItem -LiteralPath C:\ywk\build-cache -File).Count); foreach ($n in $m.cache_selection.not_in_cache_rtx_will_fetch) { Write-Host ('  this machine fetches: ' + $n) }"
> ```
>
> **合否**＝`cache_selection.copied` と `on disk` が一致すること。違ったら**止まって上げる**（写しが欠けている）。
**合否**＝終了コード 0。`C:\ywk\repo\build\out\runtime-cu130\python.exe` が在る。
**読み方**＝ログ（`C:\ywk\repo\build\out\assemble-log\`）に `cache hit` と `fetched` が並ぶ。sha256 不一致は台本が自分で止まる（`decisions.md` 41＝手で写した sha256 は台帳に無い）。
**記録すること**＝所要時間・ダウンロード総量・`fetched` の行数。

**`fallback_url` の経路も 1 度は通す**（受け入れ条件 B-1 の後半）＝下の 1 本を撃ち、`download.pytorch.org`（fallback 側）からでも同じ sha256 で取れることを見る。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$j = Get-Content -Raw -Encoding UTF8 C:\ywk\repo\ledger\runtime-cu130.json | ConvertFrom-Json; $it = $j.items | Where-Object { $_.name -eq 'torchaudio' }; Write-Host ('url      = ' + $it.url); Write-Host ('fallback = ' + $it.fallback_url); $tmp = 'C:\ywk\fallback-test.whl'; Invoke-WebRequest -Uri ($it.fallback_url -split '#')[0] -OutFile $tmp -UseBasicParsing; $h = (Get-FileHash -LiteralPath $tmp -Algorithm SHA256).Hash.ToLowerInvariant(); Write-Host ('sha256 got = ' + $h); Write-Host ('sha256 ledger = ' + $it.sha256); Write-Host ('MATCH = ' + ($h -eq $it.sha256)); Remove-Item -LiteralPath $tmp -Force"
```

**合否**＝`MATCH = True`。`fallback_url` 欄が空なら「この台帳には fallback が無い」と**そのまま**報告する（作らない）。

> **この 1 本が証明していること／していないこと**（正直に書く）＝これは `fallback_url` から取った檔の
> **sha256 が台帳の値と同じ**であることの確認であって、**`assemble-runtime.ps1` の中の「主 URL が落ちたら
> fallback に切り替える」分岐を通したわけではない**（主 URL は生きているので、その分岐は走らない）。
> 報告には「fallback_url から同じ sha256 で取れることを確認した（組み立て台本の切り替え分岐は未実行）」と書く。
> 分岐そのものを通したいなら主 URL を故意に壊す必要があり、それは**台本に書いていない操作**なのでやらない。

---

### B-3（先に撃つ）　**U-8**＝vc_redist が無い状態で `import torch` がどう落ちるか

> **順番に注意**＝vc_redist を入れる**前**にこれを撃つ。入れてしまうと二度と観測できない。

```
C:\ywk\repo\build\out\runtime-cu130\python.exe -c "import torch; print(torch.__version__)"
```

**期待**＝`ImportError: DLL load failed while importing _C` 系（`msvcp140.dll` の欠落）。
**記録すること**＝**標準出力・標準エラーの全文を逐語で**（要約しない・訳さない）。

落ちなかった場合＝「既に msvcp140 がある」事実を記録する。

```
where.exe msvcp140.dll
```

```
powershell -NoProfile -Command "$p='C:\Windows\System32\msvcp140.dll'; if (Test-Path -LiteralPath $p) { (Get-Item $p).VersionInfo | Format-List FileName,FileVersion,ProductVersion } else { Write-Host 'C:\Windows\System32\msvcp140.dll: not present' }"
```

---

### B-3（続き）　vc_redist を台帳の直リンクから入れる

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$j = Get-Content -Raw -Encoding UTF8 C:\ywk\repo\ledger\vc_redist.json | ConvertFrom-Json; Write-Host ('url = ' + $j.url); Invoke-WebRequest -Uri $j.url -OutFile C:\ywk\VC_redist.x64.exe -UseBasicParsing; $h = (Get-FileHash -LiteralPath C:\ywk\VC_redist.x64.exe -Algorithm SHA256).Hash.ToLowerInvariant(); Write-Host ('sha256 got    = ' + $h); Write-Host ('sha256 ledger = ' + $j.sha256); Write-Host ('MATCH = ' + ($h -eq $j.sha256))"
```

**合否**＝`MATCH = True`。**False なら絶対に実行しない**（止まって司令官に上げる）。

> `C:\ywk\handoff\build-cache\VC_redist.x64.exe` にも同じ檔がある。回線が細ければそれを使ってよいが、その場合も上と同じ sha256 検証を通してから使う。

```
powershell -NoProfile -Command "& C:\ywk\VC_redist.x64.exe /install /passive /norestart | Out-Null; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**合否**＝終了コード 0（既に入っている場合の 1638 もある。出た数字をそのまま記録する）。

```
C:\ywk\repo\build\out\runtime-cu130\python.exe -c "import torch; print(torch.__version__, torch.version.cuda, torch.cuda.is_available(), torch.cuda.get_device_name(0))"
```

**合否**＝`2.10.0+cu130 13.0 True NVIDIA GeForce RTX 3090` の形で 1 行返る。
**記録すること**＝この 1 行の逐語。これが **B-3 の判定**。

---

### B-2　配布物を組んでモデルを初回取得

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\build\assemble-app.ps1; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**合否**＝終了コード 0。`C:\ywk\repo\build\out\app\server\ywk_server.py` と `...\app\server\upstream\Irodori-TTS\` が在る。

```
git -C C:\ywk\repo\upstream\Irodori-TTS status --porcelain
```

```
git -C C:\ywk\repo\upstream\Irodori-TTS-Server status --porcelain
```

**合否**＝両方とも**何も出力しない**（submodule が無改変＝`decisions.md` 3・22）。1 行でも出たら止まる。

モデルの初回取得（3 リポ 22 檔・約 3.5 GB）＝

```
powershell -NoProfile -Command "$env:HF_HUB_OFFLINE=''; C:\ywk\repo\build\out\runtime-cu130\python.exe -m ywk_fetch_models --dest C:\ywk\models"
```

**合否**＝終了コード 0。進捗が JSON 行で流れる。
**記録すること**＝所要時間・最後の JSON 行・`C:\ywk\models` の総サイズ。

```
powershell -NoProfile -Command "& C:\ywk\repo\build\out\runtime-cu130\python.exe -m ywk_fetch_models --dest C:\ywk\models --check-only; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**合否**＝**`EXIT=0`**。**いま取得したばかりのキャッシュに対する `--check-only` は 0 でなければならない。**

> **`decisions.md` 49 の読み違いに注意（是正 2026-09-05）。**49 は「`models.json` は pin した revision の
> **全檔**を持つので、**既存キャッシュ**に対する `--check-only` は README 等を `file_missing` と報告する
> （異常ではない）」と書いている。ここで大事なのは、**それが起きると終了コードは 2 になる**ことである
> （`server\ywk_fetch_models.py` の実装＝`file_missing` は `failures` に積まれ、`failures` が空でなければ
> `return 2`。**この席は檔を読んで確かめた・1 行も触っていない**）。つまり「`file_missing` は異常ではない」と
> 「終了コードは 0」は**両立しない**。
>
> **したがってこの手順の判定は**＝
>
> | いつ撃った `--check-only` か | 期待 | 外れたら |
> |---|---|---|
> | **直前の取得（この 1 つ上のコマンド）が終わった直後** | **`EXIT=0`**（取得は `models.json` の全檔を取ったので、欠けは 1 檔も無い） | **異常**。`file_missing`／`file_bad`／`refs_bad` の JSON 行を**逐語で**報告して止まる |
> | 別便でできた・古い・部分的なキャッシュに対して | `EXIT=2` が出得る（`file_missing` が README・`.gitattributes` 等の非重み檔について出る＝`decisions.md` 49） | 事実として記録する。**この台本の手順ではない** |
>
> **どちらの場合も**＝**重み檔（`model.safetensors`・`weights.pth`・透かし）について `file_missing` や
> `file_bad` が出たら異常**。`refs_bad`（`refs/main` が pin と違う）も異常。

```
powershell -NoProfile -Command "Get-ChildItem C:\ywk\models -Recurse -Filter refs -Directory | ForEach-Object { Get-ChildItem $_.FullName | ForEach-Object { Write-Host ($_.FullName + ' = ' + (Get-Content -Raw $_.FullName).Trim()) } }"
```

**合否**＝3 リポそれぞれに `refs\main` が在り、中身が `models.json` の revision と一致する（`decisions.md` 49＝これが無いと `HF_HUB_OFFLINE=1` で落ちる）。これが **B-2 の判定**。

---

### B-4 前半　wrapper が GPU で起きるか

> `build\verify-runtime.ps1` は**便 C の担当檔**で、この時点で `-Precision` と `-HfHome` を持っているとは限らない。
> **判断規則**＝下のコマンドで両方の引数があると出たら手順 B-4a を撃つ。無ければ **B-4a は飛ばす**（`/health`・`/params`・`/ywk/status` は次の B-4b の `cuda-bench.ps1` が同じことを撃って JSON に残すので、飛ばしても受け入れ条件は埋まる）。**飛ばしたことを報告に書く。**

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p = (Get-Command C:\ywk\repo\build\verify-runtime.ps1).Parameters.Keys; Write-Host ('has -Precision = ' + ($p -contains 'Precision')); Write-Host ('has -HfHome = ' + ($p -contains 'HfHome')); Write-Host ('params = ' + ($p -join ','))"
```

**手順 B-4a（両方あるときだけ）**

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\build\verify-runtime.ps1 -Variant cu130 -WithModel -Device cuda:0 -Precision bf16 -HfHome C:\ywk\models; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**合否**＝終了コード 0。

---

### B-4 後半・B-5　cu130 のベンチ（速度・コールド・VRAM・未見の参照形状）

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\probe\cuda-bench.ps1 -Variant cu130 -Device cuda:0 -Precision bf16 -HfHome C:\ywk\models -ResultRoot N:\temp_for_claudecode_agents\irodori-ywk\rtx; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**時間**＝条件 12 × 4 射＋プリセット 11 射＝59 射。3090 なら 3〜6 分（＋モデル読込 10 s 級）。
**この 1 本がやること**＝参照ボイスを使い捨ての `voices` に置く（30 s 版 11 本＋先頭 10.0 s を切った 10 s 版 1 本）→ wrapper を bf16・cuda:0 で起こす→ `probe\cuda_bench.py` が ready 待ち→ 1 発目（cold）→各条件 warm 3 射（中央値）→プリセット 11 本を 1 射ずつ→ **必ずツリー kill** → kill 後 10 s の VRAM → 結果を `build\out\probe-log\` と `-ResultRoot` に写す。

**合否**＝**`EXIT=0` かつ `CHECKS=OK`**（下の `status.txt` の 2 行で見る）。

```
powershell -NoProfile -Command "Get-Content C:\ywk\repo\build\out\probe-log\cuda-bench-cu130.status.txt"
```

> **`EXIT` だけでは足りない（是正 2026-09-05）。**終了コードが表しているのは
> **「撃った全射が 200 で wav を返した」1 点だけ**である。`cuda_bench.py` はほかにも
> `GET /health`・`GET /params`・`GET /ywk/status` の可否を `checks[]` に積んでいるが、
> **それらは終了コードを動かさない**＝`/params` が 500 を返していても `EXIT=0` で終わり得る。
> そこで `status.txt` に **`CHECKS=`** の行を出すようにした。
>
> | `status.txt` の行 | 意味 |
> |---|---|
> | `CHECKS=OK` | `checks[]` が全部 `ok=true` |
> | `CHECKS=FAIL` | 1 つ以上が `ok=false`。**落ちた名前は `CHECKS_FAILED=` の行に並ぶ**（逐語で報告する） |
> | `CHECKS=NO-JSON` / `CHECKS=UNREADABLE` / `CHECKS=UNKNOWN` | 測定 JSON が無い／読めない／`checks[]` が空。**いずれも異常として報告する** |
>
> **B-4 の合否は `EXIT=0` かつ `CHECKS=OK`。**片方でも外れたら、`status.txt` 全文と
> `cuda-bench-cu130.json` の `checks` 配列を逐語で報告する。
>
> **`cuda-bench-cu130.prev.status.txt` が在ったら**＝この slug を**2 回以上撃った**という事実である
> （台本は 1 世代だけ前の判定を残す）。撃ち直した理由と、前回の `EXIT=`／`CHECKS=` を報告に書く。

**結果の読み方**（`C:\ywk\repo\build\out\probe-log\cuda-bench-cu130.json`）＝

> **JSON を読むときは必ず `-Encoding UTF8` を付ける。**Windows PowerShell 5.1 の `Get-Content -Raw` は
> **ANSI コードページ（この機体は ja-JP なので CP932）**で読む。UTF-8 の檔をそのまま食わせると
> 日本語が化け、化けが閉じ引用符まで食って `ConvertFrom-Json : Invalid object passed in, ':' or '}' expected`
> で落ちる（**設計席の機体で 2026-09-05 に再現した実測**）。`cuda_bench.py` の側でも JSON を
> **純 ASCII（日本語は `\uXXXX`）**で書くように直してあるので二重に安全だが、読み方は揃えておく。
> pwsh 7 では `-Encoding UTF8` は既定なので、付けても害はない。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$d = Get-Content -Raw -Encoding UTF8 C:\ywk\repo\build\out\probe-log\cuda-bench-cu130.json | ConvertFrom-Json; Write-Host ('device = ' + $d.status_after_ready.device.actual + ' / ' + $d.status_after_ready.device.name + ' / uuid ' + $d.status_after_ready.device.uuid + ' / pci_bus_id ' + $d.status_after_ready.device.pci_bus_id + ' / precision ' + $d.status_after_ready.device.precision); Write-Host ('cold   = ' + $d.run_cold.elapsed_seconds + ' s'); Write-Host ('VRAM idle/peak/afterkill = ' + $d.vram.idle_before_start_mib + ' / ' + $d.vram.peak_observed_mib + ' / ' + $d.vram.after_kill.after_wait_mib + ' MiB'); $d.conditions | ForEach-Object { Write-Host ($_.case + '/' + $_.steps + '/' + $_.reference + '  warm_median_rtf_http=' + $_.warm_median_rtf_http + '  ms=' + $_.warm_median_elapsed_ms) }; $d.budget_checks | ForEach-Object { Write-Host ('budget ' + $_.name + ' ok=' + $_.ok) }; $d.checks | ForEach-Object { Write-Host ('check ' + $_.check + ' ok=' + $_.ok + '  ' + $_.detail) }; Write-Host ('preset sweep spread_ratio = ' + $d.preset_sweep.summary.spread_ratio + ' slowest=' + $d.preset_sweep.summary.slowest_id)"
```

> **`pci_bus_id` を印字させているのは受け入れ条件だから**＝下の表の 1 行目が `uuid` と `pci_bus_id` の
> **両方が非 null** を求めている。以前この読み出しは `uuid` しか出しておらず、**表が求める片方を
> 誰も見ないまま合格を書ける**形になっていた（是正 2026-09-05）。`pci_bus_id` が空欄で出たら、
> それは **`/ywk/status` が返していない**という事実なので、そのまま報告する（推測で埋めない）。

| 見る欄 | 受け入れ条件 | 合格の形 | 注意 |
|---|---|---|---|
| `status_after_ready.device` | B-4 | `actual`＝`cuda:0`・`name`＝`NVIDIA GeForce RTX 3090`・`uuid` と `pci_bus_id` が非 null・`precision`＝`bf16` | `actual` が `cpu` なら**黙った CPU 転落**＝重大。止まって報告 |
| `run_cold.elapsed_seconds` | コールド ≤ 2.0 s | 2.0 s 以下 | ただしこれは **HTTP 往復**の秒数。`research/lab/notes/27` の 1.032 s は合成だけの秒数なので、少し大きく出るのが正常 |
| `conditions[].warm_median_rtf_http` | 速度行 | 短文40/none ≤ 0.25・長文40/none ≤ 0.10・**40 steps の参照あり（ref10・ref30）≤ 0.35**・短文10/none ≤ 0.10・**短文 10 steps の参照あり（ref10・ref30）≤ 0.15** | **`rtf_http` は kit の `rtf_synth` より必ず大きい**（JSON の `rtf_definitions` に両方の定義が書いてある）。超えたら「HTTP 込みで超えた」と書く。**「受け入れ条件を割った」と断定しない** |
| `vram.peak_observed_mib` | VRAM ≤ 3.2 GiB（bf16） | 3277 MiB 以下 | `nvidia-smi` の `memory.used` はプロセス全体の占有。`docs/acceptance.md` の注記＝ピークは出力潜在フレーム数に比例する |
| `vram.after_kill.after_wait_mib` | kill 後 10 s で idle | `vram.idle_before_start_mib` と同じ水準に戻る | 戻らなければ逐語で記録 |
| `preset_sweep.summary.spread_ratio` | **B-5** | 1 に近い＝CUDA には未見の参照形状の初見罰が**無い** | 1 本だけ中央値の数倍なら ROCm と同じ罰が CUDA にもある＝`decisions.md` 40 の暖機を CUDA でも既定 ON にする根拠。`slowest_id` を必ず報告に書く |

> **予算（`budget_checks`）の読み方＝ここを間違えないこと。**
>
> 1. **`budgets` の値は kit の `rtf_synth`（`total_to_decode ÷ audio_seconds`）で測られた実測に対して置かれている。**
>    この台本が測る `rtf_http` は**必ずそれより大きい**（チャンク結合・wav 直列化・uvicorn・ループバック往復が乗る）。
>    したがって `budget_checks[].ok=false` は **「HTTP 込みで予算を超えた」という測定**であって、
>    **受け入れ条件の不合格ではない**。既定では終了コードも変わらない（`-FailOnBudget` を付けたときだけ 4 になる）。
> 2. **`docs/acceptance.md` の値と研究の実測は、同じ `rtf_synth` どうしでも既に食い違っている。**
>    ⑴ **10 steps 短文**＝acceptance は「≤ 0.10・実測 0.080」と書くが、これは `research/lab/notes/27`（参照なし・SSD）の値。
>    参照条件を並べた `research/lab/notes/33` 表 1 の同じ行は **0.090** で、予算 0.10 との余裕は 10 % しかない。
>    ⑵ **長文 40 steps 参照なし**＝acceptance は「≤ 0.10・実測 0.084」（`notes/27`）だが、
>    `notes/33` 表 1 の同じ条件は **1.232 s／0.112** で、**0.10 を超えている実測が既に存在する**。
>    したがって長文が 0.10 を超えても、それだけでは「新しい未達」ではない。
> 3. **書き方**＝超えた条件は「`rtf_http` が X で予算 Y を超えた。予算は `rtf_synth` 基準であり、
>    研究の `rtf_synth` 実測にも 0.112（`notes/33`）の例がある」と**両方**書く。
>    `docs/acceptance.md` は**この席では書き換えない**（便 C の担当檔＝設計席に申し送る）。

**記録すること**＝上のコマンドの出力全文＋`cuda-bench-cu130.json` の所在。

---

### B-4b　`CUDA_VISIBLE_DEVICES` に UUID を渡す経路（`decisions.md` 34）

> **なぜ撃つか**＝`decisions.md` 34＝「`CUDA_VISIBLE_DEVICES` に UUID 形式（`GPU-xxxx`）を渡す経路は
> **調査に実射記録が無い**ので、便 B で確かめてから採否を決める。それまでランチャは UUID→index 解決＋`cuda:N` で指定する」。
> **この 3 手順がその実射**である。通れば便 D のランチャは UUID をそのまま env に載せられる。

**手順 B-4b-1　この機体の UUID を読む**

```
nvidia-smi -L
```

**記録すること**＝この出力の逐語（`GPU 0: NVIDIA GeForce RTX 3090 (UUID: GPU-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)` の形）。
**取り出すもの**＝括弧の中の `GPU-` で始まる文字列**そのもの**（`UUID: ` は含めない・大文字小文字を変えない）。

> **予想される値**＝`GPU-19adfe89-c9e0-df55-4a8a-e31798717a36`（`decisions.md` 53＝疎通確認のときにこの機体が名乗った値）。
> **ただし手順 B-4b-2 に貼るのは、いま `nvidia-smi -L` が返した文字列**であって、ここに書いてある予想ではない。
> **違っていたら、違っていたことをそのまま報告に書く**（`decisions.md` 53 の事実が古かった、という事実になる）。

**手順 B-4b-2　その UUID を載せて 1 射だけ撃つ**

下の `<UUID>` を手順 B-4b-1 で読んだ文字列に置き換えて撃つ。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\probe\cuda-bench.ps1 -Variant cu130 -Device cuda:0 -Precision bf16 -HfHome C:\ywk\models -CudaVisibleDevices <UUID> -Quick -Label uuidroute -ResultRoot N:\temp_for_claudecode_agents\irodori-ywk\rtx; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**この 1 本がやること**＝`CUDA_VISIBLE_DEVICES=<UUID>` を**そのまま**子プロセスの env に載せ、`-Device cuda:0` で 1 射だけ撃つ。
**`-Label uuidroute` を付けているので出力は `cuda-bench-cu130-uuidroute.*` になり、B-4 本測定（`cuda-bench-cu130.*`）を上書きしない。**
**`-Quick` なので速度行の実測として使わない**（短文・40 steps・参照なし・warm 1 射だけ）。

**手順 B-4b-3　一致したかを見る**

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$d = Get-Content -Raw -Encoding UTF8 C:\ywk\repo\build\out\probe-log\cuda-bench-cu130-uuidroute.json | ConvertFrom-Json; $u = $d.cuda_visible_devices_uuid; Write-Host ('CUDA_VISIBLE_DEVICES = ' + $u.cuda_visible_devices); Write-Host ('is_uuid_form         = ' + $u.is_uuid_form); Write-Host ('status device.uuid   = ' + $u.status_device_uuid); Write-Host ('status device.actual = ' + $u.status_device_actual); Write-Host ('status device.name   = ' + $u.status_device_name); Write-Host ('MATCH                = ' + $u.match); Write-Host ('shots ok             = ' + $d.ok)"
```

**判定**＝

| 結果 | 意味 | 書き方 |
|---|---|---|
| `MATCH = True` かつ `shots ok = True` | **UUID をそのまま `CUDA_VISIBLE_DEVICES` に載せる経路は動く** | `decisions.md` 34 の「UUID→index 解決」は不要にできる＝便 D の設計席に返す |
| `MATCH = False`（起きるが別のカードを掴む・`device.uuid` が null） | 経路は通るが同定にならない | 逐語で。34 の暫定策（UUID→index 解決＋`cuda:N`）を維持 |
| `shots ok = False`／`device.actual` が `cpu`／起動しない | **UUID 形式は受け付けられない** | `server-cu130-uuidroute.err.log` の末尾 50 行を逐語で。34 の暫定策を維持 |

**記録すること**＝上の 7 行の出力全文と、判定表のどの行に当たったか。**この機体は GPU 1 枚なので、
「別のカードを掴んでしまう」失敗は原理的に観測できない**＝その旨も報告に書く（1 枚での確認である、と正直に）。

---

### B-6　cu126 変種を組んで同じベンチ

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\build\assemble-runtime.ps1 -Variant cu126 -CacheRoot C:\ywk\build-cache; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**時間**＝torch cu126 が 2.59 GB。
**合否**＝終了コード 0。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\probe\cuda-bench.ps1 -Variant cu126 -Device cuda:0 -Precision bf16 -HfHome C:\ywk\models -ResultRoot N:\temp_for_claudecode_agents\irodori-ywk\rtx; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**合否**＝終了コード 0。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$a = Get-Content -Raw -Encoding UTF8 C:\ywk\repo\build\out\probe-log\cuda-bench-cu130.json | ConvertFrom-Json; $b = Get-Content -Raw -Encoding UTF8 C:\ywk\repo\build\out\probe-log\cuda-bench-cu126.json | ConvertFrom-Json; for ($i = 0; $i -lt $a.conditions.Count; $i++) { $x = $a.conditions[$i]; $y = $b.conditions[$i]; if ($null -ne $x.warm_median_elapsed_ms -and $null -ne $y.warm_median_elapsed_ms -and $x.warm_median_elapsed_ms -gt 0) { $d = [math]::Round((($y.warm_median_elapsed_ms - $x.warm_median_elapsed_ms) / $x.warm_median_elapsed_ms) * 100.0, 1); Write-Host ($x.case + '/' + $x.steps + '/' + $x.reference + '  cu130=' + $x.warm_median_elapsed_ms + ' ms  cu126=' + $y.warm_median_elapsed_ms + ' ms  diff=' + $d + ' %') } }"
```

**合否**＝**B-6**＝全条件で差が **±5 % 以内**。外れた条件があればその条件名と % を逐語で報告する（外れたこと自体は事実であって失敗ではない）。

---

### B-8　clone した木で契約テストが緑か

```
powershell -NoProfile -Command "$env:PATH = 'C:\ywk\uv;' + $env:PATH; $env:UV_PYTHON_INSTALL_DIR = 'C:\ywk\uv-python'; uv --version"
```

**合否**＝`uv 0.12.7` と出る（`build\dev-venv.ps1` は 0.12.7 以外で止まる）。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$env:PATH = 'C:\ywk\uv;' + $env:PATH; $env:UV_PYTHON_INSTALL_DIR = 'C:\ywk\uv-python'; & C:\ywk\repo\build\dev-venv.ps1; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**合否**＝終了コード 0。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\build\run-tests.ps1; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**合否**＝**`EXIT=0`**。それだけ。

> **本数を台本に書かない（是正 2026-09-05）。**以前ここには「175 本前後が緑」と書いてあったが、
> **契約テストの本数は clone した木＝`repo.bundle` が運んだ HEAD で決まる**のであって、この台本が決めるものではない。
> 便 C が `tests/` に 1 本足せば 176 になり、台本の数字は即座に嘘になる。**手で写した数値を混ぜない**
> （`probe/README.md` §1 の規律 1）。
>
> **報告に書くのは、pytest が実際に印字した行そのもの**＝末尾の `===== N passed in X.XXs =====`（または
> `N passed, M skipped`・`N failed`）の**1 行を逐語で**。数を数え直さない・丸めない・「前後」と書かない。
> `EXIT=0` と、その行と、手順 0-7／1-3 で確かめた `repo HEAD` の sha を 3 点セットで出せば、
> 「どの木で何本通ったか」が後から復元できる。

**読み方**＝ここが緑であることが `.gitattributes` の CRLF 対策の実証（`patches/*.patch` が LF のまま clone されたか・`licenses/**/LICENSE` がバイト同一で来たか）。落ちたテストがあれば**テスト名と assert の逐語**を報告に書く。これが **B-8**。

---

### B-9　結果を N: に写す

**probe の結果は写し直さない。**`cuda-bench.ps1 -ResultRoot` が走行ごとに
`N:\temp_for_claudecode_agents\irodori-ywk\rtx\probe-log-<slug>\` へ**その走行が書いた檔だけ**を写している
（`<slug>`＝`-Label` 無しなら変種名・有りなら `変種-ラベル`）。ここで `build\out\probe-log` を丸ごと
もう一度 robocopy すると、**同じ数値が 2 か所に別の名前で並び、どちらがその走行の写しなのか分からなくなる**ので撃たない。
写すのは probe **以外**のログだけ＝

```
powershell -NoProfile -Command "robocopy C:\ywk\repo\build\out\assemble-log N:\temp_for_claudecode_agents\irodori-ywk\rtx\assemble-log /E /R:2 /W:2 /NFL /NDL /NP; Write-Host ('EXIT=' + $LASTEXITCODE + '  OK=' + ($LASTEXITCODE -lt 8))"
```

```
powershell -NoProfile -Command "robocopy C:\ywk\repo\build\out\test-log N:\temp_for_claudecode_agents\irodori-ywk\rtx\test-log /E /R:2 /W:2 /NFL /NDL /NP; Write-Host ('EXIT=' + $LASTEXITCODE + '  OK=' + ($LASTEXITCODE -lt 8))"
```

**wav は 6 本まで**（`cuda-bench.ps1` が `build\out\probe-log\wav-<slug>\` に条件ごとの 1 発目を最大 6 本だけ残し、
`-ResultRoot` の `probe-log-<slug>\wav-<slug>\` へ同じものを写す）。

**写った先を数える**（これが B-9 の判定）＝

```
powershell -NoProfile -Command "Get-ChildItem N:\temp_for_claudecode_agents\irodori-ywk\rtx -Recurse -File | Select-Object FullName,Length | Format-Table -AutoSize"
```

**合否**＝走行した変種ごとに `probe-log-<slug>\cuda-bench-<slug>.json` と `.status.txt` が在ること。

**最後に停止域の証明を撃つ**＝

```
git -C C:\ywk\repo\upstream\Irodori-TTS status --porcelain
```

```
git -C C:\ywk\repo\upstream\Irodori-TTS-Server status --porcelain
```

```
powershell -NoProfile -Command "Get-ChildItem C:\ -Force -Directory | Where-Object { $_.LastWriteTime -gt (Get-Date).AddDays(-1) } | Select-Object Name,LastWriteTime | Format-Table -AutoSize"
```

> **この 1 本は `C:` しか見ない。**以前ここには `Get-PSDrive -PSProvider FileSystem` が置いてあったが、
> それは **D:／E:／F: を列挙して大きさを読む**＝停止域 0-1（`decisions.md` 20＝「読みも書きもしない」）に
> 自分で違反する 1 本だった。**停止域を守った証明のために停止域を破ってはいけない**ので撃たない。
> 「D:/E:/F: に触っていない」は**触っていないという事実を書く**ことで示す（列挙して見せるものではない）。

**報告に書くこと**＝上流 2 本が無出力であること・**D:/E:/F: を一度も列挙も参照もしていないこと**・
C: のどこを使ったか（`C:\ywk\` と、そこに置いた `C:\ywk\repo\build\out\`。ドライバ導入（§3）をやった場合は
`C:\ywk\driver\` と、`C:\Windows` 以下も NVIDIA のインストーラが触る＝その旨も書く）。

**ここで §2 は終わり。**次は **§3-0 の関門**＝`SUMMARY.md` を書き、設計席へ返信し、
**関門⑴（設計席の確認返信）と関門⑵（司令官の承諾＝`decisions.md` 72 で先書き済み・文面を自分の目で確かめる）が
両方揃うまで §3 に入らない**。

---

### B-10（**任意**）　CUDA wheel を入れたまま GPU を隠して CPU 合成が 200 で返るか（W-1）

> **これは受け入れ条件ではない**＝`probe/README.md` §2 の申し送り（`docs/acceptance.md` §3 の 6）。
> **B-9 まで終えて結果を N: に写した後**、時間が余ったときだけ撃つ。撃たなくてよい。
> **狙い**＝配布した cu130 変種を **NVIDIA が無い機体**に入れた利用者がどうなるかを、GPU を隠すことで代用して見る。
> **代用であることを必ず報告に書く**（本物の NVIDIA 無し機ではない＝ドライバ DLL は在る）。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\probe\cuda-bench.ps1 -Variant cu130 -Device cpu -Precision fp32 -HfHome C:\ywk\models -CudaVisibleDevices '-1' -Quick -Label w1-nogpu -ResultRoot N:\temp_for_claudecode_agents\irodori-ywk\rtx; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**`CUDA_VISIBLE_DEVICES=-1` が「GPU を隠す」指定**（NVIDIA の既定の作法＝どのデバイスも見せない）。
**合否**＝終了コード 0＝**CUDA wheel のまま CPU で 200 が返った**。非ゼロなら**その逐語がそのまま成果物**。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$d = Get-Content -Raw -Encoding UTF8 C:\ywk\repo\build\out\probe-log\cuda-bench-cu130-w1-nogpu.json | ConvertFrom-Json; Write-Host ('device.actual = ' + $d.status_after_ready.device.actual); Write-Host ('torch.cuda    = ' + $d.status_after_ready.torch.cuda); Write-Host ('ok            = ' + $d.ok); Write-Host ('errors        = ' + ($d.errors -join ' | '))"
```

**記録すること**＝`device.actual`（`cpu` になるはず）・`ok`・`errors`・`server-cu130-w1-nogpu.err.log` の末尾 20 行。
**速度の数字は使わない**（`-Quick`・CPU）。

---

### B-11（**任意**）　`torch/lib` の削れる DLL を数える（A14・A15）

> **これも受け入れ条件ではない**＝`licenses/first-run-notices.md` A14・A15 の申し送り。**測るだけで、消さない。**
> 消してよいかの判断は設計席が別便でやる。ここは**在るか・何バイトか**を報告するだけ。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "foreach ($v in @('cu130','cu126')) { $d = 'C:\ywk\repo\build\out\runtime-' + $v + '\Lib\site-packages\torch\lib'; if (-not (Test-Path -LiteralPath $d)) { Write-Host ($v + ': no torch\lib'); continue }; Get-ChildItem -LiteralPath $d -File | Where-Object { $_.Name -like 'zlibwapi*' -or $_.Name -like 'nvperf*' -or $_.Name -like 'nvrtc*' } | ForEach-Object { Write-Host ($v + '  ' + $_.Name + '  ' + $_.Length + ' B') } }"
```

**記録すること**＝出力の全行（変種ごとに檔名とバイト数）。`zlibwapi.dll` が**在る／無い**は
`licenses/first-run-notices.md` A14 の「出所」の議論にそのまま効く事実なので、**在っても無くても**そのまま書く。
**削除・改名・移動はしない**（`decisions.md` 22＝ライセンスの結論を推測で断定しない）。

---

## 3. B-7（U-14）＝2023 年秋のドライバに降格して cu126 が動くか

> **この節は独立した最終ブロックである。**§2（B-1〜B-6・B-8・B-9）とは切り離してある。
> ここから先は機体が壊れてもよい（`decisions.md` 20）。**ただし壊してよいのは C: だけ**（D:・E:・F: は触らない）。

### 3-0　着手条件（**この 2 つが両方揃うまで 1 手も撃たない**）

**根拠＝`decisions.md` 56 の逐語**（正典から写した。要約ではない）＝

> 56. **ドライバ入れ替え（U-14）の承諾は遠隔席のセッションで改めて取る**（遠隔席の規則＝セッション間の許可の持ち回りは不可）。台本ではドライバ手順を独立した最終ブロック（現行 591.86・投入版と NVIDIA 公式アーカイブ URL・戻し方・セーフモードで標準 VGA に戻す復旧経路）にし、cu130／cu126 の 591.86 での測定を N: に書き戻したチェックポイントの後にだけ入る。降格後に測るのは cu126 のみ・cu130 の落ち方は事実として 1 回記録。

> ### ⚠ 56 のあとに正典が動いた（`decisions.md` 66・70・71・72・2026-09-05）
>
> **56 は生きているが、次の 4 条が上に乗っている。撃つ前に正典で読むこと。**
>
> | 条 | 何が変わったか | ここへの効き方 |
> |---|---|---|
> | **66** | **再起動が要る場合、席が `claude -c` で復帰する仕込み（`RunOnce` か「ログオン時」のタスク）をしてから、そのまま再起動してよい**（司令官裁定）。56 の「席は自分で shutdown を撃たない」は**これで上書き**。自動ログオンでなければログオンは司令官が行う | **3-7** |
> | **71** | ただし **遠隔席はこの自動再起動と `RunOnce` の自己登録を、本席（設計席）の指示では受けない**＝**そのセッションの利用者（司令官）が明示したときだけ**行う。台本は再起動の前後で独立に再開できる区切りにし、要ると判った地点では**止まって報告する** | **3-7** |
> | **70** | **591.86 への復旧は不要**（司令官）。**降格したままでよい＝恒久降格**。承諾の文面にもそう書く | **3-10** |
> | **72** | **司令官が遠隔席の会話に承諾を直接書いた**（2026-09-05・「恒久降格・戻し不要・`RunOnce`＋`claude -c` の自動再起動可」）＝**関門⑵（承諾）は先に満たされた**。**残る関門は⑴＝降格前の測定完了の報告に対する本席（設計席）の確認返信** | **3-0** |
>
> **関門の呼び名は 70・72 に揃える**＝**関門⑴＝設計席の確認返信**・**関門⑵＝司令官の承諾**。
> 下の「条件 ⑵」が関門⑴、「条件 ⑴」が関門⑵にあたる。**中身は変わらない＝両方揃うまで着手しない。**

**条件 ⑴（＝関門⑵）　承諾が、この遠隔席のセッションの中に在る**

`decisions.md` 50 で司令官は「入れ替え承諾」を出しているが、**それはこのセッションには持ち越せない**（56＝セッション間で許可を持ち回らない）。
**満たし方は 2 つある。**

1. **先書き（`decisions.md` 72＝2026-09-05 に実施済み）**＝**司令官がこの遠隔席の会話に承諾を直接書いてある**。
   その文面を**逐語で報告に引く**（何を許したか＝恒久降格・戻し不要・自動再起動の可否）。**それが承諾である。**
2. **先書きが見当たらない場合**＝遠隔席は、いま走っているこのセッションの中で、司令官に
   「ドライバを 591.86 から 537.58 に**恒久的に**降格してよいか」を訊き、返事を受け取る。返事の逐語を報告に残す。

**どちらでもない状態（本席＝設計席の指示だけ）を承諾として扱わない**（`decisions.md` 71＝席の許可は本席からは渡せない）。

**条件 ⑵（＝関門⑴）　チェックポイント（入れ替え前の測定が N: に在り、設計席の確認返信が来ている）**

次の 3 つが全部済んでいること。

1. **B-1〜B-6・B-8・B-9 を終えている**（§2 を最後まで撃ち切っている）。
2. **結果が N: に書き戻されている**＝`N:\temp_for_claudecode_agents\irodori-ywk\rtx\` に
   `probe-log-cu130\`・`probe-log-cu126\`・`probe-log-cu130-uuidroute\`・`assemble-log\`・`test-log\` が在る。
3. **`SUMMARY.md` に「入れ替え前の測定完了」と書いてある**＝下の ⓐ〜ⓒ で書く。

> ### ⚠ ここは符号化の罠が 2 つ重なっている（**読んでから撃つ**）
>
> **罠 1＝台本の本体（`.ps1`）**。この台本の他の全部と同じく、**PowerShell は ASCII 限定**（`probe/README.md` §1 の表）。
> 日本語リテラルを入れた `.ps1` を **UTF-8（BOM 無し）**で保存して Windows PowerShell 5.1（ja-JP）で撃つと、
> 5.1 はその檔を **CP932 として読む**ので化けが閉じ引用符まで食い、**構文エラーで死ぬ**（設計席の機体で実射・逐語＝
> `The string is missing the terminator: '.` ／ `Missing closing ')' in expression.`）。
> 逃げ道として `-Encoding ASCII` で保存すると日本語が `?` に潰れ、**合否条件そのものが満たせない**。
> **したがって下の本体は日本語リテラルを 1 つも含まない**（要る 1 行だけを符号位置から組み立てる）。**これが既定。**
>
> **罠 2＝出力の `SUMMARY.md`**。こちらは日本語の 1 行を**含まなければならない**ので、
> **UTF-8 の BOM 付き**で書く（下の本体がそうしてある）。BOM が在れば 5.1 の `Get-Content` も
> pwsh 7 も、設計席の読み手も同じ字を読む。

**ⓐ 本体を作る**（`C:\ywk\write-summary.ps1`。**純 ASCII なので `-Encoding ASCII` で保存してよい**＝罠 1 を踏まない）

```
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = 'N:\temp_for_claudecode_agents\irodori-ywk\rtx'
# The one Japanese line the gate asks for, built from its code points so that this .ps1 stays
# pure ASCII and parses the same under Windows PowerShell 5.1 (ja-JP) and pwsh 7.
# U+5165 U+308C U+66FF U+3048 U+524D U+306E U+6E2C U+5B9A U+5B8C U+4E86
$doneLine = -join @(0x5165, 0x308C, 0x66FF, 0x3048, 0x524D, 0x306E, 0x6E2C, 0x5B9A, 0x5B8C, 0x4E86 | ForEach-Object { [char]$_ })
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# rtx seat B -- summary')
$lines.Add('')
$lines.Add($doneLine)
$lines.Add('')
$lines.Add('written : ' + (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz'))
$lines.Add('host : ' + $PSVersionTable.PSEdition + ' ' + $PSVersionTable.PSVersion.ToString())
$lines.Add('repo HEAD : ' + (git -C C:\ywk\repo rev-parse HEAD))
$lines.Add('driver before : ' + ((nvidia-smi --query-gpu=driver_version --format=csv,noheader) -join ' '))
$lines.Add('')
$lines.Add('## status.txt of every run copied to N:')
foreach ($f in (Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.status.txt' | Sort-Object FullName)) {
    $lines.Add('')
    $lines.Add('### ' + $f.FullName)
    foreach ($l in (Get-Content -LiteralPath $f.FullName)) { $lines.Add('    ' + $l) }
}
$lines.Add('')
$lines.Add('## everything under ' + $root)
foreach ($f in (Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName)) {
    $lines.Add('    ' + $f.FullName + '  ' + $f.Length + ' B')
}
$text = ($lines -join "`r`n") + "`r`n"
# BOM ON (the $true): SUMMARY.md does carry the Japanese line, and without the BOM a ja-JP
# Windows PowerShell 5.1 reader takes the file for CP932 and hands back mojibake.
$enc = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText((Join-Path $root 'SUMMARY.md'), $text, $enc)
Write-Host ('wrote ' + (Join-Path $root 'SUMMARY.md') + '  lines=' + $lines.Count)
```

**ⓑ 撃つ＝`pwsh` 7 で（既定）**（`$` を素で撃つ罠＝0-6 を踏まないよう `-File` で撃つ）

```
pwsh -NoProfile -ExecutionPolicy Bypass -File C:\ywk\write-summary.ps1
```

`pwsh` が無い機体なら 5.1 でよい（本体は純 ASCII なのでどちらでも同じに通る）。**どちらで撃ったかを報告に書く。**

```
powershell -NoProfile -ExecutionPolicy Bypass -File C:\ywk\write-summary.ps1
```

> **もし本体に日本語リテラルを直に書く形にどうしても変えるなら**（勧めない）、その `.ps1` は
> **BOM 付き UTF-8 でしか保存してはならない**。逐語＝
>
> ```
> powershell -NoProfile -ExecutionPolicy Bypass -Command "$p = 'C:\ywk\write-summary.ps1'; $text = [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8); [System.IO.File]::WriteAllText($p, $text, (New-Object System.Text.UTF8Encoding $true))"
> ```
>
> BOM が乗ったかの確認（先頭 3 バイトが `239 187 191`＝`EF BB BF`）＝
>
> ```
> powershell -NoProfile -ExecutionPolicy Bypass -Command "$b = [System.IO.File]::ReadAllBytes('C:\ywk\write-summary.ps1'); Write-Host ('first3 = ' + ($b[0..2] -join ' ')); Write-Host ('BOM = ' + (($b[0] -eq 239) -and ($b[1] -eq 187) -and ($b[2] -eq 191)))"
> ```
>
> **台本が配っている既定の本体は純 ASCII なので、この 2 本は要らない。**（既定で撃つならこの節は読み飛ばす。）

**ⓒ 書けたことを確かめる**（**目で見た日本語ではなく符号位置で確かめる**＝化けを見逃さない）

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p = 'N:\temp_for_claudecode_agents\irodori-ywk\rtx\SUMMARY.md'; $t = [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8); $want = -join @(0x5165, 0x308C, 0x66FF, 0x3048, 0x524D, 0x306E, 0x6E2C, 0x5B9A, 0x5B8C, 0x4E86 | ForEach-Object { [char]$_ }); $hit = @($t.Split([char]10) | Where-Object { $_.Trim() -eq $want }); Write-Host ('lines matching = ' + $hit.Count); Write-Host ('codepoints = ' + (($want.ToCharArray() | ForEach-Object { 'U+' + ([int]$_).ToString('X4') }) -join ' ')); Write-Host ('GATE = ' + ($hit.Count -ge 1))"
```

**合否**＝`N:\temp_for_claudecode_agents\irodori-ywk\rtx\SUMMARY.md` が在り、上の 1 本が **`GATE = True`** を出すこと
（＝**`入れ替え前の測定完了`** の 1 行が、`U+5165 U+308C U+66FF U+3048 U+524D U+306E U+6E2C U+5B9A U+5B8C U+4E86`
として在ること）。`GATE = False` なら**止まる**＝出た `lines matching` の数と、`SUMMARY.md` の先頭 5 行を逐語で報告する。

4. **この席（`irodori-TTS-for-yomiwakechan` の設計席）へ返信し、確認の返信を受け取っている。**
   返信に載せるもの＝`repo HEAD` の sha・`SUMMARY.md` の所在・各走行の `EXIT=`／`CHECKS=`・
   「ドライバ入れ替えに入ってよいか」の問い。**設計席から「入ってよい」の返信が来るまで待つ。**
   **⑴ の司令官の承諾と ⑵ のこの確認の返信、両方が揃って初めて 3-1 に進む。片方だけでは着手しない。**

---

### 3-1　この節が測る 1 つのこと（目的）

**U-14＝cu126（CUDA 12.6 ビルドの torch）が、CUDA 12.6 GA の最小要求 560.76 を下回る 2023 年秋のドライバで、
`import torch`・GPU 認識（`torch.cuda.is_available()`／`device_count`）・実合成 1 射まで通るか。**

**なぜ 2023 年秋か**＝`docs/acceptance.md` のドライバ行＝cu126（CUDA 12.x）の minor version compatibility は
「≥ 527.41 で限定機能・**GA 最小 560.76**」。**527.41 で足りるという読みは誤り**と既に書いてある。
R537／R545 は **527.41 と 560.76 の間**＝謳い方が変わる境目に在る。ここで実合成が通れば
`decisions.md` 4 の「cu126 は利用者が選べる」に**実射の裏付け**が付く。落ちれば「cu126＝古いドライバ向け」とは
**謳わない**根拠が実射で立つ。**どちらでも成果である。**

---

### 3-2　いまのドライバを記録する

```
nvidia-smi --query-gpu=name,driver_version,memory.total --format=csv
```

**記録すること**＝この行の逐語（戻すときの目標＝実測の事実は `decisions.md` 20 の **591.86**）。

---

### 3-3　復元ポイントを作る（**降格の前に必ず**）

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "Checkpoint-Computer -Description 'ywk before driver downgrade' -RestorePointType 'MODIFY_SETTINGS'; Write-Host ('OK=' + $?)"
```

> **`$LASTEXITCODE` ではなく `$?` で見る。**`Checkpoint-Computer` は**外部プログラムではなく cmdlet** なので
> `$LASTEXITCODE` を動かさない。起きたばかりの `powershell` では `$LASTEXITCODE` は**未設定**で、
> `'EXIT=' + $LASTEXITCODE` は必ず `EXIT=` と空を出す（設計席の機体で実射・逐語＝`EXIT=` ／ `isNull=True`）。
> **合否の材料にならない印字を合否の位置に置かない。**`$?` は直前の文が成功したかの真偽なので、cmdlet に効く。
> **ほんとうの合否は下の `Get-ComputerRestorePoint`** で見る（この `OK=` は目安）。

**この 1 本は管理者の PowerShell で撃つ**（`Checkpoint-Computer` は昇格が要る）。昇格しているかは §3-5 の頭の
1 本（`IsInRole`）で確かめられる。昇格した窓が無いなら司令官に頼む。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ComputerRestorePoint | Select-Object -Last 3 | Format-List SequenceNumber,Description,CreationTime"
```

**合否**＝いま作った説明文の点が並ぶこと。

> **作れなかった場合**＝システムの保護が切れている（既定で切れている機体がある）。そのときは
> **「復元ポイントは作れなかった。理由は〈出たメッセージの逐語〉」と報告してから進む**か、
> 司令官に「システムの保護を入れてよいか」を訊く。**黙って飛ばさない。**
> なお復元ポイントは**ドライバの巻き戻しの保険**であって、3-11 の復旧経路の代わりではない。

---

### 3-4　投入版と URL（**この席が HEAD で存在を確かめた**）

**入手元は NVIDIA 公式のダウンロードサーバ `us.download.nvidia.com` のアーカイブ直リンクだけ。**
第三者の配布サイトからは拾わない（`decisions.md` 22＝出所不明のドライバを入れない）。

設計席が 2026-09-05 に **HEAD だけ**（本体はダウンロードしていない）で確かめた実測＝

| 版 | URL | HEAD の結果 | Content-Length | Last-Modified |
|---|---|---|---|---|
| **537.58**（**第一候補**） | `https://us.download.nvidia.com/Windows/537.58/537.58-desktop-win10-win11-64bit-international-dch-whql.exe` | `200 OK` | `675738080` | `Tue, 10 Oct 2023 11:20:15 GMT` |
| 545.84（代案） | `https://us.download.nvidia.com/Windows/545.84/545.84-desktop-win10-win11-64bit-international-dch-whql.exe` | `200 OK` | `701525896` | `Tue, 17 Oct 2023 10:58:57 GMT` |
| 591.86（戻し用・3-10） | `https://us.download.nvidia.com/Windows/591.86/591.86-desktop-win10-win11-64bit-international-dch-whql.exe` | `200 OK` | `918362520` | `Thu, 22 Jan 2026 02:02:05 GMT` |

**3 本とも実在する**（`Content-Type: application/octet-stream`）。**537.58 を第一候補**にする（2023 年 10 月・GRD・DCH・international）。

**sha256 は NVIDIA が公開していない。**ダウンロード頁にもこの直リンクにもハッシュは無い。
したがって**照合先は無い**＝**取得後に自分で `Get-FileHash` を撃ち、出た値を `SUMMARY.md` と報告に記す**。
これは「検証済み」ではなく「**当方が測った値を記録しただけ**」である。**そう書く**（`decisions.md` 22＝推測で断定しない）。

**遠隔席が撃つ HEAD の確認**（自分の回線でも生きているかを見る。ダウンロードはしない）＝

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$u = 'https://us.download.nvidia.com/Windows/537.58/537.58-desktop-win10-win11-64bit-international-dch-whql.exe'; $r = Invoke-WebRequest -Uri $u -Method Head -UseBasicParsing -TimeoutSec 60; Write-Host ('STATUS = ' + [int]$r.StatusCode); Write-Host ('LENGTH = ' + $r.Headers['Content-Length']); Write-Host ('LAST-MODIFIED = ' + $r.Headers['Last-Modified'])"
```

**判断規則**＝

| 見たもの | やること |
|---|---|
| `STATUS = 200` かつ `LENGTH = 675738080` | **537.58 で進む**（3-5 へ） |
| `STATUS = 200` だが `LENGTH` が違う | **止まって報告**（同じ URL の中身が変わった＝事実として重い）。推測で取らない |
| 404／接続できない | 545.84 の URL で同じ HEAD を撃つ。それも駄目なら **B-7 を打ち切って報告**（別の配布サイトから拾ってこない） |

---

### 3-5　取得して sha256 を測る（**CLI だけ・ブラウザは使わない**）

> ### ⚠ 3-5 と 3-6 の前に必ず＝**昇格しているかを確かめる**
>
> **NVIDIA のインストーラ（3-6）は管理者権限を要求する。**非昇格の席が撃つと、UAC の同意窓が出て止まるか、
> `740`（`ERROR_ELEVATION_REQUIRED`）で落ちる。**遠隔席は GUI クリックができない**（`decisions.md` 55）ので
> **同意窓は自分では押せない**。取得（3-5）は非昇格でもできるが、席がどちらに居るかを**先に**知っておく。
>
> ```
> powershell -NoProfile -ExecutionPolicy Bypass -Command "$id = [Security.Principal.WindowsIdentity]::GetCurrent(); $pr = New-Object Security.Principal.WindowsPrincipal($id); Write-Host ('user     = ' + $id.Name); Write-Host ('ELEVATED = ' + $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))"
> ```
>
> **記録すること**＝`ELEVATED =` の値。**この値を 3-6 と 3-7 の報告に必ず添える**（3-7 の判断表がこれを使う）。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "New-Item -ItemType Directory -Force -Path C:\ywk\driver | Out-Null; $u = 'https://us.download.nvidia.com/Windows/537.58/537.58-desktop-win10-win11-64bit-international-dch-whql.exe'; $o = 'C:\ywk\driver\537.58-desktop-win10-win11-64bit-international-dch-whql.exe'; $ProgressPreference = 'SilentlyContinue'; Invoke-WebRequest -Uri $u -OutFile $o -UseBasicParsing -TimeoutSec 3600; Get-Item $o | Select-Object FullName,Length; Write-Host ('sha256 = ' + (Get-FileHash -LiteralPath $o -Algorithm SHA256).Hash.ToLowerInvariant())"
```

**合否**＝`Length` が 3-4 の `Content-Length`（537.58 なら `675738080`）と**一致**すること。違ったら**実行しない**（取り直す）。
**記録すること**＝URL・檔名・バイト数・**sha256**。この sha256 を `SUMMARY.md` に足す。
**書き方**＝「NVIDIA は sha256 を公開していないので照合先が無い。当方が取得後に測った値である」と明記する。

> **`$ProgressPreference = 'SilentlyContinue'` は飾りではない。**`Invoke-WebRequest` の進捗バーは
> 644 MB の取得を体感で数倍遅くする（Windows PowerShell 5.1 で顕著）。

---

### 3-6　サイレント導入（**GUI を触らない**）

遠隔席は**ブラウザ操作も GUI クリックもできない**（`decisions.md` 55＝道具は PowerShell と gh だけ）。
NVIDIA の自己展開 exe は**無人導入の引数**を受ける。**ただし管理者権限が要る**（3-5 の頭の `ELEVATED =` を見る）。

**⒜ `ELEVATED = True` のとき**＝そのまま撃つ。

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$exe = 'C:\ywk\driver\537.58-desktop-win10-win11-64bit-international-dch-whql.exe'; $p = Start-Process -FilePath $exe -ArgumentList @('-s','-noreboot','-clean') -Wait -PassThru; Write-Host ('EXIT=' + $p.ExitCode)"
```

**⒝ `ELEVATED = False` のとき**＝`-Verb RunAs` で昇格して撃つ。**UAC の同意窓が出る。**

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$exe = 'C:\ywk\driver\537.58-desktop-win10-win11-64bit-international-dch-whql.exe'; $p = Start-Process -FilePath $exe -ArgumentList @('-s','-noreboot','-clean') -Verb RunAs -Wait -PassThru; Write-Host ('EXIT=' + $p.ExitCode)"
```

> **⒝ を撃つ前に読む＝この 1 本は止まり得る。**`-Verb RunAs` は UAC の同意画面を出す。
> **席はそれを押せない**（GUI 不可＝`decisions.md` 55）。したがって ⒝ には 3 つの終わり方がある。
>
> | 起きたこと | 意味 | やること |
> |---|---|---|
> | 何も返らずぶら下がる | **UAC の同意窓が出て、誰も押していない** | **待たずに止まる。**「**UAC 待ち**＝3-6 の ⒝ を撃ったところで同意画面が出ている。押してほしい」と**そのまま返信する**（設計席が Chrome Remote Desktop から押す） |
> | `This operation requires an elevation`／`The operation was canceled by the user`（1223） | 同意窓が閉じられた・出せなかった | 同上＝**「UAC 待ち」で止まって返信する。**もう一度撃ち直さない |
> | `EXIT=` に数字が出て返る | 昇格して走り切った | **3-7 へ**（数字の意味は 3-7 の `nvidia-smi` で決める） |
>
> **記録すること**＝⒜ と ⒝ のどちらを撃ったか・`ELEVATED =` の値・出た終了コードかメッセージの逐語。

| 引数 | 意味 |
|---|---|
| `-s` | サイレント（無人）。窓も対話も出さない |
| `-noreboot` | インストーラに再起動させない（**再起動は司令官がやる**＝3-7） |
| `-clean` | **クリアインストール**＝既存のドライバ設定を消してから入れる。NVIDIA コントロールパネルの設定・プロファイルが**消える**。この機体は「壊してよい」機体なので許される（`decisions.md` 20） |

> **DDU（Display Driver Uninstaller）は使わない**（§5 の 8）。第三者ツールで、入手元と版が台帳に無い。
> `-clean` は**NVIDIA 標準インストーラ自身の**クリーンインストールであって DDU ではない。
>
> **この 3 引数を、この席（設計席）は実射していない**（外部へ接続せず、RTX 機も持たない）。
> NVIDIA の自己展開インストーラに古くからある引数として書いている。**遠隔席が実際に撃って出た終了コードと
> 挙動を、そのまま逐語で記録する**。予想と違ったら、違ったことを書く。
>
> **終了コードの意味を断定しない。**出た数字をそのまま記録し、**合否は 3-7 の `nvidia-smi` で決める**
> （インストーラの終了コードではなく、実際に載った版で決める）。

---

### 3-7　入れ替わったか確かめる／**再起動が要るとき**

```
nvidia-smi --query-gpu=name,driver_version --format=csv
```

**判断規則**＝

| 見たもの | 意味 | やること |
|---|---|---|
| `driver_version` が **537.58**（または入れた版） | 降格できた | **3-8 へ** |
| **3-6 が昇格で落ちた**＝`EXIT=740`／`EXIT=1223`、または `-Verb RunAs` がぶら下がった、または 3-5 の `ELEVATED = False` のまま ⒜ を撃った | **昇格の失敗であって、再起動不足ではない。**導入は**始まってすらいない**（`740 = ERROR_ELEVATION_REQUIRED`・`1223 = ERROR_CANCELLED`＝UAC を押していない） | **再起動しない。**「**UAC 待ち**」として止まって返信する（3-6 の ⒝ の表）。**この行を再起動の枝に落とさない** |
| `driver_version` が **591.86 のまま**、または `nvidia-smi` 自体が失敗する。**かつ 3-6 が昇格して走り切っている**（`ELEVATED = True` か ⒝ が数字を返した） | **再起動が要る**（`-noreboot` で入れたので保留されている） | **ここで止まる。**下の報告を出して待つ |
| 3-6 の `EXIT=` が 0 以外（上の 740／1223 を除く） | 導入が完走していないかもしれない | 同上＝止まって数字ごと報告する |

> **昇格失敗を再起動不足と読み違えない。**この 2 つは症状が同じ（`driver_version` が 591.86 のまま）で、
> **手当てが正反対**である＝前者は**押してもらう**、後者は**再起動する**。**先に `ELEVATED =` と 3-6 の逐語を見る。**
> 見分けがつかないなら**再起動しない**（再起動は取り返しがつかない側）。

**再起動が要ると判ったら、まず止まって、次の 2 つのどちらに当たるかを見る**（`decisions.md` 66・71）。

| いま在るもの | やってよいこと | 根拠 |
|---|---|---|
| **司令官がこの遠隔席の会話に「`RunOnce`＋`claude -c` の自動再起動可」と書いている**（3-0 の条件 ⑴ の先書き＝`decisions.md` 72 の文面） | **経路 A＝仕込んでから自分で再起動してよい。**下の 経路 A の ⑴〜⑷ を**書いてあるとおりに**撃つ。復帰後は**同じ会話の続き**＝3-7 の `nvidia-smi` から再開し、通っていれば 3-8 へ。**自動ログオンでなければログオンは司令官が行う** | 66・72 |
| **書いていない**（本席＝設計席の指示しか無い） | **経路 B＝自動再起動も `RunOnce` の自己登録もしない。**下の報告文で**止まる** | **71**（席の許可は本席からは渡せない） |

> **`decisions.md` 71 を先に読む。**経路 A に入ってよいのは、**この遠隔席の会話の中で利用者（司令官）が明示したとき
> だけ**である。**設計席の申し送りは承諾にならない。**（`decisions.md` 72 で先書き済み＝その文面を自分の目で
> 確かめてから撃つ。）

#### 経路 A（**利用者が明示したときだけ**）　`RunOnce`＋`claude -c` を仕込んで自分で再起動する

**⑴ 仕込みの本体を作る**（`C:\ywk\arm-resume.ps1`。**純 ASCII**なので `-Encoding ASCII` で保存してよい）

```
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 1. Walk UP the process chain, starting at this powershell's PARENT, until a process that is
#    really the claude seat is found.
#
#    Two traps this avoids, both hit for real while the script was being tested (2026-09-05):
#      - "the command line contains claude" also matches the PATH OF THIS SCRIPT when it sits
#        under a directory named claude (Claude Code's own scratch directories do). The
#        powershell running this script then looks like the seat, RunOnce brings back a
#        one-shot shell instead of the seat, and the machine's only route back is gone.
#        So: this process is never a candidate, and a shell is never a candidate.
#      - The seat is claude.exe on some installs and node.exe running claude-code on others.
#        Both are accepted. Nothing else is.
$shells = @('powershell.exe', 'pwsh.exe', 'cmd.exe', 'conhost.exe', 'WindowsTerminal.exe', 'OpenConsole.exe', 'bash.exe', 'sh.exe', 'zsh.exe', 'wsl.exe')
$self = Get-CimInstance -ClassName Win32_Process -Filter ('ProcessId=' + $PID) -ErrorAction SilentlyContinue
if ($null -eq $self) {
    Write-Host 'SEAT=NOT-FOUND (this process is not readable through Win32_Process)'
    Write-Host 'NOTHING was registered and NOTHING will be restarted. Stop.'
    exit 2
}
$id = [int]$self.ParentProcessId
$seat = $null
$why = ''
for ($i = 0; $i -lt 12; $i++) {
    if ($id -le 0) { break }
    $p = Get-CimInstance -ClassName Win32_Process -Filter ('ProcessId=' + $id) -ErrorAction SilentlyContinue
    if ($null -eq $p) { break }
    $cmd = [string]$p.CommandLine
    Write-Host ('  [' + $i + '] pid=' + $p.ProcessId + '  name=' + $p.Name + '  ppid=' + $p.ParentProcessId)
    Write-Host ('        cmd=' + $cmd)
    if ($shells -contains $p.Name) {
        Write-Host '        -> a shell is never the seat (-c would mean nothing to it); keep walking'
    } elseif ($p.Name -match '^claude(\.exe)?$') {
        $seat = $p; $why = 'the process name itself is claude'; break
    } elseif (($p.Name -match '^(node|bun|deno)\.exe$') -and ($cmd -match 'claude')) {
        $seat = $p; $why = 'a node-family process whose command line runs claude'; break
    } else {
        Write-Host '        -> not claude and not a shell; keep walking'
    }
    $id = [int]$p.ParentProcessId
}
if ($null -eq $seat) {
    Write-Host 'SEAT=NOT-FOUND'
    Write-Host 'NOTHING was registered and NOTHING will be restarted. Report the chain above and stop.'
    exit 2
}
$cmdline = [string]$seat.CommandLine
if ([string]::IsNullOrEmpty($cmdline)) {
    Write-Host 'SEAT=NO-COMMANDLINE'
    Write-Host 'The seat was found but Win32_Process will not hand over its command line.'
    Write-Host 'NOTHING was registered and NOTHING will be restarted. Report this and stop.'
    exit 2
}

# 2. What to run after the reboot: the seat's own command line, plus -c (continue), started in
#    the same working directory. -NoProfile and every other switch survive because the command
#    line is taken verbatim.
$cwd = (Get-Location).Path
if ($cmdline -match '(\s-c|\s--continue)\s*$') {
    Write-Host 'NOTE: the command line already ends with -c / --continue; it is taken as it stands.'
    $value = 'cmd /c cd /d "' + $cwd + '" && ' + $cmdline
} else {
    $value = 'cmd /c cd /d "' + $cwd + '" && ' + $cmdline + ' -c'
}
Write-Host ('SEAT pid = ' + $seat.ProcessId + '  name = ' + $seat.Name)
Write-Host ('MATCHED  = ' + $why)
Write-Host ('CWD      = ' + $cwd)
Write-Host ('RUNONCE  = ' + $value)

# 3. Register it. RunOnce fires once, at the next interactive logon, and deletes itself.
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce'
if (-not (Test-Path -LiteralPath $key)) { $null = New-Item -Path $key -Force }
Set-ItemProperty -Path $key -Name 'ywk-resume' -Value $value
$back = (Get-ItemProperty -Path $key -Name 'ywk-resume').'ywk-resume'
Write-Host ('READBACK = ' + $back)
Write-Host ('MATCH    = ' + ($back -eq $value))

# 4. Leave a note for the seat that wakes up after the reboot. ASCII only, for the same reason
#    write-summary.ps1 is ASCII only (3-0): a ja-JP PowerShell 5.1 mis-reads a BOM-less UTF-8
#    .ps1 and dies before it writes anything.
# The parentheses around the two concatenated elements are NOT optional: in a PowerShell array
# literal the comma binds TIGHTER than +, so 'written  : ' + $x, 'why ...' parses as
# 'written  : ' + ($x, 'why ...') and the note comes out with the label on its own line.
# Measured on the design seat's machine, 2026-09-05.
$note = @(
    'ywk progress note, written by arm-resume.ps1 before a deliberate reboot.',
    ('written  : ' + (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz')),
    'why      : NVIDIA driver downgrade (runbook 3-6) installed with -noreboot; the new driver',
    '           is not live until this machine restarts.',
    'state    : BEFORE THE REBOOT. Everything up to and including 3-6 is done.',
    'resume at: probe/rtx-remote-runbook.md 3-7 -- re-run',
    '             nvidia-smi --query-gpu=name,driver_version --format=csv',
    '           and judge it by the 3-7 table. If the version changed, go on to 3-8',
    '           (post-downgrade cu126 measurements). If it did not, STOP and report.',
    'safe     : every pre-downgrade measurement is already on N: (3-0 condition 2).',
    'runonce  : HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce\ywk-resume',
    ('cwd      : ' + $cwd)
) -join "`r`n"
[System.IO.File]::WriteAllText('C:\ywk\progress.md', $note + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'wrote C:\ywk\progress.md'
Write-Host ''
Write-Host 'ARMED. Nothing has been restarted. Fire the reboot yourself, as a separate command.'
```

**⑵ 仕込む**（**まだ再起動しない**）

```
powershell -NoProfile -ExecutionPolicy Bypass -File C:\ywk\arm-resume.ps1
```

**合否**＝`SEAT pid = …`・`MATCHED = …`・`MATCH = True`・`wrote C:\ywk\progress.md` が出ること。

| 出たもの | やること |
|---|---|
| `MATCH = True` | **⑶ へ** |
| `SEAT=NOT-FOUND`（終了コード 2） | **止まる。**印字された親子の連鎖を**逐語で**報告し、経路 B の報告文で待つ。**当てずっぽうで別のプロセスを登録しない** |
| `SEAT=NO-COMMANDLINE`（終了コード 2） | 同上＝**止まる**。命令行が読めない席は復帰させられない |
| `MATCH = False`／例外 | **止まる。**登録が読み戻せない＝復帰しない。**再起動しない** |

> **`RUNONCE = …` の行を、目で 1 度読む。**この 1 行が再起動後に走る全てである。
> **`claude` を起こす行**でなければならない。`powershell`・`pwsh`・`cmd` で始まる行だったら（＝席ではなく殻を
> 拾っている）**止まる**＝そのまま再起動すると、機体は戻ってきても**席が居ない**。
> **設計席の機体での実測（2026-09-05）**＝素朴に「命令行に `claude` を含むもの」を探す形では、
> **この台本の檔がたまたま `claude` という名の親フォルダの下に在るだけで、自分を走らせている `powershell` を
> 席と見誤った**。上の本体はそのために**自プロセスと殻を候補から外して**ある。それでも**目で読む。**

**⑶ 仕込みと成果を、再起動の前にもう一度目で確かめる**

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ItemProperty -Path HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce | Format-List *; Get-Content -LiteralPath C:\ywk\progress.md; Get-ChildItem -LiteralPath N:\temp_for_claudecode_agents\irodori-ywk\rtx -Recurse -File | Measure-Object | Select-Object Count"
```

**合否**＝`ywk-resume` の行が在り、`progress.md` が読め、N: の檔数が 0 でないこと。**1 つでも欠けたら再起動しない。**

**⑷ 再起動する**（**ここから先は取り返しがつかない。⑵⑶ の合否を外していたら撃たない**）

```
shutdown /r /t 15 /c "ywk driver downgrade"
```

> **15 秒の猶予**＝撃った直後に気が変わったら `shutdown /a` で中止できる。
> **報告に必ず書くこと**＝再起動を撃ったこと・撃った時刻・`RUNONCE` の逐語。
> **復帰したら**＝`C:\ywk\progress.md` を読み、3-7 の `nvidia-smi` から再開する。
> **RunOnce は 1 回で自分を消す**ので、2 度目の再起動が要るなら ⑴〜⑷ をもう一度撃つ。

#### 経路 B（**利用者が明示していない**）

**止まるときに出す報告**（逐語でこう書く）＝

> **再起動が要る。**`nvidia-smi` の `driver_version` は〈出た値〉のままで、3-6 の終了コードは〈出た値〉だった。
> **再起動すると Remote Control の接続が切れ、この席も落ちる。**したがって**司令官に、⑴ 機体の再起動と
> ⑵ 再起動後の席の起こし直しを、お願いする。**再起動後の再開点は **3-7 の `nvidia-smi` から**。
> 降格前の全測定は既に N: に書き戻してある（3-0 の条件 ⑵）ので、ここで落ちても失うものは無い。

**どちらの道でも、再起動の前後で台本は独立して再開できる**（`decisions.md` 71）＝
**降格前の全測定は N: に在り、進捗は `C:\ywk\` に残る**。再起動を挟んだかどうかを報告に書く。

> **再起動の前に必ず**＝§2 の結果と `SUMMARY.md` が N: に在ることをもう一度確かめる（3-0 の条件 ⑵）。
> C: の `C:\ywk\` は再起動で消えないが、**この節はここから機体を壊し得る**ので、成果は先に N: へ逃がしておく。

---

### 3-8　降格後に測るもの＝**cu126 だけ**（`decisions.md` 56）

**⑴ `import torch` の全文**

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\build\out\runtime-cu126\python.exe -c \"import torch; print(torch.__version__, torch.version.cuda)\" 2>&1 | ForEach-Object { Write-Host $_ }; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**⑵ `is_available()`／`version.cuda`／`device_count()`**

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\build\out\runtime-cu126\python.exe -c \"import torch; print('version =', torch.__version__); print('version.cuda =', torch.version.cuda); print('is_available =', torch.cuda.is_available()); print('device_count =', torch.cuda.device_count()); print('name =', (torch.cuda.get_device_name(0) if torch.cuda.is_available() else None))\" 2>&1 | ForEach-Object { Write-Host $_ }; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**⑶ `nvidia-smi` の全文**

```
nvidia-smi
```

**記録すること**＝⑴⑵⑶ の**標準出力・標準エラーの全文を逐語で**（要約しない・訳さない）。
**落ちたなら、落ちた逐語がそのまま成果物**である。

**⑷ 実合成 1 射**

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\probe\cuda-bench.ps1 -Variant cu126 -Device cuda:0 -Precision bf16 -HfHome C:\ywk\models -Quick -Label cu126-olddriver -ResultRoot N:\temp_for_claudecode_agents\irodori-ywk\rtx; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

**合否**＝`EXIT=0` かつ `cuda-bench-cu126-olddriver.status.txt` の `CHECKS=OK`。
**記録すること**＝`status.txt` 全文と、`cuda-bench-cu126-olddriver.json` の `run_cold`・`conditions[0]`・
`status_after_ready.device`。

> **檔名に注意**＝`-Label cu126-olddriver` を付けてあるので、この走行の出力は
> `cuda-bench-cu126-olddriver.json` / `.status.txt` / `.run.json`・`wav-cu126-olddriver\`・
> `server-cu126-olddriver.{out,err}.log`、`-ResultRoot` の写し先は `probe-log-cu126-olddriver\` になる。
> **3-0 の条件 ⑵ で N: に書き戻した B-6 の本測定（`cuda-bench-cu126.json`）は上書きされない。**
> ラベルを外して撃つと本測定が消えるので**外さない**。
>
> `-Quick` は「短文・40 steps・参照なし・warm 1 射」だけを撃つ。**`-Quick` の結果を速度行の実測として使わない**
> （JSON の `run.quick_note` にもそう書いてある）。ここで見るのは**可否**であって速さではない。

**判定**＝

| 結果 | 意味 | 書き方 |
|---|---|---|
| ⑴⑵⑷ すべて通る | cu126 は 537.58（560.76 未満）で**実際に動く** | `docs/acceptance.md` のドライバ行を「**537.58 で実合成を確認**」に強められる（`decisions.md` 4 の裏付け）。**`docs/acceptance.md` はこの席では書き換えない＝設計席に申し送る** |
| ⑴ で落ちる | wheel の CUDA ランタイムがドライバに届いていない | 逐語を報告。「cu126＝古いドライバ向け」と**謳わない**を維持 |
| ⑴ は通るが ⑵ が `is_available = False` | ドライバは見えているが CUDA 文脈が作れない | 同上。⑶ の `nvidia-smi` 全文と併せて逐語で |
| ⑴⑵ 通って ⑷ が落ちる | 起動はするが合成の途中で落ちる | `build\out\probe-log\server-cu126-olddriver.err.log` の末尾 50 行を逐語で |

---

### 3-9　cu130 の落ち方を **1 回だけ**記録する（`decisions.md` 56）

cu130（CUDA 13.0 ビルド）は 2023 年秋のドライバでは**落ちる想定**である。**これは失敗ではなく記録**であって、
**測るのではなく、落ち方を 1 回だけ事実として残す**（56＝「降格後に測るのは cu126 のみ・cu130 の落ち方は事実として 1 回記録」）。

**3-8 の ⑴⑵⑶ と同じ 3 点セットを、cu126 を cu130 に替えて 1 回だけ撃つ。**

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\build\out\runtime-cu130\python.exe -c \"import torch; print(torch.__version__, torch.version.cuda)\" 2>&1 | ForEach-Object { Write-Host $_ }; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& C:\ywk\repo\build\out\runtime-cu130\python.exe -c \"import torch; print('version =', torch.__version__); print('version.cuda =', torch.version.cuda); print('is_available =', torch.cuda.is_available()); print('device_count =', torch.cuda.device_count()); print('name =', (torch.cuda.get_device_name(0) if torch.cuda.is_available() else None))\" 2>&1 | ForEach-Object { Write-Host $_ }; Write-Host ('EXIT=' + $LASTEXITCODE)"
```

```
nvidia-smi
```

**記録すること**＝3 本の**全文を逐語で**。**cu130 のベンチは撃たない**（`cuda-bench.ps1` を cu130 で走らせない）。
**落ちても「失敗」と書かない。「537.58 では cu130 の `import torch` は〈逐語〉で落ちた」という事実として書く。**

---

### 3-10　591.86 に戻す＝**不要**（`decisions.md` 70）

**戻さない。降格したままでよい＝恒久降格**（司令官・2026-09-05・`decisions.md` 70）。
**この節は既定では撃たない。**「戻していない」ことを報告に書けばそれで足りる。

司令官が後から「戻せ」と言った場合にだけ、3-4 の表の **591.86** の URL に対して
**3-4（HEAD）→ 3-5（取得と sha256）→ 3-6（`-s -noreboot -clean`）→ 3-7（`nvidia-smi` と再起動）** を**同じ順で**撃つ。
その場合も**承諾はそのセッションで取る**（`decisions.md` 56・71）。

---

### 3-11　復旧経路（**画面が出ない／起動しないとき**）＝**司令官の手作業**

**席はこの節を実行できない。**画面が出ない機体には Remote Control で入れない（席はもう居ない）。
これは**司令官が手で行う手順**として書いてある。**降格を撃つ前に、司令官がこの節を読んでいることを確かめる。**

**症状**＝降格の再起動のあと、⑴ 画面に何も出ない ⑵ 解像度が極端に低いまま進まない ⑶ ログイン画面で固まる ⑷ 起動しない。

**手順 ⑴　セーフモードで起動する**（どちらか片方）

- **A：まだ Windows が起動できる場合**＝`Win+R` →`msconfig` →「ブート」タブ →「セーフブート」に
  チェック →「最小」→ OK →再起動。**復旧が終わったら同じ画面でチェックを外す**（外さないと毎回セーフモードで起動する）。
- **B：Windows が起動しない場合**＝ログイン画面（または電源メニュー）で **Shift キーを押しながら「再起動」**。
  →「トラブルシューティング」→「詳細オプション」→「スタートアップ設定」→「再起動」→
  一覧が出たら **`4`（または F4）＝セーフモードを有効にする**。
  - Shift+再起動に辿り着けないほど壊れている場合＝**電源ボタンで 3 回連続して起動を中断する**と
    Windows が自動修復に入り、同じ「トラブルシューティング」画面が出る。

**手順 ⑵　セーフモードでドライバを外す**（どちらか片方。上から順に試す）

- **A：ドライバをアンインストールする**
  1. `Win+X` →「デバイス マネージャー」（または `Win+R` →`devmgmt.msc`）。
  2. **「ディスプレイ アダプター」**を展開。
  3. **「NVIDIA GeForce RTX 3090」を右クリック →「デバイスのアンインストール」**。
  4. **「このデバイスのドライバーを削除しようとしました」／「ドライバー ソフトウェアを削除する」の
     チェックを入れる**（出たときだけ。出なければそのまま）。
  5. 「アンインストール」→ 再起動。
  6. 起動後、Windows は**標準の表示アダプター**で上がる（画面は出るが低解像度）。
     ここまで来たら **3-10 で 591.86 を入れ直す**（または司令官の判断で別の版）。
- **B：標準 VGA に差し替える**（A で直らない・A のあとも黒いまま）
  1. デバイス マネージャー →「ディスプレイ アダプター」→ 該当を右クリック →**「ドライバーの更新」**。
  2. **「コンピューターを参照してドライバーを検索」**。
  3. **「コンピューター上の利用可能なドライバーの一覧から選択します」**。
  4. 一覧から **「Microsoft 基本ディスプレイ アダプター」**（英語表示なら *Microsoft Basic Display Adapter*）を選ぶ。
  5. 「次へ」→ 入れ替わったら再起動。**必ず画面が出る状態**になる。
     ここから **3-10 で 591.86 を入れ直す**。

**手順 ⑶　それでも駄目なら復元ポイント**

3-3 で作った復元ポイントに戻す＝セーフモードまたは「トラブルシューティング」→「詳細オプション」→
**「システムの復元」**→ 説明が `ywk before driver downgrade` の点を選ぶ。

**手順 ⑷　セーフブートの解除を忘れない**

⑴ の A（`msconfig`）でセーフブートに入れた場合、**`msconfig` の「セーフブート」のチェックを外して再起動する**まで
毎回セーフモードで上がる。

> **この節で使う道具は Windows 標準だけ**（`msconfig`・デバイス マネージャー・システムの復元）。
> **DDU は使わない**（§5 の 8）。**D:・E:・F: には一切触らない**（`decisions.md` 20）＝
> 復旧作業でもこの停止域は動かない。

---

## 4. 結果の写し先（まとめ）

**写し先は 1 系統だけ**＝ベンチの結果は `cuda-bench.ps1 -ResultRoot` が走行ごとに自分で写す。
**同じものを別のコマンドで写し直さない**（同じ数値が 2 か所に別名で並ぶと、どちらがその走行の写しか分からなくなる）。

| 何 | どこへ | 誰が写すか |
|---|---|---|
| ベンチの JSON・`.status.txt`・`.run.json`・ログ・wav（最大 6 本／走行） | `N:\…\irodori-ywk\rtx\probe-log-<slug>\` | `cuda-bench.ps1 -ResultRoot`（自動・**この走行が書いた檔だけ**） |
| 組み立てログ | `N:\…\irodori-ywk\rtx\assemble-log\` | B-9 の robocopy |
| 契約テストのログ | `N:\…\irodori-ywk\rtx\test-log\` | B-9 の robocopy |
| **`SUMMARY.md`**（`入れ替え前の測定完了` の 1 行＋全 `status.txt` の逐語＋檔一覧） | `N:\…\irodori-ywk\rtx\SUMMARY.md` | **3-0 の `write-summary.ps1`**（§3 の着手条件） |
| 逐語の報告（B-3 の import エラー・B-4b の 7 行・§3 の 3 点セット×2（cu126・cu130）・停止域の証明） | 遠隔席の返信そのもの（設計席が `docs/acceptance.md`・`docs/cuda.md`・`decisions.md` に写す） | 遠隔席 |

**`<slug>` の決まり**＝`-Label` を渡さなければ変種名（`probe-log-cu130`・`probe-log-cu126`）。
渡せば `変種-ラベル`（`probe-log-cu130-uuidroute`・`probe-log-cu126-olddriver`・`probe-log-cu130-w1-nogpu`）。
**ラベルを付けた走行が、付けない本測定を上書きすることは無い。**

**N: に置いてよいのはテキストの生データと wav。**モデル・wheel・ドライバの exe を N: に置き直さない（既に `rtx-handoff/` にあるものはそのまま）。

---

## 5. 停止域（この機体で絶対にやらないこと）

1. **D:・E:・F: に触らない**（`decisions.md` 20）。読みも書きもしない（**列挙もしない**＝B-9 の注記）。
2. **N: の上で走らせない**（`decisions.md` 21・`research/lab/notes/21`）。N: は写す元／写す先であって作業場ではない。
3. **外部へ push しない**（GitHub・Hugging Face・PyPI）。HF から**取る**のは可（B-2）。
4. **上流 submodule を書き換えない**＝`upstream/Irodori-TTS`・`upstream/Irodori-TTS-Server` の `git status --porcelain` は最後まで無出力。`__pycache__` を書かせない（台本が `PYTHONDONTWRITEBYTECODE=1` を焼いている）。
5. **`C:\IrodoriTTS\`・`C:\irodori-TTS-server\` があっても触らない**（`decisions.md` 22）。
6. **ライセンスの結論を推測で断定しない。**
7. **8088 はこの機体に無い**（`docs/design/ben-b-rtx-remote.md` §4）。立てない。
8. **DDU を使わない**（§3-6 の理由）。使うのは NVIDIA 標準インストーラの `-clean` だけ。
9. **出所不明のドライバを入れない**＝`us.download.nvidia.com` の公式アーカイブ直リンク（§3-4 の表）だけ。
10. **本席（設計席）の指示だけで自動再起動や `RunOnce` の自己登録をしない**（`decisions.md` 71）。
    **司令官がこの遠隔席の会話に明示したときだけ**、`decisions.md` 66 の仕込み（`claude -c` で復帰）をして
    自分で再起動してよい。書いていなければ**止まって司令官に頼む**（§3-7）。
11. **§3 は関門⑴（設計席の確認返信）と関門⑵（司令官の承諾）が両方揃うまで着手しない**
    （`decisions.md` 56・70・72・§3-0）。**関門⑵は 72 で先に満たされている**が、
    **その文面が遠隔席の会話に在ることを自分の目で確かめる**（本席の申し送りを承諾の代わりにしない＝71）。
12. **降格後に cu130 のベンチを撃たない**（`decisions.md` 56＝測るのは cu126 のみ。cu130 は落ち方を 1 回記録するだけ＝§3-9）。
13. **`docs/acceptance.md` の数値をこの席で書き換えない**（便 C の担当檔）。強められる／弱められる根拠が出たら**設計席に申し送る**。

**`decisions.md` 22 から、この機体でも同じく効くもの**（上と重ならない分）＝

14. **`git commit` しない・`git push` しない・タグを打たない。**この機体の `C:\ywk\repo` は**使い捨ての clone** であって、
    正典の木ではない。**成果は N: の `rtx\` に置いた檔と、遠隔席の返信の逐語だけ**（`decisions.md` 21・22）。
    正典（`decisions.md`・`docs/`）に書き写すのは設計席の仕事。
15. **第三者バイナリをコミットしない**（`decisions.md` 22）。wheel・exe・dll・モデル・wav（一次）を
    `C:\ywk\repo` の中に置かない。置いてよいのは `build\out\` と `.venv-dev\` の下だけ＝どちらも `.gitignore` の中で、
    `build\check-tree.ps1` が「第三者バイナリ 0 件」を数える対象から外れている場所。
16. **Release を作らない**（`decisions.md` 22＝「Release は裁定まで作らない」）。`gh release` は撃たない。
17. **`yomiwakechan2` の檔を 1 行も変えない**（`decisions.md` 22）。この機体に在っても**読むだけ**。
18. **`research/` を書き換えない**（clone の中に入っていても読むだけ＝`probe/README.md` の冒頭）。

---

## 6. 詰まったときの規則

| 症状 | やること |
|---|---|
| `assemble-runtime` が sha256 不一致で止まった | **回避しない**（`-NoVerifyCache` を使わない）。落ちた檔名・期待 sha・実測 sha を逐語で報告して止まる |
| ダウンロードが途中で切れる | 同じコマンドをもう一度撃つ（台本はキャッシュを検証して再開する）。3 回落ちたら回線の状況と一緒に報告 |
| `cuda-bench.ps1` が非ゼロで終わった | `build\out\probe-log\cuda-bench-<slug>.status.txt`（`EXIT=`・`CHECKS=`・`CHECKS_FAILED=`・`SLUG=`・`JSON=`）・その `JSON=` が指す檔の `errors` 配列と `checks` 配列・`server-<slug>.err.log` の末尾 50 行を逐語で報告。**JSON は残してある**（失敗の証拠なので消さない）。`<slug>` は `-Label` 無しなら変種名 |
| `EXIT=0` なのに `CHECKS=FAIL` | **合格ではない。**`CHECKS_FAILED=` に並ぶ名前と、JSON の `checks` 配列の該当項目（`detail` 付き）を逐語で報告して止まる。終了コードが動かないのは仕様（`/health`・`/params`・`/ywk/status` の可否は終了コードに乗らない） |
| `cuda-bench-<slug>.prev.status.txt` が在る | その slug を**2 回以上撃った**という事実。撃ち直した理由と、前回の `EXIT=`／`CHECKS=` を報告に書く（台本は 1 世代だけ残す＝3 回目を撃つと 1 回目の判定は消える） |
| ドライバ導入のあと `nvidia-smi` が旧版のまま／失敗する | **再起動が要る。**§3-7 の表でどちらの道かを決める＝司令官がこの会話に自動再起動を明示していれば `decisions.md` 66 の仕込みをして自分で再起動、書いていなければ §3-7 の報告文で止まって司令官に頼む（`decisions.md` 71） |
| ポートが埋まっている | `cuda-bench.ps1 -Port 18092` のように空き番号を渡す。既定は 18090。**18091 を選ばない**＝便 C の `probe/rocm-warmup-probe.ps1` の既定で、`docs/radeon.md` §7 の実測もその番号で撃たれている（番号を共有すると、どちらの走行のログか分からなくなる）。**18088**（製品）・**18089**（`verify-runtime`）・**18090**（このベンチ）・**18091**（Radeon の暖機台本）は塞がりと見なし、**18092 以上**から選ぶ。選んだ番号を報告に書く |
| wrapper が `exit 2` で即死する | device 文字列の検査に落ちている（設計書 §4-6）。`server-<slug>.err.log` の 1 行を逐語で。`-Device cuda:0` の綴りを確かめる |
| `/ywk/status` の `device.actual` が `cpu` | **黙った CPU 転落**。止まって報告（これは受け入れ条件の根幹） |
| 台本に書いていない判断を迫られた | **止まって司令官に上げる。**推測で進めない（`probe/README.md` §1 の規律 3） |
