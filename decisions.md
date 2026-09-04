# decisions.md — 配布版 irodori-TTS（irodori-tts-for-yomiwakechan）の確定事項

> 本檔がこのリポの正典。依頼文（`research/`・yomiwakechan2/docs/irodori-ywk-repo-setup-brief-2026-09-04.md）は正典ではない。
> 用語＝**本体**＝yomiwakechan2／**配布版**＝このリポで作る Server＋ランチャ UI のアプリ／**上流**＝Aratako 氏の Irodori-TTS と Irodori-TTS-Server（無改変・submodule で pin）。
> 「司令官」＝利用者（mugonkun）。「席」＝この Claude Code セッション（Fable 5.1）。サブ席＝Workflow の opus 5。

## 設営時の確定事項 — 2026-09-04

### 司令官裁定（依頼文 §2 から持ち込み）

1. **別プロダクト・別リポ・本体に内包しない**。利用者は別途ダウンロードして導入。README 冒頭に「非公式・Aratako 氏とは無関係」。
2. **既定ポート 18088**・bind `127.0.0.1`・api_key なし（ループバック限定）。既存 8088（Irodori-TTS-Server 直結）と**併用可**。
3. **上流は無改変**＝submodule で pin（Irodori-TTS `8224dafb46d0aba89209a8f905f1cb7e3299d9c1`・Irodori-TTS-Server `841fb7c6ec57729c56b9b75c0ef2562249b13a10`）＋`patches/`（ビルド時適用）。本体との契約で足りない口は `server/ywk_server.py`（wrapper 1 檔・上流 app を import して路を足す）。
4. **CUDA 版＝既定 cu130・cu126 は利用者が選べる**。両版の取得台帳を持ち、検出ドライバが 580 未満なら cu126 を勧める（自動切替はしない）。cu126 が 2023 年秋ドライバに届くか（U-14）は RTX 機で実射して決着（便 B）。
5. **ROCm 未保障・Radeon 版は別リリース**（bf16 固定・起動時暖機）＝同じリポのビルド変種。保証文言は「gfx1151（Ryzen AI MAX+ 395／8060S）で確認済み」の事実だけ。
6. **起動主体＝配布版が常駐**（G-2）＝本体は `/health` で見つけるだけ。GPU 選択と精度は配布版の UI が env に載せる。
7. **既定値の焼き込み**＝`preload=true`・`empty_cache_interval=0`・精度は device 連動（GPU→bf16・CPU→fp32）・`allow_no_ref_voice=false`＋alias「デフォルト」・v4.1-Small 決め打ち・`voices_dir` 絶対パス・ready 待ち 120 s。

### 着任時に卓へ上げ、司令官が裁定した 5 点（2026-09-04・すべて管制推奨を採用）

8. **実行系（torch・依存 wheel）もモデルも初回取得**＋**第三者バイナリを配布物に入れない**（依頼文 §2-7 ⒜⒝）。Release 資産＝ランチャ＋埋め込み Python＋自作分（数十 MB）。torch は `download.pytorch.org/whl/cu1xx` の pin URL＋sha256、依存は PyPI、モデルは HF、vc_redist は Microsoft 公式 URL。`licenses/` は Irodori 系 MIT 3 本（全文）＋自作分。**オフライン導入は捨てる**。
9. **透かし（SilentCipher）は既定 ON**＝重み（≈65 MiB）を初回取得に含める。切る経路は持たない。
10. **既定 steps は 40 のまま**（上流既定）。UI のプリセットで 10 を選べる。本体の指定が来たら必ず勝つ（上流の優先順 `irodori.X`→トップレベル→env と一致）。
11. **参照潜在キャッシュ（ref_latent の事前計算 `.pt`）は持たない**。GPU 前提で節約は 40〜115 ms のみ。Radeon 版で必要になったら別途起票。

### 司令官の希望（2026-09-04・依頼文の外で追加）

