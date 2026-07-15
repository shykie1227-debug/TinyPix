param(
  [string]$ExePath = "$env:USERPROFILE\Desktop\tiny\TinyPix-Pro-3.5.1-Windows-x64-Portable.exe",
  [string]$ReportPath = "C:\Mac\Home\TinyPix\3.5pro\artifacts\windows\launch-validation.json",
  [string]$ScreenshotDirectory = "C:\Mac\Home\TinyPix\3.5pro\artifacts\windows"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TinyPixNative {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
'@

function Save-Screen([string]$Path) {
  $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
  $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
  $graphics.Dispose()
  $bitmap.Dispose()
}

Add-Type -AssemblyName System.Windows.Forms
function Start-TinyPix([string]$Path) {
  $watch = [Diagnostics.Stopwatch]::StartNew()
  $launcher = Start-Process $Path -PassThru
  $process = $null
  for ($i = 0; $i -lt 120 -and $null -eq $process; $i++) {
    Start-Sleep -Milliseconds 250
    $process = Get-Process -ErrorAction SilentlyContinue | Where-Object {
      try { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -eq 'TinyPix Pro' } catch { $false }
    } | Select-Object -First 1
  }
  $watch.Stop()
  if ($null -eq $process) { throw 'TinyPix window did not become ready.' }
  [void][TinyPixNative]::MoveWindow($process.MainWindowHandle, 0, 0, 1200, 700, $true)
  Start-Sleep -Milliseconds 300
  [ordered]@{ process=$process; launcher=$launcher; milliseconds=$watch.ElapsedMilliseconds }
}

function Open-Settings($Process) {
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
  $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty,
    '设置'
  )
  $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
  if ($null -ne $button) {
    $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    return
  }
  $rect = New-Object TinyPixNative+RECT
  [void][TinyPixNative]::GetWindowRect($Process.MainWindowHandle, [ref]$rect)
  [void][TinyPixNative]::SetForegroundWindow($Process.MainWindowHandle)
  Start-Sleep -Milliseconds 300
  [void][TinyPixNative]::SetCursorPos($rect.Left + 100, $rect.Bottom - 45)
  [TinyPixNative]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
  [TinyPixNative]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

function Stop-TinyPix($Process, $Launcher) {
  if (!$Process.HasExited) {
    $Process.CloseMainWindow() | Out-Null
    if (!$Process.WaitForExit(5000)) { $Process.Kill(); $Process.WaitForExit() }
  }
  if ($null -ne $Launcher -and !$Launcher.HasExited) { $Launcher.Kill() }
}

function Get-ProcessTreeIds([int]$RootId) {
  $ids = New-Object System.Collections.Generic.List[int]
  $ids.Add($RootId)
  for ($index = 0; $index -lt $ids.Count; $index++) {
    $parent = $ids[$index]
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$parent" -ErrorAction SilentlyContinue | ForEach-Object {
      if (!$ids.Contains([int]$_.ProcessId)) { $ids.Add([int]$_.ProcessId) }
    }
  }
  $ids
}

function Get-TinyPixRegistryEntries {
  $paths = @(
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
  )
  @(Get-ItemProperty $paths -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like '*TinyPix*' } | Select-Object -ExpandProperty DisplayName)
}

function Get-TinyPixServices {
  @(Get-Service -ErrorAction SilentlyContinue | Where-Object {
    $_.Name -like '*TinyPix*' -or $_.DisplayName -like '*TinyPix*'
  } | ForEach-Object { "$($_.Name)|$($_.DisplayName)" })
}

function Get-TinyPixScheduledTasks {
  @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object {
    $_.TaskName -like '*TinyPix*'
  } | ForEach-Object { "$($_.TaskPath)|$($_.TaskName)" })
}

function Test-SameValues($Before, $After) {
  @((Compare-Object @($Before) @($After))).Count -eq 0
}

if (!(Test-Path $ExePath)) { throw "EXE not found: $ExePath" }
New-Item $ScreenshotDirectory -ItemType Directory -Force | Out-Null
Get-Process '*TinyPix*' -ErrorAction SilentlyContinue | Stop-Process -Force
$cacheRoot = Join-Path $env:LOCALAPPDATA 'TinyPix\engine'
Remove-Item $cacheRoot -Recurse -Force -ErrorAction SilentlyContinue
$registryBefore = @(Get-TinyPixRegistryEntries)
$servicesBefore = @(Get-TinyPixServices)
$tasksBefore = @(Get-TinyPixScheduledTasks)

