# capture-ui.ps1 — TinyPix real-UI screenshot capture (run interactively as huashu)
# Registered via Task Scheduler with InteractiveToken; output PNGs to shared dir.
$ErrorActionPreference = 'Continue'
$out = 'C:\Mac\Home\TinyPix\3.5pro\artifacts\ui-shots'
$exe = 'C:\TinyPixBuild\src\artifacts\TinyPix-single-file\TinyPix.exe'
if (!(Test-Path -LiteralPath $exe)) { $exe = 'C:\Mac\Home\TinyPix\3.5pro\artifacts\TinyPix.exe' }
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
    Start-Sleep -Milliseconds 500
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

function Select-NavItem($Root, [string]$Name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $item = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($null -eq $item) { Log "nav NOT FOUND: $Name"; return $false }
    try {
        $p = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $p.Select()
        Log "nav selected (SelectionItem): $Name"
        return $true
    } catch {
        try {
            $p = $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            $p.Invoke()
            Log "nav invoked (Invoke): $Name"
            return $true
        } catch {
            Log "nav select/invoke FAILED $Name : $($_.Exception.Message)"
            return $false
        }
    }
}

# ---- launch ----
Get-Process -Name 'TinyPix' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
if (!(Test-Path -LiteralPath $exe)) { Log "EXE MISSING $exe"; 'EXE-MISSING' | Set-Content (Join-Path $out 'done.txt'); exit 2 }
Log "launch $exe"
$p = Start-Process -FilePath $exe -PassThru -WorkingDirectory (Split-Path $exe)

$main = $null
for ($i = 0; $i -lt 120; $i++) {
    Start-Sleep -Milliseconds 500
    $p.Refresh()
    if ($p.HasExited) { Log "process exited early code=$($p.ExitCode)"; break }
    if ($p.MainWindowHandle -ne 0 -and $p.MainWindowTitle -eq 'TinyPix') { $main = $p; break }
}
if ($null -eq $main) {
    # fallback: any TinyPix window
    $cand = Get-Process -Name 'TinyPix' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($null -ne $cand) { $main = $cand; Log "fallback window title='$($main.MainWindowTitle)'" }
}
if ($null -eq $main) {
    Log 'WINDOW NEVER APPEARED'
    Save-FullScreen (Join-Path $out '99-fail-fullscreen.png')
    'NO-WINDOW' | Set-Content (Join-Path $out 'done.txt')
    exit 3
}
$hwnd = $main.MainWindowHandle
Log "window ready hwnd=$hwnd title='$($main.MainWindowTitle)' pid=$($main.Id)"

[void][DpiNative]::ShowWindow($hwnd, 9)   # SW_RESTORE
[void][DpiNative]::MoveWindow($hwnd, 40, 40, 1440, 860, $true)
Start-Sleep -Seconds 3

$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# ---- 00 startup state (what the user sees first) ----
Save-WindowPrint $hwnd (Join-Path $out '00-startup-window.png')
Save-DesktopShot $hwnd (Join-Path $out '00-startup-desktop.png')

# ---- 5 category pages ----
$pages = @(
    @{ file = '01-image';       name = '图片工具' },
    @{ file = '02-video';       name = '视频工具' },
    @{ file = '03-gif';         name = 'GIF 制作' },
    @{ file = '04-video-edit';  name = '视频编辑' },
    @{ file = '05-toolbox';     name = '工具箱' }
)
foreach ($page in $pages) {
    [void](Select-NavItem $root $page.name)
    Start-Sleep -Seconds 2
    Save-WindowPrint $hwnd (Join-Path $out "$($page.file)-window.png")
    Save-DesktopShot $hwnd (Join-Path $out "$($page.file)-desktop.png")
}

# ---- settings dialog ----
[void](Select-NavItem $root '设置')
Start-Sleep -Seconds 2
Save-DesktopShot $hwnd (Join-Path $out '06-settings-desktop.png')
Save-WindowPrint $hwnd (Join-Path $out '06-settings-window.png')
# close dialog via its close button
$closeCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, '关闭')
$closeBtn = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $closeCond)
if ($null -ne $closeBtn) {
    try {
        $cp = $closeBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $cp.Invoke()
        Log 'settings dialog closed'
    } catch { Log "close dialog failed: $($_.Exception.Message)" }
}
Start-Sleep -Seconds 1

Save-FullScreen (Join-Path $out '99-fullscreen.png')

# ---- cleanup ----
Get-Process -Name 'TinyPix' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Log 'TinyPix stopped, capture done'
'DONE' | Set-Content (Join-Path $out 'done.txt')
exit 0
