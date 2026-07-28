import type { CachedGameConfigSnapshot, GameConfigSnapshotCache } from "./contracts.js";

export class MemoryGameConfigSnapshotCache implements GameConfigSnapshotCache {
  readonly #entries = new Map<string, CachedGameConfigSnapshot>();

  async read(cacheKey: string): Promise<CachedGameConfigSnapshot | null> {
    const entry = this.#entries.get(cacheKey);
    return entry
      ? {
          body: Uint8Array.from(entry.body),
          snapshotHash: entry.snapshotHash,
        }
      : null;
  }

  async write(cacheKey: string, snapshot: CachedGameConfigSnapshot): Promise<void> {
    this.#entries.set(cacheKey, {
      body: Uint8Array.from(snapshot.body),
      snapshotHash: snapshot.snapshotHash,
    });
  }
}
