using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.Tests.Messages;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.AI.Tests;

[Collection("EnvVarNonParallel")]
public sealed class RoleAgentFactoryEventModuleTests
{
    [Fact]
    public async Task RoleAgentFactory_ShouldApplyEventModules_WithRoutes()
    {
        var tempHome = MakeTempHome();
        using var scope = new EnvScope(tempHome);

        var role = "demo_role";
        var eventType = "aevatar.agents.ai.tests.TestEvent";
        WriteAgentYaml(role, $$"""
id: "{{role}}"
name: "Demo Role"
extensions:
  event_modules: "routed_module,all_module"
  event_routes: |
    - when: event.type == "{{eventType}}"
      to: routed_module
""");

        var moduleFactory = new CountingModuleFactory();
        var registry = new GlobalAgentYamlRegistry(NullLogger<GlobalAgentYamlRegistry>.Instance);
        var agentFactory = new SimpleAgentFactory();
        var roleFactory = new RoleAgentFactory(
            agentFactory,
            registry,
            new[] { moduleFactory },
            new DefaultEventRouteEvaluator(),
            NullLogger<RoleAgentFactory>.Instance);

        var agent = await roleFactory.CreateAsync(role);
        var envelope = BuildEnvelope();

        await InvokeHandleEventEnvelopeAsync(agent, envelope);

        moduleFactory.Get("routed_module").Count.ShouldBe(1);
        moduleFactory.Get("all_module").Count.ShouldBe(0);
    }

    [Fact]
    public async Task RoleAgentFactory_ShouldApplyEventModules_WithoutRoutes()
    {
        var tempHome = MakeTempHome();
        using var scope = new EnvScope(tempHome);

        var role = "demo_role_no_routes";
        WriteAgentYaml(role, """
id: "demo_role_no_routes"
name: "Demo Role"
extensions:
  event_modules: "first_module,second_module"
""");

        var moduleFactory = new CountingModuleFactory();
        var registry = new GlobalAgentYamlRegistry(NullLogger<GlobalAgentYamlRegistry>.Instance);
        var agentFactory = new SimpleAgentFactory();
        var roleFactory = new RoleAgentFactory(
            agentFactory,
            registry,
            new[] { moduleFactory },
            new DefaultEventRouteEvaluator(),
            NullLogger<RoleAgentFactory>.Instance);

        var agent = await roleFactory.CreateAsync(role);
        var envelope = BuildEnvelope();

        await InvokeHandleEventEnvelopeAsync(agent, envelope);

        moduleFactory.Get("first_module").Count.ShouldBe(1);
        moduleFactory.Get("second_module").Count.ShouldBe(1);
    }

    private static EventEnvelope BuildEnvelope()
    {
        return new EventEnvelope
        {
            Id = Guid.NewGuid().ToString("N"),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Payload = Any.Pack(new TestEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                Content = "hello",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            })
        };
    }

    private static async Task InvokeHandleEventEnvelopeAsync(RoleAIGAgent agent, EventEnvelope envelope)
    {
        var method = typeof(RoleAIGAgent)
            .GetMethod("HandleEventEnvelope", BindingFlags.Instance | BindingFlags.NonPublic);
        method.ShouldNotBeNull();
        var task = method!.Invoke(agent, new object[] { envelope }) as Task;
        if (task == null)
            throw new InvalidOperationException("HandleEventEnvelope did not return a Task.");
        await task;
    }

    private static void WriteAgentYaml(string role, string content)
    {
        var key = GlobalAgentYamlRegistry.NormalizeRoleKey(role);
        var path = AgentYamlConfigLoader.GetConfigFilePath(key);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
    }

    private static string MakeTempHome()
        => Path.Combine(Path.GetTempPath(), "aevatar-ai-tests", Guid.NewGuid().ToString("N"));

    private sealed class EnvScope : IDisposable
    {
        private readonly string _home;
        private readonly string? _oldHome;
        private readonly string? _oldUserProfile;

        public EnvScope(string home)
        {
            _home = home;
            _oldHome = Environment.GetEnvironmentVariable("HOME");
            _oldUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");

            Directory.CreateDirectory(_home);
            Environment.SetEnvironmentVariable("HOME", _home);
            Environment.SetEnvironmentVariable("USERPROFILE", _home);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("HOME", _oldHome);
            Environment.SetEnvironmentVariable("USERPROFILE", _oldUserProfile);

            try
            {
                if (Directory.Exists(_home))
                    Directory.Delete(_home, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    private sealed class SimpleAgentFactory : IGAgentFactory
    {
        public IGAgent CreateGAgent(string id, global::System.Type agentType, CancellationToken ct = default)
            => (IGAgent)(Activator.CreateInstance(agentType) ?? throw new InvalidOperationException());

        public TAgent CreateGAgent<TAgent>(string id, CancellationToken ct = default)
            where TAgent : IGAgent
            => (TAgent)CreateGAgent(id, typeof(TAgent), ct);

        public TAgent CreateGAgent<TAgent>(CancellationToken ct = default)
            where TAgent : IGAgent
            => (TAgent)CreateGAgent(string.Empty, typeof(TAgent), ct);
    }

    private sealed class CountingModuleFactory : IEventModuleFactory
    {
        private readonly ConcurrentDictionary<string, CountingModule> _modules =
            new(StringComparer.OrdinalIgnoreCase);

        public bool TryCreate(string name, out IEventModule module)
        {
            var key = (name ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                module = null!;
                return false;
            }

            var created = _modules.GetOrAdd(key, k => new CountingModule(k));
            module = created;
            return true;
        }

        public CountingModule Get(string name)
            => _modules[name];
    }

    private sealed class CountingModule : IEventModule
    {
        private int _count;

        public CountingModule(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public int Priority => 0;
        public int Count => Volatile.Read(ref _count);

        public bool CanHandle(EventEnvelope envelope) => true;

        public Task HandleAsync(EventEnvelope envelope, IEventModuleHost host, CancellationToken ct)
        {
            Interlocked.Increment(ref _count);
            return Task.CompletedTask;
        }
    }
}
