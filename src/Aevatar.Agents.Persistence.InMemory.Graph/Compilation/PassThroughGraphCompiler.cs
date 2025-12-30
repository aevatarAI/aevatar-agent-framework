using Aevatar.Agents.Persistence.Graph.Core;
using Aevatar.Agents.Persistence.Graph.Core.IR;

namespace Aevatar.Agents.Persistence.InMemory.Graph;

/// <summary>
/// InMemory 编译器：不做任何转换，直接返回 <see cref="GraphPlan.Operation"/>。
/// 目的：复用 GraphClient&lt;TCommand&gt; 的编译/执行流水线，让 Memory 与 Neo4j 形态一致。
/// </summary>
internal sealed class PassThroughGraphCompiler : IGraphCompiler<GraphOperation>
{
    public GraphOperation Compile(GraphPlan plan) => plan.Operation;
}


