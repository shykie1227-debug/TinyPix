use crate::domain::image_engine::{decode_image, encode_to_png, resize_to_max_edge};
use crate::infrastructure::ffmpeg_manager::VideoProbeInfo;
use image::GenericImageView;
use sha2::{Digest, Sha256};
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::{Mutex, OnceLock};

pub const PREVIEW_VERSION: &str = "1";

static PREVIEW_WRITE_LOCK: OnceLock<Mutex<()>> = OnceLock::new();

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ImagePreviewArtifact {
    pub path: PathBuf,
    pub width: u32,
    pub height: u32,
}

pub fn default_preview_root() -> Result<PathBuf, String> {
    dirs::data_local_dir()
        .map(|path| path.join("TinyPix").join("previews"))
        .ok_or_else(|| "无法确定本地预览缓存目录".to_string())
}

fn normalized_path(path: &Path) -> String {
    let resolved = std::fs::canonicalize(path).unwrap_or_else(|_| path.to_path_buf());
    let value = resolved.to_string_lossy().replace('\\', "/");
    if cfg!(target_os = "windows") {
        value.to_lowercase()
    } else {
        value
    }
}

pub fn preview_cache_key(path: &Path, engine_token: &str) -> Result<String, String> {
    let metadata =
        std::fs::metadata(path).map_err(|error| format!("读取预览源文件信息失败: {error}"))?;
    let modified_nanos = metadata
        .modified()
        .ok()
        .and_then(|time| time.duration_since(std::time::UNIX_EPOCH).ok())
        .map(|duration| duration.as_nanos())
        .unwrap_or_default();
    let fingerprint = format!(
        "{}\n{}\n{}\n{}\n{}",
        normalized_path(path),
        metadata.len(),
        modified_nanos,
        engine_token,
        PREVIEW_VERSION
    );
    Ok(hex::encode(Sha256::digest(fingerprint.as_bytes())))
}

pub fn generated_preview_path(
    input: &Path,
    engine_token: &str,
    category: &str,
    extension: &str,
) -> Result<PathBuf, String> {
    let key = preview_cache_key(input, engine_token)?;
    Ok(default_preview_root()?
        .join(category)
        .join(format!("{key}.{extension}")))
}

pub fn is_nonempty_cache_file(path: &Path) -> bool {
    std::fs::metadata(path)
        .map(|metadata| metadata.is_file() && metadata.len() > 0)
        .unwrap_or(false)
}

pub fn is_valid_mp4_cache_file(path: &Path) -> bool {
    std::fs::read(path)
        .ok()
        .is_some_and(|bytes| bytes.len() >= 12 && bytes.get(4..8) == Some(b"ftyp"))
}

pub fn commit_generated_preview(temporary: &Path, destination: &Path) -> Result<(), String> {
    let _guard = PREVIEW_WRITE_LOCK
        .get_or_init(|| Mutex::new(()))
        .lock()
        .map_err(|_| "预览缓存写入锁已损坏".to_string())?;
    let parent = destination
        .parent()
        .ok_or_else(|| "预览缓存路径无父目录".to_string())?;
    std::fs::create_dir_all(parent).map_err(|error| format!("创建预览缓存失败: {error}"))?;
    let destination_is_valid = if destination
        .extension()
        .and_then(|extension| extension.to_str())
        .is_some_and(|extension| extension.eq_ignore_ascii_case("mp4"))
    {
        is_valid_mp4_cache_file(destination)
    } else {
        is_nonempty_cache_file(destination)
    };
    if destination_is_valid {
        let _ = std::fs::remove_file(temporary);
        return Ok(());
    }
    if destination.exists() {
        std::fs::remove_file(destination)
            .map_err(|error| format!("替换损坏预览缓存失败: {error}"))?;
    }
    std::fs::rename(temporary, destination).map_err(|error| format!("提交预览缓存失败: {error}"))
}

pub fn is_direct_video(path: &Path, info: &VideoProbeInfo) -> bool {
    let extension_is_mp4 = path
        .extension()
        .and_then(|extension| extension.to_str())
        .is_some_and(|extension| extension.eq_ignore_ascii_case("mp4"));
    let video_is_h264 = info.video_codec.eq_ignore_ascii_case("h264");
    let audio_is_safe = !info.has_audio
        || info
            .audio_codec
            .as_deref()
            .is_some_and(|codec| codec.eq_ignore_ascii_case("aac"));
    extension_is_mp4 && video_is_h264 && audio_is_safe
}

