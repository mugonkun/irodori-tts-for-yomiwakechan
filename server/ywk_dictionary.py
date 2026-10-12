"""The app's own reading dictionary (decisions.md 166, 2026-10-12).

A plain replacement table applied to the speech ``input`` before the emoji
handling (``ywk_emoji``): ``from`` → ``to``, top to bottom, every ordinal
occurrence (the same rule as the 本体's dictionary, so one way of thinking
serves both tiers).  Irodori-TTS reads the text itself, so a "reading" is just
text: kana, kanji, Latin, an emoji or nothing at all.

File: ``<data>/dictionary.json`` -- written by the launcher, read here per
request with a size+mtime cache so an edit takes effect on the next request.

    {"schema": 1, "entries": [{"from": "🎉", "to": "ぱちぱち"}]}

A missing file is an empty table; a broken one is an empty table plus one
line of ``error`` (the launcher and ``/ywk/status`` show it; speech goes on).
Pure stdlib, no I/O beyond reading that one file.
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any

__all__ = ["FILE_NAME", "DictionaryEntry", "DictionaryTable", "load_table", "apply", "status_snapshot"]

#: Lives beside ``settings.json`` and ``voices/`` in the per-user data root.
FILE_NAME = "dictionary.json"


@dataclass(frozen=True)
class DictionaryEntry:
    source: str
    reading: str


@dataclass(frozen=True)
class DictionaryTable:
    entries: tuple[DictionaryEntry, ...]
    error: str | None = None

    @property
    def empty(self) -> bool:
        return not self.entries


EMPTY = DictionaryTable(entries=())

#: path -> (size, mtime_ns, table).  ``None`` stat means "file absent".
_cache: dict[str, tuple[tuple[int, int] | None, DictionaryTable]] = {}


def _parse(payload: Any) -> DictionaryTable:
    if not isinstance(payload, dict):
        return DictionaryTable(entries=(), error="dictionary.json must contain a JSON object")
    rows = payload.get("entries")
    if rows is None:
        return EMPTY
    if not isinstance(rows, list):
        return DictionaryTable(entries=(), error="dictionary.json: entries must be an array")
    entries: list[DictionaryEntry] = []
    for row in rows:
        if not isinstance(row, dict):
            continue
        source = row.get("from")
        reading = row.get("to", "")
        if not isinstance(source, str) or source == "":
            continue  # an empty "from" would match everywhere; the 本体 skips it too
        if reading is None:
            reading = ""
        if not isinstance(reading, str):
            continue
        entries.append(DictionaryEntry(source=source, reading=reading))
    return DictionaryTable(entries=tuple(entries))


def load_table(path: Path) -> DictionaryTable:
    """The table in ``path``, re-read only when the file changed."""
    key = str(path)
    try:
        stat = os.stat(path)
        stamp: tuple[int, int] | None = (stat.st_size, stat.st_mtime_ns)
    except OSError:
        stamp = None
    cached = _cache.get(key)
    if cached is not None and cached[0] == stamp:
        return cached[1]
    if stamp is None:
        table = EMPTY
    else:
        try:
            with path.open("r", encoding="utf-8") as handle:
                table = _parse(json.load(handle))
        except (OSError, ValueError, UnicodeDecodeError) as exc:
            table = DictionaryTable(entries=(), error=f"dictionary.json could not be read: {exc}")
    _cache[key] = (stamp, table)
    return table


def forget(path: Path) -> None:
    """Drop the cache for ``path`` (tests)."""
    _cache.pop(str(path), None)


def apply(text: str, table: DictionaryTable) -> tuple[str, int]:
    """Replace top to bottom; returns the text and how many matches were replaced."""
    hits = 0
    for entry in table.entries:
        count = text.count(entry.source)
        if count:
            hits += count
            text = text.replace(entry.source, entry.reading)
    return text, hits


def status_snapshot(path: Path) -> dict[str, Any]:
    """The ``/ywk/status.dictionary`` block (no path beyond the file name)."""
    table = load_table(path)
    return {"file": FILE_NAME, "entries": len(table.entries), "error": table.error}
