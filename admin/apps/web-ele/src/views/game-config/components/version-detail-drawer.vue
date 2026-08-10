<script lang="ts" setup>
import type { GameConfigVersion } from '#/api';

import { ref, watch } from 'vue';

import { Download, RotateCw } from '@vben/icons';

import { useMediaQuery } from '@vueuse/core';
import {
  ElAlert,
  ElButton,
  ElDescriptions,
  ElDescriptionsItem,
  ElDrawer,
  ElMessage,
} from 'element-plus';

import {
  downloadGameConfigSnapshot,
  getGameConfigVersion,
  toNovaGameApiError,
} from '#/api';
import { formatJson } from '#/features/game-config/json';

interface Props {
  canActivate: boolean;
  modelValue: boolean;
  versionId: null | string;
}

const props = defineProps<Props>();
const emit = defineEmits<{
  activate: [version: GameConfigVersion];
  'update:modelValue': [value: boolean];
}>();

const loading = ref(false);
const isMobile = useMediaQuery('(max-width: 760px)');
const downloading = ref(false);
const errorMessage = ref('');
const version = ref<GameConfigVersion | null>(null);
let loadSequence = 0;

function displayContent(content: string) {
  try {
    return formatJson(content).trimEnd();
  } catch {
    return content;
  }
}

async function loadVersion() {
  const sequence = ++loadSequence;
  const versionId = props.versionId;

  version.value = null;
  errorMessage.value = '';
  if (!props.modelValue || !versionId) {
    loading.value = false;
    return;
  }
  loading.value = true;
  try {
    const loadedVersion = await getGameConfigVersion(versionId);
    if (
      sequence === loadSequence &&
      props.modelValue &&
      props.versionId === versionId
    ) {
      version.value = loadedVersion;
    }
  } catch (error) {
    if (sequence === loadSequence) {
      errorMessage.value = toNovaGameApiError(error).message;
    }
  } finally {
    if (sequence === loadSequence) {
      loading.value = false;
    }
  }
}

function readFileName(contentDisposition: string | undefined) {
  if (!contentDisposition) {
    return null;
  }
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition)?.[1];
  if (encoded) {
    return decodeURIComponent(encoded);
  }
  return /filename="?([^";]+)"?/i.exec(contentDisposition)?.[1] ?? null;
}

async function handleDownload() {
  const requestedVersion = version.value;
  if (!requestedVersion || downloading.value) {
    return;
  }
  downloading.value = true;
  try {
    const response = await downloadGameConfigSnapshot(requestedVersion.id);
    const url = URL.createObjectURL(response.data);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download =
      readFileName(response.headers['content-disposition']) ??
      `game-config-${requestedVersion.id}.json`;
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
    ElMessage.success('Snapshot download started.');
  } catch (error) {
    ElMessage.error(toNovaGameApiError(error).message);
  } finally {
    downloading.value = false;
  }
}

function close() {
  emit('update:modelValue', false);
}

watch(
  () => [props.modelValue, props.versionId] as const,
  () => {
    void loadVersion();
  },
  { immediate: true },
);
</script>

<template>
  <ElDrawer
    :model-value="modelValue"
    destroy-on-close
    size="min(900px, 96vw)"
    title="Published version"
    @close="close"
  >
    <div
      v-loading="loading"
      class="version-content"
      data-testid="version-detail"
    >
      <ElAlert
        v-if="errorMessage"
        :closable="false"
        :title="errorMessage"
        show-icon
        type="error"
      >
        <template #default>
          <ElButton size="small" @click="loadVersion">
            <RotateCw :size="15" />
            Retry
          </ElButton>
        </template>
      </ElAlert>

      <template v-if="version">
        <ElDescriptions :column="isMobile ? 1 : 2" border size="small">
          <ElDescriptionsItem label="Version ID">
            <code data-testid="version-id">{{ version.id }}</code>
          </ElDescriptionsItem>
          <ElDescriptionsItem label="Schema">
            {{ version.schemaVersion }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="Source draft">
            <code>{{ version.sourceDraftId }}</code>
          </ElDescriptionsItem>
          <ElDescriptionsItem label="Source revision">
            {{ version.sourceDraftRevision }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="Content hash">
            <code>{{ version.contentHash }}</code>
          </ElDescriptionsItem>
          <ElDescriptionsItem label="Snapshot hash">
            <code>{{ version.snapshotHash }}</code>
          </ElDescriptionsItem>
          <ElDescriptionsItem label="Published at">
            {{ new Date(version.publishedAtUtc).toLocaleString() }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="Snapshot size">
            {{ version.snapshotLength.toLocaleString() }} bytes
          </ElDescriptionsItem>
          <ElDescriptionsItem :span="isMobile ? 1 : 2" label="Release notes">
            {{ version.releaseNotes }}
          </ElDescriptionsItem>
        </ElDescriptions>

        <section>
          <h3>Canonical content</h3>
          <pre>{{ displayContent(version.content) }}</pre>
        </section>
      </template>
    </div>

    <template #footer>
      <div class="drawer-footer">
        <ElButton @click="close">Close</ElButton>
        <ElButton
          v-if="version"
          :loading="downloading"
          data-testid="download-snapshot"
          @click="handleDownload"
        >
          <Download :size="16" />
          Download snapshot
        </ElButton>
        <ElButton
          v-if="version && version.id === versionId && canActivate && !loading"
          data-testid="activate-from-detail"
          type="primary"
          @click="emit('activate', version)"
        >
          Activate
        </ElButton>
      </div>
    </template>
  </ElDrawer>
</template>

<style scoped>
.version-content {
  display: flex;
  flex-direction: column;
  gap: 20px;
  min-height: 420px;
}

h3 {
  margin: 0 0 10px;
  font-size: 15px;
}

pre {
  max-height: 480px;
  padding: 14px;
  margin: 0;
  overflow: auto;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 12px;
  line-height: 1.6;
  background: var(--el-fill-color-lighter);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
}

code {
  overflow-wrap: anywhere;
}

.drawer-footer {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  justify-content: flex-end;
}
</style>
