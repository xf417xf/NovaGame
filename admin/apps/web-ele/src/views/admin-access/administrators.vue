<script lang="ts" setup>
import type { RouteRecordRaw } from 'vue-router';

import type {
  AdministratorDto,
  AdminRoleReferenceDto,
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
import { useAccessStore, useUserStore } from '@vben/stores';

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
  listAdministrators,
  listAdminRoles,
  toNovaGameApiError,
} from '#/api';
import {
  AdminAccessPermission,
  hasAdminAccessPermission,
} from '#/features/admin-access/permissions';
import { useAuthStore } from '#/store';

import AdministratorCreateDrawer from './components/administrator-create-drawer.vue';
import AdministratorDisplayNameDrawer from './components/administrator-display-name-drawer.vue';
import AdministratorPasswordResetDialog from './components/administrator-password-reset-dialog.vue';
import AdministratorRolesDrawer from './components/administrator-roles-drawer.vue';
import AdministratorStateDialog from './components/administrator-state-dialog.vue';

defineOptions({ name: 'AdministratorAccessAdministrators' });

type AdministratorCommand =
  | 'display-name'
  | 'password-reset'
  | 'roles'
  | 'state';

const accessStore = useAccessStore();
const authStore = useAuthStore();
const router = useRouter();
const userStore = useUserStore();
const isMobile = useMediaQuery('(max-width: 640px)');

const administrators = ref<AdministratorDto[]>([]);
const createDrawerOpen = ref(false);
const displayNameDrawerOpen = ref(false);
const errorMessage = ref('');
const forbidden = ref(false);
const loading = ref(false);
const passwordResetDialogOpen = ref(false);
const roleFilterOptions = ref<AdminRoleSummaryDto[]>([]);
const rolesDrawerOpen = ref(false);
const selectedAdministratorId = ref<null | string>(null);
const stateDialogOpen = ref(false);
const targetActiveState = ref(false);
const total = ref(0);
const query = reactive({
  isActive: undefined as boolean | undefined,
  page: 1,
  pageSize: 20,
  roleId: undefined as string | undefined,
  search: '',
});
let administratorListController: AbortController | null = null;
let administratorListSequence = 0;
let roleFilterController: AbortController | null = null;
let roleFilterSequence = 0;
let permissionRefreshPromise: null | Promise<void> = null;

const canManage = computed(() =>
  hasAdminAccessPermission(
    accessStore.accessCodes,
    AdminAccessPermission.AdministratorsManage,
  ),
);
const canReadAdministrators = computed(() =>
  hasAdminAccessPermission(
    accessStore.accessCodes,
    AdminAccessPermission.AdministratorsRead,
  ),
);
const canReadRoles = computed(() =>
  hasAdminAccessPermission(
    accessStore.accessCodes,
    AdminAccessPermission.RolesRead,
  ),
);
const canResetPassword = computed(() =>
  hasAdminAccessPermission(
    accessStore.accessCodes,
    AdminAccessPermission.AdministratorsResetPassword,
  ),
);
const currentAdministratorId = computed(
  () => userStore.userInfo?.userId ?? null,
);
const paginationLayout = computed(() =>
  isMobile.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next',
);
const paginationPagerCount = computed(() => (isMobile.value ? 5 : 7));

function formatDate(value: null | string | undefined): string {
  if (!value) {
    return 'Never';
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return 'Unavailable';
  }

  return date.toISOString().replace('T', ' ').replace('Z', ' UTC');
}

function isCurrentAdministrator(administrator: AdministratorDto): boolean {
  return Boolean(
    administrator.id && administrator.id === currentAdministratorId.value,
  );
}

function hasAdministratorRowCommand(administrator: AdministratorDto): boolean {
  return (
    canManage.value ||
    (canResetPassword.value && !isCurrentAdministrator(administrator))
  );
}

function roleLabel(role: AdminRoleReferenceDto): string {
  const name = role.name ?? role.id ?? 'Unnamed role';
  return role.isActive === false ? `${name} (inactive)` : name;
}

function roleFilterLabel(role: AdminRoleSummaryDto): string {
  const name = role.name ?? role.id ?? 'Unnamed role';
  return role.isActive === false ? `${name} (inactive)` : name;
}

function clearAdministratorResults() {
  administrators.value = [];
  total.value = 0;
}

