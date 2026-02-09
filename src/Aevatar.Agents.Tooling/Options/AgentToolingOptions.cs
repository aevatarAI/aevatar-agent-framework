namespace Aevatar.Agents.Tooling.Options;

public sealed class AgentToolingOptions
{
    /// <summary>
    /// Directories for dotnet tool files.
    /// </summary>
    public List<string> DotNetToolDirectories { get; set; } = [];

    /// <summary>
    /// Max dotnet tool files to scan.
    /// </summary>
    public int DotNetToolMaxFiles { get; set; } = 128;
}
