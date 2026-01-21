using System.Text;
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

            // Generate filename with timestamp
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"prompts_{timestamp}_{sessionId[..Math.Min(8, sessionId.Length)]}_{runId[..Math.Min(8, runId.Length)]}.md";
            var filepath = Path.Combine(PromptLogDir, filename);

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
            }, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(jsonFilepath, jsonContent, ct);

            Console.WriteLine($"[PromptLogger] Prompts saved to: {filepath}");
            Console.WriteLine($"[PromptLogger] JSON saved to: {jsonFilepath}");
        }
        catch (Exception ex)
        {
            // Don't fail the round if logging fails
            Console.WriteLine($"[PromptLogger] Warning: Failed to save prompts to file: {ex.Message}");
        }
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
