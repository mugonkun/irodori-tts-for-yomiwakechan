# 23 便・段5: DML の負荷中に GPU perf カウンタを取る（20 §5.3 と同じ方法）。
# 使い方: powershell -NoProfile -File 23_gpu_counters.ps1 -OutJson ..\out\23_gpu_counters.json
param([string]$OutJson = "..\out\23_gpu_counters.json", [int]$Reps = 12)

$lab = "C:\Users\mugonkun\source\repos\irodori-native-research\lab"
function Snap($tag) {
  $eng = @()
  try {
    (Get-Counter "\GPU Engine(*)\Utilization Percentage" -ErrorAction Stop).CounterSamples |
      Where-Object { $_.CookedValue -gt 1 } |
      ForEach-Object { $eng += @{ instance = $_.InstanceName; value = [math]::Round($_.CookedValue,2) } }
  } catch {}
  $ded = @(); $com = @(); $shr = @()
  try { (Get-Counter "\GPU Adapter Memory(*)\Dedicated Usage" -ErrorAction Stop).CounterSamples |
      Where-Object { $_.CookedValue -gt 0 } |
      ForEach-Object { $ded += @{ instance = $_.InstanceName; mb = [math]::Round($_.CookedValue/1MB,1) } } } catch {}
  try { (Get-Counter "\GPU Adapter Memory(*)\Total Committed" -ErrorAction Stop).CounterSamples |
      Where-Object { $_.CookedValue -gt 0 } |
      ForEach-Object { $com += @{ instance = $_.InstanceName; mb = [math]::Round($_.CookedValue/1MB,1) } } } catch {}
  try { (Get-Counter "\GPU Adapter Memory(*)\Shared Usage" -ErrorAction Stop).CounterSamples |
      Where-Object { $_.CookedValue -gt 0 } |
      ForEach-Object { $shr += @{ instance = $_.InstanceName; mb = [math]::Round($_.CookedValue/1MB,1) } } } catch {}
  return @{ tag = $tag; engines = $eng; dedicated = $ded; committed = $com; shared = $shr }
}

$res = @{}
$res.before = Snap "before"
$p = Start-Process -FilePath "$lab\.venv-dml\Scripts\python.exe" `
  -ArgumentList "$lab\bin\40_loop_dml.py","--ep","dml","--model","dit_step.onnx","--steps","40","--tag","dml_soak","--reps","$Reps" `
  -WorkingDirectory $lab -PassThru -WindowStyle Hidden `
  -RedirectStandardOutput "$lab\out\23_soak_stdout.log" -RedirectStandardError "$lab\out\23_soak_stderr.log"
$res.pid = $p.Id
Start-Sleep -Seconds 8
$res.during1 = Snap "during1"
Start-Sleep -Seconds 6
$res.during2 = Snap "during2"
Start-Sleep -Seconds 6
$res.during3 = Snap "during3"
$p.WaitForExit()
Start-Sleep -Seconds 3
$res.after = Snap "after"
$res | ConvertTo-Json -Depth 8 | Out-File -Encoding utf8 $OutJson
Write-Output "wrote $OutJson (pid $($p.Id))"
