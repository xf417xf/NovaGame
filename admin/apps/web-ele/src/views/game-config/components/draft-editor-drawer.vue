<script lang="ts" setup>
import type { GameConfigDraft, GameConfigVersionSummary } from '#/api';

import { computed, reactive, ref, watch } from 'vue';

import { Check, CircleCheckBig, Eye, RotateCw, SquareCode } from '@vben/icons';

import {
  ElAlert,
  ElButton,
  ElDescriptions,
  ElDescriptionsItem,
  ElDrawer,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElOption,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTooltip,
} from 'element-plus';

import {
  createGameConfigDraft,
  GameConfigDraftState,
  getGameConfigDraft,
  toNovaGameApiError,
  updateGameConfigDraft,
  validateGameConfigDraft,
} from '#/api';
import { formatJson, validateJsonSyntax } from '#/features/game-config/json';

interface Props {
  canManage: boolean;
  configurationSet: string;
  draftId: null | string;
  modelValue: boolean;
  versionOptions: GameConfigVersionSummary[];
}

const props = defineProps<Props>();
const emit = defineEmits<{
  review: [draft: GameConfigDraft];
  saved: [draft: GameConfigDraft];
  'update:modelValue': [value: boolean];
}>();

const loading = ref(false);
const saving = ref(false);
const validating = ref(false);
const draft = ref<GameConfigDraft | null>(null);
const conflictMessage = ref('');
const auditReference = ref('');
const form = reactive({
  baseVersionId: null as null | string,
  content: '{\n  "collections": {}\n}\n',
  name: '',
  schemaVersion: '1',
});

const isCreate = computed(() => props.draftId === null);
const readOnly = computed(
  () =>
    !props.canManage || draft.value?.state === GameConfigDraftState.Published,
);
const syntax = computed(() => validateJsonSyntax(form.content));
const isValidatedRevision = computed(
  () =>
    draft.value?.latestValidation?.isValid === true &&
    draft.value.latestValidation.revision === draft.value.revision,
);
const title = computed(() =>
  isCreate.value ? 'Create configuration draft' : 'Configuration draft',
);

function resetForm() {
  draft.value = null;
  conflictMessage.value = '';
  auditReference.value = '';
  form.baseVersionId = null;
  form.content = '{\n  "collections": {}\n}\n';
  form.name = '';
  form.schemaVersion = '1';
}

function applyDraft(value: GameConfigDraft) {
  draft.value = value;
  form.baseVersionId = value.baseVersionId;
  form.content = value.content;
  form.name = value.name;
  form.schemaVersion = value.schemaVersion;
}

async function loadDraft(draftId: null | string = props.draftId) {
  if (!draftId) {
    resetForm();
    return;
  }

  loading.value = true;
  conflictMessage.value = '';
  try {
    applyDraft(await getGameConfigDraft(draftId));
  } catch (error) {
    ElMessage.error(toNovaGameApiError(error).message);
  } finally {
    loading.value = false;
  }
}

async function reloadConflictedDraft() {
  await loadDraft(draft.value?.id ?? props.draftId);
}

function validateForm() {
  if (!form.name.trim()) {
    ElMessage.warning('Draft name is required.');
    return false;
  }
  if (!/^[A-Za-z0-9][A-Za-z0-9._+-]{0,31}$/.test(form.schemaVersion)) {
    ElMessage.warning('Schema version is not valid.');
    return false;
  }
  return true;
}

function handleFailure(error: unknown) {
  const apiError = toNovaGameApiError(error);
  if (apiError.status === 409) {
    conflictMessage.value = apiError.message;
    return;
  }
  ElMessage.error(apiError.message);
}

