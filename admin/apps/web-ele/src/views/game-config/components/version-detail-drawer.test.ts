/* eslint-disable vue/one-component-per-file -- Test stubs intentionally share this focused spec. */

import type { GameConfigVersion } from '#/api';

import { flushPromises, mount } from '@vue/test-utils';
import { defineComponent, h, ref } from 'vue';

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import VersionDetailDrawer from './version-detail-drawer.vue';

const api = vi.hoisted(() => ({
  downloadGameConfigSnapshot: vi.fn(),
  getGameConfigVersion: vi.fn(),
}));

const createObjectUrl = vi.fn(() => 'blob:snapshot');
const revokeObjectUrl = vi.fn();
let downloadedAnchors: Array<{ download: string; href: string }> = [];

vi.mock('#/api', () => ({
  ...api,
  toNovaGameApiError: (error: unknown) => error,
}));

vi.mock('@vben/icons', async () => {
  const Icon = defineComponent({
    setup: () => () => h('span'),
  });

  return {
    Download: Icon,
    RotateCw: Icon,
  };
});

vi.mock('@vueuse/core', () => ({
  useMediaQuery: () => ref(false),
}));

vi.mock('element-plus', async () => {
  const Container = defineComponent({
    inheritAttrs: false,
    setup(_, { attrs, slots }) {
      return () => h('div', attrs, slots.default?.());
    },
  });
  const ElAlert = defineComponent({
    inheritAttrs: false,
    props: {
      title: { default: '', type: String },
    },
    setup(props, { attrs, slots }) {
      return () =>
        h('div', attrs, [h('strong', props.title), slots.default?.()]);
    },
  });
  const ElButton = defineComponent({
    inheritAttrs: false,
    props: {
      loading: Boolean,
    },
    emits: ['click'],
    setup(props, { attrs, emit, slots }) {
      return () =>
        h(
          'button',
          {
            ...attrs,
            disabled: props.loading,
            onClick: (event: MouseEvent) => emit('click', event),
          },
          slots.default?.(),
        );
    },
  });
  const ElDrawer = defineComponent({
    inheritAttrs: false,
    props: {
      modelValue: Boolean,
      title: { default: '', type: String },
    },
    setup(props, { attrs, slots }) {
      return () =>
        props.modelValue
          ? h('section', attrs, [
              h('h2', props.title),
              slots.default?.(),
              slots.footer?.(),
            ])
          : null;
    },
  });

  return {
    ElAlert,
    ElButton,
    ElDescriptions: Container,
    ElDescriptionsItem: Container,
    ElDrawer,
    ElMessage: {
      error: vi.fn(),
      success: vi.fn(),
    },
  };
});

function createVersion(id: string): GameConfigVersion {
  return {
    configurationSet: 'default',
    content: '{"collections":{}}',
    contentHash: 'a'.repeat(64),
    id,
    publishedAtUtc: '2026-08-02T00:00:00Z',
    publishedByAdministratorId: 'administrator-id',
    releaseNotes: `${id} release`,
    schemaVersion: '1',
    snapshotHash: 'b'.repeat(64),
    snapshotLength: 128,
    sourceDraftId: `${id}-draft`,
    sourceDraftRevision: 1,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolver) => {
    resolve = resolver;
  });
  return { promise, resolve };
}

