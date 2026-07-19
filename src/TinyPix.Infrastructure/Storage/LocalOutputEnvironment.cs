using TinyPix.Core.Storage;
using TinyPix.Infrastructure.Portable;

namespace TinyPix.Infrastructure.Storage;

public sealed class LocalOutputEnvironment(
    IWritableDirectoryProbe? writeProbe = null) : IOutputEnvironment
{
    private readonly IWritableDirectoryProbe _writeProbe =
        writeProbe ?? new FileSystemWritableDirectoryProbe();

    public bool FileExists(string path) => File.Exists(Path.GetFullPath(path));

    public bool CanWriteDirectory(string directory, out string? reason) =>
        _writeProbe.CanWrite(Path.GetFullPath(directory), out reason);

    public long? GetAvailableBytes(string directory)
    {
        try
        {
            string fullPath = Path.GetFullPath(directory);
            string? root = Path.GetPathRoot(fullPath);
            return string.IsNullOrWhiteSpace(root)
                ? null
                : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public bool PathsReferToSameFile(string first, string second) =>
        string.Equals(
            Normalize(first),
            Normalize(second),
            StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }
}
