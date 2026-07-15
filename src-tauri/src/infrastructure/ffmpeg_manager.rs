use serde::Serialize;
use sha2::{Digest, Sha256};
use std::path::{Path, PathBuf};
use std::process::Command;
use thiserror::Error;

const EMBEDDED_FFMPEG: &[u8] = include_bytes!(concat!(env!("OUT_DIR"), "/ffmpeg.exe"));
const EMBEDDED_FFPROBE: &[u8] = include_bytes!(concat!(env!("OUT_DIR"), "/ffprobe.exe"));

#[derive(Debug, Clone)]
pub struct ExtractedEngine {
    pub ffmpeg_path: PathBuf,
    pub ffprobe_path: PathBuf,
    pub cache_directory: PathBuf,
    pub sha256: String,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MediaEngineStatus {
    pub ready: bool,
    pub ffmpeg_path: String,
    pub ffprobe_path: String,
    pub version: String,
    pub sha256: String,
    pub cache_directory: String,
    pub error: Option<String>,
}

fn sha256_hex(bytes: &[u8]) -> String {
    hex::encode(Sha256::digest(bytes))
}

pub fn media_engine_cache_token() -> String {
    if !EMBEDDED_FFMPEG.is_empty() {
        return sha256_hex(EMBEDDED_FFMPEG);
    }
    get_ffmpeg_path()
        .and_then(|path| {
            let metadata = std::fs::metadata(&path).ok()?;
            let modified = metadata
                .modified()
                .ok()
                .and_then(|time| time.duration_since(std::time::UNIX_EPOCH).ok())
                .map(|duration| duration.as_nanos())
                .unwrap_or_default();
            Some(hex::encode(Sha256::digest(
                format!("{}:{}:{}", path.to_string_lossy(), metadata.len(), modified).as_bytes(),
            )))
        })
        .unwrap_or_else(|| "engine-unavailable".to_string())
}

fn write_verified_atomically(path: &Path, bytes: &[u8]) -> Result<(), FFmpegError> {
    let expected_hash = sha256_hex(bytes);
    let destination_is_valid = || {
        path.is_file()
            && std::fs::read(path)
                .map(|data| sha256_hex(&data) == expected_hash)
                .unwrap_or(false)
    };
    if destination_is_valid() {
        return Ok(());
    }
    let parent = path
        .parent()
        .ok_or_else(|| FFmpegError::ExecutionFailed("媒体引擎缓存目录无效".to_string()))?;
    std::fs::create_dir_all(parent)
        .map_err(|error| FFmpegError::ExecutionFailed(format!("无法创建媒体引擎缓存: {error}")))?;
    let temporary = parent.join(format!(
        ".{}.{}.tmp",
        path.file_name()
            .and_then(|name| name.to_str())
            .unwrap_or("engine"),
        uuid::Uuid::new_v4()
    ));
    std::fs::write(&temporary, bytes).map_err(|error| {
        FFmpegError::ExecutionFailed(format!(
            "媒体引擎写入失败，可能磁盘空间不足或目录无权限: {error}"
        ))
    })?;
    if sha256_hex(
        &std::fs::read(&temporary)
            .map_err(|error| FFmpegError::ExecutionFailed(error.to_string()))?,
    ) != expected_hash
    {
        let _ = std::fs::remove_file(&temporary);
        return Err(FFmpegError::ExecutionFailed(
            "媒体引擎写入后校验失败，已禁止执行".to_string(),
        ));
    }
    // Another process may have completed the same extraction while this process
    // was writing its temporary file. Keep the valid winner and discard ours.
    if destination_is_valid() {
        let _ = std::fs::remove_file(&temporary);
        return Ok(());
    }
    if path.exists() {
        std::fs::remove_file(path).map_err(|error| {
            FFmpegError::ExecutionFailed(format!("无法替换损坏的媒体引擎: {error}"))
        })?;
    }
    if let Err(error) = std::fs::rename(&temporary, path) {
        let _ = std::fs::remove_file(&temporary);
        if !destination_is_valid() {
            return Err(FFmpegError::ExecutionFailed(format!(
                "媒体引擎原子释放失败: {error}"
            )));
        }
    }
    Ok(())
}

fn extract_embedded_engine_to(
    root: &Path,
    ffmpeg: &[u8],
    ffprobe: &[u8],
) -> Result<ExtractedEngine, FFmpegError> {
    if ffmpeg.is_empty() || ffprobe.is_empty() {
        return Err(FFmpegError::NotFound(
            "当前开发构建未内置 FFmpeg/FFprobe".to_string(),
        ));
    }
    let ffmpeg_hash = sha256_hex(ffmpeg);
    let combined_hash = sha256_hex(&[ffmpeg, ffprobe].concat());
    let cache_directory = root.join(&combined_hash);
    let ffmpeg_path = cache_directory.join("ffmpeg.exe");
    let ffprobe_path = cache_directory.join("ffprobe.exe");
    write_verified_atomically(&ffmpeg_path, ffmpeg)?;
    write_verified_atomically(&ffprobe_path, ffprobe)?;
    Ok(ExtractedEngine {
        ffmpeg_path,
        ffprobe_path,
        cache_directory,
        sha256: ffmpeg_hash,
    })
}

fn default_engine_root() -> Option<PathBuf> {
    dirs::data_local_dir().map(|path| path.join("TinyPix").join("engine"))
}

fn ensure_embedded_engine() -> Result<ExtractedEngine, FFmpegError> {
    let root = default_engine_root()
        .ok_or_else(|| FFmpegError::NotFound("无法确定本地媒体引擎缓存目录".to_string()))?;
    extract_embedded_engine_to(&root, EMBEDDED_FFMPEG, EMBEDDED_FFPROBE)
}

fn media_engine_status_sync() -> MediaEngineStatus {
    match ensure_embedded_engine() {
        Ok(engine) => {
            let version = Command::new(&engine.ffmpeg_path)
                .arg("-version")
                .output()
                .ok()
                .filter(|output| output.status.success())
                .map(|output| {
                    String::from_utf8_lossy(&output.stdout)
                        .lines()
                        .next()
                        .unwrap_or("unknown")
                        .to_string()
                });
            MediaEngineStatus {
                ready: version.is_some(),
                ffmpeg_path: engine.ffmpeg_path.to_string_lossy().to_string(),
                ffprobe_path: engine.ffprobe_path.to_string_lossy().to_string(),
                version: version.clone().unwrap_or_default(),
                sha256: engine.sha256,
                cache_directory: engine.cache_directory.to_string_lossy().to_string(),
                error: version
                    .is_none()
                    .then(|| "内置媒体引擎无法启动，已禁止执行".to_string()),
            }
        }
        Err(error) => MediaEngineStatus {
            ready: false,
            ffmpeg_path: String::new(),
            ffprobe_path: String::new(),
            version: String::new(),
            sha256: EMBEDDED_FFMPEG
                .is_empty()
                .then(String::new)
                .unwrap_or_else(|| sha256_hex(EMBEDDED_FFMPEG)),
            cache_directory: default_engine_root()
                .map(|path| path.to_string_lossy().to_string())
                .unwrap_or_default(),
            error: Some(error.to_string()),
        },
    }
}

#[tauri::command]
pub async fn get_media_engine_status() -> MediaEngineStatus {
    tokio::task::spawn_blocking(media_engine_status_sync)
        .await
        .unwrap_or_else(|error| MediaEngineStatus {
            ready: false,
            ffmpeg_path: String::new(),
            ffprobe_path: String::new(),
            version: String::new(),
            sha256: String::new(),
            cache_directory: default_engine_root()
                .map(|path| path.to_string_lossy().to_string())
                .unwrap_or_default(),
            error: Some(format!("媒体引擎后台校验失败: {error}")),
        })
}

fn clear_media_engine_cache_sync() -> Result<(), String> {
    let root = default_engine_root().ok_or_else(|| "无法确定媒体引擎缓存目录".to_string())?;
    if root.exists() {
        std::fs::remove_dir_all(&root).map_err(|error| format!("清理媒体引擎缓存失败: {error}"))?;
    }
    Ok(())
}

#[tauri::command]
pub async fn clear_media_engine_cache() -> Result<(), String> {
    tokio::task::spawn_blocking(clear_media_engine_cache_sync)
        .await
        .map_err(|error| format!("清理媒体引擎缓存任务失败: {error}"))?
}

#[derive(Error, Debug)]
pub enum FFmpegError {
    #[error("FFmpeg 未找到: {0}")]
    NotFound(String),
    #[error("FFmpeg 执行失败: {0}")]
    ExecutionFailed(String),
    #[error("FFmpeg 超时: {0}")]
    Timeout(String),
    #[error("FFprobe 执行失败: {0}")]
    ProbeFailed(String),
}

/// 获取 FFmpeg 路径（跨平台）
/// Windows: ffmpeg.exe
/// macOS/Linux: ffmpeg
/// 搜索顺序：
///  1. 本程序同目录下的 ffmpeg
///  2. 本程序同目录 resources/ffmpeg
///  3. $APPDATA/TinyPix/sidecars/ffmpeg (Windows)
///  4. PATH 中的 ffmpeg
pub fn get_ffmpeg_path() -> Option<PathBuf> {
    let ffmpeg_name = if cfg!(target_os = "windows") {
        "ffmpeg.exe"
    } else {
        "ffmpeg"
    };

    if cfg!(target_os = "windows") {
        if let Ok(engine) = ensure_embedded_engine() {
            return Some(engine.ffmpeg_path);
        }
    }

    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            let candidates = vec![
                dir.join(ffmpeg_name),
                dir.join("resources").join(ffmpeg_name),
                dir.join("resources").join("ffmpeg").join(ffmpeg_name),
            ];

            #[cfg(not(target_os = "windows"))]
            let candidates = {
                let mut candidates = candidates;
                candidates.extend(vec![
                    dir.join("..").join("Resources").join(ffmpeg_name),
                    dir.join("..")
                        .join("Resources")
                        .join("ffmpeg")
                        .join(ffmpeg_name),
                    dir.join("..")
                        .join("Resources")
                        .join("resources")
                        .join(ffmpeg_name),
                    dir.join("..")
                        .join("Resources")
                        .join("resources")
                        .join("ffmpeg")
                        .join(ffmpeg_name),
                ]);
                candidates
            };

            for candidate in candidates {
                let normalized = candidate.canonicalize().unwrap_or(candidate);
                if normalized.exists() {
                    return Some(normalized);
                }
            }
        }
    }

    if let Some(data_dir) = dirs::data_dir() {
        let candidate = data_dir.join("TinyPix").join("sidecars").join(ffmpeg_name);
        if candidate.exists() {
            return Some(candidate);
        }
    }

    which::which(ffmpeg_name).ok()
}

