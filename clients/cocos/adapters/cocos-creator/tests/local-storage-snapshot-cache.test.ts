import { describe, expect, it } from "vitest";
import { NobleSha256 } from "@novagame/game-config-client";

import { LocalStorageSnapshotCache } from "../src/index.js";
import type { StorageLike } from "../src/index.js";

describe("LocalStorageSnapshotCache", () => {
  it("writes the inactive slot before switching the active pointer", async () => {
    const storage = new MemoryStorage();
    const cache = new LocalStorageSnapshotCache({ storage });
    const first = await candidate([1, 2, 3]);
    const second = await candidate([4, 5, 6]);

    await cache.write("development/default", first);
    storage.failNextPointerWrite = true;
    await expect(cache.write("development/default", second)).rejects.toThrow(
      "simulated pointer interruption",
    );

    await expect(cache.read("development/default")).resolves.toEqual(first);
  });

  it("falls back to the previous committed slot when the active slot is corrupt", async () => {
    const storage = new MemoryStorage();
    const cache = new LocalStorageSnapshotCache({ storage });
    const first = await candidate([1, 2, 3]);
    const second = await candidate([4, 5, 6]);

    await cache.write("development/default", first);
    await cache.write("development/default", second);
    storage.corruptActiveSlot();

    await expect(cache.read("development/default")).resolves.toEqual(first);
  });

  it("falls back when the active slot remains valid JSON but its body hash is corrupt", async () => {
    const storage = new MemoryStorage();
    const cache = new LocalStorageSnapshotCache({ storage });
    const first = await candidate([1, 2, 3]);
    const second = await candidate([4, 5, 6]);

    await cache.write("development/default", first);
    await cache.write("development/default", second);
    storage.corruptActiveBody();

    await expect(cache.read("development/default")).resolves.toEqual(first);
  });

  it("does not activate a candidate that cannot be read back exactly", async () => {
    const storage = new MemoryStorage();
    const cache = new LocalStorageSnapshotCache({ storage });
    const first = await candidate([1, 2, 3]);

    storage.corruptNextSlotWrite = true;
    await expect(cache.write("development/default", first)).rejects.toThrow(
      "could not be verified",
    );
    await expect(cache.read("development/default")).resolves.toBeNull();
  });

  it("returns null for a corrupt pointer and isolates independent cache keys", async () => {
    const storage = new MemoryStorage();
    const cache = new LocalStorageSnapshotCache({ storage });
    const first = await candidate([1]);
    const second = await candidate([2]);

    await cache.write("development/default", first);
    await cache.write("production/default", second);
    storage.corruptPointer("development/default");

    await expect(cache.read("development/default")).resolves.toBeNull();
    await expect(cache.read("production/default")).resolves.toEqual(second);
  });
});

async function candidate(bytes: number[]) {
  const body = new Uint8Array(bytes);
  return {
    body,
    snapshotHash: await new NobleSha256().digestHex(body),
  };
}

class MemoryStorage implements StorageLike {
  public corruptNextSlotWrite = false;
  public failNextPointerWrite = false;
  private readonly values = new Map<string, string>();

  public getItem(key: string): null | string {
    return this.values.get(key) ?? null;
  }

  public removeItem(key: string): void {
    this.values.delete(key);
  }

  public setItem(key: string, value: string): void {
    if (key.endsWith(".pointer") && this.failNextPointerWrite) {
      this.failNextPointerWrite = false;
      throw new Error("simulated pointer interruption");
    }
    if (key.includes(".slot-") && this.corruptNextSlotWrite) {
      this.corruptNextSlotWrite = false;
      this.values.set(key, '{"version":1,"body":"corrupt"}');
      return;
    }
    this.values.set(key, value);
  }

  public corruptActiveSlot(): void {
    const { slotKey } = this.activeSlot();
    this.values.set(slotKey, "corrupt");
  }

  public corruptActiveBody(): void {
    const { slotKey, value } = this.activeSlot();
    const stored = JSON.parse(value) as {
      body: string;
      snapshotHash: string;
      version: 1;
    };
    stored.body = stored.body === "BAUG" ? "BAUH" : "BAUG";
    this.values.set(slotKey, JSON.stringify(stored));
  }

  public corruptPointer(cacheKey: string): void {
    const encodedKey = encodeURIComponent(cacheKey);
    const pointerKey = [...this.values.keys()].find(
      (key) => key.includes(`.${encodedKey}.`) && key.endsWith(".pointer"),
    );
    if (pointerKey === undefined) {
      throw new Error("The test cache has no matching pointer.");
    }
    this.values.set(pointerKey, "corrupt");
  }

  private activeSlot(): { slotKey: string; value: string } {
    const pointerEntry = [...this.values.entries()].find(([key]) => key.endsWith(".pointer"));
    if (pointerEntry === undefined) {
      throw new Error("The test cache has no pointer.");
    }
    const pointer = JSON.parse(pointerEntry[1]) as { active: "a" | "b" };
    const slotSuffix = pointer.active === "a" ? ".slot-a" : ".slot-b";
    const slotEntry = [...this.values.entries()].find(([key]) => key.endsWith(slotSuffix));
    if (slotEntry === undefined) {
      throw new Error("The test cache has no active slot.");
    }
    return { slotKey: slotEntry[0], value: slotEntry[1] };
  }
}
