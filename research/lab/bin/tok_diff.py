"""Python 側（HF / sentencepiece）と .NET 側（Microsoft.ML.Tokenizers）の id 列を突き合わせる。"""
import json
import sys

LAB = r"C:/Users/mugonkun/source/repos/irodori-native-research/lab"


def show(name, a, b, texts, limit=8):
    bad = [i for i in range(len(a)) if a[i] != b[i]]
    print("%-22s mismatch %d / %d" % (name, len(bad), len(a)))
    for i in bad[:limit]:
        print("  i=%d  %r" % (i, texts[i][:50]))
        print("    L:", a[i][:40])
        print("    R:", b[i][:40])
    return bad


def main():
    py = json.load(open(LAB + "/out/tok_py.json", encoding="utf-8"))
    cs = json.load(open(sys.argv[1] if len(sys.argv) > 1 else LAB + "/out/tok_cs.json",
                        encoding="utf-8"))
    prows, crows = py["rows"], cs["rows"]
    assert len(prows) == len(crows)
    print("cs meta:", json.dumps(cs["meta"], ensure_ascii=False))

    raws = [r["raw"] for r in prows]
    norms_py = [r["norm"] for r in prows]
    norms_cs = [r["norm"] for r in crows]

    res = {}
    res["norm_text"] = show("normalize_text(C# vs Py)", norms_cs, norms_py, raws)
    res["raw_ids_hf"] = show("ids raw: C# vs HF", [r["cs_raw"] for r in crows],
                             [r["hf_raw"] for r in prows], raws)
    res["raw_ids_sp"] = show("ids raw: C# vs spm", [r["cs_raw"] for r in crows],
                             [r["sp_raw"] for r in prows], raws)
    res["norm_ids_hf"] = show("ids norm: C# vs HF", [r["cs_norm"] for r in crows],
                              [r["hf_norm"] for r in prows], norms_py)
    json.dump({k: v for k, v in res.items()},
              open(LAB + "/out/tok_diff.json", "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
