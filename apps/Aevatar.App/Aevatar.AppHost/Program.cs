// =========================================================================
//  Aevatar.AppHost - Aspire Orchestration for Aevatar Distributed System
// =========================================================================
//  Prerequisites:
//    cd apps/Aevatar.App/Aevatar.AppHost && docker-compose up -d
//
//  Services:
//    - Orleans Silo: Agent runtime with MongoDB persistence + Kafka streaming
//    - HTTP API Host: ABP-based REST API endpoints
//    - Auth Server: OpenIddict + ABP Identity for authentication
// =========================================================================

var builder = DistributedApplication.CreateBuilder(args);

Console.WriteLine("🚀 Aspire AppHost - Aevatar Agent Platform");
Console.WriteLine("===========================================");

// -------------------------------------------------------------------------
// External Infrastructure (managed by docker-compose)
// -------------------------------------------------------------------------
// Run `docker-compose up -d` before starting this AppHost
Console.WriteLine("✅ Infrastructure: MongoDB + Redis + Kafka (external via docker-compose)");

// -------------------------------------------------------------------------
// Orleans Silo - Agent Runtime
// -------------------------------------------------------------------------
// The Silo hosts Orleans Grains for distributed agent execution.
// It connects to MongoDB for state persistence and Kafka for streaming.
// Configuration is read from appsettings.json (already configured)
var silo = builder.AddProject<Projects.Aevatar_Silo>("silo");

Console.WriteLine("✅ Silo: Orleans Agent Runtime");

// -------------------------------------------------------------------------
// Auth Server - OpenIddict Authentication
// -------------------------------------------------------------------------
// Provides OAuth2/OpenID Connect authentication using ABP Identity.
var authServer = builder.AddProject<Projects.Aevatar_AuthServer>("auth-server")
    .WaitFor(silo);

Console.WriteLine("✅ Auth Server: OpenIddict + ABP Identity");

// -------------------------------------------------------------------------
// HTTP API Host - REST API Service
// -------------------------------------------------------------------------
// ABP-based API that exposes business logic and agent interactions.
var apiHost = builder.AddProject<Projects.Aevatar_App_HttpApi_Host>("api-host")
    .WaitFor(silo)
    .WaitFor(authServer);

Console.WriteLine("✅ API Host: ABP REST API");

Console.WriteLine("");
Console.WriteLine("📊 启动后查看 Aspire Dashboard:");
Console.WriteLine("   - 分布式追踪：跟踪请求流经 API → Silo → MongoDB");
Console.WriteLine("   - 结构化日志：按服务/组件过滤");
Console.WriteLine("   - 健康检查：所有服务实时状态");
Console.WriteLine("   - 指标监控：性能与资源使用");
Console.WriteLine("");
Console.WriteLine("📋 基础设施监控:");
Console.WriteLine("   - Kafka UI: http://localhost:8082");
Console.WriteLine("   - MongoDB:  mongodb://localhost:27017");
Console.WriteLine("   - Redis:    localhost:6379");
Console.WriteLine("   - Kafka:    localhost:29092");
Console.WriteLine("");

var app = builder.Build();
await app.RunAsync();
