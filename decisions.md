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

### 便 P（プリセット話者）の事実と判断 — 2026-09-05

35. **一次 wav は 12 名中 11 名を席が生成した**（VOICEVOX 2・COEIROINK 5・VOICEROID2 4）。VOICEROID2 は本体に wav 取り出しが無いため `tools/preset-voices/Vr2SaveTool/`（net48 x86・Codeer.Friendly・音声保存ボタン→WPF 設定窓→Win32 保存ダイアログ）を新規実装して通した。一次は再生成しても sha256 が一致（決定的）。弦巻マキ（英）は裁定 27 のとおり司令官提供待ち。
36. **二次 wav は私設サーバ（8090・`.venv-rocm` を読むだけ・ROCm bf16・v4.1-Small・seed 1234・40 steps）で生成**。稼働機の 8088 と C:/irodori-TTS-server/ は無改変（上流に .pyc 0 件を確認）。生成物は再現的（前席と 1 バイトも違わない）。
37. **`voices/presets/*.wav`（席が Irodori で生成した二次ボイス）はリポにコミットする**（設計席の判断）。裁定 22 の「第三者バイナリ」には当たらない＝自作の生成物・配布物の同梱資産（11 本・32 MB）。一次 wav（各エンジンの出力）は `N:/temp_for_claudecode_agents/irodori-ywk/preset-voices/primary/` に保全しリポには入れない。
38. **プリセットの音量は正規化しない**。参照用途では上流が参照クリップごとに −16 dB へ正規化する（`ref_normalize_db=-16.0`・サーバの info 行で確認）ので、檔の RMS のばらつき（−16.6〜−23.8 dBFS）は合成結果に効かない。UI の試聴再生でだけ差が出る＝ランチャ側で再生時に揃えるかは便 D で判断。
39. **末尾切れの是正**＝検分席の所見（採用 11 本中 7 本の末尾 50 ms の RMS が全体 RMS を超える＝語の途中で終端）。同じ本文・同じ参照で seed を変え、末尾が減衰する射を機械的に採る。参照は 30 s 版を既定とし、司令官の試聴（`docs/preset-voices-listening.md`）で話者ごとに 10 s 版へ差し替えてよい。
    **判定規則（2026-09-05・便 P（3）で確定）**＝`tools/preset-voices/verify_wavs.py` の 3 条件すべてで clean＝⒜ 末尾 50 ms の RMS − 全体 RMS ≤ −20 dB ⒝ 末尾 50 ms の RMS ≤ −40 dBFS（絶対値）⒞ 末尾 300 ms を 25 ms 刻みで見て最後の 100 ms が直前より +0.5 dB を超えて立ち上がらない（−60 dBFS 以下は無音として除く）。旧規則（⒜のみ）では月読アイが Δ −20.19 dB の縁で通過して実体の末尾切れを見逃した。掃引の結果＝もち子さん 1235・ちび式じい 1235・つくよみちゃん 1240・KANA 1235・おふとんP きざ 1239・おふとんP のーまるv2 1240・琴葉茜 1235・月読アイ 1238・他 3 名は 1234。`voices/presets.json` の secondary に md5・size_bytes・tail 節を必須にした。
40. 便 C の暖機設計に効く実測（二次席）＝私設サーバの読込 18.98 s・spawn→ready 21.1 s。**未見の参照ボイス形状に当たるたびに decode_latent が跳ねる**（VOICEVOX/COEIROINK 参照で 0.6〜1.0 s → 最初の VOICEROID2 参照で 14.8 s・MIOpen の workspace 不足警告と同時）。暖機は no_ref 短文だけでなく、利用者が実際に使う参照ボイスで 1 本ずつ撃つのが確実。146 字の本文は `chunk_min_chars=80` で 2 分割される。

### 便 A（骨格＋ビルド）の完了と設計席の裁定 — 2026-09-05

