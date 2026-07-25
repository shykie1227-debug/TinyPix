$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ThemeNativeMethods {
    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
}
'@

$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$reportPath = Join-Path $gateRoot 'theme-report.txt'
Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue
function Record([string]$message) { Add-Content -LiteralPath $reportPath -Value "$(Get-Date -Format o) $message" -Encoding utf8 }
function Find-ByName($root, [string]$name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Find-ComboByName($root, [string]$name) {
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $typeCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)
    $condition = New-Object System.Windows.Automation.AndCondition($nameCondition, $typeCondition)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Save-Screenshot([string]$name) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $bitmap.Save((Join-Path $gateRoot $name), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
function Select-Theme($window, [string]$name) {
    $combo = Find-ComboByName $window '主题验证'
    $combo.SetFocus()
    if ($name -eq '深色') {
        [System.Windows.Forms.SendKeys]::SendWait('{END}')
    } elseif ($name -eq '浅色') {
        [System.Windows.Forms.SendKeys]::SendWait('{HOME}')
        [System.Windows.Forms.SendKeys]::SendWait('{DOWN}')
    } else {
        [System.Windows.Forms.SendKeys]::SendWait('{HOME}')
    }
    Start-Sleep -Milliseconds 700
    $expectedIndex = if ($name -eq '深色') { 2 } elseif ($name -eq '浅色') { 1 } else { 0 }
    $log = Get-Content -LiteralPath (Join-Path $gateRoot 'bin\Debug\net10.0-windows10.0.19041.0\win-x64\Logs\gate.log') -Raw
    $selected = $log -match "THEME selectedIndex=$expectedIndex"
    Record "THEME requested=$name selectedIndex=$expectedIndex confirmed=$selected"
    if (-not $selected) { throw "Theme $name was not applied." }
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$topLevel = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($candidate in $topLevel) {
    if ($candidate.Current.ClassName -eq 'CabinetWClass') {
        try { $candidate.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
    }
}
$process = Get-Process TinyPix.WinUIGate -ErrorAction Stop | Select-Object -First 1
[void][ThemeNativeMethods]::MoveWindow($process.MainWindowHandle, 80, 50, 1200, 800, $true)
Start-Sleep -Seconds 1
$window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
Select-Theme $window '深色'
Save-Screenshot '24-dark-1200x800-125percent.png'
Select-Theme $window '浅色'
Save-Screenshot '25-light-1200x800-125percent.png'
Select-Theme $window '跟随系统'
Record 'THEME_UIA complete=true'
