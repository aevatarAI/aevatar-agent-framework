import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { HttpClient } from "./http-client.js";
import { getServiceBaseUrl } from "./config.js";
import { registerDagTools } from "./services/dag/index.js";
import type { SisyphusConfig } from "./config.js";

/**
 * Creates and starts the MCP server.
 * Returns an async cleanup function for graceful shutdown.
 */
export async function startServer(
  config: SisyphusConfig,
): Promise<() => Promise<void>> {
  const server = new McpServer({
    name: "sisyphus",
    version: "1.0.0",
  });

  // Create HttpClient per configured service
  const dagBaseUrl = getServiceBaseUrl(config, "dag");
  const dagClient = new HttpClient({ baseUrl: dagBaseUrl });

  // Register tools for each service
  registerDagTools(server, dagClient);

  // Connect to stdio transport
  const transport = new StdioServerTransport();
  await server.connect(transport);

  // Return cleanup function
  return async () => {
    await server.close();
  };
}
