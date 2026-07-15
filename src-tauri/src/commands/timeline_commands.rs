use crate::infrastructure::ffmpeg_manager::media_engine_cache_token;
use crate::infrastructure::ffmpeg_runner::FFmpegRunner;
use crate::infrastructure::preview_cache::{
    commit_generated_preview, default_preview_root, is_nonempty_cache_file, preview_cache_key,
};
use crate::infrastructure::validation::{
    unique_output_path, validate_output_path, validate_time_range, validate_video_path,
};
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use tauri::{Emitter, Manager};

const TIMELINE_ASSET_VERSION: &str = "1";

#[derive(Debug, Clone, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct VideoEditSegment {
    pub id: String,
    pub start_secs: f64,
    pub end_secs: f64,
    pub included: bool,
}

#[derive(Debug, Clone, Copy, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum VideoEditMode {
    Lossless,
    Precise,
}

#[derive(Debug, Clone, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct TimelineAssetsResult {
    pub task_id: String,
    pub stage: String,
    pub percent: f64,
    pub filmstrip_path: String,
    pub waveform_path: Option<String>,
    pub failure_type: Option<String>,
    pub retryable: bool,
}

#[derive(Debug, Clone, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct VideoEditExportResult {
    pub task_id: String,
    pub stage: String,
    pub percent: f64,
    pub output_path: String,
    pub failure_type: Option<String>,
    pub retryable: bool,
}

#[derive(Debug, Clone, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct TimelineFailure {
    pub failure_type: String,
    pub message: String,
    pub retryable: bool,
}

impl TimelineFailure {
    fn new(failure_type: &str, message: impl Into<String>, retryable: bool) -> Self {
        Self {
            failure_type: failure_type.to_string(),
            message: message.into(),
            retryable,
        }
    }

    fn invalid(message: impl Into<String>) -> Self {
        Self::new("invalid-parameter", message, false)
    }

    fn processing(message: impl Into<String>) -> Self {
        Self::new("processing-failed", message, true)
    }
}

type TimelineResult<T> = Result<T, TimelineFailure>;

struct TemporaryDirectory(PathBuf);

impl Drop for TemporaryDirectory {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.0);
    }
}

fn task_id_or_new(task_id: Option<String>) -> String {
    task_id.unwrap_or_else(|| uuid::Uuid::new_v4().to_string())
}

fn emit_progress(app: &tauri::AppHandle, task_id: &str, stage: &str, percent: f64) {
    let _ = app.emit(
        "media-task-progress",
        serde_json::json!({
            "taskId": task_id,
            "stage": stage,
            "percent": percent.clamp(0.0, 100.0),
        }),
    );
}

fn authorize_asset(app: &tauri::AppHandle, path: &Path) -> TimelineResult<()> {
    app.asset_protocol_scope()
        .allow_file(path)
        .map_err(|error| {
            TimelineFailure::new(
                "asset-authorization-failed",
                format!("无法授权时间线缓存文件: {error}"),
                true,
            )
        })
}

fn validate_media_input(input_path: &str, duration_secs: f64, fps: f64) -> TimelineResult<()> {
    validate_video_path(input_path).map_err(|error| TimelineFailure::invalid(error.to_string()))?;
    if !duration_secs.is_finite() || duration_secs <= 0.0 {
        return Err(TimelineFailure::invalid("视频时长必须大于 0"));
    }
    if !fps.is_finite() || fps <= 0.0 {
        return Err(TimelineFailure::invalid("视频帧率必须大于 0"));
    }
    Ok(())
}

