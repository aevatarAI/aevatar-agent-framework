import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { HttpClient } from "../../http-client.js";
import { HttpClientError } from "../../http-client.js";
import { mcpText, mcpError } from "../../types.js";
import { HealthInputSchema } from "./schemas.js";

/**
 * Registers the maker_health tool on the MCP server.
 * Maps to GET /health on the sisyphus-maker service.
 */
export function registerMakerHealthTool(
  server: McpServer,
  client: HttpClient,
): void {
  server.registerTool(
    "maker_health",
    {
      title: "Maker Health Check",
      description:
        "Checks the sisyphus-maker service health. Returns status, service name, " +
        "execution engine identifier, and current UTC timestamp.",
      inputSchema: HealthInputSchema,
      annotations: {
        readOnlyHint: true,
        idempotentHint: true,
      },
    },
    async () => {
      try {
        const result = await client.get<unknown>("/health");
        return mcpText(JSON.stringify(result, null, 2));
      } catch (error) {
        if (error instanceof HttpClientError) {
          return mcpError(error.message);
        }
        return mcpError(`Unexpected error: ${(error as Error).message}`);
      }
    },
  );
}
