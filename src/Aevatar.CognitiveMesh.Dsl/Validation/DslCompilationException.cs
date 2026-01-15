namespace Aevatar.CognitiveMesh.Dsl.Validation;

public class DslCompilationException : Exception
{
    public DslCompilationException(string message)
        : this(message, Array.Empty<DslValidationError>())
    { }

    public DslCompilationException(string message, Exception innerException)
        : base(message, innerException)
    { }

    public DslCompilationException(string message, IEnumerable<DslValidationError> errors)
        : base(BuildMessage(message, errors))
    {
        Errors = errors.ToArray();
    }

    public IReadOnlyList<DslValidationError> Errors { get; } = Array.Empty<DslValidationError>();

    private static string BuildMessage(string message, IEnumerable<DslValidationError> errors)
    {
        var suffix = string.Join(", ", errors.Select(e => e.ToString()));
        return $"{message}: {suffix}";
    }
}

