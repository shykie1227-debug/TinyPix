using System;
using System.IO;

namespace TinyPix.App.Services;

/// <summary>
/// Resolves the REAL executable directory in every publish mode.
///
/// SINGLE-FILE GOTCHA (verified 2026-10-19, .NET 10.0 win-x64, Win10 22H2 with a
/// minimal probe app): in a single-file bundle,
/// <see cref="AppContext.BaseDirectory"/> AND <see cref="System.Reflection.Assembly.Location"/>
/// both point at the .NET bundle EXTRACTION directory
/// (<c>%TEMP%\.net\TinyPix\&lt;hash&gt;\</c>) - not the exe directory. Only
/// <see cref="Environment.ProcessPath"/> reports the real exe path. Portable
/// assets (portable.flag, Engines\, Config\, Data\...) live next to the exe, so
/// resolving them from BaseDirectory breaks in single-file mode. Worse: the
/// resulting InvalidOperationException is thrown from the App constructor, which
/// runs inside the WinRT/COM activation path, so it escapes through the COM ABI
/// boundary and kills the process with a bare 0xC0000005 access violation -
/// WER only shows 0x80131509 (COR_E_INVALIDOPERATION) at combase.dll, which is
/// nearly impossible to trace back to the real cause without this knowledge.
/// </summary>
internal static class AppPaths
{
    /// <summary>
    /// Directory that physically contains TinyPix.exe (single-file) or the app
    /// assemblies (multi-file). This is the portable root.
    /// </summary>
    public static string ExecutableDirectory { get; } = ResolveExecutableDirectory();

    private static string ResolveExecutableDirectory()
    {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            string? directory = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrEmpty(directory))
            {
                return directory;
            }
        }

        // Exotic hosts only (e.g. `dotnet TinyPix.dll`); multi-file mode has
        // BaseDirectory == exe directory anyway.
        return AppContext.BaseDirectory;
    }
}
