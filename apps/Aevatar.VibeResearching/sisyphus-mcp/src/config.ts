import { readFileSync } from "node:fs";

/** Configuration for a single microservice. */
export interface ServiceConfig {
  baseUrl: string;
}

/** Top-level configuration structure. */
export interface SisyphusConfig {
  services: Record<string, ServiceConfig>;
}

/** Built-in defaults for known services. */
const DEFAULTS: Record<string, ServiceConfig> = {
  dag: { baseUrl: "http://localhost:8080" },
  maker: { baseUrl: "http://localhost:8081" },
};

/** Environment variable prefix/suffix pattern for service base URLs. */
const ENV_PREFIX = "SISYPHUS_";
const ENV_SUFFIX = "_BASE_URL";

/**
 * Loads configuration with the following priority (highest wins):
 * 1. Environment variable: SISYPHUS_{SERVICE_NAME}_BASE_URL
 * 2. Config file value
 * 3. Built-in default
 */
export function loadConfig(configPath?: string): SisyphusConfig {
  const config: SisyphusConfig = {
    services: structuredClone(DEFAULTS),
  };

  const resolvedPath = configPath ?? "./sisyphus-mcp.config.json";
  const isExplicit = configPath !== undefined;
  const fileConfig = tryReadConfigFile(resolvedPath, isExplicit);

  if (fileConfig?.services) {
    for (const [name, svc] of Object.entries(fileConfig.services)) {
      config.services[name] = { ...config.services[name], ...svc };
    }
  }

  applyEnvOverrides(config);
  scanEnvForNewServices(config);

  return config;
}

/**
 * Returns the base URL for a named service.
 * Throws if the service is not configured.
 */
export function getServiceBaseUrl(
  config: SisyphusConfig,
  serviceName: string,
): string {
  const svc = config.services[serviceName];
  if (!svc) {
    throw new Error(`Service "${serviceName}" is not configured.`);
  }
  return svc.baseUrl;
}

/**
 * Attempts to read and parse a JSON config file.
 * If the path was explicitly provided and the file is missing or invalid, throws.
 * If using the default path and the file is missing, logs a warning and returns null.
 * If using the default path and the file has invalid JSON, throws.
 */
function tryReadConfigFile(
  path: string,
  isExplicit: boolean,
): SisyphusConfig | null {
  let raw: string;
  try {
    raw = readFileSync(path, "utf-8");
  } catch {
    if (isExplicit) {
      throw new Error(`Config file not found: ${path}`);
    }
    console.error(
      `[sisyphus-mcp] Config file not found at default path: ${path}. Using defaults.`,
    );
    return null;
  }

  try {
    return JSON.parse(raw) as SisyphusConfig;
  } catch (err) {
    throw new Error(
      `Invalid JSON in config file ${path}: ${(err as Error).message}`,
    );
  }
}

/** Applies environment variable overrides for services already in config. */
function applyEnvOverrides(config: SisyphusConfig): void {
  for (const name of Object.keys(config.services)) {
    const envKey = `${ENV_PREFIX}${name.toUpperCase()}${ENV_SUFFIX}`;
    const envVal = process.env[envKey];
    if (envVal && envVal.trim() !== "") {
      config.services[name] = {
        ...config.services[name],
        baseUrl: envVal.trim(),
      };
    }
  }
}

/** Scans all env vars for SISYPHUS_*_BASE_URL to discover services not yet in config. */
function scanEnvForNewServices(config: SisyphusConfig): void {
  const pattern = new RegExp(`^${ENV_PREFIX}(.+)${ENV_SUFFIX}$`);
  for (const key of Object.keys(process.env)) {
    const match = pattern.exec(key);
    if (!match) continue;

    const serviceName = match[1].toLowerCase();
    if (config.services[serviceName]) continue;

    const envVal = process.env[key];
    if (envVal && envVal.trim() !== "") {
      config.services[serviceName] = { baseUrl: envVal.trim() };
    }
  }
}
