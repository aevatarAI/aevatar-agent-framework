/* eslint-disable @typescript-eslint/no-var-requires */

/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./src/**/*.{ts,tsx}",
    // Shared UI core sources (monorepo)
    "../ui/src/**/*.{ts,tsx}",
  ],
  theme: {
    extend: {},
  },
  plugins: [require("@tailwindcss/typography")],
  // IMPORTANT: scope all utility selectors to the workbench view container
  important: ".aevatar-sra-workbench",
  // Avoid global resets inside Obsidian (preflight is global and cannot be scoped)
  corePlugins: {
    preflight: false,
  },
};


