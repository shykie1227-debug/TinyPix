use std::process::{Command, Stdio};

use tauri::Emitter;

use crate::domain::audio::AudioOperationResult;
use crate::infrastructure::ffmpeg_manager::{get_ffmpeg_path, parse_progress, FFmpegError};

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

pub struct ExtractContext<'a> {
    pub input_path: &'a str,
    pub output_path: &'a str,
    pub total_secs: f64,
    pub app: &'a tauri::AppHandle,
}

pub trait AudioStreamExtractor: Send + Sync {
    fn extract(&self, ctx: ExtractContext) -> Result<AudioOperationResult, String>;
}

/// FfmpegStreamExtractor — -c:a copy 模式，无损提取
pub struct FfmpegStreamExtractor;

impl AudioStreamExtractor for FfmpegStreamExtractor {
    fn extract(&self, ctx: ExtractContext) -> Result<AudioOperationResult, String> {
        let ffmpeg = get_ffmpeg_path().ok_or_else(|| {
            FFmpegError::NotFound("ffmpeg 未找到".to_string()).to_string()
        })?;

        let start = std::time::Instant::now();
        let original_size = std::fs::metadata(ctx.input_path)
            .map_err(|e| format!("读取原文件失败: {e}"))?
            .len();

        let args = build_extract_copy_args(ctx.input_path, ctx.output_path);

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
    fn test_build_extract_copy_args_structure() {
        let args = build_extract_copy_args("/input.mp4", "/output.aac");
        assert_eq!(args[0], "-y");
        assert_eq!(args[1], "-i");
        assert_eq!(args[2], "/input.mp4");
        assert_eq!(args[3], "-vn");
        assert_eq!(args[4], "-c:a");
        assert_eq!(args[5], "copy");
        assert_eq!(args[6], "/output.aac");
    }

    #[test]
    fn test_build_extract_copy_args_no_bitrate() {
        let args = build_extract_copy_args("/in.mkv", "/out.m4a");
        // copy 模式不应该有 -b:a
        assert!(!args.contains(&"-b:a".to_string()));
        assert!(!args.contains(&"libmp3lame".to_string()));
    }
}
