using System.Text;
using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core;
using Aevatar.Agents.Persistence.Graph.Core.IR;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;

namespace Aevatar.Agents.Persistence.Graph.Providers.Neo4j;

/// <summary>
/// Compiles graph operations into Cypher + parameters for Neo4jExecutor。
/// 创建节点/关系时会生成/使用属性 "id"（优先使用输入 Props 中的 id，否则 randomUUID）。
/// </summary>
public sealed class CypherCompiler : IGraphCompiler<CypherCommand>
{
    /// <inheritdoc />
    public CypherCommand Compile(GraphPlan plan) =>
        plan.Operation switch
        {
            ReadNode op => CompileReadNode(op),
            CreateNode op => CompileCreateNode(op),
            UpdateNode op => CompileUpdateNode(op),
            DeleteNode op => CompileDeleteNode(op),
            QueryNodes op => CompileQueryNodes(op),
            ReadEdge op => CompileReadEdge(op),
            CreateEdge op => CompileCreateEdge(op),
            UpdateEdge op => CompileUpdateEdge(op),
            DeleteEdge op => CompileDeleteEdge(op),
            ReadEdgesBetween op => CompileReadEdgesBetween(op),
            _ => throw new NotSupportedException($"Unsupported operation {plan.Operation.GetType().Name}")
        };

    private static CypherCommand CompileReadNode(ReadNode op) =>
        new(
            "MATCH (n { id: $id }) RETURN n",
            new Dictionary<string, object?> { ["id"] = op.Id.Value },
            op);

    private static CypherCommand CompileCreateNode(CreateNode op)
    {
        var props = ToPlainDictionary(op.Props);
        return new(
            """
            CREATE (n:__Generic {id: coalesce($id, randomUUID())})
            SET n:`$type`
            SET n += $props
            RETURN n
            """
            .Replace("`$type`", op.Type, StringComparison.Ordinal),
            new Dictionary<string, object?>
            {
                ["id"] = props.ContainsKey("id") ? props["id"] : null,
                ["props"] = props
            },
            op);
    }

    private static CypherCommand CompileUpdateNode(UpdateNode op) =>
        new(
            "MATCH (n { id: $id }) SET n += $props RETURN n",
            new Dictionary<string, object?>
            {
                ["id"] = op.Id.Value,
                ["props"] = ToPlainDictionary(op.Props)
            },
            op);

    private static CypherCommand CompileDeleteNode(DeleteNode op) =>
        new(
            "MATCH (n { id: $id }) DETACH DELETE n",
            new Dictionary<string, object?> { ["id"] = op.Id.Value },
            op);

    private static CypherCommand CompileQueryNodes(QueryNodes op)
    {
        var sb = new StringBuilder();
        var parameters = new Dictionary<string, object?>();
        sb.Append("MATCH (n:`").Append(op.Query.Type).Append("`)");
        if (op.Query.Conditions.Count > 0)
        {
            sb.Append(" WHERE ");
            for (var i = 0; i < op.Query.Conditions.Count; i++)
            {
                var c = op.Query.Conditions[i];
                var param = $"p{i}";
                if (i > 0) sb.Append(" AND ");
                sb.Append("n.").Append(c.Property).Append(' ').Append(ToOperator(c.Operator)).Append(" $").Append(param);
                parameters[param] = ToPlainValue(c.Value);
            }
        }
        sb.Append(" RETURN n");
        return new CypherCommand(sb.ToString(), parameters, op);
    }

    private static CypherCommand CompileReadEdge(ReadEdge op) =>
        new(
            "MATCH ()-[r { id: $id }]-() RETURN r",
            new Dictionary<string, object?> { ["id"] = op.Id.Value },
            op);

    private static CypherCommand CompileCreateEdge(CreateEdge op) =>
        new(
            """
            MATCH (from { id: $from }), (to { id: $to })
            MERGE (from)-[r:`$type` { id: coalesce($id, randomUUID()) }]->(to)
            SET r += $props
            RETURN r
            """
            .Replace("`$type`", op.Type, StringComparison.Ordinal),
            new Dictionary<string, object?>
            {
                ["from"] = op.From.Value,
                ["to"] = op.To.Value,
                ["id"] = null,
                ["props"] = ToPlainDictionary(op.Props)
            },
            op);

    private static CypherCommand CompileUpdateEdge(UpdateEdge op) =>
        new(
            "MATCH ()-[r { id: $id }]-() SET r += $props RETURN r",
            new Dictionary<string, object?>
            {
                ["id"] = op.Id.Value,
                ["props"] = ToPlainDictionary(op.Props)
            },
            op);

    private static CypherCommand CompileDeleteEdge(DeleteEdge op) =>
        new(
            "MATCH ()-[r { id: $id }]-() DELETE r",
            new Dictionary<string, object?> { ["id"] = op.Id.Value },
            op);

    private static CypherCommand CompileReadEdgesBetween(ReadEdgesBetween op)
    {
        var sb = new StringBuilder();
        var parameters = new Dictionary<string, object?>
        {
            ["from"] = op.From.Value,
            ["to"] = op.To.Value
        };

        sb.Append("MATCH (from { id: $from })-[r");
        if (!string.IsNullOrWhiteSpace(op.Filter?.Type))
            sb.Append(":`").Append(op.Filter!.Type).Append('`');
        sb.Append("]->(to { id: $to })");

        if (op.Filter?.Conditions?.Count > 0)
        {
            sb.Append(" WHERE ");
            for (var i = 0; i < op.Filter.Conditions.Count; i++)
            {
                var c = op.Filter.Conditions[i];
                var param = $"p{i}";
                if (i > 0) sb.Append(" AND ");
                sb.Append("r.").Append(c.Property).Append(' ').Append(ToOperator(c.Operator)).Append(" $").Append(param);
                parameters[param] = ToPlainValue(c.Value);
            }
        }

        sb.Append(" RETURN r");
        return new CypherCommand(sb.ToString(), parameters, op);
    }

    private static string ToOperator(Operator op) => op switch
    {
        Operator.Equals => "=",
        Operator.GreaterThan => ">",
        Operator.LessThan => "<",
        Operator.Contains => "CONTAINS",
        _ => "="
    };

    private static IReadOnlyDictionary<string, object?> ToPlainDictionary(IReadOnlyDictionary<string, Value> source) =>
        source.ToDictionary(k => k.Key, v => ToPlainValue(v.Value));

    private static object? ToPlainValue(Value v) => v switch
    {
        StringValue s => s.Data,
        IntValue i => i.Data,
        BoolValue b => b.Data,
        FloatValue f => f.Data,
        MapValue m => m.Fields.ToDictionary(k => k.Key, v => ToPlainValue(v.Value)),
        _ => null
    };
}

/// <summary>
/// 编译产物：包含 Cypher 文本、参数及来源操作。
/// </summary>
/// <param name="Text">最终执行的 Cypher 文本。</param>
/// <param name="Parameters">传递给驱动的参数。</param>
/// <param name="Operation">来源的图操作 IR。</param>
public sealed record CypherCommand(
    string Text,
    IReadOnlyDictionary<string, object?> Parameters,
    GraphOperation Operation);
