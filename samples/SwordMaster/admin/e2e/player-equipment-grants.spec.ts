import type {
  APIRequestContext,
  APIResponse,
  Page,
  Request,
  Response,
  Route,
} from '@playwright/test';

import { Buffer } from 'node:buffer';
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';

import { expect, test } from '@playwright/test';

interface ApiEnvelope<T> {
  data?: T;
  error?: null | {
    code: string;
    details?: Record<string, string[]> | null;
    message: string;
  };
  succeeded: boolean;
}

interface ExpectedPageResponse {
  label: string;
  method: string;
  path: string;
}

interface EquipmentGrant {
  configurationVersionId: string;
  configuredItemId: string;
  grantId: string;
  grantReason: string;
  grantResultingRevision: number;
  grantedAtUtc: string;
  grantedByAdministratorId: string;
  ownedItemId: string;
  playerId: string;
  rollbackReason: null | string;
  rollbackResultingRevision: null | number;
  rolledBackAtUtc: null | string;
  rolledBackByAdministratorId: null | string;
  status: 1 | 2;
}

interface EquipmentGrantMutation {
  configurationVersionId: string;
  configuredItemId: string;
  grantId: string;
  grantReason: string;
  grantedAtUtc: string;
  grantedByAdministratorId: string;
  ownedItemId: string;
  playerId: string;
  resultingRevision: number;
  rollbackReason: null | string;
  rolledBackAtUtc: null | string;
  rolledBackByAdministratorId: null | string;
  status: 1 | 2;
}

interface EquipmentGrantPage {
  items: EquipmentGrant[];
  page: number;
  pageSize: number;
  playerId: string;
  revision: number;
  totalCount: number;
}

interface PlayerAssetsMutation {
  revision: number;
}

interface ConnectionDetails {
  database: string;
  password: string;
  port: number;
  server: string;
  user: string;
}

interface DockerInspectResult {
  Config?: {
    Labels?: null | Record<string, string>;
  };
  NetworkSettings?: {
    Ports?: Record<
      string,
      null | Array<{
        HostIp?: string;
        HostPort?: string;
      }>
    >;
  };
  State?: {
    Running?: boolean;
  };
}

interface PermissionRow {
  grantedAtUtc: string;
  grantedByAdministratorIdHex: null | string;
  idHex: string;
  nameHex: string;
  roleIdHex: string;
}

interface LostGrantRequest {
  body: string;
  ifMatch: string;
  idempotencyKey: string;
  mutation: EquipmentGrantMutation;
}

const disposableDatabaseMarker = 'swordmaster-test-disposable-v1';
const equipmentGrantPermission =
  'SwordMaster.PlayerAssets.EquipmentGrants.Manage';
const administratorsRole = 'ADMINISTRATORS';
const guidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

const environment = Object.freeze({
  apiUrl: requireLoopbackUrl('SWORDMASTER_ACCEPTANCE_API_URL'),
  baseUrl: requireLoopbackUrl('SWORDMASTER_ACCEPTANCE_BASE_URL'),
  characterId: requireCanonicalGuid('SWORDMASTER_ACCEPTANCE_CHARACTER_ID'),
  configuredItemId: requireNonEmptyEnvironment(
    'SWORDMASTER_ACCEPTANCE_CONFIGURED_ITEM_ID',
  ),
  disposableDatabaseMarker: requireNonEmptyEnvironment(
    'SWORDMASTER_ACCEPTANCE_DISPOSABLE_DATABASE_MARKER',
  ),
  novaGameConnectionString: requireNonEmptyEnvironment(
    'SWORDMASTER_ACCEPTANCE_NOVAGAME_CONNECTION_STRING',
  ),
  password: requireNonEmptyEnvironment('SWORDMASTER_ACCEPTANCE_PASSWORD'),
  playerId: requireCanonicalGuid('SWORDMASTER_ACCEPTANCE_PLAYER_ID'),
  playerToken: requireNonEmptyEnvironment(
    'SWORDMASTER_ACCEPTANCE_PLAYER_TOKEN',
  ),
  swordMasterConnectionString: requireNonEmptyEnvironment(
    'SWORDMASTER_ACCEPTANCE_SWORDMASTER_CONNECTION_STRING',
  ),
  username: requireNonEmptyEnvironment('SWORDMASTER_ACCEPTANCE_USERNAME'),
});

const grantCollectionPath =
  `/api/swordmaster/operations/player-assets/${environment.playerId}` +
  '/equipment-grants';
const playerEquipmentPath = `/api/player-assets/characters/${environment.characterId}/equipment/weapon`;

test.use({ trace: 'off' });

