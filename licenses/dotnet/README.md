# licenses/dotnet/ — ランチャの exe に焼かれている第三者物

> **裁定 51 の記帳先**。ランチャは **SelfContained publish の 1 exe**（`launcher/IrodoriTtsYwk.Launcher/Properties/PublishProfiles/win-x64.pubxml`）
> であり、.NET ランタイムが自作分と一緒に 1 檔へ焼かれる。これは裁定 8「第三者バイナリを配布物に
> 入れない」の**明示された例外**で、趣旨（法解釈が立たない第三者物＝MSVC・LGPL・NVIDIA を避ける）に
> 反しない＝**.NET ランタイムは MIT** で再配布条項が明確だからである。
>
> ここに置く檔は**許諾文だけ**で、バイナリは 1 檔も置かない（リポの停止域はそのまま）。

## 1. 焼かれる物と、その許諾

| 物 | 版 | 許諾 | 檔 |
|---|---|---|---|
| .NET ランタイム（`Microsoft.NETCore.App.Runtime.win-x64`） | **10.0.11** | MIT | `dotnet-runtime-LICENSE.txt` |
| WPF／WinForms ランタイム（`Microsoft.WindowsDesktop.App.Runtime.win-x64`） | **10.0.11** | MIT | `windowsdesktop-runtime-LICENSE.txt` |
| .NET ランタイムが同梱する第三者物の通知 | 10.0.11 | 各記載 | `dotnet-runtime-THIRD-PARTY-NOTICES.txt` |
| NAudio.WinMM ＋ NAudio.Core（試し撃ちの再生＝裁定 52） | **2.2.1** | MIT（`<license type="expression">MIT</license>`・© Mark Heath 2023） | 下の §3 |

**版の出所**＝`launcher/IrodoriTtsYwk.Launcher/obj/project.assets.json` の
`Microsoft.NETCore.App.Runtime.win-x64` / `Microsoft.WindowsDesktop.App.Runtime.win-x64` が
`[10.0.11, 10.0.11]` に固定されていることを 2026-09-05 に読んだ。**SDK が上がるとこの版も上がる**ので、
`build/release-build.ps1` は publish のたびに実際の版を読み、この表と檔が合っているかを検分すること
（合っていなければ止まる＝許諾文が成果物より古いまま配られない）。

**檔の出所**（コピー元・逐語）＝

```
%USERPROFILE%\.nuget\packages\microsoft.netcore.app.runtime.win-x64\10.0.11\LICENSE.TXT
%USERPROFILE%\.nuget\packages\microsoft.netcore.app.runtime.win-x64\10.0.11\THIRD-PARTY-NOTICES.TXT
%USERPROFILE%\.nuget\packages\microsoft.windowsdesktop.app.runtime.win-x64\10.0.11\LICENSE
```

`C:\Program Files\dotnet\LICENSE.txt` は**採らない**＝あれは **SDK** の許諾（Microsoft Software
License Terms）であって、再配布されるランタイムの許諾（MIT）ではない。焼かれるのは後者である。

## 2. 焼かれない物（＝取得台帳の路）

torch・依存 wheel・埋め込み Python・モデル・`vc_redist.x64.exe` は **1 バイトも焼かれない**。
それらは `ledger/*.json` の URL と sha256 で利用者機が取る（裁定 8）。初回取得の前に
`licenses/first-run-notices.md` を表示して同意を取る（裁定 46）。

## 3. NAudio の許諾文について

NAudio 2.2.1 の nupkg は**許諾の全文檔を同梱していない**（`<license type="expression">MIT</license>`
＋ `licenseUrl` の名乗りだけ＝2026-09-05 に nupkg の中身を機械で確認し、`LICENSE` 相当の檔が
0 件であることを見た）。ここに全文を置くには上流リポ（`https://github.com/naudio/NAudio`）の
`license.txt` を取る必要があり、**まだ取っていない**。

**卓へ**＝⑴ 上流から全文を取ってここに置く（推奨）か、⑵ SPDX の名乗りと `licenseUrl` の記載だけで
足りるとするか。**席は推測で断定しない**（裁定 18 の作法）＝司令官の判断を仰ぐまで、この節が
そのまま欠落の記帳である。
