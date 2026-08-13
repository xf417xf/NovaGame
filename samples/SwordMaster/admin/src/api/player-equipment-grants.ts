import type {
  GetApiSwordmasterOperationsPlayerAssetsByPlayerIdEquipmentGrantsData as GeneratedGetPlayerEquipmentGrantPageOperation,
  GrantPlayerEquipmentRequest as GeneratedGrantPlayerEquipmentRequest,
  PlayerEquipmentGrantDto as GeneratedPlayerEquipmentGrantDto,
  PlayerEquipmentGrantMutationResultDto as GeneratedPlayerEquipmentGrantMutationResultDto,
  PlayerEquipmentGrantPageDto as GeneratedPlayerEquipmentGrantPageDto,
  PlayerEquipmentGrantStatusDto as GeneratedPlayerEquipmentGrantStatusDto,
  PostApiSwordmasterOperationsPlayerAssetsByPlayerIdEquipmentGrantsByGrantIdRollbackData as GeneratedRollbackPlayerEquipmentGrantOperation,
  PostApiSwordmasterOperationsPlayerAssetsByPlayerIdEquipmentGrantsData as GeneratedGrantPlayerEquipmentOperation,
  RollbackPlayerEquipmentGrantRequest as GeneratedRollbackPlayerEquipmentGrantRequest,
} from '#/generated/player-equipment-grants/types.gen';

import type { ApiResult } from './nova-game-contracts';

import {
  NovaGameApiError,
  toNovaGameApiError,
  unwrapApiResultBody,
} from './api-result';
import { requestClient } from './request';

// requestClient already targets the Vite-proxied /api base path.
const equipmentGrantApiRoot = '/swordmaster/operations/player-assets';
const emptyGuid = '00000000-0000-0000-0000-000000000000';
const maximumConfiguredItemIdLength = 128;
const maximumPageSize = 100;
const maximumReasonLength = 500;
const maximumSignedInt32 = 2_147_483_647;
const canonicalGuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const routeGuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const strongRevisionEtagPattern = /^"[1-9][0-9]*"$/;

type ApiRecord = Record<string, unknown>;

type GeneratedGrantStatus = NonNullable<GeneratedPlayerEquipmentGrantStatusDto>;

export interface PlayerEquipmentGrant {
  readonly configurationVersionId: NonNullable<
    GeneratedPlayerEquipmentGrantDto['configurationVersionId']
  >;
  readonly configuredItemId: NonNullable<
    GeneratedPlayerEquipmentGrantDto['configuredItemId']
  >;
  readonly grantId: NonNullable<GeneratedPlayerEquipmentGrantDto['grantId']>;
  readonly grantReason: NonNullable<
    GeneratedPlayerEquipmentGrantDto['grantReason']
  >;
  readonly grantResultingRevision: NonNullable<
    GeneratedPlayerEquipmentGrantDto['grantResultingRevision']
  >;
  readonly grantedAtUtc: NonNullable<
    GeneratedPlayerEquipmentGrantDto['grantedAtUtc']
  >;
  readonly grantedByAdministratorId: NonNullable<
    GeneratedPlayerEquipmentGrantDto['grantedByAdministratorId']
  >;
  readonly ownedItemId: NonNullable<
    GeneratedPlayerEquipmentGrantDto['ownedItemId']
  >;
  readonly playerId: NonNullable<GeneratedPlayerEquipmentGrantDto['playerId']>;
  readonly rollbackReason: null | NonNullable<
    GeneratedPlayerEquipmentGrantDto['rollbackReason']
  >;
  readonly rollbackResultingRevision: null | NonNullable<
    GeneratedPlayerEquipmentGrantDto['rollbackResultingRevision']
  >;
  readonly rolledBackAtUtc: null | NonNullable<
    GeneratedPlayerEquipmentGrantDto['rolledBackAtUtc']
  >;
  readonly rolledBackByAdministratorId: null | NonNullable<
    GeneratedPlayerEquipmentGrantDto['rolledBackByAdministratorId']
  >;
  readonly status: GeneratedGrantStatus;
}

