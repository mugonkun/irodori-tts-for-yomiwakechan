import numpy as np, onnxruntime as ort, time, statistics, json
from pathlib import Path
LAB=Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
lat=np.load(LAB/'out'/'latent_steps40.npy').transpose(0,2,1).copy()
out={}
ref=None
for name in ('dacvae_decoder_fp32.onnx','dacvae_decoder_fp32_dml.onnx','dacvae_decoder_fp16_dml.onnx'):
    for ep in ('cpu','dml'):
        so=ort.SessionOptions(); so.log_severity_level=3
        if ep=='dml': so.enable_mem_pattern=False; so.execution_mode=ort.ExecutionMode.ORT_SEQUENTIAL
        else: so.intra_op_num_threads=8; so.inter_op_num_threads=1
        P=['DmlExecutionProvider','CPUExecutionProvider'] if ep=='dml' else ['CPUExecutionProvider']
        k='%s/%s'%(name,ep)
        try:
            s=ort.InferenceSession(str(LAB/'onnx'/name), so, providers=P)
            t=time.perf_counter(); o=s.run(None,{'latent':lat})[0]; first=(time.perf_counter()-t)*1000
            ts=[]
            for _ in range(8):
                t=time.perf_counter(); s.run(None,{'latent':lat}); ts.append((time.perf_counter()-t)*1000)
            if name=='dacvae_decoder_fp32.onnx' and ep=='cpu': ref=np.asarray(o,dtype=np.float64)
            d=float(np.max(np.abs(np.asarray(o,dtype=np.float64)-ref))) if ref is not None else None
            out[k]={'providers':s.get_providers(),'first_ms':round(first,1),'median_ms':round(statistics.median(ts),1),
                    'shape':list(np.asarray(o).shape),'max_abs_vs_cpu_fp32':d,'absmax':float(np.abs(np.asarray(o,dtype=np.float64)).max())}
            print(k, out[k])
        except Exception as e:
            out[k]={'error':'%s: %s'%(type(e).__name__,str(e)[:150])}
            print(k,'ERR',type(e).__name__)
(LAB/'out'/'23_decoder_dml.json').write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8')
