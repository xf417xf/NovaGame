/* eslint-disable vue/one-component-per-file -- Test stubs intentionally share this focused spec. */

import type { GameConfigDraft } from '#/api';

import { flushPromises, mount } from '@vue/test-utils';
import { defineComponent, h, ref } from 'vue';

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import DraftEditorDrawer from './draft-editor-drawer.vue';

const api = vi.hoisted(() => ({
  createGameConfigDraft: vi.fn(),
  getGameConfigDraft: vi.fn(),
  updateGameConfigDraft: vi.fn(),
  validateGameConfigDraft: vi.fn(),
}));

vi.mock('#/api', () => ({
  ...api,
  GameConfigDraftState: {
    Editing: 1,
    Published: 4,
    Validated: 3,
    ValidationFailed: 2,
  },
  toNovaGameApiError: (error: unknown) => error,
}));

vi.mock('@vben/icons', async () => {
  const Icon = defineComponent({
    setup: () => () => h('span'),
  });

  return {
    Check: Icon,
    CircleCheckBig: Icon,
    Eye: Icon,
    RotateCw: Icon,
    SquareCode: Icon,
  };
});

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
      disabled: Boolean,
      loading: Boolean,
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
  const ElFormItem = defineComponent({
    inheritAttrs: false,
    props: {
      label: { default: '', type: String },
    },
    setup(props, { attrs, slots }) {
      return () =>
        h('label', attrs, [
          h('span', props.label),
          slots.label?.(),
          slots.default?.(),
        ]);
    },
  });
  const ElInput = defineComponent({
    inheritAttrs: false,
    props: {
      disabled: Boolean,
      modelValue: { default: '', type: String },
      readonly: Boolean,
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
          readonly: props.readonly,
          value: props.modelValue,
        });
    },
  });

  return {
    ElAlert,
    ElButton,
    ElDescriptions: Container,
    ElDescriptionsItem: Container,
    ElDrawer,
    ElForm: Container,
    ElFormItem,
    ElInput,
    ElMessage: {
      error: vi.fn(),
      success: vi.fn(),
      warning: vi.fn(),
    },
    ElOption: Container,
    ElSelect: Container,
    ElTable: Container,
    ElTableColumn: Container,
    ElTag: Container,
    ElTooltip: Container,
  };
});

function createDraft(
  overrides: Partial<GameConfigDraft> = {},
): GameConfigDraft {
  return {
    baseVersionId: null,
    configurationSet: 'default',
    content: '{"collections":{}}',
    createdAtUtc: '2026-07-26T00:00:00Z',
    createdByAdministratorId: 'administrator-id',
    id: 'draft-id',
    latestValidation: null,
    name: 'Initial draft',
    publishedVersionId: null,
    revision: 1,
    schemaVersion: '1',
    state: 1,
    updatedAtUtc: '2026-07-26T00:00:00Z',
    updatedByAdministratorId: 'administrator-id',
    ...overrides,
  };
}

function mountDrawer(draftId: null | string = null) {
  return mount(DraftEditorDrawer, {
    global: {
      directives: {
        loading: () => undefined,
      },
    },
    props: {
      canManage: true,
      configurationSet: 'default',
      draftId,
      modelValue: true,
      versionOptions: [],
    },
  });
}

function mountOwnedDrawer() {
  const OwnedDrawer = defineComponent({
    setup() {
      const draftId = ref<null | string>(null);
      const open = ref(true);

      return () =>
        h(DraftEditorDrawer, {
          canManage: true,
          configurationSet: 'default',
          draftId: draftId.value,
          modelValue: open.value,
          'onUpdate:modelValue': (value: boolean) => {
            open.value = value;
          },
          onSaved: (draft: GameConfigDraft) => {
            draftId.value = draft.id;
          },
          versionOptions: [],
        });
    },
  });

  return mount(OwnedDrawer, {
    global: {
      directives: {
        loading: () => undefined,
      },
    },
  });
}

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  document.body.innerHTML = '';
});

describe('draft editor drawer', () => {
  it('adopts and reloads a newly created draft after a revision conflict', async () => {
    const created = createDraft();
    const remote = createDraft({
      content: '{"collections":{"items":[]}}',
      name: 'Externally updated draft',
      revision: 2,
    });
    api.createGameConfigDraft.mockResolvedValue({
      correlationId: 'create-correlation',
      data: created,
    });
    api.updateGameConfigDraft.mockRejectedValue({
      message: 'The draft revision is stale.',
      status: 409,
    });
    api.getGameConfigDraft.mockResolvedValue(remote);

    const wrapper = mountOwnedDrawer();
    await wrapper.get('[data-testid="draft-name"]').setValue(created.name);
    await wrapper.get('[data-testid="save-draft"]').trigger('click');
    await flushPromises();

    expect(wrapper.getComponent(DraftEditorDrawer).props('draftId')).toBe(
      created.id,
    );
    expect(wrapper.text()).toContain('Configuration draft');
    expect(wrapper.find('[data-testid="base-version"]').exists()).toBe(false);
    expect(api.getGameConfigDraft).not.toHaveBeenCalled();

    await wrapper
      .get('[data-testid="draft-name"]')
      .setValue('Locally stale draft');
    await wrapper.get('[data-testid="save-draft"]').trigger('click');
    await flushPromises();

    const reload = wrapper.get('[data-testid="reload-conflicted-draft"]');
    await reload.trigger('click');
    await flushPromises();

    expect(api.getGameConfigDraft).toHaveBeenCalledOnce();
    expect(api.getGameConfigDraft).toHaveBeenCalledWith(created.id);
    expect(wrapper.get('[data-testid="draft-name"]').element).toHaveProperty(
      'value',
      remote.name,
    );
    expect(wrapper.get('[data-testid="draft-content"]').element).toHaveProperty(
      'value',
      remote.content,
    );
    expect(wrapper.text()).toContain('Revision 2');
    expect(
      wrapper.find('[data-testid="reload-conflicted-draft"]').exists(),
    ).toBe(false);
  });

  it('resets create mode instead of reloading the previously opened draft', async () => {
    const existing = createDraft({ id: 'existing-draft-id' });
    api.getGameConfigDraft.mockResolvedValue(existing);

    const wrapper = mountDrawer(existing.id);
    await flushPromises();
    expect(api.getGameConfigDraft).toHaveBeenCalledWith(existing.id);

    await wrapper.setProps({ modelValue: false });
    await wrapper.setProps({ draftId: null });
    await wrapper.setProps({ modelValue: true });
    await flushPromises();

    expect(api.getGameConfigDraft).toHaveBeenCalledOnce();
    expect(wrapper.get('[data-testid="draft-name"]').element).toHaveProperty(
      'value',
      '',
    );
    expect(wrapper.get('[data-testid="draft-content"]').element).toHaveProperty(
      'value',
      '{\n  "collections": {}\n}\n',
    );
  });
});
