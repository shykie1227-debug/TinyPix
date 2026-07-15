use crate::infrastructure::ffmpeg_manager::{
    media_engine_cache_token, parse_video_probe, VideoProbeInfo,
};
use crate::infrastructure::ffmpeg_runner::FFmpegRunner;
use crate::infrastructure::preview_cache::{
    build_proxy_args, commit_generated_preview, default_preview_root, generated_preview_path,
    is_direct_video, is_nonempty_cache_file, is_valid_mp4_cache_file, prepare_image_preview,
};
use serde::Serialize;
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, OnceLock};
use tauri::{Emitter, Manager};
use tokio::io::AsyncBufReadExt;
use tokio::process::Command;

static PREVIEW_TASKS: OnceLock<Mutex<HashMap<String, Arc<AtomicBool>>>> = OnceLock::new();

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "kebab-case")]
pub enum PreviewState {
    Probing,
    Generating,
    Ready,
    Error,
    Cancelled,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "kebab-case")]
pub enum PreviewKind {
    Image,
    DirectVideo,
    ProxyVideo,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MediaFailure {
    pub failure_type: String,
    pub message: String,
    pub retryable: bool,
}

#[derive(Debug, Clone)]
pub struct VideoPreviewMetadata {
    pub duration_secs: f64,
    pub width: u32,
    pub height: u32,
    pub fps: f64,
    pub container: String,
    pub video_codec: String,
    pub audio_codec: Option<String>,
    pub has_audio: bool,
}

impl From<&VideoProbeInfo> for VideoPreviewMetadata {
    fn from(info: &VideoProbeInfo) -> Self {
        Self {
            duration_secs: info.duration_secs,
            width: info.width,
            height: info.height,
            fps: info.fps,
            container: info.container.clone(),
            video_codec: info.video_codec.clone(),
            audio_codec: info.audio_codec.clone(),
            has_audio: info.has_audio,
        }
    }
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PreviewDescriptor {
    pub state: PreviewState,
    pub kind: PreviewKind,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub playback_path: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub poster_path: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub duration_secs: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub width: Option<u32>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub height: Option<u32>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub fps: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub container: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub video_codec: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub audio_codec: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub has_audio: Option<bool>,
    pub is_proxy: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub task_id: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<MediaFailure>,
}

impl PreviewDescriptor {
    fn base(state: PreviewState, kind: PreviewKind, task_id: String) -> Self {
        Self {
            state,
            kind,
            playback_path: None,
            poster_path: None,
            duration_secs: None,
            width: None,
            height: None,
            fps: None,
            container: None,
            video_codec: None,
            audio_codec: None,
            has_audio: None,
            is_proxy: false,
            task_id: Some(task_id),
            error: None,
        }
    }

    pub fn ready_proxy(
        task_id: String,
        playback_path: String,
        poster_path: Option<String>,
        metadata: VideoPreviewMetadata,
    ) -> Self {
        let mut value = Self::base(PreviewState::Ready, PreviewKind::ProxyVideo, task_id);
        value.playback_path = Some(playback_path);
        value.poster_path = poster_path;
        value.duration_secs = Some(metadata.duration_secs);
        value.width = Some(metadata.width);
        value.height = Some(metadata.height);
        value.fps = Some(metadata.fps);
        value.container = Some(metadata.container);
        value.video_codec = Some(metadata.video_codec);
        value.audio_codec = metadata.audio_codec;
        value.has_audio = Some(metadata.has_audio);
        value.is_proxy = true;
        value
    }

    fn ready_direct(task_id: String, path: String, metadata: VideoPreviewMetadata) -> Self {
        let mut value = Self::base(PreviewState::Ready, PreviewKind::DirectVideo, task_id);
        value.playback_path = Some(path);
        value.duration_secs = Some(metadata.duration_secs);
        value.width = Some(metadata.width);
        value.height = Some(metadata.height);
        value.fps = Some(metadata.fps);
        value.container = Some(metadata.container);
        value.video_codec = Some(metadata.video_codec);
        value.audio_codec = metadata.audio_codec;
        value.has_audio = Some(metadata.has_audio);
        value
    }

    fn ready_image(task_id: String, path: String, width: u32, height: u32) -> Self {
        let mut value = Self::base(PreviewState::Ready, PreviewKind::Image, task_id);
        value.playback_path = Some(path);
        value.width = Some(width);
        value.height = Some(height);
        value
    }

    pub fn failure(task_id: String, failure_type: &str, message: &str, retryable: bool) -> Self {
        Self::failure_for(
            task_id,
            PreviewKind::ProxyVideo,
            failure_type,
            message,
            retryable,
        )
    }

