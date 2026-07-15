use serde::Serialize;
use std::path::PathBuf;
use std::time::Instant;
use tauri::Emitter;
use tokio::process::Command;

use crate::infrastructure::error::{TinyPixError, TinyPixResult};
use crate::infrastructure::ffmpeg_manager::{
    check_ffmpeg_version, get_ffmpeg_path, parse_video_probe, VideoProbeInfo,
};
use crate::infrastructure::ffmpeg_runner::request_video_cancel;
use crate::infrastructure::ffmpeg_runner::FFmpegRunner;
use crate::infrastructure::validation::{
    unique_output_path, validate_output_path, validate_time_range, validate_video_path,
};

/// FFmpeg 安装状态
#[derive(Debug, Serialize)]
pub struct FFmpegStatus {
    pub installed: bool,
    pub version: String,
    pub path: String,
}

/// 视频压缩结果
#[derive(Debug, Serialize)]
pub struct VideoResult {
    pub task_id: String,
    pub stage: String,
    pub percent: f64,
    pub output_path: String,
    pub original_size: u64,
    pub output_size: u64,
    pub saved_bytes: u64,
    pub processing_time_secs: f64,
    pub failure_type: Option<String>,
    pub retryable: bool,
}

/// 视频信息
#[derive(Debug, Serialize)]
pub struct VideoInfo {
    pub duration_secs: f64,
    pub width: u32,
    pub height: u32,
    pub bitrate_kbps: u32,
    pub fps: f64,
    /// Kept for existing frontends; equivalent to `video_codec`.
    pub codec: String,
    /// Kept for existing frontends; equivalent to `container`.
    pub format: String,
    pub container: String,
    pub video_codec: String,
    pub audio_codec: Option<String>,
    pub has_audio: bool,
    pub rotation: i32,
}

#[derive(Debug, Clone, Serialize)]
pub struct CompressionProgress {
    pub current_secs: f64,
    pub total_secs: f64,
    pub progress_pct: f64,
}

/// 检查 FFmpeg 状态
#[tauri::command]
pub async fn check_ffmpeg() -> TinyPixResult<FFmpegStatus> {
    let path = get_ffmpeg_path();
    if path.is_none() {
        return Ok(FFmpegStatus {
            installed: false,
            version: String::new(),
            path: String::new(),
        });
    }

    let version = check_ffmpeg_version().map_err(|e| TinyPixError::Processing(e.to_string()))?;
    let path_str = path.unwrap().to_string_lossy().to_string();

    Ok(FFmpegStatus {
        installed: true,
        version,
        path: path_str,
    })
}

#[tauri::command]
pub fn cancel_video_tasks() {
    request_video_cancel();
}

/// 获取视频信息
#[tauri::command]
pub async fn get_video_info(_app: tauri::AppHandle, path: String) -> TinyPixResult<VideoInfo> {
    let runner = FFmpegRunner::new()?;
    let json = runner
        .run_ffprobe(&[
            "-v",
            "quiet",
            "-print_format",
            "json",
            "-show_format",
            "-show_streams",
            &path,
        ])
        .await?;

    let info: VideoProbeInfo =
        parse_video_probe(&json).map_err(|e| TinyPixError::Processing(e.to_string()))?;

    Ok(VideoInfo {
        duration_secs: info.duration_secs,
        width: info.width,
        height: info.height,
        bitrate_kbps: info.bitrate_kbps,
        fps: info.fps,
        codec: info.codec,
        format: info.format,
        container: info.container,
        video_codec: info.video_codec,
        audio_codec: info.audio_codec,
        has_audio: info.has_audio,
        rotation: info.rotation,
    })
}

/// 压缩视频
#[tauri::command]
pub async fn compress_video(
    app: tauri::AppHandle,
    input_path: String,
    output_path: String,
    preset: String,
    crf: Option<u8>,
    scale: Option<String>,
) -> TinyPixResult<VideoResult> {
    let runner = FFmpegRunner::new()?;
    validate_video_path(&input_path)?;
    validate_output_path(&output_path)?;
    let output_path = unique_output_path(&PathBuf::from(output_path))
        .to_string_lossy()
        .to_string();
    let _input_p = PathBuf::from(&input_path);
    let original_size = std::fs::metadata(&input_path).map(|m| m.len()).unwrap_or(0);

    // 根据 preset 确定 CRF（使用前端预设名: light, standard, extreme）
    let crf_val = match preset.as_str() {
        "light" => crf.unwrap_or(20),
        "standard" => crf.unwrap_or(26),
        "extreme" => crf.unwrap_or(34),
        _ => crf.unwrap_or(23),
    };

    // 先获取总时长用于进度
    let total_secs = runner.probe_duration(&input_path).await.unwrap_or(0.0);

    // 构建 FFmpeg 参数
    let mut args: Vec<String> = vec![String::from("-y"), String::from("-i"), input_path.clone()];

    if let Some(ref s) = scale {
        args.push(String::from("-vf"));
        args.push(format!("scale={}", s));
    }

    args.push(String::from("-crf"));
    args.push(crf_val.to_string());
    args.push(String::from("-c:v"));
    args.push(String::from("libx264"));
    args.push(String::from("-preset"));
    args.push(String::from("medium"));
    args.push(String::from("-c:a"));
    args.push(String::from("aac"));
    args.push(String::from("-b:a"));
    args.push(String::from("128k"));
    args.push(output_path.clone());

    execute_with_progress(
        &app,
        &runner,
        &args,
        total_secs,
        original_size,
        &output_path,
    )
    .await
}

