using System.Diagnostics;
using System.Text.Json;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.LLMTornado.ClaudeAgentSdk;

/// <summary>
/// IAevatarLLMProvider implementation backed by Claude Agent SDK (headless runner process).
///
/// IMPORTANT BOUNDARY:
/// - We do NOT map Aevatar tools/functions into Claude Agent SDK.
/// - We do NOT return <see cref="AevatarFunctionCall" />.
/// This avoids stacking two independent tool loops (Aevatar + Claude Agent SDK).
/// </summary>
public sealed class ClaudeAgentSdkProvider : AevatarLLMProviderBase
{
    private readonly LLMProviderConfig _providerConfig;
    private readonly ClaudeAgentSdkProviderConfig _config;
    private readonly ClaudeAgentSdkRunner _runner;
    private readonly ILogger<ClaudeAgentSdkProvider> _logger;

    public ClaudeAgentSdkProvider(
        LLMProviderConfig providerConfig,
        ILogger<ClaudeAgentSdkProvider> logger)
    {
        _providerConfig = providerConfig ?? throw new ArgumentNullException(nameof(providerConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Parse ProviderSpecificSettings + apply safe defaults.
        var parsed = ClaudeAgentSdkProviderConfig.From(providerConfig);

        // Best-effort: allow ApiKey from config to be forwarded as env var.
        // NOTE: do NOT log it.
        _config = MergeApiKeyIntoEnv(parsed, providerConfig.ApiKey);
        _runner = new ClaudeAgentSdkRunner(_config, _logger);
    }

    protected override ILogger? Logger => _logger;
    protected override string ProviderName => $"ClaudeAgentSdk:{(_providerConfig.Model ?? "default")}";

    public override Task<AevatarModelInfo> GetModelInfoAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AevatarModelInfo
        {
            Name = _providerConfig.Model,
            MaxTokens = _providerConfig.MaxTokens,
            SupportsStreaming = true, // best-effort
            SupportsFunctions = false // hard boundary: no tool loop mapping
        });
    }

    protected override async Task<AevatarLLMResponse> GenerateCoreAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        // Hard boundary: ignore Aevatar tools.
        // (We still allow caller to pass Functions for other providers; here we intentionally drop them.)

        var payload = BuildRunnerRequest(request, stream: false);
        var requestJson = JsonSerializer.Serialize(payload);

        _logger.LogInformation("[ClaudeAgentSdk] Executing runner (sync). Provider={Provider}, Model={Model}",
            _providerConfig.Name, _providerConfig.Model);

        var (stdout, stderr, exitCode, timedOut) = await _runner.ExecuteAsync(requestJson, cancellationToken);

        if (timedOut)
        {
            throw new OperationCanceledException($"Claude Agent SDK runner timed out after {_config.TimeoutMs}ms.");
        }

        if (exitCode != 0)
        {
            var err = string.IsNullOrWhiteSpace(stderr) ? "(no stderr)" : stderr.TrimEnd();
            throw new InvalidOperationException(
                $"Claude Agent SDK runner failed (exitCode={exitCode}). Stderr (tail): {err}");
        }

        var content = ExtractContentFromStdout(stdout);

        sw.Stop();
        _logger.LogInformation("[ClaudeAgentSdk] Runner completed (sync) in {Elapsed}ms, chars={Chars}",
            sw.ElapsedMilliseconds, content.Length);

