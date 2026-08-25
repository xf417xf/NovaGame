import type {
  APIResponse,
  Frame,
  Locator,
  Page,
  Request,
  Response,
} from '@playwright/test';

import type { AcceptanceEvidenceAudit } from './acceptance-evidence';
import type {
  SplitRoleEffectsPackageRoot,
  SplitRoleModuleRequestFailureOutcome,
  SplitRoleModuleResource,
  SplitRoleUnclassifiedNetworkModuleResource,
  SplitRoleWorkspacePackageRoot,
} from './module-request-failure-diagnostics';
import type { SplitRoleAdministratorsCreateCommandState } from './split-role-administrators-workspace-diagnostics';

import { fileURLToPath, pathToFileURL } from 'node:url';

import { expect, test } from './acceptance-evidence';
import {
  classifySplitRoleModuleRequestFailureText,
  describeSplitRoleUnclassifiedNetworkModuleResource,
} from './module-request-failure-diagnostics';
import { resolveSplitRoleAdministratorsWorkspaceDiagnosticPriority } from './split-role-administrators-workspace-diagnostics';

interface ApiEnvelope<T> {
  data?: T;
  error?: null | { code: string; message: string };
  succeeded: boolean;
}

interface LoginDestination {
  heading: string;
  path: RegExp;
}

interface LoginSubmissionState {
  buttonCount: number;
  clickCompleted: boolean;
  enabled: boolean;
  enabledAfterClick?: boolean;
  passwordInvalid?: boolean;
  usernameInvalid?: boolean;
  visibleButtonCount: number;
}

interface BootstrapLoginEndpointState {
  requestFailed: boolean;
  requestStarted: boolean;
  status?: number;
}

interface BootstrapLoginResponseState {
  accessCodes: BootstrapLoginEndpointState;
  gameConfigDrafts: BootstrapLoginEndpointState;
  gameConfigEnvironment: BootstrapLoginEndpointState;
  gameConfigVersions: BootstrapLoginEndpointState;
  login: BootstrapLoginEndpointState;
  pageError: boolean;
  userInfo: BootstrapLoginEndpointState;
}

interface BootstrapLoginState extends SplitRolePersistedAccessState {
  applicationMounted: boolean;
  applicationMountedStateAvailable: boolean;
  gameConfigHeadingVisible: boolean;
  gameConfigHeadingStateAvailable: boolean;
  isGameConfigRoute: boolean;
  isLoginRoute: boolean;
  loginUsernameVisible: boolean;
  loginUsernameStateAvailable: boolean;
  persistedAccessStateAvailable: boolean;
  routeStateAvailable: boolean;
  startupOverlayVisible: boolean;
  startupOverlayStateAvailable: boolean;
}

interface BootstrapLoginProbe<T> {
  available: boolean;
  value: T;
}

interface BootstrapLoginRouteState {
  isGameConfigRoute: boolean;
  isLoginRoute: boolean;
}

interface AccessOnlyAuditRouteLocation extends BootstrapLoginRouteState {
  requestedPathRetained: boolean;
}

interface AccessOnlyAuditRouteFallbackState {
  applicationMounted: BootstrapLoginProbe<boolean>;
  auditLogsReadPermission: BootstrapLoginProbe<boolean>;
  fallbackSurfaceVisible: BootstrapLoginProbe<boolean>;
  persistedAccessState: BootstrapLoginProbe<SplitRolePersistedAccessState>;
  protectedHeadingCount: BootstrapLoginProbe<number>;
  protectedTableCount: BootstrapLoginProbe<number>;
  routeLocation: BootstrapLoginProbe<AccessOnlyAuditRouteLocation>;
  startupError: SplitRoleStartupError;
  startupOverlayVisible: BootstrapLoginProbe<boolean>;
  targetDocumentEverCommitted: boolean;
}

interface SplitRoleSessionsRouteLocation extends BootstrapLoginRouteState {
  requestedPathRetained: boolean;
}

interface SplitRoleSessionsRouteFallbackState {
  applicationMounted: BootstrapLoginProbe<boolean>;
  fallbackSurfaceVisible: BootstrapLoginProbe<boolean>;
  moduleFailure?: SplitRoleModuleFailure;
  persistedAccessState: BootstrapLoginProbe<SplitRolePersistedAccessState>;
  protectedHeadingCount: BootstrapLoginProbe<number>;
  protectedTableCount: BootstrapLoginProbe<number>;
  routeLocation: BootstrapLoginProbe<SplitRoleSessionsRouteLocation>;
  sessionsReadPermission: BootstrapLoginProbe<boolean>;
  startupError: SplitRoleStartupError;
  startupOverlayVisible: BootstrapLoginProbe<boolean>;
  targetDocumentEverCommitted: boolean;
}

interface BootstrapWorkspaceState {
  fallbackSurfaceVisible: boolean;
  hasAccessPermission: boolean;
  hasAdministratorsReadPermission: boolean;
}

interface SplitRoleRolesFallbackState extends SplitRolePersistedAccessState {
  applicationMounted: boolean;
  applicationRootPresent: boolean;
  documentReadyState: 'unavailable' | DocumentReadyState;
  fallbackSurfaceVisible: boolean;
  isGameConfigRoute: boolean;
  isLoginRoute: boolean;
  moduleFailure?: SplitRoleModuleFailure;
  protectedHeadingCount: number;
  protectedTableCount: number;
  requestedPathRetained: boolean;
  startupError: SplitRoleStartupError;
  startupOverlayVisible: boolean;
  targetDocumentEverCommitted: boolean;
  targetDocumentStateAvailable: boolean;
}

interface SplitRolePersistedAccessState {
  hasAccessPermission: boolean;
  hasAdministratorsReadPermission: boolean;
  hasGameConfigAccessPermission: boolean;
  hasPersistedAccessToken: boolean;
  hasRolesReadPermission: boolean;
}

interface SplitRoleAdministratorsWorkspaceState extends SplitRolePersistedAccessState {
  applicationMounted: boolean;
  createCommandState: SplitRoleAdministratorsCreateCommandState;
  fallbackSurfaceVisible: boolean;
  isAdministratorAccessRootRoute: boolean;
  isAdministratorsRoute: boolean;
  isGameConfigRoute: boolean;
  isLoginRoute: boolean;
  loadingMaskVisible: boolean;
  moduleFailure?: SplitRoleModuleFailure;
  protectedHeadingVisible: boolean;
  protectedTableVisible: boolean;
  pageErrorSource: SplitRoleWorkspacePageErrorSource;
  startupOverlayVisible: boolean;
  startupError: SplitRoleStartupError;
}

interface SplitRoleLoginResponseState {
  accessCodesStatus?: number;
  gameConfigDraftsStatus?: number;
  gameConfigEnvironmentStatus?: number;
  gameConfigVersionsStatus?: number;
  loginRequestFailed: boolean;
  loginRequestStarted: boolean;
  loginStatus?: number;
  moduleFailure?: SplitRoleModuleFailure;
  userInfoStatus?: number;
}

interface LeafOnlyDirectRouteState extends SplitRolePersistedAccessState {
  applicationMounted: boolean;
  isLoginRoute: boolean;
  loginUsernameVisible: boolean;
  moduleFailure?: SplitRoleModuleFailure;
  protectedHeadingVisible: boolean;
  protectedTableVisible: boolean;
  startupError: SplitRoleStartupError;
  startupOverlayVisible: boolean;
  targetDocumentStateAvailable: boolean;
}

interface SplitRoleLoginState extends SplitRolePersistedAccessState {
  applicationMounted: boolean;
  applicationRootState: SplitRoleInitialLoginApplicationRootState;
  applicationRootStateAvailable: boolean;
  documentReadyState: SplitRoleInitialLoginDocumentReadyState;
  documentReadyStateAvailable: boolean;
  gameConfigHeadingVisible: boolean;
  gameConfigHeadingStateAvailable: boolean;
  initialLoginContextClosed: boolean;
  initialLoginMainFrameDetached: boolean;
  initialLoginPageClosed: boolean;
  initialLoginTargetDocumentChangedDuringProbe: boolean;
  initialLoginTargetDocumentPhaseAfter: SplitRoleInitialLoginTargetDocumentPhase;
  initialLoginTargetDocumentPhaseBefore: SplitRoleInitialLoginTargetDocumentPhase;
  isGameConfigRoute: boolean;
  isLoginRoute: boolean;
  loginTitleVisible: boolean;
  loginTitleStateAvailable: boolean;
  loginUsernameFieldCount: number;
  loginUsernameFieldCountStateAvailable: boolean;
  loginUsernameVisible: boolean;
  loginUsernameVisibleStateAvailable: boolean;
  persistedAccessStateAvailable: boolean;
  routeStateAvailable: boolean;
  startupError: SplitRoleStartupError;
  startupOverlayVisible: boolean;
  startupOverlayStateAvailable: boolean;
}

interface SplitRoleInitialLoginResponseState {
  moduleFailure?: SplitRoleModuleFailure;
  pendingModuleResource?: SplitRoleModuleResource;
}

interface RolePermissionApiWaitEpoch {
  active: boolean;
}

interface RolePermissionAuthenticationWaitDiagnostics {
  accessCodes: RolePermissionApiWaitDiagnostic;
  login: RolePermissionApiWaitDiagnostic;
  userInfo: RolePermissionApiWaitDiagnostic;
}

interface RolePermissionGameConfigStartupWaitDiagnostics {
  drafts: RolePermissionApiWaitDiagnostic;
  environment: RolePermissionApiWaitDiagnostic;
  versions: RolePermissionApiWaitDiagnostic;
}

type RolePermissionApiWaitDiagnostic =
  | 'access-only-game-config-drafts'
  | 'access-only-game-config-environment'
  | 'access-only-game-config-versions'
  | 'browser-read-denial'
  | 'cold-start-access-codes'
  | 'cold-start-user-info'
  | 'leaf-only-access-codes'
  | 'leaf-only-login'
  | 'leaf-only-user-info'
  | 'refresh-session'
  | 'refreshed-access-codes'
  | 'refreshed-game-config-drafts'
  | 'refreshed-game-config-environment'
  | 'refreshed-game-config-versions'
  | 'refreshed-user-info'
  | 'restored-access-codes'
  | 'restored-game-config-drafts'
  | 'restored-game-config-environment'
  | 'restored-game-config-versions'
  | 'restored-user-info';

type RolePermissionApiWaitOutcome =
  | 'not-observed'
  | 'request-failed'
  | 'request-started-no-response'
  | 'unexpected-response';

type SplitRoleStartupError = 'dynamic-module-fetch' | 'none' | 'page-error';

type SplitRoleInitialLoginApplicationRootState =
  | 'absent'
  | 'mounted'
  | 'present-unmounted'
  | 'unavailable';

type SplitRoleInitialLoginDocumentReadyState =
  | 'unavailable'
  | DocumentReadyState;

type SplitRoleInitialLoginTargetDocumentPhase =
  | 'bare-target-active'
  | 'navigation-pending'
  | 'other-document-committed'
  | 'target-not-committed';

interface SplitRoleInitialLoginTargetDocumentObservation {
  contextClosed: boolean;
  generation: number;
  phase: SplitRoleInitialLoginTargetDocumentPhase;
}

interface SplitRoleInitialLoginPageLifecycle {
  mainFrameDetached: boolean;
  pageClosed: boolean;
}

type SplitRoleWorkspacePageErrorSource =
  | 'auth-store'
  | 'lazy-view'
  | 'main-bootstrap'
  | 'router-access'
  | 'router-guard'
  | 'unattributed';

type SplitRoleModuleFailureOutcome =
  | 'client-error'
  | 'request-network'
  | 'server-error'
  | 'unexpected-status'
  | SplitRoleModuleRequestFailureOutcome;

