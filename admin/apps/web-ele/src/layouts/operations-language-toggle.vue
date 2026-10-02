<script lang="ts" setup>
import type { SupportedLanguagesType } from '@vben/locales';

import { ref } from 'vue';

import { SUPPORT_LANGUAGES } from '@vben/constants';
import { Languages } from '@vben/icons';
import { loadLocaleMessages } from '@vben/locales';
import { preferences, updatePreferences } from '@vben/preferences';

import {
  ElButton,
  ElDropdown,
  ElDropdownItem,
  ElDropdownMenu,
} from 'element-plus';

import { tOperations } from '#/locales/operations';

const switching = ref(false);

async function changeLanguage(locale: SupportedLanguagesType) {
  if (switching.value || locale === preferences.app.locale) return;
  switching.value = true;
  try {
    await loadLocaleMessages(locale);
    updatePreferences({ app: { locale } });
  } finally {
    switching.value = false;
  }
}
</script>

<template>
  <ElDropdown trigger="click" @command="changeLanguage">
    <ElButton
      :aria-label="tOperations('language')"
      :loading="switching"
      data-testid="operations-language-toggle"
      text
    >
      <Languages :size="16" />
      <span class="ml-1">{{ tOperations('language') }}</span>
    </ElButton>
    <template #dropdown>
      <ElDropdownMenu>
        <ElDropdownItem
          v-for="language in SUPPORT_LANGUAGES"
          :key="language.value"
          :command="language.value"
          :disabled="switching || language.value === preferences.app.locale"
          :data-testid="`operations-language-${language.value}`"
        >
          {{ language.label }}
        </ElDropdownItem>
      </ElDropdownMenu>
    </template>
  </ElDropdown>
</template>
