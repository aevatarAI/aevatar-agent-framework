using System;
using System.Linq;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Options;

namespace Aevatar.CognitiveMesh.Dsl.Validation.Rules;

internal sealed class TransformativeStrategyRule : IMeshSemanticRule
{
    private readonly CognitiveDslOptions _options;

    public TransformativeStrategyRule(CognitiveDslOptions options)
    {
        _options = options;
    }

    public IEnumerable<DslValidationError> Validate(MeshDefinition definition)
    {
        if (definition.Strategy != StrategyKind.UotTrans)
        {
            yield break;
        }

        var hasMetaAgent = definition.Nodes.Any(node =>
            node.Type.Equals(_options.MetaAgentTypeName, StringComparison.OrdinalIgnoreCase));

        if (!hasMetaAgent)
        {
            yield return new DslValidationError(
                "strategy.meta_agent_missing",
                $"Transformative 策略需要至少一个 '{_options.MetaAgentTypeName}' 节点。",
                "nodes");
        }
    }
}