export interface PlayerEquipmentGrantMutationResult {
  readonly configurationVersionId: NonNullable<
    GeneratedPlayerEquipmentGrantMutationResultDto['configurationVersionId']
  >;
  readonly configuredItemId: NonNullable<
    GeneratedPlayerEquipmentGrantMutationResultDto['configuredItemId']
  >;
  readonly grantId: NonNullable<
    GeneratedPlayerEquipmentGrantMutationResultDto['grantId']
  >;
  readonly grantReason: NonNullable<
    GeneratedPlayerEquipmentGrantMutationResultDto['grantReason']
  >;
  readonly grantedAtUtc: NonNullable<
    GeneratedPlayerEquipmentGrantMutationResultDto['grantedAtUtc']
  >;
  readonly grantedByAdministratorId: NonNullable<
    GeneratedPlayerEquipmentGrantMutationResultDto['grantedByAdministratorId']
  >;
  readonly ownedItemId: NonNullable<
    GeneratedPlayerEquipmentGrantMutationResultDto['ownedItemId']
  >;
  readonly playerId: NonNullable<
    GeneratedPlayerEquipmentGrantMutationResultDto['playerId']
  >;
  readonly resultingRevision: NonNullable<
    GeneratedPlayerEquipmentGrantMutationResultDto['resultingRevision']
  >;
  readonly rollbackReason: null | NonNullable<
    GeneratedPlayerEquipmentGrantMutationResultDto['rollbackReason']
  >;
  readonly rolledBackAtUtc: null | NonNullable<
    GeneratedPlayerEquipmentGrantMutationResultDto['rolledBackAtUtc']
  >;
  readonly rolledBackByAdministratorId: null | NonNullable<
    GeneratedPlayerEquipmentGrantMutationResultDto['rolledBackByAdministratorId']
  >;
  readonly status: GeneratedGrantStatus;
}

export interface PlayerEquipmentGrantPage {
  readonly items: readonly PlayerEquipmentGrant[];
  readonly page: NonNullable<GeneratedPlayerEquipmentGrantPageDto['page']>;
  readonly pageSize: NonNullable<
    GeneratedPlayerEquipmentGrantPageDto['pageSize']
  >;
  readonly playerId: NonNullable<
    GeneratedPlayerEquipmentGrantPageDto['playerId']
  >;
  readonly revision: NonNullable<
    GeneratedPlayerEquipmentGrantPageDto['revision']
  >;
  readonly totalCount: NonNullable<
    GeneratedPlayerEquipmentGrantPageDto['totalCount']
  >;
}

export interface PlayerEquipmentGrantPageRequest {
  readonly page: number;
  readonly pageSize: number;
  readonly playerId: string;
}

export interface GrantPlayerEquipmentInput {
  readonly configuredItemId: string;
  readonly idempotencyKey: string;
  readonly ifMatch: string;
  readonly playerId: string;
  readonly reason: string;
}

export interface RollbackPlayerEquipmentGrantInput {
  readonly grantId: string;
  readonly idempotencyKey: string;
  readonly ifMatch: string;
  readonly playerId: string;
  readonly reason: string;
}

export interface PlayerEquipmentGrantPageSnapshot {
  readonly data: PlayerEquipmentGrantPage;
  readonly etag: string;
}

export interface PlayerEquipmentGrantMutationSnapshot {
  readonly data: PlayerEquipmentGrantMutationResult;
  readonly etag: string;
}

export interface PlayerEquipmentGrantRequestOptions {
  readonly headers?: Readonly<Record<string, string>>;
  readonly params?: Readonly<Record<string, number>>;
  readonly responseReturn?: 'raw';
  readonly signal?: AbortSignal;
}

export interface PlayerEquipmentGrantRawResponse<T> {
  readonly data: ApiResult<T>;
  readonly headers: PlayerEquipmentGrantResponseHeaders;
  readonly status: number;
}

export interface PlayerEquipmentGrantResponseHeaders {
  get?: (name: string) => null | string | undefined;
  readonly [header: string]: unknown;
}

