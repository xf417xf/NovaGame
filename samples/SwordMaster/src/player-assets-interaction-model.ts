import { PlayerAssetsTransportError } from "./player-assets-http-transport.js";
import type {
  PlayerAssetsAttributesResult,
  PlayerAssetsEquipRequest,
  PlayerAssetsErrorDetails,
  PlayerAssetsMutationRequest,
  PlayerAssetsMutationResult,
  PlayerAssetsRequest,
  PlayerAssetsTransportErrorKind,
} from "./player-assets-http-transport.js";

export interface PlayerAssetsInteractionTransport {
  getAttributes(request: PlayerAssetsRequest): Promise<PlayerAssetsAttributesResult>;
  equip(request: PlayerAssetsEquipRequest): Promise<PlayerAssetsMutationResult>;
  unequip(request: PlayerAssetsMutationRequest): Promise<PlayerAssetsMutationResult>;
}

export interface PlayerAssetsInteractionModelOptions {
  readonly characterId: string;
  readonly transport: PlayerAssetsInteractionTransport;
  readonly createIdempotencyKey: () => string;
}

export interface PlayerAssetsInteractionOperationOptions {
  readonly signal?: AbortSignal | undefined;
}

export interface PlayerAssetsInteractionEquipInput extends PlayerAssetsInteractionOperationOptions {
  readonly ownedItemId: string;
  readonly slot: string;
}

export interface PlayerAssetsInteractionUnequipInput extends PlayerAssetsInteractionOperationOptions {
  readonly slot: string;
}

export type PlayerAssetsInteractionCommand =
  | Readonly<{
      kind: "equip";
      ownedItemId: string;
      slot: string;
    }>
  | Readonly<{
      kind: "unequip";
      slot: string;
    }>;

export type PlayerAssetsInteractionFailureKind =
  | "authorization"
  | "cancelled"
  | "conflict"
  | "invalid_contract"
  | "network"
  | "not_found"
  | "server"
  | "unexpected"
  | "validation";

export type PlayerAssetsInteractionOperation = "equip" | "load" | "refresh" | "unequip";

export interface PlayerAssetsInteractionFailure {
  readonly code?: string;
  readonly details?: PlayerAssetsErrorDetails;
  readonly kind: PlayerAssetsInteractionFailureKind;
  readonly message: string;
  readonly operation: PlayerAssetsInteractionOperation;
  readonly status?: number;
  readonly transportKind?: PlayerAssetsTransportErrorKind;
}

export type PlayerAssetsInteractionRetry = "load" | "mutation" | "refresh";

export type PlayerAssetsInteractionRecoveryReason =
  | "manual"
  | "mutation_succeeded"
  | "stale_conflict";

export interface PlayerAssetsInteractionIdleState {
  readonly phase: "idle";
}

export interface PlayerAssetsInteractionLoadingState {
  readonly phase: "loading";
  readonly snapshot?: PlayerAssetsAttributesResult;
}

export interface PlayerAssetsInteractionReadyState {
  readonly phase: "ready";
  readonly snapshot: PlayerAssetsAttributesResult;
}

export interface PlayerAssetsInteractionMutatingState {
  readonly attempt: "initial" | "retry";
  readonly command: PlayerAssetsInteractionCommand;
  readonly phase: "mutating";
  readonly snapshot: PlayerAssetsAttributesResult;
}

export interface PlayerAssetsInteractionRecoveringState {
  readonly command?: PlayerAssetsInteractionCommand;
  readonly phase: "recovering";
  readonly reason: PlayerAssetsInteractionRecoveryReason;
  readonly snapshot?: PlayerAssetsAttributesResult;
}

export interface PlayerAssetsInteractionFailedState {
  readonly canRecover: boolean;
  readonly command?: PlayerAssetsInteractionCommand;
  readonly failure: PlayerAssetsInteractionFailure;
  readonly phase: "failed";
  readonly retry: PlayerAssetsInteractionRetry | null;
  readonly snapshot?: PlayerAssetsAttributesResult;
}

