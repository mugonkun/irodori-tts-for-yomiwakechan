"""ywk_params -- the parameter table that ``GET /params`` serves.

This module holds the definition table only: field name, type, group, range,
Japanese label and description, and *where the effective default comes from*.
The values themselves are resolved at request time by :func:`build_params`
against the live ``Settings`` object (env has already been folded into it) and,
once the checkpoint is loaded, against the live ``InferenceRuntime``.

Sources of fact (read-only; the upstream trees are never modified):

* ``upstream/Irodori-TTS-Server/src/irodori_openai_tts/app.py``
  - ``IrodoriOptions`` (:37-83) -- the 44 fields listed here, in this order.
  - ``SpeechRequest`` (:86-95) -- the top-level fields and ``speed``'s ge/le.
  - ``_build_sampling_request`` (:832-1045) -- which ``settings.default_*``
    feeds which ``SamplingRequest`` field (this is what "effective default"
    means, and it is why ``cfg_scale_caption`` defaults to
    ``settings.default_cfg_scale_text``; see that field's note).
* ``upstream/Irodori-TTS-Server/src/irodori_openai_tts/config.py`` -- the
  ``Settings.default_*`` fields and their values.
* ``upstream/Irodori-TTS/irodori_tts/inference_runtime.py``
  - ``SamplingRequest`` (:201-246) -- dataclass defaults.
  - ``InferenceRuntime.synthesize`` (:1069-1160) -- the only range checks the
    upstream code actually performs; they are quoted in ``range_source``.
  - ``default_text_max_len`` / ``default_caption_max_len`` /
    ``default_max_ref_seconds`` (:603-618, :707-743) -- checkpoint-dependent.
* ``upstream/Irodori-TTS/gradio_app.py`` (:481-531) and
  ``gradio_app_voicedesign.py`` (:535-548) -- the only machine-readable
  min/max/step in the upstream tree.  They are UI slider bounds, **not**
  server-side validation: the upstream HTTP API checks no ranges at all
  (``research/lab/notes/37-handoff-contract.md`` S-5/S-6).
* ``upstream/Irodori-TTS/docs/parameters.md`` -- the prose the Japanese
  descriptions below are written from (自作・MIT).

``default_source`` tells the caller how the ``default`` value was obtained and,
crucially, whether a ``null`` default is a fact or a bug:

===================  =========================================================
``settings``         ``settings.default_*`` -- always a concrete value.
``dataclass``        ``SamplingRequest`` dataclass default -- concrete value.
``ywk``              the distribution picks the default itself (design §4-2 ⑵:
                     the fields the 本体 shows must not carry ``null``).
``settings_optional````settings.default_*`` whose own type is ``| None``; the
                     upstream default is "unset", so ``null`` is the fact.
``checkpoint``       filled in from the loaded checkpoint; ``null`` until then.
``unset``            no default at all -- omit the field to get upstream
                     behaviour.  ``null`` is the fact.
===================  =========================================================

Two more machine-readable flags ride on every field (design §4-2 ⑴⑵):

``exposed_to_ywk``   true only for the nine fields the 本体 turns into a
                     ``ParamDescriptor`` (:data:`EXPOSED_TO_YWK`).  The
                     acceptance condition A-5 -- "``default: null`` 0 件" -- is
                     shot at **this set**; the rest are the ``advanced`` group
                     the distribution's own UI shows.
``nullable``         true when omitting the field (or sending ``null``) means
                     "upstream default", i.e. for every source above that can
                     report ``null`` plus the two ``ywk`` fields whose ``""``
                     means the same thing.

``tests/contract/test_params.py`` nails all of this down.
"""

from __future__ import annotations

from typing import Any

SCHEMA = 1
ENGINE = "irodori-ywk"

#: Fields whose upstream type is ``typing.Literal``.  FastAPI/pydantic only
#: validates them when they arrive inside the ``irodori`` object; sent at the
#: top level they land in ``model_extra`` and are passed through unchecked
#: (``research/lab/notes/37-handoff-contract.md`` S-2).  The wrapper therefore
#: refuses them at the top level.
LITERAL_NESTED_ONLY = ("t_schedule_mode", "decode_mode", "cfg_guidance_mode")

#: The only ``response_format`` the distribution supports.  mp3/opus/aac would
#: need an ffmpeg binary, and the distribution ships no third-party binaries.
RESPONSE_FORMATS = ("wav",)

#: ``irodori`` alias for "参照なし".  ``voices.json`` maps it to ``no_ref``.
DEFAULT_VOICE_ID = "デフォルト"

#: ``/ywk/status.variant`` -- the build the launcher started, not a capability.
#: ``cuda`` is the default (decisions.md 4); the Radeon build names its arch
#: (``rocm-gfx1151``) because that is the only one measured (decisions.md 5).
DEFAULT_VARIANT = "cuda"

#: ``/ywk/status.warmup.state`` (便 C 設計書 §2-3).  ``cancelled`` is 便 C's
#: addition to the four the design lists: ``DELETE /ywk/warmup/{id}`` has to be
#: distinguishable from a run that finished its plan.
WARMUP_STATES = ("idle", "running", "done", "failed", "cancelled")

#: Every key ``/ywk/status.warmup`` carries.  ``shots`` is ``shots_done`` under
#: the name 便 A's contract ⑹ already promised the launcher.
WARMUP_KEYS = (
    "state",
    "id",
    "shots_done",
    "shots_total",
    "elapsed_s",
    "last_shot",
    "error",
    "shots",
)

