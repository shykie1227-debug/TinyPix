use serde::Serialize;
use std::fs;
use std::path::Path;
use std::time::UNIX_EPOCH;

#[derive(Debug, Serialize, Clone)]
pub struct FileMetadata {
    pub file_name: String,
    pub extension: String,
    pub mime_type: String,
    pub size_bytes: u64,
    pub width: Option<u32>,
    pub height: Option<u32>,
    pub created: Option<u64>,
}

pub fn read_metadata(path: &str) -> Result<FileMetadata, String> {
    let path = Path::new(path);
    let file_name = path
        .file_name()
        .and_then(|n| n.to_str())
        .unwrap_or("unknown")
        .to_string();
    let extension = path
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or("")
        .to_lowercase();
    let mime_type = mime_for_ext(&extension);
    let size_bytes = fs::metadata(path).map_err(|e| e.to_string())?.len();
    let created = fs::metadata(path)
        .ok()
        .and_then(|m| m.created().ok())
        .and_then(|t| t.duration_since(UNIX_EPOCH).ok())
        .map(|d| d.as_secs());

    Ok(FileMetadata {
        file_name,
        extension,
        mime_type,
        size_bytes,
        width: None,
        height: None,
        created,
    })
}

fn mime_for_ext(ext: &str) -> String {
    match ext {
        "jpg" | "jpeg" => "image/jpeg",
        "png" => "image/png",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        "webp" => "image/webp",
        "tiff" | "tif" => "image/tiff",
        "svg" => "image/svg+xml",
        "psd" => "image/vnd.adobe.photoshop",
        "pdf" => "application/pdf",
        _ => "application/octet-stream",
    }
    .to_string()
}
