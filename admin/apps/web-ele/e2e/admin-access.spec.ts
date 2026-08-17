import type { APIResponse, Locator, Page, Response } from '@playwright/test';

import type { AcceptanceEvidenceAudit } from './acceptance-evidence';

import { expect, test } from './acceptance-evidence';

interface ApiEnvelope<T> {
  data?: T;
  error?: null | { code: string; message: string };
  succeeded: boolean;
}

interface AccessTokenResponse {
  accessToken: string;
}

interface Administrator {
  displayName: string;
  id: string;
  isActive: boolean;
  revision: number;
  roles: Array<{ id: string; name: string }>;
  userName: string;
}

interface AdminRole {
  id: string;
  isActive: boolean;
  isBuiltIn: boolean;
  name: string;
  permissionNames: string[];
  revision: number;
}

interface AdminRolePage {
  items: AdminRole[];
}

interface AdminSessionFamily {
  administratorId: string;
  familyId: string;
  state: number;
  userName: string;
}

interface AdminSessionPage {
  items: AdminSessionFamily[];
}

interface AuditRecord {
  actorAdministratorId: null | string;
  sessionFamilyId: null | string;
  subjectAdministratorId: null | string;
  subjectUserName: null | string;
}

interface AuditRecordPage {
  items: AuditRecord[];
}

const accessPermission = 'Operations.AdminAccess.Access';
const administratorsReadPermission =
  'Operations.AdminAccess.Administrators.Read';
const gameConfigAccessPermission = 'Content.GameConfig.Access';
const loginReadyTimeoutMs = 120_000;
const rolesManagePermission = 'Operations.AdminAccess.Roles.Manage';
const rolesReadPermission = 'Operations.AdminAccess.Roles.Read';
const visualOutputDirectory = 'output/playwright/visual';

test.use({ trace: 'off' });

