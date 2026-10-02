<script lang="ts" setup>
import type { GameConfigDraft, GameConfigVersion } from '#/api';

import { computed, ref, watch } from 'vue';

import { Check, RotateCw } from '@vben/icons';

import { useMediaQuery } from '@vueuse/core';
import {
  ElAlert,
  ElButton,
  ElDescriptions,
  ElDescriptionsItem,
  ElDrawer,
  ElEmpty,
  ElInput,
  ElMessage,
  ElTag,
} from 'element-plus';

import {
  getGameConfigEnvironment,
  getGameConfigVersion,
  publishGameConfigDraft,
  toNovaGameApiError,
} from '#/api';
import { buildJsonReview, isLineChanged } from '#/features/game-config/json';
import {
  operationsErrorMessage,
  tOperations,
  useOperationsMessage,
} from '#/locales/operations';

interface Props {
  canPublish: boolean;
  draft: GameConfigDraft | null;
  environment: string;
  modelValue: boolean;
}

const props = defineProps<Props>();
const emit = defineEmits<{
  conflict: [draftId: string];
  published: [version: GameConfigVersion];
  'update:modelValue': [value: boolean];
}>();

const loading = ref(false);
const isMobile = useMediaQuery('(max-width: 760px)');
const publishing = ref(false);
const baseline = ref<null | { environment?: string; id: string }>(null);
const baselineLabel = computed(() => {
  if (!baseline.value) return tOperations('noBaseline');
  return baseline.value.environment
    ? tOperations('activeParameterParameter', {
        value0: baseline.value.environment,
        value1: baseline.value.id,
      })
    : tOperations('baseParameter', { value0: baseline.value.id });
});
const baselineContent = ref<null | string>(null);
const releaseNotes = ref('');
const errorMessage = useOperationsMessage();
const auditReference = ref('');
const review = computed(() =>
  buildJsonReview(baselineContent.value, props.draft?.content ?? ''),
);

async function loadBaseline() {
  if (!props.draft) {
    return;
  }

  loading.value = true;
  errorMessage.value = '';
  baselineContent.value = null;
  baseline.value = null;
  try {
    if (props.draft.baseVersionId) {
      const version = await getGameConfigVersion(props.draft.baseVersionId);
      baselineContent.value = version.content;
      baseline.value = { id: version.id.slice(0, 8) };
      return;
    }

    try {
      const environment = await getGameConfigEnvironment(
        props.environment,
        props.draft.configurationSet,
      );
      const version = await getGameConfigVersion(environment.activeVersionId);
      baselineContent.value = version.content;
      baseline.value = {
        environment: props.environment,
        id: version.id.slice(0, 8),
      };
    } catch (error) {
      if (toNovaGameApiError(error).status !== 404) {
        throw error;
      }
    }
  } catch (error) {
    errorMessage.value = () => operationsErrorMessage(error);
  } finally {
    loading.value = false;
  }
}

async function handlePublish() {
  if (!props.draft || !props.canPublish) {
    return;
  }
  if (!releaseNotes.value.trim()) {
    ElMessage.warning(tOperations('releaseNotesAreRequired'));
    return;
  }

  publishing.value = true;
  errorMessage.value = '';
  try {
    const result = await publishGameConfigDraft(props.draft.id, {
      expectedRevision: props.draft.revision,
      releaseNotes: releaseNotes.value.trim(),
    });
    auditReference.value = result.correlationId;
    emit('published', result.data);
    ElMessage.success(tOperations('configurationVersionPublished'));
    emit('update:modelValue', false);
  } catch (error) {
    const apiError = toNovaGameApiError(error);
    errorMessage.value = () => operationsErrorMessage(apiError);
    if (apiError.status === 409) {
      emit('conflict', props.draft.id);
    }
  } finally {
    publishing.value = false;
  }
}

function close() {
  emit('update:modelValue', false);
}

watch(
  () => [props.modelValue, props.draft?.id] as const,
  ([open]) => {
    if (open) {
      releaseNotes.value = '';
      auditReference.value = '';
      void loadBaseline();
    }
  },
  { immediate: true },
);
</script>

