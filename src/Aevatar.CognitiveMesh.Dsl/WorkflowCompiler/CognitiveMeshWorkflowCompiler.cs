using System.Text.Json;
using Aevatar.Agents.Sessions.Abstractions.Workflows;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Options;
using Aevatar.CognitiveMesh.Dsl.Validation;

namespace Aevatar.CognitiveMesh.Dsl.WorkflowCompiler;

/// <summary>
/// Workflow compiler adapter for Sessions layer.
/// Converts CognitiveMesh DSL YAML/JSON into stable workflow DTOs.
/// </summary>
public sealed class CognitiveMeshWorkflowCompiler : IWorkflowCompiler
{
    public WorkflowCompileResult Compile(WorkflowCompileRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var raw = (request.Raw ?? string.Empty).Replace("\r", "").Trim();
        if (raw.Length == 0)
        {
            return WorkflowCompileResult.Failed(
                normalizedJson: string.Empty,
                new WorkflowValidationError("mesh.empty", "workflow YAML 为空。"));
        }

        string json;
        IReadOnlySet<string> nodeTypes;
        try
        {
            (json, nodeTypes) = MeshInputCoercer.CoerceToJson(raw);
        }
        catch (Exception ex)
        {
            return WorkflowCompileResult.Failed(
                normalizedJson: string.Empty,
                new WorkflowValidationError("mesh.coerce_failed", ex.Message));
        }

        var options = CognitiveDslOptions.Default.With(
            allowedAgentTypes: MergeAllowedAgentTypes(request.KnownRoles, nodeTypes, request.MetaAgentTypeName),
            allowedConstraintTypes: CognitiveDslOptions.Default.AllowedConstraintTypes,
            metaAgentTypeName: request.MetaAgentTypeName);

        var compiler = new CognitiveDslCompiler(options);
        try
        {
            var def = compiler.Compile(json);
            return WorkflowCompileResult.Success(ToCompiled(def), json);
        }
        catch (DslCompilationException ex)
        {
            var errors = ex.Errors?.Select(e => new WorkflowValidationError(e.Code, e.Message, e.Path)).ToArray()
                         ?? Array.Empty<WorkflowValidationError>();

            if (errors.Length == 0)
            {
                errors =
                [
                    new WorkflowValidationError("mesh.compile_failed", ex.Message)
                ];
            }

            return new WorkflowCompileResult(false, null, errors, json);
        }
        catch (Exception ex)
        {
            return WorkflowCompileResult.Failed(
                normalizedJson: json,
                new WorkflowValidationError("mesh.compile_exception", ex.Message));
        }
    }

    private static IReadOnlySet<string> MergeAllowedAgentTypes(
        IReadOnlyCollection<string> knownRoles,
        IReadOnlySet<string> nodeTypes,
        string metaAgentTypeName)
    {
        var set = new HashSet<string>(CognitiveDslOptions.Default.AllowedAgentTypes, StringComparer.OrdinalIgnoreCase);
        foreach (var role in knownRoles)
            set.Add(role);
        foreach (var t in nodeTypes)
            set.Add(t);
        if (!string.IsNullOrWhiteSpace(metaAgentTypeName))
            set.Add(metaAgentTypeName);
        return set;
    }

    private static CompiledWorkflowDefinition ToCompiled(MeshDefinition def)
    {
        var nodes = def.Nodes
            .Select(n => new WorkflowNodeSpec(
                Id: (n.Id ?? string.Empty).Trim(),
                Type: (n.Type ?? string.Empty).Trim(),
                Params: n.Params))
            .Where(n => n.Id.Length > 0)
            .ToList();

        var edges = def.Edges
            .Select(e => new WorkflowEdgeSpec(
                From: (e.From ?? string.Empty).Trim(),
                To: (e.To ?? string.Empty).Trim(),
                Channel: (e.Channel ?? string.Empty).Trim()))
            .Where(e => e.From.Length > 0 && e.To.Length > 0)
            .ToList();

        return new CompiledWorkflowDefinition(
            Goal: def.Goal == null ? null : new WorkflowGoalSpec(def.Goal.Name),
            Nodes: nodes,
            Edges: edges);
    }
}