export type PlayerAssetsInteractionState =
  | PlayerAssetsInteractionFailedState
  | PlayerAssetsInteractionIdleState
  | PlayerAssetsInteractionLoadingState
  | PlayerAssetsInteractionMutatingState
  | PlayerAssetsInteractionReadyState
  | PlayerAssetsInteractionRecoveringState;

export type PlayerAssetsInteractionErrorCode = "invalid_state";

export class PlayerAssetsInteractionError extends Error {
  public readonly code: PlayerAssetsInteractionErrorCode;

  public constructor(code: PlayerAssetsInteractionErrorCode, message: string) {
    super(message);
    this.name = "PlayerAssetsInteractionError";
    this.code = code;
  }
}

type RetryPlan =
  | "load"
  | "mutation"
  | "none"
  | "post_mutation_refresh"
  | "recovery"
  | "stale_mutation"
  | "stale_refresh";

type PendingMutation =
  | Readonly<{
      command: Extract<PlayerAssetsInteractionCommand, { kind: "equip" }>;
      request: Omit<PlayerAssetsEquipRequest, "signal">;
    }>
  | Readonly<{
      command: Extract<PlayerAssetsInteractionCommand, { kind: "unequip" }>;
      request: Omit<PlayerAssetsMutationRequest, "signal">;
    }>;

export class SwordMasterPlayerAssetsInteractionModel {
  readonly #characterId: string;
  readonly #createIdempotencyKey: () => string;
  readonly #transport: PlayerAssetsInteractionTransport;
  #busy = false;
  #command: PlayerAssetsInteractionCommand | undefined;
  #minimumRefreshRevision: number | undefined;
  #pendingMutation: PendingMutation | undefined;
  #retryPlan: RetryPlan = "none";
  #snapshot: PlayerAssetsAttributesResult | undefined;
  #staleConflict: PlayerAssetsInteractionFailure | undefined;
  #staleExpectedEtag: string | undefined;
  #state: PlayerAssetsInteractionState = Object.freeze({ phase: "idle" });

  public constructor(options: PlayerAssetsInteractionModelOptions) {
    this.#characterId = options.characterId;
    this.#transport = options.transport;
    this.#createIdempotencyKey = options.createIdempotencyKey;
  }

  public get state(): PlayerAssetsInteractionState {
    return this.#state;
  }

  public async load(
    options: PlayerAssetsInteractionOperationOptions = {},
  ): Promise<PlayerAssetsInteractionState> {
    this.#assertPhase("load", "idle");
    return await this.#run(async () => await this.#load(options.signal));
  }

  public async equip(
    input: PlayerAssetsInteractionEquipInput,
  ): Promise<PlayerAssetsInteractionState> {
    this.#assertPhase("equip", "ready");
    const command = Object.freeze({
      kind: "equip",
      ownedItemId: input.ownedItemId,
      slot: input.slot,
    }) satisfies PlayerAssetsInteractionCommand;

    return await this.#run(async () => await this.#startMutation(command, "initial", input.signal));
  }

  public async unequip(
    input: PlayerAssetsInteractionUnequipInput,
  ): Promise<PlayerAssetsInteractionState> {
    this.#assertPhase("unequip", "ready");
    const command = Object.freeze({
      kind: "unequip",
      slot: input.slot,
    }) satisfies PlayerAssetsInteractionCommand;

    return await this.#run(async () => await this.#startMutation(command, "initial", input.signal));
  }

