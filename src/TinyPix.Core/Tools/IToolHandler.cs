using TinyPix.Core.Jobs;

namespace TinyPix.Core.Tools;

public sealed record ToolValidationResult(IReadOnlyList<JobError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static ToolValidationResult Valid() => new([]);

    public static ToolValidationResult Invalid(params JobError[] errors) => new(errors);
}

public sealed record ToolEstimate(TimeSpan? Duration, long? OutputBytes);

public sealed record JobExecutionContext(JobRequest Request, IProgress<JobProgress> Progress);

public sealed record ToolExecutionResult(
    IReadOnlyList<string> OutputPaths,
    JobError? PartialFailure)
{
    public static ToolExecutionResult Success(IReadOnlyList<string> outputPaths) =>
        new(outputPaths, null);

    public static ToolExecutionResult Partial(
        IReadOnlyList<string> outputPaths,
        JobError error) => new(outputPaths, error);
}

public interface IToolHandler
{
    string ToolId { get; }

    ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request,
        CancellationToken cancellationToken);

    ValueTask<ToolEstimate> EstimateAsync(
        JobRequest request,
        CancellationToken cancellationToken);

    Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken);
}
