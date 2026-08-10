import type {
  PlayerAssetsInteractionEquipInput,
  PlayerAssetsInteractionState,
  PlayerAssetsInteractionUnequipInput,
  SwordMasterPlayerAssetsInteractionModel,
} from "./player-assets-interaction-model.js";

export type PlayerAssetsCocosComponentModel = Pick<
  SwordMasterPlayerAssetsInteractionModel,
  "equip" | "load" | "recover" | "retry" | "state" | "unequip"
>;

export interface PlayerAssetsCocosComponentOptions {
  readonly model: PlayerAssetsCocosComponentModel;
}

export type PlayerAssetsCocosEquipInput = Omit<PlayerAssetsInteractionEquipInput, "signal">;

export type PlayerAssetsCocosUnequipInput = Omit<PlayerAssetsInteractionUnequipInput, "signal">;

export type PlayerAssetsCocosComponentErrorCode = "destroyed" | "model_reused";

export class PlayerAssetsCocosComponentError extends Error {
  public readonly code: PlayerAssetsCocosComponentErrorCode;

  public constructor(code: PlayerAssetsCocosComponentErrorCode, message: string) {
    super(message);
    this.name = "PlayerAssetsCocosComponentError";
    this.code = code;
  }
}

type PlayerAssetsCocosAction = "equip" | "recover" | "retry" | "start" | "unequip";

const claimedModels = new WeakSet<PlayerAssetsCocosComponentModel>();

export class SwordMasterPlayerAssetsCocosComponent {
  readonly #lifetime = new AbortController();
  readonly #model: PlayerAssetsCocosComponentModel;
  #destroyed = false;
  #generation = 0;

  public constructor(options: PlayerAssetsCocosComponentOptions) {
    if (claimedModels.has(options.model)) {
      throw modelReusedError();
    }
    claimedModels.add(options.model);
    this.#model = options.model;
  }

  public get state(): PlayerAssetsInteractionState {
    this.#assertAlive("read state");
    return this.#model.state;
  }

  public async start(): Promise<PlayerAssetsInteractionState> {
    return await this.#invoke("start", async (signal) => await this.#model.load({ signal }));
  }

  public async equip(input: PlayerAssetsCocosEquipInput): Promise<PlayerAssetsInteractionState> {
    return await this.#invoke(
      "equip",
      async (signal) => await this.#model.equip({ ...input, signal }),
    );
  }

  public async unequip(
    input: PlayerAssetsCocosUnequipInput,
  ): Promise<PlayerAssetsInteractionState> {
    return await this.#invoke(
      "unequip",
      async (signal) => await this.#model.unequip({ ...input, signal }),
    );
  }

  public async retry(): Promise<PlayerAssetsInteractionState> {
    return await this.#invoke("retry", async (signal) => await this.#model.retry({ signal }));
  }

  public async recover(): Promise<PlayerAssetsInteractionState> {
    return await this.#invoke("recover", async (signal) => await this.#model.recover({ signal }));
  }

  public onDestroy(): void {
    if (this.#destroyed) {
      return;
    }

    this.#destroyed = true;
    this.#generation += 1;
    this.#lifetime.abort(destroyedError("complete an active operation"));
  }

  async #invoke(
    action: PlayerAssetsCocosAction,
    operation: (signal: AbortSignal) => Promise<PlayerAssetsInteractionState>,
  ): Promise<PlayerAssetsInteractionState> {
    const generation = this.#captureGeneration(action);
    try {
      const state = await waitForLifetime(operation(this.#lifetime.signal), this.#lifetime.signal);
      this.#assertCurrent(action, generation);
      return state;
    } catch (error) {
      this.#assertCurrent(action, generation);
      throw error;
    }
  }

  #captureGeneration(action: PlayerAssetsCocosAction): number {
    this.#assertAlive(action);
    return this.#generation;
  }

  #assertAlive(action: string): void {
    if (this.#destroyed) {
      throw destroyedError(action);
    }
  }

  #assertCurrent(action: PlayerAssetsCocosAction, generation: number): void {
    if (this.#destroyed || generation !== this.#generation) {
      throw destroyedError(action);
    }
  }
}

async function waitForLifetime<T>(operation: Promise<T>, signal: AbortSignal): Promise<T> {
  return await new Promise<T>((resolve, reject) => {
    const onAbort = () => {
      signal.removeEventListener("abort", onAbort);
      reject(signal.reason);
    };
    signal.addEventListener("abort", onAbort, { once: true });
    void operation.then(
      (value) => {
        signal.removeEventListener("abort", onAbort);
        resolve(value);
      },
      (error: unknown) => {
        signal.removeEventListener("abort", onAbort);
        reject(error);
      },
    );
    if (signal.aborted) {
      onAbort();
    }
  });
}

function destroyedError(action: string): PlayerAssetsCocosComponentError {
  return new PlayerAssetsCocosComponentError(
    "destroyed",
    `Cannot ${action} after the SwordMaster PlayerAssets Cocos component was destroyed.`,
  );
}

function modelReusedError(): PlayerAssetsCocosComponentError {
  return new PlayerAssetsCocosComponentError(
    "model_reused",
    "A SwordMaster PlayerAssets interaction model must belong to exactly one Cocos component lifecycle and cannot be reused after destruction.",
  );
}
