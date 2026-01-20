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
var cognitiveMesh = builder.AddProject<Projects.Aevatar_CognitiveMesh>("cognitive-mesh")
    .WithHttpEndpoint(port: 5678, name: "http")
    .WithExternalHttpEndpoints();

switch (runtimeType.ToLower())
{
    case "local":
        Console.WriteLine("✅ Using Local runtime (single-machine in-memory mode)");
        tradingApi = builder.AddProject<Projects.Aevatar_Trade_Api>("trading-api")
            .WithEnvironment("AgentRuntime__RuntimeType", "Local")
            // Use CognitiveMesh as the coordinator "brain":
            // - It runs Cognitive DSL workflows (trade-cycle.yaml) and returns a decision JSON string.
            .WithEnvironment("DecisionEngine__Mode", "CognitiveMesh")
            .WithEnvironment("DecisionEngine__CognitiveMeshBaseUrl", cognitiveMesh.GetEndpoint("http"))
            .WithEnvironment("DecisionEngine__CognitiveMeshStrategy", "Cognitive")
            .WithEnvironment("DecisionEngine__CognitiveWorkflow", "trade-cycle")
            .WithEnvironment("DecisionEngine__TimeoutSeconds", "120")
            .WithExternalHttpEndpoints();
        break;

    case "orleans":
        Console.WriteLine("✅ Using Orleans runtime (distributed mode)");
        tradingApi = builder.AddProject<Projects.Aevatar_Trade_Api>("trading-api")
            .WithEnvironment("AgentRuntime__RuntimeType", "Orleans")
            .WithEnvironment("DecisionEngine__Mode", "CognitiveMesh")
            .WithEnvironment("DecisionEngine__CognitiveMeshBaseUrl", cognitiveMesh.GetEndpoint("http"))
            .WithEnvironment("DecisionEngine__CognitiveMeshStrategy", "Cognitive")
            .WithEnvironment("DecisionEngine__CognitiveWorkflow", "trade-cycle")
            .WithEnvironment("DecisionEngine__TimeoutSeconds", "120")
            .WithExternalHttpEndpoints();
        break;

    default:
        throw new InvalidOperationException($"Unsupported runtime type: {runtimeType}");
}

// ============ Frontend (Vite) ============
//
// 目的：让前端也由 Aspire Dashboard 管控，实现一键联调。
// 约束：尽量不引入额外 NuGet 依赖；优先使用 Aspire.Hosting.AppHost 自带的能力。
//
// 说明：
// - 前端 dev server 固定端口 5173（见 trade/frontend/vite.config.ts）
// - 前端通过 Vite proxy 转发 /api -> http://localhost:7100
//   因此我们把 trading-api 的 http 端口也 pin 到 7100，避免端口漂移造成联调失败。
//
// Frontend 源码在 apps/Aevatar.Trading/frontend/
var frontendDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "apps", "Aevatar.Trading", "frontend"));

// NOTE:
// - Aspire 启动的可执行进程不一定继承交互式 shell 的 PATH（尤其是 brew/nvm）。
// - 这里用一个极小的“探测列表”兜底：优先使用常见 npm 路径，最后退回 "npm"。
var npmCandidates = new[]
{
    "/opt/homebrew/bin/npm", // macOS (Apple Silicon) homebrew
    "/usr/local/bin/npm",    // macOS (Intel) homebrew
    "/usr/bin/npm"           // system
};

var npm = npmCandidates.FirstOrDefault(File.Exists) ?? "npm";

var frontend = builder.AddExecutable("trade-frontend", npm, frontendDir, "run", "dev")
    // 对可执行资源：targetPort 必须是“进程实际监听的端口”，否则 DCP 无法生成 endpoint（Dashboard 也不会显示 URL）。
    .WithHttpEndpoint(targetPort: 5173, port: 5173, name: "http", env: null, isProxied: false)
    // 把 Trading API 的真实地址注入给 Vite（用于 dev proxy），避免硬编码端口/协议导致 socket hang up。
    .WithEnvironment("TRADE_API_PROXY_TARGET", tradingApi.GetEndpoint("http"))
    .WithExternalHttpEndpoints();

// ============ Dashboard Info ============

Console.WriteLine();
Console.WriteLine("📊 Aspire Dashboard: https://localhost:15888");
Console.WriteLine();
Console.WriteLine("🌐 Trading API Endpoints:");
Console.WriteLine("   📖 Swagger:    http://localhost:7100/swagger");
Console.WriteLine("   📈 Metrics:    http://localhost:7100/metrics");
Console.WriteLine("   💓 Health:     http://localhost:7100/health");
Console.WriteLine();
Console.WriteLine("🧠 Cognitive Mesh (Decision Engine):");
Console.WriteLine("   - Web UI:     http://localhost:5678");
Console.WriteLine("   - API:        http://localhost:5678/api/reason");
Console.WriteLine("   - Workflow:   trade-cycle");
Console.WriteLine();
Console.WriteLine("🎮 Trading API Operations:");
Console.WriteLine("   POST /api/trading/initialize  - Initialize system");
Console.WriteLine("   POST /api/trading/start       - Start trading");
Console.WriteLine("   POST /api/trading/stop        - Stop trading");
Console.WriteLine("   GET  /api/trading/status      - System status");
Console.WriteLine("   GET  /api/agents              - Agent status");
Console.WriteLine();
Console.WriteLine("🖥️ Frontend:");
Console.WriteLine("   Web UI:       http://localhost:5173");
Console.WriteLine();

// ============ Build & Run ============

var app = builder.Build();
await app.RunAsync();
