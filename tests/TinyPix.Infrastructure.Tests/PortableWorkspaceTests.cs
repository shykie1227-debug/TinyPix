using TinyPix.Infrastructure.Portable;

namespace TinyPix.Infrastructure.Tests;

public sealed class PortableWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"tinypix-portable-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Portable_flag_is_mandatory()
    {
        Directory.CreateDirectory(_root);

        PortableModeException error = Assert.Throws<PortableModeException>(
            () => PortableWorkspace.Open(_root));

        Assert.Equal("portable.flag.missing", error.Code);
    }

    [Fact]
    public void Mutable_paths_are_created_beneath_the_executable_directory()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "portable.flag"), string.Empty);

        PortableWorkspace workspace = PortableWorkspace.Open(_root);

        Assert.All(
            workspace.MutableDirectories,
            path => Assert.StartsWith(Path.GetFullPath(_root), path, StringComparison.Ordinal));
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "Config", "settings.json"), workspace.SettingsPath);
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "Data", "tinypix.db"), workspace.DatabasePath);
        Assert.All(workspace.MutableDirectories, path => Assert.True(Directory.Exists(path)));
    }

    [Fact]
    public void Unwritable_portable_directory_blocks_startup_tasks()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "portable.flag"), string.Empty);

        PortableModeException error = Assert.Throws<PortableModeException>(
            () => PortableWorkspace.Open(_root, new RejectingWriteProbe()));

        Assert.Equal("portable.directory.not-writable", error.Code);
        Assert.Contains("移动", error.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class RejectingWriteProbe : IWritableDirectoryProbe
    {
        public bool CanWrite(string directory, out string? reason)
        {
            reason = "read only";
            return false;
        }
    }
}
