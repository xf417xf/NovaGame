import { defineConfig } from '@vben/eslint-config';

export default defineConfig([
  {
    ignores: ['.playwright-report/**', 'output/**'],
  },
  {
    files: ['**/*.?([cm])[jt]s?(x)'],
    languageOptions: {
      parserOptions: {
        project: false,
      },
    },
  },
  {
    files: ['pnpm-workspace.yaml'],
    rules: {
      'pnpm/yaml-no-unused-catalog-item': 'off',
    },
  },
]);
