#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

// ============================================================
//  Aevatar.Learning Desktop (Tauri)
//
//  NOTE:
//  - UI is React + Vite.
//  - Backend is a separate .NET API (ASP.NET Core) exposing:
//    - /health, /api/info
//    - /api/sessions/* (AG-UI SSE)
//
//  SECURITY:
//  - CSP/connect-src is configured in tauri.conf.json for local dev.
// ============================================================

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_fs::init())
        .run(tauri::generate_context!())
        .expect("error while running Aevatar.Learning");
}


