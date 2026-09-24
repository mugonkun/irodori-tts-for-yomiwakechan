# audio.cpp 実測と検分（2026-09-24・本機 Radeon）

司令官の言葉（2026-09-24）＝「audio.cppで大幅に削減できるらしいけど、まずはwebから調べてみてくれるか」→「今の版でいったん検分とリリースして、その後にaudio.cppの実測と検分をしようか。」
v2.0.7 の公開（裁定 160-5）の後、本機で実測した。**配布物・repo には何も入れていない**（第三者バイナリと GGUF は scratchpad の実験場だけ）。

## 0. 結論（先に）

| 問い | 答え |
|---|---|
| 本機（Radeon 8060S・gfx1151）で動くか | **Vulkan 版は動く**。**HIP（ROCm）版は落ちる**＝コミュニティ配布の Windows HIP ビルド（ROCm 6.4／7.1 とも gfx1151 を含む）が、Irodori の条件グラフを組む所で segfault（7 通りの回避策すべて同じ所で落ちる）。CPU 版は動く（RTF 2.3）。 |
| GPU メモリは減るか | **減る**。PyTorch 経路（v2.0.7）＝OS 側専用 4,245→4,590 MiB で常駐。audio.cpp Vulkan＝**待機 1,326〜1,342 MiB**・読み上げ中の山は文の長さで 1.9 GB（コメント程度・9 s まで）〜3.5 GB（25 s の参照つき）。`mem_saver`（既定 on）が段ごとにグラフの作業領域を返すので、**要求の合間は 1.3 GB に戻る**。off にすると 5.1 GB まで育つ（速さは同じ）＝既定のままが正しい。 |
| 速さは | 本機では **ほぼ同等**。暖機後の RTF＝PyTorch 0.14〜0.26／audio.cpp Vulkan 0.18〜0.25（6〜25 s の出力）。短文（1.5 s）は固定費で両者 0.43〜0.64。**新しい長さでも遅くならない**（PyTorch 経路の MIOpen の探索のような初見の代金が無い＝14_mid_c_newlen で PyTorch 3.22 s／audio.cpp 1.73 s）。 |
| 導入物の大きさ | PyTorch 経路＝実行環境 5.9 GiB＋モデル 3.57 GB（v4.1 Small safetensors 3.07 GB＋DACVAE 0.43 GB＋silentcipher）。audio.cpp＝**Vulkan 版 58 MB＋GGUF q8 1.37 GB**（CUDA 版は cudart 込みで 270〜607 MB）。 |
| 品質 | 数値では同等（長さ ±4 %・音量 RMS 0.135〜0.163・無音の縁 0.0〜1.0 s）。**耳での検分は司令官に委ねる**＝N: に 3 対を置いた（§6）。 |
| 権利・透かし | audio.cpp は Apache-2.0（ShugoAI LLC）。**移植版には SilentCipher の透かしが無い**（本アプリの PyTorch 経路は上流どおり透かしを入れる）。Irodori-TTS の利用条件（倫理制限）との整合は**司令官の裁定事項**。 |
| 今すぐ v2 に入れるか | **入れない**（裁定 152＝直しは v2・裁定 148＝v3 は保留）。ここでの実測は v3 の材料。 |

## 1. 材料（すべて scratchpad `audiocpp-lab\`・repo 外）

| 物 | 出所 | 大きさ・照合 |
|---|---|---|
| audio.cpp 上流 | https://github.com/0xShug0/audio.cpp （Apache-2.0・Copyright 2026 ShugoAI LLC・v0.8.1＝2026-09-17・★2,973） | — |
| Windows Vulkan 版 | 上流 Release v0.8.1 `audio-v0.8.1-bin-windows-x64-vulkan.zip` | 58,059,664 B・sha256 c787971e…・`audiocpp_cli.exe`／`audiocpp_server.exe`／`audiocpp_gguf.exe`・backends cpu,vulkan・msvc 19.44 |
| Windows HIP 版（コミュニティ） | https://github.com/IIIIIllllIIIIIlllll/audio.cpp/releases v0.8.0（2026-09-16）`audiocpp-bin-win-hip-rocm7.1-x64.zip` | 417,995,808 B・展開 1.6 GB（amdhip64_7・rocblas・hipblaslt の kernel library 同梱＝HIP SDK 不要）・GPU targets gfx1100/1101/1103/1150/**1151**/1200/1201・git 0f1bca2 |
| モデル | https://huggingface.co/audio-cpp/audio.cpp-gguf `Irodori-TTS-v4-Small-GGUF/irodori-tts-v4-small-q8_0.gguf` | 1,368,991,360 B・sha256 0f1b96a1…（同じ場所に f16 1.76 GB・v4.1-anime q8 1.11 GB） |
| 参照ボイス | 本アプリ同梱の `ext_hostclub_champagne.wav`（8.20 s） | — |
| 比較相手 | 本機に導入済みの v2.0.7 Radeon（wrapper pid 42080・torch 2.13.0+rocm10.0.0・bf16・暖機済み） | — |