/// 检查 FFmpeg 版本
pub fn check_ffmpeg_version() -> Result<String, FFmpegError> {
    let path = get_ffmpeg_path().ok_or_else(|| {
        FFmpegError::NotFound("FFmpeg 未在常见位置找到，请从设置下载".to_string())
    })?;

    let output = Command::new(&path)
        .arg("-version")
        .output()
        .map_err(|e| FFmpegError::ExecutionFailed(e.to_string()))?;

    let version = String::from_utf8_lossy(&output.stdout);
    Ok(version.lines().next().unwrap_or("unknown").to_string())
}

/// 进度解析：从 stderr 提取进度时间码
/// FFmpeg 输出格式：
/// frame=123 time=00:00:05.23 bitrate=1234.5kbits/s speed=2.3x
/// 解析 time=HH:MM:SS.ms 获取当前时间和总时长
/// 返回 current_secs（当前处理的秒数）
pub fn parse_progress(stderr: &str) -> Option<f64> {
    let time_line = stderr.lines().rev().find(|l| l.contains("time="))?;
    let time_str = time_line.split("time=").nth(1)?.split_whitespace().next()?;
    let parts: Vec<&str> = time_str.split(':').collect();
    if parts.len() == 3 {
        let hours: f64 = parts[0].parse().ok()?;
        let mins: f64 = parts[1].parse().ok()?;
        let secs: f64 = parts[2].replace('s', "").parse().ok()?;
        return Some(hours * 3600.0 + mins * 60.0 + secs);
    }
    None
}

