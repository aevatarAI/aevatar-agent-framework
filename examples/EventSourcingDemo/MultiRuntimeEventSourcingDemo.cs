using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Runtime.Orleans;
using Aevatar.Agents.Runtime.Orleans.Extensions;
using Aevatar.Agents.Plugins.MassTransit;
using Aevatar.Agents.Plugins.MassTransit.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;

namespace EventSourcingDemo;

/// <summary>
/// 多运行时 EventSourcing 演示
/// 使用新的 WithEventSourcingAsync 扩展方法
/// </summary>
public static class MultiRuntimeEventSourcingDemo
{
    public static async Task RunAsync()
    {
        Console.WriteLine("\n\n");
        Console.WriteLine("🌌 ═══════════════════════════════════════════════════");
        Console.WriteLine("   Multi-Runtime EventSourcing Demo");
        Console.WriteLine("   展示 EventSourcing 在不同运行时下的统一API");
        Console.WriteLine("═══════════════════════════════════════════════════\n");
        
        // 配置依赖注入
        var services = ConfigureServices();
        var serviceProvider = services.BuildServiceProvider();
        
        // 创建共享的 EventStore（所有运行时共享同一个存储）
        var sharedEventStore = serviceProvider.GetRequiredService<InMemoryEventStore>();
        
        // 1. Local 运行时演示
        await DemoLocalRuntime(sharedEventStore, serviceProvider);
        
        // 2. Orleans 运行时演示
        await DemoOrleansRuntime();
        
        Console.WriteLine("\n✅ Multi-Runtime EventSourcing Demo 完成！");
        Console.WriteLine("🌟 所有运行时都使用统一的 EventSourcing API！");
    }
    
