$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GateNativeMethods {
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public static void EnablePhysicalScreenCapture() { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
}
'@
[GateNativeMethods]::EnablePhysicalScreenCapture()

$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$reportPath = Join-Path $gateRoot 'uia-report.txt'
$lines = New-Object System.Collections.Generic.List[string]
Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue

function Record([string]$message) {
    $entry = "$(Get-Date -Format o) $message"
    $lines.Add($entry)
    Add-Content -LiteralPath $reportPath -Value $entry -Encoding utf8
}

function Find-DescendantByName($root, [string]$name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-DescendantByAutomationId($root, [string]$automationId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $automationId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Invoke-Element($element) {
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Wait-ForMainWindow {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        $candidates = @(Get-Process TinyPix.WinUIGate -ErrorAction SilentlyContinue | Sort-Object StartTime -Descending)
        foreach ($candidate in $candidates) {
            $candidate.Refresh()
            if ($candidate.MainWindowHandle -ne [IntPtr]::Zero) {
                return $candidate
            }
        }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    $diagnostics = @(Get-Process TinyPix.WinUIGate -ErrorAction SilentlyContinue | ForEach-Object {
        $_.Refresh()
        "pid=$($_.Id) handle=$($_.MainWindowHandle) responding=$($_.Responding) path=$($_.Path)"
    })
    if ($diagnostics.Count -gt 0) {
        Record "WINDOW_WAIT_FAILED $($diagnostics -join ' | ')"
    }
    throw 'TinyPix gate window handle stayed zero.'
}

$process = Wait-ForMainWindow
$window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if ($null -eq $window) { throw 'TinyPix gate window was not found through UI Automation.' }

$rect = $window.Current.BoundingRectangle
$dpi = [GateNativeMethods]::GetDpiForWindow($process.MainWindowHandle)
Record "WINDOW name=$($window.Current.Name) width=$([math]::Round($rect.Width)) height=$([math]::Round($rect.Height)) dpi=$dpi scale=$([math]::Round($dpi / 96, 2)) pid=$($process.Id)"
$initialScreen = [System.Windows.Forms.Screen]::FromHandle($process.MainWindowHandle).WorkingArea
$initialFitsScreen = $rect.Left -ge $initialScreen.Left -and $rect.Top -ge $initialScreen.Top -and $rect.Right -le $initialScreen.Right -and $rect.Bottom -le $initialScreen.Bottom
Record "INITIAL_WINDOW left=$([math]::Round($rect.Left)) top=$([math]::Round($rect.Top)) width=$([math]::Round($rect.Width)) height=$([math]::Round($rect.Height)) screenLeft=$($initialScreen.Left) screenTop=$($initialScreen.Top) screenWidth=$($initialScreen.Width) screenHeight=$($initialScreen.Height) fitsScreen=$initialFitsScreen"
if (-not $initialFitsScreen) { throw 'Initial window does not fit within the display work area.' }
if (-not [GateNativeMethods]::MoveWindow($process.MainWindowHandle, $initialScreen.Left, $initialScreen.Top, [math]::Round($rect.Width), [math]::Round($rect.Height), $true)) {
    throw 'Initial window could not be moved to the deterministic capture origin.'
}
Start-Sleep -Milliseconds 300
$window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

$buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
$buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
foreach ($button in $buttons) {
    $buttonRect = $button.Current.BoundingRectangle
    Record "BUTTON name=$($button.Current.Name) width=$([math]::Round($buttonRect.Width)) height=$([math]::Round($buttonRect.Height)) enabled=$($button.Current.IsEnabled)"
}

$settings = Find-DescendantByName $window '打开设置'
if ($null -eq $settings) { throw 'Settings button was not found.' }
Invoke-Element $settings
Start-Sleep -Milliseconds 700

$dialog = Find-DescendantByName $window '设置'
if ($null -eq $dialog) { throw 'Settings ContentDialog was not found.' }
$dialogRect = $dialog.Current.BoundingRectangle
Record "DIALOG name=$($dialog.Current.Name) type=$($dialog.Current.ControlType.ProgrammaticName) width=$([math]::Round($dialogRect.Width)) height=$([math]::Round($dialogRect.Height))"

$backgroundButton = Find-DescendantByName $window '图片工具'
Record "DIALOG_BACKGROUND enabled=$($backgroundButton.Current.IsEnabled) focusable=$($backgroundButton.Current.IsKeyboardFocusable)"
$backgroundRect = $backgroundButton.Current.BoundingRectangle
$backgroundX = [math]::Round($backgroundRect.Left + ($backgroundRect.Width / 2))
$backgroundY = [math]::Round($backgroundRect.Top + ($backgroundRect.Height / 2))
[void][GateNativeMethods]::SetCursorPos($backgroundX, $backgroundY)
[GateNativeMethods]::mouse_event([GateNativeMethods]::MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
[GateNativeMethods]::mouse_event([GateNativeMethods]::MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 300
$dialogAfterBackgroundClick = $null -ne (Find-DescendantByName $window '保存设置')
$focusedAfterBackgroundClick = [System.Windows.Automation.AutomationElement]::FocusedElement
Record "DIALOG_BACKGROUND_CLICK dialogOpen=$dialogAfterBackgroundClick focus=$($focusedAfterBackgroundClick.Current.Name)"
if (-not $dialogAfterBackgroundClick) { throw 'Settings dialog closed after background click.' }

$save = Find-DescendantByName $window '保存设置'
$cancel = Find-DescendantByName $window '取消'
if ($null -eq $save -or $null -eq $cancel) { throw 'Dialog action buttons were not found.' }
foreach ($action in @($save, $cancel)) {
    $actionRect = $action.Current.BoundingRectangle
    Record "DIALOG_BUTTON name=$($action.Current.Name) width=$([math]::Round($actionRect.Width)) height=$([math]::Round($actionRect.Height))"
}

$outputField = Find-DescendantByName $window '默认输出目录'
if ($null -eq $outputField) { throw 'Settings output field was not found.' }
$outputField.SetFocus()
for ($i = 1; $i -le 6; $i++) {
    [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
    Start-Sleep -Milliseconds 150
    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    Record "DIALOG_TAB_$i name=$($focused.Current.Name) type=$($focused.Current.ControlType.ProgrammaticName)"
}
$valuePattern = $outputField.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$valuePattern.SetValue('Z:\missing\TinyPix\Output')
Invoke-Element $save
Start-Sleep -Milliseconds 500
$focused = [System.Windows.Automation.AutomationElement]::FocusedElement
Record "SETTINGS_INVALID dialogOpen=$($null -ne (Find-DescendantByName $window '保存设置')) focus=$($focused.Current.Name)"

$valuePattern.SetValue((Join-Path $gateRoot 'bin\Debug\net10.0-windows10.0.19041.0\win-x64\Output'))
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep -Milliseconds 500
$focused = [System.Windows.Automation.AutomationElement]::FocusedElement
Record "SETTINGS_VALID dialogOpen=$($null -ne (Find-DescendantByName $window '保存设置')) focus=$($focused.Current.Name)"

$settings = Find-DescendantByName $window '打开设置'
Invoke-Element $settings
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Milliseconds 500
$focused = [System.Windows.Automation.AutomationElement]::FocusedElement
Record "SETTINGS_ESCAPE dialogOpen=$($null -ne (Find-DescendantByName $window '保存设置')) focus=$($focused.Current.Name)"

$settings = Find-DescendantByName $window '打开设置'
$settings.SetFocus()
Start-Sleep -Milliseconds 300
for ($i = 1; $i -le 5; $i++) {
    [System.Windows.Forms.SendKeys]::SendWait('{F6}')
    Start-Sleep -Milliseconds 250
    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    Record "F6_$i name=$($focused.Current.Name) type=$($focused.Current.ControlType.ProgrammaticName)"
}

[System.Windows.Forms.SendKeys]::SendWait('^j')
Start-Sleep -Milliseconds 300
$focused = [System.Windows.Automation.AutomationElement]::FocusedElement
Record "CTRL_J name=$($focused.Current.Name) type=$($focused.Current.ControlType.ProgrammaticName)"

$transform = $window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
if ($transform.Current.CanResize) {
    $targetWidth = [math]::Round(900 * $dpi / 96)
    $targetHeight = [math]::Round(600 * $dpi / 96)
    $transform.Resize($targetWidth, $targetHeight)
    Start-Sleep -Milliseconds 700
    $rect = $window.Current.BoundingRectangle
    $screenBounds = [System.Windows.Forms.Screen]::FromHandle($process.MainWindowHandle).Bounds
    Record "RESIZED width=$([math]::Round($rect.Width)) height=$([math]::Round($rect.Height)) effectiveWidth=$([math]::Round($rect.Width * 96 / $dpi)) effectiveHeight=$([math]::Round($rect.Height * 96 / $dpi))"

    $compactNames = @('输出目录', '开始本地验证任务', '取消任务', '验证异常退出')
    $compactElements = @{}
    foreach ($name in $compactNames) {
        $element = Find-DescendantByName $window $name
        if ($null -eq $element) { throw "Compact layout element was not found: $name" }
        $elementRect = $element.Current.BoundingRectangle
        Record "COMPACT_ELEMENT name=$name left=$([math]::Round($elementRect.Left)) top=$([math]::Round($elementRect.Top)) width=$([math]::Round($elementRect.Width)) height=$([math]::Round($elementRect.Height)) offscreen=$($element.Current.IsOffscreen)"
        if ($element.Current.IsOffscreen) {
            if ($name -eq '输出目录') {
                Record 'COMPACT_INITIAL_OFFSCREEN_ALLOWED name=输出目录'
                continue
            }
            throw "Compact layout element is offscreen: $name"
        }
        $withinWindow = $elementRect.Left -ge $rect.Left -and $elementRect.Top -ge $rect.Top -and $elementRect.Right -le $rect.Right -and $elementRect.Bottom -le $rect.Bottom
        $withinScreen = $elementRect.Left -ge $screenBounds.Left -and $elementRect.Top -ge $screenBounds.Top -and $elementRect.Right -le $screenBounds.Right -and $elementRect.Bottom -le $screenBounds.Bottom
        Record "COMPACT_WITHIN_WINDOW name=$name passed=$withinWindow"
        Record "COMPACT_WITHIN_SCREEN name=$name passed=$withinScreen"
        if (-not $withinWindow) { throw "Compact layout element extends outside the window: $name" }
        if (-not $withinScreen) { throw "Compact layout element extends outside the screen: $name" }
        $compactElements[$name] = $elementRect
    }

    $outputControl = Find-DescendantByAutomationId $window 'OutputPathBox'
    if ($null -eq $outputControl) { throw 'Compact output directory control was not found by AutomationId.' }
    $scrollItemPatternObject = $null
    $supportsScrollItem = $outputControl.TryGetCurrentPattern(
        [System.Windows.Automation.ScrollItemPattern]::Pattern,
        [ref]$scrollItemPatternObject)
    if ($supportsScrollItem) {
        ([System.Windows.Automation.ScrollItemPattern]$scrollItemPatternObject).ScrollIntoView()
        $scrollMode = 'ScrollItemPattern'
    } else {
        $outputControl.SetFocus()
        $scrollMode = 'FocusFallback'
    }
    Start-Sleep -Milliseconds 350
    $outputControl = Find-DescendantByAutomationId $window 'OutputPathBox'
    $outputControlRect = $outputControl.Current.BoundingRectangle
    $minimumVisibleHeight = [math]::Round(40 * $dpi / 96)
    $outputValue = $outputControl.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    $outputWithinWindow = $outputControlRect.Left -ge $rect.Left -and $outputControlRect.Top -ge $rect.Top -and $outputControlRect.Right -le $rect.Right -and $outputControlRect.Bottom -le $rect.Bottom
    $outputWithinScreen = $outputControlRect.Left -ge $screenBounds.Left -and $outputControlRect.Top -ge $screenBounds.Top -and $outputControlRect.Right -le $screenBounds.Right -and $outputControlRect.Bottom -le $screenBounds.Bottom
    Record "COMPACT_OUTPUT_SCROLL_INTO_VIEW mode=$scrollMode supported=$supportsScrollItem"
    Record "COMPACT_OUTPUT_WITHIN_WINDOW passed=$outputWithinWindow"
    Record "COMPACT_OUTPUT_WITHIN_SCREEN passed=$outputWithinScreen"
    Record "COMPACT_OUTPUT_CONTROL height=$([math]::Round($outputControlRect.Height)) minimumVisibleHeight=$minimumVisibleHeight value=$outputValue"
    if (-not $outputWithinWindow) { throw 'Compact output directory control extends outside the window after scrolling into view.' }
    if (-not $outputWithinScreen) { throw 'Compact output directory control extends outside the screen after scrolling into view.' }
    if ($outputControlRect.Height -lt $minimumVisibleHeight) {
        throw "Compact output directory is vertically clipped: height=$([math]::Round($outputControlRect.Height)), minimum=$minimumVisibleHeight"
    }
    foreach ($name in @('开始本地验证任务', '取消任务', '验证异常退出')) {
        $buttonRect = $compactElements[$name]
        $overlaps = $outputControlRect.Left -lt $buttonRect.Right -and $outputControlRect.Right -gt $buttonRect.Left -and $outputControlRect.Top -lt $buttonRect.Bottom -and $outputControlRect.Bottom -gt $buttonRect.Top
        Record "COMPACT_OVERLAP outputDirectory/$name=$overlaps"
        if ($overlaps) { throw "Compact output directory overlaps action button: $name" }
    }

    $captureName = if ($dpi -eq 96) { '28-compact-900x600-100percent.png' } elseif ($dpi -eq 144) { '29-compact-900x600-150percent.png' } else { "compact-900x600-dpi-$dpi.png" }
    $captureBounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap($captureBounds.Width, $captureBounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($captureBounds.Location, [System.Drawing.Point]::Empty, $captureBounds.Size)
        $bitmap.Save((Join-Path $gateRoot $captureName), [System.Drawing.Imaging.ImageFormat]::Png)
        Record "CAPTURE file=$captureName width=$($captureBounds.Width) height=$($captureBounds.Height)"
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
} else {
    Record 'RESIZED unsupported'
}
