using Aevatar.Agents.AI.Core;

namespace ProgressHookChatWebDemo;

public sealed class ProgressChatAgent : AIGAgentBase
{
    public ProgressChatAgent()
    {
    }

    public ProgressChatAgent(string id) : base(id)
    {
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult("ProgressHookChatWebDemo:ProgressChatAgent");
}
