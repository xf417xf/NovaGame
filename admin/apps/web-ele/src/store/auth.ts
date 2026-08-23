import type { Recordable, UserInfo } from '@vben/types';

import { ref } from 'vue';
import { useRouter } from 'vue-router';

import { LOGIN_PATH } from '@vben/constants';
import { preferences } from '@vben/preferences';
import { resetAllStores, useAccessStore, useUserStore } from '@vben/stores';

import { ElMessage, ElNotification } from 'element-plus';
import { defineStore } from 'pinia';

import {
  getAccessCodesApi,
  getUserInfoApi,
  loginApi,
  logoutApi,
  refreshTokenApi,
  toNovaGameApiError,
  waitForRefreshAccessToken,
} from '#/api';
import { $t } from '#/locales';
import {
  refreshAccessibleRoutes,
  resetAccessibleRoutes,
} from '#/router/access';

interface AuthenticatedStateRequest {
  authenticationGeneration: number;
  revision: number;
}

type LoginResult = Awaited<ReturnType<typeof loginApi>>;

function createSessionRefreshCancellation() {
  return Object.assign(new Error('The session refresh was superseded.'), {
    __CANCEL__: true,
    code: 'ERR_CANCELED',
    name: 'CanceledError',
  });
}

export const useAuthStore = defineStore('auth', () => {
  const accessStore = useAccessStore();
  const userStore = useUserStore();
  const router = useRouter();
  const loginLoading = ref(false);
  let authenticationGeneration = 0;
  let authenticatedStateRevision = 0;
  let logoutPromise: null | Promise<void> = null;
  const pendingLoginRequests = new Set<Promise<LoginResult>>();
  let restorePromise: null | Promise<boolean> = null;

  function trackLoginRequest(request: Promise<LoginResult>) {
    pendingLoginRequests.add(request);
    void request.then(
      () => {
        pendingLoginRequests.delete(request);
      },
      () => {
        pendingLoginRequests.delete(request);
      },
    );
    return request;
  }

  async function waitForPendingLoginRequests() {
    while (pendingLoginRequests.size > 0) {
      await Promise.allSettled(pendingLoginRequests);
    }
  }

  function invalidatePendingAuthenticationRestore() {
    authenticationGeneration += 1;
    authenticatedStateRevision += 1;
    restorePromise = null;
    return authenticationGeneration;
  }

  function isCurrentAuthenticationGeneration(generation: number) {
    return generation === authenticationGeneration;
  }

  function beginAuthenticatedStateRequest(
    generation: number,
  ): AuthenticatedStateRequest {
    authenticatedStateRevision += 1;
    return {
      authenticationGeneration: generation,
      revision: authenticatedStateRevision,
    };
  }

  function isCurrentAuthenticatedStateRequest(
    request: AuthenticatedStateRequest,
  ) {
    return (
      isCurrentAuthenticationGeneration(request.authenticationGeneration) &&
      request.revision === authenticatedStateRevision
    );
  }

  async function requestAuthenticatedState() {
    const [userInfo, accessCodes] = await Promise.all([
      getUserInfoApi(),
      getAccessCodesApi(),
    ]);
    return { accessCodes, userInfo };
  }

  function applyAuthenticatedState({
    accessCodes,
    userInfo,
  }: Awaited<ReturnType<typeof requestAuthenticatedState>>) {
    userStore.setUserInfo(userInfo);
    accessStore.setAccessCodes(accessCodes);
    return userInfo;
  }

  async function loadAuthenticatedState(request: AuthenticatedStateRequest) {
    const authenticatedState = await requestAuthenticatedState();
    if (!isCurrentAuthenticatedStateRequest(request)) {
      return null;
    }
    return applyAuthenticatedState(authenticatedState);
  }

  async function authLogin(
    params: Recordable<any>,
    onSuccess?: () => Promise<void> | void,
  ) {
    const pendingLogout = logoutPromise;
    const loginGeneration = invalidatePendingAuthenticationRestore();
    let userInfo: null | UserInfo = null;
    try {
      loginLoading.value = true;
      if (pendingLogout) {
        await pendingLogout;
      }
      const { accessToken } = await trackLoginRequest(
        loginApi({
          password: String(params.password ?? ''),
          username: String(params.username ?? ''),
        }),
      );
      if (!isCurrentAuthenticationGeneration(loginGeneration)) {
        return { userInfo };
      }
      accessStore.setAccessToken(accessToken);
      const loginStateRequest = beginAuthenticatedStateRequest(loginGeneration);
      try {
        userInfo = await loadAuthenticatedState(loginStateRequest);
      } catch (error) {
        if (isCurrentAuthenticatedStateRequest(loginStateRequest)) {
          const apiError = toNovaGameApiError(error);
          if (apiError.status === 401 || apiError.status === 403) {
            await expireSession();
          }
          ElMessage.error(apiError.message);
        }
        return { userInfo: null };
      }
      if (!userInfo || !isCurrentAuthenticatedStateRequest(loginStateRequest)) {
        return { userInfo: null };
      }

      if (accessStore.loginExpired) {
        accessStore.setLoginExpired(false);
      } else if (onSuccess) {
        await onSuccess();
      } else {
        await router.push(userInfo.homePath || preferences.app.defaultHomePath);
      }
      if (!isCurrentAuthenticatedStateRequest(loginStateRequest)) {
        return { userInfo: null };
      }

      ElNotification({
        message: `${$t('authentication.loginSuccessDesc')}: ${userInfo.realName}`,
        title: $t('authentication.loginSuccess'),
        type: 'success',
      });
    } catch (error) {
      if (!isCurrentAuthenticationGeneration(loginGeneration)) {
        return { userInfo: null };
      }
      ElMessage.error(toNovaGameApiError(error).message);
    } finally {
      if (isCurrentAuthenticationGeneration(loginGeneration)) {
        loginLoading.value = false;
      }
    }

    return { userInfo };
  }

  async function logout(redirect: boolean = true) {
    if (logoutPromise) {
      await logoutPromise;
      return;
    }

    invalidatePendingAuthenticationRestore();
    loginLoading.value = false;
    const redirectPath = router.currentRoute.value.fullPath;
    const pendingLogout = (async () => {
      await waitForPendingLoginRequests();
      await waitForRefreshAccessToken();
      try {
        await logoutApi();
      } catch {
        // Local state still has to be cleared when the server is unavailable.
      }
      await router.replace({
        path: LOGIN_PATH,
        query: redirect
          ? {
              redirect: encodeURIComponent(redirectPath),
            }
          : {},
      });
    })();
    logoutPromise = pendingLogout;
    resetAccessibleRoutes(router);
    resetAllStores();
    accessStore.setLoginExpired(false);
    try {
      await pendingLogout;
    } finally {
      if (logoutPromise === pendingLogout) {
        logoutPromise = null;
      }
    }
  }

  async function expireSession() {
    invalidatePendingAuthenticationRestore();
    loginLoading.value = false;
    resetAccessibleRoutes(router);
    resetAllStores();
    accessStore.setLoginExpired(false);
    if (router.currentRoute.value.path !== LOGIN_PATH) {
      await router.replace({
        path: LOGIN_PATH,
        query: {
          expired: '1',
          redirect: encodeURIComponent(router.currentRoute.value.fullPath),
        },
      });
    }
  }

  async function restoreSession() {
    const pendingLogout = logoutPromise;
    if (pendingLogout) {
      try {
        await pendingLogout;
      } catch {
        // Logout already cleared local state; do not start a replacement refresh.
      }
      return false;
    }
    if (accessStore.accessToken) {
      return true;
    }
    if (restorePromise) {
      return restorePromise;
    }

    const restoreStateRequest = beginAuthenticatedStateRequest(
      authenticationGeneration,
    );
    const pendingRestore = (async () => {
      try {
        const session = await refreshTokenApi();
        if (!isCurrentAuthenticatedStateRequest(restoreStateRequest)) {
          return false;
        }

        accessStore.setAccessToken(session.accessToken);
        return Boolean(await loadAuthenticatedState(restoreStateRequest));
      } catch {
        if (isCurrentAuthenticatedStateRequest(restoreStateRequest)) {
          resetAllStores();
        }
        return false;
      }
    })();

    restorePromise = pendingRestore;
    void pendingRestore.then(
      () => {
        if (restorePromise === pendingRestore) {
          restorePromise = null;
        }
      },
      () => {
        if (restorePromise === pendingRestore) {
          restorePromise = null;
        }
      },
    );
    return pendingRestore;
  }

  async function refreshAccessTokenForRequest(): Promise<string> {
    const refreshGeneration = authenticationGeneration;
    if (logoutPromise) {
      throw createSessionRefreshCancellation();
    }

    const session = await refreshTokenApi();
    if (
      logoutPromise ||
      !isCurrentAuthenticationGeneration(refreshGeneration)
    ) {
      throw createSessionRefreshCancellation();
    }

    accessStore.setAccessToken(session.accessToken);
    return session.accessToken;
  }

  async function fetchUserInfo() {
    const fetchStateRequest = beginAuthenticatedStateRequest(
      authenticationGeneration,
    );
    try {
      return await loadAuthenticatedState(fetchStateRequest);
    } catch (error) {
      if (!isCurrentAuthenticatedStateRequest(fetchStateRequest)) {
        return null;
      }

      const status = toNovaGameApiError(error).status;
      if (status === 401 || status === 403) {
        await expireSession();
        return null;
      }

      throw error;
    }
  }

  /**
   * Reconcile the current session after a server-authoritative permission change.
   * A forbidden refresh means the authenticated shell is no longer usable.
   */
  async function refreshAuthenticatedState() {
    const refreshStateRequest = beginAuthenticatedStateRequest(
      authenticationGeneration,
    );
    try {
      const userInfo = await loadAuthenticatedState(refreshStateRequest);
      if (!userInfo) {
        return null;
      }
      const refreshedRoutes = await refreshAccessibleRoutes(
        router,
        accessStore.accessCodes,
        () => isCurrentAuthenticatedStateRequest(refreshStateRequest),
      );
      if (
        !refreshedRoutes ||
        !isCurrentAuthenticatedStateRequest(refreshStateRequest)
      ) {
        return null;
      }
      const { accessibleMenus, accessibleRoutes } = refreshedRoutes;
      accessStore.setAccessMenus(accessibleMenus);
      accessStore.setAccessRoutes(accessibleRoutes);
      accessStore.setIsAccessChecked(true);
      return { accessibleRoutes, userInfo };
    } catch (error) {
      if (!isCurrentAuthenticatedStateRequest(refreshStateRequest)) {
        return null;
      }
      if (toNovaGameApiError(error).status === 403) {
        await expireSession();
        return null;
      }
      throw error;
    }
  }

  function $reset() {
    invalidatePendingAuthenticationRestore();
    loginLoading.value = false;
  }

  return {
    $reset,
    authLogin,
    expireSession,
    fetchUserInfo,
    loginLoading,
    logout,
    refreshAccessTokenForRequest,
    refreshAuthenticatedState,
    restoreSession,
  };
});
