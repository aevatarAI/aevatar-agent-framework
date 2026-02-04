using Aevatar.Agents.Sessions.Abstractions.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.CognitiveMesh.Dsl.WorkflowCompiler;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCognitiveMeshWorkflowCompiler(this IServiceCollection services)
    {
        // Register as non-try-add so callers can override earlier defaults.
        services.AddSingleton<IWorkflowCompiler, CognitiveMeshWorkflowCompiler>();
        return services;
    }
}

