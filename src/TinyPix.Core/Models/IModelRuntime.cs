namespace TinyPix.Core.Models;

public sealed record ModelDescriptor(
    string Id,
    string Version,
    string FileName,
    string Sha256,
    string LicenseId);

public sealed record ModelValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? Message);

public sealed record ModelRequest(
    string ModelId,
    IReadOnlyList<string> InputPaths,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record ModelResult(
    IReadOnlyList<string> ArtifactPaths,
    IReadOnlyDictionary<string, string> Metadata);

public interface IModelRuntime
{
    Task<ModelValidationResult> ValidateAsync(
        ModelDescriptor model,
        CancellationToken cancellationToken = default);

    Task<ModelResult> RunAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default);
}