export interface PlayerEquipmentGrantRequestClient {
  get<T>(url: string, config?: PlayerEquipmentGrantRequestOptions): Promise<T>;
  post<T>(
    url: string,
    data?: unknown,
    config?: PlayerEquipmentGrantRequestOptions,
  ): Promise<T>;
}

export interface PlayerEquipmentGrantTransport {
  getPage(
    request: PlayerEquipmentGrantPageRequest,
    signal?: AbortSignal,
  ): Promise<PlayerEquipmentGrantPageSnapshot>;
  grant(
    request: GrantPlayerEquipmentInput,
    signal?: AbortSignal,
  ): Promise<PlayerEquipmentGrantMutationSnapshot>;
  rollback(
    request: RollbackPlayerEquipmentGrantInput,
    signal?: AbortSignal,
  ): Promise<PlayerEquipmentGrantMutationSnapshot>;
}

export function createPlayerEquipmentGrantTransport(
  client: PlayerEquipmentGrantRequestClient = requestClient as unknown as PlayerEquipmentGrantRequestClient,
): PlayerEquipmentGrantTransport {
  return {
    async getPage(request, signal) {
      signal?.throwIfAborted();
      const playerId = validatePageRequest(request);
      const operation = {
        path: { playerId },
        query: { page: request.page, pageSize: request.pageSize },
        url: '/api/swordmaster/operations/player-assets/{playerId}/equipment-grants',
      } satisfies GeneratedGetPlayerEquipmentGrantPageOperation;
      const response = await requestRaw(signal, () =>
        client.get<
          PlayerEquipmentGrantRawResponse<GeneratedPlayerEquipmentGrantPageDto>
        >(historyPath(operation.path.playerId), {
          params: operation.query,
          responseReturn: 'raw',
          ...(signal === undefined ? {} : { signal }),
        }),
      );
      return decodePageResponse(response, { ...request, playerId });
    },

    async grant(request, signal) {
      signal?.throwIfAborted();
      const playerId = validateGrantRequest(request);
      const body = {
        configuredItemId: request.configuredItemId,
        reason: request.reason,
      } satisfies GeneratedGrantPlayerEquipmentRequest;
      const operation = {
        body,
        headers: {
          'Idempotency-Key': request.idempotencyKey,
          'If-Match': request.ifMatch,
        },
        path: { playerId },
        url: '/api/swordmaster/operations/player-assets/{playerId}/equipment-grants',
      } satisfies GeneratedGrantPlayerEquipmentOperation;
      const response = await requestRaw(signal, () =>
        client.post<
          PlayerEquipmentGrantRawResponse<GeneratedPlayerEquipmentGrantMutationResultDto>
        >(historyPath(operation.path.playerId), operation.body, {
          headers: {
            'Content-Type': 'application/json',
            ...operation.headers,
          },
          responseReturn: 'raw',
          ...(signal === undefined ? {} : { signal }),
        }),
      );
      return decodeMutationResponse(response, operation.path.playerId, 1);
    },

    async rollback(request, signal) {
      signal?.throwIfAborted();
      const route = validateRollbackRequest(request);
      const body = {
        reason: request.reason,
      } satisfies GeneratedRollbackPlayerEquipmentGrantRequest;
      const operation = {
        body,
        headers: {
          'Idempotency-Key': request.idempotencyKey,
          'If-Match': request.ifMatch,
        },
        path: route,
        url: '/api/swordmaster/operations/player-assets/{playerId}/equipment-grants/{grantId}/rollback',
      } satisfies GeneratedRollbackPlayerEquipmentGrantOperation;
      const response = await requestRaw(signal, () =>
        client.post<
          PlayerEquipmentGrantRawResponse<GeneratedPlayerEquipmentGrantMutationResultDto>
        >(
          rollbackPath(operation.path.playerId, operation.path.grantId),
          operation.body,
          {
            headers: {
              'Content-Type': 'application/json',
              ...operation.headers,
            },
            responseReturn: 'raw',
            ...(signal === undefined ? {} : { signal }),
          },
        ),
      );
      return decodeMutationResponse(
        response,
        operation.path.playerId,
        2,
        operation.path.grantId,
      );
    },
  };
}

