<script lang="ts" setup>
import type {
  AdministratorDto,
  AdminRoleSummaryDto,
} from '#/generated/admin-access/types.gen';

import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue';

import { RotateCw } from '@vben/icons';

import {
  ElAlert,
  ElButton,
  ElDrawer,
  ElForm,
  ElFormItem,
  ElInput,
  ElOption,
  ElSelect,
} from 'element-plus';

import {
  createAdministrator,
  isNovaGameRequestCancelled,
  listAdminRoles,
  toNovaGameApiError,
} from '#/api';
import {
  operationsErrorMessage,
  tOperations,
  useOperationsMessage,
} from '#/locales/operations';

import {
  createAdministratorIdempotencyKey,
  fieldErrorsFrom,
  isUncertainCommandError,
  normalizeDisplayName,
  validateAdministratorDisplayName,
  validateAdministratorPassword,
  validateAdministratorRoleIds,
  validateAdministratorUserName,
} from './administrator-form-utils';

interface Props {
  canManage: boolean;
  canReadRoles: boolean;
  modelValue: boolean;
}

const props = defineProps<Props>();
const emit = defineEmits<{
  conflict: [];
  created: [administrator: AdministratorDto];
  forbidden: [];
  'update:modelValue': [value: boolean];
}>();

const conflictMessage = useOperationsMessage();
const fieldErrors = ref<Record<string, string>>({});
const formError = useOperationsMessage();
const loadingRoles = ref(false);
const roleOptions = ref<AdminRoleSummaryDto[]>([]);
const saving = ref(false);
const retryLocked = ref(false);
const idempotencyKey = ref<null | string>(null);
const form = reactive({
  displayName: '',
  initialPassword: '',
  roleIds: [] as string[],
  userName: '',
});
let roleLoadSequence = 0;

const formDisabled = computed(() => saving.value || retryLocked.value);

function clearSecrets() {
  form.initialPassword = '';
}

function resetForm() {
  conflictMessage.value = '';
  fieldErrors.value = {};
  form.displayName = '';
  formError.value = '';
  form.roleIds = [];
  form.userName = '';
  idempotencyKey.value = null;
  retryLocked.value = false;
  clearSecrets();
}

function close(force = false) {
  if (saving.value && !force) {
    return;
  }
  resetForm();
  emit('update:modelValue', false);
}

function closeForForbidden() {
  resetForm();
  emit('update:modelValue', false);
  emit('forbidden');
}

function lockRetryAfterUncertainOutcome() {
  formError.value = () =>
    tOperations(
      'theCreateRequestMayHaveCompletedRetryOnlyWithTheUnchangedFieldsAndPassword',
    );
  retryLocked.value = true;
}

function retryOrRestart() {
  if (!retryLocked.value) {
    return;
  }
  idempotencyKey.value = null;
  retryLocked.value = false;
  clearSecrets();
  formError.value = '';
}

function reloadConflict() {
  emit('conflict');
}

async function loadRoleOptions(search = '') {
  const sequence = ++roleLoadSequence;
  if (!props.canReadRoles || !props.modelValue) {
    roleOptions.value = [];
    loadingRoles.value = false;
    return;
  }

  loadingRoles.value = true;
  try {
    const response = await listAdminRoles({
      IsActive: true,
      Page: 1,
      PageSize: 100,
      Search: search.trim() || undefined,
    });
    if (
      sequence === roleLoadSequence &&
      props.canReadRoles &&
      props.modelValue
    ) {
      roleOptions.value = response.data.items ?? [];
    }
  } catch (error) {
    if (isNovaGameRequestCancelled(error) || sequence !== roleLoadSequence) {
      return;
    }
    const apiError = toNovaGameApiError(error);
    if (apiError.status === 403) {
      closeForForbidden();
      return;
    }
    formError.value = () => operationsErrorMessage(apiError);
  } finally {
    if (sequence === roleLoadSequence) {
      loadingRoles.value = false;
    }
  }
}

function validateForm(): boolean {
  const errors = {
    displayName: validateAdministratorDisplayName(form.displayName),
    initialPassword: validateAdministratorPassword(form.initialPassword),
    roleIds: validateAdministratorRoleIds(form.roleIds),
    userName: validateAdministratorUserName(form.userName),
  };
  fieldErrors.value = Object.fromEntries(
    Object.entries(errors).filter(([, value]) => value),
  ) as Record<string, string>;
  return Object.keys(fieldErrors.value).length === 0;
}

function ensureIdempotencyKey(): boolean {
  if (idempotencyKey.value) {
    return true;
  }
  try {
    idempotencyKey.value = createAdministratorIdempotencyKey();
    return true;
  } catch (error) {
    formError.value =
      error instanceof Error
        ? error.message
        : tOperations('unableToCreateThisCommand');
    return false;
  }
}

async function submit() {
  if (
    !props.canManage ||
    saving.value ||
    !validateForm() ||
    !ensureIdempotencyKey()
  ) {
    return;
  }

  saving.value = true;
  fieldErrors.value = {};
  formError.value = '';
  try {
    const response = await createAdministrator(
      {
        displayName: normalizeDisplayName(form.displayName) ?? form.displayName,
        initialPassword: form.initialPassword,
        roleIds: [...form.roleIds],
        userName: form.userName.trim(),
      },
      idempotencyKey.value ?? '',
    );
    clearSecrets();
    emit('created', response.data);
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
    formError.value = () => operationsErrorMessage(apiError);
    if (isUncertainCommandError(apiError)) {
      lockRetryAfterUncertainOutcome();
      return;
    }

    clearSecrets();
    idempotencyKey.value = null;
    retryLocked.value = false;
    if (apiError.status === 409) {
      conflictMessage.value = () => operationsErrorMessage(apiError);
      emit('conflict');
    }
  } finally {
    saving.value = false;
  }
}

