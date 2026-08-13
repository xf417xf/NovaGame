import type { Router, RouteRecordRaw } from 'vue-router';

import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createMemoryHistory, createRouter } from 'vue-router';

const state = vi.hoisted(() => {
  const accessStore = {
    accessCodes: [] as string[],
    accessMenus: [] as unknown[],
    accessRoutes: [] as RouteRecordRaw[],
    accessToken: null as null | string,
    isAccessChecked: false,
    setAccessMenus: vi.fn((menus: unknown[]) => {
      accessStore.accessMenus = menus;
    }),
    setAccessRoutes: vi.fn((routes: RouteRecordRaw[]) => {
      accessStore.accessRoutes = routes;
    }),
    setIsAccessChecked: vi.fn((checked: boolean) => {
      accessStore.isAccessChecked = checked;
    }),
  };
  const userStore = {
    userInfo: null as null | Record<string, any>,
  };

  return {
    accessStore,
    authStore: {
      fetchUserInfo: vi.fn(),
      restoreSession: vi.fn(),
    },
    userStore,
  };
});

vi.mock('@vben/preferences', () => ({
  preferences: {
    app: {
      accessMode: 'frontend',
      defaultHomePath: '/player-equipment-grants',
    },
    transition: { progress: false },
  },
}));

vi.mock('@vben/stores', () => ({
  useAccessStore: () => state.accessStore,
  useUserStore: () => state.userStore,
}));

vi.mock('@vben/common-ui', () => ({
  Fallback: { render: () => null },
}));

vi.mock('#/layouts/auth.vue', () => ({
  default: { render: () => null },
}));
vi.mock('#/layouts/basic.vue', () => ({
  default: { render: () => null },
}));
vi.mock('#/views/_core/authentication/login.vue', () => ({
  default: { render: () => null },
}));
vi.mock('#/views/_core/fallback/coming-soon.vue', () => ({
  default: { render: () => null },
}));
vi.mock('#/views/_core/fallback/forbidden.vue', () => ({
  default: { render: () => null },
}));
vi.mock('#/views/_core/fallback/not-found.vue', () => ({
  default: { render: () => null },
}));
vi.mock('#/views/player-equipment-grants/index.vue', () => ({
  default: { render: () => null },
}));

vi.mock('#/store', () => ({ useAuthStore: () => state.authStore }));
vi.mock('#/locales', () => ({ $t: (key: string) => key }));

import { PlayerEquipmentGrantPermission } from '#/features/player-equipment-grants/permissions';

import { generateAccess } from './access';
import { createRouterGuard } from './guard';
import { isAccessRouteDenied } from './permission';
import { accessRoutes } from './routes';
import { coreRoutes } from './routes/core';

const administrator = {
  homePath: '/player-equipment-grants',
  realName: 'Operations Admin',
  roles: ['Administrators'],
  userId: '17b40e7d-01ca-45f7-b017-84180449678e',
  username: 'operator',
};

function createRouterStub() {
  return {
    addRoute: vi.fn(),
    getRoutes: vi.fn(() => []),
    removeRoute: vi.fn(),
  } as unknown as Router;
}

function createGuardedRouter() {
  const view = { render: () => null };
  const testRoutes: RouteRecordRaw[] = [
    {
      children: [],
      component: view,
      name: 'Root',
      path: '/',
    },
    {
      children: [
        {
          component: view,
          name: 'Login',
          path: 'login',
        },
      ],
      component: view,
      name: 'Authentication',
      path: '/auth',
    },
    {
      component: view,
      name: 'FallbackNotFound',
      path: '/:path(.*)*',
    },
  ];
  const router = createRouter({
    history: createMemoryHistory(),
    routes: testRoutes,
  });
  createRouterGuard(router);
  return router;
}

function setAuthenticated(accessCodes: string[]) {
  state.accessStore.accessCodes = accessCodes;
  state.accessStore.accessToken = 'access-token';
  state.userStore.userInfo = administrator;
}

function resetState() {
  state.accessStore.accessCodes = [];
  state.accessStore.accessMenus = [];
  state.accessStore.accessRoutes = [];
  state.accessStore.accessToken = null;
  state.accessStore.isAccessChecked = false;
  state.userStore.userInfo = null;
  vi.clearAllMocks();
  state.authStore.fetchUserInfo.mockImplementation(async () => {
    state.userStore.userInfo = administrator;
    return administrator;
  });
  state.authStore.restoreSession.mockResolvedValue(false);
}

