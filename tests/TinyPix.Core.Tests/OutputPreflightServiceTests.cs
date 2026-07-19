using TinyPix.Core.Storage;

namespace TinyPix.Core.Tests;

public sealed class OutputPreflightServiceTests
{
    [Fact]
    public void Valid_output_has_no_blocking_issues()
    {
        var environment = new ProbeOutputEnvironment();
        var service = new OutputPreflightService(environment);

        OutputPreflightResult result = service.Evaluate(new OutputPreflightRequest(
            ["C:/input/source.mp4"],
            ["C:/output/source-converted.mp4"],
            "C:/output",
            500));

        Assert.True(result.CanExecute);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData(false, 1_000, 500, "output.directory.not-writable")]
    [InlineData(true, 100, 500, "output.disk.insufficient")]
    public void Environment_failures_block_execution(
        bool writable,
        long availableBytes,
        long estimatedBytes,
        string expectedCode)
    {
        var environment = new ProbeOutputEnvironment
        {
            Writable = writable,
            AvailableBytes = availableBytes,
        };
        var service = new OutputPreflightService(environment);

        OutputPreflightResult result = service.Evaluate(new OutputPreflightRequest(
            ["C:/input/source.mp4"],
            ["C:/output/result.mp4"],
            "C:/output",
            estimatedBytes));

        Assert.False(result.CanExecute);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode && issue.IsBlocking);
    }

    [Fact]
    public void Source_overwrite_is_blocked_and_existing_output_requires_resolution()
    {
        var environment = new ProbeOutputEnvironment
        {
            ExistingPaths = { "C:/output/result.mp4" },
        };
        var service = new OutputPreflightService(environment);

        OutputPreflightResult result = service.Evaluate(new OutputPreflightRequest(
            ["C:/input/source.mp4"],
            ["c:/INPUT/SOURCE.mp4", "C:/output/result.mp4"],
            "C:/output",
            500));

        Assert.False(result.CanExecute);
        Assert.Contains(result.Issues, issue => issue.Code == "output.replaces-source");
        Assert.Contains(result.Issues, issue => issue.Code == "output.file.exists");
    }

    private sealed class ProbeOutputEnvironment : IOutputEnvironment
    {
        public bool Writable { get; init; } = true;

        public long? AvailableBytes { get; init; } = 1_000;

        public HashSet<string> ExistingPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool CanWriteDirectory(string directory, out string? reason)
        {
            reason = Writable ? null : "read only";
            return Writable;
        }

        public long? GetAvailableBytes(string directory) => AvailableBytes;

        public bool FileExists(string path) => ExistingPaths.Contains(path);

        public bool PathsReferToSameFile(string first, string second) =>
            string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    }
}