  public async retry(
    options: PlayerAssetsInteractionOperationOptions = {},
  ): Promise<PlayerAssetsInteractionState> {
    this.#assertRetryAvailable();
    return await this.#run(async () => {
      switch (this.#retryPlan) {
        case "load":
          return await this.#load(options.signal);
        case "mutation":
          return await this.#retryMutation(options.signal);
        case "post_mutation_refresh":
          return await this.#retryPostMutationRefresh(options.signal);
        case "recovery":
          return await this.#recover(options.signal);
        case "stale_mutation":
          return await this.#retryStaleMutation(options.signal);
        case "stale_refresh":
          return await this.#retryStaleRefresh(options.signal);
        case "none":
          throw invalidState("retry", this.#state.phase);
      }
    });
  }

  public async recover(
    options: PlayerAssetsInteractionOperationOptions = {},
  ): Promise<PlayerAssetsInteractionState> {
    this.#assertRecoveryAvailable();
    return await this.#run(async () => await this.#recover(options.signal));
  }

  async #run(
    operation: () => Promise<PlayerAssetsInteractionState>,
  ): Promise<PlayerAssetsInteractionState> {
    if (this.#busy) {
      throw invalidState("start another operation while one is active", this.#state.phase);
    }

    this.#busy = true;
    try {
      return await operation();
    } finally {
      this.#busy = false;
    }
  }

  async #load(signal?: AbortSignal): Promise<PlayerAssetsInteractionState> {
    this.#setState({
      phase: "loading",
      ...(this.#snapshot === undefined ? {} : { snapshot: this.#snapshot }),
    });

    try {
      const snapshot = await this.#getAttributes(signal);
      return this.#setReady(snapshot);
    } catch (error) {
      this.#retryPlan = "load";
      return this.#setFailed(normalizeFailure(error, "load", signal));
    }
  }

  async #startMutation(
    command: PlayerAssetsInteractionCommand,
    attempt: "initial" | "retry",
    signal?: AbortSignal,
  ): Promise<PlayerAssetsInteractionState> {
    const snapshot = this.#snapshot;
    if (snapshot === undefined) {
      throw invalidState(command.kind, this.#state.phase);
    }

    let pendingMutation: PendingMutation;
    try {
      pendingMutation = this.#createPendingMutation(command, snapshot.etag);
    } catch (error) {
      this.#command = command;
      this.#retryPlan = attempt === "retry" ? "stale_mutation" : "none";
      return this.#setFailed(normalizeFailure(error, command.kind, signal));
    }

    this.#command = command;
    this.#minimumRefreshRevision = undefined;
    this.#pendingMutation = pendingMutation;
    this.#staleConflict = undefined;
    this.#staleExpectedEtag = undefined;
    this.#retryPlan = "none";
    this.#setState({
      attempt,
      command,
      phase: "mutating",
      snapshot,
    });

    return await this.#executeMutation(pendingMutation, signal);
  }

  #createPendingMutation(
    command: PlayerAssetsInteractionCommand,
    expectedEtag: string,
  ): PendingMutation {
    const idempotencyKey = this.#createIdempotencyKey().toLowerCase();
    if (command.kind === "equip") {
      return Object.freeze({
        command,
        request: Object.freeze({
          characterId: this.#characterId,
          expectedEtag,
          idempotencyKey,
          ownedItemId: command.ownedItemId,
          slot: command.slot,
        }),
      });
    }

    return Object.freeze({
      command,
      request: Object.freeze({
        characterId: this.#characterId,
        expectedEtag,
        idempotencyKey,
        slot: command.slot,
      }),
    });
  }

  async #executeMutation(
    pendingMutation: PendingMutation,
    signal?: AbortSignal,
  ): Promise<PlayerAssetsInteractionState> {
    try {
      const mutationResult = await waitForOperation(async () => {
        if ("ownedItemId" in pendingMutation.request) {
          return await this.#transport.equip(withSignal(pendingMutation.request, signal));
        }
        return await this.#transport.unequip(withSignal(pendingMutation.request, signal));
      }, signal);

      this.#minimumRefreshRevision = mutationResult.revision;
      this.#pendingMutation = undefined;
      this.#retryPlan = "post_mutation_refresh";
      this.#setRecovering("mutation_succeeded");
      return await this.#refreshAfterMutation(signal);
    } catch (error) {
      const failure = normalizeFailure(error, pendingMutation.command.kind, signal);
      if (isConcurrencyConflict(failure)) {
        this.#pendingMutation = undefined;
        this.#staleConflict = failure;
        this.#staleExpectedEtag = pendingMutation.request.expectedEtag;
        this.#retryPlan = "stale_refresh";
        this.#setRecovering("stale_conflict");
        return await this.#refreshAfterConflict(signal);
      }

      if (isAmbiguousMutationFailure(failure)) {
        this.#retryPlan = "mutation";
      } else {
        this.#pendingMutation = undefined;
        this.#retryPlan = "none";
      }
      return this.#setFailed(failure);
    }
  }

  async #refreshAfterMutation(signal?: AbortSignal): Promise<PlayerAssetsInteractionState> {
    try {
      const snapshot = await this.#getAttributes(signal);
      this.#assertRefreshRevision(snapshot);
      return this.#setReady(snapshot);
    } catch (error) {
      this.#retryPlan = "post_mutation_refresh";
      return this.#setFailed(normalizeFailure(error, "refresh", signal));
    }
  }

  async #refreshAfterConflict(signal?: AbortSignal): Promise<PlayerAssetsInteractionState> {
    try {
      const snapshot = await this.#getAttributes(signal);
      this.#assertStaleRefreshEtag(snapshot);
      this.#snapshot = snapshot;
      this.#retryPlan = "stale_mutation";
      return this.#setFailed(
        this.#staleConflict ?? unexpectedFailure("refresh", "The stale-state conflict was lost."),
      );
    } catch (error) {
      this.#retryPlan = "stale_refresh";
      return this.#setFailed(normalizeFailure(error, "refresh", signal));
    }
  }

  async #retryMutation(signal?: AbortSignal): Promise<PlayerAssetsInteractionState> {
    const pendingMutation = this.#pendingMutation;
    const snapshot = this.#snapshot;
    if (pendingMutation === undefined || snapshot === undefined) {
      throw invalidState("retry the mutation", this.#state.phase);
    }

    this.#setState({
      attempt: "retry",
      command: pendingMutation.command,
      phase: "mutating",
      snapshot,
    });
    return await this.#executeMutation(pendingMutation, signal);
  }

  async #retryPostMutationRefresh(signal?: AbortSignal): Promise<PlayerAssetsInteractionState> {
    this.#setRecovering("mutation_succeeded");
    return await this.#refreshAfterMutation(signal);
  }

  async #retryStaleRefresh(signal?: AbortSignal): Promise<PlayerAssetsInteractionState> {
    if (this.#staleConflict === undefined || this.#command === undefined) {
      throw invalidState("refresh stale state", this.#state.phase);
    }

    this.#setRecovering("stale_conflict");
    return await this.#refreshAfterConflict(signal);
  }

  async #retryStaleMutation(signal?: AbortSignal): Promise<PlayerAssetsInteractionState> {
    const command = this.#command;
    if (command === undefined || this.#snapshot === undefined) {
      throw invalidState("retry the stale mutation", this.#state.phase);
    }

    return await this.#startMutation(command, "retry", signal);
  }

  async #recover(signal?: AbortSignal): Promise<PlayerAssetsInteractionState> {
    this.#retryPlan = "recovery";
    this.#setRecovering("manual");
    try {
      const snapshot = await this.#getAttributes(signal);
      this.#assertStaleRefreshEtag(snapshot);
      this.#assertRefreshRevision(snapshot);
      return this.#setReady(snapshot);
    } catch (error) {
      this.#retryPlan = "recovery";
      return this.#setFailed(normalizeFailure(error, "refresh", signal));
    }
  }

  async #getAttributes(signal?: AbortSignal): Promise<PlayerAssetsAttributesResult> {
    const snapshot = await waitForOperation(
      async () =>
        await this.#transport.getAttributes(withSignal({ characterId: this.#characterId }, signal)),
      signal,
    );
    return freezeSnapshot(snapshot);
  }

  #assertRefreshRevision(snapshot: PlayerAssetsAttributesResult): void {
    const minimumRevision = this.#minimumRefreshRevision;
    if (minimumRevision !== undefined && snapshot.revision < minimumRevision) {
      throw new PlayerAssetsRefreshContractError(
        `The authoritative PlayerAssets refresh revision ${snapshot.revision} is older than the confirmed mutation revision ${minimumRevision}.`,
      );
    }
  }

  #assertStaleRefreshEtag(snapshot: PlayerAssetsAttributesResult): void {
    if (this.#staleExpectedEtag !== undefined && snapshot.etag === this.#staleExpectedEtag) {
      throw new PlayerAssetsRefreshContractError(
        "The stale-state PlayerAssets refresh returned the same ETag as the rejected mutation.",
      );
    }
  }

  #setReady(snapshot: PlayerAssetsAttributesResult): PlayerAssetsInteractionReadyState {
    this.#snapshot = snapshot;
    this.#command = undefined;
    this.#minimumRefreshRevision = undefined;
    this.#pendingMutation = undefined;
    this.#retryPlan = "none";
    this.#staleConflict = undefined;
    this.#staleExpectedEtag = undefined;
    return this.#setState({ phase: "ready", snapshot });
  }

  #setRecovering(
    reason: PlayerAssetsInteractionRecoveryReason,
  ): PlayerAssetsInteractionRecoveringState {
    return this.#setState({
      phase: "recovering",
      reason,
      ...(this.#snapshot === undefined ? {} : { snapshot: this.#snapshot }),
      ...(this.#command === undefined ? {} : { command: this.#command }),
    });
  }

  #setFailed(failure: PlayerAssetsInteractionFailure): PlayerAssetsInteractionFailedState {
    return this.#setState({
      canRecover:
        this.#snapshot !== undefined ||
        this.#pendingMutation !== undefined ||
        this.#command !== undefined,
      failure,
      phase: "failed",
      retry: publicRetry(this.#retryPlan),
      ...(this.#snapshot === undefined ? {} : { snapshot: this.#snapshot }),
      ...(this.#command === undefined ? {} : { command: this.#command }),
    });
  }

  #setState<TState extends PlayerAssetsInteractionState>(state: TState): TState {
    const frozenState = Object.freeze(state) as TState;
    this.#state = frozenState;
    return frozenState;
  }

  #assertPhase(action: string, phase: PlayerAssetsInteractionState["phase"]): void {
    if (this.#busy || this.#state.phase !== phase) {
      throw invalidState(action, this.#state.phase);
    }
  }

  #assertRetryAvailable(): void {
    if (this.#busy || this.#state.phase !== "failed" || this.#retryPlan === "none") {
      throw invalidState("retry", this.#state.phase);
    }
  }

  #assertRecoveryAvailable(): void {
    if (this.#busy || this.#state.phase !== "failed" || !this.#state.canRecover) {
      throw invalidState("recover", this.#state.phase);
    }
  }
}

