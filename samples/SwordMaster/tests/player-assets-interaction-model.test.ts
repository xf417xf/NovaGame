import { describe, expect, it, vi } from "vitest";

import {
  PlayerAssetsTransportError,
  SwordMasterPlayerAssetsInteractionModel,
  type PlayerAssetsAttributesResult,
  type PlayerAssetsEquipRequest,
  type PlayerAssetsInteractionTransport,
  type PlayerAssetsMutationRequest,
  type PlayerAssetsMutationResult,
  type PlayerAssetsRequest,
} from "../src/index.js";

const characterId = "11111111-1111-1111-1111-111111111111";
const ownedItemId = "22222222-2222-2222-2222-222222222222";
const configurationVersionId = "33333333-3333-3333-3333-333333333333";
const firstIdempotencyKey = "44444444-4444-4444-4444-444444444444";
const secondIdempotencyKey = "55555555-5555-5555-5555-555555555555";

describe("SwordMasterPlayerAssetsInteractionModel", () => {
  it("loads the authoritative snapshot and exposes server-provided effective attributes", async () => {
    const transport = new StubPlayerAssetsInteractionTransport();
    const response = snapshot(1, 99);
    const pendingResponse = deferred<PlayerAssetsAttributesResult>();
    transport.enqueueGet(() => pendingResponse.promise);
    const model = createModel(transport);

    expect(model.state).toEqual({ phase: "idle" });

    const pendingLoad = model.load();

    expect(model.state).toEqual({ phase: "loading" });
    expect(transport.getAttributesRequests).toEqual([{ characterId }]);

    pendingResponse.resolve(response);
    const state = await pendingLoad;

    expect(state).toBe(model.state);
    expect(state).toEqual({ phase: "ready", snapshot: response });
    expect(state.phase).toBe("ready");
    if (state.phase !== "ready") {
      throw new Error("Expected the interaction to be ready after loading.");
    }
    expect(state.snapshot.attributes[0]?.effectiveValue).toBe(99);
    expect(Object.isFrozen(state.snapshot)).toBe(true);
    expect(Object.isFrozen(state.snapshot.attributes)).toBe(true);
    expect(Object.isFrozen(state.snapshot.attributes[0])).toBe(true);
  });

  it("retries an initial load failure as a read-only load", async () => {
    const transport = new StubPlayerAssetsInteractionTransport();
    const readySnapshot = snapshot(1, 13);
    transport.enqueueGet(() => {
      throw transportError("network", "The server is unavailable.");
    });
    transport.enqueueGet(() => readySnapshot);
    const model = createModel(transport);

    const failed = await model.load();

    expect(failed).toMatchObject({
      canRecover: false,
      failure: {
        kind: "network",
        message: "The server is unavailable.",
        operation: "load",
        transportKind: "network",
      },
      phase: "failed",
      retry: "load",
    });

    const retried = await model.retry();

    expect(retried).toMatchObject({ phase: "ready", snapshot: readySnapshot });
    expect(transport.getAttributesRequests).toHaveLength(2);
    expect(transport.equipRequests).toHaveLength(0);
    expect(transport.unequipRequests).toHaveLength(0);
  });

  it("equips with the current ETag and one logical idempotency key before refreshing", async () => {
    const transport = new StubPlayerAssetsInteractionTransport();
    const before = snapshot(1, 10);
    const after = snapshot(2, 17, true);
    const pendingMutation = deferred<PlayerAssetsMutationResult>();
    const pendingRefresh = deferred<PlayerAssetsAttributesResult>();
    transport.enqueueGet(() => before);
    transport.enqueueEquip(() => pendingMutation.promise);
    transport.enqueueGet(() => pendingRefresh.promise);
    const createIdempotencyKey = vi.fn(() => firstIdempotencyKey);
    const model = createModel(transport, createIdempotencyKey);
    await model.load();

    const pendingEquip = model.equip({ ownedItemId, slot: "weapon" });

    expect(model.state).toMatchObject({
      command: { kind: "equip", ownedItemId, slot: "weapon" },
      phase: "mutating",
      snapshot: before,
    });
    expect(withoutSignal(transport.equipRequests[0]!)).toEqual({
      characterId,
      expectedEtag: '"1"',
      idempotencyKey: firstIdempotencyKey,
      ownedItemId,
      slot: "weapon",
    });

    pendingMutation.resolve({ etag: '"2"', revision: 2 });
    await vi.waitFor(() => expect(transport.getAttributesRequests).toHaveLength(2));
    expect(model.state).toMatchObject({
      command: { kind: "equip", ownedItemId, slot: "weapon" },
      phase: "recovering",
      snapshot: before,
    });

    pendingRefresh.resolve(after);
    const state = await pendingEquip;

    expect(state).toMatchObject({ phase: "ready", snapshot: after });
    expect(createIdempotencyKey).toHaveBeenCalledOnce();
    expect(transport.equipRequests).toHaveLength(1);
  });

  it("unequips with the current ETag and refreshes the displayed assignments", async () => {
    const transport = new StubPlayerAssetsInteractionTransport();
    const before = snapshot(2, 17, true);
    const after = snapshot(3, 10);
    transport.enqueueGet(() => before);
    transport.enqueueUnequip(() => ({ etag: '"3"', revision: 3 }));
    transport.enqueueGet(() => after);
    const createIdempotencyKey = vi.fn(() => firstIdempotencyKey);
    const model = createModel(transport, createIdempotencyKey);
    await model.load();

    const state = await model.unequip({ slot: "weapon" });

    expect(withoutSignal(transport.unequipRequests[0]!)).toEqual({
      characterId,
      expectedEtag: '"2"',
      idempotencyKey: firstIdempotencyKey,
      slot: "weapon",
    });
    expect(state).toMatchObject({ phase: "ready", snapshot: after });
    expect(state.phase).toBe("ready");
    if (state.phase !== "ready") {
      throw new Error("Expected the interaction to be ready after unequipping.");
    }
    expect(state.snapshot.equipmentAssignments).toEqual([]);
    expect(createIdempotencyKey).toHaveBeenCalledOnce();
  });

  it("replays an ambiguous network failure with the exact mutation request", async () => {
    const transport = new StubPlayerAssetsInteractionTransport();
    const before = snapshot(1, 10);
    const after = snapshot(2, 17, true);
    transport.enqueueGet(() => before);
    transport.enqueueEquip(() => {
      throw transportError("network", "The response outcome is unknown.");
    });
    transport.enqueueEquip(() => ({ etag: '"2"', revision: 2 }));
    transport.enqueueGet(() => after);
    const createIdempotencyKey = vi.fn(() => firstIdempotencyKey);
    const model = createModel(transport, createIdempotencyKey);
    await model.load();

    const failed = await model.equip({ ownedItemId, slot: "weapon" });

    expect(failed).toMatchObject({
      canRecover: true,
      command: { kind: "equip", ownedItemId, slot: "weapon" },
      failure: { kind: "network", operation: "equip", transportKind: "network" },
      phase: "failed",
      retry: "mutation",
      snapshot: before,
    });

    const state = await model.retry();

    expect(state).toMatchObject({ phase: "ready", snapshot: after });
    expect(transport.equipRequests.map(withoutSignal)).toEqual([
      {
        characterId,
        expectedEtag: '"1"',
        idempotencyKey: firstIdempotencyKey,
        ownedItemId,
        slot: "weapon",
      },
      {
        characterId,
        expectedEtag: '"1"',
        idempotencyKey: firstIdempotencyKey,
        ownedItemId,
        slot: "weapon",
      },
    ]);
    expect(createIdempotencyKey).toHaveBeenCalledOnce();
  });

  it("preserves one mutation through token acquisition and authorization failures", async () => {
    const transport = new StubPlayerAssetsInteractionTransport();
    const before = snapshot(1, 10);
    const after = snapshot(2, 17, true);
    transport.enqueueGet(() => before);
    transport.enqueueEquip(() => {
      throw transportError("token_unavailable", "The player token is unavailable.");
    });
    transport.enqueueEquip(() => {
      throw transportError(
        "authorization",
        "The player token was rejected.",
        "SwordMaster.PlayerAssets.UnauthorizedPlayer",
        401,
      );
    });
    transport.enqueueEquip(() => ({ etag: '"2"', revision: 2 }));
    transport.enqueueGet(() => after);
    const createIdempotencyKey = vi.fn(() => firstIdempotencyKey);
    const model = createModel(transport, createIdempotencyKey);
    await model.load();

    const tokenFailure = await model.equip({ ownedItemId, slot: "weapon" });

    expect(tokenFailure).toMatchObject({
      failure: {
        kind: "authorization",
        operation: "equip",
        transportKind: "token_unavailable",
      },
      phase: "failed",
      retry: "mutation",
    });

    const authorizationFailure = await model.retry();

    expect(authorizationFailure).toMatchObject({
      canRecover: true,
      failure: {
        code: "SwordMaster.PlayerAssets.UnauthorizedPlayer",
        kind: "authorization",
        operation: "equip",
        status: 401,
        transportKind: "authorization",
      },
      phase: "failed",
      retry: "mutation",
    });

    const state = await model.retry();

    expect(state).toMatchObject({ phase: "ready", snapshot: after });
    expect(transport.equipRequests.map((request) => withoutSignal(request).idempotencyKey)).toEqual(
      [firstIdempotencyKey, firstIdempotencyKey, firstIdempotencyKey],
    );
    expect(createIdempotencyKey).toHaveBeenCalledOnce();
  });

  it("retries only the refresh after a confirmed mutation succeeds", async () => {
    const transport = new StubPlayerAssetsInteractionTransport();
    const before = snapshot(1, 10);
    const after = snapshot(2, 17, true);
    transport.enqueueGet(() => before);
    transport.enqueueEquip(() => ({ etag: '"2"', revision: 2 }));
    transport.enqueueGet(() => {
      throw transportError("network", "The updated attributes could not be read.");
    });
    transport.enqueueGet(() => snapshot(1, 10));
    transport.enqueueGet(() => after);
    const model = createModel(transport);
    await model.load();

    const failed = await model.equip({ ownedItemId, slot: "weapon" });

    expect(failed).toMatchObject({
      canRecover: true,
      command: { kind: "equip", ownedItemId, slot: "weapon" },
      failure: { kind: "network", operation: "refresh", transportKind: "network" },
      phase: "failed",
      retry: "refresh",
      snapshot: before,
    });

    const regressingRefresh = await model.retry();

    expect(regressingRefresh).toMatchObject({
      failure: { kind: "invalid_contract", operation: "refresh" },
      phase: "failed",
      retry: "refresh",
      snapshot: before,
    });

    const state = await model.retry();

    expect(state).toMatchObject({ phase: "ready", snapshot: after });
    expect(transport.equipRequests).toHaveLength(1);
    expect(transport.getAttributesRequests).toHaveLength(4);
  });

  it("refreshes a stale ETag conflict and requires a new command with a new key", async () => {
    const transport = new StubPlayerAssetsInteractionTransport();
    const stale = snapshot(1, 10);
    const current = snapshot(2, 12);
    const equipped = snapshot(3, 19, true);
    const pendingRefresh = deferred<PlayerAssetsAttributesResult>();
    transport.enqueueGet(() => stale);
    transport.enqueueEquip(() => {
      throw transportError(
        "conflict",
        "The PlayerAssets revision is stale.",
        "SwordMaster.PlayerAssets.ConcurrencyConflict",
        409,
      );
    });
    transport.enqueueGet(() => pendingRefresh.promise);
    transport.enqueueEquip(() => ({ etag: '"3"', revision: 3 }));
    transport.enqueueGet(() => equipped);
    const createIdempotencyKey = vi
      .fn<() => string>()
      .mockReturnValueOnce(firstIdempotencyKey)
      .mockReturnValueOnce(secondIdempotencyKey);
    const model = createModel(transport, createIdempotencyKey);
    await model.load();

    const pendingEquip = model.equip({ ownedItemId, slot: "weapon" });
    await vi.waitFor(() => expect(transport.getAttributesRequests).toHaveLength(2));
    expect(model.state).toMatchObject({
      command: { kind: "equip", ownedItemId, slot: "weapon" },
      phase: "recovering",
      snapshot: stale,
    });

    pendingRefresh.resolve(current);
    const conflicted = await pendingEquip;

    expect(conflicted).toMatchObject({
      canRecover: true,
      failure: {
        code: "SwordMaster.PlayerAssets.ConcurrencyConflict",
        kind: "conflict",
        operation: "equip",
      },
      phase: "failed",
      retry: "mutation",
      snapshot: current,
    });
    const state = await model.retry();

    expect(state).toMatchObject({ phase: "ready", snapshot: equipped });
    expect(transport.equipRequests.map(withoutSignal)).toEqual([
      {
        characterId,
        expectedEtag: '"1"',
        idempotencyKey: firstIdempotencyKey,
        ownedItemId,
        slot: "weapon",
      },
      {
        characterId,
        expectedEtag: '"2"',
        idempotencyKey: secondIdempotencyKey,
        ownedItemId,
        slot: "weapon",
      },
    ]);
  });

  it("retries only stale-state refresh when conflict recovery initially fails", async () => {
    const transport = new StubPlayerAssetsInteractionTransport();
    const stale = snapshot(1, 10);
    const current = snapshot(2, 12);
    transport.enqueueGet(() => stale);
    transport.enqueueEquip(() => {
      throw transportError(
        "conflict",
        "The PlayerAssets revision is stale.",
        "SwordMaster.PlayerAssets.ConcurrencyConflict",
        409,
      );
    });
    transport.enqueueGet(() => {
      throw transportError("network", "The stale-state refresh failed.");
    });
    transport.enqueueGet(() => stale);
    transport.enqueueGet(() => current);
    const model = createModel(transport);
    await model.load();

    const failedRefresh = await model.equip({ ownedItemId, slot: "weapon" });

    expect(failedRefresh).toMatchObject({
      canRecover: true,
      failure: {
        kind: "network",
        message: "The stale-state refresh failed.",
        operation: "refresh",
      },
      phase: "failed",
      retry: "refresh",
      snapshot: stale,
    });

    const unchangedRefresh = await model.recover();

    expect(unchangedRefresh).toMatchObject({
      canRecover: true,
      failure: {
        kind: "invalid_contract",
        operation: "refresh",
      },
      phase: "failed",
      retry: "refresh",
      snapshot: stale,
    });

    const recovered = await model.retry();

    expect(recovered).toMatchObject({ phase: "ready", snapshot: current });
    expect(transport.equipRequests).toHaveLength(1);
    expect(transport.getAttributesRequests).toHaveLength(4);
  });

  it("keeps validation failures non-optimistic and recovers through an authoritative load", async () => {
    const transport = new StubPlayerAssetsInteractionTransport();
    const before = snapshot(1, 10);
    const recoveredSnapshot = snapshot(1, 10);
    transport.enqueueGet(() => before);
    transport.enqueueEquip(() => {
      throw transportError(
        "validation",
        "The item is not compatible with this slot.",
        "SwordMaster.PlayerAssets.SlotMismatch",
        422,
        { slot: ["Expected weapon."] },
      );
    });
    const pendingRecovery = deferred<PlayerAssetsAttributesResult>();
    transport.enqueueGet(() => pendingRecovery.promise);
    const model = createModel(transport);
    await model.load();

    const failed = await model.equip({ ownedItemId, slot: "armor" });

    expect(failed).toMatchObject({
      canRecover: true,
      command: { kind: "equip", ownedItemId, slot: "armor" },
      failure: {
        code: "SwordMaster.PlayerAssets.SlotMismatch",
        details: { slot: ["Expected weapon."] },
        kind: "validation",
        operation: "equip",
        status: 422,
      },
      phase: "failed",
      retry: null,
      snapshot: before,
    });
    expect(failed.phase).toBe("failed");
    if (failed.phase !== "failed") {
      throw new Error("Expected validation to produce a failed interaction state.");
    }
    expect(failed.snapshot?.equipmentAssignments).toEqual([]);

    const pending = model.recover();
    expect(model.state).toMatchObject({ phase: "recovering", snapshot: before });
    pendingRecovery.resolve(recoveredSnapshot);
    expect(await pending).toMatchObject({ phase: "ready", snapshot: recoveredSnapshot });
    expect(transport.equipRequests).toHaveLength(1);
  });

  it("does not retry idempotency or domain conflicts and reloads during recovery", async () => {
    const transport = new StubPlayerAssetsInteractionTransport();
    const initial = snapshot(1, 10);
    const afterIdempotencyConflict = snapshot(2, 17, true);
    const afterSlotConflict = snapshot(2, 17, true);
    transport.enqueueGet(() => initial);
    transport.enqueueEquip(() => {
      throw transportError(
        "conflict",
        "The key belongs to another command.",
        "SwordMaster.PlayerAssets.IdempotencyConflict",
        409,
      );
    });
    transport.enqueueGet(() => afterIdempotencyConflict);
    transport.enqueueEquip(() => {
      throw transportError(
        "conflict",
        "The slot is already occupied.",
        "SwordMaster.PlayerAssets.SlotOccupied",
        409,
      );
    });
    transport.enqueueGet(() => afterSlotConflict);
    const createIdempotencyKey = vi
      .fn<() => string>()
      .mockReturnValueOnce(firstIdempotencyKey)
      .mockReturnValueOnce(secondIdempotencyKey);
    const model = createModel(transport, createIdempotencyKey);
    await model.load();

    const idempotencyConflict = await model.equip({ ownedItemId, slot: "weapon" });

    expect(idempotencyConflict).toMatchObject({
      canRecover: true,
      failure: {
        code: "SwordMaster.PlayerAssets.IdempotencyConflict",
        kind: "conflict",
        operation: "equip",
      },
      phase: "failed",
      retry: null,
    });
    expect(await model.recover()).toMatchObject({
      phase: "ready",
      snapshot: afterIdempotencyConflict,
    });

    const slotConflict = await model.equip({ ownedItemId, slot: "weapon" });

    expect(slotConflict).toMatchObject({
      canRecover: true,
      failure: {
        code: "SwordMaster.PlayerAssets.SlotOccupied",
        kind: "conflict",
        operation: "equip",
      },
      phase: "failed",
      retry: null,
    });
    expect(await model.recover()).toMatchObject({ phase: "ready", snapshot: afterSlotConflict });
    expect(transport.equipRequests).toHaveLength(2);
    expect(createIdempotencyKey).toHaveBeenCalledTimes(2);
  });

  it("preserves cancelled mutation recovery and rejects invalid or overlapping operations", async () => {
    const transport = new StubPlayerAssetsInteractionTransport();
    const before = snapshot(1, 10);
    transport.enqueueGet(() => before);
    transport.enqueueEquip(async (request) => {
      request.signal?.throwIfAborted();
      return await new Promise<PlayerAssetsMutationResult>((_resolve, reject) => {
        request.signal?.addEventListener("abort", () => reject(request.signal?.reason), {
          once: true,
        });
      });
    });
    const createIdempotencyKey = vi.fn(() => firstIdempotencyKey);
    const model = createModel(transport, createIdempotencyKey);

    await expect(model.equip({ ownedItemId, slot: "weapon" })).rejects.toThrow();
    expect(createIdempotencyKey).not.toHaveBeenCalled();
    await model.load();

    const controller = new AbortController();
    const pendingEquip = model.equip({
      ownedItemId,
      signal: controller.signal,
      slot: "weapon",
    });
    expect(model.state).toMatchObject({ phase: "mutating" });

    await expect(model.unequip({ slot: "weapon" })).rejects.toThrow();
    expect(transport.unequipRequests).toHaveLength(0);

    controller.abort(new Error("player cancelled the request"));
    const cancelled = await pendingEquip;

    expect(cancelled).toMatchObject({
      canRecover: true,
      command: { kind: "equip", ownedItemId, slot: "weapon" },
      failure: {
        kind: "cancelled",
        operation: "equip",
      },
      phase: "failed",
      retry: "mutation",
      snapshot: before,
    });
    expect(createIdempotencyKey).toHaveBeenCalledOnce();
  });
});

