<script lang="ts" setup>
import type { RouteRecordRaw } from 'vue-router';

import type {
  AdministratorDto,
  AdminSessionFamilyDto,
  AdminSessionFamilyState,
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

import { Copy, Ellipsis, RotateCw } from '@vben/icons';
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
  listAdministrators,
  listSessionFamilies,
  revokeSessionFamily,
  toNovaGameApiError,
} from '#/api';
import {
  AdminAccessErrorCode,
  AdminAccessPermission,
  hasAdminAccessPermission,
} from '#/features/admin-access/permissions';
import { useAuthStore } from '#/store';

defineOptions({ name: 'AdministratorAccessSessions' });

const guidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

const accessStore = useAccessStore();
const authStore = useAuthStore();
const router = useRouter();
const isMobile = useMediaQuery('(max-width: 640px)');

const administratorOptions = ref<AdministratorDto[]>([]);
const copyMessage = ref('');
const copyMessageType = ref<'error' | 'success'>('success');
const errorMessage = ref('');
const forbidden = ref(false);
const loading = ref(false);
const reconciliationError = ref('');
const reconciliationRequired = ref(false);
const reconcilingPermissions = ref(false);
const revokeDialogOpen = ref(false);
const revokeError = ref('');
const revoking = ref(false);
const selectedFamily = ref<AdminSessionFamilyDto | null>(null);
const sessionFamilies = ref<AdminSessionFamilyDto[]>([]);
const total = ref(0);
const administratorFilterError = ref('');
const query = reactive({
  administratorId: '',
  page: 1,
  pageSize: 20,
  state: undefined as AdminSessionFamilyState | undefined,
});
let administratorOptionsController: AbortController | null = null;
let administratorOptionsSequence = 0;
let permissionRefreshPromise: null | Promise<void> = null;
let sessionListController: AbortController | null = null;
let sessionListSequence = 0;

const canReadAdministrators = computed(() =>
  hasAdminAccessPermission(
    accessStore.accessCodes,
    AdminAccessPermission.AdministratorsRead,
  ),
);
const canReadSessions = computed(() =>
  hasAdminAccessPermission(
    accessStore.accessCodes,
    AdminAccessPermission.SessionsRead,
  ),
);
const canRevokeSessions = computed(() =>
  hasAdminAccessPermission(
    accessStore.accessCodes,
    AdminAccessPermission.SessionsRevoke,
  ),
);
const canReadSessionWorkspace = computed(
  () => canReadSessions.value && !reconciliationRequired.value,
);
const canRevokeSessionWorkspace = computed(
  () => canRevokeSessions.value && canReadSessionWorkspace.value,
);
const paginationLayout = computed(() =>
  isMobile.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next',
);
const paginationPagerCount = computed(() => (isMobile.value ? 5 : 7));
const sessionWorkspaceForbidden = computed(
  () => forbidden.value || reconciliationRequired.value,
);
const selectedFamilyId = computed(() => selectedFamily.value?.familyId ?? '');

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

function sessionStateLabel(state: AdminSessionFamilyState | undefined): string {
  switch (state) {
    case 1: {
      return 'Active';
    }
    case 2: {
      return 'Expired';
    }
    case 3: {
      return 'Revoked';
    }
    default: {
      return 'Unknown';
    }
  }
}

function sessionStateType(
  state: AdminSessionFamilyState | undefined,
): 'danger' | 'info' | 'success' | 'warning' {
  switch (state) {
    case 1: {
      return 'success';
    }
    case 2: {
      return 'warning';
    }
    case 3: {
      return 'info';
    }
    default: {
      return 'danger';
    }
  }
}

function revocationReasonLabel(reason: number | undefined): string {
  const labels: Record<number, string> = {
    1: 'Rotated',
    2: 'Logged out',
    3: 'Reuse detected',
    4: 'Expired',
    5: 'Administrator inactive',
    6: 'Password reset',
    7: 'Revoked by administrator',
  };

  return reason ? (labels[reason] ?? 'Unknown') : 'Not revoked';
}

function administratorLabel(administrator: AdministratorDto): string {
  const userName = administrator.userName ?? administrator.id ?? 'Unknown';
  const displayName = administrator.displayName?.trim();
  return displayName ? `${userName} (${displayName})` : userName;
}

function clearSessionResults() {
  sessionFamilies.value = [];
  total.value = 0;
}

function invalidateSessionList() {
  sessionListSequence += 1;
  sessionListController?.abort();
  sessionListController = null;
  loading.value = false;
  errorMessage.value = '';
  clearSessionResults();
}

