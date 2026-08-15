/* eslint-disable vue/one-component-per-file -- Focused Element Plus test stubs live with this workspace spec. */

import type {
  AdministratorDto,
  AdminRoleSummaryDto,
} from '#/generated/admin-access/types.gen';

import { flushPromises, mount } from '@vue/test-utils';
import { computed, defineComponent, h, inject, provide, ref } from 'vue';

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import AdministratorsWorkspace from './administrators.vue';
import AdministratorCreateDrawer from './components/administrator-create-drawer.vue';
import AdministratorDisplayNameDrawer from './components/administrator-display-name-drawer.vue';
import AdministratorPasswordResetDialog from './components/administrator-password-reset-dialog.vue';
import AdministratorRolesDrawer from './components/administrator-roles-drawer.vue';
import AdministratorStateDialog from './components/administrator-state-dialog.vue';

const api = vi.hoisted(() => ({
  changeAdministratorDisplayName: vi.fn(),
  createAdministrator: vi.fn(),
  getAdministrator: vi.fn(),
  isNovaGameRequestCancelled: vi.fn(),
  listAdminRoles: vi.fn(),
  listAdministrators: vi.fn(),
  replaceAdministratorRoles: vi.fn(),
  resetAdministratorPassword: vi.fn(),
  setAdministratorActiveState: vi.fn(),
  toNovaGameApiError: vi.fn(),
}));

const operator = vi.hoisted(() => ({
  userId: 'current-administrator-id',
}));

const stores = vi.hoisted(() => ({
  accessStore: undefined as undefined | { accessCodes: string[] },
}));

const authentication = vi.hoisted(() => ({
  expireSession: vi.fn(),
  refreshAuthenticatedState: vi.fn(),
}));

const router = vi.hoisted(() => ({
  currentRoute: { value: { path: '/admin-access/administrators' } },
  replace: vi.fn(),
}));

const viewport = vi.hoisted(() => ({
  mobile: false,
}));

vi.mock('#/api', () => api);

vi.mock('#/store', () => ({
  useAuthStore: () => authentication,
}));

vi.mock('vue-router', async () => {
  const actual =
    await vi.importActual<typeof import('vue-router')>('vue-router');
  return {
    ...actual,
    useRouter: () => router,
  };
});

vi.mock('@vben/icons', () => {
  const Icon = defineComponent({
    setup: () => () => h('span'),
  });

  return {
    CircleCheckBig: Icon,
    CircleX: Icon,
    Ellipsis: Icon,
    KeyRound: Icon,
    LockKeyhole: Icon,
    MoreHorizontal: Icon,
    Pencil: Icon,
    Plus: Icon,
    Power: Icon,
    RotateCw: Icon,
    UserRoundPen: Icon,
  };
});

vi.mock('@vben/stores', async () => {
  const { reactive } = await vi.importActual<typeof import('vue')>('vue');
  const accessStore = reactive({ accessCodes: [] as string[] });
  stores.accessStore = accessStore;
  return {
    useAccessStore: () => accessStore,
    useUserStore: () => ({
      userInfo: operator.userId ? { userId: operator.userId } : undefined,
    }),
  };
});

vi.mock('@vueuse/core', () => ({
  useMediaQuery: () => ref(viewport.mobile),
}));

