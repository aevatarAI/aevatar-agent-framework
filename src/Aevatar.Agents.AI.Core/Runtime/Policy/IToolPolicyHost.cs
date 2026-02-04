using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Tool.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal interface IToolPolicyHost
{
    ILogger Logger { get; }
    bool AllowInternalTools { get; }
    bool AllowDangerousTools { get; }

    IReadOnlyCollection<string> GetYamlSkillToolNames();
    IReadOnlyCollection<string> GetYamlDefaultSkillRoots();
    IReadOnlySet<string> GetYamlDangerousToolNames();

    bool ShouldEnableDangerousToolsFromYaml(
        AgentYamlConfig yaml,
        IReadOnlySet<string> allowlist,
        IReadOnlySet<string> dangerousToolNames);

    IReadOnlyCollection<string> GetSkillToolsAutoIncludedFromYaml(
        AgentYamlConfig yaml,
        IReadOnlyCollection<string> skillToolNames);
}

