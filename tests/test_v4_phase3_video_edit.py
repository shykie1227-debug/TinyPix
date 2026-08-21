"""阶段3 结构契约：剩余 9 个视频编辑工具（video.trim / split / merge / geometry /
speed-volume / audio / overlay / frames / metadata）。

钉住：每个 handler 已实现并注册、VideoEditHandlerBase 存在、VideoEditorPage 与
顶部 video-edit 导航接线。仅依赖纯 assert，本机用系统 python3 真跑。
"""

import os
import re

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MEDIA = os.path.join(REPO, "src", "TinyPix.Media", "Video")
APP = os.path.join(REPO, "src", "TinyPix.App")
APP_SERVICES = os.path.join(APP, "Services")
APP_PAGES = os.path.join(APP, "Pages")

EXPECTED = [
    ("VideoTrimHandler", "video.trim"),
    ("VideoSplitHandler", "video.split"),
    ("VideoMergeHandler", "video.merge"),
    ("VideoGeometryHandler", "video.geometry"),
    ("VideoSpeedVolumeHandler", "video.speed-volume"),
    ("VideoAudioHandler", "video.audio"),
    ("VideoOverlayHandler", "video.overlay"),
    ("VideoFramesHandler", "video.frames"),
    ("VideoMetadataHandler", "video.metadata"),
]


def _read(path):
    with open(path, "r", encoding="utf-8") as fh:
        return fh.read()


def test_video_edit_handler_base_exists():
    path = os.path.join(MEDIA, "VideoEditHandlerBase.cs")
    assert os.path.isfile(path), f"缺少 VideoEditHandlerBase.cs: {path}"
    src = _read(path)
    assert "abstract class VideoEditHandlerBase" in src
    assert "BuildJobs" in src
    assert "IToolHandler" in src


def test_video_edit_handlers_are_implemented():
    for class_name, tool_id in EXPECTED:
        path = os.path.join(MEDIA, f"{class_name}.cs")
        assert os.path.isfile(path), f"缺少 handler 文件: {path}"
        src = _read(path)
        assert f"class {class_name}" in src, f"{class_name} 未定义类"
        assert f'ToolId => "{tool_id}"' in src, f"{class_name} 未声明 ToolId={tool_id}"
        assert "VideoEditHandlerBase" in src, f"{class_name} 未继承 VideoEditHandlerBase"


def test_app_registers_video_edit_handlers():
    path = os.path.join(APP, "App.xaml.cs")
    assert os.path.isfile(path), f"缺少 App.xaml.cs: {path}"
    src = _read(path)
    for class_name, _ in EXPECTED:
        needle = f"registry.Register(provider.GetRequiredService<{class_name}>());"
        assert needle in src, f"App 未注册 {class_name}"


def test_video_editor_page_exists_and_wired():
    xaml = os.path.join(APP_PAGES, "VideoEditorPage.xaml")
    code = os.path.join(APP_PAGES, "VideoEditorPage.xaml.cs")
    assert os.path.isfile(xaml), f"缺少 VideoEditorPage.xaml: {xaml}"
    assert os.path.isfile(code), f"缺少 VideoEditorPage.xaml.cs: {code}"
    code_src = _read(code)
    assert "VideoEditorPage" in code_src
    assert "JobSubmissionService" in code_src
    # 工具选择项须覆盖 9 个 id
    xaml_src = _read(xaml)
    for _, tool_id in EXPECTED:
        assert f'Tag="{tool_id}"' in xaml_src, f"页面未含工具项 {tool_id}"


def test_main_window_navigates_to_video_editor():
    xaml = os.path.join(APP, "MainWindow.xaml")
    code = os.path.join(APP, "MainWindow.xaml.cs")
    assert os.path.isfile(xaml) and os.path.isfile(code)
    xaml_src = _read(xaml)
    code_src = _read(code)
    assert 'Tag="video-edit"' in xaml_src, "MainWindow 未加 video-edit 导航项"
    assert "VideoEditorPage" in code_src, "MainWindow 未导航到 VideoEditorPage"
    assert 'string.Equals(tag, "video-edit"' in code_src


def test_job_submission_preview_supports_edit_handlers():
    path = os.path.join(APP_SERVICES, "JobSubmissionService.cs")
    assert os.path.isfile(path), f"缺少 JobSubmissionService.cs: {path}"
    src = _read(path)
    assert "VideoEditHandlerBase" in src, "JobSubmissionService 未处理 VideoEditHandlerBase 预览"
    assert "editHandler.PreviewOutputPaths" in src