    /// <summary>
    /// Local 运行时演示（使用新 API）
    /// </summary>
    private static async Task DemoLocalRuntime(InMemoryEventStore eventStore, IServiceProvider serviceProvider)
    {
        Console.WriteLine("📍 Local Runtime EventSourcing");
        Console.WriteLine("════════════════════════════════════════");
        
        var agentId = Guid.NewGuid().ToString();
        Console.WriteLine($"Agent ID: {agentId:N}");
        
        // 创建工厂
        var logger = serviceProvider.GetRequiredService<ILogger<LocalGAgentActorFactory>>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var factory = new LocalGAgentActorFactory(serviceProvider, logger, loggerFactory);
        
        // ✅ 场景1：自动 EventSourcing 注入
        Console.WriteLine("\n⚡ 场景1：AIGAgentFactory 自动注入 EventStore");
        Console.WriteLine("───────────────────────────────────────────────");
        
        // 创建 Actor（EventStore 已通过 DI 自动注入）
        var actor = await factory.CreateGAgentActorAsync<BankAccountAgent>(agentId);
        
        var agent = actor.GetAgent() as BankAccountAgent;
        if (agent == null)
        {
            Console.WriteLine("  ❌ 无法获取 Agent 实例");
            return;
        }
        
        Console.WriteLine("  ✓ Actor 创建成功");
        Console.WriteLine("  ✓ EventSourcing 自动启用");
        
        // 执行交易
        await agent.CreateAccountAsync("Local User", 1000);
        await agent.DepositAsync(500, "Salary");
        await agent.WithdrawAsync(200, "Shopping");
        
        Console.WriteLine($"\n  💵 Balance: ${agent.GetState().Balance:F2}");
        Console.WriteLine($"  📈 Version: v{agent.GetCurrentVersion()}");
        Console.WriteLine($"  🔢 Transactions: {agent.GetState().TransactionCount}");
        
        // ✅ 场景1.5：类型安全代理调用（Local Runtime 直接调用，无 RPC 开销）
        Console.WriteLine("\n⚡ 场景1.5：类型安全代理调用");
        Console.WriteLine("───────────────────────────────────────────────");
        
        try
        {
            // ✨ 使用类型安全代理（Local Runtime 会直接调用，无序列化开销）
            var bankAgent = actor.As<IBankAccountAgent>();
            Console.WriteLine("  ✓ 创建代理: actor.As<IBankAccountAgent>()");
            Console.WriteLine("  ✓ Local Runtime: 直接调用 Agent 方法（零开销）");
            
            var balance = await bankAgent.GetBalanceAsync();
            var holder = await bankAgent.GetAccountHolderAsync();
            var txCount = await bankAgent.GetTransactionCountAsync();
            var version = await bankAgent.GetCurrentVersionAsync();
            var summary = await bankAgent.GetAccountSummaryAsync();
            
            Console.WriteLine($"  ✅ bankAgent.GetBalanceAsync(): ${balance:F2}");
            Console.WriteLine($"  ✅ bankAgent.GetAccountHolderAsync(): {holder}");
            Console.WriteLine($"  ✅ bankAgent.GetTransactionCountAsync(): {txCount}");
            Console.WriteLine($"  ✅ bankAgent.GetCurrentVersionAsync(): v{version}");
            Console.WriteLine($"  ✅ bankAgent.GetAccountSummaryAsync():");
            Console.WriteLine($"     - Holder: {summary.AccountHolder}");
            Console.WriteLine($"     - Balance: ${summary.Balance:F2}");
            Console.WriteLine($"     - Transactions: {summary.TransactionCount}");
            Console.WriteLine($"     - Version: v{summary.Version}");
            
            // Verify results match direct access
            if (Math.Abs(balance - agent.GetState().Balance) < 0.01 &&
                holder == agent.GetState().AccountHolder &&
                txCount == agent.GetState().TransactionCount &&
                version == agent.GetCurrentVersion())
            {
                Console.WriteLine($"\n  🎉 代理调用验证成功！Local Runtime 直接调用无开销！");
            }
            else
            {
                Console.WriteLine($"\n  ⚠️ 代理调用结果与直接访问不匹配");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ RPC 调用失败: {ex.Message}");
            Console.WriteLine($"     {ex.GetType().Name}: {ex.StackTrace}");
        }
        
        // ✅ 场景2：批量交易演示
        Console.WriteLine("\n⚡ 场景2：批量交易（展示批量提交优势）");
        Console.WriteLine("───────────────────────────────────────────────");
        
        var batchTransactions = new[]
        {
            ("deposit", 300m, "Bonus"),
            ("deposit", 100m, "Refund"),
            ("withdraw", 50m, "Coffee")
        };
        
        await agent.BatchTransactionsAsync(batchTransactions);
        
        Console.WriteLine($"  ✓ Batch completed (3 transactions in 1 commit)");
        Console.WriteLine($"  💵 New Balance: ${agent.GetState().Balance:F2}");
        Console.WriteLine($"  📈 New Version: v{agent.GetCurrentVersion()}");
        
        // ✅ 场景3：崩溃恢复
        Console.WriteLine("\n⚡ 场景3：崩溃恢复（自动事件重放）");
        Console.WriteLine("───────────────────────────────────────────────");
        
        // 停止原 Actor
        await actor.DeactivateAsync();
        Console.WriteLine("  ✓ 原 Actor 已停止");
        
        // 检查事件
        var events = await eventStore.GetEventsAsync(agentId);
        Console.WriteLine($"  📝 EventStore 中的事件数: {events.Count}");
        
        // 创建新 Actor（EventStore 会自动注入并重放事件）
        var newActor = await factory.CreateGAgentActorAsync<BankAccountAgent>(agentId);
        
        var recoveredAgent = newActor.GetAgent() as BankAccountAgent;
        if (recoveredAgent != null)
        {
            Console.WriteLine($"\n  ✅ 状态完美恢复:");
            Console.WriteLine($"     Balance: ${recoveredAgent.GetState().Balance:F2}");
            Console.WriteLine($"     Version: v{recoveredAgent.GetCurrentVersion()}");
            Console.WriteLine($"     Holder: {recoveredAgent.GetState().AccountHolder}");
            Console.WriteLine($"     Transactions: {recoveredAgent.GetState().TransactionCount}");
            
            // 验证
            if (recoveredAgent.GetState().Balance == 1650.0 && 
                recoveredAgent.GetCurrentVersion() == 7)  // 1 create + 2 individual + 3 batch + 0 (batch is 1 commit)
            {
                Console.WriteLine($"\n  🎉 Local Runtime EventSourcing 验证成功!");
            }
            
            // ✅ 场景3.5：恢复后 RPC 调用测试
            Console.WriteLine("\n⚡ 场景3.5：恢复后 RPC 调用测试");
            Console.WriteLine("───────────────────────────────────────────────");
            
            try
            {
                var bankAgent = newActor.As<IBankAccountAgent>();
                var recoveredBalance = await bankAgent.GetBalanceAsync();
                var recoveredSummary = await bankAgent.GetAccountSummaryAsync();
                
                Console.WriteLine($"  ✅ 恢复后 RPC GetBalanceAsync: ${recoveredBalance:F2}");
                Console.WriteLine($"  ✅ 恢复后 RPC GetAccountSummaryAsync:");
                Console.WriteLine($"     - Balance: ${recoveredSummary.Balance:F2}");
                Console.WriteLine($"     - Version: v{recoveredSummary.Version}");
                
                if (Math.Abs(recoveredBalance - recoveredAgent.GetState().Balance) < 0.01 &&
                    recoveredSummary.Version == recoveredAgent.GetCurrentVersion())
                {
                    Console.WriteLine($"\n  🎉 恢复后 RPC 调用验证成功！");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ 恢复后 RPC 调用失败: {ex.Message}");
            }
        }
        
        // ✅ 场景4：展示事件元数据
        Console.WriteLine("\n⚡ 场景4：事件元数据（用于审计和调试）");
        Console.WriteLine("───────────────────────────────────────────────");
        
        // 获取最近的几个事件
        var recentEvents = await eventStore.GetEventsAsync(agentId, fromVersion: 1, maxCount: 5);
        Console.WriteLine($"  📝 最近 {recentEvents.Count} 个事件:");
        
        foreach (var evt in recentEvents)
        {
            var eventName = evt.EventType.Split('.').Last();
            var metadataStr = evt.Metadata.Any() 
                ? $" | Metadata: {string.Join(", ", evt.Metadata.Select(kv => $"{kv.Key}={kv.Value}"))}"
                : "";
            Console.WriteLine($"     v{evt.Version}: {eventName,-20}{metadataStr}");
        }
        
        Console.WriteLine($"\n  ✅ Local Runtime 演示完成!");
    }
    
    /// <summary>
    /// Orleans 运行时演示（使用 MassTransit Stream）
    /// </summary>
    private static async Task DemoOrleansRuntime()
    {
        Console.WriteLine("\n\n📍 Orleans Runtime EventSourcing + RPC (MassTransit Stream)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        
        // 启动内嵌 Orleans Silo
        Console.WriteLine("🚀 启动 Orleans Silo (with MassTransit Stream)...");
        
        // Build MassTransit configuration (InMemory for demo)
        var configDict = new Dictionary<string, string?>
        {
            {"MessageStream:Provider", "MassTransit"},
            {"MessageStream:Runtime:Orleans", "MassTransit"},
            {"MassTransit:Stream:TopicPrefix", "agent-events"},
            {"MassTransit:Stream:TransportType", "InMemory"},
            {"MassTransit:Stream:RuntimeName", "Orleans"},
            {"MassTransit:Stream:Consumer:Enabled", "true"},
            {"MassTransit:Stream:Consumer:DispatchHandler", "GrainHandler"},
            {"MassTransit:Stream:Consumer:IncludeTopicPrefix", "true"},
            {"MassTransit:Stream:Producer:Enabled", "true"}
        };
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();
        
        var host = Host.CreateDefaultBuilder()
            .UseOrleans((context, siloBuilder) =>
            {
                siloBuilder.UseLocalhostClustering(
                    siloPort: 11112,  // Use different port to avoid conflicts
                    gatewayPort: 30001);
                siloBuilder.AddMemoryGrainStorage("Default");
                
                // ⚠️ IMPORTANT: Do NOT configure Orleans Stream when using MassTransit
                // MassTransit completely replaces Orleans Streaming
                // siloBuilder.AddMemoryStreams("Default");  // REMOVED
                
                siloBuilder.Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = "eventsourcing-demo";
                    options.ServiceId = "bank-demo";
                });
                
                // Configure serializer
                siloBuilder.ConfigureServices(services =>
                {
                    services.AddSerializer(serializerBuilder =>
                    {
                        serializerBuilder.AddProtobufSerializer();
                    });
                    
                    // Register OrleansStreamFactory for unified stream creation
                    services.AddSingleton<Aevatar.Agents.Runtime.Orleans.Stream.OrleansStreamFactory>();
                });
            })
            .ConfigureServices(services =>
            {
                services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
                
                // Configure MessageStreamProviderOptions
                services.Configure<MessageStreamProviderOptions>(configuration.GetSection("MessageStream"));
                
                // EventStore - required for state updates
                services.AddSingleton<InMemoryEventStore>();
                services.AddSingleton<IEventStore>(p => p.GetRequiredService<InMemoryEventStore>());
                services.AddSingleton<Aevatar.Agents.Abstractions.EventSourcing.IEventStore>(
                    p => p.GetRequiredService<InMemoryEventStore>());
                
                // Register Agent Factory
                services.AddSingleton<IGAgentFactory, AIGAgentFactory>();
                services.AddGAgentActorFactoryProvider();
                
                // Register Orleans Runtime services
                services.AddAevatarOrleansRuntime();
                
                // Register MassTransit Stream Plugin (InMemory mode for demo)
                services.AddMassTransitStreamPlugin(configuration);
            })
            .Build();
        
        await host.StartAsync();
        Console.WriteLine("✅ Orleans Silo 已启动 (MassTransit Stream enabled)\n");
        
        try
        {
            // Get cluster client
            var clusterClient = host.Services.GetRequiredService<IClusterClient>();
            
            // Setup Actor Factory with MassTransit Stream Client
            var clientServices = new ServiceCollection();
            clientServices.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
            clientServices.AddSingleton(clusterClient);
            clientServices.AddSingleton<IGrainFactory>(clusterClient);
            clientServices.AddSingleton<IClusterClient>(clusterClient);
            
            // Configure MessageStreamProviderOptions for MassTransit
            clientServices.Configure<MessageStreamProviderOptions>(options =>
            {
                options.Provider = "MassTransit";
                options.Runtime["Orleans"] = "MassTransit";
            });
            
            // Add MassTransit Stream Client (Producer-only mode)
            clientServices.AddMassTransitStreamClient(configuration);
            
            // Add Orleans Runtime
            clientServices.AddAevatarAgentSystem(builder => builder.UseOrleansRuntime());
            
            var clientServiceProvider = clientServices.BuildServiceProvider();
            
            // Start MassTransit hosted services
            Console.WriteLine("   • Starting MassTransit services...");
            var hostedServices = clientServiceProvider.GetServices<IHostedService>();
            foreach (var hostedService in hostedServices)
            {
                await hostedService.StartAsync(default);
            }
            
            // Warm up Kafka producer (if using Kafka, skip for InMemory)
            var streamProvider = clientServiceProvider.GetService<MassTransitMessageStreamProvider>();
            if (streamProvider != null)
            {
                await streamProvider.WarmupAsync();
            }
            Console.WriteLine("   ✅ MassTransit services started\n");
            
            // Get factory
            var factory = clientServiceProvider.GetRequiredService<IGAgentActorFactory>();
            
            var agentId = Guid.NewGuid().ToString();
            Console.WriteLine($"Agent ID: {agentId:N}");
            
            // Create Orleans Actor
            Console.WriteLine("\n⚡ 场景5：Orleans Runtime RPC 调用 (MassTransit Stream)");
            Console.WriteLine("───────────────────────────────────────────────");
            
            var actor = await factory.CreateGAgentActorAsync<BankAccountAgent>(agentId);
            Console.WriteLine("  ✓ Orleans Actor 创建成功");
            Console.WriteLine("  ✓ MassTransit Stream 已配置");
            
            // ✨ 使用类型安全代理（Orleans Runtime 自动走 RPC）
            var bankAgent = actor.As<IBankAccountAgent>();
            Console.WriteLine("  ✓ 创建代理: actor.As<IBankAccountAgent>()");
            Console.WriteLine("  ✓ Orleans Runtime: 自动走 RPC（Protobuf 序列化）");
            Console.WriteLine("  ✓ MassTransit Stream: 事件通过 InMemory 传输");
            
            // 通过接口调用（类型安全，与 Local Runtime 一致的 API）
            Console.Write("  bankAgent.CreateAccountAsync(...)... ");
            await bankAgent.CreateAccountAsync("Orleans User", 2000m);
            Console.WriteLine("✅");
            
            Console.Write("  bankAgent.DepositAsync(800m, ...)... ");
            await bankAgent.DepositAsync(800m, "Orleans Deposit");
            Console.WriteLine("✅");
            
            Console.Write("  bankAgent.WithdrawAsync(300m, ...)... ");
            await bankAgent.WithdrawAsync(300m, "Orleans Withdraw");
            Console.WriteLine("✅");
            
            // 通过接口获取状态
            Console.Write("  bankAgent.GetBalanceAsync()... ");
            var balance = await bankAgent.GetBalanceAsync();
            Console.WriteLine($"✅ 返回: ${balance:F2}");
            
            Console.Write("  bankAgent.GetAccountHolderAsync()... ");
            var holder = await bankAgent.GetAccountHolderAsync();
            Console.WriteLine($"✅ 返回: {holder}");
            
            Console.Write("  bankAgent.GetTransactionCountAsync()... ");
            var txCount = await bankAgent.GetTransactionCountAsync();
            Console.WriteLine($"✅ 返回: {txCount}");
            
            Console.Write("  bankAgent.GetAccountSummaryAsync()... ");
            var summary = await bankAgent.GetAccountSummaryAsync();
            Console.WriteLine("✅");
            Console.WriteLine($"     - Holder: {summary.AccountHolder}");
            Console.WriteLine($"     - Balance: ${summary.Balance:F2}");
            Console.WriteLine($"     - Transactions: {summary.TransactionCount}");
            Console.WriteLine($"     - Version: v{summary.Version}");
            
            // 验证 (TransactionCount = 2: Deposit + Withdraw, CreateAccount sets it to 0)
            var expectedBalance = 2000.0 + 800.0 - 300.0; // 2500
            if (Math.Abs(balance - expectedBalance) < 0.01 && 
                holder == "Orleans User" && 
                txCount == 2)
            {
                Console.WriteLine($"\n  🎉 Orleans Runtime RPC 验证成功！");
                Console.WriteLine($"     预期余额: ${expectedBalance:F2}, 实际: ${balance:F2}");
                Console.WriteLine($"     交易数: {txCount}");
                Console.WriteLine($"     ✅ MassTransit Stream 工作正常！");
            }
            else
            {
                Console.WriteLine($"\n  ❌ Orleans Runtime RPC 验证失败");
                Console.WriteLine($"     预期余额: ${expectedBalance:F2}, 实际: ${balance:F2}");
                Console.WriteLine($"     预期交易数: 2, 实际: {txCount}");
            }
            
            // Cleanup
            await actor.DeactivateAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n  ❌ Orleans RPC 调用失败: {ex.Message}");
            Console.WriteLine($"     {ex.GetType().Name}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"     Inner: {ex.InnerException.Message}");
            }
            Console.WriteLine($"     {ex.StackTrace}");
        }
        finally
        {
            Console.WriteLine("\n🛑 停止 Orleans Silo...");
            await host.StopAsync();
            Console.WriteLine("✅ Orleans Silo 已停止");
        }
    }
    
    /// <summary>
    /// 配置服务
    /// </summary>
    private static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();
        
        // 日志
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        
        // Local Agent Runtime - 注册工厂提供者
        services.AddAevatarLocalRuntime();
        
        // EventStore - 注册为单例（所有运行时共享）
        services.AddSingleton<InMemoryEventStore>();
        services.AddSingleton<IEventStore>(
            provider => provider.GetRequiredService<InMemoryEventStore>());

        // 注册 AIGAgentFactory（会自动注入 EventStore）
        services.AddSingleton<IGAgentFactory, AIGAgentFactory>();
        
        return services;
    }
}
