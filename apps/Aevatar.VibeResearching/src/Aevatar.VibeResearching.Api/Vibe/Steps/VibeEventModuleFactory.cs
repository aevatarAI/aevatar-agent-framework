using Aevatar.Agents.AI.Core;
using Microsoft.Extensions.DependencyInjection;

namespace VibeResearching.Api.Vibe.Steps;

internal sealed class VibeEventModuleFactory : IEventModuleFactory
{
    private readonly IServiceProvider _services;

    public VibeEventModuleFactory(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public bool TryCreate(string name, out IEventModule module)
    {
        module = name switch
        {
            "vibe_context" => ActivatorUtilities.CreateInstance<VibeContextStepModule>(_services),
            "vibe_librarian_effects" => ActivatorUtilities.CreateInstance<VibeLibrarianEffectsStepModule>(_services),
            "vibe_dag_apply" => ActivatorUtilities.CreateInstance<VibeDagApplyStepModule>(_services),
            "vibe_delivery_apply" => ActivatorUtilities.CreateInstance<VibeDeliveryApplyStepModule>(_services),
            "vibe_trace_append" => ActivatorUtilities.CreateInstance<VibeTraceAppendStepModule>(_services),
            "vibe_pivot_detection" => ActivatorUtilities.CreateInstance<VibePivotDetectionStepModule>(_services),
            _ => null!
        };

        return module != null;
    }
}
