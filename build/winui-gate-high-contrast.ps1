$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HighContrastNativeMethods {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct HIGHCONTRAST {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr lpszDefaultScheme;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref HIGHCONTRAST value, uint update);
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    public static void EnablePhysicalScreenCapture() { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
    public static HIGHCONTRAST Get() {
        var value = new HIGHCONTRAST { cbSize = (uint)Marshal.SizeOf(typeof(HIGHCONTRAST)) };
        if (!SystemParametersInfo(0x0042, value.cbSize, ref value, 0)) throw new System.ComponentModel.Win32Exception();
        return value;
    }
    public static void SetFlags(uint flags) {
        var value = new HIGHCONTRAST { cbSize = (uint)Marshal.SizeOf(typeof(HIGHCONTRAST)), dwFlags = flags, lpszDefaultScheme = IntPtr.Zero };
        if (!SystemParametersInfo(0x0043, value.cbSize, ref value, 0x0002)) throw new System.ComponentModel.Win32Exception();
    }
}
'@

$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$reportPath = Join-Path $gateRoot 'high-contrast-report.txt'
[HighContrastNativeMethods]::EnablePhysicalScreenCapture()
$initial = [HighContrastNativeMethods]::Get()
try {
    [HighContrastNativeMethods]::SetFlags($initial.dwFlags -bor 1)
    Start-Sleep -Seconds 15
    $active = [HighContrastNativeMethods]::Get()
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $bitmap.Save((Join-Path $gateRoot '26-high-contrast-1200x800-125percent.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    $passed = ($active.dwFlags -band 1) -eq 1
    "$(Get-Date -Format o) HIGH_CONTRAST initialFlags=$($initial.dwFlags) activeFlags=$($active.dwFlags) systemActive=$passed" | Set-Content -LiteralPath $reportPath -Encoding utf8
    if (-not $passed) { throw 'Windows did not enter high contrast mode.' }
} finally {
    [HighContrastNativeMethods]::SetFlags($initial.dwFlags)
    Start-Sleep -Seconds 2
}
