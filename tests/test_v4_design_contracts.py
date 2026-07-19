from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
UI_SPEC = (ROOT / "design" / "UI-SPEC.md").read_text(encoding="utf-8")
CONTROL_MAPPING = (ROOT / "design" / "WINUI-CONTROL-MAPPING.md").read_text(
    encoding="utf-8"
)
FEATURE_MATRIX = (ROOT / "design" / "FEATURE-MATRIX.md").read_text(
    encoding="utf-8"
)
QUALITY_GATE = (ROOT / "design" / "UI-QUALITY-GATE.md").read_text(encoding="utf-8")


def test_task_queue_shortcut_is_ctrl_j_everywhere() -> None:
    combined = UI_SPEC + CONTROL_MAPPING

    assert "Ctrl+J" in UI_SPEC
    assert "Ctrl+J" in CONTROL_MAPPING
    assert "Alt+Q" not in combined


def test_f6_cycles_all_five_shell_regions() -> None:
    assert "顶部、左侧、中央、右侧、底部" in UI_SPEC
    assert "顶部导航、左文件栏、中央区、右参数栏、任务队列" in CONTROL_MAPPING


def test_primary_button_uses_semantic_inverse_foreground() -> None:
    primary_row = next(
        line
        for line in CONTROL_MAPPING.splitlines()
        if "`Button/Primary`" in line and line.startswith("|")
    )

    assert "on-action-primary" in primary_row
    assert "action-primary" in primary_row
    assert "黑底白字" not in primary_row


def test_theme_policy_keeps_dark_and_high_contrast_distinct() -> None:
    assert "普通深色主题" in UI_SPEC
    assert "系统 `Highlight` / `HighlightText`" in UI_SPEC


def test_interactive_boundaries_use_a_three_to_one_semantic_token() -> None:
    design = (ROOT / "DESIGN.md").read_text(encoding="utf-8")
    combined = design + UI_SPEC + CONTROL_MAPPING

    assert "control-border" in design
    assert "#8A8A8F" in design
    assert "#6E6E73" in design
    assert "control-border" in CONTROL_MAPPING
    assert "1.52:1" in combined
    assert "1.86:1" in combined


def test_media_stage_timecodes_use_a_dedicated_readable_foreground() -> None:
    design = (ROOT / "DESIGN.md").read_text(encoding="utf-8")

    assert "on-media" in design
    assert "on-media" in UI_SPEC
    assert "on-media" in CONTROL_MAPPING


def test_video_time_trim_uses_a_single_track_keyboard_operable_timeline() -> None:
    combined = UI_SPEC + CONTROL_MAPPING + FEATURE_MATRIX

    assert "视频时间剪辑" in combined
    assert "单轨时间轴" in combined
    for shortcut in ("Space", "J/K/L", "I/O", "B", "Delete", "Ctrl+Z"):
        assert shortcut in combined
    assert "多轨" in combined


def test_toolbox_excludes_archive_compression_without_removing_media_compression() -> None:
    combined = UI_SPEC + CONTROL_MAPPING + FEATURE_MATRIX

    assert "不提供归档压缩" in combined
    assert "`file.zip`" not in combined
    assert "视频压缩" in FEATURE_MATRIX
    assert "图片压缩" in FEATURE_MATRIX


def test_toolbox_excludes_duplicate_file_detection() -> None:
    combined = UI_SPEC + CONTROL_MAPPING + FEATURE_MATRIX

    assert "`file.duplicates`" not in combined
    assert "不提供重复文件检测" in combined


def test_settings_is_a_modal_surface_not_a_media_workbench() -> None:
    combined = UI_SPEC + CONTROL_MAPPING

    assert "设置弹窗" in combined
    assert "Ctrl+," in UI_SPEC
    assert "返回触发设置的控件" in combined
    assert "设置不复用媒体预览" in combined
    assert "只有一个设置入口" in UI_SPEC
    assert "只创建一个 `SettingsDialog`" in CONTROL_MAPPING


def test_timeline_interaction_has_lossless_and_precise_export_modes() -> None:
    combined = UI_SPEC + CONTROL_MAPPING + FEATURE_MATRIX

    assert "关键帧优先" in combined
    assert "精确边界自动重编码" in combined
    assert "保留片段" in combined
    assert "排除/恢复" in combined


def test_video_navigation_reuses_one_editor_for_trim_split_and_merge() -> None:
    combined = UI_SPEC + CONTROL_MAPPING + FEATURE_MATRIX

    assert "顶层只显示三个入口" in UI_SPEC
    assert "视频输出、GIF 制作、视频剪辑" in UI_SPEC
    assert "不得为 `video.trim`、`video.split`、`video.merge` 创建重复页面" in UI_SPEC
    assert "保持独立 Handler" in CONTROL_MAPPING


def test_ui_freeze_requires_evidence_not_expert_guessing() -> None:
    assert "经验只能产生待验证假设" in QUALITY_GATE
    assert "当前尚缺 Windows WinUI 原型" in QUALITY_GATE
    assert "静态 UI 设计冻结已通过" in QUALITY_GATE
    assert "不等于运行时 UX 验收通过" in QUALITY_GATE


def test_settings_dialog_freeze_evidence_is_consistent() -> None:
    audit = (ROOT / "docs/audit/2026-07-19-recheck/TINYPIX-4-UI-RECHECK.md").read_text()
    status = (ROOT / "docs/architecture/IMPLEMENTATION-STATUS.md").read_text()

    assert "Settings — static design freeze passed" in audit
    assert "complete-root scrim" in audit
    assert "single reusable `SettingsDialog`" in audit
    assert "all switches include visible on/off text" in audit
    assert "at least 44-pixel hit targets" in audit
    assert "36 top-level nodes" in audit
    assert "26 reusable components" in audit
    assert "separated blue outer" in audit
    assert "separated yellow outer" in audit
    assert "Static UI design freeze: passed" in status
    assert "runtime UX remains gated" in status


def test_high_contrast_settings_dialog_has_system_semantics_and_full_modal_scope() -> None:
    combined = UI_SPEC + CONTROL_MAPPING + QUALITY_GATE

    assert "完整 `XamlRoot`" in combined
    assert "覆盖顶部导航" in combined
    assert "Highlight` / `HighlightText" in combined
    assert "Window` / `WindowText" in combined
    assert "开/关文字" in combined
    assert "系统焦点矩形" in combined


def test_timeline_uses_one_source_time_coordinate_system() -> None:
    combined = UI_SPEC + CONTROL_MAPPING + QUALITY_GATE

    assert "源时间坐标系" in combined
    assert "缩略图、波形和片段轨不得重叠" in combined
    assert "入点、出点和播放头" in combined
    assert "范围外" in combined
    assert "空闲态不得高亮“分割”" in combined
    assert "时间戳换算" in combined


def test_qr_generator_uses_generator_semantics_not_media_controls() -> None:
    combined = UI_SPEC + CONTROL_MAPPING + FEATURE_MATRIX + QUALITY_GATE

    assert "二维码生成" in combined
    assert "L/M/Q/H" in combined
    assert "离散单选" in combined
    assert "PNG/SVG" in combined
    assert "实时二维码预览" in combined
    assert "不得使用播放图标" in combined
    assert "不得使用连续质量滑块" in combined
