import { describe, expect, it } from "vitest";

import {
  PlayerAssetsCocosComponentError,
  SwordMasterPlayerAssetsCocosComponent,
  type PlayerAssetsAttributesResult,
  type PlayerAssetsCocosComponentModel,
  type PlayerAssetsInteractionEquipInput,
  type PlayerAssetsInteractionOperationOptions,
  type PlayerAssetsInteractionReadyState,
  type PlayerAssetsInteractionState,
  type PlayerAssetsInteractionUnequipInput,
} from "../src/index.js";

const characterId = "10000000-0000-0000-0000-000000000001";
const ownedItemId = "20000000-0000-0000-0000-000000000002";
const configurationVersionId = "30000000-0000-0000-0000-000000000003";

describe("SwordMasterPlayerAssetsCocosComponent", () => {
  it("delegates start and exposes authoritative interaction state without deriving attributes", async () => {
    const model = new StubPlayerAssetsCocosComponentModel();
    const loading = Object.freeze({ phase: "loading" }) satisfies PlayerAssetsInteractionState;
    const authoritative = readyState(snapshot(7, -17, 900));
    const load = deferred<PlayerAssetsInteractionState>();
    model.loadImplementation = async () => {
      model.state = loading;
      const result = await load.promise;
      model.state = result;
      return result;
    };
    const component = new SwordMasterPlayerAssetsCocosComponent({ model });

    const pending = component.start();
    await Promise.resolve();

    expect(model.loadCalls).toHaveLength(1);
    expect(model.loadCalls[0]?.signal).toBeInstanceOf(AbortSignal);
    expect(component.state).toBe(loading);

    load.resolve(authoritative);

    await expect(pending).resolves.toBe(authoritative);
    expect(component.state).toBe(authoritative);
    expect(authoritative.snapshot.attributes[0]).toEqual({
      baseValue: 41,
      effectiveValue: -17,
      equipmentValue: 900,
      statCode: "attack",
    });
  });

  it("forwards equip input unchanged with the component lifetime signal", async () => {
    const model = new StubPlayerAssetsCocosComponentModel();
    const before = readyState(snapshot(1, 41, 0));
    const after = readyState(snapshot(2, 53, 12));
    model.state = before;
    model.loadImplementation = async () => before;
    model.equipImplementation = async () => {
      model.state = after;
      return after;
    };
    const component = new SwordMasterPlayerAssetsCocosComponent({ model });

    await component.start();
    const result = await component.equip({ ownedItemId, slot: "weapon" });

    expect(model.equipCalls).toHaveLength(1);
    expect(withoutSignal(model.equipCalls[0]!)).toEqual({ ownedItemId, slot: "weapon" });
    expect(model.equipCalls[0]?.signal).toBe(model.loadCalls[0]?.signal);
    expect(result).toBe(after);
    expect(component.state).toBe(after);
    expect(after.snapshot).toBe(model.state.phase === "ready" ? model.state.snapshot : undefined);
  });

  it("forwards unequip input unchanged without optimistic equipment edits", async () => {
    const model = new StubPlayerAssetsCocosComponentModel();
    const equipped = readyState(snapshot(4, 53, 12, true));
    const unequipped = readyState(snapshot(5, 41, 0));
    model.state = equipped;
    model.loadImplementation = async () => equipped;
    model.unequipImplementation = async () => {
      expect(component.state).toBe(equipped);
      model.state = unequipped;
      return unequipped;
    };
    const component = new SwordMasterPlayerAssetsCocosComponent({ model });

    await component.start();
    const result = await component.unequip({ slot: "weapon" });

    expect(model.unequipCalls).toHaveLength(1);
    expect(withoutSignal(model.unequipCalls[0]!)).toEqual({ slot: "weapon" });
    expect(model.unequipCalls[0]?.signal).toBe(model.loadCalls[0]?.signal);
    expect(result).toBe(unequipped);
    expect(component.state).toBe(unequipped);
  });

  it("delegates retry and recovery with the same lifetime signal and exact model results", async () => {
    const model = new StubPlayerAssetsCocosComponentModel();
    const failed = Object.freeze({
      canRecover: true,
      failure: Object.freeze({
        kind: "network",
        message: "offline",
        operation: "equip",
      }),
      phase: "failed",
      retry: "mutation",
      snapshot: snapshot(8, 53, 12, true),
    }) satisfies PlayerAssetsInteractionState;
    const recovered = readyState(snapshot(9, 53, 12, true));
    model.state = failed;
    model.retryImplementation = async () => failed;
    model.recoverImplementation = async () => {
      model.state = recovered;
      return recovered;
    };
    const component = new SwordMasterPlayerAssetsCocosComponent({ model });

    const retryResult = await component.retry();
    const recoverResult = await component.recover();

    expect(model.retryCalls).toHaveLength(1);
    expect(model.recoverCalls).toHaveLength(1);
    expect(model.retryCalls[0]?.signal).toBe(model.recoverCalls[0]?.signal);
    expect(retryResult).toBe(failed);
    expect(recoverResult).toBe(recovered);
    expect(component.state).toBe(recovered);
  });

  it("passes model errors through by identity and never queues a second command", async () => {
    const model = new StubPlayerAssetsCocosComponentModel();
    const load = deferred<PlayerAssetsInteractionState>();
    const modelError = new Error("The interaction model rejected the overlapping command.");
    model.loadImplementation = async () => await load.promise;
    model.retryImplementation = async () => {
      throw modelError;
    };
    const component = new SwordMasterPlayerAssetsCocosComponent({ model });

    const pendingLoad = component.start();
    const pendingRetry = component.retry();

    expect(model.loadCalls).toHaveLength(1);
    expect(model.retryCalls).toHaveLength(1);
    expect(model.retryCalls[0]?.signal).toBe(model.loadCalls[0]?.signal);
    await expect(pendingRetry).rejects.toBe(modelError);

    const ready = readyState(snapshot(1, 41, 0));
    load.resolve(ready);
    await expect(pendingLoad).resolves.toBe(ready);
  });

  it("aborts an ignored load promptly, guards every later entry, and ignores a late resolve", async () => {
    const model = new StubPlayerAssetsCocosComponentModel();
    const load = deferred<PlayerAssetsInteractionState>();
    model.loadImplementation = async () => await load.promise;
    const component = new SwordMasterPlayerAssetsCocosComponent({ model });

    const pending = component.start();
    await Promise.resolve();
    const lifetimeSignal = model.loadCalls[0]?.signal;

    component.onDestroy();
    component.onDestroy();

    expect(lifetimeSignal?.aborted).toBe(true);
    await expectDestroyed(pending);

    const guardedEntries = [
      () => component.start(),
      () => component.equip({ ownedItemId, slot: "weapon" }),
      () => component.unequip({ slot: "weapon" }),
      () => component.retry(),
      () => component.recover(),
    ];
    for (const invoke of guardedEntries) {
      await expectDestroyed(Promise.resolve().then(invoke));
    }
    expect(() => component.state).toThrowError(PlayerAssetsCocosComponentError);
    expectComponentError(
      () => new SwordMasterPlayerAssetsCocosComponent({ model }),
      "model_reused",
    );

    expect(model.loadCalls).toHaveLength(1);
    expect(model.equipCalls).toHaveLength(0);
    expect(model.unequipCalls).toHaveLength(0);
    expect(model.retryCalls).toHaveLength(0);
    expect(model.recoverCalls).toHaveLength(0);

    load.resolve(readyState(snapshot(11, -1, 400)));
    await flushPromises();
  });

  it("suppresses late mutation success or failure without refresh, replay, or compensation", async () => {
    const successModel = new StubPlayerAssetsCocosComponentModel();
    const successfulMutation = deferred<PlayerAssetsInteractionState>();
    successModel.state = readyState(snapshot(3, 41, 0));
    successModel.equipImplementation = async () => {
      const state = await successfulMutation.promise;
      successModel.state = state;
      return state;
    };
    const successComponent = new SwordMasterPlayerAssetsCocosComponent({ model: successModel });

    const pendingSuccess = successComponent.equip({ ownedItemId, slot: "weapon" });
    await Promise.resolve();
    const successSignal = successModel.equipCalls[0]?.signal;

    successComponent.onDestroy();

    expect(successSignal?.aborted).toBe(true);
    await expectDestroyed(pendingSuccess);

    const lateReady = readyState(snapshot(4, 53, 12, true));
    successfulMutation.resolve(lateReady);
    await flushPromises();

    expect(successModel.state).toBe(lateReady);
    expect(successModel.equipCalls).toHaveLength(1);
    expect(successModel.loadCalls).toHaveLength(0);
    expect(successModel.unequipCalls).toHaveLength(0);
    expect(successModel.retryCalls).toHaveLength(0);
    expect(successModel.recoverCalls).toHaveLength(0);
    expectComponentError(
      () => new SwordMasterPlayerAssetsCocosComponent({ model: successModel }),
      "model_reused",
    );

    const rejectedModel = new StubPlayerAssetsCocosComponentModel();
    const rejectedMutation = deferred<PlayerAssetsInteractionState>();
    rejectedModel.state = readyState(snapshot(5, 41, 0));
    rejectedModel.equipImplementation = async () => await rejectedMutation.promise;
    const rejectedComponent = new SwordMasterPlayerAssetsCocosComponent({ model: rejectedModel });

    const pendingRejection = rejectedComponent.equip({ ownedItemId, slot: "weapon" });
    await Promise.resolve();
    const rejectedSignal = rejectedModel.equipCalls[0]?.signal;

    rejectedComponent.onDestroy();

    expect(rejectedSignal?.aborted).toBe(true);
    await expectDestroyed(pendingRejection);

    rejectedMutation.reject(new Error("late transport rejection"));
    await flushPromises();
  });
});

