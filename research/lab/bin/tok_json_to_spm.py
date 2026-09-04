"""tokenizer.json (HF Unigram) -> SentencePiece ModelProto (.model) 変換.

読むだけ: HF キャッシュの tokenizer.json。書くのは lab/out/ のみ。
"""
import argparse
import json
import os
import sys

from sentencepiece import sentencepiece_model_pb2 as spm_pb2

T = spm_pb2.ModelProto.SentencePiece.Type

DEFAULT_TOK = (
    r"C:/Users/mugonkun/.cache/huggingface/hub/models--Aratako--Irodori-TTS-v4.1-Small"
    r"/snapshots/2b28324dc263ed5e6638b3cf3dd94c82ead07b4b/tokenizer/tokenizer.json"
)


def build(tok_json_path, out_path, user_defined_mode="added", byte_type="byte",
          control_mode="control"):
    d = json.load(open(tok_json_path, encoding="utf-8"))
    model = d["model"]
    assert model["type"] == "Unigram", model["type"]
    vocab = model["vocab"]
    unk_id = model["unk_id"]
    byte_fallback = bool(model.get("byte_fallback", False))

    pre = d.get("pre_tokenizer") or {}
    # Metaspace: replacement / prepend_scheme / split
    add_dummy_prefix = False
    escape_ws = True
    if pre.get("type") == "Metaspace":
        ps = pre.get("prepend_scheme", "always")
        add_dummy_prefix = ps != "never"
        escape_ws = True
    normalizer = d.get("normalizer")

    added = {a["id"]: a for a in d.get("added_tokens", [])}

    mp = spm_pb2.ModelProto()

    ts = mp.trainer_spec
    ts.model_type = spm_pb2.TrainerSpec.UNIGRAM
    ts.vocab_size = len(vocab)
    ts.unk_id = unk_id
    ts.bos_id = 1
    ts.eos_id = 2
    ts.pad_id = 3
    ts.unk_piece = vocab[unk_id][0]
    ts.bos_piece = vocab[1][0]
    ts.eos_piece = vocab[2][0]
    ts.pad_piece = vocab[3][0]
    ts.byte_fallback = byte_fallback
    ts.treat_whitespace_as_suffix = False
    ts.split_by_unicode_script = True
    ts.split_by_whitespace = True
    ts.split_by_number = True
    ts.allow_whitespace_only_pieces = True
    ts.character_coverage = 0.9995
    ts.unk_surface = " \u2047 "

    ns = mp.normalizer_spec
    ns.name = "identity"
    ns.add_dummy_prefix = add_dummy_prefix
    ns.remove_extra_whitespaces = False
    ns.escape_whitespaces = escape_ws
    ns.precompiled_charsmap = b""
    if normalizer is not None:
        raise SystemExit("normalizer is not null; charsmap conversion not implemented")

    n_byte = n_ctrl = n_user = n_unk = n_norm = 0
    for i, (piece, score) in enumerate(vocab):
        p = mp.pieces.add()
        p.piece = piece
        p.score = float(score)
        if i == unk_id:
            p.type = T.UNKNOWN
            n_unk += 1
        elif byte_fallback and len(piece) == 6 and piece.startswith("<0x") and piece.endswith(">"):
            # byte_type="normal" ＝ Microsoft.ML.Tokenizers の ByteCodeToIdOffset 回避
            p.type = T.BYTE if byte_type == "byte" else T.NORMAL
            n_byte += 1
        elif i in added:
            if added[i].get("special") and control_mode == "control":
                p.type = T.CONTROL
                n_ctrl += 1
            elif added[i].get("special"):
                p.type = T.USER_DEFINED
                n_user += 1
            elif user_defined_mode == "added":
                p.type = T.USER_DEFINED
                n_user += 1
            else:
                p.type = T.NORMAL
                n_norm += 1
        else:
            p.type = T.NORMAL
            n_norm += 1

    blob = mp.SerializeToString()
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "wb") as f:
        f.write(blob)
    print(json.dumps({
        "out": out_path, "bytes": len(blob), "vocab": len(vocab),
        "unk_id": unk_id, "byte_fallback": byte_fallback,
        "add_dummy_prefix": add_dummy_prefix, "escape_whitespaces": escape_ws,
        "types": {"NORMAL": n_norm, "BYTE": n_byte, "CONTROL": n_ctrl,
                  "USER_DEFINED": n_user, "UNKNOWN": n_unk},
    }, ensure_ascii=False))


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--tokenizer-json", default=DEFAULT_TOK)
    ap.add_argument("--out", required=True)
    ap.add_argument("--user-defined-mode", default="added", choices=["added", "normal"])
    ap.add_argument("--byte-type", default="byte", choices=["byte", "normal"])
    ap.add_argument("--control-mode", default="control", choices=["control", "user"])
    a = ap.parse_args()
    build(a.tokenizer_json, a.out, a.user_defined_mode, a.byte_type, a.control_mode)