pub fn build_proxy_args(input: &str, output: &str, has_audio: bool) -> Vec<String> {
    let mut args = vec![
        "-y".to_string(),
        "-i".to_string(),
        input.to_string(),
        "-map".to_string(),
        "0:v:0".to_string(),
        "-vf".to_string(),
        "scale=960:540:force_original_aspect_ratio=decrease:force_divisible_by=2,fps=30"
            .to_string(),
        "-c:v".to_string(),
        "libx264".to_string(),
        "-pix_fmt".to_string(),
        "yuv420p".to_string(),
        "-preset".to_string(),
        "veryfast".to_string(),
        "-crf".to_string(),
        "28".to_string(),
    ];
    if has_audio {
        args.extend([
            "-map".to_string(),
            "0:a:0?".to_string(),
            "-c:a".to_string(),
            "aac".to_string(),
            "-b:a".to_string(),
            "128k".to_string(),
        ]);
    } else {
        args.push("-an".to_string());
    }
    args.extend([
        "-movflags".to_string(),
        "+faststart".to_string(),
        "-progress".to_string(),
        "pipe:1".to_string(),
        "-nostats".to_string(),
        output.to_string(),
    ]);
    args
}

fn is_valid_png(path: &Path) -> bool {
    let signature_is_valid = std::fs::read(path)
        .ok()
        .is_some_and(|bytes| bytes.starts_with(b"\x89PNG\r\n\x1a\n"));
    signature_is_valid && image::open(path).is_ok()
}

fn write_atomically(path: &Path, bytes: &[u8]) -> Result<(), String> {
    let parent = path
        .parent()
        .ok_or_else(|| "预览缓存路径无父目录".to_string())?;
    std::fs::create_dir_all(parent).map_err(|error| format!("创建预览缓存失败: {error}"))?;
    let temporary = parent.join(format!(
        ".{}.{}.tmp",
        path.file_name()
            .and_then(|name| name.to_str())
            .unwrap_or("preview"),
        uuid::Uuid::new_v4()
    ));
    let result = (|| {
        let mut file = std::fs::File::create(&temporary)
            .map_err(|error| format!("创建预览临时文件失败: {error}"))?;
        file.write_all(bytes)
            .map_err(|error| format!("写入预览临时文件失败: {error}"))?;
        file.sync_all()
            .map_err(|error| format!("同步预览临时文件失败: {error}"))?;
        if path.exists() {
            std::fs::remove_file(path).map_err(|error| format!("替换损坏预览缓存失败: {error}"))?;
        }
        std::fs::rename(&temporary, path).map_err(|error| format!("提交预览缓存失败: {error}"))
    })();
    if result.is_err() {
        let _ = std::fs::remove_file(temporary);
    }
    result
}

pub fn prepare_image_preview(
    path: &Path,
    engine_token: &str,
) -> Result<ImagePreviewArtifact, String> {
    let root = default_preview_root()?;
    prepare_image_preview_in(path, &root, engine_token)
}