describe('SwordMaster administrator permission routing', () => {
  beforeEach(resetState);

  it('uses the exact protected home route and permission', () => {
    expect(PlayerEquipmentGrantPermission).toEqual({
      Manage: 'SwordMaster.PlayerAssets.EquipmentGrants.Manage',
    });
    expect(coreRoutes[0]).toMatchObject({
      path: '/',
      redirect: '/player-equipment-grants',
    });
    const grantRoute = accessRoutes.find(
      (route) => route.name === 'PlayerEquipmentGrants',
    );
    expect(grantRoute).toMatchObject({
      meta: {
        authority: [PlayerEquipmentGrantPermission.Manage],
      },
      name: 'PlayerEquipmentGrants',
      path: '/player-equipment-grants',
    });
  });

  it('includes the route and menu only with the Manage permission', async () => {
    const router = createRouterStub();
    const result = await generateAccess({
      roles: [PlayerEquipmentGrantPermission.Manage],
      router,
      routes: accessRoutes,
    });

    expect(result.accessibleRoutes.map((route) => route.path)).toEqual([
      '/player-equipment-grants',
      '/forbidden',
    ]);
    expect(result.accessibleMenus).toHaveLength(1);
    expect(result.accessibleMenus[0]?.path).toBe('/player-equipment-grants');
  });

  it('removes the route and menu without the Manage permission', async () => {
    const result = await generateAccess({
      roles: [],
      router: createRouterStub(),
      routes: accessRoutes,
    });

    expect(result.accessibleRoutes.map((route) => route.path)).toEqual([
      '/forbidden',
    ]);
    expect(result.accessibleMenus).toEqual([]);
    expect(
      isAccessRouteDenied('/player-equipment-grants', accessRoutes, []),
    ).toBe(true);
    expect(
      isAccessRouteDenied('/player-equipment-grants', accessRoutes, [
        PlayerEquipmentGrantPermission.Manage,
      ]),
    ).toBe(false);
    expect(isAccessRouteDenied('/unknown', accessRoutes, [])).toBe(false);
  });
});

describe('SwordMaster administrator router guard', () => {
  beforeEach(resetState);

  it('encodes the original target after anonymous restoration fails', async () => {
    const router = createGuardedRouter();

    await router.push('/reports?tab=two');

    expect(state.authStore.restoreSession).toHaveBeenCalledOnce();
    expect(router.currentRoute.value.path).toBe('/auth/login');
    expect(router.currentRoute.value.query).toEqual({
      redirect: encodeURIComponent('/reports?tab=two'),
    });
  });

  it('restores anonymously and initializes access exactly once', async () => {
    const router = createGuardedRouter();
    state.authStore.restoreSession.mockImplementation(async () => {
      setAuthenticated([PlayerEquipmentGrantPermission.Manage]);
      return true;
    });

    await router.push('/player-equipment-grants');
    await router.push('/forbidden');
    await router.push('/player-equipment-grants');

    expect(router.currentRoute.value.name).toBe('PlayerEquipmentGrants');
    expect(state.authStore.restoreSession).toHaveBeenCalledOnce();
    expect(state.accessStore.setIsAccessChecked).toHaveBeenCalledOnce();
    expect(state.accessStore.isAccessChecked).toBe(true);
  });

  it('redirects an authenticated login visit to its decoded target', async () => {
    const router = createGuardedRouter();
    setAuthenticated([PlayerEquipmentGrantPermission.Manage]);

    await router.push({
      path: '/auth/login',
      query: {
        redirect: encodeURIComponent('/player-equipment-grants?page=2'),
      },
    });

    expect(router.currentRoute.value.fullPath).toBe(
      '/player-equipment-grants?page=2',
    );
    expect(state.accessStore.setIsAccessChecked).toHaveBeenCalledOnce();
  });

  it('routes an authenticated administrator without Manage to 403', async () => {
    const router = createGuardedRouter();
    setAuthenticated([]);

    await router.push('/player-equipment-grants');

    expect(router.currentRoute.value.name).toBe('Forbidden');
    expect(router.hasRoute('PlayerEquipmentGrants')).toBe(false);
    expect(router.hasRoute('Forbidden')).toBe(true);
  });

  it('removes a previous Manage route before initializing the next session', async () => {
    const router = createGuardedRouter();
    setAuthenticated([PlayerEquipmentGrantPermission.Manage]);
    await router.push('/player-equipment-grants');
    expect(router.hasRoute('PlayerEquipmentGrants')).toBe(true);

    state.accessStore.accessToken = null;
    const { resetRoutes } = await import('./index');
    resetRoutes(router);
    state.accessStore.accessCodes = [];
    state.accessStore.isAccessChecked = false;
    state.userStore.userInfo = null;
    await router.replace('/auth/login');

    setAuthenticated([]);
    await router.push('/player-equipment-grants');

    expect(router.currentRoute.value.name).toBe('Forbidden');
    expect(router.hasRoute('PlayerEquipmentGrants')).toBe(false);
  });
});
