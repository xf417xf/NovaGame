import type {
  ActivateGameConfigVersionRequest,
  CreateGameConfigDraftRequest,
  GameConfigDraft,
  GameConfigDraftSummary,
  GameConfigEnvironment,
  GameConfigValidationResult,
  GameConfigVersion,
  GameConfigVersionSummary,
  ListGameConfigDraftsRequest,
  ListGameConfigVersionsRequest,
  PageResult,
  PublishGameConfigDraftRequest,
  RollbackGameConfigEnvironmentRequest,
  UpdateGameConfigDraftRequest,
  ValidateGameConfigDraftRequest,
} from './nova-game-contracts';

import { createCorrelationId, requestClient } from './request';

export interface AuditedResult<T> {
  correlationId: string;
  data: T;
}

function withAuditHeader(correlationId: string) {
  return {
    headers: {
      'X-Correlation-Id': correlationId,
    },
  };
}

async function audited<T>(
  command: (correlationId: string) => Promise<T>,
): Promise<AuditedResult<T>> {
  const correlationId = createCorrelationId();
  return {
    correlationId,
    data: await command(correlationId),
  };
}

export function listGameConfigDrafts(params: ListGameConfigDraftsRequest) {
  return requestClient.get<PageResult<GameConfigDraftSummary>>(
    '/game-config/drafts',
    { params },
  );
}

export function getGameConfigDraft(draftId: string) {
  return requestClient.get<GameConfigDraft>(`/game-config/drafts/${draftId}`);
}

export function createGameConfigDraft(request: CreateGameConfigDraftRequest) {
  return audited((correlationId) =>
    requestClient.post<GameConfigDraft>(
      '/game-config/drafts',
      request,
      withAuditHeader(correlationId),
    ),
  );
}

export function updateGameConfigDraft(
  draftId: string,
  request: UpdateGameConfigDraftRequest,
) {
  return audited((correlationId) =>
    requestClient.put<GameConfigDraft>(
      `/game-config/drafts/${draftId}`,
      request,
      withAuditHeader(correlationId),
    ),
  );
}

export function validateGameConfigDraft(
  draftId: string,
  request: ValidateGameConfigDraftRequest,
) {
  return audited((correlationId) =>
    requestClient.post<GameConfigValidationResult>(
      `/game-config/drafts/${draftId}/validate`,
      request,
      withAuditHeader(correlationId),
    ),
  );
}

export function publishGameConfigDraft(
  draftId: string,
  request: PublishGameConfigDraftRequest,
) {
  return audited((correlationId) =>
    requestClient.post<GameConfigVersion>(
      `/game-config/drafts/${draftId}/publish`,
      request,
      withAuditHeader(correlationId),
    ),
  );
}

export function listGameConfigVersions(params: ListGameConfigVersionsRequest) {
  return requestClient.get<PageResult<GameConfigVersionSummary>>(
    '/game-config/versions',
    { params },
  );
}

export function getGameConfigVersion(versionId: string) {
  return requestClient.get<GameConfigVersion>(
    `/game-config/versions/${versionId}`,
  );
}

export function getGameConfigEnvironment(
  environment: string,
  configurationSet: string,
) {
  return requestClient.get<GameConfigEnvironment>(
    `/game-config/environments/${encodeURIComponent(environment)}`,
    { params: { configurationSet } },
  );
}

export function activateGameConfigVersion(
  environment: string,
  request: ActivateGameConfigVersionRequest,
) {
  return audited((correlationId) =>
    requestClient.post<GameConfigEnvironment>(
      `/game-config/environments/${encodeURIComponent(environment)}/activate`,
      request,
      withAuditHeader(correlationId),
    ),
  );
}

export function rollbackGameConfigEnvironment(
  environment: string,
  request: RollbackGameConfigEnvironmentRequest,
) {
  return audited((correlationId) =>
    requestClient.post<GameConfigEnvironment>(
      `/game-config/environments/${encodeURIComponent(environment)}/rollback`,
      request,
      withAuditHeader(correlationId),
    ),
  );
}

interface SnapshotResponse {
  data: Blob;
  headers: Record<string, string | undefined>;
  status: number;
}

export function downloadGameConfigSnapshot(versionId: string) {
  return requestClient.get<SnapshotResponse>(
    `/game-config/versions/${versionId}/snapshot`,
    {
      responseReturn: 'raw',
      responseType: 'blob',
    },
  );
}
