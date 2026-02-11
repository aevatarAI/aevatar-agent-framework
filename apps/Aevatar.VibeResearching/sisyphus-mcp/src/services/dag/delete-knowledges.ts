import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { HttpClient } from "../../http-client.js";
import { HttpClientError } from "../../http-client.js";
import { mcpText, mcpError } from "../../types.js";
import { DeleteKnowledgesInputSchema } from "./schemas.js";

/**
 * Registers the dag_delete_knowledges tool on the MCP server.
 * Maps to DELETE /api/knowledges on the DAG service.
 */
export function registerDeleteKnowledgesTool(
  server: McpServer,
  client: HttpClient,
): void {
  server.registerTool(
    "dag_delete_knowledges",
    {
      title: "Delete Knowledge Nodes",
      description:
        "Batch-deletes knowledge nodes by ID. Automatically removes all connected " +
        "dependency edges (DETACH DELETE). Idempotent: non-existent IDs are silently skipped.",
      inputSchema: DeleteKnowledgesInputSchema,
      annotations: {
        destructiveHint: true,
        idempotentHint: true,
      },
    },
    async (args) => {
      try {
        // Send the ids array directly as the HTTP body (not wrapped)
        const result = await client.delete<string[]>("/api/knowledges", args.ids);
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
