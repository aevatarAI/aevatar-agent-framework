import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { HttpClient } from "../../http-client.js";
import { HttpClientError } from "../../http-client.js";
import type { SSEEvent } from "../../http-client.js";
import { mcpText, mcpError } from "../../types.js";
import { VerifyInputSchema } from "./schemas.js";

/** Known SSE event types emitted by the maker verify endpoint. */
type MakerEventType = "started" | "phase" | "worker_done" | "vote_round" | "result" | "error";

/**
 * Registers the maker_verify tool on the MCP server.
 * Maps to POST /api/verify on the sisyphus-maker service.
 * Consumes the SSE event stream and returns a single MCP response.
 */
export function registerMakerVerifyTool(
  server: McpServer,
  client: HttpClient,
): void {
  server.registerTool(
    "maker_verify",
    {
      title: "Run Consensus Verification",
      description:
        "Executes consensus verification using the CognitiveStrategy + maker.yaml " +
        "workflow. Multiple LLM workers independently evaluate the prompt, then " +
        "vote using ahead-by-K consensus. Returns the verified result with " +
        "confidence score and full vote breakdown. Can take up to 600 seconds.",
      inputSchema: VerifyInputSchema,
      annotations: {
        readOnlyHint: true,
        idempotentHint: false,
      },
    },
    async (args) => {
      try {
        const body = {
          prompt: args.prompt,
          variables: args.variables,
          config: args.config,
        };

        const events = await client.postSSE("/api/verify", body);
        return formatVerifyResponse(events);
      } catch (error) {
        if (error instanceof HttpClientError) {
          return mcpError(error.message);
        }
        return mcpError(`Unexpected error: ${(error as Error).message}`);
      }
    },
  );
}

/**
 * Processes the collected SSE events into a single MCP text response.
 * Builds a progress summary from intermediate events and extracts the
 * final result or error from the terminal event.
 */
function formatVerifyResponse(events: SSEEvent[]): ReturnType<typeof mcpText> {
  const progressLines: string[] = [];
  let resultJson: string | null = null;
  let errorMessage: string | null = null;

  for (const event of events) {
    const type = event.event as MakerEventType;
    const parsed = tryParseJson(event.data);

    switch (type) {
      case "started":
        progressLines.push(formatStartedEvent(parsed));
        break;
      case "phase":
        progressLines.push(formatPhaseEvent(parsed));
        break;
      case "worker_done":
        progressLines.push(formatWorkerDoneEvent(parsed));
        break;
      case "vote_round":
        progressLines.push(formatVoteRoundEvent(parsed));
        break;
      case "result":
        resultJson = event.data;
        break;
      case "error":
        errorMessage = formatErrorEvent(parsed);
        break;
      // Ignore unknown event types (e.g., "message")
    }
  }

  if (errorMessage) {
    return mcpError(errorMessage);
  }

  if (!resultJson) {
    return mcpError("SSE stream ended unexpectedly without a result or error event.");
  }

  const progress = progressLines.length > 0
    ? `## Verification Progress\n\n${progressLines.map((l) => `- ${l}`).join("\n")}\n\n`
    : "";

  // Pretty-print the result JSON for readability
  const prettyResult = formatResultJson(resultJson);

  return mcpText(`${progress}## Result\n\n${prettyResult}`);
}

/** Attempts to parse a JSON string. Returns the parsed object or null. */
function tryParseJson(data: string): Record<string, unknown> | null {
  try {
    return JSON.parse(data) as Record<string, unknown>;
  } catch {
    return null;
  }
}

/** Formats a "started" SSE event into a progress line. */
function formatStartedEvent(parsed: Record<string, unknown> | null): string {
  if (!parsed) return "[started] (malformed payload)";
  return `[started] jobId=${parsed.jobId ?? "?"}, workers=${parsed.workerCount ?? "?"}`;
}

/** Formats a "phase" SSE event into a progress line. */
function formatPhaseEvent(parsed: Record<string, unknown> | null): string {
  if (!parsed) return "[phase] (malformed payload)";
  return `[phase] ${parsed.phase ?? "?"}: ${parsed.message ?? "?"} (${parsed.progress ?? "?"}%)`;
}

/** Formats a "worker_done" SSE event into a progress line. */
function formatWorkerDoneEvent(parsed: Record<string, unknown> | null): string {
  if (!parsed) return "[worker_done] (malformed payload)";
  const status = parsed.success ? "success" : "failed";
  return `[worker_done] ${parsed.workerId ?? "?"} completed (${status})`;
}

/** Formats a "vote_round" SSE event into a progress line. */
function formatVoteRoundEvent(parsed: Record<string, unknown> | null): string {
  if (!parsed) return "[vote_round] (malformed payload)";
  const consensus = parsed.consensusReached ? "consensus reached" : "no consensus";
  return `[vote_round] Round ${parsed.round ?? "?"}: ${parsed.currentVotes ?? "?"}/${parsed.votesNeeded ?? "?"} votes needed, ${consensus}`;
}

/** Formats an "error" SSE event into an error message string. */
function formatErrorEvent(parsed: Record<string, unknown> | null): string {
  if (!parsed) return "Verification failed (malformed error event).";
  const parts = [String(parsed.error ?? "Unknown verification error")];
  if (parsed.jobId) parts.push(`jobId=${parsed.jobId}`);
  if (parsed.durationMs !== undefined) parts.push(`duration=${parsed.durationMs}ms`);
  return parts.join(" | ");
}

/** Pretty-prints the result JSON, falling back to raw string on parse failure. */
function formatResultJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}
