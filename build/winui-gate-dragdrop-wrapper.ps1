try {
    $errorPath = Join-Path (Join-Path $env:TEMP 'TinyPix-WinUI-Gate') 'dragdrop-error.txt'
    Remove-Item -LiteralPath $errorPath -Force -ErrorAction SilentlyContinue
    $script = [IO.File]::ReadAllText('\\Mac\Home\TinyPix\3.5pro\build\winui-gate-dragdrop.ps1', [Text.Encoding]::UTF8)
    Invoke-Expression $script
} catch {
    $_ | Out-String | Set-Content -LiteralPath $errorPath -Encoding utf8
    exit 1
}
