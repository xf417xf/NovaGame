<script lang="ts" setup>
import type { PermissionGroupDto } from '#/generated/admin-access/types.gen';

import { computed, onBeforeUnmount, ref, watch } from 'vue';

import {
  ElAlert,
  ElButton,
  ElCheckbox,
  ElCheckboxGroup,
  ElInput,
} from 'element-plus';

import {
  getPermissionCatalog,
  isNovaGameRequestCancelled,
  toNovaGameApiError,
} from '#/api';
import {
  operationsErrorMessage,
  operationsPermissionText,
  tOperations,
  useOperationsMessage,
} from '#/locales/operations';

interface Props {
  canReadRoles: boolean;
  disabled: boolean;
  modelValue: string[];
}

const props = defineProps<Props>();
const emit = defineEmits<{
  catalogAvailabilityChange: [available: boolean];
  forbidden: [];
  'update:modelValue': [permissionNames: string[]];
}>();

const catalogAvailable = ref(false);
const catalog = ref<PermissionGroupDto[]>([]);
const errorMessage = useOperationsMessage();
const loading = ref(false);
const search = ref('');
let catalogController: AbortController | null = null;
let catalogSequence = 0;

const selectedPermissionNames = computed({
  get: () => props.modelValue,
  set: (permissionNames: string[]) => {
    emit('update:modelValue', [...new Set(permissionNames)]);
  },
});

const availablePermissionNames = computed(
  () =>
    new Set(
      catalog.value.flatMap((group) =>
        (group.permissions ?? [])
          .map((permission) => permission.name)
          .filter(
            (permissionName): permissionName is string =>
              typeof permissionName === 'string' && permissionName.length > 0,
          ),
      ),
    ),
);

const filteredGroups = computed(() => {
  const needle = search.value.trim().toLocaleLowerCase();
  return catalog.value
    .map((group) => ({
      displayName: operationsPermissionText(
        group.name,
        'title',
        group.displayName ?? group.name ?? tOperations('unnamedGroup'),
      ),
      key: group.name ?? group.displayName ?? 'unnamed-group',
      permissions: (group.permissions ?? [])
        .map((permission) => ({
          ...permission,
          displayName: operationsPermissionText(
            permission.name,
            'title',
            permission.displayName ?? permission.name ?? '',
          ),
          description: operationsPermissionText(
            permission.name,
            'description',
            permission.description ?? '',
          ),
        }))
        .filter((permission) => {
          if (!needle) {
            return true;
          }
          return [
            permission.displayName,
            permission.description,
            permission.name,
          ]
            .filter((value): value is string => typeof value === 'string')
            .some((value) => value.toLocaleLowerCase().includes(needle));
        }),
    }))
    .filter((group) => group.permissions.length > 0);
});

function removeUnavailableSelection() {
  const retainedPermissionNames = props.modelValue.filter((permissionName) =>
    availablePermissionNames.value.has(permissionName),
  );
  if (retainedPermissionNames.length !== props.modelValue.length) {
    emit('update:modelValue', retainedPermissionNames);
  }
}

function setCatalogAvailable(available: boolean) {
  if (catalogAvailable.value === available) {
    return;
  }
  catalogAvailable.value = available;
  emit('catalogAvailabilityChange', available);
}

function clearCatalog() {
  catalogController?.abort();
  catalogController = null;
  catalog.value = [];
  loading.value = false;
  setCatalogAvailable(false);
}

async function loadCatalog() {
  const sequence = ++catalogSequence;
  if (!props.canReadRoles) {
    clearCatalog();
    return;
  }

  catalogController?.abort();
  const controller = new AbortController();
  catalogController = controller;
  errorMessage.value = '';
  loading.value = true;
  setCatalogAvailable(false);
  try {
    const response = await getPermissionCatalog({ signal: controller.signal });
    if (sequence !== catalogSequence || !props.canReadRoles) {
      return;
    }
    catalog.value = response.data ?? [];
    removeUnavailableSelection();
    setCatalogAvailable(true);
  } catch (error) {
    if (isNovaGameRequestCancelled(error) || sequence !== catalogSequence) {
      return;
    }
    const apiError = toNovaGameApiError(error);
    if (apiError.status === 403) {
      clearCatalog();
      emit('forbidden');
      return;
    }
    setCatalogAvailable(false);
    errorMessage.value = () => operationsErrorMessage(apiError);
  } finally {
    if (sequence === catalogSequence) {
      loading.value = false;
    }
  }
}

