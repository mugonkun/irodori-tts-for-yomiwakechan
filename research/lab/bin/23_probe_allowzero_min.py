import numpy as np, onnxruntime as ort
from pathlib import Path
LAB=Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
def so_dml():
    s=ort.SessionOptions(); s.log_severity_level=3
    s.enable_mem_pattern=False; s.execution_mode=ort.ExecutionMode.ORT_SEQUENTIAL
    return s
x=np.random.default_rng(0).standard_normal((1,4,8)).astype(np.float32)
for az in (0,1):
    p=LAB/'out'/('23_min_reshape_az%d.onnx'%az)
    for ep in (['CPUExecutionProvider'],['DmlExecutionProvider','CPUExecutionProvider']):
        try:
            s=ort.InferenceSession(str(p), so_dml(), providers=ep)
            o=s.run(None,{'x':x})[0]
            print('allowzero=%d ep=%-4s OK diff=%g'%(az, ep[0][:3], float(np.abs(o-x).max())))
        except Exception as e:
            print('allowzero=%d ep=%-4s ERR %s'%(az, ep[0][:3], type(e).__name__))
# 回避策の検証
lat=np.load(LAB/'out'/'latent_steps40.npy')
print('latent', lat.shape, lat.dtype)
