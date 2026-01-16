using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using Aevatar.Novel.Contracts;
using Aevatar.Novel.Sidecar.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Aevatar.Novel.Sidecar.Services.Ai;

// ============================================================
//  NovelSmartWriter
//
//  GOAL:
//  - "Smart Continue": feed (rules + objects + roles + draft excerpt) to an LLM,
//    then ask it to continue writing a short segment.
//
//  DESIGN:
//  - Boundary types are Protobuf (SmartContinueRequest/Response).
//  - Sidecar is responsible for context assembly from SSOT folders.
//  - Output is returned to UI for manual review/insertion (no auto-write).
// ============================================================

public sealed class NovelSmartWriter
{
    private static readonly string[] SupportedTextExtensions = [".md", ".txt"];

    private readonly ProjectRootManager _projectRoot;
    private readonly NovelAiOptions _options;
    private readonly ILogger<NovelSmartWriter> _logger;

    public NovelSmartWriter(
        ProjectRootManager projectRoot,
        IOptions<NovelAiOptions> options,
        ILogger<NovelSmartWriter> logger)
    {
        _projectRoot = projectRoot;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SmartContinueResponse> ContinueAsync(SmartContinueRequest req, CancellationToken ct)
    {
        var root = _projectRoot.GetProjectRoot();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new SmartContinueResponse
            {
                Success = false,
                Message = "project_root_not_set"
            };
        }

        var targetRel = (req.TargetRelativePath ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(targetRel))
        {
            return new SmartContinueResponse
            {
                Success = false,
                Message = "target_relative_path_required"
            };
        }

        if (Path.IsPathRooted(targetRel))
        {
            return new SmartContinueResponse
            {
                Success = false,
                Message = "relative_path_required"
            };
        }

        var targetFull = Path.GetFullPath(Path.Combine(root, targetRel));
        if (!IsUnderRoot(Path.GetFullPath(root), targetFull))
        {
            return new SmartContinueResponse
            {
                Success = false,
                Message = "path_outside_project_root"
            };
        }

        var ext = Path.GetExtension(targetFull);
        if (!IsSupportedTextFile(targetFull))
        {
            return new SmartContinueResponse
            {
                Success = false,
                Message = "unsupported_file_type"
            };
        }

        var apiKey = (_options.ApiKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new SmartContinueResponse
            {
                Success = false,
                Message = "llm_api_key_not_configured"
            };
        }

        var maxContextChars = req.MaxContextChars > 0 ? req.MaxContextChars : _options.DefaultMaxContextChars;
        var maxOutputChars = req.MaxOutputChars > 0 ? req.MaxOutputChars : _options.DefaultMaxOutputChars;

        var includeObjects = req.IncludeObjects;
        var includeRoles = req.IncludeRoles;
        var includeRules = req.IncludeRules;

        // If client didn't specify, default to include all "knowledge" folders.
        if (!includeObjects && !includeRoles && !includeRules)
        {
            includeObjects = true;
            includeRoles = true;
            includeRules = true;
        }

        var instruction = (req.Instruction ?? string.Empty).Trim();

        // Prefer UI draft (unsaved edits) if provided; otherwise read from disk.
        var draftText = (req.DraftText ?? string.Empty);
        if (string.IsNullOrWhiteSpace(draftText))
        {
            try
            {
                draftText = File.Exists(targetFull)
                    ? await File.ReadAllTextAsync(targetFull, Encoding.UTF8, ct)
                    : string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read target file: {Path}", targetFull);
                draftText = string.Empty;
            }
        }

        // ------------------------------------------------------------
        //  Assemble context (rules/objects/roles) with a budget.
        // ------------------------------------------------------------
        var ctx = new StringBuilder();
        ctx.AppendLine("=== Project Context (SSOT) ===");
        ctx.AppendLine($"project_root: {root}");
        ctx.AppendLine($"target_file: {targetRel}");
        ctx.AppendLine();

        var budget = Math.Max(8_000, maxContextChars);
        budget -= AppendSection(ctx, "Rules (rules/)", includeRules ? ReadFolderContext(root, "rules", 0.55) : []);
        budget -= AppendSection(ctx, "Objects (objects/)", includeObjects ? ReadFolderContext(root, "objects", 0.55) : []);
        budget -= AppendSection(ctx, "Roles (roles/)", includeRoles ? ReadFolderContext(root, "roles", 0.55) : []);

        // Draft excerpt is always included (most recent is most important).
        var draftExcerpt = Tail(draftText, Math.Min(12_000, Math.Max(2_000, budget)));
        ctx.AppendLine("=== Draft Excerpt (tail) ===");
        ctx.AppendLine(draftExcerpt);
        ctx.AppendLine();

        if (!string.IsNullOrWhiteSpace(instruction))
        {
            ctx.AppendLine("=== Author Instruction ===");
            ctx.AppendLine(instruction);
            ctx.AppendLine();
        }

        var isPlainText = ext.Equals(".txt", StringComparison.OrdinalIgnoreCase);
        var outputConstraint = isPlainText
            ? "Output MUST be plain text. Do NOT use Markdown syntax."
            : "Output should be Markdown-friendly, but keep it as a clean paragraph (avoid heavy formatting).";

        var systemPrompt = $$"""
You are a professional Chinese web-novel writing assistant.

Task:
- Continue the target file with ONE coherent continuation segment.
- Keep style, character voice, rules, and world setting consistent.

Constraints:
- {{outputConstraint}}
- Do NOT repeat existing sentences verbatim.
- Avoid meta commentary. Just write the story.
- Length hint: ~{{maxOutputChars}} characters (best-effort).
""";

        var userPrompt = ctx.ToString();

        try
        {
            var chat = CreateChatClient();
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var options = new ChatOptions
            {
                ModelId = _options.Model,
                MaxOutputTokens = _options.MaxOutputTokens,
                Temperature = (float)Math.Clamp(_options.Temperature, 0, 2),
            };

            _logger.LogInformation("[SmartContinue] Calling LLM: Model={Model}, Endpoint={Endpoint}, Target={Target}",
                _options.Model,
                string.IsNullOrWhiteSpace(_options.Endpoint) ? "(default)" : _options.Endpoint,
                targetRel);

            var response = await chat.GetResponseAsync(messages, options, ct);
            var text = (response.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return new SmartContinueResponse
                {
                    Success = false,
                    Message = "empty_response",
                    Model = response.ModelId ?? _options.Model
                };
            }

            // Best-effort length clamp.
            if (maxOutputChars > 50 && text.Length > maxOutputChars * 2)
            {
                text = text[..(maxOutputChars * 2)].TrimEnd();
                text += "\n\n（已截断：输出过长）";
            }

            return new SmartContinueResponse
            {
                Success = true,
                Message = "ok",
                GeneratedText = text,
                Model = response.ModelId ?? _options.Model
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SmartContinue] LLM call failed: {Message}", ex.Message);
            return new SmartContinueResponse
            {
                Success = false,
                Message = "llm_call_failed:" + ex.Message
            };
        }
    }

