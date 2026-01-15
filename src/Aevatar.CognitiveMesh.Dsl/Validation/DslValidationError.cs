namespace Aevatar.CognitiveMesh.Dsl.Validation;

/// <summary>
/// Represents a single validation issue surfaced by the DSL compiler.
/// </summary>
public sealed record DslValidationError(string Code, string Message, string? Path = null)
{
    public override string ToString() => Path is null ? $"{Code}: {Message}" : $"{Code}({Path}): {Message}";
}

