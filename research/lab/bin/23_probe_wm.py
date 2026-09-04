import numpy as np, onnxruntime as ort, json, time, statistics
from pathlib import Path
LAB=Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
def mk(name,ep):
    so=ort.SessionOptions(); so.log_severity_level=3
    if ep=='dml': so.enable_mem_pattern=False; so.execution_mode=ort.ExecutionMode.ORT_SEQUENTIAL
    else: so.intra_op_num_threads=8; so.inter_op_num_threads=1
    P=['DmlExecutionProvider','CPUExecutionProvider'] if ep=='dml' else ['CPUExecutionProvider']
    return ort.InferenceSession(str(LAB/'onnx'/name), so, providers=P)
rng=np.random.default_rng(1234)
CASES={
 'sc_istft_manual.onnx': {'mag':rng.standard_normal((1,2049,83)).astype(np.float32),
                          'phase':rng.standard_normal((1,2049,83)).astype(np.float32)},
 'sc_enc_c_fp32.onnx': {'carrier':rng.standard_normal((1,1,2049,83)).astype(np.float32),
                        'msg':np.ones((1,1,5,83),np.float32)},
 'sc_dec_c_fp32.onnx': {'merged':rng.standard_normal((1,96,2049,83)).astype(np.float32)},
 'sc_stft.onnx': {'x':rng.standard_normal((1,167936)).astype(np.float32)},
}
res={}
for name,f in CASES.items():
    ref=None
    for ep in ('cpu','dml'):
        try:
            s=mk(name,ep); o=s.run(None,f)
            ts=[]
            for _ in range(8):
                t=time.perf_counter(); s.run(None,f); ts.append((time.perf_counter()-t)*1000)
            k='%s/%s'%(name,ep)
            rec={'providers':s.get_providers(),'median_ms':round(statistics.median(ts),3)}
            if ep=='cpu': ref=[np.asarray(x,dtype=np.float64) for x in o]
            else:
                rec['per_output_max_abs']=[round(float(np.max(np.abs(np.asarray(b,dtype=np.float64)-a))),8) for a,b in zip(ref,o)]
                rec['per_output_absmax']=[round(float(np.max(np.abs(a))),4) for a in ref]
                if name=='sc_stft.onnx':
                    # phase は 2pi の巻き戻りが出るので cos/sin で比べ直す
                    a=ref[1]; b=np.asarray(o[1],dtype=np.float64)
                    rec['phase_cos_sin_max_abs']=round(float(max(np.max(np.abs(np.cos(a)-np.cos(b))),np.max(np.abs(np.sin(a)-np.sin(b))))),8)
            res[k]=rec; print(k, json.dumps(rec,ensure_ascii=False))
            del s
        except Exception as e:
            print('%s/%s ERR %s'%(name,ep,type(e).__name__))
(LAB/'out'/'23_watermark_dml.json').write_text(json.dumps(res,ensure_ascii=False,indent=2),encoding='utf-8')