/// 从视频生成 GIF
#[tauri::command]
pub async fn create_gif(
    app: tauri::AppHandle,
    input_path: String,
    output_path: String,
    fps: u32,
    width: Option<u32>,
    quality: Option<u8>,
    start_secs: Option<f64>,
    end_secs: Option<f64>,
    loop_count: Option<u32>,
) -> TinyPixResult<VideoResult> {
    let runner = FFmpegRunner::new()?;
    validate_video_path(&input_path)?;
    validate_output_path(&output_path)?;
    let output_path = unique_output_path(&PathBuf::from(output_path))
        .to_string_lossy()
        .to_string();

    let original_size = std::fs::metadata(&input_path).map(|m| m.len()).unwrap_or(0);
    let fps = fps.clamp(5, 30);
    let width = width.map(|value| value.clamp(160, 1920));
    let quality = quality.unwrap_or(2).clamp(1, 3);
    let loop_count = loop_count.unwrap_or(0);
    if loop_count > 100 {
        return Err(TinyPixError::InvalidParam(
            "GIF 循环次数不能超过 100".to_string(),
        ));
    }

    // 验证时间范围
    if let (Some(s), Some(e)) = (start_secs, end_secs) {
        validate_time_range(s, e, 0.0)?;
    }

    // 计算裁剪时长
    let duration = match (start_secs, end_secs) {
        (Some(s), Some(e)) => Some(e - s),
        _ => None,
    };

    // 获取总时长用于进度
    let total_duration = if let (Some(s), Some(e)) = (start_secs, end_secs) {
        e - s
    } else {
        runner.probe_duration(&input_path).await.unwrap_or(0.0)
    };
    if total_duration > 60.0 {
        return Err(TinyPixError::InvalidParam(
            "GIF 片段最长 60 秒，请设置更短的起止时间".to_string(),
        ));
    }

    // 质量参数映射
    let (stats_mode, max_colors, dither) = match quality {
        3 => ("full", 256, "sierra2_4a"),
        2 => ("diff", 128, "sierra2_4a"),
        1 => ("diff", 64, "none"),
        _ => ("diff", 128, "sierra2_4a"),
    };

    // 构建两阶段调色板 GIF 滤镜
    // 第一阶段：fps + scale + palettegen
    // 第二阶段：paletteuse
    let palettegen_filter = build_gif_palette_filter(fps, width, stats_mode, max_colors, dither);

    // 构建 FFmpeg 参数
    let mut args: Vec<String> = vec![String::from("-y")];

    // 时间裁剪：-ss 放在 -i 前面实现快速 seek
    if let Some(s) = start_secs {
        args.push(String::from("-ss"));
        args.push(format!("{:.3}", s));
    }

    args.push(String::from("-i"));
    args.push(input_path.clone());

    // 持续时间
    if let Some(d) = duration {
        args.push(String::from("-t"));
        args.push(format!("{:.3}", d));
    }

    args.push(String::from("-filter_complex"));
    args.push(palettegen_filter);
    args.push(String::from("-loop"));
    args.push(loop_count.to_string());
    args.push(output_path.clone());

    execute_with_progress(
        &app,
        &runner,
        &args,
        total_duration,
        original_size,
        &output_path,
    )
    .await
}

fn build_gif_palette_filter(
    fps: u32,
    width: Option<u32>,
    stats_mode: &str,
    max_colors: u32,
    dither: &str,
) -> String {
    let scale = width
        .map(|value| format!(",scale={}:-1:flags=lanczos", value))
        .unwrap_or_default();
    format!(
        "fps={fps}{scale},split[a][b];[a]palettegen=stats_mode={stats_mode}:max_colors={max_colors}[p];[b][p]paletteuse=dither={dither}"
    )
}

fn safe_preview_name(path: &str) -> String {
    let input = PathBuf::from(path);
    let stem = input
        .file_stem()
        .and_then(|name| name.to_str())
        .unwrap_or("video");
    let safe: String = stem
        .chars()
        .map(|ch| {
            if ch.is_ascii_alphanumeric() || ch == '-' || ch == '_' {
                ch
            } else {
                '_'
            }
        })
        .collect();

    if safe.is_empty() {
        "video".to_string()
    } else {
        safe
    }
}

/// 从视频提取单帧图片
#[tauri::command]
pub async fn extract_frame(
    _app: tauri::AppHandle,
    input_path: String,
    output_path: String,
    timestamp_secs: f64,
) -> TinyPixResult<()> {
    let runner = FFmpegRunner::new()?;

    let hours = (timestamp_secs / 3600.0).floor();
    let mins = ((timestamp_secs % 3600.0) / 60.0).floor();
    let secs = timestamp_secs % 60.0;
    let time_str = format!("{:02}:{:02}:{:06.3}", hours, mins, secs);

    let args = vec![
        "-ss".to_string(),
        time_str,
        "-i".to_string(),
        input_path,
        "-vframes".to_string(),
        "1".to_string(),
        "-y".to_string(),
        output_path,
    ];

    runner
        .run_simple(&args)
        .await
        .map_err(|e| TinyPixError::Processing(format!("提取帧失败: {}", e)))?;
    Ok(())
}

// ============================================================
// 通用辅助：带进度追踪的 FFmpeg 执行
// ============================================================

async fn execute_with_progress(
    app: &tauri::AppHandle,
    runner: &FFmpegRunner,
    args: &[String],
    total_secs: f64,
    original_size: u64,
    output_path: &str,
) -> TinyPixResult<VideoResult> {
    let start = Instant::now();
    runner
        .run_with_progress(args, total_secs, |snapshot| {
            let _ = app.emit(
                "video-progress",
                CompressionProgress {
                    current_secs: snapshot.current_secs,
                    total_secs,
                    progress_pct: snapshot.progress_pct,
                },
            );
        })
        .await?;
    let _ = app.emit(
        "video-progress",
        CompressionProgress {
            current_secs: total_secs,
            total_secs,
            progress_pct: 100.0,
        },
    );
    let output_size = std::fs::metadata(output_path).map(|m| m.len()).unwrap_or(0);
    Ok(VideoResult {
        task_id: uuid::Uuid::new_v4().to_string(),
        stage: "completed".to_string(),
        percent: 100.0,
        output_path: output_path.to_string(),
        original_size,
        output_size,
        saved_bytes: original_size.saturating_sub(output_size),
        processing_time_secs: start.elapsed().as_secs_f64(),
        failure_type: None,
        retryable: false,
    })
}

// ============================================================
// 新增视频命令
// ============================================================

fn format_timestamp(secs: f64) -> String {
    let hours = (secs / 3600.0).floor();
    let mins = ((secs % 3600.0) / 60.0).floor();
    let secs_part = secs % 60.0;
    format!("{:02}:{:02}:{:06.3}", hours, mins, secs_part)
}

async fn extract_frame_at(
    runner: &FFmpegRunner,
    input_path: &str,
    output_path: &std::path::Path,
    timestamp_secs: f64,
) -> bool {
    let time_str = format_timestamp(timestamp_secs);
    let output_path_str = output_path.to_string_lossy().to_string();

    let args = vec![
        "-y".to_string(),
        "-ss".to_string(),
        time_str,
        "-i".to_string(),
        input_path.to_string(),
        "-vframes".to_string(),
        "1".to_string(),
        "-vf".to_string(),
        "scale=960:-2".to_string(),
        output_path_str,
    ];

    match runner.run_simple(&args).await {
        Ok(_) => output_path.exists(),
        Err(_) => false,
    }
}

