<#
 .SYNOPSIS
   Publish TinyPix 4.0 (WinUI 3 desktop app) as a self-contained win-x64 folder
   that contains the runnable TinyPix.exe.

 .DESCRIPTION
   TinyPix.App targets net10.0-windows + UseWinUI + Microsoft.WindowsAppSDK, so it
   can ONLY be built on Windows. This script is meant to run on a Windows machine
   (physical, Parallels/UTM VM, or any Windows host). It performs the first real
   compile of the C# code (Core/Media/Infrastructure/App), including the Phase 1
   (video output) and Phase 2 (GIF maker) handlers.

 .PREREQUISITES (run once on Windows)
   1. .NET 10 SDK 10.0.302+  -> https://dotnet.microsoft.com/download
   2. Windows App SDK build tooling. Easiest: install Visual Studio 2022 with the
      ".NET desktop development" + "Windows App SDK" workloads. Alternatively try:
        dotnet workload install microsoft-windows-appsdk
      (The csproj already sets WindowsAppSDKSelfContained=true, so the runtime is
       bundled into the output; only the build-time tooling is required.)

 .USAGE
   pwsh ./build/Publish-WinExe.ps1                 # Release, win-x64, artifacts/win-x64
   pwsh ./build/Publish-WinExe.ps1 -Configuration Debug
#>
param(
    [string] $Configuration = "Release",
    [string] $Runtime       = "win-x64",
    [string] $Output        = "artifacts/win-x64"
)

$ErrorActionPreference = "Stop"
$root    = Split-Path -Parent $MyInvocation.MyCommand.Definition | Split-Path -Parent
$appProj = Join-Path $root "src/TinyPix.App/TinyPix.App.csproj"

Write-Host "==> Root        : $root"
Write-Host "==> App project : $appProj"
Write-Host "==> Config      : $Configuration"
Write-Host "==> Runtime     : $Runtime"

Write-Host "==> Restoring"
& dotnet restore $appProj
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

Write-Host "==> Publishing (self-contained $Runtime)"
& dotnet publish $appProj -c $Configuration -r $Runtime --self-contained true -o $Output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exe = Join-Path $Output "TinyPix.exe"
if (Test-Path $exe) {
    Write-Host "==> BUILD OK -> $exe"
    Write-Host "==> Run it by launching: $exe"
} else {
    throw "TinyPix.exe was not produced by publish."
}
