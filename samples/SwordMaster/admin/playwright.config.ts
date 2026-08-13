import { defineConfig } from '@playwright/test';

import { resolve } from 'node:path';

const baseURL = process.env.SWORDMASTER_ACCEPTANCE_BASE_URL;
const outputDir = process.env.SWORDMASTER_ACCEPTANCE_OUTPUT_DIR;
const resultPath = process.env.SWORDMASTER_ACCEPTANCE_RESULT_PATH;

if (!baseURL || !outputDir || !resultPath) {
  throw new Error(
    'SWORDMASTER_ACCEPTANCE_BASE_URL, SWORDMASTER_ACCEPTANCE_OUTPUT_DIR, and SWORDMASTER_ACCEPTANCE_RESULT_PATH are required for SwordMaster browser acceptance.',
  );
}

export default defineConfig({
  expect: {
    timeout: 15_000,
  },
  forbidOnly: true,
  fullyParallel: false,
  outputDir: resolve(outputDir),
  reporter: [['list'], ['json', { outputFile: resolve(resultPath) }]],
  retries: 0,
  testDir: './e2e',
  testMatch: 'player-equipment-grants.spec.ts',
  timeout: 180_000,
  use: {
    baseURL,
    channel: (process.env.SWORDMASTER_ACCEPTANCE_BROWSER_CHANNEL ??
      'msedge') as 'chrome' | 'msedge',
    ignoreHTTPSErrors: true,
    screenshot: 'only-on-failure',
    trace: 'off',
    video: 'off',
    viewport: { height: 900, width: 1440 },
  },
  workers: 1,
});