    private IChatClient CreateChatClient()
    {
        var timeoutMs = _options.TimeoutMilliseconds > 0 ? _options.TimeoutMilliseconds : 120_000;

        var clientOptions = new OpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromMilliseconds(timeoutMs),
            // Avoid HttpClient default timeout (100s) fighting our configured timeouts.
            Transport = new HttpClientPipelineTransport(new HttpClient
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            })
        };

        if (!string.IsNullOrWhiteSpace(_options.Endpoint))
            clientOptions.Endpoint = new Uri(_options.Endpoint);

        return new OpenAI.Chat.ChatClient(_options.Model, new ApiKeyCredential(_options.ApiKey!), clientOptions).AsIChatClient();
    }

    private static bool IsSupportedTextFile(string fullPath)
    {
        var ext = Path.GetExtension(fullPath);
        return SupportedTextExtensions.Any(x => x.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnderRoot(string rootFullPath, string candidateFullPath)
    {
        var root = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        return candidateFullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(candidateFullPath, rootFullPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string Tail(string text, int maxChars)
    {
        var s = (text ?? string.Empty).Replace("\r\n", "\n");
        if (maxChars <= 0) return "";
        if (s.Length <= maxChars) return s;
        return s[(s.Length - maxChars)..];
    }

    private static int AppendSection(StringBuilder sb, string title, IEnumerable<(string RelPath, string Content)> files)
    {
        var start = sb.Length;
        sb.AppendLine($"=== {title} ===");

        foreach (var (rel, content) in files)
        {
            sb.AppendLine($"--- file: {rel} ---");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        sb.AppendLine();
        return sb.Length - start;
    }

    private IEnumerable<(string RelPath, string Content)> ReadFolderContext(string root, string folderName, double perFileRatio)
    {
        try
        {
            var dir = Path.Combine(root, folderName);
            if (!Directory.Exists(dir))
                return [];

            var all = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(IsSupportedTextFile)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (all.Count == 0)
                return [];

            var maxContextChars = _options.DefaultMaxContextChars;
            var perFileBudget = Math.Max(800, (int)(maxContextChars * perFileRatio / Math.Max(1, all.Count)));

            var outList = new List<(string, string)>();
            foreach (var full in all)
            {
                var rel = Path.GetRelativePath(root, full).Replace('\\', '/');
                var text = File.ReadAllText(full, Encoding.UTF8);
                text = Tail(text, perFileBudget);
                outList.Add((rel, text));
            }

            return outList;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read context folder: {Folder}", folderName);
            return [];
        }
    }
}


