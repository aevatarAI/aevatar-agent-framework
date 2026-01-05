import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// =============================================================================
// Vite Dev Proxy
//
// 目的：本地开发时让浏览器请求同源（避免 CORS/证书问题），由 Vite 代转发到 .NET API。
// - 前端请求：/api/*
// - 后端默认（https profile）：https://localhost:7100
//
// NOTE:
// - 7100 在默认 https profile 下是 HTTPS 端口；如果用 http 去连，会出现 "socket hang up"。
// - 我们让 Aspire AppHost 注入 `TRADE_API_PROXY_TARGET`，避免端口/协议漂移。
// =============================================================================
const apiTarget = (process.env.TRADE_API_PROXY_TARGET ?? "https://localhost:7100").replace(/\/+$/, "");

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    host: true,
    proxy: {
      "/api": {
        target: apiTarget,
        changeOrigin: true,
        secure: false,
      },
      "/swagger": {
        target: apiTarget,
        changeOrigin: true,
        secure: false,
      },
      "/metrics": {
        target: apiTarget,
        changeOrigin: true,
        secure: false,
      },
      "/health": {
        target: apiTarget,
        changeOrigin: true,
        secure: false,
      },
    },
  },
});


