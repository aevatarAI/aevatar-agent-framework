using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aevatar.Platform.Server.Endpoints;

namespace Aevatar.Platform.Cli.Commands;

public static partial class RootCommands
{
    private static partial class Handlers
    {
        public static async Task AttachAsync(
            InvocationContext ctx,
            string? target,
            string? sessionId,
            string? workingDir,
            string? token)
        {
            ApplyConfigOverrides(ctx.ParseResult);

            if (!TryApplyWorkingDirectory(workingDir, out var error))
            {
                ctx.Console.Error.WriteLine($"Invalid working directory: {error}");
                return;
            }

            var baseUrl = NormalizeBaseUrl(target ?? "http://127.0.0.1:5678");
            var client = CreateHttpClient(baseUrl, token);
            var resolvedSession = await EnsureRemoteSessionAsync(ctx, client, sessionId, ctx.GetCancellationToken());

            if (string.IsNullOrWhiteSpace(resolvedSession))
            {
                ctx.Console.Error.WriteLine("Failed to attach to remote session.");
                return;
            }

            ctx.Console.WriteLine($"Attached to {baseUrl} (session {resolvedSession})");
            ctx.Console.WriteLine("Type /exit to detach.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.GetCancellationToken());
            var streamTask = Task.Run(() => StreamRemoteEventsAsync(client, resolvedSession, cts.Token), cts.Token);

            while (!cts.IsCancellationRequested)
            {
                var line = Console.ReadLine();
                if (line == null)
                    break;

                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;

                if (trimmed.Equals("/exit", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("/quit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                await PostRemoteMessageAsync(client, resolvedSession, trimmed, Array.Empty<string>(), cts.Token);
            }

            cts.Cancel();
            try { await streamTask; } catch { }
        }

        private static async Task<RunResult> RunRemoteAsync(
            InvocationContext ctx,
            RunOptions options,
            string baseUrl,
            CancellationToken ct)
        {
            var client = CreateHttpClient(baseUrl, Environment.GetEnvironmentVariable("AEVATAR_SERVER_AUTH_TOKEN"));
            var beforeSeq = await GetLatestRemoteSeqAsync(client, options.SessionId, ct);
            var sessionId = await EnsureRemoteSessionAsync(ctx, client, options.SessionId, ct, options);
            if (string.IsNullOrWhiteSpace(sessionId))
                return new RunResult(false, string.Empty, string.Empty, string.Empty, "session_not_found");

            var attachments = options.Files.Select(f => f.Trim()).Where(f => f.Length > 0).ToArray();
            await PostRemoteMessageAsync(client, sessionId, options.Prompt ?? string.Empty, attachments, ct);

            var output = await WaitForRemoteOutputAsync(client, sessionId, beforeSeq, ct);
            var ok = !string.IsNullOrWhiteSpace(output);
            return new RunResult(ok, sessionId, string.Empty, output ?? string.Empty, ok ? null : "no_output");
        }

        private static string? ResolveAttachUrl(RunOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.AttachUrl))
                return NormalizeBaseUrl(options.AttachUrl);

            if (!string.IsNullOrWhiteSpace(options.Hostname) || options.Port.HasValue)
            {
                var host = string.IsNullOrWhiteSpace(options.Hostname) ? "127.0.0.1" : options.Hostname.Trim();
                var port = options.Port ?? 5678;
                if (port == 5000)
                    port = 5678;
                return $"http://{host}:{port}";
            }

            return null;
        }

        private static string NormalizeBaseUrl(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length == 0)
                return "http://127.0.0.1:5678";

            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return trimmed.TrimEnd('/');

            return $"http://{trimmed.TrimEnd('/')}";
        }

        private static HttpClient CreateHttpClient(string baseUrl, string? token)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(30)
            };

            var auth = (token ?? string.Empty).Trim();
            if (auth.Length > 0)
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth);

