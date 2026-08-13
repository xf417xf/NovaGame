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

export const useAuthStore = defineStore(
  'auth',
  () => {
    const accessStore = useAccessStore();
    const userStore = useUserStore();
    const router = useRouter();
    const loginLoading = ref(false);
    const logoutPending = ref(false);
    let restorePromise: null | Promise<boolean> = null;

    async function resetAuthenticatedState(pendingLogout: boolean = false) {
      const { resetRoutes } = await import('#/router');
      resetRoutes();
      resetAllStores();
      logoutPending.value = pendingLogout;
      accessStore.setLoginExpired(false);
    }

    async function loadAuthenticatedState() {
      const [userInfo, accessCodes] = await Promise.all([
        getUserInfoApi(),
        getAccessCodesApi(),
      ]);
      userStore.setUserInfo(userInfo);
      accessStore.setAccessCodes(accessCodes);
      return userInfo;
    }

    async function retryPendingLogout() {
      if (!logoutPending.value) {
        return;
      }

      await logoutApi();
      logoutPending.value = false;
    }

    async function authLogin(
      params: Recordable<any>,
      onSuccess?: () => Promise<void> | void,
    ) {
      let serverSessionCreated = false;
      let userInfo: null | UserInfo = null;
      try {
        loginLoading.value = true;
        await retryPendingLogout();

        const { accessToken } = await loginApi({
          password: String(params.password ?? ''),
          username: String(params.username ?? ''),
        });
        serverSessionCreated = true;
        accessStore.setAccessToken(accessToken);
        userInfo = await loadAuthenticatedState();

        if (accessStore.loginExpired) {
          accessStore.setLoginExpired(false);
        } else if (onSuccess) {
          await onSuccess();
        } else {
          await router.push(
            userInfo.homePath || preferences.app.defaultHomePath,
          );
        }

        ElNotification({
          message: `${$t('authentication.loginSuccessDesc')}: ${userInfo.realName}`,
          title: $t('authentication.loginSuccess'),
          type: 'success',
        });
      } catch (error) {
        let pendingLogout = logoutPending.value;
        if (serverSessionCreated) {
          try {
            await logoutApi();
            pendingLogout = false;
          } catch {
            pendingLogout = true;
          }
        }

        await resetAuthenticatedState(pendingLogout);
        ElMessage.error(toNovaGameApiError(error).message);
      } finally {
        loginLoading.value = false;
      }

      return { userInfo };
    }

    async function logout(redirect: boolean = true) {
      let logoutError: null | unknown = null;
      try {
        await logoutApi();
      } catch (error) {
        logoutError = error;
      }

      const redirectPath = router.currentRoute.value.fullPath;
      await resetAuthenticatedState(logoutError !== null);
      await router.replace({
        path: LOGIN_PATH,
        query: redirect ? { redirect: encodeURIComponent(redirectPath) } : {},
      });

      if (logoutError) {
        ElMessage.error(
          `Logout could not be confirmed. ${toNovaGameApiError(logoutError).message}`,
        );
      }
    }

    async function expireSession() {
      const redirectPath = router.currentRoute.value.fullPath;
      const requiresRedirect = router.currentRoute.value.path !== LOGIN_PATH;
      await resetAuthenticatedState();
      if (requiresRedirect) {
        await router.replace({
          path: LOGIN_PATH,
          query: {
            expired: '1',
            redirect: encodeURIComponent(redirectPath),
          },
        });
      }
    }

    async function restoreSession() {
      if (logoutPending.value) {
        return false;
      }
      if (accessStore.accessToken) {
        return true;
      }
      if (restorePromise) {
        return restorePromise;
      }

      restorePromise = (async () => {
        let serverSessionCreated = false;
        try {
          const session = await refreshTokenApi();
          serverSessionCreated = true;
          accessStore.setAccessToken(session.accessToken);
          await loadAuthenticatedState();
          return true;
        } catch {
          let pendingLogout = false;
          if (serverSessionCreated) {
            try {
              await logoutApi();
            } catch {
              pendingLogout = true;
            }
          }
          await resetAuthenticatedState(pendingLogout);
          return false;
        } finally {
          restorePromise = null;
        }
      })();

      return restorePromise;
    }

    async function fetchUserInfo() {
      return loadAuthenticatedState();
    }

    function $reset() {
      loginLoading.value = false;
      logoutPending.value = false;
      restorePromise = null;
    }

    return {
      $reset,
      authLogin,
      expireSession,
      fetchUserInfo,
      loginLoading,
      logout,
      logoutPending,
      restoreSession,
    };
  },
  {
    persist: {
      pick: ['logoutPending'],
    },
  },
);
