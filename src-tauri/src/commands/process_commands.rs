use crate::domain::exif_processor::{read_exif, strip_exif as strip_exif_file, ExifData};
use crate::domain::image_engine::{
    cancel_batch, crop_center, crop_image, decode_image, encode_to_format, process_images_batch,
    reset_cancel, resize_image, resize_to_max_edge, rotate_image, ImageColorAdjust,
    ImageCropPercent, ImageProcessItem, ProcessResult, SizeEstimate,
};
use crate::infrastructure::error::TinyPixError;
use crate::AppState;
use tauri::{AppHandle, Emitter, State};

#[tauri::command]
pub fn load_image(path: String) -> Result<String, TinyPixError> {
    decode_image(&path).map_err(TinyPixError::Processing)?;
    Ok(path)
}

#[tauri::command]
pub fn export_image(path: String, format: String) -> Result<Vec<u8>, TinyPixError> {
    let img = decode_image(&path).map_err(TinyPixError::Processing)?;
    encode_to_format(&img, &format, 85).map_err(TinyPixError::Processing)
}

/// 估算处理后文件大小
#[tauri::command]
pub async fn estimate_size(
    path: String,
    output_format: String,
    quality: u8,
) -> Result<SizeEstimate, String> {
    crate::domain::image_engine::estimate_size(&path, &output_format, quality)
}

/// 读取 EXIF 数据
#[tauri::command]
pub fn read_exif_data(path: String) -> Result<ExifData, String> {
    read_exif(&path)
}

/// 清除 EXIF 数据（strip）
#[tauri::command]
pub fn strip_exif_command(input: String, output: String) -> Result<(), String> {
    strip_exif_file(&input, &output)
}

/// 批量处理图片（并行）
#[tauri::command]
pub async fn process_images(
    app: AppHandle,
    _state: State<'_, AppState>,
    files: Vec<ImageProcessInput>,
    options: ProcessOptions,
) -> Result<Vec<ProcessResult>, String> {
    let total = files.len();
    reset_cancel();

    let items: Vec<ImageProcessItem> = files
        .iter()
        .map(|f| ImageProcessItem {
            input_path: f.path.clone(),
            output_format: options.format.clone(),
            quality: options.quality as u8,
            resize_max_px: if options.resize_enabled {
                Some(options.resize_max_px)
            } else {
                None
            },
            resize_target_width: options.resize_target_width,
            resize_target_height: options.resize_target_height,
            strip_exif: options.strip_exif,
            preserve_transparency: options.preserve_transparency,
            rotate_degrees: options.rotate_degrees.unwrap_or(0),
            crop_percent: options.crop_percent.clone(),
            flip_h: options.flip_h,
            flip_v: options.flip_v,
            color_adjust: options.color_adjust.clone(),
            opacity_percent: options.opacity_percent,
        })
        .collect();

    let output_dir = options
        .output_dir
        .clone()
        .filter(|dir| !dir.trim().is_empty())
        .unwrap_or_else(|| default_output_dir(&files));
    let results = process_images_batch(items, &output_dir);

    // 发送进度事件 (完成)
    for (i, result) in results.iter().enumerate() {
        app.emit(
            "process-progress",
            serde_json::json!({
                "current": i + 1,
                "total": total,
            }),
        )
        .map_err(|e| e.to_string())?;

        if result.success {
            app.emit(
                "process-complete",
                serde_json::json!({
                    "id": files.get(i).map(|f| &f.id).unwrap_or(&String::new()),
                    "status": "completed",
                    "outputPath": &result.output_path,
                    "outputSize": result.new_size,
                    "savedBytes": result.saved_bytes,
                }),
            )
            .map_err(|e| e.to_string())?;
        } else {
            app.emit(
                "process-error",
                serde_json::json!({
                    "id": files.get(i).map(|f| &f.id).unwrap_or(&String::new()),
                    "error": result.error.as_deref().unwrap_or("unknown error"),
                }),
            )
            .map_err(|e| e.to_string())?;
        }
    }

    // 发送批量完成事件
    let total_saved: i64 = results.iter().map(|r| r.saved_bytes).sum();

    app.emit(
        "batch-complete",
        serde_json::json!({
            "results": &results,
            "total_saved": total_saved,
        }),
    )
    .map_err(|e| e.to_string())?;

    Ok(results)
}

fn default_output_dir(files: &[ImageProcessInput]) -> String {
    files
        .first()
        .and_then(|file| std::path::Path::new(&file.path).parent())
        .map(|parent| parent.join("tinypix_output").to_string_lossy().to_string())
        .unwrap_or_else(|| "tinypix_output".to_string())
}

/// estimate_size_batch - 批量估算大小
#[tauri::command]
pub async fn estimate_size_batch(files: Vec<EstimateSizeInput>) -> Result<u64, String> {
    let mut total = 0u64;
    for file in files {
        match crate::domain::image_engine::estimate_size(
            &file.path,
            &file.format,
            file.quality as u8,
        ) {
            Ok(est) => total += est.estimated_bytes,
            Err(_) => continue,
        }
    }
    Ok(total)
}

#[derive(Debug, serde::Deserialize)]
pub struct ImageProcessInput {
    pub id: String,
    pub path: String,
}