type AttributesHandler = (
  request: PlayerAssetsRequest,
) => PlayerAssetsAttributesResult | Promise<PlayerAssetsAttributesResult>;
type EquipHandler = (
  request: PlayerAssetsEquipRequest,
) => PlayerAssetsMutationResult | Promise<PlayerAssetsMutationResult>;
type UnequipHandler = (
  request: PlayerAssetsMutationRequest,
) => PlayerAssetsMutationResult | Promise<PlayerAssetsMutationResult>;

class StubPlayerAssetsInteractionTransport implements PlayerAssetsInteractionTransport {
  public readonly equipRequests: PlayerAssetsEquipRequest[] = [];
  public readonly getAttributesRequests: PlayerAssetsRequest[] = [];
  public readonly unequipRequests: PlayerAssetsMutationRequest[] = [];

  readonly #equipHandlers: EquipHandler[] = [];
  readonly #getHandlers: AttributesHandler[] = [];
  readonly #unequipHandlers: UnequipHandler[] = [];

  public enqueueGet(handler: AttributesHandler): void {
    this.#getHandlers.push(handler);
  }

  public enqueueEquip(handler: EquipHandler): void {
    this.#equipHandlers.push(handler);
  }

  public enqueueUnequip(handler: UnequipHandler): void {
    this.#unequipHandlers.push(handler);
  }

