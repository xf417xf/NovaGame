import { defineConfig } from '@vben/oxfmt-config';

export default defineConfig({
  ignorePatterns: [
    'contracts/**',
    'dist/**',
    'node_modules/**',
    'src/generated/**',
  ],
});