41. **便 A 完了**（コミット fa05f63）。受け入れ条件 A-1〜A-8 を Radeon 機で実走して全緑＝CPU 変種の組み立て（台帳 101 item・903 MB・git/pip/uv 不使用）・パッチ 3 ハンク適用（submodule 無傷）・`/params` 1〜4 ms・CPU fp32 実合成（参照ボイス経由の 2 発目でパッチ適用を証明）・契約テスト 175 本・licenses 突合・第三者バイナリ 0 件。敵対検分 3 席（契約・ビルド・ライセンス）の high 9 件・medium 15 件は是正済み。
42. **依存解決の override `protobuf>=5.29.0` を採る**。理由＝descript-audiotools が引く protobuf 3.19.6 は `-nspkg.pth` を要し、`import site` を書かない `._pth` 運用（裁定の実射正本）と両立しない。実解は 7.36.1（調査便の lab 箱と同版）。
43. **取得台帳の kind に `sdist` を足す**（argbind・randomname は PyPI に wheel が無い）。展開は tar.exe（Windows 10 1803 以降の標準）。ランチャ（便 D）は .NET の tar 実装で同じ台帳を読む。
44. **numba／llvmlite（約 130 MB）は当面残す**。調査便は「両コードベースに import 0 件」と記録しているが、外す証明（A-4 の実合成）は便 B の CUDA 実射と一緒に行う。
45. **`IRODORI_DEFAULT_VOICE=デフォルト` を wrapper が焼く**（setdefault）。`/params` の `request.voice` が既定「デフォルト」を名乗る以上、voice 省略が上流の 400 になるのは矛盾。
46. **`licenses/first-run-notices.md` は配布物に入れる**。設計書 §1 の「配布物には入れない」は第三者物のことであり通知文ではない＝表現の誤り。設計書・檔冒頭・licenses/README・assemble-app の除外・check-licenses の検査を「入れる」に揃える。ランチャは初回取得前にこの檔を表示する。
47. **SSE（`stream_format:"sse"`）は受ける**（上流の機能を殺さない）。本体は使わない。契約文書に枠の形（`event: audio_chunk`／`done`／`error`・取消不可）を書く。
48. **caption と seed の空文字は wrapper が「未指定」に畳む**（上流は空 caption を 400 にする）。本体の新アダプタ（便 F）は「空は欄ごと省略」の現行規則のままで通るが、契約の文言が変わったことを便 F の票に書く。
49. 台帳の運用注意＝torch の pin URL は `download-r2.pytorch.org`（index の href の実体）で `download.pytorch.org` を fallback_url に持つ／`models.json` は pin した revision の全檔を持つので既存キャッシュに対する `--check-only` は README 等を file_missing と報告する（異常ではない）／初回取得後に `refs/main` を書かないと `HF_HUB_OFFLINE=1` で上流の読み込みが落ちる（是正済み・テストで釘）。

### 便 B の停止域の解除 — 2026-09-05

50. **U-14（RTX 3090 機のドライバを 2023 年秋の版に入れ替えて cu126 を実射）を司令官が承諾**（「入れ替え承諾」・2026-09-05）。便 B の台本に B-7 として組み込む。対象は RTX 機の C: だけ（D:/E:/F: は触らない）。終わったら現行 591.86 に戻すかは任意（壊してよい機体）。

### 便 D（ランチャ）の着工前の裁定 — 2026-09-05

51. **ランチャの .NET ランタイムは SelfContained publish（1 exe）**（司令官裁定）。.NET ランタイム（MIT・再配布条項が明確）を自作分と一緒に焼く。裁定 8「第三者バイナリを配布物に入れない」の**例外**として記帳する＝法解釈が立つ第三者物（MSVC・LGPL・NVIDIA）を避けるという趣旨に反しない。`licenses/` に .NET のライセンス（MIT）と第三者通知を置く。
52. 〔設計席〕ポート 18088 が塞がっていたら**止まって告知**する（次を探さない＝本体の定数と食い違うため）。試し撃ちの再生は NAudio（MIT）で、再生時に −16 dBFS 相当へ揃える（裁定 38）。UI は日本語のみ。設計書＝`docs/design/ben-d-launcher.md`（草稿・便 B・C の結果で §3・§5 を確定してから着工）。

### RTX 機の疎通と事実 — 2026-09-05

53. **遠隔席 `12900k-new-enchanted-metcalfe` と双方向の疎通を確認**（SendMessage の往復＋N: への檔 `ping/12900k-new-20260905-025233.txt`）。RTX 機の事実＝hostname `12900k-new`・pwsh 7.6.5＋PS 5.1（26100.9168）・git 2.55.0・**Python 3.14.7**（配布版は使わない＝埋め込み 3.12.10 を台帳から組む）・**uv 0.12.9**（`build/dev-venv.ps1` は 0.12.7 以外で止まる＝持ち込みの `uv.exe` 0.12.7 を PATH 先頭で使わせる）・RTX 3090 `GPU-19adfe89-c9e0-df55-4a8a-e31798717a36`・ドライバ 591.86・C: 空き ≈3.5 TiB・N: 可視・ドキュメントが OneDrive 配下（台本は全部 `-NoProfile`）・PSModulePath に Desktop 版経路が混在（pwsh→PS 5.1 の罠が成立し得る）。遠隔席の作業先は `C:/ywk/` を指定する（現状は `C:/claudeprobe`）。
54. **U-8（vc_redist 欠落機）は RTX 機で素のまま再現できない**＝`C:/Windows/System32/msvcp140.dll` 14.42.34438.0 が既に在る。便 B の B-3 は「System32 の msvcp140.dll の有無と版を検出し、在れば vc_redist を飛ばす」判定の実射に読み替える。欠落状態の実挙動を撃つには System32 の同 DLL を一時的に退避する必要がある（TrustedInstaller 所有・他アプリに影響）＝台本の最終手順として任意に置き、実施は司令官の一言を得てから。

### 遠隔席への交付の作法 — 2026-09-05