export const playerEquipmentGrantTransport =
  createPlayerEquipmentGrantTransport();

async function requestRaw<T>(
  signal: AbortSignal | undefined,
  request: () => Promise<PlayerEquipmentGrantRawResponse<T>>,
): Promise<PlayerEquipmentGrantRawResponse<T>> {
  signal?.throwIfAborted();
  try {
    const response = requireRawResponse<T>(await request());
    signal?.throwIfAborted();
    if (response.status !== 200) {
      throw invalidResponse(
        response.status,
        'The SwordMaster equipment-grant endpoint returned an unexpected success status.',
      );
    }
    return response;
  } catch (error) {
    signal?.throwIfAborted();
    throw toNovaGameApiError(error);
  }
}

function decodePageResponse(
  response: PlayerEquipmentGrantRawResponse<GeneratedPlayerEquipmentGrantPageDto>,
  request: PlayerEquipmentGrantPageRequest,
): PlayerEquipmentGrantPageSnapshot {
  const etag = readStrongRevisionEtag(response);
  const data =
    unwrapEquipmentGrantApiResult<GeneratedPlayerEquipmentGrantPageDto>(
      response.data,
      response.status,
    );
  const page = decodePage(data, request, response.status);
  return { data: page, etag };
}

function decodeMutationResponse(
  response: PlayerEquipmentGrantRawResponse<GeneratedPlayerEquipmentGrantMutationResultDto>,
  expectedPlayerId: string,
  expectedStatus: GeneratedGrantStatus,
  expectedGrantId?: string,
): PlayerEquipmentGrantMutationSnapshot {
  const etag = readStrongRevisionEtag(response);
  const data =
    unwrapEquipmentGrantApiResult<GeneratedPlayerEquipmentGrantMutationResultDto>(
      response.data,
      response.status,
    );
  const mutation = decodeMutation(
    data,
    expectedPlayerId,
    expectedStatus,
    expectedGrantId,
    response.status,
  );
  return { data: mutation, etag };
}

function unwrapEquipmentGrantApiResult<T>(value: unknown, status: number): T {
  if (!isApiResultEnvelope(value)) {
    throw invalidResponse(
      status,
      'The SwordMaster equipment-grant endpoint returned an invalid API response envelope.',
    );
  }
  return unwrapApiResultBody(value as ApiResult<T>, status);
}

function isApiResultEnvelope(value: unknown): value is ApiResult<unknown> {
  if (!isRecord(value)) {
    return false;
  }
  if (value.succeeded === true) {
    return Object.hasOwn(value, 'data') && value.error === null;
  }
  if (value.succeeded !== false || !isApiErrorPayload(value.error)) {
    return false;
  }
  return !Object.hasOwn(value, 'data') || value.data === null;
}

function isApiErrorPayload(value: unknown): boolean {
  if (!isRecord(value)) {
    return false;
  }
  return (
    typeof value.code === 'string' &&
    typeof value.message === 'string' &&
    isApiErrorDetails(value.details)
  );
}

function isApiErrorDetails(value: unknown): boolean {
  return (
    value === null ||
    (isRecord(value) &&
      Object.values(value).every(
        (messages) =>
          Array.isArray(messages) &&
          messages.every((message) => typeof message === 'string'),
      ))
  );
}

