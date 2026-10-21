# probe-start.ps1 - start TinyPix with the startup trace enabled and report why it dies.
$ErrorActionPreference = 'Continue'
$dir = 'C:\Mac\Home\TinyPix\3.5pro\artifacts\ui-shots2'
$log = Join-Path $dir 'probe-log.txt'
$exe = 'C:\TinyPixBuild2\src\TinyPix.App\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\TinyPix.exe'
$exeDir = Split-Path -Parent $exe
$trace = Join-Path $env:TEMP 'tinypix-startup.log'

function L([string]$m) { "$(Get-Date -Format 'HH:mm:ss.fff') $m" | Add-Content -LiteralPath $log }
"=== probe $(Get-Date -Format o) session=$((Get-Process -Id $PID).SessionId) user=$env:USERNAME ===" | Set-Content -LiteralPath $log

L "exe exists = $(Test-Path -LiteralPath $exe)"
L "portable.flag exists = $(Test-Path -LiteralPath (Join-Path $exeDir 'portable.flag'))"
L "exe dir listing:"
Get-ChildItem -LiteralPath $exeDir | Select-Object -First 40 | ForEach-Object { L "  $($_.Name)" }
L "exe dir subdirs:"
Get-ChildItem -LiteralPath $exeDir -Directory | ForEach-Object { L "  [dir] $($_.Name)" }

Remove-Item -LiteralPath $trace -Force -ErrorAction SilentlyContinue
$env:TINYPIX_STARTUP_LOG = '1'
L "starting $exe"
$p = Start-Process -FilePath $exe -WorkingDirectory $exeDir -PassThru
if ($null -eq $p) { L "Start-Process returned null"; exit 1 }
L "pid=$($p.Id) waiting up to 25s"
$exited = $p.WaitForExit(25000)
L "WaitForExit returned=$exited"
if ($exited) { L "exit code = $($p.ExitCode) (0x$('{0:X8}' -f $p.ExitCode))" }
else { L "still running; window title = '$($p.MainWindowTitle)'" }

L "--- startup trace ($trace) ---"
if (Test-Path -LiteralPath $trace) {
    Get-Content -LiteralPath $trace | ForEach-Object { L "  $_" }
} else {
    L "  <trace file missing>"
}
L "=== done ==="
