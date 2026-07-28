import process from 'node:process';

import { defineConfig } from '@vben/vite-config';

import basicSsl from '@vitejs/plugin-basic-ssl';
import ElementPlus from 'unplugin-element-plus/vite';
import { loadEnv } from 'vite';

export default defineConfig(async (config) => {
  const env = loadEnv(config?.mode ?? 'development', process.cwd(), '');
  const adminPort = Number(
    process.env.NOVAGAME_ADMIN_PORT || env.VITE_PORT || '5777',
  );
  const apiTarget =
    process.env.NOVAGAME_API_URL ||
    process.env.VITE_NOVAGAME_API_URL ||
    env.VITE_NOVAGAME_API_URL ||
    'http://127.0.0.1:5000';

  return {
    application: {
      nitroMock: false,
    },
    vite: {
      plugins: [
        ...(env.VITE_HTTPS === 'false'
          ? []
          : [basicSsl({ name: 'NovaGame Operations' })]),
        ElementPlus({
          format: 'esm',
        }),
      ],
      server: {
        host: env.VITE_HOST || 'localhost',
        https: env.VITE_HTTPS === 'false' ? undefined : {},
        port: adminPort,
        proxy: {
          '/api': {
            changeOrigin: true,
            secure: false,
            target: apiTarget,
            xfwd: true,
          },
        },
        strictPort: true,
      },
    },
  };
});
