use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

#[derive(Serialize, Deserialize, Clone)]
pub struct HistoryEntry {
    pub id: String,
    pub timestamp: String, // ISO 8601
    pub input_path: String,
    pub output_path: String,
    pub operation: String, // "compress" / "rotate" / "crop" / "trim" / etc.
    pub original_size: u64,
    pub output_size: u64,
    pub success: bool,
}

fn get_history_path() -> PathBuf {
    let app_data = dirs::data_dir().unwrap_or_else(|| PathBuf::from("."));
    let dir = app_data.join("TinyPix");
    let _ = fs::create_dir_all(&dir);
    dir.join("history.json")
}

pub fn load_history() -> Vec<HistoryEntry> {
    let path = get_history_path();
    if !path.exists() {
        return Vec::new();
    }
    match fs::read_to_string(&path) {
        Ok(data) => serde_json::from_str(&data).unwrap_or_default(),
        Err(_) => Vec::new(),
    }
}

pub fn save_history(entries: &[HistoryEntry]) {
    let path = get_history_path();
    let _ = fs::write(
        &path,
        serde_json::to_string_pretty(entries).unwrap_or_default(),
    );
}

pub fn add_entry(entry: HistoryEntry) {
    let mut entries = load_history();
    entries.push(entry);
    // Keep max 500 entries
    if entries.len() > 500 {
        entries = entries.split_off(entries.len() - 500);
    }
    save_history(&entries);
}

pub fn clear_all() {
    save_history(&[]);
}
