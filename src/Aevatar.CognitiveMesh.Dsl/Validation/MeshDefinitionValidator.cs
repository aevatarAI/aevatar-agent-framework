using System;
using System.Collections.Generic;
using System.Linq;
using Aevatar.CognitiveMesh.Dsl.Models;

namespace Aevatar.CognitiveMesh.Dsl.Validation;

internal static class MeshDefinitionValidator
{
    public static IReadOnlyList<DslValidationError> Validate(MeshDefinition definition)
    {
        var errors = new List<DslValidationError>();

        if (string.IsNullOrWhiteSpace(definition.DslVersion))
        {
            errors.Add(new DslValidationError("dsl.version_required", "必须提供 dsl_version。", "dsl_version"));
        }

        if (definition.Goal is null || string.IsNullOrWhiteSpace(definition.Goal.Name))
        {
            errors.Add(new DslValidationError("goal.name_required", "goal.name 不可为空。", "goal.name"));
        }

        if (definition.Nodes is null || definition.Nodes.Count == 0)
        {
            errors.Add(new DslValidationError("nodes.required", "必须至少声明一个节点。", "nodes"));
        }

        if (definition.Budget is null)
        {
            errors.Add(new DslValidationError("budget.required", "必须声明预算。", "budget"));
        }
        else
        {
            if (definition.Budget.MaxSteps <= 0)
            {
                errors.Add(new DslValidationError("budget.max_steps", "max_steps 必须大于 0。", "budget.max_steps"));
            }

            if (definition.Budget.TokenLimit <= 0)
            {
                errors.Add(new DslValidationError("budget.token_limit", "token_limit 必须大于 0。", "budget.token_limit"));
            }
        }

        if (definition.Edges is null)
        {
            errors.Add(new DslValidationError("edges.required", "edges 不可为 null。", "edges"));
        }

        if (definition.Constraints is null)
        {
            errors.Add(new DslValidationError("constraints.required", "constraints 不可为 null。", "constraints"));
        }

        return errors;
    }
}

