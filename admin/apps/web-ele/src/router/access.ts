import type { Router } from 'vue-router';

import type {
  ComponentRecordType,
  GenerateMenuAndRoutesOptions,
} from '@vben/types';

import { generateAccessible } from '@vben/access';
import { preferences } from '@vben/preferences';
import { resetStaticRoutes } from '@vben/utils';

import { BasicLayout, IFrameView } from '#/layouts';
import { accessRoutes, routes } from '#/router/routes';
import { filterAdminAccessRoutes } from '#/router/routes/modules/admin-access';

const forbiddenComponent = () => import('#/views/_core/fallback/forbidden.vue');

async function generateAccess(options: GenerateMenuAndRoutesOptions) {
  const pageMap: ComponentRecordType = import.meta.glob('../views/**/*.vue');
  const layoutMap: ComponentRecordType = { BasicLayout, IFrameView };
  const routes = filterAdminAccessRoutes(options.routes, options.roles ?? []);

  return generateAccessible(preferences.app.accessMode, {
    ...options,
    forbiddenComponent,
    layoutMap,
    pageMap,
    routes,
  });
}

function resetAccessibleRoutes(router: Router) {
  resetStaticRoutes(router, routes);
}

async function refreshAccessibleRoutes(
  router: Router,
  roles: readonly string[],
) {
  resetAccessibleRoutes(router);
  return generateAccess({
    roles: [...roles],
    router,
    routes: accessRoutes,
  });
}

export { generateAccess, refreshAccessibleRoutes, resetAccessibleRoutes };
