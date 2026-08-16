import type {
  APIResponse,
  Locator,
  Page,
  Request,
  Response,
} from '@playwright/test';

import type { AcceptanceEvidenceAudit } from './acceptance-evidence';

import { readFile } from 'node:fs/promises';

import { expect, test } from './acceptance-evidence';

interface ApiEnvelope<T> {
  data?: T;
  error?: null | { code: string; message: string };
  succeeded: boolean;
}

interface AccessTokenResponse {
  accessToken: string;
}

interface AdminRole {
  id: string;
  permissionNames: string[];
}

interface Draft {
  id: string;
  name: string;
  revision: number;
}

interface Environment {
  activeVersionId: string;
  previousVersionId: null | string;
  revision: number;
}

interface Snapshot {
  configurationSet: string;
  configurationVersion: string;
  content: unknown;
  schemaVersion: string;
}

interface ValidationResult {
  isValid: boolean;
  revision: number;
}

interface Version {
  id: string;
  sourceDraftId: string;
}

const configurationSet = 'default';
const environmentName = 'development';
const gameConfigAccessPermission = 'Content.GameConfig.Access';
const loginPageReadyTimeoutMs = 120_000;
const operationsAdminAccessPermission = 'Operations.AdminAccess.Access';
const schemaVersion = '1';
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

function formField(page: Page, testId: string): Locator {
  return page
    .locator(
      `input[data-testid="${testId}"], textarea[data-testid="${testId}"], ` +
        `[data-testid="${testId}"] input, [data-testid="${testId}"] textarea`,
    )
    .first();
}

function drawer(page: Page, testId: string): Locator {
  return page
    .getByTestId(testId)
    .locator(
      'xpath=ancestor::*[contains(concat(" ", normalize-space(@class), " "), " el-drawer ")]',
    );
}

async function readApiData<T>(
  response: APIResponse | Response,
  acceptanceEvidence: AcceptanceEvidenceAudit,
  method?: string,
): Promise<T> {
  await acceptanceEvidence.auditResponse(response, method);
  const responseText = await response.text();
  let body: ApiEnvelope<T>;
  try {
    body = JSON.parse(responseText) as ApiEnvelope<T>;
  } catch {
    throw new Error(
      `NovaGame API returned status ${response.status()} with a non-JSON response.`,
    );
  }

  if (!response.ok() || !body.succeeded) {
    const errorCode = body.error?.code ?? 'NovaGame.UnknownError';
    throw new Error(
      `NovaGame API request failed with status ${response.status()} ` +
        `and code '${errorCode}'.`,
    );
  }
  if (body.data === undefined) {
    throw new Error(
      `NovaGame API returned status ${response.status()} without success data.`,
    );
  }
  return body.data;
}

function authorizationHeaders(accessToken: string) {
  return {
    Authorization: `Bearer ${accessToken}`,
    'Content-Type': 'application/json',
    'X-Correlation-Id': crypto.randomUUID(),
  };
}

function waitForApiResponseOrFailure(
  page: Page,
  method: string,
  path: RegExp | string,
  expectedStatus?: number,
): Promise<Response> {
  const pathDescription = typeof path === 'string' ? path : path.toString();
  const matchesRequest = (request: Request) => {
    const pathname = new URL(request.url()).pathname;
    return (
      request.method() === method &&
      (typeof path === 'string' ? pathname === path : path.test(pathname))
    );
  };
  let failedRequestHandler: (request: Request) => void = () => {};
  const failedRequestPromise = new Promise<never>((_, reject) => {
    failedRequestHandler = (request) => {
      if (!matchesRequest(request)) {
        return;
      }

      reject(
        new Error(
          `${method} ${pathDescription} failed before a response: ` +
            `${request.failure()?.errorText ?? 'unknown transport failure'}.`,
        ),
      );
    };
  });
  page.on('requestfailed', failedRequestHandler);

  return Promise.race([
    page.waitForResponse(
      (response) =>
        matchesRequest(response.request()) &&
        (expectedStatus === undefined || response.status() === expectedStatus),
      { timeout: 60_000 },
    ),
    failedRequestPromise,
  ]).finally(() => {
    page.off('requestfailed', failedRequestHandler);
  });
}

