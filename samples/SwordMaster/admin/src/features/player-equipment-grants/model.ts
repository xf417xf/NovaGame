import type {
  PlayerEquipmentGrantCommand,
  PlayerEquipmentGrantFailedState,
  PlayerEquipmentGrantFailure,
  PlayerEquipmentGrantFailureKind,
  PlayerEquipmentGrantHistoryItem,
  PlayerEquipmentGrantInteractionState,
  PlayerEquipmentGrantLookup,
  PlayerEquipmentGrantMutationSnapshot,
  PlayerEquipmentGrantOperation,
  PlayerEquipmentGrantPageSnapshot,
  PlayerEquipmentGrantRetry,
} from './state';

export interface PlayerEquipmentGrantTransport {
  getPage(
    request: PlayerEquipmentGrantLookup,
    signal?: AbortSignal,
  ): Promise<PlayerEquipmentGrantPageSnapshot>;
  grant(
    request: {
      readonly configuredItemId: string;
      readonly idempotencyKey: string;
      readonly ifMatch: string;
      readonly playerId: string;
      readonly reason: string;
    },
    signal?: AbortSignal,
  ): Promise<PlayerEquipmentGrantMutationSnapshot>;
  rollback(
    request: {
      readonly grantId: string;
      readonly idempotencyKey: string;
      readonly ifMatch: string;
      readonly playerId: string;
      readonly reason: string;
    },
    signal?: AbortSignal,
  ): Promise<PlayerEquipmentGrantMutationSnapshot>;
}

export interface PlayerEquipmentGrantInteractionModelOptions {
  readonly createIdempotencyKey: () => string;
  readonly transport: PlayerEquipmentGrantTransport;
}

export interface PlayerEquipmentGrantOperationOptions {
  readonly signal?: AbortSignal;
}

export interface PlayerEquipmentGrantLookupInput
  extends PlayerEquipmentGrantOperationOptions, PlayerEquipmentGrantLookup {}

export interface PlayerEquipmentGrantGrantInput extends PlayerEquipmentGrantOperationOptions {
  readonly configuredItemId: string;
  readonly reason: string;
}

export interface PlayerEquipmentGrantRollbackInput extends PlayerEquipmentGrantOperationOptions {
  readonly grantId: string;
  readonly reason: string;
}

type StateListener = (state: PlayerEquipmentGrantInteractionState) => void;

type RetryPlan =
  | 'conflict_refresh'
  | 'exact_mutation'
  | 'load'
  | 'none'
  | 'post_mutation_refresh';

type PendingMutation =
  | Readonly<{
      command: Extract<PlayerEquipmentGrantCommand, { kind: 'grant' }>;
      kind: 'grant';
      request: {
        readonly configuredItemId: string;
        readonly idempotencyKey: string;
        readonly ifMatch: string;
        readonly playerId: string;
        readonly reason: string;
      };
    }>
  | Readonly<{
      command: Extract<PlayerEquipmentGrantCommand, { kind: 'rollback' }>;
      kind: 'rollback';
      request: {
        readonly grantId: string;
        readonly idempotencyKey: string;
        readonly ifMatch: string;
        readonly playerId: string;
        readonly reason: string;
      };
    }>;

interface ErrorLike {
  readonly code?: unknown;
  readonly details?: unknown;
  readonly message?: unknown;
  readonly status?: unknown;
}

const concurrencyConflictCode =
  'SwordMaster.PlayerAssets.EquipmentGrants.ConcurrencyConflict';
const guidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const canonicalIdempotencyKeyPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const strongEtagPattern = /^"[1-9][0-9]*"$/;
const emptyGuid = '00000000-0000-0000-0000-000000000000';
const maximumConfiguredItemIdLength = 128;
const maximumPage = 2_147_483_647;

export class PlayerEquipmentGrantInteractionError extends Error {
  public readonly code = 'invalid_state';

  public constructor(message: string) {
    super(message);
    this.name = 'PlayerEquipmentGrantInteractionError';
  }
}

