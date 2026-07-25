$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DragNativeMethods {
    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
'@

$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$outputRoot = Join-Path $gateRoot 'bin\Debug\net10.0-windows10.0.19041.0\win-x64'
$reportPath = Join-Path $gateRoot 'dragdrop-report.txt'
$errorPath = Join-Path $gateRoot 'dragdrop-error.txt'
$filePath = Join-Path $gateRoot 'Samples\sample-image.png'
$gateLog = Join-Path $outputRoot 'Logs\gate.log'
Remove-Item -LiteralPath $reportPath, $errorPath -Force -ErrorAction SilentlyContinue

$appProcess = Get-Process TinyPix.WinUIGate -ErrorAction Stop | Select-Object -First 1
[void][DragNativeMethods]::MoveWindow($appProcess.MainWindowHandle, 470, 80, 900, 600, $true)
$root = [System.Windows.Automation.AutomationElement]::RootElement
$topLevelWindows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($candidate in $topLevelWindows) {
    if ($candidate.Current.ClassName -eq 'CabinetWClass') {
        try { $candidate.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
    }
}
Start-Sleep -Milliseconds 700
$explorer = Start-Process explorer.exe -ArgumentList "/select,`"$filePath`"" -PassThru
Start-Sleep -Seconds 3
$fileCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'sample-image')
$fileElement = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $fileCondition)
if ($null -eq $fileElement) { throw 'Explorer did not expose the sample-image item.' }

$explorerWindow = $fileElement
while ($null -ne $explorerWindow -and $explorerWindow.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $explorerWindow = $walker.GetParent($explorerWindow)
}
if ($null -eq $explorerWindow -or $explorerWindow.Current.NativeWindowHandle -eq 0) { throw 'Explorer window handle was not found.' }
$explorerHandle = [IntPtr]$explorerWindow.Current.NativeWindowHandle
[void][DragNativeMethods]::MoveWindow($explorerHandle, 20, 120, 430, 560, $true)
Start-Sleep -Seconds 1

$appWindow = [System.Windows.Automation.AutomationElement]::FromHandle($appProcess.MainWindowHandle)
$previewCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '中央预览区域')
$preview = $appWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $previewCondition)
if ($null -eq $preview) { throw 'TinyPix preview target was not found.' }
$fileRect = $fileElement.Current.BoundingRectangle
$targetRect = $preview.Current.BoundingRectangle
$sourceX = [int]($fileRect.Left + $fileRect.Width / 2)
$sourceY = [int]($fileRect.Top + $fileRect.Height / 2)
$targetX = [int]($targetRect.Left + $targetRect.Width / 2)
$targetY = [int]($targetRect.Top - 120)
$beforeCount = if (Test-Path $gateLog) { (Select-String -LiteralPath $gateLog -Pattern 'IMPORT source=drop').Count } else { 0 }

[void][DragNativeMethods]::SetForegroundWindow($explorerHandle)
[void][DragNativeMethods]::SetCursorPos($sourceX, $sourceY)
Start-Sleep -Milliseconds 200
[DragNativeMethods]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
for ($step = 1; $step -le 30; $step++) {
    $x = $sourceX + [int](($targetX - $sourceX) * $step / 30)
    $y = $sourceY + [int](($targetY - $sourceY) * $step / 30)
    [void][DragNativeMethods]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 35
}
Start-Sleep -Milliseconds 300
[DragNativeMethods]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Seconds 3

$afterCount = if (Test-Path $gateLog) { (Select-String -LiteralPath $gateLog -Pattern 'IMPORT source=drop').Count } else { 0 }
$imageCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '图片预览')
$imagePreview = $appWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $imageCondition)
$passed = $afterCount -eq ($beforeCount + 1) -and $null -ne $imagePreview -and -not $imagePreview.Current.IsOffscreen
"$(Get-Date -Format o) DROP imported=$($afterCount -eq ($beforeCount + 1)) previewVisible=$($null -ne $imagePreview -and -not $imagePreview.Current.IsOffscreen) source=$sourceX,$sourceY target=$targetX,$targetY passed=$passed" | Set-Content -LiteralPath $reportPath -Encoding utf8
if (-not $passed) { throw 'Explorer drag/drop did not reach the shared import path.' }
