use image::{ColorType, DynamicImage, GenericImageView, ImageEncoder, ImageFormat};
use serde::Serialize;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};

#[cfg(feature = "psd")]
use zune_core::colorspace::ColorSpace;
#[cfg(feature = "psd")]
use zune_psd::PSDDecoder;

// ── Decode ───────────────────────────────────────────────────────────────────

pub fn decode_image(path: &str) -> Result<DynamicImage, String> {
    let path = Path::new(path);
    let ext = path
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or("")
        .to_lowercase();

    match ext.as_str() {
        "psd" => decode_psd(path),
        _ => image::open(path).map_err(|e| format!("解码失败: {}", e)),
    }
}

#[cfg(feature = "psd")]
fn decode_psd(path: &Path) -> Result<DynamicImage, String> {
    let bytes = std::fs::read(path).map_err(|e| format!("无法打开文件: {}", e))?;
    let mut decoder = PSDDecoder::new(bytes.as_slice());
    decoder
        .decode_headers()
        .map_err(|e| format!("PSD 头解析失败: {:?}", e))?;

    let (width, height) = decoder.get_dimensions().ok_or("PSD 未返回有效尺寸信息")?;
    let colorspace = decoder.get_colorspace().ok_or("PSD 未返回有效颜色空间")?;
    let raw = decoder
        .decode_raw()
        .map_err(|e| format!("PSD 解码错误: {:?}", e))?;

    let w = width as u32;
    let h = height as u32;

    match colorspace {
        ColorSpace::RGBA => image::RgbaImage::from_raw(w, h, raw)
            .map(DynamicImage::ImageRgba8)
            .ok_or("从 PSD RGBA 数据构建图像失败".to_string()),
        ColorSpace::RGB => image::RgbImage::from_raw(w, h, raw)
            .map(DynamicImage::ImageRgb8)
            .ok_or("从 PSD RGB 数据构建图像失败".to_string()),
        ColorSpace::Luma => image::GrayImage::from_raw(w, h, raw)
            .map(DynamicImage::ImageLuma8)
            .ok_or("从 PSD 灰度数据构建图像失败".to_string()),
        ColorSpace::LumaA => image::ImageBuffer::<image::LumaA<u8>, Vec<u8>>::from_raw(w, h, raw)
            .map(DynamicImage::ImageLumaA8)
            .ok_or("从 PSD 灰度 Alpha 数据构建图像失败".to_string()),
        _ => Err(format!("暂不支持的 PSD 颜色空间: {:?}", colorspace)),
    }
}

#[cfg(not(feature = "psd"))]
fn decode_psd(_path: &Path) -> Result<DynamicImage, String> {
    Err("PSD 支持未编译 — 在 Cargo.toml 中添加 `features = [\"psd\"]`".to_string())
}

// ── Resize ───────────────────────────────────────────────────────────────────

/// 按固定像素调整尺寸，保持宽高比
pub fn resize_image(img: &DynamicImage, width: u32, height: u32) -> DynamicImage {
    img.resize_exact(width, height, image::imageops::FilterType::Lanczos3)
}

/// 按长边最大像素调整尺寸（保持宽高比）
pub fn resize_to_max_edge(img: &DynamicImage, max_px: u32) -> DynamicImage {
    let (w, h) = img.dimensions();
    let max_edge = w.max(h);
    if max_edge <= max_px {
        return img.clone();
    }
    let ratio = max_px as f32 / max_edge as f32;
    let new_w = (w as f32 * ratio).round() as u32;
    let new_h = (h as f32 * ratio).round() as u32;
    img.resize_exact(
        new_w.max(1),
        new_h.max(1),
        image::imageops::FilterType::Lanczos3,
    )
}

/// 按目标宽高比调整尺寸（将fit模式）
pub fn resize_with_aspect(img: &DynamicImage, width: u32, height: u32) -> DynamicImage {
    img.resize(width, height, image::imageops::FilterType::Lanczos3)
}

// ── Rotate ───────────────────────────────────────────────────────────────────

/// 旋转图片 (90/180/270 度)
/// Security: degrees 参数白名单验证，只允许 0/90/180/270
pub fn rotate_image(img: &DynamicImage, degrees: u16) -> DynamicImage {
    match degrees {
        90 => img.rotate90(),
        180 => img.rotate180(),
        270 => img.rotate270(),
        _ => img.clone(),
    }
}

// ── Crop ────────────────────────────────────────────────────────────────────

