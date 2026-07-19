using TinyPix.Infrastructure.Storage;

namespace TinyPix.Infrastructure.Tests;

public sealed class LocalOutputEnvironmentTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"tinypix-output-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Source_path_is_never_accepted_as_an_output_path()
    {
        Directory.CreateDirectory(_root);
        string source = Path.Combine(_root, "Original.MP4");
        File.WriteAllText(source, "source");
        var environment = new LocalOutputEnvironment();

        Assert.True(environment.PathsReferToSameFile(source, source.ToLowerInvariant()));
    }

    [Fact]
    public void Existing_output_is_reported_before_execution()
    {
        Directory.CreateDirectory(_root);
        string output = Path.Combine(_root, "result.mp4");
        File.WriteAllText(output, "existing");
        var environment = new LocalOutputEnvironment();

        Assert.True(environment.FileExists(output));
        Assert.True(environment.CanWriteDirectory(_root, out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