    fn failure_for(
        task_id: String,
        kind: PreviewKind,
        failure_type: &str,
        message: &str,
        retryable: bool,
    ) -> Self {
        let mut value = Self::base(PreviewState::Error, kind, task_id);
        value.error = Some(MediaFailure {
            failure_type: failure_type.to_string(),
            message: message.to_string(),
            retryable,
        });
        value
    }

    fn cancelled(task_id: String) -> Self {
        let mut value = Self::base(PreviewState::Cancelled, PreviewKind::ProxyVideo, task_id);
        value.error = Some(MediaFailure {
            failure_type: "cancelled".to_string(),
            message: "预览生成已取消".to_string(),
            retryable: true,
        });
        value
    }
}

fn task_registry() -> &'static Mutex<HashMap<String, Arc<AtomicBool>>> {
    PREVIEW_TASKS.get_or_init(|| Mutex::new(HashMap::new()))
}

fn register_task(task_id: &str) -> Arc<AtomicBool> {
    let flag = Arc::new(AtomicBool::new(false));
    if let Ok(mut tasks) = task_registry().lock() {
        tasks.insert(task_id.to_string(), flag.clone());
    }
    flag
}

fn finish_task(task_id: &str) {
    if let Ok(mut tasks) = task_registry().lock() {
        tasks.remove(task_id);
    }
}

struct PreviewTaskGuard {
    task_id: String,
    cancelled: Arc<AtomicBool>,
}

impl PreviewTaskGuard {
    fn new(task_id: &str) -> Self {
        Self {
            task_id: task_id.to_string(),
            cancelled: register_task(task_id),
        }
    }

    fn is_cancelled(&self) -> bool {
        self.cancelled.load(Ordering::SeqCst)
    }
}

impl Drop for PreviewTaskGuard {
    fn drop(&mut self) {
        finish_task(&self.task_id);
    }
}

fn authorize_asset(app: &tauri::AppHandle, path: &Path) -> Result<(), String> {
    app.asset_protocol_scope()
        .allow_file(path)
        .map_err(|error| format!("无法授权本地预览文件: {error}"))
}

fn is_image_path(path: &Path) -> bool {
    path.extension()
        .and_then(|extension| extension.to_str())
        .is_some_and(|extension| {
            matches!(
                extension.to_ascii_lowercase().as_str(),
                "jpg" | "jpeg" | "png" | "webp" | "avif" | "bmp" | "tif" | "tiff" | "psd"
            )
        })
}

async fn probe_video(path: &str) -> Result<VideoProbeInfo, String> {
    let runner = FFmpegRunner::new().map_err(|error| error.to_string())?;
    let json = runner
        .run_ffprobe(&[
            "-v",
            "quiet",
            "-print_format",
            "json",
            "-show_format",
            "-show_streams",
            path,
        ])
        .await
        .map_err(|error| error.to_string())?;
    parse_video_probe(&json).map_err(|error| error.to_string())
}

fn emit_progress(app: &tauri::AppHandle, task_id: &str, stage: &str, percent: f64) {
    let _ = app.emit(
        "preview-progress",
        serde_json::json!({
            "taskId": task_id,
            "stage": stage,
            "percent": percent.clamp(0.0, 100.0),
        }),
    );
}

fn parse_progress_line(line: &str, duration_secs: f64) -> Option<f64> {
    if duration_secs <= 0.0 {
        return None;
    }
    let value = line
        .strip_prefix("out_time_us=")
        .or_else(|| line.strip_prefix("out_time_ms="))?
        .parse::<f64>()
        .ok()?;
    Some((value / 1_000_000.0 / duration_secs * 100.0).min(99.0))
}

async fn generate_proxy(
    app: &tauri::AppHandle,
    input: &Path,
    output: &Path,
    has_audio: bool,
    duration_secs: f64,
    task_id: &str,
    cancelled: Arc<AtomicBool>,
) -> Result<(), &'static str> {
    if is_valid_mp4_cache_file(output) {
        emit_progress(app, task_id, "ready", 100.0);
        return Ok(());
    }
    let runner = FFmpegRunner::new().map_err(|_| "engine-unavailable")?;
    let parent = output.parent().ok_or("proxy-generation-failed")?;
    tokio::fs::create_dir_all(parent)
        .await
        .map_err(|_| "directory-permission-denied")?;
    let temporary = parent.join(format!(".proxy-{}.tmp.mp4", uuid::Uuid::new_v4()));
    let args = build_proxy_args(
        &input.to_string_lossy(),
        &temporary.to_string_lossy(),
        has_audio,
    );
    let mut child = Command::new(runner.ffmpeg_path())
        .args(&args)
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .spawn()
        .map_err(|_| "engine-unavailable")?;
    let stdout = child.stdout.take().ok_or("proxy-generation-failed")?;
    let mut lines = tokio::io::BufReader::new(stdout).lines();
    let mut cancel_poll = tokio::time::interval(std::time::Duration::from_millis(100));
    loop {
        tokio::select! {
            line = lines.next_line() => {
                match line {
                    Ok(Some(line)) => {
                        if let Some(percent) = parse_progress_line(&line, duration_secs) {
                            emit_progress(app, task_id, "generating", percent);
                        }
                    }
                    Ok(None) => break,
                    Err(_) => {
                        let _ = child.kill().await;
                        let _ = tokio::fs::remove_file(&temporary).await;
                        return Err("proxy-generation-failed");
                    }
                }
            }
            _ = cancel_poll.tick() => {
                if cancelled.load(Ordering::SeqCst) {
                    let _ = child.kill().await;
                    let _ = child.wait().await;
                    let _ = tokio::fs::remove_file(&temporary).await;
                    return Err("cancelled");
                }
            }
        }
    }
    let status = child.wait().await.map_err(|_| "proxy-generation-failed")?;
    if !status.success() || !is_valid_mp4_cache_file(&temporary) {
        let _ = tokio::fs::remove_file(&temporary).await;
        return Err("proxy-generation-failed");
    }
    commit_generated_preview(&temporary, output).map_err(|_| "proxy-generation-failed")?;
    emit_progress(app, task_id, "ready", 100.0);
    Ok(())
}

