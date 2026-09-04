import numpy as np, onnxruntime as ort, json
from pathlib import Path
LAB=Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
z=np.load(LAB/'tmp'/'cond_real.npz'); x0=np.load(LAB/'tmp'/'x0.npz')['x0']
def feeds(B,t):
    r=lambda a: np.ascontiguousarray(np.concatenate([a]*B,0))
    return {"x_t":r(x0),"t":np.full((B,),t,np.float32),
            "text_state":r(z["text_state"]),"text_mask":r(z["text_mask"]),
            "speaker_state":r(z["speaker_state"]),"speaker_mask":r(z["speaker_mask"]),
            "caption_state":r(z["caption_state"]),"caption_mask":r(z["caption_mask"])}
def sess(name,ep):
    so=ort.SessionOptions(); so.log_severity_level=3
    if ep=='dml': so.enable_mem_pattern=False; so.execution_mode=ort.ExecutionMode.ORT_SEQUENTIAL
    else: so.intra_op_num_threads=8; so.inter_op_num_threads=1
    P=['DmlExecutionProvider','CPUExecutionProvider'] if ep=='dml' else ['CPUExecutionProvider']
    return ort.InferenceSession(str(LAB/'onnx'/name), so, providers=P)
out={}
f=feeds(1,0.999)
for name in ('dit_step.onnx','dit_step_fp16.onnx'):
    for ep in ('cpu','dml'):
        s=sess(name,ep); v=s.run(["v"],f)[0]
        out['%s/%s'%(name,ep)]={'absmax':float(np.abs(v).max()),'mean':float(v.mean()),
            'std':float(v.std()),'nan':int(np.isnan(v).sum()),'inf':int(np.isinf(v).sum()),
            'head':[round(float(x),5) for x in v.ravel()[:5]]}
        del s
ref=None
print(json.dumps(out,ensure_ascii=False,indent=1))
