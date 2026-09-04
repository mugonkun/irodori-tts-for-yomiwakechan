"""敵対検分用の入力文字列を JSON に落とし、HF の id 列も同時に出す（.NET と突き合わせるため）。"""
import json
import random
import sys

TOK_DIR = (
    r"C:/Users/mugonkun/.cache/huggingface/hub/models--Aratako--Irodori-TTS-v4.1-Small"
    r"/snapshots/2b28324dc263ed5e6638b3cf3dd94c82ead07b4b/tokenizer"
)
LAB = r"C:/Users/mugonkun/source/repos/irodori-native-research/lab"


def build_inputs():
    specials = ["<unk>", "<s>", "</s>", "<pad>", "<sep>", "<mask>", "<cls>",
                "<|system|>", "<|assistant|>", "<|user|>", "<|code|>", "<|prefix|>",
                "<|middle|>", "<|suffix|>", "<|file|>", "<|tool_calls|>"]
    items = []
    for t in specials:
        items += [t, "前" + t + "後", "あ" + t]
    for b in range(0x00, 0x100):
        items.append("<0x%02X>" % b)

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
    for _ in range(6000):
        n = random.randint(1, 30)
        items.append("".join(random.choice(random.choice(pools)) for _ in range(n)))

    vocab = json.load(open(TOK_DIR + "/tokenizer.json", encoding="utf-8"))["model"]["vocab"]
    for i, (piece, _s) in enumerate(vocab):
        if i < 271 or piece.startswith("<|"):
            continue
        items.append(piece.replace("\u2581", " "))
    return items


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else LAB + "/out/stress_inputs.json"
    items = build_inputs()
    from transformers import AutoTokenizer
    hf = AutoTokenizer.from_pretrained(TOK_DIR, use_fast=True, local_files_only=True)
    ids = [hf.encode(s, add_special_tokens=False) for s in items]
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"sentences": items, "hf": ids}, f, ensure_ascii=False)
    print("wrote", out, len(items))


if __name__ == "__main__":
    main()