/// 裁剪图片到指定区域
/// Security: 边界验证确保裁剪区域不超出图片尺寸
pub fn crop_image(
    img: &DynamicImage,
    x: u32,
    y: u32,
    width: u32,
    height: u32,
) -> Result<DynamicImage, String> {
    let (img_w, img_h) = img.dimensions();
    if width == 0 || height == 0 {
        return Err("裁剪宽度和高度必须大于 0".to_string());
    }
    if x + width > img_w || y + height > img_h {
        return Err(format!(
            "裁剪区域 ({}, {}, {}x{}) 超出图片尺寸 ({}x{})",
            x, y, width, height, img_w, img_h
        ));
    }
    Ok(img.crop_imm(x, y, width, height))
}

/// 居中裁剪: 根据目标宽高比从图片中心裁剪
pub fn crop_center(img: &DynamicImage, target_ratio: f32) -> Result<DynamicImage, String> {
    let (w, h) = img.dimensions();
    if target_ratio <= 0.0 {
        return Err("目标比例必须大于 0".to_string());
    }
    let (crop_w, crop_h) = if (w as f32) / (h as f32) > target_ratio {
        // 图片更宽，裁左右
        let new_w = ((h as f32) * target_ratio).round() as u32;
        (new_w.max(1), h)
    } else {
        // 图片更高，裁上下
        let new_h = ((w as f32) / target_ratio).round() as u32;
        (w, new_h.max(1))
    };
    let x = (w - crop_w) / 2;
    let y = (h - crop_h) / 2;
    crop_image(img, x, y, crop_w, crop_h)
}

// ── Encode ───────────────────────────────────────────────────────────────────

pub fn encode_to_webp(img: &DynamicImage, _quality: u8) -> Result<Vec<u8>, String> {
    let mut buf = Vec::new();
    let mut cursor = std::io::Cursor::new(&mut buf);
    img.write_to(&mut cursor, ImageFormat::WebP)
        .map_err(|e| format!("WebP 编码失败: {}", e))?;
    Ok(buf)
}

pub fn encode_to_jpeg(img: &DynamicImage, quality: u8) -> Result<Vec<u8>, String> {
    let mut buf = Vec::new();
    let mut cursor = std::io::Cursor::new(&mut buf);
    let encoder = image::codecs::jpeg::JpegEncoder::new_with_quality(&mut cursor, quality);
    let rgb = img.to_rgb8();
    let (w, h) = rgb.dimensions();
    encoder
        .write_image(rgb.as_raw(), w, h, ColorType::Rgb8.into())
        .map_err(|e| format!("JPEG 编码失败: {}", e))?;
    Ok(buf)
}

pub fn encode_to_png(img: &DynamicImage) -> Result<Vec<u8>, String> {
    let mut buf = Vec::new();
    let mut cursor = std::io::Cursor::new(&mut buf);
    img.write_to(&mut cursor, ImageFormat::Png)
        .map_err(|e| format!("PNG 编码失败: {}", e))?;
    Ok(buf)
}

pub fn encode_to_format(img: &DynamicImage, format: &str, quality: u8) -> Result<Vec<u8>, String> {
    let fmt_lower = format.to_lowercase();
    match fmt_lower.as_str() {
        "png" => encode_to_png(img),
        "jpg" | "jpeg" => encode_to_jpeg(img, quality),
        "webp" => encode_to_webp(img, quality),
        _ => Err(format!("不支持的输出格式: {}", format)),
    }
}

// ── Size Estimation ───────────────────────────────────────────────────────────

#[derive(Debug, Serialize)]
pub struct SizeEstimate {
    pub original_bytes: u64,
    pub estimated_bytes: u64,
    pub label: String,
}

pub fn estimate_size(path: &str, output_format: &str, quality: u8) -> Result<SizeEstimate, String> {
    let original_bytes = std::fs::metadata(path).map_err(|e| e.to_string())?.len();

    let img = decode_image(path)?;
    let encoded = match output_format.to_lowercase().as_str() {
        "jpg" | "jpeg" => encode_to_jpeg(&img, quality)?,
        "webp" => encode_to_webp(&img, quality)?,
        "png" => encode_to_png(&img)?,
        _ => return Err(format!("不支持的格式: {}", output_format)),
    };

    let estimated_bytes = encoded.len() as u64;
    let label = format_size_label(estimated_bytes);

    Ok(SizeEstimate {
        original_bytes,
        estimated_bytes,
        label,
    })
}

fn format_size_label(bytes: u64) -> String {
    if bytes >= 1_000_000 {
        format!("~{:.1} MB", bytes as f64 / 1_000_000.0)
    } else if bytes >= 1_000 {
        format!("~{} KB", bytes / 1_000)
    } else {
        format!("~{} B", bytes)
    }
}

