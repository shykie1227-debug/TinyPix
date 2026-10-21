# run-capture.ps1 - ASCII-only wrapper: run capture-ui.ps1 and capture all output.
$ErrorActionPreference = 'Continue'
$scriptDir = 'C:\Mac\Home\TinyPix\3.5pro\artifacts\ui-shots2'
$target = Join-Path $scriptDir 'capture-ui.ps1'
$out = Join-Path $scriptDir 'task-output.txt'
"=== wrapper start $(Get-Date -Format o) session=$((Get-Process -Id $PID).SessionId) user=$([Security.Principal.WindowsIdentity]::GetCurrent().Name) interactive=$([Environment]::UserInteractive) ===" |
    Set-Content -LiteralPath $out
try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $target *>> $out
    "wrapper exit=$LASTEXITCODE" | Add-Content -LiteralPath $out
} catch {
    "WRAPPER ERROR $($_.Exception.Message)" | Add-Content -LiteralPath $out
}
exit 0
