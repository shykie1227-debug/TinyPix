"""Phase 2 (GIF 制作业务页) structural contract.

Pins the GIF maker business page to its implementation: the video.gif two-pass
FFmpeg handler (palettegen → paletteuse), the ToolHandlerRegistry resolver, the
portable FFmpeg engine provider, the JobSubmissionService wiring, the GifMakerPage
controls, and the Engines directory on the portable workspace. Verified
cross-platform on macOS; real dotnet build + Windows runtime gate happen on Windows.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src" / "TinyPix.App"
MEDIA = ROOT / "src" / "TinyPix.Media"
CORE = ROOT / "src" / "TinyPix.Core"
INFRA = ROOT / "src" / "TinyPix.Infrastructure"


def test_gif_handler_is_implemented() -> None:
    base = (MEDIA / "Video" / "VideoOutputHandlerBase.cs").read_text(encoding="utf-8")
    assert "IToolHandler" in base
    assert ": IToolHandler" in base

    gif = (MEDIA / "Video" / "GifMakerHandler.cs").read_text(encoding="utf-8")
    assert "VideoOutputHandlerBase" in gif
    assert 'ToolId => "video.gif"' in gif, "video.gif"
    assert 'OutputSuffix => "_GIF"' in gif, "_GIF"
    # two-pass palette pipeline markers
    assert "palettegen" in gif
    assert "paletteuse" in gif


def test_naming_supports_gif() -> None:
    naming = (MEDIA / "Video" / "VideoOutputNaming.cs").read_text(encoding="utf-8")
    assert "BuildOutputPath" in naming
    assert '"gif"' in naming
    assert "_GIF" in naming


def test_core_exposes_handler_registry() -> None:
    registry = (CORE / "Tools" / "ToolHandlerRegistry.cs").read_text(encoding="utf-8")
    assert "ToolHandlerRegistry" in registry
    assert "TryResolve" in registry
    assert "Register" in registry


def test_app_wires_gif_handler_and_submission() -> None:
    app_code = (APP / "App.xaml.cs").read_text(encoding="utf-8")
    for marker in (
        "IFfmpegEngineProvider",
        "PortableFfmpegEngineProvider",
        "ToolHandlerRegistry",
        "GifMakerHandler",
        "JobSubmissionService",
    ):
        assert marker in app_code, marker

    submission = (APP / "Services" / "JobSubmissionService.cs").read_text(encoding="utf-8")
    assert "SubmitAsync" in submission
    assert "OutputPreflightService" in submission
    assert "ToolHandlerRegistry" in submission


def test_gif_page_is_wired_to_submission() -> None:
    page_code = (APP / "Pages" / "GifMakerPage.xaml.cs").read_text(encoding="utf-8")
    for marker in (
        "JobSubmissionService",
        "AddFilesAsync",
        "StartOutputButton_Click",
        "video.gif",
    ):
        assert marker in page_code, marker

    page_xaml = (APP / "Pages" / "GifMakerPage.xaml").read_text(encoding="utf-8")
    for marker in (
        "GIF 制作",
        "PreviewStageControl",
        "GIF 参数",
        "生成 GIF",
    ):
        assert marker in page_xaml, marker


def test_portable_workspace_exposes_engines_directory() -> None:
    workspace = (INFRA / "Portable" / "PortableWorkspace.cs").read_text(encoding="utf-8")
    assert "EnginesDirectory" in workspace
    assert 'UnderRoot("Engines")' in workspace
