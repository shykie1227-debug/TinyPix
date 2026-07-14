use serde::Serialize;
use thiserror::Error;

#[derive(Error, Debug, Serialize, Clone)]
#[serde(tag = "type", content = "message")]
pub enum TinyPixError {
    #[error("文件读取失败: {0}")]
    FileRead(String),
    #[error("不支持的图像格式: {0}")]
    UnsupportedFormat(String),
    #[error("图像处理失败: {0}")]
    Processing(String),
    #[error("EXIF 操作失败: {0}")]
    Exif(String),
    #[error("IO 错误: {0}")]
    Io(String),
    #[error("无效参数: {0}")]
    InvalidParam(String),
}

pub type TinyPixResult<T> = Result<T, TinyPixError>;
