# Aevatar.Agents.Persistence.Neo4j

Neo4j **基础设施层**（Backend-first）：

- `Neo4jPersistenceOptions`：连接配置（Uri/Username/Password/Database）
- `INeo4jDriverFactory` / `Neo4jDriverFactory`
- `INeo4jSessionFactory` / `Neo4jSessionFactory`
- `INeo4jClient` / `Neo4jClient`（仅提供通用 `ReadAsync/WriteAsync`）

> 注意：该项目不包含任何“业务能力”（例如 Graph 的 Cypher 编译/执行）。  
> Graph Provider 在 `Aevatar.Agents.Persistence.Neo4j.Graph`。

## 目录结构

```
src/Aevatar.Agents.Persistence.Neo4j/
├── DependencyInjection/                 # AddAevatarNeo4j
├── Options/                             # Neo4jPersistenceOptions
├── Driver/                              # Driver factory
├── Sessions/                            # Session factory
├── Client/                              # Neo4jClient (ReadAsync/WriteAsync)
└── docs/README.md
```


