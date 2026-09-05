# rtx-e1-runbook.md — E-1（vc_redist 欠落機）を RTX 機で撃つ台本（便 E・2026-09-05）

> **正典**＝`decisions.md`（90 ⑶・93・94 ⑸）・`docs/design/ben-e-installer.md` §7（E-1 は 9 段に入れない＝機体の裁定が要る）・
> `installer/README.md` §4 の E-1。**司令官の裁定（2026-09-05 12:4x）＝RTX 機の `System32\msvcp140.dll` の退避を承諾。**
> 撃つ席＝遠隔席（RTX 3090・12900k-new・ドライバ 537.58・`msvcp140.dll` 14.42.34438.0 が System32 に在る＝U-8）。
> **昇格（UAC）が要る段は設計席が Chrome Remote Desktop から「はい」を押す**（`decisions.md` 81 の 3 か条＝はい は左・同意窓の間はキーを送らない）。

## 0. 何を確かめるか（E-1）

`installer/README.md` E-1 の逐語＝「**vc_redist 欠落機で理由が読める形で止まる**（DLL 名を出す・黙って `ImportError` にしない）」。
配布版の経路は 2 つあり、**両方**を見る：

| # | 経路 | 期待（設計） | 出典 |
|---|---|---|---|
| ⑴ | 初回取得ウィザードの vc_redist 段＝`VcRedistInstaller.Evaluate` が System32 を見て **Missing → 台帳の `vc_redist.x64.exe` を取得し `/install /quiet /norestart` で入れる（UAC 1 回）** | 入った後に `msvcp140.dll` が System32 に戻り、起動の段へ進む | `decisions.md` 87 ⑷・`ledger/vc_redist.json` |
| ⑵ | 検出を素通りした場合（または手動で「飛ばす」）＝wrapper の `import torch` が落ちる | ランチャの状態帯に **理由 1 行（`msvcp140.dll` の名を含む）**・黙った CPU 転落や無言の停止にならない | `docs/acceptance.md` §3 の 3・`decisions.md` 83 |

## 1. 材料（N: が正）

- `N:\temp_for_claudecode_agents\irodori-ywk\rtx-handoff\installer\irodori-tts-ywk-setup-v0.1.0-cuda.exe`（84,986,833 B）
- 同 `SHA256SUMS.txt`＝`639e60ef135c50f739621a227dd9165eaf65137da6678513bbad9c2521a5296d  irodori-tts-ywk-setup-v0.1.0-cuda.exe`
- モデル＝`C:\ywk\models`（便 B の持ち込み・3.33 GiB）＝**データ樹へ写して再取得を省く**（§2-3）。
- ポート＝RTX 機では既定の **18088 のままでよい**（本体は無い・停止域の 18088 は Radeon 機の話）。

## 2. 準備（遠隔席・昇格なし）

### 2-1 写しと sha256

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "New-Item -ItemType Directory -Force -Path C:\ywk\installer | Out-Null; Copy-Item -LiteralPath 'N:\temp_for_claudecode_agents\irodori-ywk\rtx-handoff\installer\irodori-tts-ywk-setup-v0.1.0-cuda.exe' -Destination C:\ywk\installer\ -Force; Copy-Item -LiteralPath 'N:\temp_for_claudecode_agents\irodori-ywk\rtx-handoff\installer\SHA256SUMS.txt' -Destination C:\ywk\installer\ -Force; $h = (Get-FileHash -LiteralPath C:\ywk\installer\irodori-tts-ywk-setup-v0.1.0-cuda.exe -Algorithm SHA256).Hash.ToLowerInvariant(); Write-Host ('sha256 = ' + $h); Write-Host ('MATCH  = ' + ($h -eq '639e60ef135c50f739621a227dd9165eaf65137da6678513bbad9c2521a5296d'))"
```

**合否**＝`MATCH = True`。違えば止まる。

### 2-2 現状の記録（退避の前）

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p = 'C:\Windows\System32\msvcp140.dll'; Write-Host ('exists = ' + (Test-Path -LiteralPath $p)); if (Test-Path -LiteralPath $p) { $v = (Get-Item -LiteralPath $p).VersionInfo; Write-Host ('version = ' + $v.FileVersion + ' / ' + $v.ProductVersion); Write-Host ('bytes = ' + (Get-Item -LiteralPath $p).Length) }; Write-Host ('user = ' + [Security.Principal.WindowsIdentity]::GetCurrent().Name); Write-Host ('programs = ' + ((Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA 'Programs') -Directory -ErrorAction SilentlyContinue | Where-Object Name -like 'irodori*' | Measure-Object).Count)); Write-Host ('data tree exists = ' + (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'irodori-tts-ywk')))"
```

**記録すること**＝`version`・`bytes`（戻すときの照合に使う）。

### 2-3 無人導入とデータ樹の下拵え（昇格なし）

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$log = 'C:\ywk\logs\e1-install.log'; $p = Start-Process -FilePath C:\ywk\installer\irodori-tts-ywk-setup-v0.1.0-cuda.exe -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG=' + $log)) -Wait -PassThru; Write-Host ('EXIT=' + $p.ExitCode); Select-String -LiteralPath $log -Pattern 'User privileges|Administrative install mode|Install mode root key|Installation process succeeded' | ForEach-Object { Write-Host $_.Line }"
```

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$d = Join-Path $env:LOCALAPPDATA 'irodori-tts-ywk'; New-Item -ItemType Directory -Force -Path (Join-Path $d 'models') | Out-Null; robocopy C:\ywk\models (Join-Path $d 'models') /E /NFL /NDL /NJH /NJS /NP | Out-Null; Write-Host ('robocopy exit = ' + $LASTEXITCODE + ' (0-7 = ok)'); Write-Host ('models files = ' + (Get-ChildItem -LiteralPath (Join-Path $d 'models') -Recurse -File | Measure-Object).Count)"
```