function decodePage(
  value: unknown,
  request: PlayerEquipmentGrantPageRequest,
  status: number,
): PlayerEquipmentGrantPage {
  const page = requireRecord(value, 'equipment-grant history response', status);
  const playerId = requireCanonicalGuid(
    page.playerId,
    'history player identifier',
    status,
  );
  assertMatchingIdentifier(
    playerId,
    request.playerId,
    'history player identifier',
    status,
  );
  const responsePage = requirePositiveInteger(
    page.page,
    'history page',
    status,
  );
  const responsePageSize = requirePositiveInteger(
    page.pageSize,
    'history page size',
    status,
  );
  if (responsePage !== request.page || responsePageSize !== request.pageSize) {
    throw invalidResponse(
      status,
      'The equipment-grant history response does not match the requested page.',
    );
  }
  const items = requireArray(page.items, 'history items', status).map(
    (item, index) => {
      const grant = decodeGrant(item, index, status);
      assertMatchingIdentifier(
        grant.playerId,
        playerId,
        `history items[${index}].playerId`,
        status,
      );
      return grant;
    },
  );

  return {
    items,
    page: responsePage,
    pageSize: responsePageSize,
    playerId,
    revision: requirePositiveInteger(page.revision, 'history revision', status),
    totalCount: requireNonNegativeInteger(
      page.totalCount,
      'history total count',
      status,
    ),
  };
}

function decodeGrant(
  value: unknown,
  index: number,
  status: number,
): PlayerEquipmentGrant {
  const grant = requireRecord(value, `history items[${index}]`, status);
  const result = {
    configurationVersionId: requireCanonicalGuid(
      grant.configurationVersionId,
      `history items[${index}].configurationVersionId`,
      status,
    ),
    configuredItemId: requireNonEmptyString(
      grant.configuredItemId,
      `history items[${index}].configuredItemId`,
      status,
    ),
    grantId: requireCanonicalGuid(
      grant.grantId,
      `history items[${index}].grantId`,
      status,
    ),
    grantReason: requireNonEmptyString(
      grant.grantReason,
      `history items[${index}].grantReason`,
      status,
    ),
    grantResultingRevision: requirePositiveInteger(
      grant.grantResultingRevision,
      `history items[${index}].grantResultingRevision`,
      status,
    ),
    grantedAtUtc: requireNonEmptyString(
      grant.grantedAtUtc,
      `history items[${index}].grantedAtUtc`,
      status,
    ),
    grantedByAdministratorId: requireCanonicalGuid(
      grant.grantedByAdministratorId,
      `history items[${index}].grantedByAdministratorId`,
      status,
    ),
    ownedItemId: requireCanonicalGuid(
      grant.ownedItemId,
      `history items[${index}].ownedItemId`,
      status,
    ),
    playerId: requireCanonicalGuid(
      grant.playerId,
      `history items[${index}].playerId`,
      status,
    ),
    rollbackReason: requireNullableString(
      grant.rollbackReason,
      `history items[${index}].rollbackReason`,
      status,
    ),
    rollbackResultingRevision: requireNullablePositiveInteger(
      grant.rollbackResultingRevision,
      `history items[${index}].rollbackResultingRevision`,
      status,
    ),
    rolledBackAtUtc: requireNullableString(
      grant.rolledBackAtUtc,
      `history items[${index}].rolledBackAtUtc`,
      status,
    ),
    rolledBackByAdministratorId: requireNullableGuid(
      grant.rolledBackByAdministratorId,
      `history items[${index}].rolledBackByAdministratorId`,
      status,
    ),
    status: requireGrantStatus(
      grant.status,
      `history items[${index}].status`,
      status,
    ),
  };
  assertGrantLifecycleEvidence(result, `history items[${index}]`, status);
  return result;
}

