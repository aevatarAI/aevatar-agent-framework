namespace Aevatar.Agents.AI.Core.Hooks;

/// <summary>
/// Hook stage names for external adapters and configuration.
/// </summary>
public enum AevatarAgentHookStage
{
    SessionStart = 0,
    SessionEnd = 1,
    Stop = 2,
    BeforeLLMRequest = 3,
    AfterLLMResponse = 4,
    BeforeToolExecute = 5,
    AfterToolExecute = 6,
    OnError = 7
}
