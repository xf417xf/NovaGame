import type { RouteRecordRaw } from 'vue-router';

import { LOGIN_PATH } from '@vben/constants';
import { preferences } from '@vben/preferences';

import { $t } from '#/locales';

const BasicLayout = () => import('#/layouts/basic.vue');
const AuthPageLayout = () => import('#/layouts/auth.vue');

const fallbackNotFoundRoute: RouteRecordRaw = {
  component: () => import('#/views/_core/fallback/not-found.vue'),
  meta: {
    hideInBreadcrumb: true,
    hideInMenu: true,
    hideInTab: true,
    title: '404',
  },
  name: 'FallbackNotFound',
  path: '/:path(.*)*',
};

const coreRoutes: RouteRecordRaw[] = [
  {
    children: [],
    component: BasicLayout,
    meta: { hideInBreadcrumb: true, title: 'Root' },
    name: 'Root',
    path: '/',
    redirect: preferences.app.defaultHomePath,
  },
  {
    children: [
      {
        component: () => import('#/views/_core/authentication/login.vue'),
        meta: { title: $t('page.auth.login') },
        name: 'Login',
        path: 'login',
      },
    ],
    component: AuthPageLayout,
    meta: { hideInTab: true, title: 'Authentication' },
    name: 'Authentication',
    path: '/auth',
    redirect: LOGIN_PATH,
  },
];

export { coreRoutes, fallbackNotFoundRoute };