async function login(
  page: Page,
  username: string,
  password: string,
  acceptanceEvidence: AcceptanceEvidenceAudit,
): Promise<string> {
  acceptanceEvidence.registerSensitiveValue(password);
  await page.goto('/auth/login', {
    timeout: loginPageReadyTimeoutMs,
    waitUntil: 'commit',
  });
  const usernameField = formField(page, 'login-username');
  const passwordField = formField(page, 'login-password');
  await expect(usernameField).toBeVisible({ timeout: loginPageReadyTimeoutMs });
  await expect(passwordField).toBeVisible({ timeout: loginPageReadyTimeoutMs });
  await usernameField.fill(username);
  await passwordField.fill(password);

  const loginResponsePromise = waitForApiResponseOrFailure(
    page,
    'POST',
    '/api/auth/login',
  );
  await page.getByRole('button', { name: 'login' }).click();
  const session = await readApiData<AccessTokenResponse>(
    await loginResponsePromise,
    acceptanceEvidence,
  );

  await expect(page).toHaveURL(/game-config/, { timeout: 60_000 });
  await expect(
    page.getByRole('heading', { name: 'Game configuration' }),
  ).toBeVisible({ timeout: 60_000 });
  await acceptanceEvidence.assertCurrentRefreshCookie();
  return session.accessToken;
}

