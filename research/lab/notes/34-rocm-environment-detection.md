# 34 — ROCm の環境判定の材料（司令官の追加の問い「ROCm の環境判断は難しそうか」・主席 Fable が司令官機で実測）

> 読むだけ＝`C:/irodori-TTS-server/Irodori-TTS-Server/.venv-rocm/`（稼働機の venv・実行のみ）・AMD の wheel 索引（HTTP GET のみ）・PyPI。書いたのは本ノートのみ。2026-09-04。

## 要点（10 行以内）

1. **判定の道具は AMD 自身が配っている**＝`rocm-bootstrap` 0.2.0（PyPI に単体で存在・純 Python・172 KB・依存なし・`rocm_bootstrap/detect.py`）。Windows では **`clinfo`**（AMD ドライバ同梱＝`C:\WINDOWS\system32\clinfo.exe`・`OpenCL.dll`）を子プロセスで呼び、gfx 名を返す（Linux は sysfs/KFD）。
2. 司令官機で **`rocm-bootstrap-detect.exe` → `gfx1151`・1.5 s**。`hipInfo.exe`（`rocm-sdk-core` 同梱・133 MB）でも `gcnArchName: gfx1151`。上書き用の環境変数＝`ROCM_BOOTSTRAP_FORCE_GFX_ARCH`／`ROCM_BOOTSTRAP_DISABLE_DETECTION=1`（`detect.py:62-66`）。
3. **Windows 用 torch の device wheel は広い**＝AMD 索引 `stable.repo.amd.com/rocm/whl-next/` の `amd-torch-device-*` に gfx1010〜1036（RDNA1/2）・gfx1100〜1103／110x（RDNA3）・gfx1150〜1153／115x（RDNA3.5）・gfx1200／1201（RDNA4）が並び、win_amd64 は確認した gfx1030／1100／110x／1200／1151／115x で各 15 本（cp310〜cp314×torch 版）。`rocm-sdk targets`＝`gfx1010;…;gfx1036;gfx1100〜1103;gfx1200;gfx1201;gfx1150〜1153;gfx908;gfx90a`。
4. **手順は 4 段で、重いのは最後だけ**＝① DXGI／WMI で vendor=AMD → ② `rocm-bootstrap` で gfx 名（1.5 s） → ③ 索引に `amd-torch-device-<gfx>` の win wheel が有るか → ④ `torch[device-<gfx>]==2.13.0+rocm10.0.0`（DL 1.1〜1.3 GB・展開 ≈3.5 GB）を入れて bf16 で 1 発焼く。①〜③は数秒。
5. **難しいのは判定ではなく判定後の保証**＝⑴ 実測があるのは gfx1151（8060S）だけ。他 arch の可否・速度は未知（索引に wheel があることと動くことは別）。⑵ ドライバ要件の原文は未取得（司令官機＝AMD 32.0.31041.1004・2026-08-17）。⑶ ROCm 固有の挙動＝fp32 復号の病的遅さ（bf16 必須・`20`）・初見形状の罰 3〜12 倍（`20`）／参照長ごとに最大 19 s（`31` 追記）・GPU が見えないとき fp32 は黙って CPU 転落（`29`）。⑷ AMD 2 枚（iGPU＋dGPU）の機体は `clinfo` が両方列挙し、どちらが 0 番かは未確認。⑸ `whl-next` は AMD 公式手順に載る経路だが（`17`）名のとおり先行チャネルで版が動く。
6. 結論＝**「判定」は AMD の道具で数秒・自動化できる。難しいのは対応表の維持と挙動の保証**＝§7-2「CUDA のみで出し、ROCm は司令官機だけの手動手順」の根拠は変わらない。ROCm を自動化するなら ⒜の導入便に「検出 → device wheel 選択 → 試し撃ち」の 1 段（B 系・1 便級）が足される。
7. 未確認＝`rocm-bootstrap` を AMD 以外の機体・`clinfo` 無しの機体で走らせたときの戻り（空になるはず）・gfx1151 以外の実挙動・Windows の ROCm ドライバ要件の原文。

## 逐語

```
> .venv-rocm/Scripts/rocm-bootstrap-detect.exe
gfx1151            （real 1.488 s）
> .venv-rocm/Scripts/hipInfo.exe | grep gcnArch
gcnArchName:                      gfx1151
> .venv-rocm/Scripts/rocm-sdk.exe targets
gfx1010;gfx1011;gfx1012;gfx1030;gfx1031;gfx1032;gfx1033;gfx1034;gfx1035;gfx1036;gfx1100;gfx1101;gfx1102;gfx1103;gfx1200;gfx1201;gfx1150;gfx1151;gfx1152;gfx1153;gfx908;gfx90a
> rocm-sdk.exe --help  → {path,test,version,targets,init}
> pypi.org/pypi/rocm-bootstrap/json → name rocm-bootstrap / version 0.2.0 / releases 0.1.0, 0.2.0
> site-packages/rocm_bootstrap/__init__.py docstring
"GPU detection via Linux sysfs (KFD topology) or ``clinfo`` (Windows)"
> where clinfo → C:\WINDOWS\system32\clinfo.exe（OpenCL.dll 同居）
> Win32_VideoController: AMD Radeon(TM) 8060S Graphics  DriverVersion 32.0.31041.1004  DriverDate 2026/08/17
```

索引の win wheel 数（`https://stable.repo.amd.com/rocm/pytorch/whl-next/amd-torch-device-<gfx>/` の `win_amd64` 件数・2026-09-04）＝gfx1030 15／gfx1100 15／gfx110x 15／gfx1200 15／gfx1151 15／gfx115x 15（`gfx120x`・`gfx103x` は索引名に無い＝0）。
