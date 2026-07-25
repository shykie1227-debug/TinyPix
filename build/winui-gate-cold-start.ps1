$ErrorActionPreference = 'Stop'

$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$reportPath = Join-Path $gateRoot 'cold-start-report.txt'
$samples = [System.Collections.Generic.List[double]]::new()

Get-Process TinyPix.WinUIGate -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

for ($iteration = 1; $iteration -le 5; $iteration++) {
    $started = Get-Date
    Start-ScheduledTask -TaskName TinyPixWinUIGateLaunch
    $deadline = $started.AddSeconds(10)
    $process = $null

    do {
        Start-Sleep -Milliseconds 50
        $process = Get-Process TinyPix.WinUIGate -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } |
            Select-Object -First 1
    } until ($null -ne $process -or (Get-Date) -gt $deadline)

    if ($null -eq $process) {
        throw "Iteration $iteration did not expose a top-level window within 10 seconds."
    }

    $elapsed = ((Get-Date) - $started).TotalMilliseconds
    $samples.Add($elapsed)
    "$(Get-Date -Format o) COLD_START iteration=$iteration milliseconds=$([Math]::Round($elapsed)) pid=$($process.Id) handle=$($process.MainWindowHandle)" |
        Add-Content -LiteralPath $reportPath -Encoding utf8

    Stop-Process -Id $process.Id -Force
    $process.WaitForExit(5000)
    Start-Sleep -Milliseconds 300
}

$ordered = $samples | Sort-Object
$median = $ordered[[Math]::Floor($ordered.Count / 2)]
$maximum = ($samples | Measure-Object -Maximum).Maximum
"$(Get-Date -Format o) COLD_START_RESULT count=$($samples.Count) medianMilliseconds=$([Math]::Round($median)) maximumMilliseconds=$([Math]::Round($maximum)) targetMilliseconds=5000 passed=$($maximum -le 5000)" |
    Add-Content -LiteralPath $reportPath -Encoding utf8

if ($maximum -gt 5000) {
    throw "Cold-start maximum $maximum ms exceeded the 5000 ms target."
}