function withSignal<TRequest extends object>(
  request: TRequest,
  signal?: AbortSignal,
): TRequest & { readonly signal?: AbortSignal | undefined } {
  return signal === undefined ? request : { ...request, signal };
}

async function waitForOperation<T>(operation: () => Promise<T>, signal?: AbortSignal): Promise<T> {
  signal?.throwIfAborted();
  const promise = operation();
  if (signal === undefined) {
    return await promise;
  }

  return await new Promise<T>((resolve, reject) => {
    const onAbort = () => {
      signal.removeEventListener("abort", onAbort);
      reject(signal.reason);
    };
    signal.addEventListener("abort", onAbort, { once: true });
    if (signal.aborted) {
      onAbort();
      return;
    }

    void promise.then(
      (value) => {
        signal.removeEventListener("abort", onAbort);
        resolve(value);
      },
      (error: unknown) => {
        signal.removeEventListener("abort", onAbort);
        reject(error);
      },
    );
  });
}

function normalizeFailure(
  error: unknown,
  operation: PlayerAssetsInteractionOperation,
  signal?: AbortSignal,
): PlayerAssetsInteractionFailure {
  if (signal?.aborted || isAbortError(error)) {
    return Object.freeze({
      kind: "cancelled",
      message: "The PlayerAssets operation was cancelled.",
      operation,
    });
  }

  if (error instanceof PlayerAssetsRefreshContractError) {
    return Object.freeze({
      kind: "invalid_contract",
      message: error.message,
      operation,
    });
  }

  if (!(error instanceof PlayerAssetsTransportError)) {
    return unexpectedFailure(operation, "The PlayerAssets operation failed unexpectedly.");
  }

  return Object.freeze({
    kind: interactionFailureKind(error.kind),
    message: error.message,
    operation,
    transportKind: error.kind,
    ...(error.code === undefined ? {} : { code: error.code }),
    ...(error.details === undefined ? {} : { details: freezeErrorDetails(error.details) }),
    ...(error.status === undefined ? {} : { status: error.status }),
  });
}

