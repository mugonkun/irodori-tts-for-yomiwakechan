# 05 感情制御パラメータの読解ノート（BRIEF §4-3）

対象＝`upstream/Irodori-TTS`（gradio 本体・推論芯）と、露出比較のため `upstream/Irodori-TTS-Server`。
すべて読むだけ。行番号は `sed -n` / `grep -n` で実確認したもの。実行検証はしていない（＝静的読解）。

## 要点（10 行以内）

1. 最内の合成関数は 1 つだけ＝`InferenceRuntime.synthesize(req: SamplingRequest, *, log_fn)`（inference_runtime.py:1036）。引数は全部 `SamplingRequest` の 45 フィールド（同 :202-246）に集約されている。
2. **「HTTP API が露出していないパラメータ」はほぼ無い**＝45 フィールドのうち Server が渡さないのは `speaker_uncond_mode` の 1 つだけ（app.py:849-1046 に不在）。依頼の前提はここで一度更新が要る。
3. 露出の実際の穴は引数ではなく**別の層**＝⑴ device/precision/codec/compile は `RuntimeKey`（:187-198）＝プロセス起動時固定で per-request 不可、⑵ 進捗コールバック無し、⑶ キャンセル不可、⑷ 透かし on/off のスイッチが**どこにも無い**。
4. 感情の主経路は 2 本＝**caption（自由文の日本語）**と**本文テキスト中の絵文字**。絵文字は 45 種のパレットが同梱（gradio_emoji_palette.py:61-107）で、貼り先は caption ではなく **Text 欄**（gradio_app.py:449 / gradio_app_voicedesign.py:468）。
5. caption は数値でなく自由文＝「深く傷つき、今にも泣き出しそうな様子。声が震えており、悲痛なトーンで弱々しく話す。」（README.md:130）のような描写文。粒度の定義表は上流に無い（＝例文からの帰納のみ）。
6. caption は音色だけでなく**尺（duration predictor）にも効く**（model.py:1340-1362）＝話速も caption で動く。
7. 逐次出力は **Server の SSE のみ**でチャンク単位（app.py:693-775）。Euler ステップ単位の進捗も途中キャンセルも無い（`asyncio.shield`＝app.py:778-785 が中断を握り潰す）。
8. chunking は Server 側だけの機能＝句読点で切って `torch.cat` で単純連結（app.py:606-682）。**crossfade も無音挿入も無い**＝チャンク間の韻律連続性は保証されない。
9. 読み分けちゃんの「声の値」欄には**キャラ固定値（caption・参照音声・cfg 群・num_steps）と発話ごと可変値（seed・duration_scale・caption 上書き）を分けて持つ**のが素直。詳細は §5。
10. v4.1-Small の実メタデータ確認済＝`max_text_len=256` / `max_caption_len=512` / `ref_max_seconds=120.0`（HF キャッシュの safetensors ヘッダ）。

---

## 1. 最内の合成関数のシグネチャと全引数

### 1-1. シグネチャ（逐語）

`upstream/Irodori-TTS/irodori_tts/inference_runtime.py:1036-1041`

```python
    def synthesize(
        self,
        req: SamplingRequest,
        *,
        log_fn: Callable[[str], None] | None = None,
    ) -> SamplingResult:
```

これが唯一の最内合成関数。infer.py・gradio 2 本・Server はすべてこれ 1 本を呼ぶ。
下位の実サンプラは `upstream/Irodori-TTS/irodori_tts/rf.py:116-146`：

```python
def sample_euler_rf_cfg(
    model: TextToLatentRFDiT,
    text_input_ids: torch.Tensor,
    text_mask: torch.Tensor,
    ref_latent: torch.Tensor | None,
    ref_mask: torch.Tensor | None,
    sequence_length: int,
    caption_input_ids: torch.Tensor | None = None,
    caption_mask: torch.Tensor | None = None,
    speaker_state_override: torch.Tensor | None = None,
    speaker_mask_override: torch.Tensor | None = None,
    speaker_uncond_mode: str = "mask",
    num_steps: int = 40,
    cfg_scale_text: float = 3.0,
    cfg_scale_caption: float = 3.0,
    cfg_scale_speaker: float = 5.0,
    cfg_guidance_mode: str = "independent",
    cfg_min_t: float = 0.5,
    cfg_max_t: float = 1.0,
    seed: int = 0,
    cfg_scale: float | None = None,
    truncation_factor: float | None = None,
    rescale_k: float | None = None,
    rescale_sigma: float | None = None,
    use_context_kv_cache: bool = True,
    speaker_kv_scale: float | None = None,
    speaker_kv_max_layers: int | None = None,
    speaker_kv_min_t: float | None = None,
    t_schedule_mode: str = "linear",
    sway_coeff: float = -1.0,
) -> torch.Tensor:
```

戻り値は `SamplingResult`（inference_runtime.py:249-257）＝`audio` / `audios` / `sample_rate` / `stage_timings` / `total_to_decode` / `used_seed` / `messages`。

### 1-2. 全引数表（`SamplingRequest`・inference_runtime.py:202-246）

露出列の凡例＝`gr`＝gradio_app.py（参照音声版）／`gr-VD`＝gradio_app_voicedesign.py（caption 版）／`CLI`＝infer.py／`API`＝Irodori-TTS-Server の `POST /v1/audio/speech`。
「◯」＝利用者が値を選べる、「固定」＝コード側で定数を渡していて動かせない、「—」＝そもそも渡していない（＝既定値のまま）。

