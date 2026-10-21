# capture-ui.ps1 - TinyPix real-UI screenshot capture (run interactively as huashu)
# Registered via Task Scheduler with InteractiveToken; PNGs go to the shared dir.
param(
    [string]$Exe = 'C:\TinyPixBuild2\src\TinyPix.App\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\TinyPix.exe',
    [string]$OutDir = 'C:\Mac\Home\TinyPix\3.5pro\artifacts\ui-shots2'
)
$ErrorActionPreference = 'Continue'
$out = $OutDir
$exe = $Exe
$log = Join-Path $out 'capture-log.txt'
New-Item -ItemType Directory -Path $out -Force | Out-Null
"=== capture start $(Get-Date -Format o) session=$((Get-Process -Id $PID).SessionId) user=$([Security.Principal.WindowsIdentity]::GetCurrent().Name) ===" |
    Set-Content -LiteralPath $log

function Log([string]$m) { "$(Get-Date -Format 'HH:mm:ss.fff') $m" | Add-Content -LiteralPath $log }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DpiNative {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, int data, UIntPtr extra);
}
'@
[void][DpiNative]::SetProcessDPIAware()
Log 'DPI-aware set'

function Save-WindowPrint([IntPtr]$Hwnd, [string]$Path) {
    $rect = New-Object DpiNative+RECT
    [void][DpiNative]::GetWindowRect($Hwnd, [ref]$rect)
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { Log "printwindow skip, bad rect ${w}x${h}"; return }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    $ok = [DpiNative]::PrintWindow($Hwnd, $hdc, 2)  # PW_RENDERFULLCONTENT
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Log "printwindow $([IO.Path]::GetFileName($Path)) ok=$ok ${w}x${h}"
}

function Save-DesktopShot([IntPtr]$Hwnd, [string]$Path) {
    [void][DpiNative]::SetForegroundWindow($Hwnd)
    Start-Sleep -Milliseconds 400
    $rect = New-Object DpiNative+RECT
    [void][DpiNative]::GetWindowRect($Hwnd, [ref]$rect)
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { Log "desktop skip, bad rect ${w}x${h}"; return }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Log "desktop $([IO.Path]::GetFileName($Path)) ${w}x${h} at $($rect.Left),$($rect.Top)"
}

function Save-FullScreen([string]$Path) {
    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Log "fullscreen $([IO.Path]::GetFileName($Path)) $($bounds.Width)x$($bounds.Height)"
}

# ToggleButton / ListViewItem only reliably fire Click under a real mouse hit,
# so drive the UI with SetCursorPos + mouse_event instead of UIA patterns.
function Click-Element($element) {
    if ($null -eq $element) { Log 'click skip: null element'; return $false }
    $r = $element.Current.BoundingRectangle
    if ($r.Width -le 0 -or $r.Height -le 0) {
        # WinUI returns an empty rect for rows clipped outside the viewport, so
        # ask the list to bring the row into view before measuring again.
        try {
            $sip = $element.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern)
            $sip.ScrollIntoView()
            Start-Sleep -Milliseconds 350
            $r = $element.Current.BoundingRectangle
            Log "scrollintoview then rect=$($r.Width)x$($r.Height)"
        } catch {
            Log "scrollintoview failed: $($_.Exception.Message)"
        }
    }
    if ($r.Width -le 0 -or $r.Height -le 0) { Log 'click skip: empty rect'; return $false }
    $x = [int]($r.X + ($r.Width / 2))
    $y = [int]($r.Y + ($r.Height / 2))
    [void][DpiNative]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 150
    [DpiNative]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # LEFTDOWN
    Start-Sleep -Milliseconds 60
    [DpiNative]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # LEFTUP
    Log "click at $x,$y"
    return $true
}