<template>
  <ElDrawer
    :model-value="modelValue"
    destroy-on-close
    size="min(1100px, 98vw)"
    :title="tOperations('reviewAndPublish')"
    @close="close"
  >
    <div v-loading="loading" class="review-content" data-testid="review-drawer">
      <ElAlert
        v-if="errorMessage"
        :closable="false"
        :title="errorMessage"
        show-icon
        type="warning"
      >
        <template #default>
          <ElButton size="small" @click="loadBaseline">
            <RotateCw :size="15" />
            {{ tOperations('reload') }}
          </ElButton>
        </template>
      </ElAlert>

      <ElDescriptions
        v-if="draft"
        :column="isMobile ? 1 : 3"
        border
        size="small"
      >
        <ElDescriptionsItem :label="tOperations('draft')">
          {{ draft.name }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="tOperations('revision')">
          {{ draft.revision }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="tOperations('schema')">
          {{ draft.schemaVersion }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="tOperations('configurationSet')">
          {{ draft.configurationSet }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="tOperations('baseline')">
          {{ baselineLabel }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="tOperations('validation')">
          <ElTag type="success">{{ tOperations('passed') }}</ElTag>
        </ElDescriptionsItem>
      </ElDescriptions>

      <section class="diff-section">
        <div class="section-heading">
          <div>
            <h3>{{ tOperations('contentComparison') }}</h3>
            <p>{{ baselineLabel }}</p>
          </div>
          <div class="diff-legend">
            <span class="legend-mark before"></span>{{ tOperations('removed') }}
            <span class="legend-mark after"></span>{{ tOperations('added') }}
          </div>
        </div>

        <div class="diff-grid">
          <div class="diff-pane">
            <div class="pane-title">{{ tOperations('before') }}</div>
            <ElEmpty
              v-if="review.beforeLines.length === 0"
              :image-size="50"
              :description="tOperations('noBaselineVersion')"
            />
            <ol v-else>
              <li
                v-for="(line, index) in review.beforeLines"
                :key="`before-${index}`"
                :class="{ changed: isLineChanged(review.changedBefore, index) }"
              >
                <code>{{ line || ' ' }}</code>
              </li>
            </ol>
          </div>
          <div class="diff-pane after-pane">
            <div class="pane-title">{{ tOperations('candidate') }}</div>
            <ol>
              <li
                v-for="(line, index) in review.afterLines"
                :key="`after-${index}`"
                :class="{ changed: isLineChanged(review.changedAfter, index) }"
              >
                <code>{{ line || ' ' }}</code>
              </li>
            </ol>
          </div>
        </div>
      </section>

      <section class="release-section">
        <label for="release-notes">{{ tOperations('releaseNotes') }}</label>
        <ElInput
          id="release-notes"
          v-model="releaseNotes"
          :disabled="!canPublish"
          :rows="4"
          data-testid="release-notes"
          maxlength="2000"
          show-word-limit
          type="textarea"
        />
      </section>

      <ElDescriptions v-if="auditReference" :column="1" size="small">
        <ElDescriptionsItem :label="tOperations('auditReference')">
          <code>{{ auditReference }}</code>
        </ElDescriptionsItem>
      </ElDescriptions>
    </div>

    <template #footer>
      <div class="drawer-footer">
        <ElButton @click="close">{{ tOperations('close') }}</ElButton>
        <ElButton
          v-if="canPublish"
          :loading="publishing"
          data-testid="publish-draft"
          type="primary"
          @click="handlePublish"
        >
          <Check :size="16" />
          {{ tOperations('publishImmutableVersion') }}
        </ElButton>
      </div>
    </template>
  </ElDrawer>
</template>

<style scoped>
.review-content {
  display: flex;
  flex-direction: column;
  gap: 20px;
  min-height: 500px;
}

.section-heading,
.drawer-footer,
.diff-legend {
  display: flex;
  gap: 12px;
  align-items: center;
}

.section-heading {
  justify-content: space-between;
}

.section-heading h3,
.section-heading p {
  margin: 0;
}

.section-heading h3 {
  font-size: 15px;
  font-weight: 600;
}

.section-heading p,
.diff-legend {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.diff-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  margin-top: 10px;
  overflow: hidden;
  border: 1px solid var(--el-border-color);
  border-radius: 6px;
}

.diff-pane {
  min-width: 0;
  background: var(--el-fill-color-lighter);
}

.after-pane {
  border-left: 1px solid var(--el-border-color);
}

.pane-title {
  padding: 9px 12px;
  font-size: 12px;
  font-weight: 600;
  background: var(--el-bg-color);
  border-bottom: 1px solid var(--el-border-color);
}

ol {
  max-height: 420px;
  padding: 8px 0 8px 48px;
  margin: 0;
  overflow: auto;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 12px;
  line-height: 1.55;
  color: var(--el-text-color-secondary);
}

li {
  min-height: 19px;
  padding: 0 10px;
  white-space: pre;
}

.diff-pane li.changed {
  background: color-mix(in srgb, var(--el-color-danger) 14%, transparent);
}

.after-pane li.changed {
  background: color-mix(in srgb, var(--el-color-success) 16%, transparent);
}

.legend-mark {
  width: 11px;
  height: 11px;
  border-radius: 2px;
}

.legend-mark.before {
  background: color-mix(in srgb, var(--el-color-danger) 35%, transparent);
}

.legend-mark.after {
  background: color-mix(in srgb, var(--el-color-success) 40%, transparent);
}

.release-section label {
  display: block;
  margin-bottom: 8px;
  font-size: 13px;
  font-weight: 600;
}

.drawer-footer {
  justify-content: flex-end;
}

@media (max-width: 760px) {
  .diff-grid {
    grid-template-columns: 1fr;
  }

  .after-pane {
    border-top: 1px solid var(--el-border-color);
    border-left: 0;
  }
}
</style>