function decodeMutation(
  value: unknown,
  expectedPlayerId: string,
  expectedStatus: GeneratedGrantStatus,
  expectedGrantId: string | undefined,
  status: number,
): PlayerEquipmentGrantMutationResult {
  const mutation = requireRecord(
    value,
    'equipment-grant mutation response',
    status,
  );
  const playerId = requireCanonicalGuid(
    mutation.playerId,
    'mutation player identifier',
    status,
  );
  const grantId = requireCanonicalGuid(
    mutation.grantId,
    'mutation grant identifier',
    status,
  );
  assertMatchingIdentifier(
    playerId,
    expectedPlayerId,
    'mutation player identifier',
    status,
  );
  if (expectedGrantId !== undefined) {
    assertMatchingIdentifier(
      grantId,
      expectedGrantId,
      'mutation grant identifier',
      status,
    );
  }

  const result = {
    configurationVersionId: requireCanonicalGuid(
      mutation.configurationVersionId,
      'mutation configuration version identifier',
      status,
    ),
    configuredItemId: requireNonEmptyString(
      mutation.configuredItemId,
      'mutation configured item identifier',
      status,
    ),
    grantId,
    grantReason: requireNonEmptyString(
      mutation.grantReason,
      'mutation grant reason',
      status,
    ),
    grantedAtUtc: requireNonEmptyString(
      mutation.grantedAtUtc,
      'mutation granted timestamp',
      status,
    ),
    grantedByAdministratorId: requireCanonicalGuid(
      mutation.grantedByAdministratorId,
      'mutation granting administrator identifier',
      status,
    ),
    ownedItemId: requireCanonicalGuid(
      mutation.ownedItemId,
      'mutation owned-item identifier',
      status,
    ),
    playerId,
    resultingRevision: requirePositiveInteger(
      mutation.resultingRevision,
      'mutation resulting revision',
      status,
    ),
    rollbackReason: requireNullableString(
      mutation.rollbackReason,
      'mutation rollback reason',
      status,
    ),
    rolledBackAtUtc: requireNullableString(
      mutation.rolledBackAtUtc,
      'mutation rollback timestamp',
      status,
    ),
    rolledBackByAdministratorId: requireNullableGuid(
      mutation.rolledBackByAdministratorId,
      'mutation rollback administrator identifier',
      status,
    ),
    status: requireGrantStatus(mutation.status, 'mutation status', status),
  };
  assertMutationLifecycleEvidence(result, status);
  if (result.status !== expectedStatus) {
    throw invalidResponse(
      status,
      'The mutation lifecycle status does not match the requested command.',
    );
  }
  return result;
}

function assertGrantLifecycleEvidence(
  grant: PlayerEquipmentGrant,
  description: string,
  status: number,
): void {
  const rollbackEvidence = [
    grant.rollbackReason,
    grant.rollbackResultingRevision,
    grant.rolledBackAtUtc,
    grant.rolledBackByAdministratorId,
  ];
  if (
    (grant.status === 1 && rollbackEvidence.some((value) => value !== null)) ||
    (grant.status === 2 && rollbackEvidence.some((value) => value === null))
  ) {
    throw invalidResponse(
      status,
      `The ${description} rollback evidence does not match its lifecycle status.`,
    );
  }
}

function assertMutationLifecycleEvidence(
  mutation: PlayerEquipmentGrantMutationResult,
  status: number,
): void {
  const rollbackEvidence = [
    mutation.rollbackReason,
    mutation.rolledBackAtUtc,
    mutation.rolledBackByAdministratorId,
  ];
  if (
    (mutation.status === 1 &&
      rollbackEvidence.some((value) => value !== null)) ||
    (mutation.status === 2 && rollbackEvidence.some((value) => value === null))
  ) {
    throw invalidResponse(
      status,
      'The mutation rollback evidence does not match its lifecycle status.',
    );
  }
}

function historyPath(playerId: string): string {
  return `${equipmentGrantApiRoot}/${encodeURIComponent(playerId)}/equipment-grants`;
}

function rollbackPath(playerId: string, grantId: string): string {
  return `${historyPath(playerId)}/${encodeURIComponent(grantId)}/rollback`;
}

function validatePageRequest(request: PlayerEquipmentGrantPageRequest): string {
  const playerId = normalizeRouteGuid(request.playerId, 'playerId');
  if (!isValidPage(request.page)) {
    throw invalidRequest(
      'Page must be a positive 32-bit integer within the supported range.',
      'page',
    );
  }
  if (
    !Number.isSafeInteger(request.pageSize) ||
    request.pageSize < 1 ||
    request.pageSize > maximumPageSize
  ) {
    throw invalidRequest(
      `Page size must be an integer from 1 through ${maximumPageSize}.`,
      'pageSize',
    );
  }
  if ((request.page - 1) * request.pageSize > maximumSignedInt32) {
    throw invalidRequest(
      'The requested page is outside the supported range.',
      'page',
    );
  }
  return playerId;
}

