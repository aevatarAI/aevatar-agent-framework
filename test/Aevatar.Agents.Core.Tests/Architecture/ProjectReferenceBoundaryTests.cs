using System.Xml.Linq;
using FluentAssertions;

namespace Aevatar.Agents.Core.Tests.Architecture;

public sealed class ProjectReferenceBoundaryTests
{
    [Fact]
    public void Runtime_projects_should_not_reference_AI_Core()
    {
        var root = FindRepoRoot();

        AssertNoProjectReference(
            Path.Combine(root, "src", "Aevatar.Agents.Runtime", "Aevatar.Agents.Runtime.csproj"),
            @"..\Aevatar.Agents.AI.Core\Aevatar.Agents.AI.Core.csproj");

        AssertNoProjectReference(
            Path.Combine(root, "src", "Aevatar.Agents.Runtime.Local", "Aevatar.Agents.Runtime.Local.csproj"),
            @"..\Aevatar.Agents.AI.Core\Aevatar.Agents.AI.Core.csproj");

        AssertNoProjectReference(
            Path.Combine(root, "src", "Aevatar.Agents.Runtime.Orleans", "Aevatar.Agents.Runtime.Orleans.csproj"),
            @"..\Aevatar.Agents.AI.Core\Aevatar.Agents.AI.Core.csproj");

        AssertNoProjectReference(
            Path.Combine(root, "src", "Aevatar.Agents.Runtime.ProtoActor", "Aevatar.Agents.Runtime.ProtoActor.csproj"),
            @"..\Aevatar.Agents.AI.Core\Aevatar.Agents.AI.Core.csproj");
    }

    [Fact]
    public void Sessions_should_not_reference_RuntimeLocal_or_CognitiveMeshDsl()
    {
        var root = FindRepoRoot();
        var sessions = Path.Combine(root, "src", "Aevatar.Agents.Sessions", "Aevatar.Agents.Sessions.csproj");

        AssertNoProjectReference(
            sessions,
            @"..\Aevatar.Agents.Runtime.Local\Aevatar.Agents.Runtime.Local.csproj");

        AssertNoProjectReference(
            sessions,
            @"..\Aevatar.CognitiveMesh.Dsl\Aevatar.CognitiveMesh.Dsl.csproj");
    }

    [Fact]
    public void CognitiveCore_should_not_reference_apps()
    {
        var root = FindRepoRoot();
        var cognitiveCore = Path.Combine(root, "src", "Aevatar.Agents.Cognitive.Core", "Aevatar.Agents.Cognitive.Core.csproj");

        var refs = ReadProjectReferences(cognitiveCore);
        refs.Should().NotContain(r => r.Contains(@"\apps\", StringComparison.OrdinalIgnoreCase) ||
                                      r.Contains(@"/apps/", StringComparison.OrdinalIgnoreCase),
            "core libraries must not take dependencies on apps");
    }

    private static void AssertNoProjectReference(string csprojPath, string forbiddenInclude)
    {
        var refs = ReadProjectReferences(csprojPath);
        refs.Should().NotContain(r => string.Equals(Normalize(r), Normalize(forbiddenInclude), StringComparison.OrdinalIgnoreCase),
            $"{Path.GetFileName(csprojPath)} should not reference {forbiddenInclude}");
    }

    private static IReadOnlyList<string> ReadProjectReferences(string csprojPath)
    {
        File.Exists(csprojPath).Should().BeTrue($"expected csproj at {csprojPath}");

        var doc = XDocument.Load(csprojPath);
        var refs = doc.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .ToList();

        return refs;
    }

    private static string Normalize(string path)
        => path.Replace('/', '\\').Trim();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "aevatar-agent-framework.slnx");
            if (File.Exists(candidate))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Cannot find repo root (aevatar-agent-framework.slnx).");
    }
}

