using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace TinyPix.App.Services;

/// <summary>
/// Opt-in startup trace for the single-file publish. Writes a line-per-event log to
/// <c>%TEMP%\tinypix-startup.log</c> only when the environment variable
/// <c>TINYPIX_STARTUP_LOG</c> is set (to any value), so normal users pay nothing.
/// Used by the VM smoke probes to diagnose Windows App SDK bootstrap ordering.
/// </summary>
internal static class StartupTrace
{
    private static readonly object Gate = new();
    private static string? _path;
    private static bool _enabled;

    public static bool Enabled
    {
        get
        {
            if (!_enabled && _path is null)
            {
                string? flag = Environment.GetEnvironmentVariable("TINYPIX_STARTUP_LOG");
                if (string.IsNullOrEmpty(flag))
                {
                    return false;
                }

                _enabled = true;
                _path = Path.Combine(Path.GetTempPath(), "tinypix-startup.log");
            }

            return _enabled;
        }
    }

    public static void Write(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                File.AppendAllText(
                    _path!,
                    $"{DateTimeOffset.Now:HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Tracing must never take the app down.
        }
    }

    /// <summary>
    /// Records whether a native module is loaded, and from where. Critical for
    /// single-file: framework DLLs load from the .NET extraction directory, not
    /// the exe directory.
    /// </summary>
    public static void WriteModule(string dllName)
    {
        if (!Enabled)
        {
            return;
        }

        foreach (ProcessModule module in SafeModules())
        {
            string? fileName;
            try
            {
                fileName = module.FileName;
            }
            catch
            {
                continue;
            }

            if (!string.IsNullOrEmpty(fileName) &&
                string.Equals(Path.GetFileName(fileName), dllName, StringComparison.OrdinalIgnoreCase))
            {
                Write($"module {dllName} = {fileName}");
                return;
            }
        }

        Write($"module {dllName} = <not loaded>");
    }

    public static void WriteEnvironment(string name)
    {
        if (!Enabled)
        {
            return;
        }

        Write($"env {name} = {Environment.GetEnvironmentVariable(name) ?? "<unset>"}");
    }

    private static List<ProcessModule> SafeModules()
    {
        var modules = new List<ProcessModule>();
        try
        {
            // ProcessModuleCollection only implements non-generic IEnumerable, so
            // AddRange does not apply; enumerate and add one by one.
            foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
            {
                modules.Add(module);
            }
        }
        catch
        {
            // Best effort only.
        }

        return modules;
    }
}
