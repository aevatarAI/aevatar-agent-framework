using System.Net;
using System.Web;
using Aevatar.VibeResearching.UserProviders.Infrastructure.Codex;
using Aevatar.VibeResearching.UserProviders.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.VibeResearching.HttpApi.Host.HostedServices;

/// <summary>
/// Listens on port 1455 for Codex OAuth callbacks.
/// OpenAI's registered redirect_uri is http://localhost:1455/auth/callback.
/// After receiving the callback, exchanges the code for tokens and redirects the browser to the frontend.
/// </summary>
public sealed class CodexCallbackListenerService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly CodexOAuthOptions _options;
    private readonly ILogger<CodexCallbackListenerService> _logger;

    public CodexCallbackListenerService(
        IServiceProvider services,
        IOptions<CodexOAuthOptions> options,
        ILogger<CodexCallbackListenerService> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var prefix = $"http://localhost:{_options.CallbackPort}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
            _logger.LogInformation(
                "Codex OAuth callback listener started on port {Port}", _options.CallbackPort);
        }
        catch (HttpListenerException ex)
        {
            _logger.LogError(ex,
                "Failed to bind Codex callback listener on port {Port}. " +
                "Port may already be in use (EADDRINUSE). Codex OAuth will not work.",
                _options.CallbackPort);
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var getContextTask = listener.GetContextAsync();
                var completed = await Task.WhenAny(
                    getContextTask,
                    Task.Delay(Timeout.Infinite, stoppingToken));

                if (completed != getContextTask)
                    break;

                var context = await getContextTask;
                _ = HandleRequestAsync(context, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (ObjectDisposedException)
        {
            // Listener was stopped
        }
        finally
        {
            listener.Stop();
            listener.Close();
            _logger.LogInformation("Codex OAuth callback listener stopped.");
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Only handle GET /auth/callback
            if (request.HttpMethod != "GET" ||
                !request.Url!.AbsolutePath.Equals(_options.CallbackPath, StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = 404;
                await WriteHtmlAsync(response, "<h1>Not Found</h1>");
                return;
            }

            var query = HttpUtility.ParseQueryString(request.Url.Query);
            var code = query["code"];
            var state = query["state"];
            var error = query["error"];

            // Handle OAuth error response
            if (!string.IsNullOrEmpty(error))
            {
                var errorDesc = query["error_description"] ?? error;
                _logger.LogWarning("Codex OAuth returned error: {Error} — {Description}", error, errorDesc);
                await RedirectToFrontendAsync(response, "error", null, errorDesc);
                return;
            }

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            {
                _logger.LogWarning("Codex OAuth callback missing code or state.");
                await RedirectToFrontendAsync(response, "error", null, "Missing authorization code or state.");
                return;
            }

            await ProcessCallbackAsync(response, code, state, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Codex OAuth callback.");
            try
            {
                await RedirectToFrontendAsync(response, "error", null, "Internal error during OAuth callback.");
            }
            catch
            {
                // Best-effort
            }
        }
        finally
        {
            response.Close();
        }
    }

    private async Task ProcessCallbackAsync(
        HttpListenerResponse response, string code, string state, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var stateStore = scope.ServiceProvider.GetRequiredService<PkceStateStore>();
        var oauthService = scope.ServiceProvider.GetRequiredService<ICodexOAuthService>();

        // Peek at state to get userId (without consuming it — HandleCallbackAsync will consume)
        var entry = await stateStore.PeekAsync(state, ct);
        if (entry is null)
        {
            _logger.LogWarning("Codex OAuth callback: state not found or expired.");
            await RedirectToFrontendAsync(response, "error", null, "OAuth state expired. Please try again.");
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

        var redirectUri = $"http://localhost:{_options.CallbackPort}{_options.CallbackPath}";
        var result = await oauthService.HandleCallbackAsync(
            entry.UserId, code, state, redirectUri, timeoutCts.Token);

        _logger.LogInformation(
            "Codex OAuth callback completed for user {UserId}, status={Status}",
            entry.UserId, result.Status);

        await RedirectToFrontendAsync(response, result.Status, result.Email, null);
    }

    private async Task RedirectToFrontendAsync(
        HttpListenerResponse response, string status, string? email, string? message)
    {
        var redirectUrl = $"{_options.FrontendOrigin}/codex/callback?status={Uri.EscapeDataString(status)}";

        if (!string.IsNullOrEmpty(email))
            redirectUrl += $"&email={Uri.EscapeDataString(email)}";

        if (!string.IsNullOrEmpty(message))
            redirectUrl += $"&message={Uri.EscapeDataString(message)}";

        var html = BuildRedirectHtml(redirectUrl);
        response.StatusCode = 200;
        await WriteHtmlAsync(response, html);
    }

    private static string BuildRedirectHtml(string redirectUrl)
    {
        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="utf-8">
                <title>Connecting...</title>
                <style>
                    body {
                        background: #0a0a0f;
                        color: #e0e0e0;
                        font-family: system-ui, -apple-system, sans-serif;
                        display: flex;
                        align-items: center;
                        justify-content: center;
                        min-height: 100vh;
                        margin: 0;
                    }
                    .card {
                        text-align: center;
                        padding: 2rem;
                    }
                    .spinner {
                        width: 40px; height: 40px;
                        border: 3px solid rgba(0,255,200,0.2);
                        border-top-color: #00ffc8;
                        border-radius: 50%;
                        animation: spin 0.8s linear infinite;
                        margin: 0 auto 1rem;
                    }
                    @keyframes spin { to { transform: rotate(360deg); } }
                </style>
            </head>
            <body>
                <div class="card">
                    <div class="spinner"></div>
                    <p>Redirecting back to Aevatar...</p>
                </div>
                <script>window.location.replace("{{redirectUrl}}");</script>
            </body>
            </html>
            """;
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response, string html)
    {
        response.ContentType = "text/html; charset=utf-8";
        var buffer = System.Text.Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
    }
}
