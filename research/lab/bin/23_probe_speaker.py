import json, sys
import numpy as np, onnxruntime as ort
so = ort.SessionOptions(); so.log_severity_level = 0
so.enable_mem_pattern = False; so.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
s = ort.InferenceSession('onnx/speaker_encoder.onnx', so, providers=['DmlExecutionProvider','CPUExecutionProvider'])
feeds = {'ref_patched': np.random.default_rng(1).standard_normal((1,10,128)).astype(np.float32),
         'ref_mask': np.ones((1,10), dtype=bool)}
try:
    o = s.run(None, feeds); print('OK', [x.shape for x in o])
except Exception as e:
    print('EXC', type(e).__name__, repr(str(e))[:400])
