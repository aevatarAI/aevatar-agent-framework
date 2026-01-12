namespace Aevatar.Notebook.Api.Contracts;

// ============================================================
//  API DTOs (Notebook)
//
//  Notes:
//  - Keep DTOs minimal and stable; they are HTTP boundary contracts.
// ============================================================

public sealed record ChatInDto(string Message)
{
    public string? RequestId { get; init; }
    public string? StageHint { get; init; }
    public List<string>? SelectedSourceIds { get; init; }
}

public sealed record ReportInDto
{
    public string? Topic { get; init; }
    public string? ReportId { get; init; }
    public List<string>? SelectedSourceIds { get; init; }
}

public sealed record CreateSessionInDto
{
    public string? ProviderName { get; init; }
}

public sealed record SessionInputInDto(string Message)
{
    public string? RequestId { get; init; }
    public List<string>? SelectedSourceIds { get; init; }
}


