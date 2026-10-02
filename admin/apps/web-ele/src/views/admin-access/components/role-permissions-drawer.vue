<script lang="ts" setup>
import type { AdminRoleDto } from '#/generated/admin-access/types.gen';

import { onBeforeUnmount, reactive, ref, watch } from 'vue';

import { RotateCw } from '@vben/icons';

import {
  ElAlert,
  ElButton,
  ElDrawer,
  ElForm,
  ElFormItem,
  ElTag,
} from 'element-plus';

import {
  getAdminRole,
  isNovaGameRequestCancelled,
  replaceAdminRolePermissions,
  toNovaGameApiError,
} from '#/api';
import { AdminAccessErrorCode } from '#/features/admin-access/permissions';
import {
  operationsErrorMessage,
  tOperations,
  useOperationsMessage,
} from '#/locales/operations';

import { fieldErrorsFrom } from './administrator-form-utils';
import {
  unknownRolePermissionErrorCode,
  validateRolePermissionNames,
} from './role-form-utils';
import RolePermissionSelector from './role-permission-selector.vue';

interface Props {
  canManage: boolean;
  canReadRoles: boolean;
  modelValue: boolean;
  roleId: null | string;
}

interface StaleRolePermissionsDraft {
  kind: 'permissions';
  permissionNames: string[];
}

interface RolePermissionSelectorHandle {
  reloadCatalog: () => Promise<void>;
}

const props = defineProps<Props>();
const emit = defineEmits<{
  forbidden: [];
  stale: [draft: null | StaleRolePermissionsDraft];
  'update:modelValue': [value: boolean];
  updated: [role: AdminRoleDto];
}>();

const conflictMessage = useOperationsMessage();
const detail = ref<Awaited<ReturnType<typeof getAdminRole>> | null>(null);
const errorMessage = useOperationsMessage();
const fieldErrors = ref<Record<string, string>>({});
const loading = ref(false);
const catalogAvailable = ref(false);
const permissionSelector = ref<null | RolePermissionSelectorHandle>(null);
const reloadingPermissionCatalog = ref(false);
const saving = ref(false);
const form = reactive({ permissionNames: [] as string[] });
let hasLocalSelection = false;
let loadController: AbortController | null = null;
let loadSequence = 0;

function clearState() {
  conflictMessage.value = '';
  detail.value = null;
  errorMessage.value = '';
  fieldErrors.value = {};
  form.permissionNames = [];
  hasLocalSelection = false;
  catalogAvailable.value = false;
}

function closeForStaleDetail() {
  const draft = hasLocalSelection
    ? {
        kind: 'permissions' as const,
        permissionNames: [...form.permissionNames],
      }
    : null;
  emit('stale', draft);
  close(true);
}

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

async function loadDetail(preserveSelection = false) {
  const sequence = ++loadSequence;
  const roleId = props.roleId;
  loadController?.abort();
  const controller = new AbortController();
  loadController = controller;
  detail.value = null;
  conflictMessage.value = '';
  errorMessage.value = '';
  fieldErrors.value = {};

  if (!props.modelValue || !roleId || !props.canReadRoles) {
    loading.value = false;
    return;
  }

  loading.value = true;
  try {
    const response = await getAdminRole(roleId, { signal: controller.signal });
    if (
      sequence !== loadSequence ||
      !props.modelValue ||
      !props.canReadRoles ||
      props.roleId !== roleId
    ) {
      return;
    }
    detail.value = response;
    if (!preserveSelection || !hasLocalSelection) {
      form.permissionNames = [...(response.data.permissionNames ?? [])];
      hasLocalSelection = false;
    }
  } catch (error) {
    if (
      isNovaGameRequestCancelled(error) ||
      sequence !== loadSequence ||
      !props.modelValue ||
      !props.canReadRoles ||
      props.roleId !== roleId
    ) {
      return;
    }
    const apiError = toNovaGameApiError(error);
    if (
      apiError.status === 404 &&
      apiError.code === AdminAccessErrorCode.RoleNotFound
    ) {
      closeForStaleDetail();
      return;
    }
    if (apiError.status === 403) {
      closeForForbidden();
      return;
    }
    errorMessage.value = () => operationsErrorMessage(apiError);
  } finally {
    if (sequence === loadSequence) {
      loading.value = false;
    }
  }
}

function updatePermissionNames(permissionNames: string[]) {
  if (saving.value && !reloadingPermissionCatalog.value) {
    return;
  }
  hasLocalSelection = true;
  form.permissionNames = permissionNames;
}

function updateCatalogAvailability(available: boolean) {
  catalogAvailable.value = available;
}

async function reloadPermissionCatalog() {
  catalogAvailable.value = false;
  reloadingPermissionCatalog.value = true;
  try {
    await permissionSelector.value?.reloadCatalog();
  } finally {
    reloadingPermissionCatalog.value = false;
  }
}

