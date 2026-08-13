import type {
  PlayerEquipmentGrantHistoryItem,
  PlayerEquipmentGrantMutationSnapshot,
  PlayerEquipmentGrantPageSnapshot,
} from '#/features/player-equipment-grants/state';

import { flushPromises, mount } from '@vue/test-utils';
import { nextTick } from 'vue';

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const testState = vi.hoisted(() => ({
  mediaMatches: false,
  transport: {
    getPage: vi.fn(),
    grant: vi.fn(),
    rollback: vi.fn(),
  },
}));

vi.mock('#/api/player-equipment-grants', () => ({
  playerEquipmentGrantTransport: testState.transport,
}));

vi.mock('@vben/icons', async () => {
  const { defineComponent, h } = await import('vue');
  const Icon = defineComponent({
    setup: () => () => h('span'),
  });

  return {
    Plus: Icon,
    RotateCw: Icon,
    Search: Icon,
    Undo2: Icon,
  };
});

vi.mock('@vben/stores', async () => {
  const { reactive } = await import('vue');
  const accessStore = reactive({ accessCodes: [] as string[] });

  return {
    useAccessStore: () => accessStore,
  };
});

vi.mock('@vueuse/core', async () => {
  const { ref } = await import('vue');

  return {
    useMediaQuery: () => ref(testState.mediaMatches),
  };
});

vi.mock('element-plus', async () => {
  const { computed, defineComponent, h, inject, provide, ref } =
    await import('vue');
  const tableRowsKey = Symbol('table-rows');

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
        h('section', attrs, [h('strong', props.title), slots.default?.()]);
    },
  });
  const ElButton = defineComponent({
    inheritAttrs: false,
    props: {
      disabled: Boolean,
      loading: Boolean,
      nativeType: { default: 'button', type: String },
    },
    emits: ['click'],
    setup(props, { attrs, emit, slots }) {
      return () =>
        h(
          'button',
          {
            ...attrs,
            disabled: props.disabled || props.loading,
            onClick: (event: MouseEvent) => emit('click', event),
            type: props.nativeType,
          },
          slots.default?.(),
        );
    },
  });
  const ElDescriptionsItem = defineComponent({
    inheritAttrs: false,
    props: {
      label: { default: '', type: String },
    },
    setup(props, { attrs, slots }) {
      return () =>
        h('div', attrs, [
          h('span', { class: 'description-label' }, props.label),
          slots.default?.(),
        ]);
    },
  });
  const ElDialog = defineComponent({
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
  const ElEmpty = defineComponent({
    inheritAttrs: false,
    props: {
      description: { default: '', type: String },
    },
    setup(props, { attrs }) {
      return () => h('div', attrs, props.description);
    },
  });
  const ElFormItem = defineComponent({
    inheritAttrs: false,
    props: {
      label: { default: '', type: String },
    },
    setup(props, { attrs, slots }) {
      return () =>
        h('label', attrs, [h('span', props.label), slots.default?.()]);
    },
  });
  const ElInput = defineComponent({
    inheritAttrs: false,
    props: {
      disabled: Boolean,
      modelValue: { default: '', type: String },
      type: { default: 'text', type: String },
    },
    emits: ['update:modelValue'],
    setup(props, { attrs, emit }) {
      return () =>
        h(props.type === 'textarea' ? 'textarea' : 'input', {
          ...attrs,
          disabled: props.disabled,
          onInput: (event: Event) =>
            emit('update:modelValue', (event.target as HTMLInputElement).value),
          value: props.modelValue,
        });
    },
  });
  const ElPagination = defineComponent({
    inheritAttrs: false,
    props: {
      disabled: Boolean,
      layout: { default: '', type: String },
    },
    setup(props, { attrs }) {
      return () =>
        h('div', {
          ...attrs,
          'data-layout': props.layout,
          'data-disabled': String(props.disabled),
        });
    },
  });
  const ElResult = defineComponent({
    inheritAttrs: false,
    props: {
      subTitle: { default: '', type: String },
      title: { default: '', type: String },
    },
    setup(props, { attrs }) {
      return () =>
        h('section', attrs, [h('h2', props.title), h('p', props.subTitle)]);
    },
  });
  const ElTable = defineComponent({
    inheritAttrs: false,
    props: {
      data: { default: () => [], type: Array },
    },
    setup(props, { attrs, slots }) {
      provide(
        tableRowsKey,
        computed(() => props.data),
      );
      return () => h('div', attrs, slots.default?.());
    },
  });
  const ElTableColumn = defineComponent({
    props: {
      label: { default: '', type: String },
      prop: { default: '', type: String },
    },
    setup(props, { slots }) {
      const rows = inject<{ value: Array<Record<string, unknown>> }>(
        tableRowsKey,
        ref([]),
      );
      return () =>
        h('section', { 'data-column': props.label }, [
          h('h3', props.label),
          ...rows.value.map((row) =>
            h(
              'div',
              { class: 'table-row' },
              slots.default?.({ row }) ?? String(row[props.prop]),
            ),
          ),
        ]);
    },
  });

  return {
    ElAlert,
    ElButton,
    ElDescriptions: Container,
    ElDescriptionsItem,
    ElDialog,
    ElEmpty,
    ElForm: Container,
    ElFormItem,
    ElInput,
    ElPagination,
    ElResult,
    ElTable,
    ElTableColumn,
    ElTag: Container,
    ElTooltip: Container,
  };
});