/// 为 WebView 不能直接播放的编码生成本地预览图
#[tauri::command]
pub async fn create_video_preview(input_path: String) -> TinyPixResult<String> {
    let runner = FFmpegRunner::new()?;
    validate_video_path(&input_path)?;

    let preview_dir = std::env::temp_dir().join("tinypix_previews");
    tokio::fs::create_dir_all(&preview_dir)
        .await
        .map_err(|e| TinyPixError::Io(format!("创建预览缓存失败: {}", e)))?;

    let output_path = preview_dir.join(format!(
        "{}_{}.jpg",
        safe_preview_name(&input_path),
        uuid::Uuid::new_v4()
    ));

    let duration = runner.probe_duration(&input_path).await.ok();

    let first_try = 1.0;
    if extract_frame_at(&runner, &input_path, &output_path, first_try).await {
        return Ok(output_path.to_string_lossy().to_string());
    }

    if let Some(dur) = duration {
        if dur > 2.0 {
            let mid_try = dur * 0.5;
            if extract_frame_at(&runner, &input_path, &output_path, mid_try).await {
                return Ok(output_path.to_string_lossy().to_string());
            }
        }
    }

    let last_try = 0.0;
    if extract_frame_at(&runner, &input_path, &output_path, last_try).await {
        return Ok(output_path.to_string_lossy().to_string());
    }

    Err(TinyPixError::Processing(
        "无法生成视频预览图，但文件仍可继续使用 FFmpeg 处理".to_string(),
    ))
}

/// 导出视频缩略图
#[tauri::command]
pub async fn export_thumbnail(
    input_path: String,
    output_path: String,
    timestamp_secs: Option<f64>,
    width: Option<u32>,
) -> TinyPixResult<()> {
    let runner = FFmpegRunner::new()?;
    validate_video_path(&input_path)?;
    validate_output_path(&output_path)?;

    let ts = timestamp_secs.unwrap_or(1.0);
    let hours = (ts / 3600.0).floor();
    let mins = ((ts % 3600.0) / 60.0).floor();
    let secs = ts % 60.0;
    let time_str = format!("{:02}:{:02}:{:06.3}", hours, mins, secs);

    let mut args: Vec<String> = vec![
        "-y".to_string(),
        "-ss".to_string(),
        time_str,
        "-i".to_string(),
        input_path,
        "-vframes".to_string(),
        "1".to_string(),
    ];

    if let Some(w) = width {
        args.push("-vf".to_string());
        args.push(format!("scale={}:-1", w));
    }

    args.push("-y".to_string());
    args.push(output_path);

    runner
        .run_simple(&args)
        .await
        .map_err(|e| TinyPixError::Processing(format!("导出缩略图失败: {}", e)))?;
    Ok(())
}

/// 裁剪视频
#[tauri::command]
pub async fn trim_video(
    app: tauri::AppHandle,
    input_path: String,
    output_path: String,
    start_secs: f64,
    end_secs: f64,
    precise: Option<bool>,
) -> TinyPixResult<VideoResult> {
    let runner = FFmpegRunner::new()?;
    validate_video_path(&input_path)?;
    validate_output_path(&output_path)?;
    let output_path = unique_output_path(&PathBuf::from(output_path))
        .to_string_lossy()
        .to_string();

    let total_secs = runner.probe_duration(&input_path).await.unwrap_or(0.0);
    validate_time_range(start_secs, end_secs, total_secs)?;

    let original_size = std::fs::metadata(&input_path).map(|m| m.len()).unwrap_or(0);
    let duration = end_secs - start_secs;

    let args = if precise.unwrap_or(false) {
        build_trim_args(&input_path, &output_path, start_secs, end_secs, None)
    } else {
        build_lossless_trim_args(&input_path, &output_path, start_secs, end_secs)
    };

    execute_with_progress(&app, &runner, &args, duration, original_size, &output_path).await
}

/// 镜像翻转视频
#[tauri::command]
pub async fn mirror_video(
    app: tauri::AppHandle,
    input_path: String,
    output_path: String,
    direction: String,
) -> TinyPixResult<VideoResult> {
    let runner = FFmpegRunner::new()?;
    validate_video_path(&input_path)?;
    validate_output_path(&output_path)?;

    let filter = match direction.as_str() {
        "horizontal" => "hflip",
        "vertical" => "vflip",
        _ => {
            return Err(TinyPixError::InvalidParam(format!(
                "不支持的翻转方向: {}，可选: horizontal, vertical",
                direction
            )))
        }
    };

    let original_size = std::fs::metadata(&input_path).map(|m| m.len()).unwrap_or(0);
    let total_secs = runner.probe_duration(&input_path).await.unwrap_or(0.0);

    let args = vec![
        "-y".to_string(),
        "-i".to_string(),
        input_path,
        "-vf".to_string(),
        filter.to_string(),
        "-c:a".to_string(),
        "copy".to_string(),
        output_path.clone(),
    ];

    execute_with_progress(
        &app,
        &runner,
        &args,
        total_secs,
        original_size,
        &output_path,
    )
    .await
}

/// 旋转视频
#[tauri::command]
pub async fn rotate_video(
    app: tauri::AppHandle,
    input_path: String,
    output_path: String,
    degrees: u16,
) -> TinyPixResult<VideoResult> {
    let runner = FFmpegRunner::new()?;
    validate_video_path(&input_path)?;
    validate_output_path(&output_path)?;

    let filter = match degrees {
        90 => "transpose=1",
        180 => "transpose=1,transpose=1",
        270 => "transpose=2",
        _ => {
            return Err(TinyPixError::InvalidParam(format!(
                "不支持的旋转角度: {}，可选: 90, 180, 270",
                degrees
            )))
        }
    };

    let original_size = std::fs::metadata(&input_path).map(|m| m.len()).unwrap_or(0);
    let total_secs = runner.probe_duration(&input_path).await.unwrap_or(0.0);

    let args = vec![
        "-y".to_string(),
        "-i".to_string(),
        input_path,
        "-vf".to_string(),
        filter.to_string(),
        "-c:a".to_string(),
        "copy".to_string(),
        output_path.clone(),
    ];

    execute_with_progress(
        &app,
        &runner,
        &args,
        total_secs,
        original_size,
        &output_path,
    )
    .await
}