async fn generate_poster(proxy: &Path, poster: &Path) -> Option<String> {
    if is_nonempty_cache_file(poster) {
        return Some(poster.to_string_lossy().to_string());
    }
    let runner = FFmpegRunner::new().ok()?;
    let parent = poster.parent()?;
    tokio::fs::create_dir_all(parent).await.ok()?;
    let temporary = parent.join(format!(".poster-{}.tmp.jpg", uuid::Uuid::new_v4()));
    let args = vec![
        "-y".to_string(),
        "-ss".to_string(),
        "0.25".to_string(),
        "-i".to_string(),
        proxy.to_string_lossy().to_string(),
        "-frames:v".to_string(),
        "1".to_string(),
        "-vf".to_string(),
        "scale=960:540:force_original_aspect_ratio=decrease".to_string(),
        temporary.to_string_lossy().to_string(),
    ];
    runner.run_simple(&args).await.ok()?;
    commit_generated_preview(&temporary, poster).ok()?;
    Some(poster.to_string_lossy().to_string())
}

#[tauri::command]
pub async fn prepare_media_preview(
    app: tauri::AppHandle,
    input_path: String,
    media_type: Option<String>,
    task_id: Option<String>,
    force_proxy: Option<bool>,
) -> PreviewDescriptor {
    let task_id = task_id.unwrap_or_else(|| uuid::Uuid::new_v4().to_string());
    let task = PreviewTaskGuard::new(&task_id);
    let input = PathBuf::from(&input_path);
    let is_image = media_type.as_deref() == Some("image") || is_image_path(&input);
    emit_progress(&app, &task_id, "probing", 0.0);

    if !input.is_file() {
        return PreviewDescriptor::failure_for(
            task_id,
            if is_image {
                PreviewKind::Image
            } else {
                PreviewKind::ProxyVideo
            },
            "format-damaged",
            "媒体文件不存在或已移动",
            false,
        );
    }

    if is_image {
        let token = media_engine_cache_token();
        let input_for_task = input.clone();
        let result =
            tokio::task::spawn_blocking(move || prepare_image_preview(&input_for_task, &token))
                .await;
        if task.is_cancelled() {
            return PreviewDescriptor::cancelled(task_id);
        }
        return match result {
            Ok(Ok(artifact)) => match authorize_asset(&app, &artifact.path) {
                Ok(()) => PreviewDescriptor::ready_image(
                    task_id,
                    artifact.path.to_string_lossy().to_string(),
                    artifact.width,
                    artifact.height,
                ),
                Err(message) => PreviewDescriptor::failure_for(
                    task_id,
                    PreviewKind::Image,
                    "directory-permission-denied",
                    &message,
                    true,
                ),
            },
            _ => PreviewDescriptor::failure_for(
                task_id,
                PreviewKind::Image,
                "format-damaged",
                "无法解码图片预览",
                false,
            ),
        };
    }

    let info = match probe_video(&input_path).await {
        Ok(info) => info,
        Err(_) => {
            return PreviewDescriptor::failure(
                task_id,
                "probe-failed",
                "无法读取视频信息，请确认文件完整且媒体引擎可用",
                true,
            )
        }
    };
    if task.is_cancelled() {
        return PreviewDescriptor::cancelled(task_id);
    }
    let metadata = VideoPreviewMetadata::from(&info);
    if !force_proxy.unwrap_or(false) && is_direct_video(&input, &info) {
        return match authorize_asset(&app, &input) {
            Ok(()) => PreviewDescriptor::ready_direct(task_id, input_path, metadata),
            Err(message) => {
                PreviewDescriptor::failure(task_id, "directory-permission-denied", &message, true)
            }
        };
    }

    let token = media_engine_cache_token();
    let output = match generated_preview_path(&input, &token, "videos", "mp4") {
        Ok(path) => path,
        Err(_) => {
            return PreviewDescriptor::failure(
                task_id,
                "directory-permission-denied",
                "无法创建本地预览缓存",
                true,
            )
        }
    };
    let poster = match generated_preview_path(&input, &token, "posters", "jpg") {
        Ok(path) => path,
        Err(_) => default_preview_root()
            .unwrap_or_else(|_| std::env::temp_dir())
            .join("posters")
            .join(format!("{}.jpg", uuid::Uuid::new_v4())),
    };
    emit_progress(&app, &task_id, "generating", 0.0);
    let generated = generate_proxy(
        &app,
        &input,
        &output,
        info.has_audio,
        info.duration_secs,
        &task_id,
        task.cancelled.clone(),
    )
    .await;
    match generated {
        Ok(()) => {
            let poster_path = generate_poster(&output, &poster).await;
            if authorize_asset(&app, &output).is_err() {
                return PreviewDescriptor::failure(
                    task_id,
                    "directory-permission-denied",
                    "无法授权本地视频代理",
                    true,
                );
            }
            if let Some(path) = poster_path.as_deref() {
                let _ = authorize_asset(&app, Path::new(path));
            }
            PreviewDescriptor::ready_proxy(
                task_id,
                output.to_string_lossy().to_string(),
                poster_path,
                metadata,
            )
        }
        Err("cancelled") => PreviewDescriptor::cancelled(task_id),
        Err(failure_type) => PreviewDescriptor::failure(
            task_id,
            failure_type,
            "无法生成可播放的视频代理，请重试或检查磁盘空间",
            true,
        ),
    }
}

