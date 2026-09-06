import { defineConfig } from "vitest/config";
import { fileURLToPath } from "url";
import { dirname, resolve } from "path";

const __dirname = dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  resolve: {
    alias: {
      // Point directly to sdk-core source so tests don't require a prior build.
      "@levelmoment/sdk-core": resolve(__dirname, "../core/src/index.ts"),
      // react-native-keychain ships Flow-annotated JS and needs a native
      // module; neither survives a plain Node transform. See the stub.
      "react-native-keychain": resolve(__dirname, "test/keychainStub.ts"),
    },
  },
  test: {
    environment: "node",
  },
});
