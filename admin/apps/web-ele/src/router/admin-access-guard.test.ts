import type { RouteLocationNormalized, Router } from 'vue-router';

import { reactive } from 'vue';

import { describe, expect, it, vi } from 'vitest';

import { createRouterGuard } from './guard';

type RegisteredGuard = (
  to: RouteLocationNormalized,
  from: RouteLocationNormalized,
) => unknown;

const stores = vi.hoisted(() => ({
  accessStore: {
    accessCodes: [] as string[],
    accessToken: null as null | string,
    isAccessChecked: false,
    setAccessMenus: vi.fn(),
    setAccessRoutes: vi.fn(),
    setIsAccessChecked: vi.fn(),
  },
  userStore: {
    userInfo: undefined as undefined | { homePath?: string },
  },
}));

const authentication = vi.hoisted(() => ({
  fetchUserInfo: vi.fn(),
  restoreSession: vi.fn(),
}));

const routeAccess = vi.hoisted(() => ({
  refreshAccessibleRoutes: vi.fn(),
}));

vi.mock('@vben/constants', () => ({ LOGIN_PATH: '/auth/login' }));

vi.mock('@vben/preferences', () => ({
  preferences: {
    app: { defaultHomePath: '/game-config' },
    transition: { progress: false },
  },
}));

vi.mock('@vben/stores', () => ({
  useAccessStore: () => stores.accessStore,
  useUserStore: () => stores.userStore,
}));

vi.mock('@vben/utils', () => ({
  startProgress: vi.fn(),
  stopProgress: vi.fn(),
}));

vi.mock('#/store', () => ({ useAuthStore: () => authentication }));

vi.mock('./access', () => routeAccess);

vi.mock('./routes', () => ({ coreRouteNames: [] }));

function protectedRoute(path: string): RouteLocationNormalized {
  return {
    fullPath: path,
    hash: '',
    href: path,
    matched: [],
    meta: { title: '404' },
    name: 'FallbackNotFound',
    params: {},
    path,
    query: {},
    redirectedFrom: undefined,
  } as unknown as RouteLocationNormalized;
}

describe('administrator access guard session restoration', () => {
  it('retries the original protected path without resolving it before access routes exist', async () => {
    const beforeEachGuards: RegisteredGuard[] = [];
    const router = {
      afterEach: vi.fn(),
      beforeEach: vi.fn((guard: RegisteredGuard) => {
        beforeEachGuards.push(guard);
      }),
    } as unknown as Router;
    const target = protectedRoute('/admin-access/administrators');

    authentication.restoreSession.mockImplementation(async () => {
      stores.accessStore.accessToken = 'restored-access-token';
      return true;
    });

    createRouterGuard(router);

    const accessGuard = beforeEachGuards.at(-1);
    if (!accessGuard) {
      throw new Error('The access guard was not registered.');
    }

    await expect(
      Promise.resolve(accessGuard(target, protectedRoute('/'))),
    ).resolves.toEqual({
      path: '/admin-access/administrators',
      replace: true,
    });
  });

  it('uses store-backed state when a persisted token starts a protected route', async () => {
    const beforeEachGuards: RegisteredGuard[] = [];
    const router = {
      afterEach: vi.fn(),
      beforeEach: vi.fn((guard: RegisteredGuard) => {
        beforeEachGuards.push(guard);
      }),
      resolve: vi.fn((path: string) => ({ path })),
    } as unknown as Router;
    const target = protectedRoute('/admin-access/roles');
    const refreshedCodes = [
      'Operations.AdminAccess.Access',
      'Operations.AdminAccess.Administrators.Read',
    ];
    const refreshedUser = { homePath: '/game-config' };
    const storedUser = reactive(refreshedUser);
    stores.accessStore.accessCodes = ['Operations.AdminAccess.Roles.Read'];
    stores.accessStore.accessToken = 'persisted-access-token';
    stores.accessStore.isAccessChecked = false;
    stores.userStore.userInfo = undefined;
    authentication.fetchUserInfo.mockImplementation(async () => {
      stores.accessStore.accessCodes = refreshedCodes;
      stores.userStore.userInfo = storedUser;
      return refreshedUser;
    });
    routeAccess.refreshAccessibleRoutes.mockImplementation(
      async (_router, _accessCodes, shouldApply: () => boolean) => {
        expect(shouldApply()).toBe(true);
        return {
          accessibleMenus: [],
          accessibleRoutes: [],
        };
      },
    );

    createRouterGuard(router);

    const accessGuard = beforeEachGuards.at(-1);
    if (!accessGuard) {
      throw new Error('The access guard was not registered.');
    }

    await expect(
      Promise.resolve(accessGuard(target, protectedRoute('/'))),
    ).resolves.toEqual({ path: '/admin-access/roles', replace: true });

    expect(authentication.fetchUserInfo).toHaveBeenCalledTimes(1);
    expect(routeAccess.refreshAccessibleRoutes).toHaveBeenCalledWith(
      router,
      refreshedCodes,
      expect.any(Function),
    );
    expect(stores.accessStore.setIsAccessChecked).toHaveBeenCalledWith(true);
  });
});