export class SwordMasterPlayerEquipmentGrantInteractionModel {
  readonly #createIdempotencyKey: () => string;
  readonly #listeners = new Set<StateListener>();
  readonly #transport: PlayerEquipmentGrantTransport;
  #activeLookup = 0;
  #busy = false;
  #command: PlayerEquipmentGrantCommand | undefined;
  #conflict: PlayerEquipmentGrantFailure | undefined;
  #conflictEtag: string | undefined;
  #lastLookup: PlayerEquipmentGrantLookup | undefined;
  #mutationPreconditionEtag: string | undefined;
  #pendingMutation: PendingMutation | undefined;
  #retryPlan: RetryPlan = 'none';
  #snapshot: PlayerEquipmentGrantPageSnapshot | undefined;
  #state: PlayerEquipmentGrantInteractionState = Object.freeze({
    phase: 'idle',
  });

  public constructor(options: PlayerEquipmentGrantInteractionModelOptions) {
    this.#transport = options.transport;
    this.#createIdempotencyKey = options.createIdempotencyKey;
  }

  public get state(): PlayerEquipmentGrantInteractionState {
    return this.#state;
  }

  public subscribe(listener: StateListener): () => void {
    this.#listeners.add(listener);
    listener(this.#state);
    return () => this.#listeners.delete(listener);
  }

  public async load(
    input: PlayerEquipmentGrantLookupInput,
  ): Promise<PlayerEquipmentGrantInteractionState> {
    this.#assertNotBusy('load grant history');
    this.#assertNoUnresolvedMutation('load grant history');
    const { signal, ...lookup } = input;
    const validationFailure = validateLookup(lookup);
    if (validationFailure) {
      this.#activeLookup += 1;
      this.#snapshot = undefined;
      this.#command = undefined;
      this.#mutationPreconditionEtag = undefined;
      this.#pendingMutation = undefined;
      this.#retryPlan = 'none';
      return this.#setFailed(validationFailure);
    }
    const frozenLookup = Object.freeze({
      ...lookup,
      playerId: canonicalizeGuid(lookup.playerId),
    });
    const lookupVersion = ++this.#activeLookup;
    const samePlayerSnapshot =
      this.#snapshot?.data.playerId === frozenLookup.playerId
        ? this.#snapshot
        : undefined;
    this.#snapshot = samePlayerSnapshot;
    this.#command = undefined;
    this.#conflict = undefined;
    this.#conflictEtag = undefined;
    this.#mutationPreconditionEtag = undefined;
    this.#pendingMutation = undefined;
    this.#lastLookup = frozenLookup;
    this.#retryPlan = 'none';
    this.#setState({
      lookup: frozenLookup,
      phase: 'loading',
      ...(samePlayerSnapshot ? { snapshot: samePlayerSnapshot } : {}),
    });

    try {
      const snapshot = freezeSnapshot(
        await this.#transport.getPage(frozenLookup, signal),
      );
      if (lookupVersion !== this.#activeLookup) {
        return this.#state;
      }
      return this.#setReady(snapshot);
    } catch (error) {
      if (lookupVersion !== this.#activeLookup) {
        return this.#state;
      }
      this.#retryPlan = 'load';
      return this.#setFailed(normalizeFailure(error, 'load', signal));
    }
  }

  public async grant(
    input: PlayerEquipmentGrantGrantInput,
  ): Promise<PlayerEquipmentGrantInteractionState> {
    const snapshot = this.#requireCommandSnapshot('grant equipment');
    const command = Object.freeze({
      configuredItemId: input.configuredItemId,
      kind: 'grant',
      reason: input.reason,
    }) satisfies PlayerEquipmentGrantCommand;
    if (!strongEtagPattern.test(snapshot.etag)) {
      return this.#setCommandFailure(
        command,
        invalidContractFailure(
          command.kind,
          'The current grant-history snapshot has no valid strong ETag.',
        ),
      );
    }
    const validationFailure = validateCommand(command);
    if (validationFailure) {
      return this.#setCommandFailure(command, validationFailure);
    }
    const idempotencyKey = this.#createCommandKey(command);
    if (typeof idempotencyKey !== 'string') {
      return idempotencyKey;
    }
    const pending = Object.freeze({
      command,
      kind: 'grant',
      request: Object.freeze({
        configuredItemId: input.configuredItemId,
        idempotencyKey,
        ifMatch: snapshot.etag,
        playerId: snapshot.data.playerId,
        reason: input.reason,
      }),
    }) satisfies PendingMutation;
    return await this.#runMutation(pending, 'initial', input.signal);
  }

  public async rollback(
    input: PlayerEquipmentGrantRollbackInput,
  ): Promise<PlayerEquipmentGrantInteractionState> {
    const snapshot = this.#requireCommandSnapshot('roll back equipment grant');
    const command = Object.freeze({
      grantId: input.grantId,
      kind: 'rollback',
      reason: input.reason,
    }) satisfies PlayerEquipmentGrantCommand;
    if (!strongEtagPattern.test(snapshot.etag)) {
      return this.#setCommandFailure(
        command,
        invalidContractFailure(
          command.kind,
          'The current grant-history snapshot has no valid strong ETag.',
        ),
      );
    }
    const validationFailure = validateCommand(command);
    if (validationFailure) {
      return this.#setCommandFailure(command, validationFailure);
    }
    const canonicalCommand = Object.freeze({
      ...command,
      grantId: canonicalizeGuid(command.grantId),
    }) satisfies PlayerEquipmentGrantCommand;
    const idempotencyKey = this.#createCommandKey(canonicalCommand);
    if (typeof idempotencyKey !== 'string') {
      return idempotencyKey;
    }
    const pending = Object.freeze({
      command: canonicalCommand,
      kind: 'rollback',
      request: Object.freeze({
        grantId: canonicalCommand.grantId,
        idempotencyKey,
        ifMatch: snapshot.etag,
        playerId: snapshot.data.playerId,
        reason: input.reason,
      }),
    }) satisfies PendingMutation;
    return await this.#runMutation(pending, 'initial', input.signal);
  }

  public async retry(
    options: PlayerEquipmentGrantOperationOptions = {},
  ): Promise<PlayerEquipmentGrantInteractionState> {
    this.#assertNotBusy('retry the failed operation');
    if (this.#state.phase !== 'failed') {
      throw invalidState('retry the failed operation', this.#state.phase);
    }

    switch (this.#retryPlan) {
      case 'load': {
        if (!this.#lastLookup) {
          throw invalidState('retry grant-history lookup', this.#state.phase);
        }
        return await this.load({ ...this.#lastLookup, ...options });
      }
      case 'exact_mutation': {
        if (!this.#pendingMutation) {
          throw invalidState('retry the exact mutation', this.#state.phase);
        }
        return await this.#runMutation(
          this.#pendingMutation,
          'exact_retry',
          options.signal,
        );
      }
      case 'post_mutation_refresh':
      case 'conflict_refresh':
        return await this.#runRefresh(this.#retryPlan, options.signal);
      case 'none':
        throw invalidState('retry the failed operation', this.#state.phase);
    }
  }

  async #runMutation(
    pending: PendingMutation,
    attempt: 'exact_retry' | 'initial',
    signal?: AbortSignal,
  ): Promise<PlayerEquipmentGrantInteractionState> {
    this.#assertNotBusy('start another mutation');
    const snapshot = this.#snapshot;
    if (!snapshot) {
      throw invalidState('mutate equipment grants', this.#state.phase);
    }

    this.#busy = true;
    this.#activeLookup += 1;
    this.#command = pending.command;
    this.#pendingMutation = pending;
    this.#retryPlan = 'none';
    this.#conflict = undefined;
    this.#conflictEtag = undefined;
    this.#mutationPreconditionEtag = undefined;
    this.#setState({
      attempt,
      command: pending.command,
      phase: 'mutating',
      snapshot,
    });

    try {
      await (pending.kind === 'grant'
        ? this.#transport.grant(pending.request, signal)
        : this.#transport.rollback(pending.request, signal));
      this.#mutationPreconditionEtag = pending.request.ifMatch;
      this.#pendingMutation = undefined;
      this.#retryPlan = 'post_mutation_refresh';
      return await this.#refreshAfterMutation(signal);
    } catch (error) {
      const failure = normalizeFailure(error, pending.kind, signal);
      if (failure.kind === 'conflict') {
        this.#pendingMutation = undefined;
        this.#conflict = failure;
        this.#conflictEtag = pending.request.ifMatch;
        this.#retryPlan = 'conflict_refresh';
        return await this.#refreshAfterConflict(signal);
      }

      if (isAmbiguousMutationFailure(failure)) {
        this.#retryPlan = 'exact_mutation';
      } else {
        this.#pendingMutation = undefined;
        this.#retryPlan = 'none';
      }
      return this.#setFailed(failure);
    } finally {
      this.#busy = false;
    }
  }

  async #runRefresh(
    plan: 'conflict_refresh' | 'post_mutation_refresh',
    signal?: AbortSignal,
  ): Promise<PlayerEquipmentGrantInteractionState> {
    this.#busy = true;
    try {
      return plan === 'conflict_refresh'
        ? await this.#refreshAfterConflict(signal)
        : await this.#refreshAfterMutation(signal);
    } finally {
      this.#busy = false;
    }
  }

  async #refreshAfterMutation(
    signal?: AbortSignal,
  ): Promise<PlayerEquipmentGrantInteractionState> {
    this.#setRefreshing('mutation_succeeded');
    try {
      const snapshot = await this.#loadAuthoritativePage(signal);
      if (snapshot.etag === this.#mutationPreconditionEtag) {
        throw new PlayerEquipmentGrantRefreshContractError(
          'The authoritative grant-history refresh returned the pre-mutation ETag.',
        );
      }
      return this.#setReady(snapshot);
    } catch (error) {
      this.#retryPlan = 'post_mutation_refresh';
      return this.#setFailed(normalizeFailure(error, 'refresh', signal));
    }
  }

  async #refreshAfterConflict(
    signal?: AbortSignal,
  ): Promise<PlayerEquipmentGrantInteractionState> {
    this.#setRefreshing('conflict');
    try {
      const snapshot = await this.#loadAuthoritativePage(signal);
      if (
        this.#conflict?.code === concurrencyConflictCode &&
        snapshot.etag === this.#conflictEtag
      ) {
        throw new PlayerEquipmentGrantRefreshContractError(
          'The conflict refresh returned the stale PlayerAssets ETag.',
        );
      }

      this.#snapshot = snapshot;
      this.#retryPlan = 'none';
      return this.#setFailed(this.#conflict ?? unexpectedFailure('refresh'));
    } catch (error) {
      this.#retryPlan = 'conflict_refresh';
      return this.#setFailed(normalizeFailure(error, 'refresh', signal));
    }
  }

  async #loadAuthoritativePage(
    signal?: AbortSignal,
  ): Promise<PlayerEquipmentGrantPageSnapshot> {
    const snapshot = this.#snapshot;
    if (!snapshot) {
      throw invalidState('refresh grant history', this.#state.phase);
    }
    const lookup = Object.freeze({
      page: 1,
      pageSize: snapshot.data.pageSize,
      playerId: snapshot.data.playerId,
    });
    this.#lastLookup = lookup;
    return freezeSnapshot(await this.#transport.getPage(lookup, signal));
  }

  #setRefreshing(reason: 'conflict' | 'manual' | 'mutation_succeeded'): void {
    this.#setState({
      command: this.#command,
      phase: 'refreshing',
      reason,
      snapshot: this.#snapshot,
    });
  }

  #setReady(
    snapshot: PlayerEquipmentGrantPageSnapshot,
  ): PlayerEquipmentGrantInteractionState {
    this.#snapshot = snapshot;
    this.#lastLookup = Object.freeze({
      page: snapshot.data.page,
      pageSize: snapshot.data.pageSize,
      playerId: snapshot.data.playerId,
    });
    this.#command = undefined;
    this.#conflict = undefined;
    this.#conflictEtag = undefined;
    this.#mutationPreconditionEtag = undefined;
    this.#pendingMutation = undefined;
    this.#retryPlan = 'none';
    return this.#setState(
      snapshot.data.items.length === 0
        ? { phase: 'empty', snapshot }
        : { phase: 'ready', snapshot },
    );
  }

  #setFailed(
    failure: PlayerEquipmentGrantFailure,
  ): PlayerEquipmentGrantFailedState {
    const retry = publicRetry(this.#retryPlan);
    return this.#setState({
      canStartNewCommand: this.#snapshot !== undefined && retry === null,
      command: this.#command,
      failure,
      phase: 'failed',
      retry,
      snapshot: this.#snapshot,
    });
  }

  #setState<TState extends PlayerEquipmentGrantInteractionState>(
    state: TState,
  ): TState {
    const frozen = Object.freeze(state) as TState;
    this.#state = frozen;
    for (const listener of this.#listeners) {
      listener(frozen);
    }
    return frozen;
  }

  #requireCommandSnapshot(action: string): PlayerEquipmentGrantPageSnapshot {
    this.#assertNotBusy(action);
    if (
      !this.#snapshot ||
      (this.#state.phase !== 'empty' &&
        this.#state.phase !== 'ready' &&
        !(this.#state.phase === 'failed' && this.#state.canStartNewCommand))
    ) {
      throw invalidState(action, this.#state.phase);
    }
    return this.#snapshot;
  }

  #createCommandKey(
    command: PlayerEquipmentGrantCommand,
  ): PlayerEquipmentGrantFailedState | string {
    let idempotencyKey: string;
    try {
      idempotencyKey = this.#createIdempotencyKey();
    } catch {
      return this.#setCommandFailure(
        command,
        validationFailure(
          command.kind,
          'A canonical idempotency key could not be created.',
        ),
      );
    }
    if (
      !canonicalIdempotencyKeyPattern.test(idempotencyKey) ||
      idempotencyKey === emptyGuid
    ) {
      return this.#setCommandFailure(
        command,
        validationFailure(
          command.kind,
          'The idempotency key must be a non-empty lowercase D-format GUID.',
        ),
      );
    }
    return idempotencyKey;
  }

  #setCommandFailure(
    command: PlayerEquipmentGrantCommand,
    failure: PlayerEquipmentGrantFailure,
  ): PlayerEquipmentGrantFailedState {
    this.#command = command;
    this.#mutationPreconditionEtag = undefined;
    this.#pendingMutation = undefined;
    this.#retryPlan = 'none';
    return this.#setFailed(failure);
  }

  #assertNoUnresolvedMutation(action: string): void {
    if (
      this.#retryPlan === 'exact_mutation' ||
      this.#retryPlan === 'post_mutation_refresh' ||
      this.#retryPlan === 'conflict_refresh'
    ) {
      throw invalidState(action, this.#state.phase);
    }
  }

  #assertNotBusy(action: string): void {
    if (this.#busy) {
      throw invalidState(action, this.#state.phase);
    }
  }
}