fn included_segments(
    segments: &[VideoEditSegment],
    duration_secs: f64,
) -> TimelineResult<Vec<VideoEditSegment>> {
    let included: Vec<_> = segments
        .iter()
        .filter(|segment| segment.included)
        .cloned()
        .collect();
    if included.is_empty() {
        return Err(TimelineFailure::invalid("至少保留一个视频片段"));
    }
    let mut last_end = 0.0;
    for segment in &included {
        if !segment.start_secs.is_finite() || !segment.end_secs.is_finite() {
            return Err(TimelineFailure::invalid("片段时间必须是有效数字"));
        }
        validate_time_range(segment.start_secs, segment.end_secs, duration_secs)
            .map_err(|error| TimelineFailure::invalid(error.to_string()))?;
        if segment.start_secs + f64::EPSILON < last_end {
            return Err(TimelineFailure::invalid("保留片段不能重叠或倒序"));
        }
        last_end = segment.end_secs;
    }
    Ok(included)
}

fn timeline_asset_paths(input: &Path) -> TimelineResult<(PathBuf, PathBuf)> {
    let engine_token = format!("{}:{TIMELINE_ASSET_VERSION}", media_engine_cache_token());
    let key = preview_cache_key(input, &engine_token).map_err(TimelineFailure::processing)?;
    let directory = default_preview_root()
        .map_err(TimelineFailure::processing)?
        .join("timeline")
        .join(key);
    Ok((
        directory.join("filmstrip.png"),
        directory.join("waveform.png"),
    ))
}

pub fn build_filmstrip_args(input: &str, output: &str, duration_secs: f64) -> Vec<String> {
    let tile_count = duration_secs.ceil().clamp(1.0, 10.0) as u32;
    let interval = (duration_secs / f64::from(tile_count)).max(0.04);
    vec![
        "-y".to_string(),
        "-i".to_string(),
        input.to_string(),
        "-map".to_string(),
        "0:v:0".to_string(),
        "-vf".to_string(),
        format!(
            "fps=1/{interval:.6},scale=160:-2:flags=lanczos,tile={tile_count}x1:padding=0:margin=0"
        ),
        "-frames:v".to_string(),
        "1".to_string(),
        output.to_string(),
    ]
}

pub fn build_waveform_args(input: &str, output: &str) -> Vec<String> {
    vec![
        "-y".to_string(),
        "-i".to_string(),
        input.to_string(),
        "-map".to_string(),
        "0:a:0".to_string(),
        "-filter_complex".to_string(),
        "showwavespic=s=1200x160:split_channels=1:colors=0x9ae600".to_string(),
        "-frames:v".to_string(),
        "1".to_string(),
        output.to_string(),
    ]
}

async fn generate_cached_asset(
    runner: &FFmpegRunner,
    destination: &Path,
    args_builder: impl FnOnce(&str) -> Vec<String>,
) -> TimelineResult<()> {
    if is_valid_png_asset(destination) {
        return Ok(());
    }
    if destination.exists() {
        let _ = tokio::fs::remove_file(destination).await;
    }
    let parent = destination
        .parent()
        .ok_or_else(|| TimelineFailure::processing("时间线缓存路径无父目录"))?;
    tokio::fs::create_dir_all(parent).await.map_err(|error| {
        TimelineFailure::new(
            "directory-permission-denied",
            format!("无法创建时间线缓存目录: {error}"),
            true,
        )
    })?;
    let temporary = parent.join(format!(".{}.tmp.png", uuid::Uuid::new_v4()));
    let args = args_builder(&temporary.to_string_lossy());
    let execution = runner.run_simple(&args).await;
    if let Err(error) = execution {
        let _ = tokio::fs::remove_file(&temporary).await;
        return Err(TimelineFailure::processing(error.to_string()));
    }
    commit_generated_preview(&temporary, destination).map_err(TimelineFailure::processing)
}

fn is_valid_png_asset(path: &Path) -> bool {
    is_nonempty_cache_file(path)
        && std::fs::read(path)
            .ok()
            .is_some_and(|bytes| bytes.starts_with(b"\x89PNG\r\n\x1a\n"))
        && image::open(path).is_ok()
}

