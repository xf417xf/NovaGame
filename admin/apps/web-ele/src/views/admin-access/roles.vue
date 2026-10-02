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
  ElDialog,
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
import {
  operationsErrorMessage,
  tOperations,
  useOperationsMessage,
} from '#/locales/operations';
import { useAuthStore } from '#/store';

import RoleCreateDrawer from './components/role-create-drawer.vue';
import RolePermissionsDrawer from './components/role-permissions-drawer.vue';
import RoleRenameDrawer from './components/role-rename-drawer.vue';
import RoleStateDialog from './components/role-state-dialog.vue';

defineOptions({ name: 'AdministratorAccessRoles' });

type RoleCommand = 'name' | 'permissions' | 'state';

type RetainedRoleDraft =
  | { kind: 'permissions'; permissionNames: string[] }
  | { kind: 'rename'; name: string };

const accessStore = useAccessStore();
const authStore = useAuthStore();
const router = useRouter();
const isMobile = useMediaQuery('(max-width: 640px)');

const createDrawerOpen = ref(false);
const errorMessage = useOperationsMessage();
const forbidden = ref(false);
const loading = ref(false);
const reconciliationError = useOperationsMessage();
const reconciliationRequired = ref(false);
const renameDrawerOpen = ref(false);
const reconcilingPermissions = ref(false);
const retainedRoleDraft = ref<null | RetainedRoleDraft>(null);
const retainedRoleDraftReviewOpen = ref(false);
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
  if (!value) {
    return tOperations('never');
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return tOperations('unavailable');
  }

  return date.toISOString().replace('T', ' ').replace('Z', ' UTC');
}

function clearRoleResults() {
  roles.value = [];
  total.value = 0;
}

function clearRetainedRoleDraft() {
  retainedRoleDraft.value = null;
  retainedRoleDraftReviewOpen.value = false;
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
    clearRetainedRoleDraft();
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
        reconciliationError.value = () => operationsErrorMessage(error);
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
  errorMessage.value = () => operationsErrorMessage(apiError);
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
  closeRoleInteractions();
  void loadRoles();
}

function handleStaleRoleDraft(draft: null | RetainedRoleDraft) {
  if (draft) {
    retainedRoleDraft.value =
      draft.kind === 'rename'
        ? { kind: 'rename', name: draft.name }
        : {
            kind: 'permissions',
            permissionNames: [...draft.permissionNames],
          };
    retainedRoleDraftReviewOpen.value = false;
  }
  handleStaleRole();
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
  clearRetainedRoleDraft();
  closeRoleInteractions();
  clearRoleResults();
  forbidden.value = true;
});

watch(canManage, (canManage) => {
  if (!canManage) {
    clearRetainedRoleDraft();
    closeRoleInteractions();
  }
});

onMounted(() => {
  void loadRoles();
});

onBeforeUnmount(() => {
  roleListSequence += 1;
  roleListController?.abort();
  clearRetainedRoleDraft();
});
</script>

