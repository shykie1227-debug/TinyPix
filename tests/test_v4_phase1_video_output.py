"""Phase 1 (视频输出业务页) structural contract.

Pins the first formal business page to its implementation: the video.convert and
video.compress FFmpeg handlers, the ToolHandlerRegistry resolver, the portable
FFmpeg engine provider, the JobSubmissionService wiring, the VideoOutputPage
controls, and the Engines directory on the portable workspace. Verified
cross-platform on macOS; real dotnet build + Windows runtime gate happen on Windows.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src" / "TinyPix.App"
MEDIA = ROOT / "src" / "TinyPix.Media"
CORE = ROOT / "src" / "TinyPix.Core"
INFRA = ROOT / "src" / "TinyPix.Infrastructure"


def test_video_output_handlers_are_implemented() -> None:
    base = (MEDIA / "Video" / "VideoOutputHandlerBase.cs").read_text(encoding="utf-8")
    assert "IToolHandler" in base
    assert ": IToolHandler" in base

    convert = (MEDIA / "Video" / "VideoConvertHandler.cs").read_text(encoding="utf-8")
    compress = (MEDIA / "Video" / "VideoCompressHandler.cs").read_text(encoding="utf-8")

    for source, tool_id, suffix in (
        (convert, "video.convert", "_转换"),
        (compress, "video.compress", "_压缩"),
    ):
        assert "VideoOutputHandlerBase" in source
        assert f'ToolId => "{tool_id}"' in source, tool_id
        assert f'OutputSuffix => "{suffix}"' in source, suffix


def test_media_exposes_ffmpeg_engine_provider_and_naming() -> None:
    provider = (MEDIA / "Ffmpeg" / "IFfmpegEngineProvider.cs").read_text(encoding="utf-8")
    assert "interface IFfmpegEngineProvider" in provider
    assert "FfmpegExecutablePath" in provider

    naming = (MEDIA / "Video" / "VideoOutputNaming.cs").read_text(encoding="utf-8")
    assert "BuildOutputPath" in naming
    assert "_转换" in naming
    assert "_压缩" in naming


def test_core_exposes_handler_registry() -> None:
    registry = (CORE / "Tools" / "ToolHandlerRegistry.cs").read_text(encoding="utf-8")
    assert "ToolHandlerRegistry" in registry
    assert "TryResolve" in registry
    assert "Register" in registry


def test_app_wires_engine_provider_handlers_and_submission() -> None:
    app_code = (APP / "App.xaml.cs").read_text(encoding="utf-8")
    for marker in (
        "IFfmpegEngineProvider",
        "PortableFfmpegEngineProvider",
        "ToolHandlerRegistry",
        "VideoConvertHandler",
        "VideoCompressHandler",
        "JobSubmissionService",
    ):
        assert marker in app_code, marker

    submission = (APP / "Services" / "JobSubmissionService.cs").read_text(encoding="utf-8")
    assert "SubmitAsync" in submission
    assert "OutputPreflightService" in submission
    assert "ToolHandlerRegistry" in submission


def test_video_output_page_is_wired_to_submission() -> None:
    page_code = (APP / "Pages" / "VideoOutputPage.xaml.cs").read_text(encoding="utf-8")
    for marker in (
        "JobSubmissionService",
        "AddFilesButton_Click",
        "StartOutputButton_Click",
        "video.convert",
    ):
        assert marker in page_code, marker

    page_xaml = (APP / "Pages" / "VideoOutputPage.xaml").read_text(encoding="utf-8")
    for marker in (
        "视频输出",
        "中央预览区域",
        "输出格式",
        "开始输出",
        "添加视频",
    ):
        assert marker in page_xaml, marker


def test_portable_workspace_exposes_engines_directory() -> None:
    workspace = (INFRA / "Portable" / "PortableWorkspace.cs").read_text(encoding="utf-8")
    assert "EnginesDirectory" in workspace
    assert 'UnderRoot("Engines")' in workspace