// ── Batch Processing ─────────────────────────────────────────────────────────

use rayon::prelude::*;

#[derive(Debug, Clone, Serialize)]
pub struct ProcessResult {
    pub input_path: String,
    pub output_path: String,
    pub original_size: u64,
    pub new_size: u64,
    pub saved_bytes: i64,
    pub success: bool,
    pub error: Option<String>,
}

/// 并行处理多张图片
pub fn process_images_batch(items: Vec<ImageProcessItem>, output_dir: &str) -> Vec<ProcessResult> {
    items
        .par_iter()
        .map(|item| process_single(item, output_dir))
        .collect()
}

// ── Cancellation ────────────────────────────────────────────────────────────────

/// 全局取消标志（跨线程共享）
static CANCEL_FLAG: AtomicBool = AtomicBool::new(false);

/// 请求取消正在进行的批处理
pub fn cancel_batch() {
    CANCEL_FLAG.store(true, Ordering::SeqCst);
}

/// 重置取消标志（下次批处理前调用）
#[allow(dead_code)]
pub fn reset_cancel() {
    CANCEL_FLAG.store(false, Ordering::SeqCst);
}

/// 检查是否请求了取消
#[allow(dead_code)]
pub fn is_cancel_requested() -> bool {
    CANCEL_FLAG.load(Ordering::SeqCst)
}

/// 检查并清除取消标志（用于循环中定期检查）
#[allow(dead_code)]
pub fn check_cancel() -> bool {
    if CANCEL_FLAG.load(Ordering::SeqCst) {
        CANCEL_FLAG.store(false, Ordering::SeqCst);
        true
    } else {
        false
    }
}

#[derive(Debug, Clone, serde::Deserialize)]
pub struct ImageCropPercent {
    pub x: f32,
    pub y: f32,
    pub width: f32,
    pub height: f32,
}

#[derive(Debug, Clone)]
pub struct ImageProcessItem {
    pub input_path: String,
    pub output_format: String,
    pub quality: u8,
    pub resize_max_px: Option<u32>,
    pub strip_exif: bool,
    pub rotate_degrees: u16,
    pub crop_percent: Option<ImageCropPercent>,
}