#[derive(Debug, serde::Deserialize)]
pub struct ProcessOptions {
    pub format: String,
    pub quality: f64,
    pub resize_enabled: bool,
    pub resize_max_px: u32,
    pub resize_target_width: Option<u32>,
    pub resize_target_height: Option<u32>,
    pub strip_exif: bool,
    #[serde(default = "default_preserve_transparency")]
    pub preserve_transparency: bool,
    pub output_dir: Option<String>,
    pub rotate_degrees: Option<u16>,
    pub crop_percent: Option<ImageCropPercent>,
    #[serde(default)]
    pub flip_h: bool,
    #[serde(default)]
    pub flip_v: bool,
    #[serde(default)]
    pub color_adjust: ImageColorAdjust,
    #[serde(default = "default_opacity_percent")]
    pub opacity_percent: u8,
}

fn default_preserve_transparency() -> bool {
    true
}

fn default_opacity_percent() -> u8 {
    100
}

#[derive(Debug, serde::Deserialize)]
pub struct EstimateSizeInput {
    pub path: String,
    pub format: String,
    pub quality: f64,
}

/// 取消正在进行的批处理
#[tauri::command]
pub fn cancel_process() -> Result<(), String> {
    cancel_batch();
    Ok(())
}

/// 调整图片尺寸
#[tauri::command]
pub fn resize_image_cmd(
    path: String,
    width: u32,
    height: u32,
    mode: String,
) -> Result<Vec<u8>, TinyPixError> {
    let img = decode_image(&path).map_err(TinyPixError::Processing)?;
    let resized = match mode.as_str() {
        "max_edge" => resize_to_max_edge(&img, width),
        "aspect" => resize_image(&img, width, height),
        _ => resize_image(&img, width, height),
    };
    encode_to_format(&resized, "png", 85).map_err(TinyPixError::Processing)
}

/// 旋转图片
/// Security: degrees 白名单验证 (只允许 90/180/270)，路径遍历防护
#[tauri::command]
pub fn rotate_image_cmd(
    path: String,
    degrees: u16,
    output_format: String,
    quality: u8,
) -> Result<Vec<u8>, String> {
    // Input validation: degrees whitelist
    if !matches!(degrees, 90 | 180 | 270) {
        return Err(format!("不支持的角度: {} (仅支持 90/180/270)", degrees));
    }
    // Path traversal protection
    if path.contains("..") {
        return Err("路径不允许包含 ..".to_string());
    }
    let p = std::path::Path::new(&path);
    if !p.exists() {
        return Err(format!("文件不存在: {}", path));
    }

    let img = decode_image(&path)?;
    let rotated = rotate_image(&img, degrees);
    encode_to_format(&rotated, &output_format, quality)
}

/// 裁剪图片
/// Security: 坐标边界验证，路径遍历防护
#[tauri::command]
pub fn crop_image_cmd(
    path: String,
    x: u32,
    y: u32,
    width: u32,
    height: u32,
    output_format: String,
    quality: u8,
) -> Result<Vec<u8>, String> {
    // Path traversal protection
    if path.contains("..") {
        return Err("路径不允许包含 ..".to_string());
    }
    let p = std::path::Path::new(&path);
    if !p.exists() {
        return Err(format!("文件不存在: {}", path));
    }

    let img = decode_image(&path)?;
    let cropped = crop_image(&img, x, y, width, height)?;
    encode_to_format(&cropped, &output_format, quality)
}

/// 居中裁剪图片（按比例）
/// Security: 比例验证，路径遍历防护
#[tauri::command]
pub fn crop_center_cmd(
    path: String,
    ratio_w: u32,
    ratio_h: u32,
    output_format: String,
    quality: u8,
) -> Result<Vec<u8>, String> {
    if path.contains("..") {
        return Err("路径不允许包含 ..".to_string());
    }
    let p = std::path::Path::new(&path);
    if !p.exists() {
        return Err(format!("文件不存在: {}", path));
    }
    if ratio_w == 0 || ratio_h == 0 {
        return Err("比例值不能为 0".to_string());
    }

    let img = decode_image(&path)?;
    let target_ratio = ratio_w as f32 / ratio_h as f32;
    let cropped = crop_center(&img, target_ratio)?;
    encode_to_format(&cropped, &output_format, quality)
}

#[tauri::command]
pub fn get_history(limit: Option<u32>) -> Vec<crate::domain::history::HistoryEntry> {
    let mut entries = crate::domain::history::load_history();
    entries.reverse(); // newest first
    if let Some(lim) = limit {
        entries.truncate(lim as usize);
    }
    entries
}

#[tauri::command]
pub fn clear_history() {
    crate::domain::history::clear_all();
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_default_output_dir_with_valid_path() {
        let files = vec![ImageProcessInput {
            id: "1".to_string(),
            path: "/home/user/images/photo.jpg".to_string(),
        }];
        let result = default_output_dir(&files);
        assert!(result.contains("tinypix_output"));
        assert!(result.starts_with("/home/user/images"));
    }

    #[test]
    fn test_default_output_dir_empty_files() {
        let files: Vec<ImageProcessInput> = vec![];
        let result = default_output_dir(&files);
        assert_eq!(result, "tinypix_output");
    }

    #[test]
    fn test_default_output_dir_with_relative_path() {
        let files = vec![ImageProcessInput {
            id: "1".to_string(),
            path: "photos/image.png".to_string(),
        }];
        let result = default_output_dir(&files);
        assert!(result.contains("tinypix_output"));
    }

    #[test]
    fn test_default_output_dir_multiple_files_uses_first() {
        let files = vec![
            ImageProcessInput {
                id: "1".to_string(),
                path: "/first/folder/a.jpg".to_string(),
            },
            ImageProcessInput {
                id: "2".to_string(),
                path: "/second/folder/b.png".to_string(),
            },
        ];
        let result = default_output_dir(&files);
        assert!(result.starts_with("/first/folder"));
        assert!(result.contains("tinypix_output"));
    }
}
