$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class PickerNativeMethods {
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("user32.dll")]
    public static extern IntPtr GetDlgItem(IntPtr dialog, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetWindowText(IntPtr window, string text);
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr window);

    private static string Describe(IntPtr window) {
        var className = new StringBuilder(256);
        var title = new StringBuilder(512);
        GetClassName(window, className, className.Capacity);
        GetWindowText(window, title, title.Capacity);
        return string.Format("hwnd=0x{0:X} visible={1} id={2} class={3} title={4}", window.ToInt64(), IsWindowVisible(window), GetDlgCtrlID(window), className, title);
    }

    public static string[] DescribeAll(IntPtr parent) {
        var result = new List<string>();
        EnumWindowsProc callback = (window, parameter) => { result.Add(Describe(window)); return true; };
        if (parent == IntPtr.Zero) EnumWindows(callback, IntPtr.Zero);
        else EnumChildWindows(parent, callback, IntPtr.Zero);
        return result.ToArray();
    }

    public static IntPtr[] GetVisibleByClass(string expectedClass) {
        var result = new List<IntPtr>();
        EnumWindowsProc callback = (window, parameter) => {
            var className = new StringBuilder(256);
            GetClassName(window, className, className.Capacity);
            if (IsWindowVisible(window) && string.Equals(className.ToString(), expectedClass, StringComparison.Ordinal)) result.Add(window);
            return true;
        };
        EnumWindows(callback, IntPtr.Zero);
        return result.ToArray();
    }

    public static IntPtr FindDescendantByClassAndId(IntPtr parent, string expectedClass, int expectedId) {
        IntPtr found = IntPtr.Zero;
        EnumWindowsProc callback = (window, parameter) => {
            var className = new StringBuilder(256);
            GetClassName(window, className, className.Capacity);
            if (IsWindowVisible(window) && GetDlgCtrlID(window) == expectedId && string.Equals(className.ToString(), expectedClass, StringComparison.Ordinal)) {
                found = window;
                return false;
            }
            return true;
        };
        EnumChildWindows(parent, callback, IntPtr.Zero);
        return found;
    }

    public static bool ClickCenter(IntPtr window) {
        RECT rect;
        if (!GetWindowRect(window, out rect)) return false;
        if (!SetCursorPos((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2)) return false;
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        return true;
    }
}
'@

$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$outputRoot = Join-Path $gateRoot 'bin\Debug\net10.0-windows10.0.19041.0\win-x64'
$reportPath = Join-Path $gateRoot 'media-uia-report.txt'
Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue

function Record([string]$message) {
    Add-Content -LiteralPath $reportPath -Value "$(Get-Date -Format o) $message" -Encoding utf8
}

