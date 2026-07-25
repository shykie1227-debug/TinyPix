$ErrorActionPreference = 'Stop'

$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$reportPath = Join-Path $gateRoot 'offline-gate-report.txt'
$artifactPath = '\\Mac\Home\TinyPix\3.5pro\artifacts\windows\winui-feasibility-gate\offline-gate-report-final.txt'
$offlineUiaTask = 'TinyPixWinUIGateOfflineUIA'
$offlineMediaTask = 'TinyPixWinUIGateOfflineMediaUIA'
$localUiaScript = Join-Path $gateRoot 'offline-uia.ps1'
$localMediaScript = Join-Path $gateRoot 'offline-media-uia.ps1'
Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue

function Record([string]$message) {
    Add-Content -LiteralPath $reportPath -Value "$(Get-Date -Format o) $message" -Encoding utf8
}

function Wait-LaunchTaskReady([int]$timeoutSeconds = 20) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    do {
        Start-Sleep -Milliseconds 250
        $state = (Get-ScheduledTask -TaskName TinyPixWinUIGateLaunch).State
    } until ($state -ne 'Running' -or (Get-Date) -gt $deadline)
    if ($state -eq 'Running') { throw 'TinyPix launch task did not return to Ready.' }
}

function Start-Gate([string]$scenario) {
    Get-Process TinyPix.WinUIGate -ErrorAction SilentlyContinue | Stop-Process -Force
    Wait-LaunchTaskReady
    Start-ScheduledTask -TaskName TinyPixWinUIGateLaunch
    Start-Sleep -Seconds 4
    if ($null -eq (Get-Process TinyPix.WinUIGate -ErrorAction SilentlyContinue | Select-Object -First 1)) {
        throw "TinyPix gate did not start before $scenario."
    }
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

$utf8Bom = New-Object System.Text.UTF8Encoding($true)
[IO.File]::WriteAllText(
    $localUiaScript,
    [IO.File]::ReadAllText('\\Mac\Home\TinyPix\3.5pro\build\winui-gate-uia.ps1', [Text.Encoding]::UTF8),
    $utf8Bom)
[IO.File]::WriteAllText(
    $localMediaScript,
    [IO.File]::ReadAllText('\\Mac\Home\TinyPix\3.5pro\build\winui-gate-media-uia.ps1', [Text.Encoding]::UTF8),
    $utf8Bom)

$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $offlineUiaTask -Action (
    New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-STA -NoProfile -ExecutionPolicy Bypass -File `"$localUiaScript`"") -Principal $principal -Settings $settings -Force | Out-Null
Register-ScheduledTask -TaskName $offlineMediaTask -Action (
    New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-STA -NoProfile -ExecutionPolicy Bypass -File `"$localMediaScript`"") -Principal $principal -Settings $settings -Force | Out-Null

$activeAdapters = @(Get-NetAdapter -Physical | Where-Object { $_.Status -eq 'Up' })
if ($activeAdapters.Count -eq 0) { throw 'No active physical network adapter was available for the offline test.' }
$failure = $null
try {
    $activeAdapters | Disable-NetAdapter -Confirm:$false
    Start-Sleep -Seconds 3
    $stillOnline = @(Get-NetAdapter -Name $activeAdapters.Name | Where-Object { $_.Status -eq 'Up' })
    if ($stillOnline.Count -ne 0) { throw 'An active adapter remained online after Disable-NetAdapter.' }
    Record "NETWORK offline=True adapters=$($activeAdapters.Name -join ',')"

    Start-Gate 'offline-settings'
    Start-ScheduledTask -TaskName $offlineUiaTask
    $uiaResult = Wait-Task $offlineUiaTask 90
    Record "OFFLINE_UIA result=$uiaResult"
    if ($uiaResult -ne 0) { throw "Offline UIA failed with result $uiaResult." }

    Start-Gate 'offline-media'
    Start-ScheduledTask -TaskName $offlineMediaTask
    $mediaResult = Wait-Task $offlineMediaTask 150
    Record "OFFLINE_MEDIA result=$mediaResult"
    if ($mediaResult -ne 0) { throw "Offline media UIA failed with result $mediaResult." }
}
catch {
    $failure = $_
    Record "OFFLINE_FAILURE message=$($_.Exception.Message)"
}
finally {
    Get-Process TinyPix.WinUIGate -ErrorAction SilentlyContinue | Stop-Process -Force
    $activeAdapters | Enable-NetAdapter -Confirm:$false
    Start-Sleep -Seconds 5
    $restoredAdapters = @(Get-NetAdapter -Name $activeAdapters.Name | Where-Object { $_.Status -eq 'Up' })
    $networkRestored = $restoredAdapters.Count -eq $activeAdapters.Count
    Record "NETWORK networkRestored=$networkRestored adapters=$($restoredAdapters.Name -join ',')"
    Unregister-ScheduledTask -TaskName $offlineUiaTask -Confirm:$false -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $offlineMediaTask -Confirm:$false -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath $reportPath -Destination $artifactPath -Force
}

if ($null -ne $failure) { throw $failure }
if (-not $networkRestored) { throw 'The offline test completed but the network adapter was not restored.' }