55. **交付経路＝`SendMessage`（双方向・実測）**。本体 `docs/operations.md` §3「remote-control 経由の実装席」の正典は「走行中の bridge 席への伝達は Routine の即時発火（作って・撃って・消す）・雲席からの SendMessage は届かない」だが、本席はローカル CLI なので `SendMessage` が往復で通った（返信 3 通・N: の檔）。Routine 経路は使わない。倣う作法＝⑴ 交付は全文を投げず「在り処＋枷」（N: の持ち込み物 `rtx-handoff/runbook/` と bundle 内の `probe/rtx-remote-runbook.md` を指し、停止域・成功判定・命名・チェックポイントをメッセージに書く）⑵ MCP 前提の指示を出さない（席が持つのは gh と PowerShell）⑶ モデルは席の自己申告で照合（bridge の get_session は last_served_model を返さない）⑷ 全工程 `-NoProfile`。
56. **ドライバ入れ替え（U-14）の承諾は遠隔席のセッションで改めて取る**（遠隔席の規則＝セッション間の許可の持ち回りは不可）。台本ではドライバ手順を独立した最終ブロック（現行 591.86・投入版と NVIDIA 公式アーカイブ URL・戻し方・セーフモードで標準 VGA に戻す復旧経路）にし、cu130／cu126 の 591.86 での測定を N: に書き戻したチェックポイントの後にだけ入る。降格後に測るのは cu126 のみ・cu130 の落ち方は事実として 1 回記録。
57. **U-8 の扱いは司令官の指示待ち**（AskUserQuestion を却下）。台本には System32 の msvcp140.dll の退避手順を入れない。B-3 は「検出して飛ばす」判定の実射のみ。

### 便 C（Radeon／ROCm 版）の完了と実測 — 2026-09-05

58. **便 C 完了**。ROCm 変種の台帳 `ledger/runtime-rocm-gfx1151.json`（107 item＝cpu と共通 99・AMD 由来 8＝torch 2.13.0+rocm10.0.0・torchaudio 2.11.0.2+rocm10.0.0・amd-torch-device-gfx1151・**amd-torch-device-gfx115x**（torch[device-gfx1151] が 2 本引く）・rocm（sdist・実行時必須）・rocm-sdk-core／libraries／device-gfx1151。AMD 由来 1.274 GB・展開 4,347.7 MiB・26,523 檔）。AMD 索引は sha256 を publish しないので落として計算し、生成器に pin（8 件）を持たせて再生成で照合する。rocm-bootstrap は落とす（torch の .py に参照 0 件・実射で不要）。
59. **実測（gfx1151・bf16・cuda:0・40 steps）**＝起動（spawn→loaded）空 db 29.0 s／温 26.1 s。暖機 6 射（no_ref 4/8/12 s＋話者 3 名）空 db 51.8 s／温 12.6 s＝機体初回の合計 80.8 s・2 回目以降 38.7 s。暖機後の同一プロセスで暖機済み話者 1 発目 1.49 s・参照なし 1.05 s・未暖機話者 1 発目 2.5 s（空 db では 9.9 s）。MIOpen db は `YWK_DATA_DIR/miopen/` に 3 檔 826 KB で落ち、プロセスを跨いで効く（暖機なし新プロセスの 1 発目 4.0 s・空 db 5.4 s）。**3 発目の再発（19.4 s）は HTTP 経路でも出ない**（研究の未確認を閉じた）。
60. **C-5 の「本物が待たされるのは暖機 1 射分（≤ 10 s）」は文言を改める**（設計席）。機構は効く（本物は走行中の 1 射だけ待ち `shots_done` は進まない）が、既定段 12 s の射は空 db で 11.7 s・温 db で 10.9 s＝1 射が 10 s を超えうる。条件文＝「本物が待たされるのは走行中の暖機 1 射分（既定段では最大 ≈12 s・ランチャは `/ywk/status.warmup` を見せる）」。段の秒に上限は設けない（12 s 段は長文形状の db を育てるため）。
61. **C-8 の RTF < 0.5 は未達**（11 プリセット・定常 0.27〜1.02・単発なら 0.25〜0.35）。原因 2 つ＝⑴ 話者切替ごとの prepare_reference 0.95〜1.44 s ⑵ 連続運転で sample_rf が 618→1,400 ms（2.3 倍・decode は不変＝MIOpen ではない・原因はクロック／電力／温度と推定・測る道具なし）。⑴ は ref_latent（.pt 事前計算）で消える見込み＝裁定 11「持たない」を **Radeon 版だけ再考する材料**として卓へ（便 D の前に司令官に問う）。⑵ は未解決の事実として `docs/acceptance.md` §3 に記帳。
62. **`empty_cache_interval=0`（裁定 7）で Radeon の GPU メモリは 1 セッション 10〜13 GB まで単調増加**（OS の GPU Process Memory・torch allocator の値ではない）。この機体は共有 107 GB なので破綻しないが、VRAM の少ない Radeon での既定は卓へ（便 D の前に司令官に問う）。
63. wrapper の追加＝`YWK_VARIANT`（rocm-* で bf16 固定・fp32 は exit 2・MIOpen db を `YWK_DATA_DIR/miopen/` へ）・`POST /ywk/warmup`／`DELETE /ywk/warmup/{id}`／`/ywk/status.warmup`（state に cancelled を含む）・`YWK_WARMUP_ON_START`・SSE 中も本物のカウンタを最後のチャンクまで保持・暖機の射も本番と同じ話者解決（`resolve_default_voice`）・ROCm torch に `YWK_VARIANT=cuda` のままなら stderr に警告。`verify-runtime.ps1` は `-Variant`／`-Device`／`-Precision`／`-HfHome` を取り、`/ywk/status` の variant・actual・precision・name・hip・gcn_arch を検分（rocm 17 checks・cpu 15 checks）。`assemble-runtime -ExpectGpu` は gcnArchName を台帳の gfx と突合し bf16 の matmul を 1 回撃つ。
64. 未特定ライセンスの追加＝rocm 版 torch の `torch/lib/liblzma.dll`・`aotriton_v2.dll`（License-File 107 件に該当なし）＝`docs/acceptance.md` §3 11 番。cu の zlibwapi・cpu の libiomp5md と同型（配布物には入れない＝初回取得の通知のみ）。

