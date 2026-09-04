import numpy as np, onnxruntime as ort
for r in (1,2):
    p='out/23_min_convtranspose_%dd.onnx'%r
    x=np.random.default_rng(1).standard_normal((1,4,16) if r==1 else (1,4,1,16)).astype(np.float32)
    for ep in (['CPUExecutionProvider'],['DmlExecutionProvider','CPUExecutionProvider']):
        s=ort.SessionOptions(); s.log_severity_level=3
        s.enable_mem_pattern=False; s.execution_mode=ort.ExecutionMode.ORT_SEQUENTIAL
        try:
            se=ort.InferenceSession(p,s,providers=ep); o=se.run(None,{'x':x})[0]
            print('%dD %-4s OK %s'%(r,ep[0][:3],o.shape))
        except Exception as e:
            print('%dD %-4s ERR %s'%(r,ep[0][:3],type(e).__name__))
