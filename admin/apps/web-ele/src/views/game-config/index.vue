<script lang="ts" setup>
import type {
  GameConfigDraftState as DraftState,
  GameConfigDraft,
  GameConfigDraftSummary,
  GameConfigEnvironment,
  GameConfigVersion,
  GameConfigVersionSummary,
} from '#/api';

import { computed, onMounted, reactive, ref } from 'vue';

import { ArrowRightLeft, Eye, Plus, RotateCw, Undo2 } from '@vben/icons';
import { useAccessStore } from '@vben/stores';

import { useMediaQuery } from '@vueuse/core';
import {
  ElAlert,
  ElButton,
  ElDescriptions,
  ElDescriptionsItem,
  ElDialog,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElOption,
  ElPagination,
  ElResult,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTabPane,
  ElTabs,
  ElTag,
  ElTooltip,
} from 'element-plus';

import {
  activateGameConfigVersion,
  GameConfigDraftState,
  getGameConfigDraft,
  getGameConfigEnvironment,
  listGameConfigDrafts,
  listGameConfigVersions,
  rollbackGameConfigEnvironment,
  toNovaGameApiError,
} from '#/api';
import { GameConfigPermission } from '#/features/game-config/permissions';
import {
  operationsErrorMessage,
  tOperations,
  useOperationsMessage,
} from '#/locales/operations';

import DraftEditorDrawer from './components/draft-editor-drawer.vue';
import ReviewPublishDrawer from './components/review-publish-drawer.vue';
import VersionDetailDrawer from './components/version-detail-drawer.vue';

defineOptions({ name: 'GameConfig' });

const accessStore = useAccessStore();
const isMobile = useMediaQuery('(max-width: 640px)');
const activeTab = ref('drafts');
const configurationSet = ref('default');
const environmentName = ref('development');
const scopeError = useOperationsMessage();
const forbidden = ref(false);

const draftLoading = ref(false);
const draftError = useOperationsMessage();
const drafts = ref<GameConfigDraftSummary[]>([]);
const draftTotal = ref(0);
const draftQuery = reactive({
  page: 1,
  pageSize: 20,
  search: '',
  state: undefined as DraftState | undefined,
});

const versionLoading = ref(false);
const versionError = useOperationsMessage();
const versions = ref<GameConfigVersionSummary[]>([]);
const versionTotal = ref(0);
const versionQuery = reactive({ page: 1, pageSize: 20, schemaVersion: '' });

const environmentLoading = ref(false);
const environmentError = useOperationsMessage();
const environment = ref<GameConfigEnvironment | null>(null);

const draftDrawerOpen = ref(false);
const selectedDraftId = ref<null | string>(null);
const reviewDrawerOpen = ref(false);
const reviewDraft = ref<GameConfigDraft | null>(null);
const versionDrawerOpen = ref(false);
const selectedVersionId = ref<null | string>(null);

const activationDialogOpen = ref(false);
const activationLoading = ref(false);
const activationConflict = useOperationsMessage();
const activationForm = reactive({ reason: '', versionId: '' });
const rollbackDialogOpen = ref(false);
const rollbackLoading = ref(false);
const rollbackConflict = useOperationsMessage();
const rollbackForm = reactive({ reason: '', targetVersionId: '' });

const lastCommand = ref<null | {
  correlationId: string;
  message: string;
}>(null);

const canManageDrafts = computed(() =>
  accessStore.accessCodes.includes(GameConfigPermission.ManageDrafts),
);
const canPublish = computed(() =>
  accessStore.accessCodes.includes(GameConfigPermission.Publish),
);
const canActivate = computed(() =>
  accessStore.accessCodes.includes(GameConfigPermission.Activate),
);
const paginationLayout = computed(() =>
  isMobile.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next',
);
const paginationPagerCount = computed(() => (isMobile.value ? 5 : 7));

const draftStateOptions = computed(() => [
  { label: tOperations('editing'), value: GameConfigDraftState.Editing },
  {
    label: tOperations('validationFailed'),
    value: GameConfigDraftState.ValidationFailed,
  },
  { label: tOperations('validated'), value: GameConfigDraftState.Validated },
  { label: tOperations('published'), value: GameConfigDraftState.Published },
]);

function shortId(value: null | string) {
  return value ? value.slice(0, 8) : '—';
}

