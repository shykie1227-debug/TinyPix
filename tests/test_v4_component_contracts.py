"""TinyPix 4.0 reusable-component contracts.

Pins the 26 Pencil reusable components (design/WINUI-CONTROL-MAPPING.md §3) to
their WinUI 3 realization: semantic theme tokens (DESIGN.md §5), shared styles
for the native-control components, and composite UserControls for the rest.
These are structural string contracts, verified cross-platform on macOS; real
Windows window/UIA verification happens on the Windows VM.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src" / "TinyPix.App"


def read(relative: str) -> str:
    return (APP / relative).read_text(encoding="utf-8")


# ---------------------------------------------------------------- tokens ----

def test_semantic_theme_tokens_exist_in_three_variants() -> None:
    theme = read("Themes/TinyPix.xaml")
    for token in (
        "TinyPixCanvas",
        "TinyPixSurface",
        "TinyPixInk",
        "TinyPixMuted",
        "TinyPixBorder",
        "TinyPixControlBorder",
        "TinyPixMediaStage",
        "TinyPixOnMedia",
        "TinyPixLime",
        "TinyPixOnLime",
        "TinyPixActionPrimary",
        "TinyPixOnActionPrimary",
        "TinyPixDanger",
        "TinyPixOnDanger",
        "TinyPixFocus",
    ):
        assert f"{token}Color" in theme or f"{token}Brush" in theme, token

    for variant in ('x:Key="Light"', 'x:Key="Dark"', 'x:Key="HighContrast"'):
        assert variant in theme, variant

    # High contrast defers to system colors, never brand lime.
    hc = theme.split('x:Key="HighContrast"', 1)[1]
    assert "SystemColorWindowColor" in hc
    assert "SystemColorHighlightColor" in hc
    assert "#B4F400" not in hc


def test_app_merges_theme_and_component_styles() -> None:
    app = read("App.xaml")
    assert 'Source="Themes/TinyPix.xaml"' in app
    assert 'Source="Styles/Controls.xaml"' in app


def test_shared_styles_for_native_button_components() -> None:
    styles = read("Styles/Controls.xaml")
    for key in (
        "TinyPixPrimaryButtonStyle",
        "TinyPixSecondaryButtonStyle",
        "TinyPixDangerButtonStyle",
        "TinyPixIconButtonStyle",
    ):
        assert key in styles, key
    # primary button uses action-primary + on-action-primary tokens
    primary = styles.split("TinyPixPrimaryButtonStyle", 1)[1].split("</Style>", 1)[0]
    assert "TinyPixActionPrimaryBrush" in primary
    assert "TinyPixOnActionPrimaryBrush" in primary


# ------------------------------------------------------- composite controls ----

# component file -> required XAML markers (automation / structure / tokens)
# The 4.0 redesign replaced the placeholder-era splits (FileCardItem /
# TaskCardItem / PresetCardItem / PageTitle / EmptyState / EditorCommandBar /
# PathPicker / RightParameterPanel) with the design's own region components.
COMPOSITE = {
    "Controls/LeftFilePanelControl.xaml": (
        "LeftFilePanelControl",
        "添加媒体文件",
        "AutomationProperties.Name",
    ),
    "Controls/TaskQueueControl.xaml": (
        "TaskQueueControl",
        "任务队列",
        "AutomationProperties.Name",
    ),
    "Controls/CenterHeaderControl.xaml": (
        "CenterHeaderControl",
        "适应窗口",
        "AutomationProperties.Name",
    ),
    "Controls/PreviewStageControl.xaml": (
        "PreviewStageControl",
        "AutomationProperties.Name",
    ),
    "Controls/PresetRowControl.xaml": (
        "PresetRowControl",
        "管理预设",
        "AutomationProperties.Name",
    ),
    "Controls/TimelineEditorControl.xaml": (
        "TimelineEditorControl",
        "非破坏单轨",
        "AutomationProperties.Name",
    ),
    "Controls/SettingsDialogContent.xaml": (
        "SettingsDialogContent",
        "保存设置",
        "AutomationProperties.Name",
    ),
}


def test_composite_usercontrols_exist_with_required_markers() -> None:
    for relative, markers in COMPOSITE.items():
        assert (APP / relative).is_file(), relative
        xaml = read(relative)
        for marker in markers:
            assert marker in xaml, f"{relative}: {marker}"


def test_composite_usercontrols_have_codebehind() -> None:
    for relative in COMPOSITE:
        codebehind = relative + ".cs"
        assert (APP / codebehind).is_file(), codebehind


def test_shell_uses_real_region_components_not_placeholders() -> None:
    main_window = read("MainWindow.xaml")
    # Shell regions host real UserControls: the left file panel, the workbench
    # frame (each category page owns its own right-hand parameter column) and
    # the task queue, plus the modal settings overlay.
    for region in (
        "LeftFilePanelControl",
        "TaskQueueControl",
        "WorkbenchFrame",
        "SettingsOverlay",
    ):
        assert region in main_window, region
