"""GPU enumeration facts for the kit (V-2 / V-3 of note 30).

Writes results/gpu_props.json: import-torch time, device_count, per-device
name/uuid/pci fields (whatever the CUDA build exposes), the full attribute list
of torch.cuda.get_device_properties(0), the CUDA_* environment, and the output
of `nvidia-smi -L` / `--query-gpu` if nvidia-smi can be found.
Read-only: it never writes outside results/.
"""
import json
import os
import shutil
import subprocess
import sys
import time

out = {"argv": sys.argv, "python": sys.version, "env": {}}
for k in ("CUDA_VISIBLE_DEVICES", "CUDA_DEVICE_ORDER", "HIP_VISIBLE_DEVICES", "ROCR_VISIBLE_DEVICES"):
    out["env"][k] = os.environ.get(k)

t0 = time.perf_counter()
import torch  # noqa: E402

out["import_torch_s"] = round(time.perf_counter() - t0, 3)
out["torch"] = torch.__version__
out["torch_cuda"] = torch.version.cuda
out["torch_hip"] = torch.version.hip
out["cuda_available"] = torch.cuda.is_available()
devices = []
if out["cuda_available"]:
    t0 = time.perf_counter()
    n = torch.cuda.device_count()
    out["device_count_s"] = round(time.perf_counter() - t0, 4)
    out["device_count"] = n
    for i in range(n):
        p = torch.cuda.get_device_properties(i)
        d = {"index": i, "name": p.name, "total_memory_mb": round(p.total_memory / 1048576.0, 1),
             "major": p.major, "minor": p.minor, "multi_processor_count": p.multi_processor_count}
        for attr in ("uuid", "pci_bus_id", "pci_device_id", "pci_domain_id", "L2_cache_size", "is_integrated", "is_multi_gpu_board"):
            d[attr] = str(getattr(p, attr)) if hasattr(p, attr) else None
        d["attributes"] = sorted(a for a in dir(p) if not a.startswith("_"))
        devices.append(d)
out["devices"] = devices

smi = shutil.which("nvidia-smi")
cands = [smi, r"C:\Windows\System32\nvidia-smi.exe",
         r"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe"]
out["nvidia_smi"] = {"which": smi, "found": None, "list": None, "query": None, "error": None}
for c in cands:
    if c and os.path.isfile(c):
        out["nvidia_smi"]["found"] = c
        try:
            t0 = time.perf_counter()
            r = subprocess.run([c, "-L"], capture_output=True, text=True, timeout=30)
            out["nvidia_smi"]["list"] = r.stdout.strip()
            out["nvidia_smi"]["list_s"] = round(time.perf_counter() - t0, 3)
            r = subprocess.run([c, "--query-gpu=index,name,uuid,pci.bus_id,driver_version", "--format=csv"],
                               capture_output=True, text=True, timeout=30)
            out["nvidia_smi"]["query"] = r.stdout.strip()
        except Exception as exc:  # noqa: BLE001
            out["nvidia_smi"]["error"] = "%s: %s" % (type(exc).__name__, exc)
        break

dst = sys.argv[1] if len(sys.argv) > 1 else "results/gpu_props.json"
os.makedirs(os.path.dirname(dst) or ".", exist_ok=True)
with open(dst, "w", encoding="utf-8") as f:
    json.dump(out, f, ensure_ascii=False, indent=1)
print(json.dumps(out, ensure_ascii=False, indent=1))
