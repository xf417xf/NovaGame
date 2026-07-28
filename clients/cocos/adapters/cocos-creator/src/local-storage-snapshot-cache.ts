import type {
  CachedGameConfigSnapshot,
  GameConfigSnapshotCache,
  Sha256Digest,
} from "@novagame/game-config-client";
import { NobleSha256 } from "@novagame/game-config-client";

import type { StorageLike } from "./ports.js";

const base64Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
const snapshotHashPattern = /^[a-f0-9]{64}$/;

type Slot = "a" | "b";

interface CachePointer {
  readonly active: Slot;
  readonly previous: null | Slot;
  readonly version: 1;
}

interface StoredSnapshot {
  readonly body: string;
  readonly snapshotHash: string;
  readonly version: 1;
}

export interface LocalStorageSnapshotCacheOptions {
  readonly keyPrefix?: string;
  readonly sha256?: Sha256Digest;
  readonly storage?: StorageLike;
}

export class LocalStorageSnapshotCache implements GameConfigSnapshotCache {
  private readonly keyPrefix: string;
  private readonly sha256: Sha256Digest;
  private readonly storage: StorageLike;

  public constructor(options: LocalStorageSnapshotCacheOptions = {}) {
    this.keyPrefix = normalizePrefix(options.keyPrefix ?? "novagame.game-config");
    this.sha256 = options.sha256 ?? new NobleSha256();
    this.storage = options.storage ?? resolveLocalStorage();
  }

  public async read(cacheKey: string): Promise<CachedGameConfigSnapshot | null> {
    const keys = this.keys(cacheKey);
    const pointer = readPointer(this.storage, keys.pointer);
    if (pointer === null) {
      return null;
    }

    const active = await readSnapshot(this.storage, keys[pointer.active], this.sha256);
    if (active !== null) {
      return active;
    }
    if (pointer.previous === null) {
      return null;
    }
    return readSnapshot(this.storage, keys[pointer.previous], this.sha256);
  }

  public async write(cacheKey: string, candidate: CachedGameConfigSnapshot): Promise<void> {
    assertCandidate(candidate);
    const keys = this.keys(cacheKey);
    const currentPointer = readPointer(this.storage, keys.pointer);
    const inactiveSlot = currentPointer?.active === "a" ? "b" : "a";
    const storedSnapshot: StoredSnapshot = {
      body: encodeBase64(candidate.body),
      snapshotHash: candidate.snapshotHash,
      version: 1,
    };

    this.storage.setItem(keys[inactiveSlot], JSON.stringify(storedSnapshot));
    const verifiedCandidate = await readSnapshot(this.storage, keys[inactiveSlot], this.sha256);
    if (
      verifiedCandidate === null ||
      verifiedCandidate.snapshotHash !== candidate.snapshotHash ||
      !equalBytes(verifiedCandidate.body, candidate.body)
    ) {
      throw new Error("The snapshot candidate could not be verified in storage.");
    }

    const nextPointer: CachePointer = {
      active: inactiveSlot,
      previous: currentPointer?.active ?? null,
      version: 1,
    };
    this.storage.setItem(keys.pointer, JSON.stringify(nextPointer));
  }

  public clear(cacheKey: string): void {
    const keys = this.keys(cacheKey);
    this.storage.removeItem(keys.pointer);
    this.storage.removeItem(keys.a);
    this.storage.removeItem(keys.b);
  }

  private keys(cacheKey: string): {
    readonly a: string;
    readonly b: string;
    readonly pointer: string;
  } {
    const normalizedCacheKey = cacheKey.trim();
    if (normalizedCacheKey.length === 0) {
      throw new Error("The snapshot cache key is required.");
    }
    const namespace = `${this.keyPrefix}.${encodeURIComponent(normalizedCacheKey)}`;
    return {
      a: `${namespace}.slot-a`,
      b: `${namespace}.slot-b`,
      pointer: `${namespace}.pointer`,
    };
  }
}

function assertCandidate(candidate: CachedGameConfigSnapshot): void {
  if (!snapshotHashPattern.test(candidate.snapshotHash)) {
    throw new Error("The cached snapshot hash must be a lowercase SHA-256 hexadecimal value.");
  }
  if (candidate.body.byteLength === 0) {
    throw new Error("The cached snapshot document must not be empty.");
  }
}

