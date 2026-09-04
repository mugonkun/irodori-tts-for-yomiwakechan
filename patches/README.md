# patches/ — 上流の写しに当てる差分

上流（`upstream/Irodori-TTS` @ `8224daf` ・ `upstream/Irodori-TTS-Server` @ `841fb7c`）は
**submodule で pin・1 バイトも変えない**（正典 `decisions.md` 裁定 3・停止域 22）。
配布版が必要とする上流側の変更はここに差分として置き、
`build/assemble-app.ps1` が **`build/out/app/server/upstream/` に作った写しに対してだけ** 当てる。

| 檔 | 対象 | ハンク | 状態 |
|---|---|---|---|
| `0001-audio-io-fallback-on-importerror.patch` | `upstream/Irodori-TTS` @ `8224daf` | 3 | 上流へ PR を出す（下書きは patch の commit message そのもの） |

---

## 1. 当て方

写しを作ってから、その写しに当てる。**写し（`build/out/`）はこのリポの作業ツリーの中にある**ので、
`git apply` の癖に正面からぶつかる。次の 1 行が唯一の正しい形。

```powershell
# カレント＝リポ根。--directory は「リポ根からの相対パス」。
$rel = 'build/out/app/server/upstream/Irodori-TTS'
git apply --check -p1 --directory=$rel "patches/0001-audio-io-fallback-on-importerror.patch"
git apply         -p1 --directory=$rel "patches/0001-audio-io-fallback-on-importerror.patch"
```

- **submodule には絶対に当てない。** 当ててしまうと `git submodule status` に `+` が付く（A-2 が落ちる）。
- 当てたあと `git -C upstream/Irodori-TTS status --short` が空であることを台本が確かめる。

### ⚠ `git apply` は「リポ内の子ディレクトリで走らせる」と黙って何もしない

**この機で踏んだ事実（2026-09-04）。** 写しのディレクトリを `cd` して当てると、
`git apply` は **exit 0 を返しながら 1 檔も書かない**。`--verbose` を付けて初めて理由が出る：

```
$ cd build/out/app/server/upstream/Irodori-TTS
$ git apply --verbose -p1 <repo>/patches/0001-...patch
Skipped patch 'irodori_tts/codec.py'.
Skipped patch 'irodori_tts/inference_runtime.py'.
$ echo $?
0
```

理由＝**`git apply` はリポの作業ツリーの中で走ると、差分のパスをリポ根からの相対として解く**。
`git rev-parse --show-prefix` は `build/out/app/server/upstream/Irodori-TTS/` を返し、
差分の `irodori_tts/...` はその接頭辞の外なので「対象外」として飛ばされる
（git の documented behaviour＝"patched paths outside the directory are ignored"）。
`build/out/` が `.gitignore` に載っていることは関係ない（`git check-ignore` は当たるが、原因はこちらではない）。

**`--check` も同じく exit 0 を返す。**「検査が通ったから当たったはず」は成り立たない
＝これは**嘘の成功**なので、台本は当てたことを別の手で確かめること。確実なのは逆当て：

```powershell
# 当てた「あと」に走らせる。exit 0 ＝ 前像が消えて後像がある＝本当に当たっている。
git apply --check --reverse -p1 --directory=$rel "patches/0001-...patch"
```

（実射：当たっていない写しでは reverse-check が exit 1、当たった写しでは exit 0。
 逆に forward-check は当たった写しでは exit 1 になる。）

`git apply` がリポの**外**（例＝`%TEMP%` に作った写し）で走るときはこの癖は出ない。
本檔の §1 冒頭の形（リポ根＋`--directory`）ならどちらでも正しく当たる。

### 改行の作法（この機で実射して確かめた事実・2026-09-04）

差分檔は **LF で保つ**。`git apply` は「写しが CRLF でも LF の差分」を通すが、
「LF の写しに CRLF の差分」は通らない。

| 写し | 差分檔 | `git apply --check` |
|---|---|---|
| LF（`git show` 由来） | LF | **exit 0** |
| CRLF（作業ツリーの複写） | LF | **exit 0** |
| CRLF | LF ＋ `--ignore-whitespace` | exit 0 |
| CRLF | CRLF | exit 0 |
| LF | **CRLF** | **exit 1**（`patch does not apply`） |

⇒ **LF の差分檔なら写しの改行がどちらでも通る**。逆は通らない。

