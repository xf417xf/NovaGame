<script lang="ts" setup>
import type {
  PlayerEquipmentGrantHistoryItem,
  PlayerEquipmentGrantInteractionState,
  PlayerEquipmentGrantPageSnapshot,
} from '#/features/player-equipment-grants/state';

import { computed, onBeforeUnmount, ref } from 'vue';

import { Plus, RotateCw, Search, Undo2 } from '@vben/icons';
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
  ElPagination,
  ElResult,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTooltip,
} from 'element-plus';

import { playerEquipmentGrantTransport } from '#/api/player-equipment-grants';
import { SwordMasterPlayerEquipmentGrantInteractionModel } from '#/features/player-equipment-grants/model';
import { PlayerEquipmentGrantPermission } from '#/features/player-equipment-grants/permissions';

defineOptions({ name: 'PlayerEquipmentGrants' });

const defaultPageSize = 20;

const accessStore = useAccessStore();
const isMobile = useMediaQuery('(max-width: 640px)');
const playerIdInput = ref('');
const requestedPageSize = ref(defaultPageSize);
const grantDialogOpen = ref(false);
const rollbackDialogOpen = ref(false);
const grantForm = ref({ configuredItemId: '', reason: '' });
const rollbackForm = ref({ grantId: '', reason: '' });
const state = ref<PlayerEquipmentGrantInteractionState>({ phase: 'idle' });

const interactionModel = new SwordMasterPlayerEquipmentGrantInteractionModel({
  createIdempotencyKey: createIdempotencyKey,
  transport: playerEquipmentGrantTransport,
});
const unsubscribe = interactionModel.subscribe((nextState) => {
  state.value = nextState;
});

const canManage = computed(() =>
  accessStore.accessCodes.includes(PlayerEquipmentGrantPermission.Manage),
);
const snapshot = computed<PlayerEquipmentGrantPageSnapshot | undefined>(() => {
  const current = state.value;
  return 'snapshot' in current ? current.snapshot : undefined;
});
const history = computed(() => [...(snapshot.value?.data.items ?? [])]);
const historyPage = computed(() => snapshot.value?.data.page ?? 1);
const historyPageSize = computed(
  () => snapshot.value?.data.pageSize ?? requestedPageSize.value,
);
const historyTotalCount = computed(() => snapshot.value?.data.totalCount ?? 0);
const isBusy = computed(
  () =>
    state.value.phase === 'loading' ||
    state.value.phase === 'mutating' ||
    state.value.phase === 'refreshing',
);
const isLoading = computed(() => state.value.phase === 'loading');
const showForbidden = computed(() => !canManage.value);
const showEmpty = computed(() => state.value.phase === 'empty');
const showFailure = computed(() => state.value.phase === 'failed');
const failure = computed(() =>
  state.value.phase === 'failed' ? state.value.failure : undefined,
);
const retryKind = computed(() =>
  state.value.phase === 'failed' ? state.value.retry : null,
);
const requiresExplicitRetry = computed(
  () => retryKind.value === 'exact_mutation' || retryKind.value === 'refresh',
);
const canLoadHistory = computed(
  () => canManage.value && !isBusy.value && !requiresExplicitRetry.value,
);
const canStartNewCommand = computed(() => {
  if (!canManage.value || isBusy.value) {
    return false;
  }
  return (
    state.value.phase === 'empty' ||
    state.value.phase === 'ready' ||
    (state.value.phase === 'failed' && state.value.canStartNewCommand)
  );
});
const currentPlayerId = computed(() => snapshot.value?.data.playerId ?? null);
const paginationLayout = computed(() =>
  isMobile.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next',
);
const paginationPagerCount = computed(() => (isMobile.value ? 5 : 7));
const operationLabel = computed(() => {
  switch (state.value.phase) {
    case 'loading': {
      return 'Loading player history';
    }
    case 'mutating': {
      return state.value.command.kind === 'grant'
        ? 'Granting equipment'
        : 'Rolling back grant';
    }
    case 'refreshing': {
      if (state.value.reason === 'conflict') {
        return 'Refreshing after a conflict';
      }
      return 'Refreshing authoritative history';
    }
    default: {
      return '';
    }
  }
});

function createIdempotencyKey(): string {
  const id = globalThis.crypto?.randomUUID?.();
  if (!id) {
    throw new Error('The browser cannot generate an idempotency key.');
  }
  return id.toLowerCase();
}

