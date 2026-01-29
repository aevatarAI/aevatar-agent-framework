# Aevatar.Agents.Tooling Architecture

## 结构树 (Structure)

```
src/Aevatar.Agents.Tooling/
├── Catalog/
│   └── AgentToolCatalog.cs        # Tool 列表/注册/扫描
├── Models/
│   └── ToolCatalogModels.cs       # Tool DTO
├── Options/
│   └── AgentToolingOptions.cs     # 目录与扫描配置
├── docs/
│   └── ARCHITECTURE.md
├── Aevatar.Agents.Tooling.csproj
└── ServiceCollectionExtensions.cs # AddAevatarAgentTooling
```

## 职责边界 (Responsibilities)

- **AgentToolCatalog**：统一管理 Tool 列表与 DotNet Tool 注册。
- **AgentToolingOptions**：集中配置扫描目录与扫描上限。

## 依赖关系 (Dependencies)

- **Aevatar.Agents.AI.Core**：`AIGAgentBase` 与 Tool 基础能力。
