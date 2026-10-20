namespace TinyPix.Core.Jobs;

public enum JobWorkload
{
    HeavyMedia,
    LightFile,
}

public enum JobStatus
{
    Draft,
    Queued,
    Running,
    Cancelling,
    Succeeded,
    Failed,
    PartialFailure,
    Interrupted,
    Cancelled,
}

public sealed record JobError(string Code, string Message, bool CanRetry);

public sealed record JobProgress(double Percentage, string Message)
{
    public double Percentage { get; init; } = Math.Clamp(Percentage, 0, 100);
}

public sealed record JobRequest(
    Guid Id,
    string ToolId,
    IReadOnlyList<string> InputPaths,
    string OutputDirectory,
    IReadOnlyDictionary<string, string> Parameters,
    JobWorkload Workload)
{
    public static JobRequest Create(
        string toolId,
        IEnumerable<string> inputPaths,
        string outputDirectory,
        JobWorkload workload,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        // NOTE: file-based tools validate their own input requirements (every handler
        // rejects an empty list with its own error code). Manual-input tools
        // (code.generate / color.convert) legitimately have zero file inputs - their
        // "input" is text carried in Parameters - so this factory must not reject
        // empty input lists. All consumers of InputPaths are empty-safe.
        string[] inputs = inputPaths?.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray()
            ?? throw new ArgumentNullException(nameof(inputPaths));

        return new JobRequest(
            Guid.NewGuid(),
            toolId,
            Array.AsReadOnly(inputs),
            outputDirectory,
            parameters ?? new Dictionary<string, string>(),
            workload);
    }

    public JobRequest CreateRetry() => this with { Id = Guid.NewGuid() };
}
