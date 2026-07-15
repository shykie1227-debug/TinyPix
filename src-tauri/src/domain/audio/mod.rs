use serde::{Deserialize, Serialize};

pub mod format_converter;
pub mod inspector;
pub mod stream_extractor;

pub use format_converter::{
    AudioFormatConverter, ConvertConfig, ConvertContext, FfmpegFormatConverter,
};
pub use inspector::{AudioInspector, AudioSourceInfo, FfprobeAudioInspector};
pub use stream_extractor::{AudioStreamExtractor, ExtractContext, FfmpegStreamExtractor};

/// 统一返回类型：direct / reencode 模式都使用
#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct AudioOperationResult {
    pub output_path: String,
    pub original_size: u64,
    pub output_size: u64,
    pub saved_bytes: u64,
    pub processing_time_secs: f64,
}
