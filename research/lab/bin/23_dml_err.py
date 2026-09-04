"""23 便: DML で落ちるグラフの逐語エラーを拾う（ORT の C++ 例外文字列が cp932 混じりで
pybind の utf-8 デコードに失敗するため、stderr のログを utf-16-le で読み直す）。"""
import json, sys
from pathlib import Path
import numpy as np, onnxruntime as ort
sys.path.insert(0, str(Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab/kit-cuda")))
import bench_onnx as B
LAB = Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
name = sys.argv[1]
p = LAB / "onnx" / name
specs = json.loads((p.parent / (p.name + ".inputs.json")).read_text(encoding="utf-8"))
so = ort.SessionOptions(); so.log_severity_level = 2
so.enable_mem_pattern = False; so.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
s = ort.InferenceSession(str(p), so, providers=["DmlExecutionProvider","CPUExecutionProvider"])
rng = np.random.default_rng(1234); feeds = {}
for inp in s.get_inputs():
    arr, _ = B.make_input(inp.name, inp.type, inp.shape, specs.get(inp.name), rng, 4, 128)
    feeds[inp.name] = arr
try:
    o = s.run(None, feeds); print("OK", [np.asarray(x).shape for x in o])
except Exception as e:
    print("EXC", type(e).__name__)
