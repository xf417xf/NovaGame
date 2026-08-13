import { describe, expect, it, vi } from 'vitest';

const state = vi.hoisted(() => {
  const app = {
    mount: vi.fn(),
    use: vi.fn(),
  };

  return {
    app,
    createApp: vi.fn(() => app),
    initStores: vi.fn(async () => {}),
    registerAccessDirective: vi.fn(),
    setupI18n: vi.fn(async () => {}),
  };
});

vi.mock('vue', () => ({
  createApp: state.createApp,
  watchEffect: vi.fn(),
}));

vi.mock('@vben/access', () => ({
  registerAccessDirective: state.registerAccessDirective,
}));

vi.mock('@vben/preferences', () => ({
  preferences: {
    app: {
      dynamicTitle: false,
      name: 'SwordMaster Operations',
    },
  },
}));

vi.mock('@vben/stores', () => ({
  initStores: state.initStores,
}));

vi.mock('@vben/styles', () => ({}));
vi.mock('@vben/styles/ele', () => ({}));

vi.mock('@vueuse/core', () => ({
  useTitle: vi.fn(),
}));

vi.mock('#/locales', () => ({
  $t: (key: string) => key,
  setupI18n: state.setupI18n,
}));

vi.mock('./app.vue', () => ({ default: {} }));

vi.mock('./router', () => ({
  router: {
    currentRoute: { value: { meta: {} } },
  },
}));

import { bootstrap } from './bootstrap';

describe('SwordMaster admin store bootstrap', () => {
  it('passes the application storage namespace to Vben stores unchanged', async () => {
    const namespace = 'swordmaster-admin-test-bootstrap';

    await bootstrap(namespace);

    expect(state.initStores).toHaveBeenCalledWith(state.app, { namespace });
  });
});