/// 获取 ffprobe 路径
pub fn get_ffprobe_path() -> Option<PathBuf> {
    if cfg!(target_os = "windows") {
        if let Ok(engine) = ensure_embedded_engine() {
            return Some(engine.ffprobe_path);
        }
    }
    let ffmpeg = get_ffmpeg_path()?;
    let parent = ffmpeg.parent()?.to_path_buf();
    if cfg!(target_os = "windows") {
        Some(parent.join("ffprobe.exe"))
    } else {
        Some(parent.join("ffprobe"))
    }
}

/// 解析 ffprobe JSON 输出获取视频信息
#[derive(Debug, serde::Serialize)]
pub struct VideoProbeInfo {
    pub duration_secs: f64,
    pub width: u32,
    pub height: u32,
    pub bitrate_kbps: u32,
    pub fps: f64,
    /// Backwards-compatible alias for `video_codec`.
    pub codec: String,
    /// Backwards-compatible alias for `container`.
    pub format: String,
    pub container: String,
    pub video_codec: String,
    pub audio_codec: Option<String>,
    pub has_audio: bool,
    pub rotation: i32,
}

pub fn parse_video_probe(json: &str) -> Result<VideoProbeInfo, FFmpegError> {
    #[derive(serde::Deserialize)]
    struct ProbeFormat {
        #[serde(rename = "bit_rate")]
        bit_rate: Option<String>,
        #[serde(rename = "format_name")]
        format_name: Option<String>,
        duration: Option<String>,
    }
    #[derive(serde::Deserialize)]
    struct ProbeDisposition {
        attached_pic: Option<i32>,
    }
    #[derive(serde::Deserialize)]
    struct ProbeSideData {
        rotation: Option<i32>,
    }
    #[derive(serde::Deserialize)]
    struct ProbeTags {
        rotate: Option<String>,
    }
    #[derive(serde::Deserialize)]
    struct ProbeStream {
        codec_type: Option<String>,
        width: Option<u32>,
        height: Option<u32>,
        codec_name: Option<String>,
        avg_frame_rate: Option<String>,
        r_frame_rate: Option<String>,
        duration: Option<String>,
        duration_ts: Option<serde_json::Value>,
        time_base: Option<String>,
        disposition: Option<ProbeDisposition>,
        side_data_list: Option<Vec<ProbeSideData>>,
        tags: Option<ProbeTags>,
    }
    #[derive(serde::Deserialize)]
    struct ProbeOutput {
        format: Option<ProbeFormat>,
        streams: Option<Vec<ProbeStream>>,
    }

    let output: ProbeOutput =
        serde_json::from_str(json).map_err(|e| FFmpegError::ProbeFailed(e.to_string()))?;

    let format = output
        .format
        .ok_or_else(|| FFmpegError::ProbeFailed("缺少 format".to_string()))?;
    let streams = output
        .streams
        .ok_or_else(|| FFmpegError::ProbeFailed("缺少 streams".to_string()))?;
    let video = streams
        .iter()
        .find(|stream| {
            let has_video_dimensions =
                stream.width.unwrap_or(0) > 0 && stream.height.unwrap_or(0) > 0;
            let is_video = stream.codec_type.as_deref() == Some("video")
                || (stream.codec_type.is_none() && has_video_dimensions);
            let is_attached_picture = stream
                .disposition
                .as_ref()
                .and_then(|disposition| disposition.attached_pic)
                == Some(1);
            is_video && has_video_dimensions && !is_attached_picture
        })
        .ok_or_else(|| FFmpegError::ProbeFailed("未找到视频流".to_string()))?;

    let duration_secs = format
        .duration
        .as_deref()
        .and_then(parse_positive_number)
        .or_else(|| video.duration.as_deref().and_then(parse_positive_number))
        .or_else(|| {
            let duration_ts = video.duration_ts.as_ref().and_then(parse_json_number)?;
            let time_base = video.time_base.as_deref().and_then(parse_ratio)?;
            let duration = duration_ts * time_base;
            duration
                .is_finite()
                .then_some(duration)
                .filter(|value| *value >= 0.0)
        })
        .unwrap_or(0.0);
    let bitrate_kbps = format
        .bit_rate
        .and_then(|s| s.parse::<u32>().ok())
        .unwrap_or(0)
        / 1000;
    let fps = video
        .avg_frame_rate
        .as_deref()
        .map(parse_fps)
        .filter(|fps| *fps > 0.0)
        .or_else(|| video.r_frame_rate.as_deref().map(parse_fps))
        .unwrap_or(0.0);
    let video_codec = video.codec_name.clone().unwrap_or_default();
    let container = format.format_name.unwrap_or_default();
    let audio_codec = streams
        .iter()
        .find(|stream| stream.codec_type.as_deref() == Some("audio"))
        .and_then(|stream| stream.codec_name.clone());
    let has_audio = streams
        .iter()
        .any(|stream| stream.codec_type.as_deref() == Some("audio"));
    let rotation = video
        .side_data_list
        .as_ref()
        .and_then(|items| items.iter().find_map(|item| item.rotation))
        .or_else(|| {
            video
                .tags
                .as_ref()
                .and_then(|tags| tags.rotate.as_deref())
                .and_then(|value| value.parse::<i32>().ok())
        })
        .unwrap_or(0);

    Ok(VideoProbeInfo {
        duration_secs,
        width: video.width.unwrap_or(0),
        height: video.height.unwrap_or(0),
        bitrate_kbps,
        fps,
        codec: video_codec.clone(),
        format: container.clone(),
        container,
        video_codec,
        audio_codec,
        has_audio,
        rotation,
    })
}