test('desktop operator completes the real grant, retry, conflict, rollback, permission, and logout workflow', async ({
  page,
  request,
}) => {
  test.setTimeout(300_000);
  await page.setViewportSize({ height: 900, width: 1440 });
  resetPlayerFixture();

  await login(page);
  await lookupPlayer(page, 1);

  const responseLoss = await installOneShotGrantResponseLoss(page);
  try {
    await openGrantDialog(page);
    await fillGrantForm(page, 'M306 desktop response-loss grant');
    await page.getByTestId('confirm-grant').click();

    await expect(page.getByTestId('retry-operation')).toBeVisible({
      timeout: 60_000,
    });
    await expect(page.getByTestId('confirm-grant')).toBeHidden();
    const lostRequest = await responseLoss.waitForCommit();
    await responseLoss.dispose();
    expect(lostRequest.ifMatch).toBe('"1"');
    expect(lostRequest.idempotencyKey).toMatch(guidPattern);

    const retryRequest = page.waitForRequest((candidate) =>
      isGrantRequest(candidate, lostRequest.idempotencyKey),
    );
    const retryResponse = waitForPageResponse(
      page,
      'POST',
      grantCollectionPath,
      200,
    );
    await page.getByTestId('retry-operation').click();
    const [replayedRequest, replayedResponse] = await Promise.all([
      retryRequest,
      retryResponse,
    ]);
    const replayedMutation = await readSuccess<EquipmentGrantMutation>(
      replayedResponse,
      'exact grant retry',
    );
    expect(replayedRequest.postData() ?? '').toBe(lostRequest.body);
    expect(replayedRequest.headers()['if-match']).toBe(lostRequest.ifMatch);
    expect(replayedRequest.headers()['idempotency-key']).toBe(
      lostRequest.idempotencyKey,
    );
    expect(replayedMutation.grantId).toBe(lostRequest.mutation.grantId);
    expect(replayedMutation.ownedItemId).toBe(lostRequest.mutation.ownedItemId);

    await expect(page.getByTestId('current-revision')).toHaveText('2');
    await expect(page.getByTestId('equipment-grants-history')).toContainText(
      'M306 desktop response-loss grant',
    );
    await expect(page.getByTestId('equipment-grants-history')).toContainText(
      environment.configuredItemId,
    );

    await equipIssuedItem(request, replayedMutation.ownedItemId, 2);

    await openGrantDialog(page);
    await fillGrantForm(page, 'M306 desktop stale revision conflict');
    const conflictResponse = waitForPageResponse(
      page,
      'POST',
      grantCollectionPath,
      409,
    );
    await page.getByTestId('confirm-grant').click();
    await expectApiError(
      await conflictResponse,
      'SwordMaster.PlayerAssets.EquipmentGrants.ConcurrencyConflict',
    );
    await expect(page.getByTestId('equipment-grants-error')).toContainText(
      'SwordMaster.PlayerAssets.EquipmentGrants.ConcurrencyConflict',
    );
    await expect(page.getByTestId('current-revision')).toHaveText('3');
    await closeVisibleDialog(page, 'confirm-grant');

    await openRollbackDialog(page);
    await fillRollbackForm(page, 'M306 desktop equipped rollback block');
    const blockedResponse = waitForPageResponse(
      page,
      'POST',
      `${grantCollectionPath}/${replayedMutation.grantId}/rollback`,
      409,
    );
    await page.getByTestId('confirm-rollback').click();
    await expectApiError(
      await blockedResponse,
      'SwordMaster.PlayerAssets.EquipmentGrants.RollbackBlockedItemEquipped',
    );
    await expect(page.getByTestId('equipment-grants-error')).toContainText(
      'SwordMaster.PlayerAssets.EquipmentGrants.RollbackBlockedItemEquipped',
    );
    await closeVisibleDialog(page, 'confirm-rollback');

    await unequipIssuedItem(request, 3);
    await refreshHistory(page, 4);

    await openRollbackDialog(page);
    await fillRollbackForm(page, 'M306 desktop completed rollback');
    const rollbackResponse = waitForPageResponse(
      page,
      'POST',
      `${grantCollectionPath}/${replayedMutation.grantId}/rollback`,
      200,
    );
    await page.getByTestId('confirm-rollback').click();
    const rollbackMutation = await readSuccess<EquipmentGrantMutation>(
      await rollbackResponse,
      'desktop rollback',
    );
    expect(rollbackMutation.status).toBe(2);
    await expect(page.getByTestId('current-revision')).toHaveText('5');
    await expect(page.getByTestId('equipment-grants-history')).toContainText(
      'Rolled back',
    );
    await expect(page.getByTestId('equipment-grants-history')).toContainText(
      'M306 desktop completed rollback',
    );

    const permission = revokeEquipmentGrantPermission();
    try {
      const deniedResponse = waitForPageResponse(
        page,
        'GET',
        grantCollectionPath,
        403,
      );
      await page.getByTestId('refresh-history').click();
      await expectApiError(
        await deniedResponse,
        'SwordMaster.PlayerAssets.EquipmentGrants.Forbidden',
      );
      await expect(page.getByTestId('equipment-grants-error')).toContainText(
        'SwordMaster.PlayerAssets.EquipmentGrants.Forbidden',
      );
      await expect(page.getByTestId('retry-operation')).toBeVisible();
    } finally {
      restoreEquipmentGrantPermission(permission);
    }

    const restoredRefresh = waitForPageResponse(
      page,
      'GET',
      grantCollectionPath,
      200,
    );
    await page.getByTestId('retry-operation').click();
    await readSuccess<EquipmentGrantPage>(
      await restoredRefresh,
      'permission-restored history refresh',
    );
    await expect(page.getByTestId('current-revision')).toHaveText('5');

    await logout(page);
  } finally {
    await responseLoss.dispose();
  }
});

