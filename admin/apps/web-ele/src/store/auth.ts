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
} from '#/api';
import { $t } from '#/locales';
import {
  refreshAccessibleRoutes,
  resetAccessibleRoutes,
} from '#/router/access';

export const useAuthStore = defineStore('auth', () => {
  const accessStore = useAccessStore();
  const userStore = useUserStore();
  const router = useRouter();
  const loginLoading = ref(false);
  let restorePromise: null | Promise<boolean> = null;

  async function loadAuthenticatedState() {
    const [userInfo, accessCodes] = await Promise.all([
      getUserInfoApi(),
      getAccessCodesApi(),
    ]);
    userStore.setUserInfo(userInfo);
    accessStore.setAccessCodes(accessCodes);
    return userInfo;
  }

  async function authLogin(
    params: Recordable<any>,
    onSuccess?: () => Promise<void> | void,
  ) {
    let userInfo: null | UserInfo = null;
    try {
      loginLoading.value = true;
      const { accessToken } = await loginApi({
        password: String(params.password ?? ''),
        username: String(params.username ?? ''),
      });
      accessStore.setAccessToken(accessToken);
      userInfo = await loadAuthenticatedState();

      if (accessStore.loginExpired) {
        accessStore.setLoginExpired(false);
      } else if (onSuccess) {
        await onSuccess();
      } else {
        await router.push(userInfo.homePath || preferences.app.defaultHomePath);
      }

      ElNotification({
        message: `${$t('authentication.loginSuccessDesc')}: ${userInfo.realName}`,
        title: $t('authentication.loginSuccess'),
        type: 'success',
      });
    } catch (error) {
      ElMessage.error(toNovaGameApiError(error).message);
    } finally {
      loginLoading.value = false;
    }

    return { userInfo };
  }

  async function logout(redirect: boolean = true) {
    try {
      await logoutApi();
    } catch {
      // Local state still has to be cleared when the server is unavailable.
    }
    resetAccessibleRoutes(router);
    resetAllStores();
    accessStore.setLoginExpired(false);
    await router.replace({
      path: LOGIN_PATH,
      query: redirect
        ? { redirect: encodeURIComponent(router.currentRoute.value.fullPath) }
        : {},
    });
  }

  async function expireSession() {
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
    if (accessStore.accessToken) {
      return true;
    }
    if (restorePromise) {
      return restorePromise;
    }

    restorePromise = (async () => {
      try {
        const session = await refreshTokenApi();
        accessStore.setAccessToken(session.accessToken);
        await loadAuthenticatedState();
        return true;
      } catch {
        resetAllStores();
        return false;
      } finally {
        restorePromise = null;
      }
    })();

    return restorePromise;
  }

  async function fetchUserInfo() {
    const userInfo = await getUserInfoApi();
    userStore.setUserInfo(userInfo);
    return userInfo;
  }

  /**
   * Reconcile the current session after a server-authoritative permission change.
   * A forbidden refresh means the authenticated shell is no longer usable.
   */
  async function refreshAuthenticatedState() {
    try {
      const userInfo = await loadAuthenticatedState();
      const { accessibleMenus, accessibleRoutes } =
        await refreshAccessibleRoutes(router, accessStore.accessCodes);
      accessStore.setAccessMenus(accessibleMenus);
      accessStore.setAccessRoutes(accessibleRoutes);
      accessStore.setIsAccessChecked(true);
      return { accessibleRoutes, userInfo };
    } catch (error) {
      if (toNovaGameApiError(error).status === 403) {
        await expireSession();
        return null;
      }
      throw error;
    }
  }

  function $reset() {
    loginLoading.value = false;
    restorePromise = null;
  }

  return {
    $reset,
    authLogin,
    expireSession,
    fetchUserInfo,
    loginLoading,
    logout,
    refreshAuthenticatedState,
    restoreSession,
  };
});
