import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { HttpClient } from "../../http-client.js";
import { HttpClientError } from "../../http-client.js";
import { mcpText, mcpError } from "../../types.js";
import { UpdateKnowledgesInputSchema } from "./schemas.js";

/**
 * Registers the dag_update_knowledges tool on the MCP server.
 * Maps to PUT /api/knowledges on the DAG service.
 */
export function registerUpdateKnowledgesTool(
  server: McpServer,
  client: HttpClient,
): void {
  server.registerTool(
    "dag_update_knowledges",
    {
      title: "Update Knowledge Nodes",
      description:
        "Batch-updates existing knowledge nodes. Provide a map of node IDs to " +
        "their new values. Does NOT modify dependency edges.",
      inputSchema: UpdateKnowledgesInputSchema,
      annotations: {
        destructiveHint: false,
        idempotentHint: true,
      },
    },
    async (args) => {
      try {
        // Send the updates map directly as the HTTP body (not wrapped)
        const result = await client.put<string[]>("/api/knowledges", args.updates);
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