async function saveDraft(): Promise<GameConfigDraft | null> {
  if (readOnly.value || !validateForm()) {
    return draft.value;
  }

  saving.value = true;
  conflictMessage.value = '';
  try {
    const result = draft.value
      ? await updateGameConfigDraft(draft.value.id, {
          content: form.content,
          expectedRevision: draft.value.revision,
          name: form.name.trim(),
          schemaVersion: form.schemaVersion.trim(),
        })
      : await createGameConfigDraft({
          baseVersionId: form.baseVersionId,
          configurationSet: props.configurationSet,
          content: form.content,
          name: form.name.trim(),
          schemaVersion: form.schemaVersion.trim(),
        });

    applyDraft(result.data);
    auditReference.value = result.correlationId;
    emit('saved', result.data);
    ElMessage.success('Draft saved.');
    return result.data;
  } catch (error) {
    handleFailure(error);
    return null;
  } finally {
    saving.value = false;
  }
}

async function handleValidate() {
  const savedDraft = await saveDraft();
  if (!savedDraft) {
    return;
  }

  validating.value = true;
  conflictMessage.value = '';
  try {
    const result = await validateGameConfigDraft(savedDraft.id, {
      expectedRevision: savedDraft.revision,
    });
    auditReference.value = result.correlationId;
    const refreshed = await getGameConfigDraft(savedDraft.id);
    applyDraft(refreshed);
    emit('saved', refreshed);
    if (result.data.isValid) {
      ElMessage.success('Server validation passed.');
    } else {
      ElMessage.warning('Server validation found issues.');
    }
  } catch (error) {
    handleFailure(error);
  } finally {
    validating.value = false;
  }
}

function handleFormat() {
  try {
    form.content = formatJson(form.content);
  } catch (error) {
    const message = error instanceof Error ? error.message : 'Invalid JSON.';
    ElMessage.warning(message);
  }
}

function close() {
  emit('update:modelValue', false);
}

watch(
  () => [props.modelValue, props.draftId] as const,
  ([open, draftId], previous) => {
    if (!open) {
      return;
    }

    const wasOpen = previous?.[0] ?? false;
    if (wasOpen && draftId === draft.value?.id) {
      return;
    }

    void loadDraft(draftId);
  },
  { immediate: true },
);
</script>