function formatDate(value: string) {
  return new Date(value).toLocaleString();
}

function stateLabel(state: DraftState) {
  return (
    draftStateOptions.value.find((option) => option.value === state)?.label ??
    tOperations('unknown')
  );
}

function stateTagType(state: DraftState) {
  switch (state) {
    case GameConfigDraftState.Published: {
      return 'info';
    }
    case GameConfigDraftState.Validated: {
      return 'success';
    }
    case GameConfigDraftState.ValidationFailed: {
      return 'danger';
    }
    default: {
      return 'warning';
    }
  }
}

function validateScope() {
  const scopePattern = /^[a-z][a-z0-9-]{0,63}$/;
  if (!scopePattern.test(configurationSet.value)) {
    scopeError.value = () => tOperations('configurationSetIsNotValid');
    return false;
  }
  if (!scopePattern.test(environmentName.value)) {
    scopeError.value = () => tOperations('environmentIsNotValid');
    return false;
  }
  scopeError.value = '';
  return true;
}

function recordError(error: unknown, target: { value: string }) {
  const apiError = toNovaGameApiError(error);
  if (apiError.status === 403) {
    forbidden.value = true;
    return;
  }
  target.value = operationsErrorMessage(apiError);
}

async function loadDrafts() {
  if (!validateScope()) return;
  draftLoading.value = true;
  draftError.value = '';
  try {
    const result = await listGameConfigDrafts({
      configurationSet: configurationSet.value,
      page: draftQuery.page,
      pageSize: draftQuery.pageSize,
      search: draftQuery.search.trim() || undefined,
      state: draftQuery.state,
    });
    drafts.value = result.items;
    draftTotal.value = result.totalCount;
  } catch (error) {
    recordError(error, draftError);
  } finally {
    draftLoading.value = false;
  }
}

async function loadVersions() {
  if (!validateScope()) return;
  versionLoading.value = true;
  versionError.value = '';
  try {
    const result = await listGameConfigVersions({
      configurationSet: configurationSet.value,
      page: versionQuery.page,
      pageSize: versionQuery.pageSize,
      schemaVersion: versionQuery.schemaVersion.trim() || undefined,
    });
    versions.value = result.items;
    versionTotal.value = result.totalCount;
  } catch (error) {
    recordError(error, versionError);
  } finally {
    versionLoading.value = false;
  }
}

async function loadEnvironment() {
  if (!validateScope()) return;
  environmentLoading.value = true;
  environmentError.value = '';
  try {
    environment.value = await getGameConfigEnvironment(
      environmentName.value,
      configurationSet.value,
    );
  } catch (error) {
    const apiError = toNovaGameApiError(error);
    if (apiError.status === 404) {
      environment.value = null;
    } else {
      recordError(error, environmentError);
    }
  } finally {
    environmentLoading.value = false;
  }
}

async function refreshWorkspace() {
  if (!validateScope()) return;
  forbidden.value = false;
  await Promise.all([loadDrafts(), loadVersions(), loadEnvironment()]);
}

function applyScope() {
  draftQuery.page = 1;
  versionQuery.page = 1;
  void refreshWorkspace();
}

function openCreateDraft() {
  selectedDraftId.value = null;
  draftDrawerOpen.value = true;
}

function openDraft(draftId: string) {
  selectedDraftId.value = draftId;
  draftDrawerOpen.value = true;
}

async function openReview(draft: GameConfigDraft | GameConfigDraftSummary) {
  try {
    reviewDraft.value =
      'content' in draft ? draft : await getGameConfigDraft(draft.id);
    reviewDrawerOpen.value = true;
  } catch (error) {
    ElMessage.error(operationsErrorMessage(error));
  }
}

function openVersion(versionId: string) {
  selectedVersionId.value = versionId;
  versionDrawerOpen.value = true;
}

function openActivation(version: GameConfigVersion | GameConfigVersionSummary) {
  activationForm.versionId = version.id;
  activationForm.reason = '';
  activationConflict.value = '';
  activationDialogOpen.value = true;
}

