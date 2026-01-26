using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Vibe;

namespace VibeResearching.Api.Vibe;

// ============================================================
//  Agent Prompt Logger
//
//  Saves system prompts and user prompts for all agents to files
//  for debugging and analysis purposes.
// ============================================================

internal sealed partial class VibeOrchestrator
{
    // ============================================================
    //  Prompt Log Directory
    // ============================================================
    private static readonly string PromptLogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".aevatar",
        "prompt_logs"
    );

    // ============================================================
    //  Agent Prompt Record
    // ============================================================
    
    /// <summary>
    /// Record of an agent's prompts and output for a single execution.
    /// </summary>
    public sealed record AgentPromptRecord(
        string AgentName,
        string SystemPrompt,
        string UserPrompt,
        string? MaterialsContext,
        string RawOutput,
        DateTimeOffset Timestamp
    );

    /// <summary>
    /// Collection of all agent prompts for a single round.
    /// </summary>
    public sealed record RoundPromptLog(
        string SessionId,
        string RunId,
        string Question,
        DateTimeOffset Timestamp,
        List<AgentPromptRecord> Agents
    );

    // ============================================================
    //  Save All Agent Prompts
    // ============================================================

    /// <summary>
    /// Save all agent prompts from a round to files.
    /// </summary>
    private static async Task SaveAgentPromptsToFileAsync(
        string sessionId,
        string runId,
        string question,
        Dictionary<string, AgentPromptRecord> agentPrompts,
        CancellationToken ct)
    {
        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(PromptLogDir);

            // Generate filename based on sessionId and runId (not timestamp) to avoid duplicate files
            // Use runId as the primary identifier since it's unique per execution
            var sessionPrefix = sessionId.Length >= 8 ? sessionId[..8] : sessionId;
            var runPrefix = runId.Length >= 8 ? runId[..8] : runId;
            var filename = $"prompts_{sessionPrefix}_{runPrefix}.md";
            var filepath = Path.Combine(PromptLogDir, filename);
            
            // Use atomic file write with lock file to prevent race conditions
            // If file already exists, merge the content instead of creating a new file
            // This ensures we don't lose data from concurrent saves
            var lockFilepath = filepath + ".lock";
            var maxRetries = 10;
            var retryDelay = TimeSpan.FromMilliseconds(100);
            
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    // Try to create lock file atomically
                    using (var lockFile = new FileStream(lockFilepath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        // Lock file created successfully, proceed with write
                        try
                        {
                            // If file exists, read existing content and merge
                            Dictionary<string, AgentPromptRecord> existingPrompts = new(StringComparer.OrdinalIgnoreCase);
                            if (File.Exists(filepath))
                            {
                                try
                                {
                                    var existingJson = await File.ReadAllTextAsync(filepath.Replace(".md", ".json"), ct);
                                    if (!string.IsNullOrWhiteSpace(existingJson))
                                    {
                                        var existing = JsonSerializer.Deserialize<RoundPromptLog>(existingJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                        if (existing?.Agents != null)
                                        {
                                            foreach (var agent in existing.Agents)
                                            {
                                                existingPrompts[agent.AgentName] = new AgentPromptRecord(
                                                    agent.AgentName,
                                                    agent.SystemPrompt,
                                                    agent.UserPrompt,
                                                    agent.MaterialsContext,
                                                    agent.RawOutput,
                                                    agent.Timestamp
                                                );
                                            }
                                        }
                                    }
                                }
                                catch
                                {
                                    // If reading existing file fails, just overwrite it
                                }
                            }
                            
                            // Merge: new prompts override existing ones (latest wins)
                            foreach (var kvp in agentPrompts)
                            {
                                existingPrompts[kvp.Key] = kvp.Value;
                            }
                            
                            // Write merged content
                            await WritePromptFileAsync(filepath, sessionId, runId, question, existingPrompts, ct);
                            
                            // Release lock
                            File.Delete(lockFilepath);
                            return;
                        }
                        catch
                        {
                            // Clean up lock file on error
                            try { File.Delete(lockFilepath); } catch { }
                            throw;
                        }
                    }
                }
                catch (IOException) when (retry < maxRetries - 1)
                {
                    // Lock file exists, another process is writing, wait and retry
                    await Task.Delay(retryDelay, ct);
                }
            }
            
            // If all retries failed, append timestamp as fallback
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            filename = $"prompts_{sessionPrefix}_{runPrefix}_{timestamp}.md";
            filepath = Path.Combine(PromptLogDir, filename);
            await WritePromptFileAsync(filepath, sessionId, runId, question, agentPrompts, ct);
        }
        catch (Exception ex)
        {
            // Don't fail the round if logging fails
            Console.WriteLine($"[PromptLogger] Failed to save prompts: {ex.Message}");
        }
    }
    
    private static async Task WritePromptFileAsync(
        string filepath,
        string sessionId,
        string runId,
        string question,
        Dictionary<string, AgentPromptRecord> agentPrompts,
        CancellationToken ct)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("# Agent Prompts Log");
        sb.AppendLine();
        sb.AppendLine($"- **Session ID**: {sessionId}");
        sb.AppendLine($"- **Run ID**: {runId}");
        sb.AppendLine($"- **Timestamp**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"- **Question**: {question}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // Each agent
        foreach (var (agentName, record) in agentPrompts.OrderBy(kv => GetAgentOrder(kv.Key)))
        {
            sb.AppendLine($"## {agentName.ToUpperInvariant()}");
            sb.AppendLine();
            sb.AppendLine($"**Timestamp**: {record.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();

            // System Prompt
            sb.AppendLine("### System Prompt");
            sb.AppendLine();
            sb.AppendLine("<details>");
            sb.AppendLine("<summary>Click to expand</summary>");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(record.SystemPrompt);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();

            // User Prompt
            sb.AppendLine("### User Prompt");
            sb.AppendLine();
            sb.AppendLine("<details>");
            sb.AppendLine("<summary>Click to expand</summary>");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(record.UserPrompt);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();

            // Materials Context (if present)
            if (!string.IsNullOrWhiteSpace(record.MaterialsContext))
            {
                sb.AppendLine("### Materials Context");
                sb.AppendLine();
                sb.AppendLine("<details>");
                sb.AppendLine("<summary>Click to expand</summary>");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(Bound(record.MaterialsContext, 10000));
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine("</details>");
                sb.AppendLine();
            }

            // Raw Output
            sb.AppendLine("### Raw Output");
            sb.AppendLine();
            sb.AppendLine("<details>");
            sb.AppendLine("<summary>Click to expand</summary>");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(Bound(record.RawOutput, 15000));
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        // Write Markdown file
        await File.WriteAllTextAsync(filepath, sb.ToString(), ct);

        // Also write JSON version
        var jsonFilepath = Path.ChangeExtension(filepath, ".json");
        var jsonContent = JsonSerializer.Serialize(new
        {
            sessionId,
            runId,
            question,
            timestamp = DateTime.UtcNow,
            agents = agentPrompts.OrderBy(kv => GetAgentOrder(kv.Key)).Select(kv => new
            {
                agentName = kv.Key,
                systemPrompt = kv.Value.SystemPrompt,
                userPrompt = kv.Value.UserPrompt,
                materialsContext = kv.Value.MaterialsContext,
                rawOutput = kv.Value.RawOutput,
                timestamp = kv.Value.Timestamp
            }).ToList()
        }, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping  // Allow Chinese characters without escaping
        });

        await File.WriteAllTextAsync(jsonFilepath, jsonContent, ct);

        Console.WriteLine($"[PromptLogger] Prompts saved to: {filepath}");
        Console.WriteLine($"[PromptLogger] JSON saved to: {jsonFilepath}");
    }

    private static int GetAgentOrder(string agentName)
    {
        return agentName.ToLowerInvariant() switch
        {
            "planner" => 0,
            "reasoner" => 1,
            "librarian" => 2,
            "verifier" => 3,
            "dag_builder" => 4,
            "paper_editor" => 5,
            _ => 99
        };
    }

    // ============================================================
    //  Get System Prompt for Agent
    // ============================================================

    /// <summary>
    /// Get the system prompt for a specific agent type.
    /// </summary>
    public static string GetAgentSystemPrompt(string agentName)
    {
        return agentName.ToLowerInvariant() switch
        {
            "planner" => VibePlannerAgent.GetSystemPrompt(),
            "reasoner" => VibeReasonerAgent.GetSystemPrompt(),
            "librarian" => VibeLibrarianAgent.GetSystemPrompt(),
            "verifier" => VibeVerifierAgent.DefaultSystemPrompt,
            "dag_builder" => VibeDagBuilderAgent.GetSystemPrompt(),
            "paper_editor" => VibePaperEditorAgent.GetSystemPrompt(),
            _ => $"(Unknown agent: {agentName})"
        };
    }
}
