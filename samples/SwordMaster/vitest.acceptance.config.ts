import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    coverage: {
      enabled: false,
    },
    include: ["tests/**/*.acceptance.ts"],
    testTimeout: 30_000,
  },
});