**合否**＝`EXIT=0`・`User privileges: None`・`Administrative install mode: No`・`Installation process succeeded.`・robocopy exit 0〜7。
**まだランチャを起こさない。**「準備完了・2-2 の版＝〈値〉」と設計席へ返信して待つ。

## 3. 退避（**設計席が CRD の管理者窓から撃つ**＝遠隔席は撃たない）

設計席が CRD で「管理者: Windows PowerShell」を開き（タスクバー右クリック→ターミナル（管理者）→UAC はい）、相対パスで撃つ：

```
cd /
cd Windows
cd System32
Rename-Item -LiteralPath msvcp140.dll -NewName msvcp140.dll.e1-bak
Test-Path msvcp140.dll
```

**合否**＝`False`（消えている）。設計席が「退避した」と遠隔席へ返信。

## 4. 実射（遠隔席が起こし、設計席が CRD で画面を進める）

### 4-1 ランチャを起こす（遠隔席）

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$exe = Join-Path $env:LOCALAPPDATA 'Programs\irodori-tts-ywk\IrodoriTtsYwk.Launcher.exe'; Write-Host ('exe = ' + $exe + ' exists=' + (Test-Path -LiteralPath $exe)); $p = Start-Process -FilePath $exe -PassThru; Start-Sleep -Seconds 3; Write-Host ('pid = ' + $p.Id + ' alive=' + (-not $p.HasExited))"
```

初回取得ウィザードが自動で開く。**以後の押下は設計席が CRD で行う**＝通知に同意→変種は **cpu**（この機体は cu130 も cu126 も動くが、E-1 の狙いは DLL の欠落なので取得が最小の cpu を選ぶ）→「この構成で取得を始める」。

### 4-2 見るもの（設計席が画面・遠隔席がログ）

| 段 | 期待 | 記録 |
|---|---|---|
| vc_redist の段 | 「`msvcp140.dll` が無いので Microsoft Visual C++ 再頒布可能パッケージ … を入れる」の文言 → 取得（25.6 MB）→ **UAC（設計席が はい）** → 入った | Trail の逐語・UAC の回数 |
| 起動の段 | 起動して ready（CPU・fp32・ready まで数十秒） | 状態帯の逐語 |
| **経路 ⑵ の確認**（vc_redist 段が飛ばされた／入らなかったとき） | 状態帯に **理由 1 行（`msvcp140.dll` の名）**＝無言で止まらない | 逐語 |

遠隔席はログを読む＝`%LOCALAPPDATA%\irodori-tts-ywk\logs\`（ランチャと wrapper）：

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$d = Join-Path $env:LOCALAPPDATA 'irodori-tts-ywk\logs'; Get-ChildItem -LiteralPath $d -File | Sort-Object LastWriteTime | Select-Object -Last 3 | ForEach-Object { Write-Host ('=== ' + $_.Name + ' ==='); Get-Content -LiteralPath $_.FullName -Encoding UTF8 -Tail 40 | ForEach-Object { Write-Host $_ } }"
```

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p = 'C:\Windows\System32\msvcp140.dll'; Write-Host ('after: exists = ' + (Test-Path -LiteralPath $p)); if (Test-Path -LiteralPath $p) { Write-Host ('after: version = ' + (Get-Item -LiteralPath $p).VersionInfo.FileVersion) }"
```

**記録すること**＝⑴ vc_redist 段の文言と UAC の回数 ⑵ 入った後の `msvcp140.dll` の版 ⑶ 起動の段の結末（ready か・理由 1 行か）⑷ 「試す」1 射の結果（設計席が押す・200 か）。

## 5. 戻す

- vc_redist が入って `msvcp140.dll` が戻っていれば **`.e1-bak` は消してよい**（同じ VC 再頒布の実体。版が 2-2 と違えば両方の版を記録）。**戻っていなければ**設計席が管理者窓で `Rename-Item msvcp140.dll.e1-bak msvcp140.dll`。
- ランチャを止め（トレイ→終了、または `taskkill /IM IrodoriTtsYwk.Launcher.exe /T /F`）、無人でアンインストール：

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "$u = Join-Path $env:LOCALAPPDATA 'Programs\irodori-tts-ywk\unins000.exe'; $p = Start-Process -FilePath $u -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/LOG=C:\ywk\logs\e1-uninstall.log') -Wait -PassThru; Write-Host ('EXIT=' + $p.ExitCode); Write-Host ('programs left = ' + ((Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA 'Programs') -Directory -ErrorAction SilentlyContinue | Where-Object Name -like 'irodori*' | Measure-Object).Count)); Write-Host ('key left = ' + (Test-Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{F228543A-DCF9-45A3-8826-7485C81E1757}_is1'))"
```

データ樹（`%LOCALAPPDATA%\irodori-tts-ywk`）は**残してよい**（月曜以降の cu130／cu126 の 1 周に使う）。

## 6. 報告の形

`N:\temp_for_claudecode_agents\irodori-ywk\rtx\SUMMARY-e1.md`（新規・UTF-8）に＝2-2 の版／退避の逐語／vc_redist 段の文言と UAC の回数／入った後の版／起動の結末／「試す」の結果／戻した手順／アンインストールの逐語。**SUMMARY.md と SUMMARY-after.md は無傷のまま。**

## 7. 停止域（変わらず）

D:・E:・F: 不可侵／N: の上で走らせない／commit・push なし／`System32` に触るのは 3 と 5 の rename だけ（設計席）／他の DLL は触らない。