function clearAdministratorOptions() {
  administratorOptionsSequence += 1;
  administratorOptionsController?.abort();
  administratorOptionsController = null;
  administratorOptions.value = [];
}

function closeSessionInteractions() {
  revokeDialogOpen.value = false;
  revokeError.value = '';
  selectedFamily.value = null;
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
    closeSessionInteractions();
    clearAdministratorOptions();
    sessionListSequence += 1;
    sessionListController?.abort();
    sessionListController = null;
    loading.value = false;
    errorMessage.value = '';
    reconciliationError.value = '';
    reconciliationRequired.value = true;
    reconcilingPermissions.value = true;
    forbidden.value = true;
    clearSessionResults();

    try {
      const refreshed = await authStore.refreshAuthenticatedState();
      if (!refreshed) {
        return;
      }

      if (!canReadSessions.value) {
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
      if (canReadAdministrators.value) {
        void loadAdministratorOptions();
      }
      await loadSessionFamilies();
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

function recordError(error: unknown, target: { value: string } = errorMessage) {
  if (isNovaGameRequestCancelled(error)) {
    return;
  }
  const apiError = toNovaGameApiError(error);
  if (apiError.status === 403) {
    reconciliationRequired.value = true;
    forbidden.value = true;
    void refreshPermissionsAfterForbidden();
    return;
  }
  target.value = apiError.message;
}

function normalizeAdministratorFilter(): null | string | undefined {
  const administratorId = query.administratorId.trim();
  if (!administratorId) {
    administratorFilterError.value = '';
    return undefined;
  }
  if (!guidPattern.test(administratorId)) {
    administratorFilterError.value = 'Administrator ID must be an exact GUID.';
    return null;
  }
  administratorFilterError.value = '';
  return administratorId;
}

async function loadSessionFamilies() {
  if (!canReadSessionWorkspace.value) {
    clearSessionResults();
    forbidden.value = true;
    return;
  }

  const administratorId = normalizeAdministratorFilter();
  if (administratorId === null) {
    invalidateSessionList();
    return;
  }

  const sequence = ++sessionListSequence;
  sessionListController?.abort();
  const controller = new AbortController();
  sessionListController = controller;
  copyMessage.value = '';
  errorMessage.value = '';
  forbidden.value = false;
  loading.value = true;

  try {
    const response = await listSessionFamilies(
      {
        AdministratorId: administratorId,
        Page: query.page,
        PageSize: query.pageSize,
        State: query.state,
      },
      { signal: controller.signal },
    );
    if (sequence !== sessionListSequence || !canReadSessionWorkspace.value) {
      return;
    }
    sessionFamilies.value = response.data.items ?? [];
    total.value = response.data.totalCount ?? 0;
  } catch (error) {
    if (sequence === sessionListSequence) {
      recordError(error);
    }
  } finally {
    if (sequence === sessionListSequence) {
      loading.value = false;
    }
  }
}

async function loadAdministratorOptions(search = '') {
  if (!canReadAdministrators.value || !canReadSessionWorkspace.value) {
    clearAdministratorOptions();
    return;
  }

  const sequence = ++administratorOptionsSequence;
  administratorOptionsController?.abort();
  const controller = new AbortController();
  administratorOptionsController = controller;
  try {
    const response = await listAdministrators(
      {
        Page: 1,
        PageSize: 100,
        Search: search.trim() || undefined,
      },
      { signal: controller.signal },
    );
    if (
      sequence !== administratorOptionsSequence ||
      !canReadAdministrators.value ||
      !canReadSessionWorkspace.value
    ) {
      return;
    }
    administratorOptions.value = response.data.items ?? [];
  } catch (error) {
    if (sequence === administratorOptionsSequence) {
      recordError(error);
    }
  }
}

function applyFilters() {
  query.page = 1;
  void loadSessionFamilies();
}

function changePage(page: number) {
  query.page = page;
  void loadSessionFamilies();
}

function changePageSize(pageSize: number) {
  query.page = 1;
  query.pageSize = pageSize;
  void loadSessionFamilies();
}

function openRevokeDialog(family: AdminSessionFamilyDto) {
  if (!canRevokeSessionWorkspace.value || !family.familyId) {
    return;
  }
  selectedFamily.value = family;
  revokeError.value = '';
  revokeDialogOpen.value = true;
}

function closeRevokeDialog() {
  if (revoking.value) {
    return;
  }
  closeSessionInteractions();
}

async function submitRevocation() {
  const familyId = selectedFamilyId.value;
  if (!familyId || !canRevokeSessionWorkspace.value || revoking.value) {
    return;
  }

  revoking.value = true;
  revokeError.value = '';
  try {
    await revokeSessionFamily(familyId);
    closeSessionInteractions();
    await loadSessionFamilies();
  } catch (error) {
    if (isNovaGameRequestCancelled(error)) {
      return;
    }
    const apiError = toNovaGameApiError(error);
    if (apiError.status === 403) {
      reconciliationRequired.value = true;
      forbidden.value = true;
      closeSessionInteractions();
      void refreshPermissionsAfterForbidden();
      return;
    }
    if (
      apiError.status === 404 &&
      apiError.code === AdminAccessErrorCode.SessionFamilyNotFound
    ) {
      closeSessionInteractions();
      await loadSessionFamilies();
      return;
    }
    revokeError.value = apiError.message;
  } finally {
    revoking.value = false;
  }
}

async function copyIdentifier(label: string, value: null | string | undefined) {
  if (!value) {
    return;
  }

  if (!navigator.clipboard?.writeText) {
    copyMessage.value = `${label} could not be copied.`;
    copyMessageType.value = 'error';
    return;
  }

  try {
    await navigator.clipboard.writeText(value);
    copyMessage.value = `${label} copied.`;
    copyMessageType.value = 'success';
  } catch {
    copyMessage.value = `${label} could not be copied.`;
    copyMessageType.value = 'error';
  }
}

watch(canReadSessions, (canRead) => {
  if (canRead) {
    return;
  }
  sessionListSequence += 1;
  sessionListController?.abort();
  sessionListController = null;
  loading.value = false;
  closeSessionInteractions();
  clearSessionResults();
  forbidden.value = true;
});

watch(canReadAdministrators, (canRead) => {
  if (!canRead) {
    clearAdministratorOptions();
    return;
  }
  if (canReadSessionWorkspace.value) {
    void loadAdministratorOptions();
  }
});

watch(canRevokeSessionWorkspace, (canRevoke) => {
  if (!canRevoke) {
    closeSessionInteractions();
  }
});

onMounted(() => {
  void loadSessionFamilies();
  if (canReadAdministrators.value) {
    void loadAdministratorOptions();
  }
});

onBeforeUnmount(() => {
  sessionListSequence += 1;
  sessionListController?.abort();
  clearAdministratorOptions();
  closeSessionInteractions();
});
</script>

<template>
  <div class="session-workspace">
    <header class="workspace-header">
      <div>
        <h1>Sessions</h1>
        <p>
          Inspect administrator refresh-session families and revoke active
          access.
        </p>
      </div>
      <ElTooltip content="Refresh sessions" placement="bottom">
        <ElButton
          :disabled="sessionWorkspaceForbidden"
          :loading="loading || reconcilingPermissions"
          aria-label="Refresh sessions"
          circle
          data-testid="refresh-sessions"
          @click="loadSessionFamilies"
        >
          <RotateCw :size="16" />
        </ElButton>
      </ElTooltip>
    </header>

    <main class="workspace-body">
      <ElResult
        v-if="sessionWorkspaceForbidden"
        data-testid="sessions-forbidden"
        sub-title="Your account does not have access to administrator sessions."
        title="Forbidden"
      >
        <template #extra>
          <div class="forbidden-actions">
            <ElAlert
              v-if="reconciliationError"
              :closable="false"
              :title="reconciliationError"
              show-icon
              type="error"
            />
            <ElButton
              :loading="reconcilingPermissions"
              data-testid="retry-session-permissions"
              @click="refreshPermissionsAfterForbidden"
            >
              Retry
            </ElButton>
          </div>
        </template>
      </ElResult>

      <template v-else>
        <section class="table-toolbar" aria-label="Session filters">
          <div class="filter-row">
            <ElSelect
              v-if="canReadAdministrators"
              v-model="query.administratorId"
              aria-label="Administrator"
              clearable
              filterable
              remote
              reserve-keyword
              data-testid="session-administrator-filter"
              placeholder="All administrators"
              :remote-method="loadAdministratorOptions"
              @change="applyFilters"
            >
              <ElOption
                v-for="administrator in administratorOptions"
                :key="administrator.id"
                :label="administratorLabel(administrator)"
                :value="administrator.id ?? ''"
              />
            </ElSelect>
            <ElInput
              v-else
              v-model="query.administratorId"
              :aria-describedby="
                administratorFilterError
                  ? 'session-administrator-filter-error'
                  : undefined
              "
              :aria-invalid="Boolean(administratorFilterError)"
              aria-label="Administrator ID"
              clearable
              data-testid="session-administrator-id-filter"
              placeholder="Exact administrator GUID"
              @clear="applyFilters"
              @keyup.enter="applyFilters"
            />
            <ElSelect
              v-model="query.state"
              aria-label="Session state"
              clearable
              data-testid="session-state-filter"
              placeholder="All session states"
              @change="applyFilters"
            >
              <ElOption label="Active" :value="1" />
              <ElOption label="Expired" :value="2" />
              <ElOption label="Revoked" :value="3" />
            </ElSelect>
            <ElButton data-testid="search-sessions" @click="applyFilters">
              Search
            </ElButton>
          </div>
          <p
            v-if="administratorFilterError"
            id="session-administrator-filter-error"
            class="field-error"
            role="alert"
          >
            {{ administratorFilterError }}
          </p>
        </section>

        <ElAlert
          v-if="copyMessage"
          :closable="true"
          :title="copyMessage"
          :type="copyMessageType"
          @close="copyMessage = ''"
        />

        <ElAlert
          v-if="errorMessage"
          :closable="false"
          :title="errorMessage"
          show-icon
          type="error"
        >
          <template #default>
            <ElButton size="small" @click="loadSessionFamilies">Retry</ElButton>
          </template>
        </ElAlert>

        <div class="table-shell">
          <ElTable
            v-loading="loading"
            :data="sessionFamilies"
            data-testid="session-table"
            row-key="familyId"
            stripe
          >
            <ElTableColumn label="Administrator" min-width="220">
              <template #default="scope">
                <div class="primary-cell">
                  <strong>{{
                    scope.row.userName ?? 'Unknown administrator'
                  }}</strong>
                  <span>{{ scope.row.displayName ?? 'No display name' }}</span>
                  <div class="identifier-row">
                    <code>{{ scope.row.administratorId }}</code>
                    <ElTooltip content="Copy administrator ID" placement="top">
                      <ElButton
                        :aria-label="`Copy administrator ID ${scope.row.administratorId}`"
                        circle
                        size="small"
                        @click="
                          copyIdentifier(
                            'Administrator ID',
                            scope.row.administratorId,
                          )
                        "
                      >
                        <Copy :size="14" />
                      </ElButton>
                    </ElTooltip>
                  </div>
                </div>
              </template>
            </ElTableColumn>
            <ElTableColumn label="Family" min-width="220">
              <template #default="scope">
                <div class="identifier-row">
                  <code>{{ scope.row.familyId }}</code>
                  <ElTooltip content="Copy family ID" placement="top">
                    <ElButton
                      :aria-label="`Copy family ID ${scope.row.familyId}`"
                      circle
                      size="small"
                      @click="copyIdentifier('Family ID', scope.row.familyId)"
                    >
                      <Copy :size="14" />
                    </ElButton>
                  </ElTooltip>
                </div>
              </template>
            </ElTableColumn>
            <ElTableColumn label="State" width="112">
              <template #default="scope">
                <ElTag :type="sessionStateType(scope.row.state)" size="small">
                  {{ sessionStateLabel(scope.row.state) }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn label="First created (UTC)" min-width="176">
              <template #default="scope">
                {{ formatDate(scope.row.firstCreatedAtUtc) }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="Last activity (UTC)" min-width="176">
              <template #default="scope">
                {{ formatDate(scope.row.lastActivityAtUtc) }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="Expires (UTC)" min-width="176">
              <template #default="scope">
                {{ formatDate(scope.row.expiresAtUtc) }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="Last IP" min-width="140">
              <template #default="scope">
                {{ scope.row.lastIpAddress ?? 'Unavailable' }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="Revoked (UTC)" min-width="176">
              <template #default="scope">
                {{ formatDate(scope.row.revokedAtUtc) }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="Reason" min-width="156">
              <template #default="scope">
                {{ revocationReasonLabel(scope.row.revocationReason) }}
              </template>
            </ElTableColumn>
            <ElTableColumn
              align="right"
              fixed="right"
              label="Actions"
              :width="isMobile ? 92 : 116"
            >
              <template #default="scope">
                <ElDropdown
                  v-if="isMobile && canRevokeSessionWorkspace"
                  trigger="click"
                >
                  <ElButton
                    :aria-label="`Actions for session ${scope.row.familyId}`"
                    circle
                    data-testid="session-mobile-actions"
                    title="Session actions"
                  >
                    <Ellipsis :size="17" />
                  </ElButton>
                  <template #dropdown>
                    <ElDropdownMenu>
                      <ElDropdownItem @click="openRevokeDialog(scope.row)">
                        Revoke session
                      </ElDropdownItem>
                    </ElDropdownMenu>
                  </template>
                </ElDropdown>
                <ElButton
                  v-else-if="canRevokeSessionWorkspace"
                  data-testid="revoke-session"
                  size="small"
                  type="danger"
                  @click="openRevokeDialog(scope.row)"
                >
                  Revoke
                </ElButton>
                <span v-else class="read-only-state">Read only</span>
              </template>
            </ElTableColumn>
          </ElTable>
          <ElEmpty
            v-if="!loading && sessionFamilies.length === 0 && !errorMessage"
            description="No session families match the current filters."
          />
        </div>

        <ElPagination
          v-if="total > 0"
          :current-page="query.page"
          :layout="paginationLayout"
          :page-size="query.pageSize"
          :pager-count="paginationPagerCount"
          :page-sizes="[10, 20, 50, 100]"
          :total="total"
          background
          class="workspace-pagination"
          @current-change="changePage"
          @size-change="changePageSize"
        />
      </template>
    </main>

    <ElDialog
      :close-on-click-modal="!revoking"
      :close-on-press-escape="!revoking"
      :model-value="revokeDialogOpen"
      :show-close="!revoking"
      destroy-on-close
      title="Revoke session family"
      width="min(480px, calc(100vw - 32px))"
      @close="closeRevokeDialog"
    >
      <p>
        Revoke the session family for
        <strong class="dialog-subject" data-testid="session-revoke-subject">
          {{ selectedFamily?.userName ?? 'this administrator' }}
        </strong>
        ?
      </p>
      <code class="dialog-identifier">{{ selectedFamilyId }}</code>
      <ElAlert
        v-if="revokeError"
        :closable="false"
        :title="revokeError"
        show-icon
        type="error"
      >
        <template #default>
          <ElButton
            :loading="revoking"
            data-testid="retry-revoke-session"
            size="small"
            @click="submitRevocation"
          >
            Retry
          </ElButton>
        </template>
      </ElAlert>
      <template #footer>
        <div class="dialog-footer">
          <ElButton :disabled="revoking" @click="closeRevokeDialog">
            Cancel
          </ElButton>
          <ElButton
            :disabled="!canRevokeSessionWorkspace || !selectedFamilyId"
            :loading="revoking"
            data-testid="confirm-revoke-session"
            type="danger"
            @click="submitRevocation"
          >
            Revoke session
          </ElButton>
        </div>
      </template>
    </ElDialog>
  </div>
</template>

<style scoped>
.session-workspace {
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

.workspace-header p {
  margin: 4px 0 0;
  color: var(--el-text-color-secondary);
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

.filter-row,
.identifier-row,
.dialog-footer,
.forbidden-actions {
  display: flex;
  gap: 8px;
  align-items: center;
}

.filter-row {
  flex-wrap: wrap;
}

.filter-row :deep(.el-input),
.filter-row :deep(.el-select) {
  width: min(280px, 100%);
}

.field-error {
  margin: 8px 0 0;
  font-size: 12px;
  color: var(--el-color-danger);
}

.table-shell {
  overflow: auto;
  border: 1px solid var(--el-border-color-light);
}

.primary-cell {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.primary-cell > span {
  overflow: hidden;
  text-overflow: ellipsis;
  color: var(--el-text-color-secondary);
  white-space: nowrap;
}

.identifier-row {
  min-width: 0;
}

.identifier-row code,
.dialog-identifier {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 11px;
  color: var(--el-text-color-secondary);
  white-space: nowrap;
}

.identifier-row :deep(.el-button) {
  flex: 0 0 auto;
}

.read-only-state {
  display: inline-block;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  white-space: nowrap;
}

.workspace-pagination {
  justify-content: flex-end;
}

.forbidden-actions {
  flex-direction: column;
  align-items: center;
}

.dialog-identifier {
  display: block;
  max-width: 100%;
  margin-bottom: 16px;
}

.dialog-subject {
  min-width: 0;
  overflow-wrap: anywhere;
}

.dialog-footer {
  justify-content: flex-end;
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

  .filter-row :deep(.el-button),
  .dialog-footer :deep(.el-button) {
    min-height: 44px;
    margin-left: 0;
  }

  .dialog-footer {
    display: grid;
    grid-template-columns: 1fr 1fr;
  }
}
</style>