/// 修改视频播放速度
#[tauri::command]
pub async fn change_video_speed(
    app: tauri::AppHandle,
    input_path: String,
    output_path: String,
    speed: f64,
) -> TinyPixResult<VideoResult> {
    let runner = FFmpegRunner::new()?;
    validate_video_path(&input_path)?;
    validate_output_path(&output_path)?;

    if speed <= 0.0 || speed > 100.0 {
        return Err(TinyPixError::InvalidParam(format!(
            "不支持的速度倍率: {}，范围: (0, 100]",
            speed
        )));
    }

    let original_size = std::fs::metadata(&input_path).map(|m| m.len()).unwrap_or(0);

    // 构建 setpts 滤镜
    let video_pts = format!("{:.6}*PTS", 1.0 / speed);

    // 构建 atempo 滤镜链（atempo 范围 [0.5, 2.0]，超出需要链式）
    let audio_filter = build_atempo_chain(speed);

    let total_secs = runner.probe_duration(&input_path).await.unwrap_or(0.0);

    let args = vec![
        "-y".to_string(),
        "-i".to_string(),
        input_path,
        "-filter:v".to_string(),
        format!("setpts={}", video_pts),
        "-filter:a".to_string(),
        audio_filter,
        output_path.clone(),
    ];

    execute_with_progress(
        &app,
        &runner,
        &args,
        total_secs,
        original_size,
        &output_path,
    )
    .await
}

/// 构建 atempo 链式滤镜（atempo 范围 [0.5, 2.0]）
fn build_atempo_chain(speed: f64) -> String {
    if speed >= 0.5 && speed <= 2.0 {
        return format!("atempo={:.6}", speed);
    }

    let mut parts = Vec::new();
    let mut remaining = speed;

    while remaining < 0.5 {
        parts.push("atempo=0.5".to_string());
        remaining /= 0.5;
    }
    while remaining > 2.0 {
        parts.push("atempo=2.0".to_string());
        remaining /= 2.0;
    }
    parts.push(format!("atempo={:.6}", remaining));

    parts.join(",")
}

/// 提取音频 — 已迁出至 commands/audio_commands.rs（模块化重构）
/// 历史实现保留参考，重构后请使用 tinypix_lib::commands::audio_commands::extract_audio

/// 合并多个视频
#[tauri::command]
pub async fn merge_videos(
    app: tauri::AppHandle,
    input_paths: Vec<String>,
    output_path: String,
) -> TinyPixResult<VideoResult> {
    let runner = FFmpegRunner::new()?;
    validate_output_path(&output_path)?;

    if input_paths.is_empty() {
        return Err(TinyPixError::InvalidParam(
            "输入视频列表不能为空".to_string(),
        ));
    }
    if input_paths.len() > 100 {
        return Err(TinyPixError::InvalidParam(
            "最多支持合并 100 个视频".to_string(),
        ));
    }

    // 验证所有输入路径
    for path in &input_paths {
        validate_video_path(path)?;
    }

    // 计算原始总大小
    let mut original_size: u64 = 0;
    for path in &input_paths {
        original_size += std::fs::metadata(path).map(|m| m.len()).unwrap_or(0);
    }

    // 写临时 concat list 文件
    let temp_dir = std::env::temp_dir().join("tinypix_merge");
    std::fs::create_dir_all(&temp_dir).map_err(|e| TinyPixError::Io(e.to_string()))?;
    let list_path = temp_dir.join("concat_list.txt");

    let mut list_content = String::new();
    for path in &input_paths {
        // 对路径中的单引号进行转义
        let escaped = path.replace("'", "'\\''");
        list_content.push_str(&format!("file '{}'", escaped));
        list_content.push('\n');
    }
    std::fs::write(&list_path, &list_content).map_err(|e| TinyPixError::Io(e.to_string()))?;

    let list_path_str = list_path.to_string_lossy().to_string();

    let start = Instant::now();
    let args = vec![
        "-y".to_string(),
        "-f".to_string(),
        "concat".to_string(),
        "-safe".to_string(),
        "0".to_string(),
        "-i".to_string(),
        list_path_str,
        "-c".to_string(),
        "copy".to_string(),
        output_path.clone(),
    ];

    let run_result = runner.run_simple(&args).await;
    let _ = std::fs::remove_file(&list_path);
    run_result.map_err(|e| TinyPixError::Processing(format!("FFmpeg 合并失败: {}", e)))?;

    let output_size = std::fs::metadata(&output_path)
        .map(|m| m.len())
        .unwrap_or(0);

    // 合并完成事件
    let _ = app.emit(
        "video-progress",
        CompressionProgress {
            current_secs: 0.0,
            total_secs: 0.0,
            progress_pct: 100.0,
        },
    );

    Ok(VideoResult {
        task_id: uuid::Uuid::new_v4().to_string(),
        stage: "completed".to_string(),
        percent: 100.0,
        output_path,
        original_size,
        output_size,
        saved_bytes: original_size.saturating_sub(output_size),
        processing_time_secs: start.elapsed().as_secs_f64(),
        failure_type: None,
        retryable: false,
    })
}

/// 转换视频格式
#[tauri::command]
pub async fn convert_video_format(
    app: tauri::AppHandle,
    input_path: String,
    output_path: String,
    target_format: String,
    quality: Option<u8>,
    video_codec: Option<String>,
    resolution_width: Option<u32>,
    resolution_height: Option<u32>,
    fps: Option<u32>,
    audio_codec: Option<String>,
    audio_bitrate: Option<u32>,
) -> TinyPixResult<VideoResult> {
    let runner = FFmpegRunner::new()?;
    validate_video_path(&input_path)?;
    validate_output_path(&output_path)?;
    let output_path = unique_output_path(&PathBuf::from(output_path))
        .to_string_lossy()
        .to_string();

    // 视频编码器映射（在 build_convert_args 内部完成）
    // 仅做基本的合法性校验
    if let Some(vc) = video_codec.as_deref() {
        match vc {
            "h264" | "h265" | "av1" | "vp9" | "prores" | "mpeg4" => {}
            _ => {
                return Err(TinyPixError::InvalidParam(format!(
                    "不支持的视频编码器: {}",
                    vc
                )))
            }
        }
    }
    if let Some(ac) = audio_codec.as_deref() {
        match ac {
            "aac" | "mp3" | "opus" | "flac" | "ac3" => {}
            _ => {
                return Err(TinyPixError::InvalidParam(format!(
                    "不支持的音频编码器: {}",
                    ac
                )))
            }
        }
    }

    let crf = quality.unwrap_or(23);
    let original_size = std::fs::metadata(&input_path).map(|m| m.len()).unwrap_or(0);

    let total_secs = runner.probe_duration(&input_path).await.unwrap_or(0.0);

    // 使用纯函数构造 FFmpeg 参数
    let args = build_convert_args(
        &input_path,
        &output_path,
        &target_format,
        video_codec.as_deref(),
        resolution_width,
        resolution_height,
        fps,
        crf,
        audio_codec.as_deref(),
        audio_bitrate,
        None,
    );

    execute_with_progress(
        &app,
        &runner,
        &args,
        total_secs,
        original_size,
        &output_path,
    )
    .await
}

