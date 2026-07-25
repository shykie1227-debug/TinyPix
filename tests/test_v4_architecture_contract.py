from pathlib import Path
import json
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]


def project(name: str) -> Path:
    return ROOT / "src" / name / f"{name}.csproj"


def references(name: str) -> set[str]:
    tree = ET.parse(project(name))
    return {
        node.attrib["Include"].replace("\\", "/")
        for node in tree.findall(".//ProjectReference")
    }


def test_formal_solution_and_projects_are_versioned() -> None:
    assert (ROOT / "TinyPix.sln").is_file()
    for name in (
        "TinyPix.App",
        "TinyPix.Core",
        "TinyPix.Media",
        "TinyPix.Infrastructure",
    ):
        assert project(name).is_file()

    ignore = (ROOT / ".gitignore").read_text(encoding="utf-8")
    assert "!TinyPix.sln" in ignore
    assert "!build/" in ignore


def test_project_reference_direction_is_acyclic() -> None:
    assert references("TinyPix.Core") == set()
    assert references("TinyPix.Media") == {"../TinyPix.Core/TinyPix.Core.csproj"}
    assert references("TinyPix.Infrastructure") == {
        "../TinyPix.Core/TinyPix.Core.csproj"
    }
    assert references("TinyPix.App") == {
        "../TinyPix.Core/TinyPix.Core.csproj",
        "../TinyPix.Media/TinyPix.Media.csproj",
        "../TinyPix.Infrastructure/TinyPix.Infrastructure.csproj",
    }


def test_winui_app_is_unpackaged_self_contained_folder_publish() -> None:
    content = project("TinyPix.App").read_text(encoding="utf-8")
    tree = ET.parse(project("TinyPix.App"))
    packages = {
        node.attrib["Include"] for node in tree.findall(".//PackageReference")
    }

    for setting in (
        "<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>",
        "<AssemblyName>TinyPix</AssemblyName>",
        "<OutputType>WinExe</OutputType>",
        "<WindowsPackageType>None</WindowsPackageType>",
        "<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>",
        "<SelfContained>true</SelfContained>",
        "<RuntimeIdentifier>win-x64</RuntimeIdentifier>",
        "<PublishSingleFile>false</PublishSingleFile>",
    ):
        assert setting in content

    assert "WebView" not in content
    assert "Microsoft.WindowsAppSDK" not in packages
    assert "Microsoft.WindowsAppSDK.WinUI" in packages
    assert "Microsoft.WindowsAppSDK.Runtime" in packages


def test_portable_distribution_contract_is_present() -> None:
    required = (
        "assets/Portable/portable.flag",
        "assets/Portable/Config/settings.json",
        "assets/Portable/THIRD_PARTY_NOTICES.txt",
        "assets/Portable/OFFLINE_SECURITY.md",
        "build/publish-portable.ps1",
        "build/generate-sbom.ps1",
        "build/verify-portable-layout.ps1",
        "docs/architecture/TINPIX-4-ARCHITECTURE.md",
        "docs/architecture/WINDOWS-FEASIBILITY-GATE.md",
    )

    for relative_path in required:
        assert (ROOT / relative_path).is_file(), relative_path

    publish = (ROOT / "build" / "publish-portable.ps1").read_text(encoding="utf-8")
    sbom = (ROOT / "build" / "generate-sbom.ps1").read_text(encoding="utf-8")
    verify = (ROOT / "build" / "verify-portable-layout.ps1").read_text(encoding="utf-8")
    assert "generate-sbom.ps1" in publish
    assert "project.assets.json" in sbom
    assert "dotnet list" not in sbom
    for artifact in ("sbom.cdx.json", "dependencies.json", "OFFLINE_SECURITY.md"):
        assert artifact in verify


def test_all_package_versions_are_centrally_locked() -> None:
    props = ET.parse(ROOT / "Directory.Packages.props")
    package_versions = props.findall(".//PackageVersion")

    assert package_versions
    assert all(node.attrib.get("Version") for node in package_versions)

    for name in ("TinyPix.App", "TinyPix.Media", "TinyPix.Infrastructure"):
        tree = ET.parse(project(name))
        assert all(
            "Version" not in node.attrib
            for node in tree.findall(".//PackageReference")
        )


