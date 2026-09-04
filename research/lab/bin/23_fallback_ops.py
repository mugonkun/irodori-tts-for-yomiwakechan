"""23 便: DML EP で CPU に落ちるノードを op_type 別に数える（ORT の profiling から）。"""
import json, sys, collections
from pathlib import Path
import numpy as np, onnxruntime as ort
sys.path.insert(0, str(Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab/kit-cuda")))
import bench_onnx as B
LAB = Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
name = sys.argv[1]
p = LAB / "onnx" / name
specs = json.loads((p.parent / (p.name + ".inputs.json")).read_text(encoding="utf-8")) \
        if (p.parent / (p.name + ".inputs.json")).is_file() else {}
so = ort.SessionOptions(); so.log_severity_level = 3
so.enable_mem_pattern = False; so.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
so.enable_profiling = True
so.profile_file_prefix = str(LAB / "out" / ("23_prof2_" + p.stem))
s = ort.InferenceSession(str(p), so, providers=["DmlExecutionProvider", "CPUExecutionProvider"])
rng = np.random.default_rng(1234); feeds = {}
for inp in s.get_inputs():
    a, _ = B.make_input(inp.name, inp.type, inp.shape, specs.get(inp.name), rng, 4, 128)
    feeds[inp.name] = a
s.run(None, feeds); s.run(None, feeds)
pf = Path(s.end_profiling())
ev = json.loads(pf.read_text(encoding="utf-8"))
cnt = collections.defaultdict(lambda: collections.Counter())
dur = collections.defaultdict(lambda: collections.Counter())
for e in ev:
    if e.get("cat") == "Node":
        a = e.get("args", {})
        prov = a.get("provider")
        op = a.get("op_name")
        if prov and op:
            cnt[prov][op] += 1
            dur[prov][op] += int(e.get("dur", 0))
out = {"model": name,
       "nodes_per_provider": {k: sum(v.values()) for k, v in cnt.items()},
       "cpu_fallback_ops": dict(cnt.get("CPUExecutionProvider", {})),
       "cpu_fallback_us": dict(dur.get("CPUExecutionProvider", {})),
       "dml_top_ops_us": dict(dur.get("DmlExecutionProvider", collections.Counter()).most_common(12))}
pf.unlink(missing_ok=True)
(LAB / "out" / ("23_fallback_" + p.stem + ".json")).write_text(
    json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(out, ensure_ascii=False))
