#!/usr/bin/env python3
"""Write the venv's torch identity to JSON. Called by run-cuda-kit.ps1 (step d_torch_guard).

A separate file on purpose: passing `-c "...{'a':1}..."` through PowerShell 5.1 to a
native exe strips the double quotes and breaks the one-liner.
"""
import json
import sys
from pathlib import Path

out = {"ok": False}
try:
    import torch

    out["ok"] = True
    out["v"] = torch.__version__
    out["cuda"] = torch.version.cuda
    out["hip"] = getattr(torch.version, "hip", None)
    out["avail"] = bool(torch.cuda.is_available())
    try:
        out["cudnn"] = torch.backends.cudnn.version()
    except Exception as exc:
        out["cudnn"] = "error: %r" % (exc,)
    if out["avail"]:
        out["device_count"] = torch.cuda.device_count()
        out["device_names"] = [torch.cuda.get_device_name(i) for i in range(out["device_count"])]
except Exception as exc:
    out["error"] = "%s: %s" % (type(exc).__name__, exc)

text = json.dumps(out, ensure_ascii=True)
if len(sys.argv) > 1:
    Path(sys.argv[1]).write_text(text, encoding="utf-8")
print(text)
sys.exit(0 if out["ok"] else 1)