function Find-ByName($root, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Click-ByName($root, [string]$name) {
    $el = Find-ByName $root $name
    if ($null -eq $el) { Log "NOT FOUND: $name"; Dump-Names $root; return $false }
    return (Click-Element $el)
}

# The left panel tool list is a virtualising ListView: rows outside the viewport
# are not in the UIA tree at all, so FindFirst cannot see them. Spin the wheel
# over the list and re-query after every notch.
function Scroll-Wheel([int]$notches) {
    $delta = if ($notches -gt 0) { -120 } else { 120 }
    for ($i = 0; $i -lt [Math]::Abs($notches); $i++) {
        [DpiNative]::mouse_event(0x0800, 0, 0, $delta, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 70
    }
}

function Find-ToolRow($root, [string]$name) {
    $el = Find-ByName $root $name
    if ($null -ne $el) {
        $r = $el.Current.BoundingRectangle
        if ($r.Width -gt 0 -and $r.Height -gt 0) { return $el }
    }

    $list = Find-ByName $root '工具列表'
    if ($null -eq $list) { Log 'tool list container not found'; return $null }

    # Wheel events are ignored by the WinUI ScrollViewer here (no hover state is
    # established by a bare SetCursorPos), so drive the scroll through UIA.
    try {
        $sp = $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    } catch {
        Log "no ScrollPattern on tool list: $($_.Exception.Message)"
        return $null
    }

    foreach ($pct in @(0, 8, 16, 24, 32, 40, 48, 56, 64, 72, 80, 88, 96, 100)) {
        try { $sp.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, $pct) }
        catch { Log "SetScrollPercent($pct) failed: $($_.Exception.Message)" }
        Start-Sleep -Milliseconds 300

        $el = Find-ByName $root $name
        if ($null -ne $el) {
            $r = $el.Current.BoundingRectangle
            if ($r.Width -gt 0 -and $r.Height -gt 0) {
                Log "found '$name' at scroll $pct%"
                return $el
            }
        }
    }

    Log "scroll search exhausted: $name"
    return $null
}

function Click-ToolRow($root, [string]$name) {
    $el = Find-ToolRow $root $name
    if ($null -eq $el) { Log "TOOL ROW NOT FOUND: $name"; Dump-Names $root; return $false }
    return (Click-Element $el)
}

# Every named element the shell exposes, so a failed lookup is self-diagnosing
# instead of a silent "NOT FOUND" with no idea what the shell actually shows.
function Dump-Names($root) {
    try {
        $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        $names = @()
        foreach ($e in $all) {
            $n = $e.Current.Name
            if (-not [string]::IsNullOrWhiteSpace($n)) { $names += $n }
        }
        Log "UIA names: $($names -join ' | ')"
    } catch {
        Log "dump failed: $($_.Exception.Message)"
    }
}

# ---- launch ----
Get-Process -Name 'TinyPix' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
if (!(Test-Path -LiteralPath $exe)) { Log "EXE MISSING $exe"; 'EXE-MISSING' | Set-Content (Join-Path $out 'done.txt'); exit 2 }

# portable.flag is the portable/offline boundary: BuildServices throws
# InvalidOperationException (surfacing as 0xC000027B through the WinRT ABI) when
# it is missing. A plain `dotnet build` output never contains it, so create it
# next to the exe before launching.
$exeDir = Split-Path -Parent $exe
$flag = Join-Path $exeDir 'portable.flag'
if (!(Test-Path -LiteralPath $flag)) {
    New-Item -ItemType File -Path $flag -Force | Out-Null
    Log "created portable.flag at $flag"
} else {
    Log 'portable.flag already present'
}
$env:TINYPIX_STARTUP_LOG = '1'
Log "launch $exe"
$p = Start-Process -FilePath $exe -PassThru -WorkingDirectory $exeDir

$main = $null
for ($i = 0; $i -lt 120; $i++) {
    Start-Sleep -Milliseconds 500
    $p.Refresh()
    if ($p.HasExited) { Log "process exited early code=$($p.ExitCode)"; break }
    if ($p.MainWindowHandle -ne 0 -and $p.MainWindowTitle -eq 'TinyPix') { $main = $p; break }
}
if ($null -eq $main) {
    $cand = Get-Process -Name 'TinyPix' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($null -ne $cand) { $main = $cand; Log "fallback window title='$($main.MainWindowTitle)'" }
}
if ($null -eq $main) {
    Log 'WINDOW NEVER APPEARED'
    $trace = Join-Path $env:TEMP 'tinypix-startup.log'
    if (Test-Path -LiteralPath $trace) {
        Log '--- startup trace ---'
        Get-Content -LiteralPath $trace | ForEach-Object { Log "  $_" }
    } else {
        Log 'startup trace missing (process died before App ctor)'
    }
    Save-FullScreen (Join-Path $out '99-fail-fullscreen.png')
    'NO-WINDOW' | Set-Content (Join-Path $out 'done.txt')
    exit 3
}
$hwnd = $main.MainWindowHandle
Log "window ready hwnd=$hwnd title='$($main.MainWindowTitle)' pid=$($main.Id)"

[void][DpiNative]::ShowWindow($hwnd, 9)   # SW_RESTORE
# 1216x848 outer => 1200x800 client (48px caption), matching the design frames.
[void][DpiNative]::MoveWindow($hwnd, 20, 20, 1216, 848, $true)
Start-Sleep -Seconds 3
[void][DpiNative]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds 600

$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# ---- 00 startup state (视频工具 > 视频输出) ----
Save-WindowPrint $hwnd (Join-Path $out '00-startup-window.png')

# ---- 1 图片工具 ----
[void](Click-ByName $root '图片工具'); Start-Sleep -Seconds 2
Save-WindowPrint $hwnd (Join-Path $out '01-image-window.png')

# ---- 2 视频工具 ----
[void](Click-ByName $root '视频工具'); Start-Sleep -Seconds 2
Save-WindowPrint $hwnd (Join-Path $out '02-video-window.png')

# ---- 3 GIF 制作 (left tool row) ----
[void](Click-ToolRow $root 'GIF 制作'); Start-Sleep -Seconds 2
Save-WindowPrint $hwnd (Join-Path $out '03-gif-window.png')

# ---- 4 视频时间剪辑 (timeline editor) ----
[void](Click-ToolRow $root '视频时间剪辑'); Start-Sleep -Seconds 2
Save-WindowPrint $hwnd (Join-Path $out '04-video-edit-window.png')

# ---- 5 工具箱 > PDF 页面管理 ----
[void](Click-ByName $root '工具箱'); Start-Sleep -Seconds 2
Save-WindowPrint $hwnd (Join-Path $out '05-pdf-window.png')

# ---- 6 OCR 与二维码识别 ----
[void](Click-ToolRow $root '图片文字识别'); Start-Sleep -Seconds 2
Save-WindowPrint $hwnd (Join-Path $out '06-ocr-window.png')

# ---- 7 二维码生成器 ----
[void](Click-ToolRow $root '二维码与条码生成'); Start-Sleep -Seconds 2
Save-WindowPrint $hwnd (Join-Path $out '07-generator-window.png')

# ---- 8 智能证件照 ----
[void](Click-ToolRow $root '智能证件照'); Start-Sleep -Seconds 2
Save-WindowPrint $hwnd (Join-Path $out '08-idphoto-window.png')

# ---- 9 设置弹窗 ----
[void](Click-ByName $root '设置'); Start-Sleep -Seconds 2
Save-WindowPrint $hwnd (Join-Path $out '09-settings-window.png')
[void](Click-ByName $root '关闭设置'); Start-Sleep -Seconds 1

Save-FullScreen (Join-Path $out '99-fullscreen.png')

# ---- cleanup ----
$tracePath = Join-Path $env:TEMP 'tinypix-startup.log'
if (Test-Path -LiteralPath $tracePath) {
    Log '--- startup trace ---'
    Get-Content -LiteralPath $tracePath | ForEach-Object { Log "  $_" }
} else {
    Log 'startup trace missing'
}

Get-Process -Name 'TinyPix' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Log 'TinyPix stopped, capture done'
'DONE' | Set-Content (Join-Path $out 'done.txt')
exit 0
