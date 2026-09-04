"""HF fast tokenizer と 変換 .model(sentencepiece) の敵対検分。

(a) 特殊トークン文字列を含む文
(b) ランダム文字列（BMP／絵文字／制御・空白を混ぜる）
(c) 全 vocab の piece 単体
を突き合わせ、不一致の型を分類する。
"""
import json
import random
import sys
import unicodedata

TOK_DIR = (
    r"C:/Users/mugonkun/.cache/huggingface/hub/models--Aratako--Irodori-TTS-v4.1-Small"
    r"/snapshots/2b28324dc263ed5e6638b3cf3dd94c82ead07b4b/tokenizer"
)
LAB = r"C:/Users/mugonkun/source/repos/irodori-native-research/lab"


def main():
    spm_path = sys.argv[1] if len(sys.argv) > 1 else LAB + "/out/irodori_tok.model"
    from transformers import AutoTokenizer
    import sentencepiece as spm
    hf = AutoTokenizer.from_pretrained(TOK_DIR, use_fast=True, local_files_only=True)
    sp = spm.SentencePieceProcessor()
    sp.Load(spm_path)

    def cmp(s):
        return hf.encode(s, add_special_tokens=False), sp.EncodeAsIds(s)

    # (a) 特殊トークン
    specials = ["<unk>", "<s>", "</s>", "<pad>", "<sep>", "<mask>", "<cls>",
                "<|system|>", "<|assistant|>", "<|user|>", "<|code|>", "<|prefix|>",
                "<|middle|>", "<|suffix|>", "<|file|>", "<|tool_calls|>"]
    cases_a = []
    for t in specials:
        cases_a.append(t)
        cases_a.append("前" + t + "後")
        cases_a.append("あ" + t)
    bad_a = []
    for s in cases_a:
        h, p = cmp(s)
        if h != p:
            bad_a.append({"s": s, "hf": h, "sp": p,
                          "hf_p": hf.convert_ids_to_tokens(h), "sp_p": sp.EncodeAsPieces(s)})
    print("(a) specials: %d / %d mismatch" % (len(bad_a), len(cases_a)))
    for b in bad_a[:12]:
        print("   ", json.dumps(b, ensure_ascii=False))

    # (b) ランダム
    random.seed(20260904)
    pools = [
        [chr(c) for c in range(0x3040, 0x30FF)],
        [chr(c) for c in range(0x4E00, 0x4E00 + 2000)],
        [chr(c) for c in range(0x20, 0x7F)],
        [chr(c) for c in range(0xFF01, 0xFF60)],
        [chr(c) for c in range(0x2000, 0x2070)],
        [chr(c) for c in range(0x1F300, 0x1F600)],
        list(" \t\n\r\u3000\u00a0\u2003\u200b"),
        [chr(c) for c in range(0x0300, 0x0370)],
        [chr(c) for c in range(0xAC00, 0xAC00 + 500)],
        [chr(c) for c in range(0x0400, 0x0460)],
        [chr(c) for c in range(0x2500, 0x2580)],
        [chr(c) for c in range(0x20000, 0x20100)],
    ]
    bad_b, n_b = [], 0
    for _ in range(6000):
        n = random.randint(1, 30)
        s = "".join(random.choice(random.choice(pools)) for _ in range(n))
        if not s:
            continue
        n_b += 1
        h, p = cmp(s)
        if h != p:
            bad_b.append({"s": s, "hf": h, "sp": p})
    print("(b) random: %d / %d mismatch" % (len(bad_b), n_b))
    for b in bad_b[:10]:
        print("   ", json.dumps(b, ensure_ascii=False))

    # (c) 全 vocab piece 単体（▁ を含む piece は空白に戻して渡す）
    vocab = json.load(open(TOK_DIR + "/tokenizer.json", encoding="utf-8"))["model"]["vocab"]
    bad_c, n_c = [], 0
    for i, (piece, _score) in enumerate(vocab):
        if i < 271 or piece.startswith("<|"):
            continue  # 特殊・バイトは (a) で見る
        s = piece.replace("\u2581", " ")
        n_c += 1
        h, p = cmp(s)
        if h != p:
            bad_c.append({"i": i, "piece": piece, "hf": h, "sp": p})
    print("(c) vocab pieces: %d / %d mismatch" % (len(bad_c), n_c))
    for b in bad_c[:10]:
        print("   ", json.dumps(b, ensure_ascii=False))

    json.dump({"a": bad_a, "b": bad_b[:200], "c": bad_c[:200],
               "counts": {"a": [len(bad_a), len(cases_a)], "b": [len(bad_b), n_b],
                          "c": [len(bad_c), n_c]}},
              open(LAB + "/out/tok_stress_py.json", "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
