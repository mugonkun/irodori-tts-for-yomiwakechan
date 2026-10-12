"""Emoji handling for the speech input (decisions.md 165, 2026-10-12).

Why this exists
---------------
The shipped checkpoint (``Aratako/Irodori-TTS-v4.1-Small``) reads emoji as
style annotations, but only the 45 listed in the model repo's
``EMOJI_ANNOTATIONS.md`` were trained as such.  Every other emoji still reaches
the text encoder and -- because the duration predictor sums a per-token
estimate -- still *asks for time*: three party poppers alone are worth about
five seconds of audio with nothing to say in them, and the acoustic model fills
that time with whatever it can (the "unexpected words" the 本体 users hear).
Survey: ``research/report/emoji-survey-2026-10-12.md``.

What it does (one left-to-right pass over the text, ``TOKEN_RE.sub``)
-----------------------------------------------------------------------
1. **Supported 45 pass through, spelled as the model repo spells them.**
   Variants are folded onto the canonical form: a stray/missing VS16
   (``⏸`` → ``⏸️``, ``⏩️`` → ``⏩``), skin-tone modifiers (``🙏🏻`` → ``🙏``),
   text-presentation selectors and tag characters.  The ZWJ of ``😮‍💨`` is
   part of its skeleton, so ``😮💨`` (no ZWJ) is *not* promoted -- it becomes
   ``😮`` with the ``💨`` removed.
2. **Everything else that Unicode calls an emoji is removed**: whole clusters
   (base + modifiers, ZWJ sequences, keycaps, flags, tag flags) and the
   orphaned parts the 本体's ordinal dictionary can leave behind
   (``🙏→おねがい`` applied to ``🙏🏻`` yields ``おねがい🏻``).  Text-default
   pictographs (``©`` ``▶`` ``⚠`` ``❤`` ``☺`` …) are removed too: measured on
   CPU they ask for the same time with or without VS16 (裁定 165 Q5 = P1).
3. **Characters the upstream's own ``normalize_text`` already turns into text
   are folded here first** so the pass in step 2 cannot delete them:
   22 NFKC cases (``㊗``→祝, ``‼``→!!, ``🈵``→満 …) plus ``♥``→``♡``.
   Two small extras by 裁定: ``❗❕``→``!``, ``❓❔``→``?`` (Q8) and keycaps
   ``1️⃣``→``1`` (Q9).

The caller (``ywk_server._create_speech``) decides what to do with an input
that ends up with nothing to pronounce (silence, 200) and writes the one log
line.  This module has no I/O and no upstream imports: ``regex`` only
(transformers' dependency, present in all four runtime ledgers; its Unicode
tables are 17.0 in 2026.9.3).

Coverage: every sequence of Unicode's ``emoji-test.txt`` (17.0: 5,225 rows;
18.0: 5,244) is taken as one unit with no residue, all 500 spelling variants
of the 45 land on the canonical form, and of 295,797 assigned non-emoji code
points only VS15 and LANGUAGE TAG (both invisible format characters) are
touched (survey §6-2).
"""

from __future__ import annotations

import io
import struct
import unicodedata
from collections import Counter
from dataclasses import dataclass, field

import regex

__all__ = [
    "SUPPORTED_EMOJIS",
    "FilterResult",
    "filter_text",
    "clip_graphemes",
    "is_pronounceable",
    "is_dotnet_whitespace_only",
    "silent_wav",
    "INPUT_MAX_CHARS",
]

#: ``SpeechRequest.input`` is ``Field(max_length=4096)`` upstream (app.py:90)
#: and the wrapper's own whitelist says the same (ywk_params.REQUEST_PARAMS).
#: Folding can *lengthen* the text (VS16 補完・㊗→祝 is 1→1 but ‼→!! is 1→2),
#: so the result is clipped back to this many code points on grapheme borders.
INPUT_MAX_CHARS = 4096