function normalizeFailure(
  error: unknown,
  operation: PlayerEquipmentGrantOperation,
  signal?: AbortSignal,
): PlayerEquipmentGrantFailure {
  if (signal?.aborted || isAbortError(error)) {
    return Object.freeze({
      kind: 'cancelled',
      message: 'The equipment-grant operation was cancelled.',
      operation,
    });
  }
  if (error instanceof PlayerEquipmentGrantRefreshContractError) {
    return Object.freeze({
      kind: 'invalid_contract',
      message: error.message,
      operation,
    });
  }

  const source = asErrorLike(error);
  const status = typeof source.status === 'number' ? source.status : undefined;
  const code = typeof source.code === 'string' ? source.code : undefined;
  const message =
    typeof source.message === 'string'
      ? source.message
      : 'The equipment-grant operation failed unexpectedly.';
  const details = freezeDetails(source.details);
  return Object.freeze({
    kind: classifyFailure(status, code),
    message,
    operation,
    ...(code === undefined ? {} : { code }),
    ...(details === undefined ? {} : { details }),
    ...(status === undefined ? {} : { status }),
  });
}

function validateLookup(
  lookup: PlayerEquipmentGrantLookup,
): PlayerEquipmentGrantFailure | undefined {
  if (!isNonEmptyGuid(lookup.playerId)) {
    return validationFailure(
      'load',
      'The player identifier must be a non-empty D-format GUID.',
    );
  }
  if (!Number.isSafeInteger(lookup.page) || lookup.page <= 0) {
    return validationFailure('load', 'The page must be a positive integer.');
  }
  if (
    !Number.isSafeInteger(lookup.pageSize) ||
    lookup.pageSize <= 0 ||
    lookup.pageSize > 100
  ) {
    return validationFailure(
      'load',
      'The page size must be an integer from 1 through 100.',
    );
  }
  if (
    lookup.page > maximumPage ||
    (lookup.page - 1) * lookup.pageSize > maximumPage
  ) {
    return validationFailure(
      'load',
      'The page is outside the supported range.',
    );
  }
  return undefined;
}