type LoadImplementation = (
  options: PlayerAssetsInteractionOperationOptions,
) => Promise<PlayerAssetsInteractionState>;
type EquipImplementation = (
  input: PlayerAssetsInteractionEquipInput,
) => Promise<PlayerAssetsInteractionState>;
type UnequipImplementation = (
  input: PlayerAssetsInteractionUnequipInput,
) => Promise<PlayerAssetsInteractionState>;

class StubPlayerAssetsCocosComponentModel implements PlayerAssetsCocosComponentModel {
  public state: PlayerAssetsInteractionState = Object.freeze({ phase: "idle" });

  public readonly equipCalls: PlayerAssetsInteractionEquipInput[] = [];
  public readonly loadCalls: PlayerAssetsInteractionOperationOptions[] = [];
  public readonly recoverCalls: PlayerAssetsInteractionOperationOptions[] = [];
  public readonly retryCalls: PlayerAssetsInteractionOperationOptions[] = [];
  public readonly unequipCalls: PlayerAssetsInteractionUnequipInput[] = [];

  public loadImplementation: LoadImplementation = async () => this.state;
  public equipImplementation: EquipImplementation = async () => this.state;
  public unequipImplementation: UnequipImplementation = async () => this.state;
  public retryImplementation: LoadImplementation = async () => this.state;
  public recoverImplementation: LoadImplementation = async () => this.state;