12. Python を知らなくても導入できる。本体から独立した配布パッケージ＋UI を持つアプリ。単体 TTS として扱える。設定可能パラメータ一覧を本体が取得できる（`GET /params`）。
13. **CPU 合成は可だが遅さはケアしない**（UI にだけ露出・配信用途では非推奨と明記）。
14. **深い先読み機構は余力があれば**＝本体スケジューラはまだ対応しないが、使いやすい形で契約に準備する（設計は便 A の契約文書で案を出し、実装は後続便）。
15. **UI の目的**＝利用者が自環境で GPU・CUDA 版・パラメータ・参照ボイスを試す（配信外の読み上げテスト）。本体からの読み上げ司令は UI で指定した GPU・CUDA 版を**暗黙に**使う。パラメータは本体の指定で動作。
16. **話者＝参照ボイス檔＋名付けのペア**（本体からは話者として見える）。**参照なし＝話者名「デフォルト」**（alias `no_ref`）。
17. **プリセット話者 12 名**をリリース版のテンプレート参照ボイスとして同梱する＝琴葉茜（関西弁・VOICEROID2）・弦巻マキ英語（CeVIO AI・英語分解読み上げ）・吉田くん（VOICEROID2）・月読アイ・月読ショウタ・ちび式じい・もち子さん（VOICEVOX セクシー／あん子）・つくよみちゃん（COEIROINK）・KANA ないしょばなし（COEIROINK）・MANA いっしょうけんめい（COEIROINK）・おふとんP きざ（COEIROINK）・おふとんP のーまる v2（COEIROINK）。
    作り方＝各エンジンで一次 wav を生成 →「！」「？」絵文字などを付与した本文で既存 Irodori-TTS-Server（8088）を参照ボイスとして二次ボイスを生成 → 二次ボイスをテンプレート参照ボイスにする。
18. **権利**（司令官確認済み）＝上記エンジンの生成ボイスは個人利用・商用・有償でも再配布可（元々そういう目的の商品）。本ソフトは無償配布なので問題なし。※ 席は推測で断定しない＝各エンジンの規約原文の所在は `licenses/` の台帳に司令官の確認日付と共に記す。

### 機体・受け渡し・停止域（2026-09-04）

19. **Radeon 機（この席・gfx1151）は 2026-09-07（月）まで**。以後 RTX 3090 機へ環境移行。両機は作業完了まで司令官は使わない＝自由に使ってよい。稼働機の 8088 も落としてよい（設営時点で 8088／7861 は停止中）。
20. **RTX 3090 機**＝セットアップ直後・個人情報なし・**Windows ごと壊してよい**・ただし **D:・E:・F: ドライブは触らない**。遠隔席 `12900k-new-enchanted-metcalfe`（Opus 5・Remote Control）で操作。実測時の事実＝ドライバ 591.86・CUDA 13.1・Windows 10 Pro 26200・Python 3.12.14（キット）・torch 2.10.0+cu130。
21. **受け渡し**＝`N:/temp_for_claudecode_agents/`（両機から同じ N:）。キット・結果＝`N:/irodori-native-research/`（kit-cuda・kit-ssd・listening・results-ssd）＝「好きにしてよい」。
22. **停止域**（依頼文 §5）＝上流 clone・`C:/IrodoriTTS/`・`C:/irodori-TTS-server/` を書き換えない／外部公開なし（GitHub private・HF／PyPI へ push しない・Release は裁定まで作らない）／第三者バイナリをコミットしない／ライセンスの結論を推測で断定しない／yomiwakechan2 の檔を 1 行も変えない／RTX 機のドライバ入れ替え（U-14）は司令官の明示の一言を得てから。

### 設営の事実（2026-09-04）

23. GitHub private リポ `mugonkun/irodori-tts-for-yomiwakechan` を作成し origin にした。第一コミット＝`research/`（調査便のテキスト分 160 檔・2.3 MB）。
24. Radeon 稼働機の ROCm 構成（読むだけ）＝Python 3.12.14・`torch==2.13.0+rocm10.0.0`・`torchaudio==2.11.0.2+rocm10.0.0`・index `https://stable.repo.amd.com/rocm/whl-next/`・`torch[device-gfx1151]`・HIP 7.15。Radeon 変種の取得台帳の種。
25. この機体の道具＝.NET SDK 10.0.400・Inno Setup 6（`%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`）・pwsh 7.6.5・Python 3.13.7・uv 0.12.7。

