import numpy as np, onnxruntime as ort, time, json, statistics
from pathlib import Path
LAB=Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
z=np.load(LAB/'tmp'/'cond_real.npz')
feeds={'text_ids':z['text_ids'],'text_mask':z['text_mask_in'],'cap_ids':z['cap_ids'],
       'cap_mask':z['cap_mask_in'],'ref_latent':z['ref_latent'],'ref_mask':z['ref_mask_in']}
res={}
for name in ('encode_conditions_sdpa.onnx','encode_conditions_sdpa_az0.onnx'):
    for ep in ('cpu','dml'):
        so=ort.SessionOptions(); so.log_severity_level=3
        if ep=='dml': so.enable_mem_pattern=False; so.execution_mode=ort.ExecutionMode.ORT_SEQUENTIAL
        else: so.intra_op_num_threads=8; so.inter_op_num_threads=1
        P=['DmlExecutionProvider','CPUExecutionProvider'] if ep=='dml' else ['CPUExecutionProvider']
        try:
            s=ort.InferenceSession(str(LAB/'onnx'/name), so, providers=P)
            got=s.get_providers()
            t=time.perf_counter(); o=s.run(None,feeds); first=(time.perf_counter()-t)*1000
            ts=[]
            for _ in range(8):
                t=time.perf_counter(); s.run(None,feeds); ts.append((time.perf_counter()-t)*1000)
            key='%s/%s'%(name,ep)
            res[key]={'providers':got,'first_ms':round(first,1),'median_ms':round(statistics.median(ts),1),
                      'out_shapes':[list(np.asarray(x).shape) for x in o]}
            if ep=='cpu': res[key]['ref']= [np.asarray(x) for x in o]
            print(key, res[key]['providers'], 'first=%.1f med=%.1f'%(first, statistics.median(ts)))
            if ep=='dml' and (name+'/cpu') in res:
                ref=res[name+'/cpu']['ref']
                d=[float(np.max(np.abs(np.asarray(a,dtype=np.float64)-np.asarray(b,dtype=np.float64)))) if np.asarray(a).dtype!=bool else float(np.max(np.asarray(a)!=np.asarray(b))) for a,b in zip(ref,o)]
                print('   max_abs_diff_vs_cpu =', max(d), d)
        except Exception as e:
            print(name, ep, 'ERR', type(e).__name__, str(e)[:120])