  public async load(
    options: PlayerAssetsInteractionOperationOptions = {},
  ): Promise<PlayerAssetsInteractionState> {
    this.loadCalls.push(options);
    return await this.loadImplementation(options);
  }

  public async equip(
    input: PlayerAssetsInteractionEquipInput,
  ): Promise<PlayerAssetsInteractionState> {
    this.equipCalls.push(input);
    return await this.equipImplementation(input);
  }

  public async unequip(
    input: PlayerAssetsInteractionUnequipInput,
  ): Promise<PlayerAssetsInteractionState> {
    this.unequipCalls.push(input);
    return await this.unequipImplementation(input);
  }

  public async retry(
    options: PlayerAssetsInteractionOperationOptions = {},
  ): Promise<PlayerAssetsInteractionState> {
    this.retryCalls.push(options);
    return await this.retryImplementation(options);
  }

  public async recover(
    options: PlayerAssetsInteractionOperationOptions = {},
  ): Promise<PlayerAssetsInteractionState> {
    this.recoverCalls.push(options);
    return await this.recoverImplementation(options);
  }
}

function snapshot(
  revision: number,
  effectiveValue: number,
  equipmentValue: number,
  equipped = false,
): PlayerAssetsAttributesResult {
  return Object.freeze({
    attributes: Object.freeze([
      Object.freeze({
        baseValue: 41,
        effectiveValue,
        equipmentValue,
        statCode: "attack",
      }),
    ]),
    characterId,
    equipmentAssignments: Object.freeze(
      equipped
        ? [
            Object.freeze({
              configurationVersionId,
              configuredItemId: "training-sword",
              modifiers: Object.freeze([Object.freeze({ statCode: "attack", value: 12 })]),
              ownedItemId,
              slot: "weapon",
            }),
          ]
        : [],
    ),
    etag: `"${revision}"`,
    revision,
  });
}

function readyState(
  snapshotValue: PlayerAssetsAttributesResult,
): PlayerAssetsInteractionReadyState {
  return Object.freeze({ phase: "ready", snapshot: snapshotValue });
}

function withoutSignal<T extends PlayerAssetsInteractionOperationOptions>(
  input: T,
): Omit<T, "signal"> {
  const { signal: _signal, ...value } = input;
  return value;
}

async function expectDestroyed(operation: Promise<unknown>): Promise<void> {
  const error = await operation.then(
    () => undefined,
    (reason: unknown) => reason,
  );
  expect(error).toBeInstanceOf(PlayerAssetsCocosComponentError);
  expect(error).toMatchObject({ code: "destroyed" });
}

function expectComponentError(operation: () => unknown, code: "destroyed" | "model_reused"): void {
  let error: unknown;
  try {
    operation();
  } catch (reason) {
    error = reason;
  }
  expect(error).toBeInstanceOf(PlayerAssetsCocosComponentError);
  expect(error).toMatchObject({ code });
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

async function flushPromises(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
}
