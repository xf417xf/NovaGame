import { beforeEach, describe, expect, it, vi } from 'vitest';

import { createPinia, setActivePinia } from 'pinia';

const state = vi.hoisted(() => {
  const accessStore = {
    accessCodes: [] as string[],
    accessToken: null as null | string,
    isAccessChecked: false,
    loginExpired: false,
    setAccessCodes: vi.fn((codes: string[]) => {
      accessStore.accessCodes = codes;
    }),
    setAccessToken: vi.fn((token: null | string) => {
      accessStore.accessToken = token;
    }),
    setIsAccessChecked: vi.fn((checked: boolean) => {
      accessStore.isAccessChecked = checked;
    }),
    setLoginExpired: vi.fn((expired: boolean) => {
      accessStore.loginExpired = expired;
    }),
    setRefreshToken: vi.fn(),
  };
  const userStore = {
    setUserInfo: vi.fn((userInfo: Record<string, unknown> | null) => {
      userStore.userInfo = userInfo;
    }),
    userInfo: null as null | Record<string, unknown>,
  };

  return {
    accessStore,
    api: {
      getAccessCodesApi: vi.fn(),
      getUserInfoApi: vi.fn(),
      loginApi: vi.fn(),
      logoutApi: vi.fn(),
      refreshTokenApi: vi.fn(),
    },
    messages: {
      error: vi.fn(),
      notification: vi.fn(),
    },
    resetAllStores: vi.fn(() => {
      accessStore.accessCodes = [];
      accessStore.accessToken = null;
      accessStore.isAccessChecked = false;
      accessStore.loginExpired = false;
      userStore.userInfo = null;
    }),
    resetRoutes: vi.fn(),
    router: {
      currentRoute: {
        value: {
          fullPath: '/player-equipment-grants?page=2',
          path: '/player-equipment-grants',
        },
      },
      push: vi.fn(),
      replace: vi.fn(),
    },
    userStore,
  };
});

vi.mock('vue-router', () => ({
  useRouter: () => state.router,
}));

vi.mock('@vben/constants', () => ({ LOGIN_PATH: '/auth/login' }));

vi.mock('@vben/preferences', () => ({
  preferences: { app: { defaultHomePath: '/player-equipment-grants' } },
}));

vi.mock('@vben/stores', () => ({
  resetAllStores: state.resetAllStores,
  useAccessStore: () => state.accessStore,
  useUserStore: () => state.userStore,
}));

vi.mock('element-plus', () => ({
  ElMessage: { error: state.messages.error },
  ElNotification: state.messages.notification,
}));

vi.mock('#/api', () => ({
  ...state.api,
  toNovaGameApiError: (error: unknown) =>
    error instanceof Error ? error : new Error('Authentication failed.'),
}));

vi.mock('#/locales', () => ({ $t: (key: string) => key }));

vi.mock('#/router', () => ({ resetRoutes: state.resetRoutes }));

import { useAuthStore } from './auth';

const administrator = {
  avatar: '',
  desc: 'Administrators',
  homePath: '/player-equipment-grants',
  realName: 'Operations Admin',
  roles: ['Administrators'],
  token: '',
  userId: '17b40e7d-01ca-45f7-b017-84180449678e',
  username: 'operator',
};
const managePermission = 'SwordMaster.PlayerAssets.EquipmentGrants.Manage';

function resetState() {
  state.accessStore.accessCodes = [];
  state.accessStore.accessToken = null;
  state.accessStore.isAccessChecked = false;
  state.accessStore.loginExpired = false;
  state.userStore.userInfo = null;
  state.router.currentRoute.value = {
    fullPath: '/player-equipment-grants?page=2',
    path: '/player-equipment-grants',
  };

  for (const mock of [
    ...Object.values(state.api),
    state.accessStore.setAccessCodes,
    state.accessStore.setAccessToken,
    state.accessStore.setIsAccessChecked,
    state.accessStore.setLoginExpired,
    state.accessStore.setRefreshToken,
    state.userStore.setUserInfo,
    state.resetAllStores,
    state.resetRoutes,
    state.router.push,
    state.router.replace,
    state.messages.error,
    state.messages.notification,
  ]) {
    mock.mockClear();
  }
}

