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
  ElInput,
  ElTag,
} from 'element-plus';

import {
  getAdminRole,
  isNovaGameRequestCancelled,
  renameAdminRole,
  toNovaGameApiError,
} from '#/api';
import { AdminAccessErrorCode } from '#/features/admin-access/permissions';
import {
  operationsErrorMessage,
  tOperations,
  useOperationsMessage,
} from '#/locales/operations';

import { fieldErrorsFrom } from './administrator-form-utils';
import { normalizeRoleName, validateRoleName } from './role-form-utils';

interface Props {
  canManage: boolean;
  modelValue: boolean;
  roleId: null | string;
}

interface StaleRoleNameDraft {
  kind: 'rename';
  name: string;
}

const props = defineProps<Props>();
const emit = defineEmits<{
  forbidden: [];
  stale: [draft: null | StaleRoleNameDraft];
  'update:modelValue': [value: boolean];
  updated: [role: AdminRoleDto];
}>();

const conflictMessage = useOperationsMessage();
const detail = ref<Awaited<ReturnType<typeof getAdminRole>> | null>(null);
const errorMessage = useOperationsMessage();
const fieldErrors = ref<Record<string, string>>({});
const loading = ref(false);
const saving = ref(false);
const form = reactive({ name: '' });
let hasLocalInput = false;
let loadController: AbortController | null = null;
let loadSequence = 0;

function clearState() {
  conflictMessage.value = '';
  detail.value = null;
  errorMessage.value = '';
  fieldErrors.value = {};
  form.name = '';
  hasLocalInput = false;
}

function closeForStaleDetail() {
  const draft = hasLocalInput
    ? { kind: 'rename' as const, name: form.name }
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

async function loadDetail(preserveInput = false) {
  const sequence = ++loadSequence;
  const roleId = props.roleId;
  loadController?.abort();
  const controller = new AbortController();
  loadController = controller;
  detail.value = null;
  conflictMessage.value = '';
  errorMessage.value = '';
  fieldErrors.value = {};

  if (!props.modelValue || !roleId) {
    loading.value = false;
    return;
  }

  loading.value = true;
  try {
    const response = await getAdminRole(roleId, { signal: controller.signal });
    if (
      sequence !== loadSequence ||
      !props.modelValue ||
      props.roleId !== roleId
    ) {
      return;
    }
    detail.value = response;
    if (!preserveInput || !hasLocalInput) {
      form.name = response.data.name ?? '';
      hasLocalInput = false;
    }
  } catch (error) {
    if (
      isNovaGameRequestCancelled(error) ||
      sequence !== loadSequence ||
      !props.modelValue ||
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

function validateForm(): boolean {
  const nameError = validateRoleName(form.name);
  fieldErrors.value = nameError ? { name: nameError } : {};
  return !nameError;
}

function markLocalInput() {
  hasLocalInput = true;
}

async function submit() {
  const currentDetail = detail.value;
  if (
    !props.canManage ||
    currentDetail?.data.isBuiltIn ||
    saving.value ||
    !currentDetail ||
    !validateForm()
  ) {
    return;
  }

  saving.value = true;
  conflictMessage.value = '';
  errorMessage.value = '';
  try {
    const response = await renameAdminRole(
      currentDetail.data.id ?? '',
      { name: normalizeRoleName(form.name) ?? form.name },
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
    size="min(520px, 100vw)"
    :title="tOperations('renameRole')"
    @close="close()"
  >
    <div
      v-loading="loading"
      class="role-rename-content"
      data-testid="role-rename-drawer"
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
            data-testid="reload-conflicted-role-name"
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
          :title="tOperations('builtInRolesAreReadOnly')"
          type="info"
        />
        <ElTag class="long-value-tag" type="info">
          {{ detail.data.name }}
        </ElTag>
      </template>

      <ElForm v-else-if="detail" label-position="top">
        <ElFormItem :error="fieldErrors.name" :label="tOperations('roleName')">
          <ElInput
            v-model="form.name"
            :disabled="saving || !canManage"
            data-testid="role-name"
            maxlength="64"
            @update:model-value="markLocalInput"
          />
        </ElFormItem>
      </ElForm>
    </div>

    <template #footer>
      <div class="drawer-footer">
        <ElButton :disabled="saving" @click="close()">
          {{ tOperations('cancel') }}
        </ElButton>
        <ElButton
          v-if="!detail?.data.isBuiltIn"
          :disabled="!detail || !canManage"
          :loading="saving"
          data-testid="save-role-name"
          type="primary"
          @click="submit"
        >
          {{ tOperations('saveRoleName') }}
        </ElButton>
      </div>
    </template>
  </ElDrawer>
</template>

<style scoped>
.role-rename-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
  min-height: 220px;
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