function formatUtc(value: null | string): string {
  if (!value) {
    return 'Not recorded';
  }
  const instant = new Date(value);
  return Number.isNaN(instant.getTime()) ? value : instant.toISOString();
}

function shortId(value: string): string {
  return value.slice(0, 8);
}

function statusLabel(
  status: PlayerEquipmentGrantHistoryItem['status'],
): string {
  return status === 1 ? 'Active' : 'Rolled back';
}

function statusType(
  status: PlayerEquipmentGrantHistoryItem['status'],
): 'info' | 'success' {
  return status === 1 ? 'success' : 'info';
}

function isReasonValid(value: string): boolean {
  return (
    value.length >= 1 &&
    value.length <= 500 &&
    value === value.trim() &&
    !/\p{Cc}/u.test(value)
  );
}

function isConfiguredItemIdValid(value: string): boolean {
  return (
    value.length >= 1 &&
    value.length <= 128 &&
    value === value.trim() &&
    !/[\s\p{Cc}]/u.test(value)
  );
}

function lookupPlayer(): void {
  if (!canLoadHistory.value) {
    return;
  }
  runInteraction(() =>
    interactionModel.load({
      page: 1,
      pageSize: requestedPageSize.value,
      playerId: playerIdInput.value,
    }),
  );
}

function refreshHistory(): void {
  const resolvedPlayerId = currentPlayerId.value;
  if (!canLoadHistory.value || !resolvedPlayerId) {
    return;
  }
  runInteraction(() =>
    interactionModel.load({
      page: snapshot.value?.data.page ?? 1,
      pageSize: snapshot.value?.data.pageSize ?? requestedPageSize.value,
      playerId: resolvedPlayerId,
    }),
  );
}

function changePage(page: number): void {
  const resolvedPlayerId = currentPlayerId.value;
  if (!canLoadHistory.value || !resolvedPlayerId) {
    return;
  }
  runInteraction(() =>
    interactionModel.load({
      page,
      pageSize: snapshot.value?.data.pageSize ?? requestedPageSize.value,
      playerId: resolvedPlayerId,
    }),
  );
}

function changePageSize(pageSize: number): void {
  const resolvedPlayerId = currentPlayerId.value;
  requestedPageSize.value = pageSize;
  if (!canLoadHistory.value || !resolvedPlayerId) {
    return;
  }
  runInteraction(() =>
    interactionModel.load({ page: 1, pageSize, playerId: resolvedPlayerId }),
  );
}

function openGrantDialog(): void {
  if (!canStartNewCommand.value) {
    return;
  }
  grantDialogOpen.value = true;
}

function closeGrantDialog(): void {
  if (!isBusy.value) {
    grantDialogOpen.value = false;
  }
}

function confirmGrant(): void {
  if (
    !canStartNewCommand.value ||
    !isConfiguredItemIdValid(grantForm.value.configuredItemId) ||
    !isReasonValid(grantForm.value.reason)
  ) {
    return;
  }
  runInteraction(() =>
    interactionModel.grant({
      configuredItemId: grantForm.value.configuredItemId,
      reason: grantForm.value.reason,
    }),
  );
}

function openRollbackDialog(grant: PlayerEquipmentGrantHistoryItem): void {
  if (!canStartNewCommand.value || grant.status !== 1) {
    return;
  }
  rollbackForm.value = { grantId: grant.grantId, reason: '' };
  rollbackDialogOpen.value = true;
}

function closeRollbackDialog(): void {
  if (!isBusy.value) {
    rollbackDialogOpen.value = false;
  }
}

function confirmRollback(): void {
  if (!canStartNewCommand.value || !isReasonValid(rollbackForm.value.reason)) {
    return;
  }
  runInteraction(() =>
    interactionModel.rollback({
      grantId: rollbackForm.value.grantId,
      reason: rollbackForm.value.reason,
    }),
  );
}

function retryOperation(): void {
  if (!retryKind.value || isBusy.value) {
    return;
  }
  runInteraction(() => interactionModel.retry());
}

function runInteraction(
  operation: () => Promise<PlayerEquipmentGrantInteractionState>,
): void {
  void operation().catch(() => undefined);
}

function syncDialogState(): void {
  if (state.value.phase === 'ready' || state.value.phase === 'empty') {
    grantDialogOpen.value = false;
    rollbackDialogOpen.value = false;
    grantForm.value = { configuredItemId: '', reason: '' };
    rollbackForm.value = { grantId: '', reason: '' };
    return;
  }

  if (state.value.phase === 'failed' && state.value.retry !== null) {
    grantDialogOpen.value = false;
    rollbackDialogOpen.value = false;
  }
}

