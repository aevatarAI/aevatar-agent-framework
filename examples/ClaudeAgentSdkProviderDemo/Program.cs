using System.ComponentModel;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.LLMTornado;
using ClaudeAgentSdkProviderDemo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ============================================================================
// Claude Agent SDK Provider Demo (offline + deterministic)
// ============================================================================
//
// This demo is designed to showcase what makes ProviderType=claude_agent_sdk different:
// - projectRoot + .claude/* (File-SSoT style)
// - plugins
// - allowlist-based permissions (filesystem_read/write)
// - process isolation (external runner)
// - best-effort streaming via marker protocol
// - tool-loop boundary: ignores Functions and never returns AevatarFunctionCall
//
// IMPORTANT:
// - This demo does NOT call any real network/LLM.
// - The runner is a mock Node script under runner/ (no deps).
//
// ============================================================================

var demoBin = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
var demoMode = GetDemoMode();
var repoRoot = TryFindRepoRoot(demoBin);
var isRealMode = string.Equals(demoMode, "real", StringComparison.OrdinalIgnoreCase);

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        // Make config location deterministic: resolve appsettings from the built output directory,
        // not from current working directory.
        config.SetBasePath(demoBin);
        config.AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.secrets.json", optional: true);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.Configure<LLMProvidersConfig>(context.Configuration.GetSection("LLMProviders"));
        services.AddAevatarLLMTornado();
    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("╔══════════════════════════════════════════════════════╗");
logger.LogInformation("║   Claude Agent SDK Provider Demo (mode: {Mode})     ║", demoMode);
logger.LogInformation("╚══════════════════════════════════════════════════════╝");
logger.LogInformation("DemoBin: {DemoBin}", demoBin);
logger.LogInformation("RepoRoot: {RepoRoot}", repoRoot ?? "(auto-detect failed)");
logger.LogInformation("Mode switch: set env CLAUDE_AGENT_SDK_DEMO_MODE=real to run real runner; default is mock.");

// Patch __DEMO_BIN__ placeholders BEFORE resolving ILLMProviderFactory (factory copies configs at construction).
var llmProvidersOptions = host.Services.GetRequiredService<IOptions<LLMProvidersConfig>>();
NormalizeDemoProviderConfigs(llmProvidersOptions.Value, demoBin, demoMode, repoRoot);

var factory = host.Services.GetRequiredService<ILLMProviderFactory>();

// Shared request with a dummy function definition to prove tool-loop boundary.
var request = new AevatarLLMRequest
{
    SystemPrompt = "You are a demo assistant. Keep output deterministic.",
    Messages =
    {
        new AevatarChatMessage
        {
            Role = AevatarChatRole.User,
            Content = isRealMode
                ? """
                  (REAL MODE DEMO)
                  1) Read `data/context.txt`
                  2) Write `output/runner_output.txt` with a short summary + why claude_agent_sdk is special
                  3) Reply with a short confirmation (keep it brief)
                  """
                : "Explain what makes claude_agent_sdk special."
        }
    },
    Functions = new List<AevatarFunctionDefinition>
    {
        new()
        {
            Name = "dangerous_exec",
            Description = "A dummy tool definition (should be ignored by claude_agent_sdk provider).",
            Required = false,
            Parameters = new Dictionary<string, AevatarParameterDefinition>
            {
                ["cmd"] = new() { Type = "string", Required = true, Description = "command" }
            }
        }
    }
};

// ---------------------------------------------------------------------------
// 1) Baseline (in-process, deterministic)
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("============================================================");
Console.WriteLine("1) BASELINE (in-process, deterministic)");
Console.WriteLine("============================================================");

var baseline = new BaselineDeterministicProvider();
var baselineResp = await baseline.GenerateAsync(request);
Console.WriteLine(baselineResp.Content);
Console.WriteLine($"AevatarFunctionCall returned? {(baselineResp.AevatarFunctionCall != null ? "YES (unexpected)" : "NO (expected)")}");