/// 一站式视频编辑导出（裁切 + 速度 + 音量 + 亮度/对比度）
/// 所有可选参数为 None 时使用默认值（不做对应处理），保持向后兼容
#[tauri::command]
pub async fn edit_and_export_video(
    app: tauri::AppHandle,
    input_path: String,
    output_path: String,
    start_secs: Option<f64>,
    end_secs: Option<f64>,
    speed: Option<f64>,
    volume: Option<f64>,
    brightness: Option<f32>,
    contrast: Option<f32>,
    format: Option<String>,
) -> TinyPixResult<VideoResult> {
    let runner = FFmpegRunner::new()?;
    validate_video_path(&input_path)?;
    validate_output_path(&output_path)?;

    let original_size = std::fs::metadata(&input_path).map(|m| m.len()).unwrap_or(0);

    let total_secs = runner.probe_duration(&input_path).await.unwrap_or(0.0);

    // 解析裁切时间，默认从 0 到总时长
    let start = start_secs.unwrap_or(0.0);
    let end = end_secs.unwrap_or(total_secs);
    let duration = end - start;

    // 参数校验
    validate_time_range(start, end, total_secs)?;

    let speed_val = speed.unwrap_or(1.0);
    let volume_val = volume.unwrap_or(1.0);
    let brightness_val = brightness.unwrap_or(0.0);
    let contrast_val = contrast.unwrap_or(0.0);
    let format_val = format.unwrap_or_else(|| "mp4".to_string());

    if speed_val <= 0.0 || speed_val > 100.0 {
        return Err(TinyPixError::InvalidParam(format!(
            "不支持的速度倍率: {}，范围: (0, 100]",
            speed_val
        )));
    }
    if volume_val < 0.0 || volume_val > 10.0 {
        return Err(TinyPixError::InvalidParam(format!(
            "不支持的音量倍率: {}，范围: [0, 10]",
            volume_val
        )));
    }
    if brightness_val < -1.0 || brightness_val > 1.0 {
        return Err(TinyPixError::InvalidParam(format!(
            "不支持的亮度值: {}，范围: [-1.0, 1.0]",
            brightness_val
        )));
    }
    if contrast_val < -1.0 || contrast_val > 1.0 {
        return Err(TinyPixError::InvalidParam(format!(
            "不支持的对比度值: {}，范围: [-1.0, 1.0]",
            contrast_val
        )));
    }

    // 构建 FFmpeg 参数
    let mut args: Vec<String> = vec![String::from("-y")];

    // 1. 裁切 seek（放在 -i 前面实现快速 seek）
    args.push(String::from("-ss"));
    args.push(format!("{:.3}", start));

    // 2. 输入文件
    args.push(String::from("-i"));
    args.push(input_path.clone());

    // 3. 裁切时长
    args.push(String::from("-t"));
    args.push(format!("{:.3}", duration));

    // 4. 视频滤镜链
    let mut vfilters: Vec<String> = Vec::new();

    // 速度（setpts）
    if (speed_val - 1.0).abs() > 0.01 {
        vfilters.push(format!("setpts={:.6}*PTS", 1.0 / speed_val));
    }

    // 亮度 + 对比度（eq 滤镜）
    if brightness_val.abs() > 0.01 || contrast_val.abs() > 0.01 {
        let ffmpeg_contrast = 1.0 + contrast_val;
        vfilters.push(format!(
            "eq=brightness={:.2}:contrast={:.2}",
            brightness_val as f64, ffmpeg_contrast as f64
        ));
    }

    if !vfilters.is_empty() {
        args.push(String::from("-vf"));
        args.push(vfilters.join(","));
    }

    // 5. 音频滤镜链
    let mut afilters: Vec<String> = Vec::new();

    // 音量
    if (volume_val - 1.0).abs() > 0.01 {
        afilters.push(format!("volume={:.2}", volume_val));
    }

    // 音频速度（atempo，与视频同步）
    if (speed_val - 1.0).abs() > 0.01 {
        afilters.push(build_atempo_chain(speed_val));
    }

    if !afilters.is_empty() {
        args.push(String::from("-af"));
        args.push(afilters.join(","));
    }

    // 6. 编码参数
    let video_codec = match format_val.to_lowercase().as_str() {
        "mov" => "libx264",
        "mkv" => "libx265",
        _ => "libx264", // mp4 默认
    };
    args.push(String::from("-c:v"));
    args.push(video_codec.to_string());
    args.push(String::from("-c:a"));
    args.push(String::from("aac"));

    // 7. 输出路径
    args.push(output_path.clone());

    execute_with_progress(&app, &runner, &args, duration, original_size, &output_path).await
}

// ============================================================
// 纯函数：构建 FFmpeg 参数
// ============================================================

/// 视频编码器名称映射（短名 → FFmpeg 编码器名）
pub fn video_encoder_name(codec: &str) -> &str {
    match codec {
        "h264" => "libx264",
        "h265" => "libx265",
        "av1" => "libsvtav1",
        "vp9" => "libvpx-vp9",
        "prores" => "prores_ks",
        "mpeg4" => "mpeg4",
        _ => "libx264",
    }
}

/// 无损剪辑采用流复制，因此边界可能对齐到邻近关键帧。
pub fn build_lossless_trim_args(
    input_path: &str,
    output_path: &str,
    start_secs: f64,
    end_secs: f64,
) -> Vec<String> {
    vec![
        "-y".to_string(),
        "-i".to_string(),
        input_path.to_string(),
        "-ss".to_string(),
        format!("{:.3}", start_secs),
        "-t".to_string(),
        format!("{:.3}", end_secs - start_secs),
        "-map".to_string(),
        "0".to_string(),
        "-c".to_string(),
        "copy".to_string(),
        "-avoid_negative_ts".to_string(),
        "make_zero".to_string(),
        output_path.to_string(),
    ]
}

/// 音频编码器名称映射（短名 → FFmpeg 编码器名）
pub fn audio_encoder_name(codec: &str) -> &str {
    match codec {
        "aac" => "aac",
        "mp3" => "libmp3lame",
        "opus" => "libopus",
        "flac" => "flac",
        "ac3" => "ac3",
        _ => "aac",
    }
}