function validateCommand(
  command: PlayerEquipmentGrantCommand,
): PlayerEquipmentGrantFailure | undefined {
  if (
    command.reason.length === 0 ||
    command.reason.length > 500 ||
    command.reason !== command.reason.trim() ||
    /\p{Cc}/u.test(command.reason)
  ) {
    return validationFailure(
      command.kind,
      'The operator reason must be non-empty and no longer than 500 characters.',
    );
  }
  if (
    command.kind === 'grant' &&
    (command.configuredItemId.length === 0 ||
      command.configuredItemId.length > maximumConfiguredItemIdLength ||
      command.configuredItemId !== command.configuredItemId.trim() ||
      /[\s\p{Cc}]/u.test(command.configuredItemId))
  ) {
    return validationFailure(
      command.kind,
      'The configured-item identifier must be non-empty.',
    );
  }
  if (command.kind === 'rollback' && !isNonEmptyGuid(command.grantId)) {
    return validationFailure(
      command.kind,
      'The grant identifier must be a non-empty D-format GUID.',
    );
  }
  return undefined;
}

function validationFailure(
  operation: PlayerEquipmentGrantOperation,
  message: string,
): PlayerEquipmentGrantFailure {
  return Object.freeze({ kind: 'validation', message, operation });
}

function invalidContractFailure(
  operation: PlayerEquipmentGrantOperation,
  message: string,
): PlayerEquipmentGrantFailure {
  return Object.freeze({ kind: 'invalid_contract', message, operation });
}