#: Every key ``/ywk/status.device`` carries.  ``hip`` and ``gcn_arch`` are 便 C's:
#: ROCm's torch calls its device type ``cuda`` too, and these two are what tell
#: a gfx1151 from an RTX (both are ``null`` on a CUDA build / on cpu).
STATUS_DEVICE_KEYS = (
    "configured",
    "codec_configured",
    "actual",
    "precision",
    "name",
    "uuid",
    "pci_bus_id",
    "hip",
    "gcn_arch",
)

#: The upstream's own "no reference" spellings (``voices.py:23`` ``NO_REF_IDS``).
#: ``allow_no_ref_voice=false`` is baked in, so the upstream would 400 every one
#: of them; the wrapper normalises them to :data:`DEFAULT_VOICE_ID` instead so
#: the 本体's current adapter (``voice:"none"`` -- contract ⑼ D-4) keeps working.
NO_REF_VOICE_IDS = frozenset({"none", "no_ref", "no-ref", "null", "text-only"})

#: 設計書 §4-2 ⑵ -- the fields the 本体 turns into ``ParamDescriptor`` entries.
#: Everything else is the ``advanced`` group: the distribution's own UI may show
#: it, the 本体 does not.  ``voice`` and ``speed`` live in ``request`` (below).
EXPOSED_TO_YWK = frozenset(
    {
        "caption",
        "seed",
        "num_steps",
        "cfg_scale_text",
        "cfg_scale_caption",
        "cfg_scale_speaker",
        "t_schedule_mode",
        "sway_coeff",
    }
)

#: The top-level counterpart (``voice`` is a Choice fed by ``/ywk/voices``).
EXPOSED_REQUEST_KEYS = frozenset({"voice", "speed"})

#: ``default_source`` values whose ``default`` may legitimately be ``null``.
NULLABLE_SOURCES = frozenset({"unset", "settings_optional", "checkpoint"})

#: Fields the upstream reads with ``_explicit_option`` (``app.py:1089-1095``),
#: which consults ``model_fields_set``: an **explicit ``null``** there counts as
#: "the caller specified it" and therefore beats the env default, unlike the 41
#: fields that go through ``_coalesce``.  ``/params.rules.null_means_unset`` is
#: only true because the wrapper deletes these three when they arrive as
#: ``null`` (design §4-2 note; ``ywk_server.normalize_speech_body``).
EXPLICIT_NULL_WINS = ("ref_normalize_db", "max_ref_seconds", "first_sentence_chunk_min_chars")

#: The five reference fields the upstream checks *before* it resolves ``voice``
#: (``app.py:437-454``): if any one of them is present the speaker the caller
#: named is dropped without a word.  ``no_ref`` is the sixth.  The wrapper
#: refuses the combination instead (contract ⑷ 4-2).
REFERENCE_KEYS = ("ref_wav", "ref_wavs", "ref_latent", "ref_latents", "ref_embed")

#: String fields whose ``""`` the wrapper folds away instead of forwarding: the
#: upstream 400s an empty ``caption`` and would type-error an empty ``seed``,
#: yet ``""`` is exactly what ``/params`` reports as their default (design §4-2
#: ⑵ -- an exposed field must not carry ``default: null``).
EMPTY_STRING_MEANS_UNSET = ("caption", "seed")

_G_TEXT = "text"
_G_REF = "reference"
_G_DURATION = "duration"
_G_QUALITY = "quality"
_G_EMOTION = "emotion"
_G_SPEAKER = "speaker"
_G_ADVANCED = "advanced"
_G_CHUNKING = "chunking"

_SRC_GRADIO = "上流 gradio_app.py のスライダ（UI の範囲であって検査ではない）"
_SRC_GRADIO_VD = "上流 gradio_app_voicedesign.py のスライダ（UI の範囲であって検査ではない）"
_SRC_YWK = "配布版の推奨（上流の HTTP API に範囲検査はない）"
_SRC_RUNTIME = "上流 inference_runtime.py の検査"
_SRC_SERVER = "上流 app.py の検査"