function validateForm(): boolean {
  const permissionNamesError = validateRolePermissionNames(
    form.permissionNames,
  );
  fieldErrors.value = permissionNamesError
    ? { permissionNames: permissionNamesError }
    : {};
  return !permissionNamesError;
}

async function submit() {
  const currentDetail = detail.value;
  if (
    !props.canManage ||
    currentDetail?.data.isBuiltIn ||
    saving.value ||
    !currentDetail ||
    !catalogAvailable.value ||
    !validateForm()
  ) {
    return;
  }

  saving.value = true;
  conflictMessage.value = '';
  errorMessage.value = '';
  try {
    const response = await replaceAdminRolePermissions(
      currentDetail.data.id ?? '',
      { permissionNames: [...form.permissionNames] },
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
      apiError.code === AdminAccessErrorCode.RoleNotFound
    ) {
      closeForStaleDetail();
      return;
    }
    if (apiError.status === 403) {
      closeForForbidden();
      return;
    }
    fieldErrors.value = fieldErrorsFrom(apiError);
    if (apiError.code === unknownRolePermissionErrorCode) {
      errorMessage.value = () =>
        tOperations(
          'theRegisteredPermissionCatalogChangedReviewTheRefreshedSelectionBeforeRetrying',
        );
      await reloadPermissionCatalog();
      return;
    }
    if (apiError.status === 409) {
      conflictMessage.value = () => operationsErrorMessage(apiError);
      return;
    }
    errorMessage.value = () => operationsErrorMessage(apiError);
  } finally {
    saving.value = false;
  }
}

watch(
  () => [props.modelValue, props.roleId] as const,
  ([open], previous) => {
    if (!open) {
      loadSequence += 1;
      loadController?.abort();
      loadController = null;
      clearState();
      return;
    }
    const wasOpen = previous?.[0] ?? false;
    if (!wasOpen || previous?.[1] !== props.roleId) {
      void loadDetail();
    }
  },
  { immediate: true },
);

watch(
  () => props.canReadRoles,
  (canReadRoles) => {
    if (canReadRoles) {
      if (props.modelValue) {
        void loadDetail();
      }
      return;
    }
    loadSequence += 1;
    loadController?.abort();
    loadController = null;
    clearState();
  },
);

onBeforeUnmount(() => {
  loadSequence += 1;
  loadController?.abort();
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
    size="min(680px, 100vw)"
    :title="tOperations('editRolePermissions')"
    @close="close()"
  >
    <div
      v-loading="loading"
      class="role-permissions-content"
      data-testid="role-permissions-drawer"
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
            data-testid="reload-conflicted-role-permissions"
            size="small"
            @click="loadDetail(true)"
          >
            <RotateCw :size="15" />
            {{ tOperations('reloadAndReview') }}
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
          <ElButton size="small" @click="loadDetail(true)">
            {{ tOperations('retry') }}
          </ElButton>
        </template>
      </ElAlert>

      <template v-if="detail?.data.isBuiltIn">
        <ElAlert
          :closable="false"
          :title="tOperations('builtInRolePermissionsAreReadOnly')"
          type="info"
        />
        <div class="permission-tag-list">
          <ElTag
            v-for="permissionName in detail.data.permissionNames ?? []"
            :key="permissionName"
            class="long-value-tag"
            size="small"
            type="info"
          >
            {{ permissionName }}
          </ElTag>
        </div>
      </template>

      <ElForm v-else-if="detail && canReadRoles" label-position="top">
        <ElFormItem
          :error="fieldErrors.permissionNames"
          :label="tOperations('registeredPermissions')"
        >
          <RolePermissionSelector
            ref="permissionSelector"
            :can-read-roles="canReadRoles"
            :disabled="saving || !canManage"
            :model-value="form.permissionNames"
            @catalog-availability-change="updateCatalogAvailability"
            @forbidden="closeForForbidden"
            @update:model-value="updatePermissionNames"
          />
        </ElFormItem>
      </ElForm>

      <ElAlert
        v-else-if="modelValue"
        :closable="false"
        :title="
          tOperations('permissionSelectionIsUnavailableWithoutRoleReadAccess')
        "
        type="info"
      />
    </div>

    <template #footer>
      <div class="drawer-footer">
        <ElButton :disabled="saving" @click="close()">
          {{ tOperations('cancel') }}
        </ElButton>
        <ElButton
          v-if="!detail?.data.isBuiltIn"
          :disabled="
            !detail || !canManage || !canReadRoles || !catalogAvailable
          "
          :loading="saving"
          data-testid="save-role-permissions"
          type="primary"
          @click="submit"
        >
          {{ tOperations('savePermissions') }}
        </ElButton>
      </div>
    </template>
  </ElDrawer>
</template>

<style scoped>
.role-permissions-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
  min-height: 280px;
}

.permission-tag-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  min-width: 0;
}

.long-value-tag {
  min-width: 0;
  max-width: 100%;
  height: auto;
  line-height: 1.3;
  overflow-wrap: anywhere;
  white-space: normal;
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
