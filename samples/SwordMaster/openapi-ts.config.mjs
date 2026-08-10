/** @type {import('@hey-api/openapi-ts').UserConfig} */
export default {
  input:
    process.env.SWORDMASTER_PLAYER_ASSETS_OPENAPI_INPUT ?? "./contracts/player-assets.openapi.json",
  output: {
    clean: true,
    entryFile: false,
    path: process.env.SWORDMASTER_PLAYER_ASSETS_TYPES_OUTPUT ?? "./src/generated/player-assets",
    postProcess: ["oxfmt"],
    tsConfigPath: "./tsconfig.json",
  },
  plugins: ["@hey-api/typescript"],
};
