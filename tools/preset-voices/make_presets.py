#!/usr/bin/env python3
"""便 P — 生成結果から台帳 voices/presets.json を組み立てる（標準ライブラリのみ）。

読む物:
  build/out/preset-work/primary/gen_voicevox.result.json
  build/out/preset-work/primary/gen_coeiroink.result.json
  build/out/preset-work/logs/verify_primary.json
  build/out/preset-work/logs/verify_secondary.json
  build/out/preset-work/logs/run_secondary.result.json
  tools/preset-voices/corpus.json

書く物:
  voices/presets.json           （形＝tools/preset-voices/presets.json.schema）
  voices/presets/<id>.wav       （--copy を付けた時だけ。採用した二次を置く）

secondary.md5 と secondary.size_bytes は **実檔**（voices/presets/<id>.wav・--copy の複写後）から測る。
secondary.tail は verify_secondary.json の 3 条件判定（decisions 39）をそのまま写す。

使い方:
    python make_presets.py [--work <preset-work>] [--out <presets.json>]
                           [--ref-variant 30s|10s] [--copy]
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from verify_wavs import tail_rule_text as _tail_rule_text  # noqa: E402 — 同じディレクトリの道具

REPO = Path(__file__).resolve().parents[2]

RIGHTS_NOTE = (
    "decisions.md 18＝司令官（mugonkun）が 2026-09-04 に「本エンジンの生成ボイスは個人利用・商用・"
    "有償でも再配布可（元々そういう目的の商品）。本ソフトは無償配布なので問題なし」と確認した。"
    "ただしこれは司令官の確認であって、席が規約原文を取得して読んだ結果ではない"
    "（rights_source_fetched: false）。規約原文の所在・版・取得日は licenses/ の台帳に別途記す。"
)

# decisions 17 の 12 名。status pending / skipped の 5 名も最初から載せる。
ROSTER = [
    # 裁定 108＝司令官の改名指示（2026-09-07）。display_name はそのまま話者 id になる（裁定 17）ので、
    # engine_speaker（VOICEVOX の話者名）は「もち子さん」のまま動かさない。
    {"id": "vv_mochiko_sexy", "display_name": "もち子さん（セクシー／あん子）", "engine": "voicevox",
     "engine_speaker": "もち子さん", "speaker_uuid": None,
     "style": {"name": "セクシー／あん子", "id": 66}, "status": "done"},
    {"id": "vv_chibishikijii", "display_name": "ちび式じい", "engine": "voicevox",
     "engine_speaker": "ちび式じい", "speaker_uuid": None,
     "style": {"name": "ノーマル", "id": 42}, "status": "done"},
    {"id": "co_tsukuyomi", "display_name": "つくよみちゃん", "engine": "coeiroink",
     "engine_speaker": "つくよみちゃん", "speaker_uuid": "3c37646f-3881-5374-2a83-149267990abc",
     "style": {"name": "れいせい", "id": 0}, "status": "done"},
    {"id": "co_kana_naisho", "display_name": "KANA（ないしょばなし）", "engine": "coeiroink",
     "engine_speaker": "KANA", "speaker_uuid": "297a5b91-f88a-6951-5841-f1e648b2e594",
     "style": {"name": "ないしょばなし", "id": 33}, "status": "done"},
    {"id": "co_mana_isshoukenmei", "display_name": "MANA（いっしょうけんめい）", "engine": "coeiroink",
     "engine_speaker": "MANA", "speaker_uuid": "292ea286-3d5f-f1cc-157c-66462a6a9d08",
     "style": {"name": "いっしょうけんめい", "id": 7}, "status": "done"},
    {"id": "co_ofutonp_kiza", "display_name": "おふとんP（きざ）", "engine": "coeiroink",
     "engine_speaker": "おふとんP", "speaker_uuid": "a60ebf6c-626a-7ce6-5d69-c92bf2a1a1d0",
     "style": {"name": "きざ", "id": 23}, "status": "done"},
    {"id": "co_ofutonp_normal_v2", "display_name": "おふとんP（のーまるv2）", "engine": "coeiroink",
     "engine_speaker": "おふとんP", "speaker_uuid": "a60ebf6c-626a-7ce6-5d69-c92bf2a1a1d0",
     "style": {"name": "のーまるv2", "id": 2}, "status": "done"},
    # VOICEROID2 の 4 名（2026-09-05・G3 席が一次 wav を生成して done になった）。
    # engine_speaker は実機の Vr2SaveTool.exe dump が返した標準プリセット名と逐語一致。
    # style.id は C:\Program Files (x86)\AHS\VOICEROID2\Voice\ に実在するボイス檔の名。
    {"id": "vr2_akane_west", "display_name": "琴葉茜（関西弁）", "engine": "voiceroid2",
     "engine_speaker": "琴葉 茜", "speaker_uuid": None,
     "style": {"name": "関西弁", "id": "akane_west_emo_44"}, "status": "done"},
    {"id": "vr2_yoshida", "display_name": "吉田くん", "engine": "voiceroid2",
     "engine_speaker": "鷹の爪 吉田くん(v1)", "speaker_uuid": None,
     "style": {"name": "ノーマル", "id": "yoshidakun_44"}, "status": "done"},
    {"id": "vr2_tsukuyomi_ai", "display_name": "月読アイ", "engine": "voiceroid2",
     "engine_speaker": "月読アイ(v1)", "speaker_uuid": None,
     "style": {"name": "ノーマル", "id": "ai_44"}, "status": "done"},
    {"id": "vr2_tsukuyomi_shota", "display_name": "月読ショウタ", "engine": "voiceroid2",
     "engine_speaker": "月読ショウタ(v1)", "speaker_uuid": None,
     "style": {"name": "ノーマル", "id": "shouta_44"}, "status": "done"},
    {"id": "cevio_maki_en", "display_name": "弦巻マキ（英語）", "engine": "cevio_ai",
     "engine_speaker": "弦巻マキ 英", "speaker_uuid": None,
     "style": {"name": "英語", "id": None}, "status": "skipped"},
]

PENDING_NOTE = {
    "cevio_ai": "【司令官提供待ち】decisions 27 により今回は飛ばした（弦巻マキ 英は日本語読みができない）。"
                "一次 wav は後日司令官が生成して提供する。届いたら build/out/preset-work/primary/ に "
                "cevio_maki_en_10s.wav・cevio_maki_en_30s.wav の名で置き、run_secondary.py の "
                "PRIMARY_IDS に cevio_maki_en を足せば二次が通る。",
}


def _load(p: Path) -> dict | None:
    if not p.is_file():
        print(f"[warn] 無い: {p}")
        return None
    return json.loads(p.read_text(encoding="utf-8"))


def _device_from_log(log: str | None) -> str | None:
    """サーバのログ 1 檔から実 device を拾う（無ければ None）。"""
    if not log or not Path(log).is_file():
        return None
    for line in Path(log).read_text("utf-8", "replace").splitlines():
        if "start synthesize" in line and "model_device=" in line:
            return line.split("model_device=", 1)[1].split()[0]
    return None


def _actual_device(runsec: dict) -> str | None:
    """実 device をサーバのログから拾う。

    /health の model.model_device は *設定値* なので既定では "auto" のまま返る（実射で確認）。
    実際に使われた device は runtime の `[runtime] start synthesize model_device=<x> ...` にしか
    出ない。ROCm でも torch は "cuda" と名乗る。ログが無ければ None。
    掃引で撃ち直した行は自分の run の server_log を持つので、そちらを先に見る（下の呼び口）。
    """
    return _device_from_log(runsec.get("server_log"))


def _md5(p: Path) -> str:
    h = hashlib.md5()
    with p.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def _index_verify(report: dict | None) -> dict[str, dict]:
    if not report:
        return {}
    return {i["file"]: i for i in report.get("items", []) if "error" not in i}


def main() -> int:
    ap = argparse.ArgumentParser(description="voices/presets.json を組み立てる")
    ap.add_argument("--work", default=str(REPO / "build" / "out" / "preset-work"))
    ap.add_argument("--out", default=str(REPO / "voices" / "presets.json"))
    ap.add_argument("--ref-variant", default="30s", choices=["30s", "10s"],
                    help="既定で採用する参照の版（話者ごとの差し替えは台帳を手で直す）")
    ap.add_argument("--copy", action="store_true", help="採用した二次を voices/presets/<id>.wav へ複写する")
    args = ap.parse_args()

    work = Path(args.work).resolve()
    out = Path(args.out).resolve()
    presets_dir = out.parent / "presets"

    corpus = json.loads(Path(__file__).with_name("corpus.json").read_text(encoding="utf-8"))
    gen_vv = _load(work / "primary" / "gen_voicevox.result.json")
    gen_co = _load(work / "primary" / "gen_coeiroink.result.json")
    gen_vr2 = _load(work / "primary" / "gen_voiceroid2.result.json")
    vpri = _index_verify(_load(work / "logs" / "verify_primary.json"))
    vsec_report = _load(work / "logs" / "verify_secondary.json")
    vsec = _index_verify(vsec_report)
    # 台帳に載せる末尾判定の規則は「その測定に実際に使われた規則」を採る（verify_secondary.json の頭）。
    # 無ければ道具の既定を文字にする。
    tail_rule = (vsec_report or {}).get("tail_rule") or _tail_rule_text()
    runsec = _load(work / "logs" / "run_secondary.result.json") or {}

    # 一次の生成記録を id -> variant -> item に畳む
    gen_items: dict[str, dict[str, dict]] = {}
    for rep in (gen_vv, gen_co, gen_vr2):
        for it in (rep or {}).get("items", []):
            gen_items.setdefault(it["id"], {})[it["variant"]] = it

    sec_items = {i["voice"]: i for i in runsec.get("items", []) if "error" not in i}
    # seed 掃引（decisions 39）の記録。voice ごとに採用 seed と試行回数が入る。
    sweep = runsec.get("seed_sweep") or {}
    sweep_rows = {r["voice"]: r for r in sweep.get("results", [])}
    health = runsec.get("health", {}) or {}
    # 本文は「その 1 本を実際に撃った run」のもの＝行（items[]）が持っていればそれを採る（裁定 112）。
    # 下の 2 つは行が本文を持たない場合（＝畳み込み先の run で撃った行）の控え。
    sec_text_id = runsec.get("text_id")
    sec_text = runsec.get("text")
    par = runsec.get("irodori_params", corpus["irodori_params"])
    device = _actual_device(runsec)

    presets: list[dict] = []
    copied = 0
    for row in ROSTER:
        vid = row["id"]
        entry = dict(row)
        entry["rights_note"] = RIGHTS_NOTE
        entry["rights_confirmed_by"] = "司令官（mugonkun）"
        entry["rights_confirmed_at"] = "2026-09-04"
        entry["rights_source_fetched"] = False
        entry["primary"] = None
        entry["secondary"] = None
        # generated_at は schema で type: string＝null を許さない。まだ無い行では欄ごと落とす。

        if row["status"] != "done":
            entry["rights_note"] = RIGHTS_NOTE + " ／ " + PENDING_NOTE.get(row["engine"], "")
            presets.append(entry)
            continue

        # ---- primary
        g30 = gen_items.get(vid, {}).get("30s")
        g10 = gen_items.get(vid, {}).get("10s")
        if g30:
            v = vpri.get(g30["file"], {})
            entry["primary"] = {
                "file": g30["file"],
                "file_10s": g10["file"] if g10 else None,
                "duration_s": g30["duration_s"],
                "duration_10s_s": g10["duration_s"] if g10 else None,
                "sample_rate": g30["sample_rate"],
                "channels": g30["channels"],
                "bits": g30["bits"],
                "text_id": "text_30s",
                "peak_dbfs": v.get("peak_dbfs"),
                "rms_dbfs": v.get("rms_dbfs"),
            }

        # ---- secondary（採用する版を選ぶ）
        voice_key = vid if args.ref_variant == "30s" else f"{vid}_ref10"
        s = sec_items.get(voice_key)
        other_key = f"{vid}_ref10" if args.ref_variant == "30s" else vid
        other = sec_items.get(other_key)
        if s:
            v = vsec.get(s["file"], {})

            # 実檔（voices/presets/<id>.wav）を先に置いてから md5・size_bytes を測る。
            # --copy が無い場合は既に置いてある実檔、それも無ければ生成元（secondary/）を測る。
            src = Path(s["path"])
            if args.copy and src.is_file():
                presets_dir.mkdir(parents=True, exist_ok=True)
                shutil.copy2(src, presets_dir / f"{vid}.wav")
                copied += 1
            actual = presets_dir / f"{vid}.wav"
            if not actual.is_file():
                actual = src
            md5 = _md5(actual) if actual.is_file() else None
            size_bytes = actual.stat().st_size if actual.is_file() else None
            if md5 is None:
                print(f"[warn] 実檔が無いので md5/size_bytes を書けない: {vid}")

            # irodori 欄は「その 1 本を実際に撃った時の値」を採る（seed 掃引で話者ごとに seed が違う）。
            req_ir = ((s.get("request") or {}).get("irodori")) or {}
            irodori = {
                "num_steps": req_ir.get("num_steps", par["num_steps"]),
                "seed": req_ir.get("seed", par["seed"]),
            }
            if "trim_tail" in req_ir:
                irodori["trim_tail"] = req_ir["trim_tail"]
            # 参照ボイスへの寄せ具合（裁定 111）。撃った時に明示した行だけ載せる。
            # 無い行は「上流既定 5.0 で撃った」の意＝ここで値を捏造しない。
            if "cfg_scale_speaker" in req_ir:
                irodori["cfg_scale_speaker"] = req_ir["cfg_scale_speaker"]
            row = sweep_rows.get(voice_key)
            if row and row.get("adopted"):
                # seed 列は「その話者を撃った run のもの」を採る。掃引を 2 度回すと
                # 話者ごとに起点が違う（例＝月読アイは 1235 から撃ち直した）ので、
                # 台帳の頭にある最新 run の列を全員に当てはめると嘘になる。
                # 行が自分の列を持たない（前の run で書かれた行）ときは attempts から実際に撃った seed を拾う。
                tried = sorted({a["seed"] for a in row.get("attempts", []) if a.get("seed") is not None})
                irodori["seed_sweep"] = {
                    "seeds": row.get("seeds") or tried or sweep.get("seeds"),
                    "seeds_tried": tried,
                    "adopted_seed": row["adopted_seed"],
                    "adopted_trim_tail": row["adopted_trim_tail"],
                    "tries": row["tries"],
                    "tail_rule": row.get("tail_rule") or sweep.get("tail_rule"),
                }
            entry["secondary"] = {
                "file": f"{vid}.wav",
                # 実檔から測った素性（schema で required）。N: の写しとの突合はこの md5 で行う。
                "md5": md5,
                "size_bytes": size_bytes,
                "duration_s": v.get("duration_s"),
                "sample_rate": v.get("sample_rate"),
                "channels": v.get("channels"),
                "bits": v.get("bits"),
                "peak_dbfs": v.get("peak_dbfs"),
                "rms_dbfs": v.get("rms_dbfs"),
                # 行ごとの本文（裁定 112）。掃引で本文を変えて撃ち直した行は自分の本文を持つ
                # （run_secondary.py が item に刻み merge_result が保つ）。持たない行は
                # 畳み込み先の run＝最初に 22 本を撃った run の本文。
                "text_id": s.get("text_id") or sec_text_id,
                "text": s.get("text") or sec_text,
                "ref_variant": args.ref_variant,
                "ref_10s_file": other["file"] if other else None,
                "irodori": irodori,
                # 末尾判定（decisions 39）＝verify_wavs.py の 3 条件。
                # ⒜ 相対 Δ≦−20 dB・⒝ 絶対 ≦−40 dBFS・⒞ 末尾 300 ms を 25 ms 刻みで見て
                # 最後の 100 ms に +0.5 dB を超える立ち上がりが無い（−60 dBFS 以下は無音）。
                "tail": {
                    "window_ms": v.get("tail_window_ms"),
                    "tail_rms_dbfs": v.get("tail_rms_dbfs"),
                    "tail_delta_db": v.get("tail_delta_db"),
                    "margin_db": v.get("tail_margin_db"),
                    "delta_clean": v.get("tail_delta_clean"),
                    "abs_dbfs": v.get("tail_abs_dbfs"),
                    "abs_clean": v.get("tail_abs_clean"),
                    "profile_ms": v.get("tail_profile_ms"),
                    "step_ms": v.get("tail_step_ms"),
                    "final_ms": v.get("tail_final_ms"),
                    "floor_dbfs": v.get("tail_floor_dbfs"),
                    "rise_limit_db": v.get("tail_rise_limit_db"),
                    "rise_db": v.get("tail_rise_db"),
                    "rise_clean": v.get("tail_rise_clean"),
                    "profile_dbfs": v.get("tail_profile_dbfs"),
                    "fail_reasons": v.get("tail_fail_reasons", []),
                    "rule": tail_rule,
                    "verdict": v.get("tail_verdict"),
                },
                "server": {
                    "hf_checkpoint": health.get("model", {}).get("hf_checkpoint"),
                    "model_precision": health.get("model", {}).get("model_precision"),
                    "codec_precision": health.get("model", {}).get("codec_precision"),
                    "port": runsec.get("port"),
                    # device＝サーバログの `start synthesize model_device=` から拾った実 device。
                    # device_configured＝/health が返す設定値（既定 "auto"）。両方残す。
                    # 掃引で撃ち直した行は自分の run の server_log を持つ（merge_result が持たせる）。
                    "device": _device_from_log(s.get("server_log")) or device,
                    "device_configured": health.get("model", {}).get("model_device"),
                },
                "elapsed_s": s.get("elapsed_s"),
            }
            # 行ごとの generated_at は「その 1 本を実際に撃った run」の時刻。掃引で撃ち直した行は
            # merge_result が item に持たせた値を採る（無い行＝畳み込み先の run で撃った行は top-level）。
            entry["generated_at"] = s.get("generated_at") or runsec.get("generated_at")

        presets.append(entry)

    doc = {
        "version": 1,
        "generated_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "corpus": "tools/preset-voices/corpus.json",
        # corpus は出所の檔だけを指す文字列（形＝schema の type: string）。本文が 1 種類とは限らない
        # ので（裁定 112 で 3 行だけ expressive_20s になった）、実際に行が名乗っている本文の id を
        # 並べて添える＝台帳の頭を見ただけで「何種類の本文が混ざっているか」が分かる。
        "corpus_texts": sorted(
            {
                p["secondary"]["text_id"]
                for p in presets
                if p.get("secondary") and p["secondary"].get("text_id")
            }
        ),
        "notes": [
            "decisions.md 17・18・26・27 が正典。設計＝docs/design/ben-p-preset-voices.md。",
            "secondary.file が実体の参照ボイス＝voices/presets/<id>.wav。一次 wav は素材で配布物には入れない"
            "（build/out/preset-work/primary/ と N:/temp_for_claudecode_agents/irodori-ywk/preset-voices/primary/ に保全）。",
            "VOICEROID2 の 4 名は API が無いので GUI 駆動で一次 wav を取り出した"
            "（tools/preset-voices/gen_voiceroid2.py ＋ Vr2SaveTool・2026-09-05・設計書 §6）。",
            "status skipped＝decisions 27（CeVIO AI 弦巻マキ 英）。secondary が null の行は二次 wav がまだ無い。",
            "rights_source_fetched は全行 false＝規約原文は席が未取得。司令官の確認のみ（decisions 18）。",
            "ref_variant は 30s／10s の両方を生成済み。聴いて良い方に差し替えてよい（ref_10s_file が対の檔）。",
            "secondary.md5 / secondary.size_bytes は voices/presets/<id>.wav の実檔から測った値"
            "（make_presets.py が --copy の複写後に測る）。N: の写しとの突合はこの md5 で行う。",
            "secondary.tail は 3 条件の末尾判定（decisions 39）＝⒜ 相対 Δ≦−20 dB・⒝ 絶対 ≦−40 dBFS・"
            "⒞ 末尾 300 ms を 25 ms 刻みで見て最後の 100 ms に +0.5 dB を超える立ち上がりが無い"
            "（−60 dBFS 以下は無音扱い）。⒜ だけの旧規則は、いったん無音に落ちてから鳴り出して切れる檔を"
            "Δ=−20.19 dB の縁で通してしまった（月読アイ）。規則の逐語は secondary.tail.rule に入る。",
            "secondary.irodori.cfg_scale_speaker が無い行は上流既定 5.0 で撃った（2026-09-05）。"
            "在る行はその値で撃った（裁定 111＝co_kana_naisho・co_ofutonp_kiza・vr2_akane_west を 7.0 で撃ち直し）。"
            "ただしこの 3 名の ref_10s_file（10 s 参照版）は撃ち直していない＝cfg 5.0 の射のままなので、"
            "上の ref_variant の註に従って差し替えるなら"
            "先に `--cfg-scale-speaker 7.0 --text expressive_20s` で撃ち直すこと"
            "（cfg と本文の両方が是正前＝裁定 111・112）。",
            "secondary.text_id が expressive_20s の行は本文を約 20 秒に縮めて撃った"
            "（裁定 112＝3 本とも同じ位置で参照ボイスから離れたため・cfg_scale_speaker 7.0 のまま）。"
            "expressive_30s の行は 146 字の本文のまま。",
            "presets[].generated_at は「その行の二次を実際に撃った run」の時刻＝掃引で撃ち直した行は"
            "撃ち直した日が入る（裁定 111 の 3 行は 2026-09-09・ほかの 8 行は 2026-09-05）。"
            "doc の頭の generated_at はこの台帳を組み立てた時刻で、別物。",
        ],
        "presets": presets,
    }
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(doc, ensure_ascii=False, indent=2), encoding="utf-8")

    done = sum(1 for p in presets if p["status"] == "done")
    withsec = sum(1 for p in presets if p.get("secondary"))
    print(f"[done] {out}  全 {len(presets)} 名 / status done {done} 名 / secondary あり {withsec} 名")
    if args.copy:
        print(f"[copy] voices/presets/ へ {copied} 本を複写した")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
