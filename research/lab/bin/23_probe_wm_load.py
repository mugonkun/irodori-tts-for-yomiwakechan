import numpy as np, onnxruntime as ort, json, time, statistics
from pathlib import Path
LAB=Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
def mk(name,ep):
    so=ort.SessionOptions(); so.log_severity_level=3
    if ep=='dml': so.enable_mem_pattern=False; so.execution_mode=ort.ExecutionMode.ORT_SEQUENTIAL
    else: so.intra_op_num_threads=8; so.inter_op_num_threads=1
    P=['DmlExecutionProvider','CPUExecutionProvider'] if ep=='dml' else ['CPUExecutionProvider']
    return ort.InferenceSession(str(LAB/'onnx'/name), so, providers=P)
for name in ('sc_istft_manual.onnx','sc_istft_irfft.onnx','sc_full_static.onnx','sc_dec_c_fp32.onnx','sc_enc_c_fp32.onnx','sc_istft.onnx'):
    try:
        s=mk(name,'cpu')
        print('%-26s LOAD-OK inputs=%s' % (name, [(i.name,i.shape) for i in s.get_inputs()]))
    except Exception as e:
        print('%-26s LOAD-ERR %s' % (name, str(e)[:150]))
