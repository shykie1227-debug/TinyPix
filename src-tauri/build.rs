use std::{env, fs, path::PathBuf};

fn stage_embedded_engine(name: &str, output: &PathBuf) {
    let source = PathBuf::from("resources").join(name);
    let destination = output.join(name);
    if source.is_file() {
        fs::copy(&source, &destination).expect("failed to stage embedded media engine");
    } else {
        fs::write(&destination, []).expect("failed to create empty development engine asset");
    }
    println!("cargo:rerun-if-changed={}", source.display());
}

fn main() {
    let output = PathBuf::from(env::var("OUT_DIR").expect("OUT_DIR missing"));
    stage_embedded_engine("ffmpeg.exe", &output);
    stage_embedded_engine("ffprobe.exe", &output);
    tauri_build::build()
}
