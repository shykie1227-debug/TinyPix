use std::path::PathBuf;
use std::process::Stdio;
use tokio::io::AsyncBufReadExt;
use tokio::process::Command;

use crate::infrastructure::error::{TinyPixError, TinyPixResult};
use crate::infrastructure::ffmpeg_manager::{
    get_ffmpeg_path, get_ffprobe_path, parse_duration_from_probe, parse_progress,
};

/// 进度快照
#[derive(Debug, Clone)]
pub struct ProgressSnapshot {
    pub current_secs: f64,
    pub progress_pct: f64,
}

/// FFmpeg 执行器：封装 FFmpeg/FFprobe 的进程执行与进度追踪
///
/// 职责边界：
/// - 只负责进程生命周期（spawn / stderr 读取 / wait）
/// - 不构造 FFmpeg 参数（参数由 commands/video_commands.rs 中的纯函数生成）
/// - 不直接 emit Tauri 事件（通过回调将进度交给调用方）
pub struct FFmpegRunner {
    ffmpeg: PathBuf,
    ffprobe: PathBuf,
}

impl FFmpegRunner {
    /// 创建新的 Runner，自动查找 FFmpeg/FFprobe
    pub fn new() -> TinyPixResult<Self> {
        let ffmpeg = get_ffmpeg_path().ok_or(TinyPixError::Io("FFmpeg 未找到".to_string()))?;
        let ffprobe = get_ffprobe_path().ok_or(TinyPixError::Io("FFprobe 未找到".to_string()))?;
        Ok(Self { ffmpeg, ffprobe })
    }

    /// 探测视频时长（秒）
    pub async fn probe_duration(&self, input_path: &str) -> TinyPixResult<f64> {
        let json = self
            .run_ffprobe(&[
                "-v", "quiet", "-print_format", "json", "-show_format", input_path,
            ])
            .await?;
        parse_duration_from_probe(&json)
            .ok_or_else(|| TinyPixError::Processing("无法解析视频时长".to_string()))
    }

    /// 执行 FFmpeg 并实时追踪进度
    ///
    /// `total_secs` 用于计算进度百分比。
    /// `on_progress` 每 5% 或进度回退时触发一次（与原有节流策略保持一致）。
    pub async fn run_with_progress<F>(
        &self,
        args: &[String],
        total_secs: f64,
        mut on_progress: F,
    ) -> TinyPixResult<()>
    where
        F: FnMut(ProgressSnapshot),
    {
        let mut child = Command::new(&self.ffmpeg)
            .args(args)
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .map_err(|e| TinyPixError::Processing(format!("启动 FFmpeg 失败: {}", e)))?;

        let stderr = child.stderr.take().unwrap();
        let mut reader = tokio::io::BufReader::new(stderr).lines();
        let mut last_progress = 0.0;
        let mut full_stderr = String::new();

        while let Ok(Some(line)) = reader.next_line().await {
            full_stderr.push_str(&line);
            full_stderr.push('\n');

            if let Some(current) = parse_progress(&full_stderr) {
                let progress = if total_secs > 0.0 {
                    (current / total_secs * 100.0).min(99.0)
                } else {
                    0.0
                };

                if progress - last_progress >= 5.0 || progress < last_progress {
                    last_progress = progress;
                    on_progress(ProgressSnapshot {
                        current_secs: current,
                        progress_pct: progress,
                    });
                }
            }
        }

        let status = child.wait().await.map_err(|e| TinyPixError::Io(e.to_string()))?;

        if !status.success() {
            return Err(TinyPixError::Processing(format!("FFmpeg 执行失败: {}", status)));
        }

        Ok(())
    }

    /// 简单执行 FFmpeg（无进度追踪），返回完整输出
    pub async fn run_simple(&self, args: &[String]) -> TinyPixResult<std::process::Output> {
        let output = Command::new(&self.ffmpeg)
            .args(args)
            .output()
            .await
            .map_err(|e| TinyPixError::Processing(format!("FFmpeg 执行失败: {}", e)))?;

        if !output.status.success() {
            return Err(TinyPixError::Processing(format!(
                "FFmpeg 失败: {}",
                String::from_utf8_lossy(&output.stderr)
            )));
        }

        Ok(output)
    }

    /// 执行 FFprobe，返回 stdout 文本
    pub async fn run_ffprobe(&self, args: &[&str]) -> TinyPixResult<String> {
        let output = Command::new(&self.ffprobe)
            .args(args)
            .output()
            .await
            .map_err(|e| TinyPixError::Processing(format!("FFprobe 执行失败: {}", e)))?;

        if !output.status.success() {
            return Err(TinyPixError::Processing(format!(
                "FFprobe 失败: {}",
                String::from_utf8_lossy(&output.stderr)
            )));
        }

        Ok(String::from_utf8_lossy(&output.stdout).to_string())
    }

    /// 获取 FFmpeg 路径
    pub fn ffmpeg_path(&self) -> &PathBuf {
        &self.ffmpeg
    }

    /// 获取 FFprobe 路径
    pub fn ffprobe_path(&self) -> &PathBuf {
        &self.ffprobe
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_progress_snapshot_construction() {
        let snapshot = ProgressSnapshot {
            current_secs: 5.5,
            progress_pct: 45.2,
        };
        assert_eq!(snapshot.current_secs, 5.5);
        assert_eq!(snapshot.progress_pct, 45.2);
    }

    #[test]
    fn test_progress_snapshot_zero_values() {
        let snapshot = ProgressSnapshot {
            current_secs: 0.0,
            progress_pct: 0.0,
        };
        assert_eq!(snapshot.current_secs, 0.0);
        assert_eq!(snapshot.progress_pct, 0.0);
    }

    #[test]
    fn test_ffmpeg_runner_new_fails_when_ffmpeg_not_found() {
        // In a test environment without FFmpeg in PATH or sidecars,
        // FFmpegRunner::new() should return an error.
        let result = FFmpegRunner::new();
        // We can't assert it definitely fails because some CI environments
        // may have FFmpeg installed, but we can at least exercise the code path.
        match result {
            Ok(runner) => {
                // If FFmpeg is available, verify the paths are non-empty.
                assert!(!runner.ffmpeg_path().as_os_str().is_empty());
                assert!(!runner.ffprobe_path().as_os_str().is_empty());
            }
            Err(e) => {
                // Expect an Io error about FFmpeg not being found.
                let msg = format!("{}", e);
                assert!(
                    msg.contains("FFmpeg 未找到") || msg.contains("FFprobe 未找到"),
                    "Unexpected error message: {}",
                    msg
                );
            }
        }
    }
}
