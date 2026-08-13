import { initPreferences } from '@vben/preferences';
import { unmountGlobalLoading } from '@vben/utils';

import { overridesPreferences } from './preferences';
import { createStorageNamespace } from './storage-namespace';

async function initApplication() {
  const namespace = createStorageNamespace(import.meta.env);

  await initPreferences({
    namespace,
    overrides: overridesPreferences,
  });

  const { bootstrap } = await import('./bootstrap');
  await bootstrap(namespace);
  unmountGlobalLoading();
}

void initApplication();
