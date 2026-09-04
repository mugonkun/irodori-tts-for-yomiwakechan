import numpy as np, onnxruntime as ort, json, time, statistics
from pathlib import Path
LAB=Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
z=np.load(LAB/'tmp'/'cond_real.npz'); x0=np.load(LAB/'tmp'/'x0.npz')['x0']
r=lambda a: np.ascontiguousarray(a)
f={"x_t":r(x0),"t":np.full((1,),0.999,np.float32),
   "text_state":r(z["text_state"]),"text_mask":r(z["text_mask"]),
   "speaker_state":r(z["speaker_state"]),"speaker_mask":r(z["speaker_mask"]),
   "caption_state":r(z["caption_state"]),"caption_mask":r(z["caption_mask"])}
out={}
for name in ('dit_step_fp16_block_fix.onnx',):
    for ep in ('cpu','dml'):
        so=ort.SessionOptions(); so.log_severity_level=3
        if ep=='dml': so.enable_mem_pattern=False; so.execution_mode=ort.ExecutionMode.ORT_SEQUENTIAL
        else: so.intra_op_num_threads=8; so.inter_op_num_threads=1
        P=['DmlExecutionProvider','CPUExecutionProvider'] if ep=='dml' else ['CPUExecutionProvider']
        try:
            s=ort.InferenceSession(str(LAB/'onnx'/name), so, providers=P)
            v=s.run(["v"],f)[0]
            ts=[]
            for _ in range(6):
                t=time.perf_counter(); s.run(["v"],f); ts.append((time.perf_counter()-t)*1000)
            out['%s/%s'%(name,ep)]={'providers':s.get_providers(),'absmax':float(np.abs(v).max()),
              'std':float(v.std()),'median_ms':round(statistics.median(ts),2),
              'head':[round(float(x),5) for x in v.ravel()[:4]]}
        except Exception as e:
            out['%s/%s'%(name,ep)]={'err':type(e).__name__}
        print('%s/%s'%(name,ep), out['%s/%s'%(name,ep)])
