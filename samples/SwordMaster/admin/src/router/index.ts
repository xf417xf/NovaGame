import type { Router } from 'vue-router';

import {
  createRouter,
  createWebHashHistory,
  createWebHistory,
} from 'vue-router';

import { cloneDeep } from '@vben/utils';

import { createRouterGuard } from './guard';
import { routes } from './routes';

const staticRouteTemplate = cloneDeep(routes);
const router = createRouter({
  history:
    import.meta.env.VITE_ROUTER_HISTORY === 'hash'
      ? createWebHashHistory(import.meta.env.VITE_BASE)
      : createWebHistory(import.meta.env.VITE_BASE),
  routes: cloneDeep(staticRouteTemplate),
  scrollBehavior: (to, _from, savedPosition) => {
    if (savedPosition) {
      return savedPosition;
    }
    return to.hash ? { behavior: 'smooth', el: to.hash } : { left: 0, top: 0 };
  },
});

const resetRoutes = (targetRouter: Router = router) => {
  for (const route of targetRouter.getRoutes()) {
    if (route.name && targetRouter.hasRoute(route.name)) {
      targetRouter.removeRoute(route.name);
    }
  }
  for (const route of cloneDeep(staticRouteTemplate)) {
    targetRouter.addRoute(route);
  }
};

createRouterGuard(router);

export { resetRoutes, router };
