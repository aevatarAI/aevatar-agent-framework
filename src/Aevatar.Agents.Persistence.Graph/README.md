# Aevatar.Agents.Persistence.Graph

Graph persistence abstractions and utilities used by graph-backed stores.

## Responsibilities
- Provide storage adapters for state/memory/graph persistence.
- Hide provider-specific concerns behind framework abstractions.
- Keep external dependencies optional and isolated to this package.

## Key features
- Provider-specific adapters isolated per package.
- Configuration-driven wiring via DI.
- Designed to be optional (only include what you need).


## 快速开始（注册 + 全量 API 示例）
```csharp
using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;
using Aevatar.Agents.Persistence.Neo4j.Graph.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection()
    .AddAevatarGraphNeo4j("bolt://localhost:7687", "neo4j", "password"); // 注册 Driver/Session/Client/Compiler/Executor/GraphClient

await using var provider = services.BuildServiceProvider();
var graph = provider.GetRequiredService<IGraphClient>();

// Create node (可传入自定义 "id")
var aliceId = await graph.WriteAsync("Person", new Dictionary<string, Value>
{
    ["name"] = new StringValue("Alice"),
    ["age"] = new IntValue(30)
});

// Read node (找不到返回 null)
var alice = await graph.ReadAsync(aliceId);

// Update node
await graph.UpdateAsync(aliceId, new Dictionary<string, Value> { ["city"] = new StringValue("Shanghai") });

// Query nodes by conditions
var adults = await graph.QueryAsync(new NodeQuery
{
    Type = "Person",
    Conditions = [ new Condition("age", Operator.GreaterThan, new IntValue(18)) ]
});

// Create another node & edge
var bobId = await graph.WriteAsync("Person", new Dictionary<string, Value> { ["name"] = new StringValue("Bob") });
var edgeId = await graph.WriteAsync("FRIEND_OF", aliceId, bobId, new Dictionary<string, Value> { ["since"] = new IntValue(2021) });

// Read edge (找不到返回 null)
var edge = await graph.ReadAsync(edgeId);

// Query edges by type + conditions
var edges = await graph.QueryAsync(new EdgeQuery
{
    Type = "FRIEND_OF",
    Conditions = [ new Condition("since", Operator.GreaterThan, new IntValue(2020)) ]
});

// Update edge
await graph.UpdateAsync(edgeId, new Dictionary<string, Value> { ["close"] = new BoolValue(true) });

// Delete edge & nodes
await graph.DeleteAsync(edgeId);
await graph.DeleteAsync(aliceId);
await graph.DeleteAsync(bobId);
```

## API 速览
- `IGraphClient`
  - Nodes: `ReadAsync(NodeId)`, `WriteAsync(string, props)`, `UpdateAsync(NodeId, props)`, `DeleteAsync(NodeId)`, `DeleteAsync(NodeQuery)`, `QueryAsync(NodeQuery)`
  - Edges: `ReadAsync(EdgeId)`, `WriteAsync(string, from, to, props)`, `UpdateAsync(EdgeId, props)`, `DeleteAsync(EdgeId)`, `QueryAsync(EdgeQuery)`, `DeleteAsync(EdgeQuery)`
- Neo4j 扩展：`services.AddAevatarGraphNeo4j(uri, user, password, database?)`
- 编译/执行扩展：实现并替换 `IGraphCompiler<T>` / `IGraphExecutor<T>` 可适配新后端。

## 扩展后端
1) 复用 `Core` 层：实现新的 `IGraphCompiler<TCommand>` 与 `IGraphExecutor<TCommand>`。
2) 在 DI 中注册你的编译器/执行器替换默认的 Cypher/Neo4j 实现。
3) 如需新属性类型，扩展 `Value` 子类并在编译/映射中处理。

## Public API highlights
- `IGraphCompiler`
- `GraphClient`
- `IGraphExecutor`
- `struct`
- `IGraphClient`
- `ReadNode`
- `CreateNode`
- `UpdateNode`
- `DeleteNode`
- `QueryNodes`

## NuGet packaging
- **Recommended**: Optional.
- **Why**: This is an adapter/integration module; publish it if you want consumers to opt in without pulling extra dependencies.
- **Packaging note**: keep external dependencies isolated here; core packages should not depend on it.

## Build

```bash
dotnet build src/Aevatar.Agents.Persistence.Graph/Aevatar.Agents.Persistence.Graph.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
