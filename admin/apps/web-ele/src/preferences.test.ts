import { StorageManager } from '@vben/utils';

import { afterEach, describe, expect, it } from 'vitest';

import { overridesPreferences, resolveInitialAdminLocale } from './preferences';

const namespace = 'novagame-locale-test';
const cache = new StorageManager({ prefix: namespace });

afterEach(async () => {
  await cache.clear();
});

describe('admin locale restoration', () => {
  it('keeps English as the fresh-browser default and isolates storage namespaces', async () => {
    await cache.setItem('preferences-locale', 'zh-CN');
    expect(await resolveInitialAdminLocale(`${namespace}-different`)).toBe(
      'en-US',
    );
    expect(overridesPreferences.app?.enablePreferences).toBe(false);
  });

  it.each(['en-US', 'zh-CN'] as const)(
    'restores the selected %s locale at startup',
    async (locale) => {
      await cache.setItem('preferences-locale', locale);
      expect(await resolveInitialAdminLocale(namespace)).toBe(locale);
    },
  );

  it('falls back to English for corrupt or unsupported cached values', async () => {
    await cache.setItem('preferences-locale', 'unsupported-language');
    expect(await resolveInitialAdminLocale(namespace)).toBe('en-US');
    localStorage.setItem(`${namespace}-preferences-locale`, '{corrupt');
    expect(await resolveInitialAdminLocale(namespace)).toBe('en-US');
  });
});
