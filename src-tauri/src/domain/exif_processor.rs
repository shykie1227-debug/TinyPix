use serde::Serialize;
use std::fs::File;
use std::io::BufReader;
use std::path::Path;

/// EXIF 元数据结构
#[derive(Debug, Serialize, Clone)]
pub struct ExifData {
    /// 是否包含 EXIF
    pub has_exif: bool,
    /// 是否包含 GPS 定位
    pub has_gps: bool,
    /// 相机型号
    pub camera_model: Option<String>,
    /// GPS 纬度
    pub gps_lat: Option<f64>,
    /// GPS 经度
    pub gps_lon: Option<f64>,
    /// 拍摄时间
    pub datetime: Option<String>,
}

/// 从图片文件读取 EXIF 数据
pub fn read_exif(path: &str) -> Result<ExifData, String> {
    let file = File::open(path).map_err(|e| format!("无法打开文件: {}", e))?;
    let mut reader = BufReader::new(file);

    let exif = match exif::Reader::new().read_from_container(&mut reader) {
        Ok(e) => e,
        Err(_) => {
            return Ok(ExifData {
                has_exif: false,
                has_gps: false,
                camera_model: None,
                gps_lat: None,
                gps_lon: None,
                datetime: None,
            });
        }
    };

    let mut has_gps = false;
    let mut gps_lat: Option<f64> = None;
    let mut gps_lon: Option<f64> = None;
    let mut camera_model: Option<String> = None;
    let mut datetime: Option<String> = None;

    // GPS 数据
    if let Some(gps_lat_ref) = exif.get_field(exif::Tag::GPSLatitudeRef, exif::In::PRIMARY) {
        if let Some(gps_latitude) = exif.get_field(exif::Tag::GPSLatitude, exif::In::PRIMARY) {
            if let exif::Value::Rational(ref v) = gps_latitude.value {
                let mut deg = v[0].to_f64();
                let mut min = v[1].to_f64();
                let mut sec = v[2].to_f64();
                if deg >= 0.0 && gps_lat_ref.display_value().to_string().contains('S') {
                    deg = -deg;
                    min = -min;
                    sec = -sec;
                }
                has_gps = true;
                gps_lat = Some(deg + min / 60.0 + sec / 3600.0);
            }
        }
    }

    if let Some(gps_lon_ref) = exif.get_field(exif::Tag::GPSLongitudeRef, exif::In::PRIMARY) {
        if let Some(gps_longitude) = exif.get_field(exif::Tag::GPSLongitude, exif::In::PRIMARY) {
            if let exif::Value::Rational(ref v) = gps_longitude.value {
                let mut deg = v[0].to_f64();
                let mut min = v[1].to_f64();
                let mut sec = v[2].to_f64();
                if deg >= 0.0 && gps_lon_ref.display_value().to_string().contains('W') {
                    deg = -deg;
                    min = -min;
                    sec = -sec;
                }
                has_gps = true;
                gps_lon = Some(deg + min / 60.0 + sec / 3600.0);
            }
        }
    }

    // 相机型号
    if let Some(model) = exif.get_field(exif::Tag::Model, exif::In::PRIMARY) {
        camera_model = Some(model.display_value().to_string());
    }

    // 拍摄时间
    if let Some(dt) = exif.get_field(exif::Tag::DateTimeOriginal, exif::In::PRIMARY) {
        datetime = Some(dt.display_value().to_string());
    }

    Ok(ExifData {
        has_exif: true,
        has_gps,
        camera_model,
        gps_lat,
        gps_lon,
        datetime,
    })
}

/// 移除图片中的 EXIF 数据（生成干净副本）
///
/// JPEG: 重建文件，跳过 APP1 (EXIF) 和 APP2 (ICC) 段
/// PNG: 保留 tEXt/zTXt/iTXt，跳过 eXIf 块
/// 其他格式: 直接复制
pub fn strip_exif(input_path: &str, output_path: &str) -> Result<(), String> {
    let ext = Path::new(input_path)
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or("")
        .to_lowercase();

    match ext.as_str() {
        "jpg" | "jpeg" => strip_exif_jpeg(input_path, output_path),
        "png" => strip_exif_png(input_path, output_path),
        _ => {
            // 其他格式直接复制
            std::fs::copy(input_path, output_path).map_err(|e| format!("文件复制失败: {}", e))?;
            Ok(())
        }
    }
}

