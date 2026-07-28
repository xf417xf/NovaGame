import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";

import { describe, expect, it } from "vitest";

import type { GameConfigTransport } from "@novagame/game-config-client";

import {
  GAME_CONFIG_SNAPSHOT_ASSET,
  GAME_CONFIG_SNAPSHOT_MANIFEST_ASSET,
  SwordMasterGameConfigBootstrapComponent,
  type GameConfigAsset,
  type GameConfigAssetLoader,
  type GameConfigAssetLoadOptions,
} from "../src/index.js";

const query = {
  collection: "bootstrap-records",
  id: "startup",
} as const;

describe("SwordMasterGameConfigBootstrapComponent", () => {
  it("loads, activates, and queries the committed exported snapshot", async () => {
    const component = createComponent(new FixtureAssetLoader());

    const result = await component.start();

    expect(result.lifecycle.status).toBe("ready");
    expect(result.lifecycle.snapshot?.snapshotHash).toBe(
      "fd7206b8efc3f9f4056e6c47b37f30992c11c070818a4802369f3e46d4170916",
    );
    expect(result.record).toMatchObject({
      enabled: true,
      id: "startup",
      label: "configuration-ready",
    });
  });

  it("loads through an injected public snapshot transport", async () => {
    const snapshot = await loadFixture(GAME_CONFIG_SNAPSHOT_ASSET);
    const snapshotHash = createHash("sha256").update(snapshot).digest("hex");
    const transport: GameConfigTransport = {
      fetchSnapshot: async () => ({
        body: snapshot,
        etag: `"${snapshotHash}"`,
        status: 200,
      }),
    };
    const component = new SwordMasterGameConfigBootstrapComponent({
      query,
      transport,
    });

    const result = await component.start();

    expect(result.lifecycle.refresh.status).toBe("activated");
    expect(result.lifecycle.status).toBe("ready");
    expect(result.record).toMatchObject({
      enabled: true,
      id: "startup",
      label: "configuration-ready",
    });
  });

  it("rejects the snapshot when the trusted manifest hash is wrong", async () => {
    const loader = new FixtureAssetLoader(
      new Map<string, GameConfigAsset | null>([
        [GAME_CONFIG_SNAPSHOT_MANIFEST_ASSET, JSON.stringify({ snapshotHash: "0".repeat(64) })],
      ]),
    );

    const result = await createComponent(loader).start();

    expect(result.lifecycle.status).toBe("unavailable");
    expect(result.lifecycle.refresh).toMatchObject({
      error: { code: "snapshot_hash_mismatch" },
      status: "rejected",
    });
    expect(result.record).toBeUndefined();
  });

  it("rejects an ambiguous manifest with duplicate snapshotHash fields", async () => {
    const snapshot = await loadFixture(GAME_CONFIG_SNAPSHOT_ASSET);
    const snapshotHash = createHash("sha256").update(snapshot).digest("hex");
    const loader = new FixtureAssetLoader(
      new Map<string, GameConfigAsset | null>([
        [
          GAME_CONFIG_SNAPSHOT_MANIFEST_ASSET,
          `{"snapshotHash":"${"0".repeat(64)}","snapshotHash":"${snapshotHash}"}`,
        ],
      ]),
    );

    const result = await createComponent(loader).start();

    expect(result.lifecycle.status).toBe("unavailable");
    expect(result.lifecycle.refresh).toMatchObject({
      error: { code: "invalid_contract" },
      status: "rejected",
    });
    expect(result.record).toBeUndefined();
  });

  it("rejects a correctly hashed snapshot with an unsupported schema", async () => {
    const original = await loadFixture(GAME_CONFIG_SNAPSHOT_ASSET);
    const unsupported = new TextEncoder().encode(
      new TextDecoder().decode(original).replace('"schemaVersion":"1"', '"schemaVersion":"2"'),
    );
    const snapshotHash = createHash("sha256").update(unsupported).digest("hex");
    const loader = new FixtureAssetLoader(
      new Map<string, GameConfigAsset | null>([
        [GAME_CONFIG_SNAPSHOT_ASSET, unsupported],
        [GAME_CONFIG_SNAPSHOT_MANIFEST_ASSET, JSON.stringify({ snapshotHash })],
      ]),
    );

    const result = await createComponent(loader).start();

    expect(result.lifecycle.status).toBe("unavailable");
    expect(result.lifecycle.refresh).toMatchObject({
      error: { code: "unsupported_schema" },
      status: "rejected",
    });
    expect(result.record).toBeUndefined();
  });

  it.each([GAME_CONFIG_SNAPSHOT_MANIFEST_ASSET, GAME_CONFIG_SNAPSHOT_ASSET])(
    "reports an unavailable snapshot when the bundled asset is missing: %s",
    async (missingAsset) => {
      const loader = new FixtureAssetLoader(new Map([[missingAsset, null]]));

      const result = await createComponent(loader).start();

      expect(result.lifecycle.status).toBe("unavailable");
      expect(result.lifecycle.refresh).toMatchObject({
        error: { code: "snapshot_missing" },
        status: "rejected",
      });
      expect(result.record).toBeUndefined();
    },
  );
});

class FixtureAssetLoader implements GameConfigAssetLoader {
  readonly #overrides: ReadonlyMap<string, GameConfigAsset | null>;

  public constructor(overrides: ReadonlyMap<string, GameConfigAsset | null> = new Map()) {
    this.#overrides = overrides;
  }

  public async load(
    assetPath: string,
    options: GameConfigAssetLoadOptions = {},
  ): Promise<GameConfigAsset | null> {
    options.signal?.throwIfAborted();
    if (this.#overrides.has(assetPath)) {
      return this.#overrides.get(assetPath) ?? null;
    }
    return loadFixture(assetPath);
  }
}

function createComponent(assetLoader: GameConfigAssetLoader) {
  return new SwordMasterGameConfigBootstrapComponent({
    assetLoader,
    query,
  });
}

async function loadFixture(assetPath: string): Promise<Uint8Array> {
  return Uint8Array.from(await readFile(new URL(`../${assetPath}`, import.meta.url)));
}
