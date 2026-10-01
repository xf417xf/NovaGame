import type {
  APIRequestContext,
  APIResponse,
  Browser,
  BrowserContext,
  Page,
  Request,
  Response,
  Route,
  TestInfo,
} from '@playwright/test';

import { Buffer } from 'node:buffer';
import { spawnSync } from 'node:child_process';
import { createHash, createHmac } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { createConnection } from 'node:net';
import { basename, dirname, join } from 'node:path';
import process from 'node:process';
import { TextDecoder } from 'node:util';

import { test as base, expect } from '@playwright/test';
import { visit } from 'jsonc-parser';

interface AcceptanceEvidenceFixtures {
  acceptanceEvidence: AcceptanceEvidenceAudit;
}

interface AuditedResponse {
  sequence: number;
  method: string;
  route: string;
  status: number;
  bodyBytes: number;
  scanned: boolean;
}

interface ContextLedger {
  closedBeforeListenerDetach: number;
  observed: number;
}

interface ExpectedApiNetworkFailure {
  abortIssued: boolean;
  consumed: boolean;
  exactUrl: string;
  method: string;
  request?: Request;
  route: string;
  routeHandler: (route: Route, request: Request) => Promise<void>;
}

interface ExpectedNetworkFailureRecord {
  count: number;
  method: string;
  pathAndQuery: string;
}

interface JsonBodyInspection {
  duplicateKey: boolean;
  issuedAccessTokenSpans: JsonStringSpan[];
  parseError: boolean;
}

interface JsonStringSpan {
  length: number;
  offset: number;
  value: string;
}

interface ResponseSource {
  body(): Promise<Buffer>;
  status(): number;
  url(): string;
}

type ApiRequestFetchOptions = Exclude<
  Parameters<APIRequestContext['fetch']>[1],
  undefined
>;

interface SafeViolation {
  method: string;
  route: string;
  ruleId: string;
  status: number;
}

const auditReportDirectory = 'output/playwright/evidence';
const dynamicSecretPipeDeadlineMs = 5000;
const dynamicSecretPipeEnvironmentName = 'NOVAGAME_E2E_DYNAMIC_SECRET_PIPE';
const dynamicSecretPipeKeyEnvironmentName =
  'NOVAGAME_E2E_DYNAMIC_SECRET_PIPE_KEY';
