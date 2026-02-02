using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Primitives;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe;

namespace VibeResearching.Api.Vibe.Steps;

internal sealed class VibeLibrarianEffectsStepModule : VibeStepModuleBase
{
    private readonly VibeWorkflowParsing _parsing;
    private readonly ResearchSessionManager _sessions;
    private readonly ILogger<VibeLibrarianEffectsStepModule> _logger;

    public VibeLibrarianEffectsStepModule(
        VibeWorkflowParsing parsing,
        ResearchSessionManager sessions,
        ILogger<VibeLibrarianEffectsStepModule> logger)
    {
        _parsing = parsing ?? throw new ArgumentNullException(nameof(parsing));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override string Name => "vibe_librarian_effects";
    public override string StepType => "vibe_librarian_effects";

    public override async Task<PrimitiveResult> ExecuteAsync(
        IWorkflowCoordinatorRuntime coordinator,
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem,
        CancellationToken ct)
    {
        var sessionId = ResolveSessionId(coordinator);
        if (string.IsNullOrWhiteSpace(sessionId))
            return PrimitiveResult.Fail("vibe_librarian_effects requires session_id");

        var sourceKey = ResolveStringParameter(step.Parameters, "source", "librarian");
        var raw = ResolveStringVar(coordinator, sourceKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return PrimitiveResult.Ok(new Dictionary<string, object>
            {
                ["facts_written"] = new List<string>(),
                ["axioms"] = new List<Dictionary<string, object?>>()
            });
        }

        var actions = VibeWorkflowParsing.TryParseLibrarianActions(raw);
        if (actions == null)
        {
            return PrimitiveResult.Ok(new Dictionary<string, object>
            {
                ["facts_written"] = new List<string>(),
                ["axioms"] = new List<Dictionary<string, object?>>()
            });
        }

        var session = _sessions.GetOrCreate(sessionId);
        var factsWritten = await _parsing.TryWriteFactsAsync(session, actions.FactsWrite, ct);

        var axioms = actions.AxiomsForDag
            .Select(a => new Dictionary<string, object?>
            {
                ["id"] = a.Id ?? string.Empty,
                ["label"] = a.Label ?? string.Empty,
                ["citation"] = a.Citation ?? string.Empty,
                ["source_path"] = a.SourcePath ?? string.Empty,
                ["tags"] = a.Tags ?? new Dictionary<string, string?>()
            })
            .ToList();

        _logger.LogDebug("Librarian effects applied: facts={Facts}, axioms={Axioms}",
            factsWritten.Count, axioms.Count);

        return PrimitiveResult.Ok(new Dictionary<string, object>
        {
            ["facts_written"] = factsWritten,
            ["axioms"] = axioms
        });
    }
}
