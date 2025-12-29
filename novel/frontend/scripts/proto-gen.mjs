import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, rmSync } from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

// ============================================================
//  Protobuf TS Codegen (protoc-gen-es)
//
//  GOAL:
//  - UI <-> Sidecar is a boundary.
//  - Cross-boundary messages MUST be Protobuf-defined.
//  - Sidecar uses Protobuf JSON (C# JsonFormatter); protobuf-es can parse it.
//
//  INPUT:
//  - novel/protos/*.proto
//
//  OUTPUT:
//  - novel/frontend/src/gen/*.ts  (generated; gitignored)
// ============================================================

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const frontendDir = path.resolve(__dirname, "..");
const protoDir = path.resolve(frontendDir, "..", "protos");
const outDir = path.resolve(frontendDir, "src", "gen");

function findGoogleProtoIncludeDir() {
  const env = process.env.PROTOC_INCLUDE;
  if (env && existsSync(path.join(env, "google", "protobuf", "timestamp.proto"))) {
    return env;
  }

  const candidates = [
    "/opt/homebrew/opt/protobuf@3/include",
    "/opt/homebrew/opt/protobuf/include",
    "/usr/local/include",
    "/usr/include",
  ];

  for (const dir of candidates) {
    if (existsSync(path.join(dir, "google", "protobuf", "timestamp.proto"))) {
      return dir;
    }
  }
  return null;
}

function resolveProtocGenEsBinary() {
  const binName = process.platform === "win32" ? "protoc-gen-es.cmd" : "protoc-gen-es";
  return path.resolve(frontendDir, "node_modules", ".bin", binName);
}

function main() {
  if (!existsSync(protoDir)) {
    console.error(`[proto:gen] protoDir not found: ${protoDir}`);
    process.exit(1);
  }

  const plugin = resolveProtocGenEsBinary();
  if (!existsSync(plugin)) {
    console.error(`[proto:gen] protoc-gen-es not found: ${plugin}`);
    console.error(`[proto:gen] Run: npm install`);
    process.exit(1);
  }

  const googleInclude = findGoogleProtoIncludeDir();
  if (!googleInclude) {
    console.error("[proto:gen] Cannot find google/protobuf well-known types include dir.");
    console.error("[proto:gen] Fix options:");
    console.error("- Install protoc (and headers), e.g. on macOS: brew install protobuf@3");
    console.error("- Or export PROTOC_INCLUDE to a folder containing google/protobuf/*.proto");
    process.exit(1);
  }

  // Clean output for determinism.
  if (existsSync(outDir)) {
    rmSync(outDir, { recursive: true, force: true });
  }
  mkdirSync(outDir, { recursive: true });

  const protos = [
    path.join(protoDir, "novel_assets.proto"),
    path.join(protoDir, "novel_pipeline.proto"),
    path.join(protoDir, "novel_sidecar.proto"),
  ];

  for (const p of protos) {
    if (!existsSync(p)) {
      console.error(`[proto:gen] missing proto: ${p}`);
      process.exit(1);
    }
  }

  const args = [
    `-I${protoDir}`,
    `-I${googleInclude}`,
    `--plugin=protoc-gen-es=${plugin}`,
    `--es_out=${outDir}`,
    "--es_opt=target=ts",
    ...protos,
  ];

  console.log("[proto:gen] protoc " + args.join(" "));
  execFileSync("protoc", args, { stdio: "inherit" });
}

main();


