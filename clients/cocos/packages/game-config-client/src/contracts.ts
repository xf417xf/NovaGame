import type { LosslessNumber } from "lossless-json";

export const GAME_CONFIG_FORMAT_VERSION = "1";
export const GAME_CONFIG_SCHEMA_VERSION = "1";
export const GAME_CONFIG_HASH_ALGORITHM = "SHA-256";
export const GAME_CONFIG_CANONICALIZATION = "NovaGame.GameConfig.CanonicalJson.v1";

export type LosslessJsonValue =
  | boolean
  | LosslessNumber
  | null
  | string
  | readonly LosslessJsonValue[]
  | { readonly [property: string]: LosslessJsonValue };

export interface GameConfigRecord {
  readonly id: string;
  readonly [property: string]: LosslessJsonValue;
}

export interface GameConfigContent {
  readonly collections: Readonly<Record<string, readonly GameConfigRecord[]>>;
}

export interface GameConfigSnapshotIntegrity {
  readonly algorithm: typeof GAME_CONFIG_HASH_ALGORITHM;
  readonly canonicalization: typeof GAME_CONFIG_CANONICALIZATION;
  readonly contentHash: string;
}

export interface GameConfigSnapshot {
  readonly configurationSet: string;
  readonly configurationVersion: string;
  readonly content: GameConfigContent;
  readonly createdAtUtc: string;
  readonly etag: string;
  readonly formatVersion: typeof GAME_CONFIG_FORMAT_VERSION;
  readonly integrity: GameConfigSnapshotIntegrity;
  readonly schemaVersion: typeof GAME_CONFIG_SCHEMA_VERSION;
  readonly snapshotHash: string;

  getCollection(name: string): readonly GameConfigRecord[];
  getRecord(collection: string, id: string): GameConfigRecord | undefined;
}

export interface GameConfigTransportRequest {
  readonly ifNoneMatch?: string;
  readonly signal?: AbortSignal;
}

export type GameConfigTransportResponse =
  | {
      readonly body: Uint8Array;
      readonly etag: string;
      readonly status: 200;
    }
  | {
      readonly etag?: string;
      readonly status: 304;
    }
  | {
      readonly status: 404;
    };

export interface GameConfigTransport {
  fetchSnapshot(request: GameConfigTransportRequest): Promise<GameConfigTransportResponse>;
}

export interface CachedGameConfigSnapshot {
  readonly body: Uint8Array;
  readonly snapshotHash: string;
}

export interface GameConfigSnapshotCache {
  read(cacheKey: string): Promise<CachedGameConfigSnapshot | null>;

  /** Atomically replaces the persisted active entry or leaves the old entry intact on failure. */
  write(cacheKey: string, snapshot: CachedGameConfigSnapshot): Promise<void>;
}

export interface Sha256Digest {
  digestHex(bytes: Uint8Array): Promise<string>;
}

export type GameConfigClientErrorCode =
  | "canonicalization_mismatch"
  | "cache_miss"
  | "cache_read_failed"
  | "cache_write_failed"
  | "configuration_set_mismatch"
  | "configuration_version_mismatch"
  | "content_hash_mismatch"
  | "duplicate_record_id"
  | "hash_failed"
  | "invalid_contract"
  | "invalid_etag"
  | "invalid_json"
  | "invalid_utf8"
  | "operation_aborted"
  | "snapshot_hash_mismatch"
  | "snapshot_missing"
  | "stale_response"
  | "transport_failed"
  | "unsupported_format"
  | "unsupported_integrity"
  | "unsupported_schema";

export type GameConfigLoadResult =
  | {
      readonly snapshot: GameConfigSnapshot;
      readonly status: "activated" | "restored" | "revalidated" | "unchanged";
    }
  | {
      readonly error: import("./errors.js").GameConfigClientError;
      readonly snapshot: GameConfigSnapshot;
      readonly status: "fallback";
    }
  | {
      readonly error: import("./errors.js").GameConfigClientError;
      readonly snapshot: GameConfigSnapshot | null;
      readonly status: "rejected";
    };

export interface GameConfigClientOptions {
  readonly cache: GameConfigSnapshotCache;
  readonly cacheKey: string;
  readonly configurationSet: string;
  readonly expectedConfigurationVersion?: string;
  readonly sha256?: Sha256Digest;
  readonly transport: GameConfigTransport;
}

export interface ValidateGameConfigSnapshotOptions {
  readonly body: Uint8Array;
  readonly etag: string;
  readonly expectedConfigurationSet: string;
  readonly expectedConfigurationVersion?: string;
  readonly sha256?: Sha256Digest;
}
