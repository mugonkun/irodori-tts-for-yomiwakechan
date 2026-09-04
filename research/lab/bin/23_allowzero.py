"""23 便: DirectML EP の Reshape が落ちる条件の切り分けと、allowzero 剥がしの回避策。
  段A: 最小再現（動的 shape の Reshape × allowzero 0/1）を作って DML で走らせる
  段B: lab/onnx の該当グラフから allowzero=1 を剥がした版を lab/onnx/*_az0.onnx に出す
"""
import sys
from pathlib import Path
import numpy as np
import onnx
from onnx import helper, TensorProto

LAB = Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")


def make_min(path, allowzero):
    # x:(1,4,8) -> Shape/Gather/Concat で (1,4,8) を作り Reshape
    nodes = [
        helper.make_node("Shape", ["x"], ["sh"]),
        helper.make_node("Gather", ["sh", "i0"], ["d0"], axis=0),
        helper.make_node("Gather", ["sh", "i1"], ["d1"], axis=0),
        helper.make_node("Gather", ["sh", "i2"], ["d2"], axis=0),
        helper.make_node("Concat", ["d0", "d1", "d2"], ["newshape"], axis=0),
        helper.make_node("Reshape", ["x", "newshape"], ["y"], allowzero=allowzero),
    ]
    inits = [
        helper.make_tensor("i0", TensorProto.INT64, [1], [0]),
        helper.make_tensor("i1", TensorProto.INT64, [1], [1]),
        helper.make_tensor("i2", TensorProto.INT64, [1], [2]),
    ]
    g = helper.make_graph(
        nodes, "az%d" % allowzero,
        [helper.make_tensor_value_info("x", TensorProto.FLOAT, [1, 4, "S"])],
        [helper.make_tensor_value_info("y", TensorProto.FLOAT, [1, 4, "S"])],
        inits)
    m = helper.make_model(g, opset_imports=[helper.make_opsetid("", 20)])
    m.ir_version = 10
    onnx.save(m, str(path))


def strip_allowzero(src: Path, dst: Path) -> int:
    m = onnx.load(str(src), load_external_data=False)
    n = 0
    for node in m.graph.node:
        if node.op_type == "Reshape":
            for i, a in enumerate(node.attribute):
                if a.name == "allowzero" and a.i == 1:
                    del node.attribute[i]
                    n += 1
                    break
    onnx.save(m, str(dst))  # external data の location は相対のまま同ディレクトリを指す
    return n


if __name__ == "__main__":
    if sys.argv[1] == "make":
        for az in (0, 1):
            p = LAB / "out" / ("23_min_reshape_az%d.onnx" % az)
            make_min(p, az)
            print("wrote", p)
    else:
        for name in sys.argv[2:]:
            src = LAB / "onnx" / name
            dst = LAB / "onnx" / (src.stem + "_az0.onnx")
            n = strip_allowzero(src, dst)
            print("%s -> %s (stripped %d)" % (name, dst.name, n))
