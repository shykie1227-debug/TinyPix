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
        string[] inputs = inputPaths?.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray()
            ?? throw new ArgumentNullException(nameof(inputPaths));
        if (inputs.Length == 0)
        {
            throw new ArgumentException("At least one input path is required.", nameof(inputPaths));
        }

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