#: ``IrodoriOptions`` の 44 欄。並びは app.py:40-83 のとおり。
IRODORI_PARAMS: tuple[dict[str, Any], ...] = (
    {
        "key": "caption",
        "type": "string",
        "group": _G_EMOTION,
        "label": "演技指示（キャプション）",
        "description": (
            "声質・話し方を日本語の文で指示する。v4.1-Small のキャプション条件付けに渡る。"
            "空文字＝未指定（既定に戻る）。"
        ),
        "max_length": 2048,
        "range_source": _SRC_YWK,
        "default_source": "ywk",
        "ywk_default": "",
        "nullable": True,
        "note": (
            "空文字・空白のみ＝未指定＝wrapper が欄ごと省略して上流へ渡す"
            "（上流は空文字を 400 にするので wrapper が畳む・設計書 §4-2 ⑵）"
        ),
    },
    {
        "key": "ref_wav",
        "type": "string",
        "group": _G_REF,
        "label": "参照音声のパス",
        "description": "参照ボイスの音声檔のパス。通常は本体ではなく配布版の UI が使う（本体は voice 名で選ぶ）。",
        "max_length": 4096,
        "range_source": _SRC_YWK,
        "default_source": "unset",
        "note": "voice で話者を選ぶ経路が本命。docs/contract.md §4 を見よ",
    },
    {
        "key": "ref_wavs",
        "type": "array",
        "items": "string",
        "group": _G_REF,
        "label": "参照音声のパス（複数）",
        "description": (
            "同一話者の短い檔を並べて渡す。順に符号化して連結し max_ref_seconds で頭打ちにする。"
            "ref_wav とは併用できない。"
        ),
        "default_source": "unset",
    },
    {
        "key": "ref_latent",
        "type": "string",
        "group": _G_REF,
        "label": "参照潜在のパス",
        "description": "あらかじめ符号化した参照潜在（.pt）。配布版は事前計算キャッシュを持たない（裁定 11）。",
        "max_length": 4096,
        "range_source": _SRC_YWK,
        "default_source": "unset",
    },
    {
        "key": "ref_latents",
        "type": "array",
        "items": "string",
        "group": _G_REF,
        "label": "参照潜在のパス（複数）",
        "description": "参照潜在を順に連結する。ref_latent とは併用できない。",
        "default_source": "unset",
    },
    {
        "key": "ref_embed",
        "type": "string",
        "group": _G_REF,
        "label": "話者埋め込みのパス",
        "description": (
            "Speaker Inversion で学習した .speaker.safetensors。"
            "波形・潜在の参照および no_ref とは併用できない。"
        ),
        "max_length": 4096,
        "range_source": _SRC_YWK,
        "default_source": "unset",
    },
    {
        "key": "no_ref",
        "type": "boolean",
        "group": _G_REF,
        "label": "参照なしで合成",
        "description": (
            "話者条件付けを切る。配布版では話者名「デフォルト」がこれに当たる。"
            "voice と同時に指定すると 400。"
        ),
        "default_source": "dataclass",
        "dataclass_default": False,
        "note": "通常は voice=\"デフォルト\" を使う（voice と排他）",
    },
    {
        "key": "seconds",
        "type": "number",
        "group": _G_DURATION,
        "label": "出力の長さ（秒・手動）",
        "description": (
            "出力尺を手で決める。指定すると尺予測より優先され、"
            "上流はチャンク分割を黙って無効にする（上流 app.py:562-564）。"
        ),
        "min": 0.1,
        "max": 600.0,
        "step": 0.1,
        "range_source": _SRC_RUNTIME + "（seconds > 0・:1069）＋" + _SRC_YWK + "の上限",
        "default_source": "unset",
        "note": "指定すると chunking_enabled が無効になる",
    },
    {
        "key": "duration_scale",
        "type": "number",
        "group": _G_DURATION,
        "label": "尺の倍率",
        "description": (
            "尺予測の結果に掛ける倍率。1.0 より大きいと長く、小さいと短くなる。"
            "top-level の speed を同時に送ると duration_scale / speed に合成される。"
        ),
        "min": 0.5,
        "max": 1.5,
        "step": 0.01,
        "range_source": _SRC_GRADIO + "（:491-497）",
        "default_source": "settings",
        "settings_key": "default_duration_scale",
    },
    {
        "key": "min_seconds",
        "type": "number",
        "group": _G_DURATION,
        "label": "出力の最短（秒）",
        "description": "予測・手動いずれの尺もこの値で下から抑える。",
        "min": 0.1,
        "max": 600.0,
        "step": 0.1,
        "range_source": _SRC_RUNTIME + "（min_seconds > 0・:1075）＋" + _SRC_YWK + "の上限",
        "default_source": "settings",
        "settings_key": "default_min_seconds",
    },
    {
        "key": "max_seconds",
        "type": "number",
        "group": _G_DURATION,
        "label": "出力の最長（秒）",
        "description": "予測・手動いずれの尺もこの値で上から抑える。min_seconds 未満だと 400。",
        "min": 0.1,
        "max": 600.0,
        "step": 0.1,
        "range_source": _SRC_RUNTIME + "（max_seconds >= min_seconds・:1077）＋" + _SRC_YWK + "の上限",
        "default_source": "settings",
        "settings_key": "default_max_seconds",
    },
    {
        "key": "max_ref_seconds",
        "type": "number",
        "group": _G_REF,
        "label": "参照音声の上限（秒）",
        "description": (
            "参照の長さの頭打ち。未指定のときは checkpoint の ref_max_seconds"
            "（v4.1-Small は 120 秒・metadata の無い旧 checkpoint は 30 秒）。"
        ),
        "min": 0.0,
        "max": 600.0,
        "step": 1.0,
        "range_source": _SRC_YWK,
        "default_source": "checkpoint",
        "settings_key": "default_max_ref_seconds",
        "runtime_attr": "default_max_ref_seconds",
        "note": (
            "0 以下で上限を外す（上流 docs/parameters.md の記述）。"
            "上流は明示 null を「指定された」と読む（app.py:904-911 の _explicit_option）＝"
            "配布版は明示 null を欄ごと削って「null＝未指定」を成立させる"
        ),
    },
    {
        "key": "ref_normalize_db",
        "type": "number",
        "group": _G_REF,
        "label": "参照音声のラウドネス正規化（dB）",
        "description": (
            "DACVAE で符号化する前に参照音声を揃える目標ラウドネス。"
            "コーデックの学習時と同じ値なので既定のままが推奨。"
        ),
        "min": -60.0,
        "max": 0.0,
        "step": 0.5,
        "range_source": _SRC_YWK,
        "default_source": "settings",
        "settings_key": "default_ref_normalize_db",
        "note": (
            "上流は明示 null を「指定された」と読む（app.py:862-869 の _explicit_option）＝"
            "null を送ると正規化が切れて出る音が変わる。配布版は明示 null を欄ごと削って"
            "「null＝未指定」を成立させる"
        ),
    },
    {
        "key": "ref_ensure_max",
        "type": "boolean",
        "group": _G_REF,
        "label": "参照音声のピーク保護",
        "description": "ラウドネス正規化を切ったときだけ効く。ピークが 1.0 を超える参照だけ下げる。",
        "default_source": "settings",
        "settings_key": "default_ref_ensure_max",
    },
    {
        "key": "num_steps",
        "type": "integer",
        "group": _G_QUALITY,
        "label": "サンプリング歩数",
        "description": (
            "Euler 積分の歩数。多いほど遅く、ある点までは安定する。"
            "速さを取るなら t_schedule_mode=sway と組み合わせて減らす。"
        ),
        "min": 1,
        "max": 120,
        "step": 1,
        "range_source": _SRC_GRADIO + "（:481・minimum=1 maximum=120 step=1）",
        "default_source": "settings",
        "settings_key": "default_num_steps",
        "presets": (10, 40),
        "note": (
            "上流の HTTP API に範囲検査は無い。範囲は gradio のスライダ由来"
            "（設計書 §4-2 ⑶）。配布版の実用域は presets の 10／40"
        ),
    },
    {
        "key": "t_schedule_mode",
        "type": "enum",
        "enum": ("linear", "sway"),
        "group": _G_QUALITY,
        "label": "時刻スケジュール",
        "description": "RF Euler の時刻の刻み方。sway は Sway Sampling を有効にする。",
        "default_source": "settings",
        "settings_key": "default_t_schedule_mode",
        "note": "Literal 欄＝irodori ネストでのみ受ける（top-level は 400）",
    },
    {
        "key": "sway_coeff",
        "type": "number",
        "group": _G_QUALITY,
        "label": "Sway 係数",
        "description": "Sway Sampling の係数。負の値ほど雑音側に刻みを寄せる。t_schedule_mode=sway のときだけ効く。",
        "min": -1.0,
        "max": 1.5,
        "step": 0.1,
        "range_source": _SRC_GRADIO + "（:505-512）",
        "default_source": "settings",
        "settings_key": "default_sway_coeff",
    },
    {
        "key": "num_candidates",
        "type": "integer",
        "group": _G_QUALITY,
        "label": "候補数",
        "description": "1 回のサンプリングで作る候補の数。増やすと VRAM を食う。配布版は 1 本目だけを返す。",
        "min": 1,
        "max": 32,
        "step": 1,
        "range_source": _SRC_GRADIO + "（:482-488・MAX_GRADIO_CANDIDATES=32）",
        "default_source": "settings",
        "settings_key": "default_num_candidates",
    },
    {
        "key": "decode_mode",
        "type": "enum",
        "enum": ("sequential", "batch"),
        "group": _G_QUALITY,
        "label": "復号のまとめ方",
        "description": "sequential は候補を 1 本ずつ復号し VRAM を節約する。batch はまとめて復号し速いことがある。",
        "default_source": "settings",
        "settings_key": "default_decode_mode",
        "note": "Literal 欄＝irodori ネストでのみ受ける（top-level は 400）",
    },
    {
        "key": "cfg_scale_text",
        "type": "number",
        "group": _G_EMOTION,
        "label": "CFG 強度（本文）",
        "description": "本文条件の効き。高いほど原稿に忠実になるが、上げすぎると不自然になる。滑舌が弱いときに少し上げる。",
        "min": 0.0,
        "max": 10.0,
        "step": 0.1,
        "range_source": _SRC_GRADIO + "（:520-526）",
        "default_source": "settings",
        "settings_key": "default_cfg_scale_text",
    },
    {
        "key": "cfg_scale_caption",
        "type": "number",
        "group": _G_EMOTION,
        "label": "CFG 強度（演技指示）",
        "description": "キャプション条件の効き。caption を使うときだけ意味がある。",
        "min": 0.0,
        "max": 10.0,
        "step": 0.1,
        "range_source": _SRC_GRADIO_VD + "（:538-544）",
        "default_source": "settings",
        "settings_key": "default_cfg_scale_text",
        "note": (
            "上流 Server は既定に settings.default_cfg_scale_text を渡す"
            "（app.py:932-939）＝実効 3.0。gradio VoiceDesign のスライダ既定は 4.0"
            "（gradio_app_voicedesign.py:538-544）で、値が食い違う"
        ),
    },
    {
        "key": "cfg_scale_speaker",
        "type": "number",
        "group": _G_SPEAKER,
        "label": "CFG 強度（話者）",
        "description": "参照話者条件の効き。参照なし（no_ref）では無視される。似ないときにまず上げる欄。",
        "min": 0.0,
        "max": 10.0,
        "step": 0.1,
        "range_source": _SRC_GRADIO + "（:527-533）",
        "default_source": "settings",
        "settings_key": "default_cfg_scale_speaker",
    },
    {
        "key": "cfg_guidance_mode",
        "type": "enum",
        "enum": ("independent", "joint", "alternating"),
        "group": _G_ADVANCED,
        "label": "CFG の組み方",
        "description": (
            "independent は条件ごとに無条件枝を持つ（自由だが計算量が増える）。"
            "joint は全条件をまとめて落とし、有効な CFG 強度が揃っていることを要求する。"
            "alternating は歩ごとに落とす条件を替える。"
        ),
        "default_source": "settings",
        "settings_key": "default_cfg_guidance_mode",
        "note": "Literal 欄＝irodori ネストでのみ受ける（top-level は 400）",
    },
    {
        "key": "cfg_scale",
        "type": "number",
        "group": _G_ADVANCED,
        "label": "CFG 強度（一括上書き・非推奨）",
        "description": "本文・演技指示・話者の CFG 強度をまとめて上書きする旧欄。個別の欄を使うほうがよい。",
        "min": 0.0,
        "max": 10.0,
        "step": 0.1,
        "range_source": _SRC_GRADIO + "（個別欄と同じ 0〜10）",
        "default_source": "unset",
    },
    {
        "key": "cfg_min_t",
        "type": "number",
        "group": _G_ADVANCED,
        "label": "CFG を効かせる下限 t",
        "description": "この時刻以上でだけ CFG を効かせる。",
        "min": 0.0,
        "max": 1.0,
        "step": 0.01,
        "range_source": _SRC_YWK + "（t は [0,1]。上流 gradio は gr.Number で範囲なし）",
        "default_source": "settings",
        "settings_key": "default_cfg_min_t",
    },
    {
        "key": "cfg_max_t",
        "type": "number",
        "group": _G_ADVANCED,
        "label": "CFG を効かせる上限 t",
        "description": "この時刻以下でだけ CFG を効かせる。",
        "min": 0.0,
        "max": 1.0,
        "step": 0.01,
        "range_source": _SRC_YWK + "（t は [0,1]。上流 gradio は gr.Number で範囲なし）",
        "default_source": "settings",
        "settings_key": "default_cfg_max_t",
    },
    {
        "key": "truncation_factor",
        "type": "number",
        "group": _G_ADVANCED,
        "label": "初期雑音の切り詰め",
        "description": "サンプリング前のガウス雑音を縮める。0.8〜0.9 でばらつきが減るが表現も痩せる。",
        "min": 0.1,
        "max": 2.0,
        "step": 0.01,
        "range_source": _SRC_RUNTIME + "（truncation_factor > 0・:1117）＋" + _SRC_YWK + "の上限",
        "default_source": "unset",
    },
    {
        "key": "rescale_k",
        "type": "number",
        "group": _G_ADVANCED,
        "label": "スコア再スケール k",
        "description": "時間方向のスコア再スケール。rescale_sigma と必ず対で指定する（片方だけは 400）。",
        "min": 0.01,
        "max": 10.0,
        "step": 0.01,
        "range_source": _SRC_RUNTIME + "（rescale_k > 0・:1121）＋" + _SRC_YWK + "の上限",
        "default_source": "unset",
    },
    {
        "key": "rescale_sigma",
        "type": "number",
        "group": _G_ADVANCED,
        "label": "スコア再スケール sigma",
        "description": "時間方向のスコア再スケール。rescale_k と必ず対で指定する（片方だけは 400）。",
        "min": 0.01,
        "max": 10.0,
        "step": 0.01,
        "range_source": _SRC_RUNTIME + "（rescale_sigma > 0・:1123）＋" + _SRC_YWK + "の上限",
        "default_source": "unset",
    },
    {
        "key": "context_kv_cache",
        "type": "boolean",
        "group": _G_ADVANCED,
        "label": "条件 K/V の事前計算",
        "description": "本文・話者・演技指示の K/V 射影を先に計算してサンプリングを速くする。通常は有効のまま。",
        "default_source": "settings",
        "settings_key": "default_context_kv_cache",
    },
    {
        "key": "speaker_kv_scale",
        "type": "number",
        "group": _G_SPEAKER,
        "label": "話者 K/V の強調",
        "description": (
            "話者文脈の K/V 射影に掛ける追加倍率。1.0 より大きいと話者らしさが強まる。"
            "参照ありの checkpoint でだけ意味がある実験的な欄。"
        ),
        "min": 0.01,
        "max": 5.0,
        "step": 0.01,
        "range_source": _SRC_RUNTIME + "（speaker_kv_scale > 0・:1141）＋" + _SRC_YWK + "の上限",
        "default_source": "unset",
    },
    {
        "key": "speaker_kv_min_t",
        "type": "number",
        "group": _G_SPEAKER,
        "label": "話者 K/V を効かせる下限 t",
        "description": "この時刻以上でだけ話者 K/V の強調を効かせる。",
        "min": 0.0,
        "max": 1.0,
        "step": 0.01,
        "range_source": _SRC_RUNTIME + "（speaker_kv_min_t は [0,1]・:1145-1146）",
        "default_source": "unset",
        "note": (
            "speaker_kv_scale を指定したときだけ 0.9 に解決される"
            "（inference_runtime.py:1142-1143）。単独指定は何も効かない"
        ),
    },
    {
        "key": "speaker_kv_max_layers",
        "type": "integer",
        "group": _G_SPEAKER,
        "label": "話者 K/V を効かせる層数",
        "description": "話者 K/V の強調を先頭 N 層に限る。",
        "min": 0,
        "max": 128,
        "step": 1,
        "range_source": _SRC_RUNTIME + "（speaker_kv_max_layers >= 0・:1148-1149）＋" + _SRC_YWK + "の上限",
        "default_source": "unset",
    },
    {
        "key": "seed",
        "type": "integer",
        "group": _G_QUALITY,
        "label": "乱数の種",
        "description": (
            "同じ checkpoint・同じパラメータなら同じ音が出る。未指定だとチャンクごとに別の種になり、"
            "応答 header の X-Irodori-Seed には先頭チャンクの種しか載らない。"
        ),
        "min": 0,
        "max": 9223372036854775807,
        "step": 1,
        "range_source": _SRC_YWK + "（上流は secrets.randbits(63) で選ぶ）",
        "default_source": "ywk",
        "ywk_default": "",
        "nullable": True,
        "note": (
            "空文字＝未指定＝毎回の乱数（wrapper が欄ごと省略する）。"
            "設計書 §4-2 ⑵ の「seed は \"\" で乱数」"
        ),
    },
    {
        "key": "trim_tail",
        "type": "boolean",
        "group": _G_ADVANCED,
        "label": "末尾の無音を刈る",
        "description": "末尾の平坦な潜在を刈る。尺予測のある v4.1-Small では効きが小さい。切りすぎるときはまずこれを切る。",
        "default_source": "settings",
        "settings_key": "default_trim_tail",
    },
    {
        "key": "tail_window_size",
        "type": "integer",
        "group": _G_ADVANCED,
        "label": "末尾刈りの窓幅",
        "description": "末尾刈りの判定に使う窓の幅（潜在フレーム数）。",
        "min": 1,
        "max": 200,
        "step": 1,
        "range_source": _SRC_YWK,
        "default_source": "settings",
        "settings_key": "default_tail_window_size",
    },
    {
        "key": "tail_std_threshold",
        "type": "number",
        "group": _G_ADVANCED,
        "label": "末尾刈りの標準偏差しきい値",
        "description": "末尾刈りの判定に使う標準偏差のしきい値。",
        "min": 0.0,
        "max": 1.0,
        "step": 0.01,
        "range_source": _SRC_YWK,
        "default_source": "settings",
        "settings_key": "default_tail_std_threshold",
    },
    {
        "key": "tail_mean_threshold",
        "type": "number",
        "group": _G_ADVANCED,
        "label": "末尾刈りの平均しきい値",
        "description": "末尾刈りの判定に使う平均のしきい値。",
        "min": 0.0,
        "max": 1.0,
        "step": 0.01,
        "range_source": _SRC_YWK,
        "default_source": "settings",
        "settings_key": "default_tail_mean_threshold",
    },
    {
        "key": "max_text_len",
        "type": "integer",
        "group": _G_TEXT,
        "label": "本文のトークン上限",
        "description": "本文トークンの上限。超えた分は切り捨てられる。学習時の値のままが推奨。",
        "min": 1,
        "max": 4096,
        "step": 1,
        "range_source": _SRC_RUNTIME + "（max_text_len > 0・:1099）＋" + _SRC_YWK + "の上限",
        "default_source": "checkpoint",
        "runtime_attr": "default_text_max_len",
    },
    {
        "key": "max_caption_len",
        "type": "integer",
        "group": _G_EMOTION,
        "label": "演技指示のトークン上限",
        "description": "キャプショントークンの上限。未指定なら checkpoint の値（無ければ max_text_len と同じ）。",
        "min": 1,
        "max": 4096,
        "step": 1,
        "range_source": _SRC_RUNTIME + "（max_caption_len > 0・:1106）＋" + _SRC_YWK + "の上限",
        "default_source": "checkpoint",
        "runtime_attr": "default_caption_max_len",
    },
    {
        "key": "lora_adapter",
        "type": "string",
        "group": _G_ADVANCED,
        "label": "LoRA アダプタのディレクトリ",
        "description": "推論時に読み込む PEFT LoRA アダプタ。配布版は peft を同梱しないので通常は使えない。",
        "max_length": 4096,
        "range_source": _SRC_YWK,
        "default_source": "unset",
        "note": "配布版の取得台帳は peft を外している（ledger/README.md）",
    },
    {
        "key": "chunking_enabled",
        "type": "boolean",
        "group": _G_CHUNKING,
        "label": "長文の自動分割",
        "description": (
            "句読点で本文を切って順に合成し、つなげて返す。"
            "seconds を明示すると上流は黙って分割を止める。"
        ),
        "default_source": "settings",
        "settings_key": "default_chunking_enabled",
    },
    {
        "key": "chunk_min_chars",
        "type": "integer",
        "group": _G_CHUNKING,
        "label": "分割の最小文字数",
        "description": "1 チャンクに最低これだけの文字が入るまで区切りで切らない。",
        "min": 1,
        "max": 4096,
        "step": 1,
        "range_source": _SRC_RUNTIME + "（chunk_min_chars > 0・app.py:575）＋" + _SRC_YWK + "の上限",
        "default_source": "settings",
        "settings_key": "default_chunk_min_chars",
    },
    {
        "key": "first_sentence_chunk_min_chars",
        "type": "integer",
        "group": _G_CHUNKING,
        "label": "先頭チャンクの最小文字数",
        "description": "最初のチャンクだけ短く切って喋り出しを早める。未指定なら chunk_min_chars と同じ扱い。",
        "min": 1,
        "max": 4096,
        "step": 1,
        "range_source": _SRC_RUNTIME + "（> 0・app.py:585-589）＋" + _SRC_YWK + "の上限",
        "default_source": "settings_optional",
        "settings_key": "default_first_sentence_chunk_min_chars",
        "note": (
            "上流は明示 null を「指定された」と読む（app.py:577-584 の _explicit_option）＝"
            "配布版は明示 null を欄ごと削って「null＝未指定」を成立させる"
        ),
    },
)

