import {
  GameConfigClient,
  GameConfigClientError,
  MemoryGameConfigSnapshotCache,
  type GameConfigRecord,
  type GameConfigSnapshotCache,
  type GameConfigTransport,
} from "@novagame/game-config-client";
import {
  GameConfigBootstrap,
  type GameConfigBootstrapResult,
} from "@novagame/cocos-creator-game-config";

import { AssetSnapshotTransport, type GameConfigAssetLoader } from "./asset-snapshot-transport.js";

export interface GameConfigRecordQuery {
  readonly collection: string;
  readonly id: string;
}

interface SwordMasterGameConfigBootstrapCommonOptions {
  readonly cache?: GameConfigSnapshotCache;
  readonly cacheKey?: string;
  readonly configurationSet?: string;
  readonly query: GameConfigRecordQuery;
}

type SwordMasterGameConfigBootstrapSource =
  | {
      readonly assetLoader: GameConfigAssetLoader;
      readonly transport?: never;
    }
  | {
      readonly assetLoader?: never;
      readonly transport: GameConfigTransport;
    };

export type SwordMasterGameConfigBootstrapComponentOptions =
  SwordMasterGameConfigBootstrapCommonOptions & SwordMasterGameConfigBootstrapSource;

export interface SwordMasterGameConfigBootstrapResult {
  readonly lifecycle: GameConfigBootstrapResult;
  readonly record: GameConfigRecord | undefined;
}

export class SwordMasterGameConfigBootstrapComponent {
  readonly #bootstrap: GameConfigBootstrap;
  readonly #query: GameConfigRecordQuery;

  public constructor(options: SwordMasterGameConfigBootstrapComponentOptions) {
    const client = new GameConfigClient({
      cache: options.cache ?? new MemoryGameConfigSnapshotCache(),
      cacheKey: options.cacheKey ?? "swordmaster.game-config.default",
      configurationSet: options.configurationSet ?? "default",
      transport: resolveTransport(options),
    });

    this.#bootstrap = new GameConfigBootstrap(client);
    this.#query = options.query;
  }

  public async start(
    options: {
      readonly signal?: AbortSignal | undefined;
    } = {},
  ): Promise<SwordMasterGameConfigBootstrapResult> {
    const lifecycle = await this.#bootstrap.start(options);
    return {
      lifecycle,
      record: lifecycle.snapshot?.getRecord(this.#query.collection, this.#query.id),
    };
  }
}

function resolveTransport(
  options: SwordMasterGameConfigBootstrapComponentOptions,
): GameConfigTransport {
  if (options.assetLoader !== undefined && options.transport !== undefined) {
    throw new GameConfigClientError(
      "invalid_contract",
      "Provide either a game-configuration asset loader or a transport, not both.",
    );
  }
  if (options.transport !== undefined) {
    return options.transport;
  }
  if (options.assetLoader !== undefined) {
    return new AssetSnapshotTransport(options.assetLoader);
  }
  throw new GameConfigClientError(
    "invalid_contract",
    "A game-configuration asset loader or transport is required.",
  );
}
