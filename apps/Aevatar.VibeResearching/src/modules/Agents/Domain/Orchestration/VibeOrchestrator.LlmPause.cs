using Aevatar.Agents.AGUI;
using Aevatar.Agents.Core.Secrets;
using Aevatar.VibeResearching.Sessions;
using Aevatar.VibeResearching.Sessions.Services;

namespace Aevatar.VibeResearching.Agents;

public sealed partial class VibeOrchestrator
{
    // ============================================================
    //  LLM Provider Pause/Resume (MVP)
    //
    //  中文说明：
    //  - 目标：当发现缺少 API key 时，不要让 run 直接失败。
    //  - 行为：发出一个 UI 事件 + 说明文字，然后轮询等待用户完成配置，随后继续跑本轮。
    //  - 约束：run 是 fire-and-forget（后台任务），因此可以安全等待；但要有超时避免无限挂起。
    // ============================================================

    private const string DefaultProviderKey = "LLMProviders:Default";
    private const int ProviderWaitTimeoutSeconds = 10 * 60; // 10 minutes
    private const int ProviderWaitPollMs = 1200;

    private string ResolveEffectiveProviderName(string? providerName)
    {
        var name = (providerName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "default", StringComparison.OrdinalIgnoreCase))
        {
            if (_core.UserSecrets.TryGet(DefaultProviderKey, out var def) && !string.IsNullOrWhiteSpace(def))
                name = def.Trim();
            else
                name = "default";
        }

        // If "default" is just a placeholder, pick the first runnable provider instance.
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
        return _core.UserSecrets.TryGet(keyPath, out var v) && !string.IsNullOrWhiteSpace(v);
    }

    private string PickFirstRunnableProviderName()
    {
        const string prefix = "LLMProviders:Providers:";
        const string suffix = ":ApiKey";

        try
        {
            var all = _core.UserSecrets.GetAll();
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
        catch
        {
            return string.Empty;
        }
    }

    private async Task<string> EnsureProviderRunnableOrPauseAsync(
        ResearchSession session,
        string runId,
        string agent,
        string stepName,
        Func<string?> resolveProvider,
        Action<string> emitAssistantDelta,
        CancellationToken ct)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var lastProvider = string.Empty;
        var notified = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var effective = ResolveEffectiveProviderName(resolveProvider());
            if (IsProviderRunnable(effective))
                return effective;

            // Only emit the "please configure" message once per provider name.
            if (!notified || !string.Equals(lastProvider, effective, StringComparison.OrdinalIgnoreCase))
            {
                lastProvider = effective;
                notified = true;

                // UI: signal that we are waiting for API key configuration.
                try
                {
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = NowMs(),
                        Name = "aevatar.vibe.llm_api_key_required",
                        Value = new
                        {
                            sessionId = session.Id,
                            runId,
                            agent,
                            stepName,
                            providerName = effective,
                            keyPath = $"LLMProviders:Providers:{effective}:ApiKey"
                        }
                    });
                }
                catch
                {
                    // best-effort only
                }

                // Chat stream: tell the user what to do.
                try
                {
                    emitAssistantDelta("\n\n");
                    emitAssistantDelta("### 需要配置 LLM API Key（已暂停）\n");
                    emitAssistantDelta($"- agent: `{agent}`\n");
                    emitAssistantDelta($"- provider: `{effective}`\n");
                    emitAssistantDelta("请在左侧面板配置 API Key（或通过 Secrets API/CLI 写入 user secrets）。\n");
                    emitAssistantDelta("配置完成后，本轮会自动继续（无需重新发起）。\n\n");
                }
                catch
                {
                    // best-effort only
                }
            }

            // Timeout guard: avoid infinite background hangs.
            if ((DateTimeOffset.UtcNow - startedUtc).TotalSeconds > ProviderWaitTimeoutSeconds)
                throw new InvalidOperationException($"LLM provider '{lastProvider}' missing apiKey for too long (timeout).");

            await Task.Delay(ProviderWaitPollMs, ct);
        }
    }
}