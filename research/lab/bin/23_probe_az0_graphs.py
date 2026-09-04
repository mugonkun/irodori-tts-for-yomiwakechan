import numpy as np, onnxruntime as ort, time
from pathlib import Path
LAB=Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
def so(ep):
    s=ort.SessionOptions(); s.log_severity_level=3
    if ep=='dml': s.enable_mem_pattern=False; s.execution_mode=ort.ExecutionMode.ORT_SEQUENTIAL
    else: s.intra_op_num_threads=8; s.inter_op_num_threads=1
    return s
EP={'dml':['DmlExecutionProvider','CPUExecutionProvider'],'cpu':['CPUExecutionProvider']}
lat=np.load(LAB/'out'/'latent_steps40.npy').transpose(0,2,1).copy()  # (1,32,94)
for name in ('dacvae_decoder_fp32_az0.onnx','speaker_encoder_az0.onnx'):
    for ep in ('cpu','dml'):
        p=LAB/'onnx'/name
        try:
            s=ort.InferenceSession(str(p), so(ep), providers=EP[ep])
            if 'decoder' in name: feeds={'latent':lat}
            else: feeds={'ref_patched':np.random.default_rng(1234).standard_normal((1,10,128)).astype(np.float32),'ref_mask':np.ones((1,10),bool)}
            t=time.perf_counter(); o=s.run(None,feeds); first=(time.perf_counter()-t)*1000
            ts=[]
            for _ in range(10):
                t=time.perf_counter(); s.run(None,feeds); ts.append((time.perf_counter()-t)*1000)
            print('%-30s %s OK first=%.1fms med=%.1fms shape=%s absmax=%.5f'%(name,ep,first,sorted(ts)[5],np.asarray(o[0]).shape,float(np.abs(np.asarray(o[0],dtype=np.float64)).max())))
        except Exception as e:
            print('%-30s %s ERR %s %s'%(name,ep,type(e).__name__,str(e)[:120]))
