namespace Aevatar.Agents.AI.Tools;

public sealed record CommandToolOptions(
    string WorkingDirectory,
    IReadOnlyList<string> AllowedCommands,
    int TimeoutSeconds,
    int MaxOutputChars)
{
    public static CommandToolOptions Empty { get; } = new(
        WorkingDirectory: string.Empty,
        AllowedCommands: Array.Empty<string>(),
        TimeoutSeconds: 120,
        MaxOutputChars: 20000);
}
