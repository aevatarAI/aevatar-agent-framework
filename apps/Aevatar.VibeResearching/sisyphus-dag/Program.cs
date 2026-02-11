using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.Agents.Persistence.Neo4j.DependencyInjection;
using SisyphusDag.Data;
using SisyphusDag.Services;

var builder = WebApplication.CreateBuilder(args);

// Neo4j client registration from appsettings.json
builder.Services.AddAevatarNeo4j(options =>
{
    var section = builder.Configuration.GetSection("Neo4j");
    options.Uri = section["Uri"]!;
    options.Username = section["Username"]!;
    options.Password = section["Password"]!;
    options.Database = section["Database"] ?? "neo4j";
});

// Application services (scoped per request)
builder.Services.AddScoped<IKnowledgeRepository, KnowledgeRepository>();
builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();

// Controllers with JSON serialization options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

var app = builder.Build();

app.MapControllers();

app.Run();
