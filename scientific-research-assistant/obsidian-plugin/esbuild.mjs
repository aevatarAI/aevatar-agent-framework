import esbuild from "esbuild";
import { copyFile } from "node:fs/promises";
import { resolve } from "node:path";

// ============================================================
//  esbuild config (Obsidian Desktop plugin)
//
//  Output:
//  - dist/main.js  (requested by spec)
//  - main.js       (copied from dist for Obsidian to load)
//
//  Notes:
//  - CommonJS output: Obsidian loads plugins via require().
//  - Desktop-first: uses Node APIs; mobile support is out-of-scope for MVP.
// ============================================================

const watch = process.argv.includes("--watch");

const outdir = resolve(process.cwd(), "dist");
const distMain = resolve(outdir, "main.js");
const rootMain = resolve(process.cwd(), "main.js");

const copyMainPlugin = {
  name: "copy-main",
  setup(build) {
    build.onEnd(async (result) => {
      if (result.errors.length > 0) return;
      try {
        await postBuild();
      } catch {
        // best-effort
      }
    });
  },
};

/** @type {import("esbuild").BuildOptions} */
const buildOptions = {
  entryPoints: {
    main: "src/main.ts",
    testExports: "src/testExports.ts",
  },
  bundle: true,
  platform: "node",
  format: "cjs",
  target: "es2019",
  sourcemap: "inline",
  outdir,
  entryNames: "[name]",
  external: [
    // Provided by Obsidian runtime
    "obsidian",
    "electron",
    // Common externals in Obsidian plugin ecosystem
    "@codemirror/state",
    "@codemirror/view",
    "@codemirror/language",
    "@codemirror/search",
    "@codemirror/autocomplete",
    "@codemirror/commands",
  ],
  logLevel: "info",
  plugins: [copyMainPlugin],
};

async function postBuild() {
  // Keep Obsidian happy: it loads ./main.js from the plugin folder.
  await copyFile(distMain, rootMain);
}

if (watch) {
  const ctx = await esbuild.context(buildOptions);
  await ctx.watch();
  console.log("[esbuild] watching...");
} else {
  await esbuild.build(buildOptions);
  await postBuild();
}