function interactionFailureKind(
  kind: PlayerAssetsTransportErrorKind,
): PlayerAssetsInteractionFailureKind {
  switch (kind) {
    case "authorization":
    case "token_unavailable":
      return "authorization";
    case "conflict":
      return "conflict";
    case "invalid_contract":
      return "invalid_contract";
    case "invalid_request":
    case "validation":
      return "validation";
    case "network":
      return "network";
    case "not_found":
      return "not_found";
    case "server":
      return "server";
    case "unexpected_response":
      return "unexpected";
  }
}

function isConcurrencyConflict(failure: PlayerAssetsInteractionFailure): boolean {
  return (
    failure.kind === "conflict" && failure.code === "SwordMaster.PlayerAssets.ConcurrencyConflict"
  );
}

function isAmbiguousMutationFailure(failure: PlayerAssetsInteractionFailure): boolean {
  return (
    failure.kind === "authorization" ||
    failure.kind === "cancelled" ||
    failure.kind === "invalid_contract" ||
    failure.kind === "network" ||
    failure.kind === "server" ||
    failure.kind === "unexpected"
  );
}

function isAbortError(error: unknown): boolean {
  return (
    typeof DOMException !== "undefined" &&
    error instanceof DOMException &&
    error.name === "AbortError"
  );
}