// ---------------------------------------------------------------------------
// 2) claude_agent_sdk_minimal (deny by default)
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("============================================================");
Console.WriteLine("2) claude_agent_sdk_minimal (allowedTools empty => deny)");
Console.WriteLine("============================================================");

await RunClaudeAgentSdkOnce(factory, "claude_agent_sdk_minimal", request, demoBin);

// ---------------------------------------------------------------------------
// 3) claude_agent_sdk_full (allow read/write) + streaming markers
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("============================================================");
Console.WriteLine("3) claude_agent_sdk_full (allowlist read/write) + STREAMING");
Console.WriteLine("============================================================");

await RunClaudeAgentSdkOnce(factory, "claude_agent_sdk_full", request, demoBin);

Console.WriteLine();
Console.WriteLine("--- Streaming (best-effort via marker protocol) ---");
await RunClaudeAgentSdkStreaming(factory, "claude_agent_sdk_full", request);

Console.WriteLine();
Console.WriteLine("Done.");

// ============================================================================
// Helpers
// ============================================================================

static async Task RunClaudeAgentSdkOnce(
    ILLMProviderFactory factory,
    string providerName,
    AevatarLLMRequest request,
    string demoBin)
{
    try
    {
        // Clean previous runs so "created vs not created" is meaningful.
        var expectedOutFile = Path.Combine(demoBin, "demo_project", "output", "runner_output.txt");
        TryDeleteFile(expectedOutFile);

        var provider = factory.GetProvider(providerName);
        var resp = await provider.GenerateAsync(request);

        Console.WriteLine(resp.Content);
        Console.WriteLine($"AevatarFunctionCall returned? {(resp.AevatarFunctionCall != null ? "YES (unexpected)" : "NO (expected)")}");

        if (File.Exists(expectedOutFile))
        {
            Console.WriteLine($"Output file created: {expectedOutFile}");
        }
        else
        {
            Console.WriteLine($"Output file NOT created (expected for minimal/deny): {expectedOutFile}");
        }
    }
    catch (Exception ex) when (IsNodeMissing(ex))
    {
        Console.WriteLine("[ERROR] Node.js is required for the mock runner but was not found.");
        Console.WriteLine("Fix: install Node.js, or configure ProviderSpecificSettings.runnerCommand to point to your node binary.");
        Console.WriteLine($"Details: {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine("[ERROR] claude_agent_sdk run failed.");
        Console.WriteLine(ex);
    }
}

static void TryDeleteFile(string path)
{
    try
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
    catch
    {
        // best-effort (demo only)
    }
}

static async Task RunClaudeAgentSdkStreaming(
    ILLMProviderFactory factory,
    string providerName,
    AevatarLLMRequest request)
{
    try
    {
        var provider = factory.GetProvider(providerName);

        await foreach (var token in provider.GenerateStreamAsync(request))
        {
            if (token.IsComplete)
            {
                Console.WriteLine("[stream] <complete>");
                continue;
            }

            if (!string.IsNullOrEmpty(token.Content))
            {
                Console.WriteLine($"[stream] {token.Content}");
            }
        }
    }
    catch (Exception ex) when (IsNodeMissing(ex))
    {
        Console.WriteLine("[ERROR] Node.js is required for streaming demo but was not found.");
        Console.WriteLine($"Details: {ex.Message}");
    }
}

static bool IsNodeMissing(Exception ex)
{
    // Windows: Win32Exception (file not found)
    if (ex is Win32Exception)
        return true;

    // Unix/macOS: often surfaces as "No such file or directory" in message.
    return ex.Message.Contains("node", StringComparison.OrdinalIgnoreCase) &&
           ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);
}

