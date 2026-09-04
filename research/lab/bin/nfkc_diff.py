"""Python unicodedata.normalize("NFKC") と .NET String.Normalize(FormKC) の突き合わせ。"""
import json
import sys
import unicodedata

LAB = r"C:/Users/mugonkun/source/repos/irodori-native-research/lab"


def cps(s):
    return " ".join("U+%04X" % ord(c) for c in s)


def main():
    cs = json.load(open(LAB + "/out/nfkc_cs.json", encoding="utf-8"))["items"]
    diffs = []
    n_by_group = {}
    for it in cs:
        src = it["src"]
        py = unicodedata.normalize("NFKC", src)
        g = it["cp"]
        key = "range" if g >= 0 else ("emoji" if g == -1 else "combining")
        n_by_group[key] = n_by_group.get(key, 0) + 1
        if py != it["nfkc"]:
            diffs.append({
                "group": key,
                "cp": ("U+%04X" % g) if g >= 0 else cps(src),
                "src": src,
                "py": py, "py_cp": cps(py),
                "cs": it["nfkc"], "cs_cp": cps(it["nfkc"]),
            })
    print("checked:", n_by_group, "total", len(cs))
    print("diffs:", len(diffs))
    for d in diffs:
        print("  %-10s %-24s src=%r py=%r(%s) cs=%r(%s)"
              % (d["group"], d["cp"], d["src"], d["py"], d["py_cp"], d["cs"], d["cs_cp"]))
    json.dump({"checked": n_by_group, "total": len(cs), "diffs": diffs},
              open(LAB + "/out/nfkc_diff.json", "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)

    # normalize_text の突き合わせ
    from irodori_tts.text_normalization import normalize_text
    rows = json.load(open(LAB + "/out/norm_cs.json", encoding="utf-8"))["rows"]
    bad = []
    for r in rows:
        p = normalize_text(r["input"])
        if p != r["output"]:
            bad.append({"src": r["src"], "input": r["input"], "py": p, "cs": r["output"]})
    print("normalize_text: %d / %d mismatch" % (len(bad), len(rows)))
    for b in bad:
        print("   ", json.dumps(b, ensure_ascii=False))
    # 04 ノート §1-2 の表を再掲（C# 出力）
    print("--- 04 ノート §1-2 表の C# 出力 ---")
    for r in rows:
        if r["src"] == "table":
            print("  %-20r -> %r" % (r["input"], r["output"]))
    json.dump({"mismatch": bad, "rows": rows},
              open(LAB + "/out/norm_diff.json", "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    print("python unidata:", unicodedata.unidata_version)


if __name__ == "__main__":
    main()