function closeAdministratorInteractions() {
  createDrawerOpen.value = false;
  displayNameDrawerOpen.value = false;
  passwordResetDialogOpen.value = false;
  rolesDrawerOpen.value = false;
  selectedAdministratorId.value = null;
  stateDialogOpen.value = false;
}

function clearRoleFilterState() {
  roleFilterSequence += 1;
  roleFilterController?.abort();
  roleFilterController = null;
  roleFilterOptions.value = [];
  query.roleId = undefined;
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

async function refreshPermissionsAfterForbidden() {
  if (permissionRefreshPromise) {
    await permissionRefreshPromise;
    return;
  }

  const refresh = (async () => {
    closeAdministratorInteractions();
    clearRoleFilterState();
    administratorListSequence += 1;
    administratorListController?.abort();
    administratorListController = null;
    loading.value = false;
    errorMessage.value = '';
    forbidden.value = true;

    try {
      const refreshed = await authStore.refreshAuthenticatedState();
      if (!refreshed) {
        return;
      }

      if (!canReadRoles.value) {
        clearRoleFilterState();
      }

      if (!canReadAdministrators.value) {
        clearAdministratorResults();
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

      forbidden.value = false;
      await loadAdministrators();
    } catch (error) {
      if (!isNovaGameRequestCancelled(error)) {
        errorMessage.value = toNovaGameApiError(error).message;
      }
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

function recordError(error: unknown) {
  if (isNovaGameRequestCancelled(error)) {
    return;
  }
  const apiError = toNovaGameApiError(error);
  if (apiError.status === 403) {
    void refreshPermissionsAfterForbidden();
    return;
  }
  errorMessage.value = apiError.message;
}

async function loadAdministrators() {
  const sequence = ++administratorListSequence;
  administratorListController?.abort();
  const controller = new AbortController();
  administratorListController = controller;
  errorMessage.value = '';
  forbidden.value = false;
  loading.value = true;

  try {
    const response = await listAdministrators(
      {
        IsActive: query.isActive,
        Page: query.page,
        PageSize: query.pageSize,
        RoleId: query.roleId,
        Search: query.search.trim() || undefined,
      },
      { signal: controller.signal },
    );
    if (
      sequence !== administratorListSequence ||
      !canReadAdministrators.value
    ) {
      return;
    }
    administrators.value = response.data.items ?? [];
    total.value = response.data.totalCount ?? 0;
  } catch (error) {
    if (sequence === administratorListSequence) {
      recordError(error);
    }
  } finally {
    if (sequence === administratorListSequence) {
      loading.value = false;
    }
  }
}

async function loadRoleFilterOptions(search = '') {
  const sequence = ++roleFilterSequence;
  if (!canReadRoles.value) {
    roleFilterController?.abort();
    roleFilterController = null;
    roleFilterOptions.value = [];
    return;
  }

  roleFilterController?.abort();
  const controller = new AbortController();
  roleFilterController = controller;
  try {
    const response = await listAdminRoles(
      {
        Page: 1,
        PageSize: 100,
        Search: search.trim() || undefined,
      },
      { signal: controller.signal },
    );
    if (sequence === roleFilterSequence && canReadRoles.value) {
      roleFilterOptions.value = response.data.items ?? [];
    }
  } catch (error) {
    if (sequence === roleFilterSequence) {
      recordError(error);
    }
  }
}

function applyFilters() {
  query.page = 1;
  void loadAdministrators();
}

function openCreateDrawer() {
  createDrawerOpen.value = true;
}

function openDisplayNameDrawer(administrator: AdministratorDto) {
  selectedAdministratorId.value = administrator.id ?? null;
  displayNameDrawerOpen.value = true;
}

function openRolesDrawer(administrator: AdministratorDto) {
  selectedAdministratorId.value = administrator.id ?? null;
  rolesDrawerOpen.value = true;
}

function openStateDialog(administrator: AdministratorDto) {
  selectedAdministratorId.value = administrator.id ?? null;
  targetActiveState.value = administrator.isActive !== true;
  stateDialogOpen.value = true;
}

function openPasswordResetDialog(administrator: AdministratorDto) {
  selectedAdministratorId.value = administrator.id ?? null;
  passwordResetDialogOpen.value = true;
}

function handleRowCommand(
  command: AdministratorCommand,
  administrator: AdministratorDto,
) {
  switch (command) {
    case 'display-name': {
      openDisplayNameDrawer(administrator);
      break;
    }
    case 'password-reset': {
      openPasswordResetDialog(administrator);
      break;
    }
    case 'roles': {
      openRolesDrawer(administrator);
      break;
    }
    case 'state': {
      openStateDialog(administrator);
      break;
    }
  }
}

function replaceAdministrator(administrator: AdministratorDto) {
  const index = administrators.value.findIndex(
    (item) => item.id === administrator.id,
  );
  if (index !== -1) {
    administrators.value.splice(index, 1, administrator);
  }
  void loadAdministrators();
}

function handleAdministratorCreated() {
  query.page = 1;
  void loadAdministrators();
}

function handleStaleAdministrator() {
  void loadAdministrators();
}

watch(canReadAdministrators, (canRead) => {
  if (canRead) {
    return;
  }
  administratorListSequence += 1;
  administratorListController?.abort();
  administratorListController = null;
  loading.value = false;
  clearAdministratorResults();
  forbidden.value = true;
});

watch(canReadRoles, (canReadRoles, previouslyCouldReadRoles) => {
  if (canReadRoles) {
    return;
  }
  const hadRoleFilter = query.roleId !== undefined;
  clearRoleFilterState();
  createDrawerOpen.value = false;
  rolesDrawerOpen.value = false;
  if (
    previouslyCouldReadRoles &&
    hadRoleFilter &&
    canReadAdministrators.value
  ) {
    void loadAdministrators();
  }
});

onMounted(() => {
  void loadAdministrators();
  void loadRoleFilterOptions();
});

onBeforeUnmount(() => {
  administratorListSequence += 1;
  administratorListController?.abort();
  roleFilterSequence += 1;
  roleFilterController?.abort();
});
</script>

<template>
  <div class="administrator-workspace">
    <header class="workspace-header">
      <div>
        <h1>Administrators</h1>
        <p>Manage framework administrator accounts and assigned roles.</p>
      </div>
      <div class="header-actions">
        <ElButton
          v-if="canManage"
          data-testid="create-administrator"
          type="primary"
          @click="openCreateDrawer"
        >
          <Plus :size="16" />
          New administrator
        </ElButton>
        <ElTooltip content="Refresh administrators" placement="bottom">
          <ElButton
            :loading="loading"
            aria-label="Refresh administrators"
            circle
            data-testid="refresh-administrators"
            @click="loadAdministrators"
          >
            <RotateCw :size="16" />
          </ElButton>
        </ElTooltip>
      </div>
    </header>

    <main class="workspace-body">
      <ElResult
        v-if="forbidden"
        sub-title="Your account does not have access to administrator accounts."
        title="Forbidden"
      >
        <template #extra>
          <ElButton @click="loadAdministrators">Retry</ElButton>
        </template>
      </ElResult>

      <template v-else>
        <section class="table-toolbar" aria-label="Administrator filters">
          <div class="filter-row">
            <ElInput
              v-model="query.search"
              aria-label="Search administrators"
              clearable
              data-testid="administrator-search"
              placeholder="Search user names"
              @clear="applyFilters"
              @keyup.enter="applyFilters"
            />
            <ElSelect
              v-model="query.isActive"
              aria-label="Administrator account state"
              clearable
              data-testid="administrator-active-filter"
              placeholder="All account states"
              @change="applyFilters"
            >
              <ElOption label="Active" :value="true" />
              <ElOption label="Inactive" :value="false" />
            </ElSelect>
            <ElSelect
              v-if="canReadRoles"
              v-model="query.roleId"
              aria-label="Administrator role"
              clearable
              filterable
              remote
              reserve-keyword
              data-testid="administrator-role-filter"
              placeholder="All roles"
              :remote-method="loadRoleFilterOptions"
              @change="applyFilters"
            >
              <ElOption
                v-for="role in roleFilterOptions"
                :key="role.id"
                :label="roleFilterLabel(role)"
                :value="role.id ?? ''"
              />
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
            <ElButton size="small" @click="loadAdministrators">Retry</ElButton>
          </template>
        </ElAlert>

        <div class="table-shell">
          <ElTable
            v-loading="loading"
            :data="administrators"
            data-testid="administrator-table"
            row-key="id"
            stripe
          >
            <ElTableColumn label="User name" min-width="180">
              <template #default="scope">
                <div class="primary-cell">
                  <strong>{{ scope.row.userName ?? 'Unknown' }}</strong>
                  <code>{{ scope.row.id }}</code>
                </div>
              </template>
            </ElTableColumn>
            <ElTableColumn
              label="Display name"
              min-width="180"
              prop="displayName"
            />
            <ElTableColumn label="Roles" min-width="220">
              <template #default="scope">
                <div v-if="scope.row.roles?.length" class="role-list">
                  <ElTag
                    v-for="role in scope.row.roles"
                    :key="role.id"
                    :type="role.isActive === false ? 'warning' : 'info'"
                    size="small"
                  >
                    {{ roleLabel(role) }}
                  </ElTag>
                </div>
                <span v-else class="muted-value">No roles</span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="State" width="108">
              <template #default="scope">
                <ElTag
                  :type="scope.row.isActive ? 'success' : 'danger'"
                  size="small"
                >
                  {{ scope.row.isActive ? 'Active' : 'Inactive' }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn label="Last login (UTC)" min-width="180">
              <template #default="scope">
                {{ formatDate(scope.row.lastLoginAtUtc) }}
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
              :width="isMobile ? 92 : 204"
            >
              <template #default="scope">
                <ElDropdown
                  v-if="isMobile && hasAdministratorRowCommand(scope.row)"
                  trigger="click"
                  @command="
                    handleRowCommand($event as AdministratorCommand, scope.row)
                  "
                >
                  <ElTooltip content="Administrator actions" placement="top">
                    <ElButton
                      :aria-label="`Actions for ${scope.row.userName}`"
                      circle
                      data-testid="administrator-mobile-actions"
                    >
                      <Ellipsis :size="17" />
                    </ElButton>
                  </ElTooltip>
                  <template #dropdown>
                    <ElDropdownMenu>
                      <ElDropdownItem v-if="canManage" command="display-name">
                        Edit display name
                      </ElDropdownItem>
                      <ElDropdownItem
                        v-if="
                          canManage &&
                          !isCurrentAdministrator(scope.row) &&
                          canReadRoles
                        "
                        command="roles"
                      >
                        Edit roles
                      </ElDropdownItem>
                      <ElDropdownItem
                        v-if="canManage && !isCurrentAdministrator(scope.row)"
                        command="state"
                      >
                        {{
                          scope.row.isActive
                            ? 'Deactivate account'
                            : 'Activate account'
                        }}
                      </ElDropdownItem>
                      <ElDropdownItem
                        v-if="
                          canResetPassword && !isCurrentAdministrator(scope.row)
                        "
                        command="password-reset"
                      >
                        Reset password
                      </ElDropdownItem>
                    </ElDropdownMenu>
                  </template>
                </ElDropdown>
                <span v-else-if="isMobile" class="read-only-state">
                  Read only
                </span>
                <div v-else class="icon-actions">
                  <ElTooltip
                    v-if="canManage"
                    content="Edit display name"
                    placement="top"
                  >
                    <ElButton
                      :aria-label="`Edit display name for ${scope.row.userName}`"
                      circle
                      size="small"
                      @click="openDisplayNameDrawer(scope.row)"
                    >
                      <UserRoundPen :size="15" />
                    </ElButton>
                  </ElTooltip>
                  <ElTooltip
                    v-if="
                      canManage &&
                      !isCurrentAdministrator(scope.row) &&
                      canReadRoles
                    "
                    content="Edit roles"
                    placement="top"
                  >
                    <ElButton
                      :aria-label="`Edit roles for ${scope.row.userName}`"
                      circle
                      size="small"
                      @click="openRolesDrawer(scope.row)"
                    >
                      <UserRoundPen :size="15" />
                    </ElButton>
                  </ElTooltip>
                  <ElTooltip
                    v-if="canManage && !isCurrentAdministrator(scope.row)"
                    :content="
                      scope.row.isActive
                        ? 'Deactivate account'
                        : 'Activate account'
                    "
                    placement="top"
                  >
                    <ElButton
                      :aria-label="`${scope.row.isActive ? 'Deactivate' : 'Activate'} ${scope.row.userName}`"
                      circle
                      size="small"
                      @click="openStateDialog(scope.row)"
                    >
                      <CircleX v-if="scope.row.isActive" :size="15" />
                      <CircleCheckBig v-else :size="15" />
                    </ElButton>
                  </ElTooltip>
                  <ElTooltip
                    v-if="
                      canResetPassword && !isCurrentAdministrator(scope.row)
                    "
                    content="Reset password"
                    placement="top"
                  >
                    <ElButton
                      :aria-label="`Reset password for ${scope.row.userName}`"
                      circle
                      size="small"
                      @click="openPasswordResetDialog(scope.row)"
                    >
                      <LockKeyhole :size="15" />
                    </ElButton>
                  </ElTooltip>
                </div>
              </template>
            </ElTableColumn>
            <template #empty>
              <ElEmpty
                description="No administrators match the current filters"
              />
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
          @current-change="loadAdministrators"
          @size-change="
            query.page = 1;
            loadAdministrators();
          "
        />
      </template>
    </main>

    <AdministratorCreateDrawer
      v-model="createDrawerOpen"
      :can-manage="canManage"
      :can-read-roles="canReadRoles"
      @conflict="handleStaleAdministrator"
      @created="handleAdministratorCreated"
      @forbidden="refreshPermissionsAfterForbidden"
    />
    <AdministratorDisplayNameDrawer
      v-model="displayNameDrawerOpen"
      :administrator-id="selectedAdministratorId"
      :can-manage="canManage"
      @forbidden="refreshPermissionsAfterForbidden"
      @stale="handleStaleAdministrator"
      @updated="replaceAdministrator"
    />
    <AdministratorRolesDrawer
      v-model="rolesDrawerOpen"
      :administrator-id="selectedAdministratorId"
      :can-manage="canManage"
      :can-read-roles="canReadRoles"
      @forbidden="refreshPermissionsAfterForbidden"
      @stale="handleStaleAdministrator"
      @updated="replaceAdministrator"
    />
    <AdministratorStateDialog
      v-model="stateDialogOpen"
      :administrator-id="selectedAdministratorId"
      :can-manage="canManage"
      :target-active="targetActiveState"
      @forbidden="refreshPermissionsAfterForbidden"
      @stale="handleStaleAdministrator"
      @updated="replaceAdministrator"
    />
    <AdministratorPasswordResetDialog
      v-model="passwordResetDialogOpen"
      :administrator-id="selectedAdministratorId"
      :can-reset-password="canResetPassword"
      @forbidden="refreshPermissionsAfterForbidden"
      @stale="handleStaleAdministrator"
      @updated="replaceAdministrator"
    />
  </div>
</template>

<style scoped>
.administrator-workspace {
  min-height: 100%;
  color: var(--el-text-color-primary);
  background: var(--el-bg-color);
}

.workspace-header {
  display: flex;
  gap: 24px;
  align-items: center;
  justify-content: space-between;
  min-height: 82px;
  padding: 16px 24px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.workspace-header h1,
.workspace-header p {
  margin: 0;
}

.workspace-header h1 {
  font-size: 22px;
  font-weight: 650;
}

.workspace-header p {
  margin-top: 5px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.header-actions,
.table-toolbar,
.filter-row,
.icon-actions,
.role-list {
  display: flex;
  gap: 8px;
  align-items: center;
}

.workspace-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding: 20px 24px 28px;
}

.table-toolbar {
  justify-content: space-between;
  min-height: 40px;
}

.filter-row {
  width: min(760px, 100%);
}

.filter-row > :first-child {
  flex: 1 1 220px;
}

.filter-row .el-select {
  width: 170px;
}

.table-shell {
  min-height: 360px;
  overflow-x: auto;
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
  white-space: nowrap;
}

.role-list {
  flex-wrap: wrap;
}

.muted-value {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.read-only-state {
  display: inline-block;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  white-space: nowrap;
}

.icon-actions {
  justify-content: flex-end;
}

.el-pagination {
  justify-content: flex-end;
  margin-top: 2px;
}

@media (max-width: 900px) {
  .workspace-header {
    flex-direction: column;
    align-items: flex-start;
  }
}

@media (max-width: 640px) {
  .workspace-header,
  .workspace-body {
    padding-right: 14px;
    padding-left: 14px;
  }

  .header-actions,
  .table-toolbar,
  .filter-row {
    flex-direction: column;
    align-items: stretch;
    width: 100%;
  }

  .filter-row .el-select {
    width: 100%;
  }

  .header-actions :deep(.el-button) {
    min-height: 44px;
    margin-left: 0;
  }

  .header-actions :deep(.el-button.is-circle) {
    align-self: flex-end;
    width: 44px;
  }

  .el-pagination {
    --el-pagination-button-height: 44px;
    --el-pagination-button-width: 44px;

    justify-content: center;
  }
}
</style>