| # | 名前 | 型 | 既定値 | 範囲・制約（実確認） | 意味 | モデルのどこに効くか | gr | gr-VD | CLI | API |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `text` | `str` | 必須 | 正規化後が空だと `ValueError`（:1090-1092）。API 側は 1〜4096 字（Server app.py:92） | 読み上げ本文。**絵文字による演技指定もここに書く** | `normalize_text`→ ModernBERT text encoder → text 条件（:1188-1197） | ◯ | ◯ | ◯ | ◯ |
| 2 | `caption` | `str \| None` | `None` | 空文字だと `caption_mask.zero_()`＝無条件化（:1211-1213）。`use_caption_condition` の checkpoint のみ有効 | **声質・感情・話し方の自由文指定** | 共有 ModernBERT → caption projector → DiT の joint-attention 文脈（model.py:1818-1826・957-977）＋ duration 予測（model.py:1340-1362） | — | ◯ | ◯ | ◯ |
| 3 | `ref_wav` | `str \| None` | `None` | `ref_wavs` と併用不可（:855-856） | 参照音声 1 本のパス | DACVAE encode → speaker 条件 | ◯(File) | ◯(File) | ◯ | ◯ |
| 4 | `ref_wavs` | `list[str] \| None` | `None` | 順序が有意。合計が `max_ref_seconds` に達したら以降は無視（:929-935） | 参照音声複数 | 同上（latent を入力順に concat） | ◯ | ◯ | ◯ | ◯ |
| 5 | `ref_latent` | `str \| None` | `None` | `ref_latents` と併用不可（:857-858）。wav 系と混在不可（:859-860） | 事前計算 latent（.pt） | encode を省いて直接 speaker 条件へ | — | — | ◯ | ◯ |
| 6 | `ref_latents` | `list[str] \| None` | `None` | 同上 | 同上・複数 | 同上 | — | — | ◯ | ◯ |
| 7 | `ref_embed` | `str \| None` | `None` | 他の参照指定・`no_ref` と排他（:1010-1020） | Speaker Inversion 埋め込み（.speaker.safetensors） | `speaker_state_override` として speaker 枝を直接置換（:1022-1035） | ◯ | — | ◯ | ◯ |
| 8 | `no_ref` | `bool` | `False` | speaker 条件を持つ ckpt では参照 6 種のどれかか `no_ref` が必須（infer.py:401-410） | 参照無し合成（caption だけ／テキストだけ） | ref_latent をゼロ・mask を全 False（:872-887） | ◯ | ◯(暗黙) | ◯ | ◯ |
| 9 | `ref_normalize_db` | `float \| None` | `-16.0` | `None` で無効。コーデック学習時と同じ値なので既定推奨（docs/parameters.md:50） | 参照音声のラウドネス正規化目標 | DACVAE encode 前段（codec.py:222） | 固定 -16.0（gradio_app.py:285） | 固定 -16.0（:340） | ◯ | ◯ |
| 10 | `ref_ensure_max` | `bool` | `True` | `ref_normalize_db=None` のときだけ効く（infer.py:139-145） | ピーク 1.0 超のときだけ縮小 | 同上 | 固定 True（gradio_app.py:286） | 固定 True（:341） | ◯ | ◯ |
| 11 | `num_candidates` | `int` | `1` | `>0`（:1080-1082）。gradio は 1〜32（`MAX_GRADIO_CANDIDATES=32`・gradio_app.py:24） | 1 回のバッチで作る候補数 | バッチ次元（VRAM 増） | ◯ | ◯ | ◯ | ◯ |
| 12 | `decode_mode` | `str` | `"sequential"` | `{"sequential","batch"}`（:1083-1087） | コーデック decode の一括／逐次 | decode 段のみ（:1390-1417） | 固定 sequential（:326） | 固定 sequential（:343） | ◯ | ◯ |
| 13 | `seconds` | `float \| None` | `None` | `>0`（:1067-1068）。`min/max_seconds` でクランプ（:1243-1247） | 出力長の手動固定 | duration predictor を丸ごとバイパス | ◯ | ◯ | ◯ | ◯ |
| 14 | `duration_scale` | `float` | `1.0` | `>0`（:1069-1071）。gradio は 0.5〜1.5・step0.01（gradio_app.py:491-497） | **話速（予測尺の倍率）**。>1 で長く＝ゆっくり | 予測フレーム数に乗算（:1311-1315） | ◯ | ◯ | ◯ | ◯（`speed` からも自動換算） |
| 15 | `min_seconds` | `float` | `0.5` | `>0`（:1073-1074） | 予測尺の下限 | フレーム下限（:1313-1316） | — | — | — | ◯ |
| 16 | `max_seconds` | `float` | `30.0` | `>= min_seconds`（:1075-1078） | 予測尺の上限 | フレーム上限（:1314-1316） | — | — | — | ◯ |
| 17 | `max_ref_seconds` | `float \| None` | `None`→ckpt 値 | `None` は ckpt の `ref_max_seconds`、無ければ 30.0（:483-490・372）。**v4.1-Small は 120.0**（実測・§1-3） | 参照音声の合計上限秒 | 参照 latent の切り詰め（:891-899・967-974） | 固定 None（:329） | 固定 None（:346） | ◯ | ◯ |
| 18 | `max_text_len` | `int \| None` | `None`→ckpt 値 | `>0`（:1097-1098）。**v4.1-Small は 256**（実測） | 本文トークン上限（超過は truncate） | tokenizer.batch_encode（:1188-1191） | 固定 None（:330） | ◯ | ◯ | ◯ |
| 19 | `max_caption_len` | `int \| None` | `None`→ckpt 値 | `>0`（:1104-1105）。**v4.1-Small は 512**（実測） | caption トークン上限 | caption tokenizer（:1207-1210） | — | ◯ | ◯ | ◯ |
| 20 | `num_steps` | `int` | `40` | gradio は 1〜120・step1（gradio_app.py:481）。sway＋6 で低遅延の例（README.md:280-289） | Euler 積分ステップ数＝品質／速度の第一ノブ | rf.py の反復回数（rf.py:128） | ◯ | ◯ | ◯ | ◯ |
| 21 | `cfg_scale_text` | `float` | `3.0` | gradio 0.0〜10.0・step0.1（gradio_app.py:520-526）。`joint` では有効な scale が全一致必須（:342-347） | 本文への追従強度（発音の明瞭さ） | rf.py の text 枝 CFG（rf.py:129） | ◯ | ◯ | ◯ | ◯ |
| 22 | `cfg_scale_caption` | `float` | `3.0` | 同上。**gr-VD の初期値は 4.0**（gradio_app_voicedesign.py:538-543）、**API の既定は 3.0**（`default_cfg_scale_text` を流用・app.py:930-937） | **caption＝感情指定への追従強度** | rf.py の caption 枝 CFG（rf.py:130） | — | ◯ | ◯ | ◯ |
| 23 | `cfg_scale_speaker` | `float` | `5.0` | 同上。speaker 条件が無効なら 0.0 に落とされ info メッセージ（:333-340） | **参照話者への追従強度＝声の似せ具合** | rf.py の speaker 枝 CFG（rf.py:131） | ◯ | ◯ | ◯ | ◯ |
| 24 | `cfg_guidance_mode` | `str` | `"independent"` | `{"independent","joint","alternating"}`（:1148-1152） | CFG の作り方。independent は NFE=1+有効条件数、他は 2x（docs/parameters.md:139-146） | rf.py:180-188・464 | ◯ | ◯ | ◯ | ◯ |
| 25 | `cfg_scale` | `float \| None` | `None` | 非 None なら text/caption/speaker を全部上書き（:335-338）。**deprecated**（infer.py:230-234） | 一括上書き | 同上 | ◯ | ◯ | ◯ | ◯ |
| 26 | `cfg_min_t` | `float` | `0.5` | `cfg_min_t <= t <= cfg_max_t` の窓でのみ CFG 適用（rf.py:464） | CFG を効かせる時刻下限 | rf.py:464 | ◯ | ◯ | ◯ | ◯ |
| 27 | `cfg_max_t` | `float` | `1.0` | 同上 | CFG を効かせる時刻上限 | rf.py:464 | ◯ | ◯ | ◯ | ◯ |
| 28 | `truncation_factor` | `float \| None` | `None` | `>0`（:1115-1116）。0.8〜0.9 で変動が減るが表現力も落ちる（docs/parameters.md:107） | 初期ノイズのスケール | rf.py:137 | ◯ | ◯ | ◯ | ◯ |
| 29 | `rescale_k` | `float \| None` | `None` | `rescale_sigma` と**必ず同時**（:1117-1118） | 時間方向スコア再スケール k | rf.py:138 | ◯ | ◯ | ◯ | ◯ |
| 30 | `rescale_sigma` | `float \| None` | `None` | 同上 | 同 sigma | rf.py:139 | ◯ | ◯ | ◯ | ◯ |
| 31 | `context_kv_cache` | `bool` | `True` | — | text/speaker/caption の K/V を前計算＝高速化 | rf.py:140・`model.build_context_kv_cache` | ◯ | ◯ | ◯ | ◯ |
| 32 | `speaker_kv_scale` | `float \| None` | `None` | `>0`（:1135-1136）。参照無しなら無視して info（:1130-1134） | **話者性を強める実験ノブ**（>1 で似せる） | speaker K/V 射影に乗算（rf.py:141） | ◯ | ◯ | ◯ | ◯ |
| 33 | `speaker_kv_min_t` | `float \| None` | `None`→`0.9` | `[0,1]`（:1140-1141）。`speaker_kv_scale` 指定時のみ既定 0.9 が入る（:1137-1139） | 上記を効かせる時刻閾値 | rf.py:143 | ◯ | 固定 None（:363） | ◯ | ◯ |
| 34 | `speaker_kv_max_layers` | `int \| None` | `None`（全層） | `>=0`（:1142-1145） | 先頭 N 層だけに限定 | rf.py:142 | ◯ | 固定 None（:364） | ◯ | ◯ |
| 35 | `speaker_uncond_mode` | `str` | `"mask"` | `{"mask","noise"}`（infer.py:310-319） | CFG の話者「無条件」枝の作り方 | `model.encode_conditions`（model.py:1730） | — | — | ◯ | **— ← API 唯一の穴** |
| 36 | `seed` | `int \| None` | `None`＝毎回乱数 | `None` のとき `secrets.randbits(63)`（:1168-1172） | 再現性。同一値＝同一出力 | rf.py:135 の Generator | ◯ | ◯ | ◯ | ◯ |
| 37 | `t_schedule_mode` | `str` | `"linear"` | `{"linear","sway"}`（rf.py:194-204） | 時刻スケジュール | rf.py:190-208 | ◯（sway_coeff は非活性・:511） | ◯ | ◯ | ◯ |
| 38 | `sway_coeff` | `float` | `-1.0` | 有限必須（rf.py:192-193）。結果が単調減少でないと `ValueError`（rf.py:207-208）。gradio は -1.0〜1.5 | 負で雑音側、正でデータ側にステップを寄せる | rf.py:200 | ◯ | ◯ | ◯ | ◯ |
| 39 | `trim_tail` | `bool` | `True` | — | 末尾の平坦域を切る | `find_flattening_point`（:150-184）→ decode 後に切詰（:1396-1417） | 固定 True（:348） | 固定 True（:367） | ◯ | ◯ |
| 40 | `tail_window_size` | `int` | `20` | `max(1, …)`（:1399） | 上記の窓幅 | :156 | — | — | ◯ | ◯ |
| 41 | `tail_std_threshold` | `float` | `0.05` | — | 標準偏差閾値 | :157 | — | — | ◯ | ◯ |
| 42 | `tail_mean_threshold` | `float` | `0.1` | — | 平均閾値 | :158 | — | — | ◯ | ◯ |
| 43 | `lora_adapter` | `str \| None` | `None` | 量子化 ckpt とは非互換（Server が例外文言 `Dynamic LoRA loading is not compatible` で分岐・app.py:754-758） | 実行時 LoRA 差し替え | `_prepare_lora_for_request`（:767-835） | ◯ | ◯ | ◯ | ◯ |
| 44 | （`log_fn`・キーワード引数） | `Callable[[str],None] \| None` | `None` | 文字列 1 本のみ。数値進捗は来ない | ログ出力先 | :1042-1044 | 固定 stdout（:351） | 固定 stdout（:370） | `None`（infer.py:485） | 固定サーバログ（app.py:818-820） |

