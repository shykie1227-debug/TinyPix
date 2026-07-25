$ErrorActionPreference = 'Stop'

$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$csvPath = Join-Path $gateRoot 'tinypix-registry-capture.csv'
$detailPath = Join-Path $gateRoot 'tinypix-registry-write-candidates.csv'
$summaryPath = Join-Path $gateRoot 'procmon-registry-classification.txt'

Remove-Item -LiteralPath $detailPath, $summaryPath -Force -ErrorAction SilentlyContinue
$header = Get-Content -LiteralPath $csvPath -TotalCount 1
$matchingLines = @(Select-String -LiteralPath $csvPath -SimpleMatch 'TinyPix.WinUIGate.exe' | ForEach-Object { $_.Line })
$events = @((@($header) + $matchingLines) | ConvertFrom-Csv | Where-Object { $_.'Process Name' -eq 'TinyPix.WinUIGate.exe' })
$writeOperations = @(
    'RegCreateKey', 'RegSetValue', 'RegDeleteKey', 'RegDeleteValue', 'RegRenameKey',
    'RegLoadKey', 'RegUnloadKey', 'RegRestoreKey', 'RegReplaceKey', 'RegSetKeySecurity'
)
$writes = @($events | Where-Object { $writeOperations -contains $_.Operation })
$writes | Select-Object 'Time of Day', 'Process Name', PID, Operation, Path, Result, Detail |
    Export-Csv -LiteralPath $detailPath -NoTypeInformation -Encoding utf8

$successfulMutations = @($writes | Where-Object {
    $_.Result -eq 'SUCCESS' -and $_.Operation -ne 'RegCreateKey'
})
$failedMutations = @($writes | Where-Object { $_.Result -ne 'SUCCESS' })
$createKeyCalls = @($writes | Where-Object { $_.Operation -eq 'RegCreateKey' })

"rawWriteCandidates=$($writes.Count)" | Set-Content -LiteralPath $summaryPath -Encoding utf8
"successfulNonCreateMutations=$($successfulMutations.Count)" | Add-Content -LiteralPath $summaryPath -Encoding utf8
"failedMutationAttempts=$($failedMutations.Count)" | Add-Content -LiteralPath $summaryPath -Encoding utf8
"createKeyCalls=$($createKeyCalls.Count)" | Add-Content -LiteralPath $summaryPath -Encoding utf8
"--- successful non-create mutations ---" | Add-Content -LiteralPath $summaryPath -Encoding utf8
$successfulMutations | Format-Table Operation, Path, Result, Detail -AutoSize | Out-String -Width 4096 |
    Add-Content -LiteralPath $summaryPath -Encoding utf8
"--- create-key calls grouped by path ---" | Add-Content -LiteralPath $summaryPath -Encoding utf8
$createKeyCalls | Group-Object Path | Sort-Object Count -Descending | Select-Object Count, Name |
    Format-Table -AutoSize | Out-String -Width 4096 | Add-Content -LiteralPath $summaryPath -Encoding utf8

Copy-Item -LiteralPath $detailPath -Destination '\\Mac\Home\TinyPix\3.5pro\artifacts\windows\winui-feasibility-gate\tinypix-registry-write-candidates.csv' -Force
Copy-Item -LiteralPath $summaryPath -Destination '\\Mac\Home\TinyPix\3.5pro\artifacts\windows\winui-feasibility-gate\procmon-registry-classification.txt' -Force
