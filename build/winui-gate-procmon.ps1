$ErrorActionPreference = 'Stop'

$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$toolsRoot = Join-Path $gateRoot 'Tools\ProcessMonitor'
$zipPath = Join-Path $gateRoot 'Tools\ProcessMonitor.zip'
$procmon = Join-Path $toolsRoot 'Procmon64.exe'
$pmlPath = Join-Path $gateRoot 'tinypix-registry-capture.pml'
$csvPath = Join-Path $gateRoot 'tinypix-registry-capture.csv'
$candidatePath = Join-Path $gateRoot 'tinypix-registry-write-candidates.csv'
$reportPath = Join-Path $gateRoot 'procmon-registry-report.txt'
$artifactRoot = '\\Mac\Home\TinyPix\3.5pro\artifacts\windows\winui-feasibility-gate'

New-Item -ItemType Directory -Path (Split-Path $zipPath) -Force | Out-Null
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
Remove-Item -LiteralPath $pmlPath, $csvPath, $candidatePath, $reportPath -Force -ErrorAction SilentlyContinue

function Record([string]$message) {
    Add-Content -LiteralPath $reportPath -Value "$(Get-Date -Format o) $message" -Encoding utf8
}

function Wait-ScheduledTask([string]$taskName, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    do {
        Start-Sleep -Milliseconds 500
        $state = (Get-ScheduledTask -TaskName $taskName).State
    } until ($state -ne 'Running' -or (Get-Date) -gt $deadline)
    if ($state -eq 'Running') { throw "$taskName exceeded $timeoutSeconds seconds." }
    $result = (Get-ScheduledTaskInfo -TaskName $taskName).LastTaskResult
    Record "TASK name=$taskName result=$result"
    if ($result -ne 0) { throw "$taskName failed with result $result." }
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
    Record "GATE startedFor=$scenario"
}

if (-not (Test-Path -LiteralPath $procmon)) {
    Invoke-WebRequest -Uri 'https://download.sysinternals.com/files/ProcessMonitor.zip' -OutFile $zipPath -UseBasicParsing
    Remove-Item -LiteralPath $toolsRoot -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive -LiteralPath $zipPath -DestinationPath $toolsRoot -Force
}

$signature = Get-AuthenticodeSignature -LiteralPath $procmon
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
    throw "Process Monitor signature is not valid Microsoft Corporation code: $($signature.Status) $($signature.SignerCertificate.Subject)"
}
Record "PROCMON signature=Valid signer=$($signature.SignerCertificate.Subject)"

Get-Process Narrator -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process TinyPix.WinUIGate -ErrorAction SilentlyContinue | Stop-Process -Force
$captureProcess = Start-Process -FilePath $procmon -ArgumentList "/AcceptEula /Quiet /Minimized /BackingFile `"$pmlPath`"" -PassThru
Start-Sleep -Seconds 2
if ($captureProcess.HasExited) { throw 'Process Monitor capture process exited before the scenarios started.' }
Record "PROCMON captureStarted=true pid=$($captureProcess.Id)"

try {
    Start-Gate 'settings-and-keyboard'
    Start-ScheduledTask -TaskName TinyPixWinUIGateUIA
    Wait-ScheduledTask 'TinyPixWinUIGateUIA' 90

    Start-Gate 'media-and-ffmpeg'
    Start-ScheduledTask -TaskName TinyPixWinUIGateMediaUIA
    Wait-ScheduledTask 'TinyPixWinUIGateMediaUIA' 150
}
finally {
    Get-Process TinyPix.WinUIGate -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Process -FilePath $procmon -ArgumentList '/Terminate /Quiet' -Wait
}
Record 'PROCMON captureStopped=true'

Start-Process -FilePath $procmon -ArgumentList "/OpenLog `"$pmlPath`" /SaveAs `"$csvPath`"" -Wait
if (-not (Test-Path -LiteralPath $pmlPath) -or -not (Test-Path -LiteralPath $csvPath)) {
    throw 'Process Monitor did not create both PML and CSV evidence.'
}

