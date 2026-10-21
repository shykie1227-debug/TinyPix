$ErrorActionPreference = 'Continue'
$log = 'C:\Mac\Home\TinyPix\3.5pro\artifacts\ui-shots\status.txt'
$t = Get-ScheduledTask -TaskName 'TinyPixUICapture' -ErrorAction SilentlyContinue
if ($null -eq $t) { 'TASK-NOT-FOUND' | Set-Content -LiteralPath $log; exit 0 }
$i = $t | Get-ScheduledTaskInfo
@(
    "state=$($t.State)"
    "lastResult=$($i.LastTaskResult)"
    "lastRun=$($i.LastRunTime)"
    "nextRun=$($i.NextRunTime)"
    "powershellProcs:"
    @(Get-Process powershell, TinyPix -ErrorAction SilentlyContinue |
        ForEach-Object { "  $($_.Name) pid=$($_.Id) session=$($_.SessionId) hwnd=$($_.MainWindowHandle) title='$($_.MainWindowTitle)'" })
    "done=$(Test-Path 'C:\Mac\Home\TinyPix\3.5pro\artifacts\ui-shots\done.txt')"
    "logExists=$(Test-Path 'C:\Mac\Home\TinyPix\3.5pro\artifacts\ui-shots\capture-log.txt')"
) | Set-Content -LiteralPath $log
exit 0
