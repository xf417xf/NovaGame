import type {
  CachedGameConfigSnapshot,
  GameConfigClientOptions,
  GameConfigLoadResult,
  GameConfigSnapshot,
} from "./contracts.js";
import { GameConfigClientError, asGameConfigClientError } from "./errors.js";
import { validateGameConfigSnapshot } from "./snapshot.js";

interface ValidatedCacheEntry {
  readonly cached: CachedGameConfigSnapshot;
  readonly snapshot: GameConfigSnapshot;
}

interface CacheReadResult {
  readonly entry: ValidatedCacheEntry | null;
  readonly error: GameConfigClientError | null;
}

export class GameConfigClient {
  readonly #options: GameConfigClientOptions;
  #active: GameConfigSnapshot | null = null;
  #commitTail: Promise<void> = Promise.resolve();
  #nextGeneration = 0;
  #successfulGeneration = 0;

  constructor(options: GameConfigClientOptions) {
    if (options.cacheKey.trim().length === 0) {
      throw new GameConfigClientError("invalid_contract", "A non-empty cache key is required.");
    }
    this.#options = options;
  }

  get current(): GameConfigSnapshot | null {
    return this.#active;
  }

  async restore(signal?: AbortSignal): Promise<GameConfigLoadResult> {
    const generation = ++this.#nextGeneration;
    if (signal?.aborted) {
      return this.#fallback(generation, abortedError(), null);
    }

    const cached = await this.#readValidatedCache();
    if (signal?.aborted) {
      return this.#fallback(generation, abortedError(), cached.entry);
    }
    if (cached.error) {
      return this.#fallback(generation, cached.error, null);
    }
    if (!cached.entry) {
      return this.#fallback(
        generation,
        new GameConfigClientError(
          "cache_miss",
          "No valid persisted game-configuration snapshot is available.",
        ),
        null,
      );
    }