interface SplitRoleModuleFailure {
  effectsPackageRoot?: SplitRoleEffectsPackageRoot;
  outcome: SplitRoleModuleFailureOutcome;
  resource: SplitRoleModuleResource;
  workspacePackageRoot?: SplitRoleWorkspacePackageRoot;
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
const administratorsManagePermission =
  'Operations.AdminAccess.Administrators.Manage';
const administratorsReadPermission =
  'Operations.AdminAccess.Administrators.Read';
const administratorsResetPasswordPermission =
  'Operations.AdminAccess.Administrators.ResetPassword';
const auditLogsReadPermission = 'Operations.AdminAccess.AuditLogs.Read';
const gameConfigAccessPermission = 'Content.GameConfig.Access';
const filesystemAppSourceRoot = getViteFilesystemRoot(
  new URL('../src/', import.meta.url),
);
const filesystemConstantsPackagesRoot = getViteFilesystemRoot(
  new URL('../../../packages/constants/', import.meta.url),
);
const filesystemCorePackagesRoot = getViteFilesystemRoot(
  new URL('../../../packages/@core/', import.meta.url),
);
const filesystemEffectsPackagesRoot = getViteFilesystemRoot(
  new URL('../../../packages/effects/', import.meta.url),
);
const filesystemEffectsAccessRoot = getViteFilesystemRoot(
  new URL('../../../packages/effects/access/', import.meta.url),
);
const filesystemEffectsCommonUiRoot = getViteFilesystemRoot(
  new URL('../../../packages/effects/common-ui/', import.meta.url),
);
const filesystemEffectsHooksRoot = getViteFilesystemRoot(
  new URL('../../../packages/effects/hooks/', import.meta.url),
);
const filesystemEffectsLayoutsRoot = getViteFilesystemRoot(
  new URL('../../../packages/effects/layouts/', import.meta.url),
);
const filesystemEffectsPluginsRoot = getViteFilesystemRoot(
  new URL('../../../packages/effects/plugins/', import.meta.url),
);
const filesystemEffectsRequestRoot = getViteFilesystemRoot(
  new URL('../../../packages/effects/request/', import.meta.url),
);
const filesystemIconsPackagesRoot = getViteFilesystemRoot(
  new URL('../../../packages/icons/', import.meta.url),
);
const filesystemInternalRoot = getViteFilesystemRoot(
  new URL('../../../internal/', import.meta.url),
);
const filesystemLocalesPackagesRoot = getViteFilesystemRoot(
  new URL('../../../packages/locales/', import.meta.url),
);
const filesystemPackagesRoot = getViteFilesystemRoot(
  new URL('../../../packages/', import.meta.url),
);
const filesystemPreferencesPackagesRoot = getViteFilesystemRoot(
  new URL('../../../packages/preferences/', import.meta.url),
);
const filesystemStoresPackagesRoot = getViteFilesystemRoot(
  new URL('../../../packages/stores/', import.meta.url),
);
const filesystemStylesPackagesRoot = getViteFilesystemRoot(
  new URL('../../../packages/styles/', import.meta.url),
);
const filesystemTypesPackagesRoot = getViteFilesystemRoot(
  new URL('../../../packages/types/', import.meta.url),
);
const filesystemUtilsPackagesRoot = getViteFilesystemRoot(
  new URL('../../../packages/utils/', import.meta.url),
);
const filesystemPrefix = '/@fs';
const bootstrapLoginProbeRetryTimeoutMs = 5000;
const loginReadyTimeoutMs = 120_000;
const rolesManagePermission = 'Operations.AdminAccess.Roles.Manage';
const rolesReadPermission = 'Operations.AdminAccess.Roles.Read';
const sessionsReadPermission = 'Operations.AdminAccess.Sessions.Read';
const sessionsRevokePermission = 'Operations.AdminAccess.Sessions.Revoke';
const visualOutputDirectory = 'output/playwright/visual';
const administratorsLoginDestination: LoginDestination = {
  heading: 'Administrators',
  path: /\/admin-access\/administrators(?:[/?#].*)?$/,
};
const gameConfigLoginDestination: LoginDestination = {
  heading: 'Game configuration',
  path: /\/game-config(?:[/?#].*)?$/,
};

const allAdminAccessLeafPermissions = [
  administratorsReadPermission,
  administratorsManagePermission,
  administratorsResetPasswordPermission,
  rolesReadPermission,
  rolesManagePermission,
  sessionsReadPermission,
  sessionsRevokePermission,
  auditLogsReadPermission,
] as const;

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

function loginAction(page: Page): Locator {
  return page.getByRole('button', { exact: true, name: 'login' });
}

async function readLoginSubmissionState(
  page: Page,
): Promise<LoginSubmissionState> {
  const loginButtons = loginAction(page);
  const buttonCount = await loginButtons.count();
  const visibleButtonCount = await loginButtons.locator(':visible').count();

  return {
    buttonCount,
    clickCompleted: false,
    enabled: buttonCount === 1 && (await loginButtons.isEnabled()),
    visibleButtonCount,
  };
}

async function observeLoginSubmissionFailureState(
  page: Page,
  submissionState: LoginSubmissionState,
): Promise<void> {
  if (submissionState.buttonCount !== 1) {
    return;
  }

  submissionState.enabledAfterClick = await loginAction(page).isEnabled();
  submissionState.passwordInvalid =
    (await formField(page, 'login-password').getAttribute('aria-invalid')) ===
    'true';
  submissionState.usernameInvalid =
    (await formField(page, 'login-username').getAttribute('aria-invalid')) ===
    'true';
}

async function assertPersistedAccessStateCleared(page: Page): Promise<void> {
  await expect
    .poll(
      () =>
        page.evaluate(() => {
          for (let index = 0; index < localStorage.length; index += 1) {
            const key = localStorage.key(index);
            if (!key?.endsWith('-core-access')) {
              continue;
            }

            const serializedState = localStorage.getItem(key);
            if (!serializedState) {
              continue;
            }

            try {
              const state = JSON.parse(serializedState) as {
                accessCodes?: unknown;
                accessToken?: unknown;
              };
              if (
                state.accessToken !== null ||
                (Array.isArray(state.accessCodes) &&
                  state.accessCodes.length > 0)
              ) {
                return false;
              }
            } catch {
              return false;
            }
          }

          return true;
        }),
      { timeout: 60_000 },
    )
    .toBe(true);
}

async function assertPersistedAccessPermission(
  page: Page,
  permission: string,
  included: boolean,
): Promise<void> {
  await expect
    .poll(
      () =>
        page.evaluate(
          ({ included, permission }) => {
            for (let index = 0; index < localStorage.length; index += 1) {
              const key = localStorage.key(index);
              if (!key?.endsWith('-core-access')) {
                continue;
              }

              const serializedState = localStorage.getItem(key);
              if (!serializedState) {
                continue;
              }

              try {
                const state = JSON.parse(serializedState) as {
                  accessCodes?: unknown;
                  accessToken?: unknown;
                };
                return (
                  typeof state.accessToken === 'string' &&
                  state.accessToken.length > 0 &&
                  Array.isArray(state.accessCodes) &&
                  state.accessCodes.includes(permission) === included
                );
              } catch {
                return false;
              }
            }

            return false;
          },
          { included, permission },
        ),
      { timeout: 60_000 },
    )
    .toBe(true);
}

function tableRow(page: Page, testId: string, text: string): Locator {
  return page.getByTestId(testId).getByRole('row').filter({ hasText: text });
}

function waitForApiResponse(
  page: Page,
  method: string,
  path: RegExp | string,
  expectedStatus?: number,
  diagnostic?: RolePermissionApiWaitDiagnostic,
  epoch?: RolePermissionApiWaitEpoch,
): Promise<Response> {
  if (diagnostic) {
    if (!epoch) {
      throw new Error('Role-permission API wait diagnostics require an epoch.');
    }
    return waitForRolePermissionApiResponse(
      page,
      method,
      path,
      expectedStatus,
      diagnostic,
      epoch,
    );
  }

  return page.waitForResponse(
    (response) => {
      return (
        matchesApiRequest(response.request(), method, path) &&
        (expectedStatus === undefined || response.status() === expectedStatus)
      );
    },
    { timeout: 60_000 },
  );
}

function matchesApiRequest(
  request: Request,
  method: string,
  path: RegExp | string,
): boolean {
  const pathname = new URL(request.url()).pathname;
  return (
    request.method() === method &&
    (typeof path === 'string' ? pathname === path : path.test(pathname))
  );
}

function createRolePermissionApiWaitEpoch(): RolePermissionApiWaitEpoch {
  return { active: false };
}

function beginRolePermissionApiWaitEpoch(
  epoch: RolePermissionApiWaitEpoch,
): void {
  epoch.active = true;
}

function rolePermissionApiWaitEvidenceRule(
  diagnostic: RolePermissionApiWaitDiagnostic,
  outcome: RolePermissionApiWaitOutcome,
): string {
  return `role-permission-api-wait-${diagnostic}-${outcome}`;
}

async function waitForRolePermissionApiResponse(
  page: Page,
  method: string,
  path: RegExp | string,
  expectedStatus: number | undefined,
  diagnostic: RolePermissionApiWaitDiagnostic,
  epoch: RolePermissionApiWaitEpoch,
): Promise<Response> {
  const matchingRequests = new WeakSet<Request>();
  let requestStarted = false;
  let requestFailed = false;
  let unexpectedResponse = false;
  const observeRequest = (request: Request) => {
    if (epoch.active && matchesApiRequest(request, method, path)) {
      matchingRequests.add(request);
      requestStarted = true;
    }
  };
  const matchesEpochRequest = (request: Request) =>
    matchingRequests.has(request);
  const observeResponse = (response: Response) => {
    if (
      matchesEpochRequest(response.request()) &&
      expectedStatus !== undefined &&
      response.status() !== expectedStatus
    ) {
      unexpectedResponse = true;
    }
  };
  const observeRequestFailure = (request: Request) => {
    if (matchesEpochRequest(request)) {
      requestFailed = true;
    }
  };

  page.on('request', observeRequest);
  page.on('response', observeResponse);
  page.on('requestfailed', observeRequestFailure);
  try {
    return await page.waitForResponse(
      (response) =>
        matchesEpochRequest(response.request()) &&
        (expectedStatus === undefined || response.status() === expectedStatus),
      { timeout: 60_000 },
    );
  } catch {
    let outcome: RolePermissionApiWaitOutcome = 'not-observed';
    if (unexpectedResponse) {
      outcome = 'unexpected-response';
    } else if (requestFailed) {
      outcome = 'request-failed';
    } else if (requestStarted) {
      outcome = 'request-started-no-response';
    }
    throw new Error(
      `Acceptance evidence rule '${rolePermissionApiWaitEvidenceRule(
        diagnostic,
        outcome,
      )}' failed.`,
    );
  } finally {
    page.off('requestfailed', observeRequestFailure);
    page.off('response', observeResponse);
    page.off('request', observeRequest);
  }
}

function classifySplitRoleModuleFailureOutcome(
  status: number,
): null | SplitRoleModuleFailureOutcome {
  if (status >= 400 && status < 500) {
    return 'client-error';
  }
  if (status >= 500 && status < 600) {
    return 'server-error';
  }
  return status >= 400 ? 'unexpected-status' : null;
}

function classifySplitRoleWorkspacePageErrorSource(
  error: Error,
): SplitRoleWorkspacePageErrorSource {
  // The transient browser error is never persisted or emitted; only a fixed source category escapes.
  const errorContext = `${error.message}\n${error.stack ?? ''}`;
  if (errorContext.includes('/src/store/auth.ts')) {
    return 'auth-store';
  }
  if (errorContext.includes('/src/router/access.ts')) {
    return 'router-access';
  }
  if (errorContext.includes('/src/router/guard.ts')) {
    return 'router-guard';
  }
  if (errorContext.includes('/src/views/admin-access/')) {
    return 'lazy-view';
  }
  if (
    errorContext.includes('/src/bootstrap.ts') ||
    errorContext.includes('/src/main.ts')
  ) {
    return 'main-bootstrap';
  }
  return 'unattributed';
}

function classifySplitRoleModuleRequestFailure(
  request: Request,
): SplitRoleModuleFailureOutcome {
  return classifySplitRoleModuleRequestFailureText(
    request.failure()?.errorText,
  );
}

function getViteFilesystemRoot(url: URL): string {
  const pathname = pathToFileURL(fileURLToPath(url)).pathname.toLowerCase();
  return pathname.endsWith('/') ? pathname.slice(0, -1) : pathname;
}

function isWithinViteFilesystemRoot(
  filesystemPath: string,
  root: string,
): boolean {
  return filesystemPath === root || filesystemPath.startsWith(`${root}/`);
}

function classifySplitRoleFilesystemModuleResource(
  path: string,
): SplitRoleModuleResource {
  const filesystemPath = path.slice(filesystemPrefix.length).toLowerCase();
  if (filesystemPath.includes('/node_modules/')) {
    return 'filesystem-dependency';
  }
  if (isWithinViteFilesystemRoot(filesystemPath, filesystemAppSourceRoot)) {
    return 'filesystem-app-source';
  }
  if (isWithinViteFilesystemRoot(filesystemPath, filesystemPackagesRoot)) {
    return 'filesystem-workspace-package';
  }
  if (isWithinViteFilesystemRoot(filesystemPath, filesystemInternalRoot)) {
    return 'filesystem-workspace-internal';
  }
  return 'filesystem-unclassified';
}

function classifySplitRoleWorkspacePackageRoot(
  request: Request,
): SplitRoleWorkspacePackageRoot | undefined {
  const path = new URL(request.url()).pathname;
  if (!path.startsWith(filesystemPrefix)) {
    return undefined;
  }

  const filesystemPath = path.slice(filesystemPrefix.length).toLowerCase();
  if (
    isWithinViteFilesystemRoot(filesystemPath, filesystemConstantsPackagesRoot)
  ) {
    return 'constants';
  }
  if (isWithinViteFilesystemRoot(filesystemPath, filesystemCorePackagesRoot)) {
    return 'core';
  }
  if (
    isWithinViteFilesystemRoot(filesystemPath, filesystemEffectsPackagesRoot)
  ) {
    return 'effects';
  }
  if (isWithinViteFilesystemRoot(filesystemPath, filesystemIconsPackagesRoot)) {
    return 'icons';
  }
  if (
    isWithinViteFilesystemRoot(filesystemPath, filesystemLocalesPackagesRoot)
  ) {
    return 'locales';
  }
  if (
    isWithinViteFilesystemRoot(
      filesystemPath,
      filesystemPreferencesPackagesRoot,
    )
  ) {
    return 'preferences';
  }
  if (
    isWithinViteFilesystemRoot(filesystemPath, filesystemStoresPackagesRoot)
  ) {
    return 'stores';
  }
  if (
    isWithinViteFilesystemRoot(filesystemPath, filesystemStylesPackagesRoot)
  ) {
    return 'styles';
  }
  if (isWithinViteFilesystemRoot(filesystemPath, filesystemTypesPackagesRoot)) {
    return 'types';
  }
  if (isWithinViteFilesystemRoot(filesystemPath, filesystemUtilsPackagesRoot)) {
    return 'utils';
  }
  return 'other';
}

function classifySplitRoleEffectsPackageRoot(
  request: Request,
): SplitRoleEffectsPackageRoot | undefined {
  const path = new URL(request.url()).pathname;
  if (!path.startsWith(filesystemPrefix)) {
    return undefined;
  }

  const filesystemPath = path.slice(filesystemPrefix.length).toLowerCase();
  if (isWithinViteFilesystemRoot(filesystemPath, filesystemEffectsAccessRoot)) {
    return 'access';
  }
  if (
    isWithinViteFilesystemRoot(filesystemPath, filesystemEffectsCommonUiRoot)
  ) {
    return 'common-ui';
  }
  if (isWithinViteFilesystemRoot(filesystemPath, filesystemEffectsHooksRoot)) {
    return 'hooks';
  }
  if (
    isWithinViteFilesystemRoot(filesystemPath, filesystemEffectsLayoutsRoot)
  ) {
    return 'layouts';
  }
  if (
    isWithinViteFilesystemRoot(filesystemPath, filesystemEffectsPluginsRoot)
  ) {
    return 'plugins';
  }
  if (
    isWithinViteFilesystemRoot(filesystemPath, filesystemEffectsRequestRoot)
  ) {
    return 'request';
  }
  return 'other';
}

function classifySplitRoleModuleResource(
  request: Request,
  page: Page,
): null | SplitRoleModuleResource {
  if (
    request.resourceType() !== 'script' ||
    new URL(request.url()).origin !== new URL(page.url()).origin
  ) {
    return null;
  }

  const path = new URL(request.url()).pathname;
  if (path === '/src/bootstrap.ts') {
    return 'bootstrap';
  }
  if (path.startsWith('/node_modules/.vite/deps/')) {
    return 'optimized-dependency';
  }
  if (path.startsWith('/src/views/')) {
    return 'source-view';
  }
  if (path === '/@react-refresh' || path.startsWith('/@vite/')) {
    return 'vite-client';
  }
  if (path.startsWith('/@id/')) {
    return 'vite-virtual';
  }
  if (path.startsWith('/node_modules/')) {
    return 'direct-node-module';
  }
  if (path.startsWith('/src/')) {
    return 'source-outside-views';
  }
  if (path.startsWith('/@fs/')) {
    return classifySplitRoleFilesystemModuleResource(path);
  }
  return 'unknown';
}

function createSplitRoleModuleFailure(
  request: Request,
  outcome: SplitRoleModuleFailureOutcome,
  resource: SplitRoleModuleResource,
): SplitRoleModuleFailure {
  const workspacePackageRoot =
    resource === 'filesystem-workspace-package'
      ? classifySplitRoleWorkspacePackageRoot(request)
      : undefined;
  const effectsPackageRoot =
    workspacePackageRoot === 'effects'
      ? classifySplitRoleEffectsPackageRoot(request)
      : undefined;
  return {
    ...(effectsPackageRoot ? { effectsPackageRoot } : {}),
    outcome,
    resource,
    ...(workspacePackageRoot ? { workspacePackageRoot } : {}),
  };
}

function recordSplitRoleModuleFailure(
  responseState: Pick<SplitRoleLoginResponseState, 'moduleFailure'>,
  failure: SplitRoleModuleFailure,
): void {
  if (!responseState.moduleFailure) {
    responseState.moduleFailure = failure;
  }
}

function throwSplitRoleAdministratorsWorkspaceUnclassifiedNetworkModuleFailure(
  resource: SplitRoleUnclassifiedNetworkModuleResource,
): never {
  switch (resource) {
    case 'bootstrap': {
      throw new Error(
        'Split-role Administrators workspace bootstrap script module request had an unclassified network failure.',
      );
    }
    case 'direct node module': {
      throw new Error(
        'Split-role Administrators workspace direct node module script request had an unclassified network failure.',
      );
    }
    case 'filesystem application source': {
      throw new Error(
        'Split-role Administrators workspace filesystem application source script module request had an unclassified network failure.',
      );
    }
    case 'filesystem dependency': {
      throw new Error(
        'Split-role Administrators workspace filesystem dependency script module request had an unclassified network failure.',
      );
    }
    case 'filesystem unclassified': {
      throw new Error(
        'Split-role Administrators workspace filesystem unclassified script module request had an unclassified network failure.',
      );
    }
    case 'filesystem workspace internal': {
      throw new Error(
        'Split-role Administrators workspace filesystem workspace internal script module request had an unclassified network failure.',
      );
    }
    case 'filesystem workspace package': {
      throw new Error(
        'Split-role Administrators workspace filesystem workspace package script module request had an unclassified network failure.',
      );
    }
    case 'optimized dependency': {
      throw new Error(
        'Split-role Administrators workspace optimized dependency script module request had an unclassified network failure.',
      );
    }
    case 'source outside views': {
      throw new Error(
        'Split-role Administrators workspace source outside views script module request had an unclassified network failure.',
      );
    }
    case 'source view': {
      throw new Error(
        'Split-role Administrators workspace source view script module request had an unclassified network failure.',
      );
    }
    case 'unknown': {
      throw new Error(
        'Split-role Administrators workspace unknown script module request had an unclassified network failure.',
      );
    }
    case 'vite client': {
      throw new Error(
        'Split-role Administrators workspace Vite client script module request had an unclassified network failure.',
      );
    }
    case 'vite virtual': {
      throw new Error(
        'Split-role Administrators workspace Vite virtual script module request had an unclassified network failure.',
      );
    }
    case 'workspace constants': {
      throw new Error(
        'Split-role Administrators workspace constants package script module request had an unclassified network failure.',
      );
    }
    case 'workspace core': {
      throw new Error(
        'Split-role Administrators workspace core package script module request had an unclassified network failure.',
      );
    }
    case 'workspace effects': {
      throw new Error(
        'Split-role Administrators workspace effects package script module request had an unclassified network failure.',
      );
    }
    case 'workspace effects access': {
      throw new Error(
        'Split-role Administrators workspace effects access package script module request had an unclassified network failure.',
      );
    }
    case 'workspace effects common UI': {
      throw new Error(
        'Split-role Administrators workspace effects common UI package script module request had an unclassified network failure.',
      );
    }
    case 'workspace effects hooks': {
      throw new Error(
        'Split-role Administrators workspace effects hooks package script module request had an unclassified network failure.',
      );
    }
    case 'workspace effects layouts': {
      throw new Error(
        'Split-role Administrators workspace effects layouts package script module request had an unclassified network failure.',
      );
    }
    case 'workspace effects other': {
      throw new Error(
        'Split-role Administrators workspace effects other package script module request had an unclassified network failure.',
      );
    }
    case 'workspace effects plugins': {
      throw new Error(
        'Split-role Administrators workspace effects plugins package script module request had an unclassified network failure.',
      );
    }
    case 'workspace effects request': {
      throw new Error(
        'Split-role Administrators workspace effects request package script module request had an unclassified network failure.',
      );
    }
    case 'workspace icons': {
      throw new Error(
        'Split-role Administrators workspace icons package script module request had an unclassified network failure.',
      );
    }
    case 'workspace locales': {
      throw new Error(
        'Split-role Administrators workspace locales package script module request had an unclassified network failure.',
      );
    }
    case 'workspace other': {
      throw new Error(
        'Split-role Administrators workspace other package script module request had an unclassified network failure.',
      );
    }
    case 'workspace preferences': {
      throw new Error(
        'Split-role Administrators workspace preferences package script module request had an unclassified network failure.',
      );
    }
    case 'workspace stores': {
      throw new Error(
        'Split-role Administrators workspace stores package script module request had an unclassified network failure.',
      );
    }
    case 'workspace styles': {
      throw new Error(
        'Split-role Administrators workspace styles package script module request had an unclassified network failure.',
      );
    }
    case 'workspace types': {
      throw new Error(
        'Split-role Administrators workspace types package script module request had an unclassified network failure.',
      );
    }
    case 'workspace utils': {
      throw new Error(
        'Split-role Administrators workspace utils package script module request had an unclassified network failure.',
      );
    }
  }
}

function throwSplitRoleAdministratorsWorkspaceModuleFailure(
  moduleFailure: SplitRoleModuleFailure,
): never {
  switch (moduleFailure.outcome) {
    case 'request-aborted': {
      throw new Error(
        'Split-role Administrators workspace script module request was aborted.',
      );
    }
    case 'request-connection': {
      throw new Error(
        'Split-role Administrators workspace script module request failed to connect.',
      );
    }
    case 'request-failed': {
      throw new Error(
        'Split-role Administrators workspace script module request failed.',
      );
    }
    case 'request-missing-details': {
      throw new Error(
        'Split-role Administrators workspace script module request failure had no details.',
      );
    }
    case 'request-name-resolution': {
      throw new Error(
        'Split-role Administrators workspace script module request had a name-resolution failure.',
      );
    }
    case 'request-network-unclassified': {
      const resource = describeSplitRoleUnclassifiedNetworkModuleResource(
        moduleFailure.resource,
        moduleFailure.workspacePackageRoot,
        moduleFailure.effectsPackageRoot,
      );
      return throwSplitRoleAdministratorsWorkspaceUnclassifiedNetworkModuleFailure(
        resource,
      );
    }
    case 'request-network': {
      switch (moduleFailure.workspacePackageRoot) {
        case 'constants': {
          throw new Error(
            'Split-role Administrators workspace constants workspace package script module request had a network failure.',
          );
        }
        case 'core': {
          throw new Error(
            'Split-role Administrators workspace core workspace package script module request had a network failure.',
          );
        }
        case 'effects': {
          switch (moduleFailure.effectsPackageRoot) {
            case 'access': {
              throw new Error(
                'Split-role Administrators workspace effects access package script module request had a network failure.',
              );
            }
            case 'common-ui': {
              throw new Error(
                'Split-role Administrators workspace effects common-ui package script module request had a network failure.',
              );
            }
            case 'hooks': {
              throw new Error(
                'Split-role Administrators workspace effects hooks package script module request had a network failure.',
              );
            }
            case 'layouts': {
              throw new Error(
                'Split-role Administrators workspace effects layouts package script module request had a network failure.',
              );
            }
            case 'other': {
              throw new Error(
                'Split-role Administrators workspace other effects package script module request had a network failure.',
              );
            }
            case 'plugins': {
              throw new Error(
                'Split-role Administrators workspace effects plugins package script module request had a network failure.',
              );
            }
            case 'request': {
              throw new Error(
                'Split-role Administrators workspace effects request package script module request had a network failure.',
              );
            }
          }
          throw new Error(
            'Split-role Administrators workspace effects workspace package script module request had a network failure.',
          );
        }
        case 'icons': {
          throw new Error(
            'Split-role Administrators workspace icons workspace package script module request had a network failure.',
          );
        }
        case 'locales': {
          throw new Error(
            'Split-role Administrators workspace locales workspace package script module request had a network failure.',
          );
        }
        case 'other': {
          throw new Error(
            'Split-role Administrators workspace other workspace package script module request had a network failure.',
          );
        }
        case 'preferences': {
          throw new Error(
            'Split-role Administrators workspace preferences workspace package script module request had a network failure.',
          );
        }
        case 'stores': {
          throw new Error(
            'Split-role Administrators workspace stores workspace package script module request had a network failure.',
          );
        }
        case 'styles': {
          throw new Error(
            'Split-role Administrators workspace styles workspace package script module request had a network failure.',
          );
        }
        case 'types': {
          throw new Error(
            'Split-role Administrators workspace types workspace package script module request had a network failure.',
          );
        }
        case 'utils': {
          throw new Error(
            'Split-role Administrators workspace utils workspace package script module request had a network failure.',
          );
        }
      }
      throw new Error(
        'Split-role Administrators workspace script module request had a network failure.',
      );
    }
    case 'request-other': {
      throw new Error(
        'Split-role Administrators workspace script module request had an unrecognized failure.',
      );
    }
    case 'request-policy': {
      throw new Error(
        'Split-role Administrators workspace script module request was blocked by browser policy.',
      );
    }
    case 'request-protocol': {
      throw new Error(
        'Split-role Administrators workspace script module request had a protocol failure.',
      );
    }
    case 'request-proxy': {
      throw new Error(
        'Split-role Administrators workspace script module request had a proxy failure.',
      );
    }
    case 'request-reachability': {
      throw new Error(
        'Split-role Administrators workspace script module request had a reachability failure.',
      );
    }
    case 'request-response': {
      throw new Error(
        'Split-role Administrators workspace script module request had a response failure.',
      );
    }
    case 'request-timeout': {
      throw new Error(
        'Split-role Administrators workspace script module request timed out.',
      );
    }
    case 'request-tls': {
      throw new Error(
        'Split-role Administrators workspace script module request had a transport-security failure.',
      );
    }
  }

  switch (moduleFailure.resource) {
    case 'bootstrap': {
      switch (moduleFailure.outcome) {
        case 'client-error': {
          throw new Error(
            'Split-role Administrators workspace bootstrap module returned a client error.',
          );
        }
        case 'request-failed': {
          throw new Error(
            'Split-role Administrators workspace bootstrap module request failed.',
          );
        }
        case 'server-error': {
          throw new Error(
            'Split-role Administrators workspace bootstrap module returned a server error.',
          );
        }
        case 'unexpected-status': {
          throw new Error(
            'Split-role Administrators workspace bootstrap module returned an unexpected status.',
          );
        }
      }
      break;
    }
    case 'direct-node-module': {
      switch (moduleFailure.outcome) {
        case 'client-error': {
          throw new Error(
            'Split-role Administrators workspace direct node module returned a client error.',
          );
        }
        case 'request-failed': {
          throw new Error(
            'Split-role Administrators workspace direct node module request failed.',
          );
        }
        case 'server-error': {
          throw new Error(
            'Split-role Administrators workspace direct node module returned a server error.',
          );
        }
        case 'unexpected-status': {
          throw new Error(
            'Split-role Administrators workspace direct node module returned an unexpected status.',
          );
        }
      }
      break;
    }
    case 'filesystem-app-source': {
      switch (moduleFailure.outcome) {
        case 'client-error': {
          throw new Error(
            'Split-role Administrators workspace filesystem app source returned a client error.',
          );
        }
        case 'request-failed': {
          throw new Error(
            'Split-role Administrators workspace filesystem app source request failed.',
          );
        }
        case 'server-error': {
          throw new Error(
            'Split-role Administrators workspace filesystem app source returned a server error.',
          );
        }
        case 'unexpected-status': {
          throw new Error(
            'Split-role Administrators workspace filesystem app source returned an unexpected status.',
          );
        }
      }
      break;
    }
    case 'filesystem-dependency': {
      switch (moduleFailure.outcome) {
        case 'client-error': {
          throw new Error(
            'Split-role Administrators workspace filesystem dependency returned a client error.',
          );
        }
        case 'request-failed': {
          throw new Error(
            'Split-role Administrators workspace filesystem dependency request failed.',
          );
        }
        case 'server-error': {
          throw new Error(
            'Split-role Administrators workspace filesystem dependency returned a server error.',
          );
        }
        case 'unexpected-status': {
          throw new Error(
            'Split-role Administrators workspace filesystem dependency returned an unexpected status.',
          );
        }
      }
      break;
    }
    case 'filesystem-unclassified': {
      switch (moduleFailure.outcome) {
        case 'client-error': {
          throw new Error(
            'Split-role Administrators workspace unclassified filesystem module returned a client error.',
          );
        }
        case 'request-failed': {
          throw new Error(
            'Split-role Administrators workspace unclassified filesystem module request failed.',
          );
        }
        case 'server-error': {
          throw new Error(
            'Split-role Administrators workspace unclassified filesystem module returned a server error.',
          );
        }
        case 'unexpected-status': {
          throw new Error(
            'Split-role Administrators workspace unclassified filesystem module returned an unexpected status.',
          );
        }
      }
      break;
    }
    case 'filesystem-workspace-internal': {
      switch (moduleFailure.outcome) {
        case 'client-error': {
          throw new Error(
            'Split-role Administrators workspace filesystem workspace internal module returned a client error.',
          );
        }
        case 'request-failed': {
          throw new Error(
            'Split-role Administrators workspace filesystem workspace internal module request failed.',
          );
        }
        case 'server-error': {
          throw new Error(
            'Split-role Administrators workspace filesystem workspace internal module returned a server error.',
          );
        }
        case 'unexpected-status': {
          throw new Error(
            'Split-role Administrators workspace filesystem workspace internal module returned an unexpected status.',
          );
        }
      }
      break;
    }
    case 'filesystem-workspace-package': {
      switch (moduleFailure.outcome) {
        case 'client-error': {
          throw new Error(
            'Split-role Administrators workspace filesystem workspace package returned a client error.',
          );
        }
        case 'request-failed': {
          throw new Error(
            'Split-role Administrators workspace filesystem workspace package request failed.',
          );
        }
        case 'server-error': {
          throw new Error(
            'Split-role Administrators workspace filesystem workspace package returned a server error.',
          );
        }
        case 'unexpected-status': {
          throw new Error(
            'Split-role Administrators workspace filesystem workspace package returned an unexpected status.',
          );
        }
      }
      break;
    }
    case 'optimized-dependency': {
      switch (moduleFailure.outcome) {
        case 'client-error': {
          throw new Error(
            'Split-role Administrators workspace optimized dependency returned a client error.',
          );
        }
        case 'request-failed': {
          throw new Error(
            'Split-role Administrators workspace optimized dependency request failed.',
          );
        }
        case 'server-error': {
          throw new Error(
            'Split-role Administrators workspace optimized dependency returned a server error.',
          );
        }
        case 'unexpected-status': {
          throw new Error(
            'Split-role Administrators workspace optimized dependency returned an unexpected status.',
          );
        }
      }
      break;
    }
    case 'source-outside-views': {
      switch (moduleFailure.outcome) {
        case 'client-error': {
          throw new Error(
            'Split-role Administrators workspace source outside views returned a client error.',
          );
        }
        case 'request-failed': {
          throw new Error(
            'Split-role Administrators workspace source outside views request failed.',
          );
        }
        case 'server-error': {
          throw new Error(
            'Split-role Administrators workspace source outside views returned a server error.',
          );
        }
        case 'unexpected-status': {
          throw new Error(
            'Split-role Administrators workspace source outside views returned an unexpected status.',
          );
        }
      }
      break;
    }
    case 'source-view': {
      switch (moduleFailure.outcome) {
        case 'client-error': {
          throw new Error(
            'Split-role Administrators workspace source view returned a client error.',
          );
        }
        case 'request-failed': {
          throw new Error(
            'Split-role Administrators workspace source view request failed.',
          );
        }
        case 'server-error': {
          throw new Error(
            'Split-role Administrators workspace source view returned a server error.',
          );
        }
        case 'unexpected-status': {
          throw new Error(
            'Split-role Administrators workspace source view returned an unexpected status.',
          );
        }
      }
      break;
    }
    case 'unknown': {
      switch (moduleFailure.outcome) {
        case 'client-error': {
          throw new Error(
            'Split-role Administrators workspace unknown module returned a client error.',
          );
        }
        case 'request-failed': {
          throw new Error(
            'Split-role Administrators workspace unknown module request failed.',
          );
        }
        case 'server-error': {
          throw new Error(
            'Split-role Administrators workspace unknown module returned a server error.',
          );
        }
        case 'unexpected-status': {
          throw new Error(
            'Split-role Administrators workspace unknown module returned an unexpected status.',
          );
        }
      }
      break;
    }
    case 'vite-client': {
      switch (moduleFailure.outcome) {
        case 'client-error': {
          throw new Error(
            'Split-role Administrators workspace Vite client module returned a client error.',
          );
        }
        case 'request-failed': {
          throw new Error(
            'Split-role Administrators workspace Vite client module request failed.',
          );
        }
        case 'server-error': {
          throw new Error(
            'Split-role Administrators workspace Vite client module returned a server error.',
          );
        }
        case 'unexpected-status': {
          throw new Error(
            'Split-role Administrators workspace Vite client module returned an unexpected status.',
          );
        }
      }
      break;
    }
    case 'vite-virtual': {
      switch (moduleFailure.outcome) {
        case 'client-error': {
          throw new Error(
            'Split-role Administrators workspace Vite virtual module returned a client error.',
          );
        }
        case 'request-failed': {
          throw new Error(
            'Split-role Administrators workspace Vite virtual module request failed.',
          );
        }
        case 'server-error': {
          throw new Error(
            'Split-role Administrators workspace Vite virtual module returned a server error.',
          );
        }
        case 'unexpected-status': {
          throw new Error(
            'Split-role Administrators workspace Vite virtual module returned an unexpected status.',
          );
        }
      }
      break;
    }
  }

  throw new Error(
    'Split-role Administrators workspace module failure was invalid.',
  );
}

function throwSplitRoleRouteFallbackModuleFailure(
  routeLabel: 'Roles' | 'Sessions',
  moduleFailure: SplitRoleModuleFailure,
): never {
  switch (moduleFailure.outcome) {
    case 'client-error': {
      throw new Error(
        `Split-role ${routeLabel} route target script module returned a client error.`,
      );
    }
    case 'request-aborted': {
      throw new Error(
        `Split-role ${routeLabel} route target script module request was aborted.`,
      );
    }
    case 'request-connection': {
      throw new Error(
        `Split-role ${routeLabel} route target script module request failed to connect.`,
      );
    }
    case 'request-failed': {
      throw new Error(
        `Split-role ${routeLabel} route target script module request failed.`,
      );
    }
    case 'request-missing-details': {
      throw new Error(
        `Split-role ${routeLabel} route target script module request failure had no details.`,
      );
    }
    case 'request-name-resolution': {
      throw new Error(
        `Split-role ${routeLabel} route target script module request had a name-resolution failure.`,
      );
    }
    case 'request-network-unclassified': {
      throw new Error(
        `Split-role ${routeLabel} route target script module request had an unclassified network failure.`,
      );
    }
    case 'request-network': {
      return throwSplitRoleRouteFallbackNetworkModuleFailure(
        routeLabel,
        moduleFailure,
      );
    }
    case 'request-other': {
      throw new Error(
        `Split-role ${routeLabel} route target script module request had an unrecognized failure.`,
      );
    }
    case 'request-policy': {
      throw new Error(
        `Split-role ${routeLabel} route target script module request was blocked by browser policy.`,
      );
    }
    case 'request-protocol': {
      throw new Error(
        `Split-role ${routeLabel} route target script module request had a protocol failure.`,
      );
    }
    case 'request-proxy': {
      throw new Error(
        `Split-role ${routeLabel} route target script module request had a proxy failure.`,
      );
    }
    case 'request-reachability': {
      throw new Error(
        `Split-role ${routeLabel} route target script module request had a reachability failure.`,
      );
    }
    case 'request-response': {
      throw new Error(
        `Split-role ${routeLabel} route target script module request had a response failure.`,
      );
    }
    case 'request-timeout': {
      throw new Error(
        `Split-role ${routeLabel} route target script module request timed out.`,
      );
    }
    case 'request-tls': {
      throw new Error(
        `Split-role ${routeLabel} route target script module request had a transport-security failure.`,
      );
    }
    case 'server-error': {
      throw new Error(
        `Split-role ${routeLabel} route target script module returned a server error.`,
      );
    }
    case 'unexpected-status': {
      throw new Error(
        `Split-role ${routeLabel} route target script module returned an unexpected status.`,
      );
    }
  }
}

function throwSplitRoleRouteFallbackNetworkModuleFailure(
  routeLabel: 'Roles' | 'Sessions',
  moduleFailure: SplitRoleModuleFailure,
): never {
  switch (moduleFailure.resource) {
    case 'bootstrap': {
      throw new Error(
        `Split-role ${routeLabel} route target bootstrap module request had a network failure.`,
      );
    }
    case 'direct-node-module': {
      throw new Error(
        `Split-role ${routeLabel} route target direct node module request had a network failure.`,
      );
    }
    case 'filesystem-app-source': {
      throw new Error(
        `Split-role ${routeLabel} route target filesystem app source module request had a network failure.`,
      );
    }
    case 'filesystem-dependency': {
      throw new Error(
        `Split-role ${routeLabel} route target filesystem dependency module request had a network failure.`,
      );
    }
    case 'filesystem-unclassified': {
      throw new Error(
        `Split-role ${routeLabel} route target unclassified filesystem module request had a network failure.`,
      );
    }
    case 'filesystem-workspace-internal': {
      throw new Error(
        `Split-role ${routeLabel} route target filesystem workspace internal module request had a network failure.`,
      );
    }
    case 'filesystem-workspace-package': {
      return throwSplitRoleRouteFallbackWorkspacePackageNetworkModuleFailure(
        routeLabel,
        moduleFailure,
      );
    }
    case 'optimized-dependency': {
      throw new Error(
        `Split-role ${routeLabel} route target optimized dependency module request had a network failure.`,
      );
    }
    case 'source-outside-views': {
      throw new Error(
        `Split-role ${routeLabel} route target source module request had a network failure.`,
      );
    }
    case 'source-view': {
      throw new Error(
        `Split-role ${routeLabel} route target source view module request had a network failure.`,
      );
    }
    case 'unknown': {
      throw new Error(
        `Split-role ${routeLabel} route target unknown module request had a network failure.`,
      );
    }
    case 'vite-client': {
      throw new Error(
        `Split-role ${routeLabel} route target Vite client module request had a network failure.`,
      );
    }
    case 'vite-virtual': {
      throw new Error(
        `Split-role ${routeLabel} route target Vite virtual module request had a network failure.`,
      );
    }
  }
}

function throwSplitRoleRouteFallbackWorkspacePackageNetworkModuleFailure(
  routeLabel: 'Roles' | 'Sessions',
  moduleFailure: SplitRoleModuleFailure,
): never {
  switch (moduleFailure.workspacePackageRoot) {
    case 'constants': {
      throw new Error(
        `Split-role ${routeLabel} route target constants workspace package module request had a network failure.`,
      );
    }
    case 'core': {
      throw new Error(
        `Split-role ${routeLabel} route target core workspace package module request had a network failure.`,
      );
    }
    case 'effects': {
      return throwSplitRoleRouteFallbackEffectsPackageNetworkModuleFailure(
        routeLabel,
        moduleFailure,
      );
    }
    case 'icons': {
      throw new Error(
        `Split-role ${routeLabel} route target icons workspace package module request had a network failure.`,
      );
    }
    case 'locales': {
      throw new Error(
        `Split-role ${routeLabel} route target locales workspace package module request had a network failure.`,
      );
    }
    case 'other': {
      throw new Error(
        `Split-role ${routeLabel} route target other workspace package module request had a network failure.`,
      );
    }
    case 'preferences': {
      throw new Error(
        `Split-role ${routeLabel} route target preferences workspace package module request had a network failure.`,
      );
    }
    case 'stores': {
      throw new Error(
        `Split-role ${routeLabel} route target stores workspace package module request had a network failure.`,
      );
    }
    case 'styles': {
      throw new Error(
        `Split-role ${routeLabel} route target styles workspace package module request had a network failure.`,
      );
    }
    case 'types': {
      throw new Error(
        `Split-role ${routeLabel} route target types workspace package module request had a network failure.`,
      );
    }
    case 'utils': {
      throw new Error(
        `Split-role ${routeLabel} route target utils workspace package module request had a network failure.`,
      );
    }
    case undefined: {
      break;
    }
  }

  throw new Error(
    `Split-role ${routeLabel} route target filesystem workspace package module request had a network failure.`,
  );
}

function throwSplitRoleRouteFallbackEffectsPackageNetworkModuleFailure(
  routeLabel: 'Roles' | 'Sessions',
  moduleFailure: SplitRoleModuleFailure,
): never {
  switch (moduleFailure.effectsPackageRoot) {
    case 'access': {
      throw new Error(
        `Split-role ${routeLabel} route target effects access workspace package module request had a network failure.`,
      );
    }
    case 'common-ui': {
      throw new Error(
        `Split-role ${routeLabel} route target effects common-ui workspace package module request had a network failure.`,
      );
    }
    case 'hooks': {
      throw new Error(
        `Split-role ${routeLabel} route target effects hooks workspace package module request had a network failure.`,
      );
    }
    case 'layouts': {
      throw new Error(
        `Split-role ${routeLabel} route target effects layouts workspace package module request had a network failure.`,
      );
    }
    case 'other': {
      throw new Error(
        `Split-role ${routeLabel} route target other effects workspace package module request had a network failure.`,
      );
    }
    case 'plugins': {
      throw new Error(
        `Split-role ${routeLabel} route target effects plugins workspace package module request had a network failure.`,
      );
    }
    case 'request': {
      throw new Error(
        `Split-role ${routeLabel} route target effects request workspace package module request had a network failure.`,
      );
    }
    case undefined: {
      break;
    }
  }

  throw new Error(
    `Split-role ${routeLabel} route target effects workspace package module request had a network failure.`,
  );
}

function isSplitRoleLoginRequest(request: Request): boolean {
  return (
    request.method() === 'POST' &&
    new URL(request.url()).pathname === '/api/auth/login'
  );
}

async function waitForGameConfigStartupResponses(
  page: Page,
  diagnostics?: RolePermissionGameConfigStartupWaitDiagnostics,
  epoch?: RolePermissionApiWaitEpoch,
): Promise<Response[]> {
  const [drafts, versions, environment] = await Promise.allSettled([
    waitForApiResponse(
      page,
      'GET',
      '/api/game-config/drafts',
      undefined,
      diagnostics?.drafts,
      epoch,
    ),
    waitForApiResponse(
      page,
      'GET',
      '/api/game-config/versions',
      undefined,
      diagnostics?.versions,
      epoch,
    ),
    waitForApiResponse(
      page,
      'GET',
      '/api/game-config/environments/development',
      undefined,
      diagnostics?.environment,
      epoch,
    ),
  ]);

  if (drafts.status === 'rejected') {
    throw drafts.reason;
  }
  if (versions.status === 'rejected') {
    throw versions.reason;
  }
  if (environment.status === 'rejected') {
    throw environment.reason;
  }

  return [drafts.value, versions.value, environment.value];
}

async function auditGameConfigStartupResponses(
  responses: Promise<Response[]>,
  acceptanceEvidence: AcceptanceEvidenceAudit,
): Promise<void> {
  for (const response of await responses) {
    await acceptanceEvidence.auditResponse(response, 'GET');
  }
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

async function assertAuthorizationFailure(
  response: APIResponse | Response,
  acceptanceEvidence: AcceptanceEvidenceAudit,
  expectedStatus: 401 | 403,
  method: string,
): Promise<void> {
  await acceptanceEvidence.auditResponse(response, method);
  expect(response.status()).toBe(expectedStatus);

  const body = JSON.parse(await response.text()) as ApiEnvelope<never>;
  expect(body.succeeded).toBe(false);
  expect(body.error?.code).toBe(
    expectedStatus === 401
      ? 'NovaGame.AdminAccess.Unauthorized'
      : 'NovaGame.AdminAccess.Forbidden',
  );
  if (expectedStatus === 401) {
    expect(response.headers()['www-authenticate']).toBe('Bearer');
  }
}

async function assertAdministratorNotFoundFailure(
  response: APIResponse | Response,
  acceptanceEvidence: AcceptanceEvidenceAudit,
  method: string,
): Promise<void> {
  await acceptanceEvidence.auditResponse(response, method);
  expect(response.status()).toBe(404);

  const body = JSON.parse(await response.text()) as ApiEnvelope<never>;
  expect(body.succeeded).toBe(false);
  expect(body.error?.code).toBe('NovaGame.AdminAccess.AdministratorNotFound');
}

async function login(
  page: Page,
  userName: string,
  password: string,
  acceptanceEvidence: AcceptanceEvidenceAudit,
  assertDefaultRefreshCookie = true,
  destination: LoginDestination = gameConfigLoginDestination,
  preserveCurrentLoginRedirect = false,
  onLoginSubmissionState?: (state: LoginSubmissionState) => void,
): Promise<string> {
  acceptanceEvidence.registerSensitiveValue(password);
  const currentUrl = new URL(page.url());
  if (
    currentUrl.pathname !== '/auth/login' ||
    (!preserveCurrentLoginRedirect &&
      (currentUrl.hash.length > 0 || currentUrl.search.length > 0))
  ) {
    await page.goto('/auth/login', {
      timeout: loginReadyTimeoutMs,
      waitUntil: 'commit',
    });
  }
  await expect(formField(page, 'login-username')).toBeVisible({
    timeout: loginReadyTimeoutMs,
  });
  await formField(page, 'login-username').fill(userName);
  await formField(page, 'login-password').fill(password);
  const loginButton = loginAction(page);
  const submissionObserver = onLoginSubmissionState;
  const submissionState = submissionObserver
    ? await readLoginSubmissionState(page)
    : undefined;
  if (submissionState && submissionObserver) {
    submissionObserver(submissionState);
  }
  const loginResponse = waitForApiResponse(page, 'POST', '/api/auth/login');
  const gameConfigStartupResponses =
    destination === gameConfigLoginDestination
      ? waitForGameConfigStartupResponses(page)
      : undefined;
  await loginButton.click();
  if (submissionState) {
    submissionState.clickCompleted = true;
  }
  try {
    const session = await readApiData<AccessTokenResponse>(
      await loginResponse,
      acceptanceEvidence,
      'POST',
    );
    await expect(page).toHaveURL(destination.path, {
      timeout: 60_000,
    });
    await expect(
      page.getByRole('heading', { name: destination.heading }),
    ).toBeVisible({
      timeout: 60_000,
    });
    if (gameConfigStartupResponses) {
      await auditGameConfigStartupResponses(
        gameConfigStartupResponses,
        acceptanceEvidence,
      );
    }
    if (assertDefaultRefreshCookie) {
      await acceptanceEvidence.assertCurrentRefreshCookie();
    }
    return session.accessToken;
  } catch (error) {
    if (submissionState) {
      await observeLoginSubmissionFailureState(page, submissionState);
    }
    throw error;
  }
}

async function authenticateWithoutAdministratorAccess(
  page: Page,
  userName: string,
  password: string,
  acceptanceEvidence: AcceptanceEvidenceAudit,
  diagnostics?: RolePermissionAuthenticationWaitDiagnostics,
): Promise<string> {
  acceptanceEvidence.registerSensitiveValue(password);
  if (new URL(page.url()).pathname !== '/auth/login') {
    await page.goto('/auth/login', {
      timeout: loginReadyTimeoutMs,
      waitUntil: 'commit',
    });
  }
  await expect(formField(page, 'login-username')).toBeVisible({
    timeout: loginReadyTimeoutMs,
  });
  await formField(page, 'login-username').fill(userName);
  await formField(page, 'login-password').fill(password);
  const authenticationEpoch = diagnostics
    ? createRolePermissionApiWaitEpoch()
    : undefined;
  const loginResponse = waitForApiResponse(
    page,
    'POST',
    '/api/auth/login',
    undefined,
    diagnostics?.login,
    authenticationEpoch,
  );
  const permissionCodesResponse = waitForApiResponse(
    page,
    'GET',
    '/api/auth/codes',
    403,
    diagnostics?.accessCodes,
    authenticationEpoch,
  );
  const userInfoResponse = waitForApiResponse(
    page,
    'GET',
    '/api/user/info',
    403,
    diagnostics?.userInfo,
    authenticationEpoch,
  );
  if (authenticationEpoch) {
    beginRolePermissionApiWaitEpoch(authenticationEpoch);
  }
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
  await assertPersistedAccessStateCleared(page);
  return session.accessToken;
}

async function assertManagementRouteFallsBack(
  page: Page,
  path: string,
  heading: string,
  tableTestId: string,
): Promise<void> {
  if (new URL(page.url()).pathname !== path) {
    await page.goto(path, { timeout: 60_000, waitUntil: 'commit' });
  }
  await expect(page.locator('svg[viewBox="0 0 860 571"]')).toBeVisible({
    timeout: 60_000,
  });
  await expect(page.getByTestId(tableTestId)).toHaveCount(0, {
    timeout: 60_000,
  });
  await expect(page.getByRole('heading', { name: heading })).toHaveCount(0);
  expect(new URL(page.url()).pathname).toBe(path);
}

function hasMountedApplication(page: Page): Promise<boolean> {
  return page
    .locator('#app')
    .evaluateAll((elements) =>
      elements.some((element) => element.dataset.vApp !== undefined),
    );
}

async function readSplitRoleRolesFallbackState(
  page: Page,
  path: string,
  heading: string,
  tableTestId: string,
  targetDocumentEverCommitted: boolean,
  startupError: SplitRoleStartupError,
  moduleFailure: SplitRoleModuleFailure | undefined,
): Promise<SplitRoleRolesFallbackState> {
  try {
    const [
      persistedAccessState,
      applicationMounted,
      applicationRootPresent,
      documentReadyState,
      fallbackSurfaceVisible,
      protectedHeadingCount,
      protectedTableCount,
      startupOverlayVisible,
    ] = await Promise.all([
      readSplitRolePersistedAccessState(page),
      hasMountedApplication(page),
      page
        .locator('#app')
        .count()
        .then((count) => count > 0),
      page.evaluate(() => document.readyState),
      page.locator('svg[viewBox="0 0 860 571"]').isVisible(),
      page.getByRole('heading', { name: heading }).count(),
      page.getByTestId(tableTestId).count(),
      page.locator('#__app-loading__').isVisible(),
    ]);
    const currentPath = new URL(page.url()).pathname;

    return {
      ...persistedAccessState,
      applicationMounted,
      applicationRootPresent,
      documentReadyState,
      fallbackSurfaceVisible,
      isGameConfigRoute: currentPath === '/game-config',
      isLoginRoute: currentPath === '/auth/login',
      ...(moduleFailure ? { moduleFailure } : {}),
      protectedHeadingCount,
      protectedTableCount,
      requestedPathRetained: currentPath === path,
      startupError,
      startupOverlayVisible,
      targetDocumentEverCommitted,
      targetDocumentStateAvailable: true,
    };
  } catch {
    return {
      applicationMounted: false,
      applicationRootPresent: false,
      documentReadyState: 'unavailable',
      fallbackSurfaceVisible: false,
      hasAccessPermission: false,
      hasAdministratorsReadPermission: false,
      hasGameConfigAccessPermission: false,
      hasPersistedAccessToken: false,
      hasRolesReadPermission: false,
      isGameConfigRoute: false,
      isLoginRoute: false,
      ...(moduleFailure ? { moduleFailure } : {}),
      protectedHeadingCount: 0,
      protectedTableCount: 0,
      requestedPathRetained: false,
      startupError,
      startupOverlayVisible: false,
      targetDocumentEverCommitted,
      targetDocumentStateAvailable: false,
    };
  }
}

async function readSplitRolePersistedAccessState(
  page: Page,
): Promise<SplitRolePersistedAccessState> {
  return page.evaluate(
    ({ access, administratorsRead, gameConfigAccess, rolesRead }) => {
      const noPermissions = {
        hasAccessPermission: false,
        hasAdministratorsReadPermission: false,
        hasGameConfigAccessPermission: false,
        hasPersistedAccessToken: false,
        hasRolesReadPermission: false,
      };

      for (let index = 0; index < localStorage.length; index += 1) {
        const key = localStorage.key(index);
        if (!key?.endsWith('-core-access')) {
          continue;
        }

        const serializedState = localStorage.getItem(key);
        if (!serializedState) {
          continue;
        }

        try {
          const state = JSON.parse(serializedState) as {
            accessCodes?: unknown;
            accessToken?: unknown;
          };
          const accessCodes = Array.isArray(state.accessCodes)
            ? state.accessCodes
            : [];
          return {
            hasAccessPermission: accessCodes.includes(access),
            hasAdministratorsReadPermission:
              accessCodes.includes(administratorsRead),
            hasGameConfigAccessPermission:
              accessCodes.includes(gameConfigAccess),
            hasPersistedAccessToken:
              typeof state.accessToken === 'string' &&
              state.accessToken.length > 0,
            hasRolesReadPermission: accessCodes.includes(rolesRead),
          };
        } catch {
          return noPermissions;
        }
      }

      return noPermissions;
    },
    {
      access: accessPermission,
      administratorsRead: administratorsReadPermission,
      gameConfigAccess: gameConfigAccessPermission,
      rolesRead: rolesReadPermission,
    },
  );
}

function readPersistedAccessPermission(
  page: Page,
  permission: string,
): Promise<boolean> {
  return page.evaluate((permissionName) => {
    for (let index = 0; index < localStorage.length; index += 1) {
      const key = localStorage.key(index);
      if (!key?.endsWith('-core-access')) {
        continue;
      }

      const serializedState = localStorage.getItem(key);
      if (!serializedState) {
        continue;
      }

      try {
        const state = JSON.parse(serializedState) as {
          accessCodes?: unknown;
        };
        return (
          Array.isArray(state.accessCodes) &&
          state.accessCodes.includes(permissionName)
        );
      } catch {
        return false;
      }
    }

    return false;
  }, permission);
}

async function readSplitRoleAdministratorsWorkspaceState(
  page: Page,
  createCommandState: SplitRoleAdministratorsCreateCommandState,
  startupError: SplitRoleStartupError,
  pageErrorSource: SplitRoleWorkspacePageErrorSource,
  moduleFailure: SplitRoleModuleFailure | undefined,
): Promise<SplitRoleAdministratorsWorkspaceState> {
  const [
    persistedPermissions,
    fallbackSurfaceVisible,
    protectedHeadingVisible,
    protectedTableVisible,
    loadingMaskVisible,
    startupOverlayVisible,
    applicationMounted,
  ] = await Promise.all([
    readSplitRolePersistedAccessState(page),
    page.locator('svg[viewBox="0 0 860 571"]').isVisible(),
    page.getByRole('heading', { name: 'Administrators' }).isVisible(),
    page.getByTestId('administrator-table').isVisible(),
    page
      .getByTestId('administrator-table')
      .locator('.el-loading-mask')
      .isVisible(),
    page.locator('#__app-loading__').isVisible(),
    hasMountedApplication(page),
  ]);
  const currentPath = new URL(page.url()).pathname;

  return {
    applicationMounted,
    createCommandState,
    fallbackSurfaceVisible,
    isAdministratorAccessRootRoute: currentPath === '/admin-access',
    isAdministratorsRoute: currentPath === '/admin-access/administrators',
    isGameConfigRoute: currentPath === '/game-config',
    isLoginRoute: currentPath === '/auth/login',
    loadingMaskVisible,
    ...(moduleFailure ? { moduleFailure } : {}),
    pageErrorSource,
    protectedHeadingVisible,
    protectedTableVisible,
    startupOverlayVisible,
    startupError,
    ...persistedPermissions,
  };
}

function readSplitRoleInitialLoginPageLifecycle(
  page: Page,
): SplitRoleInitialLoginPageLifecycle {
  if (page.isClosed()) {
    return { mainFrameDetached: false, pageClosed: true };
  }

  try {
    return {
      mainFrameDetached: page.mainFrame().isDetached(),
      pageClosed: false,
    };
  } catch {
    return { mainFrameDetached: true, pageClosed: false };
  }
}

async function readSplitRoleInitialLoginApplicationRootState(
  page: Page,
): Promise<SplitRoleInitialLoginApplicationRootState> {
  const applicationRoot = page.locator('#app');
  if ((await applicationRoot.count()) === 0) {
    return 'absent';
  }

  const mounted = await applicationRoot.evaluateAll((elements) =>
    elements.some((element) => element.dataset.vApp !== undefined),
  );

  return mounted ? 'mounted' : 'present-unmounted';
}

async function readSplitRoleInitialLoginDocumentReadyState(
  page: Page,
): Promise<DocumentReadyState> {
  return page.evaluate(() => document.readyState);
}

async function readSplitRoleLoginState(
  page: Page,
  startupError: SplitRoleStartupError,
  readTargetDocumentObservation: () => SplitRoleInitialLoginTargetDocumentObservation,
): Promise<SplitRoleLoginState> {
  const unavailablePersistedAccessState: SplitRolePersistedAccessState = {
    hasAccessPermission: false,
    hasAdministratorsReadPermission: false,
    hasGameConfigAccessPermission: false,
    hasPersistedAccessToken: false,
    hasRolesReadPermission: false,
  };
  const unavailableRouteState: BootstrapLoginRouteState = {
    isGameConfigRoute: false,
    isLoginRoute: false,
  };
  const initialTargetDocumentObservation = readTargetDocumentObservation();
  const initialPageLifecycle = readSplitRoleInitialLoginPageLifecycle(page);
  const [
    persistedAccessState,
    applicationRootState,
    documentReadyState,
    gameConfigHeadingVisible,
    loginTitleVisible,
    loginUsernameFieldCount,
    loginUsernameVisible,
    routeState,
    startupOverlayVisible,
  ] = await Promise.all([
    readBootstrapLoginProbe(
      () => readSplitRolePersistedAccessState(page),
      unavailablePersistedAccessState,
    ),
    readBootstrapLoginProbe(
      () => readSplitRoleInitialLoginApplicationRootState(page),
      'unavailable' as const,
    ),
    readBootstrapLoginProbe(
      () => readSplitRoleInitialLoginDocumentReadyState(page),
      'unavailable' as const,
    ),
    readBootstrapLoginProbe(
      () =>
        page
          .getByRole('heading', { name: gameConfigLoginDestination.heading })
          .isVisible(),
      false,
    ),
    readBootstrapLoginProbe(
      () =>
        page.getByRole('heading', { name: 'NovaGame Operations' }).isVisible(),
      false,
    ),
    readBootstrapLoginProbe(() => formField(page, 'login-username').count(), 0),
    readBootstrapLoginProbe(
      () => formField(page, 'login-username').isVisible(),
      false,
    ),
    readBootstrapLoginProbe(
      () => readBootstrapLoginRouteState(page),
      unavailableRouteState,
    ),
    readBootstrapLoginProbe(
      () => page.locator('#__app-loading__').isVisible(),
      false,
    ),
  ]);
  const finalTargetDocumentObservation = readTargetDocumentObservation();
  const finalPageLifecycle = readSplitRoleInitialLoginPageLifecycle(page);

  return {
    ...persistedAccessState.value,
    applicationMounted: applicationRootState.value === 'mounted',
    applicationRootState: applicationRootState.value,
    applicationRootStateAvailable: applicationRootState.available,
    documentReadyState: documentReadyState.value,
    documentReadyStateAvailable: documentReadyState.available,
    gameConfigHeadingVisible: gameConfigHeadingVisible.value,
    gameConfigHeadingStateAvailable: gameConfigHeadingVisible.available,
    initialLoginContextClosed:
      initialTargetDocumentObservation.contextClosed ||
      finalTargetDocumentObservation.contextClosed,
    initialLoginMainFrameDetached:
      initialPageLifecycle.mainFrameDetached ||
      finalPageLifecycle.mainFrameDetached,
    initialLoginPageClosed:
      initialPageLifecycle.pageClosed || finalPageLifecycle.pageClosed,
    initialLoginTargetDocumentChangedDuringProbe:
      initialTargetDocumentObservation.generation !==
      finalTargetDocumentObservation.generation,
    initialLoginTargetDocumentPhaseAfter: finalTargetDocumentObservation.phase,
    initialLoginTargetDocumentPhaseBefore:
      initialTargetDocumentObservation.phase,
    ...routeState.value,
    loginTitleVisible: loginTitleVisible.value,
    loginTitleStateAvailable: loginTitleVisible.available,
    loginUsernameFieldCount: loginUsernameFieldCount.value,
    loginUsernameFieldCountStateAvailable: loginUsernameFieldCount.available,
    loginUsernameVisible: loginUsernameVisible.value,
    loginUsernameVisibleStateAvailable: loginUsernameVisible.available,
    persistedAccessStateAvailable: persistedAccessState.available,
    routeStateAvailable: routeState.available,
    startupError,
    startupOverlayVisible: startupOverlayVisible.value,
    startupOverlayStateAvailable: startupOverlayVisible.available,
  };
}

function createBootstrapLoginEndpointState(): BootstrapLoginEndpointState {
  return { requestFailed: false, requestStarted: false };
}

function createBootstrapLoginResponseState(): BootstrapLoginResponseState {
  return {
    accessCodes: createBootstrapLoginEndpointState(),
    gameConfigDrafts: createBootstrapLoginEndpointState(),
    gameConfigEnvironment: createBootstrapLoginEndpointState(),
    gameConfigVersions: createBootstrapLoginEndpointState(),
    login: createBootstrapLoginEndpointState(),
    pageError: false,
    userInfo: createBootstrapLoginEndpointState(),
  };
}

function getBootstrapLoginEndpointState(
  responseState: BootstrapLoginResponseState,
  request: Request,
): BootstrapLoginEndpointState | undefined {
  const method = request.method();
  const pathname = new URL(request.url()).pathname;
  if (method === 'POST' && pathname === '/api/auth/login') {
    return responseState.login;
  }
  if (method !== 'GET') {
    return undefined;
  }

  switch (pathname) {
    case '/api/auth/codes': {
      return responseState.accessCodes;
    }
    case '/api/game-config/drafts': {
      return responseState.gameConfigDrafts;
    }
    case '/api/game-config/environments/development': {
      return responseState.gameConfigEnvironment;
    }
    case '/api/game-config/versions': {
      return responseState.gameConfigVersions;
    }
    case '/api/user/info': {
      return responseState.userInfo;
    }
    default: {
      return undefined;
    }
  }
}

async function readBootstrapLoginState(
  page: Page,
): Promise<BootstrapLoginState> {
  const unavailablePersistedAccessState: SplitRolePersistedAccessState = {
    hasAccessPermission: false,
    hasAdministratorsReadPermission: false,
    hasGameConfigAccessPermission: false,
    hasPersistedAccessToken: false,
    hasRolesReadPermission: false,
  };
  const unavailableRouteState: BootstrapLoginRouteState = {
    isGameConfigRoute: false,
    isLoginRoute: false,
  };
  const [
    persistedAccessState,
    applicationMounted,
    gameConfigHeadingVisible,
    loginUsernameVisible,
    routeState,
    startupOverlayVisible,
  ] = await Promise.all([
    readBootstrapLoginPersistedAccessState(
      page,
      unavailablePersistedAccessState,
    ),
    readBootstrapLoginProbe(() => hasMountedApplication(page), false),
    readBootstrapLoginProbe(
      () =>
        page
          .getByRole('heading', { name: gameConfigLoginDestination.heading })
          .isVisible(),
      false,
    ),
    readBootstrapLoginProbe(
      () => formField(page, 'login-username').isVisible(),
      false,
    ),
    readBootstrapLoginProbe(
      () => readBootstrapLoginRouteState(page),
      unavailableRouteState,
    ),
    readBootstrapLoginProbe(
      () => page.locator('#__app-loading__').isVisible(),
      false,
    ),
  ]);

  return {
    ...persistedAccessState.value,
    applicationMounted: applicationMounted.value,
    applicationMountedStateAvailable: applicationMounted.available,
    gameConfigHeadingVisible: gameConfigHeadingVisible.value,
    gameConfigHeadingStateAvailable: gameConfigHeadingVisible.available,
    ...routeState.value,
    loginUsernameVisible: loginUsernameVisible.value,
    loginUsernameStateAvailable: loginUsernameVisible.available,
    persistedAccessStateAvailable: persistedAccessState.available,
    routeStateAvailable: routeState.available,
    startupOverlayVisible: startupOverlayVisible.value,
    startupOverlayStateAvailable: startupOverlayVisible.available,
  };
}

async function readAccessOnlyAuditRouteFallbackState(
  page: Page,
  targetDocumentEverCommitted: boolean,
  startupError: SplitRoleStartupError,
): Promise<AccessOnlyAuditRouteFallbackState> {
  const unavailableLocation: AccessOnlyAuditRouteLocation = {
    isGameConfigRoute: false,
    isLoginRoute: false,
    requestedPathRetained: false,
  };
  const unavailablePersistedAccessState: SplitRolePersistedAccessState = {
    hasAccessPermission: false,
    hasAdministratorsReadPermission: false,
    hasGameConfigAccessPermission: false,
    hasPersistedAccessToken: false,
    hasRolesReadPermission: false,
  };
  const [
    applicationMounted,
    auditLogsReadPermissionState,
    fallbackSurfaceVisible,
    persistedAccessState,
    protectedHeadingCount,
    protectedTableCount,
    routeLocation,
    startupOverlayVisible,
  ] = await Promise.all([
    readBootstrapLoginProbe(() => hasMountedApplication(page), false),
    readBootstrapLoginProbe(
      () => readPersistedAccessPermission(page, auditLogsReadPermission),
      false,
    ),
    readBootstrapLoginProbe(
      () => page.locator('svg[viewBox="0 0 860 571"]').isVisible(),
      false,
    ),
    readBootstrapLoginProbe(
      () => readSplitRolePersistedAccessState(page),
      unavailablePersistedAccessState,
    ),
    readBootstrapLoginProbe(
      () => page.getByRole('heading', { name: 'Audit history' }).count(),
      0,
    ),
    readBootstrapLoginProbe(() => page.getByTestId('audit-table').count(), 0),
    readBootstrapLoginProbe(
      () => readAccessOnlyAuditRouteLocation(page),
      unavailableLocation,
    ),
    readBootstrapLoginProbe(
      () => page.locator('#__app-loading__').isVisible(),
      false,
    ),
  ]);

  return {
    applicationMounted,
    auditLogsReadPermission: auditLogsReadPermissionState,
    fallbackSurfaceVisible,
    persistedAccessState,
    protectedHeadingCount,
    protectedTableCount,
    routeLocation,
    startupError,
    startupOverlayVisible,
    targetDocumentEverCommitted,
  };
}

async function readSplitRoleSessionsRouteFallbackState(
  page: Page,
  targetDocumentEverCommitted: boolean,
  startupError: SplitRoleStartupError,
  moduleFailure: SplitRoleModuleFailure | undefined,
): Promise<SplitRoleSessionsRouteFallbackState> {
  const unavailableLocation: SplitRoleSessionsRouteLocation = {
    isGameConfigRoute: false,
    isLoginRoute: false,
    requestedPathRetained: false,
  };
  const unavailablePersistedAccessState: SplitRolePersistedAccessState = {
    hasAccessPermission: false,
    hasAdministratorsReadPermission: false,
    hasGameConfigAccessPermission: false,
    hasPersistedAccessToken: false,
    hasRolesReadPermission: false,
  };
  const [
    applicationMounted,
    fallbackSurfaceVisible,
    persistedAccessState,
    protectedHeadingCount,
    protectedTableCount,
    routeLocation,
    sessionsReadPermissionState,
    startupOverlayVisible,
  ] = await Promise.all([
    readBootstrapLoginProbe(() => hasMountedApplication(page), false),
    readBootstrapLoginProbe(
      () => page.locator('svg[viewBox="0 0 860 571"]').isVisible(),
      false,
    ),
    readBootstrapLoginProbe(
      () => readSplitRolePersistedAccessState(page),
      unavailablePersistedAccessState,
    ),
    readBootstrapLoginProbe(
      () => page.getByRole('heading', { name: 'Sessions' }).count(),
      0,
    ),
    readBootstrapLoginProbe(() => page.getByTestId('session-table').count(), 0),
    readBootstrapLoginProbe(
      () => readSplitRoleSessionsRouteLocation(page),
      unavailableLocation,
    ),
    readBootstrapLoginProbe(
      () => readPersistedAccessPermission(page, sessionsReadPermission),
      false,
    ),
    readBootstrapLoginProbe(
      () => page.locator('#__app-loading__').isVisible(),
      false,
    ),
  ]);

  return {
    applicationMounted,
    fallbackSurfaceVisible,
    ...(moduleFailure ? { moduleFailure } : {}),
    persistedAccessState,
    protectedHeadingCount,
    protectedTableCount,
    routeLocation,
    sessionsReadPermission: sessionsReadPermissionState,
    startupError,
    startupOverlayVisible,
    targetDocumentEverCommitted,
  };
}

async function throwAccessOnlyGameConfigStartupFailure(
  page: Page,
  pageError: boolean,
): Promise<never> {
  const [
    applicationMounted,
    gameConfigHeadingVisible,
    routeState,
    startupOverlayVisible,
  ] = await Promise.all([
    readBootstrapLoginProbe(() => hasMountedApplication(page), false),
    readBootstrapLoginProbe(
      () =>
        page
          .getByRole('heading', { name: gameConfigLoginDestination.heading })
          .isVisible(),
      false,
    ),
    readBootstrapLoginProbe(() => readBootstrapLoginRouteState(page), {
      isGameConfigRoute: false,
      isLoginRoute: false,
    }),
    readBootstrapLoginProbe(
      () => page.locator('#__app-loading__').isVisible(),
      false,
    ),
  ]);

  if (pageError) {
    throw new Error('Access-only GameConfig startup reported a page error.');
  }
  if (!routeState.available) {
    throw new Error('Access-only GameConfig route state was unavailable.');
  }
  if (!routeState.value.isGameConfigRoute) {
    if (routeState.value.isLoginRoute) {
      throw new Error('Access-only GameConfig startup returned to login.');
    }
    throw new Error(
      'Access-only GameConfig startup did not resolve the route.',
    );
  }
  if (!applicationMounted.available || !applicationMounted.value) {
    throw new Error(
      'Access-only GameConfig startup did not mount the application.',
    );
  }
  if (!startupOverlayVisible.available) {
    throw new Error(
      'Access-only GameConfig startup overlay state was unavailable.',
    );
  }
  if (startupOverlayVisible.value) {
    throw new Error(
      'Access-only GameConfig startup retained the loading overlay.',
    );
  }
  if (!gameConfigHeadingVisible.available || !gameConfigHeadingVisible.value) {
    throw new Error('Access-only GameConfig heading did not become visible.');
  }

  throw new Error('Access-only GameConfig startup did not complete.');
}

async function readBootstrapLoginPersistedAccessState(
  page: Page,
  fallback: SplitRolePersistedAccessState,
): Promise<BootstrapLoginProbe<SplitRolePersistedAccessState>> {
  const initialProbe = await readBootstrapLoginProbe(
    () => readSplitRolePersistedAccessState(page),
    fallback,
  );
  if (initialProbe.available) {
    return initialProbe;
  }

  try {
    await page.waitForLoadState('domcontentloaded', {
      timeout: bootstrapLoginProbeRetryTimeoutMs,
    });
  } catch {
    return initialProbe;
  }

  return readBootstrapLoginProbe(
    () => readSplitRolePersistedAccessState(page),
    fallback,
  );
}

async function readBootstrapLoginProbe<T>(
  read: () => Promise<T>,
  fallback: T,
): Promise<BootstrapLoginProbe<T>> {
  try {
    return { available: true, value: await read() };
  } catch {
    return { available: false, value: fallback };
  }
}

async function readBootstrapLoginRouteState(
  page: Page,
): Promise<BootstrapLoginRouteState> {
  const currentPath = new URL(page.url()).pathname;
  return {
    isGameConfigRoute: currentPath === '/game-config',
    isLoginRoute: currentPath === '/auth/login',
  };
}

async function readAccessOnlyAuditRouteLocation(
  page: Page,
): Promise<AccessOnlyAuditRouteLocation> {
  const currentPath = new URL(page.url()).pathname;
  return {
    isGameConfigRoute: currentPath === '/game-config',
    isLoginRoute: currentPath === '/auth/login',
    requestedPathRetained: currentPath === '/admin-access/audit',
  };
}

async function readSplitRoleSessionsRouteLocation(
  page: Page,
): Promise<SplitRoleSessionsRouteLocation> {
  const currentPath = new URL(page.url()).pathname;
  return {
    isGameConfigRoute: currentPath === '/game-config',
    isLoginRoute: currentPath === '/auth/login',
    requestedPathRetained: currentPath === '/admin-access/sessions',
  };
}

function throwBootstrapLoginEndpointFailure(
  endpoint: string,
  endpointState: BootstrapLoginEndpointState,
): never {
  if (!endpointState.requestStarted) {
    throw new Error(`Bootstrap ${endpoint} request was not started.`);
  }
  if (endpointState.requestFailed) {
    throw new Error(`Bootstrap ${endpoint} request failed before a response.`);
  }
  if (endpointState.status === undefined) {
    throw new Error(
      `Bootstrap ${endpoint} request did not receive a response.`,
    );
  }
  if (endpointState.status === 401) {
    throw new Error(`Bootstrap ${endpoint} response was unauthorized.`);
  }
  if (endpointState.status === 403) {
    throw new Error(`Bootstrap ${endpoint} response was forbidden.`);
  }
  if (endpointState.status !== 200) {
    throw new Error(`Bootstrap ${endpoint} response did not succeed.`);
  }

  throw new Error(`Bootstrap ${endpoint} response unexpectedly failed.`);
}

function throwBootstrapLoginFailure(
  responseState: BootstrapLoginResponseState,
  loginState: BootstrapLoginState,
  submissionState: LoginSubmissionState | undefined,
): never {
  if (!submissionState) {
    throwBootstrapLoginStateUnavailable('Bootstrap login', loginState);
    if (!loginState.isLoginRoute) {
      throw new Error(
        'Bootstrap login navigation did not reach the login page.',
      );
    }
    if (!loginState.loginUsernameVisible) {
      throw new Error('Bootstrap login username field did not become visible.');
    }
    throw new Error('Bootstrap login control state was not observed.');
  }
  if (submissionState.buttonCount === 0) {
    throw new Error('Bootstrap login action was absent.');
  }
  if (submissionState.buttonCount !== 1) {
    throw new Error('Bootstrap login action was not unique.');
  }
  if (submissionState.visibleButtonCount !== 1) {
    throw new Error('Bootstrap login action was not visible.');
  }
  if (!submissionState.enabled) {
    throw new Error('Bootstrap login action was disabled.');
  }
  if (!submissionState.clickCompleted) {
    throw new Error('Bootstrap login action did not complete.');
  }
  if (submissionState.usernameInvalid || submissionState.passwordInvalid) {
    throw new Error('Bootstrap client validation prevented login submission.');
  }
  if (submissionState.enabledAfterClick === false) {
    throw new Error('Bootstrap login flow remained pending before a request.');
  }

  if (
    !responseState.login.requestStarted ||
    responseState.login.requestFailed ||
    responseState.login.status !== 200
  ) {
    throwBootstrapLoginEndpointFailure('login', responseState.login);
  }
  if (
    !responseState.accessCodes.requestStarted ||
    responseState.accessCodes.requestFailed ||
    responseState.accessCodes.status !== 200
  ) {
    throwBootstrapLoginEndpointFailure(
      'access-code',
      responseState.accessCodes,
    );
  }
  if (
    !responseState.userInfo.requestStarted ||
    responseState.userInfo.requestFailed ||
    responseState.userInfo.status !== 200
  ) {
    throwBootstrapLoginEndpointFailure(
      'user-information',
      responseState.userInfo,
    );
  }
  if (
    !responseState.gameConfigDrafts.requestStarted ||
    responseState.gameConfigDrafts.requestFailed ||
    responseState.gameConfigDrafts.status !== 200
  ) {
    throwBootstrapLoginEndpointFailure(
      'GameConfig drafts',
      responseState.gameConfigDrafts,
    );
  }
  if (
    !responseState.gameConfigVersions.requestStarted ||
    responseState.gameConfigVersions.requestFailed ||
    responseState.gameConfigVersions.status !== 200
  ) {
    throwBootstrapLoginEndpointFailure(
      'GameConfig versions',
      responseState.gameConfigVersions,
    );
  }
  if (
    !responseState.gameConfigEnvironment.requestStarted ||
    responseState.gameConfigEnvironment.requestFailed ||
    responseState.gameConfigEnvironment.status !== 200
  ) {
    throwBootstrapLoginEndpointFailure(
      'GameConfig environment',
      responseState.gameConfigEnvironment,
    );
  }

  throwBootstrapLoginStateUnavailable('Bootstrap login', loginState);
  if (!loginState.hasPersistedAccessToken) {
    throw new Error('Bootstrap persisted state omitted the access token.');
  }
  if (!loginState.hasAccessPermission) {
    throw new Error('Bootstrap persisted state omitted Administrator Access.');
  }
  if (!loginState.hasGameConfigAccessPermission) {
    throw new Error('Bootstrap persisted state omitted GameConfig Access.');
  }
  if (!loginState.hasAdministratorsReadPermission) {
    throw new Error(
      'Bootstrap persisted state omitted Administrator Read permission.',
    );
  }
  if (loginState.isLoginRoute) {
    throw new Error(
      'Bootstrap authenticated navigation returned to the login page.',
    );
  }
  if (!loginState.isGameConfigRoute) {
    throw new Error(
      'Bootstrap authenticated navigation did not resolve to GameConfig.',
    );
  }
  if (responseState.pageError) {
    throw new Error('Bootstrap GameConfig startup reported a page error.');
  }
  if (!loginState.applicationMounted) {
    throw new Error('Bootstrap application root did not mount after login.');
  }
  if (loginState.startupOverlayVisible) {
    throw new Error('Bootstrap GameConfig route retained the startup overlay.');
  }
  if (!loginState.gameConfigHeadingVisible) {
    throw new Error('Bootstrap GameConfig heading did not become visible.');
  }

  throw new Error('Bootstrap GameConfig login did not become ready.');
}

function throwBootstrapLoginStateUnavailable(
  prefix: string,
  loginState: BootstrapLoginState,
): void {
  if (!loginState.persistedAccessStateAvailable) {
    throw new Error(`${prefix} persisted access state was unavailable.`);
  }
  if (!loginState.routeStateAvailable) {
    throw new Error(`${prefix} route state was unavailable.`);
  }
  if (!loginState.applicationMountedStateAvailable) {
    throw new Error(`${prefix} application mount state was unavailable.`);
  }
  if (!loginState.startupOverlayStateAvailable) {
    throw new Error(`${prefix} startup overlay state was unavailable.`);
  }
  if (!loginState.loginUsernameStateAvailable) {
    throw new Error(`${prefix} login username state was unavailable.`);
  }
  if (!loginState.gameConfigHeadingStateAvailable) {
    throw new Error(`${prefix} GameConfig heading state was unavailable.`);
  }
}

async function readLeafOnlyDirectRouteState(
  page: Page,
  startupError: SplitRoleStartupError,
  moduleFailure: SplitRoleModuleFailure | undefined,
): Promise<LeafOnlyDirectRouteState> {
  try {
    const [
      persistedAccessState,
      applicationMounted,
      loginUsernameVisible,
      protectedHeadingVisible,
      protectedTableVisible,
      startupOverlayVisible,
    ] = await Promise.all([
      readSplitRolePersistedAccessState(page),
      hasMountedApplication(page),
      formField(page, 'login-username').isVisible(),
      page.getByRole('heading', { name: 'Administrators' }).isVisible(),
      page.getByTestId('administrator-table').isVisible(),
      page.locator('#__app-loading__').isVisible(),
    ]);
    const currentPath = new URL(page.url()).pathname;

    return {
      ...persistedAccessState,
      applicationMounted,
      isLoginRoute: currentPath === '/auth/login',
      loginUsernameVisible,
      ...(moduleFailure ? { moduleFailure } : {}),
      protectedHeadingVisible,
      protectedTableVisible,
      startupError,
      startupOverlayVisible,
      targetDocumentStateAvailable: true,
    };
  } catch {
    return {
      applicationMounted: false,
      hasAccessPermission: false,
      hasAdministratorsReadPermission: false,
      hasGameConfigAccessPermission: false,
      hasPersistedAccessToken: false,
      hasRolesReadPermission: false,
      isLoginRoute: false,
      loginUsernameVisible: false,
      ...(moduleFailure ? { moduleFailure } : {}),
      protectedHeadingVisible: false,
      protectedTableVisible: false,
      startupError,
      startupOverlayVisible: false,
      targetDocumentStateAvailable: false,
    };
  }
}

function throwLeafOnlyDirectRouteFailure(
  routeState: LeafOnlyDirectRouteState,
): never {
  if (!routeState.targetDocumentStateAvailable) {
    throw new Error(
      'Leaf-only direct Administrator route target document state was unavailable.',
    );
  }
  if (routeState.hasPersistedAccessToken) {
    throw new Error(
      'Leaf-only direct Administrator route retained the persisted access token.',
    );
  }
  if (routeState.hasAccessPermission) {
    throw new Error(
      'Leaf-only direct Administrator route retained Administrator Access.',
    );
  }
  if (routeState.hasAdministratorsReadPermission) {
    throw new Error(
      'Leaf-only direct Administrator route retained Administrator Read permission.',
    );
  }
  if (routeState.moduleFailure) {
    throwSplitRoleAdministratorsWorkspaceModuleFailure(
      routeState.moduleFailure,
    );
  }
  if (routeState.startupError === 'dynamic-module-fetch') {
    throw new Error(
      'Leaf-only direct Administrator route reported a dynamic-module fetch error.',
    );
  }
  if (routeState.startupError === 'page-error') {
    throw new Error(
      'Leaf-only direct Administrator route reported a page error.',
    );
  }
  if (!routeState.applicationMounted) {
    throw new Error(
      'Leaf-only direct Administrator route did not mount the application.',
    );
  }
  if (routeState.startupOverlayVisible) {
    throw new Error(
      'Leaf-only direct Administrator route retained the startup overlay.',
    );
  }
  if (routeState.protectedHeadingVisible) {
    throw new Error(
      'Leaf-only direct Administrator route retained the protected heading.',
    );
  }
  if (routeState.protectedTableVisible) {
    throw new Error(
      'Leaf-only direct Administrator route retained the protected table.',
    );
  }
  if (!routeState.isLoginRoute) {
    throw new Error(
      'Leaf-only direct Administrator route did not resolve to login.',
    );
  }
  if (!routeState.loginUsernameVisible) {
    throw new Error(
      'Leaf-only direct Administrator route did not render the login username field.',
    );
  }

  throw new Error('Leaf-only direct Administrator route did not become ready.');
}

function throwSplitRoleRolesFallbackFailure(
  routeState: SplitRoleRolesFallbackState,
): never {
  if (!routeState.targetDocumentEverCommitted) {
    throw new Error('Split-role Roles route target document did not commit.');
  }
  if (!routeState.targetDocumentStateAvailable) {
    throw new Error(
      'Split-role Roles route target document state was unavailable.',
    );
  }
  if (routeState.isLoginRoute) {
    throw new Error('Split-role Roles route redirected to login.');
  }
  if (routeState.isGameConfigRoute) {
    throw new Error('Split-role Roles route redirected to GameConfig.');
  }
  if (!routeState.requestedPathRetained) {
    throw new Error('Split-role Roles route changed the requested path.');
  }
  if (!routeState.applicationRootPresent) {
    throw new Error(
      'Split-role Roles route target document did not contain the Vue application root.',
    );
  }
  if (routeState.moduleFailure) {
    throwSplitRoleRouteFallbackModuleFailure('Roles', routeState.moduleFailure);
  }
  if (routeState.startupError === 'dynamic-module-fetch') {
    throw new Error(
      'Split-role Roles route reported a target-scoped dynamic-module fetch error.',
    );
  }
  if (routeState.startupError === 'page-error') {
    throw new Error(
      'Split-role Roles route reported a target-scoped page error.',
    );
  }
  if (!routeState.applicationMounted) {
    switch (routeState.documentReadyState) {
      case 'complete': {
        throw new Error(
          'Split-role Roles route completed document loading without mounting the application.',
        );
      }
      case 'interactive': {
        throw new Error(
          'Split-role Roles route remained interactive without mounting the application.',
        );
      }
      case 'loading': {
        throw new Error(
          'Split-role Roles route remained loading without mounting the application.',
        );
      }
      case 'unavailable': {
        throw new Error(
          'Split-role Roles route document readiness state was unavailable before application mount.',
        );
      }
    }
  }
  if (routeState.startupOverlayVisible) {
    throw new Error('Split-role Roles route retained the startup overlay.');
  }
  if (!routeState.hasPersistedAccessToken) {
    throw new Error('Split-role Roles route lost the persisted access token.');
  }
  if (!routeState.hasAccessPermission) {
    throw new Error('Split-role Roles route lost Administrator Access.');
  }
  if (!routeState.hasAdministratorsReadPermission) {
    throw new Error(
      'Split-role Roles route lost Administrator Read permission.',
    );
  }
  if (routeState.hasRolesReadPermission) {
    throw new Error('Split-role Roles route retained Roles Read permission.');
  }
  if (routeState.protectedHeadingCount > 0) {
    throw new Error('Split-role Roles route retained the protected heading.');
  }
  if (routeState.protectedTableCount > 0) {
    throw new Error('Split-role Roles route retained the protected table.');
  }
  if (!routeState.fallbackSurfaceVisible) {
    throw new Error('Split-role Roles route did not render its fallback.');
  }

  throw new Error(
    'Split-role Roles route failed an unexpected fallback check.',
  );
}

function throwIfRolesPermissionsTestDeadline(
  routeLabel: 'Roles' | 'Sessions',
  error: unknown,
): void {
  if (
    error instanceof Error &&
    /^Test timeout of \d+ms exceeded\.$/.test(error.message)
  ) {
    throw new Error(
      `Split-role ${routeLabel} route fallback exhausted the roles-permissions test deadline.`,
    );
  }
}

function throwAccessOnlyAuditRouteFallbackFailure(
  routeState: AccessOnlyAuditRouteFallbackState,
): never {
  if (!routeState.targetDocumentEverCommitted) {
    throw new Error('Access-only audit route target document did not commit.');
  }
  if (!routeState.routeLocation.available) {
    throw new Error('Access-only audit route location state was unavailable.');
  }
  if (!routeState.applicationMounted.available) {
    throw new Error('Access-only audit route mount state was unavailable.');
  }
  if (!routeState.startupOverlayVisible.available) {
    throw new Error('Access-only audit route overlay state was unavailable.');
  }
  if (!routeState.persistedAccessState.available) {
    throw new Error(
      'Access-only audit route persisted access state was unavailable.',
    );
  }
  if (!routeState.auditLogsReadPermission.available) {
    throw new Error(
      'Access-only audit route Audit Logs Read state was unavailable.',
    );
  }
  if (!routeState.fallbackSurfaceVisible.available) {
    throw new Error('Access-only audit route fallback state was unavailable.');
  }
  if (!routeState.protectedHeadingCount.available) {
    throw new Error(
      'Access-only audit route protected heading state was unavailable.',
    );
  }
  if (!routeState.protectedTableCount.available) {
    throw new Error(
      'Access-only audit route protected table state was unavailable.',
    );
  }
  if (routeState.routeLocation.value.isLoginRoute) {
    throw new Error('Access-only audit route redirected to login.');
  }
  if (routeState.routeLocation.value.isGameConfigRoute) {
    throw new Error('Access-only audit route redirected to GameConfig.');
  }
  if (!routeState.routeLocation.value.requestedPathRetained) {
    throw new Error('Access-only audit route changed the requested path.');
  }
  const persistedAccessState = routeState.persistedAccessState.value;
  if (!persistedAccessState.hasPersistedAccessToken) {
    throw new Error('Access-only audit route lost the persisted access token.');
  }
  if (!persistedAccessState.hasAccessPermission) {
    throw new Error('Access-only audit route lost Administrator Access.');
  }
  if (!persistedAccessState.hasGameConfigAccessPermission) {
    throw new Error('Access-only audit route lost GameConfig Access.');
  }
  if (persistedAccessState.hasAdministratorsReadPermission) {
    throw new Error(
      'Access-only audit route retained Administrator Read permission.',
    );
  }
  if (persistedAccessState.hasRolesReadPermission) {
    throw new Error('Access-only audit route retained Roles Read permission.');
  }
  if (routeState.auditLogsReadPermission.value) {
    throw new Error(
      'Access-only audit route retained Audit Logs Read permission.',
    );
  }
  if (!routeState.applicationMounted.value) {
    throw new Error('Access-only audit route did not mount the application.');
  }
  if (routeState.startupOverlayVisible.value) {
    throw new Error('Access-only audit route retained the startup overlay.');
  }
  if (routeState.protectedHeadingCount.value > 0) {
    throw new Error('Access-only audit route retained the protected heading.');
  }
  if (routeState.protectedTableCount.value > 0) {
    throw new Error('Access-only audit route retained the protected table.');
  }
  if (!routeState.fallbackSurfaceVisible.value) {
    throw new Error('Access-only audit route did not render its fallback.');
  }
  if (routeState.startupError === 'dynamic-module-fetch') {
    throw new Error(
      'Access-only audit route reported a dynamic-module fetch error.',
    );
  }
  if (routeState.startupError === 'page-error') {
    throw new Error('Access-only audit route reported a page error.');
  }

  throw new Error(
    'Access-only audit route failed an unexpected fallback check.',
  );
}

function throwSplitRoleSessionsRouteFallbackFailure(
  routeState: SplitRoleSessionsRouteFallbackState,
): never {
  if (!routeState.targetDocumentEverCommitted) {
    throw new Error(
      'Split-role Sessions route target document did not commit.',
    );
  }
  if (!routeState.routeLocation.available) {
    throw new Error(
      'Split-role Sessions route location state was unavailable.',
    );
  }
  if (!routeState.applicationMounted.available) {
    throw new Error('Split-role Sessions route mount state was unavailable.');
  }
  if (!routeState.startupOverlayVisible.available) {
    throw new Error('Split-role Sessions route overlay state was unavailable.');
  }
  if (!routeState.persistedAccessState.available) {
    throw new Error(
      'Split-role Sessions route persisted access state was unavailable.',
    );
  }
  if (!routeState.sessionsReadPermission.available) {
    throw new Error(
      'Split-role Sessions route Sessions Read state was unavailable.',
    );
  }
  if (!routeState.fallbackSurfaceVisible.available) {
    throw new Error(
      'Split-role Sessions route fallback state was unavailable.',
    );
  }
  if (!routeState.protectedHeadingCount.available) {
    throw new Error(
      'Split-role Sessions route protected heading state was unavailable.',
    );
  }
  if (!routeState.protectedTableCount.available) {
    throw new Error(
      'Split-role Sessions route protected table state was unavailable.',
    );
  }
  if (routeState.routeLocation.value.isLoginRoute) {
    throw new Error('Split-role Sessions route redirected to login.');
  }
  if (routeState.routeLocation.value.isGameConfigRoute) {
    throw new Error('Split-role Sessions route redirected to GameConfig.');
  }
  if (!routeState.routeLocation.value.requestedPathRetained) {
    throw new Error('Split-role Sessions route changed the requested path.');
  }
  const persistedAccessState = routeState.persistedAccessState.value;
  if (!persistedAccessState.hasPersistedAccessToken) {
    throw new Error(
      'Split-role Sessions route lost the persisted access token.',
    );
  }
  if (!persistedAccessState.hasAccessPermission) {
    throw new Error('Split-role Sessions route lost Administrator Access.');
  }
  if (!persistedAccessState.hasGameConfigAccessPermission) {
    throw new Error('Split-role Sessions route lost GameConfig Access.');
  }
  if (!persistedAccessState.hasAdministratorsReadPermission) {
    throw new Error(
      'Split-role Sessions route lost Administrator Read permission.',
    );
  }
  if (persistedAccessState.hasRolesReadPermission) {
    throw new Error(
      'Split-role Sessions route retained Roles Read permission.',
    );
  }
  if (routeState.sessionsReadPermission.value) {
    throw new Error(
      'Split-role Sessions route retained Sessions Read permission.',
    );
  }
  if (!routeState.applicationMounted.value) {
    throw new Error('Split-role Sessions route did not mount the application.');
  }
  if (routeState.startupOverlayVisible.value) {
    throw new Error('Split-role Sessions route retained the startup overlay.');
  }
  if (routeState.protectedHeadingCount.value > 0) {
    throw new Error(
      'Split-role Sessions route retained the protected heading.',
    );
  }
  if (routeState.protectedTableCount.value > 0) {
    throw new Error('Split-role Sessions route retained the protected table.');
  }
  if (!routeState.fallbackSurfaceVisible.value) {
    throw new Error('Split-role Sessions route did not render its fallback.');
  }
  if (routeState.moduleFailure) {
    throwSplitRoleRouteFallbackModuleFailure(
      'Sessions',
      routeState.moduleFailure,
    );
  }
  if (routeState.startupError === 'dynamic-module-fetch') {
    throw new Error(
      'Split-role Sessions route reported a target-scoped dynamic-module fetch error.',
    );
  }
  if (routeState.startupError === 'page-error') {
    throw new Error(
      'Split-role Sessions route reported a target-scoped page error.',
    );
  }

  throw new Error(
    'Split-role Sessions route failed an unexpected fallback check.',
  );
}

function throwSplitRoleInitialLoginModuleFailure(
  moduleFailure: SplitRoleModuleFailure,
): never {
  switch (moduleFailure.outcome) {
    case 'client-error': {
      throw new Error(
        'Split-role initial-login script module returned a client error.',
      );
    }
    case 'request-aborted': {
      throw new Error(
        'Split-role initial-login script module request was aborted.',
      );
    }
    case 'request-connection': {
      throw new Error(
        'Split-role initial-login script module request failed to connect.',
      );
    }
    case 'request-failed': {
      throw new Error('Split-role initial-login script module request failed.');
    }
    case 'request-missing-details': {
      throw new Error(
        'Split-role initial-login script module request failure had no details.',
      );
    }
    case 'request-name-resolution': {
      throw new Error(
        'Split-role initial-login script module request had a name-resolution failure.',
      );
    }
    case 'request-network-unclassified': {
      throw new Error(
        'Split-role initial-login script module request had an unclassified network failure.',
      );
    }
    case 'request-network': {
      throw new Error(
        'Split-role initial-login script module request had a network failure.',
      );
    }
    case 'request-other': {
      throw new Error(
        'Split-role initial-login script module request had an unrecognized failure.',
      );
    }
    case 'request-policy': {
      throw new Error(
        'Split-role initial-login script module request was blocked by browser policy.',
      );
    }
    case 'request-protocol': {
      throw new Error(
        'Split-role initial-login script module request had a protocol failure.',
      );
    }
    case 'request-proxy': {
      throw new Error(
        'Split-role initial-login script module request had a proxy failure.',
      );
    }
    case 'request-reachability': {
      throw new Error(
        'Split-role initial-login script module request had a reachability failure.',
      );
    }
    case 'request-response': {
      throw new Error(
        'Split-role initial-login script module request had a response failure.',
      );
    }
    case 'request-timeout': {
      throw new Error(
        'Split-role initial-login script module request timed out.',
      );
    }
    case 'request-tls': {
      throw new Error(
        'Split-role initial-login script module request had a transport-security failure.',
      );
    }
    case 'server-error': {
      throw new Error(
        'Split-role initial-login script module returned a server error.',
      );
    }
    case 'unexpected-status': {
      throw new Error(
        'Split-role initial-login script module returned an unexpected status.',
      );
    }
  }

  throw new Error('Split-role initial-login module failure was invalid.');
}

function throwSplitRoleLoginFailure(
  responseState: SplitRoleLoginResponseState,
  loginState: SplitRoleLoginState,
  submissionState: LoginSubmissionState | undefined,
  initialLoginResponseState: SplitRoleInitialLoginResponseState,
): never {
  if (responseState.loginStatus === undefined) {
    if (!submissionState) {
      if (loginState.initialLoginContextClosed) {
        throw new Error(
          'Split-role initial-login browser context closed during control state observation.',
        );
      }
      if (loginState.initialLoginPageClosed) {
        throw new Error(
          'Split-role initial-login page closed during control state observation.',
        );
      }
      if (loginState.initialLoginMainFrameDetached) {
        throw new Error(
          'Split-role initial-login main frame detached during control state observation.',
        );
      }
      if (
        loginState.initialLoginTargetDocumentPhaseBefore !==
        'bare-target-active'
      ) {
        throw new Error(
          'Split-role initial-login bare target document was not active before control state observation.',
        );
      }
      if (
        loginState.initialLoginTargetDocumentPhaseAfter !== 'bare-target-active'
      ) {
        throw new Error(
          'Split-role initial-login bare target document was not active after control state observation.',
        );
      }
      if (loginState.initialLoginTargetDocumentChangedDuringProbe) {
        throw new Error(
          'Split-role initial-login target document changed during control state observation.',
        );
      }
      if (!loginState.persistedAccessStateAvailable) {
        throw new Error(
          'Split-role persisted authentication state was unavailable during control state observation.',
        );
      }
      if (!loginState.routeStateAvailable) {
        throw new Error(
          'Split-role login route state was unavailable during control state observation.',
        );
      }
      if (
        loginState.hasPersistedAccessToken ||
        loginState.hasAccessPermission ||
        loginState.hasAdministratorsReadPermission ||
        loginState.hasGameConfigAccessPermission ||
        loginState.hasRolesReadPermission
      ) {
        throw new Error(
          'Split-role persisted authenticated state remained before control state observation.',
        );
      }
      if (!loginState.isLoginRoute) {
        throw new Error(
          'Split-role login route was not active before control state observation.',
        );
      }
      if (loginState.startupError === 'dynamic-module-fetch') {
        if (initialLoginResponseState.moduleFailure) {
          throwSplitRoleInitialLoginModuleFailure(
            initialLoginResponseState.moduleFailure,
          );
        }
        throw new Error(
          'Split-role login reported a dynamic-module fetch error before control state observation.',
        );
      }
      if (loginState.startupError === 'page-error') {
        throw new Error(
          'Split-role login reported a page error before control state observation.',
        );
      }
      if (!loginState.applicationRootStateAvailable) {
        throw new Error(
          'Split-role initial-login application root state was unavailable during control state observation.',
        );
      }
      if (!loginState.documentReadyStateAvailable) {
        throw new Error(
          'Split-role initial-login document readiness state was unavailable during control state observation.',
        );
      }
      if (loginState.documentReadyState === 'loading') {
        throw new Error(
          'Split-role initial-login target document remained loading during control state observation.',
        );
      }
      if (loginState.applicationRootState === 'absent') {
        throw new Error(
          'Split-role initial-login target document lacked an application root before control state observation.',
        );
      }
      if (loginState.applicationRootState === 'present-unmounted') {
        throw new Error(
          'Split-role application root did not mount before control state observation.',
        );
      }
      if (loginState.applicationRootState === 'unavailable') {
        throw new Error(
          'Split-role initial-login application root state was unavailable before control state observation.',
        );
      }
      if (!loginState.startupOverlayStateAvailable) {
        throw new Error(
          'Split-role initial-login startup overlay state was unavailable during control state observation.',
        );
      }
      if (loginState.startupOverlayVisible) {
        throw new Error(
          'Split-role login route retained the startup overlay before control state observation.',
        );
      }
      if (initialLoginResponseState.pendingModuleResource) {
        throw new Error(
          `Split-role login retained a pending ${initialLoginResponseState.pendingModuleResource} script module request before control state observation.`,
        );
      }
      if (!loginState.loginUsernameFieldCountStateAvailable) {
        throw new Error(
          'Split-role initial-login username field count was unavailable during control state observation.',
        );
      }
      if (loginState.loginUsernameFieldCount === 0) {
        throw new Error(
          'Split-role login form did not render a username field before control state observation.',
        );
      }
      if (!loginState.loginTitleStateAvailable) {
        throw new Error(
          'Split-role initial-login title state was unavailable during control state observation.',
        );
      }
      if (!loginState.loginTitleVisible) {
        throw new Error(
          'Split-role login title was not visible before control state observation.',
        );
      }
      if (!loginState.loginUsernameVisibleStateAvailable) {
        throw new Error(
          'Split-role initial-login username visibility was unavailable during control state observation.',
        );
      }
      if (!loginState.loginUsernameVisible) {
        throw new Error(
          'Split-role login username field was not visible before control state observation.',
        );
      }
      throw new Error('Split-role login control state was not observed.');
    }
    if (submissionState.buttonCount === 0) {
      throw new Error('Split-role login action was absent.');
    }
    if (submissionState.buttonCount !== 1) {
      throw new Error('Split-role login action was not unique.');
    }
    if (submissionState.visibleButtonCount !== 1) {
      throw new Error('Split-role login action was not visible.');
    }
    if (!submissionState.enabled) {
      throw new Error('Split-role login action was disabled.');
    }
    if (!submissionState.clickCompleted) {
      throw new Error('Split-role login action did not complete.');
    }
    if (submissionState.usernameInvalid || submissionState.passwordInvalid) {
      throw new Error(
        'Split-role client validation prevented login submission.',
      );
    }
    if (submissionState.enabledAfterClick === false) {
      throw new Error(
        'Split-role login flow remained pending before a request.',
      );
    }
    if (responseState.loginRequestFailed) {
      throw new Error('Split-role login request failed before a response.');
    }
    if (responseState.loginRequestStarted) {
      throw new Error('Split-role login request did not receive a response.');
    }
    if (loginState.startupError !== 'none') {
      throw new Error(
        'Split-role login reported a page error before submission.',
      );
    }
    throw new Error('Split-role login request was not started.');
  }
  if (responseState.loginStatus === 401) {
    throw new Error('Split-role login response was unauthorized.');
  }
  if (responseState.loginStatus === 403) {
    throw new Error('Split-role login response was forbidden.');
  }
  if (responseState.loginStatus !== 200) {
    throw new Error('Split-role login response did not succeed.');
  }
  if (responseState.accessCodesStatus === undefined) {
    throw new Error('Split-role access-code response was not observed.');
  }
  if (responseState.accessCodesStatus === 401) {
    throw new Error('Split-role access-code response was unauthorized.');
  }
  if (responseState.accessCodesStatus === 403) {
    throw new Error('Split-role access-code response was forbidden.');
  }
  if (responseState.accessCodesStatus !== 200) {
    throw new Error('Split-role access-code response did not succeed.');
  }
  if (responseState.userInfoStatus === undefined) {
    throw new Error('Split-role user-information response was not observed.');
  }
  if (responseState.userInfoStatus === 401) {
    throw new Error('Split-role user-information response was unauthorized.');
  }
  if (responseState.userInfoStatus === 403) {
    throw new Error('Split-role user-information response was forbidden.');
  }
  if (responseState.userInfoStatus !== 200) {
    throw new Error('Split-role user-information response did not succeed.');
  }
  if (!loginState.hasPersistedAccessToken) {
    throw new Error('Split-role persisted state omitted the access token.');
  }
  if (!loginState.hasAccessPermission) {
    throw new Error('Split-role persisted state omitted Administrator Access.');
  }
  if (!loginState.hasGameConfigAccessPermission) {
    throw new Error('Split-role persisted state omitted GameConfig Access.');
  }
  if (!loginState.hasAdministratorsReadPermission) {
    throw new Error(
      'Split-role persisted state omitted Administrator Read permission.',
    );
  }
  if (loginState.hasRolesReadPermission) {
    throw new Error(
      'Split-role persisted state retained Roles Read permission.',
    );
  }
  if (responseState.moduleFailure) {
    throw new Error(
      `Split-role GameConfig startup reported a ${responseState.moduleFailure.resource} module ${responseState.moduleFailure.outcome}.`,
    );
  }
  if (loginState.startupError === 'dynamic-module-fetch') {
    throw new Error(
      'Split-role GameConfig startup reported a dynamic-module fetch error.',
    );
  }
  if (loginState.startupError === 'page-error') {
    throw new Error('Split-role GameConfig startup reported a page error.');
  }
  if (!loginState.applicationMounted) {
    throw new Error('Split-role application root did not mount after login.');
  }
  if (loginState.startupOverlayVisible) {
    throw new Error(
      'Split-role GameConfig route retained the startup overlay.',
    );
  }
  if (!loginState.isGameConfigRoute) {
    if (loginState.isLoginRoute) {
      throw new Error('Split-role login returned to the login page.');
    }
    throw new Error('Split-role login did not resolve to GameConfig.');
  }
  if (responseState.gameConfigDraftsStatus === undefined) {
    throw new Error('Split-role GameConfig drafts response was not observed.');
  }
  if (responseState.gameConfigDraftsStatus !== 200) {
    throw new Error('Split-role GameConfig drafts response did not succeed.');
  }
  if (responseState.gameConfigVersionsStatus === undefined) {
    throw new Error(
      'Split-role GameConfig versions response was not observed.',
    );
  }
  if (responseState.gameConfigVersionsStatus !== 200) {
    throw new Error('Split-role GameConfig versions response did not succeed.');
  }
  if (responseState.gameConfigEnvironmentStatus === undefined) {
    throw new Error(
      'Split-role GameConfig environment response was not observed.',
    );
  }
  if (responseState.gameConfigEnvironmentStatus !== 200) {
    throw new Error(
      'Split-role GameConfig environment response did not succeed.',
    );
  }
  if (!loginState.gameConfigHeadingStateAvailable) {
    throw new Error('Split-role GameConfig heading state was unavailable.');
  }
  if (!loginState.gameConfigHeadingVisible) {
    throw new Error('Split-role GameConfig heading did not become visible.');
  }

  throw new Error('Split-role GameConfig startup did not become ready.');
}

function throwSplitRoleAdministratorsWorkspaceFailure(
  workspaceState: SplitRoleAdministratorsWorkspaceState,
): never {
  if (!workspaceState.hasAccessPermission) {
    throw new Error('Split-role persisted state omitted Administrator Access.');
  }
  if (!workspaceState.hasAdministratorsReadPermission) {
    throw new Error(
      'Split-role persisted state omitted Administrator Read permission.',
    );
  }
  if (workspaceState.hasRolesReadPermission) {
    throw new Error(
      'Split-role persisted state retained Roles Read permission.',
    );
  }
  if (workspaceState.fallbackSurfaceVisible) {
    throw new Error(
      'Split-role authorized route rendered the fallback surface.',
    );
  }
  const diagnosticPriority =
    resolveSplitRoleAdministratorsWorkspaceDiagnosticPriority(
      workspaceState.createCommandState,
      workspaceState.moduleFailure !== undefined,
    );
  if (diagnosticPriority === 'create-command-exposed') {
    throw new Error(
      'Split-role Administrators workspace exposed the Create administrator command without Administrator Manage permission.',
    );
  }
  if (diagnosticPriority === 'module-failure' && workspaceState.moduleFailure) {
    throwSplitRoleAdministratorsWorkspaceModuleFailure(
      workspaceState.moduleFailure,
    );
  }
  if (!workspaceState.applicationMounted) {
    if (workspaceState.startupError === 'dynamic-module-fetch') {
      throw new Error(
        'Split-role application root did not mount after an unattributed dynamic-module fetch error.',
      );
    }
    if (workspaceState.startupError === 'page-error') {
      if (workspaceState.pageErrorSource !== 'unattributed') {
        throw new Error(
          `Split-role application root did not mount after a ${workspaceState.pageErrorSource} page error.`,
        );
      }
      throw new Error(
        'Split-role application root did not mount after an unattributed page error.',
      );
    }
    throw new Error('Split-role application root did not mount.');
  }
  if (workspaceState.startupOverlayVisible) {
    throw new Error('Split-role route retained the startup overlay.');
  }
  if (!workspaceState.isAdministratorsRoute) {
    if (workspaceState.isAdministratorAccessRootRoute) {
      throw new Error(
        'Split-role Administrator Access route did not redirect to Administrators.',
      );
    }
    if (workspaceState.isGameConfigRoute) {
      throw new Error('Split-role route returned to Game configuration.');
    }
    if (workspaceState.isLoginRoute) {
      throw new Error('Split-role route returned to the login page.');
    }
    throw new Error('Split-role route did not resolve to Administrators.');
  }
  if (!workspaceState.protectedHeadingVisible) {
    throw new Error(
      'Split-role Administrators heading did not become visible.',
    );
  }
  if (!workspaceState.protectedTableVisible) {
    throw new Error('Split-role Administrators table did not become visible.');
  }
  if (workspaceState.loadingMaskVisible) {
    throw new Error('Split-role Administrators table did not finish loading.');
  }

  throw new Error('Split-role Administrators workspace did not become ready.');
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
  await assertPersistedAccessStateCleared(page);
  const usernameField = formField(page, 'login-username');
  const passwordField = formField(page, 'login-password');
  const submitButton = loginAction(page);
  await expect(usernameField).toBeVisible({ timeout: loginReadyTimeoutMs });
  await expect(usernameField).toBeEnabled();
  await expect(passwordField).toBeVisible({ timeout: loginReadyTimeoutMs });
  await expect(passwordField).toBeEnabled();
  await expect(submitButton).toBeVisible({ timeout: loginReadyTimeoutMs });
  await expect(submitButton).toBeEnabled();
}

async function openWorkspace(
  page: Page,
  path: string,
  heading: string,
  tableTestId: string,
  onNavigationCommitted?: () => void,
): Promise<void> {
  if (new URL(page.url()).pathname !== path) {
    await page.goto(path, { timeout: 60_000, waitUntil: 'commit' });
    onNavigationCommitted?.();
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

async function readBootstrapWorkspaceState(
  page: Page,
): Promise<BootstrapWorkspaceState> {
  const [persistedPermissions, fallbackSurfaceVisible] = await Promise.all([
    page.evaluate(
      ({ access, administratorsRead }) => {
        for (let index = 0; index < localStorage.length; index += 1) {
          const key = localStorage.key(index);
          if (!key?.endsWith('-core-access')) {
            continue;
          }

          const serializedState = localStorage.getItem(key);
          if (!serializedState) {
            continue;
          }

          try {
            const state = JSON.parse(serializedState) as {
              accessCodes?: unknown;
            };
            const accessCodes = Array.isArray(state.accessCodes)
              ? state.accessCodes
              : [];
            return {
              hasAccessPermission: accessCodes.includes(access),
              hasAdministratorsReadPermission:
                accessCodes.includes(administratorsRead),
            };
          } catch {
            return {
              hasAccessPermission: false,
              hasAdministratorsReadPermission: false,
            };
          }
        }

        return {
          hasAccessPermission: false,
          hasAdministratorsReadPermission: false,
        };
      },
      {
        access: accessPermission,
        administratorsRead: administratorsReadPermission,
      },
    ),
    page.locator('svg[viewBox="0 0 860 571"]').isVisible(),
  ]);

  return { fallbackSurfaceVisible, ...persistedPermissions };
}

function throwBootstrapWorkspaceFailure(
  accessCodesStatus: number | undefined,
  userInfoStatus: number | undefined,
  workspaceState: BootstrapWorkspaceState,
): never {
  if (accessCodesStatus === undefined) {
    throw new Error('Bootstrap access-code response was not observed.');
  }
  if (accessCodesStatus === 401) {
    throw new Error('Bootstrap access-code response was unauthorized.');
  }
  if (accessCodesStatus === 403) {
    throw new Error('Bootstrap access-code response was forbidden.');
  }
  if (accessCodesStatus !== 200) {
    throw new Error('Bootstrap access-code response did not succeed.');
  }
  if (userInfoStatus === undefined) {
    throw new Error('Bootstrap user-information response was not observed.');
  }
  if (userInfoStatus === 401) {
    throw new Error('Bootstrap user-information response was unauthorized.');
  }
  if (userInfoStatus === 403) {
    throw new Error('Bootstrap user-information response was forbidden.');
  }
  if (userInfoStatus !== 200) {
    throw new Error('Bootstrap user-information response did not succeed.');
  }
  if (!workspaceState.hasAccessPermission) {
    throw new Error('Bootstrap persisted state omitted Administrator Access.');
  }
  if (!workspaceState.hasAdministratorsReadPermission) {
    throw new Error(
      'Bootstrap persisted state omitted Administrator Read permission.',
    );
  }
  if (workspaceState.fallbackSurfaceVisible) {
    throw new Error(
      'Bootstrap authorized route rendered the fallback surface.',
    );
  }

  throw new Error('Bootstrap authenticated workspace did not become ready.');
}

async function openLifecycleAdministratorWorkspace(
  page: Page,
  phase: 'bootstrap' | 'secondary',
): Promise<void> {
  let accessCodesStatus: number | undefined;
  let userInfoStatus: number | undefined;
  const observeStartupResponse = (response: Response) => {
    if (response.request().method() !== 'GET') {
      return;
    }

    const path = new URL(response.url()).pathname;
    if (path === '/api/auth/codes') {
      accessCodesStatus = response.status();
    } else if (path === '/api/user/info') {
      userInfoStatus = response.status();
    }
  };
  page.on('response', observeStartupResponse);

  try {
    await openWorkspace(
      page,
      '/admin-access/administrators',
      'Administrators',
      'administrator-table',
    );
  } catch {
    if (phase === 'bootstrap') {
      throwBootstrapWorkspaceFailure(
        accessCodesStatus,
        userInfoStatus,
        await readBootstrapWorkspaceState(page),
      );
    }

    throw new Error('Lifecycle secondary workspace did not become ready.');
  } finally {
    page.off('response', observeStartupResponse);
  }
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

  let bootstrapAccessToken: string;
  let bootstrapLoginSubmissionState: LoginSubmissionState | undefined;
  let bootstrapLoginObservationArmed = false;
  const bootstrapLoginResponseState = createBootstrapLoginResponseState();
  const bootstrapLoginRequests = new WeakMap<
    Request,
    BootstrapLoginEndpointState
  >();
  const observeBootstrapLoginRequest = (request: Request) => {
    if (!bootstrapLoginObservationArmed) {
      return;
    }

    const endpointState = getBootstrapLoginEndpointState(
      bootstrapLoginResponseState,
      request,
    );
    if (!endpointState) {
      return;
    }

    endpointState.requestStarted = true;
    bootstrapLoginRequests.set(request, endpointState);
  };
  const observeBootstrapLoginResponse = (response: Response) => {
    const endpointState = bootstrapLoginRequests.get(response.request());
    if (endpointState) {
      endpointState.status = response.status();
    }
  };
  const observeBootstrapLoginRequestFailure = (request: Request) => {
    const endpointState = bootstrapLoginRequests.get(request);
    if (endpointState) {
      endpointState.requestFailed = true;
    }
  };
  const observeBootstrapLoginPageError = () => {
    if (bootstrapLoginObservationArmed) {
      bootstrapLoginResponseState.pageError = true;
    }
  };
  page.on('request', observeBootstrapLoginRequest);
  page.on('response', observeBootstrapLoginResponse);
  page.on('requestfailed', observeBootstrapLoginRequestFailure);
  page.on('pageerror', observeBootstrapLoginPageError);
  try {
    try {
      bootstrapAccessToken = await login(
        page,
        bootstrapUserName,
        bootstrapPassword,
        acceptanceEvidence,
        undefined,
        undefined,
        undefined,
        (state) => {
          bootstrapLoginSubmissionState = state;
          bootstrapLoginObservationArmed = true;
        },
      );
    } catch {
      throwBootstrapLoginFailure(
        bootstrapLoginResponseState,
        await readBootstrapLoginState(page),
        bootstrapLoginSubmissionState,
      );
    }
  } finally {
    bootstrapLoginObservationArmed = false;
    page.off('pageerror', observeBootstrapLoginPageError);
    page.off('requestfailed', observeBootstrapLoginRequestFailure);
    page.off('response', observeBootstrapLoginResponse);
    page.off('request', observeBootstrapLoginRequest);
  }
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

  await openLifecycleAdministratorWorkspace(page, 'bootstrap');
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
  try {
    await login(
      secondaryPage,
      userName,
      administratorPassword,
      acceptanceEvidence,
      false,
    );
  } catch {
    throw new Error(
      'Lifecycle secondary administrator login did not complete.',
    );
  }
  await openLifecycleAdministratorWorkspace(secondaryPage, 'secondary');
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

  try {
    await login(
      secondaryPage,
      userName,
      resetPassword,
      acceptanceEvidence,
      false,
      administratorsLoginDestination,
      true,
    );
  } catch {
    throw new Error('Lifecycle reset-password login did not complete.');
  }
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
  test.setTimeout(900_000);

  const apiBaseURL = requireEnvironment('NOVAGAME_E2E_API_URL');
  const bootstrapPassword = requireEnvironment('NOVAGAME_E2E_PASSWORD');
  const bootstrapUserName = requireEnvironment('NOVAGAME_E2E_USERNAME');
  const runId = crypto.randomUUID();
  const accessOnlyPassword = `Access-only-${runId}-Password!`;
  const leafOnlyPassword = `Leaf-only-${runId}-Password!`;
  const permissionProbePassword = `Permission-probe-${runId}-Password!`;
  const resetOnlyPassword = `Reset-only-${runId}-Password!`;
  const sessionsReadOnlyPassword = `Sessions-read-only-${runId}-Password!`;
  const splitRolePassword = `Split-role-${runId}-Password!`;
  const viewerPassword = `Viewer-${runId}-Password!`;
  const unauthenticatedResponse = await acceptanceEvidence.apiRequest(
    page.request,
    'GET',
    apiUrl(apiBaseURL, '/admin-access/administrators?Page=1&PageSize=20'),
  );
  await assertAuthorizationFailure(
    unauthenticatedResponse,
    acceptanceEvidence,
    401,
    'GET',
  );

  let bootstrapAccessToken: string;
  try {
    bootstrapAccessToken = await login(
      page,
      bootstrapUserName,
      bootstrapPassword,
      acceptanceEvidence,
    );
  } catch {
    throw new Error(
      'Roles and permissions bootstrap login did not reach Game configuration.',
    );
  }
  acceptanceEvidence.registerSensitiveValue(accessOnlyPassword);
  acceptanceEvidence.registerSensitiveValue(leafOnlyPassword);
  acceptanceEvidence.registerSensitiveValue(permissionProbePassword);
  acceptanceEvidence.registerSensitiveValue(resetOnlyPassword);
  acceptanceEvidence.registerSensitiveValue(sessionsReadOnlyPassword);
  acceptanceEvidence.registerSensitiveValue(splitRolePassword);
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

  try {
    await openWorkspace(
      page,
      '/admin-access/roles',
      'Roles and permissions',
      'role-table',
    );
  } catch {
    throw new Error('Roles bootstrap workspace did not become ready.');
  }
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
  let accessOnlyAccessToken: string;
  try {
    accessOnlyAccessToken = await login(
      page,
      accessOnlyAdministrator.userName,
      accessOnlyPassword,
      acceptanceEvidence,
    );
  } catch {
    throw new Error('Access-only login did not reach Game configuration.');
  }
  await expect(
    page.getByText('Administrator Access', { exact: true }),
  ).toHaveCount(0);
  try {
    await assertManagementRouteFallsBack(
      page,
      '/admin-access/administrators',
      'Administrators',
      'administrator-table',
    );
  } catch {
    throw new Error('Access-only administrators route did not fall back.');
  }
  try {
    await assertManagementRouteFallsBack(
      page,
      '/admin-access/roles',
      'Roles and permissions',
      'role-table',
    );
  } catch {
    throw new Error('Access-only roles route did not fall back.');
  }
  try {
    await assertManagementRouteFallsBack(
      page,
      '/admin-access/sessions',
      'Sessions',
      'session-table',
    );
  } catch {
    throw new Error('Access-only sessions route did not fall back.');
  }
  let accessOnlyAuditRouteFallbackArmed = false;
  let accessOnlyAuditRouteTargetDocumentActive = false;
  let accessOnlyAuditRouteTargetDocumentEverCommitted = false;
  let accessOnlyAuditRouteStartupError: SplitRoleStartupError = 'none';
  const observeAccessOnlyAuditRouteFrameNavigation = (frame: Frame) => {
    if (!accessOnlyAuditRouteFallbackArmed || frame !== page.mainFrame()) {
      return;
    }

    accessOnlyAuditRouteTargetDocumentActive = false;
    if (new URL(frame.url()).pathname === '/admin-access/audit') {
      accessOnlyAuditRouteTargetDocumentEverCommitted = true;
      accessOnlyAuditRouteTargetDocumentActive = true;
      accessOnlyAuditRouteStartupError = 'none';
    }
  };
  const observeAccessOnlyAuditRoutePageError = (error: Error) => {
    if (
      !accessOnlyAuditRouteFallbackArmed ||
      !accessOnlyAuditRouteTargetDocumentActive
    ) {
      return;
    }

    if (error.message.includes('Failed to fetch dynamically imported module')) {
      accessOnlyAuditRouteStartupError = 'dynamic-module-fetch';
    } else if (accessOnlyAuditRouteStartupError === 'none') {
      accessOnlyAuditRouteStartupError = 'page-error';
    }
  };
  page.on('framenavigated', observeAccessOnlyAuditRouteFrameNavigation);
  page.on('pageerror', observeAccessOnlyAuditRoutePageError);
  try {
    accessOnlyAuditRouteFallbackArmed = true;
    accessOnlyAuditRouteTargetDocumentActive = false;
    accessOnlyAuditRouteTargetDocumentEverCommitted = false;
    accessOnlyAuditRouteStartupError = 'none';
    await assertManagementRouteFallsBack(
      page,
      '/admin-access/audit',
      'Audit history',
      'audit-table',
    );
  } catch {
    throwAccessOnlyAuditRouteFallbackFailure(
      await readAccessOnlyAuditRouteFallbackState(
        page,
        accessOnlyAuditRouteTargetDocumentEverCommitted,
        accessOnlyAuditRouteStartupError,
      ),
    );
  } finally {
    accessOnlyAuditRouteFallbackArmed = false;
    accessOnlyAuditRouteTargetDocumentActive = false;
    page.off('pageerror', observeAccessOnlyAuditRoutePageError);
    page.off('framenavigated', observeAccessOnlyAuditRouteFrameNavigation);
  }
  const accessOnlyGameConfigEpoch = createRolePermissionApiWaitEpoch();
  const accessOnlyGameConfigStartupResponses =
    waitForGameConfigStartupResponses(
      page,
      {
        drafts: 'access-only-game-config-drafts',
        environment: 'access-only-game-config-environment',
        versions: 'access-only-game-config-versions',
      },
      accessOnlyGameConfigEpoch,
    );
  let accessOnlyGameConfigPageError = false;
  const observeAccessOnlyGameConfigPageError = () => {
    accessOnlyGameConfigPageError = true;
  };
  page.on('pageerror', observeAccessOnlyGameConfigPageError);
  try {
    beginRolePermissionApiWaitEpoch(accessOnlyGameConfigEpoch);
    await page.goto('/game-config', {
      timeout: loginReadyTimeoutMs,
      waitUntil: 'commit',
    });
    await expect(
      page.getByRole('heading', { name: 'Game configuration' }),
    ).toBeVisible({ timeout: 60_000 });
    await auditGameConfigStartupResponses(
      accessOnlyGameConfigStartupResponses,
      acceptanceEvidence,
    );
  } catch {
    await throwAccessOnlyGameConfigStartupFailure(
      page,
      accessOnlyGameConfigPageError,
    );
  } finally {
    page.off('pageerror', observeAccessOnlyGameConfigPageError);
  }

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
    await assertAuthorizationFailure(
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
    {
      accessCodes: 'leaf-only-access-codes',
      login: 'leaf-only-login',
      userInfo: 'leaf-only-user-info',
    },
  );
  const missingAccessResponse = await acceptanceEvidence.apiRequest(
    page.request,
    'GET',
    apiUrl(apiBaseURL, '/admin-access/administrators?page=1&pageSize=20'),
    { headers: authorizationHeaders(leafOnlyAccessToken) },
  );
  await assertAuthorizationFailure(
    missingAccessResponse,
    acceptanceEvidence,
    403,
    'GET',
  );
  let leafOnlyDirectRouteStartupError: SplitRoleStartupError = 'none';
  let leafOnlyTargetDocumentCommitted = false;
  let leafOnlyDirectRouteModuleRequests = new WeakSet<Request>();
  const leafOnlyDirectRouteResponseState: Pick<
    SplitRoleLoginResponseState,
    'moduleFailure'
  > = {};
  const observeLeafOnlyDirectRouteRequest = (request: Request) => {
    if (
      !leafOnlyTargetDocumentCommitted ||
      request.frame() !== page.mainFrame()
    ) {
      return;
    }

    if (classifySplitRoleModuleResource(request, page)) {
      leafOnlyDirectRouteModuleRequests.add(request);
    }
  };
  const observeLeafOnlyDirectRouteFrameNavigation = (frame: Frame) => {
    if (
      frame !== page.mainFrame() ||
      new URL(frame.url()).pathname !== '/admin-access/administrators'
    ) {
      return;
    }

    leafOnlyDirectRouteModuleRequests = new WeakSet<Request>();
    leafOnlyDirectRouteResponseState.moduleFailure = undefined;
    leafOnlyTargetDocumentCommitted = true;
    leafOnlyDirectRouteStartupError = 'none';
  };
  const observeLeafOnlyDirectRouteResponse = (response: Response) => {
    const moduleRequest = response.request();
    const moduleResource = leafOnlyDirectRouteModuleRequests.has(moduleRequest)
      ? classifySplitRoleModuleResource(moduleRequest, page)
      : null;
    const moduleFailureOutcome = classifySplitRoleModuleFailureOutcome(
      response.status(),
    );
    if (moduleResource && moduleFailureOutcome) {
      recordSplitRoleModuleFailure(
        leafOnlyDirectRouteResponseState,
        createSplitRoleModuleFailure(
          moduleRequest,
          moduleFailureOutcome,
          moduleResource,
        ),
      );
    }
  };
  const observeLeafOnlyDirectRouteModuleRequestFailure = (request: Request) => {
    const moduleResource = leafOnlyDirectRouteModuleRequests.has(request)
      ? classifySplitRoleModuleResource(request, page)
      : null;
    if (moduleResource) {
      recordSplitRoleModuleFailure(
        leafOnlyDirectRouteResponseState,
        createSplitRoleModuleFailure(
          request,
          classifySplitRoleModuleRequestFailure(request),
          moduleResource,
        ),
      );
    }
  };
  const observeLeafOnlyDirectRouteError = (error: Error) => {
    if (!leafOnlyTargetDocumentCommitted) {
      return;
    }

    if (error.message.includes('Failed to fetch dynamically imported module')) {
      leafOnlyDirectRouteStartupError = 'dynamic-module-fetch';
    } else if (leafOnlyDirectRouteStartupError === 'none') {
      leafOnlyDirectRouteStartupError = 'page-error';
    }
  };
  page.on('framenavigated', observeLeafOnlyDirectRouteFrameNavigation);
  page.on('request', observeLeafOnlyDirectRouteRequest);
  page.on('response', observeLeafOnlyDirectRouteResponse);
  page.on('requestfailed', observeLeafOnlyDirectRouteModuleRequestFailure);
  page.on('pageerror', observeLeafOnlyDirectRouteError);
  try {
    try {
      await page.goto('/admin-access/administrators', {
        timeout: loginReadyTimeoutMs,
        waitUntil: 'commit',
      });
      await expect(formField(page, 'login-username')).toBeVisible({
        timeout: loginReadyTimeoutMs,
      });
      await expect(page).toHaveURL(/\/auth\/login(?:[?#].*)?$/);
    } catch {
      throwLeafOnlyDirectRouteFailure(
        await readLeafOnlyDirectRouteState(
          page,
          leafOnlyDirectRouteStartupError,
          leafOnlyDirectRouteResponseState.moduleFailure,
        ),
      );
    }
  } finally {
    page.off('pageerror', observeLeafOnlyDirectRouteError);
    page.off('requestfailed', observeLeafOnlyDirectRouteModuleRequestFailure);
    page.off('response', observeLeafOnlyDirectRouteResponse);
    page.off('request', observeLeafOnlyDirectRouteRequest);
    page.off('framenavigated', observeLeafOnlyDirectRouteFrameNavigation);
  }

  const splitRoleAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/administrators'),
      {
        data: {
          displayName: 'Split-role administrator viewer',
          initialPassword: splitRolePassword,
          roleIds: [accessOnlyRole.id, leafOnlyRole.id],
          userName: `e2e-split-role-${runId}`,
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
  await expect(page).toHaveURL(/\/auth\/login(?:[?#].*)?$/);
  await assertPersistedAccessStateCleared(page);
  let splitRoleLoginSubmissionState: LoginSubmissionState | undefined;
  const splitRoleResponseState: SplitRoleLoginResponseState = {
    loginRequestFailed: false,
    loginRequestStarted: false,
  };
  let splitRoleStartupError: SplitRoleStartupError = 'none';
  let splitRoleInitialLoginReadinessArmed = false;
  let splitRoleInitialLoginTargetDocumentCommitted = false;
  let splitRoleInitialLoginTargetDocumentGeneration = 0;
  let splitRoleInitialLoginTargetDocumentPhase: SplitRoleInitialLoginTargetDocumentPhase =
    'target-not-committed';
  let splitRoleInitialLoginContextClosed = false;
  const splitRoleInitialLoginContext = page.context();
  const splitRoleInitialLoginModuleRequests = new Set<Request>();
  const splitRoleInitialLoginResponseState: SplitRoleInitialLoginResponseState =
    {};
  const isSplitRoleInitialLoginBareRoute = (url: string) => {
    const parsedUrl = new URL(url);

    return (
      parsedUrl.pathname === '/auth/login' &&
      parsedUrl.search.length === 0 &&
      parsedUrl.hash.length === 0
    );
  };
  const readSplitRoleInitialLoginTargetDocumentObservation =
    (): SplitRoleInitialLoginTargetDocumentObservation => ({
      contextClosed: splitRoleInitialLoginContextClosed,
      generation: splitRoleInitialLoginTargetDocumentGeneration,
      phase: splitRoleInitialLoginTargetDocumentPhase,
    });
  const observeSplitRoleInitialLoginContextClose = () => {
    splitRoleInitialLoginContextClosed = true;
  };
  const isSplitRoleInitialLoginReadinessActive = () =>
    splitRoleInitialLoginReadinessArmed &&
    isSplitRoleInitialLoginBareRoute(page.url());
  const isSplitRoleInitialLoginTargetDocumentActive = () =>
    isSplitRoleInitialLoginReadinessActive() &&
    splitRoleInitialLoginTargetDocumentPhase === 'bare-target-active' &&
    splitRoleInitialLoginTargetDocumentCommitted;
  const recordSplitRoleInitialLoginPendingModule = () => {
    if (splitRoleInitialLoginResponseState.pendingModuleResource) {
      return;
    }

    for (const request of splitRoleInitialLoginModuleRequests) {
      const moduleResource = classifySplitRoleModuleResource(request, page);
      if (moduleResource) {
        splitRoleInitialLoginResponseState.pendingModuleResource =
          moduleResource;
        return;
      }
    }
  };
  const observeSplitRoleRequest = (request: Request) => {
    if (isSplitRoleLoginRequest(request)) {
      splitRoleResponseState.loginRequestStarted = true;
    }

    if (
      splitRoleInitialLoginReadinessArmed &&
      request.frame() === page.mainFrame() &&
      request.isNavigationRequest()
    ) {
      splitRoleInitialLoginTargetDocumentCommitted = false;
      splitRoleInitialLoginTargetDocumentGeneration += 1;
      splitRoleInitialLoginTargetDocumentPhase = 'navigation-pending';
      splitRoleInitialLoginModuleRequests.clear();
      return;
    }

    if (
      isSplitRoleInitialLoginTargetDocumentActive() &&
      request.frame() === page.mainFrame() &&
      classifySplitRoleModuleResource(request, page)
    ) {
      splitRoleInitialLoginModuleRequests.add(request);
    }
  };
  const observeSplitRoleInitialLoginFrameNavigation = (frame: Frame) => {
    if (frame !== page.mainFrame()) {
      return;
    }

    if (!splitRoleInitialLoginReadinessArmed) {
      return;
    }

    splitRoleInitialLoginTargetDocumentCommitted = false;
    splitRoleInitialLoginTargetDocumentGeneration += 1;
    splitRoleInitialLoginModuleRequests.clear();
    splitRoleInitialLoginResponseState.moduleFailure = undefined;
    splitRoleStartupError = 'none';
    if (isSplitRoleInitialLoginBareRoute(frame.url())) {
      splitRoleInitialLoginTargetDocumentCommitted = true;
      splitRoleInitialLoginTargetDocumentPhase = 'bare-target-active';
    } else {
      splitRoleInitialLoginTargetDocumentPhase = 'other-document-committed';
    }
  };
  const observeSplitRoleAuthenticationResponse = (response: Response) => {
    const responseRequest = response.request();
    const path = new URL(response.url()).pathname;
    if (splitRoleInitialLoginModuleRequests.has(responseRequest)) {
      splitRoleInitialLoginModuleRequests.delete(responseRequest);
      const moduleResource = classifySplitRoleModuleResource(
        responseRequest,
        page,
      );
      const moduleFailureOutcome = classifySplitRoleModuleFailureOutcome(
        response.status(),
      );
      if (moduleResource && moduleFailureOutcome) {
        recordSplitRoleModuleFailure(
          splitRoleInitialLoginResponseState,
          createSplitRoleModuleFailure(
            responseRequest,
            moduleFailureOutcome,
            moduleResource,
          ),
        );
      }
    }
    if (responseRequest.method() === 'POST' && path === '/api/auth/login') {
      splitRoleResponseState.loginStatus = response.status();
      return;
    }
    if (responseRequest.method() !== 'GET') {
      return;
    }

    switch (path) {
      case '/api/auth/codes': {
        splitRoleResponseState.accessCodesStatus = response.status();
        break;
      }
      case '/api/game-config/drafts': {
        splitRoleResponseState.gameConfigDraftsStatus = response.status();
        break;
      }
      case '/api/game-config/environments/development': {
        splitRoleResponseState.gameConfigEnvironmentStatus = response.status();
        break;
      }
      case '/api/game-config/versions': {
        splitRoleResponseState.gameConfigVersionsStatus = response.status();
        break;
      }
      case '/api/user/info': {
        splitRoleResponseState.userInfoStatus = response.status();
        break;
      }
    }
  };
  const observeSplitRoleRequestFailure = (request: Request) => {
    if (isSplitRoleLoginRequest(request)) {
      splitRoleResponseState.loginRequestFailed = true;
      return;
    }

    if (splitRoleInitialLoginModuleRequests.has(request)) {
      splitRoleInitialLoginModuleRequests.delete(request);
      const moduleResource = classifySplitRoleModuleResource(request, page);
      if (moduleResource) {
        recordSplitRoleModuleFailure(
          splitRoleInitialLoginResponseState,
          createSplitRoleModuleFailure(
            request,
            classifySplitRoleModuleRequestFailure(request),
            moduleResource,
          ),
        );
      }
    }
  };
  const observeSplitRoleStartupError = (error: Error) => {
    if (!isSplitRoleInitialLoginTargetDocumentActive()) {
      return;
    }

    if (error.message.includes('Failed to fetch dynamically imported module')) {
      splitRoleStartupError = 'dynamic-module-fetch';
    } else if (splitRoleStartupError === 'none') {
      splitRoleStartupError = 'page-error';
    }
  };
  splitRoleInitialLoginContext.on(
    'close',
    observeSplitRoleInitialLoginContextClose,
  );
  page.on('request', observeSplitRoleRequest);
  page.on('response', observeSplitRoleAuthenticationResponse);
  page.on('requestfailed', observeSplitRoleRequestFailure);
  page.on('framenavigated', observeSplitRoleInitialLoginFrameNavigation);
  page.on('pageerror', observeSplitRoleStartupError);
  try {
    splitRoleInitialLoginReadinessArmed = true;
    splitRoleInitialLoginTargetDocumentCommitted =
      isSplitRoleInitialLoginBareRoute(page.url());
    splitRoleInitialLoginTargetDocumentPhase =
      splitRoleInitialLoginTargetDocumentCommitted
        ? 'bare-target-active'
        : 'other-document-committed';
    splitRoleInitialLoginTargetDocumentGeneration = 0;
    splitRoleInitialLoginContextClosed = false;
    splitRoleInitialLoginModuleRequests.clear();
    splitRoleInitialLoginResponseState.moduleFailure = undefined;
    splitRoleStartupError = 'none';
    try {
      const splitRoleLoginUrl = new URL(page.url());
      if (!isSplitRoleInitialLoginBareRoute(splitRoleLoginUrl.toString())) {
        await page.goto('/auth/login', {
          timeout: loginReadyTimeoutMs,
          waitUntil: 'commit',
        });
      }
      await expect(formField(page, 'login-username')).toBeVisible({
        timeout: loginReadyTimeoutMs,
      });
    } catch {
      recordSplitRoleInitialLoginPendingModule();
      throwSplitRoleLoginFailure(
        splitRoleResponseState,
        await readSplitRoleLoginState(
          page,
          splitRoleStartupError,
          readSplitRoleInitialLoginTargetDocumentObservation,
        ),
        splitRoleLoginSubmissionState,
        splitRoleInitialLoginResponseState,
      );
    }
    try {
      await login(
        page,
        splitRoleAdministrator.userName,
        splitRolePassword,
        acceptanceEvidence,
        undefined,
        undefined,
        undefined,
        (state) => {
          splitRoleLoginSubmissionState = state;
          splitRoleInitialLoginReadinessArmed = false;
          splitRoleInitialLoginModuleRequests.clear();
        },
      );
    } catch {
      recordSplitRoleInitialLoginPendingModule();
      throwSplitRoleLoginFailure(
        splitRoleResponseState,
        await readSplitRoleLoginState(
          page,
          splitRoleStartupError,
          readSplitRoleInitialLoginTargetDocumentObservation,
        ),
        splitRoleLoginSubmissionState,
        splitRoleInitialLoginResponseState,
      );
    } finally {
      splitRoleInitialLoginReadinessArmed = false;
      splitRoleInitialLoginTargetDocumentCommitted = false;
      splitRoleInitialLoginModuleRequests.clear();
    }
    try {
      await assertPersistedAccessPermission(page, accessPermission, true);
    } catch {
      throw new Error(
        'Split-role persisted state did not retain Administrator Access.',
      );
    }
    try {
      await assertPersistedAccessPermission(
        page,
        administratorsReadPermission,
        true,
      );
    } catch {
      throw new Error(
        'Split-role persisted state did not retain Administrator Read permission.',
      );
    }
    try {
      await assertPersistedAccessPermission(page, rolesReadPermission, false);
    } catch {
      throw new Error(
        'Split-role persisted state retained Roles Read permission.',
      );
    }
    await expect(
      page.getByText('Administrator Access', { exact: true }),
    ).toBeVisible();
  } finally {
    splitRoleInitialLoginReadinessArmed = false;
    splitRoleInitialLoginTargetDocumentCommitted = false;
    splitRoleInitialLoginTargetDocumentPhase = 'target-not-committed';
    splitRoleInitialLoginModuleRequests.clear();
    page.off('pageerror', observeSplitRoleStartupError);
    page.off('framenavigated', observeSplitRoleInitialLoginFrameNavigation);
    page.off('requestfailed', observeSplitRoleRequestFailure);
    page.off('response', observeSplitRoleAuthenticationResponse);
    page.off('request', observeSplitRoleRequest);
    splitRoleInitialLoginContext.off(
      'close',
      observeSplitRoleInitialLoginContextClose,
    );
  }
  const splitRoleAdministratorsWorkspacePage = await page.context().newPage();
  const splitRoleAdministratorsWorkspaceResponseState: Pick<
    SplitRoleLoginResponseState,
    'moduleFailure'
  > = {};
  let splitRoleAdministratorsWorkspaceNavigationArmed = false;
  let splitRoleAdministratorsWorkspaceTargetDocumentActive = false;
  let splitRoleAdministratorsWorkspaceTargetDocumentEverCommitted = false;
  let splitRoleAdministratorsWorkspaceCreateCommandState: SplitRoleAdministratorsCreateCommandState =
    'not-observed';
  const splitRoleAdministratorsWorkspaceModuleRequests = new Set<Request>();
  let splitRoleAdministratorsWorkspaceStartupError: SplitRoleStartupError =
    'none';
  let splitRoleAdministratorsWorkspacePageErrorSource: SplitRoleWorkspacePageErrorSource =
    'unattributed';
  let splitRoleAdministratorsWorkspaceGotoCommitted = false;
  const isSplitRoleAdministratorsWorkspaceTargetPath = (url: string) => {
    const pathname = new URL(url).pathname;
    return (
      pathname === '/admin-access' ||
      pathname === '/admin-access/administrators'
    );
  };
  const isSplitRoleAdministratorsWorkspaceTargetActive = () =>
    splitRoleAdministratorsWorkspaceNavigationArmed &&
    splitRoleAdministratorsWorkspaceTargetDocumentActive;
  const observeSplitRoleAdministratorsWorkspaceRequest = (request: Request) => {
    if (
      !isSplitRoleAdministratorsWorkspaceTargetActive() ||
      request.frame() !== splitRoleAdministratorsWorkspacePage.mainFrame()
    ) {
      return;
    }

    if (request.isNavigationRequest()) {
      splitRoleAdministratorsWorkspaceTargetDocumentActive = false;
      splitRoleAdministratorsWorkspaceModuleRequests.clear();
      return;
    }

    if (
      classifySplitRoleModuleResource(
        request,
        splitRoleAdministratorsWorkspacePage,
      )
    ) {
      splitRoleAdministratorsWorkspaceModuleRequests.add(request);
    }
  };
  const observeSplitRoleAdministratorsWorkspaceFrameNavigation = (
    frame: Frame,
  ) => {
    if (
      !splitRoleAdministratorsWorkspaceNavigationArmed ||
      frame !== splitRoleAdministratorsWorkspacePage.mainFrame()
    ) {
      return;
    }

    splitRoleAdministratorsWorkspaceModuleRequests.clear();
    splitRoleAdministratorsWorkspaceResponseState.moduleFailure = undefined;
    splitRoleAdministratorsWorkspaceCreateCommandState = 'not-observed';
    splitRoleAdministratorsWorkspaceStartupError = 'none';
    splitRoleAdministratorsWorkspacePageErrorSource = 'unattributed';
    splitRoleAdministratorsWorkspaceTargetDocumentActive = false;
    if (!isSplitRoleAdministratorsWorkspaceTargetPath(frame.url())) {
      return;
    }

    splitRoleAdministratorsWorkspaceTargetDocumentEverCommitted = true;
    splitRoleAdministratorsWorkspaceTargetDocumentActive = true;
  };
  const observeSplitRoleAdministratorsWorkspaceResponse = (
    response: Response,
  ) => {
    if (!isSplitRoleAdministratorsWorkspaceTargetActive()) {
      return;
    }

    const request = response.request();
    const moduleResource = splitRoleAdministratorsWorkspaceModuleRequests.has(
      request,
    )
      ? classifySplitRoleModuleResource(
          request,
          splitRoleAdministratorsWorkspacePage,
        )
      : null;
    const moduleFailureOutcome = classifySplitRoleModuleFailureOutcome(
      response.status(),
    );
    if (moduleResource && moduleFailureOutcome) {
      recordSplitRoleModuleFailure(
        splitRoleAdministratorsWorkspaceResponseState,
        createSplitRoleModuleFailure(
          request,
          moduleFailureOutcome,
          moduleResource,
        ),
      );
    }
  };
  const observeSplitRoleAdministratorsWorkspaceRequestFailure = (
    request: Request,
  ) => {
    if (!isSplitRoleAdministratorsWorkspaceTargetActive()) {
      return;
    }

    const moduleResource = splitRoleAdministratorsWorkspaceModuleRequests.has(
      request,
    )
      ? classifySplitRoleModuleResource(
          request,
          splitRoleAdministratorsWorkspacePage,
        )
      : null;
    if (moduleResource) {
      recordSplitRoleModuleFailure(
        splitRoleAdministratorsWorkspaceResponseState,
        createSplitRoleModuleFailure(
          request,
          classifySplitRoleModuleRequestFailure(request),
          moduleResource,
        ),
      );
    }
    splitRoleAdministratorsWorkspaceModuleRequests.delete(request);
  };
  const observeSplitRoleAdministratorsWorkspaceRequestFinished = (
    request: Request,
  ) => {
    splitRoleAdministratorsWorkspaceModuleRequests.delete(request);
  };
  const observeSplitRoleAdministratorsWorkspacePageError = (error: Error) => {
    if (!isSplitRoleAdministratorsWorkspaceTargetActive()) {
      return;
    }

    if (splitRoleAdministratorsWorkspaceStartupError === 'none') {
      splitRoleAdministratorsWorkspaceStartupError = 'page-error';
      splitRoleAdministratorsWorkspacePageErrorSource =
        classifySplitRoleWorkspacePageErrorSource(error);
    }
  };
  splitRoleAdministratorsWorkspacePage.on(
    'request',
    observeSplitRoleAdministratorsWorkspaceRequest,
  );
  splitRoleAdministratorsWorkspacePage.on(
    'response',
    observeSplitRoleAdministratorsWorkspaceResponse,
  );
  splitRoleAdministratorsWorkspacePage.on(
    'requestfailed',
    observeSplitRoleAdministratorsWorkspaceRequestFailure,
  );
  splitRoleAdministratorsWorkspacePage.on(
    'requestfinished',
    observeSplitRoleAdministratorsWorkspaceRequestFinished,
  );
  splitRoleAdministratorsWorkspacePage.on(
    'framenavigated',
    observeSplitRoleAdministratorsWorkspaceFrameNavigation,
  );
  splitRoleAdministratorsWorkspacePage.on(
    'pageerror',
    observeSplitRoleAdministratorsWorkspacePageError,
  );
  try {
    splitRoleAdministratorsWorkspaceNavigationArmed = true;
    splitRoleAdministratorsWorkspaceTargetDocumentActive = false;
    splitRoleAdministratorsWorkspaceTargetDocumentEverCommitted = false;
    splitRoleAdministratorsWorkspaceCreateCommandState = 'not-observed';
    splitRoleAdministratorsWorkspaceModuleRequests.clear();
    splitRoleAdministratorsWorkspaceResponseState.moduleFailure = undefined;
    splitRoleAdministratorsWorkspaceStartupError = 'none';
    splitRoleAdministratorsWorkspacePageErrorSource = 'unattributed';
    await openWorkspace(
      splitRoleAdministratorsWorkspacePage,
      '/admin-access',
      'Administrators',
      'administrator-table',
      () => {
        splitRoleAdministratorsWorkspaceGotoCommitted = true;
      },
    );
    await expect(splitRoleAdministratorsWorkspacePage).toHaveURL(
      '/admin-access/administrators',
    );
    const createAdministratorCommand =
      splitRoleAdministratorsWorkspacePage.getByTestId('create-administrator');
    try {
      await expect(createAdministratorCommand).toHaveCount(0);
      splitRoleAdministratorsWorkspaceCreateCommandState = 'absent';
    } catch (error) {
      try {
        splitRoleAdministratorsWorkspaceCreateCommandState =
          (await createAdministratorCommand.count()) > 0
            ? 'present'
            : 'not-observed';
      } catch {
        splitRoleAdministratorsWorkspaceCreateCommandState = 'not-observed';
      }
      throw error;
    }
  } catch {
    if (!splitRoleAdministratorsWorkspaceTargetDocumentEverCommitted) {
      if (splitRoleAdministratorsWorkspaceGotoCommitted) {
        throw new Error(
          'Split-role Administrators workspace navigation committed but its target-frame observer did not observe the target document.',
        );
      }

      throw new Error(
        'Split-role Administrators workspace navigation did not commit.',
      );
    }

    throwSplitRoleAdministratorsWorkspaceFailure(
      await readSplitRoleAdministratorsWorkspaceState(
        splitRoleAdministratorsWorkspacePage,
        splitRoleAdministratorsWorkspaceCreateCommandState,
        splitRoleAdministratorsWorkspaceStartupError,
        splitRoleAdministratorsWorkspacePageErrorSource,
        splitRoleAdministratorsWorkspaceResponseState.moduleFailure,
      ),
    );
  } finally {
    splitRoleAdministratorsWorkspaceNavigationArmed = false;
    splitRoleAdministratorsWorkspaceTargetDocumentActive = false;
    splitRoleAdministratorsWorkspacePage.off(
      'pageerror',
      observeSplitRoleAdministratorsWorkspacePageError,
    );
    splitRoleAdministratorsWorkspacePage.off(
      'framenavigated',
      observeSplitRoleAdministratorsWorkspaceFrameNavigation,
    );
    splitRoleAdministratorsWorkspacePage.off(
      'requestfailed',
      observeSplitRoleAdministratorsWorkspaceRequestFailure,
    );
    splitRoleAdministratorsWorkspacePage.off(
      'requestfinished',
      observeSplitRoleAdministratorsWorkspaceRequestFinished,
    );
    splitRoleAdministratorsWorkspacePage.off(
      'response',
      observeSplitRoleAdministratorsWorkspaceResponse,
    );
    splitRoleAdministratorsWorkspacePage.off(
      'request',
      observeSplitRoleAdministratorsWorkspaceRequest,
    );
    await splitRoleAdministratorsWorkspacePage.close();
  }
  const splitRoleRolesFallbackPage = await page.context().newPage();
  const splitRoleRolesFallbackResponseState: Pick<
    SplitRoleLoginResponseState,
    'moduleFailure'
  > = {};
  let splitRoleRolesFallbackNavigationArmed = false;
  let splitRoleRolesFallbackTargetDocumentActive = false;
  let splitRoleRolesFallbackTargetDocumentEverCommitted = false;
  let splitRoleRolesFallbackModuleRequests = new WeakSet<Request>();
  let splitRoleRolesFallbackStartupError: SplitRoleStartupError = 'none';
  const isSplitRoleRolesFallbackTargetActive = () =>
    splitRoleRolesFallbackNavigationArmed &&
    splitRoleRolesFallbackTargetDocumentActive;
  const observeSplitRoleRolesFallbackRequest = (request: Request) => {
    if (
      !isSplitRoleRolesFallbackTargetActive() ||
      request.frame() !== splitRoleRolesFallbackPage.mainFrame()
    ) {
      return;
    }

    if (classifySplitRoleModuleResource(request, splitRoleRolesFallbackPage)) {
      splitRoleRolesFallbackModuleRequests.add(request);
    }
  };
  const observeSplitRoleRolesFallbackFrameNavigation = (frame: Frame) => {
    if (
      !splitRoleRolesFallbackNavigationArmed ||
      frame !== splitRoleRolesFallbackPage.mainFrame()
    ) {
      return;
    }

    splitRoleRolesFallbackModuleRequests = new WeakSet<Request>();
    splitRoleRolesFallbackResponseState.moduleFailure = undefined;
    splitRoleRolesFallbackStartupError = 'none';
    splitRoleRolesFallbackTargetDocumentActive = false;
    if (new URL(frame.url()).pathname === '/admin-access/roles') {
      splitRoleRolesFallbackTargetDocumentEverCommitted = true;
      splitRoleRolesFallbackTargetDocumentActive = true;
    }
  };
  const observeSplitRoleRolesFallbackResponse = (response: Response) => {
    if (!isSplitRoleRolesFallbackTargetActive()) {
      return;
    }

    const request = response.request();
    const moduleResource = splitRoleRolesFallbackModuleRequests.has(request)
      ? classifySplitRoleModuleResource(request, splitRoleRolesFallbackPage)
      : null;
    const moduleFailureOutcome = classifySplitRoleModuleFailureOutcome(
      response.status(),
    );
    if (moduleResource && moduleFailureOutcome) {
      recordSplitRoleModuleFailure(
        splitRoleRolesFallbackResponseState,
        createSplitRoleModuleFailure(
          request,
          moduleFailureOutcome,
          moduleResource,
        ),
      );
    }
  };
  const observeSplitRoleRolesFallbackRequestFailure = (request: Request) => {
    if (!isSplitRoleRolesFallbackTargetActive()) {
      return;
    }

    const moduleResource = splitRoleRolesFallbackModuleRequests.has(request)
      ? classifySplitRoleModuleResource(request, splitRoleRolesFallbackPage)
      : null;
    if (moduleResource) {
      recordSplitRoleModuleFailure(
        splitRoleRolesFallbackResponseState,
        createSplitRoleModuleFailure(
          request,
          classifySplitRoleModuleRequestFailure(request),
          moduleResource,
        ),
      );
    }
  };
  const observeSplitRoleRolesFallbackPageError = (error: Error) => {
    if (!isSplitRoleRolesFallbackTargetActive()) {
      return;
    }

    if (error.message.includes('Failed to fetch dynamically imported module')) {
      splitRoleRolesFallbackStartupError = 'dynamic-module-fetch';
    } else if (splitRoleRolesFallbackStartupError === 'none') {
      splitRoleRolesFallbackStartupError = 'page-error';
    }
  };
  splitRoleRolesFallbackPage.on(
    'request',
    observeSplitRoleRolesFallbackRequest,
  );
  splitRoleRolesFallbackPage.on(
    'response',
    observeSplitRoleRolesFallbackResponse,
  );
  splitRoleRolesFallbackPage.on(
    'requestfailed',
    observeSplitRoleRolesFallbackRequestFailure,
  );
  splitRoleRolesFallbackPage.on(
    'framenavigated',
    observeSplitRoleRolesFallbackFrameNavigation,
  );
  splitRoleRolesFallbackPage.on(
    'pageerror',
    observeSplitRoleRolesFallbackPageError,
  );
  try {
    splitRoleRolesFallbackNavigationArmed = true;
    splitRoleRolesFallbackTargetDocumentActive = false;
    splitRoleRolesFallbackTargetDocumentEverCommitted = false;
    splitRoleRolesFallbackModuleRequests = new WeakSet<Request>();
    splitRoleRolesFallbackResponseState.moduleFailure = undefined;
    splitRoleRolesFallbackStartupError = 'none';
    await splitRoleRolesFallbackPage.goto('/admin-access/roles', {
      timeout: 60_000,
      waitUntil: 'commit',
    });
    await assertManagementRouteFallsBack(
      splitRoleRolesFallbackPage,
      '/admin-access/roles',
      'Roles and permissions',
      'role-table',
    );
  } catch (error) {
    throwIfRolesPermissionsTestDeadline('Roles', error);
    throwSplitRoleRolesFallbackFailure(
      await readSplitRoleRolesFallbackState(
        splitRoleRolesFallbackPage,
        '/admin-access/roles',
        'Roles and permissions',
        'role-table',
        splitRoleRolesFallbackTargetDocumentEverCommitted,
        splitRoleRolesFallbackStartupError,
        splitRoleRolesFallbackResponseState.moduleFailure,
      ),
    );
  } finally {
    splitRoleRolesFallbackNavigationArmed = false;
    splitRoleRolesFallbackTargetDocumentActive = false;
    splitRoleRolesFallbackPage.off(
      'pageerror',
      observeSplitRoleRolesFallbackPageError,
    );
    splitRoleRolesFallbackPage.off(
      'framenavigated',
      observeSplitRoleRolesFallbackFrameNavigation,
    );
    splitRoleRolesFallbackPage.off(
      'requestfailed',
      observeSplitRoleRolesFallbackRequestFailure,
    );
    splitRoleRolesFallbackPage.off(
      'response',
      observeSplitRoleRolesFallbackResponse,
    );
    splitRoleRolesFallbackPage.off(
      'request',
      observeSplitRoleRolesFallbackRequest,
    );
    await splitRoleRolesFallbackPage.close();
  }
  const splitRoleSessionsRouteFallbackPage = await page.context().newPage();
  const splitRoleSessionsRouteFallbackResponseState: Pick<
    SplitRoleLoginResponseState,
    'moduleFailure'
  > = {};
  let splitRoleSessionsRouteFallbackArmed = false;
  let splitRoleSessionsRouteTargetDocumentActive = false;
  let splitRoleSessionsRouteTargetDocumentEverCommitted = false;
  let splitRoleSessionsRouteModuleRequests = new WeakSet<Request>();
  let splitRoleSessionsRouteStartupError: SplitRoleStartupError = 'none';
  const isSplitRoleSessionsRouteTargetActive = () =>
    splitRoleSessionsRouteFallbackArmed &&
    splitRoleSessionsRouteTargetDocumentActive;
  const observeSplitRoleSessionsRouteRequest = (request: Request) => {
    if (
      !isSplitRoleSessionsRouteTargetActive() ||
      request.frame() !== splitRoleSessionsRouteFallbackPage.mainFrame()
    ) {
      return;
    }

    if (
      classifySplitRoleModuleResource(
        request,
        splitRoleSessionsRouteFallbackPage,
      )
    ) {
      splitRoleSessionsRouteModuleRequests.add(request);
    }
  };
  const observeSplitRoleSessionsRouteFrameNavigation = (frame: Frame) => {
    if (
      !splitRoleSessionsRouteFallbackArmed ||
      frame !== splitRoleSessionsRouteFallbackPage.mainFrame()
    ) {
      return;
    }

    splitRoleSessionsRouteModuleRequests = new WeakSet<Request>();
    splitRoleSessionsRouteFallbackResponseState.moduleFailure = undefined;
    splitRoleSessionsRouteStartupError = 'none';
    splitRoleSessionsRouteTargetDocumentActive = false;
    if (new URL(frame.url()).pathname === '/admin-access/sessions') {
      splitRoleSessionsRouteTargetDocumentEverCommitted = true;
      splitRoleSessionsRouteTargetDocumentActive = true;
    }
  };
  const observeSplitRoleSessionsRouteResponse = (response: Response) => {
    if (!isSplitRoleSessionsRouteTargetActive()) {
      return;
    }

    const request = response.request();
    const moduleResource = splitRoleSessionsRouteModuleRequests.has(request)
      ? classifySplitRoleModuleResource(
          request,
          splitRoleSessionsRouteFallbackPage,
        )
      : null;
    const moduleFailureOutcome = classifySplitRoleModuleFailureOutcome(
      response.status(),
    );
    if (moduleResource && moduleFailureOutcome) {
      recordSplitRoleModuleFailure(
        splitRoleSessionsRouteFallbackResponseState,
        createSplitRoleModuleFailure(
          request,
          moduleFailureOutcome,
          moduleResource,
        ),
      );
    }
  };
  const observeSplitRoleSessionsRouteRequestFailure = (request: Request) => {
    if (!isSplitRoleSessionsRouteTargetActive()) {
      return;
    }

    const moduleResource = splitRoleSessionsRouteModuleRequests.has(request)
      ? classifySplitRoleModuleResource(
          request,
          splitRoleSessionsRouteFallbackPage,
        )
      : null;
    if (moduleResource) {
      recordSplitRoleModuleFailure(
        splitRoleSessionsRouteFallbackResponseState,
        createSplitRoleModuleFailure(
          request,
          classifySplitRoleModuleRequestFailure(request),
          moduleResource,
        ),
      );
    }
  };
  const observeSplitRoleSessionsRoutePageError = (error: Error) => {
    if (!isSplitRoleSessionsRouteTargetActive()) {
      return;
    }

    if (error.message.includes('Failed to fetch dynamically imported module')) {
      splitRoleSessionsRouteStartupError = 'dynamic-module-fetch';
    } else if (splitRoleSessionsRouteStartupError === 'none') {
      splitRoleSessionsRouteStartupError = 'page-error';
    }
  };
  splitRoleSessionsRouteFallbackPage.on(
    'request',
    observeSplitRoleSessionsRouteRequest,
  );
  splitRoleSessionsRouteFallbackPage.on(
    'response',
    observeSplitRoleSessionsRouteResponse,
  );
  splitRoleSessionsRouteFallbackPage.on(
    'requestfailed',
    observeSplitRoleSessionsRouteRequestFailure,
  );
  splitRoleSessionsRouteFallbackPage.on(
    'framenavigated',
    observeSplitRoleSessionsRouteFrameNavigation,
  );
  splitRoleSessionsRouteFallbackPage.on(
    'pageerror',
    observeSplitRoleSessionsRoutePageError,
  );
  try {
    splitRoleSessionsRouteFallbackArmed = true;
    splitRoleSessionsRouteTargetDocumentActive = false;
    splitRoleSessionsRouteTargetDocumentEverCommitted = false;
    splitRoleSessionsRouteModuleRequests = new WeakSet<Request>();
    splitRoleSessionsRouteFallbackResponseState.moduleFailure = undefined;
    splitRoleSessionsRouteStartupError = 'none';
    await assertManagementRouteFallsBack(
      splitRoleSessionsRouteFallbackPage,
      '/admin-access/sessions',
      'Sessions',
      'session-table',
    );
  } catch (error) {
    throwIfRolesPermissionsTestDeadline('Sessions', error);
    throwSplitRoleSessionsRouteFallbackFailure(
      await readSplitRoleSessionsRouteFallbackState(
        splitRoleSessionsRouteFallbackPage,
        splitRoleSessionsRouteTargetDocumentEverCommitted,
        splitRoleSessionsRouteStartupError,
        splitRoleSessionsRouteFallbackResponseState.moduleFailure,
      ),
    );
  } finally {
    splitRoleSessionsRouteFallbackArmed = false;
    splitRoleSessionsRouteTargetDocumentActive = false;
    splitRoleSessionsRouteFallbackPage.off(
      'pageerror',
      observeSplitRoleSessionsRoutePageError,
    );
    splitRoleSessionsRouteFallbackPage.off(
      'framenavigated',
      observeSplitRoleSessionsRouteFrameNavigation,
    );
    splitRoleSessionsRouteFallbackPage.off(
      'requestfailed',
      observeSplitRoleSessionsRouteRequestFailure,
    );
    splitRoleSessionsRouteFallbackPage.off(
      'response',
      observeSplitRoleSessionsRouteResponse,
    );
    splitRoleSessionsRouteFallbackPage.off(
      'request',
      observeSplitRoleSessionsRouteRequest,
    );
    await splitRoleSessionsRouteFallbackPage.close();
  }
  const splitRoleRecoveryPage = await page.context().newPage();
  try {
    try {
      await assertManagementRouteFallsBack(
        splitRoleRecoveryPage,
        '/admin-access/audit',
        'Audit history',
        'audit-table',
      );
    } catch {
      throw new Error('Split-role audit route did not fall back.');
    }
    try {
      await openWorkspace(
        splitRoleRecoveryPage,
        '/admin-access/administrators',
        'Administrators',
        'administrator-table',
      );
    } catch {
      throw new Error(
        'Split-role restored Administrators workspace did not become ready.',
      );
    }
  } finally {
    await splitRoleRecoveryPage.close();
  }
  await logout(page, acceptanceEvidence);

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

  let viewerAccessToken: string;
  try {
    viewerAccessToken = await login(
      page,
      viewerAdministrator.userName,
      viewerPassword,
      acceptanceEvidence,
    );
  } catch {
    throw new Error('Viewer read-only login did not reach Game configuration.');
  }
  try {
    await openWorkspace(
      page,
      '/admin-access/administrators',
      'Administrators',
      'administrator-table',
    );
  } catch {
    throw new Error(
      'Viewer read-only Administrators workspace did not become ready.',
    );
  }
  await expect(page.getByTestId('create-administrator')).toHaveCount(0);
  await expect(
    page.getByLabel(
      `Edit display name for ${accessOnlyAdministrator.userName}`,
    ),
  ).toHaveCount(0);
  await expect(
    page.getByLabel(`Edit roles for ${accessOnlyAdministrator.userName}`),
  ).toHaveCount(0);
  await expect(
    page.getByLabel(`Deactivate ${accessOnlyAdministrator.userName}`),
  ).toHaveCount(0);
  await expect(
    page.getByLabel(`Reset password for ${accessOnlyAdministrator.userName}`),
  ).toHaveCount(0);

  const refreshedViewerRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(apiBaseURL, `/admin-access/roles/${viewerRole.id}/permissions`),
      {
        data: {
          permissionNames: [
            accessPermission,
            administratorsManagePermission,
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
  try {
    await login(
      page,
      viewerAdministrator.userName,
      viewerPassword,
      acceptanceEvidence,
    );
  } catch {
    throw new Error(
      'Viewer expanded-permissions login did not reach Game configuration.',
    );
  }
  try {
    await openWorkspace(
      page,
      '/admin-access/administrators',
      'Administrators',
      'administrator-table',
    );
  } catch {
    throw new Error(
      'Viewer expanded Administrators workspace did not become ready.',
    );
  }
  await expect(page.getByTestId('create-administrator')).toBeVisible();
  await expect(
    page.getByLabel(
      `Edit display name for ${accessOnlyAdministrator.userName}`,
    ),
  ).toBeVisible();
  await expect(
    page.getByLabel(`Edit roles for ${accessOnlyAdministrator.userName}`),
  ).toBeVisible();
  await expect(
    page.getByLabel(`Deactivate ${accessOnlyAdministrator.userName}`),
  ).toBeVisible();
  await expect(
    page.getByLabel(`Reset password for ${accessOnlyAdministrator.userName}`),
  ).toHaveCount(0);
  try {
    await openWorkspace(
      page,
      '/admin-access/roles',
      'Roles and permissions',
      'role-table',
    );
  } catch {
    throw new Error('Viewer Roles read workspace did not become ready.');
  }
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
  const rolesManageViewerRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(apiBaseURL, `/admin-access/roles/${viewerRole.id}/permissions`),
      {
        data: {
          permissionNames: [
            accessPermission,
            administratorsManagePermission,
            administratorsReadPermission,
            auditLogsReadPermission,
            rolesManagePermission,
            rolesReadPermission,
            gameConfigAccessPermission,
          ],
        },
        headers: mutationHeaders(
          bootstrapAccessToken,
          refreshedViewerRole.revision,
        ),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  expect(rolesManageViewerRole.permissionNames).toContain(
    rolesManagePermission,
  );
  await logout(page, acceptanceEvidence);
  await login(
    page,
    viewerAdministrator.userName,
    viewerPassword,
    acceptanceEvidence,
  );
  try {
    await openWorkspace(
      page,
      '/admin-access/roles',
      'Roles and permissions',
      'role-table',
    );
  } catch {
    throw new Error('Viewer Roles manage workspace did not become ready.');
  }
  await expect(page.getByTestId('create-role')).toBeVisible();
  await expect(page.getByLabel(`Rename ${accessOnlyRole.name}`)).toBeVisible();
  await expect(
    page.getByLabel(`Edit permissions for ${accessOnlyRole.name}`),
  ).toBeVisible();
  await expect(
    page.getByLabel(`Deactivate ${accessOnlyRole.name}`),
  ).toBeVisible();
  try {
    await openWorkspace(
      page,
      '/admin-access/audit',
      'Audit history',
      'audit-table',
    );
  } catch {
    throw new Error('Viewer Audit workspace did not become ready.');
  }
  await expect(page.getByTestId('view-audit-record').first()).toBeVisible();
  await logout(page, acceptanceEvidence);

  const resetOnlyRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/roles'),
      {
        data: {
          name: `Password reset operator ${runId}`,
          permissionNames: [
            accessPermission,
            administratorsReadPermission,
            administratorsResetPasswordPermission,
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
  const resetOnlyAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/administrators'),
      {
        data: {
          displayName: 'Password-reset-only administrator',
          initialPassword: resetOnlyPassword,
          roleIds: [resetOnlyRole.id],
          userName: `e2e-reset-only-${runId}`,
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

  try {
    await login(
      page,
      resetOnlyAdministrator.userName,
      resetOnlyPassword,
      acceptanceEvidence,
    );
  } catch {
    throw new Error('Reset-only login did not reach Game configuration.');
  }
  try {
    await openWorkspace(
      page,
      '/admin-access/administrators',
      'Administrators',
      'administrator-table',
    );
  } catch {
    throw new Error('Reset-only Administrator workspace did not become ready.');
  }
  await expect(page.getByTestId('create-administrator')).toHaveCount(0);
  await expect(
    page.getByLabel(
      `Edit display name for ${accessOnlyAdministrator.userName}`,
    ),
  ).toHaveCount(0);
  await expect(
    page.getByLabel(`Edit roles for ${accessOnlyAdministrator.userName}`),
  ).toHaveCount(0);
  await expect(
    page.getByLabel(`Deactivate ${accessOnlyAdministrator.userName}`),
  ).toHaveCount(0);
  await expect(
    page.getByLabel(`Reset password for ${accessOnlyAdministrator.userName}`),
  ).toBeVisible();
  await logout(page, acceptanceEvidence);

  const sessionsReadOnlyRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/roles'),
      {
        data: {
          name: `Sessions reader ${runId}`,
          permissionNames: [
            accessPermission,
            sessionsReadPermission,
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
  const sessionsReadOnlyAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/administrators'),
      {
        data: {
          displayName: 'Sessions-read-only administrator',
          initialPassword: sessionsReadOnlyPassword,
          roleIds: [sessionsReadOnlyRole.id],
          userName: `e2e-sessions-reader-${runId}`,
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
  try {
    await login(
      page,
      sessionsReadOnlyAdministrator.userName,
      sessionsReadOnlyPassword,
      acceptanceEvidence,
    );
  } catch {
    throw new Error(
      'Sessions read-only login did not reach Game configuration.',
    );
  }
  try {
    await openWorkspace(
      page,
      '/admin-access/sessions',
      'Sessions',
      'session-table',
    );
  } catch {
    throw new Error('Sessions read-only workspace did not become ready.');
  }
  await expect(page.getByTestId('revoke-session')).toHaveCount(0);
  await logout(page, acceptanceEvidence);

  const sessionsRevokeRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(
        apiBaseURL,
        `/admin-access/roles/${sessionsReadOnlyRole.id}/permissions`,
      ),
      {
        data: {
          permissionNames: [
            accessPermission,
            sessionsReadPermission,
            sessionsRevokePermission,
            gameConfigAccessPermission,
          ],
        },
        headers: mutationHeaders(
          bootstrapAccessToken,
          sessionsReadOnlyRole.revision,
        ),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  expect(sessionsRevokeRole.permissionNames).toContain(
    sessionsRevokePermission,
  );
  try {
    await login(
      page,
      sessionsReadOnlyAdministrator.userName,
      sessionsReadOnlyPassword,
      acceptanceEvidence,
    );
  } catch {
    throw new Error(
      'Sessions revoke-enabled login did not reach Game configuration.',
    );
  }
  try {
    await openWorkspace(
      page,
      '/admin-access/sessions',
      'Sessions',
      'session-table',
    );
  } catch {
    throw new Error('Sessions revoke-enabled workspace did not become ready.');
  }
  await expect(page.getByTestId('revoke-session').first()).toBeVisible();
  await logout(page, acceptanceEvidence);

  const permissionProbePermissions = [
    accessPermission,
    gameConfigAccessPermission,
    ...allAdminAccessLeafPermissions,
  ];
  const permissionProbeRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/roles'),
      {
        data: {
          name: `Permission probe ${runId}`,
          permissionNames: permissionProbePermissions,
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
  const permissionProbeAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'POST',
      apiUrl(apiBaseURL, '/admin-access/administrators'),
      {
        data: {
          displayName: 'Permission probe administrator',
          initialPassword: permissionProbePassword,
          roleIds: [permissionProbeRole.id],
          userName: `e2e-permission-probe-${runId}`,
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
  let permissionProbeAccessToken: string;
  try {
    permissionProbeAccessToken = await login(
      page,
      permissionProbeAdministrator.userName,
      permissionProbePassword,
      acceptanceEvidence,
    );
  } catch {
    throw new Error('Permission-probe login did not reach Game configuration.');
  }
  try {
    await openWorkspace(
      page,
      '/admin-access/administrators',
      'Administrators',
      'administrator-table',
    );
  } catch {
    throw new Error(
      'Permission-probe Administrators workspace did not become ready.',
    );
  }
  const isolatedLeafProbes: ReadonlyArray<{
    data?: object;
    method: 'GET' | 'POST';
    path: string;
    permission: (typeof allAdminAccessLeafPermissions)[number];
  }> = [
    {
      method: 'GET',
      path: '/admin-access/administrators?Page=1&PageSize=20',
      permission: administratorsReadPermission,
    },
    {
      data: {},
      method: 'POST',
      path: '/admin-access/administrators',
      permission: administratorsManagePermission,
    },
    {
      data: {},
      method: 'POST',
      path: `/admin-access/administrators/${crypto.randomUUID()}/password-reset`,
      permission: administratorsResetPasswordPermission,
    },
    {
      method: 'GET',
      path: '/admin-access/roles?Page=1&PageSize=20',
      permission: rolesReadPermission,
    },
    {
      data: {},
      method: 'POST',
      path: '/admin-access/roles',
      permission: rolesManagePermission,
    },
    {
      method: 'GET',
      path: '/admin-access/sessions?Page=1&PageSize=20',
      permission: sessionsReadPermission,
    },
    {
      method: 'POST',
      path: `/admin-access/sessions/${crypto.randomUUID()}/revoke`,
      permission: sessionsRevokePermission,
    },
    {
      method: 'GET',
      path: '/admin-access/audit-records?FromUtc=2025-01-01T00%3A00%3A00.000Z&ToUtc=2030-01-01T00%3A00%3A00.000Z&Page=1&PageSize=20',
      permission: auditLogsReadPermission,
    },
  ];
  let permissionProbeRevision = permissionProbeRole.revision;
  for (const probe of isolatedLeafProbes) {
    const updatedProbeRole = await readApiData<AdminRole>(
      await acceptanceEvidence.apiRequest(
        page.request,
        'PUT',
        apiUrl(
          apiBaseURL,
          `/admin-access/roles/${permissionProbeRole.id}/permissions`,
        ),
        {
          data: {
            permissionNames: permissionProbePermissions.filter(
              (permission) => permission !== probe.permission,
            ),
          },
          headers: mutationHeaders(
            bootstrapAccessToken,
            permissionProbeRevision,
          ),
        },
      ),
      acceptanceEvidence,
      'PUT',
    );
    permissionProbeRevision = updatedProbeRole.revision;
    const forbiddenResponse = await acceptanceEvidence.apiRequest(
      page.request,
      probe.method,
      apiUrl(apiBaseURL, probe.path),
      {
        ...(probe.data === undefined ? {} : { data: probe.data }),
        headers: authorizationHeaders(permissionProbeAccessToken),
      },
    );
    await assertAuthorizationFailure(
      forbiddenResponse,
      acceptanceEvidence,
      403,
      probe.method,
    );

    if (probe.permission === administratorsReadPermission) {
      const browserReadDenialEpoch = createRolePermissionApiWaitEpoch();
      const browserForbiddenResponse = waitForApiResponse(
        page,
        'GET',
        '/api/admin-access/administrators',
        403,
        'browser-read-denial',
        browserReadDenialEpoch,
      );
      beginRolePermissionApiWaitEpoch(browserReadDenialEpoch);
      await page.getByTestId('refresh-administrators').click();
      await assertAuthorizationFailure(
        await browserForbiddenResponse,
        acceptanceEvidence,
        403,
        'GET',
      );
      await expect(page).toHaveURL(/\/admin-access\/roles(?:[?#].*)?$/, {
        timeout: 60_000,
      });
      await expect(
        page.getByRole('heading', { name: 'Roles and permissions' }),
      ).toBeVisible({ timeout: 60_000 });
      await expect(page.getByTestId('role-table')).toBeVisible({
        timeout: 60_000,
      });
      await expect(page.getByTestId('administrator-table')).toHaveCount(0);
      await expect(page.getByTestId('refresh-administrators')).toHaveCount(0);
    }
  }
  const restoredPermissionProbeRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(
        apiBaseURL,
        `/admin-access/roles/${permissionProbeRole.id}/permissions`,
      ),
      {
        data: { permissionNames: permissionProbePermissions },
        headers: mutationHeaders(bootstrapAccessToken, permissionProbeRevision),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  expect(restoredPermissionProbeRole.permissionNames).toEqual(
    expect.arrayContaining([...allAdminAccessLeafPermissions]),
  );
  permissionProbeRevision = restoredPermissionProbeRole.revision;

  const restoredPermissionEpoch = createRolePermissionApiWaitEpoch();
  const restoredPermissionCodesResponse = waitForApiResponse(
    page,
    'GET',
    '/api/auth/codes',
    200,
    'restored-access-codes',
    restoredPermissionEpoch,
  );
  const restoredUserInfoResponse = waitForApiResponse(
    page,
    'GET',
    '/api/user/info',
    200,
    'restored-user-info',
    restoredPermissionEpoch,
  );
  const restoredGameConfigResponses = waitForGameConfigStartupResponses(
    page,
    {
      drafts: 'restored-game-config-drafts',
      environment: 'restored-game-config-environment',
      versions: 'restored-game-config-versions',
    },
    restoredPermissionEpoch,
  );
  beginRolePermissionApiWaitEpoch(restoredPermissionEpoch);
  await page.goto('/game-config', {
    timeout: loginReadyTimeoutMs,
    waitUntil: 'commit',
  });
  await assertResponseStatus(
    await restoredPermissionCodesResponse,
    acceptanceEvidence,
    200,
    'GET',
  );
  await assertResponseStatus(
    await restoredUserInfoResponse,
    acceptanceEvidence,
    200,
    'GET',
  );
  await expect(
    page.getByRole('heading', { name: 'Game configuration' }),
  ).toBeVisible({ timeout: 60_000 });
  await auditGameConfigStartupResponses(
    restoredGameConfigResponses,
    acceptanceEvidence,
  );
  try {
    await openWorkspace(
      page,
      '/admin-access/roles',
      'Roles and permissions',
      'role-table',
    );
  } catch {
    throw new Error(
      'Permission-probe restored Roles workspace did not become ready.',
    );
  }
  await assertPersistedAccessPermission(page, rolesReadPermission, true);

  const coldStartRevokedRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(
        apiBaseURL,
        `/admin-access/roles/${permissionProbeRole.id}/permissions`,
      ),
      {
        data: {
          permissionNames: permissionProbePermissions.filter(
            (permission) => permission !== rolesReadPermission,
          ),
        },
        headers: mutationHeaders(bootstrapAccessToken, permissionProbeRevision),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  permissionProbeRevision = coldStartRevokedRole.revision;
  const coldStartPermissionEpoch = createRolePermissionApiWaitEpoch();
  const coldStartPermissionCodesResponse = waitForApiResponse(
    page,
    'GET',
    '/api/auth/codes',
    200,
    'cold-start-access-codes',
    coldStartPermissionEpoch,
  );
  const coldStartUserInfoResponse = waitForApiResponse(
    page,
    'GET',
    '/api/user/info',
    200,
    'cold-start-user-info',
    coldStartPermissionEpoch,
  );
  beginRolePermissionApiWaitEpoch(coldStartPermissionEpoch);
  await page.reload({ timeout: loginReadyTimeoutMs, waitUntil: 'commit' });
  await assertResponseStatus(
    await coldStartPermissionCodesResponse,
    acceptanceEvidence,
    200,
    'GET',
  );
  await assertResponseStatus(
    await coldStartUserInfoResponse,
    acceptanceEvidence,
    200,
    'GET',
  );
  await assertPersistedAccessPermission(page, rolesReadPermission, false);
  try {
    await assertManagementRouteFallsBack(
      page,
      '/admin-access/roles',
      'Roles and permissions',
      'role-table',
    );
  } catch {
    throw new Error(
      'Cold-start roles permission revocation retained a protected route.',
    );
  }

  const coldStartRestoredRole = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(
        apiBaseURL,
        `/admin-access/roles/${permissionProbeRole.id}/permissions`,
      ),
      {
        data: { permissionNames: permissionProbePermissions },
        headers: mutationHeaders(bootstrapAccessToken, permissionProbeRevision),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  expect(coldStartRestoredRole.permissionNames).toEqual(
    expect.arrayContaining([...allAdminAccessLeafPermissions]),
  );

  const refreshedPermissionEpoch = createRolePermissionApiWaitEpoch();
  const refreshedPermissionProbeSession = waitForApiResponse(
    page,
    'POST',
    '/api/auth/refresh',
    200,
    'refresh-session',
    refreshedPermissionEpoch,
  );
  const refreshedPermissionCodesResponse = waitForApiResponse(
    page,
    'GET',
    '/api/auth/codes',
    200,
    'refreshed-access-codes',
    refreshedPermissionEpoch,
  );
  const refreshedUserInfoResponse = waitForApiResponse(
    page,
    'GET',
    '/api/user/info',
    200,
    'refreshed-user-info',
    refreshedPermissionEpoch,
  );
  await page.evaluate(() => localStorage.clear());
  await assertPersistedAccessStateCleared(page);
  beginRolePermissionApiWaitEpoch(refreshedPermissionEpoch);
  await page.reload({
    timeout: loginReadyTimeoutMs,
    waitUntil: 'commit',
  });
  await assertResponseStatus(
    await refreshedPermissionProbeSession,
    acceptanceEvidence,
    200,
    'POST',
  );
  await assertResponseStatus(
    await refreshedPermissionCodesResponse,
    acceptanceEvidence,
    200,
    'GET',
  );
  await assertResponseStatus(
    await refreshedUserInfoResponse,
    acceptanceEvidence,
    200,
    'GET',
  );
  await expect(
    page.getByRole('heading', { name: 'Roles and permissions' }),
  ).toBeVisible({ timeout: 60_000 });
  await expect(page.getByTestId('role-table')).toBeVisible({
    timeout: 60_000,
  });
  await assertPersistedAccessPermission(page, rolesReadPermission, true);
  await expect(page.getByTestId('create-role')).toBeVisible();
  await expect(page.getByLabel(`Rename ${accessOnlyRole.name}`)).toBeVisible();
  await expect(
    page.getByLabel(`Edit permissions for ${accessOnlyRole.name}`),
  ).toBeVisible();
  await expect(
    page.getByLabel(`Deactivate ${accessOnlyRole.name}`),
  ).toBeVisible();

  const refreshedGameConfigPage = await page.context().newPage();
  try {
    const refreshedGameConfigEpoch = createRolePermissionApiWaitEpoch();
    const refreshedGameConfigStartupResponses =
      waitForGameConfigStartupResponses(
        refreshedGameConfigPage,
        {
          drafts: 'refreshed-game-config-drafts',
          environment: 'refreshed-game-config-environment',
          versions: 'refreshed-game-config-versions',
        },
        refreshedGameConfigEpoch,
      );
    beginRolePermissionApiWaitEpoch(refreshedGameConfigEpoch);
    await refreshedGameConfigPage.goto('/game-config', {
      timeout: loginReadyTimeoutMs,
      waitUntil: 'commit',
    });
    await expect(
      refreshedGameConfigPage.getByRole('heading', {
        name: 'Game configuration',
      }),
    ).toBeVisible({ timeout: 60_000 });
    await auditGameConfigStartupResponses(
      refreshedGameConfigStartupResponses,
      acceptanceEvidence,
    );
    await acceptanceEvidence.assertCurrentRefreshCookie();
  } finally {
    await refreshedGameConfigPage.close();
  }
  try {
    await openWorkspace(
      page,
      '/admin-access/administrators',
      'Administrators',
      'administrator-table',
    );
  } catch {
    throw new Error(
      'Permission-probe refresh-restored Administrators workspace did not become ready.',
    );
  }
  await expect(page.getByTestId('create-administrator')).toBeVisible();
  await expect(
    page.getByLabel(
      `Edit display name for ${accessOnlyAdministrator.userName}`,
    ),
  ).toBeVisible();
  await expect(
    page.getByLabel(`Edit roles for ${accessOnlyAdministrator.userName}`),
  ).toBeVisible();
  await expect(
    page.getByLabel(`Deactivate ${accessOnlyAdministrator.userName}`),
  ).toBeVisible();
  await expect(
    page.getByLabel(`Reset password for ${accessOnlyAdministrator.userName}`),
  ).toBeVisible();
  const refreshedPermissionSessionsPage = await page.context().newPage();
  try {
    try {
      await openWorkspace(
        refreshedPermissionSessionsPage,
        '/admin-access/sessions',
        'Sessions',
        'session-table',
      );
    } catch {
      throw new Error(
        'Permission-probe refresh-restored Sessions workspace did not become ready.',
      );
    }
    await expect(
      refreshedPermissionSessionsPage.getByTestId('revoke-session').first(),
    ).toBeVisible();
  } finally {
    await refreshedPermissionSessionsPage.close();
  }
  const refreshedPermissionAuditPage = await page.context().newPage();
  try {
    try {
      await openWorkspace(
        refreshedPermissionAuditPage,
        '/admin-access/audit',
        'Audit history',
        'audit-table',
      );
    } catch {
      throw new Error(
        'Permission-probe refresh-restored Audit workspace did not become ready.',
      );
    }
    await expect(
      refreshedPermissionAuditPage.getByTestId('view-audit-record').first(),
    ).toBeVisible();
  } finally {
    await refreshedPermissionAuditPage.close();
  }
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
  const longDisplayName = `d${runId.replaceAll('-', '')}${'x'.repeat(80)}`;
  const longUserName = `u${runId.replaceAll('-', '')}${'x'.repeat(31)}`;
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
          displayName: longDisplayName,
          initialPassword: statePassword,
          roleIds: [builtInRole.id],
          userName: longUserName,
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
  await assertAuthorizationFailure(
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
  await assertAuthorizationFailure(
    forbiddenResponse,
    acceptanceEvidence,
    403,
    'GET',
  );

  await logout(page, acceptanceEvidence);
  const stateRoleWithAdministratorsRead = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(apiBaseURL, `/admin-access/roles/${stateRole.id}/permissions`),
      {
        data: {
          permissionNames: [
            accessPermission,
            administratorsReadPermission,
            gameConfigAccessPermission,
          ],
        },
        headers: mutationHeaders(bootstrapAccessToken, stateRole.revision),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  await login(
    page,
    stateViewer.userName,
    stateViewerPassword,
    acceptanceEvidence,
  );
  await page.setViewportSize({ height: 844, width: 390 });
  await openWorkspace(
    page,
    '/admin-access/administrators',
    'Administrators',
    'administrator-table',
  );
  const stateRoleWithoutAdministratorsRead = await readApiData<AdminRole>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(apiBaseURL, `/admin-access/roles/${stateRole.id}/permissions`),
      {
        data: {
          permissionNames: [accessPermission, gameConfigAccessPermission],
        },
        headers: mutationHeaders(
          bootstrapAccessToken,
          stateRoleWithAdministratorsRead.revision,
        ),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  expect(stateRoleWithoutAdministratorsRead.permissionNames).not.toContain(
    administratorsReadPermission,
  );
  const forbiddenCdpSession = await page.context().newCDPSession(page);
  let forbiddenNetworkEnabled = false;
  try {
    await forbiddenCdpSession.send('Network.enable');
    forbiddenNetworkEnabled = true;
    await forbiddenCdpSession.send('Network.emulateNetworkConditions', {
      downloadThroughput: -1,
      latency: 5000,
      offline: false,
      uploadThroughput: -1,
    });
    const browserForbiddenResponse = waitForApiResponse(
      page,
      'GET',
      '/api/admin-access/administrators',
      403,
    );
    await page.getByTestId('refresh-administrators').click();
    await assertResponseStatus(
      await browserForbiddenResponse,
      acceptanceEvidence,
      403,
      'GET',
    );
    await expect(page.getByTestId('administrators-forbidden')).toBeVisible({
      timeout: 60_000,
    });
    await assertNoBodyOverflow(page);
    await acceptanceEvidence.captureScreenshot(
      page,
      `${visualOutputDirectory}/admin-access-administrators-forbidden-mobile-390x844.png`,
    );
  } finally {
    if (forbiddenNetworkEnabled) {
      await forbiddenCdpSession.send('Network.emulateNetworkConditions', {
        downloadThroughput: -1,
        latency: 0,
        offline: false,
        uploadThroughput: -1,
      });
      await forbiddenCdpSession.send('Network.disable');
    }
    await forbiddenCdpSession.detach();
  }
  await expect(
    page.getByRole('heading', { name: 'Game configuration' }),
  ).toBeVisible({ timeout: 60_000 });

  await logout(page, acceptanceEvidence);
  await login(page, bootstrapUserName, bootstrapPassword, acceptanceEvidence);

  const notFoundResponse = await acceptanceEvidence.apiRequest(
    page.request,
    'GET',
    apiUrl(apiBaseURL, `/admin-access/administrators/${crypto.randomUUID()}`),
    { headers: authorizationHeaders(bootstrapAccessToken) },
  );
  await assertAdministratorNotFoundFailure(
    notFoundResponse,
    acceptanceEvidence,
    'GET',
  );
  await page.setViewportSize({ height: 844, width: 390 });
  const notFoundPath = `/admin-access/not-found-${runId}`;
  await page.goto(notFoundPath, { timeout: 60_000, waitUntil: 'commit' });
  await expect(page.locator('svg[viewBox="0 0 860 571"]')).toBeVisible({
    timeout: 60_000,
  });
  expect(new URL(page.url()).pathname).toBe(notFoundPath);
  await assertNoBodyOverflow(page);
  await acceptanceEvidence.captureScreenshot(
    page,
    `${visualOutputDirectory}/admin-access-client-route-not-found-mobile-390x844.png`,
  );
  await openWorkspace(
    page,
    '/admin-access/administrators',
    'Administrators',
    'administrator-table',
  );
  await formField(page, 'administrator-search').fill(longUserName);
  await formField(page, 'administrator-search').press('Enter');
  const initialStateAdministratorRow = tableRow(
    page,
    'administrator-table',
    longUserName,
  ).first();
  await expect(initialStateAdministratorRow).toBeVisible({ timeout: 60_000 });
  await initialStateAdministratorRow
    .getByTestId('administrator-mobile-actions')
    .click();
  await page.getByRole('menuitem', { name: 'Reset password' }).click();
  const passwordResetSubject = page.getByTestId(
    'password-reset-administrator-subject',
  );
  await expect(passwordResetSubject).toHaveText(longDisplayName);
  await expect
    .poll(() =>
      passwordResetSubject.evaluate((subject) => {
        const style = window.getComputedStyle(subject);
        return {
          overflowWrap: style.overflowWrap,
          whiteSpace: style.whiteSpace,
        };
      }),
    )
    .toEqual({ overflowWrap: 'anywhere', whiteSpace: 'normal' });
  const passwordResetSubjectLayout = await passwordResetSubject.evaluate(
    (subject) => {
      const range = document.createRange();
      range.selectNodeContents(subject);

      return {
        clientWidth: subject.clientWidth,
        lineCount: new Set(
          Array.from(range.getClientRects(), (rect) => Math.round(rect.top)),
        ).size,
        scrollWidth: subject.scrollWidth,
      };
    },
  );
  expect(passwordResetSubjectLayout.clientWidth).toBeGreaterThan(0);
  expect(passwordResetSubjectLayout.scrollWidth).toBeLessThanOrEqual(
    passwordResetSubjectLayout.clientWidth,
  );
  expect(passwordResetSubjectLayout.lineCount).toBeGreaterThan(1);
  await assertNoBodyOverflow(page);
  await page.keyboard.press('Escape');
  await expect(passwordResetSubject).toHaveCount(0);
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
      mobileScreenshot: `${visualOutputDirectory}/admin-access-administrators-mobile-390x844.png`,
      path: '/admin-access/administrators',
      desktopScreenshot: `${visualOutputDirectory}/admin-access-administrators-desktop-1440x900.png`,
      tableTestId: 'administrator-table',
    },
    {
      heading: 'Roles and permissions',
      mobileScreenshot: `${visualOutputDirectory}/admin-access-roles-mobile-390x844.png`,
      path: '/admin-access/roles',
      desktopScreenshot: `${visualOutputDirectory}/admin-access-roles-desktop-1440x900.png`,
      tableTestId: 'role-table',
    },
    {
      heading: 'Sessions',
      mobileScreenshot: `${visualOutputDirectory}/admin-access-sessions-mobile-390x844.png`,
      path: '/admin-access/sessions',
      desktopScreenshot: `${visualOutputDirectory}/admin-access-sessions-desktop-1440x900.png`,
      tableTestId: 'session-table',
    },
    {
      heading: 'Audit history',
      mobileScreenshot: `${visualOutputDirectory}/admin-access-audit-mobile-390x844.png`,
      path: '/admin-access/audit',
      desktopScreenshot: `${visualOutputDirectory}/admin-access-audit-desktop-1440x900.png`,
      tableTestId: 'audit-table',
    },
  ] as const;

  await page.setViewportSize({ height: 844, width: 390 });
  await openWorkspace(
    page,
    '/admin-access/administrators',
    'Administrators',
    'administrator-table',
  );
  const administratorsTable = page.getByTestId('administrator-table');
  const cdpSession = await page.context().newCDPSession(page);
  let networkEnabled = false;
  try {
    await cdpSession.send('Network.enable');
    networkEnabled = true;
    await cdpSession.send('Network.emulateNetworkConditions', {
      downloadThroughput: -1,
      latency: 5000,
      offline: false,
      uploadThroughput: -1,
    });
    const loadingResponse = waitForApiResponse(
      page,
      'GET',
      '/api/admin-access/administrators',
      200,
    );
    await page.getByTestId('refresh-administrators').click();
    await expect(page.locator('#__app-loading__')).toBeHidden({
      timeout: 60_000,
    });
    await expect(administratorsTable.locator('.el-loading-mask')).toBeVisible({
      timeout: 60_000,
    });
    await assertNoBodyOverflow(page);
    await acceptanceEvidence.captureScreenshot(
      page,
      `${visualOutputDirectory}/admin-access-administrators-loading-mobile-390x844.png`,
    );
    await assertResponseStatus(
      await loadingResponse,
      acceptanceEvidence,
      200,
      'GET',
    );
    await expect(administratorsTable.locator('.el-loading-mask')).toBeHidden({
      timeout: 60_000,
    });
  } finally {
    if (networkEnabled) {
      await cdpSession.send('Network.emulateNetworkConditions', {
        downloadThroughput: -1,
        latency: 0,
        offline: false,
        uploadThroughput: -1,
      });
      await cdpSession.send('Network.disable');
    }
    await cdpSession.detach();
  }
  for (const workspace of workspaces) {
    await openWorkspace(
      page,
      workspace.path,
      workspace.heading,
      workspace.tableTestId,
    );
    await assertNoBodyOverflow(page);
    await acceptanceEvidence.captureScreenshot(
      page,
      workspace.mobileScreenshot,
    );
  }
  await openWorkspace(
    page,
    '/admin-access/administrators',
    'Administrators',
    'administrator-table',
  );
  await page.getByTestId('create-administrator').click();
  const createAdministratorDrawer = page.getByTestId(
    'administrator-create-drawer',
  );
  await expect(createAdministratorDrawer).toBeVisible({ timeout: 60_000 });
  await page.getByTestId('submit-administrator-create').click();
  await expect(
    createAdministratorDrawer.locator('.el-form-item__error'),
  ).toHaveCount(3);
  await assertNoBodyOverflow(page);
  await acceptanceEvidence.captureScreenshot(
    page,
    `${visualOutputDirectory}/admin-access-administrators-validation-mobile-390x844.png`,
  );
  await page.keyboard.press('Escape');
  await expect(createAdministratorDrawer).toHaveCount(0);
  await openWorkspace(
    page,
    '/admin-access/roles',
    'Roles and permissions',
    'role-table',
  );
  const roleCatalogResponse = waitForApiResponse(
    page,
    'GET',
    '/api/admin-access/permissions',
    200,
  );
  await page.getByTestId('create-role').focus();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('role-create-drawer')).toBeVisible();
  await assertResponseStatus(
    await roleCatalogResponse,
    acceptanceEvidence,
    200,
    'GET',
  );
  await assertNoBodyOverflow(page);
  await acceptanceEvidence.captureScreenshot(
    page,
    `${visualOutputDirectory}/admin-access-roles-create-drawer-mobile-390x844.png`,
  );
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
    await acceptanceEvidence.captureScreenshot(
      page,
      workspace.desktopScreenshot,
    );
  }

  await logout(page, acceptanceEvidence);
  await login(
    page,
    stateAdministrator.userName ?? longUserName,
    statePassword,
    acceptanceEvidence,
  );
  await page.setViewportSize({ height: 844, width: 390 });
  await openWorkspace(
    page,
    '/admin-access/sessions',
    'Sessions',
    'session-table',
  );
  const stateSessionRow = tableRow(page, 'session-table', longUserName).first();
  await expect(stateSessionRow).toBeVisible({ timeout: 60_000 });
  await stateSessionRow.getByTestId('session-mobile-actions').click();
  await page.getByRole('menuitem', { name: 'Revoke session' }).click();
  await expect(page.getByTestId('session-revoke-subject')).toHaveText(
    longUserName,
  );
  await expect(page.getByTestId('confirm-revoke-session')).toBeVisible();
  await assertNoBodyOverflow(page);
  await acceptanceEvidence.captureScreenshot(
    page,
    `${visualOutputDirectory}/admin-access-sessions-revoke-dialog-mobile-390x844.png`,
  );
  await page.keyboard.press('Escape');
  await expect(page.getByTestId('session-revoke-subject')).toHaveCount(0);

  const deactivatedStateAdministrator = await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(
        apiBaseURL,
        `/admin-access/administrators/${stateAdministrator.id}/active-state`,
      ),
      {
        data: { isActive: false },
        headers: mutationHeaders(
          bootstrapAccessToken,
          updatedStateAdministrator.revision,
        ),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  expect(deactivatedStateAdministrator.isActive).toBe(false);
  await assertBrowserRefreshRejected(page, acceptanceEvidence);
  await expect(
    page.getByRole('heading', { name: 'NovaGame Operations' }),
  ).toBeVisible({ timeout: 60_000 });
  await assertNoBodyOverflow(page);
  await acceptanceEvidence.captureScreenshot(
    page,
    `${visualOutputDirectory}/admin-access-unauthenticated-mobile-390x844.png`,
  );
  const resumedBootstrapAccessToken = await login(
    page,
    bootstrapUserName,
    bootstrapPassword,
    acceptanceEvidence,
  );
  await page.setViewportSize({ height: 844, width: 390 });
  await openWorkspace(
    page,
    '/admin-access/administrators',
    'Administrators',
    'administrator-table',
  );
  await formField(page, 'administrator-search').fill(longUserName);
  await formField(page, 'administrator-search').press('Enter');
  const stateAdministratorRow = tableRow(
    page,
    'administrator-table',
    longUserName,
  ).first();
  await expect(stateAdministratorRow).toBeVisible({ timeout: 60_000 });
  await stateAdministratorRow
    .getByTestId('administrator-mobile-actions')
    .click();
  await page.getByRole('menuitem', { name: 'Edit display name' }).click();
  await expect(
    page.getByTestId('administrator-display-name-drawer'),
  ).toBeVisible({ timeout: 60_000 });
  await readApiData<Administrator>(
    await acceptanceEvidence.apiRequest(
      page.request,
      'PUT',
      apiUrl(
        apiBaseURL,
        `/admin-access/administrators/${stateAdministrator.id}/display-name`,
      ),
      {
        data: { displayName: `${longValue} source` },
        headers: mutationHeaders(
          resumedBootstrapAccessToken,
          deactivatedStateAdministrator.revision,
        ),
      },
    ),
    acceptanceEvidence,
    'PUT',
  );
  const displayNameConflictResponse = waitForApiResponse(
    page,
    'PUT',
    `/api/admin-access/administrators/${stateAdministrator.id}/display-name`,
    409,
  );
  await page
    .getByTestId('administrator-display-name')
    .fill(`${longValue} review`);
  await page.getByTestId('save-administrator-display-name').click();
  await assertResponseStatus(
    await displayNameConflictResponse,
    acceptanceEvidence,
    409,
    'PUT',
  );
  await expect(
    page.getByTestId('reload-conflicted-display-name'),
  ).toBeVisible();
  await assertNoBodyOverflow(page);
  await acceptanceEvidence.captureScreenshot(
    page,
    `${visualOutputDirectory}/admin-access-administrators-conflict-mobile-390x844.png`,
  );
  await page.keyboard.press('Escape');
  await expect(
    page.getByTestId('administrator-display-name-drawer'),
  ).toHaveCount(0);

  const emptySearch = `no-administrator-${runId}`;
  await formField(page, 'administrator-search').fill(emptySearch);
  await formField(page, 'administrator-search').press('Enter');
  await expect(
    page.getByText('No administrators match the current filters'),
  ).toBeVisible({ timeout: 60_000 });
  await assertNoBodyOverflow(page);
  await acceptanceEvidence.captureScreenshot(
    page,
    `${visualOutputDirectory}/admin-access-administrators-empty-mobile-390x844.png`,
  );
  await formField(page, 'administrator-search').fill('');
  await formField(page, 'administrator-search').press('Enter');
  await expect(stateAdministratorRow).toBeVisible({ timeout: 60_000 });

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
  await expect(page.getByRole('button', { name: 'Retry' })).toBeVisible();
  await assertNoBodyOverflow(page);
  await acceptanceEvidence.captureScreenshot(
    page,
    `${visualOutputDirectory}/admin-access-administrators-retry-mobile-390x844.png`,
  );
  const retryResponse = waitForApiResponse(
    page,
    'GET',
    '/api/admin-access/administrators',
    200,
  );
  await page.getByRole('button', { exact: true, name: 'Retry' }).click();
  await assertResponseStatus(
    await retryResponse,
    acceptanceEvidence,
    200,
    'GET',
  );
  await expect(stateAdministratorRow).toBeVisible({ timeout: 60_000 });

  await logout(page, acceptanceEvidence);
});