vi.mock('element-plus', () => {
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
  const ElDialog = defineComponent({
    name: 'ElDialog',
    inheritAttrs: false,
    props: {
      closeOnClickModal: Boolean,
      closeOnPressEscape: Boolean,
      modelValue: Boolean,
      showClose: { default: true, type: Boolean },
      title: { default: '', type: String },
    },
    emits: ['close'],
    setup(props, { attrs, emit, slots }) {
      return () =>
        props.modelValue
          ? h('section', { ...attrs, onClose: () => emit('close') }, [
              h('h2', props.title),
              slots.default?.(),
              slots.footer?.(),
            ])
          : null;
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
      error: { default: '', type: String },
      label: { default: '', type: String },
    },
    setup(props, { attrs, slots }) {
      return () =>
        h('label', attrs, [
          h('span', props.label),
          props.error ? h('span', props.error) : null,
          slots.default?.(),
        ]);
    },
  });
  const ElDropdown = defineComponent({
    name: 'ElDropdown',
    inheritAttrs: false,
    emits: ['command'],
    setup(_, { attrs, slots }) {
      return () =>
        h('div', attrs, [
          slots.default?.(),
          slots.extra?.(),
          slots.dropdown?.(),
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
          type: props.type,
          value: props.modelValue,
        });
    },
  });
  const ElOption = defineComponent({
    inheritAttrs: false,
    props: {
      label: { default: '', type: String },
      value: { default: '', type: [Boolean, Number, String] },
    },
    setup(props, { attrs }) {
      return () =>
        h('option', { ...attrs, value: String(props.value) }, props.label);
    },
  });
  const ElPagination = defineComponent({
    name: 'ElPagination',
    inheritAttrs: false,
    props: {
      layout: { default: '', type: String },
    },
    emits: [
      'current-change',
      'size-change',
      'update:currentPage',
      'update:pageSize',
    ],
    setup(_, { attrs, slots }) {
      return () => h('div', attrs, slots.default?.());
    },
  });
  const ElSelect = defineComponent({
    inheritAttrs: false,
    props: {
      disabled: Boolean,
      loading: Boolean,
      modelValue: { default: undefined, type: [Array, Boolean, String] },
      multiple: Boolean,
    },
    emits: ['change', 'update:modelValue'],
    setup(props, { attrs, emit, slots }) {
      return () =>
        h(
          'select',
          {
            ...attrs,
            disabled: props.disabled || props.loading,
            multiple: props.multiple,
            onChange: (event: Event) => {
              const target = event.target as HTMLSelectElement;
              const value = props.multiple
                ? [...target.selectedOptions].map((option) => option.value)
                : target.value;
              emit('update:modelValue', value);
              emit('change', value);
            },
            value: props.modelValue as string,
          },
          slots.default?.(),
        );
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
    name: 'ElTableColumn',
    inheritAttrs: false,
    props: {
      label: { default: '', type: String },
      width: { default: undefined, type: [Number, String] },
    },
    setup(props, { attrs, slots }) {
      const rows = inject(
        tableRowsKey,
        computed(() => [] as unknown[]),
      );
      return () =>
        h('div', attrs, [
          h('span', props.label),
          ...rows.value.flatMap((row) => slots.default?.({ row }) ?? []),
        ]);
    },
  });

  return {
    ElAlert,
    ElButton,
    ElDialog,
    ElDrawer,
    ElDropdown,
    ElDropdownItem: Container,
    ElDropdownMenu: Container,
    ElEmpty: Container,
    ElForm: Container,
    ElFormItem,
    ElInput,
    ElOption,
    ElPagination,
    ElResult: Container,
    ElSelect,
    ElTable,
    ElTableColumn,
    ElTag: Container,
    ElTooltip: Container,
  };
});

const accessPermission = 'Operations.AdminAccess.Access';
const administratorsManagePermission =
  'Operations.AdminAccess.Administrators.Manage';
const administratorsReadPermission =
  'Operations.AdminAccess.Administrators.Read';
const rolesReadPermission = 'Operations.AdminAccess.Roles.Read';
const idempotencyKey = '8a8af8aa-1234-4aaa-8aaa-123456789abc';

function administrator(
  overrides: Partial<AdministratorDto> = {},
): AdministratorDto {
  return {
    createdAtUtc: '2026-08-16T01:02:03Z',
    displayName: 'Original name',
    id: 'administrator-id',
    isActive: true,
    lastLoginAtUtc: '2026-08-15T01:02:03Z',
    revision: 7,
    roles: [],
    updatedAtUtc: '2026-08-16T01:02:03Z',
    userName: 'operator',
    ...overrides,
  };
}

function administratorDetail(data = administrator(), etag = '"7"') {
  return {
    data,
    etag,
    headers: { etag },
    status: 200,
  };
}

function administratorPage(items: AdministratorDto[] = []) {
  return {
    data: {
      items,
      page: 1,
      pageSize: 20,
      totalCount: items.length,
    },
    headers: {},
    status: 200,
  };
}

function role(
  overrides: Partial<AdminRoleSummaryDto> = {},
): AdminRoleSummaryDto {
  return {
    administratorCount: 1,
    createdAtUtc: '2026-08-16T01:02:03Z',
    id: 'role-id',
    isActive: true,
    isBuiltIn: false,
    name: 'Operators',
    permissionCount: 1,
    revision: 2,
    updatedAtUtc: '2026-08-16T01:02:03Z',
    ...overrides,
  };
}

function rolePage(items: AdminRoleSummaryDto[] = []) {
  return {
    data: {
      items,
      page: 1,
      pageSize: 100,
      totalCount: items.length,
    },
    headers: {},
    status: 200,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolver) => {
    resolve = resolver;
  });
  return { promise, resolve };
}

function getAccessStore() {
  if (!stores.accessStore) {
    throw new Error('The mocked access store was not initialized.');
  }
  return stores.accessStore;
}

