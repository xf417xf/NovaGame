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
  ElOption,
  ElSelect,
  ElTag,
} from 'element-plus';

import {
  getAdministrator,
  isNovaGameRequestCancelled,
  listAdminRoles,
  replaceAdministratorRoles,
  toNovaGameApiError,
} from '#/api';

import {
  fieldErrorsFrom,
  validateAdministratorRoleIds,
} from './administrator-form-utils';

interface Props {
  administratorId: null | string;
  canManage: boolean;
  canReadRoles: boolean;
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
const loadingDetail = ref(false);
const loadingRoles = ref(false);
const roleOptions = ref<AdminRoleSummaryDto[]>([]);
const saving = ref(false);
const form = reactive({ roleIds: [] as string[] });
let detailLoadSequence = 0;
let roleLoadSequence = 0;
let hasLocalSelection: boolean = false;

const inactiveAssignedRoles = computed(() =>
  (detail.value?.data.roles ?? []).filter((role) => role.isActive === false),
);

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
  conflictMessage.value = '';
  errorMessage.value = '';
  fieldErrors.value = {};
  form.roleIds = [];
  roleOptions.value = [];
  hasLocalSelection = false;
}

async function loadRoleOptions(search = '') {
  const sequence = ++roleLoadSequence;
  if (!props.modelValue || !props.canReadRoles) {
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
    errorMessage.value = apiError.message;
  } finally {
    if (sequence === roleLoadSequence) {
      loadingRoles.value = false;
    }
  }
}

async function loadDetail(preserveSelection = false) {
  const sequence = ++detailLoadSequence;
  const administratorId = props.administratorId;
  detail.value = null;
  conflictMessage.value = '';
  errorMessage.value = '';
  fieldErrors.value = {};

  if (!props.modelValue || !administratorId || !props.canReadRoles) {
    loadingDetail.value = false;
    return;
  }

  loadingDetail.value = true;
  try {
    const response = await getAdministrator(administratorId);
    if (
      sequence !== detailLoadSequence ||
      !props.canReadRoles ||
      !props.modelValue ||
      props.administratorId !== administratorId
    ) {
      return;
    }

    detail.value = response;
    if (!preserveSelection || !hasLocalSelection) {
      form.roleIds = (response.data.roles ?? [])
        .filter((role) => role.isActive !== false)
        .map((role) => role.id)
        .filter(
          (roleId): roleId is string =>
            typeof roleId === 'string' && roleId.length > 0,
        );
      hasLocalSelection = false;
    }
  } catch (error) {
    if (isNovaGameRequestCancelled(error) || sequence !== detailLoadSequence) {
      return;
    }
    const apiError = toNovaGameApiError(error);
    if (apiError.status === 404) {
      emit('stale');
      close();
      return;
    }
    if (apiError.status === 403) {
      closeForForbidden();
      return;
    }
    errorMessage.value = apiError.message;
  } finally {
    if (sequence === detailLoadSequence) {
      loadingDetail.value = false;
    }
  }
}

function validateForm(): boolean {
  const error = validateAdministratorRoleIds(form.roleIds);
  fieldErrors.value = error ? { roleIds: error } : {};
  return !error;
}

function markLocalSelection() {
  hasLocalSelection = true;
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
    const response = await replaceAdministratorRoles(
      currentDetail.data.id ?? '',
      { roleIds: [...form.roleIds] },
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
      detailLoadSequence += 1;
      roleLoadSequence += 1;
      clearState();
      return;
    }
    const wasOpen = previous?.[0] ?? false;
    if (!wasOpen || previous?.[1] !== props.administratorId) {
      void loadDetail();
      void loadRoleOptions();
    }
  },
  { immediate: true },
);

watch(
  () => props.canReadRoles,
  (canReadRoles) => {
    if (!canReadRoles) {
      detailLoadSequence += 1;
      roleLoadSequence += 1;
      loadingDetail.value = false;
      loadingRoles.value = false;
      clearState();
      return;
    }
    if (props.modelValue) {
      void loadDetail();
      void loadRoleOptions();
    }
  },
);

onBeforeUnmount(() => {
  detailLoadSequence += 1;
  roleLoadSequence += 1;
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
    size="min(620px, 100vw)"
    title="Edit administrator roles"
    @close="close"
  >
    <div
      v-loading="loadingDetail"
      class="administrator-roles-content"
      data-testid="administrator-roles-drawer"
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
            data-testid="reload-conflicted-administrator-roles"
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

      <ElAlert
        v-if="inactiveAssignedRoles.length > 0"
        :closable="false"
        show-icon
        title="Inactive assigned roles require review"
        type="warning"
      >
        <template #default>
          <span>
            Inactive roles cannot be selected again. Saving reviewed memberships
            removes them.
          </span>
          <div class="inactive-role-list">
            <ElTag
              v-for="role in inactiveAssignedRoles"
              :key="role.id"
              class="long-value-tag"
              type="warning"
            >
              {{ role.name ?? role.id }} (inactive)
            </ElTag>
          </div>
        </template>
      </ElAlert>

      <ElForm v-if="detail && canReadRoles" label-position="top">
        <ElFormItem label="User name">
          <span class="read-only-value">{{ detail.data.userName }}</span>
        </ElFormItem>
        <ElFormItem :error="fieldErrors.roleIds" label="Active roles">
          <ElSelect
            v-model="form.roleIds"
            :disabled="saving || !canManage"
            :loading="loadingRoles"
            filterable
            multiple
            remote
            reserve-keyword
            data-testid="administrator-role-ids"
            placeholder="Search active roles"
            :remote-method="loadRoleOptions"
            @update:model-value="markLocalSelection"
          >
            <ElOption
              v-for="role in roleOptions"
              :key="role.id"
              :label="role.name ?? role.id ?? ''"
              :value="role.id ?? ''"
            />
          </ElSelect>
        </ElFormItem>
      </ElForm>
      <ElAlert
        v-else-if="modelValue"
        :closable="false"
        title="Role selection is unavailable without role read access."
        type="info"
      />
    </div>

    <template #footer>
      <div class="drawer-footer">
        <ElButton :disabled="saving" @click="close()">Cancel</ElButton>
        <ElButton
          :disabled="!detail || !canManage || !canReadRoles"
          :loading="saving"
          data-testid="save-administrator-roles"
          type="primary"
          @click="submit"
        >
          Save roles
        </ElButton>
      </div>
    </template>
  </ElDrawer>
</template>

<style scoped>
.administrator-roles-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-height: 280px;
}

.inactive-role-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 8px;
}

.long-value-tag,
.read-only-value {
  min-width: 0;
  overflow-wrap: anywhere;
}

.long-value-tag {
  max-width: 100%;
  height: auto;
  line-height: 1.3;
  white-space: normal;
}

.read-only-value {
  color: var(--el-text-color-regular);
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