# ---------------------------------------------------------------------------
# The 45 supported emoji -- EMOJI_ANNOTATIONS.md order, canonical spelling.
# Written as escapes so the invisible VS16 / ZWJ never sit raw in the source.
# ---------------------------------------------------------------------------
SUPPORTED_EMOJIS: tuple[str, ...] = (
    "\U0001F442",  # 1 👂 囁き
    "\U0001F62E\U0000200D\U0001F4A8",  # 2 😮‍💨 吐息 (ZWJ)
    "\U000023F8\U0000FE0F",  # 3 ⏸️ 間 (VS16)
    "\U0001F92D",  # 4 🤭 笑い
    "\U0001F975",  # 5 🥵 喘ぎ
    "\U0001F4E2",  # 6 📢 エコー
    "\U0001F60F",  # 7 😏 からかう
    "\U0001F97A",  # 8 🥺 震え声
    "\U0001F32C\U0000FE0F",  # 9 🌬️ 息切れ (VS16)
    "\U0001F62E",  # 10 😮 息をのむ
    "\U0001F445",  # 11 👅 舐める音
    "\U0001F48B",  # 12 💋 リップノイズ
    "\U0001FAF6",  # 13 🫶 優しく
    "\U0001F62D",  # 14 😭 泣き声
    "\U0001F631",  # 15 😱 悲鳴
    "\U0001F62A",  # 16 😪 眠そう
    "\U0001F634",  # 17 😴 寝言
    "\U000023E9",  # 18 ⏩ 早口 (no VS16)
    "\U0001F4DE",  # 19 📞 電話越し
    "\U0001F422",  # 20 🐢 ゆっくり
    "\U0001F964",  # 21 🥤 飲み込む
    "\U0001F927",  # 22 🤧 咳・鼻
    "\U0001F612",  # 23 😒 舌打ち
    "\U0001F630",  # 24 😰 慌てる
    "\U0001F606",  # 25 😆 喜び
    "\U0001F4A5",  # 26 💥 勢いよく
    "\U0001F620",  # 27 😠 怒り
    "\U0001F632",  # 28 😲 驚き
    "\U0001F971",  # 29 🥱 あくび
    "\U0001F616",  # 30 😖 苦しげ
    "\U0001F61F",  # 31 😟 心配
    "\U0001FAE3",  # 32 🫣 照れ
    "\U0001F644",  # 33 🙄 呆れ
    "\U0001F60A",  # 34 😊 楽しげ
    "\U0001F60E",  # 35 😎 得意げ
    "\U0001F44C",  # 36 👌 相槌
    "\U0001F64F",  # 37 🙏 懇願
    "\U0001F974",  # 38 🥴 酔う
    "\U0001F3B5",  # 39 🎵 鼻歌
    "\U0001F910",  # 40 🤐 口を塞ぐ
    "\U0001F60C",  # 41 😌 安堵
    "\U0001F914",  # 42 🤔 疑問
    "\U0001F4AA",  # 43 💪 力強く
    "\U0001F443",  # 44 👃 嗅ぐ音
    "\U0001F4D6",  # 45 📖 朗読
)

# ---------------------------------------------------------------------------
# Patterns.  Invisible code points are spelled as \U escapes on purpose.
# ---------------------------------------------------------------------------
#: Decoration that can follow a pictograph inside one cluster: skin tones
#: (Emoji_Modifier), VS15, VS16, tag characters (tag flags).
_DECOR = r"[\p{Emoji_Modifier}\U0000FE0E\U0000FE0F\U000E0020-\U000E007F]"

#: One emoji cluster: a flag (regional-indicator pair), a keycap, or a
#: pictograph with its decoration optionally ZWJ-joined to more of the same.
_CLUSTER = (
    r"\p{Regional_Indicator}{2}"
    r"|[0-9#*]\U0000FE0F?\U000020E3"
    r"|\p{Extended_Pictographic}" + _DECOR + r"*"
    r"(?:\U0000200D\p{Extended_Pictographic}" + _DECOR + r"*)*"
)

#: A part that belongs to an emoji but did not get matched as one above:
#: a lone skin tone, a lone regional indicator, VS15/VS16, ZWJ, the keycap
#: enclosure, LANGUAGE TAG and the tag characters.
_ORPHAN = (
    r"[\p{Emoji_Modifier}\p{Regional_Indicator}"
    r"\U0000FE0E\U0000FE0F\U0000200D\U000020E3\U000E0001\U000E0020-\U000E007F]"
)

TOKEN_RE = regex.compile(r"(?P<cluster>" + _CLUSTER + r")|(?P<orphan>" + _ORPHAN + r")")
_KEY_STRIP_RE = regex.compile(_DECOR)
_KEYCAP_RE = regex.compile(r"[0-9#*]\U0000FE0F?\U000020E3")
_GRAPHEME_RE = regex.compile(r"\X")