> **統合席へ**：このリポの `.gitattributes` は `* text=auto` で、`core.autocrlf=true` の機体では
> 再 clone 時に `*.patch` が **CRLF になって落ちる**。`.gitattributes` に 1 行
> `*.patch -text`（または `*.patch text eol=lf`）を足してほしい。
> `.gitattributes` は便 A 実装席 S2 の担当パスの外なので、こちらでは触っていない。

---

## 2. `0001-audio-io-fallback-on-importerror.patch`

### 何を直すか

`irodori_tts` には **soundfile への退避が 3 箇所** ある。どれも `except RuntimeError:` しか捕らない。

| 檔 | 関数 | 行（`8224daf` 時点） |
|---|---|---|
| `irodori_tts/inference_runtime.py` | `_load_audio` | 1515-1527（`except` は 1518） |
| `irodori_tts/inference_runtime.py` | `save_wav` | 1530-1541（`except` は 1536） |
| `irodori_tts/codec.py` | `DACVAECodec.encode_file` | 269-280（`except` は 272） |

torchaudio 2.10 の `load` / `save` は TorchCodec 経由になり、torchcodec が無いと
**`ImportError`** を投げる（`RuntimeError` ではない）。よって退避が働かず、
参照音声の読み込みと wav の書き出しがその場で落ちる。

3 箇所とも `except (RuntimeError, ImportError):` に広げる。
torchcodec がある環境ではその経路が `ImportError` を投げないので**挙動は変わらない**。

### 出典（この機で原文と実挙動を確認・2026-09-04）

`torchaudio 2.10.0+cpu` の `torchaudio/_torchcodec.py`：

```python
    # Import torchcodec here to provide clear error if not available
    try:
        from torchcodec.decoders import AudioDecoder      # :80-86  load_with_torchcodec
    except ImportError as e:
        raise ImportError(
            "TorchCodec is required for load_with_torchcodec. "
            "Please install torchcodec to use this function."
        ) from e
```

同型が `:244-250`（`save_with_torchcodec`、`from torchcodec.encoders import AudioEncoder`）。

実射（torch 2.10.0+cpu / torchaudio 2.10.0+cpu / CPython 3.12.14 / Windows 11・torchcodec 未導入）：

```
>>> torchaudio.load('nope.wav')
ImportError: TorchCodec is required for load_with_torchcodec. Please install torchcodec ...
>>> torchaudio.save('nope.wav', torch.zeros(1,10), 16000)
ImportError: TorchCodec is required for save_with_torchcodec. Please install torchcodec ...
```

差分を当てた写しと当てていない上流で同じ手を実行した結果：

```
patched   _load_audio OK: (1, 1600) 16000        / save_wav OK: 3244 bytes, RIFF
unpatched _load_audio raises ImportError (the bug)
```

### なぜ配布版に要るか

取得台帳は **torchcodec を外している**（`ledger/README.md`）。
torchcodec は ffmpeg の共有ライブラリを引き連れる第三者バイナリで、
裁定 8「第三者バイナリを配布物に入れない」と、依存を絞って配布量を減らす方針に噛み合わない。
外した以上、上流の退避経路が実際に働かなければ**参照ボイスがひとつも読めない**。
間引きの正しさは受け入れ条件 A-4（CPU 実合成）で証明する。

### 上流へ出す PR

差分檔の commit message（英文）がそのまま PR 本文の下書き。
`git am` でも `git apply` でも当たる format-patch 形式にしてある。

- 題：`audio io: also fall back to soundfile on ImportError`
- 触る行：3 行（`except` 節のみ）。挙動の追加も設定の追加も無い。
- 出す先：`https://github.com/Aratako/Irodori-TTS`
- 出すのは司令官の裁定を得てから（停止域 22＝外部公開は裁定まで行わない）。

### 差分が当たらなくなったら

上流が動いて hunk がずれたときは、**行番号を書き換えるのではなく上流の原文から起こし直す**。
手順（submodule を汚さない形）：

```bash
mkdir -p /tmp/pw/a /tmp/pw/b
for f in irodori_tts/inference_runtime.py irodori_tts/codec.py; do
  git -C upstream/Irodori-TTS show HEAD:$f > /tmp/pw/a/$f     # 索引の LF 原文
  cp /tmp/pw/a/$f /tmp/pw/b/$f
done
# /tmp/pw/b/ 側の 3 つの except 節を書き換えてから
git diff --no-index -- /tmp/pw/a /tmp/pw/b   # 接頭辞 a/a・b/b を a/・b/ に直して保存
```

`git -C upstream/... diff` は使わない（submodule の作業ツリーを触ることになる）。
