use std::path::{Path, PathBuf};

use crate::infrastructure::error::TinyPixError;

pub fn validate_video_path(path: &str) -> Result<PathBuf, TinyPixError> {
    if path.is_empty() {
        return Err(TinyPixError::InvalidParam("路径不能为空".to_string()));
    }
    let p = PathBuf::from(path);
    if !p.exists() {
        return Err(TinyPixError::InvalidParam(format!("文件不存在: {}", path)));
    }
    if !p.is_file() {
        return Err(TinyPixError::InvalidParam(format!("不是文件: {}", path)));
    }
    Ok(p)
}

pub fn validate_output_path(path: &str) -> Result<PathBuf, TinyPixError> {
    if path.is_empty() {
        return Err(TinyPixError::InvalidParam("输出路径不能为空".to_string()));
    }
    let p = PathBuf::from(path);
    if let Some(parent) = p.parent() {
        if !parent.exists() {
            return Err(TinyPixError::InvalidParam(format!(
                "输出目录不存在: {}",
                parent.display()
            )));
        }
    }
    Ok(p)
}

/// Keep existing user files intact by choosing the first available sibling.
pub fn unique_output_path(requested: &Path) -> PathBuf {
    if !requested.exists() {
        return requested.to_path_buf();
    }
    let parent = requested.parent().unwrap_or_else(|| Path::new("."));
    let stem = requested
        .file_stem()
        .and_then(|value| value.to_str())
        .unwrap_or("output");
    let extension = requested.extension().and_then(|value| value.to_str());
    for index in 1..=9_999 {
        let name = match extension {
            Some(extension) => format!("{stem} ({index}).{extension}"),
            None => format!("{stem} ({index})"),
        };
        let candidate = parent.join(name);
        if !candidate.exists() {
            return candidate;
        }
    }
    let suffix = uuid::Uuid::new_v4().simple().to_string();
    match extension {
        Some(extension) => parent.join(format!("{stem}-{suffix}.{extension}")),
        None => parent.join(format!("{stem}-{suffix}")),
    }
}

pub fn validate_time_range(
    start_secs: f64,
    end_secs: f64,
    total_secs: f64,
) -> Result<(), TinyPixError> {
    if start_secs < 0.0 {
        return Err(TinyPixError::InvalidParam("起始时间不能为负数".to_string()));
    }
    if start_secs >= end_secs {
        return Err(TinyPixError::InvalidParam(
            "起始时间必须小于结束时间".to_string(),
        ));
    }
    if total_secs > 0.0 && end_secs > total_secs {
        return Err(TinyPixError::InvalidParam(format!(
            "结束时间 ({:.2}s) 超过视频总时长 ({:.2}s)",
            end_secs, total_secs
        )));
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    #[test]
    fn test_validate_video_path_empty() {
        let result = validate_video_path("");
        assert!(matches!(result, Err(TinyPixError::InvalidParam(ref s)) if s == "路径不能为空"));
    }

    #[test]
    fn test_validate_video_path_not_exists() {
        let result = validate_video_path("/nonexistent/path/file.mp4");
        assert!(
            matches!(result, Err(TinyPixError::InvalidParam(ref s)) if s.contains("文件不存在"))
        );
    }

    #[test]
    fn test_validate_video_path_is_directory() {
        let temp_dir = std::env::temp_dir();
        let result = validate_video_path(temp_dir.to_str().unwrap());
        assert!(matches!(result, Err(TinyPixError::InvalidParam(ref s)) if s.contains("不是文件")));
    }

    #[test]
    fn test_validate_video_path_valid() {
        let temp_dir = std::env::temp_dir();
        let temp_file = temp_dir.join("tinypix_test_validate.mp4");
        {
            let mut file = std::fs::File::create(&temp_file).unwrap();
            write!(file, "test").unwrap();
        }
        let path = temp_file.to_str().unwrap();
        let result = validate_video_path(path);
        assert!(result.is_ok());
        assert_eq!(result.unwrap(), PathBuf::from(path));
        let _ = std::fs::remove_file(&temp_file);
    }

    #[test]
    fn test_validate_output_path_empty() {
        let result = validate_output_path("");
        assert!(
            matches!(result, Err(TinyPixError::InvalidParam(ref s)) if s == "输出路径不能为空")
        );
    }

    #[test]
    fn test_validate_output_path_parent_not_exists() {
        let result = validate_output_path("/nonexistent_dir/output.mp4");
        assert!(
            matches!(result, Err(TinyPixError::InvalidParam(ref s)) if s.contains("输出目录不存在"))
        );
    }

    #[test]
    fn test_validate_output_path_valid() {
        let temp_dir = std::env::temp_dir();
        let output_path = temp_dir.join("output.mp4");
        let result = validate_output_path(output_path.to_str().unwrap());
        assert!(result.is_ok());
    }

    #[test]
    fn test_validate_output_path_root_level() {
        // On Unix, root exists; on Windows, this might behave differently
        #[cfg(unix)]
        {
            let result = validate_output_path("/output.mp4");
            assert!(result.is_ok());
        }
    }

    #[test]
    fn test_validate_time_range_negative_start() {
        let result = validate_time_range(-1.0, 10.0, 100.0);
        assert!(
            matches!(result, Err(TinyPixError::InvalidParam(ref s)) if s == "起始时间不能为负数")
        );
    }

    #[test]
    fn test_validate_time_range_start_equals_end() {
        let result = validate_time_range(5.0, 5.0, 100.0);
        assert!(
            matches!(result, Err(TinyPixError::InvalidParam(ref s)) if s == "起始时间必须小于结束时间")
        );
    }

    #[test]
    fn test_validate_time_range_start_greater_than_end() {
        let result = validate_time_range(10.0, 5.0, 100.0);
        assert!(
            matches!(result, Err(TinyPixError::InvalidParam(ref s)) if s == "起始时间必须小于结束时间")
        );
    }

    #[test]
    fn test_validate_time_range_end_exceeds_total() {
        let result = validate_time_range(0.0, 15.0, 10.0);
        assert!(
            matches!(result, Err(TinyPixError::InvalidParam(ref s)) if s.contains("超过视频总时长"))
        );
    }

    #[test]
    fn test_validate_time_range_valid() {
        let result = validate_time_range(0.0, 10.0, 15.0);
        assert!(result.is_ok());
    }

    #[test]
    fn test_validate_time_range_zero_total_allows_any_end() {
        // When total_secs is 0.0 (unknown), end_secs can be anything > start_secs
        let result = validate_time_range(0.0, 999.0, 0.0);
        assert!(result.is_ok());
    }
}
