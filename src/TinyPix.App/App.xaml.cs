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
using TinyPix.Capture;
using TinyPix.Media.Capture;
using TinyPix.Media.Ffmpeg;
using TinyPix.Media.Images;
using TinyPix.Media.Pdf;
using TinyPix.Media.Tools;
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
        // Safety net for the Windows App SDK bootstrap: make sure
        // MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY points at the directory that
        // actually holds the framework DLLs before any XAML DLL loads through the
        // SxS redirector. On .NET 10 single-file the SDK auto-initializer already
        // gets this right (BaseDirectory == extraction dir, verified 2026-10-19),
        // so this is normally a no-op; it self-corrects if that ever changes.
        // See WindowsAppRuntimeBaseDirectory. Runs before InitializeComponent().
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
        StartupTrace.Write("BuildServices enter");
        try
        {
            Services = BuildServices();
        }
        catch (Exception ex)
        {
            // A startup failure thrown from this ctor escapes through the WinRT/COM
            // ABI boundary and becomes a bare 0xC0000005 (no managed stack anywhere),
            // so always log the full exception first - see AppPaths for the details.
            StartupTrace.Write($"BuildServices FAILED: {ex}");
            throw;
        }

        StartupTrace.Write("BuildServices done");

        // Last-resort diagnostics: a XAML/WinRT failure raised outside managed
        // code surfaces as a bare 0xC000027B, so record whatever managed detail
        // the framework still hands us before the process goes down.
        UnhandledException += (_, e) =>
        {
            StartupTrace.Write(
                $"UNHANDLED hr=0x{e.Exception?.HResult:X8} msg={e.Exception?.Message}: {e.Exception}");
        };
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
        // MUST be AppPaths.ExecutableDirectory, NOT AppContext.BaseDirectory: in the
        // single-file publish BaseDirectory is the .NET bundle extraction directory
        // (%TEMP%\.net\TinyPix\<hash>\) and portable.flag lives next to the exe.
        // See AppPaths - the wrong choice crashes the process with 0xC0000005.
        string executableDirectory = AppPaths.ExecutableDirectory;
        StartupTrace.Write($"BuildServices: exe dir = {executableDirectory}");
        if (!File.Exists(Path.Combine(executableDirectory, "portable.flag")))
        {
            // portable.flag is the portable/offline boundary. Without it the shell
            // refuses to start instead of risking writes outside the release folder.
            throw new InvalidOperationException(
                "缺少 portable.flag，TinyPix 无法确认便携数据目录。请重新解压完整发行包。");
        }

        PortableWorkspace workspace = PortableWorkspace.Open(executableDirectory);
        StartupTrace.Write("BuildServices: workspace ok");

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

        // Phase 4 工具箱: 二维码/条码 (E9 ZXing.Net + E3 Magick.NET).
        services.AddSingleton<CodeGenerateHandler>();
        services.AddSingleton<CodeScanHandler>();

        // Phase 4 工具箱: 文件工具（file.rename / file.hash / file.metadata）。
        // file.metadata 的 E2 分支复用同一 ffprobe 引擎（BtbN 包同目录）。
        services.AddSingleton<FileRenameHandler>();
        services.AddSingleton<FileHashHandler>();
        services.AddSingleton<FileMetadataHandler>();

        // Phase 4 工具箱: 屏幕录制 (E11 Windows Graphics Capture + E1 Media
        // Foundation)。引擎在 TinyPix.Capture（Windows 专用），Media 只见抽象。
        services.AddSingleton<IScreenCaptureEngine, WindowsScreenCaptureEngine>();
        services.AddSingleton<CaptureRecordHandler>();

        // Phase 5 PDF 页面管理 (E7 + E8: PDFtoImage/PDFium + PDFsharp/PdfPig)
        services.AddSingleton<PdfMergeHandler>();
        services.AddSingleton<PdfSplitHandler>();
        services.AddSingleton<PdfReorderHandler>();
        services.AddSingleton<PdfRotateHandler>();
        services.AddSingleton<PdfWatermarkHandler>();
        services.AddSingleton<PdfToImageHandler>();
        services.AddSingleton<PdfFromImageHandler>();
        services.AddSingleton<PdfCompressHandler>();

        // Phase 5 智能证件照 (E3 + E4 YuNet + E5 MODNet)
        services.AddSingleton<TinyPix.Media.IdPhoto.IdPhotoCreateHandler>();
        services.AddSingleton<TinyPix.Media.IdPhoto.IdPhotoCheckHandler>();
        services.AddSingleton<TinyPix.Media.IdPhoto.IdPhotoPrintHandler>();

        // Output preflight (writability / disk space / source-overwrite) consumed by
        // JobSubmissionService. LocalOutputEnvironment has an optional probe, so it
        // can be constructed directly.
        services.AddSingleton<IOutputEnvironment>(_ => new LocalOutputEnvironment());
        services.AddSingleton<OutputPreflightService>();

        IServiceProvider provider = services.BuildServiceProvider();
        StartupTrace.Write("BuildServices: provider built");

        // Phase 1 (视频输出) + Phase 2 (GIF 制作) + Phase 3 (视频编辑): register the
        // media handlers implemented so far so the job queue can resolve them by
        // their frozen ToolCatalog ids.
        var registry = provider.GetRequiredService<ToolHandlerRegistry>();
        Register(registry, provider, typeof(VideoConvertHandler));
        Register(registry, provider, typeof(VideoCompressHandler));
        Register(registry, provider, typeof(GifMakerHandler));
        Register(registry, provider, typeof(VideoTrimHandler));
        Register(registry, provider, typeof(VideoSplitHandler));
        Register(registry, provider, typeof(VideoMergeHandler));
        Register(registry, provider, typeof(VideoGeometryHandler));
        Register(registry, provider, typeof(VideoSpeedVolumeHandler));
        Register(registry, provider, typeof(VideoAudioHandler));
        Register(registry, provider, typeof(VideoOverlayHandler));
        Register(registry, provider, typeof(VideoFramesHandler));
        Register(registry, provider, typeof(VideoMetadataHandler));

        // 图片工作台: same frozen ToolCatalog ids as the video handlers above.
        Register(registry, provider, typeof(ImageResizeHandler));
        Register(registry, provider, typeof(ImageCropHandler));
        Register(registry, provider, typeof(ImageTransformHandler));
        Register(registry, provider, typeof(ImageConvertHandler));
        Register(registry, provider, typeof(ImageCompressHandler));
        Register(registry, provider, typeof(ImageColorHandler));
        Register(registry, provider, typeof(ImageWatermarkHandler));
        Register(registry, provider, typeof(ImageExifHandler));
        Register(registry, provider, typeof(ImageCollageHandler));
        Register(registry, provider, typeof(ImageBatchHandler));
        Register(registry, provider, typeof(ImageBackgroundHandler));
        Register(registry, provider, typeof(CodeGenerateHandler));
        Register(registry, provider, typeof(CodeScanHandler));
        Register(registry, provider, typeof(FileRenameHandler));
        Register(registry, provider, typeof(FileHashHandler));
        Register(registry, provider, typeof(FileMetadataHandler));
        Register(registry, provider, typeof(CaptureRecordHandler));

        // Phase 5 PDF 页面管理: pdf.merge/split/reorder/rotate/watermark/to-image/from-image/compress
        Register(registry, provider, typeof(PdfMergeHandler));
        Register(registry, provider, typeof(PdfSplitHandler));
        Register(registry, provider, typeof(PdfReorderHandler));
        Register(registry, provider, typeof(PdfRotateHandler));
        Register(registry, provider, typeof(PdfWatermarkHandler));
        Register(registry, provider, typeof(PdfToImageHandler));
        Register(registry, provider, typeof(PdfFromImageHandler));
        Register(registry, provider, typeof(PdfCompressHandler));

        // Phase 5 智能证件照
        Register(registry, provider, typeof(TinyPix.Media.IdPhoto.IdPhotoCreateHandler));
        Register(registry, provider, typeof(TinyPix.Media.IdPhoto.IdPhotoCheckHandler));
        Register(registry, provider, typeof(TinyPix.Media.IdPhoto.IdPhotoPrintHandler));

        StartupTrace.Write("BuildServices: handlers registered");
        return provider;
    }

    /// <summary>
    /// Resolves one handler with per-type tracing: the single-file probe reads the
    /// last line of the startup log to see exactly which registration crashed the
    /// process (a native access violation is not catchable in managed code).
    /// </summary>
    private static void Register(
        ToolHandlerRegistry registry, IServiceProvider provider, Type handlerType)
    {
        StartupTrace.Write($"BuildServices: resolving {handlerType.Name}");
        IToolHandler handler = (IToolHandler)provider.GetRequiredService(handlerType);
        registry.Register(handler);
        StartupTrace.Write($"BuildServices: registered {handlerType.Name}");
    }
}
