import { describe, expect, it } from "vitest";

import { FetchSnapshotTransport, type FetchLike } from "@novagame/cocos-creator-game-config";

import { SwordMasterGameConfigBootstrapComponent } from "../src/index.js";

const configurationSet = "default";
const environment = "development";

describe("SwordMaster real GameConfig acceptance", () => {
  it("consumes the active snapshot published through the real operations flow", async () => {
    const baseUrl = requireEnvironment("NOVAGAME_ACCEPTANCE_API_URL");
    const component = new SwordMasterGameConfigBootstrapComponent({
      configurationSet,
      query: {
        collection: "items",
        id: "starter-item",
      },
      transport: new FetchSnapshotTransport({
        baseUrl,
        configurationSet,
        environment,
        fetch: fetchSnapshot,
      }),
    });

    const first = await component.start();

    expect(first.lifecycle.refresh.status).toBe("activated");
    expect(first.lifecycle.status).toBe("ready");
    expect(first.record).toMatchObject({
      id: "starter-item",
      name: "Starter item",
    });
    expect(first.lifecycle.snapshot?.getRecord("items", "second-item")).toBeUndefined();

    const firstSnapshot = first.lifecycle.snapshot;
    if (firstSnapshot === null) {
      throw new Error("The first real-API activation did not expose a snapshot.");
    }
    expect(firstSnapshot.etag).toBe(`"${firstSnapshot.snapshotHash}"`);

    const second = await component.start();

    expect(second.lifecycle.refresh.status).toBe("revalidated");
    expect(second.lifecycle.status).toBe("ready");
    expect(second.record).toMatchObject({
      id: "starter-item",
      name: "Starter item",
    });
    expect(second.lifecycle.snapshot).toMatchObject({
      configurationVersion: firstSnapshot.configurationVersion,
      snapshotHash: firstSnapshot.snapshotHash,
    });
    expect(second.lifecycle.snapshot?.getRecord("items", "second-item")).toBeUndefined();
  });
});

const fetchSnapshot: FetchLike = async (input, init) => {
  const request: RequestInit = {
    ...(init?.headers === undefined ? {} : { headers: { ...init.headers } }),
    ...(init?.method === undefined ? {} : { method: init.method }),
    ...(init?.signal === undefined ? {} : { signal: init.signal }),
  };
  return globalThis.fetch(input, request);
};

function requireEnvironment(name: string): string {
  const value = process.env[name]?.trim();
  if (!value) {
    throw new Error(`Required real-API acceptance environment variable '${name}' is missing.`);
  }
  return value;
}