補足＝プロセス起動時にしか決まらない層（`RuntimeKey`・inference_runtime.py:187-198）。
`checkpoint` / `model_device` / `codec_repo`（既定 `"Aratako/Semantic-DACVAE-Japanese-32dim"`）/ `model_precision`（既定 `"fp32"`）/ `codec_device`（既定 `"cpu"`）/ `codec_precision` / `codec_deterministic_encode` / `codec_deterministic_decode` / `compile_model` / `compile_dynamic`。
**gradio は UI から変えられる（gradio_app.py:427-455）が、Server は起動時の環境変数のみ**（config.py:22-34・`IRODORI_` 前置）。同一プロセスで複数キャラの device を分けることはできない。

### 1-3. 実 checkpoint の既定値（HF キャッシュの safetensors ヘッダを直読・逐語）

`C:/Users/mugonkun/.cache/huggingface/hub/models--Aratako--Irodori-TTS-v4.1-Small/snapshots/2b28324dc263ed5e6638b3cf3dd94c82ead07b4b/model.safetensors` の `__metadata__.config_json` より抜粋（読むだけ・ヘッダのみ）：

```
"use_caption_condition":true,"use_speaker_condition":true,
"caption_dim":512,"speaker_dim":768,"speaker_patch_size":4,"latent_patch_size":1,
"text_tokenizer_repo":"sbintuitions/modernbert-ja-310m","text_encoder_type":"pretrained",
"use_duration_predictor":true,"duration_architecture":"token_sum_dual_adarn_zero_no_aux",
"duration_caption_fusion":"adarn_zero","duration_caption_pooling":"masked_mean",
"max_text_len":256,"max_caption_len":512,"ref_max_seconds":120.0
```

