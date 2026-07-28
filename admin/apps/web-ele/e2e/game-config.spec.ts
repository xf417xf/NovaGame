import type { APIResponse, Locator, Page, Response } from '@playwright/test';

import { Buffer } from 'node:buffer';
import { spawnSync } from 'node:child_process';
import { mkdir, readFile } from 'node:fs/promises';

import { expect, test } from '@playwright/test';

interface ApiEnvelope<T> {
  data?: T;
  error?: null | { code: string; message: string };
  succeeded: boolean;
}

interface AccessTokenResponse {
  accessToken: string;
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

interface DockerInspectResult {
  Config?: {
    Labels?: null | Record<string, string>;
  };
  Name?: string;
  State?: {
    Running?: boolean;
  };
}

interface MySqlFixture {
  container: string;
  database: string;
  rootPassword: string;
}

interface PermissionFixture extends MySqlFixture {
  roleId: string;
}

const configurationSet = 'default';
const environmentName = 'development';
const schemaVersion = '1';
const permissionNames = [
  'Content.GameConfig.ManageDrafts',
  'Content.GameConfig.Publish',
  'Content.GameConfig.Activate',
] as const;
const visualOutputDirectory = 'output/playwright/visual';

test.use({ trace: 'off' });

function requireEnvironment(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Required E2E environment variable '${name}' is missing.`);
  }
  return value;
}

function normalizeError(error: unknown, fallbackMessage: string): Error {
  return error instanceof Error
    ? error
    : new Error(fallbackMessage, { cause: error });
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

async function readApiData<T>(response: APIResponse | Response): Promise<T> {
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
    const errorMessage =
      body.error?.message ?? 'No stable API error message was returned.';
    throw new Error(
      `NovaGame API request failed with status ${response.status()} ` +
        `and code '${errorCode}': ${errorMessage}`,
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

async function login(
  page: Page,
  username: string,
  password: string,
): Promise<string> {
  await page.goto('/auth/login');
  await formField(page, 'login-username').fill(username);
  await formField(page, 'login-password').fill(password);

  const loginResponsePromise = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname === '/api/auth/login',
  );
  await page.getByRole('button', { name: 'login' }).click();
  const session = await readApiData<AccessTokenResponse>(
    await loginResponsePromise,
  );

  await expect(page).toHaveURL(/game-config/, { timeout: 60_000 });
  await expect(
    page.getByRole('heading', { name: 'Game configuration' }),
  ).toBeVisible({ timeout: 60_000 });
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

  await mkdir(visualOutputDirectory, { recursive: true });
  await page.screenshot({
    animations: 'disabled',
    fullPage: true,
    path: `${visualOutputDirectory}/game-config-${name}.png`,
  });
}

async function createAndValidateDraft(
  page: Page,
  name: string,
  content: string,
): Promise<{ draft: Draft; validation: ValidationResult }> {
  await page.getByTestId('create-draft').click();
  await expect(page.getByTestId('draft-editor')).toBeVisible();
  await formField(page, 'draft-name').fill(name);
  await formField(page, 'schema-version').fill(schemaVersion);
  await formField(page, 'draft-content').fill(content);

  const createResponsePromise = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname === '/api/game-config/drafts',
  );
  const validationResponsePromise = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      /\/api\/game-config\/drafts\/[^/]+\/validate$/.test(
        new URL(response.url()).pathname,
      ),
  );
  await page.getByTestId('validate-draft').click();

  const draft = await readApiData<Draft>(await createResponsePromise);
  const validation = await readApiData<ValidationResult>(
    await validationResponsePromise,
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

function runDocker(args: string[], environment?: NodeJS.ProcessEnv): string {
  const result = spawnSync('docker', args, {
    encoding: 'utf8',
    env: environment,
    shell: false,
    windowsHide: true,
  });

  if (result.error || result.status !== 0) {
    throw new Error(
      `Docker E2E fixture command failed with exit code ${result.status ?? 'unknown'}.`,
    );
  }
  return result.stdout.trim();
}

function sqlUtf8Literal(value: string): string {
  return `CONVERT(0x${Buffer.from(value, 'utf8').toString('hex')} USING utf8mb4)`;
}

function executeMySql(fixture: MySqlFixture, sql: string): string {
  return runDocker(
    [
      'exec',
      '--env',
      'MYSQL_PWD',
      fixture.container,
      'mysql',
      '--protocol=TCP',
      '--user=root',
      `--database=${fixture.database}`,
      '--batch',
      '--skip-column-names',
      '--execute',
      sql,
    ],
    { ...process.env, MYSQL_PWD: fixture.rootPassword },
  );
}

function readOutputLines(output: string): string[] {
  return output
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}

function createDisposablePermissionFixture(
  username: string,
): PermissionFixture {
  const container = requireEnvironment('NOVAGAME_E2E_MYSQL_CONTAINER');
  const rootPassword = requireEnvironment('NOVAGAME_E2E_MYSQL_ROOT_PASSWORD');
  const database = requireEnvironment('NOVAGAME_E2E_MYSQL_DATABASE');
  if (database !== 'novagame_e2e') {
    throw new Error(
      `Refusing permission mutation for non-disposable database '${database}'.`,
    );
  }

  const inspectOutput = runDocker(['inspect', container]);
  let inspectResults: DockerInspectResult[];
  try {
    inspectResults = JSON.parse(inspectOutput) as DockerInspectResult[];
  } catch {
    throw new Error('Docker did not return valid inspection data.');
  }
  if (inspectResults.length !== 1) {
    throw new Error('Docker inspection did not resolve exactly one container.');
  }

  const inspected = inspectResults[0];
  const labels = inspected?.Config?.Labels ?? {};
  const composeProject = labels['com.docker.compose.project'] ?? '';
  const composeService = labels['com.docker.compose.service'] ?? '';
  const containerName = inspected?.Name?.replace(/^\/+/, '') ?? '';
  if (
    !/^novagame-test-[a-f0-9]{12}$/.test(composeProject) ||
    composeService !== 'mysql' ||
    !containerName.startsWith(`${composeProject}-mysql-`) ||
    inspected?.State?.Running !== true
  ) {
    throw new Error(
      'Refusing permission mutation outside a running disposable novagame-test-* MySQL container.',
    );
  }

  const mysqlFixture: MySqlFixture = { container, database, rootPassword };
  if (executeMySql(mysqlFixture, 'SELECT DATABASE();') !== database) {
    throw new Error(
      'The MySQL client did not select the disposable E2E database.',
    );
  }

  const normalizedUsername = username.trim().normalize('NFKC').toUpperCase();
  const roleIds = readOutputLines(
    executeMySql(
      mysqlFixture,
      `SELECT DISTINCT rm.RoleId
FROM admin_role_memberships AS rm
INNER JOIN admin_administrators AS a ON a.Id = rm.AdministratorId
WHERE a.NormalizedUserName = ${sqlUtf8Literal(normalizedUsername)}
ORDER BY rm.RoleId;`,
    ),
  );
  const roleId = roleIds[0];
  if (roleIds.length !== 1 || !roleId) {
    throw new Error(
      `Disposable permission fixture expected one role for the configured administrator, found ${roleIds.length}.`,
    );
  }

  const fixture: PermissionFixture = {
    ...mysqlFixture,
    roleId,
  };
  const permissionList = permissionNames
    .map((permissionName) => sqlUtf8Literal(permissionName))
    .join(', ');
  const permissionCount = Number(
    executeMySql(
      fixture,
      `SELECT COUNT(*)
FROM admin_role_permissions
WHERE RoleId = ${sqlUtf8Literal(fixture.roleId)}
  AND Name IN (${permissionList});`,
    ),
  );
  if (permissionCount !== permissionNames.length) {
    throw new Error(
      `Disposable permission fixture expected ${permissionNames.length} mutable permissions, found ${permissionCount}.`,
    );
  }

  return fixture;
}

function revokeMutationPermissions(fixture: PermissionFixture): void {
  const permissionList = permissionNames
    .map((permissionName) => sqlUtf8Literal(permissionName))
    .join(', ');
  const results = readOutputLines(
    executeMySql(
      fixture,
      `DELETE FROM admin_role_permissions
WHERE RoleId = ${sqlUtf8Literal(fixture.roleId)}
  AND Name IN (${permissionList});
SELECT ROW_COUNT();
SELECT COUNT(*)
FROM admin_role_permissions
WHERE RoleId = ${sqlUtf8Literal(fixture.roleId)}
  AND Name IN (${permissionList});`,
    ),
  ).map(Number);

  if (
    results.length !== 2 ||
    results[0] !== permissionNames.length ||
    results[1] !== 0
  ) {
    throw new Error(
      'Disposable permission revocation did not remove exactly three permissions.',
    );
  }
}

function restoreMutationPermissions(fixture: PermissionFixture): void {
  const permissionRows = permissionNames
    .map((permissionName, index) =>
      index === 0
        ? `SELECT ${sqlUtf8Literal(permissionName)} AS Name`
        : `UNION ALL SELECT ${sqlUtf8Literal(permissionName)}`,
    )
    .join('\n');
  const permissionList = permissionNames
    .map((permissionName) => sqlUtf8Literal(permissionName))
    .join(', ');
  const restoredCount = Number(
    executeMySql(
      fixture,
      `INSERT INTO admin_role_permissions
    (Id, RoleId, Name, GrantedAtUtc, GrantedByAdministratorId)
SELECT UUID(), ${sqlUtf8Literal(fixture.roleId)}, permissions.Name, UTC_TIMESTAMP(6), NULL
FROM (
${permissionRows}
) AS permissions
LEFT JOIN admin_role_permissions AS existing
  ON existing.RoleId = ${sqlUtf8Literal(fixture.roleId)}
 AND existing.Name = permissions.Name
WHERE existing.Id IS NULL;
SELECT COUNT(*)
FROM admin_role_permissions
WHERE RoleId = ${sqlUtf8Literal(fixture.roleId)}
  AND Name IN (${permissionList});`,
    ),
  );

  if (restoredCount !== permissionNames.length) {
    throw new Error(
      'Disposable permission restoration did not restore all three permissions.',
    );
  }
}

test('operator completes the GameConfig lifecycle against the real API', async ({
  page,
}) => {
  test.setTimeout(300_000);

  const baseURL = requireEnvironment('NOVAGAME_E2E_BASE_URL');
  const username = requireEnvironment('NOVAGAME_E2E_USERNAME');
  const password = requireEnvironment('NOVAGAME_E2E_PASSWORD');
  const runId = Date.now().toString(36);
  const firstDraftName = `E2E first ${runId}`;
  const secondDraftName = `E2E second ${runId}`;
  const permissionDraftName = `E2E permissions ${runId}`;
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

  let accessToken = await login(page, username, password);
  const authenticatedCookies = await page.context().cookies();
  const refreshCookie = authenticatedCookies.find(
    (cookie) => cookie.name === 'novagame.admin.refresh_token',
  );
  expect(refreshCookie).toMatchObject({
    httpOnly: true,
    path: '/api/auth',
    sameSite: 'Strict',
    secure: true,
  });

  await captureWorkspaceViewport(page, 'mobile-390x844', {
    height: 844,
    width: 390,
  });
  await captureWorkspaceViewport(page, 'desktop-1440x900', {
    height: 900,
    width: 1440,
  });

  const invalid = await createAndValidateDraft(
    page,
    firstDraftName,
    '{"items":[]}',
  );
  expect(invalid.validation.isValid).toBe(false);
  await expect(page.getByTestId('validation-issues')).toContainText(
    'CollectionsRequired',
  );

  await formField(page, 'draft-content').fill(validContent);
  const validUpdatePromise = page.waitForResponse(
    (response) =>
      response.request().method() === 'PUT' &&
      new URL(response.url()).pathname ===
        `/api/game-config/drafts/${invalid.draft.id}`,
  );
  const validValidationPromise = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname ===
        `/api/game-config/drafts/${invalid.draft.id}/validate`,
  );
  await page.getByTestId('validate-draft').click();
  await readApiData<Draft>(await validUpdatePromise);
  const validValidation = await readApiData<ValidationResult>(
    await validValidationPromise,
  );
  expect(validValidation.isValid).toBe(true);
  await expect(page.getByTestId('review-draft')).toBeVisible();

  const externalName = `${firstDraftName} external`;
  const externalResponse = await page.request.put(
    apiUrl(baseURL, `/game-config/drafts/${invalid.draft.id}`),
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
  const externalDraft = await readApiData<Draft>(externalResponse);

  await formField(page, 'draft-name').fill(`${firstDraftName} stale`);
  const conflictResponsePromise = page.waitForResponse(
    (response) =>
      response.request().method() === 'PUT' &&
      response.status() === 409 &&
      new URL(response.url()).pathname ===
        `/api/game-config/drafts/${invalid.draft.id}`,
  );
  await page.getByTestId('save-draft').click();
  await conflictResponsePromise;
  await expect(page.getByTestId('reload-conflicted-draft')).toBeVisible();
  await page.getByTestId('reload-conflicted-draft').click();
  await expect(formField(page, 'draft-name')).toHaveValue(externalName);

  await formField(page, 'draft-name').fill(firstDraftName);
  const finalUpdatePromise = page.waitForResponse(
    (response) =>
      response.request().method() === 'PUT' &&
      new URL(response.url()).pathname ===
        `/api/game-config/drafts/${externalDraft.id}`,
  );
  const finalValidationPromise = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname ===
        `/api/game-config/drafts/${externalDraft.id}/validate`,
  );
  await page.getByTestId('validate-draft').click();
  const finalDraft = await readApiData<Draft>(await finalUpdatePromise);
  const finalValidation = await readApiData<ValidationResult>(
    await finalValidationPromise,
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
  const firstPublishPromise = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname ===
        `/api/game-config/drafts/${finalDraft.id}/publish`,
  );
  await page.getByTestId('publish-draft').click();
  const firstVersion = await readApiData<Version>(await firstPublishPromise);
  await expect(page.getByTestId('version-detail')).toBeVisible();

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
  const firstActivationPromise = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname ===
        `/api/game-config/environments/${environmentName}/activate`,
  );
  await page.getByTestId('confirm-activation').click();
  const firstEnvironment = await readApiData<Environment>(
    await firstActivationPromise,
  );
  expect(firstEnvironment.activeVersionId).toBe(firstVersion.id);
  await closeDrawer(page, 'version-detail');

  const second = await createAndValidateDraft(
    page,
    secondDraftName,
    secondContent,
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
  const secondPublishPromise = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname ===
        `/api/game-config/drafts/${second.draft.id}/publish`,
  );
  await page.getByTestId('publish-draft').click();
  const secondVersion = await readApiData<Version>(await secondPublishPromise);
  await expect(page.getByTestId('version-detail')).toBeVisible();
  await page.getByTestId('activate-from-detail').click();
  await formField(page, 'activation-reason').fill('Second E2E activation.');
  const secondActivationPromise = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname ===
        `/api/game-config/environments/${environmentName}/activate`,
  );
  await page.getByTestId('confirm-activation').click();
  const secondEnvironment = await readApiData<Environment>(
    await secondActivationPromise,
  );
  expect(secondEnvironment).toMatchObject({
    activeVersionId: secondVersion.id,
    previousVersionId: firstVersion.id,
  });
  await closeDrawer(page, 'version-detail');

  await page.getByRole('tab', { name: 'Environment' }).click();
  await page.getByTestId('open-rollback').click();
  await formField(page, 'rollback-reason').fill('E2E rollback verification.');
  const rollbackPromise = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname ===
        `/api/game-config/environments/${environmentName}/rollback`,
  );
  await page.getByTestId('confirm-rollback').click();
  const rolledBackEnvironment = await readApiData<Environment>(
    await rollbackPromise,
  );
  expect(rolledBackEnvironment).toMatchObject({
    activeVersionId: firstVersion.id,
    previousVersionId: secondVersion.id,
  });

  const permissionDraftResponse = await page.request.post(
    apiUrl(baseURL, '/game-config/drafts'),
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
  const permissionDraft = await readApiData<Draft>(permissionDraftResponse);
  await readApiData<ValidationResult>(
    await page.request.post(
      apiUrl(baseURL, `/game-config/drafts/${permissionDraft.id}/validate`),
      {
        data: { expectedRevision: permissionDraft.revision },
        headers: authorizationHeaders(accessToken),
      },
    ),
  );

  const permissionFixture = createDisposablePermissionFixture(username);
  let permissionsMayNeedRestoration = false;
  let permissionPhaseError: Error | undefined;
  try {
    permissionsMayNeedRestoration = true;
    revokeMutationPermissions(permissionFixture);
    await logout(page);
    accessToken = await login(page, username, password);

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

    const forbiddenCreate = await page.request.post(
      apiUrl(baseURL, '/game-config/drafts'),
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
    expect(forbiddenCreate.status()).toBe(403);

    const forbiddenPublish = await page.request.post(
      apiUrl(baseURL, `/game-config/drafts/${permissionDraft.id}/publish`),
      {
        data: {
          expectedRevision: permissionDraft.revision,
          releaseNotes: 'This must be rejected.',
        },
        headers: authorizationHeaders(accessToken),
      },
    );
    expect(forbiddenPublish.status()).toBe(403);

    const forbiddenActivation = await page.request.post(
      apiUrl(baseURL, `/game-config/environments/${environmentName}/activate`),
      {
        data: {
          configurationSet,
          expectedRevision: rolledBackEnvironment.revision,
          versionId: secondVersion.id,
        },
        headers: authorizationHeaders(accessToken),
      },
    );
    expect(forbiddenActivation.status()).toBe(403);

    await logout(page);
    const loggedOutCookies = await page.context().cookies();
    expect(
      loggedOutCookies.some(
        (cookie) => cookie.name === 'novagame.admin.refresh_token',
      ),
    ).toBe(false);
  } catch (error) {
    permissionPhaseError = normalizeError(
      error,
      'The permission verification phase failed with a non-Error value.',
    );
  }

  let restorationError: Error | undefined;
  if (permissionsMayNeedRestoration) {
    try {
      restoreMutationPermissions(permissionFixture);
    } catch (error) {
      restorationError = normalizeError(
        error,
        'Permission restoration failed with a non-Error value.',
      );
    }
  }
  if (permissionPhaseError && restorationError) {
    throw new AggregateError(
      [permissionPhaseError, restorationError],
      'The permission phase and its restoration both failed.',
      { cause: restorationError },
    );
  }
  if (permissionPhaseError) {
    throw new Error(permissionPhaseError.message, {
      cause: permissionPhaseError,
    });
  }
  if (restorationError) {
    throw new Error(restorationError.message, { cause: restorationError });
  }
});
