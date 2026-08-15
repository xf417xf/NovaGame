<script lang="ts" setup>
import type { RouteRecordRaw } from 'vue-router';

import type {
  AdminRoleDto,
  AdminRoleSummaryDto,
} from '#/generated/admin-access/types.gen';

import {
  computed,
  onBeforeUnmount,
  onMounted,
  reactive,
  ref,
  watch,
} from 'vue';
import { useRouter } from 'vue-router';

import {
  CircleCheckBig,
  CircleX,
  Ellipsis,
  LockKeyhole,
  Plus,
  RotateCw,
  UserRoundPen,
} from '@vben/icons';
import { useAccessStore } from '@vben/stores';

import { useMediaQuery } from '@vueuse/core';
import {
  ElAlert,
  ElButton,
  ElDropdown,
  ElDropdownItem,
  ElDropdownMenu,
  ElEmpty,
  ElInput,
  ElOption,
  ElPagination,
  ElResult,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTooltip,
} from 'element-plus';

import {
  isNovaGameRequestCancelled,
  listAdminRoles,
  toNovaGameApiError,
} from '#/api';
import {
  AdminAccessPermission,
  hasAdminAccessPermission,
} from '#/features/admin-access/permissions';
import { useAuthStore } from '#/store';

import RoleCreateDrawer from './components/role-create-drawer.vue';
import RolePermissionsDrawer from './components/role-permissions-drawer.vue';
import RoleRenameDrawer from './components/role-rename-drawer.vue';
import RoleStateDialog from './components/role-state-dialog.vue';

defineOptions({ name: 'AdministratorAccessRoles' });

type RoleCommand = 'name' | 'permissions' | 'state';

const accessStore = useAccessStore();
const authStore = useAuthStore();
const router = useRouter();
const isMobile = useMediaQuery('(max-width: 640px)');

const createDrawerOpen = ref(false);
const errorMessage = ref('');
const forbidden = ref(false);
const loading = ref(false);
const reconciliationError = ref('');
const reconciliationRequired = ref(false);
const renameDrawerOpen = ref(false);
const reconcilingPermissions = ref(false);
const roles = ref<AdminRoleSummaryDto[]>([]);
const permissionsDrawerOpen = ref(false);
const selectedRoleId = ref<null | string>(null);
const stateDialogOpen = ref(false);
const targetActiveState = ref(false);
const total = ref(0);
const query = reactive({
  isActive: undefined as boolean | undefined,
  isBuiltIn: undefined as boolean | undefined,
  page: 1,
  pageSize: 20,
  search: '',
});
let roleListController: AbortController | null = null;
let roleListSequence = 0;
let permissionRefreshPromise: null | Promise<void> = null;

const canManage = computed(() =>
  hasAdminAccessPermission(
    accessStore.accessCodes,
    AdminAccessPermission.RolesManage,
  ),
);
const canReadRoles = computed(() =>
  hasAdminAccessPermission(
    accessStore.accessCodes,
    AdminAccessPermission.RolesRead,
  ),
);
const canReadRoleWorkspace = computed(
  () => canReadRoles.value && !reconciliationRequired.value,
);
const canManageRoleWorkspace = computed(
  () => canManage.value && canReadRoleWorkspace.value,
);
const paginationLayout = computed(() =>
  isMobile.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next',
);
const paginationPagerCount = computed(() => (isMobile.value ? 5 : 7));
const roleWorkspaceForbidden = computed(
  () => forbidden.value || reconciliationRequired.value,
);

function formatDate(value: null | string | undefined): string {
  return value
    ? new Date(value).toISOString().replace('T', ' ').replace('Z', ' UTC')
    : 'Never';
}

function clearRoleResults() {
  roles.value = [];
  total.value = 0;
}

function closeRoleInteractions() {
  createDrawerOpen.value = false;
  permissionsDrawerOpen.value = false;
  renameDrawerOpen.value = false;
  selectedRoleId.value = null;
  stateDialogOpen.value = false;
}

