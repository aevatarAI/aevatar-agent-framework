# AxiomReasoning.AppHost - Architecture

## 目标

用 .NET Aspire 将 `Aevatar.AxiomReasoning` 封装成一个可观测、可一键启动的多服务入口：

- 统一的资源视图（Aspire Dashboard）
- 统一的分布式追踪/日志/指标（OTEL）
- 统一的健康检查探针（/health）

## 目录结构

```
apps/AxiomReasoning.AppHost/
├── AxiomReasoning.AppHost.csproj   # Aspire Host 工程（引用被托管服务）
├── Program.cs                      # 分布式应用编排（AddProject/端口/健康检查）
├── README.md                       # 使用说明
└── docs/
    └── ARCHITECTURE.md             # 架构镜像（本文件）
```

## 编排模型

```
┌─────────────────────────────────────┐
│        Aspire Dashboard             │
│   (Tracing, Logs, Metrics)          │
└─────────────────────────────────────┘
                │  OTLP
                ▼
┌─────────────────────────────────────┐
│      Aevatar.AxiomReasoning         │
│  - Web UI (static files)            │
│  - REST API (/api/*)                │
│  - SSE (/api/.../events)            │
│  - Health (/health)                 │
│  - OTEL Exporter (auto via env)     │
└─────────────────────────────────────┘
```

## 关键约束（设计品味）

- **显式端口**：固定映射 `5001`，避免“动态端口导致文档/脚本漂移”的特殊情况。
- **健康检查统一**：AppHost 使用 `.WithHttpHealthCheck("/health")`，让 Dashboard 直接呈现健康状态。
- **不引入额外服务默认值层**：仓库现有 AppHost 没有 `ServiceDefaults`，保持一致，减少抽象层。

## 运行方式

```bash
cd apps/AxiomReasoning.AppHost
dotnet run
```

