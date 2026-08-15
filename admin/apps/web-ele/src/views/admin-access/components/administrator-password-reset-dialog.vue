<script lang="ts" setup>
import type { AdministratorDto } from '#/generated/admin-access/types.gen';

import { onBeforeUnmount, reactive, ref, watch } from 'vue';

import { RotateCw } from '@vben/icons';

import {
  ElAlert,
  ElButton,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
} from 'element-plus';

import {
  getAdministrator,
  isNovaGameRequestCancelled,
  resetAdministratorPassword,
  toNovaGameApiError,
} from '#/api';

import {
  createAdministratorIdempotencyKey,
  fieldErrorsFrom,
  isUncertainCommandError,
  validateAdministratorPassword,
} from './administrator-form-utils';

interface Props {
  administratorId: null | string;
  canResetPassword: boolean;
  modelValue: boolean;
}

const props = defineProps<Props>();
const emit = defineEmits<{
  forbidden: [];
  stale: [];
  'update:modelValue': [value: boolean];
  updated: [administrator: AdministratorDto];
}>();

const detail = ref<Awaited<ReturnType<typeof getAdministrator>> | null>(null);
const conflictMessage = ref('');
const errorMessage = ref('');
const fieldErrors = ref<Record<string, string>>({});
const loading = ref(false);
const pending = ref(false);
const retryLocked = ref(false);
const idempotencyKey = ref<null | string>(null);
const form = reactive({ newPassword: '' });
let loadSequence = 0;

function clearSecret() {
  form.newPassword = '';
}

function close(force = false) {
  if (force || !pending.value) {
    clearSecret();
    idempotencyKey.value = null;
    retryLocked.value = false;
    emit('update:modelValue', false);
  }
}

function closeForForbidden() {
  clearState();
  clearSecret();
  idempotencyKey.value = null;
  retryLocked.value = false;
  emit('update:modelValue', false);
  emit('forbidden');
}

function lockRetryAfterUncertainOutcome() {
  errorMessage.value =
    'The password reset may have completed. Retry only with the unchanged password.';
  retryLocked.value = true;
}

function clearState() {
  detail.value = null;
  conflictMessage.value = '';
  errorMessage.value = '';
  fieldErrors.value = {};
}

function validateForm(): boolean {
  const passwordError = validateAdministratorPassword(form.newPassword);
  fieldErrors.value = passwordError ? { newPassword: passwordError } : {};
  return !passwordError;
}

function ensureIdempotencyKey(): boolean {
  if (idempotencyKey.value) {
    return true;
  }
  try {
    idempotencyKey.value = createAdministratorIdempotencyKey();
    return true;
  } catch (error) {
    errorMessage.value =
      error instanceof Error ? error.message : 'Unable to reset the password.';
    return false;
  }
}

