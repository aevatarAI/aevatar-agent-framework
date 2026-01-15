var builder = DistributedApplication.CreateBuilder(args);

Console.WriteLine("🚀 Aspire AppHost - Maker System");
Console.WriteLine("================================");

// -------------------------------------------------------------------------
// Maker System - Multi-Agent Creative Execution Platform
// -------------------------------------------------------------------------
var makerSystem = builder.AddProject<Projects.MakerSystem>("maker-system")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

Console.WriteLine("✅ Maker System 已配置");
Console.WriteLine("");
Console.WriteLine("📊 启动后查看终端输出获取 Dashboard 登录 URL");
Console.WriteLine("   - 分布式追踪：跟踪 Task → Coordinator → Worker → LLM");
Console.WriteLine("   - 结构化日志：按 Agent/Task 过滤");
Console.WriteLine("   - 性能指标：LLM 延迟、事件吞吐");
Console.WriteLine("");

var app = builder.Build();
await app.RunAsync();

