import { describe, expect, it } from 'vitest';

import { createStorageNamespace } from './storage-namespace';
import { overridesPreferences } from './preferences';

describe('SwordMaster admin storage namespace', () => {
  it('isolates development and production state', () => {
    expect(overridesPreferences.app?.defaultHomePath).toBe(
      '/player-equipment-grants',
    );
    expect(
      createStorageNamespace({
        PROD: false,
        VITE_APP_NAMESPACE: 'swordmaster-admin',
        VITE_APP_VERSION: '3.5.0',
      }),
    ).toBe('swordmaster-admin-3.5.0-dev');
    expect(
      createStorageNamespace({
        PROD: true,
        VITE_APP_NAMESPACE: 'swordmaster-admin',
        VITE_APP_VERSION: '3.5.0',
      }),
    ).toBe('swordmaster-admin-3.5.0-prod');
  });

  it('uses a deterministic version when metadata injection is disabled', () => {
    expect(
      createStorageNamespace({
        VITE_APP_NAMESPACE: 'swordmaster-admin',
      }),
    ).toBe('swordmaster-admin-0-dev');
  });

  it('rejects a generic or empty namespace', () => {
    expect(() =>
      createStorageNamespace({ VITE_APP_NAMESPACE: 'vben-admin' }),
    ).toThrow("VITE_APP_NAMESPACE must be 'swordmaster-admin'.");
    expect(() => createStorageNamespace({})).toThrow(
      "VITE_APP_NAMESPACE must be 'swordmaster-admin'.",
    );
  });
});