test('mobile operator completes the real grant and rollback workflow without viewport overflow', async ({
  page,
}) => {
  await page.setViewportSize({ height: 844, width: 390 });
  resetPlayerFixture();

  await login(page);
  await lookupPlayer(page, 1);
  await assertMobileLayout(page);

  await openGrantDialog(page);
  await fillGrantForm(page, 'M306 mobile grant');
  const grantResponse = waitForPageResponse(
    page,
    'POST',
    grantCollectionPath,
    200,
  );
  await page.getByTestId('confirm-grant').click();
  const grantMutation = await readSuccess<EquipmentGrantMutation>(
    await grantResponse,
    'mobile grant',
  );
  expect(grantMutation.status).toBe(1);
  await expect(page.getByTestId('current-revision')).toHaveText('2');
  await expect(page.getByTestId('equipment-grants-history')).toContainText(
    'M306 mobile grant',
  );
  await assertMobileLayout(page);

  await openRollbackDialog(page);
  await fillRollbackForm(page, 'M306 mobile rollback');
  const rollbackResponse = waitForPageResponse(
    page,
    'POST',
    `${grantCollectionPath}/${grantMutation.grantId}/rollback`,
    200,
  );
  await page.getByTestId('confirm-rollback').click();
  const rollbackMutation = await readSuccess<EquipmentGrantMutation>(
    await rollbackResponse,
    'mobile rollback',
  );
  expect(rollbackMutation.status).toBe(2);
  await expect(page.getByTestId('current-revision')).toHaveText('3');
  await expect(page.getByTestId('equipment-grants-history')).toContainText(
    'Rolled back',
  );
  await assertMobileLayout(page);

  await logout(page);
});

