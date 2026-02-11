import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { HttpClient } from "../../http-client.js";
import { HttpClientError } from "../../http-client.js";
import { mcpText, mcpError } from "../../types.js";
import { CreateKnowledgesInputSchema } from "./schemas.js";

/**
 * Registers the dag_create_knowledges tool on the MCP server.
 * Maps to POST /api/knowledges on the DAG service.
 */
export function registerCreateKnowledgesTool(
  server: McpServer,
  client: HttpClient,
): void {
  server.registerTool(
    "dag_create_knowledges",
    {
      title: "Create Knowledge Nodes",
      description:
        "Batch-creates knowledge nodes in the DAG and optionally establishes " +
        "dependency edges between them. Returns the list of created node IDs " +
        "in the same order as the input.",
      inputSchema: CreateKnowledgesInputSchema,
      annotations: {
        destructiveHint: false,
        idempotentHint: false,
      },
    },
    async (args) => {
      try {
        const body = {
          knowledgeList: args.knowledgeList,
          indexDependencies: args.indexDependencies,
        };
        const result = await client.post<string[]>("/api/knowledges", body);
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
