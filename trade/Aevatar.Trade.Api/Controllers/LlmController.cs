using Aevatar.Agents.AI.Abstractions.Providers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions;

namespace Aevatar.Trade.Api.Controllers;

/// <summary>
/// LLM provider status (safe snapshot; never returns secret values).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LlmController : ControllerBase
{
    [HttpGet("status")]
    public IActionResult GetStatus(
        ILLMProviderFactory factory,
        IOptions<LLMProvidersConfig> cfg)
    {
        var config = cfg.Value;
        var defaultName = config.Default ?? "";
        var names = factory.GetAvailableProviderNames();

        var providers = names.Select(name =>
        {
            var c = factory.GetProviderConfig(name);
            return new
            {
                name,
                providerType = c.ProviderType,
                model = c.Model,
                endpoint = c.Endpoint,
                apiKeyConfigured = !string.IsNullOrWhiteSpace(c.ApiKey),
                timeoutMs = c.TimeoutMilliseconds,
                maxTokens = c.MaxTokens,
                temperature = c.Temperature
            };
        });

        // NOTE:
        // - factory.GetType() tells us which backend is active:
        //   - MEAILLMProviderFactory (OpenAI-compatible)
        //   - LLMTornadoProviderFactory (native providers like Google/Gemini)
        return Ok(new
        {
            factory = factory.GetType().FullName ?? factory.GetType().Name,
            defaultProvider = defaultName,
            providerCount = names.Count,
            providers
        });
    }

    /// <summary>
    /// Explain how TradeSystem will distribute LLM providers across roles/symbols.
    /// This is a deterministic plan (no secrets).
    ///
    /// Why:
    /// - In remote environments, Information logs may be filtered, making it look like "pick not working".
    /// - This endpoint gives a single source of truth for the assignment plan.
    /// </summary>
    [HttpGet("assignment")]
    public IActionResult GetAssignment(
        ILLMProviderFactory factory,
        IOptions<TradingConfig> trading,
        IOptions<LLMProvidersConfig> cfg)
    {
        var t = trading.Value;
        var c = cfg.Value;

        var symbols = (t.Symbols?.ToList() ?? new List<string>());
        if (symbols.Count == 0 && !string.IsNullOrWhiteSpace(t.Symbol))
            symbols.Add(t.Symbol);
        if (symbols.Count == 0)
            symbols.Add("cmt_btcusdt");

        var defaultName = string.IsNullOrWhiteSpace(c.Default) ? "default" : c.Default!;

        var configProviderNames = c.Providers.Keys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var factoryProviderNames = factory.GetAvailableProviderNames()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Prefer factory view to reflect actual runtime (what can be resolved).
        var providerNames = factoryProviderNames.Count > 0 ? factoryProviderNames : configProviderNames;

        if (providerNames.Count == 0)
        {
            providerNames.Add(defaultName);
        }
        else
        {
            providerNames.Sort((a, b) =>
            {
                var aIsDefault = string.Equals(a, defaultName, StringComparison.OrdinalIgnoreCase);
                var bIsDefault = string.Equals(b, defaultName, StringComparison.OrdinalIgnoreCase);
                if (aIsDefault && !bIsDefault) return -1;
                if (!aIsDefault && bIsDefault) return 1;
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
        }

        string pick(int idx)
        {
            var n = Math.Abs(idx) % providerNames.Count;
            return providerNames[n];
        }

        var perSymbol = new List<object>();
        for (var i = 0; i < symbols.Count; i++)
        {
            perSymbol.Add(new
            {
                symbol = symbols[i],
                sentiment = pick(i * 2),
                technical = pick(i * 2 + 1)
            });
        }

        return Ok(new
        {
            defaultProvider = defaultName,
            providers = providerNames,
            providersFromConfig = configProviderNames,
            providersFromFactory = factoryProviderNames,
            coordinator = pick(0),
            risk = pick(providerNames.Count > 1 ? 1 : 0),
            perSymbol
        });
    }

    /// <summary>
    /// Provider circuit breaker health snapshot (runtime truth).
    /// Helps debug "always circuit-open" without relying on logs.
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(
        ILLMProviderFactory factory,
        CancellationToken ct)
    {
        var names = factory.GetAvailableProviderNames()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<object>();
        foreach (var name in names)
        {
            try
            {
                var p = await factory.GetProviderAsync(name, ct);
                if (p is AevatarLLMProviderBase b)
                {
                    var hs = b.GetHealthStatus();
                    results.Add(new
                    {
                        name,
                        state = hs.State.ToString(),
                        failureCount = hs.FailureCount,
                        lastSuccess = hs.LastSuccess,
                        lastFailure = hs.LastFailure,
                        openUntil = hs.OpenUntil
                    });
                }
                else
                {
                    results.Add(new
                    {
                        name,
                        state = "unknown",
                        note = $"providerType={p.GetType().Name} (no circuit status)"
                    });
                }
            }
            catch (Exception ex)
            {
                results.Add(new
                {
                    name,
                    state = "error",
                    errorType = ex.GetType().Name,
                    error = ex.Message
                });
            }
        }

        return Ok(new
        {
            providerCount = names.Count,
            providers = results
        });
    }

    /// <summary>
    /// Probe a provider with a tiny request (short timeout).
    /// Use this to verify that at least ONE provider is callable right now.
    /// </summary>
    [HttpPost("probe")]
    public async Task<IActionResult> Probe(
        [FromQuery] string name,
        ILLMProviderFactory factory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "Missing provider name (?name=...)." });

        try
        {
            // Build a minimal request (no tools) to test connectivity quickly.
            var provider = await factory.GetProviderAsync(name, ct);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(12));

            var req = new AevatarLLMRequest
            {
                SystemPrompt = "You are a diagnostic assistant. Reply with exactly: OK",
                // Keep it minimal and avoid depending on proto-generated chat message types here.
                UserPrompt = "Reply OK",
                Settings = new AevatarLLMSettings
                {
                    Temperature = 0,
                    MaxTokens = 8
                }
            };

            var resp = await provider.GenerateAsync(req, timeoutCts.Token);
            return Ok(new
            {
                name,
                ok = true,
                content = (resp.Content ?? "").Trim(),
                usage = resp.Usage
            });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Ok(new { name, ok = false, error = "timeout>12s" });
        }
        catch (Exception ex)
        {
            return Ok(new { name, ok = false, errorType = ex.GetType().Name, error = ex.Message });
        }
    }
}


