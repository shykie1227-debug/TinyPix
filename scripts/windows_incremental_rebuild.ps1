param(
  [string]$SourceRoot = 'C:\Mac\Home\TinyPix\3.5pro',
  [string]$BuildRoot = "$env:LOCALAPPDATA\TinyPixBuild",
  [string]$TargetRoot = "$env:LOCALAPPDATA\TinyPixBuildCache\cargo-target",
  [string]$OutputDirectory = "$env:USERPROFILE\Desktop\tiny",
  [string]$HostOutputDirectory = 'C:\Mac\Home\Desktop\tiny'
)

$ErrorActionPreference = 'Stop'
$component = 'src\components\preview\OutputSettingsPanel.tsx'
Copy-Item (Join-Path $SourceRoot $component) (Join-Path $BuildRoot $component) -Force
Copy-Item (Join-Path $SourceRoot 'src-tauri\Cargo.toml') (Join-Path $BuildRoot 'src-tauri\Cargo.toml') -Force
Copy-Item (Join-Path $SourceRoot 'src-tauri\src\main.rs') (Join-Path $BuildRoot 'src-tauri\src\main.rs') -Force
Copy-Item (Join-Path $SourceRoot 'src-tauri\tauri.conf.json') (Join-Path $BuildRoot 'src-tauri\tauri.conf.json') -Force

$vsDevCmd = 'C:\BuildTools\Common7\Tools\VsDevCmd.bat'
$command = "call `"$vsDevCmd`" -arch=x64 && set `"CARGO_BUILD_JOBS=4`" && set `"CARGO_TARGET_DIR=$TargetRoot`" && cd /d `"$BuildRoot`" && npm run build && npx tauri build --no-bundle"
& cmd.exe /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw "Incremental Tauri build failed with exit code $LASTEXITCODE." }

$builtExe = Join-Path $TargetRoot 'release\tinypix.exe'
$portableName = 'TinyPix-Pro-3.5.1-Windows-x64-Portable.exe'
$portableTarget = Join-Path $TargetRoot "release\$portableName"
$desktopTarget = Join-Path $OutputDirectory $portableName
if (!(Test-Path $builtExe)) { throw "Built EXE not found: $builtExe" }
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
New-Item $HostOutputDirectory -ItemType Directory -Force | Out-Null
Copy-Item $builtExe $portableTarget -Force
Copy-Item $builtExe $desktopTarget -Force
Copy-Item $builtExe (Join-Path $HostOutputDirectory $portableName) -Force

[ordered]@{
  exePath = $desktopTarget
  exeBytes = (Get-Item $desktopTarget).Length
  exeSha256 = (Get-FileHash $desktopTarget -Algorithm SHA256).Hash.ToLowerInvariant()
} | ConvertTo-Json