async function handleActivation() {
  if (!activationForm.versionId) return;
  activationLoading.value = true;
  activationConflict.value = '';
  try {
    const result = await activateGameConfigVersion(environmentName.value, {
      configurationSet: configurationSet.value,
      expectedRevision: environment.value?.revision ?? 0,
      reason: activationForm.reason.trim() || null,
      versionId: activationForm.versionId,
    });
    environment.value = result.data;
    lastCommand.value = {
      correlationId: result.correlationId,
      message: tOperations('versionParameterActivatedInParameter', {
        value0: shortId(result.data.activeVersionId),
        value1: result.data.environment,
      }),
    };
    activationDialogOpen.value = false;
    ElMessage.success(tOperations('versionActivated'));
  } catch (error) {
    const apiError = toNovaGameApiError(error);
    if (apiError.status === 409) {
      activationConflict.value = () => operationsErrorMessage(apiError);
    } else {
      ElMessage.error(operationsErrorMessage(apiError));
    }
  } finally {
    activationLoading.value = false;
  }
}

function openRollback() {
  if (!environment.value?.previousVersionId) return;
  rollbackForm.targetVersionId = environment.value.previousVersionId;
  rollbackForm.reason = '';
  rollbackConflict.value = '';
  rollbackDialogOpen.value = true;
}

async function handleRollback() {
  if (!environment.value || !rollbackForm.targetVersionId) return;
  if (!rollbackForm.reason.trim()) {
    ElMessage.warning(tOperations('rollbackReasonIsRequired'));
    return;
  }

  rollbackLoading.value = true;
  rollbackConflict.value = '';
  try {
    const result = await rollbackGameConfigEnvironment(environmentName.value, {
      configurationSet: configurationSet.value,
      expectedRevision: environment.value.revision,
      reason: rollbackForm.reason.trim(),
      targetVersionId: rollbackForm.targetVersionId,
    });
    environment.value = result.data;
    lastCommand.value = {
      correlationId: result.correlationId,
      message: tOperations('environmentParameterRolledBackToParameter', {
        value0: result.data.environment,
        value1: shortId(result.data.activeVersionId),
      }),
    };
    rollbackDialogOpen.value = false;
    ElMessage.success(tOperations('environmentRolledBack'));
  } catch (error) {
    const apiError = toNovaGameApiError(error);
    if (apiError.status === 409) {
      rollbackConflict.value = () => operationsErrorMessage(apiError);
    } else {
      ElMessage.error(operationsErrorMessage(apiError));
    }
  } finally {
    rollbackLoading.value = false;
  }
}

function handleDraftSaved(draft: GameConfigDraft) {
  selectedDraftId.value = draft.id;
  void loadDrafts();
}

function handlePublished(version: GameConfigVersion) {
  reviewDrawerOpen.value = false;
  selectedVersionId.value = version.id;
  versionDrawerOpen.value = true;
  void Promise.all([loadDrafts(), loadVersions()]);
}

function handlePublishConflict(draftId: string) {
  reviewDrawerOpen.value = false;
  openDraft(draftId);
}

onMounted(() => {
  void refreshWorkspace();
});
</script>

