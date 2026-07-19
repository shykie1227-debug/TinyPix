namespace TinyPix.Core.Storage;

public interface IOutputEnvironment
{
    bool CanWriteDirectory(string directory, out string? reason);

    long? GetAvailableBytes(string directory);

    bool FileExists(string path);

    bool PathsReferToSameFile(string first, string second);
}

public sealed record OutputPreflightRequest(
    IReadOnlyList<string> InputPaths,
    IReadOnlyList<string> CandidateOutputPaths,
    string OutputDirectory,
    long EstimatedOutputBytes);

public sealed record OutputPreflightIssue(
    string Code,
    string Message,
    bool IsBlocking,
    string? Path = null);

public sealed record OutputPreflightResult(IReadOnlyList<OutputPreflightIssue> Issues)
{
    public bool CanExecute => Issues.All(issue => !issue.IsBlocking);
}

public sealed class OutputPreflightService(IOutputEnvironment environment)
{
    public OutputPreflightResult Evaluate(OutputPreflightRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var issues = new List<OutputPreflightIssue>();

        if (!environment.CanWriteDirectory(request.OutputDirectory, out string? reason))
        {
            issues.Add(new OutputPreflightIssue(
                "output.directory.not-writable",
                $"输出目录不可写：{reason}",
                true,
                request.OutputDirectory));
        }

        long? availableBytes = environment.GetAvailableBytes(request.OutputDirectory);
        if (availableBytes is not null &&
            request.EstimatedOutputBytes > 0 &&
            availableBytes < request.EstimatedOutputBytes)
        {
            issues.Add(new OutputPreflightIssue(
                "output.disk.insufficient",
                $"磁盘可用空间不足。预计需要 {request.EstimatedOutputBytes} 字节，可用 {availableBytes} 字节。",
                true,
                request.OutputDirectory));
        }

        foreach (string outputPath in request.CandidateOutputPaths)
        {
            if (request.InputPaths.Any(input => environment.PathsReferToSameFile(input, outputPath)))
            {
                issues.Add(new OutputPreflightIssue(
                    "output.replaces-source",
                    "输出路径与原文件相同。TinyPix 不允许覆盖原文件。",
                    true,
                    outputPath));
                continue;
            }

            if (environment.FileExists(outputPath))
            {
                issues.Add(new OutputPreflightIssue(
                    "output.file.exists",
                    "输出文件已存在，请在执行前选择新名称或明确处理冲突。",
                    true,
                    outputPath));
            }
        }

        return new OutputPreflightResult(issues.AsReadOnly());
    }
}
