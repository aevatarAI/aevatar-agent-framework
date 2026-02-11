import { loadConfig } from "./config.js";
import { startServer } from "./server.js";

/** Entry point: loads config, starts the MCP server, and registers shutdown handlers. */
async function main(): Promise<void> {
  const configPath = parseConfigArg(process.argv);
  const config = loadConfig(configPath);
  const cleanup = await startServer(config);

  const shutdown = async () => {
    await cleanup();
    process.exit(0);
  };

  process.on("SIGINT", shutdown);
  process.on("SIGTERM", shutdown);
}

/** Extracts the --config file path from CLI arguments, or returns undefined. */
function parseConfigArg(argv: string[]): string | undefined {
  const idx = argv.indexOf("--config");
  if (idx !== -1 && idx + 1 < argv.length) {
    return argv[idx + 1];
  }
  return undefined;
}

main().catch((err: Error) => {
  console.error("Fatal error:", err.message);
  process.exit(1);
});
