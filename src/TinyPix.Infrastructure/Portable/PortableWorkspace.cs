namespace TinyPix.Infrastructure.Portable;

public sealed class PortableModeException(string code, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}

public interface IWritableDirectoryProbe
{
    bool CanWrite(string directory, out string? reason);
}

public sealed class FileSystemWritableDirectoryProbe : IWritableDirectoryProbe
{
    public bool CanWrite(string directory, out string? reason)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string probePath = Path.Combine(directory, $".tinypix-write-{Guid.NewGuid():N}.tmp");
            using (new FileStream(
                       probePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 1,
                       FileOptions.DeleteOnClose))
            {
            }

            reason = null;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            reason = exception.Message;
            return false;
        }
    }
}

public sealed class PortableWorkspace
{
    private PortableWorkspace(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        ConfigDirectory = UnderRoot("Config");
        DataDirectory = UnderRoot("Data");
        CacheDirectory = UnderRoot("Cache");
        LogsDirectory = UnderRoot("Logs");
        SettingsPath = Path.Combine(ConfigDirectory, "settings.json");
        DatabasePath = Path.Combine(DataDirectory, "tinypix.db");
        MutableDirectories =
        [
            ConfigDirectory,
            DataDirectory,
            CacheDirectory,
            LogsDirectory,
        ];
    }

    public string RootDirectory { get; }

    public string ConfigDirectory { get; }

    public string DataDirectory { get; }

    public string CacheDirectory { get; }

    public string LogsDirectory { get; }

    public string SettingsPath { get; }

    public string DatabasePath { get; }

    public IReadOnlyList<string> MutableDirectories { get; }

    public static PortableWorkspace Open(
        string executableDirectory,
        IWritableDirectoryProbe? writeProbe = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        string root = Path.GetFullPath(executableDirectory);
        if (!File.Exists(Path.Combine(root, "portable.flag")))
        {
            throw new PortableModeException(
                "portable.flag.missing",
                "缺少 portable.flag，TinyPix 无法确认便携数据目录。请重新解压完整发行包。");
        }

        var workspace = new PortableWorkspace(root);
        writeProbe ??= new FileSystemWritableDirectoryProbe();
        foreach (string directory in workspace.MutableDirectories)
        {
            if (!writeProbe.CanWrite(directory, out string? reason))
            {
                throw new PortableModeException(
                    "portable.directory.not-writable",
                    $"TinyPix 所在目录不可写，处理任务已阻止。请将软件移动到普通用户可写目录后重试。目录：{directory}。原因：{reason}");
            }
        }

        return workspace;
    }

    private string UnderRoot(string name)
    {
        string path = Path.GetFullPath(Path.Combine(RootDirectory, name));
        string rootPrefix = RootDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? RootDirectory
            : RootDirectory + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new PortableModeException("portable.path.escape", "便携数据路径超出 TinyPix 根目录。");
        }

        return path;
    }
}
