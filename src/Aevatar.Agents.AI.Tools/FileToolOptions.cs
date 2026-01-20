namespace Aevatar.Agents.AI.Tools;

public sealed record FileToolOptions(
    string WorkingDirectory,
    IReadOnlyList<string> ReadRoots,
    IReadOnlyList<string> WriteRoots,
    IReadOnlyList<string> WriteExtensions,
    int MaxReadChars,
    bool AllowOverwrite)
{
    public static FileToolOptions Empty { get; } = new(
        WorkingDirectory: string.Empty,
        ReadRoots: Array.Empty<string>(),
        WriteRoots: Array.Empty<string>(),
        WriteExtensions: new[] { ".yaml", ".yml", ".json" },
        MaxReadChars: 12000,
        AllowOverwrite: false);
}
