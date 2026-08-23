import type { UserInfo } from '@vben/types';

import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '#/store/auth';

const api = vi.hoisted(() => ({
  getAccessCodesApi: vi.fn(),
  getUserInfoApi: vi.fn(),
  loginApi: vi.fn(),
  logoutApi: vi.fn(),
  refreshTokenApi: vi.fn(),
  toNovaGameApiError: vi.fn(),
  waitForRefreshAccessToken: vi.fn(),
}));

const notifications = vi.hoisted(() => ({
  error: vi.fn(),
  notify: vi.fn(),
}));

const router = vi.hoisted(() => ({
  currentRoute: { value: { fullPath: '/auth/login', path: '/auth/login' } },
  push: vi.fn(),
  replace: vi.fn(),
}));

function createDeferred<T>() {
  let reject: (reason?: unknown) => void = () => {
    throw new Error('Deferred promise rejection handler is unavailable.');
  };
  let resolve: (value: PromiseLike<T> | T) => void = () => {
    throw new Error('Deferred promise resolution handler is unavailable.');
  };
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    reject = promiseReject;
    resolve = promiseResolve;
  });

  return { promise, reject, resolve };
}

const routes = vi.hoisted(() => ({
  refreshAccessibleRoutes: vi.fn(),
  resetAccessibleRoutes: vi.fn(),
}));

const stores = vi.hoisted(() => {
  const accessStore = {
    accessCodes: [] as string[],
    accessToken: null as null | string,
    loginExpired: false,
    setAccessMenus: vi.fn(),
    setAccessRoutes: vi.fn(),
    setAccessCodes: vi.fn((accessCodes: string[]) => {
      accessStore.accessCodes = accessCodes;
    }),
    setIsAccessChecked: vi.fn(),
    setAccessToken: vi.fn((accessToken: null | string) => {
      accessStore.accessToken = accessToken;
    }),
    setLoginExpired: vi.fn((loginExpired: boolean) => {
      accessStore.loginExpired = loginExpired;
    }),
  };
  const userStore = {
    setUserInfo: vi.fn((userInfo: unknown) => {
      userStore.userInfo = userInfo;
    }),
    userInfo: undefined as unknown,
  };

  return {
    accessStore,
    resetAllStores: vi.fn(() => {
      accessStore.accessCodes = [];
      accessStore.accessToken = null;
      userStore.userInfo = undefined;
    }),
    userStore,
  };
});

vi.mock('#/api', () => api);

vi.mock('#/locales', () => ({
  $t: (key: string) => key,
}));

vi.mock('#/router/access', () => routes);

vi.mock('@vben/constants', () => ({
  LOGIN_PATH: '/auth/login',
}));

vi.mock('@vben/preferences', () => ({
  preferences: { app: { defaultHomePath: '/dashboard' } },
}));

vi.mock('@vben/stores', () => ({
  resetAllStores: stores.resetAllStores,
  useAccessStore: () => stores.accessStore,
  useUserStore: () => stores.userStore,
}));

vi.mock('element-plus', () => ({
  ElMessage: { error: notifications.error },
  ElNotification: notifications.notify,
}));

vi.mock('vue-router', () => ({
  useRouter: () => router,
}));