/// 构造视频裁剪（trim）的 FFmpeg 参数列表（纯函数）
///
/// 使用 `-ss` 前置（输入前定位，精确）+ `-t` 时长控制
/// 默认软件编码（libx264/aac），可通过 hw_encoder 启用 HW 加速
pub fn build_trim_args(
    input_path: &str,
    output_path: &str,
    start_secs: f64,
    end_secs: f64,
    hw_encoder: Option<HwEncoder>,
) -> Vec<String> {
    let duration = end_secs - start_secs;
    let video_codec = hw_encoder.map(|e| e.ffmpeg_name()).unwrap_or("libx264");

    let mut args: Vec<String> = vec![
        String::from("-y"),
        String::from("-ss"),
        format!("{:.3}", start_secs),
        String::from("-i"),
        input_path.to_string(),
        String::from("-t"),
        format!("{:.3}", duration),
        String::from("-c:v"),
        video_codec.to_string(),
        String::from("-c:a"),
        String::from("aac"),
    ];

    // HW 编码器使用 -qp，软件使用 -crf
    if hw_encoder.is_some() {
        args.push(String::from("-qp"));
        args.push(String::from("23"));
    } else {
        args.push(String::from("-crf"));
        args.push(String::from("23"));
    }

    args.push(output_path.to_string());
    args
}

/// 构造视频格式转换（convert）的 FFmpeg 参数列表（纯函数）
///
/// 支持可选的分辨率缩放、帧率转换、CRF 画质、音频比特率
/// WebM/VP9 使用 `-b:v 0` 模式（CRF 主导比特率）
pub fn build_convert_args(
    input_path: &str,
    output_path: &str,
    target_format: &str,
    video_codec: Option<&str>,
    resolution_width: Option<u32>,
    resolution_height: Option<u32>,
    fps: Option<u32>,
    crf: u8,
    audio_codec: Option<&str>,
    audio_bitrate: Option<u32>,
    hw_encoder: Option<HwEncoder>,
) -> Vec<String> {
    // 解析编码器
    let (video_codec_name, audio_codec_name) = match video_codec {
        Some(vc) => (
            video_encoder_name(vc).to_string(),
            audio_encoder_name(audio_codec.unwrap_or("aac")).to_string(),
        ),
        None => {
            let v = match target_format {
                "mp4" | "mov" => "libx264",
                "webm" => "libvpx-vp9",
                "avi" => "mpeg4",
                "mkv" => "libx265",
                _ => "libx264",
            };
            let a = match target_format {
                "webm" => "libopus",
                "avi" => "libmp3lame",
                _ => "aac",
            };
            (v.to_string(), a.to_string())
        }
    };

    let mut args: Vec<String> = vec![
        String::from("-y"),
        String::from("-i"),
        input_path.to_string(),
    ];

    // 分辨率缩放
    if let (Some(w), Some(h)) = (resolution_width, resolution_height) {
        args.push(String::from("-vf"));
        args.push(format!("scale={}:{}", w, h));
    }

    // 帧率
    if let Some(target_fps) = fps {
        args.push(String::from("-r"));
        args.push(target_fps.to_string());
    }

    // 视频编码器（HW 优先，否则用解析结果）
    let final_video_codec = hw_encoder
        .map(|e| e.ffmpeg_name().to_string())
        .unwrap_or(video_codec_name);
    let is_prores = final_video_codec == "prores_ks";
    args.push(String::from("-c:v"));
    args.push(final_video_codec);

    // 画质控制
    if !is_prores {
        if target_format == "webm" {
            args.push(String::from("-crf"));
            args.push(crf.to_string());
            args.push(String::from("-b:v"));
            args.push(String::from("0"));
        } else if hw_encoder.is_some() {
            // HW 编码器使用 -qp
            args.push(String::from("-qp"));
            args.push(crf.to_string());
        } else {
            args.push(String::from("-crf"));
            args.push(crf.to_string());
        }
    }

    // 音频编码器
    args.push(String::from("-c:a"));
    args.push(audio_codec_name);

    // 音频比特率
    if let Some(br) = audio_bitrate {
        args.push(String::from("-b:a"));
        args.push(format!("{}k", br / 1000));
    }

    args.push(output_path.to_string());
    args
}

// ============================================================
// 硬件加速编码器
// ============================================================

/// 支持的硬件加速编码器类型
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
pub enum HwEncoder {
    /// NVIDIA NVENC (h264_nvenc)
    Nvenc,
    /// Intel Quick Sync (h264_qsv)
    Qsv,
    /// AMD AMF (h264_amf)
    Amf,
    /// Apple VideoToolbox (h264_videotoolbox)
    VideoToolbox,
}

impl HwEncoder {
    /// 返回 FFmpeg 编码器名称
    pub fn ffmpeg_name(&self) -> &'static str {
        match self {
            HwEncoder::Nvenc => "h264_nvenc",
            HwEncoder::Qsv => "h264_qsv",
            HwEncoder::Amf => "h264_amf",
            HwEncoder::VideoToolbox => "h264_videotoolbox",
        }
    }
}

/// 构造压缩视频的 FFmpeg 参数列表（纯函数，可单元测试）
///
/// 根据 preset/crf/scale/hw_encoder 生成完整的 FFmpeg 命令参数。
/// 内部已经预设了 `-y` / `-i` / `-c:a aac -b:a 128k` 等公共参数。
pub fn build_compress_args(
    input_path: &str,
    output_path: &str,
    preset: &str,
    crf: u8,
    scale: Option<&str>,
    hw_encoder: Option<HwEncoder>,
) -> Vec<String> {
    let mut args: Vec<String> = vec![
        String::from("-y"),
        String::from("-i"),
        input_path.to_string(),
    ];

    // 分辨率缩放
    if let Some(s) = scale {
        args.push(String::from("-vf"));
        args.push(format!("scale={}", s));
    }

    // 选择编码器
    let video_codec = match hw_encoder {
        Some(enc) => enc.ffmpeg_name(),
        None => "libx264",
    };
    args.push(String::from("-c:v"));
    args.push(video_codec.to_string());

    // 画质参数
    match hw_encoder {
        Some(_) => {
            // HW 编码器使用 -qp 而非 -crf（NVENC/QSV/AMF 通用）
            args.push(String::from("-qp"));
            args.push(crf.to_string());
        }
        None => {
            args.push(String::from("-crf"));
            args.push(crf.to_string());
            args.push(String::from("-preset"));
            // 根据 preset 决定 x264 preset
            let x264_preset = match preset {
                "light" => "ultrafast",
                "extreme" => "slow",
                _ => "medium",
            };
            args.push(x264_preset.to_string());
        }
    }

    // 音频公共参数
    args.push(String::from("-c:a"));
    args.push(String::from("aac"));
    args.push(String::from("-b:a"));
    args.push(String::from("128k"));

    args.push(output_path.to_string());
    args
}

