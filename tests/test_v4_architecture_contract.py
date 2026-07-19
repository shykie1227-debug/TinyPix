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
        "| 当前门禁 | 静态 UI 已冻结；立即执行 Windows 可丢弃原型，"
        "真实运行验收仍未通过 |"
    ) in index
    assert "设置弹窗静态冻结证据已通过" in index
    assert index.index("Windows 可行性门禁：静态 UI 冻结后的入口") < index.index(
        "设置弹窗静态冻结证据已通过"
    )

    assert (
        "Legacy Tauri/WebView only:" in agents
        and "The WinUI target must not add WebView UI." in agents
    )
