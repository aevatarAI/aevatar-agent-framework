# Aevatar.Agents.AI.Core/Tool

## 本质

`Tool/` 是 **工具系统的实现目录**：负责工具定义（schema）、注册/执行、以及 MCP（Model Context Protocol）集成。

> 注意：本仓库的工具系统命名空间为 `Aevatar.Agents.AI.Tool.*`，但编译产物仍由 `Aevatar.Agents.AI.Core` 提供。

## 目录骨架

```
Tool/
├── Abstractions/               # ToolDefinition/ToolContext/IAevatarTool 等核心契约
│   └── Typed/                  # Typed tool base（避免 Abstractions 目录文件过多）
├── Tools/                      # ToolManager + Core/BuiltIn/Custom tools
│   └── BuiltIn/
│       └── WebSearch/          # web_search: providers + tool + models（可插拔）
├── MCP/                        # MCP 客户端封装 + ToolAdapter + 配置解析
├── Messages/                   # Tool 相关 Protobuf 生成代码的承载目录（partial/补充）
├── tool_messages.proto         # 跨边界 tool events/messages（Protobuf）
└── ToolConstants.cs            # 工具系统常量
```

## 依赖边界（极简）

- **对外**：业务只依赖 `Aevatar.Agents.AI.Core`，通过 `AIGAgentBase` 自动获得工具能力
- **对内**：`Tool/` 仅实现工具系统，不承载业务 Agent 逻辑


