import { describe, expect, it, vi } from 'vitest';

const state = vi.hoisted(() => {
  const namespace = 'swordmaster-admin-test-main';

  return {
    bootstrap: vi.fn(async () => {}),
    createStorageNamespace: vi.fn(() => namespace),
    initPreferences: vi.fn(async () => {}),
    namespace,
    unmountGlobalLoading: vi.fn(),
  };
});

vi.mock('@vben/preferences', () => ({
  defineOverridesPreferences: (preferences: unknown) => preferences,
  initPreferences: state.initPreferences,
}));

vi.mock('@vben/utils', () => ({
  unmountGlobalLoading: state.unmountGlobalLoading,
}));

vi.mock('./bootstrap', () => ({
  bootstrap: state.bootstrap,
}));

vi.mock('./storage-namespace', () => ({
  createStorageNamespace: state.createStorageNamespace,
}));

describe('SwordMaster admin application initialization', () => {
  it('uses one storage namespace for preferences and store bootstrap', async () => {
    await import('./main');

    await vi.waitFor(() => {
      expect(state.unmountGlobalLoading).toHaveBeenCalledOnce();
    });

    expect(state.createStorageNamespace).toHaveBeenCalledOnce();
    expect(state.initPreferences).toHaveBeenCalledWith(
      expect.objectContaining({ namespace: state.namespace }),
    );
    expect(state.bootstrap).toHaveBeenCalledWith(state.namespace);
  });
});
