# run-in-session.ps1 - launch a command inside the active console session.
# Task Scheduler is not dispatching tasks in this VM, so create the process with
# WTSQueryUserToken + CreateProcessAsUser (the technique PsExec uses).
param(
    [string]$Target = 'C:\Mac\Home\TinyPix\3.5pro\artifacts\ui-shots2\capture-ui.ps1'
)

$ErrorActionPreference = 'Stop'
$log = 'C:\Mac\Home\TinyPix\3.5pro\artifacts\ui-shots2\session-launch.log'
function L([string]$m) { "$(Get-Date -Format 'HH:mm:ss.fff') $m" | Add-Content -LiteralPath $log }
Set-Content -LiteralPath $log -Value "=== run-in-session $(Get-Date -Format o) caller=$((Get-Process -Id $PID).SessionId) ==="

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class SessionLauncher
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll")]
    public static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessAsUserW(
        IntPtr hToken,
        string lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr hObject);

    public static string Launch(uint sessionId, string commandLine)
    {
        IntPtr userToken;
        if (!WTSQueryUserToken(sessionId, out userToken))
        {
            return "WTSQueryUserToken failed: " + Marshal.GetLastWin32Error();
        }

        IntPtr primaryToken;
        if (!DuplicateTokenEx(userToken, 0xF01FF, IntPtr.Zero, 2, 1, out primaryToken))
        {
            return "DuplicateTokenEx failed: " + Marshal.GetLastWin32Error();
        }

        IntPtr env;
        CreateEnvironmentBlock(out env, primaryToken, false);

        var si = new STARTUPINFO();
        si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
        si.lpDesktop = "winsta0\\default";

        PROCESS_INFORMATION pi;
        const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        bool ok = CreateProcessAsUserW(
            primaryToken,
            null,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            CREATE_UNICODE_ENVIRONMENT,
            env,
            @"C:\Windows\System32",
            ref si,
            out pi);

        if (!ok)
        {
            return "CreateProcessAsUser failed: " + Marshal.GetLastWin32Error();
        }

        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        if (env != IntPtr.Zero) { DestroyEnvironmentBlock(env); }
        return "OK pid=" + pi.dwProcessId;
    }
}
'@

try {
    $session = [SessionLauncher]::WTSGetActiveConsoleSessionId()
    L "active console session = $session"
    $cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$Target`""
    L "command = $cmd"
    $result = [SessionLauncher]::Launch($session, $cmd)
    L "launch result: $result"
} catch {
    L "ERROR $($_.Exception.Message)"
    exit 1
}
exit 0