async function login(page: Page): Promise<void> {
  await page.goto('/auth/login', { timeout: 120_000, waitUntil: 'commit' });
  await expect(field(page, 'login-username')).toBeVisible({ timeout: 120_000 });
  await expect(field(page, 'login-password')).toBeVisible({ timeout: 120_000 });
  await field(page, 'login-username').fill(environment.username);
  await field(page, 'login-password').fill(environment.password);

  const [loginResponse, userInfoResponse, accessCodesResponse] =
    await observePageResponses(
      page,
      [
        {
          label: 'administrator login',
          method: 'POST',
          path: '/api/auth/login',
        },
        {
          label: 'administrator user bootstrap',
          method: 'GET',
          path: '/api/user/info',
        },
        {
          label: 'administrator permission bootstrap',
          method: 'GET',
          path: '/api/auth/codes',
        },
      ],
      async () => {
        await page.getByTestId('login-submit').click({ timeout: 60_000 });
      },
    );
  const loginSession = await readSuccess<{ accessToken: string }>(
    loginResponse,
    'administrator login',
  );
  await readSuccess<Record<string, unknown>>(
    userInfoResponse,
    'administrator user bootstrap',
  );
  const accessCodes = await readSuccess<string[]>(
    accessCodesResponse,
    'administrator permission bootstrap',
  );
  expect(loginSession.accessToken.length).toBeGreaterThan(20);
  expect(accessCodes).toContain(equipmentGrantPermission);
  await expect(page).toHaveURL(/\/player-equipment-grants(?:[?#].*)?$/, {
    timeout: 60_000,
  });
  await expect(page.getByTestId('equipment-grants-workspace')).toBeVisible({
    timeout: 60_000,
  });
}

async function logout(page: Page): Promise<void> {
  const response = waitForPageResponse(page, 'POST', '/api/auth/logout', 200);
  await page.keyboard.press('Alt+Q');
  const confirmation = page
    .getByRole('dialog')
    .filter({ hasText: 'Do you want to logout?' });
  await expect(confirmation).toBeVisible();
  await confirmation.getByRole('button', { name: 'Confirm' }).click();
  await readVoidSuccess(await response, 'administrator logout');
  await expect(page).toHaveURL(/\/auth\/login(?:[?#].*)?$/);
}

async function lookupPlayer(
  page: Page,
  expectedRevision: number,
): Promise<void> {
  await field(page, 'player-id-input').fill(environment.playerId);
  const response = waitForPageResponse(page, 'GET', grantCollectionPath, 200);
  await page.getByTestId('lookup-player').click();
  const history = await readSuccess<EquipmentGrantPage>(
    await response,
    'equipment-grant history lookup',
  );
  expect(history.playerId).toBe(environment.playerId);
  expect(history.revision).toBe(expectedRevision);
  await expect(page.getByTestId('resolved-player-id')).toHaveText(
    environment.playerId,
  );
  await expect(page.getByTestId('current-revision')).toHaveText(
    String(expectedRevision),
  );
  await expect(page.getByTestId('equipment-grants-empty')).toBeVisible();
}

async function refreshHistory(
  page: Page,
  expectedRevision: number,
): Promise<void> {
  const response = waitForPageResponse(page, 'GET', grantCollectionPath, 200);
  await page.getByTestId('refresh-history').click();
  const history = await readSuccess<EquipmentGrantPage>(
    await response,
    'authoritative history refresh',
  );
  expect(history.revision).toBe(expectedRevision);
  await expect(page.getByTestId('current-revision')).toHaveText(
    String(expectedRevision),
  );
}

async function openGrantDialog(page: Page): Promise<void> {
  await page.getByTestId('open-grant-dialog').click();
  await expect(field(page, 'grant-configured-item-id')).toBeVisible();
}

async function fillGrantForm(page: Page, reason: string): Promise<void> {
  await field(page, 'grant-configured-item-id').fill(
    environment.configuredItemId,
  );
  await field(page, 'grant-reason').fill(reason);
}

async function openRollbackDialog(page: Page): Promise<void> {
  await page.getByTestId('open-rollback-dialog').click();
  await expect(field(page, 'rollback-reason')).toBeVisible();
}

async function fillRollbackForm(page: Page, reason: string): Promise<void> {
  await field(page, 'rollback-reason').fill(reason);
}

async function closeVisibleDialog(
  page: Page,
  confirmationTestId: string,
): Promise<void> {
  const dialog = page
    .locator('.el-dialog:visible')
    .filter({ has: page.getByTestId(confirmationTestId) });
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  await expect(dialog).toBeHidden();
}

async function equipIssuedItem(
  request: APIRequestContext,
  ownedItemId: string,
  expectedRevision: number,
): Promise<void> {
  const response = await request.put(
    `${environment.apiUrl}${playerEquipmentPath}`,
    {
      data: { ownedItemId },
      headers: playerMutationHeaders(expectedRevision),
    },
  );
  const mutation = await readSuccess<PlayerAssetsMutation>(
    response,
    'player equipment assignment',
  );
  expect(mutation.revision).toBe(expectedRevision + 1);
  expect(response.headers().etag).toBe(`"${expectedRevision + 1}"`);
}

async function unequipIssuedItem(
  request: APIRequestContext,
  expectedRevision: number,
): Promise<void> {
  const response = await request.delete(
    `${environment.apiUrl}${playerEquipmentPath}`,
    { headers: playerMutationHeaders(expectedRevision) },
  );
  const mutation = await readSuccess<PlayerAssetsMutation>(
    response,
    'player equipment removal',
  );
  expect(mutation.revision).toBe(expectedRevision + 1);
  expect(response.headers().etag).toBe(`"${expectedRevision + 1}"`);
}

function playerMutationHeaders(
  expectedRevision: number,
): Record<string, string> {
  return {
    Authorization: `Bearer ${environment.playerToken}`,
    'Content-Type': 'application/json',
    'Idempotency-Key': randomUUID().toLowerCase(),
    'If-Match': `"${expectedRevision}"`,
    'X-Correlation-Id': `m306-t03-player-${randomUUID().replaceAll('-', '')}`,
  };
}

function field(page: Page, testId: string) {
  return page
    .locator(
      `input[data-testid="${testId}"], textarea[data-testid="${testId}"], ` +
        `[data-testid="${testId}"] input, [data-testid="${testId}"] textarea`,
    )
    .first();
}

function waitForPageResponse(
  page: Page,
  method: string,
  path: string,
  expectedStatus: number,
): Promise<Response> {
  return waitForPageResponseOrFailure(page, method, path).then((response) => {
    if (response.status() !== expectedStatus) {
      throw new Error(
        `${method} ${path} returned status ${response.status()} instead of ${expectedStatus}.`,
      );
    }
    return response;
  });
}

function waitForPageResponseOrFailure(
  page: Page,
  method: string,
  path: string,
): Promise<Response> {
  const matchesRequest = (request: Request): boolean =>
    request.method() === method && new URL(request.url()).pathname === path;
  let failedRequestHandler: (request: Request) => void = () => undefined;
  const failedRequestPromise = new Promise<never>((_, reject) => {
    failedRequestHandler = (request) => {
      if (!matchesRequest(request)) {
        return;
      }

      reject(
        new Error(
          `${method} ${path} failed before a response: ` +
            `${request.failure()?.errorText ?? 'unknown transport failure'}.`,
        ),
      );
    };
  });
  page.on('requestfailed', failedRequestHandler);

  return Promise.race([
    page.waitForResponse((response) => matchesRequest(response.request()), {
      timeout: 60_000,
    }),
    failedRequestPromise,
  ]).finally(() => {
    page.off('requestfailed', failedRequestHandler);
  });
}

async function observePageResponses(
  page: Page,
  expectedResponses: readonly ExpectedPageResponse[],
  trigger: () => Promise<void>,
): Promise<Response[]> {
  const observed = new Map<string, Response>();
  const failures: string[] = [];
  const keyFor = (method: string, path: string): string => `${method} ${path}`;
  const responseFor = (request: Request): ExpectedPageResponse | undefined =>
    expectedResponses.find(
      (expected) =>
        request.method() === expected.method &&
        new URL(request.url()).pathname === expected.path,
    );
  let resolveObserved: () => void = () => undefined;
  let rejectObserved: (reason: unknown) => void = () => undefined;
  const allObserved = new Promise<void>((resolve, reject) => {
    resolveObserved = resolve;
    rejectObserved = reject;
  });
  const responseHandler = (response: Response): void => {
    const expected = responseFor(response.request());
    if (!expected) {
      return;
    }

    observed.set(keyFor(expected.method, expected.path), response);
    if (observed.size === expectedResponses.length) {
      resolveObserved();
    }
  };
  const requestFailedHandler = (request: Request): void => {
    const expected = responseFor(request);
    if (!expected) {
      return;
    }

    const failure = request.failure()?.errorText ?? 'unknown transport failure';
    failures.push(`${keyFor(expected.method, expected.path)}: ${failure}`);
    rejectObserved(
      new Error(
        `Administrator login bootstrap request failed: ${failures.join('; ')}`,
      ),
    );
  };
  let timeout: NodeJS.Timeout | undefined;
  const timeoutPromise = new Promise<never>((_, reject) => {
    timeout = setTimeout(() => {
      const pending = expectedResponses
        .filter(
          (expected) => !observed.has(keyFor(expected.method, expected.path)),
        )
        .map((expected) => keyFor(expected.method, expected.path));
      const received = [...observed.entries()].map(
        ([key, response]) => `${key} (${response.status()})`,
      );
      reject(
        new Error(
          'Administrator login bootstrap did not observe every required response within 60000 ms. ' +
            `Received: ${received.join(', ') || 'none'}. ` +
            `Pending: ${pending.join(', ') || 'none'}. ` +
            `Failed: ${failures.join(', ') || 'none'}. Current URL: ${page.url()}.`,
        ),
      );
    }, 60_000);
  });

  page.on('response', responseHandler);
  page.on('requestfailed', requestFailedHandler);
  try {
    await trigger();
    await Promise.race([allObserved, timeoutPromise]);
    return expectedResponses.map((expected) => {
      const response = observed.get(keyFor(expected.method, expected.path));
      if (!response) {
        throw new Error(
          `Administrator login bootstrap lost ${keyFor(expected.method, expected.path)}.`,
        );
      }
      return response;
    });
  } finally {
    if (timeout) {
      clearTimeout(timeout);
    }
    page.off('response', responseHandler);
    page.off('requestfailed', requestFailedHandler);
  }
}

function isGrantRequest(request: Request, idempotencyKey: string): boolean {
  return (
    request.method() === 'POST' &&
    new URL(request.url()).pathname === grantCollectionPath &&
    request.headers()['idempotency-key'] === idempotencyKey
  );
}

async function installOneShotGrantResponseLoss(page: Page): Promise<{
  dispose: () => Promise<void>;
  waitForCommit: () => Promise<LostGrantRequest>;
}> {
  let committed: LostGrantRequest | undefined;
  let disposed = false;
  let settled = false;
  let resolveCommit: (value: LostGrantRequest) => void = () => undefined;
  let rejectCommit: (reason: unknown) => void = () => undefined;
  const commit = new Promise<LostGrantRequest>((resolve, reject) => {
    resolveCommit = resolve;
    rejectCommit = reject;
  });
  const handler = async (route: Route): Promise<void> => {
    const request = route.request();
    if (
      settled ||
      request.method() !== 'POST' ||
      new URL(request.url()).pathname !== grantCollectionPath
    ) {
      await route.continue();
      return;
    }

    settled = true;
    try {
      // route.fetch() forwards the original method, headers, and post body to the real Vite proxy.
      const response = await route.fetch();
      const mutation = await readSuccess<EquipmentGrantMutation>(
        response,
        'response-loss grant commit',
      );
      const ifMatch = request.headers()['if-match'];
      const idempotencyKey = request.headers()['idempotency-key'];
      if (!ifMatch || !idempotencyKey) {
        throw new Error(
          'The real response-loss grant omitted mutation headers.',
        );
      }
      committed = {
        body: request.postData() ?? '',
        ifMatch,
        idempotencyKey,
        mutation,
      };
      await route.abort('connectionreset');
      resolveCommit(committed);
    } catch (error) {
      rejectCommit(error);
      throw error;
    }
  };

  await page.route('**/api/**', handler);
  return {
    async dispose() {
      if (disposed) {
        return;
      }
      disposed = true;
      await page.unroute('**/api/**', handler);
    },
    async waitForCommit() {
      return await commit;
    },
  };
}

async function assertMobileLayout(page: Page): Promise<void> {
  await expect(page.getByTestId('equipment-grants-workspace')).toBeVisible();
  await expect(page.getByTestId('lookup-player')).toBeVisible();
  await expect(page.getByTestId('open-grant-dialog')).toBeVisible();
  const dimensions = await page.locator('body').evaluate((body) => ({
    clientWidth: body.clientWidth,
    scrollWidth: body.scrollWidth,
  }));
  expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
}

async function readSuccess<T>(
  response: APIResponse | Response,
  operation: string,
): Promise<T> {
  let envelope: ApiEnvelope<T>;
  try {
    envelope = (await response.json()) as ApiEnvelope<T>;
  } catch {
    throw new Error(`${operation} did not return a JSON API envelope.`);
  }
  if (!response.ok() || !envelope.succeeded || envelope.data === undefined) {
    const code = envelope.error?.code ?? 'NovaGame.UnknownError';
    const message =
      envelope.error?.message ?? 'No API error message was returned.';
    throw new Error(
      `${operation} did not return successful API data: HTTP ${response.status()} ` +
        `${code}: ${message}`,
    );
  }
  return envelope.data;
}

async function readVoidSuccess(
  response: APIResponse | Response,
  operation: string,
): Promise<void> {
  let envelope: ApiEnvelope<unknown>;
  try {
    envelope = (await response.json()) as ApiEnvelope<unknown>;
  } catch {
    throw new Error(`${operation} did not return a JSON API envelope.`);
  }
  if (!response.ok() || !envelope.succeeded) {
    const code = envelope.error?.code ?? 'NovaGame.UnknownError';
    const message =
      envelope.error?.message ?? 'No API error message was returned.';
    throw new Error(
      `${operation} did not return a successful API response: HTTP ${response.status()} ` +
        `${code}: ${message}`,
    );
  }
}

async function expectApiError(
  response: APIResponse | Response,
  expectedCode: string,
): Promise<void> {
  let envelope: ApiEnvelope<unknown>;
  try {
    envelope = (await response.json()) as ApiEnvelope<unknown>;
  } catch {
    throw new Error(
      'Expected an API error envelope but the response was not JSON.',
    );
  }
  expect(response.ok()).toBe(false);
  expect(envelope.succeeded).toBe(false);
  expect(envelope.error?.code).toBe(expectedCode);
}

function resetPlayerFixture(): void {
  assertDisposableFixture();
  const connection = parseConnectionString(
    environment.swordMasterConnectionString,
    'swordmaster_test',
  );
  const playerId = sqlText(environment.playerId);
  const characterId = sqlText(environment.characterId);
  const configuredItemId = sqlText(environment.configuredItemId);
  const seedItemId = sqlText('f1111111-1111-1111-1111-111111111111');
  executeMySql(
    connection,
    `START TRANSACTION;
DELETE FROM sm_player_equipment_grants WHERE PlayerId = ${playerId};
DELETE FROM sm_player_asset_command_receipts WHERE PlayerId = ${playerId};
DELETE FROM sm_equipment_assignments WHERE PlayerId = ${playerId};
DELETE FROM sm_owned_equipment_items WHERE PlayerId = ${playerId};
DELETE FROM sm_player_character_base_attributes WHERE PlayerId = ${playerId};
DELETE FROM sm_player_characters WHERE PlayerId = ${playerId};
DELETE FROM sm_player_assets_profiles WHERE PlayerId = ${playerId};
INSERT INTO sm_player_assets_profiles (PlayerId, Revision) VALUES (${playerId}, 1);
INSERT INTO sm_player_characters (PlayerId, CharacterId) VALUES (${playerId}, ${characterId});
INSERT INTO sm_player_character_base_attributes (PlayerId, StatCode, StatValue)
VALUES (${playerId}, ${sqlText('attack')}, 10), (${playerId}, ${sqlText('health')}, 100);
INSERT INTO sm_owned_equipment_items (PlayerId, OwnedItemId, ConfiguredItemId)
VALUES (${playerId}, ${seedItemId}, ${configuredItemId});
COMMIT;`,
  );
}

function revokeEquipmentGrantPermission(): PermissionRow {
  assertDisposableFixture();
  const connection = parseConnectionString(
    environment.novaGameConnectionString,
    'novagame_test',
  );
  const rows = lines(
    executeMySql(
      connection,
      `SELECT HEX(permission.Id), HEX(permission.RoleId), HEX(permission.Name),
DATE_FORMAT(permission.GrantedAtUtc, '%Y-%m-%d %H:%i:%s.%f'),
COALESCE(HEX(permission.GrantedByAdministratorId), 'NULL')
FROM admin_role_permissions AS permission
INNER JOIN admin_roles AS role ON role.Id = permission.RoleId
WHERE role.NormalizedName = ${sqlText(administratorsRole)}
  AND permission.Name = ${sqlText(equipmentGrantPermission)};`,
    ),
  );
  if (rows.length !== 1) {
    throw new Error(
      'The disposable fixture did not contain exactly one grant permission row.',
    );
  }
  const values = rows[0]!.split('\t');
  if (values.length !== 5) {
    throw new Error(
      'The disposable grant permission row has an invalid shape.',
    );
  }
  const [idHex, roleIdHex, nameHex, grantedAtUtc, grantedByAdministratorIdHex] =
    values;
  if (
    !isHex(idHex) ||
    !isHex(roleIdHex) ||
    !isHex(nameHex) ||
    !grantedAtUtc ||
    (grantedByAdministratorIdHex !== 'NULL' &&
      !isHex(grantedByAdministratorIdHex))
  ) {
    throw new Error(
      'The disposable grant permission row contains invalid values.',
    );
  }
  const permission: PermissionRow = {
    grantedAtUtc,
    grantedByAdministratorIdHex:
      grantedByAdministratorIdHex === 'NULL'
        ? null
        : grantedByAdministratorIdHex,
    idHex,
    nameHex,
    roleIdHex,
  };
  const deleted = lines(
    executeMySql(
      connection,
      `DELETE FROM admin_role_permissions WHERE Id = ${sqlHex(permission.idHex)};
SELECT ROW_COUNT();`,
    ),
  );
  if (deleted.length !== 1 || deleted[0] !== '1') {
    throw new Error(
      'The disposable grant permission was not removed exactly once.',
    );
  }
  return permission;
}

function restoreEquipmentGrantPermission(permission: PermissionRow): void {
  const connection = parseConnectionString(
    environment.novaGameConnectionString,
    'novagame_test',
  );
  const grantedAtUtc = sqlText(permission.grantedAtUtc);
  const grantedByAdministratorId = permission.grantedByAdministratorIdHex
    ? sqlHex(permission.grantedByAdministratorIdHex)
    : 'NULL';
  const result = lines(
    executeMySql(
      connection,
      `INSERT INTO admin_role_permissions
  (Id, RoleId, Name, GrantedAtUtc, GrantedByAdministratorId)
VALUES
  (${sqlHex(permission.idHex)}, ${sqlHex(permission.roleIdHex)}, ${sqlHex(permission.nameHex)},
   STR_TO_DATE(${grantedAtUtc}, '%Y-%m-%d %H:%i:%s.%f'), ${grantedByAdministratorId});
SELECT COUNT(*) FROM admin_role_permissions WHERE Id = ${sqlHex(permission.idHex)};`,
    ),
  );
  if (result.length !== 1 || result[0] !== '1') {
    throw new Error(
      'The disposable grant permission was not restored exactly once.',
    );
  }
}

function assertDisposableFixture(): void {
  if (environment.disposableDatabaseMarker !== disposableDatabaseMarker) {
    throw new Error(
      'Refusing fixture mutation without the expected disposable marker.',
    );
  }
}

function parseConnectionString(
  connectionString: string,
  expectedDatabase: string,
): ConnectionDetails {
  const values = new Map<string, string>();
  for (const segment of connectionString.split(';')) {
    const separator = segment.indexOf('=');
    if (separator <= 0) {
      continue;
    }
    values.set(
      segment.slice(0, separator).trim().toLowerCase(),
      segment.slice(separator + 1).trim(),
    );
  }
  const database = requireConnectionValue(values, ['database']);
  const server = requireConnectionValue(values, ['server', 'host']);
  const password = requireConnectionValue(values, ['password', 'pwd']);
  const user = requireConnectionValue(values, ['user id', 'uid', 'user']);
  const port = Number(requireConnectionValue(values, ['port']));
  if (
    database !== expectedDatabase ||
    !['127.0.0.1', 'localhost'].includes(server.toLowerCase()) ||
    !Number.isInteger(port) ||
    port < 1 ||
    port > 65_535 ||
    !/^[A-Za-z][A-Za-z0-9_]{0,31}$/.test(user) ||
    !/^[A-Za-z0-9_-]{32,171}$/.test(password)
  ) {
    throw new Error(
      'The browser acceptance connection string is not disposable MySQL input.',
    );
  }
  return { database, password, port, server, user };
}

function requireConnectionValue(
  values: ReadonlyMap<string, string>,
  names: string[],
): string {
  for (const name of names) {
    const value = values.get(name);
    if (value) {
      return value;
    }
  }
  throw new Error('The browser acceptance connection string is incomplete.');
}

function executeMySql(connection: ConnectionDetails, sql: string): string {
  const container = resolveDisposableMySqlContainer(connection);
  const result = spawnSync(
    'docker',
    [
      'exec',
      '--env',
      'MYSQL_PWD',
      container,
      'mysql',
      '--protocol=TCP',
      '--host=127.0.0.1',
      `--user=${connection.user}`,
      `--database=${connection.database}`,
      '--batch',
      '--raw',
      '--skip-column-names',
      '--execute',
      sql,
    ],
    {
      encoding: 'utf8',
      env: { ...process.env, MYSQL_PWD: connection.password },
      shell: false,
      windowsHide: true,
    },
  );
  if (result.error || result.status !== 0) {
    throw new Error('Guarded disposable MySQL command failed.');
  }
  return result.stdout;
}

function resolveDisposableMySqlContainer(
  connection: ConnectionDetails,
): string {
  const candidates = lines(
    runDocker([
      'ps',
      '--filter',
      'label=com.docker.compose.service=mysql',
      '--format',
      '{{.ID}}',
    ]),
  );
  const matching = candidates.filter((candidate) => {
    let inspect: DockerInspectResult[];
    try {
      inspect = JSON.parse(
        runDocker(['inspect', candidate]),
      ) as DockerInspectResult[];
    } catch {
      return false;
    }
    const details = inspect[0];
    const labels = details?.Config?.Labels ?? {};
    const projectName = labels['com.docker.compose.project'] ?? '';
    const portBindings = details?.NetworkSettings?.Ports?.['3306/tcp'] ?? [];
    return (
      inspect.length === 1 &&
      details?.State?.Running === true &&
      /^swordmaster-test-[a-f0-9]{12}$/.test(projectName) &&
      portBindings.some(
        (binding) =>
          binding.HostPort === String(connection.port) &&
          (binding.HostIp === '127.0.0.1' || binding.HostIp === '0.0.0.0'),
      )
    );
  });
  if (matching.length !== 1) {
    throw new Error(
      'Unable to resolve exactly one guarded disposable MySQL container.',
    );
  }
  return matching[0]!;
}

function runDocker(arguments_: string[]): string {
  const result = spawnSync('docker', arguments_, {
    encoding: 'utf8',
    shell: false,
    windowsHide: true,
  });
  if (result.error || result.status !== 0) {
    throw new Error('Docker inspection for browser acceptance failed.');
  }
  return result.stdout;
}

function sqlText(value: string): string {
  return `CONVERT(0x${Buffer.from(value, 'utf8').toString('hex')} USING utf8mb4)`;
}

function sqlHex(value: string): string {
  if (!isHex(value)) {
    throw new Error('Refusing to construct SQL from a non-hex fixture value.');
  }
  return `CONVERT(0x${value} USING utf8mb4)`;
}

function isHex(value: string | undefined): value is string {
  return (
    typeof value === 'string' && value.length > 0 && /^[0-9A-F]+$/.test(value)
  );
}

function lines(value: string): string[] {
  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}

function requireCanonicalGuid(name: string): string {
  const value = requireNonEmptyEnvironment(name).toLowerCase();
  if (
    !guidPattern.test(value) ||
    value === '00000000-0000-0000-0000-000000000000'
  ) {
    throw new Error(`${name} must be a non-empty lowercase D-format GUID.`);
  }
  return value;
}

function requireLoopbackUrl(name: string): string {
  const value = requireNonEmptyEnvironment(name);
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new Error(`${name} must be an absolute loopback HTTP(S) URL.`);
  }
  if (
    !['http:', 'https:'].includes(url.protocol) ||
    !['127.0.0.1', 'localhost'].includes(url.hostname.toLowerCase())
  ) {
    throw new Error(`${name} must target a loopback HTTP(S) URL.`);
  }
  return url.toString().replace(/\/$/, '');
}

function requireNonEmptyEnvironment(name: string): string {
  const value = process.env[name]?.trim();
  if (!value) {
    throw new Error(
      `Required browser acceptance variable '${name}' is missing.`,
    );
  }
  return value;
}
