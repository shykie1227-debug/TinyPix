using System;
using System.Diagnostics;
using System.IO;

namespace TinyPix.App.Services;

/// <summary>
/// Single-file runtime base directory fix (verified 2026-10-16, Win10 22H2).
///
/// In a .NET single-file bundle the Windows App SDK framework DLLs are extracted
/// to <c>%SystemTemp%\.net\&lt;app&gt;\&lt;hash&gt;\</c>, but
/// <see cref="AppContext.BaseDirectory"/> returns the directory of the EXE. The
/// SDK's own auto-initializer
/// (<c>UndockedRegFreeWinRTCS.AutoInitialize</c>, injected via
/// <c>WindowsAppSdkUndockedRegFreeWinRTInitialize</c>) sets
/// <c>MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY</c> to
/// <see cref="AppContext.BaseDirectory"/> - the wrong directory for single-file -
/// so the XAML framework can never locate its sibling DLLs and fail-fasts with
/// STATUS_STOWED_EXCEPTION 0xC000027B in Microsoft.UI.Xaml.dll before any window
/// appears.
///
/// This helper runs before the first XAML type is touched (App constructor) and
/// repoints the variable at the real extraction directory, discovered from the
/// loaded native module list. Multi-file publishes need no fix (the DLLs already
/// sit next to the exe), so the helper is a no-op there.
/// </summary>
internal static class WindowsAppRuntimeBaseDirectory
{
    private const string VariableName = "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY";
    private const string RuntimeDll = "Microsoft.WindowsAppRuntime.dll";

    /// <summary>
    /// Must be called before any WinUI/XAML type is loaded (i.e. before
    /// <c>InitializeComponent</c>) so the framework picks up the corrected
    /// directory during its own initialization.
    /// </summary>
    public static void EnsureCorrectForSingleFile()
    {
        string? current = Environment.GetEnvironmentVariable(VariableName);
        if (!string.IsNullOrEmpty(current) && File.Exists(Path.Combine(current, "Microsoft.UI.Xaml.dll")))
        {
            return; // already correct (multi-file: the exe directory holds the DLLs)
        }

        string? extractionDirectory = FindExtractionDirectory();
        if (extractionDirectory is not null)
        {
            Environment.SetEnvironmentVariable(VariableName, extractionDirectory);
        }
    }

    /// <summary>
    /// The extraction directory is where the loaded
    /// <c>Microsoft.WindowsAppRuntime.dll</c> lives (the SDK auto-initializer
    /// P/Invokes into it from the bundle, so it is always present in the module
    /// list by the time managed code runs). Falls back to scanning the .NET
    /// single-file extraction root for a directory holding the framework DLLs.
    /// </summary>
    private static string? FindExtractionDirectory()
    {
        try
        {
            foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
            {
                string? fileName;
                try
                {
                    fileName = module.FileName;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    continue; // modules from other sessions can deny access
                }

                if (!string.IsNullOrEmpty(fileName) &&
                    string.Equals(Path.GetFileName(fileName), RuntimeDll, StringComparison.OrdinalIgnoreCase))
                {
                    string? directory = Path.GetDirectoryName(fileName);
                    if (!string.IsNullOrEmpty(directory) &&
                        File.Exists(Path.Combine(directory, "Microsoft.UI.Xaml.dll")))
                    {
                        return directory;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Module enumeration is best-effort; the fallback scan below covers it.
        }

        return ScanExtractionRoot();
    }

    private static string? ScanExtractionRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), ".net");
        string appName = "TinyPix";
        string appRoot = Path.Combine(root, appName);
        if (!Directory.Exists(appRoot))
        {
            return null;
        }

        foreach (string candidate in Directory.GetDirectories(appRoot))
        {
            if (File.Exists(Path.Combine(candidate, "Microsoft.UI.Xaml.dll")))
            {
                return candidate;
            }
        }

        return null;
    }
}