function reloadCatalog() {
  return loadCatalog();
}

watch(
  () => props.canReadRoles,
  (canReadRoles) => {
    if (!canReadRoles) {
      catalogSequence += 1;
      clearCatalog();
      emit('update:modelValue', []);
      return;
    }
    void loadCatalog();
  },
  { immediate: true },
);

onBeforeUnmount(() => {
  catalogSequence += 1;
  clearCatalog();
});

defineExpose({ reloadCatalog });
</script>

<template>
  <div class="role-permission-selector" data-testid="role-permission-selector">
    <ElAlert
      v-if="!canReadRoles"
      :closable="false"
      :title="
        tOperations('permissionSelectionIsUnavailableWithoutRoleReadAccess')
      "
      type="info"
    />

    <template v-else>
      <ElInput
        v-model="search"
        :aria-label="tOperations('searchRegisteredPermissions')"
        :disabled="disabled"
        data-testid="role-permission-search"
        :placeholder="tOperations('searchRegisteredPermissions')"
      />

      <ElAlert
        v-if="errorMessage"
        :closable="false"
        :title="errorMessage"
        show-icon
        type="error"
      >
        <template #default>
          <ElButton size="small" @click="loadCatalog">
            {{ tOperations('retry') }}
          </ElButton>
        </template>
      </ElAlert>

      <div v-loading="loading" class="permission-catalog">
        <ElCheckboxGroup
          v-model="selectedPermissionNames"
          :disabled="disabled || loading"
          data-testid="role-permission-names"
        >
          <section
            v-for="group in filteredGroups"
            :key="group.key"
            class="permission-group"
          >
            <h3>{{ group.displayName }}</h3>
            <ElCheckbox
              v-for="permission in group.permissions"
              :key="permission.name ?? permission.displayName ?? group.key"
              :label="permission.name ?? ''"
            >
              <span class="permission-name">
                {{ permission.displayName ?? permission.name }}
              </span>
              <small v-if="permission.description">
                {{ permission.description }}
              </small>
            </ElCheckbox>
          </section>
        </ElCheckboxGroup>
        <p v-if="!loading && filteredGroups.length === 0" class="empty-catalog">
          {{ tOperations('noRegisteredPermissionsMatchTheCurrentSearch') }}
        </p>
      </div>
    </template>
  </div>
</template>

<style scoped>
.role-permission-selector {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.permission-catalog {
  min-height: 140px;
  max-height: min(48vh, 420px);
  padding: 4px;
  overflow: auto;
  border: 1px solid var(--el-border-color-light);
}

.permission-group {
  padding: 10px 12px;
}

.permission-group + .permission-group {
  border-top: 1px solid var(--el-border-color-lighter);
}

.permission-group h3 {
  margin: 0 0 8px;
  font-size: 13px;
  font-weight: 600;
}

.permission-group :deep(.el-checkbox) {
  display: flex;
  width: 100%;
  height: auto;
  min-height: 30px;
  margin: 0;
  white-space: normal;
}

.permission-group :deep(.el-checkbox__label) {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  padding: 4px 8px;
  overflow-wrap: anywhere;
}

.permission-name {
  color: var(--el-text-color-primary);
  overflow-wrap: anywhere;
}

.permission-group small,
.empty-catalog {
  color: var(--el-text-color-secondary);
  overflow-wrap: anywhere;
}

.empty-catalog {
  margin: 16px;
  text-align: center;
}
</style>
