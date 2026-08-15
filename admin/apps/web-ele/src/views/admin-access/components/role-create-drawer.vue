<script lang="ts" setup>
import type { AdminRoleDto } from '#/generated/admin-access/types.gen';

import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue';

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
  createAdminRole,
  isNovaGameRequestCancelled,
  toNovaGameApiError,
} from '#/api';

import {
  fieldErrorsFrom,
  isUncertainCommandError,
} from './administrator-form-utils';
import {
  createRoleIdempotencyKey,
  normalizeRoleName,
  unknownRolePermissionErrorCode,
  validateRoleName,
  validateRolePermissionNames,
} from './role-form-utils';
import RolePermissionSelector from './role-permission-selector.vue';

interface Props {
  canManage: boolean;
  canReadRoles: boolean;
  modelValue: boolean;
}

interface RolePermissionSelectorHandle {
  reloadCatalog: () => Promise<void>;
}

const props = defineProps<Props>();
const emit = defineEmits<{
  conflict: [];
  created: [role: AdminRoleDto];
  forbidden: [];
  'update:modelValue': [value: boolean];
}>();

const conflictMessage = ref('');
const catalogAvailable = ref(false);
const fieldErrors = ref<Record<string, string>>({});
const formError = ref('');
const idempotencyKey = ref<null | string>(null);
const retryLocked = ref(false);
const permissionSelector = ref<null | RolePermissionSelectorHandle>(null);
const saving = ref(false);
const form = reactive({
  name: '',
  permissionNames: [] as string[],
});

const formDisabled = computed(() => saving.value || retryLocked.value);
const submitDisabled = computed(
  () =>
    !props.canManage ||
    saving.value ||
    (props.canReadRoles && !catalogAvailable.value),
);

function clearState() {
  conflictMessage.value = '';
  fieldErrors.value = {};
  form.name = '';
  form.permissionNames = [];
  formError.value = '';
  idempotencyKey.value = null;
  retryLocked.value = false;
  catalogAvailable.value = false;
}

function close(force = false) {
  if (saving.value && !force) {
    return;
  }
  clearState();
  emit('update:modelValue', false);
}

function closeForForbidden() {
  clearState();
  emit('update:modelValue', false);
  emit('forbidden');
}

function startNewIntent() {
  idempotencyKey.value = null;
  retryLocked.value = false;
  formError.value = '';
  conflictMessage.value = '';
}

function lockRetryAfterUncertainOutcome() {
  formError.value =
    'The create request may have completed. Retry only with the unchanged role and permissions.';
  retryLocked.value = true;
}

function reloadConflict() {
  emit('conflict');
}

function updatePermissionNames(permissionNames: string[]) {
  if (retryLocked.value) {
    return;
  }
  form.permissionNames = permissionNames;
}

function updateCatalogAvailability(available: boolean) {
  catalogAvailable.value = available;
}

async function reloadPermissionCatalog() {
  catalogAvailable.value = false;
  await permissionSelector.value?.reloadCatalog();
}

function validateForm(): boolean {
  const errors = {
    name: validateRoleName(form.name),
    permissionNames: validateRolePermissionNames(form.permissionNames),
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
    idempotencyKey.value = createRoleIdempotencyKey();
    return true;
  } catch (error) {
    formError.value =
      error instanceof Error ? error.message : 'Unable to create this command.';
    return false;
  }
}

async function submit() {
  if (
    !props.canManage ||
    saving.value ||
    (props.canReadRoles && !catalogAvailable.value) ||
    !validateForm() ||
    !ensureIdempotencyKey()
  ) {
    return;
  }

  saving.value = true;
  conflictMessage.value = '';
  fieldErrors.value = {};
  formError.value = '';
  try {
    const response = await createAdminRole(
      {
        name: normalizeRoleName(form.name) ?? form.name,
        permissionNames: [...form.permissionNames],
      },
      idempotencyKey.value ?? '',
    );
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
    formError.value = apiError.message;
    if (apiError.code === unknownRolePermissionErrorCode) {
      idempotencyKey.value = null;
      retryLocked.value = false;
      formError.value =
        'The registered permission catalog changed. Review the refreshed selection before retrying.';
      await reloadPermissionCatalog();
      return;
    }
    if (isUncertainCommandError(apiError)) {
      lockRetryAfterUncertainOutcome();
      return;
    }

    idempotencyKey.value = null;
    retryLocked.value = false;
    if (apiError.status === 409) {
      conflictMessage.value = apiError.message;
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
      return;
    }
    if (!wasOpen) {
      clearState();
    }
  },
  { immediate: true },
);

watch(
  () => props.canReadRoles,
  (canReadRoles) => {
    if (!canReadRoles) {
      form.permissionNames = [];
    }
  },
);

onBeforeUnmount(clearState);
</script>

<template>
  <ElDrawer
    :close-on-click-modal="!saving"
    :close-on-press-escape="!saving"
    :model-value="modelValue"
    :show-close="!saving"
    destroy-on-close
    size="min(680px, 100vw)"
    title="Create custom role"
    @close="close()"
  >
    <div class="role-create-content" data-testid="role-create-drawer">
      <ElAlert
        v-if="conflictMessage"
        :closable="false"
        :title="conflictMessage"
        show-icon
        type="warning"
      >
        <template #default>
          <ElButton
            data-testid="reload-conflicted-role-create"
            size="small"
            @click="reloadConflict"
          >
            <RotateCw :size="15" />
            Reload and review
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
            <span>Retry only with the unchanged role and permissions.</span>
            <ElButton
              data-testid="restart-role-create"
              size="small"
              @click="startNewIntent"
            >
              Start new intent
            </ElButton>
          </div>
        </template>
      </ElAlert>

      <ElForm label-position="top">
        <ElFormItem :error="fieldErrors.name" label="Role name">
          <ElInput
            v-model="form.name"
            :disabled="formDisabled || !canManage"
            data-testid="role-create-name"
            maxlength="64"
          />
        </ElFormItem>
        <ElFormItem
          :error="fieldErrors.permissionNames"
          label="Registered permissions"
        >
          <RolePermissionSelector
            ref="permissionSelector"
            :can-read-roles="canReadRoles"
            :disabled="formDisabled || !canManage"
            :model-value="form.permissionNames"
            @catalog-availability-change="updateCatalogAvailability"
            @forbidden="closeForForbidden"
            @update:model-value="updatePermissionNames"
          />
        </ElFormItem>
      </ElForm>
    </div>

    <template #footer>
      <div class="drawer-footer">
        <ElButton :disabled="saving" @click="close()">Cancel</ElButton>
        <ElButton
          :disabled="submitDisabled"
          :loading="saving"
          data-testid="submit-role-create"
          type="primary"
          @click="submit"
        >
          Create role
        </ElButton>
      </div>
    </template>
  </ElDrawer>
</template>

<style scoped>
.role-create-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
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