#[tauri::command]
pub async fn generate_timeline_assets(
    app: tauri::AppHandle,
    input_path: String,
    duration_secs: f64,
    fps: f64,
    has_audio: bool,
    task_id: Option<String>,
) -> TimelineResult<TimelineAssetsResult> {
    validate_media_input(&input_path, duration_secs, fps)?;
    let task_id = task_id_or_new(task_id);
    let input = PathBuf::from(&input_path);
    let (filmstrip, waveform) = timeline_asset_paths(&input)?;
    let runner = FFmpegRunner::new()
        .map_err(|error| TimelineFailure::new("engine-unavailable", error.to_string(), true))?;

    emit_progress(&app, &task_id, "filmstrip", 10.0);
    generate_cached_asset(&runner, &filmstrip, |temporary| {
        build_filmstrip_args(&input_path, temporary, duration_secs)
    })
    .await?;
    emit_progress(&app, &task_id, "filmstrip", 65.0);

    let waveform_path = if has_audio {
        generate_cached_asset(&runner, &waveform, |temporary| {
            build_waveform_args(&input_path, temporary)
        })
        .await?;
        Some(waveform)
    } else {
        None
    };

    authorize_asset(&app, &filmstrip)?;
    if let Some(path) = &waveform_path {
        authorize_asset(&app, path)?;
    }
    emit_progress(&app, &task_id, "ready", 100.0);
    Ok(TimelineAssetsResult {
        task_id,
        stage: "ready".to_string(),
        percent: 100.0,
        filmstrip_path: filmstrip.to_string_lossy().to_string(),
        waveform_path: waveform_path.map(|path| path.to_string_lossy().to_string()),
        failure_type: None,
        retryable: false,
    })
}

fn lossless_output_is_supported(path: &Path) -> bool {
    path.extension()
        .and_then(|extension| extension.to_str())
        .is_some_and(|extension| {
            matches!(
                extension.to_ascii_lowercase().as_str(),
                "mp4" | "mov" | "mkv"
            )
        })
}

fn precise_output_is_supported(path: &Path) -> bool {
    path.extension()
        .and_then(|extension| extension.to_str())
        .is_some_and(|extension| extension.eq_ignore_ascii_case("mp4"))
}

pub fn build_lossless_segment_args(
    input: &str,
    output: &str,
    segment: &VideoEditSegment,
    has_audio: bool,
) -> Vec<String> {
    let mut args = vec![
        "-y".to_string(),
        "-ss".to_string(),
        format!("{:.6}", segment.start_secs),
        "-i".to_string(),
        input.to_string(),
        "-t".to_string(),
        format!("{:.6}", segment.end_secs - segment.start_secs),
        "-map".to_string(),
        "0:v:0".to_string(),
    ];
    if has_audio {
        args.extend(["-map".to_string(), "0:a:0?".to_string()]);
    }
    args.extend([
        "-c".to_string(),
        "copy".to_string(),
        "-avoid_negative_ts".to_string(),
        "make_zero".to_string(),
        output.to_string(),
    ]);
    args
}

fn concat_path_line(path: &Path) -> String {
    let normalized = path.to_string_lossy().replace('\\', "/");
    format!("file '{}'", normalized.replace('\'', "'\\''"))
}

pub fn build_lossless_concat_args(list_path: &str, output: &str) -> Vec<String> {
    let mut args = vec![
        "-y".to_string(),
        "-f".to_string(),
        "concat".to_string(),
        "-safe".to_string(),
        "0".to_string(),
        "-i".to_string(),
        list_path.to_string(),
        "-c".to_string(),
        "copy".to_string(),
    ];
    let is_mov_family = Path::new(output)
        .extension()
        .and_then(|extension| extension.to_str())
        .is_some_and(|extension| {
            extension.eq_ignore_ascii_case("mp4") || extension.eq_ignore_ascii_case("mov")
        });
    if is_mov_family {
        args.extend(["-movflags".to_string(), "+faststart".to_string()]);
    }
    args.push(output.to_string());
    args
}

