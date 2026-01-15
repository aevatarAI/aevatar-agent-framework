#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

// ============================================================
//  NovelOS Desktop (Tauri)
//
//  NOTE:
//  - UI is React + Monaco.
//  - Sidecar is a separate .NET process (ASP.NET Core) exposing:
//    - Protobuf JSON HTTP APIs
//    - SSE event stream (SidecarEvent)
//
//  SECURITY:
//  - File access is gated by Tauri capability scopes.
// ============================================================

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_fs::init())
        .run(tauri::generate_context!())
        .expect("error while running NovelOS");
}


