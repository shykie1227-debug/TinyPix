use std::path::PathBuf;
use std::process::Command;
use thiserror::Error;

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
                    dir.join("..").join("Resources").join("ffmpeg").join(ffmpeg_name),
                    dir.join("..").join("Resources").join("resources").join(ffmpeg_name),
                    dir.join("..").join("Resources").join("resources").join("ffmpeg").join(ffmpeg_name),
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
    pub codec: String,
    pub format: String,
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
    struct ProbeVideo {
        width: u32,
        height: u32,
        codec_name: Option<String>,
        r_frame_rate: Option<String>,
    }
    #[derive(serde::Deserialize)]
    struct ProbeOutput {
        format: Option<ProbeFormat>,
        streams: Option<Vec<ProbeVideo>>,
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
        .into_iter()
        .find(|s| s.width > 0 && s.height > 0)
        .ok_or_else(|| FFmpegError::ProbeFailed("未找到视频流".to_string()))?;

    let duration_secs = format
        .duration
        .and_then(|s| s.parse::<f64>().ok())
        .unwrap_or(0.0);
    let bitrate_kbps = format
        .bit_rate
        .and_then(|s| s.parse::<u32>().ok())
        .unwrap_or(0)
        / 1000;
    let fps = parse_fps(&video.r_frame_rate.unwrap_or_default());

    Ok(VideoProbeInfo {
        duration_secs,
        width: video.width,
        height: video.height,
        bitrate_kbps,
        fps,
        codec: video.codec_name.unwrap_or_default(),
        format: format.format_name.unwrap_or_default(),
    })
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
