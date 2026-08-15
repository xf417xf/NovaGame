import { defineConfig } from '@vben/eslint-config';

export default defineConfig([
  {
    ignores: ['.playwright-report/**', 'output/**'],
  },
  {
    files: ['apps/web-ele/src/generated/admin-access/types.gen.ts'],
    rules: {
      'perfectionist/sort-object-types': 'off',
      'perfectionist/sort-union-types': 'off',
    },
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