def test_app_lock_excludes_unused_windows_sdk_components_and_vulnerable_sqlite() -> None:
    lock_path = ROOT / "src" / "TinyPix.App" / "packages.lock.json"
    lock_data = json.loads(lock_path.read_text(encoding="utf-8"))
    packages = {
        name: metadata
        for framework in lock_data["dependencies"].values()
        for name, metadata in framework.items()
    }

    assert packages["Microsoft.WindowsAppSDK.WinUI"]["resolved"] == "2.2.1"
    assert packages["Microsoft.WindowsAppSDK.Runtime"]["resolved"] == "2.2.0"
    assert packages["SQLitePCLRaw.lib.e_sqlite3"]["resolved"] == "2.1.12"
    assert not {
        "Microsoft.WindowsAppSDK",
        "Microsoft.WindowsAppSDK.AI",
        "Microsoft.WindowsAppSDK.ML",
        "Microsoft.WindowsAppSDK.Widgets",
        "Microsoft.Windows.AI.MachineLearning",
    }.intersection(packages)


def test_project_has_a_stable_fast_retrieval_index() -> None:
    index_path = ROOT / "PROJECT-INDEX.md"
    assert index_path.is_file()

    index = index_path.read_text(encoding="utf-8")
    agents = (ROOT / "AGENTS.md").read_text(encoding="utf-8")
    for marker in (
        "60 秒开始",
        "事实源优先级",
        "任务到文件路由",
        "TinyPix 4.0 WinUI",
        "旧 Tauri",
        "Windows 可行性门禁",
        "Git 分支工作流",
        "验证命令",
    ):
        assert marker in index

    assert "PROJECT-INDEX.md" in agents
    assert "design/TinyPix-4.0.pen" in agents
    assert "旧 Tauri" in agents

    for relative_dir in (
        "src/TinyPix.Core/Jobs",
        "src/TinyPix.Infrastructure/Settings",
        "src/TinyPix.Infrastructure/History",
        "src/TinyPix.Infrastructure/Portable",
        "src/TinyPix.Media/Ffmpeg",
    ):
        assert f"`{relative_dir}" in index
        assert (ROOT / relative_dir).is_dir()

    assert (
        "| 当前门禁 | Windows 10 可丢弃原型已通过；Windows 11 干净 VM "
        "尚未提供，整体门禁未完成 |"
    ) in index
    assert "设置弹窗静态冻结证据已通过" in index
    assert index.index("Windows 可行性门禁：静态 UI 冻结后的入口") < index.index(
        "设置弹窗静态冻结证据已通过"
    )

    assert (
        "Legacy Tauri/WebView only:" in agents
        and "The WinUI target must not add WebView UI." in agents
    )