#[tauri::command]
pub fn cancel_preview_task(task_id: String) -> bool {
    task_registry()
        .lock()
        .ok()
        .and_then(|tasks| tasks.get(&task_id).cloned())
        .is_some_and(|flag| {
            flag.store(true, Ordering::SeqCst);
            true
        })
}

#[tauri::command]
pub async fn clear_preview_cache() -> Result<(), String> {
    let root = default_preview_root()?;
    if root.exists() {
        tokio::fs::remove_dir_all(root)
            .await
            .map_err(|error| format!("清理预览缓存失败: {error}"))?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn preview_descriptor_serializes_the_frontend_contract_in_camel_case() {
        let descriptor = PreviewDescriptor::ready_proxy(
            "task-1".to_string(),
            "C:/TinyPix/previews/proxy.mp4".to_string(),
            Some("C:/TinyPix/previews/poster.jpg".to_string()),
            VideoPreviewMetadata {
                duration_secs: 12.5,
                width: 960,
                height: 540,
                fps: 30.0,
                container: "matroska".to_string(),
                video_codec: "hevc".to_string(),
                audio_codec: Some("aac".to_string()),
                has_audio: true,
            },
        );
        let value = serde_json::to_value(descriptor).unwrap();
        assert_eq!(value["state"], "ready");
        assert_eq!(value["kind"], "proxy-video");
        assert_eq!(value["playbackPath"], "C:/TinyPix/previews/proxy.mp4");
        assert_eq!(value["durationSecs"], 12.5);
        assert_eq!(value["videoCodec"], "hevc");
        assert_eq!(value["hasAudio"], true);
        assert_eq!(value["isProxy"], true);
        assert_eq!(value["taskId"], "task-1");
    }

    #[test]
    fn preview_failure_is_structured_and_retryable() {
        let descriptor = PreviewDescriptor::failure(
            "task-2".to_string(),
            "proxy-generation-failed",
            "无法生成本地播放代理",
            true,
        );
        let value = serde_json::to_value(descriptor).unwrap();
        assert_eq!(value["state"], "error");
        assert_eq!(value["error"]["failureType"], "proxy-generation-failed");
        assert_eq!(value["error"]["retryable"], true);
    }
}
