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

pub fn encode_to_webp(img: &DynamicImage, quality: u8) -> Result<Vec<u8>, String> {
    let rgba = img.to_rgba8();
    let (width, height) = rgba.dimensions();
    let encoded =
        webp::Encoder::from_rgba(rgba.as_raw(), width, height).encode(quality.clamp(1, 100) as f32);
    Ok(encoded.to_vec())
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

pub fn encode_to_avif(img: &DynamicImage, quality: u8) -> Result<Vec<u8>, String> {
    let rgba = img.to_rgba8();
    let (width, height) = rgba.dimensions();
    let mut buffer = Vec::new();
    image::codecs::avif::AvifEncoder::new_with_speed_quality(&mut buffer, 6, quality.clamp(1, 100))
        .write_image(
            rgba.as_raw(),
            width,
            height,
            image::ExtendedColorType::Rgba8,
        )
        .map_err(|error| format!("AVIF 编码失败: {error}"))?;
    Ok(buffer)
}

pub fn encode_to_format(img: &DynamicImage, format: &str, quality: u8) -> Result<Vec<u8>, String> {
    let fmt_lower = format.to_lowercase();
    match fmt_lower.as_str() {
        "png" => encode_to_png(img),
        "jpg" | "jpeg" => encode_to_jpeg(img, quality),
        "webp" => encode_to_webp(img, quality),
        "avif" => encode_to_avif(img, quality),
        "bmp" => {
            let mut buffer = Vec::new();
            img.write_to(&mut std::io::Cursor::new(&mut buffer), ImageFormat::Bmp)
                .map_err(|error| format!("BMP 编码失败: {error}"))?;
            Ok(buffer)
        }
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
        "avif" => encode_to_avif(&img, quality)?,
        "bmp" => encode_to_format(&img, "bmp", quality)?,
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

#[derive(Debug, Clone, serde::Deserialize, Default)]
pub struct ImageColorAdjust {
    pub brightness: i16,
    pub contrast: i16,
    pub saturation: i16,
    pub sharpness: u8,
}

#[derive(Debug, Clone)]
pub struct ImageTransformOptions {
    pub crop_percent: Option<ImageCropPercent>,
    pub rotate_degrees: u16,
    pub flip_h: bool,
    pub flip_v: bool,
    pub resize_max_px: Option<u32>,
    pub resize_target_width: Option<u32>,
    pub resize_target_height: Option<u32>,
    pub color_adjust: ImageColorAdjust,
    pub opacity_percent: u8,
    pub preserve_transparency: bool,
}

impl Default for ImageTransformOptions {
    fn default() -> Self {
        Self {
            crop_percent: None,
            rotate_degrees: 0,
            flip_h: false,
            flip_v: false,
            resize_max_px: None,
            resize_target_width: None,
            resize_target_height: None,
            color_adjust: ImageColorAdjust::default(),
            opacity_percent: 100,
            preserve_transparency: true,
        }
    }
}

#[derive(Debug, Clone)]
pub struct ImageProcessItem {
    pub input_path: String,
    pub output_format: String,
    pub quality: u8,
    pub resize_max_px: Option<u32>,
    pub resize_target_width: Option<u32>,
    pub resize_target_height: Option<u32>,
    pub strip_exif: bool,
    pub preserve_transparency: bool,
    pub rotate_degrees: u16,
    pub crop_percent: Option<ImageCropPercent>,
    pub flip_h: bool,
    pub flip_v: bool,
    pub color_adjust: ImageColorAdjust,
    pub opacity_percent: u8,
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
        "avif" => "avif",
        "bmp" => "bmp",
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

    let img = match decode_image(actual_input_path) {
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

    let transform_options = ImageTransformOptions {
        crop_percent: item.crop_percent.clone(),
        rotate_degrees: item.rotate_degrees,
        flip_h: item.flip_h,
        flip_v: item.flip_v,
        resize_max_px: item.resize_max_px,
        resize_target_width: item.resize_target_width,
        resize_target_height: item.resize_target_height,
        color_adjust: item.color_adjust.clone(),
        opacity_percent: item.opacity_percent,
        preserve_transparency: item.preserve_transparency,
    };
    let img = match apply_transforms(img, &transform_options) {
        Ok(image) => image,
        Err(error) => {
            return ProcessResult {
                input_path: input_path.clone(),
                output_path,
                original_size,
                new_size: 0,
                saved_bytes: 0,
                success: false,
                error: Some(error),
            };
        }
    };

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

pub fn validate_dimensions(width: Option<u32>, height: Option<u32>) -> Result<(), String> {
    match (width, height) {
        (None, None) => Ok(()),
        (Some(width), Some(height))
            if (1..=16_384).contains(&width) && (1..=16_384).contains(&height) =>
        {
            Ok(())
        }
        (Some(_), Some(_)) => Err("图片宽高必须在 1..16384 之间".to_string()),
        _ => Err("精确尺寸必须同时提供宽度和高度".to_string()),
    }
}

pub fn apply_transforms(
    mut img: DynamicImage,
    options: &ImageTransformOptions,
) -> Result<DynamicImage, String> {
    if let Some(crop) = &options.crop_percent {
        img = crop_by_percent(&img, crop.x, crop.y, crop.width, crop.height)?;
    }
    img = rotate_image(&img, options.rotate_degrees);
    if options.flip_h {
        img = img.fliph();
    }
    if options.flip_v {
        img = img.flipv();
    }
    validate_dimensions(options.resize_target_width, options.resize_target_height)?;
    if let (Some(width), Some(height)) = (options.resize_target_width, options.resize_target_height)
    {
        img = img.resize_exact(width, height, image::imageops::FilterType::Lanczos3);
    } else if let Some(max_px) = options.resize_max_px {
        if max_px == 0 || max_px > 16_384 {
            return Err("最长边必须在 1..16384 之间".to_string());
        }
        img = resize_to_max_edge(&img, max_px);
    }
    img = apply_color_adjustments(img, &options.color_adjust);
    img = apply_opacity(img, options.opacity_percent);
    if !options.preserve_transparency {
        img = flatten_alpha_on_white(img);
    }
    Ok(img)
}

fn apply_opacity(img: DynamicImage, opacity_percent: u8) -> DynamicImage {
    let opacity = opacity_percent.min(100) as u16;
    let mut rgba = img.to_rgba8();
    for pixel in rgba.pixels_mut() {
        pixel.0[3] = ((pixel.0[3] as u16 * opacity + 50) / 100) as u8;
    }
    DynamicImage::ImageRgba8(rgba)
}

fn apply_color_adjustments(mut img: DynamicImage, adjust: &ImageColorAdjust) -> DynamicImage {
    if adjust.brightness != 0 {
        img = img.brighten((adjust.brightness.clamp(-100, 100) as f32 * 2.55).round() as i32);
    }
    if adjust.contrast != 0 {
        img = img.adjust_contrast(adjust.contrast.clamp(-100, 100) as f32);
    }
    if adjust.saturation != 0 {
        img = apply_saturation(img, adjust.saturation.clamp(-100, 100));
    }
    if adjust.sharpness != 0 {
        img = img.unsharpen(1.0 + adjust.sharpness.min(100) as f32 / 50.0, 1);
    }
    img
}

fn apply_saturation(img: DynamicImage, saturation: i16) -> DynamicImage {
    let factor = 1.0 + saturation as f32 / 100.0;
    let mut rgba = img.to_rgba8();
    for pixel in rgba.pixels_mut() {
        let r = pixel[0] as f32;
        let g = pixel[1] as f32;
        let b = pixel[2] as f32;
        let luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        pixel[0] = (luma + (r - luma) * factor).clamp(0.0, 255.0) as u8;
        pixel[1] = (luma + (g - luma) * factor).clamp(0.0, 255.0) as u8;
        pixel[2] = (luma + (b - luma) * factor).clamp(0.0, 255.0) as u8;
    }
    DynamicImage::ImageRgba8(rgba)
}

pub fn flatten_alpha_on_white(img: DynamicImage) -> DynamicImage {
    let mut rgba = img.to_rgba8();
    for pixel in rgba.pixels_mut() {
        let alpha = pixel[3] as u16;
        for channel in 0..3 {
            pixel[channel] = ((pixel[channel] as u16 * alpha + 255 * (255 - alpha)) / 255) as u8;
        }
        pixel[3] = 255;
    }
    DynamicImage::ImageRgba8(rgba)
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

#[cfg(test)]
mod tests {
    use super::*;

    fn quadrant_image(width: u32, height: u32) -> DynamicImage {
        DynamicImage::ImageRgba8(image::ImageBuffer::from_fn(width, height, |x, y| {
            match (x < width / 2, y < height / 2) {
                (true, true) => image::Rgba([255, 0, 0, 255]),
                (false, true) => image::Rgba([0, 255, 0, 255]),
                (true, false) => image::Rgba([0, 0, 255, 255]),
                (false, false) => image::Rgba([255, 255, 0, 255]),
            }
        }))
    }

    #[test]
    fn pipeline_applies_crop_rotate_flip_resize_and_color() {
        let options = ImageTransformOptions {
            crop_percent: Some(ImageCropPercent {
                x: 0.0,
                y: 0.0,
                width: 50.0,
                height: 100.0,
            }),
            rotate_degrees: 90,
            flip_h: true,
            flip_v: false,
            resize_max_px: None,
            resize_target_width: Some(40),
            resize_target_height: Some(20),
            color_adjust: ImageColorAdjust {
                brightness: 10,
                contrast: 20,
                saturation: -10,
                sharpness: 30,
            },
            opacity_percent: 100,
            preserve_transparency: true,
        };
        let result = apply_transforms(quadrant_image(100, 80), &options).unwrap();
        assert_eq!(result.dimensions(), (40, 20));
        assert_ne!(
            result.to_rgba8().get_pixel(0, 0),
            result.to_rgba8().get_pixel(39, 0)
        );
    }

    #[test]
    fn encodes_real_avif_and_bmp_signatures() {
        let img = DynamicImage::new_rgb8(4, 4);
        let avif = encode_to_format(&img, "avif", 85).unwrap();
        let bmp = encode_to_format(&img, "bmp", 85).unwrap();
        assert_eq!(&avif[4..12], b"ftypavif");
        assert_eq!(&bmp[..2], b"BM");
    }

    #[test]
    fn webp_quality_changes_lossy_output() {
        let img = quadrant_image(256, 256);
        let low = encode_to_format(&img, "webp", 30).unwrap();
        let high = encode_to_format(&img, "webp", 90).unwrap();
        assert_ne!(low, high);
        assert!(low.starts_with(b"RIFF") && high.starts_with(b"RIFF"));
    }

    #[test]
    fn flatten_transparency_uses_white_background() {
        let img = DynamicImage::ImageRgba8(image::RgbaImage::from_pixel(
            1,
            1,
            image::Rgba([0, 0, 0, 0]),
        ));
        let flattened = flatten_alpha_on_white(img).to_rgba8();
        assert_eq!(flattened.get_pixel(0, 0).0, [255, 255, 255, 255]);
    }

    #[test]
    fn opacity_is_applied_before_transparency_flattening() {
        let image = DynamicImage::ImageRgba8(image::RgbaImage::from_pixel(
            1,
            1,
            image::Rgba([10, 20, 30, 200]),
        ));
        let options = ImageTransformOptions {
            opacity_percent: 50,
            preserve_transparency: true,
            ..Default::default()
        };
        let result = apply_transforms(image, &options).unwrap().to_rgba8();
        assert_eq!(result.get_pixel(0, 0).0[3], 100);
    }

    #[test]
    fn rejects_partial_or_oversized_exact_dimensions() {
        assert!(validate_dimensions(Some(100), None).is_err());
        assert!(validate_dimensions(Some(20_000), Some(100)).is_err());
        assert!(validate_dimensions(Some(100), Some(100)).is_ok());
    }
}