pub fn build_precise_export_args(
    input: &str,
    output: &str,
    segments: &[VideoEditSegment],
    has_audio: bool,
) -> Vec<String> {
    let mut filters = Vec::new();
    for (index, segment) in segments.iter().enumerate() {
        filters.push(format!(
            "[0:v:0]trim=start={:.6}:end={:.6},setpts=PTS-STARTPTS[v{index}]",
            segment.start_secs, segment.end_secs
        ));
        if has_audio {
            filters.push(format!(
                "[0:a:0]atrim=start={:.6}:end={:.6},asetpts=PTS-STARTPTS[a{index}]",
                segment.start_secs, segment.end_secs
            ));
        }
    }
    let concat_inputs = (0..segments.len())
        .map(|index| {
            if has_audio {
                format!("[v{index}][a{index}]")
            } else {
                format!("[v{index}]")
            }
        })
        .collect::<String>();
    if has_audio {
        filters.push(format!(
            "{concat_inputs}concat=n={}:v=1:a=1[outv][outa]",
            segments.len()
        ));
    } else {
        filters.push(format!(
            "{concat_inputs}concat=n={}:v=1:a=0[outv]",
            segments.len()
        ));
    }

    let mut args = vec![
        "-y".to_string(),
        "-i".to_string(),
        input.to_string(),
        "-filter_complex".to_string(),
        filters.join(";"),
        "-map".to_string(),
        "[outv]".to_string(),
    ];
    if has_audio {
        args.extend(["-map".to_string(), "[outa]".to_string()]);
    }
    args.extend([
        "-c:v".to_string(),
        "libx264".to_string(),
        "-pix_fmt".to_string(),
        "yuv420p".to_string(),
        "-preset".to_string(),
        "medium".to_string(),
        "-crf".to_string(),
        "20".to_string(),
    ]);
    if has_audio {
        args.extend([
            "-c:a".to_string(),
            "aac".to_string(),
            "-b:a".to_string(),
            "192k".to_string(),
        ]);
    } else {
        args.push("-an".to_string());
    }
    args.extend([
        "-movflags".to_string(),
        "+faststart".to_string(),
        output.to_string(),
    ]);
    args
}

async fn export_lossless(
    app: &tauri::AppHandle,
    runner: &FFmpegRunner,
    task_id: &str,
    input_path: &str,
    output_path: &Path,
    segments: &[VideoEditSegment],
    has_audio: bool,
) -> TimelineResult<()> {
    let parent = output_path
        .parent()
        .ok_or_else(|| TimelineFailure::invalid("输出路径无父目录"))?;
    let temp_path = parent.join(format!(".tinypix-edit-{}", uuid::Uuid::new_v4()));
    tokio::fs::create_dir_all(&temp_path)
        .await
        .map_err(|error| {
            TimelineFailure::new(
                "directory-permission-denied",
                format!("无法创建剪辑临时目录: {error}"),
                true,
            )
        })?;
    let _cleanup = TemporaryDirectory(temp_path.clone());
    let mut part_paths = Vec::with_capacity(segments.len());
    for (index, segment) in segments.iter().enumerate() {
        let part_path = temp_path.join(format!("part-{index:04}.mkv"));
        let args = build_lossless_segment_args(
            input_path,
            &part_path.to_string_lossy(),
            segment,
            has_audio,
        );
        runner
            .run_simple(&args)
            .await
            .map_err(|error| TimelineFailure::processing(error.to_string()))?;
        part_paths.push(part_path);
        let percent = 10.0 + (index + 1) as f64 / segments.len() as f64 * 70.0;
        emit_progress(app, task_id, "copying-segments", percent);
    }
    let concat_file = temp_path.join("concat.txt");
    let concat_body = part_paths
        .iter()
        .map(|path| concat_path_line(path))
        .collect::<Vec<_>>()
        .join("\n");
    tokio::fs::write(&concat_file, concat_body)
        .await
        .map_err(|error| TimelineFailure::processing(format!("写入合并清单失败: {error}")))?;
    let args = build_lossless_concat_args(
        &concat_file.to_string_lossy(),
        &output_path.to_string_lossy(),
    );
    runner.run_simple(&args).await.map_err(|error| {
        TimelineFailure::new(
            "container-incompatible",
            format!("无损片段与所选封装不兼容，请改用精确模式: {error}"),
            true,
        )
    })?;
    Ok(())
}

