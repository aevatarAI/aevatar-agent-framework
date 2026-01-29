namespace Aevatar.Agents.Tooling.Models;

public sealed record AgentToolItem(
    string Name,
    string Description,
    string Source,
    string Category,
    bool IsDangerous,
    bool RequiresConfirmation,
    List<string> Tags);

public sealed record DotNetToolFileItem(
    string Name,
    string Description,
    string FilePath,
    List<string> Tags,
    bool IsDangerous);