function firstAccessiblePath(
  candidateRoutes: readonly RouteRecordRaw[],
): string | undefined {
  for (const route of candidateRoutes) {
    const childPath = route.children
      ? firstAccessiblePath(route.children)
      : undefined;
    if (childPath) {
      return childPath;
    }
    if (route.path.startsWith('/')) {
      return route.path;
    }
  }
  return undefined;
}

async function refreshPermissionsAfterAccessChange() {
  if (permissionRefreshPromise) {
    await permissionRefreshPromise;
    return;
  }

  const refresh = (async () => {
    closeRoleInteractions();
    roleListSequence += 1;
    roleListController?.abort();
    roleListController = null;
    loading.value = false;
    errorMessage.value = '';
    reconciliationError.value = '';
    reconciliationRequired.value = true;
    reconcilingPermissions.value = true;
    forbidden.value = true;
    clearRoleResults();

    try {
      const refreshed = await authStore.refreshAuthenticatedState();
      if (!refreshed) {
        return;
      }

      reconciliationError.value = '';
      if (!canReadRoles.value) {
        clearRoleResults();
        const destination = firstAccessiblePath(refreshed.accessibleRoutes);
        if (destination) {
          if (destination !== router.currentRoute.value.path) {
            await router.replace(destination);
          }
        } else {
          await authStore.expireSession();
        }
        return;
      }

      reconciliationRequired.value = false;
      forbidden.value = false;
      await loadRoles();
    } catch (error) {
      if (!isNovaGameRequestCancelled(error)) {
        reconciliationError.value = toNovaGameApiError(error).message;
      }
    } finally {
      reconcilingPermissions.value = false;
    }
  })();

  permissionRefreshPromise = refresh;
  try {
    await refresh;
  } finally {
    if (permissionRefreshPromise === refresh) {
      permissionRefreshPromise = null;
    }
  }
}

function refreshRoles() {
  if (roleWorkspaceForbidden.value) {
    void refreshPermissionsAfterAccessChange();
    return;
  }
  void loadRoles();
}

function recordError(error: unknown) {
  if (isNovaGameRequestCancelled(error)) {
    return;
  }
  const apiError = toNovaGameApiError(error);
  if (apiError.status === 403) {
    reconciliationRequired.value = true;
    forbidden.value = true;
    void refreshPermissionsAfterAccessChange();
    return;
  }
  errorMessage.value = apiError.message;
}

async function loadRoles() {
  const sequence = ++roleListSequence;
  roleListController?.abort();
  if (reconciliationRequired.value || !canReadRoles.value) {
    roleListController = null;
    loading.value = false;
    clearRoleResults();
    forbidden.value = true;
    return;
  }

  const controller = new AbortController();
  roleListController = controller;
  errorMessage.value = '';
  forbidden.value = false;
  loading.value = true;
  try {
    const response = await listAdminRoles(
      {
        IsActive: query.isActive,
        IsBuiltIn: query.isBuiltIn,
        Page: query.page,
        PageSize: query.pageSize,
        Search: query.search.trim() || undefined,
      },
      { signal: controller.signal },
    );
    if (sequence !== roleListSequence || !canReadRoles.value) {
      return;
    }
    roles.value = response.data.items ?? [];
    total.value = response.data.totalCount ?? 0;
  } catch (error) {
    if (sequence === roleListSequence) {
      recordError(error);
    }
  } finally {
    if (sequence === roleListSequence) {
      loading.value = false;
    }
  }
}

function applyFilters() {
  query.page = 1;
  void loadRoles();
}

function openCreateDrawer() {
  if (!canManageRoleWorkspace.value) {
    return;
  }
  createDrawerOpen.value = true;
}

function openRenameDrawer(role: AdminRoleSummaryDto) {
  if (!canManageRoleWorkspace.value) {
    return;
  }
  selectedRoleId.value = role.id ?? null;
  renameDrawerOpen.value = true;
}

function openPermissionsDrawer(role: AdminRoleSummaryDto) {
  if (!canManageRoleWorkspace.value) {
    return;
  }
  selectedRoleId.value = role.id ?? null;
  permissionsDrawerOpen.value = true;
}

