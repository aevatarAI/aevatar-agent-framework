using Aevatar.Agents.AGUI;
using Aevatar.Agents.Core.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Cognitive.Execution.Run;

public sealed class LlmProviderGateOptions
{
    public string DefaultProviderKey { get; set; } = "LLMProviders:Default";
    public string ApiKeyEventName { get; set; } = "aevatar.vibe.llm_api_key_required";
    public TimeSpan WaitTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(1200);
}

public sealed class LlmProviderGate
{
    private readonly IAevatarUserSecretsStore _secrets;
    private readonly LlmProviderGateOptions _options;
    private readonly ILogger<LlmProviderGate> _logger;

    public LlmProviderGate(
        IAevatarUserSecretsStore secrets,
        IOptions<LlmProviderGateOptions> options,
        ILogger<LlmProviderGate> logger)
    {
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _options = options?.Value ?? new LlmProviderGateOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> EnsureProviderRunnableOrPauseAsync(
        WorkflowRunContext context,
        string agent,
        string stepName,
        Func<string?> resolveProvider,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        resolveProvider ??= () => null;

        var startedAt = DateTimeOffset.UtcNow;
        var lastProvider = string.Empty;
        var notified = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var effective = ResolveEffectiveProviderName(resolveProvider());
            if (IsProviderRunnable(effective))
                return effective;

            if (!notified || !string.Equals(lastProvider, effective, StringComparison.OrdinalIgnoreCase))
            {
                lastProvider = effective;
                notified = true;

                PublishPauseEvent(context, agent, stepName, effective);
                EmitPauseMessage(context, agent, stepName, effective);
            }

            if ((DateTimeOffset.UtcNow - startedAt) > _options.WaitTimeout)
            {
                throw new InvalidOperationException(
                    $"LLM provider '{lastProvider}' missing apiKey for too long (timeout).");
            }

            await Task.Delay(_options.PollInterval, ct);
        }
    }

    private string ResolveEffectiveProviderName(string? providerName)
    {
        var name = (providerName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "default", StringComparison.OrdinalIgnoreCase))
        {
            if (_secrets.TryGet(_options.DefaultProviderKey, out var def) && !string.IsNullOrWhiteSpace(def))
                name = def.Trim();
            else
                name = "default";
        }

        if (string.Equals(name, "default", StringComparison.OrdinalIgnoreCase) && !IsProviderRunnable("default"))
        {
            var picked = PickFirstRunnableProviderName();
            if (!string.IsNullOrWhiteSpace(picked))
                return picked;
        }

        return name;
    }

    private bool IsProviderRunnable(string providerName)
    {
        var name = (providerName ?? string.Empty).Trim();
        if (name.Length == 0) return false;
        var keyPath = $"LLMProviders:Providers:{name}:ApiKey";
        return _secrets.TryGet(keyPath, out var v) && !string.IsNullOrWhiteSpace(v);
    }

    private string PickFirstRunnableProviderName()
    {
        const string prefix = "LLMProviders:Providers:";
        const string suffix = ":ApiKey";

        try
        {
            var all = _secrets.GetAll();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in all)
            {
                var k = kv.Key ?? string.Empty;
                if (!k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;

                var mid = k.Substring(prefix.Length, k.Length - prefix.Length - suffix.Length).Trim();
                if (mid.Length == 0) continue;
                names.Add(mid);
            }

            return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[LLM Gate] Failed to pick runnable provider.");
            return string.Empty;
        }
    }

    private void PublishPauseEvent(WorkflowRunContext context, string agent, string stepName, string providerName)
    {
        try
        {
            context.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = _options.ApiKeyEventName,
                Value = new
                {
                    sessionId = context.ThreadId,
                    runId = context.RunId,
                    agent,
                    stepName,
                    providerName,
                    keyPath = $"LLMProviders:Providers:{providerName}:ApiKey"
                }
            });
        }
        catch
        {
            // best-effort only
        }
    }

    private void EmitPauseMessage(WorkflowRunContext context, string agent, string stepName, string providerName)
    {
        try
        {
            context.EmitAssistantDelta("\n\n");
            context.EmitAssistantDelta("### 需要配置 LLM API Key（已暂停）\n");
            context.EmitAssistantDelta($"- agent: `{agent}`\n");
            context.EmitAssistantDelta($"- step: `{stepName}`\n");
            context.EmitAssistantDelta($"- provider: `{providerName}`\n");
            context.EmitAssistantDelta("请在左侧面板配置 API Key（或通过 Secrets API/CLI 写入 user secrets）。\n");
            context.EmitAssistantDelta("配置完成后，本轮会自动继续（无需重新发起）。\n\n");
        }
        catch
        {
            // best-effort only
        }
    }
}