function isNonEmptyGuid(value: string): boolean {
  return (
    typeof value === 'string' &&
    guidPattern.test(value) &&
    value.toLowerCase() !== emptyGuid
  );
}

function canonicalizeGuid(value: string): string {
  return value.toLowerCase();
}

function classifyFailure(
  status: number | undefined,
  code: string | undefined,
): PlayerEquipmentGrantFailureKind {
  if (code === 'NovaGame.InvalidResponse') return 'invalid_contract';
  if (status === 0) return 'network';
  if (status === 400 || status === 422) return 'validation';
  if (status === 401) return 'unauthenticated';
  if (status === 403) return 'forbidden';
  if (status === 404) return 'not_found';
  if (status === 409) return 'conflict';
  if (status !== undefined && status >= 500) return 'server';
  return 'unexpected';
}

function isAmbiguousMutationFailure(
  failure: PlayerEquipmentGrantFailure,
): boolean {
  return (
    failure.kind === 'cancelled' ||
    failure.kind === 'invalid_contract' ||
    failure.kind === 'network' ||
    failure.kind === 'server' ||
    failure.kind === 'unexpected'
  );
}

function freezeSnapshot(
  snapshot: PlayerEquipmentGrantPageSnapshot,
): PlayerEquipmentGrantPageSnapshot {
  return Object.freeze({
    data: Object.freeze({
      ...snapshot.data,
      items: Object.freeze(
        snapshot.data.items.map((item) => freezeHistoryItem(item)),
      ),
    }),
    etag: snapshot.etag,
  });
}

