import { defineConfig } from "tsup";

export default defineConfig([
  // Library entry (imported by consumers)
  {
    entry: ["src/index.ts"],
    format: ["esm"],
    dts: true,
    sourcemap: true,
    clean: true,
  },
  // CLI / stdio server entry
  {
    entry: { server: "src/server.ts" },
    format: ["esm"],
    sourcemap: true,
    banner: { js: "#!/usr/bin/env node" },
  },
]);