import { useAccessStore } from '@vben/stores';

import { PlayerEquipmentGrantPermission } from '#/features/player-equipment-grants/permissions';

import PlayerEquipmentGrants from './index.vue';

const administratorId = '66666666-6666-6666-6666-666666666666';
const configurationVersionId = '55555555-5555-5555-5555-555555555555';
const grantId = '33333333-3333-3333-3333-333333333333';
const idempotencyKey = '77777777-7777-7777-7777-777777777777';
const ownedItemId = '44444444-4444-4444-4444-444444444444';
const playerId = '11111111-1111-1111-1111-111111111111';

interface Deferred<T> {
  promise: Promise<T>;
  resolve: (value: T) => void;
}

function createDeferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise;
  });
  return { promise, resolve };
}

function createHistoryItem(
  overrides: Partial<PlayerEquipmentGrantHistoryItem> = {},
): PlayerEquipmentGrantHistoryItem {
  return {
    configurationVersionId,
    configuredItemId: 'starter-sword',
    grantId,
    grantReason: 'Support correction',
    grantResultingRevision: 7,
    grantedAtUtc: '2026-08-12T00:00:00Z',
    grantedByAdministratorId: administratorId,
    ownedItemId,
    playerId,
    rollbackReason: null,
    rollbackResultingRevision: null,
    rolledBackAtUtc: null,
    rolledBackByAdministratorId: null,
    status: 1,
    ...overrides,
  };
}

function createPageSnapshot({
  etag,
  items = [createHistoryItem()],
  page = 1,
  pageSize = 20,
  player = playerId,
  revision = 7,
  totalCount = items.length,
}: {
  etag?: string;
  items?: PlayerEquipmentGrantHistoryItem[];
  page?: number;
  pageSize?: number;
  player?: string;
  revision?: number;
  totalCount?: number;
} = {}): PlayerEquipmentGrantPageSnapshot {
  return {
    data: {
      items,
      page,
      pageSize,
      playerId: player,
      revision,
      totalCount,
    },
    etag: etag ?? `"${revision}"`,
  };
}

function createMutationSnapshot(
  status: 1 | 2 = 1,
  revision: number = 8,
): PlayerEquipmentGrantMutationSnapshot {
  const rollbackEvidence =
    status === 2
      ? {
          rollbackReason: 'Support rollback',
          rolledBackAtUtc: '2026-08-12T00:05:00Z',
          rolledBackByAdministratorId: administratorId,
        }
      : {
          rollbackReason: null,
          rolledBackAtUtc: null,
          rolledBackByAdministratorId: null,
        };
  return {
    data: {
      configurationVersionId,
      configuredItemId: 'starter-sword',
      grantId,
      grantReason: 'Support correction',
      grantedAtUtc: '2026-08-12T00:00:00Z',
      grantedByAdministratorId: administratorId,
      ownedItemId,
      playerId,
      resultingRevision: revision,
      status,
      ...rollbackEvidence,
    },
    etag: `"${revision}"`,
  };
}

function apiError(
  status: number,
  code: string,
  message: string,
  details?: Record<string, string[]>,
): Error {
  return Object.assign(new Error(message), { code, details, status });
}

function allowManagePermission(): void {
  useAccessStore().accessCodes = [PlayerEquipmentGrantPermission.Manage];
}

function mountWorkspace() {
  return mount(PlayerEquipmentGrants, {
    global: {
      directives: {
        loading: () => undefined,
      },
    },
  });
}