function testGlobal() {
  return {
    directives: {
      loading: () => undefined,
    },
  };
}

function mountCreateDrawer(
  props: Partial<InstanceType<typeof AdministratorCreateDrawer>['$props']> = {},
) {
  return mount(AdministratorCreateDrawer, {
    global: testGlobal(),
    props: {
      canManage: true,
      canReadRoles: false,
      modelValue: true,
      ...props,
    },
  });
}

function mountDisplayNameDrawer(
  props: Partial<
    InstanceType<typeof AdministratorDisplayNameDrawer>['$props']
  > = {},
) {
  return mount(AdministratorDisplayNameDrawer, {
    global: testGlobal(),
    props: {
      administratorId: 'administrator-id',
      canManage: true,
      modelValue: true,
      ...props,
    },
  });
}

function mountRolesDrawer(
  props: Partial<InstanceType<typeof AdministratorRolesDrawer>['$props']> = {},
) {
  return mount(AdministratorRolesDrawer, {
    global: testGlobal(),
    props: {
      administratorId: 'administrator-id',
      canManage: true,
      canReadRoles: true,
      modelValue: true,
      ...props,
    },
  });
}

function mountStateDialog(
  props: Partial<InstanceType<typeof AdministratorStateDialog>['$props']> = {},
) {
  return mount(AdministratorStateDialog, {
    global: testGlobal(),
    props: {
      administratorId: 'administrator-id',
      canManage: true,
      modelValue: true,
      targetActive: false,
      ...props,
    },
  });
}

function mountPasswordResetDialog(
  props: Partial<
    InstanceType<typeof AdministratorPasswordResetDialog>['$props']
  > = {},
) {
  return mount(AdministratorPasswordResetDialog, {
    global: testGlobal(),
    props: {
      administratorId: 'administrator-id',
      canResetPassword: true,
      modelValue: true,
      ...props,
    },
  });
}

