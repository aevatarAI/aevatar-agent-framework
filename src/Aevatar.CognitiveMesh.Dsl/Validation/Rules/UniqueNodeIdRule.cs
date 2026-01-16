using System;
using System.Collections.Generic;
using System.Linq;
using Aevatar.CognitiveMesh.Dsl.Models;

namespace Aevatar.CognitiveMesh.Dsl.Validation.Rules;

internal sealed class UniqueNodeIdRule : IMeshSemanticRule
{
    public IEnumerable<DslValidationError> Validate(MeshDefinition definition)
    {
        var duplicates = definition.Nodes
            .GroupBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var duplicate in duplicates)
        {
            yield return new DslValidationError(
                "node.duplicate_id",
                $"节点 Id '{duplicate.Key}' 出现 {duplicate.Count()} 次。",
                $"nodes[{duplicate.Key}]");
        }
    }
}

