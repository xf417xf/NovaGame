/* eslint-disable vue/one-component-per-file -- Focused Element Plus test stubs live with this workspace spec. */

import type {
  AdminRoleDto,
  AdminRoleSummaryDto,
  PermissionGroupDto,
} from '#/generated/admin-access/types.gen';

import { flushPromises, mount } from '@vue/test-utils';
import { computed, defineComponent, h, inject, provide, ref } from 'vue';

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import RoleCreateDrawer from './components/role-create-drawer.vue';
import RolePermissionSelector from './components/role-permission-selector.vue';
import RolePermissionsDrawer from './components/role-permissions-drawer.vue';
import RoleRenameDrawer from './components/role-rename-drawer.vue';
import RoleStateDialog from './components/role-state-dialog.vue';
import RolesWorkspace from './roles.vue';

const api = vi.hoisted(() => ({
  createAdminRole: vi.fn(),
  getAdminRole: vi.fn(),
  getPermissionCatalog: vi.fn(),
  isNovaGameRequestCancelled: vi.fn(),
  listAdminRoles: vi.fn(),
  renameAdminRole: vi.fn(),
  replaceAdminRolePermissions: vi.fn(),
  setAdminRoleActiveState: vi.fn(),
  toNovaGameApiError: vi.fn(),
}));

const stores = vi.hoisted(() => ({
  accessStore: undefined as undefined | { accessCodes: string[] },
}));

const authentication = vi.hoisted(() => ({
  expireSession: vi.fn(),
  refreshAuthenticatedState: vi.fn(),
}));

