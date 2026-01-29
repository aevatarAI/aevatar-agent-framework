# Aevatar.Agents.Tooling

Agent 工具目录与注册的轻量模块，负责汇总工具清单、扫描 .NET 工具文件，
并对外提供统一的工具目录接口。

## 主要职责
- 通过 `AgentToolCatalog` 汇总 Agent 已注册工具（需确保工具初始化完成）。
- 扫描并解析 .NET 工具文件（`*Tool.cs` / `*Skill.cs`），生成可注册的工具条目。
- 统一工具来源标记（Core / Custom / MCP / Skills / DotNetFile）。
- 提供 DI 扩展与集中配置。

## 关键入口
- `AddAevatarAgentTooling(...)`
- `AgentToolCatalog.GetToolsAsync(...)`
- `AgentToolCatalog.ListDotNetFilesAsync(...)`
- `AgentToolCatalog.RegisterDotNetFileAsync(...)`

## 配置
- `AgentToolingOptions.DotNetToolDirectories`
- `AgentToolingOptions.DotNetToolMaxFiles`

当未配置目录时，会使用默认候选路径（`~/.aevatar/tools/dotnet`、`~/.aevatar/dotnet-tools`）。

## 依赖关系
- `Aevatar.Agents.AI.Core`：`AIGAgentBase` 与工具注册流程
- `Aevatar.Agents.AI.Tool`：工具抽象与 DotNetFileSkillTool

## 相关文档
- `docs/ARCHITECTURE.md`

