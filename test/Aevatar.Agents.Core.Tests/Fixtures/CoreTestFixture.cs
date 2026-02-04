using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Core.Persistence;
using Aevatar.Agents.Core.Tests.EventPublisher;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Core.Tests.Fixtures;

/// <summary>
/// Test fixture for Core tests with dependency injection
/// </summary>
public class CoreTestFixture : IDisposable
{
    private IServiceProvider _serviceProvider;
    public IServiceProvider ServiceProvider => _serviceProvider;
    public TestEventPublisher EventPublisher { get; }
    public IGAgentFactory GAgentFactory => _serviceProvider.GetRequiredService<IGAgentFactory>();

    public CoreTestFixture()
    {
        var services = new ServiceCollection();
        ConfigureCoreServices(services);

        // Register TestEventPublisher as singleton so we can access it for assertions
        var eventPublisher = new TestEventPublisher();
        EventPublisher = eventPublisher;
        services.AddSingleton<IEventPublisher>(eventPublisher);
        services.AddSingleton<IGAgentFactory, DefaultGAgentFactory>();

        // Allow derived classes to add their services
        ConfigureAdditionalServices(services);

        // Build service provider
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Configure core services required by GAgentBase
    /// </summary>
    protected virtual void ConfigureCoreServices(IServiceCollection services)
    {
        // Register state stores
        services.AddSingleton(typeof(IStateStore<>), typeof(InMemoryStateStore<>));
        services.AddSingleton(typeof(IConfigStore<>), typeof(InMemoryConfigStore<>));
    }

    /// <summary>
    /// Override in derived classes to add additional services
    /// </summary>
    protected virtual void ConfigureAdditionalServices(IServiceCollection services)
    {
        // Override in derived classes
    }

    /// <summary>
    /// Update the service provider (for use by derived classes)
    /// </summary>
    protected void UpdateServiceProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public virtual void Dispose()
    {
        // Clear event publisher state
        EventPublisher?.Clear();
        
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}