import { createApp, watchEffect } from 'vue';

import { registerAccessDirective } from '@vben/access';
import { preferences } from '@vben/preferences';
import { initStores } from '@vben/stores';
import '@vben/styles';
import '@vben/styles/ele';

import { useTitle } from '@vueuse/core';

import { $t, setupI18n } from '#/locales';

import App from './app.vue';
import { router } from './router';

async function bootstrap(namespace: string) {
  const app = createApp(App);

  await setupI18n(app);
  await initStores(app, { namespace });
  registerAccessDirective(app);
  app.use(router);

  watchEffect(() => {
    if (preferences.app.dynamicTitle) {
      const routeTitle = router.currentRoute.value.meta?.title;
      const pageTitle =
        (routeTitle ? `${$t(routeTitle)} - ` : '') + preferences.app.name;
      useTitle(pageTitle);
    }
  });

  app.mount('#app');
}

export { bootstrap };
