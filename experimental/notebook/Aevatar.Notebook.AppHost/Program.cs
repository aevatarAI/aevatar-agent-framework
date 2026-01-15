using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// ============ Configuration ============
var apiPort = builder.Configuration.GetValue("Notebook:ApiPort", 5678);
var apiUrl = $"http://localhost:{apiPort}";

// ============ Banner ============
Console.WriteLine();
Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║        📓 Aevatar.Notebook - Aspire AppHost                    ║");
Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  API Port : {apiPort,-6}                                     ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ============ Backend API ============
var api = builder.AddProject<Projects.Aevatar_Notebook_Api>("notebook-api")
    .WithEnvironment("ASPNETCORE_URLS", apiUrl)
    .WithExternalHttpEndpoints();

// ============ Run ============
var app = builder.Build();
await app.RunAsync();