function validateGrantRequest(request: GrantPlayerEquipmentInput): string {
  const playerId = normalizeRouteGuid(request.playerId, 'playerId');
  validateConfiguredItemId(request.configuredItemId);
  validateReason(request.reason);
  validateMutationMetadata(request.ifMatch, request.idempotencyKey);
  return playerId;
}

function validateRollbackRequest(
  request: RollbackPlayerEquipmentGrantInput,
): Readonly<{ grantId: string; playerId: string }> {
  const playerId = normalizeRouteGuid(request.playerId, 'playerId');
  const grantId = normalizeRouteGuid(request.grantId, 'grantId');
  validateReason(request.reason);
  validateMutationMetadata(request.ifMatch, request.idempotencyKey);
  return { grantId, playerId };
}

function validateMutationMetadata(
  ifMatch: unknown,
  idempotencyKey: unknown,
): void {
  if (typeof ifMatch !== 'string' || !strongRevisionEtagPattern.test(ifMatch)) {
    throw invalidRequest(
      'If-Match must be one canonical strong positive-decimal revision ETag.',
      'If-Match',
    );
  }
  if (
    typeof idempotencyKey !== 'string' ||
    !canonicalGuidPattern.test(idempotencyKey) ||
    idempotencyKey === emptyGuid
  ) {
    throw invalidRequest(
      'Idempotency-Key must be one non-empty canonical lowercase D-format GUID.',
      'Idempotency-Key',
    );
  }
}

function normalizeRouteGuid(value: unknown, field: string): string {
  if (
    typeof value !== 'string' ||
    !routeGuidPattern.test(value) ||
    value.toLowerCase() === emptyGuid
  ) {
    throw invalidRequest(
      `${field} must be one non-empty D-format GUID.`,
      field,
    );
  }
  return value.toLowerCase();
}

function validateConfiguredItemId(value: unknown): void {
  if (
    typeof value !== 'string' ||
    value.length === 0 ||
    value.length > maximumConfiguredItemIdLength ||
    value !== value.trim() ||
    /[\s\p{Cc}]/u.test(value)
  ) {
    throw invalidRequest(
      `configuredItemId must contain 1 to ${maximumConfiguredItemIdLength} non-whitespace, non-control characters.`,
      'configuredItemId',
    );
  }
}

function validateReason(value: unknown): void {
  if (
    typeof value !== 'string' ||
    value.length === 0 ||
    value.length > maximumReasonLength ||
    value !== value.trim() ||
    /\p{Cc}/u.test(value)
  ) {
    throw invalidRequest(
      `reason must contain 1 to ${maximumReasonLength} trimmed non-control characters.`,
      'reason',
    );
  }
}

function isValidPage(value: unknown): value is number {
  return (
    typeof value === 'number' &&
    Number.isSafeInteger(value) &&
    value >= 1 &&
    value <= maximumSignedInt32
  );
}

function readStrongRevisionEtag(
  response: PlayerEquipmentGrantRawResponse<unknown>,
): string {
  const value = readHeader(response.headers, 'etag');
  if (typeof value !== 'string' || !strongRevisionEtagPattern.test(value)) {
    throw invalidResponse(
      response.status,
      'The SwordMaster equipment-grant response is missing a canonical strong revision ETag.',
    );
  }
  return value;
}

function requireRawResponse<T>(
  value: unknown,
): PlayerEquipmentGrantRawResponse<T> {
  const response = requireRecord(value, 'equipment-grant raw response', 0);
  const status = requireHttpStatus(response.status);
  if (
    !Object.hasOwn(response, 'data') ||
    !isResponseHeaders(response.headers)
  ) {
    throw invalidResponse(
      status,
      'The SwordMaster equipment-grant endpoint returned an invalid raw response.',
    );
  }
  return {
    data: response.data as ApiResult<T>,
    headers: response.headers,
    status,
  };
}

function requireHttpStatus(value: unknown): number {
  if (
    typeof value !== 'number' ||
    !Number.isSafeInteger(value) ||
    value < 100 ||
    value > 599
  ) {
    throw invalidResponse(
      0,
      'The SwordMaster equipment-grant endpoint returned an invalid HTTP status.',
    );
  }
  return value;
}

