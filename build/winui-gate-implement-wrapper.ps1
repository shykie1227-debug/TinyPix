$ErrorActionPreference = 'Stop'
$errorPath = Join-Path (Join-Path $env:TEMP 'TinyPix-WinUI-Gate') 'implement-error.txt'
Remove-Item -LiteralPath $errorPath -Force -ErrorAction SilentlyContinue
$machineDotnet = 'C:\Program Files\dotnet'
if (Test-Path -LiteralPath (Join-Path $machineDotnet 'dotnet.exe')) {
    $env:Path = "$machineDotnet;$env:Path"
}
Get-Process TinyPix.WinUIGate -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
try {
    $script = [IO.File]::ReadAllText('\\Mac\Home\TinyPix\3.5pro\build\winui-gate-implement.ps1', [Text.Encoding]::UTF8)
    Invoke-Expression $script
} catch {
    $_ | Out-String | Set-Content -LiteralPath $errorPath -Encoding utf8
    exit 1
}
