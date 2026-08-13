/** @type {import('@hey-api/openapi-ts').UserConfig} */
export default {
  input: './contracts/player-equipment-grants.openapi.json',
  output: {
    clean: true,
    entryFile: false,
    path: './src/generated/player-equipment-grants',
    postProcess: ['oxfmt'],
    tsConfigPath: './tsconfig.json',
  },
  plugins: ['@hey-api/typescript'],
};