class PlayerAssetsRefreshContractError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "PlayerAssetsRefreshContractError";
  }
}

function freezeSnapshot(snapshot: PlayerAssetsAttributesResult): PlayerAssetsAttributesResult {
  return Object.freeze({
    attributes: Object.freeze(
      snapshot.attributes.map((attribute) => Object.freeze({ ...attribute })),
    ),
    characterId: snapshot.characterId,
    equipmentAssignments: Object.freeze(
      snapshot.equipmentAssignments.map((assignment) =>
        Object.freeze({
          ...assignment,
          modifiers: Object.freeze(
            assignment.modifiers.map((modifier) => Object.freeze({ ...modifier })),
          ),
        }),
      ),
    ),
    etag: snapshot.etag,
    revision: snapshot.revision,
  });
}

function freezeErrorDetails(details: PlayerAssetsErrorDetails): PlayerAssetsErrorDetails {
  const copy: Record<string, readonly string[]> = {};
  for (const [key, messages] of Object.entries(details)) {
    copy[key] = Object.freeze([...messages]);
  }
  return Object.freeze(copy);
}

function unexpectedFailure(
  operation: PlayerAssetsInteractionOperation,
  message: string,
): PlayerAssetsInteractionFailure {
  return Object.freeze({ kind: "unexpected", message, operation });
}

function publicRetry(plan: RetryPlan): PlayerAssetsInteractionRetry | null {
  switch (plan) {
    case "load":
      return "load";
    case "mutation":
    case "stale_mutation":
      return "mutation";
    case "post_mutation_refresh":
    case "recovery":
    case "stale_refresh":
      return "refresh";
    case "none":
      return null;
  }
}

function invalidState(action: string, phase: PlayerAssetsInteractionState["phase"]): Error {
  return new PlayerAssetsInteractionError(
    "invalid_state",
    `Cannot ${action} while the PlayerAssets interaction is '${phase}'.`,
  );
}
