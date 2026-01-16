using Aevatar.Agents.Persistence.Graph.Core;
using Aevatar.Agents.Persistence.Graph.Core.IR;

namespace Aevatar.Agents.Persistence.InMemory.Graph;

/// <summary>
/// InMemory compiler: performs no transformation, directly returns <see cref="GraphPlan.Operation"/>.
/// Purpose: reuse GraphClient&lt;TCommand&gt; compilation/execution pipeline to keep Memory and Neo4j interfaces consistent.
/// </summary>
internal sealed class PassThroughGraphCompiler : IGraphCompiler<GraphOperation>
{
    public GraphOperation Compile(GraphPlan plan) => plan.Operation;
}


