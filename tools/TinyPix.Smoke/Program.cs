using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Ffmpeg;
using TinyPix.Media.Video;

namespace TinyPix.Smoke;

/// <summary>
/// Headless (no-UI) smoke harness for the real media/file pipelines. It drives the
/// production <see cref="IToolHandler"/> implementations against generated inputs and
/// verifies the LOCAL_RULES acceptance gates that can be checked without a window:
/// outputs exist and are non-empty, and every source file keeps its SHA-256.
///
/// Usage:
///   TinyPix.Smoke --engine &lt;dir-with-ffmpeg.exe&gt; --out &lt;workdir&gt; [--checks video]
///
/// Exit code 0 = no failed check (SKIP is not a failure), 1 = at least one FAIL.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string? engine = null;
        string? outDir = null;
        var checks = new List<string> { "video" };

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--engine": engine = args[++i]; break;
                case "--out": outDir = args[++i]; break;
                case "--checks": checks = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 2;
            }
        }

        outDir ??= Path.Combine(Path.GetTempPath(), "tinypix-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        int failed = 0, skipped = 0;
        foreach (string check in checks)
        {
            bool ok = check.ToLowerInvariant() switch
            {
                "video" => RunVideoChecks(engine, outDir, ref failed, ref skipped),
                _ => Skip($"unknown check '{check}'", ref skipped),
            };
            _ = ok;
        }

        Console.WriteLine();
        Console.WriteLine($"SMOKE SUMMARY: failed={failed} skipped={skipped}");
        return failed == 0 ? 0 : 1;
    }

    private static bool Skip(string reason, ref int skipped)
    {
        skipped++;
        Console.WriteLine($"SKIP  {reason}");
        return true;
    }

    private static bool Fail(string reason)
    {
        Console.WriteLine($"FAIL  {reason}");
        return false;
    }

    private sealed class EngineProvider : IFfmpegEngineProvider
    {
        public EngineProvider(string ffmpeg) => FfmpegExecutablePath = ffmpeg;

        public string FfmpegExecutablePath { get; }
    }

    private static bool RunVideoChecks(string? engine, string outDir, ref int failed, ref int skipped)
    {
        string ffmpeg = engine is null ? "ffmpeg" : Path.Combine(engine, "ffmpeg.exe");
        if (!File.Exists(ffmpeg))
        {
            return Skip($"video checks need ffmpeg.exe (looked in {ffmpeg})", ref skipped);
        }

        // Generate a deterministic source clip (same recipe as scripts/windows_media_matrix.ps1).
        string source = Path.Combine(outDir, "smoke-source.mp4");
        int gen = RunProcess(ffmpeg,
            "-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=640x360:rate=24 " +
            "-f lavfi -i sine=frequency=1000:sample_rate=48000 -t 3 -c:v libopenh264 -pix_fmt yuv420p -c:a aac -shortest " + Quote(source));
        if (gen != 0 || !File.Exists(source))
        {
            failed++;
            return Fail("could not generate the smoke source video with the bundled engine");
        }

        string sourceHash = Sha256(source);
        Console.WriteLine($"PASS  source video generated ({new FileInfo(source).Length} B, sha256 {sourceHash[..12]}…)");

        var provider = new EngineProvider(ffmpeg);
        bool ok = true;

        // video.convert
        ok &= RunHandler("video.convert", new VideoConvertHandler(provider),
            new Dictionary<string, string> { ["format"] = "mp4", ["quality"] = "80" },
            source, outDir, sourceHash, "_转换.mp4", ref failed, ref skipped);

        // video.gif (two-pass palette pipeline, 2 s window inside the 30 s frozen cap)
        ok &= RunHandler("video.gif", new GifMakerHandler(provider),
            new Dictionary<string, string>
            {
                ["format"] = "gif",
                ["startSecs"] = "0.5",
                ["endSecs"] = "2.5",
                ["fps"] = "12",
                ["width"] = "480",
            },
            source, outDir, sourceHash, "_GIF.gif", ref failed, ref skipped);

        if (Sha256(source) != sourceHash)
        {
            failed++;
            ok = false;
            Fail("source file SHA-256 changed after processing");
        }

        return ok;
    }

    private static bool RunHandler(
        string toolId,
        IToolHandler handler,
        Dictionary<string, string> parameters,
        string source,
        string outDir,
        string sourceHash,
        string expectedSuffix,
        ref int failed,
        ref int skipped)
    {
        string expected = Path.Combine(outDir, Path.GetFileNameWithoutExtension(source) + expectedSuffix);
        if (File.Exists(expected))
        {
            File.Delete(expected);
        }

        var request = JobRequest.Create(toolId, new[] { source }, outDir, JobWorkload.HeavyMedia, parameters);
        try
        {
            ToolExecutionResult result = handler.ExecuteAsync(
                new JobExecutionContext(request, new InlineProgress(_ => { })), CancellationToken.None).GetAwaiter().GetResult();

            if (result.PartialFailure is not null)
            {
                failed++;
                return Fail($"{toolId}: partial failure {result.PartialFailure.Code} {result.PartialFailure.Message}");
            }

            string? produced = result.OutputPaths.FirstOrDefault();
            if (produced is null || !File.Exists(produced) || new FileInfo(produced).Length == 0)
            {
                failed++;
                return Fail($"{toolId}: no non-empty output produced (expected {expected})");
            }

            if (!string.Equals(Path.GetFullPath(produced), Path.GetFullPath(expected), StringComparison.Ordinal))
            {
                failed++;
                return Fail($"{toolId}: output {produced} does not match the frozen naming rule {expected}");
            }

            if (Sha256(source) != sourceHash)
            {
                failed++;
                return Fail($"{toolId}: source file was modified");
            }

            Console.WriteLine($"PASS  {toolId} → {Path.GetFileName(produced)} ({new FileInfo(produced).Length} B)");
            return true;
        }
        catch (Exception ex)
        {
            failed++;
            return Fail($"{toolId}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static int RunProcess(string fileName, string arguments)
    {
        var info = new ProcessStartInfo(fileName, arguments) { UseShellExecute = false, RedirectStandardError = true };
        using var process = Process.Start(info);
        if (process is null)
        {
            return -1;
        }

        _ = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string Quote(string path) => "\"" + path + "\"";

    private sealed class InlineProgress : IProgress<JobProgress>
    {
        private readonly Action<JobProgress> _callback;

        public InlineProgress(Action<JobProgress> callback) => _callback = callback;

        public void Report(JobProgress value) => _callback(value);
    }
}