watch(
  () => props.modelValue,
  (open, wasOpen) => {
    if (!open) {
      roleLoadSequence += 1;
      clearSecrets();
      return;
    }
    if (!wasOpen) {
      resetForm();
      void loadRoleOptions();
    }
  },
  { immediate: true },
);

watch(
  () => props.canReadRoles,
  (canReadRoles) => {
    if (!canReadRoles) {
      roleLoadSequence += 1;
      loadingRoles.value = false;
      roleOptions.value = [];
      form.roleIds = [];
      return;
    }
    if (props.modelValue) {
      void loadRoleOptions();
    }
  },
);

onBeforeUnmount(() => {
  roleLoadSequence += 1;
  clearSecrets();
});
</script>

<template>
  <ElDrawer
    :close-on-click-modal="!saving"
    :close-on-press-escape="!saving"
    :model-value="modelValue"
    :show-close="!saving"
    destroy-on-close
    size="min(620px, 100vw)"
    :title="tOperations('createAdministrator')"
    @close="close"
  >
    <div
      class="administrator-create-content"
      data-testid="administrator-create-drawer"
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
            data-testid="reload-conflicted-administrator-create"
            size="small"
            @click="reloadConflict"
          >
            <RotateCw :size="15" />
            {{ tOperations('reloadAndReview') }}
          </ElButton>
        </template>
      </ElAlert>

      <ElAlert
        v-if="formError"
        :closable="false"
        :title="formError"
        show-icon
        type="error"
      >
        <template #default>
          <div v-if="retryLocked" class="retry-actions">
            <span>{{
              tOperations('retryOnlyWithTheUnchangedFieldsAndPassword')
            }}</span>
            <ElButton
              data-testid="restart-administrator-create"
              size="small"
              @click="retryOrRestart"
            >
              {{ tOperations('startNewIntent') }}
            </ElButton>
          </div>
        </template>
      </ElAlert>

      <ElForm label-position="top">
        <div class="form-grid">
          <ElFormItem
            :error="fieldErrors.userName"
            :label="tOperations('userName')"
          >
            <ElInput
              v-model="form.userName"
              :disabled="formDisabled || !canManage"
              data-testid="administrator-user-name"
              maxlength="64"
              autocomplete="username"
            />
          </ElFormItem>
          <ElFormItem
            :error="fieldErrors.displayName"
            :label="tOperations('displayName')"
          >
            <ElInput
              v-model="form.displayName"
              :disabled="formDisabled || !canManage"
              data-testid="administrator-create-display-name"
              maxlength="128"
            />
          </ElFormItem>
        </div>

        <ElFormItem
          :error="fieldErrors.initialPassword"
          :label="tOperations('initialPassword')"
        >
          <ElInput
            v-model="form.initialPassword"
            :disabled="formDisabled || !canManage"
            data-testid="administrator-initial-password"
            maxlength="256"
            autocomplete="new-password"
            show-password
            type="password"
          />
        </ElFormItem>

        <ElFormItem
          v-if="canReadRoles"
          :error="fieldErrors.roleIds"
          :label="tOperations('activeRoles')"
        >
          <ElSelect
            v-model="form.roleIds"
            :disabled="formDisabled || !canManage"
            :loading="loadingRoles"
            filterable
            multiple
            remote
            reserve-keyword
            data-testid="administrator-create-roles"
            :placeholder="tOperations('searchActiveRoles')"
            :remote-method="loadRoleOptions"
          >
            <ElOption
              v-for="role in roleOptions"
              :key="role.id"
              :label="role.name ?? role.id ?? ''"
              :value="role.id ?? ''"
            />
          </ElSelect>
        </ElFormItem>
        <ElAlert
          v-else
          :closable="false"
          :title="
            tOperations('roleSelectionIsUnavailableWithoutRoleReadAccess')
          "
          type="info"
        />
      </ElForm>
    </div>

    <template #footer>
      <div class="drawer-footer">
        <ElButton :disabled="saving" @click="close()">
          {{ tOperations('cancel') }}
        </ElButton>
        <ElButton
          :disabled="!canManage"
          :loading="saving"
          data-testid="submit-administrator-create"
          type="primary"
          @click="submit"
        >
          {{ tOperations('createAdministrator') }}
        </ElButton>
      </div>
    </template>
  </ElDrawer>
</template>

<style scoped>
.administrator-create-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.retry-actions {
  display: flex;
  gap: 8px;
  align-items: center;
  justify-content: space-between;
}

.drawer-footer {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}

@media (max-width: 640px) {
  .form-grid,
  .drawer-footer {
    display: grid;
    grid-template-columns: 1fr;
  }

  .drawer-footer :deep(.el-button) {
    min-height: 44px;
    margin-left: 0;
  }

  .retry-actions {
    flex-direction: column;
    align-items: flex-start;
  }
}
</style>
