export type Guid = string;
export type UtcDateTime = string;

export interface ApiErrorPayload {
  code: string;
  details: null | Record<string, string[]>;
  message: string;
}

export type ApiResult<T> =
  | { data: T; error: null; succeeded: true }
  | { data?: null; error: ApiErrorPayload; succeeded: false };

export type ApiVoidResult =
  | { error: ApiErrorPayload; succeeded: false }
  | { error: null; succeeded: true };

export interface AccessTokenResponse {
  accessToken: string;
  expiresAtUtc: UtcDateTime;
  tokenType: string;
}

export interface AdministratorInfo {
  displayName: string;
  id: Guid;
  permissionCodes: string[];
  roles: string[];
  userName: string;
}

export const GameConfigDraftState = {
  Editing: 1,
  Published: 4,
  Validated: 3,
  ValidationFailed: 2,
} as const;

export type GameConfigDraftState =
  (typeof GameConfigDraftState)[keyof typeof GameConfigDraftState];

export interface PageResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface GameConfigValidationIssue {
  code: string;
  column: null | number;
  line: null | number;
  location: string;
  message: string;
}

export interface GameConfigValidationResult {
  draftId: Guid;
  isValid: boolean;
  issues: GameConfigValidationIssue[];
  revision: number;
  validatedAtUtc: UtcDateTime;
  validatedByAdministratorId: Guid;
}

export interface GameConfigDraftSummary {
  baseVersionId: Guid | null;
  configurationSet: string;
  createdAtUtc: UtcDateTime;
  createdByAdministratorId: Guid;
  id: Guid;
  name: string;
  publishedVersionId: Guid | null;
  revision: number;
  schemaVersion: string;
  state: GameConfigDraftState;
  updatedAtUtc: UtcDateTime;
  updatedByAdministratorId: Guid;
}

export interface GameConfigDraft extends GameConfigDraftSummary {
  content: string;
  latestValidation: GameConfigValidationResult | null;
}

export interface GameConfigVersionSummary {
  configurationSet: string;
  contentHash: string;
  id: Guid;
  publishedAtUtc: UtcDateTime;
  publishedByAdministratorId: Guid;
  releaseNotes: string;
  schemaVersion: string;
  snapshotHash: string;
  snapshotLength: number;
  sourceDraftId: Guid;
  sourceDraftRevision: number;
}

export interface GameConfigVersion extends GameConfigVersionSummary {
  content: string;
}

export interface GameConfigEnvironment {
  activatedAtUtc: UtcDateTime;
  activatedByAdministratorId: Guid;
  activeVersionId: Guid;
  configurationSet: string;
  contentHash: string;
  environment: string;
  previousVersionId: Guid | null;
  reason: null | string;
  revision: number;
  schemaVersion: string;
}

export interface GameConfigSnapshot {
  configurationSet: string;
  configurationVersion: Guid;
  content: unknown;
  createdAtUtc: UtcDateTime;
  formatVersion: string;
  integrity: {
    algorithm: string;
    canonicalization: string;
    contentHash: string;
  };
  schemaVersion: string;
}

export interface ListGameConfigDraftsRequest {
  configurationSet?: string;
  page?: number;
  pageSize?: number;
  search?: string;
  state?: GameConfigDraftState;
}

export interface CreateGameConfigDraftRequest {
  baseVersionId?: Guid | null;
  configurationSet?: string;
  content: string;
  name: string;
  schemaVersion: string;
}

export interface UpdateGameConfigDraftRequest {
  content: string;
  expectedRevision: number;
  name: string;
  schemaVersion: string;
}

export interface ValidateGameConfigDraftRequest {
  expectedRevision: number;
}

export interface PublishGameConfigDraftRequest {
  expectedRevision: number;
  releaseNotes: string;
}

export interface ListGameConfigVersionsRequest {
  configurationSet?: string;
  page?: number;
  pageSize?: number;
  schemaVersion?: string;
}

export interface ActivateGameConfigVersionRequest {
  configurationSet?: string;
  expectedRevision: number;
  reason?: null | string;
  versionId: Guid;
}

export interface RollbackGameConfigEnvironmentRequest {
  configurationSet?: string;
  expectedRevision: number;
  reason: string;
  targetVersionId: Guid;
}
