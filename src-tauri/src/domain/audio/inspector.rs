use std::path::Path;
use std::process::Command;

use crate::infrastructure::ffmpeg_manager::{get_ffprobe_path, FFmpegError};

/// 音频源信息（与前端 AudioSourceInfo 对齐）
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct AudioSourceInfo {
    pub codec: String,
    pub sample_rate: u32,
    pub channels: u8,
    pub duration_secs: f64,
    pub bitrate_kbps: Option<u32>,
}

impl AudioSourceInfo {
    pub fn unknown() -> Self {
        Self {
            codec: "unknown".to_string(),
            sample_rate: 0,
            channels: 2,
            duration_secs: 0.0,
            bitrate_kbps: None,
        }
    }
}

#[derive(Debug, serde::Deserialize)]
struct FfprobeStream {
    codec_name: Option<String>,
    sample_rate: Option<String>,
    channels: Option<u32>,
    duration: Option<String>,
    bit_rate: Option<String>,
}

#[derive(Debug, serde::Deserialize)]
struct FfprobeOutput {
    streams: Option<Vec<FfprobeStream>>,
}

/// AudioInspector — 解析视频文件中的音频流信息
pub trait AudioInspector: Send + Sync {
    fn inspect(&self, path: &str) -> Result<AudioSourceInfo, String>;
}

/// 使用 ffprobe 的实现
pub struct FfprobeAudioInspector;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_audio_source_info_unknown() {
        let info = AudioSourceInfo::unknown();
        assert_eq!(info.codec, "unknown");
        assert_eq!(info.sample_rate, 0);
        assert_eq!(info.channels, 2);
        assert_eq!(info.duration_secs, 0.0);
        assert_eq!(info.bitrate_kbps, None);
    }
}

impl AudioInspector for FfprobeAudioInspector {
    fn inspect(&self, path: &str) -> Result<AudioSourceInfo, String> {
        let probe_path = get_ffprobe_path()
            .ok_or_else(|| FFmpegError::NotFound("ffprobe 未找到".to_string()).to_string())?;

        if !Path::new(path).exists() {
            return Err(format!("文件不存在: {path}"));
        }

        let output = Command::new(&probe_path)
            .args([
                "-v",
                "quiet",
                "-print_format",
                "json",
                "-show_streams",
                "-select_streams",
                "a:0",
                path,
            ])
            .output()
            .map_err(|e| e.to_string())?;

        if !output.status.success() {
            return Err(String::from_utf8_lossy(&output.stderr).to_string());
        }

        let json = String::from_utf8_lossy(&output.stdout);
        let parsed: FfprobeOutput =
            serde_json::from_str(&json).map_err(|e| format!("ffprobe JSON 解析失败: {e}"))?;

        let stream = parsed
            .streams
            .and_then(|s| s.into_iter().next())
            .ok_or_else(|| "未找到音频流".to_string())?;

        Ok(AudioSourceInfo {
            codec: stream.codec_name.unwrap_or_else(|| "unknown".to_string()),
            sample_rate: stream.sample_rate.and_then(|s| s.parse().ok()).unwrap_or(0),
            channels: stream.channels.map(|c| c as u8).unwrap_or(2),
            duration_secs: stream.duration.and_then(|s| s.parse().ok()).unwrap_or(0.0),
            bitrate_kbps: stream
                .bit_rate
                .and_then(|s| s.parse::<u32>().ok())
                .map(|b| b / 1000),
        })
    }
}