async function settleView(): Promise<void> {
  await flushPromises();
  await nextTick();
  await flushPromises();
}

async function lookup(
  wrapper: ReturnType<typeof mountWorkspace>,
  value: string = playerId,
): Promise<void> {
  await wrapper.get('[data-testid="player-id-input"]').setValue(value);
  await wrapper.get('form.lookup-controls').trigger('submit');
  await settleView();
}

beforeEach(() => {
  testState.mediaMatches = false;
  testState.transport.getPage.mockReset();
  testState.transport.grant.mockReset();
  testState.transport.rollback.mockReset();
  useAccessStore().accessCodes = [];
  vi.stubGlobal('crypto', {
    randomUUID: vi.fn(() => idempotencyKey),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  document.body.innerHTML = '';
});

describe('player equipment grants workspace', () => {
  it('renders the forbidden state and prevents lookup without the Manage permission', async () => {
    const wrapper = mountWorkspace();

    expect(
      wrapper.get('[data-testid="equipment-grants-forbidden"]').text(),
    ).toContain('Forbidden');
    expect(
      (
        wrapper.get('[data-testid="lookup-player"]')
          .element as HTMLButtonElement
      ).disabled,
    ).toBe(true);

    await wrapper.get('form.lookup-controls').trigger('submit');
    expect(testState.transport.getPage).not.toHaveBeenCalled();
  });

  it('shows local exact-player validation without calling the transport', async () => {
    allowManagePermission();
    const wrapper = mountWorkspace();

    await lookup(wrapper, 'not-a-guid');

    expect(
      wrapper.get('[data-testid="equipment-grants-error"]').text(),
    ).toContain('The player identifier must be a non-empty D-format GUID.');
    expect(testState.transport.getPage).not.toHaveBeenCalled();
  });

  it('renders authoritative ready history and compact mobile pagination', async () => {
    testState.mediaMatches = true;
    allowManagePermission();
    const rolledBack = createHistoryItem({
      grantId: '99999999-9999-9999-9999-999999999999',
      rollbackReason: 'Support rollback',
      rollbackResultingRevision: 8,
      rolledBackAtUtc: '2026-08-12T00:05:00Z',
      rolledBackByAdministratorId: administratorId,
      status: 2,
    });
    testState.transport.getPage.mockResolvedValue(
      createPageSnapshot({
        items: [createHistoryItem(), rolledBack],
        totalCount: 2,
      }),
    );
    const wrapper = mountWorkspace();

    await lookup(wrapper, playerId.toUpperCase());

    expect(wrapper.get('[data-testid="resolved-player-id"]').text()).toBe(
      playerId,
    );
    expect(wrapper.get('[data-testid="current-revision"]').text()).toBe('7');
    expect(
      wrapper.get('[data-testid="equipment-grants-history"]').text(),
    ).toContain('starter-sword');
    expect(wrapper.text()).toContain('Rolled back');
    expect(
      wrapper.findAll('[data-testid="open-rollback-dialog"]'),
    ).toHaveLength(1);
    expect(
      wrapper
        .get('[data-testid="history-pagination"]')
        .attributes('data-layout'),
    ).toBe('prev, pager, next');
  });

  it('renders an empty exact-player history while retaining the grant command', async () => {
    allowManagePermission();
    testState.transport.getPage.mockResolvedValue(
      createPageSnapshot({ items: [], totalCount: 0 }),
    );
    const wrapper = mountWorkspace();

    await lookup(wrapper);

    expect(
      wrapper.get('[data-testid="equipment-grants-empty"]').text(),
    ).toContain('No equipment grants were recorded for this player.');
    expect(
      (
        wrapper.get('[data-testid="open-grant-dialog"]')
          .element as HTMLButtonElement
      ).disabled,
    ).toBe(false);
  });

  it('requires grant confirmation and refreshes authoritative history after success', async () => {
    allowManagePermission();
    testState.transport.getPage
      .mockResolvedValueOnce(createPageSnapshot())
      .mockResolvedValueOnce(createPageSnapshot({ revision: 8 }));
    testState.transport.grant.mockResolvedValue(createMutationSnapshot());
    const wrapper = mountWorkspace();

    await lookup(wrapper);
    await wrapper.get('[data-testid="open-grant-dialog"]').trigger('click');
    expect(wrapper.text()).toContain('Confirm equipment grant');
    expect(testState.transport.grant).not.toHaveBeenCalled();

    await wrapper
      .get('[data-testid="grant-configured-item-id"]')
      .setValue('starter-sword');
    await wrapper
      .get('[data-testid="grant-reason"]')
      .setValue('Support correction');
    await wrapper.get('[data-testid="confirm-grant"]').trigger('click');
    await settleView();

    expect(testState.transport.grant).toHaveBeenCalledWith(
      {
        configuredItemId: 'starter-sword',
        idempotencyKey,
        ifMatch: '"7"',
        playerId,
        reason: 'Support correction',
      },
      undefined,
    );
    expect(testState.transport.getPage).toHaveBeenCalledTimes(2);
    expect(wrapper.get('[data-testid="current-revision"]').text()).toBe('8');
    expect(wrapper.text()).not.toContain('Confirm equipment grant');
  });

  it('requires a separate rollback reason before sending the selected active grant', async () => {
    allowManagePermission();
    const rolledBack = createHistoryItem({
      rollbackReason: 'Support rollback',
      rollbackResultingRevision: 8,
      rolledBackAtUtc: '2026-08-12T00:05:00Z',
      rolledBackByAdministratorId: administratorId,
      status: 2,
    });
    testState.transport.getPage
      .mockResolvedValueOnce(createPageSnapshot())
      .mockResolvedValueOnce(
        createPageSnapshot({ items: [rolledBack], revision: 8 }),
      );
    testState.transport.rollback.mockResolvedValue(createMutationSnapshot(2));
    const wrapper = mountWorkspace();

    await lookup(wrapper);
    await wrapper.get('[data-testid="open-rollback-dialog"]').trigger('click');
    const confirm = wrapper.get('[data-testid="confirm-rollback"]');
    expect((confirm.element as HTMLButtonElement).disabled).toBe(true);

    await wrapper
      .get('[data-testid="rollback-reason"]')
      .setValue('Support rollback');
    await confirm.trigger('click');
    await settleView();

    expect(testState.transport.rollback).toHaveBeenCalledWith(
      {
        grantId,
        idempotencyKey,
        ifMatch: '"7"',
        playerId,
        reason: 'Support rollback',
      },
      undefined,
    );
    expect(wrapper.text()).toContain('Rolled back');
    expect(wrapper.find('[data-testid="open-rollback-dialog"]').exists()).toBe(
      false,
    );
  });

  it('allows only one in-flight mutation and disables competing controls', async () => {
    allowManagePermission();
    const pendingGrant = createDeferred<PlayerEquipmentGrantMutationSnapshot>();
    testState.transport.getPage
      .mockResolvedValueOnce(createPageSnapshot())
      .mockResolvedValueOnce(createPageSnapshot({ revision: 8 }));
    testState.transport.grant.mockReturnValueOnce(pendingGrant.promise);
    const wrapper = mountWorkspace();

    await lookup(wrapper);
    await wrapper.get('[data-testid="open-grant-dialog"]').trigger('click');
    await wrapper
      .get('[data-testid="grant-configured-item-id"]')
      .setValue('starter-sword');
    await wrapper
      .get('[data-testid="grant-reason"]')
      .setValue('Support correction');
    const confirm = wrapper.get('[data-testid="confirm-grant"]');
    await confirm.trigger('click');
    await settleView();
    await confirm.trigger('click');

    expect(testState.transport.grant).toHaveBeenCalledTimes(1);
    expect(
      (
        wrapper.get('[data-testid="lookup-player"]')
          .element as HTMLButtonElement
      ).disabled,
    ).toBe(true);
    expect(wrapper.get('[data-testid="operation-progress"]').text()).toContain(
      'Granting equipment',
    );

    pendingGrant.resolve(createMutationSnapshot());
    await settleView();
    expect(wrapper.get('[data-testid="current-revision"]').text()).toBe('8');
  });

  it('keeps refreshed history visible when a concurrency conflict occurs', async () => {
    allowManagePermission();
    testState.transport.getPage
      .mockResolvedValueOnce(createPageSnapshot())
      .mockResolvedValueOnce(createPageSnapshot({ revision: 8 }));
    testState.transport.grant.mockRejectedValue(
      apiError(
        409,
        'SwordMaster.PlayerAssets.EquipmentGrants.ConcurrencyConflict',
        'The player assets revision is stale.',
      ),
    );
    const wrapper = mountWorkspace();

    await lookup(wrapper);
    await wrapper.get('[data-testid="open-grant-dialog"]').trigger('click');
    await wrapper
      .get('[data-testid="grant-configured-item-id"]')
      .setValue('starter-sword');
    await wrapper
      .get('[data-testid="grant-reason"]')
      .setValue('Support correction');
    await wrapper.get('[data-testid="confirm-grant"]').trigger('click');
    await settleView();

    expect(
      wrapper.get('[data-testid="equipment-grants-error"]').text(),
    ).toContain('The player assets revision is stale.');
    expect(wrapper.text()).toContain(
      'SwordMaster.PlayerAssets.EquipmentGrants.ConcurrencyConflict',
    );
    expect(wrapper.get('[data-testid="current-revision"]').text()).toBe('8');
    expect(testState.transport.getPage).toHaveBeenCalledTimes(2);
  });

  it('retries an ambiguous grant with the exact original request', async () => {
    allowManagePermission();
    testState.transport.getPage
      .mockResolvedValueOnce(createPageSnapshot())
      .mockResolvedValueOnce(createPageSnapshot({ revision: 8 }));
    testState.transport.grant
      .mockRejectedValueOnce(
        apiError(
          0,
          'NovaGame.NetworkError',
          'The network request was interrupted.',
        ),
      )
      .mockResolvedValueOnce(createMutationSnapshot());
    const wrapper = mountWorkspace();

    await lookup(wrapper);
    await wrapper.get('[data-testid="open-grant-dialog"]').trigger('click');
    await wrapper
      .get('[data-testid="grant-configured-item-id"]')
      .setValue('starter-sword');
    await wrapper
      .get('[data-testid="grant-reason"]')
      .setValue('Support correction');
    await wrapper.get('[data-testid="confirm-grant"]').trigger('click');
    await settleView();
    const originalRequest = testState.transport.grant.mock.calls[0]?.[0];

    expect(wrapper.get('[data-testid="retry-operation"]').text()).toContain(
      'Retry exact mutation',
    );
    expect(wrapper.find('[data-testid="confirm-grant"]').exists()).toBe(false);
    expect(
      (
        wrapper.get('[data-testid="lookup-player"]')
          .element as HTMLButtonElement
      ).disabled,
    ).toBe(true);
    expect(
      (
        wrapper.get('[data-testid="refresh-history"]')
          .element as HTMLButtonElement
      ).disabled,
    ).toBe(true);
    await wrapper.get('[data-testid="retry-operation"]').trigger('click');
    await settleView();

    expect(testState.transport.grant).toHaveBeenCalledTimes(2);
    expect(testState.transport.grant.mock.calls[1]?.[0]).toEqual(
      originalRequest,
    );
    expect(wrapper.get('[data-testid="current-revision"]').text()).toBe('8');
  });

  it('renders unauthenticated, forbidden, and not-found server failures with retries', async () => {
    allowManagePermission();
    testState.transport.getPage
      .mockRejectedValueOnce(
        apiError(
          401,
          'SwordMaster.PlayerAssets.EquipmentGrants.UnauthorizedAdministrator',
          'Administrator authentication is required.',
        ),
      )
      .mockRejectedValueOnce(
        apiError(
          403,
          'SwordMaster.PlayerAssets.EquipmentGrants.Forbidden',
          'The administrator cannot manage equipment grants.',
        ),
      )
      .mockRejectedValueOnce(
        apiError(
          404,
          'SwordMaster.PlayerAssets.EquipmentGrants.PlayerNotFound',
          'The exact player was not found.',
          { playerId: ['No matching PlayerAssets profile exists.'] },
        ),
      );
    const wrapper = mountWorkspace();

    await lookup(wrapper);
    expect(wrapper.text()).toContain(
      'Administrator authentication is required.',
    );
    await wrapper.get('[data-testid="retry-operation"]').trigger('click');
    await settleView();
    expect(wrapper.text()).toContain(
      'The administrator cannot manage equipment grants.',
    );
    await wrapper.get('[data-testid="retry-operation"]').trigger('click');
    await settleView();

    expect(wrapper.text()).toContain('The exact player was not found.');
    expect(wrapper.text()).toContain(
      'SwordMaster.PlayerAssets.EquipmentGrants.PlayerNotFound',
    );
    expect(
      wrapper.get('[data-testid="equipment-grants-error-details"]').text(),
    ).toContain('No matching PlayerAssets profile exists.');
  });
});