def _skeleton(cluster: str) -> str:
    """The cluster with its decoration stripped (ZWJ kept)."""
    return _KEY_STRIP_RE.sub("", cluster)


#: skeleton -> canonical spelling, for the 45.
_SUPPORTED_BY_SKELETON: dict[str, str] = {_skeleton(e): e for e in SUPPORTED_EMOJIS}
assert len(_SUPPORTED_BY_SKELETON) == len(SUPPORTED_EMOJIS) == 45

#: What the upstream's ``normalize_text`` would make of these anyway
#: (NFKC for 22 of them, a fixed ``♥``→``♡`` replacement for the last),
#: applied here *before* the removal pass so they survive it.
UPSTREAM_FOLD: dict[str, str] = {
    "\U0000203C": "!!",  # ‼
    "\U00002049": "!?",  # ⁉
    "\U00002122": "TM",  # ™
    "\U00002139": "i",  # ℹ
    "\U000024C2": "M",  # Ⓜ
    "\U00003297": "\U0000795D",  # ㊗ → 祝
    "\U00003299": "\U000079D8",  # ㊙ → 秘
    "\U0001F201": "\U000030B3\U000030B3",  # 🈁 → ココ
    "\U0001F202": "\U000030B5",  # 🈂 → サ
    "\U0001F21A": "\U00007121",  # 🈚 → 無
    "\U0001F22F": "\U00006307",  # 🈯 → 指
    "\U0001F232": "\U00007981",  # 🈲 → 禁
    "\U0001F233": "\U00007A7A",  # 🈳 → 空
    "\U0001F234": "\U00005408",  # 🈴 → 合
    "\U0001F235": "\U00006E80",  # 🈵 → 満
    "\U0001F236": "\U00006709",  # 🈶 → 有
    "\U0001F237": "\U00006708",  # 🈷 → 月
    "\U0001F238": "\U00007533",  # 🈸 → 申
    "\U0001F239": "\U00005272",  # 🈹 → 割
    "\U0001F23A": "\U000055B6",  # 🈺 → 営
    "\U0001F250": "\U00005F97",  # 🉐 → 得
    "\U0001F251": "\U000053EF",  # 🉑 → 可
    "\U00002665": "\U00002661",  # ♥ → ♡ (text_normalization.py:13)
}

#: 裁定 165 Q8: emoji-presentation punctuation keeps its punctuation.
PUNCT_FOLD: dict[str, str] = {
    "\U00002757": "!",  # ❗
    "\U00002755": "!",  # ❕
    "\U00002753": "?",  # ❓
    "\U00002754": "?",  # ❔
}

_FOLD: dict[str, str] = {**UPSTREAM_FOLD, **PUNCT_FOLD}


@dataclass
class FilterResult:
    """What ``filter_text`` did.  Lists hold the original spellings."""

    text: str
    removed: list[str] = field(default_factory=list)
    normalized: list[tuple[str, str]] = field(default_factory=list)
    folded: list[tuple[str, str]] = field(default_factory=list)

    @property
    def changed(self) -> bool:
        return bool(self.removed or self.normalized or self.folded)


def filter_text(text: str) -> FilterResult:
    """Apply the three rules in one pass.  Pure; never raises on any ``str``."""
    result = FilterResult(text="")

    def replace(match: "regex.Match[str]") -> str:
        piece = match.group(0)
        if match.group("orphan") is not None:
            result.removed.append(piece)
            return ""
        skeleton = _skeleton(piece)
        canonical = _SUPPORTED_BY_SKELETON.get(skeleton)
        if canonical is not None:
            if piece != canonical:
                result.normalized.append((piece, canonical))
            return canonical
        folded = _FOLD.get(skeleton)
        if folded is not None:
            result.folded.append((piece, folded))
            return folded
        if _KEYCAP_RE.fullmatch(piece):  # 裁定 165 Q9: keep the digit
            result.folded.append((piece, piece[0]))
            return piece[0]
        result.removed.append(piece)
        return ""

    result.text = TOKEN_RE.sub(replace, text)
    return result


def clip_graphemes(text: str, limit: int = INPUT_MAX_CHARS) -> str:
    """Cut ``text`` back to ``limit`` code points on a grapheme border."""
    if len(text) <= limit:
        return text
    out: list[str] = []
    used = 0
    for grapheme in _GRAPHEME_RE.findall(text):
        if used + len(grapheme) > limit:
            break
        out.append(grapheme)
        used += len(grapheme)
    return "".join(out)


