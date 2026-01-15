using System;
using System.Collections.Generic;
using System.Linq;
using Aevatar.CognitiveMesh.Dsl.Models;

namespace Aevatar.CognitiveMesh.Dsl.Validation.Rules;

internal sealed class EdgesReferenceExistingNodesRule : IMeshSemanticRule
{
    public IEnumerable<DslValidationError> Validate(MeshDefinition definition)
    {
        var knownNodes = definition.Nodes.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < definition.Edges.Count; index++)
        {
            var edge = definition.Edges[index];

            if (!knownNodes.Contains(edge.From))
            {
                yield return new DslValidationError(
                    "edge.unknown_from",
                    $"边 {edge.From}->{edge.To} 的起点不存在。",
                    $"edges[{index}].from");
            }

            if (!knownNodes.Contains(edge.To))
            {
                yield return new DslValidationError(
                    "edge.unknown_to",
                    $"边 {edge.From}->{edge.To} 的终点不存在。",
                    $"edges[{index}].to");
            }
        }
    }
}