  public async getAttributes(request: PlayerAssetsRequest): Promise<PlayerAssetsAttributesResult> {
    this.getAttributesRequests.push(request);
    return await takeHandler(this.#getHandlers, "getAttributes")(request);
  }

  public async equip(request: PlayerAssetsEquipRequest): Promise<PlayerAssetsMutationResult> {
    this.equipRequests.push(request);
    return await takeHandler(this.#equipHandlers, "equip")(request);
  }

  public async unequip(request: PlayerAssetsMutationRequest): Promise<PlayerAssetsMutationResult> {
    this.unequipRequests.push(request);
    return await takeHandler(this.#unequipHandlers, "unequip")(request);
  }
}

function createModel(
  transport: PlayerAssetsInteractionTransport,
  createIdempotencyKey: () => string = () => firstIdempotencyKey,
): SwordMasterPlayerAssetsInteractionModel {
  return new SwordMasterPlayerAssetsInteractionModel({
    characterId,
    createIdempotencyKey,
    transport,
  });
}

function snapshot(
  revision: number,
  effectiveValue: number,
  equipped = false,
): PlayerAssetsAttributesResult {
  return {
    attributes: [
      {
        baseValue: 10,
        effectiveValue,
        equipmentValue: equipped ? 7 : 0,
        statCode: "attack",
      },
    ],
    characterId,
    equipmentAssignments: equipped
      ? [
          {
            configurationVersionId,
            configuredItemId: "training-sword",
            modifiers: [{ statCode: "attack", value: 7 }],
            ownedItemId,
            slot: "weapon",
          },
        ]
      : [],
    etag: `"${revision}"`,
    revision,
  };
}

function transportError(
  kind: ConstructorParameters<typeof PlayerAssetsTransportError>[0],
  message: string,
  code?: string,
  status?: number,
  details?: Readonly<Record<string, readonly string[]>>,
): PlayerAssetsTransportError {
  return new PlayerAssetsTransportError(kind, message, {
    ...(code === undefined ? {} : { code }),
    ...(details === undefined ? {} : { details }),
    ...(status === undefined ? {} : { status }),
  });
}

function withoutSignal<T extends PlayerAssetsRequest>(request: T): Omit<T, "signal"> {
  const { signal: _signal, ...value } = request;
  return value;
}

function takeHandler<T>(handlers: T[], operation: string): T {
  const handler = handlers.shift();
  if (handler === undefined) {
    throw new Error(`No ${operation} response was queued.`);
  }
  return handler;
}

function deferred<T>(): {
  readonly promise: Promise<T>;
  readonly reject: (reason?: unknown) => void;
  readonly resolve: (value: T) => void;
} {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });
  return { promise, reject, resolve };
}
