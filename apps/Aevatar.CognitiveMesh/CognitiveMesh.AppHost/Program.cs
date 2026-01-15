// =========================================================================
//  CognitiveMesh.AppHost - Aspire Orchestration for Cognitive Mesh
// =========================================================================
//  Cognitive Mesh: LLM Workflow Orchestration with Visual Monitoring
//
//  Features:
//    - DSL-based workflow definition (YAML)
//    - MAKER system integration (voting consensus)
//    - Real-time workflow visualization
//    - Multiple reasoning strategies (Direct, UoT, MAKER, Cognitive DSL)
// =========================================================================

var builder = DistributedApplication.CreateBuilder(args);

Console.WriteLine("🧠 Aspire AppHost - Cognitive Mesh");
Console.WriteLine("===================================");

// -------------------------------------------------------------------------
// Cognitive Mesh Service
// -------------------------------------------------------------------------
// Web-based LLM workflow orchestration with real-time visualization.
// Supports multiple reasoning strategies and DSL-defined workflows.
var cognitiveMesh = builder.AddProject<Projects.Aevatar_CognitiveMesh>("cognitive-mesh")
    .WithHttpEndpoint(port: 5678, name: "http")
    .WithExternalHttpEndpoints();

Console.WriteLine("✅ Cognitive Mesh: Workflow Orchestration Service");
Console.WriteLine("");
Console.WriteLine("📊 服务端点:");
Console.WriteLine("   - Web UI:    http://localhost:5678");
Console.WriteLine("   - Workflow:  http://localhost:5678/workflow.html");
Console.WriteLine("   - API:       http://localhost:5678/api/projects");
Console.WriteLine("");
Console.WriteLine("🔧 支持的推理策略:");
Console.WriteLine("   - direct:    直接 LLM 调用");
Console.WriteLine("   - uot:       Universe of Thought 思维链");
Console.WriteLine("   - maker:     MAKER 投票共识系统");
Console.WriteLine("   - cognitive: Cognitive DSL 工作流");
Console.WriteLine("");

var app = builder.Build();
await app.RunAsync();