#: top-level（``SpeechRequest``）の欄。``irodori`` は別扱い。
REQUEST_PARAMS: tuple[dict[str, Any], ...] = (
    {
        "key": "model",
        "type": "string",
        "label": "モデル名",
        "description": "settings.model_name と一致しなければ 400。既定は 'irodori-tts'。",
        "max_length": 256,
        "range_source": _SRC_YWK,
        "default_source": "settings",
        "settings_key": "model_name",
        "required": True,
    },
    {
        "key": "input",
        "type": "string",
        "label": "本文",
        "description": "読み上げる本文。空白だけは 400。",
        "min_length": 1,
        "max_length": 4096,
        "range_source": "上流 app.py:90（Field(min_length=1, max_length=4096)）",
        "default_source": "unset",
        "required": True,
    },
    {
        "key": "voice",
        "type": "string",
        "label": "話者名",
        "description": (
            "話者名（GET /v1/audio/voices の id）。「デフォルト」＝参照なし。"
            "irodori.no_ref と同時に指定すると 400。"
            "省略すると IRODORI_DEFAULT_VOICE（配布版の焼き込みは「デフォルト」）が使われる。"
        ),
        "max_length": 256,
        "range_source": _SRC_YWK,
        "default_source": "settings_optional",
        "settings_key": "default_voice",
    },
    {
        "key": "speed",
        "type": "number",
        "label": "話速",
        "description": (
            "duration_scale を speed で割って尺に効かせる（上流 app.py:843-844）。"
            "duration_scale と同時に送ると合成される。"
        ),
        "min": 0.25,
        "max": 4.0,
        "step": 0.05,
        "range_source": "上流 app.py:93（Field(ge=0.25, le=4.0)）",
        "default_source": "dataclass",
        "dataclass_default": 1.0,
    },
    {
        "key": "response_format",
        "type": "enum",
        "enum": RESPONSE_FORMATS,
        "label": "出力形式",
        "description": "wav のみ。mp3/opus/aac は ffmpeg が要るので配布版は持たない（第三者バイナリ不同梱）。",
        "default_source": "settings",
        "settings_key": "default_response_format",
    },
    {
        "key": "stream_format",
        "type": "enum",
        "enum": ("sse",),
        "label": "ストリーム形式",
        "description": "sse を指定するとチャンクごとに base64 の wav を SSE で流す。省略すると 1 本の wav を返す。",
        "default_source": "unset",
    },
)

