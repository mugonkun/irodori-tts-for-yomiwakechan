import numpy as np, onnxruntime as ort, time, sys
from pathlib import Path
LAB=Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
name=sys.argv[1]
z=np.load(LAB/'tmp'/'cond_real.npz'); x0=np.load(LAB/'tmp'/'x0.npz')['x0']
B=3
def rep(a): return np.concatenate([a]*B,0)
feeds={'x_t':rep(x0),'t':np.full((B,),0.999,np.float32),
 'text_state':rep(z['text_state']),'text_mask':rep(z['text_mask']),
 'speaker_state':rep(z['speaker_state']),'speaker_mask':rep(z['speaker_mask']),
 'caption_state':rep(z['caption_state']),'caption_mask':rep(z['caption_mask'])}
s=ort.SessionOptions(); s.log_severity_level=2
s.enable_mem_pattern=False; s.execution_mode=ort.ExecutionMode.ORT_SEQUENTIAL
t=time.perf_counter()
se=ort.InferenceSession(str(LAB/'onnx'/name), s, providers=['DmlExecutionProvider','CPUExecutionProvider'])
print('init %.2fs'%(time.perf_counter()-t), se.get_providers())
try:
    t=time.perf_counter(); o=se.run(['v'],feeds)[0]; print('first %.1f ms'%((time.perf_counter()-t)*1000), o.shape, float(np.abs(o).max()))
    ts=[]
    for _ in range(5):
        t=time.perf_counter(); se.run(['v'],feeds); ts.append((time.perf_counter()-t)*1000)
    print('med %.1f ms'%sorted(ts)[2])
except Exception as e:
    print('EXC', type(e).__name__)