function isResponseHeaders(
  value: unknown,
): value is PlayerEquipmentGrantResponseHeaders {
  return (
    isRecord(value) &&
    (value.get === undefined || typeof value.get === 'function')
  );
}

function readHeader(
  headers: PlayerEquipmentGrantResponseHeaders,
  headerName: string,
): unknown {
  const fromAccessor = headers.get?.(headerName);
  if (fromAccessor !== undefined && fromAccessor !== null) {
    return fromAccessor;
  }

  const expectedName = headerName.toLowerCase();
  for (const [name, value] of Object.entries(headers)) {
    if (name.toLowerCase() === expectedName) {
      return value;
    }
  }
  return undefined;
}

function requireRecord(
  value: unknown,
  description: string,
  status: number,
): ApiRecord {
  if (!isRecord(value)) {
    throw invalidResponse(status, `The ${description} must be an object.`);
  }
  return value;
}

function isRecord(value: unknown): value is ApiRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function requireArray(
  value: unknown,
  description: string,
  status: number,
): unknown[] {
  if (!Array.isArray(value)) {
    throw invalidResponse(status, `The ${description} must be an array.`);
  }
  return value;
}

function requireCanonicalGuid(
  value: unknown,
  description: string,
  status: number,
): string {
  if (
    typeof value !== 'string' ||
    !canonicalGuidPattern.test(value) ||
    value === emptyGuid
  ) {
    throw invalidResponse(
      status,
      `The ${description} must be a non-empty canonical lowercase D-format GUID.`,
    );
  }
  return value;
}

function requireNullableGuid(
  value: unknown,
  description: string,
  status: number,
): null | string {
  return value === null
    ? null
    : requireCanonicalGuid(value, description, status);
}

function requireNonEmptyString(
  value: unknown,
  description: string,
  status: number,
): string {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw invalidResponse(
      status,
      `The ${description} must be a non-empty string.`,
    );
  }
  return value;
}

function requireNullableString(
  value: unknown,
  description: string,
  status: number,
): null | string {
  return value === null
    ? null
    : requireNonEmptyString(value, description, status);
}

function requirePositiveInteger(
  value: unknown,
  description: string,
  status: number,
): number {
  if (
    typeof value !== 'number' ||
    !Number.isFinite(value) ||
    !Number.isInteger(value) ||
    value <= 0
  ) {
    throw invalidResponse(
      status,
      `The ${description} must be a positive integer.`,
    );
  }
  return value;
}

function requireNonNegativeInteger(
  value: unknown,
  description: string,
  status: number,
): number {
  if (
    typeof value !== 'number' ||
    !Number.isFinite(value) ||
    !Number.isInteger(value) ||
    value < 0
  ) {
    throw invalidResponse(
      status,
      `The ${description} must be a non-negative integer.`,
    );
  }
  return value;
}

function requireNullablePositiveInteger(
  value: unknown,
  description: string,
  status: number,
): null | number {
  return value === null
    ? null
    : requirePositiveInteger(value, description, status);
}

function requireGrantStatus(
  value: unknown,
  description: string,
  status: number,
): GeneratedGrantStatus {
  if (value !== 1 && value !== 2) {
    throw invalidResponse(
      status,
      `The ${description} must be a known grant status.`,
    );
  }
  return value;
}

function assertMatchingIdentifier(
  value: string,
  expectedValue: string,
  description: string,
  status: number,
): void {
  if (value !== expectedValue) {
    throw invalidResponse(
      status,
      `The ${description} does not match the request.`,
    );
  }
}

function invalidRequest(message: string, field: string): NovaGameApiError {
  return new NovaGameApiError(
    message,
    'SwordMaster.PlayerAssets.EquipmentGrants.InvalidRequest',
    400,
    { [field]: [message] },
  );
}

function invalidResponse(status: number, message: string): NovaGameApiError {
  return new NovaGameApiError(message, 'NovaGame.InvalidResponse', status);
}