**注意＝モデルの版が違う。** 本アプリは `Aratako/Irodori-TTS-v4.1-Small`（safetensors）を使い、audio.cpp の既定 GGUF は **v4 Small**。audio.cpp の対応表には v4.1 Small も載っており `audiocpp_gguf.exe` で自分で GGUF にできる筈だが、今回は未実施（§7）。

## 2. 手順

1. HIP 版＝`audiocpp_cli --task tts --family irodori_tts --backend hip …`。`--list-devices` は gfx1151 を見つける（VRAM 102,129 MiB＝統合メモリの全量）。
2. CPU 版＝同じ文を `--backend cpu --threads 16`。
3. Vulkan 版＝同じ文を `--backend vulkan`。
4. 16 発の連射＝`--request-sequence requests_vk_chunked.json --out-dir … --metrics`（短 2・中 2・長 2・繰り返し 3・参照つき 3＋繰り返し 1・初見の長さ 2・短の再々 1）。走行中は `GPU Process Memory(pid_*)\Dedicated Usage`／`Shared Usage`／Private Bytes を 1 秒ごとに採る（`run_measured.ps1`）。
5. 同じ 16 文を v2.0.7 の wrapper（`POST /v1/audio/speech`・話者「デフォルト」／「シャンパンコール（ホスクラ）」）に撃ち、所要と wrapper pid の OS 側専用メモリを採る（`pt_compare.py`）。
6. `audiocpp_server --config server_irodori_vk.json --no-ui`（port 18099）を起こし、同じ 16 文を OpenAI 風 `POST /v1/audio/speech`（参照は `voice_ref: {type: base64, data: …}`）で撃つ（`server_client.py`）。
7. 出力 wav の長さ・ピーク・RMS・前後の無音を PyTorch 経路の物と並べる。

## 3. HIP 版が落ちる（gfx1151）

| 試し | 結果 |
|---|---|
| 既定（q8・mem_saver on） | **segfault（exit 139）**＝ログの最後は `irodori_tts.condition.graph_rebuild 1`（条件エンコーダのグラフを組んだ直後） |
| `--session-option irodori_tts.mem_saver=false` | 同じ |
| `--threads 1` | 同じ |
| `GGML_CUDA_DISABLE_GRAPHS=1` | 同じ |
| `GGML_CUDA_FORCE_CUBLAS=1` | 同じ |
| `GGML_CUDA_FORCE_MMQ=1` | 同じ |
| weight_type=f16／f32（codec も） | 同じ |

同じ文が CPU（RTF 2.31・13.8 s）と Vulkan で通るので、モデルの梱包ではなく **HIP 後端×この iGPU** の問題。配布側の文書も「gfx1151 で検証した」とは書いていない（コンパイルの網羅だけ）。上流の issue に gfx1151 の報告は無い（2026-09-24 時点）。ROCm 6.4 版の zip（450 MB）は未試行。

## 4. 実測＝Vulkan 版 vs PyTorch 経路（同じ 16 文・同じ機）

暖機後（audio.cpp は 1 セッション内の 1 発目から・PyTorch は導入直後の暖機 3 発の後）。wall は 1 発の所要・audio は出力の長さ・RTF＝wall÷audio。

| # | 文 | 出力 s（cpp／pt） | audio.cpp Vulkan wall s | PyTorch v2.0.7 wall s | RTF cpp | RTF pt |
|---|---|---|---|---|---|---|
| 01 | 短「こんにちは。」 | 1.48／1.52 | 0.70 | 0.97 | 0.47 | 0.64 |
| 02 | 短 | 3.20／3.20 | 0.90 | 0.91 | 0.28 | 0.29 |
| 03 | 中 | 5.96／6.00 | 1.26 | 0.92 | 0.21 | 0.15 |
| 04 | 中 | 8.96／8.92 | 1.65 | 1.25 | 0.18 | 0.14 |
| 05 | 長 | 19.20／19.92 | 3.66 | 4.09 | 0.19 | 0.21 |
| 06 | 長 | 17.52／18.32 | 3.93 | 3.70 | 0.22 | 0.20 |
| 07 | 03 の繰り返し | 5.96／6.00 | 1.38 | 0.89 | 0.23 | 0.15 |
| 08 | 01 の繰り返し | 1.48／1.52 | 0.73 | 0.65 | 0.50 | 0.43 |
| 09 | 05 の繰り返し | 19.20／19.92 | 3.70 | 2.76 | 0.19 | 0.14 |
| 10 | 参照つき 短 | 4.92／5.08 | 1.67 | 1.16 | 0.34 | 0.23 |
| 11 | 参照つき 中 | 10.20／10.32 | 2.43 | 1.74 | 0.24 | 0.17 |
| 12 | 参照つき 長 | 24.84／23.20 | 6.27 | 5.94 | 0.25 | 0.26 |
| 13 | 11 の繰り返し | 10.20／10.32 | 2.43 | 1.64 | 0.24 | 0.16 |
| 14 | 初見の長さ 中 | 8.52／8.52 | 1.73 | **3.22** | 0.20 | 0.38 |
| 15 | 初見の長さ 中 | 6.88／6.92 | 1.48 | 0.94 | 0.21 | 0.14 |
| 16 | 01 の再々 | 1.48／1.52 | 0.73 | 0.72 | 0.49 | 0.47 |