#: ``POST /v1/audio/speech`` の top-level で受ける欄名。``irodori`` の 44 欄のうち
#: Literal 3 欄を除いたものは、上流が ``model_extra`` から読むので受ける
#: （優先順＝irodori.X > top-level X > env）。
TOP_LEVEL_KEYS: frozenset[str] = frozenset(
    {spec["key"] for spec in REQUEST_PARAMS}
    | {"irodori"}
    | {spec["key"] for spec in IRODORI_PARAMS if spec["key"] not in LITERAL_NESTED_ONLY}
)

#: ``irodori`` ネストで受ける欄名（44 欄ちょうど）。
IRODORI_KEYS: frozenset[str] = frozenset(spec["key"] for spec in IRODORI_PARAMS)

_IRODORI_BY_KEY: dict[str, dict[str, Any]] = {spec["key"]: spec for spec in IRODORI_PARAMS}
_REQUEST_BY_KEY: dict[str, dict[str, Any]] = {spec["key"]: spec for spec in REQUEST_PARAMS}


def spec_for(key: str, *, nested: bool) -> dict[str, Any] | None:
    """Return the definition for ``key`` (``nested`` = inside ``irodori``)."""
    if nested:
        return _IRODORI_BY_KEY.get(key)
    return _REQUEST_BY_KEY.get(key) or _IRODORI_BY_KEY.get(key)


