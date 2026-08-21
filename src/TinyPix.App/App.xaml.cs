using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using TinyPix.App.Services;
using TinyPix.Core.Cache;
using TinyPix.Core.History;
using TinyPix.Core.Jobs;
using TinyPix.Core.Settings;
using TinyPix.Core.Storage;
using TinyPix.Core.Tools;
using TinyPix.Media.Video;
using TinyPix.Infrastructure.History;
using TinyPix.Infrastructure.Portable;
using TinyPix.Infrastructure.Settings;
using TinyPix.Infrastructure.Storage;

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

        services.AddSingleton<IFfmpegEngineProvider>(_ => new PortableFfmpegEngineProvider(workspace));
        services.AddSingleton<ToolHandlerRegistry>();
        services.AddSingleton<JobSubmissionService>();
        services.AddSingleton<VideoConvertHandler>();
        services.AddSingleton<VideoCompressHandler>();
        services.AddSingleton<GifMakerHandler>();
        services.AddSingleton<VideoTrimHandler>();
        services.AddSingleton<VideoSplitHandler>();
        services.AddSingleton<VideoMergeHandler>();
        services.AddSingleton<VideoGeometryHandler>();
        services.AddSingleton<VideoSpeedVolumeHandler>();
        services.AddSingleton<VideoAudioHandler>();
        services.AddSingleton<VideoOverlayHandler>();
        services.AddSingleton<VideoFramesHandler>();
        services.AddSingleton<VideoMetadataHandler>();

        // Output preflight (writability / disk space / source-overwrite) consumed by
        // JobSubmissionService. LocalOutputEnvironment has an optional probe, so it
        // can be constructed directly.
        services.AddSingleton<IOutputEnvironment>(_ => new LocalOutputEnvironment());
        services.AddSingleton<OutputPreflightService>();

        IServiceProvider provider = services.BuildServiceProvider();

        // Phase 1 (视频输出) + Phase 2 (GIF 制作) + Phase 3 (视频编辑): register the
        // media handlers implemented so far so the job queue can resolve them by
        // their frozen ToolCatalog ids.
        var registry = provider.GetRequiredService<ToolHandlerRegistry>();
        registry.Register(provider.GetRequiredService<VideoConvertHandler>());
        registry.Register(provider.GetRequiredService<VideoCompressHandler>());
        registry.Register(provider.GetRequiredService<GifMakerHandler>());
        registry.Register(provider.GetRequiredService<VideoTrimHandler>());
        registry.Register(provider.GetRequiredService<VideoSplitHandler>());
        registry.Register(provider.GetRequiredService<VideoMergeHandler>());
        registry.Register(provider.GetRequiredService<VideoGeometryHandler>());
        registry.Register(provider.GetRequiredService<VideoSpeedVolumeHandler>());
        registry.Register(provider.GetRequiredService<VideoAudioHandler>());
        registry.Register(provider.GetRequiredService<VideoOverlayHandler>());
        registry.Register(provider.GetRequiredService<VideoFramesHandler>());
        registry.Register(provider.GetRequiredService<VideoMetadataHandler>());

        return provider;
    }
}
