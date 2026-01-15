using System.Threading;
using System.Threading.Tasks;
using Aevatar.CognitiveMesh.Dsl;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aevatar.CognitiveMesh;

/// <summary>
/// Placeholder background service that will eventually connect DSL blueprints to the Orleans runtime.
/// </summary>
internal sealed class MeshWorkbench : BackgroundService
{
    private readonly ILogger<MeshWorkbench> _logger;
    private readonly CognitiveDslCompiler _compiler;

    public MeshWorkbench(ILogger<MeshWorkbench> logger, CognitiveDslCompiler compiler)
    {
        _logger = logger;
        _compiler = compiler;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cognitive Mesh runtime bootstrapped. Awaiting DSL submissions...");
        _logger.LogInformation("Use Cognitive Mesh AppHost to feed DSL payloads and attach Orleans silos.");

        // TODO: Wire Orleans actor runtime + Agent providers.
        return Task.CompletedTask;
    }
}

