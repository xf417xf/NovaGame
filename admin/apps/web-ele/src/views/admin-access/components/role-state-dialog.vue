<script lang="ts" setup>
import type { AdminRoleDto } from '#/generated/admin-access/types.gen';

import { computed, onBeforeUnmount, ref, watch } from 'vue';

import { RotateCw } from '@vben/icons';

import { ElAlert, ElButton, ElDialog } from 'element-plus';

import {
  getAdminRole,
  isNovaGameRequestCancelled,
  setAdminRoleActiveState,
  toNovaGameApiError,
} from '#/api';

interface Props {
  canManage: boolean;
  modelValue: boolean;
  roleId: null | string;
  targetActive: boolean;
}

const props = defineProps<Props>();
const emit = defineEmits<{
  forbidden: [];
  stale: [];
  'update:modelValue': [value: boolean];
  updated: [role: AdminRoleDto];
}>();

const conflictMessage = ref('');
const detail = ref<Awaited<ReturnType<typeof getAdminRole>> | null>(null);
const errorMessage = ref('');
const loading = ref(false);
const pending = ref(false);
let loadController: AbortController | null = null;
let loadSequence = 0;

const actionLabel = computed(() =>
  props.targetActive ? 'Activate role' : 'Deactivate role',
);
const confirmationLabel = computed(() =>
  props.targetActive ? 'Activate role' : 'Deactivate role',
);
const confirmationText = computed(() =>
  props.targetActive
    ? 'This custom role can become effective for assigned administrators.'
    : 'This custom role no longer contributes permissions to assigned administrators.',
);

function clearState() {
  conflictMessage.value = '';
  detail.value = null;
  errorMessage.value = '';
}

function close(force = false) {
  if (force || !pending.value) {
    emit('update:modelValue', false);
  }
}

function closeForForbidden() {
  clearState();
  emit('update:modelValue', false);
  emit('forbidden');
}

async function loadDetail() {
  const sequence = ++loadSequence;
  const roleId = props.roleId;
  loadController?.abort();
  const controller = new AbortController();
  loadController = controller;
  clearState();
  if (!props.modelValue || !roleId) {
    loading.value = false;
    return;
  }

  loading.value = true;
  try {
    const response = await getAdminRole(roleId, { signal: controller.signal });
    if (
      sequence === loadSequence &&
      props.modelValue &&
      props.roleId === roleId
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

async function submit() {
  const currentDetail = detail.value;
  if (
    !currentDetail ||
    !props.canManage ||
    currentDetail.data.isBuiltIn ||
    pending.value
  ) {
    return;
  }

  pending.value = true;
  conflictMessage.value = '';
  errorMessage.value = '';
  try {
    const response = await setAdminRoleActiveState(
      currentDetail.data.id ?? '',
      { isActive: props.targetActive },
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
  () => [props.modelValue, props.roleId, props.targetActive] as const,
  ([open], previous) => {
    if (!open) {
      loadSequence += 1;
      loadController?.abort();
      loadController = null;
      clearState();
      return;
    }
    if (
      !previous ||
      !previous[0] ||
      previous[1] !== props.roleId ||
      previous[2] !== props.targetActive
    ) {
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
  <ElDialog
    :close-on-click-modal="!pending"
    :close-on-press-escape="!pending"
    :model-value="modelValue"
    :show-close="!pending"
    destroy-on-close
    :title="actionLabel"
    width="min(520px, 94vw)"
    @close="close()"
  >
    <div
      v-loading="loading"
      class="role-state-content"
      data-testid="role-state-dialog"
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
            data-testid="reload-conflicted-role-state"
            size="small"
            @click="loadDetail"
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
      />

      <template v-if="detail?.data.isBuiltIn">
        <ElAlert
          :closable="false"
          title="Built-in roles are read-only."
          type="info"
        />
      </template>
      <template v-else-if="detail">
        <p>{{ confirmationText }}</p>
        <p class="role-name">{{ detail.data.name }}</p>
      </template>
    </div>

    <template #footer>
      <div class="dialog-footer">
        <ElButton :disabled="pending" @click="close()">Cancel</ElButton>
        <ElButton
          v-if="!detail?.data.isBuiltIn"
          :disabled="!detail || !canManage"
          :loading="pending"
          data-testid="confirm-role-state"
          :type="targetActive ? 'primary' : 'danger'"
          @click="submit"
        >
          {{ confirmationLabel }}
        </ElButton>
      </div>
    </template>
  </ElDialog>
</template>

<style scoped>
.role-state-content {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
  min-height: 150px;
}

.role-state-content p {
  margin: 0;
}

.role-name {
  min-width: 0;
  font-weight: 600;
  overflow-wrap: anywhere;
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
}
</style>