pub fn prepare_image_preview_in(
    path: &Path,
    cache_root: &Path,
    engine_token: &str,
) -> Result<ImagePreviewArtifact, String> {
    let _guard = PREVIEW_WRITE_LOCK
        .get_or_init(|| Mutex::new(()))
        .lock()
        .map_err(|_| "预览缓存写入锁已损坏".to_string())?;
    let key = preview_cache_key(path, engine_token)?;
    let output = cache_root.join("images").join(format!("{key}.png"));
    if !is_valid_png(&output) {
        let source = decode_image(&path.to_string_lossy())?;
        let preview = resize_to_max_edge(&source, 2048);
        let encoded = encode_to_png(&preview)?;
        write_atomically(&output, &encoded)?;
    }
    let decoded = image::open(&output).map_err(|error| format!("预览缓存校验失败: {error}"))?;
    let (width, height) = decoded.dimensions();
    Ok(ImagePreviewArtifact {
        path: output,
        width,
        height,
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::infrastructure::ffmpeg_manager::VideoProbeInfo;
    use image::{GenericImageView, Rgba, RgbaImage};
    use std::path::Path;

    fn temp_root(label: &str) -> std::path::PathBuf {
        std::env::temp_dir().join(format!("tinypix-preview-{label}-{}", uuid::Uuid::new_v4()))
    }

    fn video_info(video_codec: &str, audio_codec: Option<&str>) -> VideoProbeInfo {
        VideoProbeInfo {
            duration_secs: 12.0,
            width: 1920,
            height: 1080,
            bitrate_kbps: 4000,
            fps: 29.97,
            codec: video_codec.to_string(),
            format: "mov,mp4,m4a,3gp,3g2,mj2".to_string(),
            container: "mov,mp4,m4a,3gp,3g2,mj2".to_string(),
            video_codec: video_codec.to_string(),
            audio_codec: audio_codec.map(str::to_string),
            has_audio: audio_codec.is_some(),
            rotation: 0,
        }
    }

    #[test]
    fn cache_key_changes_with_file_size_and_modified_time() {
        let root = temp_root("key");
        std::fs::create_dir_all(&root).unwrap();
        let input = root.join("sample file.bin");
        std::fs::write(&input, b"a").unwrap();
        let first = preview_cache_key(&input, "engine-a").unwrap();
        std::thread::sleep(std::time::Duration::from_millis(5));
        std::fs::write(&input, b"changed").unwrap();
        let second = preview_cache_key(&input, "engine-a").unwrap();
        let third = preview_cache_key(&input, "engine-b").unwrap();
        assert_ne!(first, second);
        assert_ne!(second, third);
        let _ = std::fs::remove_dir_all(root);
    }

    #[test]
    fn direct_video_requires_mp4_h264_and_optional_aac() {
        assert!(is_direct_video(
            Path::new("clip.mp4"),
            &video_info("h264", Some("aac"))
        ));
        assert!(is_direct_video(
            Path::new("clip.mp4"),
            &video_info("h264", None)
        ));
        assert!(!is_direct_video(
            Path::new("clip.mov"),
            &video_info("h264", Some("aac"))
        ));
        assert!(!is_direct_video(
            Path::new("clip.mp4"),
            &video_info("hevc", Some("aac"))
        ));
        assert!(!is_direct_video(
            Path::new("clip.mp4"),
            &video_info("h264", Some("opus"))
        ));
    }

    #[test]
    fn proxy_args_enforce_webview_safe_video_and_audio() {
        let with_audio = build_proxy_args("输入 视频.mov", "proxy.mp4", true);
        let joined = with_audio.join(" ");
        for expected in [
            "scale=960:540:force_original_aspect_ratio=decrease",
            "fps=30",
            "libx264",
            "yuv420p",
            "veryfast",
            "28",
            "+faststart",
            "aac",
        ] {
            assert!(joined.contains(expected), "missing {expected}: {joined}");
        }
        let without_audio = build_proxy_args("silent.mkv", "proxy.mp4", false).join(" ");
        assert!(without_audio.contains("-an"));
        assert!(!without_audio.contains("aac"));
    }

    #[test]
    fn mp4_cache_validation_rejects_nonempty_corruption() {
        let root = temp_root("mp4-validation");
        std::fs::create_dir_all(&root).unwrap();
        let broken = root.join("broken.mp4");
        std::fs::write(&broken, b"not-an-mp4-but-nonempty").unwrap();
        assert!(!is_valid_mp4_cache_file(&broken));
        let valid = root.join("valid.mp4");
        std::fs::write(&valid, b"\0\0\0\x18ftypisom\0\0\0\0").unwrap();
        assert!(is_valid_mp4_cache_file(&valid));
        let _ = std::fs::remove_dir_all(root);
    }

    #[test]
    fn image_preview_is_png_limited_to_2048_and_repairs_empty_cache() {
        let root = temp_root("图片 空格");
        std::fs::create_dir_all(&root).unwrap();
        let input = root.join("超宽 图片.png");
        RgbaImage::from_pixel(3000, 1000, Rgba([10, 20, 30, 255]))
            .save(&input)
            .unwrap();
        let cache = root.join("cache");
        let first = prepare_image_preview_in(&input, &cache, "engine-test").unwrap();
        assert_eq!(
            &std::fs::read(&first.path).unwrap()[..8],
            b"\x89PNG\r\n\x1a\n"
        );
        let decoded = image::open(&first.path).unwrap();
        assert_eq!(decoded.dimensions(), (2048, 683));
        assert_eq!((first.width, first.height), (2048, 683));

        std::fs::write(&first.path, []).unwrap();
        let repaired = prepare_image_preview_in(&input, &cache, "engine-test").unwrap();
        assert_eq!(repaired.path, first.path);
        assert!(std::fs::metadata(&repaired.path).unwrap().len() > 8);
        let _ = std::fs::remove_dir_all(root);
    }

    #[test]
    fn concurrent_image_preview_requests_share_one_valid_file() {
        let root = temp_root("concurrent");
        std::fs::create_dir_all(&root).unwrap();
        let input = root.join("same.png");
        RgbaImage::from_pixel(64, 64, Rgba([1, 2, 3, 255]))
            .save(&input)
            .unwrap();
        let cache = root.join("cache");
        let handles: Vec<_> = (0..4)
            .map(|_| {
                let input = input.clone();
                let cache = cache.clone();
                std::thread::spawn(move || {
                    prepare_image_preview_in(&input, &cache, "engine-test")
                        .unwrap()
                        .path
                })
            })
            .collect();
        let paths: Vec<_> = handles
            .into_iter()
            .map(|handle| handle.join().unwrap())
            .collect();
        assert!(paths.windows(2).all(|pair| pair[0] == pair[1]));
        assert_eq!(
            &std::fs::read(&paths[0]).unwrap()[..8],
            b"\x89PNG\r\n\x1a\n"
        );
        let _ = std::fs::remove_dir_all(root);
    }
}
