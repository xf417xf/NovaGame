import process from 'node:process';

/** @type {import('@hey-api/openapi-ts').UserConfig} */
export default {
  input:
    process.env.NOVAGAME_ADMIN_ACCESS_OPENAPI_INPUT ??
    './contracts/admin-access.openapi.json',
  output: {
    clean: true,
    entryFile: false,
    path:
      process.env.NOVAGAME_ADMIN_ACCESS_TYPES_OUTPUT ??
      './src/generated/admin-access',
    postProcess: ['oxfmt'],
    tsConfigPath: './tsconfig.json',
  },
  plugins: ['@hey-api/typescript'],
};