static void NormalizeDemoProviderConfigs(LLMProvidersConfig cfg, string demoBin, string demoMode, string? repoRoot)
{
    foreach (var kv in cfg.Providers)
    {
        var provider = kv.Value;

        if (!string.Equals(provider.ProviderType, "claude_agent_sdk", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(provider.ProviderType, "claude-agent-sdk", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        // We normalize key fields to avoid configuration binder "object" shape pitfalls
        // (Dictionary<string, object> can produce List<object> items that stringify to "System.Object").
        //
        // Required fields for ClaudeAgentSdkProviderConfig:
        // - runnerCommand
        // - runnerArgs (non-empty)
        // - projectRoot (path)
        // We also set plugins path for demo.
        provider.ProviderSpecificSettings ??= new Dictionary<string, object>();

        if (!provider.ProviderSpecificSettings.TryGetValue("runnerCommand", out var rc) ||
            rc is not string ||
            string.IsNullOrWhiteSpace((string)rc))
        {
            provider.ProviderSpecificSettings["runnerCommand"] = "node";
        }

        // Runner selection:
        // - mock: use runner copied into build output dir (offline, deterministic)
        // - real: use source runner path so Node can resolve node_modules from runner/ (after npm install)
        provider.ProviderSpecificSettings["runnerArgs"] = new[] { ResolveRunnerScriptPath(demoBin, demoMode, repoRoot) };

        provider.ProviderSpecificSettings["projectRoot"] = Path.Combine(demoBin, "demo_project");

        // In real mode, keep plugins empty by default (Claude Agent SDK plugin format differs from mock plugins).
        // In mock mode, keep deterministic .mjs plugins demo.
        provider.ProviderSpecificSettings["plugins"] = string.Equals(demoMode, "real", StringComparison.OrdinalIgnoreCase)
            ? Array.Empty<string>()
            : new[] { Path.Combine(demoBin, "plugins") };

        // Project instructions (CLAUDE.md):
        // Agent SDK loads CLAUDE.md only when settingSources includes "project".
        provider.ProviderSpecificSettings["settingSources"] = string.Equals(demoMode, "real", StringComparison.OrdinalIgnoreCase)
            ? new[] { "project" }
            : Array.Empty<string>();

        // Keep allow-list deterministic and explicit for demo clarity.
        if (string.Equals(provider.Name, "claude_agent_sdk_full", StringComparison.OrdinalIgnoreCase))
        {
            provider.ProviderSpecificSettings["allowedTools"] = new[] { "filesystem_read", "filesystem_write" };

            // Avoid interactive prompts in real mode while still respecting allowedTools.
            if (string.Equals(demoMode, "real", StringComparison.OrdinalIgnoreCase))
            {
                provider.ProviderSpecificSettings["permissionMode"] = "acceptEdits";
            }
        }
        else if (string.Equals(provider.Name, "claude_agent_sdk_minimal", StringComparison.OrdinalIgnoreCase))
        {
            provider.ProviderSpecificSettings["allowedTools"] = Array.Empty<string>();
        }
    }
}

static string GetDemoMode()
{
    var mode = Environment.GetEnvironmentVariable("CLAUDE_AGENT_SDK_DEMO_MODE");
    if (string.Equals(mode, "real", StringComparison.OrdinalIgnoreCase))
        return "real";
    return "mock";
}

static string? TryFindRepoRoot(string demoBin)
{
    // Repo root is often reachable by walking up from the build output folder.
    foreach (var start in new[] { demoBin, Environment.CurrentDirectory })
    {
        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 12 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "examples", "ClaudeAgentSdkProviderDemo", "runner",
                "real_claude_agent_sdk_runner.mjs");
            if (File.Exists(candidate))
                return dir.FullName;
            dir = dir.Parent;
        }
    }
    return null;
}

static string ResolveRunnerScriptPath(string demoBin, string demoMode, string? repoRoot)
{
    if (!string.Equals(demoMode, "real", StringComparison.OrdinalIgnoreCase))
    {
        return Path.Combine(demoBin, "runner", "mock_claude_agent_sdk_runner.mjs");
    }

    if (!string.IsNullOrWhiteSpace(repoRoot))
    {
        return Path.Combine(repoRoot, "examples", "ClaudeAgentSdkProviderDemo", "runner",
            "real_claude_agent_sdk_runner.mjs");
    }

    // Fallback: try running from copied output (may fail to resolve node_modules, but gives a clear error).
    return Path.Combine(demoBin, "runner", "real_claude_agent_sdk_runner.mjs");
}


