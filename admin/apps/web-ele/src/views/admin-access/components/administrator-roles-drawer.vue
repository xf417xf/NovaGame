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
import { AdminAccessErrorCode } from '#/features/admin-access/permissions';
import {
  operationsErrorMessage,
  tOperations,
  useOperationsMessage,
} from '#/locales/operations';

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

interface StaleAdministratorRolesDraft {
  kind: 'roles';
  roleIds: string[];
}

const props = defineProps<Props>();
const emit = defineEmits<{
  forbidden: [];
  stale: [draft: null | StaleAdministratorRolesDraft];
  'update:modelValue': [value: boolean];
  updated: [administrator: AdministratorDto];
}>();

const detail = ref<Awaited<ReturnType<typeof getAdministrator>> | null>(null);
const conflictMessage = useOperationsMessage();
const errorMessage = useOperationsMessage();
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

function closeForStaleDetail() {
  const draft = hasLocalSelection
    ? { kind: 'roles' as const, roleIds: [...form.roleIds] }
    : null;
  emit('stale', draft);
  close(true);
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
    errorMessage.value = () => operationsErrorMessage(apiError);
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
    if (
      isNovaGameRequestCancelled(error) ||
      sequence !== detailLoadSequence ||
      !props.modelValue ||
      !props.canReadRoles ||
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
    errorMessage.value = () => operationsErrorMessage(apiError);
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
    if (
      apiError.status === 404 &&
      apiError.code === AdminAccessErrorCode.AdministratorNotFound
    ) {
      closeForStaleDetail();
      return;
    }
    if (
      apiError.status === 404 &&
      apiError.code === AdminAccessErrorCode.RoleNotFound
    ) {
      errorMessage.value = () => operationsErrorMessage(apiError);
      void loadRoleOptions();
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
    :title="tOperations('editAdministratorRoles')"
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

      <ElAlert
        v-if="inactiveAssignedRoles.length > 0"
        :closable="false"
        show-icon
        :title="tOperations('inactiveAssignedRolesRequireReview')"
        type="warning"
      >
        <template #default>
          <span>
            {{
              tOperations(
                'inactiveRolesCannotBeSelectedAgainSavingReviewedMembershipsRemovesThem',
              )
            }}
          </span>
          <div class="inactive-role-list">
            <ElTag
              v-for="role in inactiveAssignedRoles"
              :key="role.id"
              class="long-value-tag"
              type="warning"
            >
              {{ role.name ?? role.id }} {{ tOperations('inactiveSuffix') }}
            </ElTag>
          </div>
        </template>
      </ElAlert>

      <ElForm v-if="detail && canReadRoles" label-position="top">
        <ElFormItem :label="tOperations('userName')">
          <span class="read-only-value">{{ detail.data.userName }}</span>
        </ElFormItem>
        <ElFormItem
          :error="fieldErrors.roleIds"
          :label="tOperations('activeRoles')"
        >
          <ElSelect
            v-model="form.roleIds"
            :disabled="saving || !canManage"
            :loading="loadingRoles"
            filterable
            multiple
            remote
            reserve-keyword
            data-testid="administrator-role-ids"
            :placeholder="tOperations('searchActiveRoles')"
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
        :title="tOperations('roleSelectionIsUnavailableWithoutRoleReadAccess')"
        type="info"
      />
    </div>

    <template #footer>
      <div class="drawer-footer">
        <ElButton :disabled="saving" @click="close()">
          {{ tOperations('cancel') }}
        </ElButton>
        <ElButton
          :disabled="!detail || !canManage || !canReadRoles"
          :loading="saving"
          data-testid="save-administrator-roles"
          type="primary"
          @click="submit"
        >
          {{ tOperations('saveRoles') }}
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
