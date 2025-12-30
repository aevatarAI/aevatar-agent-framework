using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;
using Aevatar.Agents.Persistence.Neo4j.Graph.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Demo: 演示基于 Neo4j 实现的 GraphClient，调用所有公开方法并打印结果。
// 运行前请设置以下环境变量或修改默认值：
//   NEO4J_URI (示例：bolt://localhost:7687)
//   NEO4J_USERNAME
//   NEO4J_PASSWORD
// 可选：NEO4J_DATABASE（默认 neo4j）

var uri = Environment.GetEnvironmentVariable("NEO4J_URI") ?? "bolt://localhost:7687";
var username = Environment.GetEnvironmentVariable("NEO4J_USERNAME") ?? "neo4j";
var password = Environment.GetEnvironmentVariable("NEO4J_PASSWORD") ?? "password";
var database = Environment.GetEnvironmentVariable("NEO4J_DATABASE") ?? "neo4j";

using var services = new ServiceCollection()
    .AddLogging(b => b.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    }))
    .AddAevatarGraphNeo4j(uri, username, password, database)
    .BuildServiceProvider();

var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("GraphNeo4jDemo");
var graph = services.GetRequiredService<IGraphClient>();

logger.LogInformation("Using Neo4j @ {Uri} / DB: {Db}", uri, database);

try
{
    // 创建两个节点
    var aliceId = await graph.WriteAsync(
        "Person",
        new Dictionary<string, Value>
        {
            ["name"] = new StringValue("Alice"),
            ["age"] = new IntValue(30)
        });
    logger.LogInformation("Created node Alice: {Id}", aliceId.Value);

    var bobId = await graph.WriteAsync(
        "Person",
        new Dictionary<string, Value>
        {
            ["name"] = new StringValue("Bob"),
            ["age"] = new IntValue(28)
        });
    logger.LogInformation("Created node Bob: {Id}", bobId.Value);

    // 读取单节点
    var alice = await graph.ReadAsync(aliceId);
    logger.LogInformation("Read node Alice => {Node}", FormatNode(alice));

    // 更新节点
    await graph.UpdateAsync(aliceId, new Dictionary<string, Value>
    {
        ["city"] = new StringValue("Shanghai")
    });
    logger.LogInformation("Updated node Alice city");

    // 条件查询
    var people = await graph.QueryAsync(new NodeQuery
    {
        Type = "Person",
        Conditions =
        [
            new Condition("age", Operator.GreaterThan, new IntValue(25))
        ]
    });
    logger.LogInformation("Query people > 25 => {Count} result(s)", people.Count);
    foreach (var p in people)
    {
        logger.LogInformation(" - {Person}", FormatNode(p));
    }

    // 创建关系
    var edgeId = await graph.WriteAsync(
        "FRIEND_OF",
        aliceId,
        bobId,
        new Dictionary<string, Value>
        {
            ["since"] = new IntValue(2021),
            ["close"] = new BoolValue(true)
        });
    logger.LogInformation("Created edge FRIEND_OF: {Id}", edgeId.Value);

    // 读取关系
    var edge = await graph.ReadAsync(edgeId);
    logger.LogInformation("Read edge => {Edge}", FormatEdge(edge));

    // 查询两节点之间的关系
    var edges = await graph.ReadBetweenAsync(
        aliceId,
        bobId,
        new EdgeQuery
        {
            Type = "FRIEND_OF",
            Conditions =
            [
                new Condition("since", Operator.GreaterThan, new IntValue(2020))
            ]
        });
    logger.LogInformation("Read edges between Alice and Bob => {Count} result(s)", edges.Count);
    foreach (var e in edges)
    {
        logger.LogInformation(" - {Edge}", FormatEdge(e));
    }

    // 更新关系
    await graph.UpdateAsync(edgeId, new Dictionary<string, Value>
    {
        ["close"] = new BoolValue(false)
    });
    logger.LogInformation("Updated edge close flag");

    // 删除关系与节点
    await graph.DeleteAsync(edgeId);
    logger.LogInformation("Deleted edge: {Id}", edgeId.Value);

    await graph.DeleteAsync(aliceId);
    await graph.DeleteAsync(bobId);
    logger.LogInformation("Deleted nodes: {Alice}, {Bob}", aliceId.Value, bobId.Value);
}
catch (Exception ex)
{
    logger.LogError(ex, "Graph demo failed");
}

static string FormatNode(GraphNode? node)
{
    if (node is null) return "(null)";
    var props = string.Join(", ", node.Properties.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));
    return $"Node[{node.Id.Value}]<{node.Type}> {{{props}}}";
}

static string FormatEdge(GraphEdge? edge)
{
    if (edge is null) return "(null)";
    var props = string.Join(", ", edge.Properties.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));
    return $"Edge[{edge.Id.Value}]<{edge.Type}> {edge.From.Value} -> {edge.To.Value} {{{props}}}";
}

static string FormatValue(Value v) => v switch
{
    StringValue s => s.Data,
    IntValue i => i.Data.ToString(),
    BoolValue b => b.Data.ToString(),
    FloatValue f => f.Data.ToString("G"),
    MapValue m => $"{{{string.Join(", ", m.Fields.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"))}}}",
    _ => "(unknown)"
};
