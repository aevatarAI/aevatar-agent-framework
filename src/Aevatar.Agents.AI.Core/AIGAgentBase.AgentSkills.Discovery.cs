namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    private IReadOnlyList<AgentSkillDescriptor> DiscoverAgentSkills(
        IReadOnlyList<string> roots,
        CancellationToken cancellationToken)
    {
        return AgentSkillsRuntime.DiscoverAgentSkills(roots, cancellationToken);
    }

    private static SkillMarkdown ParseSkillMarkdown(string markdown)
        => AgentSkillsRuntime.ParseSkillMarkdown(markdown);

    private static string ReadAllTextWithLimit(string path, int maxChars, CancellationToken cancellationToken)
        => AgentSkillsRuntime.ReadAllTextWithLimit(path, maxChars, cancellationToken);

    private static Task<string> ReadAllTextWithLimitAsync(string path, int maxChars, CancellationToken cancellationToken)
        => AgentSkillsRuntime.ReadAllTextWithLimitAsync(path, maxChars, cancellationToken);
}