function mountDrawer(versionId: string) {
  return mount(VersionDetailDrawer, {
    global: {
      directives: {
        loading: () => undefined,
      },
    },
    props: {
      canActivate: true,
      modelValue: true,
      versionId,
    },
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  downloadedAnchors = [];
  Object.defineProperties(URL, {
    createObjectURL: {
      configurable: true,
      value: createObjectUrl,
    },
    revokeObjectURL: {
      configurable: true,
      value: revokeObjectUrl,
    },
  });
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(
    function (this: HTMLAnchorElement) {
      downloadedAnchors.push({ download: this.download, href: this.href });
    },
  );
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('version detail drawer', () => {
  it('hides the retained version until the newly requested version loads', async () => {
    const firstVersion = createVersion('first-version');
    const secondVersion = createVersion('second-version');
    const secondLoad = deferred<GameConfigVersion>();
    api.getGameConfigVersion
      .mockResolvedValueOnce(firstVersion)
      .mockReturnValueOnce(secondLoad.promise);

    const wrapper = mountDrawer(firstVersion.id);
    await flushPromises();

    expect(wrapper.get('[data-testid="version-id"]').text()).toBe(
      firstVersion.id,
    );
    expect(wrapper.find('[data-testid="activate-from-detail"]').exists()).toBe(
      true,
    );

    await wrapper.setProps({ modelValue: false });
    await wrapper.setProps({
      modelValue: true,
      versionId: secondVersion.id,
    });

    expect(wrapper.find('[data-testid="version-id"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="activate-from-detail"]').exists()).toBe(
      false,
    );

    secondLoad.resolve(secondVersion);
    await flushPromises();

    expect(wrapper.get('[data-testid="version-id"]').text()).toBe(
      secondVersion.id,
    );
    await wrapper.get('[data-testid="activate-from-detail"]').trigger('click');
    expect(wrapper.emitted('activate')).toEqual([[secondVersion]]);
  });

  it('ignores an older request that resolves after the current version', async () => {
    const firstVersion = createVersion('first-version');
    const secondVersion = createVersion('second-version');
    const firstLoad = deferred<GameConfigVersion>();
    const secondLoad = deferred<GameConfigVersion>();
    api.getGameConfigVersion
      .mockReturnValueOnce(firstLoad.promise)
      .mockReturnValueOnce(secondLoad.promise);

    const wrapper = mountDrawer(firstVersion.id);
    await wrapper.setProps({ versionId: secondVersion.id });

    secondLoad.resolve(secondVersion);
    await flushPromises();
    expect(wrapper.get('[data-testid="version-id"]').text()).toBe(
      secondVersion.id,
    );

    firstLoad.resolve(firstVersion);
    await flushPromises();
    expect(wrapper.get('[data-testid="version-id"]').text()).toBe(
      secondVersion.id,
    );
  });

  it('finishes a pending download with the version captured at request time', async () => {
    const firstVersion = createVersion('first-version');
    const secondVersion = createVersion('second-version');
    const download = deferred<{
      data: Blob;
      headers: Record<string, string>;
    }>();
    api.getGameConfigVersion
      .mockResolvedValueOnce(firstVersion)
      .mockResolvedValueOnce(secondVersion);
    api.downloadGameConfigSnapshot.mockReturnValueOnce(download.promise);

    const wrapper = mountDrawer(firstVersion.id);
    await flushPromises();
    await wrapper.get('[data-testid="download-snapshot"]').trigger('click');

    await wrapper.setProps({ versionId: secondVersion.id });
    await flushPromises();
    expect(wrapper.get('[data-testid="version-id"]').text()).toBe(
      secondVersion.id,
    );

    download.resolve({ data: new Blob(['{}']), headers: {} });
    await flushPromises();

    expect(api.downloadGameConfigSnapshot).toHaveBeenCalledWith(
      firstVersion.id,
    );
    expect(downloadedAnchors).toEqual([
      {
        download: `game-config-${firstVersion.id}.json`,
        href: 'blob:snapshot',
      },
    ]);
    expect(revokeObjectUrl).toHaveBeenCalledWith('blob:snapshot');
  });

  it('ignores another download while the first request is pending', async () => {
    const currentVersion = createVersion('current-version');
    const download = deferred<{
      data: Blob;
      headers: Record<string, string>;
    }>();
    api.getGameConfigVersion.mockResolvedValueOnce(currentVersion);
    api.downloadGameConfigSnapshot.mockReturnValueOnce(download.promise);

    const wrapper = mountDrawer(currentVersion.id);
    await flushPromises();
    const button = wrapper.get('[data-testid="download-snapshot"]');
    await Promise.all([button.trigger('click'), button.trigger('click')]);

    expect(api.downloadGameConfigSnapshot).toHaveBeenCalledTimes(1);

    download.resolve({
      data: new Blob(['{}']),
      headers: {
        'content-disposition': 'attachment; filename="snapshot.json"',
      },
    });
    await flushPromises();

    expect(downloadedAnchors).toEqual([
      { download: 'snapshot.json', href: 'blob:snapshot' },
    ]);
  });
});