→ v4.1-Small は **3 枝（text＋speaker＋caption）同時**・**caption が duration にも入る**構成であることが重みメタデータ側からも確定。
出力サンプルレートは `int(codec.model.sample_rate)`（codec.py:108）＝コーデック重み由来。README.md:47 が「48kHz waveform reconstruction」と明記（コーデック側 config ファイルはキャッシュに無く `weights.pth` のみ＝**数値の一次確認は未了**）。

---

## 2. 分類

### 2-1. 感情・スタイル

| パラメータ | 形 | 備考 |
|---|---|---|
| `caption` | **自由文（日本語）** | 感情制御の主役。数値でない |
| 本文中の絵文字（`text` の一部） | **自由文中の記号** | 45 種パレット（§3-2）。`normalize_text` は絵文字を落とさない（text_normalization.py:6-24 の置換表に絵文字は無い） |
| `cfg_scale_caption` | 数値 0.0〜10.0 | caption の効き具合 |
| `lora_adapter` | パス | キャラ専用 LoRA を当てる道 |

### 2-2. 話者

`ref_wav` / `ref_wavs` / `ref_latent` / `ref_latents` / `ref_embed` / `no_ref`（6 者排他）、`max_ref_seconds`、`ref_normalize_db`、`ref_ensure_max`、`cfg_scale_speaker`、`speaker_kv_scale`／`speaker_kv_min_t`／`speaker_kv_max_layers`、`speaker_uncond_mode`。

v4.1 の推奨＝**同一話者の短いクリップを複数**（合計 30 秒程度で似せ効果はほぼ飽和・docs/parameters.md:59-63）。1 本の長尺は「受け付けるが未評価」。README.md:150-154 逐語：

> `A single uninterrupted long recording is accepted by inference, but that input format has not been evaluated and may behave differently.`

