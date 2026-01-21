var builder = DistributedApplication.CreateBuilder(args);

// ============ Configuration ============

var runtimeType = builder.Configuration["AgentRuntime:RuntimeType"] ?? "Local";

// ============ Banner ============

Console.WriteLine();
Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║     🚀 WEEX AI Trading System - Aspire AppHost                ║");
Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Runtime: {runtimeType,-20}                         ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ============ Services ============

IResourceBuilder<ProjectResource> tradingApi;

switch (runtimeType.ToLower())
{
    case "local":
        Console.WriteLine("✅ Using Local runtime (single-machine in-memory mode)");
        tradingApi = builder.AddProject<Projects.Aevatar_Trade_Api>("trading-api")
            .WithEnvironment("AgentRuntime__RuntimeType", "Local")
            .WithExternalHttpEndpoints();
        break;

    case "orleans":
        Console.WriteLine("✅ Using Orleans runtime (distributed mode)");
        tradingApi = builder.AddProject<Projects.Aevatar_Trade_Api>("trading-api")
            .WithEnvironment("AgentRuntime__RuntimeType", "Orleans")
            .WithExternalHttpEndpoints();
        break;

    default:
        throw new InvalidOperationException($"Unsupported runtime type: {runtimeType}");
}

// ============ Frontend (Vite) ============
//
// 职责划分：
// - boot.sh 负责 PATH 补强 + npm install（确保环境就绪）
// - AppHost 信任环境，直接用 "npm"（继承 boot.sh 导出的 PATH）
//
// 端口约定：
// - Frontend: 5173 (Vite dev server)
// - Trading API: 7100 (launchSettings.json)
//
var frontendDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "frontend"));

var frontend = builder.AddExecutable("trade-frontend", "npm", frontendDir, "run", "dev")
    .WithHttpEndpoint(targetPort: 5173, port: 5173, name: "http", env: null, isProxied: false)
    .WithEnvironment("TRADE_API_PROXY_TARGET", tradingApi.GetEndpoint("http"))
    .WithExternalHttpEndpoints();

// ============ Build & Run ============

var app = builder.Build();
await app.RunAsync();