        return new AevatarLLMResponse
        {
            Content = content,
            ModelName = request.Settings?.ModelId ?? _providerConfig.Model,
            AevatarStopReason = AevatarStopReason.Complete,
            AevatarFunctionCall = null,
            Usage = null,
            Metadata = new Dictionary<string, object>
            {
                ["provider"] = "claude_agent_sdk",
                ["runner_command"] = _config.RunnerCommand
            }
        };
    }

    protected override async IAsyncEnumerable<AevatarLLMToken> GenerateStreamCoreAsync(
        AevatarLLMRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var payload = BuildRunnerRequest(request, stream: true);
        var requestJson = JsonSerializer.Serialize(payload);

        _logger.LogInformation("[ClaudeAgentSdk] Executing runner (stream). Provider={Provider}, Model={Model}",
            _providerConfig.Name, _providerConfig.Model);

        var idx = 0;
        await foreach (var delta in _runner.ExecuteStreamingAsync(requestJson, cancellationToken))
        {
            if (string.IsNullOrEmpty(delta))
                continue;

            yield return new AevatarLLMToken
            {
                Index = idx++,
                Content = delta,
                IsComplete = false,
                AevatarFunctionCall = null
            };
        }

        // Emit a completion token (optional but makes downstream logic consistent).
        yield return new AevatarLLMToken
        {
            Index = idx,
            Content = string.Empty,
            IsComplete = true,
            AevatarFunctionCall = null
        };
    }

    private object BuildRunnerRequest(AevatarLLMRequest request, bool stream)
    {
        // ------------------------------------------------------------
        // Minimal, stable request shape.
        // The runner is responsible for mapping this into Claude Agent SDK API calls.
        // ------------------------------------------------------------
        var messages = new List<object>();
        if (request.Messages is { Count: > 0 })
        {
            foreach (var m in request.Messages)
            {
                messages.Add(new
                {
                    role = MapRole(m.Role),
                    content = m.Content ?? string.Empty
                });
            }
        }

        // If UserPrompt is set (rare in current AIGAgentBase), append it as a user message.
        if (!string.IsNullOrWhiteSpace(request.UserPrompt))
        {
            messages.Add(new { role = "user", content = request.UserPrompt });
        }

        return new
        {
            stream,
            model = request.Settings?.ModelId ?? _providerConfig.Model,
            temperature = request.Settings?.Temperature ?? _providerConfig.Temperature,
            maxTokens = request.Settings?.MaxTokens ?? _providerConfig.MaxTokens,
            systemPrompt = request.SystemPrompt ?? string.Empty,
            messages,

            // Project-level settings (.claude/*)
            projectRoot = _config.ProjectRoot,
            workingDirectory = _config.WorkingDirectory,
            plugins = _config.Plugins,
            settingSources = _config.SettingSources,

            // Permission model (best-effort pass-through)
            allowedTools = _config.AllowedTools,
            permissionMode = _config.PermissionMode,

            // Extra metadata for observability (do not include secrets)
            metadata = new Dictionary<string, object?>
            {
                ["provider"] = "claude_agent_sdk",
                ["provider_name"] = _providerConfig.Name,
                ["timestamp_utc"] = DateTime.UtcNow.ToString("O")
            }
        };
    }

    private static string MapRole(AevatarChatRole role)
    {
        return role switch
        {
            AevatarChatRole.System => "system",
            AevatarChatRole.User => "user",
            AevatarChatRole.Assistant => "assistant",
            AevatarChatRole.Tool => "tool",
            _ => "user"
        };
    }

    private static string ExtractContentFromStdout(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return string.Empty;

        if (ClaudeAgentSdkProtocol.TryExtractOutputJsonFromText(stdout, out var json) &&
            ClaudeAgentSdkProtocol.TryExtractContentFromOutputJson(json, out var content))
        {
            return content;
        }

        // Fallback: return raw stdout tail (trim noisy whitespace).
        return stdout.Trim();
    }

    private static ClaudeAgentSdkProviderConfig MergeApiKeyIntoEnv(
        ClaudeAgentSdkProviderConfig config,
        string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return config;

        // Avoid overriding user-provided env.
        if (config.ExtraEnvironment.ContainsKey("ANTHROPIC_API_KEY"))
            return config;

        var merged = new Dictionary<string, string>(config.ExtraEnvironment, StringComparer.OrdinalIgnoreCase)
        {
            ["ANTHROPIC_API_KEY"] = apiKey
        };

        return new ClaudeAgentSdkProviderConfig
        {
            RunnerCommand = config.RunnerCommand,
            RunnerArgs = config.RunnerArgs,
            WorkingDirectory = config.WorkingDirectory,
            ProjectRoot = config.ProjectRoot,
            Plugins = config.Plugins,
            SettingSources = config.SettingSources,
            AllowedTools = config.AllowedTools,
            PermissionMode = config.PermissionMode,
            ExtraEnvironment = merged,
            TimeoutMs = config.TimeoutMs,
            MaxOutputChars = config.MaxOutputChars
        };
    }
}