fn parse_positive_number(value: &str) -> Option<f64> {
    let value = value.parse::<f64>().ok()?;
    value
        .is_finite()
        .then_some(value)
        .filter(|value| *value >= 0.0)
}

fn parse_json_number(value: &serde_json::Value) -> Option<f64> {
    value
        .as_f64()
        .or_else(|| value.as_str().and_then(|text| text.parse::<f64>().ok()))
        .filter(|value| value.is_finite())
}

fn parse_ratio(value: &str) -> Option<f64> {
    let (numerator, denominator) = value.split_once('/')?;
    let numerator = numerator.parse::<f64>().ok()?;
    let denominator = denominator.parse::<f64>().ok()?;
    (denominator != 0.0).then_some(numerator / denominator)
}

/// 解析帧率字符串 "30/1" -> 30.0
fn parse_fps(fps_str: &str) -> f64 {
    let parts: Vec<&str> = fps_str.split('/').collect();
    if parts.len() == 2 {
        let num: f64 = parts[0].parse().unwrap_or(0.0);
        let den: f64 = parts[1].parse().unwrap_or(1.0);
        if den > 0.0 {
            return num / den;
        }
    }
    fps_str.parse().unwrap_or(0.0)
}

/// 从 ffprobe JSON 输出中提取视频时长（秒）
pub fn parse_duration_from_probe(json: &str) -> Option<f64> {
    #[derive(serde::Deserialize)]
    struct ProbeFormat {
        duration: Option<String>,
    }
    #[derive(serde::Deserialize)]
    struct ProbeRoot {
        format: Option<ProbeFormat>,
    }
    let root: ProbeRoot = serde_json::from_str(json).ok()?;
    root.format?.duration?.parse().ok()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn embedded_engine_hash_is_stable_and_content_sensitive() {
        assert_eq!(sha256_hex(b"TinyPix"), sha256_hex(b"TinyPix"));
        assert_ne!(sha256_hex(b"TinyPix"), sha256_hex(b"tinypix"));
        assert_eq!(sha256_hex(b"TinyPix").len(), 64);
    }

    #[test]
    fn embedded_engine_extracts_atomically_and_repairs_corruption() {
        let root =
            std::env::temp_dir().join(format!("tinypix-engine-test-{}", uuid::Uuid::new_v4()));
        let first = extract_embedded_engine_to(&root, b"ffmpeg-binary", b"ffprobe-binary").unwrap();
        assert_eq!(std::fs::read(&first.ffmpeg_path).unwrap(), b"ffmpeg-binary");
        std::fs::write(&first.ffmpeg_path, b"broken").unwrap();
        let repaired =
            extract_embedded_engine_to(&root, b"ffmpeg-binary", b"ffprobe-binary").unwrap();
        assert_eq!(
            std::fs::read(&repaired.ffmpeg_path).unwrap(),
            b"ffmpeg-binary"
        );
        let _ = std::fs::remove_dir_all(root);
    }

    // ============================================================
    // parse_progress 测试
    // ============================================================

    #[test]
    fn test_parse_progress_valid() {
        let stderr = "frame=123 time=00:00:05.23 bitrate=1234.5kbits/s speed=2.3x";
        let result = parse_progress(stderr);
        assert!(result.is_some());
        let secs = result.unwrap();
        assert!((secs - 5.23).abs() < 0.001);
    }

    #[test]
    fn test_parse_progress_valid_hours() {
        let stderr = "frame=123 time=01:30:45.50 bitrate=1234.5kbits/s speed=2.3x";
        let result = parse_progress(stderr);
        assert!(result.is_some());
        let secs = result.unwrap();
        assert!((secs - (1.0 * 3600.0 + 30.0 * 60.0 + 45.5)).abs() < 0.001);
    }

    #[test]
    fn test_parse_progress_no_time() {
        let stderr = "frame=123 bitrate=1234.5kbits/s speed=2.3x";
        let result = parse_progress(stderr);
        assert!(result.is_none());
    }

    #[test]
    fn test_parse_progress_empty() {
        let result = parse_progress("");
        assert!(result.is_none());
    }

    #[test]
    fn test_parse_progress_invalid_time_format() {
        let stderr = "frame=123 time=abc bitrate=1234.5kbits/s";
        let result = parse_progress(stderr);
        assert!(result.is_none());
    }

    // ============================================================
    // parse_fps 测试
    // ============================================================

    #[test]
    fn test_parse_fps_fraction_30() {
        assert_eq!(parse_fps("30/1"), 30.0);
    }

    #[test]
    fn test_parse_fps_whole_25() {
        assert_eq!(parse_fps("25"), 25.0);
    }

    #[test]
    fn test_parse_fps_fraction_60() {
        assert_eq!(parse_fps("60/1"), 60.0);
    }

    #[test]
    fn test_parse_fps_fraction_2997() {
        assert!((parse_fps("30000/1001") - 29.97002997003).abs() < 0.0001);
    }

    #[test]
    fn test_parse_fps_empty() {
        assert_eq!(parse_fps(""), 0.0);
    }

    #[test]
    fn test_parse_fps_zero_denominator() {
        assert_eq!(parse_fps("30/0"), 0.0);
    }

    #[test]
    fn test_parse_fps_invalid_string() {
        assert_eq!(parse_fps("abc"), 0.0);
    }

    // ============================================================
    // parse_video_probe 测试
    // ============================================================

    #[test]
    fn test_parse_video_probe_valid() {
        let json = r#"{
            "format": {
                "bit_rate": "5000000",
                "format_name": "mov,mp4,m4a,3gp,3g2,mj2",
                "duration": "120.5"
            },
            "streams": [
                {
                    "width": 1920,
                    "height": 1080,
                    "codec_name": "h264",
                    "r_frame_rate": "30/1"
                }
            ]
        }"#;

        let info = parse_video_probe(json).unwrap();
        assert_eq!(info.duration_secs, 120.5);
        assert_eq!(info.width, 1920);
        assert_eq!(info.height, 1080);
        assert_eq!(info.bitrate_kbps, 5000);
        assert_eq!(info.fps, 30.0);
        assert_eq!(info.codec, "h264");
        assert_eq!(info.format, "mov,mp4,m4a,3gp,3g2,mj2");
    }

    #[test]
    fn test_parse_video_probe_missing_format() {
        let json = r#"{"streams": []}"#;
        let result = parse_video_probe(json);
        assert!(result.is_err());
    }

    #[test]
    fn test_parse_video_probe_missing_streams() {
        let json = r#"{"format": {"duration": "10.0"}}"#;
        let result = parse_video_probe(json);
        assert!(result.is_err());
    }

    #[test]
    fn test_parse_video_probe_no_video_stream() {
        let json = r#"{
            "format": {"duration": "10.0"},
            "streams": [{"width": 0, "height": 0}]
        }"#;
        let result = parse_video_probe(json);
        assert!(result.is_err());
    }

    #[test]
    fn test_parse_video_probe_default_values() {
        let json = r#"{
            "format": {
                "format_name": "mp4"
            },
            "streams": [
                {
                    "width": 1280,
                    "height": 720
                }
            ]
        }"#;

        let info = parse_video_probe(json).unwrap();
        assert_eq!(info.duration_secs, 0.0);
        assert_eq!(info.bitrate_kbps, 0);
        assert_eq!(info.fps, 0.0);
        assert_eq!(info.codec, "");
    }

    #[test]
    fn test_parse_video_probe_accepts_video_and_audio_streams() {
        let json = r#"{
            "format": {
                "duration": "15.25",
                "format_name": "mov,mp4,m4a,3gp,3g2,mj2"
            },
            "streams": [
                {
                    "codec_type": "video",
                    "codec_name": "hevc",
                    "width": 1920,
                    "height": 1080,
                    "avg_frame_rate": "30000/1001",
                    "side_data_list": [{"rotation": -90}]
                },
                {
                    "codec_type": "audio",
                    "codec_name": "aac",
                    "sample_rate": "48000",
                    "channels": 2
                }
            ]
        }"#;

        let info = parse_video_probe(json).unwrap();
        assert_eq!(info.video_codec, "hevc");
        assert_eq!(info.audio_codec.as_deref(), Some("aac"));
        assert!(info.has_audio);
        assert_eq!(info.rotation, -90);
        assert!((info.fps - 29.97002997003).abs() < 0.0001);
        assert_eq!(info.container, "mov,mp4,m4a,3gp,3g2,mj2");
    }

    #[test]
    fn test_parse_video_probe_accepts_video_only_streams() {
        let json = r#"{
            "format": {"duration": "3.0", "format_name": "matroska,webm"},
            "streams": [{
                "codec_type": "video",
                "codec_name": "vp9",
                "width": 1280,
                "height": 720,
                "r_frame_rate": "24/1"
            }]
        }"#;

        let info = parse_video_probe(json).unwrap();
        assert!(!info.has_audio);
        assert_eq!(info.audio_codec, None);
        assert_eq!(info.video_codec, "vp9");
    }

    #[test]
    fn test_parse_video_probe_ignores_attached_picture_stream() {
        let json = r#"{
            "format": {"duration": "8.0"},
            "streams": [
                {
                    "codec_type": "video",
                    "codec_name": "mjpeg",
                    "width": 600,
                    "height": 600,
                    "disposition": {"attached_pic": 1}
                },
                {
                    "codec_type": "video",
                    "codec_name": "h264",
                    "width": 1920,
                    "height": 1080,
                    "avg_frame_rate": "25/1",
                    "disposition": {"attached_pic": 0}
                }
            ]
        }"#;

        let info = parse_video_probe(json).unwrap();
        assert_eq!((info.width, info.height), (1920, 1080));
        assert_eq!(info.video_codec, "h264");
    }

    #[test]
    fn test_parse_video_probe_falls_back_to_stream_duration() {
        let json = r#"{
            "format": {"format_name": "mpegts"},
            "streams": [{
                "codec_type": "video",
                "width": 720,
                "height": 576,
                "duration": "42.75"
            }]
        }"#;

        let info = parse_video_probe(json).unwrap();
        assert_eq!(info.duration_secs, 42.75);
    }

    #[test]
    fn test_parse_video_probe_falls_back_to_duration_ts_and_time_base() {
        let json = r#"{
            "format": {},
            "streams": [{
                "codec_type": "video",
                "width": 640,
                "height": 360,
                "duration_ts": 900900,
                "time_base": "1/90000"
            }]
        }"#;

        let info = parse_video_probe(json).unwrap();
        assert!((info.duration_secs - 10.01).abs() < 0.0001);
    }

    #[test]
    fn test_parse_video_probe_uses_average_frame_rate_for_vfr() {
        let json = r#"{
            "format": {"duration": "60"},
            "streams": [{
                "codec_type": "video",
                "width": 3840,
                "height": 2160,
                "avg_frame_rate": "24000/1001",
                "r_frame_rate": "60/1"
            }]
        }"#;

        let info = parse_video_probe(json).unwrap();
        assert!((info.fps - 23.976023976).abs() < 0.0001);
    }

    // ============================================================
    // parse_duration_from_probe 测试
    // ============================================================

    #[test]
    fn test_parse_duration_from_probe_valid() {
        let json = r#"{"format": {"duration": "123.45"}}"#;
        let result = parse_duration_from_probe(json);
        assert!(result.is_some());
        assert!((result.unwrap() - 123.45).abs() < 0.001);
    }

    #[test]
    fn test_parse_duration_from_probe_missing() {
        let json = r#"{"format": {}}"#;
        let result = parse_duration_from_probe(json);
        assert!(result.is_none());
    }

    #[test]
    fn test_parse_duration_from_probe_invalid_json() {
        let result = parse_duration_from_probe("not json");
        assert!(result.is_none());
    }
}