### 2-3. 韻律

`duration_scale`（話速）、`seconds`（尺の直接指定）、`min_seconds`／`max_seconds`。

**注意**＝話速の専用ノブは `duration_scale` しか無く、これは「予測尺の倍率」＝ピッチは変えずに尺だけ伸縮する。Server の OpenAI 互換 `speed` は `duration_scale / speed` に写される（app.py:842-847）＝**speed>1 で短く＝速い**。
caption も尺予測に入る（model.py:1340-1362）ので「早口で」等は caption 側でも動く。

### 2-4. サンプリング

`num_steps`、`t_schedule_mode`、`sway_coeff`、`cfg_scale_text`、`cfg_guidance_mode`、`cfg_scale`、`cfg_min_t`／`cfg_max_t`、`seed`、`truncation_factor`、`rescale_k`／`rescale_sigma`、`context_kv_cache`、`num_candidates`、`decode_mode`。

### 2-5. テキスト

`text`、`max_text_len`、`max_caption_len`。正規化は `normalize_text`（text_normalization.py:60-74）＝タブ・`[n]`・全角空白を削除、`？→?` `！→!` `♥→♡`、`～/〜→ー`、外側の括弧剥がし、NFKC、`...`→`…`。

- **音素化の段は無い**（G2P なし）＝ModernBERT トークナイザに直接入る。読みの制御手段は本文の書き換えだけ。
- chunking は上流 `Irodori-TTS` に**存在しない**（`grep -rn chunk` は `torch.chunk` と LoRA の文字列分割のみ）＝Server 専用機能（§4-3）。
- 言語切替の引数も無い（日本語専用）。

### 2-6. 出力

`sample_rate`（結果側の read-only・codec 由来）、`trim_tail`＋`tail_*`、透かし。

**透かしは引数が無い**＝`silentcipher` が import でき・モデルが落とせれば**常に**掛かる（inference_runtime.py:619・1433-1441）。payload は固定 `(73, 82, 68, 84, 83)`＝`"IRDTS"`（watermark.py:10）。落とせない場合は警告メッセージを返して素通し（:1443-1447）。off にする正規の手段は上流に無い。

---

## 3. caption の文法（v4/v4.1 が期待する書き方）

### 3-1. caption ＝ 自由文の日本語描写（属性タグではない）

上流の逐語例（すべて自然文）：

| 出典 | 逐語 |
|---|---|
| `upstream/Irodori-TTS/README.md:118` | `--caption "落ち着いた女性の声で、近い距離感でやわらかく自然に読み上げてください。"` |
| `upstream/Irodori-TTS/README.md:130` | `--caption "深く傷つき、今にも泣き出しそうな様子。声が震えており、悲痛なトーンで弱々しく話す。"` |
| `upstream/Irodori-TTS/README.md:140` | `--caption "落ち着いた自然な声"` |
| `upstream/Irodori-TTS/README.md:222` | `--caption "落ち着いた、近い距離感の女性話者"` |
| `upstream/Irodori-TTS/README.md:231` | `--caption "余裕のある大人の男性。親しい相手に対して、くだけた雰囲気で呆れながらも楽しそうに話している。"` |
| `upstream/Irodori-TTS-Server/README.md:207` | `"caption": "落ち着いた低めの女性の声。丁寧で穏やかな話し方。"` |
| `upstream/Irodori-TTS-Server/README.md:226` | `"caption": "明るく元気で、楽しそうな話し方。"` |

読み取れる構文の癖（＝断定でなく**帰納**）：

- 1〜2 文。**属性を「、」で並べ、末尾を「〜話し方。」「〜話す。」で締める**形が多い。
- 情報の並び＝〔年齢・性別・音高〕→〔距離感／場面〕→〔感情・態度〕→〔話し方〕。
- 「読み上げてください」という命令形も、体言止めの「女性話者」も両方出てくる＝**厳密な文法は無い**。
- docs/parameters.md:39 の定義は逐語で `Voice and style-control text for v4-Small and other VoiceDesign checkpoints. Ignored or ineffective for checkpoints without caption conditioning.` のみ。**語彙表・タグ一覧・感情ラベルの列挙は上流のどこにも無い**（`docs/` には parameters.md 1 本しか無い）。
- caption 上限は 512 トークン（§1-3）＝相当長く書ける。

### 3-2. 絵文字パレット＝45 種（本文テキスト側）

`upstream/Irodori-TTS/irodori_tts/gradio_emoji_palette.py:61-107`（`EmojiPaletteItem(絵文字, ラベル, 説明)` の逐語）。
**貼り先は caption ではなく Text 欄**＝`build_emoji_palette(text, open=False)`（gradio_app.py:449／gradio_app_voicedesign.py:468）。

