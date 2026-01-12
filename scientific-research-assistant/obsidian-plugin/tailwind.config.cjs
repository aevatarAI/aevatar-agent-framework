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
  // IMPORTANT: scope all utility selectors to the plugin root container
  // (so both Panel + Workbench can share the same Tailwind-based design language)
  important: ".aevatar-sra",
  // Avoid global resets inside Obsidian (preflight is global and cannot be scoped)
  corePlugins: {
    preflight: false,
  },
};


