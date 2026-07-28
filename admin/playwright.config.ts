import { defineConfig } from '@playwright/test';

const baseURL = process.env.NOVAGAME_E2E_BASE_URL || 'https://localhost:5777';
const browserChannel = process.env.NOVAGAME_E2E_BROWSER_CHANNEL || 'msedge';

export default defineConfig({
  expect: {
    timeout: 10_000,
  },
  forbidOnly: Boolean(process.env.CI),
  fullyParallel: false,
  outputDir: 'output/playwright/results',
  reporter: [
    ['list'],
    ['html', { open: 'never', outputFolder: 'output/playwright/report' }],
  ],
  retries: 0,
  testDir: './apps/web-ele/e2e',
  timeout: 90_000,
  use: {
    baseURL,
    channel: browserChannel as 'chrome' | 'msedge',
    ignoreHTTPSErrors: true,
    screenshot: 'only-on-failure',
    trace: 'off',
    video: 'off',
    viewport: { height: 900, width: 1440 },
  },
  webServer: {
    command: 'pnpm run dev:ele',
    ignoreHTTPSErrors: true,
    reuseExistingServer: false,
    stderr: 'pipe',
    stdout: 'pipe',
    timeout: 120_000,
    url: baseURL,
  },
  workers: 1,
});