| 絵文字 | ラベル | 説明 | 絵文字 | ラベル | 説明 | 絵文字 | ラベル | 説明 |
|---|---|---|---|---|---|---|---|---|
| 👂 | 囁き | 耳元の音 | 😮‍💨 | 吐息 | 溜息、寝息 | ⏸️ | 間 | 沈黙 |
| 🤭 | 笑い | くすくす、含み笑い | 🥵 | 喘ぎ | うめき声、唸り声 | 📢 | エコー | リバーブ |
| 😏 | からかう | 甘えるように | 🥺 | 震え声 | 自信なさげに | 🌬️ | 息切れ | 荒い息遣い、呼吸音 |
| 😮 | 息をのむ | Gasp | 👅 | 舐める音 | 咀嚼音、水音 | 💋 | リップノイズ | Lip smack |
| 🫶 | 優しく | Tenderly | 😭 | 泣き声 | 嗚咽、悲しみ | 😱 | 悲鳴 | 叫び、絶叫 |
| 😪 | 眠そう | 気だるげに | 😴 | 寝言 | いびき | ⏩ | 早口 | 一気に、急いで |
| 📞 | 電話越し | スピーカー越し | 🐢 | ゆっくり | Slowly | 🥤 | 飲み込む | 唾を飲む音 |
| 🤧 | 咳・鼻 | 咳き込み、鼻すすり | 😒 | 舌打ち | Tutting | 😰 | 慌てる | 動揺、緊張、どもり |
| 😆 | 喜び | 嬉しそうに | 💥 | 勢いよく | 力強い勢い | 😠 | 怒り | 不満げ、拗ねる |
| 😲 | 驚き | 感嘆 | 🥱 | あくび | Yawn | 😖 | 苦しげ | Agonizingly |
| 😟 | 心配 | 不安そうに | 🫣 | 照れ | 恥ずかしそうに | 🙄 | 呆れ | Exasperatedly |
| 😊 | 楽しげ | 嬉しそうに | 😎 | 得意げ | 自信ありげに | 👌 | 相槌 | 頷く音 |
| 🙏 | 懇願 | お願いするように | 🥴 | 酔う | Drunkenly | 🎵 | 鼻歌 | Humming |
| 🤐 | 口を塞ぐ | Muffled | 😌 | 安堵 | 満足げに | 🤔 | 疑問 | Questioning |
| 💪 | 力強く | 力を込めて | 👃 | 嗅ぐ音 | 匂いを嗅ぐ音 | 📖 | 朗読 | ナレーション |

使い方の一次資料＝`upstream/Irodori-TTS/README.md:230`（逐語）：

```
  --text "あははっ🤭、それ本当に言ってるの？…😮‍💨まぁ、君らしいけどね。" \
```

→ **絵文字は本文の該当箇所にインラインで挿す**（前置きの一括指定ではなく位置指定）。
README.md:25（逐語）＝`Emoji-based Style Control: Emoji annotations in input text can influence delivery and non-verbal vocal expressions in supported checkpoints`。
論文題も `Irodori-TTS: A Flow Matching-based Text-to-Speech Model with Emoji-driven Style Control`（README.md:570）＝**絵文字が上流の設計上の主役**。

### 3-3. 感情の粒度（どう書けば怒り／悲しみ／囁きになるか）

上流に「粒度の定義」は無い。読める範囲では**三層構造**：

| 層 | 手段 | 粒度 | 効き方 |
|---|---|---|---|
| 発話全体のトーン | `caption` の自由文 | 1 発話単位 | ModernBERT が読んだ意味ベクトルが DiT の joint-attention 全層と duration に入る |
| 局所の演技・非言語音 | 本文中の絵文字 | **文字位置単位** | 本文条件の一部として時系列に入る |
| 効きの強さ | `cfg_scale_caption`（0〜10・既定 3.0／gr-VD 初期値 4.0） | 連続値 | 上げると caption に寄るが不自然化しうる（docs/parameters.md:148-151） |

対応の目安（パレットのラベル＝上流が付けた名前・逐語）：怒り＝😠、悲しみ＝😭／🥺、囁き＝👂、早口＝⏩、ゆっくり＝🐢、笑い＝🤭、驚き＝😲、照れ＝🫣、呆れ＝🙄、朗読＝📖。

**「怒り度 0.7」のような数値の感情軸は存在しない**＝連続値で動かせるのは `cfg_scale_caption`（caption 全体への追従度）と `duration_scale`（尺）だけ。

---

## 4. 実行時の進捗・キャンセル・逐次出力

### 4-1. 進捗

- `irodori_tts/progress.py` は**学習専用**＝クラスは `TrainProgress` 1 つだけ（progress.py:10-22）で `max_steps` / `rank` / `world_size` を取る tqdm ラッパ。**推論経路からは一切呼ばれない**（`grep -n "progress" irodori_tts/inference_runtime.py` は 0 件）。
- 推論の唯一の外向き通知は `log_fn: Callable[[str], None]`（:1040）＝**文字列だけ**。段ごとに 1 行ずつ（`[runtime] tokenize_text: … ms` など・:1198-1199・1233-1234・1373-1375）。**Euler ステップ単位の刻みは来ない**（rf.py に tqdm もコールバックも無い）。
- 事後の内訳は `SamplingResult.stage_timings`（:254）＝`tokenize_text` / `prepare_reference` / `predict_duration` / `sample_rf` / `unpatchify_latent` / `decode_latent` / `silentcipher_watermark`。

### 4-2. キャンセル

- **どこにも無い**＝推論経路に `cancel` / `abort` / `stop_event` に相当するものが 0 件。`synthesize` は `self._infer_lock`（:1180）と `torch.inference_mode()` で 1 本の同期ブロック（:1178-1184）。
- gradio 側にも中断ボタンは無く、`demo.queue(default_concurrency_limit=1)`（gradio_app.py:649／gradio_app_voicedesign.py:667）だけ。
- Server の SSE は `asyncio.shield` で包まれている（app.py:778-785・逐語）：