$header = Get-Content -LiteralPath $csvPath -TotalCount 1
$matchingLines = @(Select-String -LiteralPath $csvPath -SimpleMatch 'TinyPix.WinUIGate.exe' | ForEach-Object { $_.Line })
$tinyPixEvents = @((@($header) + $matchingLines) | ConvertFrom-Csv | Where-Object { $_.'Process Name' -eq 'TinyPix.WinUIGate.exe' })
$registryEvents = @($tinyPixEvents | Where-Object { $_.Operation -like 'Reg*' })
$writeOperations = @(
    'RegCreateKey',
    'RegSetValue',
    'RegDeleteKey',
    'RegDeleteValue',
    'RegRenameKey',
    'RegLoadKey',
    'RegUnloadKey',
    'RegRestoreKey',
    'RegReplaceKey',
    'RegSetKeySecurity'
)
$registryWrites = @($registryEvents | Where-Object { $writeOperations -contains $_.Operation })
$registryWrites | Select-Object 'Time of Day', 'Process Name', PID, Operation, Path, Result, Detail |
    Export-Csv -LiteralPath $candidatePath -NoTypeInformation -Encoding utf8
$openedExistingKeys = @($registryWrites | Where-Object {
    $_.Operation -eq 'RegCreateKey' -and $_.Detail -match 'REG_OPENED_EXISTING_KEY'
})
$createdNewKeys = @($registryWrites | Where-Object {
    $_.Operation -eq 'RegCreateKey' -and $_.Result -eq 'SUCCESS' -and $_.Detail -match 'REG_CREATED_NEW_KEY'
})
$failedMutations = @($registryWrites | Where-Object { $_.Result -ne 'SUCCESS' })
$successfulNonCreateMutations = @($registryWrites | Where-Object {
    $_.Operation -ne 'RegCreateKey' -and $_.Result -eq 'SUCCESS'
})
$systemManagedBamWrites = @($successfulNonCreateMutations | Where-Object {
    $_.Operation -eq 'RegSetValue' -and $_.Path -like 'HKLM\System\CurrentControlSet\Services\bam\State\*'
})
$appOwnedWrites = @(
    $createdNewKeys
    $successfulNonCreateMutations | Where-Object {
        -not ($_.Operation -eq 'RegSetValue' -and $_.Path -like 'HKLM\System\CurrentControlSet\Services\bam\State\*')
    }
)
Record "PROCMON tinyPixEvents=$($tinyPixEvents.Count) registryEvents=$($registryEvents.Count) rawWriteCandidates=$($registryWrites.Count) openedExistingKeys=$($openedExistingKeys.Count) failedMutations=$($failedMutations.Count) systemManagedBamWrites=$($systemManagedBamWrites.Count) appOwnedWrites=$($appOwnedWrites.Count)"
foreach ($entry in $registryWrites | Select-Object -First 100) {
    Record "REGISTRY_CANDIDATE operation=$($entry.Operation) path=$($entry.Path) result=$($entry.Result) detail=$($entry.Detail)"
}
if ($appOwnedWrites.Count -ne 0) {
    Record "REGISTRY_WRITE_RESULT passed=False rawCandidates=$($registryWrites.Count) appOwnedWrites=$($appOwnedWrites.Count)"
    throw "TinyPix performed $($appOwnedWrites.Count) application-owned registry writes."
}
Record "REGISTRY_WRITE_RESULT passed=True rawCandidates=$($registryWrites.Count) appOwnedWrites=0 systemManagedBamWrites=$($systemManagedBamWrites.Count)"

Copy-Item -LiteralPath $pmlPath -Destination (Join-Path $artifactRoot 'tinypix-registry-capture.pml') -Force
Copy-Item -LiteralPath $csvPath -Destination (Join-Path $artifactRoot 'tinypix-registry-capture.csv') -Force
Copy-Item -LiteralPath $candidatePath -Destination (Join-Path $artifactRoot 'tinypix-registry-write-candidates.csv') -Force
Copy-Item -LiteralPath $reportPath -Destination (Join-Path $artifactRoot 'procmon-registry-report-final.txt') -Force