async function loadDetail() {
  const sequence = ++loadSequence;
  const administratorId = props.administratorId;
  clearState();
  if (!props.modelValue || !administratorId) {
    loading.value = false;
    return;
  }

  loading.value = true;
  try {
    const response = await getAdministrator(administratorId);
    if (
      sequence === loadSequence &&
      props.modelValue &&
      props.administratorId === administratorId
    ) {
      detail.value = response;
    }
  } catch (error) {
    if (isNovaGameRequestCancelled(error) || sequence !== loadSequence) {
      return;
    }
    const apiError = toNovaGameApiError(error);
    if (apiError.status === 404) {
      emit('stale');
      close(true);
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

async function reloadAfterConflict() {
  clearSecret();
  idempotencyKey.value = null;
  retryLocked.value = false;
  await loadDetail();
}

function startNewIntent() {
  if (!retryLocked.value) {
    return;
  }
  clearSecret();
  idempotencyKey.value = null;
  retryLocked.value = false;
  errorMessage.value = '';
}

async function submit() {
  const currentDetail = detail.value;
  if (
    !currentDetail ||
    !props.canResetPassword ||
    pending.value ||
    !validateForm() ||
    !ensureIdempotencyKey()
  ) {
    return;
  }

  pending.value = true;
  conflictMessage.value = '';
  errorMessage.value = '';
  try {
    const response = await resetAdministratorPassword(
      currentDetail.data.id ?? '',
      { newPassword: form.newPassword },
      currentDetail.etag,
      idempotencyKey.value ?? '',
    );
    detail.value = response;
    clearSecret();
    emit('updated', response.data);
    close(true);
  } catch (error) {
    if (isNovaGameRequestCancelled(error)) {
      lockRetryAfterUncertainOutcome();
      return;
    }
    const apiError = toNovaGameApiError(error);
    if (apiError.status === 403) {
      closeForForbidden();
      return;
    }
    fieldErrors.value = fieldErrorsFrom(apiError);
    if (isUncertainCommandError(apiError)) {
      lockRetryAfterUncertainOutcome();
      return;
    }

    clearSecret();
    idempotencyKey.value = null;
    retryLocked.value = false;
    if (apiError.status === 409) {
      conflictMessage.value = apiError.message;
      return;
    }
    errorMessage.value = apiError.message;
  } finally {
    pending.value = false;
  }
}

watch(
  () => [props.modelValue, props.administratorId] as const,
  ([open], previous) => {
    if (!open) {
      loadSequence += 1;
      clearSecret();
      return;
    }
    if (!previous || !previous[0] || previous[1] !== props.administratorId) {
      idempotencyKey.value = null;
      retryLocked.value = false;
      clearSecret();
      void loadDetail();
    }
  },
  { immediate: true },
);

onBeforeUnmount(() => {
  loadSequence += 1;
  clearSecret();
  idempotencyKey.value = null;
});
</script>

<template>
  <ElDialog
    :close-on-click-modal="!pending"
    :close-on-press-escape="!pending"
    :model-value="modelValue"
    :show-close="!pending"
    destroy-on-close
    title="Reset administrator password"
    width="min(540px, 94vw)"
    @close="close()"
  >
    <div
      v-loading="loading"
      class="password-reset-content"
      data-testid="administrator-password-reset-dialog"
    >
      <ElAlert
        :closable="false"
        title="Resetting a password revokes the administrator's active refresh sessions."
        type="warning"
      />

      <ElAlert
        v-if="conflictMessage"
        :closable="false"
        :title="conflictMessage"
        show-icon
        type="warning"
      >
        <template #default>
          <ElButton
            data-testid="reload-conflicted-password-reset"
            size="small"
            @click="reloadAfterConflict"
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
          <div v-if="retryLocked" class="retry-actions">
            <span>Retry only with the unchanged password.</span>
            <ElButton
              data-testid="restart-password-reset"
              size="small"
              @click="startNewIntent"
            >
              Start new intent
            </ElButton>
          </div>
        </template>
      </ElAlert>

      <ElForm v-if="detail" label-position="top">
        <ElFormItem label="Administrator">
          <span>{{ detail.data.displayName || detail.data.userName }}</span>
        </ElFormItem>
        <ElFormItem :error="fieldErrors.newPassword" label="New password">
          <ElInput
            v-model="form.newPassword"
            :disabled="pending || retryLocked || !canResetPassword"
            autocomplete="new-password"
            data-testid="administrator-new-password"
            maxlength="256"
            type="password"
          />
        </ElFormItem>
      </ElForm>
    </div>

    <template #footer>
      <div class="dialog-footer">
        <ElButton :disabled="pending" @click="close()">Cancel</ElButton>
        <ElButton
          :disabled="!detail || !canResetPassword"
          :loading="pending"
          data-testid="confirm-password-reset"
          type="danger"
          @click="submit"
        >
          Reset password
        </ElButton>
      </div>
    </template>
  </ElDialog>
</template>

<style scoped>
.password-reset-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-height: 220px;
}

.retry-actions {
  display: flex;
  gap: 8px;
  align-items: center;
  justify-content: space-between;
}

.dialog-footer {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}

@media (max-width: 640px) {
  .dialog-footer {
    display: grid;
    grid-template-columns: 1fr 1fr;
  }

  .dialog-footer :deep(.el-button) {
    min-height: 44px;
    margin-left: 0;
  }

  .retry-actions {
    flex-direction: column;
    align-items: flex-start;
  }
}
</style>
