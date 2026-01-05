using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// ============ Configuration ============
var apiPort = builder.Configuration.GetValue("SRA:ApiPort", 5678);
var apiUrl = $"http://localhost:{apiPort}";

// ============ Banner ============
Console.WriteLine();
Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║   🧪 Scientific Research Assistant - Aspire AppHost            ║");
Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Backend  : {apiUrl,-38}║");
Console.WriteLine("║  Frontend : http://localhost:5173                             ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ============ Backend API ============
var api = builder.AddProject<Projects.ScientificResearchAssistant_Api>("sra-api")
    .WithEnvironment("ASPNETCORE_URLS", apiUrl)
    .WithExternalHttpEndpoints();

// ============ Frontend (Vite) ============
var frontendDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "frontend"));

var npmCandidates = new[]
{
    "/opt/homebrew/bin/npm", // macOS (Apple Silicon) homebrew
    "/usr/local/bin/npm",    // macOS (Intel) homebrew
    "/usr/bin/npm"           // system
};

var npm = npmCandidates.FirstOrDefault(File.Exists) ?? "npm";

builder.AddExecutable("sra-frontend", npm, frontendDir, "run", "dev")
    .WithHttpEndpoint(targetPort: 5173, port: 5173, name: "http", env: null, isProxied: false)
    .WithEnvironment("SRA_API_PROXY_TARGET", apiUrl)
    .WithExternalHttpEndpoints();

// ============ Run ============
var app = builder.Build();
await app.RunAsync();