$first = Start-TinyPix $ExePath
Start-Sleep -Seconds 2
Save-Screen (Join-Path $ScreenshotDirectory 'windows-cold-start.png')
Open-Settings $first.process
$extractionWatch = [Diagnostics.Stopwatch]::StartNew()
$cacheReady = $false
for ($i = 0; $i -lt 360; $i++) {
  $engineFiles = @(Get-ChildItem $cacheRoot -Recurse -File -ErrorAction SilentlyContinue)
  if (($engineFiles.Name -contains 'ffmpeg.exe') -and ($engineFiles.Name -contains 'ffprobe.exe')) { $cacheReady = $true; break }
  Start-Sleep -Milliseconds 250
}
$extractionWatch.Stop()
# The files become visible just before the command performs its final hash and
# version checks. Let that background command finish before capturing UI or
# closing the process, otherwise the next launch is not a true warm start.
Start-Sleep -Seconds 8
$first.process.Refresh()
$firstResponsive = $first.process.Responding
Save-Screen (Join-Path $ScreenshotDirectory 'windows-settings-engine.png')
$treeIds = @((Get-ProcessTreeIds $first.launcher.Id) + (Get-ProcessTreeIds $first.process.Id) | Select-Object -Unique)
$connections = @(Get-NetTCPConnection -ErrorAction SilentlyContinue | Where-Object { $treeIds -contains $_.OwningProcess } | Select-Object State,LocalAddress,LocalPort,RemoteAddress,RemotePort,OwningProcess)
$engineFiles = @(Get-ChildItem $cacheRoot -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
  [ordered]@{ path=$_.FullName; bytes=$_.Length; sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
Stop-TinyPix $first.process $first.launcher
Start-Sleep -Seconds 3

$second = Start-TinyPix $ExePath
Start-Sleep -Seconds 1
Stop-TinyPix $second.process $second.launcher
$registryAfter = @(Get-TinyPixRegistryEntries)
$servicesAfter = @(Get-TinyPixServices)
$tasksAfter = @(Get-TinyPixScheduledTasks)
$registryUnchanged = Test-SameValues $registryBefore $registryAfter
$servicesUnchanged = Test-SameValues $servicesBefore $servicesAfter
$tasksUnchanged = Test-SameValues $tasksBefore $tasksAfter

$report = [ordered]@{
  generatedAt=(Get-Date).ToString('o')
  ordinaryUser=[Security.Principal.WindowsIdentity]::GetCurrent().Name
  offline=$true
  exePath=$ExePath
  exeBytes=(Get-Item $ExePath).Length
  exeSha256=(Get-FileHash $ExePath -Algorithm SHA256).Hash.ToLowerInvariant()
  coldStartMilliseconds=$first.milliseconds
  engineExtractionMilliseconds=$extractionWatch.ElapsedMilliseconds
  secondStartMilliseconds=$second.milliseconds
  firstWindowResponsive=$firstResponsive
  cacheReady=$cacheReady
  engineFiles=$engineFiles
  tcpConnections=$connections
  registryBefore=$registryBefore
  registryAfter=$registryAfter
  registryUnchanged=$registryUnchanged
  servicesBefore=$servicesBefore
  servicesAfter=$servicesAfter
  servicesUnchanged=$servicesUnchanged
  scheduledTasksBefore=$tasksBefore
  scheduledTasksAfter=$tasksAfter
  scheduledTasksUnchanged=$tasksUnchanged
  passed=($cacheReady -and $firstResponsive -and $first.milliseconds -le 15000 -and $second.milliseconds -le 5000 -and $connections.Count -eq 0 -and $registryUnchanged -and $servicesUnchanged -and $tasksUnchanged)
}
$report | ConvertTo-Json -Depth 8 | Set-Content $ReportPath -Encoding utf8
if (!$report.passed) { throw 'Windows launch validation failed. Inspect the JSON report.' }
Write-Output $ReportPath
