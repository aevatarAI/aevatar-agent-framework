using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using Shouldly;

namespace Aevatar.Agents.AI.Core.Tests;

public class AIGAgentKeysTests
{
    [Fact(DisplayName = "TryGetToolAllowlist returns false when context or key is missing")]
    public void TryGetToolAllowlist_ShouldReturnFalse_WhenContextMissingOrKeyMissing()
    {
        var req1 = new AevatarLLMRequest { Context = null };
        AIGAgentKeys.TryGetToolAllowlist(req1, out _).ShouldBeFalse();

        var req2 = new AevatarLLMRequest { Context = new Dictionary<string, object>() };
        AIGAgentKeys.TryGetToolAllowlist(req2, out _).ShouldBeFalse();
    }

    [Fact(DisplayName = "TryGetToolAllowlist supports multiple allowlist value types")]
    public void TryGetToolAllowlist_ShouldSupport_MultipleValueTypes()
    {
        var fromSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b" };
        var reqSet = new AevatarLLMRequest
        {
            Context = new Dictionary<string, object> { [AIGAgentKeys.ToolAllowlist] = fromSet }
        };
        AIGAgentKeys.TryGetToolAllowlist(reqSet, out var setResult).ShouldBeTrue();
        setResult.Contains("a").ShouldBeTrue();
        setResult.Contains("b").ShouldBeTrue();

        var reqArray = new AevatarLLMRequest
        {
            Context = new Dictionary<string, object> { [AIGAgentKeys.ToolAllowlist] = new[] { "x", "y" } }
        };
        AIGAgentKeys.TryGetToolAllowlist(reqArray, out var arrResult).ShouldBeTrue();
        arrResult.SetEquals(new[] { "x", "y" }).ShouldBeTrue();

        var reqList = new AevatarLLMRequest
        {
            Context = new Dictionary<string, object> { [AIGAgentKeys.ToolAllowlist] = new List<string> { "m", "n" } }
        };
        AIGAgentKeys.TryGetToolAllowlist(reqList, out var listResult).ShouldBeTrue();
        listResult.SetEquals(new[] { "m", "n" }).ShouldBeTrue();

        var reqSingle = new AevatarLLMRequest
        {
            Context = new Dictionary<string, object> { [AIGAgentKeys.ToolAllowlist] = "only_one" }
        };
        AIGAgentKeys.TryGetToolAllowlist(reqSingle, out var singleResult).ShouldBeTrue();
        singleResult.SetEquals(new[] { "only_one" }).ShouldBeTrue();
    }

    [Fact(DisplayName = "TryGetToolAllowlist returns false for unsupported allowlist value types")]
    public void TryGetToolAllowlist_ShouldReturnFalse_ForUnsupportedTypes()
    {
        var req = new AevatarLLMRequest
        {
            Context = new Dictionary<string, object> { [AIGAgentKeys.ToolAllowlist] = 123 }
        };
        AIGAgentKeys.TryGetToolAllowlist(req, out _).ShouldBeFalse();
    }

    [Fact(DisplayName = "ClearToolAllowlist removes allowlist and source-skill keys")]
    public void ClearToolAllowlist_ShouldRemoveBothKeys()
    {
        var req = new AevatarLLMRequest
        {
            Context = new Dictionary<string, object>
            {
                [AIGAgentKeys.ToolAllowlist] = new[] { "a" },
                [AIGAgentKeys.ToolAllowlistSourceSkill] = "skill"
            }
        };

        AIGAgentKeys.ClearToolAllowlist(req);

        req.Context!.ContainsKey(AIGAgentKeys.ToolAllowlist).ShouldBeFalse();
        req.Context!.ContainsKey(AIGAgentKeys.ToolAllowlistSourceSkill).ShouldBeFalse();
    }
}


