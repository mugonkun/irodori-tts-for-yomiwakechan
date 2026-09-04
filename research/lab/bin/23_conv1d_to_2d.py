"""23 便: DirectML EP が 1 次元 ConvTranspose を受けないので、(N,C,L) を (N,C,1,L) に
挟み替えて 2 次元 ConvTranspose にするグラフ手術。重みは dims を [Ci,Co,K]->[Ci,Co,1,K] に
書き換えるだけ（生バイトは不変＝external data はそのまま使える）。
  python 23_conv1d_to_2d.py dacvae_decoder_fp32_az0.onnx dacvae_decoder_fp32_dml.onnx
"""
import sys
from pathlib import Path
import onnx
from onnx import helper, TensorProto

LAB = Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
src, dst = LAB / "onnx" / sys.argv[1], LAB / "onnx" / sys.argv[2]
m = onnx.load(str(src), load_external_data=False)
g = m.graph
inits = {i.name: i for i in g.initializer}
axes_name = "_ct2d_axes2"
g.initializer.append(helper.make_tensor(axes_name, TensorProto.INT64, [1], [2]))
new_nodes = []
n_conv = 0
for node in g.node:
    if node.op_type != "ConvTranspose":
        new_nodes.append(node)
        continue
    at = {a.name: a for a in node.attribute}
    strides = list(at["strides"].ints) if "strides" in at else [1]
    if len(strides) != 1:
        new_nodes.append(node)
        continue
    n_conv += 1
    pads = list(at["pads"].ints) if "pads" in at else [0, 0]
    dil = list(at["dilations"].ints) if "dilations" in at else [1]
    opad = list(at["output_padding"].ints) if "output_padding" in at else [0]
    group = at["group"].i if "group" in at else 1
    w = inits[node.input[1]]
    if len(w.dims) == 3:
        w.dims.insert(2, 1)
    x_in, y_out = node.input[0], node.output[0]
    x4 = x_in + "_ct4d"
    y4 = y_out + "_ct4d"
    new_nodes.append(helper.make_node("Unsqueeze", [x_in, axes_name], [x4],
                                      name=node.name + "_unsq"))
    ins = [x4, node.input[1]] + list(node.input[2:])
    new_nodes.append(helper.make_node(
        "ConvTranspose", ins, [y4], name=node.name + "_2d",
        strides=[1, strides[0]], pads=[0, pads[0], 0, pads[1]],
        dilations=[1, dil[0]], output_padding=[0, opad[0]], group=group))
    new_nodes.append(helper.make_node("Squeeze", [y4, axes_name], [y_out],
                                      name=node.name + "_sq"))
del g.node[:]
g.node.extend(new_nodes)
for vi in list(g.value_info):
    g.value_info.remove(vi)
onnx.save(m, str(dst))
print("converted %d ConvTranspose(1d) -> 2d ; wrote %s" % (n_conv, dst.name))
