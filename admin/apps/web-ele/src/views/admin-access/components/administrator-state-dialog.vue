<script lang="ts" setup>
import type { AdministratorDto } from '#/generated/admin-access/types.gen';

import { computed, onBeforeUnmount, ref, watch } from 'vue';

import { RotateCw } from '@vben/icons';

import { ElAlert, ElButton, ElDialog } from 'element-plus';

import {
  getAdministrator,
  isNovaGameRequestCancelled,
  setAdministratorActiveState,
  toNovaGameApiError,
} from '#/api';

interface Props {
  administratorId: null | string;
  canManage: boolean;
  modelValue: boolean;
  targetActive: boolean;
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
const loading = ref(false);
const pending = ref(false);
let loadSequence = 0;

const actionLabel = computed(() =>
  props.targetActive ? 'Activate administrator' : 'Deactivate administrator',
);
const confirmationLabel = computed(() =>
  props.targetActive ? 'Activate account' : 'Deactivate account',
);
const confirmationText = computed(() =>
  props.targetActive
    ? 'This administrator will be able to sign in again.'
    : 'This revokes the administrator access state and active refresh sessions.',
);

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

function clearState() {
  detail.value = null;
  conflictMessage.value = '';
  errorMessage.value = '';
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

async function submit() {
  const currentDetail = detail.value;
  if (!currentDetail || !props.canManage || pending.value) {
    return;
  }

  pending.value = true;
  conflictMessage.value = '';
  errorMessage.value = '';
  try {
    const response = await setAdministratorActiveState(
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
  () => [props.modelValue, props.administratorId, props.targetActive] as const,
  ([open], previous) => {
    if (!open) {
      loadSequence += 1;
      clearState();
      return;
    }
    if (
      !previous ||
      !previous[0] ||
      previous[1] !== props.administratorId ||
      previous[2] !== props.targetActive
    ) {
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
      class="administrator-state-content"
      data-testid="administrator-state-dialog"
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
            data-testid="reload-conflicted-administrator-state"
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

      <template v-if="detail">
        <p>
          {{ confirmationText }}
        </p>
        <p class="administrator-name">
          {{ detail.data.displayName || detail.data.userName }}
          <span>{{ detail.data.userName }}</span>
        </p>
      </template>
    </div>

    <template #footer>
      <div class="dialog-footer">
        <ElButton :disabled="pending" @click="close()">Cancel</ElButton>
        <ElButton
          :disabled="!detail || !canManage"
          :loading="pending"
          data-testid="confirm-administrator-state"
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
.administrator-state-content {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-height: 150px;
}

.administrator-state-content p {
  margin: 0;
}

.administrator-name {
  display: flex;
  flex-direction: column;
  gap: 3px;
  font-weight: 600;
}

.administrator-name span {
  font-size: 12px;
  font-weight: 400;
  color: var(--el-text-color-secondary);
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