### プリセット話者の一次 wav（2026-09-04・司令官）

26. **一次 wav は存在しない＝席が生成する**。手段＝yomiwakechan2 を使うか、各エンジンを個別起動して生成（VOICEVOX／COEIROINK＝HTTP API・CeVIO AI＝COM・VOICEROID2＝本体アダプタの方式に倣う）。二次ボイスは既存 Irodori-TTS-Server（8088・Radeon 機）を参照ボイス経路で叩いて生成。この作業は Radeon 機が使える 2026-09-07 までに済ませる（便 C と同じ枠）。
27. **弦巻マキ（英語・CeVIO AI）の一次 wav**＝日本語読みができないため、席が詰まったら飛ばして進めてよい。後で司令官が生成して提供する（2026-09-04）。

### 着任読解（統合席）が挙げた追加の卓 7 件の裁定 — 2026-09-04

28. **libsndfile（soundfile wheel 同梱・LGPL-2.1）＝通知のみ**（司令官裁定）。裁定 8 により soundfile は配布物に入れず利用者機が PyPI から取得するので、§6 の義務は配布側に立たないと読む。初回取得の通知（`licenses/first-run-notices.md`・ランチャの初回取得 UI）に「LGPL-2.1 の libsndfile を含む soundfile を取得する」旨とソースの所在 URL を載せる。
29. **facebook/dacvae-watermarked のライセンス表示＝Apache-2.0 と読む（3 対 1）**（司令官裁定）。`licenses/dacvae/` には GitHub facebookresearch/dacvae の LICENSE（Apache-2.0 全文）を置き、README に「HF カード本文 46 行目のみ SAM License の記述が残る（GitHub 側は 2025-12-19 `34e0b0d` で Apache-2.0 に修正済み・LICENSE 実体は不変）」と原文の檔と行、および「当方の読みである」旨を併記する。
30. 〔設計席の判断・依頼文と整合〕**torchcodec は配布物から外す**＝`patches/0001`（`inference_runtime.py` の `_load_audio`／`save_wav`・`codec.py` の `encode_file` の `except RuntimeError` → `except (RuntimeError, ImportError)`）。理由＝FFmpeg（LGPL/GPL）を落とせ、日本語パスの参照 wav は soundfile 経路で実射済み。torchcodec 経路は未実射のまま使わない。
31. 〔設計席の判断・依頼文 §2-2〕**api_key は持たない**（bind 127.0.0.1 固定）。受け入れ条件「安全」行は「既定 bind 127.0.0.1・api_key なし・empty_cache_interval=0・preload=true を焼く」に書き換える（`docs/acceptance.md`）。
32. 〔設計席の判断・依頼文 §2-2〕**ポートは 18088 で確定**。本体側は D-9（`IrodoriConstants.cs:35` の固定値と `App.xaml.cs:131` の baseUrl 受け渡し）の改修が必ずセット＝便 F の票に明記（`docs/contract.md` §9）。
33. 〔設計席の判断〕**`/params` の `cfg_scale_caption` の既定は 3.0（API 実効値）**。gradio 初期値 4.0 は説明文の注記に残す。`IRODORI_DEFAULT_CFG_SCALE_TEXT` を動かすと caption 側の既定も連動する副作用を `/params` の note に書く。
34. 〔設計席の判断〕**GPU 同定は UUID**（`torch.cuda.get_device_properties(i).uuid`・`nvidia-smi -L` は NVIDIA 専用の高速路）。`CUDA_VISIBLE_DEVICES` に UUID 形式（`GPU-xxxx`）を渡す経路は調査に実射記録が無いので、便 B（RTX 実射）で確かめてから採否を決める。それまでランチャは UUID→index 解決＋`cuda:N` で指定する。
