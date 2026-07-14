use std::process::{Command, Stdio};

use tauri::Emitter;

use crate::domain::audio::AudioOperationResult;
use crate::infrastructure::ffmpeg_manager::{get_ffmpeg_path, parse_progress, FFmpegError};

/// 构造音频转码的 FFmpeg 参数列表（纯函数，可单元测试）
///
/// 支持任意 codec（MP3/AAC/FLAC/WAV/M4A 等）和可选比特率
/// `-vn` 强制无视频流
pub fn build_extract_args(
    input_path: &str,
    output_path: &str,
    codec: &str,
    bitrate_kbps: Option<u16>,
) -> Vec<String> {
    let mut args: Vec<String> = vec![
        String::from("-y"),
        String::from("-i"),
        input_path.to_string(),
        String::from("-vn"),
        String::from("-c:a"),
        codec.to_string(),
    ];
    if let Some(br) = bitrate_kbps {
        args.push(String::from("-b:a"));
        args.push(format!("{}k", br));
    }
    args.push(output_path.to_string());
    args
}

/// 构造音频流复制（无损提取）的 FFmpeg 参数列表（纯函数）
pub fn build_extract_copy_args(input_path: &str, output_path: &str) -> Vec<String> {
    vec![
        String::from("-y"),
        String::from("-i"),
        input_path.to_string(),
        String::from("-vn"),
        String::from("-c:a"),
        String::from("copy"),
        output_path.to_string(),
    ]
}

pub struct ConvertContext<'a> {
    pub input_path: &'a str,
    pub output_path: &'a str,
    pub total_secs: f64,
    pub app: &'a tauri::AppHandle,
}

pub struct ConvertConfig {
    pub codec: String,
    pub bitrate_kbps: Option<u16>,
}

pub trait AudioFormatConverter: Send + Sync {
    fn convert(&self, ctx: ConvertContext, config: &ConvertConfig) -> Result<AudioOperationResult, String>;
}

/// FfmpegFormatConverter — 音频转码（MP3/AAC/FLAC/WAV/M4A）
pub struct FfmpegFormatConverter;

impl AudioFormatConverter for FfmpegFormatConverter {
    fn convert(&self, ctx: ConvertContext, config: &ConvertConfig) -> Result<AudioOperationResult, String> {
        let ffmpeg = get_ffmpeg_path().ok_or_else(|| {
            FFmpegError::NotFound("ffmpeg 未找到".to_string()).to_string()
        })?;

        let start = std::time::Instant::now();
        let original_size = std::fs::metadata(ctx.input_path)
            .map_err(|e| format!("读取原文件失败: {e}"))?
            .len();

        let args = build_extract_args(
            ctx.input_path,
            ctx.output_path,
            &config.codec,
            config.bitrate_kbps,
        );

        let mut child = Command::new(&ffmpeg)
            .args(&args)
            .stderr(Stdio::piped())
            .stdout(Stdio::null())
            .spawn()
            .map_err(|e| format!("启动 ffmpeg 失败: {e}"))?;

        if let Some(stderr) = child.stderr.take() {
            use std::io::{BufRead, BufReader};
            let reader = BufReader::new(stderr);
            for line in reader.lines().flatten() {
                if let Some(current) = parse_progress(&line) {
                    if ctx.total_secs > 0.0 {
                        let pct = (current / ctx.total_secs * 100.0).min(99.0);
                        let _ = ctx.app.emit("video-progress", pct);
                    }
                }
            }
        }

        let status = child.wait().map_err(|e| e.to_string())?;
        if !status.success() {
            return Err(format!("ffmpeg 执行失败, exit code: {:?}", status.code()));
        }

        let output_size = std::fs::metadata(ctx.output_path)
            .map(|m| m.len())
            .unwrap_or(0);
        let processing_time = start.elapsed().as_secs_f64();

        let _ = ctx.app.emit("video-progress", 100.0);

        Ok(AudioOperationResult {
            output_path: ctx.output_path.to_string(),
            original_size,
            output_size,
            saved_bytes: original_size.saturating_sub(output_size),
            processing_time_secs: processing_time,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_build_extract_args_mp3() {
        let args = build_extract_args("/in.mp4", "/out.mp3", "libmp3lame", Some(192));
        assert!(args.contains(&"-i".to_string()));
        assert!(args.contains(&"/in.mp4".to_string()));
        assert!(args.contains(&"-vn".to_string()));
        assert!(args.contains(&"-c:a".to_string()));
        assert!(args.contains(&"libmp3lame".to_string()));
        assert!(args.contains(&"-b:a".to_string()));
        assert!(args.contains(&"192k".to_string()));
        assert!(args.iter().any(|a| a == "/out.mp3"));
    }

    #[test]
    fn test_build_extract_args_flac_no_bitrate() {
        let args = build_extract_args("/in.wav", "/out.flac", "flac", None);
        assert!(args.contains(&"flac".to_string()));
        // FLAC 无比特率参数
        assert!(!args.contains(&"-b:a".to_string()));
        assert!(args.iter().any(|a| a == "/out.flac"));
    }

    #[test]
    fn test_build_extract_args_aac_high_bitrate() {
        let args = build_extract_args("/in.mp4", "/out.aac", "aac", Some(320));
        assert!(args.contains(&"aac".to_string()));
        assert!(args.contains(&"320k".to_string()));
    }

    #[test]
    fn test_build_extract_args_starts_with_y() {
        let args = build_extract_args("/in.mp4", "/out.mp3", "libmp3lame", Some(128));
        // 第一项必须是 -y（覆盖输出）
        assert_eq!(args[0], "-y");
    }

    #[test]
    fn test_build_extract_copy_args() {
        let args = build_extract_copy_args("/in.mp4", "/out.aac");
        assert_eq!(args[0], "-y");
        assert!(args.contains(&"-vn".to_string()));
        assert!(args.contains(&"-c:a".to_string()));
        assert!(args.contains(&"copy".to_string()));
        // copy 模式不应用 -b:a
        assert!(!args.contains(&"-b:a".to_string()));
    }
}
