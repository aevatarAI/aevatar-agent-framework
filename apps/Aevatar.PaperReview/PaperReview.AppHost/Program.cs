// =========================================================================
//  PaperReview.AppHost - Aspire Orchestration for Paper Review
// =========================================================================
//  AI-Powered Academic Paper Review Platform
//
//  Features:
//    - Multi-expert consensus via MAKER system
//    - Multiple review types (Quick/Detailed/Deep/Revision)
//    - Multiple venue support (NeurIPS, ACL, CVPR, etc.)
//    - Real-time progress tracking via SSE
// =========================================================================

var builder = DistributedApplication.CreateBuilder(args);

Console.WriteLine("📄 Aspire AppHost - Paper Review");
Console.WriteLine("===================================");

// -------------------------------------------------------------------------
// Paper Review Service
// -------------------------------------------------------------------------
// AI-powered academic paper review with MAKER consensus system.
var paperReview = builder.AddProject<Projects.Aevatar_PaperReview>("paper-review")
    .WithExternalHttpEndpoints();

Console.WriteLine("✅ Paper Review: AI Academic Review Platform");
Console.WriteLine("");
Console.WriteLine("📊 服务端点:");
Console.WriteLine("   - Web UI:    http://localhost:5002");
Console.WriteLine("   - API:       http://localhost:5002/api/sessions");
Console.WriteLine("");
Console.WriteLine("🎯 评审类型:");
Console.WriteLine("   - Quick Review:       快速预审");
Console.WriteLine("   - Detailed Review:    详细评审");
Console.WriteLine("   - Deep Analysis:      深度分析");
Console.WriteLine("   - Revision Suggestion: 修改建议");
Console.WriteLine("");
Console.WriteLine("🏛️ 支持会议/期刊:");
Console.WriteLine("   - AI:  NeurIPS, ICML, ICLR");
Console.WriteLine("   - NLP: ACL, EMNLP, NAACL");
Console.WriteLine("   - CV:  CVPR, ICCV, ECCV");
Console.WriteLine("");

var app = builder.Build();
await app.RunAsync();