_PUBLIC_KEYS = (
    "key",
    "type",
    "enum",
    "items",
    "group",
    "label",
    "description",
    "min",
    "max",
    "step",
    "min_length",
    "max_length",
    "presets",
    "range_source",
    "note",
    "required",
)


def _public(spec: dict[str, Any]) -> dict[str, Any]:
    out: dict[str, Any] = {}
    for name in _PUBLIC_KEYS:
        if name not in spec:
            continue
        value = spec[name]
        out[name] = list(value) if isinstance(value, tuple) else value
    return out


def _nullable(spec: dict[str, Any]) -> bool:
    """Does omitting the field (or sending ``null``) mean "upstream default"?"""
    if "nullable" in spec:
        return bool(spec["nullable"])
    return spec["default_source"] in NULLABLE_SOURCES


def _resolve_default(spec: dict[str, Any], settings: Any, runtime: Any) -> Any:
    source = spec["default_source"]
    if source == "dataclass":
        return spec["dataclass_default"]
    if source == "ywk":
        return spec["ywk_default"]
    if source in ("settings", "settings_optional"):
        return getattr(settings, spec["settings_key"])
    if source == "checkpoint":
        settings_key = spec.get("settings_key")
        if settings_key is not None:
            override = getattr(settings, settings_key, None)
            if override is not None:
                return override
        if runtime is None:
            return None
        return getattr(runtime, spec["runtime_attr"], None)
    return None


