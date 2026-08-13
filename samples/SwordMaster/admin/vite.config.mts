import process from 'node:process';

import { defineConfig } from '@vben/vite-config';

import basicSsl from '@vitejs/plugin-basic-ssl';
import ElementPlus from 'unplugin-element-plus/vite';
import { loadEnv } from 'vite';

export default defineConfig(async (config) => {
  const env = loadEnv(config?.mode ?? 'development', process.cwd(), '');
  const adminPort = Number(
    process.env.SWORDMASTER_ADMIN_PORT || env.VITE_PORT || '5778',
  );
  if (!Number.isInteger(adminPort) || adminPort < 1 || adminPort > 65_535) {
    throw new Error(
      'SwordMaster admin port must be an integer from 1 to 65535.',
    );
  }

  const apiTarget =
    process.env.SWORDMASTER_API_URL ||
    env.VITE_SWORDMASTER_API_URL ||
    'http://127.0.0.1:5000';

  return {
    application: {
      injectGlobalScss: false,
      injectMetadata: false,
      nitroMock: false,
    },
    vite: {
      plugins: [
        ...(env.VITE_HTTPS === 'false'
          ? []
          : [basicSsl({ name: 'SwordMaster Operations' })]),
        ElementPlus({ format: 'esm' }),
      ],
      server: {
        host: env.VITE_HOST || '127.0.0.1',
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