function openStateDialog(role: AdminRoleSummaryDto) {
  if (!canManageRoleWorkspace.value) {
    return;
  }
  selectedRoleId.value = role.id ?? null;
  targetActiveState.value = role.isActive !== true;
  stateDialogOpen.value = true;
}

function handleRowCommand(command: RoleCommand, role: AdminRoleSummaryDto) {
  switch (command) {
    case 'name': {
      openRenameDrawer(role);
      break;
    }
    case 'permissions': {
      openPermissionsDrawer(role);
      break;
    }
    case 'state': {
      openStateDialog(role);
      break;
    }
  }
}

function replaceRole(role: AdminRoleDto) {
  const index = roles.value.findIndex((item) => item.id === role.id);
  if (index !== -1) {
    roles.value.splice(index, 1, role);
  }
  void loadRoles();
}

function handleRoleCreated() {
  query.page = 1;
  void loadRoles();
}

function handleStaleRole() {
  void loadRoles();
}

function handleRoleAccessChanged(role: AdminRoleDto) {
  const index = roles.value.findIndex((item) => item.id === role.id);
  if (index !== -1) {
    roles.value.splice(index, 1, role);
  }
  void refreshPermissionsAfterAccessChange();
}

watch(canReadRoles, (canReadRoles) => {
  if (canReadRoles && !reconciliationRequired.value) {
    void loadRoles();
    return;
  }
  roleListSequence += 1;
  roleListController?.abort();
  roleListController = null;
  loading.value = false;
  closeRoleInteractions();
  clearRoleResults();
  forbidden.value = true;
});

watch(canManage, (canManage) => {
  if (!canManage) {
    closeRoleInteractions();
  }
});

onMounted(() => {
  void loadRoles();
});

onBeforeUnmount(() => {
  roleListSequence += 1;
  roleListController?.abort();
});
</script>

