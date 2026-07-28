import {
  GameConfigClientError,
  type GameConfigTransport,
  type GameConfigTransportRequest,
  type GameConfigTransportResponse,
} from "@novagame/game-config-client";

export const GAME_CONFIG_SNAPSHOT_ASSET = "config/game-config.snapshot.json";
export const GAME_CONFIG_SNAPSHOT_MANIFEST_ASSET = "config/game-config.snapshot.manifest.json";

export type GameConfigAsset = string | Uint8Array;

export interface GameConfigAssetLoadOptions {
  readonly signal?: AbortSignal | undefined;
}

export interface GameConfigAssetLoader {
  load(assetPath: string, options?: GameConfigAssetLoadOptions): Promise<GameConfigAsset | null>;
}

interface GameConfigSnapshotManifest {
  readonly snapshotHash: string;
}

const SNAPSHOT_MANIFEST_PATTERN =
  /^[ \t\r\n]*\{[ \t\r\n]*"snapshotHash"[ \t\r\n]*:[ \t\r\n]*"([0-9a-f]{64})"[ \t\r\n]*\}[ \t\r\n]*$/;

export class AssetSnapshotTransport implements GameConfigTransport {
  readonly #loader: GameConfigAssetLoader;

  public constructor(loader: GameConfigAssetLoader) {
    this.#loader = loader;
  }

  public async fetchSnapshot(
    request: GameConfigTransportRequest,
  ): Promise<GameConfigTransportResponse> {
    request.signal?.throwIfAborted();
    const manifestAsset = await this.#loader.load(GAME_CONFIG_SNAPSHOT_MANIFEST_ASSET, {
      signal: request.signal,
    });
    request.signal?.throwIfAborted();
    if (manifestAsset === null) {
      return { status: 404 };
    }

    const manifest = parseManifest(manifestAsset);
    const etag = `"${manifest.snapshotHash}"`;
    if (request.ifNoneMatch === etag) {
      return { etag, status: 304 };
    }

    const snapshotAsset = await this.#loader.load(GAME_CONFIG_SNAPSHOT_ASSET, {
      signal: request.signal,
    });
    request.signal?.throwIfAborted();
    if (snapshotAsset === null) {
      return { status: 404 };
    }

    return {
      body: toBytes(snapshotAsset),
      etag,
      status: 200,
    };
  }
}

function parseManifest(asset: GameConfigAsset): GameConfigSnapshotManifest {
  const source = toText(asset);
  try {
    JSON.parse(source);
  } catch (error) {
    throw new GameConfigClientError(
      "invalid_contract",
      "The bundled game-configuration manifest is not valid JSON.",
      { cause: error },
    );
  }

  const snapshotHash = SNAPSHOT_MANIFEST_PATTERN.exec(source)?.[1];
  if (snapshotHash === undefined) {
    throw new GameConfigClientError(
      "invalid_contract",
      "The bundled game-configuration manifest fields do not match the contract.",
    );
  }

  return { snapshotHash };
}

function toBytes(asset: GameConfigAsset): Uint8Array {
  return typeof asset === "string" ? new TextEncoder().encode(asset) : Uint8Array.from(asset);
}

function toText(asset: GameConfigAsset): string {
  if (typeof asset === "string") {
    return asset;
  }

  try {
    return new TextDecoder("utf-8", { fatal: true }).decode(asset);
  } catch (error) {
    throw new GameConfigClientError(
      "invalid_contract",
      "The bundled game-configuration manifest is not valid UTF-8.",
      { cause: error },
    );
  }
}