```python
async def _run_stream_blocking(func: Any, *args: Any, **kwargs: Any) -> Any:
    task = asyncio.create_task(_run_blocking(func, *args, **kwargs))
    try:
        return await asyncio.shield(task)
    except asyncio.CancelledError:
        await task
        raise
```

→ クライアントが切っても**実行中のチャンクは最後まで走り切る**。取り消せるのは「次のチャンクを始めない」ところまで＝**キャンセル粒度＝チャンク**。
- 同時実行は `max_concurrent_synthesis`（既定 1・config.py:37）のセマフォ、待ちは `synthesis_wait_timeout`（既定 300 秒・config.py:38）で 503（app.py:533-544）。

### 4-3. 逐次出力と chunking（Server 専用）

分割規則＝`_split_text_for_speech`（`upstream/Irodori-TTS-Server/src/irodori_openai_tts/app.py:606-635`）。

| 要素 | 実装 | 既定 |
|---|---|---|
| 境界文字 | `CHUNK_BOUNDARIES = frozenset("。、，,．.!！?？\n\r")`（app.py:30・逐語） | — |
| `chunk_min_chars` | 「境界に来たとき、**非空白**文字が閾値以上溜まっていたら切る」（app.py:616-631） | `80`（config.py:69） |
| `first_sentence_chunk_min_chars` | **最初の境界 1 回だけ**別閾値（app.py:624-627）。先頭を短く切って初音までを速くする用 | `None`＝無効（config.py:70） |
| 有効／無効 | `chunking_enabled` | `True`（config.py:68） |
| 抑止条件 | `seconds` を明示すると chunking をスキップ（app.py:561-564） | — |
| 末尾 | 余りは無条件で 1 チャンク（app.py:632-635） | — |

- **読点「、」も境界に入っている**＝80 字を超えたあと最初の読点で切れる。文単位より細かくなりうる。
- 結合＝`audio = torch.cat([_audio_as_channels_first(result.audio) for result in results], dim=-1)`（app.py:675・逐語）**のみ**。**crossfade も無音挿入もフェードも無い**。
- **チャンク間の韻律連続性の担保は皆無**＝各チャンクは独立の `synthesize` 呼び出し。`chunk_request = replace(sampling_request, text=chunk)`（app.py:655）で本文だけ差し替えるので、`seed` を明示していれば全チャンク同一 seed、未指定なら**チャンクごとに別の乱数 seed**（inference_runtime.py:1168-1170）。返す `used_seed` は先頭チャンクのものだけ（app.py:678）。
- 逐次出力＝`stream_format` を SSE にすると `event: audio_chunk` で `index` / `text` / `format` / `media_type` / `audio_base64` / `seed` / `total_to_decode` を 1 チャンクずつ流す（app.py:734-746）。最後に `event: done`（app.py:770）。**先読みには使えるが、フレーム単位のストリーミングではない**。
- 音声の符号化は `encode_audio`（audio.py:31-69）＝`mp3/opus/aac/flac/wav/pcm`。soundfile→torchaudio→ffmpeg のフォールバック。

---

## 5. 「読み分けちゃんの『声の値』欄に写せる形か」

### 5-1. 形の分類

| 形 | パラメータ | UI 案 |
|---|---|---|
| **自由文** | `caption` | 複数行テキスト欄（キャラごと 1 本）。上限 512 トークン |
| **自由文（本文側）** | 絵文字（本文へインライン） | 本文編集側の機能。45 種のパレットを移植できる（§3-2 の表がそのまま使える） |
| **ファイル／パス** | `ref_wav(s)` / `ref_latent(s)` / `ref_embed` / `lora_adapter` | キャラごとに参照音声を登録。**Server はパス文字列しか受けない**（app.py:449-455）＝サーバから読める場所に置くか `/voices` 経由で登録 |
| **真偽** | `no_ref` / `trim_tail` / `context_kv_cache` / `ref_ensure_max` | チェックボックス |
| **列挙** | `cfg_guidance_mode`(3) / `t_schedule_mode`(2) / `decode_mode`(2) / `speaker_uncond_mode`(2) | プルダウン |
| **数値スライダ** | `num_steps`(1-120) / `duration_scale`(0.5-1.5) / `cfg_scale_text`(0-10) / `cfg_scale_caption`(0-10) / `cfg_scale_speaker`(0-10) / `sway_coeff`(-1.0-1.5) / `cfg_min_t` / `cfg_max_t` / `num_candidates`(1-32) | 上流 gradio の範囲・刻みをそのまま採用できる |
| **数値（任意・空欄＝無効）** | `seed` / `seconds` / `truncation_factor` / `rescale_k` / `rescale_sigma` / `speaker_kv_*` / `max_*_len` / `max_ref_seconds` / `min_seconds` / `max_seconds` / `tail_*` | 空欄可のテキスト欄が上流 gradio の作法（gradio_app.py:489-550） |

### 5-2. キャラ固定値として持てるもの（＝「声の値」欄向き）

| 優先 | 値 | 理由 |
|---|---|---|
| 必須 | `ref_wav(s)` または `ref_embed` または `no_ref` | 声そのもの。3 者排他なので UI は択一 |
| 必須 | `caption` | キャラの地の声・話し方の記述 |
| 推奨 | `cfg_scale_speaker`（既定 5.0） | 似せ具合。キャラごとに詰める価値がある |
| 推奨 | `cfg_scale_caption`（既定 3.0／VD 初期値 4.0） | caption の効き。キャラで最適値が違う |
| 推奨 | `num_steps`（既定 40） | 品質／速度のキャラ別トレードオフ |
| 任意 | `cfg_scale_text` / `t_schedule_mode`＋`sway_coeff` / `speaker_kv_scale` / `lora_adapter` / `max_ref_seconds` | 詰め用 |
| 任意 | `duration_scale`（既定 1.0） | キャラの基準話速 |

