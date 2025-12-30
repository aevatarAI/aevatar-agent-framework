using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Orleans.MongoDB;
using Aevatar.Agents.Persistence.MongoDB;
using Aevatar.Agents.Persistence.MongoDB.GAgent;
using Aevatar.Agents.Runtime.Orleans.EventSourcing;
using Aevatar.Agents.Runtime.Orleans.MongoDB;
using Demo.Api;
using Demo.Api.Extensions;
using MongoDB.Driver;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers.MongoDB.Configuration;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

var builder = WebApplication.CreateBuilder(args);

// 添加 Aspire 可观察性
builder.AddServiceDefaults();  // Aspire 默认配置
builder.AddAevatarObservability();  // Aevatar Agents 可观察性

// 读取运行时配置
var runtimeOptions = builder.Configuration
    .GetSection(AgentRuntimeOptions.SectionName)
    .Get<AgentRuntimeOptions>() ?? new AgentRuntimeOptions();

// MongoDB 配置
var mongoConnectionString = builder.Configuration.GetConnectionString("MongoDB") 
    ?? "mongodb://localhost:27017";
var databaseName = builder.Configuration.GetSection("Storage")
    .GetValue("DatabaseName", "DemoAgents");

// 配置 BSON 序列化
MongoDBServiceCollectionExtensions.ConfigureBsonSerializers();

// 如果使用Orleans运行时，配置Orleans Host
if (runtimeOptions.RuntimeType == AgentRuntimeType.Orleans)
{
    builder.Host.UseOrleans((context, siloBuilder) =>
    {
        var orleansOptions = runtimeOptions.Orleans;
        
        // 1. Configure MongoDB Client (Shared) - 必须在其他 MongoDB 配置之前
        siloBuilder.UseMongoDBClient(mongoConnectionString);
        
        if (orleansOptions.UseLocalhostClustering)
        {
            // 开发环境：本地集群
            siloBuilder.UseLocalhostClustering(
                siloPort: orleansOptions.SiloPort,
                gatewayPort: orleansOptions.GatewayPort);
        }
        else
        {
            // 生产环境：MongoDB Clustering
            siloBuilder.UseMongoDBClustering(options =>
            {
                options.DatabaseName = databaseName;
                options.Strategy = MongoDBMembershipStrategy.SingleDocument;
                options.CollectionPrefix = "OrleansDemo";
            });
            
            siloBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = orleansOptions.ClusterId;
                options.ServiceId = orleansOptions.ServiceId;
            });
        }
        
        // 2. Configure Storage (Using shared MongoDB client)
        // Default Storage
        siloBuilder.AddMongoDBGrainStorage("Default", options =>
        {
            options.DatabaseName = databaseName;
            options.CollectionPrefix = "OrleansDemo";
        });
        
        // PubSubStore for Streaming
        siloBuilder.AddMongoDBGrainStorage("PubSubStore", options =>
        {
            options.DatabaseName = databaseName;
            options.CollectionPrefix = "StreamStorage";
        });
        
        // 3. Configure Streaming (Memory Streams for simplicity)
        siloBuilder.AddMemoryStreams("AevatarAgents", streamConfig =>
        {
            streamConfig.ConfigureStreamPubSub(StreamPubSubType.ExplicitGrainBasedAndImplicit);
        });
        
        // 4. 注册持久化服务到 Orleans Silo 内部 (关键：Grain 使用的是 Silo 的 ServiceProvider)
        siloBuilder.ConfigureServices(services =>
        {
            // MongoDB Client 和 Database
            services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));
            services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
            
            // StateStore (用于 Agent 业务 State 持久化)
            // 使用开放泛型注册 MongoDBStateStore<>，这样每种 TState 类型都会有独立的集合
            services.AddSingleton(typeof(Aevatar.Agents.Abstractions.Persistence.IStateStore<>), typeof(MongoDBStateStore<>));
            
            // Event Repository (MongoDB)
            services.AddSingleton<IEventRepository>(sp =>
            {
                var client = sp.GetRequiredService<IMongoClient>();
                var logger = sp.GetRequiredService<ILogger<MongoEventRepository>>();
                return new MongoEventRepository(client, new MongoEventRepositoryOptions
                {
                    DatabaseName = databaseName,
                    CollectionName = "agent_events",
                    EnableDetailedLogging = true
                }, logger);
            });

            // Event Store (Orleans)
            services.AddSingleton<IEventStore>(sp =>
            {
                var eventRepository = sp.GetRequiredService<IEventRepository>();
                var logger = sp.GetRequiredService<ILogger<OrleansEventStore>>();
                return new OrleansEventStore(eventRepository, logger);
            });
        });
        
        Console.WriteLine($"🌐 Orleans Silo 配置完成");
        Console.WriteLine($"   ClusterId: {orleansOptions.ClusterId}");
        Console.WriteLine($"   ServiceId: {orleansOptions.ServiceId}");
        Console.WriteLine($"   SiloPort: {orleansOptions.SiloPort}");
        Console.WriteLine($"   GatewayPort: {orleansOptions.GatewayPort}");
        Console.WriteLine($"   MongoDB: {mongoConnectionString}");
        Console.WriteLine($"   Database: {databaseName}");
    });
}

// 注册 MongoDB 服务
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));

// 添加服务
builder.Services.AddControllers();
// Swagger disabled due to version compatibility
// builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen();

// 添加Agent运行时（基于配置自动选择）
builder.Services.AddAgentRuntime(builder.Configuration);

builder.Services.AddGAgentActorFactoryProvider();

var app = builder.Build();

// 配置HTTP管道
// Swagger disabled due to version compatibility
// if (app.Environment.IsDevelopment())
// {
//     app.UseSwagger();
//     app.UseSwaggerUI();
// }

app.UseHttpsRedirection();
app.UseAuthorization();

// 添加 Prometheus metrics 端点
app.UsePrometheusMetrics();

app.MapControllers();
app.MapDefaultEndpoints();  // Aspire 健康检查端点

// 显示当前使用的运行时
Console.WriteLine($"🚀 Agent Framework Demo API");
Console.WriteLine($"📦 运行时类型: {runtimeOptions.RuntimeType}");
Console.WriteLine($"🌐 Swagger UI: https://localhost:7001/swagger");

app.Run();

