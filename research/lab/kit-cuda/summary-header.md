<!-- run-cuda-kit.ps1 が results/summary.md の先頭にそのまま貼り付ける説明。 -->
> このファイルは `run-cuda-kit.ps1` が自動生成した。数値はすべて実測。
> 読み方＝**§3 の warm RTF が 1.00 を切れば「実時間より速い」**。
> `cold` は同一プロセスの 1 発目（CUDA の形状チューニング込み）、`warm` は 2 発目以降の中央値。
> CPU 基準（AMD Ryzen AI MAX+ 395・fp32・OMP=8・同一 seed 1234・同一文面）は
> 短文 steps40 で total_to_decode 15.26 s / RTF 4.06、steps10 で 6.73 s / RTF 1.79、
> 長文 steps40 で 40.29 s / RTF 3.69、steps10 で 14.05 s / RTF 1.29。