const dynamicSecretPipeRetryDelayMs = 10;
const dynamicSecretPipeRetryErrorCodes = new Set(['EBUSY', 'ENOENT']);
const refreshCookieName = 'novagame.admin.refresh_token';
const authTokenRoutes = new Set(['/api/auth/login', '/api/auth/refresh']);
const sensitiveEnvironmentNames = [
  dynamicSecretPipeKeyEnvironmentName,
  'NOVAGAME_E2E_PASSWORD',
  'NOVAGAME_E2E_MYSQL_ROOT_PASSWORD',
  'NOVAGAME_E2E_CREATE_PASSWORD',
  'NOVAGAME_E2E_CREATE_RETRY_PASSWORD',
  'NOVAGAME_E2E_RESET_PASSWORD',
  'NOVAGAME_E2E_RESET_RETRY_PASSWORD',
  'NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING',
  'NOVAGAME_MYSQL_PASSWORD',
  'NOVAGAME_MYSQL_ROOT_PASSWORD',
  'NOVAGAME_TEST_MYSQL_CONNECTION_STRING',
  'ConnectionStrings__Default',
  'NovaGame__AdminAccess__Jwt__SigningKey',
] as const;
const reportNamesByTitle = new Map([
  [
    'administrator lifecycle and concurrency',
    'admin-access-administrator-lifecycle-response-audit.json',
  ],
  [
    'operator completes the GameConfig lifecycle against the real API',
    'game-config-response-audit.json',
  ],
  [
    'responsive and failure states',
    'admin-access-responsive-failures-response-audit.json',
  ],
  [
    'roles, permissions, and authorization',
    'admin-access-roles-permissions-response-audit.json',
  ],
  ['sessions and audit', 'admin-access-sessions-audit-response-audit.json'],
]);
const sensitiveJsonKeys = new Set([
  'accesstoken',
  'authorization',
  'connectionstring',
  'password',
  'passwordhash',
  'refreshtoken',
  'signingkey',
  'tokenhash',
]);
const forbiddenTextPatterns = [
  {
    id: 'jwt',
    pattern:
      /(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+(?![A-Za-z0-9_-])/,
  },
  {
    id: 'opaque-base64url-32-byte',
    pattern: /(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{43}(?![A-Za-z0-9_-])/,
  },
  {
    id: 'mysql-uri',
    pattern: /\bmysql(?:\+[A-Za-z0-9._-]+)?:\/\/[^\s"'<>]+/i,
  },
  {
    id: 'password-connection-string',
    pattern: /\b(?:server|host)\s*=\s*[^;\s]+;[^\r\n]{0,512}\bpassword\s*=/i,
  },
  {
    id: 'aspnet-password-hash',
    pattern:
      /(?<![A-Za-z0-9+/])AQAAAA[A-Za-z0-9+/]{48,}={0,2}(?![A-Za-z0-9+/=])/,
  },
  {
    id: 'authorization-bearer',
    pattern: /\bAuthorization\s*:\s*Bearer\s+\S+/i,
  },
  {
    id: 'sensitive-json-field',
    pattern:
      /"(?:password|connection[\s_-]*string|access[\s_-]*token|refresh[\s_-]*token|signing[\s_-]*key|token[\s_-]*hash)"\s*:\s*"(?!\[REDACTED\])[^"\r\n]+"/i,
  },
] as const;

type DynamicSecretKind = 'access' | 'refresh';

async function registerDynamicSecret(
  kind: DynamicSecretKind,
  value: string,
): Promise<void> {
  const pipeName = process.env[dynamicSecretPipeEnvironmentName];
  const encodedKey = process.env[dynamicSecretPipeKeyEnvironmentName];
  if (
    !pipeName ||
    !/^[A-Za-z0-9-]{16,128}$/.test(pipeName) ||
    !encodedKey ||
    !/^[A-Za-z0-9_-]{43}$/.test(encodedKey)
  ) {
    throw new AcceptanceEvidenceViolation('dynamic-secret-pipe-configuration');
  }
  const authenticationKey = Buffer.from(encodedKey, 'base64url');
  const secretBytes = Buffer.from(value, 'utf8');
  if (
    authenticationKey.length !== 32 ||
    secretBytes.length === 0 ||
    secretBytes.length > 16 * 1024
  ) {
    throw new AcceptanceEvidenceViolation('dynamic-secret-pipe-payload');
  }
  const header = Buffer.alloc(10);
  header.write('NGDS', 0, 'ascii');
  header[4] = 1;
  header[5] = kind === 'access' ? 1 : 2;
  header.writeUInt32BE(secretBytes.length, 6);
  const authenticatedPayload = Buffer.concat([header, secretBytes]);
  const authenticationCode = createHmac('sha256', authenticationKey)
    .update(authenticatedPayload)
    .digest();
  const frame = Buffer.concat([authenticatedPayload, authenticationCode]);
  authenticationKey.fill(0);

  await new Promise<void>((resolve, reject) => {
    let activeSocket: ReturnType<typeof createConnection> | undefined;
    let buffersCleared = false;
    const deadline = Date.now() + dynamicSecretPipeDeadlineMs;
    let retryTimer: NodeJS.Timeout | undefined;
    let settled = false;
    const deadlineTimer = setTimeout(
      () => fail('dynamic-secret-pipe-timeout'),
      dynamicSecretPipeDeadlineMs,
    );
    const clearSensitiveBuffers = () => {
      if (buffersCleared) {
        return;
      }
      buffersCleared = true;
      frame.fill(0);
      authenticatedPayload.fill(0);
      authenticationCode.fill(0);
      header.fill(0);
      secretBytes.fill(0);
    };
    const finish = (ruleId?: string) => {
      if (settled) {
        return;
      }
      settled = true;
      clearTimeout(deadlineTimer);
      if (retryTimer) {
        clearTimeout(retryTimer);
      }
      const socket = activeSocket;
      activeSocket = undefined;
      clearSensitiveBuffers();
      socket?.destroy();
      if (ruleId) {
        reject(new AcceptanceEvidenceViolation(ruleId));
      } else {
        resolve();
      }
    };
    const fail = (ruleId: string) => finish(ruleId);
    const connect = () => {
      if (settled) {
        return;
      }
      if (Date.now() >= deadline) {
        fail('dynamic-secret-pipe-timeout');
        return;
      }

      let socket: ReturnType<typeof createConnection>;
      try {
        socket = createConnection(`\\\\.\\pipe\\${pipeName}`);
      } catch {
        fail('dynamic-secret-pipe-connection');
        return;
      }
      activeSocket = socket;
      let acknowledgement = Buffer.alloc(0);
      let acknowledged = false;
      let connected = false;
      let retryAfterClose = false;

      socket.once('connect', () => {
        if (settled || activeSocket !== socket) {
          socket.destroy();
          return;
        }
        connected = true;
        socket.write(frame, (error) => {
          clearSensitiveBuffers();
          if (error) {
            fail('dynamic-secret-pipe-write');
          }
        });
      });
      socket.on('data', (chunk) => {
        if (settled || activeSocket !== socket) {
          return;
        }
        if (typeof chunk === 'string') {
          fail('dynamic-secret-pipe-acknowledgement');
          return;
        }
        acknowledgement = Buffer.concat([acknowledgement, chunk]);
        if (acknowledgement.length > 6) {
          fail('dynamic-secret-pipe-acknowledgement');
          return;
        }
        if (acknowledgement.length === 6) {
          const validAcknowledgement =
            acknowledgement.subarray(0, 4).toString('ascii') === 'NGDA' &&
            acknowledgement[4] === 1 &&
            acknowledgement[5] === 0;
          if (!validAcknowledgement) {
            fail('dynamic-secret-pipe-acknowledgement');
            return;
          }
          acknowledged = true;
          socket.destroy();
        }
      });
      socket.once('error', (error) => {
        if (settled || activeSocket !== socket || acknowledged) {
          return;
        }
        const errorCode = (error as NodeJS.ErrnoException).code;
        if (
          !connected &&
          errorCode !== undefined &&
          dynamicSecretPipeRetryErrorCodes.has(errorCode) &&
          Date.now() < deadline
        ) {
          retryAfterClose = true;
          socket.destroy();
          return;
        }
        fail('dynamic-secret-pipe-connection');
      });
      socket.once('close', () => {
        if (settled || activeSocket !== socket) {
          return;
        }
        if (acknowledged) {
          finish();
          return;
        }
        if (!retryAfterClose) {
          fail('dynamic-secret-pipe-closed');
          return;
        }

        activeSocket = undefined;
        const remaining = deadline - Date.now();
        if (remaining <= 0) {
          fail('dynamic-secret-pipe-timeout');
          return;
        }
        retryTimer = setTimeout(
          () => {
            retryTimer = undefined;
            connect();
          },
          Math.min(dynamicSecretPipeRetryDelayMs, remaining),
        );
      });
    };

    connect();
  });
}

class AcceptanceEvidenceViolation extends Error {
  constructor(public readonly ruleId: string) {
    super(`Acceptance evidence rule '${ruleId}' failed.`);
    this.name = 'AcceptanceEvidenceViolation';
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function normalizeKey(value: string): string {
  return value.replaceAll(/[^A-Za-z0-9]/g, '').toLowerCase();
}

function routeFromUrl(value: string): string {
  try {
    return new URL(value).pathname;
  } catch {
    throw new AcceptanceEvidenceViolation('invalid-response-url');
  }
}

function responseMethod(
  response: ResponseSource,
  fallback = 'UNKNOWN',
): string {
  if ('request' in response && typeof response.request === 'function') {
    return (response as Response).request().method();
  }
  return fallback;
}

function safeViolation(error: unknown): AcceptanceEvidenceViolation {
  return error instanceof AcceptanceEvidenceViolation
    ? error
    : new AcceptanceEvidenceViolation('response-audit-internal');
}

function safeRouteFromUrl(value: string): string {
  try {
    return routeFromUrl(value);
  } catch {
    return '/invalid-response-url';
  }
}

function requiredHttpOrigin(environmentName: string): string {
  const value = process.env[environmentName];
  if (!value) {
    throw new AcceptanceEvidenceViolation('evidence-origin-configuration');
  }
  let parsed: URL;
  try {
    parsed = new URL(value);
  } catch {
    throw new AcceptanceEvidenceViolation('evidence-origin-configuration');
  }
  if (
    !['http:', 'https:'].includes(parsed.protocol) ||
    parsed.pathname !== '/' ||
    parsed.search.length > 0 ||
    parsed.hash.length > 0 ||
    parsed.username.length > 0 ||
    parsed.password.length > 0
  ) {
    throw new AcceptanceEvidenceViolation('evidence-origin-configuration');
  }
  return parsed.origin;
}

function inspectJsonBody(value: string): JsonBodyInspection {
  const objectKeys: Array<Set<string>> = [];
  const inspection: JsonBodyInspection = {
    duplicateKey: false,
    issuedAccessTokenSpans: [],
    parseError: false,
  };
  visit(
    value,
    {
      onError: () => {
        inspection.parseError = true;
      },
      onLiteralValue: (
        literalValue,
        offset,
        length,
        _line,
        _character,
        getPath,
      ) => {
        const path = getPath();
        if (
          path.length === 2 &&
          path[0] === 'data' &&
          path[1] === 'accessToken' &&
          typeof literalValue === 'string'
        ) {
          inspection.issuedAccessTokenSpans.push({
            length,
            offset,
            value: literalValue,
          });
        }
      },
      onObjectBegin: () => {
        objectKeys.push(new Set<string>());
      },
      onObjectEnd: () => {
        objectKeys.pop();
      },
      onObjectProperty: (property) => {
        const currentKeys = objectKeys.at(-1);
        if (!currentKeys || currentKeys.has(property)) {
          inspection.duplicateKey = true;
          return;
        }
        currentKeys.add(property);
      },
    },
    {
      allowTrailingComma: false,
      disallowComments: true,
    },
  );
  return inspection;
}

interface ContextHandlers {
  close: () => void;
  request: (request: Request) => void;
  requestFailed: (request: Request) => void;
  requestFinished: (request: Request) => void;
  response: (response: Response) => void;
}

interface IssuedAccessTokenState {
  count: number;
  value?: string;
}

export interface ExpectedApiNetworkFailureOptions {
  exactPathAndQuery: string;
  method: 'DELETE' | 'GET' | 'PATCH' | 'POST' | 'PUT';
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

interface LegacyPermissionFixture {
  container: string;
  database: string;
  roleId: string;
  rootPassword: string;
}

const legacyGameConfigPermissionNames = [
  'Content.GameConfig.ManageDrafts',
  'Content.GameConfig.Publish',
  'Content.GameConfig.Activate',
] as const;

function runLegacyDocker(
  args: string[],
  environment?: NodeJS.ProcessEnv,
): string {
  const result = spawnSync('docker', args, {
    encoding: 'utf8',
    env: environment,
    shell: false,
    windowsHide: true,
  });
  if (result.error || result.status !== 0) {
    throw new AcceptanceEvidenceViolation('legacy-permission-fixture-docker');
  }
  return result.stdout.trim();
}

function legacySqlUtf8Literal(value: string): string {
  return `CONVERT(0x${Buffer.from(value, 'utf8').toString('hex')} USING utf8mb4)`;
}

function executeLegacyMySql(
  fixture: Omit<LegacyPermissionFixture, 'roleId'>,
  sql: string,
): string {
  return runLegacyDocker(
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

function legacyOutputLines(output: string): string[] {
  return output
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}

function createLegacyPermissionFixture(
  username: string,
): LegacyPermissionFixture {
  const container = process.env.NOVAGAME_E2E_MYSQL_CONTAINER;
  const rootPassword = process.env.NOVAGAME_E2E_MYSQL_ROOT_PASSWORD;
  const database = process.env.NOVAGAME_E2E_MYSQL_DATABASE;
  if (!container || !rootPassword || database !== 'novagame_e2e') {
    throw new AcceptanceEvidenceViolation('legacy-permission-fixture-scope');
  }

  let inspectResults: DockerInspectResult[];
  try {
    inspectResults = JSON.parse(
      runLegacyDocker(['inspect', container]),
    ) as DockerInspectResult[];
  } catch {
    throw new AcceptanceEvidenceViolation('legacy-permission-fixture-scope');
  }
  const inspected = inspectResults.length === 1 ? inspectResults[0] : undefined;
  const labels = inspected?.Config?.Labels ?? {};
  const composeProject = labels['com.docker.compose.project'] ?? '';
  const containerName = inspected?.Name?.replace(/^\/+/, '') ?? '';
  if (
    !/^novagame-test-[a-f0-9]{12}$/.test(composeProject) ||
    labels['com.docker.compose.service'] !== 'mysql' ||
    !containerName.startsWith(`${composeProject}-mysql-`) ||
    inspected?.State?.Running !== true
  ) {
    throw new AcceptanceEvidenceViolation('legacy-permission-fixture-scope');
  }

  const mysqlFixture = { container, database, rootPassword };
  if (executeLegacyMySql(mysqlFixture, 'SELECT DATABASE();') !== database) {
    throw new AcceptanceEvidenceViolation('legacy-permission-fixture-scope');
  }
  const normalizedUsername = username.trim().normalize('NFKC').toUpperCase();
  const roleIds = legacyOutputLines(
    executeLegacyMySql(
      mysqlFixture,
      `SELECT DISTINCT rm.RoleId
FROM admin_role_memberships AS rm
INNER JOIN admin_administrators AS a ON a.Id = rm.AdministratorId
WHERE a.NormalizedUserName = ${legacySqlUtf8Literal(normalizedUsername)}
ORDER BY rm.RoleId;`,
    ),
  );
  const roleId = roleIds.length === 1 ? roleIds[0] : undefined;
  if (!roleId) {
    throw new AcceptanceEvidenceViolation('legacy-permission-fixture-role');
  }
  const fixture = { ...mysqlFixture, roleId };
  const permissionList = legacyGameConfigPermissionNames
    .map((permissionName) => legacySqlUtf8Literal(permissionName))
    .join(', ');
  const permissionCount = Number(
    executeLegacyMySql(
      fixture,
      `SELECT COUNT(*)
FROM admin_role_permissions
WHERE RoleId = ${legacySqlUtf8Literal(roleId)}
  AND Name IN (${permissionList});`,
    ),
  );
  if (permissionCount !== legacyGameConfigPermissionNames.length) {
    throw new AcceptanceEvidenceViolation(
      'legacy-permission-fixture-permissions',
    );
  }
  return fixture;
}

function revokeLegacyPermissions(fixture: LegacyPermissionFixture): void {
  const permissionList = legacyGameConfigPermissionNames
    .map((permissionName) => legacySqlUtf8Literal(permissionName))
    .join(', ');
  const results = legacyOutputLines(
    executeLegacyMySql(
      fixture,
      `DELETE FROM admin_role_permissions
WHERE RoleId = ${legacySqlUtf8Literal(fixture.roleId)}
  AND Name IN (${permissionList});
SELECT ROW_COUNT();
SELECT COUNT(*)
FROM admin_role_permissions
WHERE RoleId = ${legacySqlUtf8Literal(fixture.roleId)}
  AND Name IN (${permissionList});`,
    ),
  ).map(Number);
  if (
    results.length !== 2 ||
    results[0] !== legacyGameConfigPermissionNames.length ||
    results[1] !== 0
  ) {
    throw new AcceptanceEvidenceViolation('legacy-permission-fixture-revoke');
  }
}

function restoreLegacyPermissions(fixture: LegacyPermissionFixture): void {
  const permissionRows = legacyGameConfigPermissionNames
    .map((permissionName, index) =>
      index === 0
        ? `SELECT ${legacySqlUtf8Literal(permissionName)} AS Name`
        : `UNION ALL SELECT ${legacySqlUtf8Literal(permissionName)}`,
    )
    .join('\n');
  const permissionList = legacyGameConfigPermissionNames
    .map((permissionName) => legacySqlUtf8Literal(permissionName))
    .join(', ');
  const restoredCount = Number(
    executeLegacyMySql(
      fixture,
      `INSERT INTO admin_role_permissions
    (Id, RoleId, Name, GrantedAtUtc, GrantedByAdministratorId)
SELECT UUID(), ${legacySqlUtf8Literal(fixture.roleId)}, permissions.Name, UTC_TIMESTAMP(6), NULL
FROM (
${permissionRows}
) AS permissions
LEFT JOIN admin_role_permissions AS existing
  ON existing.RoleId = ${legacySqlUtf8Literal(fixture.roleId)}
 AND existing.Name = permissions.Name
WHERE existing.Id IS NULL;
SELECT COUNT(*)
FROM admin_role_permissions
WHERE RoleId = ${legacySqlUtf8Literal(fixture.roleId)}
  AND Name IN (${permissionList});`,
    ),
  );
  if (restoredCount !== legacyGameConfigPermissionNames.length) {
    throw new AcceptanceEvidenceViolation('legacy-permission-fixture-restore');
  }
}

export class AcceptanceEvidenceAudit {
  private activeExpectedNetworkFailure?: ExpectedApiNetworkFailure;
  private activityGeneration = 0;
  private readonly browser: Browser;
  private readonly browserApiOrigin: string;
  private browserRequestsCompleted = 0;
  private browserRequestsFailed = 0;
  private browserRequestsObserved = 0;
  private browserResponsesObserved = 0;
  private readonly closedContexts = new Set<BrowserContext>();
  private readonly context: BrowserContext;
  private readonly directApiOrigin: string;
  private directApiRequestsCompleted = 0;
  private directApiRequestsFailed = 0;
  private directApiRequestsObserved = 0;
  private readonly exactSensitiveValues = new Set<string>();
  private readonly expectedNetworkFailures: ExpectedNetworkFailureRecord[] = [];
  private readonly failures: SafeViolation[] = [];
  private readonly lastAuthUrlByContext = new Map<BrowserContext, string>();
  private legacyPermissionFixture?: LegacyPermissionFixture;
  private readonly observedContexts = new Map<
    BrowserContext,
    ContextHandlers
  >();
  private readonly pending = new Set<Promise<void>>();
  private readonly refreshTokenByContext = new Map<BrowserContext, string>();
  private readonly requestContexts = new WeakMap<Request, BrowserContext>();
  private readonly requests = new Map<Request, 'pending'>();
  private readonly responses: AuditedResponse[] = [];
  private responseSequence = 0;
  private readonly responseTasks = new WeakMap<object, Promise<void>>();
  private screenshotCaptureInProgress = false;
  private readonly screenshotPaths = new Set<string>();
  private screenshotSurfaceScans = 0;

  constructor(browser: Browser, page: Page) {
    this.browser = browser;
    this.browserApiOrigin = requiredHttpOrigin('NOVAGAME_E2E_BASE_URL');
    this.directApiOrigin = requiredHttpOrigin('NOVAGAME_E2E_API_URL');
    if (this.browserApiOrigin === this.directApiOrigin) {
      throw new AcceptanceEvidenceViolation('evidence-origin-separation');
    }
    this.context = page.context();
    for (const environmentName of sensitiveEnvironmentNames) {
      const value = process.env[environmentName];
      if (value) {
        this.registerSensitiveValue(value);
      }
    }
    this.observeContext(this.context);
  }

  async apiRequest(
    requestContext: APIRequestContext,
    method: 'DELETE' | 'GET' | 'PATCH' | 'POST' | 'PUT',
    url: string,
    options: ApiRequestFetchOptions = {},
  ): Promise<APIResponse> {
    let parsedUrl: URL;
    try {
      parsedUrl = new URL(url);
    } catch {
      throw new AcceptanceEvidenceViolation('direct-api-origin');
    }
    if (
      parsedUrl.origin !== this.directApiOrigin ||
      !parsedUrl.pathname.startsWith('/api/') ||
      parsedUrl.username.length > 0 ||
      parsedUrl.password.length > 0 ||
      parsedUrl.hash.length > 0
    ) {
      throw new AcceptanceEvidenceViolation('direct-api-origin');
    }
    this.directApiRequestsObserved += 1;
    this.touchActivity();
    let response: APIResponse;
    try {
      response = await requestContext.fetch(url, { ...options, method });
    } catch {
      this.directApiRequestsFailed += 1;
      this.recordFailure(
        method,
        safeRouteFromUrl(url),
        'direct-api-request-failed',
      );
      throw new AcceptanceEvidenceViolation('direct-api-request-failed');
    }
    await this.auditResponse(response, method);
    this.directApiRequestsCompleted += 1;
    this.touchActivity();
    return response;
  }

  async assertCurrentRefreshCookie(): Promise<void> {
    await this.settle();
    const authUrl = this.lastAuthUrlByContext.get(this.context);
    if (!authUrl) {
      throw new AcceptanceEvidenceViolation('refresh-cookie-auth-url');
    }
    await this.captureCurrentRefreshCookie(this.context, authUrl, false);
    this.assertNoViolations();
  }

  async auditResponse(
    response: APIResponse | Response,
    method?: string,
  ): Promise<void> {
    await this.queueResponse(
      response,
      method ?? responseMethod(response),
      this.responseContext(response),
    );
    this.assertNoViolations();
  }

  async captureScreenshot(page: Page, requiredPath: string): Promise<void> {
    if (
      this.screenshotCaptureInProgress ||
      this.screenshotPaths.has(requiredPath) ||
      !/^output\/playwright\/visual\/[a-z0-9][a-z0-9-]*\.png$/.test(
        requiredPath,
      )
    ) {
      throw new AcceptanceEvidenceViolation('screenshot-capture-ledger');
    }
    this.screenshotCaptureInProgress = true;
    try {
      await this.scanScreenshotSurface(page);
      await mkdir(dirname(requiredPath), { recursive: true });
      await page.screenshot({
        animations: 'disabled',
        fullPage: true,
        path: requiredPath,
      });
      this.screenshotPaths.add(requiredPath);
      this.screenshotSurfaceScans += 1;
    } finally {
      this.screenshotCaptureInProgress = false;
    }
  }

  async finalize(testInfo: TestInfo): Promise<void> {
    if (this.activeExpectedNetworkFailure) {
      this.recordFailure(
        this.activeExpectedNetworkFailure.method,
        this.activeExpectedNetworkFailure.route,
        'expected-api-network-failure-left-open',
      );
    }
    await this.waitForBrowserRequestsToSettle();
    await this.drainPendingResponses();
    const liveContexts = this.browser.contexts();
    for (const context of liveContexts) {
      if (!this.observedContexts.has(context)) {
        this.recordFailure(
          'CONTEXT',
          '/browser',
          'browser-context-not-observed',
        );
      }
    }
    for (const context of liveContexts) {
      try {
        await this.closeContext(context);
      } catch {
        this.recordFailure(
          'CONTEXT',
          '/browser',
          'browser-context-close-failed',
        );
      }
    }
    await this.drainPendingResponses();
    for (const [context, handlers] of this.observedContexts) {
      context.off('close', handlers.close);
      context.off('request', handlers.request);
      context.off('requestfailed', handlers.requestFailed);
      context.off('requestfinished', handlers.requestFinished);
      context.off('response', handlers.response);
    }
    if (this.requests.size > 0) {
      this.recordFailure('REQUEST', '/browser', 'api-requests-did-not-settle');
    }
    if (
      this.browserRequestsObserved !==
      this.browserRequestsCompleted + this.browserRequestsFailed
    ) {
      this.recordFailure('REQUEST', '/browser', 'api-request-ledger-mismatch');
    }
    if (this.browserResponsesObserved !== this.browserRequestsCompleted) {
      this.recordFailure(
        'RESPONSE',
        '/browser',
        'api-response-ledger-mismatch',
      );
    }
    if (this.closedContexts.size !== this.observedContexts.size) {
      this.recordFailure(
        'CONTEXT',
        '/browser',
        'browser-context-ledger-mismatch',
      );
    }
    if (this.responses.length === 0) {
      this.recordFailure('RESPONSE', '/api/', 'response-audit-empty');
    }
    if (this.responses.some((response) => !response.scanned)) {
      this.recordFailure('RESPONSE', '/api/', 'response-audit-incomplete');
    }
    if (
      this.screenshotCaptureInProgress ||
      this.screenshotSurfaceScans !== this.screenshotPaths.size
    ) {
      this.recordFailure('SCREENSHOT', '/visual', 'screenshot-capture-ledger');
    }
    if (this.legacyPermissionFixture) {
      this.recordFailure(
        'FIXTURE',
        '/game-config/permissions',
        'legacy-permission-fixture-left-open',
      );
    }

    const reportName = reportNamesByTitle.get(testInfo.title);
    if (!reportName) {
      throw new AcceptanceEvidenceViolation('unknown-acceptance-test-title');
    }
    await mkdir(auditReportDirectory, { recursive: true });
    const orderedResponses = this.responses.toSorted(
      (left, right) => left.sequence - right.sequence,
    );
    await writeFile(
      join(auditReportDirectory, reportName),
      `${JSON.stringify(
        {
          schemaVersion: 1,
          sourceFile: basename(testInfo.file),
          testTitle: testInfo.title,
          testStatus: testInfo.status ?? 'unknown',
          responseBodies: orderedResponses,
          responseBodyCount: orderedResponses.length,
          contextLedger: {
            closedBeforeListenerDetach: this.closedContexts.size,
            observed: this.observedContexts.size,
          } satisfies ContextLedger,
          directRequestLedger: {
            directApiRequestsCompleted: this.directApiRequestsCompleted,
            directApiRequestsFailed: this.directApiRequestsFailed,
            directApiRequestsObserved: this.directApiRequestsObserved,
          },
          expectedNetworkFailures: this.expectedNetworkFailures,
          requestLedger: {
            browserRequestsCompleted: this.browserRequestsCompleted,
            browserRequestsFailed: this.browserRequestsFailed,
            browserRequestFailuresExpected: this.expectedNetworkFailures.length,
            browserRequestsObserved: this.browserRequestsObserved,
            browserResponsesObserved: this.browserResponsesObserved,
          },
          screenshotSurfaceScanCount: this.screenshotSurfaceScans,
          violations: this.failures,
        },
        null,
        2,
      )}\n`,
      'utf8',
    );
    this.assertNoViolations();
  }

  async newObservedContext(
    options?: Parameters<Browser['newContext']>[0],
  ): Promise<BrowserContext> {
    const context = await this.browser.newContext(options);
    this.observeContext(context);
    return context;
  }

  prepareLegacyGameConfigPermissionFixture(username: string): void {
    if (this.legacyPermissionFixture) {
      throw new AcceptanceEvidenceViolation('legacy-permission-fixture-ledger');
    }
    this.legacyPermissionFixture = createLegacyPermissionFixture(username);
  }

  registerSensitiveValue(value: string): void {
    if (value.length < 8) {
      throw new AcceptanceEvidenceViolation('sensitive-value-too-short');
    }
    this.exactSensitiveValues.add(value);

    const utf8Hash = createHash('sha256').update(value, 'utf8').digest();
    this.registerHashRepresentations(utf8Hash);
    if (/^[A-Za-z0-9_-]{43}$/.test(value)) {
      const decodedValue = Buffer.from(value, 'base64url');
      if (decodedValue.length === 32) {
        this.registerHashRepresentations(
          createHash('sha256').update(decodedValue).digest(),
        );
      }
    }
  }

  restoreLegacyGameConfigMutationPermissions(): void {
    if (!this.legacyPermissionFixture) {
      throw new AcceptanceEvidenceViolation('legacy-permission-fixture-ledger');
    }
    restoreLegacyPermissions(this.legacyPermissionFixture);
    this.legacyPermissionFixture = undefined;
  }

  revokeLegacyGameConfigMutationPermissions(): void {
    if (!this.legacyPermissionFixture) {
      throw new AcceptanceEvidenceViolation('legacy-permission-fixture-ledger');
    }
    revokeLegacyPermissions(this.legacyPermissionFixture);
  }

  async withExpectedApiNetworkFailure<T>(
    page: Page,
    options: ExpectedApiNetworkFailureOptions,
    trigger: () => Promise<T>,
  ): Promise<T> {
    if (this.activeExpectedNetworkFailure) {
      throw new AcceptanceEvidenceViolation(
        'expected-api-network-failure-overlap',
      );
    }
    const currentUrl = new URL(page.url());
    const exactUrl = new URL(options.exactPathAndQuery, currentUrl);
    if (
      !options.exactPathAndQuery.startsWith('/api/') ||
      exactUrl.origin !== currentUrl.origin ||
      `${exactUrl.pathname}${exactUrl.search}` !== options.exactPathAndQuery ||
      exactUrl.hash.length > 0 ||
      exactUrl.username.length > 0 ||
      exactUrl.password.length > 0
    ) {
      throw new AcceptanceEvidenceViolation('expected-api-network-failure-url');
    }

    const scope = {} as ExpectedApiNetworkFailure;
    scope.abortIssued = false;
    scope.consumed = false;
    scope.exactUrl = exactUrl.href;
    scope.method = options.method;
    scope.route = exactUrl.pathname;
    scope.routeHandler = async (route, request) => {
      if (
        request.url() !== scope.exactUrl ||
        request.method() !== scope.method ||
        (scope.request && scope.request !== request) ||
        scope.abortIssued ||
        scope.consumed
      ) {
        this.recordFailure(
          request.method(),
          safeRouteFromUrl(request.url()),
          'expected-api-network-failure-mismatch',
        );
        await route.continue();
        return;
      }
      scope.request = request;
      scope.abortIssued = true;
      await route.abort('failed');
    };
    this.activeExpectedNetworkFailure = scope;

    let result!: T;
    let triggerError: unknown;
    try {
      await page.route(scope.exactUrl, scope.routeHandler);
      result = await trigger();
      await this.waitForExpectedNetworkFailure(scope);
    } catch (error) {
      triggerError = error;
      await this.waitForExpectedNetworkFailure(scope);
    } finally {
      try {
        await page.unroute(scope.exactUrl, scope.routeHandler);
      } catch {
        this.recordFailure(
          scope.method,
          scope.route,
          'expected-api-network-failure-unroute',
        );
      }
      if (!scope.abortIssued || !scope.consumed || !scope.request) {
        this.recordFailure(
          scope.method,
          scope.route,
          'expected-api-network-failure-not-consumed',
        );
      } else {
        this.expectedNetworkFailures.push({
          count: 1,
          method: scope.method,
          pathAndQuery: options.exactPathAndQuery,
        });
      }
      this.activeExpectedNetworkFailure = undefined;
    }
    this.assertNoViolations();
    if (triggerError) {
      throw triggerError instanceof Error
        ? triggerError
        : new AcceptanceEvidenceViolation(
            'expected-api-network-failure-trigger',
          );
    }
    return result;
  }

  private assertNoViolations(): void {
    const firstFailure = this.failures[0];
    if (firstFailure) {
      throw new Error(
        `Acceptance evidence failed rule '${firstFailure.ruleId}' for ` +
          `${firstFailure.method} ${firstFailure.route} (${firstFailure.status}).`,
      );
    }
  }

  private assertSafeText(value: string, scanOpaqueBase64Url = true): void {
    for (const sensitiveValue of this.exactSensitiveValues) {
      if (value.includes(sensitiveValue)) {
        throw new AcceptanceEvidenceViolation('exact-sensitive-value');
      }
    }
    for (const forbiddenPattern of forbiddenTextPatterns) {
      if (
        !scanOpaqueBase64Url &&
        forbiddenPattern.id === 'opaque-base64url-32-byte'
      ) {
        continue;
      }
      if (forbiddenPattern.pattern.test(value)) {
        throw new AcceptanceEvidenceViolation(forbiddenPattern.id);
      }
    }
  }

  private async captureCurrentRefreshCookie(
    context: BrowserContext,
    authUrl: string,
    trackRotation: boolean,
  ): Promise<void> {
    const parsedAuthUrl = new URL(authUrl);
    const cookies = await context.cookies(authUrl);
    const refreshCookies = cookies.filter(
      (cookie) => cookie.name === refreshCookieName,
    );
    if (refreshCookies.length !== 1) {
      throw new AcceptanceEvidenceViolation('refresh-cookie-cardinality');
    }
    const refreshCookie = refreshCookies[0];
    if (refreshCookie?.value) {
      await this.registerDynamicSensitiveValue('refresh', refreshCookie.value);
    }
    if (
      !refreshCookie ||
      refreshCookie.httpOnly !== true ||
      refreshCookie.path !== '/api/auth' ||
      refreshCookie.sameSite !== 'Strict' ||
      refreshCookie.secure !== true ||
      refreshCookie.domain.replace(/^\./, '') !== parsedAuthUrl.hostname ||
      refreshCookie.expires <= Date.now() / 1000 ||
      !/^[A-Za-z0-9_-]{43}$/.test(refreshCookie.value) ||
      Buffer.from(refreshCookie.value, 'base64url').length !== 32
    ) {
      throw new AcceptanceEvidenceViolation('refresh-cookie-policy');
    }
    if (trackRotation) {
      const previousRefreshToken = this.refreshTokenByContext.get(context);
      if (previousRefreshToken === refreshCookie.value) {
        throw new AcceptanceEvidenceViolation('refresh-cookie-not-rotated');
      }
      this.refreshTokenByContext.set(context, refreshCookie.value);
    }
    this.lastAuthUrlByContext.set(context, authUrl);
  }

  private async closeContext(context: BrowserContext): Promise<void> {
    if (this.closedContexts.has(context)) {
      return;
    }
    let timeout: NodeJS.Timeout | undefined;
    try {
      await Promise.race([
        context.close(),
        new Promise<never>((_resolve, reject) => {
          timeout = setTimeout(
            () => reject(new Error('browser-context-close-timeout')),
            5000,
          );
        }),
      ]);
    } finally {
      if (timeout) {
        clearTimeout(timeout);
      }
    }
    if (!this.closedContexts.has(context)) {
      throw new AcceptanceEvidenceViolation(
        'browser-context-close-not-observed',
      );
    }
  }

  private async drainPendingResponses(): Promise<void> {
    while (this.pending.size > 0) {
      await Promise.all(this.pending);
    }
  }

  private failBrowserRequest(request: Request): void {
    if (!safeRouteFromUrl(request.url()).startsWith('/api/')) {
      return;
    }
    this.requests.delete(request);
    this.browserRequestsFailed += 1;
    this.touchActivity();
    const expectedFailure = this.activeExpectedNetworkFailure;
    if (
      expectedFailure?.request === request &&
      expectedFailure.abortIssued &&
      !expectedFailure.consumed &&
      request.method() === expectedFailure.method &&
      request.url() === expectedFailure.exactUrl
    ) {
      expectedFailure.consumed = true;
      return;
    }
    this.recordFailure(
      request.method(),
      safeRouteFromUrl(request.url()),
      'api-request-failed',
    );
  }

  private finishBrowserRequest(request: Request): void {
    if (!safeRouteFromUrl(request.url()).startsWith('/api/')) {
      return;
    }
    if (!this.requests.delete(request)) {
      this.recordFailure(
        request.method(),
        safeRouteFromUrl(request.url()),
        'api-request-finish-without-observation',
      );
      return;
    }
    this.browserRequestsCompleted += 1;
    this.touchActivity();
    if (this.activeExpectedNetworkFailure?.request === request) {
      this.recordFailure(
        request.method(),
        safeRouteFromUrl(request.url()),
        'expected-api-network-failure-finished',
      );
    }
  }

  private async inspectResponse(
    response: ResponseSource,
    method: string,
    context?: BrowserContext,
  ): Promise<void> {
    const parsedResponseUrl = new URL(response.url());
    const route = parsedResponseUrl.pathname;
    if (
      parsedResponseUrl.origin !==
      (context ? this.browserApiOrigin : this.directApiOrigin)
    ) {
      throw new AcceptanceEvidenceViolation(
        context ? 'browser-api-origin' : 'direct-api-origin',
      );
    }
    if (!/^(?:DELETE|GET|PATCH|POST|PUT)$/.test(method)) {
      throw new AcceptanceEvidenceViolation('response-method-unknown');
    }
    const sequence = this.responseSequence;
    this.responseSequence += 1;
    const record: AuditedResponse = {
      sequence,
      method,
      route,
      status: response.status(),
      bodyBytes: 0,
      scanned: false,
    };
    this.responses.push(record);

    const responseBytes = await response.body();
    record.bodyBytes = responseBytes.length;
    if (record.bodyBytes > 20 * 1024 * 1024) {
      throw new AcceptanceEvidenceViolation('response-body-size');
    }

    const isIssuedAuthResponse =
      method === 'POST' &&
      authTokenRoutes.has(route) &&
      response.status() >= 200 &&
      response.status() < 300;
    if (isIssuedAuthResponse) {
      if (!context) {
        throw new AcceptanceEvidenceViolation('auth-response-context');
      }
      await new Promise<void>((resolve) => setTimeout(resolve, 0));
      await this.captureCurrentRefreshCookie(context, response.url(), true);
    }

    let responseText: string;
    try {
      responseText = new TextDecoder('utf-8', { fatal: true }).decode(
        responseBytes,
      );
    } catch {
      throw new AcceptanceEvidenceViolation('response-body-utf8');
    }
    await this.scanResponseBody(
      responseText,
      route,
      method,
      response.status(),
      isIssuedAuthResponse,
    );
    record.scanned = true;
  }

  private observeBrowserRequest(
    context: BrowserContext,
    request: Request,
  ): void {
    if (!safeRouteFromUrl(request.url()).startsWith('/api/')) {
      return;
    }
    if (new URL(request.url()).origin !== this.browserApiOrigin) {
      this.recordFailure(
        request.method(),
        safeRouteFromUrl(request.url()),
        'browser-api-origin',
      );
    }
    if (this.requests.has(request)) {
      this.failures.push({
        method: request.method(),
        route: safeRouteFromUrl(request.url()),
        ruleId: 'api-request-observed-twice',
        status: 0,
      });
      return;
    }
    this.browserRequestsObserved += 1;
    this.touchActivity();
    this.requestContexts.set(request, context);
    this.requests.set(request, 'pending');
    const expectedFailure = this.activeExpectedNetworkFailure;
    if (
      expectedFailure &&
      request.method() === expectedFailure.method &&
      request.url() === expectedFailure.exactUrl
    ) {
      if (expectedFailure.request && expectedFailure.request !== request) {
        this.recordFailure(
          request.method(),
          safeRouteFromUrl(request.url()),
          'expected-api-network-failure-request-cardinality',
        );
      } else {
        expectedFailure.request = request;
      }
    }
  }

  private observeBrowserResponse(
    context: BrowserContext,
    response: Response,
  ): void {
    if (!safeRouteFromUrl(response.url()).startsWith('/api/')) {
      return;
    }
    if (new URL(response.url()).origin !== this.browserApiOrigin) {
      this.recordFailure(
        response.request().method(),
        safeRouteFromUrl(response.url()),
        'browser-api-origin',
      );
    }
    this.browserResponsesObserved += 1;
    this.touchActivity();
    if (this.activeExpectedNetworkFailure?.request === response.request()) {
      this.recordFailure(
        response.request().method(),
        safeRouteFromUrl(response.url()),
        'expected-api-network-failure-response',
      );
    }
    void this.queueResponse(response, response.request().method(), context);
  }

  private observeContext(context: BrowserContext): void {
    if (this.observedContexts.has(context)) {
      return;
    }
    const handlers: ContextHandlers = {
      close: () => {
        this.closedContexts.add(context);
        this.touchActivity();
      },
      request: (request) => this.observeBrowserRequest(context, request),
      requestFailed: (request) => this.failBrowserRequest(request),
      requestFinished: (request) => this.finishBrowserRequest(request),
      response: (response) => this.observeBrowserResponse(context, response),
    };
    this.observedContexts.set(context, handlers);
    context.on('close', handlers.close);
    context.on('request', handlers.request);
    context.on('requestfailed', handlers.requestFailed);
    context.on('requestfinished', handlers.requestFinished);
    context.on('response', handlers.response);
  }

  private queueResponse(
    response: APIResponse | Response,
    method: string,
    context?: BrowserContext,
  ): Promise<void> {
    const responseObject = response as object;
    const existing = this.responseTasks.get(responseObject);
    if (existing) {
      return existing;
    }

    const normalizedMethod = method.toUpperCase();
    const task = this.inspectResponse(
      response,
      normalizedMethod,
      context,
    ).catch((error: unknown) => {
      const violation = safeViolation(error);
      this.failures.push({
        method: normalizedMethod,
        route: safeRouteFromUrl(response.url()),
        ruleId: violation.ruleId,
        status: response.status(),
      });
    });
    this.responseTasks.set(responseObject, task);
    this.pending.add(task);
    this.touchActivity();
    void task.finally(() => {
      this.pending.delete(task);
      this.touchActivity();
    });
    return task;
  }

  private recordFailure(method: string, route: string, ruleId: string): void {
    this.failures.push({ method, route, ruleId, status: 0 });
    this.touchActivity();
  }

  private async registerDynamicSensitiveValue(
    kind: DynamicSecretKind,
    value: string,
  ): Promise<void> {
    this.registerSensitiveValue(value);
    await registerDynamicSecret(kind, value);
  }

  private registerHashRepresentations(value: Buffer): void {
    this.exactSensitiveValues.add(value.toString('hex'));
    this.exactSensitiveValues.add(value.toString('hex').toUpperCase());
    this.exactSensitiveValues.add(value.toString('base64'));
    this.exactSensitiveValues.add(value.toString('base64url'));
  }

  private responseContext(
    response: APIResponse | Response,
  ): BrowserContext | undefined {
    if (!('request' in response) || typeof response.request !== 'function') {
      return undefined;
    }
    const request = (response as Response).request();
    const observedContext = this.requestContexts.get(request);
    if (observedContext) {
      return observedContext;
    }
    try {
      return request.frame().page().context();
    } catch {
      return undefined;
    }
  }

  private sanitizeResponseValue(
    value: unknown,
    path: string[],
    allowIssuedAccessToken: boolean,
    issuedAccessTokenState: IssuedAccessTokenState,
  ): unknown {
    if (Array.isArray(value)) {
      return value.map((entry, index) =>
        this.sanitizeResponseValue(
          entry,
          [...path, String(index)],
          allowIssuedAccessToken,
          issuedAccessTokenState,
        ),
      );
    }
    if (!isRecord(value)) {
      return value;
    }

    const sanitized: Record<string, unknown> = {};
    for (const [key, child] of Object.entries(value)) {
      const normalizedPath = [
        ...path.map((pathPart) => normalizeKey(pathPart)),
        normalizeKey(key),
      ];
      const normalizedKey = normalizedPath.at(-1) ?? '';
      const issuedAccessTokenPath =
        allowIssuedAccessToken &&
        normalizedPath.length === 2 &&
        normalizedPath[0] === 'data' &&
        normalizedPath[1] === 'accesstoken';
      if (issuedAccessTokenPath) {
        if (
          typeof child !== 'string' ||
          !/^eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/.test(child)
        ) {
          throw new AcceptanceEvidenceViolation('invalid-issued-access-token');
        }
        issuedAccessTokenState.count += 1;
        issuedAccessTokenState.value = child;
        sanitized[key] = '[REDACTED]';
        continue;
      }
      if (sensitiveJsonKeys.has(normalizedKey)) {
        throw new AcceptanceEvidenceViolation('sensitive-response-field');
      }
      sanitized[key] = this.sanitizeResponseValue(
        child,
        [...path, key],
        allowIssuedAccessToken,
        issuedAccessTokenState,
      );
    }
    return sanitized;
  }

  private async scanResponseBody(
    responseText: string,
    route: string,
    method: string,
    status: number,
    allowIssuedAccessToken: boolean,
  ): Promise<void> {
    if (responseText.length === 0) {
      if (allowIssuedAccessToken) {
        throw new AcceptanceEvidenceViolation('invalid-auth-response-envelope');
      }
      return;
    }

    const jsonInspection = inspectJsonBody(responseText);
    let parsedBody: unknown;
    try {
      parsedBody = JSON.parse(responseText) as unknown;
    } catch {
      this.assertSafeText(responseText);
      if (allowIssuedAccessToken) {
        throw new AcceptanceEvidenceViolation('invalid-auth-response-envelope');
      }
      return;
    }
    if (jsonInspection.parseError) {
      throw new AcceptanceEvidenceViolation('response-json-visitor-error');
    }
    if (jsonInspection.duplicateKey) {
      throw new AcceptanceEvidenceViolation('response-json-duplicate-key');
    }

    if (
      allowIssuedAccessToken &&
      (!isRecord(parsedBody) ||
        parsedBody.succeeded !== true ||
        !isRecord(parsedBody.data) ||
        method !== 'POST' ||
        !authTokenRoutes.has(route) ||
        status < 200 ||
        status >= 300)
    ) {
      throw new AcceptanceEvidenceViolation('invalid-auth-response-envelope');
    }
    const issuedAccessTokenState: IssuedAccessTokenState = { count: 0 };
    const sanitizedBody = this.sanitizeResponseValue(
      parsedBody,
      [],
      allowIssuedAccessToken,
      issuedAccessTokenState,
    );
    if (allowIssuedAccessToken && issuedAccessTokenState.count !== 1) {
      throw new AcceptanceEvidenceViolation('issued-access-token-cardinality');
    }
    let rawTextForScan = responseText;
    if (allowIssuedAccessToken) {
      const tokenSpan = jsonInspection.issuedAccessTokenSpans[0];
      if (
        jsonInspection.issuedAccessTokenSpans.length !== 1 ||
        !tokenSpan ||
        tokenSpan.value !== issuedAccessTokenState.value ||
        responseText.slice(
          tokenSpan.offset,
          tokenSpan.offset + tokenSpan.length,
        ) !== JSON.stringify(tokenSpan.value)
      ) {
        throw new AcceptanceEvidenceViolation(
          'issued-access-token-source-span',
        );
      }
      await this.registerDynamicSensitiveValue('access', tokenSpan.value);
      rawTextForScan = `${responseText.slice(0, tokenSpan.offset)}"[REDACTED]"${responseText.slice(tokenSpan.offset + tokenSpan.length)}`;
    }
    this.assertSafeText(rawTextForScan);
    this.assertSafeText(JSON.stringify(sanitizedBody));
  }

  private async scanScreenshotSurface(page: Page): Promise<void> {
    await this.settle();
    const surface = await page.locator('html').evaluate((root: HTMLElement) => {
      const formValues = [...root.querySelectorAll('input, select, textarea')]
        .map((element) => {
          const value = (element as unknown as { value?: unknown }).value;
          return typeof value === 'string' ? value : '';
        })
        .join('\n');

      return {
        fullDom: root.innerHTML,
        textAndFormValues: [root.innerText, formValues].join('\n'),
      };
    });
    this.assertSafeText(surface.fullDom, false);
    this.assertSafeText(surface.textAndFormValues);
  }

  private async settle(): Promise<void> {
    await this.drainPendingResponses();
    this.assertNoViolations();
  }

  private touchActivity(): void {
    this.activityGeneration += 1;
  }

  private async waitForBrowserRequestsToSettle(): Promise<void> {
    const deadline = Date.now() + 5000;
    let quietGeneration = -1;
    let quietSince = Date.now();
    while (Date.now() < deadline) {
      await this.drainPendingResponses();
      if (this.requests.size === 0 && this.pending.size === 0) {
        if (quietGeneration !== this.activityGeneration) {
          quietGeneration = this.activityGeneration;
          quietSince = Date.now();
        } else if (Date.now() - quietSince >= 250) {
          return;
        }
      } else {
        quietGeneration = -1;
        quietSince = Date.now();
      }
      await new Promise<void>((resolve) => setTimeout(resolve, 25));
    }
    this.recordFailure('REQUEST', '/browser', 'api-request-quiet-timeout');
  }

  private async waitForExpectedNetworkFailure(
    scope: ExpectedApiNetworkFailure,
  ): Promise<void> {
    const deadline = Date.now() + 5000;
    while (!scope.consumed && Date.now() < deadline) {
      await this.drainPendingResponses();
      await new Promise<void>((resolve) => setTimeout(resolve, 25));
    }
  }
}

export const test = base.extend<AcceptanceEvidenceFixtures>({
  acceptanceEvidence: [
    async ({ browser, page }, use, testInfo) => {
      const audit = new AcceptanceEvidenceAudit(browser, page);
      try {
        await use(audit);
      } finally {
        await audit.finalize(testInfo);
      }
    },
    { auto: true },
  ],
});

export { expect };