    return this.#commit(
      generation,
      cached.entry.snapshot,
      cached.entry,
      false,
      cached.entry,
      "restored",
      false,
    );
  }

  async refresh(signal?: AbortSignal): Promise<GameConfigLoadResult> {
    const generation = ++this.#nextGeneration;
    if (signal?.aborted) {
      return this.#fallback(generation, abortedError(), null);
    }
    const cacheRead = await this.#readValidatedCache();
    const cached = cacheRead.entry;
    if (signal?.aborted) {
      return this.#fallback(generation, abortedError(), cached);
    }

    let response: Awaited<ReturnType<GameConfigClientOptions["transport"]["fetchSnapshot"]>>;
    try {
      response = await this.#options.transport.fetchSnapshot({
        ...(cached ? { ifNoneMatch: cached.snapshot.etag } : {}),
        ...(signal ? { signal } : {}),
      });
    } catch (error) {
      return this.#fallback(
        generation,
        signal?.aborted
          ? abortedError()
          : asGameConfigClientError(
              error,
              "transport_failed",
              "Snapshot transport failed before a complete response was received.",
            ),
        cached,
      );
    }

    if (signal?.aborted) {
      return this.#fallback(generation, abortedError(), cached);
    }

    if (response.status === 404) {
      return this.#fallback(
        generation,
        new GameConfigClientError(
          "snapshot_missing",
          "No active game-configuration snapshot is available.",
        ),
        cached,
      );
    }

    if (response.status === 304) {
      if (!cached) {
        return this.#fallback(
          generation,
          new GameConfigClientError(
            "cache_miss",
            "The server returned 304 but no valid matching snapshot is cached.",
          ),
          null,
        );
      }
      if (response.etag !== cached.snapshot.etag) {
        return this.#fallback(
          generation,
          new GameConfigClientError(
            "invalid_etag",
            "The 304 ETag does not match the validated cached snapshot.",
          ),
          cached,
        );
      }
      return this.#commit(generation, cached.snapshot, cached, false, cached, "revalidated", true);
    }

    let candidate: GameConfigSnapshot;
    try {
      candidate = await validateGameConfigSnapshot({
        body: response.body,
        etag: response.etag,
        expectedConfigurationSet: this.#options.configurationSet,
        ...(this.#options.expectedConfigurationVersion !== undefined
          ? {
              expectedConfigurationVersion: this.#options.expectedConfigurationVersion,
            }
          : {}),
        ...(this.#options.sha256 ? { sha256: this.#options.sha256 } : {}),
      });
    } catch (error) {
      return this.#fallback(
        generation,
        asGameConfigClientError(error, "invalid_contract", "Snapshot validation failed."),
        cached,
      );
    }

    if (signal?.aborted) {
      return this.#fallback(generation, abortedError(), cached);
    }

    const entry: ValidatedCacheEntry = {
      cached: {
        body: Uint8Array.from(response.body),
        snapshotHash: candidate.snapshotHash,
      },
      snapshot: candidate,
    };
    return this.#commit(generation, candidate, entry, true, cached, "activated", true);
  }

  async #readValidatedCache(): Promise<CacheReadResult> {
    let cached: CachedGameConfigSnapshot | null;
    try {
      cached = await this.#options.cache.read(this.#options.cacheKey);
    } catch (error) {
      return {
        entry: null,
        error: new GameConfigClientError(
          "cache_read_failed",
          "The persisted game-configuration snapshot could not be read.",
          { cause: error },
        ),
      };
    }
    if (!cached) {
      return { entry: null, error: null };
    }

    try {
      const snapshot = await validateGameConfigSnapshot({
        body: cached.body,
        etag: `"${cached.snapshotHash}"`,
        expectedConfigurationSet: this.#options.configurationSet,
        ...(this.#options.expectedConfigurationVersion !== undefined
          ? {
              expectedConfigurationVersion: this.#options.expectedConfigurationVersion,
            }
          : {}),
        ...(this.#options.sha256 ? { sha256: this.#options.sha256 } : {}),
      });
      return { entry: { cached, snapshot }, error: null };
    } catch (error) {
      return {
        entry: null,
        error: asGameConfigClientError(
          error,
          "invalid_contract",
          "The persisted snapshot failed validation.",
        ),
      };
    }
  }

  async #commit(
    generation: number,
    candidate: GameConfigSnapshot,
    entry: ValidatedCacheEntry,
    persist: boolean,
    fallback: ValidatedCacheEntry | null,
    successStatus: "activated" | "restored" | "revalidated",
    marksRequestSuccess: boolean,
  ): Promise<GameConfigLoadResult> {
    return this.#withCommitLock(async () => {
      if (generation < this.#successfulGeneration) {
        return this.#staleResult();
      }

      if (!marksRequestSuccess && this.#active) {
        return { snapshot: this.#active, status: "unchanged" };
      }

      if (this.#active?.snapshotHash === candidate.snapshotHash) {
        const cacheAlreadyCurrent = fallback?.cached.snapshotHash === candidate.snapshotHash;
        if (persist && !cacheAlreadyCurrent) {
          try {
            await this.#options.cache.write(this.#options.cacheKey, entry.cached);
          } catch (error) {
            return this.#fallbackWithoutLock(
              new GameConfigClientError(
                "cache_write_failed",
                "The validated snapshot could not repair persisted storage.",
                { cause: error },
              ),
              fallback,
            );
          }
        }
        if (marksRequestSuccess) {
          this.#successfulGeneration = Math.max(this.#successfulGeneration, generation);
        }
        return {
          snapshot: this.#active,
          status: persist ? "unchanged" : successStatus,
        };
      }

      if (persist) {
        try {
          await this.#options.cache.write(this.#options.cacheKey, entry.cached);
        } catch (error) {
          return this.#fallbackWithoutLock(
            new GameConfigClientError(
              "cache_write_failed",
              "The validated snapshot could not be persisted atomically.",
              { cause: error },
            ),
            fallback,
          );
        }
      }

      this.#active = candidate;
      if (marksRequestSuccess) {
        this.#successfulGeneration = Math.max(this.#successfulGeneration, generation);
      }
      return {
        snapshot: candidate,
        status: successStatus,
      };
    });
  }

  async #fallback(
    generation: number,
    error: GameConfigClientError,
    cached: ValidatedCacheEntry | null,
  ): Promise<GameConfigLoadResult> {
    return this.#withCommitLock(async () => {
      if (generation < this.#successfulGeneration) {
        return this.#staleResult();
      }
      return this.#fallbackWithoutLock(error, cached);
    });
  }

  #fallbackWithoutLock(
    error: GameConfigClientError,
    cached: ValidatedCacheEntry | null,
  ): GameConfigLoadResult {
    if (this.#active) {
      return { error, snapshot: this.#active, status: "fallback" };
    }
    if (cached) {
      this.#active = cached.snapshot;
      return { error, snapshot: cached.snapshot, status: "fallback" };
    }
    return { error, snapshot: null, status: "rejected" };
  }

  #staleResult(): GameConfigLoadResult {
    return {
      error: new GameConfigClientError(
        "stale_response",
        "A later snapshot request completed successfully before this response.",
      ),
      snapshot: this.#active,
      status: "rejected",
    };
  }

  async #withCommitLock<T>(operation: () => Promise<T>): Promise<T> {
    const previous = this.#commitTail;
    let release: (() => void) | undefined;
    this.#commitTail = new Promise<void>((resolve) => {
      release = resolve;
    });
    await previous;
    try {
      return await operation();
    } finally {
      release?.();
    }
  }
}

function abortedError(): GameConfigClientError {
  return new GameConfigClientError("operation_aborted", "Game-configuration loading was aborted.");
}