- PyTorch 経路は暖まった形では少し速い（中文で 0.9 s vs 1.3 s）。audio.cpp は**初見の長さに代金が無い**（14 番＝PyTorch は MIOpen の探索で 3.2 s、audio.cpp は 1.7 s）。配信の実用では両者とも実時間の 1/4〜1/5＝差は体感に出にくい。
- audio.cpp の 1 セッション目の 1 発目（プロセス起動直後）は 3.3〜3.9 s（グラフ構築込み）。サーバ常駐なら 1 回だけ。
- **長文は Vulkan の 1 バッファ上限に当たる**＝19 s の出力で codec グラフが 3.9 GB の連続確保を求め `ErrorOutOfDeviceMemory`（Vulkan の maxBufferSize）。`text_chunk_mode=japanese`＋`text_chunk_size=64` で文を分けると通る（上の表はその設定）。PyTorch 経路は 24 s を一括で通す。読み分けちゃんの用途（コメント 1 件ずつ）では分割で足りる。

## 5. GPU メモリ（OS 側・`GPU Process Memory\Dedicated Usage`・MiB）

| 経路 | 待機 | 読み上げ中の山 | 16 発の後 | 備考 |
|---|---|---|---|---|
| PyTorch v2.0.7（wrapper） | 4,245 | 4,590（伸びは新しい長さの 2 回だけ・reserved 3,822→4,110） | **4,590 で常駐** | v2.0.7 で伸びは止めたが、常駐そのものは 4.2〜4.6 GB |
| audio.cpp Vulkan CLI（mem_saver on） | — | **1,903**（#1〜4＝9 s まで）／2,529〜3,459（長文・参照つき 25 s） | 終了で 0 | 1 秒刻みの標本＝山と谷を行き来（グラフ作業領域を段ごとに返す） |
| audio.cpp Vulkan server（mem_saver on） | **1,326**（モデル読込直後） | （標本は要求の直後のみ）1,342 | **1,342**（3 秒後も同じ） | 要求の合間は 1.3 GB＝**8 GB 板でも 6.6 GB 余る** |
| audio.cpp Vulkan CLI（mem_saver off） | — | 5,143（private 9.4 GB） | — | 速さは同じ＝**off にする理由なし** |

ホスト RAM＝audio.cpp は private 1.6 GB（待機）〜4.8 GB（山）・PyTorch wrapper は working set 2.3 GB。統合メモリ機なので「専用」も実体は主記憶だが、8 GB の RTX で問題になるのは CUDA 版の山＝上の「山」の欄が目安（CUDA 版の実測は未了）。

## 6. 音の検分材料（N:）

`N:\temp_for_claudecode_agents\irodori-ywk\rtx-handoff\reports\audio-cpp-trial-2026-09-24\`
- `audiocpp_vulkan_03_mid_a.wav`／`pytorch_v207_03_mid_a.wav`（中・6 s）
- `audiocpp_vulkan_05_long_a.wav`／`pytorch_v207_05_long_a.wav`（長・19〜20 s・audio.cpp は 64 字で分割合成）
- `audiocpp_vulkan_12_clone_long.wav`／`pytorch_v207_12_clone_long.wav`（参照つき・ホスクラ・23〜25 s）
- `audiocpp_vulkan_memory.csv`・`pt_compare.json`・`server_client.json`・`requests_vk.json`（生の数字）

数値＝48 kHz／16 bit 両者とも。ピーク 0.71〜1.00・RMS 0.135〜0.163・前後の無音 0.00〜1.02 s（audio.cpp の参照つき長文は末尾 1.0 s の無音＝上流も注記する「v4 の参照条件付けで末尾に一句足すことがある」の類かは耳で）。**種（seed）が違う実装なので同じ声質・抑揚にはならない**＝良し悪しは聴いて決める物。

## 7. 未了・次にやるなら

1. **v4.1 Small を GGUF にして同条件で測る**（`audiocpp_gguf.exe` で `Aratako/Irodori-TTS-v4.1-Small` を変換）＝本アプリと同じモデルでの比較。
2. **RTX（CUDA 版）の実測**＝上流 Release の `audio-v0.8.1-bin-windows-x64-cuda13.3.zip`（270 MB）＋cudart zip。8 GB 板の山が 2〜3.5 GB に収まるかが本題。
3. HIP の segfault＝ROCm 6.4 版の zip・`--backend hip --device` の別指定・上流へ issue（gfx1151・条件グラフ構築で落ちる・再現手順）。
4. 透かし＝移植版に SilentCipher が無い件の扱い（Irodori-TTS の利用条件との整合）＝司令官の裁定。
5. 組み込むなら v3 の枠（自前の実行環境＝裁定 148 の条件「権利の確認が先」）＝audio.cpp は Apache-2.0 で再配布可・GGUF はモデルの元の条件に従う。