describe('administrator access authentication state', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    setActivePinia(createPinia());
    stores.accessStore.accessCodes = [];
    stores.accessStore.accessToken = null;
    stores.accessStore.loginExpired = false;
    stores.userStore.userInfo = undefined;
    router.currentRoute.value = {
      fullPath: '/auth/login',
      path: '/auth/login',
    };
    router.push.mockResolvedValue(undefined);
    router.replace.mockResolvedValue(undefined);
    api.getAccessCodesApi.mockRejectedValue({
      message: 'Administrator access is required.',
      status: 403,
    });
    api.getUserInfoApi.mockRejectedValue({
      message: 'Administrator access is required.',
      status: 403,
    });
    api.loginApi.mockResolvedValue({ accessToken: 'post-login-token' });
    api.logoutApi.mockResolvedValue(undefined);
    api.toNovaGameApiError.mockImplementation((error: unknown) => error);
    api.waitForRefreshAccessToken.mockResolvedValue(undefined);
  });

  it('clears newly persisted authentication state when post-login access loading is forbidden', async () => {
    const result = await useAuthStore().authLogin({
      password: 'valid-password',
      username: 'leaf-only-operator',
    });

    expect(result).toEqual({ userInfo: null });
    expect(stores.accessStore.setAccessToken).toHaveBeenCalledWith(
      'post-login-token',
    );
    expect(routes.resetAccessibleRoutes).toHaveBeenCalledWith(router);
    expect(stores.resetAllStores).toHaveBeenCalledTimes(1);
    expect(stores.accessStore.accessCodes).toEqual([]);
    expect(stores.accessStore.accessToken).toBeNull();
    expect(useAuthStore().loginLoading).toBe(false);
    expect(router.replace).not.toHaveBeenCalled();
    expect(notifications.error).toHaveBeenCalledWith(
      'Administrator access is required.',
    );
  });

  it('retains newly authenticated state when post-login access loading is transiently unavailable', async () => {
    api.getAccessCodesApi.mockRejectedValue({
      message: 'The permission service is temporarily unavailable.',
      status: 503,
    });
    api.getUserInfoApi.mockResolvedValue({
      avatar: '',
      desc: 'Administrators',
      homePath: '/admin-access',
      realName: 'Administrator',
      roles: ['Administrators'],
      token: '',
      userId: 'administrator-id',
      username: 'administrator',
    });

    const result = await useAuthStore().authLogin({
      password: 'valid-password',
      username: 'administrator',
    });

    expect(result).toEqual({ userInfo: null });
    expect(routes.resetAccessibleRoutes).not.toHaveBeenCalled();
    expect(stores.resetAllStores).not.toHaveBeenCalled();
    expect(stores.accessStore.accessToken).toBe('post-login-token');
    expect(useAuthStore().loginLoading).toBe(false);
    expect(notifications.error).toHaveBeenCalledWith(
      'The permission service is temporarily unavailable.',
    );
  });

  it('keeps authenticated state when post-login navigation fails', async () => {
    api.getAccessCodesApi.mockResolvedValue(['Operations.AdminAccess.Access']);
    api.getUserInfoApi.mockResolvedValue({
      avatar: '',
      desc: 'Administrators',
      homePath: '/admin-access',
      realName: 'Administrator',
      roles: ['Administrators'],
      token: '',
      userId: 'administrator-id',
      username: 'administrator',
    });
    router.push.mockRejectedValue(new Error('Workspace navigation failed.'));

    const result = await useAuthStore().authLogin({
      password: 'valid-password',
      username: 'administrator',
    });

    expect(result.userInfo).toMatchObject({ username: 'administrator' });
    expect(routes.resetAccessibleRoutes).not.toHaveBeenCalled();
    expect(stores.resetAllStores).not.toHaveBeenCalled();
    expect(stores.accessStore.accessCodes).toEqual([
      'Operations.AdminAccess.Access',
    ]);
    expect(stores.accessStore.accessToken).toBe('post-login-token');
    expect(stores.userStore.userInfo).toMatchObject({
      username: 'administrator',
    });
    expect(notifications.error).toHaveBeenCalledWith(
      'Workspace navigation failed.',
    );
  });

  it('keeps a newer login state when an older session restoration fails', async () => {
    const refresh = createDeferred<{ accessToken: string }>();
    api.refreshTokenApi.mockReturnValue(refresh.promise);
    api.getAccessCodesApi.mockResolvedValue(['Administrators.Access']);
    api.getUserInfoApi.mockResolvedValue({
      avatar: '',
      desc: 'Administrators',
      homePath: '/game-config',
      realName: 'Administrator',
      roles: ['Administrators'],
      token: '',
      userId: 'administrator-id',
      username: 'administrator',
    });

    const authStore = useAuthStore();
    const restoreResult = authStore.restoreSession();
    const loginResult = await authStore.authLogin({
      password: 'valid-password',
      username: 'administrator',
    });

    refresh.reject(new Error('The previous refresh token has expired.'));

    await expect(restoreResult).resolves.toBe(false);
    expect(loginResult.userInfo?.username).toBe('administrator');
    expect(stores.resetAllStores).not.toHaveBeenCalled();
    expect(stores.accessStore.accessCodes).toEqual(['Administrators.Access']);
    expect(stores.accessStore.accessToken).toBe('post-login-token');
    expect(stores.userStore.userInfo).toMatchObject({
      username: 'administrator',
    });
  });

  it('does not restore a stale session after logout clears local state', async () => {
    const refresh = createDeferred<{ accessToken: string }>();
    api.refreshTokenApi.mockReturnValue(refresh.promise);

    const authStore = useAuthStore();
    const restoreResult = authStore.restoreSession();
    await authStore.logout(false);
    refresh.resolve({ accessToken: 'stale-restored-token' });

    await expect(restoreResult).resolves.toBe(false);
    expect(stores.accessStore.accessCodes).toEqual([]);
    expect(stores.accessStore.accessToken).toBeNull();
    expect(stores.userStore.userInfo).toBeUndefined();
  });

  it('does not start a replacement restoration while logout is pending', async () => {
    const serverLogout = createDeferred<undefined>();
    api.logoutApi.mockReturnValue(serverLogout.promise);
    api.refreshTokenApi.mockResolvedValue({
      accessToken: 'stale-restored-token',
    });

    const authStore = useAuthStore();
    const logoutResult = authStore.logout(false);
    const restoreResult = authStore.restoreSession();

    expect(api.refreshTokenApi).not.toHaveBeenCalled();
    expect(stores.accessStore.accessToken).toBeNull();

    serverLogout.resolve(undefined);

    await logoutResult;
    await expect(restoreResult).resolves.toBe(false);
    expect(api.refreshTokenApi).not.toHaveBeenCalled();
    expect(stores.accessStore.accessToken).toBeNull();
    expect(stores.userStore.userInfo).toBeUndefined();
  });

  it('cancels an interceptor refresh that resolves after logout starts', async () => {
    const refresh = createDeferred<{ accessToken: string }>();
    api.refreshTokenApi.mockReturnValue(refresh.promise);
    api.waitForRefreshAccessToken.mockImplementation(async () => {
      await refresh.promise;
    });

    const authStore = useAuthStore();
    const requestRefresh = authStore.refreshAccessTokenForRequest();
    const logoutResult = authStore.logout(false);

    expect(api.logoutApi).not.toHaveBeenCalled();
    expect(stores.accessStore.accessToken).toBeNull();

    refresh.resolve({ accessToken: 'stale-request-token' });

    await expect(requestRefresh).rejects.toMatchObject({
      __CANCEL__: true,
      code: 'ERR_CANCELED',
      name: 'CanceledError',
    });
    await logoutResult;

    expect(api.logoutApi).toHaveBeenCalledTimes(1);
    expect(stores.accessStore.accessToken).toBeNull();
  });

  it('releases a superseded session restoration so a later restore can retry', async () => {
    const staleRefresh = createDeferred<{ accessToken: string }>();
    api.refreshTokenApi
      .mockReturnValueOnce(staleRefresh.promise)
      .mockResolvedValueOnce({ accessToken: 'retried-restored-token' });
    api.getAccessCodesApi.mockResolvedValue(['Operations.AdminAccess.Access']);
    api.getUserInfoApi.mockResolvedValue({
      avatar: '',
      desc: 'Administrators',
      homePath: '/admin-access',
      realName: 'Administrator',
      roles: ['Administrators'],
      token: '',
      userId: 'administrator-id',
      username: 'administrator',
    });

    const authStore = useAuthStore();
    const staleRestore = authStore.restoreSession();
    await expect(authStore.fetchUserInfo()).resolves.toMatchObject({
      username: 'administrator',
    });
    staleRefresh.resolve({ accessToken: 'stale-restored-token' });

    await expect(staleRestore).resolves.toBe(false);
    await expect(authStore.restoreSession()).resolves.toBe(true);
    expect(api.refreshTokenApi).toHaveBeenCalledTimes(2);
    expect(stores.accessStore.accessToken).toBe('retried-restored-token');
  });

  it('replaces persisted permission codes during a cold start', async () => {
    stores.accessStore.accessCodes = ['Operations.AdminAccess.Stale'];
    stores.accessStore.accessToken = 'persisted-access-token';
    api.getAccessCodesApi.mockResolvedValue([
      'Operations.AdminAccess.Access',
      'Operations.AdminAccess.Roles.Read',
    ]);
    api.getUserInfoApi.mockResolvedValue({
      avatar: '',
      desc: 'Roles reader',
      homePath: '/game-config',
      realName: 'Roles reader',
      roles: ['Roles readers'],
      token: '',
      userId: 'roles-reader-id',
      username: 'roles-reader',
    });

    await expect(useAuthStore().fetchUserInfo()).resolves.toMatchObject({
      username: 'roles-reader',
    });

    expect(stores.accessStore.accessCodes).toEqual([
      'Operations.AdminAccess.Access',
      'Operations.AdminAccess.Roles.Read',
    ]);
    expect(stores.userStore.userInfo).toMatchObject({
      username: 'roles-reader',
    });
  });

  it('expires a persisted session when cold-start state is forbidden', async () => {
    stores.accessStore.accessToken = 'persisted-access-token';
    router.currentRoute.value = {
      fullPath: '/admin-access/roles',
      path: '/admin-access/roles',
    };
    api.getAccessCodesApi.mockResolvedValue(['Operations.AdminAccess.Access']);
    api.getUserInfoApi.mockRejectedValue({
      message: 'Administrator access is required.',
      status: 403,
    });

    await expect(useAuthStore().fetchUserInfo()).resolves.toBeNull();

    expect(routes.resetAccessibleRoutes).toHaveBeenCalledWith(router);
    expect(stores.resetAllStores).toHaveBeenCalledTimes(1);
    expect(stores.accessStore.accessCodes).toEqual([]);
    expect(stores.accessStore.accessToken).toBeNull();
    expect(stores.userStore.userInfo).toBeUndefined();
    expect(router.replace).toHaveBeenCalledWith({
      path: '/auth/login',
      query: {
        expired: '1',
        redirect: encodeURIComponent('/admin-access/roles'),
      },
    });
  });

  it('does not persist a late login after logout clears local state', async () => {
    const login = createDeferred<{ accessToken: string }>();
    api.loginApi.mockReturnValue(login.promise);

    const authStore = useAuthStore();
    const loginResult = authStore.authLogin({
      password: 'valid-password',
      username: 'administrator',
    });
    const logoutResult = authStore.logout(false);
    expect(api.logoutApi).not.toHaveBeenCalled();
    login.resolve({ accessToken: 'stale-login-token' });

    await logoutResult;
    await expect(loginResult).resolves.toEqual({ userInfo: null });
    expect(api.getAccessCodesApi).not.toHaveBeenCalled();
    expect(api.getUserInfoApi).not.toHaveBeenCalled();
    expect(stores.accessStore.accessCodes).toEqual([]);
    expect(stores.accessStore.accessToken).toBeNull();
    expect(stores.userStore.userInfo).toBeUndefined();
  });

  it('waits for an in-flight login before sending server logout', async () => {
    const login = createDeferred<{ accessToken: string }>();
    api.loginApi.mockReturnValue(login.promise);

    const authStore = useAuthStore();
    const loginResult = authStore.authLogin({
      password: 'valid-password',
      username: 'administrator',
    });
    const logoutResult = authStore.logout(false);

    expect(api.logoutApi).not.toHaveBeenCalled();
    login.resolve({ accessToken: 'stale-login-token' });

    await expect(logoutResult).resolves.toBeUndefined();
    await expect(loginResult).resolves.toEqual({ userInfo: null });
    expect(api.logoutApi).toHaveBeenCalledTimes(1);
    expect(api.getAccessCodesApi).not.toHaveBeenCalled();
    expect(api.getUserInfoApi).not.toHaveBeenCalled();
    expect(stores.accessStore.accessToken).toBeNull();
  });

  it('waits for a pending logout before applying a newer login state', async () => {
    const logout = createDeferred<undefined>();
    const navigation = createDeferred<undefined>();
    stores.accessStore.accessToken = 'authenticated-token';
    api.logoutApi.mockReturnValue(logout.promise);
    router.replace.mockReturnValue(navigation.promise);
    api.getAccessCodesApi.mockResolvedValue(['Administrators.Access']);
    api.getUserInfoApi.mockResolvedValue({
      avatar: '',
      desc: 'Administrators',
      homePath: '/admin-access',
      realName: 'Administrator',
      roles: ['Administrators'],
      token: '',
      userId: 'administrator-id',
      username: 'administrator',
    });

    const authStore = useAuthStore();
    const logoutResult = authStore.logout(false);
    const loginResult = authStore.authLogin({
      password: 'valid-password',
      username: 'administrator',
    });

    expect(stores.accessStore.accessToken).toBeNull();
    expect(api.loginApi).not.toHaveBeenCalled();
    logout.resolve(undefined);

    await Promise.resolve();
    expect(api.loginApi).not.toHaveBeenCalled();

    navigation.resolve(undefined);

    await logoutResult;
    await expect(loginResult).resolves.toMatchObject({
      userInfo: { username: 'administrator' },
    });
    expect(stores.accessStore.accessCodes).toEqual(['Administrators.Access']);
    expect(stores.accessStore.accessToken).toBe('post-login-token');
    expect(stores.userStore.userInfo).toMatchObject({
      username: 'administrator',
    });
  });

  it('clears login loading when a pending logout navigation rejects', async () => {
    stores.accessStore.accessToken = 'authenticated-token';
    router.replace.mockRejectedValue(new Error('Logout navigation failed.'));

    const authStore = useAuthStore();
    const logoutResult = authStore.logout(false);
    const loginResult = authStore.authLogin({
      password: 'valid-password',
      username: 'administrator',
    });

    await expect(logoutResult).rejects.toThrow('Logout navigation failed.');
    await expect(loginResult).resolves.toEqual({ userInfo: null });
    expect(api.loginApi).not.toHaveBeenCalled();
    expect(authStore.loginLoading).toBe(false);
    expect(notifications.error).toHaveBeenCalledWith(
      'Logout navigation failed.',
    );
  });

  it('keeps a failed logout barrier until the server logout settles', async () => {
    const serverLogout = createDeferred<undefined>();
    stores.accessStore.accessToken = 'authenticated-token';
    api.logoutApi.mockReturnValue(serverLogout.promise);
    router.replace.mockRejectedValue(new Error('Logout navigation failed.'));

    const authStore = useAuthStore();
    const logoutResult = authStore.logout(false);

    await vi.waitFor(() => {
      expect(api.logoutApi).toHaveBeenCalledTimes(1);
    });

    const retryLogin = authStore.authLogin({
      password: 'valid-password',
      username: 'administrator',
    });
    expect(api.loginApi).not.toHaveBeenCalled();

    serverLogout.resolve(undefined);

    await expect(logoutResult).rejects.toThrow('Logout navigation failed.');
    await expect(retryLogin).resolves.toEqual({ userInfo: null });
    expect(api.loginApi).not.toHaveBeenCalled();
    expect(authStore.loginLoading).toBe(false);

    api.getAccessCodesApi.mockResolvedValue(['Operations.AdminAccess.Access']);
    api.getUserInfoApi.mockResolvedValue({
      avatar: '',
      desc: 'Administrators',
      homePath: '/admin-access',
      realName: 'Administrator',
      roles: ['Administrators'],
      token: '',
      userId: 'administrator-id',
      username: 'administrator',
    });

    await expect(
      authStore.authLogin({
        password: 'valid-password',
        username: 'administrator',
      }),
    ).resolves.toMatchObject({
      userInfo: { username: 'administrator' },
    });
    expect(api.loginApi).toHaveBeenCalledTimes(1);
  });

  it('does not reapply permissions when logout supersedes a refresh', async () => {
    const accessCodes = createDeferred<string[]>();
    const userInfo = createDeferred<UserInfo>();
    api.getAccessCodesApi.mockReturnValue(accessCodes.promise);
    api.getUserInfoApi.mockReturnValue(userInfo.promise);
    routes.refreshAccessibleRoutes.mockResolvedValue({
      accessibleMenus: [],
      accessibleRoutes: [],
    });
    stores.accessStore.accessToken = 'current-token';

    const authStore = useAuthStore();
    const refreshResult = authStore.refreshAuthenticatedState();
    await authStore.logout(false);
    accessCodes.resolve(['Operations.AdminAccess.Access']);
    userInfo.resolve({
      avatar: '',
      desc: 'Administrators',
      homePath: '/admin-access',
      realName: 'Administrator',
      roles: ['Administrators'],
      token: '',
      userId: 'administrator-id',
      username: 'administrator',
    });

    await expect(refreshResult).resolves.toBeNull();
    expect(routes.refreshAccessibleRoutes).not.toHaveBeenCalled();
    expect(stores.accessStore.setAccessCodes).not.toHaveBeenCalled();
    expect(stores.accessStore.setAccessMenus).not.toHaveBeenCalled();
    expect(stores.accessStore.setAccessRoutes).not.toHaveBeenCalled();
    expect(stores.accessStore.setIsAccessChecked).not.toHaveBeenCalled();
    expect(stores.accessStore.accessCodes).toEqual([]);
    expect(stores.accessStore.accessToken).toBeNull();
    expect(stores.userStore.userInfo).toBeUndefined();
  });

  it('keeps the latest permission snapshot when concurrent refreshes resolve out of order', async () => {
    const olderAccessCodes = createDeferred<string[]>();
    const olderUserInfo = createDeferred<UserInfo>();
    const newerAccessCodes = createDeferred<string[]>();
    const newerUserInfo = createDeferred<UserInfo>();
    api.getAccessCodesApi
      .mockReturnValueOnce(olderAccessCodes.promise)
      .mockReturnValueOnce(newerAccessCodes.promise);
    api.getUserInfoApi
      .mockReturnValueOnce(olderUserInfo.promise)
      .mockReturnValueOnce(newerUserInfo.promise);
    routes.refreshAccessibleRoutes.mockResolvedValue({
      accessibleMenus: ['fresh-menu'],
      accessibleRoutes: ['fresh-route'],
    });
    stores.accessStore.accessToken = 'current-token';

    const authStore = useAuthStore();
    const olderRefresh = authStore.refreshAuthenticatedState();
    const newerRefresh = authStore.refreshAuthenticatedState();
    newerAccessCodes.resolve([
      'Operations.AdminAccess.Access',
      'Operations.AdminAccess.Roles.Read',
    ]);
    newerUserInfo.resolve({
      avatar: '',
      desc: 'Roles reader',
      homePath: '/admin-access/roles',
      realName: 'Roles reader',
      roles: ['Roles readers'],
      token: '',
      userId: 'roles-reader-id',
      username: 'roles-reader',
    });

    await expect(newerRefresh).resolves.toMatchObject({
      accessibleRoutes: ['fresh-route'],
      userInfo: { username: 'roles-reader' },
    });

    olderAccessCodes.resolve([
      'Operations.AdminAccess.Access',
      'Operations.AdminAccess.Administrators.Read',
    ]);
    olderUserInfo.resolve({
      avatar: '',
      desc: 'Administrators reader',
      homePath: '/admin-access/administrators',
      realName: 'Administrators reader',
      roles: ['Administrators readers'],
      token: '',
      userId: 'administrators-reader-id',
      username: 'administrators-reader',
    });

    await expect(olderRefresh).resolves.toBeNull();
    expect(stores.accessStore.accessCodes).toEqual([
      'Operations.AdminAccess.Access',
      'Operations.AdminAccess.Roles.Read',
    ]);
    expect(stores.userStore.userInfo).toMatchObject({
      username: 'roles-reader',
    });
    expect(routes.refreshAccessibleRoutes).toHaveBeenCalledTimes(1);
    expect(routes.refreshAccessibleRoutes).toHaveBeenCalledWith(
      router,
      ['Operations.AdminAccess.Access', 'Operations.AdminAccess.Roles.Read'],
      expect.any(Function),
    );
  });

  it('does not expire the newer session when an older permission refresh is forbidden', async () => {
    const olderAccessCodes = createDeferred<string[]>();
    const olderUserInfo = createDeferred<UserInfo>();
    const newerAccessCodes = createDeferred<string[]>();
    const newerUserInfo = createDeferred<UserInfo>();
    api.getAccessCodesApi
      .mockReturnValueOnce(olderAccessCodes.promise)
      .mockReturnValueOnce(newerAccessCodes.promise);
    api.getUserInfoApi
      .mockReturnValueOnce(olderUserInfo.promise)
      .mockReturnValueOnce(newerUserInfo.promise);
    routes.refreshAccessibleRoutes.mockResolvedValue({
      accessibleMenus: ['fresh-menu'],
      accessibleRoutes: ['fresh-route'],
    });
    stores.accessStore.accessToken = 'current-token';

    const authStore = useAuthStore();
    const olderRefresh = authStore.refreshAuthenticatedState();
    const newerRefresh = authStore.refreshAuthenticatedState();
    newerAccessCodes.resolve([
      'Operations.AdminAccess.Access',
      'Operations.AdminAccess.Roles.Read',
    ]);
    newerUserInfo.resolve({
      avatar: '',
      desc: 'Roles reader',
      homePath: '/admin-access/roles',
      realName: 'Roles reader',
      roles: ['Roles readers'],
      token: '',
      userId: 'roles-reader-id',
      username: 'roles-reader',
    });

    await expect(newerRefresh).resolves.toMatchObject({
      accessibleRoutes: ['fresh-route'],
      userInfo: { username: 'roles-reader' },
    });

    olderAccessCodes.reject({
      message: 'Administrator access is required.',
      status: 403,
    });
    olderUserInfo.resolve({
      avatar: '',
      desc: 'Administrators reader',
      homePath: '/admin-access/administrators',
      realName: 'Administrators reader',
      roles: ['Administrators readers'],
      token: '',
      userId: 'administrators-reader-id',
      username: 'administrators-reader',
    });

    await expect(olderRefresh).resolves.toBeNull();
    expect(routes.resetAccessibleRoutes).not.toHaveBeenCalled();
    expect(stores.resetAllStores).not.toHaveBeenCalled();
    expect(stores.accessStore.accessCodes).toEqual([
      'Operations.AdminAccess.Access',
      'Operations.AdminAccess.Roles.Read',
    ]);
    expect(stores.accessStore.accessToken).toBe('current-token');
  });
});
