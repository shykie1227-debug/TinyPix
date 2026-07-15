use crate::domain::metadata::{self, FileMetadata};
use crate::infrastructure::error::TinyPixError;
use tauri::Manager;

#[tauri::command]
pub fn read_file_metadata(
    app: tauri::AppHandle,
    path: String,
) -> Result<FileMetadata, TinyPixError> {
    app.asset_protocol_scope()
        .allow_file(&path)
        .map_err(|error| TinyPixError::FileRead(format!("无法授权本地媒体预览: {error}")))?;
    metadata::read_metadata(&path).map_err(TinyPixError::FileRead)
}

#[tauri::command]
pub fn get_supported_formats() -> Vec<String> {
    vec![
        "jpg".to_string(),
        "jpeg".to_string(),
        "png".to_string(),
        "webp".to_string(),
        "avif".to_string(),
        "bmp".to_string(),
        "tiff".to_string(),
        "tif".to_string(),
        "psd".to_string(),
    ]
}

#[tauri::command]
pub fn open_folder(path: String) -> Result<(), TinyPixError> {
    let path = std::path::PathBuf::from(&path);
    let folder = if path.is_file() {
        path.parent().map(|p| p.to_path_buf())
    } else {
        Some(path)
    };

    let folder = folder.ok_or_else(|| TinyPixError::InvalidParam("无效的路径".into()))?;

    #[cfg(target_os = "windows")]
    {
        std::process::Command::new("explorer")
            .arg(&folder)
            .spawn()
            .map_err(|e| TinyPixError::Io(format!("打开文件夹失败: {}", e)))?;
    }

    #[cfg(target_os = "macos")]
    {
        std::process::Command::new("open")
            .arg(&folder)
            .spawn()
            .map_err(|e| TinyPixError::Io(format!("打开文件夹失败: {}", e)))?;
    }

    #[cfg(target_os = "linux")]
    {
        std::process::Command::new("xdg-open")
            .arg(&folder)
            .spawn()
            .map_err(|e| TinyPixError::Io(format!("打开文件夹失败: {}", e)))?;
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_get_supported_formats_count() {
        let formats = get_supported_formats();
        assert_eq!(formats.len(), 9);
    }

    #[test]
    fn test_get_supported_formats_contains_jpg() {
        let formats = get_supported_formats();
        assert!(formats.contains(&"jpg".to_string()));
    }

    #[test]
    fn test_get_supported_formats_contains_png() {
        let formats = get_supported_formats();
        assert!(formats.contains(&"png".to_string()));
    }

    #[test]
    fn test_get_supported_formats_contains_all_expected() {
        let formats = get_supported_formats();
        let expected = vec![
            "jpg", "jpeg", "png", "webp", "avif", "bmp", "tiff", "tif", "psd",
        ];
        for fmt in expected {
            assert!(formats.contains(&fmt.to_string()), "缺少格式: {}", fmt);
        }
    }

    #[test]
    fn test_get_supported_formats_no_duplicates() {
        let formats = get_supported_formats();
        let mut unique = formats.clone();
        unique.sort();
        unique.dedup();
        assert_eq!(formats.len(), unique.len());
    }
}
