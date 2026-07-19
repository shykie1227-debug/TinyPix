using System.Diagnostics;
using System.Text;

namespace TinyPix.Media.Ffmpeg;

public sealed record FfmpegProcessResult(int ExitCode, string StandardError);

public sealed class FfmpegProcessRunner
{
    private const int MaximumCapturedErrorCharacters = 64 * 1024;

    public async Task<FfmpegProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan? duration,
        IProgress<FfmpegProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"无法启动本地媒体引擎：{executablePath}");
        }

        var parser = new FfmpegProgressParser(duration);
        var standardError = new StringBuilder();
        Task outputTask = ReadProgressAsync(process.StandardOutput, parser, progress);
        Task errorTask = ReadErrorAsync(process.StandardError, standardError);

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
        return new FfmpegProcessResult(process.ExitCode, standardError.ToString());
    }

    private static async Task ReadProgressAsync(
        StreamReader reader,
        FfmpegProgressParser parser,
        IProgress<FfmpegProgress>? progress)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            FfmpegProgress? update = parser.ParseLine(line);
            if (update is not null)
            {
                progress?.Report(update);
            }
        }
    }

    private static async Task ReadErrorAsync(StreamReader reader, StringBuilder captured)
    {
        char[] buffer = new char[2048];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) is int count && count > 0)
        {
            int remaining = MaximumCapturedErrorCharacters - captured.Length;
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