function decodeBase64(value: string): Uint8Array | null {
  if (value.length === 0 || value.length % 4 !== 0 || !/^[A-Za-z0-9+/]*={0,2}$/.test(value)) {
    return null;
  }

  const padding = value.endsWith("==") ? 2 : value.endsWith("=") ? 1 : 0;
  const output = new Uint8Array((value.length / 4) * 3 - padding);
  let outputIndex = 0;
  for (let index = 0; index < value.length; index += 4) {
    const first = base64Alphabet.indexOf(value[index]!);
    const second = base64Alphabet.indexOf(value[index + 1]!);
    const third = value[index + 2] === "=" ? 0 : base64Alphabet.indexOf(value[index + 2]!);
    const fourth = value[index + 3] === "=" ? 0 : base64Alphabet.indexOf(value[index + 3]!);
    if (first < 0 || second < 0 || third < 0 || fourth < 0) {
      return null;
    }
    const combined = (first << 18) | (second << 12) | (third << 6) | fourth;
    if (outputIndex < output.length) {
      output[outputIndex++] = (combined >> 16) & 0xff;
    }
    if (outputIndex < output.length) {
      output[outputIndex++] = (combined >> 8) & 0xff;
    }
    if (outputIndex < output.length) {
      output[outputIndex++] = combined & 0xff;
    }
  }

  return encodeBase64(output) === value ? output : null;
}

function encodeBase64(bytes: Uint8Array): string {
  let output = "";
  for (let index = 0; index < bytes.length; index += 3) {
    const first = bytes[index]!;
    const second = bytes[index + 1];
    const third = bytes[index + 2];
    const combined = (first << 16) | ((second ?? 0) << 8) | (third ?? 0);
    output += base64Alphabet[(combined >> 18) & 0x3f]!;
    output += base64Alphabet[(combined >> 12) & 0x3f]!;
    output += second === undefined ? "=" : base64Alphabet[(combined >> 6) & 0x3f]!;
    output += third === undefined ? "=" : base64Alphabet[combined & 0x3f]!;
  }
  return output;
}

function equalBytes(left: Uint8Array, right: Uint8Array): boolean {
  if (left.byteLength !== right.byteLength) {
    return false;
  }
  for (let index = 0; index < left.byteLength; index += 1) {
    if (left[index] !== right[index]) {
      return false;
    }
  }
  return true;
}

function isPointer(value: unknown): value is CachePointer {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const candidate = value as Partial<CachePointer>;
  return (
    candidate.version === 1 &&
    (candidate.active === "a" || candidate.active === "b") &&
    (candidate.previous === null || candidate.previous === "a" || candidate.previous === "b") &&
    candidate.previous !== candidate.active
  );
}

function normalizePrefix(value: string): string {
  const normalized = value.trim();
  if (normalized.length === 0) {
    throw new Error("The local-storage key prefix is required.");
  }
  return normalized;
}

function readJson(storage: StorageLike, key: string): unknown {
  let value: null | string;
  try {
    value = storage.getItem(key);
  } catch {
    return null;
  }
  if (value === null) {
    return null;
  }
  try {
    return JSON.parse(value) as unknown;
  } catch {
    return null;
  }
}

function readPointer(storage: StorageLike, key: string): CachePointer | null {
  const value = readJson(storage, key);
  return isPointer(value) ? value : null;
}

async function readSnapshot(
  storage: StorageLike,
  key: string,
  sha256: Sha256Digest,
): Promise<CachedGameConfigSnapshot | null> {
  const value = readJson(storage, key);
  if (typeof value !== "object" || value === null) {
    return null;
  }
  const candidate = value as Partial<StoredSnapshot>;
  if (
    candidate.version !== 1 ||
    typeof candidate.body !== "string" ||
    typeof candidate.snapshotHash !== "string" ||
    !snapshotHashPattern.test(candidate.snapshotHash)
  ) {
    return null;
  }
  const body = decodeBase64(candidate.body);
  if (body === null || body.byteLength === 0) {
    return null;
  }
  const actualHash = await sha256.digestHex(body);
  return actualHash === candidate.snapshotHash
    ? { body, snapshotHash: candidate.snapshotHash }
    : null;
}

function resolveLocalStorage(): StorageLike {
  let storage: Storage | undefined;
  try {
    storage = globalThis.localStorage;
  } catch {
    storage = undefined;
  }
  if (storage === undefined) {
    throw new Error(
      "Local storage is unavailable; inject a Cocos-compatible StorageLike implementation.",
    );
  }
  return storage;
}