function requireEnvironment(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Required E2E environment variable '${name}' is missing.`);
  }
  return value;
}

function apiUrl(baseURL: string, path: string): string {
  return new URL(`/api${path}`, baseURL).toString();
}

function authorizationHeaders(accessToken: string): Record<string, string> {
  return {
    Authorization: `Bearer ${accessToken}`,
    'Content-Type': 'application/json',
    'X-Correlation-Id': crypto.randomUUID(),
  };
}

function mutationHeaders(
  accessToken: string,
  revision: number,
  includeIdempotencyKey = false,
): Record<string, string> {
  return {
    ...authorizationHeaders(accessToken),
    ...(includeIdempotencyKey
      ? { 'Idempotency-Key': crypto.randomUUID() }
      : {}),
    'If-Match': `"${revision}"`,
  };
}

function formField(page: Page, testId: string): Locator {
  return page
    .locator(
      `input[data-testid="${testId}"], textarea[data-testid="${testId}"], ` +
        `[data-testid="${testId}"] input, [data-testid="${testId}"] textarea`,
    )
    .first();
}

function tableRow(page: Page, testId: string, text: string): Locator {
  return page.getByTestId(testId).getByRole('row').filter({ hasText: text });
}

function waitForApiResponse(
  page: Page,
  method: string,
  path: RegExp | string,
  expectedStatus?: number,
): Promise<Response> {
  return page.waitForResponse(
    (response) => {
      const pathname = new URL(response.url()).pathname;
      return (
        response.request().method() === method &&
        (typeof path === 'string' ? pathname === path : path.test(pathname)) &&
        (expectedStatus === undefined || response.status() === expectedStatus)
      );
    },
    { timeout: 60_000 },
  );
}

async function readApiData<T>(
  response: APIResponse | Response,
  acceptanceEvidence: AcceptanceEvidenceAudit,
  method?: string,
): Promise<T> {
  await acceptanceEvidence.auditResponse(response, method);
  const text = await response.text();
  let body: ApiEnvelope<T>;
  try {
    body = JSON.parse(text) as ApiEnvelope<T>;
  } catch {
    throw new Error(
      `NovaGame API returned non-JSON status ${response.status()}.`,
    );
  }
  if (!response.ok() || !body.succeeded || body.data === undefined) {
    throw new Error(
      `NovaGame API failed with status ${response.status()} and code ` +
        `'${body.error?.code ?? 'NovaGame.UnknownError'}'.`,
    );
  }
  return body.data;
}

async function assertResponseStatus(
  response: APIResponse | Response,
  acceptanceEvidence: AcceptanceEvidenceAudit,
  expectedStatus: number,
  method: string,
): Promise<void> {
  await acceptanceEvidence.auditResponse(response, method);
  expect(response.status()).toBe(expectedStatus);
}

async function login(
  page: Page,
  userName: string,
  password: string,
  acceptanceEvidence: AcceptanceEvidenceAudit,
  assertDefaultRefreshCookie = true,
): Promise<string> {
  acceptanceEvidence.registerSensitiveValue(password);
  await page.goto('/auth/login', {
    timeout: loginReadyTimeoutMs,
    waitUntil: 'commit',
  });
  await expect(formField(page, 'login-username')).toBeVisible({
    timeout: loginReadyTimeoutMs,
  });
  await formField(page, 'login-username').fill(userName);
  await formField(page, 'login-password').fill(password);
  const loginResponse = waitForApiResponse(page, 'POST', '/api/auth/login');
  await page.getByRole('button', { name: 'login' }).click();
  const session = await readApiData<AccessTokenResponse>(
    await loginResponse,
    acceptanceEvidence,
    'POST',
  );
  await expect(page).toHaveURL(/\/game-config(?:[/?#].*)?$/, {
    timeout: 60_000,
  });
  await expect(
    page.getByRole('heading', { name: 'Game configuration' }),
  ).toBeVisible({ timeout: 60_000 });
  if (assertDefaultRefreshCookie) {
    await acceptanceEvidence.assertCurrentRefreshCookie();
  }
  return session.accessToken;
}

async function authenticateWithoutAdministratorAccess(
  page: Page,
  userName: string,
  password: string,
  acceptanceEvidence: AcceptanceEvidenceAudit,
): Promise<string> {
  acceptanceEvidence.registerSensitiveValue(password);
  await page.goto('/auth/login', {
    timeout: loginReadyTimeoutMs,
    waitUntil: 'commit',
  });
  await expect(formField(page, 'login-username')).toBeVisible({
    timeout: loginReadyTimeoutMs,
  });
  await formField(page, 'login-username').fill(userName);
  await formField(page, 'login-password').fill(password);
  const loginResponse = waitForApiResponse(page, 'POST', '/api/auth/login');
  const permissionCodesResponse = waitForApiResponse(
    page,
    'GET',
    '/api/auth/codes',
    403,
  );
  const userInfoResponse = waitForApiResponse(
    page,
    'GET',
    '/api/user/info',
    403,
  );
  await page.getByRole('button', { name: 'login' }).click();
  const session = await readApiData<AccessTokenResponse>(
    await loginResponse,
    acceptanceEvidence,
    'POST',
  );
  await assertResponseStatus(
    await permissionCodesResponse,
    acceptanceEvidence,
    403,
    'GET',
  );
  await assertResponseStatus(
    await userInfoResponse,
    acceptanceEvidence,
    403,
    'GET',
  );
  await expect(page).toHaveURL(/\/auth\/login(?:[?#].*)?$/);
  await acceptanceEvidence.assertCurrentRefreshCookie();
  return session.accessToken;
}

async function logout(
  page: Page,
  acceptanceEvidence: AcceptanceEvidenceAudit,
): Promise<void> {
  const logoutResponse = waitForApiResponse(page, 'POST', '/api/auth/logout');
  await page.keyboard.press('Alt+Q');
  const confirmation = page
    .getByRole('dialog')
    .filter({ hasText: 'Do you want to logout?' });
  await expect(confirmation).toBeVisible();
  await confirmation.getByRole('button', { name: 'Confirm' }).click();
  await acceptanceEvidence.auditResponse(await logoutResponse, 'POST');
  await expect(page).toHaveURL(/\/auth\/login(?:[?#].*)?$/);
}

async function assertBrowserRefreshRejected(
  page: Page,
  acceptanceEvidence: AcceptanceEvidenceAudit,
): Promise<void> {
  const refreshResponse = waitForApiResponse(
    page,
    'POST',
    '/api/auth/refresh',
    401,
  );
  await page.evaluate(() => localStorage.clear());
  await page.reload({ timeout: loginReadyTimeoutMs, waitUntil: 'commit' });
  await assertResponseStatus(
    await refreshResponse,
    acceptanceEvidence,
    401,
    'POST',
  );
  await expect(page).toHaveURL(/\/auth\/login(?:[?#].*)?$/);
}

async function openWorkspace(
  page: Page,
  path: string,
  heading: string,
  tableTestId: string,
): Promise<void> {
  if (new URL(page.url()).pathname !== path) {
    await page.goto(path, { timeout: 60_000, waitUntil: 'commit' });
  }
  await expect(page.getByRole('heading', { name: heading })).toBeVisible({
    timeout: 60_000,
  });
  const table = page.getByTestId(tableTestId);
  await expect(table).toBeVisible({ timeout: 60_000 });
  await expect(table.locator('.el-loading-mask')).toBeHidden({
    timeout: 60_000,
  });
}

async function assertNoBodyOverflow(page: Page): Promise<void> {
  const width = await page.locator('body').evaluate((body) => ({
    clientWidth: body.clientWidth,
    scrollWidth: body.scrollWidth,
  }));
  expect(width.scrollWidth).toBeLessThanOrEqual(width.clientWidth);
}

test('administrator lifecycle and concurrency', async ({
  acceptanceEvidence,
  page,
}) => {
  test.setTimeout(300_000);

  const apiBaseURL = requireEnvironment('NOVAGAME_E2E_API_URL');
  const bootstrapPassword = requireEnvironment('NOVAGAME_E2E_PASSWORD');
  const bootstrapUserName = requireEnvironment('NOVAGAME_E2E_USERNAME');
  const runId = crypto.randomUUID();
  const administratorPassword = `Lifecycle-${runId}-Password!`;
  const resetPassword = `Lifecycle-Reset-${runId}-Password!`;
  const userName = `e2e-lifecycle-${runId}`;

  const bootstrapAccessToken = await login(
    page,
    bootstrapUserName,
    bootstrapPassword,
    acceptanceEvidence,
  );
  acceptanceEvidence.registerSensitiveValue(administratorPassword);
  acceptanceEvidence.registerSensitiveValue(resetPassword);

  const roles = await readApiData<AdminRolePage>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'GET',
      apiUrl(apiBaseURL, '/admin-access/roles?page=1&pageSize=100'),
      { headers: authorizationHeaders(bootstrapAccessToken) },
    ),
    acceptanceEvidence,
    'GET',
  );
  const builtInRole = roles.items.find((role) => role.isBuiltIn);
  if (!builtInRole) {
    throw new Error('The bootstrap administrator must have one built-in role.');
  }

  const createdAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/administrators'),
      {
        data: {
          displayName: 'Lifecycle administrator',
          initialPassword: administratorPassword,
          roleIds: [builtInRole.id],
          userName,
        },
        headers: {
          ...authorizationHeaders(bootstrapAccessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );

  await openWorkspace(
    page,
    '/admin-access/administrators',
    'Administrators',
    'administrator-table',
  );
  await formField(page, 'administrator-search').fill(userName);
  await formField(page, 'administrator-search').press('Enter');
  await expect(tableRow(page, 'administrator-table', userName)).toContainText(
    'Lifecycle administrator',
  );

  const readOnlyRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/roles'),
      {
        data: {
          name: `Lifecycle reader ${runId}`,
          permissionNames: [
            accessPermission,
            administratorsReadPermission,
            gameConfigAccessPermission,
          ],
        },
        headers: {
          ...authorizationHeaders(bootstrapAccessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );

  const renamedAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(
        apiBaseURL,
        `/admin-access/administrators/${createdAdministrator.id}/display-name`,
      ),
      {
        data: { displayName: 'Lifecycle administrator updated' },
        headers: mutationHeaders(
          bootstrapAccessToken,
          createdAdministrator.revision,
        ),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  const roleChangedAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(
        apiBaseURL,
        `/admin-access/administrators/${createdAdministrator.id}/roles`,
      ),
      {
        data: { roleIds: [readOnlyRole.id] },
        headers: mutationHeaders(
          bootstrapAccessToken,
          renamedAdministrator.revision,
        ),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );

  const temporarilyInactiveAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(
        apiBaseURL,
        `/admin-access/administrators/${createdAdministrator.id}/active-state`,
      ),
      {
        data: { isActive: false },
        headers: mutationHeaders(
          bootstrapAccessToken,
          roleChangedAdministrator.revision,
        ),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  const activeAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(
        apiBaseURL,
        `/admin-access/administrators/${createdAdministrator.id}/active-state`,
      ),
      {
        data: { isActive: true },
        headers: mutationHeaders(
          bootstrapAccessToken,
          temporarilyInactiveAdministrator.revision,
        ),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );

  const secondaryContext = await acceptanceEvidence.newObservedContext();
  const secondaryPage = await secondaryContext.newPage();
  await login(
    secondaryPage,
    userName,
    administratorPassword,
    acceptanceEvidence,
    false,
  );
  await openWorkspace(
    secondaryPage,
    '/admin-access/administrators',
    'Administrators',
    'administrator-table',
  );
  await expect(secondaryPage.getByTestId('create-administrator')).toHaveCount(
    0,
  );

  const currentAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(
        apiBaseURL,
        `/admin-access/administrators/${createdAdministrator.id}/display-name`,
      ),
      {
        data: { displayName: 'Lifecycle concurrency winner' },
        headers: mutationHeaders(
          bootstrapAccessToken,
          activeAdministrator.revision,
        ),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  const staleResponse = await acceptanceEvidence.apiRequest(
    page.request,
    'PUT',
    apiUrl(
      apiBaseURL,
      `/admin-access/administrators/${createdAdministrator.id}/display-name`,
    ),
    {
      data: { displayName: 'Lifecycle stale update' },
      headers: mutationHeaders(
        bootstrapAccessToken,
        activeAdministrator.revision,
      ),
    },
  );
  await assertResponseStatus(staleResponse, acceptanceEvidence, 409, 'PUT');

  const resetKey = crypto.randomUUID();
  const resetHeaders = {
    ...mutationHeaders(bootstrapAccessToken, currentAdministrator.revision),
    'Idempotency-Key': resetKey,
  };
  const resetAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(
        apiBaseURL,
        `/admin-access/administrators/${createdAdministrator.id}/password-reset`,
      ),
      { data: { newPassword: resetPassword }, headers: resetHeaders },
    ),
    acceptanceEvidence,
    'POST',
  );
  const replayedReset = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(
        apiBaseURL,
        `/admin-access/administrators/${createdAdministrator.id}/password-reset`,
      ),
      { data: { newPassword: resetPassword }, headers: resetHeaders },
    ),
    acceptanceEvidence,
    'POST',
  );
  expect(replayedReset.revision).toBe(resetAdministrator.revision);

  await assertBrowserRefreshRejected(secondaryPage, acceptanceEvidence);

  await login(
    secondaryPage,
    userName,
    resetPassword,
    acceptanceEvidence,
    false,
  );
  const deactivatedAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(
        apiBaseURL,
        `/admin-access/administrators/${createdAdministrator.id}/active-state`,
      ),
      {
        data: { isActive: false },
        headers: mutationHeaders(
          bootstrapAccessToken,
          resetAdministrator.revision,
        ),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  expect(deactivatedAdministrator.isActive).toBe(false);
  await assertBrowserRefreshRejected(secondaryPage, acceptanceEvidence);

  await logout(page, acceptanceEvidence);
});

test('roles, permissions, and authorization', async ({
  acceptanceEvidence,
  page,
}) => {
  test.setTimeout(300_000);

  const apiBaseURL = requireEnvironment('NOVAGAME_E2E_API_URL');
  const bootstrapPassword = requireEnvironment('NOVAGAME_E2E_PASSWORD');
  const bootstrapUserName = requireEnvironment('NOVAGAME_E2E_USERNAME');
  const runId = crypto.randomUUID();
  const accessOnlyPassword = `Access-only-${runId}-Password!`;
  const leafOnlyPassword = `Leaf-only-${runId}-Password!`;
  const viewerPassword = `Viewer-${runId}-Password!`;

  const bootstrapAccessToken = await login(
    page,
    bootstrapUserName,
    bootstrapPassword,
    acceptanceEvidence,
  );
  acceptanceEvidence.registerSensitiveValue(accessOnlyPassword);
  acceptanceEvidence.registerSensitiveValue(leafOnlyPassword);
  acceptanceEvidence.registerSensitiveValue(viewerPassword);

  const managedRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/roles'),
      {
        data: {
          name: `Operations manager ${runId}`,
          permissionNames: [
            accessPermission,
            rolesManagePermission,
            rolesReadPermission,
          ],
        },
        headers: {
          ...authorizationHeaders(bootstrapAccessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );
  const renamedRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(apiBaseURL, `/admin-access/roles/${managedRole.id}/name`),
      {
        data: { name: `Operations manager renamed ${runId}` },
        headers: mutationHeaders(bootstrapAccessToken, managedRole.revision),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  const permissionReplacedRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(apiBaseURL, `/admin-access/roles/${managedRole.id}/permissions`),
      {
        data: { permissionNames: [accessPermission, rolesReadPermission] },
        headers: mutationHeaders(bootstrapAccessToken, renamedRole.revision),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  const inactiveRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(apiBaseURL, `/admin-access/roles/${managedRole.id}/active-state`),
      {
        data: { isActive: false },
        headers: mutationHeaders(
          bootstrapAccessToken,
          permissionReplacedRole.revision,
        ),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  expect(inactiveRole.isActive).toBe(false);

  await openWorkspace(
    page,
    '/admin-access/roles',
    'Roles and permissions',
    'role-table',
  );
  await expect(page.getByTestId('built-in-role-read-only')).toBeVisible();
  await formField(page, 'role-search').fill(
    `Operations manager renamed ${runId}`,
  );
  await formField(page, 'role-search').press('Enter');
  await expect(
    tableRow(page, 'role-table', `Operations manager renamed ${runId}`),
  ).toContainText('Inactive');

  const accessOnlyRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/roles'),
      {
        data: {
          name: `Access only ${runId}`,
          permissionNames: [accessPermission, gameConfigAccessPermission],
        },
        headers: {
          ...authorizationHeaders(bootstrapAccessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );
  const accessOnlyAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/administrators'),
      {
        data: {
          displayName: 'Access-only administrator',
          initialPassword: accessOnlyPassword,
          roleIds: [accessOnlyRole.id],
          userName: `e2e-access-only-${runId}`,
        },
        headers: {
          ...authorizationHeaders(bootstrapAccessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );
  await logout(page, acceptanceEvidence);
  const accessOnlyAccessToken = await login(
    page,
    accessOnlyAdministrator.userName,
    accessOnlyPassword,
    acceptanceEvidence,
  );

  const forbiddenRequests: ReadonlyArray<{
    data?: object;
    method: 'GET' | 'POST';
    path: string;
  }> = [
    { method: 'GET', path: '/admin-access/administrators?page=1&pageSize=20' },
    { data: {}, method: 'POST', path: '/admin-access/administrators' },
    {
      data: {},
      method: 'POST',
      path: `/admin-access/administrators/${crypto.randomUUID()}/password-reset`,
    },
    { method: 'GET', path: '/admin-access/roles?page=1&pageSize=20' },
    { data: {}, method: 'POST', path: '/admin-access/roles' },
    { method: 'GET', path: '/admin-access/sessions?page=1&pageSize=20' },
    {
      method: 'POST',
      path: `/admin-access/sessions/${crypto.randomUUID()}/revoke`,
    },
    {
      method: 'GET',
      path: '/admin-access/audit-records?fromUtc=2025-01-01T00%3A00%3A00.000Z&toUtc=2030-01-01T00%3A00%3A00.000Z&page=1&pageSize=20',
    },
  ];
  for (const request of forbiddenRequests) {
    const response = await acceptanceEvidence.apiRequest(
      page.request,
      request.method,
      apiUrl(apiBaseURL, request.path),
      {
        ...(request.data === undefined ? {} : { data: request.data }),
        headers: authorizationHeaders(accessOnlyAccessToken),
      },
    );
    await assertResponseStatus(
      response,
      acceptanceEvidence,
      403,
      request.method,
    );
  }

  const leafOnlyRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/roles'),
      {
        data: {
          name: `Leaf without access ${runId}`,
          permissionNames: [administratorsReadPermission],
        },
        headers: {
          ...authorizationHeaders(bootstrapAccessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );
  const leafOnlyAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/administrators'),
      {
        data: {
          displayName: 'Leaf-only administrator',
          initialPassword: leafOnlyPassword,
          roleIds: [leafOnlyRole.id],
          userName: `e2e-leaf-only-${runId}`,
        },
        headers: {
          ...authorizationHeaders(bootstrapAccessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );
  await logout(page, acceptanceEvidence);
  const leafOnlyAccessToken = await authenticateWithoutAdministratorAccess(
    page,
    leafOnlyAdministrator.userName,
    leafOnlyPassword,
    acceptanceEvidence,
  );
  const missingAccessResponse = await acceptanceEvidence.apiRequest(
    page.request,
    'GET',
    apiUrl(apiBaseURL, '/admin-access/administrators?page=1&pageSize=20'),
    { headers: authorizationHeaders(leafOnlyAccessToken) },
  );
  await assertResponseStatus(
    missingAccessResponse,
    acceptanceEvidence,
    403,
    'GET',
  );

  const viewerRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/roles'),
      {
        data: {
          name: `Administrator viewer ${runId}`,
          permissionNames: [
            accessPermission,
            administratorsReadPermission,
            gameConfigAccessPermission,
          ],
        },
        headers: {
          ...authorizationHeaders(bootstrapAccessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );
  const viewerAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/administrators'),
      {
        data: {
          displayName: 'Administrator viewer',
          initialPassword: viewerPassword,
          roleIds: [viewerRole.id],
          userName: `e2e-viewer-${runId}`,
        },
        headers: {
          ...authorizationHeaders(bootstrapAccessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );

  await page.evaluate(() => localStorage.clear());
  await page.reload({ timeout: loginReadyTimeoutMs, waitUntil: 'commit' });
  const viewerAccessToken = await login(
    page,
    viewerAdministrator.userName,
    viewerPassword,
    acceptanceEvidence,
  );
  await openWorkspace(
    page,
    '/admin-access/administrators',
    'Administrators',
    'administrator-table',
  );
  await expect(page.getByTestId('create-administrator')).toHaveCount(0);

  const refreshedViewerRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(apiBaseURL, `/admin-access/roles/${viewerRole.id}/permissions`),
      {
        data: {
          permissionNames: [
            accessPermission,
            administratorsReadPermission,
            rolesReadPermission,
            gameConfigAccessPermission,
          ],
        },
        headers: mutationHeaders(bootstrapAccessToken, viewerRole.revision),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  expect(refreshedViewerRole.permissionNames).toContain(rolesReadPermission);
  await logout(page, acceptanceEvidence);
  await login(
    page,
    viewerAdministrator.userName,
    viewerPassword,
    acceptanceEvidence,
  );
  await openWorkspace(
    page,
    '/admin-access/roles',
    'Roles and permissions',
    'role-table',
  );
  await expect(page.getByTestId('create-role')).toHaveCount(0);
  const rolesManageResponse = await acceptanceEvidence.apiRequest(
    page.request,
    'POST',
    apiUrl(apiBaseURL, '/admin-access/roles'),
    {
      data: {},
      headers: authorizationHeaders(viewerAccessToken),
    },
  );
  await assertResponseStatus(
    rolesManageResponse,
    acceptanceEvidence,
    403,
    'POST',
  );

  await logout(page, acceptanceEvidence);
});

test('sessions and audit', async ({ acceptanceEvidence, page }) => {
  test.setTimeout(300_000);

  const apiBaseURL = requireEnvironment('NOVAGAME_E2E_API_URL');
  const bootstrapPassword = requireEnvironment('NOVAGAME_E2E_PASSWORD');
  const bootstrapUserName = requireEnvironment('NOVAGAME_E2E_USERNAME');
  const runId = crypto.randomUUID();
  const sessionPassword = `Session-${runId}-Password!`;
  const sessionUserName = `e2e-session-${runId}`;

  let bootstrapAccessToken = await login(
    page,
    bootstrapUserName,
    bootstrapPassword,
    acceptanceEvidence,
  );
  acceptanceEvidence.registerSensitiveValue(sessionPassword);
  const roles = await readApiData<AdminRolePage>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'GET',
      apiUrl(apiBaseURL, '/admin-access/roles?page=1&pageSize=100'),
      { headers: authorizationHeaders(bootstrapAccessToken) },
    ),
    acceptanceEvidence,
    'GET',
  );
  const builtInRole = roles.items.find((role) => role.isBuiltIn);
  if (!builtInRole) {
    throw new Error('The session acceptance requires the built-in role.');
  }
  const sessionAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/administrators'),
      {
        data: {
          displayName: 'Session acceptance administrator',
          initialPassword: sessionPassword,
          roleIds: [builtInRole.id],
          userName: sessionUserName,
        },
        headers: {
          ...authorizationHeaders(bootstrapAccessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );
  await page.evaluate(() => localStorage.clear());
  await login(page, sessionUserName, sessionPassword, acceptanceEvidence);
  await page.evaluate(() => localStorage.clear());
  bootstrapAccessToken = await login(
    page,
    bootstrapUserName,
    bootstrapPassword,
    acceptanceEvidence,
  );

  const sessionFamilies = await readApiData<AdminSessionPage>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'GET',
      apiUrl(
        apiBaseURL,
        `/admin-access/sessions?administratorId=${encodeURIComponent(sessionAdministrator.id)}&page=1&pageSize=20`,
      ),
      { headers: authorizationHeaders(bootstrapAccessToken) },
    ),
    acceptanceEvidence,
    'GET',
  );
  const sessionFamily = sessionFamilies.items.find(
    (family) => family.administratorId === sessionAdministrator.id,
  );
  if (!sessionFamily) {
    throw new Error('The created administrator session was not projected.');
  }
  expect(JSON.stringify(sessionFamily)).not.toContain('token');

  await openWorkspace(
    page,
    '/admin-access/sessions',
    'Sessions',
    'session-table',
  );
  const sessionRow = tableRow(page, 'session-table', sessionUserName);
  await expect(sessionRow).toBeVisible({ timeout: 60_000 });
  const revokeResponse = waitForApiResponse(
    page,
    'POST',
    `/api/admin-access/sessions/${sessionFamily.familyId}/revoke`,
  );
  await sessionRow.getByTestId('revoke-session').click();
  await expect(
    page.getByRole('dialog', { name: 'Revoke session family' }),
  ).toBeVisible();
  await page.getByTestId('confirm-revoke-session').click();
  const revokedSession = await readApiData<AdminSessionFamily>(
    await revokeResponse,
    acceptanceEvidence,
    'POST',
  );
  expect(revokedSession.familyId).toBe(sessionFamily.familyId);

  const repeatedRevoke = await readApiData<AdminSessionFamily>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(
        apiBaseURL,
        `/admin-access/sessions/${sessionFamily.familyId}/revoke`,
      ),
      { headers: authorizationHeaders(bootstrapAccessToken) },
    ),
    acceptanceEvidence,
    'POST',
  );
  expect(repeatedRevoke.familyId).toBe(sessionFamily.familyId);

  const fromUtc = new Date(Date.now() - 3_600_000).toISOString();
  const toUtc = new Date(Date.now() + 3_600_000).toISOString();
  const audits = await readApiData<AuditRecordPage>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'GET',
      apiUrl(
        apiBaseURL,
        `/admin-access/audit-records?fromUtc=${encodeURIComponent(fromUtc)}&toUtc=${encodeURIComponent(toUtc)}&subjectAdministratorId=${encodeURIComponent(sessionAdministrator.id)}&page=1&pageSize=20`,
      ),
      { headers: authorizationHeaders(bootstrapAccessToken) },
    ),
    acceptanceEvidence,
    'GET',
  );
  expect(
    audits.items.some(
      (record) =>
        record.subjectAdministratorId === sessionAdministrator.id ||
        record.sessionFamilyId === sessionFamily.familyId ||
        record.subjectUserName === sessionUserName,
    ),
  ).toBe(true);

  await openWorkspace(
    page,
    '/admin-access/audit',
    'Audit history',
    'audit-table',
  );
  await formField(page, 'audit-from-utc').fill(fromUtc);
  await formField(page, 'audit-to-utc').fill(toUtc);
  await page.getByTestId('audit-subject-filter').click();
  const auditSubjectOption = page.getByRole('option', {
    name: sessionUserName,
  });
  await expect(auditSubjectOption).toBeVisible({ timeout: 60_000 });
  const filteredAuditResponse = waitForApiResponse(
    page,
    'GET',
    '/api/admin-access/audit-records',
  );
  await auditSubjectOption.click();
  const filteredAudits = await readApiData<AuditRecordPage>(
    await filteredAuditResponse,
    acceptanceEvidence,
    'GET',
  );
  expect(
    filteredAudits.items.some(
      (record) => record.subjectAdministratorId === sessionAdministrator.id,
    ),
  ).toBe(true);
  await expect(
    tableRow(page, 'audit-table', sessionAdministrator.id).first(),
  ).toBeVisible({
    timeout: 60_000,
  });

  await logout(page, acceptanceEvidence);
});

test('responsive and failure states', async ({ acceptanceEvidence, page }) => {
  test.setTimeout(300_000);

  const apiBaseURL = requireEnvironment('NOVAGAME_E2E_API_URL');
  const bootstrapPassword = requireEnvironment('NOVAGAME_E2E_PASSWORD');
  const bootstrapUserName = requireEnvironment('NOVAGAME_E2E_USERNAME');
  const runId = crypto.randomUUID();
  const longValue = `E2E-${runId}-${'long-operational-value-'.repeat(3)}`;
  const statePassword = `State-${runId}-Password!`;

  const bootstrapAccessToken = await login(
    page,
    bootstrapUserName,
    bootstrapPassword,
    acceptanceEvidence,
  );
  acceptanceEvidence.registerSensitiveValue(statePassword);
  const roles = await readApiData<AdminRolePage>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'GET',
      apiUrl(apiBaseURL, '/admin-access/roles?page=1&pageSize=100'),
      { headers: authorizationHeaders(bootstrapAccessToken) },
    ),
    acceptanceEvidence,
    'GET',
  );
  const builtInRole = roles.items.find((role) => role.isBuiltIn);
  if (!builtInRole) {
    throw new Error('The responsive acceptance requires the built-in role.');
  }
  const stateAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/administrators'),
      {
        data: {
          displayName: longValue,
          initialPassword: statePassword,
          roleIds: [builtInRole.id],
          userName: `e2e-state-${runId}`,
        },
        headers: {
          ...authorizationHeaders(bootstrapAccessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );

  const unauthenticatedResponse = await acceptanceEvidence.apiRequest(
    page.request,
    'GET',
    apiUrl(apiBaseURL, '/admin-access/administrators?page=1&pageSize=20'),
  );
  await assertResponseStatus(
    unauthenticatedResponse,
    acceptanceEvidence,
    401,
    'GET',
  );

  const stateRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/roles'),
      {
        data: {
          name: `State access only ${runId}`,
          permissionNames: [accessPermission, gameConfigAccessPermission],
        },
        headers: {
          ...authorizationHeaders(bootstrapAccessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );
  const stateViewerPassword = `State-viewer-${runId}-Password!`;
  acceptanceEvidence.registerSensitiveValue(stateViewerPassword);
  const stateViewer = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/administrators'),
      {
        data: {
          displayName: 'State access-only administrator',
          initialPassword: stateViewerPassword,
          roleIds: [stateRole.id],
          userName: `e2e-state-viewer-${runId}`,
        },
        headers: {
          ...authorizationHeaders(bootstrapAccessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );
  await logout(page, acceptanceEvidence);
  const stateViewerAccessToken = await login(
    page,
    stateViewer.userName,
    stateViewerPassword,
    acceptanceEvidence,
  );
  const forbiddenResponse = await acceptanceEvidence.apiRequest(
    page.request,
    'GET',
    apiUrl(apiBaseURL, '/admin-access/administrators?page=1&pageSize=20'),
    { headers: authorizationHeaders(stateViewerAccessToken) },
  );
  await assertResponseStatus(forbiddenResponse, acceptanceEvidence, 403, 'GET');

  await logout(page, acceptanceEvidence);
  await login(page, bootstrapUserName, bootstrapPassword, acceptanceEvidence);

  const notFoundResponse = await acceptanceEvidence.apiRequest(
    page.request,
    'GET',
    apiUrl(apiBaseURL, `/admin-access/administrators/${crypto.randomUUID()}`),
    { headers: authorizationHeaders(bootstrapAccessToken) },
  );
  await assertResponseStatus(notFoundResponse, acceptanceEvidence, 404, 'GET');
  const updatedStateAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(
        apiBaseURL,
        `/admin-access/administrators/${stateAdministrator.id}/display-name`,
      ),
      {
        data: { displayName: `${longValue} updated` },
        headers: mutationHeaders(
          bootstrapAccessToken,
          stateAdministrator.revision,
        ),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  const conflictResponse = await acceptanceEvidence.apiRequest(
    page.request,
    'PUT',
    apiUrl(
      apiBaseURL,
      `/admin-access/administrators/${stateAdministrator.id}/display-name`,
    ),
    {
      data: { displayName: `${longValue} stale` },
      headers: mutationHeaders(
        bootstrapAccessToken,
        stateAdministrator.revision,
      ),
    },
  );
  await assertResponseStatus(conflictResponse, acceptanceEvidence, 409, 'PUT');
  expect(updatedStateAdministrator.revision).toBeGreaterThan(
    stateAdministrator.revision,
  );

  const workspaces = [
    {
      heading: 'Administrators',
      path: '/admin-access/administrators',
      screenshot: `${visualOutputDirectory}/admin-access-administrators-desktop-1440x900.png`,
      tableTestId: 'administrator-table',
    },
    {
      heading: 'Roles and permissions',
      path: '/admin-access/roles',
      screenshot: `${visualOutputDirectory}/admin-access-roles-desktop-1440x900.png`,
      tableTestId: 'role-table',
    },
    {
      heading: 'Sessions',
      path: '/admin-access/sessions',
      screenshot: `${visualOutputDirectory}/admin-access-sessions-desktop-1440x900.png`,
      tableTestId: 'session-table',
    },
    {
      heading: 'Audit history',
      path: '/admin-access/audit',
      screenshot: `${visualOutputDirectory}/admin-access-audit-desktop-1440x900.png`,
      tableTestId: 'audit-table',
    },
  ] as const;

  await page.setViewportSize({ height: 844, width: 390 });
  for (const workspace of workspaces) {
    await openWorkspace(
      page,
      workspace.path,
      workspace.heading,
      workspace.tableTestId,
    );
    await assertNoBodyOverflow(page);
  }
  await openWorkspace(
    page,
    '/admin-access/roles',
    'Roles and permissions',
    'role-table',
  );
  await page.getByTestId('create-role').focus();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('role-create-drawer')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByTestId('role-create-drawer')).toHaveCount(0);

  await page.setViewportSize({ height: 900, width: 1440 });
  for (const workspace of workspaces) {
    await openWorkspace(
      page,
      workspace.path,
      workspace.heading,
      workspace.tableTestId,
    );
    await assertNoBodyOverflow(page);
    await acceptanceEvidence.captureScreenshot(page, workspace.screenshot);
  }

  await openWorkspace(
    page,
    '/admin-access/administrators',
    'Administrators',
    'administrator-table',
  );
  await acceptanceEvidence.withExpectedApiNetworkFailure(
    page,
    {
      exactPathAndQuery: '/api/admin-access/administrators?Page=1&PageSize=20',
      method: 'GET',
    },
    async () => {
      await page.getByTestId('refresh-administrators').click();
    },
  );

  await logout(page, acceptanceEvidence);
});
