/* eslint-disable vue/one-component-per-file -- Focused Element Plus test stubs live with this workspace spec. */

import type {
  AdminAccessAuditRecordDto,
  AdministratorDto,
  AdminRoleSummaryDto,
  AdminSessionFamilyDto,
} from '#/generated/admin-access/types.gen';

import { flushPromises, mount } from '@vue/test-utils';
import { computed, defineComponent, h, inject, provide, ref } from 'vue';

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import AuditWorkspace from './audit.vue';
import SessionsWorkspace from './sessions.vue';

const api = vi.hoisted(() => ({
  isNovaGameRequestCancelled: vi.fn(),
  listAdministrators: vi.fn(),
  listAdminRoles: vi.fn(),
  listAuditRecords: vi.fn(),
  listSessionFamilies: vi.fn(),
  revokeSessionFamily: vi.fn(),
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
  currentRoute: { value: { path: '/admin-access/sessions' } },
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
    Copy: Icon,
    Ellipsis: Icon,
    RotateCw: Icon,
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
      return () =>
        h('div', attrs, [
          slots.default?.(),
          slots.extra?.(),
          slots.dropdown?.(),
        ]);
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
  const ElDrawer = defineComponent({
    inheritAttrs: false,
    props: {
      modelValue: Boolean,
      title: { default: '', type: String },
    },
    setup(props, { attrs, slots }) {
      return () =>
        props.modelValue
          ? h('section', attrs, [h('h2', props.title), slots.default?.()])
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
  const ElInput = defineComponent({
    inheritAttrs: false,
    props: {
      disabled: Boolean,
      modelValue: { default: '', type: String },
    },
    emits: ['update:modelValue'],
    setup(props, { attrs, emit }) {
      return () =>
        h('input', {
          ...attrs,
          disabled: props.disabled,
          onInput: (event: Event) =>
            emit('update:modelValue', (event.target as HTMLInputElement).value),
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
      modelValue: {
        default: undefined,
        type: [Array, Boolean, Number, String],
      },
      multiple: Boolean,
    },
    emits: ['change', 'update:modelValue'],
    setup(props, { attrs, emit, slots }) {
      function selectedValue(value: string) {
        if (value === 'false') {
          return false;
        }
        if (value === 'true') {
          return true;
        }
        if (/^\d+$/.test(value)) {
          return Number(value);
        }
        return value;
      }

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
                ? [...target.selectedOptions].map((option) =>
                    selectedValue(option.value),
                  )
                : selectedValue(target.value);
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
    ElDropdown: Container,
    ElDropdownItem: Container,
    ElDropdownMenu: Container,
    ElEmpty,
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
const administratorsReadPermission =
  'Operations.AdminAccess.Administrators.Read';
const auditReadPermission = 'Operations.AdminAccess.AuditLogs.Read';
const rolesReadPermission = 'Operations.AdminAccess.Roles.Read';
const sessionsReadPermission = 'Operations.AdminAccess.Sessions.Read';
const sessionsRevokePermission = 'Operations.AdminAccess.Sessions.Revoke';
const administratorId = '11111111-1111-4111-8111-111111111111';
const actorAdministratorId = '22222222-2222-4222-8222-222222222222';
const roleId = '33333333-3333-4333-8333-333333333333';
const familyId = '44444444-4444-4444-8444-444444444444';

function administrator(
  overrides: Partial<AdministratorDto> = {},
): AdministratorDto {
  return {
    createdAtUtc: '2026-08-16T01:02:03Z',
    displayName: 'Operations user',
    id: administratorId,
    isActive: true,
    revision: 1,
    userName: 'operator',
    ...overrides,
  };
}

function administratorPage(items: AdministratorDto[] = []) {
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

function role(
  overrides: Partial<AdminRoleSummaryDto> = {},
): AdminRoleSummaryDto {
  return {
    administratorCount: 1,
    createdAtUtc: '2026-08-16T01:02:03Z',
    id: roleId,
    isActive: true,
    isBuiltIn: false,
    name: 'Operators',
    permissionCount: 1,
    revision: 1,
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

function sessionFamily(
  overrides: Partial<AdminSessionFamilyDto> = {},
): AdminSessionFamilyDto {
  return {
    administratorId,
    displayName: 'Operations user',
    expiresAtUtc: '2026-08-17T01:02:03Z',
    familyId,
    firstCreatedAtUtc: '2026-08-15T01:02:03Z',
    lastActivityAtUtc: '2026-08-16T01:02:03Z',
    lastIpAddress: '203.0.113.7',
    revocationReason: undefined,
    revokedAtUtc: null,
    state: 1,
    userName: 'operator',
    ...overrides,
  };
}

function sessionPage(items: AdminSessionFamilyDto[] = []) {
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

function auditRecord(
  overrides: Partial<AdminAccessAuditRecordDto> = {},
): AdminAccessAuditRecordDto {
  return {
    action: 19,
    actorAdministratorId,
    correlationId: 'correlation-42',
    errorCode: null,
    id: '55555555-5555-4555-8555-555555555555',
    ipAddress: '203.0.113.7',
    occurredAtUtc: '2026-08-16T01:02:03Z',
    permissionName: sessionsRevokePermission,
    roleId,
    sessionFamilyId: familyId,
    subjectAdministratorId: administratorId,
    subjectUserName: 'operator',
    succeeded: true,
    targetRevision: 7,
    userAgent: 'NovaGame operations browser',
    ...overrides,
  };
}

function auditPage(items: AdminAccessAuditRecordDto[] = []) {
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

const mountedWrappers: { unmount: () => void }[] = [];

function mountSessions() {
  const wrapper = mount(SessionsWorkspace, { global: testGlobal() });
  mountedWrappers.push(wrapper);
  return wrapper;
}

function mountAudit() {
  const wrapper = mount(AuditWorkspace, { global: testGlobal() });
  mountedWrappers.push(wrapper);
  return wrapper;
}

beforeEach(() => {
  vi.clearAllMocks();
  viewport.mobile = false;
  getAccessStore().accessCodes = [
    accessPermission,
    administratorsReadPermission,
    auditReadPermission,
    rolesReadPermission,
    sessionsReadPermission,
    sessionsRevokePermission,
  ];
  api.isNovaGameRequestCancelled.mockReturnValue(false);
  api.listAdministrators.mockResolvedValue(
    administratorPage([administrator()]),
  );
  api.listAdminRoles.mockResolvedValue(rolePage([role()]));
  api.listAuditRecords.mockResolvedValue(auditPage());
  api.listSessionFamilies.mockResolvedValue(sessionPage());
  api.revokeSessionFamily.mockResolvedValue(undefined);
  api.toNovaGameApiError.mockImplementation((error: unknown) => error);
  authentication.expireSession.mockResolvedValue(undefined);
  authentication.refreshAuthenticatedState.mockResolvedValue({
    accessibleRoutes: [
      {
        children: [{ path: '/admin-access/sessions' }],
        path: '/admin-access',
      },
    ],
    userInfo: {},
  });
  router.currentRoute.value.path = '/admin-access/sessions';
  router.replace.mockReset();
});

afterEach(() => {
  for (const wrapper of mountedWrappers.splice(0)) {
    wrapper.unmount();
  }
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('session and audit workspaces', () => {
  it('gates both workspaces and never requests protected lists or selector catalogs without access', async () => {
    getAccessStore().accessCodes = [
      sessionsReadPermission,
      auditReadPermission,
    ];

    const sessions = mountSessions();
    const audit = mountAudit();
    await flushPromises();

    expect(api.listSessionFamilies).not.toHaveBeenCalled();
    expect(api.listAuditRecords).not.toHaveBeenCalled();
    expect(api.listAdministrators).not.toHaveBeenCalled();
    expect(api.listAdminRoles).not.toHaveBeenCalled();
    expect(sessions.find('[data-testid="sessions-forbidden"]').exists()).toBe(
      true,
    );
    expect(audit.find('[data-testid="audit-forbidden"]').exists()).toBe(true);
  });

  it('uses exact session filters and paging while retaining a read-only administrator filter fallback', async () => {
    getAccessStore().accessCodes = [accessPermission, sessionsReadPermission];
    api.listSessionFamilies.mockResolvedValue(sessionPage());

    const wrapper = mountSessions();
    await flushPromises();

    expect(api.listSessionFamilies).toHaveBeenCalledWith(
      {
        AdministratorId: undefined,
        Page: 1,
        PageSize: 20,
        State: undefined,
      },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
    expect(api.listAdministrators).not.toHaveBeenCalled();
    expect(
      wrapper.find('[data-testid="session-administrator-id-filter"]').exists(),
    ).toBe(true);
    expect(
      wrapper
        .get('[data-testid="session-administrator-id-filter"]')
        .attributes('aria-label'),
    ).toBe('Administrator ID');
    expect(
      wrapper
        .get('[data-testid="session-state-filter"]')
        .attributes('aria-label'),
    ).toBe('Session state');
    expect(wrapper.text()).toContain(
      'No session families match the current filters.',
    );
    api.listSessionFamilies.mockResolvedValue(sessionPage([sessionFamily()]));

    await wrapper
      .get('[data-testid="session-administrator-id-filter"]')
      .setValue(` ${administratorId} `);
    await wrapper
      .get('[data-testid="session-administrator-id-filter"]')
      .trigger('keyup.enter');
    await flushPromises();
    expect(api.listSessionFamilies).toHaveBeenLastCalledWith(
      {
        AdministratorId: administratorId,
        Page: 1,
        PageSize: 20,
        State: undefined,
      },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
    await wrapper.get('[data-testid="session-state-filter"]').setValue('3');
    await wrapper.get('[data-testid="search-sessions"]').trigger('click');
    await flushPromises();

    expect(api.listSessionFamilies).toHaveBeenLastCalledWith(
      {
        AdministratorId: administratorId,
        Page: 1,
        PageSize: 20,
        State: 3,
      },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );

    const pagination = wrapper.findComponent({ name: 'ElPagination' });
    pagination.vm.$emit('current-change', 3);
    await flushPromises();
    expect(api.listSessionFamilies).toHaveBeenLastCalledWith(
      {
        AdministratorId: administratorId,
        Page: 3,
        PageSize: 20,
        State: 3,
      },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );

    pagination.vm.$emit('size-change', 50);
    await flushPromises();
    expect(api.listSessionFamilies).toHaveBeenLastCalledWith(
      {
        AdministratorId: administratorId,
        Page: 1,
        PageSize: 50,
        State: 3,
      },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
  });

  it('recovers the session-family list after a transient failure is retried', async () => {
    getAccessStore().accessCodes = [accessPermission, sessionsReadPermission];
    api.listSessionFamilies
      .mockRejectedValueOnce({
        message: 'Session list is temporarily unavailable.',
        status: 503,
      })
      .mockResolvedValueOnce(
        sessionPage([sessionFamily({ userName: 'recovered-session-owner' })]),
      );

    const wrapper = mountSessions();
    await flushPromises();

    expect(api.listSessionFamilies).toHaveBeenCalledTimes(1);
    expect(wrapper.text()).toContain(
      'Session list is temporarily unavailable.',
    );
    const retryButton = wrapper
      .findAll('button')
      .find((button) => button.text() === 'Retry');
    if (!retryButton) {
      throw new Error('The session list retry command was not rendered.');
    }

    await retryButton.trigger('click');
    await flushPromises();

    expect(api.listSessionFamilies).toHaveBeenCalledTimes(2);
    expect(wrapper.text()).toContain('recovered-session-owner');
    expect(wrapper.text()).not.toContain(
      'Session list is temporarily unavailable.',
    );
  });

  it('keeps mobile session controls labelled and uses a read-only state when revoke access is absent', async () => {
    viewport.mobile = true;
    getAccessStore().accessCodes = [
      accessPermission,
      sessionsReadPermission,
      sessionsRevokePermission,
    ];
    api.listSessionFamilies.mockResolvedValue(sessionPage([sessionFamily()]));

    const writable = mountSessions();
    await flushPromises();

    expect(
      writable.find('[data-testid="session-mobile-actions"]').exists(),
    ).toBe(true);
    expect(writable.find('[content="Session actions"]').exists()).toBe(true);
    expect(
      writable.findComponent({ name: 'ElPagination' }).props('layout'),
    ).toBe('prev, pager, next');

    const actionsColumn = writable
      .findAllComponents({ name: 'ElTableColumn' })
      .find((column) => column.props('label') === 'Actions');
    if (!actionsColumn) {
      throw new Error('The session actions column was not rendered.');
    }
    expect(actionsColumn.props('width')).toBe(92);

    getAccessStore().accessCodes = [accessPermission, sessionsReadPermission];
    const readOnly = mountSessions();
    await flushPromises();

    expect(
      readOnly.find('[data-testid="session-mobile-actions"]').exists(),
    ).toBe(false);
    expect(readOnly.text()).toContain('Read only');
  });

  it('keeps an unbroken session owner name breakable in the revoke dialog', async () => {
    const userName = 'u'.repeat(64);
    api.listSessionFamilies.mockResolvedValue(
      sessionPage([sessionFamily({ userName })]),
    );

    const wrapper = mountSessions();
    await flushPromises();

    await wrapper.get('[data-testid="revoke-session"]').trigger('click');

    const subject = wrapper.get('[data-testid="session-revoke-subject"]');
    expect(subject.text()).toBe(userName);
    expect(subject.classes()).toContain('dialog-subject');
  });

  it('confirms, retries, reloads, and repeats authoritative session-family revocation', async () => {
    const family = sessionFamily();
    const pendingRevocation = deferred<undefined>();
    api.listSessionFamilies.mockResolvedValue(sessionPage([family]));
    api.revokeSessionFamily
      .mockImplementationOnce(() => pendingRevocation.promise)
      .mockResolvedValueOnce(undefined)
      .mockRejectedValueOnce({
        message: 'Temporary revocation failure.',
        status: 500,
      })
      .mockResolvedValueOnce(undefined);

    const wrapper = mountSessions();
    await flushPromises();

    await wrapper.get('[data-testid="revoke-session"]').trigger('click');
    expect(wrapper.text()).toContain('Revoke session family');
    const firstConfirmation = wrapper.get(
      '[data-testid="confirm-revoke-session"]',
    );
    await firstConfirmation.trigger('click');
    await flushPromises();

    expect(api.revokeSessionFamily).toHaveBeenCalledWith(familyId);
    expect(firstConfirmation.attributes('disabled')).toBeDefined();

    pendingRevocation.resolve(undefined);
    await flushPromises();
    expect(
      wrapper.find('[data-testid="confirm-revoke-session"]').exists(),
    ).toBe(false);
    expect(api.listSessionFamilies).toHaveBeenCalledTimes(2);

    await wrapper.get('[data-testid="revoke-session"]').trigger('click');
    await wrapper
      .get('[data-testid="confirm-revoke-session"]')
      .trigger('click');
    await flushPromises();
    expect(api.revokeSessionFamily).toHaveBeenCalledTimes(2);
    expect(api.listSessionFamilies).toHaveBeenCalledTimes(3);

    await wrapper.get('[data-testid="revoke-session"]').trigger('click');
    await wrapper
      .get('[data-testid="confirm-revoke-session"]')
      .trigger('click');
    await flushPromises();
    expect(wrapper.text()).toContain('Temporary revocation failure.');
    expect(wrapper.find('[data-testid="retry-revoke-session"]').exists()).toBe(
      true,
    );

    await wrapper.get('[data-testid="retry-revoke-session"]').trigger('click');
    await flushPromises();
    expect(api.revokeSessionFamily).toHaveBeenCalledTimes(4);
    expect(api.revokeSessionFamily).toHaveBeenLastCalledWith(familyId);
    expect(api.listSessionFamilies).toHaveBeenCalledTimes(4);
  });

  it('closes and reloads only when an authoritative session-family target-not-found is returned', async () => {
    api.listSessionFamilies
      .mockResolvedValueOnce(sessionPage([sessionFamily()]))
      .mockResolvedValueOnce(sessionPage());
    api.revokeSessionFamily.mockRejectedValueOnce({
      code: 'NovaGame.AdminAccess.SessionFamilyNotFound',
      message: 'The session family was not found.',
      status: 404,
    });

    const wrapper = mountSessions();
    await flushPromises();
    await wrapper.get('[data-testid="revoke-session"]').trigger('click');
    await wrapper
      .get('[data-testid="confirm-revoke-session"]')
      .trigger('click');
    await flushPromises();

    expect(
      wrapper.find('[data-testid="confirm-revoke-session"]').exists(),
    ).toBe(false);
    expect(api.listSessionFamilies).toHaveBeenCalledTimes(2);
  });

  it('keeps a session revocation dialog open for an unrelated 404', async () => {
    api.listSessionFamilies.mockResolvedValue(sessionPage([sessionFamily()]));
    api.revokeSessionFamily.mockRejectedValueOnce({
      code: 'NovaGame.AdminAccess.RoleNotFound',
      message: 'A related role was not found.',
      status: 404,
    });

    const wrapper = mountSessions();
    await flushPromises();
    await wrapper.get('[data-testid="revoke-session"]').trigger('click');
    await wrapper
      .get('[data-testid="confirm-revoke-session"]')
      .trigger('click');
    await flushPromises();

    expect(
      wrapper.find('[data-testid="confirm-revoke-session"]').exists(),
    ).toBe(true);
    expect(wrapper.text()).toContain('A related role was not found.');
    expect(api.listSessionFamilies).toHaveBeenCalledTimes(1);
  });

  it('reconciles a forbidden session request before allowing any further session access', async () => {
    getAccessStore().accessCodes = [accessPermission, sessionsReadPermission];
    api.listSessionFamilies.mockRejectedValueOnce({
      message: 'Session access was revoked.',
      status: 403,
    });
    authentication.refreshAuthenticatedState.mockImplementationOnce(
      async () => {
        getAccessStore().accessCodes = [accessPermission];
        return {
          accessibleRoutes: [
            {
              children: [{ path: '/admin-access/audit' }],
              path: '/admin-access',
            },
          ],
          userInfo: {},
        };
      },
    );

    const wrapper = mountSessions();
    await flushPromises();

    expect(authentication.refreshAuthenticatedState).toHaveBeenCalledTimes(1);
    expect(router.replace).toHaveBeenCalledWith('/admin-access/audit');
    expect(api.listSessionFamilies).toHaveBeenCalledTimes(1);
    expect(api.listAdministrators).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="sessions-forbidden"]').exists()).toBe(
      true,
    );
  });

  it('invalidates a pending session query before showing a local filter error', async () => {
    getAccessStore().accessCodes = [accessPermission, sessionsReadPermission];
    const pendingSessionList = deferred<ReturnType<typeof sessionPage>>();
    api.listSessionFamilies.mockImplementationOnce(
      () => pendingSessionList.promise,
    );

    const wrapper = mountSessions();
    await flushPromises();

    const sessionCall = api.listSessionFamilies.mock.calls[0];
    if (!sessionCall) {
      throw new Error('The initial session query was not issued.');
    }
    const sessionOptions = sessionCall[1] as { signal: AbortSignal };

    await wrapper
      .get('[data-testid="session-administrator-id-filter"]')
      .setValue('not-a-guid');
    await wrapper.get('[data-testid="search-sessions"]').trigger('click');
    await flushPromises();

    expect(api.listSessionFamilies).toHaveBeenCalledTimes(1);
    expect(sessionOptions.signal.aborted).toBe(true);
    expect(wrapper.text()).toContain('Administrator ID must be an exact GUID.');
    expect(
      wrapper
        .get('[data-testid="session-administrator-id-filter"]')
        .attributes('aria-invalid'),
    ).toBe('true');
    expect(
      wrapper
        .get('[data-testid="session-administrator-id-filter"]')
        .attributes('aria-describedby'),
    ).toBe('session-administrator-filter-error');
    expect(
      wrapper.get('#session-administrator-filter-error').attributes('role'),
    ).toBe('alert');

    pendingSessionList.resolve(sessionPage([sessionFamily()]));
    await flushPromises();

    expect(wrapper.find('[data-testid="revoke-session"]').exists()).toBe(false);
    expect(wrapper.text()).toContain(
      'No session families match the current filters.',
    );
  });

  it('does not let older valid session and administrator-selector responses replace newer state', async () => {
    getAccessStore().accessCodes = [
      accessPermission,
      administratorsReadPermission,
      sessionsReadPermission,
    ];
    const firstSessionList = deferred<ReturnType<typeof sessionPage>>();
    const secondSessionList = deferred<ReturnType<typeof sessionPage>>();
    const firstAdministratorCatalog =
      deferred<ReturnType<typeof administratorPage>>();
    const secondAdministratorCatalog =
      deferred<ReturnType<typeof administratorPage>>();
    api.listSessionFamilies
      .mockReturnValueOnce(firstSessionList.promise)
      .mockReturnValueOnce(secondSessionList.promise);
    api.listAdministrators
      .mockReturnValueOnce(firstAdministratorCatalog.promise)
      .mockReturnValueOnce(secondAdministratorCatalog.promise);

    const wrapper = mountSessions();
    await flushPromises();
    const firstSessionOptions = api.listSessionFamilies.mock.calls[0]?.[1] as {
      signal: AbortSignal;
    };
    const firstCatalogOptions = api.listAdministrators.mock.calls[0]?.[1] as {
      signal: AbortSignal;
    };

    await wrapper.get('[data-testid="session-state-filter"]').setValue('2');
    getAccessStore().accessCodes = [accessPermission, sessionsReadPermission];
    await flushPromises();
    getAccessStore().accessCodes = [
      accessPermission,
      administratorsReadPermission,
      sessionsReadPermission,
    ];
    await flushPromises();

    expect(firstSessionOptions.signal.aborted).toBe(true);
    expect(firstCatalogOptions.signal.aborted).toBe(true);
    secondSessionList.resolve(
      sessionPage([sessionFamily({ userName: 'Fresh session owner' })]),
    );
    secondAdministratorCatalog.resolve(
      administratorPage([administrator({ userName: 'Fresh selector owner' })]),
    );
    await flushPromises();
    firstSessionList.resolve(
      sessionPage([sessionFamily({ userName: 'Stale session owner' })]),
    );
    firstAdministratorCatalog.resolve(
      administratorPage([administrator({ userName: 'Stale selector owner' })]),
    );
    await flushPromises();

    expect(wrapper.text()).toContain('Fresh session owner');
    expect(wrapper.text()).toContain('Fresh selector owner');
    expect(wrapper.text()).not.toContain('Stale session owner');
    expect(wrapper.text()).not.toContain('Stale selector owner');
  });

  it('uses the default seven-day audit window, exact filters, and paging parameters', async () => {
    getAccessStore().accessCodes = [accessPermission, auditReadPermission];

    const wrapper = mountAudit();
    await flushPromises();

    const defaultCall = api.listAuditRecords.mock.calls[0];
    if (!defaultCall) {
      throw new Error('The default audit query was not issued.');
    }
    const [defaultQuery, defaultOptions] = defaultCall;
    expect(defaultQuery).toEqual({
      Action: undefined,
      ActorAdministratorId: undefined,
      CorrelationId: undefined,
      FromUtc: expect.any(String),
      Page: 1,
      PageSize: 20,
      RoleId: undefined,
      SubjectAdministratorId: undefined,
      Succeeded: undefined,
      ToUtc: expect.any(String),
    });
    expect(
      new Date(defaultQuery.ToUtc).getTime() -
        new Date(defaultQuery.FromUtc).getTime(),
    ).toBe(7 * 24 * 60 * 60 * 1000);
    expect(
      wrapper.get('[data-testid="audit-from-utc"]').attributes('aria-label'),
    ).toBe('From UTC');
    expect(
      wrapper.get('[data-testid="audit-to-utc"]').attributes('aria-label'),
    ).toBe('To UTC');
    expect(
      wrapper
        .get('[data-testid="audit-action-filter"]')
        .attributes('aria-label'),
    ).toBe('Audit action');
    expect(
      wrapper
        .get('[data-testid="audit-success-filter"]')
        .attributes('aria-label'),
    ).toBe('Audit outcome');
    expect(defaultOptions).toEqual(
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
    expect(api.listAdministrators).not.toHaveBeenCalled();
    expect(api.listAdminRoles).not.toHaveBeenCalled();
    api.listAuditRecords.mockResolvedValue(auditPage([auditRecord()]));

    await wrapper
      .get('[data-testid="audit-correlation-filter"]')
      .setValue('keyboard-search');
    await wrapper
      .get('[data-testid="audit-correlation-filter"]')
      .trigger('keyup.enter');
    await flushPromises();
    expect(api.listAuditRecords).toHaveBeenLastCalledWith(
      expect.objectContaining({
        CorrelationId: 'keyboard-search',
        Page: 1,
      }),
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );

    await wrapper
      .get('[data-testid="audit-from-utc"]')
      .setValue('2026-08-01T00:00:00.000Z');
    await wrapper
      .get('[data-testid="audit-to-utc"]')
      .setValue('2026-08-02T12:00:00.000Z');
    await wrapper.get('[data-testid="audit-action-filter"]').setValue('19');
    await wrapper.get('[data-testid="audit-success-filter"]').setValue('false');
    await wrapper
      .get('[data-testid="audit-actor-id-filter"]')
      .setValue(actorAdministratorId);
    await wrapper
      .get('[data-testid="audit-subject-id-filter"]')
      .setValue(administratorId);
    await wrapper.get('[data-testid="audit-role-id-filter"]').setValue(roleId);
    await wrapper
      .get('[data-testid="audit-correlation-filter"]')
      .setValue('correlation-99');
    await wrapper.get('[data-testid="search-audit"]').trigger('click');
    await flushPromises();

    expect(api.listAuditRecords).toHaveBeenLastCalledWith(
      {
        Action: 19,
        ActorAdministratorId: actorAdministratorId,
        CorrelationId: 'correlation-99',
        FromUtc: '2026-08-01T00:00:00.000Z',
        Page: 1,
        PageSize: 20,
        RoleId: roleId,
        SubjectAdministratorId: administratorId,
        Succeeded: false,
        ToUtc: '2026-08-02T12:00:00.000Z',
      },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );

    const pagination = wrapper.findComponent({ name: 'ElPagination' });
    pagination.vm.$emit('current-change', 4);
    await flushPromises();
    expect(api.listAuditRecords).toHaveBeenLastCalledWith(
      expect.objectContaining({ Page: 4, PageSize: 20 }),
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );

    pagination.vm.$emit('size-change', 50);
    await flushPromises();
    expect(api.listAuditRecords).toHaveBeenLastCalledWith(
      expect.objectContaining({ Page: 1, PageSize: 50 }),
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
  });

  it('recovers the audit list after a transient failure is retried', async () => {
    getAccessStore().accessCodes = [accessPermission, auditReadPermission];
    api.listAuditRecords
      .mockRejectedValueOnce({
        message: 'Audit list is temporarily unavailable.',
        status: 503,
      })
      .mockResolvedValueOnce(
        auditPage([auditRecord({ correlationId: 'recovered-audit-record' })]),
      );

    const wrapper = mountAudit();
    await flushPromises();

    expect(api.listAuditRecords).toHaveBeenCalledTimes(1);
    expect(wrapper.text()).toContain('Audit list is temporarily unavailable.');
    const retryButton = wrapper
      .findAll('button')
      .find((button) => button.text() === 'Retry');
    if (!retryButton) {
      throw new Error('The audit list retry command was not rendered.');
    }

    await retryButton.trigger('click');
    await flushPromises();

    expect(api.listAuditRecords).toHaveBeenCalledTimes(2);
    expect(wrapper.text()).toContain('recovered-audit-record');
    expect(wrapper.text()).not.toContain(
      'Audit list is temporarily unavailable.',
    );
  });

  it('keeps mobile audit controls labelled and uses compact pagination', async () => {
    viewport.mobile = true;
    getAccessStore().accessCodes = [accessPermission, auditReadPermission];
    api.listAuditRecords.mockResolvedValue(auditPage([auditRecord()]));

    const wrapper = mountAudit();
    await flushPromises();

    const mobileActions = wrapper.get('[data-testid="audit-mobile-actions"]');
    expect(mobileActions.attributes('aria-label')).toBe(
      `Actions for audit record ${auditRecord().id}`,
    );
    expect(wrapper.find('[content="Audit record actions"]').exists()).toBe(
      true,
    );
    expect(
      wrapper.findComponent({ name: 'ElPagination' }).props('layout'),
    ).toBe('prev, pager, next');
  });

  it('rejects invalid audit ranges locally without issuing an invalid query', async () => {
    getAccessStore().accessCodes = [accessPermission, auditReadPermission];
    const wrapper = mountAudit();
    await flushPromises();
    const requestCount = api.listAuditRecords.mock.calls.length;

    await wrapper
      .get('[data-testid="audit-from-utc"]')
      .setValue('2026-08-16T00:00:00.000Z');
    await wrapper
      .get('[data-testid="audit-to-utc"]')
      .setValue('2026-08-16T00:00:00.000Z');
    await wrapper.get('[data-testid="search-audit"]').trigger('click');
    await flushPromises();
    expect(api.listAuditRecords).toHaveBeenCalledTimes(requestCount);
    expect(wrapper.text()).toContain(
      'Use canonical UTC ISO values with From before To.',
    );
    expect(
      wrapper.get('[data-testid="audit-from-utc"]').attributes('aria-invalid'),
    ).toBe('true');
    expect(
      wrapper
        .get('[data-testid="audit-to-utc"]')
        .attributes('aria-describedby'),
    ).toBe('audit-range-filter-error');
    expect(wrapper.get('#audit-range-filter-error').attributes('role')).toBe(
      'alert',
    );

    await wrapper
      .get('[data-testid="audit-from-utc"]')
      .setValue('2026-06-01T00:00:00.000Z');
    await wrapper
      .get('[data-testid="audit-to-utc"]')
      .setValue('2026-08-16T00:00:00.000Z');
    await wrapper.get('[data-testid="search-audit"]').trigger('click');
    await flushPromises();
    expect(api.listAuditRecords).toHaveBeenCalledTimes(requestCount);
    expect(wrapper.text()).toContain('The audit range cannot exceed 31 days.');
  });

  it('invalidates a pending audit query before showing a local filter error', async () => {
    getAccessStore().accessCodes = [accessPermission, auditReadPermission];
    const pendingAuditList = deferred<ReturnType<typeof auditPage>>();
    api.listAuditRecords.mockImplementationOnce(() => pendingAuditList.promise);

    const wrapper = mountAudit();
    await flushPromises();

    const auditCall = api.listAuditRecords.mock.calls[0];
    if (!auditCall) {
      throw new Error('The default audit query was not issued.');
    }
    const auditOptions = auditCall[1] as { signal: AbortSignal };

    await wrapper
      .get('[data-testid="audit-from-utc"]')
      .setValue('not-a-canonical-utc-value');
    await wrapper.get('[data-testid="search-audit"]').trigger('click');
    await flushPromises();

    expect(api.listAuditRecords).toHaveBeenCalledTimes(1);
    expect(auditOptions.signal.aborted).toBe(true);
    expect(wrapper.text()).toContain(
      'Use canonical UTC ISO values with From before To.',
    );

    pendingAuditList.resolve(auditPage([auditRecord()]));
    await flushPromises();

    expect(wrapper.text()).not.toContain('correlation-42');
    expect(wrapper.text()).toContain(
      'No audit records match the current filters.',
    );
  });

  it('does not let older valid audit or selector responses replace newer state', async () => {
    getAccessStore().accessCodes = [
      accessPermission,
      administratorsReadPermission,
      auditReadPermission,
      rolesReadPermission,
    ];
    const firstAuditList = deferred<ReturnType<typeof auditPage>>();
    const secondAuditList = deferred<ReturnType<typeof auditPage>>();
    const firstAdministratorCatalog =
      deferred<ReturnType<typeof administratorPage>>();
    const secondAdministratorCatalog =
      deferred<ReturnType<typeof administratorPage>>();
    const firstRoleCatalog = deferred<ReturnType<typeof rolePage>>();
    const secondRoleCatalog = deferred<ReturnType<typeof rolePage>>();
    api.listAuditRecords
      .mockReturnValueOnce(firstAuditList.promise)
      .mockReturnValueOnce(secondAuditList.promise);
    api.listAdministrators
      .mockReturnValueOnce(firstAdministratorCatalog.promise)
      .mockReturnValueOnce(secondAdministratorCatalog.promise);
    api.listAdminRoles
      .mockReturnValueOnce(firstRoleCatalog.promise)
      .mockReturnValueOnce(secondRoleCatalog.promise);

    const wrapper = mountAudit();
    await flushPromises();
    const firstAuditOptions = api.listAuditRecords.mock.calls[0]?.[1] as {
      signal: AbortSignal;
    };
    const firstAdministratorOptions = api.listAdministrators.mock
      .calls[0]?.[1] as {
      signal: AbortSignal;
    };
    const firstRoleOptions = api.listAdminRoles.mock.calls[0]?.[1] as {
      signal: AbortSignal;
    };

    await wrapper.get('[data-testid="audit-action-filter"]').setValue('5');
    getAccessStore().accessCodes = [accessPermission, auditReadPermission];
    await flushPromises();
    getAccessStore().accessCodes = [
      accessPermission,
      administratorsReadPermission,
      auditReadPermission,
      rolesReadPermission,
    ];
    await flushPromises();

    expect(firstAuditOptions.signal.aborted).toBe(true);
    expect(firstAdministratorOptions.signal.aborted).toBe(true);
    expect(firstRoleOptions.signal.aborted).toBe(true);
    secondAuditList.resolve(
      auditPage([auditRecord({ correlationId: 'fresh-audit-record' })]),
    );
    secondAdministratorCatalog.resolve(
      administratorPage([administrator({ userName: 'fresh-audit-selector' })]),
    );
    secondRoleCatalog.resolve(rolePage([role({ name: 'Fresh audit role' })]));
    await flushPromises();
    firstAuditList.resolve(
      auditPage([auditRecord({ correlationId: 'stale-audit-record' })]),
    );
    firstAdministratorCatalog.resolve(
      administratorPage([administrator({ userName: 'stale-audit-selector' })]),
    );
    firstRoleCatalog.resolve(rolePage([role({ name: 'Stale audit role' })]));
    await flushPromises();

    expect(wrapper.text()).toContain('fresh-audit-record');
    expect(wrapper.text()).toContain('fresh-audit-selector');
    expect(wrapper.text()).toContain('Fresh audit role');
    expect(wrapper.text()).not.toContain('stale-audit-record');
    expect(wrapper.text()).not.toContain('stale-audit-selector');
    expect(wrapper.text()).not.toContain('Stale audit role');
  });

  it('reports clipboard failures as errors and successful copies as successes', async () => {
    api.listSessionFamilies.mockResolvedValue(sessionPage([sessionFamily()]));

    const wrapper = mountSessions();
    await flushPromises();
    const copyButton = wrapper.get(
      `[aria-label="Copy administrator ID ${administratorId}"]`,
    );

    const originalClipboard = Object.getOwnPropertyDescriptor(
      navigator,
      'clipboard',
    );
    const writeText = vi
      .fn()
      .mockRejectedValueOnce(new Error('Clipboard permission denied.'))
      .mockResolvedValueOnce(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText },
    });
    try {
      await copyButton.trigger('click');
      await flushPromises();
      expect(wrapper.find('[type="error"]').text()).toContain(
        'Administrator ID could not be copied.',
      );

      await copyButton.trigger('click');
      await flushPromises();
      expect(wrapper.find('[type="success"]').text()).toContain(
        'Administrator ID copied.',
      );
    } finally {
      if (originalClipboard) {
        Object.defineProperty(navigator, 'clipboard', originalClipboard);
      } else {
        delete (navigator as { clipboard?: unknown }).clipboard;
      }
    }
  });

  it('renders malformed server timestamps as unavailable', async () => {
    api.listSessionFamilies.mockResolvedValue(
      sessionPage([sessionFamily({ firstCreatedAtUtc: 'not-a-timestamp' })]),
    );
    api.listAuditRecords.mockResolvedValue(
      auditPage([auditRecord({ occurredAtUtc: 'not-a-timestamp' })]),
    );

    const sessions = mountSessions();
    await flushPromises();

    expect(sessions.text()).toContain('Unavailable');
    sessions.unmount();

    const audit = mountAudit();
    await flushPromises();
    expect(audit.text()).toContain('Unavailable');
    await audit.get('[data-testid="view-audit-record"]').trigger('click');
    await flushPromises();
    expect(audit.get('[data-testid="audit-record-drawer"]').text()).toContain(
      'Unavailable',
    );
  });

  it('shows an audit record in a read-only redacted detail drawer', async () => {
    getAccessStore().accessCodes = [accessPermission, auditReadPermission];
    const record = auditRecord() as AdminAccessAuditRecordDto & {
      password: string;
      tokenHash: string;
    };
    record.password = 'must-not-be-rendered';
    record.tokenHash = 'must-not-be-rendered';
    api.listAuditRecords.mockResolvedValue(auditPage([record]));

    const wrapper = mountAudit();
    await flushPromises();
    await wrapper.get('[data-testid="view-audit-record"]').trigger('click');
    await flushPromises();

    const drawer = wrapper.get('[data-testid="audit-record-drawer"]');
    expect(drawer.text()).toContain('correlation-42');
    expect(drawer.text()).toContain('Session family ID');
    expect(drawer.text()).not.toContain('must-not-be-rendered');
    expect(drawer.find('input').exists()).toBe(false);
    expect(drawer.find('select').exists()).toBe(false);
    expect(drawer.find('[data-testid="confirm-revoke-session"]').exists()).toBe(
      false,
    );
  });

  it('reconciles a forbidden audit request and redirects away from the unavailable workspace', async () => {
    getAccessStore().accessCodes = [accessPermission, auditReadPermission];
    router.currentRoute.value.path = '/admin-access/audit';
    api.listAuditRecords.mockRejectedValueOnce({
      message: 'Audit access was revoked.',
      status: 403,
    });
    authentication.refreshAuthenticatedState.mockImplementationOnce(
      async () => {
        getAccessStore().accessCodes = [accessPermission];
        return {
          accessibleRoutes: [
            {
              children: [{ path: '/admin-access/sessions' }],
              path: '/admin-access',
            },
          ],
          userInfo: {},
        };
      },
    );

    const wrapper = mountAudit();
    await flushPromises();

    expect(authentication.refreshAuthenticatedState).toHaveBeenCalledTimes(1);
    expect(router.replace).toHaveBeenCalledWith('/admin-access/sessions');
    expect(api.listAuditRecords).toHaveBeenCalledTimes(1);
    expect(api.listAdministrators).not.toHaveBeenCalled();
    expect(api.listAdminRoles).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="audit-forbidden"]').exists()).toBe(true);
  });
});
