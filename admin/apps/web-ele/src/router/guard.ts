import type { Router } from 'vue-router';

import { LOGIN_PATH } from '@vben/constants';
import { preferences } from '@vben/preferences';
import { useAccessStore, useUserStore } from '@vben/stores';
import { startProgress, stopProgress } from '@vben/utils';

import { coreRouteNames } from '#/router/routes';
import { useAuthStore } from '#/store';

import { refreshAccessibleRoutes } from './access';

function setupCommonGuard(router: Router) {
  const loadedPaths = new Set<string>();

  router.beforeEach((to) => {
    to.meta.loaded = loadedPaths.has(to.path);
    if (!to.meta.loaded && preferences.transition.progress) {
      startProgress();
    }
    return true;
  });

  router.afterEach((to) => {
    loadedPaths.add(to.path);
    if (preferences.transition.progress) {
      stopProgress();
    }
  });
}

function setupAccessGuard(router: Router) {
  router.beforeEach(async (to, from) => {
    const accessStore = useAccessStore();
    const userStore = useUserStore();
    const authStore = useAuthStore();

    if (coreRouteNames.includes(to.name as string)) {
      if (to.path === LOGIN_PATH && accessStore.accessToken) {
        return decodeURIComponent(
          (to.query.redirect as string) ||
            userStore.userInfo?.homePath ||
            preferences.app.defaultHomePath,
        );
      }
      return true;
    }

    if (!accessStore.accessToken) {
      if (to.meta.ignoreAccess) {
        return true;
      }

      if (await authStore.restoreSession()) {
        return { ...router.resolve(to.fullPath), replace: true };
      }

      return {
        path: LOGIN_PATH,
        query:
          to.fullPath === preferences.app.defaultHomePath
            ? {}
            : { redirect: encodeURIComponent(to.fullPath) },
        replace: true,
      };
    }

    if (accessStore.isAccessChecked) {
      return true;
    }

    const userInfo = userStore.userInfo || (await authStore.fetchUserInfo());
    const { accessibleMenus, accessibleRoutes } = await refreshAccessibleRoutes(
      router,
      accessStore.accessCodes,
    );

    accessStore.setAccessMenus(accessibleMenus);
    accessStore.setAccessRoutes(accessibleRoutes);
    accessStore.setIsAccessChecked(true);

    const redirectPath = (from.query.redirect ??
      (to.path === preferences.app.defaultHomePath
        ? userInfo.homePath || preferences.app.defaultHomePath
        : to.fullPath)) as string;

    return {
      ...router.resolve(decodeURIComponent(redirectPath)),
      replace: true,
    };
  });
}

function createRouterGuard(router: Router) {
  setupCommonGuard(router);
  setupAccessGuard(router);
}

export { createRouterGuard };
