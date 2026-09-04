@echo off
setlocal
set "SRC=%~dp0results"
set "DST=N:\irodori-native-research\results-ssd"
if not exist "%SRC%" mkdir "%SRC%"
echo Mirroring "%SRC%" to "%DST%" every minute (never deletes on N:). Close this window to stop.
robocopy "%SRC%" "%DST%" /E /R:1 /W:2 /NP /NFL /NDL /MOT:1
