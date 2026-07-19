namespace TinyPix.Core.Preview;

public enum PreviewKind
{
    Image,
    VideoThumbnail,
    VideoWaveform,
    PdfPage,
    UnsupportedFallback,
}

public sealed record PreviewRequest(
    string SourcePath,
    PreviewKind Kind,
    int MaximumWidth,
    int MaximumHeight,
    int? PageNumber = null,
    TimeSpan? Position = null);

public sealed record PreviewArtifact(
    string CachePath,
    string MediaType,
    int Width,
    int Height,
    string SourceFingerprint);

public interface IMediaPreviewService
{
    Task<PreviewArtifact> CreateAsync(
        PreviewRequest request,
        CancellationToken cancellationToken = default);
}
