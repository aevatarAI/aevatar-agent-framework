namespace Aevatar.Agents.Cognitive.Primitives;

// ============================================================
//  Workflow / Step data model (DSL runtime core)
//
//  WHY:
//  - WorkflowParser needs a stable C# data model to carry YAML DSL.
//  - These types don't cross Actor boundaries (runtime memory structures), don't require Protobuf.
// ============================================================

public sealed class WorkflowDefinition
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";

    public List<InputParameter> Inputs { get; set; } = [];
    public List<StepDefinition> Steps { get; set; } = [];

    /// <summary>
    /// Output mapping: key -> template expression.
    /// </summary>
    public Dictionary<string, string> Output { get; set; } = new(StringComparer.Ordinal);
}

public sealed class InputParameter
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public object? DefaultValue { get; set; }
}

public sealed class StepDefinition
{
    // ============================================================
    //  Identity
    // ============================================================

    public string Id { get; set; } = "";
    public string Type { get; set; } = "";

    // ============================================================
    //  Common fields
    // ============================================================

    public Dictionary<string, object?> Parameters { get; set; } = new(StringComparer.Ordinal);
    public string? Store { get; set; }

    // ============================================================
    //  conditional
    // ============================================================

    public string? Condition { get; set; }
    public List<StepDefinition>? IfTrue { get; set; }
    public List<StepDefinition>? IfFalse { get; set; }

    // ============================================================
    //  vote
    // ============================================================

    public StepDefinition? Generator { get; set; }

    // ============================================================
    //  fan_out
    // ============================================================

    public string? ForEach { get; set; }
    public StepDefinition? Step { get; set; }
    public string? Reduce { get; set; }
    public object? MaxConcurrency { get; set; }

    // ============================================================
    //  workflow_call
    // ============================================================

    public string? Workflow { get; set; }
    public Dictionary<string, object?>? Params { get; set; }
    public object? MaxDepth { get; set; }
}

public interface IWorkflowRegistry
{
    void Register(WorkflowDefinition workflow);
    WorkflowDefinition? Get(string name);
    IReadOnlyList<string> List();
    void Remove(string name);
    void Clear();
}