def is_pronounceable(text: str) -> bool:
    """True if there is something for the model to voice (裁定 165 Q11).

    A letter or a digit, or one of the 45 supported emoji -- ``😭`` alone is
    a sob and ``😆😆😆`` a laugh, which is exactly what the 本体 sends them
    for.  Punctuation-only leftovers (``！`` ``。`` ``…``) are not: on the
    shipped checkpoint a lone ``。`` still synthesises three seconds of
    something.
    """
    for ch in text:
        if unicodedata.category(ch)[0] in ("L", "N"):
            return True
    return any(emoji in text for emoji in SUPPORTED_EMOJIS)


def is_dotnet_whitespace_only(text: str) -> bool:
    """``string.IsNullOrWhiteSpace`` as .NET defines it (the 本体's own gate).

    The 本体 never sends such an input (``IrodoriYwkSynthesizer.cs:182``), so
    this is the only shape the wrapper still answers with the upstream's 400
    ``ywk_empty_input``.  Python's ``str.isspace`` is wider (U+001C..U+001F),
    which is why ``str.strip`` cannot be used for this decision (Q18).
    """
    for ch in text:
        if ch in "\t\n\v\f\r\x85":
            continue
        if unicodedata.category(ch) in ("Zs", "Zl", "Zp"):
            continue
        return False
    return True


def silent_wav(sample_rate: int = 48000, seconds: float = 0.1) -> bytes:
    """A PCM16 mono RIFF/WAVE of digital silence (at least one sample).

    The shape the 本体 can apply its volume multiplier to
    (``IrodoriYwkVolumeScaler.cs``: PCM 16-bit, ``fmt`` before ``data``, data
    length > 0) and that NAudio opens without complaint.  44-byte header, the
    same layout ``soundfile`` writes for the real audio.
    """
    rate = max(1, int(sample_rate))
    samples = max(1, int(round(rate * float(seconds))))
    data = b"\x00\x00" * samples
    buf = io.BytesIO()
    buf.write(b"RIFF")
    buf.write(struct.pack("<I", 36 + len(data)))
    buf.write(b"WAVE")
    buf.write(b"fmt ")
    buf.write(struct.pack("<IHHIIHH", 16, 1, 1, rate, rate * 2, 2, 16))
    buf.write(b"data")
    buf.write(struct.pack("<I", len(data)))
    buf.write(data)
    return buf.getvalue()


# ---------------------------------------------------------------------------
# Log rendering (no body text ever: emoji, counts and code points only).
# ---------------------------------------------------------------------------
_INVISIBLE_CATEGORIES = frozenset({"Mn", "Me", "Cf", "Sk", "Cn", "Co"})
_MAX_KINDS = 10


def _label(piece: str) -> str:
    """The piece as written, plus code points when it would not show up."""
    if all(unicodedata.category(ch) in _INVISIBLE_CATEGORIES for ch in piece):
        return "+".join(f"U+{ord(ch):04X}" for ch in piece)
    return piece


def _summarize(pieces: list[str]) -> str:
    counts = Counter(pieces)
    parts = [f"{_label(k)}x{n}" if n > 1 else _label(k) for k, n in counts.most_common(_MAX_KINDS)]
    if len(counts) > _MAX_KINDS:
        parts.append(f"+{len(counts) - _MAX_KINDS}kinds")
    return " ".join(parts)


def describe(
    result: FilterResult | None,
    *,
    chars_before: int,
    chars_after: int,
    dropped_chunks: int = 0,
    silent: bool = False,
) -> str:
    """One line for the launcher log, e.g.
    ``removed=🎉x3 U+1F3FB normalized=1 folded=1 chars=12->8``."""
    parts: list[str] = []
    if result is not None and result.removed:
        parts.append("removed=" + _summarize(result.removed))
    if result is not None and result.normalized:
        parts.append("normalized=" + _summarize([f"{a}>{b}" for a, b in result.normalized]))
    if result is not None and result.folded:
        parts.append("folded=" + _summarize([f"{a}>{b}" for a, b in result.folded]))
    if dropped_chunks:
        parts.append(f"chunks_dropped={dropped_chunks}")
    parts.append(f"chars={chars_before}->{chars_after}")
    if silent:
        parts.append("silent=1")
    return " ".join(parts)
