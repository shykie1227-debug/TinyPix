# set-resolution.ps1 - raise the VM display mode so a 1200x800 client area fits.
$out = 'C:\Mac\Home\TinyPix\3.5pro\artifacts\ui-shots2'
$log = Join-Path $out 'resolution-log.txt'
function L([string]$m) { "$(Get-Date -Format 'HH:mm:ss.fff') $m" | Add-Content -LiteralPath $log }
"=== set-resolution $(Get-Date -Format o) session=$((Get-Process -Id $PID).SessionId) ===" | Set-Content -LiteralPath $log

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class Display
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    public static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

    public const int ENUM_CURRENT_SETTINGS = -1;
    public const int DM_PELSWIDTH = 0x80000;
    public const int DM_PELSHEIGHT = 0x100000;
    public const int CDS_UPDATEREGISTRY = 0x01;

    public static string Current()
    {
        var dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm)) { return "enum failed"; }
        return dm.dmPelsWidth + "x" + dm.dmPelsHeight + "@" + dm.dmDisplayFrequency;
    }

    public static string List()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 400; i++)
        {
            var dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (!EnumDisplaySettings(null, i, ref dm)) { break; }
            sb.Append(dm.dmPelsWidth).Append('x').Append(dm.dmPelsHeight)
              .Append('@').Append(dm.dmDisplayFrequency).Append(' ')
              .Append(dm.dmBitsPerPel).Append("bit; ");
        }
        return sb.ToString();
    }

    public static string Set(int w, int h)
    {
        var dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm)) { return "enum failed"; }
        dm.dmPelsWidth = w;
        dm.dmPelsHeight = h;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;
        int rc = ChangeDisplaySettings(ref dm, CDS_UPDATEREGISTRY);
        return "ChangeDisplaySettings(" + w + "x" + h + ") => " + rc;
    }
}
'@

L "current = $([Display]::Current())"
L "modes = $([Display]::List())"

$targets = @(@(1920, 1200), @(1920, 1080), @(1680, 1050), @(1600, 1200))
foreach ($t in $targets) {
    $r = [Display]::Set($t[0], $t[1])
    L "$r -> now $([Display]::Current())"
    if (("[Display]::Current()" -ne $null) -and ([Display]::Current() -like "$($t[0])x$($t[1])*")) {
        L "settled on $($t[0])x$($t[1])"
        break
    }
}
L "final = $([Display]::Current())"
L "=== done ==="
