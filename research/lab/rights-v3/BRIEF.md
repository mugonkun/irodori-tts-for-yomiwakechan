# v3 権利調査 — 共有依頼文（全席共通・2026-09-14）

## 0. 何のための調査か

リポジトリ `C:\Users\mugonkun\source\repos\irodori-tts-for-yomiwakechan`（Windows 向け TTS アプリ）。
現行 v2 は「第三者バイナリを配布物に入れず、初回起動時に利用者の機体が公式配布元から取得する」方式。
**v3 は据え置き中**で、着手条件（decisions.md 裁定 148）は
⑴ 実行系（埋め込み Python・torch と依存 wheel・CUDA／ROCm 実行時ライブラリ・vc_redist）を**自前で組む**
⑵ **全部を自前の置き場（GitHub Release か自前配信）から配る**
⑶ **権利の確認＝各構成物の再配布条件を原文で読んで台帳にする**。配れない物があれば v3 の形を変える。
さらに v3 では、未署名の第三者ネイティブモジュールがスマート アプリ コントロールに遮断される問題
（裁定 150）を避けるため、**配布側が未署名バイナリに Authenticode 署名を付ける**構想がある。
**モデル（HF の重み 3 件）は v3 でも初回取得のまま＝本調査の対象外**。

本調査はその ⑶ である。**読むだけの調査**＝リポジトリの檔は一切書き換えない。
成果は scratchpad の下に書く（後述）。

## 1. 鉄則（席が守ること）

1. **推測で断定しない**（decisions.md 22 の停止域）。書けるのは「原文のどの檔の何行目に何と書いてあるか」と、
   その文言から機械的に読み取れる分類まで。法解釈（例：「Community 版の利用者は licensed user に当たる」）は
   **書かない**＝「卓（司令官）の裁定が要る点」として列挙する。
2. **逐語**＝引用は原文のまま（英語のまま）。要約は逐語の後に別途つける。行番号か節番号を必ず添える。
3. **原文は必ず保存**＝取得したライセンス文・ページは `originals/<短い名前>.txt` に保存し、**sha256 とバイト数と取得時刻**
   （`Get-Date -Format o` の値）と URL を記録する。HTML はテキスト化して保存してよいが、保存した物の sha256 を出す。
   取得に失敗した URL は失敗した事実（HTTP 状態）を記す。
4. **実測は実測と書く**＝wheel を開いた・PE の署名を読んだ等の観測は、コマンドと結果を記す。
5. 版は台帳（`ledger/*.json`）の pin に合わせる（torch 2.10.0+cu130／+cu126、torch 2.13.0+rocm10.0.0、Python 3.12.10、vc_redist 14.44.35211.0 など）。
6. **リポジトリの檔を編集しない。git 操作をしない。** 書いてよいのは `rights-v3/` の下だけ。
7. WebFetch／WebSearch は使ってよい（読むだけ）。大きな wheel（torch の cu 版 1.8〜2.6 GB）は**丸ごと落とさない**
   ＝HTTP Range で中央ディレクトリと必要エントリだけ読む道具 `research/lab/tmp/rt-license/zipget.py` を使う
   （`python zipget.py <url> --list` で全エントリ、`python zipget.py <url> <outdir> <substring>...` で抽出）。
8. 出力の JSON は指定のスキーマに従う。分からない欄は `"unknown"` と書き、理由を `notes` に書く。

## 2. 既にある材料（読んでから始めること）

- `research/lab/notes/22-runtime-license.md` ＝ 2026-09-04 の実行系ライセンス調査（CUDA EULA・cuDNN SLA・MSVC・PSF・
  libsndfile・ROCm wheel の観測）。**本調査はこれを土台に、「自前の置き場から配る」「署名を付ける」の 2 観点を足す**。
  未確認 1〜6 は本調査で潰す対象。
- `research/lab/tmp/rt-license/` ＝ 2026-09-04 取得の原文（cuda-eula.txt・cudnn-sla.txt・msvc-redist.txt・vs2022-redist.txt・
  py-embed-LICENSE.txt・libsndfile-COPYING.txt・apache2.txt 等）。**今日取り直して差分の有無を見る**（sha256 比較）。
- `licenses/first-run-notices.md` ＝ v2 の初回取得物の通知（A1〜A19・B1〜B8・C の卓待ち 2 点）。
- `licenses/README.md` ＝ 同梱ライセンスの索引と、dacvae／SilentCipher の観測。
- `ledger/runtime-cu130.json`・`runtime-cu126.json`・`runtime-rocm-gfx1151.json`・`python-embed.json`・`vc_redist.json`
  ＝ 各構成物の名・版・URL・sha256・size・（wheel の宣言）license 欄。**これが対象物の全集合**。
- ローカルの実物：
  - `build/out/handoff-stage/build-cache/` ＝ 台帳の PyPI wheel／sdist 99 件＋`python-3.12.10-embed-amd64.zip`＋`VC_redist.x64.exe`
    （torch／torchaudio の cu 版は無い）。
  - `%LOCALAPPDATA%\irodori-tts-ywk-radeon\runtime\rocm-gfx1151\` ＝ Radeon 版の実行系が展開済み
    （埋め込み Python＋`site-packages`。torch 2.13.0+rocm10.0.0・rocm_sdk_* が入っている）。**読むだけ・書き換えない**。
- この機体（Radeon PC）の Visual Studio は **Visual Studio Build Tools 2026 のみ**
  （vswhere: productId `Microsoft.VisualStudio.Product.BuildTools`）。Community／Professional は入っていない。.NET SDK 10.0.400。

## 3. 各席の出力

各席は `findings/<席名>.json` に次の形で書き、最後の返答（StructuredOutput）にも同じ内容を入れる。

```json
{
  "seat": "R1-nvidia",
  "fetched_at": "2026-09-14T..+09:00",
  "items": [
    {
      "id": "R1-01",
      "component": "対象（例：cudart64_13.dll ほか CUDA Toolkit 実行時 DLL）",
      "covers_files": ["cudart64_13.dll", "..."],
      "license_name": "NVIDIA CUDA Toolkit EULA",
      "source_url": "https://...",
      "original_file": "originals/cuda-eula-2026-09-14.txt",
      "sha256": "...", "bytes": 12345,
      "same_as_2026_09_04": true,
      "quotes": [{"where": "§1.1.2 / line 123", "text": "逐語..."}],
      "redistribution_grant": "explicit-yes | conditional | not-found | explicit-no | unknown",
      "conditions": ["条件を逐語の要約で列挙"],
      "modification_or_resigning": {"status": "allowed | forbidden | not-stated | unknown", "quote": "逐語"},
      "authenticode_observed": "signed-by-vendor | unsigned | mixed | not-checked",
      "open_points_for_commander": ["法解釈が要る点"],
      "notes": "実測のコマンドと結果、失敗した取得など"
    }
  ],
  "summary": "10 行以内の要点",
  "not_done": ["やり残し・取得できなかった物"]
}
```