#[tauri::command]
#[allow(clippy::too_many_arguments)]
pub async fn export_video_edit(
    app: tauri::AppHandle,
    input_path: String,
    duration_secs: f64,
    fps: f64,
    has_audio: bool,
    segments: Vec<VideoEditSegment>,
    mode: VideoEditMode,
    output_path: String,
    task_id: Option<String>,
) -> TimelineResult<VideoEditExportResult> {
    validate_media_input(&input_path, duration_secs, fps)?;
    validate_output_path(&output_path)
        .map_err(|error| TimelineFailure::invalid(error.to_string()))?;
    let included = included_segments(&segments, duration_secs)?;
    let requested_output = PathBuf::from(output_path);
    match mode {
        VideoEditMode::Lossless if !lossless_output_is_supported(&requested_output) => {
            return Err(TimelineFailure::new(
                "container-incompatible",
                "无损模式仅支持 MP4、MOV 或 MKV；WebM/AVI 请改用对应编码器或选择精确 MP4",
                false,
            ));
        }
        VideoEditMode::Precise if !precise_output_is_supported(&requested_output) => {
            return Err(TimelineFailure::new(
                "container-incompatible",
                "精确模式固定输出 MP4/H.264/AAC，请将扩展名改为 .mp4",
                false,
            ));
        }
        _ => {}
    }

    let task_id = task_id_or_new(task_id);
    let output = unique_output_path(&requested_output);
    let runner = FFmpegRunner::new()
        .map_err(|error| TimelineFailure::new("engine-unavailable", error.to_string(), true))?;
    emit_progress(&app, &task_id, "preparing", 5.0);
    match mode {
        VideoEditMode::Lossless => {
            export_lossless(
                &app,
                &runner,
                &task_id,
                &input_path,
                &output,
                &included,
                has_audio,
            )
            .await?;
        }
        VideoEditMode::Precise => {
            let args = build_precise_export_args(
                &input_path,
                &output.to_string_lossy(),
                &included,
                has_audio,
            );
            emit_progress(&app, &task_id, "encoding", 15.0);
            runner
                .run_simple(&args)
                .await
                .map_err(|error| TimelineFailure::processing(error.to_string()))?;
        }
    }
    emit_progress(&app, &task_id, "completed", 100.0);
    Ok(VideoEditExportResult {
        task_id,
        stage: "completed".to_string(),
        percent: 100.0,
        output_path: output.to_string_lossy().to_string(),
        failure_type: None,
        retryable: false,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn segment(id: &str, start_secs: f64, end_secs: f64, included: bool) -> VideoEditSegment {
        VideoEditSegment {
            id: id.to_string(),
            start_secs,
            end_secs,
            included,
        }
    }

    #[test]
    fn precise_multi_segment_filter_concats_video_and_audio() {
        let args = build_precise_export_args(
            "C:/素材/source.mov",
            "C:/输出/result.mp4",
            &[segment("a", 0.0, 2.5, true), segment("b", 7.0, 9.25, true)],
            true,
        );
        let filter = args
            .iter()
            .position(|arg| arg == "-filter_complex")
            .map(|index| &args[index + 1])
            .unwrap();
        assert!(filter.contains("trim=start=0.000000:end=2.500000"));
        assert!(filter.contains("atrim=start=7.000000:end=9.250000"));
        assert!(filter.contains("[v0][a0][v1][a1]concat=n=2:v=1:a=1[outv][outa]"));
        assert!(args.windows(2).any(|pair| pair == ["-c:v", "libx264"]));
        assert!(args.windows(2).any(|pair| pair == ["-c:a", "aac"]));
    }

    #[test]
    fn precise_video_only_export_never_references_audio_stream() {
        let args = build_precise_export_args(
            "input.mp4",
            "output.mp4",
            &[segment("a", 1.0, 3.0, true)],
            false,
        );
        let joined = args.join(" ");
        assert!(!joined.contains("0:a:0"));
        assert!(!joined.contains("[outa]"));
        assert!(joined.contains("concat=n=1:v=1:a=0[outv]"));
        assert!(args.contains(&"-an".to_string()));
    }

    #[test]
    fn lossless_segment_uses_stream_copy_and_optional_audio() {
        let args = build_lossless_segment_args(
            "input.mov",
            "part.mkv",
            &segment("part", 3.0, 8.5, true),
            true,
        );
        assert!(args.windows(2).any(|pair| pair == ["-ss", "3.000000"]));
        assert!(args.windows(2).any(|pair| pair == ["-t", "5.500000"]));
        assert!(args.windows(2).any(|pair| pair == ["-c", "copy"]));
        assert!(args.contains(&"0:a:0?".to_string()));
    }

    #[test]
    fn lossless_concat_only_adds_faststart_to_mov_family_outputs() {
        let mp4 = build_lossless_concat_args("concat.txt", "output.mp4");
        assert!(mp4
            .windows(2)
            .any(|pair| pair == ["-movflags", "+faststart"]));

        let mkv = build_lossless_concat_args("concat.txt", "output.mkv");
        assert!(!mkv.contains(&"-movflags".to_string()));
    }

    #[test]
    fn ignored_segments_are_not_exported_and_overlap_is_rejected() {
        let selected = included_segments(
            &[
                segment("keep", 0.0, 2.0, true),
                segment("delete", 2.0, 4.0, false),
                segment("keep-two", 4.0, 6.0, true),
            ],
            10.0,
        )
        .unwrap();
        assert_eq!(selected.len(), 2);
        assert_eq!(selected[1].id, "keep-two");

        let error = included_segments(
            &[
                segment("first", 0.0, 5.0, true),
                segment("overlap", 4.0, 6.0, true),
            ],
            10.0,
        )
        .unwrap_err();
        assert_eq!(error.failure_type, "invalid-parameter");
    }

    #[test]
    fn filmstrip_is_bounded_to_ten_tiles_and_waveform_uses_audio() {
        let filmstrip = build_filmstrip_args("input.mov", "filmstrip.png", 120.0);
        let joined = filmstrip.join(" ");
        assert!(joined.contains("tile=10x1"));
        assert!(joined.contains("scale=160:-2"));

        let waveform = build_waveform_args("input.mov", "waveform.png");
        assert!(waveform.windows(2).any(|pair| pair == ["-map", "0:a:0"]));
        assert!(waveform.join(" ").contains("showwavespic=s=1200x160"));
    }

    #[test]
    fn concat_list_escapes_quotes_and_windows_separators() {
        let line = concat_path_line(Path::new("C:\\用户\\it's\\part.mkv"));
        assert_eq!(line, "file 'C:/用户/it'\\''s/part.mkv'");
    }

    #[test]
    fn incompatible_output_containers_are_rejected_before_execution() {
        assert!(!lossless_output_is_supported(Path::new("output.webm")));
        assert!(!lossless_output_is_supported(Path::new("output.avi")));
        assert!(lossless_output_is_supported(Path::new("output.mkv")));
        assert!(!precise_output_is_supported(Path::new("output.mov")));
        assert!(precise_output_is_supported(Path::new("output.mp4")));
    }

    #[test]
    fn timeline_png_cache_rejects_nonempty_corruption() {
        let directory = std::env::temp_dir().join(format!(
            "tinypix-timeline-cache-test-{}",
            uuid::Uuid::new_v4()
        ));
        std::fs::create_dir_all(&directory).unwrap();
        let damaged = directory.join("filmstrip.png");
        std::fs::write(&damaged, b"not-a-png").unwrap();
        assert!(!is_valid_png_asset(&damaged));
        let _ = std::fs::remove_dir_all(directory);
    }
}
