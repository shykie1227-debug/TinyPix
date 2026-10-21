using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TinyPix.Media.Ffmpeg;

/// <summary>
/// Result of an ffprobe invocation: exit code plus the raw JSON written to
/// stdout (ffprobe writes its report to stdout when <c>-print_format json</c>
/// is used; diagnostics go to stderr).
/// </summary>
public sealed record FfprobeResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Minimal ffprobe driver. Unlike <see cref="FfmpegProcessRunner"/> (which
/// parses encode progress from stdout and discards it) this runner keeps the
/// full stdout because that is where ffprobe's JSON report lands. Only the
/// pinned engine executable is ever launched; no shell is involved.
/// </summary>
public sealed class FfprobeRunner
{
    private const int MaximumCapturedCharacters = 1024 * 1024;

    public async Task<FfprobeResult> RunAsync(
        string executablePath,
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("quiet");
        startInfo.ArgumentList.Add("-print_format");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("-show_format");
        startInfo.ArgumentList.Add("-show_streams");
        startInfo.ArgumentList.Add(inputPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"无法启动本地媒体引擎：{executablePath}");
        }

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        Task outputTask = ReadAllAsync(process.StandardOutput, standardOutput);
        Task errorTask = ReadAllAsync(process.StandardError, standardError);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            throw;
        }

        await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
        return new FfprobeResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
    }

    private static async Task ReadAllAsync(StreamReader reader, StringBuilder captured)
    {
        char[] buffer = new char[4096];
        while (await reader.ReadAsync(buffer, cancellationToken: CancellationToken.None).ConfigureAwait(false) is int count && count > 0)
        {
            int remaining = MaximumCapturedCharacters - captured.Length;
            if (remaining > 0)
            {
                captured.Append(buffer, 0, Math.Min(count, remaining));
            }
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the HasExited check and Kill.
        }
    }
}
