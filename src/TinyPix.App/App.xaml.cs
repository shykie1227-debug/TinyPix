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
using TinyPix.Media.Ffmpeg;
using TinyPix.Media.Images;
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
        // MUST run before InitializeComponent(): the Windows App SDK auto-initializer
        // (module initializer injected into this assembly) unconditionally sets
        // MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY to AppContext.BaseDirectory, which in
        // single-file mode is the exe directory while the WinUI framework DLLs actually live
        // in the .NET bundle extraction directory. Re-point the variable before any XAML DLL
        // gets loaded through the SxS redirector. No-op in multi-file mode (DLLs next to exe).
        StartupTrace.Write("App ctor enter");
        StartupTrace.WriteEnvironment("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY");
        StartupTrace.WriteModule("Microsoft.WindowsAppRuntime.dll");
        WindowsAppRuntimeBaseDirectory.EnsureCorrectForSingleFile();
        StartupTrace.WriteEnvironment("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY");

        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            StartupTrace.Write($"InitializeComponent FAILED: {ex}");
            StartupTrace.WriteModule("Microsoft.UI.Xaml.dll");
            throw;
        }

        StartupTrace.Write("InitializeComponent ok");
        Services = BuildServices();
    }

    internal static IServiceProvider Services { get; private set; } = default!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // One native shell window for the whole app; navigation happens inside it.
        StartupTrace.Write("OnLaunched enter");
        _shell = new MainWindow();
        _shell.Activate();
        StartupTrace.Write("OnLaunched done");
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

        // Phase 3 图片工作台 (11 tools, FEATURE-MATRIX §4).
        services.AddSingleton<ImageResizeHandler>();
        services.AddSingleton<ImageCropHandler>();
        services.AddSingleton<ImageTransformHandler>();
        services.AddSingleton<ImageConvertHandler>();
        services.AddSingleton<ImageCompressHandler>();
        services.AddSingleton<ImageColorHandler>();
        services.AddSingleton<ImageWatermarkHandler>();
        services.AddSingleton<ImageExifHandler>();
        services.AddSingleton<ImageCollageHandler>();
        services.AddSingleton<ImageBatchHandler>();
        services.AddSingleton<ImageBackgroundHandler>();

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

        // 图片工作台: same frozen ToolCatalog ids as the video handlers above.
        registry.Register(provider.GetRequiredService<ImageResizeHandler>());
        registry.Register(provider.GetRequiredService<ImageCropHandler>());
        registry.Register(provider.GetRequiredService<ImageTransformHandler>());
        registry.Register(provider.GetRequiredService<ImageConvertHandler>());
        registry.Register(provider.GetRequiredService<ImageCompressHandler>());
        registry.Register(provider.GetRequiredService<ImageColorHandler>());
        registry.Register(provider.GetRequiredService<ImageWatermarkHandler>());
        registry.Register(provider.GetRequiredService<ImageExifHandler>());
        registry.Register(provider.GetRequiredService<ImageCollageHandler>());
        registry.Register(provider.GetRequiredService<ImageBatchHandler>());
        registry.Register(provider.GetRequiredService<ImageBackgroundHandler>());

        return provider;
    }
}