fn process_single(item: &ImageProcessItem, output_dir: &str) -> ProcessResult {
    let input_path = &item.input_path;
    if is_cancel_requested() {
        return ProcessResult {
            input_path: input_path.clone(),
            output_path: String::new(),
            original_size: 0,
            new_size: 0,
            saved_bytes: 0,
            success: false,
            error: Some("用户已取消".to_string()),
        };
    }

    let original_size = match std::fs::metadata(input_path) {
        Ok(m) => m.len(),
        Err(e) => {
            return ProcessResult {
                input_path: input_path.clone(),
                output_path: String::new(),
                original_size: 0,
                new_size: 0,
                saved_bytes: 0,
                success: false,
                error: Some(e.to_string()),
            };
        }
    };

    // 构建输出路径
    let in_path = std::path::Path::new(input_path);
    let stem = in_path
        .file_stem()
        .and_then(|s| s.to_str())
        .unwrap_or("output");
    let ext = match item.output_format.to_lowercase().as_str() {
        "jpg" | "jpeg" => "jpg",
        "webp" => "webp",
        "png" => "png",
        _ => "bin",
    };
    let output_dir_path = PathBuf::from(output_dir);
    if let Err(e) = std::fs::create_dir_all(&output_dir_path) {
        return ProcessResult {
            input_path: input_path.clone(),
            output_path: output_dir_path.to_string_lossy().to_string(),
            original_size,
            new_size: 0,
            saved_bytes: 0,
            success: false,
            error: Some(format!("无法创建输出目录: {}", e)),
        };
    }
    let output_path_buf = unique_output_path(&output_dir_path, stem, ext, in_path);
    let output_path = output_path_buf.to_string_lossy().to_string();

    // ── Decode (with optional EXIF strip) ───────────────────────────────
    // If strip_exif is requested, strip to a temp file first, then decode
    let work_path: std::path::PathBuf;
    let actual_input_path: &str;
    if item.strip_exif {
        let ext = in_path.extension().and_then(|e| e.to_str()).unwrap_or("");
        let temp_path =
            std::env::temp_dir().join(format!("tinypix_stripped_{}.{}", uuid_v4(), ext));
        if let Err(e) =
            crate::domain::exif_processor::strip_exif(input_path, temp_path.to_str().unwrap_or(""))
        {
            return ProcessResult {
                input_path: input_path.clone(),
                output_path,
                original_size,
                new_size: 0,
                saved_bytes: 0,
                success: false,
                error: Some(format!("EXIF 剥离失败: {}", e)),
            };
        }
        work_path = temp_path;
        actual_input_path = work_path.to_str().unwrap_or(input_path);
    } else {
        work_path = std::path::PathBuf::new();
        actual_input_path = input_path;
    }

    let mut img = match decode_image(actual_input_path) {
        Ok(img) => img,
        Err(e) => {
            // Clean up temp file on decode failure
            if item.strip_exif {
                let _ = std::fs::remove_file(&work_path);
            }
            return ProcessResult {
                input_path: input_path.clone(),
                output_path,
                original_size,
                new_size: 0,
                saved_bytes: 0,
                success: false,
                error: Some(e),
            };
        }
    };

    // Clean up temp stripped file after decode
    if item.strip_exif {
        let _ = std::fs::remove_file(&work_path);
    }

    img = rotate_image(&img, item.rotate_degrees);

    if let Some(crop) = &item.crop_percent {
        match crop_by_percent(&img, crop.x, crop.y, crop.width, crop.height) {
            Ok(cropped) => img = cropped,
            Err(e) => {
                return ProcessResult {
                    input_path: input_path.clone(),
                    output_path,
                    original_size,
                    new_size: 0,
                    saved_bytes: 0,
                    success: false,
                    error: Some(e),
                };
            }
        }
    }

    if let Some(max_px) = item.resize_max_px {
        img = resize_to_max_edge(&img, max_px);
    }

    // ── Encode ─────────────────────────────────────────────────────────
    let encoded = match encode_to_format(&img, &item.output_format, item.quality) {
        Ok(b) => b,
        Err(e) => {
            return ProcessResult {
                input_path: input_path.clone(),
                output_path,
                original_size,
                new_size: 0,
                saved_bytes: 0,
                success: false,
                error: Some(e),
            };
        }
    };

    // ── Write ──────────────────────────────────────────────────────────
    if let Err(e) = std::fs::write(&output_path, &encoded) {
        return ProcessResult {
            input_path: input_path.clone(),
            output_path,
            original_size,
            new_size: 0,
            saved_bytes: 0,
            success: false,
            error: Some(e.to_string()),
        };
    }

    let new_size = encoded.len() as u64;
    let saved_bytes = original_size as i64 - new_size as i64;

    ProcessResult {
        input_path: input_path.clone(),
        output_path,
        original_size,
        new_size,
        saved_bytes,
        success: true,
        error: None,
    }
}

fn crop_by_percent(
    img: &DynamicImage,
    x: f32,
    y: f32,
    width: f32,
    height: f32,
) -> Result<DynamicImage, String> {
    let (img_w, img_h) = img.dimensions();
    let pct = |value: f32| value.clamp(0.0, 100.0) / 100.0;
    let crop_w = ((img_w as f32) * pct(width)).round().max(1.0) as u32;
    let crop_h = ((img_h as f32) * pct(height)).round().max(1.0) as u32;
    let max_x = img_w.saturating_sub(crop_w);
    let max_y = img_h.saturating_sub(crop_h);
    let crop_x = (((img_w as f32) * pct(x)).round() as u32).min(max_x);
    let crop_y = (((img_h as f32) * pct(y)).round() as u32).min(max_y);

    if crop_x == 0 && crop_y == 0 && crop_w >= img_w && crop_h >= img_h {
        return Ok(img.clone());
    }

    crop_image(img, crop_x, crop_y, crop_w, crop_h)
}

// UUID v4 generator using the `uuid` crate for cryptographically secure random UUIDs
fn uuid_v4() -> String {
    uuid::Uuid::new_v4().to_string()
}

fn unique_output_path(output_dir: &Path, stem: &str, ext: &str, input_path: &Path) -> PathBuf {
    let mut candidate = output_dir.join(format!("{}.{}", stem, ext));
    let input_canon = input_path.canonicalize().ok();
    let is_same_as_input = |path: &PathBuf| match (&input_canon, path.canonicalize().ok()) {
        (Some(input), Some(output)) => input == &output,
        _ => false,
    };

    if !candidate.exists() && !is_same_as_input(&candidate) {
        return candidate;
    }

    let mut idx = 1u32;
    loop {
        candidate = output_dir.join(format!("{}_tinypix_{}.{}", stem, idx, ext));
        if !candidate.exists() && !is_same_as_input(&candidate) {
            return candidate;
        }
        idx += 1;
    }
}
