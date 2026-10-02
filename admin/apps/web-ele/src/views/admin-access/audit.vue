<script lang="ts" setup>
import type { RouteRecordRaw } from 'vue-router';

import type {
  AdminAccessAuditActionDto,
  AdminAccessAuditRecordDto,
  AdministratorDto,
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

import { Copy, Ellipsis, RotateCw } from '@vben/icons';
import { useAccessStore } from '@vben/stores';

import { useMediaQuery } from '@vueuse/core';
import {
  ElAlert,
  ElButton,
  ElDrawer,
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
  listAuditRecords,
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

defineOptions({ name: 'AdministratorAccessAudit' });

const guidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const canonicalUtcPattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/;
const maximumAuditRangeMilliseconds = 31 * 24 * 60 * 60 * 1000;
const initialNowUtc = new Date();

const accessStore = useAccessStore();
const authStore = useAuthStore();
const router = useRouter();
const isMobile = useMediaQuery('(max-width: 640px)');

const administratorOptions = ref<AdministratorDto[]>([]);
const auditRecords = ref<AdminAccessAuditRecordDto[]>([]);
const copyMessage = useOperationsMessage();
const copyMessageType = ref<'error' | 'success'>('success');
const errorMessage = useOperationsMessage();
const filterErrors = reactive<Record<string, string>>({});
const forbidden = ref(false);
const loading = ref(false);
const reconciliationError = useOperationsMessage();
const reconciliationRequired = ref(false);
const reconcilingPermissions = ref(false);
const roleOptions = ref<AdminRoleSummaryDto[]>([]);
const selectedRecord = ref<AdminAccessAuditRecordDto | null>(null);
const detailDrawerOpen = ref(false);
const total = ref(0);
const query = reactive({
  action: undefined as AdminAccessAuditActionDto | undefined,
  actorAdministratorId: '',
  correlationId: '',
  fromUtc: new Date(
    initialNowUtc.getTime() - 7 * 24 * 60 * 60 * 1000,
  ).toISOString(),
  page: 1,
  pageSize: 20,
  roleId: '',
  subjectAdministratorId: '',
  succeeded: undefined as boolean | undefined,
  toUtc: initialNowUtc.toISOString(),
});
let administratorOptionsController: AbortController | null = null;
let administratorOptionsSequence = 0;
let auditListController: AbortController | null = null;
let auditListSequence = 0;
let permissionRefreshPromise: null | Promise<void> = null;
let roleOptionsController: AbortController | null = null;
let roleOptionsSequence = 0;

const auditActions = computed<
  ReadonlyArray<{
    label: string;
    value: AdminAccessAuditActionDto;
  }>
>(() => [
  { label: tOperations('login'), value: 1 },
  { label: tOperations('refresh'), value: 2 },
  { label: tOperations('logout'), value: 3 },
  { label: tOperations('refreshTokenReuseDetected'), value: 4 },
  { label: tOperations('administratorCreated'), value: 5 },
  { label: tOperations('roleCreated'), value: 6 },
  { label: tOperations('roleAssigned'), value: 7 },
  { label: tOperations('permissionGranted'), value: 8 },
  { label: tOperations('permissionRevoked'), value: 9 },
  { label: tOperations('passwordRehashed'), value: 10 },
  { label: tOperations('administratorDisplayNameChanged'), value: 11 },
  { label: tOperations('administratorActivated'), value: 12 },
  { label: tOperations('administratorDeactivated'), value: 13 },
  { label: tOperations('administratorPasswordReset'), value: 14 },
  { label: tOperations('roleRemoved'), value: 15 },
  { label: tOperations('roleRenamed'), value: 16 },
  { label: tOperations('roleActivated'), value: 17 },
  { label: tOperations('roleDeactivated'), value: 18 },
  { label: tOperations('sessionFamilyRevoked'), value: 19 },
]);

const canReadAdministrators = computed(() =>
  hasAdminAccessPermission(
    accessStore.accessCodes,
    AdminAccessPermission.AdministratorsRead,
  ),
);
const canReadAudit = computed(() =>
  hasAdminAccessPermission(
    accessStore.accessCodes,
    AdminAccessPermission.AuditLogsRead,
  ),
);
const canReadRoles = computed(() =>
  hasAdminAccessPermission(
    accessStore.accessCodes,
    AdminAccessPermission.RolesRead,
  ),
);
const canReadAuditWorkspace = computed(
  () => canReadAudit.value && !reconciliationRequired.value,
);
const auditWorkspaceForbidden = computed(
  () => forbidden.value || reconciliationRequired.value,
);
const paginationLayout = computed(() =>
  isMobile.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next',
);
const paginationPagerCount = computed(() => (isMobile.value ? 5 : 7));

function formatDate(value: null | string | undefined): string {
  if (!value) {
    return tOperations('unavailable');
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return tOperations('unavailable');
  }

  return date.toISOString().replace('T', ' ').replace('Z', ' UTC');
}

function actionLabel(action: AdminAccessAuditActionDto | undefined): string {
  return (
    auditActions.value.find((entry) => entry.value === action)?.label ??
    tOperations('unknown')
  );
}

function administratorLabel(administrator: AdministratorDto): string {
  const userName =
    administrator.userName ?? administrator.id ?? tOperations('unknown');
  const displayName = administrator.displayName?.trim();
  return displayName ? `${userName} (${displayName})` : userName;
}

function roleLabel(role: AdminRoleSummaryDto): string {
  const name = role.name ?? role.id ?? tOperations('unknownRole');
  return role.isActive === false
    ? tOperations('inactiveName', { value0: name })
    : name;
}

function clearAuditResults() {
  auditRecords.value = [];
  total.value = 0;
}

function invalidateAuditList() {
  auditListSequence += 1;
  auditListController?.abort();
  auditListController = null;
  loading.value = false;
  errorMessage.value = '';
  closeAuditInteractions();
  clearAuditResults();
}

function clearAdministratorOptions() {
  administratorOptionsSequence += 1;
  administratorOptionsController?.abort();
  administratorOptionsController = null;
  administratorOptions.value = [];
}

function clearRoleOptions() {
  roleOptionsSequence += 1;
  roleOptionsController?.abort();
  roleOptionsController = null;
  roleOptions.value = [];
}

function closeAuditInteractions() {
  detailDrawerOpen.value = false;
  selectedRecord.value = null;
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
    closeAuditInteractions();
    clearAdministratorOptions();
    clearRoleOptions();
    auditListSequence += 1;
    auditListController?.abort();
    auditListController = null;
    loading.value = false;
    errorMessage.value = '';
    reconciliationError.value = '';
    reconciliationRequired.value = true;
    reconcilingPermissions.value = true;
    forbidden.value = true;
    clearAuditResults();

    try {
      const refreshed = await authStore.refreshAuthenticatedState();
      if (!refreshed) {
        return;
      }

      if (!canReadAudit.value) {
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
      if (canReadRoles.value) {
        void loadRoleOptions();
      }
      await loadAuditRecords();
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

function recordError(error: unknown) {
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
  errorMessage.value = () => operationsErrorMessage(apiError);
}

function normalizedGuid(
  value: string,
  label: string,
): null | string | undefined {
  const normalized = value.trim();
  if (!normalized) {
    delete filterErrors[label];
    return undefined;
  }
  if (!guidPattern.test(normalized)) {
    const labels = {
      'Actor administrator ID': tOperations('actorAdministratorId'),
      'Subject administrator ID': tOperations('subjectAdministratorId'),
      'Role ID': tOperations('roleId'),
    };
    filterErrors[label] = tOperations('exactGuidRequired', {
      value0: labels[label as keyof typeof labels] ?? label,
    });
    return null;
  }
  delete filterErrors[label];
  return normalized;
}

function parseCanonicalUtc(value: string): Date | null {
  if (!canonicalUtcPattern.test(value)) {
    return null;
  }
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime()) || parsed.toISOString() !== value) {
    return null;
  }
  return parsed;
}

function validateAuditRange(): null | { from: Date; to: Date } {
  const from = parseCanonicalUtc(query.fromUtc);
  const to = parseCanonicalUtc(query.toUtc);
  if (!from || !to || from.getTime() >= to.getTime()) {
    filterErrors.range = tOperations(
      'useCanonicalUtcIsoValuesWithFromBeforeTo',
    );
    return null;
  }
  if (to.getTime() - from.getTime() > maximumAuditRangeMilliseconds) {
    filterErrors.range = tOperations('theAuditRangeCannotExceed31Days');
    return null;
  }
  delete filterErrors.range;
  return { from, to };
}

function validateAuditQuery(): null | {
  actorAdministratorId: string | undefined;
  fromUtc: string;
  roleId: string | undefined;
  subjectAdministratorId: string | undefined;
  toUtc: string;
} {
  const range = validateAuditRange();
  if (!range) {
    return null;
  }

  const actorAdministratorId = normalizedGuid(
    query.actorAdministratorId,
    'Actor administrator ID',
  );
  const subjectAdministratorId = normalizedGuid(
    query.subjectAdministratorId,
    'Subject administrator ID',
  );
  const roleId = normalizedGuid(query.roleId, 'Role ID');
  if (
    actorAdministratorId === null ||
    subjectAdministratorId === null ||
    roleId === null
  ) {
    return null;
  }

  return {
    actorAdministratorId,
    fromUtc: range.from.toISOString(),
    roleId,
    subjectAdministratorId,
    toUtc: range.to.toISOString(),
  };
}

async function loadAuditRecords() {
  if (!canReadAuditWorkspace.value) {
    clearAuditResults();
    forbidden.value = true;
    return;
  }

  const validated = validateAuditQuery();
  if (!validated) {
    invalidateAuditList();
    return;
  }

  const sequence = ++auditListSequence;
  auditListController?.abort();
  const controller = new AbortController();
  auditListController = controller;
  copyMessage.value = '';
  errorMessage.value = '';
  forbidden.value = false;
  loading.value = true;

  try {
    const response = await listAuditRecords(
      {
        Action: query.action,
        ActorAdministratorId: validated.actorAdministratorId,
        CorrelationId: query.correlationId || undefined,
        FromUtc: validated.fromUtc,
        Page: query.page,
        PageSize: query.pageSize,
        RoleId: validated.roleId,
        SubjectAdministratorId: validated.subjectAdministratorId,
        Succeeded: query.succeeded,
        ToUtc: validated.toUtc,
      },
      { signal: controller.signal },
    );
    if (sequence !== auditListSequence || !canReadAuditWorkspace.value) {
      return;
    }
    auditRecords.value = response.data.items ?? [];
    total.value = response.data.totalCount ?? 0;
  } catch (error) {
    if (sequence === auditListSequence) {
      recordError(error);
    }
  } finally {
    if (sequence === auditListSequence) {
      loading.value = false;
    }
  }
}

async function loadAdministratorOptions(search = '') {
  if (!canReadAdministrators.value || !canReadAuditWorkspace.value) {
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
      !canReadAuditWorkspace.value
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

async function loadRoleOptions(search = '') {
  if (!canReadRoles.value || !canReadAuditWorkspace.value) {
    clearRoleOptions();
    return;
  }

  const sequence = ++roleOptionsSequence;
  roleOptionsController?.abort();
  const controller = new AbortController();
  roleOptionsController = controller;
  try {
    const response = await listAdminRoles(
      {
        Page: 1,
        PageSize: 100,
        Search: search.trim() || undefined,
      },
      { signal: controller.signal },
    );
    if (
      sequence !== roleOptionsSequence ||
      !canReadRoles.value ||
      !canReadAuditWorkspace.value
    ) {
      return;
    }
    roleOptions.value = response.data.items ?? [];
  } catch (error) {
    if (sequence === roleOptionsSequence) {
      recordError(error);
    }
  }
}

function applyFilters() {
  query.page = 1;
  void loadAuditRecords();
}

function changePage(page: number) {
  query.page = page;
  void loadAuditRecords();
}

function changePageSize(pageSize: number) {
  query.page = 1;
  query.pageSize = pageSize;
  void loadAuditRecords();
}

function openRecordDetail(record: AdminAccessAuditRecordDto) {
  selectedRecord.value = record;
  detailDrawerOpen.value = true;
}

function closeRecordDetail() {
  detailDrawerOpen.value = false;
  selectedRecord.value = null;
}

async function copyIdentifier(label: string, value: null | string | undefined) {
  if (!value) {
    return;
  }

  if (!navigator.clipboard?.writeText) {
    copyMessage.value = () => tOperations('copyFailed', { value0: label });
    copyMessageType.value = 'error';
    return;
  }

  try {
    await navigator.clipboard.writeText(value);
    copyMessage.value = () => tOperations('copied', { value0: label });
    copyMessageType.value = 'success';
  } catch {
    copyMessage.value = () => tOperations('copyFailed', { value0: label });
    copyMessageType.value = 'error';
  }
}

watch(canReadAudit, (canRead) => {
  if (canRead) {
    return;
  }
  auditListSequence += 1;
  auditListController?.abort();
  auditListController = null;
  loading.value = false;
  closeAuditInteractions();
  clearAuditResults();
  forbidden.value = true;
});

watch(canReadAdministrators, (canRead) => {
  if (!canRead) {
    clearAdministratorOptions();
    return;
  }
  if (canReadAuditWorkspace.value) {
    void loadAdministratorOptions();
  }
});

watch(canReadRoles, (canRead) => {
  if (!canRead) {
    clearRoleOptions();
    return;
  }
  if (canReadAuditWorkspace.value) {
    void loadRoleOptions();
  }
});

onMounted(() => {
  void loadAuditRecords();
  if (canReadAdministrators.value) {
    void loadAdministratorOptions();
  }
  if (canReadRoles.value) {
    void loadRoleOptions();
  }
});

onBeforeUnmount(() => {
  auditListSequence += 1;
  auditListController?.abort();
  clearAdministratorOptions();
  clearRoleOptions();
  closeAuditInteractions();
});
</script>

<template>
  <div class="audit-workspace">
    <header class="workspace-header">
      <div>
        <h1>{{ tOperations('auditHistory') }}</h1>
        <p>
          {{
            tOperations('reviewRedactedAdministratorAccessEventsInServerOrder')
          }}
        </p>
      </div>
      <ElTooltip
        :content="tOperations('refreshAuditHistory')"
        placement="bottom"
      >
        <ElButton
          :disabled="auditWorkspaceForbidden"
          :loading="loading || reconcilingPermissions"
          :aria-label="tOperations('refreshAuditHistory')"
          circle
          data-testid="refresh-audit"
          @click="loadAuditRecords"
        >
          <RotateCw :size="16" />
        </ElButton>
      </ElTooltip>
    </header>

    <main class="workspace-body">
      <ElResult
        v-if="auditWorkspaceForbidden"
        data-testid="audit-forbidden"
        :sub-title="
          tOperations('yourAccountDoesNotHaveAccessToAdministratorAuditHistory')
        "
        :title="tOperations('forbidden')"
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
              data-testid="retry-audit-permissions"
              @click="refreshPermissionsAfterForbidden"
            >
              {{ tOperations('retry') }}
            </ElButton>
          </div>
        </template>
      </ElResult>

      <template v-else>
        <section
          class="table-toolbar"
          :aria-label="tOperations('auditHistoryFilters')"
        >
          <div class="filter-row">
            <ElInput
              v-model="query.fromUtc"
              :aria-describedby="
                filterErrors.range ? 'audit-range-filter-error' : undefined
              "
              :aria-invalid="Boolean(filterErrors.range)"
              :aria-label="tOperations('fromUtc')"
              clearable
              data-testid="audit-from-utc"
              :placeholder="tOperations('fromUtcYyyyMmDdthhMmSsSssz')"
              @clear="applyFilters"
              @input="validateAuditRange"
              @keyup.enter="applyFilters"
            />
            <ElInput
              v-model="query.toUtc"
              :aria-describedby="
                filterErrors.range ? 'audit-range-filter-error' : undefined
              "
              :aria-invalid="Boolean(filterErrors.range)"
              :aria-label="tOperations('toUtc')"
              clearable
              data-testid="audit-to-utc"
              :placeholder="tOperations('toUtcYyyyMmDdthhMmSsSssz')"
              @clear="applyFilters"
              @input="validateAuditRange"
              @keyup.enter="applyFilters"
            />
            <ElSelect
              v-model="query.action"
              :aria-label="tOperations('auditAction')"
              clearable
              data-testid="audit-action-filter"
              :placeholder="tOperations('allActions')"
              @change="applyFilters"
            >
              <ElOption
                v-for="action in auditActions"
                :key="action.value"
                :label="action.label"
                :value="action.value"
              />
            </ElSelect>
            <ElSelect
              v-model="query.succeeded"
              :aria-label="tOperations('auditOutcome')"
              clearable
              data-testid="audit-success-filter"
              :placeholder="tOperations('allOutcomes')"
              @change="applyFilters"
            >
              <ElOption :label="tOperations('succeeded')" :value="true" />
              <ElOption :label="tOperations('failed')" :value="false" />
            </ElSelect>
            <ElSelect
              v-if="canReadAdministrators"
              v-model="query.actorAdministratorId"
              :aria-label="tOperations('actorAdministrator')"
              clearable
              filterable
              remote
              reserve-keyword
              data-testid="audit-actor-filter"
              :placeholder="tOperations('allActors')"
              :remote-method="loadAdministratorOptions"
              @change="applyFilters"
            >
              <ElOption
                v-for="administrator in administratorOptions"
                :key="`actor-${administrator.id}`"
                :label="administratorLabel(administrator)"
                :value="administrator.id ?? ''"
              />
            </ElSelect>
            <ElInput
              v-else
              v-model="query.actorAdministratorId"
              :aria-describedby="
                filterErrors['Actor administrator ID']
                  ? 'audit-actor-administrator-filter-error'
                  : undefined
              "
              :aria-invalid="Boolean(filterErrors['Actor administrator ID'])"
              :aria-label="tOperations('actorAdministratorId')"
              clearable
              data-testid="audit-actor-id-filter"
              :placeholder="tOperations('exactActorAdministratorGuid')"
              @clear="applyFilters"
              @keyup.enter="applyFilters"
            />
            <ElSelect
              v-if="canReadAdministrators"
              v-model="query.subjectAdministratorId"
              :aria-label="tOperations('subjectAdministrator')"
              clearable
              filterable
              remote
              reserve-keyword
              data-testid="audit-subject-filter"
              :placeholder="tOperations('allSubjects')"
              :remote-method="loadAdministratorOptions"
              @change="applyFilters"
            >
              <ElOption
                v-for="administrator in administratorOptions"
                :key="`subject-${administrator.id}`"
                :label="administratorLabel(administrator)"
                :value="administrator.id ?? ''"
              />
            </ElSelect>
            <ElInput
              v-else
              v-model="query.subjectAdministratorId"
              :aria-describedby="
                filterErrors['Subject administrator ID']
                  ? 'audit-subject-administrator-filter-error'
                  : undefined
              "
              :aria-invalid="Boolean(filterErrors['Subject administrator ID'])"
              :aria-label="tOperations('subjectAdministratorId')"
              clearable
              data-testid="audit-subject-id-filter"
              :placeholder="tOperations('exactSubjectAdministratorGuid')"
              @clear="applyFilters"
              @keyup.enter="applyFilters"
            />
            <ElSelect
              v-if="canReadRoles"
              v-model="query.roleId"
              :aria-label="tOperations('role')"
              clearable
              filterable
              remote
              reserve-keyword
              data-testid="audit-role-filter"
              :placeholder="tOperations('allRoles')"
              :remote-method="loadRoleOptions"
              @change="applyFilters"
            >
              <ElOption
                v-for="role in roleOptions"
                :key="role.id"
                :label="roleLabel(role)"
                :value="role.id ?? ''"
              />
            </ElSelect>
            <ElInput
              v-else
              v-model="query.roleId"
              :aria-describedby="
                filterErrors['Role ID'] ? 'audit-role-filter-error' : undefined
              "
              :aria-invalid="Boolean(filterErrors['Role ID'])"
              :aria-label="tOperations('roleId')"
              clearable
              data-testid="audit-role-id-filter"
              :placeholder="tOperations('exactRoleGuid')"
              @clear="applyFilters"
              @keyup.enter="applyFilters"
            />
            <ElInput
              v-model="query.correlationId"
              :aria-label="tOperations('correlationId')"
              clearable
              data-testid="audit-correlation-filter"
              :placeholder="tOperations('exactCorrelationId')"
              @clear="applyFilters"
              @keyup.enter="applyFilters"
            />
            <ElButton data-testid="search-audit" @click="applyFilters">
              {{ tOperations('search') }}
            </ElButton>
          </div>
          <div
            v-if="Object.keys(filterErrors).length > 0"
            class="filter-errors"
          >
            <p
              v-for="(message, field) in filterErrors"
              :id="
                field === 'range'
                  ? 'audit-range-filter-error'
                  : field === 'Actor administrator ID'
                    ? 'audit-actor-administrator-filter-error'
                    : field === 'Subject administrator ID'
                      ? 'audit-subject-administrator-filter-error'
                      : 'audit-role-filter-error'
              "
              :key="field"
              role="alert"
            >
              {{ message }}
            </p>
          </div>
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
            <ElButton size="small" @click="loadAuditRecords">
              {{ tOperations('retry') }}
            </ElButton>
          </template>
        </ElAlert>

        <div class="table-shell">
          <ElTable
            v-loading="loading"
            :data="auditRecords"
            data-testid="audit-table"
            row-key="id"
            stripe
          >
            <ElTableColumn :label="tOperations('occurredUtc')" min-width="176">
              <template #default="scope">
                {{ formatDate(scope.row.occurredAtUtc) }}
              </template>
            </ElTableColumn>
            <ElTableColumn :label="tOperations('action')" min-width="200">
              <template #default="scope">
                {{ actionLabel(scope.row.action) }}
              </template>
            </ElTableColumn>
            <ElTableColumn :label="tOperations('outcome')" width="112">
              <template #default="scope">
                <ElTag
                  :type="scope.row.succeeded === false ? 'danger' : 'success'"
                  size="small"
                >
                  {{
                    scope.row.succeeded === false
                      ? tOperations('failed')
                      : tOperations('succeeded')
                  }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn :label="tOperations('subject')" min-width="210">
              <template #default="scope">
                <div class="primary-cell">
                  <strong>{{
                    scope.row.subjectUserName ??
                    tOperations('noSubjectUserName')
                  }}</strong>
                  <div class="identifier-row">
                    <code>{{
                      scope.row.subjectAdministratorId ??
                      tOperations('noAdministratorId')
                    }}</code>
                    <ElTooltip
                      v-if="scope.row.subjectAdministratorId"
                      :content="tOperations('copySubjectAdministratorId')"
                      placement="top"
                    >
                      <ElButton
                        :aria-label="
                          tOperations('copySubjectAdministratorIdParameter', {
                            value0: scope.row.subjectAdministratorId,
                          })
                        "
                        circle
                        size="small"
                        @click="
                          copyIdentifier(
                            tOperations('subjectAdministratorId'),
                            scope.row.subjectAdministratorId,
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
            <ElTableColumn :label="tOperations('actor')" min-width="190">
              <template #default="scope">
                <div class="identifier-row">
                  <code>{{
                    scope.row.actorAdministratorId ?? tOperations('noActorId')
                  }}</code>
                  <ElTooltip
                    v-if="scope.row.actorAdministratorId"
                    :content="tOperations('copyActorAdministratorId')"
                    placement="top"
                  >
                    <ElButton
                      :aria-label="
                        tOperations('copyActorAdministratorIdParameter', {
                          value0: scope.row.actorAdministratorId,
                        })
                      "
                      circle
                      size="small"
                      @click="
                        copyIdentifier(
                          tOperations('actorAdministratorId'),
                          scope.row.actorAdministratorId,
                        )
                      "
                    >
                      <Copy :size="14" />
                    </ElButton>
                  </ElTooltip>
                </div>
              </template>
            </ElTableColumn>
            <ElTableColumn :label="tOperations('role')" min-width="180">
              <template #default="scope">
                <div class="identifier-row">
                  <code>{{ scope.row.roleId ?? tOperations('noRoleId') }}</code>
                  <ElTooltip
                    v-if="scope.row.roleId"
                    :content="tOperations('copyRoleId')"
                    placement="top"
                  >
                    <ElButton
                      :aria-label="
                        tOperations('copyRoleIdParameter', {
                          value0: scope.row.roleId,
                        })
                      "
                      circle
                      size="small"
                      @click="
                        copyIdentifier(tOperations('roleId'), scope.row.roleId)
                      "
                    >
                      <Copy :size="14" />
                    </ElButton>
                  </ElTooltip>
                </div>
              </template>
            </ElTableColumn>
            <ElTableColumn
              :label="tOperations('sessionFamily')"
              min-width="190"
            >
              <template #default="scope">
                <div class="identifier-row">
                  <code>{{
                    scope.row.sessionFamilyId ?? tOperations('noFamilyId')
                  }}</code>
                  <ElTooltip
                    v-if="scope.row.sessionFamilyId"
                    :content="tOperations('copySessionFamilyId')"
                    placement="top"
                  >
                    <ElButton
                      :aria-label="
                        tOperations('copySessionFamilyIdParameter', {
                          value0: scope.row.sessionFamilyId,
                        })
                      "
                      circle
                      size="small"
                      @click="
                        copyIdentifier(
                          tOperations('sessionFamilyId'),
                          scope.row.sessionFamilyId,
                        )
                      "
                    >
                      <Copy :size="14" />
                    </ElButton>
                  </ElTooltip>
                </div>
              </template>
            </ElTableColumn>
            <ElTableColumn :label="tOperations('permission')" min-width="180">
              <template #default="scope">
                {{ scope.row.permissionName ?? tOperations('none') }}
              </template>
            </ElTableColumn>
            <ElTableColumn :label="tOperations('error')" min-width="180">
              <template #default="scope">
                {{ scope.row.errorCode ?? tOperations('none') }}
              </template>
            </ElTableColumn>
            <ElTableColumn :label="tOperations('correlation')" min-width="190">
              <template #default="scope">
                <div class="identifier-row">
                  <code>{{
                    scope.row.correlationId ?? tOperations('unavailable')
                  }}</code>
                  <ElTooltip
                    v-if="scope.row.correlationId"
                    :content="tOperations('copyCorrelationId')"
                    placement="top"
                  >
                    <ElButton
                      :aria-label="
                        tOperations('copyCorrelationIdParameter', {
                          value0: scope.row.correlationId,
                        })
                      "
                      circle
                      size="small"
                      @click="
                        copyIdentifier(
                          tOperations('correlationId'),
                          scope.row.correlationId,
                        )
                      "
                    >
                      <Copy :size="14" />
                    </ElButton>
                  </ElTooltip>
                </div>
              </template>
            </ElTableColumn>
            <ElTableColumn :label="tOperations('targetRevision')" width="132">
              <template #default="scope">
                {{ scope.row.targetRevision ?? tOperations('none') }}
              </template>
            </ElTableColumn>
            <ElTableColumn
              align="right"
              fixed="right"
              :label="tOperations('details')"
              :width="isMobile ? 68 : 92"
            >
              <template #default="scope">
                <ElDropdown v-if="isMobile" trigger="click">
                  <ElButton
                    :aria-label="
                      tOperations('actionsForAuditRecordParameter', {
                        value0: scope.row.id,
                      })
                    "
                    circle
                    data-testid="audit-mobile-actions"
                    :title="tOperations('auditRecordActions')"
                  >
                    <Ellipsis :size="17" />
                  </ElButton>
                  <template #dropdown>
                    <ElDropdownMenu>
                      <ElDropdownItem @click="openRecordDetail(scope.row)">
                        {{ tOperations('viewDetails') }}
                      </ElDropdownItem>
                    </ElDropdownMenu>
                  </template>
                </ElDropdown>
                <ElButton
                  v-else
                  data-testid="view-audit-record"
                  size="small"
                  @click="openRecordDetail(scope.row)"
                >
                  {{ tOperations('view') }}
                </ElButton>
              </template>
            </ElTableColumn>
          </ElTable>
          <ElEmpty
            v-if="!loading && auditRecords.length === 0 && !errorMessage"
            :description="tOperations('noAuditRecordsMatchTheCurrentFilters')"
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

    <ElDrawer
      :model-value="detailDrawerOpen"
      destroy-on-close
      size="min(620px, 100vw)"
      :title="tOperations('auditRecordDetails')"
      @close="closeRecordDetail"
    >
      <div
        v-if="selectedRecord"
        class="audit-details"
        data-testid="audit-record-drawer"
      >
        <dl>
          <div>
            <dt>{{ tOperations('recordId') }}</dt>
            <dd class="identifier-row">
              <code>{{ selectedRecord.id }}</code>
              <ElTooltip
                :content="tOperations('copyAuditRecordId')"
                placement="top"
              >
                <ElButton
                  :aria-label="
                    tOperations('copyAuditRecordIdParameter', {
                      value0: selectedRecord.id,
                    })
                  "
                  circle
                  size="small"
                  @click="
                    copyIdentifier(
                      tOperations('auditRecordId'),
                      selectedRecord.id,
                    )
                  "
                >
                  <Copy :size="14" />
                </ElButton>
              </ElTooltip>
            </dd>
          </div>
          <div>
            <dt>{{ tOperations('occurredUtc') }}</dt>
            <dd>{{ formatDate(selectedRecord.occurredAtUtc) }}</dd>
          </div>
          <div>
            <dt>{{ tOperations('action') }}</dt>
            <dd>{{ actionLabel(selectedRecord.action) }}</dd>
          </div>
          <div>
            <dt>{{ tOperations('outcome') }}</dt>
            <dd>
              {{
                selectedRecord.succeeded === false
                  ? tOperations('failed')
                  : tOperations('succeeded')
              }}
            </dd>
          </div>
          <div>
            <dt>{{ tOperations('subjectUserName') }}</dt>
            <dd>
              {{ selectedRecord.subjectUserName ?? tOperations('unavailable') }}
            </dd>
          </div>
          <div>
            <dt>{{ tOperations('subjectAdministratorId') }}</dt>
            <dd>
              {{
                selectedRecord.subjectAdministratorId ??
                tOperations('unavailable')
              }}
            </dd>
          </div>
          <div>
            <dt>{{ tOperations('actorAdministratorId') }}</dt>
            <dd>
              {{
                selectedRecord.actorAdministratorId ??
                tOperations('unavailable')
              }}
            </dd>
          </div>
          <div>
            <dt>{{ tOperations('roleId') }}</dt>
            <dd>{{ selectedRecord.roleId ?? tOperations('unavailable') }}</dd>
          </div>
          <div>
            <dt>{{ tOperations('sessionFamilyId') }}</dt>
            <dd>
              {{ selectedRecord.sessionFamilyId ?? tOperations('unavailable') }}
            </dd>
          </div>
          <div>
            <dt>{{ tOperations('permission') }}</dt>
            <dd>
              {{ selectedRecord.permissionName ?? tOperations('unavailable') }}
            </dd>
          </div>
          <div>
            <dt>{{ tOperations('errorCode') }}</dt>
            <dd>{{ selectedRecord.errorCode ?? tOperations('none') }}</dd>
          </div>
          <div>
            <dt>{{ tOperations('ipAddress') }}</dt>
            <dd>
              {{ selectedRecord.ipAddress ?? tOperations('unavailable') }}
            </dd>
          </div>
          <div>
            <dt>{{ tOperations('userAgent') }}</dt>
            <dd>
              {{ selectedRecord.userAgent ?? tOperations('unavailable') }}
            </dd>
          </div>
          <div>
            <dt>{{ tOperations('correlationId') }}</dt>
            <dd>
              {{ selectedRecord.correlationId ?? tOperations('unavailable') }}
            </dd>
          </div>
          <div>
            <dt>{{ tOperations('targetRevision') }}</dt>
            <dd>{{ selectedRecord.targetRevision ?? tOperations('none') }}</dd>
          </div>
        </dl>
      </div>
    </ElDrawer>
  </div>
</template>

<style scoped>
.audit-workspace {
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
  width: min(260px, 100%);
}

.filter-errors {
  margin-top: 8px;
  font-size: 12px;
  color: var(--el-color-danger);
}

.filter-errors p {
  margin: 2px 0;
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

.identifier-row {
  min-width: 0;
}

.identifier-row code {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 11px;
  color: var(--el-text-color-secondary);
  white-space: nowrap;
}

.identifier-row :deep(.el-button) {
  flex: 0 0 auto;
}

.workspace-pagination {
  justify-content: flex-end;
}

.forbidden-actions {
  flex-direction: column;
  align-items: center;
}

.audit-details dl {
  display: grid;
  grid-template-columns: minmax(140px, 0.45fr) minmax(0, 1fr);
  gap: 0;
  margin: 0;
  border-top: 1px solid var(--el-border-color-light);
}

.audit-details dl > div {
  display: contents;
}

.audit-details dt,
.audit-details dd {
  min-width: 0;
  padding: 12px;
  margin: 0;
  border-bottom: 1px solid var(--el-border-color-light);
}

.audit-details dt {
  color: var(--el-text-color-secondary);
  background: var(--el-fill-color-lighter);
}

.audit-details dd {
  overflow-wrap: anywhere;
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

  .audit-details dl {
    grid-template-columns: 1fr;
  }

  .audit-details dt {
    padding-bottom: 4px;
    border-bottom: 0;
  }

  .audit-details dd {
    padding-top: 4px;
  }
}
</style>
