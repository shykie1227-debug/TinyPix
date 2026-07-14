import importlib.util
import base64
import json
from pathlib import Path


def load_build_module():
    build_path = Path(__file__).resolve().parents[1] / "build.py"
    spec = importlib.util.spec_from_file_location("tinypix_build", build_path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def test_fix_tauri_config_keeps_frontend_dist_relative(tmp_path):
    build = load_build_module()
    build.TARGET = tmp_path

    config_dir = tmp_path / "src-tauri"
    config_dir.mkdir()
    config_path = config_dir / "tauri.conf.json"
    config_path.write_text(
        json.dumps(
            {
                "build": {
                    "frontendDist": "C:/Users/huashu/AppData/Local/TinyPixBuild/dist",
                    "beforeBuildCommand": "",
                }
            }
        ),
        encoding="utf-8",
    )

    assert build.fix_tauri_config() is True

    config = json.loads(config_path.read_text(encoding="utf-8"))
    assert config["build"]["frontendDist"] == "../dist"


def test_copy_excludes_generated_directories():
    build = load_build_module()

    assert "node_modules" in build.COPY_EXCLUDED_DIRS
    assert "dist" in build.COPY_EXCLUDED_DIRS
    assert "src-tauri\\target" in build.COPY_EXCLUDED_DIRS
    assert "logs" in build.COPY_EXCLUDED_DIRS


def test_source_candidates_prefer_manual_parallels_test_copy():
    build = load_build_module()
    candidates = [str(candidate) for candidate in build.SOURCE_CANDIDATES]

    manual_copy = r"C:\Mac\Home\Desktop\test\3.5pro"
    legacy_smb = r"Y:\TinyPix\3.5pro"

    assert manual_copy in candidates
    assert candidates.index(manual_copy) < candidates.index(legacy_smb)


def test_ffmpeg_bundle_candidates_include_sidecar_and_common_install_paths():
    build = load_build_module()
    candidates = [str(path).replace("/", "\\") for path in build.get_ffmpeg_bundle_candidates("ffmpeg.exe")]

    assert r"C:\Users\huashu\AppData\Roaming\TinyPix\sidecars\ffmpeg.exe" in candidates
    assert r"C:\ffmpeg\bin\ffmpeg.exe" in candidates
    assert r"C:\tools\ffmpeg\bin\ffmpeg.exe" in candidates


def test_build_caches_are_outside_cleaned_target():
    build = load_build_module()

    assert build.BUILD_CACHE_ROOT != build.TARGET
    assert build.NPM_CACHE_DIR != build.TARGET / ".npm-cache"
    assert build.CARGO_TARGET_CACHE != build.TARGET / "src-tauri" / "target"
    assert build.FFMPEG_CACHE_DIR != build.TARGET / ".ffmpeg-cache"
    assert str(build.BUILD_CACHE_ROOT).replace("/", "\\").endswith(r"TinyPixBuildCache")


def test_node_requirement_matches_the_automatic_installer():
    build = load_build_module()

    bundled_version = build.parse_version(build._NODE_LTS_URL)
    assert bundled_version >= build.NODE_MIN


def test_powershell_scripts_use_encoded_command_to_survive_cmd_quoting():
    build = load_build_module()
    script = '$url="https://example.test/file.exe";$out="$env:TEMP\\tool.exe";& $out -y'

    command = build.powershell_command(script)
    encoded = command.rsplit(" ", 1)[-1]

    assert "-EncodedCommand" in command
    assert base64.b64decode(encoded).decode("utf-16le") == script


def test_msvc_environment_is_loaded_from_vcvars(monkeypatch, tmp_path):
    build = load_build_module()
    vcvars = tmp_path / "vcvars64.bat"
    vcvars.write_text("@echo off", encoding="utf-8")
    monkeypatch.setattr(
        build,
        "run",
        lambda *args, **kwargs: (
            0,
            "PATH=C:\\BuildTools\\VC\\bin;C:\\Windows\\System32\n"
            "INCLUDE=C:\\BuildTools\\VC\\include\n",
            "",
        ),
    )

    assert build.activate_msvc_environment(vcvars) is True
    assert build.os.environ["PATH"].startswith(r"C:\BuildTools\VC\bin")
    assert build.os.environ["INCLUDE"] == r"C:\BuildTools\VC\include"


def test_local_windows_source_builds_in_place(monkeypatch):
    build = load_build_module()
    build.SOURCE = Path(r"C:\Users\huashu\Desktop\test\3.5pro")
    monkeypatch.delenv("TINYPIX_FORCE_STAGE", raising=False)
    monkeypatch.delenv("TINYPIX_BUILD_IN_PLACE", raising=False)

    build.configure_build_target()

    assert build.TARGET == build.SOURCE


def test_shared_source_still_uses_local_stage(monkeypatch):
    build = load_build_module()
    build.SOURCE = Path(r"C:\Mac\Home\Desktop\test\3.5pro")
    monkeypatch.delenv("TINYPIX_FORCE_STAGE", raising=False)
    monkeypatch.delenv("TINYPIX_BUILD_IN_PLACE", raising=False)

    build.configure_build_target()

    assert build.TARGET == build.DEFAULT_TARGET
