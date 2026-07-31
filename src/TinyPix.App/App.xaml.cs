using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using TinyPix.App.Services;
using TinyPix.Core.Cache;
using TinyPix.Core.History;
using TinyPix.Core.Jobs;
using TinyPix.Core.Settings;
using TinyPix.Infrastructure.History;
using TinyPix.Infrastructure.Portable;
using TinyPix.Infrastructure.Settings;

namespace TinyPix.App;

/// <summary>
/// Composition root for the formal WinUI shell. The application is unpackaged,
/// self-contained and fully offline: every mutable path lives under the portable
/// workspace next to TinyPix.exe, gated by the portable.flag sentinel.
/// </summary>
public partial class App : Application
{
    private Window? _shell;

    public App()
    {
        InitializeComponent();
        Services = BuildServices();
    }

    internal static IServiceProvider Services { get; private set; } = default!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // One native shell window for the whole app; navigation happens inside it.
        _shell = new MainWindow();
        _shell.Activate();
    }

    private static IServiceProvider BuildServices()
    {
        string executableDirectory = AppContext.BaseDirectory;
        if (!File.Exists(Path.Combine(executableDirectory, "portable.flag")))
        {
            // portable.flag is the portable/offline boundary. Without it the shell
            // refuses to start instead of risking writes outside the release folder.
            throw new InvalidOperationException(
                "缺少 portable.flag，TinyPix 无法确认便携数据目录。请重新解压完整发行包。");
        }

        PortableWorkspace workspace = PortableWorkspace.Open(executableDirectory);

        var services = new ServiceCollection();
        services.AddSingleton(workspace);
        services.AddSingleton<JobQueueService>();
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(
            workspace.SettingsPath,
            TinyPixSettings.Default(Path.Combine(workspace.RootDirectory, "Output"))));
        services.AddSingleton<IHistoryRepository>(_ => new SqliteHistoryRepository(
            workspace.DatabasePath));
        services.AddSingleton<ICacheService>(_ => new LocalFileCacheService(
            workspace.CacheDirectory));

        return services.BuildServiceProvider();
    }
}
