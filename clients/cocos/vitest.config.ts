import { fileURLToPath } from "node:url";

import { defineConfig } from "vitest/config";

export default defineConfig({
  resolve: {
    alias: {
      "@novagame/game-config-client": fileURLToPath(
        new URL("./packages/game-config-client/src/index.ts", import.meta.url),
      ),
    },
  },
  test: {
    coverage: {
      enabled: false,
    },
    include: ["packages/**/*.test.ts", "adapters/**/*.test.ts"],
  },
});