function freezeHistoryItem(
  item: PlayerEquipmentGrantHistoryItem,
): PlayerEquipmentGrantHistoryItem {
  return Object.freeze({ ...item });
}

function freezeDetails(
  value: unknown,
): Readonly<Record<string, readonly string[]>> | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return undefined;
  }
  const details: Record<string, readonly string[]> = {};
  for (const [key, messages] of Object.entries(value)) {
    if (
      !Array.isArray(messages) ||
      !messages.every((item) => typeof item === 'string')
    ) {
      continue;
    }
    details[key] = Object.freeze([...messages]);
  }
  return Object.freeze(details);
}

function asErrorLike(error: unknown): ErrorLike {
  return error && typeof error === 'object' ? (error as ErrorLike) : {};
}

function isAbortError(error: unknown): boolean {
  return (
    typeof DOMException !== 'undefined' &&
    error instanceof DOMException &&
    error.name === 'AbortError'
  );
}

function publicRetry(plan: RetryPlan): null | PlayerEquipmentGrantRetry {
  switch (plan) {
    case 'load':
      return 'load';
    case 'exact_mutation':
      return 'exact_mutation';
    case 'conflict_refresh':
    case 'post_mutation_refresh':
      return 'refresh';
    case 'none':
      return null;
  }
}

function unexpectedFailure(
  operation: PlayerEquipmentGrantOperation,
): PlayerEquipmentGrantFailure {
  return Object.freeze({
    kind: 'unexpected',
    message: 'The equipment-grant operation failed unexpectedly.',
    operation,
  });
}

function invalidState(
  action: string,
  phase: PlayerEquipmentGrantInteractionState['phase'],
): PlayerEquipmentGrantInteractionError {
  return new PlayerEquipmentGrantInteractionError(
    `Cannot ${action} while equipment-grant interaction is '${phase}'.`,
  );
}

class PlayerEquipmentGrantRefreshContractError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = 'PlayerEquipmentGrantRefreshContractError';
  }
}