<template>
  <ElDrawer
    :model-value="modelValue"
    :title="title"
    class="draft-editor-drawer"
    destroy-on-close
    size="min(960px, 96vw)"
    @close="close"
  >
    <div v-loading="loading" class="drawer-content" data-testid="draft-editor">
      <ElAlert
        v-if="conflictMessage"
        :closable="false"
        show-icon
        title="The draft changed on the server"
        type="warning"
      >
        <template #default>
          <div class="alert-action-row">
            <span>{{ conflictMessage }}</span>
            <ElButton
              data-testid="reload-conflicted-draft"
              size="small"
              @click="reloadConflictedDraft"
            >
              <RotateCw :size="15" />
              Reload latest
            </ElButton>
          </div>
        </template>
      </ElAlert>

      <ElForm label-position="top">
        <div class="form-grid">
          <ElFormItem label="Draft name">
            <ElInput
              v-model="form.name"
              :disabled="readOnly"
              data-testid="draft-name"
              maxlength="128"
            />
          </ElFormItem>
          <ElFormItem label="Schema version">
            <ElInput
              v-model="form.schemaVersion"
              :disabled="readOnly"
              data-testid="schema-version"
              maxlength="32"
            />
          </ElFormItem>
          <ElFormItem v-if="isCreate" label="Base version">
            <ElSelect
              v-model="form.baseVersionId"
              clearable
              data-testid="base-version"
              placeholder="No base version"
            >
              <ElOption
                v-for="version in versionOptions"
                :key="version.id"
                :label="`${version.schemaVersion} · ${version.id.slice(0, 8)}`"
                :value="version.id"
              />
            </ElSelect>
          </ElFormItem>
        </div>

        <ElFormItem label="JSON content">
          <template #label>
            <div class="editor-label-row">
              <span>JSON content</span>
              <ElTooltip content="Format JSON" placement="top">
                <ElButton
                  :disabled="readOnly || !syntax.valid"
                  aria-label="Format JSON"
                  circle
                  size="small"
                  @click="handleFormat"
                >
                  <SquareCode :size="16" />
                </ElButton>
              </ElTooltip>
            </div>
          </template>
          <ElInput
            v-model="form.content"
            :readonly="readOnly"
            :rows="22"
            class="json-editor"
            data-testid="draft-content"
            resize="vertical"
            type="textarea"
          />
        </ElFormItem>
      </ElForm>

      <ElAlert
        v-if="!syntax.valid"
        :closable="false"
        :title="`JSON syntax: ${syntax.message}`"
        show-icon
        type="warning"
      >
        <template v-if="syntax.line" #default>
          Line {{ syntax.line }}, column {{ syntax.column }}
        </template>
      </ElAlert>

      <section v-if="draft" class="validation-section">
        <div class="section-heading">
          <div>
            <h3>Validation</h3>
            <p>Revision {{ draft.revision }}</p>
          </div>
          <ElTag
            v-if="draft.latestValidation"
            :type="draft.latestValidation.isValid ? 'success' : 'danger'"
          >
            {{ draft.latestValidation.isValid ? 'Passed' : 'Issues found' }}
          </ElTag>
          <ElTag v-else type="info">Not validated</ElTag>
        </div>

        <ElTable
          v-if="draft.latestValidation?.issues.length"
          :data="draft.latestValidation.issues"
          data-testid="validation-issues"
          max-height="260"
          stripe
        >
          <ElTableColumn label="Location" min-width="160" prop="location" />
          <ElTableColumn label="Code" min-width="180" prop="code" />
          <ElTableColumn label="Message" min-width="300" prop="message" />
          <ElTableColumn label="Position" width="110">
            <template #default="scope">
              <span v-if="scope.row.line !== null">
                {{ scope.row.line + 1 }}:{{ (scope.row.column ?? 0) + 1 }}
              </span>
              <span v-else>—</span>
            </template>
          </ElTableColumn>
        </ElTable>
      </section>

      <ElDescriptions v-if="auditReference" :column="1" size="small">
        <ElDescriptionsItem label="Audit reference">
          <code>{{ auditReference }}</code>
        </ElDescriptionsItem>
      </ElDescriptions>
    </div>

    <template #footer>
      <div class="drawer-footer">
        <ElButton @click="close">Close</ElButton>
        <template v-if="!readOnly">
          <ElButton
            :loading="saving"
            data-testid="save-draft"
            @click="saveDraft"
          >
            <Check :size="16" />
            Save
          </ElButton>
          <ElButton
            :loading="validating"
            data-testid="validate-draft"
            type="primary"
            @click="handleValidate"
          >
            <CircleCheckBig :size="16" />
            Save and validate
          </ElButton>
        </template>
        <ElButton
          v-if="draft && isValidatedRevision"
          data-testid="review-draft"
          type="success"
          @click="emit('review', draft)"
        >
          <Eye :size="16" />
          Review
        </ElButton>
      </div>
    </template>
  </ElDrawer>
</template>

<style scoped>
.drawer-content {
  display: flex;
  flex-direction: column;
  gap: 18px;
  min-height: 420px;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 16px;
}

.editor-label-row,
.section-heading,
.alert-action-row,
.drawer-footer {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
}

.json-editor :deep(textarea) {
  min-height: 360px;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 13px;
  line-height: 1.6;
}

.validation-section {
  padding-top: 18px;
  border-top: 1px solid var(--el-border-color-lighter);
}

.section-heading h3,
.section-heading p {
  margin: 0;
}

.section-heading h3 {
  font-size: 15px;
  font-weight: 600;
}

.section-heading p {
  margin-top: 3px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.drawer-footer {
  flex-wrap: wrap;
  justify-content: flex-end;
}

code {
  overflow-wrap: anywhere;
}

@media (max-width: 720px) {
  .form-grid {
    grid-template-columns: 1fr;
  }

  .editor-label-row :deep(.el-button.is-circle) {
    width: 44px;
    min-width: 44px;
    height: 44px;
  }

  .alert-action-row {
    flex-direction: column;
    align-items: flex-start;
  }
}
</style>
