$ErrorActionPreference = 'Stop'
$log = 'C:\Mac\Home\TinyPix\3.5pro\artifacts\ui-shots\bootstrap-log.txt'
$script = 'C:\Mac\Home\TinyPix\3.5pro\artifacts\ui-shots\run-capture.ps1'
function L([string]$m) { "$(Get-Date -Format 'HH:mm:ss.fff') $m" | Add-Content -LiteralPath $log }
"=== bootstrap $(Get-Date -Format o) ===" | Set-Content -LiteralPath $log
try {
    L "script exists: $(Test-Path -LiteralPath $script)"
    Get-ScheduledTask -TaskName 'TinyPixUICapture' -ErrorAction SilentlyContinue |
        Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue
    L 'old task removed'
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$script`""
    $principal = New-ScheduledTaskPrincipal -UserId 'huashu' -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName 'TinyPixUICapture' -Action $action -Principal $principal -Force | Out-Null
    L 'task registered'
    Start-ScheduledTask -TaskName 'TinyPixUICapture'
    L 'task started'
} catch {
    L "ERROR $($_.Exception.GetType().FullName): $($_.Exception.Message)"
    L "SCRIPTSTACK $($_.ScriptStackTrace)"
    exit 1
}
exit 0
