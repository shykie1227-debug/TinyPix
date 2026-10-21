# capture-published.ps1 - run the full UI capture against the PUBLISHED single-file exe.
# Proves the shipped artifact (not just the dotnet build output) renders the new UI.
$ErrorActionPreference = 'Continue'
$root = 'C:\Mac\Home\TinyPix\3.5pro\artifacts'
$shop = Join-Path $root 'ui-shots3'
New-Item -ItemType Directory -Path $shop -Force | Out-Null
& powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File (Join-Path $root 'ui-shots2\capture-ui.ps1') `
    -Exe 'C:\TinyPixBuild2\artifacts\TinyPix-single-file\TinyPix.exe' `
    -OutDir $shop
exit $LASTEXITCODE
