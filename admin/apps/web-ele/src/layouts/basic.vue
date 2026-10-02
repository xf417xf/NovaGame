<script lang="ts" setup>
import { computed } from 'vue';

import { AuthenticationLoginExpiredModal } from '@vben/common-ui';
import { BasicLayout, UserDropdown } from '@vben/layouts';
import { preferences } from '@vben/preferences';
import { useAccessStore, useUserStore } from '@vben/stores';

import { tOperations } from '#/locales/operations';
import { useAuthStore } from '#/store';
import LoginForm from '#/views/_core/authentication/login.vue';

import OperationsLanguageToggle from './operations-language-toggle.vue';

const userStore = useUserStore();
const authStore = useAuthStore();
const accessStore = useAccessStore();
const menus = computed(() => []);
const avatar = computed(
  () => userStore.userInfo?.avatar || preferences.app.defaultAvatar,
);
const roleLabel = computed(
  () => userStore.userInfo?.roles?.[0] ?? tOperations('operator'),
);

async function handleLogout() {
  await authStore.logout(false);
}
</script>

<template>
  <BasicLayout @clear-preferences-and-logout="handleLogout">
    <template #header-right-65>
      <OperationsLanguageToggle />
    </template>
    <template #user-dropdown>
      <UserDropdown
        :avatar
        :description="userStore.userInfo?.username"
        :menus
        :tag-text="roleLabel"
        :text="userStore.userInfo?.realName"
        @clear-preferences-and-logout="handleLogout"
        @logout="handleLogout"
      />
    </template>
    <template #extra>
      <AuthenticationLoginExpiredModal
        v-model:open="accessStore.loginExpired"
        :avatar
      >
        <LoginForm />
      </AuthenticationLoginExpiredModal>
    </template>
  </BasicLayout>
</template>
