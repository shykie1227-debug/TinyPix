$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class NarratorWindowNativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
'@

$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$reportPath = Join-Path $gateRoot 'narrator-uia-report.txt'
Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue

function Record([string]$message) {
    Add-Content -LiteralPath $reportPath -Value "$(Get-Date -Format o) $message" -Encoding utf8
}

function Find-ByName($root, [string]$name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Record-Focused([string]$marker, [string]$expectedName) {
    Start-Sleep -Milliseconds 800
    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    if ($null -eq $focused) { throw "$marker did not expose a focused element." }
    $actualName = $focused.Current.Name
    $actualType = $focused.Current.ControlType.ProgrammaticName
    Record "$marker name=$actualName type=$actualType expected=$expectedName"
    if ($actualName -ne $expectedName) {
        throw "$marker expected '$expectedName' but focused '$actualName'."
    }
}

function Save-Screenshot([string]$name) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $bitmap.Save((Join-Path $gateRoot $name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$narratorPath = Join-Path $env:SystemRoot 'System32\Narrator.exe'
if (-not (Test-Path -LiteralPath $narratorPath)) { throw 'Windows Narrator.exe is unavailable.' }
$narrator = Get-Process Narrator -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $narrator) {
    Start-Process -FilePath $narratorPath
    Start-Sleep -Seconds 5
    $narrator = Get-Process Narrator -ErrorAction Stop | Select-Object -First 1
}
$narrator.Refresh()
if ($narrator.MainWindowHandle -ne [IntPtr]::Zero) {
    [void][NarratorWindowNativeMethods]::ShowWindow($narrator.MainWindowHandle, 6)
}
Record "NARRATOR processActive=True pid=$($narrator.Id)"

$process = Get-Process TinyPix.WinUIGate -ErrorAction Stop |
    Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
    Select-Object -First 1
if ($null -eq $process) { throw 'TinyPix gate has no available main window.' }
$window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if ($null -eq $window) { throw 'TinyPix gate UIA window is unavailable.' }
[void][NarratorWindowNativeMethods]::SetForegroundWindow($process.MainWindowHandle)
Start-Sleep -Seconds 1
$baseline = Find-ByName $window '图片工具'
if ($null -eq $baseline) { throw 'The Narrator baseline control was not found.' }
$baseline.SetFocus()
Record-Focused 'NARRATOR_BASELINE' '图片工具'

$expectedF6 = @('图片工具', '添加媒体文件', '中央预览区域', '输出格式', '任务队列区域')
for ($index = 0; $index -lt $expectedF6.Count; $index++) {
    [System.Windows.Forms.SendKeys]::SendWait('{F6}')
    Record-Focused "NARRATOR_F6_$($index + 1)" $expectedF6[$index]
}

[System.Windows.Forms.SendKeys]::SendWait('^j')
Record-Focused 'NARRATOR_CTRL_J' '任务队列区域'

$settings = Find-ByName $window '打开设置'
if ($null -eq $settings) { throw 'The accessible Settings trigger was not found.' }
$settings.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 1
$dialog = Find-ByName $window '设置'
if ($null -eq $dialog) { throw 'The Settings dialog was not exposed to Narrator/UIA.' }
$output = Find-ByName $dialog '默认输出目录'
if ($null -eq $output) { throw 'The Settings output field has no accessible name.' }
$output.SetFocus()
Record-Focused 'NARRATOR_SETTINGS' '默认输出目录'

[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Record-Focused 'NARRATOR_SETTINGS_RETURN' '打开设置'
$narratorStillActive = $null -ne (Get-Process Narrator -ErrorAction SilentlyContinue | Select-Object -First 1)
Record "NARRATOR processStillActive=$narratorStillActive"
if (-not $narratorStillActive) { throw 'Narrator stopped during the keyboard focus walk.' }

Save-Screenshot '31-narrator-focus-walk.png'
Record 'NARRATOR_UIA complete=true'
