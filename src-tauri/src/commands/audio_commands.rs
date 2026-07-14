use std::path::Path;

use serde::{Deserialize, Serialize};
use tauri::Emitter;

use crate::domain::audio::{
    AudioFormatConverter, AudioInspector, AudioSourceInfo, AudioStreamExtractor, ConvertConfig,
    ConvertContext, ExtractContext, FfmpegFormatConverter, FfmpegStreamExtractor,
    FfprobeAudioInspector,
};
use crate::infrastructure::error::TinyPixError;

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct AudioExtractionResult {
    pub output_path: String,
    pub original_size: u64,
    pub output_size: u64,
    pub saved_bytes: u64,
    pub processing_time_secs: f64,
    pub mode: String,
}

const FORMAT_CODEC_MAP: &[(&str, &str)] = &[
    ("mp3", "libmp3lame"),
    ("wav", "pcm_s16le"),
    ("aac", "aac"),
    ("flac", "flac"),
    ("m4a", "aac"),
];

fn validate_input_path(path: &str) -> Result<(), TinyPixError> {
    if !Path::new(path).exists() {
        return Err(TinyPixError::InvalidParam(format!("输入文件不存在: {path}")));
    }
    Ok(())
}

fn validate_output_path(path: &str) -> Result<(), TinyPixError> {
    if path.trim().is_empty() {
        return Err(TinyPixError::InvalidParam("输出路径不能为空".to_string()));
    }
    if !Path::new(path).extension().is_some() {
        return Err(TinyPixError::InvalidParam(
            "输出路径必须包含扩展名".to_string(),
        ));
    }
    Ok(())
}

fn codec_for_format(format: &str) -> Result<&'static str, String> {
    FORMAT_CODEC_MAP
        .iter()
        .find(|(f, _)| *f == format.to_lowercase())
        .map(|(_, c)| *c)
        .ok_or_else(|| format!("不支持的音频格式: {format}"))
}

/// Tauri Command: 提取音频（薄壳编排器）
///
/// 流程：
/// 1. M5 校验输入/输出路径
/// 2. M1 解析源音频信息（获取 duration 用于进度计算）
/// 3. 根据 mode 路由到 M2（direct）或 M3（reencode）
#[tauri::command]
pub async fn extract_audio(
    app: tauri::AppHandle,
    input_path: String,
    output_path: String,
    format: String,
    bitrate_kbps: Option<u16>,
    mode: Option<String>,
) -> Result<AudioExtractionResult, String> {
    validate_input_path(&input_path).map_err(|e| e.to_string())?;
    validate_output_path(&output_path).map_err(|e| e.to_string())?;

    let mode = mode.unwrap_or_else(|| "reencode".to_string());
    let inspector = FfprobeAudioInspector;
    let source_info: AudioSourceInfo = inspector
        .inspect(&input_path)
        .unwrap_or_else(|_| AudioSourceInfo::unknown());
    let total_secs = source_info.duration_secs;

    let _ = app.emit("video-progress", 0.0);

    let result = match mode.as_str() {
        "direct" => {
            let extractor = FfmpegStreamExtractor;
            let ctx = ExtractContext {
                input_path: &input_path,
                output_path: &output_path,
                total_secs,
                app: &app,
            };
            extractor.extract(ctx)
        }
        "reencode" => {
            let codec = codec_for_format(&format)?;
            let config = ConvertConfig {
                codec: codec.to_string(),
                bitrate_kbps,
            };
            let converter = FfmpegFormatConverter;
            let ctx = ConvertContext {
                input_path: &input_path,
                output_path: &output_path,
                total_secs,
                app: &app,
            };
            converter.convert(ctx, &config)
        }
        other => return Err(format!("未知的提取模式: {other}")),
    }?;

    Ok(AudioExtractionResult {
        output_path: result.output_path,
        original_size: result.original_size,
        output_size: result.output_size,
        saved_bytes: result.saved_bytes,
        processing_time_secs: result.processing_time_secs,
        mode,
    })
}

/// Tauri Command: 解析源音频信息（M1）
///
/// 失败时返回 `unknown` 占位对象而非错误，保证 UI 可继续工作。
#[tauri::command]
pub async fn inspect_audio(input_path: String) -> Result<AudioSourceInfo, String> {
    let inspector = FfprobeAudioInspector;
    match inspector.inspect(&input_path) {
        Ok(info) => Ok(info),
        Err(_) => Ok(AudioSourceInfo::unknown()),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_codec_for_format_mp3() {
        assert_eq!(codec_for_format("mp3").unwrap(), "libmp3lame");
    }

    #[test]
    fn test_codec_for_format_wav() {
        assert_eq!(codec_for_format("wav").unwrap(), "pcm_s16le");
    }

    #[test]
    fn test_codec_for_format_aac() {
        assert_eq!(codec_for_format("aac").unwrap(), "aac");
    }

    #[test]
    fn test_codec_for_format_flac() {
        assert_eq!(codec_for_format("flac").unwrap(), "flac");
    }

    #[test]
    fn test_codec_for_format_m4a() {
        assert_eq!(codec_for_format("m4a").unwrap(), "aac");
    }

    #[test]
    fn test_codec_for_format_uppercase() {
        assert_eq!(codec_for_format("MP3").unwrap(), "libmp3lame");
        assert_eq!(codec_for_format("WAV").unwrap(), "pcm_s16le");
    }

    #[test]
    fn test_codec_for_format_mixed_case() {
        assert_eq!(codec_for_format("Mp3").unwrap(), "libmp3lame");
        assert_eq!(codec_for_format("FlAc").unwrap(), "flac");
    }

    #[test]
    fn test_codec_for_format_unsupported() {
        let result = codec_for_format("ogg");
        assert!(result.is_err());
        assert!(result.unwrap_err().contains("不支持的音频格式"));
    }

    #[test]
    fn test_codec_for_format_empty() {
        let result = codec_for_format("");
        assert!(result.is_err());
    }
}