<template>
  <div class="role-workspace">
    <header class="workspace-header">
      <h1>Roles and permissions</h1>
      <div class="header-actions">
        <ElButton
          v-if="canManageRoleWorkspace"
          data-testid="create-role"
          type="primary"
          @click="openCreateDrawer"
        >
          <Plus :size="16" />
          New role
        </ElButton>
        <ElTooltip content="Refresh roles" placement="bottom">
          <ElButton
            :loading="loading || reconcilingPermissions"
            aria-label="Refresh roles"
            circle
            data-testid="refresh-roles"
            @click="refreshRoles"
          >
            <RotateCw :size="16" />
          </ElButton>
        </ElTooltip>
      </div>
    </header>

    <main class="workspace-body">
      <ElResult
        v-if="roleWorkspaceForbidden"
        data-testid="roles-forbidden"
        sub-title="Your account does not have access to roles and permissions."
        title="Forbidden"
      >
        <template #extra>
          <div class="forbidden-actions">
            <ElAlert
              v-if="reconciliationError"
              :closable="false"
              :title="reconciliationError"
              data-testid="role-access-reconciliation-error"
              show-icon
              type="error"
            />
            <ElButton
              :loading="reconcilingPermissions"
              data-testid="retry-role-access-reconciliation"
              @click="refreshPermissionsAfterAccessChange"
            >
              Retry access check
            </ElButton>
          </div>
        </template>
      </ElResult>

      <template v-else>
        <section class="table-toolbar" aria-label="Role filters">
          <div class="filter-row">
            <ElInput
              v-model="query.search"
              clearable
              data-testid="role-search"
              placeholder="Search role names"
              @clear="applyFilters"
              @keyup.enter="applyFilters"
            />
            <ElSelect
              v-model="query.isActive"
              clearable
              data-testid="role-active-filter"
              placeholder="All role states"
              @change="applyFilters"
            >
              <ElOption label="Active" :value="true" />
              <ElOption label="Inactive" :value="false" />
            </ElSelect>
            <ElSelect
              v-model="query.isBuiltIn"
              clearable
              data-testid="role-built-in-filter"
              placeholder="All role kinds"
              @change="applyFilters"
            >
              <ElOption label="Built-in" :value="true" />
              <ElOption label="Custom" :value="false" />
            </ElSelect>
            <ElButton @click="applyFilters">Search</ElButton>
          </div>
        </section>

        <ElAlert
          v-if="errorMessage"
          :closable="false"
          :title="errorMessage"
          show-icon
          type="error"
        >
          <template #default>
            <ElButton size="small" @click="loadRoles">Retry</ElButton>
          </template>
        </ElAlert>

        <div class="table-shell">
          <ElTable
            v-loading="loading"
            :data="roles"
            data-testid="role-table"
            row-key="id"
            stripe
          >
            <ElTableColumn label="Role" min-width="190">
              <template #default="scope">
                <div class="primary-cell">
                  <strong>{{ scope.row.name ?? 'Unnamed role' }}</strong>
                  <code>{{ scope.row.id }}</code>
                </div>
              </template>
            </ElTableColumn>
            <ElTableColumn label="Kind" width="106">
              <template #default="scope">
                <ElTag
                  :type="scope.row.isBuiltIn ? 'info' : 'primary'"
                  size="small"
                >
                  {{ scope.row.isBuiltIn ? 'Built-in' : 'Custom' }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn label="State" width="104">
              <template #default="scope">
                <ElTag
                  :type="scope.row.isActive ? 'success' : 'danger'"
                  size="small"
                >
                  {{ scope.row.isActive ? 'Active' : 'Inactive' }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn
              label="Permissions"
              prop="permissionCount"
              width="122"
            >
              <template #default="scope">
                {{ scope.row.permissionCount ?? 0 }}
              </template>
            </ElTableColumn>
            <ElTableColumn
              label="Administrators"
              prop="administratorCount"
              width="132"
            >
              <template #default="scope">
                {{ scope.row.administratorCount ?? 0 }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="Updated (UTC)" min-width="180">
              <template #default="scope">
                {{ formatDate(scope.row.updatedAtUtc) }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="Revision" prop="revision" width="100" />
            <ElTableColumn
              align="right"
              fixed="right"
              label="Actions"
              :width="isMobile ? 92 : 168"
            >
              <template #default="scope">
                <span
                  v-if="scope.row.isBuiltIn"
                  class="read-only-state"
                  data-testid="built-in-role-read-only"
                >
                  Read-only
                </span>
                <ElDropdown
                  v-else-if="isMobile && canManage"
                  trigger="click"
                  @command="handleRowCommand($event as RoleCommand, scope.row)"
                >
                  <ElButton
                    :aria-label="`Actions for ${scope.row.name}`"
                    circle
                    data-testid="role-mobile-actions"
                  >
                    <Ellipsis :size="17" />
                  </ElButton>
                  <template #dropdown>
                    <ElDropdownMenu>
                      <ElDropdownItem command="name">
                        Rename role
                      </ElDropdownItem>
                      <ElDropdownItem command="permissions">
                        Edit permissions
                      </ElDropdownItem>
                      <ElDropdownItem command="state">
                        {{
                          scope.row.isActive
                            ? 'Deactivate role'
                            : 'Activate role'
                        }}
                      </ElDropdownItem>
                    </ElDropdownMenu>
                  </template>
                </ElDropdown>
                <div v-else-if="canManage" class="icon-actions">
                  <ElTooltip content="Rename role" placement="top">
                    <ElButton
                      :aria-label="`Rename ${scope.row.name}`"
                      circle
                      size="small"
                      @click="openRenameDrawer(scope.row)"
                    >
                      <UserRoundPen :size="15" />
                    </ElButton>
                  </ElTooltip>
                  <ElTooltip content="Edit permissions" placement="top">
                    <ElButton
                      :aria-label="`Edit permissions for ${scope.row.name}`"
                      circle
                      size="small"
                      @click="openPermissionsDrawer(scope.row)"
                    >
                      <LockKeyhole :size="15" />
                    </ElButton>
                  </ElTooltip>
                  <ElTooltip
                    :content="
                      scope.row.isActive ? 'Deactivate role' : 'Activate role'
                    "
                    placement="top"
                  >
                    <ElButton
                      :aria-label="`${scope.row.isActive ? 'Deactivate' : 'Activate'} ${scope.row.name}`"
                      circle
                      size="small"
                      @click="openStateDialog(scope.row)"
                    >
                      <CircleX v-if="scope.row.isActive" :size="15" />
                      <CircleCheckBig v-else :size="15" />
                    </ElButton>
                  </ElTooltip>
                </div>
              </template>
            </ElTableColumn>
            <template #empty>
              <ElEmpty description="No roles match the current filters" />
            </template>
          </ElTable>
        </div>

        <ElPagination
          v-model:current-page="query.page"
          v-model:page-size="query.pageSize"
          :layout="paginationLayout"
          :page-sizes="[10, 20, 50, 100]"
          :pager-count="paginationPagerCount"
          :total="total"
          background
          @current-change="loadRoles"
          @size-change="
            query.page = 1;
            loadRoles();
          "
        />
      </template>
    </main>

    <RoleCreateDrawer
      v-model="createDrawerOpen"
      :can-manage="canManageRoleWorkspace"
      :can-read-roles="canReadRoleWorkspace"
      @conflict="handleStaleRole"
      @created="handleRoleCreated"
      @forbidden="refreshPermissionsAfterAccessChange"
    />
    <RoleRenameDrawer
      v-model="renameDrawerOpen"
      :can-manage="canManageRoleWorkspace"
      :role-id="selectedRoleId"
      @forbidden="refreshPermissionsAfterAccessChange"
      @stale="handleStaleRole"
      @updated="replaceRole"
    />
    <RolePermissionsDrawer
      v-model="permissionsDrawerOpen"
      :can-manage="canManageRoleWorkspace"
      :can-read-roles="canReadRoleWorkspace"
      :role-id="selectedRoleId"
      @forbidden="refreshPermissionsAfterAccessChange"
      @stale="handleStaleRole"
      @updated="handleRoleAccessChanged"
    />
    <RoleStateDialog
      v-model="stateDialogOpen"
      :can-manage="canManageRoleWorkspace"
      :role-id="selectedRoleId"
      :target-active="targetActiveState"
      @forbidden="refreshPermissionsAfterAccessChange"
      @stale="handleStaleRole"
      @updated="handleRoleAccessChanged"
    />
  </div>
</template>

<style scoped>
.role-workspace {
  min-height: 100%;
  color: var(--el-text-color-primary);
  background: var(--el-bg-color);
}

.workspace-header {
  display: flex;
  gap: 24px;
  align-items: center;
  justify-content: space-between;
  padding: 20px 24px;
  border-bottom: 1px solid var(--el-border-color-light);
}

.workspace-header h1 {
  margin: 0;
  font-size: 20px;
  font-weight: 650;
}

.header-actions,
.filter-row,
.icon-actions {
  display: flex;
  gap: 8px;
  align-items: center;
}

.workspace-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 20px 24px;
}

.table-toolbar {
  padding: 12px;
  background: var(--el-fill-color-blank);
  border: 1px solid var(--el-border-color-light);
}

.filter-row :deep(.el-input),
.filter-row :deep(.el-select) {
  width: min(240px, 100%);
}

.table-shell {
  overflow: hidden;
  border: 1px solid var(--el-border-color-light);
}

.primary-cell {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.primary-cell code {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 11px;
  color: var(--el-text-color-secondary);
}

.read-only-state {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.forbidden-actions {
  display: flex;
  flex-direction: column;
  gap: 12px;
  align-items: center;
}

@media (max-width: 640px) {
  .workspace-header,
  .workspace-body {
    padding: 16px;
  }

  .workspace-header {
    align-items: flex-start;
  }

  .filter-row {
    display: grid;
    grid-template-columns: 1fr;
  }

  .filter-row :deep(.el-input),
  .filter-row :deep(.el-select) {
    width: 100%;
  }

  .filter-row :deep(.el-button) {
    min-height: 44px;
  }
}
</style>
