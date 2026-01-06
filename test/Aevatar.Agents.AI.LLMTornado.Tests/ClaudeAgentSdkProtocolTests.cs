using Aevatar.Agents.AI.LLMTornado.ClaudeAgentSdk;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.AI.LLMTornado.Tests;

public class ClaudeAgentSdkProtocolTests
{
    [Fact]
    public void TryParseStreamLine_ShouldExtractDelta()
    {
        var ok = ClaudeAgentSdkProtocol.TryParseStreamLine(
            "AEVATAR_AGENT_SDK_STREAM:hello",
            out var delta);

        ok.ShouldBeTrue();
        delta.ShouldBe("hello");
    }

    [Fact]
    public void TryExtractOutputJsonFromText_ShouldFindLastMarker()
    {
        var stdout =
            "noise\n" +
            "AEVATAR_AGENT_SDK_OUTPUT:{\"content\":\"first\"}\n" +
            "more\n" +
            "AEVATAR_AGENT_SDK_OUTPUT:{\"content\":\"second\"}\n";

        ClaudeAgentSdkProtocol.TryExtractOutputJsonFromText(stdout, out var json).ShouldBeTrue();
        ClaudeAgentSdkProtocol.TryExtractContentFromOutputJson(json, out var content).ShouldBeTrue();
        content.ShouldBe("second");
    }
}