<template>
  <div class="workspace-page">
    <header class="workspace-header">
      <div class="title-block">
        <h1>{{ tOperations('gameConfiguration') }}</h1>
        <div class="scope-summary">
          <span>{{ configurationSet }}</span>
          <span class="scope-divider">/</span>
          <span>{{ environmentName }}</span>
          <ElTag v-if="environment" size="small" type="success">
            {{ tOperations('active') }}
          </ElTag>
          <ElTag v-else size="small" type="info">
            {{ tOperations('notActivated') }}
          </ElTag>
        </div>
      </div>

      <div class="scope-controls">
        <ElInput
          v-model="configurationSet"
          :aria-label="tOperations('configurationSet')"
          data-testid="configuration-set"
          maxlength="64"
          :placeholder="tOperations('configurationSet')"
          @keyup.enter="applyScope"
        />
        <ElInput
          v-model="environmentName"
          :aria-label="tOperations('environment')"
          data-testid="environment-name"
          maxlength="64"
          :placeholder="tOperations('environment')"
          @keyup.enter="applyScope"
        />
        <ElButton data-testid="apply-scope" @click="applyScope">
          {{ tOperations('apply') }}
        </ElButton>
        <ElTooltip
          :content="tOperations('refreshWorkspace')"
          placement="bottom"
        >
          <ElButton
            :aria-label="tOperations('refreshWorkspace')"
            circle
            data-testid="refresh-workspace"
            @click="refreshWorkspace"
          >
            <RotateCw :size="16" />
          </ElButton>
        </ElTooltip>
      </div>
    </header>

    <main class="workspace-body">
      <ElAlert
        v-if="scopeError"
        :closable="false"
        :title="scopeError"
        show-icon
        type="warning"
      />

      <ElAlert
        v-if="lastCommand"
        :closable="true"
        :title="lastCommand.message"
        show-icon
        type="success"
        @close="lastCommand = null"
      >
        <template #default>
          {{ tOperations('auditReferencePrefix') }}
          <code>{{ lastCommand.correlationId }}</code>
        </template>
      </ElAlert>

      <ElResult
        v-if="forbidden"
        :sub-title="tOperations('yourAccountDoesNotHaveAccessToThisWorkspace')"
        :title="tOperations('forbidden')"
      >
        <template #extra>
          <ElButton @click="refreshWorkspace">
            {{ tOperations('retry') }}
          </ElButton>
        </template>
      </ElResult>

      <ElTabs v-else v-model="activeTab" class="workspace-tabs">
        <ElTabPane :label="tOperations('drafts')" name="drafts">
          <div class="table-toolbar">
            <div class="filter-row">
              <ElInput
                v-model="draftQuery.search"
                clearable
                data-testid="draft-search"
                :placeholder="tOperations('searchDrafts')"
                @clear="loadDrafts"
                @keyup.enter="loadDrafts"
              />
              <ElSelect
                v-model="draftQuery.state"
                clearable
                data-testid="draft-state-filter"
                :placeholder="tOperations('allStates')"
                @change="
                  draftQuery.page = 1;
                  loadDrafts();
                "
              >
                <ElOption
                  v-for="option in draftStateOptions"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </ElSelect>
              <ElButton
                @click="
                  draftQuery.page = 1;
                  loadDrafts();
                "
              >
                {{ tOperations('search') }}
              </ElButton>
            </div>
            <ElButton
              v-if="canManageDrafts"
              data-testid="create-draft"
              type="primary"
              @click="openCreateDraft"
            >
              <Plus :size="16" />
              {{ tOperations('newDraft') }}
            </ElButton>
          </div>

          <ElAlert
            v-if="draftError"
            :closable="false"
            :title="draftError"
            show-icon
            type="error"
          >
            <template #default>
              <ElButton size="small" @click="loadDrafts">
                {{ tOperations('retry') }}
              </ElButton>
            </template>
          </ElAlert>

          <ElTable
            v-loading="draftLoading"
            :data="drafts"
            data-testid="draft-table"
            row-key="id"
            stripe
          >
            <ElTableColumn :label="tOperations('name')" min-width="220">
              <template #default="scope">
                <div class="primary-cell">
                  <strong>{{ scope.row.name }}</strong>
                  <code>{{ shortId(scope.row.id) }}</code>
                </div>
              </template>
            </ElTableColumn>
            <ElTableColumn
              :label="tOperations('schema')"
              prop="schemaVersion"
              width="120"
            />
            <ElTableColumn :label="tOperations('state')" width="150">
              <template #default="scope">
                <ElTag :type="stateTagType(scope.row.state)" size="small">
                  {{ stateLabel(scope.row.state) }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn
              :label="tOperations('revision')"
              prop="revision"
              width="100"
            />
            <ElTableColumn :label="tOperations('updated')" min-width="190">
              <template #default="scope">
                {{ formatDate(scope.row.updatedAtUtc) }}
              </template>
            </ElTableColumn>
            <ElTableColumn
              align="right"
              fixed="right"
              :label="tOperations('actions')"
              width="132"
            >
              <template #default="scope">
                <div class="icon-actions">
                  <ElTooltip
                    :content="tOperations('openDraft')"
                    placement="top"
                  >
                    <ElButton
                      :aria-label="
                        tOperations('openParameter', { value0: scope.row.name })
                      "
                      circle
                      size="small"
                      @click="openDraft(scope.row.id)"
                    >
                      <Eye :size="15" />
                    </ElButton>
                  </ElTooltip>
                  <ElTooltip
                    v-if="scope.row.state === GameConfigDraftState.Validated"
                    :content="tOperations('review')"
                    placement="top"
                  >
                    <ElButton
                      :aria-label="
                        tOperations('reviewParameter', {
                          value0: scope.row.name,
                        })
                      "
                      circle
                      data-testid="review-from-table"
                      size="small"
                      @click="openReview(scope.row)"
                    >
                      <ArrowRightLeft :size="15" />
                    </ElButton>
                  </ElTooltip>
                </div>
              </template>
            </ElTableColumn>
            <template #empty>
              <ElEmpty :description="tOperations('noDrafts')" />
            </template>
          </ElTable>

          <ElPagination
            v-model:current-page="draftQuery.page"
            v-model:page-size="draftQuery.pageSize"
            :page-sizes="[10, 20, 50, 100]"
            :total="draftTotal"
            background
            :layout="paginationLayout"
            :pager-count="paginationPagerCount"
            @current-change="loadDrafts"
            @size-change="
              draftQuery.page = 1;
              loadDrafts();
            "
          />
        </ElTabPane>

        <ElTabPane :label="tOperations('publishedVersions')" name="versions">
          <div class="table-toolbar">
            <div class="filter-row compact-filter">
              <ElInput
                v-model="versionQuery.schemaVersion"
                clearable
                data-testid="version-schema-filter"
                :placeholder="tOperations('schemaVersion')"
                @clear="loadVersions"
                @keyup.enter="loadVersions"
              />
              <ElButton
                @click="
                  versionQuery.page = 1;
                  loadVersions();
                "
              >
                {{ tOperations('filter') }}
              </ElButton>
            </div>
          </div>

          <ElAlert
            v-if="versionError"
            :closable="false"
            :title="versionError"
            show-icon
            type="error"
          />

          <ElTable
            v-loading="versionLoading"
            :data="versions"
            data-testid="version-table"
            row-key="id"
            stripe
          >
            <ElTableColumn :label="tOperations('version')" min-width="160">
              <template #default="scope">
                <code>{{ shortId(scope.row.id) }}</code>
              </template>
            </ElTableColumn>
            <ElTableColumn
              :label="tOperations('schema')"
              prop="schemaVersion"
              width="120"
            />
            <ElTableColumn
              :label="tOperations('releaseNotes')"
              min-width="260"
              prop="releaseNotes"
            />
            <ElTableColumn :label="tOperations('snapshot')" min-width="160">
              <template #default="scope">
                <code>{{ scope.row.snapshotHash.slice(0, 12) }}</code>
              </template>
            </ElTableColumn>
            <ElTableColumn :label="tOperations('published')" min-width="190">
              <template #default="scope">
                {{ formatDate(scope.row.publishedAtUtc) }}
              </template>
            </ElTableColumn>
            <ElTableColumn
              align="right"
              fixed="right"
              :label="tOperations('actions')"
              width="132"
            >
              <template #default="scope">
                <div class="icon-actions">
                  <ElTooltip
                    :content="tOperations('inspectVersion')"
                    placement="top"
                  >
                    <ElButton
                      :aria-label="
                        tOperations('inspectVersionParameter', {
                          value0: shortId(scope.row.id),
                        })
                      "
                      circle
                      size="small"
                      @click="openVersion(scope.row.id)"
                    >
                      <Eye :size="15" />
                    </ElButton>
                  </ElTooltip>
                  <ElTooltip
                    v-if="canActivate"
                    :content="tOperations('activate')"
                    placement="top"
                  >
                    <ElButton
                      :aria-label="
                        tOperations('activateVersionParameter', {
                          value0: shortId(scope.row.id),
                        })
                      "
                      circle
                      data-testid="activate-version"
                      size="small"
                      @click="openActivation(scope.row)"
                    >
                      <ArrowRightLeft :size="15" />
                    </ElButton>
                  </ElTooltip>
                </div>
              </template>
            </ElTableColumn>
            <template #empty>
              <ElEmpty :description="tOperations('noPublishedVersions')" />
            </template>
          </ElTable>

          <ElPagination
            v-model:current-page="versionQuery.page"
            v-model:page-size="versionQuery.pageSize"
            :page-sizes="[10, 20, 50, 100]"
            :total="versionTotal"
            background
            :layout="paginationLayout"
            :pager-count="paginationPagerCount"
            @current-change="loadVersions"
            @size-change="
              versionQuery.page = 1;
              loadVersions();
            "
          />
        </ElTabPane>

        <ElTabPane :label="tOperations('environment')" name="environment">
          <section v-loading="environmentLoading" class="environment-section">
            <ElAlert
              v-if="environmentError"
              :closable="false"
              :title="environmentError"
              show-icon
              type="error"
            />

            <template v-if="environment">
              <div class="environment-heading">
                <div>
                  <h2>{{ environment.environment }}</h2>
                  <p>
                    {{ tOperations('revision') }} {{ environment.revision }}
                  </p>
                </div>
                <div class="environment-actions">
                  <ElButton @click="openVersion(environment.activeVersionId)">
                    <Eye :size="16" />
                    {{ tOperations('inspectActive') }}
                  </ElButton>
                  <ElButton
                    v-if="canActivate && environment.previousVersionId"
                    data-testid="open-rollback"
                    type="warning"
                    @click="openRollback"
                  >
                    <Undo2 :size="16" />
                    {{ tOperations('rollBack') }}
                  </ElButton>
                </div>
              </div>

              <ElDescriptions :column="2" border>
                <ElDescriptionsItem :label="tOperations('activeVersion')">
                  <code>{{ environment.activeVersionId }}</code>
                </ElDescriptionsItem>
                <ElDescriptionsItem :label="tOperations('previousVersion')">
                  <code>{{ environment.previousVersionId ?? '—' }}</code>
                </ElDescriptionsItem>
                <ElDescriptionsItem :label="tOperations('schema')">
                  {{ environment.schemaVersion }}
                </ElDescriptionsItem>
                <ElDescriptionsItem :label="tOperations('contentHash')">
                  <code>{{ environment.contentHash }}</code>
                </ElDescriptionsItem>
                <ElDescriptionsItem :label="tOperations('activatedAt')">
                  {{ formatDate(environment.activatedAtUtc) }}
                </ElDescriptionsItem>
                <ElDescriptionsItem :label="tOperations('reason')">
                  {{ environment.reason ?? '—' }}
                </ElDescriptionsItem>
              </ElDescriptions>
            </template>

            <ElEmpty
              v-else-if="!environmentError"
              :description="tOperations('noActiveVersionForThisEnvironment')"
            >
              <ElButton @click="activeTab = 'versions'">
                {{ tOperations('viewPublishedVersions') }}
              </ElButton>
            </ElEmpty>
          </section>
        </ElTabPane>
      </ElTabs>
    </main>

    <DraftEditorDrawer
      v-model="draftDrawerOpen"
      :can-manage="canManageDrafts"
      :configuration-set="configurationSet"
      :draft-id="selectedDraftId"
      :version-options="versions"
      @review="openReview"
      @saved="handleDraftSaved"
    />

    <ReviewPublishDrawer
      v-model="reviewDrawerOpen"
      :can-publish="canPublish"
      :draft="reviewDraft"
      :environment="environmentName"
      @conflict="handlePublishConflict"
      @published="handlePublished"
    />

    <VersionDetailDrawer
      v-model="versionDrawerOpen"
      :can-activate="canActivate"
      :version-id="selectedVersionId"
      @activate="openActivation"
    />

    <ElDialog
      v-model="activationDialogOpen"
      destroy-on-close
      :title="tOperations('activateVersion')"
      width="min(520px, 94vw)"
    >
      <ElAlert
        v-if="activationConflict"
        :closable="false"
        :title="activationConflict"
        show-icon
        type="warning"
      >
        <template #default>
          <ElButton size="small" @click="loadEnvironment">
            {{ tOperations('reloadEnvironment') }}
          </ElButton>
        </template>
      </ElAlert>
      <ElForm label-position="top">
        <ElFormItem :label="tOperations('version')">
          <ElSelect
            v-model="activationForm.versionId"
            data-testid="activation-version"
          >
            <ElOption
              v-for="version in versions"
              :key="version.id"
              :label="`${version.schemaVersion} · ${shortId(version.id)}`"
              :value="version.id"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem :label="tOperations('reason')">
          <ElInput
            v-model="activationForm.reason"
            data-testid="activation-reason"
            maxlength="512"
            type="textarea"
          />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="activationDialogOpen = false">
          {{ tOperations('cancel') }}
        </ElButton>
        <ElButton
          :loading="activationLoading"
          data-testid="confirm-activation"
          type="primary"
          @click="handleActivation"
        >
          {{ tOperations('activate') }}
        </ElButton>
      </template>
    </ElDialog>

    <ElDialog
      v-model="rollbackDialogOpen"
      destroy-on-close
      :title="tOperations('rollBackEnvironment')"
      width="min(520px, 94vw)"
    >
      <ElAlert
        v-if="rollbackConflict"
        :closable="false"
        :title="rollbackConflict"
        show-icon
        type="warning"
      />
      <ElForm label-position="top">
        <ElFormItem :label="tOperations('targetVersion')">
          <ElSelect
            v-model="rollbackForm.targetVersionId"
            data-testid="rollback-version"
          >
            <ElOption
              v-for="version in versions"
              :key="version.id"
              :label="`${version.schemaVersion} · ${shortId(version.id)}`"
              :value="version.id"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem :label="tOperations('reason')">
          <ElInput
            v-model="rollbackForm.reason"
            data-testid="rollback-reason"
            maxlength="512"
            type="textarea"
          />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="rollbackDialogOpen = false">
          {{ tOperations('cancel') }}
        </ElButton>
        <ElButton
          :loading="rollbackLoading"
          data-testid="confirm-rollback"
          type="warning"
          @click="handleRollback"
        >
          {{ tOperations('rollBack') }}
        </ElButton>
      </template>
    </ElDialog>
  </div>
</template>

<style scoped>
.workspace-page {
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

.title-block h1 {
  margin: 0;
  font-size: 22px;
  font-weight: 650;
}

.scope-summary {
  display: flex;
  gap: 7px;
  align-items: center;
  margin-top: 5px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.scope-divider {
  color: var(--el-border-color-darker);
}

.scope-controls {
  display: grid;
  grid-template-columns: minmax(150px, 1fr) minmax(150px, 1fr) auto 32px;
  gap: 8px;
  align-items: center;
  width: min(650px, 100%);
}

.scope-controls :deep(.el-button + .el-button) {
  margin-left: 0;
}

.workspace-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding: 0 24px 28px;
}

.workspace-tabs :deep(.el-tabs__header) {
  margin-bottom: 14px;
}

.table-toolbar,
.filter-row,
.icon-actions,
.environment-heading,
.environment-actions {
  display: flex;
  gap: 10px;
  align-items: center;
}

.table-toolbar {
  justify-content: space-between;
  min-height: 42px;
  margin-bottom: 12px;
}

.filter-row {
  width: min(620px, 100%);
}

.filter-row > :first-child {
  flex: 1;
}

.filter-row .el-select {
  width: 180px;
}

.compact-filter {
  width: 360px;
}

.icon-actions {
  justify-content: flex-end;
}

.primary-cell {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.primary-cell code,
td code {
  font-size: 11px;
  color: var(--el-text-color-secondary);
}

.el-pagination {
  justify-content: flex-end;
  margin-top: 16px;
}

.environment-section {
  min-height: 380px;
  padding-top: 4px;
}

.environment-heading {
  justify-content: space-between;
  margin-bottom: 16px;
}

.environment-heading h2,
.environment-heading p {
  margin: 0;
}

.environment-heading h2 {
  font-size: 18px;
}

.environment-heading p {
  margin-top: 3px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

code {
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  overflow-wrap: anywhere;
}

@media (max-width: 900px) {
  .workspace-header {
    flex-direction: column;
    align-items: flex-start;
  }

  .scope-controls {
    width: 100%;
  }
}

@media (max-width: 640px) {
  .workspace-header,
  .workspace-body {
    padding-right: 14px;
    padding-left: 14px;
  }

  .scope-controls {
    grid-template-columns: 1fr 1fr;
  }

  .table-toolbar,
  .filter-row,
  .environment-heading {
    flex-direction: column;
    align-items: stretch;
  }

  .filter-row,
  .compact-filter,
  .filter-row .el-select {
    width: 100%;
  }

  .environment-actions {
    flex-wrap: wrap;
  }

  .scope-controls :deep(.el-button.is-circle),
  .icon-actions :deep(.el-button.is-circle) {
    width: 44px;
    min-width: 44px;
    height: 44px;
  }

  .icon-actions :deep(.el-button + .el-button) {
    margin-left: 0;
  }

  .el-pagination {
    --el-pagination-button-width: 44px;
    --el-pagination-button-height: 44px;

    justify-content: center;
  }

  .el-pagination :deep(.btn-prev),
  .el-pagination :deep(.btn-next),
  .el-pagination :deep(.el-pager li) {
    margin-right: 2px;
    margin-left: 2px;
  }
}
</style>