describe('administrator authentication store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    resetState();
    state.api.getUserInfoApi.mockResolvedValue(administrator);
    state.api.getAccessCodesApi.mockResolvedValue([managePermission]);
    state.api.logoutApi.mockResolvedValue(undefined);
  });

  it('logs in, loads current state, and routes to the SwordMaster home', async () => {
    state.api.loginApi.mockResolvedValue({ accessToken: 'access-token' });
    const store = useAuthStore();

    await expect(
      store.authLogin({ password: 'secret', username: 'operator' }),
    ).resolves.toEqual({ userInfo: administrator });

    expect(state.api.loginApi).toHaveBeenCalledWith({
      password: 'secret',
      username: 'operator',
    });
    expect(state.accessStore.accessToken).toBe('access-token');
    expect(state.accessStore.accessCodes).toEqual([managePermission]);
    expect(state.userStore.userInfo).toEqual(administrator);
    expect(state.router.push).toHaveBeenCalledWith('/player-equipment-grants');
    expect(state.accessStore.setRefreshToken).not.toHaveBeenCalled();
    expect(store.loginLoading).toBe(false);
  });

  it('clears a token when authenticated-state loading fails after login', async () => {
    state.api.loginApi.mockResolvedValue({ accessToken: 'access-token' });
    state.api.getAccessCodesApi.mockRejectedValue(new Error('codes failed'));
    const store = useAuthStore();

    await expect(
      store.authLogin({ password: 'secret', username: 'operator' }),
    ).resolves.toEqual({ userInfo: null });

    expect(state.resetAllStores).toHaveBeenCalledOnce();
    expect(state.resetRoutes).toHaveBeenCalledOnce();
    expect(state.api.logoutApi).toHaveBeenCalledOnce();
    expect(state.accessStore.accessToken).toBeNull();
    expect(state.userStore.userInfo).toBeNull();
    expect(state.router.push).not.toHaveBeenCalled();
    expect(state.messages.error).toHaveBeenCalledWith('codes failed');
    expect(store.logoutPending).toBe(false);
  });

  it('preserves the bootstrap error and blocks restore when cleanup is uncertain', async () => {
    state.api.loginApi.mockResolvedValue({ accessToken: 'access-token' });
    state.api.getAccessCodesApi.mockRejectedValue(new Error('codes failed'));
    state.api.logoutApi.mockRejectedValue(new Error('cleanup offline'));
    const store = useAuthStore();

    await expect(
      store.authLogin({ password: 'secret', username: 'operator' }),
    ).resolves.toEqual({ userInfo: null });

    expect(state.messages.error).toHaveBeenCalledWith('codes failed');
    expect(store.logoutPending).toBe(true);
    expect(state.resetRoutes).toHaveBeenCalledOnce();
    await expect(store.restoreSession()).resolves.toBe(false);
    expect(state.api.refreshTokenApi).not.toHaveBeenCalled();
  });

  it('refreshes both current user and permission codes for a persisted token', async () => {
    state.accessStore.accessToken = 'persisted-access-token';
    state.accessStore.accessCodes = ['stale.permission'];
    const store = useAuthStore();

    await expect(store.fetchUserInfo()).resolves.toEqual(administrator);

    expect(state.api.getUserInfoApi).toHaveBeenCalledOnce();
    expect(state.api.getAccessCodesApi).toHaveBeenCalledOnce();
    expect(state.accessStore.accessCodes).toEqual([managePermission]);
  });

  it('coalesces concurrent cold restores and stores no refresh token', async () => {
    let resolveRefresh!: (value: { accessToken: string }) => void;
    state.api.refreshTokenApi.mockReturnValue(
      new Promise((resolve) => {
        resolveRefresh = resolve;
      }),
    );
    const store = useAuthStore();

    const first = store.restoreSession();
    const second = store.restoreSession();
    expect(state.api.refreshTokenApi).toHaveBeenCalledOnce();

    resolveRefresh({ accessToken: 'restored-access-token' });
    await expect(Promise.all([first, second])).resolves.toEqual([true, true]);
    expect(state.accessStore.accessToken).toBe('restored-access-token');
    expect(state.api.getUserInfoApi).toHaveBeenCalledOnce();
    expect(state.api.getAccessCodesApi).toHaveBeenCalledOnce();
    expect(state.accessStore.setRefreshToken).not.toHaveBeenCalled();
  });

  it('clears all local state when cookie restoration fails', async () => {
    state.accessStore.accessCodes = ['stale.permission'];
    state.userStore.userInfo = administrator;
    state.api.refreshTokenApi.mockRejectedValue(new Error('expired'));
    const store = useAuthStore();

    await expect(store.restoreSession()).resolves.toBe(false);
    expect(state.resetAllStores).toHaveBeenCalledOnce();
    expect(state.resetRoutes).toHaveBeenCalledOnce();
    expect(state.accessStore.accessCodes).toEqual([]);
    expect(state.userStore.userInfo).toBeNull();
    expect(state.api.logoutApi).not.toHaveBeenCalled();
    expect(store.logoutPending).toBe(false);
  });

  it('clears a refreshed server session when authenticated-state loading fails', async () => {
    state.api.refreshTokenApi.mockResolvedValue({
      accessToken: 'restored-access-token',
    });
    state.api.getAccessCodesApi.mockRejectedValue(new Error('codes failed'));
    const store = useAuthStore();

    await expect(store.restoreSession()).resolves.toBe(false);

    expect(state.api.logoutApi).toHaveBeenCalledOnce();
    expect(state.resetAllStores).toHaveBeenCalledOnce();
    expect(state.resetRoutes).toHaveBeenCalledOnce();
    expect(store.logoutPending).toBe(false);
  });

  it('blocks another restore when refreshed-session cleanup cannot be confirmed', async () => {
    state.api.refreshTokenApi.mockResolvedValue({
      accessToken: 'restored-access-token',
    });
    state.api.getAccessCodesApi.mockRejectedValue(new Error('codes failed'));
    state.api.logoutApi.mockRejectedValue(new Error('cleanup offline'));
    const store = useAuthStore();

    await expect(store.restoreSession()).resolves.toBe(false);

    expect(store.logoutPending).toBe(true);
    state.api.refreshTokenApi.mockClear();
    await expect(store.restoreSession()).resolves.toBe(false);
    expect(state.api.refreshTokenApi).not.toHaveBeenCalled();
  });

  it('clears local state after confirmed server logout', async () => {
    state.accessStore.accessToken = 'access-token';
    state.userStore.userInfo = administrator;
    const store = useAuthStore();

    await store.logout();

    expect(state.resetAllStores).toHaveBeenCalledOnce();
    expect(state.resetRoutes).toHaveBeenCalledOnce();
    expect(state.accessStore.accessToken).toBeNull();
    expect(store.logoutPending).toBe(false);
    expect(state.router.replace).toHaveBeenCalledWith({
      path: '/auth/login',
      query: {
        redirect: encodeURIComponent('/player-equipment-grants?page=2'),
      },
    });
    expect(state.messages.error).not.toHaveBeenCalled();
  });

  it('reports an uncertain logout and prevents automatic restoration', async () => {
    state.accessStore.accessToken = 'access-token';
    state.api.logoutApi.mockRejectedValue(new Error('offline'));
    const store = useAuthStore();

    await store.logout();

    expect(state.resetAllStores).toHaveBeenCalledOnce();
    expect(state.resetRoutes).toHaveBeenCalledOnce();
    expect(store.logoutPending).toBe(true);
    expect(state.messages.error).toHaveBeenCalledWith(
      'Logout could not be confirmed. offline',
    );
    await expect(store.restoreSession()).resolves.toBe(false);
    expect(state.api.refreshTokenApi).not.toHaveBeenCalled();
  });

  it('retries a pending server logout before the next explicit login', async () => {
    state.api.loginApi.mockResolvedValue({ accessToken: 'access-token' });
    const store = useAuthStore();
    store.logoutPending = true;

    await store.authLogin({ password: 'secret', username: 'operator' });

    expect(state.api.logoutApi).toHaveBeenCalledOnce();
    expect(state.api.loginApi).toHaveBeenCalledOnce();
    expect(state.api.logoutApi.mock.invocationCallOrder[0]!).toBeLessThan(
      state.api.loginApi.mock.invocationCallOrder[0]!,
    );
    expect(store.logoutPending).toBe(false);
  });

  it('does not submit credentials while pending logout remains unconfirmed', async () => {
    state.api.logoutApi.mockRejectedValue(new Error('still offline'));
    const store = useAuthStore();
    store.logoutPending = true;

    await expect(
      store.authLogin({ password: 'secret', username: 'operator' }),
    ).resolves.toEqual({ userInfo: null });

    expect(state.api.loginApi).not.toHaveBeenCalled();
    expect(store.logoutPending).toBe(true);
    expect(state.messages.error).toHaveBeenCalledWith('still offline');
  });

  it('blocks persisted access-token restoration while logout is pending', async () => {
    state.accessStore.accessToken = 'persisted-access-token';
    const store = useAuthStore();
    store.logoutPending = true;

    await expect(store.restoreSession()).resolves.toBe(false);

    expect(state.api.refreshTokenApi).not.toHaveBeenCalled();
  });

  it('expires once and does not redirect-loop on the login route', async () => {
    const store = useAuthStore();

    await store.expireSession();
    expect(state.resetRoutes).toHaveBeenCalledOnce();
    expect(state.router.replace).toHaveBeenCalledWith({
      path: '/auth/login',
      query: {
        expired: '1',
        redirect: encodeURIComponent('/player-equipment-grants?page=2'),
      },
    });

    state.router.replace.mockClear();
    state.router.currentRoute.value = {
      fullPath: '/auth/login',
      path: '/auth/login',
    };
    await store.expireSession();
    expect(state.router.replace).not.toHaveBeenCalled();
  });
});