def build_irodori_params(settings: Any, runtime: Any = None) -> list[dict[str, Any]]:
    """The 44 ``irodori`` fields with their *effective* defaults filled in."""
    out: list[dict[str, Any]] = []
    for spec in IRODORI_PARAMS:
        item = _public(spec)
        item["default"] = _resolve_default(spec, settings, runtime)
        item["default_source"] = spec["default_source"]
        item["nullable"] = _nullable(spec)
        item["exposed_to_ywk"] = spec["key"] in EXPOSED_TO_YWK
        out.append(item)
    return out


def build_request_params(settings: Any, runtime: Any = None) -> dict[str, Any]:
    """The top-level fields, keyed by field name (design §4-2 ``request``)."""
    out: dict[str, Any] = {}
    for spec in REQUEST_PARAMS:
        item = _public(spec)
        item.pop("key", None)
        item["default"] = _resolve_default(spec, settings, runtime)
        item["default_source"] = spec["default_source"]
        item["nullable"] = _nullable(spec)
        item["exposed_to_ywk"] = spec["key"] in EXPOSED_REQUEST_KEYS
        if spec["key"] == "voice":
            # design §4-2 ⑵: an ``exposed_to_ywk`` field must not report
            # ``null``, and whatever it does report must be a value the caller
            # can actually send back (decisions.md 45).
            if item["default"] == DEFAULT_VOICE_ID:
                # The distribution's own baked ``IRODORI_DEFAULT_VOICE``
                # (ywk_server.apply_env_defaults).  Omitting ``voice`` lands on
                # it and synthesises reference-free -- so it is not required.
                item["required"] = False
                item["default_source"] = "ywk"
                item["nullable"] = False
                item["note"] = (
                    "voice を省くと IRODORI_DEFAULT_VOICE が使われる。"
                    "配布版はそこに「デフォルト」を焼く（decisions.md 45）＝省略しても 400 にならず"
                    "参照なし合成になる（contract ⑶ 3-1・⑷ 4-2）。"
                    "ランチャが IRODORI_DEFAULT_VOICE を上書きすれば、その話者が既定になる"
                )
            elif item["default"] is None:
                # ``IRODORI_DEFAULT_VOICE`` was emptied by the launcher: the
                # upstream then 400s an omitted ``voice`` (voices.py:75-79), so
                # say so and still name the value that always works.
                item["required"] = True
                item["default"] = DEFAULT_VOICE_ID
                item["default_source"] = "ywk"
                item["nullable"] = False
                item["note"] = (
                    "voice は必須（省略・null は上流が 400 にする）。"
                    "IRODORI_DEFAULT_VOICE が空なので配布版は「デフォルト」を既定として名乗る"
                    "＝参照なし合成（contract ⑷ 4-2）"
                )
            else:
                # The launcher named a real speaker: it is the effective
                # default and ``voice`` may be omitted.
                item["required"] = False
                item["nullable"] = False
                item["note"] = (
                    "voice を省くと IRODORI_DEFAULT_VOICE の話者が使われる"
                    "（現在の値＝この default 欄）。参照なしは「デフォルト」（contract ⑷ 4-2）"
                )
        out[spec["key"]] = item
    return out