<template>
  <div class="role-workspace">
    <header class="workspace-header">
      <h1>{{ tOperations('rolesAndPermissions') }}</h1>
      <div class="header-actions">
        <ElButton
          v-if="canManageRoleWorkspace"
          data-testid="create-role"
          type="primary"
          @click="openCreateDrawer"
        >
          <Plus :size="16" />
          {{ tOperations('newRole') }}
        </ElButton>
        <ElTooltip :content="tOperations('refreshRoles')" placement="bottom">
          <ElButton
            :loading="loading || reconcilingPermissions"
            :aria-label="tOperations('refreshRoles')"
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
        :sub-title="
          tOperations('yourAccountDoesNotHaveAccessToRolesAndPermissions')
        "
        :title="tOperations('forbidden')"
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
              {{ tOperations('retryAccessCheck') }}
            </ElButton>
          </div>
        </template>
      </ElResult>

      <template v-else>
        <section class="table-toolbar" :aria-label="tOperations('roleFilters')">
          <div class="filter-row">
            <ElInput
              v-model="query.search"
              :aria-label="tOperations('searchRoles')"
              clearable
              data-testid="role-search"
              :placeholder="tOperations('searchRoleNames')"
              @clear="applyFilters"
              @keyup.enter="applyFilters"
            />
            <ElSelect
              v-model="query.isActive"
              :aria-label="tOperations('roleState')"
              clearable
              data-testid="role-active-filter"
              :placeholder="tOperations('allRoleStates')"
              @change="applyFilters"
            >
              <ElOption :label="tOperations('active')" :value="true" />
              <ElOption :label="tOperations('inactive')" :value="false" />
            </ElSelect>
            <ElSelect
              v-model="query.isBuiltIn"
              :aria-label="tOperations('roleKind')"
              clearable
              data-testid="role-built-in-filter"
              :placeholder="tOperations('allRoleKinds')"
              @change="applyFilters"
            >
              <ElOption :label="tOperations('builtIn')" :value="true" />
              <ElOption :label="tOperations('custom')" :value="false" />
            </ElSelect>
            <ElButton @click="applyFilters">
              {{ tOperations('search') }}
            </ElButton>
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
            <ElButton size="small" @click="loadRoles">
              {{ tOperations('retry') }}
            </ElButton>
          </template>
        </ElAlert>

        <ElAlert
          v-if="retainedRoleDraft"
          :closable="false"
          data-testid="retained-role-draft"
          show-icon
          :title="
            tOperations(
              'unsavedChangesFromAnUnavailableDetailAreAvailableForReview',
            )
          "
          type="warning"
        >
          <template #default>
            <div class="retained-draft-actions">
              <ElButton
                data-testid="review-retained-role-draft"
                size="small"
                @click="retainedRoleDraftReviewOpen = true"
              >
                {{ tOperations('reviewRetainedDraft') }}
              </ElButton>
              <ElButton
                data-testid="discard-retained-role-draft"
                size="small"
                text
                @click="clearRetainedRoleDraft"
              >
                {{ tOperations('discard') }}
              </ElButton>
            </div>
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
            <ElTableColumn :label="tOperations('role')" min-width="190">
              <template #default="scope">
                <div class="primary-cell">
                  <strong>{{
                    scope.row.name ?? tOperations('unnamedRole')
                  }}</strong>
                  <code>{{ scope.row.id }}</code>
                </div>
              </template>
            </ElTableColumn>
            <ElTableColumn :label="tOperations('kind')" width="106">
              <template #default="scope">
                <ElTag
                  :type="scope.row.isBuiltIn ? 'info' : 'primary'"
                  size="small"
                >
                  {{
                    scope.row.isBuiltIn
                      ? tOperations('builtIn')
                      : tOperations('custom')
                  }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn :label="tOperations('state')" width="104">
              <template #default="scope">
                <ElTag
                  :type="scope.row.isActive ? 'success' : 'danger'"
                  size="small"
                >
                  {{
                    scope.row.isActive
                      ? tOperations('active')
                      : tOperations('inactive')
                  }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn
              :label="tOperations('permissions')"
              prop="permissionCount"
              width="122"
            >
              <template #default="scope">
                {{ scope.row.permissionCount ?? 0 }}
              </template>
            </ElTableColumn>
            <ElTableColumn
              :label="tOperations('administrators')"
              prop="administratorCount"
              width="132"
            >
              <template #default="scope">
                {{ scope.row.administratorCount ?? 0 }}
              </template>
            </ElTableColumn>
            <ElTableColumn :label="tOperations('updatedUtc')" min-width="180">
              <template #default="scope">
                {{ formatDate(scope.row.updatedAtUtc) }}
              </template>
            </ElTableColumn>
            <ElTableColumn
              :label="tOperations('revision')"
              prop="revision"
              width="100"
            />
            <ElTableColumn
              align="right"
              fixed="right"
              :label="tOperations('actions')"
              :width="isMobile ? 92 : 168"
            >
              <template #default="scope">
                <span
                  v-if="scope.row.isBuiltIn"
                  class="read-only-state"
                  data-testid="built-in-role-read-only"
                >
                  {{ tOperations('readOnlyHyphenated') }}
                </span>
                <ElDropdown
                  v-else-if="isMobile && canManage"
                  trigger="click"
                  @command="handleRowCommand($event as RoleCommand, scope.row)"
                >
                  <ElButton
                    :aria-label="
                      tOperations('actionsForParameter', {
                        value0: scope.row.name,
                      })
                    "
                    circle
                    data-testid="role-mobile-actions"
                    :title="tOperations('roleActions')"
                  >
                    <Ellipsis :size="17" />
                  </ElButton>
                  <template #dropdown>
                    <ElDropdownMenu>
                      <ElDropdownItem command="name">
                        {{ tOperations('renameRole') }}
                      </ElDropdownItem>
                      <ElDropdownItem command="permissions">
                        {{ tOperations('editPermissions') }}
                      </ElDropdownItem>
                      <ElDropdownItem command="state">
                        {{
                          scope.row.isActive
                            ? tOperations('deactivateRole')
                            : tOperations('activateRole')
                        }}
                      </ElDropdownItem>
                    </ElDropdownMenu>
                  </template>
                </ElDropdown>
                <div v-else-if="canManage" class="icon-actions">
                  <ElTooltip
                    :content="tOperations('renameRole')"
                    placement="top"
                  >
                    <ElButton
                      :aria-label="
                        tOperations('renameParameter', {
                          value0: scope.row.name,
                        })
                      "
                      circle
                      size="small"
                      @click="openRenameDrawer(scope.row)"
                    >
                      <UserRoundPen :size="15" />
                    </ElButton>
                  </ElTooltip>
                  <ElTooltip
                    :content="tOperations('editPermissions')"
                    placement="top"
                  >
                    <ElButton
                      :aria-label="
                        tOperations('editPermissionsForParameter', {
                          value0: scope.row.name,
                        })
                      "
                      circle
                      size="small"
                      @click="openPermissionsDrawer(scope.row)"
                    >
                      <LockKeyhole :size="15" />
                    </ElButton>
                  </ElTooltip>
                  <ElTooltip
                    :content="
                      scope.row.isActive
                        ? tOperations('deactivateRole')
                        : tOperations('activateRole')
                    "
                    placement="top"
                  >
                    <ElButton
                      :aria-label="`${scope.row.isActive ? tOperations('deactivate') : tOperations('activate')} ${scope.row.name}`"
                      circle
                      size="small"
                      @click="openStateDialog(scope.row)"
                    >
                      <CircleX v-if="scope.row.isActive" :size="15" />
                      <CircleCheckBig v-else :size="15" />
                    </ElButton>
                  </ElTooltip>
                </div>
                <span v-else class="read-only-state">{{
                  tOperations('readOnly')
                }}</span>
              </template>
            </ElTableColumn>
            <template #empty>
              <ElEmpty
                :description="tOperations('noRolesMatchTheCurrentFilters')"
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
          @current-change="loadRoles"
          @size-change="
            query.page = 1;
            loadRoles();
          "
        />
      </template>
    </main>

    <ElDialog
      :model-value="retainedRoleDraftReviewOpen"
      :title="tOperations('reviewRetainedDraft')"
      width="min(520px, 94vw)"
      @close="retainedRoleDraftReviewOpen = false"
    >
      <div
        v-if="retainedRoleDraft"
        class="retained-draft-content"
        data-testid="retained-role-draft-review"
      >
        <p>
          {{
            tOperations(
              'theOriginalRoleDetailIsNoLongerAvailableThisDraftIsReadOnlyAndCannotBeSubmitted',
            )
          }}
        </p>
        <template v-if="retainedRoleDraft.kind === 'rename'">
          <span class="retained-draft-label">{{
            tOperations('roleName')
          }}</span>
          <strong class="retained-draft-value">
            {{ retainedRoleDraft.name || tOperations('noValueEntered') }}
          </strong>
        </template>
        <template v-else>
          <span class="retained-draft-label">{{
            tOperations('selectedPermissions')
          }}</span>
          <div class="retained-draft-tag-list">
            <ElTag
              v-for="permissionName in retainedRoleDraft.permissionNames"
              :key="permissionName"
              class="retained-draft-tag"
              type="warning"
            >
              {{ permissionName }}
            </ElTag>
            <span v-if="retainedRoleDraft.permissionNames.length === 0">
              {{ tOperations('noPermissionsSelected') }}
            </span>
          </div>
        </template>
      </div>
      <template #footer>
        <div class="dialog-footer">
          <ElButton @click="retainedRoleDraftReviewOpen = false">
            {{ tOperations('close') }}
          </ElButton>
          <ElButton
            data-testid="discard-retained-role-draft-from-review"
            type="warning"
            @click="clearRetainedRoleDraft"
          >
            {{ tOperations('discardDraft') }}
          </ElButton>
        </div>
      </template>
    </ElDialog>

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
      @stale="handleStaleRoleDraft"
      @updated="replaceRole"
    />
    <RolePermissionsDrawer
      v-model="permissionsDrawerOpen"
      :can-manage="canManageRoleWorkspace"
      :can-read-roles="canReadRoleWorkspace"
      :role-id="selectedRoleId"
      @forbidden="refreshPermissionsAfterAccessChange"
      @stale="handleStaleRoleDraft"
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
  min-height: 360px;
  overflow: auto;
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

.retained-draft-actions,
.dialog-footer {
  display: flex;
  gap: 8px;
  align-items: center;
}

.retained-draft-content {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
}

.retained-draft-content p {
  margin: 0;
  color: var(--el-text-color-secondary);
}

.retained-draft-label {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.retained-draft-value,
.retained-draft-tag {
  min-width: 0;
  overflow-wrap: anywhere;
}

.retained-draft-tag-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  min-width: 0;
}

.dialog-footer {
  justify-content: flex-end;
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
    padding: 16px;
  }

  .workspace-header {
    align-items: flex-start;
  }

  .header-actions {
    flex-direction: column;
    align-items: stretch;
    width: 100%;
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

  .retained-draft-actions,
  .dialog-footer {
    flex-direction: column;
    align-items: stretch;
  }

  .retained-draft-actions :deep(.el-button),
  .dialog-footer :deep(.el-button) {
    min-height: 44px;
    margin-left: 0;
  }
}
</style>
