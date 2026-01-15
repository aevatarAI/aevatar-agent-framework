using System.Collections.Generic;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Options;

namespace Aevatar.CognitiveMesh.Dsl.Validation.Rules;

internal sealed class AllowedConstraintRule : IMeshSemanticRule
{
    private readonly CognitiveDslOptions _options;

    public AllowedConstraintRule(CognitiveDslOptions options)
    {
        _options = options;
    }

    public IEnumerable<DslValidationError> Validate(MeshDefinition definition)
    {
        for (var i = 0; i < definition.Constraints.Count; i++)
        {
            var constraint = definition.Constraints[i];
            if (!_options.AllowedConstraintTypes.Contains(constraint.Type))
            {
                yield return new DslValidationError(
                    "constraint.unsupported_type",
                    $"约束 '{constraint.Type}' 未在 DSL 配置中注册。",
                    $"constraints[{i}].type");
            }
        }
    }
}

