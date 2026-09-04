"""Python 側の基準値を出す。

1) HF AutoTokenizer (fast/Rust) の id 列（add_special_tokens=False）
2) 変換した .model を sentencepiece で読んだ id 列
3) normalize_text の出力
を JSON に落とし、1 と 2 の一致を分類する。
"""
import json
import os
import sys
import unicodedata

TOK_DIR = (
    r"C:/Users/mugonkun/.cache/huggingface/hub/models--Aratako--Irodori-TTS-v4.1-Small"
    r"/snapshots/2b28324dc263ed5e6638b3cf3dd94c82ead07b4b/tokenizer"
)
LAB = r"C:/Users/mugonkun/source/repos/irodori-native-research/lab"


def main():
    spm_path = sys.argv[1] if len(sys.argv) > 1 else LAB + "/out/irodori_tok.model"
    out_path = sys.argv[2] if len(sys.argv) > 2 else LAB + "/out/tok_py.json"

    corpus = json.load(open(LAB + "/out/corpus60.json", encoding="utf-8"))
    sents = corpus["sentences"]

    from transformers import AutoTokenizer
    hf = AutoTokenizer.from_pretrained(TOK_DIR, use_fast=True, trust_remote_code=False,
                                       local_files_only=True)

    import sentencepiece as spm
    sp = spm.SentencePieceProcessor()
    sp.Load(spm_path)

    from irodori_tts.text_normalization import normalize_text

    rows = []
    for i, s in enumerate(sents):
        norm = normalize_text(s).strip()
        r = {
            "i": i,
            "raw": s,
            "norm": norm,
            "hf_raw": hf.encode(s, add_special_tokens=False),
            "hf_norm": hf.encode(norm, add_special_tokens=False),
            "sp_raw": sp.EncodeAsIds(s),
            "sp_norm": sp.EncodeAsIds(norm),
        }
        r["hf_raw_pieces"] = hf.convert_ids_to_tokens(r["hf_raw"])
        r["sp_raw_pieces"] = sp.EncodeAsPieces(s)
        rows.append(r)

    n_raw = sum(1 for r in rows if r["hf_raw"] == r["sp_raw"])
    n_norm = sum(1 for r in rows if r["hf_norm"] == r["sp_norm"])

    meta = {
        "spm_model": spm_path,
        "spm_vocab": sp.GetPieceSize(),
        "hf_vocab": hf.vocab_size,
        "match_raw": n_raw, "match_norm": n_norm, "total": len(rows),
        "sentencepiece": __import__("sentencepiece").__version__,
        "tokenizers": __import__("tokenizers").__version__,
        "transformers": __import__("transformers").__version__,
        "python": sys.version,
        "unicodedata_unidata_version": unicodedata.unidata_version,
    }
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump({"meta": meta, "rows": rows}, f, ensure_ascii=False, indent=1)
    print(json.dumps(meta, ensure_ascii=False))

    for r in rows:
        if r["hf_raw"] != r["sp_raw"]:
            print("--- MISMATCH raw i=%d %r" % (r["i"], r["raw"][:40]))
            print("  hf:", r["hf_raw"][:40])
            print("  sp:", r["sp_raw"][:40])
            print("  hf_p:", r["hf_raw_pieces"][:40])
            print("  sp_p:", r["sp_raw_pieces"][:40])
    for r in rows:
        if r["hf_norm"] != r["sp_norm"]:
            print("--- MISMATCH norm i=%d %r" % (r["i"], r["norm"][:40]))
            print("  hf:", r["hf_norm"][:40])
            print("  sp:", r["sp_norm"][:40])


if __name__ == "__main__":
    main()