**注意**＝Server の voice レジストリ（`VoiceSpec`・voices.py:28-36）は**参照音声のパスしか持てない**（`voice_id` と ref 系 6 個のみ）。caption も cfg も seed も**保持できない**。
→ ⒞案（専用制御方式）を採るなら「キャラ＝参照音声＋caption＋数値一式」を**読み分けちゃん側に持ち、毎リクエストで送る**か、薄い層に自前のキャラ辞書を置くかの二択。上流のレジストリは流用できない。

### 5-3. 発話ごとに変えたいもの

| 値 | 理由 |
|---|---|
| `caption`（上書き） | 「今の台詞は怒っている」＝**行ごとの感情**。キャラ既定 caption に追記／差し替えできる設計が要る |
| 本文中の絵文字 | 局所の演技。台詞テキストと不可分 |
| `duration_scale` / `speed` | 台詞ごとの緩急、尺合わせ |
| `seed` | 「もう一度違うテイク」／逆に「同じテイクを再現」 |
| `num_candidates` | 候補出しをしたい場面のみ |
| `seconds` | 尺を厳密に合わせたい場面のみ（＝chunking が切れる副作用に注意・§4-3） |

### 5-4. 「声の値」に写せない／写しても意味が薄いもの

- `model_device` / `model_precision` / `codec_device` / `codec_repo` / `compile_*`（`RuntimeKey`・inference_runtime.py:187-198）＝**プロセス単位**。Server では起動時環境変数のみ（config.py:27-34）。**キャラごとの GPU 指定は現状の Server では不可能**（BRIEF §4-4 の席へ）。
- 透かし＝スイッチが存在しない。
- `tail_*` / `min_seconds` / `max_seconds` / `rescale_*` / `max_*_len` ＝上級者向けで既定のままが推奨（docs/parameters.md:200-206）。「声の値」欄に出すなら折りたたみの奥。

---

## 6. 主席への注意

1. **BRIEF §4-3 の前提を 1 つ更新願う**＝「HTTP API が露出していないパラメータ」は実質 `speaker_uncond_mode` 1 個だけ。`min_seconds` / `max_seconds` / `tail_window_size` / `tail_std_threshold` / `tail_mean_threshold` / `lora_adapter` は逆に **API にはあるが gradio に無い**（`min/max_seconds` は CLI にも無い）。したがって⒞案の値打ちは「未露出パラメータを掘る」ことではなく、**⑴ キャラごとの device 指定、⑵ 進捗・キャンセル、⑶ キャラ辞書（caption＋cfg をサーバ側に保持）、⑷ chunk 境界の制御**の 4 点にある。ここが⒞案の実質。
2. **感情制御の主役は数値でなく自由文と絵文字**。読み分けちゃんの「声の値」欄が数値スライダ前提なら、caption 用の複数行欄と、本文編集側の絵文字パレット（45 種・§3-2 の表がそのまま使える）の 2 箇所に手が要る。＝比較表の「感情パラメータの露出度」の軸は「数値の本数」ではなく「自由文＋絵文字をどう UI に載せるか」で採点した方が実態に合う。
3. **chunk 間の韻律は保証されない**（単純 `torch.cat`・seed も別）。長文の自然さは⒞案で最初に効く改善点＝境界の選び方・チャンク間 seed 固定・crossfade。既定 `chunk_min_chars=80` は「、」でも切れるので、読み分けちゃん側で 1 発話ずつ投げて chunking を切る（`chunking_enabled: false`）方が制御しやすい可能性あり。
4. **キャンセルは現状「次チャンクを始めない」までしか効かない**（`asyncio.shield`）。配信用途の「言い直し」は⒞案の要求に入れるべき。
5. **透かしは常時 ON で off の正規手段が無い**＝配布・再配布の議論（§4-1 ライセンス席）と、レイテンシ（`silentcipher_watermark` 段が `stage_timings` に出る）の両方に効く。SilentCipher は別ライセンスの別モデル（HF キャッシュに `models--sony--silentcipher` あり）＝同梱可否は§4-1 の席へ引き継ぎ。
6. **未確認として残したもの**：
   - 出力サンプルレート 48000 はコーデック重み由来で、README.md:47 の記述しか一次資料が無い（コーデック側 config はキャッシュに無く `weights.pth` のみ）。
   - 稼働中の 8088 が本 clone と同一版かは照合していない。
   - Server の既定 checkpoint は `Aratako/Irodori-TTS-v4-Small`（config.py:23）で **v4.1 ではない**＝実機の 8088 がどちらを載せているかは要確認。docs/parameters.md:5-6 は「`main` targets the unified `Aratako/Irodori-TTS-v4.1-Small` release」と書いており、Server の既定だけ v4 のまま取り残されている。
   - caption の語彙・粒度は上流に定義表が無く、本ノート §3 の記述は例文からの帰納。どの語がどれだけ効くかは CPU 実験（別席）でしか埋まらない。
   - 絵文字が実際に効くかは未実験（コードは本文を素通しするだけで、絵文字専用の処理は無い＝効きは重み側に埋まっている）。