def test_winui_gate_uses_activated_dpi_and_strict_compact_bounds() -> None:
    implementation = (ROOT / "build" / "winui-gate-implement.ps1").read_text(
        encoding="utf-8"
    )
    implementation_wrapper = (
        ROOT / "build" / "winui-gate-implement-wrapper.ps1"
    ).read_text(encoding="utf-8")
    uia = (ROOT / "build" / "winui-gate-uia.ps1").read_text(encoding="utf-8")

    assert 'Activated += OnWindowActivated' in implementation
    assert 'AppWindow.MoveAndResize' in implementation
    assert 'AppWindow.Resize(GetInitialWindowSize())' not in implementation
    assert '<Setter Target="ParameterRegion.Padding" Value="10" />' in implementation
    assert '<AdaptiveTrigger MinWindowWidth="1000" />' in implementation
    assert '可行性测试（非正式界面）' in implementation
    assert '<Setter Property="IsSpellCheckEnabled" Value="False" />' in implementation
    assert '<VisualState x:Name="HighTextScale">' in implementation
    assert '<Setter Target="LeftColumn.Width" Value="240" />' in implementation
    assert '<Setter Target="RightColumn.Width" Value="250" />' in implementation
    assert '<Setter Target="AppTitleStack.Visibility" Value="Collapsed" />' in implementation
    assert '<Setter Target="OfflineBadge.Visibility" Value="Collapsed" />' in implementation
    assert "ApplyTextScaleState()" in implementation
    assert "TextScaleFactor >= 1.5" in implementation
    assert 'SizeChanged="OnPageSizeChanged"' in implementation
    assert "DispatcherQueue.TryEnqueue(ApplyTextScaleState)" in implementation
    assert "LeftColumn.Width = new GridLength(240)" in implementation
    assert "RightColumn.Width = new GridLength(250)" in implementation
    assert '<StackPanel x:Name="SecondaryActions" Orientation="Vertical" Spacing="8">' in implementation
    assert '<Button x:Name="FailureButton"' in implementation

    assert 'INITIAL_WINDOW' in uia
    assert 'fitsScreen=' in uia
    assert 'MoveWindow' in uia
    assert 'COMPACT_WITHIN_WINDOW' in uia
    assert 'COMPACT_WITHIN_SCREEN' in uia
    assert 'ScrollItemPattern' in uia
    assert 'ScrollIntoView' in uia
    assert 'COMPACT_OUTPUT_SCROLL_INTO_VIEW' in uia
    assert 'COMPACT_INITIAL_OFFSCREEN_ALLOWED name=输出目录' in uia
    assert 'COMPACT_OUTPUT_WITHIN_WINDOW' in uia
    assert 'COMPACT_OUTPUT_WITHIN_SCREEN' in uia

    assert "Remove-Item -LiteralPath $errorPath" in implementation_wrapper
    assert "C:\\Program Files\\dotnet" in implementation_wrapper
    assert "Get-Process TinyPix.WinUIGate" in implementation_wrapper
    assert "Stop-Process -Force" in implementation_wrapper


def test_winui_gate_scale_selection_is_locale_independent() -> None:
    scale_script = (ROOT / "build" / "winui-gate-set-scale.ps1").read_text(
        encoding="utf-8"
    )

    assert "@('100%', '125%', '150%')" in scale_script
    assert ".StartsWith($target" in scale_script
    assert "125% (推荐)" not in scale_script


def test_winui_gate_exercises_real_ffmpeg_video_thumbnail_fallback() -> None:
    media_uia = (ROOT / "build" / "winui-gate-media-uia.ps1").read_text(
        encoding="utf-8"
    )

    assert "-c:v ffv1" in media_uia
    assert "已显示 FFmpeg 本地缩略图回退" in media_uia
    assert "VIDEO_FALLBACK" in media_uia
    assert "30-video-ffmpeg-thumbnail-fallback.png" in media_uia
    assert "[bool]$requirePreview = $true" in media_uia
    assert "@('视频预览', '图片预览') $false" in media_uia
    assert "if (-not $requirePreview)" in media_uia
    assert "VIDEO_FALLBACK systemDecodeFailureObserved=true" in media_uia
    assert "catch [System.Runtime.InteropServices.COMException]" in media_uia
    assert "$window = Refresh-GateWindow" in media_uia
    implementation = (ROOT / "build" / "winui-gate-implement.ps1").read_text(
        encoding="utf-8"
    )
    assert 'AutomationProperties.AutomationId="PreviewFocusTarget"' in implementation
    assert "DispatcherQueue.TryEnqueue(async () => await ShowVideoFallbackAsync())" in implementation
    assert "private async Task ShowVideoFallbackAsync()" in implementation
    assert "VIDEO_FALLBACK exception=" in implementation


def test_winui_gate_runs_real_narrator_with_keyboard_focus_walk() -> None:
    narrator = ROOT / "build" / "winui-gate-narrator-uia.ps1"
    wrapper = ROOT / "build" / "winui-gate-narrator-uia-wrapper.ps1"

    assert narrator.is_file()
    assert wrapper.is_file()
    script = narrator.read_text(encoding="utf-8")
    for marker in (
        "Narrator.exe",
        "NARRATOR processActive=True",
        "NARRATOR_BASELINE",
        "SendWait('{F6}')",
        "SendWait('^j')",
        "NARRATOR_F6_$($index + 1)",
        "NARRATOR_CTRL_J",
        "NARRATOR_SETTINGS",
        "NARRATOR_UIA complete=true",
    ):
        assert marker in script
    assert "@('图片工具', '添加媒体文件', '中央预览区域', '输出格式', '任务队列区域')" in script


