using Aevatar.Agents;
using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Sessions;

public sealed class CognitiveSessionOptions
{
    public string DefaultProviderName { get; set; } = AevatarAgentsConstants.DefaultProviderName;
    public int DefaultWorkerCount { get; set; } = 5;
    public bool EnableAgentMemory { get; set; }
    public bool EnableSessionMemory { get; set; } = true;
    public bool RegisterAllWorkflows { get; set; } = true;
}
