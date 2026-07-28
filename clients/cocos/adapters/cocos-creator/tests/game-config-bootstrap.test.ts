import { describe, expect, it, vi } from "vitest";

import { GameConfigClientError } from "@novagame/game-config-client";

import { GameConfigBootstrap } from "../src/index.js";
import type { GameConfigLoadResult, GameConfigSnapshot } from "../src/index.js";

const snapshot = createSnapshot();

describe("GameConfigBootstrap", () => {
  it("restores cached state before refreshing the active environment", async () => {
    const order: string[] = [];
    const restore = restored(snapshot);
    const refresh = unchanged(snapshot);
    const bootstrap = new GameConfigBootstrap({
      async refresh() {
        order.push("refresh");
        return refresh;
      },
      async restore() {
        order.push("restore");
        return restore;
      },
    });

    await expect(bootstrap.start()).resolves.toEqual({
      refresh,
      restore,
      snapshot,
      status: "ready",
    });
    expect(order).toEqual(["restore", "refresh"]);
  });

  it("returns the restored snapshot as an explicit fallback", async () => {
    const restore = restored(snapshot);
    const refresh = rejected("transport_failed");
    const bootstrap = new GameConfigBootstrap({
      async refresh() {
        return refresh;
      },
      async restore() {
        return restore;
      },
    });

    await expect(bootstrap.start()).resolves.toEqual({
      refresh,
      restore,
      snapshot,
      status: "fallback",
    });
  });

  it("becomes ready when refresh succeeds after a cache miss", async () => {
    const restore = rejected("cache_miss");
    const refresh = activated(snapshot);
    const bootstrap = new GameConfigBootstrap({
      async refresh() {
        return refresh;
      },
      async restore() {
        return restore;
      },
    });

    await expect(bootstrap.start()).resolves.toEqual({
      refresh,
      restore,
      snapshot,
      status: "ready",
    });
  });

  it("reports unavailable when neither cache nor refresh yields a snapshot", async () => {
    const restore = rejected("cache_miss");
    const refresh = rejected("snapshot_missing");
    const bootstrap = new GameConfigBootstrap({
      async refresh() {
        return refresh;
      },
      async restore() {
        return restore;
      },
    });

    await expect(bootstrap.start()).resolves.toEqual({
      refresh,
      restore,
      snapshot: null,
      status: "unavailable",
    });
  });

  it("honors AbortSignal between restore and refresh", async () => {
    const controller = new AbortController();
    const refresh = vi.fn(async () => unchanged(snapshot));
    const bootstrap = new GameConfigBootstrap({
      refresh,
      async restore() {
        controller.abort();
        return restored(snapshot);
      },
    });

    await expect(bootstrap.start({ signal: controller.signal })).rejects.toMatchObject({
      name: "AbortError",
    });
    expect(refresh).not.toHaveBeenCalled();
  });
});

function activated(value: GameConfigSnapshot): GameConfigLoadResult {
  return { snapshot: value, status: "activated" };
}

function createSnapshot(): GameConfigSnapshot {
  return {
    configurationSet: "default",
    configurationVersion: "1",
    content: { collections: {} },
    createdAtUtc: "2026-07-26T00:00:00.000000Z",
    etag: `"${"a".repeat(64)}"`,
    formatVersion: "1",
    getCollection: () => [],
    getRecord: () => undefined,
    integrity: {
      algorithm: "SHA-256",
      canonicalization: "NovaGame.GameConfig.CanonicalJson.v1",
      contentHash: "b".repeat(64),
    },
    schemaVersion: "1",
    snapshotHash: "a".repeat(64),
  };
}

function rejected(
  code: "cache_miss" | "snapshot_missing" | "transport_failed",
): GameConfigLoadResult {
  return {
    error: new GameConfigClientError(code, code),
    snapshot: null,
    status: "rejected",
  };
}

function restored(value: GameConfigSnapshot): GameConfigLoadResult {
  return { snapshot: value, status: "restored" };
}

function unchanged(value: GameConfigSnapshot): GameConfigLoadResult {
  return { snapshot: value, status: "unchanged" };
}