/// 探测系统可用的硬件加速编码器（按优先级 NVENC > QSV > AMF > VideoToolbox）
///
/// 通过执行 `ffmpeg -hide_banner -encoders` 检查编码器是否存在。
/// 返回 `None` 表示系统没有可用的 HW 编码器，将回退到 libx264。
pub async fn detect_hw_encoder() -> Option<HwEncoder> {
    let ffmpeg = get_ffmpeg_path()?;

    let output = Command::new(&ffmpeg)
        .arg("-hide_banner")
        .arg("-encoders")
        .output()
        .await
        .ok()?;

    if !output.status.success() {
        return None;
    }

    let stdout = String::from_utf8_lossy(&output.stdout);

    // 按优先级检查
    if stdout.contains("h264_nvenc") {
        Some(HwEncoder::Nvenc)
    } else if stdout.contains("h264_qsv") {
        Some(HwEncoder::Qsv)
    } else if stdout.contains("h264_amf") {
        Some(HwEncoder::Amf)
    } else if stdout.contains("h264_videotoolbox") {
        Some(HwEncoder::VideoToolbox)
    } else {
        None
    }
}

// ============================================================
// 单元测试（TDD）
// ============================================================

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_build_compress_args_default_uses_libx264() {
        let args = build_compress_args("/input.mp4", "/output.mp4", "standard", 26, None, None);

        // 必须包含输入输出
        assert!(args.contains(&"-i".to_string()));
        assert!(args.iter().any(|a| a == "/input.mp4"));
        assert!(args.iter().any(|a| a == "/output.mp4"));

        // 软件编码路径
        assert!(args.contains(&"-c:v".to_string()));
        assert!(args.contains(&"libx264".to_string()));
        assert!(args.contains(&"-crf".to_string()));
        assert!(args.contains(&"26".to_string()));
        assert!(args.contains(&"-preset".to_string()));
        assert!(args.contains(&"medium".to_string()));

        // 音频公共参数
        assert!(args.contains(&"-c:a".to_string()));
        assert!(args.contains(&"aac".to_string()));
        assert!(args.contains(&"-b:a".to_string()));
        assert!(args.contains(&"128k".to_string()));
    }

    #[test]
    fn test_build_compress_args_with_scale() {
        let args = build_compress_args(
            "/in.mp4",
            "/out.mp4",
            "standard",
            26,
            Some("1920:1080"),
            None,
        );

        assert!(args.contains(&"-vf".to_string()));
        assert!(args.contains(&"scale=1920:1080".to_string()));
    }

    #[test]
    fn test_build_compress_args_preset_light_uses_ultrafast() {
        let args = build_compress_args("/in.mp4", "/out.mp4", "light", 20, None, None);

        assert!(args.contains(&"ultrafast".to_string()));
        assert!(args.contains(&"20".to_string()));
    }

    #[test]
    fn test_build_compress_args_preset_extreme_uses_slow() {
        let args = build_compress_args("/in.mp4", "/out.mp4", "extreme", 34, None, None);

        assert!(args.contains(&"slow".to_string()));
        assert!(args.contains(&"34".to_string()));
    }

    #[test]
    fn test_build_compress_args_with_nvenc() {
        let args = build_compress_args(
            "/in.mp4",
            "/out.mp4",
            "standard",
            23,
            None,
            Some(HwEncoder::Nvenc),
        );

        // NVENC 路径
        assert!(args.contains(&"h264_nvenc".to_string()));
        // HW 编码器使用 -qp 而非 -crf
        assert!(args.contains(&"-qp".to_string()));
        assert!(args.contains(&"23".to_string()));
        // 不应该包含软件编码器参数
        assert!(!args.contains(&"libx264".to_string()));
        assert!(!args.contains(&"-crf".to_string()));
        assert!(!args.contains(&"-preset".to_string()));
    }

    #[test]
    fn test_build_compress_args_with_qsv() {
        let args = build_compress_args(
            "/in.mp4",
            "/out.mp4",
            "standard",
            26,
            None,
            Some(HwEncoder::Qsv),
        );

        assert!(args.contains(&"h264_qsv".to_string()));
        assert!(args.contains(&"-qp".to_string()));
    }

    #[test]
    fn test_build_compress_args_with_amf() {
        let args = build_compress_args(
            "/in.mp4",
            "/out.mp4",
            "standard",
            26,
            None,
            Some(HwEncoder::Amf),
        );

        assert!(args.contains(&"h264_amf".to_string()));
    }

    #[test]
    fn test_build_compress_args_with_videotoolbox() {
        let args = build_compress_args(
            "/in.mp4",
            "/out.mp4",
            "standard",
            26,
            None,
            Some(HwEncoder::VideoToolbox),
        );

        assert!(args.contains(&"h264_videotoolbox".to_string()));
    }

    #[test]
    fn test_hw_encoder_ffmpeg_name() {
        assert_eq!(HwEncoder::Nvenc.ffmpeg_name(), "h264_nvenc");
        assert_eq!(HwEncoder::Qsv.ffmpeg_name(), "h264_qsv");
        assert_eq!(HwEncoder::Amf.ffmpeg_name(), "h264_amf");
        assert_eq!(HwEncoder::VideoToolbox.ffmpeg_name(), "h264_videotoolbox");
    }

    #[test]
    fn test_build_compress_args_full_pipeline_with_scale_and_hw() {
        let args = build_compress_args(
            "/in.mp4",
            "/out.mp4",
            "standard",
            28,
            Some("1280:720"),
            Some(HwEncoder::Nvenc),
        );

        // 验证完整参数序列的关键节点
        assert!(args[0] == "-y");
        assert!(args.contains(&"-i".to_string()));
        assert!(args.contains(&"scale=1280:720".to_string()));
        assert!(args.contains(&"h264_nvenc".to_string()));
        assert!(args.contains(&"-qp".to_string()));
        assert!(args.contains(&"28".to_string()));
        assert!(args.contains(&"aac".to_string()));
        assert!(args.iter().any(|a| a == "/out.mp4"));
    }

    // ============================================================
    // build_trim_args 测试 (6 个)
    // ============================================================

    #[test]
    fn test_build_trim_args_basic() {
        let args = build_trim_args("/in.mp4", "/out.mp4", 5.0, 15.0, None);

        // 关键参数
        assert!(args.contains(&"-ss".to_string()));
        assert!(args.contains(&"5.000".to_string()));
        assert!(args.contains(&"-i".to_string()));
        assert!(args.contains(&"/in.mp4".to_string()));
        assert!(args.contains(&"-t".to_string()));
        assert!(args.contains(&"10.000".to_string()));
        assert!(args.contains(&"-c:v".to_string()));
        assert!(args.contains(&"libx264".to_string()));
        assert!(args.contains(&"-c:a".to_string()));
        assert!(args.contains(&"aac".to_string()));
        assert!(args.contains(&"-crf".to_string()));
        assert!(args.contains(&"23".to_string()));
        assert!(args.iter().any(|a| a == "/out.mp4"));
    }

    #[test]
    fn test_build_trim_args_with_nvenc() {
        let args = build_trim_args("/in.mp4", "/out.mp4", 0.0, 30.0, Some(HwEncoder::Nvenc));

        assert!(args.contains(&"h264_nvenc".to_string()));
        // HW 编码器使用 -qp 而非 -crf
        assert!(args.contains(&"-qp".to_string()));
        assert!(!args.contains(&"libx264".to_string()));
    }

    #[test]
    fn test_build_trim_args_with_qsv() {
        let args = build_trim_args("/in.mp4", "/out.mp4", 0.0, 30.0, Some(HwEncoder::Qsv));
        assert!(args.contains(&"h264_qsv".to_string()));
    }

    #[test]
    fn test_build_trim_args_calculates_duration() {
        let args = build_trim_args("/in.mp4", "/out.mp4", 2.5, 7.75, None);
        // duration = 7.75 - 2.5 = 5.25
        assert!(args.contains(&"5.250".to_string()));
        assert!(args.contains(&"2.500".to_string()));
    }

    #[test]
    fn test_build_trim_args_zero_start() {
        let args = build_trim_args("/in.mp4", "/out.mp4", 0.0, 60.0, None);
        assert!(args.contains(&"0.000".to_string()));
        assert!(args.contains(&"60.000".to_string()));
    }

    #[test]
    fn test_video_encoder_name_mapping() {
        assert_eq!(video_encoder_name("h264"), "libx264");
        assert_eq!(video_encoder_name("h265"), "libx265");
        assert_eq!(video_encoder_name("av1"), "libsvtav1");
        assert_eq!(video_encoder_name("vp9"), "libvpx-vp9");
        assert_eq!(video_encoder_name("prores"), "prores_ks");
        assert_eq!(video_encoder_name("mpeg4"), "mpeg4");
        assert_eq!(video_encoder_name("unknown"), "libx264"); // fallback
    }

    #[test]
    fn test_build_lossless_trim_args_uses_stream_copy_and_output_seek() {
        let args = build_lossless_trim_args("/in.mp4", "/out.mp4", 2.5, 8.0);
        assert_eq!(
            args,
            vec![
                "-y",
                "-i",
                "/in.mp4",
                "-ss",
                "2.500",
                "-t",
                "5.500",
                "-map",
                "0",
                "-c",
                "copy",
                "-avoid_negative_ts",
                "make_zero",
                "/out.mp4",
            ]
        );
    }

    #[test]
    fn test_unique_output_path_keeps_free_name_and_suffixes_existing_name() {
        let root =
            std::env::temp_dir().join(format!("tinypix-video-output-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&root).unwrap();
        let requested = root.join("演示.mp4");
        assert_eq!(unique_output_path(&requested), requested);
        std::fs::write(&requested, b"existing").unwrap();
        assert_eq!(unique_output_path(&requested), root.join("演示 (1).mp4"));
        let _ = std::fs::remove_dir_all(root);
    }

    // ============================================================
    // build_convert_args 测试 (6 个)
    // ============================================================

    #[test]
    fn test_build_convert_args_default_mp4() {
        let args = build_convert_args(
            "/in.mp4", "/out.mp4", "mp4", None, None, None, None, 23, None, None, None,
        );

        // mp4 默认 h264/aac
        assert!(args.contains(&"libx264".to_string()));
        assert!(args.contains(&"aac".to_string()));
        assert!(args.contains(&"-crf".to_string()));
        assert!(args.contains(&"23".to_string()));
        assert!(args.iter().any(|a| a == "/in.mp4"));
        assert!(args.iter().any(|a| a == "/out.mp4"));
    }

    #[test]
    fn test_build_convert_args_webm_uses_vp9() {
        let args = build_convert_args(
            "/in.mp4",
            "/out.webm",
            "webm",
            None,
            None,
            None,
            None,
            30,
            None,
            None,
            None,
        );

        assert!(args.contains(&"libvpx-vp9".to_string()));
        assert!(args.contains(&"libopus".to_string()));
        // WebM 模式使用 -b:v 0
        assert!(args.contains(&"-b:v".to_string()));
        assert!(args.contains(&"0".to_string()));
    }

    #[test]
    fn test_build_convert_args_with_resolution() {
        let args = build_convert_args(
            "/in.mp4",
            "/out.mp4",
            "mp4",
            None,
            Some(1920),
            Some(1080),
            None,
            23,
            None,
            None,
            None,
        );

        assert!(args.contains(&"-vf".to_string()));
        assert!(args.contains(&"scale=1920:1080".to_string()));
    }

    #[test]
    fn test_build_convert_args_with_fps() {
        let args = build_convert_args(
            "/in.mp4",
            "/out.mp4",
            "mp4",
            None,
            None,
            None,
            Some(60),
            23,
            None,
            None,
            None,
        );

        assert!(args.contains(&"-r".to_string()));
        assert!(args.contains(&"60".to_string()));
    }

    #[test]
    fn test_build_convert_args_with_audio_bitrate() {
        let args = build_convert_args(
            "/in.mp4",
            "/out.mp4",
            "mp4",
            None,
            None,
            None,
            None,
            23,
            Some("aac"),
            Some(192000),
            None,
        );

        assert!(args.contains(&"-b:a".to_string()));
        assert!(args.contains(&"192k".to_string()));
    }

    #[test]
    fn test_build_convert_args_explicit_h264_with_nvenc_override() {
        let args = build_convert_args(
            "/in.mp4",
            "/out.mp4",
            "mp4",
            Some("h264"),
            None,
            None,
            None,
            25,
            Some("aac"),
            Some(128000),
            Some(HwEncoder::Nvenc),
        );

        // HW 编码器覆盖 h264
        assert!(args.contains(&"h264_nvenc".to_string()));
        // HW 使用 -qp
        assert!(args.contains(&"-qp".to_string()));
        assert!(args.contains(&"25".to_string()));
        assert!(!args.contains(&"libx264".to_string()));
        assert!(args.contains(&"aac".to_string()));
        assert!(args.contains(&"128k".to_string()));
    }

    #[test]
    fn gif_palette_filter_omits_scale_for_original_dimensions() {
        let filter = build_gif_palette_filter(15, None, "diff", 128, "sierra2_4a");
        assert!(filter.starts_with("fps=15,split"));
        assert!(!filter.contains("scale="));
    }

    #[test]
    fn gif_palette_filter_scales_only_when_width_is_selected() {
        let filter = build_gif_palette_filter(15, Some(720), "diff", 128, "sierra2_4a");
        assert!(filter.contains("fps=15,scale=720:-1:flags=lanczos,split"));
    }
}
