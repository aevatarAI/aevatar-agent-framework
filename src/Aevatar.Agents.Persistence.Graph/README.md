# Aevatar.Agents.Persistence.Graph

基于强类型 IR 的图持久化库（**Graph Core**），提供统一的节点/关系 CRUD 与查询 API，并内置编译-执行流水线。
具体后端以 Provider 形式提供（例如 Neo4j / Memory），Graph Core 本身不依赖任何数据库驱动。

## 目录速览
- `Abstractions/`：`NodeId`、`EdgeId`、`IGraphClient` 等公共契约
- `Core/`
  - `Semantic/`：`GraphNode`、`GraphEdge`、`Value`（String/Int/Bool/Float/Map）、`Condition`、`Operator`、`NodeQuery`、`EdgeQuery`
  - `IR/`：`GraphOperation` 及具体操作记录（Read/Create/Update/Delete/Query）
  - `GraphClient`：组合编译器与执行器的统一客户端
- Providers（独立项目）
  - `Aevatar.Agents.Persistence.Neo4j.Graph`：Cypher 编译 + Neo4j 执行 + DI 扩展
  - `Aevatar.Agents.Persistence.InMemory.Graph`：InMemory 执行 + DI 扩展（开发/测试最快）

## 运行流程
`GraphOperation` → `GraphPlan` → `IGraphCompiler<TCommand>`（Provider 自定义）→ `IGraphExecutor<TCommand>`（Provider 自定义）→ 返回 `GraphNode`/`GraphEdge`/Id/列表。

## 配置
- Neo4j Provider：参见 `Aevatar.Agents.Persistence.Neo4j.Graph` 的文档（连接配置、Cypher 细节等）。
- Id 生成约定：创建节点/关系时，属性中包含 `"id"` 则使用该值，否则由后端生成并写入属性 `id`。
- 属性类型：所有属性通过 `Value` 族封装（String/Int/Bool/Float/Map）。

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