65. **参照潜在キャッシュ（ref_latent の事前計算 .pt）は Radeon 版だけ持つ**（司令官裁定・2026-09-05）。裁定 11 の「持たない」は CUDA 版に限る。Radeon 版では話者登録時（プリセットの初回展開時と利用者の追加時）に wrapper が `.pt` を 1 回焼き、`voices.json` の alias を `ref_latent` に向ける（上流の VoiceSpec は `ref_latent` を受ける・同 stem の `.wav` が `.pt` に勝つ穴があるので `.pt` は別名で置く＝research 40 §6-2・handoff §1-7）。実装は便 D（ランチャの話者台帳）と wrapper の `POST /ywk/voices/{id}/precompute` 相当で行い、`empty_cache_interval` の扱いは別途裁定待ち。

66. **便 B のドライバ入れ替えで再起動が要る場合＝席が `claude -c` で復帰するよう仕込んでから、そのまま再起動してよい**（司令官裁定・2026-09-05）。仕込み＝遠隔席が自分を起こした元のコマンド行（`Get-CimInstance Win32_Process` で自プロセスの CommandLine を読む）に `-c`（continue）を付けたものを `HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce` か「ログオン時」のタスクに登録し、作業ディレクトリ・-NoProfile を保ったまま `shutdown /r /t 15` を撃つ。復帰後は同じ会話の続き（関門⑴の確認返信と関門⑵の承諾が済んだ後の手順）から再開する。自動ログオンでなければログオンは司令官が行う。裁定 56 の「席は自分で shutdown を撃たない」はこれで上書き。

### 司令官の追加要件（便 D・Radeon 版）— 2026-09-05 就寝前

67. **Radeon 版のランチャ UI**＝⑴ 参照潜在キャッシュの ON／OFF の切り替え（既定 ON・裁定 65）⑵ 参照ボイスごとの消費メモリの概算表示（wav 参照＝+0.7 GB 級・潜在参照＝増えない・出力尺 1 フレーム ≈3.5 MB＝research 40 §1・§6 と便 C（2）の実測で係数を確定）⑶ GPU メモリの使用量と占有量（torch の allocated と reserved・OS 側の GPU Process Memory）の目視表示。実装＝wrapper の `/ywk/status` に `memory`（allocated／reserved／max・話者ごとの潜在サイズ）を足し、ランチャが常時表示する。CUDA 版でも同じ欄を出す（値の意味は同じ）。
68. 二次 wav（プリセット）の司令官の試聴は**後回し**（`docs/preset-voices-listening.md` は据え置き）。

69. **就寝前の 3 点は既定どおり**（司令官「規定通りでよい」・2026-09-05）＝⑴ U-8 は検出判定の実射のみ・System32 の DLL は退避しない ⑵ Radeon 版の `empty_cache_interval` はランチャの設定項目（初期値 0）にし、使用量表示（裁定 67）で利用者が判断できる形にする（便 C（2）の実測を docs に残す）⑶ 便 D はこの Radeon 機で着工し、UIA 検分など機体固有の段は RTX 移行後に回す。

70. **便 B のドライバ入れ替え＝最新版（591.86）への復旧は不要**（司令官・2026-09-05）。降格したままでよい。承諾なしの実行は司令官の希望だが、遠隔席の規則（承諾はそのセッションで取る＝裁定 56）は本席から上書きできない。**司令官が遠隔席の会話に先に承諾を書いておけば関門⑵は先に満たせる**（台本と交付文にその旨を書く）。

