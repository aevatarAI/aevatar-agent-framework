# Aevatar.Agents.Persistence.Neo4j.Graph

Neo4j provider for `Aevatar.Agents.Persistence.Graph`：

- **编译**：`GraphOperation` → `CypherCommand`（`CypherCompiler`）
- **执行**：`CypherCommand` → Neo4j.Driver（`Neo4jExecutor` / `INeo4jClient`）
- **DI**：`services.AddAevatarGraphNeo4j(...)`（内部会先注册 `Aevatar.Agents.Persistence.Neo4j` 的基础设施）

## 目录结构

```
src/Aevatar.Agents.Persistence.Neo4j.Graph/
├── DependencyInjection/            # DI 扩展（AddAevatarGraphNeo4j）
├── Client/                         # Graph 相关便捷扩展（ReadNodesAsync / ReadEdgesAsync）
├── Compilation/                    # CypherCompiler + CypherCommand
├── Execution/                      # Neo4jExecutor
├── Mapping/                        # GraphMapper
└── docs/README.md                  # 本文档
```

对应的 Neo4j 基础设施在：

```
src/Aevatar.Agents.Persistence.Neo4j/
├── DependencyInjection/            # AddAevatarNeo4j
├── Options/                        # Neo4jPersistenceOptions
├── Driver/                         # Driver factory
├── Sessions/                       # Session factory
└── Client/                         # Neo4jClient (ReadAsync/WriteAsync)
```

## 快速开始

```csharp
using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Neo4j.Graph.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection()
    .AddAevatarGraphNeo4j("bolt://localhost:7687", "neo4j", "password");

await using var provider = services.BuildServiceProvider();
var graph = provider.GetRequiredService<IGraphClient>();
```

## 配置

`Neo4jPersistenceOptions`：

- `Uri`（必填，`bolt://...` / `neo4j+s://...`）
- `Username` / `Password`（必填）
- `Database`（默认 `neo4j`）