const unsubscribeDialogs = interactionModel.subscribe(syncDialogState);

onBeforeUnmount(() => {
  unsubscribeDialogs();
  unsubscribe();
});
</script>

<template>
  <div class="workspace-page" data-testid="equipment-grants-workspace">
    <header class="workspace-header">
      <div class="title-block">
        <h1>Player equipment grants</h1>
        <p>
          Review one exact player, grant one configured item, and retain the
          authoritative history.
        </p>
      </div>

      <form class="lookup-controls" @submit.prevent="lookupPlayer">
        <ElInput
          v-model="playerIdInput"
          :disabled="!canLoadHistory"
          aria-label="Exact player ID"
          data-testid="player-id-input"
          maxlength="36"
          placeholder="Exact player UUID"
        />
        <ElButton
          :disabled="!canLoadHistory"
          data-testid="lookup-player"
          native-type="submit"
          type="primary"
        >
          <Search :size="16" />
          Lookup
        </ElButton>
        <ElTooltip content="Refresh current history" placement="bottom">
          <ElButton
            :disabled="!canLoadHistory || !currentPlayerId"
            aria-label="Refresh current history"
            circle
            data-testid="refresh-history"
            @click="refreshHistory"
          >
            <RotateCw :size="16" />
          </ElButton>
        </ElTooltip>
      </form>
    </header>

    <main class="workspace-body">
      <ElResult
        v-if="showForbidden"
        data-testid="equipment-grants-forbidden"
        sub-title="Your account does not have permission to operate player equipment grants."
        title="Forbidden"
      />

      <template v-else>
        <ElAlert
          v-if="operationLabel"
          :closable="false"
          :title="operationLabel"
          data-testid="operation-progress"
          show-icon
          type="info"
        />

        <ElAlert
          v-if="showFailure && failure"
          :closable="false"
          :title="failure.message"
          data-testid="equipment-grants-error"
          show-icon
          type="error"
        >
          <template #default>
            <div class="failure-details">
              <code v-if="failure.code">{{ failure.code }}</code>
              <span v-else>{{ failure.kind }}</span>
              <ElButton
                v-if="retryKind"
                :disabled="isBusy"
                data-testid="retry-operation"
                size="small"
                @click="retryOperation"
              >
                <RotateCw :size="15" />
                Retry {{ retryKind.replace('_', ' ') }}
              </ElButton>
            </div>
            <ul
              v-if="failure.details"
              class="failure-field-details"
              data-testid="equipment-grants-error-details"
            >
              <li
                v-for="[field, messages] in Object.entries(failure.details)"
                :key="field"
              >
                <strong>{{ field }}:</strong> {{ messages.join(' ') }}
              </li>
            </ul>
          </template>
        </ElAlert>

        <section
          v-if="snapshot"
          class="player-summary"
          data-testid="player-summary"
        >
          <ElDescriptions :column="isMobile ? 1 : 3" border size="small">
            <ElDescriptionsItem label="Player ID">
              <code data-testid="resolved-player-id">{{
                snapshot.data.playerId
              }}</code>
            </ElDescriptionsItem>
            <ElDescriptionsItem label="Current revision">
              <span data-testid="current-revision">{{
                snapshot.data.revision
              }}</span>
            </ElDescriptionsItem>
            <ElDescriptionsItem label="History records">
              {{ snapshot.data.totalCount }}
            </ElDescriptionsItem>
          </ElDescriptions>
        </section>

        <section v-if="snapshot" class="history-section">
          <div class="section-heading">
            <div>
              <h2>Grant history</h2>
              <p>
                Server order and lifecycle evidence are shown without local
                reconciliation.
              </p>
            </div>
            <ElButton
              :disabled="!canStartNewCommand"
              data-testid="open-grant-dialog"
              type="primary"
              @click="openGrantDialog"
            >
              <Plus :size="16" />
              Grant equipment
            </ElButton>
          </div>

          <ElEmpty
            v-if="showEmpty"
            data-testid="equipment-grants-empty"
            description="No equipment grants were recorded for this player."
          />

          <div v-else class="history-table-wrap">
            <ElTable
              v-loading="isLoading || isBusy"
              :data="history"
              data-testid="equipment-grants-history"
              row-key="grantId"
              stripe
            >
              <ElTableColumn label="Status" min-width="120">
                <template
                  #default="{ row }: { row: PlayerEquipmentGrantHistoryItem }"
                >
                  <ElTag :type="statusType(row.status)" size="small">
                    {{ statusLabel(row.status) }}
                  </ElTag>
                </template>
              </ElTableColumn>
              <ElTableColumn
                label="Configured item"
                min-width="170"
                prop="configuredItemId"
              >
                <template
                  #default="{ row }: { row: PlayerEquipmentGrantHistoryItem }"
                >
                  <div class="primary-cell">
                    <strong>{{ row.configuredItemId }}</strong>
                    <code>Owned {{ shortId(row.ownedItemId) }}</code>
                  </div>
                </template>
              </ElTableColumn>
              <ElTableColumn label="Grant evidence" min-width="235">
                <template
                  #default="{ row }: { row: PlayerEquipmentGrantHistoryItem }"
                >
                  <div class="primary-cell">
                    <code>{{ row.grantId }}</code>
                    <span>Revision {{ row.grantResultingRevision }}</span>
                    <span>{{ formatUtc(row.grantedAtUtc) }}</span>
                  </div>
                </template>
              </ElTableColumn>
              <ElTableColumn label="Configuration" min-width="180">
                <template
                  #default="{ row }: { row: PlayerEquipmentGrantHistoryItem }"
                >
                  <code>{{ row.configurationVersionId }}</code>
                </template>
              </ElTableColumn>
              <ElTableColumn label="Grant actor and reason" min-width="230">
                <template
                  #default="{ row }: { row: PlayerEquipmentGrantHistoryItem }"
                >
                  <div class="primary-cell">
                    <code>{{ row.grantedByAdministratorId }}</code>
                    <span>{{ row.grantReason }}</span>
                  </div>
                </template>
              </ElTableColumn>
              <ElTableColumn label="Rollback evidence" min-width="240">
                <template
                  #default="{ row }: { row: PlayerEquipmentGrantHistoryItem }"
                >
                  <div v-if="row.status === 2" class="primary-cell">
                    <code>{{ row.rolledBackByAdministratorId }}</code>
                    <span>{{ row.rollbackReason }}</span>
                    <span>{{ formatUtc(row.rolledBackAtUtc) }}</span>
                  </div>
                  <span v-else>Active grant</span>
                </template>
              </ElTableColumn>
              <ElTableColumn fixed="right" label="Action" min-width="120">
                <template
                  #default="{ row }: { row: PlayerEquipmentGrantHistoryItem }"
                >
                  <ElButton
                    v-if="row.status === 1"
                    :disabled="!canStartNewCommand"
                    data-testid="open-rollback-dialog"
                    size="small"
                    type="warning"
                    @click="openRollbackDialog(row)"
                  >
                    <Undo2 :size="15" />
                    Roll back
                  </ElButton>
                  <span v-else class="muted-cell">Completed</span>
                </template>
              </ElTableColumn>
            </ElTable>
          </div>

          <ElPagination
            :current-page="historyPage"
            :disabled="!canLoadHistory"
            :layout="paginationLayout"
            :page-size="historyPageSize"
            :page-sizes="[10, 20, 50, 100]"
            :pager-count="paginationPagerCount"
            :total="historyTotalCount"
            data-testid="history-pagination"
            @current-change="changePage"
            @size-change="changePageSize"
          />
        </section>

        <ElEmpty
          v-else-if="!isLoading && state.phase === 'idle'"
          data-testid="equipment-grants-idle"
          description="Enter one exact player UUID to load grant history."
        />
      </template>
    </main>

    <ElDialog
      :close-on-click-modal="false"
      :close-on-press-escape="!isBusy"
      :model-value="grantDialogOpen"
      :show-close="!isBusy"
      destroy-on-close
      title="Confirm equipment grant"
      width="min(560px, calc(100vw - 28px))"
      @close="closeGrantDialog"
    >
      <ElForm label-position="top">
        <ElFormItem label="Configured item ID" required>
          <ElInput
            v-model="grantForm.configuredItemId"
            :disabled="isBusy"
            data-testid="grant-configured-item-id"
            maxlength="128"
            placeholder="Configured item ID"
          />
        </ElFormItem>
        <ElFormItem label="Reason" required>
          <ElInput
            v-model="grantForm.reason"
            :disabled="isBusy"
            data-testid="grant-reason"
            maxlength="500"
            placeholder="Required operator reason"
            :rows="4"
            type="textarea"
          />
        </ElFormItem>
        <p class="confirmation-context">
          Player <code>{{ currentPlayerId }}</code
          >, revision <strong>{{ snapshot?.data.revision }}</strong
          >.
        </p>
      </ElForm>

      <template #footer>
        <div class="dialog-actions">
          <ElButton :disabled="isBusy" @click="closeGrantDialog"
            >Cancel</ElButton
          >
          <ElButton
            :disabled="
              !isConfiguredItemIdValid(grantForm.configuredItemId) ||
              !isReasonValid(grantForm.reason) ||
              !canStartNewCommand
            "
            :loading="isBusy"
            data-testid="confirm-grant"
            type="primary"
            @click="confirmGrant"
          >
            Confirm grant
          </ElButton>
        </div>
      </template>
    </ElDialog>

    <ElDialog
      :close-on-click-modal="false"
      :close-on-press-escape="!isBusy"
      :model-value="rollbackDialogOpen"
      :show-close="!isBusy"
      destroy-on-close
      title="Confirm equipment grant rollback"
      width="min(560px, calc(100vw - 28px))"
      @close="closeRollbackDialog"
    >
      <ElForm label-position="top">
        <ElFormItem label="Rollback reason" required>
          <ElInput
            v-model="rollbackForm.reason"
            :disabled="isBusy"
            data-testid="rollback-reason"
            maxlength="500"
            placeholder="Required operator reason"
            :rows="4"
            type="textarea"
          />
        </ElFormItem>
        <p class="confirmation-context">
          Grant <code>{{ rollbackForm.grantId }}</code> for player
          <code>{{ currentPlayerId }}</code
          >. The server decides whether rollback remains valid.
        </p>
      </ElForm>

      <template #footer>
        <div class="dialog-actions">
          <ElButton :disabled="isBusy" @click="closeRollbackDialog">
            Cancel
          </ElButton>
          <ElButton
            :disabled="
              !isReasonValid(rollbackForm.reason) || !canStartNewCommand
            "
            :loading="isBusy"
            data-testid="confirm-rollback"
            type="warning"
            @click="confirmRollback"
          >
            Confirm rollback
          </ElButton>
        </div>
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