71. **遠隔席は再起動の自動実行と RunOnce の自己登録を本席の指示では受けない**（そのセッションの利用者が明示した場合のみ）。よって裁定 66 の仕込みは**司令官が遠隔席の会話に直接書いたときだけ**行われる。台本 B-7 は再起動の前後で独立して再開できる区切りにし（進捗を `C:\ywk\` に残す・降格前の全測定は N: に書き戻し済み）、再起動が要る地点では止まって報告する。降格は「恒久」（裁定 70）として承諾の文面に書く。

72. **司令官が遠隔席の会話にドライバ入れ替えの承諾（恒久降格・戻し不要・RunOnce＋`claude -c` の自動再起動可）を直接書いた**（2026-09-05「承認を通知した」）。関門⑵は先に満たされた。残る関門は⑴＝降格前の測定完了の報告に対する本席の確認返信。

73. **便 B 着手**（2026-09-05 04:0x）＝遠隔席（セッション名は「Windows sandbox environment」に改名・同一席）が N: の台本を読み B-0（持ち込み・sha256 突合・bundle からの clone・submodule pin 確認・uv 0.12.7）を完走。bundle の HEAD＝`dc6525e4cacef1fc6ebb9e592ee0fa672fb8b4f2`（decisions 72）＝本席が最新と確認し B-1 へ進むよう返信。遠隔席の利用者は「この Windows 環境はサンドボックス・D:/E:/F: に触らなければ自由」と指示済み＝B-1 以降は連続で撃つ。関門⑴（降格前の測定完了の報告→本席の確認）は生きている。既知の不具合＝`build/make-handoff.ps1` が MANIFEST の head_line を CP932 で復号して uXXXX エスケープに落とす（檔は無傷・要修正）。

74. **便 B の再起動後のログオン**＝司令官の Chrome から対象機（12900k-new）へ Chrome Remote Desktop が繋がっており、再起動後は PIN 入力画面になる。PIN は司令官から本席に渡され、**席のローカルのメモリにだけ保持**（リポ・docs・台本・他セッションへのメッセージには書かない）。自動ログオンでなければ本席が Chrome の CRD 画面で PIN を入れ、遠隔席の RunOnce（`claude -c`）が復帰するのを待つ。

75. **便 B §2（降格前の測定）完了・関門⑴を本席が確認**（2026-09-05 04:3x・遠隔席の報告・repo HEAD dc6525e・結果は `N:\temp_for_claudecode_agents\irodori-ywk\rtx\`）＝⑴ cu130／cu126 とも台帳だけから組み上がり（torch と torchaudio の 2 檔だけ取得・他 99 件は持ち込みの cache hit・fallback_url は sha256 一致）⑵ モデル 22 檔 3,570,982,039 B を 68 s で取得・`--check-only` EXIT=0・refs/main 一致 ⑶ **U-8 は再現せず**（msvcp140.dll 14.42 が既在・裁定 54 どおり・検出判定のみ実射）⑷ GPU 実合成＝device.actual=cuda:0・RTX 3090・uuid `19adfe89-…`・pci_bus_id 1・bf16・cold cu130 1.363 s／cu126 1.258 s・warm の rtf_http（短文 40 参照なし 0.277／0.262・長文 40 0.101／0.096・短文 10 0.107／0.104・参照 30 s あり 0.309／0.313）＝kit の rtf_synth より HTTP 分だけ大きい（予算 0.25／0.10 を 10 % 台で超えるのは測定の定義差＝未達とは書かない）・VRAM はプロセス全体の占有で peak 7.8 GB（torch allocator の値ではない・idle 752 MiB に完全復帰）⑸ **CUDA では未見の参照形状の罰は無い**（11 プリセットの spread 1.12／1.30）⑹ cu126 と cu130 は 12 条件中 8 条件が ±5 % 以内・4 条件は cu126 が 5〜13 % 速い ⑺ **`CUDA_VISIBLE_DEVICES` に UUID（GPU-xxxx）を載せる経路は動く**（device.actual=cuda:0・2 射 200・不一致表示は接頭辞 `GPU-` の有無だけ＝比較を直す。1 枚機での確認）＝裁定 34 の暫定（UUID→index 解決）は不要にできる ⑻ 契約テスト 255 passed（新規 clone では `build/dev-venv.ps1` の smoke が transformers を要求して落ちる＝fallback では外す・要修正）⑼ 3 点セット（import torch 全文・is_available/version.cuda/device_count・nvidia-smi 全文）を両変種で採取済み。§3（ドライバ）は第 4 ラウンドの台本の到着待ち。

76. **便 C（2）完了＝Radeon 版の参照潜在キャッシュ（裁定 65）を実装・実測**。wrapper に `POST /ywk/voices/precompute`（202・優先度は暖機と同じ・rocm-* は ready 直後に自動）・`DELETE /ywk/voices/precompute/{id}`・`DELETE /ywk/voices/{id}/latent`（alias を wav に戻して .pt と sidecar を消す）・`/ywk/status.precompute`・一覧の `latent`／`latent_stale`。潜在は `<voices_dir>/latents/<stem(話者 id の sha256 12 桁)>.pt`（fp32・(T,D)・上流が runtime dtype へ cast）＋sidecar `<stem>.json`（元 wav の sha256・符号化条件・checkpoint・latent_dim）。alias は `{"ref_latent":"latents/<stem>.pt"}` に置換（temp→os.replace）。**実測（gfx1151・bf16）**＝焼き 11 名 11〜16 s・.pt 11 檔 1.1 MB・prepare_reference は焼く前 0.95〜1.44 s → 焼いた後 1.0〜4.3 ms（プロセス初見 7.7〜16.7 ms）・**C-8 は 2 巡目で 11/11 が RTF < 0.5**（0.22〜0.40）。プロセス最初の 1 発だけ 0.73〜0.85（暖機の枠）。便 C の原因⑵（連続運転で sample_rf 2.3 倍）は 22 射・35 s では再現せず＝解消の証明ではない（未確認のまま）。焼く工程は GPU メモリを約 3.6 GB 残す（研究の「潜在は VRAM を増やさない」は射ごとのピークの話）。
77. **`empty_cache_interval` 0 と 10 の材料**（裁定 69＝設定項目・初期値 0）＝射の所要への増分は見えない（10 射目 18.6 ms・20 射目 6.4 ms＝ばらつきの中）・OS 側の GPU メモリは =10 でも下がる標本 0（標本間隔 1.4〜1.8 s の限界）・11〜21 射目で =10 は 8.54 GB で平ら・=0 は 8.54→9.16 GB（1 走ずつなので断定しない）。裁定は据え置き（設定項目・初期値 0）。
78. 便 D への申し送り＝⑴ ランチャは話者を voices.json に書いた後に明示的に `POST /ywk/voices/precompute {"all":true}` を叩く（起動時の自動 all は ready 時点の voices.json を見る）⑵ voices.json は「書くときに読み直す」（wrapper が alias 欄を書き換えるため）⑶ 話者の削除は `DELETE /ywk/voices/{id}/latent` → wav と台帳の順。便 F への申し送り＝焼いた後は要求時の `ref_normalize_db`／`ref_ensure_max` が効かない（契約 ⑷ 4-4）。

79. **便 B の持ち込みを HEAD 6916264（便 B 準備 1〜4 のコミット）で作り直し、遠隔席へ「差し替え完了・§3 に入ってよい」を送信**（2026-09-05 05:02）。関門⑴＝本席が §2 の測定を確認（裁定 75）・関門⑵＝遠隔席の会話に承諾あり（裁定 72）。§3 の手順＝復元ポイント→直リンク HEAD→取得と sha256→サイレント導入（昇格・UAC は本席が CRD から押す）→ nvidia-smi→再起動が要れば RunOnce＋`claude -c`（遠隔席の利用者の承諾済み）→ 復帰後 cu126 の 3 点セット＋1 射・cu130 の 3 点セットを記録→ `SUMMARY-after.md`。降格は恒久（裁定 70）。

80. **U-14 の裁定＝cu126 は古いドライバで実効する（実射で確定・2026-09-05 05:35）。** RTX 3090・ドライバ **537.58**（2023-10・`nvidia-smi` の CUDA Version 12.2）で `torch 2.10.0+cu126`＝`is_available=True`・`device_count=1`・`cuda-bench -Variant cu126 -Quick -Label cu126-olddriver`＝4 検査すべて PASS（`/health` 200 まで 19.2 s・`/ywk/status` device.actual=cuda:0 bf16・2 射とも 200 で wav）。cold 1 発目 1.776 s（591.86 では 1.258 s＝1 標本ずつなので差は断定しない）・warm 0.996 s（RTF_http 0.265）・VRAM ピーク 5,196 MiB（2 射のみ・10 条件走の 7,844 とは比べない）。**同じドライバで cu130 は `cudaGetDeviceCount() returned cudaErrorNotSupported`＝`is_available=False`・`device_count=0`（import は通り、落ちない）。** 根拠＝`N:\temp_for_claudecode_agents\irodori-ywk\rtx\logs\step-B7-3-8-cu126-3pointset.log`・`step-B7-3-9-cu130-olddriver.log`・`probe-log-cu126-olddriver\`。**これで裁定 4 の「cu126＝古いドライバ向けと謳わない」は解除**＝配布文言は「cu126 は 537.58 まで実測済み」。起動前検査の閾値＝**cu130 ≥ 580.xx／cu126 ≥ 528.33**（NVIDIA の CUDA 12.x minor version compatibility の Windows 下限・**未実測**）とし、**537.58 未満は「未実測の帯」と UI で告知して合成は許す**（司令官の「既定どおり」の範囲で本席が置いた初期値＝覆してよい）。ランチャの variant 選択は `import torch` の成否ではなく **`torch.cuda.is_available()` と `device_count`** で決める（cu130 は古いドライバでも import が通るため）。
81. **UAC と CRD の実射で判った運用（台本 §3-6 に反映）**＝⑴ 同意窓の「はい」は**左**（既定でハイライトされるのは右の「いいえ」）。1 回目の 1223 は本席の誤押下。⑵ **同意窓が出ている間、CRD 側はキー入力を送らない**（本席の Escape が同意窓に当たって 2 回目の即 1223）。⑶ 2 度 1223 なら遠隔席は撃ち直さず、本席が CRD で「管理者: Windows PowerShell」を開き（タスクバー右クリック→ターミナル（管理者）→はい）、`cd /`→`cd ywk`→相対パスで `run-b7-elevated.ps1` を撃つ（CRD の鍵盤は日本語配列に写り `:`・`\` が化ける）。⑷ この機体の UAC は `PromptOnSecureDesktop=1`・`ConsentPromptBehaviorAdmin=5` だったが **CRD から同意窓は見え、押せた**＝遠隔席の「原理的に押せない」は取り下げ済み。⑸ 昇格ブロックのログは `Tee-Object` だと UTF-16LE になる＝`Out-File -Encoding utf8` に揃える。
82. **§3 の事実の記録**＝⑴ 3-3 の復元ポイントは **1440 分規則で作られなかった**（直近は 2026-09-03 21:43 の「Windows モジュール インストーラー」）＝巻き戻しの保険なしで 3-6 に入った（台本 3-11 の復旧経路は生きている・機体は壊してよい＝裁定 20）。⑵ 3-6 `-s -noreboot -clean` は **173 秒で `EXIT=1`**・**再起動前の `nvidia-smi` がすでに 537.58**＝台本 3-7 の表どおり再起動せず 3-8 へ進み、3-8 が通った（EXIT=1 の意味は断定しない）。⑶ 降格は恒久（裁定 70）＝591.86 へ戻さない。⑷ `SUMMARY-after.md` は遠隔席が書く（N: の `rtx\`）。
83. **B-10（W-1＝CUDA wheel のまま GPU を隠して CPU 合成）の実射（537.58・降格後）**＝**cu126 は通る**（`CUDA_VISIBLE_DEVICES=-1`・`/ywk/status` device.actual=cpu fp32・2 射とも 200・first 13,580 ms／warm 12,846 ms＝GPU の約 13 倍・-Quick なので速度行には使わない）。**cu130 は落ちる**＝起動は健全に見え（`/health` 200 を 50.8 s で・`/params`・`/ywk/status` も 200・device.actual=cpu）、**最初の合成で `prepare_reference: 0.1 ms` の直後にプロセスが 0xC0000005（STATUS_ACCESS_VIOLATION・-1073741819）で消える**。Python 例外は出ず、サーバのログにも残らず、クライアントには `ConnectionResetError 10054` だけ。**限界**＝降格後に撃ったため「GPU を隠した」と「537.58 では cu130 が GPU を見られない」を分離できていない。**分離測定はしない**（591.86 へ戻すのは裁定 70 の恒久降格と衝突・本席の判断）。**配布への帰結**＝⑴ ランチャは **`is_available()=False` の機体で cu130 を起動しない**（cu130 の CPU 転落は禁止＝cpu か cu126 の変種に切り替える。裁定 80 の variant 選択規則を強める）⑵ 「起動して `/health` が 200 でも最初の合成でプロセスが消える」形があるので、**ランチャの死活監視は `/health` だけでなくプロセスの生存（exit code）を見て、消えたら理由 1 行で告知する**（便 D への申し送り）⑶ 本物の NVIDIA 無し機での W-1 は**未確認のまま**（acceptance §3 の 6 は cu126 について「代用で通る」に更新・cu130 は「代用で落ちる・分離未」）。根拠＝N: `rtx\probe-log-cu126-w1-nogpu\`・`probe-log-cu130-w1-nogpu\`・`SUMMARY-after.md`。
84. **B-11（A14・A15＝`torch/lib` の削れる DLL）の実射**＝台本の path `\Lib\site-packages` は誤り（埋め込み Python は `runtime-<v>\site-packages`）で直した。実測（削除・改名・移動はしていない）＝**zlibwapi.dll は両変種に在る（89,088 B）**・torch 配下に LICENSE/NOTICE は再帰で 1 檔も無い（＝「wheel の third_party に zlib の LICENSE が同梱」の裏付けはこの機体では取れない・A14 は未解決のまま）。nvperf_host.dll＝cu130 27.8 MB／cu126 15.4 MB・nvrtc-builtins64＝4.5／5.3 MB・**nvrtc64_*_0.dll と同名 `.alt.dll` がほぼ同サイズで並ぶ**＝cu130 91.0＋91.0 MB／cu126 45.9＋45.9 MB（5 檔合計 cu130 204 MiB／cu126 107 MiB）。torch\lib 全体＝cu130 53 檔 2.49 GB／cu126 53 檔 3.75 GB（cu126 の torch_cuda.dll 1.04 GB・cudnn_engines_precompiled64_9.dll 514 MB）。**削ってよいかの判断は別便**（削減余地は `.alt.dll` が zlibwapi より 3 桁大きい）。
85. **便 B 終了（2026-09-05 05:5x）**＝§2（591.86 の cu130/cu126 本測定・UUID 経路）・§3（降格・U-14）・B-10・B-11 まで完了。成果物は N: `rtx\`（SUMMARY.md 無傷・SUMMARY-after.md 35,845 B・6 走行の json・step-*.log 30 檔）。遠隔席は commit/push なし・D:E:F: 不可侵を最後まで維持・ドライバ 537.58 のまま（恒久）。B-10 の分離測定は要らない（本席の判断＝裁定 70 優先）。管理者窓は本席が CRD から閉じた。
86. **便 D（ランチャ MVP）を ae5e7a4 で取り込んだ（2026-09-05 06:4x）**＝WPF／.NET 10・SelfContained 1 exe（66.4 MiB・裁定 51）・骨組み→取得席／起動席／画面席の 3 並列→統合席（実 ROCm 実行系で無人検分 `probe/d-launch-probe.ps1` 27 段）→敵対検分 3 席（起動・取得・画面）→是正席（high/medium 18 件を直し・xUnit 401 本・検分 31/31・8088/7861 は無傷）。受け入れ条件の実測＝D-1 exit 2 を 1.65 s・D-2 列挙 2.22 s・D-3 追加 2 操作で再起動なし反映・D-4 ログ 4 行・D-5 取得→展開→`._pth`→`import torch` が実弾で exit 0（25,012 檔・903.3 MiB＝assemble-runtime と 1 檔も違わない）・D-6 起動→待機 27.5〜28.9 s・D-7 台本 31 段。**残る low 14 件と卓への票は 87〜88 の便 D（2）で処理**。実機で判った 2 つ（UI Automation は自プロセスの持ち窓と共通檔窓を列挙しないことがある＝Win32 `EnumWindows` を併用・PS 5.1 の `Get-Content` は `-Encoding UTF8` 必須）は便 E の検分台本の前提にする。
87. **便 D（2）の裁定＝⑴ `/ywk/status.memory` の形（裁定 67 ⑶・wrapper に足す）**＝`{"device":"cuda:0"|"cpu"|null,"allocated":int|null,"reserved":int|null,"max":int|null,"gpu_total":int|null,"gpu_free":int|null,"gpu_used":int|null,"latents":{"<話者 id>":bytes,…},"latents_total":int,"sampled_at":"<ISO 8601>"}`。単位はすべてバイト。`allocated`／`reserved`／`max` は torch の allocator（`memory_allocated`／`memory_reserved`／`max_memory_allocated`）、`gpu_total`／`gpu_free` は `torch.cuda.mem_get_info`（ROCm も同じ口）、`gpu_used = gpu_total − gpu_free`＝**カード全体の占有（他プロセス込み）**。device が cpu か未読込のときは数値欄を null にし `latents` だけ出す。`latents` は `latents/<stem>.pt` の実サイズを話者 id で引いた表（焼いていない話者は載せない）。ランチャの表示＝**使用量＝allocated・占有量＝reserved・GPU 全体＝gpu_used／gpu_total**、話者ごとの概算＝焼いてあれば `latents[id]`、無ければ wav 参照の係数（実測前の概算と明示）。**⑵ `device.pci_bus_id` は wrapper が `str()` を掛けて文字列で出す**（ROCm は int を返す・契約 ⑹ に `string|null` と書く）。**⑶ 契約の位置レコード 4 つ（`WarmupStartResult`・`PrecomputeStartResult`・`CancelResult`・`DropLatentResult`）に `[JsonPropertyName]` を書く**（SnakeCaseLower 頼みをやめる）。**⑷ vc_redist の起動引数は台帳（`ledger/vc_redist.json` の `silent_args`＝`/install /quiet /norestart`）が正**＝設計書 §6 の `/passive` を台帳に合わせて直す。`VcRedistAction.Unknown` は「入れる」に落とさず利用者に問う（low 7）。
88. **便 D（2）の裁定＝ランチャの変種の門と死活（裁定 80・83 の実装）**＝⑴ **起動前の門**＝GPU 変種（cu130／cu126／rocm）を起動する前に、その変種の `python.exe` で torch を検分し（既存の `TorchGpuProbe`）、`is_available()=False` か `device_count=0` なら**起動しない**で理由 1 行（例＝「cu130 はこの機体で GPU を見られません（ドライバ 537.58・CUDA 12.2）。cu126 か cpu の変種に切り替えてください」）と**勧める変種**（`nvidia-smi` の driver_version が ≥ 580 なら cu130・528.33 以上なら cu126・それ未満か NVIDIA 無しなら cpu）を出す。**cu130 の CPU 転落は禁止**（裁定 83＝最初の合成でプロセスが消える）。cpu 変種は門を通さない。⑵ **ドライバの閾値**＝cu130 ≥ 580.00／cu126 ≥ 528.33。cu126 で 528.33 ≤ v < 537.58 は「未実測の帯」と状態帯に 1 行出して合成は許す（裁定 80）。⑶ **死活**＝Ready 後にサーバの子が消えたら（exit code が取れた瞬間）状態を Failed に落とし、`ServerExitCodes.Describe`＋stderr の末尾 1 行で理由を出す。合成中の消失は「試す」画面にも「サーバが落ちました（exit −1073741819）」と出し、HTTP の timeout を待たない。見張りは `ServerProcess` の 1 本に寄せ、窓は最新の `StatusResponse` を読むだけにする（low 3）。⑷ **low 14 件は全部直す**（1〜9・11〜14 はランチャ・10 は `ledger/README.md` §7 を 5.26 GiB に）。⑸ **卓への票**＝`build/assemble-app.ps1` が `voices/presets/*.wav` と `voices/presets.json` を配布樹に写す行を足し、`build/verify-runtime.ps1` か check-tree 側に「配布樹にプリセット 11 檔＋presets.json が在る」検査を 1 本足す（便 A の欠け＝この便で埋める）。`CUDA_DEVICE_ORDER=PCI_BUS_ID` の 2 台構成の実射は便 E（RTX 機）へ。

