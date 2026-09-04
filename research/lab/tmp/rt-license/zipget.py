"""HTTP Range で remote zip/whl の中央ディレクトリを読み、指定エントリだけを local header 経由で取り出す。

usage: python zipget.py <url> <outdir> <substring> [<substring> ...]
       python zipget.py <url> --list   # 全エントリ名 + local header offset を出す
lab/out/verify17/zipcd.py の read_cd() を流用し、中央ディレクトリから
local header offset (46+ の 42..46 バイト / ZIP64 extra) も拾う点だけが違う。
"""
import json, struct, sys, os, zlib, urllib.request

UA = {"User-Agent": "curl/8.21.0"}


def rng(url, start, end):
    req = urllib.request.Request(url, headers=dict(UA, Range=f"bytes={start}-{end}"))
    with urllib.request.urlopen(req, timeout=300) as r:
        return r.read()


def total(url):
    req = urllib.request.Request(url, headers=UA, method="HEAD")
    with urllib.request.urlopen(req, timeout=120) as r:
        return int(r.headers["Content-Length"])


def read_cd(url):
    n = total(url)
    tail = rng(url, max(0, n - 65557), n - 1)
    i = tail.rfind(b"PK\x05\x06")
    assert i >= 0, "no EOCD"
    cnt, cdsize, cdoff = struct.unpack("<HII", tail[i + 10 : i + 20])
    j = tail.rfind(b"PK\x06\x07")
    if j >= 0:
        z64off = struct.unpack("<Q", tail[j + 8 : j + 16])[0]
        z = rng(url, z64off, z64off + 55)
        assert z[:4] == b"PK\x06\x06", z[:4]
        cnt = struct.unpack("<Q", z[32:40])[0]
        cdsize = struct.unpack("<Q", z[40:48])[0]
        cdoff = struct.unpack("<Q", z[48:56])[0]
    cd = rng(url, cdoff, cdoff + cdsize - 1)
    return n, cnt, cd


def parse(cd):
    out, p = [], 0
    while p + 46 <= len(cd) and cd[p : p + 4] == b"PK\x01\x02":
        method = struct.unpack("<H", cd[p + 10 : p + 12])[0]
        csz, usz = struct.unpack("<II", cd[p + 20 : p + 28])
        nlen, elen, clen = struct.unpack("<HHH", cd[p + 28 : p + 34])
        lho = struct.unpack("<I", cd[p + 42 : p + 46])[0]
        name = cd[p + 46 : p + 46 + nlen].decode("utf-8", "replace")
        extra = cd[p + 46 + nlen : p + 46 + nlen + elen]
        if usz == 0xFFFFFFFF or csz == 0xFFFFFFFF or lho == 0xFFFFFFFF:
            q = 0
            while q + 4 <= len(extra):
                hid, hsz = struct.unpack("<HH", extra[q : q + 4])
                body = extra[q + 4 : q + 4 + hsz]
                if hid == 0x0001:
                    k = 0
                    if usz == 0xFFFFFFFF:
                        usz = struct.unpack("<Q", body[k : k + 8])[0]; k += 8
                    if csz == 0xFFFFFFFF:
                        csz = struct.unpack("<Q", body[k : k + 8])[0]; k += 8
                    if lho == 0xFFFFFFFF:
                        lho = struct.unpack("<Q", body[k : k + 8])[0]; k += 8
                    break
                q += 4 + hsz
        out.append({"n": name, "m": method, "c": csz, "u": usz, "o": lho})
        p += 46 + nlen + elen + clen
    return out


def fetch_entry(url, e):
    """local header を読んで名前長/extra 長を得てから本体を Range 取得し展開する。"""
    lh = rng(url, e["o"], e["o"] + 29)
    assert lh[:4] == b"PK\x03\x04", lh[:4]
    nlen, elen = struct.unpack("<HH", lh[26:30])
    start = e["o"] + 30 + nlen + elen
    raw = rng(url, start, start + e["c"] - 1)
    if e["m"] == 0:
        return raw
    return zlib.decompress(raw, -15)


if __name__ == "__main__":
    url = sys.argv[1]
    n, cnt, cd = read_cd(url)
    ents = parse(cd)
    print(f"# url={url}\n# bytes={n} eocd={cnt} parsed={len(ents)}", file=sys.stderr)
    if sys.argv[2] == "--list":
        json.dump(ents, sys.stdout)
        sys.exit(0)
    outdir = sys.argv[2]
    os.makedirs(outdir, exist_ok=True)
    pats = sys.argv[3:]
    for e in ents:
        if any(p in e["n"] for p in pats):
            data = fetch_entry(url, e)
            fn = os.path.join(outdir, e["n"].replace("/", "__"))
            with open(fn, "wb") as f:
                f.write(data)
            print(f"{len(data):>9}  {e['n']}  -> {fn}")
