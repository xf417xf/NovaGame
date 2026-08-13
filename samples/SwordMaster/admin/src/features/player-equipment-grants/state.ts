export type PlayerEquipmentGrantStatus = 1 | 2;

export interface PlayerEquipmentGrantHistoryItem {
  readonly configurationVersionId: string;
  readonly configuredItemId: string;
  readonly grantId: string;
  readonly grantReason: string;
  readonly grantResultingRevision: number;
  readonly grantedAtUtc: string;
  readonly grantedByAdministratorId: string;
  readonly ownedItemId: string;
  readonly playerId: string;
  readonly rollbackReason: null | string;
  readonly rollbackResultingRevision: null | number;
  readonly rolledBackAtUtc: null | string;
  readonly rolledBackByAdministratorId: null | string;
  readonly status: PlayerEquipmentGrantStatus;
}

export interface PlayerEquipmentGrantPage {
  readonly items: readonly PlayerEquipmentGrantHistoryItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly playerId: string;
  readonly revision: number;
  readonly totalCount: number;
}

export interface PlayerEquipmentGrantPageSnapshot {
  readonly data: PlayerEquipmentGrantPage;
  readonly etag: string;
}

export interface PlayerEquipmentGrantMutationData {
  readonly configurationVersionId: string;
  readonly configuredItemId: string;
  readonly grantId: string;
  readonly grantReason: string;
  readonly grantedAtUtc: string;
  readonly grantedByAdministratorId: string;
  readonly ownedItemId: string;
  readonly playerId: string;
  readonly resultingRevision: number;
  readonly rollbackReason: null | string;
  readonly rolledBackAtUtc: null | string;
  readonly rolledBackByAdministratorId: null | string;
  readonly status: PlayerEquipmentGrantStatus;
}

export interface PlayerEquipmentGrantMutationSnapshot {
  readonly data: PlayerEquipmentGrantMutationData;
  readonly etag: string;
}

export interface PlayerEquipmentGrantLookup {
  readonly page: number;
  readonly pageSize: number;
  readonly playerId: string;
}

export type PlayerEquipmentGrantCommand =
  | Readonly<{
      configuredItemId: string;
      kind: 'grant';
      reason: string;
    }>
  | Readonly<{
      grantId: string;
      kind: 'rollback';
      reason: string;
    }>;

export type PlayerEquipmentGrantFailureKind =
  | 'cancelled'
  | 'conflict'
  | 'forbidden'
  | 'invalid_contract'
  | 'network'
  | 'not_found'
  | 'server'
  | 'unauthenticated'
  | 'unexpected'
  | 'validation';

export type PlayerEquipmentGrantOperation =
  | 'grant'
  | 'load'
  | 'refresh'
  | 'rollback';

export interface PlayerEquipmentGrantFailure {
  readonly code?: string;
  readonly details?: Readonly<Record<string, readonly string[]>>;
  readonly kind: PlayerEquipmentGrantFailureKind;
  readonly message: string;
  readonly operation: PlayerEquipmentGrantOperation;
  readonly status?: number;
}

export type PlayerEquipmentGrantRetry = 'exact_mutation' | 'load' | 'refresh';

export interface PlayerEquipmentGrantIdleState {
  readonly phase: 'idle';
}

export interface PlayerEquipmentGrantLoadingState {
  readonly lookup: PlayerEquipmentGrantLookup;
  readonly phase: 'loading';
  readonly snapshot?: PlayerEquipmentGrantPageSnapshot;
}

export interface PlayerEquipmentGrantReadyState {
  readonly phase: 'ready';
  readonly snapshot: PlayerEquipmentGrantPageSnapshot;
}

export interface PlayerEquipmentGrantEmptyState {
  readonly phase: 'empty';
  readonly snapshot: PlayerEquipmentGrantPageSnapshot;
}

export interface PlayerEquipmentGrantMutatingState {
  readonly attempt: 'exact_retry' | 'initial';
  readonly command: PlayerEquipmentGrantCommand;
  readonly phase: 'mutating';
  readonly snapshot: PlayerEquipmentGrantPageSnapshot;
}

export interface PlayerEquipmentGrantRefreshingState {
  readonly command?: PlayerEquipmentGrantCommand;
  readonly phase: 'refreshing';
  readonly reason: 'conflict' | 'manual' | 'mutation_succeeded';
  readonly snapshot?: PlayerEquipmentGrantPageSnapshot;
}

export interface PlayerEquipmentGrantFailedState {
  readonly canStartNewCommand: boolean;
  readonly command?: PlayerEquipmentGrantCommand;
  readonly failure: PlayerEquipmentGrantFailure;
  readonly phase: 'failed';
  readonly retry: null | PlayerEquipmentGrantRetry;
  readonly snapshot?: PlayerEquipmentGrantPageSnapshot;
}

export type PlayerEquipmentGrantInteractionState =
  | PlayerEquipmentGrantEmptyState
  | PlayerEquipmentGrantFailedState
  | PlayerEquipmentGrantIdleState
  | PlayerEquipmentGrantLoadingState
  | PlayerEquipmentGrantMutatingState
  | PlayerEquipmentGrantReadyState
  | PlayerEquipmentGrantRefreshingState;
