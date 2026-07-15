#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    tinypix_lib::configure_tracing();

    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .manage(tinypix_lib::AppState::new())
        .invoke_handler(tauri::generate_handler![
            tinypix_lib::commands::file_commands::read_file_metadata,
            tinypix_lib::commands::file_commands::get_supported_formats,
            tinypix_lib::commands::file_commands::open_folder,
            tinypix_lib::commands::process_commands::load_image,
            tinypix_lib::commands::process_commands::export_image,
            tinypix_lib::commands::process_commands::estimate_size,
            tinypix_lib::commands::process_commands::read_exif_data,
            tinypix_lib::commands::process_commands::strip_exif_command,
            tinypix_lib::commands::process_commands::estimate_size_batch,
            tinypix_lib::commands::process_commands::process_images,
            tinypix_lib::commands::process_commands::cancel_process,
            tinypix_lib::commands::process_commands::resize_image_cmd,
            tinypix_lib::commands::process_commands::rotate_image_cmd,
            tinypix_lib::commands::process_commands::crop_image_cmd,
            tinypix_lib::commands::process_commands::crop_center_cmd,
            tinypix_lib::commands::process_commands::get_history,
            tinypix_lib::commands::process_commands::clear_history,
            tinypix_lib::commands::preview_commands::prepare_media_preview,
            tinypix_lib::commands::preview_commands::cancel_preview_task,
            tinypix_lib::commands::preview_commands::clear_preview_cache,
            tinypix_lib::commands::timeline_commands::generate_timeline_assets,
            tinypix_lib::commands::timeline_commands::export_video_edit,
            tinypix_lib::commands::video_commands::check_ffmpeg,
            tinypix_lib::commands::video_commands::cancel_video_tasks,
            tinypix_lib::infrastructure::ffmpeg_manager::get_media_engine_status,
            tinypix_lib::infrastructure::ffmpeg_manager::clear_media_engine_cache,
            tinypix_lib::commands::video_commands::get_video_info,
            tinypix_lib::commands::video_commands::compress_video,
            tinypix_lib::commands::video_commands::create_gif,
            tinypix_lib::commands::video_commands::create_video_preview,
            tinypix_lib::commands::video_commands::extract_frame,
            tinypix_lib::commands::video_commands::export_thumbnail,
            tinypix_lib::commands::video_commands::trim_video,
            tinypix_lib::commands::video_commands::mirror_video,
            tinypix_lib::commands::video_commands::rotate_video,
            tinypix_lib::commands::video_commands::change_video_speed,
            tinypix_lib::commands::video_commands::merge_videos,
            tinypix_lib::commands::video_commands::convert_video_format,
            tinypix_lib::commands::video_commands::edit_and_export_video,
            tinypix_lib::commands::audio_commands::extract_audio,
            tinypix_lib::commands::audio_commands::inspect_audio,
        ])
        .run(tauri::generate_context!())
        .expect("failed to start Tauri app");
}
