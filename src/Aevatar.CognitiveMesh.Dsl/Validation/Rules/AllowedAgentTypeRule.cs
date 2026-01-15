using System.Collections.Generic;
using System.Linq;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Options;

namespace Aevatar.CognitiveMesh.Dsl.Validation.Rules;

internal sealed class AllowedAgentTypeRule : IMeshSemanticRule
{
    private readonly CognitiveDslOptions _options;

    public AllowedAgentTypeRule(CognitiveDslOptions options)
    {
        _options = options;
    }

    public IEnumerable<DslValidationError> Validate(MeshDefinition definition)
    {
        for (var index = 0; index < definition.Nodes.Count; index++)
        {
            var node = definition.Nodes[index];
            if (!_options.AllowedAgentTypes.Contains(node.Type))
            {
                yield return new DslValidationError(
                    "node.unsupported_type",
                    $"节点 '{node.Id}' 使用了未注册的 Agent 类型 '{node.Type}'。",
                    $"nodes[{index}].type");
            }
        }
    }
}