const router = vi.hoisted(() => ({
  currentRoute: { value: { path: '/admin-access/roles' } },
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
    LockKeyhole: Icon,
    Plus: Icon,
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
      return () => h('div', attrs, [slots.default?.(), slots.extra?.()]);
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
  const ElCheckbox = defineComponent({
    inheritAttrs: false,
    props: {
      label: { default: '', type: String },
    },
    setup(props, { attrs, slots }) {
      return () => h('label', attrs, [props.label, slots.default?.()]);
    },
  });
  const ElCheckboxGroup = defineComponent({
    inheritAttrs: false,
    props: {
      disabled: Boolean,
      modelValue: { default: () => [], type: Array },
    },
    emits: ['update:modelValue'],
    setup(_, { attrs, slots }) {
      return () => h('div', attrs, slots.default?.());
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
    inheritAttrs: false,
    props: {
      label: { default: '', type: String },
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
    ElCheckbox,
    ElCheckboxGroup,
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
const rolesManagePermission = 'Operations.AdminAccess.Roles.Manage';
const rolesReadPermission = 'Operations.AdminAccess.Roles.Read';
const idempotencyKey = '8a8af8aa-1234-4aaa-8aaa-123456789abc';

function role(overrides: Partial<AdminRoleDto> = {}): AdminRoleDto {
  return {
    administratorCount: 1,
    createdAtUtc: '2026-08-16T01:02:03Z',
    id: 'role-id',
    isActive: true,
    isBuiltIn: false,
    name: 'Operators',
    permissionCount: 1,
    permissionNames: ['Operations.AdminAccess.Roles.Manage'],
    revision: 7,
    updatedAtUtc: '2026-08-16T01:02:03Z',
    ...overrides,
  };
}

function roleSummary(
  overrides: Partial<AdminRoleSummaryDto> = {},
): AdminRoleSummaryDto {
  const detail = role(overrides);
  const { permissionNames: _, ...summary } = detail;
  return summary;
}

function roleDetail(data = role(), etag = '"7"') {
  return {
    data,
    etag,
    headers: { etag },
    status: 200,
  };
}

function rolePage(items: AdminRoleSummaryDto[] = []) {
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

function permissionCatalog(): PermissionGroupDto[] {
  return [
    {
      displayName: 'Administrator access',
      name: 'Operations.AdminAccess',
      permissions: [
        {
          description: 'Enter the administrator access workspace.',
          displayName: 'Access workspace',
          name: 'Operations.AdminAccess.Access',
        },
        {
          description: 'Manage custom roles.',
          displayName: 'Manage roles',
          name: 'Operations.AdminAccess.Roles.Manage',
        },
      ],
    },
  ];
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

function mountWorkspace() {
  return mount(RolesWorkspace, { global: testGlobal() });
}

function mountCreateDrawer(
  props: Partial<InstanceType<typeof RoleCreateDrawer>['$props']> = {},
) {
  return mount(RoleCreateDrawer, {
    global: testGlobal(),
    props: {
      canManage: true,
      canReadRoles: true,
      modelValue: true,
      ...props,
    },
  });
}

function mountRenameDrawer(
  props: Partial<InstanceType<typeof RoleRenameDrawer>['$props']> = {},
) {
  return mount(RoleRenameDrawer, {
    global: testGlobal(),
    props: {
      canManage: true,
      modelValue: true,
      roleId: 'role-id',
      ...props,
    },
  });
}

function mountPermissionsDrawer(
  props: Partial<InstanceType<typeof RolePermissionsDrawer>['$props']> = {},
) {
  return mount(RolePermissionsDrawer, {
    global: testGlobal(),
    props: {
      canManage: true,
      canReadRoles: true,
      modelValue: true,
      roleId: 'role-id',
      ...props,
    },
  });
}

function mountStateDialog(
  props: Partial<InstanceType<typeof RoleStateDialog>['$props']> = {},
) {
  return mount(RoleStateDialog, {
    global: testGlobal(),
    props: {
      canManage: true,
      modelValue: true,
      roleId: 'role-id',
      targetActive: false,
      ...props,
    },
  });
}

beforeEach(() => {
  viewport.mobile = false;
  getAccessStore().accessCodes = [
    accessPermission,
    rolesManagePermission,
    rolesReadPermission,
  ];
  api.createAdminRole.mockResolvedValue(roleDetail());
  api.getAdminRole.mockResolvedValue(roleDetail());
  api.getPermissionCatalog.mockResolvedValue({
    data: permissionCatalog(),
    headers: {},
    status: 200,
  });
  api.isNovaGameRequestCancelled.mockReturnValue(false);
  api.listAdminRoles.mockResolvedValue(rolePage());
  api.renameAdminRole.mockResolvedValue(roleDetail());
  api.replaceAdminRolePermissions.mockResolvedValue(roleDetail());
  api.setAdminRoleActiveState.mockResolvedValue(roleDetail());
  api.toNovaGameApiError.mockImplementation((error: unknown) => error);
  authentication.expireSession.mockReset();
  authentication.refreshAuthenticatedState.mockResolvedValue({
    accessibleRoutes: [
      {
        path: '/admin-access',
        children: [{ path: '/admin-access/roles' }],
      },
    ],
    userInfo: {},
  });
  router.replace.mockReset();
  vi.spyOn(globalThis.crypto, 'randomUUID').mockReturnValue(idempotencyKey);
});

afterEach(() => {
  vi.clearAllMocks();
  vi.restoreAllMocks();
});

describe('roles and permissions workspace', () => {
  it('uses server filters and renders the built-in role as read-only', async () => {
    api.listAdminRoles.mockResolvedValue(
      rolePage([
        roleSummary({
          id: 'administrators-role-id',
          isBuiltIn: true,
          name: 'Administrators',
        }),
      ]),
    );

    const wrapper = mountWorkspace();
    await flushPromises();

    expect(api.listAdminRoles).toHaveBeenCalledWith(
      {
        IsActive: undefined,
        IsBuiltIn: undefined,
        Page: 1,
        PageSize: 20,
        Search: undefined,
      },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
    expect(wrapper.text()).toContain('Administrators');
    expect(
      wrapper.find('[data-testid="built-in-role-read-only"]').exists(),
    ).toBe(true);
    expect(wrapper.find('[aria-label="Rename Administrators"]').exists()).toBe(
      false,
    );

    await wrapper.get('[data-testid="role-search"]').setValue('  operators ');
    await wrapper.get('[data-testid="role-search"]').trigger('keyup.enter');
    await flushPromises();
    expect(api.listAdminRoles).toHaveBeenLastCalledWith(
      {
        IsActive: undefined,
        IsBuiltIn: undefined,
        Page: 1,
        PageSize: 20,
        Search: 'operators',
      },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
  });

  it('does not request roles and shows a forbidden state without role read access', async () => {
    getAccessStore().accessCodes = [accessPermission];

    const wrapper = mountWorkspace();
    await flushPromises();

    expect(api.listAdminRoles).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="roles-forbidden"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="create-role"]').exists()).toBe(false);
  });

  it('groups the server catalog locally and creates a role with exact selected names only', async () => {
    const selector = mount(RolePermissionSelector, {
      global: testGlobal(),
      props: {
        canReadRoles: true,
        disabled: false,
        modelValue: ['Operations.AdminAccess.Roles.Manage'],
      },
    });
    await flushPromises();

    expect(selector.text()).toContain('Administrator access');
    expect(selector.text()).toContain('Access workspace');
    await selector
      .get('[data-testid="role-permission-search"]')
      .setValue('manage');
    expect(selector.text()).toContain('Manage roles');
    expect(selector.text()).not.toContain('Access workspace');

    const drawer = mountCreateDrawer();
    await flushPromises();
    await drawer
      .get('[data-testid="role-create-name"]')
      .setValue(' Operators ');
    drawer
      .findComponent(RolePermissionSelector)
      .vm.$emit('update:modelValue', ['Operations.AdminAccess.Roles.Manage']);
    await flushPromises();
    await drawer.get('[data-testid="submit-role-create"]').trigger('click');
    await flushPromises();

    expect(api.createAdminRole).toHaveBeenCalledWith(
      {
        name: 'Operators',
        permissionNames: ['Operations.AdminAccess.Roles.Manage'],
      },
      idempotencyKey,
    );
    expect(api.createAdminRole).not.toHaveBeenCalledWith(
      expect.objectContaining({
        permissionNames: expect.arrayContaining([
          'Operations.AdminAccess.Access',
        ]),
      }),
      expect.any(String),
    );
  });

  it('reloads and prunes a removed catalog permission before retrying role creation', async () => {
    const refreshedCatalog = deferred<{
      data: PermissionGroupDto[];
      headers: Record<string, never>;
      status: number;
    }>();
    const catalogWithoutRoleManage = permissionCatalog().map((group) => ({
      ...group,
      permissions: (group.permissions ?? []).filter(
        (permission) => permission.name === accessPermission,
      ),
    }));
    api.getPermissionCatalog
      .mockResolvedValueOnce({
        data: permissionCatalog(),
        headers: {},
        status: 200,
      })
      .mockReturnValueOnce(refreshedCatalog.promise);
    api.createAdminRole.mockRejectedValueOnce({
      code: 'NovaGame.AdminAccess.UnknownPermission',
      message: 'The selected permission is no longer registered.',
      status: 422,
    });

    const wrapper = mountCreateDrawer();
    await flushPromises();
    await wrapper.get('[data-testid="role-create-name"]').setValue('Operators');
    wrapper
      .findComponent(RolePermissionSelector)
      .vm.$emit('update:modelValue', [accessPermission, rolesManagePermission]);
    await flushPromises();

    await wrapper.get('[data-testid="submit-role-create"]').trigger('click');
    await flushPromises();

    expect(api.createAdminRole).toHaveBeenCalledTimes(1);
    expect(api.getPermissionCatalog).toHaveBeenCalledTimes(2);
    expect(
      wrapper.get('[data-testid="submit-role-create"]').attributes('disabled'),
    ).toBeDefined();

    refreshedCatalog.resolve({
      data: catalogWithoutRoleManage,
      headers: {},
      status: 200,
    });
    await flushPromises();

    expect(
      wrapper.findComponent(RolePermissionSelector).props('modelValue'),
    ).toEqual([accessPermission]);
    expect(
      wrapper.get('[data-testid="submit-role-create"]').attributes('disabled'),
    ).toBeUndefined();

    await wrapper.get('[data-testid="submit-role-create"]').trigger('click');
    await flushPromises();
    expect(api.createAdminRole).toHaveBeenLastCalledWith(
      { name: 'Operators', permissionNames: [accessPermission] },
      idempotencyKey,
    );
  });

  it('reuses a create key only for an unchanged uncertain role-create intent', async () => {
    api.createAdminRole.mockRejectedValue({
      message: 'The network response is uncertain.',
      status: 503,
    });
    const wrapper = mountCreateDrawer({ canReadRoles: false });
    await wrapper.get('[data-testid="role-create-name"]').setValue('Operators');
    await wrapper.get('[data-testid="submit-role-create"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="submit-role-create"]').trigger('click');
    await flushPromises();

    expect(api.createAdminRole).toHaveBeenCalledTimes(2);
    expect(api.createAdminRole).toHaveBeenNthCalledWith(
      1,
      { name: 'Operators', permissionNames: [] },
      idempotencyKey,
    );
    expect(api.createAdminRole).toHaveBeenNthCalledWith(
      2,
      { name: 'Operators', permissionNames: [] },
      idempotencyKey,
    );
    expect(
      wrapper.get('[data-testid="role-create-name"]').attributes('disabled'),
    ).toBeDefined();
  });

  it('reloads a rename conflict with a fresh ETag while retaining the entered name', async () => {
    api.getAdminRole
      .mockResolvedValueOnce(roleDetail())
      .mockResolvedValueOnce(roleDetail(role({ revision: 8 }), '"8"'));
    api.renameAdminRole.mockRejectedValueOnce({
      message: 'The role revision is stale.',
      status: 409,
    });

    const wrapper = mountRenameDrawer();
    await flushPromises();
    await wrapper.get('[data-testid="role-name"]').setValue('New operators');
    await wrapper.get('[data-testid="save-role-name"]').trigger('click');
    await flushPromises();
    expect(api.renameAdminRole).toHaveBeenCalledWith(
      'role-id',
      { name: 'New operators' },
      '"7"',
    );

    await wrapper
      .get('[data-testid="reload-conflicted-role-name"]')
      .trigger('click');
    await flushPromises();
    expect(
      (wrapper.get('[data-testid="role-name"]').element as HTMLInputElement)
        .value,
    ).toBe('New operators');

    await wrapper.get('[data-testid="save-role-name"]').trigger('click');
    await flushPromises();
    expect(api.renameAdminRole).toHaveBeenLastCalledWith(
      'role-id',
      { name: 'New operators' },
      '"8"',
    );
  });

  it('uses the loaded role ETag for a permission replacement and retains selection after conflict', async () => {
    api.getAdminRole
      .mockResolvedValueOnce(roleDetail())
      .mockResolvedValueOnce(roleDetail(role({ revision: 8 }), '"8"'));
    api.replaceAdminRolePermissions.mockRejectedValueOnce({
      message: 'The role revision is stale.',
      status: 409,
    });

    const wrapper = mountPermissionsDrawer();
    await flushPromises();
    wrapper
      .findComponent(RolePermissionSelector)
      .vm.$emit('update:modelValue', [
        'Operations.AdminAccess.Access',
        'Operations.AdminAccess.Roles.Manage',
      ]);
    await flushPromises();
    await wrapper.get('[data-testid="save-role-permissions"]').trigger('click');
    await flushPromises();
    expect(api.replaceAdminRolePermissions).toHaveBeenCalledWith(
      'role-id',
      {
        permissionNames: [
          'Operations.AdminAccess.Access',
          'Operations.AdminAccess.Roles.Manage',
        ],
      },
      '"7"',
    );

    await wrapper
      .get('[data-testid="reload-conflicted-role-permissions"]')
      .trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="save-role-permissions"]').trigger('click');
    await flushPromises();
    expect(api.replaceAdminRolePermissions).toHaveBeenLastCalledWith(
      'role-id',
      {
        permissionNames: [
          'Operations.AdminAccess.Access',
          'Operations.AdminAccess.Roles.Manage',
        ],
      },
      '"8"',
    );
  });

  it('reloads and prunes a removed catalog permission before retrying a replacement', async () => {
    const refreshedCatalog = deferred<{
      data: PermissionGroupDto[];
      headers: Record<string, never>;
      status: number;
    }>();
    const catalogWithoutRoleManage = permissionCatalog().map((group) => ({
      ...group,
      permissions: (group.permissions ?? []).filter(
        (permission) => permission.name === accessPermission,
      ),
    }));
    api.getAdminRole.mockResolvedValue(
      roleDetail(
        role({ permissionNames: [accessPermission, rolesManagePermission] }),
      ),
    );
    api.getPermissionCatalog
      .mockResolvedValueOnce({
        data: permissionCatalog(),
        headers: {},
        status: 200,
      })
      .mockReturnValueOnce(refreshedCatalog.promise);
    api.replaceAdminRolePermissions.mockRejectedValueOnce({
      code: 'NovaGame.AdminAccess.UnknownPermission',
      message: 'The selected permission is no longer registered.',
      status: 422,
    });

    const wrapper = mountPermissionsDrawer();
    await flushPromises();
    expect(
      wrapper.findComponent(RolePermissionSelector).props('modelValue'),
    ).toEqual([accessPermission, rolesManagePermission]);

    await wrapper.get('[data-testid="save-role-permissions"]').trigger('click');
    await flushPromises();

    expect(api.replaceAdminRolePermissions).toHaveBeenCalledTimes(1);
    expect(api.getPermissionCatalog).toHaveBeenCalledTimes(2);
    expect(
      wrapper
        .get('[data-testid="save-role-permissions"]')
        .attributes('disabled'),
    ).toBeDefined();

    refreshedCatalog.resolve({
      data: catalogWithoutRoleManage,
      headers: {},
      status: 200,
    });
    await flushPromises();

    expect(
      wrapper.findComponent(RolePermissionSelector).props('modelValue'),
    ).toEqual([accessPermission]);
    expect(
      wrapper
        .get('[data-testid="save-role-permissions"]')
        .attributes('disabled'),
    ).toBeUndefined();

    await wrapper.get('[data-testid="save-role-permissions"]').trigger('click');
    await flushPromises();
    expect(api.replaceAdminRolePermissions).toHaveBeenLastCalledWith(
      'role-id',
      { permissionNames: [accessPermission] },
      '"7"',
    );
  });

  it('does not reload the catalog after an unrelated role replacement rejection', async () => {
    api.getAdminRole.mockResolvedValue(
      roleDetail(role({ permissionNames: [rolesManagePermission] })),
    );
    api.replaceAdminRolePermissions.mockRejectedValueOnce({
      code: 'NovaGame.AdminAccess.RoleInactive',
      message: 'The role cannot be changed while inactive.',
      status: 422,
    });

    const wrapper = mountPermissionsDrawer();
    await flushPromises();

    await wrapper.get('[data-testid="save-role-permissions"]').trigger('click');
    await flushPromises();

    expect(api.getPermissionCatalog).toHaveBeenCalledTimes(1);
    expect(
      wrapper.findComponent(RolePermissionSelector).props('modelValue'),
    ).toEqual([rolesManagePermission]);
    expect(
      wrapper
        .get('[data-testid="save-role-permissions"]')
        .attributes('disabled'),
    ).toBeUndefined();
  });

  it('keeps long built-in role and permission values in wrapping tags', async () => {
    const longRoleName = 'R'.repeat(64);
    const longPermissionName = `Operations.${'Permission'.repeat(12)}`;
    api.getAdminRole.mockResolvedValue(
      roleDetail(
        role({
          isBuiltIn: true,
          name: longRoleName,
          permissionNames: [longPermissionName],
        }),
      ),
    );

    const rename = mountRenameDrawer();
    await flushPromises();
    expect(rename.get('.long-value-tag').text()).toBe(longRoleName);

    const permissions = mountPermissionsDrawer();
    await flushPromises();
    expect(permissions.get('.long-value-tag').text()).toBe(longPermissionName);
  });

  it('uses the loaded ETag for an explicit custom-role active-state confirmation', async () => {
    const wrapper = mountStateDialog();
    await flushPromises();
    await wrapper.get('[data-testid="confirm-role-state"]').trigger('click');
    await flushPromises();

    expect(api.setAdminRoleActiveState).toHaveBeenCalledWith(
      'role-id',
      { isActive: false },
      '"7"',
    );
  });

  it('keeps a role state confirmation open when Escape is requested while pending', async () => {
    const pendingCommand = deferred<ReturnType<typeof roleDetail>>();
    api.getAdminRole.mockResolvedValue(roleDetail());
    api.setAdminRoleActiveState.mockReturnValueOnce(pendingCommand.promise);

    const wrapper = mountStateDialog();
    await flushPromises();
    await wrapper.get('[data-testid="confirm-role-state"]').trigger('click');
    await flushPromises();

    const dialog = wrapper.findComponent({ name: 'ElDialog' });
    expect(dialog.props('closeOnPressEscape')).toBe(false);
    expect(dialog.props('showClose')).toBe(false);
    expect(wrapper.get('button').attributes('disabled')).toBeDefined();

    dialog.vm.$emit('close');
    await flushPromises();
    expect(wrapper.emitted('update:modelValue')).toBeUndefined();

    pendingCommand.resolve(roleDetail(role({ isActive: false }), '"8"'));
    await flushPromises();
    expect(wrapper.emitted('update:modelValue')).toEqual([[false]]);
  });

  it('reconciles permissions after a role change and removes controls that were revoked', async () => {
    const wrapper = mountWorkspace();
    await flushPromises();
    authentication.refreshAuthenticatedState.mockImplementationOnce(
      async () => {
        getAccessStore().accessCodes = [accessPermission, rolesReadPermission];
        return {
          accessibleRoutes: [
            {
              path: '/admin-access',
              children: [{ path: '/admin-access/roles' }],
            },
          ],
          userInfo: {},
        };
      },
    );

    wrapper.findComponent(RolePermissionsDrawer).vm.$emit('updated', role());
    await flushPromises();

    expect(authentication.refreshAuthenticatedState).toHaveBeenCalledTimes(1);
    expect(wrapper.find('[data-testid="create-role"]').exists()).toBe(false);
  });

  it('reconciles a forbidden role request and redirects when role read access is revoked', async () => {
    const wrapper = mountWorkspace();
    await flushPromises();
    authentication.refreshAuthenticatedState.mockImplementationOnce(
      async () => {
        getAccessStore().accessCodes = [accessPermission];
        return {
          accessibleRoutes: [
            {
              path: '/admin-access',
              children: [{ path: '/admin-access/administrators' }],
            },
          ],
          userInfo: {},
        };
      },
    );

    wrapper.findComponent(RoleStateDialog).vm.$emit('forbidden');
    await flushPromises();

    expect(authentication.refreshAuthenticatedState).toHaveBeenCalledTimes(1);
    expect(router.replace).toHaveBeenCalledWith('/admin-access/administrators');
  });

  it('keeps roles forbidden until a failed permission reconciliation is retried successfully', async () => {
    const refreshedState = deferred<{
      accessibleRoutes: {
        children: { path: string }[];
        path: string;
      }[];
      userInfo: Record<string, never>;
    }>();
    authentication.refreshAuthenticatedState
      .mockRejectedValueOnce({
        message: 'Permission refresh is unavailable.',
        status: 0,
      })
      .mockReturnValueOnce(refreshedState.promise);

    const wrapper = mountWorkspace();
    await flushPromises();
    const listCallCountBeforeReconciliation =
      api.listAdminRoles.mock.calls.length;
    const refreshCallCountBeforeReconciliation =
      authentication.refreshAuthenticatedState.mock.calls.length;
    expect(listCallCountBeforeReconciliation).toBeGreaterThan(0);

    wrapper.findComponent(RolePermissionsDrawer).vm.$emit('updated', role());
    await flushPromises();

    expect(wrapper.find('[data-testid="roles-forbidden"]').exists()).toBe(true);
    expect(
      wrapper.find('[data-testid="role-access-reconciliation-error"]').text(),
    ).toContain('Permission refresh is unavailable.');
    expect(wrapper.find('[data-testid="create-role"]').exists()).toBe(false);
    expect(api.listAdminRoles).toHaveBeenCalledTimes(
      listCallCountBeforeReconciliation,
    );

    await wrapper
      .get('[data-testid="retry-role-access-reconciliation"]')
      .trigger('click');
    await flushPromises();

    expect(authentication.refreshAuthenticatedState).toHaveBeenCalledTimes(
      refreshCallCountBeforeReconciliation + 2,
    );
    expect(wrapper.find('[data-testid="roles-forbidden"]').exists()).toBe(true);
    expect(api.listAdminRoles).toHaveBeenCalledTimes(
      listCallCountBeforeReconciliation,
    );

    refreshedState.resolve({
      accessibleRoutes: [
        {
          children: [{ path: '/admin-access/roles' }],
          path: '/admin-access',
        },
      ],
      userInfo: {},
    });
    await flushPromises();

    expect(wrapper.find('[data-testid="roles-forbidden"]').exists()).toBe(
      false,
    );
    expect(wrapper.find('[data-testid="create-role"]').exists()).toBe(true);
    expect(api.listAdminRoles).toHaveBeenCalledTimes(
      listCallCountBeforeReconciliation + 1,
    );
  });

  it('does not let an older cancelled list result replace a newer query result', async () => {
    const firstLoad = deferred<ReturnType<typeof rolePage>>();
    const secondLoad = deferred<ReturnType<typeof rolePage>>();
    api.listAdminRoles
      .mockReturnValueOnce(firstLoad.promise)
      .mockReturnValueOnce(secondLoad.promise);

    const wrapper = mountWorkspace();
    await wrapper.get('[data-testid="refresh-roles"]').trigger('click');
    secondLoad.resolve(rolePage([roleSummary({ name: 'New result' })]));
    await flushPromises();
    expect(wrapper.text()).toContain('New result');

    firstLoad.resolve(rolePage([roleSummary({ name: 'Old result' })]));
    await flushPromises();
    expect(wrapper.text()).toContain('New result');
    expect(wrapper.text()).not.toContain('Old result');
  });

  it('keeps filters labelled, renders malformed timestamps safely, and compacts row commands on mobile', async () => {
    viewport.mobile = true;
    api.listAdminRoles.mockResolvedValue(
      rolePage([
        roleSummary({ name: 'Operators', updatedAtUtc: 'not-a-timestamp' }),
      ]),
    );

    const wrapper = mountWorkspace();
    await flushPromises();

    expect(
      wrapper.get('[data-testid="role-search"]').attributes('aria-label'),
    ).toBe('Search roles');
    expect(
      wrapper
        .get('[data-testid="role-active-filter"]')
        .attributes('aria-label'),
    ).toBe('Role state');
    expect(
      wrapper
        .get('[data-testid="role-built-in-filter"]')
        .attributes('aria-label'),
    ).toBe('Role kind');
    expect(wrapper.text()).toContain('Unavailable');
    expect(wrapper.find('[data-testid="role-mobile-actions"]').exists()).toBe(
      true,
    );
    expect(wrapper.find('[content="Role actions"]').exists()).toBe(true);
    expect(wrapper.find('[aria-label="Rename Operators"]').exists()).toBe(
      false,
    );
    expect(
      wrapper.findComponent({ name: 'ElPagination' }).props('layout'),
    ).toBe('prev, pager, next');
  });

  it('routes a mobile role row-menu command to the matching drawer', async () => {
    viewport.mobile = true;
    api.listAdminRoles.mockResolvedValue(rolePage([roleSummary()]));

    const wrapper = mountWorkspace();
    await flushPromises();

    const rowMenu = wrapper.findComponent({ name: 'ElDropdown' });
    expect(rowMenu.exists()).toBe(true);
    rowMenu.vm.$emit('command', 'name');
    await flushPromises();

    const drawer = wrapper.findComponent(RoleRenameDrawer);
    expect(drawer.props('modelValue')).toBe(true);
    expect(drawer.props('roleId')).toBe('role-id');
  });
});