async function logout(page: Page): Promise<void> {
  await page.keyboard.press('Alt+Q');
  const confirmation = page
    .getByRole('dialog')
    .filter({ hasText: 'Do you want to logout?' });
  await expect(confirmation).toBeVisible();
  await confirmation.getByRole('button', { name: 'Confirm' }).click();
  await expect(page).toHaveURL(/\/auth\/login(?:[?#].*)?$/);
}

async function captureWorkspaceViewport(
  page: Page,
  name: string,
  viewport: { height: number; width: number },
  acceptanceEvidence: AcceptanceEvidenceAudit,
): Promise<void> {
  await page.setViewportSize(viewport);
  await expect(
    page.getByRole('heading', { name: 'Game configuration' }),
  ).toBeVisible();
  await expect(page.getByTestId('draft-table')).toBeVisible();
  await expect(page.locator('.el-loading-mask')).toHaveCount(0);
  await expect(page.locator('.el-notification')).toHaveCount(0);

  const bodyWidth = await page.locator('body').evaluate((body) => ({
    clientWidth: body.clientWidth,
    scrollWidth: body.scrollWidth,
  }));
  expect(
    bodyWidth.scrollWidth,
    `${name} body width ${bodyWidth.scrollWidth}px exceeds ` +
      `${bodyWidth.clientWidth}px.`,
  ).toBeLessThanOrEqual(bodyWidth.clientWidth);

  await acceptanceEvidence.captureScreenshot(
    page,
    `${visualOutputDirectory}/game-config-${name}.png`,
  );
}

async function createAndValidateDraft(
  page: Page,
  name: string,
  content: string,
  acceptanceEvidence: AcceptanceEvidenceAudit,
): Promise<{ draft: Draft; validation: ValidationResult }> {
  await page.getByTestId('create-draft').click();
  await expect(page.getByTestId('draft-editor')).toBeVisible();
  await formField(page, 'draft-name').fill(name);
  await formField(page, 'schema-version').fill(schemaVersion);
  await formField(page, 'draft-content').fill(content);

  const createResponsePromise = waitForApiResponseOrFailure(
    page,
    'POST',
    '/api/game-config/drafts',
  );
  const validationResponsePromise = waitForApiResponseOrFailure(
    page,
    'POST',
    /\/api\/game-config\/drafts\/[^/]+\/validate$/,
  );
  await page.getByTestId('validate-draft').click();

  const draft = await readApiData<Draft>(
    await createResponsePromise,
    acceptanceEvidence,
  );
  const validation = await readApiData<ValidationResult>(
    await validationResponsePromise,
    acceptanceEvidence,
  );
  return { draft, validation };
}

async function closeDrawer(page: Page, testId: string): Promise<void> {
  const currentDrawer = drawer(page, testId);
  await currentDrawer
    .getByRole('button', { exact: true, name: 'Close' })
    .click();
  await expect(page.getByTestId(testId)).toBeHidden();
}

test('operator completes the GameConfig lifecycle against the real API', async ({
  acceptanceEvidence,
  page,
}) => {
  test.setTimeout(300_000);

  const apiBaseURL = requireEnvironment('NOVAGAME_E2E_API_URL');
  const username = requireEnvironment('NOVAGAME_E2E_USERNAME');
  const password = requireEnvironment('NOVAGAME_E2E_PASSWORD');
  const runId = Date.now().toString(36);
  const firstDraftName = `E2E first ${runId}`;
  const secondDraftName = `E2E second ${runId}`;
  const permissionDraftName = `E2E permissions ${runId}`;
  const permissionRoleName = `E2E GameConfig viewer ${runId}`;
  const permissionUserName = `e2e-game-config-viewer-${runId}`;
  const permissionPassword = `E2E-GameConfig-${crypto.randomUUID()}!`;
  const validContent = JSON.stringify({
    collections: {
      items: [{ id: 'starter-item', name: 'Starter item' }],
    },
  });
  const secondContent = JSON.stringify({
    collections: {
      items: [
        { id: 'starter-item', name: 'Starter item' },
        { id: 'second-item', name: 'Second item' },
      ],
    },
  });

  let accessToken = await login(page, username, password, acceptanceEvidence);

  await captureWorkspaceViewport(
    page,
    'mobile-390x844',
    {
      height: 844,
      width: 390,
    },
    acceptanceEvidence,
  );
  await captureWorkspaceViewport(
    page,
    'desktop-1440x900',
    {
      height: 900,
      width: 1440,
    },
    acceptanceEvidence,
  );

  const invalid = await createAndValidateDraft(
    page,
    firstDraftName,
    '{"items":[]}',
    acceptanceEvidence,
  );
  expect(invalid.validation.isValid).toBe(false);
  await expect(page.getByTestId('validation-issues')).toContainText(
    'CollectionsRequired',
  );

  await formField(page, 'draft-content').fill(validContent);
  const validUpdatePromise = waitForApiResponseOrFailure(
    page,
    'PUT',
    `/api/game-config/drafts/${invalid.draft.id}`,
  );
  const validValidationPromise = waitForApiResponseOrFailure(
    page,
    'POST',
    `/api/game-config/drafts/${invalid.draft.id}/validate`,
  );
  await page.getByTestId('validate-draft').click();
  await readApiData<Draft>(await validUpdatePromise, acceptanceEvidence);
  const validValidation = await readApiData<ValidationResult>(
    await validValidationPromise,
    acceptanceEvidence,
  );
  expect(validValidation.isValid).toBe(true);
  await expect(page.getByTestId('review-draft')).toBeVisible();

  const externalName = `${firstDraftName} external`;
  const externalResponse = await acceptanceEvidence.apiRequest(
    page.request,
    'PUT',
    apiUrl(apiBaseURL, `/game-config/drafts/${invalid.draft.id}`),
    {
      data: {
        content: validContent,
        expectedRevision: validValidation.revision,
        name: externalName,
        schemaVersion,
      },
      headers: authorizationHeaders(accessToken),
    },
  );
  const externalDraft = await readApiData<Draft>(
    externalResponse,
    acceptanceEvidence,
    'PUT',
  );

  await formField(page, 'draft-name').fill(`${firstDraftName} stale`);
  const conflictResponsePromise = waitForApiResponseOrFailure(
    page,
    'PUT',
    `/api/game-config/drafts/${invalid.draft.id}`,
    409,
  );
  await page.getByTestId('save-draft').click();
  await acceptanceEvidence.auditResponse(await conflictResponsePromise);
  await expect(page.getByTestId('reload-conflicted-draft')).toBeVisible();
  await page.getByTestId('reload-conflicted-draft').click();
  await expect(formField(page, 'draft-name')).toHaveValue(externalName);

  await formField(page, 'draft-name').fill(firstDraftName);
  const finalUpdatePromise = waitForApiResponseOrFailure(
    page,
    'PUT',
    `/api/game-config/drafts/${externalDraft.id}`,
  );
  const finalValidationPromise = waitForApiResponseOrFailure(
    page,
    'POST',
    `/api/game-config/drafts/${externalDraft.id}/validate`,
  );
  await page.getByTestId('validate-draft').click();
  const finalDraft = await readApiData<Draft>(
    await finalUpdatePromise,
    acceptanceEvidence,
  );
  const finalValidation = await readApiData<ValidationResult>(
    await finalValidationPromise,
    acceptanceEvidence,
  );
  expect(finalValidation.isValid).toBe(true);
  await closeDrawer(page, 'draft-editor');

  const firstDraftRow = page
    .getByTestId('draft-table')
    .getByRole('row')
    .filter({ hasText: firstDraftName });
  await expect(firstDraftRow).toContainText('Validated');
  await firstDraftRow.getByTestId('review-from-table').click();
  await formField(page, 'release-notes').fill('Initial E2E publication.');
  const firstPublishPromise = waitForApiResponseOrFailure(
    page,
    'POST',
    `/api/game-config/drafts/${finalDraft.id}/publish`,
  );
  await page.getByTestId('publish-draft').click();
  const firstVersion = await readApiData<Version>(
    await firstPublishPromise,
    acceptanceEvidence,
  );
  await expect(page.getByTestId('version-detail')).toBeVisible();
  await expect(page.getByTestId('version-id')).toHaveText(firstVersion.id);

  const downloadPromise = page.waitForEvent('download');
  await page.getByTestId('download-snapshot').click();
  const download = await downloadPromise;
  const downloadPath = await download.path();
  if (!downloadPath) {
    throw new Error('Playwright did not expose the downloaded snapshot path.');
  }
  const snapshot = JSON.parse(await readFile(downloadPath, 'utf8')) as Snapshot;
  expect(snapshot).toMatchObject({
    configurationSet,
    configurationVersion: firstVersion.id,
    schemaVersion,
  });
  expect(snapshot.content).toEqual(JSON.parse(validContent));

  await page.getByTestId('activate-from-detail').click();
  await formField(page, 'activation-reason').fill('Initial E2E activation.');
  const firstActivationPromise = waitForApiResponseOrFailure(
    page,
    'POST',
    `/api/game-config/environments/${environmentName}/activate`,
  );
  await page.getByTestId('confirm-activation').click();
  const firstEnvironment = await readApiData<Environment>(
    await firstActivationPromise,
    acceptanceEvidence,
  );
  expect(firstEnvironment.activeVersionId).toBe(firstVersion.id);
  await closeDrawer(page, 'version-detail');

  const second = await createAndValidateDraft(
    page,
    secondDraftName,
    secondContent,
    acceptanceEvidence,
  );
  expect(second.validation.isValid).toBe(true);
  await closeDrawer(page, 'draft-editor');
  const secondDraftRow = page
    .getByTestId('draft-table')
    .getByRole('row')
    .filter({ hasText: secondDraftName });
  await expect(secondDraftRow).toContainText('Validated');
  await secondDraftRow.getByTestId('review-from-table').click();
  await formField(page, 'release-notes').fill('Second E2E publication.');
  const secondPublishPromise = waitForApiResponseOrFailure(
    page,
    'POST',
    `/api/game-config/drafts/${second.draft.id}/publish`,
  );
  await page.getByTestId('publish-draft').click();
  const secondVersion = await readApiData<Version>(
    await secondPublishPromise,
    acceptanceEvidence,
  );
  await expect(page.getByTestId('version-detail')).toBeVisible();
  await expect(page.getByTestId('version-id')).toHaveText(secondVersion.id);
  await page.getByTestId('activate-from-detail').click();
  await formField(page, 'activation-reason').fill('Second E2E activation.');
  const secondActivationPromise = waitForApiResponseOrFailure(
    page,
    'POST',
    `/api/game-config/environments/${environmentName}/activate`,
  );
  await page.getByTestId('confirm-activation').click();
  const secondEnvironment = await readApiData<Environment>(
    await secondActivationPromise,
    acceptanceEvidence,
  );
  expect(secondEnvironment).toMatchObject({
    activeVersionId: secondVersion.id,
    previousVersionId: firstVersion.id,
  });
  await closeDrawer(page, 'version-detail');

  await page.getByRole('tab', { name: 'Environment' }).click();
  await page.getByTestId('open-rollback').click();
  await formField(page, 'rollback-reason').fill('E2E rollback verification.');
  const rollbackPromise = waitForApiResponseOrFailure(
    page,
    'POST',
    `/api/game-config/environments/${environmentName}/rollback`,
  );
  await page.getByTestId('confirm-rollback').click();
  const rolledBackEnvironment = await readApiData<Environment>(
    await rollbackPromise,
    acceptanceEvidence,
  );
  expect(rolledBackEnvironment).toMatchObject({
    activeVersionId: firstVersion.id,
    previousVersionId: secondVersion.id,
  });

  const permissionDraftResponse = await acceptanceEvidence.apiRequest(
    page.request,
    'POST',
    apiUrl(apiBaseURL, '/game-config/drafts'),
    {
      data: {
        configurationSet,
        content: validContent,
        name: permissionDraftName,
        schemaVersion,
      },
      headers: authorizationHeaders(accessToken),
    },
  );
  const permissionDraft = await readApiData<Draft>(
    permissionDraftResponse,
    acceptanceEvidence,
    'POST',
  );
  await readApiData<ValidationResult>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, `/game-config/drafts/${permissionDraft.id}/validate`),
      {
        data: { expectedRevision: permissionDraft.revision },
        headers: authorizationHeaders(accessToken),
      },
    ),
    acceptanceEvidence,
    'POST',
  );

  acceptanceEvidence.registerSensitiveValue(permissionPassword);
  const permissionRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/roles'),
      {
        data: {
          name: permissionRoleName,
          permissionNames: [
            gameConfigAccessPermission,
            operationsAdminAccessPermission,
          ],
        },
        headers: {
          ...authorizationHeaders(accessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );
  expect(permissionRole.permissionNames).toEqual([
    gameConfigAccessPermission,
    operationsAdminAccessPermission,
  ]);

  await readApiData<{ id: string }>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/administrators'),
      {
        data: {
          displayName: 'E2E GameConfig viewer',
          initialPassword: permissionPassword,
          roleIds: [permissionRole.id],
          userName: permissionUserName,
        },
        headers: {
          ...authorizationHeaders(accessToken),
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    ),
    acceptanceEvidence,
    'POST',
  );

  await logout(page);
  accessToken = await login(
    page,
    permissionUserName,
    permissionPassword,
    acceptanceEvidence,
  );

  await expect(page).toHaveURL(/\/game-config(?:[/?#].*)?$/);
  await expect(
    page.getByRole('heading', { name: 'Game configuration' }),
  ).toBeVisible();
  await expect(
    page.locator('a[role="menuitem"][href="/game-config"]'),
  ).toBeVisible();
  await expect(page.getByTestId('create-draft')).toHaveCount(0);
  const permissionDraftRow = page
    .getByTestId('draft-table')
    .getByRole('row')
    .filter({ hasText: permissionDraftName });
  await expect(permissionDraftRow).toContainText('Validated');
  await permissionDraftRow
    .getByRole('button', { name: `Open ${permissionDraftName}` })
    .click();
  await expect(page.getByTestId('save-draft')).toHaveCount(0);
  await expect(page.getByTestId('validate-draft')).toHaveCount(0);
  await closeDrawer(page, 'draft-editor');

  await permissionDraftRow.getByTestId('review-from-table').click();
  await expect(page.getByTestId('publish-draft')).toHaveCount(0);
  await closeDrawer(page, 'review-drawer');

  await page.getByRole('tab', { name: 'Published versions' }).click();
  await expect(page.getByTestId('activate-version')).toHaveCount(0);
  await page.getByRole('tab', { name: 'Environment' }).click();
  await expect(page.getByTestId('open-rollback')).toHaveCount(0);

  const forbiddenCreate = await acceptanceEvidence.apiRequest(
    page.request,
    'POST',
    apiUrl(apiBaseURL, '/game-config/drafts'),
    {
      data: {
        configurationSet,
        content: validContent,
        name: `${permissionDraftName} forbidden`,
        schemaVersion,
      },
      headers: authorizationHeaders(accessToken),
    },
  );
  await acceptanceEvidence.auditResponse(forbiddenCreate, 'POST');
  expect(forbiddenCreate.status()).toBe(403);

  const forbiddenPublish = await acceptanceEvidence.apiRequest(
    page.request,
    'POST',
    apiUrl(apiBaseURL, `/game-config/drafts/${permissionDraft.id}/publish`),
    {
      data: {
        expectedRevision: permissionDraft.revision,
        releaseNotes: 'This must be rejected.',
      },
      headers: authorizationHeaders(accessToken),
    },
  );
  await acceptanceEvidence.auditResponse(forbiddenPublish, 'POST');
  expect(forbiddenPublish.status()).toBe(403);

  const forbiddenActivation = await acceptanceEvidence.apiRequest(
    page.request,
    'POST',
    apiUrl(apiBaseURL, `/game-config/environments/${environmentName}/activate`),
    {
      data: {
        configurationSet,
        expectedRevision: rolledBackEnvironment.revision,
        versionId: secondVersion.id,
      },
      headers: authorizationHeaders(accessToken),
    },
  );
  await acceptanceEvidence.auditResponse(forbiddenActivation, 'POST');
  expect(forbiddenActivation.status()).toBe(403);

  await logout(page);
  const loggedOutCookies = await page.context().cookies();
  expect(
    loggedOutCookies.some(
      (cookie) => cookie.name === 'novagame.admin.refresh_token',
    ),
  ).toBe(false);
});
