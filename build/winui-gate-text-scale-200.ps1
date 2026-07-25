$ErrorActionPreference = 'Stop'

$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$reportPath = Join-Path $gateRoot 'text-scale-200-report.txt'
$artifactRoot = '\\Mac\Home\TinyPix\3.5pro\artifacts\windows\winui-feasibility-gate'
$setTask = 'TinyPixWinUIGateTextScaleSet'
$launchTask = 'TinyPixWinUIGateTextScaleLaunch'
$uiaTask = 'TinyPixWinUIGateTextScaleUIA'
$exe = Join-Path $gateRoot 'bin\Debug\net10.0-windows10.0.19041.0\win-x64\TinyPix.WinUIGate.exe'
Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue

function Record([string]$message) {
    Add-Content -LiteralPath $reportPath -Value "$(Get-Date -Format o) $message" -Encoding utf8
}

function Wait-Task([string]$taskName, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    do {
        Start-Sleep -Milliseconds 500
        $state = (Get-ScheduledTask -TaskName $taskName).State
    } until ($state -ne 'Running' -or (Get-Date) -gt $deadline)
    if ($state -eq 'Running') { throw "$taskName exceeded $timeoutSeconds seconds." }
    return (Get-ScheduledTaskInfo -TaskName $taskName).LastTaskResult
}

function Set-TextScale([int]$target) {
    Set-Content -LiteralPath (Join-Path $gateRoot 'text-scale-target.txt') -Value $target -Encoding ascii
    Start-ScheduledTask -TaskName $setTask
    $result = Wait-Task $setTask 45
    if ($result -ne 0) { throw "Setting Windows text scale to $target failed with result $result." }
    Record "TEXT_SCALE set=$target result=0"
}

$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $setTask -Action (
    New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-STA -NoProfile -ExecutionPolicy Bypass -File \\Mac\Home\TinyPix\3.5pro\build\winui-gate-set-text-scale-wrapper.ps1') -Principal $principal -Settings $settings -Force | Out-Null
Register-ScheduledTask -TaskName $launchTask -Action (
    New-ScheduledTaskAction -Execute $exe) -Principal $principal -Settings $settings -Force | Out-Null
Register-ScheduledTask -TaskName $uiaTask -Action (
    New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-STA -NoProfile -ExecutionPolicy Bypass -File \\Mac\Home\TinyPix\3.5pro\build\winui-gate-uia-wrapper.ps1') -Principal $principal -Settings $settings -Force | Out-Null

$failure = $null
try {
    Set-TextScale 200
    Get-Process TinyPix.WinUIGate -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-ScheduledTask -TaskName $launchTask
    Start-Sleep -Seconds 5
    Start-ScheduledTask -TaskName $uiaTask
    $uiaResult = Wait-Task $uiaTask 120
    if ($uiaResult -ne 0) { throw "200% text-scale UIA failed with result $uiaResult." }
    Record 'TEXT_SCALE_UIA result=0'
    Copy-Item -LiteralPath (Join-Path $gateRoot 'uia-report.txt') -Destination (Join-Path $artifactRoot 'uia-report-200percent-text-final.txt') -Force
    Copy-Item -LiteralPath (Join-Path $gateRoot '28-compact-900x600-100percent.png') -Destination (Join-Path $artifactRoot '32-compact-900x600-200percent-text.png') -Force
}
catch {
    $failure = $_
    Record "TEXT_SCALE_FAILURE message=$($_.Exception.Message)"
}
finally {
    Get-Process TinyPix.WinUIGate -ErrorAction SilentlyContinue | Stop-Process -Force
    try {
        Set-TextScale 100
        Record 'TEXT_SCALE restored=100'
    }
    catch {
        if ($null -eq $failure) { $failure = $_ }
        Record "TEXT_SCALE_RESTORE_FAILURE message=$($_.Exception.Message)"
    }
    Get-ScheduledTask -TaskName $setTask, $launchTask, $uiaTask -ErrorAction SilentlyContinue |
        Unregister-ScheduledTask -Confirm:$false
    Get-ScheduledTask -TaskName 'TinyPixWinUIGateTextScaleInspect' -ErrorAction SilentlyContinue |
        Unregister-ScheduledTask -Confirm:$false
    Copy-Item -LiteralPath $reportPath -Destination (Join-Path $artifactRoot 'text-scale-200-report-final.txt') -Force
}

if ($null -ne $failure) { throw $failure }