def build_rules() -> dict[str, Any]:
    return {
        "priority": "irodori.X > top-level X > env",
        "literal_must_be_nested": list(LITERAL_NESTED_ONLY),
        "voice_and_no_ref_exclusive": True,
        "voice_and_reference_exclusive": list(REFERENCE_KEYS),
        "unknown_field": "400",
        "out_of_range": "400",
        "response_format": list(RESPONSE_FORMATS),
        "null_means_unset": True,
        # The three fields upstream would otherwise read as "explicitly unset";
        # the wrapper deletes them so the line above is true for all 44.
        "null_normalized_by_wrapper": list(EXPLICIT_NULL_WINS),
        "empty_string_means_unset": list(EMPTY_STRING_MEANS_UNSET),
        "no_ref_voice_aliases": sorted(NO_REF_VOICE_IDS),
        "default_voice": DEFAULT_VOICE_ID,
        "exposed_to_ywk": sorted(EXPOSED_TO_YWK | EXPOSED_REQUEST_KEYS),
        "error_code_prefix": "ywk_",
    }


#: ``type`` -> the JSON Schema ``type`` the upstream openapi reports.
OPENAPI_TYPE = {
    "string": "string",
    "enum": "string",
    "number": "number",
    "integer": "integer",
    "boolean": "boolean",
    "array": "array",
}


def openapi_mismatches(openapi_schema: dict[str, Any]) -> list[str]:
    """Compare this table against the live upstream openapi (腐り検知).

    Returns one human-readable line per disagreement; an empty list means the
    table still matches ``IrodoriOptions``.
    """
    problems: list[str] = []
    try:
        props = openapi_schema["components"]["schemas"]["IrodoriOptions"]["properties"]
    except (KeyError, TypeError):
        return ["openapi: components.schemas.IrodoriOptions.properties が読めない"]

    upstream_keys = set(props)
    ours = set(IRODORI_KEYS)
    for missing in sorted(upstream_keys - ours):
        problems.append(f"openapi にあって ywk_params に無い欄: {missing}")
    for extra in sorted(ours - upstream_keys):
        problems.append(f"ywk_params にあって openapi に無い欄: {extra}")

    for spec in IRODORI_PARAMS:
        key = spec["key"]
        if key not in props:
            continue
        want = OPENAPI_TYPE[spec["type"]]
        got = _openapi_types(props[key])
        if want not in got:
            problems.append(f"型が違う欄 {key}: ywk_params={want} openapi={sorted(got)}")
    return problems


def _openapi_types(prop: dict[str, Any]) -> set[str]:
    branches = prop.get("anyOf") or prop.get("oneOf") or [prop]
    types: set[str] = set()
    for branch in branches:
        if not isinstance(branch, dict):
            continue
        value = branch.get("type")
        if isinstance(value, str):
            types.add(value)
        elif isinstance(value, list):
            types.update(str(item) for item in value)
        elif "enum" in branch:
            types.add("string")
    return types
