<script lang="ts" setup>
import type { AdministratorDto } from '#/generated/admin-access/types.gen';

import { onBeforeUnmount, reactive, ref, watch } from 'vue';

import { RotateCw } from '@vben/icons';

import {
  ElAlert,
  ElButton,
  ElDrawer,
  ElForm,
  ElFormItem,
  ElInput,
} from 'element-plus';

import {
  changeAdministratorDisplayName,
  getAdministrator,
  isNovaGameRequestCancelled,
  toNovaGameApiError,
} from '#/api';
import { AdminAccessErrorCode } from '#/features/admin-access/permissions';

import {
  fieldErrorsFrom,
  normalizeDisplayName,
  validateAdministratorDisplayName,
} from './administrator-form-utils';

interface Props {
  administratorId: null | string;
  canManage: boolean;
  modelValue: boolean;
}

interface StaleDisplayNameDraft {
  displayName: string;
  kind: 'display-name';
}

const props = defineProps<Props>();
const emit = defineEmits<{
  forbidden: [];
  stale: [draft: null | StaleDisplayNameDraft];
  'update:modelValue': [value: boolean];
  updated: [administrator: AdministratorDto];
}>();

const detail = ref<Awaited<ReturnType<typeof getAdministrator>> | null>(null);
const errorMessage = ref('');
const fieldErrors = ref<Record<string, string>>({});
const loading = ref(false);
const saving = ref(false);
const conflictMessage = ref('');
const form = reactive({ displayName: '' });
let loadSequence = 0;
let hasLocalInput: boolean = false;

function close(force = false) {
  if (force || !saving.value) {
    emit('update:modelValue', false);
  }
}

function closeForForbidden() {
  clearState();
  emit('update:modelValue', false);
  emit('forbidden');
}

function clearState() {
  detail.value = null;
  errorMessage.value = '';
  fieldErrors.value = {};
  conflictMessage.value = '';
  form.displayName = '';
  hasLocalInput = false;
}

function closeForStaleDetail() {
  const draft = hasLocalInput
    ? { displayName: form.displayName, kind: 'display-name' as const }
    : null;
  emit('stale', draft);
  close(true);
}

async function loadDetail(preserveInput = false) {
  const sequence = ++loadSequence;
  const administratorId = props.administratorId;
  detail.value = null;
  errorMessage.value = '';
  fieldErrors.value = {};
  conflictMessage.value = '';

  if (!props.modelValue || !administratorId) {
    loading.value = false;
    return;
  }

  loading.value = true;
  try {
    const response = await getAdministrator(administratorId);
    if (
      sequence !== loadSequence ||
      !props.modelValue ||
      props.administratorId !== administratorId
    ) {
      return;
    }

    detail.value = response;
    if (!preserveInput || !hasLocalInput) {
      form.displayName = response.data.displayName ?? '';
      hasLocalInput = false;
    }
  } catch (error) {
    if (
      isNovaGameRequestCancelled(error) ||
      sequence !== loadSequence ||
      !props.modelValue ||
      props.administratorId !== administratorId
    ) {
      return;
    }

    const apiError = toNovaGameApiError(error);
    if (
      apiError.status === 404 &&
      apiError.code === AdminAccessErrorCode.AdministratorNotFound
    ) {
      closeForStaleDetail();
      return;
    }
    if (apiError.status === 403) {
      closeForForbidden();
      return;
    }
    errorMessage.value = apiError.message;
  } finally {
    if (sequence === loadSequence) {
      loading.value = false;
    }
  }
}

function validateForm(): boolean {
  const displayNameError = validateAdministratorDisplayName(form.displayName);
  fieldErrors.value = displayNameError ? { displayName: displayNameError } : {};
  return !displayNameError;
}

function markLocalInput() {
  hasLocalInput = true;
}

async function submit() {
  const currentDetail = detail.value;
  if (!props.canManage || saving.value || !currentDetail || !validateForm()) {
    return;
  }

  saving.value = true;
  conflictMessage.value = '';
  errorMessage.value = '';
  try {
    const response = await changeAdministratorDisplayName(
      currentDetail.data.id ?? '',
      {
        displayName: normalizeDisplayName(form.displayName) ?? form.displayName,
      },
      currentDetail.etag,
    );
    detail.value = response;
    emit('updated', response.data);
    close(true);
  } catch (error) {
    if (isNovaGameRequestCancelled(error)) {
      return;
    }

    const apiError = toNovaGameApiError(error);
    if (
      apiError.status === 404 &&
      apiError.code === AdminAccessErrorCode.AdministratorNotFound
    ) {
      closeForStaleDetail();
      return;
    }
    if (apiError.status === 403) {
      closeForForbidden();
      return;
    }
    fieldErrors.value = fieldErrorsFrom(apiError);
    if (apiError.status === 409) {
      conflictMessage.value = apiError.message;
      return;
    }
    errorMessage.value = apiError.message;
  } finally {
    saving.value = false;
  }
}

watch(
  () => [props.modelValue, props.administratorId] as const,
  ([open], previous) => {
    if (!open) {
      clearState();
      return;
    }
    const wasOpen = previous?.[0] ?? false;
    if (!wasOpen || previous?.[1] !== props.administratorId) {
      void loadDetail();
    }
  },
  { immediate: true },
);

onBeforeUnmount(() => {
  loadSequence += 1;
  clearState();
});
</script>

<template>
  <ElDrawer
    :close-on-click-modal="!saving"
    :close-on-press-escape="!saving"
    :model-value="modelValue"
    :show-close="!saving"
    destroy-on-close
    size="min(520px, 100vw)"
    title="Edit display name"
    @close="close"
  >
    <div
      v-loading="loading"
      class="administrator-drawer-content"
      data-testid="administrator-display-name-drawer"
    >
      <ElAlert
        v-if="conflictMessage"
        :closable="false"
        :title="conflictMessage"
        show-icon
        type="warning"
      >
        <template #default>
          <ElButton
            data-testid="reload-conflicted-display-name"
            size="small"
            @click="loadDetail(true)"
          >
            <RotateCw :size="15" />
            Reload and review
          </ElButton>
        </template>
      </ElAlert>

      <ElAlert
        v-if="errorMessage"
        :closable="false"
        :title="errorMessage"
        show-icon
        type="error"
      >
        <template #default>
          <ElButton size="small" @click="loadDetail(true)">Retry</ElButton>
        </template>
      </ElAlert>

      <ElForm v-if="detail" label-position="top">
        <ElFormItem label="User name">
          <ElInput :model-value="detail.data.userName ?? ''" disabled />
        </ElFormItem>
        <ElFormItem :error="fieldErrors.displayName" label="Display name">
          <ElInput
            v-model="form.displayName"
            :disabled="saving || !canManage"
            data-testid="administrator-display-name"
            maxlength="128"
            @update:model-value="markLocalInput"
          />
        </ElFormItem>
      </ElForm>
    </div>

    <template #footer>
      <div class="drawer-footer">
        <ElButton :disabled="saving" @click="close()">Cancel</ElButton>
        <ElButton
          :disabled="!detail || !canManage"
          :loading="saving"
          data-testid="save-administrator-display-name"
          type="primary"
          @click="submit"
        >
          Save display name
        </ElButton>
      </div>
    </template>
  </ElDrawer>
</template>

<style scoped>
.administrator-drawer-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-height: 260px;
}

.drawer-footer {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}

@media (max-width: 640px) {
  .drawer-footer {
    display: grid;
    grid-template-columns: 1fr 1fr;
  }

  .drawer-footer :deep(.el-button) {
    min-height: 44px;
    margin-left: 0;
  }
}
</style>