.title-block h1,
.title-block p,
.section-heading h2,
.section-heading p {
  margin: 0;
}

.title-block h1 {
  font-size: 22px;
  font-weight: 650;
}

.title-block p,
.section-heading p,
.muted-cell {
  margin-top: 4px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.lookup-controls {
  display: grid;
  grid-template-columns: minmax(250px, 1fr) auto 32px;
  gap: 8px;
  align-items: center;
  width: min(620px, 100%);
}

.lookup-controls :deep(.el-button + .el-button) {
  margin-left: 0;
}

.workspace-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 20px 24px 28px;
}

.history-section {
  min-width: 0;
}

.section-heading {
  display: flex;
  gap: 16px;
  align-items: flex-end;
  justify-content: space-between;
  margin-bottom: 14px;
}

.section-heading h2 {
  font-size: 18px;
}

.history-table-wrap {
  min-width: 0;
  overflow-x: auto;
}

.primary-cell {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.primary-cell span {
  overflow-wrap: anywhere;
}

.failure-details,
.dialog-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  align-items: center;
}

.failure-details {
  margin-top: 8px;
}

.dialog-actions {
  justify-content: flex-end;
}

.confirmation-context {
  margin: 4px 0 0;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

code {
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  overflow-wrap: anywhere;
}

.el-pagination {
  justify-content: flex-end;
  margin-top: 16px;
}

@media (max-width: 900px) {
  .workspace-header {
    align-items: flex-start;
    flex-direction: column;
  }

  .lookup-controls {
    width: 100%;
  }
}

@media (max-width: 640px) {
  .workspace-header,
  .workspace-body {
    padding-right: 14px;
    padding-left: 14px;
  }

  .lookup-controls {
    grid-template-columns: 1fr auto;
  }

  .lookup-controls :deep(.el-button.is-circle) {
    width: 44px;
    min-width: 44px;
    height: 44px;
  }

  .section-heading {
    align-items: stretch;
    flex-direction: column;
  }

  .section-heading .el-button {
    width: 100%;
  }

  .el-pagination {
    --el-pagination-button-height: 44px;
    --el-pagination-button-width: 44px;

    justify-content: center;
  }

  .el-pagination :deep(.btn-next),
  .el-pagination :deep(.btn-prev),
  .el-pagination :deep(.el-pager li) {
    margin-right: 2px;
    margin-left: 2px;
  }
}
</style>
