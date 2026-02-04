using System.Text.Json;
using Aevatar.Agents.Cognitive.Execution.Run;
using Aevatar.Agents.Cognitive.Researching.Modules;
using Aevatar.Agents.Cognitive.Researching.Runtime;

namespace Aevatar.Agents.Cognitive.Researching.Round;

// ============================================================
//  ResearchingRoundServices (MVP)
//
//  中文说明：
//  - 仅保留可复用的子步骤/解析逻辑
//  - 轮次编排交给 WorkflowRunExecutor + Step Modules
// ============================================================
public sealed partial class ResearchingRoundServices
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly ResearchingCore _core;
    private readonly ResearchingPivot _pivot;
    private readonly ResearchingMesh _mesh;
    private readonly ResearchingHost _host;
    private readonly LlmProviderGate _llmGate;

    internal IResearchingRuntime Runtime => _core.Runtime;

    public ResearchingRoundServices(
        ResearchingCore core,
        ResearchingPivot pivot,
        ResearchingMesh mesh,
        ResearchingHost host,
        LlmProviderGate llmGate)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
        _pivot = pivot ?? throw new ArgumentNullException(nameof(pivot));
        _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _llmGate = llmGate ?? throw new ArgumentNullException(nameof(llmGate));
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