function Find-ByName($root, [string]$name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-ByAutomationId($root, [string]$automationId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Refresh-GateWindow() {
    $currentProcess = Get-Process TinyPix.WinUIGate -ErrorAction Stop |
        Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
        Select-Object -First 1
    if ($null -eq $currentProcess) { throw 'TinyPix gate process has no available main window.' }
    $currentWindow = [System.Windows.Automation.AutomationElement]::FromHandle($currentProcess.MainWindowHandle)
    if ($null -eq $currentWindow) { throw 'TinyPix gate window could not be refreshed after the file picker closed.' }
    return $currentWindow
}

function Find-CommonFileDialog() {
    $classCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, '#32770')
    $windowCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window)
    $condition = New-Object System.Windows.Automation.AndCondition($classCondition, $windowCondition)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Invoke-Element($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Save-Screenshot([string]$name) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $bitmap.Save((Join-Path $gateRoot $name), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Import-With-Picker($window, [string]$path, [string[]]$expectedPreviewNames, [bool]$requirePreview = $true) {
    Invoke-Element (Find-ByName $window '添加媒体文件')
    $dialogDeadline = (Get-Date).AddSeconds(6)
    do {
        Start-Sleep -Milliseconds 250
        $dialogHandles = [PickerNativeMethods]::GetVisibleByClass('#32770')
    } until ($dialogHandles.Count -gt 0 -or (Get-Date) -gt $dialogDeadline)
    if ($dialogHandles.Count -ne 1) {
        foreach ($description in [PickerNativeMethods]::DescribeAll([IntPtr]::Zero)) { Record "WIN32_TOP $description" }
        foreach ($description in [PickerNativeMethods]::DescribeAll([IntPtr]$process.MainWindowHandle)) { Record "WIN32_CHILD $description" }
        throw "Expected one Win32 file picker window, found $($dialogHandles.Count)."
    }
    $dialogHandle = $dialogHandles[0]
    $fileNameHandle = [PickerNativeMethods]::FindDescendantByClassAndId($dialogHandle, 'Edit', 1148)
    $openButtonHandle = [PickerNativeMethods]::GetDlgItem($dialogHandle, 1)
    if ($fileNameHandle -eq [IntPtr]::Zero -or $openButtonHandle -eq [IntPtr]::Zero) {
        throw 'The Win32 file picker filename/open controls were not found.'
    }
    [void][PickerNativeMethods]::SetForegroundWindow($dialogHandle)
    [void][PickerNativeMethods]::SendMessage($dialogHandle, 0x0028, $fileNameHandle, [IntPtr]1)
    Start-Sleep -Milliseconds 150
    [System.Windows.Forms.Clipboard]::SetText($path)
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    [System.Windows.Forms.SendKeys]::SendWait('^v')
    Start-Sleep -Milliseconds 150
    if (-not [PickerNativeMethods]::ClickCenter($openButtonHandle)) {
        throw 'The Win32 file picker Open button could not be clicked.'
    }
    Start-Sleep -Seconds 2
    if (-not $requirePreview) {
        Record "PICKER path=$path preview=pending-fallback"
        return
    }
    $preview = $null
    $actualPreviewName = $null
    foreach ($expectedPreviewName in $expectedPreviewNames) {
        $candidate = Find-ByName $window $expectedPreviewName
        if ($null -ne $candidate -and -not $candidate.Current.IsOffscreen) {
            $preview = $candidate
            $actualPreviewName = $expectedPreviewName
            break
        }
    }
    if ($null -eq $preview -and $requirePreview) {
        throw "Picker import did not expose any expected preview: $($expectedPreviewNames -join ', ')."
    }
    if ($null -eq $preview) {
        Record "PICKER path=$path preview=pending-fallback"
    } else {
        Record "PICKER path=$path preview=$actualPreviewName offscreen=$($preview.Current.IsOffscreen)"
    }
}

$process = Get-Process TinyPix.WinUIGate -ErrorAction Stop | Select-Object -First 1
$window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if ($null -eq $window) { throw 'TinyPix gate window was not found.' }
$root = [System.Windows.Automation.AutomationElement]::RootElement
$topLevel = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($element in $topLevel) {
    Record "TOP name=$($element.Current.Name) class=$($element.Current.ClassName) type=$($element.Current.ControlType.ProgrammaticName) id=$($element.Current.AutomationId)"
}
$existingDialogs = [PickerNativeMethods]::GetVisibleByClass('#32770')
foreach ($existingDialog in $existingDialogs) {
    $existingCancel = [PickerNativeMethods]::GetDlgItem($existingDialog, 2)
    if ($existingCancel -ne [IntPtr]::Zero) {
        [void][PickerNativeMethods]::SendMessage($existingCancel, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
    }
    [void][PickerNativeMethods]::SendMessage($existingDialog, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
}
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')

$imagePath = Join-Path $gateRoot 'Samples\sample-image.png'
$videoPath = Join-Path $gateRoot 'Samples\sample-video.mp4'
Import-With-Picker $window $imagePath '图片预览'
Save-Screenshot '05-real-image-picker-preview.png'
Import-With-Picker $window $videoPath '视频预览'
Save-Screenshot '06-real-video-picker-preview.png'

$gateLog = Join-Path $outputRoot 'Logs\gate.log'
Remove-Item -LiteralPath $gateLog -Force -ErrorAction SilentlyContinue
$sourceBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath $videoPath).Hash
Invoke-Element (Find-ByName $window '开始本地验证任务')
Start-Sleep -Seconds 2
$cancel = Find-ByName $window '取消任务'
Record "CANCEL enabledBefore=$($cancel.Current.IsEnabled)"
Invoke-Element $cancel
Start-Sleep -Seconds 2
$ffmpegAfterCancel = @(Get-Process ffmpeg -ErrorAction SilentlyContinue).Count
Record "CANCEL ffmpegProcessCount=$ffmpegAfterCancel sourceUnchanged=$($sourceBefore -eq (Get-FileHash -Algorithm SHA256 -LiteralPath $videoPath).Hash)"

Invoke-Element (Find-ByName $window '开始本地验证任务')
$deadline = (Get-Date).AddSeconds(25)
do {
    Start-Sleep -Seconds 1
    $log = Get-Content -LiteralPath $gateLog -Raw
    $finished = $log -match 'JOB exit=0 sourceUnchanged=True'
} until ($finished -or (Get-Date) -gt $deadline)
if (-not $finished) { throw 'Successful FFmpeg job did not finish within 25 seconds.' }
$sourceAfter = (Get-FileHash -Algorithm SHA256 -LiteralPath $videoPath).Hash
$outputPath = Join-Path $outputRoot 'Output\sample-video-gate.mp4'
Record "SUCCESS outputExists=$(Test-Path -LiteralPath $outputPath) outputBytes=$((Get-Item -LiteralPath $outputPath).Length) sourceUnchanged=$($sourceBefore -eq $sourceAfter) ffmpegProcessCount=$(@(Get-Process ffmpeg -ErrorAction SilentlyContinue).Count)"

Invoke-Element (Find-ByName $window '验证异常退出')
Start-Sleep -Seconds 2
$log = Get-Content -LiteralPath $gateLog -Raw
Record "FAILURE isolated=$($log -match 'JOB abnormalExit=-?\d+ isolated=True') ffmpegProcessCount=$(@(Get-Process ffmpeg -ErrorAction SilentlyContinue).Count)"
Save-Screenshot '07-media-job-success.png'

$ffmpeg = Join-Path $outputRoot 'Engines\ffmpeg.exe'
$fallbackVideoPath = Join-Path $gateRoot 'Samples\sample-video-ffv1.mkv'
$fallbackThumbnail = Join-Path $outputRoot 'Cache\video-fallback.png'
Remove-Item -LiteralPath $fallbackVideoPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $fallbackThumbnail -Force -ErrorAction SilentlyContinue
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc=size=320x240:rate=5' -t 2 -c:v ffv1 $fallbackVideoPath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $fallbackVideoPath)) {
    throw 'FFmpeg could not create the FFV1/MKV fallback sample.'
}
$fallbackSourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $fallbackVideoPath).Hash
Import-With-Picker $window $fallbackVideoPath @('视频预览', '图片预览') $false
$fallbackDeadline = (Get-Date).AddSeconds(20)
$fallbackReady = $false
$uiaTransitionObserved = $false
do {
    Start-Sleep -Milliseconds 500
    try {
        $window = Refresh-GateWindow
        $fallbackStatus = Find-ByName $window '已显示 FFmpeg 本地缩略图回退'
        $fallbackPreview = Find-ByName $window '图片预览'
        $fallbackReady = $null -ne $fallbackStatus -and $null -ne $fallbackPreview -and -not $fallbackPreview.Current.IsOffscreen -and (Test-Path -LiteralPath $fallbackThumbnail)
    }
    catch [System.Runtime.InteropServices.COMException] {
        $uiaTransitionObserved = $true
        $fallbackReady = $false
    }
} until ($fallbackReady -or (Get-Date) -gt $fallbackDeadline)
if (-not $fallbackReady) {
    throw 'The real FFV1/MKV sample did not reach the FFmpeg thumbnail fallback state.'
}
Record "VIDEO_FALLBACK systemDecodeFailureObserved=true uiaTransitionObserved=$uiaTransitionObserved"
$fallbackSourceUnchanged = $fallbackSourceHash -eq (Get-FileHash -Algorithm SHA256 -LiteralPath $fallbackVideoPath).Hash
Record "VIDEO_FALLBACK status=ready thumbnailExists=$(Test-Path -LiteralPath $fallbackThumbnail) previewOffscreen=$($fallbackPreview.Current.IsOffscreen) sourceUnchanged=$fallbackSourceUnchanged"
Save-Screenshot '30-video-ffmpeg-thumbnail-fallback.png'
Record 'MEDIA_UIA complete=true'
