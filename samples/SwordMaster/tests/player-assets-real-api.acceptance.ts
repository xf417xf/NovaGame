import { describe, expect, it } from "vitest";

import {
  SwordMasterPlayerAssetsCocosComponent,
  SwordMasterPlayerAssetsHttpTransport,
  SwordMasterPlayerAssetsInteractionModel,
  type PlayerAssetsAttributesResult,
  type PlayerAssetsInteractionState,
} from "../src/index.js";

describe("SwordMaster PlayerAssets real API acceptance", () => {
  it("persists equipment attribute effects across fresh Cocos-compatible lifecycles", async () => {
    const apiUrl = requireEnvironment("SWORDMASTER_ACCEPTANCE_API_URL");
    const playerToken = requireEnvironment("SWORDMASTER_ACCEPTANCE_PLAYER_TOKEN");
    const characterId = requireEnvironment("SWORDMASTER_ACCEPTANCE_CHARACTER_ID");
    const ownedItemId = requireEnvironment("SWORDMASTER_ACCEPTANCE_OWNED_ITEM_ID");
    const slot = requireEnvironment("SWORDMASTER_ACCEPTANCE_EQUIPMENT_SLOT");
    const configuredItemId = requireEnvironment("SWORDMASTER_ACCEPTANCE_CONFIGURED_ITEM_ID");
    const configurationVersionId = requireEnvironment(
      "SWORDMASTER_ACCEPTANCE_CONFIGURATION_VERSION_ID",
    );

    const createComponent = (): SwordMasterPlayerAssetsCocosComponent =>
      new SwordMasterPlayerAssetsCocosComponent({
        model: new SwordMasterPlayerAssetsInteractionModel({
          characterId,
          createIdempotencyKey: () => globalThis.crypto.randomUUID(),
          transport: new SwordMasterPlayerAssetsHttpTransport({
            baseUrl: apiUrl,
            fetch: async (input, init) => await globalThis.fetch(input, init),
            getAccessToken: () => playerToken,
          }),
        }),
      });

    const firstComponent = createComponent();
    let equippedSnapshot: PlayerAssetsAttributesResult;
    try {
      const initialState = await firstComponent.start();
      expect(firstComponent.state).toBe(initialState);
      const initialSnapshot = requireReadySnapshot(initialState);
      expectSnapshot(initialSnapshot, {
        assignments: [],
        attack: [10, 0, 10],
        characterId,
        etag: '"1"',
        health: [100, 0, 100],
        revision: 1,
      });

      const equippedState = await firstComponent.equip({ ownedItemId, slot });
      expect(firstComponent.state).toBe(equippedState);
      equippedSnapshot = requireReadySnapshot(equippedState);
      expectSnapshot(equippedSnapshot, {
        assignments: [
          {
            configurationVersionId,
            configuredItemId,
            modifiers: [
              ["attack", 7],
              ["health", 20],
            ],
            ownedItemId,
            slot,
          },
        ],
        attack: [10, 7, 17],
        characterId,
        etag: '"2"',
        health: [100, 20, 120],
        revision: 2,
      });
    } finally {
      firstComponent.onDestroy();
    }

    const secondComponent = createComponent();
    try {
      const reloadedState = await secondComponent.start();
      expect(secondComponent.state).toBe(reloadedState);
      const reloadedSnapshot = requireReadySnapshot(reloadedState);
      expect(reloadedSnapshot).toEqual(equippedSnapshot);
      expectSnapshot(reloadedSnapshot, {
        assignments: [
          {
            configurationVersionId,
            configuredItemId,
            modifiers: [
              ["attack", 7],
              ["health", 20],
            ],
            ownedItemId,
            slot,
          },
        ],
        attack: [10, 7, 17],
        characterId,
        etag: '"2"',
        health: [100, 20, 120],
        revision: 2,
      });

      const unequippedState = await secondComponent.unequip({ slot });
      expect(secondComponent.state).toBe(unequippedState);
      const finalSnapshot = requireReadySnapshot(unequippedState);
      expectSnapshot(finalSnapshot, {
        assignments: [],
        attack: [10, 0, 10],
        characterId,
        etag: '"3"',
        health: [100, 0, 100],
        revision: 3,
      });
    } finally {
      secondComponent.onDestroy();
    }
  });
});

interface ExpectedSnapshot {
  readonly assignments: readonly ExpectedAssignment[];
  readonly attack: readonly [number, number, number];
  readonly characterId: string;
  readonly etag: string;
  readonly health: readonly [number, number, number];
  readonly revision: number;
}

interface ExpectedAssignment {
  readonly configurationVersionId: string;
  readonly configuredItemId: string;
  readonly modifiers: readonly (readonly [string, number])[];
  readonly ownedItemId: string;
  readonly slot: string;
}

function requireReadySnapshot(state: PlayerAssetsInteractionState): PlayerAssetsAttributesResult {
  expect(state.phase).toBe("ready");
  if (state.phase !== "ready") {
    throw new Error(`Expected a ready PlayerAssets state, received '${state.phase}'.`);
  }
  return state.snapshot;
}

function expectSnapshot(snapshot: PlayerAssetsAttributesResult, expected: ExpectedSnapshot): void {
  expect(snapshot.characterId.toLowerCase()).toBe(expected.characterId.toLowerCase());
  expect(snapshot.revision).toBe(expected.revision);
  expect(snapshot.etag).toBe(expected.etag);
  expect(
    [...snapshot.attributes]
      .sort((left, right) => left.statCode.localeCompare(right.statCode))
      .map(({ statCode, baseValue, equipmentValue, effectiveValue }) => [
        statCode,
        baseValue,
        equipmentValue,
        effectiveValue,
      ]),
  ).toEqual([
    ["attack", ...expected.attack],
    ["health", ...expected.health],
  ]);
  expect(
    [...snapshot.equipmentAssignments]
      .sort((left, right) => left.slot.localeCompare(right.slot))
      .map((assignment) => ({
        configurationVersionId: assignment.configurationVersionId.toLowerCase(),
        configuredItemId: assignment.configuredItemId,
        modifiers: [...assignment.modifiers]
          .sort((left, right) => left.statCode.localeCompare(right.statCode))
          .map(({ statCode, value }) => [statCode, value]),
        ownedItemId: assignment.ownedItemId.toLowerCase(),
        slot: assignment.slot,
      })),
  ).toEqual(
    expected.assignments.map((assignment) => ({
      ...assignment,
      configurationVersionId: assignment.configurationVersionId.toLowerCase(),
      ownedItemId: assignment.ownedItemId.toLowerCase(),
    })),
  );
}

function requireEnvironment(name: string): string {
  const value = process.env[name]?.trim();
  if (!value) {
    throw new Error(`Required PlayerAssets acceptance environment variable '${name}' is missing.`);
  }
  return value;
}