            return client;
        }

        private static async Task<string?> EnsureRemoteSessionAsync(
            InvocationContext ctx,
            HttpClient client,
            string? sessionId,
            CancellationToken ct,
            RunOptions? options = null)
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var response = await client.GetAsync($"/api/sessions/{sessionId}", ct);
                if (response.IsSuccessStatusCode)
                    return sessionId;
            }

            var continueFlag = options?.Continue ?? ctx.ParseResult.GetValueForOption(RootOptions.Continue);
            if (continueFlag)
            {
                var list = await client.GetFromJsonAsync<List<SessionSummaryDto>>("/api/sessions", ct);
                var latest = list?.OrderByDescending(x => x.LastActivityUtc ?? DateTime.MinValue).FirstOrDefault();
                if (latest != null)
                    return latest.SessionId;
            }

            var profile = options?.Agent ?? ResolveProfile(ctx.ParseResult);
            var provider = options?.Provider ?? ctx.ParseResult.GetValueForOption(RootOptions.Provider);
            var workflow = options?.Workflow ?? ctx.ParseResult.GetValueForOption(RootOptions.Workflow);
            var model = options?.Model ?? ctx.ParseResult.GetValueForOption(RootOptions.Model);
            var workingDir = options?.WorkingDirectory ?? ctx.ParseResult.GetValueForOption(RootOptions.WorkingDirectory);

            var request = new SessionsEndpoints.CreateSessionRequest(
                Profile: profile,
                Provider: provider,
                Workflow: workflow,
                Model: model,
                WorkingDirectory: workingDir);

            var result = await client.PostAsJsonAsync("/api/sessions", request, ct);
            if (!result.IsSuccessStatusCode)
                return null;

            var payload = await result.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (payload.TryGetProperty("session_id", out var id))
                return id.GetString();

            return null;
        }

        private static async Task PostRemoteMessageAsync(
            HttpClient client,
            string sessionId,
            string text,
            IReadOnlyList<string> files,
            CancellationToken ct)
        {
            var request = new SessionsEndpoints.UserMessageRequest(text, files.ToList());
            await client.PostAsJsonAsync($"/api/sessions/{sessionId}/messages", request, ct);
        }

        private static async Task<string?> WaitForRemoteOutputAsync(
            HttpClient client,
            string sessionId,
            ulong beforeSeq,
            CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                string json;
                try
                {
                    json = await client.GetStringAsync($"/api/sessions/{sessionId}/events", ct);
                }
                catch
                {
                    await Task.Delay(500, ct);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    await Task.Delay(500, ct);
                    continue;
                }

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return null;

                var events = doc.RootElement.EnumerateArray()
                    .Select(e => new { Seq = TryGetSeq(e), Data = e })
                    .Where(e => e.Seq.HasValue && e.Seq.Value > beforeSeq)
                    .OrderBy(e => e.Seq!.Value);

                foreach (var item in events)
                {
                    if (item.Data.TryGetProperty("agent_output_delta", out var deltaObj) &&
                        deltaObj.TryGetProperty("delta", out var deltaValue))
                    {
                        return deltaValue.GetString();
                    }
                }

                await Task.Delay(500, ct);
            }

            return null;
        }

        private static async Task<ulong> GetLatestRemoteSeqAsync(
            HttpClient client,
            string? sessionId,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return 0;

            string json;
            try
            {
                json = await client.GetStringAsync($"/api/sessions/{sessionId}/events", ct);
            }
            catch
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(json))
                return 0;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return 0;

            var max = 0UL;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var seq = TryGetSeq(item);
                if (seq.HasValue && seq.Value > max)
                    max = seq.Value;
            }

            return max;
        }

        private static ulong? TryGetSeq(JsonElement element)
        {
            if (!element.TryGetProperty("seq", out var seq))
                return null;

            if (seq.ValueKind == JsonValueKind.Number && seq.TryGetUInt64(out var numeric))
                return numeric;

            if (seq.ValueKind == JsonValueKind.String && ulong.TryParse(seq.GetString(), out var parsed))
                return parsed;

            return null;
        }

        private static async Task StreamRemoteEventsAsync(HttpClient client, string sessionId, CancellationToken ct)
        {
            using var stream = await client.GetStreamAsync($"/api/sessions/{sessionId}/stream", ct);
            using var reader = new StreamReader(stream);

            string? eventName = null;
            var dataBuilder = new StringBuilder();

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line == null)
                    break;

                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    eventName = line[6..].Trim();
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    dataBuilder.Append(line[5..].Trim());
                    continue;
                }

                if (line.Length == 0 && eventName != null)
                {
                    RenderServerEvent(eventName, dataBuilder.ToString());
                    eventName = null;
                    dataBuilder.Clear();
                }
            }
        }

        private static void RenderServerEvent(string eventName, string data)
        {
            if (eventName == "event")
            {
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    if (doc.RootElement.TryGetProperty("agent_output_delta", out var delta) &&
                        delta.TryGetProperty("delta", out var deltaText))
                    {
                        Console.WriteLine(deltaText.GetString());
                        return;
                    }
                }
                catch
                {
                    // ignore parse errors
                }
            }
        }
    }
}