fn strip_exif_jpeg(input_path: &str, output_path: &str) -> Result<(), String> {
    let data = std::fs::read(input_path).map_err(|e| format!("读取文件失败: {}", e))?;

    if data.len() < 2 || data[0] != 0xFF || data[1] != 0xD8 {
        return Err("不是有效的 JPEG 文件".to_string());
    }

    let mut output = Vec::with_capacity(data.len());
    output.push(0xFF);
    output.push(0xD8); // SOI

    let mut i = 2;
    while i + 3 < data.len() {
        if data[i] != 0xFF {
            // 内容数据，直接复制到结尾
            output.extend_from_slice(&data[i..]);
            break;
        }

        let marker = data[i + 1];

        // 跳过 EXIF (APP1) 和 ICC (APP2)
        if marker == 0xE1 {
            // 检查是否是 EXIF — 先确保有足够字节读取长度
            if i + 4 >= data.len() {
                output.extend_from_slice(&data[i..]);
                break;
            }
            let seg_len = ((data[i + 2] as usize) << 8) | (data[i + 3] as usize);
            let next = i + 2 + seg_len;
            if next <= data.len() && i + 8 <= data.len() && &data[i + 4..i + 8] == b"Exif\x00" {
                i = next;
                continue;
            }
        }

        if marker == 0xFE {
            // COM 注释段，跳过
            if i + 4 > data.len() {
                output.extend_from_slice(&data[i..]);
                break;
            }
            let seg_len = ((data[i + 2] as usize) << 8) | (data[i + 3] as usize);
            i = i + 2 + seg_len;
            continue;
        }

        if marker == 0xD9 {
            // EOI
            output.push(0xFF);
            output.push(0xD9);
            break;
        }

        if marker == 0xD8 || marker == 0xD9 || marker == 0x01 || marker == 0x02 {
            // 无长度段
            output.push(data[i]);
            output.push(data[i + 1]);
            i += 2;
            continue;
        }

        // 常规段：复制 — 先确保有足够字节读取长度
        if i + 4 > data.len() {
            output.extend_from_slice(&data[i..]);
            break;
        }
        let seg_len = ((data[i + 2] as usize) << 8) | (data[i + 3] as usize);
        let next = i + 2 + seg_len;
        if next > data.len() {
            // 无效长度，复制剩余
            output.extend_from_slice(&data[i..]);
            break;
        }
        output.extend_from_slice(&data[i..next]);
        i = next;
    }

    std::fs::write(output_path, output).map_err(|e| format!("写入文件失败: {}", e))?;
    Ok(())
}

fn strip_exif_png(input_path: &str, output_path: &str) -> Result<(), String> {
    let data = std::fs::read(input_path).map_err(|e| format!("读取文件失败: {}", e))?;

    if data.len() < 8 || &data[0..8] != b"\x89PNG\r\n\x1a\n" {
        return Err("不是有效的 PNG 文件".to_string());
    }

    let mut output = Vec::with_capacity(data.len());
    output.extend_from_slice(&data[0..8]); // PNG signature

    let mut i = 8;
    while i < data.len() {
        let chunk_len = ((data[i] as u32) << 24)
            | ((data[i + 1] as u32) << 16)
            | ((data[i + 2] as u32) << 8)
            | (data[i + 3] as u32);
        let chunk_type = &data[i + 4..i + 8];

        // 跳过 eXIf 块
        if chunk_type == b"eXIf" {
            i += 12 + chunk_len as usize;
            continue;
        }

        // 复制其他所有块
        output.extend_from_slice(&data[i..i + 12 + chunk_len as usize]);
        i += 12 + chunk_len as usize;
    }

    std::fs::write(output_path, output).map_err(|e| format!("写入文件失败: {}", e))?;
    Ok(())
}
