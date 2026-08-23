import type { Router, RouteRecordRaw } from 'vue-router';

import type {
  ComponentRecordType,
  GenerateMenuAndRoutesOptions,
} from '@vben/types';

import { generateAccessible } from '@vben/access';
import { preferences } from '@vben/preferences';
import { resetStaticRoutes } from '@vben/utils';

import { BasicLayout, IFrameView } from '#/layouts';
import { accessRoutes, routes } from '#/router/routes';
import {
  administratorAccessChildRouteNames,
  administratorAccessRouteName,
  filterAdminAccessRoutes,
} from '#/router/routes/modules/admin-access';

const forbiddenComponent = () => import('#/views/_core/fallback/forbidden.vue');

interface RouteRefreshState {
  revision: number;
  tail: Promise<void>;
}

const routeRefreshStates = new WeakMap<Router, RouteRefreshState>();

function getRouteRefreshState(router: Router) {
  let state = routeRefreshStates.get(router);
  if (!state) {
    state = { revision: 0, tail: Promise.resolve() };
    routeRefreshStates.set(router, state);
  }
  return state;
}

function resetRouteRecords(router: Router) {
  resetStaticRoutes(router, routes);
}

function reconcileAdminAccessRoutes(
  router: Router,
  accessibleRoutes: readonly RouteRecordRaw[],
) {
  const adminAccessRoute = accessibleRoutes.find(
    (route) => route.name === administratorAccessRouteName,
  );
  const activeChildRouteNames = new Set(
    adminAccessRoute?.children
      ?.map((child) => child.name)
      .filter((name): name is string => typeof name === 'string'),
  );

  for (const routeName of administratorAccessChildRouteNames) {
    if (!activeChildRouteNames.has(routeName) && router.hasRoute(routeName)) {
      router.removeRoute(routeName);
    }
  }

  if (!adminAccessRoute && router.hasRoute(administratorAccessRouteName)) {
    router.removeRoute(administratorAccessRouteName);
  }
}

async function generateAccess(options: GenerateMenuAndRoutesOptions) {
  const pageMap: ComponentRecordType = import.meta.glob('../views/**/*.vue');
  const layoutMap: ComponentRecordType = { BasicLayout, IFrameView };
  const routes = filterAdminAccessRoutes(options.routes, options.roles ?? []);

  const generatedAccess = await generateAccessible(preferences.app.accessMode, {
    ...options,
    forbiddenComponent,
    layoutMap,
    pageMap,
    routes,
  });
  reconcileAdminAccessRoutes(options.router, generatedAccess.accessibleRoutes);
  return generatedAccess;
}

function resetAccessibleRoutes(router: Router) {
  const state = getRouteRefreshState(router);
  state.revision += 1;
  resetRouteRecords(router);
}

async function refreshAccessibleRoutes(
  router: Router,
  roles: readonly string[],
  shouldApply: () => boolean = () => true,
) {
  const state = getRouteRefreshState(router);
  const revision = state.revision + 1;
  state.revision = revision;
  const refresh = state.tail.then(async () => {
    if (state.revision !== revision || !shouldApply()) {
      return null;
    }

    resetRouteRecords(router);
    let generatedAccess;
    try {
      generatedAccess = await generateAccess({
        roles: [...roles],
        router,
        routes: accessRoutes,
      });
    } catch (error) {
      // Vben mutates the router while it generates menus and routes.
      resetRouteRecords(router);
      throw error;
    }
    if (state.revision !== revision || !shouldApply()) {
      resetRouteRecords(router);
      return null;
    }
    return generatedAccess;
  });
  state.tail = refresh.then(
    () => undefined,
    () => undefined,
  );
  return refresh;
}

export { generateAccess, refreshAccessibleRoutes, resetAccessibleRoutes };