def test_winui_gate_captures_and_rejects_registry_writes_with_procmon() -> None:
    procmon = ROOT / "build" / "winui-gate-procmon.ps1"

    assert procmon.is_file()
    script = procmon.read_text(encoding="utf-8")
    for marker in (
        "https://download.sysinternals.com/files/ProcessMonitor.zip",
        "Get-AuthenticodeSignature",
        "Microsoft Corporation",
        "/AcceptEula /Quiet /Minimized /BackingFile",
        "/Terminate /Quiet",
        "/OpenLog",
        "/SaveAs",
        "TinyPix.WinUIGate.exe",
        "RegCreateKey",
        "RegSetValue",
        "RegDeleteKey",
        "RegDeleteValue",
        "REGISTRY_WRITE_RESULT passed=True",
        "REG_OPENED_EXISTING_KEY",
        "REG_CREATED_NEW_KEY",
        "Services\\bam\\State",
        "appOwnedWrites",
        "$captureProcess = Start-Process",
        "-PassThru",
        "Wait-LaunchTaskReady",
        "TinyPix gate did not start before",
    ):
        assert marker in script
    capture_line = next(line for line in script.splitlines() if "$captureProcess = Start-Process" in line)
    assert "-Wait" not in capture_line


def test_winui_gate_repeats_settings_media_and_ffmpeg_with_network_disabled() -> None:
    offline = ROOT / "build" / "winui-gate-offline.ps1"

    assert offline.is_file()
    script = offline.read_text(encoding="utf-8")
    for marker in (
        "Disable-NetAdapter",
        "Enable-NetAdapter",
        "finally",
        "NETWORK offline=True",
        "TinyPixWinUIGateOfflineUIA",
        "TinyPixWinUIGateOfflineMediaUIA",
        "OFFLINE_UIA result=$uiaResult",
        "OFFLINE_MEDIA result=$mediaResult",
        "networkRestored=$networkRestored",
    ):
        assert marker in script


def test_windows_formal_projects_are_tested_from_a_clean_guest_local_copy() -> None:
    formal_tests = ROOT / "build" / "windows-formal-tests.ps1"

    assert formal_tests.is_file()
    script = formal_tests.read_text(encoding="utf-8")
    for marker in (
        "$env:TEMP",
        "/XD bin obj",
        "TinyPix.Core.Tests.csproj",
        "TinyPix.Media.Tests.csproj",
        "TinyPix.Infrastructure.Tests.csproj",
        "RestoreLockedMode=true",
        "TinyPix.App.csproj",
        "--locked-mode",
        "WINDOWS_FORMAL_TESTS passed=True",
    ):
        assert marker in script


def test_winui_gate_runs_200_percent_text_scale_and_restores_100_percent() -> None:
    setter = ROOT / "build" / "winui-gate-set-text-scale.ps1"
    orchestrator = ROOT / "build" / "winui-gate-text-scale-200.ps1"

    assert setter.is_file()
    assert orchestrator.is_file()
    setter_script = setter.read_text(encoding="utf-8")
    orchestrator_script = orchestrator.read_text(encoding="utf-8")
    assert "SystemSettings_EaseOfAccess_Experience_TextScalingDesktop_Slider" in setter_script
    assert "SystemSettings_EaseOfAccess_Experience_TextScalingDesktop_ButtonRemove" in setter_script
    assert "RangeValuePattern" in setter_script
    assert "SetValue($target)" in setter_script
    for marker in (
        "Set-TextScale 200",
        "TEXT_SCALE_UIA result=0",
        "32-compact-900x600-200percent-text.png",
        "finally",
        "Set-TextScale 100",
        "TEXT_SCALE restored=100",
    ):
        assert marker in orchestrator_script
