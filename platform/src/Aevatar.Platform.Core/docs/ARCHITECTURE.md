# Aevatar.Platform.Core — 架构（Core 层）

> 目标：Core 负责配置、会话、DSL 编译与最小执行编排；CLI/TUI/Server 只做输入输出与会话驱动。

## 目录结构

```
src/Aevatar.Platform.Core/
├── Config/                   # ~/.aevatar 配置加载与默认值
├── Packs/                    # Profiles/Packs 解析（领域装配）
├── Sessions/                 # 会话事件存储与导入导出
├── Tools/                    # 工具策略与执行过滤
│   └── Plugins/              # 工具插件目录解析与注册
└── Workflow/                 # DSL 编译与执行编排
    ├── PlatformMeshCompiler.cs   # DSL JSON/YAML → MeshDefinition
    ├── WorkflowEngine.cs         # Plan + RunWorkflowAsync（执行入口）
    ├── WorkflowExecutor.cs       # Plan 执行（Hermes 路由 + 单角色执行）
    ├── HermesRouter.cs           # Hermes 规划 + 解析 + 产物落盘
    ├── RoleAgentRunner.cs        # Role Agent 运行与 YAML 装配
    ├── HermesAIGAgent.cs         # Hermes 专用 Agent（文件工具由框架层提供）
    ├── PlatformRoleAIGAgent.cs   # 平台默认角色 Agent（注册常用工具）
    └── WorkflowRuntimeModels.cs  # 运行期 DTO（RunInput/RunResult）
```

## 模块职责（一句话）

- `WorkflowEngine`: 生成执行计划，并作为运行入口调用执行器。
- `WorkflowExecutor`: 执行计划，处理 Hermes 路由与后续单角色执行。
- `HermesRouter`: 负责 Hermes prompt/解析/落盘与路由决策。
- `RoleAgentRunner`: 基于 YAML/Provider 配置运行 Role Agent。
- `HermesAIGAgent`: Hermes 专用 Agent，注册 file_read/file_write（来自框架层工具库）。
- `PlatformRoleAIGAgent`: 平台默认角色 Agent，统一注册 coding 常用工具。
- `PlatformToolPluginLoader`: 注册 ~/.aevatar/tools 与 ./aevatar/tools 中的 dotnet-file 工具。

## 架构决策

- **拆分 Hermes/执行/Agent 运行**：避免 `WorkflowEngine` 过度膨胀，降低耦合半径。
- **Hermes 文件工具内置**：工具实现上移到框架层，平台只做权限与落盘路径约束。
- **工具插件层**：C# 单文件工具沉淀在 `~/.aevatar/tools` / `./aevatar/tools`，统一注册入口。

## 开发规范

- 跨边界类型必须使用 Protobuf（state/events/config）。
- Hermes 工具仅允许访问 `~/.aevatar/{agents,workflows}` 与 `./aevatar/agents`。
- 工具插件目录默认：`~/.aevatar/tools` 与 `./aevatar/tools`。
- Workflow 执行保持最小可用，复杂编排后续再扩展。

## 变更日志

- 2026-01-17：拆分 WorkflowEngine；新增 HermesRouter/WorkflowExecutor/RoleAgentRunner/HermesAIGAgent。
- 2026-01-17：新增工具插件层（PlatformToolPluginLoader/PlatformToolPluginCatalog）。
