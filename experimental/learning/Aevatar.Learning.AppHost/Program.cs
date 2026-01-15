using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// ============ Configuration ============
//
// Repo policy:
// - No :5000
// - Default backend:  5678
// - Default frontend: 5173
//
var apiPort = builder.Configuration.GetValue("Learning:ApiPort", 5678);
var frontendPort = builder.Configuration.GetValue("Learning:FrontendPort", 5173);
var apiUrl = $"http://localhost:{apiPort}";

// ============ Banner ============

Console.WriteLine();
Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║        🎓 Aevatar.Learning - Aspire AppHost                    ║");
Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  API Port : {apiPort,-6}  Frontend Port : {frontendPort,-6}            ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ============ Backend API ============

var api = builder.AddProject<Projects.Aevatar_Learning_Api>("learning-api")
    .WithEnvironment("ASPNETCORE_URLS", apiUrl)
    .WithExternalHttpEndpoints();

// ============ Frontend (Vite web dev) ============
//
//  Purpose:
//  - Let frontend be managed by Aspire Dashboard for one-click dev.
//
var frontendDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "frontend"));

// NOTE:
// - Aspire-launched processes may not inherit interactive shell PATH (brew/nvm),
//   so we probe common npm locations and fall back to "npm".
var npmCandidates = new[]
{
    "/opt/homebrew/bin/npm", // macOS (Apple Silicon) homebrew
    "/usr/local/bin/npm",    // macOS (Intel) homebrew
    "/usr/bin/npm"           // system
};

var npm = npmCandidates.FirstOrDefault(File.Exists) ?? "npm";

var frontend = builder.AddExecutable("learning-frontend", npm, frontendDir, "run", "dev:web")
    .WithHttpEndpoint(targetPort: frontendPort, port: frontendPort, name: "http", env: null, isProxied: false)
    .WithEnvironment("VITE_LEARNING_API_URL", apiUrl)
    .WithExternalHttpEndpoints();

// ============ Run ============

var app = builder.Build();
await app.RunAsync();