function mountWorkspace() {
  return mount(AdministratorsWorkspace, {
    global: testGlobal(),
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  viewport.mobile = false;
  getAccessStore().accessCodes = [];
  operator.userId = 'current-administrator-id';
  authentication.expireSession.mockResolvedValue(undefined);
  authentication.refreshAuthenticatedState.mockResolvedValue({
    accessibleRoutes: [],
    userInfo: {},
  });
  api.isNovaGameRequestCancelled.mockReturnValue(false);
  api.toNovaGameApiError.mockImplementation((error: unknown) => error);
  api.listAdminRoles.mockResolvedValue(rolePage());
  api.listAdministrators.mockResolvedValue(administratorPage());
  vi.stubGlobal('crypto', { randomUUID: vi.fn(() => idempotencyKey) });
});

afterEach(() => {
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
});

describe('administrator workspace', () => {
  it('validates create input locally and does not load role choices without Roles.Read', async () => {
    const wrapper = mountCreateDrawer();
    await flushPromises();

    await wrapper
      .get('[data-testid="submit-administrator-create"]')
      .trigger('click');
    await flushPromises();

    expect(api.createAdministrator).not.toHaveBeenCalled();
    expect(api.listAdminRoles).not.toHaveBeenCalled();
    expect(
      wrapper.find('[data-testid="administrator-create-roles"]').exists(),
    ).toBe(false);
    expect(
      wrapper
        .get('[data-testid="administrator-initial-password"]')
        .attributes('autocomplete'),
    ).toBe('new-password');
    expect(wrapper.text()).toContain('User name must contain 3 to 64');
    expect(wrapper.text()).toContain('Password must contain 12 to 256');
  });

  it('clears a rejected create secret and retains the same UUID for an uncertain retry', async () => {
    api.createAdministrator
      .mockRejectedValueOnce({ message: 'Stale create intent.', status: 409 })
      .mockRejectedValueOnce({
        message: 'The network response is uncertain.',
        status: 503,
      })
      .mockRejectedValueOnce({
        message: 'The network response is uncertain.',
        status: 503,
      });

    const wrapper = mountCreateDrawer();
    await wrapper
      .get('[data-testid="administrator-user-name"]')
      .setValue('operator');
    await wrapper
      .get('[data-testid="administrator-create-display-name"]')
      .setValue('Operator');
    await wrapper
      .get('[data-testid="administrator-initial-password"]')
      .setValue('initial-password');
    await wrapper
      .get('[data-testid="submit-administrator-create"]')
      .trigger('click');
    await flushPromises();

    expect(
      wrapper.get('[data-testid="administrator-initial-password"]').element,
    ).toHaveProperty('value', '');
    expect(wrapper.emitted('conflict')).toEqual([[]]);
    expect(
      wrapper
        .find('[data-testid="reload-conflicted-administrator-create"]')
        .exists(),
    ).toBe(true);

    await wrapper
      .get('[data-testid="administrator-initial-password"]')
      .setValue('initial-password');
    await wrapper
      .get('[data-testid="submit-administrator-create"]')
      .trigger('click');
    await flushPromises();
    await wrapper
      .get('[data-testid="submit-administrator-create"]')
      .trigger('click');
    await flushPromises();

    expect(api.createAdministrator).toHaveBeenCalledTimes(3);
    expect(api.createAdministrator.mock.calls[1]?.[1]).toBe(idempotencyKey);
    expect(api.createAdministrator.mock.calls[2]?.[1]).toBe(idempotencyKey);
    expect(
      wrapper
        .get('[data-testid="administrator-initial-password"]')
        .attributes('disabled'),
    ).toBeDefined();
  });

  it('locks a cancelled create intent until it is retried with the same UUID', async () => {
    const cancellation = { code: 'ERR_CANCELED' };
    api.isNovaGameRequestCancelled.mockImplementation(
      (error: unknown) => error === cancellation,
    );
    api.createAdministrator.mockRejectedValue(cancellation);

    const wrapper = mountCreateDrawer();
    await wrapper
      .get('[data-testid="administrator-user-name"]')
      .setValue('operator');
    await wrapper
      .get('[data-testid="administrator-create-display-name"]')
      .setValue('Operator');
    await wrapper
      .get('[data-testid="administrator-initial-password"]')
      .setValue('initial-password');
    await wrapper
      .get('[data-testid="submit-administrator-create"]')
      .trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain(
      'Retry only with the unchanged fields and password.',
    );
    expect(
      wrapper
        .get('[data-testid="administrator-initial-password"]')
        .attributes('disabled'),
    ).toBeDefined();

    await wrapper
      .get('[data-testid="submit-administrator-create"]')
      .trigger('click');
    await flushPromises();

    expect(api.createAdministrator).toHaveBeenCalledTimes(2);
    expect(api.createAdministrator).toHaveBeenNthCalledWith(
      1,
      {
        displayName: 'Operator',
        initialPassword: 'initial-password',
        roleIds: [],
        userName: 'operator',
      },
      idempotencyKey,
    );
    expect(api.createAdministrator).toHaveBeenNthCalledWith(
      2,
      {
        displayName: 'Operator',
        initialPassword: 'initial-password',
        roleIds: [],
        userName: 'operator',
      },
      idempotencyKey,
    );
  });

  it('reports a forbidden create so the workspace can refresh permission state', async () => {
    api.createAdministrator.mockRejectedValue({
      message: 'Administrator management is no longer permitted.',
      status: 403,
    });

    const wrapper = mountCreateDrawer();
    await wrapper
      .get('[data-testid="administrator-user-name"]')
      .setValue('operator');
    await wrapper
      .get('[data-testid="administrator-create-display-name"]')
      .setValue('Operator');
    await wrapper
      .get('[data-testid="administrator-initial-password"]')
      .setValue('initial-password');
    await wrapper
      .get('[data-testid="submit-administrator-create"]')
      .trigger('click');
    await flushPromises();

    expect(wrapper.emitted('forbidden')).toEqual([[]]);
    expect(wrapper.emitted('update:modelValue')).toEqual([[false]]);
  });

  it('preserves a display-name draft through conflict review and sends the reloaded detail ETag', async () => {
    const initial = administrator({
      displayName: 'Original',
      id: 'administrator-id',
    });
    const refreshed = administrator({
      displayName: 'Remote name',
      id: 'administrator-id',
    });
    api.getAdministrator
      .mockResolvedValueOnce(administratorDetail(initial, '"7"'))
      .mockResolvedValueOnce(administratorDetail(refreshed, '"8"'));
    api.changeAdministratorDisplayName
      .mockRejectedValueOnce({
        message: 'The administrator revision is stale.',
        status: 409,
      })
      .mockResolvedValueOnce(administratorDetail(refreshed, '"9"'));

    const wrapper = mountDisplayNameDrawer();
    await flushPromises();
    await wrapper
      .get('[data-testid="administrator-display-name"]')
      .setValue('Locally edited');
    await wrapper
      .get('[data-testid="save-administrator-display-name"]')
      .trigger('click');
    await flushPromises();

    expect(api.changeAdministratorDisplayName).toHaveBeenLastCalledWith(
      'administrator-id',
      { displayName: 'Locally edited' },
      '"7"',
    );
    expect(
      wrapper.find('[data-testid="reload-conflicted-display-name"]').exists(),
    ).toBe(true);

    await wrapper
      .get('[data-testid="reload-conflicted-display-name"]')
      .trigger('click');
    await flushPromises();
    expect(
      wrapper.get('[data-testid="administrator-display-name"]').element,
    ).toHaveProperty('value', 'Locally edited');

    await wrapper
      .get('[data-testid="save-administrator-display-name"]')
      .trigger('click');
    await flushPromises();
    expect(api.changeAdministratorDisplayName).toHaveBeenLastCalledWith(
      'administrator-id',
      { displayName: 'Locally edited' },
      '"8"',
    );
  });

  it('does not let an older administrator-detail request replace the currently selected administrator', async () => {
    const firstLoad = deferred<ReturnType<typeof administratorDetail>>();
    const secondLoad = deferred<ReturnType<typeof administratorDetail>>();
    api.getAdministrator
      .mockReturnValueOnce(firstLoad.promise)
      .mockReturnValueOnce(secondLoad.promise);

    const wrapper = mountDisplayNameDrawer({
      administratorId: 'first-administrator-id',
    });
    await wrapper.setProps({ administratorId: 'second-administrator-id' });

    secondLoad.resolve(
      administratorDetail(
        administrator({ displayName: 'Second', id: 'second-administrator-id' }),
      ),
    );
    await flushPromises();
    expect(
      wrapper.get('[data-testid="administrator-display-name"]').element,
    ).toHaveProperty('value', 'Second');

    firstLoad.resolve(
      administratorDetail(
        administrator({ displayName: 'First', id: 'first-administrator-id' }),
      ),
    );
    await flushPromises();
    expect(
      wrapper.get('[data-testid="administrator-display-name"]').element,
    ).toHaveProperty('value', 'Second');
  });

  it('does not make administrator detail or role catalog calls without Roles.Read', async () => {
    const wrapper = mountRolesDrawer({ canReadRoles: false });
    await flushPromises();

    expect(api.getAdministrator).not.toHaveBeenCalled();
    expect(api.listAdminRoles).not.toHaveBeenCalled();
    expect(
      wrapper.find('[data-testid="administrator-role-ids"]').exists(),
    ).toBe(false);
    expect(wrapper.text()).toContain(
      'Role selection is unavailable without role read access.',
    );
  });

  it('does not let a revoked Roles.Read catalog response replace a newer role drawer state', async () => {
    const staleCatalog = deferred<ReturnType<typeof rolePage>>();
    api.getAdministrator.mockResolvedValue(administratorDetail());
    api.listAdminRoles
      .mockReturnValueOnce(staleCatalog.promise)
      .mockResolvedValueOnce(rolePage([role({ name: 'Fresh role' })]));

    const wrapper = mountRolesDrawer();
    await wrapper.setProps({ canReadRoles: false });
    await wrapper.setProps({ canReadRoles: true });
    await flushPromises();

    staleCatalog.resolve(rolePage([role({ name: 'Stale role' })]));
    await flushPromises();

    expect(wrapper.text()).toContain('Fresh role');
    expect(wrapper.text()).not.toContain('Stale role');
  });

  it('marks inactive assigned roles and sends the loaded detail ETag when replacing active roles', async () => {
    const detail = administrator({
      roles: [
        { id: 'active-role-id', isActive: true, name: 'Active role' },
        {
          id: 'inactive-role-id',
          isActive: false,
          name: 'A'.repeat(64),
        },
      ],
    });
    api.getAdministrator.mockResolvedValue(administratorDetail(detail, '"7"'));
    api.listAdminRoles.mockResolvedValue(
      rolePage([role({ id: 'next-role-id' })]),
    );
    api.replaceAdministratorRoles.mockResolvedValue(
      administratorDetail(detail, '"8"'),
    );

    const wrapper = mountRolesDrawer();
    await flushPromises();

    expect(wrapper.text()).toContain('Inactive assigned roles require review');
    expect(wrapper.get('.long-value-tag').text()).toBe(
      `${'A'.repeat(64)} (inactive)`,
    );
    await wrapper
      .get('[data-testid="administrator-role-ids"]')
      .setValue(['next-role-id']);
    await wrapper
      .get('[data-testid="save-administrator-roles"]')
      .trigger('click');
    await flushPromises();

    expect(api.replaceAdministratorRoles).toHaveBeenCalledWith(
      'administrator-id',
      { roleIds: ['next-role-id'] },
      '"7"',
    );
  });

  it('uses an explicit active-state confirmation and the current detail ETag', async () => {
    api.getAdministrator.mockResolvedValue(administratorDetail());
    api.setAdministratorActiveState.mockResolvedValue(
      administratorDetail(administrator({ isActive: false }), '"8"'),
    );

    const wrapper = mountStateDialog();
    await flushPromises();

    expect(wrapper.text()).toContain('Deactivate administrator');
    expect(wrapper.text()).toContain(
      'This revokes the administrator access state',
    );
    await wrapper
      .get('[data-testid="confirm-administrator-state"]')
      .trigger('click');
    await flushPromises();

    expect(api.setAdministratorActiveState).toHaveBeenCalledWith(
      'administrator-id',
      { isActive: false },
      '"7"',
    );
  });

  it('keeps an administrator state confirmation open when Escape is requested while pending', async () => {
    const pendingCommand = deferred<ReturnType<typeof administratorDetail>>();
    api.getAdministrator.mockResolvedValue(administratorDetail());
    api.setAdministratorActiveState.mockReturnValueOnce(pendingCommand.promise);

    const wrapper = mountStateDialog();
    await flushPromises();
    await wrapper
      .get('[data-testid="confirm-administrator-state"]')
      .trigger('click');
    await flushPromises();

    const dialog = wrapper.findComponent({ name: 'ElDialog' });
    expect(dialog.props('closeOnPressEscape')).toBe(false);
    expect(dialog.props('showClose')).toBe(false);
    expect(wrapper.get('button').attributes('disabled')).toBeDefined();

    dialog.vm.$emit('close');
    await flushPromises();
    expect(wrapper.emitted('update:modelValue')).toBeUndefined();

    pendingCommand.resolve(
      administratorDetail(administrator({ isActive: false }), '"8"'),
    );
    await flushPromises();
    expect(wrapper.emitted('update:modelValue')).toEqual([[false]]);
  });

  it('clears a password after conflict and requires reload and review before a new reset', async () => {
    api.getAdministrator
      .mockResolvedValueOnce(administratorDetail())
      .mockResolvedValueOnce(
        administratorDetail(administrator({ revision: 8 }), '"8"'),
      );
    api.resetAdministratorPassword.mockRejectedValueOnce({
      message: 'The administrator revision is stale.',
      status: 409,
    });

    const wrapper = mountPasswordResetDialog();
    await flushPromises();
    const password = wrapper.get('[data-testid="administrator-new-password"]');
    expect(password.attributes('autocomplete')).toBe('new-password');
    await password.setValue('new-password-123');
    await wrapper
      .get('[data-testid="confirm-password-reset"]')
      .trigger('click');
    await flushPromises();

    expect(
      wrapper.get('[data-testid="administrator-new-password"]').element,
    ).toHaveProperty('value', '');
    expect(
      wrapper.find('[data-testid="reload-conflicted-password-reset"]').exists(),
    ).toBe(true);
    expect(api.resetAdministratorPassword).toHaveBeenCalledWith(
      'administrator-id',
      { newPassword: 'new-password-123' },
      '"7"',
      idempotencyKey,
    );

    await wrapper
      .get('[data-testid="reload-conflicted-password-reset"]')
      .trigger('click');
    await flushPromises();
    expect(api.getAdministrator).toHaveBeenCalledTimes(2);
  });

  it('reuses a canonical UUID and unchanged password after an uncertain reset result', async () => {
    api.getAdministrator.mockResolvedValue(administratorDetail());
    api.resetAdministratorPassword.mockRejectedValue({
      message: 'The network response is uncertain.',
      status: 503,
    });

    const wrapper = mountPasswordResetDialog();
    await flushPromises();
    await wrapper
      .get('[data-testid="administrator-new-password"]')
      .setValue('new-password-123');
    await wrapper
      .get('[data-testid="confirm-password-reset"]')
      .trigger('click');
    await flushPromises();
    await wrapper
      .get('[data-testid="confirm-password-reset"]')
      .trigger('click');
    await flushPromises();

    expect(api.resetAdministratorPassword).toHaveBeenCalledTimes(2);
    expect(api.resetAdministratorPassword).toHaveBeenNthCalledWith(
      1,
      'administrator-id',
      { newPassword: 'new-password-123' },
      '"7"',
      idempotencyKey,
    );
    expect(api.resetAdministratorPassword).toHaveBeenNthCalledWith(
      2,
      'administrator-id',
      { newPassword: 'new-password-123' },
      '"7"',
      idempotencyKey,
    );
    expect(
      wrapper
        .get('[data-testid="administrator-new-password"]')
        .attributes('disabled'),
    ).toBeDefined();
  });

  it('locks a cancelled password reset intent until it is retried with the same UUID', async () => {
    const cancellation = { code: 'ERR_CANCELED' };
    api.getAdministrator.mockResolvedValue(administratorDetail());
    api.isNovaGameRequestCancelled.mockImplementation(
      (error: unknown) => error === cancellation,
    );
    api.resetAdministratorPassword.mockRejectedValue(cancellation);

    const wrapper = mountPasswordResetDialog();
    await flushPromises();
    await wrapper
      .get('[data-testid="administrator-new-password"]')
      .setValue('new-password-123');
    await wrapper
      .get('[data-testid="confirm-password-reset"]')
      .trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('Retry only with the unchanged password.');
    expect(
      wrapper
        .get('[data-testid="administrator-new-password"]')
        .attributes('disabled'),
    ).toBeDefined();

    await wrapper
      .get('[data-testid="confirm-password-reset"]')
      .trigger('click');
    await flushPromises();

    expect(api.resetAdministratorPassword).toHaveBeenCalledTimes(2);
    expect(api.resetAdministratorPassword).toHaveBeenNthCalledWith(
      1,
      'administrator-id',
      { newPassword: 'new-password-123' },
      '"7"',
      idempotencyKey,
    );
    expect(api.resetAdministratorPassword).toHaveBeenNthCalledWith(
      2,
      'administrator-id',
      { newPassword: 'new-password-123' },
      '"7"',
      idempotencyKey,
    );
  });

  it('sends server paging and filters while only loading the role selector with Roles.Read', async () => {
    getAccessStore().accessCodes = [
      accessPermission,
      administratorsManagePermission,
      administratorsReadPermission,
      rolesReadPermission,
    ];
    api.listAdministrators.mockResolvedValue(administratorPage());
    api.listAdminRoles.mockResolvedValue(rolePage([role()]));

    const wrapper = mountWorkspace();
    await flushPromises();
    expect(api.listAdminRoles).toHaveBeenCalledWith(
      { Page: 1, PageSize: 100, Search: undefined },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );

    await wrapper
      .get('[data-testid="administrator-search"]')
      .setValue('  oper ');
    await wrapper
      .get('[data-testid="administrator-search"]')
      .trigger('keyup.enter');
    await flushPromises();
    expect(api.listAdministrators).toHaveBeenLastCalledWith(
      {
        IsActive: undefined,
        Page: 1,
        PageSize: 20,
        RoleId: undefined,
        Search: 'oper',
      },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );

    const pagination = wrapper.findComponent({ name: 'ElPagination' });
    pagination.vm.$emit('update:currentPage', 3);
    pagination.vm.$emit('current-change', 3);
    await flushPromises();
    expect(api.listAdministrators).toHaveBeenLastCalledWith(
      {
        IsActive: undefined,
        Page: 3,
        PageSize: 20,
        RoleId: undefined,
        Search: 'oper',
      },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
  });

  it('does not load a role catalog or permit an older list response to replace a newer result', async () => {
    getAccessStore().accessCodes = [
      accessPermission,
      administratorsReadPermission,
    ];
    const firstLoad = deferred<ReturnType<typeof administratorPage>>();
    const secondLoad = deferred<ReturnType<typeof administratorPage>>();
    api.listAdministrators
      .mockReturnValueOnce(firstLoad.promise)
      .mockReturnValueOnce(secondLoad.promise);

    const wrapper = mountWorkspace();
    await wrapper
      .get('[data-testid="refresh-administrators"]')
      .trigger('click');
    secondLoad.resolve(
      administratorPage([administrator({ userName: 'new-result' })]),
    );
    await flushPromises();
    expect(wrapper.text()).toContain('new-result');

    firstLoad.resolve(
      administratorPage([administrator({ userName: 'old-result' })]),
    );
    await flushPromises();
    expect(api.listAdminRoles).not.toHaveBeenCalled();
    expect(
      wrapper.find('[data-testid="administrator-role-filter"]').exists(),
    ).toBe(false);
    expect(wrapper.text()).toContain('new-result');
    expect(wrapper.text()).not.toContain('old-result');
  });

  it('clears a revoked role filter and rejects its late catalog response', async () => {
    getAccessStore().accessCodes = [
      accessPermission,
      administratorsReadPermission,
      rolesReadPermission,
    ];
    const staleCatalog = deferred<ReturnType<typeof rolePage>>();
    api.listAdminRoles.mockReturnValueOnce(staleCatalog.promise);

    const wrapper = mountWorkspace();
    await flushPromises();
    getAccessStore().accessCodes = [
      accessPermission,
      administratorsReadPermission,
    ];
    await flushPromises();
    staleCatalog.resolve(rolePage([role({ name: 'Stale role' })]));
    await flushPromises();

    getAccessStore().accessCodes = [
      accessPermission,
      administratorsReadPermission,
      rolesReadPermission,
    ];
    await flushPromises();

    expect(
      wrapper.find('[data-testid="administrator-role-filter"]').exists(),
    ).toBe(true);
    expect(wrapper.text()).not.toContain('Stale role');
  });

  it('keeps filters labelled, renders malformed timestamps safely, and compacts row commands on mobile', async () => {
    viewport.mobile = true;
    getAccessStore().accessCodes = [
      accessPermission,
      administratorsManagePermission,
      administratorsReadPermission,
      rolesReadPermission,
    ];
    api.listAdministrators.mockResolvedValue(
      administratorPage([
        administrator({
          lastLoginAtUtc: 'not-a-timestamp',
          updatedAtUtc: 'also-not-a-timestamp',
        }),
      ]),
    );

    const wrapper = mountWorkspace();
    await flushPromises();

    expect(
      wrapper
        .get('[data-testid="administrator-search"]')
        .attributes('aria-label'),
    ).toBe('Search administrators');
    expect(
      wrapper
        .get('[data-testid="administrator-active-filter"]')
        .attributes('aria-label'),
    ).toBe('Administrator account state');
    expect(
      wrapper
        .get('[data-testid="administrator-role-filter"]')
        .attributes('aria-label'),
    ).toBe('Administrator role');
    expect(wrapper.text()).toContain('Unavailable');
    expect(
      wrapper.find('[data-testid="administrator-mobile-actions"]').exists(),
    ).toBe(true);
    expect(wrapper.find('[content="Administrator actions"]').exists()).toBe(
      true,
    );
    expect(
      wrapper.find('[aria-label="Edit display name for operator"]').exists(),
    ).toBe(false);
    expect(
      wrapper.findComponent({ name: 'ElPagination' }).props('layout'),
    ).toBe('prev, pager, next');

    const actionsColumn = wrapper
      .findAllComponents({ name: 'ElTableColumn' })
      .find((column) => column.props('label') === 'Actions');
    if (!actionsColumn) {
      throw new Error('The administrator actions column was not rendered.');
    }
    expect(actionsColumn.props('width')).toBe(92);

    getAccessStore().accessCodes = [
      accessPermission,
      administratorsReadPermission,
    ];
    const readOnly = mountWorkspace();
    await flushPromises();
    expect(
      readOnly.find('[data-testid="administrator-mobile-actions"]').exists(),
    ).toBe(false);
    expect(readOnly.text()).toContain('Read only');
  });

  it('routes a mobile administrator row-menu command to the matching drawer', async () => {
    viewport.mobile = true;
    getAccessStore().accessCodes = [
      accessPermission,
      administratorsManagePermission,
      administratorsReadPermission,
    ];
    api.listAdministrators.mockResolvedValue(
      administratorPage([administrator()]),
    );

    const wrapper = mountWorkspace();
    await flushPromises();

    const rowMenu = wrapper.findComponent({ name: 'ElDropdown' });
    expect(rowMenu.exists()).toBe(true);
    rowMenu.vm.$emit('command', 'display-name');
    await flushPromises();

    const drawer = wrapper.findComponent(AdministratorDisplayNameDrawer);
    expect(drawer.props('administratorId')).toBe('administrator-id');
    expect(drawer.props('modelValue')).toBe(true);
  });

  it('refreshes permissions and removes management controls after a child reports forbidden', async () => {
    getAccessStore().accessCodes = [
      accessPermission,
      administratorsManagePermission,
      administratorsReadPermission,
    ];
    authentication.refreshAuthenticatedState.mockImplementationOnce(
      async () => {
        getAccessStore().accessCodes = [
          accessPermission,
          administratorsReadPermission,
        ];
        return { accessibleRoutes: [], userInfo: {} };
      },
    );

    const wrapper = mountWorkspace();
    await flushPromises();
    await wrapper.get('[data-testid="create-administrator"]').trigger('click');
    await flushPromises();
    expect(
      wrapper.find('[data-testid="administrator-create-drawer"]').exists(),
    ).toBe(true);

    wrapper.findComponent(AdministratorCreateDrawer).vm.$emit('forbidden');
    await flushPromises();

    expect(authentication.refreshAuthenticatedState).toHaveBeenCalledTimes(1);
    expect(
      wrapper.find('[data-testid="administrator-create-drawer"]').exists(),
    ).toBe(false);
    expect(wrapper.find('[data-testid="create-administrator"]').exists()).toBe(
      false,
    );
  });
});
